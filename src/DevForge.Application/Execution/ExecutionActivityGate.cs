namespace DevForge.Application.Execution;

internal static class ExecutionActivityGate
{
    private static int _isActive;

    public static bool TryEnter() => Interlocked.CompareExchange(ref _isActive, 1, 0) == 0;

    public static void Exit() => Volatile.Write(ref _isActive, 0);
}
