using System.Threading.Channels;
using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Web.Cases;

/// <summary>
/// In-memory state of the cases. The durable state lives in skail; this is only the UI view.
/// A step event is applied once (same case, stage and step), so a retried command does not count
/// a second model call.
/// </summary>
public sealed class CaseStore(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, CaseState> _cases = [];
    private readonly Dictionary<string, HashSet<(CaseStage, int)>> _seen = [];
    private readonly Dictionary<string, List<Channel<CaseState>>> _subscribers = [];

    public CaseState Create(string caseId, string question)
    {
        lock (_gate)
        {
            var state = new CaseState(caseId, question, CasePhase.Starting, 0, 0, null, [], null, null);
            _cases[caseId] = state;
            _seen[caseId] = [];
            return state;
        }
    }

    public CaseState? Get(string caseId)
    {
        lock (_gate) return _cases.GetValueOrDefault(caseId);
    }

    public void Remove(string caseId)
    {
        lock (_gate)
        {
            _cases.Remove(caseId);
            _seen.Remove(caseId);
            Complete(caseId);
        }
    }

    /// <returns>true when the event changed the case; false when ignored (unknown or finished, duplicate).</returns>
    public bool Apply(CaseEvent evt)
    {
        lock (_gate)
        {
            if (!_cases.TryGetValue(evt.CaseId, out var state) || state.IsTerminal) return false;
            if (!_seen[evt.CaseId].Add((evt.Stage, evt.Step))) return false;

            CaseState? next = evt.Stage switch
            {
                CaseStage.Started when state.Phase == CasePhase.Starting =>
                    Add(state with { Phase = CasePhase.Thinking, Planner = evt.Planner }, "Started", 0, $"Customer asked: {state.Question}"),
                CaseStage.ModelDecided when evt.Decision is { } d => Decided(state, evt.Step, d),
                CaseStage.ToolUsed =>
                    Add(state with { Phase = CasePhase.Thinking }, "ToolResult", evt.Step, $"Step {evt.Step}: tool result: {evt.Text}"),
                CaseStage.Answered when evt.Result is { } r =>
                    Add(state with { Phase = CasePhase.Answered, Result = r }, "Answer", evt.Step, $"Step {evt.Step}: answered the customer: {r.Text}"),
                CaseStage.Escalated when evt.Result is { } r =>
                    Add(state with { Phase = CasePhase.Escalated, Result = r }, "Handoff", evt.Step, $"No answer in {r.Steps} steps: escalated to a human"),
                CaseStage.Failed =>
                    Add(state with { Phase = CasePhase.Failed, Error = evt.Error ?? "unknown error" }, "Failed", 0, evt.Error ?? "unknown error"),
                _ => null,
            };
            if (next is null) return false;

            Publish(next);
            return true;
        }
    }

    public Subscription Subscribe(string caseId)
    {
        var channel = Channel.CreateUnbounded<CaseState>();
        lock (_gate)
        {
            if (!_subscribers.TryGetValue(caseId, out var list)) _subscribers[caseId] = list = [];
            list.Add(channel);
        }
        return new Subscription(channel.Reader, () =>
        {
            lock (_gate)
            {
                if (_subscribers.TryGetValue(caseId, out var list)) list.Remove(channel);
            }
        });
    }

    private CaseState Decided(CaseState state, int step, Decision d)
    {
        var next = state with { Step = step, ModelCalls = state.ModelCalls + 1 };
        if (!string.IsNullOrWhiteSpace(d.Answer))
            return Add(next with { Phase = CasePhase.Thinking }, "ModelAnswer", step, $"Step {step}: model answered: {d.Answer}");
        if (!string.IsNullOrWhiteSpace(d.Tool))
            return Add(next with { Phase = CasePhase.UsingTool }, "ModelTool", step, $"Step {step}: model chose {d.Tool}(\"{d.Argument}\")");
        return Add(next with { Phase = CasePhase.Thinking }, "Invalid", step, $"Step {step}: invalid model decision, asking again");
    }

    private CaseState Add(CaseState state, string kind, int step, string text) =>
        state with { Timeline = [.. state.Timeline, new TimelineEntry(kind, step, text, _clock.GetUtcNow())] };

    // must be called with _gate held
    private void Publish(CaseState state)
    {
        _cases[state.CaseId] = state;
        if (_subscribers.TryGetValue(state.CaseId, out var list))
            foreach (var channel in list) channel.Writer.TryWrite(state);
        if (state.IsTerminal) Complete(state.CaseId);
    }

    private void Complete(string caseId)
    {
        if (!_subscribers.TryGetValue(caseId, out var list)) return;
        foreach (var channel in list) channel.Writer.TryComplete();
    }

    public sealed class Subscription(ChannelReader<CaseState> reader, Action onDispose) : IDisposable
    {
        public ChannelReader<CaseState> Reader { get; } = reader;
        public void Dispose() => onDispose();
    }
}
