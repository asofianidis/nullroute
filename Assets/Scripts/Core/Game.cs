using System;
using System.Collections.Generic;
using System.Linq;

namespace Nullroute.Core
{
    public enum Mode { AskHandle, Playing, GameOver }

    /// <summary>
    /// The whole game, engine-agnostic. A front end feeds it text commands via <see cref="Exec"/>,
    /// advances time with <see cref="Tick"/>, and drains <see cref="Outbox"/> for output lines.
    /// </summary>
    public partial class Game
    {
        /// <summary>Sent through the outbox to tell the front end to clear the screen.</summary>
        public const string ClearSignal = "\u0001CLEAR";

        public SaveData D { get; private set; }
        public Mode Mode { get; private set; }
        public readonly Queue<string> Outbox = new Queue<string>();
        public bool QuitRequested;

        public NodeData Conn { get; private set; }
        public readonly List<string> Route = new List<string>();
        public bool TraceActive { get; private set; }
        public double Trace { get; private set; }
        public readonly List<Proc> Procs = new List<Proc>();

        /// <summary>Headline events for a graphical front end. Capped so headless runs never grow it.</summary>
        public readonly Queue<Notice> Notices = new Queue<Notice>();
        /// <summary>Bumped on every error line, so a front end can flash the prompt.</summary>
        public int ErrorCount { get; private set; }
        /// <summary>Procs that finished, with whether they succeeded, for a front end to animate.</summary>
        public readonly Queue<Proc> Finished = new Queue<Proc>();

        readonly ISaveStore store;
        readonly Rng rng;
        readonly HashSet<string> used = new HashSet<string>();
        Tutorial tutorial;
        int nextPid = 100;
        int traceWarn;
        double autosave;
        double upkeep;

        public Game(ISaveStore store, int seed)
        {
            this.store = store;
            rng = new Rng(seed);
            BuildCommands();
        }

        public PlayerData P { get { return D.player; } }
        public int Rank { get { return Balance.RankFor(P.rep); } }
        public bool Used(string cmd) { return used.Contains(cmd); }
        public Tutorial Tutorial { get { return tutorial; } }

        // ------------------------------------------------------------------ output

        public void Print(string s = "")
        {
            foreach (var line in s.Split('\n')) Outbox.Enqueue(line);
        }

        void Warn(string s) { Print(Tx.R("[!] ") + Tx.Y(s)); }
        void Err(string s) { ErrorCount++; Print(Tx.R(s)); }

        public void Notify(NoticeKind kind, string title, string text = null)
        {
            Notices.Enqueue(new Notice { kind = kind, title = title, text = text });
            while (Notices.Count > 24) Notices.Dequeue();
        }
        void Ok(string s) { Print(Tx.G(s)); }

        public string Prompt
        {
            get
            {
                if (Mode == Mode.AskHandle) return Tx.Cy("handle") + Tx.D("> ");
                if (Mode == Mode.GameOver) return Tx.R("press enter") + Tx.D("> ");
                string host = Conn == null ? "gateway" : Conn.ip;
                return Tx.G(P.handle) + Tx.D("@") + (Conn == null ? Tx.Cy(host) : Tx.Y(host)) + Tx.D(":~$ ");
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public void Start()
        {
            PrintBanner();
            if (store.Exists())
            {
                SaveData loaded = null;
                try { loaded = store.Load(); }
                catch (Exception e) { Err("Save file unreadable (" + e.Message + "). Starting fresh."); }
                if (loaded != null && loaded.player != null && !string.IsNullOrEmpty(loaded.player.handle))
                {
                    D = loaded;
                    Resume();
                    return;
                }
            }
            AskHandle();
        }

        void PrintBanner()
        {
            Print(Tx.G(@"                 _ _                 _       "));
            Print(Tx.G(@"     _ __  _   _| | |_ __ ___  _   _| |_ ___ "));
            Print(Tx.G(@"    | '_ \| | | | | | '__/ _ \| | | | __/ _ \"));
            Print(Tx.G(@"    | | | | |_| | | | | | (_) | |_| | ||  __/"));
            Print(Tx.G(@"    |_| |_|\__,_|_|_|_|  \___/ \__,_|\__\___|"));
            Print(Tx.D("    gateway os 4.7 :: secure shell :: no logs, no masters"));
            Print();
        }

        void AskHandle()
        {
            D = null;
            Conn = null;
            Procs.Clear();
            Mode = Mode.AskHandle;
            Print(Tx.W("No career found on this gateway."));
            Print("Choose a " + Tx.Cy("handle") + " (2-16 characters: letters, digits, _ or -).");
        }

        void NewGame(string handle)
        {
            D = new SaveData();
            P.handle = handle;
            P.gatewayIp = NameGen.Ip(rng);
            P.credits = Balance.StartCredits;
            P.tools.Add(new ToolLevel { id = ToolId.PwCrack, version = 1 });
            P.tools.Add(new ToolLevel { id = ToolId.LogWipe, version = 1 });

            EnsureRelays();
            var training = WorldGen.CreateTraining(D, rng);
            D.trainingNodeId = training.id;
            var tm = WorldGen.CreateTutorialMission(D, training);
            D.tutorialMissionId = tm.id;
            D.board.Add(tm);
            RefillBoard();

            used.Clear();
            Mode = Mode.Playing;
            Print();
            Print(Tx.D("[boot] ") + "mounting encrypted volume.............. " + Tx.G("ok"));
            Print(Tx.D("[boot] ") + "gateway address " + Tx.Cy(P.gatewayIp) + " .......... " + Tx.G("ok"));
            Print(Tx.D("[boot] ") + "loading toolkit: pwcrack v1, logwipe v1 .. " + Tx.G("ok"));
            Print(Tx.D("[boot] ") + "credits on hand: " + Tx.Y(Tx.Money(P.credits)));
            Print();
            Print("Welcome, " + Tx.G(handle) + ". There is no finish line here - only the next job.");
            Print();
            tutorial = new Tutorial(this);
            tutorial.Begin();
            Save();
        }

        void Resume()
        {
            Mode = Mode.Playing;
            Conn = null;
            Procs.Clear();
            foreach (var n in D.nodes) { n.proxyDown = n.firewallDown = n.monitorDown = false; }
            EnsureRelays();
            RefillBoard();
            tutorial = new Tutorial(this);
            Print("Welcome back, " + Tx.G(P.handle) + ". " + Tx.D("Rank: ") + Tx.Cy(Balance.RankTitle(Rank)) +
                  Tx.D("  Credits: ") + Tx.Y(Tx.Money(P.credits)));
            if (D.investigations.Count > 0)
                Warn(D.investigations.Count + " forensic investigation(s) still open. Type `status`.");
            Print(Tx.D("Type `help` for commands."));
            if (tutorial.Active) tutorial.Show();
        }

        /// <summary>Call when the application is closing.</summary>
        public void Shutdown()
        {
            if (Mode != Mode.Playing || D == null) return;
            if (Conn != null) Disconnect(false); // quitting mid-job leaves evidence, no escape hatch
            Save();
        }

        public void Save()
        {
            if (D == null || Mode != Mode.Playing) return;
            try { store.Save(D); }
            catch (Exception e) { Err("Save failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ simulation

        public void Tick(double dt)
        {
            if (Mode != Mode.Playing || D == null) return;
            D.time += dt;

            List<Proc> finished = null;
            foreach (var p in Procs)
            {
                p.elapsed += dt;
                if (p.elapsed >= p.duration) (finished ?? (finished = new List<Proc>())).Add(p);
            }
            if (finished != null)
            {
                foreach (var p in finished)
                {
                    Procs.Remove(p);
                    Finished.Enqueue(p);
                    while (Finished.Count > 24) Finished.Dequeue();
                    Complete(p);
                    if (Mode != Mode.Playing) return;
                }
                tutorial.Check();
            }

            if (Conn != null && TraceActive)
            {
                double dur = TraceDuration(Conn, Route.Count);
                if (dur > 0)
                {
                    Trace += dt / dur;
                    if (Trace >= 0.5 && traceWarn < 1) { traceWarn = 1; Warn("Trace at 50%."); Notify(NoticeKind.Warn, "TRACE 50%", "Halfway there. Keep moving."); }
                    if (Trace >= 0.75 && traceWarn < 2) { traceWarn = 2; Warn("Trace at 75%. Finish up."); Notify(NoticeKind.Danger, "TRACE 75%", "Finish up and wipe the logs."); }
                    if (Trace >= 0.9 && traceWarn < 3) { traceWarn = 3; Print(Tx.R("[!!!] TRACE AT 90% - DISCONNECT NOW (`dc`)")); Notify(NoticeKind.Danger, "TRACE 90%", "DISCONNECT NOW - type dc"); }
                    if (Trace >= 1)
                    {
                        Caught("Active trace completed on " + Conn.name + " (" + Conn.ip + ").");
                        return;
                    }
                }
            }

            for (int i = D.investigations.Count - 1; i >= 0; i--)
            {
                var inv = D.investigations[i];
                if (D.time < inv.ends) continue;
                D.investigations.RemoveAt(i);
                ResolveInvestigation(inv);
                if (Mode != Mode.Playing) return;
            }

            upkeep += dt;
            if (upkeep >= 1)
            {
                upkeep = 0;
                Upkeep();
            }

            autosave += dt;
            if (autosave >= 20)
            {
                autosave = 0;
                Save();
            }
        }

        void Upkeep()
        {
            for (int i = D.board.Count - 1; i >= 0; i--)
                if (D.board[i].expires <= D.time) { var m = D.board[i]; D.board.RemoveAt(i); Prune(m.nodeId); }

            for (int i = D.active.Count - 1; i >= 0; i--)
            {
                var m = D.active[i];
                if (m.expires > D.time) continue;
                D.active.RemoveAt(i);
                int loss = Math.Min(P.rep, Math.Max(2, m.rep / 2));
                P.rep -= loss;
                Warn("Contract #" + m.id + " expired. The client is unhappy: -" + loss + " rep.");
                Notify(NoticeKind.Warn, "CONTRACT #" + m.id + " EXPIRED", "-" + loss + " rep");
                Prune(m.nodeId);
            }
            RefillBoard();
        }

        public double TraceDuration(NodeData n, int bounces)
        {
            if (n.traceTime <= 0) return 0;
            int mon = n.monitor > 0 && !n.monitorDown ? n.monitor : 0;
            return Balance.TraceDuration(n.traceTime, bounces, P.cloak, mon);
        }

        // ------------------------------------------------------------------ world upkeep

        void EnsureRelays()
        {
            int want = Balance.MaxBounces(P.nic) + 3;
            int have = D.nodes.Count(n => n.kind == NodeKind.Relay);
            for (int i = have; i < want; i++) WorldGen.CreateRelay(D, rng);
        }

        void RefillBoard()
        {
            int rank = Rank;
            int open = D.board.Count(m => !m.tutorial);
            for (int i = open; i < Balance.BoardSize; i++)
            {
                // Mostly jobs at your rank, some easier, some a stretch.
                double roll = rng.Value();
                int tier = roll < 0.2 ? rank - 1 : roll < 0.75 ? rank : rank + 1;
                if (tier < 0) tier = 0;
                D.board.Add(WorldGen.CreateMission(D, rng, tier));
            }
        }

        /// <summary>Removes a target node once nothing refers to it any more.</summary>
        void Prune(string nodeId)
        {
            var n = Node(nodeId);
            if (n == null || n.kind == NodeKind.Relay || IsReferenced(n)) return;
            if (n.passwordKnown && n.kind != NodeKind.Training)
            {
                // Breached systems stay around as bounce hops, up to a cap; past it the oldest go.
                var spare = D.nodes.Where(x => x.passwordKnown && x.kind != NodeKind.Training && !IsReferenced(x))
                                   .OrderBy(x => x.breachedAt).ToList();
                while (spare.Count > 25) { D.nodes.Remove(spare[0]); spare.RemoveAt(0); }
                return;
            }
            D.nodes.Remove(n);
        }

        bool IsReferenced(NodeData n)
        {
            return Conn == n || P.bounces.Contains(n.id) ||
                   D.board.Any(m => m.nodeId == n.id) || D.active.Any(m => m.nodeId == n.id) ||
                   D.investigations.Any(i => i.nodeId == n.id);
        }

        public NodeData Node(string id)
        {
            foreach (var n in D.nodes) if (n.id == id) return n;
            return null;
        }

        public NodeData NodeByIp(string ip)
        {
            foreach (var n in D.nodes) if (n.ip == ip) return n;
            return null;
        }

        public int ToolVersion(ToolId id)
        {
            foreach (var t in P.tools) if (t.id == id) return t.version;
            return 0;
        }

        public int StorageUsed() { return P.files.Sum(f => f.size); }

        public bool HasAccess(NodeData n) { return n.isPublic || n.passwordKnown; }

        string OriginIp()
        {
            if (Route.Count == 0) return P.gatewayIp;
            var last = Node(Route[Route.Count - 1]);
            return last != null ? last.ip : P.gatewayIp;
        }

        // ------------------------------------------------------------------ connection

        void Connect(NodeData n)
        {
            Route.Clear();
            foreach (var id in P.bounces) if (Node(id) != null && id != n.id) Route.Add(id);
            Conn = n;
            TraceActive = false;
            Trace = 0;
            traceWarn = 0;
            n.logs.Add(new LogEntry { time = D.time, text = "connection from " + OriginIp(), byPlayer = true });
        }

        /// <summary>Marks an action as hostile: starts the trace and leaves evidence.</summary>
        void Hostile(string what)
        {
            var n = Conn;
            n.logs.Add(new LogEntry { time = D.time, text = what + " from " + OriginIp(), byPlayer = true, suspicious = true });
            if (n.traceTime > 0 && !TraceActive)
            {
                TraceActive = true;
                Warn("Intrusion detected - " + n.name + " has started tracing you.");
                Notify(NoticeKind.Danger, "INTRUSION DETECTED", n.name + " is tracing you");
            }
        }

        public void Disconnect(bool verbose)
        {
            var n = Conn;
            if (n == null) return;
            int aborted = Procs.RemoveAll(p => p.nodeId == n.id);
            n.proxyDown = n.firewallDown = n.monitorDown = false;
            Conn = null;
            TraceActive = false;
            Trace = 0;
            if (verbose)
            {
                Notify(NoticeKind.Info, "DISCONNECTED", n.ip + (aborted > 0 ? "  (" + aborted + " process(es) aborted)" : ""));
                Print(Tx.D("Connection to ") + n.ip + Tx.D(" closed.") + (aborted > 0 ? Tx.Y(" " + aborted + " remote process(es) aborted.") : ""));
            }

            int evidence = n.logs.Count(l => l.byPlayer && l.suspicious);
            if (evidence > 0 && n.kind != NodeKind.Training) OpenInvestigation(n, evidence);
            Route.Clear();
        }

        void OpenInvestigation(NodeData n, int evidence)
        {
            double chance = Balance.InvestigationChance(n.tier, evidence, Route.Count, P.cloak, ToolVersion(ToolId.Spoofer));
            var inv = D.investigations.FirstOrDefault(i => i.nodeId == n.id);
            if (inv == null)
            {
                inv = new InvestigationData { nodeId = n.id, ends = D.time + Balance.InvestigationDelay(n.tier, rng), chance = chance };
                D.investigations.Add(inv);
            }
            else
            {
                inv.chance = Math.Max(inv.chance, chance);
            }
            Warn("You left " + evidence + " suspicious log entr" + (evidence == 1 ? "y" : "ies") + " on " + n.name + ".");
            Notify(NoticeKind.Warn, "EVIDENCE LEFT BEHIND", n.name + ": forensics in " + Tx.Clock(inv.ends - D.time) + ", risk " + Tx.Pct(inv.chance));
            Print(Tx.Y("    Forensic trace begins in " + Tx.Clock(inv.ends - D.time) + " (risk " + Tx.Pct(inv.chance) +
                       "). Reconnect and `logwipe` to kill it."));
        }

        void ResolveInvestigation(InvestigationData inv)
        {
            var n = Node(inv.nodeId);
            if (n == null) return;
            bool evidence = n.logs.Any(l => l.byPlayer && l.suspicious);
            if (!evidence)
            {
                Print(Tx.G("[+] Investigation at " + n.name + " closed: no evidence."));
                Notify(NoticeKind.Good, "INVESTIGATION CLOSED", n.name + ": no evidence");
            }
            else if (rng.Chance(inv.chance))
            {
                Caught("Forensic analysts at " + n.name + " followed your log trail back to your gateway.");
                return;
            }
            else
            {
                foreach (var l in n.logs) if (l.byPlayer) l.suspicious = false;
                Print(Tx.G("[+] Investigation at " + n.name + " went cold. You got lucky."));
                Notify(NoticeKind.Good, "INVESTIGATION WENT COLD", n.name + ": you got lucky");
            }
            Prune(n.id);
        }

        void Caught(string how)
        {
            Mode = Mode.GameOver;
            Conn = null;
            Procs.Clear();
            Route.Clear();
            try { store.Delete(); } catch { }
            Notify(NoticeKind.Danger, "GATEWAY SEIZED", how);
            Print();
            Print(Tx.R("################################################################"));
            Print(Tx.R("##                                                            ##"));
            Print(Tx.R("##            TRACE COMPLETE  ::  GATEWAY SEIZED              ##"));
            Print(Tx.R("##                                                            ##"));
            Print(Tx.R("################################################################"));
            Print();
            Print(Tx.Y(how));
            Print(Tx.Y("Your gateway at " + P.gatewayIp + " has been located. They're at the door."));
            Print();
            Print(Tx.W("  CAREER SUMMARY"));
            Print("  handle ........ " + Tx.G(P.handle));
            Print("  final rank .... " + Tx.Cy(Balance.RankTitle(Rank)) + Tx.D(" (" + P.rep + " rep)"));
            Print("  contracts ..... " + P.jobsDone);
            Print("  systems ....... " + P.nodesBreached + " breached");
            Print("  earned ........ " + Tx.Y(Tx.Money(P.totalEarned)));
            Print("  time active ... " + Tx.Clock(D.time));
            Print();
            Print(Tx.D("There are no second chances. Press ENTER to start a new career."));
        }

        // ------------------------------------------------------------------ processes

        Proc StartProc(ProcKind kind, string label, double duration, string nodeId, string arg = null, string arg2 = null, string arg3 = null)
        {
            var p = new Proc { pid = nextPid++, kind = kind, label = label, duration = duration, nodeId = nodeId, arg = arg, arg2 = arg2, arg3 = arg3 };
            Procs.Add(p);
            return p;
        }

        bool RamFree()
        {
            if (Procs.Count < Balance.RamSlots(P.ram)) return true;
            Err("Out of memory: " + Balance.RamSlots(P.ram) + " processes max. Wait, `kill` one, or `upgrade ram`.");
            return false;
        }

        void Complete(Proc p)
        {
            NodeData n = p.nodeId != null ? Node(p.nodeId) : null;
            if (p.nodeId != null && (n == null || Conn != n)) return;

            switch (p.kind)
            {
                case ProcKind.FwBypass:
                    n.firewallDown = true;
                    Ok("[" + p.pid + "] Firewall bypassed.");
                    Notify(NoticeKind.Good, "FIREWALL BYPASSED", n.name);
                    break;
                case ProcKind.PxBypass:
                    n.proxyDown = true;
                    Ok("[" + p.pid + "] Proxy bypassed.");
                    Notify(NoticeKind.Good, "PROXY BYPASSED", n.name);
                    break;
                case ProcKind.MonBypass:
                    n.monitorDown = true;
                    Ok("[" + p.pid + "] Monitor blinded - trace slowed.");
                    Notify(NoticeKind.Good, "MONITOR BLINDED", "Trace slowed");
                    break;
                case ProcKind.Crack:
                    n.passwordKnown = true;
                    if (n.breachedAt <= 0) { n.breachedAt = D.time + 0.001; P.nodesBreached++; }
                    Ok("[" + p.pid + "] Password cracked: " + Tx.W(Tx.Safe(n.adminPassword ?? "hunter2")));
                    Ok("    ACCESS GRANTED - logged in as admin. Try `ls`" + (n.records.Count > 0 ? " or `db`." : "."));
                    Notify(NoticeKind.Good, "ACCESS GRANTED", n.name + " - logged in as admin");
                    break;
                case ProcKind.LogWipe:
                {
                    int removed = n.logs.RemoveAll(l => l.byPlayer);
                    Ok("[" + p.pid + "] Logs scrubbed: " + removed + " entr" + (removed == 1 ? "y" : "ies") + " removed.");
                    int cancelled = D.investigations.RemoveAll(i => i.nodeId == n.id);
                    if (cancelled > 0) Ok("    The investigation at " + n.name + " has nothing to go on. It's dead.");
                    Notify(NoticeKind.Good, "LOGS SCRUBBED", removed + " entr" + (removed == 1 ? "y" : "ies") + " removed" + (cancelled > 0 ? ", investigation dead" : ""));
                    break;
                }
                case ProcKind.Download:
                {
                    var f = n.files.FirstOrDefault(x => x.name == p.arg);
                    if (f == null) { Err("[" + p.pid + "] Download failed: file vanished."); break; }
                    P.files.RemoveAll(x => x.name == f.name);
                    P.files.Add(new LocalFile { name = f.name, size = f.size, encryption = f.encryption, fromNode = n.id });
                    Notify(NoticeKind.Good, "DOWNLOAD COMPLETE", f.name + (f.encryption > 0 ? "  (encrypted)" : ""));
                    Ok("[" + p.pid + "] Downloaded " + f.name + " (" + f.size + " MB)" + (f.encryption > 0 ? Tx.Y(" - encrypted, lvl " + f.encryption) : "") + ".");
                    break;
                }
                case ProcKind.Upload:
                {
                    var lf = P.files.FirstOrDefault(x => x.name == p.arg);
                    if (lf == null) { Err("[" + p.pid + "] Upload failed: local file missing."); break; }
                    n.files.RemoveAll(x => x.name == lf.name);
                    n.files.Add(new FileData { name = lf.name, size = lf.size, encryption = lf.encryption });
                    P.files.Remove(lf);
                    Ok("[" + p.pid + "] Uploaded " + lf.name + " to " + n.ip + ".");
                    Notify(NoticeKind.Good, "UPLOAD COMPLETE", lf.name);
                    break;
                }
                case ProcKind.Delete:
                {
                    int r = n.files.RemoveAll(x => x.name == p.arg);
                    if (r > 0) { Ok("[" + p.pid + "] Deleted " + p.arg + "."); Notify(NoticeKind.Good, "FILE DELETED", p.arg); }
                    else Err("[" + p.pid + "] Delete failed: file not found.");
                    break;
                }
                case ProcKind.DbEdit:
                {
                    var rec = n.records.FirstOrDefault(x => x.id.Equals(p.arg, StringComparison.OrdinalIgnoreCase));
                    var fld = rec?.fields.FirstOrDefault(x => x.key == p.arg2);
                    if (fld == null) { Err("[" + p.pid + "] Edit failed: record changed."); break; }
                    fld.value = p.arg3;
                    Notify(NoticeKind.Good, "RECORD UPDATED", rec.id + "." + fld.key + " = " + Tx.Safe(fld.value));
                    Ok("[" + p.pid + "] Record " + rec.id + " updated: " + fld.key + " = " + Tx.Safe(fld.value));
                    break;
                }
                case ProcKind.Decrypt:
                {
                    var lf = P.files.FirstOrDefault(x => x.name == p.arg);
                    if (lf == null) { Err("[" + p.pid + "] Decrypt failed: file missing."); break; }
                    lf.encryption = 0;
                    Ok("[" + p.pid + "] Decrypted " + lf.name + ".");
                    Notify(NoticeKind.Good, "DECRYPTED", lf.name);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ status lines for the HUD

        public List<string> StatusLines()
        {
            var lines = new List<string>();
            if (Mode != Mode.Playing || D == null)
            {
                lines.Add(Tx.G(" NULLROUTE") + Tx.D("  ::  ") + (Mode == Mode.GameOver ? Tx.R("CAREER TERMINATED") : Tx.D("no career loaded")));
                return lines;
            }

            int rank = Rank;
            string sep = Tx.D("  |  ");
            lines.Add(Tx.G(" NULLROUTE") + sep + Tx.W(P.handle) + sep + Tx.Y(Tx.Money(P.credits)) + sep +
                      Tx.Cy(Balance.RankTitle(rank)) + Tx.D(" " + P.rep + "/" + Balance.RankThreshold(rank + 1) + " rep") + sep +
                      Tx.D("cpu") + P.cpu + Tx.D(" ram") + P.ram + Tx.D(" nic") + P.nic + Tx.D(" hdd") + P.storage + Tx.D(" cloak") + P.cloak +
                      sep + Tx.D("jobs ") + D.active.Count + "/" + Balance.MaxActiveJobs);

            if (Conn == null)
            {
                lines.Add(Tx.D(" LINK  offline") + (P.bounces.Count > 0 ? Tx.D("   route: " + P.bounces.Count + " bounce(s) ready") : ""));
            }
            else
            {
                string link = Tx.D(" LINK  ") + Tx.Y(Conn.ip) + " " + Tx.W(Conn.name) + Tx.D(" via " + Route.Count + " hop(s)");
                if (Conn.traceTime <= 0) link += sep + Tx.D("TRACE n/a (training)");
                else if (!TraceActive) link += sep + Tx.G("TRACE idle");
                else
                {
                    string col = Trace < 0.5 ? Tx.Yellow : Trace < 0.8 ? Tx.Orange : Tx.Red;
                    double left = (1 - Trace) * TraceDuration(Conn, Route.Count);
                    link += sep + Tx.C(col, "TRACE " + Tx.Bar(Trace, 20) + " " + Tx.Pct(Trace) + " ~" + Tx.Clock(left));
                }
                lines.Add(link);
            }

            foreach (var inv in D.investigations)
            {
                var n = Node(inv.nodeId);
                lines.Add(Tx.R(" FORENSICS ") + Tx.Y((n != null ? n.name : "?") + " in " + Tx.Clock(inv.ends - D.time) + " (risk " + Tx.Pct(inv.chance) + ")"));
            }

            foreach (var p in Procs)
                lines.Add(Tx.D(" [" + p.pid + "] ") + Tx.Pad(Tx.Cy(p.label), 34) + Tx.G(Tx.Bar(p.Progress, 24)) + " " +
                          Tx.PadL(Tx.Pct(p.Progress), 4) + Tx.D(" " + Tx.Clock(p.Remaining)));
            return lines;
        }
    }
}
