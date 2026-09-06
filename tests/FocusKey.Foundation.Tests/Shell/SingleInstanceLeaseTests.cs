using FocusKey.Foundation.Shell;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FocusKey.Foundation.Tests.Shell;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void FirstAcquires_CompetingCannot_ReleaseAllowsNext()
    {
        string name = "FocusKey.Tests." + Guid.NewGuid();
        SingleInstanceLease first = SingleInstanceLease.TryAcquire(name)!;
        Assert.NotNull(first);
        try
        {
            SingleInstanceLease? competing = null;
            var thread = new Thread(() => competing = SingleInstanceLease.TryAcquire(name));
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(competing);
            first.Dispose();
            first = null!;
            SingleInstanceLease second = SingleInstanceLease.TryAcquire(name)!;
            Assert.NotNull(second);
            second.Dispose();
        }
        finally { first?.Dispose(); }
    }

    [Fact]
    public void DisposeOnWrongThreadIsRejected_AndOwnerCanRelease()
    {
        string name = "FocusKey.Tests." + Guid.NewGuid();
        SingleInstanceLease lease = SingleInstanceLease.TryAcquire(name)!;
        try
        {
            Assert.NotNull(lease);
            Exception? error = null;
            var thread = new Thread(() => { try { lease.Dispose(); } catch (Exception exception) { error = exception; } });
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.IsType<InvalidOperationException>(error);
        }
        finally { lease.Dispose(); }
    }

    [Fact]
    public void AbandonedMutexCanBeAcquiredBySuccessorThread()
    {
        string name = "FocusKey.Tests." + Guid.NewGuid();
        using var acquired = new ManualResetEventSlim();
        Mutex? abandoned = null;
        SafeWaitHandle? nativeThread = null;
        var thread = new Thread(() =>
        {
            nativeThread = new SafeWaitHandle(OpenThread(0x00100000, false, GetCurrentThreadId()), ownsHandle: true);
            abandoned = new Mutex(false, name);
            abandoned.WaitOne();
            acquired.Set();
        });
        thread.Start();
        Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        SingleInstanceLease? successor = null;
        try
        {
            Assert.NotNull(nativeThread);
            Assert.False(nativeThread.IsInvalid);
            // Thread.Join observes managed completion; wait for native thread teardown too.
            Assert.Equal(0u, WaitForSingleObject(nativeThread, 5000));
            successor = SingleInstanceLease.TryAcquire(name);
            Assert.NotNull(successor);
        }
        finally
        {
            successor?.Dispose();
            abandoned?.Dispose();
            nativeThread?.Dispose();
        }
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint access, bool inheritHandle, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
