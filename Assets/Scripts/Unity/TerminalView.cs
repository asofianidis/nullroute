using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nullroute.Core;
using UnityEngine;

namespace Nullroute.UnityFrontend
{
    /// <summary>
    /// Full-screen retro terminal drawn with IMGUI. Owns the <see cref="Game"/>, feeds it keyboard
    /// input and time, and renders its output plus a live HUD (trace, processes, forensics).
    ///
    /// Keys: Enter run · Tab complete · Up/Down history · PgUp/PgDn/wheel scroll · Esc clear line
    ///       Ctrl+L clear screen · Ctrl+= / Ctrl+- font size
    /// </summary>
    public class TerminalView : MonoBehaviour
    {
        const int MaxLines = 4000;
        const int MaxInput = 200;

        static readonly Color Bg = new Color32(4, 10, 6, 255);
        static readonly Color HudBg = new Color32(9, 26, 14, 255);
        static readonly Color Rule = new Color32(31, 154, 76, 255);
        static readonly Color TextGreen = new Color32(57, 255, 122, 255);
        static readonly Color InputWhite = new Color32(230, 255, 233, 255);

        Game game;
        readonly List<string> lines = new List<string>();
        readonly Queue<string> pending = new Queue<string>();
        readonly List<string> history = new List<string>();
        string input = "";
        int historyPos;
        int scroll;
        float reveal;

        int fontSize;
        Font font;
        GUIStyle text, hud, plain;
        Texture2D pixel, scan;

        bool autotest;
        string autotestDir;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-autotest") { autotest = true; autotestDir = args[i + 1]; }

            var store = autotest ? new UnitySaveStore(Path.Combine(autotestDir, "save.json")) : new UnitySaveStore();
            if (autotest) { Directory.CreateDirectory(autotestDir); store.Delete(); }

            game = new Game(store, System.Environment.TickCount);
            fontSize = Mathf.Clamp(Screen.height / 44, 13, 30);
        }

        void Start()
        {
            game.Start();
            if (autotest) StartCoroutine(Autotest());
        }

        void Update()
        {
            game.Tick(Time.unscaledDeltaTime);
            while (game.Outbox.Count > 0) pending.Enqueue(game.Outbox.Dequeue());

            // Stream output in line by line for that old-terminal feel; dump instantly if it piles up.
            if (pending.Count > 0)
            {
                reveal += Time.unscaledDeltaTime * (pending.Count > 80 ? 5000f : 140f);
                while (reveal >= 1f && pending.Count > 0) { reveal -= 1f; Emit(pending.Dequeue()); }
            }
            else reveal = 0;

            if (game.QuitRequested && pending.Count == 0)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        void OnApplicationQuit()
        {
            if (!autotest) game.Shutdown();
        }

        void Emit(string line)
        {
            if (line == Game.ClearSignal) { lines.Clear(); scroll = 0; return; }
            lines.Add(line);
            if (scroll > 0) scroll++;
            if (lines.Count > MaxLines) lines.RemoveRange(0, lines.Count - MaxLines);
        }

        void FlushPending()
        {
            while (pending.Count > 0) Emit(pending.Dequeue());
        }

        void Submit()
        {
            FlushPending();
            string cmd = input;
            input = "";
            scroll = 0;
            if (cmd.Trim().Length > 0 && (history.Count == 0 || history[history.Count - 1] != cmd)) history.Add(cmd);
            historyPos = history.Count;
            game.Exec(cmd);
        }

        // ------------------------------------------------------------------ input

        void HandleInput(Event e)
        {
            if (e.type == EventType.ScrollWheel)
            {
                scroll = Mathf.Clamp(scroll + (e.delta.y < 0 ? 3 : -3), 0, Mathf.Max(0, lines.Count - 1));
                e.Use();
                return;
            }
            if (e.type != EventType.KeyDown) return;

            bool ctrl = e.control || e.command;
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit(); e.Use(); return;
                case KeyCode.Backspace:
                    if (input.Length > 0)
                    {
                        if (ctrl)
                        {
                            string t = input.TrimEnd();
                            int cut = t.LastIndexOf(' ');
                            input = cut < 0 ? "" : t.Substring(0, cut + 1);
                        }
                        else input = input.Substring(0, input.Length - 1);
                    }
                    e.Use(); return;
                case KeyCode.UpArrow:
                    if (history.Count > 0) { historyPos = Mathf.Max(0, historyPos - 1); input = history[historyPos]; }
                    e.Use(); return;
                case KeyCode.DownArrow:
                    if (historyPos < history.Count - 1) { historyPos++; input = history[historyPos]; }
                    else { historyPos = history.Count; input = ""; }
                    e.Use(); return;
                case KeyCode.Tab:
                    FlushPending();
                    input = game.Complete(input);
                    e.Use(); return;
                case KeyCode.PageUp:
                    scroll = Mathf.Min(Mathf.Max(0, lines.Count - 1), scroll + PageLines());
                    e.Use(); return;
                case KeyCode.PageDown:
                    scroll = Mathf.Max(0, scroll - PageLines());
                    e.Use(); return;
                case KeyCode.Escape:
                    input = ""; e.Use(); return;
            }

            if (ctrl)
            {
                if (e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.Plus || e.keyCode == KeyCode.KeypadPlus) { fontSize = Mathf.Min(48, fontSize + 1); e.Use(); }
                else if (e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus) { fontSize = Mathf.Max(10, fontSize - 1); e.Use(); }
                else if (e.keyCode == KeyCode.L) { lines.Clear(); scroll = 0; e.Use(); }
                return;
            }

            char c = e.character;
            if (c != '\0' && !char.IsControl(c) && input.Length < MaxInput)
            {
                input += c;
                e.Use();
            }
        }

        int PageLines()
        {
            float lh = text != null ? text.lineHeight : 18f;
            return Mathf.Max(5, (int)(Screen.height / lh) - 8);
        }

        // ------------------------------------------------------------------ drawing

        void EnsureStyles()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }, fontSize);

            if (text == null || text.fontSize != fontSize)
            {
                text = new GUIStyle();
                text.font = font;
                text.fontSize = fontSize;
                text.richText = true;
                text.wordWrap = true;
                text.alignment = TextAnchor.UpperLeft;
                text.clipping = TextClipping.Clip;
                text.normal.textColor = TextGreen;

                hud = new GUIStyle(text);
                hud.wordWrap = false;

                plain = new GUIStyle(text);
                plain.richText = false;
                plain.wordWrap = false;
                plain.normal.textColor = InputWhite;
            }

            if (pixel == null)
            {
                pixel = new Texture2D(1, 1);
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }

            if (scan == null)
            {
                // Faint CRT scanlines: one dark row every three pixels.
                scan = new Texture2D(1, 3);
                scan.wrapMode = TextureWrapMode.Repeat;
                scan.filterMode = FilterMode.Point;
                scan.SetPixel(0, 0, new Color(0, 0, 0, 0.22f));
                scan.SetPixel(0, 1, new Color(0, 0, 0, 0));
                scan.SetPixel(0, 2, new Color(0, 0, 0, 0));
                scan.Apply();
            }
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, pixel);
            GUI.color = old;
        }

        void OnGUI()
        {
            EnsureStyles();
            HandleInput(Event.current);
            if (Event.current.type != EventType.Repaint) return;

            float W = Screen.width, H = Screen.height;
            const float pad = 14f;
            float lh = text.lineHeight;

            Fill(new Rect(0, 0, W, H), Bg);

            // HUD: status, link/trace, forensics, processes.
            var status = game.StatusLines();
            float hudH = status.Count * lh + 12;
            Fill(new Rect(0, 0, W, hudH), HudBg);
            Fill(new Rect(0, hudH, W, 1), Rule);
            for (int i = 0; i < status.Count; i++)
                GUI.Label(new Rect(pad, 6 + i * lh, W - 2 * pad, lh + 2), status[i], hud);

            // Input line.
            float inputY = H - pad - lh;
            Fill(new Rect(0, inputY - 8, W, 1), new Color(Rule.r, Rule.g, Rule.b, 0.5f));
            string prompt = game.Prompt;
            float pw = hud.CalcSize(new GUIContent(prompt)).x;
            GUI.Label(new Rect(pad, inputY, pw + 4, lh + 2), prompt, hud);
            bool blink = (Time.unscaledTime % 1f) < 0.55f;
            GUI.Label(new Rect(pad + pw, inputY, W - pad * 2 - pw, lh + 2), input + (blink ? "█" : ""), plain);

            // Scrollback, drawn bottom-up so word-wrapped lines stack correctly.
            float top = hudH + 8, bottom = inputY - 14, width = W - 2 * pad;
            GUI.BeginGroup(new Rect(pad, top, width, bottom - top));
            float y = bottom - top;
            int idx = lines.Count - 1 - scroll;
            while (idx >= 0 && y > 0)
            {
                var content = new GUIContent(lines[idx]);
                float h = lines[idx].Length == 0 ? lh : Mathf.Max(lh, text.CalcHeight(content, width));
                y -= h;
                GUI.Label(new Rect(0, y, width, h), content, text);
                idx--;
            }
            GUI.EndGroup();

            if (scroll > 0)
            {
                string note = " -- scrolled back " + scroll + " lines (PgDn) -- ";
                float nw = hud.CalcSize(new GUIContent(note)).x;
                Fill(new Rect(W - nw - pad, bottom - lh, nw, lh), HudBg);
                GUI.Label(new Rect(W - nw - pad, bottom - lh, nw, lh), note, hud);
            }

            GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), scan, new Rect(0, 0, 1, H / 3f));
        }

        // ------------------------------------------------------------------ autotest (screenshots for CI / dev)

        void Type(string s)
        {
            input = s;
            Submit();
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            FlushPending();
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(autotestDir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.3f);
        }

        IEnumerator WaitProcs(float max = 30f)
        {
            float t = 0;
            while (game.Procs.Count > 0 && t < max) { t += Time.unscaledDeltaTime; yield return null; }
        }

        IEnumerator Autotest()
        {
            yield return new WaitForSecondsRealtime(1.0f);
            Type("ghost");
            yield return Shot("01_new_career");
            Type("help");
            yield return Shot("02_help");
            Type("clear");
            Type("jobs");
            yield return Shot("03_jobs");

            var training = game.Node(game.D.trainingNodeId);
            Type("accept " + game.D.tutorialMissionId);
            Type("connect " + training.ip);
            Type("probe");
            Type("crack");
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("04_cracking");
            yield return WaitProcs();
            Type("ls");
            Type("download training_key.dat");
            yield return WaitProcs();
            Type("logwipe");
            yield return WaitProcs();
            Type("dc");
            Type("submit " + game.D.tutorialMissionId);
            Type("clear");
            Type("shop");
            yield return Shot("05_shop");
            Type("buy fwbypass");
            Type("clear");

            var job = game.D.board.Where(m => !m.tutorial).OrderBy(m => m.tier).First();
            Type("accept " + job.id);
            Type("bounce auto");
            Type("connect " + game.Node(job.nodeId).ip);
            Type("probe");
            Type(game.Node(job.nodeId).firewall > 0 ? "fwbypass" : "crack");
            yield return new WaitForSecondsRealtime(6f);
            yield return Shot("06_trace");
            Type("dc");
            Type("rig");
            yield return Shot("07_rig");

            Application.Quit();
        }
    }
}
