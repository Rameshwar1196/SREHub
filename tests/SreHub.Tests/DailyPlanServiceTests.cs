using Aras.SreHub.Models;
using Aras.SreHub.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Aras.SreHub.Tests;

public sealed class DailyPlanServiceTests : IDisposable
{
    // 2026-11-09 is a Monday, so the first iteration is 2026-11-09 to 2026-11-22.
    private const string Monday = "2026-11-09";
    private const string Friday = "2026-11-13";
    private const string NextIterationDay = "2026-11-24";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "srehub-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkLogService _workLog;
    private readonly DailyPlanService _service;

    public DailyPlanServiceTests()
    {
        Directory.CreateDirectory(_root);
        var environment = new TestEnvironment(_root);
        _workLog = new WorkLogService(environment);
        _service = new DailyPlanService(environment, _workLog);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static IterationTarget Target(string title, string status = "Todo", double? days = null, params (string Title, bool Done)[] subTasks) => new()
    {
        Title = title,
        Status = status,
        PlannedDays = days,
        SubTasks = subTasks.Select(subTask => new SubTask { Title = subTask.Title, Done = subTask.Done }).ToList()
    };

    private Task<DailyPlanView> Save(string date, params IterationTarget[] targets) =>
        _service.SaveAsync(date, new DailyPlanSave { Targets = [.. targets] }, CancellationToken.None);

    private Task<DailyPlanView> SaveDay(string date, DailyPlanDay day) =>
        _service.SaveAsync(date, new DailyPlanSave { Day = day }, CancellationToken.None);

    [Fact]
    public async Task Get_ForAnEmptyStoreReturnsAMondayToSundayFortnight()
    {
        var view = await _service.GetAsync(Friday, CancellationToken.None);

        Assert.Equal(Monday, view.Iteration.Start);
        Assert.Equal("2026-11-22", view.Iteration.End);
        Assert.Empty(view.Iteration.Targets);
        Assert.Equal(Friday, view.Day.Date);
    }

    [Fact]
    public async Task Save_TrimsTextAppliesDefaultsAndAssignsIds()
    {
        var view = await Save(Friday, new IterationTarget { Title = "  Infra update  ", Priority = "", Status = "" });

        var target = Assert.Single(view.Iteration.Targets);
        Assert.Equal("Infra update", target.Title);
        Assert.Equal("P2", target.Priority);
        Assert.Equal("Todo", target.Status);
        Assert.Equal(32, target.Id.Length);
    }

    [Fact]
    public async Task Save_DropsTargetsAndSubTasksWithoutTitle()
    {
        var view = await Save(Friday, Target("Real", subTasks: [("Build", false), ("  ", false)]), Target("   "));

        var target = Assert.Single(view.Iteration.Targets);
        Assert.Equal("Build", Assert.Single(target.SubTasks).Title);
    }

    [Fact]
    public async Task Save_SharesTargetsAcrossDaysOfTheSameIteration()
    {
        await Save(Monday, Target("Infra update"));

        var view = await _service.GetAsync("2026-11-20", CancellationToken.None);

        Assert.Equal("Infra update", Assert.Single(view.Iteration.Targets).Title);
    }

    [Fact]
    public async Task Save_KeepsTheDayNotesSeparatePerDay()
    {
        await SaveDay(Monday, new DailyPlanDay { EndOfDay = "- Monday work" });

        var other = await _service.GetAsync(Friday, CancellationToken.None);
        var same = await _service.GetAsync(Monday, CancellationToken.None);

        Assert.Equal(string.Empty, other.Day.EndOfDay);
        Assert.Equal("- Monday work", same.Day.EndOfDay);
    }

    [Fact]
    public async Task Save_EmptyDayAndNoTargetsRemovesStoredData()
    {
        await Save(Friday, Target("Task"));
        await SaveDay(Friday, new DailyPlanDay { Focus = "Focus" });

        var cleared = await _service.SaveAsync(Friday, new DailyPlanSave(), CancellationToken.None);

        Assert.True(cleared.Day.IsEmpty);
        Assert.Empty(cleared.Iteration.Targets);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(6)]
    [InlineData(1.5)]
    public async Task Save_RejectsPlannedDaysOutsideTheAllowedValues(double days)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, Target("Task", days: days)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Save_AcceptsAllowedPlannedDays(double days)
    {
        var view = await Save(Friday, Target("Task", days: days));

        Assert.Equal(days, view.Iteration.Targets[0].PlannedDays);
    }

    [Theory]
    [InlineData("Started")]
    [InlineData("done")]
    public async Task Save_RejectsUnknownStatus(string status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, Target("Task", status)));
    }

    [Fact]
    public async Task Save_RejectsUnknownPriority()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, new IterationTarget { Title = "Task", Priority = "P9" }));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a link")]
    public async Task Save_RejectsNonHttpLinks(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, new IterationTarget { Title = "Task", Url = url }));
    }

    [Fact]
    public async Task Save_RejectsInvalidDate()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Save("13-11-2026", Target("Task")));
    }

    [Fact]
    public async Task Save_RejectsMoreThanFiftyTargets()
    {
        var targets = Enumerable.Range(0, 51).Select(index => Target($"Task {index}")).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, targets));
    }

    [Fact]
    public async Task Save_RejectsMoreThanTwentySubTasks()
    {
        var subTasks = Enumerable.Range(0, 21).Select(index => ($"Step {index}", false)).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => Save(Friday, Target("Task", subTasks: subTasks)));
    }

    [Fact]
    public async Task Save_RejectsOverlongFocus()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SaveDay(Friday, new DailyPlanDay { Focus = new string('x', 201) }));
    }

    [Fact]
    public async Task Percent_IsTheShareOfDoneSubTasks()
    {
        var view = await Save(Friday, Target("Infra update", subTasks: [("Build", true), ("Deploy", false), ("Verify", false)]));

        Assert.Equal(33, view.Iteration.Targets[0].Percent);
        Assert.Equal("InProgress", view.Iteration.Targets[0].Status);
    }

    [Fact]
    public async Task Percent_IsZeroWithoutSubTasksUntilTheTargetIsDone()
    {
        var view = await Save(Friday, Target("Open"), Target("Finished", "Done"));

        Assert.Equal(0, view.Iteration.Targets[0].Percent);
        Assert.Equal(100, view.Iteration.Targets[1].Percent);
    }

    [Fact]
    public async Task Done_MarksEverySubTaskDoneAndStampsTheCompletionDate()
    {
        var view = await Save(Friday, Target("Infra update", "Done", subTasks: [("Build", false), ("Deploy", false)]));

        var target = view.Iteration.Targets[0];
        Assert.All(target.SubTasks, subTask => Assert.True(subTask.Done));
        Assert.Equal(100, target.Percent);
        Assert.Equal(Friday, target.CompletedOn);
    }

    [Fact]
    public async Task CompletingAllSubTasksMarksTheTargetDone()
    {
        var view = await Save(Friday, Target("Infra update", "InProgress", subTasks: [("Build", true), ("Deploy", true)]));

        Assert.Equal("Done", view.Iteration.Targets[0].Status);
        Assert.Equal(Friday, view.Iteration.Targets[0].CompletedOn);
    }

    [Fact]
    public async Task ReopeningATargetClearsItsCompletionDate()
    {
        var done = await Save(Friday, Target("Task", "Done"));
        var reopened = done.Iteration.Targets[0];
        reopened.Status = "InProgress";

        var view = await _service.SaveAsync(Friday, new DailyPlanSave { Targets = [reopened] }, CancellationToken.None);

        Assert.Equal(string.Empty, view.Iteration.Targets[0].CompletedOn);
    }

    [Fact]
    public async Task Progress_WeightsTargetsByPlannedDays()
    {
        var view = await Save(Friday, Target("Big", "Done", days: 3), Target("Small", "Todo", days: 1));

        Assert.Equal(75, view.Iteration.Progress);
    }

    [Fact]
    public async Task Progress_IgnoresTargetsWithoutPlannedDays()
    {
        var view = await Save(Friday, Target("A", "Done", days: 2), Target("B"), Target("C", "Done"));

        Assert.Equal(100, view.Iteration.Progress);
    }

    [Fact]
    public async Task Progress_IgnoresCarriedTargets()
    {
        var view = await Save(Friday, Target("A", "Done", days: 1), Target("B", "Carried", days: 1));

        Assert.Equal(100, view.Iteration.Progress);
    }

    [Fact]
    public async Task Acf_ClearsPlannedDaysAndIsNotATarget()
    {
        var view = await Save(Friday, Target("Waiting", "ACF", days: 3), Target("Real", "Done", days: 2));

        var acf = view.Iteration.Targets[0];
        Assert.Null(acf.PlannedDays);
        Assert.False(acf.CountsAsTarget);
        Assert.Equal(100, view.Iteration.Progress);
    }

    [Fact]
    public async Task Progress_IsZeroWithoutTargets()
    {
        var view = await _service.GetAsync(Friday, CancellationToken.None);

        Assert.Equal(0, view.Iteration.Progress);
    }

    [Fact]
    public async Task Get_StartsTheNextIterationFourteenDaysAfterTheStoredOne()
    {
        await Save(Monday, Target("Task"));

        var view = await _service.GetAsync(NextIterationDay, CancellationToken.None);

        Assert.Equal("2026-11-23", view.Iteration.Start);
        Assert.Equal("2026-12-06", view.Iteration.End);
    }

    [Fact]
    public async Task SetIterationStart_MovesTheIterationWindow()
    {
        var view = await _service.SetIterationStartAsync(Friday, "2026-11-11", CancellationToken.None);

        Assert.Equal("2026-11-11", view.Iteration.Start);
        Assert.Equal("2026-11-24", view.Iteration.End);
    }

    [Fact]
    public async Task SetIterationStart_IsKeptWhenLaterSavesHaveNoTargets()
    {
        await _service.SetIterationStartAsync(Friday, "2026-11-11", CancellationToken.None);

        await SaveDay(Friday, new DailyPlanDay { Focus = "Focus" });
        var view = await _service.GetAsync(Friday, CancellationToken.None);

        Assert.Equal("2026-11-11", view.Iteration.Start);
    }

    [Fact]
    public async Task SetIterationStart_KeepsExistingTargets()
    {
        await Save(Friday, Target("Infra update"));

        var view = await _service.SetIterationStartAsync(Friday, "2026-11-11", CancellationToken.None);

        Assert.Equal("Infra update", Assert.Single(view.Iteration.Targets).Title);
    }

    [Theory]
    [InlineData("2026-11-14")]
    [InlineData("2026-10-20")]
    public async Task SetIterationStart_RejectsAWindowThatExcludesTheViewedDay(string start)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetIterationStartAsync(Friday, start, CancellationToken.None));
    }

    [Fact]
    public async Task SetIterationStart_RejectsOverlapWithAnotherIteration()
    {
        await Save(Monday, Target("First"));
        await Save(NextIterationDay, Target("Second"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetIterationStartAsync(NextIterationDay, "2026-11-15", CancellationToken.None));
    }

    [Fact]
    public async Task Get_BeforeTheEarliestStoredIterationStepsBackwardsFromIt()
    {
        await _service.SetIterationStartAsync(Friday, "2026-11-11", CancellationToken.None);

        var view = await _service.GetAsync("2026-11-10", CancellationToken.None);

        Assert.Equal("2026-10-28", view.Iteration.Start);
        Assert.Equal("2026-11-10", view.Iteration.End);
    }

    [Fact]
    public async Task Get_ShortensAWindowThatWouldRunIntoALaterStoredIteration()
    {
        await Save(Monday, Target("First"));
        await _service.SetIterationStartAsync("2026-11-25", "2026-11-25", CancellationToken.None);

        var view = await _service.GetAsync("2026-11-23", CancellationToken.None);

        Assert.Equal("2026-11-23", view.Iteration.Start);
        Assert.Equal("2026-11-24", view.Iteration.End);
    }

    [Fact]
    public async Task Get_CountsOnlyInProgressBlockedAndAcfTargetsFromTheEarlierIteration()
    {
        await Save(Monday, Target("Open"), Target("Working", "InProgress"), Target("Stuck", "Blocked"), Target("Waiting", "ACF"),
            Target("Finished", "Done"), Target("Dropped", "Cancelled"));

        var view = await _service.GetAsync(NextIterationDay, CancellationToken.None);

        Assert.Equal(3, view.CarryOverCount);
    }

    [Fact]
    public async Task CarryOver_MovesOnlyInProgressBlockedAndAcf_AndLeavesCompletedWorkWhereItWasFinished()
    {
        await Save(Monday, Target("Working", "InProgress", 2), Target("Stuck", "Blocked", 1), Target("Waiting", "ACF"),
            Target("Not started", "Todo", 1), Target("Finished", "Done", 2), Target("Dropped", "Cancelled", 1));

        var view = await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        Assert.Equal(["Working", "Stuck", "Waiting"], view.Iteration.Targets.Select(target => target.Title));
        Assert.Equal(["InProgress", "Blocked", "ACF"], view.Iteration.Targets.Select(target => target.Status));

        var previous = (await _service.GetAsync(Monday, CancellationToken.None)).Iteration;
        Assert.Equal("Done", previous.Targets.Single(target => target.Title == "Finished").Status);
        Assert.Equal("Todo", previous.Targets.Single(target => target.Title == "Not started").Status);
        Assert.Equal("Cancelled", previous.Targets.Single(target => target.Title == "Dropped").Status);
        Assert.All(new[] { "Working", "Stuck", "Waiting" }, title => Assert.Equal("Carried", previous.Targets.Single(target => target.Title == title).Status));
    }

    [Fact]
    public async Task Sync_NeverMovesACompletedTargetToAnotherIteration()
    {
        await Sync([WorkItem("1", "Closed", closedOn: new DateTime(2026, 11, 12), iterationStart: new DateTime(2026, 11, 9))]);

        await Sync([WorkItem("1", "Closed", closedOn: new DateTime(2026, 11, 12), iterationStart: new DateTime(2026, 11, 23))]);

        Assert.Single((await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets);
        Assert.Empty((await _service.GetAsync(NextIterationDay, CancellationToken.None)).Iteration.Targets);
    }

    [Fact]
    public async Task CarryOver_CopiesInProgressTargetsWithSubTasksAndMarksOriginalsCarried()
    {
        await Save(Monday, Target("Infra update", "InProgress", 3, ("Build", true), ("Deploy", false)), Target("Finished", "Done"));

        var view = await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        var carried = Assert.Single(view.Iteration.Targets);
        Assert.Equal("Infra update", carried.Title);
        Assert.Equal(3, carried.PlannedDays);
        Assert.Equal("2026-11-09", carried.CarriedFrom);
        Assert.Equal(2, carried.SubTasks.Count);
        Assert.True(carried.SubTasks[0].Done);

        var previous = (await _service.GetAsync(Monday, CancellationToken.None)).Iteration;
        Assert.Contains(previous.Targets, target => target.Title == "Infra update" && target.Status == "Carried");
        Assert.Contains(previous.Targets, target => target.Title == "Finished" && target.Status == "Done");
    }

    [Fact]
    public async Task CarryOver_CalledTwiceDoesNotDuplicateTargets()
    {
        await Save(Monday, Target("Open", "InProgress"));

        await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);
        var second = await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        Assert.Single(second.Iteration.Targets);
        Assert.Equal(0, second.CarryOverCount);
    }

    [Fact]
    public async Task CarryOver_KeepsTheOriginalIterationAcrossSeveralIterations()
    {
        await Save(Monday, Target("Long running", "InProgress"));
        await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        var view = await _service.CarryOverAsync("2026-12-08", CancellationToken.None);

        Assert.Equal("2026-11-09", Assert.Single(view.Iteration.Targets).CarriedFrom);
    }

    [Fact]
    public async Task CarryOver_IgnoresIterationsOlderThanTheWindow()
    {
        await Save("2026-08-03", Target("Ancient", "InProgress"));

        var view = await _service.CarryOverAsync(Friday, CancellationToken.None);

        Assert.Empty(view.Iteration.Targets);
    }

    [Fact]
    public async Task CarryOver_WithNothingToCarryStoresNothing()
    {
        await _service.CarryOverAsync(Friday, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_root, "App_Data", "dailyplan.json"))
            && (await File.ReadAllTextAsync(Path.Combine(_root, "App_Data", "dailyplan.json"))).Contains("\"start\""));
    }

    [Fact]
    public async Task ExportCsv_WritesATargetRowAndOneRowPerSubTask()
    {
        await Save(Friday, Target("Infra update", "InProgress", 2, ("Build", true), ("Deploy", false)));

        var csv = await _service.ExportCsvAsync(Friday, CancellationToken.None);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Iteration Start,Priority,Target", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains("\"50\"", lines[1]);
        Assert.Contains("\"Build\",\"Yes\"", lines[2]);
        Assert.Contains("\"Deploy\",\"No\"", lines[3]);
    }

    [Fact]
    public async Task ExportCsv_NeutralisesSpreadsheetFormulas()
    {
        await Save(Friday, Target("=HYPERLINK(\"http://evil\")"));

        var csv = await _service.ExportCsvAsync(Friday, CancellationToken.None);

        Assert.Contains("\"'=HYPERLINK(", csv);
    }

    [Fact]
    public async Task PushToWorkLog_JoinsTheAchievedLinesAndDefaultsTheRestOfTheEntry()
    {
        await SaveDay(Friday, new DailyPlanDay { EndOfDay = "- Fix alert\n* Tune SLO\n\nDeployed" });

        var entry = await _service.PushToWorkLogAsync(Friday, CancellationToken.None);

        Assert.Equal("Fix alert; Tune SLO; Deployed", entry.Summary);
        Assert.Equal("Yes", entry.Attendance);
        Assert.Equal("No", entry.EmergencyLeave);
        Assert.Equal("No", entry.NetworkPower);
    }

    [Fact]
    public async Task PushToWorkLog_KeepsExistingAttendanceAndEscalation()
    {
        await _workLog.SaveAsync(Friday, new WorkLogEntry
        {
            Date = Friday, Attendance = "DO-wk", EmergencyLeave = "No", NetworkPower = "Power outage",
            Summary = "old", Escalation = "Minor", Note = "keep"
        }, CancellationToken.None);
        await SaveDay(Friday, new DailyPlanDay { EndOfDay = "- Fix alert" });

        var entry = await _service.PushToWorkLogAsync(Friday, CancellationToken.None);

        Assert.Equal("DO-wk", entry.Attendance);
        Assert.Equal("Power outage", entry.NetworkPower);
        Assert.Equal("Minor", entry.Escalation);
        Assert.Equal("keep", entry.Note);
        Assert.Equal("Fix alert", entry.Summary);
    }

    [Fact]
    public async Task PushToWorkLog_TruncatesSummaryToTheWorkLogLimit()
    {
        var lines = Enumerable.Range(0, 8).Select(index => new string((char)('a' + index), 100));
        await SaveDay(Friday, new DailyPlanDay { EndOfDay = string.Join('\n', lines) });

        var entry = await _service.PushToWorkLogAsync(Friday, CancellationToken.None);

        Assert.Equal(500, entry.Summary.Length);
        Assert.EndsWith("...", entry.Summary);
    }

    [Fact]
    public async Task PushToWorkLog_IncludesTargetsCompletedOnThatDayWithoutDuplicatingNotes()
    {
        await _service.SaveAsync(Friday, new DailyPlanSave
        {
            Day = new DailyPlanDay { EndOfDay = "- infra update\n- Fix alert" },
            Targets = [Target("Infra update", "Done"), Target("SDE request", "Done"), Target("Still open", "InProgress")]
        }, CancellationToken.None);

        var entry = await _service.PushToWorkLogAsync(Friday, CancellationToken.None);

        Assert.Equal("infra update; Fix alert; SDE request", entry.Summary);
    }

    [Fact]
    public async Task PushToWorkLog_WithNothingAchievedThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PushToWorkLogAsync(Friday, CancellationToken.None));
    }

    private static readonly AzureDevOpsOptions SyncOptions = new() { Organization = "ArasCorp", Project = "Managed Services", PersonalAccessToken = "x" };
    private static readonly DateTime SyncToday = new(2026, 11, 13);

    private static ExternalWorkItem WorkItem(string id, string state, int? priority = 2, DateTime? closedOn = null, string title = "Fix alert", DateTime? iterationStart = null, string iterationPath = "") =>
        new(id, "Task", title, state, priority, closedOn, $"https://dev.azure.com/ArasCorp/Managed%20Services/_workitems/edit/{id}", iterationStart, iterationPath);

    [Fact]
    public async Task Sync_RecordsTheWorkItemIterationPathAndKeepsItCurrent()
    {
        await Sync([WorkItem("1", "Active", iterationPath: "Managed Services\\2026 Q4\\03-Q4-2026")]);
        await Sync([WorkItem("1", "Active", iterationPath: "Managed Services\\2026 Q4\\04-Q4-2026")]);

        var target = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single();
        Assert.Equal("Managed Services\\2026 Q4\\04-Q4-2026", target.ExternalIteration);
    }

    [Fact]
    public async Task Sync_MapsImplementingStatesToInProgressAndCancelledToCancelled()
    {
        await Sync([
            WorkItem("1", "Implementing"), WorkItem("2", "Ready for Implementing"),
            WorkItem("3", "Cancelled", closedOn: new DateTime(2026, 11, 12)), WorkItem("4", "Closed", closedOn: new DateTime(2026, 11, 12))]);

        var targets = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        Assert.Equal(["InProgress", "InProgress", "Cancelled", "Done"], targets.Select(target => target.Status));
    }

    [Fact]
    public async Task CancelledTargets_AreIgnoredByProgressAndCarryOver()
    {
        await Sync([WorkItem("1", "Cancelled", closedOn: new DateTime(2026, 11, 12)), WorkItem("2", "Closed", closedOn: new DateTime(2026, 11, 12))]);
        var synced = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        synced.ForEach(target => target.PlannedDays = 2);
        await _service.SaveAsync(Friday, new DailyPlanSave { Targets = synced }, CancellationToken.None);

        var view = await _service.GetAsync(Friday, CancellationToken.None);
        var next = await _service.GetAsync(NextIterationDay, CancellationToken.None);

        Assert.Equal(100, view.Iteration.Progress);
        Assert.Equal(0, next.CarryOverCount);
    }

    [Fact]
    public async Task Sync_PlacesItemsInTheIterationOfTheirWorkItemAndMovesThemWhenItChanges()
    {
        await Save(Friday, Target("Existing"));
        await Sync([WorkItem("1", "Active", iterationStart: new DateTime(2026, 10, 26)), WorkItem("2", "Active")]);

        Assert.Equal(["#1 Fix alert"], (await _service.GetAsync("2026-10-30", CancellationToken.None)).Iteration.Targets.Select(target => target.Title));
        Assert.Equal(["Existing", "#2 Fix alert"], (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Title));

        await Sync([WorkItem("1", "Active", iterationStart: new DateTime(2026, 11, 9)), WorkItem("2", "Active", iterationStart: new DateTime(2026, 10, 26))]);

        Assert.Equal(["#2 Fix alert"], (await _service.GetAsync("2026-10-30", CancellationToken.None)).Iteration.Targets.Select(target => target.Title));
        Assert.Equal(["Existing", "#1 Fix alert"], (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Title));
    }

    [Fact]
    public async Task Sync_ReReadsTargetsImportedBeforeCancelledExisted()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App_Data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "App_Data", "dailyplan.json"), """
            { "iterations": [ { "start": "2026-11-09", "end": "2026-11-22", "targets": [
              { "id": "a1", "title": "#7 Old", "status": "Todo", "externalId": "ado:7", "externalState": "Cancelled" },
              { "id": "a2", "title": "#8 Old", "status": "Todo", "externalId": "ado:8", "externalState": "Implementing" } ] } ] }
            """);

        await Sync([WorkItem("8", "Implementing")], [WorkItem("7", "Cancelled", closedOn: new DateTime(2026, 11, 5))]);

        var targets = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        Assert.Equal(["Cancelled", "InProgress"], targets.Select(target => target.Status));
    }

    private Task<WorkItemSyncResult> Sync(IReadOnlyList<ExternalWorkItem> assigned, IReadOnlyList<ExternalWorkItem>? tracked = null) =>
        _service.ApplyWorkItemsAsync(assigned, tracked ?? [], SyncToday, SyncOptions, CancellationToken.None);

    [Fact]
    public async Task Sync_AddsAssignedWorkItemsAsTargetsOnce()
    {
        var first = await Sync([WorkItem("101", "Active", priority: 1), WorkItem("102", "New", priority: 4)]);
        var second = await Sync([WorkItem("101", "Active", priority: 1), WorkItem("102", "New", priority: 4)]);

        var targets = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(["#101 Fix alert", "#102 Fix alert"], targets.Select(target => target.Title));
        Assert.Equal(["P1", "P3"], targets.Select(target => target.Priority));
        Assert.Equal(["InProgress", "Todo"], targets.Select(target => target.Status));
        Assert.Contains("_workitems/edit/101", targets[0].Url);
    }

    [Fact]
    public async Task Sync_MarksTheTargetDoneWithTheClosedDateWhenTheWorkItemCloses()
    {
        await Sync([WorkItem("101", "Active")]);

        var result = await Sync([WorkItem("101", "Closed", closedOn: new DateTime(2026, 11, 12))]);

        var target = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single();
        Assert.Equal(1, result.Updated);
        Assert.Equal("Done", target.Status);
        Assert.Equal("2026-11-12", target.CompletedOn);
    }

    [Fact]
    public async Task Sync_PutsAnAlreadyClosedItemInTheIterationOfItsClosedDate()
    {
        await Sync([WorkItem("100", "Active"), WorkItem("101", "Closed", closedOn: new DateTime(2026, 11, 3))]);

        var view = await _service.GetAsync("2026-11-03", CancellationToken.None);
        Assert.Equal("Done", view.Iteration.Targets.Single().Status);
        Assert.Equal("#100 Fix alert", (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single().Title);
    }

    [Fact]
    public async Task Sync_KeepsManualStatusUntilTheWorkItemStateChanges()
    {
        await Sync([WorkItem("101", "Active")]);
        var view = await _service.GetAsync(Friday, CancellationToken.None);
        view.Iteration.Targets[0].Status = "Blocked";
        await _service.SaveAsync(Friday, new DailyPlanSave { Targets = view.Iteration.Targets }, CancellationToken.None);

        await Sync([WorkItem("101", "Active")]);

        Assert.Equal("Blocked", (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single().Status);
    }

    [Fact]
    public async Task Sync_DoesNotBringBackATargetTheUserRemoved()
    {
        await Sync([WorkItem("101", "Active")]);
        await _service.SaveAsync(Friday, new DailyPlanSave { RemovedExternalIds = ["ado:101"] }, CancellationToken.None);

        var result = await Sync([WorkItem("101", "Active")]);

        Assert.Equal(0, result.Added);
        Assert.Empty((await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets);
    }

    [Fact]
    public async Task Save_FromAnOlderPageKeepsTargetsSyncedSinceItLoaded()
    {
        await Save(Friday, Target("Manual target"));
        var stale = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        await Sync([WorkItem("101", "Active")]);

        await _service.SaveAsync(Friday, new DailyPlanSave { Targets = stale }, CancellationToken.None);

        var titles = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Title);
        Assert.Equal(["Manual target", "#101 Fix alert"], titles);
    }

    [Fact]
    public async Task Sync_DropsUntouchedTargetsNoLongerAssignedButKeepsWorkedOnes()
    {
        await Sync([WorkItem("101", "New"), WorkItem("102", "New")]);
        var view = await _service.GetAsync(Friday, CancellationToken.None);
        view.Iteration.Targets[1].SubTasks = [new SubTask { Title = "Started", Done = false }];
        await _service.SaveAsync(Friday, new DailyPlanSave { Targets = view.Iteration.Targets }, CancellationToken.None);

        var result = await Sync([], [WorkItem("101", "New"), WorkItem("102", "New")]);

        Assert.Equal(1, result.Removed);
        Assert.Equal(["#102 Fix alert"], (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Title));
    }

    [Fact]
    public void BuildQuery_FiltersToMyOpenItemsAndRecentlyClosedOnesAndEscapesQuotes()
    {
        var options = new AzureDevOpsOptions { Project = "O'Neil", WorkItemTypes = ["Task", "Bug"] };

        var query = AzureDevOpsWorkItemSource.BuildQuery(options, new DateTime(2026, 11, 1));

        Assert.Contains("[System.TeamProject] = 'O''Neil'", query);
        Assert.Contains("[System.AssignedTo] = @Me", query);
        Assert.Contains("[System.WorkItemType] IN ('Task', 'Bug')", query);
        Assert.Contains("[System.ChangedDate] >= '2026-11-01'", query);
        Assert.Contains("[System.State] IN ('Closed', 'Done', 'Resolved', 'Completed', 'Cancelled', 'Canceled')", query);
    }

    [Fact]
    public async Task Sync_ImportsFromTheIterationStartAndIsDueOnlyForTheFirstAndLastDay()
    {
        await Save(Friday, Target("Existing"));
        var source = new FakeWorkItemSource();
        var clock = new FakeClock(new DateTime(2026, 11, 13));
        var sync = new WorkItemSyncService(_service, source, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance);

        Assert.True(await sync.IsDueAsync(CancellationToken.None));
        await sync.SyncAsync(CancellationToken.None);

        Assert.Equal(new DateTime(2026, 11, 9), source.ClosedSince);
        Assert.False(await sync.IsDueAsync(CancellationToken.None));

        clock.Now = new DateTime(2026, 11, 20);
        Assert.False(await sync.IsDueAsync(CancellationToken.None));

        clock.Now = new DateTime(2026, 11, 22);
        Assert.True(await sync.IsDueAsync(CancellationToken.None));
        await sync.SyncAsync(CancellationToken.None);
        Assert.False(await sync.IsDueAsync(CancellationToken.None));

        clock.Now = new DateTime(2026, 11, 23);
        Assert.True(await sync.IsDueAsync(CancellationToken.None));
    }

    private sealed class FakeClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeWorkItemSource(IStateMapping? states = null, IReadOnlyList<ExternalWorkItem>? items = null) : IWorkItemSource
    {
        public DateTime? ClosedSince { get; private set; }

        public string Name => "Fake";

        public bool IsConfigured => true;

        public int SyncMinutes => 30;

        public string SetupHint => string.Empty;

        public IStateMapping States { get; } = states ?? SyncOptions;

        public Task<IReadOnlyList<ExternalWorkItem>> GetAssignedAsync(DateTime closedSince, CancellationToken cancellationToken)
        {
            ClosedSince = closedSince;
            return Task.FromResult(items ?? []);
        }

        public Task<IReadOnlyList<ExternalWorkItem>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalWorkItem>>([]);
    }

    private static readonly SrePortalOptions SreOptions = new() { Username = "u", Password = "p", AssigneeName = "Rameswar Suryawanshi" };

    private static ExternalWorkItem Incident(string number, string state, DateTime? closedOn = null) =>
        new(number, "Support Incident", "Tokamak UAT deployment", state, null, closedOn, "https://myinnovator.com/?StartItem=x");

    [Fact]
    public async Task Sync_MapsSupportIncidentStatesAndKeepsThemApartFromAzureDevOpsItems()
    {
        await Sync([WorkItem("101", "Active")]);
        var clock = new FakeClock(new DateTime(2026, 11, 13));
        var source = new FakeWorkItemSource(SreOptions, [
            Incident("SI-1", "In Review"), Incident("SI-2", "In Work"), Incident("SI-3", "Awaiting customer feedback"),
            Incident("SI-4", "Closed", new DateTime(2026, 11, 12))]);
        var sync = new WorkItemSyncService(_service, source, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance);

        var result = await sync.SyncAsync(CancellationToken.None);
        await sync.SyncAsync(CancellationToken.None);

        var targets = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        Assert.Equal(4, result.Added);
        Assert.Equal(["#101 Fix alert", "SI-1 Tokamak UAT deployment", "SI-2 Tokamak UAT deployment", "SI-3 Tokamak UAT deployment", "SI-4 Tokamak UAT deployment"], targets.Select(target => target.Title));
        Assert.Equal(["InProgress", "Todo", "InProgress", "ACF", "Done"], targets.Select(target => target.Status));
        Assert.Equal("2026-11-12", targets[4].CompletedOn);
        Assert.Equal("sre:SI-2", targets[2].ExternalId);
    }

    [Fact]
    public async Task Sync_RecheckesImportedSupportIncidentsOnceSoAwaitingFeedbackBecomesAcfWithoutDays()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App_Data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "App_Data", "dailyplan.json"), """
            { "syncSchema": 2, "iterations": [ { "start": "2026-11-09", "end": "2026-11-22", "targets": [
              { "id": "a1", "title": "SI-9 Karcher", "status": "InProgress", "plannedDays": 2, "externalId": "sre:SI-9", "externalState": "Awaiting Customer Feedback" },
              { "id": "a2", "title": "SI-8 Other", "status": "InProgress", "plannedDays": 1, "externalId": "sre:SI-8", "externalState": "In Work" } ] } ] }
            """);
        var clock = new FakeClock(new DateTime(2026, 11, 13));
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance;
        var source = new FakeWorkItemSource(SreOptions, [Incident("SI-9", "Awaiting Customer Feedback"), Incident("SI-8", "In Work")]);

        await new WorkItemSyncService(_service, source, clock, logger).SyncAsync(CancellationToken.None);
        await new WorkItemSyncService(_service, source, clock, logger).SyncAsync(CancellationToken.None);

        var targets = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets;
        Assert.Equal("ACF", targets[0].Status);
        Assert.Null(targets[0].PlannedDays);
        Assert.Equal("InProgress", targets[1].Status);
        Assert.Equal(1, targets[1].PlannedDays);
    }

    [Fact]
    public async Task Sync_MovesAnAcfIncidentBackToInProgressWhenItLeavesAwaitingFeedback()
    {
        var clock = new FakeClock(new DateTime(2026, 11, 13));
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance;
        await new WorkItemSyncService(_service, new FakeWorkItemSource(SreOptions, [Incident("SI-9", "Awaiting customer feedback")]), clock, logger).SyncAsync(CancellationToken.None);
        Assert.Equal("ACF", (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single().Status);

        await new WorkItemSyncService(_service, new FakeWorkItemSource(SreOptions, [Incident("SI-9", "In Work")]), clock, logger).SyncAsync(CancellationToken.None);

        Assert.Equal("InProgress", (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Single().Status);
    }

    [Fact]
    public async Task Sync_DoesNotDropAzureDevOpsTargetsWhenAnotherSourceSyncs()
    {
        await Sync([WorkItem("101", "New")]);
        var sync = new WorkItemSyncService(_service, new FakeWorkItemSource(SreOptions), new FakeClock(new DateTime(2026, 11, 13)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance);

        await sync.SyncAsync(CancellationToken.None);

        Assert.Equal(["#101 Fix alert"], (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Title));
    }

    [Fact]
    public async Task Sync_UpdatesLinksItBuiltEarlierButNotOnesTypedByHand()
    {
        ExternalWorkItem WithUrl(string number, string url) => new(number, "Support Incident", "Title", "In Work", null, null, url);
        var clock = new FakeClock(new DateTime(2026, 11, 13));
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkItemSyncService>.Instance;
        await new WorkItemSyncService(_service, new FakeWorkItemSource(SreOptions, [WithUrl("SI-1", "https://myinnovator.com/?StartItem=Support%20Incident:A"), WithUrl("SI-2", "https://myinnovator.com/?StartItem=Support%20Incident:B")]), clock, logger)
            .SyncAsync(CancellationToken.None);
        var view = await _service.GetAsync(Friday, CancellationToken.None);
        view.Iteration.Targets[1].Url = "https://example.com/my-own-link";
        await _service.SaveAsync(Friday, new DailyPlanSave { Targets = view.Iteration.Targets }, CancellationToken.None);

        await new WorkItemSyncService(_service, new FakeWorkItemSource(SreOptions, [WithUrl("SI-1", "https://www.aras.com/arasinternalportal/?StartItem=Support%20Incident:A"), WithUrl("SI-2", "https://www.aras.com/arasinternalportal/?StartItem=Support%20Incident:B")]), clock, logger)
            .SyncAsync(CancellationToken.None);

        var urls = (await _service.GetAsync(Friday, CancellationToken.None)).Iteration.Targets.Select(target => target.Url);
        Assert.Equal(["https://www.aras.com/arasinternalportal/?StartItem=Support%20Incident:A", "https://example.com/my-own-link"], urls);
    }

    [Fact]
    public void SrePortalFilter_UsesTheAssigneeClassificationAndRecentClosedIncidents()
    {
        var filter = SrePortalWorkItemSource.BuildAssignedFilter(SreOptions, new DateTime(2026, 11, 9));

        Assert.Contains("assignee/keyed_name eq 'Rameswar Suryawanshi'", filter);
        Assert.Contains("classification eq 'Infrastructure'", filter);
        Assert.Contains("state ne 'Closed'", filter);
        Assert.Contains("closed_on ge 2026-11-09T00:00:00)", filter);
        Assert.DoesNotContain("classification", SrePortalWorkItemSource.BuildAssignedFilter(new SrePortalOptions { AssigneeName = "x", Classification = "" }, DateTime.Today));
    }

    [Fact]
    public void SrePortalParse_ReadsAnIncidentRowAndSkipsRowsWithoutANumber()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            [ { "id": "ABC123", "incident_number": "SI-089083", "summary": "Tokamak", "state": "Closed", "priority_level": "2 - High", "closed_on": "2026-11-12T10:00:00Z" },
              { "id": "X", "summary": "no number" } ]
            """);
        var rows = document.RootElement.EnumerateArray().ToList();

        var item = SrePortalWorkItemSource.Parse(rows[0], SreOptions)!;

        Assert.Equal("SI-089083", item.Id);
        Assert.Equal("Closed", item.State);
        Assert.Equal(2, item.Priority);
        Assert.NotNull(item.ClosedOn);
        Assert.StartsWith("https://www.aras.com/arasinternalportal/?StartItem=Support%20Incident:ABC123", item.Url);
        Assert.Null(SrePortalWorkItemSource.Parse(rows[1], SreOptions));
    }

    internal sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SreHub.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
