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
    internal sealed class GeneratoreAllegatoDecadenza : GeneratoreAllegatoBase
    {
        public enum Modalita
        {
            SenzaRecuperoSomme,
            ConRecuperoSomme
        }

        private const string CodiceSenzaRecuperoSomme = "40";
        private const string CodiceConRecuperoSomme = "41";

        private readonly Modalita modalita;
        private readonly bool conRecuperoSomme;

        public GeneratoreAllegatoDecadenza(SqlConnection connection, Modalita modalita)
            : base(connection)
        {
            this.modalita = modalita;
            conRecuperoSomme = modalita == Modalita.ConRecuperoSomme;
        }

        public override string Codice => conRecuperoSomme
            ? CodiceConRecuperoSomme
            : CodiceSenzaRecuperoSomme;

        public override string Descrizione => conRecuperoSomme
            ? "Decadenza con recupero somme"
            : "Decadenza senza recupero somme";

        public override void Generate(AllegatoContext context)
        {
            List<string> benefici = ResolveBenefici(context.TipoBeneficio);

            DataTable input = ReadAndValidateInput(context.FileExcel);

            Logger.LogInfo(10, $"Righe modello Decadenza lette: {input.Rows.Count}");

            CreateInputTempTable(input);
            ValidateDomandePresenti(input, context);

            int progress = 30;

            foreach (string codBeneficio in benefici)
            {
                Logger.LogInfo(progress, $"Esecuzione query Decadenza {codBeneficio}...");

                DataTable result = ExecuteQuery(
                    GetQuery(codBeneficio),
                    new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico },
                    new SqlParameter("@CodBeneficio", SqlDbType.VarChar, 2) { Value = codBeneficio });

                ExportScarti(input, result, context, codBeneficio);

                if (result.Rows.Count == 0)
                {
                    Logger.LogWarning(
                        progress + 20,
                        $"Decadenza {codBeneficio}: nessuno studente valido. File allegato non creato.");

                    progress = Math.Min(progress + 20, 90);
                    continue;
                }

                if (conRecuperoSomme && IsBeneficioBorsa(codBeneficio))
                    ValidateRecuperiPresenti(result, context);

                string fileName = BuildPlaceholderFileName(context.AnnoAccademico, codBeneficio);
                string fullPath = ExportAllegato(result, context.SaveFolder, fileName, context.AnnoAccademico, codBeneficio);

                Logger.LogInfo(progress + 20, $"Creato allegato Decadenza {codBeneficio}: {fullPath}");
                progress = Math.Min(progress + 20, 90);
            }
        }

        private static List<string> ResolveBenefici(string beneficio)
        {
            if (string.IsNullOrWhiteSpace(beneficio))
                return new List<string> { "BS" };

            List<string> selected = beneficio
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(b => b.Trim().Trim('\'').ToUpperInvariant())
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selected.Count == 0)
                return new List<string> { "BS" };

            if (selected.Contains("00", StringComparer.OrdinalIgnoreCase))
                return new List<string> { "BS", "PA", "CI" };

            string[] supported = { "BS", "PA", "CI" };
            List<string> unsupported = selected
                .Where(b => !supported.Contains(b, StringComparer.OrdinalIgnoreCase))
                .OrderBy(b => b)
                .ToList();

            if (unsupported.Count > 0)
            {
                throw new ValidationException(
                    "La generazione Decadenza è predisposta per i benefici BS, PA e CI. Benefici non gestiti: " +
                    string.Join(", ", unsupported));
            }

            return selected;
        }

        private static bool IsBeneficioBorsa(string codBeneficio)
        {
            return codBeneficio.Equals("BS", StringComparison.OrdinalIgnoreCase);
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

        private static DataTable ReadAndValidateInput(string filePath)
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheets.First();

            var headerRow = ws.Row(1);
            int lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

            if (lastColumn == 0)
                throw new ValidationException("Il modello Excel non contiene intestazioni.");

            Dictionary<string, int> headers = new(StringComparer.OrdinalIgnoreCase);

            for (int col = 1; col <= lastColumn; col++)
            {
                string header = NormalizeHeader(headerRow.Cell(col).GetString());
                if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
                    headers.Add(header, col);
            }

            int cfCol = ResolveColumn(headers,
                "codicefiscale",
                "codfiscale",
                "cod_fiscale",
                "cf");

            int motivoCol = ResolveColumn(headers,
                "motivodecadenza",
                "motivazione",
                "motivo");

            var input = new DataTable("InputDecadenza");
            input.Columns.Add("Cod_fiscale", typeof(string));
            input.Columns.Add("Motivo_decadenza", typeof(string));

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            for (int row = 2; row <= lastRow; row++)
            {
                string cf = ws.Cell(row, cfCol).GetString().Trim().ToUpperInvariant();
                string motivo = ws.Cell(row, motivoCol).GetString().Trim();

                if (string.IsNullOrWhiteSpace(cf) && string.IsNullOrWhiteSpace(motivo))
                    continue;

                if (string.IsNullOrWhiteSpace(cf))
                    throw new ValidationException($"Codice fiscale mancante alla riga {row}.");

                if (cf.Length != 16)
                    throw new ValidationException($"Codice fiscale non valido alla riga {row}: '{cf}'.");

                if (string.IsNullOrWhiteSpace(motivo))
                    throw new ValidationException($"Motivo decadenza mancante alla riga {row}.");

                if (!seen.Add(cf))
                    throw new ValidationException($"Codice fiscale duplicato nel modello: {cf}.");

                input.Rows.Add(cf, motivo);
            }

            if (input.Rows.Count == 0)
                throw new ValidationException("Il modello Excel non contiene righe da elaborare.");

            return input;
        }

        private static string NormalizeHeader(string header)
        {
            return header
                .Trim()
                .Replace(" ", "")
                .Replace("_", "")
                .Replace("-", "")
                .ToLowerInvariant();
        }

        private static int ResolveColumn(Dictionary<string, int> headers, params string[] aliases)
        {
            foreach (string alias in aliases)
            {
                string key = NormalizeHeader(alias);
                if (headers.TryGetValue(key, out int col))
                    return col;
            }

            throw new ValidationException(
                $"Colonna obbligatoria non trovata. Colonne ammesse: {string.Join(", ", aliases)}.");
        }

        private void CreateInputTempTable(DataTable input)
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#InputDecadenza') IS NOT NULL
    DROP TABLE #InputDecadenza;

CREATE TABLE #InputDecadenza
(
    Cod_fiscale NVARCHAR(16) NOT NULL,
    Motivo_decadenza NVARCHAR(500) NOT NULL,
    CONSTRAINT PK_InputDecadenza PRIMARY KEY CLUSTERED (Cod_fiscale)
);";

            using (var cmd = new SqlCommand(sql, Connection))
            {
                cmd.CommandTimeout = 120;
                cmd.ExecuteNonQuery();
            }

            using var bulk = new SqlBulkCopy(Connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "#InputDecadenza",
                BulkCopyTimeout = 120,
                BatchSize = 5000
            };

            bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
            bulk.ColumnMappings.Add("Motivo_decadenza", "Motivo_decadenza");
            bulk.WriteToServer(input);

            using var statCmd = new SqlCommand("UPDATE STATISTICS #InputDecadenza;", Connection)
            {
                CommandTimeout = 120
            };
            statCmd.ExecuteNonQuery();
        }

        private void ValidateDomandePresenti(DataTable input, AllegatoContext context)
        {
            DataTable errori = ExecuteQuery(
                GetDomandeMancantiQuery(),
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico });

            if (errori.Rows.Count == 0)
                return;

            string fullPath = ExportSimpleTable(
                errori,
                context.SaveFolder,
                BuildSupportFileName("ERRORI_Decadenza_DomandeNonTrovate", context.AnnoAccademico, null));

            throw new ValidationException(
                "Elaborazione bloccata. Codici fiscali senza domanda valida per l'anno indicato. " +
                $"File errori creato: {fullPath}");
        }

        private void ExportScarti(DataTable input, DataTable result, AllegatoContext context, string codBeneficio)
        {
            HashSet<string> included = result.AsEnumerable()
                .Select(r => S(r, "Cod_fiscale").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> missing = input.AsEnumerable()
                .Select(r => r.Field<string>("Cod_fiscale") ?? "")
                .Where(cf => !included.Contains(cf))
                .OrderBy(cf => cf)
                .ToList();

            if (missing.Count == 0)
                return;

            DataTable scarti = ExecuteQuery(
                GetScartiQuery(codBeneficio),
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico },
                new SqlParameter("@CodBeneficio", SqlDbType.VarChar, 2) { Value = codBeneficio });

            DataTable filtered = scarti.Clone();
            HashSet<string> missingSet = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow row in scarti.Rows)
            {
                if (missingSet.Contains(S(row, "Cod_fiscale")))
                    filtered.ImportRow(row);
            }

            HashSet<string> filteredSet = filtered.AsEnumerable()
                .Select(r => S(r, "Cod_fiscale"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow row in input.Rows)
            {
                string codFiscale = row.Field<string>("Cod_fiscale") ?? string.Empty;
                if (!missingSet.Contains(codFiscale) || filteredSet.Contains(codFiscale))
                    continue;

                filtered.Rows.Add(
                    codFiscale,
                    string.Empty,
                    codBeneficio,
                    row.Field<string>("Motivo_decadenza") ?? string.Empty,
                    "Non restituito dalla query Decadenza");
            }

            if (filtered.Rows.Count == 0)
                return;

            string fullPath = ExportSimpleTable(
                filtered,
                context.SaveFolder,
                BuildSupportFileName("SCARTI_Decadenza", context.AnnoAccademico, codBeneficio));

            Logger.LogWarning(
                null,
                $"Decadenza {codBeneficio}: creato file scarti per {filtered.Rows.Count} studenti: {fullPath}");
        }

        private void ValidateRecuperiPresenti(DataTable result, AllegatoContext context)
        {
            DataTable errori = new("ErroriDecadenza");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Num_domanda", typeof(string));
            errori.Columns.Add("Motivo_decadenza", typeof(string));
            errori.Columns.Add("Errore", typeof(string));

            foreach (DataRow row in result.AsEnumerable()
                .Where(r => D(r, "Liquidato") <= 0m || (D(r, "Recupero_borsa_di_studio") <= 0m && D(r, "Recupero_servizio_abitativo") <= 0m))
                .OrderBy(r => S(r, "Cod_fiscale")))
            {
                errori.Rows.Add(
                    S(row, "Cod_fiscale"),
                    S(row, "Num_domanda"),
                    S(row, "Motivo_decadenza"),
                    D(row, "Liquidato") <= 0m
                        ? "Decadenza con recupero somme BS senza pagamenti."
                        : "Decadenza con recupero somme BS senza importi di recupero.");
            }

            if (errori.Rows.Count == 0)
                return;

            string fullPath = ExportSimpleTable(
                errori,
                context.SaveFolder,
                BuildSupportFileName("ERRORI_Decadenza_RecuperiBS", context.AnnoAccademico, "BS"));

            throw new ValidationException(
                "Elaborazione bloccata. Decadenza con recupero somme selezionata, ma alcuni studenti BS non hanno importi di recupero. " +
                $"File errori creato: {fullPath}");
        }

        private string BuildPlaceholderFileName(string aa, string codBeneficio)
        {
            string tipo = conRecuperoSomme
                ? "ConRecuperoSomme"
                : "SenzaRecuperoSomme";

            return Sanitize($"PLACEHOLDER_Decadenza_{tipo}_{codBeneficio}_{aa}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        private static string BuildSupportFileName(string prefix, string aa, string? codBeneficio)
        {
            string beneficio = string.IsNullOrWhiteSpace(codBeneficio)
                ? string.Empty
                : $"_{codBeneficio}";

            return Sanitize($"{prefix}{beneficio}_{aa}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        private string ExportAllegato(DataTable dataTable, string folderPath, string fileName, string aa, string codBeneficio)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            List<AllegatoColumn> columns = GetAllegatoColumns(dataTable);
            int totalColumns = columns.Count;

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Allegato");

            int row = 1;
            string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";
            string descrizioneBeneficio = GetDescrizioneBeneficio(codBeneficio);
            string titolo = conRecuperoSomme
                ? $"Decadenze con recupero somme - {descrizioneBeneficio} - {anno}"
                : $"Decadenze senza recupero somme - {descrizioneBeneficio} - {anno}";

            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PagesWide = 1;
            ws.PageSetup.PagesTall = 0;
            ws.PageSetup.CenterHorizontally = true;
            ws.PageSetup.Margins.Left = 0.25;
            ws.PageSetup.Margins.Right = 0.25;
            ws.PageSetup.Margins.Top = 0.5;
            ws.PageSetup.Margins.Bottom = 0.5;

            ws.Cell(row, 1).Value = titolo;
            ws.Range(row, 1, row, totalColumns).Merge();
            ws.Range(row, 1, row, totalColumns).Style
                .Font.SetBold()
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(row).Height = 20;
            row += 2;

            for (int col = 0; col < totalColumns; col++)
                ws.Cell(row, col + 1).Value = columns[col].Header;

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
            Dictionary<string, decimal> totals = EuroColumns.ToDictionary(c => c, _ => 0m, StringComparer.OrdinalIgnoreCase);

            foreach (DataRow dataRow in dataTable.AsEnumerable().OrderBy(r => S(r, "Cod_fiscale")))
            {
                int col = 1;

                foreach (AllegatoColumn column in columns)
                {
                    var cell = ws.Cell(row, col);

                    if (column.Column == null)
                    {
                        cell.Value = progressivo;
                    }
                    else if (column.Column.Equals("TipiPagamento", StringComparison.OrdinalIgnoreCase))
                    {
                        cell.Value = TrasformaTipiPagamento(S(dataRow, column.Column));
                    }
                    else if (EuroColumns.Contains(column.Column))
                    {
                        decimal value = D(dataRow, column.Column);
                        cell.Value = value;
                        totals[column.Column] += value;
                    }
                    else
                    {
                        cell.Value = S(dataRow, column.Column);
                    }

                    col++;
                }

                progressivo++;
                row++;
            }

            bool hasEuroColumns = columns.Any(c => c.Column != null && EuroColumns.Contains(c.Column));

            if (hasEuroColumns)
            {
                ws.Cell(row, 1).Value = "Totale:";

                for (int i = 0; i < columns.Count; i++)
                {
                    string? columnName = columns[i].Column;
                    if (columnName != null && totals.TryGetValue(columnName, out decimal total))
                        ws.Cell(row, i + 1).Value = total;
                }

                ws.Range(row, 1, row, totalColumns).Style.Font.SetBold();
                ws.Range(row, 1, row, totalColumns).Style.Fill.SetBackgroundColor(XLColor.LightGray);
            }
            else
            {
                row--;
            }

            string euroFormat =
                "_-[$€-it-IT]* #,##0.00_-;-[$€-it-IT]* #,##0.00_-;_-[$€-it-IT]* \"-\"??_-;_-@_-";

            for (int i = 0; i < columns.Count; i++)
            {
                string? columnName = columns[i].Column;
                if (columnName != null && EuroColumns.Contains(columnName))
                    ws.Column(i + 1).Style.NumberFormat.Format = euroFormat;
            }

            var usedRange = ws.Range(1, 1, row, totalColumns);
            usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            usedRange.Style.Font.FontSize = 8;

            ws.Range(headerRow, 1, row, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(headerRow, 1, row, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            ws.SheetView.FreezeRows(headerRow);
            ApplyColumnWidths(ws, columns);

            ws.PageSetup.PrintAreas.Clear();
            ws.PageSetup.PrintAreas.Add(1, 1, row, totalColumns);

            wb.SaveAs(fullPath);

            return fullPath;
        }

        private static string ExportSimpleTable(DataTable dataTable, string folderPath, string fileName)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Dettaglio");

            for (int col = 0; col < dataTable.Columns.Count; col++)
            {
                ws.Cell(1, col + 1).Value = dataTable.Columns[col].ColumnName;
                ws.Column(col + 1).Width = Math.Max(14, dataTable.Columns[col].ColumnName.Length + 2);
            }

            int row = 2;
            foreach (DataRow dataRow in dataTable.Rows)
            {
                for (int col = 0; col < dataTable.Columns.Count; col++)
                    ws.Cell(row, col + 1).Value = dataRow[col]?.ToString() ?? string.Empty;

                row++;
            }

            if (dataTable.Columns.Count > 0)
            {
                var headerRange = ws.Range(1, 1, 1, dataTable.Columns.Count);
                headerRange.Style
                    .Fill.SetBackgroundColor(XLColor.CornflowerBlue)
                    .Font.SetBold()
                    .Font.SetFontColor(XLColor.White)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                ws.Range(1, 1, Math.Max(1, row - 1), dataTable.Columns.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(1, 1, Math.Max(1, row - 1), dataTable.Columns.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                ws.SheetView.FreezeRows(1);
            }

            wb.SaveAs(fullPath);
            return fullPath;
        }

        private List<AllegatoColumn> GetAllegatoColumns(DataTable dataTable)
        {
            List<AllegatoColumn> columns = GetBaseAllegatoColumns();

            if (IsBeneficioSoloAnagrafico(dataTable))
                return GetAnagraficaAllegatoColumns();

            if (!conRecuperoSomme)
            {
                return columns
                    .Where(c => c.Column == null || !SenzaRecuperoHiddenColumns.Contains(c.Column))
                    .ToList();
            }

            if (!HasAnyPostoAlloggioData(dataTable))
            {
                columns = columns
                    .Where(c => c.Column == null || !PostoAlloggioColumns.Contains(c.Column))
                    .ToList();
            }

            return columns;
        }

        private static bool IsBeneficioSoloAnagrafico(DataTable dataTable)
        {
            return dataTable.Columns.Contains("Cod_beneficio")
                && dataTable.AsEnumerable().All(r =>
                {
                    string codBeneficio = S(r, "Cod_beneficio");
                    return codBeneficio.Equals("PA", StringComparison.OrdinalIgnoreCase)
                        || codBeneficio.Equals("CI", StringComparison.OrdinalIgnoreCase);
                });
        }

        private static List<AllegatoColumn> GetBaseAllegatoColumns()
        {
            return new List<AllegatoColumn>
            {
                new("N°", null),
                new("Università", "Descrizione"),
                new("Codice Fiscale", "Cod_fiscale"),
                new("Motivo decadenza", "Motivo_decadenza"),
                new("Num domanda", "Num_domanda"),
                new("Codice studente", "Codice_Studente"),
                new("Nome", "Nome"),
                new("Cognome", "Cognome"),
                new("Data di nascita", "data_nascita"),
                new("Tipo Pagamento", "TipiPagamento"),
                new("Mandato", "Mandati_pagamento"),
                new("Esercizio finanziario", "Esercizio_finanziario_mandato"),
                new("Tipo fondo", "Tipo_fondo"),
                new("Determina", "Determina_conferimento"),
                new("Impegno I rata", "num_impegno_primaRata"),
                new("Anno impegno I rata", "Esercizio_prima_rata"),
                new("Impegno saldo", "num_impegno_saldo"),
                new("Anno impegno saldo", "esercizio_saldo"),
                new("Importo beneficio", "Imp_BS"),
                new("Importo pagato", "Liquidato"),
                new("Economia", "Economia"),
                new("Recupero borsa", "Recupero_borsa_di_studio"),
                new("Pensionato", "Pensionato"),
                new("Permanenza", "Permanenza"),
                new("Costo alloggio", "Costo_posto_alloggio"),
                new("Trattenuta", "Trattenuta_applicata_I_rata"),
                new("Num reversale", "Num_reversale"),
                new("Recupero servizio", "Recupero_servizio_abitativo")
            };
        }

        private static List<AllegatoColumn> GetAnagraficaAllegatoColumns()
        {
            return new List<AllegatoColumn>
            {
                new("N°", null),
                new("Università", "Descrizione"),
                new("Codice Fiscale", "Cod_fiscale"),
                new("Motivo decadenza", "Motivo_decadenza"),
                new("Num domanda", "Num_domanda"),
                new("Codice studente", "Codice_Studente"),
                new("Nome", "Nome"),
                new("Cognome", "Cognome"),
                new("Data di nascita", "data_nascita")
            };
        }

        private static readonly HashSet<string> SenzaRecuperoHiddenColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "TipiPagamento",
            "Mandati_pagamento",
            "Esercizio_finanziario_mandato",
            "Liquidato",
            "Recupero_borsa_di_studio",
            "Pensionato",
            "Permanenza",
            "Costo_posto_alloggio",
            "Trattenuta_applicata_I_rata",
            "Num_reversale",
            "Recupero_servizio_abitativo"
        };

        private static readonly HashSet<string> PostoAlloggioColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pensionato",
            "Permanenza",
            "Costo_posto_alloggio",
            "Trattenuta_applicata_I_rata",
            "Num_reversale",
            "Recupero_servizio_abitativo"
        };

        private static readonly HashSet<string> EuroColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            "Imp_BS",
            "Liquidato",
            "Economia",
            "Recupero_borsa_di_studio",
            "Costo_posto_alloggio",
            "Trattenuta_applicata_I_rata",
            "Recupero_servizio_abitativo"
        };

        private static bool HasAnyPostoAlloggioData(DataTable dataTable)
        {
            foreach (DataRow row in dataTable.Rows)
            {
                foreach (string column in PostoAlloggioColumns)
                {
                    if (!dataTable.Columns.Contains(column))
                        continue;

                    if (!IsEmptyValue(row[column]))
                        return true;
                }
            }

            return false;
        }

        private static bool IsEmptyValue(object? value)
        {
            if (value == null || value == DBNull.Value)
                return true;

            if (value is string text)
                return string.IsNullOrWhiteSpace(text);

            if (value is decimal dec)
                return dec == 0m;

            if (value is int integer)
                return integer == 0;

            if (value is long longValue)
                return longValue == 0L;

            if (value is double doubleValue)
                return Math.Abs(doubleValue) < double.Epsilon;

            if (value is float floatValue)
                return Math.Abs(floatValue) < float.Epsilon;

            return false;
        }

        private static void ApplyColumnWidths(IXLWorksheet ws, List<AllegatoColumn> columns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                double width = GetColumnWidth(columns[i]);
                ws.Column(i + 1).Width = width;
            }
        }

        private static double GetColumnWidth(AllegatoColumn column)
        {
            if (column.Column == null)
                return 4;

            return column.Column switch
            {
                "Descrizione" => 14,
                "Cod_fiscale" => 16,
                "Motivo_decadenza" => 24,
                "Num_domanda" => 11,
                "Codice_Studente" => 11,
                "Nome" => 12,
                "Cognome" => 14,
                "data_nascita" => 10,
                "TipiPagamento" => 14,
                "Mandati_pagamento" => 11,
                "Esercizio_finanziario_mandato" => 10,
                "Tipo_fondo" => 9,
                "Determina_conferimento" => 12,
                "num_impegno_primaRata" => 10,
                "Esercizio_prima_rata" => 8,
                "num_impegno_saldo" => 10,
                "esercizio_saldo" => 8,
                "Imp_BS" => 11,
                "Liquidato" => 11,
                "Economia" => 10,
                "Recupero_borsa_di_studio" => 11,
                "Pensionato" => 12,
                "Permanenza" => 8,
                "Costo_posto_alloggio" => 11,
                "Trattenuta_applicata_I_rata" => 10,
                "Num_reversale" => 11,
                "Recupero_servizio_abitativo" => 11,
                _ => 12
            };
        }

        private static string GetQuery(string codBeneficio)
        {
            return IsBeneficioBorsa(codBeneficio)
                ? GetDecadenzaQuery()
                : GetDecadenzaAnagraficaQuery();
        }

        private static string GetDomandeMancantiQuery()
        {
            return @"
SELECT
    i.Cod_fiscale,
    i.Motivo_decadenza,
    'Domanda non trovata per anno accademico e tipo bando LZ' AS Errore
FROM #InputDecadenza i
WHERE NOT EXISTS (
    SELECT 1
    FROM Domanda d
    WHERE d.Cod_fiscale = i.Cod_fiscale
      AND d.Anno_accademico = @AA
      AND d.Tipo_bando = 'LZ'
)
ORDER BY i.Cod_fiscale;";
        }

        private static string GetScartiQuery(string codBeneficio)
        {
            string esitoExists = codBeneficio.ToUpperInvariant() switch
            {
                "BS" => @"EXISTS (
        SELECT 1
        FROM vEsiti_concorsiBS e
        WHERE e.Anno_accademico = d.Anno_accademico
          AND e.Num_domanda = d.Num_domanda
    )",
                "PA" => @"EXISTS (
        SELECT 1
        FROM vEsiti_concorsiPA e
        WHERE e.Anno_accademico = d.Anno_accademico
          AND e.Num_domanda = d.Num_domanda
    )",
                "CI" => @"EXISTS (
        SELECT 1
        FROM vEsiti_concorsiCI e
        WHERE e.Anno_accademico = d.Anno_accademico
          AND e.Num_domanda = d.Num_domanda
    )",
                _ => "1 = 0"
            };

            return $@"
;WITH Domande AS (
    SELECT
        i.Cod_fiscale,
        i.Motivo_decadenza,
        d.Anno_accademico,
        d.Num_domanda
    FROM #InputDecadenza i
    INNER JOIN Domanda d
        ON d.Cod_fiscale = i.Cod_fiscale
       AND d.Anno_accademico = @AA
       AND d.Tipo_bando = 'LZ'
)
SELECT
    d.Cod_fiscale,
    d.Num_domanda,
    @CodBeneficio AS Cod_beneficio,
    d.Motivo_decadenza,
    CASE
        WHEN NOT EXISTS (
            SELECT 1
            FROM Benefici_richiesti br
            WHERE br.Anno_accademico = d.Anno_accademico
              AND br.Num_domanda = d.Num_domanda
              AND br.Cod_beneficio = @CodBeneficio
              AND br.Data_fine_validita IS NULL
        ) THEN 'Beneficio richiesto attivo non presente'
        WHEN NOT ({esitoExists}) THEN 'Esito beneficio non presente'
        ELSE 'Non restituito dalla query Decadenza'
    END AS Motivo_scarto
FROM Domande d
WHERE NOT (
    EXISTS (
        SELECT 1
        FROM Benefici_richiesti br
        WHERE br.Anno_accademico = d.Anno_accademico
          AND br.Num_domanda = d.Num_domanda
          AND br.Cod_beneficio = @CodBeneficio
          AND br.Data_fine_validita IS NULL
    )
    AND {esitoExists}
)
ORDER BY d.Cod_fiscale;";
        }

        private static string GetDecadenzaQuery()
        {
            return @"
;WITH CodiciPagamentoBS AS (
    SELECT DISTINCT x.Cod_tipo_pagam
    FROM (
        SELECT dpn.Cod_tipo_pagam_new AS Cod_tipo_pagam
        FROM Decod_pagam_new dpn
        INNER JOIN Tipologie_pagam tp
            ON tp.Cod_tipo_pagam = dpn.Cod_tipo_pagam_new
        WHERE LEFT(tp.Cod_tipo_pagam, 2) = @CodBeneficio
          AND tp.Visibile = 1
          AND dpn.Cod_tipo_pagam_new IS NOT NULL

        UNION

        SELECT dpn.Cod_tipo_pagam_old AS Cod_tipo_pagam
        FROM Decod_pagam_new dpn
        INNER JOIN Tipologie_pagam tp
            ON tp.Cod_tipo_pagam = dpn.Cod_tipo_pagam_new
        WHERE LEFT(tp.Cod_tipo_pagam, 2) = @CodBeneficio
          AND tp.Visibile = 1
          AND dpn.Cod_tipo_pagam_old IS NOT NULL
    ) x
),
Domande AS (
    SELECT
        d.Anno_accademico,
        d.Tipo_bando,
        d.Num_domanda,
        d.Cod_fiscale,
        i.Motivo_decadenza
    FROM #InputDecadenza i
    INNER JOIN Domanda d
        ON d.Cod_fiscale = i.Cod_fiscale
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'LZ'

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
        STRING_AGG(p.Cod_tipo_pagam, '#') AS TipiPagamento,
        STRING_AGG(p.Cod_mandato, '#') AS Mandati,
        STRING_AGG(CONVERT(varchar(20), p.Ese_finanziario), '#') AS Ese_finanziari
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
    d.Motivo_decadenza,
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
    CONVERT(money, ISNULL(p.ImportoPagato, 0), 0) AS Liquidato,
    CONVERT(money, ISNULL(p.ImportoPagato, 0), 0) AS Recupero_borsa_di_studio,
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
JOIN Tipologie_studi ts
    ON ts.Cod_tipologia_studi = isc.Cod_tipologia_studi
JOIN vValori_calcolati vc
    ON vc.Anno_accademico = d.Anno_accademico
   AND vc.Num_domanda = d.Num_domanda
JOIN vDATIGENERALI_dom dg
    ON dg.Anno_accademico = d.Anno_accademico
   AND dg.Num_domanda = d.Num_domanda
JOIN vResidenza res
    ON res.ANNO_ACCADEMICO = d.Anno_accademico
   AND res.COD_FISCALE = d.Cod_fiscale
JOIN Specifiche_impegni si
    ON si.Anno_accademico = d.Anno_accademico
   AND si.Num_domanda = d.Num_domanda
   AND si.Cod_beneficio = 'BS'  and si.Data_validita =  (SELECT        MAX(Data_validita) AS Expr1
                               FROM            dbo.Specifiche_impegni AS ap
                               WHERE        (Anno_accademico = si.Anno_accademico) AND (Cod_fiscale = si.Cod_fiscale) and Cod_beneficio = 'bs' )
JOIN Benefici_richiesti br
    ON br.Anno_accademico = d.Anno_accademico
   AND br.Num_domanda = d.Num_domanda
   AND br.Cod_beneficio = @CodBeneficio
   AND br.Data_fine_validita IS NULL
JOIN vEsiti_concorsiBS bs
    ON bs.Anno_accademico = d.Anno_accademico
   AND bs.Num_domanda = d.Num_domanda
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
ORDER BY
    d.Cod_fiscale;";
        }

        private static string GetDecadenzaAnagraficaQuery()
        {
            return @"
;WITH Domande AS (
    SELECT
        d.Anno_accademico,
        d.Tipo_bando,
        d.Num_domanda,
        d.Cod_fiscale,
        i.Motivo_decadenza
    FROM #InputDecadenza i
    INNER JOIN Domanda d
        ON d.Cod_fiscale = i.Cod_fiscale
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'LZ'
)
SELECT
    d.Anno_accademico,
    @CodBeneficio AS Cod_beneficio,
    ss.Descrizione,
    d.Cod_fiscale,
    d.Motivo_decadenza,
    d.Num_domanda,
    st.Codice_Studente,
    st.Nome,
    st.Cognome,
    CONVERT(char(12), st.Data_nascita, 103) AS data_nascita
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
JOIN Benefici_richiesti br
    ON br.Anno_accademico = d.Anno_accademico
   AND br.Num_domanda = d.Num_domanda
   AND br.Cod_beneficio = @CodBeneficio
   AND br.Data_fine_validita IS NULL
WHERE
    (
        @CodBeneficio = 'PA'
        AND EXISTS (
            SELECT 1
            FROM vEsiti_concorsiPA pa
            WHERE pa.Anno_accademico = d.Anno_accademico
              AND pa.Num_domanda = d.Num_domanda
        )
    )
    OR
    (
        @CodBeneficio = 'CI'
        AND EXISTS (
            SELECT 1
            FROM vEsiti_concorsiCI ci
            WHERE ci.Anno_accademico = d.Anno_accademico
              AND ci.Num_domanda = d.Num_domanda
        )
    )
ORDER BY
    d.Cod_fiscale;";
        }

        private static string TrasformaTipiPagamento(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "BSP0", "Prima Rata" },
                { "BSI0", "Integrazione Prima Rata" },
                { "BSS0", "Saldo" },
                { "BSS1", "Saldo" },
                { "BSS2", "Saldo" },
                { "01", "Prima Rata" },
                { "BSP1", "Prima Rata" }
            };

            List<string> translated = new();

            foreach (string code in input.Split('#', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                translated.Add(mapping.TryGetValue(code, out string? value)
                    ? value
                    : "Altro");
            }

            return string.Join("#", translated
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetTipoPagamentoOrder)
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase));
        }

        private static int GetTipoPagamentoOrder(string tipoPagamento)
        {
            return tipoPagamento switch
            {
                "Prima Rata" => 0,
                "Integrazione Prima Rata" => 1,
                "Saldo" => 2,
                "Altro" => 3,
                _ => 4
            };
        }
    }
}
