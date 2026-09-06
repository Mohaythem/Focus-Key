namespace FocusKey.Foundation.Shell;

/// <summary>
/// Orders shell resources around session shutdown. UI-thread confined except for async awaits;
/// owns no session state and forwards activation without implementing any activation UI.
/// </summary>
public sealed class BackgroundShell : IDisposable
{
    private readonly IShellIntegration _integration;
    private readonly Func<Task> _shutdown;
    private readonly SemaphoreSlim _exitGate = new(1, 1);
    private bool _started;
    private bool _exiting;
    private bool _disposed;

    public BackgroundShell(IShellIntegration integration, Func<Task> shutdown)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentNullException.ThrowIfNull(shutdown);
        _integration = integration;
        _shutdown = shutdown;
    }

    public event Action<ShellActivationKind>? ActivationRequested;
    public event Action? Exited;
    public event Action<Exception>? ErrorOccurred;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _integration.ActivationRequested += OnActivation;
        _integration.ExitRequested += OnExitRequested;
        _integration.ErrorOccurred += OnError;
        try
        {
            _integration.Start();
            _started = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async Task ExitAsync()
    {
        await _exitGate.WaitAsync();
        try
        {
            if (_disposed) return;
            if (!_started) throw new InvalidOperationException("Start the shell before requesting exit.");
            _exiting = true;
            // Keep native resources and ownership alive if persistence fails, allowing retry.
            await _shutdown();
            Dispose();
            Exited?.Invoke();
        }
        finally
        {
            _exiting = false;
            _exitGate.Release();
        }
    }

    public void RequestActivation(ShellActivationKind kind)
    {
        if (_started && !_exiting && !_disposed) ActivationRequested?.Invoke(kind);
    }

    private void OnActivation(ShellActivationKind kind) => RequestActivation(kind);

    private async void OnExitRequested()
    {
        try { await ExitAsync(); }
        catch (Exception exception) { OnError(exception); }
    }

    private void OnError(Exception exception) => ErrorOccurred?.Invoke(exception);

    /// <summary>Releases platform resources on startup failure or after coordinated shutdown.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _integration.ActivationRequested -= OnActivation;
        _integration.ExitRequested -= OnExitRequested;
        _integration.ErrorOccurred -= OnError;
        _integration.Dispose();
    }
}
