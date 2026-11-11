namespace Game.Core.Diagnostics;

public interface IDiagnosticLogger
{
    void Log(DiagnosticLogLevel level, string message, Exception? exception = null);
}
