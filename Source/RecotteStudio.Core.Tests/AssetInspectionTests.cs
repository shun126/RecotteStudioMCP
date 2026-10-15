using System.Text.Json.Nodes;

namespace RecotteStudio.Core.Tests;

public sealed class AssetInspectionTests : IDisposable
{
    private static readonly string ImageKey = new('a', 64);
    private static readonly string FigureKey = new('f', 64);
    private static readonly string UnknownKey = new('e', 64);
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"RecotteStudio.Core.Tests-{Guid.NewGuid():N}");

    public AssetInspectionTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void InspectAssets_ResolvesSavedProjectImageAndItsReferencingObject()
    {
        RecotteProjectDocument document = RecotteProject.Load(Path.Combine(
            TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "004OneImage", "004OneImage.ccproj"));

        AssetInspection inspection = document.InspectAssets();

        AssetReference asset = Assert.Single(inspection.Assets);
        Assert.Equal(0, asset.Location);
        Assert.Equal(AssetLocationKind.Project, asset.LocationKind);
        Assert.Equal(AssetReferenceStatus.Found, asset.Status);
        Assert.True(asset.RelativePathExists);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(document.SourcePath!)!, "Sample.png"), asset.ResolvedRelativePath);
        AssetReferencingObject user = Assert.Single(asset.ReferencedBy);
        Assert.Equal((2, "Image"), (user.LayerIndex, user.ObjectType));
        Assert.Empty(inspection.DanglingReferences);
    }

    [Fact]
    public void InspectAssets_ReportsMissingFileWhenNeitherStoredPathExists()
    {
        string project = SaveImageProject();
        File.Delete(Path.Combine(directory, "Picture.png"));
        RecotteProjectDocument document = RecotteProject.Load(project);

        AssetReference asset = Assert.Single(document.InspectAssets().Assets);

        Assert.Equal(AssetReferenceStatus.Missing, asset.Status);
        Assert.False(asset.RelativePathExists);
        Assert.False(asset.AbsolutePathExists);
        Assert.Equal(Path.Combine(directory, "Picture.png"), asset.ResolvedAbsolutePath);
        Assert.Equal(1, document.GetSummary().MissingAssetCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectAssets_ResolvesFigureOnlyAgainstTheGivenFiguresDirectory(bool useRelativeDirectory)
    {
        string figures = Path.Combine(directory, "figures");
        Directory.CreateDirectory(Path.Combine(figures, "emotion"));
        File.WriteAllText(Path.Combine(figures, "emotion", "emotion_0019.svg"), "<svg/>");
        string project = SaveImageProject(new JsonObject
        {
            ["loc"] = 1, ["ik"] = FigureKey, ["rpath"] = "emotion/emotion_0019.svg",
            ["apath"] = new Uri(Path.Combine(directory, "absent", "emotion_0019.svg")).AbsoluteUri,
        });
        RecotteProjectDocument document = RecotteProject.Load(project);

        string figuresArgument = useRelativeDirectory ? Path.GetRelativePath(Environment.CurrentDirectory, figures) : figures;
        AssetReference resolved = document.InspectAssets(figuresArgument).Assets.Single(asset => asset.Key == FigureKey);
        AssetReference unresolved = document.InspectAssets().Assets.Single(asset => asset.Key == FigureKey);

        Assert.Equal(AssetLocationKind.Figures, resolved.LocationKind);
        Assert.Equal(AssetReferenceStatus.Found, resolved.Status);
        Assert.True(resolved.RelativePathExists);
        Assert.False(resolved.AbsolutePathExists);
        Assert.Equal(Path.Combine(figures, "emotion", "emotion_0019.svg"), resolved.ResolvedRelativePath);
        Assert.Empty(resolved.ReferencedBy);
        Assert.Equal(AssetReferenceStatus.Unresolved, unresolved.Status);
        Assert.Null(unresolved.ResolvedRelativePath);
        Assert.Null(document.GetSummary().MissingAssetCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InspectAssets_RejectsRootedRelativePathButStillChecksAbsolutePath(bool absolutePathExists)
    {
        string existing = Path.Combine(directory, "Existing.svg");
        File.WriteAllText(existing, "<svg/>");
        string project = SaveImageProject(new JsonObject
        {
            ["loc"] = 0, ["ik"] = FigureKey, ["rpath"] = existing,
            ["apath"] = new Uri(absolutePathExists ? existing : Path.Combine(directory, "Absent.svg")).AbsoluteUri,
        });

        AssetReference asset = RecotteProject.Load(project).InspectAssets().Assets.Single(item => item.Key == FigureKey);

        Assert.Null(asset.ResolvedRelativePath);
        Assert.False(asset.RelativePathExists);
        Assert.Equal(absolutePathExists, asset.AbsolutePathExists);
        Assert.Equal(absolutePathExists ? AssetReferenceStatus.Found : AssetReferenceStatus.Unresolved, asset.Status);
    }

    [Fact]
    public void InspectAssets_ResolvesParentRelativePath()
    {
        string project = SaveImageProject(new JsonObject
        {
            ["loc"] = 1, ["ik"] = FigureKey, ["rpath"] = "../Picture.png",
        });
        string figures = Path.Combine(directory, "figures");
        Directory.CreateDirectory(figures);

        AssetReference asset = RecotteProject.Load(project).InspectAssets(figures).Assets.Single(item => item.Key == FigureKey);

        Assert.Equal(AssetReferenceStatus.Found, asset.Status);
        Assert.True(asset.RelativePathExists);
        Assert.Equal(Path.Combine(directory, "Picture.png"), asset.ResolvedRelativePath);
    }

    [Fact]
    public void InspectAssets_LeavesUnverifiedLocationUnresolved()
    {
        File.WriteAllText(Path.Combine(directory, "Other.png"), string.Empty);
        string project = SaveImageProject(new JsonObject
        {
            ["loc"] = 7, ["ik"] = UnknownKey, ["rpath"] = "Other.png",
            ["apath"] = new Uri(Path.Combine(directory, "absent", "Other.png")).AbsoluteUri,
        });

        AssetReference asset = RecotteProject.Load(project).InspectAssets().Assets.Single(item => item.Key == UnknownKey);

        Assert.Equal(AssetLocationKind.Unknown, asset.LocationKind);
        Assert.Equal(AssetReferenceStatus.Unresolved, asset.Status);
        Assert.Null(asset.ResolvedRelativePath);
        Assert.False(asset.AbsolutePathExists);
    }

    [Fact]
    public void InspectAssets_ReportsObjectsWhoseFileKeyIsNotRegistered()
    {
        string project = SaveImageProject();
        JsonObject root = JsonNode.Parse(File.ReadAllText(project))!.AsObject();
        root["file-items"]!.AsArray().Single()!["ik"] = UnknownKey;
        File.WriteAllText(project, root.ToJsonString());

        AssetInspection inspection = RecotteProject.Load(project).InspectAssets();

        Assert.Empty(Assert.Single(inspection.Assets).ReferencedBy);
        AssetReferencingObject dangling = Assert.Single(inspection.DanglingReferences);
        Assert.Equal((ImageKey, 2, "Image"), (dangling.FileItemKey, dangling.LayerIndex, dangling.ObjectType));
    }

    // Saves a project with one image object referencing Picture.png, then appends any extra file items to its registry.
    private string SaveImageProject(params JsonObject[] extraFileItems)
    {
        string asset = Path.Combine(directory, "Picture.png");
        File.Copy(Path.Combine(TestProjects.RepositoryRoot, "Documents", "RecotteProjects", "004OneImage", "Sample.png"), asset);
        string output = Path.Combine(directory, "Assets.ccproj");
        RecotteProjectDocument document = RecotteProject.Create(new("Assets", directory));
        using (ProjectEditSession session = document.BeginEdit(fileItemKeyAllocator: new FixedFileKeyAllocator(ImageKey)))
        {
            Assert.True(session.Editor.AddImageAsset(new(2, asset, output, "picture", new(0m), new(2m))).Success);
            Assert.True(session.Commit().Success);
        }
        document.SaveCopy(output);
        if (extraFileItems.Length == 0) return output;
        JsonObject root = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
        foreach (JsonObject item in extraFileItems) root["file-items"]!.AsArray().Add(item);
        File.WriteAllText(output, root.ToJsonString());
        return output;
    }

    private sealed class FixedFileKeyAllocator(string key) : IFileItemKeyAllocator
    {
        public string Allocate(IReadOnlyCollection<string> existingKeys) => key;
    }
}
