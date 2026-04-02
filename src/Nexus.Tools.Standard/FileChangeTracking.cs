using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Core.Pipeline;
using Nexus.Core.Tools;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nexus.Tools.Standard;

public sealed class FileChangeTrackingOptions
{
    public string BaseDirectory { get; set; } = Directory.GetCurrentDirectory();

    public int MaxPreviewLines { get; set; } = 60;

    public int MaxPreviewCharacters { get; set; } = 4_000;

    public bool TrackFileWrite { get; set; } = true;

    public bool TrackFileEdit { get; set; } = true;
}

public sealed record FileChangeStats(int AddedLines, int RemovedLines);

public sealed record TrackedFileChange(
    int ChangeId,
    string ToolName,
    string Path,
    string AbsolutePath,
    DateTimeOffset Timestamp,
    string? BeforeText,
    string? AfterText,
    string UnifiedDiff,
    FileChangeStats Stats,
    bool IsReverted = false,
    DateTimeOffset? RevertedAt = null);

public sealed record FileChangeTrackingResult(
    int ChangeId,
    string Path,
    string ToolName,
    int AddedLines,
    int RemovedLines,
    string DiffPreview,
    object? OriginalResult);

public sealed record FileChangeRevertResult(bool Succeeded, string Message, TrackedFileChange? Change = null);

public interface IFileChangeJournal
{
    IReadOnlyList<TrackedFileChange> ListChanges();

    TrackedFileChange? GetChange(int changeId);

    TrackedFileChange? GetLatestChange();

    Task<FileChangeRevertResult> RevertAsync(int changeId, CancellationToken ct = default);
}

public sealed class InMemoryFileChangeJournal : IFileChangeJournal
{
    private readonly object _gate = new();
    private readonly List<TrackedFileChange> _changes = [];
    private int _nextId = 1;

    public IReadOnlyList<TrackedFileChange> ListChanges()
    {
        lock (_gate)
            return _changes.ToArray();
    }

    public TrackedFileChange? GetChange(int changeId)
    {
        lock (_gate)
            return _changes.FirstOrDefault(change => change.ChangeId == changeId);
    }

    public TrackedFileChange? GetLatestChange()
    {
        lock (_gate)
            return _changes.LastOrDefault();
    }

    internal TrackedFileChange Record(string toolName, string path, string absolutePath, string? beforeText, string? afterText)
    {
        var diff = UnifiedDiffFormatter.Create(path, beforeText, afterText);
        var stats = UnifiedDiffFormatter.CalculateStats(beforeText, afterText);
        lock (_gate)
        {
            var change = new TrackedFileChange(
                _nextId++,
                toolName,
                path,
                absolutePath,
                DateTimeOffset.UtcNow,
                beforeText,
                afterText,
                diff,
                stats);
            _changes.Add(change);
            return change;
        }
    }

    public async Task<FileChangeRevertResult> RevertAsync(int changeId, CancellationToken ct = default)
    {
        TrackedFileChange? change;
        lock (_gate)
            change = _changes.FirstOrDefault(item => item.ChangeId == changeId);

        if (change is null)
            return new FileChangeRevertResult(false, $"Change #{changeId} was not found.");

        if (change.IsReverted)
            return new FileChangeRevertResult(false, $"Change #{changeId} was already reverted.", change);

        string? current = File.Exists(change.AbsolutePath)
            ? await File.ReadAllTextAsync(change.AbsolutePath, ct).ConfigureAwait(false)
            : null;

        if (!string.Equals(current, change.AfterText, StringComparison.Ordinal))
            return new FileChangeRevertResult(false, $"Change #{changeId} can no longer be reverted cleanly because the file has changed since it was applied.", change);

        if (change.BeforeText is null)
        {
            if (File.Exists(change.AbsolutePath))
                File.Delete(change.AbsolutePath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(change.AbsolutePath)!);
            await File.WriteAllTextAsync(change.AbsolutePath, change.BeforeText, ct).ConfigureAwait(false);
        }

        lock (_gate)
        {
            var index = _changes.FindIndex(item => item.ChangeId == changeId);
            if (index >= 0)
            {
                change = change with
                {
                    IsReverted = true,
                    RevertedAt = DateTimeOffset.UtcNow,
                };
                _changes[index] = change;
            }
        }

        return new FileChangeRevertResult(true, $"Reverted change #{changeId} ({change.Path}).", change);
    }
}

public sealed class FileChangeTrackingToolMiddleware : IToolMiddleware
{
    private readonly InMemoryFileChangeJournal _journal;
    private readonly FileChangeTrackingOptions _options;

    public FileChangeTrackingToolMiddleware(InMemoryFileChangeJournal journal, FileChangeTrackingOptions options)
    {
        _journal = journal;
        _options = options;
    }

    public async Task<ToolResult> InvokeAsync(
        ITool tool,
        JsonElement input,
        IToolContext ctx,
        ToolExecutionDelegate next,
        CancellationToken ct)
    {
        if (!ShouldTrack(tool, input, out var relativePath, out var absolutePath))
            return await next(tool, input, ctx, ct).ConfigureAwait(false);

        var beforeText = File.Exists(absolutePath)
            ? await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false)
            : null;

        var result = await next(tool, input, ctx, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
            return result;

        var afterText = File.Exists(absolutePath)
            ? await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false)
            : null;

        if (string.Equals(beforeText, afterText, StringComparison.Ordinal))
            return result;

        var change = _journal.Record(tool.Name, relativePath, absolutePath, beforeText, afterText);
        var preview = UnifiedDiffFormatter.Trim(change.UnifiedDiff, _options.MaxPreviewLines, _options.MaxPreviewCharacters);
        var trackedResult = new FileChangeTrackingResult(
            change.ChangeId,
            change.Path,
            change.ToolName,
            change.Stats.AddedLines,
            change.Stats.RemovedLines,
            preview,
            result.Value);

        var metadata = new Dictionary<string, object>(result.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["changeId"] = change.ChangeId,
            ["path"] = change.Path,
            ["toolName"] = change.ToolName,
            ["addedLines"] = change.Stats.AddedLines,
            ["removedLines"] = change.Stats.RemovedLines,
            ["diffPreview"] = preview,
        };

        return new ToolResult
        {
            IsSuccess = true,
            Value = trackedResult,
            Metadata = metadata,
        };
    }

    private bool ShouldTrack(ITool tool, JsonElement input, out string relativePath, out string absolutePath)
    {
        relativePath = string.Empty;
        absolutePath = string.Empty;

        var name = tool.Name;
        var supported = (_options.TrackFileWrite && string.Equals(name, "file_write", StringComparison.OrdinalIgnoreCase))
            || (_options.TrackFileEdit && string.Equals(name, "file_edit", StringComparison.OrdinalIgnoreCase));
        if (!supported)
            return false;

        if (!input.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.String)
            return false;

        relativePath = pathElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        absolutePath = PathSandbox.ResolvePath(_options.BaseDirectory, relativePath);
        relativePath = PathSandbox.ToRelativePath(_options.BaseDirectory, absolutePath);
        return true;
    }
}

public static class FileChangeTrackingServiceCollectionExtensions
{
    public static IServiceCollection AddFileChangeTracking(this IServiceCollection services, Action<FileChangeTrackingOptions>? configure = null)
    {
        var options = services.FirstOrDefault(service => service.ServiceType == typeof(FileChangeTrackingOptions))?.ImplementationInstance as FileChangeTrackingOptions;
        if (options is null)
        {
            options = new FileChangeTrackingOptions();
            services.AddSingleton(options);
        }

        configure?.Invoke(options);

        services.TryAddSingleton<InMemoryFileChangeJournal>();
        services.TryAddSingleton<IFileChangeJournal>(sp => sp.GetRequiredService<InMemoryFileChangeJournal>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IToolMiddleware, FileChangeTrackingToolMiddleware>());
        return services;
    }
}

internal static class UnifiedDiffFormatter
{
    public static string Create(string path, string? beforeText, string? afterText)
    {
        var beforeLines = SplitLines(beforeText);
        var afterLines = SplitLines(afterText);
        var operations = BuildOperations(beforeLines, afterLines);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"--- a/{path}{Environment.NewLine}");
        builder.Append(CultureInfo.InvariantCulture, $"+++ b/{path}{Environment.NewLine}");
        foreach (var operation in operations)
        {
            var prefix = operation.Kind switch
            {
                DiffKind.Equal => ' ',
                DiffKind.Delete => '-',
                DiffKind.Insert => '+',
                _ => ' ',
            };

            builder.Append(prefix);
            builder.AppendLine(operation.Line);
        }

        return builder.ToString().TrimEnd();
    }

    public static FileChangeStats CalculateStats(string? beforeText, string? afterText)
    {
        var beforeLines = SplitLines(beforeText);
        var afterLines = SplitLines(afterText);
        var operations = BuildOperations(beforeLines, afterLines);
        return new FileChangeStats(
            operations.Count(operation => operation.Kind == DiffKind.Insert),
            operations.Count(operation => operation.Kind == DiffKind.Delete));
    }

    public static string Trim(string diff, int maxLines, int maxCharacters)
    {
        if (string.IsNullOrEmpty(diff))
            return diff;

        var lines = diff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var limitedLines = lines.Take(maxLines).ToList();
        var trimmed = string.Join(Environment.NewLine, limitedLines);
        if (trimmed.Length > maxCharacters)
            trimmed = trimmed[..maxCharacters];

        if (limitedLines.Count < lines.Length || trimmed.Length < diff.Length)
            trimmed += Environment.NewLine + "...";

        return trimmed;
    }

    private static string[] SplitLines(string? text)
    {
        if (text is null)
            return [];

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }

    private static List<DiffOperation> BuildOperations(string[] beforeLines, string[] afterLines)
    {
        var lengths = new int[beforeLines.Length + 1, afterLines.Length + 1];
        for (var i = beforeLines.Length - 1; i >= 0; i--)
        {
            for (var j = afterLines.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(beforeLines[i], afterLines[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var operations = new List<DiffOperation>();
        var beforeIndex = 0;
        var afterIndex = 0;

        while (beforeIndex < beforeLines.Length && afterIndex < afterLines.Length)
        {
            if (string.Equals(beforeLines[beforeIndex], afterLines[afterIndex], StringComparison.Ordinal))
            {
                operations.Add(new DiffOperation(DiffKind.Equal, beforeLines[beforeIndex]));
                beforeIndex++;
                afterIndex++;
                continue;
            }

            if (lengths[beforeIndex + 1, afterIndex] >= lengths[beforeIndex, afterIndex + 1])
            {
                operations.Add(new DiffOperation(DiffKind.Delete, beforeLines[beforeIndex]));
                beforeIndex++;
            }
            else
            {
                operations.Add(new DiffOperation(DiffKind.Insert, afterLines[afterIndex]));
                afterIndex++;
            }
        }

        while (beforeIndex < beforeLines.Length)
            operations.Add(new DiffOperation(DiffKind.Delete, beforeLines[beforeIndex++]));

        while (afterIndex < afterLines.Length)
            operations.Add(new DiffOperation(DiffKind.Insert, afterLines[afterIndex++]));

        return operations;
    }

    private enum DiffKind
    {
        Equal,
        Delete,
        Insert,
    }

    private sealed record DiffOperation(DiffKind Kind, string Line);
}