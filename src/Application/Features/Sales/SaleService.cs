using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Stock;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Sales;

public sealed record SaleItemRequest(Guid ProductId, int Quantity, decimal UnitPrice, Guid? BatchId);
public sealed record CreateSaleRequest(Guid PharmacyId, DateOnly SaleDate, string? Note, IReadOnlyList<SaleItemRequest> Items);
public sealed record ReverseSaleRequest(string Reason);
public sealed record SaleItemBatchDto(Guid BatchId, string BatchNumber, DateOnly ExpiryDate, int Quantity);
public sealed record SaleItemDto(Guid Id, Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal Amount, IReadOnlyList<SaleItemBatchDto> Batches);
public sealed record SaleDto(Guid Id, Guid DealerId, string DealerName, Guid PharmacyId, string PharmacyName, DateOnly SaleDate, SaleStatus Status, string? Note, string? ReverseReason, DateTimeOffset CreatedAt, decimal TotalAmount, IReadOnlyList<SaleItemDto> Items);
public sealed record SaleCreationResult(SaleDto Sale, bool AlreadyExisted);
public sealed record SaleQuery : PageQuery
{
    public Guid? DealerId { get; init; }
    public Guid? PharmacyId { get; init; }
    public Guid? ProductId { get; init; }
    public SaleStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.PharmacyId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.Items).Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .When(x => x.Items is not null)
            .WithMessage("Bir dori bir marta kiritilishi kerak");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class ReverseSaleRequestValidator : AbstractValidator<ReverseSaleRequest>
{
    public ReverseSaleRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class SaleService(
    IAppDbContext db,
    IStockLedger ledger,
    ICurrentUser currentUser,
    LocationService locations,
    TimeProvider clock)
{
    public Task<PagedResult<SaleDto>> ListAsync(SaleQuery query, CancellationToken ct)
    {
        var sales = Scoped();
        if (query.DealerId is { } dealerId) sales = sales.Where(s => s.DealerId == dealerId);
        if (query.PharmacyId is { } pharmacyId) sales = sales.Where(s => s.PharmacyId == pharmacyId);
        if (query.ProductId is { } productId) sales = sales.Where(s => s.Items.Any(i => i.ProductId == productId));
        if (query.Status is { } status) sales = sales.Where(s => s.Status == status);
        if (query.From is { } from) sales = sales.Where(s => s.SaleDate >= from);
        if (query.To is { } to) sales = sales.Where(s => s.SaleDate <= to);

        return Project(sales.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.CreatedAt)).ToPagedAsync(query, ct);
    }

    public async Task<SaleDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(Scoped().Where(s => s.Id == id)).FirstOrNotFoundAsync(ct);

    /// <summary>
    /// Safe to retry: a mobile client resends the same key after a lost response and gets the original
    /// sale back instead of a duplicate.
    /// </summary>
    public async Task<SaleCreationResult> CreateAsync(string clientRequestId, CreateSaleRequest request, CancellationToken ct)
    {
        var dealerId = currentUser.RequireDealerId();
        var existing = await FindByRequestIdAsync(dealerId, clientRequestId, ct);
        if (existing is not null) return new SaleCreationResult(existing, true);

        if (request.SaleDate > clock.Today()) throw new DomainException(ErrorCodes.SaleDateInFuture);
        await EnsurePharmacyAvailableAsync(request.PharmacyId, dealerId, ct);
        var location = await locations.MyLocationAsync(ct);

        try
        {
            var saleId = await db.InTransactionAsync(async () =>
            {
                var sale = new Sale
                {
                    DealerId = dealerId,
                    PharmacyId = request.PharmacyId,
                    SaleDate = request.SaleDate,
                    Note = request.Note,
                    ClientRequestId = clientRequestId,
                    CreatedById = currentUser.UserId
                };

                foreach (var line in request.Items)
                    sale.Items.Add(await BuildItemAsync(sale, line, location, ct));

                db.Sales.Add(sale);
                await db.SaveChangesAsync(ct);
                return sale.Id;
            }, ct);

            return new SaleCreationResult(await GetAsync(saleId, ct), false);
        }
        catch (DbUpdateException)
        {
            // Two retries raced each other: the unique key let one through, so return that one.
            var winner = await FindByRequestIdAsync(dealerId, clientRequestId, ct);
            if (winner is null) throw;
            return new SaleCreationResult(winner, true);
        }
    }

    /// <summary>Sales are never edited or deleted. A mistake is reversed and entered again.</summary>
    public async Task<SaleDto> ReverseAsync(Guid id, ReverseSaleRequest request, CancellationToken ct)
    {
        await db.InTransactionAsync(async () =>
        {
            var sale = await db.Sales.Include(s => s.Items).ThenInclude(i => i.Batches)
                .Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
            if (currentUser.IsDealer() && sale.DealerId != currentUser.DealerId)
                throw new DomainException(ErrorCodes.NotFound);

            sale.Reverse(request.Reason, clock.GetUtcNow());
            var location = await locations.LocationOfDealerAsync(sale.DealerId, ct);
            foreach (var allocation in sale.Items.SelectMany(i => i.Batches))
                await ledger.ApplyAsync(location, allocation.BatchId, allocation.Quantity, MovementType.SaleReversal, sale.Id, ct);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    private async Task<SaleItem> BuildItemAsync(Sale sale, SaleItemRequest line, Guid location, CancellationToken ct)
    {
        var allocations = line.BatchId is { } batchId
            ? [await AllocateChosenBatchAsync(batchId, line, sale.SaleDate, ct)]
            : await ledger.AllocateFefoAsync(location, line.ProductId, line.Quantity, sale.SaleDate, ct);

        var item = new SaleItem { ProductId = line.ProductId, Quantity = line.Quantity, UnitPrice = line.UnitPrice };
        foreach (var allocation in allocations)
        {
            item.Batches.Add(new SaleItemBatch { BatchId = allocation.BatchId, Quantity = allocation.Quantity });
            await ledger.ApplyAsync(location, allocation.BatchId, -allocation.Quantity, MovementType.Sale, sale.Id, ct);
        }

        return item;
    }

    private async Task<BatchAllocation> AllocateChosenBatchAsync(Guid batchId, SaleItemRequest line, DateOnly saleDate, CancellationToken ct)
    {
        var batch = await db.Batches.AsNoTracking()
            .Where(b => b.Id == batchId && b.ProductId == line.ProductId)
            .FirstOrNotFoundAsync(ct);
        if (batch.ExpiryDate < saleDate) throw new DomainException(ErrorCodes.BatchExpired, batch.BatchNumber);
        return new BatchAllocation(batchId, line.Quantity);
    }

    private async Task EnsurePharmacyAvailableAsync(Guid pharmacyId, Guid dealerId, CancellationToken ct)
    {
        // A dealer may sell to a pharmacy they just requested, so an offline sale is not blocked by pending approval.
        var available = await db.Pharmacies.AnyAsync(p => p.Id == pharmacyId && p.IsActive
            && (p.Status == PharmacyStatus.Active || (p.Status == PharmacyStatus.Pending && p.RequestedByDealerId == dealerId)), ct);
        if (!available) throw new DomainException(ErrorCodes.PharmacyNotAvailable);
    }

    private async Task<SaleDto?> FindByRequestIdAsync(Guid dealerId, string clientRequestId, CancellationToken ct) =>
        await Project(db.Sales.AsNoTracking().Where(s => s.DealerId == dealerId && s.ClientRequestId == clientRequestId))
            .FirstOrDefaultAsync(ct);

    private IQueryable<Sale> Scoped()
    {
        var sales = db.Sales.AsNoTracking();
        return currentUser.IsDealer() ? sales.Where(s => s.DealerId == currentUser.DealerId) : sales;
    }

    private static IQueryable<SaleDto> Project(IQueryable<Sale> sales) => sales.Select(s => new SaleDto(
        s.Id, s.DealerId, s.Dealer.Name, s.PharmacyId, s.Pharmacy.Name, s.SaleDate, s.Status, s.Note, s.ReverseReason, s.CreatedAt,
        s.Items.Sum(i => i.Quantity * i.UnitPrice),
        s.Items.Select(i => new SaleItemDto(
            i.Id, i.ProductId, i.Product.Name, i.Quantity, i.UnitPrice, i.Quantity * i.UnitPrice,
            i.Batches.Select(b => new SaleItemBatchDto(b.BatchId, b.Batch.BatchNumber, b.Batch.ExpiryDate, b.Quantity)).ToList())).ToList()));
}
