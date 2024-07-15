using ProcedureNet7.ProceduraAllegatiSpace;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcedureNet7
{
    internal class ProceduraBlocchi : BaseProcedure<ArgsProceduraBlocchi>
    {
        public string _blocksYear = string.Empty;
        public string _blocksUsername = string.Empty;
        private readonly Dictionary<string, List<string>> blocksToRemove = new();
        private readonly Dictionary<string, List<string>> blocksToAdd = new();

        private List<string> codiciFiscaliTotali = new();
        private List<string> codiciFiscaliConErrori = new();

        public ProceduraBlocchi(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraBlocchi args)
        {
            string blocksFilePath = args._blocksFilePath;
            _blocksYear = args._blocksYear;
            _blocksUsername = args._blocksUsername;

            _masterForm.inProcedure = true;
            try
            {
                DataTable dataTable = Utilities.ReadExcelToDataTable(blocksFilePath);
                ProcessWorksheet(dataTable);
            }
            catch (Exception ex)
            {
                Logger.LogInfo(0, $"Error: {ex.Message}");
                _masterForm.inProcedure = false;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.LogInfo(100, $"Fine Lavorazione");
            }
        }

        private void ProcessWorksheet(DataTable dataTable)
        {
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                DataRow row = dataTable.Rows[i];
                string? nullableCodFiscale = row[0].ToString();

                if (nullableCodFiscale == null)
                {
                    Logger.LogWarning(null, $"Riga {i} con cella codice fiscale nulla");
                    continue;
                }

                string codFiscale = nullableCodFiscale;
                ProcessRowInMemory(codFiscale, row);
            }

            Logger.LogInfo(50, "Processata memoria");

            using SqlTransaction transaction = CONNECTION.BeginTransaction();
            try
            {
                // Process blocks to remove and add within the transaction
                ApplyBlocks(CONNECTION, transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.LogError(0, $"Transaction Error: {ex.Message}");
                transaction.Rollback();
                throw;
            }
        }

        private void ProcessRowInMemory(string codFiscale, DataRow data)
        {
            if (string.IsNullOrEmpty(data[1].ToString()) && string.IsNullOrEmpty(data[2].ToString()))
            {
                return;
            }

            // Process codFiscale values to remove
            if (!string.IsNullOrEmpty(data[1].ToString()))
            {
                foreach (string block in data[1].ToString().Split(';'))
                {
                    if (!blocksToRemove.TryGetValue(block, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToRemove[block] = value;
                    }

                    value.Add(codFiscale);
                }
            }

            // Process codFiscale values to add
            if (!string.IsNullOrEmpty(data[2].ToString()))
            {
                foreach (string block in data[2].ToString().Split(';'))
                {
                    if (!blocksToAdd.TryGetValue(block, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToAdd[block] = value;
                    }

                    value.Add(codFiscale);
                }
            }
        }

        private void ApplyBlocks(SqlConnection conn, SqlTransaction transaction)
        {
            foreach (string block in blocksToRemove.Keys)
            {
                try
                {
                    RemoveBlock(conn, transaction, blocksToRemove[block], block);
                    Logger.LogInfo(75, $"Processato blocco {block} da togliere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to remove: {block} - {ex.Message}");
                }
            }
            foreach (string block in blocksToAdd.Keys)
            {
                try
                {
                    AddBlock(conn, transaction, blocksToAdd[block], block);
                    Logger.LogInfo(75, $"Processato blocco {block} da mettere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to add: {block} - {ex.Message}");
                }
            }
        }

        private void AddBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode)
        {
            string annoAccademico = _blocksYear;
            string utente = _blocksUsername;

            string codFiscaleList = string.Join(",", codFiscaleCol.ConvertAll(c => $"'{c}'"));

            string sql = $@"
                DECLARE @Cod_tipologia_blocco char(3);
                DECLARE @anno_accademico char(8);
                DECLARE @utente varchar(20);

                SET @Cod_tipologia_blocco = @blockCode;
                SET @anno_accademico = @annoAcademico;
                SET @utente = @utenteValue;

                INSERT INTO dbo.Motivazioni_blocco_pagamenti
                    (Anno_accademico, Num_domanda, Cod_tipologia_blocco, Blocco_pagamento_attivo,
                        Data_validita, Utente, Data_fine_validita, Utente_sblocco)
                SELECT Anno_accademico, Num_domanda, @Cod_tipologia_blocco, '1' AS Expr2, 
                        CURRENT_TIMESTAMP, @utente, NULL AS Expr5, NULL AS Expr6
                FROM dbo.Domanda
                WHERE Anno_accademico = @anno_accademico 
                    AND tipo_bando IN ('lz', 'l2') 
                    AND Cod_fiscale IN ({codFiscaleList})
                    AND Num_domanda NOT IN
                        (SELECT DISTINCT Num_domanda
                            FROM dbo.Motivazioni_blocco_pagamenti AS Motivazioni_blocco_pagamenti_1
                            WHERE Anno_accademico = @anno_accademico 
                                AND Cod_tipologia_blocco = @Cod_tipologia_blocco
				                and Data_fine_validita is null)


	            INSERT INTO [DatiGenerali_dom] ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort], [RichiestaMensa])
	            SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, @utente, vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 1, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort , vDATIGENERALI_dom.RichiestaMensa 
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

            using SqlCommand command = new(sql, conn, transaction);
            command.Parameters.AddWithValue("@blockCode", blockCode);
            command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
            command.Parameters.AddWithValue("@utenteValue", utente);
            command.ExecuteNonQuery();
        }

        private void RemoveBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode)
        {
            string annoAccademico = _blocksYear;
            string utente = _blocksUsername;

            string codFiscaleList = string.Join(",", codFiscaleCol.ConvertAll(c => $"'{c}'"));

            string sql = $@"
                DECLARE @Cod_tipologia_blocco char(3);
                DECLARE @anno_accademico char(8);

                SET @Cod_tipologia_blocco = @blockCode;
                SET @anno_accademico = @annoAcademico;

                UPDATE Motivazioni_blocco_pagamenti
                SET Blocco_pagamento_attivo = 0, 
                    Data_fine_validita = CURRENT_TIMESTAMP, 
                    Utente_sblocco = @utente
                WHERE Anno_accademico = @anno_accademico 
                    AND Cod_tipologia_blocco = @Cod_tipologia_blocco 
		            and Blocco_pagamento_attivo=1
                    AND Num_domanda IN 
                        (SELECT Num_domanda
                         FROM Domanda
                         WHERE Anno_accademico = @anno_accademico 
                             AND tipo_bando IN ('lz') 
                             AND Cod_fiscale IN ({codFiscaleList}))

	            INSERT INTO [DatiGenerali_dom] ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort], [RichiestaMensa])
	            SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, @utente, vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 0, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort , vDATIGENERALI_dom.RichiestaMensa 
	            FROM 
		            Domanda INNER JOIN vDATIGENERALI_dom ON Domanda.Anno_accademico = vDATIGENERALI_dom.Anno_accademico AND 
		            Domanda.Num_domanda = vDATIGENERALI_dom.Num_domanda 
	            WHERE 
		            (Domanda.Anno_accademico = @anno_accademico) and 
		            tipo_bando in ('lz','l2') AND 
		            Cod_fiscale in ({codFiscaleList}) and
		            Domanda.Num_domanda not in (SELECT DISTINCT Num_domanda
                         FROM Motivazioni_blocco_pagamenti
                         WHERE Anno_accademico = @anno_accademico 
                             AND Data_fine_validita IS NULL 
                             AND Blocco_pagamento_attivo = 1)
            ";

            using SqlCommand command = new(sql, conn, transaction);
            command.Parameters.AddWithValue("@blockCode", blockCode);
            command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
            command.Parameters.AddWithValue("@utente", utente);
            command.ExecuteNonQuery();
        }
    }
}
