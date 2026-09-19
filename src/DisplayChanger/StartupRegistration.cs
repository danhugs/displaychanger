using Microsoft.Win32;

namespace DisplayChanger;

/// <summary>Manages the per-user "Run" registry entry that launches the app at sign-in. No admin rights needed.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "DisplayChanger";

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    private static string ExpectedCommand => $"\"{ExecutablePath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>True when the entry exists but points at a different exe (e.g. the app was moved).</summary>
    public static bool IsStale()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value
            && !string.Equals(value, ExpectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the Run registry key.");
        key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
