using DevForge.Application.Contracts;

namespace DevForge.E2ETests.Desktop;

internal sealed class UnusedLocalReadyService : ILocalReadyService
{
    public LocalReadyPresentation Describe(RunCheckpoint checkpoint) =>
        LocalReadyPresentation.Create(
            "C:\\Projects\\sample",
            ["C:\\DevForgeData\\reports\\run.json", "C:\\DevForgeData\\reports\\run.md"]).Value;

    public Task OpenIdeAsync(
        RunCheckpoint checkpoint,
        string ideId,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The test does not open an IDE.");
}
