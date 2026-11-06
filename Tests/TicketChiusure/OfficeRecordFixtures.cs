using ProcedureNet7;
using System.Text.RegularExpressions;

internal static class OfficeRecordFixtures
{
    // Solo adattamento dei vecchi esempi descrittivi ai campi codificati della query.
    public static void SetKnownBlocks(TicketOfficeRecord record)
    {
        record.HasBlockCodeData = true;
        if (record.ActiveBlockCodes.Length > 0) return;
        var codes = Regex.Matches(record.Blocks, @"\b(?:BPP|BDR|BSI|BVI|BTR|BIS|BMI)\b").Select(m => m.Value).ToList();
        if (codes.Count == 0 && record.Blocks.Length > 0)
        {
            if (record.Blocks.Contains("iscrizione", StringComparison.OrdinalIgnoreCase)) codes.Add("BVI");
            else if (record.Blocks.Contains("specialistica", StringComparison.OrdinalIgnoreCase)) codes.Add("BIS");
            else if (record.Blocks.Contains("merito", StringComparison.OrdinalIgnoreCase)) codes.Add("BMI");
            else codes.Add("UNKNOWN");
        }
        record.ActiveBlockCodes = string.Join(" | ", codes);
    }
}
