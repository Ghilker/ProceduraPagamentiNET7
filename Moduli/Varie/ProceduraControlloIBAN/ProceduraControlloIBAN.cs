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

        string? selectedAA;
        SqlTransaction? sqlTransaction;

        public override void RunProcedure(ArgsProceduraControlloIBAN args)
        {
            selectedAA = args._annoAccademico;
            Logger.LogInfo(null, "Inizio procedura controllo IBAN.");

            sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                // 1) Seleziono i dati degli studenti (Cod_fiscale, IBAN) che potrebbero avere un IBAN errato
                string sqlQuery = $@"
                    SELECT DISTINCT
                        domanda.Cod_fiscale, 
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
                    ORDER BY domanda.cod_fiscale
                ";

                // 2) Estraggo i codici fiscali degli studenti con IBAN non valido
                List<string> studentiDaBloccare = new();
                using (SqlCommand readData = new(sqlQuery, CONNECTION, sqlTransaction))
                {
                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                            string IBAN = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();

                            bool ibanValido = IbanValidatorUtil.ValidateIban(IBAN);
                            if (!ibanValido)
                            {
                                studentiDaBloccare.Add(codFiscale);
                            }
                        }
                    }
                }

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
                string messaggio =
                    "Gentile studente, abbiamo riscontrato incongruenze nell''IBAN inserito nella sua area personale.<br>" +
                    "La invitiamo ad aggiornare la modalità prescelta in modo da poter essere inserito in eventuali pagamenti.";

                // 4) Recupero in un'unica query *tutti* gli anni accademici associati a *tutti* i CF
                //    (invece di farne uno per ogni studente)
                List<string> distinctCF = studentiDaBloccare.Distinct().ToList();

                // Costruisco una stringa con gli N CF tra apici singoli, es. 'CF1','CF2','CF3'
                // (In un contesto reale potrebbe essere preferibile usare i parametri multipli o una table-valued parameter)
                string cfsJoined = string.Join("','", distinctCF);

                // Query per prendere (Cod_fiscale, Anno_accademico) in bulk
                string anniQuery = $@"
                    SELECT DISTINCT 
                        Cod_fiscale, 
                        Anno_accademico
                    FROM Domanda
                    WHERE Cod_fiscale IN ('{cfsJoined}')
                ";

                // Mappatura CF -> lista di AA
                Dictionary<string, List<string>> studAnnoAccademico = new();

                using (SqlCommand cmd = new SqlCommand(anniQuery, CONNECTION, sqlTransaction))
                {
                    using (SqlDataReader anniReader = cmd.ExecuteReader())
                    {
                        while (anniReader.Read())
                        {
                            string cf = Utilities.SafeGetString(anniReader, "Cod_fiscale").ToUpper().Trim();
                            string aa = Utilities.SafeGetString(anniReader, "Anno_accademico");

                            // Controllo che non sia stringa vuota
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
                    }
                }

                // studAnnoAccademico: { "ABCD1234": ["20232024", "20222023"], "XYZT5678": ["20232024"] ... }

                // 5) Invertire la mappatura (annoAccademico -> [cf1, cf2, ...])
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

                // 6) Aggiungo i blocchi in bulk per anno accademico
                foreach (var kvp in cfsPerAnno)
                {
                    string annoAccademico = kvp.Key;
                    List<string> codFiscali = kvp.Value.Distinct().ToList();

                    BlocksUtil.AddBlock(
                        CONNECTION,
                        sqlTransaction,
                        codFiscali,
                        "BSS",
                        annoAccademico,
                        "Area4_IbanCheck",
                        true
                    );
                }

                // 7) Inserisco i messaggi per tutti gli studenti con IBAN errato, in un’unica chiamata
                MessageUtils.InsertMessages(
                    CONNECTION,
                    sqlTransaction,
                    distinctCF,
                    messaggio,
                    "Area4_IbanCheck"
                );

                // 8) Confermo la transazione (interazione con l'utente)
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
                        Logger.LogInfo(100, "Procedura terminata.");
                        sqlTransaction?.Commit();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Procedura interrotta dall'utente.");
                        sqlTransaction?.Rollback();
                    }
                });
            }
            catch (Exception ex)
            {
                sqlTransaction?.Rollback();
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }
    }
}
