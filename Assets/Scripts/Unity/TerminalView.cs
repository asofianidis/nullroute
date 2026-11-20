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
    /// input and time, and renders its output next to a live dashboard: a top bar (credits, rank),
    /// a sidebar (link, security layers, trace, forensics, processes, contracts, rig), pop-up toasts
    /// for game events, and screen-edge alerts while you're being traced.
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
        GUIStyle text, hud, plain;  // more styles further down
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

            UpdateFeedback();

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

        // ------------------------------------------------------------------ feedback state

        class Toast
        {
            public NoticeKind kind;
            public string title, text;
            public float born, life;
        }

        class Floater
        {
            public string text;
            public Color color;
            public float born;
        }

        class DoneFx
        {
            public string label;
            public float born;
        }

        readonly List<Toast> toasts = new List<Toast>();
        readonly List<Floater> floaters = new List<Floater>();
        readonly List<DoneFx> doneFx = new List<DoneFx>();
        readonly Dictionary<string, double> invSpan = new Dictionary<string, double>();
        float dangerFlash, goodFlash, errorFlash, rankFlash;
        int seenErrors;
        SaveData seenSave;
        long seenCredits;
        double shownCredits;

        /// <summary>Turns core events into toasts, flashes and floating numbers. Called every frame.</summary>
        void UpdateFeedback()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;

            while (game.Notices.Count > 0)
            {
                var n = game.Notices.Dequeue();
                float life = n.kind == NoticeKind.Danger ? 6f : n.kind == NoticeKind.Rank ? 7f : 4.5f;
                toasts.Add(new Toast { kind = n.kind, title = n.title, text = n.text, born = now, life = life });
                if (n.kind == NoticeKind.Danger) dangerFlash = 1f;
                if (n.kind == NoticeKind.Good || n.kind == NoticeKind.Money) goodFlash = 1f;
                if (n.kind == NoticeKind.Rank) rankFlash = 1f;
            }
            while (toasts.Count > 6) toasts.RemoveAt(0);
            toasts.RemoveAll(t => now - t.born > t.life);

            while (game.Finished.Count > 0)
            {
                var p = game.Finished.Dequeue();
                doneFx.Add(new DoneFx { label = p.label, born = now });
            }
            doneFx.RemoveAll(d => now - d.born > 1.8f);

            if (game.ErrorCount != seenErrors) { seenErrors = game.ErrorCount; errorFlash = 1f; }

            // Credits: animate the counter and float the change above it.
            if (game.D != seenSave)
            {
                seenSave = game.D;
                seenCredits = game.D != null ? game.P.credits : 0;
                shownCredits = seenCredits;
                invSpan.Clear();
            }
            else if (game.D != null && game.P.credits != seenCredits)
            {
                long delta = game.P.credits - seenCredits;
                seenCredits = game.P.credits;
                floaters.Add(new Floater
                {
                    text = (delta > 0 ? "+" : "-") + Tx.Money(System.Math.Abs(delta)),
                    color = delta > 0 ? Hex(Tx.Green) : Hex(Tx.Red),
                    born = now,
                });
            }
            if (game.D != null)
            {
                double target = game.P.credits;
                shownCredits += (target - shownCredits) * Mathf.Clamp01(dt * 6f);
                if (System.Math.Abs(target - shownCredits) < 1) shownCredits = target;
            }
            floaters.RemoveAll(f => now - f.born > 2f);

            dangerFlash = Mathf.Max(0, dangerFlash - dt * 1.2f);
            goodFlash = Mathf.Max(0, goodFlash - dt * 2f);
            errorFlash = Mathf.Max(0, errorFlash - dt * 3f);
            rankFlash = Mathf.Max(0, rankFlash - dt * 0.5f);
        }

        // ------------------------------------------------------------------ drawing

        static readonly Color PanelBg = new Color32(7, 20, 11, 245);
        static readonly Color PanelHead = new Color32(12, 36, 19, 255);
        static readonly Color Muted = new Color32(23, 70, 38, 255);

        GUIStyle small, smallR, smallC, title, titleC, big, bar;
        Texture2D vignette;
        int styledSize = -1;

        static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.white;
        }

        static string HexOf(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        static float Pulse(float speed) { return 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed); }

        static Color KindColor(NoticeKind k)
        {
            switch (k)
            {
                case NoticeKind.Good: return Hex(Tx.Green);
                case NoticeKind.Warn: return Hex(Tx.Yellow);
                case NoticeKind.Danger: return Hex(Tx.Red);
                case NoticeKind.Money: return Hex(Tx.Yellow);
                case NoticeKind.Rank: return Hex(Tx.Magenta);
                default: return Hex(Tx.Cyan);
            }
        }

        static Color TraceColor(double t)
        {
            if (t < 0.5) return Color.Lerp(Hex(Tx.Yellow), Hex(Tx.Orange), (float)(t / 0.5));
            return Color.Lerp(Hex(Tx.Orange), Hex(Tx.Red), Mathf.Clamp01((float)((t - 0.5) / 0.3)));
        }

        void EnsureStyles()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Consolas", "Cascadia Mono", "Lucida Console", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }, fontSize);

            if (styledSize != fontSize)
            {
                styledSize = fontSize;
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

                small = new GUIStyle(hud);
                small.fontSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * 0.86f));
                small.alignment = TextAnchor.MiddleLeft;

                smallR = new GUIStyle(small);
                smallR.alignment = TextAnchor.MiddleRight;

                smallC = new GUIStyle(small);
                smallC.alignment = TextAnchor.MiddleCenter;

                title = new GUIStyle(small);
                title.fontStyle = FontStyle.Bold;

                titleC = new GUIStyle(title);
                titleC.alignment = TextAnchor.MiddleCenter;

                big = new GUIStyle(hud);
                big.fontSize = Mathf.RoundToInt(fontSize * 1.25f);
                big.fontStyle = FontStyle.Bold;
                big.alignment = TextAnchor.MiddleLeft;

                bar = new GUIStyle(hud);
                bar.alignment = TextAnchor.MiddleLeft;
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

            if (vignette == null)
            {
                // White at the screen edges, transparent in the middle; tinted when drawn.
                const int S = 64;
                vignette = new Texture2D(S, S);
                vignette.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                        float d = Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v));
                        float a = Mathf.Clamp01(1 - d / 0.2f);
                        vignette.SetPixel(x, y, new Color(1, 1, 1, a * a));
                    }
                vignette.Apply();
            }
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, pixel);
            GUI.color = old;
        }

        void Frame(Rect r, Color c, float t = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>A horizontal meter: track, fill and a bright leading edge.</summary>
        void Meter(Rect r, double frac, Color c, bool glow = false)
        {
            float f = Mathf.Clamp01((float)frac);
            Fill(r, A(c, 0.14f));
            var fr = new Rect(r.x, r.y, r.width * f, r.height);
            Fill(fr, A(c, glow ? 0.65f + 0.35f * Pulse(10f) : 0.85f));
            if (f > 0 && f < 1) Fill(new Rect(fr.xMax - 2, r.y, 2, r.height), Color.white);
            // Segment ticks so it still reads as a terminal gauge.
            for (float x = r.x + 6; x < r.xMax; x += 6) Fill(new Rect(x, r.y, 1, r.height), A(Bg, 0.5f));
        }

        float TextW(string s, GUIStyle st) { return st.CalcSize(new GUIContent(s)).x; }

        /// <summary>Draws a label at (x, y) and returns the x just past it.</summary>
        float Put(float x, float y, float h, string s, GUIStyle st)
        {
            float w = TextW(s, st);
            GUI.Label(new Rect(x, y, w + 2, h), s, st);
            return x + w;
        }

        void OnGUI()
        {
            EnsureStyles();
            HandleInput(Event.current);
            if (Event.current.type != EventType.Repaint) return;

            float W = Screen.width, H = Screen.height;
            Fill(new Rect(0, 0, W, H), Bg);

            float topH = Mathf.Round(text.lineHeight * 1.9f);
            DrawTopBar(new Rect(0, 0, W, topH));

            bool side = game.D != null && game.Mode == Mode.Playing && W >= 720;
            float sideW = side ? Mathf.Round(Mathf.Clamp(W * 0.31f, 300f, 540f)) : 0f;
            var term = new Rect(0, topH, W - sideW, H - topH);
            DrawTerminal(term);
            if (side) DrawSidebar(new Rect(W - sideW, topH, sideW, H - topH));
            DrawToasts(term);
            DrawAlerts(W, H);

            GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), scan, new Rect(0, 0, 1, H / 3f));
        }

        // ---- top bar -------------------------------------------------------------

        void DrawTopBar(Rect r)
        {
            Fill(r, HudBg);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), Rule);
            float pad = 14, h = r.height, x = r.x + pad;

            if (game.Mode == Mode.GameOver)
            {
                Fill(r, A(Hex(Tx.Red), 0.25f + 0.15f * Pulse(4f)));
                x = Put(x, r.y, h, Tx.G("NULLROUTE"), big) + 24;
                Put(x, r.y, h, Tx.R("CAREER TERMINATED") + Tx.D("   ::   ") + Tx.W("press ENTER to start over"), big);
                return;
            }
            if (game.D == null || game.Mode != Mode.Playing)
            {
                x = Put(x, r.y, h, Tx.G("NULLROUTE"), big) + 24;
                Put(x, r.y, h, Tx.D("no career loaded  ::  choose a handle to begin"), bar);
                return;
            }

            var P = game.P;
            string sep = Tx.D("   |   ");
            x = Put(x, r.y, h, Tx.G("NULLROUTE"), big);
            x = Put(x, r.y, h, sep + Tx.W(P.handle) + sep, bar);

            // Credits, with a flash and a floating delta whenever they change.
            float cx = x;
            if (goodFlash > 0) Fill(new Rect(cx - 6, r.y + 4, TextW(Tx.Money((long)shownCredits), big) + 12, h - 8), A(Hex(Tx.Yellow), 0.25f * goodFlash));
            x = Put(x, r.y, h, Tx.Y(Tx.Money((long)System.Math.Round(shownCredits))), big);
            float now = Time.unscaledTime;
            foreach (var f in floaters)
            {
                float age = (now - f.born) / 2f;
                var col = A(f.color, 1 - age);
                Put(cx + 4, r.y + h * 0.55f - age * h * 0.6f, h * 0.6f, "<color=" + HexOf(col) + ">" + f.text + "</color>", title);
            }
            x = Put(x, r.y, h, sep, bar);

            // Rank and a graphical reputation bar.
            int rank = game.Rank;
            int lo = Balance.RankThreshold(rank), hi = Balance.RankThreshold(rank + 1);
            double frac = hi > lo ? (double)(P.rep - lo) / (hi - lo) : 0;
            string rankText = Tx.Cy(Balance.RankTitle(rank));
            if (rankFlash > 0) Fill(new Rect(x - 6, r.y + 4, TextW(rankText, bar) + 12, h - 8), A(Hex(Tx.Magenta), 0.35f * rankFlash));
            x = Put(x, r.y, h, rankText, bar) + 10;
            float bw = Mathf.Clamp(r.width * 0.09f, 60f, 140f);
            Meter(new Rect(x, r.y + h * 0.5f - 4, bw, 8), frac, Hex(Tx.Cyan));
            x += bw + 8;
            x = Put(x, r.y, h, Tx.D(P.rep + "/" + hi + " rep"), small);

            // Right side: contracts held and session clock.
            string right = Tx.D("contracts ") + Tx.W(game.D.active.Count + "/" + Balance.MaxActiveJobs) + sep + Tx.D("uptime ") + Tx.W(Tx.Clock(game.D.time));
            float rw = TextW(right, bar);
            if (r.xMax - pad - rw > x + 20) GUI.Label(new Rect(r.xMax - pad - rw, r.y, rw + 2, h), right, bar);
        }

        // ---- terminal ------------------------------------------------------------

        void DrawTerminal(Rect r)
        {
            const float pad = 14f;
            float lh = text.lineHeight;

            // Input line, in its own strip so it's always easy to find. Flashes red on errors.
            float stripH = lh + 16;
            var strip = new Rect(r.x, r.yMax - stripH, r.width, stripH);
            Fill(strip, HudBg);
            Fill(new Rect(strip.x, strip.y, strip.width, 1), A(Rule, 0.6f));
            if (errorFlash > 0) Fill(strip, A(Hex(Tx.Red), 0.35f * errorFlash));
            float shake = errorFlash > 0 ? Mathf.Sin(Time.unscaledTime * 70f) * 5f * errorFlash : 0f;

            float inputY = strip.y + 8;
            string prompt = game.Prompt;
            float pw = TextW(prompt, hud);
            GUI.Label(new Rect(r.x + pad + shake, inputY, pw + 4, lh + 2), prompt, hud);
            bool blink = (Time.unscaledTime % 1f) < 0.55f;
            GUI.Label(new Rect(r.x + pad + pw + shake, inputY, r.width - pad * 2 - pw, lh + 2), input + (blink ? "█" : ""), plain);

            // Scrollback, drawn bottom-up so word-wrapped lines stack correctly.
            float top = r.y + 8, bottom = strip.y - 6, width = r.width - 2 * pad;
            GUI.BeginGroup(new Rect(r.x + pad, top, width, bottom - top));
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
                float nw = TextW(note, hud);
                Fill(new Rect(r.xMax - nw - pad, bottom - lh, nw, lh), HudBg);
                GUI.Label(new Rect(r.xMax - nw - pad, bottom - lh, nw, lh), note, hud);
            }
        }

        // ---- sidebar -------------------------------------------------------------

        float sh; // sidebar line height

        void DrawSidebar(Rect r)
        {
            Fill(r, new Color32(5, 14, 8, 255));
            Fill(new Rect(r.x, r.y, 1, r.height), Rule);
            sh = Mathf.Round(small.lineHeight + 4);

            GUI.BeginGroup(r);
            float x = 10, w = r.width - 20, y = 10;
            y = PanelLink(x, y, w) + 10;
            if (game.Conn != null) y = PanelTrace(x, y, w) + 10;
            if (game.D.investigations.Count > 0) y = PanelForensics(x, y, w) + 10;
            y = PanelProcs(x, y, w) + 10;
            y = PanelContracts(x, y, w) + 10;
            if (game.Tutorial != null && game.Tutorial.Active) y = PanelTrainer(x, y, w) + 10;
            PanelRig(x, y, w);
            GUI.EndGroup();
        }

        /// <summary>Draws a panel frame with a title strip; returns the y where content starts.</summary>
        float PanelFrame(float x, float y, float w, float contentH, string name, Color accent, string right = null)
        {
            var box = new Rect(x, y, w, sh + contentH + 8);
            Fill(box, PanelBg);
            Fill(new Rect(x, y, w, sh), PanelHead);
            Frame(box, A(accent, 0.45f));
            Fill(new Rect(x, y, 3, box.height), accent);
            GUI.Label(new Rect(x + 10, y, w - 20, sh), "<color=" + (accent == Muted ? Tx.Dim : HexOf(accent)) + ">" + name + "</color>", title);
            if (right != null) GUI.Label(new Rect(x + 10, y, w - 20, sh), right, smallR);
            return y + sh + 4;
        }

        float PanelLink(float x, float y, float w)
        {
            var n = game.Conn;
            var P = game.P;
            if (n == null)
            {
                int hops = P.bounces.Count;
                float cy = PanelFrame(x, y, w, sh * 2, "LINK", Muted, Tx.D("offline"));
                GUI.Label(new Rect(x + 12, cy, w - 24, sh), Tx.D("Not connected.  ") + Tx.W("connect <ip>"), small);
                string route = hops > 0
                    ? Tx.Cy(hops + " bounce hop" + (hops == 1 ? "" : "s")) + Tx.D(" ready  (traces x" + Tx.F2(Balance.BounceMult(hops)) + " slower)")
                    : Tx.O("No bounces - direct link. ") + Tx.D("try ") + Tx.W("bounce auto");
                GUI.Label(new Rect(x + 12, cy + sh, w - 24, sh), route, small);
                return cy + sh * 2 + 4;
            }

            // Route chain: gateway -> hops -> target, with a packet running along it.
            var chain = new List<string>();
            chain.Add(Tx.Cy("gateway ") + Tx.D(P.gatewayIp));
            foreach (var id in game.Route)
            {
                var h = game.Node(id);
                if (h != null) chain.Add(Tx.Y(h.ip) + Tx.D("  " + h.name));
            }
            bool access = game.HasAccess(n);
            string tag = n.isPublic ? Tx.D("PUBLIC") : access ? Tx.G("ACCESS") : Tx.R("LOCKED");
            chain.Add(Tx.W(n.name) + "  " + tag);

            bool layers = !n.isPublic;
            float chipH = sh * 2 + 6;
            float contentH = chain.Count * sh + 4 + (layers ? chipH + 8 : 0);
            Color accent = access ? Hex(Tx.Green) : Hex(Tx.Yellow);
            float y0 = PanelFrame(x, y, w, contentH, "LINK", accent, Tx.Y(n.ip));

            float lineX = x + 18;
            float firstY = y0 + sh * 0.5f, lastY = y0 + (chain.Count - 1) * sh + sh * 0.5f;
            Fill(new Rect(lineX - 1, firstY, 2, lastY - firstY), A(Rule, 0.8f));
            if (chain.Count > 1)
            {
                float t = (Time.unscaledTime * 0.8f) % 1f;
                Fill(new Rect(lineX - 3, Mathf.Lerp(firstY, lastY, t) - 3, 6, 6), Hex(Tx.Cyan));
            }
            for (int i = 0; i < chain.Count; i++)
            {
                float cy = y0 + i * sh;
                bool last = i == chain.Count - 1;
                Color dot = i == 0 ? Hex(Tx.Cyan) : last ? (access ? Hex(Tx.Green) : Hex(Tx.Red)) : Hex(Tx.Yellow);
                float ds = last ? 10 : 7;
                Fill(new Rect(lineX - ds / 2, cy + sh / 2 - ds / 2, ds, ds), dot);
                GUI.Label(new Rect(lineX + 14, cy, w - 40, sh), chain[i], small);
            }

            if (layers)
            {
                float cy = y0 + chain.Count * sh + 8;
                float gap = 6, cw = (w - 20 - gap * 3) / 4f;
                Chip(new Rect(x + 10, cy, cw, chipH), "PROXY", n.proxy, n.proxyDown, ProcKind.PxBypass, n);
                Chip(new Rect(x + 10 + (cw + gap), cy, cw, chipH), "FIREWALL", n.firewall, n.firewallDown, ProcKind.FwBypass, n);
                Chip(new Rect(x + 10 + (cw + gap) * 2, cy, cw, chipH), "MONITOR", n.monitor, n.monitorDown, ProcKind.MonBypass, n);
                Chip(new Rect(x + 10 + (cw + gap) * 3, cy, cw, chipH), "PASSWORD", n.password, n.passwordKnown, ProcKind.Crack, n);
            }
            return y0 + contentH + 4;
        }

        /// <summary>One security layer: grey if absent, red if up, green if beaten, with a progress fill while attacked.</summary>
        void Chip(Rect r, string name, int level, bool down, ProcKind attack, NodeData n)
        {
            Proc running = game.Procs.FirstOrDefault(p => p.kind == attack && p.nodeId == n.id);
            Color c; string state;
            if (level <= 0) { c = Muted; state = Tx.D("none"); }
            else if (down) { c = Hex(Tx.Green); state = Tx.G(name == "PASSWORD" ? "CRACKED" : "DOWN"); }
            else if (running != null) { c = Hex(Tx.Cyan); state = Tx.Cy(Tx.Pct(running.Progress)); }
            else { c = Hex(Tx.Red); state = Tx.R("L" + level + (name == "PASSWORD" ? " LOCK" : " UP")); }

            Fill(r, A(c, level <= 0 ? 0.05f : down ? 0.22f : 0.12f));
            if (running != null) Fill(new Rect(r.x, r.y, r.width * (float)running.Progress, r.height), A(c, 0.25f + 0.1f * Pulse(8f)));
            Frame(r, A(c, level > 0 && !down && running == null ? 0.5f + 0.4f * Pulse(3f) : 0.8f));
            string label = name.Length > 0 && r.width < TextW(name, title) + 8 ? name.Substring(0, 4) : name;
            GUI.Label(new Rect(r.x, r.y + 3, r.width, sh), "<color=" + HexOf(level <= 0 ? A(TextGreen, 0.4f) : c) + ">" + label + "</color>", titleC);
            GUI.Label(new Rect(r.x, r.y + sh + 1, r.width, sh), state, smallC);
        }

        float PanelTrace(float x, float y, float w)
        {
            var n = game.Conn;
            float barH = Mathf.Round(sh * 1.1f);
            float contentH = barH + sh + 8;

            if (n.traceTime <= 0 || n.isPublic)
            {
                float cy = PanelFrame(x, y, w, sh, "TRACE", Muted, Tx.D("n/a"));
                GUI.Label(new Rect(x + 12, cy, w - 24, sh), Tx.D(n.isPublic ? "Public relay - nobody is watching." : "Training system - no trace."), small);
                return cy + sh + 4;
            }
            if (!game.TraceActive)
            {
                double full = game.TraceDuration(n, game.Route.Count);
                float cy = PanelFrame(x, y, w, sh * 2, "TRACE", Hex(Tx.Green), Tx.G("idle"));
                GUI.Label(new Rect(x + 12, cy, w - 24, sh), Tx.G("Not traced yet."), small);
                GUI.Label(new Rect(x + 12, cy + sh, w - 24, sh), Tx.D("Any attack starts a trace of ~") + Tx.Y(Tx.Clock(full)), small);
                return cy + sh * 2 + 4;
            }

            double t = game.Trace;
            Color c = TraceColor(t);
            double left = (1 - t) * game.TraceDuration(n, game.Route.Count);
            float y0 = PanelFrame(x, y, w, contentH, t >= 0.75 ? "TRACE  !! DISCONNECT !!" : "TRACE", c, "<color=" + HexOf(c) + ">~" + Tx.Clock(left) + " left</color>");
            var bar = new Rect(x + 12, y0 + 2, w - 24, barH);
            Meter(bar, t, c, t >= 0.75);
            GUI.Label(bar, "<color=#ffffff>" + Tx.Pct(t) + "</color>", titleC);
            string hint = t >= 0.9 ? Tx.R("Type dc NOW.") : t >= 0.75 ? Tx.O("Wipe logs and get out.") : Tx.D("Finish the job, ") + Tx.W("logwipe") + Tx.D(", then ") + Tx.W("dc") + Tx.D(".");
            GUI.Label(new Rect(x + 12, y0 + barH + 6, w - 24, sh), hint, small);
            return y0 + contentH + 4;
        }

        float PanelForensics(float x, float y, float w)
        {
            var invs = game.D.investigations;
            float rowH = sh + 10;
            Color red = Hex(Tx.Red);
            float y0 = PanelFrame(x, y, w, invs.Count * rowH, "FORENSICS", red, Tx.R(invs.Count + " open"));
            var live = new HashSet<string>();
            for (int i = 0; i < invs.Count; i++)
            {
                var inv = invs[i];
                live.Add(inv.nodeId);
                var node = game.Node(inv.nodeId);
                double remaining = inv.ends - game.D.time;
                double span;
                if (!invSpan.TryGetValue(inv.nodeId, out span) || remaining > span) invSpan[inv.nodeId] = span = remaining;
                float ry = y0 + i * rowH;
                bool soon = remaining < 30;
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), Tx.W(node != null ? node.name : "?"), small);
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), (soon ? Tx.R(Tx.Clock(remaining)) : Tx.Y(Tx.Clock(remaining))) + Tx.D("  risk ") + Tx.R(Tx.Pct(inv.chance)), smallR);
                Meter(new Rect(x + 12, ry + sh, w - 24, 4), span > 0 ? remaining / span : 0, soon ? A(red, 0.6f + 0.4f * Pulse(8f)) : Hex(Tx.Orange));
            }
            foreach (var k in invSpan.Keys.Where(k => !live.Contains(k)).ToList()) invSpan.Remove(k);
            return y0 + invs.Count * rowH + 4;
        }

        float PanelProcs(float x, float y, float w)
        {
            int slots = Balance.RamSlots(game.P.ram);
            var procs = game.Procs;
            float rowH = sh + 10;
            int rows = procs.Count + doneFx.Count;
            float contentH = Mathf.Max(1, rows) * rowH;
            Color accent = procs.Count > 0 ? Hex(Tx.Cyan) : Muted;
            float y0 = PanelFrame(x, y, w, contentH, "PROCESSES", accent);

            // RAM slots as little blocks in the title strip.
            float bs = sh * 0.45f, bx = x + w - 12 - slots * (bs + 3);
            GUI.Label(new Rect(bx - 60, y, 56, sh), Tx.D("ram"), smallR);
            for (int i = 0; i < slots; i++)
            {
                var b = new Rect(bx + i * (bs + 3), y + (sh - bs) / 2, bs, bs);
                if (i < procs.Count) Fill(b, Hex(Tx.Cyan)); else Frame(b, A(Hex(Tx.Cyan), 0.5f));
            }

            if (rows == 0)
            {
                GUI.Label(new Rect(x + 12, y0, w - 24, sh), Tx.D("idle - tools you run show up here"), small);
                return y0 + rowH + 4;
            }
            float ry = y0;
            foreach (var p in procs)
            {
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), Tx.D("[" + p.pid + "] ") + Tx.Cy(p.label), small);
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), Tx.W(Tx.Pct(p.Progress)) + Tx.D("  " + Tx.Clock(p.Remaining)), smallR);
                Meter(new Rect(x + 12, ry + sh, w - 24, 5), p.Progress, Hex(Tx.Cyan));
                ry += rowH;
            }
            float now = Time.unscaledTime;
            foreach (var d in doneFx)
            {
                float a = 1 - (now - d.born) / 1.8f;
                Color g = Hex(Tx.Green);
                Fill(new Rect(x + 8, ry - 2, w - 16, rowH - 2), A(g, 0.18f * a));
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), "<color=" + HexOf(A(g, a)) + ">" + d.label + "</color>", small);
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), "<color=" + HexOf(A(g, a)) + ">DONE</color>", smallR);
                Fill(new Rect(x + 12, ry + sh, w - 24, 5), A(g, 0.8f * a));
                ry += rowH;
            }
            return y0 + contentH + 4;
        }

        float PanelContracts(float x, float y, float w)
        {
            var list = game.D.active;
            float rowH = sh * 2 + 6;
            float contentH = Mathf.Max(1, list.Count) * rowH;
            bool anyReady = false;
            foreach (var m in list) { string _; if (game.MissionReady(m, out _)) anyReady = true; }
            Color accent = anyReady ? Hex(Tx.Green) : list.Count > 0 ? Hex(Tx.Yellow) : Muted;
            float y0 = PanelFrame(x, y, w, contentH, "CONTRACTS", accent, Tx.D(list.Count + "/" + Balance.MaxActiveJobs));
            if (list.Count == 0)
            {
                GUI.Label(new Rect(x + 12, y0, w - 24, sh), Tx.D("none - type ") + Tx.W("jobs") + Tx.D(" to find work"), small);
                return y0 + rowH + 4;
            }
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                float ry = y0 + i * rowH;
                var node = game.Node(m.nodeId);
                string why;
                bool ready = game.MissionReady(m, out why);
                if (ready) Fill(new Rect(x + 8, ry - 1, w - 16, rowH - 2), A(Hex(Tx.Green), 0.08f + 0.06f * Pulse(4f)));
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), Tx.W("#" + m.id) + "  " + TypeTag(m.type) + "  " + Tx.Y(node != null ? node.ip : "?"), small);
                string due = "";
                if (!m.tutorial)
                {
                    double left = m.expires - game.D.time;
                    due = left < 120 ? Tx.R(Tx.Clock(left)) : Tx.D(Tx.Clock(left));
                }
                GUI.Label(new Rect(x + 12, ry, w - 24, sh), Tx.Y(Tx.Money(m.payout)) + "  " + due, smallR);
                string status = ready ? Tx.G("READY  >  submit " + m.id) : Tx.D("next: ") + Tx.Safe(why);
                GUI.Label(new Rect(x + 12, ry + sh, w - 24, sh), status, small);
            }
            return y0 + contentH + 4;
        }

        static string TypeTag(MissionType t)
        {
            switch (t)
            {
                case MissionType.Steal: return Tx.Cy("STEAL");
                case MissionType.Delete: return Tx.R("DELETE");
                case MissionType.Plant: return Tx.M("PLANT");
                default: return Tx.Y("ALTER");
            }
        }

        float PanelTrainer(float x, float y, float w)
        {
            var t = game.Tutorial;
            Color m = Hex(Tx.Magenta);
            float y0 = PanelFrame(x, y, w, sh + 10, "TRAINER", m, Tx.M("step " + (t.StepIndex + 1) + "/" + t.StepCount));
            Meter(new Rect(x + 12, y0 + 2, w - 24, 4), (double)t.StepIndex / t.StepCount, m);
            GUI.Label(new Rect(x + 12, y0 + 8, w - 24, sh), Tx.D("Lost? ") + Tx.W("tutorial") + Tx.D(" repeats the hint."), small);
            return y0 + sh + 14;
        }

        float PanelRig(float x, float y, float w)
        {
            var P = game.P;
            float y0 = PanelFrame(x, y, w, sh * 2 + 8, "RIG", Muted, Tx.D(P.gatewayIp));
            string hw = Tx.D("cpu ") + Tx.W("" + P.cpu) + Tx.D("  ram ") + Tx.W("" + P.ram) + Tx.D("  nic ") + Tx.W("" + P.nic) +
                        Tx.D("  hdd ") + Tx.W("" + P.storage) + Tx.D("  cloak ") + Tx.W("" + P.cloak);
            GUI.Label(new Rect(x + 12, y0, w - 24, sh), hw, small);
            int used = game.StorageUsed(), cap = Balance.StorageCap(P.storage);
            GUI.Label(new Rect(x + 12, y0 + sh, w - 24, sh), Tx.D("storage " + used + "/" + cap + " MB"), small);
            float mx = x + 12 + TextW(Tx.D("storage " + used + "/" + cap + " MB"), small) + 10;
            double f = cap > 0 ? (double)used / cap : 0;
            Meter(new Rect(mx, y0 + sh + sh / 2 - 3, x + w - 12 - mx, 6), f, f > 0.85 ? Hex(Tx.Orange) : Hex(Tx.Green));
            return y0 + sh * 2 + 12;
        }

        // ---- toasts & alerts -----------------------------------------------------

        void DrawToasts(Rect area)
        {
            float now = Time.unscaledTime;
            float w = Mathf.Min(480f, area.width - 40f);
            float th = Mathf.Round(sh > 0 ? sh * 2 + 10 : text.lineHeight * 2.2f);
            if (sh <= 0) sh = Mathf.Round(small.lineHeight + 4);
            float y = area.y + 12;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float age = now - t.born;
                float slide = Mathf.Clamp01(age / 0.18f);
                float fade = Mathf.Clamp01((t.life - age) / 0.6f);
                float x = area.xMax - 16 - w + (1 - slide) * (w + 20);
                Color c = KindColor(t.kind);
                var r = new Rect(x, y, w, th);
                Fill(r, new Color(0.02f, 0.06f, 0.03f, 0.94f * fade));
                if (age < 0.5f) Fill(r, A(c, 0.35f * (1 - age / 0.5f) * fade));
                Frame(r, A(c, 0.8f * fade));
                Fill(new Rect(r.x, r.y, 5, r.height), A(c, fade));
                GUI.Label(new Rect(r.x + 16, r.y + 4, w - 24, sh), "<color=" + HexOf(A(c, fade)) + ">" + Tx.Strip(t.title) + "</color>", title);
                if (!string.IsNullOrEmpty(t.text))
                    GUI.Label(new Rect(r.x + 16, r.y + 4 + sh, w - 24, sh), "<color=" + HexOf(A(InputWhite, fade)) + ">" + Tx.Strip(t.text) + "</color>", small);
                // Remaining-life bar along the bottom.
                Fill(new Rect(r.x + 5, r.yMax - 2, (w - 5) * (1 - age / t.life), 2), A(c, 0.6f * fade));
                y += th + 8;
            }
        }

        void DrawAlerts(float W, float H)
        {
            var full = new Rect(0, 0, W, H);
            var old = GUI.color;
            Color red = Hex(Tx.Red);

            // Red glow creeping in from the screen edges as the trace closes in.
            if (game.Conn != null && game.TraceActive)
            {
                float t = (float)game.Trace;
                float speed = t < 0.5f ? 2f : t < 0.75f ? 4f : t < 0.9f ? 7f : 11f;
                float a = 0.12f + 0.5f * t * t;
                if (t >= 0.5f) a *= 0.65f + 0.35f * Pulse(speed);
                GUI.color = A(TraceColor(t), a);
                GUI.DrawTexture(full, vignette);
            }

            if (dangerFlash > 0)
            {
                GUI.color = A(red, 0.7f * dangerFlash);
                GUI.DrawTexture(full, vignette);
                GUI.color = old;
                Fill(full, A(red, 0.12f * dangerFlash));
            }

            if (game.Mode == Mode.GameOver)
            {
                GUI.color = A(red, 0.5f + 0.2f * Pulse(2f));
                GUI.DrawTexture(full, vignette);
            }
            GUI.color = old;
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
