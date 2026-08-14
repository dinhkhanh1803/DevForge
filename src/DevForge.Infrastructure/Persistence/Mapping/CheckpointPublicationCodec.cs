using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevForge.Application.Contracts;
using DevForge.Domain.Projects;

namespace DevForge.Infrastructure.Persistence.Mapping;

internal static class CheckpointPublicationCodec
{
    public const int MaximumPublicationJsonBytes = 16_384;
    private const int SchemaVersion = 1;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly JsonSerializerOptions _options = new()
    {
        MaxDepth = 16,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static EncodedPublication Encode(PublicationSnapshot publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.FinalTreeDigest is null)
        {
            throw new PersistenceDataException();
        }

        var dto = new PublicationDto
        {
            SchemaVersion = SchemaVersion,
            GitState = publication.GitState.ToString(),
            GitHubState = publication.GitHubState.ToString(),
            ReceiptState = publication.ReceiptState.ToString(),
            FinalTreeDigest = publication.FinalTreeDigest,
            InitialCommitId = publication.InitialCommitId,
            Branches = [.. publication.Branches],
            Repository = publication.RepositoryIdentity is null
                ? null
                : new RepositoryDto
                {
                    Host = publication.RepositoryIdentity.Host,
                    Account = publication.RepositoryIdentity.Account,
                    Name = publication.RepositoryIdentity.RepositoryName,
                },
            IsPrivate = publication.IsPrivate,
            OwnershipNonce = publication.OwnershipNonce,
            RepositoryUrl = publication.RepositoryUrl,
            ReceiptPath = publication.ReceiptPath?.Value,
            ReceiptBodyDigest = publication.ReceiptBodyDigest,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, _options);
        EnsureBound(bytes.Length);
        return new EncodedPublication(
            _utf8.GetString(bytes),
            $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}");
    }

    public static PublicationSnapshot Decode(string json, string expectedChecksum)
    {
        try
        {
            var bytes = _utf8.GetBytes(json);
            EnsureBound(bytes.Length);
            var checksum = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(bytes))}";
            if (!StringComparer.Ordinal.Equals(checksum, expectedChecksum))
            {
                throw new PersistenceDataException();
            }

            using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            }))
            {
                RejectDuplicateProperties(document.RootElement);
            }

            var dto = JsonSerializer.Deserialize<PublicationDto>(bytes, _options)
                ?? throw new PersistenceDataException();
            if (dto.SchemaVersion != SchemaVersion
                || dto.Repository is not null
                    && !StringComparer.Ordinal.Equals(dto.Repository.Host, "github.com"))
            {
                throw new PersistenceDataException();
            }

            var repository = dto.Repository is null
                ? null
                : Require(GitHubRepositoryIdentity.Create(
                    dto.Repository.Account,
                    dto.Repository.Name));
            var publication = Require(PublicationSnapshot.Create(
                ParseDefined<GitPublicationState>(dto.GitState),
                ParseDefined<GitHubPublicationState>(dto.GitHubState),
                ParseDefined<PublicationReceiptState>(dto.ReceiptState),
                dto.FinalTreeDigest,
                dto.InitialCommitId,
                dto.Branches,
                repository,
                dto.IsPrivate,
                dto.OwnershipNonce,
                dto.RepositoryUrl,
                dto.ReceiptPath is null
                    ? null
                    : Require(WorkspaceRelativePath.Create(dto.ReceiptPath)),
                dto.ReceiptBodyDigest));
            if (!StringComparer.Ordinal.Equals(json, Encode(publication).Json))
            {
                throw new PersistenceDataException();
            }

            return publication;
        }
        catch (Exception exception) when (IsDataFailure(exception))
        {
            throw new PersistenceDataException();
        }
    }

    private static T ParseDefined<T>(string? value)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new PersistenceDataException();

    private static T Require<T>(DevForge.Domain.Validation.ValidationResult<T> result) =>
        result.IsValid ? result.Value : throw new PersistenceDataException();

    private static void EnsureBound(int byteCount)
    {
        if (byteCount > MaximumPublicationJsonBytes)
        {
            throw new PersistenceDataException();
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new PersistenceDataException();
                }

                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item);
            }
        }
    }

    private static bool IsDataFailure(Exception exception) => exception is
        JsonException
        or DecoderFallbackException
        or EncoderFallbackException
        or ArgumentException
        or InvalidOperationException
        or PersistenceDataException;

    internal sealed record EncodedPublication(string Json, string BodyChecksum);

    private sealed class PublicationDto
    {
        public int SchemaVersion { get; set; }
        public string? GitState { get; set; }
        public string? GitHubState { get; set; }
        public string? ReceiptState { get; set; }
        public string? FinalTreeDigest { get; set; }
        public string? InitialCommitId { get; set; }
        public string?[]? Branches { get; set; }
        public RepositoryDto? Repository { get; set; }
        public bool IsPrivate { get; set; }
        public string? OwnershipNonce { get; set; }
        public string? RepositoryUrl { get; set; }
        public string? ReceiptPath { get; set; }
        public string? ReceiptBodyDigest { get; set; }
    }

    private sealed class RepositoryDto
    {
        public string? Host { get; set; }
        public string? Account { get; set; }
        public string? Name { get; set; }
    }
}
