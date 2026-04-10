using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Path = System.IO.Path;


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
        private string GetQuery(string enteFilterSql)
        {
            return $@"
;WITH CodiciPagamentoBS AS (
    SELECT DISTINCT x.Cod_tipo_pagam
    FROM (
        SELECT dpn.Cod_tipo_pagam_new AS Cod_tipo_pagam
        FROM Decod_pagam_new dpn
        INNER JOIN Tipologie_pagam tp
            ON tp.Cod_tipo_pagam = dpn.Cod_tipo_pagam_new
        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
          AND tp.Visibile = 1
          AND dpn.Cod_tipo_pagam_new IS NOT NULL

        UNION

        SELECT dpn.Cod_tipo_pagam_old AS Cod_tipo_pagam
        FROM Decod_pagam_new dpn
        INNER JOIN Tipologie_pagam tp
            ON tp.Cod_tipo_pagam = dpn.Cod_tipo_pagam_new
        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS'
          AND tp.Visibile = 1
          AND dpn.Cod_tipo_pagam_old IS NOT NULL
    ) x
),
Domande AS (
    SELECT
        d.Anno_accademico,
        d.Tipo_bando,
        d.Num_domanda,
        d.Cod_fiscale
    FROM Domanda d
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'LZ'
      AND EXISTS (
          SELECT 1
          FROM vMotivazioni_blocco_pagamenti mbp
          WHERE mbp.Anno_accademico = d.Anno_accademico
            AND mbp.Num_domanda = d.Num_domanda
            AND mbp.Cod_tipologia_blocco = 'BCC'
      )
),
DomandeCF AS (
    SELECT DISTINCT
        d.Anno_accademico,
        d.Cod_fiscale
    FROM Domande d
),
PagamentiAgg AS (
    SELECT
        p.Anno_accademico,
        p.Num_domanda,
        SUM(p.Imp_pagato) AS ImportoPagato,
        STRING_AGG(p.Cod_tipo_pagam, ', ') AS TipiPagamento,
        STRING_AGG(p.Cod_mandato, '/') AS Mandati,
        STRING_AGG(CONVERT(varchar(20), p.Ese_finanziario), '/') AS Ese_finanziari
    FROM Pagamenti p
    INNER JOIN Domande d
        ON d.Anno_accademico = p.Anno_accademico
       AND d.Num_domanda = p.Num_domanda
    INNER JOIN CodiciPagamentoBS cp
        ON cp.Cod_tipo_pagam = p.Cod_tipo_pagam
    WHERE p.Anno_accademico = @AA
      AND p.Ritirato_azienda = 0
    GROUP BY
        p.Anno_accademico,
        p.Num_domanda
),
AssegnazioniBase AS (
    SELECT
        ap.Id_assegnazione_PA,
        ap.Anno_Accademico,
        ap.Cod_Fiscale,
        ap.Cod_Pensionato,
        ap.Cod_Stanza,
        ap.Data_Decorrenza,
        ap.Data_Fine_Assegnazione
    FROM Assegnazione_PA ap
    INNER JOIN DomandeCF dcf
        ON dcf.Anno_accademico = ap.Anno_Accademico
       AND dcf.Cod_fiscale = ap.Cod_Fiscale
    WHERE ap.Anno_Accademico = @AA
      AND ap.Cod_movimento = '01'
      AND ap.Ind_Assegnazione = 1
      AND ap.Status_Assegnazione = 0
      AND ap.Data_Accettazione IS NOT NULL
      AND ap.Data_Decorrenza IS NOT NULL
      AND ap.Data_Fine_Assegnazione IS NOT NULL
),
AssegnazioniRanked AS (
    SELECT
        ab.Id_assegnazione_PA,
        ab.Anno_Accademico,
        ab.Cod_Fiscale,
        ab.Cod_Pensionato,
        ab.Cod_Stanza,
        ab.Data_Decorrenza,
        ab.Data_Fine_Assegnazione,
        ROW_NUMBER() OVER (
            PARTITION BY ab.Anno_Accademico, ab.Cod_Fiscale
            ORDER BY ab.Id_assegnazione_PA DESC
        ) AS rn_last
    FROM AssegnazioniBase ab
),
AssegnazioniDettaglio AS (
    SELECT
        ap.Anno_Accademico,
        ap.Cod_Fiscale,
        ap.Cod_Pensionato,
        ap.Cod_Stanza,
        calc.Permanenza,
        CONVERT(decimal(18, 2),
            (ISNULL(cs.Importo, 0) / 30.4375) * calc.Permanenza
        ) AS Costo_posto_alloggio
    FROM AssegnazioniRanked ap
    CROSS APPLY (
        SELECT GiorniBase = DATEDIFF(DAY, ap.Data_Decorrenza, ap.Data_Fine_Assegnazione)
    ) gb
    CROSS APPLY (
        SELECT Permanenza =
            gb.GiorniBase +
            CASE
                WHEN ap.rn_last = 1
                 AND gb.GiorniBase <> 0
                THEN 1
                ELSE 0
            END
    ) calc
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
        STRING_AGG(CONVERT(varchar(50), r.Num_reversale), '/') AS Num_reversale
    FROM Reversali r
    INNER JOIN Domande d
        ON d.Anno_accademico = r.Anno_accademico
       AND d.Num_domanda = r.Num_domanda
    WHERE r.Anno_accademico = @AA
      AND r.Cod_reversale IN ('01', '03')
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

    CONVERT(money, si.importo_assegnato, 0) AS Imp_BS,
    p.TipiPagamento,
    CONVERT(money, p.ImportoPagato, 0) AS Liquidato,
    CONVERT(money, p.ImportoPagato, 0) AS Recupero_borsa_di_studio,
    CONVERT(money, si.importo_assegnato - ISNULL(p.ImportoPagato, 0), 0) AS Economia,

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
        private string ExportDataTableToExcel_Euro(
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

                // ✔ file principale
                ExportDataTableToExcel_Euro(kvp.Value, folder, true, fileName);

                // ✔ file allegato
                string allegatoFileName =
                    Path.GetFileNameWithoutExtension(fileName) + " - allegato.xlsx";

                ExportAllegato(kvp.Value, folder, allegatoFileName, kvp.Key, aa);
                // ✔ PDF trasparenza
                string pdfFileName =
                    Path.GetFileNameWithoutExtension(fileName) + " - trasparenza.pdf";

                ExportTrasparenzaPdf(kvp.Value, folder, pdfFileName, kvp.Key, aa);
            }
        }

        private string S(DataRow r, string col) => r[col] == DBNull.Value ? "" : r[col].ToString();

        private string NormalizeLongPath(string path)
        {
            if (path.StartsWith(@"\\?\"))
                return path;

            return @"\\?\" + Path.GetFullPath(path);
        }

        private decimal D(DataRow r, string col) => r[col] == DBNull.Value ? 0 : Convert.ToDecimal(r[col]);
        private string ExportAllegato(
            DataTable dataTable,
            string folderPath,
            string fileName,
            string key,
            string aa)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            Directory.CreateDirectory(folderPath);

            var colonne = GetAllegatoColumns();
            var headers = colonne.Select(c => c.Header).ToArray();

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Allegato");

                int row = 1;
                int totalColumns = headers.Length;

                string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";
                bool conRecupero = key.StartsWith("Con rec somme", StringComparison.OrdinalIgnoreCase);

                string titolo = conRecupero
                    ? $"Revoche con recupero somme - BS - {anno}"
                    : $"Revoche senza recupero somme - BS - {anno}";

                // =========================
                // PAGE SETUP
                // =========================
                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.PagesWide = 1;   // tutta la larghezza in una sola pagina
                ws.PageSetup.PagesTall = 0;   // altezza libera su più pagine se necessario
                ws.PageSetup.CenterHorizontally = true;
                ws.PageSetup.Margins.Left = 0.25;
                ws.PageSetup.Margins.Right = 0.25;
                ws.PageSetup.Margins.Top = 0.5;
                ws.PageSetup.Margins.Bottom = 0.5;

                // =========================
                // TITOLO
                // =========================
                ws.Cell(row, 1).Value = titolo;
                ws.Range(row, 1, row, totalColumns).Merge();

                ws.Range(row, 1, row, totalColumns).Style
                    .Font.SetBold()
                    .Font.SetFontSize(11)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

                ws.Row(row).Height = 20;
                row += 2;

                // =========================
                // HEADER
                // =========================
                for (int col = 0; col < headers.Length; col++)
                {
                    ws.Cell(row, col + 1).Value = headers[col];
                }

                var headerRange = ws.Range(row, 1, row, totalColumns);
                headerRange.Style
                    .Fill.SetBackgroundColor(XLColor.CornflowerBlue)
                    .Font.SetBold()
                    .Font.SetFontColor(XLColor.White)
                    .Font.SetFontSize(8)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                    .Alignment.SetWrapText(true);

                ws.Row(row).Height = 28;

                int headerRow = row;
                row++;

                int progressivo = 1;

                decimal totBeneficio = 0;
                decimal totPagato = 0;
                decimal totRecupero = 0;
                decimal totEconomia = 0;
                decimal totCostoAlloggio = 0;
                decimal totTrattenute = 0;
                decimal totRecuperoServizio = 0;

                // =========================
                // DATI
                // =========================
                foreach (DataRow r in dataTable.Rows)
                {
                    int col = 1;

                    ws.Cell(row, col++).Value = progressivo++;
                    ws.Cell(row, col++).Value = S(r, "Descrizione");
                    ws.Cell(row, col++).Value = S(r, "Cod_fiscale");
                    ws.Cell(row, col++).Value = S(r, "Num_domanda");
                    ws.Cell(row, col++).Value = S(r, "Codice_Studente");
                    ws.Cell(row, col++).Value = S(r, "Nome");
                    ws.Cell(row, col++).Value = S(r, "Cognome");
                    ws.Cell(row, col++).Value = S(r, "data_nascita");
                    ws.Cell(row, col++).Value = TrasformaTipiPagamento(S(r, "TipiPagamento"));
                    ws.Cell(row, col++).Value = S(r, "Mandati_pagamento");
                    ws.Cell(row, col++).Value = S(r, "Esercizio_finanziario_mandato");
                    ws.Cell(row, col++).Value = S(r, "Tipo_fondo");
                    ws.Cell(row, col++).Value = S(r, "Determina_conferimento");
                    ws.Cell(row, col++).Value = S(r, "num_impegno_primaRata");
                    ws.Cell(row, col++).Value = S(r, "Esercizio_prima_rata");
                    ws.Cell(row, col++).Value = S(r, "num_impegno_saldo");
                    ws.Cell(row, col++).Value = S(r, "esercizio_saldo");

                    decimal impBeneficio = D(r, "Imp_BS");
                    decimal impPagato = D(r, "Liquidato");
                    decimal recupero = D(r, "Recupero_borsa_di_studio");
                    decimal economia = D(r, "Economia");
                    decimal costoAlloggio = D(r, "Costo_posto_alloggio");
                    decimal trattenuta = D(r, "Trattenuta_applicata_I_rata");
                    decimal recuperoServizio = D(r, "Recupero_servizio_abitativo");

                    ws.Cell(row, col++).Value = impBeneficio;
                    ws.Cell(row, col++).Value = impPagato;
                    ws.Cell(row, col++).Value = economia;
                    ws.Cell(row, col++).Value = recupero;
                    ws.Cell(row, col++).Value = S(r, "Pensionato");
                    ws.Cell(row, col++).Value = S(r, "Permanenza");
                    ws.Cell(row, col++).Value = costoAlloggio;
                    ws.Cell(row, col++).Value = trattenuta;
                    ws.Cell(row, col++).Value = S(r, "Num_reversale");
                    ws.Cell(row, col++).Value = recuperoServizio;

                    totBeneficio += impBeneficio;
                    totPagato += impPagato;
                    totRecupero += recupero;
                    totEconomia += economia;
                    totCostoAlloggio += costoAlloggio;
                    totTrattenute += trattenuta;
                    totRecuperoServizio += recuperoServizio;

                    row++;
                }

                // =========================
                // TOTALE
                // =========================
                ws.Cell(row, 1).Value = "Totale:";
                ws.Cell(row, 18).Value = totBeneficio;
                ws.Cell(row, 19).Value = totPagato;
                ws.Cell(row, 20).Value = totEconomia;
                ws.Cell(row, 21).Value = totRecupero;
                ws.Cell(row, 24).Value = totCostoAlloggio;
                ws.Cell(row, 25).Value = totTrattenute;
                ws.Cell(row, 27).Value = totRecuperoServizio;

                ws.Range(row, 1, row, totalColumns).Style.Font.SetBold();
                ws.Range(row, 1, row, totalColumns).Style.Fill.SetBackgroundColor(XLColor.LightGray);

                // =========================
                // FORMATTAZIONE
                // =========================
                string euroFormat =
                    "_-[$€-it-IT]* #,##0.00_-;-[$€-it-IT]* #,##0.00_-;_-[$€-it-IT]* \"-\"??_-;_-@_-";

                foreach (int c in new[] { 18, 19, 20, 21, 24, 25, 27 })
                {
                    ws.Column(c).Style.NumberFormat.Format = euroFormat;
                }

                var usedRange = ws.Range(1, 1, row, totalColumns);
                usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                usedRange.Style.Font.FontSize = 8;

                // bordi tabella
                ws.Range(headerRow, 1, row, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(headerRow, 1, row, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                // Freeze
                ws.SheetView.FreezeRows(headerRow);

                // =========================
                // LARGHEZZE COMPATTE
                // =========================
                ws.Column(1).Width = 4;    // N°
                ws.Column(2).Width = 14;   // Università
                ws.Column(3).Width = 16;   // CF
                ws.Column(4).Width = 11;   // Num domanda
                ws.Column(5).Width = 11;   // Codice studente
                ws.Column(6).Width = 12;   // Nome
                ws.Column(7).Width = 14;   // Cognome
                ws.Column(8).Width = 10;   // Data nascita
                ws.Column(9).Width = 14;   // Tipo pagamento
                ws.Column(10).Width = 11;  // Mandato
                ws.Column(11).Width = 10;  // Esercizio fin
                ws.Column(12).Width = 9;   // Tipo fondo
                ws.Column(13).Width = 12;  // Determina
                ws.Column(14).Width = 10;  // Impegno I rata
                ws.Column(15).Width = 8;   // Anno imp I rata
                ws.Column(16).Width = 10;  // Impegno saldo
                ws.Column(17).Width = 8;   // Anno imp saldo
                ws.Column(18).Width = 11;  // Importo beneficio
                ws.Column(19).Width = 11;  // Importo pagato
                ws.Column(20).Width = 10;  // Economia
                ws.Column(21).Width = 11;  // Recupero borsa
                ws.Column(22).Width = 12;  // Pensionato
                ws.Column(23).Width = 8;   // Permanenza
                ws.Column(24).Width = 11;  // Costo alloggio
                ws.Column(25).Width = 10;  // Trattenuta
                ws.Column(26).Width = 11;  // Num reversale
                ws.Column(27).Width = 11;  // Recupero servizio

                // area di stampa
                ws.PageSetup.PrintAreas.Clear();
                ws.PageSetup.PrintAreas.Add(1, 1, row, totalColumns);

                wb.SaveAs(fullPath);
            }

            return fullPath;
        }

        private string TrasformaTipiPagamento(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var mapping = new Dictionary<string, string>
            {
                { "BSP0", "Prima Rata" },
                { "BSI0", "Integrazione Prima Rata" },
                { "01", "Prima Rata" },
                { "BSP1", "Prima Rata" }
                // aggiungi qui altri codici
            };

            foreach (var kv in mapping)
            {
                input = input.Replace(kv.Key, kv.Value);
            }

            return input;
        }

        private readonly HashSet<string> ColonneSensibili = new(StringComparer.OrdinalIgnoreCase)
            {
                "Cod_fiscale",
                "Nome",
                "Cognome",
                "data_nascita",
                "Codice_Studente"
            };
        private List<(string Header, string Column)> GetAllegatoColumns()
        {
            return new List<(string, string)>
            {
                ("N°", null),
                ("Università", "Descrizione"),
                ("Codice Fiscale", "Cod_fiscale"),
                ("Num domanda", "Num_domanda"),
                ("Codice studente", "Codice_Studente"),
                ("Nome", "Nome"),
                ("Cognome", "Cognome"),
                ("Data di nascita", "data_nascita"),
                ("Tipo Pagamento", "TipiPagamento"),
                ("Mandato", "Mandati_pagamento"),
                ("Esercizio finanziario", "Esercizio_finanziario_mandato"),
                ("Tipo fondo", "Tipo_fondo"),
                ("Determina", "Determina_conferimento"),
                ("Impegno I rata", "num_impegno_primaRata"),
                ("Anno impegno I rata", "Esercizio_prima_rata"),
                ("Impegno saldo", "num_impegno_saldo"),
                ("Anno impegno saldo", "esercizio_saldo"),
                ("Importo beneficio", "Imp_BS"),
                ("Importo pagato", "Liquidato"),
                ("Economia", "Economia"),
                ("Recupero borsa", "Recupero_borsa_di_studio"),
                ("Pensionato", "Pensionato"),
                ("Permanenza", "Permanenza"),
                ("Costo alloggio", "Costo_posto_alloggio"),
                ("Trattenuta", "Trattenuta_applicata_I_rata"),
                ("Num reversale", "Num_reversale"),
                ("Recupero servizio", "Recupero_servizio_abitativo")
            };
        }

        private void ExportTrasparenzaPdf(DataTable dataTable, string folderPath, string fileName, string key, string aa)
        {
            string fullPath = Path.Combine(folderPath, fileName);
            Directory.CreateDirectory(folderPath);

            string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";
            bool conRecupero = key.StartsWith("Con rec somme", StringComparison.OrdinalIgnoreCase);

            string titolo = conRecupero
                ? $"Revoche con recupero somme - BS - {anno}"
                : $"Revoche senza recupero somme - BS - {anno}";

            var colonne = GetAllegatoColumns()
                .Where(c => c.Column == null || !ColonneSensibili.Contains(c.Column))
                .ToList();

            if (colonne.Count == 0)
                throw new Exception("Nessuna colonna disponibile");

            // larghezze relative compatte per riempire bene la pagina orizzontale
            float[] widths = new float[]
            {
                1.0f, // N°
                2.6f, // Università
                1.8f, // Num domanda
                2.3f, // Tipo Pagamento
                1.7f, // Mandato
                1.4f, // Esercizio finanziario
                1.4f, // Tipo fondo
                2.0f, // Determina
                1.6f, // Impegno I rata
                1.3f, // Anno impegno I rata
                1.6f, // Impegno saldo
                1.3f, // Anno impegno saldo

                2.5f, // Importo beneficio
                2.5f, // Importo pagato
                2.2f, // Economia
                2.5f, // Recupero borsa

                2.0f, // Pensionato
                1.3f, // Permanenza

                2.5f, // Costo alloggio
                2.2f, // Trattenuta
                1.8f, // Num reversale
                2.7f  // Recupero servizio
            };

            using (var writer = new PdfWriter(fullPath))
            using (var pdf = new PdfDocument(writer))
            using (var document = new Document(pdf, PageSize.A4.Rotate()))
            {
                document.SetMargins(8, 8, 8, 8);

                PdfFont font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                PdfFont fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
                PdfFont fontMono = PdfFontFactory.CreateFont(StandardFonts.COURIER);

                document.SetFont(font);

                document.Add(new Paragraph(titolo)
                    .SetFont(fontBold)
                    .SetFontSize(11)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginTop(0)
                    .SetMarginBottom(6));

                var table = new Table(UnitValue.CreatePercentArray(widths))
                    .UseAllAvailableWidth()
                    .SetFixedLayout();
                var headerBlue = new DeviceRgb(100, 149, 237); // CornflowerBlue
                // HEADER
                foreach (var col in colonne)
                {
                    table.AddHeaderCell(
                        new Cell()
                            .Add(new Paragraph(col.Header)
                                .SetFont(fontBold)
                                .SetFontSize(6.5f)
                                .SetTextAlignment(TextAlignment.CENTER))
                            .SetBackgroundColor(headerBlue)
                            .SetFontColor(ColorConstants.WHITE)
                            .SetPadding(2)
                    );
                }

                int progressivo = 1;

                decimal totBeneficio = 0;
                decimal totPagato = 0;
                decimal totEconomia = 0;
                decimal totRecupero = 0;
                decimal totCosto = 0;
                decimal totTrattenute = 0;
                decimal totRecServizio = 0;

                foreach (DataRow r in dataTable.Rows)
                {
                    foreach (var col in colonne)
                    {
                        string text;
                        bool isContabilita = col.Column != null && ColonneContabilita.Contains(col.Column);

                        if (col.Column == null)
                        {
                            text = progressivo.ToString();
                        }
                        else if (!dataTable.Columns.Contains(col.Column) || r[col.Column] == DBNull.Value || r[col.Column] == null)
                        {
                            text = isContabilita ? "€ -" : "";
                        }
                        else
                        {
                            var val = r[col.Column];

                            if (col.Column.Equals("TipiPagamento", StringComparison.OrdinalIgnoreCase))
                            {
                                text = TrasformaTipiPagamento(val.ToString() ?? "");
                            }
                            else if (isContabilita)
                            {
                                text = FormatContabilitaPdf(Convert.ToDecimal(val));
                            }
                            else if (val is decimal dec)
                            {
                                text = dec.ToString("N2", ItCulture);
                            }
                            else
                            {
                                text = val.ToString() ?? "";
                            }
                        }

                        table.AddCell(
                            new Cell()
                                .Add(new Paragraph(text)
                                    .SetFont(isContabilita ? fontMono : font)
                                    .SetFontSize(isContabilita ? 5.8f : 6f)
                                    .SetMargin(0))
                                .SetPadding(isContabilita ? 1f : 1.5f)
                                .SetTextAlignment(isContabilita ? TextAlignment.RIGHT : TextAlignment.CENTER)
                                .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                        );
                    }

                    totBeneficio += D(r, "Imp_BS");
                    totPagato += D(r, "Liquidato");
                    totEconomia += D(r, "Economia");
                    totRecupero += D(r, "Recupero_borsa_di_studio");
                    totCosto += D(r, "Costo_posto_alloggio");
                    totTrattenute += D(r, "Trattenuta_applicata_I_rata");
                    totRecServizio += D(r, "Recupero_servizio_abitativo");

                    progressivo++;
                }

                // RIGA TOTALE
                foreach (var col in colonne)
                {
                    string text = "";
                    bool isContabilita = col.Column != null && ColonneContabilita.Contains(col.Column);

                    if (col.Column == null)
                        text = "Totale:";
                    else if (col.Column == "Imp_BS")
                        text = FormatContabilitaPdf(totBeneficio);
                    else if (col.Column == "Liquidato")
                        text = FormatContabilitaPdf(totPagato);
                    else if (col.Column == "Economia")
                        text = FormatContabilitaPdf(totEconomia);
                    else if (col.Column == "Recupero_borsa_di_studio")
                        text = FormatContabilitaPdf(totRecupero);
                    else if (col.Column == "Costo_posto_alloggio")
                        text = FormatContabilitaPdf(totCosto);
                    else if (col.Column == "Trattenuta_applicata_I_rata")
                        text = FormatContabilitaPdf(totTrattenute);
                    else if (col.Column == "Recupero_servizio_abitativo")
                        text = FormatContabilitaPdf(totRecServizio);

                    table.AddCell(
                        new Cell()
                            .Add(new Paragraph(text)
                                .SetFont(isContabilita ? fontMono : fontBold)
                                .SetFontSize(isContabilita ? 5.8f : 6.5f)
                                .SetMargin(0))
                            .SetBackgroundColor(ColorConstants.LIGHT_GRAY)
                            .SetPadding(isContabilita ? 1f : 2f)
                            .SetTextAlignment(isContabilita ? TextAlignment.RIGHT : TextAlignment.CENTER)
                            .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                    );
                }

                document.Add(table);

                document.Add(new Paragraph($"Generato il {DateTime.Now:dd/MM/yyyy HH:mm}")
                    .SetFont(font)
                    .SetFontSize(7)
                    .SetTextAlignment(TextAlignment.CENTER)
                    .SetMarginTop(5)
                    .SetMarginBottom(0));
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

        private string BuildFileName(string aa, string key)
        {
            string a = $"{aa.Substring(2, 2)}-{aa.Substring(6, 2)}";
            var p = key.Split('\\');

            return Sanitize(
                $"Revoche {FmtEnte(p[2])} a.a. {a} {FmtFondo(p[1])} {FmtRec(p[0])} {FmtPA(p[3])}"
            ) + ".xlsx";
        }

        private string FmtEnte(string e) => e switch
        {
            "Roma_1" => "Roma 1",
            "Roma_2" => "Roma 2",
            "Roma_3" => "Roma 3",
            "Roma_2_3" => "Roma 2 e Roma 3",
            _ => e
        };

        private string FmtFondo(string f) => f switch
        {
            "PNRR" => "Fondo PNRR",
            "DISCO" => "Fondo DiSCo",
            _ => f
        };

        private string FmtRec(string r) => r switch
        {
            "Con rec somme" => "con recupero somme",
            "Senza rec somme" => "senza recupero somme",
            _ => ""
        };
        private string FmtPA(string p) => p switch
        {
            "Con posto alloggio" => "e PA",
            "Senza posto alloggio" => "e senza PA",
            _ => ""
        };

        // =========================
        // HELPERS
        // =========================

        private static readonly HashSet<string> ColonneContabilita = new(StringComparer.OrdinalIgnoreCase)
{
    "Imp_BS",
    "Liquidato",
    "Economia",
    "Recupero_borsa_di_studio",
    "Costo_posto_alloggio",
    "Trattenuta_applicata_I_rata",
    "Recupero_servizio_abitativo"
};

        private static readonly CultureInfo ItCulture = CultureInfo.GetCultureInfo("it-IT");

        private string FormatContabilitaPdf(decimal value)
        {
            if (value == 0m)
                return "€ -";

            if (value < 0m)
                return "-€ " + Math.Abs(value).ToString("#,##0.00", ItCulture);

            return "€ " + value.ToString("#,##0.00", ItCulture);
        }
        private DataTable ToDataTable(DataTable schema, IEnumerable<DataRow> rows)
        {
            var dt = schema.Clone();
            foreach (var r in rows) dt.ImportRow(r);
            return dt;
        }

        private string BuildNested(string root, string key)
        {
            foreach (var p in key.Split('\\'))
                root = Path.Combine(root, Sanitize(p));
            return root;
        }

        private string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }

        private bool HasRecuperoSomme(DataRow r) =>
            !string.IsNullOrWhiteSpace(r["TipiPagamento"]?.ToString());

        private bool HasPA(DataRow r) =>
            r["Recupero_servizio_abitativo"] != DBNull.Value &&
            Convert.ToDecimal(r["Recupero_servizio_abitativo"]) > 0;

        private string GetTipoFondoGroup(DataRow r)
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

        private string GetEnteGestioneGroup(DataRow r, string fondo)
        {
            string ente = r["Cod_ente_gestione"]?.ToString() ?? "";
            string sede = r["Cod_sede_studi_gestione"]?.ToString()?.ToUpper() ?? "";

            if (sede == "B") return "Roma_1";
            if (ente == "04") return fondo == "PNRR" ? "Roma_3" : "Roma_2";
            if (new[] { "03", "07", "10", "11" }.Contains(ente)) return "Roma_3";
            if (ente == "05") return "Viterbo";

            return "Cassino";
        }

        private string BuildEnteFilterSql(string e) => e switch
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
            cmd.CommandTimeout = 999999;
            cmd.Parameters.AddRange(p);
            using var r = cmd.ExecuteReader();
            dt.Load(r);
            return dt;
        }
    }
}