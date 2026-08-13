using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.CreateProject;

public sealed partial class DynamicInputViewModel : ObservableObject
{
    private readonly BlueprintInputPropertyDefinition _definition;

    [ObservableProperty]
    private string? _textValue;

    [ObservableProperty]
    private bool _booleanValue;

    [ObservableProperty]
    private long? _wholeNumberValue;

    [ObservableProperty]
    private string? _validationMessage;

    public DynamicInputViewModel(BlueprintInputPropertyDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        TextValue = definition.DefaultValue?.Kind == BlueprintValueKind.Text
            ? definition.DefaultValue.StringValue
            : null;
        BooleanValue = definition.DefaultValue?.Kind == BlueprintValueKind.Boolean
            && definition.DefaultValue.BooleanValue;
        WholeNumberValue = definition.DefaultValue?.Kind == BlueprintValueKind.WholeNumber
            ? definition.DefaultValue.IntegerValue
            : null;
    }

    public event EventHandler? ValueChanged;

    public string Id => _definition.Id;

    public BlueprintInputKind Kind => _definition.Kind;

    public bool IsRequired => _definition.Required;

    public ImmutableArray<string> AllowedValues => _definition.AllowedValues;

    public int? MinimumLength => _definition.MinimumLength;

    public int? MaximumLength => _definition.MaximumLength;

    public long? Minimum => _definition.Minimum;

    public long? Maximum => _definition.Maximum;

    public bool HasValue => Kind switch
    {
        BlueprintInputKind.Text or BlueprintInputKind.Choice => TextValue is not null,
        BlueprintInputKind.Boolean => true,
        BlueprintInputKind.WholeNumber => WholeNumberValue is not null,
        _ => false,
    };

    public ValidationResult<DynamicInputValue> BuildValue()
    {
        var issues = new List<ValidationIssue>();
        ValidationResult<DynamicInputValue>? value = Kind switch
        {
            BlueprintInputKind.Text => BuildTextValue(issues, choice: false),
            BlueprintInputKind.Choice => BuildTextValue(issues, choice: true),
            BlueprintInputKind.Boolean => DynamicInputValue.Boolean(BooleanValue),
            BlueprintInputKind.WholeNumber => BuildWholeNumberValue(issues),
            _ => null,
        };

        if (value is null)
        {
            issues.Add(Issue("creation.input.kind.invalid", "The input kind is unsupported."));
        }
        else if (!value.IsValid)
        {
            issues.AddRange(value.Issues.Select(issue => new ValidationIssue(
                issue.Code,
                issue.Message,
                $"inputs.{Id}")));
        }

        ValidationMessage = issues.FirstOrDefault()?.Message;
        return issues.Count == 0
            ? value!
            : ValidationResult.Failure<DynamicInputValue>(issues);
    }

    partial void OnTextValueChanged(string? value)
    {
        MarkChanged();
    }

    partial void OnBooleanValueChanged(bool value)
    {
        MarkChanged();
    }

    partial void OnWholeNumberValueChanged(long? value)
    {
        MarkChanged();
    }

    private ValidationResult<DynamicInputValue>? BuildTextValue(
        List<ValidationIssue> issues,
        bool choice)
    {
        if (TextValue is null || IsRequired && string.IsNullOrWhiteSpace(TextValue))
        {
            issues.Add(Issue("creation.input.value.required", "A required input value is missing."));
            return null;
        }

        if (MinimumLength is not null && TextValue.Length < MinimumLength
            || MaximumLength is not null && TextValue.Length > MaximumLength)
        {
            issues.Add(Issue("creation.input.length.invalid", "The input length is outside the allowed range."));
        }

        if (choice && !AllowedValues.Contains(TextValue, StringComparer.Ordinal))
        {
            issues.Add(Issue("creation.input.choice.invalid", "The selected value is not an allowed choice."));
        }

        return DynamicInputValue.Text(TextValue);
    }

    private ValidationResult<DynamicInputValue>? BuildWholeNumberValue(
        List<ValidationIssue> issues)
    {
        if (WholeNumberValue is null)
        {
            issues.Add(Issue("creation.input.value.required", "A required whole number is missing."));
            return null;
        }

        if (Minimum is not null && WholeNumberValue < Minimum
            || Maximum is not null && WholeNumberValue > Maximum)
        {
            issues.Add(Issue("creation.input.number.invalid", "The whole number is outside the allowed range."));
        }

        return DynamicInputValue.WholeNumber(WholeNumberValue.Value);
    }

    private ValidationIssue Issue(string code, string message)
    {
        return new ValidationIssue(code, message, $"inputs.{Id}");
    }

    private void MarkChanged()
    {
        ValidationMessage = null;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}
