using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7;

internal abstract class BaseProcedure<T>
{
    protected readonly MasterForm _masterForm;
    protected readonly SqlConnection CONNECTION;

    protected BaseProcedure(MasterForm masterForm, SqlConnection connection)
    {
        _masterForm = masterForm;
        CONNECTION = connection;
    }

    public abstract void RunProcedure(T args);
}

internal sealed class MasterForm : Form
{
    public bool inProcedure;
}

internal static class Utilities
{
    public static string SafeGetString(SqlDataReader reader, string name)
        => Convert.ToString(reader[name]) ?? "";

    public static string ExportDataTableToExcel(DataTable table, string folder, bool includeHeaders, string fileName)
        => throw new NotSupportedException("L'esportazione Excel non è prevista nei test SQL dei blocchi.");
}

internal static class Logger
{
    public static readonly List<string> Messages = new();
    public static void LogInfo(int? progress, string message) => Messages.Add(message);
    public static void LogWarning(int? progress, string message) => Messages.Add(message);
    public static void LogError(int? progress, string message) => Messages.Add(message);
}
