using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Web.Cases;

namespace SkailAgentWithTools.Tests;

public class CaseStoreTests
{
    private readonly CaseStore _store = new();

    private CaseStore Started()
    {
        _store.Create("a1", "Where is order 1234?");
        _store.Apply(CaseEvent.Started("a1", "Where is order 1234?") with { Planner = "Sample planner" });
        return _store;
    }

    [Fact]
    public void Create_starts_in_Starting_with_no_model_calls()
    {
        var s = _store.Create("a1", "hi");

        Assert.Equal((CasePhase.Starting, 0, true), (s.Phase, s.ModelCalls, s.Active));
    }

    [Fact]
    public void Started_moves_to_Thinking_and_records_the_planner()
    {
        var s = Started().Get("a1")!;

        Assert.Equal((CasePhase.Thinking, "Sample planner"), (s.Phase, s.Planner));
    }

    [Fact]
    public void Model_decision_counts_a_model_call_and_shows_the_chosen_tool()
    {
        var store = Started();

        store.Apply(CaseEvent.ModelDecided("a1", 1, Decision.UseTool(Tools.LookUpOrder, "1234")));

        var s = store.Get("a1")!;
        Assert.Equal((CasePhase.UsingTool, 1, 1), (s.Phase, s.Step, s.ModelCalls));
        Assert.Equal("Step 1: model chose LookUpOrder(\"1234\")", s.Timeline[^1].Text);
    }

    [Fact]
    public void Tool_result_goes_back_to_Thinking()
    {
        var store = Started();
        store.Apply(CaseEvent.ModelDecided("a1", 1, Decision.UseTool(Tools.LookUpOrder, "1234")));

        store.Apply(CaseEvent.ToolUsed("a1", 1, Tools.LookUpOrder, "1234", "enviado"));

        var s = store.Get("a1")!;
        Assert.Equal(CasePhase.Thinking, s.Phase);
        Assert.Equal("Step 1: tool result: enviado", s.Timeline[^1].Text);
    }

    [Fact]
    public void Empty_decision_shows_up_as_invalid()
    {
        var store = Started();

        store.Apply(CaseEvent.ModelDecided("a1", 1, Decision.Empty));

        Assert.Equal("Step 1: invalid model decision, asking again", store.Get("a1")!.Timeline[^1].Text);
    }

    [Fact]
    public void Duplicate_step_events_are_ignored_and_do_not_count_model_calls_twice()
    {
        var store = Started();
        var evt = CaseEvent.ModelDecided("a1", 1, Decision.AnswerWith("hi"));

        Assert.True(store.Apply(evt));
        Assert.False(store.Apply(evt));

        var s = store.Get("a1")!;
        Assert.Equal(1, s.ModelCalls);
        Assert.Equal(2, s.Timeline.Count); // started + one decision
    }

    [Fact]
    public void Answered_is_terminal_with_the_result()
    {
        var store = Started();
        var result = CaseResult.Answered("a1", "Arrives tomorrow", 3);

        store.Apply(CaseEvent.Answered("a1", result));

        var s = store.Get("a1")!;
        Assert.Equal((CasePhase.Answered, false, result), (s.Phase, s.Active, s.Result));
    }

    [Fact]
    public void Escalated_is_terminal_with_the_conversation()
    {
        var store = Started();

        store.Apply(CaseEvent.Escalated("a1", CaseResult.Escalated("a1", ["customer: x"])));

        Assert.Equal(CasePhase.Escalated, store.Get("a1")!.Phase);
        Assert.True(store.Get("a1")!.IsTerminal);
    }

    [Fact]
    public void Failed_is_terminal_with_the_error()
    {
        var store = Started();

        store.Apply(CaseEvent.Failed("a1", "boom"));

        var s = store.Get("a1")!;
        Assert.Equal((CasePhase.Failed, "boom"), (s.Phase, s.Error));
    }

    [Fact]
    public void Events_after_the_end_and_for_unknown_ids_are_ignored()
    {
        var store = Started();
        store.Apply(CaseEvent.Failed("a1", "boom"));

        Assert.False(store.Apply(CaseEvent.ModelDecided("a1", 1, Decision.Empty)));
        Assert.False(store.Apply(CaseEvent.Started("ghost", "x")));
    }

    [Fact]
    public async Task Subscribers_get_a_snapshot_per_change_and_the_channel_completes_at_the_end()
    {
        var store = Started();
        using var sub = store.Subscribe("a1");

        store.Apply(CaseEvent.ModelDecided("a1", 1, Decision.AnswerWith("hi")));
        store.Apply(CaseEvent.Answered("a1", CaseResult.Answered("a1", "hi", 1)));

        var phases = new List<CasePhase>();
        await foreach (var s in sub.Reader.ReadAllAsync()) phases.Add(s.Phase);
        Assert.Equal([CasePhase.Thinking, CasePhase.Answered], phases);
    }

    [Fact]
    public void Remove_forgets_the_case()
    {
        Started();

        _store.Remove("a1");

        Assert.Null(_store.Get("a1"));
    }
}
