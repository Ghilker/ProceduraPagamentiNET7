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
                var dtBs = ExecuteQuery(@"
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
       AND d.Tipo_bando = 'lz'
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
                Logger.LogInfo(40, $"[BS] Retrieved {dtBs.Rows.Count} rows.");

                var dtPa = ExecuteQuery(@"
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
       AND d.Tipo_bando = 'lz'
    INNER JOIN vEsiti_concorsi AS ve
        ON d.Num_domanda = ve.Num_domanda
       AND ve.Cod_beneficio = 'pa'
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
                Logger.LogInfo(41, $"[PA] Retrieved {dtPa.Rows.Count} rows.");

                // =================== Filtro BS \ PA ===================
                var paSet = new HashSet<string>(
                    dtPa.AsEnumerable()
                        .Select(r => (r.Field<string>("Cod_fiscale") ?? "").Trim())
                        .Where(cf => !string.IsNullOrEmpty(cf)),
                    StringComparer.OrdinalIgnoreCase);

                DataTable dtBsFiltered;
                var bsRows = dtBs.AsEnumerable()
                    .Where(r =>
                    {
                        var cf = (r.Field<string>("Cod_fiscale") ?? "").Trim();
                        return !string.IsNullOrEmpty(cf) && !paSet.Contains(cf);
                    });

                if (bsRows.Any())
                {
                    dtBsFiltered = bsRows.CopyToDataTable();
                    Logger.LogInfo(41, $"[BS] Filtrate {dtBs.Rows.Count - dtBsFiltered.Rows.Count} sovrapposizioni con PA. Rimasti {dtBsFiltered.Rows.Count}.");
                }
                else
                {
                    dtBsFiltered = dtBs.Clone();
                    Logger.LogInfo(41, $"[BS] Tutti i {dtBs.Rows.Count} record erano in PA. Rimasti 0.");
                }

                if (_sendMail)
                {
                    if (!File.Exists(mailFilePath))
                    {
                        Logger.LogInfo(100, $"Mail file path '{mailFilePath}' does not exist.");
                        return;
                    }

                    var toEmails = new List<string>();
                    var ccEmails = new List<string>();
                    ReadMailConfig(mailFilePath, toEmails, ccEmails, ref senderMail, ref senderPassword);

                    if (toEmails.Count == 0)
                    {
                        Logger.LogInfo(100, "No recipient email addresses found.");
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

                    // =================== Invii (PA prima) ===================
                    if (dtPa.Rows.Count > 0)
                    {
                        SendEmailWithAttachment(
                            toEmails, ccEmails, dtPa, currentDateFolder,
                            filePrefix: "ps_pa",
                            subject: $"Estrazione studenti per PS (PA) - {DateTime.Now:dd/MM/yyyy}",
                            htmlBody: GetMailBodyPa(),
                            minRowsPerEmail: 10
                        );
                    }
                    else
                    {
                        Logger.LogInfo(61, "[PA] Nessun record: nessun invio.");
                    }

                    if (dtBsFiltered.Rows.Count > 0)
                    {
                        SendEmailWithAttachment(
                            toEmails, ccEmails, dtBsFiltered, currentDateFolder,
                            filePrefix: "ps_bs",
                            subject: $"Estrazione studenti per PS (BS) - {DateTime.Now:dd/MM/yyyy}",
                            htmlBody: GetMailBodyBs(),
                            minRowsPerEmail: 10
                        );
                    }
                    else
                    {
                        Logger.LogInfo(60, "[BS] Nessun record dopo filtro PA: nessun invio.");
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

        private void ReadMailConfig(string path, List<string> toEmails, List<string> ccEmails, ref string id, ref string pw)
        {
            Logger.LogInfo(70, "Reading email configurations from the file.");
            using var sr = new StreamReader(path);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (line.StartsWith("TO#SI#"))
                    toEmails.Add(line.Substring(6));
                else if (line.StartsWith("CC#"))
                    ccEmails.Add(line[3..]);
                else if (line.StartsWith("ID#") && string.IsNullOrEmpty(id))
                    id = line[3..];
                else if (line.StartsWith("PW#") && string.IsNullOrEmpty(pw))
                    pw = line[3..];
            }
        }

        private string GetMailBodyBs() => @"
<p>Buongiorno,</p>
<p>su richiesta di Rita che legge in copia,</p>
<p>in allegato troverai l'estrazione aggiornata alla data odierna relativa agli studenti stranieri per cui devono
essere validati i documenti di soggiorno (passaporto/richiesta o rinnovo PS/permesso di soggiorno), questo file dovrà essere lavorato a seguito del completamento della lavorazione del file precedente.
In questo file potranno essere presenti studenti già lavorati per sovrapposizione con i posti alloggio</p>
<p>Grazie e buon lavoro!</p>
<p>Giacomo Pavone</p>";

        private string GetMailBodyPa() => @"
<p>Buongiorno,</p>
<p>su richiesta di Rita che legge in copia,</p>
<p>in allegato troverai l'estrazione aggiornata alla data odierna relativa agli studenti stranieri per cui devono essere validati in maniera urgente i documenti di soggiorno (passaporto/richiesta o rinnovo PS/permesso di soggiorno).</p>
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
