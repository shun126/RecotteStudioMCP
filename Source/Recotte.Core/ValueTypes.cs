namespace Recotte.Core;

/// <summary>Represents a non-negative time in a Recotte Studio project.</summary>
public readonly record struct ProjectTime
{
    /// <summary>Creates a project time from decimal seconds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when seconds is negative.</exception>
    public ProjectTime(decimal totalSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalSeconds);
        TotalSeconds = totalSeconds;
    }

    /// <summary>Gets the total number of seconds.</summary>
    public decimal TotalSeconds { get; }
}

/// <summary>Identifies a timeline object within a loaded document.</summary>
public readonly record struct TimelineObjectId(int LayerIndex, int ObjectKey);
