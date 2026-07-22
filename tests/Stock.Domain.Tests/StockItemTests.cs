using FluentAssertions;
using Stock.Domain.Entities;
using Stock.Domain.Errors;

namespace Stock.Domain.Tests;

public class StockItemTests
{
    private static StockItem NewItem(decimal onHand = 20, decimal min = 10, decimal max = 50) =>
        new(Guid.NewGuid(), "Tomatoes", "kg", onHand, min, max);

    [Fact]
    public void Constructor_rejects_invalid_thresholds()
    {
        var act = () => new StockItem(Guid.NewGuid(), "Tomatoes", "kg", 10, 20, 20);
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DomainErrorCode.Validation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_blank_name(string name)
    {
        var act = () => new StockItem(Guid.NewGuid(), name, "kg", 10, 1, 20);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void In_movement_increases_quantity()
    {
        var item = NewItem(onHand: 20);
        var result = item.ApplyMovement(MovementType.In, 5, "delivery", "user-1");

        item.QuantityOnHand.Should().Be(25);
        result.Movement.Type.Should().Be(MovementType.In);
        result.AutoReplenishment.Should().BeNull();
        item.Movements.Should().ContainSingle();
    }

    [Fact]
    public void Out_movement_decreases_quantity()
    {
        var item = NewItem(onHand: 20, min: 5);
        item.ApplyMovement(MovementType.Out, 8, "lunch service", "user-1");

        item.QuantityOnHand.Should().Be(12);
    }

    [Fact]
    public void Out_movement_cannot_exceed_stock()
    {
        var item = NewItem(onHand: 3);
        var act = () => item.ApplyMovement(MovementType.Out, 5, "oops", "user-1");

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DomainErrorCode.Conflict);
        item.QuantityOnHand.Should().Be(3, "failed movements must not change stock");
    }

    [Fact]
    public void Adjustment_sets_absolute_quantity()
    {
        var item = NewItem(onHand: 20);
        item.ApplyMovement(MovementType.Adjustment, 7, "inventory count", "user-1");

        item.QuantityOnHand.Should().Be(7);
    }

    [Theory]
    [InlineData(MovementType.In, 0)]
    [InlineData(MovementType.In, -1)]
    [InlineData(MovementType.Out, 0)]
    [InlineData(MovementType.Adjustment, -1)]
    public void Invalid_quantities_are_rejected(MovementType type, decimal quantity)
    {
        var item = NewItem();
        var act = () => item.ApplyMovement(type, quantity, "reason", "user-1");
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DomainErrorCode.Validation);
    }

    [Fact]
    public void Out_below_minimum_generates_automatic_replenishment_up_to_max()
    {
        var item = NewItem(onHand: 12, min: 10, max: 50);
        var result = item.ApplyMovement(MovementType.Out, 5, "dinner rush", "user-1");

        item.QuantityOnHand.Should().Be(7);
        result.AutoReplenishment.Should().NotBeNull();
        result.AutoReplenishment!.QuantityRequested.Should().Be(43, "should refill to ThresholdMax (50 - 7)");
        result.AutoReplenishment.Status.Should().Be(ReplenishmentStatus.Pending);
        result.AutoReplenishment.IsAutomatic.Should().BeTrue();
        result.AutoReplenishment.TenantId.Should().Be(item.TenantId);
    }

    [Fact]
    public void Out_staying_above_minimum_does_not_generate_replenishment()
    {
        var item = NewItem(onHand: 30, min: 10);
        var result = item.ApplyMovement(MovementType.Out, 5, "service", "user-1");

        result.AutoReplenishment.Should().BeNull();
    }

    [Fact]
    public void Receiving_replenishment_adds_requested_quantity()
    {
        var item = NewItem(onHand: 5, min: 10, max: 50);
        var request = new ReplenishmentRequest(item.TenantId, item.Id, 45, "user-1");

        item.ReceiveReplenishment(request, "admin-1");

        item.QuantityOnHand.Should().Be(50);
    }

    [Fact]
    public void Receiving_replenishment_of_another_item_is_rejected()
    {
        var item = NewItem();
        var foreign = new ReplenishmentRequest(item.TenantId, Guid.NewGuid(), 10, "user-1");

        var act = () => item.ReceiveReplenishment(foreign, "admin-1");
        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be(DomainErrorCode.Conflict);
    }
}
