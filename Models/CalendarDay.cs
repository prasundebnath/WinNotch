namespace WinNotch.Models;

/// <summary>
/// Represents a single cell in the mini calendar grid.
/// </summary>
public sealed record CalendarDay(
    /// <summary>Day-of-month label, e.g. "1"–"31", or "" for padding cells.</summary>
    string Label,
    /// <summary>True when this cell is today's date.</summary>
    bool IsToday,
    /// <summary>True when this cell belongs to the current month (false = prev/next month overflow).</summary>
    bool IsCurrentMonth);
