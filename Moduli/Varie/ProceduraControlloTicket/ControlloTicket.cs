using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal class ControlloTicket : BaseProcedure<ArgsControlloTicket>
    {
        public List<ProcessedTicket> ProcessedTickets { get; private set; } = new();

        private KeywordConfig? keywordConfig;

        public ControlloTicket(MasterForm? masterForm, SqlConnection? connection_string)
            : base(masterForm, connection_string)
        {
        }

        public override void RunProcedure(ArgsControlloTicket args)
        {
            if (CONNECTION == null)
                throw new Exception("SqlConnection is null (no active DB connection).");
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            // 1) Load keywords (from ControlloTicketKeywords.json or your chosen file)
            LoadKeywords();

            // 2) Read CSV into DataTable
            DataTable csvData = CsvToDataTable(args.SelectedCsvPath);

            // 3) Convert DataTable -> List<StudentTicket>
            List<StudentTicket> tickets = ConvertToStudentTickets(csvData);

            // 4) Distinct CODFISC list
            var codFiscList = tickets
                .Select(t => t.CODFISC)
                .Where(cf => !string.IsNullOrWhiteSpace(cf))
                .Distinct()
                .ToList();

            // 5) Fetch closure data (blocks, esito, etc.)
            var dbClosureData = FetchClosureData(codFiscList);

            // 6) Merge CSV + DB
            var mergedData = MergeData(tickets, dbClosureData);

            // 7) Filter for closure, residence, etc.
            var filteredForClosure = ApplyAdditionalFilters(mergedData, permessoCheck: false);
            var closureResults = AnalyzeMessagesForClosure(filteredForClosure);

            var filteredForResidence = ApplyAdditionalFilters(mergedData, permessoCheck: true);
            var residenceResults = AnalyzeMessagesForResidence(filteredForResidence);
            var transferResults = AnalyzeMessagesForTransfer(filteredForClosure);

            var paymentResults = AnalyzeMessagesForPayment(filteredForClosure);
            UpdatePaymentDetails(paymentResults, codFiscList);

            // 8) Combine if needed (so the Form can show all)
            ProcessedTickets = closureResults
                .Union(residenceResults)
                .Union(transferResults)
                .Union(paymentResults)
                .ToList();

            // 9) Write the 3 CSV outputs in the same folder as input
            string? inputDir = Path.GetDirectoryName(args.SelectedCsvPath);
            if (string.IsNullOrEmpty(inputDir))
                inputDir = AppDomain.CurrentDomain.BaseDirectory;

            WriteClosureTicketsCsv(closureResults, inputDir);
            WriteResidenceTicketsCsv(residenceResults, inputDir);
            WritePaymentTicketsCsv(paymentResults, inputDir);
            WriteTransferTicketsCsv(transferResults, inputDir);

            Logger.LogInfo(100, "Fine lavorazione");
        }

        #region 1) Load Keywords
        private void LoadKeywords()
        {
            try
            {
                // Adjust the path as needed:
                // e.g. "ProcedureNet7\\Moduli\\Varie\\ProceduraControlloTicket\\ControlloTicketKeywords.json"
                string jsonFilePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Moduli",
                    "Varie",
                    "ProceduraControlloTicket",
                    "ControlloTicketKeywords.json");

                if (!File.Exists(jsonFilePath))
                    throw new FileNotFoundException("ControlloTicketKeywords.json not found.", jsonFilePath);

                string content = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                keywordConfig = JsonConvert.DeserializeObject<KeywordConfig>(content);
                if (keywordConfig == null)
                    throw new Exception("Failed to deserialize keyword config.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(100, "Error loading keywords.json: " + ex.Message);
                throw;
            }
        }
        #endregion

        #region 2) CSV Reading
        public static DataTable CsvToDataTable(string? csvFilePath)
        {
            if (string.IsNullOrWhiteSpace(csvFilePath) || !File.Exists(csvFilePath))
                throw new FileNotFoundException("CSV file not found.", csvFilePath);

            DataTable dt = new DataTable();
            bool isFirstRow = true;

            // If your CSV is not actually UTF-8, you might need Encoding.GetEncoding(1252)
            using (var reader = new StreamReader(csvFilePath, Encoding.GetEncoding(1252)))
            {
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(';');

                    if (isFirstRow)
                    {
                        // create columns from first row
                        foreach (var col in values)
                            dt.Columns.Add(col.Trim());
                        isFirstRow = false;
                    }
                    else
                    {
                        var row = dt.NewRow();
                        for (int i = 0; i < values.Length && i < dt.Columns.Count; i++)
                            row[i] = values[i].Trim();
                        dt.Rows.Add(row);
                    }
                }
            }
            return dt;
        }

        private List<StudentTicket> ConvertToStudentTickets(DataTable table)
        {
            var list = new List<StudentTicket>();

            foreach (DataRow row in table.Rows)
            {
                var ticket = new StudentTicket
                {
                    NREC = GetSafeString(row, "NREC"),
                    ID_TICKET = GetSafeString(row, "ID_TICKET"),
                    CODSTUD = GetSafeString(row, "CODSTUD"),
                    COGNOME = GetSafeString(row, "COGNOME"),
                    NOME = GetSafeString(row, "NOME"),
                    CODFISC = GetSafeString(row, "CODFISC"),
                    OGGETTO = GetSafeString(row, "OGGETTO"),
                    CATEGORIA = GetSafeString(row, "CATEGORIA"),
                    SOTTOCATEGORIA = GetSafeString(row, "SOTTOCATEGORIA"),
                    ANNO_ACCADEMICO_RICHIESTA = GetSafeString(row, "ANNO_ACCADEMICO_RICHIESTA"),
                    STATO = GetSafeString(row, "STATO"),
                    DATA_CREAZIONE = GetSafeString(row, "DATA_CREAZIONE"),
                    PRIMO_MSG_STUDENTE = GetSafeString(row, "PRIMO_MSG_STUDENTE"),
                    PRIMO_MSG_OPERATORE = GetSafeString(row, "PRIMO_MSG_OPERATORE"),
                    UID = GetSafeString(row, "UID"),
                    DATA_ULTIMO_MESSAGGIO = GetSafeString(row, "DATA_ULTIMO_MESSAGGIO")
                };
                list.Add(ticket);
            }
            return list;
        }

        private string GetSafeString(DataRow row, string colName)
        {
            if (!row.Table.Columns.Contains(colName))
                return string.Empty;
            return row[colName]?.ToString() ?? string.Empty;
        }
        #endregion

        #region 5) Fetch Closure Data (FULL T-SQL)
        private List<DatabaseRecord> FetchClosureData(List<string> codFiscList)
        {
            var result = new List<DatabaseRecord>();
            if (codFiscList.Count == 0) return result;

            using (var transaction = CONNECTION.BeginTransaction())
            {
                try
                {
                    // create #tmpCodFisc
                    string createTmp = @"
                        IF OBJECT_ID('tempdb..#tmpCodFisc') IS NOT NULL
                            DROP TABLE #tmpCodFisc;
                        CREATE TABLE #tmpCodFisc (Cod_fiscale VARCHAR(20) NOT NULL);
                        ";
                    using (var cmd = new SqlCommand(createTmp, CONNECTION, transaction))
                        cmd.ExecuteNonQuery();

                    // bulk insert
                    DataTable dt = new DataTable();
                    dt.Columns.Add("Cod_fiscale", typeof(string));
                    foreach (var cf in codFiscList) dt.Rows.Add(cf);

                    using (SqlBulkCopy bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, transaction))
                    {
                        bulk.DestinationTableName = "#tmpCodFisc";
                        bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
                        bulk.WriteToServer(dt);
                    }

                    // your big T-SQL batch from the WPF code
                    string sqlBatch = @"
                        -- Materialize Temp Tables for 2023/2024
                        SELECT Num_domanda, esito_BS
                        INTO #Temp_vEsiti_concorsiBS_2324
                        FROM vEsiti_concorsiBS
                        WHERE Anno_accademico = '20232024';

                        SELECT Num_domanda, esito_PA
                        INTO #Temp_vEsiti_concorsiPA_2324
                        FROM vEsiti_concorsiPA
                        WHERE Anno_accademico = '20232024';

                        SELECT Cod_fiscale, Cod_sede_studi
                        INTO #Temp_vIscrizioni_2324
                        FROM vIscrizioni
                        WHERE Anno_accademico = '20232024' AND tipo_bando = 'lz';

                        SELECT
                            MBP.num_domanda,
                            STRING_AGG(TMP2.Descrizione, '#') AS Blocchi
                        INTO #Temp_CTE_Blocchi_2324
                        FROM Motivazioni_blocco_pagamenti AS MBP
                        INNER JOIN Tipologie_motivazioni_blocco_pag AS TMP2
                            ON MBP.Cod_tipologia_blocco = TMP2.Cod_tipologia_blocco
                        WHERE MBP.blocco_pagamento_attivo = '1' AND MBP.Anno_accademico = '20232024'
                        GROUP BY MBP.num_domanda;

                        -- Materialize Temp Tables for 2024/2025
                        SELECT Num_domanda, esito_BS
                        INTO #Temp_vEsiti_concorsiBS_2425
                        FROM vEsiti_concorsiBS
                        WHERE Anno_accademico = '20242025';

                        SELECT Num_domanda, esito_PA
                        INTO #Temp_vEsiti_concorsiPA_2425
                        FROM vEsiti_concorsiPA
                        WHERE Anno_accademico = '20242025';

                        SELECT Cod_fiscale, Cod_sede_studi
                        INTO #Temp_vIscrizioni_2425
                        FROM vIscrizioni
                        WHERE Anno_accademico = '20242025' AND tipo_bando = 'lz';

                        SELECT
                            MBP.num_domanda,
                            STRING_AGG(TMP2.Descrizione, '#') AS Blocchi
                        INTO #Temp_CTE_Blocchi_2425
                        FROM Motivazioni_blocco_pagamenti AS MBP
                        INNER JOIN Tipologie_motivazioni_blocco_pag AS TMP2
                            ON MBP.Cod_tipologia_blocco = TMP2.Cod_tipologia_blocco
                        WHERE MBP.blocco_pagamento_attivo = '1' AND MBP.Anno_accademico = '20242025'
                        GROUP BY MBP.num_domanda;

                        -- Final Query with Aggregation by Cod_fiscale
                        SELECT
                            d.Cod_fiscale,
                            MAX(COALESCE(vBS_2425.esito_BS, '')) AS Esito_BS_24_25,
                            MAX(COALESCE(vPA_2425.esito_PA, '')) AS Esito_PA_24_25,
                            STRING_AGG(COALESCE(mbpagg_2425.Blocchi, ''), '') AS Blocchi_24_25,
                            MAX(COALESCE(vBS_2324.esito_BS, '')) AS Esito_BS_23_24,
                            STRING_AGG(COALESCE(mbpagg_2324.Blocchi, ''), '') AS Blocchi_23_24,
                            MAX(ss.Descrizione) AS Sede_Università_24_25
                        FROM Domanda d
                            INNER JOIN #tmpCodFisc t ON d.Cod_fiscale = t.Cod_fiscale
                            LEFT JOIN #Temp_vEsiti_concorsiBS_2425 vBS_2425 ON d.Num_domanda = vBS_2425.Num_domanda
                            LEFT JOIN #Temp_vEsiti_concorsiPA_2425 vPA_2425 ON d.Num_domanda = vPA_2425.Num_domanda
                            LEFT JOIN #Temp_CTE_Blocchi_2425 mbpagg_2425 ON mbpagg_2425.num_domanda = d.Num_domanda
                            LEFT JOIN #Temp_vEsiti_concorsiBS_2324 vBS_2324 ON d.Num_domanda = vBS_2324.Num_domanda
                            LEFT JOIN #Temp_CTE_Blocchi_2324 mbpagg_2324 ON mbpagg_2324.num_domanda = d.Num_domanda
                            INNER JOIN #Temp_vIscrizioni_2425 vi2425 ON vi2425.Cod_fiscale = d.Cod_fiscale
                            INNER JOIN Sede_studi ss ON ss.Cod_sede_studi = vi2425.Cod_sede_studi
                        WHERE 
                            d.Anno_accademico IN ('20232024', '20242025')
                            AND d.Tipo_bando IN ('LZ', 'L2')
                        GROUP BY 
                            d.Cod_fiscale
                        ORDER BY 
                            d.Cod_fiscale;

                        -- Clean up temporary tables
                        DROP TABLE #Temp_vEsiti_concorsiBS_2324;
                        DROP TABLE #Temp_vEsiti_concorsiPA_2324;
                        DROP TABLE #Temp_vIscrizioni_2324;
                        DROP TABLE #Temp_CTE_Blocchi_2324;
                        DROP TABLE #Temp_vEsiti_concorsiBS_2425;
                        DROP TABLE #Temp_vEsiti_concorsiPA_2425;
                        DROP TABLE #Temp_vIscrizioni_2425;
                        DROP TABLE #Temp_CTE_Blocchi_2425;
                        ";

                    using (SqlCommand cmdBatch = new SqlCommand(sqlBatch, CONNECTION, transaction))
                    {
                        using (SqlDataReader reader = cmdBatch.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                result.Add(new DatabaseRecord
                                {
                                    Cod_fiscale = reader["Cod_fiscale"]?.ToString(),
                                    Esito_BS_24_25 = reader["Esito_BS_24_25"]?.ToString(),
                                    Esito_PA_24_25 = reader["Esito_PA_24_25"]?.ToString(),
                                    Blocchi_24_25 = reader["Blocchi_24_25"]?.ToString(),
                                    Esito_BS_23_24 = reader["Esito_BS_23_24"]?.ToString(),
                                    Blocchi_23_24 = reader["Blocchi_23_24"]?.ToString(),
                                    Sede_Università_24_25 = reader["Sede_Università_24_25"]?.ToString()
                                });
                            }
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception("Error fetching closure data: " + ex.Message, ex);
                }
            }
            return result;
        }
        #endregion

        #region 6) Merge
        private List<MergedTicket> MergeData(List<StudentTicket> tickets, List<DatabaseRecord> dbData)
        {
            var merged = from t in tickets
                         join db in dbData on t.CODFISC equals db.Cod_fiscale into dbGroup
                         from db in dbGroup.DefaultIfEmpty()
                         select new MergedTicket
                         {
                             ID_TICKET = t.ID_TICKET,
                             CODSTUD = t.CODSTUD,
                             CODFISC = t.CODFISC,
                             Esito_BS_24_25 = db?.Esito_BS_24_25,
                             Esito_PA_24_25 = db?.Esito_PA_24_25,
                             Blocchi_24_25 = db?.Blocchi_24_25,
                             Esito_BS_23_24 = db?.Esito_BS_23_24,
                             Blocchi_23_24 = db?.Blocchi_23_24,
                             Sede_Università_24_25 = db?.Sede_Università_24_25,
                             Primo_Msg_Studente = t.PRIMO_MSG_STUDENTE,
                             CATEGORIA = t.CATEGORIA,
                             SOTTOCATEGORIA = t.SOTTOCATEGORIA,
                             PRIMO_MSG_OPERATORE = t.PRIMO_MSG_OPERATORE,
                             STATOTK = t.STATO
                         };
            return merged.ToList();
        }
        #endregion

        #region 7) Filter + Analyze
        private List<MergedTicket> ApplyAdditionalFilters(List<MergedTicket> mergedData, bool permessoCheck)
        {
            return mergedData.Where(m =>
                m.STATOTK != null &&
                !m.STATOTK.Equals("CHIUSO", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(m.PRIMO_MSG_OPERATORE) &&
                m.CATEGORIA != null && (
                    m.CATEGORIA.Contains("Borse di studio", StringComparison.OrdinalIgnoreCase) ||
                    m.CATEGORIA.Contains("Documentazione", StringComparison.OrdinalIgnoreCase) ||
                    m.CATEGORIA.Contains("Informazioni Bando", StringComparison.OrdinalIgnoreCase) ||
                    m.CATEGORIA.Contains("ISEE", StringComparison.OrdinalIgnoreCase) ||
                    m.CATEGORIA.Contains("MERITO", StringComparison.OrdinalIgnoreCase)
                ) &&
                (permessoCheck || (
                    ((m.Esito_BS_24_25 != null && (m.Esito_BS_24_25.Contains("Idoneo", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Esito_BS_24_25.Contains("Vincitore", StringComparison.OrdinalIgnoreCase))) ||
                     (m.Esito_BS_23_24 != null && (m.Esito_BS_23_24.Contains("Idoneo", StringComparison.OrdinalIgnoreCase) ||
                                                   m.Esito_BS_23_24.Contains("Vincitore", StringComparison.OrdinalIgnoreCase)))) &&
                    string.IsNullOrWhiteSpace(m.Blocchi_24_25) &&
                    string.IsNullOrWhiteSpace(m.Blocchi_23_24)
                ))
            ).ToList();
        }

        private List<ProcessedTicket> AnalyzeMessagesForClosure(List<MergedTicket> filteredData)
        {
            if (keywordConfig == null)
                throw new InvalidOperationException("Keyword config not loaded.");

            var posDict = keywordConfig.PositiveKeywords
                .ToDictionary(k => k.Keyword, k => k.Probability, StringComparer.OrdinalIgnoreCase);

            var negDict = keywordConfig.NegativeKeywords
                .ToDictionary(k => k.Keyword, k => Math.Abs(k.Weight), StringComparer.OrdinalIgnoreCase);

            var processed = new List<ProcessedTicket>();
            foreach (var m in filteredData)
            {
                string msg = m.Primo_Msg_Studente ?? "";
                double posProb = 0.0;
                double negWeight = 0.0;

                foreach (var kvp in posDict)
                {
                    if (msg.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        posProb += kvp.Value;
                }
                foreach (var kvp in negDict)
                {
                    if (msg.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        negWeight += kvp.Value;
                }

                double finalProb = Math.Max(0.0, Math.Min(posProb - negWeight, 1.0));
                string status = finalProb switch
                {
                    >= 0.75 => "Chiudere",
                    >= 0.35 => "Verificare",
                    _ => "Mantenere aperto"
                };

                processed.Add(new ProcessedTicket
                {
                    ID_TICKET = m.ID_TICKET,
                    ID_STUDENTE = m.CODSTUD,
                    CODFISC = m.CODFISC,
                    Categoria = m.CATEGORIA,
                    SottoCategoria = m.SOTTOCATEGORIA,
                    Probability_Close = Math.Round(finalProb, 2),
                    Status = status,
                    Primo_Msg_Studente = m.Primo_Msg_Studente,
                    Esito_BS_24_25 = m.Esito_BS_24_25,
                    Esito_PA_24_25 = m.Esito_PA_24_25,
                    Blocchi_24_25 = m.Blocchi_24_25,
                    Esito_BS_23_24 = m.Esito_BS_23_24,
                    Blocchi_23_24 = m.Blocchi_23_24,
                    Sede_Università_24_25 = m.Sede_Università_24_25
                });
            }
            return processed;
        }

        private List<ProcessedTicket> AnalyzeMessagesForResidence(List<MergedTicket> filteredData)
        {
            if (keywordConfig == null)
                throw new InvalidOperationException("Keyword config not loaded.");

            var residenceDict = keywordConfig.ResidencePermitKeywords
                .ToDictionary(k => k.Keyword, k => k.Probability, StringComparer.OrdinalIgnoreCase);

            var processed = new List<ProcessedTicket>();
            foreach (var m in filteredData)
            {
                string msg = m.Primo_Msg_Studente ?? "";
                bool isResidence = residenceDict.Any(kvp =>
                    msg.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!isResidence) continue;

                processed.Add(new ProcessedTicket
                {
                    ID_TICKET = m.ID_TICKET,
                    ID_STUDENTE = m.CODSTUD,
                    CODFISC = m.CODFISC,
                    Categoria = m.CATEGORIA,
                    SottoCategoria = m.SOTTOCATEGORIA,
                    Primo_Msg_Studente = m.Primo_Msg_Studente,
                    Status = "Verificare Permesso",
                    Esito_BS_24_25 = m.Esito_BS_24_25,
                    Esito_PA_24_25 = m.Esito_PA_24_25,
                    Blocchi_24_25 = m.Blocchi_24_25,
                    Esito_BS_23_24 = m.Esito_BS_23_24,
                    Blocchi_23_24 = m.Blocchi_23_24,
                    Sede_Università_24_25 = m.Sede_Università_24_25,
                    IsResidencePermitRelated = true
                });
            }
            return processed;
        }

        private List<ProcessedTicket> AnalyzeMessagesForPayment(List<MergedTicket> filteredData)
        {
            if (keywordConfig == null)
                throw new InvalidOperationException("Keyword config not loaded.");

            var paymentDict = keywordConfig.PaymentKeywords != null
                ? keywordConfig.PaymentKeywords.ToDictionary(k => k.Keyword, k => k.Probability, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, double>();

            var processed = new List<ProcessedTicket>();
            foreach (var m in filteredData)
            {
                string msg = m.Primo_Msg_Studente ?? "";
                bool isPayment = paymentDict.Any(kvp =>
                    msg.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0);

                processed.Add(new ProcessedTicket
                {
                    ID_TICKET = m.ID_TICKET,
                    ID_STUDENTE = m.CODSTUD,
                    CODFISC = m.CODFISC,
                    Categoria = m.CATEGORIA,
                    SottoCategoria = m.SOTTOCATEGORIA,
                    Primo_Msg_Studente = m.Primo_Msg_Studente,
                    Status = isPayment ? "Verificare Pagamento" : "Non correlato",
                    Esito_BS_24_25 = m.Esito_BS_24_25,
                    Esito_PA_24_25 = m.Esito_PA_24_25,
                    Blocchi_24_25 = m.Blocchi_24_25,
                    Esito_BS_23_24 = m.Esito_BS_23_24,
                    Blocchi_23_24 = m.Blocchi_23_24,
                    Sede_Università_24_25 = m.Sede_Università_24_25,
                    IsPaymentKeywordPresent = isPayment
                });
            }
            return processed;
        }

        private List<ProcessedTicket> AnalyzeMessagesForTransfer(List<MergedTicket> filteredData)
        {
            if (keywordConfig == null || keywordConfig.TransferKeywords == null)
                throw new InvalidOperationException("Keyword config not loaded.");

            // Precompile regex patterns for each group's keywords
            var groupRegexes = keywordConfig.TransferKeywords.Select(group => new
            {
                GroupName = group.Group,
                Weight = group.Weight,
                // Create regexes for each keyword in the group (match whole words)
                Patterns = group.Keywords.Select(k => new Regex(@"\b" + Regex.Escape(k.ToLowerInvariant()) + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase))
                                          .ToList()
            }).ToList();

            var processed = new List<ProcessedTicket>();
            foreach (var m in filteredData)
            {
                string msg = m.Primo_Msg_Studente ?? "";
                string lowerMsg = msg.ToLowerInvariant();
                double transferProbability = 0;

                foreach (var group in groupRegexes)
                {
                    bool foundInGroup = group.Patterns.Any(p => p.IsMatch(lowerMsg));
                    if (foundInGroup)
                    {
                        transferProbability += group.Weight;
                    }
                }


                processed.Add(new ProcessedTicket
                {
                    ID_TICKET = m.ID_TICKET,
                    ID_STUDENTE = m.CODSTUD,
                    CODFISC = m.CODFISC,
                    Categoria = m.CATEGORIA,
                    SottoCategoria = m.SOTTOCATEGORIA,
                    Primo_Msg_Studente = m.Primo_Msg_Studente,
                    TransferProbability = transferProbability,
                    Esito_BS_24_25 = m.Esito_BS_24_25,
                    Esito_PA_24_25 = m.Esito_PA_24_25,
                    Blocchi_24_25 = m.Blocchi_24_25,
                    Esito_BS_23_24 = m.Esito_BS_23_24,
                    Blocchi_23_24 = m.Blocchi_23_24,
                    Sede_Università_24_25 = m.Sede_Università_24_25
                });
            }
            return processed;
        }

        #endregion

        #region 11) Payment
        private void UpdatePaymentDetails(List<ProcessedTicket> tickets, List<string> codFiscList)
        {
            if (tickets.Count == 0 || codFiscList.Count == 0) return;

            var payRecords = FetchPayments(codFiscList);
            foreach (var tk in tickets)
            {
                var matching = payRecords
                    .Where(p => p.CODICE_FISCALE.Equals(tk.CODFISC, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matching.Any())
                {
                    tk.IsInPayment = true;
                    tk.PaymentAmount = matching.Sum(p => p.IMPORTO);
                    tk.PaymentDate = matching.Max(p => p.DATA_INSERIMENTO);
                }
            }
        }

        private List<PaymentRecord> FetchPayments(List<string> codFiscList)
        {
            var res = new List<PaymentRecord>();
            if (codFiscList.Count == 0) return res;

            using (var transaction = CONNECTION.BeginTransaction())
            {
                try
                {
                    string createTmp = @"
                        IF OBJECT_ID('tempdb..#tmpCodFisc') IS NOT NULL
                            DROP TABLE #tmpCodFisc;
                        CREATE TABLE #tmpCodFisc (Cod_fiscale VARCHAR(20) NOT NULL);
                        ";
                    using (var cmd = new SqlCommand(createTmp, CONNECTION, transaction))
                        cmd.ExecuteNonQuery();

                    // bulk
                    DataTable dt = new DataTable();
                    dt.Columns.Add("Cod_fiscale", typeof(string));
                    foreach (var cf in codFiscList) dt.Rows.Add(cf);

                    using (SqlBulkCopy bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, transaction))
                    {
                        bulk.DestinationTableName = "#tmpCodFisc";
                        bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
                        bulk.WriteToServer(dt);
                    }

                    // query
                    string paymentQuery = @"
                        SELECT 
                            MCEE.CODICE_FISCALE, 
                            MCEE.IMPORTO, 
                            MCEE.DATA_INSERIMENTO
                        FROM MOVIMENTI_CONTABILI_ELEMENTARI MCEE
                        INNER JOIN MOVIMENTI_CONTABILI_GENERALI MCG 
                            ON MCEE.CODICE_MOVIMENTO = MCG.CODICE_MOVIMENTO
                        WHERE MCG.COD_MANDATO LIKE '%BS%'
                          AND MCEE.CODICE_FISCALE IN (SELECT Cod_fiscale FROM #tmpCodFisc)
                        ";
                    using (SqlCommand cmdPay = new SqlCommand(paymentQuery, CONNECTION, transaction))
                    using (SqlDataReader r = cmdPay.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            res.Add(new PaymentRecord
                            {
                                CODICE_FISCALE = r["CODICE_FISCALE"]?.ToString() ?? "",
                                IMPORTO = Convert.ToDecimal(r["IMPORTO"]),
                                DATA_INSERIMENTO = Convert.ToDateTime(r["DATA_INSERIMENTO"])
                            });
                        }
                    }
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception("Error fetching payment data: " + ex.Message, ex);
                }
            }

            return res;
        }
        #endregion

        #region 9) Output CSV
        public void WriteClosureTicketsCsv(List<ProcessedTicket> closureTickets, string folderPath)
        {
            string outputPath = Path.Combine(folderPath, "ClosureTickets.csv");
            var sb = new StringBuilder();

            sb.AppendLine("ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;PROBABILITY;CHIUSURA_LABEL;FULL_MESSAGE");

            foreach (var c in closureTickets)
            {
                sb.AppendLine(string.Join(";",
                    c.ID_TICKET,
                    c.ID_STUDENTE,
                    c.CODFISC,
                    EscapeForCsv(c.Categoria),
                    EscapeForCsv(c.SottoCategoria),
                    c.Probability_Close.ToString(CultureInfo.InvariantCulture),
                    EscapeForCsv(c.Status),
                    EscapeForCsv(c.Primo_Msg_Studente)
                ));
            }

            // Write with UTF-8 BOM
            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(outputPath, sb.ToString(), utf8Bom);
        }

        /// <summary>
        /// ADD the esiti 23/24 and 24/25 plus the blocks to the residence CSV
        /// (ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;ESITO_BS_23_24;BLOCCHI_23_24;ESITO_BS_24_25;BLOCCHI_24_25;FULL_MESSAGE)
        /// </summary>
        public void WriteResidenceTicketsCsv(List<ProcessedTicket> residenceTickets, string folderPath)
        {
            string outputPath = Path.Combine(folderPath, "ResidenceTickets.csv");
            var sb = new StringBuilder();

            sb.AppendLine("ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;ESITO_BS_23_24;BLOCCHI_23_24;ESITO_BS_24_25;BLOCCHI_24_25;FULL_MESSAGE");

            foreach (var r in residenceTickets)
            {
                sb.AppendLine(string.Join(";",
                    r.ID_TICKET,
                    r.ID_STUDENTE,
                    r.CODFISC,
                    EscapeForCsv(r.Categoria),
                    EscapeForCsv(r.SottoCategoria),
                    EscapeForCsv(r.Esito_BS_23_24),
                    EscapeForCsv(r.Blocchi_23_24),
                    EscapeForCsv(r.Esito_BS_24_25),
                    EscapeForCsv(r.Blocchi_24_25),
                    EscapeForCsv(r.Primo_Msg_Studente)
                ));
            }

            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(outputPath, sb.ToString(), utf8Bom);
        }

        /// <summary>
        /// ADD the esiti 23/24 and 24/25 plus the blocks to payment CSV
        /// (ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;ESITO_BS_23_24;BLOCCHI_23_24;ESITO_BS_24_25;BLOCCHI_24_25;PAYMENT_AMOUNT;PAYMENT_DATE;FULL_MESSAGE)
        /// </summary>
        public void WritePaymentTicketsCsv(List<ProcessedTicket> paymentTickets, string folderPath)
        {
            string outputPath = Path.Combine(folderPath, "PaymentTickets.csv");
            var sb = new StringBuilder();

            sb.AppendLine("ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;ESITO_BS_23_24;BLOCCHI_23_24;ESITO_BS_24_25;BLOCCHI_24_25;PAYMENT_AMOUNT;PAYMENT_DATE;FULL_MESSAGE");

            foreach (var p in paymentTickets)
            {
                string dateStr = p.PaymentDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                sb.AppendLine(string.Join(";",
                    p.ID_TICKET,
                    p.ID_STUDENTE,
                    p.CODFISC,
                    EscapeForCsv(p.Categoria),
                    EscapeForCsv(p.SottoCategoria),
                    EscapeForCsv(p.Esito_BS_23_24),
                    EscapeForCsv(p.Blocchi_23_24),
                    EscapeForCsv(p.Esito_BS_24_25),
                    EscapeForCsv(p.Blocchi_24_25),
                    p.PaymentAmount.ToString(CultureInfo.InvariantCulture),
                    dateStr,
                    EscapeForCsv(p.Primo_Msg_Studente)
                ));
            }

            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(outputPath, sb.ToString(), utf8Bom);
        }

        public void WriteTransferTicketsCsv(List<ProcessedTicket> transferTickets, string folderPath)
        {
            string outputPath = Path.Combine(folderPath, "TransferTickets.csv");
            var sb = new StringBuilder();

            sb.AppendLine("ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;ESITO_BS_23_24;BLOCCHI_23_24;ESITO_BS_24_25;BLOCCHI_24_25;TRANSFER_PROBABILITY;FULL_MESSAGE");

            foreach (var p in transferTickets)
            {
                sb.AppendLine(string.Join(";",
                    p.ID_TICKET,
                    p.ID_STUDENTE,
                    p.CODFISC,
                    EscapeForCsv(p.Categoria),
                    EscapeForCsv(p.SottoCategoria),
                    EscapeForCsv(p.Esito_BS_23_24),
                    EscapeForCsv(p.Blocchi_23_24),
                    EscapeForCsv(p.Esito_BS_24_25),
                    EscapeForCsv(p.Blocchi_24_25),
                    p.TransferProbability,
                    EscapeForCsv(p.Primo_Msg_Studente)
                ));
            }

            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(outputPath, sb.ToString(), utf8Bom);
        }

        private string EscapeForCsv(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Minimal approach: remove line breaks
            return s.Replace("\r", " ").Replace("\n", " ");
        }
        #endregion
    }

    #region Models
    internal class StudentTicket
    {
        public string? NREC { get; set; }
        public string? ID_TICKET { get; set; }
        public string? CODSTUD { get; set; }   // This becomes ID_STUDENTE in final CSV
        public string? COGNOME { get; set; }
        public string? NOME { get; set; }
        public string? CODFISC { get; set; }
        public string? OGGETTO { get; set; }
        public string? CATEGORIA { get; set; }
        public string? SOTTOCATEGORIA { get; set; }
        public string? ANNO_ACCADEMICO_RICHIESTA { get; set; }
        public string? STATO { get; set; }
        public string? DATA_CREAZIONE { get; set; }
        public string? PRIMO_MSG_STUDENTE { get; set; }
        public string? PRIMO_MSG_OPERATORE { get; set; }
        public string? UID { get; set; }
        public string? DATA_ULTIMO_MESSAGGIO { get; set; }
    }

    internal class DatabaseRecord
    {
        public string? Cod_fiscale { get; set; }
        public string? Esito_BS_24_25 { get; set; }
        public string? Esito_PA_24_25 { get; set; }
        public string? Blocchi_24_25 { get; set; }
        public string? Esito_BS_23_24 { get; set; }
        public string? Blocchi_23_24 { get; set; }
        public string? Sede_Università_24_25 { get; set; }
    }

    internal class MergedTicket
    {
        public string? ID_TICKET { get; set; }
        public string? CODSTUD { get; set; }
        public string? CODFISC { get; set; }
        public string? Esito_BS_24_25 { get; set; }
        public string? Esito_PA_24_25 { get; set; }
        public string? Blocchi_24_25 { get; set; }
        public string? Esito_BS_23_24 { get; set; }
        public string? Blocchi_23_24 { get; set; }
        public string? Sede_Università_24_25 { get; set; }
        public string? Primo_Msg_Studente { get; set; }
        public string? CATEGORIA { get; set; }
        public string? SOTTOCATEGORIA { get; set; }
        public string? PRIMO_MSG_OPERATORE { get; set; }
        public string? STATOTK { get; set; }
    }

    internal class ProcessedTicket
    {
        public string? ID_TICKET { get; set; }
        public string? ID_STUDENTE { get; set; }  // from CODSTUD
        public string? CODFISC { get; set; }
        public string? Categoria { get; set; }
        public string? SottoCategoria { get; set; }

        public double Probability_Close { get; set; }
        public string? Status { get; set; }
        public string? Primo_Msg_Studente { get; set; }

        public string? Esito_BS_24_25 { get; set; }
        public string? Esito_PA_24_25 { get; set; }
        public string? Blocchi_24_25 { get; set; }
        public string? Esito_BS_23_24 { get; set; }
        public string? Blocchi_23_24 { get; set; }
        public string? Sede_Università_24_25 { get; set; }

        public bool IsResidencePermitRelated { get; set; }
        public bool IsPaymentKeywordPresent { get; set; }

        public bool IsInPayment { get; set; }
        public decimal PaymentAmount { get; set; }
        public DateTime? PaymentDate { get; set; }

        public double? TransferProbability { get; set; }
    }

    internal class PaymentRecord
    {
        public string CODICE_FISCALE { get; set; } = "";
        public decimal IMPORTO { get; set; }
        public DateTime DATA_INSERIMENTO { get; set; }
    }
    #endregion

    #region Keywords
    internal class KeywordConfig
    {
        public List<PositiveKeyword> PositiveKeywords { get; set; } = new();
        public List<NegativeKeyword> NegativeKeywords { get; set; } = new();
        public List<PaymentKeyword> PaymentKeywords { get; set; } = new();
        public List<KeywordProbability> ResidencePermitKeywords { get; set; } = new();
        public List<KeywordTransfer> TransferKeywords { get; set; } = new();
    }

    internal class PositiveKeyword
    {
        public string Keyword { get; set; } = "";
        public double Probability { get; set; }
    }

    internal class NegativeKeyword
    {
        public string Keyword { get; set; } = "";
        public double Weight { get; set; }
    }

    internal class PaymentKeyword
    {
        public string Keyword { get; set; } = "";
        public double Probability { get; set; }
    }

    internal class KeywordProbability
    {
        public string Keyword { get; set; } = "";
        public double Probability { get; set; }
    }

    internal class KeywordTransfer
    {
        public double Weight { get; set; }
        public string Group { get; set; } = "";
        public List<string> Keywords { get; set; } = new();
    }
    #endregion
}
