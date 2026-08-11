using DevForge.Domain.Execution;

namespace DevForge.UnitTests.Domain;

public sealed class ExecutionModelTests
{
    [Fact]
    public void ExecutionPlanSnapshotsAndValidatesExecutionValidators()
    {
        var validator = ExecutionValidator.Create(
            "build",
            "validate-command",
            [KeyValuePair.Create<string, PlanValue?>("required", PlanValue.FromBoolean(true))],
            TimeSpan.FromMinutes(1),
            required: true).Value;
        var validators = new List<ExecutionValidator?> { validator };

        var result = ExecutionPlan.Create("sha256:" + new string('a', 64), [], validators);
        validators.Clear();
        var invalid = ExecutionPlan.Create("plan", [], [validator, validator, null]);

        Assert.True(result.IsValid);
        Assert.Same(validator, Assert.Single(result.Value.Validators));
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Issues, issue => issue.Code == "plan.validator.id.duplicate");
        Assert.Contains(invalid.Issues, issue => issue.Code == "plan.validator.required");
    }

    [Fact]
    public void ExecutionValidatorAggregatesInvalidFieldsAndSnapshotsInputs()
    {
        var inputs = new List<KeyValuePair<string, PlanValue?>>
        {
            KeyValuePair.Create<string, PlanValue?>("required", PlanValue.FromBoolean(true)),
        };
        var valid = ExecutionValidator.Create(
            "build",
            "validate-command",
            inputs,
            TimeSpan.FromSeconds(5),
            required: false);
        inputs.Clear();
        var invalid = ExecutionValidator.Create(" ", " ", null, TimeSpan.Zero, true);

        Assert.True(valid.IsValid);
        Assert.Single(valid.Value.Inputs);
        Assert.False(invalid.IsValid);
        Assert.Equal(4, invalid.Issues.Length);
    }

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
