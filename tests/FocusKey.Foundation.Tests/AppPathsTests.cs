namespace FocusKey.Foundation.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ForRoot_ComposesTheDocumentedLayout()
    {
        AppPaths paths = AppPaths.ForRoot(@"C:\data\FocusKey");

        Assert.Equal(@"C:\data\FocusKey", paths.RootDirectory);
        Assert.Equal(@"C:\data\FocusKey\focus_key.db", paths.DatabaseFile);
        Assert.Equal(@"C:\data\FocusKey\logs", paths.LogsDirectory);
        Assert.Equal(@"C:\data\FocusKey\logs\focus_key.log", paths.LogFile);
    }

    [Fact]
    public void ForRoot_NormalizesRelativeSegments()
    {
        AppPaths paths = AppPaths.ForRoot(@"C:\data\sub\..\FocusKey");

        Assert.Equal(@"C:\data\FocusKey", paths.RootDirectory);
    }

    [Fact]
    public void ForRoot_IsDeterministic()
    {
        AppPaths first = AppPaths.ForRoot(@"C:\data\FocusKey");
        AppPaths second = AppPaths.ForRoot(@"C:\data\FocusKey");

        Assert.Equal(first.RootDirectory, second.RootDirectory);
        Assert.Equal(first.DatabaseFile, second.DatabaseFile);
        Assert.Equal(first.LogFile, second.LogFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ForRoot_RejectsBlankRoot(string root)
    {
        Assert.Throws<ArgumentException>(() => AppPaths.ForRoot(root));
    }

    [Fact]
    public void ForRoot_RejectsNullRoot()
    {
        Assert.Throws<ArgumentNullException>(() => AppPaths.ForRoot(null!));
    }

    [Fact]
    public void ForLocalApplicationData_UsesLocalApplicationData()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        AppPaths paths = AppPaths.ForLocalApplicationData();

        Assert.Equal(Path.Combine(localAppData, "FocusKey"), paths.RootDirectory);
    }

    [Fact]
    public void Resolve_PrefersTheEnvironmentOverrideWhenSet()
    {
        using var temp = new TempDirectory();
        string? original = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, temp.Path);

            AppPaths paths = AppPaths.Resolve();

            Assert.Equal(temp.Path, paths.RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, original);
        }
    }

    [Fact]
    public void Resolve_FallsBackToLocalApplicationData()
    {
        string? original = Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, "   ");

            Assert.Equal(AppPaths.ForLocalApplicationData().RootDirectory, AppPaths.Resolve().RootDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable, original);
        }
    }

    [Fact]
    public void EnsureCreated_CreatesRootAndLogsDirectories()
    {
        using var temp = new TempDirectory();
        AppPaths paths = AppPaths.ForRoot(Path.Combine(temp.Path, "FocusKey"));

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.RootDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
    }

    [Fact]
    public void EnsureCreated_IsSafeToRepeat()
    {
        using var temp = new TempDirectory();
        AppPaths paths = AppPaths.ForRoot(Path.Combine(temp.Path, "FocusKey"));

        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.RootDirectory, "existing.txt"), "kept");
        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(paths.RootDirectory, "existing.txt")));
    }
}
