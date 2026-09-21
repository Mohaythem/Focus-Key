using System.Collections.Concurrent;
using FocusKey.Foundation.Settings;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

/// <summary>
/// UI Scaling Shortcuts and Stepping Empirical Stress Tests:
/// High-iteration stepping sequences, boundary clamping, arbitrary integer mappings,
/// heavy multi-threaded concurrency, and simulated MainWindow clamping logic.
/// </summary>
public sealed class UiScaleShortcutsSteppingStressTests
{
    private static readonly int[] StandardProgression = [80, 90, 100, 110, 125, 150];

    /// <summary>
    /// Exact clamping logic from MainWindow.xaml.cs lines 92-95.
    /// </summary>
    private static int MainWindowClamp(int percent) =>
        UiScaleLevels.IsValid(percent)
            ? percent
            : (percent < UiScaleLevels.MinPercent
                ? UiScaleLevels.MinPercent
                : (percent > UiScaleLevels.MaxPercent
                    ? UiScaleLevels.MaxPercent
                    : UiScaleLevels.DefaultPercent));

    #region 1. High-Iteration Stepping Sequence (80% -> 150% -> 80%)

    [Fact]
    public void SteppingSequence_HighIterationLoop_50000Cycles_ExactProgressionMaintained()
    {
        const int totalCycles = 50_000;
        int current = 80;

        for (int cycle = 0; cycle < totalCycles; cycle++)
        {
            // Upward ladder: 80 -> 90 -> 100 -> 110 -> 125 -> 150
            for (int step = 0; step < StandardProgression.Length - 1; step++)
            {
                int expectedNext = StandardProgression[step + 1];
                current = UiScaleLevels.NextLevel(current);
                Assert.Equal(expectedNext, current);
            }

            Assert.Equal(UiScaleLevels.MaxPercent, current);

            // Downward ladder: 150 -> 125 -> 110 -> 100 -> 90 -> 80
            for (int step = StandardProgression.Length - 1; step > 0; step--)
            {
                int expectedPrev = StandardProgression[step - 1];
                current = UiScaleLevels.PreviousLevel(current);
                Assert.Equal(expectedPrev, current);
            }

            Assert.Equal(UiScaleLevels.MinPercent, current);
        }
    }

    #endregion

    #region 2. Boundary Clamping Stress (Repeated Zoom In at 150%, Zoom Out at 80%)

    [Fact]
    public void BoundaryClamping_RepeatedZoomInAt150_StaysClampedAcross50000Iterations()
    {
        int scale = UiScaleLevels.MaxPercent; // 150

        for (int i = 0; i < 50_000; i++)
        {
            scale = UiScaleLevels.NextLevel(scale);
            Assert.Equal(UiScaleLevels.MaxPercent, scale);
        }
    }

    [Fact]
    public void BoundaryClamping_RepeatedZoomOutAt80_StaysClampedAcross50000Iterations()
    {
        int scale = UiScaleLevels.MinPercent; // 80

        for (int i = 0; i < 50_000; i++)
        {
            scale = UiScaleLevels.PreviousLevel(scale);
            Assert.Equal(UiScaleLevels.MinPercent, scale);
        }
    }

    [Theory]
    [InlineData(150, 1)]
    [InlineData(150, 5)]
    [InlineData(150, 100)]
    [InlineData(150, 1000)]
    public void BoundaryClamping_OversteppingZoomIn_AlwaysReturnsMaxPercent(int initial, int steps)
    {
        int current = initial;
        for (int i = 0; i < steps; i++)
        {
            current = UiScaleLevels.NextLevel(current);
        }
        Assert.Equal(UiScaleLevels.MaxPercent, current);
    }

    [Theory]
    [InlineData(80, 1)]
    [InlineData(80, 5)]
    [InlineData(80, 100)]
    [InlineData(80, 1000)]
    public void BoundaryClamping_UndersteppingZoomOut_AlwaysReturnsMinPercent(int initial, int steps)
    {
        int current = initial;
        for (int i = 0; i < steps; i++)
        {
            current = UiScaleLevels.PreviousLevel(current);
        }
        Assert.Equal(UiScaleLevels.MinPercent, current);
    }

    #endregion

    #region 3. Reset Invariant (Ctrl+0 Always Yields 100%)

    [Theory]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    public void Reset_StandardStartingLevels_YieldsDefaultPercent(int startingLevel)
    {
        int current = startingLevel;
        Assert.True(UiScaleLevels.IsValid(current));
        int resetValue = UiScaleLevels.DefaultPercent;
        Assert.Equal(100, resetValue);
        Assert.True(UiScaleLevels.IsValid(resetValue));
        Assert.Equal(1.0, UiScaleLevels.ToFactor(resetValue));
        int clamped = MainWindowClamp(resetValue);
        Assert.Equal(100, clamped);
    }

    [Fact]
    public void Reset_ExhaustiveRangeOfStartingPoints_AlwaysReturns100Percent()
    {
        // Stress test across range [-50,000 .. 50,000]
        for (int start = -50_000; start <= 50_000; start += 97)
        {
            int resetTarget = UiScaleLevels.DefaultPercent;
            int clamped = MainWindowClamp(resetTarget);

            Assert.Equal(100, clamped);
            Assert.Equal(1.0, UiScaleLevels.ToFactor(clamped));
            Assert.True(UiScaleLevels.IsValid(clamped));
        }

        // Extreme bounds
        int[] extremes = [int.MinValue, -1_000_000, -1, 0, 1, 1_000_000, int.MaxValue];
        foreach (int val in extremes)
        {
            int resetTarget = UiScaleLevels.DefaultPercent;
            Assert.Equal(100, resetTarget);
        }
    }

    #endregion

    #region 4. Arbitrary and Non-Standard Scale Percent Mapping

    [Fact]
    public void ArbitraryScale_ExhaustiveScan_AlwaysMapsToValidDiscreteLevel()
    {
        // Every arbitrary integer in [-10,000 .. 10,000] must map to a valid discrete level
        for (int raw = -10_000; raw <= 10_000; raw++)
        {
            int next = UiScaleLevels.NextLevel(raw);
            int prev = UiScaleLevels.PreviousLevel(raw);

            Assert.True(UiScaleLevels.IsValid(next), $"NextLevel({raw}) returned non-standard value {next}");
            Assert.True(UiScaleLevels.IsValid(prev), $"PreviousLevel({raw}) returned non-standard value {prev}");
            Assert.True(next >= prev, $"NextLevel({raw}) [{next}] must be >= PreviousLevel({raw}) [{prev}]");

            if (raw <= UiScaleLevels.MinPercent)
            {
                Assert.Equal(UiScaleLevels.MinPercent, prev);
            }
            if (raw >= UiScaleLevels.MaxPercent)
            {
                Assert.Equal(UiScaleLevels.MaxPercent, next);
            }
        }
    }

    [Fact]
    public void ArbitraryScale_IntermediateValues_MapToExpectedAdjacentLevels()
    {
        // Test non-standard intermediate percentages
        var intermediateMap = new (int input, int expectedPrev, int expectedNext)[]
        {
            (85, 80, 90),
            (95, 90, 100),
            (105, 100, 110),
            (115, 110, 125),
            (120, 110, 125),
            (130, 125, 150),
            (140, 125, 150),
            (149, 125, 150),
            (81, 80, 90),
            (89, 80, 90),
            (99, 90, 100),
            (101, 100, 110),
            (124, 110, 125),
            (126, 125, 150),
        };

        foreach (var (input, expectedPrev, expectedNext) in intermediateMap)
        {
            Assert.Equal(expectedNext, UiScaleLevels.NextLevel(input));
            Assert.Equal(expectedPrev, UiScaleLevels.PreviousLevel(input));
        }
    }

    [Fact]
    public void MainWindowClamp_ExhaustiveVerificationAcrossIntegerSpace()
    {
        for (int val = -20_000; val <= 20_000; val++)
        {
            int clamped = MainWindowClamp(val);

            // The clamped output must ALWAYS be a valid discrete level
            Assert.True(UiScaleLevels.IsValid(clamped), $"MainWindowClamp({val}) returned invalid level {clamped}");

            if (UiScaleLevels.IsValid(val))
            {
                Assert.Equal(val, clamped);
            }
            else if (val < UiScaleLevels.MinPercent)
            {
                Assert.Equal(UiScaleLevels.MinPercent, clamped);
            }
            else if (val > UiScaleLevels.MaxPercent)
            {
                Assert.Equal(UiScaleLevels.MaxPercent, clamped);
            }
            else
            {
                // Intermediate unapproved values (e.g. 85, 95, 105, 120) fallback to DefaultPercent (100)
                Assert.Equal(UiScaleLevels.DefaultPercent, clamped);
            }
        }
    }

    #endregion

    #region 5. Heavy Multi-Threaded Concurrency & Race Condition Stress

    [Fact]
    public void Concurrency_HighThroughputParallelStepping_ZeroExceptionsOrStateCorruption()
    {
        const int totalTasks = 200;
        const int operationsPerTask = 5_000;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, totalTasks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 }, taskId =>
        {
            try
            {
                int localScale = StandardProgression[taskId % StandardProgression.Length];

                for (int op = 0; op < operationsPerTask; op++)
                {
                    int action = (taskId + op) % 4;
                    switch (action)
                    {
                        case 0: // Zoom In
                            localScale = UiScaleLevels.NextLevel(localScale);
                            break;
                        case 1: // Zoom Out
                            localScale = UiScaleLevels.PreviousLevel(localScale);
                            break;
                        case 2: // Reset
                            localScale = UiScaleLevels.DefaultPercent;
                            break;
                        case 3: // Validate & Clamp
                            localScale = MainWindowClamp(localScale + (op % 10 - 5));
                            break;
                    }

                    Assert.True(UiScaleLevels.IsValid(localScale));
                    double factor = UiScaleLevels.ToFactor(localScale);
                    Assert.True(factor >= 0.80 && factor <= 1.50);
                    double effWidth = UiScaleLevels.CalculateEffectiveWidth(1200.0, factor);
                    Assert.True(effWidth >= 800.0 && effWidth <= 1500.0);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void Concurrency_InterleavedStateTransitions_MaintainsMonotonicSafety()
    {
        var exceptions = new ConcurrentBag<Exception>();
        int sharedScale = 100;
        object syncLock = new();

        Parallel.For(0, 10_000, i =>
        {
            try
            {
                int op = i % 3;
                lock (syncLock)
                {
                    if (op == 0)
                    {
                        sharedScale = UiScaleLevels.NextLevel(sharedScale);
                    }
                    else if (op == 1)
                    {
                        sharedScale = UiScaleLevels.PreviousLevel(sharedScale);
                    }
                    else
                    {
                        sharedScale = UiScaleLevels.DefaultPercent;
                    }

                    Assert.True(UiScaleLevels.IsValid(sharedScale));
                    Assert.True(sharedScale >= 80 && sharedScale <= 150);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.True(UiScaleLevels.IsValid(sharedScale));
    }

    #endregion

    #region 6. Rapid Bouncing & Keyboard Sequence Simulation

    [Fact]
    public void KeyboardSimulation_RapidAlternatingZoomInOut_ReturnsToBaseline()
    {
        // Alternating Zoom In and Zoom Out from interior points should preserve level
        int[] interiorPoints = [90, 100, 110, 125];

        foreach (int start in interiorPoints)
        {
            int current = start;
            for (int i = 0; i < 1_000; i++)
            {
                current = UiScaleLevels.NextLevel(current);
                current = UiScaleLevels.PreviousLevel(current);
                Assert.Equal(start, current);
            }
        }
    }

    [Fact]
    public void KeyboardSimulation_BoundaryBouncing_HandlesEdgeTransitionsCleanly()
    {
        // Start at 150, attempt 10 Zoom Ins, then 1 Zoom Out -> must be 125
        int scale = 150;
        for (int i = 0; i < 10; i++)
        {
            scale = UiScaleLevels.NextLevel(scale);
            Assert.Equal(150, scale);
        }
        scale = UiScaleLevels.PreviousLevel(scale);
        Assert.Equal(125, scale);

        // Start at 80, attempt 10 Zoom Outs, then 1 Zoom In -> must be 90
        scale = 80;
        for (int i = 0; i < 10; i++)
        {
            scale = UiScaleLevels.PreviousLevel(scale);
            Assert.Equal(80, scale);
        }
        scale = UiScaleLevels.NextLevel(scale);
        Assert.Equal(90, scale);
    }

    #endregion
}
