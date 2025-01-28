using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcedureNet7.Storni
{
    internal class ProceduraStorni : BaseProcedure<ArgsProceduraStorni>
    {
        public ProceduraStorni(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        string selectedStorniFile = string.Empty;
        string esercizioFinanziario = string.Empty;
        SqlTransaction sqlTransaction;
        List<Studente> studenti = new List<Studente>();
        List<Studente> studentiDaBloccare = new List<Studente>();
        List<Studente> studentiRimossi = new List<Studente>();
        List<string> codFiscaliConErrori = new List<string>();
        public override void RunProcedure(ArgsProceduraStorni args)
        {
            try
            {
                _masterForm.inProcedure = true;

                esercizioFinanziario = args._esercizioFinanziario;
                selectedStorniFile = args._selectedFile;
                sqlTransaction = CONNECTION.BeginTransaction();
                DataTable dataTable = Utilities.ReadExcelToDataTable(selectedStorniFile);

                foreach (DataRow row in dataTable.Rows)
                {
                    string codFiscale = row["CODICE FISCALE"].ToString();
                    string IBAN = IBANHunter(row["MOTIVO STORNO"].ToString());
                    string numMandato = row["mandato"].ToString();
                    string impegnoRentroito = row["IMPEGNO dI provenienza"].ToString();

                    if (string.IsNullOrEmpty(codFiscale) || string.IsNullOrEmpty(numMandato) || string.IsNullOrEmpty(IBAN) || string.IsNullOrEmpty(impegnoRentroito))
                    {
                        continue;
                    }

                    Studente studente = new Studente(codFiscale, IBAN, numMandato, impegnoRentroito);
                    studenti.Add(studente);
                }

                Logger.LogInfo(30, $"Lavorazione studenti");
                List<string> codFiscali = studenti.Select(studente => studente.codFiscale).ToList();

                string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                Logger.LogInfo(30, $"Lavorazione studenti - creazione tabella codici fiscali");
                for (int i = 0; i < codFiscali.Count; i += 1000)
                {
                    var batch = codFiscali.Skip(i).Take(1000);
                    var insertQuery = "INSERT INTO #CFEstrazione (Cod_fiscale) VALUES " + string.Join(", ", batch.Select(cf => $"('{cf}')"));
                    SqlCommand insertCmd = new(insertQuery, CONNECTION, sqlTransaction);
                    insertCmd.ExecuteNonQuery();
                }

                string queryCheckIBAN = $@"
                        SELECT vMODALITA_PAGAMENTO.Cod_fiscale, IBAN 
                        FROM vMODALITA_PAGAMENTO INNER JOIN
                            #CFEstrazione AS CF ON vMODALITA_PAGAMENTO.cod_fiscale = CF.cod_fiscale
                        WHERE Data_fine_validita IS NULL
                    ";

                SqlCommand readData = new(queryCheckIBAN, CONNECTION, sqlTransaction);
                Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper();
                        Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                        if (studente != null)
                        {

                            string IBAN = Utilities.SafeGetString(reader, "IBAN");

                            if (studente.IBAN != IBAN)
                            {
                                continue;
                            }

                            if (studentiDaBloccare.Contains(studente))
                            {
                                continue;
                            }
                            studentiDaBloccare.Add(studente);
                        }
                        else
                        {
                            codFiscaliConErrori.Add(codFiscale);
                        }
                    }
                }


                string sqlMappingTable = $@"
                        CREATE TABLE #MappingTable
                        (
                            CodFiscale CHAR(16),
                            NumMandato VARCHAR(10),
                            impReintroito VARCHAR(10),
                            IBAN_Storno VARCHAR(50)
                        )";

                SqlCommand mappingTableCmd = new(sqlMappingTable, CONNECTION, sqlTransaction);
                mappingTableCmd.ExecuteNonQuery();

                using (SqlCommand cmd = new SqlCommand())
                {
                    cmd.Connection = CONNECTION;
                    cmd.CommandType = CommandType.Text;
                    cmd.Transaction = sqlTransaction;

                    foreach (Studente studente in studenti)
                    {
                        cmd.CommandText = "INSERT INTO #MappingTable (CodFiscale, NumMandato, impReintroito, IBAN_Storno) VALUES (@CodFiscale, @NumMandato, @impReintroito, @IbanStorno)";

                        cmd.Parameters.Clear();

                        cmd.Parameters.AddWithValue("@CodFiscale", studente.codFiscale);
                        cmd.Parameters.AddWithValue("@NumMandato", studente.mandatoPagamento);
                        cmd.Parameters.AddWithValue("@impReintroito", studente.impegnoReintroito);
                        cmd.Parameters.AddWithValue("@IbanStorno", studente.IBAN);

                        cmd.ExecuteNonQuery();
                    }
                }

                string queryAddStudenteAA = $@" 
                    SELECT Pagamenti.Anno_accademico, Domanda.cod_fiscale
                    FROM Pagamenti INNER JOIN
                    Domanda ON Pagamenti.anno_accademico = Domanda.anno_accademico AND Pagamenti.num_domanda = Domanda.num_domanda INNER JOIN
                    #MappingTable AS MT ON Domanda.cod_fiscale = MT.CodFiscale AND Pagamenti.cod_mandato = MT.NumMandato
                    WHERE pagamenti.ese_finanziario = '{esercizioFinanziario}'
                    ";

                SqlCommand studenteAA = new(queryAddStudenteAA, CONNECTION, sqlTransaction);
                Logger.LogInfo(12, $"Lavorazione studenti - aggiunta anno accademico");
                using (SqlDataReader reader = studenteAA.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper();
                        Studente? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                        if (studente != null)
                        {
                            string annoAccademico = Utilities.SafeGetString(reader, "Anno_accademico");
                            studente.SetStudenteAA(annoAccademico);
                        }
                        else
                        {
                            codFiscaliConErrori.Add(codFiscale);
                        }
                    }
                }


                foreach (Studente studente in studenti)
                {
                    if (
                        string.IsNullOrWhiteSpace(studente.studenteAA) ||
                        string.IsNullOrWhiteSpace(studente.mandatoPagamento) ||
                        string.IsNullOrWhiteSpace(studente.impegnoReintroito)
                    )
                    {
                        studentiRimossi.Add(studente);
                    }
                }

                studenti.RemoveAll(studentiRimossi.Contains);

                string sqlUpdatePagam = $@"
                    UPDATE Pagamenti
                    SET Ritirato_azienda = '1',
                        data_reintroito = '{DateTime.Now.ToString("dd/MM/yyyy")}',
                        utente_reintroito = 'Area 4',
                        impegno_reintroito = MT.impReintroito,
                        causale_reintroito = 'Transazione non possibile',
                        IBAN_Storno = MT.IBAN_Storno
                    FROM Pagamenti INNER JOIN 
                    Domanda ON Pagamenti.anno_accademico = Domanda.anno_accademico AND Pagamenti.num_domanda = Domanda.num_domanda INNER JOIN
                    #MappingTable AS MT ON Domanda.cod_fiscale = MT.CodFiscale AND Pagamenti.cod_mandato = MT.NumMandato
                    WHERE pagamenti.ese_finanziario = '{esercizioFinanziario}'
                    ";

                SqlCommand updatepagamCmd = new(sqlUpdatePagam, CONNECTION, sqlTransaction);
                updatepagamCmd.ExecuteNonQuery();

                AddBlocks(CONNECTION);

                string dropTempTable = "DROP TABLE #CFEstrazione; DROP TABLE #MappingTable;";
                SqlCommand dropCmd = new(dropTempTable, CONNECTION, sqlTransaction);
                _ = dropCmd.ExecuteNonQuery();

                _masterForm.inProcedure = false;
                sqlTransaction.Commit();
                Logger.LogInfo(100, $"Fine lavorazione");
                Thread.Sleep(10);
                Logger.LogInfo(100, $"Lavorati {studenti.Count} studenti");
                Thread.Sleep(10);
                Logger.LogInfo(100, $"Di cui {studentiDaBloccare.Count} bloccati per IBAN");
                Thread.Sleep(10);
                foreach (Studente studente in studentiDaBloccare)
                {
                    Logger.LogInfo(100, $"CF bloccato: {studente.codFiscale}");
                    Thread.Sleep(10);
                }
                Logger.LogInfo(100, $"Rimossi {studentiRimossi.Count} per mancanza di dati/pagamento non inserito");
                Thread.Sleep(10);
                foreach (Studente studente in studentiRimossi)
                {
                    Logger.LogInfo(100, $"CF rimosso: {studente.codFiscale}");
                    Thread.Sleep(10);
                }
                Logger.LogInfo(100, $"Rilevati {codFiscaliConErrori.Count} cod fiscale con errori");
                Thread.Sleep(10);
                foreach (string studente in codFiscaliConErrori)
                {
                    Logger.LogInfo(100, $"CF errore: {studente}");
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(100, $"Errore: {ex.Message}");
                sqlTransaction.Rollback();
                _masterForm.inProcedure = false;
            }
        }

        private void AddBlocks(SqlConnection CONNECTION)
        {
            try
            {
                var groupedByAnnoAccademico = studentiDaBloccare.GroupBy(s => s.studenteAA);

                foreach (var group in groupedByAnnoAccademico)
                {
                    List<string> codFiscaleMainList = group.Select(s => s.codFiscale).ToList();
                    BlocksUtil.AddBlock(CONNECTION, sqlTransaction, codFiscaleMainList, "BSS", group.Key, "IBANStorni", true);
                }
            }
            catch
            {
                throw;
            }
        }

        public static string IBANHunter(string text)
        {
            string pattern = @"\b[A-Z]{2}\d{2}[A-Z0-9]{0,30}\b";

            Regex regex = new Regex(pattern);

            Match match = regex.Match(text);

            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }
    }
}
