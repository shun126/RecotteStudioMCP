using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

#pragma warning disable CS1591 // Public record members are documented by their containing API types and README.

namespace Recotte.Core;

/// <summary>Describes whether a lookup found zero, one, or multiple candidates.</summary>
public enum LookupStatus { Found, NotFound, Ambiguous }

/// <summary>Contains a lookup result without silently selecting an ambiguous candidate.</summary>
public sealed record LookupResult<T>(LookupStatus Status, T? Value, IReadOnlyList<T> Candidates,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

/// <summary>Combines supported layer filters.</summary>
public sealed record LayerQuery
{
    public int? Index { get; init; }
    public string? Name { get; init; }
    public string? Type { get; init; }
    public string? SpeakerKey { get; init; }
}

/// <summary>Combines supported timeline-object filters.</summary>
public sealed record TimelineObjectQuery
{
    public int? LayerIndex { get; init; }
    public int? ObjectKey { get; init; }
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? Text { get; init; }
    public ProjectTimeRange? OverlappingRange { get; init; }
}

/// <summary>Combines supported speaker-registry filters.</summary>
public sealed record SpeakerQuery
{
    public int? Index { get; init; }
    public string? Key { get; init; }
    public string? Name { get; init; }
}

/// <summary>Combines supported file-item filters.</summary>
public sealed record FileItemQuery
{
    public int? Index { get; init; }
    public string? Key { get; init; }
    public string? Path { get; init; }
    public string? FileName { get; init; }
    public string? Extension { get; init; }
}

/// <summary>Represents a non-empty half-open project time range.</summary>
public readonly record struct ProjectTimeRange
{
    public ProjectTimeRange(ProjectTime start, ProjectTime end)
    {
        if (start.TotalSeconds >= end.TotalSeconds)
            throw new ArgumentException("A time range must have start before end.");
        Start = start;
        End = end;
    }
    public ProjectTime Start { get; }
    public ProjectTime End { get; }
    public ProjectTime Duration => new(End.TotalSeconds - Start.TotalSeconds);
}

/// <summary>Reports granular operations implemented safely for the current project format.</summary>
public sealed record ProjectCapabilities(
    bool CanRead,
    bool CanValidate,
    bool CanEditSpeakerText,
    bool CanMoveTimelineObject,
    bool CanRemoveTimelineObject,
    bool CanAddTextOnlySpeakerVoice,
    bool CanAddImageObject,
    bool CanAddVideoObject,
    bool CanAddAnnotationText,
    bool CanImportCharacter,
    bool CanAddLayer,
    bool CanAddSpeaker,
    bool CanAddAudioObject,
    bool CanAddCharacter,
    bool CanSaveCopy,
    bool CanOverwrite);

/// <summary>Reports operations safe for one particular timeline entry.</summary>
public sealed record TimelineEntryCapabilities(bool CanUpdateText, bool CanMove, bool CanRemove);

/// <summary>Provides a flattened immutable timeline entry.</summary>
public sealed record TimelineEntryView(
    int LayerIndex,
    string? LayerName,
    string? LayerType,
    int ObjectIndex,
    TimelineObjectId? ObjectId,
    string? ObjectType,
    string? Name,
    string? Text,
    ProjectTime? StartTime,
    ProjectTime? EndTime,
    ProjectTime? Duration,
    bool IsLocked,
    TimelineEntryCapabilities Capabilities);

/// <summary>Filters flattened timeline entries. Range matching uses half-open intervals.</summary>
public sealed record TimelineQuery
{
    public LayerReference? Layer { get; init; }
    public string? LayerType { get; init; }
    public string? ObjectType { get; init; }
    public ProjectTimeRange? OverlappingRange { get; init; }
}

/// <summary>Contains an aggregated, machine-readable project inspection.</summary>
public sealed record ProjectSummary(
    string? SourcePath,
    string? ApplicationVersion,
    CompatibilityLevel Compatibility,
    string CompatibilityReason,
    ProjectCapabilities Capabilities,
    long Revision,
    ProjectTime? Duration,
    bool? AutoDuration,
    int? Width,
    int? Height,
    decimal? FramesPerSecond,
    int LayerCount,
    int ObjectCount,
    int SpeakerCount,
    int FileItemCount,
    IReadOnlyDictionary<string, int> ObjectCountsByType,
    ProjectValidationResult Validation,
    int ErrorCount,
    int WarningCount,
    int? MissingAssetCount);

public sealed partial class RecotteProjectDocument
{
    private ProjectCapabilities? capabilities;

    /// <summary>Gets granular capabilities for this exact project format and its current content.</summary>
    public ProjectCapabilities Capabilities => capabilities ??= BuildCapabilities();

    private ProjectCapabilities BuildCapabilities() => new(
        ProjectCompatibility.CanRead,
        ProjectCompatibility.CanValidate,
        profile.CanEditSpeakerVoice,
        profile.CanEditSpeakerVoice,
        ProjectCompatibility.CanRemoveObjects,
        ProjectCompatibility.CanAddObjects && profile.CanAddTextOnlySpeakerVoice && HasTextOnlySpeakerVoiceTemplate,
        ProjectCompatibility.CanAddObjects && Compatibility == CompatibilityLevel.Supported,
        ProjectCompatibility.CanAddObjects && Compatibility == CompatibilityLevel.Supported,
        ProjectCompatibility.CanAddObjects && Compatibility == CompatibilityLevel.Supported && AnnotationTextTemplateResources.Exists,
        ProjectCompatibility.CanAddObjects && Compatibility == CompatibilityLevel.Supported,
        false, false, false, false,
        ProjectCompatibility.CanSaveCopy,
        ProjectCompatibility.CanOverwrite);

    /// <summary>Gets whether a clonable text-only Speaker Voice is reachable for at least one Speaker layer.</summary>
    private bool HasTextOnlySpeakerVoiceTemplate => TextVoiceTemplateResources.IsCompatibleWith(root) ||
        (root["layers"] is JsonArray layers && layers.OfType<JsonObject>()
            .Where(layer => JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker")
            .SelectMany(layer => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
            .Any(ProjectEditor.IsTextOnlySpeakerVoice));

    public LookupResult<LayerView> FindLayer(LayerReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        LayerQuery query = reference switch
        {
            LayerIndexReference value => new() { Index = value.LayerIndex },
            LayerNameReference value => new() { Name = value.Name },
            SpeakerLayerReference value => new() { Type = "Speaker", SpeakerKey = value.SpeakerKey },
            _ => new(),
        };
        return FindLayer(query);
    }

    public LookupResult<LayerView> FindLayer(LayerQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<LayerView> matches = Layers;
        if (query.Index is int index) matches = matches.Where(item => item.Index == index);
        if (query.Name is not null) matches = matches.Where(item => string.Equals(item.Name, query.Name, StringComparison.Ordinal));
        if (query.Type is not null) matches = matches.Where(item => string.Equals(item.Type, query.Type, StringComparison.Ordinal));
        if (query.SpeakerKey is not null) matches = matches.Where(item =>
            string.Equals(JsonAccess.GetPropertyString(item.JsonNode, "Speaker"), query.SpeakerKey, StringComparison.Ordinal));
        return ToLookup(matches, "layer");
    }

    public LookupResult<TimelineObjectView> FindTimelineObject(TimelineObjectReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        TimelineObjectQuery query = reference switch
        {
            TimelineObjectIdReference value => new() { LayerIndex = value.ObjectId.LayerIndex, ObjectKey = value.ObjectId.ObjectKey },
            ObjectKeyReference value => new() { ObjectKey = value.ObjectKey },
            _ => new(),
        };
        return FindTimelineObject(query);
    }

    public LookupResult<TimelineObjectView> FindTimelineObject(TimelineObjectQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<TimelineObjectView> matches = Layers.SelectMany(layer => layer.Objects);
        if (query.LayerIndex is int layerIndex) matches = matches.Where(item => item.LayerIndex == layerIndex);
        if (query.ObjectKey is int key) matches = matches.Where(item => item.ObjectKey == key);
        if (query.Type is not null) matches = matches.Where(item => string.Equals(item.Type, query.Type, StringComparison.Ordinal));
        if (query.Name is not null) matches = matches.Where(item => string.Equals(item.Name, query.Name, StringComparison.Ordinal));
        if (query.Text is not null) matches = matches.Where(item => string.Equals(item.Text, query.Text, StringComparison.Ordinal));
        if (query.OverlappingRange is ProjectTimeRange range) matches = matches.Where(item => Overlaps(item, range));
        return ToLookup(matches, "timeline object");
    }

    public LookupResult<SpeakerView> FindSpeaker(SpeakerQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<SpeakerView> matches = Speakers;
        if (query.Index is int index) matches = matches.Where(item => item.Index == index);
        if (query.Key is not null) matches = matches.Where(item => string.Equals(item.Key, query.Key, StringComparison.Ordinal));
        if (query.Name is not null) matches = matches.Where(item => string.Equals(item.Name, query.Name, StringComparison.Ordinal));
        return ToLookup(matches, "speaker");
    }

    public LookupResult<FileItemView> FindFileItem(FileItemQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEnumerable<FileItemView> matches = FileItems;
        if (query.Index is int index) matches = matches.Where(item => item.Index == index);
        if (query.Key is not null) matches = matches.Where(item => string.Equals(item.Key, query.Key, StringComparison.Ordinal));
        if (query.Path is not null) matches = matches.Where(item => PathEquals(item.RelativePath, query.Path) || PathEquals(item.AbsolutePath, query.Path));
        if (query.FileName is not null) matches = matches.Where(item => string.Equals(GetFileName(item), query.FileName, PathComparison));
        if (query.Extension is not null) matches = matches.Where(item => string.Equals(Path.GetExtension(GetFileName(item)), query.Extension, PathComparison));
        return ToLookup(matches, "file item");
    }

    public ProjectSummary GetSummary()
    {
        ProjectValidationResult validation = Validate();
        TimelineObjectView[] objects = Layers.SelectMany(layer => layer.Objects).ToArray();
        IReadOnlyDictionary<string, int> typeCounts = new ReadOnlyDictionary<string, int>(objects
            .GroupBy(item => item.Type ?? "<unknown>", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal));
        return new(SourcePath, ApplicationVersionText, Compatibility, ProjectCompatibility.Reason, Capabilities, Revision,
            Settings?.Duration is decimal duration ? new(duration) : null, Settings?.AutoDuration, Settings?.Width, Settings?.Height,
            Settings?.FramesPerSecond, Layers.Count, objects.Length, Speakers.Count, FileItems.Count, typeCounts, validation,
            validation.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Error),
            validation.Diagnostics.Count(item => item.Severity == DiagnosticSeverity.Warning), TryCountMissingAssets());
    }

    public IReadOnlyList<TimelineEntryView> GetTimelineEntries(TimelineQuery? query = null)
    {
        int? selectedLayer = null;
        if (query?.Layer is not null)
        {
            LookupResult<LayerView> lookup = FindLayer(query.Layer);
            if (lookup.Status != LookupStatus.Found)
                return Array.Empty<TimelineEntryView>();
            selectedLayer = lookup.Value!.Index;
        }
        IEnumerable<LayerView> layers = Layers;
        if (selectedLayer is int index) layers = layers.Where(layer => layer.Index == index);
        if (query?.LayerType is not null) layers = layers.Where(layer => string.Equals(layer.Type, query.LayerType, StringComparison.Ordinal));
        IEnumerable<TimelineEntryView> entries = layers.SelectMany(layer => layer.Objects.Select(item => ToTimelineEntry(layer, item)));
        if (query?.ObjectType is not null) entries = entries.Where(item => string.Equals(item.ObjectType, query.ObjectType, StringComparison.Ordinal));
        if (query?.OverlappingRange is ProjectTimeRange range) entries = entries.Where(item => EntryOverlaps(item, range));
        return entries.OrderBy(item => item.StartTime?.TotalSeconds ?? decimal.MaxValue)
            .ThenBy(item => item.LayerIndex).ThenBy(item => item.ObjectIndex).ToArray();
    }

    public IReadOnlyList<TimelineEntryView> FindObjectsAt(ProjectTime time, TimelineQuery? query = null) =>
        GetTimelineEntries(query).Where(item => item.StartTime is ProjectTime start && item.EndTime is ProjectTime end &&
            start.TotalSeconds <= time.TotalSeconds && time.TotalSeconds < end.TotalSeconds).ToArray();

    public IReadOnlyList<TimelineEntryView> FindObjectsOverlapping(ProjectTime start, ProjectTime end, TimelineQuery? query = null)
    {
        ProjectTimeRange range = new(start, end);
        return GetTimelineEntries(query).Where(item => EntryOverlaps(item, range)).ToArray();
    }

    public IReadOnlyList<ProjectTimeRange> FindEmptyRanges(LayerReference layer, ProjectTime start, ProjectTime end)
    {
        ProjectTimeRange requested = new(start, end);
        LookupResult<LayerView> found = FindLayer(layer);
        if (found.Status != LookupStatus.Found)
            throw new InvalidOperationException(found.Diagnostics.First().Message);
        var occupied = found.Value!.Objects.Where(item => item.StartTime is not null && item.EndTime is not null)
            .Select(item => (start: Math.Max(start.TotalSeconds, item.StartTime!.Value), end: Math.Min(end.TotalSeconds, item.EndTime!.Value)))
            .Where(range => range.start < range.end).OrderBy(range => range.start).ToArray();
        List<ProjectTimeRange> empty = new();
        decimal cursor = requested.Start.TotalSeconds;
        foreach (var range in occupied)
        {
            if (range.start > cursor) empty.Add(new(new(cursor), new(range.start)));
            cursor = Math.Max(cursor, range.end);
        }
        if (cursor < requested.End.TotalSeconds) empty.Add(new(new(cursor), requested.End));
        return empty;
    }

    private TimelineEntryView ToTimelineEntry(LayerView layer, TimelineObjectView item)
    {
        bool locked = IsLocked(layer.JsonNode) || IsLocked(item.JsonNode);
        bool speakerVoice = item.Type == "Speaker Voice";
        bool textOnly = speakerVoice && !item.JsonNode.ContainsKey("audio") && string.IsNullOrEmpty(item.FileItemKey) &&
            JsonAccess.TryGetInt32(item.JsonNode, "voice-hash", out int hash) && hash == 0;
        ProjectTime? start = item.StartTime is decimal startSeconds && startSeconds >= 0 ? new(startSeconds) : null;
        ProjectTime? end = item.EndTime is decimal endSeconds && endSeconds >= 0 ? new(endSeconds) : null;
        ProjectTime? duration = start is ProjectTime a && end is ProjectTime b && b.TotalSeconds >= a.TotalSeconds
            ? new(b.TotalSeconds - a.TotalSeconds) : null;
        TimelineObjectId? id = item.ObjectKey is int key ? new(item.LayerIndex, key) : null;
        return new(layer.Index, layer.Name, layer.Type, item.Index, id, item.Type, item.Name, item.Text, start, end,
            duration, locked, new(textOnly && !locked && Capabilities.CanEditSpeakerText,
                speakerVoice && !locked && Capabilities.CanMoveTimelineObject,
                id is not null && !locked && Capabilities.CanRemoveTimelineObject));
    }

    private static LookupResult<T> ToLookup<T>(IEnumerable<T> source, string target)
    {
        T[] candidates = source.ToArray();
        if (candidates.Length == 1) return new(LookupStatus.Found, candidates[0], candidates, Array.Empty<ProjectDiagnostic>());
        string code = candidates.Length == 0 ? "RC6001" : "RC6002";
        string state = candidates.Length == 0 ? "was not found" : "is ambiguous";
        return new(candidates.Length == 0 ? LookupStatus.NotFound : LookupStatus.Ambiguous, default, candidates,
            new[] { new ProjectDiagnostic(code, DiagnosticSeverity.Error, $"The {target} {state}.") });
    }

    private static bool Overlaps(TimelineObjectView item, ProjectTimeRange range) => item.StartTime is decimal start &&
        item.EndTime is decimal end && start < range.End.TotalSeconds && range.Start.TotalSeconds < end;
    private static bool EntryOverlaps(TimelineEntryView item, ProjectTimeRange range) => item.StartTime is ProjectTime start &&
        item.EndTime is ProjectTime end && start.TotalSeconds < range.End.TotalSeconds && range.Start.TotalSeconds < end.TotalSeconds;
    private static bool IsLocked(JsonObject value) => new[] { "locked", "tl-locked", "st-locked", "pv-locked" }
        .Any(name => value[name] is JsonValue json && json.TryGetValue(out bool locked) && locked);
    private static bool PathEquals(string? left, string right) => left is not null && string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), PathComparison);
    private static string? GetFileName(FileItemView item)
    {
        string? value = item.RelativePath ?? item.AbsolutePath;
        if (value is null) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.IsFile) value = uri.LocalPath;
        return Path.GetFileName(value);
    }

    private int? TryCountMissingAssets()
    {
        if (SourcePath is null) return null;
        try
        {
            string directory = Path.GetDirectoryName(SourcePath)!;
            int missing = 0;
            foreach (FileItemView item in FileItems)
            {
                string? candidate = item.RelativePath is not null ? Path.GetFullPath(item.RelativePath, directory) : null;
                if (candidate is null && item.AbsolutePath is not null && Uri.TryCreate(item.AbsolutePath, UriKind.Absolute, out Uri? uri) && uri.IsFile)
                    candidate = uri.LocalPath;
                if (candidate is null) return null;
                if (!File.Exists(candidate)) missing++;
            }
            return missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
