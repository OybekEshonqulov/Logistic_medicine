using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Application.Features.Analytics;

public sealed record ExportFile(string FileName, byte[] Content);

public sealed class ExportService(AnalyticsReportService reports, ISpreadsheet spreadsheet)
{
    public async Task<ExportFile> ExportAsync(string report, AnalyticsFilter filter, CancellationToken ct) => report.ToLowerInvariant() switch
    {
        "sales" => await SalesAsync(filter, ct),
        "stock" => await StockAsync(filter, ct),
        "expiring" => await ExpiringAsync(filter, ct),
        "dealers" => await DealersAsync(filter, ct),
        _ => throw new DomainException(ErrorCodes.UnknownReport)
    };

    private async Task<ExportFile> SalesAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var rows = await reports.SalesAsync(filter, SalesGroupBy.Dealer, ct);
        return Build("sotuvlar", Texts.SalesSheet, Texts.SalesColumns, rows.Select(r => new object?[] { r.Label, r.Units, r.Amount }));
    }

    private async Task<ExportFile> StockAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var rows = await reports.StockAsync(filter, StockGroupBy.Batch, ct);
        return Build("qoldiq", Texts.StockSheet, Texts.StockColumns, rows.Select(r => new object?[] { r.Label, r.Units, r.Value }));
    }

    private async Task<ExportFile> ExpiringAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var rows = await reports.ExpiringAsync(filter, null, ct);
        return Build("muddati-yaqin", Texts.ExpiringSheet, Texts.ExpiringColumns,
            rows.Select(r => new object?[] { r.LocationName, r.ProductName, r.BatchNumber, r.ExpiryDate, r.DaysLeft, r.Units, r.Value }));
    }

    private async Task<ExportFile> DealersAsync(AnalyticsFilter filter, CancellationToken ct)
    {
        var rows = await reports.DealersAsync(filter, ct);
        return Build("dilerlar", Texts.DealersSheet, Texts.DealersColumns,
            rows.Select(r => new object?[] { r.Name, r.RegionName, r.SalesAmount, r.SalesUnits, r.StockUnits, r.DaysOfStock, r.LastSaleDate }));
    }

    private ExportFile Build(string fileName, LocalizedText sheet, IEnumerable<LocalizedText> headers, IEnumerable<object?[]> rows) =>
        new($"{fileName}.xlsx", spreadsheet.Write(sheet.Current, Texts.Current(headers), rows));
}
