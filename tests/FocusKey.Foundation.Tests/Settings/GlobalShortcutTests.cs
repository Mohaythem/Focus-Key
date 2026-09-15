using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class GlobalShortcutTests
{
    [Fact]
    public void DefaultIsShiftPlusF3()
    {
        var def = GlobalShortcut.Default;
        Assert.Equal(ShortcutModifiers.Shift, def.Modifiers);
        Assert.Equal((uint)0x72, def.VirtualKey);
        Assert.Equal("Shift + F3", def.ToString());
    }

    [Theory]
    [InlineData("Shift + F3", ShortcutModifiers.Shift, 0x72)]
    [InlineData("Ctrl + Alt + F", ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x46)]
    [InlineData("Win + Shift + K", ShortcutModifiers.Windows | ShortcutModifiers.Shift, 0x4B)]
    [InlineData("F9", ShortcutModifiers.None, 0x78)]
    [InlineData("Ctrl + Shift + Space", ShortcutModifiers.Control | ShortcutModifiers.Shift, 0x20)]
    [InlineData("Alt + F4", ShortcutModifiers.Alt, 0x73)]
    public void RoundTripFormattingAndParsing(string formatted, ShortcutModifiers expectedMods, uint expectedVk)
    {
        var parsed = GlobalShortcut.Parse(formatted);
        Assert.Equal(expectedMods, parsed.Modifiers);
        Assert.Equal(expectedVk, parsed.VirtualKey);
        Assert.Equal(formatted, parsed.ToString());
    }

    [Theory]
    [InlineData("F1", true)]
    [InlineData("F3", true)]
    [InlineData("F12", true)]
    [InlineData("Shift + F3", true)]
    [InlineData("Ctrl + O", true)]
    [InlineData("Ctrl + Alt + F", true)]
    [InlineData("Alt + Space", true)]
    [InlineData("Win + Shift + K", true)]
    [InlineData("A", false)] // bare letter
    [InlineData("5", false)] // bare digit
    [InlineData("Shift + A", false)] // Shift alone on alphanumeric is typing
    [InlineData("Shift + 1", false)] // Shift alone on digit is typing
    [InlineData("Escape", false)] // Escape reserved
    [InlineData("Ctrl + Escape", false)] // Escape reserved
    [InlineData("Win + L", false)] // Windows lock screen
    [InlineData("Ctrl", false)] // modifier only
    [InlineData("Shift", false)] // modifier only
    public void ValidationRules(string text, bool expectedValid)
    {
        bool parsed = GlobalShortcut.TryParse(text, out var shortcut);
        if (!parsed)
        {
            Assert.False(expectedValid);
            return;
        }
        Assert.Equal(expectedValid, shortcut.IsValid(out string? reason));
        if (!expectedValid)
        {
            Assert.NotNull(reason);
            Assert.Throws<ArgumentException>(() => shortcut.Validate());
        }
    }

    [Fact]
    public async Task SqlitePersistence_SavesAndLoadsCustomShortcut()
    {
        using var store = new SessionStore();
        var repo = new SqliteSettingsRepository(store.Connections);
        var initial = await repo.LoadAsync();
        Assert.Equal(GlobalShortcut.Default, initial.GlobalShortcut);

        var customShortcut = GlobalShortcut.Parse("Ctrl + Alt + F");
        var updated = initial with { GlobalShortcut = customShortcut };
        await repo.SaveAsync(updated);

        var loaded = await repo.LoadAsync();
        Assert.Equal(customShortcut, loaded.GlobalShortcut);
        Assert.Equal("Ctrl + Alt + F", loaded.GlobalShortcut.ToString());
    }
}
