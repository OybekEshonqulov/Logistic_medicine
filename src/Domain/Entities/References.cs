using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class Region : Entity
{
    public Guid? ParentId { get; set; }
    public Region? Parent { get; set; }
    public RegionType Type { get; set; }
    public required string NameUz { get; set; }
    public required string NameRu { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Dealer : Entity
{
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public Guid RegionId { get; set; }
    public Region Region { get; set; } = null!;
    public required string Address { get; set; }
    public bool IsActive { get; set; } = true;
    public List<DealerRegion> ServiceRegions { get; init; } = [];
}

public sealed class DealerRegion
{
    public Guid DealerId { get; init; }
    public Guid RegionId { get; init; }
    public Region Region { get; init; } = null!;
}

public sealed class Pharmacy : Entity
{
    public required string Name { get; set; }
    public Guid RegionId { get; set; }
    public Region Region { get; set; } = null!;
    public required string Address { get; set; }
    public string? Phone { get; set; }
    public PharmacyStatus Status { get; set; } = PharmacyStatus.Pending;
    public Guid? RequestedByDealerId { get; init; }
    public bool IsActive { get; set; } = true;
}

public sealed class Supplier : Entity
{
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Manufacturer : Entity
{
    public required string Name { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Product : Entity
{
    public required string Name { get; set; }
    public required string Form { get; set; }
    public string? Dosage { get; set; }
    public Guid ManufacturerId { get; set; }
    public Manufacturer Manufacturer { get; set; } = null!;
    public string? Gtin { get; set; }
    public bool IsActive { get; set; } = true;
}
