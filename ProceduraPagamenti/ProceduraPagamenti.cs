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
        string selectedSaveFolder;
        string selectedAA;

        string selectedCodEnte;
        string selectedDataRiferimento;
        string selectedNumeroMandato;
        string selectedVecchioMandato;
        string selectedTipoProcedura;
        string selectedTipoPagamento;
        string dbTableName;
        bool dbTableExists;
        string tipoStudente;
        string tipoBeneficio;
        string codTipoPagamento;
        string selectedImpegno;
        string categoriaPagam;
        bool isIntegrazione = false;
        bool isRiemissione = false;

        bool usingFiltroManuale = false;

        List<Studente> listaStudentiDaPagare = new List<Studente>();
        Dictionary<Studente, List<string>> studentiConErroriPA = new Dictionary<Studente, List<string>>();
        List<Studente> listaStudentiDaNonPagare = new List<Studente>();

        List<string> impegniList = new List<string>();

        Dictionary<string, string> dictQueryWhere;
        string stringQueryWhere;
        bool usingStringWhere = false;

        bool isTR = false;

        public ProceduraPagamenti(IProgress<(int, string)> progress, MainUI mainUI, string connection_string) : base(progress, mainUI, connection_string) { }

        public override void RunProcedure(ArgsPagamenti args)
        {
            bool exitProcedureEarly = false;
            _mainForm.inProcedure = true;
            selectedSaveFolder = args._selectedSaveFolder;
            selectedAA = args._annoAccademico;
            selectedDataRiferimento = args._dataRiferimento;
            selectedNumeroMandato = args._numeroMandato;
            selectedTipoProcedura = args._tipoProcedura;
            selectedVecchioMandato = args._vecchioMandato;
            categoriaPagam = "";
            selectedCodEnte = "00";
            usingFiltroManuale = args._filtroManuale;

            using SqlConnection conn = new(CONNECTION_STRING);
            conn.Open();

            RiepilogoArguments.Instance.annoAccademico = selectedAA;
            RiepilogoArguments.Instance.dataRiferimento = selectedDataRiferimento;
            RiepilogoArguments.Instance.cartellaSalvataggio = selectedSaveFolder;
            RiepilogoArguments.Instance.numMandato = selectedNumeroMandato;
            RiepilogoArguments.Instance.numMandatoOld = selectedVecchioMandato;

            _mainForm.Invoke((MethodInvoker)delegate
            {
                using (SelectTipoPagam selectTipoPagam = new SelectTipoPagam(conn))
                {
                    selectTipoPagam.StartPosition = FormStartPosition.CenterParent;
                    DialogResult result = selectTipoPagam.ShowDialog(_mainForm);

                    if (result == DialogResult.OK)
                    {
                        selectedTipoPagamento = selectTipoPagam.SelectedCodPagamento;
                        tipoBeneficio = selectTipoPagam.SelectedTipoBeneficio;
                        if (tipoBeneficio == "TR")
                        {
                            tipoBeneficio = "BS";
                            isTR = true;
                        }
                        categoriaPagam = selectTipoPagam.SelectedCategoriaBeneficio;
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        exitProcedureEarly = true;
                    }
                }
            });

            if (exitProcedureEarly)
            {
                _mainForm.inProcedure = false;
                return;
            }
            codTipoPagamento = tipoBeneficio + selectedTipoPagamento;

            if (string.IsNullOrWhiteSpace(selectedTipoPagamento) || string.IsNullOrWhiteSpace(tipoBeneficio))
            {
                _mainForm.inProcedure = false;
                return;
            }
            _mainForm.Invoke((MethodInvoker)delegate
            {
                string currentTipoBeneficio = isTR ? "TR" : tipoBeneficio;
                using (SelectPagamentoSettings selectPagamentoSettings = new SelectPagamentoSettings(conn, selectedAA, currentTipoBeneficio, categoriaPagam))
                {
                    selectPagamentoSettings.StartPosition = FormStartPosition.CenterParent;
                    DialogResult dialogResult = selectPagamentoSettings.ShowDialog(_mainForm);
                    if (dialogResult == DialogResult.OK)
                    {
                        selectedCodEnte = selectPagamentoSettings.InputCodEnte.inputVar;
                        tipoStudente = selectPagamentoSettings.InputTipoStud.inputVar;
                        selectedImpegno = selectPagamentoSettings.InputImpegno.inputVar;
                        impegniList = selectPagamentoSettings.InputImpegno.inputList;
                    }
                    else if (dialogResult == DialogResult.Cancel)
                    {
                        exitProcedureEarly = true;
                    }
                }
            });
            if (exitProcedureEarly)
            {
                _mainForm.inProcedure = false;
                return;
            }
            _mainForm.Invoke((MethodInvoker)delegate
            {
                using (SelectTableName selectTableName = new SelectTableName())
                {
                    selectTableName.StartPosition = FormStartPosition.CenterParent;
                    DialogResult result = selectTableName.ShowDialog(_mainForm);

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
            });
            if (exitProcedureEarly)
            {
                _mainForm.inProcedure = false;
                return;
            }

            _mainForm.Invoke((MethodInvoker)delegate
            {
                using (RiepilogoPagamenti riepilogo = new RiepilogoPagamenti(RiepilogoArguments.Instance))
                {
                    riepilogo.StartPosition = FormStartPosition.CenterParent;
                    DialogResult result = riepilogo.ShowDialog(_mainForm);

                    if (result == DialogResult.Cancel)
                    {
                        exitProcedureEarly = true;
                    }

                }
            });

            if (exitProcedureEarly)
            {
                _mainForm.inProcedure = false;
                return;
            }

            using (SqlCommand command = new SqlCommand($"SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = N'{dbTableName}'", conn))
            {
                dbTableExists = command.ExecuteScalar() != null;
            }

            if (selectedTipoProcedura == "0" || selectedTipoProcedura == "2")
            {
                CreateDBTable(conn);
            }

            if (selectedTipoProcedura == "2" || (selectedTipoProcedura == "1" && !dbTableExists))
            {
                _progress.Report((100, "Fine lavorazione"));
                _mainForm.inProcedure = false;
                return;
            }

            if (usingFiltroManuale)
            {
                _mainForm.Invoke((MethodInvoker)delegate
                {
                    using (FiltroManuale selectFiltroManuale = new FiltroManuale(conn, dbTableName))
                    {
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
                    }
                });
            }

            if (exitProcedureEarly)
            {
                _mainForm.inProcedure = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedVecchioMandato))
            {
                ClearMovimentiContabili(conn);
            }

            GenerateStudentListToPay(conn);

            ProcessStudentList();
            _progress.Report((100, $"Lavorazione studenti - Fine lavorazione"));
            _mainForm.inProcedure = false;

        }
        void CreateDBTable(SqlConnection conn)
        {
            _progress.Report((1, $"Creazione Tabella: {dbTableName}"));
            ProgressUpdater progressUpdater = new ProgressUpdater(_progress, 1);
            progressUpdater.StartUpdating();
            StringBuilder queryBuilder = new StringBuilder();

            if (dbTableExists)
            {
                queryBuilder.Append($@" TRUNCATE TABLE {dbTableName};");
            }
            else
            {
                queryBuilder.Append($@"
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
                                            imp_pagato DECIMAL(8,2),
                                            liquidabile CHAR(1),
                                            note VARCHAR(MAX),
                                            togliere_loreto VARCHAR(4),
                                            togliere_PEC CHAR(1)
                                        );");
            }

            queryBuilder.Append($@"         DECLARE @maxDataValidita DATETIME
                                            DECLARE @annoAccademico VARCHAR(8)
                                            DECLARE @codBeneficio CHAR(2)
                                            SET @annoAccademico = '{selectedAA}'
                                            SET @maxDataValidita = '{selectedDataRiferimento}'
                                            SET @codBeneficio = '{tipoBeneficio}';");

            queryBuilder.Append(PagamentiSettings.SQLTabellaAppoggio);
            queryBuilder.Append($@"
                                            ,PagamentiTotali
                                            AS
                                            (
                                                SELECT
                                                 Anno_accademico
                                                ,Num_domanda
                                                ,SUM(Imp_pagato) as Imp_pagato
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
                                                ,PagamentiTotali.Imp_pagato
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

            SqlCommand cmd = new(sqlQuery, conn)
            {
                CommandTimeout = 90000000
            };
            cmd.ExecuteNonQuery();
            progressUpdater.StopUpdating();
            _progress.Report((1, "Fine creazione tabella d'appoggio"));
        }
        void ClearMovimentiContabili(SqlConnection conn)
        {
            _progress.Report((10, "Pulizia da movimenti contabili elementari del vecchio codice mandato"));
            string deleteSQL = $@"          DELETE FROM [MOVIMENTI_CONTABILI_ELEMENTARI] WHERE codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
            SqlCommand deleteCmd = new(deleteSQL, conn);
            deleteCmd.ExecuteNonQuery();
            _progress.Report((10, "Set in movimenti contabili elementari dello stato a 0 dove era in elaborazione"));
            string updateSQL = $@"          UPDATE [MOVIMENTI_CONTABILI_ELEMENTARI] SET stato = 0, codice_movimento = null WHERE stato = 2 AND codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
            SqlCommand updateCmd = new(updateSQL, conn);
            updateCmd.ExecuteNonQuery();
            _progress.Report((10, "Pulizia da stati del movimento contabile del vecchio codice mandato"));
            string deleteStatiSQL = $@"     DELETE FROM [STATI_DEL_MOVIMENTO_CONTABILE] WHERE codice_movimento in 
                                            (SELECT codice_movimento FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%'))";
            SqlCommand deleteStatiCmd = new(deleteStatiSQL, conn);
            deleteStatiCmd.ExecuteNonQuery();
            _progress.Report((10, "Pulizia da movimenti contabili generali del vecchio codice mandato"));
            string deleteGeneraliSQL = $@"  DELETE FROM [MOVIMENTI_CONTABILI_GENERALI] WHERE cod_mandato LIKE ('{selectedVecchioMandato}%')";
            SqlCommand deleteGeneraliCmd = new(deleteGeneraliSQL, conn);
            deleteGeneraliCmd.ExecuteNonQuery();
        }
        void GenerateStudentListToPay(SqlConnection conn)
        {
            string dataQuery = $"SELECT * FROM {dbTableName}";
            if (usingFiltroManuale)
            {
                if (usingStringWhere)
                {
                    if (!stringQueryWhere.TrimStart().StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
                    {
                        stringQueryWhere = "WHERE " + stringQueryWhere;
                    }
                    dataQuery = dataQuery + $@" {stringQueryWhere} AND anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_PEC = '0'";
                }
                else
                {
                    StringBuilder conditionalBuilder = new StringBuilder();
                    if (dictQueryWhere["Sesso"] != "''")
                    {
                        conditionalBuilder.Append($" sesso in ({dictQueryWhere["Sesso"]}) AND ");
                    }
                    if (dictQueryWhere["StatusSede"] != "''")
                    {
                        conditionalBuilder.Append($" status_sede in ({dictQueryWhere["StatusSede"]}) AND ");
                    }
                    if (dictQueryWhere["Cittadinanza"] != "''")
                    {
                        conditionalBuilder.Append($" cod_cittadinanza in ({dictQueryWhere["Cittadinanza"]}) AND ");
                    }
                    if (dictQueryWhere["CodEnte"] != "''")
                    {
                        conditionalBuilder.Append($" cod_ente in ({dictQueryWhere["CodEnte"]}) AND ");
                    }
                    if (dictQueryWhere["EsitoPA"] != "''")
                    {
                        conditionalBuilder.Append($" esitoPA in ({dictQueryWhere["EsitoPA"]}) AND ");
                    }
                    if (dictQueryWhere["AnnoCorso"] != "''")
                    {
                        conditionalBuilder.Append($" anno_corso in ({dictQueryWhere["AnnoCorso"]}) AND ");
                    }
                    if (dictQueryWhere["Disabile"] != "''")
                    {
                        conditionalBuilder.Append($" disabile in ({dictQueryWhere["Disabile"]}) AND ");
                    }
                    if (dictQueryWhere["TipoCorso"] != "''")
                    {
                        conditionalBuilder.Append($" cod_corso in ({dictQueryWhere["TipoCorso"]}) AND ");
                    }
                    if (dictQueryWhere["SedeStudi"] != "''")
                    {
                        conditionalBuilder.Append($" sede_studi in ({dictQueryWhere["SedeStudi"]}) AND ");
                    }
                    if (dictQueryWhere["TogliereLoreto"] != "''")
                    {
                        conditionalBuilder.Append($" togliere_loreto IN ({dictQueryWhere["TogliereLoreto"]}) AND ");
                    }

                    if (conditionalBuilder.Length > 0)
                    {
                        conditionalBuilder.Length -= " AND ".Length;
                    }

                    string whereString = conditionalBuilder.ToString();
                    dataQuery += $" WHERE anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_PEC = '0' AND " + whereString;

                    if (conditionalBuilder.Length <= 0)
                    {
                        _progress.Report((100, "Errore nella costruzione della query - Selezionare almeno un parametro se si usa il filtro manuale"));
                        _mainForm.inProcedure = false;
                        return;
                    }
                }
            }
            else
            {
                dataQuery += $@" WHERE anno_accademico = '{selectedAA}' AND cod_beneficio = '{tipoBeneficio}' AND liquidabile = '1' AND Togliere_loreto = '0' AND Togliere_PEC = '0'";
            }

            _progress.Report((20, "Generazione studenti"));

            SqlCommand readData = new(dataQuery, conn)
            {
                CommandTimeout = 900000
            };
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    int.TryParse(reader["disabile"].ToString(), out int disabile);
                    int.TryParse(reader["Superamento_esami"].ToString(), out int superamentoEsami);
                    int.TryParse(reader["Superamento_esami_tassa_reg"].ToString(), out int superamentoEsamiTassaRegionale);
                    int.TryParse(reader["anno_corso"].ToString(), out int annoCorso);
                    string studenteCodEnte = reader["cod_ente"].ToString();

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


                    Studente studente = new Studente(
                            Utilities.RemoveAllSpaces(reader["num_domanda"].ToString()),
                            Utilities.RemoveAllSpaces(reader["cod_fiscale"].ToString().ToUpper()),
                            reader["Cognome"].ToString().Trim(),
                            reader["Nome"].ToString().Trim(),
                            (DateTime)reader["Data_nascita"],
                            reader["sesso"].ToString(),
                            studenteCodEnte,
                            disabile == 1 ? true : false,
                            double.TryParse(Utilities.RemoveAllSpaces(reader["imp_beneficio"].ToString()), out double importoBeneficio) ? importoBeneficio : 0,
                            annoCorso,
                            int.TryParse(Utilities.RemoveAllSpaces(reader["cod_corso"].ToString()), out int codCorso) ? codCorso : 0,
                            Utilities.RemoveAllSpaces(reader["EsitoPA"].ToString()) == "2" ? true : false,
                            double.TryParse(Utilities.RemoveAllSpaces(reader["Imp_pagato"].ToString()), out double importoPagato) ? importoPagato : 0,
                            superamentoEsami == 1 ? true : false,
                            superamentoEsamiTassaRegionale == 1 ? true : false
                        );

                    listaStudentiDaPagare.Add(studente);
                }
            }
            _progress.Report((20, $"UPDATE:Generazione studenti - Completato"));
        }
        private void FilterPagamenti(SqlConnection conn)
        {
            string sqlPagam = $@"
                            SELECT
                                Domanda.Cod_fiscale,
                                Decod_pagam_new.Cod_tipo_pagam_new AS Cod_tipo_pagam,
                                Ritirato_azienda
                            FROM
                                Pagamenti
                                INNER JOIN Domanda ON Pagamenti.anno_accademico = Domanda.anno_accademico AND Pagamenti.num_domanda = Domanda.Num_domanda
                                INNER JOIN #CFestrazione cf ON Domanda.cod_fiscale = cf.Cod_fiscale
	                            INNER JOIN Decod_pagam_new ON Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_old OR Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_new

                            WHERE
                                Domanda.Anno_accademico = '{selectedAA}'
                                    ";

            SqlCommand readData = new(sqlPagam, conn);
            _progress.Report((11, $"Lavorazione studenti - inserimento pagamenti"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        studente.AddPagamentoEffettuato(
                            reader["Cod_tipo_pagam"].ToString(),
                            reader["Ritirato_azienda"].ToString() == "1" ? true : false
                            );
                    }
                }
            }

            List<Studente> studentiDaRimuovere = new List<Studente>();
            foreach (Studente studenteDaControllare in listaStudentiDaPagare)
            {
                bool stessoPagamento = false;

                if (studenteDaControllare.pagamentiEffettuati == null)
                {
                    continue;
                }

                foreach (Pagamento pagamento in studenteDaControllare.pagamentiEffettuati)
                {
                    if (pagamento.codTipoPagam == codTipoPagamento)
                    {
                        stessoPagamento = true;
                        break;
                    }
                }

                if (stessoPagamento)
                {
                    studentiDaRimuovere.Add(studenteDaControllare);
                    break;
                }
            }

            listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);

            string lastValue = codTipoPagamento.Substring(3);
            string firstPart = codTipoPagamento.Substring(0, 3);
            string integrazioneValue = codTipoPagamento.Substring(2, 1);
            if (integrazioneValue == "I")
            {
                isIntegrazione = true;
            }
            if (lastValue != "0" && lastValue != "9" && lastValue != "6")
            {
                ControlloRiemissioni(firstPart, lastValue);
                isRiemissione = true;
            }
            else if (integrazioneValue == "I")
            {
                ControlloIntegrazioni();
            }

        }

        private void ControlloIntegrazioni()
        {
            List<Studente> studentiDaRimuovere = new List<Studente>();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                if (studente.pagamentiEffettuati == null)
                {
                    studentiDaRimuovere.Add(studente);
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
            listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);
        }
        private void ControlloRiemissioni(string firstPart, string lastValue)
        {
            List<Studente> studentiDaRimuovere = new List<Studente>();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                if (studente.pagamentiEffettuati == null)
                {
                    studentiDaRimuovere.Add(studente);
                    continue;
                }

                bool pagamentoPossibile = false;
                foreach (Pagamento pagamento in studente.pagamentiEffettuati)
                {
                    string pagamFirstPart = pagamento.codTipoPagam.Substring(0, 3);
                    string pagamLastValue = pagamento.codTipoPagam.Substring(3);
                    if (pagamFirstPart != firstPart)
                    {
                        continue;
                    }

                    switch (pagamLastValue)
                    {
                        case "0":
                            if ((lastValue == "1") && pagamento.ritiratoAzienda)
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "6":
                            if ((lastValue == "7") && pagamento.ritiratoAzienda)
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "9":
                            if ((lastValue == "A") && pagamento.ritiratoAzienda)
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "1":
                            if ((lastValue == "2") && pagamento.ritiratoAzienda)
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "A":
                            if ((lastValue == "B") && pagamento.ritiratoAzienda)
                            {
                                pagamentoPossibile = true;
                            }
                            break;
                        case "7":
                            if ((lastValue == "8") && pagamento.ritiratoAzienda)
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

            listaStudentiDaPagare.RemoveAll(studentiDaRimuovere.Contains);

        }
        private void CheckLiquefazione(SqlConnection conn)
        {
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

            SqlCommand readData = new(sqlKiller, conn);
            _progress.Report((12, $"Lavorazione studenti - controllo eliminabili"));
            List<Studente> listaStudentiDaEliminare = new List<Studente>();
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (listaStudentiDaEliminare.Contains(studente))
                        {
                            continue;
                        }
                        listaStudentiDaEliminare.Add(studente);
                    }
                }
            }

            string sqlStudentiSenzaIBAN = $@" 
                        SELECT DISTINCT
                            vMODALITA_PAGAMENTO.Cod_fiscale
                        FROM
                            vMODALITA_PAGAMENTO 
                            INNER JOIN #CFEstrazione cfe ON vMODALITA_PAGAMENTO.Cod_fiscale = cfe.Cod_fiscale 
                        WHERE 
                            IBAN = ''
                     ";
            SqlCommand IBANDATA = new(sqlStudentiSenzaIBAN, conn);
            _progress.Report((12, $"Lavorazione studenti - controllo IBAN"));
            List<Studente> listaStudentiDaEliminareIBAN = new List<Studente>();
            using (SqlDataReader reader = IBANDATA.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (listaStudentiDaEliminareIBAN.Contains(studente))
                        {
                            continue;
                        }
                        listaStudentiDaEliminareIBAN.Add(studente);
                    }
                }
            }

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
            SqlCommand nonVincitori = new(sqlStudentiNonVincitori, conn);
            _progress.Report((12, $"Lavorazione studenti - controllo Vincitori"));
            List<Studente> listaStudentiDaEliminareNonVincitori = new List<Studente>();
            using (SqlDataReader reader = nonVincitori.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (listaStudentiDaEliminareNonVincitori.Contains(studente))
                        {
                            continue;
                        }
                        listaStudentiDaEliminareNonVincitori.Add(studente);
                    }
                }
            }

            string sqlStudentiPecKiller = $@"
                            SELECT LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, Indirizzo_PEC 
                            FROM LUOGO_REPERIBILITA_STUDENTE
                            LEFT OUTER JOIN vProfilo ON LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = vProfilo.Cod_Fiscale 
                            WHERE ANNO_ACCADEMICO = {selectedAA} 
                                AND tipo_bando = 'lz' 
                                AND TIPO_LUOGO = 'DOL'
                                AND DATA_FINE_VALIDITA IS NULL
                                AND (INDIRIZZO = '' OR INDIRIZZO = 'ROMA' OR INDIRIZZO = 'CASSINO' OR INDIRIZZO = 'FROSINONE')
                                AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vResidenza where ANNO_ACCADEMICO = '{selectedAA}' AND provincia_residenza = 'ee')
                                AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vDomicilio where ANNO_ACCADEMICO = '{selectedAA}' AND (Indirizzo_domicilio = '' or Indirizzo_domicilio = 'ROMA' or Indirizzo_domicilio = 'CASSINO' or Indirizzo_domicilio = 'FROSINONE'  or prov = 'EE'))
	                            AND Indirizzo_PEC IS NULL
                            ";
            SqlCommand nonPecCmd = new(sqlStudentiPecKiller, conn);
            _progress.Report((12, $"Lavorazione studenti - controllo PEC"));
            List<Studente> listaStudentiDaEliminarePEC = new List<Studente>();
            using (SqlDataReader reader = nonPecCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (listaStudentiDaEliminarePEC.Contains(studente))
                        {
                            continue;
                        }
                        listaStudentiDaEliminarePEC.Add(studente);
                    }
                }
            }


            if (listaStudentiDaEliminare.Count > 0)
            {
                List<string> codFiscaliDaEliminare = listaStudentiDaEliminare.Select(studente => studente.codFiscale).ToList();
                string sqlUpdate = $"UPDATE {dbTableName} SET togliere_loreto = 1, note = 'Studente con blocchi al momento della generazione del flusso' where cod_fiscale in ({string.Join(", ", codFiscaliDaEliminare.Select(cf => $"'{cf}'"))}) ";
                using (SqlCommand cmd = new SqlCommand(sqlUpdate, conn))
                {
                    cmd.ExecuteNonQuery(); // Execute the query
                }
            }

            if (listaStudentiDaEliminareIBAN.Count > 0)
            {
                List<string> codFiscaliDaEliminareIBAN = listaStudentiDaEliminareIBAN.Select(studente => studente.codFiscale).ToList();
                string sqlUpdateIBAN = $"UPDATE {dbTableName} SET liquidabile = 0, note = 'Studente senza IBAN al momento della generazione del flusso' where cod_fiscale in ({string.Join(", ", codFiscaliDaEliminareIBAN.Select(cf => $"'{cf}'"))}) ";
                using (SqlCommand cmd = new SqlCommand(sqlUpdateIBAN, conn))
                {
                    cmd.ExecuteNonQuery(); // Execute the query
                }
            }

            if (listaStudentiDaEliminareNonVincitori.Count > 0)
            {
                List<string> codFiscaliDaEliminareNonVincitori = listaStudentiDaEliminareNonVincitori.Select(studente => studente.codFiscale).ToList();
                string sqlUpdateNonVincitori = $"UPDATE {dbTableName} SET togliere_loreto = 1, note = 'Studente non vincitore al momento della generazione del flusso' where cod_fiscale in ({string.Join(", ", codFiscaliDaEliminareNonVincitori.Select(cf => $"'{cf}'"))}) ";
                using (SqlCommand cmd = new SqlCommand(sqlUpdateNonVincitori, conn))
                {
                    cmd.ExecuteNonQuery(); // Execute the query
                }
            }

            if (listaStudentiDaEliminarePEC.Count > 0)
            {
                List<string> codFiscaliDaEliminarePEC = listaStudentiDaEliminarePEC.Select(studente => studente.codFiscale).ToList();
                string sqlUpdatePEC = $"UPDATE {dbTableName} SET togliere_PEC = 1, note = 'Studente senza PEC al momento della generazione del flusso' where cod_fiscale in ({string.Join(", ", codFiscaliDaEliminarePEC.Select(cf => $"'{cf}'"))}) ";
                using (SqlCommand cmd = new SqlCommand(sqlUpdatePEC, conn))
                {
                    cmd.ExecuteNonQuery(); // Execute the query
                }
            }

            listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminare.Contains);
            listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminareIBAN.Contains);
            listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminareNonVincitori.Contains);
            listaStudentiDaPagare.RemoveAll(listaStudentiDaEliminarePEC.Contains);

            _progress.Report((12, $"Lavorazione studenti - controllo eliminabili - completato"));
        }

        void ProcessStudentList()
        {
            if (listaStudentiDaPagare.Count == 0)
            {
                return;
            }
            _progress.Report((30, $"Lavorazione studenti"));
            List<string> codFiscali = listaStudentiDaPagare.Select(studente => studente.codFiscale).ToList();

            using SqlConnection conn = new(CONNECTION_STRING);
            conn.Open();

            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
            SqlCommand createCmd = new(createTempTable, conn);
            createCmd.ExecuteNonQuery();

            _progress.Report((30, $"Lavorazione studenti - creazione tabella codici fiscali"));
            for (int i = 0; i < codFiscali.Count; i += 1000)
            {
                var batch = codFiscali.Skip(i).Take(1000);
                var insertQuery = "INSERT INTO #CFEstrazione (Cod_fiscale) VALUES " + string.Join(", ", batch.Select(cf => $"('{cf}')"));
                SqlCommand insertCmd = new(insertQuery, conn);
                insertCmd.ExecuteNonQuery();
            }
            if (!isTR)
            {
                FilterPagamenti(conn);
            }
            CheckLiquefazione(conn);

            if (listaStudentiDaPagare.Count > 0)
            {
                PopulateStudentLuogoNascita(conn);
                PopulateStudentResidenza(conn);
                PopulateStudentInformation(conn);
                if (tipoBeneficio == "BS" && !isTR)
                {
                    PopulateStudentDetrazioni(conn);
                    if (!isIntegrazione)
                    {
                        PopulateStudentiAssegnazioni(conn);
                    }
                }

                PopulateStudentiImpegni(conn);

                PopulateImportoDaPagare(conn);

                if (listaStudentiDaPagare.Count > 0)
                {
                    GenerateOutputFiles(conn);
                    InsertIntoMovimentazioni(conn);
                }
            }

            string dropTempTable = "DROP TABLE #CFEstrazione;";
            SqlCommand dropCmd = new(dropTempTable, conn);
            dropCmd.ExecuteNonQuery();
            string test = "";
        }

        private void InsertIntoMovimentazioni(SqlConnection conn)
        {
            try
            {
                _progress.Report((80, $"Lavorazione studenti - Inserimento in movimenti contabili"));
                Dictionary<int, Studente> codMovimentiPerStudente = new Dictionary<int, Studente>();

                int lastCodiceMovimento = 0;
                string sqlCodMovimento = $"SELECT TOP(1) CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI ORDER BY CODICE_MOVIMENTO DESC";
                SqlCommand cmdCM = new SqlCommand(sqlCodMovimento, conn);
                object result = cmdCM.ExecuteScalar();
                if (result != null)
                {
                    lastCodiceMovimento = Convert.ToInt32(result);
                }

                int nextCodiceMovimento = lastCodiceMovimento + 1;

                const int batchSize = 1000;
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_GENERALI (ID_CAUSALE_MOVIMENTO_GENERALE, IMPORTO_MOVIMENTO, UTENTE_VALIDAZIONE, DATA_VALIDITA_MOVIMENTO_GENERALE, NOTE_VALIDAZIONE_MOVIMENTO, COD_MANDATO) VALUES ";
                string note = "Inserimento tramite elaborazione file pagamenti";
                int numberOfBatches = (int)Math.Ceiling((double)listaStudentiDaPagare.Count / batchSize);

                StringBuilder finalQueryBuilder = new StringBuilder();
                _progress.Report((80, $"Lavorazione studenti - Movimenti contabili generali - batch n°0"));
                for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
                {
                    StringBuilder queryBuilder = new StringBuilder();
                    queryBuilder.Append(baseSqlInsert);

                    var batch = listaStudentiDaPagare.Skip(batchNumber * batchSize).Take(batchSize);

                    List<string> valuesList = new List<string>();

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
                        codMovimentiPerStudente.Add(nextCodiceMovimento, studente);
                        nextCodiceMovimento++;
                    }

                    queryBuilder.Append(string.Join(",", valuesList));
                    queryBuilder.Append("; ");

                    finalQueryBuilder.Append(queryBuilder.ToString());
                    _progress.Report((80, $"UPDATE:Lavorazione studenti - Movimenti contabili generali - batch n°{batchNumber}"));
                }
                string finalQuery = finalQueryBuilder.ToString();
                try
                {
                    using (SqlCommand cmd = new SqlCommand(finalQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _progress.Report((100, ex.Message));
                    throw;
                }

                InsertIntoStatiDelMovimentoContabile(conn, codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariPagamenti(conn, codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariDetrazioni(conn, codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariAssegnazioni(conn, codMovimentiPerStudente);
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                throw;
            }
        }
        private void InsertIntoStatiDelMovimentoContabile(SqlConnection conn, Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                const int batchSize = 1000;
                string baseSqlInsert = "INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO) VALUES ";
                StringBuilder finalQueryBuilder = new StringBuilder();

                List<string> batchStatements = new List<string>();
                int currentBatchSize = 0;
                _progress.Report((80, $"Lavorazione studenti - Stati del movimento contabile"));
                foreach (var entry in codMovimentiPerStudente)
                {
                    int codMovimento = entry.Key;
                    string insertStatement = $"(2, '{codMovimento}', '{DateTime.Now.ToString("dd/MM/yyyy")}', 'sa')";

                    batchStatements.Add(insertStatement);
                    currentBatchSize++;

                    if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                    {
                        finalQueryBuilder.Append(baseSqlInsert);
                        finalQueryBuilder.Append(string.Join(",", batchStatements));
                        finalQueryBuilder.Append("; ");

                        batchStatements.Clear();
                        currentBatchSize = 0;
                    }
                }

                string finalQuery = finalQueryBuilder.ToString();

                if (string.IsNullOrWhiteSpace(finalQuery))
                {
                    return;
                }
                try
                {
                    using (SqlCommand cmd = new SqlCommand(finalQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _progress.Report((100, ex.Message));
                    throw;
                }

                _progress.Report((80, $"UPDATE:Lavorazione studenti - Stati del movimento contabile - completo"));
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                throw;
            }
        }
        private void InsertIntoMovimentiContabiliElementariPagamenti(SqlConnection conn, Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                string codMovimentoElementare = "00";
                string sqlCodMovimento = $"SELECT DISTINCT Cod_mov_contabile_elem FROM Decod_pagam_new where Cod_tipo_pagam_new = '{codTipoPagamento}'";
                SqlCommand cmdCM = new SqlCommand(sqlCodMovimento, conn);
                object result = cmdCM.ExecuteScalar();
                if (result != null)
                {
                    codMovimentoElementare = result.ToString();
                }


                const int batchSize = 1000; // Maximum number of rows per INSERT statement
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                StringBuilder finalQueryBuilder = new StringBuilder();

                List<string> batchStatements = new List<string>();
                int currentBatchSize = 0;
                _progress.Report((80, $"Lavorazione studenti - Movimenti contabili elementari"));
                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {
                    int codMovimento = entry.Key;
                    string importoDaPagare = entry.Value.importoDaPagare.ToString(CultureInfo.InvariantCulture);
                    int segno = 1;

                    // Assuming you might need some data from the Studente object, you can access it like this: entry.Value
                    string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '{codMovimentoElementare}', '{codMovimento}', '{importoDaPagare}', '{segno}', '{DateTime.Now.ToString("yyyy")}','2', '{DateTime.Now.ToString("dd/MM/yyyy")}', 'sa', '', '')";

                    batchStatements.Add(insertStatement);
                    currentBatchSize++;

                    // Execute batch when reaching batchSize or end of dictionary
                    if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                    {
                        finalQueryBuilder.Append(baseSqlInsert);
                        finalQueryBuilder.Append(string.Join(",", batchStatements));
                        finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                        batchStatements.Clear(); // Clear the batch for the next round
                        currentBatchSize = 0; // Reset the batch size counter
                    }
                }

                string finalQuery = finalQueryBuilder.ToString();
                if (string.IsNullOrWhiteSpace(finalQuery))
                {
                    return;
                }
                try
                {
                    // Execute all accumulated SQL statements at once
                    using (SqlCommand cmd = new SqlCommand(finalQuery, conn))
                    {
                        cmd.ExecuteNonQuery(); // Execute the query
                    }
                }
                catch (Exception ex)
                {
                    _progress.Report((100, ex.Message));
                    throw;
                }

                _progress.Report((80, $"UPDATE:Lavorazione studenti - Movimenti contabili elementari - completo"));
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                throw;
            }
        }
        private void InsertIntoMovimentiContabiliElementariDetrazioni(SqlConnection conn, Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                const int batchSize = 1000; // Maximum number of rows per INSERT statement
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                StringBuilder finalQueryBuilder = new StringBuilder();

                List<string> batchStatements = new List<string>();
                int currentBatchSize = 0;

                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {

                    if (entry.Value.detrazioni == null)
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
                        string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '01', '{codMovimento}', '{importoDaDetrarre}', '{segno}', '{DateTime.Now.ToString("yyyy")}','2', '{DateTime.Now.ToString("dd/MM/yyyy")}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            finalQueryBuilder.Append(baseSqlInsert);
                            finalQueryBuilder.Append(string.Join(",", batchStatements));
                            finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }
                }

                string finalQuery = finalQueryBuilder.ToString();
                if (string.IsNullOrWhiteSpace(finalQuery))
                {
                    return;
                }
                // Execute all accumulated SQL statements at once
                try
                {
                    using (SqlCommand cmd = new SqlCommand(finalQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _progress.Report((100, ex.Message));
                    throw;
                }
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                throw;
            }
        }
        private void InsertIntoMovimentiContabiliElementariAssegnazioni(SqlConnection conn, Dictionary<int, Studente> codMovimentiPerStudente)
        {
            try
            {
                const int batchSize = 1000; // Maximum number of rows per INSERT statement
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                StringBuilder finalQueryBuilder = new StringBuilder();

                List<string> batchStatements = new List<string>();
                int currentBatchSize = 0;

                foreach (KeyValuePair<int, Studente> entry in codMovimentiPerStudente)
                {

                    if (entry.Value.assegnazioni == null)
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
                        string insertStatement = $"('{entry.Value.codFiscale}', '{selectedAA}', '02', '{codMovimento}', '{costoPostoAlloggio}', '{segno}', '{DateTime.Now.ToString("yyyy")}','2', '{DateTime.Now.ToString("dd/MM/yyyy")}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            finalQueryBuilder.Append(baseSqlInsert);
                            finalQueryBuilder.Append(string.Join(",", batchStatements));
                            finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }
                }

                string finalQuery = finalQueryBuilder.ToString();
                if (string.IsNullOrWhiteSpace(finalQuery))
                {
                    return;
                }
                try
                {
                    // Execute all accumulated SQL statements at once
                    using (SqlCommand cmd = new SqlCommand(finalQuery, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    _progress.Report((100, ex.Message));
                    throw;
                }
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                throw;
            }
        }

        private void GenerateOutputFiles(SqlConnection conn)
        {
            _progress.Report((60, $"Lavorazione studenti - Generazione files"));
            string currentMonthName = DateTime.Now.ToString("MMMM").ToUpper();
            string currentYear = DateTime.Now.ToString("yyyy");
            string firstHalfAA = selectedAA.Substring(2, 2);
            string secondHalfAA = selectedAA.Substring(6, 2);
            string baseFolderPath = Utilities.EnsureDirectory(Path.Combine(selectedSaveFolder, currentMonthName + currentYear + "_" + firstHalfAA + secondHalfAA));

            string currentBeneficio = isTR ? "TR" : tipoBeneficio;
            string currentCodTipoPagamento = currentBeneficio + selectedTipoPagamento;

            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{currentCodTipoPagamento}'";
            SqlCommand cmd = new SqlCommand(sqlTipoPagam, conn);
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();
            string beneficioFolderPath = Utilities.EnsureDirectory(Path.Combine(baseFolderPath, pagamentoDescrizione));

            bool doAllImpegni = selectedImpegno == "0000";
            IEnumerable<string> impegnoList = doAllImpegni ? impegniList : new List<string> { selectedImpegno };

            foreach (string impegno in impegnoList)
            {
                _progress.Report((60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno}"));
                string sqlImpegno = $"SELECT Descr FROM Impegni WHERE anno_accademico = '{selectedAA}' AND num_impegno = '{impegno}' AND categoria_pagamento = '{categoriaPagam}'";
                SqlCommand cmdImpegno = new SqlCommand(sqlImpegno, conn);
                string impegnoDescrizione = (string)cmdImpegno.ExecuteScalar();
                string currentFolder = Utilities.EnsureDirectory(Path.Combine(beneficioFolderPath, $"imp-{impegno}-{impegnoDescrizione}"));
                ProcessImpegno(conn, impegno, currentFolder);
            }

            var studentiSenzaImpegno = listaStudentiDaPagare
                .Where(s => string.IsNullOrWhiteSpace(s.numeroImpegno))
                .ToList();

            if (studentiSenzaImpegno.Any())
            {
                DataTable studentiSenzaImpegnoTable = GenerateDataTableFromList(studentiSenzaImpegno);
                Utilities.WriteDataTableToTextFile(studentiSenzaImpegnoTable, beneficioFolderPath, "Studenti Senza Impegno");
            }

            _progress.Report((60, $"UPDATE:Lavorazione studenti - Generazione files - Completato"));
        }
        private void ProcessImpegno(SqlConnection conn, string impegno, string currentFolder)
        {
            var groupedStudents = listaStudentiDaPagare
                .Where(s => s.numeroImpegno == impegno)
                .ToList();

            if (groupedStudents.Any())
            {
                GenerateOutputFilesPA(conn, currentFolder, groupedStudents, impegno);
            }
        }

        private void GenerateOutputFilesPA(SqlConnection conn, string currentFolder, List<Studente> studentsWithSameImpegno, string impegno)
        {
            _progress.Report((60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Senza detrazioni"));
            var studentsWithPA = studentsWithSameImpegno
                .Where(s => s.assegnazioni != null || (s.detrazioni != null && s.detrazioni.Any(d => d.codReversale == "01" && d.daContabilizzare)))
                .ToList();
            var studentsWithoutPA = studentsWithSameImpegno
                .Where(s => s.assegnazioni == null && !(s.detrazioni != null && s.detrazioni.Any(d => d.codReversale == "01" && d.daContabilizzare)))
                .ToList();

            if (studentsWithoutPA.Count > 0)
            {

                if (tipoStudente == "2")
                {
                    ProcessStudentsByAnnoCorso(studentsWithoutPA, currentFolder, processMatricole: true, processAnniSuccessivi: true, "SenzaDetrazioni", "00", impegno);
                    DataTable dataTableMatricole = GenerareExcelDataTableNoDetrazioni(conn, studentsWithoutPA.Where(s => s.annoCorso == 1).ToList(), impegno);
                    DataTable dataTableASuccessivi = GenerareExcelDataTableNoDetrazioni(conn, studentsWithoutPA.Where(s => s.annoCorso != 1).ToList(), impegno);
                    if (dataTableMatricole != null)
                    {
                        Utilities.ExportDataTableToExcel(dataTableMatricole, currentFolder, false, "Matricole");
                    }
                    if (dataTableASuccessivi != null)
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
            if (studentsWithPA.Count > 0)
            {
                string newFolderPath = Utilities.EnsureDirectory(Path.Combine(currentFolder, "CON DETRAZIONE"));
                ProcessStudentsByCodEnte(conn, selectedCodEnte, studentsWithPA, newFolderPath, impegno);
                GenerateGiuliaFile(newFolderPath, studentsWithPA, impegno);
            }
        }

        private void ProcessStudentsByAnnoCorso(List<Studente> students, string folderPath, bool processMatricole, bool processAnniSuccessivi, string nomeFileInizio, string codEnteFlusso, string impegnoFlusso)
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
        private void ProcessStudentsByCodEnte(SqlConnection conn, string selectedCodEnte, List<Studente> studentsWithPA, string newFolderPath, string impegno)
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
                SqlCommand cmdSede = new SqlCommand(sqlCodEnte, conn);
                nomeCodEnte = (string)cmdSede.ExecuteScalar();
                nomeCodEnte = Utilities.SanitizeColumnName(nomeCodEnte);
                _progress.Report((60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Flusso con detrazioni ente: {nomeCodEnte}"));
                string specificFolderPath = Utilities.EnsureDirectory(Path.Combine(newFolderPath, $"{nomeCodEnte}"));

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
            DataTable dataTableMatricole = GenerareExcelDataTableConDetrazioni(conn, studentsWithPA.Where(s => s.annoCorso == 1).ToList(), sediStudi, impegno);
            DataTable dataTableASuccessivi = GenerareExcelDataTableConDetrazioni(conn, studentsWithPA.Where(s => s.annoCorso != 1).ToList(), sediStudi, impegno);
            if (dataTableMatricole != null)
            {
                Utilities.ExportDataTableToExcel(dataTableMatricole, newFolderPath, false, "Matricole");
            }
            if (dataTableASuccessivi != null)
            {
                Utilities.ExportDataTableToExcel(dataTableASuccessivi, newFolderPath, false, "AnniSuccessivi");
            }

        }
        private void ProcessAndWriteStudents(List<Studente> students, string folderPath, string fileName, string codEnteFlusso, string impegnoFlusso)
        {
            if (students.Any())
            {
                DataTable dataTableFlusso = GenerareFlussoDataTable(students, codEnteFlusso);
                if (dataTableFlusso != null)
                {
                    Utilities.WriteDataTableToTextFile(dataTableFlusso, folderPath, $"flusso_{fileName}_impegnoFlusso");
                }
            }
        }

        private void GenerateGiuliaFile(string newFolderPath, List<Studente> studentsWithPA, string impegno)
        {

            DataTable dataTable = GenerareGiuliaDataTable(studentsWithPA, impegno);
            if (dataTable.Rows.Count > 0)
            {
                Utilities.ExportDataTableToExcel(dataTable, newFolderPath, false, "Dettaglio PA");
            }
        }
        private DataTable GenerareGiuliaDataTable(List<Studente> studentsWithPA, string impegno)
        {
            _progress.Report((60, $"Lavorazione studenti - impegno n°{impegno} - Generazione Dettaglio PA"));
            int progressivo = 1;
            DataTable returnDataTable = new DataTable();

            returnDataTable.Columns.Add("1");
            returnDataTable.Columns.Add("2");
            returnDataTable.Columns.Add("3");
            returnDataTable.Columns.Add("4");
            returnDataTable.Columns.Add("5");
            returnDataTable.Columns.Add("6");
            returnDataTable.Columns.Add("7");
            returnDataTable.Columns.Add("8");

            foreach (Studente studente in studentsWithPA)
            {
                if (studente.assegnazioni == null)
                {
                    continue;
                }

                returnDataTable.Rows.Add(progressivo, studente.cognome, studente.nome);
                returnDataTable.Rows.Add(" ");
                returnDataTable.Rows.Add(" ", "Costo periodo", "Totale parziale", "Residenza", "Tipo stanza", "Data decorrenza", "Data fine assegnazione", "Num giorni");
                double costoServizioPA = 0;
                double costoPA = 0;
                double accontoPA = 0;
                double totaleParzialePA = 0;
                foreach (Assegnazione assegnazione in studente.assegnazioni)
                {
                    double roundedCostoServizioPA = 0;
                    costoPA = Math.Round(assegnazione.costoTotale, 2);
                    accontoPA = Math.Round(studente.importoAccontoPA, 2);
                    costoServizioPA += costoPA;
                    roundedCostoServizioPA = Math.Round(costoServizioPA, 2);
                    returnDataTable.Rows.Add(" ", costoPA, roundedCostoServizioPA, assegnazione.codPensionato, assegnazione.codTipoStanza, assegnazione.dataDecorrenza, assegnazione.dataFineAssegnazione, (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days);
                }
                costoServizioPA = Math.Round(costoServizioPA, 2);
                returnDataTable.Rows.Add(" ");
                returnDataTable.Rows.Add(" ", studente.cognome, studente.nome);
                returnDataTable.Rows.Add(" ", "Importo borsa totale", studente.importoBeneficio);
                returnDataTable.Rows.Add(" ", "Costo servizio PA", costoServizioPA);
                returnDataTable.Rows.Add(" ", "Acconto PA", studente.importoAccontoPA);
                returnDataTable.Rows.Add(" ", "Saldo PA", Math.Round(costoServizioPA - studente.importoAccontoPA, 2));
                returnDataTable.Rows.Add(" ");
                returnDataTable.Rows.Add(" ", "Saldo", $"Lordo = {Math.Round(studente.importoDaPagareLordo)}", $"Ritenuta = {Math.Round(costoServizioPA - accontoPA)}", $"Netto = {Math.Round(studente.importoDaPagareLordo - (costoServizioPA - accontoPA))}");
                returnDataTable.Rows.Add(" ");
                returnDataTable.Rows.Add(" ");
                progressivo++;
            }

            return returnDataTable;
        }

        void PopulateStudentLuogoNascita(SqlConnection conn)
        {
            string dataQuery = @"
                SELECT *
                FROM Studente 
                LEFT OUTER JOIN Comuni ON Studente.Cod_comune_nasc = Comuni.Cod_comune 
                INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale";

            SqlCommand readData = new(dataQuery, conn);
            _progress.Report((30, $"Lavorazione studenti - inserimento in luogo nascita"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        studente.SetLuogoNascita(
                            reader["COD_COMUNE_NASC"].ToString(),
                            reader["descrizione"].ToString(),
                            reader["COD_PROVINCIA"].ToString()
                        );
                    }
                }
            }
            _progress.Report((30, $"UPDATE:Lavorazione studenti - inserimento in luogo nascita - completato"));
        }
        void PopulateStudentResidenza(SqlConnection conn)
        {
            string dataQuery = @"
                    SELECT vResidenza.* 
                    FROM vResidenza 
                    INNER JOIN #CFEstrazione cfe ON vResidenza.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE ANNO_ACCADEMICO = @AnnoAccademico";

            SqlCommand readData = new(dataQuery, conn);
            readData.Parameters.AddWithValue("@AnnoAccademico", selectedAA);
            _progress.Report((35, $"Lavorazione studenti - inserimento in residenza"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        studente.SetResidenza(
                            reader["INDIRIZZO"].ToString(),
                            reader["Cod_comune"].ToString(),
                            reader["provincia_residenza"].ToString(),
                            reader["CAP"].ToString(),
                            reader["comune_residenza"].ToString()
                        );
                    }
                }
            }
            _progress.Report((35, $"UPDATE:Lavorazione studenti - inserimento in residenza - completato"));
        }
        void PopulateStudentInformation(SqlConnection conn)
        {
            string dataQuery = @"
                    SELECT * 
                    FROM Studente LEFT OUTER JOIN 
                    vModalita_pagamento ON studente.cod_fiscale = vmodalita_pagamento.cod_fiscale
                    INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale 
                    ";

            SqlCommand readData = new(dataQuery, conn);
            _progress.Report((40, $"Lavorazione studenti - inserimento in informazioni"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        studente.SetInformations(
                            long.TryParse(Regex.Replace(reader["telefono_cellulare"].ToString(), @"[^\d]", ""), out long telefonoNumber) ? telefonoNumber : 0,
                            reader["indirizzo_e_mail"].ToString(),
                            reader["IBAN"].ToString(),
                            reader["swift"].ToString()
                        );
                    }
                }
            }
            _progress.Report((40, $"UPDATE:Lavorazione studenti - inserimento in informazioni - completato"));
        }
        void PopulateStudentDetrazioni(SqlConnection conn)
        {
            string dataQuery = $@"
                    SELECT Domanda.Cod_fiscale, Reversali.*, (SELECT DISTINCT cod_tipo_pagam_new FROM Decod_pagam_new where Cod_tipo_pagam_old = Reversali.Cod_tipo_pagam OR Cod_tipo_pagam_new = Reversali.Cod_tipo_pagam) AS cod_tipo_pagam_new
                    FROM Domanda 
                    INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    INNER JOIN Reversali ON Domanda.num_domanda = Reversali.num_domanda AND Domanda.Anno_accademico = Reversali.Anno_accademico
                    WHERE Reversali.Ritirato_azienda = 0 AND Domanda.Anno_accademico = '{selectedAA}'
                    ";

            SqlCommand readData = new(dataQuery, conn);
            _progress.Report((45, $"Lavorazione studenti - inserimento in detrazioni"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        studente.AddDetrazione(
                                reader["cod_reversale"].ToString(),
                                double.TryParse(reader["importo"].ToString(), out double importo) ? importo : 0,
                                reader["Note"].ToString(),
                                reader["cod_tipo_pagam"].ToString(),
                                reader["cod_tipo_pagam_new"].ToString()
                            );
                    }
                }
            }
            _progress.Report((45, $"UPDATE:Lavorazione studenti - inserimento in detrazioni - completato"));
        }
        void PopulateStudentiAssegnazioni(SqlConnection conn)
        {
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

            SqlCommand readData = new(dataQuery, conn);
            _progress.Report((50, $"Lavorazione studenti - inserimento in assegnazioni"));
            HashSet<string> processedFiscalCodes = new HashSet<string>();

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);

                    if (studente == null || reader["Cod_Stanza"].ToString() == "XXX")
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

                        if (studente.detrazioni != null)
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

                        if (studente.annoCorso > 0)
                        {
                            studente.AddDetrazione("01", 450, "Detrazione acconto PA", "01", "BSP0", true);
                        }
                        else if (studenteFuoriCorso || studenteDisabileFuoriCorso)
                        {
                            studente.AddDetrazione("01", 200, "Detrazione acconto PA", "01", "BSP0", true);
                        }
                        processedFiscalCodes.Add(codFiscale);

                    }
                    else if (categoriaPagam == "SA")
                    {

                        AssegnazioneDataCheck result = studente.AddAssegnazione(
                                 reader["cod_pensionato"].ToString().Trim(),
                                 reader["cod_stanza"].ToString().Trim(),
                                 DateTime.Parse(reader["data_decorrenza"].ToString().Trim()),
                                 DateTime.TryParse(reader["data_fine_assegnazione"].ToString().Trim(), out DateTime date) ? date : new DateTime(2024, 07, 31),
                                 reader["Cod_fine_assegnazione"].ToString().Trim(),
                                 reader["tipo_stanza"].ToString().Trim(),
                                 double.TryParse(reader["importo_mensile"].ToString().Trim(), out double importoMensile) ? importoMensile : 0,
                                 new DateTime(2023, 10, 01),
                                 new DateTime(2024, 07, 31),
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
                        if (!studentiConErroriPA.ContainsKey(studente))
                        {
                            studentiConErroriPA.Add(studente, new List<string>());
                        }
                        studentiConErroriPA[studente].Add(message);
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

                    studente.AddAssegnazione(
                        vecchiaAssegnazione.codPensionato,
                        vecchiaAssegnazione.codStanza,
                        vecchiaAssegnazione.dataDecorrenza,
                        new DateTime(2024, 07, 31),
                        vecchiaAssegnazione.codFineAssegnazione,
                        vecchiaAssegnazione.codTipoStanza,
                        vecchiaAssegnazione.costoMensile,
                        new DateTime(2023, 10, 01),
                        new DateTime(2024, 07, 31),
                        studenteFuoriCorso || studenteDisabileFuoriCorso
                        );
                    studente.assegnazioni.RemoveAt(0);
                }
            }
            _progress.Report((50, $"UPDATE:Lavorazione studenti - inserimento in assegnazioni - completato"));
        }
        void PopulateImportoDaPagare(SqlConnection conn)
        {
            _progress.Report((55, $"Lavorazione studenti - Calcolo importi"));
            List<Studente> studentiDaRimuovereDalPagamento = new List<Studente>();
            foreach (Studente studente in listaStudentiDaPagare)
            {
                double importoDaPagare = studente.importoBeneficio;
                double importoMassimo = studente.importoBeneficio;

                double importoPA = 0;
                double accontoPA = 0;

                if (tipoBeneficio == "BS" && studente.assegnazioni != null)
                {
                    foreach (Assegnazione assegnazione in studente.assegnazioni)
                    {
                        importoPA += Math.Max(assegnazione.costoTotale, 0);
                    }
                }
                if (studente.detrazioni != null)
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
                        importoPA = importoPA - accontoPA;
                    }
                }
                studente.SetImportoSaldoPA(Math.Round(importoPA, 2));

                double importiPagati = isTR ? 0 : studente.importoPagato;


                if (categoriaPagam == "PR" && tipoBeneficio == "BS" && !isTR)
                {
                    string currentYear = selectedAA.Substring(0, 4);
                    DateTime percentDate = new DateTime(int.Parse(currentYear), 11, 10);
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
                if (studente.detrazioni != null)
                {
                    foreach (Detrazione detrazione in studente.detrazioni)
                    {
                        if (!detrazione.daContabilizzare && detrazione.codReversale == "01" && tipoBeneficio == "BS")
                        {
                            continue;
                        }
                        importoReversali += detrazione.importo;
                    }
                }

                studente.SetImportoDaPagareLordo(Math.Round(importoDaPagare - importiPagati, 2));
                importoDaPagare = Math.Max(importoDaPagare - (importiPagati + importoPA + importoReversali), 0);
                importoDaPagare = Math.Round(importoDaPagare, 2);

                if (importoDaPagare == 0 && !studentiDaRimuovereDalPagamento.Contains(studente))
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

            listaStudentiDaPagare.RemoveAll(studentiDaRimuovereDalPagamento.Contains);
            _progress.Report((55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato"));
        }
        void PopulateStudentiImpegni(SqlConnection conn)
        {
            if (isTR)
            {
                foreach (Studente studente in listaStudentiDaPagare)
                {
                    studente.SetImpegno("3120");
                }
                _progress.Report((45, $"UPDATE:Lavorazione studenti - inserimento impegni - completato"));
                return;
            }
            string dataQuery = $@"
                    SELECT Cod_fiscale, num_impegno_primaRata, num_impegno_saldo
                    FROM Specifiche_impegni
                    WHERE 
                        tipo_beneficio = '{tipoBeneficio}' AND
                        Anno_accademico = '{selectedAA}'
                    ";

            SqlCommand readData = new(dataQuery, conn);
            _progress.Report((45, $"Lavorazione studenti - inserimento impegni"));
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string impegnoToSet = "";
                    string codFiscale = Utilities.RemoveAllSpaces(reader["Cod_fiscale"].ToString().ToUpper());
                    Studente studente = listaStudentiDaPagare.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        if (categoriaPagam == "PR")
                        {
                            impegnoToSet = reader["num_impegno_primaRata"].ToString();
                        }
                        else
                        {
                            impegnoToSet = reader["num_impegno_saldo"].ToString();
                        }

                        studente.SetImpegno(impegnoToSet);
                    }
                }
            }
            _progress.Report((45, $"UPDATE:Lavorazione studenti - inserimento impegni - completato"));
        }

        private DataTable GenerareFlussoDataTable(List<Studente> studentiDaGenerare, string codEnteFlusso)
        {
            if (!studentiDaGenerare.Any())
            {
                return null;
            }
            DataTable studentsData = new DataTable();
            studentsData.Columns.Add("Incrementale", typeof(int));
            studentsData.Columns.Add("Cod_fiscale", typeof(string));
            studentsData.Columns.Add("Cognome", typeof(string));
            studentsData.Columns.Add("Nome", typeof(string));
            studentsData.Columns.Add("totale_lordo", typeof(double));
            studentsData.Columns.Add("reversali", typeof(double));
            studentsData.Columns.Add("importo_netto", typeof(double));
            studentsData.Columns.Add("conferma_pagamento", typeof(int));
            studentsData.Columns.Add("IBAN", typeof(string));
            studentsData.Columns.Add("Istituto_bancario", typeof(string));
            studentsData.Columns.Add("italiano", typeof(string));
            studentsData.Columns.Add("indirizzo_residenza", typeof(string));
            studentsData.Columns.Add("cod_catastale_residenza", typeof(string));
            studentsData.Columns.Add("provincia_residenza", typeof(string));
            studentsData.Columns.Add("cap_residenza", typeof(string));
            studentsData.Columns.Add("nazione_citta_residenza", typeof(string));
            studentsData.Columns.Add("sesso", typeof(string));
            studentsData.Columns.Add("data_nascita", typeof(string));
            studentsData.Columns.Add("luogo_nascita", typeof(string));
            studentsData.Columns.Add("cod_catastale_luogo_nascita", typeof(string));
            studentsData.Columns.Add("provincia_nascita", typeof(string));
            studentsData.Columns.Add("vuoto1", typeof(string));
            studentsData.Columns.Add("vuoto2", typeof(string));
            studentsData.Columns.Add("vuoto3", typeof(string));
            studentsData.Columns.Add("vuoto4", typeof(string));
            studentsData.Columns.Add("vuoto5", typeof(string));
            studentsData.Columns.Add("mail", typeof(string));
            studentsData.Columns.Add("vuoto6", typeof(string));
            studentsData.Columns.Add("telefono", typeof(long));

            int incremental = 1;


            foreach (Studente studente in studentiDaGenerare)
            {
                DateTime.TryParse(selectedDataRiferimento, out DateTime dataTabella);
                string dataCreazioneTabella = dataTabella.ToString("ddMMyy");

                string annoAccademicoBreve = selectedAA.Substring(2, 2) + selectedAA.Substring(6, 2);

                string mandatoProvvisorio = $"{codTipoPagamento}_{dataCreazioneTabella}_{annoAccademicoBreve}_{codEnteFlusso}_{studente.numeroImpegno}";
                studente.SetMandatoProvvisorio(mandatoProvvisorio);

                int straniero = studente.residenza.provincia == "EE" ? 0 : 1;
                string indirizzoResidenza = straniero == 0 ? studente.residenza.indirizzo.Replace("//", "-") : studente.residenza.indirizzo;
                string capResidenza = straniero == 0 ? "00000" : studente.residenza.CAP;
                string dataSenzaSlash = studente.dataNascita.ToString("ddMMyyyy");

                studentsData.Rows.Add(
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
        private DataTable GenerateDataTableFromList(List<Studente> studentiSenzaImpegno)
        {
            DataTable table = new DataTable();
            table.Columns.Add("Codice Fiscale", typeof(string));
            table.Columns.Add("Num Domanda", typeof(string));
            foreach (var student in studentiSenzaImpegno)
            {
                DataRow row = table.NewRow();
                row["Codice Fiscale"] = student.codFiscale;
                row["Num Domanda"] = student.numDomanda;
                table.Rows.Add(row);
            }

            return table;
        }

        private DataTable GenerareExcelDataTableConDetrazioni(SqlConnection conn, List<Studente> studentiDaGenerare, List<string> sediStudi, string impegno)
        {
            if (!studentiDaGenerare.Any())
            {
                return null;
            }
            DataTable studentsData = new DataTable();
            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            SqlCommand cmd = new SqlCommand(sqlTipoPagam, conn);
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();

            string annoAccedemicoFileName = selectedAA.Substring(2, 2) + selectedAA.Substring(6, 2);

            string impegnoNome = "impegno " + impegno;

            string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

            studentsData.Columns.Add("1");
            studentsData.Columns.Add("2");
            studentsData.Columns.Add("3");
            studentsData.Columns.Add("4");
            studentsData.Columns.Add("5");
            studentsData.Columns.Add("6");
            studentsData.Columns.Add("7");

            studentsData.Rows.Add(titolo);
            studentsData.Rows.Add("ALLEGATO DETERMINA");

            foreach (string codEnte in sediStudi)
            {
                string sqlCodEnte = $"SELECT descrizione FROM Enti_di_gestione WHERE cod_ente = '{codEnte}'";
                SqlCommand cmdSede = new SqlCommand(sqlCodEnte, conn);
                string nomeCodEnte = (string)cmdSede.ExecuteScalar();


                string nomePA = categoriaPagam == "PR" ? "ACCONTO PA" : "SALDO COSTO DEL SERVIZIO";

                studentsData.Rows.Add(" ");
                studentsData.Rows.Add(nomeCodEnte);
                studentsData.Rows.Add("N.PROG.", "CODICE_FISCALE", "COGNOME", "NOME", "TOTALE LORDO", nomePA, "IMPORTO NETTO");

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

                    studentsData.Rows.Add(progressivo, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), costoPA.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                    totaleLordo += s.importoDaPagareLordo;
                    totalePA += costoPA;
                    totaleNetto += s.importoDaPagare;
                    progressivo++;
                }
                studentsData.Rows.Add(" ");
                studentsData.Rows.Add(" ");
                studentsData.Rows.Add(" ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totalePA, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
                studentsData.Rows.Add(" ");
                studentsData.Rows.Add(" ");
            }


            return studentsData;
        }
        private DataTable GenerareExcelDataTableNoDetrazioni(SqlConnection conn, List<Studente> studentiDaGenerare, string impegno)
        {
            if (!studentiDaGenerare.Any())
            {
                return null;
            }
            DataTable studentsData = new DataTable();
            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            SqlCommand cmd = new SqlCommand(sqlTipoPagam, conn);
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();

            string annoAccedemicoFileName = selectedAA.Substring(2, 2) + selectedAA.Substring(6, 2);

            string impegnoNome = "impegno " + impegno;

            string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

            studentsData.Columns.Add("1");
            studentsData.Columns.Add("2");
            studentsData.Columns.Add("3");
            studentsData.Columns.Add("4");
            studentsData.Columns.Add("5");
            studentsData.Columns.Add("6");
            studentsData.Columns.Add("7");

            studentsData.Rows.Add(titolo);
            studentsData.Rows.Add("ALLEGATO DETERMINA");
            studentsData.Rows.Add(" ");
            studentsData.Rows.Add(" ");
            studentsData.Rows.Add("N.PROG.", "CODICE_FISCALE", "COGNOME", "NOME", "TOTALE LORDO", "ACCONTO PA", "IMPORTO NETTO");

            int progressivo = 1;
            double totaleLordo = 0;
            double totaleAcconto = 0;
            double totaleNetto = 0;
            foreach (Studente s in studentiDaGenerare)
            {
                studentsData.Rows.Add(progressivo, s.codFiscale, s.cognome, s.nome, s.importoDaPagareLordo.ToString().Replace(",", "."), s.importoAccontoPA.ToString().Replace(",", "."), s.importoDaPagare.ToString().Replace(",", "."));
                totaleLordo += s.importoDaPagareLordo;
                totaleAcconto += s.importoAccontoPA;
                totaleNetto += s.importoDaPagare;
                progressivo++;
            }
            studentsData.Rows.Add(" ");
            studentsData.Rows.Add(" ");
            studentsData.Rows.Add(" ", " ", " ", "TOTALE", Math.Round(totaleLordo, 2).ToString().Replace(",", "."), Math.Round(totaleAcconto, 2).ToString().Replace(",", "."), Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
            studentsData.Rows.Add(" ");
            studentsData.Rows.Add(" ");
            return studentsData;
        }

    }
}
