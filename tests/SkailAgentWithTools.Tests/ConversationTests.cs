using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Worker;

namespace SkailAgentWithTools.Tests;

public class ConversationTests
{
    [Fact]
    public void Customer_line_comes_first() => Assert.Equal("customer: hi", Conversation.CustomerLine("hi"));

    [Fact]
    public void Tool_line_shows_call_and_result() =>
        Assert.Equal("LookUpOrder(1234) => enviado", Conversation.ToolLine("LookUpOrder", "1234", "enviado"));

    [Fact]
    public void Tool_result_is_truncated_to_keep_the_history_small()
    {
        var line = Conversation.ToolLine("LookUpOrder", "1", new string('x', 1000));

        Assert.True(line.Length < 400);
    }

    [Fact]
    public void Warning_text_matches_what_the_model_is_told() =>
        Assert.Equal("system: invalid decision. Use a tool or answer the customer.", Conversation.InvalidDecisionWarning);
}
