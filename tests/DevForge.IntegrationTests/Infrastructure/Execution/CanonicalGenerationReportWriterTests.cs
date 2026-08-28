using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Projects;
using DevForge.Domain.Reports;
using DevForge.Domain.Runs;
using DevForge.Infrastructure.Execution;
using DevForge.Infrastructure.FileSystem;
using DevForge.Infrastructure.Git;
using DevForge.Infrastructure.Security;

namespace DevForge.IntegrationTests.Infrastructure.Execution;

public sealed class CanonicalGenerationReportWriterTests
{
    [Fact]
    public async Task PythonDistMembershipPreservesReviewedFilesAndBindsExactRetry()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(WorkspaceRoot.Create(rootPath).Value, default);
            await workspace.CreateDirectoryAsync(Path("dist"), default);
            foreach (var file in new[] { "pyproject.toml", "uv.lock", "dist\\app.whl", "dist\\reviewed.whl" })
            {
                await using var stream = await workspace.OpenWriteAsync(Path(file), false, default);
                await stream.WriteAsync("sample\n"u8.ToArray());
            }
            string[] buildArguments = ["run", "--frozen", "--no-sync", "--no-config", "pyproject-build", "--no-isolation"];
            var validator = ExecutionValidator.Create("build", "validate-command",
                new Dictionary<string, PlanValue?>
                {
                    ["executable"] = PlanValue.FromString("uv").Value,
                    ["workingDirectory"] = PlanValue.FromString(".").Value,
                    ["arguments"] = PlanValue.FromArray(buildArguments
                        .Select(item => (PlanValue?)PlanValue.FromString(item).Value)).Value,
                }, TimeSpan.FromSeconds(30), true).Value;
            var checkpoint = Checkpoint(workspace.Root, validators: [validator], artifacts:
                [new BlueprintArtifact("pyproject.toml"), new BlueprintArtifact("uv.lock"), new BlueprintArtifact("dist\\reviewed.whl")]);
            var writer = new CanonicalProjectEvidenceWriter();
            Assert.True((await writer.WriteAsync(checkpoint, EvidenceReport(checkpoint), workspace, default)).IsSuccessful);
            Assert.True(await workspace.FileExistsAsync(ProjectEvidencePathPolicy.BuildOutputsPath, default));
            var tree = await CanonicalProjectTree.CaptureAsync(workspace, false, default);
            Assert.DoesNotContain(tree.SourceFiles, path => path.Value == "dist\\app.whl");
            Assert.Contains(tree.SourceFiles, path => path.Value == "dist\\reviewed.whl");
            Assert.Contains(tree.AllFiles, path => path.Value == "dist\\app.whl");
            Assert.True((await writer.WriteAsync(checkpoint, EvidenceReport(checkpoint), workspace, default)).IsSuccessful);
        }
        finally { Directory.Delete(rootPath, recursive: true); }
    }

    [Fact]
    public async Task LargeBuildOutputEvidenceIsScannableAndExactRetryPreservesReviewedSource()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(WorkspaceRoot.Create(rootPath).Value, default);
            await workspace.CreateDirectoryAsync(Path("src\\obj"), default);
            async Task Write(string path, string text)
            {
                await using var stream = await workspace.OpenWriteAsync(Path(path), false, default);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
            }
            await Write("src\\App.csproj", "<Project />\n");
            await Write("src\\obj\\Reviewed.cs", "reviewed source\n");
            for (var index = 0; index < 200; index++)
            {
                await Write($"src\\obj\\{index:D3}-{new string('x', 100)}.json", "{}\n");
            }
            var validator = ExecutionValidator.Create("build", "validate-command",
                new Dictionary<string, PlanValue?>
                {
                    ["executable"] = PlanValue.FromString("dotnet").Value,
                    ["workingDirectory"] = PlanValue.FromString(".").Value,
                }, TimeSpan.FromSeconds(30), true).Value;
            var checkpoint = Checkpoint(workspace.Root, validators: [validator], artifacts:
                [new BlueprintArtifact("src\\App.csproj"), new BlueprintArtifact("src\\obj\\Reviewed.cs")]);
            var writer = new CanonicalProjectEvidenceWriter();
            var first = await writer.WriteAsync(checkpoint, EvidenceReport(checkpoint), workspace, default);
            Assert.True(first.IsSuccessful);
            var marker = await ReadAsync(workspace, ProjectEvidencePathPolicy.BuildOutputsPath);
            Assert.True(marker.Length > 16_384);
            var scan = await new WorkspaceSecretScanner().ScanAsync(SecretScanRequest.WholeWorkspace(workspace).Value, default);
            Assert.Empty(scan.Findings);
            var tree = await CanonicalProjectTree.CaptureAsync(workspace, false, default);
            Assert.Contains(tree.SourceFiles, path => path.Value == "src\\obj\\Reviewed.cs");
            var retry = await writer.WriteAsync(checkpoint, EvidenceReport(checkpoint), workspace, default);
            Assert.True(retry.IsSuccessful);
            Assert.Equal(marker, await ReadAsync(workspace, ProjectEvidencePathPolicy.BuildOutputsPath));
            await using (var tamper = await workspace.OpenWriteAsync(ProjectEvidencePathPolicy.BuildOutputsPath, true, default))
            {
                await tamper.WriteAsync("{}"u8.ToArray());
            }
            var refused = await writer.WriteAsync(checkpoint, EvidenceReport(checkpoint), workspace, default);
            Assert.False(refused.IsSuccessful);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static readonly WorkspaceRelativePath[] _evidencePaths =
    [
        Path(".devforge\\project.recipe.yaml"),
        Path("devforge.lock.json"),
        Path("generation-report.json"),
        Path("policy.snapshot.json"),
    ];

    [Fact]
    public void ProjectEvidenceReceiptRequiresCanonicalPathOrderAndDigestCount()
    {
        var digests = Enumerable.Range(1, 4)
            .Select(index => $"sha256:{new string((char)('0' + index), 64)}")
            .ToArray();

        Assert.False(ProjectEvidenceWriteReceipt.Create(_evidencePaths.Reverse(), digests).IsValid);
        Assert.False(ProjectEvidenceWriteReceipt.Create(_evidencePaths, digests.Take(3)).IsValid);
        Assert.True(ProjectEvidenceWriteReceipt.Create(_evidencePaths, digests).IsValid);
    }

    [Fact]
    public async Task ProjectEvidenceRetryAdoptsMatchingCanonicalFileAndCompletesMissingFiles()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root);
            var report = EvidenceReport(checkpoint);
            var writer = new CanonicalProjectEvidenceWriter();

            var first = await writer.WriteAsync(checkpoint, report, workspace, CancellationToken.None);
            Assert.True(first.IsSuccessful);
            var canonical = await Task.WhenAll(_evidencePaths.Select(path => ReadAsync(workspace, path)));
            for (var retainedCount = 0; retainedCount <= _evidencePaths.Length; retainedCount++)
            {
                foreach (var path in _evidencePaths)
                {
                    File.Delete(System.IO.Path.Combine(rootPath, path.Value));
                }

                for (var index = 0; index < retainedCount; index++)
                {
                    var path = _evidencePaths[index];
                    var separator = path.Value.LastIndexOf('\\');
                    if (separator > 0)
                    {
                        var directory = Path(path.Value[..separator]);
                        if (!await workspace.DirectoryExistsAsync(directory, CancellationToken.None))
                        {
                            await workspace.CreateDirectoryAsync(directory, CancellationToken.None);
                        }
                    }

                    await using var output = await workspace.OpenWriteAsync(
                        path,
                        overwrite: false,
                        CancellationToken.None);
                    await output.WriteAsync(Encoding.UTF8.GetBytes(canonical[index]), CancellationToken.None);
                }

                var retry = await writer.WriteAsync(checkpoint, report, workspace, CancellationToken.None);

                Assert.True(retry.IsSuccessful);
                var recovered = await Task.WhenAll(_evidencePaths.Select(path => ReadAsync(workspace, path)));
                Assert.Equal(canonical, recovered);
            }
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyCheckpointWithoutEngineVersionWritesDeterministicNotRecordedProvenance()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, includeEngineVersion: false);
            var report = EvidenceReport(checkpoint);
            var writer = new CanonicalProjectEvidenceWriter();

            var first = await writer.WriteAsync(checkpoint, report, workspace, CancellationToken.None);
            Assert.True(first.IsSuccessful, first.Error?.Summary);
            var firstBytes = await ReadAsync(workspace, Path("generation-report.json"));
            var retry = await writer.WriteAsync(checkpoint, report, workspace, CancellationToken.None);
            var retryBytes = await ReadAsync(workspace, Path("generation-report.json"));

            Assert.True(retry.IsSuccessful);
            Assert.Equal(firstBytes, retryBytes);
            using var document = JsonDocument.Parse(firstBytes);
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("engineVersion").ValueKind);
            Assert.Equal(
                "not-recorded",
                document.RootElement.GetProperty("engineVersionStatus").GetString());
            using var legacyLock = JsonDocument.Parse(
                await ReadAsync(workspace, Path("devforge.lock.json")));
            Assert.Equal(JsonValueKind.Null, legacyLock.RootElement.GetProperty("engineVersion").ValueKind);
            Assert.Equal(
                "not-recorded",
                legacyLock.RootElement.GetProperty("engineVersionStatus").GetString());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectEvidenceRejectsSecretShapedBytesBeforePersistingAnyFile()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root);
            var report = GenerationReport.Create(
                checkpoint.Run.Id,
                DateTimeOffset.UnixEpoch,
                [],
                [],
                ["github_pat_abcdefghijklmnopqrstuvwxyz"]).Value;

            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint,
                report,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            foreach (var path in _evidencePaths)
            {
                Assert.False(await workspace.FileExistsAsync(path, CancellationToken.None));
            }
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectEvidenceRejectsMismatchedExistingBytesWithoutWritingMissingFiles()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            await using (var output = await workspace.OpenWriteAsync(
                Path("devforge.lock.json"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("tampered"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(workspace.Root);
            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint,
                EvidenceReport(checkpoint),
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("tampered", await ReadAsync(workspace, Path("devforge.lock.json")));
            foreach (var path in _evidencePaths.Where(path => path.Value != "devforge.lock.json"))
            {
                Assert.False(await workspace.FileExistsAsync(path, CancellationToken.None));
            }
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task TargetGenerationReportIncludesVersionStepsArtifactsAndIntegrityEvidence()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root);

            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint,
                EvidenceReport(checkpoint),
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            var json = await ReadAsync(workspace, Path("generation-report.json"));
            using var projectDocument = JsonDocument.Parse(json);
            Assert.Equal(
                "devforge-project-generation-report-v1",
                projectDocument.RootElement.GetProperty("schema").GetString());
            Assert.Equal(
                "validated-pre-finalization",
                projectDocument.RootElement.GetProperty("capturePhase").GetString());
            Assert.Equal("1.0.0", projectDocument.RootElement.GetProperty("engineVersion").GetString());
            Assert.Equal(JsonValueKind.Array, projectDocument.RootElement.GetProperty("warnings").ValueKind);
            Assert.Equal(JsonValueKind.Array, projectDocument.RootElement.GetProperty("errors").ValueKind);
            Assert.Contains("\"toolStatuses\"", json, StringComparison.Ordinal);
            Assert.Contains("\"stepResults\"", json, StringComparison.Ordinal);
            Assert.Contains("\"artifactSummary\"", json, StringComparison.Ordinal);
            var lockJson = await ReadAsync(workspace, Path("devforge.lock.json"));
            Assert.Contains("\"evidenceDigests\"", lockJson, StringComparison.Ordinal);
            using var lockDocument = JsonDocument.Parse(lockJson);
            Assert.Equal("1.0.0", lockDocument.RootElement.GetProperty("engineVersion").GetString());
            Assert.Equal(
                "recorded",
                lockDocument.RootElement.GetProperty("engineVersionStatus").GetString());
            Assert.Equal(4, result.Value.Paths.Length);
            Assert.Equal(4, result.Value.Digests.Length);

            var runResult = await new CanonicalGenerationReportWriter().WriteAsync(
                checkpoint,
                EvidenceReport(checkpoint),
                workspace,
                CancellationToken.None);
            Assert.True(runResult.IsSuccessful);
            using var runDocument = JsonDocument.Parse(
                await ReadAsync(workspace, runResult.Value.JsonReport));
            Assert.Equal(
                "devforge-generation-report-v1",
                runDocument.RootElement.GetProperty("schema").GetString());
            Assert.NotEqual(
                runDocument.RootElement.GetProperty("schema").GetString(),
                projectDocument.RootElement.GetProperty("schema").GetString());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectReportCapturesImmutablePreFinalizationBoundaryAcrossCheckpointPhases()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var bundles = new List<string[]>();
            foreach (var phase in new[]
            {
                FinalizationState.NotStarted,
                FinalizationState.IntentPersisted,
                FinalizationState.Succeeded,
            })
            {
                var phaseRoot = System.IO.Path.Combine(rootPath, phase.ToString());
                Directory.CreateDirectory(phaseRoot);
                var workspace = await fileSystem.OpenWorkspaceAsync(
                    WorkspaceRoot.Create(phaseRoot).Value,
                    CancellationToken.None);
                var checkpoint = Checkpoint(workspace.Root, phase);

                var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                    checkpoint,
                    EvidenceReport(checkpoint),
                    workspace,
                    CancellationToken.None);

                Assert.True(result.IsSuccessful, result.Error?.Summary);
                bundles.Add(await Task.WhenAll(
                    _evidencePaths.Select(path => ReadAsync(workspace, path))));
            }

            Assert.All(bundles, bundle =>
            {
                var report = bundle[2];
                using var document = JsonDocument.Parse(report);
                Assert.Equal(
                    "validated-pre-finalization",
                    document.RootElement.GetProperty("capturePhase").GetString());
                Assert.False(document.RootElement.TryGetProperty("checkpoint", out _));
            });
            Assert.All(bundles.Skip(1), bundle => Assert.Equal(bundles[0], bundle));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectRecipeEvidenceIncludesExactReviewedGitAndTeamIntent()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var git = GitOptions.Create(
                initializeRepository: true,
                useDevelopBranch: true,
                publishToGitHub: true,
                isPrivate: true,
                githubAccount: "octocat",
                githubRepository: "team-tool").Value;
            var checkpoint = Checkpoint(
                workspace.Root,
                git: git,
                teamProfileId: "platform-team",
                teamProfileName: "Platform Team",
                teamStandardsJson: "{\"company-name\":\"Acme\",\"root-namespace\":\"Acme.Tools\"}");

            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint,
                EvidenceReport(checkpoint),
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            var recipe = await ReadAsync(workspace, Path(".devforge\\project.recipe.yaml"));
            Assert.Contains("projectName: \"project\"", recipe, StringComparison.Ordinal);
            Assert.Contains("projectNameStatus: recorded", recipe, StringComparison.Ordinal);
            Assert.Contains("  initializeRepository: true", recipe, StringComparison.Ordinal);
            Assert.Contains("  primaryBranch: \"main\"", recipe, StringComparison.Ordinal);
            Assert.Contains("  useDevelopBranch: true", recipe, StringComparison.Ordinal);
            Assert.Contains("  publishToGitHub: true", recipe, StringComparison.Ordinal);
            Assert.Contains("  isPrivate: true", recipe, StringComparison.Ordinal);
            Assert.Contains("  githubAccount: \"octocat\"", recipe, StringComparison.Ordinal);
            Assert.Contains("  githubRepository: \"team-tool\"", recipe, StringComparison.Ordinal);
            Assert.Contains("team:\n  id: \"platform-team\"", recipe.Replace("\r\n", "\n"), StringComparison.Ordinal);
            Assert.Contains("teamSnapshotStatus: recorded", recipe, StringComparison.Ordinal);
            Assert.Contains("  name: \"Platform Team\"", recipe, StringComparison.Ordinal);
            Assert.Contains("    \"company-name\": \"Acme\"", recipe, StringComparison.Ordinal);
            Assert.Contains("    \"root-namespace\": \"Acme.Tools\"", recipe, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "recorded", "none")]
    [InlineData(false, "not-recorded", "not-recorded")]
    public async Task ProjectRecipeDistinguishesNewNoTeamFromMissingLegacyContext(
        bool includeProjectContext,
        string expectedProjectStatus,
        string expectedTeamStatus)
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(
                workspace.Root,
                includeProjectName: includeProjectContext,
                includeTeamSnapshotStatus: includeProjectContext);

            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint,
                EvidenceReport(checkpoint),
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            var recipe = await ReadAsync(workspace, Path(".devforge\\project.recipe.yaml"));
            Assert.Contains($"projectNameStatus: {expectedProjectStatus}", recipe, StringComparison.Ordinal);
            Assert.Contains($"teamSnapshotStatus: {expectedTeamStatus}", recipe, StringComparison.Ordinal);
            Assert.Contains("team: null", recipe, StringComparison.Ordinal);
            Assert.Contains(
                includeProjectContext ? "projectName: \"project\"" : "projectName: null",
                recipe,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task TargetReportPersistsExactStepValidationAndCheckpointResultsWithoutTimestamps()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var step = ExecutionStep.Create(
                "build", "Build", "run-process", [], TimeSpan.FromMinutes(1), RetryPolicy.None).Value;
            var validator = ExecutionValidator.Create(
                "quality", "validate-command", [], TimeSpan.FromMinutes(1), required: false).Value;
            var stepError = SafeError("DF-EXEC-001", "Build failed.", "private diagnostic omitted");
            var validationError = SafeError(
                "DF-VAL-001", "Optional quality check failed.", "private diagnostic omitted");
            var startedAt = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
            var run = ProjectRun.Create("run-report", "recipe-1").Value
                .TransitionTo(RunStatus.Planning).Value
                .TransitionTo(RunStatus.Executing).Value
                .StartAttempt("build", startedAt).Value
                .CompleteAttempt(
                    "build", 1, StepAttemptOutcome.Failed, startedAt.AddMilliseconds(1250),
                    1, stepError, $"sha256:{new string('4', 64)}").Value;
            var evidence = new[]
            {
                ExecutionEvidence.Create(
                    ExecutionEvidenceKind.Step,
                    "build",
                    ExecutionEvidenceStatus.Failed,
                    $"sha256:{new string('4', 64)}",
                    startedAt,
                    startedAt.AddMilliseconds(1250),
                    stepError.Code,
                    stepError.Summary).Value,
                ExecutionEvidence.Create(
                    ExecutionEvidenceKind.Validator,
                    "quality",
                    ExecutionEvidenceStatus.Warning,
                    $"sha256:{new string('5', 64)}",
                    startedAt.AddSeconds(2),
                    startedAt.AddMilliseconds(2250),
                    validationError.Code,
                    validationError.Summary).Value,
            };
            var checkpoint = Checkpoint(
                workspace.Root,
                run: run,
                steps: [step],
                validators: [validator],
                evidence: evidence);
            var report = GenerationReport.Create(
                run.Id,
                DateTimeOffset.UnixEpoch,
                [new ValidationCheck("quality", ValidationCheckStatus.Warning, validationError.Summary, null)],
                [],
                ["src\\App.csproj"]).Value;

            var result = await new CanonicalProjectEvidenceWriter().WriteAsync(
                checkpoint, report, workspace, CancellationToken.None);

            Assert.True(result.IsSuccessful);
            using var document = JsonDocument.Parse(
                await ReadAsync(workspace, Path("generation-report.json")));
            var root = document.RootElement;
            Assert.False(root.TryGetProperty("checkpoint", out _));
            var stepResult = Assert.Single(root.GetProperty("stepResults").EnumerateArray());
            Assert.Equal(1250, stepResult.GetProperty("durationMilliseconds").GetInt64());
            Assert.Equal("Failed", stepResult.GetProperty("checkpointStatus").GetString());
            Assert.Equal("sha256:" + new string('4', 64), stepResult.GetProperty("outputDigest").GetString());
            Assert.Equal("DF-EXEC-001", stepResult.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("Build failed.", stepResult.GetProperty("error").GetProperty("summary").GetString());
            Assert.DoesNotContain("private diagnostic omitted", root.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("2026-08-26", root.GetRawText(), StringComparison.Ordinal);
            var validation = Assert.Single(root.GetProperty("validations").EnumerateArray());
            Assert.Equal(250, validation.GetProperty("durationMilliseconds").GetInt64());
            Assert.Equal("Warning", validation.GetProperty("checkpointStatus").GetString());
            Assert.Equal("sha256:" + new string('5', 64), validation.GetProperty("outputDigest").GetString());
            Assert.Equal("advisory", validation.GetProperty("severity").GetString());
            Assert.False(validation.GetProperty("required").GetBoolean());
            Assert.Equal("DF-VAL-001", validation.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(
                "Optional quality check failed.",
                validation.GetProperty("error").GetProperty("summary").GetString());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void CrossVolumeTreeComparerUsesExactOrdinalPathIdentity()
    {
        var comparerType = typeof(AtomicProjectFinalizer).GetNestedType(
            "WorkspacePathComparer",
            BindingFlags.NonPublic);
        var instance = comparerType?.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var comparer = Assert.IsAssignableFrom<IEqualityComparer<WorkspaceRelativePath>>(instance);

        Assert.False(comparer.Equals(Path("src\\App.cs"), Path("src\\app.cs")));
    }

    [Fact]
    public async Task WritesBoundedCanonicalJsonAndMarkdownWithoutWorkspacePaths()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root);
            var report = GenerationReport.Create(
                checkpoint.Run.Id,
                new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                [new ValidationCheck("build", ValidationCheckStatus.Passed, "Build passed.", null)],
                [new ReportToolStatus("dotnet", true, true, true, "10.0.0")],
                [new ReportWarning(
                    "planner.optional",
                    DevForge.Domain.Privacy.RedactedText.FromTrustedRedaction(
                        "An optional capability was not selected.").Value)],
                [],
                ["src\\App.csproj"]).Value;
            var writer = new CanonicalGenerationReportWriter();

            var result = await writer.WriteAsync(
                checkpoint,
                report,
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            var json = await ReadAsync(workspace, result.Value.JsonReport);
            var markdown = await ReadAsync(workspace, result.Value.MarkdownReport);
            Assert.Contains(checkpoint.PlanHash, json, StringComparison.Ordinal);
            Assert.Contains("desktop.csharp-wpf-tool", json, StringComparison.Ordinal);
            Assert.Contains("\"toolStatuses\"", json, StringComparison.Ordinal);
            Assert.Contains("\"warnings\"", json, StringComparison.Ordinal);
            Assert.Contains("dotnet", markdown, StringComparison.Ordinal);
            Assert.Contains("planner.optional", markdown, StringComparison.Ordinal);
            Assert.Contains("Build passed.", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(rootPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(rootPath, markdown, StringComparison.OrdinalIgnoreCase);
            Assert.True(Encoding.UTF8.GetByteCount(json) <= CanonicalGenerationReportWriter.MaximumReportBytes);
            Assert.True(Encoding.UTF8.GetByteCount(markdown) <= CanonicalGenerationReportWriter.MaximumReportBytes);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerMovesPayloadAtomicallyWithoutOverwritingTarget()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(workspace, checkpoint, corruptPlanHash: false);
            await using (var output = await workspace.OpenWriteAsync(
                Path(".devforge-staging\\run-report\\payload\\app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("hello"u8.ToArray(), CancellationToken.None);
            }

            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;
            var finalizer = new AtomicProjectFinalizer();

            var result = await finalizer.FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            Assert.True(await workspace.FileExistsAsync(
                Path("project\\app.txt"),
                CancellationToken.None));
            Assert.False(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.StartsWith("sha256:", result.Value.TreeDigest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task ReportWriterReturnsScrubbedFailureForGuardedWorkspaceOperationError()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var workspace = await new WindowsFileSystem().OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            await using (var blocker = await workspace.OpenWriteAsync(
                Path("reports"),
                overwrite: false,
                CancellationToken.None))
            {
                await blocker.WriteAsync("blocked"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(workspace.Root);
            var report = GenerationReport.Create(
                checkpoint.Run.Id,
                DateTimeOffset.UnixEpoch,
                [],
                [],
                []).Value;

            var result = await new CanonicalGenerationReportWriter().WriteAsync(
                checkpoint,
                report,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerRejectsTamperedOwnershipMarkerAndRetainsPayload()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(workspace, checkpoint, corruptPlanHash: true);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.False(await workspace.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerReturnsScrubbedFailureWhenOwnershipMarkerIsMissing()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, payload).Value,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerRefusesAnExistingTargetWithoutChangingIt()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var workspace = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(workspace.Root, FinalizationState.IntentPersisted);
            await workspace.CreateDirectoryAsync(checkpoint.Staging.PayloadDirectory, CancellationToken.None);
            await workspace.CreateDirectoryAsync(checkpoint.Target.TargetDirectory, CancellationToken.None);
            var payload = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(System.IO.Path.Combine(
                    rootPath,
                    checkpoint.Staging.PayloadDirectory.Value)).Value,
                CancellationToken.None);
            var staging = StagingWorkspace.Create(checkpoint.Staging, payload).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                workspace,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.True(await workspace.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task DetachedPayloadUsesVerifiedCopyThenAtomicTargetRename()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "source");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            await source.CreateDirectoryAsync(Path("src"), CancellationToken.None);
            await using (var output = await source.OpenWriteAsync(
                Path("src\\app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("verified copy"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);
            var staging = StagingWorkspace.Create(checkpoint.Staging, source).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                target,
                CancellationToken.None);

            Assert.True(result.IsSuccessful);
            Assert.True(await target.FileExistsAsync(
                Path("project\\src\\app.txt"),
                CancellationToken.None));
            Assert.False(await target.DirectoryExistsAsync(
                Path(".devforge-finalize-run-report"),
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static RunCheckpoint Checkpoint(
        WorkspaceRoot artifactRoot,
        FinalizationState finalizationState = FinalizationState.NotStarted,
        GitOptions? git = null,
        string? teamProfileId = null,
        string? teamProfileName = null,
        string? teamStandardsJson = null,
        ProjectRun? run = null,
        IEnumerable<ExecutionStep?>? steps = null,
        IEnumerable<ExecutionValidator?>? validators = null,
        IEnumerable<ExecutionEvidence?>? evidence = null,
        bool includeEngineVersion = true,
        bool includeProjectName = true,
        bool includeTeamSnapshotStatus = true,
        IEnumerable<BlueprintArtifact?>? artifacts = null)
    {
        var hash = $"sha256:{new string('1', 64)}";
        var checkpointRun = run ?? ProjectRun.Create("run-report", "recipe-1").Value
            .TransitionTo(RunStatus.Planning).Value
            .TransitionTo(RunStatus.Executing).Value;
        var context = new List<KeyValuePair<string, string?>>();
        if (includeProjectName)
        {
            context.Add(KeyValuePair.Create<string, string?>("project.name", "project"));
        }
        if (includeTeamSnapshotStatus)
        {
            context.Add(KeyValuePair.Create<string, string?>("team.snapshot_status", "none"));
        }
        if (includeEngineVersion)
        {
            context.Add(KeyValuePair.Create<string, string?>("engine.version", "1.0.0"));
        }
        if (teamProfileId is not null && teamProfileName is not null && teamStandardsJson is not null)
        {
            context.RemoveAll(item => item.Key == "team.snapshot_status");
            context.Add(KeyValuePair.Create<string, string?>("team.snapshot_status", "recorded"));
            context.Add(KeyValuePair.Create<string, string?>("team.profile_id", teamProfileId));
            context.Add(KeyValuePair.Create<string, string?>("team.profile_name", teamProfileName));
            context.Add(KeyValuePair.Create<string, string?>("team.standards_json", teamStandardsJson));
        }

        var stepSnapshot = steps?.ToArray() ?? [];
        var validatorSnapshot = validators?.ToArray() ?? [];
        var plan = ExecutionPlan.Create(
            hash,
            stepSnapshot,
            validatorSnapshot,
            context).Value;
        var blueprint = BlueprintReference.Create("desktop.csharp-wpf-tool", "1.0.0").Value;
        var fingerprint = BlueprintFingerprint.Create(
            "built-in",
            Path("desktop.csharp-wpf-tool\\1.0.0"),
            BlueprintTrust.BuiltIn,
            $"sha256:{new string('2', 64)}").Value;
        var preview = PlanPreview.Create(
            blueprint,
            stepSnapshot.Select(item => new PlanPreviewStep(item!.Id, item.Handler, item.Timeout)),
            validatorSnapshot.Select(item => new PlanPreviewValidator(
                item!.Id,
                item.Handler,
                item.Timeout,
                item.Required)),
            [],
            [],
            [],
            artifacts ?? [],
            [],
            [],
            [],
            git ?? GitOptions.Create().Value,
            CompletionOptions.Create().Value,
            hash).Value;
        return RunCheckpoint.Create(
            checkpointRun,
            plan,
            preview,
            blueprint,
            fingerprint,
            StagingDescriptor.Create(
                Path(".devforge-staging\\run-report"),
                Path(".devforge-staging\\run-report\\payload"),
                Path(".devforge-staging\\run-report\\ownership.json"),
                "run-report").Value,
            TargetDescriptor.Create(artifactRoot, Path("project"), null).Value,
            RunArtifactDescriptor.Create(artifactRoot).Value,
            evidence ?? [],
            finalizationState,
            ReportPersistenceState.NotStarted).Value;
    }

    private static GenerationReport EvidenceReport(RunCheckpoint checkpoint) => GenerationReport.Create(
        checkpoint.Run.Id,
        DateTimeOffset.UnixEpoch,
        [new ValidationCheck("secret-scan", ValidationCheckStatus.Passed, "No secrets detected.", null)],
        [new ReportToolStatus("dotnet", true, true, true, "10.0.302")],
        [],
        [],
        ["src\\App.csproj"]).Value;

    private static DevForgeError SafeError(string code, string summary, string detail) =>
        DevForgeError.Create(
            code,
            summary,
            RedactedText.FromTrustedRedaction(detail).Value,
            "test",
            null,
            true,
            ["Review the safe failure."],
            []).Value;

    private static async Task<string> ReadAsync(
        IWorkspaceFileSystem workspace,
        WorkspaceRelativePath path)
    {
        await using var stream = await workspace.OpenReadAsync(path, CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CorruptedCrossVolumeCopyIsRejectedAndTemporaryDirectoryIsRemoved()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "source");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var realTarget = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            await using (var output = await source.OpenWriteAsync(
                Path("app.txt"),
                overwrite: false,
                CancellationToken.None))
            {
                await output.WriteAsync("verified bytes"u8.ToArray(), CancellationToken.None);
            }

            var checkpoint = Checkpoint(realTarget.Root, FinalizationState.IntentPersisted);
            await realTarget.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(realTarget, checkpoint, corruptPlanHash: false);
            var corruptingTarget = new CorruptingAtomicWorkspace(
                Assert.IsAssignableFrom<IAtomicWorkspaceFileSystem>(realTarget));

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, source).Value,
                corruptingTarget,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.Equal("DF-FINAL-001", result.Error?.Code);
            Assert.False(await realTarget.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
            Assert.False(await realTarget.DirectoryExistsAsync(
                Path(".devforge-finalize-run-report"),
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeFinalizerRejectsPayloadWorkspaceThatDoesNotOwnDescriptorPath()
    {
        var rootPath = TestRoot();
        var sourcePath = System.IO.Path.Combine(rootPath, "detached");
        var targetPath = System.IO.Path.Combine(rootPath, "target-parent");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var source = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(sourcePath).Value,
                CancellationToken.None);
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(targetPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                StagingWorkspace.Create(checkpoint.Staging, source).Value,
                target,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.True(await target.DirectoryExistsAsync(
                checkpoint.Staging.PayloadDirectory,
                CancellationToken.None));
            Assert.False(await target.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task FinalizerRejectsPayloadBeyondTheExplicitFileCountBound()
    {
        var rootPath = TestRoot();
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileSystem = new WindowsFileSystem();
            var target = await fileSystem.OpenWorkspaceAsync(
                WorkspaceRoot.Create(rootPath).Value,
                CancellationToken.None);
            var checkpoint = Checkpoint(target.Root, FinalizationState.IntentPersisted);
            await target.CreateDirectoryAsync(
                checkpoint.Staging.ContainerDirectory,
                CancellationToken.None);
            await WriteMarkerAsync(target, checkpoint, corruptPlanHash: false);
            var source = new ManyFilesWorkspace(AtomicProjectFinalizer.MaximumFileCount + 1);
            var staging = StagingWorkspace.Create(checkpoint.Staging, source).Value;

            var result = await new AtomicProjectFinalizer().FinalizeAsync(
                checkpoint,
                staging,
                target,
                CancellationToken.None);

            Assert.False(result.IsSuccessful);
            Assert.False(await target.DirectoryExistsAsync(
                checkpoint.Target.TargetDirectory,
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task WriteMarkerAsync(
        IWorkspaceFileSystem workspace,
        RunCheckpoint checkpoint,
        bool corruptPlanHash)
    {
        var planHash = corruptPlanHash
            ? $"sha256:{new string('f', 64)}"
            : checkpoint.PlanHash;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            markerId = checkpoint.Staging.MarkerId,
            runId = checkpoint.Run.Id,
            planHash,
            blueprintId = checkpoint.Blueprint.Id,
            blueprintVersion = checkpoint.Blueprint.Version,
            blueprintChecksum = checkpoint.BlueprintFingerprint.AggregateChecksum,
            lifecycleIntent = "staging",
        });
        await using var output = await workspace.OpenWriteAsync(
            checkpoint.Staging.MarkerFile,
            overwrite: false,
            CancellationToken.None);
        await output.WriteAsync(bytes, CancellationToken.None);
        await output.FlushAsync(CancellationToken.None);
    }

    private static WorkspaceRelativePath Path(string value) =>
        WorkspaceRelativePath.Create(value).Value;

    private static string TestRoot() => System.IO.Path.GetFullPath(System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "DevForge.ReportWriterTests",
        Guid.NewGuid().ToString("N")));

    private sealed class ManyFilesWorkspace(int count) : IWorkspaceFileSystem
    {
        private readonly System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath> _files =
            Enumerable.Range(0, count)
                .Select(index => Path($"files\\f{index:D5}.txt"))
                .ToImmutableArray();

        public WorkspaceRoot Root { get; } = WorkspaceRoot.Create("C:\\bounded-source").Value;

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(
            CancellationToken cancellationToken) => Task.FromResult(_files);

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                System.Collections.Immutable.ImmutableArray.Create(Path("files")));

        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(
            WorkspaceRelativePath directory,
            CancellationToken cancellationToken) => Task.FromResult(
                System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>.Empty);

        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(WorkspaceRelativePath path, bool overwrite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<System.Collections.Immutable.ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CorruptingAtomicWorkspace(IAtomicWorkspaceFileSystem inner) :
        IAtomicWorkspaceFileSystem
    {
        public WorkspaceRoot Root => inner.Root;

        public Task<bool> TryCreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) =>
            inner.TryCreateDirectoryAsync(path, cancellationToken);
        public Task<bool> FileExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.FileExistsAsync(path, cancellationToken);
        public Task<bool> DirectoryExistsAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DirectoryExistsAsync(path, cancellationToken);
        public Task CreateDirectoryAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.CreateDirectoryAsync(path, cancellationToken);
        public Task<Stream> OpenReadAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.OpenReadAsync(path, cancellationToken);

        public async Task<Stream> OpenWriteAsync(
            WorkspaceRelativePath path,
            bool overwrite,
            CancellationToken cancellationToken)
        {
            var stream = await inner.OpenWriteAsync(path, overwrite, cancellationToken);
            return path.Value.StartsWith(".devforge-finalize-", StringComparison.Ordinal)
                ? new CorruptingWriteStream(stream)
                : stream;
        }

        public Task DeleteFileAsync(WorkspaceRelativePath path, CancellationToken cancellationToken) => inner.DeleteFileAsync(path, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateAllFilesAsync(CancellationToken cancellationToken) => inner.EnumerateAllFilesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateRootDirectoriesAsync(CancellationToken cancellationToken) => inner.EnumerateRootDirectoriesAsync(cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateFilesAsync(WorkspaceRelativePath directory, bool recursive, CancellationToken cancellationToken) => inner.EnumerateFilesAsync(directory, recursive, cancellationToken);
        public Task<ImmutableArray<WorkspaceRelativePath>> EnumerateDirectoriesAsync(WorkspaceRelativePath directory, CancellationToken cancellationToken) => inner.EnumerateDirectoriesAsync(directory, cancellationToken);
        public Task DeleteDirectoryAsync(WorkspaceRelativePath path, DirectoryCleanupIntent intent, CancellationToken cancellationToken) => inner.DeleteDirectoryAsync(path, intent, cancellationToken);
        public Task MoveDirectoryAsync(WorkspaceRelativePath source, WorkspaceRelativePath destination, WorkspaceMoveIntent intent, CancellationToken cancellationToken) => inner.MoveDirectoryAsync(source, destination, intent, cancellationToken);
    }

    private sealed class CorruptingWriteStream(Stream inner) : Stream
    {
        private bool _corrupted;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            var copy = buffer.AsSpan(offset, count).ToArray();
            Corrupt(copy);
            inner.Write(copy, 0, copy.Length);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var copy = buffer.ToArray();
            Corrupt(copy);
            await inner.WriteAsync(copy, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Corrupt(byte[] bytes)
        {
            if (!_corrupted && bytes.Length > 0)
            {
                bytes[0] ^= 0xff;
                _corrupted = true;
            }
        }
    }
}
