using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
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

                // First query (CON 23/24)
                DataTable dataTableCon2324 = new DataTable();
                string queryCon2324 = $@"
                                       SELECT DISTINCT domanda.Cod_fiscale, Cognome, nome, Codice_Studente
FROM            Domanda inner join studente on Domanda.Cod_fiscale = Studente.Cod_fiscale 
inner join vStatus_compilazione on Domanda.Anno_accademico = vStatus_compilazione.anno_accademico and Domanda.Num_domanda = vStatus_compilazione.num_domanda
                    WHERE        (Domanda.Anno_accademico >= '20232024') AND (Tipo_bando = 'lz') AND domanda.Cod_fiscale IN
(SELECT DISTINCT 
    sps.Cod_fiscale
FROM STATUS_ALLEGATI AS sa
INNER JOIN Specifiche_permesso_soggiorno AS sps ON sa.id_allegato = sps.id_allegato
WHERE
    sa.data_validita = (
        SELECT MAX(data_validita)
        FROM STATUS_ALLEGATI AS STA
        WHERE id_allegato = sa.id_allegato
    )
    AND sps.Data_validita = (
        SELECT MAX(Data_validita)
        FROM Specifiche_permesso_soggiorno AS br
        WHERE Num_domanda = sps.Num_domanda AND Cod_fiscale = sps.Cod_fiscale
    )
    AND sa.cod_status = '01'
    AND sps.Anno_accademico IS NULL
)
and status_compilazione >= '90'
ORDER BY domanda.Cod_fiscale;
                ";

                Logger.LogInfo(30, "Executing SQL query for CON 23/24.");
                SqlCommand readDataCon2324 = new(queryCon2324, CONNECTION)
                {
                    CommandTimeout = 90000000
                };
                using (SqlDataReader readerCon2324 = readDataCon2324.ExecuteReader())
                {
                    dataTableCon2324.Load(readerCon2324);
                }

                Logger.LogInfo(40, $"Data extraction for CON 23/24 completed. Retrieved {dataTableCon2324.Rows.Count} rows.");

                // Second query (SENZA 23/24)
                DataTable dataTableSenza2324 = new DataTable();
                string querySenza2324 = $@"
                    SELECT DISTINCT domanda.Cod_fiscale, Cognome, nome, Codice_Studente
                    FROM            Domanda inner join studente on Domanda.Cod_fiscale = Studente.Cod_fiscale 
                    inner join vStatus_compilazione on Domanda.Anno_accademico = vStatus_compilazione.anno_accademico and Domanda.Num_domanda = vStatus_compilazione.num_domanda
                                        WHERE        (Domanda.Anno_accademico >= '20232024') AND (Tipo_bando = 'lz') AND domanda.Cod_fiscale IN
                    (SELECT DISTINCT 
                        sps.Cod_fiscale
                    FROM STATUS_ALLEGATI AS sa
                    INNER JOIN Specifiche_permesso_soggiorno AS sps ON sa.id_allegato = sps.id_allegato
                    WHERE
                        sa.data_validita = (
                            SELECT MAX(data_validita)
                            FROM STATUS_ALLEGATI AS STA
                            WHERE id_allegato = sa.id_allegato
                        )
                        AND sps.Data_validita = (
                            SELECT MAX(Data_validita)
                            FROM Specifiche_permesso_soggiorno AS br
                            WHERE Num_domanda = sps.Num_domanda AND Cod_fiscale = sps.Cod_fiscale
                        )
                        AND sa.cod_status = '01'
                        AND sps.Anno_accademico IS NULL
                    )
                    and status_compilazione >= '90'
                    ORDER BY domanda.Cod_fiscale;
                ";

                Logger.LogInfo(50, "Executing SQL query for SENZA 23/24.");
                SqlCommand readDataSenza2324 = new(querySenza2324, CONNECTION)
                {
                    CommandTimeout = 90000000
                };
                using (SqlDataReader readerSenza2324 = readDataSenza2324.ExecuteReader())
                {
                    dataTableSenza2324.Load(readerSenza2324);
                }

                Logger.LogInfo(60, $"Data extraction for SENZA 23/24 completed. Retrieved {dataTableSenza2324.Rows.Count} rows.");

                if (_sendMail)
                {
                    List<string> toEmailsCon2324 = new();
                    List<string> toEmailsSenza2324 = new();
                    List<string> ccEmails = new();

                    Logger.LogInfo(70, "Reading email configurations from the file.");

                    if (!File.Exists(mailFilePath))
                    {
                        Logger.LogInfo(100, $"Mail file path '{mailFilePath}' does not exist.");
                        return;
                    }

                    using (StreamReader sr = new(mailFilePath))
                    {
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.StartsWith("TO#SI#"))
                            {
                                toEmailsCon2324.Add(line.Substring(6));
                            }
                            else if (line.StartsWith("TO#NO#"))
                            {
                                toEmailsSenza2324.Add(line.Substring(6));
                            }
                            else if (line.StartsWith("CC#"))
                            {
                                ccEmails.Add(line[3..]);
                            }
                            else if (line.StartsWith("ID#") && string.IsNullOrEmpty(senderMail))
                            {
                                senderMail = line[3..];
                            }
                            else if (line.StartsWith("PW#") && string.IsNullOrEmpty(senderPassword))
                            {
                                senderPassword = line[3..];
                            }
                        }
                    }

                    if (toEmailsCon2324.Count == 0 && toEmailsSenza2324.Count == 0)
                    {
                        Logger.LogInfo(100, "No recipient email addresses found.");
                        return;
                    }

                    if (string.IsNullOrEmpty(senderMail) || string.IsNullOrEmpty(senderPassword))
                    {
                        Logger.LogInfo(100, "Sender email credentials are missing.");
                        return;
                    }

                    // Create a directory with the current date to save the files
                    string currentDateFolder = Path.Combine(savePath, DateTime.Now.ToString("yyyyMMdd"));
                    if (!Directory.Exists(currentDateFolder))
                    {
                        Directory.CreateDirectory(currentDateFolder);
                    }

                    // Handle the first dataset (CON 23/24)
                    if (toEmailsCon2324.Count > 0)
                    {
                        SendEmailWithAttachment(toEmailsCon2324, ccEmails, dataTableCon2324, currentDateFolder, "CON_2324", "CON 23/24");
                    }

                    // Handle the second dataset (SENZA 23/24)
                    if (toEmailsSenza2324.Count > 0)
                    {
                        SendEmailWithAttachment(toEmailsSenza2324, ccEmails, dataTableSenza2324, currentDateFolder, "SENZA_2324", "SENZA 23/24");
                    }
                }

                Logger.LogInfo(100, "Processing completed successfully.");
            }
            catch (Exception ex)
            {
                Logger.LogInfo(null, $"An error occurred during processing: {ex.Message}");
            }
        }

        private void SendEmailWithAttachment(List<string> toEmails, List<string> ccEmails, DataTable dataTable, string saveFolder, string filePrefix, string subjectSuffix)
        {
            // Calculate the number of rows per email
            int rowsPerEmail = dataTable.Rows.Count / toEmails.Count;
            int remainder = dataTable.Rows.Count % toEmails.Count;

            int startIndex = 0;

            foreach (var toEmail in toEmails)
            {
                // Calculate the number of rows to include for this email
                int rowsForThisEmail = rowsPerEmail + (remainder > 0 ? 1 : 0);
                remainder = Math.Max(0, remainder - 1);

                // Create a new DataTable for this email
                DataTable emailDataTable = dataTable.Clone();
                for (int i = startIndex; i < startIndex + rowsForThisEmail; i++)
                {
                    emailDataTable.ImportRow(dataTable.Rows[i]);
                }

                startIndex += rowsForThisEmail;

                string emailSafe = toEmail.Replace("@", "_at_").Replace(".", "_dot_");
                string individualSavePath = Path.Combine(saveFolder, $"{filePrefix}_{emailSafe}.xlsx");

                Logger.LogInfo(91, $"Saving data to file: {individualSavePath}");
                string savedToPath = Utilities.ExportDataTableToExcel(emailDataTable, individualSavePath);

                Logger.LogInfo(92, $"Preparing to send email to {toEmail} with subject '{subjectSuffix}'.");

                try
                {
                    SmtpClient smtpClient = new("smtp.gmail.com")
                    {
                        Port = 587,
                        Credentials = new NetworkCredential(senderMail, senderPassword),
                        EnableSsl = true,
                    };

                    MailMessage mailMessage = new()
                    {
                        From = new MailAddress(senderMail),
                        Subject = $"Estrazione studenti per PS - {DateTime.Now:dd/MM/yyyy}",
                        Body = $@"  <p>Buongiorno,</p>
                           <p>su richiesta di Rita che legge in copia,</p>
                           <p>in allegato troverai l'estrazione aggiornata alla data odierna relativa agli studenti stranieri per cui devono essere <b>urgentemente</b> validati i documenti di soggiorno (passaporto/richiesta o rinnovo PS/permesso di soggiorno).</p>
                            <p>L'allegato presente in questa mail sostituisce gli allegati già ricevuti in precedenza.</p>
                            <p>Ti chiedo di suddividere il file e condividere le parti con il personale a te assegnato, specificando che <b>non devono rimuovere i blocchi</b> poiché è attiva una procedura che li rimuove massivamente ogni 2 ore circa.</p> 

                           <p>Grazie e buon lavoro!</p>",
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    foreach (string ccEmail in ccEmails)
                    {
                        mailMessage.CC.Add(ccEmail);
                    }

                    mailMessage.Attachments.Add(new Attachment(savedToPath));

                    Logger.LogInfo(93, $"Sending email to {toEmail}.");
                    smtpClient.Send(mailMessage);
                    Logger.LogInfo(94, $"Email sent successfully to {toEmail}.");
                    mailMessage.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogInfo(95, $"Failed to send email to {toEmail}. Error: {ex.Message}");
                }
            }
        }

    }
}
