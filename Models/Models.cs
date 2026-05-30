using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using DataStock.Windows.ViewModels;

namespace DataStock.Windows.Models;

public enum SyncSourceKind
{
    Folder,
    File
}

public enum SyncRunMode
{
    Synchronize,
    Transfer
}

public enum ScheduleKind
{
    Manual,
    Daily,
    Weekly,
    Monthly,
    EveryNDays,
    CustomWeekdays
}

public enum ScheduleIntervalUnit
{
    Day,
    Week,
    Month
}

public enum SyncLogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class ClockTime : ObservableObject, IComparable<ClockTime>
{
    private int hour;
    private int minute;

    public ClockTime()
    {
    }

    public ClockTime(int hour, int minute)
    {
        this.hour = hour;
        this.minute = minute;
    }

    public int Hour
    {
        get => hour;
        set
        {
            if (SetProperty(ref hour, Math.Clamp(value, 0, 23)))
            {
                OnPropertyChanged(nameof(Formatted));
            }
        }
    }

    public int Minute
    {
        get => minute;
        set
        {
            if (SetProperty(ref minute, Math.Clamp(value, 0, 59)))
            {
                OnPropertyChanged(nameof(Formatted));
            }
        }
    }

    [JsonIgnore]
    public string Formatted => $"{Hour:00}:{Minute:00}";

    public int CompareTo(ClockTime? other)
    {
        if (other is null)
        {
            return 1;
        }

        var hourComparison = Hour.CompareTo(other.Hour);
        return hourComparison != 0 ? hourComparison : Minute.CompareTo(other.Minute);
    }

    public ClockTime Clone()
    {
        return new ClockTime(Hour, Minute);
    }
}

public sealed class SyncDestination : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string path = "";
    private bool isEnabled = true;

    public SyncDestination()
    {
    }

    public SyncDestination(string path)
    {
        this.path = path;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Path
    {
        get => path;
        set => SetProperty(ref path, value ?? "");
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public SyncDestination Clone()
    {
        return new SyncDestination(Path)
        {
            Id = Id,
            IsEnabled = IsEnabled
        };
    }
}

public sealed class SyncExclusion : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string relativePath = "";

    public SyncExclusion()
    {
    }

    public SyncExclusion(string relativePath)
    {
        this.relativePath = relativePath;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string RelativePath
    {
        get => relativePath;
        set => SetProperty(ref relativePath, value ?? "");
    }

    public SyncExclusion Clone()
    {
        return new SyncExclusion(RelativePath) { Id = Id };
    }
}

public sealed class SyncSchedule : ObservableObject
{
    private bool isEnabled;
    private SyncRunMode runMode = SyncRunMode.Synchronize;
    private ScheduleKind kind = ScheduleKind.Manual;
    private ObservableCollection<ClockTime> times = new([new ClockTime(9, 0)]);
    private HashSet<int> weekdays = new() { 2 };
    private int dayOfMonth = 1;
    private int intervalDays = 2;
    private ScheduleIntervalUnit intervalUnit = ScheduleIntervalUnit.Day;
    private DateTimeOffset anchorDate = DateTimeOffset.Now;

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (SetProperty(ref isEnabled, value))
            {
                NormalizeKind();
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public SyncRunMode RunMode
    {
        get => runMode;
        set
        {
            if (SetProperty(ref runMode, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public ScheduleKind Kind
    {
        get => kind;
        set
        {
            if (SetProperty(ref kind, value))
            {
                NormalizeKind();
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public ObservableCollection<ClockTime> Times
    {
        get => times;
        set
        {
            var next = value ?? new ObservableCollection<ClockTime>();
            if (next.Count == 0)
            {
                next.Add(new ClockTime(9, 0));
            }

            if (SetProperty(ref times, next))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public HashSet<int> Weekdays
    {
        get => weekdays;
        set
        {
            var next = value is { Count: > 0 } ? value : new HashSet<int> { 2 };
            if (SetProperty(ref weekdays, next))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int DayOfMonth
    {
        get => dayOfMonth;
        set
        {
            if (SetProperty(ref dayOfMonth, Math.Clamp(value, 1, 31)))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int IntervalDays
    {
        get => intervalDays;
        set
        {
            if (SetProperty(ref intervalDays, Math.Clamp(value, 1, 365)))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public ScheduleIntervalUnit IntervalUnit
    {
        get => intervalUnit;
        set
        {
            if (SetProperty(ref intervalUnit, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public DateTimeOffset AnchorDate
    {
        get => anchorDate;
        set => SetProperty(ref anchorDate, value);
    }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (!IsEnabled || Kind == ScheduleKind.Manual)
            {
                return L10n.Text("AutomationDisabled");
            }

            var sortedTimes = Times.OrderBy(time => time).Select(time => time.Formatted).ToArray();
            var timeText = sortedTimes.Length == 0 ? "09:00" : string.Join(", ", sortedTimes);

            var rhythmText = Kind switch
            {
                ScheduleKind.Daily => L10n.Text("EveryDayAtFormat", timeText),
                ScheduleKind.Weekly or ScheduleKind.CustomWeekdays => L10n.Text("EveryWeekAtFormat", WeekdaySummary, timeText),
                ScheduleKind.Monthly => L10n.Text("EveryMonthDayAtFormat", DayOfMonth, timeText),
                ScheduleKind.EveryNDays => IntervalUnit switch
                {
                    ScheduleIntervalUnit.Week => IntervalDays == 1
                        ? L10n.Text("EveryWeekAtFormat", WeekdaySummary, timeText)
                        : L10n.Text("EveryNWeeksAtFormat", IntervalDays, WeekdaySummary, timeText),
                    ScheduleIntervalUnit.Month => IntervalDays == 1
                        ? L10n.Text("EveryMonthDayAtFormat", DayOfMonth, timeText)
                        : L10n.Text("EveryNMonthsDayAtFormat", IntervalDays, DayOfMonth, timeText),
                    _ => IntervalDays == 1
                        ? L10n.Text("EveryDayAtFormat", timeText)
                        : L10n.Text("EveryNDaysAtFormat", IntervalDays, timeText)
                },
                _ => L10n.Text("AutomationDisabled")
            };

            return L10n.Text("ScheduleSummaryWithModeFormat", L10n.RunModeLabel(RunMode), rhythmText);
        }
    }

    [JsonIgnore]
    public string WeekdaySummary
    {
        get
        {
            var labels = Weekdays
                .OrderBy(WeekdayOrder)
                .Select(ShortWeekdayName)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray();
            return labels.Length == 0 ? L10n.Text("NoDay") : string.Join(", ", labels);
        }
    }

    public string? RunKey(DateTimeOffset date)
    {
        if (!IsEnabled || Kind == ScheduleKind.Manual)
        {
            return null;
        }

        if (!Times.Any(time => time.Hour == date.Hour && time.Minute == date.Minute))
        {
            return null;
        }

        var weekday = SwiftWeekday(date.DayOfWeek);
        switch (Kind)
        {
            case ScheduleKind.Daily:
                break;
            case ScheduleKind.Weekly:
            case ScheduleKind.CustomWeekdays:
                if (!Weekdays.Contains(weekday))
                {
                    return null;
                }

                break;
            case ScheduleKind.Monthly:
                if (date.Day != EffectiveDayOfMonth(date))
                {
                    return null;
                }

                break;
            case ScheduleKind.EveryNDays:
                var interval = Math.Max(IntervalDays, 1);
                if (!MatchesInterval(date, interval, weekday))
                {
                    return null;
                }

                break;
        }

        return $"{date:yyyy-MM-dd HH:mm}";
    }

    public int EffectiveDayOfMonth(DateTimeOffset date)
    {
        var lastDay = DateTime.DaysInMonth(date.Year, date.Month);
        return Math.Min(DayOfMonth, lastDay);
    }

    public bool IsWeekdaySelected(int weekday)
    {
        return Weekdays.Contains(weekday);
    }

    public void SetWeekday(int weekday, bool selected)
    {
        var updated = new HashSet<int>(Weekdays);
        if (selected)
        {
            updated.Add(weekday);
        }
        else
        {
            updated.Remove(weekday);
        }

        if (updated.Count == 0)
        {
            updated.Add(weekday);
        }

        Weekdays = updated;
    }

    public SyncSchedule Clone()
    {
        return new SyncSchedule
        {
            IsEnabled = IsEnabled,
            RunMode = RunMode,
            Kind = Kind,
            Times = new ObservableCollection<ClockTime>(Times.Select(time => time.Clone())),
            Weekdays = new HashSet<int>(Weekdays),
            DayOfMonth = DayOfMonth,
            IntervalDays = IntervalDays,
            IntervalUnit = IntervalUnit,
            AnchorDate = AnchorDate
        };
    }

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(RunMode));
        OnPropertyChanged(nameof(IntervalUnit));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(WeekdaySummary));
    }

    private bool MatchesInterval(DateTimeOffset date, int interval, int weekday)
    {
        switch (IntervalUnit)
        {
            case ScheduleIntervalUnit.Week:
                if (!Weekdays.Contains(weekday))
                {
                    return false;
                }

                var anchorWeekStart = StartOfWeek(AnchorDate.Date);
                var currentWeekStart = StartOfWeek(date.Date);
                var weeks = (int)((currentWeekStart - anchorWeekStart).TotalDays / 7);
                return weeks >= 0 && weeks % interval == 0;
            case ScheduleIntervalUnit.Month:
                var months = ((date.Year - AnchorDate.Year) * 12) + date.Month - AnchorDate.Month;
                return months >= 0 && months % interval == 0 && date.Day == EffectiveDayOfMonth(date);
            default:
                var days = (date.Date - AnchorDate.Date).Days;
                return days >= 0 && days % interval == 0;
        }
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + ((int)date.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static int SwiftWeekday(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday ? 1 : (int)dayOfWeek + 1;
    }

    private static int WeekdayOrder(int weekday)
    {
        return weekday == 1 ? 7 : weekday - 1;
    }

    private static string ShortWeekdayName(int weekday)
    {
        return weekday switch
        {
            1 => L10n.Text("WeekdaySundayShort"),
            2 => L10n.Text("WeekdayMondayShort"),
            3 => L10n.Text("WeekdayTuesdayShort"),
            4 => L10n.Text("WeekdayWednesdayShort"),
            5 => L10n.Text("WeekdayThursdayShort"),
            6 => L10n.Text("WeekdayFridayShort"),
            7 => L10n.Text("WeekdaySaturdayShort"),
            _ => ""
        };
    }

    private void NormalizeKind()
    {
        if (Kind == ScheduleKind.CustomWeekdays)
        {
            kind = ScheduleKind.Weekly;
            OnPropertyChanged(nameof(Kind));
        }

        if (IsEnabled && Kind == ScheduleKind.Manual)
        {
            kind = ScheduleKind.Daily;
            OnPropertyChanged(nameof(Kind));
        }

        if (Times.Count == 0)
        {
            Times.Add(new ClockTime(9, 0));
        }

        if (Weekdays.Count == 0)
        {
            Weekdays.Add(2);
        }
    }
}

public sealed class SyncJob : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private string name = L10n.Text("NewBackup");
    private SyncSourceKind sourceKind = SyncSourceKind.Folder;
    private string sourcePath = "";
    private ObservableCollection<SyncDestination> destinations = [];
    private ObservableCollection<SyncExclusion> exclusions = [];
    private SyncSchedule schedule = new();
    private bool isEnabled = true;
    private DateTimeOffset createdAt = DateTimeOffset.Now;
    private DateTimeOffset? lastRunDate;
    private bool? lastRunSucceeded;
    private string? lastRunMessage;
    private string? lastScheduledRunKey;
    private ObservableCollection<SyncHistoryEntry> history = [];
    private bool isRunning;
    private SyncRunMode? runningMode;

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public SyncSourceKind SourceKind
    {
        get => sourceKind;
        set
        {
            if (SetProperty(ref sourceKind, value))
            {
                OnPropertyChanged(nameof(SourceKindLabel));
            }
        }
    }

    public string SourcePath
    {
        get => sourcePath;
        set
        {
            if (SetProperty(ref sourcePath, value ?? ""))
            {
                DetectSourceKind();
                RefreshState();
            }
        }
    }

    public ObservableCollection<SyncDestination> Destinations
    {
        get => destinations;
        set
        {
            if (SetProperty(ref destinations, value ?? []))
            {
                RefreshState();
            }
        }
    }

    public ObservableCollection<SyncExclusion> Exclusions
    {
        get => exclusions;
        set => SetProperty(ref exclusions, value ?? []);
    }

    public SyncSchedule Schedule
    {
        get => schedule;
        set
        {
            if (SetProperty(ref schedule, value ?? new SyncSchedule()))
            {
                OnPropertyChanged(nameof(ScheduleSummary));
            }
        }
    }

    public bool IsEnabled
    {
        get => isEnabled;
        set => SetProperty(ref isEnabled, value);
    }

    public DateTimeOffset CreatedAt
    {
        get => createdAt;
        set => SetProperty(ref createdAt, value);
    }

    public DateTimeOffset? LastRunDate
    {
        get => lastRunDate;
        set
        {
            if (SetProperty(ref lastRunDate, value))
            {
                OnPropertyChanged(nameof(LastRunDateDisplay));
            }
        }
    }

    public bool? LastRunSucceeded
    {
        get => lastRunSucceeded;
        set
        {
            if (SetProperty(ref lastRunSucceeded, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusGlyph));
            }
        }
    }

    public string? LastRunMessage
    {
        get => lastRunMessage;
        set => SetProperty(ref lastRunMessage, value);
    }

    public string? LastScheduledRunKey
    {
        get => lastScheduledRunKey;
        set => SetProperty(ref lastScheduledRunKey, value);
    }

    public ObservableCollection<SyncHistoryEntry> History
    {
        get => history;
        set => SetProperty(ref history, value ?? []);
    }

    [JsonIgnore]
    public bool IsRunning
    {
        get => isRunning;
        set
        {
            if (SetProperty(ref isRunning, value))
            {
                RefreshState();
                OnPropertyChanged(nameof(ScheduleSummary));
            }
        }
    }

    [JsonIgnore]
    public SyncRunMode? RunningMode
    {
        get => runningMode;
        set
        {
            if (SetProperty(ref runningMode, value))
            {
                OnPropertyChanged(nameof(RunStatusText));
            }
        }
    }

    [JsonIgnore]
    public bool CanRun => !string.IsNullOrWhiteSpace(SourcePath) && Destinations.Any(destination => destination.IsEnabled);

    [JsonIgnore]
    public bool CanStartRun => CanRun && !IsRunning;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? L10n.Text("Unnamed") : Name;

    [JsonIgnore]
    public string ScheduleSummary => IsRunning ? L10n.Text("Running") : Schedule.Summary;

    [JsonIgnore]
    public string SourceKindLabel => L10n.SourceKindLabel(SourceKind);

    [JsonIgnore]
    public string StatusText => CanRun ? L10n.Text("Ready") : L10n.Text("ConfigurationIncomplete");

    [JsonIgnore]
    public string StatusGlyph => CanRun ? "\uE73E" : "\uE7BA";

    [JsonIgnore]
    public string RunStatusText => RunningMode switch
    {
        SyncRunMode.Transfer => L10n.Text("Transferring"),
        SyncRunMode.Synchronize => L10n.Text("Syncing"),
        _ => ""
    };

    [JsonIgnore]
    public string LastRunDateDisplay => LastRunDate is null
        ? ""
        : LastRunDate.Value.LocalDateTime.ToString("g", L10n.CultureInfoFor(L10n.CurrentLanguage));

    public SyncJob CloneForRun()
    {
        return new SyncJob
        {
            Id = Id,
            Name = Name,
            SourceKind = SourceKind,
            SourcePath = SourcePath,
            Destinations = new ObservableCollection<SyncDestination>(Destinations.Select(destination => destination.Clone())),
            Exclusions = new ObservableCollection<SyncExclusion>(Exclusions.Select(exclusion => exclusion.Clone())),
            Schedule = Schedule.Clone(),
            IsEnabled = IsEnabled,
            CreatedAt = CreatedAt,
            LastRunDate = LastRunDate,
            LastRunSucceeded = LastRunSucceeded,
            LastRunMessage = LastRunMessage,
            LastScheduledRunKey = LastScheduledRunKey,
            History = new ObservableCollection<SyncHistoryEntry>(History.Select(entry => entry.Clone()))
        };
    }

    public void RefreshState()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanStartRun));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    public void RefreshLocalizedProperties()
    {
        Schedule.RefreshLocalizedProperties();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ScheduleSummary));
        OnPropertyChanged(nameof(SourceKindLabel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RunStatusText));
        OnPropertyChanged(nameof(LastRunDateDisplay));
    }

    private void DetectSourceKind()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(SourcePath));
            if (Directory.Exists(fullPath))
            {
                SourceKind = SyncSourceKind.Folder;
            }
            else if (File.Exists(fullPath))
            {
                SourceKind = SyncSourceKind.File;
            }
        }
        catch
        {
            // The sync engine will report invalid paths when a run is requested.
        }
    }
}

public sealed class SyncHistoryEntry : ObservableObject
{
    private Guid id = Guid.NewGuid();
    private DateTimeOffset date = DateTimeOffset.Now;
    private bool succeeded;
    private string message = "";

    public SyncHistoryEntry()
    {
    }

    public SyncHistoryEntry(DateTimeOffset date, bool succeeded, string message)
    {
        this.date = date;
        this.succeeded = succeeded;
        this.message = message;
    }

    public Guid Id
    {
        get => id;
        set => SetProperty(ref id, value);
    }

    public DateTimeOffset Date
    {
        get => date;
        set
        {
            if (SetProperty(ref date, value))
            {
                OnPropertyChanged(nameof(DateDisplay));
            }
        }
    }

    public bool Succeeded
    {
        get => succeeded;
        set => SetProperty(ref succeeded, value);
    }

    public string Message
    {
        get => message;
        set => SetProperty(ref message, value ?? "");
    }

    [JsonIgnore]
    public string DateDisplay => Date.LocalDateTime.ToString("g", L10n.CultureInfoFor(L10n.CurrentLanguage));

    [JsonIgnore]
    public string ResultText => Succeeded ? L10n.Text("Success") : L10n.Text("Failure");

    public SyncHistoryEntry Clone()
    {
        return new SyncHistoryEntry(Date, Succeeded, Message) { Id = Id };
    }
}

public sealed class AppSettings : ObservableObject
{
    private bool launchAtLogin;
    private string? lastLaunchAtLoginError;
    private string? languageCode;

    public bool LaunchAtLogin
    {
        get => launchAtLogin;
        set => SetProperty(ref launchAtLogin, value);
    }

    public string? LastLaunchAtLoginError
    {
        get => lastLaunchAtLoginError;
        set => SetProperty(ref lastLaunchAtLoginError, value);
    }

    public string? LanguageCode
    {
        get => languageCode;
        set => SetProperty(ref languageCode, value);
    }
}

public sealed class DataSnapshot
{
    public ObservableCollection<SyncJob> Jobs { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}

public sealed class SyncLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Date { get; set; } = DateTimeOffset.Now;
    public Guid? JobId { get; set; }
    public string? JobName { get; set; }
    public SyncLogLevel Level { get; set; }
    public string Message { get; set; } = "";

    [JsonIgnore]
    public string TimeDisplay => Date.LocalDateTime.ToString("t", L10n.CultureInfoFor(L10n.CurrentLanguage));

    [JsonIgnore]
    public string Glyph => Level switch
    {
        SyncLogLevel.Success => "\uE73E",
        SyncLogLevel.Warning => "\uE7BA",
        SyncLogLevel.Error => "\uEA39",
        _ => "\uE946"
    };
}

public sealed class SyncReport
{
    public int Imported { get; set; }
    public int Copied { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Conflicts { get; set; }

    public string Summary => L10n.Text("SyncReportSummaryFormat", Imported, Copied, Updated, Deleted, Conflicts);
}
