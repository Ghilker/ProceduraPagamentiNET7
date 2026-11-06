using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DomicilioAutomation
{
    /// <summary>Checks a proposed payload patch without changing any registered data.</summary>
    public static class DomicilioAutomationCorrectionPatch
    {
        public static string Apply(string currentPayload, DomicilioAutomationOperationCorrection correction,
            int entity, int operation)
        {
            if (correction == null || correction.OperationId <= 0 || operation is not (1 or 2) || entity is not (2 or 3) ||
                correction.Fields == null || correction.Fields.Count == 0 ||
                !string.Equals(currentPayload, correction.BeforePayload, StringComparison.Ordinal))
                throw Invalid();
            using var beforeDocument = Parse(currentPayload);
            using var afterDocument = Parse(correction.AfterPayload);
            var before = Properties(beforeDocument.RootElement);
            var after = Properties(afterDocument.RootElement);
            if (before.Count != after.Count || before.Keys.Any(key => !after.ContainsKey(key))) throw Invalid();
            var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in correction.Fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Field) || !changed.Add(field.Field) ||
                    field.Page <= 0 || field.AttachmentId <= 0 || string.IsNullOrWhiteSpace(field.Label) ||
                    !Allowed(field.Field, entity, out var isDate)) throw Invalid();
                var actualKey = before.Keys.SingleOrDefault(key => string.Equals(key, field.Field, StringComparison.OrdinalIgnoreCase));
                if (actualKey == null || !after.TryGetValue(actualKey, out var afterValue)) throw Invalid();
                var beforeValue = before[actualKey];
                if (beforeValue.ValueKind != JsonValueKind.String || afterValue.ValueKind != JsonValueKind.String ||
                    !string.Equals(beforeValue.GetString(), field.Before, StringComparison.Ordinal) ||
                    !string.Equals(afterValue.GetString(), field.After, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(field.Before) || string.IsNullOrWhiteSpace(field.After) ||
                    string.Equals(field.Before, field.After, StringComparison.Ordinal)) throw Invalid();
                if (isDate)
                {
                    var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" };
                    if (!DateTime.TryParseExact(field.Before, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var oldDate) ||
                        !DateTime.TryParseExact(field.After, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var newDate) ||
                        oldDate.TimeOfDay != TimeSpan.Zero || oldDate.Year is < 1900 or > 2900 || newDate.Year is < 1900 or > 2900 ||
                        oldDate.Date == newDate.Date) throw Invalid();
                }
                else if (field.After.Length > (entity == 3 ? 50 : 100)) throw Invalid();
            }
            foreach (var pair in before)
                if (!changed.Contains(pair.Key) && !Equal(pair.Value, after[pair.Key])) throw Invalid();
            return correction.AfterPayload;
        }

        private static bool Allowed(string field, int entity, out bool date)
        {
            var normalized = field.ToUpperInvariant();
            date = normalized is "DATAREGISTRAZIONECONTRATTO" or "DATAINIZIOCONTRATTO" or "DATAFINECONTRATTO" or
                "DATAINIZIOSUBENTRO" or "DATADECORRENZA" or "DATASCADENZA";
            return entity == 2
                ? normalized is "NUMEROSERIECONTRATTO" or "DATAREGISTRAZIONECONTRATTO" or "DATAINIZIOCONTRATTO" or
                    "DATAFINECONTRATTO" or "NUMEROSERIESUBENTRO" or "DATAINIZIOSUBENTRO"
                : normalized is "NUMEROSERIEPROROGA" or "DATADECORRENZA" or "DATASCADENZA";
        }

        private static JsonDocument Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Invalid();
            try { return JsonDocument.Parse(json); }
            catch (JsonException) { throw Invalid(); }
        }

        private static Dictionary<string, JsonElement> Properties(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object) throw Invalid();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw Invalid();
                ValidateNested(property.Value);
                result.Add(property.Name, property.Value);
            }
            return result;
        }

        private static void ValidateNested(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object) Properties(value);
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var item in value.EnumerateArray()) ValidateNested(item);
        }

        private static bool Equal(JsonElement before, JsonElement after)
        {
            if (before.ValueKind != after.ValueKind) return false;
            if (before.ValueKind == JsonValueKind.Object)
            {
                var left = Properties(before); var right = Properties(after);
                return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && Equal(pair.Value, value));
            }
            if (before.ValueKind == JsonValueKind.Array)
                return before.GetArrayLength() == after.GetArrayLength() && before.EnumerateArray().Zip(after.EnumerateArray(), Equal).All(x => x);
            return before.ValueKind == JsonValueKind.String
                ? string.Equals(before.GetString(), after.GetString(), StringComparison.Ordinal)
                : string.Equals(before.GetRawText(), after.GetRawText(), StringComparison.Ordinal);
        }
        private static ArgumentException Invalid() => new("Correzione automatica non valida o non limitata ai campi e alle fonti autorizzati.");
    }
}
