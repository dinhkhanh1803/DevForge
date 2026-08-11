using System.Runtime.CompilerServices;
using DevForge.Application.Contracts.Persistence;
using DevForge.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace DevForge.Infrastructure.Persistence.Repositories;

internal static class RepositorySupport
{
    private static readonly ConditionalWeakTable<DevForgeDbContextFactory, SemaphoreSlim> _writeGates = new();

    public static string NormalizeIdentifier(string? value, string parameterName)
    {
        var issues = new List<ValidationIssue>();
        var normalized = MetadataRules.NormalizeIdentifier(
            value,
            "persistence.repository.identifier.invalid",
            parameterName,
            issues);
        return normalized ?? throw new ArgumentException("A valid metadata identifier is required.", parameterName);
    }

    public static string NormalizeSettingKey(string? value, string parameterName)
    {
        var result = AppSetting.Create(value, AppSettingValue.CreateBoolean(false), DateTimeOffset.UnixEpoch);
        return result.IsValid
            ? result.Value.Key
            : throw new ArgumentException("A valid setting key is required.", parameterName);
    }

    public static (string Id, string Version) NormalizeBlueprintKey(
        string? id,
        string? version,
        string idParameterName,
        string versionParameterName)
    {
        var result = BlueprintMetadataRecord.Create(
            id,
            version,
            BlueprintSource.BuiltIn,
            global::DevForge.Blueprints.Abstractions.Models.BlueprintTrust.BuiltIn,
            new string('a', 64),
            false,
            DateTimeOffset.UnixEpoch);
        if (!result.IsValid)
        {
            if (result.Issues.Any(issue => issue.Location == "id"))
            {
                throw new ArgumentException("A valid blueprint identifier is required.", idParameterName);
            }

            throw new ArgumentException("A valid blueprint version is required.", versionParameterName);
        }

        return (result.Value.Id, result.Value.Version);
    }

    public static string NormalizeProjectPath(string? value, string parameterName)
    {
        return LocalPersistencePathPolicy.TryNormalize(value)
            ?? throw new ArgumentException("A canonical local drive path is required.", parameterName);
    }

    public static async Task UpsertAsync<TEntity>(
        DevForgeDbContextFactory factory,
        TEntity incoming,
        object[] keys,
        Action<TEntity, TEntity> update,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _writeGates.GetValue(factory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = factory.CreateDbContext();
            var set = context.Set<TEntity>();
            var current = await set.FindAsync(keys, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                set.Add(incoming);
            }
            else
            {
                update(current, incoming);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<bool> RemoveAsync<TEntity>(
        DevForgeDbContextFactory factory,
        object[] keys,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = _writeGates.GetValue(factory, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = factory.CreateDbContext();
            var set = context.Set<TEntity>();
            var current = await set.FindAsync(keys, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return false;
            }

            set.Remove(current);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }
}
