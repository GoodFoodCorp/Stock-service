using FluentAssertions;
using Moq;
using Stock.Application.Orders;
using Stock.Domain.Entities;
using Stock.Domain.Errors;
using Stock.Domain.Repositories;

namespace Stock.Application.Tests;

public class OrderStockHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly Mock<IStockItemRepository> _stocks = new();
    private readonly Mock<IReplenishmentRequestRepository> _replenishments = new();
    private readonly Mock<IOrderConsumptionRepository> _consumptions = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private void SetupItems(params StockItem[] items) =>
        _stocks.Setup(s => s.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Guid> ids, CancellationToken _) => items.Where(i => ids.Contains(i.Id)).ToList());

    private CheckAvailabilityHandler CheckHandler => new(_stocks.Object);

    private ConsumeOrderHandler ConsumeHandler => new(_stocks.Object, _replenishments.Object, _consumptions.Object, _uow.Object);

    private RestoreOrderHandler RestoreHandler => new(_stocks.Object, _consumptions.Object, _uow.Object);

    // ---- Check ----

    [Fact]
    public async Task Check_available_when_all_items_have_enough()
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        var b = new StockItem(TenantId, "B", "L", 5, 1, 50);
        SetupItems(a, b);

        var result = await CheckHandler.Handle(new CheckAvailabilityQuery(TenantId,
            [new(a.Id, 10), new(b.Id, 2.5m)]), default);

        result.Available.Should().BeTrue();
        result.Missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_lists_short_item_with_name_unit_required_onhand()
    {
        var a = new StockItem(TenantId, "Flour", "kg", 3, 1, 50);
        SetupItems(a);

        var result = await CheckHandler.Handle(new CheckAvailabilityQuery(TenantId, [new(a.Id, 5)]), default);

        result.Available.Should().BeFalse();
        result.Missing.Should().ContainSingle().Which.Should()
            .Be(new MissingItemDto(a.Id, "Flour", "kg", 5, 3));
    }

    [Fact]
    public async Task Check_treats_unknown_item_as_missing_with_zero_onhand()
    {
        SetupItems();
        var unknown = Guid.NewGuid();

        var result = await CheckHandler.Handle(new CheckAvailabilityQuery(TenantId, [new(unknown, 2)]), default);

        result.Available.Should().BeFalse();
        var m = result.Missing.Should().ContainSingle().Subject;
        m.StockItemId.Should().Be(unknown);
        m.Required.Should().Be(2);
        m.OnHand.Should().Be(0);
    }

    [Fact]
    public async Task Check_treats_other_tenant_item_as_missing_without_leaking_its_name()
    {
        var foreign = new StockItem(Guid.NewGuid(), "Secret sauce", "L", 100, 1, 200);
        SetupItems(foreign);

        var result = await CheckHandler.Handle(new CheckAvailabilityQuery(TenantId, [new(foreign.Id, 1)]), default);

        result.Available.Should().BeFalse();
        var m = result.Missing.Should().ContainSingle().Subject;
        m.Name.Should().NotContain("Secret");
        m.OnHand.Should().Be(0);
    }

    // ---- Consume ----

    [Fact]
    public async Task Consume_decrements_all_items_and_stores_removed_quantities()
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        var b = new StockItem(TenantId, "B", "kg", 8, 1, 50);
        SetupItems(a, b);
        var orderId = Guid.NewGuid();
        OrderConsumption? stored = null;
        _consumptions.Setup(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()))
            .Callback<OrderConsumption, CancellationToken>((c, _) => stored = c);

        var result = await ConsumeHandler.Handle(new ConsumeOrderCommand(orderId, TenantId,
            [new(a.Id, 4), new(b.Id, 1.5m)]), default);

        result.Status.Should().Be("APPLIED");
        result.Shortfalls.Should().BeEmpty();
        a.QuantityOnHand.Should().Be(6);
        b.QuantityOnHand.Should().Be(6.5m);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(OrderConsumptionStatus.Applied);
        stored.Lines.Should().BeEquivalentTo(new[] { new ConsumptionLine(a.Id, 4), new ConsumptionLine(b.Id, 1.5m) });
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_twice_is_noop_second_time()
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrderConsumption.Applied(orderId, TenantId, [new(a.Id, 4)]));

        var result = await ConsumeHandler.Handle(new ConsumeOrderCommand(orderId, TenantId, [new(a.Id, 4)]), default);

        result.Status.Should().Be("ALREADY_APPLIED");
        a.QuantityOnHand.Should().Be(10);
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
        _consumptions.Verify(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_after_cancel_is_skipped()
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrderConsumption.CancelledBeforeConsume(orderId, TenantId));

        var result = await ConsumeHandler.Handle(new ConsumeOrderCommand(orderId, TenantId, [new(a.Id, 4)]), default);

        result.Status.Should().Be("SKIPPED_CANCELLED");
        a.QuantityOnHand.Should().Be(10);
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_reports_shortfall_and_stores_only_removed_quantity()
    {
        var a = new StockItem(TenantId, "A", "kg", 3, 1, 50);
        SetupItems(a);
        OrderConsumption? stored = null;
        _consumptions.Setup(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()))
            .Callback<OrderConsumption, CancellationToken>((c, _) => stored = c);

        var result = await ConsumeHandler.Handle(new ConsumeOrderCommand(Guid.NewGuid(), TenantId, [new(a.Id, 5)]), default);

        result.Status.Should().Be("APPLIED");
        result.Shortfalls.Should().ContainSingle().Which.Should().Be(new ShortfallDto(a.Id, 2));
        a.QuantityOnHand.Should().Be(0);
        stored!.Lines.Should().ContainSingle().Which.Quantity.Should().Be(3);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Consume_creates_automatic_replenishment_once_when_below_minimum(bool pending, int expectedAdds)
    {
        var a = new StockItem(TenantId, "A", "kg", 6, 5, 50);
        SetupItems(a);
        _replenishments.Setup(r => r.HasPendingForItemAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pending);

        await ConsumeHandler.Handle(new ConsumeOrderCommand(Guid.NewGuid(), TenantId, [new(a.Id, 2)]), default);

        _replenishments.Verify(r => r.AddAsync(
            It.Is<ReplenishmentRequest>(x => x.IsAutomatic && x.StockItemId == a.Id),
            It.IsAny<CancellationToken>()), Times.Exactly(expectedAdds));
    }

    [Fact]
    public async Task Consume_unknown_or_foreign_item_throws_NotFound_and_saves_nothing()
    {
        var ok = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        var foreign = new StockItem(Guid.NewGuid(), "Secret sauce", "L", 100, 1, 200);
        SetupItems(ok, foreign);

        var act = () => ConsumeHandler.Handle(new ConsumeOrderCommand(Guid.NewGuid(), TenantId,
            [new(ok.Id, 1), new(foreign.Id, 1)]), default);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be(DomainErrorCode.NotFound);
        ex.Which.Message.Should().NotContain("Secret");
        ok.QuantityOnHand.Should().Be(10);
        _consumptions.Verify(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_sums_duplicate_lines_of_the_same_item()
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        SetupItems(a);

        await ConsumeHandler.Handle(new ConsumeOrderCommand(Guid.NewGuid(), TenantId, [new(a.Id, 2), new(a.Id, 3)]), default);

        a.QuantityOnHand.Should().Be(5);
    }

    // ---- Restore ----

    [Fact]
    public async Task Restore_returns_exactly_removed_quantities()
    {
        var a = new StockItem(TenantId, "A", "kg", 0, 0, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        var record = OrderConsumption.Applied(orderId, TenantId, [new(a.Id, 3)]);
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        result.Status.Should().Be("RESTORED");
        a.QuantityOnHand.Should().Be(3);
        record.Status.Should().Be(OrderConsumptionStatus.Cancelled);
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Restore_skips_zero_quantity_lines()
    {
        var a = new StockItem(TenantId, "A", "kg", 0, 0, 50);
        var b = new StockItem(TenantId, "B", "kg", 1, 0, 50);
        SetupItems(a, b);
        var orderId = Guid.NewGuid();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrderConsumption.Applied(orderId, TenantId, [new(a.Id, 0), new(b.Id, 2)]));

        var result = await RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        result.Status.Should().Be("RESTORED");
        a.QuantityOnHand.Should().Be(0);
        b.QuantityOnHand.Should().Be(3);
    }

    [Fact]
    public async Task Restore_twice_is_noop()
    {
        var a = new StockItem(TenantId, "A", "kg", 5, 0, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        var record = OrderConsumption.Applied(orderId, TenantId, [new(a.Id, 3)]);
        record.Cancel();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        result.Status.Should().Be("ALREADY_RESTORED");
        a.QuantityOnHand.Should().Be(5);
        _stocks.Verify(s => s.AddMovementAsync(It.IsAny<StockMovement>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Restore_without_record_stores_tombstone()
    {
        var orderId = Guid.NewGuid();

        var result = await RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        result.Status.Should().Be("CANCELLED_BEFORE_CONSUME");
        _consumptions.Verify(c => c.AddAsync(
            It.Is<OrderConsumption>(x => x.OrderId == orderId && x.TenantId == TenantId
                && x.Status == OrderConsumptionStatus.Cancelled && x.Lines.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- Chain ----

    [Fact]
    public async Task Partial_shortfall_then_cancel_restores_only_what_was_consumed()
    {
        var a = new StockItem(TenantId, "A", "kg", 3, 0, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        OrderConsumption? stored = null;
        _consumptions.Setup(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()))
            .Callback<OrderConsumption, CancellationToken>((c, _) => stored = c);

        var consumed = await ConsumeHandler.Handle(new ConsumeOrderCommand(orderId, TenantId, [new(a.Id, 5)]), default);
        consumed.Shortfalls.Should().ContainSingle().Which.Shortfall.Should().Be(2);
        a.QuantityOnHand.Should().Be(0);

        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        var restored = await RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        restored.Status.Should().Be("RESTORED");
        a.QuantityOnHand.Should().Be(3);
    }

    // ---- Tenant isolation ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consume_record_of_another_tenant_throws_NotFound(bool cancelled)
    {
        var a = new StockItem(TenantId, "A", "kg", 10, 1, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        var other = Guid.NewGuid();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cancelled ? OrderConsumption.CancelledBeforeConsume(orderId, other)
                                    : OrderConsumption.Applied(orderId, other, [new(a.Id, 1)]));

        var act = () => ConsumeHandler.Handle(new ConsumeOrderCommand(orderId, TenantId, [new(a.Id, 4)]), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.NotFound);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Restore_record_of_another_tenant_throws_NotFound_and_changes_nothing()
    {
        var a = new StockItem(TenantId, "A", "kg", 5, 0, 50);
        SetupItems(a);
        var orderId = Guid.NewGuid();
        var record = OrderConsumption.Applied(orderId, Guid.NewGuid(), [new(a.Id, 3)]);
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var act = () => RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.NotFound);
        a.QuantityOnHand.Should().Be(5);
        record.Status.Should().Be(OrderConsumptionStatus.Applied);
        _consumptions.Verify(c => c.AddAsync(It.IsAny<OrderConsumption>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Restore_does_not_touch_items_of_another_tenant()
    {
        var foreign = new StockItem(Guid.NewGuid(), "A", "kg", 5, 0, 50);
        SetupItems(foreign);
        var orderId = Guid.NewGuid();
        _consumptions.Setup(c => c.GetByOrderIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrderConsumption.Applied(orderId, TenantId, [new(foreign.Id, 3)]));

        var act = () => RestoreHandler.Handle(new RestoreOrderCommand(orderId, TenantId), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(DomainErrorCode.NotFound);
        foreign.QuantityOnHand.Should().Be(5);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
