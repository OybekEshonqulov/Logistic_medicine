using Microsoft.EntityFrameworkCore;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Common;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Region> Regions { get; }
    DbSet<Dealer> Dealers { get; }
    DbSet<Pharmacy> Pharmacies { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Manufacturer> Manufacturers { get; }
    DbSet<Product> Products { get; }
    DbSet<Batch> Batches { get; }
    DbSet<StockLocation> StockLocations { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<ReceiptItem> ReceiptItems { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentItem> ShipmentItems { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<SaleItemBatch> SaleItemBatches { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountItem> StockCountItems { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Runs the action in one database transaction; everything rolls back if it throws.</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}

public interface ICurrentUser
{
    Guid? UserIdOrNull { get; }
    Guid UserId { get; }
    UserRole Role { get; }
    Guid? DealerId { get; }
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed record IssuedTokens(string AccessToken, string RefreshToken, string RefreshTokenHash, int ExpiresInSeconds, DateTimeOffset RefreshExpiresAt);

public interface ITokenService
{
    IssuedTokens Issue(User user);
    string HashRefreshToken(string refreshToken);
}
