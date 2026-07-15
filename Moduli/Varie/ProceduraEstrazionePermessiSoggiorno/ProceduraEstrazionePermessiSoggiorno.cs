using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;

namespace ProcedureNet7
{
    internal class ProceduraEstrazionePermessiSoggiorno : BaseProcedure<ArgsProceduraEstrazionePermessiSoggiorno>
    {
        string savePath = string.Empty;
        string mailFilePath = string.Empty;
        bool _sendMail = true;
        string senderMail = string.Empty;
        string senderPassword = string.Empty;

        public ProceduraEstrazionePermessiSoggiorno(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsProceduraEstrazionePermessiSoggiorno args)
        {
            try
            {
                savePath = args._savePath;
                mailFilePath = args._mailFilePath;

                if (string.IsNullOrEmpty(savePath) || string.IsNullOrEmpty(mailFilePath))
                {
                    Logger.LogInfo(100, "Save path or mail file path is missing.");
                    return;
                }

                Logger.LogInfo(20, "Starting data extraction for Permessi di Soggiorno.");

                // =================== Estrazioni ===================
                var dtOld = ExecuteQuery(@"
WITH base AS (
    SELECT
        vs.Cod_fiscale,
        va.cod_status,
        ROW_NUMBER() OVER (
            PARTITION BY vs.Cod_fiscale, vs.Tipo_documento, vs.Tipo_permesso
            ORDER BY vs.Id_allegato DESC
        ) AS rn
    FROM Specifiche_permesso_soggiorno AS vs
    INNER JOIN STATUS_ALLEGATI AS va
        ON vs.id_allegato = va.id_allegato
    INNER JOIN ALLEGATI AS al
        ON al.id_allegato = vs.id_allegato and al.data_fine_validita is null
    INNER JOIN Domanda AS d
        ON vs.Cod_fiscale = d.Cod_fiscale
       AND d.Anno_accademico IN (20252026, 20242025, 20232024)
       AND d.Tipo_bando like 'l%'
    INNER JOIN vEsiti_concorsi AS ve
        ON d.Num_domanda = ve.Num_domanda
       AND ve.Cod_beneficio = 'bs'
       AND ve.Cod_tipo_esito <> 0
    INNER JOIN Studente AS s
        ON d.Cod_fiscale = s.Cod_fiscale
	--where d.Num_domanda in (select num_domanda from vMotivazioni_blocco_pagamenti where anno_accademico in (20252026, 20242025) and Cod_tipologia_blocco = 'BPP')
)
SELECT DISTINCT Cod_fiscale
FROM base
WHERE rn = 1
  AND cod_status = '01'
");
                Logger.LogInfo(40, $"[OLD] Retrieved {dtOld.Rows.Count} rows for AA 20252026, 20242025, 20232024.");

                var dtNew = ExecuteQuery(@"
WITH base AS (
    SELECT
        vs.Cod_fiscale,
        va.cod_status,
        ROW_NUMBER() OVER (
            PARTITION BY vs.Cod_fiscale, vs.Tipo_documento, vs.Tipo_permesso
            ORDER BY vs.Id_allegato DESC
        ) AS rn
    FROM vSpecifiche_permesso_soggiorno AS vs
    INNER JOIN vSTATUS_ALLEGATI AS va
        ON vs.id_allegato = va.id_allegato
    INNER JOIN ALLEGATI AS al
        ON al.id_allegato = vs.id_allegato and al.data_fine_validita is null
    INNER JOIN Domanda AS d
        ON vs.Cod_fiscale = d.Cod_fiscale
       AND d.Anno_accademico IN (20262027)
       AND d.Tipo_bando like 'l%'
	   inner join vStatus_compilazione vsc on d.Anno_accademico = vsc.anno_accademico and d.Num_domanda = vsc.num_domanda and vsc.status_compilazione >= 90
    INNER JOIN Studente AS s
        ON d.Cod_fiscale = s.Cod_fiscale
	--where d.Num_domanda in (select num_domanda from vMotivazioni_blocco_pagamenti where anno_accademico in (20252026, 20242025) and Cod_tipologia_blocco = 'BPP')
)
SELECT DISTINCT Cod_fiscale
FROM base
WHERE rn = 1
  AND cod_status = '01'
");
                Logger.LogInfo(41, $"[NEW] Retrieved {dtNew.Rows.Count} rows for AA 20262027.");

                if (_sendMail)
                {
                    if (!File.Exists(mailFilePath))
                    {
                        Logger.LogInfo(100, $"Mail file path '{mailFilePath}' does not exist.");
                        return;
                    }

                    var oldToEmails = new List<string>();
                    var newToEmails = new List<string>();
                    var ccEmails = new List<string>();
                    ReadMailConfig(mailFilePath, oldToEmails, newToEmails, ccEmails, ref senderMail, ref senderPassword);

                    if (oldToEmails.Count == 0 && newToEmails.Count == 0)
                    {
                        Logger.LogInfo(100, "No recipient email addresses found for #TO#OLD# or #TO#NEW#.");
                        return;
                    }
                    if (string.IsNullOrEmpty(senderMail) || string.IsNullOrEmpty(senderPassword))
                    {
                        Logger.LogInfo(100, "Sender email credentials are missing.");
                        return;
                    }

                    // Cartella datata
                    string currentDateFolder = Path.Combine(savePath, DateTime.Now.ToString("yyyyMMdd"));
                    if (!Directory.Exists(currentDateFolder))
                        Directory.CreateDirectory(currentDateFolder);

                    // =================== Invii per anno accademico ===================
                    if (dtOld.Rows.Count > 0)
                    {
                        SendEmailWithAttachment(
                            oldToEmails, ccEmails, dtOld, currentDateFolder,
                            filePrefix: "ps_old",
                            subject: $"Estrazione studenti per PS (AA 20252026-20232024) - {DateTime.Now:dd/MM/yyyy}",
                            htmlBody: GetMailBodyOld(),
                            minRowsPerEmail: 10
                        );
                    }
                    else
                    {
                        Logger.LogInfo(60, "[OLD] Nessun record: nessun invio.");
                    }

                    if (dtNew.Rows.Count > 0)
                    {
                        SendEmailWithAttachment(
                            newToEmails, ccEmails, dtNew, currentDateFolder,
                            filePrefix: "ps_new",
                            subject: $"Estrazione studenti per PS (AA 20262027) - {DateTime.Now:dd/MM/yyyy}",
                            htmlBody: GetMailBodyNew(),
                            minRowsPerEmail: 10
                        );
                    }
                    else
                    {
                        Logger.LogInfo(61, "[NEW] Nessun record: nessun invio.");
                    }
                }

                Logger.LogInfo(100, "Processing completed successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(null, $"An error occurred during processing: {ex.Message}");
            }
        }

        // =================== Helpers ===================
        private DataTable ExecuteQuery(string sql)
        {
            var dt = new DataTable();
            Logger.LogInfo(30, "Executing SQL query.");
            using var cmd = new SqlCommand(sql, CONNECTION) { CommandTimeout = 90000000 };
            using var reader = cmd.ExecuteReader();
            dt.Load(reader);
            return dt;
        }

        private void ReadMailConfig(string path, List<string> oldToEmails, List<string> newToEmails, List<string> ccEmails, ref string id, ref string pw)
        {
            Logger.LogInfo(70, "Reading email configurations from the file.");
            using var sr = new StreamReader(path);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (TryReadMailValue(line, "#TO#OLD", out var oldTo))
                    oldToEmails.Add(oldTo);
                else if (TryReadMailValue(line, "#TO#NEW", out var newTo))
                    newToEmails.Add(newTo);
                else if (line.StartsWith("CC#"))
                    ccEmails.Add(line[3..]);
                else if (line.StartsWith("ID#") && string.IsNullOrEmpty(id))
                    id = line[3..];
                else if (line.StartsWith("PW#") && string.IsNullOrEmpty(pw))
                    pw = line[3..];
            }
        }

        private static bool TryReadMailValue(string line, string prefix, out string value)
        {
            value = string.Empty;
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var valueStart = prefix.Length;
            if (line.Length > valueStart)
            {
                var separator = line[valueStart];
                if (separator == '#')
                {
                    valueStart++;
                }
                else if (!char.IsWhiteSpace(separator))
                {
                    return false;
                }
            }

            value = line[valueStart..].Trim();
            return !string.IsNullOrEmpty(value);
        }

        private string GetMailBodyOld() => @"
<p>Buongiorno,</p>
<p>su richiesta di Rita che legge in copia,</p>
<p>in allegato troverai l'estrazione aggiornata alla data odierna relativa agli studenti stranieri degli anni accademici 20252026, 20242025 e 20232024 per cui devono essere validati i documenti di soggiorno (passaporto/richiesta o rinnovo PS/permesso di soggiorno).</p>
<p>Grazie e buon lavoro!</p>
<p>Giacomo Pavone</p>";

        private string GetMailBodyNew() => @"
<p>Buongiorno,</p>
<p>su richiesta di Rita che legge in copia,</p>
<p>in allegato troverai l'estrazione aggiornata alla data odierna relativa agli studenti stranieri dell'anno accademico 20262027 per cui devono essere validati i documenti di soggiorno (passaporto/richiesta o rinnovo PS/permesso di soggiorno).</p>
<p>Grazie e buon lavoro!</p>
<p>Giacomo Pavone</p>";

        private void SendEmailWithAttachment(
            List<string> toEmailsOriginal,
            List<string> ccEmails,
            DataTable dataTable,
            string saveFolder,
            string filePrefix,
            string subject,
            string htmlBody,
            int minRowsPerEmail = 10)
        {
            var totalRows = dataTable.Rows.Count;

            var toEmails = new List<string>(toEmailsOriginal);

            if (toEmails.Count > 0)
            {
                int targetRecipients = Math.Max(1, totalRows / minRowsPerEmail);
                if (targetRecipients == 0) targetRecipients = 1;

                if (toEmails.Count > targetRecipients)
                {
                    var rng = new Random();
                    var skipped = new List<string>();

                    while (toEmails.Count > targetRecipients)
                    {
                        int idx = rng.Next(0, toEmails.Count);
                        string removed = toEmails[idx];
                        toEmails.RemoveAt(idx);
                        skipped.Add(removed);
                    }

                    Logger.LogInfo(85, $"Ridotti i destinatari TO per garantire almeno {minRowsPerEmail} righe per email. Saltati (casuali): {string.Join(", ", skipped)}");
                }

                if (totalRows < minRowsPerEmail)
                {
                    Logger.LogInfo(86, $"Attenzione: solo {totalRows} righe totali — impossibile garantire {minRowsPerEmail} per email. Invio a un solo destinatario.");
                }
            }

            if (toEmails.Count == 0)
            {
                Logger.LogInfo(87, "Nessun destinatario dopo l'aggiustamento: invio annullato per questo dataset.");
                return;
            }

            int rowsPerEmail = totalRows / toEmails.Count;
            int remainder = totalRows % toEmails.Count;
            int startIndex = 0;

            foreach (var toEmail in toEmails)
            {
                int rowsForThisEmail = rowsPerEmail + (remainder > 0 ? 1 : 0);
                if (remainder > 0) remainder--;

                if (rowsForThisEmail <= 0) continue;

                DataTable emailDataTable = dataTable.Clone();
                for (int i = startIndex; i < startIndex + rowsForThisEmail; i++)
                    emailDataTable.ImportRow(dataTable.Rows[i]);

                startIndex += rowsForThisEmail;

                string emailSafe = toEmail.Replace("@", "_at_").Replace(".", "_dot_");
                string individualSavePath = Path.Combine(saveFolder, $"{filePrefix}_{emailSafe}.xlsx");

                Logger.LogInfo(91, $"Saving data to file: {individualSavePath}");
                string savedToPath = Utilities.ExportDataTableToExcel(emailDataTable, individualSavePath);

                Logger.LogInfo(92, $"Preparing to send email to {toEmail} with subject '{subject}'.");

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
                    foreach (string cc in ccEmails) mailMessage.CC.Add(cc);
                    mailMessage.Attachments.Add(new Attachment(savedToPath));

                    Logger.LogInfo(93, $"Sending email to {toEmail}.");
                    smtpClient.Send(mailMessage);
                    Logger.LogInfo(94, $"Email sent successfully to {toEmail}.");
                }
                catch (Exception ex)
                {
                    Logger.LogInfo(95, $"Failed to send email to {toEmail}. Error: {ex.Message}");
                }
            }
        }
    }
}
