using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class StockCount : Entity
{
    public Guid LocationId { get; init; }
    public StockLocation Location { get; init; } = null!;
    public StockCountStatus Status { get; private set; } = StockCountStatus.InProgress;
    public Guid CreatedById { get; init; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public List<StockCountItem> Items { get; init; } = [];

    public void Complete(DateTimeOffset now)
    {
        if (Status != StockCountStatus.InProgress) throw new DomainException(ErrorCodes.CountNotInProgress);
        Status = StockCountStatus.Completed;
        CompletedAt = now;
    }
}

public sealed class StockCountItem : Entity
{
    public Guid StockCountId { get; init; }
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int ExpectedQuantity { get; set; }
    public int? CountedQuantity { get; set; }
}

public sealed class StockAdjustment : Entity
{
    public Guid LocationId { get; init; }
    public StockLocation Location { get; init; } = null!;
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public AdjustmentType Type { get; init; }
    public int Quantity { get; init; }
    public required string Reason { get; init; }
    public Guid? PharmacyId { get; init; }
    public Pharmacy? Pharmacy { get; init; }
    public decimal? UnitPrice { get; init; }
    public AdjustmentStatus Status { get; private set; } = AdjustmentStatus.Pending;
    public Guid RequestedById { get; init; }
    public Guid? ReviewedById { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public void Approve(Guid reviewerId, DateTimeOffset now) => Review(AdjustmentStatus.Approved, reviewerId, now);

    public void Reject(Guid reviewerId, DateTimeOffset now) => Review(AdjustmentStatus.Rejected, reviewerId, now);

    private void Review(AdjustmentStatus status, Guid reviewerId, DateTimeOffset now)
    {
        if (Status != AdjustmentStatus.Pending) throw new DomainException(ErrorCodes.AdjustmentNotPending);
        Status = status;
        ReviewedById = reviewerId;
        ReviewedAt = now;
    }
}
