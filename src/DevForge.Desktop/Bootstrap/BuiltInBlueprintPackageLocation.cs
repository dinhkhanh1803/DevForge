using System.IO;
using DevForge.Application.Contracts;
using DevForge.Blueprints.BuiltIn;

namespace DevForge.Desktop.Bootstrap;

public sealed class BuiltInBlueprintPackageLocation
{
    private BuiltInBlueprintPackageLocation(WorkspaceRoot root)
    {
        Root = root;
    }

    public WorkspaceRoot Root { get; }

    public static BuiltInBlueprintPackageLocation Create(string? applicationBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationBaseDirectory))
        {
            throw new ArgumentException(
                "A canonical application base directory is required.",
                nameof(applicationBaseDirectory));
        }

        var root = WorkspaceRoot.Create(Path.Combine(
            applicationBaseDirectory,
            BuiltInBlueprintCatalog.OutputDirectory));
        if (!root.IsValid)
        {
            throw new ArgumentException(
                "The built-in blueprint package root is invalid.",
                nameof(applicationBaseDirectory));
        }

        return new BuiltInBlueprintPackageLocation(root.Value);
    }
}
