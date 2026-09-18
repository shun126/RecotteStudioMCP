using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Requests registration and placement of a PNG image.</summary>
public sealed record AddImageAssetRequest(int LayerIndex, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests registration and placement of an MP4 video.</summary>
public sealed record AddVideoAssetRequest(int LayerIndex, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests placement of an editor note as a text box on an annotation layer.</summary>
public sealed record AddAnnotationTextRequest(int LayerIndex, string Text, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Contains the result of registering and placing an external asset.</summary>
public sealed record AssetAddResult(EditResult EditResult, TimelineObjectId? ObjectId, string? FileItemKey)
{
    /// <summary>Gets whether the asset operation succeeded.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

/// <summary>Requests import of one registered character from another project.</summary>
public sealed record ImportCharacterRequest(RecotteProjectDocument Donor, string SpeakerKey,
    ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Contains the result of importing a character registration and layer.</summary>
public sealed record CharacterImportResult(EditResult EditResult, SpeakerLayerId? LayerId,
    TimelineObjectId? ObjectId, string? SpeakerKey)
{
    /// <summary>Gets whether the import succeeded.</summary>
    public bool Success => EditResult.Success;
    /// <summary>Gets the operation diagnostics.</summary>
    public IReadOnlyList<ProjectDiagnostic> Diagnostics => EditResult.Diagnostics;
    /// <summary>Gets the staged project changes.</summary>
    public IReadOnlyList<ProjectChange> Changes => EditResult.Changes;
}

public sealed partial class ProjectEditor
{
    /// <summary>Registers a PNG and places an Image object on an annotation layer.</summary>
    public AssetAddResult AddImageAsset(AddImageAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AddAsset(request.LayerIndex, request.AssetPath, request.ProjectPath, request.DisplayName,
            request.StartTime, request.EndTime, ".png", "Annot", "Image", "Recotte.Core.Templates.Image.ccproj");
    }

    /// <summary>Registers an MP4 and places a Video object on a video layer.</summary>
    public AssetAddResult AddVideoAsset(AddVideoAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AddAsset(request.LayerIndex, request.AssetPath, request.ProjectPath, request.DisplayName,
            request.StartTime, request.EndTime, ".mp4", "Video", "Video", "Recotte.Core.Templates.Video.ccproj");
    }

    /// <summary>Adds a text-box Figure to an annotation layer for editor notes and production hints.</summary>
    public EditResult AddAnnotationText(AddAnnotationTextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
            return EditResult.Failed("RC4701", "Annotation text is required.");
        if (request.StartTime.TotalSeconds >= request.EndTime.TotalSeconds)
            return EditResult.Failed("RC4702", "The annotation time range is invalid.");

        var layerPair = Layers().FirstOrDefault(item => item.index == request.LayerIndex);
        if (layerPair.layer is null || !JsonAccess.TryGetString(layerPair.layer, "type", out string type) ||
            type != "Annot" || IsLockedAdvanced(layerPair.layer) || layerPair.layer["layer-objects"] is not JsonArray objects)
            return EditResult.Failed("RC4703", $"Layer {request.LayerIndex} is not an unlocked Annot layer.");
        if (!AnnotationTextTemplateResources.TryClone(root, out JsonObject? annotation, out JsonObject? missingStyle,
            out string? templateError))
            return EditResult.Failed("RC4704", templateError!);

        int objectKey;
        try { objectKey = allocator.Allocate(AllObjectKeys()); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        { return EditResult.Failed("RC4705", exception.Message); }
        annotation!["objkey"] = objectKey;
        annotation["start-time"] = request.StartTime.TotalSeconds;
        annotation["end-time"] = request.EndTime.TotalSeconds;
        JsonObject text = (JsonObject)annotation["text"]!;
        text["text"] = request.Text;
        if (text["stext"] is not JsonArray styledText)
            return EditResult.Failed("RC4704", "The verified annotation text template has no styled-text array.");
        JsonObject? textRun = styledText.OfType<JsonObject>()
            .SingleOrDefault(item => JsonAccess.TryGetString(item, "c", out string kind) && kind == "t");
        if (textRun is null)
            return EditResult.Failed("RC4704", "The verified annotation text template has no text run.");
        textRun["text"] = request.Text;

        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds);
        if (durationError is not null) return durationError;

        List<ProjectChange> made = new();
        if (missingStyle is not null && root["text-styles"] is JsonArray styles)
        {
            string styleName = missingStyle["StyleName"]!.GetValue<string>();
            styles.Add(missingStyle);
            made.Add(new("RegisterAnnotationTextStyle", $"style:{styleName}", null,
                styleName, $"$.text-styles[{styles.Count - 1}]"));
        }
        objects.Add(annotation);
        made.Add(new("AddAnnotationText", objectKey.ToString(), null, request.Text,
            $"$.layers[{request.LayerIndex}].layer-objects[{objects.Count - 1}]"));
        changes.AddRange(made);
        return EditResult.Succeeded(made);
    }

    /// <summary>Imports one character registration and its character-only Speaker layer from a donor document.</summary>
    public CharacterImportResult ImportCharacter(ImportCharacterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SpeakerKey)) return CharacterFailed("RC4602", "A speaker key is required.");
        if (request.StartTime.TotalSeconds >= request.EndTime.TotalSeconds) return CharacterFailed("RC4605", "The character time range is invalid.");

        JsonObject donorRoot = request.Donor.CloneRoot();
        if (donorRoot["speakers"] is not JsonArray donorSpeakers || donorRoot["layers"] is not JsonArray donorLayers)
            return CharacterFailed("RC4601", "The donor project has no valid speaker registry or layers.");
        JsonObject[] speakerMatches = donorSpeakers.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetString(item, "key", out string key) && key == request.SpeakerKey).ToArray();
        if (speakerMatches.Length != 1) return CharacterFailed("RC4602", "The requested speaker was not found uniquely in the donor project.");
        JsonObject donorSpeaker = speakerMatches[0];
        if (!JsonAccess.TryGetString(donorSpeaker, "file", out string model) || string.IsNullOrWhiteSpace(model))
            return CharacterFailed("RC4604", "The donor speaker has no character model reference.");

        if (root["speakers"] is not JsonArray targetSpeakers || root["layers"] is not JsonArray targetLayers)
            return CharacterFailed("RC4601", "The target project has no valid speaker registry or layers.");
        if (targetSpeakers.OfType<JsonObject>().Any(item => JsonAccess.TryGetString(item, "key", out string key) && key == request.SpeakerKey))
            return CharacterFailed("RC4603", "The speaker key is already registered in the target project.");
        HashSet<string> donorKeys = donorSpeakers.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetString(item, "key", out _))
            .Select(item => { JsonAccess.TryGetString(item, "key", out string key); return key; }).ToHashSet(StringComparer.Ordinal);
        if (targetSpeakers.OfType<JsonObject>().Any(item => JsonAccess.TryGetString(item, "key", out string key) && !donorKeys.Contains(key)))
            return CharacterFailed("RC4606", "The donor does not contain every speaker already registered in the target project.");
        foreach (string collectionName in new[] { "text-styles", "text-style-lib" })
            if (!SemanticJsonComparer.Equals(root[collectionName], donorRoot[collectionName]))
                return CharacterFailed("RC4606", $"The donor and target {collectionName} definitions are not compatible.");

        JsonObject[] layerMatches = donorLayers.OfType<JsonObject>().Where(layer =>
            JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker" &&
            JsonAccess.GetPropertyString(layer, "Speaker") == request.SpeakerKey).ToArray();
        if (layerMatches.Length != 1) return CharacterFailed("RC4602", "The requested speaker does not map uniquely to a donor Speaker layer.");
        JsonObject importedLayer = (JsonObject)layerMatches[0].DeepClone();
        if (IsLockedAdvanced(importedLayer)) return CharacterFailed("RC4605", "The donor Speaker layer is locked.");
        if (importedLayer["layer-objects"] is not JsonArray importedObjects)
            return CharacterFailed("RC4601", "The donor Speaker layer has no object array.");
        JsonObject[] characters = importedObjects.OfType<JsonObject>()
            .Where(item => JsonAccess.TryGetString(item, "type", out string type) && type == "Speaker Character").ToArray();
        if (characters.Length != 1) return CharacterFailed("RC4602", "The donor Speaker layer must contain exactly one character object.");
        JsonObject character = characters[0];
        importedObjects.Clear();
        character["start-time"] = request.StartTime.TotalSeconds;
        character["end-time"] = request.EndTime.TotalSeconds;
        importedObjects.Add(character);

        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds);
        if (durationError is not null) return new(durationError, null, null, null);

        int targetIndex = FindReusableSpeakerLayer(targetLayers);
        if (targetIndex >= 0)
            targetLayers[targetIndex] = importedLayer;
        else
        {
            targetIndex = targetLayers.Select((node, index) => (node, index)).FirstOrDefault(pair =>
                pair.node is JsonObject layer && JsonAccess.TryGetString(layer, "type", out string type) && type == "Annot").index;
            if (targetIndex == 0 && targetLayers[0] is JsonObject first && (!JsonAccess.TryGetString(first, "type", out string firstType) || firstType != "Annot"))
                targetIndex = targetLayers.Count;
            targetLayers.Insert(targetIndex, importedLayer);
        }
        targetSpeakers.Add(donorSpeaker.DeepClone());

        if (donorRoot["telop-frames"] is JsonArray donorFrames)
            root["telop-frames"] = donorFrames.DeepClone();

        int objectKey = JsonAccess.TryGetInt32(character, "objkey", out int keyValue) ? keyValue : 0;
        TimelineObjectId objectId = new(targetIndex, objectKey);
        ProjectChange change = new("ImportCharacter", $"speaker:{request.SpeakerKey}", null, request.SpeakerKey,
            $"$.layers[{targetIndex}]");
        changes.Add(change);
        return new(EditResult.Succeeded(new[] { change }), new SpeakerLayerId(targetIndex), objectId, request.SpeakerKey);
    }

    private AssetAddResult AddAsset(int layerIndex, string assetPath, string projectPath, string displayName,
        ProjectTime start, ProjectTime end, string extension, string layerType, string objectType, string resourceName)
    {
        if (!string.Equals(Path.GetExtension(assetPath), extension, StringComparison.OrdinalIgnoreCase))
            return AssetFailed("RC4501", $"Only {extension} assets are supported by this operation.");
        string fullAsset;
        string fullProject;
        try { fullAsset = Path.GetFullPath(assetPath); fullProject = Path.GetFullPath(projectPath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return AssetFailed("RC4506", exception.Message); }
        if (!File.Exists(fullAsset)) return AssetFailed("RC4502", "The asset file does not exist.");
        (int Width, int Height)? imageSize = null;
        if (objectType == "Image")
        {
            try { imageSize = ReadPngSize(fullAsset); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            { return AssetFailed("RC4506", exception.Message); }
        }
        if (start.TotalSeconds >= end.TotalSeconds) return AssetFailed("RC4506", "The asset time range is invalid.");

        var layerPair = Layers().FirstOrDefault(item => item.index == layerIndex);
        if (layerPair.layer is null || !JsonAccess.TryGetString(layerPair.layer, "type", out string actualType) || actualType != layerType || IsLockedAdvanced(layerPair.layer))
            return AssetFailed("RC4503", $"Layer {layerIndex} is not an unlocked {layerType} layer.");
        if (layerPair.layer["layer-objects"] is not JsonArray targetObjects)
            return AssetFailed("RC4503", "The target layer has no object array.");

        JsonObject templateRoot;
        try { templateRoot = ProjectTemplateResources.LoadRoot(resourceName); }
        catch (Exception exception) { return AssetFailed("RC4504", exception.Message); }
        JsonObject? template = templateRoot["layers"] is JsonArray templateLayers
            ? templateLayers.OfType<JsonObject>().SelectMany(layer => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
                .SingleOrDefault(item => JsonAccess.TryGetString(item, "type", out string type) && type == objectType)
            : null;
        if (template is null) return AssetFailed("RC4504", "The verified asset template is unavailable.");

        int objectKey;
        try { objectKey = allocator.Allocate(AllObjectKeys()); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        { return AssetFailed("RC4505", exception.Message); }
        HashSet<string> existingFileKeys = root["file-items"] is JsonArray existingItems
            ? existingItems.OfType<JsonObject>().Where(item => JsonAccess.TryGetString(item, "ik", out _))
                .Select(item => { JsonAccess.TryGetString(item, "ik", out string value); return value; }).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);
        string fileKey;
        try { fileKey = fileItemKeyAllocator.Allocate(existingFileKeys); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { return AssetFailed("RC4505", exception.Message); }
        if (fileKey.Length != 64 || fileKey.Any(characterValue => !Uri.IsHexDigit(characterValue)) || existingFileKeys.Contains(fileKey))
            return AssetFailed("RC4505", "The file-item key allocator returned an invalid or duplicate key.");
        if (root["file-items"] is not JsonArray fileItems) return AssetFailed("RC4504", "The project has no file-item registry.");

        EditResult? durationError = ApplyDuration(end.TotalSeconds);
        if (durationError is not null) return new(durationError, null, null);

        string projectDirectory = Path.GetDirectoryName(fullProject)!;
        string relativePath = Path.GetRelativePath(projectDirectory, fullAsset).Replace('\\', '/');
        JsonObject fileItem = new() { ["loc"] = 0, ["ik"] = fileKey, ["rpath"] = relativePath, ["apath"] = new Uri(fullAsset).AbsoluteUri };
        fileItems.Add(fileItem);

        JsonObject added = (JsonObject)template.DeepClone();
        added["objkey"] = objectKey;
        added["name"] = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(fullAsset) : displayName;
        added["start-time"] = start.TotalSeconds;
        added["end-time"] = end.TotalSeconds;
        SetPropertyValue(added, "File", fileKey);
        if (imageSize is { } size)
            SetPropertyValue(added, "CropBounds", new JsonArray(0m, 0m, size.Width, size.Height));
        if (objectType == "Video")
        {
            SetPropertyValue(added, "StartTime", 0m);
            SetPropertyValue(added, "EndTime", end.TotalSeconds - start.TotalSeconds);
            SetPropertyValue(added, "VideoOffset", 0m);
        }
        targetObjects.Add(added);
        TimelineObjectId objectId = new(layerIndex, objectKey);
        ProjectChange[] made =
        {
            new("RegisterFileItem", $"file:{fileKey}", null, relativePath, $"$.file-items[{fileItems.Count - 1}]"),
            new($"Add{objectType}Asset", objectId.ToString(), null, added["name"]?.ToString(),
                $"$.layers[{layerIndex}].layer-objects[{targetObjects.Count - 1}]"),
        };
        changes.AddRange(made);
        return new(EditResult.Succeeded(made), objectId, fileKey);
    }

    private static (int Width, int Height) ReadPngSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header[..8].SequenceEqual(signature) || !header[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("The PNG asset has no valid IHDR header.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw new InvalidDataException("The PNG asset dimensions are invalid.");
        return ((int)width, (int)height);
    }

    private IReadOnlyCollection<int> AllObjectKeys() => Layers()
        .SelectMany(item => (item.layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
        .Where(item => JsonAccess.TryGetInt32(item, "objkey", out _))
        .Select(item => { JsonAccess.TryGetInt32(item, "objkey", out int value); return value; }).ToArray();

    private static int FindReusableSpeakerLayer(JsonArray layers)
    {
        for (int index = 0; index < layers.Count; index++)
            if (layers[index] is JsonObject layer && JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker" &&
                !IsLockedAdvanced(layer) && layer["layer-objects"] is JsonArray objects && objects.Count == 0 &&
                string.IsNullOrEmpty(JsonAccess.GetPropertyString(layer, "Speaker"))) return index;
        return -1;
    }

    private static void SetPropertyValue(JsonObject timelineObject, string property, object value)
    {
        if (timelineObject["properties"] is JsonObject properties && properties[property] is JsonObject entry)
            entry["p-value"] = JsonValue.Create(value);
    }

    private static void SetPropertyValue(JsonObject timelineObject, string property, JsonNode value)
    {
        if (timelineObject["properties"] is JsonObject properties && properties[property] is JsonObject entry)
            entry["p-value"] = value;
    }

    private static bool IsLockedAdvanced(JsonObject value) => new[] { "locked", "tl-locked", "st-locked", "pv-locked" }
        .Any(name => value[name] is JsonValue json && json.TryGetValue(out bool locked) && locked);

    private static AssetAddResult AssetFailed(string code, string message) => new(EditResult.Failed(code, message), null, null);
    private static CharacterImportResult CharacterFailed(string code, string message) => new(EditResult.Failed(code, message), null, null, null);
}
