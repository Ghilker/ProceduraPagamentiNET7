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
    internal sealed class GeneratoreAllegatoRiammissioneIdoneo : GeneratoreAllegatoBase
    {
        public GeneratoreAllegatoRiammissioneIdoneo(SqlConnection connection)
            : base(connection)
        {
        }

        public override string Codice => "02";

        public override string Descrizione => "Riammissione come idoneo";

        public override void Generate(AllegatoContext context)
        {
            DataTable input = ReadAndValidateInput(context.FileExcel);
            List<string> benefici = ResolveBenefici(context.TipoBeneficio);

            Logger.LogInfo(10, $"Righe modello Riammissione come idoneo lette: {input.Rows.Count}");

            CreateInputTempTable(input);
            ValidateDomandePresenti(input, context);

            int progress = 30;
            foreach (string codBeneficio in benefici)
            {
                DataTable result = ExecuteQuery(
                    GetQuery(),
                    new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico },
                    new SqlParameter("@CodBeneficio", SqlDbType.VarChar, 2) { Value = codBeneficio });

                if (result.Rows.Count == 0)
                {
                    Logger.LogWarning(
                        progress,
                        $"Riammissione come idoneo {codBeneficio}: nessuno studente con beneficio richiesto attivo. File non creato.");
                    progress = Math.Min(progress + 20, 90);
                    continue;
                }

                string fileName = Sanitize(
                    $"PLACEHOLDER_RiammissioneComeIdoneo_{codBeneficio}_{context.AnnoAccademico}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
                string fullPath = ExportAllegato(
                    result,
                    context.SaveFolder,
                    fileName,
                    context.AnnoAccademico,
                    codBeneficio);

                Logger.LogInfo(
                    progress,
                    $"Creato allegato Riammissione come idoneo {codBeneficio}: {fullPath}");
                progress = Math.Min(progress + 20, 90);
            }
        }

        private static DataTable ReadAndValidateInput(string filePath)
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
            int motivoCol = ResolveColumn(
                headers,
                "motivo",
                "motivazione",
                "motivazioneriammissione",
                "motivoriammissione");

            DataTable input = new("InputRiammissioneIdoneo");
            input.Columns.Add("Cod_fiscale", typeof(string));
            input.Columns.Add("Motivo", typeof(string));

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

                string motivo = ws.Cell(row, motivoCol).GetString().Trim();
                if (string.IsNullOrWhiteSpace(motivo))
                    throw new ValidationException($"Motivo non valorizzato alla riga {row} per il codice fiscale {cf}.");

                input.Rows.Add(cf, motivo);
            }

            if (input.Rows.Count == 0)
                throw new ValidationException("Il modello Excel non contiene righe da elaborare.");

            return input;
        }

        private static string NormalizeHeader(string header)
        {
            return header.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        }

        private static int ResolveColumn(Dictionary<string, int> headers, params string[] aliases)
        {
            foreach (string alias in aliases)
            {
                if (headers.TryGetValue(NormalizeHeader(alias), out int column))
                    return column;
            }

            throw new ValidationException(
                $"Colonna obbligatoria non trovata. Colonne ammesse: {string.Join(", ", aliases)}.");
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
                    "La Riammissione come idoneo gestisce i benefici BS, PA e CI. Benefici non gestiti: " +
                    string.Join(", ", unsupported));
            }

            return selected;
        }

        private void CreateInputTempTable(DataTable input)
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#InputRiammissioneIdoneo') IS NOT NULL
    DROP TABLE #InputRiammissioneIdoneo;

CREATE TABLE #InputRiammissioneIdoneo
(
    Cod_fiscale NVARCHAR(16) NOT NULL,
    Motivo NVARCHAR(MAX) NOT NULL,
    CONSTRAINT PK_InputRiammissioneIdoneo PRIMARY KEY CLUSTERED (Cod_fiscale)
);";

            using (var cmd = new SqlCommand(sql, Connection) { CommandTimeout = 120 })
                cmd.ExecuteNonQuery();

            using var bulk = new SqlBulkCopy(Connection)
            {
                DestinationTableName = "#InputRiammissioneIdoneo",
                BulkCopyTimeout = 120
            };
            bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
            bulk.ColumnMappings.Add("Motivo", "Motivo");
            bulk.WriteToServer(input);
        }

        private void ValidateDomandePresenti(DataTable input, AllegatoContext context)
        {
            DataTable presenti = ExecuteQuery(
                @"SELECT DISTINCT i.Cod_fiscale
FROM #InputRiammissioneIdoneo i
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

            DataTable errori = new("Errori");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Errore", typeof(string));
            foreach (string cf in mancanti)
                errori.Rows.Add(cf, "Domanda LZ non trovata per l'anno accademico selezionato.");

            string fileName = Sanitize(
                $"ERRORI_RiammissioneComeIdoneo_{context.AnnoAccademico}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
            string fullPath = ExportSimpleTable(errori, context.SaveFolder, fileName);

            throw new ValidationException(
                $"Elaborazione bloccata. Alcune domande non sono state trovate. File errori creato: {fullPath}");
        }

        private static string ExportAllegato(
            DataTable data,
            string folderPath,
            string fileName,
            string aa,
            string codBeneficio)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Allegato");
            string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";

            ws.Cell(1, 1).Value =
                $"Riammissione come idoneo - {GetDescrizioneBeneficio(codBeneficio)} - {anno}";
            ws.Range(1, 1, 1, 6).Merge();
            ws.Range(1, 1, 1, 6).Style
                .Font.SetBold()
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            string[] headers = { "N", "CF", "COGNOME", "NOME", "NUM_DOMANDA", "MOTIVO" };
            for (int col = 0; col < headers.Length; col++)
                ws.Cell(3, col + 1).Value = headers[col];

            ws.Range(3, 1, 3, headers.Length).Style
                .Fill.SetBackgroundColor(XLColor.CornflowerBlue)
                .Font.SetBold()
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            int row = 4;
            int numero = 1;
            foreach (DataRow dataRow in data.AsEnumerable().OrderBy(r => S(r, "Cod_fiscale")))
            {
                ws.Cell(row, 1).Value = numero++;
                ws.Cell(row, 2).Value = S(dataRow, "Cod_fiscale");
                ws.Cell(row, 3).Value = S(dataRow, "Cognome");
                ws.Cell(row, 4).Value = S(dataRow, "Nome");
                ws.Cell(row, 5).Value = S(dataRow, "Num_domanda");
                ws.Cell(row, 6).Value = S(dataRow, "Motivo");
                row++;
            }

            ws.Range(3, 1, row - 1, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(3, 1, row - 1, 6).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Column(1).Width = 5;
            ws.Column(2).Width = 19;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 18;
            ws.Column(5).Width = 15;
            ws.Column(6).Width = 45;
            ws.Column(6).Style.Alignment.WrapText = true;
            ws.SheetView.FreezeRows(3);

            wb.SaveAs(fullPath);
            return fullPath;
        }

        private static string ExportSimpleTable(DataTable data, string folderPath, string fileName)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Errori");
            ws.Cell(1, 1).InsertTable(data);
            ws.Columns().AdjustToContents();
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

        private static string GetQuery()
        {
            return @"
SELECT DISTINCT
    d.Cod_fiscale,
    st.Cognome,
    st.Nome,
    d.Num_domanda,
    i.Motivo
FROM #InputRiammissioneIdoneo i
INNER JOIN Domanda d
    ON d.Cod_fiscale = i.Cod_fiscale
INNER JOIN Studente st
    ON st.Cod_fiscale = d.Cod_fiscale
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
