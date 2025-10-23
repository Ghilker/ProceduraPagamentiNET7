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

        public string BlocchiJoined => BlocchiParts.Count == 0 ? "" : string.Join(" | ", BlocchiParts.OrderBy(x => x));
        public string EsitoBSJoined => EsitiBS.Count == 0 ? "" : string.Join(" | ", EsitiBS.OrderBy(x => x));
        public string EsitoPAJoined => EsitiPA.Count == 0 ? "" : string.Join(" | ", EsitiPA.OrderBy(x => x));
        public string SediStudiJoined => SediStudi.Count == 0 ? "" : string.Join(" | ", SediStudi.OrderBy(x => x));
        public string SediDescrizioniJoined => SediDescrizioni.Count == 0 ? "" : string.Join(" | ", SediDescrizioni.OrderBy(x => x));
    }

    internal class ProceduraTicket : BaseProcedure<ArgsProceduraTicket>
    {
        private bool _isFileWithMessagges;
        private bool _deleteAnniPrecedenti;
        private bool _sendMail;

        // anni target configurabili
        private readonly List<int> _targetYears = new() { 20242025, 20252026 };

        public ProceduraTicket(MasterForm masterForm, SqlConnection mainConn) : base(masterForm, mainConn) { }

        public override void RunProcedure(ArgsProceduraTicket args)
        {
            _isFileWithMessagges = args._ticketChecks[0];
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

                // aggiungi colonne per anni target
                EnsureTicketColumns(tickets, _targetYears);

                // keyword labels (etichette sole)
                EnsureTopicColumns(tickets);

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
                        }
                        else
                        {
                            r[$"BLOCCHI_{y}"] = "";
                            r[$"ESITO_BS_{y}"] = "";
                            r[$"ESITO_PA_{y}"] = "";
                            r[$"SEDE_DESCR_{y}"] = "";
                        }
                    }

                    // keyword engine V6: solo etichette
                    var text = SafeStr(r, "PRIMO_MSG_STUDENTE");
                    var ext = KeywordEngineV6.Extract(text);
                    r["ARGOMENTO_PRIMARIO"] = ext.TopicPrimary ?? "";
                    r["ARGOMENTO_SECONDARIO"] = ext.TopicSecondary ?? "";
                    r["RIFERITO_A_BLOCCHI"] = ext.TopicTertiary ?? "";
                }

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
                        <p>vi invio l'estrazione dei ticket aperti e nuovi dal 01/06/2025 
                        con gli esiti di borsa e i blocchi presenti, integrato con le università
                        di appartenenza dello studente.</p>";

                    messageBody += @"
                        <p>I ticket contengono anche il primo messaggio che lo studente 
                        ha inviato, dati su blocchi ed esiti ed in più una sezione che cerca di riassumere il contenuto
                        del ticket così da facilitare la lavorazione e migliorare la distribuzione per funzioni.</p>";

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
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                _masterForm.inProcedure = false;
                Logger.Log(100, "Fine Lavorazione", LogLevel.INFO);
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
            }
        }

        private static void EnsureTopicColumns(DataTable t)
        {
            AddStringCol(t, "ARGOMENTO_PRIMARIO");
            AddStringCol(t, "ARGOMENTO_SECONDARIO");
            AddStringCol(t, "RIFERITO_A_BLOCCHI");
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
}
