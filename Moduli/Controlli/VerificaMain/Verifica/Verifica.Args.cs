using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica
    {
        private static string GetStringArg(object args, string n1, string n2, string n3, string n4, string fallback)
        {
            foreach (var name in new[] { n1, n2, n3, n4 })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value is string s && !string.IsNullOrWhiteSpace(s))
                    return s;

                if (value != null)
                    return value.ToString() ?? fallback;
            }
            return fallback;
        }

        private static bool GetBoolArg(object args, string n1, string n2, bool fallback)
        {
            foreach (var name in new[] { n1, n2 })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value is bool b) return b;
                if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
                if (value is int i) return i != 0;
            }
            return fallback;
        }

        private static IReadOnlyCollection<string>? GetStringListArg(object args, params string[] names)
        {
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prop = args.GetType().GetProperty(name.Trim(), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop == null) continue;

                var value = prop.GetValue(args);
                if (value == null) continue;

                if (value is IReadOnlyCollection<string> roc) return roc;
                if (value is IEnumerable<string> e) return e.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                // singola stringa "CF1;CF2;..."
                if (value is string s)
                {
                    var parts = s.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(x => x.Trim())
                                 .Where(x => x.Length > 0)
                                 .ToList();
                    return parts.Count > 0 ? parts : null;
                }
            }
            return null;
        }
    }
}
