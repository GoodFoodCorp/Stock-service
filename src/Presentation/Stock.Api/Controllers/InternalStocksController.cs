using MediatR;
using Microsoft.AspNetCore.Mvc;
using Stock.Application.Orders;
using Stock.Domain.Entities;

namespace Stock.Api.Controllers;

/// <summary>Service-to-service API called by order-service. Protected by the
/// <c>X-Service-Key</c> header, never exposed under <c>/api</c> (the ingress
/// only routes <c>/api/...</c>), so it stays internal to the cluster.</summary>
[ApiController]
[Route("internal/stocks")]
// Fully qualified: the .NET 8 SDK also ships Microsoft.Extensions.DependencyInjection.ServiceKeyAttribute.
[Stock.Api.Auth.ServiceKey]
public sealed class InternalStocksController(IMediator mediator) : ControllerBase
{
    public sealed record CheckBody(Guid TenantId, IReadOnlyList<ConsumptionLine> Lines);

    public sealed record ConsumeBody(Guid OrderId, Guid TenantId, IReadOnlyList<ConsumptionLine> Lines);

    public sealed record RestoreBody(Guid OrderId, Guid TenantId);

    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] CheckBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new CheckAvailabilityQuery(body.TenantId, body.Lines), ct));

    [HttpPost("consume")]
    public async Task<IActionResult> Consume([FromBody] ConsumeBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new ConsumeOrderCommand(body.OrderId, body.TenantId, body.Lines), ct));

    [HttpPost("restore")]
    public async Task<IActionResult> Restore([FromBody] RestoreBody body, CancellationToken ct) =>
        Ok(await mediator.Send(new RestoreOrderCommand(body.OrderId, body.TenantId), ct));
}
