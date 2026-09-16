using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal static class VerificaBlocchiIncongruenzeCatalog
    {
        public const string IseeNonUniversitario = "INU";
        public const string IncongruenzaIseeNonUniversitario = "31";

        private static readonly IReadOnlyDictionary<string, string> Descrizioni =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [IseeNonUniversitario] = "ISEE universitario mancante: è presente un'attestazione ordinaria da regolarizzare entro il 10 dicembre"
            };

        public static string GetDescrizione(string? codice)
        {
            string normalized = NormalizeAndValidate(codice);
            return Descrizioni.TryGetValue(normalized, out string? descrizione)
                ? descrizione
                : normalized;
        }

        public static string NormalizeAndValidate(string? codice)
        {
            string normalized = (codice ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.Length != 3)
                throw new ArgumentException("I codici dei blocchi/incongruenze devono essere VARCHAR(3).", nameof(codice));

            return normalized;
        }

        public static string NormalizeAndValidateIncongruenza(string? codice)
        {
            string normalized = (codice ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.Length != 2)
                throw new ArgumentException("I codici delle incongruenze devono essere VARCHAR(2).", nameof(codice));

            return normalized;
        }

        public static string GetDescrizioneIncongruenza(string? codice)
        {
            string normalized = NormalizeAndValidateIncongruenza(codice);
            return normalized == IncongruenzaIseeNonUniversitario
                ? Descrizioni[IseeNonUniversitario]
                : normalized;
        }
    }

    internal sealed class CalcoloBlocchiIncongruenze : IVerificaModule
    {
        public string Name => "BlocchiIncongruenze";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            int inuCount = 0;
            int incongruenza31Count = 0;
            foreach (var pair in context.Students)
            {
                var facts = pair.Value.InformazioniBeneficio.EsitoBorsaFacts;
                facts.CodiciBlocchiIncongruenze.Clear();
                facts.CodiciIncongruenzeNonEscludenti.Clear();

                if (facts.IseeOrdinarioInAttesaRegolarizzazione)
                {
                    facts.CodiciBlocchiIncongruenze.Add(
                        VerificaBlocchiIncongruenzeCatalog.IseeNonUniversitario);
                    facts.CodiciIncongruenzeNonEscludenti.Add(
                        VerificaBlocchiIncongruenzeCatalog.IncongruenzaIseeNonUniversitario);
                    inuCount++;
                    incongruenza31Count++;
                }
            }

            Logger.LogInfo(
                null,
                $"[Verifica.Module.{Name}] Segnalazioni non escludenti calcolate | INU={inuCount} | incongruenza 31={incongruenza31Count}");
        }
    }

    internal static class VerificaBlocchiIncongruenzeWriter
    {
        private const string UtenteVerifica = "Verifica";

        public static void Sincronizza(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            SincronizzaCodice(
                context,
                VerificaBlocchiIncongruenzeCatalog.IseeNonUniversitario);

            SincronizzaIncongruenza(
                context,
                VerificaBlocchiIncongruenzeCatalog.IncongruenzaIseeNonUniversitario);
        }

        private static void SincronizzaCodice(VerificaPipelineContext context, string codice)
        {
            string normalizedCode = VerificaBlocchiIncongruenzeCatalog.NormalizeAndValidate(codice);
            var daAggiungere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var daRimuovere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in context.Students)
            {
                string codFiscale = pair.Key.CodFiscale;
                if (string.IsNullOrWhiteSpace(codFiscale))
                    continue;

                bool attivo = pair.Value.InformazioniBeneficio.EsitoBorsaFacts
                    .CodiciBlocchiIncongruenze.Contains(normalizedCode);

                if (attivo)
                    daAggiungere.Add(codFiscale);
                else
                    daRimuovere.Add(codFiscale);
            }

            using SqlTransaction transaction = context.Connection.BeginTransaction();
            try
            {
                AssicuraDefinizioneCatalogo(
                    context.Connection,
                    transaction,
                    normalizedCode,
                    VerificaBlocchiIncongruenzeCatalog.GetDescrizione(normalizedCode));

                BlockRemoveResult removeResult = BlocksUtil.RemoveBlock(
                    context.Connection,
                    transaction,
                    daRimuovere.ToList(),
                    normalizedCode,
                    context.AnnoAccademico,
                    UtenteVerifica);

                BlockAddResult addResult = BlocksUtil.AddBlock(
                    context.Connection,
                    transaction,
                    daAggiungere.ToList(),
                    normalizedCode,
                    context.AnnoAccademico,
                    UtenteVerifica,
                    inserisciGiaRimossi: true);

                transaction.Commit();

                Logger.LogInfo(
                    null,
                    $"[Verifica.BlocchiIncongruenze] {normalizedCode} sincronizzato | aggiunti={addResult.ActuallyAdded.Count} | già attivi={addResult.AlreadyHasBlock.Count} | rimossi={removeResult.ActuallyRemoved.Count}");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void AssicuraDefinizioneCatalogo(
            SqlConnection connection,
            SqlTransaction transaction,
            string codice,
            string descrizione)
        {
            const string sql = @"
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.Tipologie_motivazioni_blocco_pag
    WHERE Cod_tipologia_blocco = @Codice
)
BEGIN
    INSERT INTO dbo.Tipologie_motivazioni_blocco_pag
        (Cod_tipologia_blocco, Descrizione)
    VALUES
        (@Codice, @Descrizione);
END;";

            using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@Codice", System.Data.SqlDbType.VarChar, 3).Value = codice;
            command.Parameters.Add("@Descrizione", System.Data.SqlDbType.NVarChar, 500).Value = descrizione;
            command.ExecuteNonQuery();
        }

        private static void SincronizzaIncongruenza(VerificaPipelineContext context, string codice)
        {
            string normalizedCode =
                VerificaBlocchiIncongruenzeCatalog.NormalizeAndValidateIncongruenza(codice);

            var targets = new DataTable();
            targets.Columns.Add("NumDomanda", typeof(decimal));
            targets.Columns.Add("Attiva", typeof(bool));

            var targetStates = new Dictionary<decimal, bool>();
            foreach (var pair in context.Students)
            {
                if (!decimal.TryParse(
                        pair.Key.NumDomanda,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal numDomanda))
                {
                    continue;
                }

                bool attiva = pair.Value.InformazioniBeneficio.EsitoBorsaFacts
                    .CodiciIncongruenzeNonEscludenti.Contains(normalizedCode);
                targetStates[numDomanda] = attiva;
            }

            foreach (var target in targetStates)
                targets.Rows.Add(target.Key, target.Value);

            if (targets.Rows.Count == 0)
                return;

            using SqlTransaction transaction = context.Connection.BeginTransaction();
            try
            {
                const string createTempSql = @"
CREATE TABLE #VerificaIncongruenzeTarget
(
    NumDomanda DECIMAL(18,0) NOT NULL PRIMARY KEY,
    Attiva BIT NOT NULL
);";
                using (var createCommand = new SqlCommand(createTempSql, context.Connection, transaction))
                    createCommand.ExecuteNonQuery();

                using (var bulk = new SqlBulkCopy(context.Connection, SqlBulkCopyOptions.Default, transaction))
                {
                    bulk.DestinationTableName = "#VerificaIncongruenzeTarget";
                    bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
                    bulk.ColumnMappings.Add("Attiva", "Attiva");
                    bulk.WriteToServer(targets);
                }

                const string syncSql = @"
DECLARE @Rimosse INT = 0;
DECLARE @Aggiunte INT = 0;

UPDATE i
SET i.Data_fine_validita = CURRENT_TIMESTAMP,
    i.EliminataDa = @Utente
FROM dbo.Incongruenze i
INNER JOIN #VerificaIncongruenzeTarget t
    ON t.NumDomanda = i.Num_domanda
WHERE i.Anno_accademico = @AA
  AND i.Cod_incongruenza = @Codice
  AND i.Data_fine_validita IS NULL
  AND t.Attiva = 0;

SET @Rimosse = @@ROWCOUNT;

INSERT INTO dbo.Incongruenze
    (Anno_accademico, Num_domanda, Cod_incongruenza, Data_validita,
     Data_fine_validita, Cod_forzatura, Utente, id_domanda)
SELECT
    @AA,
    t.NumDomanda,
    @Codice,
    CURRENT_TIMESTAMP,
    NULL,
    NULL,
    @Utente,
    d.Id_domanda
FROM #VerificaIncongruenzeTarget t
OUTER APPLY
(
    SELECT TOP (1) dom.Id_domanda
    FROM dbo.Domanda dom
    WHERE dom.Anno_accademico = @AA
      AND dom.Num_domanda = t.NumDomanda
    ORDER BY dom.Data_validita DESC
) d
WHERE t.Attiva = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.Incongruenze i
      WHERE i.Anno_accademico = @AA
        AND i.Num_domanda = t.NumDomanda
        AND i.Cod_incongruenza = @Codice
        AND i.Data_fine_validita IS NULL
  );

SET @Aggiunte = @@ROWCOUNT;
SELECT @Aggiunte AS Aggiunte, @Rimosse AS Rimosse;";

                int aggiunte = 0;
                int rimosse = 0;
                using (var command = new SqlCommand(syncSql, context.Connection, transaction))
                {
                    command.Parameters.Add("@AA", SqlDbType.Char, 8).Value = context.AnnoAccademico;
                    command.Parameters.Add("@Codice", SqlDbType.VarChar, 2).Value = normalizedCode;
                    command.Parameters.Add("@Utente", SqlDbType.VarChar, 50).Value = UtenteVerifica;
                    using SqlDataReader reader = command.ExecuteReader();
                    if (reader.Read())
                    {
                        aggiunte = reader.GetInt32(reader.GetOrdinal("Aggiunte"));
                        rimosse = reader.GetInt32(reader.GetOrdinal("Rimosse"));
                    }
                }

                transaction.Commit();
                Logger.LogInfo(
                    null,
                    $"[Verifica.BlocchiIncongruenze] incongruenza {normalizedCode} sincronizzata | aggiunte={aggiunte} | rimosse={rimosse}");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
