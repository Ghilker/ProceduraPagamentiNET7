using ClosedXML.Excel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed record RigaFlusso(int NumeroRiga, string CodiceFiscale,
        decimal Lordo, decimal Reversali, decimal Netto, string Impegno);

    // Import/export isolato dal form e dal database per poterlo verificare.
    internal static class GeneratoreFlussiFile
    {
        private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");
        private static readonly UTF8Encoding Utf8 = new(false);

        internal static List<RigaFlusso> LeggiExcel(string path, List<string> errori)
        {
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.FirstOrDefault();
            var righe = new List<RigaFlusso>();
            var range = sheet?.RangeUsed(XLCellsUsedOptions.Contents);
            if (sheet == null || range == null)
            {
                errori.Add("Il primo foglio Excel è vuoto.");
                return righe;
            }
            var colonne = new Dictionary<string, int>();
            for (int c = range.RangeAddress.FirstAddress.ColumnNumber; c <= range.RangeAddress.LastAddress.ColumnNumber; c++)
            {
                string header = Regex.Replace(sheet.Cell(1, c).GetString().Trim().ToLowerInvariant(), @"[\s_]", "");
                header = header switch
                {
                    "codfiscale" or "cf" => "codicefiscale",
                    "lordo" => "totalelordo",
                    "netto" => "importonetto",
                    _ => header
                };
                if (header.Length > 0 && !colonne.TryAdd(header, c))
                    errori.Add($"Intestazione duplicata: {sheet.Cell(1, c).GetString()}.");
            }
            foreach (string richiesta in new[] { "codicefiscale", "totalelordo", "reversali", "importonetto" })
                if (!colonne.ContainsKey(richiesta))
                    errori.Add($"Colonna obbligatoria mancante: {richiesta}. Usare il modello scaricabile.");
            if (errori.Count > 0) return righe;

            bool usaImpegno = colonne.TryGetValue("impegno", out int colImpegno) &&
                sheet.Column(colImpegno).CellsUsed(XLCellsUsedOptions.Contents)
                    .Any(c => c.Address.RowNumber > 1 && !string.IsNullOrWhiteSpace(c.GetString()));
            var chiavi = new Dictionary<(string Cf, string Impegno), int>();
            for (int r = 2; r <= range.RangeAddress.LastAddress.RowNumber; r++)
            {
                if (sheet.Row(r).CellsUsed(XLCellsUsedOptions.Contents).All(c => string.IsNullOrWhiteSpace(c.GetString())))
                    continue;
                int erroriPrima = errori.Count;
                string cf = sheet.Cell(r, colonne["codicefiscale"]).GetString().Trim().ToUpperInvariant();
                if (!Regex.IsMatch(cf, @"^[A-Z0-9]{16}$"))
                    errori.Add($"Riga {r}: codice fiscale assente o non composto da 16 caratteri alfanumerici.");
                string impegno = usaImpegno
                    ? sheet.Cell(r, colImpegno).GetFormattedString(CultureInfo.InvariantCulture).Trim() : "";
                if (usaImpegno && impegno.Length == 0)
                    errori.Add($"Riga {r}: compilare l'impegno su tutte le righe oppure lasciarlo tutto vuoto per generare un flusso unico.");
                if (impegno.Any(char.IsControl))
                    errori.Add($"Riga {r}: l'impegno contiene caratteri di controllo.");
                if (!chiavi.TryAdd((cf, impegno), r))
                    errori.Add($"Riga {r}: CF {cf} duplicato nello stesso impegno (prima occorrenza: riga {chiavi[(cf, impegno)]}).");
                decimal lordo = LeggiImporto(sheet.Cell(r, colonne["totalelordo"]), "Totale lordo", r, false, errori);
                decimal reversali = LeggiImporto(sheet.Cell(r, colonne["reversali"]), "Reversali", r, true, errori);
                decimal netto = LeggiImporto(sheet.Cell(r, colonne["importonetto"]), "Importo netto", r, false, errori);
                if (errori.Count == erroriPrima && lordo - reversali != netto)
                    errori.Add($"Riga {r}: il netto deve essere uguale a lordo meno reversali.");
                if (errori.Count == erroriPrima)
                    righe.Add(new RigaFlusso(r, cf, lordo, reversali, netto, impegno));
            }
            if (righe.Count == 0 && errori.Count == 0)
                errori.Add("Il foglio non contiene righe da elaborare.");
            return righe;
        }

        private static decimal LeggiImporto(IXLCell cell, string nome, int riga, bool vuotoComeZero, List<string> errori)
        {
            string testo = cell.GetString().Trim();
            if (testo.Length == 0 && vuotoComeZero) return 0m;
            decimal valore = 0;
            bool valido;
            if (cell.DataType == XLDataType.Number)
                valido = cell.TryGetValue(out valore);
            else
                // Testo italiano senza migliaia: evita interpretazioni ambigue.
                valido = Regex.IsMatch(testo, @"^\d+(,\d{1,2})?$") &&
                    decimal.TryParse(testo, NumberStyles.AllowDecimalPoint, Italiano, out valore);
            if (!valido || valore < 0 || decimal.Round(valore, 2) != valore)
            {
                errori.Add($"Riga {riga}: {nome} non valido. Usare un numero non negativo con massimo due decimali (testo: 1234,56 senza separatori delle migliaia).");
                return 0m;
            }
            return valore;
        }

        internal static string[] CreaCampi(RigaFlusso riga, DataRow dati, List<string> errori)
        {
            string Leggi(string campo) => dati[campo] == DBNull.Value ? "" : dati[campo].ToString()!.Trim();
            string Richiesto(string campo)
            {
                string valore = Leggi(campo);
                if (valore.Length == 0)
                    errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: {campo} mancante nel database.");
                return valore;
            }
            string iban = Regex.Replace(Leggi("IBAN"), @"\s", "").ToUpperInvariant();
            bool girocontoInterno = riga.Lordo == riga.Reversali && riga.Netto == 0m;
            if (!girocontoInterno && !IbanValidatorUtil.ValidateIban(iban))
                errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: IBAN assente o non valido.");
            string swift = Regex.Replace(Leggi("Swift"), @"\s", "").ToUpperInvariant();
            if (!girocontoInterno && swift.Length > 0 && !Regex.IsMatch(swift, @"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$"))
                errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: codice SWIFT non valido.");
            string provincia = Richiesto("provincia_residenza").ToUpperInvariant();
            bool estero = provincia == "EE";
            string cap = estero ? "00000" : Richiesto("CAP");
            // I CAP memorizzati come numeri possono perdere gli zeri iniziali (4010 -> 04010).
            if (!estero && Regex.IsMatch(cap, @"^[0-9]{1,5}$"))
                cap = cap.PadLeft(5, '0');
            if (!estero && !Regex.IsMatch(cap, @"^[0-9]{5}$"))
                errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: CAP letto dal database '{cap}': formato non valido (attese 5 cifre).");
            string dataNascita = "";
            if (dati["Data_nascita"] is DateTime data && data > DateTime.MinValue && data.Date <= DateTime.Today)
                dataNascita = data.ToString("ddMMyyyy", CultureInfo.InvariantCulture);
            else
                errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: data di nascita assente o non valida.");
            string sesso = Richiesto("Sesso").ToUpperInvariant();
            if (sesso != "M" && sesso != "F")
                errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: sesso diverso da M/F.");
            return new[]
            {
                "", riga.CodiceFiscale, Richiesto("Cognome"), Richiesto("Nome"),
                Euro(riga.Lordo), Euro(riga.Reversali), Euro(riga.Netto), "1", iban, swift,
                estero ? "0" : "1", estero ? Richiesto("INDIRIZZO").Replace("//", "-") : Richiesto("INDIRIZZO"),
                Richiesto("COD_COMUNE"), provincia, cap, Richiesto("Comune_residenza"), sesso, dataNascita,
                Richiesto("Comune_nascita"), Richiesto("Cod_comune_nasc"), Richiesto("Provincia_nascita"),
                "", "", "", "", "", Leggi("indirizzo_e_mail"), "", Leggi("telefono_cellulare")
            }.Select(PulisciCampo).ToArray();
        }

        private static string PulisciCampo(string valore) =>
            Regex.Replace(valore.Replace(';', ' '), @"\s+", " ").Trim();

        private static string Euro(decimal valore) => valore.ToString("0.00", Italiano);

        internal static void CreaModello(string path)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Modello");
            string[] headers = { "Codice fiscale", "Totale lordo", "Reversali", "Importo netto", "Impegno" };
            for (int i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightBlue;
            sheet.Column(1).Style.NumberFormat.Format = "@";
            sheet.Column(5).Style.NumberFormat.Format = "@";
            sheet.Columns(2, 4).Style.NumberFormat.Format = "0.00";
            sheet.Columns(1, 5).Width = 23;
            sheet.SheetView.FreezeRows(1);
            var istruzioni = workbook.Worksheets.Add("Istruzioni");
            string[] note =
            {
                "Compilare il foglio Modello: le intestazioni sono obbligatorie, l'ordine delle colonne è libero.",
                "Importi numerici non negativi con massimo due decimali. Se scritti come testo: 1234,56 senza separatori delle migliaia.",
                "Reversali vuote = zero. Importo netto = Totale lordo - Reversali. Sono ammesse righe con netto zero.",
                "Impegno facoltativo: tutto vuoto = flusso unico; se usato, deve essere compilato in ogni riga.",
                "Un CF può comparire su impegni diversi; i duplicati CF/impegno sono bloccati. Le righe vuote vengono ignorate.",
                "Vengono verificati anagrafica e residenza per studenti con domanda LZ. I controlli IBAN e SWIFT sono esclusi per i giroconti con lordo uguale a reversale e netto zero."
              
            };
            for (int i = 0; i < note.Length; i++) istruzioni.Cell(i + 1, 1).Value = note[i];
            istruzioni.Column(1).Width = 110;
            istruzioni.Column(1).Style.Alignment.WrapText = true;
            istruzioni.Rows(1, note.Length).Height = 32;
            workbook.SaveAs(path);
        }

        internal static string ScriviErrori(string cartella, List<string> errori)
        {
            Directory.CreateDirectory(cartella);
            string path = Path.Combine(cartella, $"errori_generatore_{Guid.NewGuid():N}.txt");
            using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew), Utf8);
            writer.WriteLine("GENERAZIONE BLOCCATA - Nessun flusso pubblicato.");
            foreach (string errore in errori) writer.WriteLine(errore);
            return path;
        }

        internal static string ScriviFlussi(string cartella, List<(RigaFlusso Riga, string[] Campi)> flussi)
        {
            if (flussi.Count == 0) throw new InvalidDataException("Nessuna riga da esportare.");
            string root = Path.GetFullPath(cartella);
            Directory.CreateDirectory(root);
            string nomeCartella = "flussi_" + DateTime.Now.ToString("dd_MM_yyyy", CultureInfo.InvariantCulture);
            string destinazione = Path.Combine(root, nomeCartella);
            if (Directory.Exists(destinazione) || File.Exists(destinazione))
                throw new IOException($"La destinazione {destinazione} esiste già. Selezionare un'altra cartella di salvataggio oppure rinominare quella esistente.");
            string temporanea = Path.Combine(root, $".generazione_{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(temporanea);
            try
            {
                var riepilogo = new List<string> { "File;Impegno;Righe;Totale lordo;Reversali;Importo netto" };
                int indice = 0;
                foreach (var gruppo in flussi.GroupBy(f => f.Riga.Impegno).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    string nomeSicuro = Regex.Replace(gruppo.Key, @"[^\p{L}\p{N}_-]", "_");
                    if (nomeSicuro.Length > 60) nomeSicuro = nomeSicuro[..60];
                    string nome = gruppo.Key.Length == 0 ? "flusso_generato.txt" : $"flusso_generato_imp_{++indice:D3}_{nomeSicuro}.txt";
                    using (var writer = new StreamWriter(Path.Combine(temporanea, nome), false, Utf8))
                    {
                        writer.NewLine = "\r\n";
                        int progressivo = 0;
                        foreach (var voce in gruppo.OrderBy(f => f.Riga.NumeroRiga))
                        {
                            if (voce.Campi.Length != 29) throw new InvalidDataException("Tracciato diverso da 29 campi.");
                            var campi = voce.Campi.Select(PulisciCampo).ToArray();
                            campi[0] = (++progressivo).ToString(CultureInfo.InvariantCulture);
                            writer.WriteLine(string.Join(";", campi));
                        }
                    }
                    riepilogo.Add(string.Join(";", nome, PulisciCampo(gruppo.Key), gruppo.Count(),
                        Euro(gruppo.Sum(f => f.Riga.Lordo)), Euro(gruppo.Sum(f => f.Riga.Reversali)), Euro(gruppo.Sum(f => f.Riga.Netto))));
                }
                riepilogo.Add(string.Join(";", "TOTALE", "", flussi.Count,
                    Euro(flussi.Sum(f => f.Riga.Lordo)), Euro(flussi.Sum(f => f.Riga.Reversali)), Euro(flussi.Sum(f => f.Riga.Netto))));
                File.WriteAllLines(Path.Combine(temporanea, "riepilogo.csv"), riepilogo, Utf8);
                var dettaglio = new List<string> { "Riga Excel;Codice fiscale;Impegno;Totale lordo;Reversali;Importo netto;Esito" };
                dettaglio.AddRange(flussi.OrderBy(f => f.Riga.NumeroRiga).Select(f => string.Join(";",
                    f.Riga.NumeroRiga, f.Riga.CodiceFiscale, PulisciCampo(f.Riga.Impegno),
                    Euro(f.Riga.Lordo), Euro(f.Riga.Reversali), Euro(f.Riga.Netto), "ESPORTATA")));
                File.WriteAllLines(Path.Combine(temporanea, "dettaglio.csv"), dettaglio, Utf8);
                // Pubblicazione della cartella solo dopo la scrittura di tutti i file.
                Directory.Move(temporanea, destinazione);
                return destinazione;
            }
            catch
            {
                // Solo la cartella temporanea di questa esecuzione, mai output pubblicati.
                // Un errore di pulizia non deve nascondere l'errore originale di scrittura.
                try { Directory.Delete(temporanea, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
