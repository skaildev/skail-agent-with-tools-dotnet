using Skail.Platform.Runtime;
using Skail.Platform.Runtime.Threading;
using SkailAgentWithTools.Demo;

namespace SkailAgentWithTools;

public class SupportAgent(IPlanner planner, SimulatedOrderSystem orders, CaseDesk desk)
{
    private const string InvalidDecisionWarning = "system: invalid decision. Use a tool or answer the customer.";

    /// <summary>
    /// The agent loop: at each step the model decides to use a tool or to answer the customer. Each decision and each
    /// tool call is a recorded step, so after an app restart the case goes on without asking the model again for what
    /// it already decided. With no answer in 6 steps, the case goes to a person.
    /// The name and the parameters are the trigger contract: ["{caseId}", "{question}"].
    /// </summary>
    [SkailFunction]
    public async SkailTask<CaseResult> AssistCustomer(string caseId, string question)
    {
        var conversation = new List<string> { $"customer: {question}" };

        for (var step = 1; step <= AgentLimits.MaxSteps; step++)
        {
            // each call gets its own copy: the arguments of a command are recorded and must not change afterwards
            var decision = await DecideNextStep(caseId, step, conversation.ToArray());
            if (decision.Answer is not null)
                return await Answer(caseId, step, decision.Answer);

            if (decision.Tool is null)
            {
                // neither a tool nor an answer: the model gets a warning and decides again
                conversation.Add(InvalidDecisionWarning);
                continue;
            }

            var result = await UseTool(caseId, step, decision.Tool, decision.Argument ?? "");
            conversation.Add($"{decision.Tool}({decision.Argument}) => {result}");
        }

        return await EscalateToHuman(caseId, conversation.ToArray());
    }

    // The commands are the I/O. The function reads only their recorded results, so a resumed case rebuilds the
    // conversation without calling the model again.

    /// <summary>Asks the model for the next step. A failed call throws, and skail retries it.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask<Decision> DecideNextStep(string caseId, int step, string[] conversation)
    {
        var decision = await planner.NextStepAsync(conversation);
        desk.Decided(caseId, step, decision);
        return decision;
    }

    /// <summary>The tools only read data, so a retry has no side effect. An unknown tool is text for the model.</summary>
    [SkailCommand(retryCount: 3)]
    public async SkailTask<string> UseTool(string caseId, int step, string tool, string argument)
    {
        var result = tool switch
        {
            Tools.LookUpOrder => orders.LookUpOrder(argument),
            Tools.TrackDelivery => orders.TrackDelivery(argument),
            _ => $"unknown tool: {tool}",
        };
        desk.ToolUsed(caseId, step, tool, argument, result);
        return await Task.FromResult(result);
    }

    [SkailCommand]
    public async SkailTask<CaseResult> Answer(string caseId, int step, string answer)
    {
        var result = new CaseResult(caseId, Answered: true, answer, step);
        desk.Ended(result);
        return await Task.FromResult(result);
    }

    [SkailCommand]
    public async SkailTask<CaseResult> EscalateToHuman(string caseId, string[] conversation)
    {
        var result = new CaseResult(caseId, Answered: false,
            $"no answer in {AgentLimits.MaxSteps} steps: escalated to a person with {conversation.Length} conversation lines",
            AgentLimits.MaxSteps);
        desk.Ended(result);
        return await Task.FromResult(result);
    }
}
