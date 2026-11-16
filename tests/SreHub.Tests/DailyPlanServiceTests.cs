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
    public async Task Progress_TreatsMissingPlannedDaysAsOneDay()
    {
        var view = await Save(Friday, Target("A", "Done"), Target("B"));

        Assert.Equal(50, view.Iteration.Progress);
    }

    [Fact]
    public async Task Progress_IgnoresCarriedTargets()
    {
        var view = await Save(Friday, Target("A", "Done"), Target("B", "Carried"));

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
    public async Task Get_CountsOpenTargetsFromTheEarlierIteration()
    {
        await Save(Monday, Target("Open"), Target("Working", "InProgress"), Target("Stuck", "Blocked"), Target("Finished", "Done"));

        var view = await _service.GetAsync(NextIterationDay, CancellationToken.None);

        Assert.Equal(3, view.CarryOverCount);
    }

    [Fact]
    public async Task CarryOver_CopiesOpenTargetsWithSubTasksAndMarksOriginalsCarried()
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
        await Save(Monday, Target("Open"));

        await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);
        var second = await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        Assert.Single(second.Iteration.Targets);
        Assert.Equal(0, second.CarryOverCount);
    }

    [Fact]
    public async Task CarryOver_KeepsTheOriginalIterationAcrossSeveralIterations()
    {
        await Save(Monday, Target("Long running"));
        await _service.CarryOverAsync(NextIterationDay, CancellationToken.None);

        var view = await _service.CarryOverAsync("2026-12-08", CancellationToken.None);

        Assert.Equal("2026-11-09", Assert.Single(view.Iteration.Targets).CarriedFrom);
    }

    [Fact]
    public async Task CarryOver_IgnoresIterationsOlderThanTheWindow()
    {
        await Save("2026-08-03", Target("Ancient"));

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
    public async Task PushToWorkLog_WithNothingAchievedThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PushToWorkLogAsync(Friday, CancellationToken.None));
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "SreHub.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
