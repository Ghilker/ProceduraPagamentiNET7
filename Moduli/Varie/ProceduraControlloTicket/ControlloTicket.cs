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

            // 1) Load keywords
            LoadKeywords();

            // 2) Read CSV
            DataTable csvData = CsvToDataTable(args.SelectedCsvPath);

            // 3) Convert -> model
            List<StudentTicket> tickets = ConvertToStudentTickets(csvData);

            // 4) Distinct CF list
            var codFiscList = tickets
                .Select(t => t.CODFISC)
                .Where(cf => !string.IsNullOrWhiteSpace(cf))
                .Distinct()
                .ToList();

            // 5) Fetch status_compilazione from DB
            var dbStatusData = FetchStatusData(codFiscList);

            // 6) Merge
            var mergedData = MergeData(tickets, dbStatusData);

            // 7) Filter
            var filtered = ApplyFilters(mergedData);

            // 8) Analyze
            var analyzed = AnalyzeMessages(filtered);

            // 9) Save & CSV
            ProcessedTickets = analyzed.ToList();

            string? inputDir = Path.GetDirectoryName(args.SelectedCsvPath);
            if (string.IsNullOrEmpty(inputDir))
                inputDir = AppDomain.CurrentDomain.BaseDirectory;

            WriteTicketsCsv(ProcessedTickets, inputDir);

            Logger.LogInfo(100, "Fine lavorazione");
        }

        #region 1) Load Keywords
        private void LoadKeywords()
        {
            try
            {
                string jsonFilePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Moduli",
                    "Varie",
                    "ProceduraControlloTicket",
                    "ControlloTicketKeywords.json");

                if (!File.Exists(jsonFilePath))
                {
                    Logger.LogWarning(100, $"ControlloTicketKeywords.json not found at {jsonFilePath}. Proceeding with empty keyword list.");
                    keywordConfig = new KeywordConfig();
                    return;
                }

                string content = File.ReadAllText(jsonFilePath, Encoding.UTF8);
                keywordConfig = JsonConvert.DeserializeObject<KeywordConfig>(content) ?? new KeywordConfig();

                // Deduplicate (case-insensitive by keyword string)
                keywordConfig.Keywords = keywordConfig.Keywords
                    .GroupBy(k => k.Keyword, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                keywordConfig.NegativeKeywords = keywordConfig.NegativeKeywords
                    .GroupBy(k => k.Keyword, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
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

            using (var reader = new StreamReader(csvFilePath, Encoding.GetEncoding(1252)))
            {
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var values = line.Split(';');

                    if (isFirstRow)
                    {
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

        #region 5) Fetch Status Data
        private List<StatusRecord> FetchStatusData(List<string> codFiscList)
        {
            var result = new List<StatusRecord>();
            if (codFiscList.Count == 0) return result;

            using (var transaction = CONNECTION.BeginTransaction())
            {
                try
                {
                    string createTmp = @"
IF OBJECT_ID('tempdb..#tmpCodFisc') IS NOT NULL DROP TABLE #tmpCodFisc;
CREATE TABLE #tmpCodFisc (Cod_fiscale VARCHAR(20) COLLATE Latin1_General_CI_AS NOT NULL);";
                    using (var cmd = new SqlCommand(createTmp, CONNECTION, transaction))
                        cmd.ExecuteNonQuery();

                    // bulk insert temp cod fisc
                    DataTable dt = new DataTable();
                    dt.Columns.Add("Cod_fiscale", typeof(string));
                    foreach (var cf in codFiscList) dt.Rows.Add(cf);

                    using (SqlBulkCopy bulk = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, transaction))
                    {
                        bulk.DestinationTableName = "#tmpCodFisc";
                        bulk.ColumnMappings.Add("Cod_fiscale", "Cod_fiscale");
                        bulk.WriteToServer(dt);
                    }

                    string sql = @"
SELECT
    d.Anno_accademico,
    d.Num_domanda,
    d.Cod_fiscale,
    d.Data_validita,
    d.Utente,
    d.Tipo_bando,
    d.Id_Domanda,
    d.DataCreazioneRecord,
    vs.anno_accademico AS Vs_Anno_accademico,
    vs.num_domanda     AS Vs_Num_domanda,
    vs.status_compilazione,
    vs.data_validita   AS Vs_Data_validita,
    vs.utente          AS Vs_Utente,
    vs.lettura,
    vs.Id_Domanda      AS Vs_Id_Domanda
FROM Domanda AS d
INNER JOIN vStatus_compilazione AS vs
    ON d.Anno_accademico = vs.anno_accademico
   AND d.Num_domanda     = vs.num_domanda
WHERE d.Anno_accademico = '20252026'
  AND d.Cod_fiscale IN (SELECT Cod_fiscale FROM #tmpCodFisc);";

                    using (var cmdBatch = new SqlCommand(sql, CONNECTION, transaction))
                    using (SqlDataReader reader = cmdBatch.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new StatusRecord
                            {
                                Cod_fiscale = reader["Cod_fiscale"]?.ToString(),
                                Anno_accademico = reader["Anno_accademico"]?.ToString(),
                                Num_domanda = reader["Num_domanda"]?.ToString(),
                                Status_compilazione = SafeToInt(reader["status_compilazione"]),
                                Id_Domanda = reader["Id_Domanda"]?.ToString()
                            });
                        }
                    }

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw new Exception("Error fetching status data: " + ex.Message, ex);
                }
            }

            return result;
        }

        private int SafeToInt(object? value)
        {
            if (value == null || value == DBNull.Value) return 0;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                int.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out int v);
                return v;
            }
        }
        #endregion

        #region 6) Merge
        private List<MergedTicket> MergeData(List<StudentTicket> tickets, List<StatusRecord> dbData)
        {
            var merged = from t in tickets
                         join db in dbData on t.CODFISC equals db.Cod_fiscale into dbGroup
                         from db in dbGroup.OrderByDescending(d => d.Status_compilazione).Take(1).DefaultIfEmpty()
                         select new MergedTicket
                         {
                             ID_TICKET = t.ID_TICKET,
                             CODSTUD = t.CODSTUD,
                             CODFISC = t.CODFISC,
                             Primo_Msg_Studente = t.PRIMO_MSG_STUDENTE,
                             CATEGORIA = t.CATEGORIA,
                             SOTTOCATEGORIA = t.SOTTOCATEGORIA,
                             PRIMO_MSG_OPERATORE = t.PRIMO_MSG_OPERATORE,
                             STATOTK = t.STATO,
                             Status_compilazione = db?.Status_compilazione ?? 0,
                             Num_domanda = db?.Num_domanda,
                             Anno_accademico = db?.Anno_accademico
                         };
            return merged.ToList();
        }
        #endregion

        #region 7) Filter + Analyze
        private List<MergedTicket> ApplyFilters(List<MergedTicket> mergedData)
        {
            return mergedData.Where(m =>
                m.STATOTK != null &&
                !m.STATOTK.Equals("CHIUSO", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(m.PRIMO_MSG_OPERATORE) &&
                m.Status_compilazione >= 90
            ).ToList();
        }

        private List<ProcessedTicket> AnalyzeMessages(List<MergedTicket> filteredData)
        {
            if (keywordConfig == null)
                throw new InvalidOperationException("Keyword config not loaded.");

            var posRules = keywordConfig.Keywords ?? new List<WeightedKeyword>();
            var negRules = keywordConfig.NegativeKeywords ?? new List<WeightedKeyword>();

            var processed = new List<ProcessedTicket>();

            foreach (var m in filteredData)
            {
                string rawMsg = m.Primo_Msg_Studente ?? "";
                string msg = NormalizeForMatch(rawMsg);

                // avoid double counting same keyword
                var matchedPos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var matchedNeg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                double posWeight = 0.0;
                double negWeight = 0.0;

                foreach (var kw in posRules)
                {
                    if (IsMatch(msg, kw.Keyword, kw.IsRegex) && matchedPos.Add(kw.Keyword))
                        posWeight += kw.Weight;
                }
                foreach (var kw in negRules)
                {
                    if (IsMatch(msg, kw.Keyword, kw.IsRegex) && matchedNeg.Add(kw.Keyword))
                        negWeight += kw.Weight;
                }

                // Scores with smooth exponential curve
                double posScore = 1.0 - Math.Exp(-posWeight);
                double negScore = 1.0 - Math.Exp(-negWeight);

                double final = posScore * (1.0 - 0.6 * negScore);
                final = Math.Clamp(final, 0.0, 1.0);

                string label = final switch
                {
                    >= 0.65 => "Chiudere",
                    >= 0.30 => "Verificare",
                    _ => "Mantenere aperto"
                };

                processed.Add(new ProcessedTicket
                {
                    ID_TICKET = m.ID_TICKET,
                    ID_STUDENTE = m.CODSTUD,
                    CODFISC = m.CODFISC,
                    Categoria = m.CATEGORIA,
                    SottoCategoria = m.SOTTOCATEGORIA,
                    Primo_Msg_Studente = rawMsg,
                    Probability = Math.Round(final, 3),
                    PositiveScore = Math.Round(posScore, 3),
                    NegativeScore = Math.Round(negScore, 3),
                    StatusLabel = label,
                    KeywordMatch = matchedPos.Count > 0,
                    Status_compilazione = m.Status_compilazione,
                    Num_domanda = m.Num_domanda,
                    Anno_accademico = m.Anno_accademico
                });
            }

            return processed;
        }

        private static bool IsMatch(string text, string pattern, bool isRegex)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;

            if (isRegex)
            {
                try
                {
                    return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch
                {
                    // Fallback to simple contains if the regex is invalid
                    return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            // simple substring match for stems
            return text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeForMatch(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Lower + remove diacritics + collapse spaces
            string lower = input.ToLowerInvariant();
            string noAccents = RemoveDiacritics(lower);

            return Regex.Replace(noAccents, @"\s+", " ").Trim();
        }

        private static string RemoveDiacritics(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(c);
                if (uc != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
        #endregion

        #region 9) Output CSV
        public void WriteTicketsCsv(List<ProcessedTicket> tickets, string folderPath)
        {
            string outputPath = Path.Combine(folderPath, "TicketsStatus.csv");
            var sb = new StringBuilder();

            sb.AppendLine("ID_TICKET;ID_STUDENTE;COD_FISCALE;CATEGORIA;SOTTOCATEGORIA;STATUS_COMPILAZIONE;POS_SCORE;NEG_SCORE;PROBABILITY;KEYWORD_MATCH;LABEL;FULL_MESSAGE");

            foreach (var t in tickets)
            {
                sb.AppendLine(string.Join(";",
                    t.ID_TICKET,
                    t.ID_STUDENTE,
                    t.CODFISC,
                    EscapeForCsv(t.Categoria),
                    EscapeForCsv(t.SottoCategoria),
                    t.Status_compilazione.ToString(CultureInfo.InvariantCulture),
                    t.PositiveScore.ToString(CultureInfo.InvariantCulture),
                    t.NegativeScore.ToString(CultureInfo.InvariantCulture),
                    t.Probability.ToString(CultureInfo.InvariantCulture),
                    t.KeywordMatch ? "1" : "0",
                    EscapeForCsv(t.StatusLabel),
                    EscapeForCsv(t.Primo_Msg_Studente)
                ));
            }

            var utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(outputPath, sb.ToString(), utf8Bom);
        }

        private string EscapeForCsv(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ");
        }
        #endregion
    }

    #region Models
    internal class StudentTicket
    {
        public string? NREC { get; set; }
        public string? ID_TICKET { get; set; }
        public string? CODSTUD { get; set; }
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

    internal class StatusRecord
    {
        public string? Cod_fiscale { get; set; }
        public string? Anno_accademico { get; set; }
        public string? Num_domanda { get; set; }
        public int Status_compilazione { get; set; }
        public string? Id_Domanda { get; set; }
    }

    internal class MergedTicket
    {
        public string? ID_TICKET { get; set; }
        public string? CODSTUD { get; set; }
        public string? CODFISC { get; set; }
        public string? Primo_Msg_Studente { get; set; }
        public string? CATEGORIA { get; set; }
        public string? SOTTOCATEGORIA { get; set; }
        public string? PRIMO_MSG_OPERATORE { get; set; }
        public string? STATOTK { get; set; }

        public int Status_compilazione { get; set; }
        public string? Num_domanda { get; set; }
        public string? Anno_accademico { get; set; }
    }

    internal class ProcessedTicket
    {
        public string? ID_TICKET { get; set; }
        public string? ID_STUDENTE { get; set; }
        public string? CODFISC { get; set; }
        public string? Categoria { get; set; }
        public string? SottoCategoria { get; set; }
        public string? Primo_Msg_Studente { get; set; }

        public double Probability { get; set; }
        public double PositiveScore { get; set; }
        public double NegativeScore { get; set; }
        public string? StatusLabel { get; set; }
        public bool KeywordMatch { get; set; }

        public int Status_compilazione { get; set; }
        public string? Num_domanda { get; set; }
        public string? Anno_accademico { get; set; }
    }
    #endregion

    #region Keywords
    internal class KeywordConfig
    {
        public List<WeightedKeyword> Keywords { get; set; } = new();
        public List<WeightedKeyword> NegativeKeywords { get; set; } = new();
    }

    internal class WeightedKeyword
    {
        public string Keyword { get; set; } = "";
        public double Weight { get; set; } = 0.4;
        public bool IsRegex { get; set; } = false;
    }
    #endregion
}
