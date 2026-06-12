using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private VerificaPipelineContext CurrentContext
            => _currentContext ?? throw new InvalidOperationException("Contesto verifica non inizializzato.");

        private IReadOnlyDictionary<StudentKey, StudenteInfo> CurrentStudents
            => CurrentContext.Students;

        private static IDisposable MeasureCollectionStep(string scope, string details)
            => new LogMeasureScope(scope, details);

        private static void AddAaParameter(SqlCommand command, string aa)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            command.Parameters.Add("@AA", SqlDbType.Char, 8).Value = aa;
        }

        private static DateTime GetDataValiditaMaxEconomici(string aa)
        {
            string value = (aa ?? string.Empty).Trim();
            if (value.Length < 4 || !int.TryParse(value.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int startYear))
                throw new ArgumentException($"Anno accademico non valido per cutoff economici: {aa}", nameof(aa));

            return new DateTime(startYear, 12, 31, 23, 59, 59, 997, DateTimeKind.Unspecified);
        }

        private static void AddDataValiditaMaxParameter(SqlCommand command, string aa)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            command.Parameters.Add("@DataValiditaMax", SqlDbType.DateTime).Value = GetDataValiditaMaxEconomici(aa);
        }

        private static string NormalizeCf(string? value)
            => Utilities.RemoveAllSpaces((value ?? string.Empty).Trim().ToUpperInvariant());

        private static string NormalizeDomanda(string? value)
            => Utilities.RemoveAllSpaces((value ?? string.Empty).Trim());

        private static StudentKey CreateStudentKey(string? codFiscale, string? numDomanda)
            => new StudentKey(NormalizeCf(codFiscale), NormalizeDomanda(numDomanda));

        private bool TryGetStudentInfo(StudentKey key, out StudenteInfo info)
        {
            if (CurrentStudents.TryGetValue(key, out info!) && info != null)
            {
                info.InformazioniEconomiche ??= new InformazioniEconomiche();
                return true;
            }

            return false;
        }

        private bool TryGetStudentInfo(
            SqlDataReader reader,
            out StudenteInfo info,
            string cfColumn = "Cod_fiscale",
            string domandaColumn = "Num_domanda")
        {
            var key = CreateStudentKey(
                reader.SafeGetString(cfColumn),
                reader.SafeGetString(domandaColumn));

            return TryGetStudentInfo(key, out info!);
        }

        private InformazioniEconomiche GetEconomicInfo(StudentKey key)
        {
            if (TryGetStudentInfo(key, out var info))
                return info.InformazioniEconomiche;

            throw new InvalidOperationException(
                $"Studente non trovato per chiave economica {key.CodFiscale}/{key.NumDomanda}.");
        }

        private void ReadAndMergeSingleDto<TDto>(
            SqlCommand command,
            Func<SqlDataReader, StudentKey> keyFactory,
            Func<SqlDataReader, TDto> dtoFactory,
            Action<StudenteInfo, TDto> merge)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (keyFactory == null)
                throw new ArgumentNullException(nameof(keyFactory));
            if (dtoFactory == null)
                throw new ArgumentNullException(nameof(dtoFactory));
            if (merge == null)
                throw new ArgumentNullException(nameof(merge));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = keyFactory(reader);
                if (!TryGetStudentInfo(key, out var info))
                    continue;

                merge(info, dtoFactory(reader));
            }
        }

        private void ReadAndMergeBufferedDtos<TDto>(
            SqlCommand command,
            Func<SqlDataReader, StudentKey> keyFactory,
            Func<SqlDataReader, TDto> dtoFactory,
            Action<Dictionary<StudentKey, TDto>, StudentKey, TDto> accumulate,
            Action<StudenteInfo, TDto> merge)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (keyFactory == null)
                throw new ArgumentNullException(nameof(keyFactory));
            if (dtoFactory == null)
                throw new ArgumentNullException(nameof(dtoFactory));
            if (accumulate == null)
                throw new ArgumentNullException(nameof(accumulate));
            if (merge == null)
                throw new ArgumentNullException(nameof(merge));

            var dtoMap = new Dictionary<StudentKey, TDto>(CurrentStudents.Count);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = keyFactory(reader);
                if (!CurrentStudents.ContainsKey(key))
                    continue;

                accumulate(dtoMap, key, dtoFactory(reader));
            }

            foreach (var pair in dtoMap)
            {
                if (!TryGetStudentInfo(pair.Key, out var info))
                    continue;

                merge(info, pair.Value);
            }
        }

        private static string ReadDomandaAsString(SqlDataReader reader, int ordinal)
        {
            if (reader.IsDBNull(ordinal))
                return string.Empty;

            object value = reader.GetValue(ordinal);
            return value switch
            {
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                decimal d => d.ToString(CultureInfo.InvariantCulture),
                string s => NormalizeDomanda(s),
                _ => NormalizeDomanda(Convert.ToString(value, CultureInfo.InvariantCulture))
            };
        }

        private static decimal GetDecimalOrZero(SqlDataReader reader, int ordinal)
            => reader.IsDBNull(ordinal) ? 0m : reader.GetDecimal(ordinal);

        private sealed class LogMeasureScope : IDisposable
        {
            private readonly string _scope;
            private readonly string _details;
            private readonly Stopwatch _sw;

            public LogMeasureScope(string scope, string details)
            {
                _scope = scope;
                _details = details ?? string.Empty;
                _sw = Stopwatch.StartNew();

                Logger.LogInfo(null, $"[{_scope}] START{FormatDetails(_details)}");
            }

            public void Dispose()
            {
                _sw.Stop();
                Logger.LogInfo(
                    null,
                    $"[{_scope}] END | elapsed={_sw.ElapsedMilliseconds} ms{FormatDetails(_details)}");
            }

            private static string FormatDetails(string details)
                => string.IsNullOrWhiteSpace(details)
                    ? string.Empty
                    : $" | {details}";
        }
    }
}