using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed record CreateAdjustmentRequest(Guid? LocationId, Guid BatchId, AdjustmentType Type, int Quantity, string Reason, Guid? PharmacyId, decimal? UnitPrice);
public sealed record AdjustmentDto(Guid Id, Guid LocationId, string LocationName, Guid BatchId, string BatchNumber, Guid ProductId, string ProductName, AdjustmentType Type, int Quantity, string Reason, Guid? PharmacyId, string? PharmacyName, decimal? UnitPrice, AdjustmentStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record AdjustmentQuery : PageQuery
{
    public Guid? LocationId { get; init; }
    public AdjustmentStatus? Status { get; init; }
    public AdjustmentType? Type { get; init; }
}

public sealed class CreateAdjustmentRequestValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public CreateAdjustmentRequestValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.PharmacyId).NotEmpty().When(x => x.Type == AdjustmentType.PharmacyReturn);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).When(x => x.UnitPrice is not null);
    }
}

public sealed class AdjustmentService(
    IAppDbContext db,
    IStockLedger ledger,
    ICurrentUser currentUser,
    LocationService locations,
    TimeProvider clock)
{
    public async Task<PagedResult<AdjustmentDto>> ListAsync(AdjustmentQuery query, CancellationToken ct)
    {
        var adjustments = await ScopedAsync(ct);
        if (query.LocationId is { } locationId) adjustments = adjustments.Where(a => a.LocationId == locationId);
        if (query.Status is { } status) adjustments = adjustments.Where(a => a.Status == status);
        if (query.Type is { } type) adjustments = adjustments.Where(a => a.Type == type);

        return await Project(adjustments.OrderByDescending(a => a.CreatedAt)).ToPagedAsync(query, ct);
    }

    public async Task<AdjustmentDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project((await ScopedAsync(ct)).Where(a => a.Id == id)).FirstOrNotFoundAsync(ct);

    /// <summary>Creates a request only. Stock changes when an administrator approves it.</summary>
    public async Task<AdjustmentDto> CreateAsync(CreateAdjustmentRequest request, CancellationToken ct)
    {
        var locationId = await locations.ResolveManagedLocationAsync(request.LocationId, ct);
        await db.Batches.Where(b => b.Id == request.BatchId).FirstOrNotFoundAsync(ct);
        if (request.PharmacyId is { } pharmacyId)
            await db.Pharmacies.Where(p => p.Id == pharmacyId).FirstOrNotFoundAsync(ct);

        var adjustment = new StockAdjustment
        {
            LocationId = locationId,
            BatchId = request.BatchId,
            Type = request.Type,
            Quantity = request.Quantity,
            Reason = request.Reason,
            PharmacyId = request.PharmacyId,
            UnitPrice = request.UnitPrice,
            RequestedById = currentUser.UserId
        };
        db.StockAdjustments.Add(adjustment);
        await db.SaveChangesAsync(ct);
        return await GetAsync(adjustment.Id, ct);
    }

    public async Task<AdjustmentDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var adjustment = await db.StockAdjustments.Where(a => a.Id == id).FirstOrNotFoundAsync(ct);
            adjustment.Approve(currentUser.UserId, clock.GetUtcNow());

            var (delta, movementType) = adjustment.Type == AdjustmentType.PharmacyReturn
                ? (adjustment.Quantity, MovementType.PharmacyReturn)
                : (-adjustment.Quantity, MovementType.WriteOff);
            await ledger.ApplyAsync(adjustment.LocationId, adjustment.BatchId, delta, movementType, adjustment.Id, ct);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<AdjustmentDto> RejectAsync(Guid id, CancellationToken ct)
    {
        var adjustment = await db.StockAdjustments.Where(a => a.Id == id).FirstOrNotFoundAsync(ct);
        adjustment.Reject(currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<IQueryable<StockAdjustment>> ScopedAsync(CancellationToken ct)
    {
        var adjustments = db.StockAdjustments.AsNoTracking();
        if (!currentUser.IsDealer()) return adjustments;

        var own = await locations.MyLocationAsync(ct);
        return adjustments.Where(a => a.LocationId == own);
    }

    private static IQueryable<AdjustmentDto> Project(IQueryable<StockAdjustment> adjustments) => adjustments.Select(a => new AdjustmentDto(
        a.Id, a.LocationId, a.Location.Name, a.BatchId, a.Batch.BatchNumber, a.Batch.ProductId, a.Batch.Product.Name,
        a.Type, a.Quantity, a.Reason, a.PharmacyId, a.Pharmacy != null ? a.Pharmacy.Name : null, a.UnitPrice, a.Status, a.CreatedAt, a.ReviewedAt));
}
