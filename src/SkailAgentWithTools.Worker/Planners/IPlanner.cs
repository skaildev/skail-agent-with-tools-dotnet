using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker.Planners;

/// <summary>Decides the next step from the conversation so far. Runs only inside the DecideNextStep command.</summary>
public interface IPlanner
{
    /// <summary>Shown on the page.</summary>
    string Name { get; }

    Task<Decision> NextStepAsync(string[] conversation, CancellationToken ct = default);
}
