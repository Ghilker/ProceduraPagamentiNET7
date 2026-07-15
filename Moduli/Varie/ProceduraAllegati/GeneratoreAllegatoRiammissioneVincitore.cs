using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Path = System.IO.Path;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal sealed class GeneratoreAllegatoRiammissioneVincitore : GeneratoreAllegatoBase
    {
        internal enum Modalita
        {
            RiammissioneVincitore,
            IdoneoAVincitore
        }

        private readonly Modalita modalita;

        public GeneratoreAllegatoRiammissioneVincitore(
            SqlConnection connection,
            Modalita modalita = Modalita.RiammissioneVincitore)
            : base(connection)
        {
            this.modalita = modalita;
        }

        public override string Codice => modalita == Modalita.IdoneoAVincitore ? "09" : "01";

        public override string Descrizione =>
            modalita == Modalita.IdoneoAVincitore ? "Da idoneo a vincitore" : "Riammissione come vincitore";

        private string OperationFileToken =>
            modalita == Modalita.IdoneoAVincitore ? "IdoneoAVincitore" : "RiammissioneComeVincitore";

        public override void Generate(AllegatoContext context)
        {
            List<string> benefici = ResolveBenefici(context.TipoBeneficio);
            DataTable input = ReadAndValidateInput(context.FileExcel, benefici.Contains("BS"), Descrizione);

            Logger.LogInfo(10, $"Righe modello {Descrizione} lette: {input.Rows.Count}");

            CreateInputTempTable(input);
            ValidateDomandePresenti(input, context);

            int progress = 30;
            foreach (string codBeneficio in benefici)
            {
                bool isBorsa = codBeneficio.Equals("BS", StringComparison.OrdinalIgnoreCase);
                DataTable result = ExecuteQuery(
                    isBorsa ? GetQueryBorsa() : GetQueryAltroBeneficio(),
                    new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico },
                    new SqlParameter("@CodBeneficio", SqlDbType.VarChar, 2) { Value = codBeneficio });

                if (result.Rows.Count == 0)
                {
                    Logger.LogWarning(
                        progress,
                        $"{Descrizione} {codBeneficio}: nessuno studente valido. File non creato.");
                    progress = Math.Min(progress + 20, 90);
                    continue;
                }

                if (isBorsa)
                    ValidateDatiBorsa(result, context);

                string fileName = Sanitize(
                    $"PLACEHOLDER_{OperationFileToken}_{codBeneficio}_{context.AnnoAccademico}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
                string fullPath = ExportAllegato(
                    result,
                    context.SaveFolder,
                    fileName,
                    context.AnnoAccademico,
                    codBeneficio,
                    isBorsa);

                Logger.LogInfo(progress, $"Creato allegato {Descrizione} {codBeneficio}: {fullPath}");
                progress = Math.Min(progress + 20, 90);
            }
        }

        private static DataTable ReadAndValidateInput(
            string filePath,
            bool requireImpegniColumns,
            string descrizione)
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheets.First();
            int lastColumn = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;

            if (lastColumn == 0)
                throw new ValidationException("Il modello Excel non contiene intestazioni.");

            Dictionary<string, int> headers = new(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= lastColumn; col++)
            {
                string header = NormalizeHeader(ws.Cell(1, col).GetString());
                if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
                    headers.Add(header, col);
            }

            int cfCol = ResolveColumn(headers, "codicefiscale", "codfiscale", "cod_fiscale", "cf");
            int motivazioneCol = ResolveColumn(
                headers,
                "motivazioneriammissione",
                "motivazionemodifica",
                "motivazione",
                "motivo");
            int? primaRataCol = TryResolveColumn(
                headers,
                "numeroimpegnoprimarata",
                "impegnoprimarata",
                "numimpegnoprimarata");
            int? saldoCol = TryResolveColumn(
                headers,
                "numeroimpegnosaldo",
                "impegnosaldo",
                "numimpegnosaldo");

            if (requireImpegniColumns && (!primaRataCol.HasValue || !saldoCol.HasValue))
            {
                throw new ValidationException(
                    $"Per {descrizione} BS sono obbligatorie le colonne " +
                    "'Numero Impegno prima rata' e 'Numero Impegno saldo'.");
            }

            DataTable input = new("InputRiammissioneVincitore");
            input.Columns.Add("Cod_fiscale", typeof(string));
            input.Columns.Add("ImpegnoPrimaRata", typeof(string));
            input.Columns.Add("ImpegnoSaldo", typeof(string));
            input.Columns.Add("Motivazione", typeof(string));

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (int row = 2; row <= lastRow; row++)
            {
                string cf = ws.Cell(row, cfCol).GetString().Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(cf))
                    continue;

                if (cf.Length != 16)
                    throw new ValidationException($"Codice fiscale non valido alla riga {row}: '{cf}'.");
                if (!seen.Add(cf))
                    throw new ValidationException($"Codice fiscale duplicato nel modello: {cf}.");

                string motivazione = ws.Cell(row, motivazioneCol).GetString().Trim();
                if (string.IsNullOrWhiteSpace(motivazione))
                    throw new ValidationException($"Motivazione non valorizzata alla riga {row} per {cf}.");

                input.Rows.Add(
                    cf,
                    primaRataCol.HasValue ? ws.Cell(row, primaRataCol.Value).GetString().Trim() : string.Empty,
                    saldoCol.HasValue ? ws.Cell(row, saldoCol.Value).GetString().Trim() : string.Empty,
                    motivazione);
            }

            if (input.Rows.Count == 0)
                throw new ValidationException("Il modello Excel non contiene righe da elaborare.");

            return input;
        }

        private static string NormalizeHeader(string value)
        {
            return value.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        }

        private static int ResolveColumn(Dictionary<string, int> headers, params string[] aliases)
        {
            return TryResolveColumn(headers, aliases)
                ?? throw new ValidationException(
                    $"Colonna obbligatoria non trovata. Colonne ammesse: {string.Join(", ", aliases)}.");
        }

        private static int? TryResolveColumn(Dictionary<string, int> headers, params string[] aliases)
        {
            foreach (string alias in aliases)
            {
                if (headers.TryGetValue(NormalizeHeader(alias), out int column))
                    return column;
            }

            return null;
        }

        private static List<string> ResolveBenefici(string beneficio)
        {
            List<string> selected = (beneficio ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(b => b.Trim().Trim('\'').ToUpperInvariant())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selected.Count == 0)
                selected.Add("BS");
            if (selected.Contains("00", StringComparer.OrdinalIgnoreCase))
                return new List<string> { "BS", "PA", "CI" };

            string[] supported = { "BS", "PA", "CI" };
            List<string> unsupported = selected
                .Where(b => !supported.Contains(b, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unsupported.Count > 0)
            {
                throw new ValidationException(
                    "La procedura gestisce i benefici BS, PA e CI. Benefici non gestiti: " +
                    string.Join(", ", unsupported));
            }

            return selected;
        }

        private void CreateInputTempTable(DataTable input)
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#InputRiammissioneVincitore') IS NOT NULL
    DROP TABLE #InputRiammissioneVincitore;
CREATE TABLE #InputRiammissioneVincitore
(
    Cod_fiscale NVARCHAR(16) NOT NULL PRIMARY KEY,
    ImpegnoPrimaRata NVARCHAR(100) NULL,
    ImpegnoSaldo NVARCHAR(100) NULL,
    Motivazione NVARCHAR(MAX) NOT NULL
);";

            using (var cmd = new SqlCommand(sql, Connection) { CommandTimeout = 120 })
                cmd.ExecuteNonQuery();

            using var bulk = new SqlBulkCopy(Connection)
            {
                DestinationTableName = "#InputRiammissioneVincitore",
                BulkCopyTimeout = 120
            };
            foreach (DataColumn column in input.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            bulk.WriteToServer(input);
        }

        private void ValidateDomandePresenti(DataTable input, AllegatoContext context)
        {
            DataTable presenti = ExecuteQuery(
                @"SELECT DISTINCT i.Cod_fiscale
FROM #InputRiammissioneVincitore i
INNER JOIN Domanda d ON d.Cod_fiscale = i.Cod_fiscale
WHERE d.Anno_accademico = @AA AND d.Tipo_bando = 'LZ';",
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico });

            HashSet<string> trovati = presenti.AsEnumerable()
                .Select(r => S(r, "Cod_fiscale"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<string> mancanti = input.AsEnumerable()
                .Select(r => S(r, "Cod_fiscale"))
                .Where(cf => !trovati.Contains(cf))
                .ToList();

            if (mancanti.Count == 0)
                return;

            DataTable errori = CreateErrorTable();
            foreach (string cf in mancanti)
                errori.Rows.Add(cf, "Domanda LZ non trovata per l'anno accademico selezionato.");

            string fullPath = ExportErrors(errori, context, "Domande");
            throw new ValidationException(
                $"Elaborazione bloccata. Alcune domande non sono state trovate. File errori creato: {fullPath}");
        }

        private void ValidateDatiBorsa(DataTable result, AllegatoContext context)
        {
            DataTable errori = CreateErrorTable();
            foreach (DataRow row in result.Rows)
            {
                string errore = string.Empty;
                if (string.IsNullOrWhiteSpace(S(row, "ImpegnoPrimaRataInput")))
                    errore = "Impegno prima rata obbligatorio per BS.";
                else if (string.IsNullOrWhiteSpace(S(row, "ImpegnoSaldoInput")))
                    errore = "Impegno saldo obbligatorio per BS.";
                else if (string.IsNullOrWhiteSpace(S(row, "ImpegnoPrimaRata")))
                    errore = "Impegno prima rata non trovato nella tabella Impegni.";
                else if (string.IsNullOrWhiteSpace(S(row, "ImpegnoSaldo")))
                    errore = "Impegno saldo non trovato nella tabella Impegni.";
                else if (row["ImportoBorsa"] == DBNull.Value)
                    errore = "Importo borsa non presente in vEsiti_concorsiBS.";

                if (!string.IsNullOrWhiteSpace(errore))
                    errori.Rows.Add(S(row, "Cod_fiscale"), errore);
            }

            if (errori.Rows.Count == 0)
                return;

            string fullPath = ExportErrors(errori, context, "DatiBS");
            throw new ValidationException(
                $"Elaborazione bloccata. Dati BS incompleti o non validi. File errori creato: {fullPath}");
        }

        private static DataTable CreateErrorTable()
        {
            DataTable errori = new("Errori");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Errore", typeof(string));
            return errori;
        }

        private string ExportErrors(DataTable data, AllegatoContext context, string suffix)
        {
            string fileName = Sanitize(
                $"ERRORI_{OperationFileToken}_{suffix}_{context.AnnoAccademico}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            string fullPath = NormalizeLongPath(Path.Combine(context.SaveFolder, fileName));
            System.IO.Directory.CreateDirectory(context.SaveFolder);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Errori");
            ws.Cell(1, 1).InsertTable(data);
            ws.Columns().AdjustToContents();
            wb.SaveAs(fullPath);
            return fullPath;
        }

        private string ExportAllegato(
            DataTable data,
            string folderPath,
            string fileName,
            string aa,
            string codBeneficio,
            bool isBorsa)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Allegato");

            List<string> headers = new() { "N", "CF", "COGNOME", "NOME", "NUM_DOMANDA" };
            if (isBorsa)
            {
                headers.Add("IMPORTO BORSA ASSEGNATO");
                headers.Add("IMPEGNO PRIMA RATA");
                headers.Add("IMPEGNO SALDO");
            }
            headers.Add("MOTIVAZIONE");

            string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";
            ws.Cell(1, 1).Value =
                $"{Descrizione} - {GetDescrizioneBeneficio(codBeneficio)} - {anno}";
            ws.Range(1, 1, 1, headers.Count).Merge();
            ws.Range(1, 1, 1, headers.Count).Style
                .Font.SetBold()
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            for (int col = 0; col < headers.Count; col++)
                ws.Cell(3, col + 1).Value = headers[col];
            ws.Range(3, 1, 3, headers.Count).Style
                .Fill.SetBackgroundColor(XLColor.CornflowerBlue)
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            int row = 4;
            int numero = 1;
            decimal totale = 0m;
            foreach (DataRow dataRow in data.AsEnumerable().OrderBy(r => S(r, "Cod_fiscale")))
            {
                int col = 1;
                ws.Cell(row, col++).Value = numero++;
                ws.Cell(row, col++).Value = S(dataRow, "Cod_fiscale");
                ws.Cell(row, col++).Value = S(dataRow, "Cognome");
                ws.Cell(row, col++).Value = S(dataRow, "Nome");
                ws.Cell(row, col++).Value = S(dataRow, "Num_domanda");

                if (isBorsa)
                {
                    decimal importo = D(dataRow, "ImportoBorsa");
                    ws.Cell(row, col++).Value = importo;
                    ws.Cell(row, col++).Value = S(dataRow, "ImpegnoPrimaRata");
                    ws.Cell(row, col++).Value = S(dataRow, "ImpegnoSaldo");
                    totale += importo;
                }

                ws.Cell(row, col).Value = S(dataRow, "Motivazione");
                row++;
            }

            int lastDataRow = row - 1;
            if (isBorsa)
            {
                ws.Cell(row, 5).Value = "TOTALE";
                ws.Cell(row, 6).Value = totale;
                ws.Range(row, 5, row, headers.Count).Style.Font.SetBold();
                ws.Column(6).Style.NumberFormat.Format = "#,##0.00 [$€-it-IT]";
                lastDataRow = row;
            }

            ws.Range(3, 1, lastDataRow, headers.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(3, 1, lastDataRow, headers.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Columns(1, headers.Count).AdjustToContents();
            ws.Column(headers.Count).Width = Math.Max(ws.Column(headers.Count).Width, 40);
            ws.Column(headers.Count).Style.Alignment.WrapText = true;
            ws.SheetView.FreezeRows(3);
            wb.SaveAs(fullPath);
            return fullPath;
        }

        private static string GetDescrizioneBeneficio(string codBeneficio)
        {
            return codBeneficio.ToUpperInvariant() switch
            {
                "BS" => "Borsa di studio",
                "PA" => "Posto alloggio",
                "CI" => "Contributo integrativo",
                _ => codBeneficio
            };
        }

        private static string GetQueryBorsa()
        {
            return @"
SELECT DISTINCT
    d.Cod_fiscale,
    st.Cognome,
    st.Nome,
    d.Num_domanda,
    i.Motivazione,
    i.ImpegnoPrimaRata AS ImpegnoPrimaRataInput,
    i.ImpegnoSaldo AS ImpegnoSaldoInput,
    CONVERT(money, bs.Imp_beneficio, 0) AS ImportoBorsa,
    CONVERT(NVARCHAR(100), impPrima.num_impegno) AS ImpegnoPrimaRata,
    CONVERT(NVARCHAR(100), impSaldo.num_impegno) AS ImpegnoSaldo
FROM #InputRiammissioneVincitore i
INNER JOIN Domanda d ON d.Cod_fiscale = i.Cod_fiscale
INNER JOIN Studente st ON st.Cod_fiscale = d.Cod_fiscale
INNER JOIN Benefici_richiesti br
    ON br.Anno_accademico = d.Anno_accademico
   AND br.Num_domanda = d.Num_domanda
   AND br.Cod_beneficio = 'BS'
   AND br.Data_fine_validita IS NULL
LEFT JOIN vEsiti_concorsiBS bs
    ON bs.Anno_accademico = d.Anno_accademico
   AND bs.Num_domanda = d.Num_domanda
OUTER APPLY (
    SELECT TOP 1 im.num_impegno
    FROM Impegni im
    WHERE im.anno_accademico = d.Anno_accademico
      AND (
          LTRIM(RTRIM(CONVERT(NVARCHAR(100), im.num_impegno))) = LTRIM(RTRIM(i.ImpegnoPrimaRata))
          OR LTRIM(RTRIM(im.descr)) = LTRIM(RTRIM(i.ImpegnoPrimaRata))
      )
    ORDER BY CASE WHEN LTRIM(RTRIM(CONVERT(NVARCHAR(100), im.num_impegno))) = LTRIM(RTRIM(i.ImpegnoPrimaRata)) THEN 0 ELSE 1 END
) impPrima
OUTER APPLY (
    SELECT TOP 1 im.num_impegno
    FROM Impegni im
    WHERE im.anno_accademico = d.Anno_accademico
      AND (
          LTRIM(RTRIM(CONVERT(NVARCHAR(100), im.num_impegno))) = LTRIM(RTRIM(i.ImpegnoSaldo))
          OR LTRIM(RTRIM(im.descr)) = LTRIM(RTRIM(i.ImpegnoSaldo))
      )
    ORDER BY CASE WHEN LTRIM(RTRIM(CONVERT(NVARCHAR(100), im.num_impegno))) = LTRIM(RTRIM(i.ImpegnoSaldo)) THEN 0 ELSE 1 END
) impSaldo
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'LZ'
ORDER BY d.Cod_fiscale;";
        }

        private static string GetQueryAltroBeneficio()
        {
            return @"
SELECT DISTINCT
    d.Cod_fiscale,
    st.Cognome,
    st.Nome,
    d.Num_domanda,
    i.Motivazione
FROM #InputRiammissioneVincitore i
INNER JOIN Domanda d ON d.Cod_fiscale = i.Cod_fiscale
INNER JOIN Studente st ON st.Cod_fiscale = d.Cod_fiscale
INNER JOIN Benefici_richiesti br
    ON br.Anno_accademico = d.Anno_accademico
   AND br.Num_domanda = d.Num_domanda
   AND br.Cod_beneficio = @CodBeneficio
   AND br.Data_fine_validita IS NULL
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'LZ'
ORDER BY d.Cod_fiscale;";
        }
    }
}
