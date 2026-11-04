namespace Aras.SreHub.Models;

public sealed class WorkLogEntry
{
    public string Date { get; set; } = string.Empty;
    public string Attendance { get; set; } = string.Empty;
    public string EmergencyLeave { get; set; } = string.Empty;
    public string NetworkPower { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Escalation { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    public bool IsEmpty =>
        Attendance.Length == 0 && EmergencyLeave.Length == 0 && NetworkPower.Length == 0
        && Summary.Length == 0 && Escalation.Length == 0 && Note.Length == 0;
}

public sealed class WorkLogSummaryRow
{
    public string Category { get; set; } = string.Empty;
    public int Days { get; set; }
    public double Percentage { get; set; }
}

public sealed class WorkLogMonth
{
    public string Month { get; set; } = string.Empty;
    public IReadOnlyList<WorkLogEntry> Entries { get; set; } = [];
    public IReadOnlyList<WorkLogSummaryRow> Summary { get; set; } = [];
}

public sealed class WorkLogYear
{
    public int Year { get; set; }
    public IReadOnlyList<WorkLogEntry> Entries { get; set; } = [];
    public IReadOnlyList<WorkLogSummaryRow> Summary { get; set; } = [];
    public IReadOnlyList<WorkLogMonth> Months { get; set; } = [];
}
