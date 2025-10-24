using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using static System.ComponentModel.Design.ObjectSelectorEditor;

namespace ProcedureNet7
{
    public class RecordExcelGeneratoreFlussi
    {
        public string CodiceFiscale { get; set; } = string.Empty;
        public decimal TotaleLordo { get; set; }
        public decimal Reversali { get; set; }
        public decimal ImportoNetto { get; set; }
    }

    internal class ProceduraGeneratoreFlussi : BaseProcedure<ArgsProceduraGeneratoreFlussi>
    {
        public ProceduraGeneratoreFlussi(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        public override void RunProcedure(ArgsProceduraGeneratoreFlussi args)
        {
            string selectedFilePath = args.FilePath;
            string selectedFolderPath = args.FolderPath;

            DataTable records = Utilities.ReadExcelToDataTable(selectedFilePath);
            if (records.Rows.Count == 0)
                return;

            List<string> codiciFiscali = new List<string>();

            foreach (DataRow row in records.Rows) {
                codiciFiscali.Add(row[0].ToString());
            }

            string cfstring = string.Join(", ", codiciFiscali.Select(cf => $"'{cf}'"));

            List<StudentePagamenti> studentiDaGenerare = new();

            string sql = @"
                 WITH UltimaResidenza AS (SELECT *, ROW_NUMBER() OVER (PARTITION BY cod_fiscale ORDER BY anno_accademico DESC) AS rn FROM vResidenza)
                 SELECT distinct
                    Domanda.Cod_fiscale, 
                    Studente.Cognome,
                    Studente.Nome, 
                    vMODALITA_PAGAMENTO.IBAN,
                    vMODALITA_PAGAMENTO.Swift, 
                    vResidenza.provincia_residenza,     
                    vResidenza.INDIRIZZO, 
                    vResidenza.COD_COMUNE,
                    vResidenza.CAP, 
                    Comuni.Descrizione as Comune_residenza, 
                    Studente.Sesso,
                    Studente.Data_nascita, 
                    Comuni_1.Descrizione AS Comune_nascita, 
                    Studente.Cod_comune_nasc, 
                    Comuni_1.Cod_provincia as Provincia_nascita, 
                    studente.indirizzo_e_mail,
                    studente.telefono_cellulare
                FROM 
                    Domanda 
                INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                INNER JOIN vMODALITA_PAGAMENTO ON Studente.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                INNER JOIN UltimaResidenza vResidenza ON Studente.Cod_fiscale = vResidenza.COD_FISCALE AND vResidenza.rn = 1
                INNER JOIN Comuni ON vResidenza.COD_COMUNE = Comuni.Cod_comune 
                INNER JOIN Comuni AS Comuni_1 ON Studente.Cod_comune_nasc = Comuni_1.Cod_comune
                WHERE 
                    Domanda.cod_fiscale IN (" + cfstring + @") 
                    AND Domanda.Tipo_bando = 'LZ';
                ";

            using SqlCommand readData = new SqlCommand(sql, CONNECTION);

            using SqlDataReader reader = readData.ExecuteReader();
            while (reader.Read())
            {
                string codFiscaleEstratto = Utilities.SafeGetString(reader, "cod_fiscale");


                DataRow? recordRow = records.AsEnumerable().FirstOrDefault(r => r[0].ToString() == codFiscaleEstratto);

                if (recordRow == null)
                    continue;


                StudentePagamenti studente = new StudentePagamenti
                {
                    InformazioniPersonali = new InformazioniPersonali
                    {
                        CodFiscale = codFiscaleEstratto,
                        Cognome = Utilities.SafeGetString(reader, "cognome"),
                        Nome = Utilities.SafeGetString(reader, "nome"),
                        DataNascita = Utilities.SafeGetDateTime(reader, "data_nascita") is DateTime dataNascita ? dataNascita.ToString("dd/MM/yyyy") : string.Empty,
                        Sesso = Utilities.SafeGetString(reader, "sesso"),
                        LuogoNascita = new LuogoNascita { nomeComune = Utilities.SafeGetString(reader, "Comune_nascita"), 
                        codComune = Utilities.SafeGetString(reader, "Cod_comune_nasc"), 
                        provincia = Utilities.SafeGetString(reader, "Provincia_nascita") },
                        IndirizzoEmail = Utilities.SafeGetString(reader, "indirizzo_e_mail"),
                        Telefono = long.Parse(Utilities.SafeGetString(reader, "telefono_cellulare"))
                    },
                    InformazioniPagamento = new InformazioniPagamento
                    {
                        ImportoDaPagare = double.Parse(recordRow[3].ToString()),
                        ImportoDaPagareLordo = double.Parse(recordRow[1].ToString()),
                        GeneratoreFlussoReversaleNONLOTOCCAREGIACOMOTIAMMAZZO = double.Parse(recordRow[2].ToString())
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
                            codComune = Utilities.SafeGetString(reader, "COD_COMUNE"),
                        }
                    }
                };
                
                studentiDaGenerare.Add(studente);
            }

            DataTable flusso = GenerareFlussoDataTable(studentiDaGenerare);
            // Salvataggio su file opzionale
            if (flusso != null && flusso.Rows.Count > 0)
            {
                Utilities.WriteDataTableToTextFile(flusso, selectedFolderPath, $"flusso_generato");
            }
        }

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
                string indirizzoResidenza = straniero == 0 ? studente.InformazioniSede.Residenza.indirizzo.Replace("//", "-") : studente.InformazioniSede.Residenza.indirizzo;
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