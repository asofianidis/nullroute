using System;
using System.Collections.Generic;
using System.Linq;

namespace Nullroute.Core
{
    /// <summary>
    /// A step-by-step walkthrough of the first contract. Each step has a hint and a condition;
    /// conditions are re-checked after every command and process, so players can go out of order.
    /// </summary>
    public class Tutorial
    {
        class Step
        {
            public Func<string> msg;
            public Func<bool> done;
        }

        readonly Game g;
        readonly List<Step> steps = new List<Step>();

        public Tutorial(Game game)
        {
            g = game;
            Build();
        }

        public bool Active { get { return g.D != null && !g.D.tutorialDone; } }

        NodeData Training { get { return g.Node(g.D.trainingNodeId); } }
        string Ip { get { var t = Training; return t != null ? t.ip : "?"; } }
        int Job { get { return g.D.tutorialMissionId; } }
        bool Accepted { get { return g.D.active.Any(m => m.id == Job) || g.D.tutorialMissionDone; } }

        void S(Func<string> msg, Func<bool> done) { steps.Add(new Step { msg = msg, done = done }); }

        void Build()
        {
            S(() => "I'm the training daemon bundled with your gateway. I'll walk you through your first job.\n" +
                    "After that you're on your own - and there is no finish line. Just bigger jobs, better tools,\n" +
                    "and more ways to get caught. Get traced once and your career is over for good.\n\n" +
                    "Type " + Tx.W("help") + " to see every command.",
              () => g.Used("help"));

            S(() => "That's everything you can do. Now look at your hardware: type " + Tx.W("rig") + ".",
              () => g.Used("rig"));

            S(() => "This is your workstation. A faster CPU runs tools faster, more RAM runs more of them at once,\n" +
                    "and a better cloak slows down anyone tracing you. You'll pay for upgrades with job money.\n\n" +
                    "Find work on the contract board: type " + Tx.W("jobs") + ".",
              () => g.Used("jobs"));

            S(() => "Each contract pays credits and reputation. Reputation raises your rank, which unlocks better\n" +
                    "tools and tougher, better-paying contracts.\n\n" +
                    "Take the training contract: " + Tx.W("accept " + Job),
              () => Accepted);

            S(() => "The target's address is now in your network map. Type " + Tx.W("net") + ".",
              () => g.Used("net") || g.Conn != null);

            S(() => "The training server is at " + Tx.Y(Ip) + ". Connect to it: " + Tx.W("connect " + Ip) + "\n" +
                    Tx.D("(Tab completes IPs and file names.)"),
              () => g.Conn != null && g.Conn.id == g.D.trainingNodeId);

            S(() => "You're connected but not logged in. Inspect its defenses: " + Tx.W("probe"),
              () => g.Used("probe") || (Training != null && Training.passwordKnown));

            S(() => "Only a password here. Real systems also run firewalls, proxies and monitors, and each needs\n" +
                    "the right tool at a high enough version.\n\n" +
                    "Tools run in the background - watch the bars at the top of the screen. Out in the wild, any\n" +
                    "hostile action starts a " + Tx.R("TRACE") + ". If it reaches 100% while you're connected, you're caught.\n" +
                    "This server is for training, so it won't trace you.\n\n" +
                    "Launch your password cracker: " + Tx.W("crack"),
              () => Training != null && Training.passwordKnown);

            S(() => "Access granted. List the files: " + Tx.W("ls"),
              () => g.Used("ls") || HasKey());

            S(() => "There's your target. Grab it: " + Tx.W("download training_key.dat"),
              () => HasKey());

            S(() => "Every hostile action leaves entries in the access log (see them with " + Tx.W("logs") + ").\n" +
                    "If you disconnect with evidence left behind, sysadmins open a " + Tx.R("forensic investigation") + ".\n" +
                    "When the timer runs out, they may trace you - unless you've wiped the logs.\n\n" +
                    "Cover your tracks: " + Tx.W("logwipe"),
              () => g.Used("logwipe") && Training != null && !Training.logs.Any(l => l.byPlayer));

            S(() => "Clean. Now drop the line: " + Tx.W("dc") + Tx.D(" (short for disconnect)"),
              () => g.Conn == null);

            S(() => "Deliver the file to your client: " + Tx.W("submit " + Job),
              () => g.D.tutorialMissionDone);

            S(() => "Paid. Now spend it: " + Tx.W("shop") + " lists tools and hardware.",
              () => g.Used("shop"));

            S(() => "Most real systems run a firewall, and you don't own a bypass yet. Buy one: " + Tx.W("buy fwbypass"),
              () => g.ToolVersion(ToolId.FwBypass) > 0);
        }

        bool HasKey()
        {
            return g.D.tutorialMissionDone || g.P.files.Any(f => f.name == "training_key.dat");
        }

        void Say(string msg)
        {
            g.Print();
            foreach (var line in msg.Split('\n'))
                g.Print(Tx.M("  ▌ ") + line);
            g.Print();
        }

        public void Begin()
        {
            g.D.tutorialStep = 0;
            g.D.tutorialDone = false;
            Show();
        }

        public void Show()
        {
            if (!Active) return;
            int i = Math.Min(g.D.tutorialStep, steps.Count - 1);
            Say(Tx.M("TRAINER ") + Tx.D("[" + (i + 1) + "/" + steps.Count + "]") + "\n" + steps[i].msg());
        }

        public void Check()
        {
            if (!Active) return;
            bool advanced = false;
            while (g.D.tutorialStep < steps.Count && steps[g.D.tutorialStep].done())
            {
                g.D.tutorialStep++;
                advanced = true;
            }
            if (g.D.tutorialStep >= steps.Count) { Finish(); return; }
            if (advanced) Show();
        }

        public void Skip()
        {
            // Make sure a skipped tutorial doesn't leave the player stranded.
            g.D.tutorialDone = true;
            g.D.board.RemoveAll(m => m.tutorial);
            g.D.active.RemoveAll(m => m.tutorial);
            Say(Tx.M("TRAINER ") + "Tutorial skipped. `help` lists every command. Don't get caught.");
            g.Save();
        }

        void Finish()
        {
            g.D.tutorialDone = true;
            Say(Tx.M("TRAINER ") + Tx.G("Training complete.") + " A few things to remember:\n" +
                "  - " + Tx.W("bounce auto") + " routes you through public relays. Every hop slows traces.\n" +
                "  - " + Tx.W("upgrade cpu") + " speeds up tools; " + Tx.W("upgrade cloak") + " makes you harder to trace.\n" +
                "  - Tool versions are capped at your rank + 2. Rank up to unlock more.\n" +
                "  - Always " + Tx.W("logwipe") + " before you " + Tx.W("dc") + ". Always.\n" +
                "  - " + Tx.W("help <command>") + " explains anything.\n\n" +
                "The board refreshes constantly. Good luck - and don't get caught.");
            g.Save();
        }
    }
}
