using SkailAgentWithTools.Worker.Orders;

namespace SkailAgentWithTools.Tests;

public class OrderSystemTests
{
    private readonly SimulatedOrderSystem _orders = new();

    [Fact]
    public void Shipped_order_mentions_the_tracking_code() =>
        Assert.Contains("BR123456789", _orders.LookUpOrder("1234"));

    [Fact]
    public void Order_in_preparation_has_no_tracking_code() =>
        Assert.DoesNotContain("BR", _orders.LookUpOrder("5678"));

    [Theory]
    [InlineData("9999")]
    [InlineData(" 9999 ")]
    public void Unknown_order_is_a_result_not_an_exception(string number) =>
        Assert.Equal("order not found", _orders.LookUpOrder(number));

    [Fact]
    public void Order_number_is_trimmed() =>
        Assert.Equal(_orders.LookUpOrder("1234"), _orders.LookUpOrder(" 1234 "));

    [Fact]
    public void Known_tracking_code_has_a_delivery_forecast() =>
        Assert.Contains("arriving", _orders.TrackDelivery("BR123456789"));

    [Fact]
    public void Unknown_tracking_code_has_no_events() =>
        Assert.Equal("no tracking events", _orders.TrackDelivery("BR000"));

    [Fact]
    public void Empty_argument_is_reported_back_to_the_model()
    {
        Assert.Equal("empty argument", _orders.LookUpOrder(""));
        Assert.Equal("empty argument", _orders.TrackDelivery("  "));
    }
}
