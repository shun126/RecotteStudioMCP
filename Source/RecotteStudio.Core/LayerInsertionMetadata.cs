using System.Text.RegularExpressions;

namespace RecotteStudio.Core;

// Rewrite structured locations only; user text and before/after values are preserved.
internal static class LayerInsertionMetadata
{
    internal static int Index(int index, int inserted) => index >= inserted ? index + 1 : index;

    internal static string? Target(string? target, int inserted)
    {
        if (target is null) return null;
        return Regex.Replace(target,
            @"^(layer:)(\d+)$|^(TimelineObjectId \{ LayerIndex = )(\d+)(, ObjectKey = -?\d+ \})$",
            match => match.Groups[1].Success
                ? match.Groups[1].Value + Index(int.Parse(match.Groups[2].Value), inserted)
                : match.Groups[3].Value + Index(int.Parse(match.Groups[4].Value), inserted) + match.Groups[5].Value);
    }

    private static string? Path(string? path, int inserted) => path is null ? null :
        Regex.Replace(path, @"^\$\.layers\[(\d+)\]",
            match => $"$.layers[{Index(int.Parse(match.Groups[1].Value), inserted)}]");

    internal static ProjectChange Change(ProjectChange change, int inserted) => change with
    {
        Target = Target(change.Target, inserted)!,
        JsonPath = Path(change.JsonPath, inserted)!,
    };

    internal static ProjectDiagnostic Diagnostic(ProjectDiagnostic diagnostic, int inserted) => diagnostic with
    {
        JsonPath = Path(diagnostic.JsonPath, inserted),
        Message = diagnostic.Code == "RC4208"
            ? Regex.Replace(diagnostic.Message, @"(?<=on layer )\d+(?= was changed\.)",
                match => Index(int.Parse(match.Value), inserted).ToString())
            : diagnostic.Message,
    };
}
