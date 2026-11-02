using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class Batch : Entity
{
    public Guid ProductId { get; init; }
    public Product Product { get; init; } = null!;
    public required string BatchNumber { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public decimal PurchasePrice { get; set; }
}

public sealed class StockLocation : Entity
{
    public static readonly Guid CentralId = new("00000000-0000-0000-0000-000000000001");

    public StockLocation() { }

    public StockLocation(Guid id) => Id = id;

    public LocationType Type { get; init; }
    public Guid? DealerId { get; init; }
    public Dealer? Dealer { get; init; }
    public required string Name { get; set; }
}

/// <summary>Current quantity per location and batch. Always changed together with a movement.</summary>
public sealed class StockBalance
{
    public Guid LocationId { get; init; }
    public StockLocation Location { get; init; } = null!;
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int Quantity { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Append-only ledger. Rows are never edited; mistakes are fixed by a reversing movement.</summary>
public sealed class StockMovement : Entity
{
    public Guid LocationId { get; init; }
    public StockLocation Location { get; init; } = null!;
    public Guid BatchId { get; init; }
    public Batch Batch { get; init; } = null!;
    public int QuantityDelta { get; init; }
    public MovementType Type { get; init; }
    public Guid SourceId { get; init; }
    public Guid? CreatedById { get; init; }
}
