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
        var stepInputs = new Dictionary<string, PlanValue?>
        {
            ["configuration"] = PlanValue.FromString("Release").Value,
        };
        var steps = new List<ExecutionStep>
        {
            ExecutionStep.Create(
                "build",
                "Build",
                "validate-command",
                stepInputs,
                TimeSpan.FromMinutes(5),
                RetryPolicy.None).Value,
        };

        var plan = ExecutionPlan.Create("plan-1", steps).Value;
        stepInputs["configuration"] = PlanValue.FromString("Debug").Value;
        steps.Clear();

        Assert.Single(plan.Steps);
        Assert.Equal("Release", plan.Steps[0].Inputs["configuration"].StringValue);
    }

    [Fact]
    public void ExecutionStepCreateAggregatesExpectedInputIssues()
    {
        var result = ExecutionStep.Create(null, null, null, null, TimeSpan.Zero, null);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "step.id.required",
                "step.name.required",
                "step.handler.required",
                "step.inputs.required",
                "step.timeout.invalid",
                "step.retry-policy.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void ExecutionPlanCreateAggregatesExpectedInputIssues()
    {
        var result = ExecutionPlan.Create(" ", null);

        Assert.False(result.IsValid);
        Assert.Equal(
            ["plan.id.required", "plan.steps.required"],
            result.Issues.Select(issue => issue.Code));
    }
}
