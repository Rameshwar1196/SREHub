namespace Aras.SreHub.Models;

// How a source's state names map onto targets; Prefix keeps each source's targets separate in the plan.
public interface IStateMapping
{
    string Prefix { get; }
    string[] Closed { get; }
    string[] Cancelled { get; }
    string[] Removed { get; }
    string[] Acf { get; }
    string[] Active { get; }
}

public sealed class SrePortalOptions : IStateMapping
{
    public const string SectionName = "SrePortal";

    private static readonly string[] DefaultActive = ["In Work"];
    private static readonly string[] DefaultAcf = ["Awaiting customer feedback"];

    public string BaseUrl { get; set; } = "https://myinnovator.com";
    public string Database { get; set; } = "MyInnovator";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ItemType { get; set; } = "Support Incident";
    public string AssigneeName { get; set; } = string.Empty;
    public string Classification { get; set; } = "Infrastructure";
    public string PortalUrl { get; set; } = "https://www.aras.com/arasinternalportal/";
    public int SyncMinutes { get; set; } = 30;
    public string[]? ClosedStates { get; set; }
    public string[]? ActiveStates { get; set; }
    public string[]? AcfStates { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && !string.IsNullOrWhiteSpace(AssigneeName);

    public string Prefix => "sre:";
    public string[] Closed => ClosedStates is { Length: > 0 } ? ClosedStates : ["Closed"];
    public string[] Cancelled => [];
    public string[] Removed => [];
    public string[] Acf => AcfStates is { Length: > 0 } ? AcfStates : DefaultAcf;
    public string[] Active => ActiveStates is { Length: > 0 } ? ActiveStates : DefaultActive;
}

public sealed class AzureDevOpsOptions : IStateMapping
{
    public const string SectionName = "AzureDevOps";

    private static readonly string[] DefaultClosed = ["Closed", "Done", "Resolved", "Completed"];
    private static readonly string[] DefaultCancelled = ["Cancelled", "Canceled"];
    private static readonly string[] DefaultRemoved = ["Removed"];
    private static readonly string[] DefaultActive = ["Active", "In Progress", "Committed", "Doing", "Implementing", "Ready for Implementing"];

    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;
    public int SyncMinutes { get; set; } = 30;
    public string[]? WorkItemTypes { get; set; }
    public string[]? ClosedStates { get; set; }
    public string[]? CancelledStates { get; set; }
    public string[]? RemovedStates { get; set; }
    public string[]? ActiveStates { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Organization)
        && !string.IsNullOrWhiteSpace(Project)
        && !string.IsNullOrWhiteSpace(PersonalAccessToken);

    public string Prefix => "ado:";
    public string[] Acf => [];
    public string[] Closed => ClosedStates is { Length: > 0 } ? ClosedStates : DefaultClosed;
    public string[] Cancelled => CancelledStates is { Length: > 0 } ? CancelledStates : DefaultCancelled;
    public string[] Removed => RemovedStates is { Length: > 0 } ? RemovedStates : DefaultRemoved;
    public string[] Active => ActiveStates is { Length: > 0 } ? ActiveStates : DefaultActive;
}

// ClosedOn is the day the item was closed or cancelled; IterationStart is the first day of the Azure DevOps iteration it belongs to.
public sealed record ExternalWorkItem(
    string Id, string Type, string Title, string State, int? Priority, DateTime? ClosedOn, string Url, DateTime? IterationStart = null, string IterationPath = "");

public sealed class WorkItemSyncResult
{
    public bool Configured { get; set; }
    public string RanAt { get; set; } = string.Empty;
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Skipped { get; set; }
    public string Error { get; set; } = string.Empty;
}

public sealed class DailyPlanSyncView
{
    public DailyPlanView View { get; set; } = new();
    public WorkItemSyncResult Sync { get; set; } = new();
}
