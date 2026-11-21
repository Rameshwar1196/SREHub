namespace Aras.SreHub.Models;

public sealed class SubTask
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Done { get; set; }
}

public sealed class IterationTarget
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = "P2";
    public string Status { get; set; } = "Todo";
    public double? PlannedDays { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string CompletedOn { get; set; } = string.Empty;
    public string CarriedFrom { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalState { get; set; } = string.Empty;
    public string ExternalIteration { get; set; } = string.Empty;
    public List<SubTask> SubTasks { get; set; } = [];

    // A target counts toward the iteration once it has days planned; Carried, Cancelled and ACF items never count.
    public bool CountsAsTarget => PlannedDays is > 0 && Status is not ("Carried" or "Cancelled" or "ACF");

    public int Percent =>
        Status == "Done" ? 100
        : SubTasks.Count == 0 ? 0
        : (int)Math.Round(100.0 * SubTasks.Count(subTask => subTask.Done) / SubTasks.Count);
}

public sealed class IterationPlan
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public List<IterationTarget> Targets { get; set; } = [];

    public int Progress
    {
        get
        {
            var counted = Targets.Where(target => target.CountsAsTarget).ToList();
            var weight = counted.Sum(Weight);
            return weight == 0 ? 0 : (int)Math.Round(counted.Sum(target => target.Percent * Weight(target)) / weight);
        }
    }

    private static double Weight(IterationTarget target) => target.PlannedDays ?? 0;
}

public sealed class DailyPlanDay
{
    public string Date { get; set; } = string.Empty;
    public string Focus { get; set; } = string.Empty;
    public string EndOfDay { get; set; } = string.Empty;
    public string Blockers { get; set; } = string.Empty;
    public string Tomorrow { get; set; } = string.Empty;

    public bool IsEmpty => Focus.Length == 0 && EndOfDay.Length == 0 && Blockers.Length == 0 && Tomorrow.Length == 0;
}

public sealed class DailyPlanSave
{
    public DailyPlanDay Day { get; set; } = new();
    public List<IterationTarget> Targets { get; set; } = [];
    public List<string> RemovedExternalIds { get; set; } = [];
}

public sealed class DailyPlanView
{
    public DailyPlanDay Day { get; set; } = new();
    public IterationPlan Iteration { get; set; } = new();
    public int CarryOverCount { get; set; }
}

public sealed class IterationStartRequest
{
    public string Start { get; set; } = string.Empty;
}

public sealed class DailyPlanStore
{
    public List<DailyPlanDay> Days { get; set; } = [];
    public List<IterationPlan> Iterations { get; set; } = [];
    public List<string> DismissedExternalIds { get; set; } = [];
    public int SyncSchema { get; set; }
}
