using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataStock.Windows.Models;

namespace DataStock.Windows.Services;

public sealed class DataStore : INotifyPropertyChanged
{
    private readonly SyncEngine engine = new();
    private readonly object saveLock = new();
    private readonly string applicationSupportPath;
    private readonly string snapshotPath;
    private bool isLoading;
    private bool isSaving;

    public DataStore()
    {
        applicationSupportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataStock");
        snapshotPath = Path.Combine(applicationSupportPath, "DataStock.json");

        Jobs.CollectionChanged += JobsCollectionChanged;
        Logs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Logs));
        Load();
        RefreshStartupRegistration();
        Settings.LaunchAtLogin = StartupRegistrationService.IsEnabled();
        InitializeLanguage();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SyncJob> Jobs { get; } = [];
    public ObservableCollection<SyncLogEntry> Logs { get; } = [];
    public AppSettings Settings { get; private set; } = new();

    public AppLanguage Language => AppLanguageExtensions.FromCode(Settings.LanguageCode);

    public Guid AddJob()
    {
        var job = new SyncJob
        {
            Name = SuggestedJobName()
        };
        Jobs.Add(job);
        Save();
        return job.Id;
    }

    public void DeleteJob(Guid id)
    {
        var job = Jobs.FirstOrDefault(item => item.Id == id);
        if (job is null)
        {
            return;
        }

        Jobs.Remove(job);
        Save();
    }

    public bool MoveJob(Guid id, int offset)
    {
        var job = Jobs.FirstOrDefault(item => item.Id == id);
        if (job is null)
        {
            return false;
        }

        var currentIndex = Jobs.IndexOf(job);
        if (currentIndex < 0)
        {
            return false;
        }

        var newIndex = Math.Clamp(currentIndex + offset, 0, Jobs.Count - 1);
        if (newIndex == currentIndex)
        {
            return false;
        }

        Jobs.Move(currentIndex, newIndex);
        return true;
    }

    public void RunAll()
    {
        foreach (var job in Jobs.Where(job => job.CanStartRun).ToArray())
        {
            Run(job.Id, SyncRunMode.Synchronize);
        }
    }

    public void Run(Guid jobId, SyncRunMode mode, string? triggeredByScheduleKey = null)
    {
        var job = Jobs.FirstOrDefault(item => item.Id == jobId);
        if (job is null || job.IsRunning)
        {
            return;
        }

        if (!job.CanRun)
        {
            AppendLog(job, SyncLogLevel.Warning, L10n.Text("NotReadyMessage"));
            return;
        }

        job.IsRunning = true;
        job.RunningMode = mode;
        if (triggeredByScheduleKey is not null)
        {
            job.LastScheduledRunKey = triggeredByScheduleKey;
            Save();
        }

        var jobSnapshot = job.CloneForRun();
        Task.Run(() =>
        {
            try
            {
                var report = engine.Synchronize(jobSnapshot, mode, entry =>
                {
                    InvokeOnUi(() =>
                    {
                        Logs.Insert(0, entry);
                        TrimLogs();
                    });
                });

                InvokeOnUi(() => Finish(jobId, succeeded: true, report.Summary));
            }
            catch (Exception ex)
            {
                InvokeOnUi(() =>
                {
                    AppendLog(job, SyncLogLevel.Error, ex.Message);
                    Finish(jobId, succeeded: false, ex.Message);
                });
            }
        });
    }

    public void SetLaunchAtLogin(bool enabled)
    {
        try
        {
            StartupRegistrationService.SetEnabled(enabled);
            Settings.LaunchAtLogin = StartupRegistrationService.IsEnabled();
            Settings.LastLaunchAtLoginError = null;
            Save();
        }
        catch (Exception ex)
        {
            Settings.LaunchAtLogin = StartupRegistrationService.IsEnabled();
            Settings.LastLaunchAtLoginError = ex.Message;
            AppendLog(null, SyncLogLevel.Error, L10n.Text("LaunchAtLoginErrorFormat", ex.Message));
            Save();
        }

        OnPropertyChanged(nameof(Settings));
    }

    public void SetLanguage(AppLanguage language)
    {
        if (language == Language)
        {
            return;
        }

        Settings.LanguageCode = language.Code();
        L10n.CurrentLanguage = language;
        RefreshLocalizedProperties();
        Save();
    }

    public void AppendLog(SyncJob? job, SyncLogLevel level, string message)
    {
        Logs.Insert(0, new SyncLogEntry
        {
            JobId = job?.Id,
            JobName = job?.Name,
            Level = level,
            Message = message
        });
        TrimLogs();
    }

    private void RefreshStartupRegistration()
    {
        try
        {
            StartupRegistrationService.RefreshEnabledRegistration();
        }
        catch
        {
        }
    }

    public void Save()
    {
        if (isLoading || isSaving)
        {
            return;
        }

        lock (saveLock)
        {
            try
            {
                isSaving = true;
                Directory.CreateDirectory(applicationSupportPath);
                var snapshot = new DataSnapshot
                {
                    Jobs = new ObservableCollection<SyncJob>(Jobs.Select(job => job.CloneForRun())),
                    Settings = Settings
                };
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);
                File.WriteAllText(snapshotPath, json);
            }
            catch (Exception ex)
            {
                if (!isSaving)
                {
                    AppendLog(null, SyncLogLevel.Error, L10n.Text("SaveSettingsErrorFormat", ex.Message));
                }
            }
            finally
            {
                isSaving = false;
            }
        }
    }

    public void RefreshLocalizedProperties()
    {
        foreach (var job in Jobs)
        {
            job.RefreshLocalizedProperties();
            foreach (var entry in job.History)
            {
                entry.GetType();
            }
        }

        foreach (var entry in Logs)
        {
            entry.GetType();
        }

        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(Settings));
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    private void Finish(Guid jobId, bool succeeded, string message)
    {
        var job = Jobs.FirstOrDefault(item => item.Id == jobId);
        if (job is not null)
        {
            var finishedAt = DateTimeOffset.Now;
            job.LastRunDate = finishedAt;
            job.LastRunSucceeded = succeeded;
            job.LastRunMessage = message;
            job.History.Insert(0, new SyncHistoryEntry(finishedAt, succeeded, message));
            while (job.History.Count > 50)
            {
                job.History.RemoveAt(job.History.Count - 1);
            }

            job.IsRunning = false;
            job.RunningMode = null;
        }

        Save();
    }

    private void Load()
    {
        try
        {
            isLoading = true;
            if (!File.Exists(snapshotPath))
            {
                return;
            }

            var json = File.ReadAllText(snapshotPath);
            var snapshot = JsonSerializer.Deserialize<DataSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                return;
            }

            Settings = snapshot.Settings ?? new AppSettings();
            Settings.PropertyChanged += (_, _) => Save();
            Jobs.Clear();
            foreach (var job in snapshot.Jobs)
            {
                Jobs.Add(job);
            }
        }
        catch (Exception ex)
        {
            AppendLog(null, SyncLogLevel.Error, L10n.Text("LoadSettingsErrorFormat", ex.Message));
        }
        finally
        {
            isLoading = false;
            RegisterAll();
        }
    }

    private void InitializeLanguage()
    {
        var language = AppLanguageExtensions.FromCode(Settings.LanguageCode);
        Settings.LanguageCode = language.Code();
        L10n.CurrentLanguage = language;
        Save();
    }

    private void JobsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace && e.NewItems is not null)
        {
            foreach (SyncJob job in e.NewItems)
            {
                RegisterJob(job);
            }
        }

        Save();
        OnPropertyChanged(nameof(Jobs));
    }

    private void RegisterAll()
    {
        Settings.PropertyChanged += (_, _) => Save();
        foreach (var job in Jobs)
        {
            RegisterJob(job);
        }
    }

    private void RegisterJob(SyncJob job)
    {
        job.PropertyChanged += (_, _) => Save();
        job.Destinations.CollectionChanged += (_, e) =>
        {
            RegisterDestinations(job, e.NewItems);
            job.RefreshState();
            Save();
        };
        RegisterDestinations(job, job.Destinations);

        job.Exclusions.CollectionChanged += (_, e) =>
        {
            RegisterCollectionItems<SyncExclusion>(e.NewItems);
            Save();
        };
        RegisterCollectionItems<SyncExclusion>(job.Exclusions);

        job.History.CollectionChanged += (_, _) => Save();
        RegisterSchedule(job);
    }

    private void RegisterSchedule(SyncJob job)
    {
        job.Schedule.PropertyChanged += (_, _) =>
        {
            job.RefreshState();
            Save();
        };
        job.Schedule.Times.CollectionChanged += (_, e) =>
        {
            RegisterCollectionItems<ClockTime>(e.NewItems);
            job.Schedule.RefreshLocalizedProperties();
            job.RefreshState();
            Save();
        };
        RegisterCollectionItems<ClockTime>(job.Schedule.Times);
    }

    private void RegisterDestinations(SyncJob job, System.Collections.IEnumerable? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (SyncDestination destination in items)
        {
            destination.PropertyChanged += (_, _) =>
            {
                job.RefreshState();
                Save();
            };
        }
    }

    private void RegisterCollectionItems<T>(System.Collections.IEnumerable? items)
        where T : INotifyPropertyChanged
    {
        if (items is null)
        {
            return;
        }

        foreach (T item in items)
        {
            item.PropertyChanged += (_, _) => Save();
        }
    }

    private void TrimLogs()
    {
        while (Logs.Count > 250)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private string SuggestedJobName()
    {
        var baseName = L10n.Text("NewBackup");
        if (Jobs.All(job => job.Name != baseName))
        {
            return baseName;
        }

        var index = 2;
        while (Jobs.Any(job => job.Name == $"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
