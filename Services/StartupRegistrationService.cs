using Microsoft.Win32;

namespace DataStock.Windows.Services;

public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DataStock";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var executablePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Unable to resolve the application path.");
            }

            key.SetValue(AppName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static string StatusText()
    {
        return IsEnabled() ? L10n.Text("StartupEnabled") : L10n.Text("StartupDisabled");
    }
}
