using System;
using System.Collections.Generic;

namespace Nullroute.Core
{
    /// <summary>Procedurally generates systems and contracts. Difficulty scales with tier, without limit.</summary>
    public static class WorldGen
    {
        static readonly NodeKind[] TargetKinds =
        {
            NodeKind.Corporate, NodeKind.Corporate, NodeKind.Bank, NodeKind.Research, NodeKind.University,
            NodeKind.Police, NodeKind.Medical, NodeKind.Media
        };

        static readonly NodeKind[] RecordKinds =
        {
            NodeKind.Bank, NodeKind.University, NodeKind.Police, NodeKind.Medical, NodeKind.Corporate
        };

        public static string UniqueIp(SaveData d, Rng r)
        {
            while (true)
            {
                string ip = NameGen.Ip(r);
                if (ip == d.player.gatewayIp) continue;
                bool clash = false;
                foreach (var n in d.nodes) if (n.ip == ip) { clash = true; break; }
                if (!clash) return ip;
            }
        }

        static string NewNodeId(SaveData d) { return "n" + (d.nextId++); }

        public static NodeData CreateRelay(SaveData d, Rng r)
        {
            var n = new NodeData
            {
                id = NewNodeId(d),
                ip = UniqueIp(d, r),
                kind = NodeKind.Relay,
                name = NameGen.Company(r, NodeKind.Relay),
                tier = 0,
                isPublic = true,
                known = true,
                traceTime = Balance.BaseTraceTime(0),
            };
            AddLog(n, 0, "relay daemon started");
            d.nodes.Add(n);
            return n;
        }

        public static NodeData CreateTraining(SaveData d, Rng r)
        {
            var n = new NodeData
            {
                id = NewNodeId(d),
                ip = UniqueIp(d, r),
                kind = NodeKind.Training,
                name = "Training Server",
                tier = 0,
                known = true,
                password = 1,
                traceTime = 0, // never traces
                adminPassword = NameGen.Password(r),
            };
            n.files.Add(new FileData { name = "training_key.dat", size = 3 });
            n.files.Add(new FileData { name = "welcome.txt", size = 1 });
            n.files.Add(new FileData { name = "notes_" + r.Range(10, 99) + ".txt", size = 1 });
            AddLog(n, 0, "admin logged in from 10.0.0.2");
            AddLog(n, 0, "backup completed");
            d.nodes.Add(n);
            return n;
        }

        static void AddLog(NodeData n, double t, string text)
        {
            n.logs.Add(new LogEntry { time = t, text = text });
        }

        static int Lvl(Rng r, int v) { return Math.Max(1, v); }

        public static NodeData CreateTarget(SaveData d, Rng r, NodeKind kind, int tier)
        {
            var n = new NodeData
            {
                id = NewNodeId(d),
                ip = UniqueIp(d, r),
                kind = kind,
                name = NameGen.Company(r, kind),
                tier = tier,
                adminPassword = NameGen.Password(r),
            };

            // Security layers. A layer first appears at the tier where its counter-tool
            // becomes buyable (tool unlock rank + 1), so every job on the board is beatable.
            n.password = Lvl(r, tier + r.Range(-1, 1));
            if (tier >= 1 || r.Chance(0.4)) n.firewall = Lvl(r, tier + r.Range(-1, 0));
            if (tier >= 3 || (tier == 2 && r.Chance(0.6))) n.proxy = Lvl(r, tier - 1 + r.Range(-1, 0));
            if (tier >= 5 || (tier >= 3 && r.Chance(0.5))) n.monitor = Lvl(r, tier - 2 + r.Range(-1, 0));

            double kindMult;
            switch (kind)
            {
                case NodeKind.Police: kindMult = 0.75; break;
                case NodeKind.Bank: kindMult = 0.8; break;
                case NodeKind.Research: kindMult = 0.9; break;
                case NodeKind.University: kindMult = 1.2; break;
                case NodeKind.Media: kindMult = 1.1; break;
                default: kindMult = 1.0; break;
            }
            n.traceTime = Balance.BaseTraceTime(tier) * kindMult * r.Range(0.9, 1.1);

            int files = r.Range(4, 8);
            for (int i = 0; i < files; i++) AddRandomFile(n, r);

            if (Array.IndexOf(RecordKinds, kind) >= 0)
            {
                int people = r.Range(6, 11);
                for (int i = 0; i < people; i++) n.records.Add(MakeRecord(n, r));
            }

            int logs = r.Range(2, 5);
            for (int i = 0; i < logs; i++)
                AddLog(n, -r.Range(60, 86400), r.Pick(new[]
                {
                    "admin logged in from 10." + r.Range(0, 255) + "." + r.Range(0, 255) + "." + r.Range(1, 254),
                    "scheduled backup completed",
                    "password policy updated",
                    "user " + NameGen.Person(r).Split(' ')[0].ToLowerInvariant() + " logged out",
                    "firmware check: OK",
                }));

            d.nodes.Add(n);
            return n;
        }

        static FileData AddRandomFile(NodeData n, Rng r)
        {
            string name;
            do { name = NameGen.FileFor(r, n.kind); } while (n.files.Exists(f => f.name == name));
            int enc = 0;
            if (n.tier >= 2 && r.Chance(0.45)) enc = Lvl(r, n.tier - 1 + r.Range(-1, 0));
            var f2 = new FileData { name = name, size = r.Range(1, 4 + n.tier * 2), encryption = enc };
            n.files.Add(f2);
            return f2;
        }

        static readonly string[] Status = { "active", "frozen", "closed" };
        static readonly string[] Degree = { "enrolled", "suspended", "expelled", "graduated" };
        static readonly string[] Criminal = { "clean", "misdemeanor", "felony" };
        static readonly string[] Warrant = { "none", "active" };
        static readonly string[] Insurance = { "covered", "denied", "pending" };
        static readonly string[] Condition = { "stable", "critical", "discharged" };
        static readonly string[] Clearance = { "level-1", "level-2", "level-3", "level-4", "level-5" };

        static RecordData MakeRecord(NodeData n, Rng r)
        {
            var rec = new RecordData { id = "R-" + r.Range(1000, 9999), name = NameGen.Person(r) };
            while (n.records.Exists(x => x.id == rec.id)) rec.id = "R-" + r.Range(1000, 9999);
            switch (n.kind)
            {
                case NodeKind.Bank:
                    Field(rec, "balance", r.Range(50, 90000).ToString());
                    Field(rec, "status", r.Pick(Status));
                    break;
                case NodeKind.University:
                    Field(rec, "gpa", Tx.F1(r.Range(1.8, 3.9)));
                    Field(rec, "degree", r.Pick(Degree));
                    break;
                case NodeKind.Police:
                    Field(rec, "record", r.Pick(Criminal));
                    Field(rec, "warrant", r.Pick(Warrant));
                    break;
                case NodeKind.Medical:
                    Field(rec, "insurance", r.Pick(Insurance));
                    Field(rec, "condition", r.Pick(Condition));
                    break;
                default:
                    Field(rec, "salary", (r.Range(30, 180) * 1000).ToString());
                    Field(rec, "clearance", r.Pick(Clearance));
                    break;
            }
            return rec;
        }

        static void Field(RecordData rec, string k, string v) { rec.fields.Add(new FieldData { key = k, value = v }); }

        static string AlterTarget(Rng r, string field, string current)
        {
            string[] pool = null;
            switch (field)
            {
                case "balance": return r.Chance(0.5) ? "0" : (r.Range(25, 400) * 10000).ToString();
                case "salary": return (r.Range(250, 900) * 1000).ToString();
                case "gpa": return r.Chance(0.5) ? "4.0" : "0.5";
                case "status": pool = Status; break;
                case "degree": pool = Degree; break;
                case "record": pool = Criminal; break;
                case "warrant": pool = Warrant; break;
                case "insurance": pool = Insurance; break;
                case "condition": pool = Condition; break;
                case "clearance": pool = Clearance; break;
            }
            if (pool == null) return "0";
            string v;
            do { v = r.Pick(pool); } while (v == current);
            return v;
        }

        public static MissionData CreateMission(SaveData d, Rng r, int tier)
        {
            var type = (MissionType)r.Range(0, 3);
            NodeKind kind = type == MissionType.Alter ? r.Pick(RecordKinds) : r.Pick(TargetKinds);
            var node = CreateTarget(d, r, kind, tier);

            var m = new MissionData
            {
                id = d.nextId++,
                type = type,
                tier = tier,
                nodeId = node.id,
                payout = Balance.Payout(tier, type, r),
                rep = Balance.RepGain(tier, type),
                expires = d.time + r.Range(600.0, 1200.0),
                client = r.Chance(0.25) ? "anonymous" : NameGen.Handle(r),
            };

            switch (type)
            {
                case MissionType.Steal:
                case MissionType.Delete:
                {
                    var f = r.Pick(node.files);
                    m.fileName = f.name;
                    m.fileSize = f.size;
                    break;
                }
                case MissionType.Plant:
                {
                    string name;
                    do { name = NameGen.PlantFile(r); } while (node.files.Exists(f => f.name == name));
                    m.fileName = name;
                    m.fileSize = r.Range(1, 3 + tier);
                    break;
                }
                case MissionType.Alter:
                {
                    var rec = r.Pick(node.records);
                    var fld = r.Pick(rec.fields);
                    m.recordId = rec.id;
                    m.personName = rec.name;
                    m.field = fld.key;
                    m.value = AlterTarget(r, fld.key, fld.value);
                    break;
                }
            }
            return m;
        }

        public static MissionData CreateTutorialMission(SaveData d, NodeData training)
        {
            return new MissionData
            {
                id = d.nextId++,
                type = MissionType.Steal,
                tier = 0,
                nodeId = training.id,
                fileName = "training_key.dat",
                fileSize = 3,
                payout = 400,
                rep = 10,
                expires = 1e12,
                client = "trainer",
                tutorial = true,
            };
        }

        public static string Describe(MissionData m, NodeData n)
        {
            string where = n != null ? n.name : "?";
            switch (m.type)
            {
                case MissionType.Steal: return "Retrieve " + m.fileName + " from " + where + " (decrypted)";
                case MissionType.Delete: return "Destroy " + m.fileName + " on " + where;
                case MissionType.Plant: return "Upload " + m.fileName + " to " + where;
                default: return "Set " + m.field + " of " + m.personName + " (" + m.recordId + ") on " + where + " to '" + m.value + "'";
            }
        }
    }
}
