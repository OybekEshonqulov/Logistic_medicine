using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Stock;

public sealed record ReceiptItemRequest(Guid ProductId, string BatchNumber, DateOnly ExpiryDate, int Quantity, decimal UnitPrice);
public sealed record CreateReceiptRequest(string DocumentNumber, Guid SupplierId, DateOnly ReceivedOn, string? Note, IReadOnlyList<ReceiptItemRequest> Items);
public sealed record ReceiptItemDto(Guid BatchId, Guid ProductId, string ProductName, string BatchNumber, DateOnly ExpiryDate, int Quantity, decimal UnitPrice);
public sealed record ReceiptDto(Guid Id, string DocumentNumber, Guid SupplierId, string SupplierName, DateOnly ReceivedOn, string? Note, DateTimeOffset CreatedAt, IReadOnlyList<ReceiptItemDto> Items);
public sealed record ImportRowError(int Row, string Message);
public sealed record ReceiptQuery : PageQuery
{
    public Guid? SupplierId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed class CreateReceiptRequestValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptRequestValidator()
    {
        RuleFor(x => x.DocumentNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.BatchNumber).NotEmpty().MaximumLength(50);
            item.RuleFor(i => i.Quantity).GreaterThan(0);
            item.RuleFor(i => i.UnitPrice).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class ReceiptService(IAppDbContext db, IStockLedger ledger, ISpreadsheet spreadsheet, ICurrentUser currentUser)
{
    private static readonly string[] ImportColumns = ["GTIN yoki dori nomi", "Partiya raqami", "Yaroqlilik muddati (YYYY-MM-DD)", "Miqdor", "Narx"];

    public Task<PagedResult<ReceiptDto>> ListAsync(ReceiptQuery query, CancellationToken ct)
    {
        var receipts = db.Receipts.AsNoTracking();
        if (query.SupplierId is { } supplierId) receipts = receipts.Where(r => r.SupplierId == supplierId);
        if (query.From is { } from) receipts = receipts.Where(r => r.ReceivedOn >= from);
        if (query.To is { } to) receipts = receipts.Where(r => r.ReceivedOn <= to);
        if (!string.IsNullOrWhiteSpace(query.Search)) receipts = receipts.Where(r => r.DocumentNumber.Contains(query.Search));

        return Project(receipts.OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.CreatedAt)).ToPagedAsync(query, ct);
    }

    public async Task<ReceiptDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Receipts.AsNoTracking().Where(r => r.Id == id)).FirstOrNotFoundAsync(ct);

    public async Task<ReceiptDto> CreateAsync(CreateReceiptRequest request, CancellationToken ct)
    {
        await db.Suppliers.Where(s => s.Id == request.SupplierId).FirstOrNotFoundAsync(ct);

        var receiptId = await db.InTransactionAsync(async () =>
        {
            var receipt = new Receipt
            {
                DocumentNumber = request.DocumentNumber,
                SupplierId = request.SupplierId,
                ReceivedOn = request.ReceivedOn,
                Note = request.Note,
                CreatedById = currentUser.UserId
            };

            foreach (var item in request.Items)
            {
                var batch = await GetOrCreateBatchAsync(item, ct);
                receipt.Items.Add(new ReceiptItem { BatchId = batch.Id, Quantity = item.Quantity, UnitPrice = item.UnitPrice });
                await ledger.ApplyAsync(StockLocation.CentralId, batch.Id, item.Quantity, MovementType.Receipt, receipt.Id, ct);
            }

            db.Receipts.Add(receipt);
            await db.SaveChangesAsync(ct);
            return receipt.Id;
        }, ct);

        return await GetAsync(receiptId, ct);
    }

    public byte[] ImportTemplate() => spreadsheet.Write("Kirim", ImportColumns, []);

    /// <summary>All-or-nothing: a single bad row rejects the file and every problem is reported at once.</summary>
    public async Task<ReceiptDto> ImportAsync(Stream file, string documentNumber, Guid supplierId, DateOnly receivedOn, CancellationToken ct)
    {
        var rows = spreadsheet.Read(file, ImportColumns.Length);
        var keys = rows.Select(r => r[0]).OfType<string>().Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => keys.Contains(p.Name) || (p.Gtin != null && keys.Contains(p.Gtin)))
            .Select(p => new { p.Id, p.Name, p.Gtin })
            .ToListAsync(ct);

        var items = new List<ReceiptItemRequest>();
        var errors = new List<ImportRowError>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2;
            var matches = products.Where(p => p.Gtin == row[0] || p.Name == row[0]).ToList();
            if (matches.Count != 1) errors.Add(new ImportRowError(rowNumber, "Dori topilmadi yoki bir xil nomli dorilar bor"));
            if (string.IsNullOrWhiteSpace(row[1])) errors.Add(new ImportRowError(rowNumber, "Partiya raqami bo'sh"));
            if (!DateOnly.TryParse(row[2], CultureInfo.InvariantCulture, out var expiry)) errors.Add(new ImportRowError(rowNumber, "Muddat noto'g'ri"));
            if (!int.TryParse(row[3], CultureInfo.InvariantCulture, out var quantity) || quantity <= 0) errors.Add(new ImportRowError(rowNumber, "Miqdor noto'g'ri"));
            if (!decimal.TryParse(row[4], CultureInfo.InvariantCulture, out var price) || price < 0) errors.Add(new ImportRowError(rowNumber, "Narx noto'g'ri"));

            if (errors.All(e => e.Row != rowNumber))
                items.Add(new ReceiptItemRequest(matches[0].Id, row[1]!, expiry, quantity, price));
        }

        if (rows.Count == 0) errors.Add(new ImportRowError(2, "Fayl bo'sh"));
        if (errors.Count > 0) throw new DomainException(ErrorCodes.ImportFailed, errors.Cast<object>().ToArray());

        return await CreateAsync(new CreateReceiptRequest(documentNumber, supplierId, receivedOn, null, items), ct);
    }

    private async Task<Batch> GetOrCreateBatchAsync(ReceiptItemRequest item, CancellationToken ct)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.ProductId == item.ProductId && b.BatchNumber == item.BatchNumber, ct);
        if (batch is not null)
        {
            if (batch.ExpiryDate != item.ExpiryDate) throw new DomainException(ErrorCodes.BatchExpiryMismatch, item.BatchNumber);
            return batch;
        }

        await db.Products.Where(p => p.Id == item.ProductId).FirstOrNotFoundAsync(ct);
        batch = new Batch { ProductId = item.ProductId, BatchNumber = item.BatchNumber, ExpiryDate = item.ExpiryDate, PurchasePrice = item.UnitPrice };
        db.Batches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    private static IQueryable<ReceiptDto> Project(IQueryable<Receipt> receipts) => receipts.Select(r => new ReceiptDto(
        r.Id, r.DocumentNumber, r.SupplierId, r.Supplier.Name, r.ReceivedOn, r.Note, r.CreatedAt,
        r.Items.Select(i => new ReceiptItemDto(i.BatchId, i.Batch.ProductId, i.Batch.Product.Name, i.Batch.BatchNumber, i.Batch.ExpiryDate, i.Quantity, i.UnitPrice)).ToList()));
}
