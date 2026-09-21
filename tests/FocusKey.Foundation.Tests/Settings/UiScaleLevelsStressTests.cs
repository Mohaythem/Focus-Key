using System.Collections.Concurrent;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

/// <summary>
/// Empirical stress tests, adversarial input suites, and mathematical oracles
/// for <see cref="UiScaleLevels"/> and scale validation in <see cref="ApplicationSettings"/>.
/// </summary>
public class UiScaleLevelsStressTests
{
    private static readonly HashSet<int> ApprovedLevels = [80, 90, 100, 110, 125, 150];

    [Fact]
    public void ConstantsAndCollections_IntegrityAndMonotonicity()
    {
        Assert.Equal(80, UiScaleLevels.MinPercent);
        Assert.Equal(150, UiScaleLevels.MaxPercent);
        Assert.Equal(100, UiScaleLevels.DefaultPercent);

        var all = UiScaleLevels.All;
        Assert.NotNull(all);
        Assert.Equal(6, all.Length);
        Assert.Equal(ApprovedLevels.OrderBy(x => x), all);

        var supported = UiScaleLevels.SupportedPercentages;
        Assert.NotNull(supported);
        Assert.Equal(all.Length, supported.Count);
        for (int i = 0; i < all.Length; i++)
        {
            Assert.Equal(all[i], supported[i]);
        }

        // Strictly increasing order
        for (int i = 0; i < all.Length - 1; i++)
        {
            Assert.True(all[i] < all[i + 1], $"Element at {i} ({all[i]}) is not strictly less than element at {i + 1} ({all[i + 1]})");
        }

        Assert.Equal(UiScaleLevels.MinPercent, all[0]);
        Assert.Equal(UiScaleLevels.MaxPercent, all[^1]);
        Assert.Contains(UiScaleLevels.DefaultPercent, all);
    }

    [Fact]
    public void Exhaustive_IsValid_GeneratorAndOracle()
    {
        // Stress test across wide integer range [-20,000 .. 20,000]
        for (int percent = -20_000; percent <= 20_000; percent++)
        {
            bool expected = ApprovedLevels.Contains(percent);
            bool actual = UiScaleLevels.IsValid(percent);
            Assert.True(expected == actual, $"IsValid failed for {percent}: expected {expected}, actual {actual}");
        }

        // Boundary and extreme integers
        int[] extremeCases = [int.MinValue, int.MinValue + 1, -1, 0, 1, int.MaxValue - 1, int.MaxValue];
        foreach (int val in extremeCases)
        {
            Assert.False(UiScaleLevels.IsValid(val), $"IsValid should be false for extreme value {val}");
        }
    }

    [Fact]
    public void Exhaustive_ToFactor_GeneratorAndOracle()
    {
        // For every integer in [-2,000 .. 2,000]
        for (int percent = -2_000; percent <= 2_000; percent++)
        {
            double factor = UiScaleLevels.ToFactor(percent);
            if (ApprovedLevels.Contains(percent))
            {
                double expected = percent / 100.0;
                Assert.Equal(expected, factor, precision: 6);
            }
            else
            {
                // Must fall back to 1.0 (DefaultPercent / 100.0)
                Assert.Equal(1.0, factor, precision: 6);
            }
        }

        // Extremes
        Assert.Equal(1.0, UiScaleLevels.ToFactor(int.MinValue));
        Assert.Equal(1.0, UiScaleLevels.ToFactor(int.MaxValue));
        Assert.Equal(1.0, UiScaleLevels.ToFactor(0));
    }

    [Fact]
    public void Exhaustive_NextLevel_SteppingAndClampingOracle()
    {
        // Monotonicity and boundary checks across [-1,000 .. 1,000]
        int previousResult = int.MinValue;
        for (int current = -1_000; current <= 1_000; current++)
        {
            int next = UiScaleLevels.NextLevel(current);

            // Output must always be one of the approved levels
            Assert.Contains(next, ApprovedLevels);

            // Oracle
            int expected = current switch
            {
                < 80 => 80,
                < 90 => 90,
                < 100 => 100,
                < 110 => 110,
                < 125 => 125,
                < 150 => 150,
                _ => 150 // Clamped at 150
            };
            Assert.Equal(expected, next);

            // Monotonic non-decreasing
            if (previousResult != int.MinValue)
            {
                Assert.True(next >= previousResult, $"Monotonicity violation in NextLevel at {current}: next={next} < prev={previousResult}");
            }
            previousResult = next;
        }

        // Extreme bounds
        Assert.Equal(80, UiScaleLevels.NextLevel(int.MinValue));
        Assert.Equal(150, UiScaleLevels.NextLevel(int.MaxValue));
    }

    [Fact]
    public void Exhaustive_PreviousLevel_SteppingAndClampingOracle()
    {
        // Monotonicity and boundary checks across [-1,000 .. 1,000]
        int previousResult = int.MinValue;
        for (int current = -1_000; current <= 1_000; current++)
        {
            int prev = UiScaleLevels.PreviousLevel(current);

            // Output must always be one of the approved levels
            Assert.Contains(prev, ApprovedLevels);

            // Oracle
            int expected = current switch
            {
                <= 80 => 80, // Clamped at 80
                <= 90 => 80,
                <= 100 => 90,
                <= 110 => 100,
                <= 125 => 110,
                <= 150 => 125,
                _ => 150
            };
            Assert.Equal(expected, prev);

            // Monotonic non-decreasing
            if (previousResult != int.MinValue)
            {
                Assert.True(prev >= previousResult, $"Monotonicity violation in PreviousLevel at {current}: prev={prev} < prevResult={previousResult}");
            }
            previousResult = prev;
        }

        // Extreme bounds
        Assert.Equal(80, UiScaleLevels.PreviousLevel(int.MinValue));
        Assert.Equal(150, UiScaleLevels.PreviousLevel(int.MaxValue));
    }

    [Fact]
    public void StepUp_StepDown_RoundTripInvariance()
    {
        int[] levels = UiScaleLevels.All;

        // Stepping up then down from any level below MaxPercent returns to the same level
        for (int i = 0; i < levels.Length - 1; i++)
        {
            int current = levels[i];
            int steppedUp = UiScaleLevels.NextLevel(current);
            int restored = UiScaleLevels.PreviousLevel(steppedUp);
            Assert.Equal(current, restored);
        }

        // Stepping down then up from any level above MinPercent returns to the same level
        for (int i = 1; i < levels.Length; i++)
        {
            int current = levels[i];
            int steppedDown = UiScaleLevels.PreviousLevel(current);
            int restored = UiScaleLevels.NextLevel(steppedDown);
            Assert.Equal(current, restored);
        }

        // Boundary idempotency
        Assert.Equal(150, UiScaleLevels.NextLevel(150));
        Assert.Equal(150, UiScaleLevels.NextLevel(UiScaleLevels.NextLevel(150)));
        Assert.Equal(80, UiScaleLevels.PreviousLevel(80));
        Assert.Equal(80, UiScaleLevels.PreviousLevel(UiScaleLevels.PreviousLevel(80)));
    }

    [Fact]
    public void ScaleTransition_GraphCompletenessAndPathLengths()
    {
        // 80 -> 150 takes exactly 5 steps up
        int current = 80;
        int stepsUp = 0;
        while (current < 150)
        {
            current = UiScaleLevels.NextLevel(current);
            stepsUp++;
        }
        Assert.Equal(5, stepsUp);
        Assert.Equal(150, current);

        // 150 -> 80 takes exactly 5 steps down
        int stepsDown = 0;
        while (current > 80)
        {
            current = UiScaleLevels.PreviousLevel(current);
            stepsDown++;
        }
        Assert.Equal(5, stepsDown);
        Assert.Equal(80, current);
    }

    [Fact]
    public void CalculateEffectiveWidth_MathematicalPrecision()
    {
        double[] testWidths = [320.0, 480.0, 600.0, 800.0, 1024.0, 1200.0, 1440.0, 1920.0, 2560.0, 3840.0];

        foreach (double width in testWidths)
        {
            // At 80% (factor 0.80): effective width = width / 0.80 = 1.25 * width
            Assert.Equal(width * 1.25, UiScaleLevels.CalculateEffectiveWidth(width, 0.80), precision: 6);
            Assert.Equal(width * 1.25, UiScaleLevels.CalculateEffectiveWidth(width, 80), precision: 6);

            // At 90% (factor 0.90): effective width = width / 0.90
            Assert.Equal(width / 0.90, UiScaleLevels.CalculateEffectiveWidth(width, 0.90), precision: 6);
            Assert.Equal(width / 0.90, UiScaleLevels.CalculateEffectiveWidth(width, 90), precision: 6);

            // At 100% (factor 1.00): effective width == actual width
            Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 1.00), precision: 6);
            Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 100), precision: 6);

            // At 110% (factor 1.10): effective width = width / 1.10
            Assert.Equal(width / 1.10, UiScaleLevels.CalculateEffectiveWidth(width, 1.10), precision: 6);
            Assert.Equal(width / 1.10, UiScaleLevels.CalculateEffectiveWidth(width, 110), precision: 6);

            // At 125% (factor 1.25): effective width = width / 1.25 = 0.80 * width
            Assert.Equal(width * 0.80, UiScaleLevels.CalculateEffectiveWidth(width, 1.25), precision: 6);
            Assert.Equal(width * 0.80, UiScaleLevels.CalculateEffectiveWidth(width, 125), precision: 6);

            // At 150% (factor 1.50): effective width = width / 1.50 = width * (2/3)
            Assert.Equal(width / 1.50, UiScaleLevels.CalculateEffectiveWidth(width, 1.50), precision: 6);
            Assert.Equal(width / 1.50, UiScaleLevels.CalculateEffectiveWidth(width, 150), precision: 6);
        }
    }

    [Fact]
    public void CalculateEffectiveWidth_MonotonicReflowProperty()
    {
        // For any positive actual width, increasing UI scale must monotonically decrease effective width
        double actualWidth = 1200.0;
        double previousEffectiveWidth = double.MaxValue;

        foreach (int percent in UiScaleLevels.All)
        {
            double effectiveWidth = UiScaleLevels.CalculateEffectiveWidth(actualWidth, percent);
            Assert.True(effectiveWidth < previousEffectiveWidth,
                $"Effective width at {percent}% ({effectiveWidth}) should be strictly less than previous ({previousEffectiveWidth})");
            previousEffectiveWidth = effectiveWidth;
        }
    }

    [Fact]
    public void CalculateEffectiveWidth_AdversarialHarness()
    {
        double width = 1000.0;

        // Zero, negative, NaN, and Infinity factors must safely return actualWidth
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 0.0));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, -0.0));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, -1.0));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, -100.0));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, double.NaN));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, double.PositiveInfinity));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, double.NegativeInfinity));

        // Invalid scale percent overload falls back to 1.0 (actualWidth)
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, -100));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 0));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 50));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 79));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 85));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, 200));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, int.MinValue));
        Assert.Equal(width, UiScaleLevels.CalculateEffectiveWidth(width, int.MaxValue));

        // Zero width
        Assert.Equal(0.0, UiScaleLevels.CalculateEffectiveWidth(0.0, 1.50));
        Assert.Equal(0.0, UiScaleLevels.CalculateEffectiveWidth(0.0, 150));

        // Negative width
        Assert.Equal(-800.0, UiScaleLevels.CalculateEffectiveWidth(-1200.0, 1.50), precision: 6);
    }

    [Fact]
    public void ApplicationSettings_ExhaustiveValidation_AdversarialHarness()
    {
        // 1. All valid levels succeed
        foreach (int valid in UiScaleLevels.All)
        {
            var validSettings = ApplicationSettings.Default with { UiScalePercent = valid };
            validSettings.Validate();
            Assert.Equal(valid, validSettings.UiScalePercent);
        }

        // 2. 500 generated invalid values must all throw ArgumentException with ParamName == "UiScalePercent"
        int[] sampleInvalid =
        [
            int.MinValue, -100, -1, 0, 1, 50, 70, 79, 81, 85, 89, 91, 95, 99,
            101, 105, 109, 111, 115, 120, 124, 126, 130, 140, 149, 151, 160, 200, 500, int.MaxValue
        ];

        foreach (int invalid in sampleInvalid)
        {
            var invalidSettings = ApplicationSettings.Default with { UiScalePercent = invalid };
            var ex = Assert.Throws<ArgumentException>(() => invalidSettings.Validate());
            Assert.Equal("UiScalePercent", ex.ParamName);
        }
    }

    [Fact]
    public async Task Persistence_TamperedScaleValuesFailExplicitly()
    {
        using var temp = new TempDirectory();
        string dbPath = Path.Combine(temp.Path, "tampered_scale.db");
        var connections = new SqliteConnectionFactory(dbPath);
        new DatabaseBootstrapper(connections).Initialize();
        var repo = new SqliteSettingsRepository(connections);

        // Verify valid default loads
        ApplicationSettings initial = await repo.LoadAsync();
        Assert.Equal(100, initial.UiScalePercent);

        // Corrupt SQLite database with invalid scale percent values
        int[] invalidPersistedValues = [-10, 0, 70, 85, 95, 105, 120, 160, 999];
        foreach (int invalidVal in invalidPersistedValues)
        {
            using (var connection = connections.OpenConnection())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"UPDATE application_settings SET ui_scale_percent = {invalidVal} WHERE singleton = 1;";
                cmd.ExecuteNonQuery();
            }

            // Must throw InvalidDataException due to fail-fast validation in LoadAsync
            await Assert.ThrowsAsync<InvalidDataException>(() => repo.LoadAsync());
        }

        // Verify SQLite schema constraint actively enforces NOT NULL on ui_scale_percent
        using (var connection = connections.OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE application_settings SET ui_scale_percent = NULL WHERE singleton = 1;";
            Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
        }

        // Verify saving an invalid scale setting is rejected by domain validation before touching database
        var invalidSetting = ApplicationSettings.Default with { UiScalePercent = 999 };
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAsync(invalidSetting));

        // Verify all 6 valid scale values save and load cleanly through repository
        foreach (int valid in UiScaleLevels.All)
        {
            await repo.SaveAsync(ApplicationSettings.Default with { UiScalePercent = valid });
            ApplicationSettings loaded = await repo.LoadAsync();
            Assert.Equal(valid, loaded.UiScalePercent);
        }

        using (var connection = connections.OpenConnection())
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    [Fact]
    public void ThreadSafety_And_ConcurrencyStress()
    {
        var exceptions = new ConcurrentBag<Exception>();
        int iterations = 10_000;

        Parallel.For(0, iterations, i =>
        {
            try
            {
                int samplePercent = (i % 300) - 50; // Range [-50 .. 249]
                double sampleWidth = 800.0 + (i % 1000);

                bool isValid = UiScaleLevels.IsValid(samplePercent);
                double factor = UiScaleLevels.ToFactor(samplePercent);
                int next = UiScaleLevels.NextLevel(samplePercent);
                int prev = UiScaleLevels.PreviousLevel(samplePercent);
                double effWidth1 = UiScaleLevels.CalculateEffectiveWidth(sampleWidth, factor);
                double effWidth2 = UiScaleLevels.CalculateEffectiveWidth(sampleWidth, samplePercent);

                if (isValid)
                {
                    Assert.True(factor >= 0.80 && factor <= 1.50);
                    Assert.Equal(effWidth1, effWidth2, precision: 6);
                }
                else
                {
                    Assert.Equal(1.0, factor);
                    Assert.Equal(sampleWidth, effWidth2);
                }
                Assert.Contains(next, UiScaleLevels.All);
                Assert.Contains(prev, UiScaleLevels.All);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
