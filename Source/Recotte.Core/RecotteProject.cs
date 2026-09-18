using System.Text.Json;
using System.Text.Json.Nodes;

namespace Recotte.Core;

/// <summary>Loads Recotte Studio project documents.</summary>
public static class RecotteProject
{
    /// <summary>Creates a Recotte Studio 1.8.5.0 empty project from the verified built-in template.</summary>
    public static RecotteProjectDocument Create(ProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProjectName)) throw new ArgumentException("A project name is required.", nameof(request));
        string directory = Path.GetFullPath(request.ProjectDirectory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"The project directory does not exist: {directory}");
        JsonObject root = ProjectTemplateResources.LoadRoot("Recotte.Core.Templates.Empty.ccproj");
        root["time"] = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
        if (root["setting"] is not JsonObject setting) throw new RecotteProjectLoadException("The empty project template has no setting object.");
        setting["guid"] = Guid.NewGuid().ToString("D").ToUpperInvariant();
        setting["project-name"] = request.ProjectName;
        setting["file-explorer-path"] = ProjectCreationValidator.DirectoryUri(directory);
        setting["duration"] = 0m;
        setting["position"] = 0m;
        ProjectCreationRequest normalizedRequest = request with { ProjectDirectory = directory };
        IReadOnlyList<ProjectDiagnostic> diagnostics = ProjectCreationValidator.Validate(root, normalizedRequest, true);
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            throw new RecotteProjectCreationException("The generated project failed creation validation.", diagnostics);
        return new RecotteProjectDocument(root, null, null, normalizedRequest);
    }

    /// <summary>Loads a project or throws when the file cannot be parsed.</summary>
    public static RecotteProjectDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        SourceFileState sourceFileState = SourceFileState.Capture(fullPath);
        using FileStream stream = File.OpenRead(fullPath);
        return Load(stream, fullPath, sourceFileState);
    }

    /// <summary>Loads a project from a stream without taking ownership of the stream.</summary>
    public static RecotteProjectDocument Load(Stream stream) => Load(stream, null, null);

    /// <summary>Attempts to load a project and returns structured diagnostics.</summary>
    public static ProjectLoadResult TryLoad(string path)
    {
        try
        {
            RecotteProjectDocument document = Load(path);
            return new ProjectLoadResult(document, document.LoadDiagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or RecotteProjectLoadException)
        {
            return new ProjectLoadResult(null, new[]
            {
                new ProjectDiagnostic("RC1002", DiagnosticSeverity.Error, exception.Message),
            });
        }
    }

    /// <summary>Attempts to load a project from a stream without taking ownership of it.</summary>
    public static ProjectLoadResult TryLoad(Stream stream)
    {
        try
        {
            RecotteProjectDocument document = Load(stream);
            return new ProjectLoadResult(document, document.LoadDiagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or RecotteProjectLoadException)
        {
            return new ProjectLoadResult(null, new[]
            {
                new ProjectDiagnostic("RC1002", DiagnosticSeverity.Error, exception.Message),
            });
        }
    }

    private static RecotteProjectDocument Load(Stream stream, string? sourcePath, SourceFileState? sourceFileState)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            JsonNode? node = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });

            if (node is not JsonObject root)
            {
                throw new RecotteProjectLoadException("The Recotte project root must be a JSON object.");
            }

            return new RecotteProjectDocument(root, sourcePath, sourceFileState, null);
        }
        catch (JsonException exception)
        {
            throw new RecotteProjectLoadException(
                $"The Recotte project is not valid JSON at line {exception.LineNumber}, byte {exception.BytePositionInLine}.",
                exception);
        }
    }
}
