namespace DevForge.Infrastructure;

public sealed class InfrastructureOperationException : Exception
{
    internal InfrastructureOperationException(string code, string safeMessage)
        : base(safeMessage)
    {
        Code = code;
    }

    public string Code { get; }
}
