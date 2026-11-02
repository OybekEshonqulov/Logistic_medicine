using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Stock;

namespace PharmaTrack.Api.Controllers;

[Authorize(Roles = Roles.CentralAll)]
public sealed class ReceiptsController(ReceiptService receipts) : ApiController
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public Task<PagedResult<ReceiptDto>> List([FromQuery] ReceiptQuery query, CancellationToken ct) => receipts.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ReceiptDto> Get(Guid id, CancellationToken ct) => receipts.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.CentralStaff)]
    public async Task<ActionResult<ReceiptDto>> Create(CreateReceiptRequest request, CancellationToken ct)
    {
        var receipt = await receipts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = receipt.Id }, receipt);
    }

    [HttpGet("import/template"), Authorize(Roles = Roles.CentralStaff)]
    public IActionResult ImportTemplate() => File(receipts.ImportTemplate(), ExcelType, "kirim-shablon.xlsx");

    [HttpPost("import"), Authorize(Roles = Roles.CentralStaff), Consumes("multipart/form-data")]
    public async Task<ActionResult<ReceiptDto>> Import(IFormFile file, [FromForm] string documentNumber, [FromForm] Guid supplierId, [FromForm] DateOnly receivedOn, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var receipt = await receipts.ImportAsync(stream, documentNumber, supplierId, receivedOn, ct);
        return CreatedAtAction(nameof(Get), new { id = receipt.Id }, receipt);
    }
}

public sealed class BatchesController(StockService stock) : ApiController
{
    [HttpGet]
    public Task<PagedResult<BatchDto>> List([FromQuery] BatchQuery query, CancellationToken ct) => stock.ListBatchesAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<BatchDto> Get(Guid id, CancellationToken ct) => stock.GetBatchAsync(id, ct);
}

public sealed class StockController(StockService stock) : ApiController
{
    [HttpGet("balances")]
    public Task<PagedResult<BalanceDto>> Balances([FromQuery] BalanceQuery query, CancellationToken ct) => stock.ListBalancesAsync(query, ct);

    [HttpGet("movements")]
    public Task<PagedResult<MovementDto>> Movements([FromQuery] MovementQuery query, CancellationToken ct) => stock.ListMovementsAsync(query, ct);
}

public sealed class ShipmentsController(ShipmentService shipments) : ApiController
{
    [HttpGet]
    public Task<PagedResult<ShipmentDto>> List([FromQuery] ShipmentQuery query, CancellationToken ct) => shipments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ShipmentDto> Get(Guid id, CancellationToken ct) => shipments.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public async Task<ActionResult<ShipmentDto>> Create(CreateShipmentRequest request, CancellationToken ct)
    {
        var shipment = await shipments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = shipment.Id }, shipment);
    }

    [HttpPost("{id:guid}/approve"), Authorize(Roles = Roles.CentralStaff)]
    public Task<ShipmentDto> Approve(Guid id, CancellationToken ct) => shipments.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/send"), Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public Task<ShipmentDto> Send(Guid id, CancellationToken ct) => shipments.SendAsync(id, ct);

    [HttpPost("{id:guid}/receive"), Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public Task<ShipmentDto> Receive(Guid id, ReceiveShipmentRequest request, CancellationToken ct) => shipments.ReceiveAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel"), Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public Task<ShipmentDto> Cancel(Guid id, CancellationToken ct) => shipments.CancelAsync(id, ct);

    [HttpPost("{id:guid}/items/{itemId:guid}/resolve-discrepancy"), Authorize(Roles = Roles.Admin)]
    public Task<ShipmentDto> ResolveDiscrepancy(Guid id, Guid itemId, ResolveDiscrepancyRequest request, CancellationToken ct) =>
        shipments.ResolveDiscrepancyAsync(id, itemId, request, ct);
}

public sealed class StockCountsController(StockCountService counts) : ApiController
{
    [HttpGet]
    public Task<PagedResult<StockCountDto>> List([FromQuery] StockCountQuery query, CancellationToken ct) => counts.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<StockCountDto> Get(Guid id, CancellationToken ct) => counts.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public async Task<ActionResult<StockCountDto>> Create(CreateStockCountRequest request, CancellationToken ct)
    {
        var count = await counts.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = count.Id }, count);
    }

    [HttpPost("{id:guid}/complete"), Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public Task<StockCountDto> Complete(Guid id, CompleteStockCountRequest request, CancellationToken ct) => counts.CompleteAsync(id, request, ct);
}

public sealed class AdjustmentsController(AdjustmentService adjustments) : ApiController
{
    [HttpGet]
    public Task<PagedResult<AdjustmentDto>> List([FromQuery] AdjustmentQuery query, CancellationToken ct) => adjustments.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AdjustmentDto> Get(Guid id, CancellationToken ct) => adjustments.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public async Task<ActionResult<AdjustmentDto>> Create(CreateAdjustmentRequest request, CancellationToken ct)
    {
        var adjustment = await adjustments.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = adjustment.Id }, adjustment);
    }

    [HttpPost("{id:guid}/approve"), Authorize(Roles = Roles.Admin)]
    public Task<AdjustmentDto> Approve(Guid id, CancellationToken ct) => adjustments.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/reject"), Authorize(Roles = Roles.Admin)]
    public Task<AdjustmentDto> Reject(Guid id, CancellationToken ct) => adjustments.RejectAsync(id, ct);
}
