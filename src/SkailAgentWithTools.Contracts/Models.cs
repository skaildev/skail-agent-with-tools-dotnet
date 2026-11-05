using System.Text.Json.Serialization;

namespace SkailAgentWithTools.Contracts;

/// <summary>Tool names, shared by the model prompt, the planners and the command that runs them.</summary>
public static class Tools
{
    public const string LookUpOrder = "LookUpOrder";
    public const string TrackDelivery = "TrackDelivery";

    public static bool IsKnown(string? name) => name is LookUpOrder or TrackDelivery;
}

public static class AgentLimits
{
    public const int MaxSteps = 6;
    public const string DefaultMessage = "Where is my order 1234?";
    public const int MaxMessageLength = 300;
}

/// <summary>
/// What the model decided for a step: a tool (with its argument) or an answer to the customer.
/// An all-empty decision is an invalid one: the function warns the model and asks again.
/// </summary>
public sealed record Decision(string? Tool, string? Argument, string? Answer)
{
    public static Decision Empty { get; } = new(null, null, null);

    public static Decision UseTool(string tool, string argument) => new(tool, argument, null);

    public static Decision AnswerWith(string answer) => new(null, null, answer);

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Tool) && string.IsNullOrWhiteSpace(Answer);
}

public enum CaseEnd
{
    Answered,
    Escalated,
}

public sealed record CaseResult(
    string CaseId,
    CaseEnd End,
    string Text,
    int Steps,
    IReadOnlyList<string> Conversation)
{
    public static CaseResult Answered(string caseId, string answer, int steps) =>
        new(caseId, CaseEnd.Answered, answer, steps, []);

    public static CaseResult Escalated(string caseId, IReadOnlyList<string> conversation) =>
        new(caseId, CaseEnd.Escalated,
            $"no answer in {AgentLimits.MaxSteps} steps: case escalated to a human",
            AgentLimits.MaxSteps, conversation);
}

public enum CaseStage
{
    Started,
    ModelDecided,
    ToolUsed,
    Answered,
    Escalated,
    Failed,
}

/// <summary>Progress reported by the worker to the web app (the page is only a view; skail holds the state).</summary>
public sealed record CaseEvent(
    string CaseId,
    CaseStage Stage,
    int Step,
    string? Text,
    Decision? Decision,
    string? Tool,
    string? Argument,
    string? Planner,
    CaseResult? Result,
    string? Error)
{
    public static CaseEvent Started(string caseId, string question) =>
        new(caseId, CaseStage.Started, 0, question, null, null, null, null, null, null);

    public static CaseEvent ModelDecided(string caseId, int step, Decision decision) =>
        new(caseId, CaseStage.ModelDecided, step, null, decision, null, null, null, null, null);

    public static CaseEvent ToolUsed(string caseId, int step, string tool, string argument, string result) =>
        new(caseId, CaseStage.ToolUsed, step, result, null, tool, argument, null, null, null);

    public static CaseEvent Answered(string caseId, CaseResult result) =>
        new(caseId, CaseStage.Answered, result.Steps, null, null, null, null, null, result, null);

    public static CaseEvent Escalated(string caseId, CaseResult result) =>
        new(caseId, CaseStage.Escalated, result.Steps, null, null, null, null, null, result, null);

    public static CaseEvent Failed(string caseId, string error) =>
        new(caseId, CaseStage.Failed, 0, null, null, null, null, null, null, error);
}
