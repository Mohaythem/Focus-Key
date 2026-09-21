using System.Diagnostics;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class UiScaleKeyboardAndRapidPersistenceTests
{
    private static void ClearPool(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        SqliteConnection.ClearPool(connection);
    }

    #region 1. Rapid Persistence Stress: Burst of 50 Zoom Keypresses in 100ms

    [Fact]
    public async Task RapidPersistence_BurstOf50ZoomKeypressesIn100ms_CompletesWithoutLockingExceptionsOrDeadlock()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_burst_50.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        // Verify initial state is default 100%
        ApplicationSettings initial = await service.LoadAsync();
        Assert.Equal(100, initial.UiScalePercent);

        // Simulate user holding Ctrl+Plus or rapidly tapping zoom keys:
        // 50 rapid calls dispatched within ~100ms, exactly mirroring MainWindow.ApplyUiScale -> PersistUiScale
        const int burstCount = 50;
        int[] discreteLevels = UiScaleLevels.All; // [80, 90, 100, 110, 125, 150]
        var persistenceTasks = new List<Task<ApplicationSettings>>(burstCount);

        var stopwatch = Stopwatch.StartNew();

        int simulatedCurrentScale = 100;
        for (int i = 0; i < burstCount; i++)
        {
            // Alternate stepping up, stepping down, or resetting
            if (i % 3 == 0)
            {
                simulatedCurrentScale = UiScaleLevels.NextLevel(simulatedCurrentScale);
            }
            else if (i % 3 == 1)
            {
                simulatedCurrentScale = UiScaleLevels.PreviousLevel(simulatedCurrentScale);
            }
            else
            {
                simulatedCurrentScale = UiScaleLevels.DefaultPercent;
            }

            int scaleToPersist = simulatedCurrentScale;
            persistenceTasks.Add(service.UpdateUiScalePercentAsync(scaleToPersist));

            // Small delay between some dispatches to model ~100ms total burst time
            if (i % 10 == 0)
            {
                await Task.Delay(2);
            }
        }

        long dispatchDurationMs = stopwatch.ElapsedMilliseconds;

        // Await all 50 operations to complete; none must throw SQLite locking exceptions (SQLITE_BUSY) or deadlock
        ApplicationSettings[] results = await Task.WhenAll(persistenceTasks);

        stopwatch.Stop();

        // Assert all 50 operations completed successfully
        Assert.Equal(burstCount, results.Length);
        foreach (var result in results)
        {
            Assert.True(UiScaleLevels.IsValid(result.UiScalePercent),
                $"Persisted scale {result.UiScalePercent} must be one of the discrete valid levels.");
        }

        // Verify that the final in-memory result matches the last operation's target
        Assert.Equal(simulatedCurrentScale, results[^1].UiScalePercent);

        // Cold verification directly from disk with a fresh repository and cleared connection pool
        ClearPool(connections);
        var coldRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings coldLoaded = await coldRepo.LoadAsync();

        Assert.Equal(simulatedCurrentScale, coldLoaded.UiScalePercent);

        ClearPool(connections);
    }

    [Fact]
    public async Task RapidPersistence_HighFrequencySteppingUpToMaxAndClamping_PreservesClampedInvariants()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_burst_clamp_max.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        // 30 rapid zoom-in keypresses starting from 100%
        int currentScale = 100;
        var tasks = new List<Task<ApplicationSettings>>();
        for (int i = 0; i < 30; i++)
        {
            currentScale = UiScaleLevels.NextLevel(currentScale);
            tasks.Add(service.UpdateUiScalePercentAsync(currentScale));
        }

        ApplicationSettings[] results = await Task.WhenAll(tasks);

        // After 4 steps from 100 (110 -> 125 -> 150 -> 150...), all subsequent calls must be clamped at 150
        Assert.Equal(150, currentScale);
        Assert.Equal(150, results[^1].UiScalePercent);

        // Verify disk state
        ClearPool(connections);
        var coldRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings coldLoaded = await coldRepo.LoadAsync();
        Assert.Equal(150, coldLoaded.UiScalePercent);

        ClearPool(connections);
    }

    [Fact]
    public async Task RapidPersistence_HighFrequencySteppingDownToMinAndClamping_PreservesClampedInvariants()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_burst_clamp_min.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        // 30 rapid zoom-out keypresses starting from 100%
        int currentScale = 100;
        var tasks = new List<Task<ApplicationSettings>>();
        for (int i = 0; i < 30; i++)
        {
            currentScale = UiScaleLevels.PreviousLevel(currentScale);
            tasks.Add(service.UpdateUiScalePercentAsync(currentScale));
        }

        ApplicationSettings[] results = await Task.WhenAll(tasks);

        // After 2 steps from 100 (90 -> 80 -> 80...), all subsequent calls must be clamped at 80
        Assert.Equal(80, currentScale);
        Assert.Equal(80, results[^1].UiScalePercent);

        // Verify disk state
        ClearPool(connections);
        var coldRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings coldLoaded = await coldRepo.LoadAsync();
        Assert.Equal(80, coldLoaded.UiScalePercent);

        ClearPool(connections);
    }

    #endregion

    #region 2. Virtual Key Code Combinations Oracle & Verification

    /// <summary>
    /// Oracle mirroring MainWindow.xaml.cs OnMainSurfacePreviewKeyDown key evaluation logic.
    /// </summary>
    private static bool EvaluateKeyMatching(
        int rawKey,
        int rawOriginalKey,
        bool isCtrl,
        bool isAlt,
        bool isWin,
        out string action)
    {
        action = "None";

        if (isCtrl && !isAlt && !isWin)
        {
            bool isZoomIn = rawKey == 187 || rawOriginalKey == 187 ||
                            rawKey == 0xBB || rawOriginalKey == 0xBB ||
                            rawKey == 107 || rawOriginalKey == 107 || rawKey == 0x6B || rawOriginalKey == 0x6B;

            bool isZoomOut = rawKey == 189 || rawOriginalKey == 189 ||
                             rawKey == 0xBD || rawOriginalKey == 0xBD ||
                             rawKey == 109 || rawOriginalKey == 109 || rawKey == 0x6D || rawOriginalKey == 0x6D;

            bool isReset = rawKey == 48 || rawOriginalKey == 48 ||
                           rawKey == 96 || rawOriginalKey == 96 ||
                           rawKey == 0x30 || rawKey == 0x60 ||
                           rawOriginalKey == 0x30 || rawOriginalKey == 0x60;

            if (isZoomIn) { action = "ZoomIn"; return true; }
            if (isZoomOut) { action = "ZoomOut"; return true; }
            if (isReset) { action = "Reset"; return true; }
        }

        return false;
    }

    [Theory]
    // Standard keyboard + / = key (VK_OEM_PLUS = 187 / 0xBB)
    [InlineData(187, 187, "ZoomIn")]
    [InlineData(187, 0, "ZoomIn")]
    [InlineData(0, 187, "ZoomIn")]
    // Numeric keypad + key (VK_ADD = 107 / 0x6B)
    [InlineData(107, 107, "ZoomIn")]
    [InlineData(107, 0, "ZoomIn")]
    [InlineData(0, 107, "ZoomIn")]
    // Standard keyboard - / _ key (VK_OEM_MINUS = 189 / 0xBD)
    [InlineData(189, 189, "ZoomOut")]
    [InlineData(189, 0, "ZoomOut")]
    [InlineData(0, 189, "ZoomOut")]
    // Numeric keypad - key (VK_SUBTRACT = 109 / 0x6D)
    [InlineData(109, 109, "ZoomOut")]
    [InlineData(109, 0, "ZoomOut")]
    [InlineData(0, 109, "ZoomOut")]
    // Standard keyboard 0 / ) key (VK_0 = 48 / 0x30)
    [InlineData(48, 48, "Reset")]
    [InlineData(48, 0, "Reset")]
    [InlineData(0, 48, "Reset")]
    // Numeric keypad 0 key (VK_NUMPAD0 = 96 / 0x60)
    [InlineData(96, 96, "Reset")]
    [InlineData(96, 0, "Reset")]
    [InlineData(0, 96, "Reset")]
    public void KeyCodeCombinations_StandardVsNumpad_ResolvesCorrectActions(int rawKey, int rawOriginalKey, string expectedAction)
    {
        bool handled = EvaluateKeyMatching(rawKey, rawOriginalKey, isCtrl: true, isAlt: false, isWin: false, out string action);
        Assert.True(handled);
        Assert.Equal(expectedAction, action);
    }

    [Theory]
    // Non-zoom keys with Ctrl must NEVER trigger zoom or reset
    [InlineData(65, 65)] // 'A'
    [InlineData(67, 67)] // 'C' (Ctrl+C Copy)
    [InlineData(86, 86)] // 'V' (Ctrl+V Paste)
    [InlineData(88, 88)] // 'X' (Ctrl+X Cut)
    [InlineData(90, 90)] // 'Z' (Ctrl+Z Undo)
    [InlineData(83, 83)] // 'S' (Ctrl+S Save)
    [InlineData(79, 79)] // 'O' (Ctrl+O Open)
    [InlineData(49, 49)] // '1'
    [InlineData(57, 57)] // '9'
    [InlineData(32, 32)] // Space
    [InlineData(13, 13)] // Enter
    public void KeyCodeCombinations_NonZoomKeysWithCtrl_FallThroughWithoutHandling(int rawKey, int rawOriginalKey)
    {
        bool handled = EvaluateKeyMatching(rawKey, rawOriginalKey, isCtrl: true, isAlt: false, isWin: false, out string action);
        Assert.False(handled);
        Assert.Equal("None", action);
    }

    #endregion

    #region 3. Modifier Combinations Matrix & AltGr Defense

    [Theory]
    // Valid modifier combination: Ctrl only
    [InlineData(true, false, false, true)]
    // Blocked modifier combinations:
    // Alt pressed (e.g. AltGr = Ctrl+Alt on international keyboards)
    [InlineData(true, true, false, false)]
    // Windows key pressed (Ctrl+Win)
    [InlineData(true, false, true, false)]
    // Ctrl + Alt + Win
    [InlineData(true, true, true, false)]
    // No Ctrl pressed (standard typing, +/- on numpad or keyboard without Ctrl)
    [InlineData(false, false, false, false)]
    // Alt only
    [InlineData(false, true, false, false)]
    // Win only
    [InlineData(false, false, true, false)]
    public void ModifierCombinations_MatrixVerification(bool isCtrl, bool isAlt, bool isWin, bool expectedHandled)
    {
        // Test with ZoomIn key (VK_OEM_PLUS 187)
        bool handledPlus = EvaluateKeyMatching(187, 187, isCtrl, isAlt, isWin, out string actionPlus);
        Assert.Equal(expectedHandled, handledPlus);
        Assert.Equal(expectedHandled ? "ZoomIn" : "None", actionPlus);

        // Test with ZoomOut key (VK_OEM_MINUS 189)
        bool handledMinus = EvaluateKeyMatching(189, 189, isCtrl, isAlt, isWin, out string actionMinus);
        Assert.Equal(expectedHandled, handledMinus);
        Assert.Equal(expectedHandled ? "ZoomOut" : "None", actionMinus);

        // Test with Reset key (VK_0 48)
        bool handledZero = EvaluateKeyMatching(48, 48, isCtrl, isAlt, isWin, out string actionZero);
        Assert.Equal(expectedHandled, handledZero);
        Assert.Equal(expectedHandled ? "Reset" : "None", actionZero);
    }

    [Fact]
    public void ModifierCombinations_AltGrSuppression_PreventsInadvertentZoomOnInternationalKeyboards()
    {
        // On European/German/Polish layouts, AltGr reports as both Control and Alt (0x11 and 0x12).
        // If isAlt were not checked, typing AltGr + Key could inadvertently trigger zoom actions.
        const bool isCtrlFromAltGr = true;
        const bool isAltFromAltGr = true;
        const bool isWin = false;

        bool handledPlus = EvaluateKeyMatching(187, 187, isCtrlFromAltGr, isAltFromAltGr, isWin, out string action);
        Assert.False(handledPlus);
        Assert.Equal("None", action);

        bool handledMinus = EvaluateKeyMatching(189, 189, isCtrlFromAltGr, isAltFromAltGr, isWin, out action);
        Assert.False(handledMinus);
        Assert.Equal("None", action);

        bool handledReset = EvaluateKeyMatching(48, 48, isCtrlFromAltGr, isAltFromAltGr, isWin, out action);
        Assert.False(handledReset);
        Assert.Equal("None", action);
    }

    #endregion

    #region 4. Text Input Suppression Type & Hierarchy Model Verification

    private interface IMockVisualObject
    {
        IMockVisualObject? Parent { get; }
    }

    private sealed class MockVisualNode(string typeName, IMockVisualObject? parent = null) : IMockVisualObject
    {
        public string TypeName { get; } = typeName;
        public IMockVisualObject? Parent { get; set; } = parent;
    }

    private static bool EvaluateIsTextInput(IMockVisualObject? element)
    {
        if (element is null) return false;

        IMockVisualObject? current = element;
        while (current is not null)
        {
            if (current is MockVisualNode node)
            {
                if (node.TypeName is "TextBox" or "PasswordBox" or "RichEditBox" or "AutoSuggestBox")
                    return true;
            }
            current = current.Parent;
        }

        return false;
    }

    [Theory]
    [InlineData("TextBox", true)]
    [InlineData("PasswordBox", true)]
    [InlineData("RichEditBox", true)]
    [InlineData("AutoSuggestBox", true)]
    [InlineData("Button", false)]
    [InlineData("Slider", false)]
    [InlineData("NavigationViewItem", false)]
    [InlineData("Grid", false)]
    [InlineData("Border", false)]
    [InlineData("TextBlock", false)]
    public void IsTextInput_DirectControlTypes_CorrectlyIdentified(string controlType, bool expectedIsTextInput)
    {
        var node = new MockVisualNode(controlType);
        Assert.Equal(expectedIsTextInput, EvaluateIsTextInput(node));
    }

    [Fact]
    public void IsTextInput_NullElement_ReturnsFalse()
    {
        Assert.False(EvaluateIsTextInput(null));
    }

    [Theory]
    // Internal elements inside a TextBox template (ScrollViewer -> Border -> ContentPresenter -> Grid -> TextBox)
    [InlineData("TextBox", true)]
    [InlineData("PasswordBox", true)]
    [InlineData("RichEditBox", true)]
    [InlineData("AutoSuggestBox", true)]
    public void IsTextInput_DeepNestedTemplateElements_SuccessfullyDetectsAncestorInput(string rootInputType, bool expected)
    {
        var root = new MockVisualNode(rootInputType);
        var border = new MockVisualNode("Border", root);
        var scrollViewer = new MockVisualNode("ScrollViewer", border);
        var contentPresenter = new MockVisualNode("ContentPresenter", scrollViewer);
        var internalGrid = new MockVisualNode("Grid", contentPresenter);

        // When focus or OriginalSource is the deeply nested internal Grid
        bool result = EvaluateIsTextInput(internalGrid);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTextInput_DeepNestedNonInputElements_ReturnsFalse()
    {
        var page = new MockVisualNode("TodayPage");
        var scrollViewer = new MockVisualNode("ScrollViewer", page);
        var mainGrid = new MockVisualNode("Grid", scrollViewer);
        var cardBorder = new MockVisualNode("Border", mainGrid);
        var button = new MockVisualNode("Button", cardBorder);
        var textBlock = new MockVisualNode("TextBlock", button);

        Assert.False(EvaluateIsTextInput(textBlock));
    }

    #endregion
}
