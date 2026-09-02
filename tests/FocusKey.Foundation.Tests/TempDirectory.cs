namespace FocusKey.Foundation.Tests;

/// <summary>
/// An isolated directory under the system temp folder, removed when the test finishes.
/// Only ever touches the single directory it created.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    private const string ContainerName = "focus-key-tests";

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            ContainerName,
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file must never fail a test run; the temp folder is disposable.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
