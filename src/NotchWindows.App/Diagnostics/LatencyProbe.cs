#if DEBUG
using System.Diagnostics;
using System.Runtime.CompilerServices;
using NotchWindows.Core;

namespace NotchWindows.App.Diagnostics;

// Debug-only: time from a provider raising an event to the pill starting to show it.
internal static class LatencyProbe
{
    // Keyed by reference; entries die with their events.
    private static readonly ConditionalWeakTable<IslandEvent, StrongBox<long>> Created = new();

    /// <summary>Raised on the UI thread with the event kind and the latency in milliseconds.</summary>
    public static event Action<IslandKind, double>? Measured;

    /// <summary>Called on the provider thread when the event is raised (right after it is created).</summary>
    public static void Stamp(IslandEvent e) =>
        Created.AddOrUpdate(e, new StrongBox<long>(Stopwatch.GetTimestamp()));

    /// <summary>Called on the UI thread when the pill starts expanding (or swapping) to show the event.</summary>
    public static void Measure(IslandEvent? e)
    {
        if (e is null || !Created.TryGetValue(e, out var stamp))
            return;
        Created.Remove(e);
        double ms = Stopwatch.GetElapsedTime(stamp.Value).TotalMilliseconds;
        Trace.TraceInformation($"Latency {e.Kind}: {ms:F1} ms");
        Measured?.Invoke(e.Kind, ms);
    }
}
#endif
