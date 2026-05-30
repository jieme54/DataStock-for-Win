using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using DataStock.Windows.Models;
using DataStock.Windows.Services;

namespace DataStock.Windows.ViewModels;

public sealed record OptionItem<T>(T Value, string Label);

public sealed class MainViewModel : ObservableObject
{
    private readonly Scheduler scheduler;
    private SyncJob? selectedJob;
    private string selectedLanguageCode;

    public MainViewModel()
    {
        Store = new DataStore();
        scheduler = new Scheduler(Store);
        selectedLanguageCode = Store.Language.Code();
        RefreshOptions();

        AddJobCommand = new RelayCommand(AddJob);
        DeleteSelectedJobCommand = new RelayCommand(DeleteSelectedJob, () => SelectedJob is not null);
        MoveJobUpCommand = new RelayCommand(parameter => MoveJob(parameter, -1), parameter => CanMoveJob(parameter, -1));
        MoveJobDownCommand = new RelayCommand(parameter => MoveJob(parameter, 1), parameter => CanMoveJob(parameter, 1));
        RunSelectedSynchronizeCommand = new RelayCommand(() => RunSelected(SyncRunMode.Synchronize), () => SelectedJob?.CanStartRun == true);
        RunSelectedTransferCommand = new RelayCommand(() => RunSelected(SyncRunMode.Transfer), () => SelectedJob?.CanStartRun == true);
        RunAllCommand = new RelayCommand(Store.RunAll);
        BrowseSourceFolderCommand = new RelayCommand(BrowseSourceFolder, () => SelectedJob is not null);
        BrowseSourceFileCommand = new RelayCommand(BrowseSourceFile, () => SelectedJob is not null);
        AddDestinationCommand = new RelayCommand(AddDestination, () => SelectedJob is not null);
        BrowseDestinationCommand = new RelayCommand(BrowseDestination);
        RemoveDestinationCommand = new RelayCommand(RemoveDestination);
        AddFolderExclusionCommand = new RelayCommand(() => AddExclusion(isFolder: true), () => SelectedJob is not null && !string.IsNullOrWhiteSpace(SelectedJob.SourcePath));
        AddFileExclusionCommand = new RelayCommand(() => AddExclusion(isFolder: false), () => SelectedJob is not null && !string.IsNullOrWhiteSpace(SelectedJob.SourcePath));
        EditFolderExclusionCommand = new RelayCommand(parameter => EditExclusion(parameter, isFolder: true), CanEditExclusion);
        EditFileExclusionCommand = new RelayCommand(parameter => EditExclusion(parameter, isFolder: false), CanEditExclusion);
        RemoveExclusionCommand = new RelayCommand(RemoveExclusion);
        AddTimeCommand = new RelayCommand(AddTime, () => SelectedJob is not null);
        RemoveTimeCommand = new RelayCommand(RemoveTime);
        QuitCommand = new RelayCommand(() => RequestQuit?.Invoke(this, EventArgs.Empty));

        Store.Jobs.CollectionChanged += (_, _) =>
        {
            if (SelectedJob is null || !Store.Jobs.Any(job => job.Id == SelectedJob.Id))
            {
                SelectedJob = Store.Jobs.FirstOrDefault();
            }

            RaiseCommandStates();
        };

        foreach (var job in Store.Jobs)
        {
            ObserveJob(job);
        }

        Store.Jobs.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace && e.NewItems is not null)
            {
                foreach (SyncJob job in e.NewItems)
                {
                    ObserveJob(job);
                }
            }

            RaiseCommandStates();
        };

        SelectedJob = Store.Jobs.FirstOrDefault();
        scheduler.Start();
    }

    public DataStore Store { get; }

    public event EventHandler? RequestQuit;

    public SyncJob? SelectedJob
    {
        get => selectedJob;
        set
        {
            if (SetProperty(ref selectedJob, value))
            {
                RaiseSelectedJobDependents();
                RaiseCommandStates();
            }
        }
    }

    public IReadOnlyList<LanguageOption> LanguageOptions => L10n.LanguageOptions;

    public string SelectedLanguageCode
    {
        get => selectedLanguageCode;
        set
        {
            if (SetProperty(ref selectedLanguageCode, value))
            {
                Store.SetLanguage(AppLanguageExtensions.FromCode(value));
                RefreshOptions();
                RaiseSelectedJobDependents();
            }
        }
    }

    public bool LaunchAtStartup
    {
        get => Store.Settings.LaunchAtLogin;
        set
        {
            Store.SetLaunchAtLogin(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StartupStatusText));
        }
    }

    public string StartupStatusText => StartupRegistrationService.StatusText();

    public IReadOnlyList<OptionItem<ScheduleKind>> ScheduleKindOptions { get; private set; } = [];
    public IReadOnlyList<OptionItem<ScheduleIntervalUnit>> IntervalUnitOptions { get; private set; } = [];
    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToArray();
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 12).Select(index => index * 5).ToArray();
    public IReadOnlyList<int> MonthDays { get; } = Enumerable.Range(1, 31).ToArray();

    public RelayCommand AddJobCommand { get; }
    public RelayCommand DeleteSelectedJobCommand { get; }
    public RelayCommand MoveJobUpCommand { get; }
    public RelayCommand MoveJobDownCommand { get; }
    public RelayCommand RunSelectedSynchronizeCommand { get; }
    public RelayCommand RunSelectedTransferCommand { get; }
    public RelayCommand RunAllCommand { get; }
    public RelayCommand BrowseSourceFolderCommand { get; }
    public RelayCommand BrowseSourceFileCommand { get; }
    public RelayCommand AddDestinationCommand { get; }
    public RelayCommand BrowseDestinationCommand { get; }
    public RelayCommand RemoveDestinationCommand { get; }
    public RelayCommand AddFolderExclusionCommand { get; }
    public RelayCommand AddFileExclusionCommand { get; }
    public RelayCommand EditFolderExclusionCommand { get; }
    public RelayCommand EditFileExclusionCommand { get; }
    public RelayCommand RemoveExclusionCommand { get; }
    public RelayCommand AddTimeCommand { get; }
    public RelayCommand RemoveTimeCommand { get; }
    public RelayCommand QuitCommand { get; }

    public bool IsMondaySelected
    {
        get => IsWeekdaySelected(2);
        set => SetWeekday(2, value);
    }

    public bool IsTuesdaySelected
    {
        get => IsWeekdaySelected(3);
        set => SetWeekday(3, value);
    }

    public bool IsWednesdaySelected
    {
        get => IsWeekdaySelected(4);
        set => SetWeekday(4, value);
    }

    public bool IsThursdaySelected
    {
        get => IsWeekdaySelected(5);
        set => SetWeekday(5, value);
    }

    public bool IsFridaySelected
    {
        get => IsWeekdaySelected(6);
        set => SetWeekday(6, value);
    }

    public bool IsSaturdaySelected
    {
        get => IsWeekdaySelected(7);
        set => SetWeekday(7, value);
    }

    public bool IsSundaySelected
    {
        get => IsWeekdaySelected(1);
        set => SetWeekday(1, value);
    }

    private void AddJob()
    {
        var id = Store.AddJob();
        SelectedJob = Store.Jobs.FirstOrDefault(job => job.Id == id);
    }

    private void DeleteSelectedJob()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var deletedId = SelectedJob.Id;
        Store.DeleteJob(deletedId);
        SelectedJob = Store.Jobs.FirstOrDefault();
    }

    private void MoveJob(object? parameter, int offset)
    {
        if (parameter is not SyncJob job)
        {
            return;
        }

        if (Store.MoveJob(job.Id, offset))
        {
            SelectedJob = job;
            RaiseCommandStates();
        }
    }

    private bool CanMoveJob(object? parameter, int offset)
    {
        if (parameter is not SyncJob job || Store.Jobs.Count < 2)
        {
            return false;
        }

        var index = Store.Jobs.IndexOf(job);
        var newIndex = index + offset;
        return index >= 0 && newIndex >= 0 && newIndex < Store.Jobs.Count;
    }

    private void RunSelected(SyncRunMode mode)
    {
        if (SelectedJob is not null)
        {
            Store.Run(SelectedJob.Id, mode);
        }
    }

    private void BrowseSourceFolder()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var path = DialogService.ChooseFolder(SelectedJob.SourcePath);
        if (path is null)
        {
            return;
        }

        SelectedJob.SourcePath = path;
        SelectedJob.SourceKind = SyncSourceKind.Folder;
    }

    private void BrowseSourceFile()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var path = DialogService.ChooseFile(SelectedJob.SourcePath);
        if (path is null)
        {
            return;
        }

        SelectedJob.SourcePath = path;
        SelectedJob.SourceKind = SyncSourceKind.File;
    }

    private void AddDestination()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var path = DialogService.ChooseFolder();
        if (path is not null)
        {
            SelectedJob.Destinations.Add(new SyncDestination(path));
        }
    }

    private void BrowseDestination(object? parameter)
    {
        if (parameter is not SyncDestination destination)
        {
            return;
        }

        var path = DialogService.ChooseFolder(destination.Path);
        if (path is not null)
        {
            destination.Path = path;
        }
    }

    private void RemoveDestination(object? parameter)
    {
        if (SelectedJob is not null && parameter is SyncDestination destination)
        {
            SelectedJob.Destinations.Remove(destination);
        }
    }

    private void AddExclusion(bool isFolder)
    {
        if (SelectedJob is null || string.IsNullOrWhiteSpace(SelectedJob.SourcePath))
        {
            return;
        }

        var path = isFolder
            ? DialogService.ChooseFolder(SelectedJob.SourcePath)
            : DialogService.ChooseFile(SelectedJob.SourcePath);
        if (path is null)
        {
            return;
        }

        var relative = RelativeExclusionPath(path, SelectedJob.SourcePath);
        if (relative is null || SelectedJob.Exclusions.Any(exclusion => exclusion.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        SelectedJob.Exclusions.Add(new SyncExclusion(relative));
    }

    private void EditExclusion(object? parameter, bool isFolder)
    {
        if (SelectedJob is null || parameter is not SyncExclusion exclusion || string.IsNullOrWhiteSpace(SelectedJob.SourcePath))
        {
            return;
        }

        var currentPath = ExclusionAbsolutePath(exclusion.RelativePath, SelectedJob.SourcePath);
        var path = isFolder
            ? DialogService.ChooseFolder(currentPath)
            : DialogService.ChooseFile(currentPath);
        if (path is null)
        {
            return;
        }

        var relative = RelativeExclusionPath(path, SelectedJob.SourcePath);
        if (relative is null ||
            SelectedJob.Exclusions.Any(item => !ReferenceEquals(item, exclusion) && item.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        exclusion.RelativePath = relative;
    }

    private bool CanEditExclusion(object? parameter)
    {
        return SelectedJob is not null && parameter is SyncExclusion && !string.IsNullOrWhiteSpace(SelectedJob.SourcePath);
    }

    private void RemoveExclusion(object? parameter)
    {
        if (SelectedJob is not null && parameter is SyncExclusion exclusion)
        {
            SelectedJob.Exclusions.Remove(exclusion);
        }
    }

    private void AddTime()
    {
        if (SelectedJob is null)
        {
            return;
        }

        SelectedJob.Schedule.Times.Add(new ClockTime(9, 0));
        SortTimes(SelectedJob.Schedule.Times);
    }

    private void RemoveTime(object? parameter)
    {
        if (SelectedJob is null || parameter is not ClockTime time)
        {
            return;
        }

        SelectedJob.Schedule.Times.Remove(time);
        if (SelectedJob.Schedule.Times.Count == 0)
        {
            SelectedJob.Schedule.Times.Add(new ClockTime(9, 0));
        }
    }

    private bool IsWeekdaySelected(int weekday)
    {
        return SelectedJob?.Schedule.IsWeekdaySelected(weekday) == true;
    }

    private void SetWeekday(int weekday, bool selected)
    {
        SelectedJob?.Schedule.SetWeekday(weekday, selected);
        RaiseWeekdayProperties();
    }

    private void ObserveJob(SyncJob job)
    {
        job.PropertyChanged += JobPropertyChanged;
        job.Schedule.PropertyChanged += JobPropertyChanged;
        job.Schedule.Times.CollectionChanged += (_, _) => RaiseCommandStates();
    }

    private void JobPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseCommandStates();
        if (ReferenceEquals(sender, SelectedJob) || ReferenceEquals(sender, SelectedJob?.Schedule))
        {
            RaiseSelectedJobDependents();
        }
    }

    private void RaiseCommandStates()
    {
        DeleteSelectedJobCommand.RaiseCanExecuteChanged();
        MoveJobUpCommand.RaiseCanExecuteChanged();
        MoveJobDownCommand.RaiseCanExecuteChanged();
        RunSelectedSynchronizeCommand.RaiseCanExecuteChanged();
        RunSelectedTransferCommand.RaiseCanExecuteChanged();
        BrowseSourceFolderCommand.RaiseCanExecuteChanged();
        BrowseSourceFileCommand.RaiseCanExecuteChanged();
        AddDestinationCommand.RaiseCanExecuteChanged();
        AddFolderExclusionCommand.RaiseCanExecuteChanged();
        AddFileExclusionCommand.RaiseCanExecuteChanged();
        EditFolderExclusionCommand.RaiseCanExecuteChanged();
        EditFileExclusionCommand.RaiseCanExecuteChanged();
        AddTimeCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSelectedJobDependents()
    {
        RaiseWeekdayProperties();
        OnPropertyChanged(nameof(SelectedJob));
    }

    private void RaiseWeekdayProperties()
    {
        OnPropertyChanged(nameof(IsMondaySelected));
        OnPropertyChanged(nameof(IsTuesdaySelected));
        OnPropertyChanged(nameof(IsWednesdaySelected));
        OnPropertyChanged(nameof(IsThursdaySelected));
        OnPropertyChanged(nameof(IsFridaySelected));
        OnPropertyChanged(nameof(IsSaturdaySelected));
        OnPropertyChanged(nameof(IsSundaySelected));
    }

    private void RefreshOptions()
    {
        ScheduleKindOptions =
        [
            new OptionItem<ScheduleKind>(ScheduleKind.Daily, L10n.Text("Daily")),
            new OptionItem<ScheduleKind>(ScheduleKind.Weekly, L10n.Text("Weekly")),
            new OptionItem<ScheduleKind>(ScheduleKind.Monthly, L10n.Text("Monthly")),
            new OptionItem<ScheduleKind>(ScheduleKind.EveryNDays, L10n.Text("CustomInterval"))
        ];

        IntervalUnitOptions =
        [
            new OptionItem<ScheduleIntervalUnit>(ScheduleIntervalUnit.Day, L10n.Text("Days")),
            new OptionItem<ScheduleIntervalUnit>(ScheduleIntervalUnit.Week, L10n.Text("Weeks")),
            new OptionItem<ScheduleIntervalUnit>(ScheduleIntervalUnit.Month, L10n.Text("Months"))
        ];

        OnPropertyChanged(nameof(ScheduleKindOptions));
        OnPropertyChanged(nameof(IntervalUnitOptions));
        OnPropertyChanged(nameof(StartupStatusText));
        SelectedJob?.Schedule.RefreshLocalizedProperties();
    }

    private static void SortTimes(ObservableCollection<ClockTime> times)
    {
        var sorted = times.OrderBy(time => time).ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var oldIndex = times.IndexOf(sorted[index]);
            if (oldIndex != index)
            {
                times.Move(oldIndex, index);
            }
        }
    }

    private static string? RelativeExclusionPath(string itemPath, string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var item = Path.GetFullPath(itemPath);
        if (item.Equals(source, StringComparison.OrdinalIgnoreCase) ||
            !item.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetRelativePath(source, item).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ExclusionAbsolutePath(string relativePath, string sourcePath)
    {
        return Path.GetFullPath(Path.Combine(sourcePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
