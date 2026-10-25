using Microsoft.Extensions.Logging;

namespace NotchWindows.Core;

public sealed class ProviderHost(
    EventBus bus,
    IEnumerable<IEventProvider> providers,
    TimeSpan retryDelay,
    ILogger<ProviderHost> logger)
{
    private const int MaxFailures = 3;

    private readonly TimeSpan _retryDelay = retryDelay >= TimeSpan.Zero
        ? retryDelay
        : throw new ArgumentOutOfRangeException(nameof(retryDelay), "retryDelay must not be negative.");

    private readonly List<ProviderState> _states = providers.Select(p => new ProviderState(p)).ToList();

    public bool IsDisabled(IEventProvider provider)
    {
        var s = _states.First(x => x.Provider == provider);
        lock (s.Gate) return s.Disabled;
    }

    public void StartAll()
    {
        foreach (var s in _states)
        {
            var state = s;
            state.Provider.Raised += e => { ResetFailures(state); bus.Publish(e); };
            state.Provider.AmbientChanged += u => { ResetFailures(state); bus.PublishAmbient(u); };
            state.Provider.Faulted += ex => Fail(state, ex);
        }

        foreach (var s in _states)
        {
            int attempt;
            lock (s.Gate) attempt = ++s.Generation;
            try { s.Provider.Start(); }
            catch (Exception ex) { Fail(s, ex, attempt); }
        }
    }

    private static void ResetFailures(ProviderState s)
    {
        lock (s.Gate) s.Failures = 0;
    }

    // A failure is attributed to the current start attempt (Generation). A Faulted raised
    // during Start() and a Start() throw for the same attempt are counted once and
    // schedule a single restart.
    // attempt == null means "whatever attempt is current" (a Faulted event); a Start() throw
    // passes its own attempt so a concurrently scheduled restart cannot re-attribute it.
    private void Fail(ProviderState s, Exception ex, int? attempt = null)
    {
        int generation;
        lock (s.Gate)
        {
            if (s.Disabled) return;

            if (s.Provider.Status == ProviderStatus.Unavailable)
            {
                logger.LogInformation("Provider {Provider} is unavailable; not restarting.", s.Provider.Name);
                return;
            }

            var gen = attempt ?? s.Generation;
            if (gen <= s.FailedGeneration) return;
            s.FailedGeneration = gen;

            s.Failures++;
            if (s.Failures >= MaxFailures)
            {
                s.Disabled = true;
                logger.LogWarning(ex, "Provider {Provider} disabled for this session after {Count} consecutive failures.",
                    s.Provider.Name, s.Failures);
                return;
            }

            generation = s.Generation;
            logger.LogWarning(ex, "Provider {Provider} failed ({Count}/{Max}); restarting.",
                s.Provider.Name, s.Failures, MaxFailures);
        }

        _ = Task.Run(() => RestartAsync(s, generation));
    }

    private async Task RestartAsync(ProviderState s, int generation)
    {
        try
        {
            await Task.Delay(_retryDelay).ConfigureAwait(false);

            int attempt;
            lock (s.Gate)
            {
                if (s.Disabled || s.Generation != generation) return;
                attempt = ++s.Generation;
            }

            try { s.Provider.Stop(); }
            catch (Exception ex) { logger.LogDebug(ex, "Provider {Provider} threw on Stop during restart.", s.Provider.Name); }

            try { s.Provider.Start(); }
            catch (Exception ex) { Fail(s, ex, attempt); }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error restarting provider {Provider}.", s.Provider.Name);
            Fail(s, ex);
        }
    }

    private sealed class ProviderState(IEventProvider provider)
    {
        public IEventProvider Provider { get; } = provider;
        public object Gate { get; } = new();
        public int Failures;
        public bool Disabled;
        public int Generation;
        public int FailedGeneration = -1;
    }
}
