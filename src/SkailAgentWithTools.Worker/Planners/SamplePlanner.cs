using System.Text.RegularExpressions;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker.Planners;

/// <summary>
/// The planner used without an OpenAI key: deterministic and offline. An optional delay per decision
/// gives time to stop the worker in the middle of a case.
/// </summary>
public sealed partial class SamplePlanner(TimeSpan? delay = null) : IPlanner
{
    public string Name => "Sample planner";

    public async Task<Decision> NextStepAsync(string[] conversation, CancellationToken ct = default)
    {
        if (delay is { } d && d > TimeSpan.Zero) await Task.Delay(d, ct);

        var question = conversation.FirstOrDefault(l => l.StartsWith("customer: ", StringComparison.Ordinal)) ?? "";

        // a complaint never gets an answer, so the case goes to a person
        if (question.Contains("complaint", StringComparison.OrdinalIgnoreCase)) return Decision.Empty;

        var delivery = LastResult(conversation, Tools.TrackDelivery);
        if (delivery is not null) return Decision.AnswerWith($"Your delivery is {delivery}.");

        var order = LastResult(conversation, Tools.LookUpOrder);
        if (order is not null)
        {
            if (order.Contains("not found", StringComparison.Ordinal))
                return Decision.AnswerWith($"I could not find order {FindOrderNumber(question)}. Could you check the number?");
            if (TrackingCode().Match(order) is { Success: true } code)
                return Decision.UseTool(Tools.TrackDelivery, code.Value);
            return Decision.AnswerWith($"Your order is {order}.");
        }

        return FindOrderNumber(question) is { Length: > 0 } number
            ? Decision.UseTool(Tools.LookUpOrder, number)
            : Decision.AnswerWith("Could you tell me the order number?");
    }

    private static string? LastResult(string[] conversation, string tool)
    {
        var prefix = tool + "(";
        var line = conversation.LastOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        var i = line?.IndexOf(" => ", StringComparison.Ordinal) ?? -1;
        return i < 0 ? null : line![(i + 4)..];
    }

    private static string FindOrderNumber(string question) => Digits().Match(question) is { Success: true } m ? m.Value : "";

    [GeneratedRegex(@"\d{3,}")] private static partial Regex Digits();
    [GeneratedRegex(@"BR\d+")] private static partial Regex TrackingCode();
}
