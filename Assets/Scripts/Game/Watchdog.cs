using System.Diagnostics;
using System.Threading;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Finds where the game gets stuck. The game marks each step it starts with <see cref="Mark"/>;
    /// a background thread notices if one step lasts more than a few seconds and writes which one
    /// to Unity's log, which it can do even while the main thread is stuck.
    /// </summary>
    public static class Watchdog
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static volatile string step = "starting";
        private static long stepStartedMs;
        private static Thread thread;
        private static volatile bool running;

        /// <summary>How long one step can take before it's reported as stuck.</summary>
        private const long StuckMs = 3000;

        public static void Mark(string what)
        {
            step = what;
            Interlocked.Exchange(ref stepStartedMs, Clock.ElapsedMilliseconds);
        }

        public static void Start()
        {
            if (running) return;
            running = true;
            Mark("starting");
            thread = new Thread(Watch) { IsBackground = true, Name = "Shapes watchdog" };
            thread.Start();
        }

        public static void Stop() => running = false;

        private static void Watch()
        {
            long reportedFor = -1;
            while (running)
            {
                Thread.Sleep(500);
                var started = Interlocked.Read(ref stepStartedMs);
                var stuckFor = Clock.ElapsedMilliseconds - started;
                // One report per stuck step, then a reminder every ten seconds.
                if (stuckFor >= StuckMs && (reportedFor != started || stuckFor % 10000 < 500))
                {
                    reportedFor = started;
                    UnityEngine.Debug.LogWarning($"[Shapes] Stuck for {stuckFor / 1000.0:0.0} s in: {step}");
                }
            }
        }
    }
}
