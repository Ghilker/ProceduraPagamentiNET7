using ProcedureNet7.ProceduraAllegatiSpace;
using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal class ProceduraBlocchi : BaseProcedure<ArgsProceduraBlocchi>
    {
        public string _blocksYear = string.Empty;
        public string _blocksUsername = string.Empty;

        // blocksToRemove, blocksToAdd keyed by block -> list of CF
        private readonly Dictionary<string, List<string>> blocksToRemove = new();
        private readonly Dictionary<string, List<string>> blocksToAdd = new();

        // Accumulate (block, reason) for each CF so we can build one message
        //   CF -> list of (Block, Reason)
        private readonly Dictionary<string, List<(string Block, string Reason)>> blocksAddedByCF = new();

        private bool _blocksGiaRimossi;
        private bool _blocksInsertMessaggio;

        private readonly Dictionary<string, string> _blockDescriptions = new();


        public ProceduraBlocchi(MasterForm masterForm, SqlConnection mainConnection)
            : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsProceduraBlocchi args)
        {
            string blocksFilePath = args._blocksFilePath;
            _blocksYear = args._blocksYear;
            _blocksUsername = args._blocksUsername;
            _blocksGiaRimossi = args._blocksGiaRimossi;
            _blocksInsertMessaggio = args._blocksInsertMessaggio;

            _masterForm.inProcedure = true;
            try
            {
                // 1) Load block descriptions from DB (outside any transaction).
                LoadBlockDescriptions();

                // 2) Read Excel
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

        private void LoadBlockDescriptions()
        {
            string sql = @"
                SELECT Cod_tipologia_blocco, Descrizione
                FROM   Tipologie_motivazioni_blocco_pag
            ";

            using SqlCommand cmd = new SqlCommand(sql, CONNECTION);
            using SqlDataReader reader = cmd.ExecuteReader();

            // Clear any old data first
            _blockDescriptions.Clear();

            while (reader.Read())
            {
                string code = reader["Cod_tipologia_blocco"]?.ToString()?.Trim() ?? "";
                string desc = reader["Descrizione"]?.ToString() ?? "";

                // Clean up the description (strip HTML-unsafe chars, etc.)
                desc = CleanForHtmlAndSql(desc);

                if (!string.IsNullOrEmpty(code))
                {
                    _blockDescriptions[code] = desc;
                }
            }

            static string CleanForHtmlAndSql(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return string.Empty;

                // Example: minimal encoding for HTML
                string encoded = System.Net.WebUtility.HtmlEncode(input);
                return encoded;
            }
        }

        private void ProcessWorksheet(DataTable dataTable)
        {
            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                DataRow row = dataTable.Rows[i];
                string? nullableCodFiscale = row[0]?.ToString();

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
                // 1) Apply blocks (remove, then add)
                ApplyBlocks(CONNECTION, transaction);

                // 2) Insert messages, if the user chose so
                if (_blocksInsertMessaggio)
                {
                    InsertMessagesPerStudent(CONNECTION, transaction);
                }

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
            // data[0] = codFiscale
            // data[1] = blocks to remove
            // data[2] = blocks to add
            // data[3] = reasons (OPTIONAL)

            if (string.IsNullOrEmpty(data[1]?.ToString()) && string.IsNullOrEmpty(data[2]?.ToString()))
            {
                return;
            }

            // Delimiters for block columns
            char[] blockDelimiters = { ';', ':', '|', '/', '#' };

            static string CleanBlock(string b) => b.TrimEnd(';', ':', '|', '/', '#').Trim();

            // Blocks to Remove
            if (!string.IsNullOrWhiteSpace(data[1]?.ToString()))
            {
                foreach (string blk in data[1].ToString().Split(blockDelimiters, StringSplitOptions.RemoveEmptyEntries))
                {
                    string block = CleanBlock(blk);
                    if (!blocksToRemove.TryGetValue(block, out List<string>? cfList))
                    {
                        cfList = new List<string>();
                        blocksToRemove[block] = cfList;
                    }
                    cfList.Add(codFiscale);
                }
            }

            // Blocks to Add
            if (!string.IsNullOrWhiteSpace(data[2]?.ToString()))
            {
                var toAdd = data[2].ToString()
                    .Split(blockDelimiters, StringSplitOptions.RemoveEmptyEntries)
                    .Select(CleanBlock)
                    .Where(b => !string.IsNullOrEmpty(b))
                    .ToList();

                List<string> reasonsList = new();
                if (data.Table.Columns.Count > 3)
                {
                    string reasonCell = data[3]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(reasonCell))
                    {
                        reasonsList = reasonCell
                            .Split('#', StringSplitOptions.RemoveEmptyEntries)
                            .Select(r => r.Trim())
                            .ToList();
                    }
                }

                for (int i = 0; i < toAdd.Count; i++)
                {
                    string block = toAdd[i];
                    string reason = (i < reasonsList.Count) ? reasonsList[i] : string.Empty;

                    if (!blocksToAdd.TryGetValue(block, out List<string>? cfList))
                    {
                        cfList = new List<string>();
                        blocksToAdd[block] = cfList;
                    }
                    cfList.Add(codFiscale);

                    // We'll record (block, reason), but only tentatively.
                    // We'll refine it later to send messages only if the block is truly added.
                    if (!blocksAddedByCF.ContainsKey(codFiscale))
                    {
                        blocksAddedByCF[codFiscale] = new List<(string Block, string Reason)>();
                    }
                    blocksAddedByCF[codFiscale].Add((block, reason));
                }
            }
        }

        private void ApplyBlocks(SqlConnection conn, SqlTransaction transaction)
        {
            // 1) Remove blocks
            foreach (var block in blocksToRemove.Keys)
            {
                if (string.IsNullOrWhiteSpace(block))
                    continue;

                try
                {
                    var cfList = blocksToRemove[block];
                    // We'll get the result of removal:
                    BlockRemoveResult removeResult = BlocksUtil.RemoveBlock(
                        conn,
                        transaction,
                        cfList,
                        block,
                        _blocksYear,
                        _blocksUsername
                    );

                    Logger.LogInfo(75, $"Processato blocco {block} da togliere");
                    // If you need to do something with removeResult, you can do it here.
                    // e.g. removeResult.ActuallyRemoved, removeResult.NothingToRemove
                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error removing block {block}: {ex.Message}");
                }
            }

            // 2) Add blocks
            foreach (var block in blocksToAdd.Keys)
            {
                if (string.IsNullOrWhiteSpace(block))
                    continue;

                try
                {
                    var cfList = blocksToAdd[block];
                    // We'll get the result of addition:
                    BlockAddResult addResult = BlocksUtil.AddBlock(
                        conn,
                        transaction,
                        cfList,
                        block,
                        _blocksYear,
                        _blocksUsername,
                        _blocksGiaRimossi
                    );

                    Logger.LogInfo(75, $"Processato blocco {block} da mettere");

                    // Now refine blocksAddedByCF so we only keep those that actually got added
                    // for each CF in addResult.ActuallyAdded:
                    foreach (var cf in cfList)
                    {
                        // If the CF wasn't in addResult.ActuallyAdded, remove it from blocksAddedByCF
                        // (or skip) so we won't send them a message.
                        if (!addResult.ActuallyAdded.Contains(cf) && blocksAddedByCF.ContainsKey(cf))
                        {
                            // remove any (block, reason) pair with this block
                            blocksAddedByCF[cf].RemoveAll(x => x.Block.Equals(block, StringComparison.OrdinalIgnoreCase));

                            // If that leaves the list empty, remove the CF key altogether
                            if (blocksAddedByCF[cf].Count == 0)
                            {
                                blocksAddedByCF.Remove(cf);
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError(0, $"Error adding block {block}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Builds one HTML message per CF summarizing all blocks (and reasons), then inserts them.
        /// We only insert messages for CFs that truly had blocks added (see refined blocksAddedByCF).
        /// </summary>
        private void InsertMessagesPerStudent(SqlConnection conn, SqlTransaction transaction)
        {
            var messagesDict = new Dictionary<string, string>();

            // For each CF that had blocks actually added (refined in ApplyBlocks)
            foreach (var cf in blocksAddedByCF.Keys)
            {
                var blocksList = blocksAddedByCF[cf]; // List<(string BlockCode, string Reason)>

                // Build HTML
                string htmlMessage = "Gentile studente, i seguenti blocchi sono stati aggiunti sulla sua posizione:<br>";

                var lines = new List<string>();
                foreach (var (blockCode, reason) in blocksList)
                {
                    string blockDesc = GetBlockDescription(blockCode);
                    lines.Add($"- <b>{blockDesc}</b>: {reason}");
                }

                htmlMessage += string.Join("<br>", lines);

                messagesDict[cf] = htmlMessage;
            }

            if (messagesDict.Count > 0)
            {
                MessageUtils.InsertMessages(conn, transaction, messagesDict, _blocksUsername);
            }
        }

        private string GetBlockDescription(string blockCode)
        {
            if (_blockDescriptions.TryGetValue(blockCode, out string decoded))
            {
                return decoded;
            }

            // Fallback: code not found in the dictionary, just return the code
            return blockCode;
        }
    }
}
