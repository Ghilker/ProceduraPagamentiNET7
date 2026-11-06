using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace ProcedureNet7
{
    internal class PremiLaurea : BaseProcedure<ArgsPremiLaurea>
    {
        public PremiLaurea(MasterForm? _masterForm, SqlConnection? connection_string)
            : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsPremiLaurea args)
        {
            string inputPath = args.FileExcelInput;
            string outputPath = args.FileExcelOutput;

            AggiungiColonnePremiLaurea(inputPath, outputPath, args.AnnoAccademico);
        }

        private void AggiungiColonnePremiLaurea(string inputPath, string outputPath, string annoAccademico)
        {
            int? annoAccademicoAtteso = ParseAnnoAccademico(annoAccademico);

            // --- 1️⃣ Lettura Excel come DataTable ---
            DataTable dt = Utilities.ReadExcelToDataTable(inputPath);
            if (dt == null || dt.Rows.Count == 0)
                throw new Exception("Il file Excel di input è vuoto o non leggibile.");
            if (dt.Columns.Count < 21)
                throw new Exception("Il file Excel deve contenere le 21 colonne del tracciato premi laurea, da A a U.");

            // --- 2️⃣ Aggiunge le colonne di verifica ---
            string[] nuoveColonne = new[]
            {
                "SE Data",
                "SE Voto",
                "SE Lode",
                "SE Carriera pregressa",
                "SE domanda oltre termine",
                "SE anno di corso coerente",
                "SE anno di corso effettivo",
                "SE anno accademico di conseguimento titolo",
                "SE lavorabile massivamente"
            };

            foreach (string col in nuoveColonne)
            {
                if (!dt.Columns.Contains(col))
                    dt.Columns.Add(col, typeof(string));
            }

            // --- 3️⃣ Crea due DataTable separate ---
            DataTable okTable = dt.Clone();
            DataTable noTable = dt.Clone();

            // --- 4️⃣ Indici colonne (zero-based) ---
            int idxDataTrasmissione = 7;
            int idxInvalido = 8;
            int idxAAImmDich = 9;
            int idxDataDich = 10;
            int idxVotoDich = 11;
            int idxLodeDich = 12;
            int idxAnnoCorsoDich = 13;
            int idxAAImmCert = 14;
            int idxDataCert = 15;
            int idxVotoCert = 16;
            int idxLodeCert = 17;
            int idxAAConseguimentoCert = 18;
            int idxAnnoCorsoCert = 19;
            int idxAbbreviazione = 20;
            int idxSede = 4;

            // --- 5️⃣ Elaborazione righe ---
            foreach (DataRow row in dt.Rows)
            {
                try
                {
                    // --- Lettura valori ---
                    DateTime? dataTrasmissione = ParseData(row[idxDataTrasmissione]);
                    DateTime? dataDichiarato = ParseData(row[idxDataDich]);
                    DateTime? dataCertificato = ParseData(row[idxDataCert]);

                    decimal? votoDich = ParseDecimale(row[idxVotoDich]);
                    decimal? votoCert = ParseDecimale(row[idxVotoCert]);

                    string? lodeDich = row[idxLodeDich]?.ToString()?.Trim();
                    string? lodeCert = row[idxLodeCert]?.ToString()?.Trim();
                    string? abbreviazione = row[idxAbbreviazione]?.ToString()?.Trim();

                    string? aaImmCertStr = row[idxAAImmCert]?.ToString()?.Trim();
                    int? aaImmCert = ParseAnnoAccademico(aaImmCertStr);
                    int? annoCorsoDich = ParseInt(row[idxAnnoCorsoDich]);
                    int? annoCorsoCert = ParseInt(row[idxAnnoCorsoCert]);
                    int? invalido = ParseInt(row[idxInvalido]);
                    int? aaConseguimentoCert = ParseAnnoAccademico(row[idxAAConseguimentoCert]?.ToString());

                    // --- Calcolo SE ---
                    string seData = (dataDichiarato.HasValue && dataCertificato.HasValue &&
                                     dataDichiarato.Value.Date == dataCertificato.Value.Date) ? "OK" : "NO";

                    string seVoto = (votoDich.HasValue && votoCert.HasValue &&
                                     votoDich.Value == votoCert.Value) ? "OK" : "NO";

                    string seLode = (!string.IsNullOrEmpty(lodeDich) && !string.IsNullOrEmpty(lodeCert) &&
                                     string.Equals(lodeDich, lodeCert, StringComparison.OrdinalIgnoreCase)) ? "OK" : "NO";

                    string seCarrieraPregressa = (string.IsNullOrEmpty(abbreviazione) ||
                                                  ParseDecimale(abbreviazione) == 0) ? "OK" : "NO";

                    string seDomandaOltreTermine = "NO";
                    if (dataTrasmissione.HasValue && dataCertificato.HasValue)
                    {
                        double giorniDiff = (dataTrasmissione.Value - dataCertificato.Value).TotalDays;
                        if (giorniDiff >= 0 && giorniDiff <= 30)
                            seDomandaOltreTermine = "OK";
                    }

                    string seAnnoCorso = "NO";
                    // Controlla che i valori necessari siano presenti
                    if (!string.IsNullOrWhiteSpace(aaImmCertStr) &&
                        annoCorsoDich.HasValue &&
                        row[idxAAConseguimentoCert] != null &&
                        row[idxAAConseguimentoCert] != DBNull.Value)
                    {
                        // Estrae il valore della cella come stringa e rimuove spazi/slash
                        string annoRiferimento = row[idxAAConseguimentoCert].ToString()!.Trim();

                        // Converte l'anno di immatricolazione in formato pulito
                        string aaImmPulito = aaImmCertStr.Replace("/", "").Trim();
                        // Converte l'anno di riferimento in formato pulito
                        string annoRiferimentoPulito = annoRiferimento.Replace("/", "").Trim();
                        // Verifica l'anno accademico senza sottrarre 1
                        int verifica = VerificaAnnoAccademico(aaImmPulito, annoCorsoDich.Value -1, annoRiferimentoPulito);

                        if (verifica != -1)
                            seAnnoCorso = "OK";
                    }

                    string seAnnoCorsoEffettivo = (annoCorsoDich.HasValue &&
                                                  annoCorsoCert.HasValue &&
                                                  annoCorsoDich.Value == annoCorsoCert.Value &&
                                                  (annoCorsoCert.Value != -1 || invalido == 1)) ? "OK" : "NO";

                    string seAAConseguimento = (annoAccademicoAtteso.HasValue &&
                                               aaConseguimentoCert.HasValue &&
                                               annoAccademicoAtteso.Value == aaConseguimentoCert.Value) ? "OK" : "NO";

                    string seLavorabile = (seData == "OK" &&
                                           seVoto == "OK" &&
                                           seLode == "OK" &&
                                           seCarrieraPregressa == "OK" &&
                                           seDomandaOltreTermine == "OK" &&
                                           seAnnoCorso == "OK" &&
                                           seAnnoCorsoEffettivo == "OK" &&
                                           seAAConseguimento == "OK") ? "OK" : "NO";

                    // --- Scrive valori SE ---
                    row["SE Data"] = seData;
                    row["SE Voto"] = seVoto;
                    row["SE Lode"] = seLode;
                    row["SE Carriera pregressa"] = seCarrieraPregressa;
                    row["SE domanda oltre termine"] = seDomandaOltreTermine;
                    row["SE anno di corso coerente"] = seAnnoCorso;
                    row["SE anno di corso effettivo"] = seAnnoCorsoEffettivo;
                    row["SE anno accademico di conseguimento titolo"] = seAAConseguimento;
                    row["SE lavorabile massivamente"] = seLavorabile;

                    // --- Smista nelle tabelle ---
                    DataRow newRow = (seLavorabile == "OK") ? okTable.NewRow() : noTable.NewRow();
                    newRow.ItemArray = row.ItemArray.Clone() as object[];
                    if (seLavorabile == "OK")
                        okTable.Rows.Add(newRow);
                    else
                        noTable.Rows.Add(newRow);
                }
                catch (Exception ex)
                {
                    row["SE lavorabile massivamente"] = $"ERRORE: {ex.Message}";
                    DataRow newRow = noTable.NewRow();
                    newRow.ItemArray = row.ItemArray.Clone() as object[];
                    noTable.Rows.Add(newRow);
                }
            }

            // --- 6️⃣ Scrive i due file finali nella cartella indicata da outputPath ---
            // Assicurati che la cartella esista
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            // Nome della sede
            string sedeStudi = dt.Rows.Count > 0 ? dt.Rows[0][idxSede]?.ToString() ?? "Sede" : "Sede";
            foreach (var c in Path.GetInvalidFileNameChars())
                sedeStudi = sedeStudi.Replace(c, '_');

            string annoFile = annoAccademico.Replace("/", "").Trim();
            EsportaRisultati(okTable, outputPath, $"PL {annoFile} {sedeStudi} validati massivamente");
            EsportaRisultati(noTable, outputPath, $"PL {annoFile} {sedeStudi} validati manualmente");
        }

        private static void EsportaRisultati(DataTable dati, string cartella, string nomeFile)
        {
            string percorso = Utilities.ExportDataTableToExcel(dati, cartella, fileName: nomeFile);
            if (string.IsNullOrEmpty(percorso)) return;

            using var workbook = new XLWorkbook(percorso);
            var sheet = workbook.Worksheet(1);
            sheet.Row(1).InsertRowsAbove(1);
            int ultimaRiga = dati.Rows.Count + 2;
            int ultimaColonna = dati.Columns.Count;
            var area = sheet.Range(1, 1, ultimaRiga, ultimaColonna);
            area.Style.Font.FontName = "Arial";
            area.Style.Font.FontSize = 10;
            area.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            area.Style.Alignment.WrapText = true;
            sheet.ShowGridLines = false;

            string[] intestazioni =
            {
                "N.", "Codice fiscale", "Cognome", "Nome", "Sede studi", "Facoltà",
                "Numero domanda PL", "Data trasmissione", "Invalido",
                "AA prima immatricolazione", "Data conseguimento titolo", "Voto conseguito", "Lode", "Anno effettivo di corso",
                "AA prima immatricolazione", "Data conseguimento titolo", "Voto conseguito", "Lode",
                "AA conseguimento titolo", "Anno effettivo di corso", "Abbreviazione carriera / CFU riconosciuti"
            };
            double[] larghezze = { 7, 24, 22, 20, 24, 28, 18, 18, 10, 22, 18, 12, 10, 22, 22, 18, 12, 10, 22, 22, 28 };
            for (int col = 1; col <= ultimaColonna; col++)
            {
                sheet.Column(col).Width = col <= larghezze.Length ? larghezze[col - 1] : 23;
                if (col <= intestazioni.Length) sheet.Cell(2, col).Value = intestazioni[col - 1];
            }

            var intestazione = sheet.Range(1, 1, 2, ultimaColonna);
            intestazione.Style.Font.Bold = true;
            intestazione.Style.Font.FontColor = XLColor.White;
            intestazione.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Row(1).Height = 26;
            sheet.Row(2).Height = 76;
            sheet.Rows(3, ultimaRiga).Height = 30;

            foreach (var (inizio, fine, titolo, colore) in new[]
            {
                (1, 9, "DATI DOMANDA", "#475569"),
                (10, 14, "DICHIARATO", "#245A81"),
                (15, 21, "CERTIFICATO", "#65528A"),
                (22, ultimaColonna, "CONTROLLI SE", "#7C541A")
            })
            {
                sheet.Range(1, inizio, 1, fine).Merge().Value = titolo;
                sheet.Range(1, inizio, 2, fine).Style.Fill.BackgroundColor = XLColor.FromHtml(colore);
                sheet.Range(2, inizio, 2, fine).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(2, inizio, 2, fine).Style.Border.InsideBorderColor = XLColor.White;
                sheet.Range(1, inizio, ultimaRiga, inizio).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
                sheet.Range(1, inizio, ultimaRiga, inizio).Style.Border.LeftBorderColor = XLColor.FromHtml(colore);
            }

            for (int col = 22; col <= ultimaColonna; col++)
            {
                if (!dati.Columns[col - 1].ColumnName.StartsWith("SE ", StringComparison.Ordinal)) continue;
                var esiti = sheet.Range(3, col, ultimaRiga, col);
                esiti.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                esiti.AddConditionalFormat().WhenEquals("NO")
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#FDE9E7"))
                    .Font.SetFontColor(XLColor.FromHtml("#9C2424")).Font.SetBold();
                esiti.AddConditionalFormat().WhenStartsWith("ERRORE:")
                    .Fill.SetBackgroundColor(XLColor.FromHtml("#FFF0CC"))
                    .Font.SetFontColor(XLColor.FromHtml("#7C541A")).Font.SetBold();
            }
            sheet.Range(2, 1, ultimaRiga, ultimaColonna).SetAutoFilter();
            sheet.SheetView.FreezeRows(2);
            sheet.SheetView.FreezeColumns(4);
            workbook.Save();
        }

        // 🔧 Parser sicuri
        private DateTime? ParseData(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (DateTime.TryParse(value.ToString(), out var dt)) return dt;
            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var oa))
                return DateTime.FromOADate(oa);
            return null;
        }

        private decimal? ParseDecimale(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                return d;
            return null;
        }

        private int? ParseInt(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            if (int.TryParse(value.ToString(), out int i)) return i;
            return null;
        }

        private int? ParseAnnoAccademico(string? aa)
        {
            if (string.IsNullOrWhiteSpace(aa)) return null;
            var aaNew = aa.Replace("/", "").Trim();
            if (aaNew.Length != 8) return null;
            if (int.TryParse(aaNew, out int anno)) return anno;
            return null;
        }

        // ✅ Verifica anno accademico
        public static int VerificaAnnoAccademico(string annoAccademico, int anniDaAggiungere, string annoRiferimento)
        {
            if (string.IsNullOrWhiteSpace(annoAccademico))
                return -1;

            string aaPulito = annoAccademico.Replace("/", "").Trim();
            if (aaPulito.Length != 8 || !int.TryParse(aaPulito.Substring(0, 4), out int inizio) || !int.TryParse(aaPulito.Substring(4, 4), out int fine))
                return -1;

            int nuovoInizio = inizio + anniDaAggiungere;
            int nuovoFine = fine + anniDaAggiungere;
            string nuovoAnno = $"{nuovoInizio}{nuovoFine}";

            string annoRifPulito = annoRiferimento?.Replace("/", "").Trim() ?? "";

            return nuovoAnno == annoRifPulito ? anniDaAggiungere : -1;
        }
    }
}
