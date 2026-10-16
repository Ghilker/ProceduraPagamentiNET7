using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using ProcedureNet7;

internal static class DrosoExtractionTests
{
    public static void Run(Action<bool, string> check)
    {
        using var connection = new SqlConnection();
        using var unlimited = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0);
        check(unlimited.Connection == connection && connection.State == ConnectionState.Closed,
            "Costruire la selezione non apre una connessione SQL");
        check(unlimited.CommandText.Contains("SELECT s.Id_allegato,s.cf,s.tipo,s.coorte,sa.data_validita,", StringComparison.Ordinal)
            && !unlimited.CommandText.Contains("TOP (@maximum)", StringComparison.Ordinal)
            && !unlimited.Parameters.Contains("@maximum"),
            "Massimo zero seleziona tutti senza TOP né un limite sostitutivo");
        check(unlimited.CommandText.Contains("CROSS APPLY (SELECT TOP (1) cod_status,data_validita,Id_STATUS_ALLEGATI FROM STATUS_ALLEGATI", StringComparison.Ordinal)
            && unlimited.CommandText.Split("TOP", StringSplitOptions.None).Length == 2,
            "Massimo zero conserva solo TOP (1) per l'ultimo stato, senza altri cap SQL");
        check(unlimited.Parameters.Count == 0 && !unlimited.CommandText.Contains("@startDate", StringComparison.Ordinal)
            && !unlimited.CommandText.Contains("@endDateExclusive", StringComparison.Ordinal),
            "Date assenti non impongono un intervallo implicito");

        foreach (int maximum in new[] { 1, 50, int.MaxValue })
        {
            using var limited = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", maximum);
            check(limited.CommandText.Contains("SELECT TOP (@maximum) s.Id_allegato", StringComparison.Ordinal)
                && limited.Parameters["@maximum"].SqlDbType == SqlDbType.Int
                && (int)limited.Parameters["@maximum"].Value == maximum,
                $"Massimo {maximum} parametrizzato senza troncamenti");
        }
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", -1), "maximum"),
            "Massimo negativo respinto prima di SQL");

        DateTime start = new(2026, 10, 5, 15, 20, 30);
        DateTime end = new(2026, 10, 6, 8, 10, 15);
        using var ranged = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 50, start, end);
        var lower = ranged.Parameters["@startDate"];
        var upper = ranged.Parameters["@endDateExclusive"];
        check(lower.SqlDbType == SqlDbType.DateTime && (DateTime)lower.Value == new DateTime(2026, 10, 5)
            && upper.SqlDbType == SqlDbType.DateTime && (DateTime)upper.Value == new DateTime(2026, 10, 7),
            "Intervallo parametrizzato: inizio a mezzanotte e fine esclusiva al giorno successivo");
        check(ranged.CommandText.Contains("AND sa.data_validita>=@startDate", StringComparison.Ordinal)
            && ranged.CommandText.Contains("AND sa.data_validita<@endDateExclusive", StringComparison.Ordinal),
            "Intervallo applicato alla data dello stato pendente selezionato");
        bool Within(DateTime submitted) => submitted >= (DateTime)lower.Value && submitted < (DateTime)upper.Value;
        check(!Within(start.Date.AddMilliseconds(-3)) && Within(start.Date)
            && Within(new DateTime(2026, 10, 6, 23, 59, 59, 997)) && !Within(end.Date.AddDays(1)),
            "Inclusi entrambi i giorni interi, esclusi istanti prima dell'inizio e dal giorno seguente");
        int finalWhere = ranged.CommandText.IndexOf("WHERE sa.cod_status='01'", StringComparison.Ordinal);
        check(finalWhere > ranged.CommandText.IndexOf("FROM ranked WHERE rn=1", StringComparison.Ordinal)
            && finalWhere > ranged.CommandText.IndexOf("ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC) sa", StringComparison.Ordinal)
            && ranged.CommandText.IndexOf("sa.data_validita>=@startDate", StringComparison.Ordinal) > finalWhere
            && ranged.CommandText.IndexOf("sa.data_validita<@endDateExclusive", StringComparison.Ordinal) > finalWhere,
            "Date filtrate dopo ultimo allegato e ultimo stato: nessun allegato o stato vecchio riemerge");
        check(ranged.CommandText.EndsWith("ORDER BY sa.data_validita,s.Id_allegato", StringComparison.Ordinal)
            && ranged.CommandText.Contains("t.data_validita=sa.data_validita)=1", StringComparison.Ordinal),
            "Selezione mantiene ordinamento e controllo degli stati con data ambigua");

        using var fromOnly = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0, startDate: start);
        check(fromOnly.Parameters.Count == 1 && fromOnly.Parameters.Contains("@startDate")
            && !fromOnly.CommandText.Contains("@endDateExclusive", StringComparison.Ordinal),
            "Solo inizio imposta un unico estremo inferiore");
        using var throughOnly = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0, endDate: end);
        check(throughOnly.Parameters.Count == 1 && throughOnly.Parameters.Contains("@endDateExclusive")
            && !throughOnly.CommandText.Contains("@startDate", StringComparison.Ordinal),
            "Solo fine imposta un unico estremo superiore inclusivo per giorno");
        using var sameDay = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0, end.Date.AddHours(23), end.Date);
        check((DateTime)sameDay.Parameters["@startDate"].Value == end.Date
            && (DateTime)sameDay.Parameters["@endDateExclusive"].Value == end.Date.AddDays(1),
            "Stesso giorno ammesso anche quando gli orari passati sono invertiti");
        using var minimum = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            SqlDateTime.MinValue.Value, SqlDateTime.MinValue.Value);
        check((DateTime)minimum.Parameters["@startDate"].Value == new DateTime(1753, 1, 1),
            "Prima data SQL datetime ammessa");
        using var maximumStart = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            startDate: SqlDateTime.MaxValue.Value);
        using var maximumEnd = DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            endDate: new DateTime(9999, 12, 30));
        check((DateTime)maximumStart.Parameters["@startDate"].Value == new DateTime(9999, 12, 31)
            && (DateTime)maximumEnd.Parameters["@endDateExclusive"].Value == new DateTime(9999, 12, 31),
            "Estremi SQL al limite superiore non sono troncati");
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0, end, start), "endDate"),
            "Intervallo invertito respinto prima di SQL");
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            startDate: new DateTime(1752, 12, 31)), "startDate"), "Inizio prima del minimo SQL respinto");
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            endDate: DateTime.MinValue), "endDate"), "Fine prima del minimo SQL respinta");
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, "SyntheticFiles", 0,
            endDate: DateTime.MaxValue), "endDate"), "Fine senza giorno successivo rappresentabile respinta senza overflow");

        string malicious = "Files]; DROP TABLE STATUS_ALLEGATI;--";
        string quoted = "[Files]]; DROP TABLE STATUS_ALLEGATI;--].DIRSTUDIO.ALLEGATI";
        using var quotedArchive = DrosoDocumentRepository.CreateSelectionCommand(connection, malicious, 0);
        check(quotedArchive.CommandText.Contains("FROM " + quoted + " WHERE Id_Allegato_Adisu=s.Id_allegato", StringComparison.Ordinal)
            && !quotedArchive.CommandText.Replace(quoted, "", StringComparison.Ordinal).Contains("DROP TABLE", StringComparison.Ordinal),
            "Nome archivio con delimitatori e testo SQL resta un solo identificatore escapato");
        string longest = new(']', 128);
        using var longestArchive = DrosoDocumentRepository.CreateSelectionCommand(connection, longest, 0);
        check(longestArchive.CommandText.Contains("[" + new string(']', 256) + "].DIRSTUDIO.ALLEGATI", StringComparison.Ordinal),
            "Nome archivio lungo 128 caratteri conservato per intero dopo escaping");
        check(Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, new string('a', 129), 0), "filesDatabase")
            && Rejects(() => DrosoDocumentRepository.CreateSelectionCommand(connection, " ", 0), "filesDatabase"),
            "Nome archivio vuoto o troppo lungo respinto senza truncamento");

        var untouched = new DrosoSession { SourceDatabase = "SYNTHETIC", FilesDatabase = "ORIGINAL" };
        check(Rejects(() => DrosoDocumentRepository.ExtractAsync(connection, "SyntheticFiles", 0, untouched,
            new Progress<string>(), CancellationToken.None, startDate: end, endDate: start).GetAwaiter().GetResult(), "endDate")
            && connection.State == ConnectionState.Closed && untouched.Documents.Count == 0
            && untouched.SourceDatabase == "SYNTHETIC" && untouched.FilesDatabase == "ORIGINAL",
            "ExtractAsync respinge intervallo invalido prima di SQL e senza modificare la sessione");
        check(Rejects(() => DrosoDocumentRepository.ExtractAsync(connection, "SyntheticFiles", -1, untouched,
            new Progress<string>(), CancellationToken.None).GetAwaiter().GetResult(), "maximum"),
            "ExtractAsync respinge massimo negativo prima di verificare la connessione SQL");
    }

    private static bool Rejects(Action action, string parameter)
    {
        try { action(); return false; }
        catch (ArgumentException ex) { return ex.ParamName == parameter; }
    }
}
