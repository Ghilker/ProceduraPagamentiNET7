using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ProceduraVariazioni : BaseProcedure<ArgsProceduraVariazioni>
    {
        public ProceduraVariazioni(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        private readonly Dictionary<string, string> daRiammettereCodValues = new()
        {
            {"03","Revoca"},
            {"04","Decadenza"},
            {"011","Revoca per incompatibilità col bando"},
            {"018","Revoca per sede distaccata"},
            {"019","Revoca per mancata iscrizione"},
            {"020","Revoca per studente iscritto ripetente"},
            {"021","Revoca per ISEE non presente in banca dati"},
            {"022","Revoca per studente già laureato"},
            {"023","Revoca per patrimonio oltre il limite"},
            {"024","Revoca per reddito oltre il limite"},
            {"025","Revoca per mancanza esami o crediti"},
            {"027","Revoca per iscrizione fuori temine"},
            {"028","Revoca per ISEE fuori termine"},
            {"029","Revoca per ISEE non prodotta"},
            {"030","Decadenza per mancata comunicazione modalità di pagamento"},
            {"031","Revoca per mancanza contratto di affitto"},
        };

        private readonly Dictionary<string, string> riammissioniValues = new()
        {
            {"05","Riammissione come idoneo"},
            {"06","Riammissione come vincitore"},
        };

        List<string> codFiscaliList = new List<string>();
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);
        public override void RunProcedure(ArgsProceduraVariazioni args)
        {
            string selectedFilePath = args._selectedFilePath;
            string selectedVariazioniValue = args._selectedVariazioniValue;
            string selectedBeneficioValue = args._selectedBeneficioValue;
            string variazNotaText = args._variazNotaText;
            string variazDataVariazioneText = args._variazDataVariazioneText;
            string variazUtenzaText = args._variazUtenzaText;
            string variazAAText = args._variazAAText;

            _masterForm.inProcedure = true;
            try
            {
                DataTable dataTable = Utilities.ReadExcelToDataTable(selectedFilePath);
                Panel? variazioniPanel = null;
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    variazioniPanel = _masterForm.GetProcedurePanel();
                });
                DataGridView dataGridView = Utilities.CreateDataGridView(dataTable, _masterForm, variazioniPanel, DataGridViewMouseClick_handler);

                _waitHandle.Reset();
                _waitHandle.WaitOne();

                string codFiscaleSql = string.Join(",", codFiscaliList.ConvertAll(c => $"'{c}'"));

                if (string.IsNullOrEmpty(codFiscaleSql))
                {
                    dataTable.Dispose();
                    _masterForm.inProcedure = false;
                    Logger.Log(100, $"Fine Lavorazione - Lista CF vuota", LogLevel.INFO);
                    return;
                }

                string sqlAddVariazione = $@"
                                        INSERT INTO [dbo].[Variazioni] ([Anno_accademico],[Num_domanda],[Cod_tipo_variaz],[Cod_beneficio],[Note],[Data_riferimento],[Data_validita],[Utente],[Id_Domanda])
                                        SELECT {variazAAText}, Domanda.Num_domanda, '{selectedVariazioniValue}', '{selectedBeneficioValue}', '{variazNotaText}', '{variazDataVariazioneText}', CURRENT_TIMESTAMP, '{variazUtenzaText}', Domanda.Id_Domanda
                                        FROM
                                            Domanda
                                        WHERE 
                                            Domanda.Anno_accademico = {variazAAText} AND 
                                            Domanda.Cod_fiscale IN ({codFiscaleSql}) AND 
                                            Domanda.tipo_bando = 'lz'
                                            ";
                using SqlCommand insertVariazCommand = new(sqlAddVariazione, CONNECTION);
                _ = insertVariazCommand.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Logger.Log(0, $"Error: {ex.Message}", LogLevel.INFO);
                _masterForm.inProcedure = false;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.Log(100, $"Fine Lavorazione", LogLevel.INFO);
            }
        }

        private void DataGridViewMouseClick_handler(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                // Check if the click is on a valid cell
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    codFiscaliList = new List<string>();

                    // Iterate from the clicked row to the last row in the DataGridView
                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        // Assuming fiscal codes are in the first column (index 0)
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? codFiscale = cellValue.ToString();
                            if (string.IsNullOrEmpty(codFiscale))
                            {
                                continue;
                            }
                            codFiscaliList.Add(codFiscale);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }
    }
}
