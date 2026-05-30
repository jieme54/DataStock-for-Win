using System.Threading;

namespace DataStock.Windows;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\DataStock.Windows.SingleInstance";
    private const string ShowWindowEventName = @"Local\DataStock.Windows.ShowWindow";

    private Mutex? singleInstanceMutex;
    private EventWaitHandle? showWindowEvent;
    private Thread? showWindowThread;
    private volatile bool isShuttingDown;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
            Environment.Exit(0);
            return;
        }

        showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        showWindowThread = new Thread(WaitForShowWindowRequests)
        {
            IsBackground = true,
            Name = "DataStock activation listener"
        };
        showWindowThread.Start();

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        isShuttingDown = true;
        showWindowEvent?.Set();
        showWindowEvent?.Dispose();

        singleInstanceMutex?.ReleaseMutex();
        singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var existingEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
            existingEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void WaitForShowWindowRequests()
    {
        try
        {
            while (!isShuttingDown && showWindowEvent?.WaitOne() == true)
            {
                if (isShuttingDown)
                {
                    break;
                }

                Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.ShowFromExternalActivation();
                    }
                });
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
