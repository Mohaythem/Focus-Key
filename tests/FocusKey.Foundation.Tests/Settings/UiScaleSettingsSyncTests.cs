using FocusKey.Foundation.Settings;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class UiScaleSettingsSyncTests
{
    private static readonly string[] UiScaleOptions = ["80%", "90%", "100%", "110%", "125%", "150%"];

    [Theory]
    [InlineData("80%", 80)]
    [InlineData("90%", 90)]
    [InlineData("100%", 100)]
    [InlineData("110%", 110)]
    [InlineData("125%", 125)]
    [InlineData("150%", 150)]
    public void StringParsing_MatchesDiscreteSupportedLevels(string text, int expected)
    {
        Assert.True(text.EndsWith('%'));
        bool success = int.TryParse(text.TrimEnd('%'), out int parsed);
        Assert.True(success);
        Assert.Equal(expected, parsed);
        Assert.True(UiScaleLevels.IsValid(parsed));
    }

    [Fact]
    public void UiScaleOptions_ContainsAllDiscreteLevelsInAscendingOrder()
    {
        Assert.Equal(UiScaleLevels.All.Length, UiScaleOptions.Length);
        for (int i = 0; i < UiScaleOptions.Length; i++)
        {
            Assert.Equal($"{UiScaleLevels.All[i]}%", UiScaleOptions[i]);
        }
    }

    [Theory]
    [InlineData(80, 0)]
    [InlineData(90, 1)]
    [InlineData(100, 2)]
    [InlineData(110, 3)]
    [InlineData(125, 4)]
    [InlineData(150, 5)]
    public void PercentToIndex_MapsToCorrectUiScaleOption(int percent, int expectedIndex)
    {
        string target = $"{percent}%";
        int idx = Array.IndexOf(UiScaleOptions, target);
        Assert.Equal(expectedIndex, idx);
    }

    [Theory]
    [InlineData(0, 80)]
    [InlineData(50, 80)]
    [InlineData(79, 80)]
    [InlineData(105, 100)]
    [InlineData(160, 150)]
    [InlineData(-10, 80)]
    public void UnrecognizedOrBoundaryPercents_ClampToValidLevels(int invalidPercent, int expectedClamped)
    {
        int clamped = UiScaleLevels.IsValid(invalidPercent)
            ? invalidPercent
            : (invalidPercent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (invalidPercent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

        Assert.Equal(expectedClamped, clamped);
        Assert.True(UiScaleLevels.IsValid(clamped));
    }

    [Fact]
    public async Task Controller_UpdatesUiScale_AndNotifiesRuntime()
    {
        var repo = new MemorySettingsRepo();
        int runtimeRefreshes = 0;
        var controller = new SettingsPageController(
            new SettingsService(repo),
            () => { runtimeRefreshes++; return Task.CompletedTask; },
            _ => Assert.Fail("Unexpected error in controller"));

        await controller.LoadAsync();
        Assert.Equal(1, runtimeRefreshes);
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        var settledFields = new List<SettingsField>();
        controller.Settled += (f, _) => settledFields.Add(f);

        await controller.UpdateUiScaleAsync(125);
        Assert.Equal(2, runtimeRefreshes);
        Assert.Equal(125, controller.Saved.UiScalePercent);
        Assert.Equal(125, repo.Current.UiScalePercent);
        Assert.Contains(SettingsField.UiScale, settledFields);

        await controller.UpdateUiScaleAsync(80);
        Assert.Equal(3, runtimeRefreshes);
        Assert.Equal(80, controller.Saved.UiScalePercent);
        Assert.Equal(80, repo.Current.UiScalePercent);

        await controller.UpdateUiScaleAsync(150);
        Assert.Equal(4, runtimeRefreshes);
        Assert.Equal(150, controller.Saved.UiScalePercent);
        Assert.Equal(150, repo.Current.UiScalePercent);
    }

    [Fact]
    public void ApplyingGuardFlag_PreventsReentrantSelectionChangedLoops()
    {
        // Simulates the bidirectional synchronization pattern implemented in SettingsView
        bool applying = false;
        int simulatedSaveCalls = 0;
        int currentSelectionIndex = 2; // "100%"

        void OnSelectionChanged(int newIndex)
        {
            if (applying) return;
            simulatedSaveCalls++;
        }

        void ApplyUiScale(int percent)
        {
            applying = true;
            try
            {
                int clamped = UiScaleLevels.IsValid(percent) ? percent : UiScaleLevels.DefaultPercent;
                int idx = Array.IndexOf(UiScaleOptions, $"{clamped}%");
                if (idx >= 0 && currentSelectionIndex != idx)
                {
                    currentSelectionIndex = idx;
                    OnSelectionChanged(idx);
                }
            }
            finally
            {
                applying = false;
            }
        }

        // Programmatic update via shortcut should NOT increment simulatedSaveCalls
        ApplyUiScale(125);
        Assert.Equal(4, currentSelectionIndex);
        Assert.Equal(0, simulatedSaveCalls);

        ApplyUiScale(150);
        Assert.Equal(5, currentSelectionIndex);
        Assert.Equal(0, simulatedSaveCalls);

        // User manual change (applying is false) DOES trigger save
        OnSelectionChanged(0);
        Assert.Equal(1, simulatedSaveCalls);
    }

    private sealed class MemorySettingsRepo : ISettingsRepository
    {
        public ApplicationSettings Current = ApplicationSettings.Default;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            settings.Validate();
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
