using System.Data;
using System.Data.SqlClient;
using ProcedureNet7;

internal static class DrosoSqlStatusTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        string connectionString = Environment.GetEnvironmentVariable("DROSO_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Connessione di test non disponibile.");
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await Execute(connection, @"
CREATE TABLE #DrosoAllegati(id_allegato numeric(18,0) NOT NULL PRIMARY KEY,
    cod_fiscale char(16) NOT NULL, data_fine_validita datetime NULL);
CREATE TABLE #DrosoStatus(id_allegato numeric(18,0) NOT NULL, cod_status varchar(2) NOT NULL,
    data_validita datetime NOT NULL, utente varchar(50) NOT NULL, data_fine_validita datetime NULL,
    Id_STATUS_ALLEGATI int IDENTITY NOT NULL, Motivazione_Esclusione nvarchar(2) NULL,
    Messaggio_inviato char(1) NULL, PRIMARY KEY(id_allegato,cod_status,data_validita));");

        var document = await Seed(connection, 1);
        document.Predicted = "PASSAPORTO"; document.Confidence = .900001;
        var first = await Write(connection, document);
        check(first.Applied, "SQL: categoria ammessa oltre il 90% inserisce il nuovo stato");
        check(await Status(connection, 1) == "02", "SQL: nuovo stato 02");
        check(await Count(connection, 1) == 2, "SQL: INSERT conserva lo stato storico");
        check(await Scalar(connection, "SELECT COUNT(*) FROM #DrosoStatus WHERE id_allegato=1 AND data_fine_validita IS NOT NULL") == 0,
            "SQL: nessun UPDATE delle righe precedenti");
        check(!((await Write(connection, document)).Applied) && await Count(connection, 1) == 2,
            "SQL: una seconda esecuzione non inserisce duplicati");

        var exact = await Seed(connection, 2);
        exact.Predicted = "RICEVUTA"; exact.Confidence = .90;
        check((await Write(connection, exact)).Applied && await Status(connection, 2) == "04",
            "SQL: 90% esatto inserisce status 04");
        check(await Scalar(connection, "SELECT COUNT(*) FROM #DrosoStatus WHERE id_allegato=2 AND cod_status='04' AND Motivazione_Esclusione IS NULL AND LEN(utente)>0") == 1,
            "SQL: motivo vuoto e utente di scrittura registrato");

        var other = await Seed(connection, 3);
        other.Predicted = "ERRATO"; other.Confidence = .99;
        check((await Write(connection, other)).Applied && await Status(connection, 3) == "04",
            "SQL: ERRATO ad alta confidenza resta respinto");

        var changed = await Seed(connection, 4);
        changed.Predicted = "PERMESSO_SOGGIORNO"; changed.Confidence = .99;
        await Execute(connection, "INSERT #DrosoStatus(id_allegato,cod_status,data_validita,utente) VALUES(4,'01',DATEADD(day,1,GETDATE()),'SYNTHETIC-OFFICE')");
        check(!((await Write(connection, changed)).Applied) && await Count(connection, 4) == 2,
            "SQL: nuova riga 01 dell'ufficio invalida la selezione precedente");
        await SelectLatest(connection, changed);
        var afterHuman = await Write(connection, changed);
        check(afterHuman.Applied && afterHuman.Date > changed.SubmittedAt,
            "SQL: timestamp del nuovo stato successivo alla selezione");

        var wrongOwner = await Seed(connection, 5);
        wrongOwner.Predicted = "PASSAPORTO"; wrongOwner.Confidence = .99;
        wrongOwner.CodiceFiscale = "OTHER00000000001";
        check(!((await Write(connection, wrongOwner)).Applied) && await Count(connection, 5) == 1,
            "SQL: identità dell'allegato diversa non viene aggiornata");

        var ended = await Seed(connection, 6);
        ended.Predicted = "PASSAPORTO"; ended.Confidence = .99;
        await Execute(connection, "UPDATE #DrosoAllegati SET data_fine_validita=GETDATE() WHERE id_allegato=6");
        check(!((await Write(connection, ended)).Applied) && await Count(connection, 6) == 1,
            "SQL: allegato cessato non viene aggiornato");
    }

    private static async Task<DrosoDocument> Seed(SqlConnection connection, int id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT #DrosoAllegati(id_allegato,cod_fiscale) VALUES(@id,'TESTCF0000000001');
INSERT #DrosoStatus(id_allegato,cod_status,data_validita,utente) VALUES(@id,'01','20200101','SYNTHETIC');";
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await command.ExecuteNonQueryAsync();
        var document = new DrosoDocument { IdAllegato = id, CodiceFiscale = "TESTCF0000000001", AnalysisCompleted = true };
        await SelectLatest(connection, document);
        return document;
    }

    private static async Task SelectLatest(SqlConnection connection, DrosoDocument document)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) Id_STATUS_ALLEGATI,data_validita FROM #DrosoStatus WHERE id_allegato=@id ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC";
        command.Parameters.Add("@id", SqlDbType.Decimal).Value = document.IdAllegato;
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new Exception("Fixture sintetica assente.");
        document.SelectedStatusId = reader.GetInt32(0); document.SubmittedAt = reader.GetDateTime(1);
    }

    private static async Task<(bool Applied, DateTime? Date)> Write(SqlConnection connection, DrosoDocument document)
    {
        using var transaction = connection.BeginTransaction();
        using var command = DrosoStatusRepository.CreateInsertCommand(connection, transaction, document);
        // The production statement is exercised only against connection-local tables.
        command.CommandText = command.CommandText.Replace("dbo.STATUS_ALLEGATI", "#DrosoStatus", StringComparison.Ordinal)
            .Replace("dbo.ALLEGATI", "#DrosoAllegati", StringComparison.Ordinal);
        if (command.CommandText.Contains("dbo.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Il test contiene riferimenti a tabelle persistenti.");
        bool applied; DateTime? date;
        using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new Exception("Risultato SQL assente.");
            applied = reader.GetBoolean(reader.GetOrdinal("Applied"));
            int ordinal = reader.GetOrdinal("StatusDate");
            date = reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
        }
        transaction.Commit();
        return (applied, date);
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> Scalar(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private static Task<int> Count(SqlConnection connection, int id) => Scalar(connection,
        "SELECT COUNT(*) FROM #DrosoStatus WHERE id_allegato=" + id);
    private static async Task<string> Status(SqlConnection connection, int id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) cod_status FROM #DrosoStatus WHERE id_allegato=@id ORDER BY data_validita DESC,Id_STATUS_ALLEGATI DESC";
        command.Parameters.Add("@id", SqlDbType.Int).Value = id;
        return Convert.ToString(await command.ExecuteScalarAsync())!;
    }
}
