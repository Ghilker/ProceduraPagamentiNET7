using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class SpecificheImpegni : BaseProcedure<ArgsSpecificheImpegni>
    {
        string selectedFile = string.Empty;
        string selectedDate = string.Empty;
        string tipoFondo = string.Empty;
        bool aperturaNuovaSpecifica;
        string capitolo = string.Empty;
        string descrDetermina = string.Empty;
        string esePR = string.Empty;
        string eseSA = string.Empty;
        string impegnoPR = string.Empty;
        string impegnoSA = string.Empty;
        string numDetermina = string.Empty;
        string selectedAA = string.Empty;
        string selectedCodBeneficio = string.Empty;

        SqlTransaction? sqlTransaction = null;
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);

        private List<string> _numDomandas = new List<string>();
        int _numDomandaColumn;
        Dictionary<string, double> numDomandaImporti = new Dictionary<string, double>();

        public SpecificheImpegni(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsSpecificheImpegni args)
        {
            try
            {
                sqlTransaction = CONNECTION.BeginTransaction();

                if (CONNECTION == null || sqlTransaction == null)
                {
                    Logger.LogInfo(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                    return;
                }

                selectedFile = args._selectedFile;
                selectedDate = args._selectedDate;
                tipoFondo = args._tipoFondo;
                aperturaNuovaSpecifica = args._aperturaNuovaSpecifica;
                capitolo = args._capitolo;
                descrDetermina = args._descrDetermina;
                esePR = args._esePR;
                eseSA = args._eseSA;
                impegnoPR = args._impegnoPR;
                impegnoSA = args._impegnoSA;
                numDetermina = args._numDetermina;
                selectedAA = args._selectedAA;
                selectedCodBeneficio = args._selectedCodBeneficio;
                Panel? specificheImpegniPanel = null;

                _masterForm.Invoke((MethodInvoker)delegate
                {
                    specificheImpegniPanel = _masterForm.GetProcedurePanel();
                });

                Logger.LogInfo(20, "Selezione numeri domanda");
                DataTable allStudentsData = Utilities.ReadExcelToDataTable(selectedFile);
                DataGridView numDomandaGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, specificheImpegniPanel, OnNumDomandaClicked);
                MessageBox.Show("Cliccare sul primo numero domanda", "Selezionare il numero domanda");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                _masterForm.Invoke((MethodInvoker)delegate
                {
                    numDomandaGridView.Dispose();
                });

                string numDomandaString = string.Join(", ", _numDomandas.Select(numDom => $"'{numDom}'"));
                string sqlUpdate = $@"
                UPDATE Specifiche_impegni
                SET data_fine_validita = '{selectedDate}',
                    Num_determina = '{numDetermina}',
                    data_determina = '{selectedDate}',
                    descrizione_determina = '{descrDetermina}'
                WHERE
                    anno_accademico = '{selectedAA}' AND
                    cod_beneficio = '{selectedCodBeneficio}' AND
                    num_domanda IN ({numDomandaString}) AND
                    data_fine_validita IS NULL";

                Logger.LogInfo(30, "Update specifiche impegni con la chiusura delle righe");

                SqlCommand updateCommand = new SqlCommand(sqlUpdate, CONNECTION, sqlTransaction);
                updateCommand.ExecuteNonQuery();

                if (!aperturaNuovaSpecifica)
                {
                    sqlTransaction.Commit();
                    Logger.LogInfo(100, "Fine lavorazione");
                    _masterForm.inProcedure = false;
                    return;
                }
                Logger.LogInfo(60, "Selezione importi attuali");

                DataGridView importiGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, specificheImpegniPanel, OnImportiClicked);
                MessageBox.Show("Cliccare sul primo importo attuale", "Selezionare l'importo attuale");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                _masterForm.Invoke((MethodInvoker)delegate
                {
                    importiGridView.Dispose();
                });

                string sqlInsert = @"
                    INSERT INTO [specifiche_impegni] ([Anno_accademico], [Num_domanda], [Cod_fiscale], [Cod_beneficio], [Data_validita], [Utente], [Codice_Studente], [Tipo_fondo], [Capitolo], [Importo_assegnato], [Determina_conferimento], [num_impegno_primaRata], [num_impegno_saldo], [esercizio_saldo], [Esercizio_prima_rata], [data_fine_validita], [Num_determina], [data_determina], [descrizione_determina])
                    SELECT @selectedAA, @numDomanda, Domanda.Cod_fiscale, @selectedCodBeneficio, CURRENT_TIMESTAMP, 'Area4', Studente.Codice_Studente, @tipoFondo, @capitolo, @importo, @numDetermina, @impegnoPR, @impegnoSA, @eseSA, @esePR, NULL, NULL, NULL, NULL
                    FROM Domanda 
                    INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale
                    WHERE Domanda.Anno_accademico = @selectedAA AND Domanda.Num_domanda = @numDomanda
                ";

                using SqlCommand insertCommand = new SqlCommand(sqlInsert, CONNECTION, sqlTransaction);
                // Initialize all parameters just once
                insertCommand.Parameters.Add("@selectedAA", SqlDbType.VarChar); // Assuming the type is VarChar
                insertCommand.Parameters.Add("@selectedCodBeneficio", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@tipoFondo", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@capitolo", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@numDetermina", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@impegnoPR", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@impegnoSA", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@eseSA", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@esePR", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@numDomanda", SqlDbType.VarChar);
                insertCommand.Parameters.Add("@importo", SqlDbType.Float); // Adjust data type if needed

                try
                {
                    int counter = 0;
                    foreach (KeyValuePair<string, double> pair in numDomandaImporti)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key))
                            continue;

                        insertCommand.Parameters["@numDomanda"].Value = pair.Key;
                        insertCommand.Parameters["@importo"].Value = pair.Value;
                        insertCommand.Parameters["@selectedAA"].Value = selectedAA;
                        insertCommand.Parameters["@selectedCodBeneficio"].Value = selectedCodBeneficio;
                        insertCommand.Parameters["@tipoFondo"].Value = tipoFondo;
                        insertCommand.Parameters["@capitolo"].Value = capitolo;
                        insertCommand.Parameters["@numDetermina"].Value = $"{numDetermina} del {selectedDate}";
                        insertCommand.Parameters["@impegnoPR"].Value = impegnoPR;
                        insertCommand.Parameters["@impegnoSA"].Value = impegnoSA;
                        insertCommand.Parameters["@eseSA"].Value = eseSA;
                        insertCommand.Parameters["@esePR"].Value = esePR;

                        insertCommand.ExecuteNonQuery();
                        counter++;
                    }
                    sqlTransaction.Commit();
                    Logger.LogInfo(90, $"Inserite {counter} nuove righe in specifiche impegni");

                }
                catch
                {
                    sqlTransaction.Rollback();
                    throw;
                }
                finally
                {
                    Logger.LogInfo(100, "Fine lavorazione");
                    _masterForm.inProcedure = false;
                }
            }
            catch
            {
                sqlTransaction.Rollback();
                throw;
            }
        }
        private void OnNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _numDomandas = new List<string>();

                    _numDomandaColumn = e.ColumnIndex;

                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? numDomanda = cellValue.ToString();
                            if (string.IsNullOrEmpty(numDomanda) || !int.TryParse(numDomanda, out int _))
                            {
                                continue;
                            }
                            _numDomandas.Add(numDomanda);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }

        private void OnImportiClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var numDomandaVar = dataGridView.Rows[i].Cells[_numDomandaColumn].Value;
                        var importoVar = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (numDomandaVar != null && importoVar != null)
                        {
                            string? numDomanda = numDomandaVar.ToString();
                            string? importoString = importoVar.ToString();
                            if ((string.IsNullOrEmpty(numDomanda) || !int.TryParse(numDomanda, out int _)) ||
                                (string.IsNullOrEmpty(importoString) || !double.TryParse(importoString, out double importo)))
                            {
                                continue;
                            }
                            numDomandaImporti.Add(numDomanda, importo);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }
    }
}
