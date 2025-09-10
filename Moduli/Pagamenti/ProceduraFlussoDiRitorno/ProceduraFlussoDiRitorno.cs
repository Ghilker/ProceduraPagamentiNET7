using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class ProceduraFlussoDiRitorno : BaseProcedure<ArgsProceduraFlussoDiRitorno>
    {
        private const int BatchSize = 1000;
        private const int BulkTimeoutSeconds = 600;

        string selectedFileFlusso = "";
        string selectedOldMandato = "";
        string selectedTipoBando = "";

        bool ignoraFlussi1 = false;

        readonly List<StudenteRitorno> studenteRitornoList = new();
        readonly List<StudenteRitorno> studentiScartati = new();

        public ProceduraFlussoDiRitorno(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraFlussoDiRitorno args)
        {
            using SqlTransaction sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                _masterForm.inProcedure = true;
                Logger.LogInfo(0, $"Inizio lavorazione");

                selectedFileFlusso = args._selectedFileFlusso;
                selectedOldMandato = args._selectedImpegnoProvv;
                ignoraFlussi1 = args._ignoraFlussi1;

                Logger.LogInfo(10, $"Creazione lista studenti");
                CreateStudentiList();

                Logger.LogInfo(20, $"Selezione codice movimento generale");
                SetCodMovimentoGenerale(CONNECTION, sqlTransaction);

                Logger.LogInfo(30, $"Aggiornamento tabella movimento elementare (batch {BatchSize})");
                UpdateMovimentoElementari(CONNECTION, sqlTransaction);

                Logger.LogInfo(40, $"Aggiornamento tabella stati movimento contabile");
                UpdateStatiMovimentoContabile(CONNECTION, sqlTransaction);

                Logger.LogInfo(55, $"Inserimento in pagamenti");
                InsertIntoPagamenti(CONNECTION, sqlTransaction);

                Logger.LogInfo(66, $"Inserimento in reversali");
                InsertIntoReversali(CONNECTION, sqlTransaction);

                Logger.LogInfo(77, $"Aggiornamento mandati");
                UpdateMandato(CONNECTION, sqlTransaction);

                InsertMessaggio(CONNECTION, sqlTransaction);

                if (!ignoraFlussi1)
                {
                    Logger.LogInfo(88, $"Annullamento pagamenti scartati");
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
                Logger.LogInfo(100, $"Fine lavorazione");
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

        private void CreateStudentiList()
        {
            try
            {
                if (!File.Exists(selectedFileFlusso))
                    throw new FileNotFoundException("File flusso non trovato", selectedFileFlusso);

                using var sr = new StreamReader(selectedFileFlusso, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

                string? line;
                int lineNo = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var data = line.Split(';');
                    if (data.Length < 15)
                    {
                        Logger.LogWarning(null, $"Riga {lineNo}: colonne insufficienti ({data.Length}). Riga saltata.");
                        continue;
                    }

                    // columns used: [1] CF, [10] numMandato, [11] dataPagamento, [12] numReversale, [14] scartato
                    var cf = data[1]?.Trim().ToUpperInvariant();
                    var numMandato = data[10]?.Trim();
                    var dataPagStr = data[11]?.Trim();
                    var numReversale = data[12]?.Trim();
                    var scartatoStr = data[14]?.Trim();

                    if (string.IsNullOrWhiteSpace(cf))
                    {
                        Logger.LogWarning(null, $"Riga {lineNo}: Codice fiscale mancante. Riga saltata.");
                        continue;
                    }

                    if (!DateTime.TryParseExact(dataPagStr, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out var dataPagamento))
                    {
                        Logger.LogWarning(null, $"Riga {lineNo}: Data pagamento non valida '{dataPagStr}'. Riga saltata.");
                        continue;
                    }

                    bool pagamentoScartato = scartatoStr == "1";

                    var studente = new StudenteRitorno(cf, numMandato ?? "", dataPagamento, numReversale ?? "", pagamentoScartato);
                    studenteRitornoList.Add(studente);
                    if (pagamentoScartato) studentiScartati.Add(studente);
                }
            }
            catch { throw; }
        }

        private void SetCodMovimentoGenerale(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string dataQuery = @"
                    SELECT DISTINCT CODICE_MOVIMENTO, CODICE_FISCALE
	                FROM MOVIMENTI_CONTABILI_ELEMENTARI
	                WHERE CODICE_MOVIMENTO IN (
                        SELECT CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI WHERE cod_mandato LIKE @mandatoPrefix
                    );";

                using SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction);
                readData.Parameters.AddWithValue("@mandatoPrefix", $"{selectedOldMandato}%");

                using SqlDataReader reader = readData.ExecuteReader();
                var byCf = studenteRitornoList.ToDictionary(s => s.codFiscale, s => s, StringComparer.OrdinalIgnoreCase);

                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpperInvariant();
                    if (byCf.TryGetValue(codFiscale, out var studente))
                    {
                        string codMovimento = Utilities.SafeGetString(reader, "CODICE_MOVIMENTO");
                        studente.SetCodMovimentoGenerale(codMovimento);
                    }
                }
            }
            catch { throw; }
        }

        private void UpdateMovimentoElementari(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                var studentiValidi = studenteRitornoList
                    .Where(s => !string.IsNullOrWhiteSpace(s.codMovimentoGenerale))
                    .ToList();

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

                foreach (var batch in Chunk(studentiValidi, BatchSize))
                {
                    // Prepare DataTable for this batch
                    var dt = new DataTable();
                    dt.Columns.Add("CodFiscale", typeof(string));
                    dt.Columns.Add("CodMovimentoGenerale", typeof(string));
                    dt.Columns.Add("NumReversale", typeof(string));

                    foreach (var s in batch)
                        dt.Rows.Add(s.codFiscale, s.codMovimentoGenerale, s.numReversale);

                    // TRUNCATE + bulk insert
                    using (var trunc = new SqlCommand("TRUNCATE TABLE #BatchCFMov;", CONNECTION, sqlTransaction))
                        trunc.ExecuteNonQuery();

                    BulkInsertIntoSqlTable(CONNECTION, dt, "#BatchCFMov", sqlTransaction);

                    // 1) Update stato = '1' via join (no giant IN)
                    string updStato = @"
                        UPDATE MCE
                        SET MCE.stato = '1'
                        FROM MOVIMENTI_CONTABILI_ELEMENTARI AS MCE
                        INNER JOIN #BatchCFMov B
                            ON B.CodFiscale = MCE.codice_fiscale
                           AND B.CodMovimentoGenerale = MCE.codice_movimento;";
                    using (var cmd = new SqlCommand(updStato, CONNECTION, sqlTransaction))
                        cmd.ExecuteNonQuery();

                    // 2) Update numero_reversale for segno = '0'
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
            }
            catch { throw; }
        }

        private void UpdateStatiMovimentoContabile(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
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

                // Insert only if not already present in STATI_DEL_MOVIMENTO_CONTABILE for (ID_STATO=1, CODICE_MOVIMENTO)
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
            catch { throw; }
        }

        private void InsertIntoPagamenti(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
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
            catch { throw; }
        }

        private void InsertIntoReversali(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
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
            catch { throw; }
        }

        private void UpdateMandato(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
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
            catch { throw; }
        }

        private void InsertMessaggio(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string tipo = (selectedOldMandato?.Length ?? 0) >= 4 ? selectedOldMandato.Substring(0, 4) : selectedOldMandato ?? "";
                string sqlTipoPagam = "SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = @tipo";
                using SqlCommand cmd1 = new(sqlTipoPagam, CONNECTION, sqlTransaction);
                cmd1.Parameters.AddWithValue("@tipo", tipo);
                string pagamentoDescrizione = Convert.ToString(cmd1.ExecuteScalar()) ?? "il pagamento";

                string messaggio = $@"Gentile studente/ssa, il pagamento riguardante ''{pagamentoDescrizione}'' è avvenuto con successo. <br>Puoi trovare il dettaglio nella sezione storico esiti pagamenti della tua area riservata";

                var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var studente in studenteRitornoList)
                {
                    if (!messages.ContainsKey(studente.codFiscale))
                        messages.Add(studente.codFiscale, messaggio);
                }

                MessageUtils.InsertMessages(CONNECTION, sqlTransaction, messages);
            }
            catch { throw; }
        }

        private void Scartati(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
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
            }
            catch { throw; }
        }

        private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
                yield return source.Skip(i).Take(size).ToList();
        }

        DataTable CodMovimentoToDateAndCf(IEnumerable<StudenteRitorno> studenteRitornoList)
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("DataEmissione", typeof(DateTime));
            dt.Columns.Add("CodMovimentoGenerale", typeof(string));

            foreach (var studente in studenteRitornoList)
            {
                if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                {
                    Logger.LogWarning(null, $"Studente {studente.codFiscale} ha il codice movimento vuoto/nullo!");
                    continue;
                }
                dt.Rows.Add(studente.codFiscale, studente.dataPagamento, studente.codMovimentoGenerale);
            }
            return dt;
        }

        DataTable NumReversaleToCodFiscale(IEnumerable<StudenteRitorno> studenteRitornoList)
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("NumReversale", typeof(string));

            foreach (var studente in studenteRitornoList)
            {
                if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                {
                    Logger.LogWarning(null, $"Studente {studente.codFiscale} ha il codice movimento vuoto/nullo!");
                    continue;
                }
                table.Rows.Add(studente.codFiscale, studente.numReversale);
            }
            return table;
        }

        DataTable PagamentiTable(IEnumerable<StudenteRitorno> studenteRitornoList)
        {
            var table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("DataEmissione", typeof(DateTime));
            table.Columns.Add("CodMovimentoGenerale", typeof(string));
            table.Columns.Add("NumMandato", typeof(string));

            foreach (var studente in studenteRitornoList)
            {
                if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                {
                    Logger.LogWarning(null, $"Studente {studente.codFiscale} ha il codice movimento vuoto/nullo!");
                    continue;
                }
                table.Rows.Add(studente.codFiscale, studente.dataPagamento, studente.codMovimentoGenerale, studente.numMandatoFlusso);
            }
            return table;
        }

        private DataTable NumMandatoUpdate(List<StudenteRitorno> studenteRitornoList)
        {
            var table = new DataTable();
            table.Columns.Add("NumMandato", typeof(string));
            table.Columns.Add("CodMovimentoGenerale", typeof(string));

            foreach (var studente in studenteRitornoList)
            {
                if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                {
                    Logger.LogWarning(null, $"Studente {studente.codFiscale} ha il codice movimento vuoto/nullo!");
                    continue;
                }
                table.Rows.Add(studente.numMandatoFlusso, studente.codMovimentoGenerale);
            }
            return table;
        }

        static void BulkInsertIntoSqlTable(SqlConnection CONNECTION, DataTable table, string destinationTable, SqlTransaction sqlTransaction)
        {
            try
            {
                using var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction)
                {
                    DestinationTableName = destinationTable,
                    BatchSize = table.Rows.Count > 0 ? Math.Min(table.Rows.Count, BatchSize) : 0,
                    BulkCopyTimeout = BulkTimeoutSeconds
                };
                bulkCopy.WriteToServer(table);
            }
            catch { throw; }
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
