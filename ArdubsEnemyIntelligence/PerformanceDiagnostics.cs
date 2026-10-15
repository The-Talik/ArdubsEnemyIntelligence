using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ArdubsEnemyIntelligence;

// Opt-in, aggregated timings. Never send these through the in-game combat log:
// writing a message for every cursor update would itself create stutters.
internal static class PerformanceDiagnostics
{
    internal enum Stage
    {
        CursorUpdate,
        Footprint,
        PreviewUpdate,
        PathSetup,
        RouteFinalization,
        FlyingVisual,
        CombatLogWrite,
        Reinforcements,
        Count
    }

    private static readonly object Sync = new object();
    private static readonly long[] Counts = new long[(int)Stage.Count];
    private static readonly long[] Totals = new long[(int)Stage.Count];
    private static readonly long[] Maximums = new long[(int)Stage.Count];
    private static long nextReport;
    private static long frames;
    private static long slowFrames;
    private static double totalFrameMs;
    private static double maximumFrameMs;

    internal static long Start() => ArdubsEnemyIntelligenceMain.Settings?.ProfileCursorPerformance == true
        ? Stopwatch.GetTimestamp() : 0;

    internal static void End(Stage stage, long start)
    {
        if (start == 0 || ArdubsEnemyIntelligenceMain.Settings?.ProfileCursorPerformance != true) return;
        var elapsed = Stopwatch.GetTimestamp() - start;
        if (elapsed < 0) return;
        lock (Sync)
        {
            var index = (int)stage;
            Counts[index]++;
            Totals[index] += elapsed;
            if (elapsed > Maximums[index]) Maximums[index] = elapsed;
        }
    }

    internal static void Frame(float unscaledDeltaTime)
    {
        if (ArdubsEnemyIntelligenceMain.Settings?.ProfileCursorPerformance != true) return;
        var ms = unscaledDeltaTime * 1000.0;
        // Exclude paused/alt-tab gaps that are not an in-game frame.
        if (ms <= 0 || ms >= 5000) return;
        lock (Sync)
        {
            frames++;
            totalFrameMs += ms;
            if (ms > maximumFrameMs) maximumFrameMs = ms;
            if (ms >= 50) slowFrames++;
        }
    }

    internal static void ReportIfDue()
    {
        if (ArdubsEnemyIntelligenceMain.Settings?.ProfileCursorPerformance != true) return;
        var now = Stopwatch.GetTimestamp();
        long[] counts;
        long[] totals;
        long[] maximums;
        long frameCount;
        long slowFrameCount;
        double frameTotal;
        double frameMaximum;
        lock (Sync)
        {
            if (nextReport == 0) nextReport = now + Stopwatch.Frequency * 10;
            if (now < nextReport) return;
            nextReport = now + Stopwatch.Frequency * 10;
            counts = (long[])Counts.Clone();
            totals = (long[])Totals.Clone();
            maximums = (long[])Maximums.Clone();
            Array.Clear(Counts, 0, Counts.Length);
            Array.Clear(Totals, 0, Totals.Length);
            Array.Clear(Maximums, 0, Maximums.Length);
            frameCount = frames;
            slowFrameCount = slowFrames;
            frameTotal = totalFrameMs;
            frameMaximum = maximumFrameMs;
            frames = 0;
            slowFrames = 0;
            totalFrameMs = 0;
            maximumFrameMs = 0;
        }
        var report = new StringBuilder("Performance (10s, ms): frame ");
        report.Append(frameCount).Append(" samples, avg ")
            .Append((frameCount == 0 ? 0 : frameTotal / frameCount).ToString("0.00", CultureInfo.InvariantCulture))
            .Append(", max ").Append(frameMaximum.ToString("0.00", CultureInfo.InvariantCulture))
            .Append(", >=50ms ").Append(slowFrameCount);
        for (var index = 0; index < (int)Stage.Count; index++)
        {
            if (counts[index] == 0) continue;
            var factor = 1000.0 / Stopwatch.Frequency;
            report.Append("; ").Append((Stage)index).Append(' ')
                .Append(counts[index]).Append(" calls, avg ")
                .Append((totals[index] * factor / counts[index]).ToString("0.00", CultureInfo.InvariantCulture))
                .Append(", max ").Append((maximums[index] * factor).ToString("0.00", CultureInfo.InvariantCulture));
        }
        ArdubsEnemyIntelligenceMain.ModEntry.Logger.Log(report.ToString());
    }
}
