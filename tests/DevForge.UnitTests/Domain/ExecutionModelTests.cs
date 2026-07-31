using DevForge.Domain.Execution;

namespace DevForge.UnitTests.Domain;

public sealed class ExecutionModelTests
{
    [Fact]
    public void RetryPolicyRejectsInvalidValuesTogether()
    {
        var result = RetryPolicy.Create(0, TimeSpan.FromSeconds(-1), 0.5);

        Assert.False(result.IsValid);
        Assert.Equal(
            ["retry.max-attempts.invalid", "retry.delay.invalid", "retry.backoff.invalid"],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ExecutionPlanSnapshotsStepsAndStepInputs()
    {
        var stepInputs = new Dictionary<string, string> { ["configuration"] = "Release" };
        var steps = new List<ExecutionStep>
        {
            ExecutionStep.Create(
                "build",
                "Build",
                "validate-command",
                stepInputs,
                TimeSpan.FromMinutes(5),
                RetryPolicy.None),
        };

        var plan = new ExecutionPlan("plan-1", steps);
        stepInputs["configuration"] = "Debug";
        steps.Clear();

        Assert.Single(plan.Steps);
        Assert.Equal("Release", plan.Steps[0].Inputs["configuration"]);
    }
}
