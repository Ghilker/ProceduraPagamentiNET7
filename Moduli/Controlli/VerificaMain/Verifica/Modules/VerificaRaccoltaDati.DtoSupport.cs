using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private static Dictionary<StudentKey, TDto> ReadDtoMap<TDto>(
            SqlCommand command,
            Func<SqlDataReader, StudentKey> keyReader,
            Func<TDto> factory,
            Action<SqlDataReader, TDto> readRow,
            out int readCount,
            Func<StudentKey, bool>? keyFilter = null,
            int capacity = 0)
            where TDto : class
        {
            var map = capacity > 0
                ? new Dictionary<StudentKey, TDto>(capacity)
                : new Dictionary<StudentKey, TDto>();

            readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;
                var key = keyReader(reader);
                if (keyFilter != null && !keyFilter(key))
                    continue;

                if (!map.TryGetValue(key, out var dto))
                {
                    dto = factory();
                    map[key] = dto;
                }

                readRow(reader, dto);
            }

            return map;
        }

        private static List<(StudentKey Key, TDto Dto)> ReadDtoList<TDto>(
            SqlCommand command,
            Func<SqlDataReader, StudentKey> keyReader,
            Func<SqlDataReader, TDto> projector,
            out int readCount,
            Func<StudentKey, bool>? keyFilter = null,
            int capacity = 0)
        {
            var list = capacity > 0
                ? new List<(StudentKey Key, TDto Dto)>(capacity)
                : new List<(StudentKey Key, TDto Dto)>();

            readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;
                var key = keyReader(reader);
                if (keyFilter != null && !keyFilter(key))
                    continue;

                list.Add((key, projector(reader)));
            }

            return list;
        }

        private static void MergeDtoMap<TDto>(
            IReadOnlyDictionary<StudentKey, TDto> dtoMap,
            IReadOnlyDictionary<StudentKey, StudenteInfo> students,
            Action<StudenteInfo, TDto> apply)
        {
            foreach (var pair in dtoMap)
            {
                if (students.TryGetValue(pair.Key, out var info) && info != null)
                    apply(info, pair.Value);
            }
        }

        private static void MergeDtoList<TDto>(
            IEnumerable<(StudentKey Key, TDto Dto)> rows,
            IReadOnlyDictionary<StudentKey, StudenteInfo> students,
            Action<StudenteInfo, TDto> apply)
        {
            foreach (var row in rows)
            {
                if (students.TryGetValue(row.Key, out var info) && info != null)
                    apply(info, row.Dto);
            }
        }

        private static StudentKey ReadStudentKey(SqlDataReader reader, string cfColumn = "Cod_fiscale", string domandaColumn = "Num_domanda")
            => CreateStudentKey(reader.SafeGetString(cfColumn), reader.SafeGetString(domandaColumn));

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
    }
}
