using System.ComponentModel;
using System.Runtime.InteropServices;
using FocusKey.Foundation.Shell;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;

namespace FocusKey.Shell;

internal sealed class WindowsShellIntegration : IShellIntegration
{
    private const int OverlayHotkeyId = 0x464B;
    private const int MainWindowHotkeyId = 0x464C;
    private const int HotkeyId = OverlayHotkeyId;
    private const uint TrayCallback = 0x0400 + 71;
    private const uint OpenCommand = 1;
    private const uint ExitCommand = 2;
    private const uint MiniTimerCommand = 3;
    private readonly NativeMethods.WndProc _windowProcedure;
    private readonly string _className = $"FocusKey.Shell.{Guid.NewGuid():N}";
    private IntPtr _instance;
    private IntPtr _window;
    private IntPtr _icon;
    private IntPtr _powerRegistration;
    private bool _classRegistered;
    private bool _trayAdded;
    private bool _hotkeyRegistered;
    private bool _mainWindowHotkeyRegistered;
    private bool _started;
    private bool _disposed;
    private uint _taskbarCreated;

    public GlobalShortcut CurrentShortcut { get; private set; } = GlobalShortcut.Default;
    public GlobalShortcut CurrentMainWindowShortcut { get; private set; } = GlobalShortcut.DefaultMainWindow;

    public WindowsShellIntegration(GlobalShortcut? initialShortcut = null, GlobalShortcut? initialMainWindowShortcut = null)
    {
        _windowProcedure = WindowProcedure;
        if (initialShortcut is not null) CurrentShortcut = initialShortcut;
        if (initialMainWindowShortcut is not null) CurrentMainWindowShortcut = initialMainWindowShortcut;
    }

    public event Action<ShellActivationKind>? ActivationRequested;
    public event Action? ExitRequested;
    public event Action<Exception>? ErrorOccurred;
    public event Action? ClockChangedOrResumed;

    // Use the existing notification-area identity. Windows supplies its standard quiet sound
    // and owns banner styling, dismissal, and user notification suppression settings.
    public void NotifyCompleted(SessionRecord session)
    {
        if (session.Status != SessionStatus.Completed) throw new ArgumentException("Only completed sessions may notify.", nameof(session));
        if (!_started || _disposed) throw new InvalidOperationException("The Windows shell is not running.");
        var data = TrayData(NativeMethods.NIF_INFO);
        data.szInfoTitle = "Focus Key";
        data.szInfo = session.Type == SessionType.Work ? "Work session completed." : "Break session completed.";
        data.dwInfoFlags = NativeMethods.NIIF_USER | NativeMethods.NIIF_RESPECT_QUIET_TIME;
        data.hBalloonIcon = _icon;
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data))
        {
            // Notifications are best-effort. If Explorer fails, do not crash the app.
        }
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WindowsShellIntegration));
        if (_started) return;
        _instance = NativeMethods.GetModuleHandle(null);
        if (_instance == IntPtr.Zero) throw LastError("Could not resolve the application module.");
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreated == 0) throw LastError("Could not register the TaskbarCreated notification message.");
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(), lpfnWndProc = _windowProcedure,
            hInstance = _instance, hIcon = NativeMethods.LoadIcon(_instance, (IntPtr)NativeMethods.IDI_APPLICATION),
            hIconSm = NativeMethods.LoadIcon(_instance, (IntPtr)NativeMethods.IDI_APPLICATION),
            lpszClassName = _className, lpszMenuName = string.Empty
        };
        if (NativeMethods.RegisterClassEx(ref wc) == 0) throw LastError("Could not register the shell window class.");
        _classRegistered = true;
        try
        {
            _window = NativeMethods.CreateWindowEx(NativeMethods.WS_EX_TOOLWINDOW, _className, "Focus Key", NativeMethods.WS_POPUP,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_window == IntPtr.Zero) throw LastError("Could not create the shell window.");
            _powerRegistration = NativeMethods.RegisterSuspendResumeNotification(_window, 0); // DEVICE_NOTIFY_WINDOW_HANDLE
            if (_powerRegistration == IntPtr.Zero) throw LastError("Could not subscribe to system resume notifications.");
            uint fsModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(CurrentShortcut.Modifiers);
            if (!NativeMethods.RegisterHotKey(_window, OverlayHotkeyId, fsModifiers, CurrentShortcut.VirtualKey))
            {
                // Hotkey is a convenience; do not crash the application if it is taken.
                _hotkeyRegistered = false;
            }
            else
            {
                _hotkeyRegistered = true;
            }

            uint mwModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(CurrentMainWindowShortcut.Modifiers);
            if (!NativeMethods.RegisterHotKey(_window, MainWindowHotkeyId, mwModifiers, CurrentMainWindowShortcut.VirtualKey))
            {
                _mainWindowHotkeyRegistered = false;
            }
            else
            {
                _mainWindowHotkeyRegistered = true;
            }
            AddTrayIcon();
            _started = true;
        }
        catch { Rollback(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Rollback();
        ActivationRequested = null; ExitRequested = null; ErrorOccurred = null; ClockChangedOrResumed = null;
    }

    private void AddTrayIcon()
    {
        _icon = NativeMethods.LoadIcon(_instance, (IntPtr)NativeMethods.IDI_APPLICATION);
        if (_icon == IntPtr.Zero) _icon = NativeMethods.LoadIcon(IntPtr.Zero, (IntPtr)NativeMethods.IDI_APPLICATION);
        if (_icon == IntPtr.Zero) return;
        var data = TrayData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        for (int retry = 0; retry < 5; retry++)
        {
            if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
            {
                _trayAdded = true;
                data.uTimeoutOrVersion = NativeMethods.NIM_SETVERSION4;
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
                return;
            }
            Thread.Sleep(100);
        }
    }

    private NativeMethods.NOTIFYICONDATA TrayData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(), hWnd = _window, uID = 1,
        uFlags = flags, uCallbackMessage = TrayCallback, hIcon = _icon, szTip = "Focus Key",
        szInfo = string.Empty, szInfoTitle = string.Empty
    };

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // This instance delegate is rooted for the entire native window lifetime.
            if (_disposed) return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
            // Broadcasts reach this existing top-level shell window even when all UI is hidden.
            // Re-evaluate UTC deadlines after a clock change or an automatic/user resume.
            if (message == NativeMethods.WM_TIMECHANGE || (message == NativeMethods.WM_POWERBROADCAST &&
                wParam.ToInt64() is NativeMethods.PBT_APMRESUMESUSPEND or NativeMethods.PBT_APMRESUMEAUTOMATIC))
            {
                ClockChangedOrResumed?.Invoke();
                return message == NativeMethods.WM_POWERBROADCAST ? (IntPtr)1 : IntPtr.Zero;
            }
            if (message == _taskbarCreated && _started) { AddTrayIcon(); return IntPtr.Zero; }
            if (message == NativeMethods.WM_HOTKEY)
            {
                long id = wParam.ToInt64();
                if (id == OverlayHotkeyId) { RaiseActivation(ShellActivationKind.Hotkey); return IntPtr.Zero; }
                if (id == MainWindowHotkeyId) { RaiseActivation(ShellActivationKind.ShowWindow); return IntPtr.Zero; }
            }
            if (message == TrayCallback)
            {
                // NOTIFYICON_VERSION_4 packs event in LOWORD and icon id in HIWORD.
                int action = (int)(lParam.ToInt64() & 0xFFFF);
                if (action == NativeMethods.NIN_SELECT || action == NativeMethods.NIN_KEYSELECT)
                    RaiseActivation(ShellActivationKind.ShowWindow);
                else if (action == NativeMethods.WM_CONTEXTMENU) ShowMenu();
                return IntPtr.Zero;
            }
            if (message == NativeMethods.WM_COMMAND)
            {
                switch ((uint)(wParam.ToInt64() & 0xFFFF)) { case OpenCommand: RaiseActivation(ShellActivationKind.ShowWindow); break; case ExitCommand: RaiseExit(); break; case MiniTimerCommand: RaiseActivation(ShellActivationKind.MiniTimer); break; }
                return IntPtr.Zero;
            }
        }
        catch (Exception exception) { Report(exception); }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (_window == IntPtr.Zero) return;
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero) throw LastError("Could not create the Focus Key tray menu.");
        try
        {
            if (!NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)OpenCommand, "Open Focus Key") ||
                !NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)MiniTimerCommand, "Quick Overlay") ||
                !NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)ExitCommand, "Exit Focus Key"))
                throw LastError("Could not populate the Focus Key tray menu.");
            if (!NativeMethods.GetCursorPos(out var point)) throw LastError("Could not position the tray menu.");
            NativeMethods.SetForegroundWindow(_window);
            uint command = NativeMethods.TrackPopupMenu(menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON, point.X, point.Y, 0, _window, IntPtr.Zero);
            // Required for reliable menu dismissal on subsequent invocations.
            NativeMethods.PostMessage(_window, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (command != 0 && !NativeMethods.PostMessage(_window, NativeMethods.WM_COMMAND, (IntPtr)command, IntPtr.Zero))
                throw LastError("Could not route the tray command.");
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }
    private void RaiseActivation(ShellActivationKind kind) { try { ActivationRequested?.Invoke(kind); } catch (Exception e) { Report(e); } }
    private void RaiseExit() { try { ExitRequested?.Invoke(); } catch (Exception e) { Report(e); } }
    private void Report(Exception e) { try { ErrorOccurred?.Invoke(e); } catch { } }
    private static Win32Exception LastError(string message) => new(Marshal.GetLastWin32Error(), message);
    public bool TryUpdateHotkey(GlobalShortcut newShortcut, out string? error)
    {
        error = null;
        if (!newShortcut.IsValid(out string? validationError))
        {
            error = validationError;
            return false;
        }

        if (newShortcut == CurrentMainWindowShortcut)
        {
            error = "Quick Overlay shortcut cannot be identical to Open Focus Key shortcut.";
            return false;
        }

        if (_disposed)
        {
            error = "The shell integration has been disposed.";
            return false;
        }

        if (!_started || _window == IntPtr.Zero)
        {
            CurrentShortcut = newShortcut;
            return true;
        }

        // Unregister existing hotkey if currently registered
        if (_hotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_window, OverlayHotkeyId);
            _hotkeyRegistered = false;
        }

        uint fsModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(newShortcut.Modifiers);
        if (NativeMethods.RegisterHotKey(_window, OverlayHotkeyId, fsModifiers, newShortcut.VirtualKey))
        {
            _hotkeyRegistered = true;
            CurrentShortcut = newShortcut;
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        error = err == 1409
            ? $"The shortcut '{newShortcut}' is already in use by another application."
            : $"Failed to register shortcut '{newShortcut}' (Error {err}).";

        // Rollback to previous working shortcut
        uint prevModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(CurrentShortcut.Modifiers);
        if (NativeMethods.RegisterHotKey(_window, OverlayHotkeyId, prevModifiers, CurrentShortcut.VirtualKey))
        {
            _hotkeyRegistered = true;
        }

        return false;
    }

    public bool TryUpdateMainWindowHotkey(GlobalShortcut newShortcut, out string? error)
    {
        error = null;
        if (!newShortcut.IsValid(out string? validationError))
        {
            error = validationError;
            return false;
        }

        if (newShortcut == CurrentShortcut)
        {
            error = "Open Focus Key shortcut cannot be identical to Quick Overlay shortcut.";
            return false;
        }

        if (_disposed)
        {
            error = "The shell integration has been disposed.";
            return false;
        }

        if (!_started || _window == IntPtr.Zero)
        {
            CurrentMainWindowShortcut = newShortcut;
            return true;
        }

        // Unregister existing hotkey if currently registered
        if (_mainWindowHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_window, MainWindowHotkeyId);
            _mainWindowHotkeyRegistered = false;
        }

        uint fsModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(newShortcut.Modifiers);
        if (NativeMethods.RegisterHotKey(_window, MainWindowHotkeyId, fsModifiers, newShortcut.VirtualKey))
        {
            _mainWindowHotkeyRegistered = true;
            CurrentMainWindowShortcut = newShortcut;
            return true;
        }

        int err = Marshal.GetLastWin32Error();
        error = err == 1409
            ? $"The shortcut '{newShortcut}' is already in use by another application."
            : $"Failed to register shortcut '{newShortcut}' (Error {err}).";

        // Rollback to previous working shortcut
        uint prevModifiers = NativeMethods.MOD_NOREPEAT | MapModifiers(CurrentMainWindowShortcut.Modifiers);
        if (NativeMethods.RegisterHotKey(_window, MainWindowHotkeyId, prevModifiers, CurrentMainWindowShortcut.VirtualKey))
        {
            _mainWindowHotkeyRegistered = true;
        }

        return false;
    }

    private static uint MapModifiers(ShortcutModifiers modifiers)
    {
        uint result = 0;
        if ((modifiers & ShortcutModifiers.Alt) != 0) result |= NativeMethods.MOD_ALT;
        if ((modifiers & ShortcutModifiers.Control) != 0) result |= NativeMethods.MOD_CONTROL;
        if ((modifiers & ShortcutModifiers.Shift) != 0) result |= NativeMethods.MOD_SHIFT;
        if ((modifiers & ShortcutModifiers.Windows) != 0) result |= NativeMethods.MOD_WIN;
        return result;
    }

    private void Rollback()
    {
        if (_powerRegistration != IntPtr.Zero) NativeMethods.UnregisterSuspendResumeNotification(_powerRegistration);
        _powerRegistration = IntPtr.Zero;
        if (_hotkeyRegistered && _window != IntPtr.Zero) NativeMethods.UnregisterHotKey(_window, OverlayHotkeyId);
        _hotkeyRegistered = false;
        if (_mainWindowHotkeyRegistered && _window != IntPtr.Zero) NativeMethods.UnregisterHotKey(_window, MainWindowHotkeyId);
        _mainWindowHotkeyRegistered = false;
        if (_trayAdded && _window != IntPtr.Zero)
        {
            var data = TrayData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        }
        _trayAdded = false;
        if (_window != IntPtr.Zero) NativeMethods.DestroyWindow(_window);
        _window = IntPtr.Zero;
        if (_classRegistered) NativeMethods.UnregisterClass(_className, _instance);
        _classRegistered = false;
        _started = false;
    }
}
