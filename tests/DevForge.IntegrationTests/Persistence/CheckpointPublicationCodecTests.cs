using DevForge.Application.Contracts;
using DevForge.Domain.Projects;
using DevForge.Infrastructure.Persistence;
using DevForge.Infrastructure.Persistence.Mapping;

namespace DevForge.IntegrationTests.Persistence;

public sealed class CheckpointPublicationCodecTests
{
    [Fact]
    public void CanonicalPublicationRoundTripsEverySafeEvidenceField()
    {
        var identity = GitHubRepositoryIdentity.Create("octocat", "devforge").Value;
        var snapshot = PublicationSnapshot.Create(
            GitPublicationState.Succeeded,
            GitHubPublicationState.Succeeded,
            PublicationReceiptState.Succeeded,
            Digest('a'),
            new string('b', 40),
            ["main", "develop"],
            identity,
            isPrivate: true,
            ownershipNonce: new string('c', 32),
            repositoryUrl: identity.HttpsWebUrl,
            WorkspaceRelativePath.Create("reports\\run-1.publication.json").Value,
            Digest('d')).Value;

        var encoded = CheckpointPublicationCodec.Encode(snapshot);
        var decoded = CheckpointPublicationCodec.Decode(encoded.Json, encoded.BodyChecksum);

        Assert.Equal(snapshot.GitState, decoded.GitState);
        Assert.Equal(snapshot.GitHubState, decoded.GitHubState);
        Assert.Equal(snapshot.ReceiptState, decoded.ReceiptState);
        Assert.Equal(snapshot.FinalTreeDigest, decoded.FinalTreeDigest);
        Assert.Equal(snapshot.InitialCommitId, decoded.InitialCommitId);
        Assert.Equal(snapshot.Branches.ToArray(), decoded.Branches.ToArray());
        Assert.Equal(snapshot.RepositoryIdentity, decoded.RepositoryIdentity);
        Assert.Equal(snapshot.IsPrivate, decoded.IsPrivate);
        Assert.Equal(snapshot.OwnershipNonce, decoded.OwnershipNonce);
        Assert.Equal(snapshot.RepositoryUrl, decoded.RepositoryUrl);
        Assert.Equal(snapshot.ReceiptPath, decoded.ReceiptPath);
        Assert.Equal(snapshot.ReceiptBodyDigest, decoded.ReceiptBodyDigest);
        Assert.StartsWith("sha256:", encoded.BodyChecksum, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicationCodecRejectsChecksumCanonicalAndBoundViolations()
    {
        var snapshot = PublicationSnapshot.CreateNotRequested(Digest('a')).Value;
        var encoded = CheckpointPublicationCodec.Encode(snapshot);
        var indented = encoded.Json.Replace("{", "{\r\n  ", StringComparison.Ordinal);

        Assert.Throws<PersistenceDataException>(() =>
            CheckpointPublicationCodec.Decode(encoded.Json, Digest('f')));
        Assert.Throws<PersistenceDataException>(() =>
            CheckpointPublicationCodec.Decode(indented, DigestBytes(indented)));
        Assert.Throws<PersistenceDataException>(() =>
            CheckpointPublicationCodec.Decode(new string('x', 16_385), Digest('f')));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("sensitive-unknown")]
    [InlineData("undefined-state")]
    [InlineData("cross-state")]
    public void PublicationCodecRejectsRechecksummedStructuralAndStateTampering(string mutation)
    {
        var encoded = CheckpointPublicationCodec.Encode(
            PublicationSnapshot.CreateNotRequested(Digest('a')).Value);
        var tampered = mutation switch
        {
            "duplicate" => encoded.Json.Replace(
                "\"gitState\":\"NotRequested\"",
                "\"gitState\":\"NotRequested\",\"gitState\":\"NotRequested\"",
                StringComparison.Ordinal),
            "sensitive-unknown" => encoded.Json.Replace(
                "\"schemaVersion\":1",
                "\"schemaVersion\":1,\"credential\":\"not-a-real-secret\"",
                StringComparison.Ordinal),
            "undefined-state" => encoded.Json.Replace(
                "\"gitState\":\"NotRequested\"",
                "\"gitState\":\"999\"",
                StringComparison.Ordinal),
            "cross-state" => encoded.Json.Replace(
                "\"gitState\":\"NotRequested\"",
                "\"gitState\":\"Succeeded\"",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };

        Assert.NotEqual(encoded.Json, tampered);
        Assert.Throws<PersistenceDataException>(() =>
            CheckpointPublicationCodec.Decode(tampered, DigestBytes(tampered)));
    }

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static string DigestBytes(string value) =>
        $"sha256:{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)))}";
}
