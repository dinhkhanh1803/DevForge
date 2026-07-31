using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts;

public interface IProjectPlanner
{
    Task<ValidationResult<ExecutionPlan>> CreatePlanAsync(
        ProjectRecipe recipe,
        CancellationToken cancellationToken);
}
