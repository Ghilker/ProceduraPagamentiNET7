using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>Regole di chiusura assistita. Non modifica ticket, documenti o pagamenti.</summary>
    internal static class TicketClosureReview
    {
        private static readonly HashSet<string> IgnoredCategories = new(StringComparer.Ordinal)
        {
            "contributi alloggio", "posti alloggio", "mensa", "accettazione posto alloggio",
            "rimborso deposito cauzionale", "rimborso deposito cauzionale-non piu attiva",
            "area 8", "ufficio inclusione", "urp", "urp - non piu attiva",
            "certificazione unica", "portafuturo"
        };

        public static bool IsEligible(DataRow row) =>
            (Read(row, "STATO").Equals("NUOVO", StringComparison.OrdinalIgnoreCase) ||
             Read(row, "STATO").Equals("APERTO", StringComparison.OrdinalIgnoreCase)) &&
            Read(row, "UID").Length == 0 && Read(row, "PRIMO_MSG_OPERATORE").Length == 0 &&
            !Read(row, "AZIONE").Equals("PRESA_IN_CARICO", StringComparison.OrdinalIgnoreCase) &&
            !IgnoredCategories.Contains(Normalize(Read(row, "CATEGORIA")));

        public static DataTable SelectEligible(DataTable source)
        {
            DataTable result = source.Clone();
            foreach (DataRow row in source.Rows)
                if (IsEligible(row)) result.ImportRow(row);
            return result;
        }

        public static DataTable Build(
            DataTable tickets,
            IReadOnlyDictionary<string, List<TicketOfficeRecord>> officeRecords)
        {
            var items = tickets.AsEnumerable().Where(IsEligible)
                .Select(row => Assess(row, officeRecords))
                .OrderBy(item => item.Created ?? DateTime.MaxValue)
                .ThenBy(item => NumericId(Read(item.Source, "ID_TICKET")))
                .ThenBy(item => Read(item.Source, "ID_TICKET"), StringComparer.Ordinal)
                .ToList();
            MarkDuplicates(items);

            var result = new DataTable();
            foreach (string name in new[] { "ID_TICKET", "AZIONE_PROPOSTA", "REGOLA", "MOTIVO", "EVIDENZE",
                "TICKET_PRINCIPALE", "ANNO_ACCADEMICO_RICHIESTA", "NUM_DOMANDA", "CODFISC", "CATEGORIA",
                "SOTTOCATEGORIA", "OGGETTO", "PRIMO_MSG_STUDENTE" })
                result.Columns.Add(name, typeof(string));
            result.Columns.Add("DATA_CREAZIONE", typeof(DateTime));
            foreach (string name in new[] { "IMPORTO_BORSA_LORDO", "PAGATO_BORSA_LORDO", "PAGATO_RIMBORSO_TASSA" })
                result.Columns.Add(name, typeof(decimal));
            foreach (string name in new[] { "RICHIESTE_RICONOSCIUTE", "CODICI_BLOCCHI_ATTIVI", "DETTAGLIO_PAGAMENTI", "ISTANZA_DOMICILIO", "VERSIONE_REGOLE" })
                result.Columns.Add(name, typeof(string));
            result.Columns.Add("RESIDUO_BORSA_LORDO", typeof(decimal));
            result.Columns.Add("DATA_ESTRAZIONE", typeof(DateTime));
            DateTime extractedAt = DateTime.Now;

            foreach (Assessment item in items)
            {
                DataRow row = result.NewRow();
                foreach (string name in new[] { "ID_TICKET", "CODFISC", "CATEGORIA", "SOTTOCATEGORIA", "OGGETTO", "PRIMO_MSG_STUDENTE" })
                    row[name] = Read(item.Source, name);
                row["AZIONE_PROPOSTA"] = item.CanClose ? "CHIUDERE" : "VERIFICARE";
                row["REGOLA"] = item.DuplicateOf.Length > 0 ? "DUPLICATO" : string.Join(" | ", item.Checks.Select(c => c.Code).Distinct());
                row["MOTIVO"] = item.DuplicateOf.Length > 0
                    ? "Richiesta duplicata; mantenere il ticket più vecchio indicato."
                    : item.CanClose ? "Tutte le richieste riconosciute sono soddisfatte o previste su un altro canale."
                    : string.Join(" | ", item.Problems.Concat(item.Checks.Where(c => !c.Resolved).Select(c => c.Evidence)).Distinct());
                row["EVIDENZE"] = string.Join(" | ", item.Checks.Select(c => c.Code + ": " + c.Evidence).Distinct());
                row["TICKET_PRINCIPALE"] = item.DuplicateOf;
                row["ANNO_ACCADEMICO_RICHIESTA"] = item.Year.HasValue ? TicketAcademicYearResolver.Format(item.Year.Value) : "";
                row["NUM_DOMANDA"] = item.Record?.ApplicationNumber ?? "";
                row["DATA_CREAZIONE"] = item.Created.HasValue ? (object)item.Created.Value : DBNull.Value;
                row["RICHIESTE_RICONOSCIUTE"] = string.Join(" | ", item.Checks.Select(c => c.Code).Distinct());
                row["VERSIONE_REGOLE"] = TicketClosureKnowledge.Version;
                row["DATA_ESTRAZIONE"] = extractedAt;
                row["CODICI_BLOCCHI_ATTIVI"] = item.Record?.HasBlockCodeData == true
                    ? (item.Record.ActiveBlockCodes.Length == 0 ? "NESSUNO" : item.Record.ActiveBlockCodes) : "NON DISPONIBILI";
                if (item.Record != null)
                {
                    row["IMPORTO_BORSA_LORDO"] = item.Record.BenefitAmount;
                    row["PAGATO_BORSA_LORDO"] = item.Record.BsPaidAmount;
                    row["PAGATO_RIMBORSO_TASSA"] = item.Record.TaxRefundPaidAmount;
                    row["RESIDUO_BORSA_LORDO"] = Math.Max(0m, item.Record.BenefitAmount - item.Record.BsPaidAmount);
                    row["DETTAGLIO_PAGAMENTI"] = item.Record.PaymentEvidence;
                    row["ISTANZA_DOMICILIO"] = item.Record.DomicileRequestNumber;
                }
                result.Rows.Add(row);
            }
            return result;
        }

        private static Assessment Assess(DataRow row, IReadOnlyDictionary<string, List<TicketOfficeRecord>> records)
        {
            string message = Read(row, "PRIMO_MSG_STUDENTE");
            string subject = Read(row, "OGGETTO");
            string declared = Read(row, "AA_DICHIARATO_TICKET");
            if (declared.Length == 0) declared = Read(row, "ANNO_ACCADEMICO_RICHIESTA");
            var year = TicketAcademicYearResolver.Resolve(declared, message);
            if (!year.HasAcademicYear) year = TicketAcademicYearResolver.Resolve(declared, subject);
            var item = new Assessment(row, year.AcademicYear, ParseDate(Read(row, "DATA_CREAZIONE")));
            var messageYear = TicketAcademicYearResolver.Resolve("", message);
            var subjectYear = TicketAcademicYearResolver.Resolve("", subject);
            if (TicketAcademicYearResolver.TryParseAcademicYear(declared, out int declaredYear) &&
                messageYear.AcademicYear.HasValue && messageYear.AcademicYear.Value != declaredYear)
                item.Problems.Add("Anno dichiarato e anno del messaggio discordanti: verificare l'anno della richiesta.");
            if (item.Year.HasValue && subjectYear.AcademicYear.HasValue && item.Year != subjectYear.AcademicYear)
                item.Problems.Add("Anno nell'oggetto discordante: verificare l'anno della richiesta.");
            if (!item.Year.HasValue) item.Problems.Add("Anno accademico non identificato; nessun ripiego sull'ultima domanda.");
            if (Read(row, "ID_TICKET").Length == 0) item.Problems.Add("Identificativo ticket mancante.");
            string fiscalCode = Read(row, "CODFISC");
            if (fiscalCode.Length == 0) item.Problems.Add("Codice fiscale mancante.");
            if (item.Year.HasValue && records.TryGetValue(fiscalCode, out var applications))
                item.Record = applications.Where(r => r.AcademicYear == item.Year.Value)
                    .OrderByDescending(r => NumericId(r.ApplicationNumber)).FirstOrDefault();

            if (MultipleYears(message) || MultipleYears(subject)) item.Problems.Add("Il ticket cita più anni accademici: verificare ogni richiesta.");
            if (message.Length == 0) item.Problems.Add("Messaggio studente assente.");

            string text = Normalize(message);
            item.Text = text;
            item.Subject = Normalize(subject);
            if (Match(text, @"(?:rimbors\w*|pagament\w*|accredit\w*).{0,70}(?:due|tre|quattro|[2-9]) annualita"))
                item.Problems.Add("Richiesta di più annualità: occorre verificare separatamente ogni anno.");
            AnalyzeManualRequests(item, text, item.Subject);
            var parts = Regex.Split(text, @"(?<=[!?;])\s*|[\r\n]+|(?<!\d)\.(?!\d)|\b(?:inoltre|e poi|also)\b|\band\s+(?=(?:i\s+)?(?:want|need|would|ask|request|could|should)\b)|\be\s+(?=(?:vi\s+)?(?:vorrei|chiedo|devo|voglio|potete|richiedo|desidero)\b)")
                .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            foreach (string part in parts)
            {
                int before = item.Checks.Count;
                AnalyzePart(item, part);
                if (item.Checks.Count == before)
                {
                    if (item.Checks.LastOrDefault()?.Code is "PERMESSO" or "PERMESSO_CARICATO" &&
                        Match(part, @"^(?:vi |i |please )?(?:chiedo|richiedo|potete|vorrei|would|could|validate|verify).{0,35}(?:verific\w*|valid\w*|conferm\w*|check|verify)|^please (?:validate|verify|check) (?:it|them)"))
                    {
                        var family = TicketClosureKnowledge.BlockFamilies.Single(f => f.Code == "PERMESSO");
                        Add(item, "PERMESSO", TicketClosureKnowledge.Absent(item.Record, family), TicketClosureKnowledge.BlockEvidence(item.Record, family), part);
                    }
                    Check? reference = Match(part, @"(?:chiedo|richiedo|potete|vorrei).{0,40}(?:lavorarla|verificarla|controllarla|conferma (?:della )?ricezione|conferma.{0,15}ricezione|riscontro)|(?:necessario|devo).{0,25}caricarli.{0,30}altra parte|devo fare altro.{0,35}fuori\s?sede")
                        ? item.Checks.LastOrDefault(c => c.Code.StartsWith("ISTANZA_", StringComparison.Ordinal))
                        : Match(part, @"^(?:vi )?(?:chiedo|richiedo).{0,20}riemission\w*\b")
                            ? item.Checks.LastOrDefault(c => c.Code is "PAGAMENTO_BORSA" or "PRIMA_RATA" or "INTEGRAZIONE_BORSA") : null;
                    if (reference != null && item.Checks.Count == before) item.Checks.Add(reference);
                }
                if (item.Checks.Count == before && (part.Contains('?') || IsRequest(part) || Match(part, @"\b(?:non|not|cannot|unable)\b|errat\w*|sbagliat\w*|incongruenz\w*|mancant\w*|sospes\w*|risult\w*.{0,25}esclus\w*")))
                    item.Problems.Add("Richiesta non coperta dalle regole: " + Short(part));
            }
            if (item.Checks.Count == 0) item.Problems.Add("Nessuna richiesta riconosciuta con sufficiente certezza.");
            return item;
        }

        private static void AnalyzeManualRequests(Assessment item, string text, string subject)
        {
            string all = subject + "; " + text;
            if (Match(all, @"(?:cambi\w*|modific\w*|rettific\w*|corregg\w*|aggiorn\w*).{0,45}(?:residenza|anagrafic\w*|codice fiscale|cognome|nome|indirizzo email|numero di telefono)|(?:residenza|dati anagrafici|codice fiscale|cognome|nome|telefono).{0,45}(?:cambiat\w*|modificat\w*|errat\w*|sbagliat\w*|non.{0,6}corrett\w*)|(?:change|update|correct).{0,35}(?:fiscal code|surname|date of birth|residence address)|(?:fiscal code|surname|date of birth).{0,25}(?:wrong|incorrect)"))
                Add(item, "DATI_ANAGRAFICI", false, "Residenza e rettifiche anagrafiche richiedono lavorazione tramite ticket.", text);
            if (Match(all, @"(?:rilasci\w*|richied\w*|ottenere|ho bisogno|mi servir\w*|mi serve|inviar\w*|avete).{0,50}(?:attestazion\w*|certificat\w*|document[oi]|quietanz\w*|\bcud\b)|(?:certificat\w*|attestazion\w*|documento di residenza).{0,35}(?:mi serve|mi inviate|inviarmi)|certificazione fiscale|\bcud\b|(?:need|request|issue|send me).{0,40}(?:certificate|payment receipt|statement)"))
                Add(item, "DOCUMENTO_RICHIESTO", false, "Rilascio di documento o certificazione non dimostrato come effettuato.", text);
            if (TicketClosureKnowledge.FinancialDispute(all))
                Add(item, "CONTESTAZIONE_IMPORTO", false, "Revisione, maggiorazione o contestazione dell'importo: la sola emissione non risolve la richiesta.", text);
            if (Match(text, @"rateizzazion\w*|\bdebito\b|repay\w*|pay back|return.{0,20}(?:scholarship|money|grant)") || Match(text, @"restitu\w*|ridare") && Match(all, @"\bborsa\b|\brata\b|\bsoldi\b"))
                Add(item, "RESTITUZIONE_BORSA", false, "Restituzione o rateizzazione da lavorare.", text);
            if (Match(all, @"(?:aggiorn\w*|rettific\w*|modific\w*|corregg\w*|riconosc\w*).{0,45}(?:cfu|crediti|esami|\bisee\w*)|(?:cfu|crediti|esami).{0,35}(?:non registrat\w*|mancant\w*|non riconosciut\w*)|(?:nel portale|nel sistema).{0,35}\d+ cfu.{0,60}(?:mentre|invece)|crediti.{0,20}not registered"))
                Add(item, "RETTIFICA_DATI_DOMANDA", false, "Rettifica di merito o dati economici non dimostrata come effettuata.", text);
            if (Match(text, @"\bcfu\b|\bcrediti\b|\besami\b") && Match(text, @"ho sbagliato|erroneamente|per errore|accidentalmente|discrepanz\w*"))
                Add(item, "RETTIFICA_DATI_DOMANDA", false, "Dichiarazione di crediti o esami errata: l'assenza di blocchi non dimostra la rettifica.", text);
            if (Match(text, @"(?:domicilio|indirizzo di domicilio).{0,25}(?:non e corretto|errat\w*|sbagliat\w*)"))
                Add(item, "ANOMALIA_DOMICILIO", false, "Dati di domicilio segnalati come errati: correzione non dimostrata.", text);
            if (Match(text, @"(?:comunicher\w*|trasmetter\w*|inviar\w*).{0,55}(?:universita|ateneo|segreteria)"))
                Add(item, "COMUNICAZIONE_ATENEO", false, "Comunicazione all'ateneo non dimostrata come effettuata.", text);
            if (Match(text, @"rimbors\w*.{0,25}tass\w*") && !Match(all, @"tass[ae] regional[ei]|regional tax"))
                Add(item, "RIMBORSO_DA_IDENTIFICARE", false, "Rimborso tasse non identificato come tassa regionale: non usare automaticamente i movimenti BST.", text);
            if (Match(all, @"(?:assenz\w*|non risult\w*|non (?:e |sono |risulto )?present\w*).{0,80}graduatori\w*|graduatori\w*.{0,70}(?:non risult\w*|assent\w*|non present\w*)|risult\w*.{0,20}ancora idone\w*.{0,65}scorriment\w*|(?:premio|posizione).{0,100}non riesc\w*.{0,30}visualizz\w*"))
                Add(item, "ANOMALIA_GRADUATORIA", false, "Presenza, posizione o esito in graduatoria da verificare; la trasmissione della domanda non basta.", text);
            if (Match(text, @"(?:fiss\w*|prenot\w*|richied\w*|vorrei).{0,35}appuntamento|(?:need|book|request|schedule).{0,30}appointment|ricontattat\w* telefonicamente|impedimento (?:sanitario|di salute)|forza maggiore"))
                Add(item, "ASSISTENZA_PERSONALE", false, "Richiesta di assistenza personale o appuntamento da lavorare.", text);
        }

        private static void AnalyzePart(Assessment item, string part)
        {
            string context = part;
            TicketOfficeRecord? record = item.Record;
            bool technical = TicketClosureKnowledge.Technical(part);
            bool block = Match(part, @"\b(?:blocc\w*|sblocc\w*|incongruenz\w*|unblock\w*|blocks?|blocked)\b");
            bool genericInfo = TicketClosureKnowledge.GeneralQuestion(part);
            bool anomaly = technical || Match(part, @"contest\w*|ricorso|reclam\w*|importo (?:errato|inferiore|sbagliato)|non corretto|meno del previsto|incongruenz\w*|sbagliat\w*|esclus[oa]|non risult\w*|non.{0,25}registrat\w*");

            bool domicile = Match(context, @"\b(?:domicilio|domicile|locazione|affitto|rental|lease|tenancy)\b");
            string careerContext = Regex.Replace(context, @"\bfuori\s+sede\b", "fuorisede");
            bool career = Match(careerContext, @"\b(?:cambio|cambiare|cambiato|modifica|aggiornamento)\b.{0,40}\b(?:corso|sede|ateneo)\b|passaggio di corso|(?:trasferimento|trasferit[oa]).{0,35}\b(?:corso|sede|universita|ateneo)\b|abbreviazione di corso|(?:change|changing|transfer).{0,25}(?:course|university|campus)");
            bool withdrawal = Match(context, @"\brinunci\w*\b|\bwithdraw(?:al)?\b|\brenounc\w*\b") &&
                !Match(context, @"non.{0,30}rinunci\w*|(?:do not|don't|not).{0,25}(?:withdraw|renounc)") &&
                !Match(context, @"mensa|monetizzazion\w*|contributo alloggio") &&
                Match(context, @"\b(?:borsa|alloggio|studi|corso|scholarship|studies|course|university)\b");
            bool iban = Match(context, @"\biban\b|bank account");
            bool ibanChange = iban && Match(context, @"modific\w*|cambi\w*|aggiorn\w*|inser\w*|nuovo|update|new|change");
            bool regionalTax = Match(context, @"tass[ae] regional[ei]|regional tax");
            bool refund = regionalTax && (Match(context, @"rimbors\w*|refund|accredit\w*|erog\w*") || Match(item.Subject, @"rimbors\w*|refund"));
            bool permit = Match(context, TicketClosureKnowledge.BlockFamilies.Single(f => f.Code == "PERMESSO").Pattern);
            bool explicitBsPayment = Match(context, @"\b(?:saldo|rata|rate|tranche|installments?|instalments?|balance)\b");
            bool prize = Match(context + " " + item.Subject, @"premio (?:di )?laurea|graduation (?:award|bonus)");
            bool universityFee = Match(context, @"(?:pagament\w*|pagare).{0,25}(?:tass[ae]|rett[ae]|contribut[oi]).{0,25}(?:iscrizione|universitar\w*)");
            bool payment = (!refund || explicitBsPayment && Match(context, @"\bborsa\b|scholarship")) && (!prize || explicitBsPayment && Match(context, @"\bborsa\b|scholarship")) && !Match(context, @"restituzion\w*|rateizzazion\w*|\bdebito\b") &&
                (!universityFee || Match(context, @"(?:saldo|rata|tranche).{0,20}borsa|pagamento della borsa|scholarship payment")) &&
                Match(context, @"\b(?:saldo|rata|rate|tranche|installments?|instalments?|balance)\b|pagament\w*|accredit\w*|erog\w*|liquid\w*|bonific\w*|trasferimento.{0,25}importo|\bpayment\b|(?:quando|ricev\w*).{0,30}\bresto\b|remaining (?:amount|part)|remainder") &&
                Match(item.Subject + " " + item.Text, @"\bborsa\b|scholarship|\bbds\b");
            bool amountDispute = TicketClosureKnowledge.FinancialDispute(part);
            bool generalSchedule = genericInfo && !anomaly && !Match(part, @"\b(?:mio|mia|miei|mie|my)\b|non ho ricevuto|non ricevut\w*|non accredit\w*") &&
                Match(part, @"quando (?:iniziano|inizieranno|inizia|iniziera)|calendario|tempistiche generali");
            bool manualData = item.Checks.Any(c => c.Code == "DATI_ANAGRAFICI");
            bool documentRequest = item.Checks.Any(c => c.Code == "DOCUMENTO_RICHIESTO");
            bool debt = Match(part, @"rateizzazion\w*|(?:restituzion\w*|restitui\w*).{0,45}borsa|\bdebito\b");
            bool meritUpdate = Match(part, @"(?:aggiorn\w*|rettific\w*|modific\w*|riconosc\w*).{0,35}(?:cfu|credit\w*|esam\w*)|(?:cfu|credit\w*|esam\w*).{0,35}(?:non registrat\w*|mancant\w*)");
            if (debt) Add(item, "RESTITUZIONE_BORSA", false, "Restituzione o rateizzazione da lavorare.", part);
            if (amountDispute) Add(item, "CONTESTAZIONE_IMPORTO", false, "La contestazione dell'importo richiede verifica.", part);
            if (meritUpdate) Add(item, "AGGIORNAMENTO_MERITO", false, "Aggiornamento di esami o crediti da verificare.", part);

            if (domicile)
            {
                bool submission = Match(part, @"(?:non riesc\w*|impossibil\w*|cannot|unable).{0,25}(?:invi\w*|trasmett\w*|present\w*|submit\w*)");
                bool completed = record?.DomicileRequestDate != null && record.DomicileRequestNumber.Length > 0 && item.Created.HasValue && record.DomicileRequestDate >= item.Created;
                Add(item, "ISTANZA_DOMICILIO", !technical || (submission && completed),
                    technical ? submission && completed ? $"Istanza {record!.DomicileRequestNumber} presentata il {record.DomicileRequestDate:dd/MM/yyyy HH:mm}: invio effettuato dopo il ticket." : "Anomalia tecnica sul domicilio da verificare."
                    : "Domicilio e contratto si gestiscono tramite istanza; anche il sollecito di istanza pendente è chiudibile.", part);
                if (Match(part, @"status.{0,25}(?:errat\w*|sbagliat\w*)|risult\w*.{0,20}pendolare"))
                    Add(item, "DOMICILIO_STATUS", record?.CampusStatus == "B", record?.CampusStatus == "B"
                        ? "Status attuale fuori sede: modifica richiesta già effettuata." : "Anomalia sullo status fuori sede non dimostrata come risolta.", part);
            }
            if (career)
                Add(item, "ISTANZA_CARRIERA", !anomaly, anomaly ? "Anomalia personale sulla carriera da verificare." : "Cambio corso, sede o trasferimento tramite istanza aperta.", part);
            if (withdrawal)
            {
                string kind = Match(context, @"posto alloggio|alloggio") ? "ALLOGGIO" : Match(context, @"rinunci\w*.{0,12}(?:agli studi|all'universita|al corso)") ? "STUDI" : "BORSA";
                Add(item, "ISTANZA_RINUNCIA_" + kind, !technical, technical ? "Errore nella presentazione della rinuncia da verificare." : "Rinuncia da presentare tramite istanza.", part);
            }
            if (ibanChange)
            {
                string requestedIban = Regex.Match(part, @"\b[a-z]{2}\d{2}[a-z0-9]{11,30}\b", RegexOptions.IgnoreCase).Value;
                bool updated = record?.IbanDataValidita != null && record.Iban.Length > 0 && item.Created.HasValue && record.IbanDataValidita.Value >= item.Created.Value &&
                    (requestedIban.Length == 0 || requestedIban.Equals(Regex.Replace(record.Iban, @"\s+", ""), StringComparison.OrdinalIgnoreCase));
                Add(item, "PROFILO_IBAN", !technical || updated, technical ? updated ? "IBAN aggiornato dopo l'apertura del ticket." : "Anomalia tecnica nella modifica dell'IBAN da verificare." : "Lo studente modifica autonomamente l'IBAN nella sezione Profilo.", part);
            }
            if (refund)
            {
                bool paid = record != null && record.TaxRefundPaidAmount > 0m && record.TaxRefundMandates.Length > 0;
                bool generalRefund = !TicketClosureKnowledge.PersonalAnomaly(item.Text) && !technical && Match(context, @"come funziona|how does|automatic\w*|bisogna fare|devo fare.{0,20}richiesta") && !Match(item.Text, @"non.{0,20}(?:ricevut\w*|arrivat\w*|accredit\w*)|per (?:errore|sbaglio)");
                Add(item, generalRefund ? "INFORMAZIONE_GENERALE" : "RIMBORSO_TASSA", generalRefund || paid, generalRefund ? "Informazioni sulla procedura generale di rimborso, senza anomalia personale." : paid
                    ? $"Rimborso BST0/BST1/BST2 dello stesso AA: lordo {Money(record!.TaxRefundPaidAmount)}, mandati {record.TaxRefundMandates}."
                    : record == null ? "Dati del rimborso non disponibili per l'anno richiesto." : "Rimborso non pagato o mandato assente per l'anno richiesto.", part);
            }
            if (payment && generalSchedule)
                Add(item, "INFORMAZIONE_GENERALE", true, "Richiesta sul calendario generale dei pagamenti, senza anomalia personale.", part);
            if (payment && !generalSchedule)
            {
                PaymentOperationalData? data = record == null ? null : TicketDomainModuleRegistry.Payments.Extract(record);
                bool first = Match(context, @"prima rata|prima tranche|prima erogazione|\b1(?:a|°)? rata\b|first (?:installment|instalment|payment|tranche)|\bBSP[012]\b");
                bool balance = Match(item.Text + " " + item.Subject, @"\bsaldo\b|seconda (?:rata|tranche|parte|erogazione)|secondo pagamento|ultima rata|rata finale|(?:second|2nd) (?:installment|instalment|part|payment)|\b2(?:a|nd)? rata\b|\bbalance\b|(?:quando|ricev\w*).{0,30}\bresto\b|remaining (?:amount|part|installment)|remainder|(?:restante importo|importo restante|other half)");
                bool integration = Match(context, @"integrazion\w*|integrativ\w*");
                decimal amount = data == null ? 0m : integration && first && !balance ? record!.BsIntegrationFirstInstallmentOriginalAmount + record.BsIntegrationFirstInstallmentReissueAmount
                    : integration && !balance ? data.IntegrationAmount : first && !balance ? data.FirstInstallmentAmount : data.PaidAmount;
                string mandates = record == null ? "" : integration && !balance ? record.BsIntegrationMandates : first && !balance ? record.BsFirstInstallmentMandates : record.BsMandates;
                bool full = data != null && data.AssignedAmount > 0m && data.ResidualAmount <= 0.01m;
                if (full && record!.BsMandates.Length > 0) { amount = data!.PaidAmount; mandates = record.BsMandates; }
                bool resolved = record != null && !record.HasUnclassifiedBsPaymentCodes && amount > 0m && mandates.Length > 0 && (first && !balance || integration && !balance || full);
                Add(item, integration && first && !balance ? "INTEGRAZIONE_PRIMA_RATA" : integration && !balance ? "INTEGRAZIONE_BORSA" : first && !balance ? "PRIMA_RATA" : "PAGAMENTO_BORSA", resolved,
                    record == null ? "Dati di pagamento non disponibili per l'anno richiesto."
                    : $"Esito lordo {Money(record.BenefitAmount)}; pagato lordo BS {Money(record.BsPaidAmount)}; residuo {Money(Math.Max(0m, record.BenefitAmount - record.BsPaidAmount))}; componente richiesta {Money(amount)}; mandati {mandates}."
                        + (resolved ? " Richiesta soddisfatta anche in presenza di dichiarata mancata ricezione." : " Liquidazione della richiesta non dimostrata."), part);
            }

            int beforeBlocks = item.Checks.Count;
            if (permit)
            {
                bool notify = Match(part, @"ho (?:gia )?(?:caricato|inserito|allegato|inviato)|\b(?:uploaded|submitted)\b") &&
                    !block && !Match(part, @"verific\w*|valid\w*|conferm\w*|confirm|check|verify");
                var permitFamily = TicketClosureKnowledge.BlockFamilies.Single(f => f.Code == "PERMESSO");
                bool absent = TicketClosureKnowledge.Absent(record, permitFamily);
                Add(item, notify ? "PERMESSO_CARICATO" : "PERMESSO", !technical && (absent || notify), notify && !absent
                    ? "Comunicazione dell'avvenuto caricamento del permesso; nessuna lavorazione richiesta sul ticket."
                    : absent ? TicketClosureKnowledge.BlockEvidence(record, permitFamily)
                    : record == null ? "Domanda dello stesso anno non disponibile per verificare il blocco del permesso." : "Blocco del permesso ancora presente o anomalia tecnica.", part);
            }
            bool taxNotification = !refund && Match(part, @"tassa regionale") && Match(part, @"ricevuta|ho pagato|pagamento effettuato|allego|allegato");
            if ((block || taxNotification) && !technical)
            {
                var families = TicketClosureKnowledge.BlockFamilies.Where(f => Match(context, f.Pattern)).ToList();
                if (families.Count == 0) families = TicketClosureKnowledge.BlockFamilies.Where(f => Match(item.Subject + " " + item.Text, f.Pattern)).ToList();
                foreach (var group in families)
                {
                    if (group.Code == "PERMESSO" && permit) continue;
                    bool absent = TicketClosureKnowledge.Absent(record, group);
                    Add(item, "BLOCCO_" + group.Code, absent, TicketClosureKnowledge.BlockEvidence(record, group), part);
                }
                if (item.Checks.Count == beforeBlocks)
                    Add(item, "BLOCCHI_GENERICI", record?.HasBlockCodeData == true && !TicketClosureKnowledge.Codes(record).Any(), TicketClosureKnowledge.BlockEvidence(record, null), part);
            }

            bool known = domicile || career || withdrawal || ibanChange || refund || payment || permit || manualData || documentRequest || debt || amountDispute || meritUpdate || ((block || taxNotification) && !technical);
            if (technical && !known)
            {
                bool submission = Match(context, @"(?:non riesc\w*|impossibil\w*|non consente|cannot|unable).{0,35}(?:invi\w*|trasmett\w*|submit\w*).{0,35}(?:domanda|application)") && !item.Checks.Any(c => !c.Resolved);
                Add(item, "INVIO_DOMANDA", submission && record?.CompilationStatus >= 90,
                    submission && record?.CompilationStatus >= 90 ? "Domanda dello stesso anno ora trasmessa/completa." : "Operazione segnalata non dimostrata come effettuata.", part);
            }
            else if (!known && genericInfo && !anomaly && !TicketClosureKnowledge.PersonalAnomaly(item.Text) && !TicketClosureKnowledge.Technical(item.Text) && !item.Checks.Any(c => !c.Resolved) && Match(context, @"graduatori\w*|premio.{0,15}laurea|scorriment\w*|bando|requisiti|erasmus|mobilita|requirements|(?:quanti|quali).{0,30}(?:crediti|cfu)|crediti.{0,30}servono|(?:how many|which).{0,30}(?:credits?|exams?)"))
                Add(item, "INFORMAZIONE_GENERALE", true, "Richiesta informativa senza anomalia personale riconosciuta.", part);
        }

        private static void MarkDuplicates(List<Assessment> items)
        {
            var exact = new Dictionary<string, Assessment>(StringComparer.Ordinal);
            var semantic = new Dictionary<string, Assessment>(StringComparer.Ordinal);
            foreach (Assessment item in items)
            {
                string message = Regex.Replace(Normalize(Read(item.Source, "PRIMO_MSG_STUDENTE")), @"\s+", " ");
                string fiscalCode = Read(item.Source, "CODFISC").ToUpperInvariant();
                if (!item.Year.HasValue || fiscalCode.Length == 0 || message.Length == 0 || Read(item.Source, "ID_TICKET").Length == 0) continue;
                string scope = fiscalCode + ":" + item.Year.Value + ":";
                string exactKey = scope + message;
                // Stessa richiesta solo per ambiti senza parametri personali non confrontabili.
                bool canCompare = item.Problems.Count == 0 && item.Checks.Count > 0 && item.Checks.All(c =>
                    c.Code is "PAGAMENTO_BORSA" or "PRIMA_RATA" or "INTEGRAZIONE_BORSA" or "RIMBORSO_TASSA" or "PERMESSO" or
                    "PERMESSO_CARICATO" or "BLOCCO_ISCRIZIONE" or "BLOCCO_TASSA" or "BLOCCO_REDDITI" or "BLOCCO_PERMESSO") &&
                    !Match(message, @"rettific\w*|corregg\w*|nuovo|nuova|altr[oa]|invece|different|new document");
                string qualifiers = Regex.Replace(message, @"\b(?:20)?\d{2}\s*[/\-]\s*(?:20)?\d{2}\b", "");
                string numbers = string.Join(",", Regex.Matches(qualifiers, @"\b\d+(?:[.,]\d+)?\b").Select(m => m.Value).Distinct().OrderBy(v => v, StringComparer.Ordinal));
                string semanticKey = canCompare ? scope + string.Join("+", item.Checks.Select(c => c.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal)) + ":" + numbers : "";
                Assessment? original = exact.TryGetValue(exactKey, out var sameText) ? sameText
                    : semanticKey.Length > 0 && semantic.TryGetValue(semanticKey, out var sameRequest) ? sameRequest : null;
                if (original != null)
                {
                    item.DuplicateOf = Read(original.Source, "ID_TICKET");
                    item.Checks.Add(new Check("DUPLICATO", true, "Stesso studente e anno; riferimento al ticket più vecchio " + item.DuplicateOf + (sameText != null ? "; messaggio identico." : "; stessa richiesta riconosciuta.")));
                }
                Assessment principal = original ?? item;
                exact.TryAdd(exactKey, principal);
                if (semanticKey.Length > 0) semantic.TryAdd(semanticKey, principal);
            }
        }

        private static void Add(Assessment item, string code, bool resolved, string evidence, string part) =>
            item.Checks.Add(new Check(code, resolved, evidence + " Testo: " + Short(part)));

        private static bool IsRequest(string text) => Match(text,
            @"\b(?:chiedo|richiedo|vorrei|voglio|devo|potete|posso|potrei|volevo|desidero|richiesta|please|could|would|should|need|ask|request)\b|desiderer\w*|vorremm\w*|chieder\w*|comunicar\w*|informar\w*|e possibile|non (?:ho|riesco)|problema|errore|rettific\w*");
        private static bool Match(string text, string pattern) => TicketClosureKnowledge.Match(text, pattern);
        private static string Short(string text) => text.Length <= 180 ? text : text[..180] + "…";
        private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.GetCultureInfo("it-IT"));
        internal static string Read(DataRow row, string column) => row.Table.Columns.Contains(column) && row[column] != DBNull.Value ? Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
        private static long NumericId(string value) => long.TryParse(value, out long id) ? id : long.MaxValue;
        private static DateTime? ParseDate(string value) => DateTime.TryParse(value, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
        private static string Normalize(string value) => TicketClosureKnowledge.Normalize(value);
        private static bool MultipleYears(string message)
        {
            var years = new HashSet<int>();
            foreach (Match match in Regex.Matches(message, @"(?<![\d/\-])\b(?:20)?\d{2}\s*[/\-]\s*(?:20)?\d{2}\b(?!\s*[/\-]\s*\d)|\b20\d{2}20\d{2}\b"))
                if (TicketAcademicYearResolver.TryParseAcademicYear(match.Value, out int year) || TicketAcademicYearResolver.TryParseAcademicYear("a.a. " + match.Value, out year)) years.Add(year);
            return years.Count > 1;
        }

        private sealed record Check(string Code, bool Resolved, string Evidence);
        private sealed class Assessment
        {
            public Assessment(DataRow source, int? year, DateTime? created) { Source = source; Year = year; Created = created; }
            public DataRow Source { get; }
            public int? Year { get; }
            public DateTime? Created { get; }
            public TicketOfficeRecord? Record { get; set; }
            public List<Check> Checks { get; } = new();
            public List<string> Problems { get; } = new();
            public string DuplicateOf { get; set; } = "";
            public string Text { get; set; } = "";
            public string Subject { get; set; } = "";
            public bool CanClose => Year.HasValue && (DuplicateOf.Length > 0 || Problems.Count == 0 && Checks.Count > 0 && Checks.All(c => c.Resolved));
        }
    }
}
