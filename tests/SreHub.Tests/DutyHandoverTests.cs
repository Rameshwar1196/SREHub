using Aras.SreHub.Models;
using Aras.SreHub.Services;

namespace Aras.SreHub.Tests;

public sealed class DutyHandoverTests : IDisposable
{
    private const string Friday = "2026-11-13";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "srehub-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkLogService _workLog;
    private readonly DutyLogService _duty;

    public DutyHandoverTests()
    {
        Directory.CreateDirectory(_root);
        var environment = new DailyPlanServiceTests.TestEnvironment(_root);
        _workLog = new WorkLogService(environment);
        _duty = new DutyLogService(environment, _workLog);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Task<DutyEntry> Save(string handover, string shift = "Japan") =>
        _duty.SaveAsync(Friday, new DutyEntry { DoShift = shift, ShiftTime = "5:30 AM - 1:30 PM", Handover = handover }, CancellationToken.None);

    private async Task<WorkLogEntry?> WorkLogEntryAsync() =>
        (await _workLog.GetMonthAsync("2026-11", CancellationToken.None)).Entries.FirstOrDefault(entry => entry.Date == Friday);

    [Theory]
    [InlineData("Japan", "Worked as DO in Japan shift")]
    [InlineData("India", "Worked as DO in India shift")]
    [InlineData("Japan + India", "Worked as DO in Japan and India shifts")]
    [InlineData("", "Worked as DO")]
    public async Task CompletedHandover_WritesTheDoShiftIntoTheDailyWorkSummary(string shift, string expected)
    {
        await Save("Completed", shift);

        Assert.Equal(expected, (await WorkLogEntryAsync())!.Summary);
    }

    [Fact]
    public async Task WorkInProgressHandover_DoesNotTouchTheWorkLog()
    {
        await Save("Work in progress");

        Assert.Null(await WorkLogEntryAsync());
    }

    [Fact]
    public async Task CompletedHandover_KeepsOtherFieldsAndSummaryTextAndIsNotDuplicated()
    {
        await _workLog.SaveAsync(Friday, new WorkLogEntry { Attendance = "Yes", Summary = "Fixed alert", Escalation = "Minor" }, CancellationToken.None);

        await Save("Completed");
        await Save("Completed");

        var entry = (await WorkLogEntryAsync())!;
        Assert.Equal("Fixed alert; Worked as DO in Japan shift", entry.Summary);
        Assert.Equal("Yes", entry.Attendance);
        Assert.Equal("Minor", entry.Escalation);
    }

    [Fact]
    public async Task LeavingCompleted_RemovesOnlyTheDoLine()
    {
        await _workLog.SaveAsync(Friday, new WorkLogEntry { Summary = "Fixed alert" }, CancellationToken.None);
        await Save("Completed");

        await Save("Work in progress");

        Assert.Equal("Fixed alert", (await WorkLogEntryAsync())!.Summary);
    }

    [Fact]
    public async Task ChangingTheShiftOfACompletedHandover_UpdatesTheLine()
    {
        await Save("Completed", "Japan");

        await Save("Completed", "India");

        Assert.Equal("Worked as DO in India shift", (await WorkLogEntryAsync())!.Summary);
    }

    [Fact]
    public void Apply_KeepsTheDoLineWhenTheRestIsTooLong()
    {
        var summary = DoWorkSummary.Apply(new string('x', 600), "Worked as DO in Japan shift");

        Assert.Equal(DoWorkSummary.MaxLength, summary.Length);
        Assert.EndsWith("...; Worked as DO in Japan shift", summary);
    }

    [Fact]
    public async Task CompletedHandover_ReplacesYourOwnWorkingAsDoWording_AndLeavingCompletedKeepsIt()
    {
        await _workLog.SaveAsync(Friday, new WorkLogEntry { Attendance = "DO-wk", Summary = "Working as DO in Japan Shift." }, CancellationToken.None);

        await Save("Completed");
        Assert.Equal("Worked as DO in Japan shift", (await WorkLogEntryAsync())!.Summary);

        await _workLog.SaveAsync(Friday, new WorkLogEntry { Attendance = "DO-wk", Summary = "Working as DO in Japan Shift." }, CancellationToken.None);
        await Save("Work in progress");
        Assert.Equal("Working as DO in Japan Shift.", (await WorkLogEntryAsync())!.Summary);
    }

    [Fact]
    public async Task RetiredGivenHandover_ReadsAsNoHandover()
    {
        Directory.CreateDirectory(Path.Combine(_root, "App_Data"));
        await File.WriteAllTextAsync(Path.Combine(_root, "App_Data", "dutylog.json"), """[ { "date": "2026-11-16", "doShift": "Japan", "handover": "Given" } ]""");

        var year = await _duty.GetYearAsync(2026, CancellationToken.None);

        Assert.Equal(string.Empty, year.Entries.Single().Handover);
        await Assert.ThrowsAsync<ArgumentException>(() => Save("Given"));
    }

    [Fact]
    public async Task MarkingATargetDone_AddsItToThatDaysSummaryWithoutLosingTheRest_AndReopeningRemovesIt()
    {
        var plan = new DailyPlanService(new DailyPlanServiceTests.TestEnvironment(_root), _workLog);
        await _workLog.SaveAsync(Friday, new WorkLogEntry { Attendance = "DO-wk", Summary = "Working as DO in Japan Shift." }, CancellationToken.None);
        var open = new IterationTarget { Title = "Karcher UAT deployment", Status = "InProgress", PlannedDays = 1 };
        var view = await plan.SaveAsync(Friday, new DailyPlanSave { Targets = [open] }, CancellationToken.None);
        Assert.Equal("Working as DO in Japan Shift.", (await WorkLogEntryAsync())!.Summary);

        var target = view.Iteration.Targets.Single();
        target.Status = "Done";
        await plan.SaveAsync(Friday, new DailyPlanSave { Targets = [target] }, CancellationToken.None);
        await plan.SaveAsync(Friday, new DailyPlanSave { Targets = [target] }, CancellationToken.None);

        Assert.Equal("Working as DO in Japan Shift.; Karcher UAT deployment", (await WorkLogEntryAsync())!.Summary);
        Assert.Equal("DO-wk", (await WorkLogEntryAsync())!.Attendance);

        target.Status = "InProgress";
        await plan.SaveAsync(Friday, new DailyPlanSave { Targets = [target] }, CancellationToken.None);

        Assert.Equal("Working as DO in Japan Shift.", (await WorkLogEntryAsync())!.Summary);
    }

    [Fact]
    public async Task PushingTheDailyPlan_KeepsTheDoLine()
    {
        var plan = new DailyPlanService(new DailyPlanServiceTests.TestEnvironment(_root), _workLog);
        await Save("Completed");
        await plan.SaveAsync(Friday, new DailyPlanSave { Day = new DailyPlanDay { EndOfDay = "- Fix alert" } }, CancellationToken.None);

        var entry = await plan.PushToWorkLogAsync(Friday, CancellationToken.None);

        Assert.Equal("Fix alert; Worked as DO in Japan shift", entry.Summary);
    }
}
