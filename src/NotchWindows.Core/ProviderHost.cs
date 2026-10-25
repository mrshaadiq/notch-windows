using Microsoft.Extensions.Logging;

namespace NotchWindows.Core;

public sealed class ProviderHost(
    EventBus bus,
    IEnumerable<IEventProvider> providers,
    TimeSpan retryDelay,
    ILogger<ProviderHost> logger)
{
    private const int MaxFailures = 3;

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
            state.Provider.Faulted += ex => Fail(state, ex, fromStartThrow: false);
        }

        foreach (var s in _states)
        {
            try { s.Provider.Start(); }
            catch (Exception ex) { Fail(s, ex, fromStartThrow: true); }
        }
    }

    private static void ResetFailures(ProviderState s)
    {
        lock (s.Gate) s.Failures = 0;
    }

    // Faults raised while a restart is in flight are coalesced into it (not counted twice).
    private void Fail(ProviderState s, Exception ex, bool fromStartThrow)
    {
        lock (s.Gate)
        {
            if (s.Disabled) return;
            if (!fromStartThrow && s.Restarting) return;

            if (s.Provider.Status == ProviderStatus.Unavailable)
            {
                s.Restarting = false;
                logger.LogInformation("Provider {Provider} is unavailable; not restarting.", s.Provider.Name);
                return;
            }

            s.Failures++;
            if (s.Failures >= MaxFailures)
            {
                s.Disabled = true;
                s.Restarting = false;
                logger.LogWarning(ex, "Provider {Provider} disabled for this session after {Count} consecutive failures.",
                    s.Provider.Name, s.Failures);
                return;
            }

            s.Restarting = true;
            logger.LogWarning(ex, "Provider {Provider} failed ({Count}/{Max}); restarting.",
                s.Provider.Name, s.Failures, MaxFailures);
        }

        _ = Task.Run(() => RestartAsync(s));
    }

    private async Task RestartAsync(ProviderState s)
    {
        await Task.Delay(retryDelay).ConfigureAwait(false);

        try { s.Provider.Stop(); }
        catch (Exception ex) { logger.LogDebug(ex, "Provider {Provider} threw on Stop during restart.", s.Provider.Name); }

        try { s.Provider.Start(); }
        catch (Exception ex)
        {
            Fail(s, ex, fromStartThrow: true);
            return;
        }

        lock (s.Gate) s.Restarting = false;
    }

    private sealed class ProviderState(IEventProvider provider)
    {
        public IEventProvider Provider { get; } = provider;
        public object Gate { get; } = new();
        public int Failures;
        public bool Disabled;
        public bool Restarting;
    }
}
