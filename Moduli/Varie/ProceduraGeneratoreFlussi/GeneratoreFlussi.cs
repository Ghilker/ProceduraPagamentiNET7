using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal class ProceduraGeneratoreFlussi : BaseProcedure<ArgsProceduraGeneratoreFlussi>
    {
        public ProceduraGeneratoreFlussi(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection) { }

        public override void RunProcedure(ArgsProceduraGeneratoreFlussi args)
        {
            if (!File.Exists(args.FilePath))
                throw new FileNotFoundException("Il file Excel selezionato non esiste.", args.FilePath);
            if (string.IsNullOrWhiteSpace(args.FolderPath))
                throw new ArgumentException("Selezionare una cartella di destinazione.");

            var errori = new List<string>();
            var righe = GeneratoreFlussiFile.LeggiExcel(args.FilePath, errori);
            if (errori.Count > 0) Interrompi(args.FolderPath, errori);
            Logger.LogInfo(15, $"Acquisite {righe.Count} righe Excel. Verifica anagrafiche e conti...");
            var anagrafiche = CaricaStudentiDaDb(righe.Select(r => r.CodiceFiscale).Distinct().ToArray());
            var flussi = new List<(RigaFlusso Riga, string[] Campi)>();
            foreach (var riga in righe)
            {
                if (!anagrafiche.TryGetValue(riga.CodiceFiscale, out var candidati) || candidati.Count == 0)
                {
                    errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: studente non trovato o privo di domanda LZ.");
                    continue;
                }
                if (candidati.Count != 1)
                {
                    errori.Add($"Riga {riga.NumeroRiga}, CF {riga.CodiceFiscale}: più anagrafiche/conti/residenze disponibili; risolvere l'ambiguità nel database.");
                    continue;
                }
                flussi.Add((riga, GeneratoreFlussiFile.CreaCampi(riga, candidati[0], errori)));
            }
            if (errori.Count > 0) Interrompi(args.FolderPath, errori);
            Logger.LogInfo(75, "Validazione completata. Scrittura flussi e riepilogo...");
            string cartella = GeneratoreFlussiFile.ScriviFlussi(args.FolderPath, flussi);
            Logger.LogInfo(100, $"Generazione completata: {righe.Count} righe, netto {righe.Sum(r => r.Netto):N2}. File e riepilogo: {cartella}");
        }

        private static void Interrompi(string cartella, List<string> errori)
        {
            string report = GeneratoreFlussiFile.ScriviErrori(cartella, errori);
            throw new InvalidDataException($"Generazione bloccata: {errori.Count} anomalie. Nessun flusso pubblicato. Report: {report}");
        }

        private Dictionary<string, List<DataRow>> CaricaStudentiDaDb(string[] codiciFiscali)
        {
            if (CONNECTION?.State != ConnectionState.Open)
                throw new InvalidOperationException("Connessione al database non disponibile.");
            var risultato = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
            // Batch limitati e parametri tipizzati anche per fogli grandi.
            foreach (var lotto in codiciFiscali.Chunk(500))
            {
                string parametri = string.Join(",", lotto.Select((_, i) => $"@cf{i}"));
                string sql = $@"
                    WITH UltimaResidenza AS (
                        SELECT *, DENSE_RANK() OVER (
                            PARTITION BY cod_fiscale ORDER BY anno_accademico DESC) AS rn
                        FROM vResidenza WHERE cod_fiscale IN ({parametri})
                    )
                    SELECT DISTINCT s.Cod_fiscale, s.Cognome, s.Nome,
                        p.IBAN, p.Swift, r.provincia_residenza, r.INDIRIZZO,
                        r.COD_COMUNE, r.CAP, c.Descrizione AS Comune_residenza,
                        s.Sesso, s.Data_nascita, n.Descrizione AS Comune_nascita,
                        s.Cod_comune_nasc, n.Cod_provincia AS Provincia_nascita,
                        s.indirizzo_e_mail, s.telefono_cellulare
                    FROM Studente s
                    LEFT JOIN vMODALITA_PAGAMENTO p ON s.Cod_fiscale = p.Cod_fiscale
                    LEFT JOIN UltimaResidenza r ON s.Cod_fiscale = r.COD_FISCALE AND r.rn = 1
                    LEFT JOIN Comuni c ON r.COD_COMUNE = c.Cod_comune
                    LEFT JOIN Comuni n ON s.Cod_comune_nasc = n.Cod_comune
                    WHERE s.Cod_fiscale IN ({parametri})
                      AND EXISTS (SELECT 1 FROM Domanda d
                          WHERE d.Cod_fiscale = s.Cod_fiscale AND d.Tipo_bando = 'LZ');";
                using var cmd = new SqlCommand(sql, CONNECTION) { CommandTimeout = 120 };
                for (int i = 0; i < lotto.Length; i++)
                    cmd.Parameters.Add($"@cf{i}", SqlDbType.VarChar, 16).Value = lotto[i];
                using var reader = cmd.ExecuteReader();
                var table = new DataTable();
                table.Load(reader);
                foreach (DataRow row in table.Rows)
                {
                    string cf = row["Cod_fiscale"].ToString()!.Trim().ToUpperInvariant();
                    if (!risultato.TryGetValue(cf, out var righe))
                        risultato[cf] = righe = new List<DataRow>();
                    righe.Add(row);
                }
            }
            return risultato;
        }
    }
}
