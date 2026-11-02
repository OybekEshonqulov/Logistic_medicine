using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Sales;
using PharmaTrack.Application.Features.Sync;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Api.Controllers;

public sealed class SalesController(SaleService sales) : ApiController
{
    [HttpGet]
    public Task<PagedResult<SaleDto>> List([FromQuery] SaleQuery query, CancellationToken ct) => sales.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<SaleDto> Get(Guid id, CancellationToken ct) => sales.GetAsync(id, ct);

    /// <summary>The client generates one Idempotency-Key per sale and reuses it on every retry. A retry returns 200 with the original sale.</summary>
    [HttpPost, Authorize(Roles = Roles.Dealer)]
    public async Task<ActionResult<SaleDto>> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CreateSaleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new DomainException(ErrorCodes.IdempotencyKeyRequired);

        var result = await sales.CreateAsync(idempotencyKey, request, ct);
        return result.AlreadyExisted
            ? Ok(result.Sale)
            : CreatedAtAction(nameof(Get), new { id = result.Sale.Id }, result.Sale);
    }

    [HttpPost("{id:guid}/reverse"), Authorize(Roles = $"{Roles.Admin},{Roles.Dealer}")]
    public Task<SaleDto> Reverse(Guid id, ReverseSaleRequest request, CancellationToken ct) => sales.ReverseAsync(id, request, ct);
}

[Authorize(Roles = Roles.Dealer)]
public sealed class SyncController(SyncService sync) : ApiController
{
    [HttpGet("references")]
    public Task<SyncResponse> References([FromQuery] DateTimeOffset? updatedSince, CancellationToken ct) => sync.GetAsync(updatedSince, ct);
}
