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

            sqlTransaction = CONNECTION.BeginTransaction();
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
                        Domanda.Anno_accademico = '{selectedAA}' 
                        AND Cod_tipologia_blocco = 'BSS' 
                        AND Pagamenti.Ritirato_azienda = 1
                        AND Pagamenti.Data_validita = (
                            SELECT MAX(Data_validita)
                            FROM Pagamenti AS p2
                            WHERE p2.Anno_accademico = Pagamenti.Anno_accademico
                              AND p2.Num_domanda = Pagamenti.Num_domanda
                              AND p2.Ritirato_azienda = Pagamenti.Ritirato_azienda
                        )
                        AND IBAN_storno IS NOT NULL
                    order by domanda.cod_fiscale
                    ";

                List<string> studentiDaSbloccare = new List<string>();
                SqlCommand readData = new(sqlQuery, CONNECTION, sqlTransaction);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper().Trim();
                        string IBAN_Storno = Utilities.SafeGetString(reader, "IBAN_storno").ToUpper().Trim();
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

                if (studentiDaSbloccare.Any())
                {
                    BlocksUtil.RemoveBlock(CONNECTION, sqlTransaction, studentiDaSbloccare, "BSS", selectedAA, "Verif_IBAN");
                }

                sqlTransaction.Commit();
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }
    }
}
