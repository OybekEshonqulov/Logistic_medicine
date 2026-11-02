using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class Shipment : Entity
{
    public long Number { get; init; }
    public Guid FromLocationId { get; init; }
    public StockLocation FromLocation { get; init; } = null!;
    public Guid ToLocationId { get; init; }
    public StockLocation ToLocation { get; init; } = null!;
    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Draft;
    public string? Note { get; init; }
    public Guid CreatedById { get; init; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public List<ShipmentItem> Items { get; init; } = [];

    public void Approve(DateTimeOffset now)
    {
        EnsureStatus(ShipmentStatus.Draft);
        Status = ShipmentStatus.Approved;
        ApprovedAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        EnsureStatus(ShipmentStatus.Approved);
        Status = ShipmentStatus.InTransit;
        SentAt = now;
    }

    public void MarkReceived(DateTimeOffset now)
    {
        EnsureStatus(ShipmentStatus.InTransit);
        Status = ShipmentStatus.Received;
        ReceivedAt = now;
    }

    public void Cancel()
    {
        if (Status is not (ShipmentStatus.Draft or ShipmentStatus.Approved))
            throw new DomainException(ErrorCodes.InvalidShipmentStatus, Status);
        Status = ShipmentStatus.Cancelled;
    }

    private void EnsureStatus(ShipmentStatus expected)
    {
        if (Status != expected) throw new DomainException(ErrorCodes.InvalidShipmentStatus, Status);
    }
}

public sealed class ShipmentItem : Entity
{
    public Guid ShipmentId { get; init; }
    public Shipment Shipment { get; init; } = null!;
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int SentQuantity { get; init; }
    public int? ReceivedQuantity { get; private set; }
    public string? DiscrepancyReason { get; private set; }
    public DiscrepancyStatus DiscrepancyStatus { get; private set; } = DiscrepancyStatus.None;
    public DiscrepancyResolution? Resolution { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>Positive when goods went missing, negative when more arrived than was sent.</summary>
    public int Difference => SentQuantity - (ReceivedQuantity ?? SentQuantity);

    public void Receive(int quantity, string? reason)
    {
        ReceivedQuantity = quantity;
        DiscrepancyReason = reason;
        DiscrepancyStatus = quantity == SentQuantity ? DiscrepancyStatus.None : DiscrepancyStatus.Open;
    }

    public void Resolve(DiscrepancyResolution resolution, string? note)
    {
        if (DiscrepancyStatus != DiscrepancyStatus.Open) throw new DomainException(ErrorCodes.DiscrepancyNotOpen);
        DiscrepancyStatus = DiscrepancyStatus.Resolved;
        Resolution = resolution;
        ResolutionNote = note;
    }
}
