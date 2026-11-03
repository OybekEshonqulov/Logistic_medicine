using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Domain.Entities;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Notifications;

/// <summary>Finds problems on a schedule and tells the people who can act. The same alert is never sent twice.</summary>
public sealed class AlertService(IAppDbContext db, IOptions<AlertOptions> alertOptions, TimeProvider clock)
{
    private const int SalesWindowDays = 30;
    private readonly AlertOptions _options = alertOptions.Value;

    private sealed record Alert(NotificationType Type, LocalizedText Message, Guid? EntityId, string DedupKey, Guid? DealerId);

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = clock.Today();
        var alerts = new List<Alert>();
        alerts.AddRange(await ExpiringAsync(today, ct));
        alerts.AddRange(await LowStockAsync(today, ct));
        alerts.AddRange(await StuckShipmentsAsync(ct));
        alerts.AddRange(await MissingReportsAsync(today, ct));

        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.Admin || u.Role == UserRole.Executive || u.Role == UserRole.Dealer))
            .Select(u => new { u.Id, u.Role, u.DealerId })
            .ToListAsync(ct);
        var keys = alerts.Select(a => a.DedupKey).ToList();
        var existing = (await db.Notifications.AsNoTracking()
            .Where(n => keys.Contains(n.DedupKey))
            .Select(n => new { n.UserId, n.DedupKey })
            .ToListAsync(ct))
            .Select(n => (n.UserId, n.DedupKey))
            .ToHashSet();

        var created = 0;
        foreach (var alert in alerts)
        {
            // Central managers see everything; a dealer only hears about their own stock.
            var recipients = users.Where(u => u.Role != UserRole.Dealer || (alert.DealerId is not null && u.DealerId == alert.DealerId));
            foreach (var user in recipients.Where(u => !existing.Contains((u.Id, alert.DedupKey))))
            {
                db.Notifications.Add(new Notification { UserId = user.Id, Type = alert.Type, Message = alert.Message.Uz, MessageRu = alert.Message.Ru, MessageCyrl = alert.Message.Cyrl, EntityId = alert.EntityId, DedupKey = alert.DedupKey });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    private async Task<IEnumerable<Alert>> ExpiringAsync(DateOnly today, CancellationToken ct)
    {
        var until = today.AddDays(_options.ExpiryWarningDays);
        var rows = await db.StockBalances.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.Batch.ExpiryDate <= until)
            .Select(b => new { b.LocationId, LocationName = b.Location.Name, b.Location.DealerId, b.BatchId, b.Batch.BatchNumber, ProductName = b.Batch.Product.Name, b.Batch.ExpiryDate, b.Quantity })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var daysLeft = r.ExpiryDate.DayNumber - today.DayNumber;
            // A new alert fires each time the batch crosses into a more urgent stage.
            var stage = daysLeft switch { < 0 => "expired", <= 30 => "30", <= 60 => "60", _ => "90" };
            var location = Texts.LocationText(r.LocationId, r.LocationName);
            var message = daysLeft < 0
                ? Texts.BatchExpired(location, r.ProductName, r.BatchNumber, r.Quantity)
                : Texts.BatchExpiring(location, r.ProductName, r.BatchNumber, daysLeft, r.Quantity);
            return new Alert(NotificationType.ExpiringBatch, message, r.BatchId, $"expiry:{r.LocationId}:{r.BatchId}:{stage}", r.DealerId);
        });
    }

    private async Task<IEnumerable<Alert>> LowStockAsync(DateOnly today, CancellationToken ct)
    {
        var since = today.AddDays(-(SalesWindowDays - 1));
        var sold = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Active && i.Sale.SaleDate >= since)
            .GroupBy(i => new { i.Sale.DealerId, i.ProductId, ProductName = i.Product.Name })
            .Select(g => new { g.Key.DealerId, g.Key.ProductId, g.Key.ProductName, Units = g.Sum(i => i.Quantity) })
            .ToListAsync(ct);

        var stock = await db.StockBalances.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.Location.DealerId != null)
            .GroupBy(b => new { DealerId = b.Location.DealerId!.Value, b.Batch.ProductId })
            .Select(g => new { g.Key.DealerId, g.Key.ProductId, Units = g.Sum(b => b.Quantity) })
            .ToDictionaryAsync(x => (x.DealerId, x.ProductId), x => x.Units, ct);

        var dealerNames = await db.Dealers.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var weekKey = AnalyticsCalc.BucketStart(today, TrendGranularity.Week).ToString("yyyyMMdd");

        return sold
            .Select(s => new { s, Left = stock.GetValueOrDefault((s.DealerId, s.ProductId)), Days = AnalyticsCalc.DaysOfStock(stock.GetValueOrDefault((s.DealerId, s.ProductId)), s.Units, SalesWindowDays) })
            .Where(x => x.Days < _options.LowStockDays)
            .Select(x => new Alert(
                NotificationType.LowStock,
                Texts.LowStock(dealerNames[x.s.DealerId], x.s.ProductName, x.Left, x.Days),
                x.s.ProductId,
                $"low:{x.s.DealerId}:{x.s.ProductId}:{weekKey}",
                x.s.DealerId));
    }

    private async Task<IEnumerable<Alert>> StuckShipmentsAsync(CancellationToken ct)
    {
        var before = clock.GetUtcNow().AddDays(-_options.ShipmentStuckDays);
        var stuck = await db.Shipments.AsNoTracking()
            .Where(s => s.Status == ShipmentStatus.InTransit && s.SentAt < before)
            .Select(s => new { s.Id, s.Number, s.ToLocationId, s.ToLocation.Name, s.ToLocation.DealerId })
            .ToListAsync(ct);

        return stuck.Select(s => new Alert(
            NotificationType.ShipmentNotReceived,
            Texts.ShipmentNotReceived(s.Number, Texts.LocationText(s.ToLocationId, s.Name)),
            s.Id,
            $"shipment:{s.Id}",
            s.DealerId));
    }

    private async Task<IEnumerable<Alert>> MissingReportsAsync(DateOnly today, CancellationToken ct)
    {
        var dealers = await db.Dealers.AsNoTracking().Where(d => d.IsActive).Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        var last = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Active)
            .GroupBy(s => s.DealerId)
            .Select(g => new { g.Key, Last = g.Max(s => s.SaleDate) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var withStock = await db.StockBalances.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.Location.DealerId != null)
            .Select(b => b.Location.DealerId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // A dealer with nothing on hand has nothing to report yet.
        return dealers
            .Where(d => withStock.Contains(d.Id))
            .Where(d => !last.TryGetValue(d.Id, out var date) || today.DayNumber - date.DayNumber > _options.SalesReportGraceDays)
            .Select(d => new Alert(
                NotificationType.SalesNotReported,
                Texts.SalesNotReported(d.Name),
                d.Id,
                $"report:{d.Id}:{(last.TryGetValue(d.Id, out var date) ? date.ToString("yyyyMMdd") : "never")}",
                d.Id));
    }
}
