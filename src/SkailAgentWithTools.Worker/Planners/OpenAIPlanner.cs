using System.Net.Http.Json;
using System.Text.Json;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker.Planners;

/// <summary>
/// Asks an OpenAI chat model for the next step. The key is only used in the request header: it is never
/// logged, notified or returned. An answer outside the expected format is an empty (invalid) decision, not an error;
/// an HTTP failure throws so skail retries the command.
/// </summary>
public sealed class OpenAIPlanner(HttpClient http, string apiKey, string model) : IPlanner
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private static readonly string Prompt = $$"""
        You are a customer support agent. Decide the next step from the conversation.
        Tools: {{Tools.LookUpOrder}}(order number) and {{Tools.TrackDelivery}}(tracking code).
        Reply ONLY with JSON: {"tool": string|null, "argument": string|null, "answer": string|null}.
        Either use a tool or answer the customer, never both.
        """;

    public string Name => "OpenAI";

    public async Task<Decision> NextStepAsync(string[] conversation, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = Prompt },
                    new { role = "user", content = string.Join('\n', conversation) },
                },
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static Decision Parse(string body)
    {
        try
        {
            using var completion = JsonDocument.Parse(body);
            var content = completion.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            using var decision = JsonDocument.Parse(content ?? "");
            var root = decision.RootElement;
            return new Decision(Text(root, "tool"), Text(root, "argument"), Text(root, "answer"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return Decision.Empty;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}
