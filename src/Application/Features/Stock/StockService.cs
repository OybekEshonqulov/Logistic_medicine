using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed record BatchDto(Guid Id, Guid ProductId, string ProductName, string BatchNumber, DateOnly ExpiryDate, decimal? PurchasePrice);
public sealed record BatchQuery : PageQuery
{
    public Guid? ProductId { get; init; }
    public DateOnly? ExpiringBefore { get; init; }
}

public sealed record BalanceDto(Guid LocationId, string LocationName, Guid BatchId, string BatchNumber, DateOnly ExpiryDate, Guid ProductId, string ProductName, int Quantity);
public sealed record BalanceQuery : PageQuery
{
    public Guid? LocationId { get; init; }
    public Guid? DealerId { get; init; }
    public Guid? ProductId { get; init; }
    public DateOnly? ExpiringBefore { get; init; }
    public bool IncludeEmpty { get; init; }
}

public sealed record MovementDto(Guid Id, DateTimeOffset CreatedAt, Guid LocationId, string LocationName, Guid BatchId, string BatchNumber, Guid ProductId, string ProductName, int QuantityDelta, MovementType Type, Guid SourceId);
public sealed record MovementQuery : PageQuery
{
    public Guid? LocationId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? BatchId { get; init; }
    public MovementType? Type { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}

public sealed class StockService(IAppDbContext db, ICurrentUser currentUser, LocationService locations)
{
    public Task<PagedResult<BatchDto>> ListBatchesAsync(BatchQuery query, CancellationToken ct)
    {
        var batches = db.Batches.AsNoTracking();
        if (query.ProductId is { } productId) batches = batches.Where(b => b.ProductId == productId);
        if (query.ExpiringBefore is { } date) batches = batches.Where(b => b.ExpiryDate <= date);
        if (!string.IsNullOrWhiteSpace(query.Search)) batches = batches.Where(b => b.BatchNumber.Contains(query.Search));

        // Purchase prices are the company's margin, so dealers never receive them.
        var showPrice = !currentUser.IsDealer();
        return batches.OrderBy(b => b.ExpiryDate)
            .Select(b => new BatchDto(b.Id, b.ProductId, b.Product.Name, b.BatchNumber, b.ExpiryDate, showPrice ? b.PurchasePrice : null))
            .ToPagedAsync(query, ct);
    }

    public async Task<BatchDto> GetBatchAsync(Guid id, CancellationToken ct)
    {
        var showPrice = !currentUser.IsDealer();
        return await db.Batches.AsNoTracking().Where(b => b.Id == id)
            .Select(b => new BatchDto(b.Id, b.ProductId, b.Product.Name, b.BatchNumber, b.ExpiryDate, showPrice ? b.PurchasePrice : null))
            .FirstOrNotFoundAsync(ct);
    }

    public async Task<PagedResult<BalanceDto>> ListBalancesAsync(BalanceQuery query, CancellationToken ct)
    {
        var balances = db.StockBalances.AsNoTracking();
        if (currentUser.IsDealer())
        {
            var own = await locations.MyLocationAsync(ct);
            balances = balances.Where(b => b.LocationId == own);
        }

        if (query.LocationId is { } locationId) balances = balances.Where(b => b.LocationId == locationId);
        if (query.DealerId is { } dealerId) balances = balances.Where(b => b.Location.DealerId == dealerId);
        if (query.ProductId is { } productId) balances = balances.Where(b => b.Batch.ProductId == productId);
        if (query.ExpiringBefore is { } date) balances = balances.Where(b => b.Batch.ExpiryDate <= date);
        if (!query.IncludeEmpty) balances = balances.Where(b => b.Quantity > 0);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            balances = balances.Where(b => b.Batch.Product.Name.ToLower().Contains(search) || b.Batch.BatchNumber.ToLower().Contains(search));
        }

        return await balances
            .OrderBy(b => b.Location.Name).ThenBy(b => b.Batch.Product.Name).ThenBy(b => b.Batch.ExpiryDate)
            .Select(b => new BalanceDto(b.LocationId, b.Location.Name, b.BatchId, b.Batch.BatchNumber, b.Batch.ExpiryDate, b.Batch.ProductId, b.Batch.Product.Name, b.Quantity))
            .ToPagedAsync(query, ct);
    }

    public async Task<PagedResult<MovementDto>> ListMovementsAsync(MovementQuery query, CancellationToken ct)
    {
        var movements = db.StockMovements.AsNoTracking();
        if (currentUser.IsDealer())
        {
            var own = await locations.MyLocationAsync(ct);
            movements = movements.Where(m => m.LocationId == own);
        }

        if (query.LocationId is { } locationId) movements = movements.Where(m => m.LocationId == locationId);
        if (query.ProductId is { } productId) movements = movements.Where(m => m.Batch.ProductId == productId);
        if (query.BatchId is { } batchId) movements = movements.Where(m => m.BatchId == batchId);
        if (query.Type is { } type) movements = movements.Where(m => m.Type == type);
        if (query.From is { } from) movements = movements.Where(m => m.CreatedAt >= from);
        if (query.To is { } to) movements = movements.Where(m => m.CreatedAt <= to);

        return await movements.OrderByDescending(m => m.CreatedAt)
            .Select(m => new MovementDto(m.Id, m.CreatedAt, m.LocationId, m.Location.Name, m.BatchId, m.Batch.BatchNumber, m.Batch.ProductId, m.Batch.Product.Name, m.QuantityDelta, m.Type, m.SourceId))
            .ToPagedAsync(query, ct);
    }
}
