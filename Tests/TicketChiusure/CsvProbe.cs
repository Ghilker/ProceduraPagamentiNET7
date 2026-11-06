using System.Data;
using System.Text;
using System.Text.Json;
using System.Data.SqlClient;
using Microsoft.VisualBasic.FileIO;
using ProcedureNet7;

internal static class CsvProbe
{
    public static void Run(string path, string? databaseOutput = null, bool casesOnly = false)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var parser = new TextFieldParser(path, Encoding.GetEncoding(1252));
        parser.SetDelimiters(";");
        parser.HasFieldsEnclosedInQuotes = true;
        var table = new DataTable();
        foreach (string field in parser.ReadFields()!) table.Columns.Add(field);
        int total = 0;
        while (!parser.EndOfData)
        {
            string[] fields = parser.ReadFields()!;
            Array.Resize(ref fields, table.Columns.Count);
            var row = table.NewRow();
            row.ItemArray = fields;
            total++;
            if (TicketClosureReview.IsEligible(row) && (!casesOnly || new[] { "540539", "531209", "438035", "426734", "487503", "486039", "507018", "536918", "488249", "515025", "516510", "544105", "546779", "541577", "544362", "544134", "544123", "531216" }.Contains(Convert.ToString(row["ID_TICKET"])))) table.Rows.Add(row);
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var records = new Dictionary<string, List<TicketOfficeRecord>>();
        if (databaseOutput != null)
        {
            string connectionString = Environment.GetEnvironmentVariable("TICKET_REVIEW_CONNECTION") ?? throw new InvalidOperationException("TICKET_REVIEW_CONNECTION mancante.");
            using var connection = new SqlConnection(connectionString);
            records = TicketOfficeDataService.Load(table.AsEnumerable().Select(r => Convert.ToString(r["CODFISC"])!).Distinct().ToList(), connection);
            Console.WriteLine($"DB: {records.Count} studenti, {records.Values.Sum(v => v.Count)} domande lette.");
        }
        var result = TicketClosureReview.Build(table, records);
        var closed = result.AsEnumerable().Where(r => Convert.ToString(r["AZIONE_PROPOSTA"]) == "CHIUDERE").ToList();
        if (databaseOutput != null) Directory.CreateDirectory(databaseOutput);
        string output = Path.Combine(databaseOutput ?? "work", "ticket-chiusure-csv-probe.json");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Total = total, Eligible = table.Rows.Count, Proposed = closed.Count, HasDatabase = databaseOutput != null, Seconds = clock.Elapsed.TotalSeconds,
            MaxEvidenceLength = result.AsEnumerable().Select(r => Convert.ToString(r["EVIDENZE"])!.Length).DefaultIfEmpty(0).Max(),
            Rules = closed.GroupBy(r => Convert.ToString(r["REGOLA"])).Select(g => new { Rule = g.Key, Count = g.Count() }).OrderByDescending(g => g.Count),
            Samples = closed.Where(r => Convert.ToString(r["REGOLA"]) != "DUPLICATO")
                .GroupBy(r => Convert.ToString(r["REGOLA"]))
                .SelectMany(g => g.Take(2).Concat(g.TakeLast(2))).Distinct()
                .Select(r => new { Id = r["ID_TICKET"], Rule = r["REGOLA"], Message = r["PRIMO_MSG_STUDENTE"], Evidence = r["EVIDENZE"] })
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (databaseOutput != null)
        {
            // Nessun aggiornamento o invio: il probe usa esclusivamente il loader e il valutatore.
            Console.WriteLine("Excel: " + TicketClosureWorkbook.Export(result, databaseOutput));
            File.WriteAllText(Path.Combine(databaseOutput, "decisions.json"), JsonSerializer.Serialize(result.AsEnumerable().Select(r => result.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => r[c] == DBNull.Value ? null : r[c])), new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"CSV: {total} ticket, {table.Rows.Count} elaborabili, {closed.Count} proposte; {clock.Elapsed.TotalSeconds:F1}s. Dettaglio locale: {output}");
    }
}
