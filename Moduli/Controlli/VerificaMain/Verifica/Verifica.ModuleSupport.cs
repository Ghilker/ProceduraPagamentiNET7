using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal interface IVerificaModule
    {
        string Name { get; }
        void Calculate(VerificaPipelineContext context);
    }

    internal static class VerificaExecutionSupport
    {
        public static void ExecuteTimed(string scope, Action action, Func<string>? details = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var sw = Stopwatch.StartNew();
            Logger.LogInfo(null, $"[{scope}] START{FormatDetails(details)}");
            action();
            sw.Stop();
            Logger.LogInfo(null, $"[{scope}] END | elapsed={sw.ElapsedMilliseconds} ms{FormatDetails(details)}");
        }

        public static T ExecuteTimed<T>(string scope, Func<T> action, Func<string>? details = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var sw = Stopwatch.StartNew();
            Logger.LogInfo(null, $"[{scope}] START{FormatDetails(details)}");
            T result = action();
            sw.Stop();
            Logger.LogInfo(null, $"[{scope}] END | elapsed={sw.ElapsedMilliseconds} ms{FormatDetails(details)}");
            return result;
        }

        public static IReadOnlyList<KeyValuePair<StudentKey, StudenteInfo>> OrderStudents(IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            if (students == null)
                throw new ArgumentNullException(nameof(students));

            return students
                .OrderBy(pair => pair.Key.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Key.NumDomanda, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FormatDetails(Func<string>? details)
        {
            if (details == null)
                return string.Empty;

            string value = details() ?? string.Empty;
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $" | {value}";
        }
    }
}
