using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Worker.Planners;

namespace SkailAgentWithTools.Tests;

public class SamplePlannerTests
{
    private static Task<Decision> Plan(params string[] conversation) => new SamplePlanner().NextStepAsync(conversation);

    [Fact]
    public async Task Question_with_an_order_number_starts_with_LookUpOrder()
    {
        var d = await Plan("customer: Where is my order 1234?");

        Assert.Equal((Tools.LookUpOrder, "1234", null), (d.Tool, d.Argument, d.Answer));
    }

    [Fact]
    public async Task Order_result_with_a_tracking_code_leads_to_TrackDelivery()
    {
        var d = await Plan("customer: Where is my order 1234?", "LookUpOrder(1234) => shipped, tracking code BR123456789");

        Assert.Equal((Tools.TrackDelivery, "BR123456789"), (d.Tool, d.Argument));
    }

    [Fact]
    public async Task Order_result_without_tracking_is_answered()
    {
        var d = await Plan("customer: order 5678?", "LookUpOrder(5678) => being prepared, no tracking code yet");

        Assert.Contains("being prepared", d.Answer);
        Assert.Null(d.Tool);
    }

    [Fact]
    public async Task Tracking_result_is_answered()
    {
        var d = await Plan("customer: order 1234?", "LookUpOrder(1234) => shipped, tracking code BR123456789",
            "TrackDelivery(BR123456789) => in transit in São Paulo, arriving tomorrow");

        Assert.Contains("arriving tomorrow", d.Answer);
    }

    [Fact]
    public async Task Unknown_order_is_answered_asking_to_check_the_number()
    {
        var d = await Plan("customer: order 9999?", "LookUpOrder(9999) => order not found");

        Assert.Contains("9999", d.Answer);
    }

    [Fact]
    public async Task Question_without_an_order_number_asks_for_it()
    {
        var d = await Plan("customer: Hi, I need help");

        Assert.Contains("order number", d.Answer);
    }

    [Theory]
    [InlineData("customer: I want to file a complaint about order 1234")]
    [InlineData("customer: COMPLAINT about my order")]
    public async Task Complaints_never_get_an_answer_so_the_case_escalates(string question)
    {
        Assert.True((await Plan(question)).IsEmpty);
        Assert.True((await Plan(question, "system: invalid decision. Use a tool or answer the customer.")).IsEmpty);
    }

    [Fact]
    public async Task Same_conversation_gives_the_same_decision()
    {
        string[] conversation = ["customer: order 1234?"];

        Assert.Equal(await Plan(conversation), await Plan(conversation));
    }

    [Fact]
    public void Name_is_shown_on_the_page() => Assert.Equal("Sample planner", new SamplePlanner().Name);
}
