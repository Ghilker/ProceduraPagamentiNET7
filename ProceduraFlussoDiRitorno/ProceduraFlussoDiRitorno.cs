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
        string selectedFileFlusso;
        string selectedOldMandato;
        string selectedTipoBando;

        List<StudenteRitorno> studenteRitornoList = new List<StudenteRitorno>();
        List<StudenteRitorno> studentiScartati = new List<StudenteRitorno>();

        public ProceduraFlussoDiRitorno(IProgress<(int, string)> progress, MainUI mainUI, string connection_string) : base(progress, mainUI, connection_string) { }

        public override void RunProcedure(ArgsProceduraFlussoDiRitorno args)
        {
            using SqlConnection conn = new(CONNECTION_STRING);
            conn.Open();
            SqlTransaction sqlTransaction = conn.BeginTransaction();
            try
            {

                _mainForm.inProcedure = true;
                _progress.Report((0, $"Inizio lavorazione"));
                selectedFileFlusso = args._selectedFileFlusso;
                selectedOldMandato = args._selectedImpegnoProvv;
                selectedTipoBando = args._selectedTipoBando;
                _progress.Report((10, $"Creazione lista studenti"));
                CreateStudentiList();
                _progress.Report((20, $"Selezione codice movimento generale"));
                SetCodMovimentoGenerale(conn, sqlTransaction);
                _progress.Report((30, $"Aggiornamento tabella movimento elementare"));
                UpdateMovimentoElementari(conn, sqlTransaction);
                _progress.Report((40, $"Aggiornamento tabella stati movimento contabile"));
                UpdateStatiMovimentoContabile(conn, sqlTransaction);

                _progress.Report((55, $"Inserimento in pagamenti"));
                InsertIntoPagamenti(conn, sqlTransaction);
                _progress.Report((66, $"Inserimento in reversali"));
                InsertIntoReversali(conn, sqlTransaction);
                _progress.Report((77, $"Aggiornamento mandati"));
                UpdateMandato(conn, sqlTransaction);
                _progress.Report((88, $"Annullamento pagamenti scartati"));
                Scartati(conn, sqlTransaction);

                _mainForm.inProcedure = false;
                _progress.Report((100, $"Fine lavorazione"));
                _progress.Report((100, $"Totale studenti lavorati: {studenteRitornoList.Count}"));
                _progress.Report((100, $"Di cui scartati: {studentiScartati.Count}"));

                sqlTransaction.Commit();
            }
            catch (Exception ex)
            {
                _progress.Report((100, ex.Message));
                sqlTransaction.Rollback();
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
        void SetCodMovimentoGenerale(SqlConnection conn, SqlTransaction sqlTransaction)
        {
            try
            {
                string dataQuery = @$"
                    SELECT DISTINCT  CODICE_MOVIMENTO, CODICE_FISCALE
	                FROM MOVIMENTI_CONTABILI_ELEMENTARI
	                WHERE CODICE_MOVIMENTO IN (SELECT CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI WHERE cod_mandato='{selectedOldMandato}')
                    ";

                SqlCommand readData = new(dataQuery, conn, sqlTransaction);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = reader["CODICE_FISCALE"].ToString().ToUpper();
                        StudenteRitorno studente = studenteRitornoList.FirstOrDefault(s => s.codFiscale == codFiscale);
                        if (studente != null)
                        {
                            string codMovimento = reader["CODICE_MOVIMENTO"].ToString();
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
        private void UpdateMovimentoElementari(SqlConnection conn, SqlTransaction sqlTransaction)
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
                SqlCommand updateAllCmd = new(sqlUpdateAll, conn, sqlTransaction);
                updateAllCmd.ExecuteNonQuery();

                string createTempTable = @"
                    CREATE TABLE #TempStudenteMappings
                    (
                        CodFiscale CHAR(16),
                        NumReversale VARCHAR(20)
                    );
                    ";
                SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempStudentMappingTable = NumReversaleToCodFiscale(studenteRitornoList);
                BulkInsertIntoSqlTable(conn, tempStudentMappingTable, "#TempStudenteMappings", sqlTransaction);

                string sqlUpdateReversali = $@"
                        UPDATE MOVIMENTI_CONTABILI_ELEMENTARI
                        SET MOVIMENTI_CONTABILI_ELEMENTARI.numero_reversale = #TempStudenteMappings.NumReversale
                        FROM MOVIMENTI_CONTABILI_ELEMENTARI
                        INNER JOIN #TempStudenteMappings ON MOVIMENTI_CONTABILI_ELEMENTARI.codice_fiscale = #TempStudenteMappings.CodFiscale
                        WHERE MOVIMENTI_CONTABILI_ELEMENTARI.Codice_movimento IN ({listaCodMov}) AND MOVIMENTI_CONTABILI_ELEMENTARI.segno = '0';
                        ";

                SqlCommand updateReversaliCmd = new(sqlUpdateReversali, conn, sqlTransaction);
                updateReversaliCmd.ExecuteNonQuery();

                string dropTable = "DROP TABLE #TempStudenteMappings";

                SqlCommand dropTableCmd = new(dropTable, conn, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();

            }
            catch
            {
                throw;
            }
        }
        private void UpdateStatiMovimentoContabile(SqlConnection conn, SqlTransaction sqlTransaction)
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
                SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempMovimentoContabili = CodMovimentoToDateAndCf(studenteRitornoList);
                BulkInsertIntoSqlTable(conn, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

                string sqlInsertStati = $@"
                INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO, NOTE_STATO)
                SELECT '1', mcg.CODICE_MOVIMENTO, tm.DataEmissione, mcg.UTENTE_VALIDAZIONE, mcg.NOTE_VALIDAZIONE_MOVIMENTO 
                
                FROM STATI_DEL_MOVIMENTO_CONTABILE as stm INNER JOIN
                #TempMovimentiContabili as tm ON stm.codice_movimento = tm.CodMovimentoGenerale INNER JOIN
                MOVIMENTI_CONTABILI_GENERALI AS mcg ON tm.CodMovimentoGenerale = mcg.CODICE_MOVIMENTO

                WHERE mcg.cod_mandato = '{selectedOldMandato}';
                ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertStati, conn, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }
                string dropTable = "DROP TABLE #TempMovimentiContabili";

                SqlCommand dropTableCmd = new(dropTable, conn, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();

            }
            catch
            {
                throw;
            }
        }
        private void InsertIntoPagamenti(SqlConnection conn, SqlTransaction sqlTransaction)
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
                SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable pagamTable = PagamentiTable(studenteRitornoList);
                BulkInsertIntoSqlTable(conn, pagamTable, "#TempPagamenti", sqlTransaction);

                string sqlInsertPagam = $@"
                        INSERT INTO PAGAMENTI (Cod_tipo_pagam, Anno_accademico, Cod_mandato, Data_emissione_mandato, Num_domanda, Imp_pagato, Data_validita, Ese_finanziario, Note, Non_riscosso, Ritirato_azienda, Utente)
                        SELECT mcg.id_causale_movimento_generale, mce.anno_accademico, tmp.NumMandato, tmp.DataEmissione, mce.Num_Domanda, mce.Importo, mcg.data_validita_movimento_generale, mce.Anno_esercizio_finanziario, mcg.note_validazione_movimento, '0' as Non_riscosso, '0' as ritirato_azienda, mcg.utente_validazione
                        FROM MOVIMENTI_CONTABILI_GENERALI as mcg INNER JOIN
                            (SELECT DISTINCT 
                                mce.ANNO_ACCADEMICO, mce.CODICE_MOVIMENTO, Domanda.Num_domanda, mce.ANNO_ESERCIZIO_FINANZIARIO,importo
                            FROM MOVIMENTI_CONTABILI_ELEMENTARI as mce INNER JOIN
                                Domanda ON mce.ANNO_ACCADEMICO = Domanda.Anno_accademico AND mce.CODICE_FISCALE = Domanda.Cod_fiscale INNER JOIN
                                #TempPagamenti ON Domanda.Cod_fiscale = #TempPagamenti.CodFiscale AND mce.CODICE_MOVIMENTO = #TempPagamenti.CodMovimentoGenerale
                            WHERE segno='1' AND Domanda.Tipo_bando = '{selectedTipoBando}') 
                            as mce ON mcg.codice_movimento = mce.codice_movimento INNER JOIN
                        #TempPagamenti as tmp ON mcg.codice_movimento = tmp.CodMovimentoGenerale
                        WHERE mcg.cod_mandato = '{selectedOldMandato}'
                        ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertPagam, conn, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                throw;
            }
        }
        private void InsertIntoReversali(SqlConnection conn, SqlTransaction sqlTransaction)
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
                SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable pagamTable = PagamentiTable(studenteRitornoList);
                BulkInsertIntoSqlTable(conn, pagamTable, "#TempReversali", sqlTransaction);

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
                        WHERE mce.segno='0' AND Domanda.Tipo_bando = '{selectedTipoBando}' and mcg.cod_mandato = '{selectedOldMandato}'

                        ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertPagam, conn, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
                throw;
            }
        }
        private void UpdateMandato(SqlConnection conn, SqlTransaction sqlTransaction)
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
                SqlCommand createCmd = new(createTempTable, conn, sqlTransaction);
                createCmd.ExecuteNonQuery();

                DataTable tempMovimentoContabili = NumMandatoUpdate(studenteRitornoList);
                BulkInsertIntoSqlTable(conn, tempMovimentoContabili, "#TempMovimentiContabili", sqlTransaction);

                string sqlInsertStati = $@"
                UPDATE MOVIMENTI_CONTABILI_GENERALI
                SET cod_mandato = tm.NumMandato

                FROM MOVIMENTI_CONTABILI_GENERALI INNER JOIN
                #TempMovimentiContabili as tm ON MOVIMENTI_CONTABILI_GENERALI.codice_movimento = tm.codMovimentoGenerale

                WHERE MOVIMENTI_CONTABILI_GENERALI.cod_mandato = '{selectedOldMandato}';
                ";

                using (SqlCommand cmd = new SqlCommand(sqlInsertStati, conn, sqlTransaction))
                {
                    cmd.ExecuteNonQuery();
                }

                string dropTable = "DROP TABLE #TempMovimentiContabili";
                SqlCommand dropTableCmd = new(dropTable, conn, sqlTransaction);
                dropTableCmd.ExecuteNonQuery();
            }
            catch
            {
                throw;
            }
        }
        private void Scartati(SqlConnection conn, SqlTransaction sqlTransaction)
        {
            try
            {
                foreach (StudenteRitorno studente in studentiScartati)
                {
                    string sqlScartati = "SP_annulla_pagamento";

                    using (SqlCommand cmd = new SqlCommand(sqlScartati, conn, sqlTransaction))
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
                table.Rows.Add(studente.codFiscale, studente.dataPagamento, studente.codMovimentoGenerale, studente.numMandatoFlusso);
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
                table.Rows.Add(studente.numMandatoFlusso, studente.codMovimentoGenerale);
            }

            return table;
        }

        static void BulkInsertIntoSqlTable(SqlConnection conn, DataTable studentMappingsTable, string destinationTable, SqlTransaction sqlTransaction)
        {
            try
            {
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, sqlTransaction))
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
        }

        public void SetCodMovimentoGenerale(string codMovimentoGenerale)
        {
            this.codMovimentoGenerale = codMovimentoGenerale;
        }
    }
}
