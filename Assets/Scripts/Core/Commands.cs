using System;
using System.Collections.Generic;
using System.Linq;

namespace Nullroute.Core
{
    public partial class Game
    {
        class Cmd
        {
            public string name;
            public string[] aliases;
            public string usage;
            public string help;
            public string cat;
            public Action<string[], string> run; // (args, rest-of-line after command)
        }

        readonly List<Cmd> commands = new List<Cmd>();
        readonly Dictionary<string, Cmd> lookup = new Dictionary<string, Cmd>();

        static readonly string[] Categories = { "general", "career", "workstation", "network", "intrusion", "remote", "local" };

        void Add(string cat, string name, string aliases, string usage, string help, Action<string[], string> run)
        {
            var c = new Cmd
            {
                cat = cat, name = name, usage = usage, help = help, run = run,
                aliases = string.IsNullOrEmpty(aliases) ? new string[0] : aliases.Split(','),
            };
            commands.Add(c);
            lookup[name] = c;
            foreach (var a in c.aliases) lookup[a] = c;
        }

        void BuildCommands()
        {
            Add("general", "help", "?,man", "help [command]", "List commands, or explain one in detail.", CmdHelp);
            Add("general", "status", "whoami", "status", "Your career: credits, reputation, rank, open investigations.", (a, r) => CmdStatus());
            Add("general", "tutorial", "hint", "tutorial [skip]", "Repeat the current tutorial hint, or skip the tutorial.", CmdTutorial);
            Add("general", "clear", "cls", "clear", "Clear the screen.", (a, r) => Outbox.Enqueue(ClearSignal));
            Add("general", "save", "", "save", "Save now (the game also autosaves).", (a, r) => { Save(); Ok("Saved."); });
            Add("general", "quit", "", "quit", "Save and quit. Quitting while connected counts as a disconnect.", (a, r) => { Shutdown(); QuitRequested = true; Print(Tx.D("Goodbye.")); });

            Add("career", "jobs", "board", "jobs", "Show the contract board.", (a, r) => CmdJobs());
            Add("career", "accept", "take", "accept <job#>", "Accept a contract from the board.", CmdAccept);
            Add("career", "missions", "active,contracts", "missions", "Show your accepted contracts and their progress.", (a, r) => CmdMissions());
            Add("career", "submit", "complete", "submit <job#>", "Deliver a finished contract and get paid.", CmdSubmit);
            Add("career", "abandon", "", "abandon <job#>", "Drop an accepted contract (costs some reputation).", CmdAbandon);

            Add("workstation", "rig", "gateway,hw", "rig", "Show your workstation hardware and what each part does.", (a, r) => CmdRig());
            Add("workstation", "tools", "", "tools", "List the software you own.", (a, r) => CmdTools());
            Add("workstation", "shop", "store", "shop", "Browse tools and hardware upgrades.", (a, r) => CmdShop());
            Add("workstation", "buy", "", "buy <tool>", "Buy a tool, or the next version of one you own.", CmdBuy);
            Add("workstation", "upgrade", "", "upgrade <cpu|ram|nic|hdd|cloak>", "Upgrade a workstation component.", CmdUpgrade);
            Add("workstation", "ps", "", "ps", "List running processes.", (a, r) => CmdPs());
            Add("workstation", "kill", "", "kill <pid>", "Stop a running process.", CmdKill);

            Add("network", "net", "map,hosts", "net", "List every system you know about.", (a, r) => CmdNet());
            Add("network", "bounce", "route", "bounce [add <ip>|rm <ip>|clear|auto]", "Manage your bounce route. Each hop slows traces.", CmdBounce);
            Add("network", "connect", "ssh", "connect <ip>", "Connect to a system through your bounce route.", CmdConnect);
            Add("network", "disconnect", "dc,exit,logout", "disconnect", "Drop the current connection (stops the trace).", (a, r) => { if (Conn == null) Err("Not connected."); else Disconnect(true); });
            Add("network", "probe", "scan", "probe", "Inspect the defenses of the connected system.", (a, r) => CmdProbe());
            Add("network", "logs", "", "logs", "Read the connected system's access log.", (a, r) => CmdLogs());

            Add("intrusion", "crack", "pwcrack", "crack", "Crack the login password (needs proxy & firewall down).", (a, r) => StartBreach(ProcKind.Crack));
            Add("intrusion", "fwbypass", "", "fwbypass", "Bypass the firewall.", (a, r) => StartBreach(ProcKind.FwBypass));
            Add("intrusion", "pxbypass", "", "pxbypass", "Bypass the authentication proxy.", (a, r) => StartBreach(ProcKind.PxBypass));
            Add("intrusion", "monbypass", "", "monbypass", "Blind the intrusion monitor so traces run slower.", (a, r) => StartBreach(ProcKind.MonBypass));
            Add("intrusion", "logwipe", "", "logwipe", "Erase your entries from the access log.", (a, r) => CmdLogWipe());

            Add("remote", "ls", "dir", "ls", "List files on the connected system.", (a, r) => CmdLs());
            Add("remote", "download", "dl,get", "download <file>", "Copy a remote file to local storage.", CmdDownload);
            Add("remote", "upload", "put", "upload <file>", "Copy a local file onto the connected system.", CmdUpload);
            Add("remote", "delete", "del", "delete <file>", "Delete a file on the connected system.", CmdDelete);
            Add("remote", "db", "records", "db [record] | db edit <record> <field> <value>", "Browse or edit the connected system's records.", CmdDb);

            Add("local", "local", "files,storage", "local", "List files in your local storage.", (a, r) => CmdLocal());
            Add("local", "rm", "", "rm <file>", "Delete a file from local storage.", CmdRm);
            Add("local", "decrypt", "", "decrypt <file>", "Decrypt a file in local storage (safe: runs offline).", CmdDecrypt);
        }

        // ------------------------------------------------------------------ entry point

        public void Exec(string raw)
        {
            raw = (raw ?? "").Trim();

            if (Mode == Mode.AskHandle)
            {
                Print(Prompt + Tx.W(Tx.Safe(raw)));
                if (raw.Length < 2 || raw.Length > 16 || !raw.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
                {
                    Err("Handle must be 2-16 characters: letters, digits, _ or -.");
                    return;
                }
                NewGame(raw);
                return;
            }

            if (Mode == Mode.GameOver)
            {
                Outbox.Enqueue(ClearSignal);
                PrintBanner();
                AskHandle();
                return;
            }

            Print(Prompt + Tx.W(Tx.Safe(raw)));
            if (raw.Length == 0) return;

            var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string word = parts[0].ToLowerInvariant();
            string rest = raw.Substring(parts[0].Length).Trim();
            var args = parts.Skip(1).ToArray();

            Cmd cmd;
            if (!lookup.TryGetValue(word, out cmd))
            {
                Err("Unknown command '" + Tx.Safe(word) + "'. Type `help`.");
                return;
            }
            used.Add(cmd.name);
            cmd.run(args, rest);
            if (Mode == Mode.Playing && tutorial != null) tutorial.Check();
        }

        // ------------------------------------------------------------------ general

        void CmdHelp(string[] a, string rest)
        {
            if (a.Length > 0)
            {
                Cmd c;
                if (!lookup.TryGetValue(a[0].ToLowerInvariant(), out c)) { Err("No such command."); return; }
                Print(Tx.Cy(c.usage));
                Print("  " + c.help);
                if (c.aliases.Length > 0) Print(Tx.D("  aliases: " + string.Join(", ", c.aliases)));
                return;
            }
            foreach (var cat in Categories)
            {
                Print(Tx.W(cat.ToUpperInvariant()));
                foreach (var c in commands.Where(x => x.cat == cat))
                    Print("  " + Tx.Pad(Tx.Cy(c.usage), 44) + Tx.D(c.help));
            }
            Print(Tx.D("Tab completes commands, IPs and file names. Up/Down recalls history. PgUp/PgDn scrolls."));
        }

        void CmdStatus()
        {
            int rank = Rank;
            Print(Tx.W("CAREER"));
            Print("  handle ......... " + Tx.G(P.handle) + Tx.D("   gateway " + P.gatewayIp));
            Print("  credits ........ " + Tx.Y(Tx.Money(P.credits)));
            Print("  rank ........... " + Tx.Cy(Balance.RankTitle(rank)) + Tx.D(" (rank " + rank + ")"));
            Print("  reputation ..... " + P.rep + Tx.D(" / next rank at " + Balance.RankThreshold(rank + 1)));
            Print("  tool cap ....... v" + Balance.MaxToolVersion(rank));
            Print("  contracts done . " + P.jobsDone + Tx.D("   systems breached " + P.nodesBreached + "   lifetime earnings " + Tx.Money(P.totalEarned)));
            if (D.investigations.Count == 0) Print("  forensics ...... " + Tx.G("none open"));
            foreach (var inv in D.investigations)
            {
                var n = Node(inv.nodeId);
                Print("  forensics ...... " + Tx.R((n != null ? n.name + " (" + n.ip + ")" : "?") + " in " + Tx.Clock(inv.ends - D.time) + ", risk " + Tx.Pct(inv.chance)));
            }
        }

        void CmdTutorial(string[] a, string rest)
        {
            if (!tutorial.Active) { Print(Tx.D("Tutorial already finished. `help` lists everything.")); return; }
            if (a.Length > 0 && a[0].ToLowerInvariant() == "skip") { tutorial.Skip(); return; }
            tutorial.Show();
        }

        // ------------------------------------------------------------------ career

        MissionData FindMission(List<MissionData> list, string token)
        {
            int id;
            if (token == null || !int.TryParse(token.TrimStart('#'), out id)) return null;
            return list.FirstOrDefault(m => m.id == id);
        }

        string Difficulty(int tier)
        {
            int rank = Rank;
            if (tier < rank) return Tx.D("easy");
            if (tier == rank) return Tx.G("fair");
            return Tx.R("hard");
        }

        string TypeLabel(MissionType t)
        {
            switch (t)
            {
                case MissionType.Steal: return Tx.Cy("STEAL ");
                case MissionType.Delete: return Tx.R("DELETE");
                case MissionType.Plant: return Tx.M("PLANT ");
                default: return Tx.Y("ALTER ");
            }
        }

        void CmdJobs()
        {
            Print(Tx.W("CONTRACT BOARD") + Tx.D("  (" + Balance.RankTitle(Rank) + ", " + D.active.Count + "/" + Balance.MaxActiveJobs + " accepted)"));
            var list = D.board.OrderBy(m => m.tutorial ? 0 : 1).ThenBy(m => m.tier).ThenByDescending(m => m.payout).ToList();
            if (list.Count == 0) { Print(Tx.D("  Nothing posted. Check back soon.")); return; }
            foreach (var m in list)
            {
                var n = Node(m.nodeId);
                string left = m.tutorial ? "--:--" : Tx.Clock(m.expires - D.time);
                Print("  " + Tx.Pad(Tx.W("#" + m.id), 6) + Tx.Pad(Difficulty(m.tier) + Tx.D(" t" + m.tier), 10) + TypeLabel(m.type) +
                      Tx.PadL(Tx.Y(Tx.Money(m.payout)), 11) + Tx.Pad(Tx.D(" +" + m.rep + "rep"), 9) + Tx.Pad(Tx.D(left), 7) + WorldGen.Describe(m, n));
            }
            Print(Tx.D("  `accept <#>` to take a job. Harder jobs pay more but need better tools."));
        }

        void CmdAccept(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: accept <job#>"); return; }
            var m = FindMission(D.board, a[0]);
            if (m == null) { Err("No contract #" + Tx.Safe(a[0]) + " on the board."); return; }
            if (D.active.Count >= Balance.MaxActiveJobs) { Err("You already hold " + Balance.MaxActiveJobs + " contracts. Finish or `abandon` one."); return; }
            if (m.type == MissionType.Plant && StorageUsed() + m.fileSize > Balance.StorageCap(P.storage))
            {
                Err("No room for the client's payload (" + m.fileSize + " MB). Free some storage.");
                return;
            }

            D.board.Remove(m);
            D.active.Add(m);
            if (!m.tutorial) m.expires = D.time + 900 + 300 * m.tier;
            var n = Node(m.nodeId);
            n.known = true;
            if (m.type == MissionType.Plant)
                P.files.Add(new LocalFile { name = m.fileName, size = m.fileSize, missionId = m.id });

            Ok("Contract #" + m.id + " accepted from " + Tx.W(m.client) + ".");
            Print("  " + WorldGen.Describe(m, n));
            Print("  Target: " + Tx.Y(n.ip) + Tx.D("  (" + n.name + ")"));
            if (m.type == MissionType.Plant) Print(Tx.D("  The client sent ") + m.fileName + Tx.D(" to your local storage."));
            if (!m.tutorial) Print(Tx.D("  Deadline: " + Tx.Clock(m.expires - D.time)));
            RefillBoard();
            Save();
        }

        void CmdMissions()
        {
            if (D.active.Count == 0) { Print(Tx.D("No accepted contracts. See `jobs`.")); return; }
            foreach (var m in D.active)
            {
                var n = Node(m.nodeId);
                Print(Tx.W("#" + m.id) + " " + TypeLabel(m.type) + " " + WorldGen.Describe(m, n));
                Print("     target " + Tx.Y(n != null ? n.ip : "?") + Tx.D("   pays ") + Tx.Y(Tx.Money(m.payout)) + Tx.D(" +" + m.rep + " rep") +
                      (m.tutorial ? "" : Tx.D("   deadline " + Tx.Clock(m.expires - D.time))));
                string why;
                Print("     " + (MissionReady(m, out why) ? Tx.G("READY - `submit " + m.id + "`") : Tx.D("todo: " + why)));
            }
        }

        bool MissionReady(MissionData m, out string why)
        {
            var n = Node(m.nodeId);
            why = "";
            switch (m.type)
            {
                case MissionType.Steal:
                {
                    var lf = P.files.FirstOrDefault(f => f.name == m.fileName && f.fromNode == m.nodeId);
                    if (lf == null) { why = "download " + m.fileName + " from " + (n != null ? n.ip : "the target"); return false; }
                    if (lf.encryption > 0) { why = "decrypt " + m.fileName + " (lvl " + lf.encryption + ")"; return false; }
                    return true;
                }
                case MissionType.Delete:
                    if (n != null && n.files.Any(f => f.name == m.fileName)) { why = "delete " + m.fileName + " on " + n.ip; return false; }
                    return true;
                case MissionType.Plant:
                    if (n == null || !n.files.Any(f => f.name == m.fileName)) { why = "upload " + m.fileName + " to " + (n != null ? n.ip : "the target"); return false; }
                    return true;
                default:
                {
                    var rec = n?.records.FirstOrDefault(x => x.id == m.recordId);
                    var fld = rec?.fields.FirstOrDefault(x => x.key == m.field);
                    if (fld == null || !string.Equals(fld.value.Trim(), m.value, StringComparison.OrdinalIgnoreCase))
                    {
                        why = "db edit " + m.recordId + " " + m.field + " " + m.value;
                        return false;
                    }
                    return true;
                }
            }
        }

        void CmdSubmit(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: submit <job#>"); return; }
            var m = FindMission(D.active, a[0]);
            if (m == null) { Err("You haven't accepted contract #" + Tx.Safe(a[0]) + ". See `missions`."); return; }
            string why;
            if (!MissionReady(m, out why)) { Err("Not done yet: " + why + "."); return; }

            if (m.type == MissionType.Steal) P.files.RemoveAll(f => f.name == m.fileName && f.fromNode == m.nodeId);
            D.active.Remove(m);
            int before = Rank;
            P.credits += m.payout;
            P.totalEarned += m.payout;
            P.rep += m.rep;
            P.jobsDone++;
            if (m.tutorial) D.tutorialMissionDone = true;

            Ok("Contract #" + m.id + " complete. " + Tx.W(m.client) + " transferred " + Tx.Y(Tx.Money(m.payout)) + Tx.G(". +" + m.rep + " rep."));
            int after = Rank;
            if (after > before) RankUp(before, after);
            Prune(m.nodeId);
            RefillBoard();
            Save();
        }

        void RankUp(int before, int after)
        {
            Print();
            Print(Tx.M(">>> RANK UP: you are now " + Balance.RankTitle(after).ToUpperInvariant() + " <<<"));
            Print(Tx.M("    Tool versions up to v" + Balance.MaxToolVersion(after) + " unlocked. Harder, better-paying contracts are on the board."));
            foreach (var t in Balance.Tools.Where(t => t.unlockRank > before && t.unlockRank <= after))
                Print(Tx.M("    New tool available: " + t.name + " (`buy " + t.key + "`)"));
            Print();
        }

        void CmdAbandon(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: abandon <job#>"); return; }
            var m = FindMission(D.active, a[0]);
            if (m == null) { Err("You haven't accepted contract #" + Tx.Safe(a[0]) + "."); return; }
            if (m.tutorial) { Err("Finish the training contract first - or `tutorial skip`."); return; }
            D.active.Remove(m);
            int loss = Math.Min(P.rep, Math.Max(2, m.rep / 3));
            P.rep -= loss;
            P.files.RemoveAll(f => f.missionId == m.id);
            Print(Tx.Y("Contract #" + m.id + " abandoned. -" + loss + " rep."));
            Prune(m.nodeId);
            Save();
        }

        // ------------------------------------------------------------------ workstation

        int HwLevel(HwId id)
        {
            switch (id)
            {
                case HwId.Cpu: return P.cpu;
                case HwId.Ram: return P.ram;
                case HwId.Nic: return P.nic;
                case HwId.Storage: return P.storage;
                default: return P.cloak;
            }
        }

        string HwEffect(HwId id, int lvl)
        {
            switch (id)
            {
                case HwId.Cpu: return "tools run " + Tx.F2(Balance.CpuMult(lvl)) + "x";
                case HwId.Ram: return Balance.RamSlots(lvl) + " processes";
                case HwId.Nic: return Tx.F1(Balance.Bandwidth(lvl)) + " MB/s, " + Balance.MaxBounces(lvl) + " bounces";
                case HwId.Storage: return Balance.StorageCap(lvl) + " MB";
                default: return "traces " + Tx.F2(Balance.CloakMult(lvl)) + "x slower";
            }
        }

        void CmdRig()
        {
            Print(Tx.W("WORKSTATION") + Tx.D("  gateway " + P.gatewayIp));
            foreach (var h in Balance.Hardware)
            {
                int l = HwLevel(h.id);
                Print("  " + Tx.Pad(Tx.Cy(h.key), 7) + Tx.Pad(h.name, 14) + Tx.Pad(Tx.W("L" + l), 5) + Tx.Pad(Tx.G(HwEffect(h.id, l)), 30) +
                      Tx.D("next: " + HwEffect(h.id, l + 1) + " for ") + Tx.Y(Tx.Money(Balance.HwPrice(h.id, l + 1))));
            }
            Print(Tx.D("  storage used " + StorageUsed() + "/" + Balance.StorageCap(P.storage) + " MB.  `upgrade <part>` to improve."));
        }

        void CmdTools()
        {
            Print(Tx.W("TOOLKIT"));
            foreach (var t in Balance.Tools)
            {
                int v = ToolVersion(t.id);
                if (v == 0) continue;
                Print("  " + Tx.Pad(Tx.Cy(t.key), 11) + Tx.Pad(t.name, 18) + Tx.Pad(Tx.W("v" + v), 5) + Tx.D(t.desc));
            }
            Print(Tx.D("  `shop` to buy new tools and versions."));
        }

        void CmdShop()
        {
            int rank = Rank;
            int cap = Balance.MaxToolVersion(rank);
            Print(Tx.W("SOFTWARE") + Tx.D("  (your rank allows up to v" + cap + ")"));
            foreach (var t in Balance.Tools)
            {
                int v = ToolVersion(t.id);
                string owned = v == 0 ? Tx.D("-") : Tx.W("v" + v);
                string offer;
                if (t.unlockRank > rank) offer = Tx.D("locked - reach " + Balance.RankTitle(t.unlockRank));
                else if (v + 1 > cap) offer = Tx.D("v" + (v + 1) + " needs a higher rank");
                else
                {
                    long price = Balance.ToolPrice(t.id, v + 1);
                    offer = (v == 0 ? "buy v1" : "v" + (v + 1)) + " " + (P.credits >= price ? Tx.Y(Tx.Money(price)) : Tx.R(Tx.Money(price)));
                }
                Print("  " + Tx.Pad(Tx.Cy(t.key), 11) + Tx.Pad(t.name, 18) + Tx.Pad(owned, 5) + Tx.Pad(offer, 26) + Tx.D(t.blurb));
            }
            Print(Tx.W("HARDWARE"));
            foreach (var h in Balance.Hardware)
            {
                int l = HwLevel(h.id);
                long price = Balance.HwPrice(h.id, l + 1);
                Print("  " + Tx.Pad(Tx.Cy(h.key), 11) + Tx.Pad(h.name, 18) + Tx.Pad(Tx.W("L" + l), 5) +
                      Tx.Pad("L" + (l + 1) + " " + (P.credits >= price ? Tx.Y(Tx.Money(price)) : Tx.R(Tx.Money(price))), 26) + Tx.D(h.desc));
            }
            Print(Tx.D("  You have ") + Tx.Y(Tx.Money(P.credits)) + Tx.D(".  `buy <tool>`  `upgrade <part>`"));
        }

        void CmdBuy(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: buy <tool>. See `shop`."); return; }
            var t = Balance.FindTool(a[0]);
            if (t == null)
            {
                if (Balance.FindHw(a[0]) != null) { CmdUpgrade(a, rest); return; }
                Err("No such tool. See `shop`.");
                return;
            }
            int rank = Rank;
            if (t.unlockRank > rank) { Err(t.name + " unlocks at rank " + Balance.RankTitle(t.unlockRank) + "."); return; }
            int v = ToolVersion(t.id);
            int cap = Balance.MaxToolVersion(rank);
            if (v + 1 > cap) { Err(t.name + " v" + (v + 1) + " needs a higher rank (your cap is v" + cap + ")."); return; }
            long price = Balance.ToolPrice(t.id, v + 1);
            if (P.credits < price) { Err("Not enough credits: " + t.name + " v" + (v + 1) + " costs " + Tx.Money(price) + "."); return; }
            P.credits -= price;
            var tl = P.tools.FirstOrDefault(x => x.id == t.id);
            if (tl == null) P.tools.Add(tl = new ToolLevel { id = t.id });
            tl.version = v + 1;
            Ok("Installed " + t.name + " v" + tl.version + " for " + Tx.Money(price) + ".");
            Save();
        }

        void CmdUpgrade(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: upgrade <cpu|ram|nic|hdd|cloak>"); return; }
            var h = Balance.FindHw(a[0]);
            if (h == null) { Err("No such component. Options: cpu, ram, nic, hdd, cloak."); return; }
            int l = HwLevel(h.id);
            long price = Balance.HwPrice(h.id, l + 1);
            if (P.credits < price) { Err("Not enough credits: " + h.name + " L" + (l + 1) + " costs " + Tx.Money(price) + "."); return; }
            P.credits -= price;
            switch (h.id)
            {
                case HwId.Cpu: P.cpu++; break;
                case HwId.Ram: P.ram++; break;
                case HwId.Nic: P.nic++; EnsureRelays(); break;
                case HwId.Storage: P.storage++; break;
                default: P.cloak++; break;
            }
            Ok(h.name + " upgraded to L" + (l + 1) + ": " + HwEffect(h.id, l + 1) + ".");
            Save();
        }

        void CmdPs()
        {
            if (Procs.Count == 0) { Print(Tx.D("No running processes. (" + Balance.RamSlots(P.ram) + " slots)")); return; }
            foreach (var p in Procs)
                Print("  " + Tx.Pad(Tx.W(p.pid.ToString()), 6) + Tx.Pad(Tx.Cy(p.label), 36) + Tx.Pct(p.Progress) + Tx.D("  " + Tx.Clock(p.Remaining) + " left"));
            Print(Tx.D("  " + Procs.Count + "/" + Balance.RamSlots(P.ram) + " slots in use."));
        }

        void CmdKill(string[] a, string rest)
        {
            int pid;
            if (a.Length == 0 || !int.TryParse(a[0], out pid)) { Err("Usage: kill <pid>"); return; }
            var p = Procs.FirstOrDefault(x => x.pid == pid);
            if (p == null) { Err("No process " + pid + "."); return; }
            Procs.Remove(p);
            Print(Tx.D("Killed [" + pid + "] " + p.label + "."));
        }

        // ------------------------------------------------------------------ network

        void CmdNet()
        {
            Print(Tx.W("KNOWN SYSTEMS"));
            var jobs = new HashSet<string>(D.active.Select(m => m.nodeId));
            foreach (var n in D.nodes.Where(x => x.known).OrderBy(x => x.kind == NodeKind.Relay ? 1 : 0).ThenBy(x => x.tier))
            {
                var tags = new List<string>();
                if (n.kind == NodeKind.Relay) tags.Add(Tx.D("relay"));
                if (jobs.Contains(n.id)) tags.Add(Tx.Y("JOB"));
                if (n.passwordKnown) tags.Add(Tx.G("ACCESS"));
                if (P.bounces.Contains(n.id)) tags.Add(Tx.Cy("BOUNCE"));
                if (D.investigations.Any(i => i.nodeId == n.id)) tags.Add(Tx.R("FORENSICS"));
                if (Conn == n) tags.Add(Tx.M("CONNECTED"));
                Print("  " + Tx.Pad(Tx.Y(n.ip), 17) + Tx.Pad(n.name, 36) + Tx.Pad(Tx.D(NameGen.KindLabel(n.kind)), 10) +
                      Tx.Pad(Tx.D("t" + n.tier), 5) + string.Join(" ", tags));
            }
            Print(Tx.D("  `connect <ip>` to connect. Relays and systems you've breached can be used with `bounce add <ip>`."));
        }

        void CmdBounce(string[] a, string rest)
        {
            int max = Balance.MaxBounces(P.nic);
            string sub = a.Length > 0 ? a[0].ToLowerInvariant() : "list";
            if (sub != "list" && Conn != null) { Err("Can't reroute while connected. Disconnect first."); return; }
            switch (sub)
            {
                case "add":
                {
                    if (a.Length < 2) { Err("Usage: bounce add <ip>"); return; }
                    var n = NodeByIp(a[1]);
                    if (n == null || !n.known) { Err("Unknown host " + Tx.Safe(a[1]) + "."); return; }
                    if (n.kind == NodeKind.Training) { Err("The training server can't route traffic."); return; }
                    if (!HasAccess(n)) { Err("You need access to " + n.ip + " to route through it. Use relays or systems you've breached."); return; }
                    if (P.bounces.Contains(n.id)) { Err("Already in your route."); return; }
                    if (P.bounces.Count >= max) { Err("Your network card supports " + max + " bounces. `upgrade nic` for more."); return; }
                    P.bounces.Add(n.id);
                    Ok("Added " + n.ip + " (" + n.name + ") as hop " + P.bounces.Count + ".");
                    break;
                }
                case "rm":
                case "remove":
                {
                    if (a.Length < 2) { Err("Usage: bounce rm <ip>"); return; }
                    var n = NodeByIp(a[1]);
                    if (n == null || !P.bounces.Remove(n.id)) { Err("That host isn't in your route."); return; }
                    Ok("Removed " + n.ip + " from your route.");
                    break;
                }
                case "clear":
                    P.bounces.Clear();
                    Ok("Route cleared. You will connect directly - traces will be fast.");
                    break;
                case "auto":
                {
                    P.bounces.Clear();
                    foreach (var n in D.nodes.Where(x => x.kind == NodeKind.Relay && x.known).Take(max)) P.bounces.Add(n.id);
                    Ok("Route set through " + P.bounces.Count + " public relay(s).");
                    break;
                }
                case "list":
                    break;
                default:
                    Err("Usage: bounce [add <ip>|rm <ip>|clear|auto]");
                    return;
            }
            PrintRoute(max);
        }

        void PrintRoute(int max)
        {
            var hops = P.bounces.Select(Node).Where(n => n != null).ToList();
            string path = Tx.Cy("gateway") + Tx.D(" -> ");
            foreach (var h in hops) path += Tx.Y(h.ip) + Tx.D(" -> ");
            path += Tx.W("target");
            Print("  Route: " + path);
            Print(Tx.D("  " + hops.Count + "/" + max + " hops. Traces run " + Tx.F2(Balance.BounceMult(hops.Count)) + "x slower through this route."));
        }

        void CmdConnect(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: connect <ip>"); return; }
            if (Conn != null) { Err("Already connected to " + Conn.ip + ". Disconnect first."); return; }
            var n = NodeByIp(a[0]);
            if (n == null || !n.known) { Err("No route to host " + Tx.Safe(a[0]) + ". Target addresses appear when you accept a job."); return; }
            if (P.bounces.Contains(n.id)) { Err("That host is part of your bounce route. Remove it first."); return; }
            Connect(n);
            string path = Tx.Cy("gateway");
            foreach (var id in Route) path += Tx.D(" -> ") + Tx.Y(Node(id).ip);
            path += Tx.D(" -> ") + Tx.W(n.ip);
            Print(Tx.D("Routing: ") + path);
            Ok("Connected to " + n.name + ".");
            if (n.isPublic) Print(Tx.D("Public relay - nothing to steal here."));
            else if (n.passwordKnown) Ok("Stored credentials accepted. Logged in as admin.");
            else Print(Tx.D("Login required. `probe` to inspect its defenses."));
        }

        void CmdProbe()
        {
            var n = Conn;
            if (n == null) { Err("Not connected. `connect <ip>` first."); return; }
            Print(Tx.W("PROBE ") + Tx.Y(n.ip) + " " + n.name + Tx.D("  [" + NameGen.KindLabel(n.kind) + ", tier " + n.tier + "]"));
            if (n.isPublic) { Print(Tx.D("  Open public relay. No defenses.")); return; }
            ProbeLayer("Proxy", n.proxy, n.proxyDown, ToolId.PxBypass, "pxbypass");
            ProbeLayer("Firewall", n.firewall, n.firewallDown, ToolId.FwBypass, "fwbypass");
            ProbeLayer("Monitor", n.monitor, n.monitorDown, ToolId.MonBypass, "monbypass");
            if (n.passwordKnown) Print("  " + Tx.Pad("Password", 10) + Tx.Pad("lvl " + n.password, 8) + Tx.G("CRACKED"));
            else ProbeLayer("Password", n.password, false, ToolId.PwCrack, "crack");
            if (n.traceTime <= 0) Print("  " + Tx.Pad("Trace", 10) + Tx.D("none (training system)"));
            else
                Print("  " + Tx.Pad("Trace", 10) + "full trace in " + Tx.Y(Tx.Clock(TraceDuration(n, Route.Count))) +
                      Tx.D(" via " + Route.Count + " hop(s), cloak L" + P.cloak + (n.monitor > 0 && !n.monitorDown ? ", monitor active" : "")));
            int enc = n.files.Count(f => f.encryption > 0);
            if (enc > 0 && HasAccess(n)) Print("  " + Tx.Pad("Files", 10) + enc + " encrypted (max lvl " + n.files.Max(f => f.encryption) + ")");
        }

        void ProbeLayer(string label, int level, bool down, ToolId tool, string cmd)
        {
            if (level <= 0) { Print("  " + Tx.Pad(label, 10) + Tx.D("none")); return; }
            string state = down ? Tx.G("BYPASSED") : Tx.R(label == "Password" ? "LOCKED" : "ACTIVE");
            int v = ToolVersion(tool);
            string need = "";
            if (!down)
            {
                var info = Balance.Tool(tool);
                if (v == 0) need = Tx.R("you need " + info.name + " v" + level + "+");
                else if (v < level) need = Tx.R(info.key + " v" + v + " too weak, need v" + level);
                else need = Tx.G("`" + cmd + "`") + Tx.D(" ~" + Tx.Clock(Balance.BreachTime(level, v, P.cpu)));
            }
            Print("  " + Tx.Pad(label, 10) + Tx.Pad("lvl " + level, 8) + Tx.Pad(state, 10) + need);
        }

        void CmdLogs()
        {
            var n = Conn;
            if (n == null) { Err("Not connected."); return; }
            if (!HasAccess(n)) { Err("Access denied. Crack the password first."); return; }
            Print(Tx.W("ACCESS LOG ") + Tx.Y(n.ip));
            foreach (var l in n.logs.Skip(Math.Max(0, n.logs.Count - 15)))
            {
                string t = l.time < 0 ? "-" + Tx.Clock(-l.time) : "+" + Tx.Clock(l.time);
                string txt = l.byPlayer ? (l.suspicious ? Tx.R(l.text) : Tx.Y(l.text)) : Tx.D(l.text);
                Print("  " + Tx.Pad(Tx.D(t), 10) + txt);
            }
            int mine = n.logs.Count(l => l.byPlayer);
            Print(mine > 0 ? Tx.Y("  " + mine + " entr" + (mine == 1 ? "y" : "ies") + " point at you. `logwipe` before you leave.") : Tx.G("  No trace of you here."));
        }

        // ------------------------------------------------------------------ intrusion

        void StartBreach(ProcKind kind)
        {
            var n = Conn;
            if (n == null) { Err("Not connected to anything. `connect <ip>` first."); return; }
            if (n.isPublic) { Err("This is a public relay - there's nothing to break."); return; }

            int level;
            ToolId tool;
            string layer;
            switch (kind)
            {
                case ProcKind.FwBypass:
                    tool = ToolId.FwBypass; layer = "firewall"; level = n.firewall;
                    if (level == 0) { Err("No firewall on this system."); return; }
                    if (n.firewallDown) { Err("Firewall already bypassed."); return; }
                    break;
                case ProcKind.PxBypass:
                    tool = ToolId.PxBypass; layer = "proxy"; level = n.proxy;
                    if (level == 0) { Err("No proxy on this system."); return; }
                    if (n.proxyDown) { Err("Proxy already bypassed."); return; }
                    break;
                case ProcKind.MonBypass:
                    tool = ToolId.MonBypass; layer = "monitor"; level = n.monitor;
                    if (level == 0) { Err("No intrusion monitor on this system."); return; }
                    if (n.monitorDown) { Err("Monitor already blinded."); return; }
                    break;
                default:
                    tool = ToolId.PwCrack; layer = "password"; level = n.password;
                    if (HasAccess(n)) { Err("You already have access."); return; }
                    if (n.proxy > 0 && !n.proxyDown) { Err("The proxy rejects your login attempts. `pxbypass` first."); return; }
                    if (n.firewall > 0 && !n.firewallDown) { Err("The firewall blocks the cracker. `fwbypass` first."); return; }
                    break;
            }

            var info = Balance.Tool(tool);
            int v = ToolVersion(tool);
            if (v == 0) { Err("You don't own " + info.name + ". See `shop`."); return; }
            if (v < level) { Err(info.name + " v" + v + " can't beat a level " + level + " " + layer + ". You need v" + level + "+."); return; }
            if (Procs.Any(p => p.kind == kind && p.nodeId == n.id)) { Err(info.name + " is already running."); return; }
            if (!RamFree()) return;

            double t = Balance.BreachTime(level, v, P.cpu);
            Hostile(layer + " attack");
            var proc = StartProc(kind, info.key + " v" + v + " -> " + layer, t, n.id);
            Print(Tx.D("[" + proc.pid + "] ") + "Running " + info.name + " v" + v + " against " + layer + " (lvl " + level + "). ETA " + Tx.Clock(t) + ".");
        }

        void CmdLogWipe()
        {
            var n = Conn;
            if (n == null) { Err("Not connected."); return; }
            if (!HasAccess(n)) { Err("You need admin access to touch the logs. Crack the password first."); return; }
            int v = ToolVersion(ToolId.LogWipe);
            if (v == 0) { Err("You don't own a Log Wiper."); return; }
            if (Procs.Any(p => p.kind == ProcKind.LogWipe && p.nodeId == n.id)) { Err("Already wiping."); return; }
            if (!RamFree()) return;
            int entries = n.logs.Count(l => l.byPlayer) + 1;
            double t = Balance.LogWipeTime(entries, v, P.cpu);
            Hostile("log tampering");
            var proc = StartProc(ProcKind.LogWipe, "logwipe v" + v + " -> " + n.ip, t, n.id);
            Print(Tx.D("[" + proc.pid + "] ") + "Scrubbing " + entries + " log entr" + (entries == 1 ? "y" : "ies") + ". ETA " + Tx.Clock(t) + ".");
        }

        // ------------------------------------------------------------------ remote files

        bool RemoteReady()
        {
            if (Conn == null) { Err("Not connected."); return false; }
            if (!HasAccess(Conn)) { Err("Access denied. Crack the password first."); return false; }
            return true;
        }

        void CmdLs()
        {
            if (!RemoteReady()) return;
            var n = Conn;
            if (n.files.Count == 0) { Print(Tx.D("(no files)")); return; }
            var targets = new HashSet<string>(D.active.Where(m => m.nodeId == n.id && m.fileName != null).Select(m => m.fileName));
            foreach (var f in n.files.OrderBy(f => f.name))
                Print("  " + Tx.Pad(targets.Contains(f.name) ? Tx.Y(f.name) : Tx.W(f.name), 30) + Tx.PadL(f.size + " MB", 7) +
                      (f.encryption > 0 ? Tx.M("  encrypted lvl " + f.encryption) : "") + (targets.Contains(f.name) ? Tx.Y("  <- contract") : ""));
            if (n.records.Count > 0) Print(Tx.D("  This system also holds " + n.records.Count + " records. `db` to browse."));
        }

        FileData RemoteFile(string name)
        {
            return Conn.files.FirstOrDefault(f => f.name == name) ??
                   Conn.files.FirstOrDefault(f => f.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        void CmdDownload(string[] a, string rest)
        {
            if (!RemoteReady()) return;
            if (a.Length == 0) { Err("Usage: download <file>"); return; }
            var f = RemoteFile(a[0]);
            if (f == null) { Err("No such file. `ls` to list."); return; }
            if (Procs.Any(p => p.kind == ProcKind.Download && p.arg == f.name)) { Err("Already downloading that."); return; }
            int free = Balance.StorageCap(P.storage) - StorageUsed() - Procs.Where(p => p.kind == ProcKind.Download).Sum(p => RemoteSize(p.arg));
            if (f.size > free) { Err("Not enough local storage (" + f.size + " MB needed, " + free + " free). `rm` something or `upgrade hdd`."); return; }
            if (!RamFree()) return;
            double t = Balance.TransferTime(f.size, P.nic);
            Hostile("file copy " + f.name);
            var p2 = StartProc(ProcKind.Download, "download " + f.name, t, Conn.id, f.name);
            Print(Tx.D("[" + p2.pid + "] ") + "Downloading " + f.name + " (" + f.size + " MB). ETA " + Tx.Clock(t) + ".");
        }

        int RemoteSize(string name)
        {
            var f = Conn?.files.FirstOrDefault(x => x.name == name);
            return f != null ? f.size : 0;
        }

        void CmdUpload(string[] a, string rest)
        {
            if (!RemoteReady()) return;
            if (a.Length == 0) { Err("Usage: upload <file>"); return; }
            var lf = LocalByName(a[0]);
            if (lf == null) { Err("No such local file. `local` to list."); return; }
            if (Procs.Any(p => p.kind == ProcKind.Upload && p.arg == lf.name)) { Err("Already uploading that."); return; }
            if (!RamFree()) return;
            double t = Balance.TransferTime(lf.size, P.nic);
            Hostile("file write " + lf.name);
            var p2 = StartProc(ProcKind.Upload, "upload " + lf.name, t, Conn.id, lf.name);
            Print(Tx.D("[" + p2.pid + "] ") + "Uploading " + lf.name + ". ETA " + Tx.Clock(t) + ".");
        }

        void CmdDelete(string[] a, string rest)
        {
            if (!RemoteReady()) return;
            if (a.Length == 0) { Err("Usage: delete <file>"); return; }
            var f = RemoteFile(a[0]);
            if (f == null) { Err("No such file."); return; }
            if (Procs.Any(p => p.kind == ProcKind.Delete && p.arg == f.name)) { Err("Already deleting that."); return; }
            if (!RamFree()) return;
            double t = Balance.DeleteTime(P.cpu);
            Hostile("file delete " + f.name);
            var p2 = StartProc(ProcKind.Delete, "delete " + f.name, t, Conn.id, f.name);
            Print(Tx.D("[" + p2.pid + "] ") + "Shredding " + f.name + ". ETA " + Tx.Clock(t) + ".");
        }

        void CmdDb(string[] a, string rest)
        {
            if (!RemoteReady()) return;
            var n = Conn;
            if (n.records.Count == 0) { Err("No database on this system."); return; }

            if (a.Length == 0)
            {
                Print(Tx.W("RECORDS ") + Tx.D(n.name));
                foreach (var r in n.records)
                    Print("  " + Tx.Pad(Tx.Cy(r.id), 9) + Tx.Pad(r.name, 22) + Tx.D(string.Join("  ", r.fields.Select(f => f.key + "=" + Tx.Safe(f.value)))));
                Print(Tx.D("  `db edit <record> <field> <value>` to change a value."));
                return;
            }

            if (a[0].ToLowerInvariant() != "edit")
            {
                var rec = n.records.FirstOrDefault(x => x.id.Equals(a[0], StringComparison.OrdinalIgnoreCase));
                if (rec == null) { Err("No record " + Tx.Safe(a[0]) + "."); return; }
                Print(Tx.Cy(rec.id) + " " + Tx.W(rec.name));
                foreach (var f in rec.fields) Print("  " + Tx.Pad(f.key, 12) + Tx.Safe(f.value));
                return;
            }

            if (a.Length < 4) { Err("Usage: db edit <record> <field> <value>"); return; }
            var target = n.records.FirstOrDefault(x => x.id.Equals(a[1], StringComparison.OrdinalIgnoreCase));
            if (target == null) { Err("No record " + Tx.Safe(a[1]) + "."); return; }
            var fld = target.fields.FirstOrDefault(x => x.key.Equals(a[2], StringComparison.OrdinalIgnoreCase));
            if (fld == null) { Err("Record " + target.id + " has no field '" + Tx.Safe(a[2]) + "'. Fields: " + string.Join(", ", target.fields.Select(f => f.key))); return; }
            string value = string.Join(" ", a.Skip(3)).Trim();
            if (value.Length > 40) { Err("Value too long."); return; }
            if (!RamFree()) return;
            double t = Balance.DbEditTime(P.cpu);
            Hostile("record edit " + target.id);
            var p2 = StartProc(ProcKind.DbEdit, "db edit " + target.id + "." + fld.key, t, n.id, target.id, fld.key, value);
            Print(Tx.D("[" + p2.pid + "] ") + "Rewriting " + target.id + "." + fld.key + ". ETA " + Tx.Clock(t) + ".");
        }

        // ------------------------------------------------------------------ local files

        LocalFile LocalByName(string name)
        {
            return P.files.FirstOrDefault(f => f.name == name) ??
                   P.files.FirstOrDefault(f => f.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        void CmdLocal()
        {
            Print(Tx.W("LOCAL STORAGE ") + Tx.D(StorageUsed() + "/" + Balance.StorageCap(P.storage) + " MB"));
            if (P.files.Count == 0) { Print(Tx.D("  (empty)")); return; }
            foreach (var f in P.files)
            {
                var src = f.fromNode != null ? Node(f.fromNode) : null;
                string from = f.missionId > 0 ? "client payload" : src != null ? "from " + src.ip : "";
                Print("  " + Tx.Pad(Tx.W(f.name), 30) + Tx.PadL(f.size + " MB", 7) + (f.encryption > 0 ? Tx.M("  encrypted lvl " + f.encryption) : "      ") + Tx.D("  " + from));
            }
        }

        void CmdRm(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: rm <file>"); return; }
            var f = LocalByName(a[0]);
            if (f == null) { Err("No such local file."); return; }
            P.files.Remove(f);
            Print(Tx.D("Removed " + f.name + "."));
        }

        void CmdDecrypt(string[] a, string rest)
        {
            if (a.Length == 0) { Err("Usage: decrypt <file>  (works on files in local storage)"); return; }
            var f = LocalByName(a[0]);
            if (f == null) { Err("No such local file. Download it first."); return; }
            if (f.encryption == 0) { Err(f.name + " isn't encrypted."); return; }
            int v = ToolVersion(ToolId.Decrypt);
            if (v == 0) { Err("You don't own a Decryptor. See `shop`."); return; }
            if (v < f.encryption) { Err("Decryptor v" + v + " can't break level " + f.encryption + " encryption. You need v" + f.encryption + "+."); return; }
            if (Procs.Any(p => p.kind == ProcKind.Decrypt && p.arg == f.name)) { Err("Already decrypting."); return; }
            if (!RamFree()) return;
            double t = Balance.DecryptTime(f.encryption, v, P.cpu);
            var p2 = StartProc(ProcKind.Decrypt, "decrypt " + f.name, t, null, f.name);
            Print(Tx.D("[" + p2.pid + "] ") + "Decrypting " + f.name + " offline. ETA " + Tx.Clock(t) + ".");
        }

        // ------------------------------------------------------------------ tab completion

        public string Complete(string input)
        {
            if (Mode != Mode.Playing || D == null) return input;
            bool trailingSpace = input.EndsWith(" ");
            var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0) return input;

            IEnumerable<string> pool;
            string partial;
            if (parts.Count == 1 && !trailingSpace)
            {
                partial = parts[0].ToLowerInvariant();
                pool = commands.Select(c => c.name);
            }
            else
            {
                partial = trailingSpace ? "" : parts[parts.Count - 1];
                Cmd c;
                lookup.TryGetValue(parts[0].ToLowerInvariant(), out c);
                string name = c != null ? c.name : "";
                int argIndex = trailingSpace ? parts.Count - 1 : parts.Count - 2;
                pool = ArgPool(name, argIndex, parts);
            }

            var matches = pool.Where(s => s.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            if (matches.Count == 0) return input;

            string common = matches[0];
            foreach (var m in matches.Skip(1))
            {
                int i = 0;
                while (i < common.Length && i < m.Length && char.ToLowerInvariant(common[i]) == char.ToLowerInvariant(m[i])) i++;
                common = common.Substring(0, i);
            }
            if (matches.Count > 1 && common.Length <= partial.Length)
            {
                Print(Tx.D(string.Join("   ", matches)));
                return input;
            }

            string head = trailingSpace ? input : input.Substring(0, input.Length - partial.Length);
            return head + common + (matches.Count == 1 ? " " : "");
        }

        IEnumerable<string> ArgPool(string cmd, int argIndex, List<string> parts)
        {
            switch (cmd)
            {
                case "help": return commands.Select(c => c.name);
                case "connect": return D.nodes.Where(n => n.known).Select(n => n.ip);
                case "bounce":
                    if (argIndex == 0) return new[] { "add", "rm", "clear", "auto" };
                    if (parts.Count > 1 && parts[1] == "rm") return P.bounces.Select(Node).Where(n => n != null).Select(n => n.ip);
                    return D.nodes.Where(n => n.known && HasAccess(n) && n.kind != NodeKind.Training).Select(n => n.ip);
                case "download":
                case "delete":
                    return Conn != null && HasAccess(Conn) ? Conn.files.Select(f => f.name) : new string[0];
                case "upload":
                case "rm":
                case "decrypt":
                    return P.files.Select(f => f.name);
                case "buy": return Balance.Tools.Select(t => t.key);
                case "upgrade": return Balance.Hardware.Select(h => h.key);
                case "accept": return D.board.Select(m => m.id.ToString());
                case "submit":
                case "abandon":
                    return D.active.Select(m => m.id.ToString());
                case "kill": return Procs.Select(p => p.pid.ToString());
                case "tutorial": return new[] { "skip" };
                case "db":
                    if (Conn == null || !HasAccess(Conn)) return new string[0];
                    if (argIndex == 0) return new[] { "edit" }.Concat(Conn.records.Select(r => r.id));
                    if (argIndex == 1) return Conn.records.Select(r => r.id);
                    if (argIndex == 2 && parts.Count > 2)
                    {
                        var rec = Conn.records.FirstOrDefault(r => r.id.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
                        return rec != null ? rec.fields.Select(f => f.key) : new string[0];
                    }
                    return new string[0];
                default: return new string[0];
            }
        }
    }
}
