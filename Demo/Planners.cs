using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkailAgentWithTools.Demo;

/// <summary>Decides the next step from the conversation so far. Runs only inside the DecideNextStep command.</summary>
public interface IPlanner
{
    /// <summary>Shown on the page.</summary>
    string Name { get; }

    Task<Decision> NextStepAsync(string[] conversation);
}

/// <summary>
/// The planner used without an OpenAI key: offline and always the same for the same conversation. It takes 1.5
/// seconds per decision, like a model would, which also gives time to close the app in the middle of a case.
/// </summary>
public sealed partial class SamplePlanner : IPlanner
{
    public string Name => "Sample planner (offline)";

    public async Task<Decision> NextStepAsync(string[] conversation)
    {
        await Task.Delay(1500);
        var question = conversation[0];

        // a complaint never gets an answer, so the case goes to a person
        if (question.Contains("complaint", StringComparison.OrdinalIgnoreCase)) return Decision.Empty;

        if (LastResult(conversation, Tools.TrackDelivery) is { } delivery)
            return Decision.AnswerWith($"Your delivery is {delivery}.");

        var number = Digits().Match(question).Value;
        if (LastResult(conversation, Tools.LookUpOrder) is { } order)
        {
            if (order == "order not found")
                return Decision.AnswerWith($"I could not find order {number}. Could you check the number?");
            if (TrackingCode().Match(order) is { Success: true } code)
                return Decision.UseTool(Tools.TrackDelivery, code.Value);
            return Decision.AnswerWith($"Your order is {order}.");
        }

        return number.Length > 0
            ? Decision.UseTool(Tools.LookUpOrder, number)
            : Decision.AnswerWith("Could you tell me the order number?");
    }

    private static string? LastResult(string[] conversation, string tool) =>
        conversation.LastOrDefault(l => l.StartsWith(tool + "(", StringComparison.Ordinal)) is { } line
            ? line[(line.IndexOf(" => ", StringComparison.Ordinal) + 4)..]
            : null;

    [GeneratedRegex(@"\d{3,}")] private static partial Regex Digits();
    [GeneratedRegex(@"BR\d+")] private static partial Regex TrackingCode();
}

/// <summary>
/// Asks an OpenAI chat model for the next step. The key goes only in the request header: it is never logged nor
/// recorded. An answer outside the expected format is an invalid decision, not an error; an HTTP failure throws, so
/// skail retries the command.
/// </summary>
public sealed class OpenAIPlanner(HttpClient http, string apiKey, string model) : IPlanner
{
    private const string Prompt = $$"""
        You are a customer support agent. Decide the next step from the conversation.
        Tools: {{Tools.LookUpOrder}}(order number) and {{Tools.TrackDelivery}}(tracking code).
        Reply ONLY with JSON: {"tool": string|null, "argument": string|null, "answer": string|null}.
        Either use a tool or answer the customer, never both.
        """;

    public string Name => $"OpenAI {model}";

    public async Task<Decision> NextStepAsync(string[] conversation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
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

        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        try
        {
            using var completion = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var message = completion.RootElement.GetProperty("choices")[0].GetProperty("message");
            using var decision = JsonDocument.Parse(message.GetProperty("content").GetString() ?? "");
            var root = decision.RootElement;
            return new Decision(Text(root, "tool"), Text(root, "argument"), Text(root, "answer"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Decision.Empty;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
}
