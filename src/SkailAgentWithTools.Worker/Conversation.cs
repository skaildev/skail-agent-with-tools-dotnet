using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker;

/// <summary>
/// How the conversation lines are written. The lines go to skail's history on every model call,
/// so tool results are kept short.
/// </summary>
public static class Conversation
{
    public const string InvalidDecisionWarning = "system: invalid decision. Use a tool or answer the customer.";
    private const int MaxResult = 200;

    public static string CustomerLine(string question) => $"customer: {question}";

    public static string ToolLine(string tool, string? argument, string result)
    {
        var clipped = result.Length <= MaxResult ? result : result[..MaxResult] + "…";
        return $"{tool}({argument}) => {clipped}";
    }
}
