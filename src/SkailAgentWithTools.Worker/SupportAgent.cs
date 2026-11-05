using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker;

public class SupportAgent(AgentCommands commands)
{
    /// <summary>
    /// The agent loop: at each step the model decides to use a tool or to answer the customer. Each decision and
    /// each tool call is a recorded step, so after a crash the case resumes without asking the model again for what
    /// it already decided. With no answer in 6 steps, the case goes to a person.
    /// </summary>
    [SkailFunction]
    public async SkailTask<CaseResult> AssistCustomer(string caseId, string question)
    {
        try
        {
            await commands.ReportStatus(CaseEvent.Started(caseId, question));
            var conversation = new List<string> { Conversation.CustomerLine(question) };

            for (var step = 1; step <= AgentLimits.MaxSteps; step++)
            {
                var decision = await commands.DecideNextStep(caseId, step, conversation.ToArray());
                if (decision.Answer is not null)
                    return await commands.Answer(caseId, step, decision.Answer);

                if (decision.Tool is null)
                {
                    // Neither a tool nor an answer: the model gets a warning and decides again.
                    conversation.Add(Conversation.InvalidDecisionWarning);
                    continue;
                }

                var result = await commands.UseTool(caseId, step, decision.Tool, decision.Argument ?? "");
                conversation.Add(Conversation.ToolLine(decision.Tool, decision.Argument, result));
            }

            return await commands.EscalateToHuman(caseId, conversation.ToArray());
        }
        catch (Exception ex) when (ex is not SkailNonDeterministicException)
        {
            await commands.ReportStatus(CaseEvent.Failed(caseId, ex.Message));
            throw;
        }
    }
}
