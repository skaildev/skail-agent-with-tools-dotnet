namespace SkailAgentWithTools.Web.Skail;

public sealed class SkailGatewayException(string message, Exception? inner = null) : Exception(message, inner);

public interface ISkailGateway
{
    /// <summary>Starts the AssistCustomer function; <paramref name="caseId"/> is the execution id.</summary>
    Task TriggerAsync(string caseId, string question, CancellationToken ct = default);
}
