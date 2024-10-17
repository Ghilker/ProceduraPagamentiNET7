using ProcedureNet7.ProceduraAllegatiSpace;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using Excel = Microsoft.Office.Interop.Excel;

namespace ProcedureNet7
{
    internal class ProceduraBlocchi : BaseProcedure<ArgsProceduraBlocchi>
    {
        public string _blocksYear = string.Empty;
        public string _blocksUsername = string.Empty;
        private readonly Dictionary<string, List<string>> blocksToRemove = new();
        private readonly Dictionary<string, List<string>> blocksToAdd = new();

        private List<string> codiciFiscaliTotali = new();
        private List<string> codiciFiscaliConErrori = new();

        public ProceduraBlocchi(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraBlocchi args)
        {
            string blocksFilePath = args._blocksFilePath;
            _blocksYear = args._blocksYear;
            _blocksUsername = args._blocksUsername;

            _masterForm.inProcedure = true;
            try
            {
                DataTable dataTable = Utilities.ReadExcelToDataTable(blocksFilePath);
                ProcessWorksheet(dataTable);
            }
            catch (Exception ex)
            {
                Logger.LogInfo(0, $"Error: {ex.Message}");
                _masterForm.inProcedure = false;
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.LogInfo(100, $"Fine Lavorazione");
            }
        }

        private void ProcessWorksheet(DataTable dataTable)
        {
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                DataRow row = dataTable.Rows[i];
                string? nullableCodFiscale = row[0].ToString();

                if (nullableCodFiscale == null)
                {
                    Logger.LogWarning(null, $"Riga {i} con cella codice fiscale nulla");
                    continue;
                }

                string codFiscale = nullableCodFiscale;
                ProcessRowInMemory(codFiscale, row);
            }

            Logger.LogInfo(50, "Processata memoria");

            using SqlTransaction transaction = CONNECTION.BeginTransaction();
            try
            {
                // Process blocks to remove and add within the transaction
                ApplyBlocks(CONNECTION, transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.LogError(0, $"Transaction Error: {ex.Message}");
                transaction.Rollback();
                throw;
            }
        }

        private void ProcessRowInMemory(string codFiscale, DataRow data)
        {
            if (string.IsNullOrEmpty(data[1].ToString()) && string.IsNullOrEmpty(data[2].ToString()))
            {
                return;
            }

            // Define the set of delimiters
            char[] delimiters = new char[] { ';', ':', '|', '/', '#' };

            // Helper function to clean block string
            string CleanBlock(string block)
            {
                // Remove trailing non-alphanumeric characters
                return block.TrimEnd(';', ':', '|', '/', '#').Trim();
            }

            // Process codFiscale values to remove
            if (!string.IsNullOrEmpty(data[1].ToString()))
            {
                foreach (string block in data[1].ToString().Split(delimiters, StringSplitOptions.RemoveEmptyEntries))
                {
                    string cleanedBlock = CleanBlock(block);
                    if (!blocksToRemove.TryGetValue(cleanedBlock, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToRemove[cleanedBlock] = value;
                    }

                    value.Add(codFiscale);
                }
            }

            // Process codFiscale values to add
            if (!string.IsNullOrEmpty(data[2].ToString()))
            {
                foreach (string block in data[2].ToString().Split(delimiters, StringSplitOptions.RemoveEmptyEntries))
                {
                    string cleanedBlock = CleanBlock(block);
                    if (!blocksToAdd.TryGetValue(cleanedBlock, out List<string>? value))
                    {
                        value = new List<string>();
                        blocksToAdd[cleanedBlock] = value;
                    }

                    value.Add(codFiscale);
                }
            }
        }


        private void ApplyBlocks(SqlConnection conn, SqlTransaction transaction)
        {
            foreach (string block in blocksToRemove.Keys)
            {
                try
                {
                    BlocksUtil.RemoveBlock(conn, transaction, blocksToRemove[block], block, _blocksYear, _blocksUsername);
                    Logger.LogInfo(75, $"Processato blocco {block} da togliere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to remove: {block} - {ex.Message}");
                }
            }
            foreach (string block in blocksToAdd.Keys)
            {
                try
                {
                    BlocksUtil.AddBlock(conn, transaction, blocksToAdd[block], block, _blocksYear, _blocksUsername);
                    Logger.LogInfo(75, $"Processato blocco {block} da mettere");
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error processing block to add: {block} - {ex.Message}");
                }
            }
        }
    }
}
