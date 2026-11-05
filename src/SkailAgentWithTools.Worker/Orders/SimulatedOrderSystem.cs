namespace SkailAgentWithTools.Worker.Orders;

/// <summary>The data the tools read. Simulated: the demo has no real order system.</summary>
public interface IOrderSystem
{
    string LookUpOrder(string number);
    string TrackDelivery(string trackingCode);
}

public sealed class SimulatedOrderSystem : IOrderSystem
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

    public string LookUpOrder(string number) => Lookup(Orders, number, "order not found");

    public string TrackDelivery(string trackingCode) => Lookup(Deliveries, trackingCode, "no tracking events");

    // not finding the data is a business result the model can act on, never an exception
    private static string Lookup(Dictionary<string, string> data, string key, string notFound)
    {
        key = key.Trim();
        if (key.Length == 0) return "empty argument";
        return data.GetValueOrDefault(key, notFound);
    }
}
