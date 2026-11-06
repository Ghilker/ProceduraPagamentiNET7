using System.Data;
using System.Data.SqlClient;
using System.Reflection;
using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class CatalogoMotiviExportRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "BS", "PA", "PK", "CI" })
        {
            Check(benefit, "motivo verificato in DB", 0, "RED012", "047", "COMPLETA");
            Check(benefit, "nuovo motivo con numero assegnato", 0, "RED033", "231", "DA REGISTRARE", "RED033");
            Check(benefit, "motivi esistenti e nuovi", 0, "RED012;RED033", "047;231", "DA REGISTRARE", "RED033");
            Check(benefit, "alias del nuovo motivo deduplicato", 0, "RED033;GENF231;RED012", "231;047", "DA REGISTRARE", "231");
            Check(benefit, "motivo sconosciuto", 0, "SCONOSCIUTO", "", "DA VERIFICARE", "SCONOSCIUTO");
            Check(benefit, "motivo verificato e sconosciuto", 0, "RED012;SCONOSCIUTO", "047", "DA VERIFICARE", "SCONOSCIUTO");
            Check(benefit, "sconosciuto prevale sullo stato da registrare", 0,
                "RED012;RED033;SCONOSCIUTO", "047;231", "DA VERIFICARE", "SCONOSCIUTO");
            Check(benefit, "forzatura storica assente nel catalogo corrente", 0, "GENF211", "", "DA VERIFICARE", "GENF211");
            Check(benefit, "forzatura 223 conserva il codice gia occupato", 0, "GENF223", "223", "COMPLETA");
            Check(benefit, "forzatura 224 conserva il codice gia occupato", 0, "GENF224", "224", "COMPLETA");
            Check(benefit, "escluso senza motivo", 0, "", "", "DA VERIFICARE", "motivi");
            Check(benefit, "escluso con soli separatori", 0, " ; ; ", "", "DA VERIFICARE", "motivi");
            Check(benefit, "idoneo", 1, "", "", "NON APPLICABILE");
            Check(benefit, "vincitore", 2, "", "", "NON APPLICABILE");
            Check(benefit, "ammesso non espone segnalazioni pur mantenendo i motivi tecnici", 1,
                "RED033;SCONOSCIUTO", "", "NON APPLICABILE");
            Check(benefit, "risultato assente", null, "", "", "");
        }
        Check("BS", "MER170 separato dalla forzatura semestre filtro", 0,
            "GENF201;MER170", "201;239", "DA REGISTRARE", "MER170");
        Check("BS", "deduplica del motivo BS004 e forzatura 222", 0,
            "BS004;GENF222", "222", "COMPLETA");
        Check("BS", "114 riguarda le modalita di pagamento", 0, "VAR030", "114", "COMPLETA");
        Check("BS", "beneficio non restituito distinto dalla rinuncia 051", 0, "BS002", "245", "DA REGISTRARE", "BS002");
        Check("CI", "beneficio non restituito distinto dalla rinuncia 073", 0, "CI004", "246", "DA REGISTRARE", "CI004");

        Verify("colonne della mappatura accanto ai motivi tecnici", () =>
        {
            var table = BuildOutput();
            foreach (string benefit in new[] { "BS", "PA", "PK", "CI" })
            {
                int index = table.Columns[$"CodiciEsclusioneCalcolata_{benefit}"]!.Ordinal;
                Equal(table.Columns[index + 1].ColumnName, $"DescrizioneEsclusioneCalcolata_{benefit}", "descrizione tecnica");
                Equal(table.Columns[index + 2].ColumnName, $"CodiciSegnalazioneCalcolati_{benefit}", "codici segnalazione");
                Equal(table.Columns[index + 3].ColumnName, $"StatoMappaturaSegnalazioni_{benefit}", "stato mappatura");
                Equal(table.Columns[index + 4].ColumnName, $"DettaglioMappaturaSegnalazioni_{benefit}", "diagnostica mappatura");
                for (int offset = 0; offset <= 4; offset++)
                    Equal(table.Columns[index + offset].DataType, typeof(string), "colonna testuale");
            }
        });
        Verify("catalogo consultabile con codici esistenti e da registrare", () =>
        {
            var method = typeof(Verifica).GetMethod("BuildCatalogoEsclusioniTable", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Tabella del catalogo assente.");
            var catalog = (DataTable)method.Invoke(null, null)!;
            Equal(catalog.Columns.Count, 8, "otto colonne del catalogo");
            foreach (string column in new[]
            {
                "Codice regola", "Descrizione regola", "Benefici", "Codice segnalazione",
                "Descrizione segnalazione", "Stato", "Fonte", "Nota"
            })
                if (!catalog.Columns.Contains(column))
                    throw new InvalidOperationException($"Colonna catalogo assente: {column}.");
            var staticRows = catalog.AsEnumerable()
                .Where(row => !row.Field<string>("Codice regola")!.Contains('*')).ToArray();
            Equal(staticRows.Length, 74, "74 motivi statici");
            Equal(staticRows.Select(row => row.Field<string>("Codice segnalazione")).Distinct().Count(), 74,
                "numeri univoci nel catalogo esportato");
            foreach (DataRow row in staticRows)
            {
                Required(row.Field<string>("Codice segnalazione"), "numero di segnalazione");
                Required(row.Field<string>("Descrizione segnalazione"), "descrizione della segnalazione");
                Required(row.Field<string>("Fonte"), "fonte del mapping");
            }
            var merit = staticRows.Single(row => row.Field<string>("Codice regola") == "MER012");
            Equal(merit.Field<string>("Codice segnalazione")!, "033", "codice DB del merito");
            Equal(merit.Field<string>("Stato")!, "Verificata", "stato del codice DB");
            var newlyAssigned = staticRows.Single(row => row.Field<string>("Codice regola") == "RED033");
            Equal(newlyAssigned.Field<string>("Codice segnalazione")!, "231", "numero assegnato al nuovo motivo");
            Equal(newlyAssigned.Field<string>("Stato")!, "DaRegistrare", "nuovo numero non dichiarato gia registrato");
            Required(newlyAssigned.Field<string>("Descrizione segnalazione"), "descrizione da registrare");
            var payment = staticRows.Single(row => row.Field<string>("Codice regola") == "VAR030");
            Equal(payment.Field<string>("Codice segnalazione")!, "114", "114 conservato");
            Contains(payment.Field<string>("Descrizione regola"), "pagamento", "causale tecnica 114");
            Contains(payment.Field<string>("Descrizione segnalazione"), "pagamento", "causale DB 114");
        });

        if (failures.Count > 0)
            throw new InvalidOperationException($"Esportazione catalogo: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: export dei codici DB, numeri da registrare, sconosciuti e catalogo completo.");

        void Check(string benefit, string scenario, int? outcome, string reasons, string mapped, string state, string? diagnostic = null)
        {
            Verify($"{benefit}: {scenario}", () =>
            {
                using var connection = new SqlConnection();
                var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico = "20262027" };
                var key = new StudentKey("TEST", "1");
                var info = pipeline.GetOrCreateStudent(key);
                info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
                EsitoBeneficioCalcolato? originalResult = null;
                if (outcome.HasValue)
                {
                    originalResult = new EsitoBeneficioCalcolato
                    {
                        CodBeneficio = benefit, Richiesto = true, EsitoCalcolato = outcome.Value,
                        CodiciMotivo = reasons, Motivi = "Descrizione tecnica preservata"
                    };
                    pipeline.GetOrCreateEsitiCalcolatiByBenefit(key)[benefit] = originalResult;
                }
                var table = BuildOutput();
                var row = table.NewRow();
                var fill = typeof(Verifica).GetMethod("FillBenefitOutcomeColumns", BindingFlags.NonPublic | BindingFlags.Static)!;
                fill.Invoke(null, new object[] { row, pipeline, key });
                Equal(row[$"CodiciSegnalazioneCalcolati_{benefit}"], mapped, "codici di segnalazione");
                Equal(row[$"StatoMappaturaSegnalazioni_{benefit}"], state, "stato mappatura");
                if (diagnostic != null)
                    Contains(Convert.ToString(row[$"DettaglioMappaturaSegnalazioni_{benefit}"]), diagnostic, "diagnostica");
                else
                    Equal(row[$"DettaglioMappaturaSegnalazioni_{benefit}"], "", "nessuna diagnostica superflua");
                if (outcome.HasValue)
                {
                    Equal(row[$"EsitoCalcolato_{benefit}"], outcome.Value, "esito esportato preservato");
                    Equal(row[$"CodiciEsclusioneCalcolata_{benefit}"], reasons, "motivo tecnico esportato preservato");
                    Equal(row[$"DescrizioneEsclusioneCalcolata_{benefit}"], "Descrizione tecnica preservata", "descrizione preservata");
                    Equal(originalResult!.EsitoCalcolato, outcome.Value, "esito sorgente preservato");
                    Equal(originalResult.CodiciMotivo, reasons, "motivi sorgente preservati");
                }
                else
                {
                    Equal(row[$"EsitoCalcolato_{benefit}"], DBNull.Value, "nessun esito inventato");
                }

                // Riutilizzare la riga non deve lasciare segnalazioni di un beneficio non piu richiesto.
                info.EsitoBorsaFacts.BeneficiRichiesti.Clear();
                fill.Invoke(null, new object[] { row, pipeline, key });
                Equal(row[$"CodiciSegnalazioneCalcolati_{benefit}"], "", "beneficio non richiesto");
                Equal(row[$"StatoMappaturaSegnalazioni_{benefit}"], "", "stato non richiesto");
                Equal(row[$"DettaglioMappaturaSegnalazioni_{benefit}"], "", "diagnostica non richiesta");
            });
        }

        void Verify(string scenario, Action action)
        {
            checks++;
            try { action(); }
            catch (Exception ex) { failures.Add($"{scenario}: {ex.GetBaseException().Message}"); }
        }
    }

    private static DataTable BuildOutput()
        => (DataTable)typeof(Verifica).GetMethod("BuildOutputTable", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;

    private static void Required(string? actual, string label)
    {
        if (string.IsNullOrWhiteSpace(actual))
            throw new InvalidOperationException($"Manca {label}.");
    }

    private static void Contains(string? actual, string expected, string label)
    {
        if (actual == null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label}: manca {expected} in {actual}.");
    }

    private static void Equal(object actual, object expected, string label)
    {
        if (!Equals(actual, expected))
            throw new InvalidOperationException($"{label}: {actual}, atteso {expected}.");
    }
}

