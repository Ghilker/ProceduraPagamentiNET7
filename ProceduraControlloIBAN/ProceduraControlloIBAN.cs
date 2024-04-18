using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ProceduraControlloIBAN : BaseProcedure<ArgsProceduraControlloIBAN>
    {

        public ProceduraControlloIBAN(MainUI mainUI, string connection_string) : base(mainUI, connection_string) { }

        string? selectedAA;
        SqlTransaction? sqlTransaction;
        public override void RunProcedure(ArgsProceduraControlloIBAN args)
        {
            selectedAA = args._annoAccademico;

            using SqlConnection conn = new(CONNECTION_STRING);
            conn.Open();
            sqlTransaction = conn.BeginTransaction();
            try
            {
                string sqlQuery = $@"
                    SELECT 
                        domanda.Cod_fiscale, 
                        pagamenti.iban_storno, 
                        vMODALITA_PAGAMENTO.IBAN
                    FROM 
                        vMotivazioni_blocco_pagamenti AS mot 
                    INNER JOIN 
                        Domanda ON mot.Anno_accademico = Domanda.Anno_accademico AND mot.Num_domanda = Domanda.Num_domanda 
                    INNER JOIN 
                        vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                    INNER JOIN 
                        Pagamenti ON Domanda.Anno_accademico = Pagamenti.Anno_accademico AND Domanda.Num_domanda = Pagamenti.Num_domanda
                    WHERE 
                        Domanda.Anno_accademico = {selectedAA} 
                        AND Cod_tipologia_blocco = 'BSS' 
                        AND Pagamenti.Ritirato_azienda = 1
                        AND Pagamenti.Data_validita = (
                            SELECT MAX(Data_validita)
                            FROM Pagamenti AS p2
                            WHERE p2.Anno_accademico = Pagamenti.Anno_accademico
                              AND p2.Num_domanda = Pagamenti.Num_domanda
                              AND p2.Ritirato_azienda = Pagamenti.Ritirato_azienda
                        )
                    order by domanda.cod_fiscale
                    ";

                List<string> studentiDaSbloccare = new List<string>();
                SqlCommand readData = new(sqlQuery, conn, sqlTransaction);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                        string IBAN_Storno = Utilities.SafeGetString(reader, "iban_storno").ToUpper().Trim();
                        string IBAN_attuale = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();

                        if (string.IsNullOrWhiteSpace(IBAN_attuale) || string.IsNullOrWhiteSpace(IBAN_Storno))
                        {
                            continue;
                        }

                        if (IBAN_attuale != IBAN_Storno)
                        {
                            studentiDaSbloccare.Add(codFiscale);
                        }
                    }
                }

                RemoveBlock(conn, studentiDaSbloccare);
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }

        private void RemoveBlock(SqlConnection conn, List<string> codFiscaleCol)
        {
            try
            {
                string blockCode = "BSS";
                string annoAccademico = selectedAA ?? string.Empty;
                string utente = "Area4";

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

	                    INSERT INTO [ADISU].[dbo].[DatiGenerali_dom] ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort])
	                    SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, @utente, vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 0, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort 
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
                using SqlCommand command = new(sql, conn, sqlTransaction);
                _ = command.Parameters.AddWithValue("@blockCode", blockCode);
                _ = command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
                _ = command.Parameters.AddWithValue("@utente", utente);
                _ = command.ExecuteNonQuery();
            }
            catch { throw; }
        }
    }
}
