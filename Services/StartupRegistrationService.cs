using Microsoft.Win32;

namespace DataStock.Windows.Services;

public static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DataStock";
    private const string BackgroundLaunchArgument = "--background";

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

            key.SetValue(AppName, StartupCommand(executablePath));
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static bool IsBackgroundLaunch(IEnumerable<string> args)
    {
        return args.Any(arg => arg.Equals(BackgroundLaunchArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static void RefreshEnabledRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(AppName) is not string value || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var executablePath = Environment.ProcessPath ?? "";
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        var expectedValue = StartupCommand(executablePath);
        if (!value.Equals(expectedValue, StringComparison.Ordinal))
        {
            key.SetValue(AppName, expectedValue);
        }
    }

    public static string StatusText()
    {
        return IsEnabled() ? L10n.Text("StartupEnabled") : L10n.Text("StartupDisabled");
    }

    private static string StartupCommand(string executablePath)
    {
        return $"\"{executablePath}\" {BackgroundLaunchArgument}";
    }
}
