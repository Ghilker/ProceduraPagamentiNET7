using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal class ProceduraControlloPS : BaseProcedure<ArgsProceduraControlloPS>
    {
        public ProceduraControlloPS(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }
        public SqlTransaction sqlTransaction;
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

                sqlTransaction = CONNECTION.BeginTransaction();

                // New query that covers both academic years
                string query = @"
                    WITH
                    -- Subquery for 2023/2024 conditions
                    SubQ_2023_2024 AS (
                        SELECT DISTINCT d.Cod_fiscale
                        FROM Domanda d
                        WHERE d.Anno_accademico = '20232024'
                          AND d.Tipo_bando = 'lz'
                          AND EXISTS (
                              SELECT 1
                              FROM vEsiti_concorsiBS es
                              WHERE es.Anno_accademico = d.Anno_accademico
                                AND es.Num_domanda = d.Num_domanda
                                AND es.Cod_tipo_esito = 2
                          )
                          AND EXISTS (
                              SELECT 1
                              FROM vMotivazioni_blocco_pagamenti mot
                              WHERE mot.Anno_accademico = d.Anno_accademico
                                AND mot.Num_domanda = d.Num_domanda
                                AND mot.Cod_tipologia_blocco = 'BPP'
                          )
                          AND EXISTS (
                              SELECT 1
                              FROM vCittadinanza c
                              WHERE c.COD_FISCALE = d.Cod_fiscale
                                AND c.Cod_cittadinanza NOT IN (SELECT codice FROM Cittadinanze_Ue)
                          )
                          AND d.Cod_fiscale IN (
                              -- Union of all permit conditions for 2023/2024
                              SELECT cod_fiscale FROM (
                                  -- Valid expiring permit
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso = '01'
                                    AND s.Data_scadenza >= '31/07/2024'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')

                                  UNION

                                  -- Long-term permit
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso = '02'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')

                                  UNION

                                  -- Unlimited permit
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso = '03'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')

                                  UNION

                                  -- Expired permit with valid receipt
                                  SELECT s.cod_fiscale
                                  FROM Specifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso = '01'
                                    AND s.Data_scadenza > '31/08/2023'
                                    AND s.Data_scadenza < '31/07/2024'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')
                                    AND EXISTS (
                                        SELECT 1
                                        FROM Specifiche_permesso_soggiorno s2
                                        INNER JOIN VStatus_Allegati a2 ON s2.id_allegato = a2.id_allegato
                                        WHERE s2.cod_fiscale = s.cod_fiscale
                                          AND s2.Tipo_documento = '02'
                                          AND s2.Data_richiesta_rilascio_rinnovo > '01/07/2023'
                                          AND a2.cod_status = '05'
                                          AND (s2.Anno_accademico IS NULL OR s2.Anno_accademico = '20232024')
                                    )
                              ) AS permits
                          )
                    ),

                    -- Subquery for 2024/2025 conditions
                    SubQ_2024_2025 AS (
                        SELECT DISTINCT d.Cod_fiscale
                        FROM Domanda d
                        WHERE d.Anno_accademico = '20242025'
                          AND d.Tipo_bando = 'lz'
                          AND EXISTS (
                              SELECT 1
                              FROM vMotivazioni_blocco_pagamenti mot
                              WHERE mot.Anno_accademico = d.Anno_accademico
                                AND mot.Num_domanda = d.Num_domanda
                                AND mot.Cod_tipologia_blocco = 'BPP'
                          )
                          AND EXISTS (
                              SELECT 1
                              FROM vCittadinanza c
                              WHERE c.COD_FISCALE = d.Cod_fiscale
                                AND c.Cod_cittadinanza NOT IN (SELECT codice FROM Cittadinanze_Ue)
                          )
                          AND d.Cod_fiscale IN (
                              -- Union of all permit conditions for 2024/2025
                              SELECT cod_fiscale FROM (
                                  -- Valid expiring permit
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso = '01'
                                    AND s.Data_scadenza >= '01/06/2024'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')

                                  UNION

                                  -- Long-term or unlimited permit
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '03'
                                    AND s.Tipo_permesso IN ('02', '03')
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')

                                  UNION

                                  -- Valid receipt
                                  SELECT s.cod_fiscale
                                  FROM vSpecifiche_permesso_soggiorno s
                                  INNER JOIN VStatus_Allegati a ON s.id_allegato = a.id_allegato
                                  WHERE s.Tipo_documento = '02'
                                    AND s.Data_richiesta_rilascio_rinnovo >= '01/11/2023'
                                    AND a.cod_status = '05'
                                    AND (s.Anno_accademico IS NULL OR s.Anno_accademico = '20232024')
                              ) AS permits
                          )
                    ),

                    -- Combine all students who meet conditions for at least one year
                    Students_OK AS (
                        SELECT DISTINCT Cod_fiscale
                        FROM (
                            SELECT Cod_fiscale FROM SubQ_2023_2024
                            UNION
                            SELECT Cod_fiscale FROM SubQ_2024_2025
                        ) AS OKStudents
                    )

                    -- Final output
                    SELECT
                        s.Cod_fiscale,
                        CASE WHEN s.Cod_fiscale IN (SELECT Cod_fiscale FROM SubQ_2023_2024) THEN 'Y' ELSE 'N' END AS OK_2023_2024,
                        CASE WHEN s.Cod_fiscale IN (SELECT Cod_fiscale FROM SubQ_2024_2025) THEN 'Y' ELSE 'N' END AS OK_2024_2025
                    FROM Students_OK s
                    ORDER BY s.Cod_fiscale;
                    ";

                Logger.LogInfo(null, "Esecuzione della query per ottenere i codici fiscali per i blocchi.");
                List<string> codiciFiscaliBlocchi20232024 = new List<string>();
                List<string> codiciFiscaliBlocchi20242025 = new List<string>();
                SqlCommand command = new SqlCommand(query, CONNECTION, sqlTransaction);
                using (SqlDataReader reader = command.ExecuteReader())
                {


                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale");
                        string ok2023_2024 = Utilities.SafeGetString(reader, "OK_2023_2024");
                        string ok2024_2025 = Utilities.SafeGetString(reader, "OK_2024_2025");

                        if (ok2023_2024 == "Y")
                        {
                            codiciFiscaliBlocchi20232024.Add(codFiscale);
                        }
                        if (ok2024_2025 == "Y")
                        {
                            codiciFiscaliBlocchi20242025.Add(codFiscale);
                        }
                    }

                    Logger.LogInfo(null, $"Numero di codici fiscali per i blocchi 2023/2024 trovati: {codiciFiscaliBlocchi20232024.Count}");
                    Logger.LogInfo(null, $"Numero di codici fiscali per i blocchi 2024/2025 trovati: {codiciFiscaliBlocchi20242025.Count}");
                }
                // Remove blocks for 2023/2024
                if (codiciFiscaliBlocchi20232024.Count > 0)
                {
                    Logger.LogInfo(25, "Inizio della rimozione dei blocchi per 2023/2024.");
                    BlocksUtil.RemoveBlock(CONNECTION, sqlTransaction, codiciFiscaliBlocchi20232024, "BPP", "20232024", "Verif_perm_sogg");
                    Logger.LogInfo(50, "Fine della rimozione dei blocchi per 2023/2024.");
                }

                // Remove blocks for 2024/2025
                if (codiciFiscaliBlocchi20242025.Count > 0)
                {
                    Logger.LogInfo(75, "Inizio della rimozione dei blocchi per 2024/2025.");
                    BlocksUtil.RemoveBlock(CONNECTION, sqlTransaction, codiciFiscaliBlocchi20242025, "BPP", "20242025", "Verif_perm_sogg");
                    Logger.LogInfo(100, "Fine della rimozione dei blocchi per 2024/2025.");
                }
                sqlTransaction.Commit();
                Logger.LogInfo(100, "Fine della procedura.");
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                Logger.LogError(null, $"Errore durante l'esecuzione di RunProcedure: {ex.Message}");
            }
        }
    }
}
