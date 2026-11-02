using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Common;

public sealed record BatchAllocation(Guid BatchId, int Quantity);

/// <summary>The only way stock changes. Must be called inside a transaction.</summary>
public interface IStockLedger
{
    /// <summary>Changes a balance and queues its journal row. Throws if the balance would go negative.</summary>
    Task ApplyAsync(Guid locationId, Guid batchId, int delta, MovementType type, Guid sourceId, CancellationToken ct);

    /// <summary>Picks batches earliest-expiry-first (FEFO), skipping batches expired on the given date, and locks them.</summary>
    Task<IReadOnlyList<BatchAllocation>> AllocateFefoAsync(Guid locationId, Guid productId, int quantity, DateOnly onDate, CancellationToken ct);
}

public interface ISpreadsheet
{
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<object?[]> rows);
    IReadOnlyList<string?[]> Read(Stream file, int columnCount);
}

public static class BusinessClock
{
    private static readonly TimeSpan TashkentOffset = TimeSpan.FromHours(5);

    /// <summary>Calendar date in Uzbekistan, which all business dates (sales, expiry) refer to.</summary>
    public static DateOnly Today(this TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TashkentOffset).DateTime);
}
