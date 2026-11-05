using System.Text.Json;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Tests;

public class ContractsTests
{
    [Fact]
    public void Decision_distinguishes_tool_answer_and_empty()
    {
        Assert.False(Decision.UseTool(Tools.LookUpOrder, "1234").IsEmpty);
        Assert.False(Decision.AnswerWith("hi").IsEmpty);
        Assert.True(Decision.Empty.IsEmpty);
        Assert.True(new Decision(" ", null, "").IsEmpty);
    }

    [Theory]
    [InlineData("LookUpOrder", true)]
    [InlineData("TrackDelivery", true)]
    [InlineData("DeleteEverything", false)]
    [InlineData(null, false)]
    public void Tools_knows_the_two_tools(string? name, bool known) =>
        Assert.Equal(known, Tools.IsKnown(name));

    [Fact]
    public void Step_limit_is_six() => Assert.Equal(6, AgentLimits.MaxSteps);

    [Fact]
    public void Answered_result_carries_text_and_steps()
    {
        var r = CaseResult.Answered("a1", "Arrives tomorrow", 3);

        Assert.Equal((CaseEnd.Answered, "Arrives tomorrow", 3), (r.End, r.Text, r.Steps));
    }

    [Fact]
    public void Escalated_result_keeps_the_conversation_and_the_reason()
    {
        string[] conversation = ["customer: hi", "system: x"];

        var r = CaseResult.Escalated("a1", conversation);

        Assert.Equal(CaseEnd.Escalated, r.End);
        Assert.Equal(conversation, r.Conversation);
        Assert.Contains(AgentLimits.MaxSteps.ToString(), r.Text);
        Assert.Equal(AgentLimits.MaxSteps, r.Steps);
    }

    [Fact]
    public void Events_round_trip_through_json_with_null_fields()
    {
        var evt = CaseEvent.ModelDecided("a1", 2, Decision.UseTool(Tools.TrackDelivery, "BR1"));

        var back = JsonSerializer.Deserialize<CaseEvent>(JsonSerializer.Serialize(evt, Json.Options), Json.Options)!;

        Assert.Equal(evt, back);
        Assert.Null(back.Decision!.Answer);
    }

    [Fact]
    public void Answered_event_round_trips_with_its_result()
    {
        var evt = CaseEvent.Answered("a1", CaseResult.Answered("a1", "ok", 2));

        var back = JsonSerializer.Deserialize<CaseEvent>(JsonSerializer.Serialize(evt, Json.Options), Json.Options)!;

        Assert.Equal(CaseStage.Answered, back.Stage);
        Assert.Equal("ok", back.Result!.Text);
    }
}
