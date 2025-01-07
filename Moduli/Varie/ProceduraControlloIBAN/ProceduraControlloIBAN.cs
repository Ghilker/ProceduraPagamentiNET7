using IbanNet;
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

        public ProceduraControlloIBAN(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        string? selectedAA;
        SqlTransaction? sqlTransaction;
        public override void RunProcedure(ArgsProceduraControlloIBAN args)
        {
            selectedAA = args._annoAccademico;

            Logger.LogInfo(null, $"Inizio procedura controllo IBAN.");
            sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                string sqlQuery = $@"
                    SELECT distinct
                        domanda.Cod_fiscale, 
                        vMODALITA_PAGAMENTO.IBAN
                    FROM 
                        Domanda 
                    INNER JOIN 
                        vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                    INNER JOIN 
	                    vEsiti_concorsiBS vb on Domanda.Anno_accademico = vb.Anno_accademico and Domanda.Num_domanda = vb.Num_domanda
                    WHERE 
                        Domanda.Anno_accademico >= '{selectedAA}' and Domanda.Tipo_bando = 'lz' and vb.Cod_tipo_esito <> 0

                    order by domanda.cod_fiscale
                    ";

                List<string> studentiDaBloccare = new List<string>();
                SqlCommand readData = new(sqlQuery, CONNECTION, sqlTransaction);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                        string IBAN = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();

                        bool ibanValido = IbanValidatorUtil.ValidateIban(IBAN);

                        if (ibanValido)
                        {
                            continue;
                        }

                        studentiDaBloccare.Add(codFiscale);
                    }
                }

                Logger.LogInfo(null, $"Trovati {studentiDaBloccare.Count} studenti con errori IBAN nel {selectedAA}.");
                string messaggio = "Gentile studente, abbiamo riscontrato incongruenze nell''IBAN inserito nella sua area personale.<br>La invitiamo ad aggiornare la modalità prescelta in modo da poter essere inserito in eventuali pagamenti.";

                BlocksUtil.AddBlock(CONNECTION, sqlTransaction, studentiDaBloccare, "BSS", "20232024", "Area4_IbanCheck", true);
                BlocksUtil.AddBlock(CONNECTION, sqlTransaction, studentiDaBloccare, "BSS", "20242025", "Area4_IbanCheck", true);
                MessageUtils.InsertMessages(CONNECTION, sqlTransaction, studentiDaBloccare, messaggio, "Area4_IbanCheck");

                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    DialogResult result = MessageBox.Show(_masterForm, "Completare procedura?", "Attenzione", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    {
                        if (result == DialogResult.OK)
                        {
                            sqlTransaction?.Commit();
                        }
                        else
                        {
                            Logger.LogInfo(null, $"Procedura chiusa dall'utente");
                            sqlTransaction?.Rollback();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }
    }
}
