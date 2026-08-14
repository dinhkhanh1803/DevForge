using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using DevForge.Application.Contracts;

namespace DevForge.Application.Publication;

internal static class PublicationReceiptSerializer
{
    public static (byte[] Body, string Digest) Serialize(
        RunCheckpoint checkpoint,
        PublicationSnapshot publication)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(publication);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("runId", checkpoint.Run.Id);
            writer.WriteString("planHash", checkpoint.PlanHash);
            writer.WriteString("finalTreeDigest", publication.FinalTreeDigest);
            writer.WriteString("gitState", publication.GitState.ToString());
            writer.WriteString("branchPolicy", checkpoint.Preview!.Git.BranchPolicy.ToString());
            writer.WriteString("initialCommitId", publication.InitialCommitId);
            writer.WriteStartArray("branches");
            foreach (var branch in publication.Branches)
            {
                writer.WriteStringValue(branch);
            }

            writer.WriteEndArray();
            writer.WriteString("gitHubState", publication.GitHubState.ToString());
            if (publication.RepositoryIdentity is null)
            {
                writer.WriteNull("repositoryAccount");
                writer.WriteNull("repositoryName");
                writer.WriteNull("repositoryUrl");
                writer.WriteNull("ownershipNonce");
            }
            else
            {
                writer.WriteString("repositoryAccount", publication.RepositoryIdentity.Account);
                writer.WriteString("repositoryName", publication.RepositoryIdentity.RepositoryName);
                writer.WriteString("repositoryUrl", publication.RepositoryUrl);
                writer.WriteString("ownershipNonce", publication.OwnershipNonce);
            }

            writer.WriteBoolean("isPrivate", publication.IsPrivate);
            writer.WriteEndObject();
        }

        var body = buffer.WrittenSpan.ToArray();
        var digest = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(body))}";
        return (body, digest);
    }
}
