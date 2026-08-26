using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application.Diagnostics;

public sealed class SupportBundleContractTests
{
    [Fact]
    public void RequestAcceptsCanonicalRunIdAndRejectsPathOrSecretShapedValues()
    {
        Assert.True(SupportBundleRequest.Create("run-001", includeEnvironmentSnapshot: true).IsValid);

        Assert.False(SupportBundleRequest.Create("..\\outside", false).IsValid);
        Assert.False(SupportBundleRequest.Create("ghp_abcdefghijklmnop", false).IsValid);
        Assert.False(SupportBundleRequest.Create(new string('a', 129), false).IsValid);
    }

    [Fact]
    public void ReceiptRequiresOwnedCanonicalBundlePathDigestLengthAndUtcTimestamp()
    {
        var valid = SupportBundleReceipt.Create(
            "bundle-001",
            WorkspaceRelativePath.Create("support-bundles\\bundle-001.zip").Value,
            new string('a', 64),
            128,
            DateTimeOffset.UnixEpoch);

        Assert.True(valid.IsValid);
        Assert.False(SupportBundleReceipt.Create(
            "bundle-001",
            WorkspaceRelativePath.Create("logs\\bundle-001.zip").Value,
            new string('a', 64),
            128,
            DateTimeOffset.UnixEpoch).IsValid);
        Assert.False(SupportBundleReceipt.Create(
            "bundle-001",
            WorkspaceRelativePath.Create("support-bundles\\bundle-001.zip").Value,
            "not-a-digest",
            0,
            DateTimeOffset.Now).IsValid);
    }
}
