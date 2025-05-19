using DocumentFormat.OpenXml;
using ProcedureNet7.PagamentiProcessor;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    public class ProceduraPagamenti : BaseProcedure<ArgsPagamenti>
    {
        string selectedSaveFolder = string.Empty;
        string selectedAA = string.Empty;

        string selectedCodEnte = string.Empty;
        string selectedDataRiferimento = string.Empty;
        string selectedNumeroMandato = string.Empty;
        string selectedVecchioMandato = string.Empty;
        string selectedTipoProcedura = string.Empty;
        string selectedTipoPagamento = string.Empty;
        string selectedRichiestoPA = string.Empty;
        string dbTableName = string.Empty;
        bool dbTableExists;
        string tipoStudente = string.Empty;
        string tipoBeneficio = string.Empty;
        string codTipoPagamento = string.Empty;
        string selectedImpegno = string.Empty;
        string categoriaPagam = string.Empty;
        bool isIntegrazione = false;
        bool isRiemissione = false;

        bool usingFiltroManuale = false;

        double importoTotale = 0;

        bool massivoDefault = false;
        string massivoString = string.Empty;

        bool studenteForzato = false;
        string studenteForzatoCF = string.Empty;

        Dictionary<string, StudentePagam> studentiDaPagare = new();
        readonly Dictionary<StudentePagam, List<string>> studentiConErroriPA = new();

        List<string> impegniList = new();

        Dictionary<string, string> dictQueryWhere = new();
        string stringQueryWhere = string.Empty;
        bool usingStringWhere = false;

        bool isTR = false;
        bool insertInDatabase = false;

        SqlTransaction? sqlTransaction = null;
        bool exitProcedureEarly = false;

        public int studentiProcessatiAmount = 0;
        public Dictionary<string, Dictionary<string, int>> impegnoAmount = new Dictionary<string, Dictionary<string, int>>();

        IAcademicYearProcessor? selectedAcademicProcessor;

        public ProceduraPagamenti(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsPagamenti args)
        {
            Logger.LogDebug(null, "Inizio dell'esecuzione di RunProcedure");
            try
            {
                if (CONNECTION == null)
                {
                    Logger.LogError(null, "CONNESSIONE ASSENTE O NULLA");
                    return;
                }
                if (_masterForm == null)
                {
                    Logger.LogError(null, "MASTER FORM NULLO!!!");//come
                    return;
                }
                InitializeProcedure(args);
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo InitializeProcedure");
                    return;
                }

                ProcessorChooser processorChooser = new ProcessorChooser();
                selectedAcademicProcessor = processorChooser.GetProcessor(selectedAA);
                if (selectedAcademicProcessor == null)
                {
                    Logger.LogDebug(null, "Processor non può essere nullo in questo punto!");
                    return;
                }

                sqlTransaction = CONNECTION.BeginTransaction();

                if (this.CONNECTION == null || sqlTransaction == null)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                    _masterForm.inProcedure = false;
                    sqlTransaction?.Rollback();
                    return;
                }
                if (!massivoDefault)
                {
                    HandleTipoPagamentoDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTipoPagamentoDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    HandlePagamentoSettingsDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandlePagamentoSettingsDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }
                }
                else
                {
                    HandleDefaultMassivo();
                }

                HandleTableNameSelectionDialog();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTableNameSelectionDialog");
                    _masterForm.inProcedure = false;
                    sqlTransaction?.Rollback();
                    return;
                }

                if (!massivoDefault)
                {
                    HandleRiepilogoPagamentiDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleRiepilogoPagamentiDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }
                }

                CheckAndCreateDatabaseTable();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo CheckAndCreateDatabaseTable");
                    _masterForm.inProcedure = false;
                    sqlTransaction?.Rollback();
                    return;
                }

                HandleFiltroManuale();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleFiltroManuale");
                    _masterForm.inProcedure = false;
                    sqlTransaction?.Rollback();
                    return;
                }

                ClearMovimentiIfNeeded();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo ClearMovimentiIfNeeded");
                    _masterForm.inProcedure = false;
                    sqlTransaction?.Rollback();
                    return;
                }

                GenerateStudentListToPay();
                ProcessStudentList();
                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    DialogResult result = MessageBox.Show(_masterForm, "Completare procedura?", "Attenzione", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    {
                        if (result == DialogResult.OK)
                        {
                            Logger.LogInfo(100, $"Numero studenti lavorati: {studentiProcessatiAmount}", System.Drawing.Color.DarkGreen);
                            foreach (KeyValuePair<string, Dictionary<string, int>> impegno in impegnoAmount)
                            {
                                int totaleImpegno = 0;
                                foreach (KeyValuePair<string, int> ente in impegno.Value)
                                {
                                    totaleImpegno += ente.Value;
                                }

                                Logger.LogInfo(null, $" - di cui: {totaleImpegno} con impegno n°{impegno.Key}", ColorTranslator.FromHtml("#A4449A"));

                                foreach (KeyValuePair<string, int> ente in impegno.Value)
                                {
                                    Logger.LogInfo(null, $" - - di cui: {ente.Value} per {ente.Key}", System.Drawing.Color.DarkBlue);
                                }
                            }
                            Logger.LogInfo(null, $"Totale pagamenti: {Math.Round(importoTotale, 2)} €");
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
                Logger.LogError(null, $"Errore: {ex.Message}");
                sqlTransaction?.Rollback();
                throw;
            }
            finally
            {
                Logger.LogDebug(100, "Fine dell'esecuzione di RunProcedure");
                FinalizeProcedure();
            }
        }

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
            massivoDefault = args._elaborazioneMassivaCheck;
            massivoString = args._elaborazioneMassivaString;
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

        private void HandleDefaultMassivo()
        {
            if (CONNECTION == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }

            List<string> parts = massivoString.Split(new[] { '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (parts.Count != 7)
            {
                throw new Exception("Stringa elaborazione massiva errata!");
            }

            tipoBeneficio = parts[0];
            selectedTipoPagamento = parts[1];
            categoriaPagam = parts[2];
            selectedCodEnte = parts[3];
            tipoStudente = parts[4];
            selectedRichiestoPA = parts[5];
            selectedImpegno = parts[6];

            codTipoPagamento = tipoBeneficio + selectedTipoPagamento;
            if ((codTipoPagamento).Substring(0, 3) == "BST")
            {
                isTR = true;
            }
            SqlCommand readData = new($"SELECT * FROM impegni WHERE Cod_beneficio = '{tipoBeneficio}' and anno_accademico = '{selectedAA}' and categoria_pagamento = '{categoriaPagam}'", CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    impegniList.Add(Utilities.SafeGetString(reader, "num_impegno"));
                }
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
            ProgressUpdater progressUpdater = new(1, LogLevel.INFO);
            progressUpdater.StartUpdating();
            StringBuilder queryBuilder = new();

            if (dbTableExists)
            {
                _ = queryBuilder.Append($@" TRUNCATE TABLE {dbTableName};");
            }
            else
            {
                _ = queryBuilder.Append($@"
                                        CREATE TABLE {dbTableName} (
	                                        anno_accademico CHAR(8),
	                                        cod_fiscale CHAR(16),
	                                        Cognome VARCHAR(75),
	                                        Nome VARCHAR(75),
	                                        Data_nascita DATETIME,
                                            sesso CHAR(1),
	                                        num_domanda NUMERIC (10,0),
	                                        cod_tipo_esito CHAR(1),
	                                        status_sede CHAR(1),
	                                        cod_cittadinanza CHAR(4),
	                                        cod_ente CHAR(2),
	                                        cod_beneficio CHAR(2),
                                            esitoPA CHAR(1),
	                                        anno_corso CHAR(2),
	                                        disabile CHAR(1),
	                                        imp_beneficio DECIMAL(8,2),
	                                        iscrizione_fuoritermine CHAR(1),
	                                        pagamento_tassareg CHAR(1),
	                                        blocco_pagamento CHAR(1),
	                                        esonero_pag_tassa_reg CHAR(1),
	                                        cod_corso SMALLINT,
	                                        facolta VARCHAR(150),
	                                        sede_studi VARCHAR(3),
	                                        superamento_esami CHAR(1),
	                                        superamento_esami_tassa_reg CHAR(1),
                                            richiesta_mensa CHAR(1),
                                            Rifug_politico CHAR(1),
                                            liquidabile CHAR(1),
                                            note VARCHAR(MAX),
                                            togliere_loreto VARCHAR(4),
                                            togliere_PEC CHAR(1)
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
            if (studenteForzato)
            {
                queryBuilder.AppendLine($"      AND StatisticheTotali.Cod_fiscale = '{studenteForzatoCF}'");
            }

            string sqlQuery = queryBuilder.ToString();
            try
            {

                SqlCommand cmd = new(sqlQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 90000000
                };
                _ = cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                progressUpdater.StopUpdating();
                throw ex;
            }
            finally
            {
                progressUpdater.StopUpdating();
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


                    StudentePagam studente = new(
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "num_domanda")),
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_fiscale").ToUpper()),
                            Utilities.SafeGetString(reader, "Cognome").Trim().ToUpper(),
                            Utilities.SafeGetString(reader, "Nome").Trim().ToUpper(),
                            ((DateTime)reader["Data_nascita"]).ToString("dd/MM/yyyy"),
                            Utilities.SafeGetString(reader, "sesso").Trim().ToUpper(),
                            studenteCodEnte,
                            Utilities.SafeGetString(reader, "Cod_cittadinanza"),
                            disabile == 1,
                            double.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "imp_beneficio")), out double importoBeneficio) ? importoBeneficio : 0,
                            annoCorso,
                            int.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_corso")), out int codCorso) ? codCorso : 0,
                            esitoPA,
                            superamentoEsami == 1,
                            superamentoEsamiTassaRegionale == 1,
                            Utilities.SafeGetString(reader, "Status_sede").Trim().ToUpper(),
                            Utilities.SafeGetInt(reader, "Rifug_politico") == 1
                        );

                    studentiDaPagare.Add(studente.codFiscale, studente);
                    studentCount++;
                }

                Logger.LogInfo(null, $"Elaborati {studentCount} studenti nella query.");
            }
            Logger.LogInfo(20, $"UPDATE:Generazione studenti - Completato");
        }
        private void ProcessStudentList()
        {
            if (studentiDaPagare.Count == 0)
            {
                return;
            }
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");
            List<string> codFiscali = studentiDaPagare.Keys.ToList();

            Logger.LogDebug(null, "Creazione tabella CF");
            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
            using (SqlCommand createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                createCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Inserimento in tabella CF dei codici fiscali");
            Logger.LogInfo(30, "Lavorazione studenti - creazione tabella codici fiscali");

            // Create a DataTable to hold the fiscal codes
            using (DataTable cfTable = new DataTable())
            {
                cfTable.Columns.Add("Cod_fiscale", typeof(string));

                foreach (var cf in codFiscali)
                {
                    cfTable.Rows.Add(cf);
                }

                // Use SqlBulkCopy to efficiently insert the data into the temporary table
                using SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
                bulkCopy.DestinationTableName = "#CFEstrazione";
                bulkCopy.WriteToServer(cfTable);
            }

            Logger.LogDebug(null, "Creazione index della tabella CF");
            string indexingCFTable = "CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale)";
            using (SqlCommand indexingCFTableCmd = new SqlCommand(indexingCFTable, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                indexingCFTableCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
            string updateStatistics = "UPDATE STATISTICS #CFEstrazione";
            using (SqlCommand updateStatisticsCmd = new SqlCommand(updateStatistics, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                updateStatisticsCmd.ExecuteNonQuery();
            }
            #endregion

            ControlloPagamenti();
            Logger.LogDebug(30, $"Numero studenti prima della pulizia = {studentiDaPagare.Count}");
            if (studentiDaPagare.Count == 0)
            {
                string dropCFTable = "DROP TABLE #CFEstrazione;";
                SqlCommand drop = new(dropCFTable, CONNECTION, sqlTransaction);
                _ = drop.ExecuteNonQuery();
                return;
            }
            CheckLiquefazione();
            Logger.LogDebug(30, $"Numero studenti dopo la pulizia = {studentiDaPagare.Count}");


            if (studentiDaPagare.Count > 0)
            {
                PopulateStudentsInformations();

                List<StudentePagam> studenti = studentiDaPagare.Values.ToList();
                bool continueProcessing = true;
                _ = _masterForm?.Invoke((MethodInvoker)delegate
                {
                    if (MessageBox.Show(_masterForm, "Do you want to see an overview of the current student list?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        continueProcessing = ShowStudentOverview(studenti);
                    }

                    bool ShowStudentOverview(List<StudentePagam> studenti)
                    {
                        bool result = false;
                        _ = _masterForm.Invoke((MethodInvoker)delegate
                        {
                            using var overviewForm = new StudentOverview(studenti, ref studentiDaPagare, _masterForm);
                            if (overviewForm.ShowDialog() == DialogResult.OK)
                            {
                                result = true;
                            }
                        });
                        return result;
                    }
                });

                if (!continueProcessing)
                {
                    string dropTempTable1 = "DROP TABLE #CFEstrazione;";
                    SqlCommand dropCmd1 = new(dropTempTable1, CONNECTION, sqlTransaction);
                    _ = dropCmd1.ExecuteNonQuery();
                    return;
                }

                bool generateFiles = false;
                insertInDatabase = false;
                _ = _masterForm?.Invoke((MethodInvoker)delegate
                {
                    if (MessageBox.Show(_masterForm, "Generate students files?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        generateFiles = true;
                        if (MessageBox.Show(_masterForm, "Insert into DB?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            insertInDatabase = true;
                        }
                    }
                });

                List<StudentePagam> studentiAfter = studentiDaPagare.Values.ToList();
                if (studentiDaPagare.Count > 0 && generateFiles)
                {
                    GenerateOutputFiles();
                }
            }

            string dropTempTable = "DROP TABLE #CFEstrazione;";
            SqlCommand dropCmd = new(dropTempTable, CONNECTION, sqlTransaction);
            _ = dropCmd.ExecuteNonQuery();
        }
        private void ControlloPagamenti()
        {
            if (!studenteForzato)
            {
                ControlloInMovimentazioni();
            }

            FilterPagamenti();

            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR && !studenteForzato && categoriaPagam != "PR")
            {
                ControlloProvvedimenti();
            }

            void ControlloInMovimentazioni()
            {
                Logger.LogDebug(null, "Inizio del filtraggio dei pagamenti degli studenti");
                string sqlPagam = $@"
                    SELECT        
                        CODICE_FISCALE
                    FROM            
                        MOVIMENTI_CONTABILI_ELEMENTARI
                    WHERE           
                        (CODICE_MOVIMENTO IN
                            (SELECT        CODICE_MOVIMENTO
                            FROM            MOVIMENTI_CONTABILI_GENERALI
                            WHERE        (COD_MANDATO LIKE '{codTipoPagamento}%')))
                            ";
                HashSet<string> studentiDaRimuovereHash = new();
                SqlCommand readData = new(sqlPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(11, $"Lavorazione studenti - controllo movimentazioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpper());

                        if (codFiscale == "BLDLSS00M23E958G")
                        {
                            string test = "";
                        }

                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        if (studente != null)
                        {
                            studentiDaRimuovereHash.Add(studente.codFiscale);
                            continue;
                        }
                    }
                }

                foreach (string codFiscale in studentiDaRimuovereHash)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereHash.Count} studenti dalla lista di pagamento");
            }

            void FilterPagamenti()
            {
                Logger.LogDebug(null, "Inizio del filtraggio dei pagamenti degli studenti");
                string sqlPagam = $@"
                    SELECT distinct
                        Domanda.Cod_fiscale,
                        Decod_pagam_new.Cod_tipo_pagam_new AS Cod_tipo_pagam,
                        Pagamenti.Imp_pagato,
                        Ritirato_azienda
                    FROM
                        Pagamenti
                        INNER JOIN Domanda ON Pagamenti.anno_accademico = Domanda.anno_accademico AND Pagamenti.num_domanda = Domanda.Num_domanda
                        INNER JOIN #CFestrazione cf ON Domanda.cod_fiscale = cf.Cod_fiscale
                        INNER JOIN Decod_pagam_new ON Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_old OR Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_new
                            ";
                if (tipoBeneficio != TipoBeneficio.PremioDiLaurea.ToCode())
                {
                    sqlPagam += $@"
                    WHERE
                        Domanda.Anno_accademico = '{selectedAA}'";
                }
                SqlCommand readData = new(sqlPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(11, $"Lavorazione studenti - inserimento pagamenti");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                        if (codFiscale == "MSTSLV99T61E958K")
                        {
                            string test = "";
                        }

                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        if (studente != null)
                        {
                            string cod_tipo_pagam = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_tipo_pagam")).ToUpper();

                            if (!studenteForzato)
                            {
                                if (cod_tipo_pagam.Substring(0, 2) != tipoBeneficio)
                                {
                                    continue;
                                }

                                if (cod_tipo_pagam.Substring(0, 3) == "BST" && !isTR)
                                {
                                    continue;
                                }

                                if (cod_tipo_pagam.Substring(0, 2) != "BS" && isTR)
                                {
                                    continue;
                                }
                            }
                            double.TryParse(Utilities.SafeGetString(reader, "Imp_pagato"), out double impPagato);
                            studente.AddPagamentoEffettuato(
                                cod_tipo_pagam,
                                impPagato,
                                Utilities.SafeGetString(reader, "Ritirato_azienda") == "1"
                                );
                        }
                    }
                }

                HashSet<string> studentiDaRimuovereHash = new();
                foreach (var pair in studentiDaPagare)
                {
                    if (studenteForzato) { continue; }

                    StudentePagam studenteDaControllare = pair.Value;
                    bool stessoPagamento = false;
                    bool okTassaRegionale = false;

                    if (studenteDaControllare.pagamentiEffettuati == null || studenteDaControllare.pagamentiEffettuati.Count <= 0)
                    {
                        continue;
                    }
                    double importiPagati = 0;
                    foreach (Pagamento pagamento in studenteDaControllare.pagamentiEffettuati)
                    {
                        if (pagamento.ritiratoAzienda)
                        {
                            continue;
                        }
                        if (pagamento.codTipoPagam == codTipoPagamento)
                        {
                            stessoPagamento = true;
                            if (tipoBeneficio == TipoBeneficio.PremioDiLaurea.ToCode())
                            {
                                stessoPagamento = false;
                                Logger.LogWarning(null, $"Attenzione: Studente con cf {studenteDaControllare.codFiscale} ha già preso il premio di laurea!");
                            }
                            break;
                        }

                        if (isTR &&
                            (
                                (
                                    (studenteDaControllare.superamentoEsami
                                     || studenteDaControllare.superamentoEsamiTassaRegionale)
                                    && (pagamento.codTipoPagam == "BSS0"
                                        || pagamento.codTipoPagam == "BSS1"
                                        || pagamento.codTipoPagam == "BSS2")
                                    && !pagamento.ritiratoAzienda
                                )
                                ||
                                (
                                    studenteDaControllare.superamentoEsamiTassaRegionale
                                    && (pagamento.codTipoPagam == "BSP0"
                                        || pagamento.codTipoPagam == "BSP1"
                                        || pagamento.codTipoPagam == "BSP2")
                                    && !pagamento.ritiratoAzienda
                                )
                            )
                        )
                        {
                            okTassaRegionale = true;
                        }


                        importiPagati += pagamento.importoPagamento;
                    }
                    if ((stessoPagamento || (isTR && !okTassaRegionale)) && !studentiDaRimuovereHash.Contains(studenteDaControllare.codFiscale))
                    {
                        studentiDaRimuovereHash.Add(studenteDaControllare.codFiscale);
                        continue;
                    }

                    Math.Round(importiPagati, 2);
                    studenteDaControllare.SetImportiPagati(importiPagati);

                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR && Math.Abs(studenteDaControllare.importoBeneficio - studenteDaControllare.importoPagato) < 5)
                    {
                        studentiDaRimuovereHash.Add(studenteDaControllare.codFiscale);
                    }
                }

                foreach (string codFiscale in studentiDaRimuovereHash)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereHash.Count} studenti dalla lista di pagamento");

                string lastValue = codTipoPagamento[3..];
                string firstPart = codTipoPagamento[..3];
                string integrazioneValue = codTipoPagamento.Substring(2, 1);
                if (integrazioneValue == "I")
                {
                    isIntegrazione = true;
                    Logger.LogInfo(null, "Il tipo di pagamento indica una integrazione");
                }
                if (lastValue != "0" && lastValue != "9" && lastValue != "6" && lastValue != "I")
                {
                    ControlloRiemissioni(firstPart, lastValue);
                    isRiemissione = true;
                    Logger.LogInfo(null, "Il tipo di pagamento indica una riemissione");
                }
                else if (integrazioneValue == "I")
                {
                    ControlloIntegrazioni();
                    Logger.LogInfo(null, "Controllo integrazioni eseguito per i tipi di pagamento con integrazione");
                }
            }

            void ControlloRiemissioni(string firstPart, string lastValue)
            {
                Logger.LogDebug(null, "Inizio del controllo delle riemissioni per gli studenti");
                HashSet<string> studentiDaRimuovere = new();
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagam studente = pair.Value;
                    if (studente.codFiscale == "BLDLSS00M23E958G")
                    {
                        string test = "";
                    }
                    if (studente.pagamentiEffettuati == null || studente.pagamentiEffettuati.Count <= 0)
                    {
                        studentiDaRimuovere.Add(studente.codFiscale);
                        continue;
                    }

                    bool pagamentoPossibile = false;
                    foreach (Pagamento pagamento in studente.pagamentiEffettuati)
                    {
                        string pagamFirstPart = pagamento.codTipoPagam[..3];
                        string pagamLastValue = pagamento.codTipoPagam[3..];
                        if (pagamFirstPart != firstPart)
                        {
                            continue;
                        }

                        if (!pagamento.ritiratoAzienda)
                        {
                            continue;
                        }

                        bool conditionMet = false;
                        switch (pagamLastValue)
                        {
                            case "0":
                                conditionMet = lastValue == "1";
                                break;
                            case "6":
                                conditionMet = lastValue == "7";
                                break;
                            case "9":
                                conditionMet = lastValue == "A";
                                break;
                            case "1":
                                conditionMet = lastValue == "2";
                                break;
                            case "A":
                                conditionMet = lastValue == "B";
                                break;
                            case "7":
                                conditionMet = lastValue == "8";
                                break;
                        }

                        if (conditionMet)
                        {
                            pagamentoPossibile = true;
                            break;
                        }
                    }
                    if (!pagamentoPossibile)
                    {
                        studentiDaRimuovere.Add(studente.codFiscale);
                    }
                }
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovere.Count} studenti dalla lista di pagamento dopo il controllo delle riemissioni");
            }

            void ControlloIntegrazioni()
            {
                Logger.LogDebug(null, "Inizio del controllo delle integrazioni per gli studenti");
                HashSet<string> studentiDaRimuovere = new();
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagam studente = pair.Value;
                    if (studente.pagamentiEffettuati == null || studente.pagamentiEffettuati.Count <= 0)
                    {
                        studentiDaRimuovere.Add(studente.codFiscale);
                        continue;
                    }
                    bool pagamentoPossibile = false;
                    foreach (Pagamento pagamento in studente.pagamentiEffettuati)
                    {
                        string pagamentoBeneficio = pagamento.codTipoPagam.Substring(0, 2);
                        if (pagamentoBeneficio != tipoBeneficio)
                        {
                            continue;
                        }

                        if (pagamento.ritiratoAzienda)
                        {
                            continue;
                        }

                        if (pagamento.codTipoPagam == codTipoPagamento)
                        {
                            continue;
                        }

                        string codCatPagam = pagamento.codTipoPagam.Substring(2, 2);
                        switch (codCatPagam)
                        {
                            case "P0":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "P1":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "P2":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S0":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S1":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S2":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "I9":
                                if (selectedTipoPagamento == "II")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                        }
                    }
                    if (!pagamentoPossibile)
                    {
                        studentiDaRimuovere.Add(studente.codFiscale);
                    }
                }
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovere.Count} studenti dalla lista di pagamento dopo il controllo delle integrazioni");
            }
            void ControlloProvvedimenti()
            {
                string? sqlProvv = selectedAcademicProcessor.GetProvvedimentiQuery(selectedAA, tipoBeneficio);

                SqlCommand readData = new(sqlProvv, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(null, "Lavorazione studenti - controllo provvedimenti");
                HashSet<string> listaStudentiDaMantenere = new();

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    listaStudentiDaMantenere = selectedAcademicProcessor.ProcessProvvedimentiQuery(reader);
                }

                HashSet<string> studentiDaRimuovere = new();
                // Find students to remove
                foreach (var pair in studentiDaPagare)
                {
                    if (!listaStudentiDaMantenere.Contains(pair.Key))
                    {
                        studentiDaRimuovere.Add(pair.Key);
                    }
                }

                DataTable studentiDaRimuovereTable = new();
                studentiDaRimuovereTable.Columns.Add("CodFiscale");
                Logger.LogInfo(null, $"Trovati {studentiDaRimuovere.Count} studenti senza provvedimento");
                // Remove students not present in the query
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaRimuovereTable.Rows.Add(codFiscale);
                    studentiDaPagare.Remove(codFiscale);
                }
                if (studentiDaRimuovereTable.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(studentiDaRimuovereTable, selectedSaveFolder, fileName: "Studenti senza provvedimento modifica importo");
                }
            }
        }

        private void CheckLiquefazione()
        {
            // Check for payment blocks
            string sqlKiller = $@"
                    SELECT DISTINCT
                        Domanda.cod_fiscale 
                    FROM 
                        Domanda 
                        INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE
                        Domanda.anno_accademico = '{selectedAA}' and
                        Domanda.num_domanda in (
                            SELECT DISTINCT Num_domanda
                                FROM vMotivazioni_blocco_pagamenti
                                WHERE Anno_accademico = '{selectedAA}' 
                                    AND Data_fine_validita IS NULL 
                                    AND Blocco_pagamento_attivo = 1";
            if (tipoBeneficio == TipoBeneficio.BuonoLibro.ToCode())
            {
                sqlKiller += " AND cod_tipologia_blocco in ('BPD', 'BSS', 'BS1')";
            }
            sqlKiller += " )";

            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode())
            {
                sqlKiller += " AND Domanda.tipo_bando like 'L%'";
            }

            if (tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode())
            {
                sqlKiller += " AND Domanda.tipo_bando like 'CS'";
            }

            SqlCommand readData = new(sqlKiller, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili");
            HashSet<string> listaStudentiDaEliminareBlocchi = new();
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == "BLDLSS00M23E958G")
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                    if (studente != null && !listaStudentiDaEliminareBlocchi.Contains(studente.codFiscale))
                    {
                        listaStudentiDaEliminareBlocchi.Add(studente.codFiscale);
                    }
                }
            }
            string listaCFblocchi = string.Join(", ", listaStudentiDaEliminareBlocchi.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFblocchi))
            {
                string sqlUpdateBlocchi = $@"
                    UPDATE {dbTableName}
                        SET togliere_loreto = 'BLC',
                            note = 'Studente con blocchi al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFblocchi})
                    ";
                SqlCommand blocchiUpdate = new(sqlUpdateBlocchi, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                blocchiUpdate.ExecuteNonQuery();
            }

            // Check for missing IBAN
            string sqlStudentiSenzaIBAN = $@" 
                SELECT DISTINCT
                    vMODALITA_PAGAMENTO.Cod_fiscale, vMODALITA_PAGAMENTO.IBAN
                FROM
                    vMODALITA_PAGAMENTO 
                    INNER JOIN #CFEstrazione cfe ON vMODALITA_PAGAMENTO.Cod_fiscale = cfe.Cod_fiscale 
             ";
            SqlCommand IBANDATA = new(sqlStudentiSenzaIBAN, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo IBAN");
            HashSet<string> listaStudentiDaEliminareIBAN = new();
            HashSet<string> listaStudentiDaBloccareIBAN = new();
            using (SqlDataReader reader = IBANDATA.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == "BLDLSS00M23E958G")
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                    if (studente != null)
                    {
                        string IBAN = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();
                        if (IBAN == string.Empty && !listaStudentiDaEliminareIBAN.Contains(studente.codFiscale))
                        {
                            listaStudentiDaEliminareIBAN.Add(studente.codFiscale);
                        }

                        bool ibanValido = IbanValidatorUtil.ValidateIban(IBAN);

                        if (!ibanValido && !listaStudentiDaBloccareIBAN.Contains(studente.codFiscale))
                        {
                            listaStudentiDaBloccareIBAN.Add(studente.codFiscale);
                        }
                    }
                }
            }
            string listaCFIBANMancante = string.Join(", ", listaStudentiDaEliminareIBAN.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFIBANMancante))
            {
                string sqlUpdateIban = $@"
                    UPDATE {dbTableName}
                        SET liquidabile = 0,
                            note = 'Studente senza IBAN valido al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFIBANMancante})
                    ";
                SqlCommand ibanUpdate = new(sqlUpdateIban, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                ibanUpdate.ExecuteNonQuery();
            }

            string listaCFIBANNonValido = string.Join(", ", listaStudentiDaEliminareIBAN.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFIBANNonValido))
            {
                string sqlUpdateIban = $@"
                    UPDATE {dbTableName}
                        SET liquidabile = 0,
                            note = 'Studente con IBAN errato al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFIBANNonValido})
                    ";
                SqlCommand ibanUpdate = new(sqlUpdateIban, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                ibanUpdate.ExecuteNonQuery();
                BlocksUtil.AddBlock(CONNECTION, sqlTransaction, listaStudentiDaEliminareIBAN.ToList<string>(), "BSS", selectedAA, "IBAN_Check", true);
            }

            // Check for non-winners
            string sqlStudentiNonVincitori = $@" 
                SELECT DISTINCT
                    Domanda.cod_fiscale
                FROM
                    Domanda 
                    INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    INNER JOIN vEsiti_concorsi ON Domanda.Anno_accademico = vEsiti_concorsi.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsi.Num_domanda
                WHERE 
                    Domanda.anno_accademico = '{selectedAA}'
                    AND cod_beneficio = '{tipoBeneficio}'
                    AND cod_tipo_esito <> 2
                    
             ";
            SqlCommand nonVincitori = new(sqlStudentiNonVincitori, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo Vincitori");
            HashSet<string> listaStudentiDaEliminareNonVincitori = new();
            using (SqlDataReader reader = nonVincitori.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == "BLDLSS00M23E958G")
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                    if (studente != null && !listaStudentiDaEliminareNonVincitori.Contains(studente.codFiscale))
                    {
                        listaStudentiDaEliminareNonVincitori.Add(studente.codFiscale);
                    }
                }
            }
            string listaCFEscluso = string.Join(", ", listaStudentiDaEliminareNonVincitori.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFEscluso))
            {
                string sqlUpdateEscluso = $@"
                    UPDATE {dbTableName}
                        SET togliere_loreto = 'EXL',
                            note = 'Studente non vincitore al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFEscluso})
                    ";
                SqlCommand esclusoUpdate = new(sqlUpdateEscluso, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                esclusoUpdate.ExecuteNonQuery();
            }
            HashSet<string> listaStudentiDaEliminarePEC = new();
            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode())
            {
                // Check for students with invalid PEC addresses
                string sqlStudentiPecKiller = $@"
                        SELECT LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, Indirizzo_PEC 
                        FROM LUOGO_REPERIBILITA_STUDENTE
                        INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = cfe.Cod_fiscale 
                        LEFT OUTER JOIN vProfilo ON LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = vProfilo.Cod_Fiscale 
                        WHERE ANNO_ACCADEMICO = '{selectedAA}' 
                            AND tipo_bando = 'lz' 
                            AND TIPO_LUOGO = 'DOL'
                            AND DATA_FINE_VALIDITA IS NULL
                            AND (INDIRIZZO = '' OR INDIRIZZO = 'ROMA' OR INDIRIZZO = 'CASSINO' OR INDIRIZZO = 'FROSINONE')
                            AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vResidenza where ANNO_ACCADEMICO = '{selectedAA}' AND provincia_residenza = 'ee')
                            AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vDomicilio where ANNO_ACCADEMICO = '{selectedAA}' AND (Indirizzo_domicilio = '' or Indirizzo_domicilio = 'ROMA' or Indirizzo_domicilio = 'CASSINO' or Indirizzo_domicilio = 'FROSINONE'  or prov = 'EE'))
                            AND Indirizzo_PEC IS NULL
                        ";
                SqlCommand nonPecCmd = new(sqlStudentiPecKiller, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(12, $"Lavorazione studenti - controllo PEC");

                using (SqlDataReader reader = nonPecCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        if (studente != null && !listaStudentiDaEliminarePEC.Contains(studente.codFiscale))
                        {
                            listaStudentiDaEliminarePEC.Add(studente.codFiscale);
                        }
                    }
                }
                string listaCFPEC = string.Join(", ", listaStudentiDaEliminarePEC.Select(cf => $"'{cf}'"));
                if (!string.IsNullOrWhiteSpace(listaCFPEC))
                {
                    string sqlUpdatePec = $@"
                        UPDATE {dbTableName}
                            SET togliere_PEC = 1,
                                note = 'Studente senza PEC al momento dell''estrazione'
                        WHERE
                            cod_fiscale IN ({listaCFPEC})
                        ";
                    SqlCommand pecUpdate = new(sqlUpdatePec, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    pecUpdate.ExecuteNonQuery();
                }
            }
            Logger.LogInfo(12, $"Numero studenti da eliminare per blocchi presenti in domanda = {listaStudentiDaEliminareBlocchi.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare per IBAN mancante = {listaStudentiDaEliminareIBAN.Count}");
            Logger.LogInfo(12, $"Numero studenti bloccati per IBAN non valido = {listaStudentiDaBloccareIBAN.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare perché non più vincitori = {listaStudentiDaEliminareNonVincitori.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare per PEC mancante = {listaStudentiDaEliminarePEC.Count}");
            foreach (string codFiscale in listaStudentiDaEliminareBlocchi)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminareIBAN)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaBloccareIBAN)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminareNonVincitori)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminarePEC)
            {
                studentiDaPagare.Remove(codFiscale);
            }

            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili - completato");
        }

        private void PopulateStudentsInformations()
        {
            PopulateStudentLuogoNascita();
            PopulateStudentResidenza();
            PopulateStudentDomicilio();
            PopulateStudentForzature();
            PopulateStudentPaymentMethod();
            PopulateStudentiVecchioEsitoPA();
            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR)
            {
                PopulateStudentReversali();
                PopulateStudentDetrazioni();
                PopulateNucleoFamiliare();
                if (!isIntegrazione)
                {
                    PopulateStudentiAssegnazioni();
                }
            }
            PopulateStudentiImpegni();
            PopulateImportoDaPagare();

            void PopulateStudentLuogoNascita()
            {
                string dataQuery = @"
                SELECT *
                FROM Studente 
                    LEFT OUTER JOIN Comuni ON Studente.Cod_comune_nasc = Comuni.Cod_comune 
                    INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(30, $"Lavorazione studenti - inserimento in luogo nascita");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.SetLuogoNascita(
                                Utilities.SafeGetString(reader, "COD_COMUNE_NASC"),
                                Utilities.SafeGetString(reader, "descrizione"),
                                Utilities.SafeGetString(reader, "COD_PROVINCIA")
                            );
                    }
                }
                Logger.LogInfo(30, $"UPDATE:Lavorazione studenti - inserimento in luogo nascita - completato");
            }
            void PopulateStudentResidenza()
            {
                string dataQuery = $@"


                        SELECT        LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO, LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, LUOGO_REPERIBILITA_STUDENTE.INDIRIZZO, Comuni.Cod_comune, Comuni.Descrizione AS comune_residenza, 
                                                 Comuni.Cod_provincia AS provincia_residenza, LUOGO_REPERIBILITA_STUDENTE.CAP
                        FROM            LUOGO_REPERIBILITA_STUDENTE INNER JOIN
                                                 Comuni ON LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE = Comuni.Cod_comune
                                        INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.Cod_fiscale = cfe.Cod_fiscale 
                        WHERE        (LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = '{selectedAA}') AND (LUOGO_REPERIBILITA_STUDENTE.TIPO_LUOGO = 'RES') AND 
                                                 (LUOGO_REPERIBILITA_STUDENTE.DATA_VALIDITA =
                                                     (SELECT        MAX(DATA_VALIDITA) AS Expr1
                                                       FROM            LUOGO_REPERIBILITA_STUDENTE AS rsd
                                                       WHERE        (COD_FISCALE = luogo_reperibilita_studente.cod_fiscale) AND (ANNO_ACCADEMICO = luogo_reperibilita_studente.anno_accademico) AND (TIPO_LUOGO = 'RES')))
                        ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(35, $"Lavorazione studenti - inserimento in residenza");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.SetResidenza(
                                Utilities.SafeGetString(reader, "INDIRIZZO"),
                                Utilities.SafeGetString(reader, "Cod_comune"),
                                Utilities.SafeGetString(reader, "provincia_residenza"),
                                Utilities.SafeGetString(reader, "CAP"),
                                Utilities.SafeGetString(reader, "comune_residenza")
                            );
                    }
                }
                Logger.LogInfo(35, $"UPDATE:Lavorazione studenti - inserimento in residenza - completato");
            }
            void PopulateStudentForzature()
            {
                string dataQuery = $@"


                        SELECT forz.cod_fiscale, forz.status_sede
                        FROM Forzature_StatusSede forz
                        INNER JOIN #CFEstrazione cfe ON forz.Cod_fiscale = cfe.Cod_fiscale 
                        where Anno_Accademico = '{selectedAA}' and Data_fine_validita is null
                             ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(35, $"Lavorazione studenti - inserimento in forzature");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        string forzatura = Utilities.SafeGetString(reader, "status_sede").ToUpper();
                        studente?.SetForzatura(forzatura);
                    }
                }
                Logger.LogInfo(35, $"UPDATE:Lavorazione studenti - inserimento in forzatura - completato");
            }
            void PopulateStudentDomicilio()
            {
                // -----------------------
                // 0) Parse the academic year
                // -----------------------
                int startYear = int.Parse(selectedAA.Substring(0, 4));
                int endYear = int.Parse(selectedAA.Substring(4, 4));
                DateTime dateRangeStart = new DateTime(startYear, 10, 1);
                DateTime dateRangeEnd = new DateTime(endYear, 9, 30);

                // -----------------------
                // 1) Prepare the query
                // -----------------------
                string dataQuery = $@"
                    SELECT 
                        LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, 
                        LUOGO_REPERIBILITA_STUDENTE.TITOLO_ONEROSO, 
                        LUOGO_REPERIBILITA_STUDENTE.N_SERIE_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_REG_CONTRATTO,  
                        LUOGO_REPERIBILITA_STUDENTE.DATA_DECORRENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_SCADENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.ESTREMI_PROROGA,
                        LUOGO_REPERIBILITA_STUDENTE.TIPO_CONTRATTO_TITOLO_ONEROSO,
                        LUOGO_REPERIBILITA_STUDENTE.DENOM_ENTE,
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO,
                        LUOGO_REPERIBILITA_STUDENTE.IMPORTO_RATA
                    FROM LUOGO_REPERIBILITA_STUDENTE
                    INNER JOIN Comuni ON LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE = Comuni.Cod_comune
                    INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE 
                        (LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = '{selectedAA}') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.TIPO_LUOGO   = 'DOM') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.DATA_VALIDITA = (
                            SELECT MAX(DATA_VALIDITA)
                            FROM LUOGO_REPERIBILITA_STUDENTE AS rsd
                            WHERE 
                                (COD_FISCALE    = LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE) 
                                AND (ANNO_ACCADEMICO = LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO) 
                                AND (TIPO_LUOGO     = 'DOM')
                        ))
                ";

                // -----------------------
                // 2) Read data into DTOs
                // -----------------------
                var domicilioRows = new List<StudentiDomicilioDTO>();
                Logger.LogInfo(35, "Lavorazione studenti - inserimento in domicilio");

                using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                            domicilioRows.Add(new StudentiDomicilioDTO
                            {
                                CodFiscale = codFiscale,
                                TitoloOneroso = (Utilities.SafeGetInt(reader, "TITOLO_ONEROSO") == 1),
                                ContrattoEnte = (Utilities.SafeGetInt(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1),
                                SerieContratto = Utilities.SafeGetString(reader, "N_SERIE_CONTRATTO"),
                                DataRegistrazioneString = Utilities.SafeGetString(reader, "DATA_REG_CONTRATTO"),
                                DataDecorrenzaString = Utilities.SafeGetString(reader, "DATA_DECORRENZA"),
                                DataScadenzaString = Utilities.SafeGetString(reader, "DATA_SCADENZA"),
                                DurataContratto = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO"),
                                Prorogato = (Utilities.SafeGetInt(reader, "PROROGA") == 1),
                                DurataProroga = Utilities.SafeGetInt(reader, "DURATA_PROROGA"),
                                SerieProroga = Utilities.SafeGetString(reader, "ESTREMI_PROROGA"),
                                DenominazioneEnte = Utilities.SafeGetString(reader, "DENOM_ENTE"),
                                ImportoRataEnte = Utilities.SafeGetDouble(reader, "IMPORTO_RATA")
                            });
                        }
                    }
                }

                // -----------------------
                // 3) Process the rows
                // -----------------------
                foreach (var row in domicilioRows)
                {
                    if (!studentiDaPagare.TryGetValue(row.CodFiscale, out StudentePagam? studente))
                        continue;
                    if (studente == null)
                        continue;

                    // Debug check
                    if (studente.codFiscale == "VLNDNS01T54E885A")
                    {
                        string test = ""; // Just to set a breakpoint, presumably
                    }

                    // ----- TITOLO ONEROSO -----
                    bool titoloOneroso = row.TitoloOneroso;
                    DateTime.TryParse(row.DataRegistrazioneString, out DateTime dataRegistrazione);
                    DateTime.TryParse(row.DataDecorrenzaString, out DateTime dataDecorrenza);
                    DateTime.TryParse(row.DataScadenzaString, out DateTime dataScadenza);

                    // Only if there's a real contract
                    if (titoloOneroso)
                    {
                        // Calculate the overlap between the contract and the academic year period
                        DateTime effectiveStart = (dataDecorrenza > dateRangeStart) ? dataDecorrenza : dateRangeStart;
                        DateTime effectiveEnd = (dataScadenza < dateRangeEnd) ? dataScadenza : dateRangeEnd;

                        if (effectiveStart <= effectiveEnd)
                        {
                            int monthsCovered = ((effectiveEnd.Year - effectiveStart.Year) * 12)
                                              + (effectiveEnd.Month - effectiveStart.Month + 1);
                            if (monthsCovered >= 10)
                            {
                                // Mark the student as having a valid domicile for >=10 months
                                studente.SetDomicilioCheck(true);
                            }
                        }
                    }

                    // ----- CONTRATTO ENTE -----
                    bool contrattoEnte = row.ContrattoEnte;
                    string denominazioneEnte = row.DenominazioneEnte;
                    int durataContratto = row.DurataContratto;
                    double importoRataEnte = row.ImportoRataEnte;
                    bool contrattoEnteValido = false;

                    if (contrattoEnte)
                    {
                        if (string.IsNullOrWhiteSpace(denominazioneEnte))
                        {
                            // If there's no "Ente" name, consider invalid
                            studente.SetDomicilioCheck(false);
                        }
                        else
                        {
                            // Must have at least 10 months and a rate > 0
                            if (durataContratto < 10 || importoRataEnte <= 0)
                            {
                                studente.SetDomicilioCheck(false);
                            }
                            else
                            {
                                studente.SetDomicilioCheck(true);
                                contrattoEnteValido = true;
                            }
                        }
                    }

                    // ----- Serie Contratto & Serie Proroga -----
                    string serieContratto = row.SerieContratto;
                    string serieProroga = row.SerieProroga;

                    bool contrattoValido = contrattoEnteValido || IsValidSerie(serieContratto);
                    bool prorogaValido = IsValidSerie(serieProroga);

                    // If the proroga contains the same base as the contract, we consider that invalid
                    if (!string.IsNullOrEmpty(serieContratto)
                        && !string.IsNullOrEmpty(serieProroga)
                        && serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        prorogaValido = false;
                    }

                    // Debug check
                    if (studente.codFiscale == "DRSMLD03A68F443G")
                    {
                        string test = ""; // Another debugging breakpoint
                    }

                    // Finally, store all the domicile info into the Studente object
                    studente.SetDomicilio(
                        titoloOneroso,
                        serieContratto,
                        dataRegistrazione,
                        dataDecorrenza,
                        dataScadenza,
                        row.DurataContratto,
                        row.Prorogato ?? false,
                        row.DurataProroga,
                        serieProroga,
                        contrattoValido,
                        prorogaValido,
                        contrattoEnte,
                        denominazioneEnte,
                        importoRataEnte
                    );
                }

                Logger.LogInfo(35, "UPDATE:Lavorazione studenti - inserimento in domicilio - completato");

                // -----------------------
                // Local helper method
                // -----------------------
                bool IsValidSerie(string serie)
                {
                    if (string.IsNullOrWhiteSpace(serie))
                        return false;

                    serie = serie.Trim();
                    // Remove trailing dots
                    serie = serie.TrimEnd('.');

                    // Case-insensitive matching
                    RegexOptions options = RegexOptions.IgnoreCase;

                    // Exclude date-only entries or date ranges
                    string dateOnlyPattern1 = @"^\d{1,2}/\d{1,2}/\d{2,4}$";
                    string dateOnlyPattern2 = @"^\d{1,2}/\d{1,2}/\d{2,4}\s*[\-–]\s*\d{1,2}/\d{1,2}/\d{2,4}$";
                    string dateOnlyPattern3 = @"^dal\s+\d{1,2}/\d{1,2}/\d{2,4}\s+al\s+\d{1,2}/\d{1,2}/\d{2,4}$";
                    string dateWordsPattern = @"^dal\s+\d{1,2}\s+\w+\s+\d{4}\s+al\s+\d{1,2}\s+\w+\s+\d{4}$";

                    if (Regex.IsMatch(serie, dateOnlyPattern1, options) ||
                        Regex.IsMatch(serie, dateOnlyPattern2, options) ||
                        Regex.IsMatch(serie, dateOnlyPattern3, options) ||
                        Regex.IsMatch(serie, dateWordsPattern, options))
                    {
                        return false;
                    }

                    // Exclude '3T' or 'serie 3T' alone
                    string serieWithoutSpaces = Regex.Replace(serie, @"\s+", "");
                    if (string.Equals(serieWithoutSpaces, "3T", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(serieWithoutSpaces, "serie3T", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    // Exclude 'Foglio/part/sub/Cat' patterns unless they match a valid code
                    if (Regex.IsMatch(serie, @"\b(Foglio|part|sub|Cat)\b", options) &&
                        !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                    {
                        return false;
                    }

                    // Exclude 'PRENOTAZIONE' unless there's a valid code
                    if (Regex.IsMatch(serie, @"\bPRENOTAZIONE\b", options) &&
                        !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                    {
                        return false;
                    }

                    // Exclude 'automatico' unless there's a valid code
                    if (Regex.IsMatch(serie, @"automatico", options) &&
                        !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                    {
                        return false;
                    }

                    // Patterns for valid codes
                    string pattern1 = @"^(T|TRF|TEL)\s?[A-Z0-9]{10,50}\.?$";
                    string pattern1b = @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b";
                    string pattern2 = @"^[\d/\s\-]{4,}$";
                    string pattern2b = @"^\d{1,20}([/\s\-]\d{1,20})+$";
                    string pattern3 = @"(?i)^(.*\b(serie\s*3\s*T|serie\s*3T|serie\s*T3|serie\s*T|serie\s*IT|3\s*T|3T|T3|3/T)\b.*)$";
                    string pattern4 = @"^QC([\s/]*\w+)+$";
                    string pattern5 = @"(?i)^(.*\b(Protocollo|PROT\.?|prot\.?n?\.?|Protocol-?)\b.*\d+.*)$";
                    string pattern6 = @"^(RA/|RM|FC/)\s*\S+$";
                    // At least one digit, one letter, can include slash/hyphen/spaces, 5-50 in length
                    string pattern7 = @"^(?=.*[A-Za-z])(?=.*\d)[A-Za-z0-9/\s\-]{5,50}$";

                    // NEW: Pattern for "3Tn.15706" style series.
                    string pattern8 = @"^3Tn\.[0-9]+$";

                    // Check them in sequence
                    if (Regex.IsMatch(serie, pattern1, options)) return true;
                    if (Regex.IsMatch(serie, pattern1b, options)) return true;
                    if (Regex.IsMatch(serie, pattern2, options)) return true;
                    if (Regex.IsMatch(serie, pattern2b, options)) return true;
                    if (Regex.IsMatch(serie, pattern3, options)) return true;
                    if (Regex.IsMatch(serie, pattern4, options)) return true;
                    if (Regex.IsMatch(serie, pattern5, options)) return true;
                    if (Regex.IsMatch(serie, pattern6, options)) return true;
                    if (Regex.IsMatch(serie, pattern7, options)) return true;
                    if (Regex.IsMatch(serie, pattern8, options)) return true;

                    // If none match, it's invalid
                    return false;
                }

            }
            void PopulateStudentPaymentMethod()
            {
                string dataQuery = @"
                    SELECT * 
                    FROM Studente 
                        LEFT OUTER JOIN vModalita_pagamento ON studente.cod_fiscale = vmodalita_pagamento.cod_fiscale
                        INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale 
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento in informazioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.SetInformations(
                                long.TryParse(Regex.Replace(Utilities.SafeGetString(reader, "telefono_cellulare"), @"[^\d]", ""), out long telefonoNumber) ? telefonoNumber : 0,
                                Utilities.SafeGetString(reader, "indirizzo_e_mail"),
                                Utilities.SafeGetString(reader, "IBAN"),
                                Utilities.SafeGetString(reader, "swift"),
                                Utilities.SafeGetString(reader, "Bonifico_estero") != "1"
                            );
                    }
                }
                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento in informazioni - completato");
            }
            void PopulateStudentReversali()
            {
                string dataQuery = $@"
                    SELECT Domanda.Cod_fiscale, Reversali.*, (SELECT DISTINCT cod_tipo_pagam_new FROM Decod_pagam_new where Cod_tipo_pagam_old = Reversali.Cod_tipo_pagam OR Cod_tipo_pagam_new = Reversali.Cod_tipo_pagam) AS cod_tipo_pagam_new
                    FROM Domanda 
                        INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                        INNER JOIN Reversali ON Domanda.num_domanda = Reversali.num_domanda AND Domanda.Anno_accademico = Reversali.Anno_accademico
                    WHERE Reversali.Ritirato_azienda = 0 AND Domanda.Anno_accademico = '{selectedAA}'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(45, $"Lavorazione studenti - inserimento in reversali");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.AddReversale(
                                    Utilities.SafeGetString(reader, "cod_reversale"),
                                    double.TryParse(Utilities.SafeGetString(reader, "importo"), out double importo) ? importo : 0,
                                    Utilities.SafeGetString(reader, "Note"),
                                    Utilities.SafeGetString(reader, "cod_tipo_pagam"),
                                    Utilities.SafeGetString(reader, "cod_tipo_pagam_new")
                                );
                    }
                }
                Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento in reversali - completato");
            }
            void PopulateStudentDetrazioni()
            {
                string dataQuery = $@"
                    SELECT CODICE_FISCALE, ID_CAUSALE, IMPORTO, NOTE_MOVIMENTO_ELEMENTARE
                    FROM MOVIMENTI_CONTABILI_ELEMENTARI
                        INNER JOIN #CFEstrazione cfe ON MOVIMENTI_CONTABILI_ELEMENTARI.CODICE_FISCALE = cfe.Cod_fiscale 
                    WHERE SEGNO = 0 AND Anno_accademico = '{selectedAA}' AND CODICE_MOVIMENTO IS NULL
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(45, $"Lavorazione studenti - inserimento in detrazioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.AddDetrazione(
                                    Utilities.SafeGetString(reader, "ID_CAUSALE"),
                                    double.TryParse(Utilities.SafeGetString(reader, "IMPORTO"), out double importo) ? importo : 0,
                                    Utilities.SafeGetString(reader, "NOTE_MOVIMENTO_ELEMENTARE"),
                                    true
                                );
                    }
                }
                Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento in detrazioni - completato");
            }
            void PopulateStudentiVecchioEsitoPA()
            {
                string dataQuery = $@"
                    select distinct Domanda.Cod_fiscale, Esiti_concorsi.Cod_tipo_esito From 
                        Domanda inner join 
                        #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale inner join
                        Esiti_concorsi ON Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda
                    where domanda.Anno_accademico = '{selectedAA}' and Esiti_concorsi.Cod_beneficio = 'PA' and Esiti_concorsi.Cod_tipo_esito = '2'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento esiti PA");
                List<string> studentiDaRimuovere = new List<string>();
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);
                        studente?.SetEraVincitorePA(true);

                        if (studente != null && selectedRichiestoPA == "3")
                        {
                            studentiDaRimuovere.Add(studente.codFiscale);
                            continue;
                        }
                    }
                }

                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento esiti PA - completato");
            }
            void PopulateNucleoFamiliare()
            {
                string dataQuery = $@"
                    select domanda.Cod_fiscale, Num_componenti, Numero_conviventi_estero
                    from Domanda 
                    inner join #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale
                    inner join vNucleo_familiare vn on Domanda.Anno_accademico = vn.Anno_accademico and Domanda.Num_domanda = vn.Num_domanda 
                    where Domanda.Anno_accademico = '{selectedAA}' and Tipo_bando = 'lz'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento nucleo familiare");
                List<string> studentiDaRimuovere = new List<string>();
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente);


                        if (studente != null)
                        {
                            int numeroComponenti = Utilities.SafeGetInt(reader, "Num_componenti");
                            int numeroComponentiEstero = Utilities.SafeGetInt(reader, "Numero_conviventi_estero");
                            studente.SetNucleoFamiliare(numeroComponenti, numeroComponentiEstero);
                        }
                    }
                }

                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento nucleo familiare - completato");
            }
            void PopulateStudentiAssegnazioni()
            {
                // ---------------------------
                // 1) Get "date info" FIRST
                // ---------------------------
                string dateQuery = $@"
                    SELECT 
                        FORMAT(min_data_PA, 'dd/MM/yyyy') AS min_data_PA, 
                        FORMAT(max_data_PA, 'dd/MM/yyyy') AS max_data_PA, 
                        detrazione_PA, 
                        detrazione_PA_fuori_corso
                    FROM DatiGenerali_con 
                    WHERE Anno_accademico = '{selectedAA}'
                ";

                // Local variables to store data from the first query
                DateTime min_data_PA = new(1990, 01, 01);
                DateTime max_data_PA = new(2999, 01, 01);
                double detrazione_PA = 0;
                double detrazione_PA_fuori_corso = 0;

                // Read the single row from DatiGenerali_con (if any)
                using (SqlCommand readDate = new(dateQuery, CONNECTION, sqlTransaction))
                {
                    readDate.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readDate.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string minDataPAStr = Utilities.SafeGetString(reader, "min_data_PA");
                            string maxDataPAStr = Utilities.SafeGetString(reader, "max_data_PA");
                            string detrazionePAStr = Utilities.SafeGetString(reader, "detrazione_PA");
                            string detrazionePAFuoriCorsoStr = Utilities.SafeGetString(reader, "detrazione_PA_fuori_corso");

                            if (!DateTime.TryParse(minDataPAStr, out min_data_PA))
                            {
                                Logger.LogError(null, "Errore nella data minima per Assegnazione PA");
                            }
                            if (!DateTime.TryParse(maxDataPAStr, out max_data_PA))
                            {
                                Logger.LogError(null, "Errore nella data massima per Assegnazione PA");
                            }
                            if (!double.TryParse(detrazionePAStr, out detrazione_PA))
                            {
                                Logger.LogError(null, "Errore nella detrazione PA");
                            }
                            if (!double.TryParse(detrazionePAFuoriCorsoStr, out detrazione_PA_fuori_corso))
                            {
                                Logger.LogError(null, "Errore nella detrazione PA per i fuori corso");
                            }
                        }
                    }
                }

                // ---------------------------
                // 2) Read "assegnazioni"
                // ---------------------------
                // We'll store rows in a simple in-memory list, then close the reader
                var assegnazioniList = new List<AssegnazionePaDto>();

                string dataQuery = $@"
                    SELECT DISTINCT     
                        Assegnazione_PA.Cod_fiscale,
                        Assegnazione_PA.Cod_Pensionato, 
                        Assegnazione_PA.Cod_Stanza, 
                        FORMAT(Assegnazione_PA.Data_Decorrenza, 'dd/MM/yyyy') AS Data_Decorrenza, 
                        FORMAT(Assegnazione_PA.Data_Fine_Assegnazione, 'dd/MM/yyyy') AS Data_Fine_Assegnazione, 
                        Assegnazione_PA.Cod_Fine_Assegnazione,
                        Costo_Servizio.Tipo_stanza, 
                        Costo_Servizio.Importo as importo_mensile,
                        Assegnazione_PA.id_assegnazione_pa
                    FROM            
                        Assegnazione_PA 
                        INNER JOIN vStanza 
                            ON Assegnazione_PA.Cod_Stanza = vStanza.Cod_Stanza 
                            AND Assegnazione_PA.Cod_Pensionato = vStanza.Cod_Pensionato 
                        INNER JOIN Costo_Servizio 
                            ON vStanza.Tipo_costo_Stanza = Costo_Servizio.Tipo_stanza 
                            AND Assegnazione_PA.Anno_Accademico = Costo_Servizio.Anno_accademico 
                            AND Assegnazione_PA.Cod_Pensionato = Costo_Servizio.Cod_pensionato
                        INNER JOIN #CFEstrazione cfe 
                            ON Assegnazione_PA.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE        
                        (Assegnazione_PA.Anno_Accademico = '{selectedAA}') AND 
                        (Assegnazione_PA.Cod_movimento = '01') AND 
                        (Assegnazione_PA.Ind_Assegnazione = 1) AND 
                        (Assegnazione_PA.Status_Assegnazione = 0) AND
                        Costo_Servizio.Cod_periodo = 'M' AND 
                        Assegnazione_PA.Data_Accettazione IS NOT NULL
                    ORDER BY Assegnazione_PA.id_assegnazione_pa
                ";

                Logger.LogInfo(50, "Lavorazione studenti - inserimento in assegnazioni");
                HashSet<string> processedFiscalCodes = new();
                HashSet<string> studentiDaRimuovere = new();

                using (SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            assegnazioniList.Add(new AssegnazionePaDto
                            {
                                CodFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper()),
                                CodPensionato = Utilities.SafeGetString(reader, "Cod_Pensionato"),
                                CodStanza = Utilities.SafeGetString(reader, "Cod_Stanza"),
                                DataDecorrenza = Utilities.SafeGetString(reader, "Data_Decorrenza"),
                                DataFineAssegnazione = Utilities.SafeGetString(reader, "Data_Fine_Assegnazione"),
                                CodFineAssegnazione = Utilities.SafeGetString(reader, "Cod_Fine_Assegnazione"),
                                TipoStanza = Utilities.SafeGetString(reader, "Tipo_stanza"),
                                ImportoMensileStr = Utilities.SafeGetString(reader, "importo_mensile"),
                                IdAssegnazionePa = Utilities.SafeGetString(reader, "id_assegnazione_pa")
                            });
                        }
                    }
                }

                // ---------------------------
                // 3) Process data OFFLINE
                // ---------------------------
                // Now we do the "business logic" with the results in memory.

                foreach (var assegnazione in assegnazioniList)
                {
                    // For convenience
                    string codFiscale = assegnazione.CodFiscale;
                    if (!studentiDaPagare.TryGetValue(codFiscale, out StudentePagam? studente))
                    {
                        // Studente not found in our dictionary
                        continue;
                    }

                    // If stanza is 'XXX' or studente is null, just skip it
                    if (studente == null || assegnazione.CodStanza == "XXX")
                    {
                        continue;
                    }

                    // If selectedRichiestoPA is 0 or 3 => remove the student
                    if (selectedRichiestoPA == "0" || selectedRichiestoPA == "3")
                    {
                        studentiDaRimuovere.Add(studente.codFiscale);
                        continue;
                    }

                    bool studenteFuoriCorso = (studente.annoCorso == -1 && !studente.disabile);
                    bool studenteDisabileFuoriCorso = (studente.annoCorso == -2 && studente.disabile);

                    // ---------------------
                    //    PAGAMENTO = PR
                    // ---------------------
                    if (categoriaPagam == "PR" && !processedFiscalCodes.Contains(codFiscale))
                    {
                        if (studente.esitoPA != 2)
                        {
                            continue;
                        }

                        // Check detrazioni or reversali
                        bool detrazioneAcconto = false;
                        if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                        {
                            foreach (Detrazione detrazione in studente.detrazioni)
                            {
                                if (detrazione.codReversale == "01")
                                {
                                    detrazioneAcconto = true;
                                    break;
                                }
                            }
                            if (detrazioneAcconto) continue;
                        }
                        else if (studente.reversali != null && studente.reversali.Count > 0)
                        {
                            foreach (Reversale reversale in studente.reversali)
                            {
                                if (reversale.codReversale == "01")
                                {
                                    detrazioneAcconto = true;
                                    break;
                                }
                            }
                            if (detrazioneAcconto) continue;
                        }
                        else if (isRiemissione)
                        {
                            // If is a re-issue, skip again
                            continue;
                        }

                        // Add Detrazione logic
                        if (!studenteFuoriCorso && !studenteDisabileFuoriCorso)
                        {
                            studente.AddDetrazione("01", detrazione_PA, "Detrazione acconto PA");
                        }
                        else
                        {
                            studente.AddDetrazione("01", detrazione_PA_fuori_corso, "Detrazione acconto PA");
                        }
                        processedFiscalCodes.Add(codFiscale);
                    }
                    // ---------------------
                    //   PAGAMENTO = SA
                    // ---------------------
                    else if (categoriaPagam == "SA")
                    {
                        if (!DateTime.TryParse(assegnazione.DataFineAssegnazione?.Trim(), out DateTime endDate))
                        {
                            endDate = DateTime.MaxValue;
                        }

                        // Parse the importo mensile
                        double importoMensile = 0;
                        _ = double.TryParse(assegnazione.ImportoMensileStr?.Trim(), out importoMensile);

                        // Actually add the assegnazione
                        AssegnazioneDataCheck result = studente.AddAssegnazione(
                            assegnazione.CodPensionato?.Trim() ?? "",
                            assegnazione.CodStanza?.Trim() ?? "",
                            DateTime.Parse(assegnazione.DataDecorrenza?.Trim() ?? "01/01/0001"),
                            endDate,
                            assegnazione.CodFineAssegnazione?.Trim() ?? "",
                            assegnazione.TipoStanza?.Trim() ?? "",
                            importoMensile,
                            min_data_PA,
                            max_data_PA,
                            studenteFuoriCorso || studenteDisabileFuoriCorso,
                            assegnazione.IdAssegnazionePa
                        );

                        // Check if we had any error from AddAssegnazione
                        if (result != AssegnazioneDataCheck.Corretto)
                        {
                            Logger.LogDebug(null,
                                $"Errore nell'assegnazione dello studente {codFiscale}: {result}");

                            string message = result switch
                            {
                                AssegnazioneDataCheck.Eccessivo =>
                                    "Assegnazione posto alloggio superiore alle mensilità possibili (10 mesi)",
                                AssegnazioneDataCheck.Incorretto =>
                                    "Assegnazione posto alloggio con data fine minore della data di entrata",
                                AssegnazioneDataCheck.DataUguale =>
                                    "Assegnazione posto alloggio con data decorrenza e fine uguali",
                                AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin =>
                                    "Assegnazione posto alloggio con data decorrenza minore del minimo previsto dal bando",
                                AssegnazioneDataCheck.DataFineAssMaggioreMax =>
                                    "Assegnazione posto alloggio con data fine maggiore del massimo previsto dal bando",
                                AssegnazioneDataCheck.MancanzaDataFineAssegnazione =>
                                    "Assegnazione posto alloggio senza data fine",
                                // Fallback
                                _ => "Errore sconosciuto"
                            };

                            if (!studentiConErroriPA.TryGetValue(studente, out List<string>? value))
                            {
                                value = new List<string>();
                                studentiConErroriPA.Add(studente, value);
                            }
                            value.Add(message);
                        }
                    }
                }

                // -----------------------------
                // Clean-up or post-processing
                // -----------------------------
                // Remove the students we flagged
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }

                // Additional fixes for students with errors
                foreach (StudentePagam studente in studentiConErroriPA.Keys)
                {
                    if (studente.assegnazioni.Count == 1 && studente.esitoPA == 2)
                    {
                        Assegnazione vecchiaAssegnazione = studente.assegnazioni[0];
                        if (vecchiaAssegnazione.statoCorrettezzaAssegnazione == AssegnazioneDataCheck.DataUguale)
                        {
                            // Mark the data check differently if needed
                            vecchiaAssegnazione.SetAssegnazioneDataCheck(AssegnazioneDataCheck.ErroreControlloData);
                        }
                    }
                }

                Logger.LogInfo(50, "UPDATE:Lavorazione studenti - inserimento in assegnazioni - completato");
            }
            void PopulateStudentiImpegni()
            {
                string currentBeneficio = isTR ? "TR" : tipoBeneficio;

                string dataQuery = $@"
                    SELECT 
                        Specifiche_impegni.Cod_fiscale, 
                        num_impegno_primaRata, 
                        num_impegno_saldo, 
                        importo_assegnato
                    FROM Specifiche_impegni
                    INNER JOIN #CFEstrazione cfe ON Specifiche_impegni.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE 
                        Cod_beneficio = '{currentBeneficio}' 
                        AND Anno_accademico = '{selectedAA}' 
                        AND Data_fine_validita IS NULL
                ";

                var studentiSenzaImpegno = new HashSet<string>();
                var studentiDaRimuovereIntegrazione = new HashSet<string>();

                // Step 1: Read all data from the DB into an in-memory list
                List<PopulateStudentiImpegniDTO> rows = new();

                Logger.LogInfo(45, $"Lavorazione studenti - inserimento impegni");

                using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var codFiscale = Utilities.RemoveAllSpaces(
                                                   Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                            var impegnoPrimaRata = Utilities.SafeGetString(reader, "num_impegno_primaRata");
                            var impegnoSaldo = Utilities.SafeGetString(reader, "num_impegno_saldo");
                            double.TryParse(Utilities.SafeGetString(reader, "importo_assegnato"),
                                            out double importoAssegnato);

                            rows.Add(new PopulateStudentiImpegniDTO
                            {
                                CodFiscale = codFiscale,
                                ImpegnoPrimaRata = impegnoPrimaRata,
                                ImpegnoSaldo = impegnoSaldo,
                                ImportoAssegnato = importoAssegnato
                            });
                        }
                    }
                }

                // Step 2: Process in-memory
                foreach (var row in rows)
                {
                    if (studentiDaPagare.TryGetValue(row.CodFiscale, out StudentePagam? studente))
                    {
                        if (studente is null) continue;

                        // Integrations check
                        if (isIntegrazione)
                        {
                            if (studente.importoBeneficio != row.ImportoAssegnato)
                            {
                                studentiDaRimuovereIntegrazione.Add(studente.codFiscale);
                                continue;
                            }
                        }

                        // Decide which impegno to use
                        string impegnoToSet = (categoriaPagam == "PR")
                            ? row.ImpegnoPrimaRata
                            : row.ImpegnoSaldo;

                        if (string.IsNullOrWhiteSpace(impegnoToSet))
                        {
                            studentiSenzaImpegno.Add(studente.codFiscale);
                            continue;
                        }

                        // Finally set the impegno
                        studente.SetImpegno(impegnoToSet);
                    }
                }

                // Clean up
                if (studentiSenzaImpegno.Any())
                {
                    Logger.LogWarning(null,
                        $"Trovati {studentiSenzaImpegno.Count} studenti senza impegno");
                }
                if (studentiDaRimuovereIntegrazione.Any())
                {
                    Logger.LogWarning(null,
                        $"Trovati {studentiDaRimuovereIntegrazione.Count} studenti " +
                        $"con importo borsa diverso da quello assegnato");
                }

                foreach (string codFiscale in studentiSenzaImpegno)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                foreach (string codFiscale in studentiDaRimuovereIntegrazione)
                {
                    Logger.LogDebug(null,
                        $"Rimosso cf: {codFiscale} per assenza provvedimento per integrazione importi");
                    studentiDaPagare.Remove(codFiscale);
                }

                Logger.LogInfo(45,
                    "UPDATE:Lavorazione studenti - inserimento impegni - completato");
            }

            void PopulateImportoDaPagare()
            {
                Logger.LogInfo(55, $"Lavorazione studenti - Calcolo importi");

                // Use thread-safe collections
                ConcurrentDictionary<string, bool> studentiDaRimuovereDallaTabella = new();
                ConcurrentDictionary<string, double> studentiPAnegativo = new();
                ConcurrentBag<(string CodFiscale, string Motivazione)> studentiRimossiBag = new();
                ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari = new();
                ThreadLocal<double> importoTotalePerThread = new(() => 0.0, true);

                Parallel.ForEach(studentiDaPagare, pair =>
                {
                    StudentePagam studente = pair.Value;

                    if (studente.codFiscale == "DLULMR99P61Z129L")
                    {
                        string stest = "";
                    }
                    if (studente.codFiscale == "DAOMWN92M21Z352U")
                    {
                        string stest = "";
                    }
                    if (studente.codFiscale == "STNRCR01L07D972T")
                    {
                        string stest = "";
                    }


                    // Initialize variables
                    double importoDaPagare = studente.importoBeneficio;
                    double importoMassimo = studente.importoBeneficio;

                    double importoPA = 0;
                    double accontoPA = 0;

                    double importoDetrazioni = 0;
                    double importoReversali = 0;

                    // Check selectedImpegno
                    if (selectedImpegno != "0000")
                    {
                        if (studente.numeroImpegno != selectedImpegno)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            return;
                        }
                    }

                    // Initialize flags
                    bool hasPrimaRata = false;
                    bool primaRataStorno = false;
                    bool hasSaldo = false;
                    bool saldoStorno = false;
                    bool riemessaPrimaRata = false;
                    bool riemessaSecondaRata = false;
                    bool integrazionePrimaRata = false;
                    bool stornoIntegrazionePrimaRata = false;
                    bool riemessaIntegrazionePrimaRata = false;

                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode())
                    {
                        // Process pagamentiEffettuati
                        foreach (Pagamento pagamento in studente.pagamentiEffettuati)
                        {
                            string lastValue = pagamento.codTipoPagam[2..];
                            if (lastValue == "P0")
                            {
                                hasPrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    primaRataStorno = true;
                                }
                                continue;
                            }
                            if (lastValue == "S0")
                            {
                                hasSaldo = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    saldoStorno = true;
                                }
                                continue;
                            }
                            if (lastValue == "P1" || lastValue == "P2")
                            {
                                riemessaPrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaPrimaRata = false;
                                }
                                continue;
                            }
                            if (lastValue == "S1" || lastValue == "S2")
                            {
                                riemessaSecondaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaSecondaRata = false;
                                }
                                continue;
                            }
                            if (lastValue == "I0")
                            {
                                integrazionePrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    stornoIntegrazionePrimaRata = true;
                                }
                                continue;
                            }
                            if (lastValue == "I1" || lastValue == "I2")
                            {
                                riemessaIntegrazionePrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaIntegrazionePrimaRata = false;
                                }
                            }
                        }

                        // Check conditions and possibly remove student
                        if (!studenteForzato && categoriaPagam == "SA" && hasPrimaRata && primaRataStorno && !riemessaPrimaRata)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            studentiRimossiBag.Add((studente.codFiscale, "Prima rata non riemessa"));
                            return;
                        }

                        if (!studenteForzato && categoriaPagam == "SA" && integrazionePrimaRata && stornoIntegrazionePrimaRata && !riemessaIntegrazionePrimaRata)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            studentiRimossiBag.Add((studente.codFiscale, "Integrazione prima rata non riemessa"));
                            return;
                        }

                        // Process assegnazioni
                        if (studente.assegnazioni != null && studente.assegnazioni.Count > 0)
                        {
                            foreach (Assegnazione assegnazione in studente.assegnazioni)
                            {
                                importoPA += Math.Max(assegnazione.costoTotale, 0);
                            }
                        }

                        // Process reversali
                        if (studente.reversali != null && studente.reversali.Count > 0)
                        {
                            foreach (Reversale reversale in studente.reversali)
                            {
                                if (reversale.codReversale == "01")
                                {
                                    importoPA -= reversale.importo;
                                    studente.SetImportoAccontoPA(Math.Round(reversale.importo, 2));
                                }

                                if (riemessaPrimaRata && reversale.codReversale == "01")
                                {
                                    importoReversali += reversale.importo;
                                }
                                else if (riemessaSecondaRata && reversale.codReversale == "02")
                                {
                                    importoReversali += reversale.importo;
                                }
                            }
                        }

                        // Process detrazioni
                        if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                        {
                            foreach (Detrazione detrazione in studente.detrazioni)
                            {
                                if (detrazione.codReversale == "01" && accontoPA <= 0)
                                {
                                    accontoPA += detrazione.importo;
                                    studente.SetImportoAccontoPA(Math.Round(accontoPA, 2));
                                    importoPA = accontoPA;
                                }
                                else if (detrazione.codReversale != "01")
                                {
                                    importoDetrazioni += detrazione.importo;
                                }
                            }
                        }
                    }

                    // Check if importoPA is negative
                    if (importoPA < 0)
                    {
                        studentiPAnegativo[studente.codFiscale] = importoPA;
                    }

                    importoPA = Math.Round(Math.Max(importoPA, 0), 2);
                    studente.SetImportoSaldoPA(importoPA);

                    double importiPagati = isTR ? 0 : studente.importoPagato;

                    // Check if importoMassimo and importoPagato are close
                    if (Math.Abs(importoMassimo - studente.importoPagato) < 5 && !isTR)
                    {
                        studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                        studentiRimossiBag.Add((studente.codFiscale, "Importo da pagare minore di €5"));
                        return;
                    }

                    // Calculate importoDaPagare based on various conditions
                    if (categoriaPagam == "PR" && (
                        tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode()
                        ) && !isTR)
                    {
                        string currentYear = selectedAA[..4];
                        DateTime percentDate = new(int.Parse(currentYear), 11, 10);
                        if (DateTime.Parse(selectedDataRiferimento) <= percentDate && studente.annoCorso == 1 && (studente.tipoCorso == 3 || studente.tipoCorso == 4))
                        {
                            importoDaPagare = importoMassimo * 0.2;
                            importoMassimo *= 0.2;
                        }
                        else if (DateTime.Parse(selectedDataRiferimento) <= percentDate)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            return;
                        }
                        else
                        {
                            importoDaPagare = importoMassimo * 0.5;
                            importoMassimo *= 0.5;
                        }
                    }
                    else if ((tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode()) && !isTR)
                    {
                        importoDaPagare = importoMassimo;
                        if (studente.annoCorso == 1)
                        {
                            if (!studente.superamentoEsami && studente.superamentoEsamiTassaRegionale && !(studente.tipoCorso == 6 || studente.tipoCorso == 7))
                            {
                                importoDaPagare = importoMassimo * 0.5;
                                importoMassimo *= 0.5;
                            }
                            else if (!studente.superamentoEsami && !studente.superamentoEsamiTassaRegionale)
                            {
                                studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                                studentiRimossiBag.Add((studente.codFiscale, "Senza superamento esami"));

                                return;
                            }
                        }
                    }
                    else if (isTR)
                    {
                        importoDaPagare = 140;

                        if ((!hasSaldo || (hasSaldo && saldoStorno && !riemessaSecondaRata)) && !studenteForzato)
                        {
                            if (!(studente.annoCorso == 1 && (studente.superamentoEsami || studente.superamentoEsamiTassaRegionale)))
                            {
                                studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                                studentiRimossiBag.Add((studente.codFiscale, "Non ha saldo/saldo non riemesso"));
                                return;
                            }
                        }

                        if (studente.disabile)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            return;
                        }

                        if (studente.tipoCorso == 6 || studente.tipoCorso == 7)
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            return;
                        }

                        if (studente.annoCorso == 1 && !(studente.superamentoEsami || studente.superamentoEsamiTassaRegionale))
                        {
                            studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                            return;
                        }
                    }
                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR)
                    {
                        selectedAcademicProcessor.AdjustPendolarePayment(
                            studente,
                            ref importoDaPagare,
                            ref importoMassimo,
                            studentiPagatiComePendolari
                        );
                    }

                    if (Math.Abs(importiPagati - (importoMassimo - importoReversali)) < 5)
                    {
                        studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                        studentiRimossiBag.Add((studente.codFiscale, "Importo da pagare minore di € 5"));
                        return;
                    }

                    studente.SetImportoDaPagareLordo(Math.Round(importoDaPagare - importiPagati - importoReversali, 2));
                    importoDaPagare -= (importiPagati + importoPA + importoDetrazioni + importoReversali);
                    importoDaPagare = Math.Round(importoDaPagare, 2);

                    // Accumulate importoDaPagare per thread
                    importoTotalePerThread.Value += importoDaPagare;

                    if ((importoDaPagare == 0 || Math.Abs(importoDaPagare) < 5) && !studentiDaRimuovereDallaTabella.ContainsKey(studente.codFiscale))
                    {
                        studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                        studentiRimossiBag.Add((studente.codFiscale, "Importo da pagare minore di € 5"));
                        return;
                    }

                    studente.SetImportoDaPagare(importoDaPagare);

                    if (isRiemissione)
                    {
                        studente.SetImportoDaPagareLordo(importoDaPagare);
                        studente.RemoveAllAssegnazioni();
                    }

                    if (importoDaPagare < 0 && !studentiDaRimuovereDallaTabella.ContainsKey(studente.codFiscale))
                    {
                        studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                        studentiRimossiBag.Add((studente.codFiscale, "Importo da pagare negativo"));
                        return;
                    }
                });

                // Sum up the importoTotale from all threads
                importoTotale = importoTotalePerThread.Values.Sum();

                Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato");

                // Remove students with zero or negative importoDaPagare
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagam studente = pair.Value;

                    if (studente.importoDaPagare > 0)
                    {
                        continue;
                    }
                    studentiDaRimuovereDallaTabella[studente.codFiscale] = true;
                }

                // Remove students from the database
                if (studentiDaRimuovereDallaTabella.Count > 0 && !string.IsNullOrWhiteSpace(selectedVecchioMandato))
                {
                    Logger.LogInfo(55, $"Lavorazione studenti - Rimozione dalla tabella d'appoggio");
                    string createTempTableSql = "CREATE TABLE #TempCodFiscale (cod_fiscale VARCHAR(255));";
                    SqlCommand createTableCommand = new(createTempTableSql, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    createTableCommand.ExecuteNonQuery();

                    DataTable codFiscalesTable = new DataTable();
                    codFiscalesTable.Columns.Add("cod_fiscale", typeof(string));

                    foreach (string codFiscale in studentiDaRimuovereDallaTabella.Keys)
                    {
                        DataRow row = codFiscalesTable.NewRow();
                        row["cod_fiscale"] = codFiscale;
                        codFiscalesTable.Rows.Add(row);

                        studentiPAnegativo.TryRemove(codFiscale, out _);
                    }

                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                    {
                        bulkCopy.DestinationTableName = "#TempCodFiscale";
                        bulkCopy.ColumnMappings.Add("cod_fiscale", "cod_fiscale");
                        bulkCopy.WriteToServer(codFiscalesTable);
                    }

                    string deleteSql = $@"
                        DELETE main
                        FROM {dbTableName} AS main
                        INNER JOIN #TempCodFiscale temp ON main.cod_fiscale = temp.cod_fiscale;
                    ";
                    SqlCommand deleteCommand = new(deleteSql, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    deleteCommand.ExecuteNonQuery();

                    SqlCommand dropTableCommand = new("DROP TABLE #TempCodFiscale;", CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    dropTableCommand.ExecuteNonQuery();

                    Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Rimozione dalla tabella d'appoggio - Completato");
                }

                // Export negative PA students
                if (studentiPAnegativo.Count > 0)
                {
                    DataTable estrazionePAneg = new DataTable();
                    estrazionePAneg.Columns.Add("cod_fiscale", typeof(string));
                    estrazionePAneg.Columns.Add("rimborso", typeof(double));
                    foreach (var studentePair in studentiPAnegativo)
                    {
                        estrazionePAneg.Rows.Add(studentePair.Key, Math.Round(studentePair.Value, 2).ToString("F2"));
                    }
                    Utilities.ExportDataTableToExcel(estrazionePAneg, selectedSaveFolder, fileName: "Studenti PA negativo");
                }

                // Export removed students with reasons
                if (studentiRimossiBag.Count > 0)
                {
                    DataTable studentiRimossi = new DataTable();
                    studentiRimossi.Columns.Add("CodFiscale");
                    studentiRimossi.Columns.Add("Motivazione");

                    foreach (var item in studentiRimossiBag)
                    {
                        studentiRimossi.Rows.Add(item.CodFiscale, item.Motivazione);
                    }

                    Utilities.ExportDataTableToExcel(studentiRimossi, selectedSaveFolder, fileName: "Studenti rimossi con motivi");
                }

                if (studentiPagatiComePendolari.Count > 0)
                {
                    DataTable studentiPagatiPendolari = new DataTable();
                    studentiPagatiPendolari.Columns.Add("CodFiscale");
                    studentiPagatiPendolari.Columns.Add("Motivazione");

                    foreach (var item in studentiPagatiComePendolari)
                    {
                        studentiPagatiPendolari.Rows.Add(item.CodFiscale, item.Motivazione);
                    }

                    Utilities.ExportDataTableToExcel(studentiPagatiPendolari, selectedSaveFolder, fileName: "Studenti fuori sede pagati come pendolari");
                }

                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereDallaTabella.Count} studenti dal pagamento");

                // Remove students from the main collection
                foreach (string codFiscale in studentiDaRimuovereDallaTabella.Keys)
                {
                    studentiDaPagare.Remove(codFiscale);
                }

                Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato");
            }

        }

        private void GenerateOutputFiles()
        {
            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files");
            string currentMonthName = DateTime.Now.ToString("MMMM").ToUpper();
            string currentYear = DateTime.Now.ToString("yyyy");
            string firstHalfAA = selectedAA.Substring(2, 2);
            string secondHalfAA = selectedAA.Substring(6, 2);
            string baseFolderPath = Utilities.EnsureDirectory(Path.Combine(selectedSaveFolder, currentMonthName + currentYear + "_" + firstHalfAA + secondHalfAA));

            //string currentBeneficio = isTR ? "TR" : tipoBeneficio;
            string currentCodTipoPagamento = tipoBeneficio + selectedTipoPagamento;

            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{currentCodTipoPagamento}'";
            SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();
            string beneficioFolderPath = Utilities.EnsureDirectory(Path.Combine(baseFolderPath, pagamentoDescrizione));

            //EstrazioneDatiBanca(beneficioFolderPath);

            bool doAllImpegni = selectedImpegno == "0000";
            IEnumerable<string> impegnoList = doAllImpegni ? impegniList : new List<string> { selectedImpegno };
            try
            {
                foreach (string impegno in impegnoList)
                {
                    Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno}");
                    string sqlImpegno = $"SELECT Descr FROM Impegni WHERE anno_accademico = '{selectedAA}' AND num_impegno = '{impegno}' AND categoria_pagamento = '{categoriaPagam}'";
                    SqlCommand cmdImpegno = new(sqlImpegno, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    string impegnoDescrizione = (string)cmdImpegno.ExecuteScalar();
                    string currentFolder = Utilities.EnsureDirectory(Path.Combine(beneficioFolderPath, $"imp-{impegno}-{impegnoDescrizione}"));
                    ProcessImpegno(impegno, currentFolder);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Completato");

            if (!insertInDatabase)
            {
                return;
            }

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
        private void ProcessImpegno(string impegno, string currentFolder)
        {
            var groupedStudents = studentiDaPagare.Values
                .Where(s => s.numeroImpegno == impegno)
                .ToList();

            if (groupedStudents.Any())
            {
                GenerateOutputFilesPA(currentFolder, groupedStudents, impegno);
            }

            void GenerateOutputFilesPA(string currentFolder, List<StudentePagam> studentsWithSameImpegno, string impegno)
            {
                Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Senza detrazioni");
                var studentsWithPA = studentsWithSameImpegno
                    .Where(s => (s.assegnazioni != null && s.assegnazioni.Count > 0) || (s.detrazioni != null && s.detrazioni.Count > 0 && s.detrazioni.Any(d => d.codReversale == "01")))
                    .ToList();
                var studentsWithoutPA = studentsWithSameImpegno
                    .Where(s => (s.assegnazioni == null || s.assegnazioni.Count <= 0) && !(s.detrazioni != null && s.detrazioni.Count > 0 && s.detrazioni.Any(d => d.codReversale == "01")))
                    .ToList();
                impegnoAmount.Add(impegno, new Dictionary<string, int>());
                if (studentsWithoutPA.Count > 0 && (selectedRichiestoPA == "2" || selectedRichiestoPA == "0" || selectedRichiestoPA == "3"))
                {
                    impegnoAmount[impegno].Add("Senza detrazioni", studentsWithoutPA.Count);
                    if (tipoStudente == "2")
                    {
                        ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: true, processAnniSuccessivi: true, "SenzaDetrazioni", "00", impegno);
                    }
                    else if (tipoStudente == "0")
                    {
                        ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: true, processAnniSuccessivi: false, "SenzaDetrazioni", "00", impegno);
                    }
                    else if (tipoStudente == "1")
                    {
                        ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: false, processAnniSuccessivi: true, "SenzaDetrazioni", "00", impegno);
                    }

                    DataTable dataTableMatricole = GenerareExcelDataTableNoDetrazioni(studentsWithoutPA.Where(s => s.annoCorso == 1).ToList(), impegno);
                    if (dataTableMatricole != null && dataTableMatricole.Rows.Count > 0)
                    {
                        Utilities.ExportDataTableToExcel(dataTableMatricole, currentFolder, false, "Matricole");
                    }

                    DataTable dataTableASuccessivi = GenerareExcelDataTableNoDetrazioni(studentsWithoutPA.Where(s => s.annoCorso != 1).ToList(), impegno);
                    if (dataTableASuccessivi != null && dataTableASuccessivi.Rows.Count > 0)
                    {
                        Utilities.ExportDataTableToExcel(dataTableASuccessivi, currentFolder, false, "AnniSuccessivi");
                    }
                }
                try
                {
                    if (studentsWithPA.Count > 0 && (selectedRichiestoPA == "2" || selectedRichiestoPA == "1"))
                    {
                        string newFolderPath = Utilities.EnsureDirectory(Path.Combine(currentFolder, "CON DETRAZIONE"));
                        ProcessStudentsByCodEnte(selectedCodEnte, studentsWithPA, newFolderPath, impegno);
                        GenerateGiuliaFile(newFolderPath, studentsWithPA, impegno);
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            void ProcessStudentsByAnnoCorso(List<StudentePagam> students, string folderPath, bool processMatricole, bool processAnniSuccessivi, string nomeFileInizio, string codEnteFlusso, string impegnoFlusso)
            {
                if (processMatricole && processAnniSuccessivi)
                {
                    ProcessAndWriteStudents(students, folderPath, $"{nomeFileInizio}", codEnteFlusso, impegnoFlusso);
                }
                else
                {
                    if (processMatricole)
                    {
                        ProcessAndWriteStudents(students.Where(s => s.annoCorso == 1).ToList(), folderPath, $"{nomeFileInizio}_Matricole", codEnteFlusso, impegnoFlusso);
                    }
                    if (processAnniSuccessivi)
                    {
                        ProcessAndWriteStudents(students.Where(s => s.annoCorso != 1).ToList(), folderPath, $"{nomeFileInizio}_AnniSuccessivi", codEnteFlusso, impegnoFlusso);
                    }
                }
            }
            void ProcessStudentsByCodEnte(string selectedCodEnte, List<StudentePagam> studentsWithPA, string newFolderPath, string impegno)
            {
                if (studentsWithPA.Count <= 0)
                {
                    return;
                }

                bool allCodEnte = selectedCodEnte == "00";

                // Create a dictionary to hold the groups
                Dictionary<string, List<string>> codEnteGroups = new Dictionary<string, List<string>>();

                if (allCodEnte)
                {
                    // Define the three main groups
                    codEnteGroups.Add("02", new List<string> { "02" });
                    codEnteGroups.Add("05", new List<string> { "05" });
                    var otherCodEntes = studentsWithPA.Select(s => s.codEnte).Distinct().Where(c => c != "02" && c != "05").ToList();
                    codEnteGroups.Add("Roma", otherCodEntes);
                }
                else
                {
                    codEnteGroups.Add(selectedCodEnte, new List<string> { selectedCodEnte });
                }

                foreach (var group in codEnteGroups)
                {
                    string groupName = group.Key;
                    List<string> groupCodEntes = group.Value;

                    var studentsInGroup = studentsWithPA.Where(s => groupCodEntes.Contains(s.codEnte)).ToList();

                    if (studentsInGroup.Count == 0)
                    {
                        continue;
                    }

                    string nomeCodEnte = "";

                    if (groupName == "Roma")
                    {
                        nomeCodEnte = "Roma territoriale";
                    }
                    else
                    {
                        string sqlCodEnte = $"SELECT Descrizione FROM Enti_di_gestione WHERE cod_ente = '{groupName}'";
                        SqlCommand cmdSede = new(sqlCodEnte, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        nomeCodEnte = (string)cmdSede.ExecuteScalar();
                        nomeCodEnte = Utilities.SanitizeColumnName(nomeCodEnte);
                    }

                    Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Flusso con detrazioni ente: {nomeCodEnte}");
                    string specificFolderPath = Utilities.EnsureDirectory(Path.Combine(newFolderPath, $"{nomeCodEnte}"));
                    impegnoAmount[impegno].Add(nomeCodEnte, studentsInGroup.Count);

                    if (tipoStudente == "2")
                    {
                        ProcessStudentsByAnnoCorso(studentsInGroup, specificFolderPath, processMatricole: true, processAnniSuccessivi: true, "Con Detrazioni_" + nomeCodEnte, groupName, impegno);
                    }
                    else
                    {
                        bool processMatricole = tipoStudente == "0";
                        ProcessStudentsByAnnoCorso(studentsInGroup, specificFolderPath, processMatricole: processMatricole, processAnniSuccessivi: !processMatricole, "Con Detrazioni_" + nomeCodEnte, groupName, impegno);
                    }
                }

                // Collect all codEnte codes for the final processing
                List<string> sediStudi = studentsWithPA.Select(s => s.codEnte).Distinct().ToList();
                DataTable dataTableMatricole = GenerareExcelDataTableConDetrazioni(studentsWithPA.Where(s => s.annoCorso == 1).ToList(), sediStudi, impegno);
                DataTable dataTableASuccessivi = GenerareExcelDataTableConDetrazioni(studentsWithPA.Where(s => s.annoCorso != 1).ToList(), sediStudi, impegno);

                if (dataTableMatricole != null && dataTableMatricole.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(dataTableMatricole, newFolderPath, false, "Matricole");
                }
                if (dataTableASuccessivi != null && dataTableASuccessivi.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(dataTableASuccessivi, newFolderPath, false, "AnniSuccessivi");
                }
            }

            void ProcessAndWriteStudents(List<StudentePagam> students, string folderPath, string fileName, string codEnteFlusso, string impegnoFlusso)
            {
                if (students.Any())
                {
                    DataTable dataTableFlusso = GenerareFlussoDataTable(students, codEnteFlusso);
                    if (dataTableFlusso != null && dataTableFlusso.Rows.Count > 0)
                    {
                        Utilities.WriteDataTableToTextFile(dataTableFlusso, folderPath, $"flusso_{fileName}_{impegnoFlusso}");
                    }
                    if (insertInDatabase)
                    {
                        InsertIntoMovimentazioni(students, impegnoFlusso);
                    }
                    studentiProcessatiAmount += students.Count;
                }
            }

            static void GenerateGiuliaFile(string newFolderPath, List<StudentePagam> studentsWithPA, string impegno)
            {
                DataTable dataTable = GenerareGiuliaDataTable(studentsWithPA, impegno);
                if (dataTable.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(dataTable, newFolderPath, true, "Dettaglio PA");
                }
            }
            static DataTable GenerareGiuliaDataTable(List<StudentePagam> studentsWithPA, string impegno)
            {
                Logger.LogInfo(60, $"Lavorazione studenti - impegno n°{impegno} - Generazione Dettaglio PA");
                int progressivo = 1;
                DataTable returnDataTable = new();

                _ = returnDataTable.Columns.Add("Progressivo");
                _ = returnDataTable.Columns.Add("ID Assegnazione");
                _ = returnDataTable.Columns.Add("Cognome");
                _ = returnDataTable.Columns.Add("Nome");
                _ = returnDataTable.Columns.Add("CodFiscale");
                _ = returnDataTable.Columns.Add("Residenza");
                _ = returnDataTable.Columns.Add("Data decorrenza");
                _ = returnDataTable.Columns.Add("Data fine assegnazione");
                _ = returnDataTable.Columns.Add("Data inizio PA");
                _ = returnDataTable.Columns.Add("Data fine PA");
                _ = returnDataTable.Columns.Add("Num giorni");
                _ = returnDataTable.Columns.Add("Importo borsa totale");
                _ = returnDataTable.Columns.Add("Importo lordo borsa");
                _ = returnDataTable.Columns.Add("Acconto PA");
                _ = returnDataTable.Columns.Add("Saldo PA");
                _ = returnDataTable.Columns.Add("Importo netto borsa");
                _ = returnDataTable.Columns.Add("Stato correttezza");
                _ = returnDataTable.Columns.Add("Controllo status sede");

                foreach (StudentePagam studente in studentsWithPA)
                {
                    if (studente.assegnazioni == null || studente.assegnazioni.Count <= 0)
                    {
                        continue;
                    }

                    if (studente.codFiscale == "DGLFPP01H27H501U")
                    {
                        string test = "";
                    }

                    double accontoPA = Math.Round(studente.importoAccontoPA, 2);
                    double saldoPA = Math.Round(studente.importoSaldoPA, 2);
                    double saldo = Math.Round(studente.importoDaPagare, 2);

                    DateTime dataIniziale = DateTime.MinValue;
                    DateTime dataFinale = DateTime.MinValue;

                    foreach (Assegnazione assegnazioneCheck in studente.assegnazioni)
                    {
                        if (dataIniziale == DateTime.MinValue)
                        {
                            dataIniziale = assegnazioneCheck.dataDecorrenza;
                        }

                        if (dataFinale < assegnazioneCheck.dataFineAssegnazione)
                        {
                            dataFinale = assegnazioneCheck.dataFineAssegnazione;
                        }
                    }

                    bool controlloApprofondito = false;

                    // Check if the student has been "in" for more than 7 months
                    bool isMoreThanSevenMonths = (dataFinale - dataIniziale).TotalDays > 7 * 30;  // Approximate 7 months as 7 * 30 days

                    // If the student has been "in" for less than or equal to 7 months, do further checks
                    if (dataFinale < new DateTime(2024, 7, 15) && !isMoreThanSevenMonths)
                    {
                        bool hasDomicilio = studente.domicilioCheck;
                        bool isMoreThanHalfAbroad = studente.numeroComponentiNucleoFamiliareEstero >= (studente.numeroComponentiNucleoFamiliare / 2.0);

                        // Determine if the student needs a controllo
                        if (!hasDomicilio && !isMoreThanHalfAbroad)
                        {
                            controlloApprofondito = true;
                        }
                    }

                    foreach (Assegnazione assegnazione in studente.assegnazioni)
                    {
                        _ = returnDataTable.Rows.Add(
                            progressivo.ToString(),
                            assegnazione.idAssegnazione,
                            studente.cognome,
                            studente.nome,
                            studente.codFiscale,
                            assegnazione.codPensionato,
                            assegnazione.dataDecorrenza.ToString("dd/MM/yyyy"),
                            assegnazione.dataFineAssegnazione.ToString("dd/MM/yyyy"),
                            dataIniziale.ToString("dd/MM/yyyy"),
                            dataFinale.ToString("dd/MM/yyyy"),
                            (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days.ToString(),
                            studente.importoBeneficio.ToString("F2"),
                            studente.importoDaPagareLordo.ToString("F2"),
                            accontoPA.ToString("F2"),
                            saldoPA.ToString("F2"),
                            saldo.ToString("F2"),
                            assegnazione.statoCorrettezzaAssegnazione.ToString(),
                            controlloApprofondito ? "CONTROLLARE" : "OK"
                        );
                    }

                    progressivo++;
                }
                _ = returnDataTable.Rows.Add(" ");
                return returnDataTable;
            }

            DataTable GenerareFlussoDataTable(List<StudentePagam> studentiDaGenerare, string codEnteFlusso)
            {
                if (!studentiDaGenerare.Any())
                {
                    return new DataTable();
                }
                DataTable studentsData = new();
                _ = studentsData.Columns.Add("Incrementale", typeof(int));
                _ = studentsData.Columns.Add("Cod_fiscale", typeof(string));
                _ = studentsData.Columns.Add("Cognome", typeof(string));
                _ = studentsData.Columns.Add("Nome", typeof(string));
                _ = studentsData.Columns.Add("totale_lordo", typeof(double));
                _ = studentsData.Columns.Add("reversali", typeof(double));
                _ = studentsData.Columns.Add("importo_netto", typeof(double));
                _ = studentsData.Columns.Add("conferma_pagamento", typeof(int));
                _ = studentsData.Columns.Add("IBAN", typeof(string));
                _ = studentsData.Columns.Add("Istituto_bancario", typeof(string));
                _ = studentsData.Columns.Add("italiano", typeof(string));
                _ = studentsData.Columns.Add("indirizzo_residenza", typeof(string));
                _ = studentsData.Columns.Add("cod_catastale_residenza", typeof(string));
                _ = studentsData.Columns.Add("provincia_residenza", typeof(string));
                _ = studentsData.Columns.Add("cap_residenza", typeof(string));
                _ = studentsData.Columns.Add("nazione_citta_residenza", typeof(string));
                _ = studentsData.Columns.Add("sesso", typeof(string));
                _ = studentsData.Columns.Add("data_nascita", typeof(string));
                _ = studentsData.Columns.Add("luogo_nascita", typeof(string));
                _ = studentsData.Columns.Add("cod_catastale_luogo_nascita", typeof(string));
                _ = studentsData.Columns.Add("provincia_nascita", typeof(string));
                _ = studentsData.Columns.Add("vuoto1", typeof(string));
                _ = studentsData.Columns.Add("vuoto2", typeof(string));
                _ = studentsData.Columns.Add("vuoto3", typeof(string));
                _ = studentsData.Columns.Add("vuoto4", typeof(string));
                _ = studentsData.Columns.Add("vuoto5", typeof(string));
                _ = studentsData.Columns.Add("mail", typeof(string));
                _ = studentsData.Columns.Add("vuoto6", typeof(string));
                _ = studentsData.Columns.Add("telefono", typeof(long));

                int incremental = 1;


                foreach (StudentePagam studente in studentiDaGenerare)
                {
                    _ = DateTime.TryParse(selectedDataRiferimento, out DateTime dataTabella);
                    string dataCreazioneTabella = dataTabella.ToString("ddMMyy");

                    string annoAccademicoBreve = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));

                    string mandatoProvvisorio = selectedNumeroMandato;
                    if (string.IsNullOrWhiteSpace(selectedNumeroMandato))
                    {
                        mandatoProvvisorio = $"{codTipoPagamento}_{dataCreazioneTabella}_{annoAccademicoBreve}_{studente.numeroImpegno}_{codEnteFlusso}";
                    }
                    studente.SetMandatoProvvisorio(mandatoProvvisorio);

                    int straniero = studente.residenza.provincia == "EE" ? 0 : 1;
                    string indirizzoResidenza = straniero == 0 ? studente.residenza.indirizzo.Replace("//", "-") : studente.residenza.indirizzo;
                    string capResidenza = straniero == 0 ? "00000" : studente.residenza.CAP;
                    string dataSenzaSlash = studente.dataNascita.Replace("/", "");
                    bool hasAssegnazione = (categoriaPagam == "PR" && studente.detrazioni != null && studente.detrazioni.Count > 0 && studente.detrazioni.FirstOrDefault(d => d.codReversale == "01") != null) || (studente.assegnazioni != null && studente.assegnazioni.Count > 0);

                    double accontoPA = studente.importoAccontoPA;
                    if (categoriaPagam == "SA")
                    {
                        accontoPA = 0;
                    }

                    _ = studentsData.Rows.Add(
                        incremental,
                        studente.codFiscale,
                        studente.cognome,
                        studente.nome,
                        studente.importoDaPagareLordo,
                        hasAssegnazione ? (studente.importoSaldoPA == 0 ? accontoPA : studente.importoSaldoPA) : 0,
                        studente.importoDaPagare,
                        1,
                        studente.IBAN,
                        studente.swift,
                        straniero,
                        indirizzoResidenza,
                        studente.residenza.codComune,
                        studente.residenza.provincia,
                        capResidenza,
                        studente.residenza.nomeComune,
                        studente.sesso,
                        dataSenzaSlash,
                        studente.luogoNascita.nomeComune,
                        studente.luogoNascita.codComune,
                        studente.luogoNascita.provincia,
                        "",
                        "",
                        "",
                        "",
                        "",
                        studente.indirizzoEmail,
                        "",
                        studente.telefono
                        );
                    incremental++;
                }
                return studentsData;
            }
            DataTable GenerareExcelDataTableConDetrazioni(List<StudentePagam> studentiDaGenerare, List<string> sediStudi, string impegno)
            {
                if (!studentiDaGenerare.Any())
                {
                    return new DataTable();
                }
                DataTable studentsData = new();
                string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
                SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                string pagamentoDescrizione = (string)cmd.ExecuteScalar();

                string annoAccedemicoFileName = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));

                string impegnoNome = "impegno " + impegno;

                string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

                _ = studentsData.Columns.Add("1");
                _ = studentsData.Columns.Add("2");
                _ = studentsData.Columns.Add("3");
                _ = studentsData.Columns.Add("4");
                _ = studentsData.Columns.Add("5");
                _ = studentsData.Columns.Add("6");
                _ = studentsData.Columns.Add("7");
                _ = studentsData.Columns.Add("8");

                _ = studentsData.Rows.Add(titolo);
                _ = studentsData.Rows.Add("ALLEGATO DETERMINA");

                foreach (string codEnte in sediStudi)
                {
                    string sqlCodEnte = $"SELECT descrizione FROM Enti_di_gestione WHERE cod_ente = '{codEnte}'";
                    SqlCommand cmdSede = new(sqlCodEnte, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    string nomeCodEnte = (string)cmdSede.ExecuteScalar();


                    string nomePA = categoriaPagam == "PR" ? "ACCONTO PA" : "SALDO COSTO DEL SERVIZIO";

                    _ = studentsData.Rows.Add(" ");
                    _ = studentsData.Rows.Add(nomeCodEnte);
                    _ = studentsData.Rows.Add("N.PROG.", "NUMERO DOMANDA", "CODICE FISCALE", "COGNOME", "NOME", "TOTALE LORDO", nomePA, "IMPORTO NETTO");

                    int progressivo = 1;
                    double totaleLordo = 0;
                    double totalePA = 0;
                    double totaleNetto = 0;
                    foreach (StudentePagam s in studentiDaGenerare)
                    {
                        if (s.codEnte != codEnte)
                        {
                            continue;
                        }

                        double costoPA = categoriaPagam == "PR" ? s.importoAccontoPA : s.importoSaldoPA;

                        _ = studentsData.Rows.Add(progressivo, s.numDomanda, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), costoPA.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                        totaleLordo += s.importoDaPagareLordo;
                        totalePA += costoPA;
                        totaleNetto += s.importoDaPagare;
                        progressivo++;
                    }
                    _ = studentsData.Rows.Add(" ");
                    _ = studentsData.Rows.Add(" ");
                    _ = studentsData.Rows.Add(" ", " ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totalePA, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
                    _ = studentsData.Rows.Add(" ");
                    _ = studentsData.Rows.Add(" ");
                }


                return studentsData;
            }
            DataTable GenerareExcelDataTableNoDetrazioni(List<StudentePagam> studentiDaGenerare, string impegno)
            {
                if (!studentiDaGenerare.Any())
                {
                    return new DataTable();
                }
                DataTable studentsData = new();
                string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
                SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                string pagamentoDescrizione = (string)cmd.ExecuteScalar();

                string annoAccedemicoFileName = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));

                string impegnoNome = "impegno " + impegno;

                string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

                _ = studentsData.Columns.Add("1");
                _ = studentsData.Columns.Add("2");
                _ = studentsData.Columns.Add("3");
                _ = studentsData.Columns.Add("4");
                _ = studentsData.Columns.Add("5");
                _ = studentsData.Columns.Add("6");
                _ = studentsData.Columns.Add("7");
                _ = studentsData.Columns.Add("8");

                _ = studentsData.Rows.Add(titolo);
                _ = studentsData.Rows.Add("ALLEGATO DETERMINA");
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add("N.PROG.", "NUMERO DOMANDA", "CODICE FISCALE", "COGNOME", "NOME", "TOTALE LORDO", "ACCONTO PA", "IMPORTO NETTO");

                int progressivo = 1;
                double totaleLordo = 0;
                double totaleAcconto = 0;
                double totaleNetto = 0;
                foreach (StudentePagam s in studentiDaGenerare)
                {

                    double importoAcconto = 0;

                    _ = studentsData.Rows.Add(progressivo, s.numDomanda, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), importoAcconto.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                    totaleLordo += s.importoDaPagareLordo;
                    totaleAcconto += importoAcconto;
                    totaleNetto += s.importoDaPagare;
                    progressivo++;
                }
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ", " ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totaleAcconto, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
                return studentsData;
            }
        }
        private void InsertIntoMovimentazioni(List<StudentePagam> studentiDaProcessare, string impegno)
        {
            List<StudentePagam> studentiSenzaImpegno = new();
            foreach (StudentePagam studente in studentiDaProcessare)
            {
                if (string.IsNullOrWhiteSpace(studente.numeroImpegno) || impegno != studente.numeroImpegno)
                {
                    studentiSenzaImpegno.Add(studente);
                }
            }

            _ = studentiDaProcessare.RemoveAll(studentiSenzaImpegno.Contains);

            if (studentiDaProcessare.Count == 0)
            {
                throw new Exception("Nessuno studente con impegno trovato");
            }

            try
            {
                int lastCodiceMovimento = 0;
                int nextCodiceMovimento = 0;
                Logger.LogInfo(80, $"Lavorazione studenti - Inserimento in movimenti contabili");
                Dictionary<int, StudentePagam> codMovimentiPerStudente = new();
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_GENERALI (ID_CAUSALE_MOVIMENTO_GENERALE, IMPORTO_MOVIMENTO, UTENTE_VALIDAZIONE, DATA_VALIDITA_MOVIMENTO_GENERALE, NOTE_VALIDAZIONE_MOVIMENTO, COD_MANDATO) VALUES ";
                string note = "Inserimento tramite elaborazione file pagamenti";
                string notaNow = note;

                if (studentiDaProcessare.Any())
                {
                    StudentePagam firstStudent = studentiDaProcessare.First();
                    if (firstStudent.pagatoPendolare)
                    {
                        notaNow = "Pagamento effettuato come pendolare";
                    }
                    else
                    {
                        notaNow = note;
                    }
                    string firstStudentValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            codTipoPagamento,
                            firstStudent.importoDaPagare.ToString(CultureInfo.InvariantCulture),
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            notaNow,
                            firstStudent.mandatoProvvisorio);
                    string initialInsertQuery = $"{baseSqlInsert} {firstStudentValues};";
                    using (SqlCommand cmd = new(initialInsertQuery, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    })
                    {
                        _ = cmd.ExecuteNonQuery();
                    }
                    string sqlCodMovimento = "SELECT TOP(1) CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI ORDER BY CODICE_MOVIMENTO DESC";
                    object? result;
                    using (SqlCommand cmdCM = new(sqlCodMovimento, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    })
                    {
                        result = cmdCM.ExecuteScalar() ?? null;
                    }
                    if (result != null)
                    {
                        lastCodiceMovimento = Convert.ToInt32(result);
                        codMovimentiPerStudente.Add(lastCodiceMovimento, firstStudent);
                    }
                    else
                    {
                        throw new Exception("Ultimo codice movimento non trovato");
                    }
                }
                else
                {
                    throw new Exception("Lista studenti da pagare è vuota a questo punto");
                }

                const int batchSize = 1000;
                int numberOfBatches = (int)Math.Ceiling((double)(studentiDaProcessare.Count - 1) / batchSize);
                int currentMovimento = lastCodiceMovimento;
                StringBuilder finalQueryBuilder = new();
                Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili generali - batch n°0");
                for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
                {
                    nextCodiceMovimento = currentMovimento + 1;
                    StringBuilder queryBuilder = new();
                    _ = queryBuilder.Append(baseSqlInsert);

                    var batch = studentiDaProcessare.Skip(1 + batchNumber * batchSize).Take(batchSize);
                    List<string> valuesList = new();

                    foreach (StudentePagam studente in batch)
                    {
                        if (studente.pagatoPendolare)
                        {
                            notaNow = "Pagamento effettuato come pendolare";
                        }
                        else
                        {
                            notaNow = note;
                        }
                        string studenteValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            codTipoPagamento,
                            studente.importoDaPagare.ToString(CultureInfo.InvariantCulture),
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            notaNow,
                            studente.mandatoProvvisorio);

                        valuesList.Add(studenteValues);
                        if (codMovimentiPerStudente.ContainsKey(nextCodiceMovimento))
                        {
                            Logger.LogError(null, $"Codice movimento {nextCodiceMovimento} già presente, studente duplicato? CF: {studente.codFiscale}");
                            continue;
                        }
                        codMovimentiPerStudente.Add(nextCodiceMovimento, studente);
                        nextCodiceMovimento++;
                    }

                    currentMovimento = nextCodiceMovimento - 1;

                    _ = queryBuilder.Append(string.Join(",", valuesList));
                    _ = queryBuilder.Append("; ");

                    _ = finalQueryBuilder.Append(queryBuilder);
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili generali - batch n°{batchNumber}");
                }
                string finalQuery = finalQueryBuilder.ToString();
                try
                {
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                }
                catch
                {
                    throw;
                }

                InsertIntoStatiDelMovimentoContabile(codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariPagamenti(codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariDetrazioni(codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariAssegnazioni(codMovimentiPerStudente);
            }
            catch (Exception ex)
            {
                Logger.LogError(100, ex.Message);
                throw;
            }

            void InsertIntoStatiDelMovimentoContabile(Dictionary<int, StudentePagam> codMovimentiPerStudente)
            {
                try
                {
                    const int batchSize = 1000;
                    string baseSqlInsert = "INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO) VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;
                    Logger.LogInfo(80, $"Lavorazione studenti - Stati del movimento contabile");
                    foreach (var entry in codMovimentiPerStudente)
                    {
                        int codMovimento = entry.Key;
                        string insertStatement = $"(2, '{codMovimento}', '{DateTime.Now:dd/MM/yyyy}', 'sa')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; ");

                            batchStatements.Clear();
                            currentBatchSize = 0;
                        }
                    }

                    string finalQuery = finalQueryBuilder.ToString();

                    if (string.IsNullOrWhiteSpace(finalQuery))
                    {
                        throw new Exception("STATI_DEL_MOVIMENTO_CONTABILE senza contenuti.");
                    }
                    try
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        throw;
                    }

                    Logger.LogInfo(80, $"Lavorazione studenti - Stati del movimento contabile - completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariPagamenti(Dictionary<int, StudentePagam> codMovimentiPerStudente)
            {
                try
                {
                    string codMovimentoElementare = "00";
                    string sqlCodMovimento = $"SELECT DISTINCT Cod_mov_contabile_elem FROM Decod_pagam_new where Cod_tipo_pagam_new = '{codTipoPagamento}'";
                    SqlCommand cmdCM = new(sqlCodMovimento, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    object result = cmdCM.ExecuteScalar();
                    if (result != null)
                    {
                        string? nullableCode = result.ToString();
                        string code;
                        if (nullableCode != null)
                        {
                            code = nullableCode;
                        }
                        else
                        {
                            throw new Exception($"Codice movimento nullo nel database");
                        }
                        codMovimentoElementare = code;
                    }


                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari");
                    foreach (KeyValuePair<int, StudentePagam> entry in codMovimentiPerStudente)
                    {
                        int codMovimento = entry.Key;
                        double importoLordo = entry.Value.importoDaPagareLordo;
                        int segno = 1;

                        foreach (Detrazione detrazione in entry.Value.detrazioni)
                        {
                            if (detrazione.codReversale != "01")
                            {
                                importoLordo += detrazione.importo;
                            }
                        }

                        string importoFinale = importoLordo.ToString(CultureInfo.InvariantCulture);

                        // Assuming you might need some data from the StudentePagam object, you can access it like this: entry.Value
                        string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '{codMovimentoElementare}', '{codMovimento}', '{importoFinale}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(finalQuery))
                    {
                        throw new Exception("MOVIMENTI_CONTABILI_ELEMENTARI senza contenuti.");
                    }
                    try
                    {
                        // Execute all accumulated SQL statements at once
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery(); // Execute the query
                    }
                    catch
                    {
                        throw;
                    }

                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariDetrazioni(Dictionary<int, StudentePagam> codMovimentiPerStudente)
            {
                try
                {
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Detrazioni");
                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;

                    foreach (KeyValuePair<int, StudentePagam> entry in codMovimentiPerStudente)
                    {

                        if (entry.Value.detrazioni == null || entry.Value.detrazioni.Count <= 0)
                        {
                            continue;
                        }

                        foreach (Detrazione detrazione in entry.Value.detrazioni)
                        {
                            int codMovimento = entry.Key;
                            string importoDaDetrarre = detrazione.importo.ToString(CultureInfo.InvariantCulture);
                            int segno = 0;

                            if (detrazione.needUpdate)
                            {
                                string updateStr = $@"UPDATE MOVIMENTI_CONTABILI_ELEMENTARI
                                    SET CODICE_MOVIMENTO = '{codMovimento}'
                                    ,STATO = 2
                                    WHERE ID_CAUSALE = '{detrazione.codReversale}'
                                    AND ANNO_ACCADEMICO = '{selectedAA}'
                                    AND CODICE_FISCALE = '{entry.Value.codFiscale}'";
                                using SqlCommand cmd = new(updateStr, CONNECTION, sqlTransaction)
                                {
                                    CommandTimeout = 9000000
                                };
                                _ = cmd.ExecuteNonQuery();
                                continue;
                            }

                            // Assuming you might need some data from the StudentePagam object, you can access it like this: entry.Value
                            string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '01', '{codMovimento}', '{importoDaDetrarre}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                            batchStatements.Add(insertStatement);
                            currentBatchSize++;

                            // Execute batch when reaching batchSize or end of dictionary
                            if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last() && detrazione == entry.Value.detrazioni.Last()))
                            {
                                _ = finalQueryBuilder.Append(baseSqlInsert);
                                _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                                _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                                batchStatements.Clear(); // Clear the batch for the next round
                                currentBatchSize = 0; // Reset the batch size counter
                            }
                        }
                    }

                    // Execute any remaining statements that didn't fill a complete batch
                    if (batchStatements.Count > 0)
                    {
                        _ = finalQueryBuilder.Append(baseSqlInsert);
                        _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                        _ = finalQueryBuilder.Append("; ");
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Detrazioni - Completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariAssegnazioni(Dictionary<int, StudentePagam> codMovimentiPerStudente)
            {
                try
                {
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Assegnazioni");
                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;

                    foreach (KeyValuePair<int, StudentePagam> entry in codMovimentiPerStudente)
                    {

                        if (entry.Value.assegnazioni == null || entry.Value.assegnazioni.Count <= 0)
                        {
                            continue;
                        }

                        double costoPA = 0;
                        foreach (Assegnazione assegnazione in entry.Value.assegnazioni)
                        {
                            if (assegnazione.costoTotale <= 0)
                            {
                                continue;
                            }
                            costoPA += assegnazione.costoTotale;
                        }
                        costoPA = Math.Round(costoPA - entry.Value.importoAccontoPA, 2);
                        int codMovimento = entry.Key;

                        string costoPostoAlloggio = costoPA.ToString(CultureInfo.InvariantCulture);
                        int segno = 0;

                        // Assuming you might need some data from the StudentePagam object, you can access it like this: entry.Value
                        string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '02', '{codMovimento}', '{costoPostoAlloggio}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last()))
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }


                    // Execute any remaining statements that didn't fill a complete batch
                    if (batchStatements.Count > 0)
                    {
                        _ = finalQueryBuilder.Append(baseSqlInsert);
                        _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                        _ = finalQueryBuilder.Append("; ");
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Assegnazioni - Completo");
                }
                catch
                {
                    throw;
                }
            }
        }
    }
}
