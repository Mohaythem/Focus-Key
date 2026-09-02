using System.Text;

namespace FocusKey.Foundation.Logging;

/// <summary>
/// Appends plain-text diagnostic lines to a single local file.
/// Deliberately small: one file, UTC timestamps, no rotation, no external logging framework.
/// </summary>
public sealed class FileAppLogger : IAppLogger, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    /// <param name="logFile">Full path of the log file. Its directory is created if missing.</param>
    public FileAppLogger(string logFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFile);

        string fullPath = Path.GetFullPath(logFile);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FilePath = fullPath;

        // FileShare.ReadWrite so the file stays readable (and tail-able) while the app runs.
        var stream = new FileStream(
            fullPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    public string FilePath { get; }

    public void Info(string message) => Write("INFO", message, exception: null);

    public void Warning(string message) => Write("WARN", message, exception: null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        string line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level,-5}] {message}");

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(line);
            if (exception is not null)
            {
                _writer.WriteLine(exception.ToString());
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
