namespace FocusKey.Foundation.Shell;

/// <summary>
/// An OS-owned named mutex lease. Acquire and dispose on the same thread (the app UI thread).
/// Process death abandons the mutex; a successor can acquire it without deleting stale files.
/// </summary>
public sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex _mutex;
    private readonly int _ownerThread;
    private bool _disposed;

    private SingleInstanceLease(Mutex mutex)
    {
        _mutex = mutex;
        _ownerThread = Environment.CurrentManagedThreadId;
    }

    public static SingleInstanceLease? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new SingleInstanceLease(mutex);
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Release single-instance ownership on its acquiring thread.");
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
