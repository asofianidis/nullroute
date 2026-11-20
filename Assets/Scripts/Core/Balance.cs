using System;

namespace Nullroute.Core
{
    public class ToolInfo
    {
        public ToolId id;
        public string key;       // shop / buy key
        public string name;
        public string layer;     // what it attacks
        public int basePrice;
        public int unlockRank;
        public string desc;
        public string blurb;     // one-liner for the shop
    }

    public class HwInfo
    {
        public HwId id;
        public string key;
        public string name;
        public int basePrice;
        public string desc;
    }

    /// <summary>Every tunable number in the game lives here.</summary>
    public static class Balance
    {
        public const long StartCredits = 300;
        public const int BoardSize = 6;
        public const int MaxActiveJobs = 3;

        public static readonly ToolInfo[] Tools =
        {
            new ToolInfo { id = ToolId.PwCrack, blurb = "Breaks login passwords.",   key = "pwcrack",   name = "Password Cracker", layer = "password", basePrice = 300, unlockRank = 0,
                desc = "Cracks login passwords (command: crack). Version must be >= password level." },
            new ToolInfo { id = ToolId.FwBypass, blurb = "Gets you past firewalls.",  key = "fwbypass",  name = "Firewall Bypass",  layer = "firewall", basePrice = 350, unlockRank = 0,
                desc = "Punches through firewalls. Version must be >= firewall level." },
            new ToolInfo { id = ToolId.LogWipe, blurb = "Erases your log entries.",   key = "logwipe",   name = "Log Wiper",        layer = "logs",     basePrice = 250, unlockRank = 0,
                desc = "Erases your entries from a system's access logs. Higher versions wipe faster." },
            new ToolInfo { id = ToolId.PxBypass, blurb = "Gets you past proxies.",  key = "pxbypass",  name = "Proxy Bypass",     layer = "proxy",    basePrice = 450, unlockRank = 1,
                desc = "Slips past authentication proxies. Version must be >= proxy level." },
            new ToolInfo { id = ToolId.Decrypt, blurb = "Decrypts stolen files.",   key = "decrypt",   name = "Decryptor",        layer = "encryption", basePrice = 500, unlockRank = 1,
                desc = "Decrypts files in local storage. Version must be >= encryption level." },
            new ToolInfo { id = ToolId.MonBypass, blurb = "Blinds trace-boosting monitors.", key = "monbypass", name = "Monitor Bypass",   layer = "monitor",  basePrice = 600, unlockRank = 2,
                desc = "Blinds intrusion monitors that speed up traces. Version must be >= monitor level." },
            new ToolInfo { id = ToolId.Spoofer, blurb = "Passive: lowers forensic risk.",   key = "spoofer",   name = "Origin Spoofer",   layer = "forensics", basePrice = 900, unlockRank = 3,
                desc = "Passive. Forges your origin in logs, cutting the odds a forensic investigation finds you." },
        };

        public static readonly HwInfo[] Hardware =
        {
            new HwInfo { id = HwId.Cpu,     key = "cpu",   name = "Processor",    basePrice = 600, desc = "Speeds up every tool you run." },
            new HwInfo { id = HwId.Ram,     key = "ram",   name = "Memory",       basePrice = 450, desc = "More tools running at the same time." },
            new HwInfo { id = HwId.Nic,     key = "nic",   name = "Network Card", basePrice = 500, desc = "Faster transfers and more bounce hops." },
            new HwInfo { id = HwId.Storage, key = "hdd",   name = "Storage",      basePrice = 300, desc = "Room for stolen files." },
            new HwInfo { id = HwId.Cloak,   key = "cloak", name = "Signal Cloak", basePrice = 800, desc = "Slows traces and muddies forensic investigations." },
        };

        public static ToolInfo Tool(ToolId id)
        {
            foreach (var t in Tools) if (t.id == id) return t;
            throw new ArgumentException("unknown tool " + id);
        }

        public static ToolInfo FindTool(string key)
        {
            key = key.ToLowerInvariant();
            if (key == "crack" || key == "cracker" || key == "password") key = "pwcrack";
            foreach (var t in Tools) if (t.key == key) return t;
            return null;
        }

        public static HwInfo Hw(HwId id)
        {
            foreach (var h in Hardware) if (h.id == id) return h;
            throw new ArgumentException("unknown hardware " + id);
        }

        public static HwInfo FindHw(string key)
        {
            key = key.ToLowerInvariant();
            if (key == "storage" || key == "disk") key = "hdd";
            if (key == "memory") key = "ram";
            if (key == "network" || key == "modem") key = "nic";
            if (key == "processor") key = "cpu";
            foreach (var h in Hardware) if (h.key == key) return h;
            return null;
        }

        static long Round10(double v) { return Math.Max(10, (long)Math.Round(v / 10.0) * 10); }

        public static long ToolPrice(ToolId id, int version) { return Round10(Tool(id).basePrice * Math.Pow(1.6, version - 1)); }

        public static long HwPrice(HwId id, int toLevel) { return Round10(Hw(id).basePrice * Math.Pow(1.75, toLevel - 2)); }

        /// <summary>Tool versions above this are locked until you rank up.</summary>
        public static int MaxToolVersion(int rank) { return rank + 2; }

        // ---- hardware effects ----
        public static double CpuMult(int lvl) { return 1 + 0.45 * (lvl - 1); }
        public static int RamSlots(int lvl) { return 1 + lvl; }
        public static double Bandwidth(int lvl) { return 1.5 * (1 + 0.6 * (lvl - 1)); }
        public static int StorageCap(int lvl) { return 24 * lvl; }
        public static double CloakMult(int lvl) { return 1 + 0.2 * (lvl - 1); }
        public static int MaxBounces(int nicLvl) { return 1 + nicLvl; }

        // ---- ranks (unbounded) ----
        static readonly string[] Titles =
        {
            "Script Kiddie", "Novice", "Operator", "Intruder", "Infiltrator", "Specialist",
            "Phantom", "Ghost", "Shadow", "Architect", "Legend", "Myth"
        };

        public static int RankThreshold(int r) { return 20 * r * r + 40 * r; }

        public static int RankFor(int rep)
        {
            int r = 0;
            while (RankThreshold(r + 1) <= rep) r++;
            return r;
        }

        public static string RankTitle(int r)
        {
            if (r < Titles.Length) return Titles[r];
            return "Myth " + Tx.Roman(r - Titles.Length + 2);
        }

        // ---- timings (seconds) ----
        static double VersionEdge(int version, int level) { return 1 + 0.3 * Math.Max(0, version - level); }

        public static double BreachTime(int level, int version, int cpu)
        {
            return 6 * (1 + 0.7 * level) * Math.Pow(1.04, level) / CpuMult(cpu) / VersionEdge(version, level);
        }

        public static double LogWipeTime(int entries, int version, int cpu)
        {
            return (3 + 1.2 * entries) / CpuMult(cpu) / (1 + 0.3 * (version - 1));
        }

        public static double DecryptTime(int enc, int version, int cpu)
        {
            return 5 * (1 + 0.8 * enc) / CpuMult(cpu) / VersionEdge(version, enc);
        }

        public static double TransferTime(int sizeMb, int nic) { return Math.Max(1, sizeMb / Bandwidth(nic)); }
        public static double DeleteTime(int cpu) { return 2.5 / CpuMult(cpu); }
        public static double DbEditTime(int cpu) { return 4 / CpuMult(cpu); }

        // ---- trace ----
        public static double BaseTraceTime(int tier) { return 90 * Math.Pow(0.88, tier); }

        /// <summary>How much a bounce route slows traces. Diminishing returns per hop.</summary>
        public static double BounceMult(int bounces) { return 1 + 0.8 * Math.Log(1 + bounces); }

        public static double TraceDuration(double baseTime, int bounces, int cloak, int activeMonitor)
        {
            return baseTime * BounceMult(bounces) * CloakMult(cloak) / (1 + 0.35 * activeMonitor);
        }

        public static double InvestigationChance(int tier, int evidence, int bounces, int cloak, int spoofer)
        {
            double b = Math.Min(0.85, 0.35 + 0.07 * tier);
            double ev = Math.Min(1.0, 0.4 + 0.2 * evidence);
            double c = b * ev / (1 + 0.35 * bounces) / (1 + 0.35 * spoofer) / Math.Sqrt(CloakMult(cloak));
            return Math.Max(0.03, Math.Min(0.95, c));
        }

        public static double InvestigationDelay(int tier, Rng r)
        {
            return r.Range(150.0, 270.0) * (1 - Math.Min(0.5, tier * 0.03));
        }

        // ---- rewards ----
        public static long Payout(int tier, MissionType t, Rng r)
        {
            double m = t == MissionType.Steal ? 1.0 : t == MissionType.Delete ? 0.9 : t == MissionType.Plant ? 1.1 : 1.2;
            return Round10((300 + 80 * tier) * Math.Pow(1.45, tier) * m * r.Range(0.85, 1.15));
        }

        public static int RepGain(int tier, MissionType t)
        {
            double m = t == MissionType.Plant ? 1.1 : t == MissionType.Alter ? 1.2 : 1.0;
            return (int)Math.Round((6 + 5 * tier) * m);
        }
    }
}
