using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Common.Interfaces;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence;

public sealed class DualDatabaseSyncInterceptor : SaveChangesInterceptor
{
    private static readonly AsyncLocal<bool> _isSyncing = new();
    private readonly IDatabaseContextResolver _resolver;
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ILogger<DualDatabaseSyncInterceptor> _logger;

    private readonly List<SyncItem> _pendingItems = new();

    public DualDatabaseSyncInterceptor(
        IDatabaseContextResolver resolver,
        IApplicationDbContextFactory dbFactory,
        ILogger<DualDatabaseSyncInterceptor> logger)
    {
        _resolver = resolver;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static IDisposable BeginSuppression()
    {
        _isSyncing.Value = true;
        return new SuppressionScope();
    }

    private sealed class SuppressionScope : IDisposable
    {
        public void Dispose()
        {
            _isSyncing.Value = false;
        }
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_isSyncing.Value || eventData.Context == null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        _pendingItems.Clear();

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var entity = entry.Entity;

            // Sales, SaleItems, Payments, and RegisterSessions are not synchronized via companion change-tracker
            if (entity is Sale or SaleItem or Payment or RegisterSession)
            {
                continue;
            }

            // Loyalty transactions linked to an active unshifted sale are transferred by SalesShiftService
            if (entity is LoyaltyTransaction lt && lt.SaleId.HasValue)
            {
                continue;
            }

            var keyProps = entry.Metadata.FindPrimaryKey()?.Properties;
            if (keyProps == null || keyProps.Count == 0)
            {
                continue;
            }

            var keyValues = keyProps.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

            var propValues = new Dictionary<string, object?>();
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                foreach (var prop in entry.Properties)
                {
                    if (!prop.Metadata.IsShadowProperty())
                    {
                        propValues[prop.Metadata.Name] = prop.CurrentValue;
                    }
                }
            }

            _pendingItems.Add(new SyncItem(
                entry.Metadata.ClrType,
                keyValues,
                entry.State,
                propValues
            ));
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_isSyncing.Value || _pendingItems.Count == 0 || eventData.Context == null)
        {
            return await base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        var itemsToSync = _pendingItems.ToList();
        _pendingItems.Clear();

        var currentConn = eventData.Context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(currentConn))
        {
            return result;
        }

        var normalConn = _resolver.GetNormalConnectionString();
        var adminConn = _resolver.GetAdminConnectionString();

        string targetConn = currentConn.Contains("pos_system_admin", StringComparison.OrdinalIgnoreCase)
            ? normalConn
            : adminConn;

        if (string.Equals(currentConn, targetConn, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        _isSyncing.Value = true;
        try
        {
            using var companionDb = _dbFactory.CreateDbContext(targetConn);

            foreach (var item in itemsToSync)
            {
                var existing = await companionDb.FindAsync(item.ClrType, item.KeyValues, cancellationToken);

                if (item.State == EntityState.Deleted)
                {
                    if (existing != null)
                    {
                        companionDb.Remove(existing);
                    }
                }
                else if (item.State == EntityState.Modified)
                {
                    if (existing != null)
                    {
                        companionDb.Entry(existing).CurrentValues.SetValues(item.PropertyValues);
                    }
                    else
                    {
                        var newObj = Activator.CreateInstance(item.ClrType)!;
                        var companionEntry = companionDb.Add(newObj);
                        companionEntry.CurrentValues.SetValues(item.PropertyValues);
                    }
                }
                else if (item.State == EntityState.Added)
                {
                    if (existing != null)
                    {
                        companionDb.Entry(existing).CurrentValues.SetValues(item.PropertyValues);
                    }
                    else
                    {
                        var newObj = Activator.CreateInstance(item.ClrType)!;
                        var companionEntry = companionDb.Add(newObj);
                        companionEntry.CurrentValues.SetValues(item.PropertyValues);
                    }
                }
            }

            await companionDb.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Synchronized {Count} entities to companion database.", itemsToSync.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize {Count} entities to companion database.", itemsToSync.Count);
        }
        finally
        {
            _isSyncing.Value = false;
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private sealed record SyncItem(
        Type ClrType,
        object?[] KeyValues,
        EntityState State,
        Dictionary<string, object?> PropertyValues
    );
}
