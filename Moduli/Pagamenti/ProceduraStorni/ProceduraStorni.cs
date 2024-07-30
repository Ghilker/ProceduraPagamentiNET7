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
                    AddBlock(CONNECTION, codFiscaleMainList, group.Key);
                }
            }
            catch
            {
                throw;
            }
        }

        private void AddBlock(SqlConnection CONNECTION, List<string> codFiscaleMainList, string annoAccademico)
        {
            try
            {
                string utente = "IBANStorni";

                string codFiscaleList = string.Join(",", codFiscaleMainList.ConvertAll(c => $"'{c}'"));

                string sql = $@"
                DECLARE @Cod_tipologia_blocco char(3);
                DECLARE @anno_accademico char(8);
                DECLARE @utente varchar(20);

                SET @Cod_tipologia_blocco = 'BSS';
                SET @anno_accademico = '{annoAccademico}';
                SET @utente = '{utente}';

                INSERT INTO Motivazioni_blocco_pagamenti
                    (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
                        Data_validita, Utente, Data_fine_validita, Utente_sblocco)
                SELECT Anno_accademico, Num_domanda, @Cod_tipologia_blocco, '1' AS Expr2, 
                        CURRENT_TIMESTAMP, @utente, NULL AS Expr5, NULL AS Expr6
                FROM Domanda
                WHERE Anno_accademico = @anno_accademico 
                    AND tipo_bando IN ('lz', 'l2') 
                    AND Cod_fiscale IN ({codFiscaleList})
                    AND Num_domanda NOT IN
                        (SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti AS Motivazioni_blocco_pagamenti_1
                            WHERE Anno_accademico = @anno_accademico 
                                AND Cod_tipologia_blocco = @Cod_tipologia_blocco
				                and Data_fine_validita is null)


	            INSERT INTO [DatiGenerali_dom] ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort], [RichiestaMensa])
	            SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, @utente, vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 1, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort, vDATIGENERALI_dom.RichiestaMensa 
	            FROM 
		            Domanda INNER JOIN vDATIGENERALI_dom ON Domanda.Anno_accademico = vDATIGENERALI_dom.Anno_accademico AND 
		            Domanda.Num_domanda = vDATIGENERALI_dom.Num_domanda 
	            WHERE 
		            (Domanda.Anno_accademico = @anno_accademico) and 
		            tipo_bando in ('lz','l2') AND 
                    Cod_fiscale IN ({codFiscaleList}) and
		            Domanda.Num_domanda not in (SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @anno_accademico 
                                AND Data_fine_validita IS not NULL 
                                AND Blocco_pagamento_attivo = 1)
            ";

                using SqlCommand command = new(sql, CONNECTION, sqlTransaction);
                _ = command.ExecuteNonQuery();
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
