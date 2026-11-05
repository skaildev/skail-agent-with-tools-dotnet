using System.Net;
using System.Text.Json;
using SkailAgentWithTools.Web.Skail;

namespace SkailAgentWithTools.Tests;

public class GatewayTests
{
    private static readonly SkailOptions Opts = new()
    {
        BaseUrl = "https://skail.example.test/",
        Namespace = "tenant-1/env-1",
        Key = "env-key",
        Workload = "agent-with-tools:v0.0.1",
    };

    private static SkailApiGateway Gateway(StubHandler handler, SkailOptions? options = null) =>
        new(new HttpClient(handler), options ?? Opts, retryDelay: _ => TimeSpan.Zero);

    [Fact]
    public async Task Trigger_posts_id_and_question_to_the_function_route_of_the_workload()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);

        await Gateway(handler).TriggerAsync("a-1", "Where is order 1234?");

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://skail.example.test/api/v1/trigger/agent-with-tools/v0.0.1/AssistCustomer/a-1", request.RequestUri!.ToString());
        Assert.Equal("env-key", request.Headers.GetValues("skail-key").Single());
        Assert.Equal("Bearer env-key", request.Headers.GetValues("Authorization").Single());
        Assert.Equal("tenant-1/env-1", request.Headers.GetValues("Skail-namespace").Single());
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/json");
        Assert.Equal(["a-1", "Where is order 1234?"], JsonSerializer.Deserialize<string[]>(body));
    }

    [Fact]
    public async Task Id_is_escaped_in_the_route()
    {
        var handler = StubHandler.Status(HttpStatusCode.OK);

        await Gateway(handler).TriggerAsync("a/1 x", "hi");

        Assert.EndsWith("/AssistCustomer/a%2F1%20x", handler.Calls[0].Request.RequestUri!.OriginalString);
    }

    [Fact]
    public async Task Transient_failures_are_retried_with_the_same_id_and_body()
    {
        var handler = new StubHandler((_, n) => new HttpResponseMessage(n < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        await Gateway(handler).TriggerAsync("a-1", "hi");

        Assert.Equal(3, handler.Calls.Count);
        Assert.Single(handler.Calls.Select(c => c.Body).Distinct());
        Assert.Single(handler.Calls.Select(c => c.Request.RequestUri).Distinct());
    }

    [Fact]
    public async Task Network_errors_are_retried()
    {
        var handler = new StubHandler((_, n) => n == 1 ? throw new HttpRequestException("reset") : new HttpResponseMessage(HttpStatusCode.OK));

        await Gateway(handler).TriggerAsync("a-1", "hi");

        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_attempts()
    {
        var handler = StubHandler.Status(HttpStatusCode.TooManyRequests);

        var e = await Assert.ThrowsAsync<SkailGatewayException>(() => Gateway(handler).TriggerAsync("a-1", "hi"));

        Assert.Equal(SkailApiGateway.MaxAttempts, handler.Calls.Count);
        Assert.Contains("giving up", e.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Auth_failures_are_not_retried(HttpStatusCode status)
    {
        var handler = StubHandler.Status(status);

        var e = await Assert.ThrowsAsync<SkailGatewayException>(() => Gateway(handler).TriggerAsync("a-1", "hi"));

        Assert.Single(handler.Calls);
        Assert.Contains("SKAIL_KEY", e.Message);
    }

    [Fact]
    public async Task Other_client_errors_are_not_retried()
    {
        var handler = StubHandler.Status(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<SkailGatewayException>(() => Gateway(handler).TriggerAsync("a-1", "hi"));

        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Missing_base_url_is_explained()
    {
        var e = await Assert.ThrowsAsync<SkailGatewayException>(() =>
            Gateway(StubHandler.Status(HttpStatusCode.OK), new SkailOptions { Workload = "a:b" }).TriggerAsync("a-1", "hi"));

        Assert.Contains("SKAIL_BASE_URL", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("semversao")]
    [InlineData("a:b:c")]
    public async Task Workload_must_be_name_colon_version(string workload)
    {
        var options = new SkailOptions { BaseUrl = "https://x.test", Workload = workload };

        var e = await Assert.ThrowsAsync<SkailGatewayException>(() =>
            Gateway(StubHandler.Status(HttpStatusCode.OK), options).TriggerAsync("a-1", "hi"));

        Assert.Contains("SKAIL_WORKLOAD", e.Message);
    }
}
