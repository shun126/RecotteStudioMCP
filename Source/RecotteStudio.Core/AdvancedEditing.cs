using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

/// <summary>Requests registration and placement of a PNG image.</summary>
public sealed record AddImageAssetRequest(int LayerIndex, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests registration and placement of an MP4 video.</summary>
public sealed record AddVideoAssetRequest(int LayerIndex, string AssetPath, string ProjectPath,
    string DisplayName, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests placement of an editor note as a text box on an annotation layer.</summary>
public sealed record AddAnnotationTextRequest(int LayerIndex, string Text, ProjectTime StartTime, ProjectTime EndTime);

/// <summary>Requests placement of an installed stamp at a screen rectangle given in project pixels.</summary>
public sealed record AddFigureRequest(int LayerIndex, FigureDefinition Figure, ProjectTime StartTime, ProjectTime EndTime,
    decimal X, decimal Y, decimal Width, decimal Height);

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

/// <summary>
/// Requests import of one registered character from another project. The imported layer keeps the donor layer's name
/// unless <paramref name="LayerName"/> is given.
/// </summary>
public sealed record ImportCharacterRequest(RecotteProjectDocument Donor, string SpeakerKey,
    ProjectTime StartTime, ProjectTime EndTime, string? LayerName = null);

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
    /// <summary>Gets whether the import inserted a new layer, shifting the layers after it, rather than filling the empty Speaker layer.</summary>
    public bool InsertedLayer { get; init; }
}

public sealed partial class ProjectEditor
{
    /// <summary>Registers a PNG and places an Image object on an annotation layer.</summary>
    public AssetAddResult AddImageAsset(AddImageAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AddAsset(request.LayerIndex, request.AssetPath, request.ProjectPath, request.DisplayName,
            request.StartTime, request.EndTime, ".png", "Annot", "Image", "RecotteStudio.Core.Templates.Image.ccproj");
    }

    /// <summary>Registers an MP4 and places a Video object on a video layer.</summary>
    public AssetAddResult AddVideoAsset(AddVideoAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AddAsset(request.LayerIndex, request.AssetPath, request.ProjectPath, request.DisplayName,
            request.StartTime, request.EndTime, ".mp4", "Video", "Video", "RecotteStudio.Core.Templates.Video.ccproj");
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
        if (annotation["properties"] is JsonObject annotationProperties && annotationProperties["Bounds"] is JsonObject annotationBounds &&
            annotationBounds["p-value"] is JsonArray templateBounds && AnnotationTextTemplateResources.Screen is (int fromWidth, int fromHeight) &&
            ScreenLayout.TryGetSize(root, out int toWidth, out int toHeight) && (fromWidth, fromHeight) != (toWidth, toHeight) &&
            ScreenLayout.AdaptBox(templateBounds, fromWidth, fromHeight, toWidth, toHeight) is JsonArray adapted)
            annotationBounds["p-value"] = adapted;
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

    /// <summary>
    /// Places an installed SVG stamp on an annotation layer, registering its SVG as a figures-relative file item unless
    /// the project already references it.
    /// </summary>
    public AssetAddResult AddFigure(AddFigureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        FigureDefinition figure = request.Figure;
        if (!figure.CanPlace)
            return AssetFailed("RC4801", $"Figure '{figure.Key}' cannot be placed: only SVG figures using the plain 'figure' style are verified.");
        if (request.StartTime.TotalSeconds >= request.EndTime.TotalSeconds)
            return AssetFailed("RC4802", "The figure time range is invalid.");
        if (request.Width <= 0 || request.Height <= 0)
            return AssetFailed("RC4802", "The figure width and height must be positive.");

        var layerPair = Layers().FirstOrDefault(item => item.index == request.LayerIndex);
        if (layerPair.layer is null || !JsonAccess.TryGetString(layerPair.layer, "type", out string type) ||
            type != "Annot" || IsLockedAdvanced(layerPair.layer) || layerPair.layer["layer-objects"] is not JsonArray objects)
            return AssetFailed("RC4803", $"Layer {request.LayerIndex} is not an unlocked Annot layer.");
        if (root["file-items"] is not JsonArray fileItems) return AssetFailed("RC4804", "The project has no file-item registry.");

        JsonObject? template;
        try
        {
            template = ProjectTemplateResources.LoadRoot("RecotteStudio.Core.Templates.Figure.ccproj")["layers"] is JsonArray templateLayers
                ? templateLayers.OfType<JsonObject>()
                    .SelectMany(layer => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>() ?? Array.Empty<JsonObject>())
                    .SingleOrDefault(item => JsonAccess.TryGetString(item, "type", out string itemType) && itemType == "Figure" &&
                        !string.IsNullOrEmpty(JsonAccess.GetPropertyString(item, "File")))
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or RecotteProjectLoadException)
        { return AssetFailed("RC4804", exception.Message); }
        if (template is null) return AssetFailed("RC4804", "The verified figure template is unavailable.");

        int objectKey;
        try { objectKey = allocator.Allocate(AllObjectKeys()); }
        catch (Exception exception) when (exception is OverflowException or InvalidOperationException)
        { return AssetFailed("RC4805", exception.Message); }

        // Recotte Studio stores a stamp's SVG with loc 1 and a path relative to its figures directory.
        JsonObject? registered = fileItems.OfType<JsonObject>().FirstOrDefault(item =>
            JsonAccess.TryGetInt32(item, "loc", out int location) && location == 1 &&
            JsonAccess.TryGetString(item, "rpath", out string relative) &&
            string.Equals(relative, figure.SvgRelativePath, StringComparison.OrdinalIgnoreCase) &&
            JsonAccess.TryGetString(item, "ik", out _));
        string fileKey;
        if (registered is not null) JsonAccess.TryGetString(registered, "ik", out fileKey);
        else
        {
            HashSet<string> existingFileKeys = fileItems.OfType<JsonObject>()
                .Where(item => JsonAccess.TryGetString(item, "ik", out _))
                .Select(item => { JsonAccess.TryGetString(item, "ik", out string value); return value; })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            try { fileKey = fileItemKeyAllocator.Allocate(existingFileKeys); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            { return AssetFailed("RC4805", exception.Message); }
            if (fileKey.Length != 64 || fileKey.Any(characterValue => !Uri.IsHexDigit(characterValue)) || existingFileKeys.Contains(fileKey))
                return AssetFailed("RC4805", "The file-item key allocator returned an invalid or duplicate key.");
        }

        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds);
        if (durationError is not null) return new(durationError, null, null);

        List<ProjectChange> made = new();
        if (registered is null)
        {
            fileItems.Add(new JsonObject
            {
                ["loc"] = 1, ["ik"] = fileKey, ["rpath"] = figure.SvgRelativePath,
                ["apath"] = "file://" + figure.SvgPath!.Replace('\\', '/'),
            });
            made.Add(new("RegisterFileItem", $"file:{fileKey}", null, figure.SvgRelativePath, $"$.file-items[{fileItems.Count - 1}]"));
        }

        JsonObject added = (JsonObject)template.DeepClone();
        added["objkey"] = objectKey;
        added["start-time"] = request.StartTime.TotalSeconds;
        added["end-time"] = request.EndTime.TotalSeconds;
        if (added.ContainsKey("selected")) added["selected"] = false;
        SetPropertyValue(added, "Bounds", new JsonArray(request.X, request.Y, request.Width, request.Height));
        SetPropertyValue(added, "File", fileKey);
        SetPropertyValue(added, "FigureKey", figure.Key);
        SetPropertyValue(added, "FigureName", figure.DisplayNameJa ?? figure.DisplayNameEn ?? figure.Key);
        objects.Add(added);
        TimelineObjectId objectId = new(request.LayerIndex, objectKey);
        made.Add(new("AddFigure", objectId.ToString(), null, figure.Key,
            $"$.layers[{request.LayerIndex}].layer-objects[{objects.Count - 1}]"));
        changes.AddRange(made);
        return new(EditResult.Succeeded(made), objectId, fileKey);
    }

    /// <summary>
    /// Imports one character registration and its character-only Speaker layer from a donor document. The donor's telop
    /// frames replace the target's, because Recotte Studio fits them around every character's position, so the donor must
    /// contain every speaker and every telop frame the target already uses, with each existing character placed as it is here.
    /// </summary>
    public CharacterImportResult ImportCharacter(ImportCharacterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SpeakerKey)) return CharacterFailed("RC4602", "A speaker key is required.");
        if (request.StartTime.TotalSeconds >= request.EndTime.TotalSeconds) return CharacterFailed("RC4605", "The character time range is invalid.");

        JsonObject donorRoot = request.Donor.CloneRoot();
        // A donor made for another screen size is first relaid out for this one, as SetProjectSize does. The layout is
        // deterministic, so characters imported from the same donor earlier still match it.
        if (ScreenLayout.TryGetSize(donorRoot, out int donorWidth, out int donorHeight) &&
            ScreenLayout.TryGetSize(root, out int targetWidth, out int targetHeight) &&
            (donorWidth, donorHeight) != (targetWidth, targetHeight))
            ScreenLayout.Resize(donorRoot, targetWidth, targetHeight, ScreenFit.Cover, respectLocks: false, new(), new());
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

        int reusableIndex = FindReusableSpeakerLayer(targetLayers);
        string donorLayerName = JsonAccess.TryGetString(importedLayer, "name", out string donorName) ? donorName : string.Empty;
        string layerName = request.LayerName ?? donorLayerName;
        if (string.IsNullOrWhiteSpace(layerName))
            return CharacterFailed("RC4607", "The imported layer needs a name; pass a layer name.");
        if (targetLayers.Select((node, index) => (node, index)).Any(pair => pair.index != reusableIndex &&
                pair.node is JsonObject layer && JsonAccess.TryGetString(layer, "name", out string name) && name == layerName))
            return CharacterFailed("RC4607", $"A layer named '{layerName}' already exists; pass a different layer name.");
        if (donorRoot["telop-frames"] is JsonArray)
        {
            HashSet<string> donorFrames = TelopFrameView.ReadAll(donorRoot).Select(frame => frame.Name).ToHashSet(StringComparer.Ordinal);
            // A line can draw its telop in a frame other than its layer's default, and that frame must survive the replacement too.
            string? missingFrame = TargetSpeakerLayers(targetLayers, reusableIndex)
                .SelectMany(item => (item.Layer["layer-objects"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    .Where(voice => JsonAccess.TryGetString(voice, "type", out string type) && type == "Speaker Voice")
                    .Select(voice => JsonAccess.GetPropertyString(voice, "TelopFrame"))
                    .Prepend(JsonAccess.GetPropertyString(item.Layer, "TelopFrame")))
                .FirstOrDefault(frame => !string.IsNullOrEmpty(frame) && !donorFrames.Contains(frame));
            if (missingFrame is not null)
                return CharacterFailed("RC4606", $"The target uses telop frame '{missingFrame}', which the donor does not define.");
            // The donor's frames are fitted around the donor's characters, so each character already here must sit where it does in the donor.
            string? movedSpeaker = TargetSpeakerLayers(targetLayers, reusableIndex)
                .Select(item => (Speaker: JsonAccess.GetPropertyString(item.Layer, "Speaker"), Bounds: CharacterBounds(item.Layer)))
                .Where(item => item.Bounds is not null)
                .FirstOrDefault(item => DonorLayerOfSpeaker(donorLayers, item.Speaker) is not JsonObject donorLayer ||
                    !SemanticJsonComparer.Equals(item.Bounds, CharacterBounds(donorLayer))).Speaker;
            if (movedSpeaker is not null)
                return CharacterFailed("RC4606", $"Character '{movedSpeaker}' is placed differently in the donor, whose telop frames would no longer fit it.");
        }

        importedObjects.Clear();
        character["start-time"] = request.StartTime.TotalSeconds;
        character["end-time"] = request.EndTime.TotalSeconds;
        importedObjects.Add(character);
        importedLayer["name"] = layerName;
        // Recotte Studio writes this layer's generated voices into LinkFolder, which still names the donor's folder.
        SetPropertyValue(importedLayer, "LinkFolder", string.Empty);
        // TelopTargetLayer names a layer: keep it only when it resolves to the same character's layer in this project.
        string? telopTarget = JsonAccess.GetPropertyString(importedLayer, "TelopTargetLayer");
        if (!string.IsNullOrEmpty(telopTarget))
            SetPropertyValue(importedLayer, "TelopTargetLayer", telopTarget == donorLayerName ? layerName
                : TargetLayerOfDonorLayer(donorLayers, targetLayers, reusableIndex, telopTarget) ?? string.Empty);

        EditResult? durationError = ApplyDuration(request.EndTime.TotalSeconds);
        if (durationError is not null) return new(durationError, null, null, null);

        int targetIndex = reusableIndex;
        if (targetIndex >= 0)
            targetLayers[targetIndex] = importedLayer;
        else
        {
            // Layers draw in array order, so the new character goes right after the last Speaker layer: an Annot layer the
            // user put at the back to hold a background image stays behind it.
            int[] speakerIndexes = TargetSpeakerLayers(targetLayers, -1).Select(item => item.Index).ToArray();
            if (speakerIndexes.Length > 0) targetIndex = speakerIndexes.Max() + 1;
            else
            {
                targetIndex = targetLayers.Select((node, index) => (node, index)).FirstOrDefault(pair =>
                    pair.node is JsonObject layer && JsonAccess.TryGetString(layer, "type", out string type) && type == "Annot").index;
                if (targetIndex == 0 && targetLayers[0] is JsonObject first && (!JsonAccess.TryGetString(first, "type", out string firstType) || firstType != "Annot"))
                    targetIndex = targetLayers.Count;
            }
            targetLayers.Insert(targetIndex, importedLayer);
            for (int prior = 0; prior < changes.Count; prior++)
                changes[prior] = LayerInsertionMetadata.Change(changes[prior], targetIndex);
        }
        targetSpeakers.Add(donorSpeaker.DeepClone());

        if (donorRoot["telop-frames"] is JsonArray donorTelopFrames)
            root["telop-frames"] = donorTelopFrames.DeepClone();

        int objectKey = JsonAccess.TryGetInt32(character, "objkey", out int keyValue) ? keyValue : 0;
        TimelineObjectId objectId = new(targetIndex, objectKey);
        List<ProjectChange> made = new()
        {
            new("ImportCharacter", $"speaker:{request.SpeakerKey}", null, request.SpeakerKey, $"$.layers[{targetIndex}]"),
        };
        // A layer imported earlier may have lost its TelopTargetLayer because this layer was not here yet.
        foreach ((JsonObject layer, int index) in TargetSpeakerLayers(targetLayers, targetIndex))
        {
            JsonObject? donorLayer = DonorLayerOfSpeaker(donorLayers, JsonAccess.GetPropertyString(layer, "Speaker"));
            if (donorLayer is null || JsonAccess.GetPropertyString(donorLayer, "TelopTargetLayer") != donorLayerName ||
                !string.IsNullOrEmpty(JsonAccess.GetPropertyString(layer, "TelopTargetLayer"))) continue;
            SetPropertyValue(layer, "TelopTargetLayer", layerName);
            made.Add(new("LinkTelopTargetLayer", $"layer:{index}", string.Empty, layerName,
                $"$.layers[{index}].properties.TelopTargetLayer.p-value"));
        }
        changes.AddRange(made);
        // The speaker was registered last, but the layer it filled may sit in front of other Speaker layers.
        OrderSpeakersByLayers(made);
        return new(EditResult.Succeeded(made), new SpeakerLayerId(targetIndex), objectId, request.SpeakerKey)
        {
            InsertedLayer = reusableIndex < 0,
        };
    }

    // Speaker layers of the target other than the one at excludedIndex (the layer being replaced or just imported).
    private static IEnumerable<(JsonObject Layer, int Index)> TargetSpeakerLayers(JsonArray layers, int excludedIndex) =>
        layers.Select((node, index) => (Layer: node as JsonObject, Index: index))
            .Where(item => item.Index != excludedIndex && item.Layer is not null &&
                JsonAccess.TryGetString(item.Layer, "type", out string type) && type == "Speaker")
            .Select(item => (item.Layer!, item.Index));

    private static JsonObject? DonorLayerOfSpeaker(JsonArray donorLayers, string? speakerKey) =>
        string.IsNullOrEmpty(speakerKey) ? null : donorLayers.OfType<JsonObject>().FirstOrDefault(layer =>
            JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker" &&
            JsonAccess.GetPropertyString(layer, "Speaker") == speakerKey);

    private static JsonNode? CharacterBounds(JsonObject layer) => (layer["layer-objects"] as JsonArray)?.OfType<JsonObject>()
        .Where(item => JsonAccess.TryGetString(item, "type", out string type) && type == "Speaker Character")
        .Select(item => (item["properties"] as JsonObject)?["CharBounds"]).FirstOrDefault();

    // Maps a donor layer name to the name of the target layer holding the same character, if it was imported.
    private static string? TargetLayerOfDonorLayer(JsonArray donorLayers, JsonArray targetLayers, int excludedIndex, string donorLayerName)
    {
        string? speakerKey = donorLayers.OfType<JsonObject>().Where(layer =>
                JsonAccess.TryGetString(layer, "type", out string type) && type == "Speaker" &&
                JsonAccess.TryGetString(layer, "name", out string name) && name == donorLayerName)
            .Select(layer => JsonAccess.GetPropertyString(layer, "Speaker")).FirstOrDefault();
        if (string.IsNullOrEmpty(speakerKey)) return null;
        return TargetSpeakerLayers(targetLayers, excludedIndex)
            .Where(item => JsonAccess.GetPropertyString(item.Layer, "Speaker") == speakerKey)
            .Select(item => JsonAccess.TryGetString(item.Layer, "name", out string name) ? name : null).FirstOrDefault();
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
        // The templates fill a 1920x1080 screen; a new image or video fills this project's screen, whatever its size.
        if (ScreenLayout.TryGetSize(root, out int screenWidth, out int screenHeight))
            SetPropertyValue(added, "Bounds", new JsonArray(0m, 0m, screenWidth, screenHeight));
        if (imageSize is { } size)
            SetPropertyValue(added, "CropBounds", new JsonArray(0m, 0m, size.Width, size.Height));
        // CropBounds is the part of the source shown, in source pixels; a video the size cannot be read from keeps the template's.
        if (objectType == "Video" && Mp4VideoSize.TryRead(fullAsset, out int videoWidth, out int videoHeight))
            SetPropertyValue(added, "CropBounds", new JsonArray(0m, 0m, videoWidth, videoHeight));
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
