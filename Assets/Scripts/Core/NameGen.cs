using System.Text;

namespace Nullroute.Core
{
    /// <summary>Procedural names for companies, people, files, places and handles.</summary>
    public static class NameGen
    {
        static readonly string[] Pre =
        {
            "Vex", "Halc", "Orin", "Cal", "Nimb", "Kor", "Zent", "Ast", "Mer", "Tal", "Qua", "Ober", "Sol",
            "Lum", "Dra", "Pyx", "Ves", "Cyr", "Ark", "Neo", "Hel", "Tyr", "Ul", "Xan", "Bry", "Fen", "Gal",
            "Ix", "Jor", "Mor", "Nyx", "Ost", "Pel", "Rho", "Syn", "Tor", "Vol", "Wex", "Yar", "Zor"
        };

        static readonly string[] Suf =
        {
            "tronic", "yon", "ix", "dyne", "core", "logic", "sys", "tek", "gen", "path", "nova", "mark",
            "sphere", "vale", "forge", "wave", "lith", "corp", "ium", "ara", "eon", "ica", "onix", "tera"
        };

        static readonly string[] PlacePre =
        {
            "North", "Ash", "Iron", "Silver", "Raven", "Stone", "Glen", "Hollow", "Marsh", "Oak", "East",
            "West", "Frost", "Amber", "Copper", "Lake", "River", "Red", "Black", "High"
        };

        static readonly string[] PlaceSuf =
        {
            "vale", "ford", "haven", "port", "field", "ridge", "wick", "moor", "gate", "crest", "bury",
            "mouth", "fall", "brook", "stead", "march"
        };

        static readonly string[] First =
        {
            "Ada", "Bram", "Cora", "Dmitri", "Elena", "Felix", "Greta", "Hiro", "Iris", "Jonas", "Kara",
            "Leon", "Mira", "Nils", "Opal", "Pavel", "Quinn", "Rosa", "Silas", "Tess", "Umar", "Vera",
            "Wade", "Ximena", "Yusuf", "Zara", "Anika", "Basil", "Cyrus", "Dahlia", "Emil", "Freya",
            "Gideon", "Hana", "Ivo", "Juno", "Kofi", "Lena", "Marek", "Noor"
        };

        static readonly string[] Last =
        {
            "Abara", "Brandt", "Castellan", "Dunmore", "Eriksen", "Falk", "Gallo", "Hartley", "Ishikawa",
            "Jovanovic", "Kowal", "Lindqvist", "Moreau", "Nakamura", "Okafor", "Petrov", "Quill", "Rasmussen",
            "Sandoval", "Thorne", "Ueda", "Varga", "Whitlock", "Xu", "Yilmaz", "Zelenko", "Achebe", "Bellamy",
            "Crane", "Delacroix", "Esposito", "Fairbanks", "Grell", "Holloway", "Ivers", "Jaramillo"
        };

        static readonly string[] Codenames =
        {
            "aurora", "basilisk", "cobalt", "daybreak", "ember", "foxglove", "glacier", "halcyon", "ironwood",
            "juniper", "kestrel", "lumen", "monolith", "nightjar", "obsidian", "paragon", "quasar", "rook",
            "sable", "tundra", "umbra", "vanguard", "wraith", "zephyr", "cinder", "mantis", "solstice", "verdigris"
        };

        static readonly string[] HandlePre =
        {
            "zero", "null", "byte", "hex", "cipher", "static", "void", "ghost", "neon", "rust", "acid", "glitch",
            "silent", "chrome", "dark", "shard", "flux", "vector", "proxy", "kernel"
        };

        static readonly string[] HandleSuf =
        {
            "wolf", "fox", "moth", "crow", "viper", "saint", "monk", "drift", "pulse", "spark", "wire",
            "shade", "lynx", "hound", "raven", "echo"
        };

        public static string Ip(Rng r)
        {
            return r.Range(11, 223) + "." + r.Range(0, 255) + "." + r.Range(0, 255) + "." + r.Range(2, 254);
        }

        static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        public static string Brand(Rng r) { return Cap(r.Pick(Pre) + r.Pick(Suf)); }

        public static string Place(Rng r) { return r.Pick(PlacePre) + r.Pick(PlaceSuf); }

        public static string Person(Rng r) { return r.Pick(First) + " " + r.Pick(Last); }

        public static string Codename(Rng r) { return r.Pick(Codenames); }

        public static string Handle(Rng r)
        {
            string h = r.Pick(HandlePre) + r.Pick(HandleSuf);
            if (r.Chance(0.4)) h += r.Range(1, 99);
            return h;
        }

        public static string Company(Rng r, NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Corporate:
                    return Brand(r) + " " + r.Pick(new[] { "Systems", "Industries", "Holdings", "Technologies", "Dynamics", "Group" });
                case NodeKind.Bank:
                    return Brand(r) + " " + r.Pick(new[] { "Bank", "Savings & Trust", "Financial", "Capital", "Credit Union" });
                case NodeKind.Research:
                    return Brand(r) + " " + r.Pick(new[] { "Biotech", "Research", "Labs", "Institute", "Robotics" });
                case NodeKind.University:
                    return r.Chance(0.5) ? "University of " + Place(r) : Place(r) + " " + r.Pick(new[] { "College", "Polytechnic", "Academy" });
                case NodeKind.Police:
                    return Place(r) + " " + r.Pick(new[] { "Police Dept.", "Records Bureau", "Sheriff's Office", "Justice Archive" });
                case NodeKind.Medical:
                    return Place(r) + " " + r.Pick(new[] { "Medical Center", "General Hospital", "Health Network", "Clinic" });
                case NodeKind.Media:
                    return Brand(r) + " " + r.Pick(new[] { "News Network", "Media", "Broadcasting", "Daily" });
                case NodeKind.Relay:
                    return "Public Relay - " + Place(r);
                default:
                    return "Training Server";
            }
        }

        public static string KindLabel(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Corporate: return "corp";
                case NodeKind.Bank: return "bank";
                case NodeKind.Research: return "lab";
                case NodeKind.University: return "edu";
                case NodeKind.Police: return "police";
                case NodeKind.Medical: return "medical";
                case NodeKind.Media: return "media";
                case NodeKind.Relay: return "relay";
                default: return "training";
            }
        }

        static string Fill(Rng r, string template)
        {
            return template.Replace("{n}", r.Range(10, 999).ToString()).Replace("{c}", Codename(r));
        }

        public static string FileFor(Rng r, NodeKind k)
        {
            string[] t;
            switch (k)
            {
                case NodeKind.Corporate:
                    t = new[] { "q{n}_financials.xls", "board_minutes_{n}.doc", "roadmap_{c}.pdf", "src_{c}.tar", "payroll_{n}.db", "merger_{c}.doc", "client_list_{n}.csv" };
                    break;
                case NodeKind.Bank:
                    t = new[] { "ledger_{n}.db", "audit_{n}.pdf", "vault_codes_{c}.enc", "wire_log_{n}.csv", "risk_model_{c}.xls" };
                    break;
                case NodeKind.Research:
                    t = new[] { "prototype_{c}.cad", "trial_results_{n}.csv", "genome_seq_{n}.dat", "patent_draft_{c}.doc", "lab_notes_{n}.txt" };
                    break;
                case NodeKind.University:
                    t = new[] { "exam_{n}.pdf", "grant_{c}.doc", "thesis_{c}.pdf", "admissions_{n}.csv", "research_{c}.dat" };
                    break;
                case NodeKind.Police:
                    t = new[] { "case_{n}.file", "informants_{c}.txt", "evidence_{n}.img", "surveillance_{c}.mp4", "warrants_{n}.db" };
                    break;
                case NodeKind.Medical:
                    t = new[] { "patient_index_{n}.db", "trial_{c}.csv", "pharma_order_{n}.xls", "scan_{n}.img", "billing_{n}.db" };
                    break;
                case NodeKind.Media:
                    t = new[] { "draft_story_{n}.doc", "sources_{c}.txt", "embargo_{n}.doc", "footage_{c}.mp4", "leak_{n}.pdf" };
                    break;
                default:
                    t = new[] { "readme_{n}.txt", "motd.txt" };
                    break;
            }
            return Fill(r, r.Pick(t));
        }

        public static string PlantFile(Rng r)
        {
            return Fill(r, r.Pick(new[] { "memo_{n}.doc", "invoice_{n}.pdf", "backdoor_{c}.bin", "evidence_{c}.img", "confession_{n}.txt", "payload_{c}.bin" }));
        }

        public static string Password(Rng r)
        {
            const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!#$%&*";
            var sb = new StringBuilder();
            int len = r.Range(8, 14);
            for (int i = 0; i < len; i++) sb.Append(chars[r.Range(0, chars.Length - 1)]);
            return sb.ToString();
        }
    }
}
