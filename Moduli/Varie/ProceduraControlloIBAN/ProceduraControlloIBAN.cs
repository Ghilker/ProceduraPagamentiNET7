using IbanNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Windows.Forms;  // Ensure your project references System.Windows.Forms

namespace ProcedureNet7
{
    internal class ProceduraControlloIBAN : BaseProcedure<ArgsProceduraControlloIBAN>
    {
        public ProceduraControlloIBAN(MasterForm masterForm, SqlConnection mainConnection)
            : base(masterForm, mainConnection) { }

        private string? selectedAA;
        private SqlTransaction? sqlTransaction;

        public override void RunProcedure(ArgsProceduraControlloIBAN args)
        {
            selectedAA = args._annoAccademico;
            Logger.LogInfo(null, "Inizio procedura controllo IBAN.");

            sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                // 1) Seleziono i dati degli studenti (Cod_fiscale, IBAN) con possibili IBAN errati
                Logger.LogInfo(null, "Step 1: Seleziono i dati (Cod_fiscale, IBAN) degli studenti con possibili IBAN errati.");

                string sqlQuery = $@"
                    SELECT DISTINCT
                        Domanda.Cod_fiscale, 
                        vMODALITA_PAGAMENTO.IBAN
                    FROM 
                        Domanda 
                    INNER JOIN 
                        vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                    INNER JOIN 
                        vEsiti_concorsi vb ON Domanda.Anno_accademico = vb.Anno_accademico 
                                              AND Domanda.Num_domanda = vb.Num_domanda
                    WHERE 
                        Domanda.Anno_accademico >= '{selectedAA}' 
                        AND Domanda.Tipo_bando = 'lz' 
                        AND vb.Cod_tipo_esito <> 0
						AND vb.Cod_beneficio = 'BS'
                    ORDER BY Domanda.cod_fiscale
                ";

                Logger.LogInfo(null, $"Eseguo la seguente query:\n{sqlQuery}");

                // Step 2: Verifico validità IBAN per ciascuno studente estratto.
                Logger.LogInfo(null, "Step 2: Verifico validità IBAN per ciascuno studente estratto.");
                List<string> studentiDaBloccare = new();
                Dictionary<string, string> fiscalCodeToIbanMap = new(); // Dictionary to store Cod_fiscale -> IBAN mapping
                int rowCounter = 0;

                using (SqlCommand readData = new(sqlQuery, CONNECTION, sqlTransaction))
                {
                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            rowCounter++;
                            string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                            string IBAN = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();

                            // Save Cod_fiscale -> IBAN mapping for later use
                            if (!fiscalCodeToIbanMap.ContainsKey(codFiscale))
                            {
                                fiscalCodeToIbanMap[codFiscale] = IBAN;
                            }

                            // Validate the IBAN
                            bool ibanValido = IbanValidatorUtil.ValidateIban(IBAN);
                            if (!ibanValido)
                            {
                                studentiDaBloccare.Add(codFiscale);
                            }
                        }
                    }
                }

                Logger.LogInfo(null, $"Trovati {rowCounter} record in totale. " +
                                     $"Di questi, {studentiDaBloccare.Count} presentano IBAN non valido.");


                // Se non c'è nessuno da bloccare, posso interrompere qui
                if (!studentiDaBloccare.Any())
                {
                    Logger.LogInfo(null, "Nessuno studente da bloccare. Procedura terminata.");
                    sqlTransaction?.Rollback();
                    return;
                }

                Logger.LogInfo(null,
                    $"Trovati {studentiDaBloccare.Count} studenti con IBAN errato, a partire dall'anno accademico {selectedAA}."
                );

                // 3) Messaggio da inviare
                Logger.LogInfo(null, "Step 3: Creo il messaggio di notifica per gli studenti.");
                string messaggio =
                    "Gentile studente, abbiamo riscontrato incongruenze nell''IBAN inserito nella sua area personale.<br>" +
                    "La invitiamo ad aggiornare la modalità prescelta in modo da poter essere inserito in eventuali pagamenti.";

                // 4) Recupero in un'unica query tutti gli anni accademici associati a *tutti* i CF
                Logger.LogInfo(null, "Step 4: Recupero in un'unica query tutti gli anni accademici per i CF individuati.");
                List<string> distinctCF = studentiDaBloccare.Distinct().ToList();

                // Costruisco una stringa con gli N CF tra apici singoli, es. 'CF1','CF2','CF3'
                string cfsJoined = string.Join("','", distinctCF);

                string anniQuery = $@"
                    SELECT DISTINCT 
                        Cod_fiscale, 
                        Anno_accademico
                    FROM Domanda
                    WHERE Cod_fiscale IN ('{cfsJoined}')
                ";

                Logger.LogInfo(null, $"Eseguo la query per anni accademici:\n{anniQuery}");

                // Mappatura CF -> lista di AA
                Dictionary<string, List<string>> studAnnoAccademico = new();

                using (SqlCommand cmd = new SqlCommand(anniQuery, CONNECTION, sqlTransaction))
                {
                    using (SqlDataReader anniReader = cmd.ExecuteReader())
                    {
                        int anniCounter = 0;
                        while (anniReader.Read())
                        {
                            anniCounter++;
                            string cf = Utilities.SafeGetString(anniReader, "Cod_fiscale").ToUpper().Trim();
                            string aa = Utilities.SafeGetString(anniReader, "Anno_accademico");

                            if (!string.IsNullOrWhiteSpace(cf) && !string.IsNullOrWhiteSpace(aa))
                            {
                                if (!studAnnoAccademico.ContainsKey(cf))
                                {
                                    studAnnoAccademico[cf] = new List<string>();
                                }
                                if (!studAnnoAccademico[cf].Contains(aa))
                                {
                                    studAnnoAccademico[cf].Add(aa);
                                }
                            }
                        }
                        Logger.LogInfo(null, $"Recuperati {anniCounter} record di (CF, AA).");
                    }
                }

                // 5) Invertire la mappatura (CF -> [anni]) in (AnnoAccademico -> [CF])
                Logger.LogInfo(null, "Step 5: Invertire la mappatura (CF -> [anni]) in (AnnoAccademico -> [CF]).");
                Dictionary<string, List<string>> cfsPerAnno = new();
                foreach (var kvp in studAnnoAccademico)
                {
                    string cf = kvp.Key;
                    List<string> anni = kvp.Value;

                    foreach (string aa in anni)
                    {
                        if (!cfsPerAnno.ContainsKey(aa))
                        {
                            cfsPerAnno[aa] = new List<string>();
                        }
                        cfsPerAnno[aa].Add(cf);
                    }
                }

                // 6) Aggiunta blocchi in bulk per ciascun anno accademico, 
                //    ma prima verifichiamo chi HA GIÀ il blocco e saltiamo quei CF.
                Logger.LogInfo(null, "Step 6: Aggiunta blocchi in bulk per ciascun anno accademico (saltando chi ha già il blocco).");

                // Per i messaggi: vogliamo inviarli solo a chi riceve effettivamente il nuovo blocco
                // (ossia chi NON lo aveva già).
                var cfsThatWillReceiveBlock = new HashSet<string>();

                foreach (var kvp in cfsPerAnno)
                {
                    string annoAccademico = kvp.Key;
                    List<string> allCfsForThisYear = kvp.Value.Distinct().ToList();

                    // 6a) Bulk-check: chi ha già il blocco "BSS" su questo AnnoAccademico?
                    Dictionary<string, bool> hasBlockDict = BlocksUtil.HasBlock(
                        CONNECTION,
                        sqlTransaction,
                        allCfsForThisYear,
                        "BSS",
                        annoAccademico
                    );

                    // 6b) Teniamo solo chi NON ha il blocco
                    List<string> cfsToBlock = allCfsForThisYear
                        .Where(cf => !hasBlockDict[cf])
                        .ToList();

                    if (!cfsToBlock.Any())
                    {
                        // Nessuno da bloccare per quest'anno
                        Logger.LogInfo(null,
                            $"Nessun nuovo blocco aggiunto per AnnoAccademico={annoAccademico} (tutti avevano già il blocco o nessuno da bloccare).");
                        continue;
                    }

                    // Aggiungo i CF effettivamente da bloccare
                    Logger.LogInfo(null,
                        $"Aggiungo blocco [BSS] per AnnoAccademico={annoAccademico}, su {cfsToBlock.Count} CF (saltati {allCfsForThisYear.Count - cfsToBlock.Count} con blocco già presente).");

                    // 6c) Invocazione AddBlock solo sui CF senza blocco
                    BlocksUtil.AddBlock(
                        CONNECTION,
                        sqlTransaction,
                        cfsToBlock,
                        "BSS",
                        annoAccademico,
                        "Area4_IbanCheck",
                        true
                    );

                    // 6d) Segno questi CF come coloro che riceveranno anche il messaggio
                    foreach (var cf in cfsToBlock)
                    {
                        cfsThatWillReceiveBlock.Add(cf);
                    }
                }

                // Step 7: Inserisco il messaggio di avviso SOLO per i CF a cui abbiamo effettivamente aggiunto il blocco
                Logger.LogInfo(null, "Step 7: Creazione dei messaggi personalizzati per gli studenti con blocco.");

                if (cfsThatWillReceiveBlock.Any())
                {
                    var personalizedMessages = new Dictionary<string, string>();

                    foreach (var cf in cfsThatWillReceiveBlock)
                    {
                        // Retrieve the wrong IBAN from the dictionary
                        string wrongIban = fiscalCodeToIbanMap.ContainsKey(cf)
                            ? fiscalCodeToIbanMap[cf]
                            : "IBAN non disponibile";

                        // Create the personalized message
                        string messaggioPersonalizzato =
                            $"Gentile studente, abbiamo riscontrato incongruenze nell''IBAN inserito nella sua area personale.<br>" +
                            $"IBAN: {wrongIban}#<br>" +
                            "La invitiamo ad aggiornare la modalità prescelta.";

                        // Add the message to the dictionary
                        personalizedMessages[cf] = messaggioPersonalizzato;
                    }

                    // Insert personalized messages into the database
                    MessageUtils.InsertMessages(
                        CONNECTION,
                        sqlTransaction,
                        personalizedMessages,
                        "Area4_IbanCheck"
                    );

                    Logger.LogInfo(null, $"Inseriti {personalizedMessages.Count} messaggi personalizzati per i rispettivi studenti.");
                }
                else
                {
                    Logger.LogInfo(null, "Nessun nuovo blocco inserito => nessun messaggio inserito.");
                }

                try
                {
                    string activeBlocksQuery = $@"
                        SELECT
                            bmp.Anno_accademico,
                            bmp.Num_domanda,
                            bmp.Data_validita AS BlockDate,
                            d.Cod_fiscale
                        FROM vMotivazioni_blocco_pagamenti bmp
                        INNER JOIN Domanda d ON bmp.num_domanda = d.num_domanda
                        WHERE  bmp.Cod_tipologia_blocco = 'BSS'
                          AND d.anno_accademico = '{selectedAA}'
                    ";

                    List<(string cf, DateTime blockDate, string annoAcc, string numDomanda)> activeBlocks = new();

                    using (SqlCommand cmd = new SqlCommand(activeBlocksQuery, CONNECTION, sqlTransaction))
                    {
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string cf = Utilities.SafeGetString(reader, "Cod_fiscale").Trim().ToUpper();
                                DateTime blockDate = reader.GetDateTime(reader.GetOrdinal("BlockDate"));
                                string annoAcc = Utilities.SafeGetString(reader, "Anno_accademico");
                                string numDomanda = Utilities.SafeGetString(reader, "Num_domanda");

                                activeBlocks.Add((cf, blockDate, annoAcc, numDomanda));
                            }
                        }
                    }
                    Logger.LogInfo(null, $"Trovati {activeBlocks.Count} blocchi BSS attivi inseriti da area4_iban_check.");
                    foreach (var block in activeBlocks)
                    {
                        // a) Prelevo l'IBAN immediatamente prima del blockDate
                        //    - In caso non ci fosse, "ibanBefore" rimarrà null (o string.Empty).
                        string ibanBefore = null;
                        string ibanBeforeQuery = @"
                                SELECT TOP 1 IBAN
                                FROM MODALITA_PAGAMENTO
                                WHERE Cod_fiscale = @cf
                                  AND data_validita < @blockDate
                                ORDER BY data_validita DESC
                            ";

                        using (SqlCommand cmdBefore = new SqlCommand(ibanBeforeQuery, CONNECTION, sqlTransaction))
                        {
                            cmdBefore.Parameters.AddWithValue("@cf", block.cf);
                            cmdBefore.Parameters.AddWithValue("@blockDate", block.blockDate);

                            object result = cmdBefore.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                ibanBefore = Convert.ToString(result)?.Trim().ToUpper();
                            }
                        }

                        // b) Prelevo l'IBAN attuale (l'ultimo in ordine di data_validita)
                        string ibanCurrent = null;
                        string ibanCurrentQuery = @"
                                SELECT IBAN
                                FROM vMODALITA_PAGAMENTO
                                WHERE Cod_fiscale = @cf
                            ";

                        using (SqlCommand cmdCurrent = new SqlCommand(ibanCurrentQuery, CONNECTION, sqlTransaction))
                        {
                            cmdCurrent.Parameters.AddWithValue("@cf", block.cf);

                            object result = cmdCurrent.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                ibanCurrent = Convert.ToString(result)?.Trim().ToUpper();
                            }
                        }

                        // If we can't find an IBAN at all, skip the check
                        if (string.IsNullOrWhiteSpace(ibanCurrent))
                        {
                            // Possibly log something or skip
                            Logger.LogInfo(null, $"CF={block.cf}: Nessun IBAN corrente trovato. Salto il controllo.");
                            continue;
                        }

                        // c) Confronto
                        bool ibanChanged = false;
                        if (ibanBefore == null)
                        {
                            // Se non esiste un IBAN "prima", potremmo considerarli diversi
                            // perché l'utente ha inserito un IBAN dopo il blocco o non ne aveva affatto.
                            ibanChanged = true;
                        }
                        else
                        {
                            // Se la stringa attuale è diversa da quella pre-blocco
                            ibanChanged = !ibanCurrent.Equals(ibanBefore, StringComparison.OrdinalIgnoreCase);
                        }

                        if (ibanChanged)
                        {
                            bool ibanValido = IbanValidatorUtil.ValidateIban(ibanCurrent);
                            if (!ibanValido)
                            {
                                Logger.LogInfo(null, $"CF={block.cf}: IBAN: {ibanCurrent} errato. Inserisco messaggio.");
                                string messaggioPersonalizzato =
                                    $"Gentile studente, abbiamo riscontrato incongruenze nell''IBAN inserito nella sua area personale.<br>" +
                                    $"IBAN: {ibanCurrent}#<br>" +
                                    "La invitiamo ad aggiornare la modalità prescelta.";

                                MessageUtils.InsertMessages(
                                    CONNECTION,
                                    sqlTransaction,
                                    new List<string>() { block.cf },
                                    messaggioPersonalizzato,
                                    "Area4_IbanCheck"
                                );
                                continue;
                            }
                            else
                            {
                                Logger.LogInfo(null, $"CF={block.cf}: IBAN: {ibanCurrent} modificato. Rimuovo blocco.");
                                BlocksUtil.RemoveBlock(CONNECTION, sqlTransaction, new List<string>() { block.cf }, "BSS", selectedAA, "Area4_IbanCheck");
                            }
                        }
                        else
                        {
                            Logger.LogInfo(null,
                                $"CF={block.cf}: L'IBAN NON è cambiato (prima={ibanBefore}, adesso={ibanCurrent}). Blocco lasciato attivo.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(100, $"Errore nello rimozione blocchi su IBAN aggiornati: {ex.Message}");
                    throw;
                }


                // step 8) Confermo la transazione (interazione con l'utente)
                Logger.LogInfo(null, "Step 8: Richiedo conferma per completare la procedura.");
                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    DialogResult result = MessageBox.Show(
                        _masterForm,
                        "Completare procedura?",
                        "Attenzione",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.OK)
                    {
                        Logger.LogInfo(100, "Procedura terminata con successo. Eseguo commit della transazione.");
                        sqlTransaction?.Commit();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Procedura interrotta dall'utente. Eseguo rollback della transazione.");
                        sqlTransaction?.Rollback();
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(100, $"Errore durante l'esecuzione della procedura: {ex.Message}");
                sqlTransaction?.Rollback();
            }
        }
    }
}
