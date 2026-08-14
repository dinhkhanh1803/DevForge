using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Infrastructure.Execution;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class ClosedExecutionHandlerRegistryTests
{
    private static readonly string[] _operationalIds =
    [
        "create-directory",
        "render-template",
        "copy-overlay",
        "patch-json",
        "patch-yaml",
        "patch-xml",
        "run-process",
        "package-install",
        "validate-command",
        "validate-file-exists",
        "validate-file-content",
    ];

    private static readonly string[] _implementationSourceFiles =
    [
        "ClosedExecutionHandlerRegistry.cs",
        "RuntimePlanValueMaterializer.cs",
    ];

    [Theory]
    [MemberData(nameof(OperationalHandlerIds))]
    public void ResolvesEveryOperationalHandlerByExactOrdinalId(string id)
    {
        var handlers = _operationalIds.Select(candidate => new StubHandler(candidate)).ToArray();

        var result = ClosedExecutionHandlerRegistry.Create(BlueprintTrust.TrustedLocal, handlers);

        Assert.True(result.IsValid);
        Assert.Equal(id, result.Value.Resolve(id)?.Id);
        Assert.Null(result.Value.Resolve(id.ToUpperInvariant()));
    }

    [Fact]
    public async Task BuiltInDeferredHandlersReturnStableUnsupportedFailure()
    {
        var result = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.BuiltIn,
            BuiltInHandlers());

        Assert.True(result.IsValid);
        foreach (var id in new[] { "git-operation", "github-operation" })
        {
            var handler = Assert.IsAssignableFrom<IExecutionHandler>(result.Value.Resolve(id));
            var execution = await handler.ExecuteAsync(null!, null, CancellationToken.None);
            Assert.Equal(id, handler.Id);
            Assert.Equal(ExecutionPhase.Execute, execution.Phase);
            Assert.Equal(ExecutionHandlerOutcome.Failed, execution.Outcome);
            Assert.Equal("DF-EXEC-001", execution.Error?.Code);
            Assert.DoesNotContain("GitHub", execution.Error?.TechnicalDetail.Value ?? string.Empty);
        }
    }

    [Fact]
    public void BuiltInRegistryRequiresAndResolvesDedicatedFinalizationBoundary()
    {
        var result = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.BuiltIn,
            BuiltInHandlers());

        Assert.True(result.IsValid);
        Assert.Equal("finalize-workspace", result.Value.Resolve("finalize-workspace")?.Id);
    }

    [Fact]
    public void TrustedLocalCannotResolveBuiltInOnlyOrFinalizationHandlers()
    {
        var result = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.TrustedLocal,
            _operationalIds.Select(id => new StubHandler(id)));

        Assert.True(result.IsValid);
        Assert.Null(result.Value.Resolve("git-operation"));
        Assert.Null(result.Value.Resolve("github-operation"));
        Assert.Null(result.Value.Resolve("finalize-workspace"));
        Assert.Null(result.Value.Resolve("unknown-handler"));
        Assert.Null(result.Value.Resolve(" create-directory"));
    }

    [Fact]
    public void RejectsMissingDuplicateUnknownAndUntrustedRegistrations()
    {
        var missing = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.BuiltIn,
            BuiltInHandlers().Skip(1));
        var duplicate = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.BuiltIn,
            BuiltInHandlers().Append(new StubHandler(_operationalIds[0])));
        var unknown = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.BuiltIn,
            BuiltInHandlers().Append(new StubHandler("custom-handler")));
        var untrusted = ClosedExecutionHandlerRegistry.Create(
            BlueprintTrust.Untrusted,
            _operationalIds.Select(id => new StubHandler(id)));

        Assert.False(missing.IsValid);
        Assert.False(duplicate.IsValid);
        Assert.False(unknown.IsValid);
        Assert.False(untrusted.IsValid);
        Assert.All(
            new[] { missing, duplicate, unknown, untrusted },
            item => Assert.All(item.Issues, issue => Assert.Equal("DF-EXEC-001", issue.Code)));
    }

    [Fact]
    public void RegistrationSnapshotsCallerCollection()
    {
        var handlers = _operationalIds.Select(id => (IExecutionHandler)new StubHandler(id)).ToList();
        var result = ClosedExecutionHandlerRegistry.Create(BlueprintTrust.TrustedLocal, handlers);

        handlers.Clear();

        Assert.True(result.IsValid);
        Assert.NotNull(result.Value.Resolve("create-directory"));
    }

    [Fact]
    public void ProviderSelectsRegistryFromReopenedTrustAndSnapshotsHandlers()
    {
        var handlers = BuiltInHandlers().ToList();
        var provider = new ClosedExecutionHandlerRegistryProvider(handlers);
        handlers.Clear();

        var builtIn = provider.Create(BlueprintTrust.BuiltIn);
        var trustedLocal = provider.Create(BlueprintTrust.TrustedLocal);
        var untrusted = provider.Create(BlueprintTrust.Untrusted);

        Assert.True(builtIn.IsSuccessful);
        Assert.NotNull(builtIn.Value.Resolve("finalize-workspace"));
        Assert.True(trustedLocal.IsSuccessful);
        Assert.Null(trustedLocal.Value.Resolve("finalize-workspace"));
        Assert.False(untrusted.IsSuccessful);
        Assert.Equal("DF-EXEC-001", untrusted.Error?.Code);
    }

    [Fact]
    public void DispatchAndMaterializationUseNoReflectionOrDirectProcessBoundary()
    {
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var sources = _implementationSourceFiles.Select(file => File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "DevForge.Infrastructure",
            "Execution",
            file)));
        var combined = string.Join('\n', sources);

        Assert.DoesNotContain("System.Reflection", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("IProcessRunner", combined, StringComparison.Ordinal);
    }

    public static TheoryData<string> OperationalHandlerIds => new(_operationalIds);

    private static IEnumerable<IExecutionHandler> BuiltInHandlers()
    {
        return _operationalIds
            .Append("finalize-workspace")
            .Select(id => new StubHandler(id));
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevForge.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class StubHandler(string id) : IExecutionHandler
    {
        public string Id { get; } = id;

        public ExecutionResumeBehavior ResumeBehavior => ExecutionResumeBehavior.RevalidatePostcondition;

        public Task<ExecutionHandlerResult> PrepareAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExecutionHandlerResult> CheckPreconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExecutionHandlerResult> ExecuteAsync(
            ExecutionHandlerRequest request,
            IProgress<ExecutionProgressLine>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExecutionHandlerResult> CheckPostconditionsAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExecutionHandlerResult> CleanupForRetryAsync(
            ExecutionHandlerRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
