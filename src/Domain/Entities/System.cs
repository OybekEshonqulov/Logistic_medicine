using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? UserId { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? Changes { get; init; }
    public DateTimeOffset At { get; init; }
}

public sealed class Notification : Entity
{
    public Guid UserId { get; init; }
    public NotificationType Type { get; init; }
    /// <summary>Stored in every language because the reader's language is unknown when the alert is created.</summary>
    public required string Message { get; init; }
    public string? MessageRu { get; init; }
    public string? MessageCyrl { get; init; }
    public Guid? EntityId { get; init; }
    /// <summary>Prevents the alert job from sending the same alert to a user twice.</summary>
    public required string DedupKey { get; init; }
    public DateTimeOffset? ReadAt { get; set; }
}
