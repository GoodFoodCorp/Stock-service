using FluentAssertions;
using Stock.Domain.Entities;

namespace Stock.Domain.Tests;

public class OrderConsumptionTests
{
    [Fact]
    public void Cancel_on_applied_returns_lines_and_marks_cancelled()
    {
        var line = new ConsumptionLine(Guid.NewGuid(), 3m);
        var consumption = OrderConsumption.Applied(Guid.NewGuid(), Guid.NewGuid(), [line]);

        var toRestore = consumption.Cancel();

        toRestore.Should().Equal(line);
        consumption.Status.Should().Be(OrderConsumptionStatus.Cancelled);
    }

    [Fact]
    public void Cancel_twice_returns_empty_second_time()
    {
        var consumption = OrderConsumption.Applied(
            Guid.NewGuid(), Guid.NewGuid(), [new ConsumptionLine(Guid.NewGuid(), 1m)]);
        consumption.Cancel();

        consumption.Cancel().Should().BeEmpty();
        consumption.Status.Should().Be(OrderConsumptionStatus.Cancelled);
    }

    [Fact]
    public void CancelledBeforeConsume_has_no_lines_and_cancel_returns_empty()
    {
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var consumption = OrderConsumption.CancelledBeforeConsume(orderId, tenantId);

        consumption.OrderId.Should().Be(orderId);
        consumption.TenantId.Should().Be(tenantId);
        consumption.Status.Should().Be(OrderConsumptionStatus.Cancelled);
        consumption.Lines.Should().BeEmpty();
        consumption.Cancel().Should().BeEmpty();
    }

    [Fact]
    public void Applied_keeps_removed_quantities_as_given()
    {
        var a = new ConsumptionLine(Guid.NewGuid(), 3m);
        var b = new ConsumptionLine(Guid.NewGuid(), 0m);

        var consumption = OrderConsumption.Applied(Guid.NewGuid(), Guid.NewGuid(), [a, b]);

        consumption.Status.Should().Be(OrderConsumptionStatus.Applied);
        consumption.Lines.Should().Equal(a, b);
    }
}
