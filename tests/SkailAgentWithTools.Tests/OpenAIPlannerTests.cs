using System.Net;
using System.Text.Json;
using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Worker.Planners;

namespace SkailAgentWithTools.Tests;

public class OpenAIPlannerTests
{
    private static string Completion(string content) => JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } });

    private static OpenAIPlanner Planner(StubHandler handler) => new(new HttpClient(handler), "sk-secret", "gpt-test");

    [Fact]
    public async Task Sends_the_conversation_with_the_key_and_model_to_chat_completions()
    {
        var handler = StubHandler.Json(Completion("""{"answer":"hi"}"""));

        await Planner(handler).NextStepAsync(["customer: hi", "LookUpOrder(1) => x"]);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-secret", request.Headers.GetValues("Authorization").Single());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("gpt-test", json.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("temperature").GetInt32());
        var messages = json.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains(Tools.LookUpOrder, messages[0].GetProperty("content").GetString());
        Assert.Contains(Tools.TrackDelivery, messages[0].GetProperty("content").GetString());
        Assert.Contains("LookUpOrder(1) => x", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Tool_decision_is_parsed()
    {
        var d = await Planner(StubHandler.Json(Completion("""{"tool":"LookUpOrder","argument":"1234","answer":null}"""))).NextStepAsync(["customer: x"]);

        Assert.Equal((Tools.LookUpOrder, "1234", null), (d.Tool, d.Argument, d.Answer));
    }

    [Fact]
    public async Task Blank_strings_become_null_so_an_answer_wins_cleanly()
    {
        var d = await Planner(StubHandler.Json(Completion("""{"tool":"","argument":"","answer":"Arrives tomorrow"}"""))).NextStepAsync(["customer: x"]);

        Assert.Equal((null, null, "Arrives tomorrow"), (d.Tool, d.Argument, d.Answer));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task Output_outside_the_format_is_an_empty_decision_not_an_error(string content) =>
        Assert.True((await Planner(StubHandler.Json(Completion(content))).NextStepAsync(["customer: x"])).IsEmpty);

    [Fact]
    public async Task Body_outside_the_format_is_an_empty_decision() =>
        Assert.True((await Planner(StubHandler.Json("{}")).NextStepAsync(["customer: x"])).IsEmpty);

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Http_failure_throws_so_skail_retries(HttpStatusCode status) =>
        await Assert.ThrowsAsync<HttpRequestException>(() => Planner(StubHandler.Status(status)).NextStepAsync(["customer: x"]));

    [Fact]
    public async Task The_key_is_not_in_the_exception_message()
    {
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Planner(StubHandler.Status(HttpStatusCode.Unauthorized)).NextStepAsync(["customer: x"]));

        Assert.DoesNotContain("sk-secret", e.ToString());
    }

    [Fact]
    public void Name_is_shown_on_the_page() => Assert.Equal("OpenAI", Planner(StubHandler.Status(HttpStatusCode.OK)).Name);
}
