using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Enums;

namespace PharmaTrack.Application.Features.Notifications;

public sealed record NotificationDto(Guid Id, NotificationType Type, string Message, Guid? EntityId, DateTimeOffset CreatedAt, bool IsRead);
public sealed record NotificationQuery : PageQuery
{
    public bool? UnreadOnly { get; init; }
}
public sealed record UnreadCountDto(int Count);

public sealed class NotificationService(IAppDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    public Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct)
    {
        var notifications = db.Notifications.AsNoTracking().Where(n => n.UserId == currentUser.UserId);
        if (query.UnreadOnly == true) notifications = notifications.Where(n => n.ReadAt == null);

        return notifications.OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type, Languages.Pick(n.Message, n.MessageRu, n.MessageCyrl), n.EntityId, n.CreatedAt, n.ReadAt != null))
            .ToPagedAsync(query, ct);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(CancellationToken ct) =>
        new(await db.Notifications.CountAsync(n => n.UserId == currentUser.UserId && n.ReadAt == null, ct));

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var notification = await db.Notifications.Where(n => n.Id == id && n.UserId == currentUser.UserId).FirstOrNotFoundAsync(ct);
        notification.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Notifications.Where(n => n.UserId == currentUser.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
