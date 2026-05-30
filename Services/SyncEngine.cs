using DataStock.Windows.Models;
using System.IO;

namespace DataStock.Windows.Services;

public enum SyncEngineErrorKind
{
    MissingSource,
    InvalidSource,
    InvalidDestination,
    OverlappingPaths
}

public sealed class SyncEngineException : Exception
{
    public SyncEngineException(SyncEngineErrorKind kind, string path)
        : base(MessageFor(kind, path))
    {
        Kind = kind;
        Path = path;
    }

    public SyncEngineErrorKind Kind { get; }
    public string Path { get; }

    private static string MessageFor(SyncEngineErrorKind kind, string path)
    {
        return kind switch
        {
            SyncEngineErrorKind.MissingSource => L10n.Text("MissingSourceFormat", path),
            SyncEngineErrorKind.InvalidSource => L10n.Text("InvalidSourceFormat", path),
            SyncEngineErrorKind.InvalidDestination => L10n.Text("InvalidDestinationFormat", path),
            SyncEngineErrorKind.OverlappingPaths => L10n.Text("SourceOverlapsDestinationFormat", path),
            _ => path
        };
    }
}

public sealed class SyncEngine
{
    public SyncReport Synchronize(SyncJob job, SyncRunMode mode, Action<SyncLogEntry> progress)
    {
        var report = new SyncReport();
        progress(new SyncLogEntry
        {
            JobId = job.Id,
            JobName = job.Name,
            Level = SyncLogLevel.Info,
            Message = L10n.Text(mode == SyncRunMode.Transfer ? "TransferStarted" : "SyncStarted")
        });

        switch (job.SourceKind)
        {
            case SyncSourceKind.File:
                SynchronizeFile(job, mode, report, progress);
                break;
            default:
                SynchronizeFolder(job, mode, report, progress);
                break;
        }

        progress(new SyncLogEntry
        {
            JobId = job.Id,
            JobName = job.Name,
            Level = SyncLogLevel.Success,
            Message = L10n.Text(mode == SyncRunMode.Transfer ? "TransferFinishedFormat" : "SyncFinishedFormat", report.Summary)
        });

        return report;
    }

    private static void SynchronizeFolder(SyncJob job, SyncRunMode mode, SyncReport report, Action<SyncLogEntry> progress)
    {
        var source = FullPath(job.SourcePath);
        if (!Directory.Exists(source))
        {
            if (File.Exists(source))
            {
                throw new SyncEngineException(SyncEngineErrorKind.InvalidSource, source);
            }

            throw new SyncEngineException(SyncEngineErrorKind.MissingSource, source);
        }

        var destinations = job.Destinations
            .Where(destination => destination.IsEnabled)
            .Select(destination => FullPath(destination.Path))
            .ToArray();
        var exclusions = new ExclusionMatcher(job.Exclusions);
        ValidateNoOverlap(source, destinations);

        if (mode == SyncRunMode.Synchronize)
        {
            foreach (var destination in destinations)
            {
                EnsureDirectory(destination);
                progress(new SyncLogEntry
                {
                    JobId = job.Id,
                    JobName = job.Name,
                    Level = SyncLogLevel.Info,
                    Message = L10n.Text("ImportNewItemsFormat", destination)
                });
                ImportMissingItems(destination, source, exclusions, report);
            }
        }

        foreach (var destination in destinations)
        {
            progress(new SyncLogEntry
            {
                JobId = job.Id,
                JobName = job.Name,
                Level = SyncLogLevel.Info,
                Message = L10n.Text("ReplaceChildDirectoryFormat", destination)
            });
            MirrorDirectory(source, destination, exclusions, report);
        }
    }

    private static void SynchronizeFile(SyncJob job, SyncRunMode mode, SyncReport report, Action<SyncLogEntry> progress)
    {
        var source = FullPath(job.SourcePath);
        if (!File.Exists(source))
        {
            if (Directory.Exists(source))
            {
                throw new SyncEngineException(SyncEngineErrorKind.InvalidSource, source);
            }

            throw new SyncEngineException(SyncEngineErrorKind.MissingSource, source);
        }

        var destinations = job.Destinations
            .Where(destination => destination.IsEnabled)
            .Select(destination => FullPath(destination.Path))
            .ToArray();
        var sourceParent = Path.GetDirectoryName(source) ?? source;
        ValidateNoOverlap(sourceParent, destinations);

        if (mode == SyncRunMode.Synchronize)
        {
            foreach (var destinationFolder in destinations)
            {
                EnsureDirectory(destinationFolder);
                var childFile = Path.Combine(destinationFolder, Path.GetFileName(source));
                if (File.Exists(childFile) && FilesDiffer(source, childFile) && IsNewer(childFile, source))
                {
                    var backup = UniqueConflictPath(source);
                    File.Copy(source, backup);
                    File.Delete(source);
                    File.Copy(childFile, source);
                    report.Imported++;
                    report.Conflicts++;
                    progress(new SyncLogEntry
                    {
                        JobId = job.Id,
                        JobName = job.Name,
                        Level = SyncLogLevel.Warning,
                        Message = L10n.Text("ChildNewerImportedFormat", Path.GetFileName(backup))
                    });
                }
            }
        }

        foreach (var destinationFolder in destinations)
        {
            EnsureDirectory(destinationFolder);
            var childFile = Path.Combine(destinationFolder, Path.GetFileName(source));
            if (File.Exists(childFile))
            {
                if (FilesDiffer(source, childFile))
                {
                    File.Delete(childFile);
                    File.Copy(source, childFile);
                    report.Updated++;
                }
            }
            else
            {
                File.Copy(source, childFile);
                report.Copied++;
            }
        }
    }

    private static void ImportMissingItems(string child, string parent, ExclusionMatcher exclusions, SyncReport report)
    {
        foreach (var item in EnumerateTree(child, exclusions))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(child, item));
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var target = Path.Combine(parent, relative);
            if (Directory.Exists(item))
            {
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }

                continue;
            }

            EnsureDirectory(Path.GetDirectoryName(target) ?? parent);
            if (!File.Exists(target))
            {
                File.Copy(item, target);
                report.Imported++;
            }
            else if (FilesDiffer(item, target) && IsNewer(item, target))
            {
                var conflict = UniqueConflictPath(target);
                File.Copy(item, conflict);
                report.Imported++;
                report.Conflicts++;
            }
        }
    }

    private static void MirrorDirectory(string source, string destination, ExclusionMatcher exclusions, SyncReport report)
    {
        EnsureDirectory(destination);
        var sourceRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceItem in EnumerateTree(source, exclusions))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(source, sourceItem));
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            sourceRelativePaths.Add(relative);
            var target = Path.Combine(destination, relative);
            if (Directory.Exists(sourceItem))
            {
                EnsureDirectory(target);
                continue;
            }

            EnsureDirectory(Path.GetDirectoryName(target) ?? destination);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
                File.Copy(sourceItem, target);
                report.Updated++;
            }
            else if (File.Exists(target))
            {
                if (FilesDiffer(sourceItem, target))
                {
                    File.Delete(target);
                    File.Copy(sourceItem, target);
                    report.Updated++;
                }
            }
            else
            {
                File.Copy(sourceItem, target);
                report.Copied++;
            }
        }

        if (!Directory.Exists(destination))
        {
            return;
        }

        var extras = Directory
            .EnumerateFileSystemEntries(destination, "*", SearchOption.AllDirectories)
            .Where(item =>
            {
                var relative = NormalizeRelative(Path.GetRelativePath(destination, item));
                return !sourceRelativePaths.Contains(relative);
            })
            .OrderByDescending(item => item.Length)
            .ToArray();

        foreach (var extra in extras)
        {
            if (Directory.Exists(extra))
            {
                Directory.Delete(extra, recursive: true);
            }
            else if (File.Exists(extra))
            {
                File.Delete(extra);
            }

            report.Deleted++;
        }
    }

    private static IEnumerable<string> EnumerateTree(string root, ExclusionMatcher exclusions)
    {
        return EnumerateTree(root, root, exclusions);
    }

    private static IEnumerable<string> EnumerateTree(string originalRoot, string currentRoot, ExclusionMatcher exclusions)
    {
        foreach (var directory in Directory.EnumerateDirectories(currentRoot))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(originalRoot, directory));
            if (exclusions.Contains(relative))
            {
                continue;
            }

            yield return directory;
            foreach (var child in EnumerateTree(originalRoot, directory, exclusions))
            {
                yield return child;
            }
        }

        foreach (var file in Directory.EnumerateFiles(currentRoot))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(originalRoot, file));
            if (!exclusions.Contains(relative))
            {
                yield return file;
            }
        }
    }

    private static void ValidateNoOverlap(string source, IEnumerable<string> destinations)
    {
        var sourcePath = TrimEndingSeparator(FullPath(source));
        foreach (var destination in destinations)
        {
            if (File.Exists(destination))
            {
                throw new SyncEngineException(SyncEngineErrorKind.InvalidDestination, destination);
            }

            var destinationPath = TrimEndingSeparator(FullPath(destination));
            if (IsSameOrChild(sourcePath, destinationPath) || IsSameOrChild(destinationPath, sourcePath))
            {
                throw new SyncEngineException(SyncEngineErrorKind.OverlappingPaths, destination);
            }
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new SyncEngineException(SyncEngineErrorKind.InvalidDestination, path);
        }

        Directory.CreateDirectory(path);
    }

    private static bool FilesDiffer(string lhs, string rhs)
    {
        var leftInfo = new FileInfo(lhs);
        var rightInfo = new FileInfo(rhs);
        if (leftInfo.Length != rightInfo.Length)
        {
            return true;
        }

        return Math.Abs((leftInfo.LastWriteTimeUtc - rightInfo.LastWriteTimeUtc).TotalSeconds) > 1;
    }

    private static bool IsNewer(string lhs, string rhs)
    {
        return File.GetLastWriteTimeUtc(lhs) > File.GetLastWriteTimeUtc(rhs).AddSeconds(1);
    }

    private static string UniqueConflictPath(string original)
    {
        var directory = Path.GetDirectoryName(original) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(original);
        var extension = Path.GetExtension(original);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(directory, $"{baseName}.conflit-{stamp}{extension}");
        var index = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}.conflit-{stamp}-{index}{extension}");
            index++;
        }

        return candidate;
    }

    private static bool IsSameOrChild(string path, string possibleParent)
    {
        return path.Equals(possibleParent, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(possibleParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string FullPath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string TrimEndingSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeRelative(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

internal sealed class ExclusionMatcher
{
    private readonly string[] relativePaths;

    public ExclusionMatcher(IEnumerable<SyncExclusion> exclusions)
        : this(exclusions.Select(exclusion => Normalize(exclusion.RelativePath)).Where(path => path.Length > 0).ToArray())
    {
    }

    private ExclusionMatcher(string[] relativePaths)
    {
        this.relativePaths = relativePaths;
    }

    public bool Contains(string relativePath)
    {
        var normalizedPath = Normalize(relativePath);
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        return relativePaths.Any(excludedPath =>
            normalizedPath.Equals(excludedPath, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(excludedPath + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        return path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Aggregate("", (current, part) => current.Length == 0 ? part : $"{current}/{part}");
    }
}
