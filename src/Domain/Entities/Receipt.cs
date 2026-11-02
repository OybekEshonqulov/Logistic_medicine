using PharmaTrack.Domain.Common;

namespace PharmaTrack.Domain.Entities;

public sealed class Receipt : Entity
{
    public required string DocumentNumber { get; init; }
    public Guid SupplierId { get; init; }
    public Supplier Supplier { get; init; } = null!;
    public DateOnly ReceivedOn { get; init; }
    public string? Note { get; init; }
    public Guid CreatedById { get; init; }
    public List<ReceiptItem> Items { get; init; } = [];
}

public sealed class ReceiptItem : Entity
{
    public Guid ReceiptId { get; init; }
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}
