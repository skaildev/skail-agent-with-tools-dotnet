namespace SkailAgentWithTools.Demo;

/// <summary>The data the tools read, simulated. Not finding it is a result the model can act on, never an exception.</summary>
public sealed class SimulatedOrderSystem
{
    private static readonly Dictionary<string, string> Orders = new()
    {
        ["1234"] = "shipped, tracking code BR123456789",
        ["5678"] = "being prepared, no tracking code yet",
    };

    private static readonly Dictionary<string, string> Deliveries = new()
    {
        ["BR123456789"] = "in transit in São Paulo, arriving tomorrow",
    };

    public string LookUpOrder(string number) => Orders.GetValueOrDefault(number.Trim(), "order not found");

    public string TrackDelivery(string trackingCode) => Deliveries.GetValueOrDefault(trackingCode.Trim(), "no tracking events");
}
