using DevForge.Application.Contracts.Persistence;

namespace DevForge.UnitTests.Application.Persistence;

public sealed class DatabaseLocationTests
{
    [Fact]
    public void CreatesCanonicalDatabaseAndBackupPathsInsideLocalDataRoot()
    {
        var result = DatabaseLocation.Create(
            @"C:\Users\dev\AppData\Local\DevForge",
            "devforge.db");

        Assert.True(result.IsValid);
        Assert.Equal(@"C:\Users\dev\AppData\Local\DevForge", result.Value.LocalDataRoot);
        Assert.Equal(
            @"C:\Users\dev\AppData\Local\DevForge\devforge.db",
            result.Value.DatabasePath);
        Assert.Equal(
            @"C:\Users\dev\AppData\Local\DevForge\devforge.backup-20260810T120000Z.db",
            result.Value.CreateBackupPath("20260810T120000Z"));
    }

    [Theory]
    [InlineData(@"relative\data")]
    [InlineData(@"\\server\share\DevForge")]
    [InlineData(@"\\?\C:\DevForge")]
    [InlineData(@"\\.\C:\DevForge")]
    [InlineData(@"C:\DevForge:stream")]
    [InlineData(@"C:\CON\DevForge")]
    [InlineData("C:\\DevForge\0data")]
    public void RejectsUnsafeLocalDataRoots(string root)
    {
        var result = DatabaseLocation.Create(root, "devforge.db");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.path.root.invalid");
    }

    [Theory]
    [InlineData("../devforge.db")]
    [InlineData("data/devforge.db")]
    [InlineData("devforge.db:stream")]
    [InlineData("devforge.sqlite")]
    [InlineData("CON.db")]
    public void RejectsUnsafeDatabaseFileNames(string fileName)
    {
        var result = DatabaseLocation.Create(@"C:\DevForge", fileName);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "persistence.path.file-name.invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("bad/suffix")]
    public void BackupSuffixMustBeSafe(string suffix)
    {
        var location = DatabaseLocation.Create(@"C:\DevForge", "devforge.db").Value;

        Assert.Throws<ArgumentException>(() => location.CreateBackupPath(suffix));
    }
}
