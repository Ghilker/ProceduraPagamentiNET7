using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ProceduraControlloPS : BaseProcedure<ArgsProceduraControlloPS>
    {
        List<string> codiciFiscaliBlocchi = new List<string>();
        List<string> codiciFiscaliIncongruenze = new List<string>();

        public ProceduraControlloPS(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsProceduraControlloPS args)
        {
            Logger.LogInfo(null, "Inizio dell'esecuzione di RunProcedure");
            try
            {
                if (CONNECTION == null)
                {
                    Logger.LogError(null, "CONNESSIONE ASSENTE O NULLA");
                    return;
                }
                if (_masterForm == null)
                {
                    Logger.LogError(null, "MASTER FORM NULLO!!!");
                    return;
                }

                string blocchiQuery = $@"
                    SELECT Cod_fiscale 
                    FROM Domanda 
                    WHERE Anno_accademico = '20232024' 
                        AND Tipo_bando = 'lz'
                        AND Num_domanda IN (
                            SELECT Domanda.Num_domanda 
                            FROM Domanda 
                            INNER JOIN vEsiti_concorsiBS es ON Domanda.Anno_accademico = es.Anno_accademico AND Domanda.Num_domanda = es.Num_domanda 
                            WHERE Domanda.Anno_accademico = '20232024' 
                                AND Tipo_bando = 'lz' 
                                AND es.Cod_tipo_esito = 2
                        )
                        AND Num_domanda IN (
                            SELECT Domanda.Num_domanda 
                            FROM Domanda 
                            INNER JOIN vMotivazioni_blocco_pagamenti mot ON Domanda.Anno_accademico = mot.Anno_accademico AND Domanda.Num_domanda = mot.Num_domanda 
                            WHERE Domanda.Anno_accademico = '20232024' 
                                AND Tipo_bando = 'lz' 
                                AND Cod_tipologia_blocco = 'BPP' 
                        )
                        AND Cod_fiscale IN (
                            SELECT Domanda.Cod_fiscale 
                            FROM Domanda 
                            INNER JOIN vCittadinanza ON Domanda.Cod_fiscale = vCittadinanza.COD_FISCALE
                            WHERE Domanda.Anno_accademico = '20232024' 
                                AND Domanda.tipo_bando = 'lz'  
                                AND vCittadinanza.Cod_cittadinanza NOT IN (SELECT codice FROM Cittadinanze_Ue)
                        )
                        AND (
                            Cod_fiscale IN (
                                -- Con permesso a scadenza valido
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '01' 
                                    AND vSpecifiche_permesso_soggiorno.Data_scadenza >= '2024-07-31'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso lunga permanenza
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '02'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso illimitato
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '03'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso scaduto ma presenza di ricevuta valida
                                SELECT DISTINCT Cod_fiscale
                                FROM Specifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON Specifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    Specifiche_permesso_soggiorno.Data_validita = (
                                        SELECT MAX(Data_validita)
                                        FROM Specifiche_permesso_soggiorno AS br
                                        WHERE br.id_allegato = Specifiche_permesso_soggiorno.id_allegato 
                                            AND br.Num_domanda = Specifiche_permesso_soggiorno.Num_domanda
                                            AND br.Cod_fiscale = Specifiche_permesso_soggiorno.Cod_fiscale
                                    )
                                    AND Specifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND Specifiche_permesso_soggiorno.Tipo_permesso = '01' 
                                    AND Specifiche_permesso_soggiorno.Data_scadenza > '2023-08-31' 
                                    AND Specifiche_permesso_soggiorno.Data_scadenza < '2024-07-31'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                                    AND Cod_fiscale IN (
                                        SELECT DISTINCT Cod_fiscale
                                        FROM Specifiche_permesso_soggiorno 
                                        INNER JOIN VStatus_Allegati ON Specifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                        WHERE 
                                            Specifiche_permesso_soggiorno.Data_validita = (
                                                SELECT MAX(Data_validita)
                                                FROM Specifiche_permesso_soggiorno AS br
                                                WHERE br.id_allegato = Specifiche_permesso_soggiorno.id_allegato 
                                                    AND br.Num_domanda = Specifiche_permesso_soggiorno.Num_domanda
                                                    AND br.Cod_fiscale = Specifiche_permesso_soggiorno.Cod_fiscale
                                            )
                                            AND Specifiche_permesso_soggiorno.Tipo_documento = '02' 
                                            AND Specifiche_permesso_soggiorno.Data_richiesta_rilascio_rinnovo > '2023-07-01'
                                            AND cod_status = '05' 
                                            AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                                    )
                            )
                        )
                ";
                Logger.LogInfo(null, "Esecuzione della query per ottenere i codici fiscali per i blocchi.");

                SqlCommand readBlocchi = new(blocchiQuery, CONNECTION);
                using (SqlDataReader reader = readBlocchi.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale");
                        codiciFiscaliBlocchi.Add(codFiscale);
                    }
                }

                Logger.LogInfo(null, $"Numero di codici fiscali per i blocchi trovati: {codiciFiscaliBlocchi.Count}");

                string incongruenzeQuery = $@"
                    SELECT DISTINCT Cod_fiscale 
                    FROM Domanda 
                    WHERE Anno_accademico = '20242025' 
                        AND Tipo_bando = 'lz'
                        AND Cod_fiscale IN (
                            SELECT Domanda.Cod_fiscale 
                            FROM Domanda 
                            INNER JOIN vCittadinanza ON Domanda.Cod_fiscale = vCittadinanza.COD_FISCALE
                            WHERE Domanda.Anno_accademico = '20242025' 
                                AND Domanda.tipo_bando = 'lz'  
                                AND vCittadinanza.Cod_cittadinanza NOT IN (SELECT codice FROM Cittadinanze_Ue)
                        )
                        AND Num_domanda IN (
                            SELECT Num_domanda 
                            FROM Incongruenze 
                            WHERE Anno_accademico = '20242025' 
                                AND Cod_incongruenza = '18' 
                                AND Data_fine_validita IS NULL
                        )
                        AND (
                            Cod_fiscale IN (
                                -- Con permesso a scadenza valido
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '01' 
                                    AND vSpecifiche_permesso_soggiorno.Data_scadenza >= '2024-06-01'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso lunga permanenza
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '02'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso illimitato
                                SELECT DISTINCT Cod_fiscale
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '03' 
                                    AND vSpecifiche_permesso_soggiorno.Tipo_permesso = '03'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                            OR Cod_fiscale IN (
                                -- Con permesso ricevuta valida
                                SELECT DISTINCT Cod_fiscale 
                                FROM vSpecifiche_permesso_soggiorno 
                                INNER JOIN VStatus_Allegati ON vSpecifiche_permesso_soggiorno.id_allegato = VStatus_Allegati.id_allegato
                                WHERE 
                                    vSpecifiche_permesso_soggiorno.Tipo_documento = '02' 
                                    AND vSpecifiche_permesso_soggiorno.Data_richiesta_rilascio_rinnovo >= '2024-06-01'
                                    AND cod_status = '05' 
                                    AND (Anno_accademico IS NULL OR Anno_accademico = '20232024')
                            )
                        )
                ";
                Logger.LogInfo(null, "Esecuzione della query per ottenere i codici fiscali per le incongruenze.");

                SqlCommand readIncongruenze = new(incongruenzeQuery, CONNECTION);
                using (SqlDataReader reader = readIncongruenze.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale");
                        codiciFiscaliIncongruenze.Add(codFiscale);
                    }
                }

                Logger.LogInfo(null, $"Numero di codici fiscali per le incongruenze trovati: {codiciFiscaliIncongruenze.Count}");

                Logger.LogInfo(25, "Inizio della rimozione dei blocchi.");
                RemoveBlock(codiciFiscaliBlocchi, "BPP");
                Logger.LogInfo(50, "Fine della rimozione dei blocchi.");

                Logger.LogInfo(75, "Inizio della rimozione delle incongruenze.");
                RemoveIncongruenze(codiciFiscaliIncongruenze, "18");
                Logger.LogInfo(100, "Fine della rimozione delle incongruenze.");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore durante l'esecuzione di RunProcedure: {ex.Message}");
            }
        }

        private void RemoveBlock(List<string> codFiscaleCol, string blockCode)
        {
            Logger.LogInfo(null, $"Inizio della rimozione del blocco '{blockCode}' per {codFiscaleCol.Count} codici fiscali.");

            try
            {
                string annoAccademico = "20232024";
                string utente = "Verif_perm_sogg";

                // Build the list of Cod_fiscale parameters
                Logger.LogInfo(null, "Costruzione dei parametri per Cod_fiscale.");
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
                Logger.LogInfo(null, "Recupero dei nomi delle colonne per 'DatiGenerali_dom' e 'vDATIGENERALI_dom'.");
                List<string> columnNames = GetColumnNames("DatiGenerali_dom");
                List<string> vColumns = GetColumnNames("vDATIGENERALI_dom");

                // Define the columns that need explicit values
                Dictionary<string, string> explicitValues = new Dictionary<string, string>()
                {
                    { "Data_validita", "CURRENT_TIMESTAMP" },
                    { "Utente", "@utenteValue" },
                    { "Blocco_pagamento", "0" },
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

                Logger.LogInfo(null, "Esecuzione dell'UPDATE e INSERT per la rimozione del blocco.");

                using (SqlCommand command = new SqlCommand(sql, CONNECTION))
                {
                    command.Parameters.AddWithValue("@annoAcademico", annoAccademico);
                    command.Parameters.AddWithValue("@utenteValue", utente);
                    command.Parameters.AddWithValue("@blockCode", blockCode);
                    foreach (var param in codFiscaleParams)
                    {
                        command.Parameters.Add(param);
                    }

                    int rowsAffected = command.ExecuteNonQuery();
                    Logger.LogInfo(null, $"Rimozione del blocco completata. Righe interessate: {rowsAffected}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore durante la rimozione del blocco: {ex.Message}");
            }

            Logger.LogInfo(null, "Fine della rimozione del blocco.");
        }

        private void RemoveIncongruenze(List<string> codFiscaleCol, string codIncongruenza)
        {
            Logger.LogInfo(null, $"Inizio della rimozione dell'incongruenza '{codIncongruenza}' per {codFiscaleCol.Count} codici fiscali.");

            try
            {
                string annoAccademico = "20242025";

                // Build the list of Cod_fiscale parameters
                Logger.LogInfo(null, "Costruzione dei parametri per Cod_fiscale.");
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

                string sql = $@"
                    UPDATE [dbo].[Incongruenze]
                    SET [Data_fine_validita] = CURRENT_TIMESTAMP
                    WHERE Anno_accademico = @annoAccademico 
                        AND Cod_incongruenza = @codIncongruenza 
                        AND Data_fine_validita IS NULL 
                        AND Num_domanda IN (
                            SELECT Num_domanda 
                            FROM Domanda 
                            WHERE Anno_accademico = @annoAccademico 
                                AND Tipo_bando = 'lz' 
                                AND Cod_fiscale IN ({codFiscaleInClause})
                        );
                ";

                Logger.LogInfo(null, "Esecuzione dell'UPDATE per la rimozione dell'incongruenza.");

                using (SqlCommand command = new SqlCommand(sql, CONNECTION))
                {
                    command.Parameters.AddWithValue("@annoAccademico", annoAccademico);
                    command.Parameters.AddWithValue("@codIncongruenza", codIncongruenza);

                    foreach (var param in codFiscaleParams)
                    {
                        command.Parameters.Add(param);
                    }

                    int rowsAffected = command.ExecuteNonQuery();
                    Logger.LogInfo(null, $"Rimozione dell'incongruenza completata. Righe interessate: {rowsAffected}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore durante la rimozione dell'incongruenza: {ex.Message}");
            }

            Logger.LogInfo(null, "Fine della rimozione dell'incongruenza.");
        }

        private List<string> GetColumnNames(string tableName)
        {

            List<string> columnNames = new List<string>();
            string query = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName";

            try
            {
                using (SqlCommand cmd = new SqlCommand(query, CONNECTION))
                {
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string columnName = reader.GetString(0);
                            if (columnName == "Id_DatiGenerali_dom" || columnName == "Id_Domanda")
                            {
                                continue;
                            }
                            columnNames.Add(columnName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore durante il recupero dei nomi delle colonne per la tabella '{tableName}': {ex.Message}");
            }

            return columnNames;
        }
    }
}
