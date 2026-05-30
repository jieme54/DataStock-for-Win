using Microsoft.Win32;
using System.IO;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
using WpfWindow = System.Windows.Window;

namespace DataStock.Windows.Services;

public static class DialogService
{
    public static string? ChooseFolder(string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L10n.Text("ChooseFolder")
        };

        var initialDirectory = ResolveInitialDirectory(initialPath);
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var owner = System.Windows.Application.Current.Windows.OfType<WpfWindow>().FirstOrDefault(window => window.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public static string? ChooseFile(string? initialPath = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = L10n.Text("ChooseFile"),
            CheckFileExists = true,
            Multiselect = false
        };

        var initialDirectory = ResolveInitialDirectory(initialPath);
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var owner = System.Windows.Application.Current.Windows.OfType<WpfWindow>().FirstOrDefault(window => window.IsActive);
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static string? ResolveInitialDirectory(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            return null;
        }

        var expanded = Path.GetFullPath(Environment.ExpandEnvironmentVariables(initialPath));
        if (Directory.Exists(expanded))
        {
            return expanded;
        }

        if (File.Exists(expanded))
        {
            return Path.GetDirectoryName(expanded);
        }

        var parent = Path.GetDirectoryName(expanded);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) ? parent : null;
    }
}
