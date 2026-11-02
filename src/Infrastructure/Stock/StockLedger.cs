using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;
using PharmaTrack.Infrastructure.Persistence;

namespace PharmaTrack.Infrastructure.Stock;

public sealed class StockLedger(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IStockLedger
{
    public async Task ApplyAsync(Guid locationId, Guid batchId, int delta, MovementType type, Guid sourceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (delta < 0)
        {
            // Atomic conditional update: two simultaneous sales can never take the same stock.
            var updated = await db.StockBalances
                .Where(b => b.LocationId == locationId && b.BatchId == batchId && b.Quantity >= -delta)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Quantity, b => b.Quantity + delta)
                    .SetProperty(b => b.UpdatedAt, now), ct);
            if (updated == 0) throw new DomainException(ErrorCodes.InsufficientStock, batchId);
        }
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stock_balances (location_id, batch_id, quantity, updated_at)
                VALUES ({locationId}, {batchId}, {delta}, {now})
                ON CONFLICT (location_id, batch_id)
                DO UPDATE SET quantity = stock_balances.quantity + EXCLUDED.quantity, updated_at = EXCLUDED.updated_at
                """, ct);
        }

        db.StockMovements.Add(new StockMovement
        {
            LocationId = locationId,
            BatchId = batchId,
            QuantityDelta = delta,
            Type = type,
            SourceId = sourceId,
            CreatedById = currentUser.UserIdOrNull
        });
    }

    public async Task<IReadOnlyList<BatchAllocation>> AllocateFefoAsync(Guid locationId, Guid productId, int quantity, DateOnly onDate, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<AvailableRow>($"""
            SELECT sb.batch_id, sb.quantity
            FROM stock_balances sb
            JOIN batches b ON b.id = sb.batch_id
            WHERE sb.location_id = {locationId} AND b.product_id = {productId}
              AND sb.quantity > 0 AND b.expiry_date >= {onDate}
            ORDER BY b.expiry_date, b.batch_number
            FOR UPDATE OF sb
            """).ToListAsync(ct);

        var allocations = new List<BatchAllocation>();
        var remaining = quantity;
        foreach (var row in rows)
        {
            if (remaining == 0) break;
            var taken = Math.Min(row.Quantity, remaining);
            allocations.Add(new BatchAllocation(row.BatchId, taken));
            remaining -= taken;
        }

        if (remaining > 0) throw new DomainException(ErrorCodes.InsufficientStock, productId);
        return allocations;
    }

    private sealed record AvailableRow(Guid BatchId, int Quantity);
}
