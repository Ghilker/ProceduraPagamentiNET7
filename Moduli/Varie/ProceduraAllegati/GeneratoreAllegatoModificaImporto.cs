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
    internal sealed class GeneratoreAllegatoModificaImporto : GeneratoreAllegatoBase
    {
        internal enum Modalita
        {
            ModificaImporto,
            CambioStatusSede
        }

        private readonly Modalita modalita;

        public GeneratoreAllegatoModificaImporto(
            SqlConnection connection,
            Modalita modalita = Modalita.ModificaImporto)
            : base(connection)
        {
            this.modalita = modalita;
        }

        public override string Codice => modalita == Modalita.CambioStatusSede ? "13" : "05";

        public override string Descrizione =>
            modalita == Modalita.CambioStatusSede ? "Cambio status sede" : "Modifica importo";

        private bool IncludeStatusSede => modalita == Modalita.CambioStatusSede;

        public override void Generate(AllegatoContext context)
        {
            DataTable input = ReadAndValidateInput(context.FileExcel);

            Logger.LogInfo(10, $"Righe modello {Descrizione} lette: {input.Rows.Count}");

            CreateInputTempTable(input);

            Logger.LogInfo(30, $"Esecuzione query {Descrizione} BS...");

            DataTable result = ExecuteQuery(
                GetModificaImportoQuery(),
                new SqlParameter("@AA", SqlDbType.Char, 8) { Value = context.AnnoAccademico });

            ValidateQueryResult(input, result, context);
            ValidateStatusSede(result, context);
            ValidateAccountingData(result, context);
            DataTable allegato = FilterUnchangedAmounts(result, context);

            var gruppiPerFondo = allegato.AsEnumerable()
                .GroupBy(
                    r => S(r, "TipoFondo").Trim().ToUpperInvariant(),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var gruppo in gruppiPerFondo)
            {
                DataTable allegatoFondo = allegato.Clone();
                foreach (DataRow row in gruppo)
                    allegatoFondo.ImportRow(row);

                string fileName = BuildPlaceholderFileName(context.AnnoAccademico, gruppo.Key);
                string fullPath = ExportAllegato(
                    allegatoFondo,
                    context.SaveFolder,
                    fileName,
                    context.AnnoAccademico,
                    gruppo.Key);

                Logger.LogInfo(
                    null,
                    $"Creato allegato {Descrizione} BS per il fondo '{gruppo.Key}': {fullPath}");
            }

            Logger.LogInfo(100, $"Creati tutti gli allegati {Descrizione} BS suddivisi per tipo fondo.");
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

            int? impegnoPrimaRataCol = TryResolveColumn(headers,
                "numeroimpegnoprimarata",
                "numeroimpegnoprimarata(opzionale)",
                "impegnoprimarata",
                "numimpegnoprimarata",
                "num_impegno_primaRata",
                "num_impegno_prima_rata");

            int? impegnoSaldoCol = TryResolveColumn(headers,
                "numeroimpegnosaldo",
                "numeroimpegnosaldo(opzionale)",
                "impegnosaldo",
                "numimpegnosaldo",
                "num_impegno_saldo");

            int motivazioneCol = ResolveColumn(headers,
                "motivazionemodificaimporto",
                "motivazionemodificastatussede",
                "motivazione",
                "motivomodificaimporto",
                "motivomodificastatussede",
                "motivo");

            var input = new DataTable("InputModificaImporto");
            input.Columns.Add("Cod_fiscale", typeof(string));
            input.Columns.Add("ImpegnoPrimaRata", typeof(string));
            input.Columns.Add("ImpegnoSaldo", typeof(string));
            input.Columns.Add("Motivazione", typeof(string));

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

            for (int row = 2; row <= lastRow; row++)
            {
                string cf = ws.Cell(row, cfCol).GetString().Trim().ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(cf))
                    continue;

                if (cf.Length != 16)
                    throw new ValidationException($"Codice fiscale non valido alla riga {row}: '{cf}'.");

                if (!seen.Add(cf))
                    throw new ValidationException($"Codice fiscale duplicato nel modello: {cf}.");

                string impegnoPrimaRata = impegnoPrimaRataCol.HasValue
                    ? ws.Cell(row, impegnoPrimaRataCol.Value).GetString().Trim()
                    : string.Empty;

                string impegnoSaldo = impegnoSaldoCol.HasValue
                    ? ws.Cell(row, impegnoSaldoCol.Value).GetString().Trim()
                    : string.Empty;

                string motivazione = ws.Cell(row, motivazioneCol).GetString().Trim();

                input.Rows.Add(cf, impegnoPrimaRata, impegnoSaldo, motivazione);
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

        private static int? TryResolveColumn(Dictionary<string, int> headers, params string[] aliases)
        {
            foreach (string alias in aliases)
            {
                string key = NormalizeHeader(alias);
                if (headers.TryGetValue(key, out int col))
                    return col;
            }

            return null;
        }

        private void CreateInputTempTable(DataTable input)
        {
            const string sql = @"
IF OBJECT_ID('tempdb..#InputModificaImporto') IS NOT NULL
    DROP TABLE #InputModificaImporto;

CREATE TABLE #InputModificaImporto
(
    Cod_fiscale NVARCHAR(16) NOT NULL,
    ImpegnoPrimaRata NVARCHAR(100) NULL,
    ImpegnoSaldo NVARCHAR(100) NULL,
    Motivazione NVARCHAR(MAX) NULL,
    CONSTRAINT PK_InputModificaImporto PRIMARY KEY CLUSTERED (Cod_fiscale)
);";

            using (var cmd = new SqlCommand(sql, Connection))
            {
                cmd.CommandTimeout = 120;
                cmd.ExecuteNonQuery();
            }

            using var bulk = new SqlBulkCopy(Connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "#InputModificaImporto",
                BulkCopyTimeout = 120,
                BatchSize = 5000
            };

            bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
            bulk.ColumnMappings.Add("ImpegnoPrimaRata", "ImpegnoPrimaRata");
            bulk.ColumnMappings.Add("ImpegnoSaldo", "ImpegnoSaldo");
            bulk.ColumnMappings.Add("Motivazione", "Motivazione");
            bulk.WriteToServer(input);

            using var statCmd = new SqlCommand("UPDATE STATISTICS #InputModificaImporto;", Connection)
            {
                CommandTimeout = 120
            };
            statCmd.ExecuteNonQuery();
        }

        private void ValidateQueryResult(DataTable input, DataTable result, AllegatoContext context)
        {
            HashSet<string> returned = result.AsEnumerable()
                .Select(r => S(r, "CF").Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> missing = input.AsEnumerable()
                .Select(r => r.Field<string>("Cod_fiscale") ?? string.Empty)
                .Where(cf => !returned.Contains(cf))
                .OrderBy(cf => cf)
                .ToList();

            if (missing.Count == 0)
                return;

            DataTable errori = new("ErroriModificaImporto");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Errore", typeof(string));

            foreach (string cf in missing)
            {
                errori.Rows.Add(
                    cf,
                    "Studente non trovato, senza esito BS o senza riga valida in Specifiche_impegni.");
            }

            string fullPath = ExportSimpleTable(
                errori,
                context.SaveFolder,
                BuildSupportFileName($"ERRORI_{OperationFileToken}_BS", context.AnnoAccademico));

            throw new ValidationException(
                $"Elaborazione bloccata. Alcuni codici fiscali non hanno dati sufficienti per l'allegato {Descrizione} BS. " +
                $"File errori creato: {fullPath}");
        }

        private void ValidateAccountingData(DataTable result, AllegatoContext context)
        {
            DataTable errori = new("ErroriImpegniModificaImporto");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Cognome", typeof(string));
            errori.Columns.Add("Nome", typeof(string));
            errori.Columns.Add("Num_domanda", typeof(string));
            errori.Columns.Add("Impegno_prima_rata_modello", typeof(string));
            errori.Columns.Add("Impegno_saldo_modello", typeof(string));
            errori.Columns.Add("Impegno_richiesto", typeof(string));
            errori.Columns.Add("Errore", typeof(string));

            foreach (DataRow row in result.Rows)
            {
                string impegnoRichiesto = S(row, "ImpegnoRichiesto").Trim();
                string impegnoTrovato = S(row, "Impegno").Trim();
                string tipoFondo = S(row, "TipoFondo").Trim();
                string capitolo = S(row, "Capitolo").Trim();

                if (string.IsNullOrWhiteSpace(impegnoRichiesto))
                {
                    AddAccountingError(errori, row, "Impegno saldo non presente nel modello e non presente in Specifiche_impegni.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(impegnoTrovato))
                {
                    AddAccountingError(errori, row, "Impegno non trovato nella tabella Impegni.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tipoFondo))
                {
                    AddAccountingError(errori, row, "Tipo fondo non valorizzato nella tabella Impegni.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(capitolo))
                {
                    AddAccountingError(errori, row, "Tipo fondo dell'impegno non trovato nella tabella Capitoli.");
                }
            }

            if (errori.Rows.Count == 0)
                return;

            string fullPath = ExportSimpleTable(
                errori,
                context.SaveFolder,
                BuildSupportFileName($"ERRORI_{OperationFileToken}_BS_Impegni", context.AnnoAccademico));

            throw new ValidationException(
                $"Elaborazione bloccata. Alcuni impegni del {Descrizione} BS non sono validi o non hanno dati contabili completi. " +
                $"File errori creato: {fullPath}");
        }

        private void ValidateStatusSede(DataTable result, AllegatoContext context)
        {
            if (!IncludeStatusSede)
                return;

            DataTable errori = new("ErroriStatusSede");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Cognome", typeof(string));
            errori.Columns.Add("Nome", typeof(string));
            errori.Columns.Add("Num_domanda", typeof(string));
            errori.Columns.Add("Status_sede_attuale", typeof(string));
            errori.Columns.Add("Status_sede_precedente", typeof(string));
            errori.Columns.Add("Errore", typeof(string));

            foreach (DataRow row in result.Rows)
            {
                string attuale = S(row, "StatusSedeAttuale").Trim();
                string precedente = S(row, "StatusSedePrecedente").Trim();
                bool attualeValido = IsStatusSedeValido(attuale);
                bool precedenteValido = IsStatusSedeValido(precedente);

                if (!attualeValido || !precedenteValido)
                {
                    string errore = string.IsNullOrWhiteSpace(attuale)
                        ? "Status sede attuale non trovato in Valori_calcolati."
                        : string.IsNullOrWhiteSpace(precedente)
                            ? "Nessuno status sede precedente diverso da quello attuale trovato in Valori_calcolati."
                            : !attualeValido
                                ? $"Status sede attuale non valido: '{attuale}'. Valori ammessi: A, B, C, D."
                                : $"Status sede precedente non valido: '{precedente}'. Valori ammessi: A, B, C, D.";

                    errori.Rows.Add(
                        S(row, "CF"),
                        S(row, "COGNOME"),
                        S(row, "NOME"),
                        S(row, "NUM_DOMANDA"),
                        attuale,
                        precedente,
                        errore);
                }
            }

            if (errori.Rows.Count == 0)
                return;

            string fullPath = ExportSimpleTable(
                errori,
                context.SaveFolder,
                BuildSupportFileName("ERRORI_CambioStatusSede_BS_Status", context.AnnoAccademico));

            throw new ValidationException(
                "Elaborazione bloccata. Per alcuni studenti non è stato possibile determinare lo status sede attuale e precedente. " +
                $"File errori creato: {fullPath}");
        }

        private static bool IsStatusSedeValido(string status)
        {
            return status.Trim().ToUpperInvariant() is "A" or "B" or "C" or "D";
        }

        private static void AddAccountingError(DataTable errori, DataRow row, string errore)
        {
            errori.Rows.Add(
                S(row, "CF"),
                S(row, "COGNOME"),
                S(row, "NOME"),
                S(row, "NUM_DOMANDA"),
                S(row, "ImpegnoPrimaRataInput"),
                S(row, "ImpegnoSaldoInput"),
                S(row, "ImpegnoRichiesto"),
                errore);
        }

        private DataTable FilterUnchangedAmounts(DataTable result, AllegatoContext context)
        {
            DataTable allegato = result.Clone();
            DataTable errori = new("ErroriImportiUguali");
            errori.Columns.Add("Cod_fiscale", typeof(string));
            errori.Columns.Add("Cognome", typeof(string));
            errori.Columns.Add("Nome", typeof(string));
            errori.Columns.Add("Num_domanda", typeof(string));
            errori.Columns.Add("Importo_precedente", typeof(decimal));
            errori.Columns.Add("Importo_attuale", typeof(decimal));
            errori.Columns.Add("Errore", typeof(string));

            foreach (DataRow row in result.Rows)
            {
                decimal importoPrecedente = D(row, "ImportoPrecedente");
                decimal importoAttuale = D(row, "ImportoAttuale");

                if (importoPrecedente == importoAttuale)
                {
                    errori.Rows.Add(
                        S(row, "CF"),
                        S(row, "COGNOME"),
                        S(row, "NOME"),
                        S(row, "NUM_DOMANDA"),
                        importoPrecedente,
                        importoAttuale,
                        "Importo precedente e importo attuale coincidono.");
                    continue;
                }

                allegato.ImportRow(row);
            }

            if (errori.Rows.Count > 0)
            {
                string fullPath = ExportSimpleTable(
                    errori,
                    context.SaveFolder,
                    BuildSupportFileName($"ERRORI_{OperationFileToken}_BS_ImportiUguali", context.AnnoAccademico));

                Logger.LogWarning(
                    null,
                    $"{Descrizione} BS: esclusi {errori.Rows.Count} studenti con importi invariati. File errori creato: {fullPath}");
            }

            if (allegato.Rows.Count == 0)
            {
                throw new ValidationException(
                    "Elaborazione bloccata. Nessuno studente ha una differenza tra importo precedente e importo attuale.");
            }

            return allegato;
        }

        private string BuildPlaceholderFileName(string aa, string tipoFondo)
        {
            return Sanitize(
                $"PLACEHOLDER_{OperationFileToken}_BS_Fondi_{tipoFondo}_{aa}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        private string OperationFileToken =>
            IncludeStatusSede ? "CambioStatusSede" : "ModificaImporto";

        private static string BuildSupportFileName(string prefix, string aa)
        {
            return Sanitize($"{prefix}_{aa}_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
        }

        private string ExportAllegato(
            DataTable dataTable,
            string folderPath,
            string fileName,
            string aa,
            string tipoFondo)
        {
            if (dataTable.Rows.Count == 0)
                throw new ValidationException($"La query {Descrizione} BS non ha restituito righe.");

            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            List<string> impegni = dataTable.AsEnumerable()
                .Select(r => S(r, "Impegno").Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (impegni.Count == 0)
                impegni.Add(string.Empty);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Foglio 1");

            int statusColumns = IncludeStatusSede ? 2 : 0;
            int importoPrecedenteColumn = 6 + statusColumns;
            int importoAttualeColumn = importoPrecedenteColumn + 1;
            int differenzaColumn = importoAttualeColumn + 1;
            int motivazioneColumn = differenzaColumn + 1;
            int impegniStartColumn = motivazioneColumn + 1;
            int totalColumns = impegniStartColumn - 1 + impegni.Count;
            string anno = $"{aa.Substring(0, 4)}/{aa.Substring(4, 4)}";

            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;

            string titoloOperazione = IncludeStatusSede ? "CAMBIO STATUS SEDE" : "MODIFICA IMPORTO BORSA";
            ws.Cell(1, 1).Value = $"BS {anno} - {titoloOperazione} - FONDI {tipoFondo}";
            ws.Range(1, 1, 1, Math.Max(8, totalColumns - 1)).Merge();
            ws.Cell(2, 1).Value = "ALLEGATO DETERMINA";
            ws.Range(2, 1, 2, Math.Max(8, totalColumns - 1)).Merge();

            ws.Range(1, 1, 2, Math.Max(8, totalColumns - 1)).Style
                .Font.SetBold()
                .Font.SetFontSize(12)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            int headerRow = 4;
            List<string> headers = new()
            {
                "N",
                "CF",
                "COGNOME",
                "NOME",
                "NUM_DOMANDA"
            };

            if (IncludeStatusSede)
            {
                headers.Add("Status sede attuale");
                headers.Add("Status sede precedente");
            }

            headers.AddRange(new[]
            {
                "IMPORTO PRECEDENTE",
                "IMPORTO ATTUALE",
                "DIFFERENZA",
                "MOTIVAZIONE"
            });

            for (int col = 0; col < headers.Count; col++)
                ws.Cell(headerRow, col + 1).Value = headers[col];

            for (int i = 0; i < impegni.Count; i++)
            {
                string impegno = impegni[i];
                ws.Cell(headerRow, headers.Count + i + 1).Value = string.IsNullOrWhiteSpace(impegno)
                    ? "IMPEGNO"
                    : $"IMPEGNO {impegno}";
            }

            ws.Range(headerRow, 1, headerRow, totalColumns).Style
                .Font.SetBold()
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
                .Fill.SetBackgroundColor(XLColor.FromHtml("#D9EAF7"))
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetInsideBorder(XLBorderStyleValues.Thin);

            int row = headerRow + 1;
            int progressivo = 1;
            Dictionary<string, decimal> impegnoTotals = impegni.ToDictionary(i => i, _ => 0m, StringComparer.OrdinalIgnoreCase);
            decimal totalePrecedente = 0m;
            decimal totaleAttuale = 0m;
            decimal totaleDifferenza = 0m;

            foreach (DataRow dataRow in dataTable.AsEnumerable().OrderBy(r => S(r, "COGNOME")).ThenBy(r => S(r, "NOME")))
            {
                decimal importoPrecedente = D(dataRow, "ImportoPrecedente");
                decimal importoAttuale = D(dataRow, "ImportoAttuale");
                decimal differenza = importoAttuale - importoPrecedente;
                string impegno = S(dataRow, "Impegno").Trim();

                ws.Cell(row, 1).Value = progressivo;
                ws.Cell(row, 2).Value = S(dataRow, "CF");
                ws.Cell(row, 3).Value = S(dataRow, "COGNOME");
                ws.Cell(row, 4).Value = S(dataRow, "NOME");
                ws.Cell(row, 5).Value = S(dataRow, "NUM_DOMANDA");

                if (IncludeStatusSede)
                {
                    ws.Cell(row, 6).Value = FormatStatusSede(S(dataRow, "StatusSedeAttuale"));
                    ws.Cell(row, 7).Value = FormatStatusSede(S(dataRow, "StatusSedePrecedente"));
                }

                ws.Cell(row, importoPrecedenteColumn).Value = importoPrecedente;
                ws.Cell(row, importoAttualeColumn).Value = importoAttuale;
                ws.Cell(row, differenzaColumn).Value = differenza;
                ws.Cell(row, motivazioneColumn).Value = S(dataRow, "Motivazione");

                int impegnoIndex = impegni.FindIndex(i => i.Equals(impegno, StringComparison.OrdinalIgnoreCase));
                if (impegnoIndex < 0)
                    impegnoIndex = 0;

                ws.Cell(row, impegniStartColumn + impegnoIndex).Value = differenza;
                impegnoTotals[impegni[impegnoIndex]] += differenza;

                totalePrecedente += importoPrecedente;
                totaleAttuale += importoAttuale;
                totaleDifferenza += differenza;

                row++;
                progressivo++;
            }

            int totalRow = row;
            ws.Cell(totalRow, 4).Value = "TOTALE";
            ws.Cell(totalRow, importoPrecedenteColumn).Value = totalePrecedente;
            ws.Cell(totalRow, importoAttualeColumn).Value = totaleAttuale;
            ws.Cell(totalRow, differenzaColumn).Value = totaleDifferenza;

            for (int i = 0; i < impegni.Count; i++)
                ws.Cell(totalRow, impegniStartColumn + i).Value = impegnoTotals[impegni[i]];

            ws.Range(totalRow, 4, totalRow, totalColumns).Style.Font.SetBold();

            int riepilogoRow = totalRow + 3;
            foreach (string impegno in impegni)
            {
                decimal totaleImpegno = impegnoTotals[impegno];
                string label = BuildRiepilogoLabel(totaleImpegno, impegno);

                ws.Cell(riepilogoRow, 4).Value = label;
                ws.Cell(riepilogoRow, importoPrecedenteColumn).Value = totaleImpegno;
                ws.Range(riepilogoRow, 4, riepilogoRow, importoPrecedenteColumn).Style.Font.SetBold();
                riepilogoRow++;
            }

            string euroFormat =
                "_-[$€-it-IT]* #,##0.00_-;-[$€-it-IT]* #,##0.00_-;_-[$€-it-IT]* \"-\"??_-;_-@_-";

            for (int col = importoPrecedenteColumn; col <= differenzaColumn; col++)
                ws.Column(col).Style.NumberFormat.Format = euroFormat;

            for (int col = impegniStartColumn; col <= totalColumns; col++)
                ws.Column(col).Style.NumberFormat.Format = euroFormat;

            ws.Range(headerRow, 1, totalRow, totalColumns).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(headerRow, 1, totalRow, totalColumns).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Range(headerRow + 1, 1, totalRow, totalColumns).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            ApplyColumnWidths(ws, totalColumns, IncludeStatusSede);
            ws.Column(motivazioneColumn).Style.Alignment.WrapText = true;
            ws.SheetView.FreezeRows(headerRow);

            wb.SaveAs(fullPath);
            return fullPath;
        }

        private static string FormatStatusSede(string status)
        {
            return status.Trim().ToUpperInvariant() switch
            {
                "A" => "In sede",
                "B" => "Fuori sede",
                "C" => "Pendolare",
                "D" => "Pendolare calcolato",
                _ => status.Trim().ToUpperInvariant()
            };
        }

        private static string BuildRiepilogoLabel(decimal importo, string impegno)
        {
            string tipo = importo switch
            {
                > 0m => "MAGGIORE SPESA",
                < 0m => "ECONOMIA",
                _ => "NESSUNA VARIAZIONE"
            };

            return string.IsNullOrWhiteSpace(impegno)
                ? tipo
                : $"{tipo} IMPEGNO {impegno}";
        }

        private static void ApplyColumnWidths(IXLWorksheet ws, int totalColumns, bool includeStatusSede)
        {
            double[] widths = includeStatusSede
                ? new double[] { 4, 19, 14, 16, 14, 22, 24, 21, 18, 15, 36 }
                : new double[] { 4, 19, 14, 16, 14, 21, 18, 15, 36 };

            for (int i = 0; i < widths.Length; i++)
                ws.Column(i + 1).Width = widths[i];

            for (int col = widths.Length + 1; col <= totalColumns; col++)
                ws.Column(col).Width = 16;
        }

        private static string ExportSimpleTable(DataTable dataTable, string folderPath, string fileName)
        {
            string fullPath = NormalizeLongPath(Path.Combine(folderPath, fileName));
            System.IO.Directory.CreateDirectory(folderPath);

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Errori");

            for (int col = 0; col < dataTable.Columns.Count; col++)
            {
                ws.Cell(1, col + 1).Value = dataTable.Columns[col].ColumnName;
                ws.Column(col + 1).Width = Math.Max(16, dataTable.Columns[col].ColumnName.Length + 2);
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
                ws.Range(1, 1, 1, dataTable.Columns.Count).Style
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

        private static string GetModificaImportoQuery()
        {
            return @"
;WITH Domande AS (
    SELECT
        d.Anno_accademico,
        d.Num_domanda,
        d.Cod_fiscale,
        NULLIF(LTRIM(RTRIM(i.ImpegnoPrimaRata)), '') AS ImpegnoPrimaRataInput,
        NULLIF(LTRIM(RTRIM(i.ImpegnoSaldo)), '') AS ImpegnoSaldoInput,
        LTRIM(RTRIM(ISNULL(i.Motivazione, ''))) AS Motivazione
    FROM #InputModificaImporto i
    INNER JOIN Domanda d
        ON d.Cod_fiscale = i.Cod_fiscale
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'LZ'
),
SpecificheRanked AS (
    SELECT
        si.Anno_accademico,
        si.Num_domanda,
        si.Importo_assegnato,
        d.ImpegnoPrimaRataInput,
        d.ImpegnoSaldoInput,
        d.Motivazione,
        COALESCE(
            d.ImpegnoSaldoInput,
            d.ImpegnoPrimaRataInput,
            NULLIF(LTRIM(RTRIM(si.num_impegno_saldo)), '')
        ) AS ImpegnoRichiesto,
        ROW_NUMBER() OVER (
            PARTITION BY si.Anno_accademico, si.Num_domanda
            ORDER BY
                CASE WHEN si.Data_fine_validita IS NULL THEN 0 ELSE 1 END,
                si.Data_validita DESC
        ) AS rn
    FROM Specifiche_impegni si
    INNER JOIN Domande d
        ON d.Anno_accademico = si.Anno_accademico
       AND d.Num_domanda = si.Num_domanda
    WHERE si.Cod_beneficio = 'BS'
)
SELECT
    d.Cod_fiscale AS CF,
    st.Cognome AS COGNOME,
    st.Nome AS NOME,
    d.Num_domanda AS NUM_DOMANDA,
    CONVERT(money, si.Importo_assegnato, 0) AS ImportoPrecedente,
    CONVERT(money, bs.Imp_beneficio, 0) AS ImportoAttuale,
    si.ImpegnoPrimaRataInput,
    si.ImpegnoSaldoInput,
    si.Motivazione,
    statusAttuale.Status_sede AS StatusSedeAttuale,
    statusPrecedente.Status_sede AS StatusSedePrecedente,
    si.ImpegnoRichiesto,
    CONVERT(NVARCHAR(100), imp.num_impegno) AS Impegno,
    imp.descr AS DescrizioneImpegno,
    imp.anno_esercizio_finanziario AS AnnoEsercizioFinanziario,
    UPPER(LTRIM(RTRIM(imp.Tipo_fondo))) AS TipoFondo,
    cap.Capitolo,
    cap.Descrizione_capitolo AS DescrizioneCapitolo
FROM Domande d
INNER JOIN Studente st
    ON st.Cod_fiscale = d.Cod_fiscale
INNER JOIN vEsiti_concorsiBS bs
    ON bs.Anno_accademico = d.Anno_accademico
   AND bs.Num_domanda = d.Num_domanda
INNER JOIN SpecificheRanked si
    ON si.Anno_accademico = d.Anno_accademico
   AND si.Num_domanda = d.Num_domanda
   AND si.rn = 1
OUTER APPLY (
    SELECT TOP 1
        i.num_impegno,
        i.descr,
        i.anno_esercizio_finanziario,
        i.Tipo_fondo
    FROM Impegni i
    WHERE i.anno_accademico = d.Anno_accademico
      AND (
          LTRIM(RTRIM(CONVERT(NVARCHAR(100), i.num_impegno))) = si.ImpegnoRichiesto
          OR LTRIM(RTRIM(i.descr)) = si.ImpegnoRichiesto
      )
    ORDER BY
        CASE WHEN LTRIM(RTRIM(CONVERT(NVARCHAR(100), i.num_impegno))) = si.ImpegnoRichiesto THEN 0 ELSE 1 END,
        i.num_impegno
) imp
OUTER APPLY (
    SELECT TOP 1
        UPPER(LTRIM(RTRIM(vc.Status_sede))) AS Status_sede,
        vc.Id_Valori_calcolati
    FROM Valori_calcolati vc
    WHERE vc.Anno_accademico = d.Anno_accademico
      AND vc.Num_domanda = d.Num_domanda
      AND NULLIF(LTRIM(RTRIM(vc.Status_sede)), '') IS NOT NULL
    ORDER BY vc.Id_Valori_calcolati DESC
) statusAttuale
OUTER APPLY (
    SELECT TOP 1
        UPPER(LTRIM(RTRIM(vc.Status_sede))) AS Status_sede
    FROM Valori_calcolati vc
    WHERE vc.Anno_accademico = d.Anno_accademico
      AND vc.Num_domanda = d.Num_domanda
      AND NULLIF(LTRIM(RTRIM(vc.Status_sede)), '') IS NOT NULL
      AND vc.Id_Valori_calcolati < statusAttuale.Id_Valori_calcolati
      AND UPPER(LTRIM(RTRIM(vc.Status_sede))) <> statusAttuale.Status_sede
    ORDER BY vc.Id_Valori_calcolati DESC
) statusPrecedente
LEFT JOIN Capitoli cap
    ON UPPER(LTRIM(RTRIM(cap.Tipo_fondo))) = UPPER(LTRIM(RTRIM(imp.Tipo_fondo)))
ORDER BY
    st.Cognome,
    st.Nome,
    d.Cod_fiscale;";
        }
    }
}
