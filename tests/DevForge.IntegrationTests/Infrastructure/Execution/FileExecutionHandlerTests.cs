using System.Collections.Immutable;
using System.Text;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Validation;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Templates;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class FileExecutionHandlerTests
{
    [Fact]
    public async Task FileAndContentValidatorsAreBoundedReadOnlyAndReturnValidationFailures()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        await fixture.WriteAsync("README.md", "Framework: net10.0");
        var file = new FileExistsValidationHandler();
        var content = new FileContentValidationHandler();
        var fileRequest = fixture.ValidatorRequest(
            "validate-file-exists",
            ("path", Text("README.md")));
        var contentRequest = fixture.ValidatorRequest(
            "validate-file-content",
            ("path", Text("README.md")),
            ("contains", Text("net10.0")));
        var missingContentRequest = fixture.ValidatorRequest(
            "validate-file-content",
            ("path", Text("README.md")),
            ("contains", Text("net11.0")));
        await fixture.WriteAsync(
            "oversized.txt",
            new string('x', FileExecutionHandlerBase.MaximumFileBytes + 1));
        var oversizedRequest = fixture.ValidatorRequest(
            "validate-file-exists",
            ("path", Text("oversized.txt")));

        Assert.Equal(
            ExecutionHandlerOutcome.Succeeded,
            (await file.ExecuteAsync(fileRequest, null, CancellationToken.None)).Outcome);
        Assert.Equal(
            ExecutionHandlerOutcome.Succeeded,
            (await content.ExecuteAsync(contentRequest, null, CancellationToken.None)).Outcome);
        var failure = await content.ExecuteAsync(
            missingContentRequest,
            null,
            CancellationToken.None);
        Assert.Equal(ExecutionHandlerOutcome.Failed, failure.Outcome);
        Assert.Equal("DF-VALID-001", failure.Error?.Code);
        var oversized = await file.ExecuteAsync(
            oversizedRequest,
            null,
            CancellationToken.None);
        Assert.Equal(ExecutionHandlerOutcome.Failed, oversized.Outcome);
        Assert.Equal("DF-VALID-001", oversized.Error?.Code);
        Assert.Equal("Framework: net10.0", await fixture.ReadAsync("README.md"));
    }

    [Fact]
    public async Task CreateRenderAndCopyHandlersUseOnlyGuardedWorkspaces()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        var create = new CreateDirectoryExecutionHandler();
        var render = new RenderTemplateExecutionHandler(new RestrictedScribanTemplateRenderer());
        var copy = new CopyOverlayExecutionHandler();

        var createRequest = fixture.Request("create-directory", ("path", Text("src")));
        var renderRequest = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text("src\\App.txt")));
        var copyRequest = fixture.Request(
            "copy-overlay",
            ("source", Text("overlays\\base")),
            ("target", Text("assets")));

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await create.PrepareAsync(createRequest, default)).Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await create.ExecuteAsync(createRequest, null, default)).Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await render.ExecuteAsync(renderRequest, null, default)).Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await copy.ExecuteAsync(copyRequest, null, default)).Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await render.CheckPostconditionsAsync(renderRequest, default)).Outcome);
        Assert.Equal("Hello Sample App", await fixture.ReadAsync("src\\App.txt"));
        Assert.Equal("alpha", await fixture.ReadAsync("assets\\a.txt"));
        Assert.Equal("beta", await fixture.ReadAsync("assets\\nested\\b.txt"));
    }

    [Fact]
    public async Task StructuredPatchHandlersApplySetAndRemoveIdempotently()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        await fixture.WriteAsync("settings.json", "{\"name\":\"old\",\"remove\":\"x\"}");
        await fixture.WriteAsync("settings.yaml", "name: old\nremove: x\n");
        await fixture.WriteAsync(
            "Project.xml",
            "<Project><PropertyGroup><TargetFramework>net9.0</TargetFramework><Remove>x</Remove></PropertyGroup></Project>");

        var json = fixture.Request(
            "patch-json",
            ("target", Text("settings.json")),
            ("operations", Operations(
                Set("/name", Text("new")),
                Set("/nested/enabled", PlanValue.FromBoolean(true)),
                Remove("/remove"))));
        var yaml = fixture.Request(
            "patch-yaml",
            ("target", Text("settings.yaml")),
            ("operations", Operations(
                Set("/name", Text("new")),
                Set("/nested/enabled", PlanValue.FromBoolean(true)),
                Remove("/remove"))));
        var xml = fixture.Request(
            "patch-xml",
            ("target", Text("Project.xml")),
            ("operations", Operations(
                Set("/Project/PropertyGroup/TargetFramework", Text("net10.0")),
                Set("/Project/PropertyGroup/@Condition", Text("Release")),
                Remove("/Project/PropertyGroup/Remove"))));

        await AssertIdempotentAsync(new JsonPatchExecutionHandler(), json, fixture, "settings.json");
        await AssertIdempotentAsync(new YamlPatchExecutionHandler(), yaml, fixture, "settings.yaml");
        await AssertIdempotentAsync(new XmlPatchExecutionHandler(), xml, fixture, "Project.xml");

        Assert.Equal("{\"name\":\"new\",\"nested\":{\"enabled\":true}}", await fixture.ReadAsync("settings.json"));
        var yamlText = await fixture.ReadAsync("settings.yaml");
        Assert.Contains("name: new", yamlText, StringComparison.Ordinal);
        Assert.Contains("enabled: true", yamlText, StringComparison.Ordinal);
        Assert.DoesNotContain("remove:", yamlText, StringComparison.Ordinal);
        var xmlText = await fixture.ReadAsync("Project.xml");
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", xmlText, StringComparison.Ordinal);
        Assert.Contains("Condition=\"Release\"", xmlText, StringComparison.Ordinal);
        Assert.DoesNotContain("<Remove>", xmlText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("patch-json", "unsafe.json", "{\"x\":1,\"x\":2}", "/value")]
    [InlineData("patch-yaml", "unsafe.yaml", "base: &base\n  value: x\ncopy: *base\n", "/value")]
    [InlineData("patch-yaml", "unsafe.yaml", "value: !custom tagged\n", "/value")]
    [InlineData("patch-yaml", "unsafe.yaml", "x: one\nx: two\n", "/value")]
    [InlineData("patch-yaml", "unsafe.yaml", "base: { value: x }\nmerged: { <<: { value: y } }\n", "/value")]
    [InlineData("patch-xml", "unsafe.xml", "<!DOCTYPE x [<!ENTITY e 'unsafe'>]><x>&e;</x>", "/x/value")]
    [InlineData("patch-xml", "unsafe.xml", "<x xmlns=\"urn:test\"><value>old</value></x>", "/x/value")]
    [InlineData("patch-xml", "unsafe.xml", "<x />", "/x/bad:name")]
    public async Task MaliciousStructuredDocumentsFailWithoutMutation(
        string handlerId,
        string target,
        string original,
        string operationPath)
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        await fixture.WriteAsync(target, original);
        var request = fixture.Request(
            handlerId,
            ("target", Text(target)),
            ("operations", Operations(Set(operationPath, Text("safe")))));
        var handler = CreatePatchHandler(handlerId);

        var result = await handler.ExecuteAsync(request, null, CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Equal("DF-EXEC-001", result.Error?.Code);
        Assert.False(result.Error?.IsRetryable);
        Assert.Equal(original, await fixture.ReadAsync(target));
    }

    [Theory]
    [InlineData("..\\outside")]
    [InlineData(".env")]
    public async Task UnsafeOutputPathsAreRejectedBeforeMutation(string path)
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        var request = fixture.Request("create-directory", ("path", Text(path)));

        var result = await new CreateDirectoryExecutionHandler().ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Error?.IsRetryable);
        Assert.Empty(await fixture.Payload.EnumerateRootDirectoriesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EnvironmentExamplePathIsAllowed()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        var request = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text(".env.example")));

        var result = await new RenderTemplateExecutionHandler(
            new RestrictedScribanTemplateRenderer()).ExecuteAsync(
                request,
                null,
                CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, result.Outcome);
        Assert.True(await fixture.Payload.FileExistsAsync(
            Relative(".env.example"),
            CancellationToken.None));
    }

    [Fact]
    public async Task LockedRenderTargetPreservesOriginalContent()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        await fixture.WriteAsync("locked.txt", "original");
        using var locked = new FileStream(
            Path.Combine(fixture.RootPath, "locked.txt"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var request = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text("locked.txt")));

        var result = await new RenderTemplateExecutionHandler(
            new RestrictedScribanTemplateRenderer()).ExecuteAsync(request, null, CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.True(result.Error?.IsRetryable);
        Assert.Equal("original", await fixture.ReadAsync("locked.txt"));
    }

    [Fact]
    public async Task InvalidTemplateIsAStableNonRetryableFailure()
    {
        await using var fixture = await HandlerFixture.CreateAsync(malformedTemplate: true);
        var request = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text("generated.txt")));

        var result = await new RenderTemplateExecutionHandler(
            new RestrictedScribanTemplateRenderer()).ExecuteAsync(
                request,
                null,
                CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.False(result.Error?.IsRetryable);
        Assert.False(await fixture.Payload.FileExistsAsync(
            Relative("generated.txt"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAndOversizedTemplateDoNotPublishOutput()
    {
        await using var fixture = await HandlerFixture.CreateAsync(
            oversizedTemplate: true);
        var request = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text("generated.txt")));
        var handler = new RenderTemplateExecutionHandler(new RestrictedScribanTemplateRenderer());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(request, null, cancellation.Token));
        var oversized = await handler.ExecuteAsync(request, null, CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, oversized.Outcome);
        Assert.False(await fixture.Payload.FileExistsAsync(
            Relative("generated.txt"),
            CancellationToken.None));
    }

    [Fact]
    public async Task RetryCleanupRemovesOnlyDeclaredRenderAndOverlayFiles()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        await fixture.WriteAsync("unrelated.txt", "preserve");
        var render = new RenderTemplateExecutionHandler(new RestrictedScribanTemplateRenderer());
        var copy = new CopyOverlayExecutionHandler();
        var renderRequest = fixture.Request(
            "render-template",
            ("source", Text("templates\\app.txt")),
            ("target", Text("generated.txt")));
        var copyRequest = fixture.Request(
            "copy-overlay",
            ("source", Text("overlays\\base")),
            ("target", Text("assets")));
        await render.ExecuteAsync(renderRequest, null, CancellationToken.None);
        await copy.ExecuteAsync(copyRequest, null, CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await render.CleanupForRetryAsync(renderRequest, default)).Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await copy.CleanupForRetryAsync(copyRequest, default)).Outcome);

        Assert.False(await fixture.Payload.FileExistsAsync(Relative("generated.txt"), default));
        Assert.False(await fixture.Payload.FileExistsAsync(Relative("assets\\a.txt"), default));
        Assert.False(await fixture.Payload.FileExistsAsync(Relative("assets\\nested\\b.txt"), default));
        Assert.Equal("preserve", await fixture.ReadAsync("unrelated.txt"));
    }

    [Fact]
    public async Task OverlayEnvironmentFileIsRejectedBeforePayloadMutation()
    {
        await using var fixture = await HandlerFixture.CreateAsync(unsafeOverlay: true);
        var request = fixture.Request(
            "copy-overlay",
            ("source", Text("overlays\\base")),
            ("target", Text("assets")));

        var result = await new CopyOverlayExecutionHandler().ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Empty(await fixture.Payload.EnumerateAllFilesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StructuredPatchRejectsSecretShapedDestinationKey()
    {
        await using var fixture = await HandlerFixture.CreateAsync();
        const string original = "{\"safe\":true}";
        await fixture.WriteAsync("settings.json", original);
        var request = fixture.Request(
            "patch-json",
            ("target", Text("settings.json")),
            ("operations", Operations(Set("/databasePassword", Text("not-a-secret")))));

        var result = await new JsonPatchExecutionHandler().ExecuteAsync(
            request,
            null,
            CancellationToken.None);

        Assert.Equal(ExecutionHandlerOutcome.Failed, result.Outcome);
        Assert.Equal(original, await fixture.ReadAsync("settings.json"));
    }

    [Fact]
    public async Task HandlerRequestRejectsAStepNotOwnedByTheHashedPlan()
    {
        await using var fixture = await HandlerFixture.CreateAsync();

        var result = fixture.MismatchedPlanRequest();

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "handler.request.step.plan-mismatch");
    }

    private static async Task AssertIdempotentAsync(
        IExecutionHandler handler,
        ExecutionHandlerRequest request,
        HandlerFixture fixture,
        string target)
    {
        var first = await handler.ExecuteAsync(request, null, CancellationToken.None);
        var firstText = await fixture.ReadAsync(target);
        var second = await handler.ExecuteAsync(request, null, CancellationToken.None);
        var secondText = await fixture.ReadAsync(target);

        Assert.Equal(ExecutionHandlerOutcome.Succeeded, first.Outcome);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, second.Outcome);
        Assert.Equal(firstText, secondText);
        Assert.Equal(ExecutionHandlerOutcome.Succeeded, (await handler.CheckPostconditionsAsync(request, default)).Outcome);
    }

    private static IExecutionHandler CreatePatchHandler(string handlerId) => handlerId switch
    {
        "patch-json" => new JsonPatchExecutionHandler(),
        "patch-yaml" => new YamlPatchExecutionHandler(),
        "patch-xml" => new XmlPatchExecutionHandler(),
        _ => throw new InvalidOperationException(),
    };

    private static PlanValue Operations(params PlanValue[] operations) =>
        PlanValue.FromArray(operations).Value;

    private static PlanValue Set(string path, PlanValue value) =>
        Map(("op", Text("set")), ("path", Text(path)), ("value", value));

    private static PlanValue Remove(string path) =>
        Map(("op", Text("remove")), ("path", Text(path)));

    private static PlanValue Map(params (string Key, PlanValue Value)[] values) =>
        PlanValue.FromObject(values.Select(item =>
            KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value))).Value;

    private static PlanValue Text(string value) => PlanValue.FromString(value).Value;

    private static WorkspaceRelativePath Relative(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private sealed class HandlerFixture : IAsyncDisposable
    {
        private HandlerFixture(
            string rootPath,
            IWorkspaceFileSystem payload,
            BlueprintExecutionPackage package)
        {
            RootPath = rootPath;
            Payload = payload;
            Package = package;
        }

        public string RootPath { get; }

        public IWorkspaceFileSystem Payload { get; }

        private BlueprintExecutionPackage Package { get; }

        public static async Task<HandlerFixture> CreateAsync(
            bool oversizedTemplate = false,
            bool unsafeOverlay = false,
            bool malformedTemplate = false)
        {
            var rootPath = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "DevForge-M5-FileHandlers-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(rootPath);
            var payload = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var template = oversizedTemplate
                ? new string('x', TemplateRenderRequest.MaxTemplateLength + 1)
                : malformedTemplate
                    ? "{{ while true }}{{ break }}{{ end }}"
                    : "Hello {{ project.name }}";
            var packageFiles = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal)
            {
                ["templates/app.txt"] = [.. Encoding.UTF8.GetBytes(template)],
                ["overlays/base/a.txt"] = [.. Encoding.UTF8.GetBytes("alpha")],
                ["overlays/base/nested/b.txt"] = [.. Encoding.UTF8.GetBytes("beta")],
            }.ToImmutableDictionary(StringComparer.Ordinal);
            if (unsafeOverlay)
            {
                packageFiles = packageFiles.Add(
                    "overlays/base/.env",
                    [.. Encoding.UTF8.GetBytes("safe-looking-but-forbidden")]);
            }
            var checksum = $"sha256:{new string('a', 64)}";
            var packageWorkspace = VerifiedBlueprintWorkspace.Create(
                checksum,
                packageFiles,
                CancellationToken.None);
            var manifest = BlueprintManifest.Create(
                new BlueprintManifestDraft(
                    "sample.blueprint",
                    "1.0.0",
                    ">=1.0.0 <2.0.0",
                    [],
                    [],
                    [],
                    [],
                    []),
                new BlueprintTrustAssignment(BlueprintTrust.BuiltIn)).Value;
            var fingerprint = BlueprintFingerprint.Create(
                "built-in",
                Relative("sample.blueprint\\1.0.0"),
                BlueprintTrust.BuiltIn,
                checksum).Value;
            var resolved = ResolvedBlueprint.Create(manifest, [], fingerprint).Value;
            var package = BlueprintExecutionPackage.Create(resolved, packageWorkspace).Value;
            return new HandlerFixture(rootPath, payload, package);
        }

        public ExecutionHandlerRequest Request(
            string handler,
            params (string Key, PlanValue Value)[] inputs)
        {
            var step = ExecutionStep.Create(
                "step",
                "Step",
                handler,
                inputs.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                TimeSpan.FromMinutes(1),
                RetryPolicy.None).Value;
            var descriptor = StagingDescriptor.Create(
                Relative(".devforge-staging\\run-1"),
                Relative(".devforge-staging\\run-1\\payload"),
                Relative(".devforge-staging\\run-1\\ownership.json"),
                "marker-1").Value;
            var staging = StagingWorkspace.Create(descriptor, Payload).Value;
            var plan = ExecutionPlan.Create(
                $"sha256:{new string('b', 64)}",
                [step],
                [],
                [KeyValuePair.Create<string, string?>("project.name", "Sample App")]).Value;
            return ExecutionHandlerRequest.Create(
                "run-1",
                step,
                staging,
                Package,
                plan).Value;
        }

        public ExecutionHandlerRequest ValidatorRequest(
            string handler,
            params (string Key, PlanValue Value)[] inputs)
        {
            var validator = ExecutionValidator.Create(
                "validator",
                handler,
                inputs.Select(item => KeyValuePair.Create<string, PlanValue?>(item.Key, item.Value)),
                TimeSpan.FromMinutes(1),
                required: true).Value;
            var descriptor = StagingDescriptor.Create(
                Relative(".devforge-staging\\run-1"),
                Relative(".devforge-staging\\run-1\\payload"),
                Relative(".devforge-staging\\run-1\\ownership.json"),
                "marker-1").Value;
            var staging = StagingWorkspace.Create(descriptor, Payload).Value;
            var plan = ExecutionPlan.Create(
                $"sha256:{new string('b', 64)}",
                [],
                [validator],
                [KeyValuePair.Create<string, string?>("project.name", "Sample App")]).Value;
            return ExecutionHandlerRequest.Create(
                "run-1",
                validator,
                staging,
                Package,
                plan).Value;
        }

        public ValidationResult<ExecutionHandlerRequest> MismatchedPlanRequest()
        {
            var owned = ExecutionStep.Create(
                "owned",
                "Owned",
                "create-directory",
                [KeyValuePair.Create<string, PlanValue?>("path", Text("owned"))],
                TimeSpan.FromMinutes(1),
                RetryPolicy.None).Value;
            var supplied = ExecutionStep.Create(
                "supplied",
                "Supplied",
                "create-directory",
                [KeyValuePair.Create<string, PlanValue?>("path", Text("supplied"))],
                TimeSpan.FromMinutes(1),
                RetryPolicy.None).Value;
            var plan = ExecutionPlan.Create(
                $"sha256:{new string('b', 64)}",
                [owned],
                [],
                []).Value;
            var descriptor = StagingDescriptor.Create(
                Relative(".devforge-staging\\run-1"),
                Relative(".devforge-staging\\run-1\\payload"),
                Relative(".devforge-staging\\run-1\\ownership.json"),
                "marker-1").Value;
            var staging = StagingWorkspace.Create(descriptor, Payload).Value;
            return ExecutionHandlerRequest.Create(
                "run-1",
                supplied,
                staging,
                Package,
                plan);
        }

        public async Task WriteAsync(string path, string content)
        {
            await using var stream = await Payload.OpenWriteAsync(
                Relative(path),
                overwrite: true,
                CancellationToken.None);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(content), CancellationToken.None);
        }

        public async Task<string> ReadAsync(string path)
        {
            await using var stream = await Payload.OpenReadAsync(Relative(path), CancellationToken.None);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(CancellationToken.None);
        }

        public ValueTask DisposeAsync()
        {
            var fullPath = Path.GetFullPath(RootPath);
            if (!fullPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullPath).StartsWith(
                    "DevForge-M5-FileHandlers-",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to clean an unexpected handler fixture.");
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
