using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Windows.Forms;
using System.IO;

namespace ProcedureNet7
{
    internal class ProceduraTicket : BaseProcedure<ArgsProceduraTicket>
    {
        private bool _isFileWithMessagges = true;
        private bool _sendMail;

        private const int CurrentApplicationAcademicYear = 20262027;

        // Anni per cui il riepilogo mantiene il dettaglio storico già presente.
        private readonly List<int> _targetYears = new() { 20242025, 20252026, 20262027 };

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
                Logger.LogInfo(null, $"Avvio procedura ticket. File selezionato: {ticketFilePath}");
                Logger.LogInfo(null, $"Invio email al termine: {(_sendMail ? "SI" : "NO")}");

                Logger.Log(1, "Caricamento file", LogLevel.INFO);
                DataTable tickets = Utilities.CsvToDataTable(ticketFilePath);
                Logger.LogInfo(null, $"File ticket caricato. Righe lette: {tickets.Rows.Count}");

                Logger.LogInfo(null, "Avvio sincronizzazione iniziale della tabella VALIDAZIONE_TICKET");
                SyncValidazioneTicket(tickets, CONNECTION);
                Logger.LogInfo(null, "Sincronizzazione iniziale della tabella VALIDAZIONE_TICKET completata");

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
                Logger.LogInfo(null, $"Filtraggio ticket completato. Ticket da elaborare: {tickets.Rows.Count}");

                // drop colonne non necessarie
                string[] columnsToRemove = _isFileWithMessagges
                    ? new[] { "NREC", "PRIMO_MSG_OPERATORE", "UID", "DATA_LOG" }
                    : new[] { "NREC", "NUM_TICKET_CREATI_STUDENTE", "NUM_RICHIESTE_STUDENTE", "NUM_RISPOSTE_OPERATORE", "DATA_ULTIMO_MESSAGGIO", "AZIONE", "UID", "DATA_LOG" };

                foreach (var c in columnsToRemove)
                    if (tickets.Columns.Contains(c)) tickets.Columns.Remove(c);

                tickets.DefaultView.Sort = "CODFISC ASC";
                tickets = tickets.DefaultView.ToTable();
                Logger.LogInfo(null, $"Preparazione colonne completata. Colonne disponibili: {tickets.Columns.Count}");

                // ===== ENRICH FROM DB =====
                var cfList = tickets.AsEnumerable()
                                    .Select(r => SafeStr(r, "CODFISC"))
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToList();

                Logger.LogInfo(null, $"Codici fiscali distinti da interrogare: {cfList.Count}");

                Logger.LogInfo(null, "Estrazione operativa centralizzata per domande, pagamenti, domicilio e documenti");
                var officeRecords = TicketOfficeDataService.Load(cfList, CONNECTION);
                var officeData = TicketOfficeDataIndex.Create(officeRecords, _targetYears);
                var presence = officeData.AcademicYearsByFiscalCode;
                var allAcademicYears = officeData.AcademicYearsByFiscalCode;
                var yearInfo = officeData.YearInfoByFiscalCode;
                var psAllDocsWorked = officeData.ResidencePermitDocumentsWorkedByFiscalCode;
                var domicilioInfo = officeData.DomicileInfoByFiscalCode;
                Logger.LogInfo(
                    null,
                    $"Dati operativi indicizzati. Studenti: {officeRecords.Count}; anni storici: {allAcademicYears.Values.Sum(years => years.Count)}; anni target: {yearInfo.Values.Sum(years => years.Count)}");

                // aggiungi colonne per anni target
                EnsureTicketColumns(tickets, _targetYears);

                // keyword labels (etichette sole)
                EnsureTopicColumns(tickets);

                EnsureDomicilioColumns(tickets);

                Logger.LogInfo(null, $"Avvio arricchimento e classificazione di {tickets.Rows.Count} ticket");
                foreach (DataRow r in tickets.Rows)
                {
                    var cf = SafeStr(r, "CODFISC");
                    if (allAcademicYears.TryGetValue(cf, out var academicYears))
                    {
                        r["ANNI_ACCADEMICI_PARTECIPATI"] = string.Join(
                            " | ",
                            academicYears
                                .OrderBy(year => year)
                                .Select(FormatAcademicYear));
                    }
                    else
                    {
                        r["ANNI_ACCADEMICI_PARTECIPATI"] = "";
                    }

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

                    var ext = KeywordEngineV6.ExtractTicket(
                        SafeStr(r, "PRIMO_MSG_STUDENTE"),
                        SafeStr(r, "OGGETTO"),
                        SafeStr(r, "CATEGORIA"),
                        SafeStr(r, "SOTTOCATEGORIA"));

                    r["ARGOMENTO_PRIMARIO"] = ext.TopicPrimary ?? "";
                    r["ARGOMENTO_SECONDARIO"] = ext.TopicSecondary ?? "";
                    r["RIFERITO_A_BLOCCHI"] = ext.TopicTertiary ?? "";
                    r["PUNTEGGIO_CONFIDENZA"] = ext.ConfidenceScore;
                    r["MOTIVO_VERIFICA"] = ext.VerificationReason ?? "";
                    r["KEYWORD_ARGOMENTO_PRIMARIO"] = ext.MatchedPrimaryKeywords ?? "";
                    r["KEYWORD_ARGOMENTO_SECONDARIO"] = ext.MatchedTop1Keywords ?? "";
                    r["CLASSIFICAZIONE_DA_VERIFICARE"] = ext.IsLowConfidence ? "SI" : "";

                    // Campi predisposti per lavorazione e revisione manuale. Non viene
                    // assegnato un ufficio automaticamente: manca una mappatura approvata.
                    if (string.IsNullOrWhiteSpace(SafeStr(r, "UFFICIO_DESTINATARIO")))
                        r["UFFICIO_DESTINATARIO"] = "";
                    if (string.IsNullOrWhiteSpace(SafeStr(r, "CLASSIFICAZIONE_CORRETTA")))
                        r["CLASSIFICAZIONE_CORRETTA"] = "";
                    if (string.IsNullOrWhiteSpace(SafeStr(r, "STATO_REVISIONE")) ||
                        EqualsCI(SafeStr(r, "STATO_REVISIONE"), "NON_RICHIESTA") ||
                        EqualsCI(SafeStr(r, "STATO_REVISIONE"), "DA_REVISIONARE"))
                    {
                        r["STATO_REVISIONE"] = ext.IsLowConfidence ? "DA_REVISIONARE" : "NON_RICHIESTA";
                    }

                    // Risoluzione AA con precedenza esplicita:
                    // 1. valore compilato in AA_DICHIARATO_TICKET;
                    // 2. anno rilevato nel solo messaggio dello studente;
                    // 3. nessun anno nel ticket: la selezione del record userà l'ultima domanda.
                    // La fonte rimane interna alla selezione: non vengono create colonne di audit aggiuntive.
                    var academicYear = TicketAcademicYearResolver.Resolve(
                        SafeStr(r, "AA_DICHIARATO_TICKET"),
                        SafeStr(r, "PRIMO_MSG_STUDENTE"));

                    r["ANNO_ACCADEMICO_RICHIESTA"] = academicYear.FormattedAcademicYear;

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
                int lowConfidenceCount = tickets.AsEnumerable()
                    .Count(row => EqualsCI(SafeStr(row, "CLASSIFICAZIONE_DA_VERIFICARE"), "SI"));
                int classifiedCount = tickets.AsEnumerable()
                    .Count(row => !string.IsNullOrWhiteSpace(SafeStr(row, "ARGOMENTO_PRIMARIO")));
                Logger.LogInfo(
                    null,
                    $"Classificazione completata. Ticket classificati: {classifiedCount}; da verificare: {lowConfidenceCount}");

                Logger.LogInfo(null, "Sincronizzazione degli argomenti nella tabella VALIDAZIONE_TICKET");
                SyncTopicsToValidazioneTicket(tickets, CONNECTION);
                Logger.LogInfo(null, "Sincronizzazione degli argomenti completata");

                // export
                string directoryPath = Path.GetDirectoryName(ticketFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var sheetContext = new TicketTopicSheetContext(
                    tickets,
                    allAcademicYears,
                    officeRecords,
                    CONNECTION,
                    CurrentApplicationAcademicYear);

                ITicketTopicSheet[] topicSheets = TicketTopicModuleRegistry.CreateSheets();

                Logger.LogInfo(null, $"Avvio creazione file Excel multi-foglio nella cartella: {directoryPath}");
                string excelFile = TicketWorkbookExporter.Export(
                    tickets,
                    sheetContext,
                    topicSheets,
                    directoryPath);
                Logger.LogInfo(null, $"File Excel pronto: {excelFile}");

                if (_sendMail)
                {
                    Logger.LogInfo(null, "Avvio creazione file Excel semplificato per invio mail");
                    string mailExcelFile = TicketWorkbookExporter.ExportMailAttachment(
                        tickets,
                        directoryPath);
                    Logger.LogInfo(null, $"File Excel per mail pronto: {mailExcelFile}");

                    Logger.LogInfo(null, "Avvio preparazione dei destinatari email");
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
                    Logger.LogInfo(null, $"Destinatari email caricati. TO: {toEmails.Count}; CC: {ccEmails.Count}");

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
                        <p>vi invio l'estrazione dei ticket aperti e nuovi dal 01/01/2026 
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
                    mail.Attachments.Add(new Attachment(mailExcelFile));

                    Logger.Log(99, "Invio mail", LogLevel.INFO);
                    try
                    {
                        smtpClient.Send(mail);
                        Logger.LogInfo(null, "Email con il file ticket inviata correttamente");
                    }
                    catch (Exception ex) { Logger.Log(0, $"Errore invio mail: {ex.Message}", LogLevel.ERROR); throw; }
                    finally { mail.Dispose(); }
                }
                else
                {
                    Logger.LogInfo(null, "Invio email non richiesto: procedura completata con la sola generazione del file");
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
                Logger.LogInfo(null, "Procedura ticket terminata");
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
            if (tickets == null || tickets.Rows.Count == 0)
            {
                Logger.LogInfo(null, "SyncValidazioneTicket ignorata: nessun ticket disponibile");
                return;
            }

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

            if (map.Count == 0)
            {
                Logger.LogInfo(null, "SyncValidazioneTicket ignorata: nessun ID ticket valido");
                return;
            }
            Logger.LogInfo(null, $"SyncValidazioneTicket: ticket univoci preparati: {map.Count}");

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

            if (dtLocal.Rows.Count == 0)
            {
                Logger.LogInfo(null, "SyncValidazioneTicket ignorata: tabella temporanea vuota");
                return;
            }

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

                int insertedRows;
                using (var cmdInsert = new SqlCommand(insertSql, cnExisting, tx))
                {
                    insertedRows = cmdInsert.ExecuteNonQuery();
                }

                const string updateSql = @"
UPDATE v
SET v.STATO = 'CHIUSO'
FROM VALIDAZIONE_TICKET v
JOIN #ValidazioneTicketTmp t
  ON v.ID_TICKET = t.ID_TICKET
WHERE UPPER(ISNULL(t.STATO,'')) = 'CHIUSO'
  AND UPPER(ISNULL(v.STATO,'')) <> 'CHIUSO';";

                int closedRows;
                using (var cmdUpdate = new SqlCommand(updateSql, cnExisting, tx))
                {
                    closedRows = cmdUpdate.ExecuteNonQuery();
                }

                tx.Commit();
                Logger.LogInfo(
                    null,
                    $"SyncValidazioneTicket completata. Inseriti: {insertedRows}; aggiornati a CHIUSO: {closedRows}");
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
            if (tickets == null || tickets.Rows.Count == 0)
            {
                Logger.LogInfo(null, "SyncTopicsToValidazioneTicket ignorata: nessun ticket disponibile");
                return;
            }

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

            if (dtLocal.Rows.Count == 0)
            {
                Logger.LogInfo(null, "SyncTopicsToValidazioneTicket ignorata: nessun argomento valorizzato");
                return;
            }
            Logger.LogInfo(null, $"SyncTopicsToValidazioneTicket: argomenti da sincronizzare: {dtLocal.Rows.Count}");

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

                int updatedRows;
                using (var cmdUpdate = new SqlCommand(updateSql, cnExisting, tx))
                {
                    updatedRows = cmdUpdate.ExecuteNonQuery();
                }

                tx.Commit();
                Logger.LogInfo(
                    null,
                    $"SyncTopicsToValidazioneTicket completata. Ticket aggiornati: {updatedRows}");
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
            AddStringCol(t, "ANNI_ACCADEMICI_PARTECIPATI");

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
            AddIntCol(t, "PUNTEGGIO_CONFIDENZA");
            AddStringCol(t, "MOTIVO_VERIFICA");
            AddStringCol(t, "UFFICIO_DESTINATARIO");
            AddStringCol(t, "STATO_REVISIONE");
            AddStringCol(t, "CLASSIFICAZIONE_CORRETTA");
            AddStringCol(t, "KEYWORD_ARGOMENTO_PRIMARIO");
            AddStringCol(t, "KEYWORD_ARGOMENTO_SECONDARIO");
            AddStringCol(t, "CLASSIFICAZIONE_DA_VERIFICARE");
            AddStringCol(t, "RIFERITO_A_BLOCCHI");
            AddStringCol(t, "PS_DOCUMENTI_LAVORATI");
            // AA_DICHIARATO_TICKET è una colonna di input opzionale: non viene aggiunta
            // quando manca dal file sorgente. L'anno risolto usa la colonna consolidata già esistente.
            AddStringCol(t, "ANNO_ACCADEMICO_RICHIESTA");
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

        private static void AddIntCol(DataTable t, string name)
        {
            if (!t.Columns.Contains(name))
                t.Columns.Add(name, typeof(int));
        }

        private static void AddDoubleCol(DataTable t, string name)
        {
            if (!t.Columns.Contains(name))
                t.Columns.Add(name, typeof(double));
        }

        private static string SafeStr(DataRow r, string col)
        {
            if (!r.Table.Columns.Contains(col)) return "";
            var v = r[col];
            return v == null || v == DBNull.Value ? "" : v.ToString() ?? "";
        }

        private static bool EqualsCI(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string FormatAcademicYear(int year)
        {
            string raw = year.ToString(CultureInfo.InvariantCulture);
            return raw.Length == 8
                ? $"{raw[..4]}/{raw[4..]}"
                : raw;
        }

        // Query storiche sostituite da TicketOfficeDataService + TicketOfficeDataIndex.
        // ===== DB: TVP + fallback =====
        /*
           Le query TVP/fallback precedenti sono state rimosse dal percorso operativo.
           TicketOfficeDataService esegue ora una sola estrazione set-based per batch e
           TicketOfficeDataIndex ricava in memoria anni, presenza, blocchi, esiti, rate,
           stato documenti e istanze di domicilio.
        */

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
