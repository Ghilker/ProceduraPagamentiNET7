using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;

namespace ProcedureNet7
{
    internal class GenerazioneFileRevoche : BaseProcedure<ArgsGenerazioneFileRevoche>
    {
        public GenerazioneFileRevoche(MasterForm? _masterForm, SqlConnection? connection_string)
            : base(_masterForm, connection_string) { }

        private string selectedAA = "";
        private string selectedEnte = "-1";
        private string selectedSaveFolder = "";
        public Dictionary<string, DataTable> TabelleSeparate { get; private set; }
    = new(StringComparer.OrdinalIgnoreCase);
        public override void RunProcedure(ArgsGenerazioneFileRevoche args)
        {
            selectedAA = args._aaGenerazioneRev;
            selectedEnte = args._selectedCodEnte;
            selectedSaveFolder = args._selectedFolderPath;

            string enteFilterSql = BuildEnteFilterSql(selectedEnte);

            string query = $@"
;WITH
Domande AS
(
    SELECT d.Anno_accademico, d.Tipo_bando, d.Num_domanda, d.Cod_fiscale
    FROM Domanda d
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando      = 'LZ'
),
PagamentiAgg AS
(
    SELECT
        p.Anno_accademico,
        p.Num_domanda,
        SUM(p.Imp_pagato) AS ImportoPagato,
        STRING_AGG(p.Cod_tipo_pagam, ', ') AS TipiPagamento,
        STRING_AGG(p.Cod_mandato, '/') AS Mandati,
        STRING_AGG(p.Ese_finanziario, '/') AS Ese_finanziari
    FROM Pagamenti p
    WHERE p.Ritirato_azienda = 0
      AND (
            p.cod_tipo_pagam IN
            (
                SELECT DISTINCT dpn.Cod_tipo_pagam_new
                FROM Decod_pagam_new dpn
                INNER JOIN Tipologie_pagam tp
                    ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
                  AND tp.visibile = 1
            )
            OR
            p.cod_tipo_pagam IN
            (
                SELECT dpn.Cod_tipo_pagam_old
                FROM Decod_pagam_new dpn
                INNER JOIN Tipologie_pagam tp
                    ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
                  AND tp.visibile = 1
            )
      )
    GROUP BY p.Anno_accademico, p.Num_domanda
),
AssegnazioniRanked AS
(
    SELECT
        ap.Anno_Accademico,
        ap.Cod_Fiscale,
        ap.Cod_Pensionato,
        ap.Cod_Stanza,
        ap.Data_Decorrenza,
        ap.Data_Fine_Assegnazione,
        ROW_NUMBER() OVER
        (
            PARTITION BY ap.Anno_Accademico, ap.Cod_Fiscale
            ORDER BY
                ap.Data_Fine_Assegnazione DESC,
                ap.Data_Decorrenza DESC,
                ap.Cod_Pensionato DESC,
                ap.Cod_Stanza DESC
        ) AS rn_last
    FROM Assegnazione_PA ap
    WHERE ap.Anno_Accademico     = @AA
      AND ap.Cod_movimento       = '01'
      AND ap.Ind_Assegnazione    = 1
      AND ap.Status_Assegnazione = 0
      AND ap.Data_Accettazione IS NOT NULL
      AND ap.Data_Decorrenza IS NOT NULL
      AND ap.Data_Fine_Assegnazione IS NOT NULL
),
AssegnazioniDettaglio AS
(
    SELECT
        ap.Anno_Accademico,
        ap.Cod_Fiscale,
        ap.Cod_Pensionato,
        ap.Cod_Stanza,
        DATEDIFF(DAY, ap.Data_Decorrenza, ap.Data_Fine_Assegnazione)
        + CASE WHEN ap.rn_last = 1 THEN 1 ELSE 0 END AS Permanenza,
        CONVERT(decimal(18, 2),
            (ISNULL(cs.Importo, 0) / 30.4375) *
            (
                DATEDIFF(DAY, ap.Data_Decorrenza, ap.Data_Fine_Assegnazione)
                + CASE WHEN ap.rn_last = 1 THEN 1 ELSE 0 END
            )
        ) AS Costo_posto_alloggio
    FROM AssegnazioniRanked ap
    LEFT JOIN vStanza vzCosto
        ON vzCosto.Cod_Pensionato = ap.Cod_Pensionato
       AND vzCosto.Cod_Stanza     = ap.Cod_Stanza
    LEFT JOIN Costo_Servizio cs
        ON cs.Anno_accademico = ap.Anno_Accademico
       AND cs.Cod_pensionato  = ap.Cod_Pensionato
       AND cs.Cod_periodo     = 'M'
       AND cs.Tipo_stanza     = vzCosto.Tipo_Costo_Stanza
),
AssegnPAAgg AS
(
    SELECT
        a.Anno_Accademico,
        a.Cod_Fiscale,
        MAX(a.Cod_Pensionato) AS Cod_Pensionato,
        MAX(a.Cod_Stanza) AS Cod_Stanza,
        SUM(a.Permanenza) AS Permanenza,
        CONVERT(money, SUM(a.Costo_posto_alloggio)) AS Costo_posto_alloggio
    FROM AssegnazioniDettaglio a
    GROUP BY
        a.Anno_Accademico,
        a.Cod_Fiscale
)
SELECT
    d.Anno_accademico,
    ss.Descrizione,
    d.Cod_fiscale,
    d.Num_domanda,
    app.Cod_ente AS Cod_ente_gestione,
    app.Cod_sede_studi AS Cod_sede_studi_gestione,

    st.Codice_Studente,
    st.Nome,
    st.Cognome,
    CONVERT(char(12), st.Data_nascita, 103) AS data_nascita,

    ts.Descrizione AS Tipologia_studi,

    CONVERT(money, bs.Imp_beneficio, 0) AS Imp_BS,

    p.TipiPagamento,
    CONVERT(money, p.ImportoPagato, 0) AS Liquidato,
    CONVERT(money, p.ImportoPagato, 0) AS Recupero_borsa_di_studio,
    CONVERT(money, bs.Imp_beneficio - p.ImportoPagato, 0) AS Economia,

    pa.esito_PA,
    ci.Cod_tipo_esito AS esito_CI,

    pen.Descrizione AS Pensionato,
    vz.Tipo_Stanza,

    apa.Permanenza,
    apa.Costo_posto_alloggio,

    r.Importo AS Trattenuta_applicata_I_rata,
    CASE
        WHEN apa.Costo_posto_alloggio IS NULL THEN NULL
        ELSE CONVERT(money, apa.Costo_posto_alloggio - ISNULL(r.Importo, 0))
    END AS Recupero_servizio_abitativo,

    si.num_impegno_primaRata,
    si.Esercizio_prima_rata,
    si.num_impegno_saldo,
    si.esercizio_saldo,
    si.Tipo_fondo,
    si.Capitolo,

    res.INDIRIZZO AS Indirizzo_residenza,
    res.civico AS Civico_residenza,
    res.CAP AS CAP_residenza,
    res.comune_residenza,
    res.provincia_residenza,

    dom.Indirizzo_domicilio,
    dom.Cap_domicilio,
    dom.Descrizione AS Comune_domicilio,
    dom.prov AS Provincia_domicilio,

    st.Indirizzo_e_mail,
    pr.Indirizzo_PEC,
    st.Telefono_cellulare,

    p.Mandati AS Mandati_pagamento,
    p.Ese_finanziari AS Esercizio_finanziario_mandato,

    r.Cod_reversale,
    si.Esercizio_prima_rata AS Ese_finanziario_reversale,
    si.Determina_conferimento
FROM Domande d
JOIN Studente st
    ON st.Cod_fiscale = d.Cod_fiscale

JOIN vAppartenenza app
    ON app.Anno_accademico = d.Anno_accademico
   AND app.Cod_fiscale = d.Cod_fiscale
   AND app.Tipo_bando = d.Tipo_bando

JOIN Sede_studi ss
    ON ss.Cod_sede_studi = app.Cod_sede_studi
   AND ss.Cod_ente = app.Cod_ente

JOIN vIscrizioni isc
    ON isc.Anno_accademico = d.Anno_accademico
   AND isc.Cod_fiscale = d.Cod_fiscale
   AND isc.Cod_sede_studi = ss.Cod_sede_studi
   AND isc.Tipo_bando = d.Tipo_bando
   AND isc.Cod_tipologia_studi NOT IN ('06', '07')
   AND isc.Anno_corso = 1

JOIN Tipologie_studi ts
    ON ts.Cod_tipologia_studi = isc.Cod_tipologia_studi

JOIN vValori_calcolati vc
    ON vc.Anno_accademico = d.Anno_accademico
   AND vc.Num_domanda = d.Num_domanda

JOIN vDATIGENERALI_dom dg
    ON dg.Anno_accademico = d.Anno_accademico
   AND dg.Num_domanda = d.Num_domanda
   AND dg.Superamento_esami = 0
   AND dg.Superamento_esami_tassa_reg = 0

JOIN vResidenza res
    ON res.ANNO_ACCADEMICO = d.Anno_accademico
   AND res.COD_FISCALE = d.Cod_fiscale

JOIN vSpecifiche_impegni si
    ON si.Anno_accademico = d.Anno_accademico
   AND si.Num_domanda = d.Num_domanda
   AND si.Cod_beneficio = 'BS'

JOIN vEsiti_concorsiBS bs
    ON bs.Anno_accademico = d.Anno_accademico
   AND bs.Num_domanda = d.Num_domanda
   AND bs.Cod_tipo_esito <> 0

LEFT JOIN vEsiti_concorsiPA pa
    ON pa.Anno_accademico = d.Anno_accademico
   AND pa.Num_domanda = d.Num_domanda

LEFT JOIN vEsiti_concorsiCI ci
    ON ci.Anno_accademico = d.Anno_accademico
   AND ci.Num_domanda = d.Num_domanda

LEFT JOIN PagamentiAgg p
    ON p.Anno_accademico = d.Anno_accademico
   AND p.Num_domanda = d.Num_domanda

LEFT JOIN Reversali r
    ON r.Anno_accademico = d.Anno_accademico
   AND r.Num_domanda = d.Num_domanda
   AND r.Cod_reversale = '01'

LEFT JOIN vDomicilio dom
    ON dom.ANNO_ACCADEMICO = d.Anno_accademico
   AND dom.COD_FISCALE = d.Cod_fiscale

LEFT JOIN vProfilo pr
    ON pr.Cod_Fiscale = st.Cod_fiscale

LEFT JOIN AssegnPAAgg apa
    ON apa.Anno_Accademico = d.Anno_accademico
   AND apa.Cod_Fiscale = d.Cod_fiscale

LEFT JOIN Pensionati pen
    ON pen.Cod_pensionato = apa.Cod_Pensionato

LEFT JOIN vStanza vz
    ON vz.Cod_Pensionato = apa.Cod_Pensionato
   AND vz.Cod_Stanza = apa.Cod_Stanza

WHERE 1 = 1
{enteFilterSql}
ORDER BY
    app.Cod_ente,
    ss.Descrizione,
    d.Cod_fiscale;";

            DataTable estrazioneRevoche = ExecuteQuery(
                query,
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = selectedAA }
            );

            TabelleSeparate = SplitTablesCascade(estrazioneRevoche, selectedEnte);

            Logger.LogInfo(30, $"Tabelle separate generate: {TabelleSeparate.Count}");

            ExportSplitTablesToFolders(TabelleSeparate, selectedSaveFolder, selectedAA);

            Logger.LogInfo(100, "Fine lavorazione.");


            string test = "";
        }

        private Dictionary<string, DataTable> SplitTablesCascade(DataTable source, string selectedEnte)
        {
            var result = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

            var lvlRecupero = source.AsEnumerable()
                .GroupBy(r => HasRecuperoSomme(r) ? "CON_RECUPERO_SOMME" : "SENZA_RECUPERO_SOMME");

            foreach (var gRecupero in lvlRecupero)
            {
                var lvlFondo = gRecupero
                    .GroupBy(r => GetTipoFondoGroup(r));

                foreach (var gFondo in lvlFondo)
                {
                    if (selectedEnte == "-1")
                    {
                        var lvlEnte = gFondo
                            .GroupBy(r => GetEnteGestioneGroup(r, gFondo.Key));

                        foreach (var gEnte in lvlEnte)
                        {
                            var lvlPa = gEnte
                                .GroupBy(r => HasPA(r) ? "CON_PA" : "SENZA_PA");

                            foreach (var gPa in lvlPa)
                            {
                                string key = $"{gRecupero.Key}\\{gFondo.Key}\\{gEnte.Key}\\{gPa.Key}";
                                result[key] = ToDataTable(source, gPa);
                            }
                        }
                    }
                    else
                    {
                        var lvlPa = gFondo
                            .GroupBy(r => HasPA(r) ? "CON_PA" : "SENZA_PA");

                        foreach (var gPa in lvlPa)
                        {
                            string key = $"{gRecupero.Key}\\{gFondo.Key}\\{gPa.Key}";
                            result[key] = ToDataTable(source, gPa);
                        }
                    }
                }
            }

            return result;
        }

        private void ExportSplitTablesToFolders(
    Dictionary<string, DataTable> tables,
    string rootFolder,
    string aa)
        {
            if (tables == null || tables.Count == 0)
            {
                Logger.LogInfo(30, "Nessuna tabella da esportare.");
                return;
            }

            if (string.IsNullOrWhiteSpace(rootFolder))
                throw new ArgumentException("La cartella di destinazione non è valida.", nameof(rootFolder));

            string exportRoot = Path.Combine(
                rootFolder,
                SanitizePathSegment($"Revoche_{aa}")
            );

            Directory.CreateDirectory(exportRoot);

            foreach (var kvp in tables)
            {
                string key = kvp.Key;
                DataTable dt = kvp.Value;

                if (dt == null || dt.Rows.Count == 0)
                {
                    Logger.LogInfo(30, $"Tabella vuota saltata: {key}");
                    continue;
                }

                string targetFolder = BuildNestedFolderFromKey(exportRoot, key);
                Directory.CreateDirectory(targetFolder);

                string fileName = $"Revoche_{aa}.xlsx";

                Logger.LogInfo(30, $"Esporto '{key}' in '{targetFolder}\\{fileName}' ({dt.Rows.Count} righe)");

                Utilities.ExportDataTableToExcel(
                    dt,
                    targetFolder,
                    includeHeaders: true,
                    fileName: fileName
                );
            }
        }

        private static string BuildNestedFolderFromKey(string rootFolder, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return rootFolder;

            string current = rootFolder;

            string[] parts = key
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizePathSegment)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            foreach (string part in parts)
                current = Path.Combine(current, part);

            return current;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "EMPTY";

            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);

            foreach (char c in value.Trim())
            {
                if (invalidChars.Contains(c))
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            string result = sb.ToString().Trim();

            while (result.Contains("  "))
                result = result.Replace("  ", " ");

            result = result.TrimEnd('.', ' ');

            return string.IsNullOrWhiteSpace(result) ? "EMPTY" : result;
        }

        private static DataTable ToDataTable(DataTable sourceSchema, IEnumerable<DataRow> rows)
        {
            var dt = sourceSchema.Clone();

            foreach (var row in rows)
                dt.ImportRow(row);

            return dt;
        }

        private static bool HasRecuperoSomme(DataRow row)
        {
            string tipiPagamento = row["TipiPagamento"] == DBNull.Value
                ? ""
                : Convert.ToString(row["TipiPagamento"]) ?? "";

            return !string.IsNullOrWhiteSpace(tipiPagamento);
        }

        private static string GetTipoFondoGroup(DataRow row)
        {
            string tipoFondo = row["Tipo_fondo"] == DBNull.Value
                ? ""
                : (Convert.ToString(row["Tipo_fondo"]) ?? "").ToUpperInvariant();

            if (tipoFondo.Contains("PNRR"))
                return "PNRR";

            if (tipoFondo.Contains("DISCO"))
                return "DISCO";

            return "ALTRO";
        }

        private static bool HasPA(DataRow row)
        {
            if (row["Recupero_servizio_abitativo"] == DBNull.Value)
                return false;

            decimal valore = SafeToDecimal(row["Recupero_servizio_abitativo"]);
            return valore > 0;
        }

        private static string GetEnteGestioneGroup(DataRow row, string fondoGroup)
        {
            string codEnte = row["Cod_ente_gestione"] == DBNull.Value
                ? ""
                : (Convert.ToString(row["Cod_ente_gestione"]) ?? "").Trim();

            string codSede = row["Cod_sede_studi_gestione"] == DBNull.Value
                ? ""
                : (Convert.ToString(row["Cod_sede_studi_gestione"]) ?? "").Trim().ToUpperInvariant();

            bool isPnrr = string.Equals(fondoGroup, "PNRR", StringComparison.OrdinalIgnoreCase);

            // 0 -> solo cod_sede_studi = B
            if (codSede == "B")
                return "Roma_1";

            // 1 -> cod_ente = 04
            if (codEnte == "04")
                return isPnrr ? "Roma_3" : "Roma_2";

            // 2 -> cod_ente in ('03', '07', '10', '11')
            if (codEnte == "03" || codEnte == "07" || codEnte == "10" || codEnte == "11")
                return isPnrr ? "Roma_3" : "Roma_3";

            // 3 -> cod_ente = 01 e non sede B + cod_ente in ('02','06','08','09','12','13')
            if ((codEnte == "01" && codSede != "B")
                || codEnte == "02"
                || codEnte == "06"
                || codEnte == "08"
                || codEnte == "09"
                || codEnte == "12"
                || codEnte == "13")
                return "Cassino";

            // 4 -> cod_ente = 05
            if (codEnte == "05")
                return "Viterbo";

            return $"ALTRO_{codEnte}_{codSede}";
        }
        private static decimal SafeToDecimal(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;

            if (value is decimal dec)
                return dec;

            if (value is int i)
                return i;

            if (value is long l)
                return l;

            if (value is double d)
                return Convert.ToDecimal(d);

            if (value is float f)
                return Convert.ToDecimal(f);

            string s = Convert.ToString(value)?.Trim() ?? "";

            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv))
                return inv;

            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out var ita))
                return ita;

            return 0m;
        }

        private static string BuildEnteFilterSql(string selectedEnte)
        {
            return selectedEnte switch
            {
                "-1" => "",
                "0" => "AND app.Cod_sede_studi = 'B'",
                "1" => "AND app.Cod_ente = '04'",
                "2" => "AND app.Cod_ente IN ('03', '07', '10', '11')",
                "3" => @"AND (
                                (app.Cod_ente = '01' AND ISNULL(app.Cod_sede_studi, '') <> 'B')
                                OR app.Cod_ente IN ('02', '06', '08', '09', '12', '13')
                            )",
                "4" => "AND app.Cod_ente = '05'",
                _ => throw new ArgumentOutOfRangeException(nameof(selectedEnte), selectedEnte, "Codice ente non gestito.")
            };
        }

        private DataTable ExecuteQuery(string sql, params SqlParameter[] parameters)
        {
            var dt = new DataTable();
            Logger.LogInfo(30, "Executing SQL query.");

            using var cmd = new SqlCommand(sql, CONNECTION) { CommandTimeout = 90000000 };
            if (parameters != null && parameters.Length > 0)
                cmd.Parameters.AddRange(parameters);

            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
            return dt;
        }
    }
}