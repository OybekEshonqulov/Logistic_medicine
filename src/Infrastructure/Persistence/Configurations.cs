using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaTrack.Domain.Entities;

namespace PharmaTrack.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.HasIndex(x => x.Phone).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    public void Configure(EntityTypeBuilder<Region> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.NameUz).HasMaxLength(150);
        b.Property(x => x.NameRu).HasMaxLength(150);
        b.Property(x => x.NameCyrl).HasMaxLength(150);
        b.HasIndex(x => x.NameUz).IsUnique();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> b)
    {
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.NameUz).HasMaxLength(150);
        b.Property(x => x.NameRu).HasMaxLength(150);
        b.Property(x => x.NameCyrl).HasMaxLength(150);
        b.HasIndex(x => new { x.RegionId, x.NameUz }).IsUnique();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DealerConfiguration : IEntityTypeConfiguration<Dealer>
{
    public void Configure(EntityTypeBuilder<Dealer> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DealerDistrictConfiguration : IEntityTypeConfiguration<DealerDistrict>
{
    public void Configure(EntityTypeBuilder<DealerDistrict> b)
    {
        b.HasKey(x => new { x.DealerId, x.DistrictId });
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Dealer>().WithMany(x => x.ServiceDistricts).HasForeignKey(x => x.DealerId);
    }
}

internal sealed class DealerRegionConfiguration : IEntityTypeConfiguration<DealerRegion>
{
    public void Configure(EntityTypeBuilder<DealerRegion> b)
    {
        b.HasKey(x => new { x.DealerId, x.RegionId });
        b.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Dealer>().WithMany(x => x.ServiceRegions).HasForeignKey(x => x.DealerId);
    }
}

internal sealed class PharmacyConfiguration : IEntityTypeConfiguration<Pharmacy>
{
    public void Configure(EntityTypeBuilder<Pharmacy> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.HasOne(x => x.Region).WithMany().HasForeignKey(x => x.RegionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Dealer>().WithMany().HasForeignKey(x => x.RequestedByDealerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(20);
    }
}

internal sealed class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Country).HasMaxLength(100);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Form).HasMaxLength(100);
        b.Property(x => x.Dosage).HasMaxLength(100);
        b.Property(x => x.Gtin).HasMaxLength(14);
        b.HasIndex(x => x.Gtin).IsUnique().HasFilter("gtin IS NOT NULL");
        b.HasOne(x => x.Manufacturer).WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.HasIndex(x => new { x.ProductId, x.BatchNumber }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasIndex(x => x.DealerId).IsUnique();
        b.HasOne(x => x.Dealer).WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.HasKey(x => new { x.LocationId, x.BatchId });
        b.ToTable(t => t.HasCheckConstraint("ck_stock_balances_quantity_not_negative", "quantity >= 0"));
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.HasIndex(x => new { x.LocationId, x.BatchId });
        b.HasIndex(x => x.SourceId);
        b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.Property(x => x.DocumentNumber).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReceiptId);
    }
}

internal sealed class ReceiptItemConfiguration : IEntityTypeConfiguration<ReceiptItem>
{
    public void Configure(EntityTypeBuilder<ReceiptItem> b) =>
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.Property(x => x.Number).UseIdentityByDefaultColumn();
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => x.Status);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ShipmentItemConfiguration : IEntityTypeConfiguration<ShipmentItem>
{
    public void Configure(EntityTypeBuilder<ShipmentItem> b)
    {
        b.Property(x => x.DiscrepancyReason).HasMaxLength(500);
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.Ignore(x => x.Difference);
        b.HasOne(x => x.Shipment).WithMany(x => x.Items).HasForeignKey(x => x.ShipmentId);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.Property(x => x.ClientRequestId).HasMaxLength(100);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.ReverseReason).HasMaxLength(500);
        b.HasIndex(x => new { x.DealerId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.SaleDate, x.Status });
        b.HasOne(x => x.Dealer).WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pharmacy).WithMany().HasForeignKey(x => x.PharmacyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> b)
    {
        b.HasOne(x => x.Sale).WithMany(x => x.Items).HasForeignKey(x => x.SaleId);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SaleItemBatchConfiguration : IEntityTypeConfiguration<SaleItemBatch>
{
    public void Configure(EntityTypeBuilder<SaleItemBatch> b)
    {
        b.HasKey(x => new { x.SaleItemId, x.BatchId });
        b.HasOne(x => x.SaleItem).WithMany(x => x.Batches).HasForeignKey(x => x.SaleItemId);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> b)
    {
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.StockCountId);
    }
}

internal sealed class StockCountItemConfiguration : IEntityTypeConfiguration<StockCountItem>
{
    public void Configure(EntityTypeBuilder<StockCountItem> b) =>
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => x.Status);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Pharmacy).WithMany().HasForeignKey(x => x.PharmacyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Action).HasMaxLength(20);
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.EntityId).HasMaxLength(100);
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.At);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Property(x => x.Message).HasMaxLength(500);
        b.Property(x => x.MessageRu).HasMaxLength(500);
        b.Property(x => x.MessageCyrl).HasMaxLength(500);
        b.Property(x => x.DedupKey).HasMaxLength(200);
        b.HasIndex(x => new { x.UserId, x.DedupKey }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.ReadAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
    }
}
