using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Common;
using PharmaTrack.Domain.Entities;

namespace PharmaTrack.Infrastructure.Persistence;

/// <summary>Fills timestamps and records who changed what, so services never have to remember to.</summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> SecretProperties = [nameof(User.PasswordHash), nameof(RefreshToken.TokenHash)];
    private static readonly HashSet<Type> NotAudited = [typeof(RefreshToken), typeof(StockMovement), typeof(Notification)];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext context)
    {
        var now = clock.GetUtcNow();
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<Entity>().ToList())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && !NotAudited.Contains(entry.Entity.GetType()))
            {
                logs.Add(ToLog(entry, now));
            }
        }

        context.Set<AuditLog>().AddRange(logs);
    }

    private AuditLog ToLog(EntityEntry<Entity> entry, DateTimeOffset now)
    {
        var changed = entry.Properties
            .Where(p => entry.State == EntityState.Added || p.IsModified)
            .Where(p => !SecretProperties.Contains(p.Metadata.Name))
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

        return new AuditLog
        {
            UserId = currentUser.UserIdOrNull,
            Action = entry.State.ToString(),
            EntityType = entry.Entity.GetType().Name,
            EntityId = entry.Entity.Id.ToString(),
            Changes = JsonSerializer.Serialize(changed),
            At = now
        };
    }
}
