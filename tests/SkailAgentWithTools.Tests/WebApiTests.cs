using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Web.Skail;

namespace SkailAgentWithTools.Tests;

public sealed class FakeGateway : ISkailGateway
{
    public List<(string CaseId, string Question)> Triggers { get; } = [];
    public Exception? FailWith { get; set; }

    public Task TriggerAsync(string caseId, string question, CancellationToken ct = default)
    {
        if (FailWith is not null) throw FailWith;
        Triggers.Add((caseId, question));
        return Task.CompletedTask;
    }
}

public sealed class WebFactory : WebApplicationFactory<Program>
{
    public FakeGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<ISkailGateway>();
            s.AddSingleton<ISkailGateway>(Gateway);
        });
    }
}

public class WebApiTests : IClassFixture<WebFactory>
{
    private readonly WebFactory _factory;
    private readonly HttpClient _http;

    public WebApiTests(WebFactory factory)
    {
        _factory = factory;
        _http = factory.CreateClient();
    }

    private async Task<JsonElement> Start(string? message)
    {
        var res = await _http.PostAsJsonAsync("/api/cases", new { message });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> Report(string id, CaseEvent evt) =>
        _http.PostAsJsonAsync($"/internal/cases/{id}/events", evt, Json.Options);

    [Fact]
    public async Task Start_triggers_the_function_with_the_question_and_a_new_guid()
    {
        var state = await Start("Where is order 1234?");

        var id = state.GetProperty("caseId").GetString()!;
        Assert.True(Guid.TryParse(id, out _));
        Assert.Contains((id, "Where is order 1234?"), _factory.Gateway.Triggers);
        Assert.Equal("Starting", state.GetProperty("phase").GetString());
        Assert.True(state.GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_without_a_message_uses_the_default(string? message)
    {
        var state = await Start(message);

        Assert.Equal(AgentLimits.DefaultMessage, state.GetProperty("question").GetString());
    }

    [Fact]
    public async Task Start_rejects_a_message_that_is_too_long()
    {
        var res = await _http.PostAsJsonAsync("/api/cases", new { message = new string('x', AgentLimits.MaxMessageLength + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Start_answers_502_and_forgets_the_case_when_the_trigger_fails()
    {
        _factory.Gateway.FailWith = new SkailGatewayException("giving up");
        try
        {
            var res = await _http.PostAsJsonAsync("/api/cases", new { message = "hi" });
            Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        }
        finally { _factory.Gateway.FailWith = null; }
    }

    [Fact]
    public async Task Get_unknown_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/cases/ghost")).StatusCode);

    [Fact]
    public async Task Worker_events_update_the_state()
    {
        var id = (await Start("hi")).GetProperty("caseId").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await Report(id, CaseEvent.Started(id, "hi") with { Planner = "Sample planner" })).StatusCode);
        await Report(id, CaseEvent.ModelDecided(id, 1, Decision.UseTool(Tools.LookUpOrder, "1234")));

        var state = await _http.GetFromJsonAsync<JsonElement>($"/api/cases/{id}");
        Assert.Equal("UsingTool", state.GetProperty("phase").GetString());
        Assert.Equal(1, state.GetProperty("modelCalls").GetInt32());
        Assert.Equal("Sample planner", state.GetProperty("planner").GetString());
    }

    [Fact]
    public async Task Stream_sends_the_snapshots_in_order_and_ends_at_the_terminal_state()
    {
        var id = (await Start("hi")).GetProperty("caseId").GetString()!;
        await Report(id, CaseEvent.Started(id, "hi"));
        await Report(id, CaseEvent.ModelDecided(id, 1, Decision.AnswerWith("hi!")));
        await Report(id, CaseEvent.Answered(id, CaseResult.Answered(id, "hi!", 1)));

        using var res = await _http.GetAsync($"/api/cases/{id}/stream", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType!.MediaType);
        var body = await res.Content.ReadAsStringAsync(); // terminal: the stream closes after the first snapshot

        var last = JsonDocument.Parse(body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Last()["data: ".Length..]).RootElement;
        Assert.Equal("Answered", last.GetProperty("phase").GetString());
        Assert.Equal("hi!", last.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Stream_of_unknown_is_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/cases/ghost/stream")).StatusCode);

    [Fact]
    public async Task Config_has_the_default_message()
    {
        var config = await _http.GetFromJsonAsync<JsonElement>("/api/config");

        Assert.Equal(AgentLimits.DefaultMessage, config.GetProperty("defaultMessage").GetString());
    }

    [Fact]
    public async Task Healthz_is_ok() => Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/healthz")).StatusCode);

    [Fact]
    public async Task Page_has_the_timeline_and_the_model_call_counter()
    {
        var html = await _http.GetStringAsync("/");

        Assert.Contains("id=\"timeline\"", html);
        Assert.Contains("id=\"modelCalls\"", html);
        Assert.Contains("id=\"planner\"", html);
    }
}
