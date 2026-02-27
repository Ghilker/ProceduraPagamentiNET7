using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace ProcedureNet7
{
    // ====== ENGINE PORTED FROM TicketCFMenu ======
    internal sealed class YearInfo
    {
        public HashSet<string> BlocchiParts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EsitiBS { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EsitiPA { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SediStudi { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SediDescrizioni { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasPrimaRata { get; set; }
        public bool HasSaldo { get; set; }
        public bool HasRimborso { get; set; }

        public string BlocchiJoined => BlocchiParts.Count == 0 ? "" : string.Join(" | ", BlocchiParts.OrderBy(x => x));
        public string EsitoBSJoined => EsitiBS.Count == 0 ? "" : string.Join(" | ", EsitiBS.OrderBy(x => x));
        public string EsitoPAJoined => EsitiPA.Count == 0 ? "" : string.Join(" | ", EsitiPA.OrderBy(x => x));
        public string SediStudiJoined => SediStudi.Count == 0 ? "" : string.Join(" | ", SediStudi.OrderBy(x => x));
        public string SediDescrizioniJoined => SediDescrizioni.Count == 0 ? "" : string.Join(" | ", SediDescrizioni.OrderBy(x => x));
    }


    internal class ProceduraTicket : BaseProcedure<ArgsProceduraTicket>
    {
        private bool _isFileWithMessagges = true;
        private bool _sendMail;

        // anni target configurabili
        private readonly List<int> _targetYears = new() { 20242025, 20252026 };

        public ProceduraTicket(MasterForm masterForm, SqlConnection mainConn) : base(masterForm, mainConn) { }

        public override void RunProcedure(ArgsProceduraTicket args)
        {
            _sendMail = args._ticketChecks[0];

            string ticketFilePath = args._ticketFilePath;
            string mailFilePath = args._mailFilePath;
            string senderMail = string.Empty;
            string senderPassword = string.Empty;

            _masterForm.inProcedure = true;
            try
            {
                Logger.Log(1, "Caricamento file", LogLevel.INFO);
                DataTable tickets = Utilities.CsvToDataTable(ticketFilePath);

                SyncValidazioneTicket(tickets, CONNECTION);

                var unwantedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Contributi Alloggio","Posti Alloggio","MENSA","Accettazione Posto Alloggio",
                    "RIMBORSO DEPOSITO CAUZIONALE","AREA 8","Ufficio Inclusione","URP",
                    "CERTIFICAZIONE UNICA","Portafuturo","RIMBORSO DEPOSITO CAUZIONALE-Non più attiva"
                };

                var filteredRows = tickets.AsEnumerable()
                    .Where(row =>
                        !EqualsCI(row.Field<string>("STATO"), "CHIUSO") &&
                        !unwantedCategories.Contains(row.Field<string>("CATEGORIA")) &&
                        (!_isFileWithMessagges ? !EqualsCI(row.Field<string>("AZIONE"), "PRESA_IN_CARICO") : true) &&
                        (_isFileWithMessagges
                            ? string.IsNullOrWhiteSpace(row.Field<string>("PRIMO_MSG_OPERATORE"))
                            : row.Field<string>("NUM_RICHIESTE_STUDENTE") != "0")
                    );

                if (!filteredRows.Any())
                    throw new Exception("Nessun ticket presente nel file che soddisfa i requisiti");

                tickets = filteredRows.CopyToDataTable();

                // drop colonne non necessarie
                string[] columnsToRemove = _isFileWithMessagges
                    ? new[] { "NREC", "PRIMO_MSG_OPERATORE", "UID", "DATA_LOG" }
                    : new[] { "NREC", "NUM_TICKET_CREATI_STUDENTE", "NUM_RICHIESTE_STUDENTE", "NUM_RISPOSTE_OPERATORE", "DATA_ULTIMO_MESSAGGIO", "AZIONE", "UID", "DATA_LOG" };

                foreach (var c in columnsToRemove)
                    if (tickets.Columns.Contains(c)) tickets.Columns.Remove(c);

                tickets.DefaultView.Sort = "CODFISC ASC";
                tickets = tickets.DefaultView.ToTable();

                // ===== ENRICH FROM DB =====
                var cfList = tickets.AsEnumerable()
                                    .Select(r => SafeStr(r, "CODFISC"))
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                var presence = GetAnnoPresenceByCF_Smart(cfList, _targetYears, CONNECTION);
                var yearInfo = GetYearInfoByCF_Smart(cfList, _targetYears, CONNECTION);

                var psAllDocsWorked = GetPsAllDocsWorkedByCF(cfList, CONNECTION);

                var domicilioInfo = GetDomicilioInfoByCF(cfList, _targetYears, CONNECTION);


                // aggiungi colonne per anni target
                EnsureTicketColumns(tickets, _targetYears);

                // keyword labels (etichette sole)
                EnsureTopicColumns(tickets);

                EnsureDomicilioColumns(tickets);

                foreach (DataRow r in tickets.Rows)
                {
                    var cf = SafeStr(r, "CODFISC");

                    foreach (var y in _targetYears)
                    {
                        var hasYear = presence.TryGetValue(cf, out var anni) && anni.Contains(y);
                        r[$"DOMANDA_{y}"] = hasYear ? "SI" : "NO";

                        if (yearInfo.TryGetValue(cf, out var perYear) && perYear.TryGetValue(y, out var info))
                        {
                            r[$"BLOCCHI_{y}"] = info.BlocchiJoined;
                            r[$"ESITO_BS_{y}"] = info.EsitoBSJoined;
                            r[$"ESITO_PA_{y}"] = info.EsitoPAJoined;
                            r[$"SEDE_DESCR_{y}"] = info.SediDescrizioniJoined;
                            r[$"HA_PRIMA_RATA_{y}"] = info.HasPrimaRata ? "SI" : "";
                            r[$"HA_SALDO_{y}"] = info.HasSaldo ? "SI" : "";
                            r[$"HA_RIMBORSO_{y}"] = info.HasRimborso ? "SI" : "";
                        }
                        else
                        {
                            r[$"BLOCCHI_{y}"] = "";
                            r[$"ESITO_BS_{y}"] = "";
                            r[$"ESITO_PA_{y}"] = "";
                            r[$"SEDE_DESCR_{y}"] = "";
                            r[$"HA_PRIMA_RATA_{y}"] = "";
                            r[$"HA_SALDO_{y}"] = "";
                            r[$"HA_RIMBORSO_{y}"] = "";
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(cf) &&
                        psAllDocsWorked.TryGetValue(cf, out bool allPsOk) &&
                        allPsOk)
                    {
                        r["PS_DOCUMENTI_LAVORATI"] = "SI";
                    }
                    else
                    {
                        r["PS_DOCUMENTI_LAVORATI"] = "";
                    }

                    // keyword engine V6: solo etichette
                    var text = SafeStr(r, "PRIMO_MSG_STUDENTE");
                    var ext = KeywordEngineV6.Extract(text);
                    r["ARGOMENTO_PRIMARIO"] = ext.TopicPrimary ?? "";
                    r["ARGOMENTO_SECONDARIO"] = ext.TopicSecondary ?? "";
                    r["RIFERITO_A_BLOCCHI"] = ext.TopicTertiary ?? "";

                    if (domicilioInfo.TryGetValue(cf, out var dom))
                    {
                        r["ISTANZA_DOMICILIO_APERTA"] = dom.HasOpen ? "SI" : "";
                        r["ISTANZA_DOMICILIO_LAVORATA"] = dom.HasWorked ? "SI" : "";
                    }
                    else
                    {
                        r["ISTANZA_DOMICILIO_APERTA"] = "";
                        r["ISTANZA_DOMICILIO_LAVORATA"] = "";
                    }

                }

                SyncTopicsToValidazioneTicket(tickets, CONNECTION);

                // export
                string directoryPath = Path.GetDirectoryName(ticketFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string excelFile = Utilities.ExportDataTableToExcel(tickets, directoryPath);

                if (_sendMail)
                {
                    List<string> toEmails = new();
                    List<string> ccEmails = new();
                    using (var sr = new StreamReader(mailFilePath, Encoding.UTF8))
                    {
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.StartsWith("TO#", StringComparison.OrdinalIgnoreCase)) toEmails.Add(line[3..].Trim());
                            else if (line.StartsWith("CC#", StringComparison.OrdinalIgnoreCase)) ccEmails.Add(line[3..].Trim());
                            else if (line.StartsWith("ID#", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(senderMail)) senderMail = line[3..].Trim();
                            else if (line.StartsWith("PW#", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(senderPassword)) senderPassword = line[3..].Trim();
                        }
                    }

                    Logger.Log(97, "Preparazione mail", LogLevel.INFO);

                    var smtpClient = new SmtpClient("smtp.gmail.com")
                    {
                        Port = 587,
                        Credentials = new NetworkCredential(senderMail, senderPassword),
                        EnableSsl = true,
                        UseDefaultCredentials = false
                    };

                    string messageBody = @"
                        <p>Buongiorno,</p>
                        <p>vi invio l'estrazione dei ticket aperti e nuovi dal 01/01/2025 
                        con gli esiti di borsa e i blocchi presenti, integrato con le università
                        di appartenenza dello studente.</p>";

                    messageBody += @"
                        <p>I ticket contengono anche il primo messaggio che lo studente 
                        ha inviato, dati su blocchi ed esiti ed in più una sezione che cerca di riassumere il contenuto
                        del ticket così da facilitare la lavorazione e migliorare la distribuzione per funzioni.
                        Queste nuove colonne con gli argomenti sono indicative ma non del tutto precise, se doveste trovare 
                        errori o incongruenze fatemi sapere.</p>";

                    messageBody += @"
                        <p>Per domande, chiarimenti e suggerimenti resto a disposizione.</p>
                        <p>Buona giornata e buon lavoro.</p>
                        <p>Giacomo Pavone</p> ";

                    var mail = new MailMessage
                    {
                        From = new MailAddress(senderMail),
                        Subject = $"Estrazione tickets con esiti e blocchi {DateTime.Now:dd/MM}",
                        Body = messageBody,
                        IsBodyHtml = true
                    };
                    foreach (var to in toEmails) mail.To.Add(to);
                    foreach (var cc in ccEmails) mail.CC.Add(cc);
                    mail.Attachments.Add(new Attachment(excelFile));

                    Logger.Log(99, "Invio mail", LogLevel.INFO);
                    try { smtpClient.Send(mail); }
                    catch (Exception ex) { Logger.Log(0, $"Errore invio mail: {ex.Message}", LogLevel.ERROR); throw; }
                    finally { mail.Dispose(); }
                }
            }
            catch(Exception ex)
            {
                Logger.LogError(null, "Errore: " + ex);
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.Log(100, "Fine Lavorazione", LogLevel.INFO);
            }
        }

        /// <summary>
        /// Sincronizza la tabella VALIDAZIONE_TICKET con i ticket presenti nel file.
        /// - Aggrega localmente per ID_TICKET (una sola riga per ticket).
        /// - Inserisce solo i ticket non presenti.
        /// - Se il ticket esiste già ed è CHIUSO nel file, aggiorna STATO = 'CHIUSO'.
        /// </summary>
        private static void SyncValidazioneTicket(DataTable tickets, SqlConnection cnExisting)
        {
            if (tickets == null || tickets.Rows.Count == 0) return;

            // 1) Aggregazione locale per ID_TICKET (una sola riga per ticket)
            var map = new Dictionary<string, TicketAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in tickets.Rows)
            {
                string idTicket = SafeStr(r, "ID_TICKET");
                if (string.IsNullOrWhiteSpace(idTicket))
                    continue;

                string codStud = SafeStr(r, "CODSTUD");      // nel file
                string codFisc = SafeStr(r, "CODFISC");      // nel file
                string stato = SafeStr(r, "STATO");
                string msg = SafeStr(r, "PRIMO_MSG_STUDENTE");
                string dataStr = SafeStr(r, "DATA_CREAZIONE");

                bool isChiuso = string.Equals(stato?.Trim(), "CHIUSO", StringComparison.OrdinalIgnoreCase);

                if (!map.TryGetValue(idTicket, out var agg))
                {
                    agg = new TicketAgg
                    {
                        IdTicket = idTicket.Trim(),
                        CodStud = codStud?.Trim() ?? "",
                        CodFisc = codFisc?.Trim() ?? "",
                        Stato = stato?.Trim() ?? "",
                        PrimoMsgStudente = msg ?? ""
                    };

                    if (DateTime.TryParse(dataStr, out var dt))
                        agg.DataCreazione = dt;

                    map[idTicket] = agg;
                }
                else
                {
                    if (!string.Equals(agg.Stato, "CHIUSO", StringComparison.OrdinalIgnoreCase) && isChiuso)
                        agg.Stato = "CHIUSO";

                    if (!agg.DataCreazione.HasValue && DateTime.TryParse(dataStr, out var dt2))
                        agg.DataCreazione = dt2;

                    if (string.IsNullOrEmpty(agg.PrimoMsgStudente) && !string.IsNullOrEmpty(msg))
                        agg.PrimoMsgStudente = msg;
                }
            }

            if (map.Count == 0) return;

            // 2) DataTable locale per bulk
            var dtLocal = new DataTable();
            dtLocal.Columns.Add("ID_TICKET", typeof(string));
            dtLocal.Columns.Add("CODICE_STUDENTE", typeof(string));
            dtLocal.Columns.Add("COD_FISCALE", typeof(string));
            dtLocal.Columns.Add("STATO", typeof(string));
            dtLocal.Columns.Add("DATA_CREAZIONE", typeof(DateTime));
            dtLocal.Columns.Add("PRIMO_MSG_STUDENTE", typeof(string));

            foreach (var kvp in map)
            {
                var agg = kvp.Value;
                var row = dtLocal.NewRow();
                row["ID_TICKET"] = agg.IdTicket;
                row["CODICE_STUDENTE"] = string.IsNullOrWhiteSpace(agg.CodStud) ? (object)DBNull.Value : agg.CodStud;
                row["COD_FISCALE"] = string.IsNullOrWhiteSpace(agg.CodFisc) ? (object)DBNull.Value : agg.CodFisc;
                row["STATO"] = string.IsNullOrWhiteSpace(agg.Stato) ? (object)DBNull.Value : agg.Stato.ToUpperInvariant();
                row["PRIMO_MSG_STUDENTE"] = string.IsNullOrEmpty(agg.PrimoMsgStudente) ? (object)DBNull.Value : agg.PrimoMsgStudente;
                if (agg.DataCreazione.HasValue)
                    row["DATA_CREAZIONE"] = agg.DataCreazione.Value;
                else
                    row["DATA_CREAZIONE"] = DBNull.Value;

                dtLocal.Rows.Add(row);
            }

            if (dtLocal.Rows.Count == 0) return;

            bool reopen = cnExisting.State != ConnectionState.Open;
            if (reopen) cnExisting.Open();

            using var tx = cnExisting.BeginTransaction();
            try
            {
                const string createTempSql = @"
IF OBJECT_ID('tempdb..#ValidazioneTicketTmp') IS NOT NULL
    DROP TABLE #ValidazioneTicketTmp;

CREATE TABLE #ValidazioneTicketTmp (
    ID_TICKET            VARCHAR(50) NOT NULL,
    CODICE_STUDENTE      VARCHAR(50) NULL,
    COD_FISCALE          VARCHAR(16) NULL,
    STATO                VARCHAR(50) NULL,
    DATA_CREAZIONE       DATETIME NULL,
    PRIMO_MSG_STUDENTE   NVARCHAR(MAX) NULL
);";

                using (var cmdCreate = new SqlCommand(createTempSql, cnExisting, tx))
                {
                    cmdCreate.ExecuteNonQuery();
                }

                using (var bulk = new SqlBulkCopy(cnExisting, SqlBulkCopyOptions.Default, tx))
                {
                    bulk.DestinationTableName = "#ValidazioneTicketTmp";
                    bulk.BulkCopyTimeout = 0;

                    bulk.ColumnMappings.Add("ID_TICKET", "ID_TICKET");
                    bulk.ColumnMappings.Add("CODICE_STUDENTE", "CODICE_STUDENTE");
                    bulk.ColumnMappings.Add("COD_FISCALE", "COD_FISCALE");
                    bulk.ColumnMappings.Add("STATO", "STATO");
                    bulk.ColumnMappings.Add("DATA_CREAZIONE", "DATA_CREAZIONE");
                    bulk.ColumnMappings.Add("PRIMO_MSG_STUDENTE", "PRIMO_MSG_STUDENTE");

                    bulk.WriteToServer(dtLocal);
                }

                const string insertSql = @"
INSERT INTO VALIDAZIONE_TICKET (ID_TICKET, CODICE_STUDENTE, COD_FISCALE, STATO, DATA_CREAZIONE, PRIMO_MSG_STUDENTE)
SELECT t.ID_TICKET, t.CODICE_STUDENTE, t.COD_FISCALE, t.STATO, t.DATA_CREAZIONE, t.PRIMO_MSG_STUDENTE
FROM #ValidazioneTicketTmp t
LEFT JOIN VALIDAZIONE_TICKET v WITH (UPDLOCK, HOLDLOCK)
    ON v.ID_TICKET = t.ID_TICKET
WHERE v.ID_TICKET IS NULL;";

                using (var cmdInsert = new SqlCommand(insertSql, cnExisting, tx))
                {
                    cmdInsert.ExecuteNonQuery();
                }

                const string updateSql = @"
UPDATE v
SET v.STATO = 'CHIUSO'
FROM VALIDAZIONE_TICKET v
JOIN #ValidazioneTicketTmp t
  ON v.ID_TICKET = t.ID_TICKET
WHERE UPPER(ISNULL(t.STATO,'')) = 'CHIUSO'
  AND UPPER(ISNULL(v.STATO,'')) <> 'CHIUSO';";

                using (var cmdUpdate = new SqlCommand(updateSql, cnExisting, tx))
                {
                    cmdUpdate.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                Logger.LogError(null, "Errore SyncValidazioneTicket (batch): " + ex);
                throw;
            }
        }

        /// <summary>
        /// Aggiorna VALIDAZIONE_TICKET con TOPIC_PRIMARIO e TOPIC_SECONDARIO
        /// calcolati dal KeywordEngine, solo se in tabella sono ancora vuoti.
        /// </summary>
        private static void SyncTopicsToValidazioneTicket(DataTable tickets, SqlConnection cnExisting)
        {
            if (tickets == null || tickets.Rows.Count == 0) return;

            var map = new Dictionary<string, (string TopicPrim, string TopicSec)>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in tickets.Rows)
            {
                string idTicket = SafeStr(r, "ID_TICKET");
                if (string.IsNullOrWhiteSpace(idTicket))
                    continue;

                string topicPrim = SafeStr(r, "ARGOMENTO_PRIMARIO");
                string topicSec = SafeStr(r, "ARGOMENTO_SECONDARIO");

                if (!map.TryGetValue(idTicket, out var cur))
                {
                    map[idTicket] = (topicPrim, topicSec);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(cur.TopicPrim) && !string.IsNullOrWhiteSpace(topicPrim))
                        cur.TopicPrim = topicPrim;
                    if (string.IsNullOrWhiteSpace(cur.TopicSec) && !string.IsNullOrWhiteSpace(topicSec))
                        cur.TopicSec = topicSec;

                    map[idTicket] = cur;
                }
            }

            var dtLocal = new DataTable();
            dtLocal.Columns.Add("ID_TICKET", typeof(string));
            dtLocal.Columns.Add("TOPIC_PRIMARIO", typeof(string));
            dtLocal.Columns.Add("TOPIC_SECONDARIO", typeof(string));

            foreach (var kvp in map)
            {
                var id = kvp.Key;
                var (tp, ts) = kvp.Value;

                if (string.IsNullOrWhiteSpace(tp) && string.IsNullOrWhiteSpace(ts))
                    continue;

                var row = dtLocal.NewRow();
                row["ID_TICKET"] = id;
                row["TOPIC_PRIMARIO"] = string.IsNullOrWhiteSpace(tp) ? (object)DBNull.Value : tp;
                row["TOPIC_SECONDARIO"] = string.IsNullOrWhiteSpace(ts) ? (object)DBNull.Value : ts;
                dtLocal.Rows.Add(row);
            }

            if (dtLocal.Rows.Count == 0) return;

            bool reopen = cnExisting.State != ConnectionState.Open;
            if (reopen) cnExisting.Open();

            using var tx = cnExisting.BeginTransaction();
            try
            {
                const string createTempSql = @"
IF OBJECT_ID('tempdb..#ValidazioneTopicTmp') IS NOT NULL
    DROP TABLE #ValidazioneTopicTmp;

CREATE TABLE #ValidazioneTopicTmp (
    ID_TICKET        VARCHAR(50) NOT NULL,
    TOPIC_PRIMARIO   NVARCHAR(MAX) NULL,
    TOPIC_SECONDARIO NVARCHAR(MAX) NULL
);";

                using (var cmdCreate = new SqlCommand(createTempSql, cnExisting, tx))
                {
                    cmdCreate.ExecuteNonQuery();
                }

                using (var bulk = new SqlBulkCopy(cnExisting, SqlBulkCopyOptions.Default, tx))
                {
                    bulk.DestinationTableName = "#ValidazioneTopicTmp";
                    bulk.BulkCopyTimeout = 0;

                    bulk.ColumnMappings.Add("ID_TICKET", "ID_TICKET");
                    bulk.ColumnMappings.Add("TOPIC_PRIMARIO", "TOPIC_PRIMARIO");
                    bulk.ColumnMappings.Add("TOPIC_SECONDARIO", "TOPIC_SECONDARIO");

                    bulk.WriteToServer(dtLocal);
                }

                const string updateSql = @"
UPDATE v
SET 
    v.TOPIC_PRIMARIO = CASE 
                           WHEN ISNULL(v.TOPIC_PRIMARIO,'') = '' AND ISNULL(t.TOPIC_PRIMARIO,'') <> '' 
                                THEN t.TOPIC_PRIMARIO 
                           ELSE v.TOPIC_PRIMARIO 
                       END,
    v.TOPIC_SECONDARIO = CASE 
                             WHEN ISNULL(v.TOPIC_SECONDARIO,'') = '' AND ISNULL(t.TOPIC_SECONDARIO,'') <> '' 
                                  THEN t.TOPIC_SECONDARIO 
                             ELSE v.TOPIC_SECONDARIO 
                         END
FROM VALIDAZIONE_TICKET v
JOIN #ValidazioneTopicTmp t
  ON v.ID_TICKET = t.ID_TICKET
WHERE (ISNULL(v.TOPIC_PRIMARIO,'') = '' AND ISNULL(t.TOPIC_PRIMARIO,'') <> '')
   OR (ISNULL(v.TOPIC_SECONDARIO,'') = '' AND ISNULL(t.TOPIC_SECONDARIO,'') <> '');";

                using (var cmdUpdate = new SqlCommand(updateSql, cnExisting, tx))
                {
                    cmdUpdate.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch { }
                Logger.LogError(null, "Errore SyncTopicsToValidazioneTicket (batch): " + ex);
                throw;
            }
        }

        // ===== Helpers: columns =====
        private static void EnsureTicketColumns(DataTable t, IEnumerable<int> years)
        {
            foreach (var y in years)
            {
                AddStringCol(t, $"DOMANDA_{y}");
                AddStringCol(t, $"BLOCCHI_{y}");
                AddStringCol(t, $"ESITO_BS_{y}");
                AddStringCol(t, $"ESITO_PA_{y}");
                AddStringCol(t, $"SEDE_DESCR_{y}");
                AddStringCol(t, $"HA_PRIMA_RATA_{y}");
                AddStringCol(t, $"HA_SALDO_{y}");
                AddStringCol(t, $"HA_RIMBORSO_{y}");
            }
        }


        private static void EnsureTopicColumns(DataTable t)
        {
            AddStringCol(t, "ARGOMENTO_PRIMARIO");
            AddStringCol(t, "ARGOMENTO_SECONDARIO");
            AddStringCol(t, "RIFERITO_A_BLOCCHI");
            AddStringCol(t, "PS_DOCUMENTI_LAVORATI");
        }

        private static void EnsureDomicilioColumns(DataTable t)
        {
            AddStringCol(t, "ISTANZA_DOMICILIO_APERTA");
            AddStringCol(t, "ISTANZA_DOMICILIO_LAVORATA");
        }

        private static void AddStringCol(DataTable t, string name)
        {
            if (!t.Columns.Contains(name))
                t.Columns.Add(name, typeof(string));
        }

        private static string SafeStr(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col)) return "";
            var v = r[col];
            return v == null || v == DBNull.Value ? "" : v.ToString() ?? "";
        }

        private static bool EqualsCI(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        // ===== DB: TVP + fallback =====
        private static DataTable ToTvp(IEnumerable<string> cfs)
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale", typeof(string));
            foreach (var cf in cfs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var v = (cf ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(v)) dt.Rows.Add(v);
            }
            return dt;
        }

        private static Dictionary<string, HashSet<int>> GetAnnoPresenceByCF_Smart(
            List<string> cfList, IEnumerable<int> years, SqlConnection cn)
        {
            try { return GetAnnoPresenceByCF_Tvp(cfList, years, cn); }
            catch (Exception ex)
            {
                Logger.Log(10, "TVP Presence non disponibile. Fallback batched. " + ex.Message, LogLevel.WARN);
                return GetAnnoPresenceByCF_Batched(cfList, years, cn);
            }
        }

        private static Dictionary<string, Dictionary<int, YearInfo>> GetYearInfoByCF_Smart(
            List<string> cfList, IEnumerable<int> years, SqlConnection cn)
        {
            try { return GetYearInfoByCF_Tvp(cfList, years, cn); }
            catch (Exception ex)
            {
                Logger.Log(11, "TVP YearInfo non disponibile. Fallback batched. " + ex.Message, LogLevel.WARN);
                return GetYearInfoByCF_Batched(cfList, years, cn);
            }
        }
        private static Dictionary<string, bool> GetPsAllDocsWorkedByCF(
    List<string> cfList,
    SqlConnection cnExisting)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (cfList == null || cfList.Count == 0) return result;

            const int batchSize = 900;
            for (int i = 0; i < cfList.Count; i += batchSize)
            {
                var batch = cfList.Skip(i).Take(batchSize).ToList();
                if (batch.Count == 0) continue;

                bool reopen = cnExisting.State != ConnectionState.Open;
                if (reopen) cnExisting.Open();

                string inList = string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"));

                string sql = $@"
WITH UltimoDocumentoPS AS (
    SELECT
        a.cod_fiscale,
        s.Tipo_Documento,
        s.id_allegato,
        ROW_NUMBER() OVER (
            PARTITION BY a.cod_fiscale, s.Tipo_Documento
            ORDER BY s.data_validita DESC, s.Id_Specifiche_permesso_soggiorno DESC
        ) AS rn
    FROM Specifiche_permesso_soggiorno s WITH (NOLOCK)
    INNER JOIN allegati a WITH (NOLOCK)
        ON a.id_allegato = s.id_allegato
    WHERE s.Tipo_Documento IN ('01','02','03')
	and (s.Anno_accademico is null or s.Anno_accademico >= '20222023')
      AND a.cod_fiscale IN ({inList})
),
UltimoStatusPS AS (
    SELECT
        u.cod_fiscale,
        u.Tipo_Documento,
        sa.cod_status
    FROM UltimoDocumentoPS u
    OUTER APPLY (
        SELECT TOP (1) sa.cod_status
        FROM STATUS_ALLEGATI sa WITH (NOLOCK)
        WHERE sa.id_allegato = u.id_allegato
        ORDER BY sa.data_validita DESC
    ) sa
    WHERE u.rn = 1
),
Agg AS (
    SELECT
        cod_fiscale,
        COUNT(DISTINCT CASE WHEN Tipo_Documento IN ('01','02','03') THEN Tipo_Documento END) AS NumTipiPresenti,
        SUM(CASE WHEN Tipo_Documento IN ('01','02','03') AND cod_status = '05' THEN 1 ELSE 0 END) AS NumTipiStatus05
    FROM UltimoStatusPS
    GROUP BY cod_fiscale
)
SELECT
    cod_fiscale,
    CASE WHEN NumTipiPresenti = NumTipiStatus05 THEN 1 ELSE 0 END AS AllPsDocsWorked
FROM Agg;
";

                using (var cmd = new SqlCommand(sql, cnExisting))
                {
                    for (int p = 0; p < batch.Count; p++)
                        cmd.Parameters.AddWithValue($"@cf{p}", batch[p]);

                    using var rd = cmd.ExecuteReader();
                    while (rd.Read())
                    {
                        string cf = rd.IsDBNull(0) ? "" : rd.GetString(0);
                        if (string.IsNullOrWhiteSpace(cf)) continue;

                        bool allWorked = !rd.IsDBNull(1) && rd.GetInt32(1) == 1;
                        result[cf] = allWorked;
                    }
                }

                if (reopen) cnExisting.Close();
            }

            return result;
        }

        private static Dictionary<string, (bool HasOpen, bool HasWorked)> GetDomicilioInfoByCF(
    List<string> cfList,
    IEnumerable<int> years,
    SqlConnection cnExisting)
        {
            var result = new Dictionary<string, (bool HasOpen, bool HasWorked)>(StringComparer.OrdinalIgnoreCase);
            var yearList = years.ToList();

            if (cfList.Count == 0 || yearList.Count == 0)
                return result;

            bool reopen = cnExisting.State != ConnectionState.Open;
            if (reopen)
                cnExisting.Open();

            var tvp = ToTvp(cfList);

            string sql = $@"
SELECT 
    idg.Cod_fiscale,
    MAX(CASE 
            WHEN idg.cod_tipo_istanza = '01'
             AND idg.Esito_istanza IS NULL
             AND iis.data_fine_validita IS NULL
         THEN 1 ELSE 0 
        END) AS HasOpen,
    MAX(CASE 
            WHEN idg.cod_tipo_istanza = '01'
             AND idg.Esito_istanza in ('1', '2')
         THEN 1 ELSE 0 
        END) AS HasWorked
FROM Istanza_dati_generali AS idg WITH (NOLOCK)
INNER JOIN Istanza_status AS iis WITH (NOLOCK)
        ON idg.Num_istanza = iis.Num_istanza
JOIN @Cf TVP
        ON TVP.CodFiscale = idg.Cod_fiscale
WHERE CAST(idg.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
GROUP BY idg.Cod_fiscale;";

            using (var cmd = new SqlCommand(sql, cnExisting))
            {
                var p = cmd.Parameters.AddWithValue("@Cf", tvp);
                p.SqlDbType = SqlDbType.Structured;
                p.TypeName = "dbo.CfList";

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string cf = rd.GetString(0);
                    int hasOpen = rd.IsDBNull(1) ? 0 : rd.GetInt32(1);
                    int hasWorked = rd.IsDBNull(2) ? 0 : rd.GetInt32(2);

                    result[cf] = (hasOpen == 1, hasWorked == 1);
                }
            }

            if (reopen)
                cnExisting.Close();

            return result;
        }


        private static Dictionary<string, HashSet<int>> GetAnnoPresenceByCF_Tvp(
            List<string> cfList, IEnumerable<int> years, SqlConnection cnExisting)
        {
            var result = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var yearList = years.ToList();
            if (cfList.Count == 0 || yearList.Count == 0) return result;

            bool reopen = cnExisting.State != ConnectionState.Open;
            if (reopen) cnExisting.Open();

            string sql = $@"
SELECT d.Cod_fiscale, CAST(d.Anno_accademico AS INT) AS Anno_accademico
FROM Domanda d WITH (NOLOCK)
JOIN @Cf TVP ON TVP.CodFiscale = d.Cod_fiscale
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)});
";
            using var cmd = new SqlCommand(sql, cnExisting);
            var tvp = ToTvp(cfList);
            var p = cmd.Parameters.AddWithValue("@Cf", tvp);
            p.SqlDbType = SqlDbType.Structured;
            p.TypeName = "dbo.CfList";

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var cf = rd.GetString(0);
                int anno = Convert.ToInt32(rd.GetValue(1));
                if (!result.TryGetValue(cf, out var set)) { set = new HashSet<int>(); result[cf] = set; }
                set.Add(anno);
            }

            if (reopen) cnExisting.Close();
            return result;
        }

        private static Dictionary<string, Dictionary<int, YearInfo>> GetYearInfoByCF_Tvp(
            List<string> cfList, IEnumerable<int> years, SqlConnection cnExisting)
        {
            var result = new Dictionary<string, Dictionary<int, YearInfo>>(StringComparer.OrdinalIgnoreCase);
            var yearList = years.ToList();
            if (cfList.Count == 0 || yearList.Count == 0) return result;

            bool reopen = cnExisting.State != ConnectionState.Open;
            if (reopen) cnExisting.Open();
            var tvp = ToTvp(cfList);

            // Blocchi
            string sqlBlocchi = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,
       d.Num_domanda,
       dbo.SlashDescrBlocchi(d.Num_domanda, d.Anno_accademico, '') AS Blocchi
FROM Domanda d WITH (NOLOCK)
JOIN @Cf TVP ON TVP.CodFiscale = d.Cod_fiscale
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)});
";
            using (var cmdB = new SqlCommand(sqlBlocchi, cnExisting))
            {
                var pB = cmdB.Parameters.AddWithValue("@Cf", tvp);
                pB.SqlDbType = SqlDbType.Structured; pB.TypeName = "dbo.CfList";
                using var rdB = cmdB.ExecuteReader();
                while (rdB.Read())
                {
                    string cf = rdB.GetString(0);
                    int anno = Convert.ToInt32(rdB.GetValue(1));
                    string blocchi = rdB.IsDBNull(3) ? "" : (rdB.GetString(3) ?? "");
                    var info = GetOrCreate(result, cf, anno);
                    if (!string.IsNullOrWhiteSpace(blocchi))
                        foreach (var part in blocchi.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var token = part.Trim();
                            if (!string.IsNullOrEmpty(token)) info.BlocchiParts.Add(token);
                        }
                }
            }

            // Esiti BS/PA
            string sqlEsiti = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,
       ec.Cod_beneficio,
       ec.Cod_tipo_esito
FROM vEsiti_concorsi ec WITH (NOLOCK)
JOIN Domanda d WITH (NOLOCK)
  ON d.Num_domanda = ec.Num_domanda
 AND CAST(d.Anno_accademico AS INT) = CAST(ec.Anno_accademico AS INT)
JOIN @Cf TVP ON TVP.CodFiscale = d.Cod_fiscale
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND ec.Cod_beneficio IN ('BS','PA');
";
            using (var cmdE = new SqlCommand(sqlEsiti, cnExisting))
            {
                var pE = cmdE.Parameters.AddWithValue("@Cf", tvp);
                pE.SqlDbType = SqlDbType.Structured; pE.TypeName = "dbo.CfList";
                using var rdE = cmdE.ExecuteReader();
                while (rdE.Read())
                {
                    string cf = rdE.GetString(0);
                    int anno = Convert.ToInt32(rdE.GetValue(1));
                    string codBeneficio = rdE.IsDBNull(2) ? "" : (rdE.GetString(2) ?? "");
                    string codTipoEsito = rdE.IsDBNull(3) ? "" : (rdE.GetString(3) ?? "");
                    string label = codTipoEsito switch
                    {
                        "0" => "Escluso",
                        "1" => "Idoneo",
                        "2" => "Vincitore",
                        _ => "Non richiesto"
                    };
                    var info = GetOrCreate(result, cf, anno);
                    if (string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase)) info.EsitiBS.Add(label);
                    else if (string.Equals(codBeneficio, "PA", StringComparison.OrdinalIgnoreCase)) info.EsitiPA.Add(label);
                }
            }

            // Iscrizioni + Sede
            string sqlIscrSede = $@"
SELECT vi.Cod_fiscale,
       CAST(vi.Anno_accademico AS INT) AS Anno,
       vi.Cod_sede_studi,
       ss.Descrizione
FROM vIscrizioni AS vi WITH (NOLOCK)
INNER JOIN Sede_studi AS ss WITH (NOLOCK)
        ON vi.Cod_sede_studi = ss.Cod_sede_studi
JOIN @Cf TVP ON TVP.CodFiscale = vi.Cod_fiscale
WHERE CAST(vi.Anno_accademico AS INT) IN ({string.Join(",", yearList)});
";
            using (var cmdIS = new SqlCommand(sqlIscrSede, cnExisting))
            {
                var pIS = cmdIS.Parameters.AddWithValue("@Cf", tvp);
                pIS.SqlDbType = SqlDbType.Structured; pIS.TypeName = "dbo.CfList";
                using var rdIS = cmdIS.ExecuteReader();
                while (rdIS.Read())
                {
                    string cf = rdIS.GetString(0);
                    int anno = Convert.ToInt32(rdIS.GetValue(1));
                    string codSede = rdIS.IsDBNull(2) ? "" : (rdIS.GetString(2) ?? "");
                    string descr = rdIS.IsDBNull(3) ? "" : (rdIS.GetString(3) ?? "");
                    var info = GetOrCreate(result, cf, anno);
                    if (!string.IsNullOrWhiteSpace(descr)) info.SediDescrizioni.Add(descr.Trim());
                }
            }


            // dopo Iscrizioni+Sede, stesso using della connessione aperta
            string sqlPay = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,

       MAX(CASE WHEN p.Cod_tipo_pagam='BSP0' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSP0_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSP1' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSP1_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSP2' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSP2_OK,

       MAX(CASE WHEN p.Cod_tipo_pagam='BSS0' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS0_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSS1' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS1_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSS2' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS2_OK,

       MAX(CASE WHEN p.Cod_tipo_pagam='BST0' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST0_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BST1' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST1_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BST2' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST2_OK
FROM Domanda d WITH (NOLOCK)
LEFT JOIN Pagamenti p WITH (NOLOCK)
  ON p.Num_domanda = d.Num_domanda
 AND CAST(p.Anno_accademico AS INT) = CAST(d.Anno_accademico AS INT)
JOIN @Cf TVP ON TVP.CodFiscale = d.Cod_fiscale
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
GROUP BY d.Cod_fiscale, CAST(d.Anno_accademico AS INT);";

            using (var cmdP = new SqlCommand(sqlPay, cnExisting))
            {
                var pTv = cmdP.Parameters.AddWithValue("@Cf", tvp);
                pTv.SqlDbType = SqlDbType.Structured; pTv.TypeName = "dbo.CfList";
                using var rdP = cmdP.ExecuteReader();
                while (rdP.Read())
                {
                    string cf = rdP.GetString(0);
                    int anno = Convert.ToInt32(rdP.GetValue(1));

                    int bsp0 = rdP.IsDBNull(2) ? 0 : rdP.GetInt32(2);
                    int bsp1 = rdP.IsDBNull(3) ? 0 : rdP.GetInt32(3);
                    int bsp2 = rdP.IsDBNull(4) ? 0 : rdP.GetInt32(4);

                    int bss0 = rdP.IsDBNull(5) ? 0 : rdP.GetInt32(5);
                    int bss1 = rdP.IsDBNull(6) ? 0 : rdP.GetInt32(6);
                    int bss2 = rdP.IsDBNull(7) ? 0 : rdP.GetInt32(7);

                    int bst0 = rdP.IsDBNull(8) ? 0 : rdP.GetInt32(8);
                    int bst1 = rdP.IsDBNull(9) ? 0 : rdP.GetInt32(9);
                    int bst2 = rdP.IsDBNull(10) ? 0 : rdP.GetInt32(10);

                    bool hasPrimaRata = (bsp0 == 1) || (bsp0 == 0 && (bsp1 == 1 || (bsp1 == 0 && bsp2 == 1)));
                    bool hasSaldo = (bss0 == 1) || (bss0 == 0 && (bss1 == 1 || (bss1 == 0 && bss2 == 1)));
                    bool hasRimborso = (bst0 == 1) || (bst0 == 0 && (bst1 == 1 || (bst1 == 0 && bst2 == 1)));

                    var info = GetOrCreate(result, cf, anno);
                    info.HasPrimaRata = info.HasPrimaRata || hasPrimaRata; // <-- NEW
                    info.HasSaldo = info.HasSaldo || hasSaldo;
                    info.HasRimborso = info.HasRimborso || hasRimborso;
                }
            }


            if (reopen) cnExisting.Close();
            return result;

            static YearInfo GetOrCreate(Dictionary<string, Dictionary<int, YearInfo>> map, string cf, int anno)
            {
                if (!map.TryGetValue(cf, out var perYear)) { perYear = new Dictionary<int, YearInfo>(); map[cf] = perYear; }
                if (!perYear.TryGetValue(anno, out var info)) { info = new YearInfo(); perYear[anno] = info; }
                return info;
            }
        }

        private static Dictionary<string, HashSet<int>> GetAnnoPresenceByCF_Batched(
            List<string> cfList, IEnumerable<int> years, SqlConnection cnExisting)
        {
            var result = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var yearList = years.ToList();
            if (cfList.Count == 0 || yearList.Count == 0) return result;

            const int batchSize = 900;
            for (int i = 0; i < cfList.Count; i += batchSize)
            {
                var batch = cfList.Skip(i).Take(batchSize).ToList();
                bool reopen = cnExisting.State != ConnectionState.Open;
                if (reopen) cnExisting.Open();

                string sql = $@"
SELECT Cod_fiscale, CAST(Anno_accademico AS INT) AS Anno_accademico
FROM Domanda WITH (NOLOCK)
WHERE CAST(Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND Cod_fiscale IN ({string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"))});
";
                using var cmd = new SqlCommand(sql, cnExisting);
                for (int p = 0; p < batch.Count; p++) cmd.Parameters.AddWithValue($"@cf{p}", batch[p]);

                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    string cf = rd.GetString(0);
                    int anno = Convert.ToInt32(rd.GetValue(1));
                    if (!result.TryGetValue(cf, out var set)) { set = new HashSet<int>(); result[cf] = set; }
                    set.Add(anno);
                }

                if (reopen) cnExisting.Close();
            }
            return result;
        }

        private static Dictionary<string, Dictionary<int, YearInfo>> GetYearInfoByCF_Batched(
            List<string> cfList, IEnumerable<int> years, SqlConnection cnExisting)
        {
            var result = new Dictionary<string, Dictionary<int, YearInfo>>(StringComparer.OrdinalIgnoreCase);
            var yearList = years.ToList();
            if (cfList.Count == 0 || yearList.Count == 0) return result;

            const int batchSize = 900;
            for (int i = 0; i < cfList.Count; i += batchSize)
            {
                var batch = cfList.Skip(i).Take(batchSize).ToList();

                bool reopen = cnExisting.State != ConnectionState.Open;
                if (reopen) cnExisting.Open();

                // Blocchi
                string sqlBlocchi = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,
       d.Num_domanda,
       dbo.SlashDescrBlocchi(d.Num_domanda, d.Anno_accademico, '') AS Blocchi
FROM Domanda d WITH (NOLOCK)
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND d.Cod_fiscale IN ({string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"))});
";
                using (var cmdB = new SqlCommand(sqlBlocchi, cnExisting))
                {
                    for (int p = 0; p < batch.Count; p++) cmdB.Parameters.AddWithValue($"@cf{p}", batch[p]);
                    using var rdB = cmdB.ExecuteReader();
                    while (rdB.Read())
                    {
                        string cf = rdB.GetString(0);
                        int anno = Convert.ToInt32(rdB.GetValue(1));
                        string blocchi = rdB.IsDBNull(3) ? "" : (rdB.GetString(3) ?? "");
                        var info = GetOrCreate(result, cf, anno);
                        if (!string.IsNullOrWhiteSpace(blocchi))
                            foreach (var part in blocchi.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                var token = part.Trim();
                                if (!string.IsNullOrEmpty(token)) info.BlocchiParts.Add(token);
                            }
                    }
                }

                // Esiti
                string sqlEsiti = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,
       ec.Cod_beneficio,
       ec.Cod_tipo_esito
FROM vEsiti_concorsi ec WITH (NOLOCK)
JOIN Domanda d WITH (NOLOCK)
  ON d.Num_domanda = ec.Num_domanda
 AND CAST(d.Anno_accademico AS INT) = CAST(ec.Anno_accademico AS INT)
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND d.Cod_fiscale IN ({string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"))})
  AND ec.Cod_beneficio IN ('BS','PA');
";
                using (var cmdE = new SqlCommand(sqlEsiti, cnExisting))
                {
                    for (int p = 0; p < batch.Count; p++) cmdE.Parameters.AddWithValue($"@cf{p}", batch[p]);
                    using var rdE = cmdE.ExecuteReader();
                    while (rdE.Read())
                    {
                        string cf = rdE.GetString(0);
                        int anno = Convert.ToInt32(rdE.GetValue(1));
                        string codBeneficio = rdE.IsDBNull(2) ? "" : (rdE.GetString(2) ?? "");
                        string codTipoEsito = rdE.IsDBNull(3) ? "" : (rdE.GetString(3) ?? "");
                        string label = codTipoEsito switch
                        {
                            "0" => "Escluso",
                            "1" => "Idoneo",
                            "2" => "Vincitore",
                            _ => "Non richiesto"
                        };
                        var info = GetOrCreate(result, cf, anno);
                        if (string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase)) info.EsitiBS.Add(label);
                        else if (string.Equals(codBeneficio, "PA", StringComparison.OrdinalIgnoreCase)) info.EsitiPA.Add(label);
                    }
                }

                // Iscrizioni + Sede
                string sqlIscrSede = $@"
SELECT vi.Cod_fiscale,
       CAST(vi.Anno_accademico AS INT) AS Anno,
       vi.Cod_sede_studi,
       ss.Descrizione
FROM vIscrizioni AS vi WITH (NOLOCK)
INNER JOIN Sede_studi AS ss WITH (NOLOCK)
        ON vi.Cod_sede_studi = ss.Cod_sede_studi
WHERE CAST(vi.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND vi.Cod_fiscale IN ({string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"))});
";
                using (var cmdIS = new SqlCommand(sqlIscrSede, cnExisting))
                {
                    for (int p = 0; p < batch.Count; p++) cmdIS.Parameters.AddWithValue($"@cf{p}", batch[p]);
                    using var rdIS = cmdIS.ExecuteReader();
                    while (rdIS.Read())
                    {
                        string cf = rdIS.GetString(0);
                        int anno = Convert.ToInt32(rdIS.GetValue(1));
                        string codSede = rdIS.IsDBNull(2) ? "" : (rdIS.GetString(2) ?? "");
                        string descr = rdIS.IsDBNull(3) ? "" : (rdIS.GetString(3) ?? "");
                        var info = GetOrCreate(result, cf, anno);
                        if (!string.IsNullOrWhiteSpace(descr)) info.SediDescrizioni.Add(descr.Trim());
                    }
                }
                string sqlPay = $@"
SELECT d.Cod_fiscale,
       CAST(d.Anno_accademico AS INT) AS Anno,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSS0' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS0_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSS1' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS1_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BSS2' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BSS2_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BST0' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST0_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BST1' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST1_OK,
       MAX(CASE WHEN p.Cod_tipo_pagam='BST2' AND ISNULL(p.Ritirato_azienda,0)=0 THEN 1 ELSE 0 END) AS BST2_OK
FROM Domanda d WITH (NOLOCK)
LEFT JOIN Pagamenti p WITH (NOLOCK)
  ON p.Num_domanda = d.Num_domanda
 AND CAST(p.Anno_accademico AS INT) = CAST(d.Anno_accademico AS INT)
WHERE CAST(d.Anno_accademico AS INT) IN ({string.Join(",", yearList)})
  AND d.Cod_fiscale IN ({string.Join(",", batch.Select((cf, idx) => $"@cf{idx}"))})
GROUP BY d.Cod_fiscale, CAST(d.Anno_accademico AS INT);";

                using (var cmdP = new SqlCommand(sqlPay, cnExisting))
                {
                    for (int p = 0; p < batch.Count; p++) cmdP.Parameters.AddWithValue($"@cf{p}", batch[p]);
                    using var rdP = cmdP.ExecuteReader();
                    while (rdP.Read())
                    {
                        string cf = rdP.GetString(0);
                        int anno = Convert.ToInt32(rdP.GetValue(1));
                        int bss0 = rdP.IsDBNull(2) ? 0 : rdP.GetInt32(2);
                        int bss1 = rdP.IsDBNull(3) ? 0 : rdP.GetInt32(3);
                        int bss2 = rdP.IsDBNull(4) ? 0 : rdP.GetInt32(4);
                        int bst0 = rdP.IsDBNull(5) ? 0 : rdP.GetInt32(5);
                        int bst1 = rdP.IsDBNull(6) ? 0 : rdP.GetInt32(6);
                        int bst2 = rdP.IsDBNull(7) ? 0 : rdP.GetInt32(7);

                        bool hasSaldo = (bss0 == 1) || (bss0 == 0 && (bss1 == 1 || (bss1 == 0 && bss2 == 1)));
                        bool hasRimborso = (bst0 == 1) || (bst0 == 0 && (bst1 == 1 || (bst1 == 0 && bst2 == 1)));

                        var info = GetOrCreate(result, cf, anno);
                        info.HasSaldo = info.HasSaldo || hasSaldo;
                        info.HasRimborso = info.HasRimborso || hasRimborso;
                    }
                }

                if (reopen) cnExisting.Close();
            }
            return result;

            static YearInfo GetOrCreate(Dictionary<string, Dictionary<int, YearInfo>> map, string cf, int anno)
            {
                if (!map.TryGetValue(cf, out var perYear)) { perYear = new Dictionary<int, YearInfo>(); map[cf] = perYear; }
                if (!perYear.TryGetValue(anno, out var info)) { info = new YearInfo(); perYear[anno] = info; }
                return info;
            }
        }
    }

    class TicketAgg
    {
        public string IdTicket { get; set; } = "";
        public string CodStud { get; set; } = "";
        public string CodFisc { get; set; } = "";
        public string Stato { get; set; } = "";
        public DateTime? DataCreazione { get; set; }
        public string PrimoMsgStudente { get; set; } = "";
    }
}
