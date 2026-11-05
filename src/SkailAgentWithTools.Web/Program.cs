using System.Text.Json;
using System.Text.Json.Serialization;
using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Web.Cases;
using SkailAgentWithTools.Web.Skail;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<CaseStore>();
builder.Services.AddSingleton(SkailOptions.FromEnvironment());
builder.Services.AddHttpClient<ISkailGateway, SkailApiGateway>(c => c.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache" });

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapGet("/api/config", () => Results.Ok(new
{
    defaultMessage = AgentLimits.DefaultMessage,
    monitorUrl = Environment.GetEnvironmentVariable("MONITOR_URL") is { Length: > 0 } url ? url : null,
}));

app.MapPost("/api/cases", async (StartCaseRequest? request, CaseStore store, ISkailGateway skail, CancellationToken ct) =>
{
    var message = string.IsNullOrWhiteSpace(request?.Message) ? AgentLimits.DefaultMessage : request.Message.Trim();
    if (message.Length > AgentLimits.MaxMessageLength)
        return Results.BadRequest(new { error = $"The message can have at most {AgentLimits.MaxMessageLength} characters" });

    var caseId = Guid.NewGuid().ToString();
    var state = store.Create(caseId, message);
    try
    {
        await skail.TriggerAsync(caseId, message, ct);
    }
    catch (SkailGatewayException e)
    {
        store.Remove(caseId);
        return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    return Results.Ok(state);
});

app.MapGet("/api/cases/{id}", (string id, CaseStore store) =>
    store.Get(id) is { } state ? Results.Ok(state) : Results.NotFound());

// called by the worker's commands (ReportStatus, DecideNextStep, UseTool, Answer, EscalateToHuman)
app.MapPost("/internal/cases/{id}/events", (string id, CaseEvent evt, CaseStore store) =>
{
    store.Apply(evt with { CaseId = id });
    return Results.NoContent();
});

app.MapGet("/api/cases/{id}/stream", async (string id, HttpContext http, CaseStore store, CancellationToken ct) =>
{
    using var subscription = store.Subscribe(id); // subscribe before reading the snapshot: no update is lost
    if (store.Get(id) is not { } current) return Results.NotFound();

    http.Response.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";

    async Task Send(CaseState state)
    {
        var json = JsonSerializer.Serialize(state, Json.Options);
        await http.Response.WriteAsync($"data: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    try
    {
        await Send(current);
        if (current.IsTerminal) return Results.Empty;

        await foreach (var state in subscription.Reader.ReadAllAsync(ct))
        {
            await Send(state);
            if (state.IsTerminal) break;
        }
    }
    catch (OperationCanceledException)
    {
        // client disconnected
    }
    return Results.Empty;
});

app.Run();

public sealed record StartCaseRequest(string? Message);

public partial class Program;
