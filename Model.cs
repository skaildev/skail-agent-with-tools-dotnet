namespace SkailAgentWithTools;

/// <summary>The tools the model can use. Both only read data.</summary>
public static class Tools
{
    public const string LookUpOrder = "LookUpOrder";
    public const string TrackDelivery = "TrackDelivery";
}

public static class AgentLimits
{
    public const int MaxSteps = 6;
}

/// <summary>
/// What the model decided for a step: a tool (with its argument) or an answer to the customer.
/// A decision with neither is invalid: the function warns the model and asks again.
/// </summary>
public sealed record Decision(string? Tool, string? Argument, string? Answer)
{
    public static Decision Empty { get; } = new(null, null, null);

    public static Decision UseTool(string tool, string argument) => new(tool, argument, null);

    public static Decision AnswerWith(string answer) => new(null, null, answer);
}

/// <summary>How the case ended: answered by the agent, or escalated to a person with the conversation.</summary>
public sealed record CaseResult(string CaseId, bool Answered, string Text, int Steps);
