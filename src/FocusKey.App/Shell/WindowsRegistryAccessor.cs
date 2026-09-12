using Microsoft.Win32;
using FocusKey.Foundation.Shell;

namespace FocusKey.Shell;

/// <summary>
/// Production implementation of <see cref="IRegistryAccessor"/> using the Win32 CurrentUser registry.
/// </summary>
public sealed class WindowsRegistryAccessor : IRegistryAccessor
{
    /// <inheritdoc/>
    public string? GetValue(string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void SetValue(string subKey, string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Unable to open or create registry key HKCU\\{subKey}");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    /// <inheritdoc/>
    public void DeleteValue(string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: true);
            if (key is not null && key.GetValue(valueName) is not null)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Fail gracefully if absent or inaccessible
        }
    }

    /// <inheritdoc/>
    public bool ValueExists(string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
