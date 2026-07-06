using System;
using System.IO;
using VoidRunner.Content;
using VoidRunner.Core;
using VoidRunner.Modding;
using VoidRunner.Replay;
using VoidRunner.Rng;

namespace VoidRunner.Tools
{
    /// <summary>
    /// Command-line front end for the shared VoidRunner simulation core.
    ///
    /// Commands:
    ///   vrverify verify &lt;packs-dir&gt; &lt;replay.vrplay&gt;
    ///       Loads the content packs, replays the file, prints PASS/FAIL and the reproduced score.
    ///
    ///   vrverify record &lt;packs-dir&gt; &lt;seed&gt; &lt;ticks&gt; &lt;out.vrplay&gt;
    ///       Runs a scripted bot for N ticks against the packs and writes a replay. Handy for
    ///       generating deterministic fixtures and for the end-to-end CI check.
    ///
    ///   vrverify info &lt;packs-dir&gt;
    ///       Prints the loaded content summary and fingerprint.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { PrintUsage(); return 2; }

                switch (args[0])
                {
                    case "verify": return CmdVerify(args);
                    case "record": return CmdRecord(args);
                    case "info": return CmdInfo(args);
                    default:
                        Console.Error.WriteLine($"unknown command '{args[0]}'");
                        PrintUsage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return 3;
            }
        }

        private static (ContentRegistry reg, ulong fp) LoadPacks(string dir)
        {
            var result = PackDiscovery.DiscoverAndLoad(dir, out var order);
            if (!result.Ok)
            {
                foreach (var e in result.Errors) Console.Error.WriteLine("content error: " + e);
                throw new Exception($"content failed to load from '{dir}'");
            }
            Console.WriteLine($"loaded {order.Count} pack(s): {result.Registry.EnemyCount} enemies, " +
                              $"{result.Registry.WeaponCount} weapons, {result.Registry.RoomCount} rooms");
            return (result.Registry, ContentFingerprint.Compute(result.Registry));
        }

        private static int CmdInfo(string[] args)
        {
            if (args.Length < 2) { PrintUsage(); return 2; }
            var (_, fp) = LoadPacks(args[1]);
            Console.WriteLine($"content fingerprint: {fp}");
            return 0;
        }

        private static int CmdVerify(string[] args)
        {
            if (args.Length < 3) { PrintUsage(); return 2; }
            var (reg, fp) = LoadPacks(args[1]);

            var replay = ReplayCodec.Deserialize(File.ReadAllText(args[2]));
            Console.WriteLine($"replay: seed={replay.Seed} ticks={replay.TickCount} " +
                              $"claimedScore={replay.FinalScore} room={replay.FinalRoom} label='{replay.Label}'");

            var result = ReplayVerifier.Verify(replay, reg, fp);
            Console.WriteLine(result.Message);
            if (result.Reproduced)
            {
                Console.WriteLine($"PASS — reproduced score {result.ReplayedScore}, room {result.ReplayedRoom}");
                return 0;
            }
            Console.WriteLine("FAIL — replay did not reproduce (desync or tampered file)");
            return 1;
        }

        private static int CmdRecord(string[] args)
        {
            if (args.Length < 5) { PrintUsage(); return 2; }
            var (reg, fp) = LoadPacks(args[1]);

            ulong seed = ulong.TryParse(args[2], out var s) ? s : DeterministicRandom.FromString(args[2]).Seed;
            int ticks = int.Parse(args[3]);
            string outPath = args[4];

            var sim = new Simulation(reg, seed);
            var rec = new ReplayRecorder(seed, fp, args[2]);
            for (int t = 0; t < ticks; t++)
            {
                var cmd = ScriptedInput(t);
                rec.Record(cmd);
                sim.Step(cmd);
                if (sim.RunOver) break;
            }
            var replay = rec.Finish(sim);
            File.WriteAllText(outPath, ReplayCodec.Serialize(replay));
            Console.WriteLine($"recorded {replay.TickCount} ticks → {outPath} (score {replay.FinalScore}, room {replay.FinalRoom})");
            return 0;
        }

        /// <summary>Deterministic scripted "bot" input, identical to the one used in the unit tests.</summary>
        private static InputCommand ScriptedInput(int tick)
        {
            var r = new DeterministicRandom((ulong)tick * 2654435761UL);
            float mx = r.NextFloat() * 2f - 1f;
            float my = r.NextFloat() * 2f - 1f;
            float aim = r.NextFloat() * 360f;
            bool fire = (tick % 5) != 0;
            return InputCommand.From(new Vec2(mx, my), SimMathUtil.FromAngle(aim), fire);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("vrverify — VoidRunner replay/content tool");
            Console.WriteLine("usage:");
            Console.WriteLine("  vrverify info   <packs-dir>");
            Console.WriteLine("  vrverify verify <packs-dir> <replay.vrplay>");
            Console.WriteLine("  vrverify record <packs-dir> <seed> <ticks> <out.vrplay>");
        }
    }
}
