namespace DevForge.Blueprints.Abstractions.Validation;

public sealed record BlueprintValidationIssue
{
    public BlueprintValidationIssue(string code, string message, string? location = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A blueprint validation issue code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A blueprint validation issue message is required.", nameof(message));
        }

        if (location is not null && string.IsNullOrWhiteSpace(location))
        {
            throw new ArgumentException(
                "A blueprint validation issue location cannot be blank.",
                nameof(location));
        }

        Code = code.Trim();
        Message = message.Trim();
        Location = location?.Trim();
    }

    public string Code { get; }

    public string Message { get; }

    public string? Location { get; }

    public static BlueprintValidationIssue Create(
        string code,
        string message,
        string? location = null)
    {
        return new BlueprintValidationIssue(code, message, location);
    }
}
