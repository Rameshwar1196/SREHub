namespace Aras.SreHub.Models;

public sealed class DutyEntry
{
    public string Date { get; set; } = string.Empty;
    public string DoShift { get; set; } = string.Empty;
    public string ShiftTime { get; set; } = string.Empty;
    public string Handover { get; set; } = string.Empty;
    public string DoNote { get; set; } = string.Empty;

    public bool IsEmpty => DoShift.Length == 0 && ShiftTime.Length == 0 && Handover.Length == 0 && DoNote.Length == 0;
}

public sealed class DutyYear
{
    public int Year { get; set; }
    public IReadOnlyList<DutyEntry> Entries { get; set; } = [];
}

public sealed class DutyRosterBlock
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Japan { get; set; } = string.Empty;
    public string India { get; set; } = string.Empty;
}

// Who updates the OneNote in an iteration, independent of the DO roster.
public sealed class OneNoteIteration
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Page { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed class DutySettings
{
    public string MyDoId { get; set; } = "DO3";
    public string MyItId { get; set; } = string.Empty;
    public Dictionary<string, string> Names { get; set; } = new() { ["DO3"] = "Rameshwar" };
}

public sealed class DutyRoster
{
    public DutySettings Settings { get; set; } = new();
    public List<DutyRosterBlock> Blocks { get; set; } = [];
    public List<OneNoteIteration> Iterations { get; set; } = [];
}
