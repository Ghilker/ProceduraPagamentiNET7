using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal abstract class GeneratoreAllegatoBase : IGeneratoreAllegato
    {
        protected readonly SqlConnection Connection;

        protected GeneratoreAllegatoBase(SqlConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public abstract string Codice { get; }

        public abstract string Descrizione { get; }

        public abstract void Generate(AllegatoContext context);

        protected DataTable ExecuteQuery(string sql, params SqlParameter[] parameters)
        {
            var dt = new DataTable();

            using var cmd = new SqlCommand(sql, Connection)
            {
                CommandTimeout = 999999
            };

            cmd.Parameters.AddRange(parameters);

            using var reader = cmd.ExecuteReader();
            dt.Load(reader);

            return dt;
        }

        protected static string S(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value || row[column] == null)
                return string.Empty;

            return row[column].ToString() ?? string.Empty;
        }

        protected static decimal D(DataRow row, string column)
        {
            if (!row.Table.Columns.Contains(column) || row[column] == DBNull.Value || row[column] == null)
                return 0m;

            return Convert.ToDecimal(row[column], CultureInfo.GetCultureInfo("it-IT"));
        }

        protected static string NormalizeLongPath(string path)
        {
            if (path.StartsWith(@"\\?\"))
                return path;

            return @"\\?\" + Path.GetFullPath(path);
        }

        protected static string Sanitize(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value.Trim();
        }
    }
}
