namespace FocusKey.Foundation.Shell;

public enum ShellActivationKind { ShowWindow, Hotkey, MiniTimer }

/// <summary>Platform resources and events. All calls and events belong to the application UI thread.</summary>
public interface IShellIntegration : IDisposable
{
    event Action<ShellActivationKind>? ActivationRequested;
    event Action? ExitRequested;
    event Action<Exception>? ErrorOccurred;
    void Start();
}
