using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Hosting;
using SkailAgentWithTools;
using SkailAgentWithTools.Demo;
using SkailAgentWithTools.Skail;

[assembly: VisibleToSkailPlatform]

// usage: dotnet run
//   then open http://localhost:8080
if (args.Any(a => !a.StartsWith('-')))
{
    Console.Error.WriteLine("This demo takes no arguments.");
    return 1;
}

DotEnv.Load();
string[] required = ["SKAIL_WORKLOAD", "SKAIL_NAMESPACE", "SKAIL_KEY", "SKAIL_SIDECAR"];
var missing = required.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
if (missing.Length > 0)
{
    Console.Error.WriteLine($"Missing {string.Join(", ", missing)}. Copy .env.example to .env and fill it in.");
    return 1;
}

var builder = WebApplication.CreateBuilder(args);

// keep the console for the demo: only warnings and errors from the host and the libraries
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// the page: localhost only, unless DEMO_LISTEN says otherwise (the container listens on all interfaces)
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("DEMO_LISTEN") ?? "http://localhost:8080");

// OpenAI when there is a key; otherwise the offline sample planner
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(openAiKey))
{
    builder.Services.AddSingleton<IPlanner, SamplePlanner>();
}
else
{
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") is { Length: > 0 } m ? m : "gpt-4o-mini";
    builder.Services.AddHttpClient("openai", http => http.Timeout = TimeSpan.FromSeconds(60));
    builder.Services.AddSingleton<IPlanner>(sp =>
        new OpenAIPlanner(sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai"), openAiKey, model));
}
builder.Services.AddSingleton<SimulatedOrderSystem>();
builder.Services.AddSingleton<CaseDesk>();
builder.Services.AddSingleton(SkailApiOptions.FromEnvironment());
builder.Services.AddHttpClient<SkailApiClient>(http => http.Timeout = TimeSpan.FromSeconds(10));

// hosts the worker that executes SupportAgent; reads the SKAIL_* variables
builder.UseSkail();

var app = builder.Build();
var desk = app.Services.GetRequiredService<CaseDesk>();

var page = Path.Combine(AppContext.BaseDirectory, "Page", "index.html");
app.MapGet("/", () => Results.File(page, "text/html; charset=utf-8"));
app.MapGet("/api/state", () => Results.Json(desk.View()));
app.MapPost("/api/cases", async (AskRequest request, CancellationToken ct) =>
{
    var question = request.Question?.Trim() ?? "";
    if (question.Length is 0 or > 300) return Results.BadRequest();
    await desk.StartAsync(question, ct);
    return Results.Ok();
});

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine("Open http://localhost:8080 to ask the agent. Ctrl+C exits; a case in the middle goes on in skail."));

await app.RunAsync();
return 0;

internal sealed record AskRequest(string? Question);
