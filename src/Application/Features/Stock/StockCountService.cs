using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed record CreateStockCountRequest(Guid? LocationId);
public sealed record CountedItemRequest(Guid BatchId, int CountedQuantity);
public sealed record CompleteStockCountRequest(IReadOnlyList<CountedItemRequest> Items);
public sealed record StockCountItemDto(Guid BatchId, string BatchNumber, DateOnly ExpiryDate, Guid ProductId, string ProductName, int ExpectedQuantity, int? CountedQuantity, int? Difference);
public sealed record StockCountDto(Guid Id, Guid LocationId, string LocationName, StockCountStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, IReadOnlyList<StockCountItemDto> Items);
public sealed record StockCountQuery : PageQuery
{
    public Guid? LocationId { get; init; }
    public StockCountStatus? Status { get; init; }
}

public sealed class CompleteStockCountRequestValidator : AbstractValidator<CompleteStockCountRequest>
{
    public CompleteStockCountRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items).Must(items => items.Select(i => i.BatchId).Distinct().Count() == items.Count)
            .When(x => x.Items is not null)
            .WithMessage(_ => Texts.DuplicateBatch.Current);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.BatchId).NotEmpty();
            item.RuleFor(i => i.CountedQuantity).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class StockCountService(
    IAppDbContext db,
    IStockLedger ledger,
    ICurrentUser currentUser,
    LocationService locations,
    TimeProvider clock)
{
    public async Task<PagedResult<StockCountDto>> ListAsync(StockCountQuery query, CancellationToken ct)
    {
        var counts = await ScopedAsync(ct);
        if (query.LocationId is { } locationId) counts = counts.Where(c => c.LocationId == locationId);
        if (query.Status is { } status) counts = counts.Where(c => c.Status == status);

        return await Project(counts.OrderByDescending(c => c.CreatedAt)).ToPagedAsync(query, ct);
    }

    public async Task<StockCountDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project((await ScopedAsync(ct)).Where(c => c.Id == id)).FirstOrNotFoundAsync(ct);

    /// <summary>Starts a count with the current balances, which clients use as the checklist.</summary>
    public async Task<StockCountDto> CreateAsync(CreateStockCountRequest request, CancellationToken ct)
    {
        var locationId = await locations.ResolveManagedLocationAsync(request.LocationId, ct);
        var balances = await db.StockBalances.AsNoTracking()
            .Where(b => b.LocationId == locationId && b.Quantity > 0)
            .Select(b => new { b.BatchId, b.Quantity })
            .ToListAsync(ct);

        var count = new StockCount
        {
            LocationId = locationId,
            CreatedById = currentUser.UserId,
            Items = balances.Select(b => new StockCountItem { BatchId = b.BatchId, ExpectedQuantity = b.Quantity }).ToList()
        };
        db.StockCounts.Add(count);
        await db.SaveChangesAsync(ct);
        return await GetAsync(count.Id, ct);
    }

    /// <summary>The system balance becomes what was physically counted; every difference is journaled.</summary>
    public async Task<StockCountDto> CompleteAsync(Guid id, CompleteStockCountRequest request, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var count = await db.StockCounts.Include(c => c.Items).Where(c => c.Id == id).FirstOrNotFoundAsync(ct);
            await locations.ResolveManagedLocationAsync(count.LocationId, ct);

            var counted = request.Items.ToDictionary(i => i.BatchId, i => i.CountedQuantity);
            if (count.Items.Any(i => !counted.ContainsKey(i.BatchId)))
                throw new DomainException(ErrorCodes.CountItemsMismatch);

            count.Complete(clock.GetUtcNow());

            var batchIds = counted.Keys.ToList();
            var current = await db.StockBalances.AsNoTracking()
                .Where(b => b.LocationId == count.LocationId && batchIds.Contains(b.BatchId))
                .ToDictionaryAsync(b => b.BatchId, b => b.Quantity, ct);

            foreach (var (batchId, quantity) in counted)
            {
                var expected = current.GetValueOrDefault(batchId);
                var item = count.Items.FirstOrDefault(i => i.BatchId == batchId);
                if (item is null)
                {
                    // A batch found on the shelf that was not on the checklist.
                    item = new StockCountItem { StockCountId = count.Id, BatchId = batchId };
                    db.StockCountItems.Add(item);
                }

                item.ExpectedQuantity = expected;
                item.CountedQuantity = quantity;
                if (quantity != expected)
                    await ledger.ApplyAsync(count.LocationId, batchId, quantity - expected, MovementType.CountAdjustment, count.Id, ct);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    private async Task<IQueryable<StockCount>> ScopedAsync(CancellationToken ct)
    {
        var counts = db.StockCounts.AsNoTracking();
        if (!currentUser.IsDealer()) return counts;

        var own = await locations.MyLocationAsync(ct);
        return counts.Where(c => c.LocationId == own);
    }

    private static IQueryable<StockCountDto> Project(IQueryable<StockCount> counts) => counts.Select(c => new StockCountDto(
        c.Id, c.LocationId, Texts.LocationName(c.LocationId, c.Location.Name), c.Status, c.CreatedAt, c.CompletedAt,
        c.Items.Select(i => new StockCountItemDto(
            i.BatchId, i.Batch.BatchNumber, i.Batch.ExpiryDate, i.Batch.ProductId, i.Batch.Product.Name,
            i.ExpectedQuantity, i.CountedQuantity, i.CountedQuantity - i.ExpectedQuantity)).ToList()));
}
