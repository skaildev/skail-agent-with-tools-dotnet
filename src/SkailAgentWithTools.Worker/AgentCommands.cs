using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker;

/// <summary>The I/O of the agent. Each command does one thing, because a retry runs the whole command again.</summary>
public class AgentCommands(AgentOperations operations)
{
    /// <summary>Asks the model for the next step. The decision is recorded, so a resumed case does not ask again.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask<Decision> DecideNextStep(string caseId, int step, string[] conversation)
        => await operations.DecideAsync(caseId, step, conversation);

    [SkailCommand(retryCount: 3)]
    public async SkailTask<string> UseTool(string caseId, int step, string tool, string argument)
        => await operations.UseToolAsync(caseId, step, tool, argument);

    [SkailCommand(retryCount: 3)]
    public async SkailTask<CaseResult> Answer(string caseId, int step, string answer)
        => await operations.AnswerAsync(caseId, step, answer);

    [SkailCommand(retryCount: 3)]
    public async SkailTask<CaseResult> EscalateToHuman(string caseId, string[] conversation)
        => await operations.EscalateAsync(caseId, conversation);

    /// <summary>Idempotent: the web app ignores a repeated event (same case, stage and step).</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask ReportStatus(CaseEvent evt)
        => await operations.ReportStatusAsync(evt);
}
