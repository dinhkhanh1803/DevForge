using DevForge.Application.Contracts;

namespace DevForge.E2ETests.Desktop;

internal sealed class UnusedProjectPublicationWorkflow : IProjectPublicationWorkflow
{
    public Task<ExecutionOperationResult<ProjectPublicationOutcome>> CompleteAsync(
        string runId,
        PublicationMutationMode mutationMode,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Publication is not expected in this test.");
}
