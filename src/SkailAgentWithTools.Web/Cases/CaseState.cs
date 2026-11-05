using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Web.Cases;

public enum CasePhase
{
    Starting,
    Thinking,
    UsingTool,
    Answered,
    Escalated,
    Failed,
}

public sealed record TimelineEntry(string Kind, int Step, string Text, DateTimeOffset At);

public sealed record CaseState(
    string CaseId,
    string Question,
    CasePhase Phase,
    int Step,
    int ModelCalls,
    string? Planner,
    IReadOnlyList<TimelineEntry> Timeline,
    CaseResult? Result,
    string? Error)
{
    public bool IsTerminal => Phase is CasePhase.Answered or CasePhase.Escalated or CasePhase.Failed;
    public bool Active => !IsTerminal;
}
