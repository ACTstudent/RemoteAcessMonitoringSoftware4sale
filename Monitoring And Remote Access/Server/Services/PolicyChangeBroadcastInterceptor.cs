using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Server.Hubs;
using Server.Models;
using Shared.Contracts;

namespace Server.Services;

public sealed class PolicyChangeBroadcastInterceptor : SaveChangesInterceptor
{
    private readonly IHubContext<RemoteMonitoringHub> _hub;
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public PolicyChangeBroadcastInterceptor(IHubContext<RemoteMonitoringHub> hub) => _hub = hub;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Track(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Track(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Take(eventData.Context))
            _ = BroadcastAsync(CancellationToken.None);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (Take(eventData.Context))
            await BroadcastAsync(cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Take(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Take(eventData.Context);
        return Task.CompletedTask;
    }

    private void Track(DbContext? context)
    {
        if (context is null) return;
        var changed = context.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            entry.Entity is RestrictionRule or BlacklistItem or ApplicationCategory or WebsiteCategory);
        if (changed)
            _pending[context.ContextId.InstanceId] = 0;
    }

    private bool Take(DbContext? context) =>
        context is not null && _pending.TryRemove(context.ContextId.InstanceId, out _);

    private Task BroadcastAsync(CancellationToken cancellationToken) =>
        _hub.Clients.Group(HubEventNames.StudentsGroup)
            .SendAsync(HubEventNames.PolicyRefreshRequired, cancellationToken);
}
