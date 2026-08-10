namespace DevForge.Infrastructure.Persistence;

public sealed class PersistenceDataException : Exception
{
    public const string ErrorCode = "DF-DB-001";

    public PersistenceDataException()
        : base("Stored metadata failed persistence integrity validation.")
    {
    }

    public string Code { get; } = ErrorCode;
}
