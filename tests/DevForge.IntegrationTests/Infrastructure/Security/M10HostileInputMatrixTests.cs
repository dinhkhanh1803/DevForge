using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Blueprints;
using DevForge.Infrastructure.FileSystem;

namespace DevForge.IntegrationTests.Infrastructure.Security;

public sealed class M10HostileInputMatrixTests
{
    [Theory]
    [InlineData("run-process", "executable", "powershell")]
    [InlineData("run-process", "executable", "cmd")]
    [InlineData("run-process", "executable", "curl")]
    [InlineData("create-directory", "path", "..\\outside")]
    [InlineData("create-directory", "path", "src\\.env")]
    public async Task ProductionLoaderQuarantinesHostileActionsWithoutWorkspaceMutation(
        string handler,
        string parameter,
        string value)
    {
        await using var fixture = await HostilePackageFixture.CreateAsync(
            handler,
            parameter,
            value);

        var result = await new BlueprintPackageLoader().LoadAsync(
            fixture.Source,
            WorkspaceRelativePath.Create("hostile.blueprint").Value,
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        Assert.Equal("DF-BP-003", Assert.Single(result.Inspection.Issues).Code);
        Assert.Equal(0, fixture.WorkspaceMutationAttempts);
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData("C:\\outside")]
    [InlineData("\\\\server\\share")]
    [InlineData("\\\\?\\GLOBALROOT\\Device\\HarddiskVolumeShadowCopy1")]
    [InlineData("\\??\\C:\\outside")]
    [InlineData("src/forward-slash")]
    [InlineData("src\\child.")]
    [InlineData("src\\COM1")]
    [InlineData("src\\.env")]
    public void UnsafeOutputPathsAreRejectedBeforeExecution(string path)
    {
        var action = Action("create-directory", ("path", Text(path)));

        var issue = Assert.Single(BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(path, issue.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("cmd")]
    [InlineData("bash")]
    [InlineData("curl")]
    [InlineData("msiexec")]
    public void ShellDownloadAndInstallerIdentitiesAreRejectedBeforeExecution(string executable)
    {
        var action = Action(
            "run-process",
            ("executable", Text(executable)),
            ("arguments", Sequence(Text("ignored"))),
            ("workingDirectory", Text(".")),
            ("allowedExitCodes", Sequence(BlueprintValue.FromInteger(0))));

        var issue = Assert.Single(BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(executable, issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("registry-operation")]
    [InlineData("firewall-operation")]
    [InlineData("service-operation")]
    [InlineData("require-administrator")]
    [InlineData("download-executable")]
    public void PrivilegedAndDownloadHandlersAreOutsideTheClosedVocabulary(string handler)
    {
        var issue = Assert.Single(BlueprintActionPolicy.Validate(
            Action(handler),
            BlueprintTrust.BuiltIn));

        Assert.Equal("DF-BP-003", issue.Code);
        Assert.DoesNotContain(handler, issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BlueprintTrust.Untrusted)]
    [InlineData(BlueprintTrust.Quarantined)]
    public void NonExecutableTrustFailsBeforeActionInspection(BlueprintTrust trust)
    {
        var issue = Assert.Single(BlueprintActionPolicy.Validate(
            Action("create-directory", ("path", Text("src"))),
            trust));

        Assert.Equal("DF-BP-002", issue.Code);
    }

    [Fact]
    public void SecretShapedNestedPayloadKeysAreRejectedWithoutEcho()
    {
        const string secretKey = "github-token";
        var result = BlueprintValue.FromObject(
        [
            KeyValuePair.Create<string, BlueprintValue?>(
                secretKey,
                Text("not-a-real-secret")),
        ]);

        var issue = Assert.Single(result.Issues);

        Assert.Equal("blueprint.value.key.secret-shaped", issue.Code);
        Assert.DoesNotContain(secretKey, issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static BlueprintActionDefinition Action(
        string handler,
        params (string Key, BlueprintValue Value)[] parameters) =>
        new(
            "hostile-fixture",
            handler,
            parameters.ToImmutableDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            TimeSpan.FromMinutes(1));

    private static BlueprintValue Text(string value) => BlueprintValue.FromString(value).Value;

    private static BlueprintValue Sequence(params BlueprintValue[] values) =>
        BlueprintValue.FromArray(values).Value;

    private sealed class HostilePackageFixture : IAsyncDisposable
    {
        private readonly string _rootPath;
        private readonly MutationRejectingWorkspace _workspace;

        private HostilePackageFixture(
            string rootPath,
            MutationRejectingWorkspace workspace,
            BlueprintPackageSource source)
        {
            _rootPath = rootPath;
            _workspace = workspace;
            Source = source;
        }

        public BlueprintPackageSource Source { get; }

        public int WorkspaceMutationAttempts => _workspace.MutationAttempts;

        public static async Task<HostilePackageFixture> CreateAsync(
            string handler,
            string parameter,
            string value)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "DevForge-M10-Hostile-" + Guid.NewGuid().ToString("N"));
            var package = Path.Combine(root, "hostile.blueprint");
            Directory.CreateDirectory(Path.Combine(package, "templates"));
            var actionParameters = handler == "run-process"
                ? $"""
                         executable: {value}
                         arguments:
                           - ignored
                         workingDirectory: .
                         allowedExitCodes:
                           - 0
                  """
                : $"""
                         {parameter}: '{value.Replace("'", "''", StringComparison.Ordinal)}'
                  """;
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.yaml"] = Encoding.UTF8.GetBytes($"""
                    id: hostile.blueprint
                    name: Hostile Blueprint
                    version: 1.0.0
                    engineVersion: ">=1.0.0 <2.0.0"
                    tools: []
                    features: []
                    actions:
                      - id: hostile-action
                        handler: {handler}
                        timeoutSeconds: 30
                        parameters:
                    {actionParameters}
                    validators: []
                    artifacts: []
                    dependencies: []
                    """),
                ["inputs.schema.json"] = """
                    {"type":"object","properties":{},"required":[],"additionalProperties":false}
                    """u8.ToArray(),
                ["rules.yaml"] = "[]"u8.ToArray(),
                ["templates/app.txt"] = "safe"u8.ToArray(),
            };
            foreach (var file in files)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(package, file.Key.Replace('/', Path.DirectorySeparatorChar)),
                    file.Value);
            }

            var checksums = files.ToDictionary(
                item => item.Key,
                item => Convert.ToHexString(SHA256.HashData(item.Value)).ToLowerInvariant(),
                StringComparer.Ordinal);
            await File.WriteAllBytesAsync(
                Path.Combine(package, "checksums.json"),
                JsonSerializer.SerializeToUtf8Bytes(checksums));

            var guarded = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(root).Value,
                CancellationToken.None);
            var recording = new MutationRejectingWorkspace(guarded);
            var source = BlueprintPackageSource.Create(
                "m10-hostile-fixture",
                recording,
                BlueprintSourceProvenance.BuiltIn).Value;
            return new HostilePackageFixture(root, recording, source);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutationRejectingWorkspace(IWorkspaceFileSystem inner)
        : IWorkspaceFileSystem
    {
        private int _mutationAttempts;

        public int MutationAttempts => Volatile.Read(ref _mutationAttempts);

        public WorkspaceRoot Root => inner.Root;

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            inner.FileExistsAsync(path, token);

        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken token) =>
            inner.DirectoryExistsAsync(path, token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken token) =>
            inner.EnumerateAllFilesAsync(token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken token) =>
            inner.EnumerateRootDirectoriesAsync(token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(
            WorkspaceRelativePath directory,
            bool recursive,
            CancellationToken token) => inner.EnumerateFilesAsync(directory, recursive, token);

        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken token) => inner.EnumerateDirectoriesAsync(directory, token);

        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken token) =>
            inner.OpenReadAsync(path, token);

        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken token) =>
            RejectMutation();

        public Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken token) => RejectMutation<Stream>();

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken token) =>
            RejectMutation();

        public Task DeleteDirectoryAsync(
            WorkspaceRelativePath path,
            DirectoryCleanupIntent intent,
            CancellationToken token) => RejectMutation();

        public Task MoveDirectoryAsync(
            WorkspaceRelativePath source,
            WorkspaceRelativePath destination,
            WorkspaceMoveIntent intent,
            CancellationToken token) => RejectMutation();

        private Task RejectMutation()
        {
            Interlocked.Increment(ref _mutationAttempts);
            throw new InvalidOperationException("The package loader attempted a workspace mutation.");
        }

        private Task<T> RejectMutation<T>()
        {
            Interlocked.Increment(ref _mutationAttempts);
            throw new InvalidOperationException("The package loader attempted a workspace mutation.");
        }
    }
}
