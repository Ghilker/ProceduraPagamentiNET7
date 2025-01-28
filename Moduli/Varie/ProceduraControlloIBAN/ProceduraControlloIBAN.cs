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
                        vEsiti_concorsiBS vb ON Domanda.Anno_accademico = vb.Anno_accademico 
                                              AND Domanda.Num_domanda = vb.Num_domanda
                    WHERE 
                        Domanda.Anno_accademico >= '{selectedAA}' 
                        AND Domanda.Tipo_bando = 'lz' 
                        AND vb.Cod_tipo_esito <> 0
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
                            "La invitiamo ad aggiornare la modalità prescelta in modo da poter essere inserito in eventuali pagamenti.";

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


                // 8) Confermo la transazione (interazione con l'utente)
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
