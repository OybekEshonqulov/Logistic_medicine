using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed record ShipmentItemRequest(Guid BatchId, int Quantity);
public sealed record CreateShipmentRequest(Guid? ToDealerId, string? Note, IReadOnlyList<ShipmentItemRequest> Items);
public sealed record ReceiveItemRequest(Guid ItemId, int ReceivedQuantity, string? Reason);
public sealed record ReceiveShipmentRequest(IReadOnlyList<ReceiveItemRequest> Items);
public sealed record ResolveDiscrepancyRequest(DiscrepancyResolution Resolution, string? Note);
public sealed record ShipmentItemDto(Guid Id, Guid BatchId, string BatchNumber, DateOnly ExpiryDate, Guid ProductId, string ProductName, int SentQuantity, int? ReceivedQuantity, DiscrepancyStatus DiscrepancyStatus, string? DiscrepancyReason, DiscrepancyResolution? Resolution, string? ResolutionNote);
public sealed record ShipmentDto(Guid Id, long Number, Guid FromLocationId, string FromLocationName, Guid ToLocationId, string ToLocationName, ShipmentStatus Status, string? Note, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? SentAt, DateTimeOffset? ReceivedAt, IReadOnlyList<ShipmentItemDto> Items);
public sealed record ShipmentQuery : PageQuery
{
    public ShipmentStatus? Status { get; init; }
    public Guid? DealerId { get; init; }
    public bool? HasOpenDiscrepancy { get; init; }
}

public sealed class CreateShipmentRequestValidator : AbstractValidator<CreateShipmentRequest>
{
    public CreateShipmentRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items).Must(items => items.Select(i => i.BatchId).Distinct().Count() == items.Count)
            .When(x => x.Items is not null)
            .WithMessage(_ => Texts.DuplicateBatch.Current);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.BatchId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ReceiveShipmentRequestValidator : AbstractValidator<ReceiveShipmentRequest>
{
    public ReceiveShipmentRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ItemId).NotEmpty();
            item.RuleFor(i => i.ReceivedQuantity).GreaterThanOrEqualTo(0);
            item.RuleFor(i => i.Reason).MaximumLength(500);
        });
    }
}

public sealed class ResolveDiscrepancyRequestValidator : AbstractValidator<ResolveDiscrepancyRequest>
{
    public ResolveDiscrepancyRequestValidator()
    {
        RuleFor(x => x.Resolution).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class ShipmentService(
    IAppDbContext db,
    IStockLedger ledger,
    ICurrentUser currentUser,
    LocationService locations,
    TimeProvider clock)
{
    public async Task<PagedResult<ShipmentDto>> ListAsync(ShipmentQuery query, CancellationToken ct)
    {
        var shipments = await ScopedAsync(ct);
        if (query.Status is { } status) shipments = shipments.Where(s => s.Status == status);
        if (query.DealerId is { } dealerId)
            shipments = shipments.Where(s => s.FromLocation.DealerId == dealerId || s.ToLocation.DealerId == dealerId);
        if (query.HasOpenDiscrepancy == true)
            shipments = shipments.Where(s => s.Items.Any(i => i.DiscrepancyStatus == DiscrepancyStatus.Open));

        return await Project(shipments.OrderByDescending(s => s.Number)).ToPagedAsync(query, ct);
    }

    public async Task<ShipmentDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project((await ScopedAsync(ct)).Where(s => s.Id == id)).FirstOrNotFoundAsync(ct);

    /// <summary>Central staff ship to a dealer; a dealer can only return goods to the centre.</summary>
    public async Task<ShipmentDto> CreateAsync(CreateShipmentRequest request, CancellationToken ct)
    {
        var (from, to) = await ResolveRouteAsync(request.ToDealerId, ct);

        var batchIds = request.Items.Select(i => i.BatchId).ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToListAsync(ct);
        if (batches.Count != batchIds.Count) throw new DomainException(ErrorCodes.NotFound);

        var today = clock.Today();
        var goingToDealer = to != StockLocation.CentralId;
        var expired = batches.FirstOrDefault(b => goingToDealer && b.ExpiryDate < today);
        if (expired is not null) throw new DomainException(ErrorCodes.BatchExpired, expired.BatchNumber);

        var shipment = new Shipment
        {
            FromLocationId = from,
            ToLocationId = to,
            Note = request.Note,
            CreatedById = currentUser.UserId,
            Items = request.Items.Select(i => new ShipmentItem { BatchId = i.BatchId, SentQuantity = i.Quantity }).ToList()
        };
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync(ct);
        return await GetAsync(shipment.Id, ct);
    }

    public async Task<ShipmentDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
        shipment.Approve(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Stock leaves the sender here and stays "in transit" until the recipient confirms.</summary>
    public async Task<ShipmentDto> SendAsync(Guid id, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var shipment = await db.Shipments.Include(s => s.Items).Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
            await EnsureActsForAsync(shipment.FromLocationId, ct);

            shipment.MarkSent(clock.GetUtcNow());
            foreach (var item in shipment.Items)
                await ledger.ApplyAsync(shipment.FromLocationId, item.BatchId, -item.SentQuantity, MovementType.ShipmentOut, shipment.Id, ct);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<ShipmentDto> ReceiveAsync(Guid id, ReceiveShipmentRequest request, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var shipment = await db.Shipments.Include(s => s.Items).Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
            await EnsureActsForAsync(shipment.ToLocationId, ct);

            var received = request.Items.ToDictionary(i => i.ItemId);
            if (received.Count != shipment.Items.Count || shipment.Items.Any(i => !received.ContainsKey(i.Id)))
                throw new DomainException(ErrorCodes.ReceiveItemsMismatch);

            shipment.MarkReceived(clock.GetUtcNow());
            foreach (var item in shipment.Items)
            {
                var line = received[item.Id];
                item.Receive(line.ReceivedQuantity, line.Reason);
                if (line.ReceivedQuantity > 0)
                    await ledger.ApplyAsync(shipment.ToLocationId, item.BatchId, line.ReceivedQuantity, MovementType.ShipmentIn, shipment.Id, ct);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<ShipmentDto> CancelAsync(Guid id, CancellationToken ct)
    {
        var shipment = await db.Shipments.Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
        await EnsureActsForAsync(shipment.FromLocationId, ct);

        shipment.Cancel();
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ShipmentDto> ResolveDiscrepancyAsync(Guid id, Guid itemId, ResolveDiscrepancyRequest request, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var shipment = await db.Shipments.Include(s => s.Items).Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
            var item = shipment.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new DomainException(ErrorCodes.NotFound);

            item.Resolve(request.Resolution, request.Note);
            if (request.Resolution == DiscrepancyResolution.CorrectSender)
                await ledger.ApplyAsync(shipment.FromLocationId, item.BatchId, item.Difference, MovementType.ShipmentCorrection, shipment.Id, ct);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    private async Task<(Guid From, Guid To)> ResolveRouteAsync(Guid? toDealerId, CancellationToken ct)
    {
        if (currentUser.IsDealer())
        {
            if (toDealerId is not null) throw new DomainException(ErrorCodes.InvalidShipmentRoute);
            return (await locations.MyLocationAsync(ct), StockLocation.CentralId);
        }

        if (toDealerId is null) throw new DomainException(ErrorCodes.InvalidShipmentRoute);
        return (StockLocation.CentralId, await locations.LocationOfDealerAsync(toDealerId.Value, ct));
    }

    private async Task EnsureActsForAsync(Guid locationId, CancellationToken ct)
    {
        if (locationId == StockLocation.CentralId)
        {
            if (!currentUser.IsCentralStaff()) throw new DomainException(ErrorCodes.Forbidden);
            return;
        }

        if (!currentUser.IsDealer() || locationId != await locations.MyLocationAsync(ct))
            throw new DomainException(ErrorCodes.Forbidden);
    }

    private async Task<IQueryable<Shipment>> ScopedAsync(CancellationToken ct)
    {
        var shipments = db.Shipments.AsNoTracking();
        if (!currentUser.IsDealer()) return shipments;

        var own = await locations.MyLocationAsync(ct);
        return shipments.Where(s => s.FromLocationId == own || s.ToLocationId == own);
    }

    private static IQueryable<ShipmentDto> Project(IQueryable<Shipment> shipments) => shipments.Select(s => new ShipmentDto(
        s.Id, s.Number, s.FromLocationId, Texts.LocationName(s.FromLocationId, s.FromLocation.Name), s.ToLocationId, Texts.LocationName(s.ToLocationId, s.ToLocation.Name), s.Status, s.Note,
        s.CreatedAt, s.ApprovedAt, s.SentAt, s.ReceivedAt,
        s.Items.Select(i => new ShipmentItemDto(
            i.Id, i.BatchId, i.Batch.BatchNumber, i.Batch.ExpiryDate, i.Batch.ProductId, i.Batch.Product.Name,
            i.SentQuantity, i.ReceivedQuantity, i.DiscrepancyStatus, i.DiscrepancyReason, i.Resolution, i.ResolutionNote)).ToList()));
}
