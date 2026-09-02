using FocusKey.Foundation.Logging;

namespace FocusKey.Foundation.Tests;

public sealed class FileAppLoggerTests
{
    [Fact]
    public void Constructor_CreatesMissingLogDirectory()
    {
        using var temp = new TempDirectory();
        string logFile = Path.Combine(temp.Path, "logs", "focus_key.log");

        using var logger = new FileAppLogger(logFile);

        Assert.True(File.Exists(logFile));
    }

    [Fact]
    public void Write_RecordsLevelAndMessage()
    {
        using var temp = new TempDirectory();
        string logFile = Path.Combine(temp.Path, "logs", "focus_key.log");

        using (var logger = new FileAppLogger(logFile))
        {
            logger.Info("started");
            logger.Warning("careful");
            logger.Error("broken");
        }

        string[] lines = File.ReadAllLines(logFile);

        Assert.Equal(3, lines.Length);
        Assert.Contains("[INFO ] started", lines[0]);
        Assert.Contains("[WARN ] careful", lines[1]);
        Assert.Contains("[ERROR] broken", lines[2]);
    }

    [Fact]
    public void Write_UsesUtcTimestamps()
    {
        using var temp = new TempDirectory();
        string logFile = Path.Combine(temp.Path, "focus_key.log");

        using (var logger = new FileAppLogger(logFile))
        {
            logger.Info("stamped");
        }

        string line = File.ReadAllLines(logFile)[0];
        string timestamp = line[..24];

        Assert.EndsWith("Z", timestamp);
        Assert.True(DateTimeOffset.TryParse(
            timestamp,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out _));
    }

    [Fact]
    public void Error_IncludesExceptionDetail()
    {
        using var temp = new TempDirectory();
        string logFile = Path.Combine(temp.Path, "focus_key.log");

        using (var logger = new FileAppLogger(logFile))
        {
            logger.Error("initialization failed", new InvalidOperationException("database unavailable"));
        }

        string contents = File.ReadAllText(logFile);

        Assert.Contains("initialization failed", contents);
        Assert.Contains("InvalidOperationException", contents);
        Assert.Contains("database unavailable", contents);
    }

    [Fact]
    public void Logger_AppendsAcrossSessions()
    {
        using var temp = new TempDirectory();
        string logFile = Path.Combine(temp.Path, "focus_key.log");

        using (var first = new FileAppLogger(logFile))
        {
            first.Info("first run");
        }

        using (var second = new FileAppLogger(logFile))
        {
            second.Info("second run");
        }

        string[] lines = File.ReadAllLines(logFile);

        Assert.Equal(2, lines.Length);
        Assert.Contains("first run", lines[0]);
        Assert.Contains("second run", lines[1]);
    }

    [Fact]
    public void Write_AfterDispose_DoesNotThrow()
    {
        using var temp = new TempDirectory();
        var logger = new FileAppLogger(Path.Combine(temp.Path, "focus_key.log"));

        logger.Dispose();
        logger.Dispose();
        logger.Info("ignored");
    }

    [Fact]
    public void Constructor_RejectsBlankPath()
    {
        Assert.Throws<ArgumentException>(() => new FileAppLogger("   "));
    }
}
