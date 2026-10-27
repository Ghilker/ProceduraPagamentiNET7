using System.Data;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed record IbanApplication(string FiscalCode, string Year, string Iban);
    internal sealed record IbanExistingBlock(string FiscalCode, string Year);
    internal sealed class IbanFixture
    {
        public List<IbanApplication> Applications { get; } = new();
        public List<IbanExistingBlock> ExistingBlocks { get; } = new();
        public List<(string FiscalCode, string Year)> Added { get; } = new();
        public List<(string FiscalCode, string Year)> Removed { get; } = new();
        public List<string> CheckedYears { get; } = new();
        public List<string> Messages { get; } = new();
        public List<IbanApplication> YearRows { get; } = new();
        public List<IbanExistingBlock> BlockRows { get; } = new();
        public bool Committed { get; set; }
        public bool RolledBack { get; set; }
        public bool IgnoreYearFilter { get; init; }

        public DataTable Read(string sql, System.Data.SqlClient.SqlParameterCollection parameters)
        {
            string YearParameter(string token) => parameters.Value(token)?.ToString() ??
                Regex.Match(sql, @"anno_accademico\s*(?:>=|=)\s*'(?<year>\d{8})'", RegexOptions.IgnoreCase).Groups["year"].Value;
            if (sql.Contains("FROM vMotivazioni_blocco_pagamenti"))
            {
                var year = YearParameter("@aa");
                var matchYear = Regex.IsMatch(sql, @"bmp\.Anno_accademico\s*=\s*d\.Anno_accademico", RegexOptions.IgnoreCase);
                var table = Table(("Anno_accademico", typeof(string)), ("Num_domanda", typeof(string)),
                    ("BlockDate", typeof(DateTime)), ("Cod_fiscale", typeof(string)));
                foreach (var block in ExistingBlocks.Where(b => Applications.Any(a => a.FiscalCode == b.FiscalCode && a.Year == year && (!matchYear || a.Year == b.Year))))
                {
                    BlockRows.Add(block);
                    table.Rows.Add(block.Year, "1", new DateTime(2026, 1, 1), block.FiscalCode);
                }
                return table;
            }
            if (sql.Contains("FROM Domanda") && sql.Contains("IN ('"))
            {
                var cfList = Regex.Match(sql, @"IN\s*\('(?<values>[^)]+)'\)").Groups["values"].Value.Split("','").ToHashSet();
                var year = YearParameter("@aaMinimo");
                var filtered = Regex.IsMatch(sql, @"Anno_accademico\s*>=", RegexOptions.IgnoreCase);
                var table = Table(("Cod_fiscale", typeof(string)), ("Anno_accademico", typeof(string)));
                foreach (var app in Applications.Where(a => cfList.Contains(a.FiscalCode) && (IgnoreYearFilter || !filtered || string.CompareOrdinal(a.Year, year) >= 0)))
                {
                    YearRows.Add(app);
                    table.Rows.Add(app.FiscalCode, app.Year);
                }
                return table;
            }
            if (sql.Contains("vMODALITA_PAGAMENTO") && sql.Contains("vEsiti_concorsi"))
            {
                var year = YearParameter("@aaMinimo");
                var table = Table(("Cod_fiscale", typeof(string)), ("IBAN", typeof(string)));
                foreach (var app in Applications.Where(a => string.CompareOrdinal(a.Year, year) >= 0).DistinctBy(a => a.FiscalCode))
                    table.Rows.Add(app.FiscalCode, app.Iban);
                return table;
            }
            throw new InvalidOperationException("Query inattesa nella fixture.");
        }

        private static DataTable Table(params (string Name, Type Type)[] columns)
        {
            var table = new DataTable();
            foreach (var (name, type) in columns) table.Columns.Add(name, type);
            return table;
        }
    }
    internal sealed class ArgsProceduraControlloIBAN { public string _annoAccademico { get; init; } = ""; }
    internal abstract class BaseProcedure<T>
    {
        protected readonly MasterForm _masterForm;
        protected readonly System.Data.SqlClient.SqlConnection CONNECTION;
        protected BaseProcedure(MasterForm masterForm, System.Data.SqlClient.SqlConnection connection)
        { _masterForm = masterForm; CONNECTION = connection; }
        public abstract void RunProcedure(T args);
    }
    internal sealed class MasterForm { public object? Invoke(Delegate action) { action.DynamicInvoke(); return null; } }
    internal static class Logger
    {
        public static List<string> Errors { get; } = new();
        public static void LogInfo(int? progress, string message) { }
        public static void LogError(int? progress, string message) => Errors.Add(message);
    }
    internal static class Utilities
    {
        public static string SafeGetString(System.Data.SqlClient.SqlDataReader reader, string name) => Convert.ToString(reader.Value(name)) ?? "";
    }
    internal static class BlocksUtil
    {
        public static Dictionary<string, bool> HasBlock(System.Data.SqlClient.SqlConnection c, System.Data.SqlClient.SqlTransaction? tx,
            List<string> cfs, string code, string year)
        { c.Fixture.CheckedYears.Add(year); return cfs.ToDictionary(cf => cf, cf => false); }
        public static void AddBlock(System.Data.SqlClient.SqlConnection c, System.Data.SqlClient.SqlTransaction? tx,
            List<string> cfs, string code, string year, string source, bool removed)
        { foreach (var cf in cfs) c.Fixture.Added.Add((cf, year)); }
        public static void RemoveBlock(System.Data.SqlClient.SqlConnection c, System.Data.SqlClient.SqlTransaction? tx,
            List<string> cfs, string code, string? year, string source)
        { foreach (var cf in cfs) c.Fixture.Removed.Add((cf, year ?? "")); }
    }
    internal static class MessageUtils
    {
        public static void InsertMessages(System.Data.SqlClient.SqlConnection c, System.Data.SqlClient.SqlTransaction? tx,
            Dictionary<string, string> messages, string source) => c.Fixture.Messages.AddRange(messages.Keys);
        public static void InsertMessages(System.Data.SqlClient.SqlConnection c, System.Data.SqlClient.SqlTransaction? tx,
            List<string> cfs, string message, string source) => c.Fixture.Messages.AddRange(cfs);
    }
}

namespace System.Data.SqlClient
{
    internal sealed class SqlConnection
    {
        public SqlConnection(ProcedureNet7.IbanFixture fixture) { Fixture = fixture; }
        public ProcedureNet7.IbanFixture Fixture { get; }
        public SqlTransaction BeginTransaction() => new(Fixture);
    }
    internal sealed class SqlTransaction
    {
        private readonly ProcedureNet7.IbanFixture fixture;
        public SqlTransaction(ProcedureNet7.IbanFixture fixture) { this.fixture = fixture; }
        public void Commit() => fixture.Committed = true;
        public void Rollback() => fixture.RolledBack = true;
    }
    internal sealed class SqlCommand : IDisposable
    {
        private readonly string text;
        private readonly SqlConnection connection;
        public SqlCommand(string text, SqlConnection connection, SqlTransaction? transaction)
        { this.text = text; this.connection = connection; }
        public SqlParameterCollection Parameters { get; } = new();
        public SqlDataReader ExecuteReader() => new(connection.Fixture.Read(text, Parameters).CreateDataReader());
        public object? ExecuteScalar() => text.Contains("data_validita <") ? "ERRATO_PRECEDENTE"
            : connection.Fixture.Applications.First(a => a.FiscalCode == Parameters.Value("@cf")?.ToString()).Iban;
        public void Dispose() { }
    }
    internal sealed class SqlParameter { public object? Value { get; set; } }
    internal sealed class SqlParameterCollection
    {
        private readonly Dictionary<string, SqlParameter> values = new();
        public SqlParameter Add(string name, SqlDbType type, int size) => values[name] = new();
        public SqlParameter AddWithValue(string name, object value) => values[name] = new() { Value = value };
        public object? Value(string name) => values.GetValueOrDefault(name)?.Value;
    }
    internal sealed class SqlDataReader : IDisposable
    {
        private readonly DataTableReader reader;
        public SqlDataReader(DataTableReader reader) { this.reader = reader; }
        public bool Read() => reader.Read();
        public int GetOrdinal(string name) => reader.GetOrdinal(name);
        public DateTime GetDateTime(int index) => reader.GetDateTime(index);
        public object Value(string name) => reader[name];
        public void Dispose() => reader.Dispose();
    }
}

namespace System.Windows.Forms
{
    internal delegate void MethodInvoker();
    internal enum DialogResult { OK, Cancel }
    internal enum MessageBoxButtons { OKCancel }
    internal enum MessageBoxIcon { Question }
    internal static class MessageBox
    {
        public static DialogResult Show(object owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => DialogResult.OK;
    }
}
