using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SkailAgentWithTools.Contracts;
using SkailAgentWithTools.Worker.Orders;
using SkailAgentWithTools.Worker.Planners;
using SkailAgentWithTools.Worker.Progress;

namespace SkailAgentWithTools.Worker;

/// <summary>
/// The I/O behind the commands, as plain tasks. Commands only run inside the skail runtime, so keeping the
/// logic here lets it be unit tested without skail. The progress report of a model decision or a tool is
/// best effort: if the web app is down it must not make skail retry the command (that would call the model again).
/// </summary>
public class AgentOperations(IPlanner planner, IOrderSystem orders, IProgressNotifier notifier, ILogger<AgentOperations> log)
{
    // the decision goes to the log as JSON, with accents kept readable
    private static readonly JsonSerializerOptions LogJson = new(Json.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Decision> DecideAsync(string caseId, int step, string[] conversation)
    {
        var decision = await planner.NextStepAsync(conversation);
        log.LogInformation("Case {CaseId} step {Step}: the model decided {Decision}", caseId, step, JsonSerializer.Serialize(decision, LogJson));
        await TryNotifyAsync(CaseEvent.ModelDecided(caseId, step, decision));
        return decision;
    }

    /// <summary>Read-only tools: a retry has no side effect. An unknown tool is text for the model, not an exception.</summary>
    public async Task<string> UseToolAsync(string caseId, int step, string tool, string argument)
    {
        var result = tool switch
        {
            Tools.LookUpOrder => orders.LookUpOrder(argument),
            Tools.TrackDelivery => orders.TrackDelivery(argument),
            _ => $"unknown tool: {tool}",
        };
        log.LogInformation("Case {CaseId} step {Step}: {Tool}({Argument}) => {Result}", caseId, step, tool, argument, result);
        await TryNotifyAsync(CaseEvent.ToolUsed(caseId, step, tool, argument, result));
        return result;
    }

    /// <summary>Idempotent: the result derives from the arguments, and the web app ignores a repeated stage.</summary>
    public async Task<CaseResult> AnswerAsync(string caseId, int step, string answer)
    {
        var result = CaseResult.Answered(caseId, answer, step);
        log.LogInformation("Case {CaseId} answered on step {Step}", caseId, step);
        await notifier.NotifyAsync(CaseEvent.Answered(caseId, result));
        return result;
    }

    /// <summary>Idempotent, like <see cref="AnswerAsync"/>.</summary>
    public async Task<CaseResult> EscalateAsync(string caseId, string[] conversation)
    {
        var result = CaseResult.Escalated(caseId, conversation);
        log.LogWarning("Case {CaseId} escalated to a human", caseId);
        await notifier.NotifyAsync(CaseEvent.Escalated(caseId, result));
        return result;
    }

    public Task ReportStatusAsync(CaseEvent evt) =>
        notifier.NotifyAsync(evt.Stage == CaseStage.Started ? evt with { Planner = planner.Name } : evt);

    private async Task TryNotifyAsync(CaseEvent evt)
    {
        try
        {
            await notifier.NotifyAsync(evt);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(e, "Could not report the progress of case {CaseId}", evt.CaseId);
        }
    }
}
