using System.Text.RegularExpressions;

namespace PharmaTrack.Api.Infrastructure;

/// <summary>Turns controller names into URL segments: StockCounts becomes stock-counts.</summary>
public sealed partial class KebabCaseRouteTransformer : IOutboundParameterTransformer
{
    public string? TransformOutbound(object? value) =>
        value is null ? null : WordBoundary().Replace(value.ToString()!, "$1-$2").ToLowerInvariant();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex WordBoundary();
}
