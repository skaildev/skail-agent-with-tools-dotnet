using Microsoft.Extensions.Logging.Abstractions;
using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Worker;
using SkailAgentWithTools.Worker.Orders;
using SkailAgentWithTools.Worker.Planners;
using SkailAgentWithTools.Worker.Progress;

namespace SkailAgentWithTools.Tests;

public sealed class RecordingNotifier : IProgressNotifier
{
    public List<CaseEvent> Events { get; } = [];
    public Exception? FailWith { get; set; }

    public Task NotifyAsync(CaseEvent evt, CancellationToken ct = default)
    {
        if (FailWith is not null) throw FailWith;
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

public sealed class ScriptedPlanner(params Decision[] decisions) : IPlanner
{
    private int _next;
    public int Calls => _next;
    public List<string[]> Conversations { get; } = [];
    public Exception? FailWith { get; set; }
    public string Name => "Scripted";

    public Task<Decision> NextStepAsync(string[] conversation, CancellationToken ct = default)
    {
        if (FailWith is not null) throw FailWith;
        Conversations.Add(conversation);
        return Task.FromResult(decisions[_next++]);
    }
}

/// <summary>Commands need the skail runtime; the logic they delegate to is tested here.</summary>
public class AgentOperationsTests
{
    private static (AgentOperations Ops, RecordingNotifier Notifier, ScriptedPlanner Planner) Create(params Decision[] decisions)
    {
        var notifier = new RecordingNotifier();
        var planner = new ScriptedPlanner(decisions);
        return (new AgentOperations(planner, new SimulatedOrderSystem(), notifier, NullLogger<AgentOperations>.Instance), notifier, planner);
    }

    [Fact]
    public async Task Decide_returns_the_planner_decision_and_reports_it_with_the_step()
    {
        var decision = Decision.UseTool(Tools.LookUpOrder, "1234");
        var (ops, notifier, planner) = Create(decision);

        var result = await ops.DecideAsync("a1", 2, ["customer: hi"]);

        Assert.Equal(decision, result);
        Assert.Equal(["customer: hi"], Assert.Single(planner.Conversations));
        var evt = Assert.Single(notifier.Events);
        Assert.Equal((CaseStage.ModelDecided, 2, decision), (evt.Stage, evt.Step, evt.Decision));
    }

    [Fact]
    public async Task Decide_does_not_call_the_model_again_when_only_the_progress_report_fails()
    {
        var (ops, notifier, planner) = Create(Decision.AnswerWith("hi"));
        notifier.FailWith = new HttpRequestException("web is down");

        var result = await ops.DecideAsync("a1", 1, ["customer: hi"]);

        Assert.Equal("hi", result.Answer);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task Decide_throws_when_the_planner_fails_so_skail_retries()
    {
        var (ops, _, planner) = Create();
        planner.FailWith = new HttpRequestException("429");

        await Assert.ThrowsAsync<HttpRequestException>(() => ops.DecideAsync("a1", 1, ["customer: hi"]));
    }

    [Theory]
    [InlineData(Tools.LookUpOrder, "1234", "BR123456789")]
    [InlineData(Tools.TrackDelivery, "BR123456789", "arriving")]
    public async Task UseTool_dispatches_by_name_and_reports_the_result(string tool, string arg, string expected)
    {
        var (ops, notifier, _) = Create();

        var result = await ops.UseToolAsync("a1", 1, tool, arg);

        Assert.Contains(expected, result);
        var evt = Assert.Single(notifier.Events);
        Assert.Equal((CaseStage.ToolUsed, 1, tool, arg, result), (evt.Stage, evt.Step, evt.Tool, evt.Argument, evt.Text));
    }

    [Fact]
    public async Task UseTool_unknown_tool_is_text_for_the_model_not_an_exception()
    {
        var (ops, _, _) = Create();

        Assert.Equal("unknown tool: DeleteEverything", await ops.UseToolAsync("a1", 1, "DeleteEverything", "x"));
    }

    [Fact]
    public async Task UseTool_does_not_fail_when_only_the_progress_report_fails()
    {
        var (ops, notifier, _) = Create();
        notifier.FailWith = new HttpRequestException("web is down");

        Assert.Contains("BR123456789", await ops.UseToolAsync("a1", 1, Tools.LookUpOrder, "1234"));
    }

    [Fact]
    public async Task Answer_returns_answered_and_reports_it()
    {
        var (ops, notifier, _) = Create();

        var result = await ops.AnswerAsync("a1", 3, "Arrives tomorrow");

        Assert.Equal((CaseEnd.Answered, "Arrives tomorrow", 3), (result.End, result.Text, result.Steps));
        var evt = Assert.Single(notifier.Events);
        Assert.Equal((CaseStage.Answered, result), (evt.Stage, evt.Result));
    }

    [Fact]
    public async Task Answer_is_idempotent_for_a_retry()
    {
        var (ops, _, _) = Create();

        Assert.Equal(await ops.AnswerAsync("a1", 3, "x"), await ops.AnswerAsync("a1", 3, "x"));
    }

    [Fact]
    public async Task Answer_throws_when_it_cannot_deliver_so_skail_retries()
    {
        var (ops, notifier, _) = Create();
        notifier.FailWith = new HttpRequestException("web is down");

        await Assert.ThrowsAsync<HttpRequestException>(() => ops.AnswerAsync("a1", 1, "x"));
    }

    [Fact]
    public async Task Escalate_records_the_conversation_and_reports_the_handoff()
    {
        var (ops, notifier, _) = Create();
        string[] conversation = ["customer: complaint", "system: invalid decision."];

        var result = await ops.EscalateAsync("a1", conversation);

        Assert.Equal((CaseEnd.Escalated, conversation), (result.End, result.Conversation));
        var evt = Assert.Single(notifier.Events);
        Assert.Equal((CaseStage.Escalated, result), (evt.Stage, evt.Result));
        Assert.Equal(result, await ops.EscalateAsync("a1", conversation));
    }

    [Fact]
    public async Task ReportStatus_forwards_the_event()
    {
        var (ops, notifier, _) = Create();

        await ops.ReportStatusAsync(CaseEvent.Failed("a1", "boom"));

        Assert.Equal(CaseStage.Failed, Assert.Single(notifier.Events).Stage);
    }

    [Fact]
    public async Task ReportStatus_of_started_tells_which_planner_is_in_use()
    {
        var (ops, notifier, _) = Create();

        await ops.ReportStatusAsync(CaseEvent.Started("a1", "hi"));

        Assert.Equal("Scripted", Assert.Single(notifier.Events).Planner);
    }
}
