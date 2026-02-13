using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Net.Mail;

namespace ProcedureNet7
{
    internal class ProceduraEstrazioneIstanze : BaseProcedure<ArgsEstrazioneIstanze>
    {
        private string savePath = string.Empty;
        private string mailFilePath = string.Empty;
        private bool _sendMail = true;
        private string senderMail = string.Empty;
        private string senderPassword = string.Empty;

        // Destinatari per domicilio (contratto locazione)
        private readonly List<string> _domRecipients = new();

        // Destinatari per iscrizione: CodGruppo (01..05) -> email
        private readonly Dictionary<string, string> _enteRecipients =
            new(StringComparer.OrdinalIgnoreCase);

        // CC comuni
        private readonly List<string> _ccEmails = new();

        private const int MinRowsPerRecipient = 10;

        public ProceduraEstrazioneIstanze(MasterForm? masterForm, SqlConnection? connection_string)
            : base(masterForm, connection_string)
        {
        }

        public override void RunProcedure(ArgsEstrazioneIstanze args)
        {
            try
            {
                savePath = args._savePath;
                mailFilePath = args._mailFilePath;

                if (string.IsNullOrEmpty(savePath) || string.IsNullOrEmpty(mailFilePath))
                {
                    Logger.LogInfo(0, "Save path or mail file path is missing.");
                    return;
                }

                if (!_sendMail)
                {
                    Logger.LogInfo(3, "SendMail flag is false: no email will be sent.");
                    return;
                }

                if (!File.Exists(mailFilePath))
                {
                    Logger.LogInfo(4, $"Mail file path '{mailFilePath}' does not exist.");
                    return;
                }

                // Configurazione mail (DOM, ENTE, CC, ID, PW)
                ReadMailConfig(
                    mailFilePath,
                    _domRecipients,
                    _enteRecipients,
                    _ccEmails,
                    ref senderMail,
                    ref senderPassword
                );

                if (string.IsNullOrEmpty(senderMail) || string.IsNullOrEmpty(senderPassword))
                {
                    Logger.LogInfo(6, "Sender email credentials are missing.");
                    return;
                }

                bool hasDomRecipients = _domRecipients.Count > 0;
                bool hasEnteRecipients = _enteRecipients.Count > 0;

                if (!hasDomRecipients && !hasEnteRecipients)
                {
                    Logger.LogInfo(6, "No DOM or ENTE recipients configured. Nothing to do.");
                    return;
                }

                string currentDateFolder = Path.Combine(savePath, DateTime.Now.ToString("yyyyMMdd"));
                if (!Directory.Exists(currentDateFolder))
                    Directory.CreateDirectory(currentDateFolder);

                Logger.LogInfo(1, "Starting data extraction for Istanze.");

                // 1) DOMICILIO (contratto locazione - tipo 01)
                if (hasDomRecipients)
                {
                    ProcessContrattoLocazione(currentDateFolder);
                }
                else
                {
                    Logger.LogInfo(11, "No DOM recipients configured. Skipping contratti di locazione.");
                }

                // 2) ISCRIZIONE (modifica iscrizione - tipo 03)
                if (hasEnteRecipients)
                {
                    ProcessModificaIscrizione(currentDateFolder);
                }
                else
                {
                    Logger.LogInfo(40, "No ENTE recipients configured. Skipping modifiche iscrizione.");
                }

                _masterForm.inProcedure = false;
                Logger.LogInfo(100, "Processing EstrazioneIstanze completed successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(null, $"An error occurred during EstrazioneIstanze processing: {ex.Message}");
            }
        }

        // =================== PROCESSORI PER TIPO ISTANZA ===================

        private void ProcessContrattoLocazione(string currentDateFolder)
        {
            Logger.LogInfo(12, "Starting extraction for ISTANZE CONTRATTO LOCAZIONE (cod_tipo_istanza = '01').");

            var dtIstanze = ExecuteQuery(@"
SELECT idg.Num_istanza,
       idg.Cod_fiscale
FROM   Istanza_dati_generali idg
       INNER JOIN Istanza_Contratto_locazione icl ON idg.Num_istanza = icl.Num_istanza
       INNER JOIN Istanza_status           iis    ON idg.Num_istanza = iis.Num_istanza
WHERE  idg.Esito_istanza IS NULL
  AND  idg.Data_fine_validita IS NULL
  AND  iis.presa_carico = 0

ORDER BY idg.Num_istanza;
");

            int totalRows = dtIstanze.Rows.Count;
            Logger.LogInfo(13, $"[ISTANZE LOCAZIONE] Retrieved {totalRows} rows.");

            if (totalRows == 0)
            {
                Logger.LogInfo(14, "No istanze locazione to send: dataset empty.");
                return;
            }

            if (_domRecipients.Count == 0)
            {
                Logger.LogInfo(15, "No DOM recipients configured, cannot send locazione emails.");
                return;
            }

            var activeRecipients = new List<string>(_domRecipients);
            while (activeRecipients.Count > 1 &&
                   (totalRows / activeRecipients.Count) < MinRowsPerRecipient)
            {
                string removed = activeRecipients[activeRecipients.Count - 1];
                activeRecipients.RemoveAt(activeRecipients.Count - 1);
                Logger.LogInfo(
                    16,
                    $"Removing DOM recipient {removed} to ensure at least {MinRowsPerRecipient} rows per recipient. Remaining DOM recipients: {activeRecipients.Count}."
                );
            }

            Logger.LogInfo(
                17,
                $"DOM active recipients: {activeRecipients.Count}, totalRows={totalRows}, approxRowsPerRecipient={totalRows / activeRecipients.Count}."
            );

            SendEmailRoundRobin(
                activeRecipients,
                _ccEmails,
                dtIstanze,
                currentDateFolder,
                filePrefix: "istanze_locazione",
                subject: $"Estrazione istanze contratto locazione da prendere in carico - {DateTime.Now:dd/MM/yyyy}",
                htmlBody: GetMailBodyIstanzeLocazione()
            );
        }

        private void ProcessModificaIscrizione(string currentDateFolder)
        {
            Logger.LogInfo(41, "Starting extraction for ISTANZE MODIFICA ISCRIZIONE (cod_tipo_istanza = '03').");

            var dtModIscr = ExecuteQuery(@"
SELECT idg.Num_istanza,
       idg.Cod_fiscale,
       idg.Anno_accademico,
       idg.cod_tipo_istanza,
       imi.Id_modifica,
       imic.Cod_sede_studi,
       ss.Cod_ente,
       ss.Descrizione AS Descrizione_sede_studi
FROM   Istanza_dati_generali               idg
       INNER JOIN Istanza_modifica_iscrizione       imi  ON idg.Num_istanza = imi.Num_istanza
       INNER JOIN Istanza_modifica_iscrizione_corso imic ON imi.Id_modifica = imic.Id_modifica
       INNER JOIN Istanza_status                    iis  ON idg.Num_istanza = iis.Num_istanza
       LEFT  JOIN Sede_studi                        ss   ON imic.Cod_sede_studi = ss.Cod_sede_studi
WHERE  idg.Esito_istanza IS NULL
  AND  idg.Data_fine_validita IS NULL
  AND  idg.cod_tipo_istanza = '03'
  AND  iis.presa_carico = 0
ORDER BY idg.Num_istanza;
");

            int totalRows = dtModIscr.Rows.Count;
            Logger.LogInfo(42, $"[ISTANZE MODIFICA ISCRIZIONE] Retrieved {totalRows} rows.");

            if (totalRows == 0)
            {
                Logger.LogInfo(43, "No istanze modifica iscrizione to send: dataset empty.");
                return;
            }

            if (_enteRecipients.Count == 0)
            {
                Logger.LogInfo(44, "No ENTE recipients configured, cannot send iscrizione emails.");
                return;
            }

            SendEmailByEnte(
                dtModIscr,
                currentDateFolder,
                filePrefix: "istanze_iscrizione",
                subject: $"Estrazione istanze modifica iscrizione da prendere in carico - {DateTime.Now:dd/MM/yyyy}",
                htmlBody: GetMailBodyIstanzeIscrizione()
            );
        }

        // =================== HELPERS SQL / MAIL ===================

        private DataTable ExecuteQuery(string sql)
        {
            var dt = new DataTable();
            Logger.LogInfo(10, "Executing SQL query for EstrazioneIstanze.");
            using var cmd = new SqlCommand(sql, CONNECTION) { CommandTimeout = 90000000 };
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
            return dt;
        }

        private void ReadMailConfig(
            string path,
            List<string> domRecipients,
            Dictionary<string, string> enteRecipients,
            List<string> ccEmails,
            ref string id,
            ref string pw)
        {
            Logger.LogInfo(20, "Reading email configurations from the file (EstrazioneIstanze).");

            using var sr = new StreamReader(path);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                // TO#DOM#email -> destinatari istanze domicilio
                if (line.StartsWith("TO#DOM#", StringComparison.OrdinalIgnoreCase))
                {
                    string mail = line.Substring("TO#DOM#".Length).Trim();
                    if (!string.IsNullOrEmpty(mail))
                        domRecipients.Add(mail);
                }
                // ENTE#codGruppo#email -> destinatari istanze iscrizione per gruppo logico (01..05)
                else if (line.StartsWith("ENTE#", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('#');
                    if (parts.Length >= 3)
                    {
                        string codGruppo = parts[1].Trim();
                        string mail = parts[2].Trim();
                        if (!string.IsNullOrEmpty(codGruppo) && !string.IsNullOrEmpty(mail))
                            enteRecipients[codGruppo] = mail;
                    }
                }
                // CC#email -> CC comuni
                else if (line.StartsWith("CC#", StringComparison.OrdinalIgnoreCase))
                {
                    string mail = line[3..].Trim();
                    if (!string.IsNullOrEmpty(mail))
                        ccEmails.Add(mail);
                }
                // ID#... / PW#... -> credenziali mittente
                else if (line.StartsWith("ID#", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(id))
                {
                    id = line[3..].Trim();
                }
                else if (line.StartsWith("PW#", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(pw))
                {
                    pw = line[3..].Trim();
                }
            }

            Logger.LogInfo(
                21,
                $"Mail config: DOM={domRecipients.Count}, ENTE={enteRecipients.Count}, CC={ccEmails.Count}, ID={(string.IsNullOrEmpty(id) ? "none" : "present")}."
            );
        }

        private string GetMailBodyIstanzeLocazione() => @"
<p>Buongiorno,</p>
<p>Su richiesta di Rita, in allegato troverai l'estrazione aggiornata alla data odierna delle istanze di contratto di locazione che sono da lavorare.</p>
<p>Le istanze inviate potrebbero essere lavorate durante la giornata, per cui se non le trovate andate avanti, non è necessario segnalarle.</p>
<p>Grazie e buon lavoro!</p>
<p>Giacomo Pavone</p>";

        private string GetMailBodyIstanzeIscrizione() => @"
<p>Buongiorno,</p>
<p>Su richiesta di Rita, in allegato troverai l'estrazione aggiornata alla data odierna delle istanze di modifica iscrizione per la sede di competenza da distribuire agli operatori.</p>
<p>Le istanze inviate potrebbero essere lavorate durante la giornata.</p>
<p>Grazie e buon lavoro!</p>
<p>Giacomo Pavone</p>";

        // =================== INVIO MAIL: ROUND ROBIN (LOCAZIONE) ===================

        private void SendEmailRoundRobin(
            List<string> toEmails,
            List<string> ccEmails,
            DataTable allRows,
            string saveFolder,
            string filePrefix,
            string subject,
            string htmlBody)
        {
            if (toEmails == null || toEmails.Count == 0)
            {
                Logger.LogInfo(30, "SendEmailRoundRobin: no DOM recipients, abort.");
                return;
            }

            if (allRows == null || allRows.Rows.Count == 0)
            {
                Logger.LogInfo(31, "SendEmailRoundRobin: dataset empty, nothing to send.");
                return;
            }

            Logger.LogInfo(
                32,
                $"SendEmailRoundRobin: {allRows.Rows.Count} rows to be distributed to {toEmails.Count} DOM recipients."
            );

            var tablesByRecipient = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var email in toEmails)
            {
                tablesByRecipient[email] = allRows.Clone();
            }

            var recipientsArray = toEmails.ToArray();
            int recipientIndex = 0;

            for (int i = 0; i < allRows.Rows.Count; i++)
            {
                var row = allRows.Rows[i];
                string targetEmail = recipientsArray[recipientIndex];

                tablesByRecipient[targetEmail].ImportRow(row);

                recipientIndex++;
                if (recipientIndex >= recipientsArray.Length)
                    recipientIndex = 0;
            }

            foreach (var kvp in tablesByRecipient)
            {
                Logger.LogInfo(33, $"DOM recipient {kvp.Key} has {kvp.Value.Rows.Count} istanze assigned.");
            }

            foreach (var kvp in tablesByRecipient)
            {
                string toEmail = kvp.Key;
                DataTable emailTable = kvp.Value;

                if (emailTable.Rows.Count == 0)
                {
                    Logger.LogInfo(34, $"No rows assigned to {toEmail}, skipping email.");
                    continue;
                }

                string emailSafe = toEmail.Replace("@", "_at_").Replace(".", "_dot_");
                string individualSavePath = Path.Combine(saveFolder, $"{filePrefix}_{emailSafe}.xlsx");

                Logger.LogInfo(35, $"Saving data for {toEmail} to file: {individualSavePath}");
                string savedToPath = Utilities.ExportDataTableToExcel(emailTable, individualSavePath);

                Logger.LogInfo(36, $"Preparing to send email to {toEmail} with subject '{subject}'.");

                TrySendMail(toEmail, ccEmails, savedToPath, subject, htmlBody);
            }
        }

        // =================== INVIO MAIL: PER ENTE (ISCRIZIONE, GRUPPI 01..05) ===================

        private void SendEmailByEnte(
            DataTable allRows,
            string saveFolder,
            string filePrefix,
            string subject,
            string htmlBody)
        {
            if (allRows == null || allRows.Rows.Count == 0)
            {
                Logger.LogInfo(50, "SendEmailByEnte: dataset empty, nothing to send.");
                return;
            }

            if (!allRows.Columns.Contains("Cod_ente") ||
                !allRows.Columns.Contains("Cod_sede_studi"))
            {
                Logger.LogInfo(51, "SendEmailByEnte: DataTable does not contain 'Cod_ente' or 'Cod_sede_studi' column.");
                return;
            }

            // email -> DataTable (somma di tutti i gruppi che puntano alla stessa mail)
            var tablesByRecipient = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);

            // Per ogni gruppo configurato (01..05) si applica il filtro logico e si accumulano le righe sull'email associata
            foreach (var groupKvp in _enteRecipients)
            {
                string groupCode = groupKvp.Key;   // 01, 02, 03, 04, 05
                string email = groupKvp.Value;

                if (!tablesByRecipient.TryGetValue(email, out var dt))
                {
                    dt = allRows.Clone();
                    tablesByRecipient[email] = dt;
                }

                foreach (DataRow row in allRows.Rows)
                {
                    if (IsRowInGroupForEnte(row, groupCode))
                    {
                        dt.ImportRow(row);
                    }
                }
            }

            // Rimuovi destinatari senza righe
            var nonEmpty = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in tablesByRecipient)
            {
                if (kvp.Value.Rows.Count > 0)
                    nonEmpty[kvp.Key] = kvp.Value;
            }

            if (nonEmpty.Count == 0)
            {
                Logger.LogInfo(52, "SendEmailByEnte: no rows mapped to ENTE recipients (after grouping logic).");
                return;
            }

            // Regola minimo 10 righe a persona:
            // - se almeno uno ha >= MinRowsPerRecipient, invia solo a quelli sopra soglia
            // - altrimenti invia solo a quello con più righe
            var sendList = new List<KeyValuePair<string, DataTable>>(nonEmpty);
            var filtered = new List<KeyValuePair<string, DataTable>>();

            foreach (var kvp in sendList)
            {
                if (kvp.Value.Rows.Count >= MinRowsPerRecipient)
                    filtered.Add(kvp);
            }

            if (filtered.Count > 0)
            {
                Logger.LogInfo(
                    53,
                    $"SendEmailByEnte: applying MinRowsPerRecipient={MinRowsPerRecipient}, will send to {filtered.Count} ENTE recipients."
                );
                sendList = filtered;
            }
            else
            {
                KeyValuePair<string, DataTable> best = sendList[0];
                foreach (var kvp in sendList)
                {
                    if (kvp.Value.Rows.Count > best.Value.Rows.Count)
                        best = kvp;
                }

                sendList = new List<KeyValuePair<string, DataTable>> { best };

                Logger.LogInfo(
                    54,
                    $"SendEmailByEnte: no recipient reaches {MinRowsPerRecipient} rows, sending only to {best.Key} with {best.Value.Rows.Count} rows."
                );
            }

            foreach (var kvp in sendList)
            {
                string toEmail = kvp.Key;
                DataTable emailTable = kvp.Value;

                Logger.LogInfo(
                    55,
                    $"ENTE recipient {toEmail} has {emailTable.Rows.Count} istanze modifica iscrizione assigned."
                );

                if (emailTable.Rows.Count == 0)
                    continue;

                string emailSafe = toEmail.Replace("@", "_at_").Replace(".", "_dot_");
                string individualSavePath = Path.Combine(saveFolder, $"{filePrefix}_{emailSafe}.xlsx");

                Logger.LogInfo(56, $"Saving data for {toEmail} to file: {individualSavePath}");
                string savedToPath = Utilities.ExportDataTableToExcel(emailTable, individualSavePath);

                Logger.LogInfo(57, $"Preparing to send email to {toEmail} with subject '{subject}'.");

                TrySendMail(toEmail, _ccEmails, savedToPath, subject, htmlBody);
            }
        }

        // Logica di appartenenza riga → gruppo ENTE (01..05)
        private bool IsRowInGroupForEnte(DataRow row, string groupCode)
        {
            string codEnte = (Convert.ToString(row["Cod_ente"]) ?? "").Trim();
            string codSede = (Convert.ToString(row["Cod_sede_studi"]) ?? "").Trim();

            switch (groupCode)
            {
                case "01":
                    // se nella mail c'è il numero 01, invia solo i dati con cod_Sede_studi = 'B'
                    return codSede.Equals("B", StringComparison.OrdinalIgnoreCase);

                case "02":
                    // se nella mail c'è 02:
                    //  - cod_ente = '01' e cod_sede_studi <> 'B'
                    //  - oppure cod_ente in (02, 06, 08, 09, 12, 13)
                    if (codEnte == "01" && !codSede.Equals("B", StringComparison.OrdinalIgnoreCase))
                        return true;

                    return codEnte is "02" or "06" or "08" or "09" or "12" or "13";

                case "03":
                    // cod_ente in (03, 04, 07, 10, 11)
                    return codEnte is "03" or "04" or "07" or "10" or "11";

                case "05":
                    // cod_ente = '05'
                    return codEnte == "05";

                default:
                    return false;
            }
        }

        // =================== INVIO SMTP COMUNE ===================

        private void TrySendMail(string toEmail, List<string> ccEmails, string attachmentPath, string subject, string htmlBody)
        {
            try
            {
                using var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(senderMail, senderPassword),
                    EnableSsl = true,
                };

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderMail),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);
                foreach (string cc in ccEmails)
                    mailMessage.CC.Add(cc);

                mailMessage.Attachments.Add(new Attachment(attachmentPath));

                Logger.LogInfo(80, $"Sending email to {toEmail}.");
                smtpClient.Send(mailMessage);
                Logger.LogInfo(81, $"Email sent successfully to {toEmail}.");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(100, $"Failed to send email to {toEmail}. Error: {ex.Message}");
            }
        }
    }
}
