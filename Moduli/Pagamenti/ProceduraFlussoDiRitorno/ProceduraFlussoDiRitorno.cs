using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class ProceduraFlussoDiRitorno : BaseProcedure<ArgsProceduraFlussoDiRitorno>
    {
        private const int BatchSize = 1000;
        private const int BulkTimeoutSeconds = 600;

        private string selectedFileFlusso = "";
        private string selectedOldMandato = "";
        private string selectedTipoBando = "";

        private bool ignoraFlussi1 = false;

        private readonly List<StudenteRitorno> studenteRitornoList = new();
        private readonly List<StudenteRitorno> studentiScartati = new();

        // Log per riga (export Excel)
        private DataTable _flussoLog = new();
        private string _flussoLogExcelPath = "";

        public ProceduraFlussoDiRitorno(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraFlussoDiRitorno args)
        {
            using SqlTransaction sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                _masterForm.inProcedure = true;
                Logger.LogInfo(0, "Inizio lavorazione");

                selectedFileFlusso = args._selectedFileFlusso;
                selectedOldMandato = args._selectedImpegnoProvv;
                ignoraFlussi1 = args._ignoraFlussi1;

                Logger.LogInfo(10, "Creazione lista studenti + log per riga (Excel)");
                CreateStudentiList(); // include export excel in cartella flusso

                Logger.LogInfo(15, $"File log flusso: {_flussoLogExcelPath}");

                Logger.LogInfo(20, "Selezione codice movimento generale");
                SetCodMovimentoGenerale(CONNECTION, sqlTransaction);

                Logger.LogInfo(30, $"Aggiornamento tabella movimento elementare (batch {BatchSize})");
                UpdateMovimentoElementari(CONNECTION, sqlTransaction);

                Logger.LogInfo(40, "Aggiornamento tabella stati movimento contabile");
                UpdateStatiMovimentoContabile(CONNECTION, sqlTransaction);

                Logger.LogInfo(55, "Inserimento in pagamenti");
                InsertIntoPagamenti(CONNECTION, sqlTransaction);

                Logger.LogInfo(66, "Inserimento in reversali");
                InsertIntoReversali(CONNECTION, sqlTransaction);

                Logger.LogInfo(77, "Aggiornamento mandati");
                UpdateMandato(CONNECTION, sqlTransaction);

                Logger.LogInfo(82, "Inserimento messaggi");
                InsertMessaggio(CONNECTION, sqlTransaction);

                if (!ignoraFlussi1)
                {
                    Logger.LogInfo(88, "Annullamento pagamenti scartati");
                    Scartati(CONNECTION, sqlTransaction);
                }

                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    var result = MessageBox.Show(_masterForm, "continuare?", "cont", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No)
                    {
                        sqlTransaction.Rollback();
                        throw new Exception("Chiuso dall'utente");
                    }
                });

                _masterForm.inProcedure = false;
                Logger.LogInfo(100, "Fine lavorazione");
                Logger.LogInfo(100, $"Totale studenti lavorati: {studenteRitornoList.Count}");
                if (!ignoraFlussi1) Logger.LogInfo(100, $"Di cui scartati: {studentiScartati.Count}");

                sqlTransaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.LogError(100, ex.ToString());
                try { sqlTransaction.Rollback(); } catch { /* ignore */ }
                _masterForm.inProcedure = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // LOG PER RIGA (EXCEL)
        // ─────────────────────────────────────────────────────────────────────────────
        private static DataTable CreateFlussoLogTable()
        {
            var dt = new DataTable("LogFlusso");

            dt.Columns.Add("LineNo", typeof(int));
            dt.Columns.Add("Esito", typeof(string));              // OK | SKIP
            dt.Columns.Add("ErroreCodice", typeof(string));       // EMPTY_LINE, COLS_LT_15, CF_MISSING, DATE_INVALID, EXCEPTION
            dt.Columns.Add("ErroreDettaglio", typeof(string));    // dettaglio breve

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumMandatoFlusso", typeof(string));
            dt.Columns.Add("DataPagamentoRaw", typeof(string));
            dt.Columns.Add("DataPagamentoParsed", typeof(DateTime));
            dt.Columns.Add("NumReversale", typeof(string));
            dt.Columns.Add("ScartatoRaw", typeof(string));
            dt.Columns.Add("PagamentoScartato", typeof(bool));

            dt.Columns.Add("RawLine", typeof(string));

            return dt;
        }

        private static void AddFlussoLogRow(
            DataTable dt,
            int lineNo,
            string esito,
            string errCode,
            string errDetail,
            string cf,
            string numMandato,
            string dataPagRaw,
            DateTime? dataPagParsed,
            string numReversale,
            string scartatoRaw,
            bool? pagamentoScartato,
            string rawLine)
        {
            var r = dt.NewRow();

            r["LineNo"] = lineNo;
            r["Esito"] = esito ?? "";
            r["ErroreCodice"] = errCode ?? "";
            r["ErroreDettaglio"] = errDetail ?? "";

            r["CodFiscale"] = cf ?? "";
            r["NumMandatoFlusso"] = numMandato ?? "";
            r["DataPagamentoRaw"] = dataPagRaw ?? "";
            if (dataPagParsed.HasValue) r["DataPagamentoParsed"] = dataPagParsed.Value;
            r["NumReversale"] = numReversale ?? "";
            r["ScartatoRaw"] = scartatoRaw ?? "";
            if (pagamentoScartato.HasValue) r["PagamentoScartato"] = pagamentoScartato.Value;

            r["RawLine"] = rawLine ?? "";

            dt.Rows.Add(r);
        }

        private void ExportFlussoLogExcel()
        {
            var folder = Path.GetDirectoryName(selectedFileFlusso) ?? "";
            if (string.IsNullOrWhiteSpace(folder))
                folder = Environment.CurrentDirectory;

            var baseName = Path.GetFileNameWithoutExtension(selectedFileFlusso);
            var safeBase = string.IsNullOrWhiteSpace(baseName) ? "flusso" : baseName;

            var fileName = $"LOG_{safeBase}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            _flussoLogExcelPath = Utilities.ExportDataTableToExcel(_flussoLog, folder, includeHeaders: true, fileName: fileName);
            Logger.LogInfo(12, $"Creato file log flusso: {_flussoLogExcelPath}");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // PARSING FILE + LOG PER RIGA
        // ─────────────────────────────────────────────────────────────────────────────
        private void CreateStudentiList()
        {
            if (!File.Exists(selectedFileFlusso))
                throw new FileNotFoundException("File flusso non trovato", selectedFileFlusso);

            studenteRitornoList.Clear();
            studentiScartati.Clear();

            _flussoLog = CreateFlussoLogTable();

            int totalLines = 0;
            int ok = 0;
            int skipped = 0;

            using var sr = new StreamReader(selectedFileFlusso, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                totalLines++;
                int lineNo = totalLines;

                // default values per logging
                string cf = "";
                string numMandato = "";
                string dataPagRaw = "";
                DateTime? dataPagParsed = null;
                string numReversale = "";
                string scartatoRaw = "";
                bool? pagamentoScartato = null;

                try
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        skipped++;
                        AddFlussoLogRow(_flussoLog, lineNo, "SKIP", "EMPTY_LINE", "Riga vuota",
                            cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                        continue;
                    }

                    var data = line.Split(';');
                    if (data.Length < 15)
                    {
                        skipped++;
                        AddFlussoLogRow(_flussoLog, lineNo, "SKIP", "COLS_LT_15", $"Colonne insufficienti: {data.Length} (<15)",
                            cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                        continue;
                    }

                    // columns used: [1] CF, [10] numMandato, [11] dataPagamento, [12] numReversale, [14] scartato
                    cf = (data[1] ?? "").Trim().ToUpperInvariant();
                    numMandato = (data[10] ?? "").Trim();
                    dataPagRaw = (data[11] ?? "").Trim();
                    numReversale = (data[12] ?? "").Trim();
                    scartatoRaw = (data[14] ?? "").Trim();

                    if (string.IsNullOrWhiteSpace(cf))
                    {
                        skipped++;
                        AddFlussoLogRow(_flussoLog, lineNo, "SKIP", "CF_MISSING", "Codice fiscale mancante",
                            cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                        continue;
                    }

                    if (!DateTime.TryParseExact(dataPagRaw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtPag))
                    {
                        skipped++;
                        AddFlussoLogRow(_flussoLog, lineNo, "SKIP", "DATE_INVALID", $"Data pagamento non valida: '{dataPagRaw}'",
                            cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                        continue;
                    }

                    dataPagParsed = dtPag;
                    pagamentoScartato = (scartatoRaw == "1");

                    var studente = new StudenteRitorno(cf, numMandato, dtPag, numReversale, pagamentoScartato.Value);
                    studenteRitornoList.Add(studente);
                    if (pagamentoScartato.Value) studentiScartati.Add(studente);

                    ok++;
                    AddFlussoLogRow(_flussoLog, lineNo, "OK", "", "Riga valida",
                        cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                }
                catch (Exception exRow)
                {
                    skipped++;
                    AddFlussoLogRow(_flussoLog, lineNo, "SKIP", "EXCEPTION", exRow.Message,
                        cf, numMandato, dataPagRaw, dataPagParsed, numReversale, scartatoRaw, pagamentoScartato, line);
                }
            }

            Logger.LogInfo(10, $"Flusso letto: righe={totalLines}, OK={ok}, SKIP={skipped}, studenti_validi={studenteRitornoList.Count}, scartati={studentiScartati.Count}");

            // export excel nella cartella del flusso
            ExportFlussoLogExcel();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // MOVIMENTI: set codice movimento generale
        // ─────────────────────────────────────────────────────────────────────────────
        private void SetCodMovimentoGenerale(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string dataQuery = @"
                SELECT DISTINCT CODICE_MOVIMENTO, CODICE_FISCALE
                FROM MOVIMENTI_CONTABILI_ELEMENTARI
                WHERE CODICE_MOVIMENTO IN (
                    SELECT CODICE_MOVIMENTO
                    FROM MOVIMENTI_CONTABILI_GENERALI
                    WHERE cod_mandato LIKE @mandatoPrefix
                );";

            using SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction);
            readData.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");

            using SqlDataReader reader = readData.ExecuteReader();
            var byCf = studenteRitornoList.ToDictionary(s => s.codFiscale, s => s, StringComparer.OrdinalIgnoreCase);

            int found = 0;

            while (reader.Read())
            {
                string codFiscale = Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpperInvariant();
                if (byCf.TryGetValue(codFiscale, out var studente))
                {
                    string codMovimento = Utilities.SafeGetString(reader, "CODICE_MOVIMENTO");
                    studente.SetCodMovimentoGenerale(codMovimento);
                    found++;
                }
            }

            int missing = studenteRitornoList.Count - studenteRitornoList.Count(s => !string.IsNullOrWhiteSpace(s.codMovimentoGenerale));
            Logger.LogInfo(20, $"SetCodMovimentoGenerale: associati={found}, senza_movimento={missing}");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // MOVIMENTI ELEMENTARI (batch + temp join)
        // ─────────────────────────────────────────────────────────────────────────────
        private void UpdateMovimentoElementari(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            var studentiValidi = studenteRitornoList
                .Where(s => !string.IsNullOrWhiteSpace(s.codMovimentoGenerale))
                .ToList();

            int tot = studentiValidi.Count;
            int batches = (int)Math.Ceiling(tot / (double)BatchSize);

            Logger.LogInfo(30, $"UpdateMovimentoElementari: validi={tot}, batchSize={BatchSize}, batches={batches}");

            // Create reusable temp table once
            string createTemp = @"
                IF OBJECT_ID('tempdb..#BatchCFMov') IS NULL
                CREATE TABLE #BatchCFMov
                (
                    CodFiscale CHAR(16) COLLATE Latin1_General_CI_AS NOT NULL,
                    CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL,
                    NumReversale VARCHAR(20) COLLATE Latin1_General_CI_AS NULL
                );";
            using (var createCmd = new SqlCommand(createTemp, CONNECTION, sqlTransaction))
                createCmd.ExecuteNonQuery();

            int batchIndex = 0;
            int processed = 0;

            foreach (var batch in Chunk(studentiValidi, BatchSize))
            {
                batchIndex++;
                processed += batch.Count;

                Logger.LogInfo(30, $"UpdateMovimentoElementari: batch {batchIndex}/{batches} (righe={batch.Count}, processed={processed}/{tot})");

                var dt = new DataTable();
                dt.Columns.Add("CodFiscale", typeof(string));
                dt.Columns.Add("CodMovimentoGenerale", typeof(string));
                dt.Columns.Add("NumReversale", typeof(string));

                foreach (var s in batch)
                    dt.Rows.Add(s.codFiscale, s.codMovimentoGenerale, s.numReversale);

                using (var trunc = new SqlCommand("TRUNCATE TABLE #BatchCFMov;", CONNECTION, sqlTransaction))
                    trunc.ExecuteNonQuery();

                BulkInsertIntoSqlTable(CONNECTION, dt, "#BatchCFMov", sqlTransaction);

                // Update stato = '1'
                string updStato = @"
                    UPDATE MCE
                    SET MCE.stato = '1'
                    FROM MOVIMENTI_CONTABILI_ELEMENTARI AS MCE
                    INNER JOIN #BatchCFMov B
                        ON B.CodFiscale = MCE.codice_fiscale
                       AND B.CodMovimentoGenerale = MCE.codice_movimento;";
                using (var cmd = new SqlCommand(updStato, CONNECTION, sqlTransaction))
                    cmd.ExecuteNonQuery();

                // Update numero_reversale per segno = '0'
                string updReversale = @"
                    UPDATE MCE
                    SET MCE.numero_reversale = B.NumReversale
                    FROM MOVIMENTI_CONTABILI_ELEMENTARI AS MCE
                    INNER JOIN #BatchCFMov B
                        ON B.CodFiscale = MCE.codice_fiscale
                       AND B.CodMovimentoGenerale = MCE.codice_movimento
                    WHERE MCE.segno = '0';";
                using (var cmd = new SqlCommand(updReversale, CONNECTION, sqlTransaction))
                    cmd.ExecuteNonQuery();
            }

            Logger.LogInfo(30, $"UpdateMovimentoElementari: completato (processed={processed})");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // STATI MOVIMENTO CONTABILE (temp + bulk)
        // ─────────────────────────────────────────────────────────────────────────────
        private void UpdateStatiMovimentoContabile(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string createTempTable = @"
                IF OBJECT_ID('tempdb..#TempMovimentiContabili') IS NOT NULL DROP TABLE #TempMovimentiContabili;
                CREATE TABLE #TempMovimentiContabili
                (
                    CodFiscale CHAR(16) COLLATE Latin1_General_CI_AS,
                    DataEmissione DATETIME,
                    CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS
                );";
            using (var createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction))
                createCmd.ExecuteNonQuery();

            DataTable tempMovimentoContabili = CodMovimentoToDateAndCf(studenteRitornoList);
            BulkInsertIntoSqlTable(CONNECTION, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

            string sqlInsertStati = @"
                INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE 
                    (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO, NOTE_STATO)
                SELECT 
                    '1', mcg.CODICE_MOVIMENTO, tm.DataEmissione, mcg.UTENTE_VALIDAZIONE, mcg.NOTE_VALIDAZIONE_MOVIMENTO
                FROM #TempMovimentiContabili tm
                INNER JOIN MOVIMENTI_CONTABILI_GENERALI mcg
                    ON tm.CodMovimentoGenerale = mcg.CODICE_MOVIMENTO
                WHERE mcg.cod_mandato LIKE @mandatoPrefix
                  AND NOT EXISTS (
                      SELECT 1 
                      FROM STATI_DEL_MOVIMENTO_CONTABILE s
                      WHERE s.ID_STATO = '1' AND s.CODICE_MOVIMENTO = mcg.CODICE_MOVIMENTO
                  );";

            using (SqlCommand cmd = new(sqlInsertStati, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");
                cmd.ExecuteNonQuery();
            }

            using (var drop = new SqlCommand("DROP TABLE #TempMovimentiContabili;", CONNECTION, sqlTransaction))
                drop.ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // INSERT PAGAMENTI (temp + bulk)
        // ─────────────────────────────────────────────────────────────────────────────
        private void InsertIntoPagamenti(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string createTempTable = @"
                IF OBJECT_ID('tempdb..#TempPagamenti') IS NOT NULL DROP TABLE #TempPagamenti;
                CREATE TABLE #TempPagamenti
                (
                    CodFiscale CHAR(16) COLLATE Latin1_General_CI_AS,
                    DataEmissione DATETIME,
                    CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS,
                    NumMandato VARCHAR(20) COLLATE Latin1_General_CI_AS
                );";
            using (var createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction))
                createCmd.ExecuteNonQuery();

            DataTable pagamTable = PagamentiTable(studenteRitornoList);
            BulkInsertIntoSqlTable(CONNECTION, pagamTable, "#TempPagamenti", sqlTransaction);

            string sqlInsertPagam = @"
                INSERT INTO PAGAMENTI 
                    (Cod_tipo_pagam, Anno_accademico, Cod_mandato, Data_emissione_mandato, Num_domanda, 
                     Imp_pagato, Data_validita, Ese_finanziario, Note, Non_riscosso, Ritirato_azienda, Utente)
                SELECT 
                    mcg.id_causale_movimento_generale,
                    mce.anno_accademico,
                    tmp.NumMandato,
                    tmp.DataEmissione,
                    mce.Num_Domanda,
                    mce.Importo,
                    mcg.data_validita_movimento_generale,
                    mce.Anno_esercizio_finanziario,
                    mcg.note_validazione_movimento,
                    '0',
                    '0',
                    mcg.utente_validazione
                FROM MOVIMENTI_CONTABILI_GENERALI mcg
                INNER JOIN (
                    SELECT DISTINCT 
                        mce.ANNO_ACCADEMICO, 
                        mce.CODICE_MOVIMENTO, 
                        Domanda.Num_domanda, 
                        mce.ANNO_ESERCIZIO_FINANZIARIO,
                        mce.importo
                    FROM MOVIMENTI_CONTABILI_ELEMENTARI mce
                    INNER JOIN Domanda 
                        ON mce.ANNO_ACCADEMICO = Domanda.Anno_accademico 
                       AND mce.CODICE_FISCALE = Domanda.Cod_fiscale
                    INNER JOIN #TempPagamenti 
                        ON Domanda.Cod_fiscale = #TempPagamenti.CodFiscale 
                       AND mce.CODICE_MOVIMENTO = #TempPagamenti.CodMovimentoGenerale
                    WHERE mce.segno = '1' AND Domanda.Tipo_bando = 'LZ'
                ) AS mce
                    ON mcg.codice_movimento = mce.codice_movimento
                INNER JOIN #TempPagamenti tmp 
                    ON mcg.codice_movimento = tmp.CodMovimentoGenerale
                WHERE mcg.cod_mandato LIKE @mandatoPrefix;";

            using (SqlCommand cmd = new(sqlInsertPagam, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");
                cmd.ExecuteNonQuery();
            }

            using (var drop = new SqlCommand("DROP TABLE #TempPagamenti;", CONNECTION, sqlTransaction))
                drop.ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // INSERT REVERSALI (temp + bulk)
        // ─────────────────────────────────────────────────────────────────────────────
        private void InsertIntoReversali(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string createTempTable = @"
                IF OBJECT_ID('tempdb..#TempReversali') IS NOT NULL DROP TABLE #TempReversali;
                CREATE TABLE #TempReversali
                (
                    CodFiscale CHAR(16) COLLATE Latin1_General_CI_AS,
                    DataEmissione DATETIME,
                    CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS,
                    NumMandato VARCHAR(20) COLLATE Latin1_General_CI_AS
                );";
            using (var createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction))
                createCmd.ExecuteNonQuery();

            DataTable pagamTable = PagamentiTable(studenteRitornoList);
            BulkInsertIntoSqlTable(CONNECTION, pagamTable, "#TempReversali", sqlTransaction);

            string sqlInsertRev = @"
                INSERT INTO REVERSALI (
                    Cod_tipo_pagam, 
                    Anno_accademico, 
                    Data_validita, 
                    Num_domanda, 
                    Cod_reversale,
                    Note,
                    Num_reversale,
                    Importo,
                    Utente,
                    Ritirato_azienda,
                    cod_mandato)
                SELECT 
                    mcg.id_causale_movimento_generale, 
                    mce.anno_accademico, 
                    mce.data_inserimento,
                    domanda.num_domanda,
                    mce.id_causale as reversale,
                    mce.note_movimento_elementare,
                    mce.numero_reversale,
                    mce.importo,
                    mce.utente_movimento,
                    '0',
                    tm.numMandato
                FROM MOVIMENTI_CONTABILI_GENERALI mcg
                INNER JOIN MOVIMENTI_CONTABILI_ELEMENTARI mce 
                    ON mcg.codice_movimento = mce.codice_movimento
                INNER JOIN domanda 
                    ON mce.anno_accademico = domanda.anno_accademico 
                   AND mce.codice_fiscale = domanda.cod_fiscale
                INNER JOIN #TempReversali tm 
                    ON mce.codice_movimento = tm.codMovimentoGenerale 
                   AND mce.codice_fiscale = tm.codFiscale
                WHERE mce.segno = '0' 
                  AND Domanda.Tipo_bando = 'LZ' 
                  AND mcg.cod_mandato LIKE @mandatoPrefix;";

            using (SqlCommand cmd = new(sqlInsertRev, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");
                cmd.ExecuteNonQuery();
            }

            using (var drop = new SqlCommand("DROP TABLE #TempReversali;", CONNECTION, sqlTransaction))
                drop.ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // UPDATE MANDATO (temp + bulk)
        // ─────────────────────────────────────────────────────────────────────────────
        private void UpdateMandato(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string createTempTable = @"
                IF OBJECT_ID('tempdb..#TempMovimentiContabili') IS NOT NULL DROP TABLE #TempMovimentiContabili;
                CREATE TABLE #TempMovimentiContabili
                (
                    NumMandato VARCHAR(50) COLLATE Latin1_General_CI_AS,
                    CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS
                );";
            using (var createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction))
                createCmd.ExecuteNonQuery();

            DataTable tempMovimentoContabili = NumMandatoUpdate(studenteRitornoList);
            BulkInsertIntoSqlTable(CONNECTION, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

            string sqlUpdate = @"
                UPDATE mcg
                SET mcg.cod_mandato = tm.NumMandato
                FROM MOVIMENTI_CONTABILI_GENERALI mcg
                INNER JOIN #TempMovimentiContabili tm 
                    ON mcg.codice_movimento = tm.codMovimentoGenerale
                WHERE mcg.cod_mandato LIKE @mandatoPrefix;";
            using (SqlCommand cmd = new(sqlUpdate, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");
                cmd.ExecuteNonQuery();
            }

            using (var drop = new SqlCommand("DROP TABLE #TempMovimentiContabili;", CONNECTION, sqlTransaction))
                drop.ExecuteNonQuery();
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // MESSAGGI
        // ─────────────────────────────────────────────────────────────────────────────
        private void InsertMessaggio(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            string tipo = (selectedOldMandato?.Length ?? 0) >= 4 ? selectedOldMandato.Substring(0, 4) : selectedOldMandato ?? "";
            string sqlTipoPagam = "SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = @tipo";

            using SqlCommand cmd1 = new(sqlTipoPagam, CONNECTION, sqlTransaction);
            cmd1.Parameters.AddWithValue("@tipo", tipo);
            string pagamentoDescrizione = Convert.ToString(cmd1.ExecuteScalar()) ?? "il pagamento";

            string messaggioBase =
                $@"Gentile studente/ssa, il pagamento riguardante ''{pagamentoDescrizione}'' è avvenuto con successo. <br>Puoi trovare il dettaglio nella sezione pagamenti della tua area riservata";

            string messaggioPendolare =
                $@"Gentile studente/ssa, il pagamento relativo a ''{pagamentoDescrizione}'' è stato eseguito correttamente, ma è stato effettuato come pendolare poiché, al momento dell'estrazione, non risultavano soddisfatti i requisiti di fuori sede (anche se era presente un'istanza non ancora lavorata).<br>Puoi consultare il dettaglio del pagamento nella sezione Pagamenti della tua area riservata.";

            // recupera le NOTE_VALIDAZIONE_MOVIMENTO per i movimenti coinvolti (NO limite 2100 parametri)
            var noteByMovimento = GetNoteValidazionePerMovimento(CONNECTION, sqlTransaction);
            Logger.LogInfo(82, $"InsertMessaggio: note_validazione_lette={noteByMovimento.Count}");

            var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int duplicateCf = 0;
            int pendolareMsg = 0;

            foreach (var studente in studenteRitornoList)
            {
                if (messages.ContainsKey(studente.codFiscale))
                {
                    duplicateCf++;
                    continue;
                }

                string messaggioDaInviare = messaggioBase;

                if (!string.IsNullOrWhiteSpace(studente.codMovimentoGenerale) &&
                    noteByMovimento.TryGetValue(studente.codMovimentoGenerale, out var nota) &&
                    string.Equals(nota?.Trim(), "Pagamento effettuato come pendolare", StringComparison.OrdinalIgnoreCase))
                {
                    messaggioDaInviare = messaggioPendolare;
                    pendolareMsg++;
                }

                messages.Add(studente.codFiscale, messaggioDaInviare);
            }

            Logger.LogInfo(82, $"InsertMessaggio: CF_unici={messages.Count}, duplicati_saltati={duplicateCf}, msg_pendolare={pendolareMsg}");

            MessageUtils.InsertMessages(CONNECTION, sqlTransaction, messages);
        }

        // temp table + bulk (evita limite 2100 parametri)
        private Dictionary<string, string> GetNoteValidazionePerMovimento(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var movimenti = studenteRitornoList
                .Select(s => s.codMovimentoGenerale)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (movimenti.Count == 0)
                return result;

            const string tempName = "#TempMovNote";

            try
            {
                string createTemp = $@"
                    IF OBJECT_ID('tempdb..{tempName}') IS NOT NULL DROP TABLE {tempName};
                    CREATE TABLE {tempName}
                    (
                        CodMovimentoGenerale VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY
                    );";

                using (var createCmd = new SqlCommand(createTemp, CONNECTION, sqlTransaction))
                    createCmd.ExecuteNonQuery();

                var dt = new DataTable();
                dt.Columns.Add("CodMovimentoGenerale", typeof(string));
                foreach (var m in movimenti)
                    dt.Rows.Add(m);

                BulkInsertIntoSqlTable(CONNECTION, dt, tempName, sqlTransaction);

                string sql = $@"
                    SELECT mcg.CODICE_MOVIMENTO, mcg.NOTE_VALIDAZIONE_MOVIMENTO
                    FROM MOVIMENTI_CONTABILI_GENERALI mcg
                    INNER JOIN {tempName} t
                        ON t.CodMovimentoGenerale = mcg.CODICE_MOVIMENTO;";

                using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction);
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string codMov = Utilities.SafeGetString(reader, "CODICE_MOVIMENTO");
                    string nota = Utilities.SafeGetString(reader, "NOTE_VALIDAZIONE_MOVIMENTO");

                    if (!string.IsNullOrWhiteSpace(codMov) && !result.ContainsKey(codMov))
                        result.Add(codMov, nota);
                }

                return result;
            }
            finally
            {
                try
                {
                    using var drop = new SqlCommand($"IF OBJECT_ID('tempdb..{tempName}') IS NOT NULL DROP TABLE {tempName};", CONNECTION, sqlTransaction);
                    drop.ExecuteNonQuery();
                }
                catch { /* ignore */ }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // SCARTATI
        // ─────────────────────────────────────────────────────────────────────────────
        private void Scartati(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            foreach (var studente in studentiScartati)
            {
                using SqlCommand cmd = new("SP_annulla_pagamento", CONNECTION, sqlTransaction)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@NumMandato", studente.numMandatoFlusso ?? "");
                cmd.Parameters.AddWithValue("@esercizio_finanziario", DateTime.Now.Year);
                cmd.Parameters.AddWithValue("@cod_fiscale", studente.codFiscale);
                cmd.ExecuteNonQuery();
            }

            Logger.LogInfo(88, $"Scartati: annullati={studentiScartati.Count}");
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // TABLE BUILDERS (no warning per riga: log massivo)
        // ─────────────────────────────────────────────────────────────────────────────
        private DataTable CodMovimentoToDateAndCf(IEnumerable<StudenteRitorno> list)
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("DataEmissione", typeof(DateTime));
            dt.Columns.Add("CodMovimentoGenerale", typeof(string));

            var warn = new MassWarningLog("CodMovimentoToDateAndCf", sampleLimit: 10);

            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.codMovimentoGenerale))
                {
                    warn.Add("CodMovimentoGenerale mancante", $"CF={s.codFiscale} mandato={s.numMandatoFlusso}");
                    continue;
                }
                dt.Rows.Add(s.codFiscale, s.dataPagamento, s.codMovimentoGenerale);
            }

            warn.Flush();
            Logger.LogInfo(40, $"CodMovimentoToDateAndCf: righe_out={dt.Rows.Count}");
            return dt;
        }

        private DataTable PagamentiTable(IEnumerable<StudenteRitorno> list)
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("DataEmissione", typeof(DateTime));
            table.Columns.Add("CodMovimentoGenerale", typeof(string));
            table.Columns.Add("NumMandato", typeof(string));

            var warn = new MassWarningLog("PagamentiTable", sampleLimit: 10);

            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.codMovimentoGenerale))
                {
                    warn.Add("CodMovimentoGenerale mancante", $"CF={s.codFiscale} mandato={s.numMandatoFlusso}");
                    continue;
                }
                table.Rows.Add(s.codFiscale, s.dataPagamento, s.codMovimentoGenerale, s.numMandatoFlusso);
            }

            warn.Flush();
            Logger.LogInfo(55, $"PagamentiTable: righe_out={table.Rows.Count}");
            return table;
        }

        private DataTable NumMandatoUpdate(List<StudenteRitorno> list)
        {
            var table = new DataTable();
            table.Columns.Add("NumMandato", typeof(string));
            table.Columns.Add("CodMovimentoGenerale", typeof(string));

            var warn = new MassWarningLog("NumMandatoUpdate", sampleLimit: 10);

            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.codMovimentoGenerale))
                {
                    warn.Add("CodMovimentoGenerale mancante", $"CF={s.codFiscale} mandato={s.numMandatoFlusso}");
                    continue;
                }
                table.Rows.Add(s.numMandatoFlusso, s.codMovimentoGenerale);
            }

            warn.Flush();
            Logger.LogInfo(77, $"NumMandatoUpdate: righe_out={table.Rows.Count}");
            return table;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // UTILS
        // ─────────────────────────────────────────────────────────────────────────────
        private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
                yield return source.Skip(i).Take(size).ToList();
        }

        private static void BulkInsertIntoSqlTable(SqlConnection CONNECTION, DataTable table, string destinationTable, SqlTransaction sqlTransaction)
        {
            using var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction)
            {
                DestinationTableName = destinationTable,
                BatchSize = table.Rows.Count > 0 ? Math.Min(table.Rows.Count, BatchSize) : 0,
                BulkCopyTimeout = BulkTimeoutSeconds
            };
            bulkCopy.WriteToServer(table);
        }
    }

    internal sealed class MassWarningLog
    {
        private readonly string _scope;
        private readonly int _sampleLimit;
        private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _samples = new(StringComparer.OrdinalIgnoreCase);

        public MassWarningLog(string scope, int sampleLimit = 20)
        {
            _scope = scope;
            _sampleLimit = Math.Max(0, sampleLimit);
        }

        public void Add(string key, string sampleLine = "")
        {
            if (!_counters.TryGetValue(key, out var n)) n = 0;
            _counters[key] = n + 1;

            if (_sampleLimit == 0) return;
            if (string.IsNullOrWhiteSpace(sampleLine)) return;

            if (!_samples.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _samples[key] = list;
            }

            if (list.Count < _sampleLimit)
                list.Add(sampleLine);
        }

        public void Flush()
        {
            if (_counters.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine($"[{_scope}] Warning massivo: {_counters.Values.Sum()} occorrenze, {_counters.Count} categorie.");

            foreach (var kv in _counters.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"- {kv.Key}: {kv.Value}");

                if (_samples.TryGetValue(kv.Key, out var list) && list.Count > 0)
                {
                    foreach (var s in list)
                        sb.AppendLine($"    • {s}");
                }
            }

            Logger.LogWarning(null, sb.ToString());
        }
    }

    public class StudenteRitorno
    {
        public string codFiscale { get; private set; }
        public string numMandatoFlusso { get; private set; }
        public DateTime dataPagamento { get; private set; }
        public string numReversale { get; private set; }
        public bool pagamentoScartato { get; private set; }
        public string codMovimentoGenerale { get; private set; }

        public StudenteRitorno(string codFiscale, string numMandatoFlusso, DateTime dataPagamento, string numReversale, bool pagamentoScartato)
        {
            this.codFiscale = codFiscale ?? "";
            this.numMandatoFlusso = numMandatoFlusso ?? "";
            this.dataPagamento = dataPagamento;
            this.numReversale = numReversale ?? "";
            this.pagamentoScartato = pagamentoScartato;
            codMovimentoGenerale = "";
        }

        public void SetCodMovimentoGenerale(string codMovimentoGenerale)
        {
            this.codMovimentoGenerale = codMovimentoGenerale ?? "";
        }
    }
}
