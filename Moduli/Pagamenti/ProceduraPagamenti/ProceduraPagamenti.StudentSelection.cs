using DocumentFormat.OpenXml;
using ProcedureNet7.PagamentiProcessor;
using ProcedureNet7.Storni;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti
    {
        private void InitializeProcedure(ArgsPagamenti args)
        {
            _masterForm.inProcedure = true;
            AssignArgumentValues(args);
        }
        private void FinalizeProcedure()
        {
            _masterForm.inProcedure = false;
            Logger.LogInfo(100, "Procedura Completa.");
        }
        private void AssignArgumentValues(ArgsPagamenti args)
        {
            selectedSaveFolder = args._selectedSaveFolder;
            selectedAA = args._annoAccademico;
            selectedDataRiferimento = args._dataRiferimento;
            selectedNumeroMandato = args._numeroMandato;
            selectedTipoProcedura = args._tipoProcedura;
            selectedVecchioMandato = args._vecchioMandato;
            categoriaPagam = "";
            selectedCodEnte = "00";
            selectedRichiestoPA = "0";
            usingFiltroManuale = args._filtroManuale;
            cicloTuttiIPagamenti = args._elaborazioneMassivaCheck;
            studenteForzato = args._forzareStudenteCheck;
            studenteForzatoCF = args._forzareStudenteString;
        }

        private void HandleTipoPagamentoDialog()
        {
            if (CONNECTION == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }

            _ = _masterForm.Invoke((MethodInvoker)delegate
            {
                using SelectTipoPagam selectTipoPagam = new(CONNECTION, sqlTransaction);
                selectTipoPagam.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = selectTipoPagam.ShowDialog(_masterForm);
                ProcessTipoPagamentoDialogResult(result, selectTipoPagam);
            });
        }
        private void ProcessTipoPagamentoDialogResult(DialogResult result, SelectTipoPagam selectTipoPagam)
        {
            if (result == DialogResult.OK)
            {
                selectedTipoPagamento = selectTipoPagam.SelectedCodPagamento;
                tipoBeneficio = selectTipoPagam.SelectedTipoBeneficio;
                if ((tipoBeneficio + selectedTipoPagamento).Substring(0, 3) == "BST")
                {
                    isTR = true;
                }

                categoriaPagam = selectTipoPagam.SelectedCategoriaBeneficio;
                codTipoPagamento = tipoBeneficio + selectedTipoPagamento;
            }
            else if (result == DialogResult.Cancel)
            {
                exitProcedureEarly = true;
            }
        }

        private void HandlePagamentoSettingsDialog()
        {
            if (CONNECTION == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }
            _ = _masterForm.Invoke((MethodInvoker)delegate
            {
                string currentBeneficio;

                if (isTR)
                {
                    currentBeneficio = "TR";
                }
                else
                {
                    currentBeneficio = tipoBeneficio;
                }
                using SelectPagamentoSettings selectPagamentoSettings = new(CONNECTION, sqlTransaction, selectedAA, currentBeneficio, categoriaPagam);
                selectPagamentoSettings.StartPosition = FormStartPosition.CenterParent;
                DialogResult dialogResult = selectPagamentoSettings.ShowDialog(_masterForm);
                ProcessPagamentoSettingsDialogResult(dialogResult, selectPagamentoSettings);
            });
        }

        private void ProcessPagamentoSettingsDialogResult(DialogResult dialogResult, SelectPagamentoSettings selectPagamentoSettings)
        {
            if (dialogResult == DialogResult.OK)
            {
                selectedCodEnte = selectPagamentoSettings.InputCodEnte.inputVar;
                tipoStudente = selectPagamentoSettings.InputTipoStud.inputVar;
                selectedImpegno = selectPagamentoSettings.InputImpegno.inputVar;
                impegniList = selectPagamentoSettings.InputImpegno.inputList;
                selectedRichiestoPA = selectPagamentoSettings.InputRichiestaPA.inputVar;
            }
            else if (dialogResult == DialogResult.Cancel)
            {
                exitProcedureEarly = true;
            }
        }

        private void HandleTableNameSelectionDialog()
        {
            _ = _masterForm.Invoke((MethodInvoker)delegate
            {
                using SelectTableName selectTableName = new();
                selectTableName.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = selectTableName.ShowDialog(_masterForm);
                ProcessTableNameSelectionDialogResult(result, selectTableName);
            });
        }
        private void ProcessTableNameSelectionDialogResult(DialogResult result, SelectTableName selectTableName)
        {
            if (result == DialogResult.OK)
            {
                dbTableName = selectTableName.InputText;
                RiepilogoArguments.Instance.nomeTabella = dbTableName;
            }
            else if (result == DialogResult.Cancel)
            {
                exitProcedureEarly = true;
            }
        }

        private void HandleRiepilogoPagamentiDialog()
        {
            _ = _masterForm.Invoke((MethodInvoker)delegate
            {
                using RiepilogoPagamenti riepilogo = new(RiepilogoArguments.Instance);
                riepilogo.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = riepilogo.ShowDialog(_masterForm);
                if (result == DialogResult.Cancel)
                {
                    exitProcedureEarly = true;
                }
            });
        }

        private void CheckAndCreateDatabaseTable()
        {
            Logger.LogDebug(null, "Inizio della verifica e creazione della tabella del database");
            if (CONNECTION == null || sqlTransaction == null)
            {
                Logger.LogDebug(null, "Connessione o transazione non disponibili, uscita anticipata da CheckAndCreateDatabaseTable");
                exitProcedureEarly = true;
                return;
            }

            using (SqlCommand command = new($"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'{dbTableName}'", CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                dbTableExists = command.ExecuteScalar() != null;
            }

            if (selectedTipoProcedura == TipologiaProcedura.Completa.ToCode() || selectedTipoProcedura == TipologiaProcedura.SoloCreazioneTabella.ToCode())
            {
                Logger.LogDebug(null, "Creazione della tabella del database in base al tipo di procedura selezionato");
                CreateDBTable();
            }

            if (selectedTipoProcedura == TipologiaProcedura.SoloCreazioneTabella.ToCode())
            {
                Logger.LogInfo(null, "Verifica o creazione della tabella del database completata");
                exitProcedureEarly = true;
                sqlTransaction?.Commit();
                return;
            }

            if (selectedTipoProcedura == TipologiaProcedura.SenzaCreazioneTabella.ToCode() && !dbTableExists)
            {
                Logger.LogInfo(null, $"Tabella con nome {dbTableName} non esistente nel database");
                exitProcedureEarly = true;
                return;
            }

            Logger.LogDebug(null, "Fine della verifica e creazione della tabella del database");
        }
        private void CreateDBTable()
        {
            Logger.LogInfo(1, $"Creazione Tabella: {dbTableName}");
            Logger.LogInfo(1, $"-");
            //ProgressUpdater progressUpdater = new(1, LogLevel.INFO);
            //progressUpdater.StartUpdating();
            StringBuilder queryBuilder = new();

            if (dbTableExists)
            {
                _ = queryBuilder.Append($@" TRUNCATE TABLE {dbTableName};");
            }
            else
            {
                _ = queryBuilder.Append($@"
                                        CREATE TABLE {dbTableName} (
                                        anno_accademico CHAR(8) COLLATE Latin1_General_CI_AS,
                                        cod_fiscale CHAR(16) COLLATE Latin1_General_CI_AS,
                                        Cognome VARCHAR(75) COLLATE Latin1_General_CI_AS,
                                        Nome VARCHAR(75) COLLATE Latin1_General_CI_AS,
                                        Data_nascita DATETIME,
                                        sesso CHAR(1) COLLATE Latin1_General_CI_AS,
                                        num_domanda NUMERIC (10,0),
                                        cod_tipo_esito CHAR(1) COLLATE Latin1_General_CI_AS,
                                        status_sede CHAR(1) COLLATE Latin1_General_CI_AS,
                                        cod_cittadinanza CHAR(4) COLLATE Latin1_General_CI_AS,
                                        cod_ente CHAR(2) COLLATE Latin1_General_CI_AS,
                                        cod_beneficio CHAR(2) COLLATE Latin1_General_CI_AS,
                                        esitoPA CHAR(1) COLLATE Latin1_General_CI_AS,
                                        anno_corso CHAR(2) COLLATE Latin1_General_CI_AS,
                                        disabile CHAR(1) COLLATE Latin1_General_CI_AS,
                                        esonero_tassa_regionale CHAR(1) COLLATE Latin1_General_CI_AS,
                                        imp_beneficio DECIMAL(8,2),
                                        iscrizione_fuoritermine CHAR(1) COLLATE Latin1_General_CI_AS,
                                        pagamento_tassareg CHAR(1) COLLATE Latin1_General_CI_AS,
                                        blocco_pagamento CHAR(1) COLLATE Latin1_General_CI_AS,
                                        esonero_pag_tassa_reg CHAR(1) COLLATE Latin1_General_CI_AS,
                                        cod_corso SMALLINT,
                                        facolta VARCHAR(150) COLLATE Latin1_General_CI_AS,
                                        sede_studi VARCHAR(3) COLLATE Latin1_General_CI_AS,
                                        superamento_esami CHAR(1) COLLATE Latin1_General_CI_AS,
                                        superamento_esami_tassa_reg CHAR(1) COLLATE Latin1_General_CI_AS,
                                        richiesta_mensa CHAR(1) COLLATE Latin1_General_CI_AS,
                                        Rifug_politico CHAR(1) COLLATE Latin1_General_CI_AS,
                                        Conferma_semestre_filtro CHAR(1) COLLATE Latin1_General_CI_AS,
                                        liquidabile CHAR(1) COLLATE Latin1_General_CI_AS,
                                        note VARCHAR(MAX) COLLATE Latin1_General_CI_AS,
                                        togliere_loreto VARCHAR(4) COLLATE Latin1_General_CI_AS,
                                        togliere_PEC CHAR(1) COLLATE Latin1_General_CI_AS
                                    );");
            }

            _ = queryBuilder.Append($@"         DECLARE @maxDataValidita DATETIME
                                            DECLARE @annoAccademico VARCHAR(8)
                                            DECLARE @codBeneficio CHAR(2)
                                            SET @annoAccademico = '{selectedAA}'
                                            SET @maxDataValidita = '{selectedDataRiferimento}'
                                            SET @codBeneficio = '{tipoBeneficio}';");

            _ = queryBuilder.Append(PagamentiSettings.SQLTabellaAppoggio);
            _ = queryBuilder.Append($@"
                                            ,PagamentiTotali
                                            AS
                                            (
                                                SELECT
                                                 Anno_accademico
                                                ,Num_domanda
                                                FROM
                                                    pagamenti
                                                WHERE Anno_accademico = @annoAccademico and ritirato_azienda = 0 AND (
                                                                    cod_tipo_pagam in (
                                                                        SELECT DISTINCT Cod_tipo_pagam_new
                                                                        FROM Decod_pagam_new inner join 
                                                                            Tipologie_pagam on Decod_pagam_new.Cod_tipo_pagam_new = Tipologie_pagam.Cod_tipo_pagam 
                                                                        WHERE LEFT(Cod_tipo_pagam,2) = '{tipoBeneficio}' AND visibile = 1
                                                                    ) OR
                                                                    cod_tipo_pagam in (
                                                                        SELECT Cod_tipo_pagam_old 
                                                                        FROM Decod_pagam_new inner join 
                                                                            Tipologie_pagam on Decod_pagam_new.Cod_tipo_pagam_new = Tipologie_pagam.Cod_tipo_pagam 
                                                                        WHERE LEFT(Cod_tipo_pagam,2) = '{tipoBeneficio}' AND visibile = 1
                                                                    )
                                                                )
									            GROUP BY Anno_accademico, Num_domanda
                                            )

                                            INSERT INTO {dbTableName}
                                            SELECT DISTINCT
                                                StatisticheTotali.*
                                                ,'1' as liquidabile
                                                ,NULL as note
                                                ,'0' as togliere_loreto
                                                ,'0' as togliere_PEC
                                            FROM
                                                StatisticheTotali  LEFT OUTER JOIN
                                                PagamentiTotali ON StatisticheTotali.Anno_accademico = PagamentiTotali.Anno_accademico AND StatisticheTotali.Num_domanda = PagamentiTotali.Num_domanda 
                                                WHERE 
                                                StatisticheTotali.Anno_accademico = @annoAccademico
                                                ");

            if (!studenteForzato)
            {
                queryBuilder.Append($@"         AND StatisticheTotali.Cod_fiscale IN (
													SELECT 
														Cod_fiscale
													FROM
														vMODALITA_PAGAMENTO
													WHERE
														IBAN <> '' AND IBAN IS NOT NULL 
														AND data_fine_validita IS NULL
														AND conferma_ufficio = 1
												    )
                                                AND StatisticheTotali.num_domanda NOT IN(
                                                    SELECT
                                                        num_domanda
                                                    FROM
                                                        pagamenti
                                                    WHERE anno_accademico=@annoAccademico
                                                    AND cod_tipo_pagam in (
                                                            SELECT Cod_tipo_pagam_old FROM Decod_pagam_new WHERE Cod_tipo_pagam_new = '{codTipoPagamento}'
                                                        )
                                                    OR cod_tipo_pagam = '{codTipoPagamento}'
                                                    )");
            }

                //queryBuilder.AppendLine($"      AND StatisticheTotali.Cod_fiscale = 'DFLLSS05R69D883F'");
            
  
            

            string sqlQuery = queryBuilder.ToString();
            try
            {

                SqlCommand cmd = new(sqlQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 90000000
                };
                Logger.LogDebug(null, "QUERY SQL CREAZIONE TABELLA:");
                _ = cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                //progressUpdater.StopUpdating();
                throw ex;
            }
            finally
            {
                //progressUpdater.StopUpdating();


                Logger.LogInfo(1, "Fine creazione tabella d'appoggio");
            }
        }

        private void HandleFiltroManuale()
        {
            if (CONNECTION == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }
            if (usingFiltroManuale)
            {
                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    using FiltroManuale selectFiltroManuale = new(CONNECTION, sqlTransaction, dbTableName);
                    selectFiltroManuale.StartPosition = FormStartPosition.CenterParent;
                    DialogResult result = selectFiltroManuale.ShowDialog(_masterForm);

                    if (result == DialogResult.OK)
                    {
                        TipoFiltro tipoFiltro = selectFiltroManuale.TipoFiltro;
                        switch (tipoFiltro)
                        {
                            case TipoFiltro.filtroQuery:
                                usingStringWhere = true;
                                break;
                            case TipoFiltro.filtroGuidato:
                                usingStringWhere = false;
                                break;
                        }
                        dictQueryWhere = selectFiltroManuale.DictWhereItems;
                        stringQueryWhere = selectFiltroManuale.StringQueryWhere;
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        exitProcedureEarly = true;
                    }
                });
            }
        }

        private void ClearMovimentiIfNeeded()
        {

            if (!string.IsNullOrWhiteSpace(selectedVecchioMandato))
            {
                if (CONNECTION == null || sqlTransaction == null)
                {
                    exitProcedureEarly = true;
                    return;
                }
                ClearMovimentiContabili();
            }
        }
        private void ClearMovimentiContabili()
        {
            try
            {
                Logger.LogInfo(10, "Set in movimenti contabili elementari dello stato a 0 dove era in elaborazione");
                string updateSQL = $@"          
                UPDATE [MOVIMENTI_CONTABILI_ELEMENTARI] SET stato = 0, codice_movimento = NULL 
                WHERE 
                    stato = 2 AND 
                    segno = 0 AND
                    ID_CAUSALE not in ('01', '02') AND
                    codice_movimento in 
                        (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand updateCmd = new(updateSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = updateCmd.ExecuteNonQuery();

                Logger.LogInfo(10, "Pulizia da movimenti contabili elementari del vecchio codice mandato");
                string deleteSQL = $@"DELETE FROM [MOVIMENTI_CONTABILI_ELEMENTARI] WHERE codice_movimento in 
                              (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand deleteCmd = new(deleteSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = deleteCmd.ExecuteNonQuery();

                Logger.LogInfo(10, "Pulizia da stati del movimento contabile del vecchio codice mandato");
                string deleteStatiSQL = $@"DELETE FROM [STATI_DEL_MOVIMENTO_CONTABILE] WHERE codice_movimento in 
                                   (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand deleteStatiCmd = new(deleteStatiSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = deleteStatiCmd.ExecuteNonQuery();

                Logger.LogInfo(10, "Pulizia da movimenti contabili generali del vecchio codice mandato");
                string deleteGeneraliSQL = $@"DELETE FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%')";
                SqlCommand deleteGeneraliCmd = new(deleteGeneraliSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = deleteGeneraliCmd.ExecuteNonQuery();

                Logger.LogInfo(10, "Starting index optimization");

                // Step to reorganize/rebuild indexes
                string rebuildIndexesSQL = @"
                    DECLARE @TableName NVARCHAR(255);
                    DECLARE IndexCursor CURSOR FOR
                    SELECT DISTINCT t.name 
                    FROM sys.indexes i 
                    INNER JOIN sys.tables t ON i.object_id = t.object_id 
                    WHERE t.name IN ('MOVIMENTI_CONTABILI_ELEMENTARI', 'MOVIMENTI_CONTABILI_GENERALI', 'STATI_DEL_MOVIMENTO_CONTABILE');

                    OPEN IndexCursor;
                    FETCH NEXT FROM IndexCursor INTO @TableName;

                    WHILE @@FETCH_STATUS = 0
                    BEGIN
                        EXEC ('ALTER INDEX ALL ON ' + @TableName + ' REORGANIZE;');
                        FETCH NEXT FROM IndexCursor INTO @TableName;
                    END;

                    CLOSE IndexCursor;
                    DEALLOCATE IndexCursor;
                ";
                SqlCommand rebuildIndexesCmd = new(rebuildIndexesSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = rebuildIndexesCmd.ExecuteNonQuery();

                // Step to update statistics
                Logger.LogInfo(10, "Updating statistics");
                string updateStatisticsSQL = @"
                    UPDATE STATISTICS [MOVIMENTI_CONTABILI_ELEMENTARI];
                    UPDATE STATISTICS [MOVIMENTI_CONTABILI_GENERALI];
                    UPDATE STATISTICS [STATI_DEL_MOVIMENTO_CONTABILE];
                 ";
                SqlCommand updateStatisticsCmd = new(updateStatisticsSQL, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                _ = updateStatisticsCmd.ExecuteNonQuery();

                Logger.LogInfo(10, "Index and statistics optimization complete.");
            }
            catch
            {
                throw;
            }
        }

        private void GenerateStudentListToPay()
        {
            Logger.LogDebug(null, "Inizio della generazione della lista degli studenti per il pagamento");
            string dataQuery = $"SELECT * FROM {dbTableName}";
            if (usingFiltroManuale)
            {
                if (usingStringWhere)
                {
                    if (!stringQueryWhere.TrimStart().StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                    {
                        stringQueryWhere = "WHERE " + stringQueryWhere;
                    }
                    dataQuery += $@" {stringQueryWhere} AND anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_PEC = '0'";
                    Logger.LogDebug(null, "Query costruita con filtro manuale utilizzando stringQueryWhere.");
                }
                else
                {
                    int countConditions = 0;
                    StringBuilder conditionalBuilder = new();
                    if (dictQueryWhere["Sesso"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" sesso in ({dictQueryWhere["Sesso"]}) AND ");
                    }
                    if (dictQueryWhere["StatusSede"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" status_sede in ({dictQueryWhere["StatusSede"]}) AND ");
                    }
                    if (dictQueryWhere["Cittadinanza"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" cod_cittadinanza in ({dictQueryWhere["Cittadinanza"]}) AND ");
                    }
                    if (dictQueryWhere["CodEnte"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" cod_ente in ({dictQueryWhere["CodEnte"]}) AND ");
                    }
                    if (dictQueryWhere["EsitoPA"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" esitoPA in ({dictQueryWhere["EsitoPA"]}) AND ");
                    }
                    if (dictQueryWhere["AnnoCorso"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" anno_corso in ({dictQueryWhere["AnnoCorso"]}) AND ");
                    }
                    if (dictQueryWhere["Disabile"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" disabile in ({dictQueryWhere["Disabile"]}) AND ");
                    }
                    if (dictQueryWhere["TipoCorso"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" cod_corso in ({dictQueryWhere["TipoCorso"]}) AND ");
                    }
                    if (dictQueryWhere["SedeStudi"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" sede_studi in ({dictQueryWhere["SedeStudi"]}) AND ");
                    }
                    if (dictQueryWhere["TogliereLoreto"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" togliere_loreto IN ({dictQueryWhere["TogliereLoreto"]}) AND ");
                    }
                    if (dictQueryWhere["SemestreFiltro"] != "''")
                    {
                        countConditions++;
                        _ = conditionalBuilder.Append($" Conferma_semestre_filtro in ({dictQueryWhere["SemestreFiltro"]}) AND ");
                    }

                    if (conditionalBuilder.Length > 0)
                    {
                        conditionalBuilder.Length -= " AND ".Length;
                    }

                    string whereString = conditionalBuilder.ToString();
                    dataQuery += $" WHERE anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_PEC = '0' AND " + whereString;

                    if (conditionalBuilder.Length <= 0)
                    {
                        Logger.LogError(null, "Errore nella costruzione della query - Selezionare almeno un parametro se si usa il filtro manuale");
                        throw new Exception("Errore nella costruzione della query - Selezionare almeno un parametro se si usa il filtro manuale");
                    }
                    Logger.LogDebug(null, $"Query costruita con {countConditions} condizioni utilizzando filtro manuale.");
                }
            }
            else
            {
                dataQuery += $@" WHERE anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_loreto = '0' AND Togliere_PEC = '0'";
                Logger.LogDebug(null, "Query costruita senza filtro manuale.");
            }

            Logger.LogInfo(20, "Generazione della lista degli studenti in corso");

            SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 900000
            };
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                int studentCount = 0;
                while (reader.Read())
                {
                    _ = int.TryParse(Utilities.SafeGetString(reader, "disabile"), out int disabile);
                    _ = int.TryParse(Utilities.SafeGetString(reader, "esonero_tassa_regionale"), out int esoneroTassaRegionale);
                    _ = int.TryParse(Utilities.SafeGetString(reader, "Superamento_esami"), out int superamentoEsami);
                    _ = int.TryParse(Utilities.SafeGetString(reader, "Superamento_esami_tassa_reg"), out int superamentoEsamiTassaRegionale);
                    _ = int.TryParse(Utilities.SafeGetString(reader, "anno_corso"), out int annoCorso);
                    int esitoPA = Utilities.SafeGetInt(reader, "EsitoPA");
                    string studenteCodEnte = Utilities.SafeGetString(reader, "cod_ente");

                    bool skipTipoStudente = false;
                    bool skipCodEnte = false;

                    if (esitoPA == 2 && (selectedRichiestoPA == "0" || selectedRichiestoPA == "3"))
                    {
                        continue;
                    }

                    switch (tipoStudente)
                    {
                        case "0":
                            skipTipoStudente = annoCorso != 1;
                            break;
                        case "1":
                            skipTipoStudente = annoCorso == 1;
                            break;
                        case "2":
                            skipTipoStudente = false;
                            break;
                    }

                    if (selectedCodEnte != "00")
                    {
                        skipCodEnte = studenteCodEnte != selectedCodEnte;
                    }

                    if (skipTipoStudente || skipCodEnte)
                    {
                        continue;
                    }


                    StudentePagamenti studente = new(
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "num_domanda")),
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_fiscale").ToUpper()),
                            Utilities.SafeGetString(reader, "Cognome").Trim().ToUpper(),
                            Utilities.SafeGetString(reader, "Nome").Trim().ToUpper(),
                            ((DateTime)reader["Data_nascita"]).ToString("dd/MM/yyyy"),
                            Utilities.SafeGetString(reader, "sesso").Trim().ToUpper(),
                            studenteCodEnte,
                            Utilities.SafeGetString(reader, "Cod_cittadinanza"),
                            disabile == 1,
                            esoneroTassaRegionale == 1,
                            double.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "imp_beneficio")), out double importoBeneficio) ? importoBeneficio : 0,
                            annoCorso,
                            int.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_corso")), out int codCorso) ? codCorso : 0,
                            esitoPA,
                            superamentoEsami == 1,
                            superamentoEsamiTassaRegionale == 1,
                            Utilities.SafeGetString(reader, "Status_sede").Trim().ToUpper(),
                            Utilities.SafeGetInt(reader, "Rifug_politico") == 1
                        );

                    studentiDaPagare.Add(studente.InformazioniPersonali.CodFiscale, studente);
                    studentCount++;
                }

                Logger.LogInfo(null, $"Elaborati {studentCount} studenti nella query.");
            }
            Logger.LogInfo(20, $"UPDATE:Generazione studenti - Completato");
        }
    }
}
