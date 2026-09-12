namespace FocusKey.Foundation.Shell;

/// <summary>
/// Abstraction over Windows registry key access for testability and portability.
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>
    /// Reads a string value from the specified subkey under HKCU.
    /// Returns null if the subkey or value does not exist.
    /// </summary>
    string? GetValue(string subKey, string valueName);

    /// <summary>
    /// Writes a string (REG_SZ) value to the specified subkey under HKCU.
    /// Creates the subkey if it does not already exist.
    /// </summary>
    void SetValue(string subKey, string valueName, string value);

    /// <summary>
    /// Deletes a value from the specified subkey under HKCU.
    /// Does not throw if the subkey or value does not exist.
    /// </summary>
    void DeleteValue(string subKey, string valueName);

    /// <summary>
    /// Returns true if the value exists in the specified subkey under HKCU.
    /// </summary>
    bool ValueExists(string subKey, string valueName);
}
