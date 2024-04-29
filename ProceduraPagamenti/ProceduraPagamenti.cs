using DocumentFormat.OpenXml.Wordprocessing;
using ProceduraPagamentiNET7.ProceduraPagamenti;
using ProcedureNet7.Storni;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal class ProceduraPagamenti : BaseProcedure<ArgsPagamenti>
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

        readonly List<Studente> listaStudentiDaPagare = new();
        readonly Dictionary<Studente, List<string>> studentiConErroriPA = new();
        readonly List<Studente> listaStudentiDaNonPagare = new();

        List<string> impegniList = new();

        Dictionary<string, string> dictQueryWhere = new();
        string stringQueryWhere = string.Empty;
        bool usingStringWhere = false;

        readonly bool isTR = false;

        SqlConnection? conn = null;
        SqlTransaction? sqlTransaction = null;
        bool exitProcedureEarly = false;

        public int studentiProcessatiAmount = 0;
        public Dictionary<string, Dictionary<string, int>> impegnoAmount = new Dictionary<string, Dictionary<string, int>>();

        public ProceduraPagamenti(MainUI mainUI, string connection_string) : base(mainUI, connection_string) { }

        public override void RunProcedure(ArgsPagamenti args)
        {
            Logger.LogDebug(null, "Inizio dell'esecuzione di RunProcedure"); // Logging method start
            try
            {
                InitializeProcedure(args);
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo InitializeProcedure");
                    return;
                }

                using SqlConnection conn = new(CONNECTION_STRING);
                this.conn = conn;
                this.conn.Open();
                sqlTransaction = this.conn.BeginTransaction();

                if (this.conn == null || sqlTransaction == null)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                    return;
                }

                HandleTipoPagamentoDialog();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTipoPagamentoDialog");
                    return;
                }

                HandlePagamentoSettingsDialog();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandlePagamentoSettingsDialog");
                    return;
                }

                HandleTableNameSelectionDialog();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTableNameSelectionDialog");
                    return;
                }

                HandleRiepilogoPagamentiDialog();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleRiepilogoPagamentiDialog");
                    return;
                }

                CheckAndCreateDatabaseTable();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo CheckAndCreateDatabaseTable");
                    return;
                }

                HandleFiltroManuale();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleFiltroManuale");
                    return;
                }

                ClearMovimentiIfNeeded();
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo ClearMovimentiIfNeeded");
                    return;
                }

                GenerateStudentListToPay();
                ProcessStudentList();

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

                sqlTransaction?.Commit();
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
            _mainForm.inProcedure = true;
            AssignArgumentValues(args);
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
        }

        private void HandleTipoPagamentoDialog()
        {
            if (conn == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }

            _ = _mainForm.Invoke((MethodInvoker)delegate
            {
                using SelectTipoPagam selectTipoPagam = new(conn, sqlTransaction);
                selectTipoPagam.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = selectTipoPagam.ShowDialog(_mainForm);
                ProcessTipoPagamentoDialogResult(result, selectTipoPagam);
            });
        }

        private void ProcessTipoPagamentoDialogResult(DialogResult result, SelectTipoPagam selectTipoPagam)
        {
            if (result == DialogResult.OK)
            {
                selectedTipoPagamento = selectTipoPagam.SelectedCodPagamento;
                tipoBeneficio = selectTipoPagam.SelectedTipoBeneficio;
                if (tipoBeneficio == "TR") tipoBeneficio = "BS";
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
            if (conn == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }
            _ = _mainForm.Invoke((MethodInvoker)delegate
            {
                using SelectPagamentoSettings selectPagamentoSettings = new(conn, sqlTransaction, selectedAA, tipoBeneficio, categoriaPagam);
                selectPagamentoSettings.StartPosition = FormStartPosition.CenterParent;
                DialogResult dialogResult = selectPagamentoSettings.ShowDialog(_mainForm);
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
            _ = _mainForm.Invoke((MethodInvoker)delegate
            {
                using SelectTableName selectTableName = new();
                selectTableName.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = selectTableName.ShowDialog(_mainForm);
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
            _ = _mainForm.Invoke((MethodInvoker)delegate
            {
                using RiepilogoPagamenti riepilogo = new(RiepilogoArguments.Instance);
                riepilogo.StartPosition = FormStartPosition.CenterParent;
                DialogResult result = riepilogo.ShowDialog(_mainForm);
                if (result == DialogResult.Cancel)
                {
                    exitProcedureEarly = true;
                }
            });
        }

        private void CheckAndCreateDatabaseTable()
        {
            Logger.LogDebug(null, "Inizio della verifica e creazione della tabella del database"); // Logging method start
            if (conn == null || sqlTransaction == null)
            {
                Logger.LogDebug(null, "Connessione o transazione non disponibili, uscita anticipata da CheckAndCreateDatabaseTable");
                exitProcedureEarly = true;
                return;
            }

            using (SqlCommand command = new($"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'{dbTableName}'", conn, sqlTransaction))
            {
                dbTableExists = command.ExecuteScalar() != null;
            }

            if (selectedTipoProcedura == "0" || selectedTipoProcedura == "2")
            {
                Logger.LogDebug(null, "Creazione della tabella del database in base al tipo di procedura selezionato");
                CreateDBTable();
            }

            if (selectedTipoProcedura == "2" || (selectedTipoProcedura == "1" && !dbTableExists))
            {
                Logger.LogInfo(null, "Verifica o creazione della tabella del database completata");
                _mainForm.inProcedure = false;
                return;
            }

            Logger.LogDebug(null, "Fine della verifica e creazione della tabella del database"); // Logging method end
        }


        private void HandleFiltroManuale()
        {
            if (conn == null || sqlTransaction == null)
            {
                exitProcedureEarly = true;
                return;
            }
            if (usingFiltroManuale)
            {
                _ = _mainForm.Invoke((MethodInvoker)delegate
                {
                    using FiltroManuale selectFiltroManuale = new(conn, sqlTransaction, dbTableName);
                    selectFiltroManuale.StartPosition = FormStartPosition.CenterParent;
                    DialogResult result = selectFiltroManuale.ShowDialog(_mainForm);

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
                if (conn == null || sqlTransaction == null)
                {
                    exitProcedureEarly = true;
                    return;
                }
                ClearMovimentiContabili();
            }
        }

        private void FinalizeProcedure()
        {
            _mainForm.inProcedure = false;
            Logger.LogInfo(100, "Procedura Completa.");
        }

        void CreateDBTable()
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
	                                            AND StatisticheTotali.Cod_fiscale IN (
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
                                                    )
                                            ");

            string sqlQuery = queryBuilder.ToString();

            SqlCommand cmd = new(sqlQuery, conn, sqlTransaction)
            {
                CommandTimeout = 90000000
            };
            _ = cmd.ExecuteNonQuery();
            progressUpdater.StopUpdating();
            Logger.LogInfo(1, "Fine creazione tabella d'appoggio");
        }
        void ClearMovimentiContabili()
        {
            try
            {
                Logger.LogInfo(10, "Pulizia da movimenti contabili elementari del vecchio codice mandato");
                string deleteSQL = $@"          DELETE FROM [MOVIMENTI_CONTABILI_ELEMENTARI] WHERE codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand deleteCmd = new(deleteSQL, conn, sqlTransaction);
                _ = deleteCmd.ExecuteNonQuery();
                Logger.LogInfo(10, "Set in movimenti contabili elementari dello stato a 0 dove era in elaborazione");
                string updateSQL = $@"          UPDATE [MOVIMENTI_CONTABILI_ELEMENTARI] SET stato = 0, codice_movimento = null WHERE stato = 2 AND codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand updateCmd = new(updateSQL, conn, sqlTransaction);
                _ = updateCmd.ExecuteNonQuery();
                Logger.LogInfo(10, "Pulizia da stati del movimento contabile del vecchio codice mandato");
                string deleteStatiSQL = $@"     DELETE FROM [STATI_DEL_MOVIMENTO_CONTABILE] WHERE codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
                SqlCommand deleteStatiCmd = new(deleteStatiSQL, conn, sqlTransaction);
                _ = deleteStatiCmd.ExecuteNonQuery();
                Logger.LogInfo(10, "Pulizia da movimenti contabili generali del vecchio codice mandato");
                string deleteGeneraliSQL = $@"  DELETE FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%')";
                SqlCommand deleteGeneraliCmd = new(deleteGeneraliSQL, conn, sqlTransaction);
                _ = deleteGeneraliCmd.ExecuteNonQuery();
            }
            catch
            {
                throw;
            }
        }
        void GenerateStudentListToPay()
        {
            Logger.LogDebug(null, "Inizio della generazione della lista degli studenti per il pagamento"); // Start of method
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

            SqlCommand readData = new(dataQuery, conn, sqlTransaction)
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
                    string studenteCodEnte = Utilities.SafeGetString(reader, "cod_ente");

                    bool skipTipoStudente = false;
                    bool skipCodEnte = false;

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


                    Studente studente = new(
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "num_domanda")),
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_fiscale").ToUpper()),
                            Utilities.SafeGetString(reader, "Cognome").Trim(),
                            Utilities.SafeGetString(reader, "Nome").Trim(),
                            (DateTime)reader["Data_nascita"],
                            Utilities.SafeGetString(reader, "sesso"),
                            studenteCodEnte,
                            disabile == 1,
                            double.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "imp_beneficio")), out double importoBeneficio) ? importoBeneficio : 0,
                            annoCorso,
                            int.TryParse(Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "cod_corso")), out int codCorso) ? codCorso : 0,
                            Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "EsitoPA")) == "2",
                            superamentoEsami == 1,
                            superamentoEsamiTassaRegionale == 1
                        );

                    listaStudentiDaPagare.Add(studente);
                    studentCount++;
                }
                Logger.LogInfo(null, $"Elaborati {studentCount} studenti nella query.");
            }
            Logger.LogInfo(20, $"UPDATE:Generazione studenti - Completato");
        }
        private void FilterPagamenti()
        {
            Logger.LogDebug(null, "Inizio del filtraggio dei pagamenti degli studenti");
            string sqlPagam = $@"
                    SELECT
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
            if (tipoBeneficio != "PL")
            {
                sqlPagam += $@"
                    WHERE
                        Domanda.Anno_accademico = '{selectedAA}'";
            }
            SqlCommand readData = new(sqlPagam, conn, sqlTransaction);
            Logger.LogInfo(11, $"Lavorazione studenti - inserimento pagamenti");
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (studente.codFiscale == "DTTSLV00S48H501J")
                        {
                            string test = "";
                        }
                        string cod_tipo_pagam = Utilities.SafeGetString(reader, "Cod_tipo_pagam");

                        if (cod_tipo_pagam.Substring(0, 2) != tipoBeneficio)
                        {
                            continue;
                        }

                        if (cod_tipo_pagam.Substring(0, 3) == "BST" && !isTR)
                        {
                            continue;
                        }

                        if (cod_tipo_pagam.Substring(0, 3) != "BST" && isTR)
                        {
                            continue;
                        }
                        double.TryParse(Utilities.SafeGetString(reader, "Imp_pagato"), out double impPagato);
                        studente.AddPagamentoEffettuato(
                            cod_tipo_pagam,
                            impPagato,
                            Utilities.SafeGetString(reader, "Ritirato_azienda") == "1"
                            );
                    }
                    else
                    {
                        Logger.LogWarning(null, $"Attenzione: Studente non trovato per il codice fiscale {codFiscale}");
                    }
                }
            }

            List<Studente> studentiDaRimuovere = new();
            foreach (Studente studenteDaControllare in listaStudentiDaPagare)
            {
                if (studenteDaControllare.codFiscale == "DTTSLV00S48H501J")
                {
                    string test = "";
                }
                bool stessoPagamento = false;

                if (studenteDaControllare.pagamentiEffettuati == null || studenteDaControllare.pagamentiEffettuati.Count <= 0)
                {
                    continue;
                }
                double importiPagati = 0;
                foreach (Pagamento pagamento in studenteDaControllare.pagamentiEffettuati)
                {
                    if (pagamento.codTipoPagam == codTipoPagamento)
                    {
                        stessoPagamento = true;
                        if (tipoBeneficio == "PL")
                        {
                            stessoPagamento = false;
                            Logger.LogWarning(null, $"Attenzione: Studente con cf {studenteDaControllare.codFiscale} ha già preso il premio di laurea!");
                        }
                        break;
                    }
                    importiPagati += pagamento.importoPagamento;
                }
                if (stessoPagamento)
                {
                    studentiDaRimuovere.Add(studenteDaControllare);
                    continue;
                }
                Math.Round(importiPagati, 2);
                studenteDaControllare.SetImportiPagati(importiPagati);
            }

            _ = listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);
            Logger.LogInfo(null, $"Rimossi {studentiDaRimuovere.Count} studenti dalla lista di pagamento");

            string lastValue = codTipoPagamento[3..];
            string firstPart = codTipoPagamento[..3];
            string integrazioneValue = codTipoPagamento.Substring(2, 1);
            if (integrazioneValue == "I")
            {
                isIntegrazione = true;
                Logger.LogInfo(null, "Il tipo di pagamento indica una integrazione");
            }
            if (lastValue != "0" && lastValue != "9" && lastValue != "6")
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


        private void ControlloIntegrazioni()
        {
            Logger.LogDebug(null, "Inizio del controllo delle integrazioni per gli studenti");
            List<Studente> studentiDaRimuovere = new();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                if (studente.pagamentiEffettuati == null || studente.pagamentiEffettuati.Count <= 0)
                {
                    studentiDaRimuovere.Add(studente);
                    continue;
                }
                bool pagamentoPossibile = false;
                foreach (Pagamento pagamento in studente.pagamentiEffettuati)
                {
                    string pagamentoBeneficio = pagamento.codTipoPagam[..2];
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
                        case "P1":
                        case "P2":
                            if (selectedTipoPagamento == "I0")
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "S0":
                        case "S1":
                        case "S2":
                            if (selectedTipoPagamento == "I9")
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                    }
                }
                if (!pagamentoPossibile)
                {
                    studentiDaRimuovere.Add(studente);
                }
            }
            int removedCount = studentiDaRimuovere.Count;
            _ = listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);
            Logger.LogInfo(null, $"Rimossi {removedCount} studenti dalla lista di pagamento dopo il controllo delle integrazioni");
        }

        private void ControlloRiemissioni(string firstPart, string lastValue)
        {
            Logger.LogDebug(null, "Inizio del controllo delle riemissioni per gli studenti"); // Start of method
            List<Studente> studentiDaRimuovere = new();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                if (studente.pagamentiEffettuati == null || studente.pagamentiEffettuati.Count <= 0)
                {
                    studentiDaRimuovere.Add(studente);
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
                    studentiDaRimuovere.Add(studente);
                }
            }

            int removedCount = studentiDaRimuovere.Count;
            _ = listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);
            Logger.LogInfo(null, $"Rimossi {removedCount} studenti dalla lista di pagamento dopo il controllo delle riemissioni");
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
                        Domanda.num_domanda in (
                            SELECT DISTINCT Num_domanda
                                FROM vMotivazioni_blocco_pagamenti
                                WHERE Anno_accademico = '{selectedAA}' 
                                    AND Data_fine_validita IS NULL 
                                    AND Blocco_pagamento_attivo = 1
                            )";

            SqlCommand readData = new(sqlKiller, conn, sqlTransaction);
            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili");
            List<Studente> listaStudentiDaEliminareBlocchi = new();
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null && !listaStudentiDaEliminareBlocchi.Contains(studente))
                    {
                        listaStudentiDaEliminareBlocchi.Add(studente);
                    }
                }
            }

            // Check for missing IBAN
            string sqlStudentiSenzaIBAN = $@" 
                SELECT DISTINCT
                    vMODALITA_PAGAMENTO.Cod_fiscale
                FROM
                    vMODALITA_PAGAMENTO 
                    INNER JOIN #CFEstrazione cfe ON vMODALITA_PAGAMENTO.Cod_fiscale = cfe.Cod_fiscale 
                WHERE 
                    IBAN = ''
             ";
            SqlCommand IBANDATA = new(sqlStudentiSenzaIBAN, conn, sqlTransaction);
            Logger.LogInfo(12, $"Lavorazione studenti - controllo IBAN");
            List<Studente> listaStudentiDaEliminareIBAN = new();
            using (SqlDataReader reader = IBANDATA.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null && !listaStudentiDaEliminareIBAN.Contains(studente))
                    {
                        listaStudentiDaEliminareIBAN.Add(studente);
                    }
                }
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
            SqlCommand nonVincitori = new(sqlStudentiNonVincitori, conn, sqlTransaction);
            Logger.LogInfo(12, $"Lavorazione studenti - controllo Vincitori");
            List<Studente> listaStudentiDaEliminareNonVincitori = new();
            using (SqlDataReader reader = nonVincitori.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null && !listaStudentiDaEliminareNonVincitori.Contains(studente))
                    {
                        listaStudentiDaEliminareNonVincitori.Add(studente);
                    }
                }
            }

            // Check for students with invalid PEC addresses
            string sqlStudentiPecKiller = $@"
                    SELECT LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, Indirizzo_PEC 
                    FROM LUOGO_REPERIBILITA_STUDENTE
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
            SqlCommand nonPecCmd = new(sqlStudentiPecKiller, conn, sqlTransaction);
            Logger.LogInfo(12, $"Lavorazione studenti - controllo PEC");
            List<Studente> listaStudentiDaEliminarePEC = new();
            using (SqlDataReader reader = nonPecCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null && !listaStudentiDaEliminarePEC.Contains(studente))
                    {
                        listaStudentiDaEliminarePEC.Add(studente);
                    }
                }
            }

            Logger.LogDebug(12, $"Numero studenti da eliminare per blocchi presenti in domanda = {listaStudentiDaEliminareBlocchi.Count}");
            Logger.LogDebug(12, $"Numero studenti da eliminare per IBAN mancante = {listaStudentiDaEliminareIBAN.Count}");
            Logger.LogDebug(12, $"Numero studenti da eliminare perché non più vincitori = {listaStudentiDaEliminareNonVincitori.Count}");
            Logger.LogDebug(12, $"Numero studenti da eliminare per PEC mancante = {listaStudentiDaEliminarePEC.Count}");

            _ = listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminareBlocchi.Contains);
            _ = listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminareIBAN.Contains);
            _ = listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminareNonVincitori.Contains);
            _ = listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminarePEC.Contains);

            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili - completato");
        }


        void ProcessStudentList()
        {
            if (listaStudentiDaPagare.Count == 0)
            {
                return;
            }
            Logger.LogInfo(30, $"Lavorazione studenti");
            List<string> codFiscali = listaStudentiDaPagare.Select(studente => studente.codFiscale).ToList();

            Logger.LogDebug(null, "Creazione tabella CF");
            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
            SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
            _ = createCmd.ExecuteNonQuery();

            Logger.LogDebug(null, "Inserimento in tabella CF dei codici fiscali");
            Logger.LogInfo(30, $"Lavorazione studenti - creazione tabella codici fiscali");
            for (int i = 0; i < codFiscali.Count; i += 1000)
            {
                var batch = codFiscali.Skip(i).Take(1000);
                var insertQuery = "INSERT INTO #CFEstrazione (Cod_fiscale) VALUES " + string.Join(", ", batch.Select(cf => $"('{cf}')"));
                SqlCommand insertCmd = new(insertQuery, conn, sqlTransaction);
                _ = insertCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Creazione index della tabella CF");
            string indexingCFTable = "CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale)";
            SqlCommand indexingCFTableCmd = new SqlCommand(indexingCFTable, conn, sqlTransaction);
            indexingCFTableCmd.ExecuteNonQuery();


            FilterPagamenti();

            Logger.LogDebug(30, $"Numero studenti prima della pulizia = {listaStudentiDaPagare.Count}");

            CheckLiquefazione();

            Logger.LogDebug(30, $"Numero studenti dopo la pulizia = {listaStudentiDaPagare.Count}");
            if (listaStudentiDaPagare.Count > 0)
            {
                PopulateStudentLuogoNascita();
                PopulateStudentResidenza();
                PopulateStudentInformation();
                if (tipoBeneficio == "BS" && !isTR)
                {
                    PopulateStudentDetrazioni();
                    if (!isIntegrazione)
                    {
                        PopulateStudentiAssegnazioni();
                    }
                }

                PopulateStudentiImpegni();

                PopulateImportoDaPagare();

                if (listaStudentiDaPagare.Count > 0)
                {
                    GenerateOutputFiles();
                }
            }

            string dropTempTable = "DROP TABLE #CFEstrazione;";
            SqlCommand dropCmd = new(dropTempTable, conn, sqlTransaction);
            _ = dropCmd.ExecuteNonQuery();
        }

        private void InsertIntoMovimentazioni(List<Studente> studentiDaProcessare, string impegno)
        {
            List<Studente> studentiSenzaImpegno = new();
            foreach (Studente studente in studentiDaProcessare)
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
                Dictionary<int, Studente> codMovimentiPerStudente = new();
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_GENERALI (ID_CAUSALE_MOVIMENTO_GENERALE, IMPORTO_MOVIMENTO, UTENTE_VALIDAZIONE, DATA_VALIDITA_MOVIMENTO_GENERALE, NOTE_VALIDAZIONE_MOVIMENTO, COD_MANDATO) VALUES ";
                string note = "Inserimento tramite elaborazione file pagamenti";

                if (studentiDaProcessare.Any())
                {
                    Studente firstStudent = studentiDaProcessare.First();
                    string firstStudentValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            codTipoPagamento,
                            firstStudent.importoDaPagare.ToString(CultureInfo.InvariantCulture),
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            note,
                            firstStudent.mandatoProvvisorio);
                    string initialInsertQuery = $"{baseSqlInsert} {firstStudentValues};";
                    using (SqlCommand cmd = new(initialInsertQuery, conn, sqlTransaction))
                    {
                        _ = cmd.ExecuteNonQuery();
                    }
                    string sqlCodMovimento = "SELECT TOP(1) CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI ORDER BY CODICE_MOVIMENTO DESC";
                    object? result;
                    using (SqlCommand cmdCM = new(sqlCodMovimento, conn, sqlTransaction))
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

                    foreach (Studente studente in batch)
                    {
                        string studenteValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            codTipoPagamento,
                            studente.importoDaPagare.ToString(CultureInfo.InvariantCulture),
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            note,
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
                        using SqlCommand cmd = new(finalQuery, conn, sqlTransaction);
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
        }
        private void InsertIntoStatiDelMovimentoContabile(Dictionary<int, Studente> codMovimentiPerStudente)
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
                    using SqlCommand cmd = new(finalQuery, conn, sqlTransaction);
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
        private void InsertIntoMovimentiContabiliElementariPagamenti(Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                string codMovimentoElementare = "00";
                string sqlCodMovimento = $"SELECT DISTINCT Cod_mov_contabile_elem FROM Decod_pagam_new where Cod_tipo_pagam_new = '{codTipoPagamento}'";
                SqlCommand cmdCM = new(sqlCodMovimento, conn, sqlTransaction);
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
                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {
                    int codMovimento = entry.Key;
                    string importoLordo = entry.Value.importoDaPagareLordo.ToString(CultureInfo.InvariantCulture);
                    int segno = 1;

                    // Assuming you might need some data from the Studente object, you can access it like this: entry.Value
                    string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '{codMovimentoElementare}', '{codMovimento}', '{importoLordo}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

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
                    using SqlCommand cmd = new(finalQuery, conn, sqlTransaction);
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
        private void InsertIntoMovimentiContabiliElementariDetrazioni(Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Detrazioni");
                const int batchSize = 1000; // Maximum number of rows per INSERT statement
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                StringBuilder finalQueryBuilder = new();

                List<string> batchStatements = new();
                int currentBatchSize = 0;

                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {

                    if (entry.Value.detrazioni == null || entry.Value.detrazioni.Count <= 0)
                    {
                        continue;
                    }

                    foreach (Detrazione detrazione in entry.Value.detrazioni)
                    {
                        if (!detrazione.daContabilizzare)
                        {
                            continue;
                        }
                        int codMovimento = entry.Key;
                        string importoDaDetrarre = detrazione.importo.ToString(CultureInfo.InvariantCulture);
                        int segno = 0;

                        // Assuming you might need some data from the Studente object, you can access it like this: entry.Value
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
                    using SqlCommand cmd = new(finalQuery, conn, sqlTransaction);
                    _ = cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                throw;
            }
        }
        private void InsertIntoMovimentiContabiliElementariAssegnazioni(Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Assegnazioni");
                const int batchSize = 1000; // Maximum number of rows per INSERT statement
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                StringBuilder finalQueryBuilder = new();

                List<string> batchStatements = new();
                int currentBatchSize = 0;

                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {

                    if (entry.Value.assegnazioni == null || entry.Value.assegnazioni.Count <= 0)
                    {
                        continue;
                    }

                    foreach (Assegnazione assegnazione in entry.Value.assegnazioni)
                    {
                        if (assegnazione.costoTotale <= 0)
                        {
                            continue;
                        }

                        int codMovimento = entry.Key;
                        string costoPostoAlloggio = assegnazione.costoTotale.ToString(CultureInfo.InvariantCulture);
                        int segno = 0;

                        // Assuming you might need some data from the Studente object, you can access it like this: entry.Value
                        string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '02', '{codMovimento}', '{costoPostoAlloggio}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last() && assegnazione == entry.Value.assegnazioni.Last()))
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
                    using SqlCommand cmd = new(finalQuery, conn, sqlTransaction);
                    _ = cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                throw;
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

            string currentBeneficio = isTR ? "TR" : tipoBeneficio;
            string currentCodTipoPagamento = currentBeneficio + selectedTipoPagamento;

            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{currentCodTipoPagamento}'";
            SqlCommand cmd = new(sqlTipoPagam, conn, sqlTransaction);
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();
            string beneficioFolderPath = Utilities.EnsureDirectory(Path.Combine(baseFolderPath, pagamentoDescrizione));

            bool doAllImpegni = selectedImpegno == "0000";
            IEnumerable<string> impegnoList = doAllImpegni ? impegniList : new List<string> { selectedImpegno };

            foreach (string impegno in impegnoList)
            {
                Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno}");
                string sqlImpegno = $"SELECT Descr FROM Impegni WHERE anno_accademico = '{selectedAA}' AND num_impegno = '{impegno}' AND categoria_pagamento = '{categoriaPagam}'";
                SqlCommand cmdImpegno = new(sqlImpegno, conn, sqlTransaction);
                string impegnoDescrizione = (string)cmdImpegno.ExecuteScalar();
                string currentFolder = Utilities.EnsureDirectory(Path.Combine(beneficioFolderPath, $"imp-{impegno}-{impegnoDescrizione}"));
                ProcessImpegno(impegno, currentFolder);
            }
            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Completato");
        }
        private void ProcessImpegno(string impegno, string currentFolder)
        {
            var groupedStudents = listaStudentiDaPagare
                .Where(s => s.numeroImpegno == impegno)
                .ToList();

            if (groupedStudents.Any())
            {
                GenerateOutputFilesPA(currentFolder, groupedStudents, impegno);
            }
        }
        private void GenerateOutputFilesPA(string currentFolder, List<Studente> studentsWithSameImpegno, string impegno)
        {
            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Senza detrazioni");
            var studentsWithPA = studentsWithSameImpegno
                .Where(s => (s.assegnazioni != null && s.assegnazioni.Count > 0) || (s.detrazioni != null && s.detrazioni.Count > 0 && s.detrazioni.Any(d => d.codReversale == "01" && d.daContabilizzare)))
                .ToList();
            var studentsWithoutPA = studentsWithSameImpegno
                .Where(s => (s.assegnazioni == null || s.assegnazioni.Count <= 0) && !(s.detrazioni != null && s.detrazioni.Count > 0 && s.detrazioni.Any(d => d.codReversale == "01" && d.daContabilizzare)))
                .ToList();
            impegnoAmount.Add(impegno, new Dictionary<string, int>());
            if (studentsWithoutPA.Count > 0 && (selectedRichiestoPA == "2" || selectedRichiestoPA == "0"))
            {
                impegnoAmount[impegno].Add("Senza detrazioni", studentsWithoutPA.Count);
                if (tipoStudente == "2")
                {
                    ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: true, processAnniSuccessivi: true, "SenzaDetrazioni", "00", impegno);
                    DataTable dataTableMatricole = GenerareExcelDataTableNoDetrazioni(studentsWithoutPA.Where(s => s.annoCorso == 1).ToList(), impegno);
                    DataTable dataTableASuccessivi = GenerareExcelDataTableNoDetrazioni(studentsWithoutPA.Where(s => s.annoCorso != 1).ToList(), impegno);
                    if (dataTableMatricole != null && dataTableMatricole.Rows.Count > 0)
                    {
                        Utilities.ExportDataTableToExcel(dataTableMatricole, currentFolder, false, "Matricole");
                    }
                    if (dataTableASuccessivi != null && dataTableASuccessivi.Rows.Count > 0)
                    {
                        Utilities.ExportDataTableToExcel(dataTableASuccessivi, currentFolder, false, "AnniSuccessivi");
                    }
                }
                else if (tipoStudente == "0")
                {
                    ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: true, processAnniSuccessivi: false, "SenzaDetrazioni", "00", impegno);
                }
                else if (tipoStudente == "1")
                {
                    ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: false, processAnniSuccessivi: true, "SenzaDetrazioni", "00", impegno);
                }
            }
            if (studentsWithPA.Count > 0 && (selectedRichiestoPA == "2" || selectedRichiestoPA == "1"))
            {
                string newFolderPath = Utilities.EnsureDirectory(Path.Combine(currentFolder, "CON DETRAZIONE"));
                ProcessStudentsByCodEnte(selectedCodEnte, studentsWithPA, newFolderPath, impegno);
                GenerateGiuliaFile(newFolderPath, studentsWithPA, impegno);
            }
        }
        private void ProcessStudentsByAnnoCorso(List<Studente> students, string folderPath, bool processMatricole, bool processAnniSuccessivi, string nomeFileInizio, string codEnteFlusso, string impegnoFlusso)
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
        private void ProcessStudentsByCodEnte(string selectedCodEnte, List<Studente> studentsWithPA, string newFolderPath, string impegno)
        {
            if (studentsWithPA.Count <= 0)
            {
                return;
            }
            bool allCodEnte = selectedCodEnte == "00";
            var codEnteList = allCodEnte ? studentsWithPA.Select(s => s.codEnte).Distinct() : new List<string> { selectedCodEnte };

            foreach (var codEnte in codEnteList)
            {

                var studentsInCodEnte = studentsWithPA.Where(s => s.codEnte == codEnte).ToList();
                var anniCorsi = studentsInCodEnte.Select(s => s.annoCorso).Distinct();

                string nomeCodEnte = "";
                string sqlCodEnte = $"SELECT Descrizione FROM Enti_di_gestione WHERE cod_ente = '{codEnte}'";
                SqlCommand cmdSede = new(sqlCodEnte, conn, sqlTransaction);
                nomeCodEnte = (string)cmdSede.ExecuteScalar();
                nomeCodEnte = Utilities.SanitizeColumnName(nomeCodEnte);
                Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Flusso con detrazioni ente: {nomeCodEnte}");
                string specificFolderPath = Utilities.EnsureDirectory(Path.Combine(newFolderPath, $"{nomeCodEnte}"));
                impegnoAmount[impegno].Add(nomeCodEnte, studentsInCodEnte.Count);
                if (tipoStudente == "2")
                {
                    ProcessStudentsByAnnoCorso(studentsInCodEnte, specificFolderPath, processMatricole: true, processAnniSuccessivi: true, "Con Detrazioni_" + nomeCodEnte, codEnte, impegno);
                }
                else
                {
                    bool processMatricole = tipoStudente == "0";
                    ProcessStudentsByAnnoCorso(studentsInCodEnte, specificFolderPath, processMatricole: processMatricole, processAnniSuccessivi: !processMatricole, "Con Detrazioni_" + nomeCodEnte, codEnte, impegno);
                }
            }
            List<string> sediStudi = codEnteList.ToList();
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
        private void ProcessAndWriteStudents(List<Studente> students, string folderPath, string fileName, string codEnteFlusso, string impegnoFlusso)
        {
            if (students.Any())
            {
                DataTable dataTableFlusso = GenerareFlussoDataTable(students, codEnteFlusso);
                if (dataTableFlusso != null && dataTableFlusso.Rows.Count > 0)
                {
                    Utilities.WriteDataTableToTextFile(dataTableFlusso, folderPath, $"flusso_{fileName}_{impegnoFlusso}");
                }
                InsertIntoMovimentazioni(students, impegnoFlusso);
                studentiProcessatiAmount += students.Count;
            }
        }

        private static void GenerateGiuliaFile(string newFolderPath, List<Studente> studentsWithPA, string impegno)
        {
            DataTable dataTable = GenerareGiuliaDataTable(studentsWithPA, impegno);
            if (dataTable.Rows.Count > 0)
            {
                Utilities.ExportDataTableToExcel(dataTable, newFolderPath, false, "Dettaglio PA");
            }
        }
        private static DataTable GenerareGiuliaDataTable(List<Studente> studentsWithPA, string impegno)
        {
            Logger.LogInfo(60, $"Lavorazione studenti - impegno n°{impegno} - Generazione Dettaglio PA");
            int progressivo = 1;
            DataTable returnDataTable = new();

            _ = returnDataTable.Columns.Add("1");
            _ = returnDataTable.Columns.Add("2");
            _ = returnDataTable.Columns.Add("3");
            _ = returnDataTable.Columns.Add("4");
            _ = returnDataTable.Columns.Add("5");
            _ = returnDataTable.Columns.Add("6");
            _ = returnDataTable.Columns.Add("7");
            _ = returnDataTable.Columns.Add("8");

            foreach (Studente studente in studentsWithPA)
            {
                if (studente.assegnazioni == null || studente.assegnazioni.Count <= 0)
                {
                    continue;
                }

                _ = returnDataTable.Rows.Add(progressivo, studente.cognome, studente.nome, studente.codFiscale);
                _ = returnDataTable.Rows.Add(" ");
                _ = returnDataTable.Rows.Add(" ", "Costo periodo", "Totale parziale", "Residenza", "Tipo stanza", "Data decorrenza", "Data fine assegnazione", "Num giorni");
                double costoServizioPA = 0;
                double costoPA = 0;
                double accontoPA = 0;
                foreach (Assegnazione assegnazione in studente.assegnazioni)
                {
                    double roundedCostoServizioPA = 0;
                    costoPA = Math.Round(assegnazione.costoTotale, 2);
                    accontoPA = Math.Round(studente.importoAccontoPA, 2);
                    costoServizioPA += costoPA;
                    roundedCostoServizioPA = Math.Round(costoServizioPA, 2);
                    _ = returnDataTable.Rows.Add(" ", costoPA, roundedCostoServizioPA, assegnazione.codPensionato, assegnazione.codTipoStanza, assegnazione.dataDecorrenza, assegnazione.dataFineAssegnazione, (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days);
                }
                costoServizioPA = Math.Round(costoServizioPA, 2);
                _ = returnDataTable.Rows.Add(" ");
                _ = returnDataTable.Rows.Add(" ", studente.cognome, studente.nome);
                _ = returnDataTable.Rows.Add(" ", "Importo borsa totale", studente.importoBeneficio);
                _ = returnDataTable.Rows.Add(" ", "Costo servizio PA", costoServizioPA);
                _ = returnDataTable.Rows.Add(" ", "Acconto PA", studente.importoAccontoPA);
                _ = returnDataTable.Rows.Add(" ", "Saldo PA", Math.Round(costoServizioPA - studente.importoAccontoPA, 2));
                _ = returnDataTable.Rows.Add(" ");
                _ = returnDataTable.Rows.Add(" ", "Saldo", $"Lordo = {Math.Round(studente.importoDaPagareLordo)}", $"Ritenuta = {Math.Round(costoServizioPA - accontoPA)}", $"Netto = {Math.Round(studente.importoDaPagareLordo - (costoServizioPA - accontoPA))}");
                _ = returnDataTable.Rows.Add(" ");
                _ = returnDataTable.Rows.Add(" ");
                progressivo++;
            }

            return returnDataTable;
        }

        void PopulateStudentLuogoNascita()
        {
            string dataQuery = @"
                SELECT *
                FROM Studente 
                LEFT OUTER JOIN Comuni ON Studente.Cod_comune_nasc = Comuni.Cod_comune 
                INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(30, $"Lavorazione studenti - inserimento in luogo nascita");
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
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
                    SELECT vResidenza.* 
                    FROM vResidenza 
                    INNER JOIN #CFEstrazione cfe ON vResidenza.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE ANNO_ACCADEMICO = '{selectedAA}'";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(35, $"Lavorazione studenti - inserimento in residenza");
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
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
        void PopulateStudentInformation()
        {
            string dataQuery = @"
                    SELECT * 
                    FROM Studente LEFT OUTER JOIN 
                    vModalita_pagamento ON studente.cod_fiscale = vmodalita_pagamento.cod_fiscale
                    INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale 
                    ";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(40, $"Lavorazione studenti - inserimento in informazioni");
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    studente?.SetInformations(
                            long.TryParse(Regex.Replace(Utilities.SafeGetString(reader, "telefono_cellulare"), @"[^\d]", ""), out long telefonoNumber) ? telefonoNumber : 0,
                            Utilities.SafeGetString(reader, "indirizzo_e_mail"),
                            Utilities.SafeGetString(reader, "IBAN"),
                            Utilities.SafeGetString(reader, "swift")
                        );
                }
            }
            Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento in informazioni - completato");
        }
        void PopulateStudentDetrazioni()
        {
            string dataQuery = $@"
                    SELECT Domanda.Cod_fiscale, Reversali.*, (SELECT DISTINCT cod_tipo_pagam_new FROM Decod_pagam_new where Cod_tipo_pagam_old = Reversali.Cod_tipo_pagam OR Cod_tipo_pagam_new = Reversali.Cod_tipo_pagam) AS cod_tipo_pagam_new
                    FROM Domanda 
                    INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    INNER JOIN Reversali ON Domanda.num_domanda = Reversali.num_domanda AND Domanda.Anno_accademico = Reversali.Anno_accademico
                    WHERE Reversali.Ritirato_azienda = 0 AND Domanda.Anno_accademico = '{selectedAA}'
                    ";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(45, $"Lavorazione studenti - inserimento in detrazioni");
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    studente?.AddDetrazione(
                                Utilities.SafeGetString(reader, "cod_reversale"),
                                double.TryParse(Utilities.SafeGetString(reader, "importo"), out double importo) ? importo : 0,
                                Utilities.SafeGetString(reader, "Note"),
                                Utilities.SafeGetString(reader, "cod_tipo_pagam"),
                                Utilities.SafeGetString(reader, "cod_tipo_pagam_new"),
                                false
                            );
                }
            }
            Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento in detrazioni - completato");
        }
        void PopulateStudentiAssegnazioni()
        {

            string dateQuery = $@"
                    SELECT min_data_PA, max_data_PA , detrazione_PA, detrazione_PA_fuori_corso
                    FROM DatiGenerali_con 
                    WHERE Anno_accademico = '{selectedAA}'";

            DateTime min_data_PA = new(1990, 01, 01);
            DateTime max_data_PA = new(2999, 01, 01);
            double detrazione_PA = 0;
            double detrazione_PA_fuori_corso = 0;

            SqlCommand readDate = new(dateQuery, conn, sqlTransaction);
            using (SqlDataReader reader = readDate.ExecuteReader())
            {
                while (reader.Read())
                {
                    DateTime.TryParse(Utilities.SafeGetString(reader, "min_data_PA"), out min_data_PA);
                    DateTime.TryParse(Utilities.SafeGetString(reader, "max_data_PA"), out max_data_PA);
                    double.TryParse(Utilities.SafeGetString(reader, "detrazione_PA"), out detrazione_PA);
                    double.TryParse(Utilities.SafeGetString(reader, "detrazione_PA_fuori_corso"), out detrazione_PA_fuori_corso);
                }
            }

            string dataQuery = $@"
                    SELECT DISTINCT     
                        Assegnazione_PA.Cod_fiscale,
                        Assegnazione_PA.Cod_Pensionato, 
                        Assegnazione_PA.Cod_Stanza, 
                        Assegnazione_PA.Data_Decorrenza, 
                        Assegnazione_PA.Data_Fine_Assegnazione, 
                        Assegnazione_PA.Cod_Fine_Assegnazione,
                        Costo_Servizio.Tipo_stanza, 
                        Costo_Servizio.Importo as importo_mensile,
	                    Assegnazione_PA.id_assegnazione_pa
                    FROM            
	                    Assegnazione_PA INNER JOIN
	                    Stanza ON Assegnazione_PA.Cod_Stanza = Stanza.Cod_Stanza AND Assegnazione_PA.Cod_Pensionato = Stanza.Cod_Pensionato INNER JOIN
	                    Costo_Servizio ON Stanza.Tipo_Stanza = Costo_Servizio.Tipo_stanza AND Assegnazione_PA.Anno_Accademico = Costo_Servizio.Anno_accademico AND Assegnazione_PA.Cod_Pensionato = Costo_Servizio.Cod_pensionato
                        INNER JOIN #CFEstrazione cfe ON Assegnazione_PA.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE        
	                    (Assegnazione_PA.Anno_Accademico = '{selectedAA}') AND 
	                    (Assegnazione_PA.Cod_movimento = '01') AND 
	                    (Assegnazione_PA.Ind_Assegnazione = 1) AND 
	                    (Assegnazione_PA.Status_Assegnazione = 0) AND
	                    Costo_Servizio.Cod_periodo = 'M' 
                    ORDER BY Assegnazione_PA.id_assegnazione_pa
                    ";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(50, $"Lavorazione studenti - inserimento in assegnazioni");
            HashSet<string> processedFiscalCodes = new();

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);

                    if (studente == null || Utilities.SafeGetString(reader, "Cod_Stanza") == "XXX")
                    {
                        continue;
                    }

                    bool studenteFuoriCorso = studente.annoCorso == -1;
                    bool studenteDisabileFuoriCorso = (studente.annoCorso == -2 || studente.annoCorso == -1) && studente.disabile;

                    if (categoriaPagam == "PR" && !processedFiscalCodes.Contains(codFiscale))
                    {
                        if (!studente.vincitorePA)
                        {
                            continue;
                        }

                        if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                        {
                            bool detrazioneAcconto = false;
                            foreach (Detrazione detrazione in studente.detrazioni)
                            {
                                if (detrazione.codReversale == "01")
                                {
                                    detrazioneAcconto = true;
                                }
                            }

                            if (detrazioneAcconto)
                            {
                                continue;
                            }
                        }
                        else if ((studente.detrazioni == null || studente.detrazioni.Count <= 0) && isRiemissione)
                        {
                            continue;
                        }

                        if (studente.annoCorso > 0)
                        {
                            studente.AddDetrazione("01", detrazione_PA, "Detrazione acconto PA", "01", "BSP0", true);
                        }
                        else if (studenteFuoriCorso || studenteDisabileFuoriCorso)
                        {
                            studente.AddDetrazione("01", detrazione_PA_fuori_corso, "Detrazione acconto PA", "01", "BSP0", true);
                        }
                        _ = processedFiscalCodes.Add(codFiscale);

                    }
                    else if (categoriaPagam == "SA")
                    {

                        AssegnazioneDataCheck result = studente.AddAssegnazione(
                                 Utilities.SafeGetString(reader, "cod_pensionato").Trim(),
                                 Utilities.SafeGetString(reader, "cod_stanza").Trim(),
                                 DateTime.Parse(Utilities.SafeGetString(reader, "data_decorrenza").Trim()),
                                 DateTime.TryParse(Utilities.SafeGetString(reader, "data_fine_assegnazione").Trim(), out DateTime date) ? date : max_data_PA,
                                 Utilities.SafeGetString(reader, "Cod_fine_assegnazione").Trim(),
                                 Utilities.SafeGetString(reader, "tipo_stanza").Trim(),
                                 double.TryParse(Utilities.SafeGetString(reader, "importo_mensile").Trim(), out double importoMensile) ? importoMensile : 0,
                                 min_data_PA,
                                 max_data_PA,
                                 studenteFuoriCorso || studenteDisabileFuoriCorso
                             );

                        string message = "";
                        switch (result)
                        {
                            case AssegnazioneDataCheck.Eccessivo:
                                message = "Assegnazione posto alloggio superiore alle mensilità possibili (10 mesi)";
                                break;
                            case AssegnazioneDataCheck.Incorretto:
                                message = "Assegnazione posto alloggio con data fine decorrenza minore della data di entrata";
                                break;
                            case AssegnazioneDataCheck.DataUguale:
                                message = "Assegnazione posto alloggio con data decorrenza e fine assegnazione uguali";
                                break;
                            case AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin:
                                message = "Assegnazione posto alloggio con data decorrenza minore del minimo previsto dal bando";
                                break;
                            case AssegnazioneDataCheck.DataFineAssMaggioreMax:
                                message = "Assegnazione posto alloggio con data fine assegnazione maggiore del massimo previsto dal bando";
                                break;
                        }
                        if (result == AssegnazioneDataCheck.Corretto)
                        {
                            continue;
                        }
                        // Log any issues with the assignment data checks
                        else
                        {
                            Logger.LogDebug(null, $"Errore nell'assegnazione dello studente {codFiscale}: {result}");
                        }
                        if (!studentiConErroriPA.TryGetValue(studente, out List<string>? value))
                        {
                            value = new List<string>();
                            studentiConErroriPA.Add(studente, value);
                        }

                        value.Add(message);
                    }
                }
            }

            foreach (Studente studente in studentiConErroriPA.Keys)
            {
                if (studente.assegnazioni.Count == 1 && studente.vincitorePA)
                {
                    Assegnazione vecchiaAssegnazione = studente.assegnazioni[0];
                    if (vecchiaAssegnazione.statoCorrettezzaAssegnazione != AssegnazioneDataCheck.DataUguale)
                    {
                        continue;
                    }

                    bool studenteFuoriCorso = studente.annoCorso == -1;
                    bool studenteDisabileFuoriCorso = (studente.annoCorso == -2 || studente.annoCorso == -1) && studente.disabile;

                    _ = studente.AddAssegnazione(
                        vecchiaAssegnazione.codPensionato,
                        vecchiaAssegnazione.codStanza,
                        vecchiaAssegnazione.dataDecorrenza,
                        max_data_PA,
                        vecchiaAssegnazione.codFineAssegnazione,
                        vecchiaAssegnazione.codTipoStanza,
                        vecchiaAssegnazione.costoMensile,
                        min_data_PA,
                        max_data_PA,
                        studenteFuoriCorso || studenteDisabileFuoriCorso
                        );
                    studente.assegnazioni.RemoveAt(0);
                }
            }
            Logger.LogInfo(50, $"UPDATE:Lavorazione studenti - inserimento in assegnazioni - completato");
        }
        void PopulateImportoDaPagare()
        {
            Logger.LogInfo(55, $"Lavorazione studenti - Calcolo importi");
            List<Studente> studentiDaRimuovereDalPagamento = new();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                if (studente.codFiscale == "DTTSLV00S48H501J")
                {
                    string test = "";
                }
                double importoDaPagare = studente.importoBeneficio;
                double importoMassimo = studente.importoBeneficio;

                double importoPA = 0;
                double accontoPA = 0;

                if (tipoBeneficio == "BS" && studente.assegnazioni != null && studente.assegnazioni.Count > 0)
                {
                    foreach (Assegnazione assegnazione in studente.assegnazioni)
                    {
                        importoPA += Math.Max(assegnazione.costoTotale, 0);
                    }
                }
                if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                {
                    foreach (Detrazione detrazione in studente.detrazioni)
                    {
                        if (detrazione.codReversale == "01" && tipoBeneficio == "BS")
                        {
                            accontoPA += detrazione.importo;
                            studente.SetImportoAccontoPA(Math.Round(accontoPA, 2));
                        }
                    }
                    if (categoriaPagam == "PR")
                    {
                        importoPA = accontoPA;
                    }
                    else
                    {
                        importoPA -= accontoPA;
                    }
                }
                studente.SetImportoSaldoPA(Math.Round(importoPA, 2));

                double importiPagati = isTR ? 0 : studente.importoPagato;


                if (categoriaPagam == "PR" && tipoBeneficio == "BS" && !isTR)
                {
                    string currentYear = selectedAA[..4];
                    DateTime percentDate = new(int.Parse(currentYear), 11, 10);
                    if (DateTime.Parse(selectedDataRiferimento) <= percentDate && studente.annoCorso == 1 && (studente.tipoCorso == 3 || studente.tipoCorso == 4))
                    {
                        importoDaPagare = importoMassimo * 0.20;
                    }
                    else if (DateTime.Parse(selectedDataRiferimento) <= percentDate)
                    {
                        importoDaPagare = 0;
                        listaStudentiDaNonPagare.Add(studente);
                        studentiDaRimuovereDalPagamento.Add(studente);
                    }
                    else
                    {
                        importoDaPagare = importoMassimo * 0.5;
                    }
                }
                else if (tipoBeneficio == "BS" && !isTR)
                {
                    importoDaPagare = importoMassimo;
                    if (studente.annoCorso == 1)
                    {
                        if (!studente.superamentoEsami && studente.superamentoEsamiTassaRegionale)
                        {
                            importoDaPagare = importoMassimo * 0.5;
                        }
                        else if (!studente.superamentoEsami && !studente.superamentoEsamiTassaRegionale)
                        {
                            importoDaPagare = 0;
                            listaStudentiDaNonPagare.Add(studente);
                            studentiDaRimuovereDalPagamento.Add(studente);
                        }
                    }
                }
                else if (isTR)
                {
                    importoDaPagare = 140;

                    if (studente.disabile)
                    {
                        importoDaPagare = 0;
                        listaStudentiDaNonPagare.Add(studente);
                        studentiDaRimuovereDalPagamento.Add(studente);
                    }

                    if (studente.tipoCorso == 6 || studente.tipoCorso == 7)
                    {
                        importoDaPagare = 0;
                        listaStudentiDaNonPagare.Add(studente);
                        studentiDaRimuovereDalPagamento.Add(studente);
                    }

                    if (!studente.superamentoEsami && !studente.superamentoEsamiTassaRegionale && studente.annoCorso == 1)
                    {
                        importoDaPagare = 0;
                        listaStudentiDaNonPagare.Add(studente);
                        studentiDaRimuovereDalPagamento.Add(studente);
                    }
                }

                double importoReversali = 0;
                if (studente.detrazioni != null && studente.detrazioni.Count > 0)
                {
                    foreach (Detrazione detrazione in studente.detrazioni)
                    {
                        if (detrazione.codReversale == "01" && tipoBeneficio == "BS")
                        {
                            continue;
                        }
                        importoReversali += detrazione.importo;
                    }
                }

                studente.SetImportoDaPagareLordo(Math.Round(importoDaPagare - importiPagati, 2));
                importoDaPagare = importoDaPagare - (importiPagati + importoPA + importoReversali);
                importoDaPagare = Math.Round(importoDaPagare, 2);

                if ((importoDaPagare == 0 || Math.Abs(importoDaPagare) < 5) && !studentiDaRimuovereDalPagamento.Contains(studente))
                {
                    studentiDaRimuovereDalPagamento.Add(studente);
                }

                studente.SetImportoDaPagare(importoDaPagare);
                if (isRiemissione)
                {
                    studente.SetImportoDaPagareLordo(importoDaPagare);
                    studente.RemoveAllAssegnazioni();
                }
            }

            _ = listaStudentiDaPagare.RemoveAll(studentiDaRimuovereDalPagamento.Contains);
            Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato");
        }
        void PopulateStudentiImpegni()
        {
            if (isTR)
            {
                foreach (Studente studente in listaStudentiDaPagare)
                {
                    studente.SetImpegno("3120");
                }
                Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento impegni - completato");
                return;
            }

            string dataQuery = $@"
                    SELECT Specifiche_impegni.Cod_fiscale, num_impegno_primaRata, num_impegno_saldo
                    FROM Specifiche_impegni
                        INNER JOIN #CFEstrazione cfe ON Specifiche_impegni.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE 
                        tipo_beneficio = '{tipoBeneficio}' AND
                        Anno_accademico = '{selectedAA}'
                    ";

            SqlCommand readData = new(dataQuery, conn, sqlTransaction);
            Logger.LogInfo(45, $"Lavorazione studenti - inserimento impegni");
            List<Studente> studentiSenzaImpegno = new();
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string impegnoToSet = "";
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    Studente? studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (categoriaPagam == "PR")
                        {
                            impegnoToSet = Utilities.SafeGetString(reader, "num_impegno_primaRata");
                        }
                        else
                        {
                            impegnoToSet = Utilities.SafeGetString(reader, "num_impegno_saldo");
                        }

                        if (string.IsNullOrWhiteSpace(impegnoToSet))
                        {
                            studentiSenzaImpegno.Add(studente);
                            continue;
                        }

                        studente.SetImpegno(impegnoToSet);
                    }
                }
            }
            if (studentiSenzaImpegno.Any())
            {
                Logger.Log(45, $"Trovati {studentiSenzaImpegno.Count} studenti senza impegno", LogLevel.WARN);
                DataTable studentiSenzaImpegnoTable = GenerateDataTableFromList(studentiSenzaImpegno);
                Utilities.WriteDataTableToTextFile(studentiSenzaImpegnoTable, selectedSaveFolder, "Studenti Senza Impegno");
            }
            // _ = listaStudentiDaPagare.RemoveAll(studentiSenzaImpegno.Contains);

            Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento impegni - completato");
        }

        private DataTable GenerareFlussoDataTable(List<Studente> studentiDaGenerare, string codEnteFlusso)
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


            foreach (Studente studente in studentiDaGenerare)
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
                string dataSenzaSlash = studente.dataNascita.ToString("ddMMyyyy");

                _ = studentsData.Rows.Add(
                    incremental,
                    studente.codFiscale,
                    studente.cognome,
                    studente.nome,
                    studente.importoDaPagareLordo,
                    studente.importoSaldoPA == 0 ? studente.importoAccontoPA : studente.importoSaldoPA,
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
        private static DataTable GenerateDataTableFromList(List<Studente> studentiSenzaImpegno)
        {
            DataTable table = new();
            _ = table.Columns.Add("Codice Fiscale", typeof(string));
            _ = table.Columns.Add("Num Domanda", typeof(string));
            foreach (var student in studentiSenzaImpegno)
            {
                DataRow row = table.NewRow();
                row["Codice Fiscale"] = student.codFiscale;
                row["Num Domanda"] = student.numDomanda;
                table.Rows.Add(row);
            }

            return table;
        }

        private DataTable GenerareExcelDataTableConDetrazioni(List<Studente> studentiDaGenerare, List<string> sediStudi, string impegno)
        {
            if (!studentiDaGenerare.Any())
            {
                return new DataTable();
            }
            DataTable studentsData = new();
            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            SqlCommand cmd = new(sqlTipoPagam, conn, sqlTransaction);
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

            _ = studentsData.Rows.Add(titolo);
            _ = studentsData.Rows.Add("ALLEGATO DETERMINA");

            foreach (string codEnte in sediStudi)
            {
                string sqlCodEnte = $"SELECT descrizione FROM Enti_di_gestione WHERE cod_ente = '{codEnte}'";
                SqlCommand cmdSede = new(sqlCodEnte, conn, sqlTransaction);
                string nomeCodEnte = (string)cmdSede.ExecuteScalar();


                string nomePA = categoriaPagam == "PR" ? "ACCONTO PA" : "SALDO COSTO DEL SERVIZIO";

                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(nomeCodEnte);
                _ = studentsData.Rows.Add("N.PROG.", "CODICE_FISCALE", "COGNOME", "NOME", "TOTALE LORDO", nomePA, "IMPORTO NETTO");

                int progressivo = 1;
                double totaleLordo = 0;
                double totalePA = 0;
                double totaleNetto = 0;
                foreach (Studente s in studentiDaGenerare)
                {
                    if (s.codEnte != codEnte)
                    {
                        continue;
                    }

                    double costoPA = categoriaPagam == "PR" ? s.importoAccontoPA : s.importoSaldoPA;

                    _ = studentsData.Rows.Add(progressivo, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), costoPA.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                    totaleLordo += s.importoDaPagareLordo;
                    totalePA += costoPA;
                    totaleNetto += s.importoDaPagare;
                    progressivo++;
                }
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totalePA, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
            }


            return studentsData;
        }
        private DataTable GenerareExcelDataTableNoDetrazioni(List<Studente> studentiDaGenerare, string impegno)
        {
            if (!studentiDaGenerare.Any())
            {
                return new DataTable();
            }
            DataTable studentsData = new();
            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            SqlCommand cmd = new(sqlTipoPagam, conn, sqlTransaction);
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

            _ = studentsData.Rows.Add(titolo);
            _ = studentsData.Rows.Add("ALLEGATO DETERMINA");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add("N.PROG.", "CODICE_FISCALE", "COGNOME", "NOME", "TOTALE LORDO", "ACCONTO PA", "IMPORTO NETTO");

            int progressivo = 1;
            double totaleLordo = 0;
            double totaleAcconto = 0;
            double totaleNetto = 0;
            foreach (Studente s in studentiDaGenerare)
            {
                _ = studentsData.Rows.Add(progressivo, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), s.importoAccontoPA.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                totaleLordo += s.importoDaPagareLordo;
                totaleAcconto += s.importoAccontoPA;
                totaleNetto += s.importoDaPagare;
                progressivo++;
            }
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totaleAcconto, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            return studentsData;
        }

    }
}
