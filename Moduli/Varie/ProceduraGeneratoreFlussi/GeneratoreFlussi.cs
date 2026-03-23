using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal class ProceduraGeneratoreFlussi : BaseProcedure<ArgsProceduraGeneratoreFlussi>
    {
        public ProceduraGeneratoreFlussi(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection) { }

        public override void RunProcedure(ArgsProceduraGeneratoreFlussi args)
        {
            try
            {

                var excelLookup = LeggiExcel(args.FilePath);


                if (excelLookup.Count == 0)
                    return;

                var studenti = CaricaStudentiDaDb(excelLookup.Keys.ToList(), excelLookup);

                var flusso = GenerareFlussoDataTable(studenti);

                if (flusso.Rows.Count > 0)
                    Utilities.WriteDataTableToTextFile(flusso, args.FolderPath, "flusso_generato");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, ex.Message);
            }
        }

        // ===============================
        // LETTURA EXCEL → DICTIONARY O(1)
        // ===============================
        private Dictionary<string, DataRow> LeggiExcel(string path)
        {
            DataTable records = Utilities.ReadExcelToDataTable(path);

            return records.AsEnumerable()
                .Where(r => r[0] != null)
                .ToDictionary(
                    r => r[0]!.ToString()!.Trim().ToUpperInvariant(),
                    r => r
                );
        }

        // ===============================
        // QUERY PARAMETRIZZATA SICURA
        // ===============================
        private List<StudentePagamenti> CaricaStudentiDaDb(
            List<string> codiciFiscali,
            Dictionary<string, DataRow> excelLookup)
        {
            List<StudentePagamenti> studenti = new();

            if (codiciFiscali.Count == 0)
                return studenti;

            // Parametri dinamici sicuri
            var parameters = codiciFiscali
                .Select((cf, i) => $"@cf{i}")
                .ToList();

            string sql = $@"
                WITH UltimaResidenza AS (
                    SELECT *, 
                           ROW_NUMBER() OVER (PARTITION BY cod_fiscale ORDER BY anno_accademico DESC) AS rn 
                    FROM vResidenza
                )
                SELECT DISTINCT
                    Domanda.Cod_fiscale, 
                    Studente.Cognome,
                    Studente.Nome, 
                    vMODALITA_PAGAMENTO.IBAN,
                    vMODALITA_PAGAMENTO.Swift, 
                    vResidenza.provincia_residenza,     
                    vResidenza.INDIRIZZO, 
                    vResidenza.COD_COMUNE,
                    vResidenza.CAP, 
                    Comuni.Descrizione AS Comune_residenza, 
                    Studente.Sesso,
                    Studente.Data_nascita, 
                    Comuni_1.Descrizione AS Comune_nascita, 
                    Studente.Cod_comune_nasc, 
                    Comuni_1.Cod_provincia AS Provincia_nascita, 
                    Studente.indirizzo_e_mail,
                    Studente.telefono_cellulare
                FROM Domanda 
                INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                INNER JOIN vMODALITA_PAGAMENTO ON Studente.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                INNER JOIN UltimaResidenza vResidenza 
                    ON Studente.Cod_fiscale = vResidenza.COD_FISCALE AND vResidenza.rn = 1
                INNER JOIN Comuni ON vResidenza.COD_COMUNE = Comuni.Cod_comune 
                INNER JOIN Comuni AS Comuni_1 ON Studente.Cod_comune_nasc = Comuni_1.Cod_comune
                WHERE Domanda.cod_fiscale IN ({string.Join(",", parameters)})
                  AND Domanda.Tipo_bando = 'LZ';
            ";

            using SqlCommand cmd = new SqlCommand(sql, CONNECTION);

            for (int i = 0; i < codiciFiscali.Count; i++)
                cmd.Parameters.AddWithValue(parameters[i], codiciFiscali[i]);

            using SqlDataReader reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                string cf = Utilities.SafeGetString(reader, "cod_fiscale")
                    .Trim().ToUpperInvariant();

                if (!excelLookup.TryGetValue(cf, out DataRow excelRow))
                    continue;

                studenti.Add(CreaStudente(reader, excelRow));
            }

            foreach (var excelCF in excelLookup)
            {
                string CF = excelCF.Key;
                if (studenti.Any(studente => studente.InformazioniPersonali.CodFiscale == CF))
                    continue;
                else
                    Logger.LogWarning(null, $"Studente con CF non trovato: {CF}");

            }


            return studenti;
        }

        // ===============================
        // MAPPING OGGETTO
        // ===============================
        private StudentePagamenti CreaStudente(SqlDataReader reader, DataRow excelRow)

        {
            var culturaIt = new CultureInfo("it-IT");

            double.TryParse(excelRow[1]?.ToString(), NumberStyles.Any, culturaIt, out double lordo);
            double.TryParse(excelRow[2]?.ToString(), NumberStyles.Any, culturaIt, out double reversali);
            double.TryParse(excelRow[3]?.ToString(), NumberStyles.Any, culturaIt, out double netto);

            long.TryParse(Utilities.SafeGetString(reader, "telefono_cellulare"), out long telefono);

            return new StudentePagamenti
            {
                InformazioniPersonali = new InformazioniPersonali
                {
                    CodFiscale = Utilities.SafeGetString(reader, "cod_fiscale"),
                    Cognome = Utilities.SafeGetString(reader, "cognome"),
                    Nome = Utilities.SafeGetString(reader, "nome"),
                    DataNascita = Utilities.SafeGetDateTime(reader, "data_nascita").ToString("dd/MM/yyyy") ?? string.Empty,
                    Sesso = Utilities.SafeGetString(reader, "sesso"),
                    IndirizzoEmail = Utilities.SafeGetString(reader, "indirizzo_e_mail"),
                    Telefono = telefono
                },
                InformazioniPagamento = new InformazioniPagamento
                {
                    ImportoDaPagareLordo = lordo,
                    ImportoReversale = reversali,
                    ImportoDaPagare = netto
                },
                InformazioniConto = new InformazioniConto
                {
                    IBAN = Utilities.SafeGetString(reader, "IBAN"),
                    Swift = Utilities.SafeGetString(reader, "Swift")
                },
                InformazioniSede = new InformazioniSede
                {
                    Residenza = new Residenza
                    {
                        indirizzo = Utilities.SafeGetString(reader, "INDIRIZZO"),
                        CAP = Utilities.SafeGetString(reader, "CAP"),
                        provincia = Utilities.SafeGetString(reader, "provincia_residenza"),
                        nomeComune = Utilities.SafeGetString(reader, "Comune_residenza"),
                        codComune = Utilities.SafeGetString(reader, "COD_COMUNE")
                    }
                }
            };
        }

        // ===============================
        // GENERAZIONE FLUSSO
        // ===============================
        private DataTable GenerareFlussoDataTable(List<StudentePagamenti> studentiDaGenerare)
        {
            DataTable table = new();
            string[] columns = new string[]
            {
                "Incrementale", "Cod_fiscale", "Cognome", "Nome", "totale_lordo", "reversali", "importo_netto",
                "conferma_pagamento", "IBAN", "Istituto_bancario", "italiano", "indirizzo_residenza",
                "cod_catastale_residenza", "provincia_residenza", "cap_residenza", "nazione_citta_residenza",
                "sesso", "data_nascita", "luogo_nascita", "cod_catastale_luogo_nascita", "provincia_nascita",
                "vuoto1", "vuoto2", "vuoto3", "vuoto4", "vuoto5", "mail", "vuoto6", "telefono"
            };

            Type[] types = new Type[]
            {
                typeof(int), typeof(string), typeof(string), typeof(string), typeof(double), typeof(double), typeof(double),
                typeof(int), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(long)
            };

            for (int i = 0; i < columns.Length; i++)
                table.Columns.Add(columns[i], types[i]);

            int incrementale = 1;
            foreach (var studente in studentiDaGenerare)
            {
                int straniero = studente.InformazioniSede.Residenza.provincia == "EE" ? 0 : 1;
                string indirizzoResidenza = straniero == 0
                    ? studente.InformazioniSede.Residenza.indirizzo.Replace("//", "-")
                    : studente.InformazioniSede.Residenza.indirizzo;

                string capResidenza = straniero == 0 ? "00000" : studente.InformazioniSede.Residenza.CAP;
                string dataSenzaSlash = studente.InformazioniPersonali.DataNascita.Replace("/", "");

                table.Rows.Add(
                    incrementale++,
                    studente.InformazioniPersonali.CodFiscale,
                    studente.InformazioniPersonali.Cognome,
                    studente.InformazioniPersonali.Nome,
                    studente.InformazioniPagamento.ImportoDaPagareLordo,
                    studente.InformazioniPagamento.GeneratoreFlussoReversaleNONLOTOCCAREGIACOMOTIAMMAZZO,
                    studente.InformazioniPagamento.ImportoDaPagare,
                    1,
                    studente.InformazioniConto.IBAN,
                    studente.InformazioniConto.Swift,
                    straniero,
                    indirizzoResidenza,
                    studente.InformazioniSede.Residenza.codComune,
                    studente.InformazioniSede.Residenza.provincia,
                    capResidenza,
                    studente.InformazioniSede.Residenza.nomeComune,
                    studente.InformazioniPersonali.Sesso,
                    dataSenzaSlash,
                    studente.InformazioniPersonali.LuogoNascita.nomeComune,
                    studente.InformazioniPersonali.LuogoNascita.codComune,
                    studente.InformazioniPersonali.LuogoNascita.provincia,
                    "", "", "", "", "",
                    studente.InformazioniPersonali.IndirizzoEmail,
                    "",
                    studente.InformazioniPersonali.Telefono
                );
            }

            return table;
        }
    }

}