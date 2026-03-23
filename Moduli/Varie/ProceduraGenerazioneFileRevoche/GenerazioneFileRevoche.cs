using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace ProcedureNet7
{
    internal class GenerazioneFileRevoche : BaseProcedure<ArgsGenerazioneFileRevoche>
    {
        public GenerazioneFileRevoche(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection) { }

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
            string query = GetQuery(enteFilterSql);

            Logger.LogInfo(30, "Esecuzione query...");

            DataTable dt = ExecuteQuery(
                query,
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = selectedAA }
            );

            TabelleSeparate = SplitTablesCascade(dt);

            Logger.LogInfo(30, $"Tabelle separate: {TabelleSeparate.Count}");

            ExportSplitTablesToFolders(TabelleSeparate, selectedSaveFolder, selectedAA);

            Logger.LogInfo(100, "Fine lavorazione.");
        }

        // =========================
        // QUERY COMPLETA
        // =========================
        private static string GetQuery(string enteFilterSql)
        {
            return $@"
                    ;WITH Domande AS (
                        SELECT 
                            d.Anno_accademico,
                            d.Tipo_bando,
                            d.Num_domanda,
                            d.Cod_fiscale
                        FROM Domanda d
                        WHERE d.Anno_accademico = @AA
                          AND d.Tipo_bando = 'LZ'
                    ),

                    PagamentiAgg AS (
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
                                p.cod_tipo_pagam IN (
                                    SELECT dpn.Cod_tipo_pagam_new
                                    FROM Decod_pagam_new dpn
                                    INNER JOIN Tipologie_pagam tp 
                                        ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
                                      AND tp.visibile = 1)
                             OR p.cod_tipo_pagam IN (
                                    SELECT dpn.Cod_tipo_pagam_old
                                    FROM Decod_pagam_new dpn
                                    INNER JOIN Tipologie_pagam tp 
                                        ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
                                      AND tp.visibile = 1))
                        GROUP BY 
                            p.Anno_accademico,
                            p.Num_domanda
                    ),

                    AssegnazioniRanked AS (
                        SELECT 
                            ap.Anno_Accademico,
                            ap.Cod_Fiscale,
                            ap.Cod_Pensionato,
                            ap.Cod_Stanza,
                            ap.Data_Decorrenza,
                            ap.Data_Fine_Assegnazione,
                            ROW_NUMBER() OVER (
                                PARTITION BY ap.Anno_Accademico, ap.Cod_Fiscale
                                ORDER BY 
                                    ap.Data_Fine_Assegnazione DESC,
                                    ap.Data_Decorrenza DESC,
                                    ap.Cod_Pensionato DESC,
                                    ap.Cod_Stanza DESC) AS rn_last
                        FROM Assegnazione_PA ap
                        WHERE ap.Anno_Accademico = @AA
                          AND ap.Cod_movimento = '01'
                          AND ap.Ind_Assegnazione = 1
                          AND ap.Status_Assegnazione = 0
                          AND ap.Data_Accettazione IS NOT NULL
                          AND ap.Data_Decorrenza IS NOT NULL
                          AND ap.Data_Fine_Assegnazione IS NOT NULL
                    ),

                    AssegnazioniDettaglio AS (
                        SELECT 
                            ap.Anno_Accademico,
                            ap.Cod_Fiscale,
                            ap.Cod_Pensionato,
                            ap.Cod_Stanza,
                            DATEDIFF(DAY, ap.Data_Decorrenza, ap.Data_Fine_Assegnazione)
                                + CASE WHEN ap.rn_last = 1 THEN 1 ELSE 0 END AS Permanenza,
                            CONVERT(decimal(18, 2),
                                (ISNULL(cs.Importo, 0) / 30.4375) *
                                (DATEDIFF(DAY, ap.Data_Decorrenza, ap.Data_Fine_Assegnazione)+ CASE WHEN ap.rn_last = 1 THEN 1 ELSE 0 END)) AS Costo_posto_alloggio
                        FROM AssegnazioniRanked ap
                        LEFT JOIN vStanza vzCosto 
                            ON vzCosto.Cod_Pensionato = ap.Cod_Pensionato
                           AND vzCosto.Cod_Stanza = ap.Cod_Stanza
                        LEFT JOIN Costo_Servizio cs 
                            ON cs.Anno_accademico = ap.Anno_Accademico
                           AND cs.Cod_pensionato = ap.Cod_Pensionato
                           AND cs.Cod_periodo = 'M'
                           AND cs.Tipo_stanza = vzCosto.Tipo_Costo_Stanza
                    ),

                    AssegnPAAgg AS (
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
                    ),

                    ReversaliAgg AS (
                        SELECT
                            r.Anno_accademico,
                            r.Num_domanda,
                            SUM(r.Importo) AS Importo,
                            STRING_AGG(r.num_reversale, '/') AS Num_reversale
                        FROM Reversali r
                        WHERE r.Cod_reversale in ('01','03')
                        GROUP BY
                            r.Anno_accademico,
                            r.Num_domanda
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

                        r.Num_reversale,
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

                    LEFT JOIN ReversaliAgg r 
                        ON r.Anno_accademico = d.Anno_accademico
                       AND r.Num_domanda = d.Num_domanda

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
                        d.Cod_fiscale;
                    ";


        }

        // =========================
        // SPLIT
        // =========================
        private Dictionary<string, DataTable> SplitTablesCascade(DataTable source)
        {
            var result = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

            foreach (var gRec in source.AsEnumerable().GroupBy(HasRecuperoSomme))
            {
                string recKey = gRec.Key ? "Con rec somme" : "Senza rec somme";

                foreach (var gFondo in gRec.GroupBy(GetTipoFondoGroup))
                {
                    bool merge = selectedEnte == "1" && gFondo.Key == "PNRR";

                    if (gFondo.Key == "SCARTA" || !gFondo.Any())
                        continue;

                    if (merge)
                    {
                        SplitByPA(result, source, gFondo,
                            $"{recKey}\\{gFondo.Key}\\Roma_2_3");
                    }
                    else
                    {
                        foreach (var gEnte in gFondo.GroupBy(r => GetEnteGestioneGroup(r, gFondo.Key)))
                        {
                            SplitByPA(result, source, gEnte,
                                $"{recKey}\\{gFondo.Key}\\{gEnte.Key}");
                        }
                    }
                }
            }

            return result;
        }

        private void SplitByPA(Dictionary<string, DataTable> result,
            DataTable schema,
            IEnumerable<DataRow> rows,
            string baseKey)
        {
            foreach (var gPa in rows.GroupBy(HasPA))
            {
                string key = $"{baseKey}\\{(gPa.Key ? "Con posto alloggio" : "Senza posto alloggio")}";
                result[key] = ToDataTable(schema, gPa);
            }
        }

        // =========================
        // EXPORT
        // =========================
        private static string ExportDataTableToExcel_Euro(
    DataTable dataTable,
    string folderPath,
    bool includeHeaders = true,
    string fileName = "")
        {
            if (dataTable == null || dataTable.Rows.Count == 0)
                return string.Empty;

            fileName = string.IsNullOrWhiteSpace(fileName)
                ? $"Export_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
                : fileName;

            string fullPath = Path.Combine(folderPath, fileName);
            Directory.CreateDirectory(folderPath);

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Sheet1");

                int currentRow = 1;
                int totalColumns = dataTable.Columns.Count;

                // Header
                if (includeHeaders)
                {
                    for (int col = 0; col < totalColumns; col++)
                    {
                        worksheet.Cell(currentRow, col + 1).Value =
                            dataTable.Columns[col].ColumnName;
                    }
                    currentRow++;
                }

                // colonne da formattare in euro
                string[] colonneEuro =
                {
            "Imp_BS",
            "Liquidato",
            "Recupero_borsa_di_studio",
            "Economia",
            "Costo_posto_alloggio",
            "Recupero_servizio_abitativo",
            "Trattenuta_applicata_I_rata"
        };

                // Populate righe
                foreach (DataRow row in dataTable.Rows)
                {
                    for (int col = 0; col < totalColumns; col++)
                    {
                        var cell = worksheet.Cell(currentRow, col + 1);
                        var column = dataTable.Columns[col];
                        var value = row[col];

                        if (value == DBNull.Value)
                        {
                            cell.Value = "";
                            continue;
                        }

                        // SE NUMERICO → scrivi numero vero (NON stringa)
                        if (column.DataType == typeof(decimal) ||
                            column.DataType == typeof(double) ||
                            column.DataType == typeof(float) ||
                            column.DataType == typeof(int))
                        {
                            cell.Value = Convert.ToDecimal(value);

                            // Applica formato EURO solo su alcune colonne
                            if (colonneEuro.Contains(column.ColumnName))
                            {
                                cell.Style.NumberFormat.Format =
                                    "_-[$€-it-IT]* #,##0.00_-;-[$€-it-IT]* #,##0.00_-;_-[$€-it-IT]* \"-\"??_-;_-@_-";
                            }
                        }
                        else
                        {
                            cell.Value = value.ToString();
                        }
                    }

                    currentRow++;
                }

                worksheet.Columns().AdjustToContents();

                workbook.SaveAs(fullPath);
            }

            return fullPath;
        }
        private void ExportSplitTablesToFolders(Dictionary<string, DataTable> tables, string root, string aa)
        {
            string exportRoot = Path.Combine(root, Sanitize(BuildRootFolderName(aa)));
            Directory.CreateDirectory(exportRoot);

            foreach (var kvp in tables)
            {
                if (kvp.Value.Rows.Count == 0) continue;

                string folder = BuildNested(exportRoot, kvp.Key);
                Directory.CreateDirectory(folder);

                string fileName = BuildFileName(aa, kvp.Key);

                ExportDataTableToExcel_Euro(kvp.Value, folder, true, fileName);
            }
        }

        // =========================
        // NAMING
        // =========================
        private string BuildRootFolderName(string aa)
        {
            string a = $"{aa.Substring(2, 2)}-{aa.Substring(6, 2)}";

            string ente = selectedEnte switch
            {
                "-1" => "TUTTI",
                "0" => "RM1",
                "1" => "RM3 e RM2",
                "2" => "Cassino",
                "3" => "Viterbo",
                _ => "ALTRO"
            };

            return $"Revoche {a} {ente}";
        }

        private static string BuildFileName(string aa, string key)
        {
            string a = $"{aa.Substring(2, 2)}-{aa.Substring(6, 2)}";
            var p = key.Split('\\');

            return Sanitize(
                $"Revoche {FmtEnte(p[2])} a.a. {a} {FmtFondo(p[1])} {FmtRec(p[0])} {FmtPA(p[3])}"
            ) + ".xlsx";
        }

        private static string FmtEnte(string e) => e switch
        {
            "Roma_1" => "Roma 1",
            "Roma_2" => "Roma 2",
            "Roma_3" => "Roma 3",
            "Roma_2_3" => "Roma 2 e Roma 3",
            _ => e
        };

        private static string FmtFondo(string f) => f switch
        {
            "PNRR" => "Fondo PNRR",
            "DISCO" => "Fondo DiSCo",
            _ => f
        };

        private static string FmtRec(string r) => r switch
        {
            "Con rec somme" => "con recupero somme",
            "Senza rec somme" => "senza recupero somme",
            _ => ""
        };
        private static string FmtPA(string p) => p switch
        {
            "Con posto alloggio" => "e PA",
            "Senza posto alloggio" => "e senza PA",
            _ => ""
        };

        // =========================
        // HELPERS
        // =========================
        private static DataTable ToDataTable(DataTable schema, IEnumerable<DataRow> rows)
        {
            var dt = schema.Clone();
            foreach (var r in rows) dt.ImportRow(r);
            return dt;
        }

        private static string BuildNested(string root, string key)
        {
            foreach (var p in key.Split('\\'))
                root = Path.Combine(root, Sanitize(p));
            return root;
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }

        private static bool HasRecuperoSomme(DataRow r) =>
            !string.IsNullOrWhiteSpace(r["TipiPagamento"]?.ToString());

        private static bool HasPA(DataRow r) =>
            r["Recupero_servizio_abitativo"] != DBNull.Value &&
            Convert.ToDecimal(r["Recupero_servizio_abitativo"]) > 0;

        private static string GetTipoFondoGroup(DataRow r)
        {
            if (r["Tipo_fondo"] == DBNull.Value)
                return "SCARTA";

            string f = r["Tipo_fondo"].ToString().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(f))
                return "SCARTA";

            if (f.Contains("PNRR")) return "PNRR";
            if (f.Contains("DISCO")) return "DISCO";

            return "ALTRO";
        }

        private static string GetEnteGestioneGroup(DataRow r, string fondo)
        {
            string ente = r["Cod_ente_gestione"]?.ToString() ?? "";
            string sede = r["Cod_sede_studi_gestione"]?.ToString()?.ToUpper() ?? "";

            if (sede == "B") return "Roma_1";
            if (ente == "04") return fondo == "PNRR" ? "Roma_3" : "Roma_2";
            if (new[] { "03", "07", "10", "11" }.Contains(ente)) return "Roma_3";
            if (ente == "05") return "Viterbo";

            return "Cassino";
        }

        private static string BuildEnteFilterSql(string e) => e switch
        {
            "-1" => "",
            "0" => "AND app.Cod_sede_studi = 'B'",
            "1" => "AND (app.Cod_ente = '04' OR app.Cod_ente IN ('03','07','10','11'))",
            "2" => "AND app.Cod_ente IN ('01','02','06','08','09','12','13')",
            "3" => "AND app.Cod_ente = '05'",
            _ => ""
        };

        private DataTable ExecuteQuery(string sql, params SqlParameter[] p)
        {
            var dt = new DataTable();
            using var cmd = new SqlCommand(sql, CONNECTION);
            cmd.Parameters.AddRange(p);
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }
    }
}