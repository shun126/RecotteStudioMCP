namespace Recotte.McpServer;

public sealed record McpToolResult<T>(bool Success, string Category, string? ErrorCode, string? Message,
    T? Data, IReadOnlyList<McpDiagnosticDto> Diagnostics);
public sealed record McpDiagnosticDto(string Code, string Severity, string Message, string? JsonPath,
    int? OperationIndex = null, string? OperationType = null, string? Target = null);
public sealed record LayerTargetDto(int? LayerIndex = null, string? Name = null, string? Type = null);
public sealed record TimelineObjectTargetDto(int? LayerIndex = null, int? ObjectKey = null, string? LayerName = null);
public sealed record ProjectOperationDto(string Type, TimelineObjectTargetDto? Target = null, LayerTargetDto? Layer = null,
    string? Text = null, decimal? Start = null, decimal? End = null, string? AssetPath = null,
    string? ProjectPath = null, string? DisplayName = null);
public sealed record TextSequenceItemDto(string Text, decimal? Duration = null);
