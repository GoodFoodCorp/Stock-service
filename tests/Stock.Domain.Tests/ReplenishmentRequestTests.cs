using FluentAssertions;
using Stock.Domain.Entities;
using Stock.Domain.Errors;

namespace Stock.Domain.Tests;

public class ReplenishmentRequestTests
{
    private static ReplenishmentRequest NewRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 25, "manager-1");

    [Fact]
    public void New_request_starts_pending()
    {
        var request = NewRequest();
        request.Status.Should().Be(ReplenishmentStatus.Pending);
        request.IsAutomatic.Should().BeFalse();
    }

    [Fact]
    public void Zero_quantity_is_rejected()
    {
        var act = () => new ReplenishmentRequest(Guid.NewGuid(), Guid.NewGuid(), 0, "manager-1");
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(ReplenishmentStatus.Approved, true)]
    [InlineData(ReplenishmentStatus.Cancelled, true)]
    [InlineData(ReplenishmentStatus.Ordered, false)]
    [InlineData(ReplenishmentStatus.Received, false)]
    public void Pending_transitions(ReplenishmentStatus target, bool allowed)
    {
        var request = NewRequest();
        var act = () => request.TransitionTo(target);

        if (allowed)
        {
            act.Should().NotThrow();
            request.Status.Should().Be(target);
        }
        else
        {
            act.Should().Throw<DomainException>()
                .Which.Code.Should().Be(DomainErrorCode.InvalidTransition);
        }
    }

    [Fact]
    public void Full_lifecycle_pending_to_received()
    {
        var request = NewRequest();
        request.TransitionTo(ReplenishmentStatus.Approved);
        request.TransitionTo(ReplenishmentStatus.Ordered);
        request.TransitionTo(ReplenishmentStatus.Received);

        request.Status.Should().Be(ReplenishmentStatus.Received);
    }

    [Fact]
    public void Terminal_states_are_frozen()
    {
        var request = NewRequest();
        request.TransitionTo(ReplenishmentStatus.Cancelled);

        var act = () => request.TransitionTo(ReplenishmentStatus.Approved);
        act.Should().Throw<DomainException>();
    }
}
