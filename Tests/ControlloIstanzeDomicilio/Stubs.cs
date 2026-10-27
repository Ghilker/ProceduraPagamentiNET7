using System.Data.SqlClient;

namespace ProcedureNet7;

// Only desktop infrastructure is substituted. The data loader, validator, PDF reader
// and report linked by the test project are the production implementations.
internal sealed class MasterForm { }

internal abstract class BaseProcedure<TArgs> : IDisposable
{
    protected readonly SqlConnection? CONNECTION;
    protected BaseProcedure(MasterForm? masterForm, SqlConnection? connection) => CONNECTION = connection;
    public abstract void RunProcedure(TArgs args);
    public void Dispose() { }
}

internal static class Logger
{
    public static void LogInfo(int? progress, string message) => Console.WriteLine(message);
    public static void LogWarning(int? progress, string message) => Console.WriteLine(message);
}

internal sealed class ArgsControlloDomicilio
{
    public string _selectedAA = string.Empty;
    public string _folderPath = string.Empty;
    public IReadOnlyCollection<string>? _codiciFiscali;
}
