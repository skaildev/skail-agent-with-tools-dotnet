using System.Net.Http.Json;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker.Progress;

public sealed class HttpProgressNotifier(HttpClient http) : IProgressNotifier
{
    public async Task NotifyAsync(CaseEvent evt, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync($"internal/cases/{Uri.EscapeDataString(evt.CaseId)}/events", evt, Json.Options, ct);
        response.EnsureSuccessStatusCode();
    }
}
