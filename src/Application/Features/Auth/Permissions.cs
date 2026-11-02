using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Auth;

/// <summary>Lets clients show or hide screens. The API still enforces every rule itself.</summary>
public static class Permissions
{
    private static readonly string[] Admin =
    [
        "users.manage", "references.manage", "receipts.manage", "shipments.manage", "shipments.receive",
        "sales.read", "stock.read", "stock_counts.manage", "adjustments.request", "adjustments.review",
        "analytics.dashboard", "analytics.read", "exports.read"
    ];

    private static readonly string[] Warehouse =
    [
        "references.manage", "receipts.manage", "shipments.manage", "shipments.receive",
        "sales.read", "stock.read", "stock_counts.manage", "adjustments.request",
        "analytics.read", "exports.read"
    ];

    private static readonly string[] Executive =
    [
        "references.read", "receipts.read", "shipments.read", "sales.read", "stock.read",
        "adjustments.read", "analytics.dashboard", "analytics.read", "exports.read"
    ];

    private static readonly string[] Dealer =
    [
        "references.read", "pharmacies.request", "shipments.receive", "shipments.return", "sales.manage", "stock.read",
        "stock_counts.manage", "adjustments.request", "analytics.read"
    ];

    public static IReadOnlyList<string> For(UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.Warehouse => Warehouse,
        UserRole.Executive => Executive,
        UserRole.Dealer => Dealer,
        _ => []
    };
}
