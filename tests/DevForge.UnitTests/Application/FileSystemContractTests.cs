using System.Collections.Immutable;
using DevForge.Application.Contracts;

namespace DevForge.UnitTests.Application;

public sealed class FileSystemContractTests
{
    [Fact]
    public void WorkspaceSupportsWholeRootEnumerationWithoutExposingRootPath()
    {
        var method = typeof(IWorkspaceFileSystem).GetMethod(
            "EnumerateAllFilesAsync",
            [typeof(CancellationToken)]);

        Assert.NotNull(method);
        Assert.Equal(
            typeof(Task<ImmutableArray<WorkspaceRelativePath>>),
            method.ReturnType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\absolute.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("folder/file.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("folder\\.\\file.txt")]
    [InlineData("folder\\..\\file.txt")]
    [InlineData("folder\\\\file.txt")]
    [InlineData("file.txt:stream")]
    public void WorkspaceRelativePathRejectsUnguardedPaths(string? value)
    {
        var result = WorkspaceRelativePath.Create(value);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WorkspaceRelativePathIsAValueObject()
    {
        var first = WorkspaceRelativePath.Create("src\\Program.cs").Value;
        var same = WorkspaceRelativePath.Create("src\\Program.cs").Value;
        var different = WorkspaceRelativePath.Create("tests\\ProgramTests.cs").Value;

        Assert.Equal(first, same);
        Assert.NotEqual(first, different);
        Assert.Equal("src\\Program.cs", first.Value);
    }

    [Fact]
    public void WorkspaceRootRequiresAnAbsolutePathAndHasValueEquality()
    {
        Assert.False(WorkspaceRoot.Create(null).IsValid);
        Assert.False(WorkspaceRoot.Create("relative").IsValid);

        var first = WorkspaceRoot.Create("C:\\work").Value;
        var same = WorkspaceRoot.Create("C:\\work").Value;
        Assert.Equal(first, same);
    }

    [Fact]
    public void FileSystemOpensAValidatedRootAndScopedPathOperationsUseOnlyGuardedPaths()
    {
        var openMethod = Assert.Single(typeof(IFileSystem).GetMethods());
        Assert.Equal(typeof(WorkspaceRoot), openMethod.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Task<IWorkspaceFileSystem>), openMethod.ReturnType);

        foreach (var method in typeof(IWorkspaceFileSystem).GetMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.Name?.Contains("path", StringComparison.OrdinalIgnoreCase) == true
                    || parameter.Name?.Contains("source", StringComparison.OrdinalIgnoreCase) == true
                    || parameter.Name?.Contains("destination", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Assert.Equal(typeof(WorkspaceRelativePath), parameter.ParameterType);
                }
            }
        }
    }
}
