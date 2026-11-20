using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Nullroute.Core;

// Headless harness for the nullroute core.
//   dotnet run --project tools/Sim -- test      scripted checks + a bot career (exit code 1 on failure)
//   dotnet run --project tools/Sim -- bot 5     run 5 bot careers and print balance stats
//   dotnet run --project tools/Sim -- play      play in a plain console (no colours, real-time clock)

class MemStore : ISaveStore
{
    public string Json;
    static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { IncludeFields = true };
    public bool Exists() => Json != null;
    public SaveData Load() => JsonSerializer.Deserialize<SaveData>(Json, Opts);
    public void Save(SaveData d) => Json = JsonSerializer.Serialize(d, Opts);
    public void Delete() => Json = null;
}

static class Program
{
    static int failures;
    static bool verbose;

    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "test";
        verbose = args.Contains("-v");
        switch (mode)
        {
            case "play": Play(); return 0;
            case "bot":
            {
                int n = args.Length > 1 && int.TryParse(args[1], out var k) ? k : 5;
                for (int i = 0; i < n; i++) BotCareer(1000 + i, 400, true);
                return 0;
            }
            default:
                RunTests();
                Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " FAILURE(S)");
                return failures == 0 ? 0 : 1;
        }
    }

    static void Check(bool cond, string what)
    {
        if (cond) { if (verbose) Console.WriteLine("  ok   " + what); }
        else { failures++; Console.WriteLine("  FAIL " + what); }
    }

    static List<string> Drain(Game g)
    {
        var lines = new List<string>();
        while (g.Outbox.Count > 0)
        {
            var l = Tx.Strip(g.Outbox.Dequeue());
            lines.Add(l);
            if (verbose) Console.WriteLine("    | " + l);
        }
        return lines;
    }

    /// <summary>Simulated typing time before each command (bot careers only).</summary>
    static double latency;

    static List<string> Run(Game g, string cmd)
    {
        for (double t = 0; t < latency && g.Mode == Mode.Playing; t += 0.1) g.Tick(0.1);
        if (latency > 0 && g.Mode != Mode.Playing) return Drain(g); // bot was caught while "typing"
        g.Exec(cmd);
        return Drain(g);
    }

    /// <summary>Ticks until all processes finish (or the game ends).</summary>
    static void Wait(Game g, double max = 600)
    {
        double t = 0;
        while (g.Procs.Count > 0 && g.Mode == Mode.Playing && t < max) { g.Tick(0.1); t += 0.1; }
        Drain(g);
    }

    // ------------------------------------------------------------------ tests

    static void RunTests()
    {
        Console.WriteLine("== tutorial walkthrough");
        var store = new MemStore();
        var g = new Game(store, 42);
        g.Start();
        Drain(g);
        Check(g.Mode == Mode.AskHandle, "asks for a handle on first boot");
        Run(g, "x");
        Check(g.Mode == Mode.AskHandle, "rejects a 1-char handle");
        Run(g, "tester");
        Check(g.Mode == Mode.Playing, "starts a career");
        Check(store.Exists(), "saves immediately");

        var training = g.Node(g.D.trainingNodeId);
        int job = g.D.tutorialMissionId;
        foreach (var c in new[] { "help", "rig", "jobs", "accept " + job, "net", "connect " + training.ip, "probe", "crack" }) Run(g, c);
        Check(g.Procs.Count == 1, "crack runs as a background process");
        Wait(g);
        Check(training.passwordKnown, "crack grants access");
        Run(g, "ls");
        Run(g, "download training_key.dat");
        Wait(g);
        Check(g.P.files.Any(f => f.name == "training_key.dat"), "download lands in local storage");
        Run(g, "logwipe");
        Wait(g);
        Check(!training.logs.Any(l => l.byPlayer), "logwipe removes player entries");
        Run(g, "dc");
        Check(g.D.investigations.Count == 0, "training server never opens investigations");
        long before = g.P.credits;
        Run(g, "submit " + job);
        Check(g.P.credits == before + 400, "tutorial job pays 400");
        Run(g, "shop");
        Run(g, "buy fwbypass");
        Check(g.ToolVersion(ToolId.FwBypass) == 1, "can buy fwbypass after the tutorial");
        Check(g.D.tutorialDone, "tutorial completes");

        Console.WriteLine("== save / load round-trip");
        g.Save();
        var g2 = new Game(store, 7);
        g2.Start();
        Drain(g2);
        Check(g2.Mode == Mode.Playing && g2.P.handle == "tester", "loads the saved career");
        Check(g2.P.credits == g.P.credits && g2.P.rep == g.P.rep, "credits and rep survive");
        Check(g2.D.nodes.Count == g.D.nodes.Count, "world survives");
        Check(g2.D.board.Count == Balance.BoardSize, "board is full");

        Console.WriteLine("== tab completion");
        Check(g2.Complete("dis") == "disconnect ", "completes a command");
        var relay = g2.D.nodes.First(n => n.kind == NodeKind.Relay);
        Check(g2.Complete("connect " + relay.ip.Substring(0, relay.ip.Length - 1)).StartsWith("connect "), "completes an IP");

        Console.WriteLine("== caught by an active trace");
        {
            var s = new MemStore();
            var gg = new Game(s, 5);
            gg.Start(); Run(gg, "victim"); Run(gg, "tutorial skip");
            var m = gg.D.board.First(x => x.tier == 0);
            Run(gg, "accept " + m.id);
            var n = gg.Node(m.nodeId);
            gg.P.tools.Add(new ToolLevel { id = ToolId.FwBypass, version = 1 });
            Run(gg, "connect " + n.ip);
            Check(!gg.TraceActive, "connecting alone doesn't start a trace");
            Run(gg, n.firewall > 0 ? "fwbypass" : "crack");
            Check(gg.TraceActive, "hostile action starts the trace");
            Run(gg, "kill " + gg.Procs.First().pid);
            for (int i = 0; i < 4000 && gg.Mode == Mode.Playing; i++) gg.Tick(0.1);
            Drain(gg);
            Check(gg.Mode == Mode.GameOver, "trace completion ends the game");
            Check(!s.Exists(), "game over deletes the save (permadeath)");
            Run(gg, "");
            Check(gg.Mode == Mode.AskHandle, "enter starts a new career");
        }

        Console.WriteLine("== forensic investigation");
        {
            var s = new MemStore();
            var gg = new Game(s, 11);
            gg.Start(); Run(gg, "suspect"); Run(gg, "tutorial skip");
            var m = gg.D.board.First(x => x.tier == 0);
            Run(gg, "accept " + m.id);
            var n = gg.Node(m.nodeId);
            gg.P.tools.Add(new ToolLevel { id = ToolId.FwBypass, version = 1 });
            Run(gg, "connect " + n.ip);
            if (n.firewall > 0) { Run(gg, "fwbypass"); Wait(gg); }
            Run(gg, "crack"); Wait(gg);
            Run(gg, "dc");
            Check(gg.D.investigations.Count == 1, "leaving evidence opens an investigation");
            Run(gg, "connect " + n.ip);
            Run(gg, "logwipe"); Wait(gg);
            Check(gg.D.investigations.Count == 0, "wiping logs kills the investigation");
            Run(gg, "dc");
            Check(gg.D.investigations.Count == 0, "no new investigation after a clean exit");
        }

        Console.WriteLine("== quitting mid-trace is not an escape hatch");
        {
            var s = new MemStore();
            var gg = new Game(s, 13);
            gg.Start(); Run(gg, "quitter"); Run(gg, "tutorial skip");
            var m = gg.D.board.First(x => x.tier == 0);
            Run(gg, "accept " + m.id);
            var n = gg.Node(m.nodeId);
            gg.P.tools.Add(new ToolLevel { id = ToolId.FwBypass, version = 1 });
            Run(gg, "connect " + n.ip);
            Run(gg, n.firewall > 0 ? "fwbypass" : "crack");
            gg.Shutdown();
            var reload = new Game(s, 14);
            reload.Start(); Drain(reload);
            Check(reload.D.investigations.Count == 1, "quit while connected leaves an open investigation");
        }

        Console.WriteLine("== balance: bot careers");
        int survived = 0, totalJobs = 0, maxRank = 0;
        for (int i = 0; i < 6; i++)
        {
            var r = BotCareer(2000 + i, 250, false);
            if (r.alive) survived++;
            totalJobs += r.jobs;
            maxRank = Math.Max(maxRank, r.rank);
        }
        Console.WriteLine($"  6 careers: {survived} survived, {totalJobs} jobs total, best rank {maxRank}");
        Check(totalJobs > 300, "a careful bot completes plenty of jobs");
        Check(maxRank >= 5, "a careful bot keeps ranking up");
    }

    // ------------------------------------------------------------------ bot

    struct Result { public bool alive; public int jobs; public int rank; public double hours; }

    static Result BotCareer(int seed, int attempts, bool report)
    {
        var s = new MemStore();
        var g = new Game(s, seed);
        latency = 0;
        g.Start(); Run(g, "bot" + seed); Run(g, "tutorial skip");
        latency = 2.5;
        var sw = Stopwatch.StartNew();
        var rankTimes = new List<string>();
        int lastRank = 0;
        int aborted = 0, abandoned = 0;

        for (int it = 0; it < attempts && g.Mode == Mode.Playing; it++)
        {
            Shop(g);
            Run(g, "bounce auto");

            // Let open investigations resolve or wipe them first.
            if (g.D.investigations.Count > 0)
            {
                var inv = g.D.investigations[0];
                var bad = g.Node(inv.nodeId);
                if (bad != null && bad.passwordKnown)
                {
                    Run(g, "connect " + bad.ip); Run(g, "logwipe"); WaitSafe(g); Run(g, "dc");
                    continue;
                }
            }

            var job = PickJob(g);
            if (job == null) { for (int k = 0; k < 600; k++) g.Tick(0.1); Drain(g); continue; }
            Run(g, "accept " + job.id);
            var n = g.Node(job.nodeId);
            if (g.Mode != Mode.Playing) break;
            Run(g, "connect " + n.ip);

            bool ok = true;
            foreach (var layer in new[] { "pxbypass", "fwbypass", "monbypass" })
            {
                bool present = layer == "pxbypass" ? n.proxy > 0 : layer == "fwbypass" ? n.firewall > 0 : n.monitor > 0;
                if (present) Run(g, layer);
            }
            ok &= WaitSafe(g);
            if (ok && !n.passwordKnown) { Run(g, "crack"); ok &= WaitSafe(g); }
            if (ok)
            {
                switch (job.type)
                {
                    case MissionType.Steal: Run(g, "download " + job.fileName); break;
                    case MissionType.Delete: Run(g, "delete " + job.fileName); break;
                    case MissionType.Plant: Run(g, "upload " + job.fileName); break;
                    case MissionType.Alter: Run(g, $"db edit {job.recordId} {job.field} {job.value}"); break;
                }
                ok &= WaitSafe(g);
            }
            if (ok) { Run(g, "logwipe"); ok &= WaitSafe(g); }
            if (g.Mode != Mode.Playing) break;
            if (!ok) aborted++;
            Run(g, "dc");

            var lf = g.P.files.FirstOrDefault(f => f.name == job.fileName && f.encryption > 0);
            if (lf != null) { Run(g, "decrypt " + lf.name); Wait(g); }
            Run(g, "submit " + job.id);
            if (g.D.active.Any(m => m.id == job.id)) { Run(g, "abandon " + job.id); abandoned++; }
            if (g.Rank > lastRank) { lastRank = g.Rank; rankTimes.Add("r" + lastRank + "@" + (g.D.time / 60).ToString("0") + "m"); }
        }
        latency = 0;

        var res = new Result { alive = g.Mode == Mode.Playing, jobs = g.P.jobsDone, rank = g.Rank, hours = g.D.time / 3600 };
        if (report)
            Console.WriteLine($"seed {seed}: {(res.alive ? "alive" : "CAUGHT")} jobs={res.jobs} rank={res.rank} ({Balance.RankTitle(res.rank)}) " +
                              $"credits={g.P.credits} cpu{g.P.cpu} ram{g.P.ram} nic{g.P.nic} cloak{g.P.cloak} " +
                              $"tools=[{string.Join(",", g.P.tools.Select(t => t.id + "v" + t.version))}] aborted={aborted} abandoned={abandoned} " +
                              $"gametime={res.hours:0.0}h ({sw.ElapsedMilliseconds}ms)\n    ranks: {string.Join(" ", rankTimes)}");
        return res;
    }

    /// <summary>Waits for processes, bailing out if the trace gets dangerous.</summary>
    static bool WaitSafe(Game g)
    {
        while (g.Procs.Count > 0 && g.Mode == Mode.Playing)
        {
            g.Tick(0.1);
            if (g.TraceActive && g.Trace > 0.85) { Drain(g); return false; }
        }
        Drain(g);
        return g.Mode == Mode.Playing;
    }

    static bool CanBeat(Game g, NodeData n)
    {
        if (n.proxy > g.ToolVersion(ToolId.PxBypass)) return false;
        if (n.firewall > g.ToolVersion(ToolId.FwBypass)) return false;
        if (n.password > g.ToolVersion(ToolId.PwCrack)) return false;
        return true;
    }

    static MissionData PickJob(Game g)
    {
        // The bot cheats slightly by peeking at defenses; a human would probe first.
        return g.D.board.Where(m => !m.tutorial)
            .Where(m => CanBeat(g, g.Node(m.nodeId)))
            .Where(m => m.type != MissionType.Steal || g.Node(m.nodeId).files.First(f => f.name == m.fileName).encryption <= g.ToolVersion(ToolId.Decrypt))
            .OrderByDescending(m => m.payout).FirstOrDefault();
    }

    static void Shop(Game g)
    {
        // Greedy: keep buying the cheapest useful thing we can afford, holding back a small reserve.
        for (int guard = 0; guard < 50; guard++)
        {
            int cap = Balance.MaxToolVersion(g.Rank);
            var options = new List<(string cmd, long price)>();
            foreach (var t in Balance.Tools)
            {
                int v = g.ToolVersion(t.id);
                if (t.unlockRank > g.Rank || v + 1 > cap) continue;
                options.Add(("buy " + t.key, Balance.ToolPrice(t.id, v + 1)));
            }
            options.Add(("upgrade cpu", Balance.HwPrice(HwId.Cpu, g.P.cpu + 1)));
            options.Add(("upgrade cloak", Balance.HwPrice(HwId.Cloak, g.P.cloak + 1)));
            options.Add(("upgrade nic", Balance.HwPrice(HwId.Nic, g.P.nic + 1)));
            if (g.P.ram < 3) options.Add(("upgrade ram", Balance.HwPrice(HwId.Ram, g.P.ram + 1)));
            if (g.P.storage < g.P.cpu) options.Add(("upgrade hdd", Balance.HwPrice(HwId.Storage, g.P.storage + 1)));
            var pick = options.Where(o => o.price <= g.P.credits).OrderBy(o => o.price).FirstOrDefault();
            if (pick.cmd == null) return;
            Run(g, pick.cmd);
        }
    }

    // ------------------------------------------------------------------ console play

    static void Play()
    {
        var store = new FileStore(System.IO.Path.Combine(AppContext.BaseDirectory, "console_save.json"));
        var g = new Game(store, Environment.TickCount);
        g.Start();
        var clock = Stopwatch.StartNew();
        double last = 0;
        while (!g.QuitRequested)
        {
            double now = clock.Elapsed.TotalSeconds;
            g.Tick(now - last);
            last = now;
            foreach (var l in Drain(g)) if (l != Game.ClearSignal) Console.WriteLine(l);
            foreach (var l in g.StatusLines().Skip(1)) Console.WriteLine(Tx.Strip(l));
            Console.Write(Tx.Strip(g.Prompt));
            var line = Console.ReadLine();
            if (line == null) { g.Shutdown(); break; }
            now = clock.Elapsed.TotalSeconds;
            g.Tick(now - last);
            last = now;
            g.Exec(line);
        }
        foreach (var l in Drain(g)) Console.WriteLine(l);
    }

    class FileStore : ISaveStore
    {
        readonly string path;
        readonly MemStore mem = new MemStore();
        public FileStore(string p) { path = p; }
        public bool Exists() => System.IO.File.Exists(path);
        public SaveData Load() { mem.Json = System.IO.File.ReadAllText(path); return mem.Load(); }
        public void Save(SaveData d) { mem.Save(d); System.IO.File.WriteAllText(path, mem.Json); }
        public void Delete() { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }
}
