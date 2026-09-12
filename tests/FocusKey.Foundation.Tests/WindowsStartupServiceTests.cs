using FocusKey.Foundation.Shell;
using Xunit;

namespace FocusKey.Foundation.Tests;

public sealed class WindowsStartupServiceTests
{
    private sealed class InMemoryRegistryAccessor : IRegistryAccessor
    {
        public Dictionary<(string SubKey, string ValueName), string> Values { get; } = new(new KeyComparer());

        public string? GetValue(string subKey, string valueName)
        {
            return Values.TryGetValue((subKey, valueName), out string? val) ? val : null;
        }

        public void SetValue(string subKey, string valueName, string value)
        {
            Values[(subKey, valueName)] = value;
        }

        public void DeleteValue(string subKey, string valueName)
        {
            Values.Remove((subKey, valueName));
        }

        public bool ValueExists(string subKey, string valueName)
        {
            return Values.ContainsKey((subKey, valueName));
        }

        private sealed class KeyComparer : IEqualityComparer<(string SubKey, string ValueName)>
        {
            public bool Equals((string SubKey, string ValueName) x, (string SubKey, string ValueName) y)
            {
                return string.Equals(x.SubKey, y.SubKey, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(x.ValueName, y.ValueName, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode((string SubKey, string ValueName) obj)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SubKey),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ValueName));
            }
        }
    }

    [Fact]
    public void Initially_Unregistered_ReturnsDisabled()
    {
        var registry = new InMemoryRegistryAccessor();
        var service = new WindowsStartupService(registry, () => @"C:\Programs\Focus Key\FocusKey.exe");

        Assert.False(service.IsEnabled());
        Assert.Null(service.GetConfiguredCommandLine());
    }

    [Fact]
    public void Enable_WritesSafelyQuotedPathWithStartupFlag()
    {
        var registry = new InMemoryRegistryAccessor();
        const string exePath = @"C:\Users\John Doe\AppData\Local\Programs\Focus Key\FocusKey.exe";
        var service = new WindowsStartupService(registry, () => exePath);

        service.SetEnabled(true);

        Assert.True(service.IsEnabled());
        string? configured = service.GetConfiguredCommandLine();
        Assert.NotNull(configured);
        Assert.Equal($"\"{exePath}\" --startup", configured);
    }

    [Fact]
    public void Enable_QuotesPathEvenWhenNoSpacesExist()
    {
        var registry = new InMemoryRegistryAccessor();
        const string exePath = @"C:\FocusKey.exe";
        var service = new WindowsStartupService(registry, () => exePath);

        service.SetEnabled(true);

        Assert.True(service.IsEnabled());
        Assert.Equal("\"C:\\FocusKey.exe\" --startup", service.GetConfiguredCommandLine());
    }

    [Fact]
    public void Enable_HandlesAlreadyQuotedInputGracefully()
    {
        var registry = new InMemoryRegistryAccessor();
        const string exePath = "\"C:\\Path With Spaces\\FocusKey.exe\"";
        var service = new WindowsStartupService(registry, () => exePath);

        service.SetEnabled(true);

        Assert.Equal("\"C:\\Path With Spaces\\FocusKey.exe\" --startup", service.GetConfiguredCommandLine());
    }

    [Fact]
    public void Enable_RepairsStaleOrBrokenRegistration()
    {
        var registry = new InMemoryRegistryAccessor();
        registry.SetValue(WindowsStartupService.RunSubKey, WindowsStartupService.ValueName, @"D:\old_broken_path\FocusKey.exe");

        const string currentPath = @"C:\Users\test\AppData\Local\Programs\Focus Key\FocusKey.exe";
        var service = new WindowsStartupService(registry, () => currentPath);

        // Before repair: returns true because value exists
        Assert.True(service.IsEnabled());
        Assert.Equal(@"D:\old_broken_path\FocusKey.exe", service.GetConfiguredCommandLine());

        // Enabling repairs to the current valid path
        service.SetEnabled(true);

        Assert.True(service.IsEnabled());
        Assert.Equal($"\"{currentPath}\" --startup", service.GetConfiguredCommandLine());
    }

    [Fact]
    public void Disable_RemovesValue_AndLeavesOtherEntriesIntact()
    {
        var registry = new InMemoryRegistryAccessor();
        registry.SetValue(WindowsStartupService.RunSubKey, "OtherApp", "\"C:\\OtherApp.exe\"");
        registry.SetValue(WindowsStartupService.RunSubKey, WindowsStartupService.ValueName, "\"C:\\FocusKey.exe\" --startup");

        var service = new WindowsStartupService(registry, () => @"C:\FocusKey.exe");

        Assert.True(service.IsEnabled());

        service.SetEnabled(false);

        Assert.False(service.IsEnabled());
        Assert.Null(service.GetConfiguredCommandLine());

        // Verify other registry entries in the same key are completely untouched
        Assert.Equal("\"C:\\OtherApp.exe\"", registry.GetValue(WindowsStartupService.RunSubKey, "OtherApp"));
    }

    [Fact]
    public void Disable_WhenAlreadyAbsent_DoesNotThrow()
    {
        var registry = new InMemoryRegistryAccessor();
        var service = new WindowsStartupService(registry, () => @"C:\FocusKey.exe");

        var exception = Record.Exception(() => service.SetEnabled(false));
        Assert.Null(exception);
        Assert.False(service.IsEnabled());
    }

    [Theory]
    [InlineData(new string[] { "FocusKey.exe", "--startup" }, true)]
    [InlineData(new string[] { "FocusKey.exe", "--STARTUP" }, true)]
    [InlineData(new string[] { "FocusKey.exe", "-startup" }, true)]
    [InlineData(new string[] { "FocusKey.exe", "/startup" }, true)]
    [InlineData(new string[] { "FocusKey.exe", "--other-flag", "--startup" }, true)]
    [InlineData(new string[] { "FocusKey.exe" }, false)]
    [InlineData(new string[] { "FocusKey.exe", "--other" }, false)]
    [InlineData(new string[] { }, false)]
    [InlineData(null, false)]
    public void StartupArguments_DetectsStartupFlag(string[]? args, bool expected)
    {
        bool actual = StartupArguments.IsStartupLaunch(args);
        Assert.Equal(expected, actual);
    }
}
