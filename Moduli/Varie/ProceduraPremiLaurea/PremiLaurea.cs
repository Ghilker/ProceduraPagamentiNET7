using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;

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

            AggiungiColonnePremiLaurea(inputPath, outputPath);
        }

        private void AggiungiColonnePremiLaurea(string inputPath, string outputPath)
        {
            // --- 1️⃣ Lettura Excel come DataTable ---
            DataTable dt = Utilities.ReadExcelToDataTable(inputPath);
            if (dt == null || dt.Rows.Count == 0)
                throw new Exception("Il file Excel di input è vuoto o non leggibile.");

            // --- 2️⃣ Aggiunge le colonne di verifica ---
            string[] nuoveColonne = new[]
            {
                "SE Data",
                "SE Voto",
                "SE Lode",
                "SE Carriera pregressa",
                "SE domanda oltre termine",
                "SE anno di corso coerente",
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
            int idxAAImmDich = 9;
            int idxDataDich = 10;
            int idxVotoDich = 11;
            int idxLodeDich = 12;
            int idxAnnoCorsoDich = 13;
            int idxAAImmCert = 14;
            int idxDataCert = 15;
            int idxVotoCert = 16;
            int idxLodeCert = 17;
            int idxAnnoCorsoCert = 18;
            int idxAbbreviazione = 19;
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

                    // --- Calcolo SE ---
                    string seData = (dataDichiarato.HasValue && dataCertificato.HasValue &&
                                     dataDichiarato.Value.Date == dataCertificato.Value.Date) ? "OK" : "NO";

                    string seVoto = (votoDich.HasValue && votoCert.HasValue &&
                                     votoDich.Value == votoCert.Value) ? "OK" : "NO";

                    string seLode = (!string.IsNullOrEmpty(lodeDich) && !string.IsNullOrEmpty(lodeCert) &&
                                     string.Equals(lodeDich, lodeCert, StringComparison.OrdinalIgnoreCase)) ? "OK" : "NO";

                    string seCarrieraPregressa = string.IsNullOrEmpty(abbreviazione) ? "OK" : "NO";

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
                        row[idxAnnoCorsoCert] != null &&
                        row[idxAnnoCorsoCert] != DBNull.Value)
                    {
                        // Estrae il valore della cella come stringa e rimuove spazi/slash
                        string annoRiferimento = row[idxAnnoCorsoCert].ToString()!.Trim();

                        // Converte l'anno di immatricolazione in formato pulito
                        string aaImmPulito = aaImmCertStr.Replace("/", "").Trim();
                        // Converte l'anno di riferimento in formato pulito
                        string annoRiferimentoPulito = annoRiferimento.Replace("/", "").Trim();
                        // Verifica l'anno accademico senza sottrarre 1
                        int verifica = VerificaAnnoAccademico(aaImmPulito, annoCorsoDich.Value -1, annoRiferimentoPulito);

                        if (verifica != -1)
                            seAnnoCorso = "OK";
                    }

                    string seLavorabile = (seData == "OK" &&
                                           seVoto == "OK" &&
                                           seLode == "OK" &&
                                           seCarrieraPregressa == "OK" &&
                                           seDomandaOltreTermine == "OK" &&
                                           seAnnoCorso == "OK") ? "OK" : "NO";

                    // --- Scrive valori SE ---
                    row["SE Data"] = seData;
                    row["SE Voto"] = seVoto;
                    row["SE Lode"] = seLode;
                    row["SE Carriera pregressa"] = seCarrieraPregressa;
                    row["SE domanda oltre termine"] = seDomandaOltreTermine;
                    row["SE anno di corso coerente"] = seAnnoCorso;
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
            string baseDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;

            // Assicurati che la cartella esista
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            // Nome della sede
            string sedeStudi = dt.Rows.Count > 0 ? dt.Rows[0][idxSede]?.ToString() ?? "Sede" : "Sede";
            foreach (var c in Path.GetInvalidFileNameChars())
                sedeStudi = sedeStudi.Replace(c, '_');

            // Percorsi finali dei file
            string fileOK = Path.Combine(outputPath, $"PL 2324 {sedeStudi} validati massivamente.xlsx");
            string fileNO = Path.Combine(outputPath, $"PL 2324 {sedeStudi} validati manualmente.xlsx");

            // Esporta i DataTable
            Utilities.ExportDataTableToExcel(okTable, fileOK);
            Utilities.ExportDataTableToExcel(noTable, fileNO);
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
