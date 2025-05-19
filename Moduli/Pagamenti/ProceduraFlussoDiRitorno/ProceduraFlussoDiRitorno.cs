using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ProcedureNet7
{
    internal class ProceduraFlussoDiRitorno : BaseProcedure<ArgsProceduraFlussoDiRitorno>
    {
        string selectedFileFlusso = "";
        string selectedOldMandato = "";
        string selectedTipoBando = "";

        bool ignoraFlussi1 = false;

        List<StudenteRitorno> studenteRitornoList = new List<StudenteRitorno>();
        List<StudenteRitorno> studentiScartati = new List<StudenteRitorno>();

        public ProceduraFlussoDiRitorno(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraFlussoDiRitorno args)
        {
            SqlTransaction sqlTransaction = CONNECTION.BeginTransaction();
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
                Logger.LogInfo(30, $"Aggiornamento tabella movimento elementare");
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
                    DialogResult result = MessageBox.Show(_masterForm, "continuare?", "cont", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No)
                    {
                        sqlTransaction.Rollback();
                        throw new Exception("Chiuso dall'utente");
                    }
                });

                _masterForm.inProcedure = false;
                Logger.LogInfo(100, $"Fine lavorazione");
                Logger.LogInfo(100, $"Totale studenti lavorati: {studenteRitornoList.Count}");
                if (!ignoraFlussi1)
                {
                    Logger.LogInfo(100, $"Di cui scartati: {studentiScartati.Count}");
                }
                sqlTransaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.LogError(100, ex.Message);
                sqlTransaction.Rollback();
                _masterForm.inProcedure = false;
            }
        }

        private void CreateStudentiList()
        {
            try
            {
                string[] lines = File.ReadAllLines(selectedFileFlusso);

                foreach (var line in lines)
                {
                    var data = line.Split(';');

                    if (data.Length >= 15)
                    {
                        StudenteRitorno studente = new StudenteRitorno(
                            data[1].ToString().ToUpper(),
                            data[10].ToString(),
                            DateTime.ParseExact(data[11], "dd/MM/yyyy", CultureInfo.InvariantCulture),
                            data[12].ToString(),
                            data[14].ToString() == "1" ? true : false
                            );

                        studenteRitornoList.Add(studente);
                        if (studente.pagamentoScartato)
                        {
                            studentiScartati.Add(studente);
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }
        void SetCodMovimentoGenerale(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string dataQuery = @$"
                    SELECT DISTINCT  CODICE_MOVIMENTO, CODICE_FISCALE
	                FROM MOVIMENTI_CONTABILI_ELEMENTARI
	                WHERE CODICE_MOVIMENTO IN (SELECT CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI WHERE cod_mandato like '{selectedOldMandato}%')
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpper();
                        StudenteRitorno? studente = studenteRitornoList.FirstOrDefault(s => s.codFiscale == codFiscale);
                        if (studente != null)
                        {
                            string codMovimento = Utilities.SafeGetString(reader, "CODICE_MOVIMENTO");
                            studente.SetCodMovimentoGenerale(codMovimento);
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }
        private void UpdateMovimentoElementari(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                List<string> cfStudenti = new List<string>();
                List<string> codMovimenti = new List<string>();
                foreach (StudenteRitorno studente in studenteRitornoList)
                {
                    if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                    {
                        continue;
                    }
                    cfStudenti.Add(studente.codFiscale);
                    codMovimenti.Add(studente.codMovimentoGenerale);
                }

                string listaCF = string.Join(", ", cfStudenti.Select(cf => $"'{cf}'"));
                string listaCodMov = string.Join(", ", codMovimenti.Select(cm => $"{cm}"));

                string sqlUpdateAll = $@"
                    UPDATE MOVIMENTI_CONTABILI_ELEMENTARI SET stato='1'
                    WHERE codice_fiscale IN ({listaCF}) AND codice_movimento IN ({listaCodMov});
                    ";
                SqlCommand updateAllCmd = new(sqlUpdateAll, CONNECTION, sqlTransaction);
                updateAllCmd.ExecuteNonQuery();

                string createTempTable = @"
                    CREATE TABLE #TempStudenteMappings
                    (
                        CodFiscale CHAR(16),
                        NumReversale VARCHAR(20)
                    );
                    ";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempStudentMappingTable = NumReversaleToCodFiscale(studenteRitornoList);
                BulkInsertIntoSqlTable(CONNECTION, tempStudentMappingTable, "#TempStudenteMappings", sqlTransaction);

                string sqlUpdateReversali = $@"
                        UPDATE MOVIMENTI_CONTABILI_ELEMENTARI
                        SET MOVIMENTI_CONTABILI_ELEMENTARI.numero_reversale = #TempStudenteMappings.NumReversale
                        FROM MOVIMENTI_CONTABILI_ELEMENTARI
                        INNER JOIN #TempStudenteMappings ON MOVIMENTI_CONTABILI_ELEMENTARI.codice_fiscale = #TempStudenteMappings.CodFiscale
                        WHERE MOVIMENTI_CONTABILI_ELEMENTARI.Codice_movimento IN ({listaCodMov}) AND MOVIMENTI_CONTABILI_ELEMENTARI.segno = '0';
                        ";

                SqlCommand updateReversaliCmd = new(sqlUpdateReversali, CONNECTION, sqlTransaction);
                updateReversaliCmd.ExecuteNonQuery();

                string dropTable = "DROP TABLE #TempStudenteMappings";

                SqlCommand dropTableCmd = new(dropTable, CONNECTION, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();

            }
            catch
            {
                throw;
            }
        }
        private void UpdateStatiMovimentoContabile(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string createTempTable = @"
                        CREATE TABLE #TempMovimentiContabili
                        (
                            CodFiscale CHAR(16),
                            DataEmissione DATETIME,
                            CodMovimentoGenerale VARCHAR(50)
                        );
                        ";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempMovimentoContabili = CodMovimentoToDateAndCf(studenteRitornoList);
                BulkInsertIntoSqlTable(CONNECTION, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

                string sqlInsertStati = $@"
                INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO, NOTE_STATO)
                SELECT '1', mcg.CODICE_MOVIMENTO, tm.DataEmissione, mcg.UTENTE_VALIDAZIONE, mcg.NOTE_VALIDAZIONE_MOVIMENTO 
                
                FROM STATI_DEL_MOVIMENTO_CONTABILE as stm INNER JOIN
                #TempMovimentiContabili as tm ON stm.codice_movimento = tm.CodMovimentoGenerale INNER JOIN
                MOVIMENTI_CONTABILI_GENERALI AS mcg ON tm.CodMovimentoGenerale = mcg.CODICE_MOVIMENTO

                WHERE mcg.cod_mandato like '{selectedOldMandato}%';
                ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertStati, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }
                string dropTable = "DROP TABLE #TempMovimentiContabili";

                SqlCommand dropTableCmd = new(dropTable, CONNECTION, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();

            }
            catch
            {
                throw;
            }
        }
        private void InsertIntoPagamenti(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string createTempTable = @"
                        CREATE TABLE #TempPagamenti
                        (
                            CodFiscale CHAR(16),
                            DataEmissione DATETIME,
                            CodMovimentoGenerale VARCHAR(50),
                            NumMandato VARCHAR(20)
                        );
                        ";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable pagamTable = PagamentiTable(studenteRitornoList);
                BulkInsertIntoSqlTable(CONNECTION, pagamTable, "#TempPagamenti", sqlTransaction);

                string sqlInsertPagam = $@"
                        INSERT INTO PAGAMENTI (Cod_tipo_pagam, Anno_accademico, Cod_mandato, Data_emissione_mandato, Num_domanda, Imp_pagato, Data_validita, Ese_finanziario, Note, Non_riscosso, Ritirato_azienda, Utente)
                        SELECT mcg.id_causale_movimento_generale, mce.anno_accademico, tmp.NumMandato, tmp.DataEmissione, mce.Num_Domanda, mce.Importo, mcg.data_validita_movimento_generale, mce.Anno_esercizio_finanziario, mcg.note_validazione_movimento, '0' as Non_riscosso, '0' as ritirato_azienda, mcg.utente_validazione
                        FROM MOVIMENTI_CONTABILI_GENERALI as mcg INNER JOIN
                            (SELECT DISTINCT 
                                mce.ANNO_ACCADEMICO, mce.CODICE_MOVIMENTO, Domanda.Num_domanda, mce.ANNO_ESERCIZIO_FINANZIARIO,importo
                            FROM MOVIMENTI_CONTABILI_ELEMENTARI as mce INNER JOIN
                                Domanda ON mce.ANNO_ACCADEMICO = Domanda.Anno_accademico AND mce.CODICE_FISCALE = Domanda.Cod_fiscale INNER JOIN
                                #TempPagamenti ON Domanda.Cod_fiscale = #TempPagamenti.CodFiscale AND mce.CODICE_MOVIMENTO = #TempPagamenti.CodMovimentoGenerale
                            WHERE segno='1' AND Domanda.Tipo_bando = 'LZ') 
                            as mce ON mcg.codice_movimento = mce.codice_movimento INNER JOIN
                        #TempPagamenti as tmp ON mcg.codice_movimento = tmp.CodMovimentoGenerale
                        WHERE mcg.cod_mandato like '{selectedOldMandato}%'
                        ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertPagam, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                string dropTable = "DROP TABLE #TempPagamenti";

                SqlCommand dropTableCmd = new(dropTable, CONNECTION, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();
            }
            catch
            {
                throw;
            }
        }
        private void InsertIntoReversali(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string createTempTable = @"
                        CREATE TABLE #TempReversali
                        (
                            CodFiscale CHAR(16),
                            DataEmissione DATETIME,
                            CodMovimentoGenerale VARCHAR(50),
                            NumMandato VARCHAR(20)
                        );
                        ";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable pagamTable = PagamentiTable(studenteRitornoList);
                BulkInsertIntoSqlTable(CONNECTION, pagamTable, "#TempReversali", sqlTransaction);

                string sqlInsertPagam = $@"
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
                            '0' as ritirato_azienda,
                            tm.numMandato
                        FROM 
                            MOVIMENTI_CONTABILI_GENERALI as mcg INNER JOIN
                            MOVIMENTI_CONTABILI_ELEMENTARI as mce ON mcg.codice_movimento = mce.codice_movimento INNER JOIN
                            domanda ON mce.anno_accademico = domanda.anno_accademico AND mce.codice_fiscale = domanda.cod_fiscale INNER JOIN
                            #TempReversali as tm ON mce.codice_movimento = tm.codMovimentoGenerale AND mce.codice_fiscale = tm.codFiscale
                        WHERE mce.segno='0' AND Domanda.Tipo_bando = 'LZ' and mcg.cod_mandato like '{selectedOldMandato}%'

                        ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertPagam, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                string dropTable = "DROP TABLE #TempReversali";
                SqlCommand dropTableCmd = new(dropTable, CONNECTION, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();
            }
            catch
            {
                throw;
            }
        }
        private void UpdateMandato(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string createTempTable = @"
                        CREATE TABLE #TempMovimentiContabili
                        (
                            NumMandato VARCHAR(50),
                            CodMovimentoGenerale VARCHAR(50)
                        );
                        ";
                SqlCommand createCmd = new(createTempTable, CONNECTION, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempMovimentoContabili = NumMandatoUpdate(studenteRitornoList);
                BulkInsertIntoSqlTable(CONNECTION, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

                string sqlInsertStati = $@"
                UPDATE MOVIMENTI_CONTABILI_GENERALI
                SET cod_mandato = tm.NumMandato

                FROM MOVIMENTI_CONTABILI_GENERALI INNER JOIN
                #TempMovimentiContabili as tm ON MOVIMENTI_CONTABILI_GENERALI.codice_movimento = tm.codMovimentoGenerale

                WHERE MOVIMENTI_CONTABILI_GENERALI.cod_mandato like '{selectedOldMandato}%';
                ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertStati, CONNECTION, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                string dropTable = "DROP TABLE #TempMovimentiContabili";
                SqlCommand dropTableCmd = new(dropTable, CONNECTION, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();
            }
            catch
            {
                throw;
            }
        }

        private void InsertMessaggio(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{selectedOldMandato.Substring(0, 4)}'";
                SqlCommand cmd1 = new(sqlTipoPagam, CONNECTION, sqlTransaction);
                string pagamentoDescrizione = (string)cmd1.ExecuteScalar();

                string messaggio = @$"Gentile studente/ssa, il pagamento riguardante ''{pagamentoDescrizione}'' è avvenuto con successo. <br>Puoi trovare il dettaglio nella sezione storico esiti pagamenti della tua area riservata";
                Dictionary<string, string> messages = new();
                foreach (StudenteRitorno studente in studenteRitornoList)
                {

                    if (messages.ContainsKey(studente.codFiscale))
                    {
                        continue;
                    }

                    messages.Add(studente.codFiscale, messaggio);
                }

                MessageUtils.InsertMessages(CONNECTION, sqlTransaction, messages);
            }
            catch
            {
                throw;
            }
        }

        private void Scartati(SqlConnection CONNECTION, SqlTransaction sqlTransaction)
        {
            try
            {
                foreach (StudenteRitorno studente in studentiScartati)
                {
                    string sqlScartati = "SP_annulla_pagamento";

                    using (SqlCommand cmd = new SqlCommand(sqlScartati, CONNECTION, sqlTransaction))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;

                        cmd.Parameters.AddWithValue("@NumMandato", studente.numMandatoFlusso);
                        cmd.Parameters.AddWithValue("@esercizio_finanziario", DateTime.Now.Year);
                        cmd.Parameters.AddWithValue("@cod_fiscale", studente.codFiscale);

                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch
            {
                throw;
            }
        }

        DataTable CodMovimentoToDateAndCf(IEnumerable<StudenteRitorno> studenteRitornoList)
        {
            DataTable dt = new DataTable();
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
            DataTable table = new DataTable();
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
            DataTable table = new DataTable();
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

        DataTable MessaggioTable(IEnumerable<StudenteRitorno> studenteRitornoList)
        {
            DataTable table = new DataTable();
            table.Columns.Add("CodFiscale", typeof(string));

            foreach (var studente in studenteRitornoList)
            {
                if (string.IsNullOrWhiteSpace(studente.codMovimentoGenerale))
                {
                    Logger.LogWarning(null, $"Studente {studente.codFiscale} ha il codice movimento vuoto/nullo!");
                    continue;
                }

                if (studente.pagamentoScartato)
                {
                    continue;
                }

                table.Rows.Add(studente.codFiscale);
            }

            return table;
        }
        private DataTable NumMandatoUpdate(List<StudenteRitorno> studenteRitornoList)
        {
            DataTable table = new DataTable();
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

        static void BulkInsertIntoSqlTable(SqlConnection CONNECTION, DataTable studentMappingsTable, string destinationTable, SqlTransaction sqlTransaction)
        {
            try
            {
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                {
                    bulkCopy.DestinationTableName = destinationTable;
                    bulkCopy.WriteToServer(studentMappingsTable);
                }
            }
            catch
            {
                throw;
            }
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

        public StudenteRitorno(
                string codFiscale,
                string numMandatoFlusso,
                DateTime dataPagamento,
                string numReversale,
                bool pagamentoScartato
            )
        {
            this.codFiscale = codFiscale;
            this.numMandatoFlusso = numMandatoFlusso;
            this.dataPagamento = dataPagamento;
            this.numReversale = numReversale;
            this.pagamentoScartato = pagamentoScartato;
            codMovimentoGenerale = "";
        }

        public void SetCodMovimentoGenerale(string codMovimentoGenerale)
        {
            this.codMovimentoGenerale = codMovimentoGenerale;
        }
    }
}
