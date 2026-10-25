using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;
using NotchWindows.App.Shell;
using NotchWindows.App.ViewModels;
using NotchWindows.Core;
using NotchWindows.Native;
using NotchWindows.Providers;

namespace NotchWindows.App;

// Composition root: providers -> EventBus -> IslandController -> IslandViewModel -> PillWindow.
public sealed class Bootstrap : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    private readonly ILogger _logger;
    private readonly List<IEventProvider> _providers = new();
    private readonly List<IDisposable> _sources = new();
    private DispatcherTimer? _tickTimer;
    private bool _disposed;

    private Bootstrap(ILogger logger) => _logger = logger;

    /// <summary>
    /// Builds every component, registers only the providers the hardware supports, attaches the
    /// view model to <paramref name="pill"/>, shows it, then starts the providers. UI thread only.
    /// </summary>
    public static Bootstrap Start(Application app, PillWindow pill, AppSettings settings, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(pill);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var bootstrap = new Bootstrap(loggerFactory.CreateLogger<Bootstrap>());
        bootstrap.Build(app.Dispatcher, pill, settings, loggerFactory);
        return bootstrap;
    }

    private void Build(Dispatcher dispatcher, PillWindow pill, AppSettings settings, ILoggerFactory loggerFactory)
    {
        var clock = new SystemClock();

        // Volume: always registered when the source can be built (no render device -> constructor throws).
        var volume = TryCreate("Volume", () => new NAudioVolumeSource());
        if (volume is not null)
            Register(new VolumeProvider(volume, clock), volume);

        // Brightness: laptop only (first Read() returns a value).
        var brightness = TryCreate("Brightness", () =>
            new WmiBrightnessSource() is var s && s.Read() is not null ? s : null);
        if (brightness is not null)
            Register(new BrightnessProvider(brightness, clock), null);

        // Battery: only when a battery is present.
        var power = TryCreate("Battery", () =>
        {
            var s = new PowerStatusSource();
            if (s.Read().HasBattery) return s;
            s.Dispose();
            return null;
        });
        if (power is not null)
            Register(new BatteryProvider(power, clock), power);

        // Media: always registered; no session is handled inside the source/provider.
        var media = TryCreate("Media", () => new SmtcMediaSource());
        if (media is not null)
            Register(new MediaProvider(media), media);

        // BeginInvoke (not Invoke): provider threads never block on the UI thread during an event
        // storm, and same-priority dispatcher operations run FIFO, so publish order is kept.
        var bus = new EventBus(action => dispatcher.BeginInvoke(action));
        var controller = new IslandController(new IslandQueue(clock), clock);
        var viewModel = new IslandViewModel(controller, dispatcher);

        // All controller mutations go through the view model wrappers, which Refresh afterwards.
        bus.Subscribe(viewModel.Publish);
        bus.SubscribeAmbient(viewModel.ApplyAmbient);

        pill.Attach(viewModel, settings);
        if (media is not null)
            pill.ConfigureMedia(media.TogglePlayPauseAsync, media.SkipNextAsync);

        _tickTimer = new DispatcherTimer(TickInterval, DispatcherPriority.Normal, (_, _) => viewModel.Tick(), dispatcher);

#if DEBUG
        // Subscribed before ProviderHost wires its handlers, so the stamp is taken first, on the provider thread.
        foreach (var p in _providers)
            p.Raised += Diagnostics.LatencyProbe.Stamp;
#endif

        var host = new ProviderHost(bus, _providers, RetryDelay, loggerFactory.CreateLogger<ProviderHost>());
        pill.Show();
        host.StartAll();
        _logger.LogInformation("Providers started: {Providers}", string.Join(", ", _providers.Select(p => p.Name)));
    }

    private void Register(IEventProvider provider, IDisposable? source)
    {
        _providers.Add(provider);
        if (source is not null)
            _sources.Add(source);
    }

    // Any exception from a source constructor or probe means "not registered", never a crash.
    private T? TryCreate<T>(string name, Func<T?> factory) where T : class
    {
        try
        {
            var source = factory();
            if (source is null)
                _logger.LogInformation("Provider {Provider} not registered: not supported on this hardware.", name);
            return source;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Provider} not registered: source failed to initialize.", name);
            return null;
        }
    }

    /// <summary>Stops every provider first, then disposes the sources.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tickTimer?.Stop();
        _tickTimer = null;

        foreach (var provider in _providers)
        {
            try { provider.Stop(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Provider {Provider} threw on Stop.", provider.Name); }
        }

        foreach (var source in _sources)
        {
            try { source.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Source {Source} threw on Dispose.", source.GetType().Name); }
        }
    }
}
