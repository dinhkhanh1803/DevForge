namespace DevForge.Blueprints.Abstractions.Validation;

public static class BlueprintIdentifierValidator
{
    internal const int MaximumLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length > MaximumLength
            || !IsLowercaseLetter(candidate[0])
            || !IsLowercaseLetterOrDigit(candidate[^1]))
        {
            return false;
        }

        var previousWasSeparator = false;
        foreach (var character in candidate)
        {
            if (IsLowercaseLetterOrDigit(character))
            {
                previousWasSeparator = false;
                continue;
            }

            if ((character != '.' && character != '-') || previousWasSeparator)
            {
                return false;
            }

            previousWasSeparator = true;
        }

        return true;
    }

    private static bool IsLowercaseLetter(char value)
    {
        return value is >= 'a' and <= 'z';
    }

    private static bool IsLowercaseLetterOrDigit(char value)
    {
        return IsLowercaseLetter(value) || value is >= '0' and <= '9';
    }
}
