using System.Text.RegularExpressions;

namespace RecotteStudio.Core;

// Rewrite structured locations only; user text and before/after values are preserved. A layer insertion or move gives
// every layer a new index, described by a map from the old index to the new one.
internal static class LayerInsertionMetadata
{
    internal static int Index(int index, int inserted) => index >= inserted ? index + 1 : index;

    internal static Func<int, int> Insertion(int inserted) => index => Index(index, inserted);

    // The moved layer lands at to; the layers it passed close the gap it left or open the one it fills.
    internal static Func<int, int> Move(int from, int to) => index =>
        index == from ? to
        : from < to && index > from && index <= to ? index - 1
        : to < from && index >= to && index < from ? index + 1
        : index;

    internal static string? Target(string? target, int inserted) => Target(target, Insertion(inserted));

    internal static string? Target(string? target, Func<int, int> map)
    {
        if (target is null) return null;
        return Regex.Replace(target,
            @"^(layer:)(\d+)$|^(TimelineObjectId \{ LayerIndex = )(\d+)(, ObjectKey = -?\d+ \})$",
            match => match.Groups[1].Success
                ? match.Groups[1].Value + map(int.Parse(match.Groups[2].Value))
                : match.Groups[3].Value + map(int.Parse(match.Groups[4].Value)) + match.Groups[5].Value);
    }

    private static string? Path(string? path, Func<int, int> map) => path is null ? null :
        Regex.Replace(path, @"^\$\.layers\[(\d+)\]",
            match => $"$.layers[{map(int.Parse(match.Groups[1].Value))}]");

    internal static ProjectChange Change(ProjectChange change, int inserted) => Change(change, Insertion(inserted));

    internal static ProjectChange Change(ProjectChange change, Func<int, int> map) => change with
    {
        Target = Target(change.Target, map)!,
        JsonPath = Path(change.JsonPath, map)!,
    };

    internal static ProjectDiagnostic Diagnostic(ProjectDiagnostic diagnostic, int inserted) => Diagnostic(diagnostic, Insertion(inserted));

    internal static ProjectDiagnostic Diagnostic(ProjectDiagnostic diagnostic, Func<int, int> map) => diagnostic with
    {
        JsonPath = Path(diagnostic.JsonPath, map),
        Message = diagnostic.Code == "RC4208"
            ? Regex.Replace(diagnostic.Message, @"(?<=on layer )\d+(?= was changed\.)",
                match => map(int.Parse(match.Value)).ToString())
            : diagnostic.Message,
    };
}
