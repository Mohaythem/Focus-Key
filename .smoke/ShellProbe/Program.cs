using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;

return await ShellProbe.RunAsync(args);

internal static class ShellProbe
{
    private const string ClassPrefix = "FocusKey.Shell.";
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_HOTKEY = 0x0312;
    private const int FOCUS_KEY_HOTKEY_ID = 0x464B;
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
                "mini" => PostCommand(ParsePid(args), 3),
                "mini-probe" => ProbeMini(ParsePid(args), false),
                "mini-hide" => ProbeMini(ParsePid(args), true),
                "overlay-probe" => ProbeMini(ParsePid(args), false, true),
                "overlay-hide" => ProbeMini(ParsePid(args), true, true),
                "hotkey" => PostHotkey(ParsePid(args)),
                "exit" => PostCommand(ParsePid(args), 2),
                "menu" => OpenMenu(ParsePid(args)),
                "hotkey-free" => HotkeyFree(),
                "hold-hotkey" => HoldHotkey(),
                "seed" => await SeedAsync(args),
                "seed-reports" => await SeedReportsAsync(args),
                "set-durations" => await SetDurationsAsync(args),
                "set-appearance" => await SetAppearanceAsync(args),
                "inspect-settings" => await InspectSettingsAsync(args),
                "settings-write-failure" => SettingsWriteFailure(args),
                "set-colors" => await SetColorsAsync(args),
                "inspect" => await InspectAsync(args),
                "start-configured" => await StartConfiguredAsync(args),
                "start-short" => await StartShortAsync(args),
                "reevaluate" => PostClockSignal(ParsePid(args), false),
                "resume" => PostClockSignal(ParsePid(args), true),
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

    private static int ProbeMini(int pid, bool hide, bool overlay = false)
    {
        string expectedTitle = overlay ? "Focus Key — Quick Overlay" : "Focus Key Mini Timer";
        var matches = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (owner == pid && title.ToString() == expectedTitle) matches.Add(window);
            return true;
        }, IntPtr.Zero);
        if (matches.Count != 1) throw new InvalidOperationException($"Expected one Mini Timer, found {matches.Count}.");
        if (!IsWindowVisible(matches[0])) throw new InvalidOperationException("Mini Timer is hidden.");
        if (hide && !PostMessage(matches[0], 0x0010, IntPtr.Zero, IntPtr.Zero)) throw LastError("Could not hide Mini Timer.");
        if (!GetWindowRect(matches[0], out var rect)) throw LastError("Could not read Mini Timer bounds.");
        bool pinned = (GetWindowLongPtr(matches[0], -20).ToInt64() & 8) != 0;
        Console.WriteLine($"Mini Timer hwnd=0x{matches[0].ToInt64():X} visible; hide={hide}; pinned={pinned}; bounds={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}.");
        return 0;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    private static int PostCommand(int pid, uint command)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1) throw new InvalidOperationException($"Expected exactly one {ClassPrefix} top-level window for PID {pid}, found {windows.Count}.");
        if (!PostMessage(windows[0], WM_COMMAND, (IntPtr)command, IntPtr.Zero)) throw LastError("PostMessage failed.");
        Console.WriteLine($"posted command={command} pid={pid} hwnd=0x{windows[0].ToInt64():X}");
        return 0;
    }

    private static int PostHotkey(int pid)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1) throw new InvalidOperationException("Expected one shell window.");
        if (!PostMessage(windows[0], WM_HOTKEY, (IntPtr)FOCUS_KEY_HOTKEY_ID, IntPtr.Zero))
            throw LastError("Could not post the Focus Key hotkey message.");
        Console.WriteLine($"posted hotkey pid={pid}");
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
        foreach (var row in rows) Console.WriteLine($"{row.Id} {row.Type} {row.Status} started={row.StartedAt:O} duration={row.PlannedDuration.TotalSeconds:0}s plannedEnd={row.PlannedEndAt:O} ended={row.EndedAt:O}");
        return 0;
    }

    private static async Task<int> SetDurationsAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], out int workSeconds) || workSeconds <= 0 ||
            !int.TryParse(args[3], out int breakSeconds) || breakSeconds <= 0)
            throw new ArgumentException("Usage: set-durations <dataRoot> <workSeconds> <breakSeconds>");
        AppPaths paths = GuardedPaths(args[1]);
        if (!File.Exists(paths.DatabaseFile)) throw new ArgumentException("Use an initialized isolated smoke data root.");
        var settings = new SettingsService(new SqliteSettingsRepository(
            new SqliteConnectionFactory(paths.DatabaseFile)));
        ApplicationSettings current = await settings.LoadAsync();
        ApplicationSettings updated = current with
        {
            WorkDuration = TimeSpan.FromSeconds(workSeconds),
            BreakDuration = TimeSpan.FromSeconds(breakSeconds),
        };
        await settings.SaveAsync(updated);
        Console.WriteLine($"work={workSeconds}s break={breakSeconds}s");
        return 0;
    }

    private static async Task<int> InspectSettingsAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: inspect-settings <isolatedDataRoot>");
        var paths = GuardedPaths(args[1]);
        if (!File.Exists(paths.DatabaseFile)) throw new ArgumentException("Use an initialized smoke database.");
        var settings = await new SettingsService(new SqliteSettingsRepository(new SqliteConnectionFactory(paths.DatabaseFile))).LoadAsync();
        Console.WriteLine($"Work={settings.WorkDuration.TotalSeconds}s Break={settings.BreakDuration.TotalSeconds}s Appearance={settings.Appearance} WorkColor={settings.WorkColor} BreakColor={settings.BreakColor}");
        return 0;
    }

    private static int SettingsWriteFailure(string[] args)
    {
        if (args.Length != 3 || args[2] is not ("on" or "off"))
            throw new ArgumentException("Usage: settings-write-failure <isolatedDataRoot> <on|off>");
        var paths = GuardedPaths(args[1]);
        if (!File.Exists(paths.DatabaseFile)) throw new ArgumentException("Use an initialized smoke database.");
        using var connection = new SqliteConnectionFactory(paths.DatabaseFile).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = args[2] == "on"
            ? "CREATE TRIGGER IF NOT EXISTS smoke_reject_settings BEFORE UPDATE ON application_settings BEGIN SELECT RAISE(ABORT, 'isolated settings smoke failure'); END;"
            : "DROP TRIGGER IF EXISTS smoke_reject_settings;";
        command.ExecuteNonQuery();
        Console.WriteLine($"Settings write failure {args[2]} in isolated database.");
        return 0;
    }

    private static async Task<int> SetColorsAsync(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Usage: set-colors <dataRoot> <#RRGGBB work> <#RRGGBB break>");
        var work = HexColor.Parse(args[2]);
        var rest = HexColor.Parse(args[3]);
        AppPaths paths = GuardedPaths(args[1]);
        if (!File.Exists(paths.DatabaseFile)) throw new ArgumentException("Use an initialized isolated smoke data root.");
        var settings = new SettingsService(new SqliteSettingsRepository(new SqliteConnectionFactory(paths.DatabaseFile)));
        await settings.SaveAsync((await settings.LoadAsync()) with { WorkColor = work, BreakColor = rest });
        Console.WriteLine($"Work={work} Break={rest}");
        return 0;
    }

    private static async Task<int> SetAppearanceAsync(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Usage: set-appearance <dataRoot> <system|light|dark>");
        Appearance appearance = args[2].ToLowerInvariant() switch
        {
            "system" => Appearance.System,
            "light" => Appearance.Light,
            "dark" => Appearance.Dark,
            _ => throw new ArgumentException("Appearance must be system, light, or dark."),
        };
        AppPaths paths = GuardedPaths(args[1]);
        if (!File.Exists(paths.DatabaseFile)) throw new ArgumentException("Use an initialized isolated smoke data root.");
        var settings = new SettingsService(new SqliteSettingsRepository(
            new SqliteConnectionFactory(paths.DatabaseFile)));
        await settings.UpdateAppearanceAsync(appearance);
        Console.WriteLine($"appearance={appearance}");
        return 0;
    }

    private static async Task<int> SeedReportsAsync(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: seed-reports <freshDataRoot>");
        AppPaths paths = GuardedPaths(args[1]);
        if (Directory.Exists(paths.RootDirectory)) throw new ArgumentException("Reports fixture requires a fresh isolated root.");
        paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.DatabaseFile);
        new DatabaseBootstrapper(factory, NullAppLogger.Instance).Initialize();
        var repository = new SqliteSessionRepository(factory);
        var zone = TimeZoneInfo.Local;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        async Task Add(DateOnly date, int minute, SessionType type, SessionStatus status, int length)
        {
            var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
            var start = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            var stamp = new DateTimeOffset(start);
            await repository.AddAsync(new SessionRecord { Id = SessionId.New(), Type = type, Status = status,
                StartedAt = stamp, CreatedAt = stamp, PlannedDuration = TimeSpan.FromMinutes(length),
                EndedAt = stamp.AddMinutes(status == SessionStatus.Completed ? length : 1) });
        }
        await Add(today, 0, SessionType.Work, SessionStatus.Completed, 30);
        await Add(today, 30, SessionType.Break, SessionStatus.Completed, 10);
        await Add(today, 40, SessionType.Work, SessionStatus.Stopped, 30);
        await Add(today, 42, SessionType.Break, SessionStatus.Interrupted, 10);
        await Add(today.AddDays(-7), 0, SessionType.Work, SessionStatus.Completed, 60);
        await Add(new DateOnly(today.Year, today.Month, 1).AddDays(-1), 0, SessionType.Work, SessionStatus.Completed, 90);
        Console.WriteLine($"Reports fixture local date {today:yyyy-MM-dd}: Work 30m, Break 10m, 1 completed each, 1 Stopped, 1 Interrupted, 50% completion. Prior week +60m, prior month +90m.");
        return 0;
    }

    private static async Task<int> StartShortAsync(string[] args)
    {
        if (args.Length != 5 || !int.TryParse(args[1], out int pid) || pid <= 0 ||
            !int.TryParse(args[4], out int seconds) || seconds is < 1 or > 120 ||
            args[3] is not ("work" or "break"))
            throw new ArgumentException("Usage: start-short <pid> <dataRoot> <work|break> <seconds 1..120>");
        AppPaths paths = GuardedPaths(args[2]);
        if (!File.Exists(paths.DatabaseFile) || !File.Exists(paths.LogFile))
            throw new ArgumentException("Use an existing isolated smoke data root.");
        using var logStream = new FileStream(paths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var logReader = new StreamReader(logStream);
        if (!logReader.ReadToEnd().Contains($"(process {pid}).", StringComparison.Ordinal))
            throw new ArgumentException("Use the existing isolated data root belonging to the running smoke app.");
        if (FindWindows(pid).Count != 1) throw new InvalidOperationException("Expected one live Focus Key shell window.");
        var repository = new SqliteSessionRepository(new SqliteConnectionFactory(paths.DatabaseFile));
        var duration = TimeSpan.FromSeconds(seconds);
        var engine = new SessionEngine(repository, TimeProvider.System, new SessionDurations(duration, duration));
        var session = await engine.StartAsync(args[3] == "work" ? SessionType.Work : SessionType.Break);
        Console.WriteLine($"{session.Id} {session.Type} started={session.StartedAt:O} plannedEnd={session.PlannedEndAt:O}");
        return PostClockSignal(pid, false);
    }

    private static async Task<int> StartConfiguredAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int pid) || pid <= 0 ||
            args[3] is not ("work" or "break"))
            throw new ArgumentException("Usage: start-configured <pid> <dataRoot> <work|break>");
        AppPaths paths = GuardedPaths(args[2]);
        if (!File.Exists(paths.DatabaseFile) || FindWindows(pid).Count != 1)
            throw new ArgumentException("Use the isolated data root belonging to a running smoke app.");
        var connections = new SqliteConnectionFactory(paths.DatabaseFile);
        var settings = new SettingsService(new SqliteSettingsRepository(connections));
        var engine = new SessionEngine(new SqliteSessionRepository(connections), TimeProvider.System,
            durationProvider: new SettingsSessionDurationProvider(settings));
        SessionRecord session = await engine.StartAsync(
            args[3] == "work" ? SessionType.Work : SessionType.Break);
        Console.WriteLine($"{session.Id} {session.Type} duration={session.PlannedDuration.TotalSeconds:0}s plannedEnd={session.PlannedEndAt:O}");
        return PostClockSignal(pid, false);
    }

    private static int PostClockSignal(int pid, bool resume)
    {
        var windows = FindWindows(pid);
        if (windows.Count != 1) throw new InvalidOperationException("Expected one Focus Key shell window.");
        if (!PostMessage(windows[0], resume ? 0x0218u : 0x001Eu, resume ? (IntPtr)18 : IntPtr.Zero, IntPtr.Zero))
            throw LastError("Could not post completion evaluation signal.");
        Console.WriteLine($"posted {(resume ? "resume" : "clock-change")} pid={pid}");
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
    private static int Usage() { Console.Error.WriteLine("Usage: probe|open|hotkey|exit|reevaluate|resume <pid> | hotkey-free | hold-hotkey | seed <dataRoot> <future|due> | seed-reports <freshDataRoot> | set-durations <dataRoot> <workSeconds> <breakSeconds> | set-appearance <dataRoot> <system|light|dark> | inspect <dataRoot> | start-configured <pid> <dataRoot> <work|break> | start-short <pid> <dataRoot> <work|break> <seconds 1..120>"); return 2; }
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
