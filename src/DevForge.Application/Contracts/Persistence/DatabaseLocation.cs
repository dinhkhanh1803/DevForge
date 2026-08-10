using System.Text.RegularExpressions;
using DevForge.Domain.Validation;

namespace DevForge.Application.Contracts.Persistence;

public sealed class DatabaseLocation
{
    private static readonly Regex _backupSuffixPattern = new(
        "^[A-Za-z0-9-]{1,64}$",
        RegexOptions.CultureInvariant);

    private DatabaseLocation(string localDataRoot, string databaseFileName)
    {
        LocalDataRoot = localDataRoot;
        DatabaseFileName = databaseFileName;
        DatabasePath = Path.Combine(localDataRoot, databaseFileName);
    }

    public string LocalDataRoot { get; }

    public string DatabaseFileName { get; }

    public string DatabasePath { get; }

    public static ValidationResult<DatabaseLocation> Create(
        string? localDataRoot,
        string? databaseFileName)
    {
        var issues = new List<ValidationIssue>();
        var normalizedRoot = NormalizeRoot(localDataRoot);
        if (normalizedRoot is null)
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.path.root.invalid",
                    "The persistence root must be a canonical local drive path.",
                    "localDataRoot"));
        }

        var normalizedFileName = NormalizeFileName(databaseFileName);
        if (normalizedFileName is null)
        {
            issues.Add(
                new ValidationIssue(
                    "persistence.path.file-name.invalid",
                    "The database file name must be a safe .db file name.",
                    "databaseFileName"));
        }

        return issues.Count == 0
            ? ValidationResult.Success(new DatabaseLocation(normalizedRoot!, normalizedFileName!))
            : ValidationResult.Failure<DatabaseLocation>(issues);
    }

    public string CreateBackupPath(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix) || !_backupSuffixPattern.IsMatch(suffix))
        {
            throw new ArgumentException("A safe backup suffix is required.", nameof(suffix));
        }

        var baseName = Path.GetFileNameWithoutExtension(DatabaseFileName);
        return Path.Combine(LocalDataRoot, $"{baseName}.backup-{suffix}.db");
    }

    private static string? NormalizeRoot(string? root)
    {
        return LocalPersistencePathPolicy.TryNormalize(root);
    }

    private static string? NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Length > 128
            || fileName.Any(char.IsControl)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(fileName), ".db", StringComparison.OrdinalIgnoreCase)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(stem) || LocalPersistencePathPolicy.IsReservedSegment(stem)
            ? null
            : fileName.Trim();
    }
}
