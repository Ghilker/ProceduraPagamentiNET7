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
                    RemoveBlock(CONNECTION, sqlTransaction, studentiDaSbloccare);
                }

                sqlTransaction.Commit();
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }

        private void RemoveBlock(SqlConnection conn, SqlTransaction transaction, List<string> codFiscaleCol, string blockCode = "BSS")
        {
            string annoAccademico = selectedAA ?? string.Empty;
            string utente = "Area4";

            // Build the list of Cod_fiscale parameters
            List<string> codFiscaleParamNames = new List<string>();
            List<SqlParameter> codFiscaleParams = new List<SqlParameter>();
            int index = 0;
            foreach (string cf in codFiscaleCol)
            {
                string paramName = "@cf" + index;
                codFiscaleParamNames.Add(paramName);
                codFiscaleParams.Add(new SqlParameter(paramName, cf));
                index++;
            }

            string codFiscaleInClause = string.Join(", ", codFiscaleParamNames);

            // Now, get the list of columns from DatiGenerali_dom
            List<string> columnNames = GetColumnNames(conn, transaction, "DatiGenerali_dom");

            // Get the list of columns from vDATIGENERALI_dom
            List<string> vColumns = GetColumnNames(conn, transaction, "vDATIGENERALI_dom");

            // Define the columns that need explicit values
            Dictionary<string, string> explicitValues = new Dictionary<string, string>()
            {
                { "Data_validita", "CURRENT_TIMESTAMP" }, // SQL expression
                { "Utente", "@utenteValue" },             // Parameter
                { "Blocco_pagamento", "0" },              // For RemoveBlock
            };

            List<string> insertColumns = new List<string>();
            List<string> selectColumns = new List<string>();

            foreach (string columnName in columnNames)
            {
                insertColumns.Add($"[{columnName}]");

                if (explicitValues.ContainsKey(columnName))
                {
                    selectColumns.Add(explicitValues[columnName]);
                }
                else if (vColumns.Contains(columnName))
                {
                    selectColumns.Add($"v.[{columnName}]");
                }
                else
                {
                    // Assign NULL for columns not in vDATIGENERALI_dom and not in explicitValues
                    selectColumns.Add("NULL");
                }
            }

            string insertColumnsList = string.Join(", ", insertColumns);
            string selectColumnsList = string.Join(", ", selectColumns);

            string sql = $@"
                    UPDATE Motivazioni_blocco_pagamenti
                    SET Blocco_pagamento_attivo = 0, 
                        Data_fine_validita = CURRENT_TIMESTAMP, 
                        Utente_sblocco = @utenteValue
                    WHERE Anno_accademico = @annoAcademico 
                        AND Cod_tipologia_blocco = @blockCode 
                        AND Blocco_pagamento_attivo = 1
                        AND Num_domanda IN 
                            (SELECT Num_domanda
                             FROM Domanda d
                             WHERE Anno_accademico = @annoAcademico 
                                 AND d.tipo_bando IN ('lz') 
                                 AND d.Cod_fiscale IN ({codFiscaleInClause}));

                    INSERT INTO [DatiGenerali_dom] ({insertColumnsList})
                    SELECT DISTINCT {selectColumnsList}
                    FROM 
                        Domanda d
                        INNER JOIN vDATIGENERALI_dom v ON d.Anno_accademico = v.Anno_accademico AND 
                                                         d.Num_domanda = v.Num_domanda
                    WHERE 
                        d.Anno_accademico = @annoAcademico AND
                        d.tipo_bando IN ('lz', 'l2') AND
                        d.Cod_fiscale IN ({codFiscaleInClause}) AND
                        d.Num_domanda NOT IN (
                            SELECT DISTINCT Num_domanda
                            FROM Motivazioni_blocco_pagamenti
                            WHERE Anno_accademico = @annoAcademico 
                                AND Data_fine_validita IS NULL
                                AND Blocco_pagamento_attivo = 1
                        );
                    ";

            using (SqlCommand command = new SqlCommand(sql, conn, transaction))
            {
                command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
                command.Parameters.AddWithValue("@utenteValue", utente);
                command.Parameters.AddWithValue("@blockCode", blockCode);
                foreach (var param in codFiscaleParams)
                {
                    command.Parameters.Add(param);
                }

                command.ExecuteNonQuery();
            }
        }
        private List<string> GetColumnNames(SqlConnection conn, SqlTransaction transaction, string tableName)
        {
            List<string> columnNames = new List<string>();
            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName";

            using (SqlCommand cmd = new SqlCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.GetString(0) == "Id_DatiGenerali_dom" || reader.GetString(0) == "Id_Domanda")
                        {
                            continue;
                        }
                        columnNames.Add(reader.GetString(0));
                    }
                }
            }
            return columnNames;
        }
    }
}
