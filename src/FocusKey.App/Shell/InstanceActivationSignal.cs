using Microsoft.UI.Dispatching;

namespace FocusKey.Shell;

/// <summary>A queued wake-up signal, created before ownership acquisition so startup races retain it.</summary>
internal sealed class InstanceActivationSignal : IDisposable
{
    private readonly EventWaitHandle _event;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    internal InstanceActivationSignal(string name) =>
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name);

    internal void Send() => _event.Set();

    internal void Listen(DispatcherQueue dispatcher, Action activate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) =>
        {
            dispatcher.TryEnqueue(() => { if (!_disposed) activate(); });
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        _event.Dispose();
    }
}
