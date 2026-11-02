using Microsoft.AspNetCore.Mvc;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.Notifications;

namespace PharmaTrack.Api.Controllers;

public sealed class NotificationsController(NotificationService notifications) : ApiController
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] NotificationQuery query, CancellationToken ct) => notifications.ListAsync(query, ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken ct) => notifications.UnreadCountAsync(ct);

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
