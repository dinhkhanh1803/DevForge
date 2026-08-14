using System.Collections.Immutable;
using System.Text.Json;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.GitHub;

internal static class GitHubEvidenceParser
{
    private const int MaximumRepositoryJsonCharacters = 16 * 1024;
    private static readonly ImmutableHashSet<string> _repositoryProperties =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "nameWithOwner",
            "description",
            "visibility",
            "isEmpty",
            "isFork",
            "isInOrganization",
            "isArchived",
            "isMirror",
            "isTemplate",
            "url");

    public static RepositoryEvidence ParseRepository(
        string[] lines,
        GitHubPublishRequest request)
    {
        if (lines.Length != 1 || lines[0].Length > MaximumRepositoryJsonCharacters)
        {
            throw UnsafeEvidence();
        }

        try
        {
            using var document = JsonDocument.Parse(
                lines[0],
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 8,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw UnsafeEvidence();
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !_repositoryProperties.Contains(property.Name))
                {
                    throw UnsafeEvidence();
                }
            }

            if (seen.Count != _repositoryProperties.Count)
            {
                throw UnsafeEvidence();
            }

            var root = document.RootElement;
            var expectedSlug = request.Repository.Account + "/" + request.Repository.RepositoryName;
            var expectedDescription = "DevForge ownership " + request.OwnershipNonce;
            var expectedVisibility = request.IsPrivate ? "PRIVATE" : "PUBLIC";
            var evidence = new RepositoryEvidence(
                RequiredString(root, "nameWithOwner"),
                RequiredString(root, "description"),
                RequiredString(root, "visibility"),
                RequiredBoolean(root, "isEmpty"),
                RequiredBoolean(root, "isFork"),
                RequiredBoolean(root, "isInOrganization"),
                RequiredBoolean(root, "isArchived"),
                RequiredBoolean(root, "isMirror"),
                RequiredBoolean(root, "isTemplate"),
                RequiredString(root, "url"));

            if (!StringComparer.Ordinal.Equals(evidence.NameWithOwner, expectedSlug)
                || !StringComparer.Ordinal.Equals(evidence.Description, expectedDescription)
                || !StringComparer.Ordinal.Equals(evidence.Visibility, expectedVisibility)
                || evidence.IsFork
                || evidence.IsInOrganization
                || evidence.IsArchived
                || evidence.IsMirror
                || evidence.IsTemplate
                || !StringComparer.Ordinal.Equals(evidence.Url, request.Repository.HttpsWebUrl))
            {
                throw UnsafeEvidence();
            }

            return evidence;
        }
        catch (JsonException)
        {
            throw UnsafeEvidence();
        }
        catch (InvalidOperationException)
        {
            throw UnsafeEvidence();
        }
    }

    public static ImmutableDictionary<string, string> ParseReferences(
        string[] lines,
        GitHubPublishRequest request)
    {
        if (lines.Length > request.Branches.Length)
        {
            throw UnsafeEvidence();
        }

        var references = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length > 128)
            {
                throw UnsafeEvidence();
            }

            var separator = line.IndexOf('\t');
            if (separator <= 0 || separator != line.LastIndexOf('\t'))
            {
                throw UnsafeEvidence();
            }

            const string prefix = "refs/heads/";
            var reference = line[..separator];
            var branch = reference.StartsWith(prefix, StringComparison.Ordinal)
                ? reference[prefix.Length..]
                : string.Empty;
            var objectId = line[(separator + 1)..];
            if (!request.Branches.Contains(branch, StringComparer.Ordinal)
                || !StringComparer.Ordinal.Equals(objectId, request.InitialCommitId)
                || !references.TryAdd(branch, objectId))
            {
                throw UnsafeEvidence();
            }
        }

        return references.ToImmutable();
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var property = root.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw UnsafeEvidence();
        }

        var value = property.GetString();
        return !string.IsNullOrEmpty(value) && value.Length <= 512
            ? value
            : throw UnsafeEvidence();
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        var property = root.GetProperty(name);
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw UnsafeEvidence(),
        };
    }

    private static InfrastructureOperationException UnsafeEvidence() => new(
        "DF-GH-004",
        "The remote repository evidence does not match the reviewed publication intent.");
}

internal sealed record RepositoryEvidence(
    string NameWithOwner,
    string Description,
    string Visibility,
    bool IsEmpty,
    bool IsFork,
    bool IsInOrganization,
    bool IsArchived,
    bool IsMirror,
    bool IsTemplate,
    string Url);
