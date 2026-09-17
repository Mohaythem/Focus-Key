namespace FocusKey.Foundation.Settings;

[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record GlobalShortcut(ShortcutModifiers Modifiers, uint VirtualKey)
{
    public static GlobalShortcut Default { get; } = new(ShortcutModifiers.Shift, 0x72); // Shift + F3
    public static GlobalShortcut DefaultOverlay => Default;
    public static GlobalShortcut DefaultMainWindow { get; } = new(ShortcutModifiers.Shift, 0x73); // Shift + F4

    public void Validate()
    {
        if (!IsValid(out string? error))
            throw new ArgumentException(error ?? "Invalid global shortcut.", nameof(GlobalShortcut));
    }

    public bool IsValid(out string? error)
    {
        error = null;
        if (VirtualKey == 0)
        {
            error = "No key was specified.";
            return false;
        }

        // Modifier keys cannot be the target key
        if (VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)
        {
            error = "Modifier keys (Shift, Ctrl, Alt, Win) cannot be used as standalone shortcuts.";
            return false;
        }

        // Escape is reserved for canceling
        if (VirtualKey == 0x1B) // VK_ESCAPE
        {
            error = "Escape cannot be used as a global shortcut.";
            return false;
        }

        bool isFunctionKey = VirtualKey is >= 0x70 and <= 0x7B; // F1 - F12
        bool isAlphaNumeric = (VirtualKey is >= 0x41 and <= 0x5A) || (VirtualKey is >= 0x30 and <= 0x39);

        if (Modifiers == ShortcutModifiers.None)
        {
            if (!isFunctionKey)
            {
                error = "Regular keys require at least one modifier key (Ctrl, Alt, Shift, or Win).";
                return false;
            }
            return true;
        }

        // If it is alphanumeric, require at least Ctrl, Alt, or Win (Shift alone is normal typing: uppercase letters / symbols)
        if (isAlphaNumeric && Modifiers == ShortcutModifiers.Shift)
        {
            error = "Alphanumeric shortcuts require Ctrl, Alt, or Win to prevent interfering with typing.";
            return false;
        }

        // Check reserved combinations (e.g. Win + L is lock screen)
        if (Modifiers.HasFlag(ShortcutModifiers.Windows) && VirtualKey == 0x4C) // Win + L
        {
            error = "Win + L is reserved by Windows.";
            return false;
        }

        return true;
    }

    public static string FormatKey(uint vk)
    {
        if (vk is >= 0x70 and <= 0x7B) return $"F{vk - 0x70 + 1}";
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        return vk switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0xC0 => "`",
            0xBD => "-",
            0xBB => "=",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xBA => ";",
            0xDE => "'",
            0xBC => ",",
            0xBE => ".",
            0xBF => "/",
            _ => $"0x{vk:X2}"
        };
    }

    public static uint ParseKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return 0;
        string k = keyName.Trim();
        if (k.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(1), out int fNum) && fNum is >= 1 and <= 12)
            return (uint)(0x70 + fNum - 1);
        if (k.Length == 1)
        {
            char c = char.ToUpperInvariant(k[0]);
            if (c is >= 'A' and <= 'Z') return (uint)c;
            if (c is >= '0' and <= '9') return (uint)c;
            return c switch
            {
                '`' or '~' => 0xC0,
                '-' => 0xBD,
                '=' or '+' => 0xBB,
                '[' => 0xDB,
                ']' => 0xDD,
                '\\' => 0xDC,
                ';' => 0xBA,
                '\'' => 0xDE,
                ',' => 0xBC,
                '.' => 0xBE,
                '/' => 0xBF,
                _ => 0
            };
        }
        if (k.Equals("Space", StringComparison.OrdinalIgnoreCase)) return 0x20;
        if (k.Equals("Tab", StringComparison.OrdinalIgnoreCase)) return 0x09;
        if (k.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(k.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint hexVal))
            return hexVal;
        return 0;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
        if (Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        parts.Add(FormatKey(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static GlobalShortcut Parse(string text)
    {
        if (!TryParse(text, out var shortcut))
            throw new FormatException($"Invalid shortcut format: '{text}'.");
        return shortcut;
    }

    public static bool TryParse(string? text, out GlobalShortcut shortcut)
    {
        shortcut = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        ShortcutModifiers mods = ShortcutModifiers.None;
        uint key = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase))
                mods |= ShortcutModifiers.Control;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= ShortcutModifiers.Alt;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= ShortcutModifiers.Shift;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) || token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                mods |= ShortcutModifiers.Windows;
            else
            {
                if (key != 0) return false;
                key = ParseKey(token);
                if (key == 0) return false;
            }
        }

        if (key == 0) return false;
        shortcut = new GlobalShortcut(mods, key);
        return true;
    }
}
