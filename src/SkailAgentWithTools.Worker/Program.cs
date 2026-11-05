using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Hosting;
using SkailAgentWithTools.Worker;
using SkailAgentWithTools.Worker.Orders;
using SkailAgentWithTools.Worker.Planners;
using SkailAgentWithTools.Worker.Progress;

[assembly: VisibleToSkailPlatform]

var builder = Host.CreateApplicationBuilder(args);

// HttpClient request logs would show up as extra lines under each command in the skail Monitor
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

var webBaseUrl = Environment.GetEnvironmentVariable("WEB_BASE_URL") ?? "http://localhost:8080";

builder.Services.AddHttpClient<IProgressNotifier, HttpProgressNotifier>(c =>
{
    c.BaseAddress = new Uri(webBaseUrl.EndsWith('/') ? webBaseUrl : webBaseUrl + "/");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// OpenAI when there is a key; otherwise the offline sample planner
var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrWhiteSpace(openAiKey))
{
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") is { Length: > 0 } m ? m : "gpt-4o-mini";
    builder.Services.AddHttpClient("openai", c => c.Timeout = TimeSpan.FromSeconds(60));
    builder.Services.AddSingleton<IPlanner>(sp =>
        new OpenAIPlanner(sp.GetRequiredService<IHttpClientFactory>().CreateClient("openai"), openAiKey, model));
}
else
{
    var delayMs = int.TryParse(Environment.GetEnvironmentVariable("SAMPLE_PLANNER_DELAY_MS"), out var ms) ? Math.Max(ms, 0) : 0;
    builder.Services.AddSingleton<IPlanner>(new SamplePlanner(TimeSpan.FromMilliseconds(delayMs)));
}

builder.Services.AddSingleton<IOrderSystem, SimulatedOrderSystem>();
builder.Services.AddSingleton<AgentOperations>();

// reads SKAIL_WORKLOAD, SKAIL_NAMESPACE, SKAIL_KEY and SKAIL_SIDECAR from the environment (.env)
builder.UseSkail();

await builder.Build().RunAsync();
