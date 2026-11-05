using SkailAgentWithTools.Contracts;

namespace SkailAgentWithTools.Worker.Progress;

public interface IProgressNotifier
{
    Task NotifyAsync(CaseEvent evt, CancellationToken ct = default);
}
