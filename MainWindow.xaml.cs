using System.ComponentModel;
using System.Drawing;
using System.Windows;
using DataStock.Windows.ViewModels;
using Button = System.Windows.Controls.Button;
using WinForms = System.Windows.Forms;

namespace DataStock.Windows;

public partial class MainWindow : Window
{
    private readonly WinForms.NotifyIcon notifyIcon;
    private readonly Icon notifyIconImage;
    private bool isQuitting;

    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        viewModel.RequestQuit += (_, _) => QuitApplication();
        DataContext = viewModel;

        notifyIconImage = LoadTrayIcon();
        notifyIcon = CreateNotifyIcon(notifyIconImage, viewModel);
        notifyIcon.Visible = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!isQuitting)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        notifyIconImage.Dispose();
        base.OnClosing(e);
    }

    private WinForms.NotifyIcon CreateNotifyIcon(Icon icon, MainViewModel viewModel)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(L10n.Text("AppOpen"), null, (_, _) => ShowFromTray());
        menu.Items.Add(L10n.Text("RunAllNow"), null, (_, _) => viewModel.Store.RunAll());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(L10n.Text("AppQuit"), null, (_, _) => QuitApplication());

        var item = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "DataStock",
            ContextMenuStrip = menu,
            Visible = true
        };
        item.DoubleClick += (_, _) => ShowFromTray();
        return item;
    }

    private static Icon LoadTrayIcon()
    {
        var streamInfo = System.Windows.Application.GetResourceStream(new Uri("Resources/AppIcon.ico", UriKind.Relative));
        return streamInfo is null ? SystemIcons.Application : new Icon(streamInfo.Stream);
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowFromExternalActivation()
    {
        ShowFromTray();
    }

    private void QuitApplication()
    {
        isQuitting = true;
        notifyIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    private void OpenButtonContextMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
