using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;
using FocusKey.Foundation.Sessions;

return await ShellProbe.RunAsync(args);

internal static class ShellProbe
{
    private const string ClassPrefix = "FocusKey.Shell.";
    private const uint WM_COMMAND = 0x0111;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_F3 = 0x72;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            return args[0].ToLowerInvariant() switch
            {
                "probe" => Probe(ParsePid(args)),
                "open" => PostCommand(ParsePid(args), 1),
                "exit" => PostCommand(ParsePid(args), 2),
                "menu" => OpenMenu(ParsePid(args)),
                "hotkey-free" => HotkeyFree(),
                "hold-hotkey" => HoldHotkey(),
                "seed" => await SeedAsync(args),
                "inspect" => await InspectAsync(args),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int Probe(int pid)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1)
            throw new InvalidOperationException($"Expected exactly one {ClassPrefix} top-level window for PID {pid}, found {windows.Count}.");
        var window = windows[0];
        var id = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = window, uID = 1 };
        int result = Shell_NotifyIconGetRect(ref id, out RECT rect);
        if (result != 0) throw new Win32Exception(result, "Shell_NotifyIconGetRect failed.");
        Console.WriteLine($"pid={pid} hwnd=0x{window.ToInt64():X} class={ClassName(window)} rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
        return 0;
    }

    private static int PostCommand(int pid, uint command)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1) throw new InvalidOperationException($"Expected exactly one {ClassPrefix} top-level window for PID {pid}, found {windows.Count}.");
        if (!PostMessage(windows[0], WM_COMMAND, (IntPtr)command, IntPtr.Zero)) throw LastError("PostMessage failed.");
        Console.WriteLine($"posted command={command} pid={pid} hwnd=0x{windows[0].ToInt64():X}");
        return 0;
    }

    private static int OpenMenu(int pid)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1) throw new InvalidOperationException("Expected one shell window.");
        // Exercise the actual NOTIFYICON_VERSION_4 notification boundary and native menu.
        if (!PostMessage(windows[0], 0x0400 + 71, IntPtr.Zero, (IntPtr)((1 << 16) | 0x007B)))
            throw LastError("Could not open the native tray menu.");
        Console.WriteLine("posted version-4 context-menu callback");
        return 0;
    }

    private static int HotkeyFree()
    {
        int id = 0x534B;
        if (!RegisterHotKey(IntPtr.Zero, id, MOD_SHIFT | MOD_NOREPEAT, VK_F3)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Shift+F3 is not free.");
        try { Console.WriteLine("hotkey-free"); return 0; }
        finally { UnregisterHotKey(IntPtr.Zero, id); }
    }

    private static int HoldHotkey()
    {
        int id = 0x534C;
        if (!RegisterHotKey(IntPtr.Zero, id, MOD_SHIFT | MOD_NOREPEAT, VK_F3)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Shift+F3 is not free.");
        try { Console.WriteLine("HELD"); Console.Out.Flush(); Console.ReadLine(); return 0; }
        finally { UnregisterHotKey(IntPtr.Zero, id); }
    }

    private static async Task<int> SeedAsync(string[] args)
    {
        if (args.Length != 3 || (args[2] != "future" && args[2] != "due")) throw new ArgumentException("Usage: seed <dataRoot> <future|due>");
        AppPaths paths = GuardedPaths(args[1]); paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.DatabaseFile);
        new DatabaseBootstrapper(factory, NullAppLogger.Instance).Initialize();
        var now = WholeSecond(DateTimeOffset.UtcNow);
        DateTimeOffset started = args[2] == "future" ? now : now - TimeSpan.FromHours(1);
        TimeSpan planned = args[2] == "future" ? TimeSpan.FromHours(2) : TimeSpan.FromMinutes(30);
        var session = new SessionRecord { Id = SessionId.New(), Type = SessionType.Work, Status = SessionStatus.Running, StartedAt = started, PlannedDuration = planned, CreatedAt = now };
        await new SqliteSessionRepository(factory).AddAsync(session);
        Console.WriteLine($"id={session.Id} plannedEnd={session.PlannedEndAt:O}");
        return 0;
    }

    private static async Task<int> InspectAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: inspect <dataRoot>");
        AppPaths paths = GuardedPaths(args[1]);
        var repository = new SqliteSessionRepository(new SqliteConnectionFactory(paths.DatabaseFile));
        var rows = await repository.GetStartedBetweenAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        foreach (var row in rows) Console.WriteLine($"{row.Id} {row.Type} {row.Status} started={row.StartedAt:O} plannedEnd={row.PlannedEndAt:O} ended={row.EndedAt:O}");
        return 0;
    }

    private static AppPaths GuardedPaths(string root)
    {
        string baseRoot = Path.GetFullPath(@"D:\Focus Key\.smoke");
        string full = Path.GetFullPath(root);
        if (!full.StartsWith(baseRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Data root must be strictly under '{baseRoot}'.");
        return AppPaths.ForRoot(full);
    }
    private static DateTimeOffset WholeSecond(DateTimeOffset value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    private static int ParsePid(string[] args) => args.Length == 2 && int.TryParse(args[1], out int pid) && pid > 0 ? pid : throw new ArgumentException("Usage: <probe|open|exit> <pid>");
    private static int Usage() { Console.Error.WriteLine("Usage: probe|open|exit <pid> | hotkey-free | hold-hotkey | seed <dataRoot> <future|due> | inspect <dataRoot>"); return 2; }
    private static Win32Exception LastError(string message) => new(Marshal.GetLastWin32Error(), message);

    private static List<IntPtr> FindWindows(int pid)
    {
        var result = new List<IntPtr>();
        EnumWindows((window, _) => { GetWindowThreadProcessId(window, out uint owner); if (owner == pid && ClassName(window).StartsWith(ClassPrefix, StringComparison.Ordinal)) result.Add(window); return true; }, IntPtr.Zero);
        return result;
    }
    private static string ClassName(IntPtr window) { var buffer = new StringBuilder(256); int length = GetClassName(window, buffer, buffer.Capacity); return length == 0 ? string.Empty : buffer.ToString(); }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NOTIFYICONIDENTIFIER { internal uint cbSize; internal IntPtr hWnd; internal uint uID; internal Guid guidItem; }
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);
}
