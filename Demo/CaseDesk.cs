using SkailAgentWithTools.Skail;

namespace SkailAgentWithTools.Demo;

/// <summary>
/// Starts the cases for the page and keeps what it shows: each step of the agent and how the case ended. A case the
/// app does not know (it was started before the app restarted) shows up with its next step.
/// </summary>
public sealed class CaseDesk(SkailApiClient skail, IPlanner planner)
{
    private sealed record Step(int Number, string Kind, string Text);

    private sealed class Case(string id, string question)
    {
        public string Id { get; } = id;
        public string Question { get; } = question;
        public List<Step> Steps { get; } = [];
        public int ModelCalls { get; set; }
        public CaseResult? Result { get; set; }
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Case> cases = [];
    private readonly List<Case> order = [];

    public async Task StartAsync(string question, CancellationToken ct)
    {
        var caseId = Guid.NewGuid().ToString();
        await skail.TriggerAsync(caseId, question, ct);
        lock (gate) Add(caseId, question);
        Print(caseId, $"customer: {question}");
    }

    public void Decided(string caseId, int step, Decision decision)
    {
        var text = decision.Answer is not null ? "answer the customer"
            : decision.Tool is not null ? $"use {decision.Tool}({decision.Argument})"
            : "neither use a tool nor answer";
        lock (gate)
        {
            var c = Find(caseId);
            c.ModelCalls++;
            c.Steps.Add(new Step(step, "model", text));
        }
        Print(caseId, $"step {step}: the model decided to {text}");
    }

    public void ToolUsed(string caseId, int step, string tool, string argument, string result)
    {
        lock (gate) Find(caseId).Steps.Add(new Step(step, "tool", $"{tool}({argument}) → {result}"));
        Print(caseId, $"step {step}: {tool}({argument}) → {result}");
    }

    public void Ended(CaseResult result)
    {
        lock (gate) Find(result.CaseId).Result = result;
        Print(result.CaseId, result.Answered ? $"answered: {result.Text}" : $"escalated: {result.Text}");
    }

    /// <summary>What the page shows: the planner and the cases, the latest first.</summary>
    public object View()
    {
        lock (gate)
        {
            return new
            {
                planner = planner.Name,
                cases = order.AsEnumerable().Reverse().Take(5).Select(c => new
                {
                    c.Id, c.Question, c.ModelCalls, c.Result,
                    steps = c.Steps.Select(s => new { s.Number, s.Kind, s.Text }),
                }),
            };
        }
    }

    // a case started before the app restarted: the page follows it from its next step
    private Case Find(string caseId) =>
        cases.TryGetValue(caseId, out var c) ? c : Add(caseId, "(started before the app restarted)");

    private Case Add(string caseId, string question)
    {
        var c = cases[caseId] = new Case(caseId, question);
        order.Add(c);
        return c;
    }

    private static void Print(string caseId, string message) => Console.WriteLine($"[{caseId[..8]}] {message}");
}
