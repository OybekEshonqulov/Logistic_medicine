using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class Sale : Entity
{
    public Guid DealerId { get; init; }
    public Dealer Dealer { get; init; } = null!;
    public Guid PharmacyId { get; init; }
    public Pharmacy Pharmacy { get; init; } = null!;
    public DateOnly SaleDate { get; init; }
    public string? Note { get; init; }
    public required string ClientRequestId { get; init; }
    public Guid CreatedById { get; init; }
    public SaleStatus Status { get; private set; } = SaleStatus.Active;
    public DateTimeOffset? ReversedAt { get; private set; }
    public string? ReverseReason { get; private set; }
    public List<SaleItem> Items { get; init; } = [];

    public void Reverse(string reason, DateTimeOffset now)
    {
        if (Status == SaleStatus.Reversed) throw new DomainException(ErrorCodes.SaleAlreadyReversed);
        Status = SaleStatus.Reversed;
        ReversedAt = now;
        ReverseReason = reason;
    }
}

public sealed class SaleItem : Entity
{
    public Guid SaleId { get; init; }
    public Sale Sale { get; init; } = null!;
    public Guid ProductId { get; init; }
    public Product Product { get; init; } = null!;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public List<SaleItemBatch> Batches { get; init; } = [];
}

public sealed class SaleItemBatch
{
    public Guid SaleItemId { get; init; }
    public SaleItem SaleItem { get; init; } = null!;
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int Quantity { get; init; }
}
