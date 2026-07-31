namespace DevForge.Domain.Validation;

public sealed record ValidationIssue(
    string Code,
    string Message,
    string? Location = null);
