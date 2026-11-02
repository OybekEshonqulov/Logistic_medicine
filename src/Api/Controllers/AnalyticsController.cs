using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmaTrack.Application.Features.Analytics;
using PharmaTrack.Domain.Common;

namespace PharmaTrack.Api.Controllers;

public sealed class AnalyticsController(DashboardService dashboard, AnalyticsReportService reports) : ApiController
{
    /// <summary>Everything the executive dashboard shows, in a single response.</summary>
    [HttpGet("dashboard"), Authorize(Roles = Roles.Managers)]
    public Task<DashboardDto> Dashboard([FromQuery] AnalyticsFilter filter, CancellationToken ct) => dashboard.GetAsync(filter, ct);

    [HttpGet("summary"), Authorize(Roles = Roles.Managers)]
    public Task<DashboardKpis> Summary([FromQuery] AnalyticsFilter filter, CancellationToken ct) => dashboard.KpisOnlyAsync(filter, ct);

    [HttpGet("sales")]
    public Task<IReadOnlyList<SalesGroupRow>> Sales([FromQuery] AnalyticsFilter filter, [FromQuery] SalesGroupBy groupBy = SalesGroupBy.Day, CancellationToken ct = default) =>
        reports.SalesAsync(filter, groupBy, ct);

    [HttpGet("stock")]
    public Task<IReadOnlyList<StockGroupRow>> Stock([FromQuery] AnalyticsFilter filter, [FromQuery] StockGroupBy groupBy = StockGroupBy.Location, CancellationToken ct = default) =>
        reports.StockAsync(filter, groupBy, ct);

    [HttpGet("expiring")]
    public Task<IReadOnlyList<ExpiringRow>> Expiring([FromQuery] AnalyticsFilter filter, [FromQuery] int? withinDays, CancellationToken ct) =>
        reports.ExpiringAsync(filter, withinDays, ct);

    [HttpGet("days-of-stock")]
    public Task<IReadOnlyList<ProductStockRow>> DaysOfStock([FromQuery] AnalyticsFilter filter, CancellationToken ct) =>
        reports.DaysOfStockAsync(filter, ct);

    [HttpGet("dead-stock")]
    public Task<IReadOnlyList<DeadStockRow>> DeadStock([FromQuery] AnalyticsFilter filter, [FromQuery] int days = 60, CancellationToken ct = default) =>
        reports.DeadStockAsync(filter, days, ct);

    [HttpGet("dealers")]
    public Task<IReadOnlyList<DealerRow>> Dealers([FromQuery] AnalyticsFilter filter, CancellationToken ct) => reports.DealersAsync(filter, ct);

    /// <summary>Confirms every balance equals the sum of its journal rows.</summary>
    [HttpGet("reconciliation"), Authorize(Roles = Roles.CentralAll)]
    public Task<ReconciliationDto> Reconciliation(CancellationToken ct) => reports.ReconcileAsync(ct);
}

[Authorize(Roles = Roles.CentralAll)]
public sealed class ExportsController(ExportService exports) : ApiController
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <param name="report">sales, stock, expiring or dealers</param>
    [HttpGet("{report}")]
    public async Task<IActionResult> Export(string report, [FromQuery] AnalyticsFilter filter, CancellationToken ct)
    {
        var file = await exports.ExportAsync(report, filter, ct);
        return File(file.Content, ExcelType, file.FileName);
    }
}
