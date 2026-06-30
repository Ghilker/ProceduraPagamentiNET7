using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    /// <summary>
    /// Costruisce la coda operativa dopo il riepilogo originario.
    /// La coda mette in testa i ticket per cui i dati attestano che la situazione
    /// richiesta dallo studente è già stata risolta.
    /// </summary>
    internal static class TicketOperationalWorkbook
    {
        private const string CloseResolved = "CHIUDIBILE_SITUAZIONE_GIA_RISOLTA";
        private const string CloseStandard = "CHIUDIBILE_CON_RISPOSTA_STANDARD";
        private const string ClosePersonalized = "FUORI_PERIMETRO_RISCONTRO_ASSISTITO";
        private const string ActionRequired = "NON_CHIUDIBILE_AZIONE_OPERATIVA_NECESSARIA";
        private const string DataMissing = "NON_CHIUDIBILE_DOCUMENTO_O_DATO_MANCANTE";
        private const string Verify = "DA_VERIFICARE_DATI_INSUFFICIENTI";

        private static readonly CultureInfo ItalianCulture = CultureInfo.GetCultureInfo("it-IT");
        private static readonly Lazy<IReadOnlyDictionary<string, string>> ResponseTexts = new(() =>
            BuildRulesAndResponseCodes()
                .AsEnumerable()
                .ToDictionary(
                    row => ReadString(row, "CODICE_RISPOSTA"),
                    row => ReadString(row, "RISCONTRO_BASE"),
                    StringComparer.OrdinalIgnoreCase));

        public static DataTable BuildOperationalQueue(TicketTopicSheetContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            DataTable result = CreateQueueTable();
            List<QueueItem> items = GetDistinctTicketRows(context.Tickets)
                .Select(item => BuildQueueItem(context, item.Row, item.Index))
                .OrderBy(item => item.PriorityOrder)
                .ThenByDescending(item => item.DaysOpen ?? -1)
                .ThenBy(item => item.TicketId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (QueueItem item in items)
            {
                DataRow row = result.NewRow();
                row["CHIUDIBILE"] = item.Closable;
                row["PRIORITA"] = item.Priority;
                row["PROSSIMO_CONTROLLO"] = item.NextCheck;
                row["GRUPPO_VERIFICA"] = item.VerificationGroup;
                row["AZIONE_RICHIESTA"] = item.RequiredAction;
                row["CODICE_RISPOSTA"] = item.ResponseCode;
                row["TESTO_RISCONTRO_BASE"] = item.ResponseText;
                row["EVIDENZE_CHIAVE"] = item.Evidence;
                row["MOTIVO_NON_CHIUSURA"] = item.NonClosureReason;
                row["FOGLIO_DETTAGLIO"] = item.DetailSheet;
                row["ID_TICKET"] = item.TicketId;
                row["ETA_TICKET_GIORNI"] = item.DaysOpen.HasValue
                    ? (object)item.DaysOpen.Value
                    : DBNull.Value;
                row["DATA_APERTURA"] = item.CreationDate.HasValue
                    ? (object)item.CreationDate.Value
                    : DBNull.Value;
                row["DATA_ULTIMO_MESSAGGIO"] = item.LastMessageDate.HasValue
                    ? (object)item.LastMessageDate.Value
                    : DBNull.Value;
                row["ARGOMENTO"] = item.Topic;
                row["OGGETTO"] = item.Subject;
                row["PRIMO_MSG_STUDENTE"] = item.StudentMessage;
                row["STATO_CHIUDIBILITA"] = item.ClosureStatus;
                row["OGGETTO_RICHIESTO"] = item.RequestedObject;
                row["AA_COERENZA"] = item.AcademicYearConsistency;
                row["AMBITI_RICHIESTI"] = item.RequestedScopes;
                row["ESITO_CONTROLLI_CHIUSURA"] = item.ClosureChecks;
                row["CONDIZIONE_RISOLTA"] = item.ResolvedCondition;
                row["MOTIVO_PRIORITA"] = item.PriorityReason;
                row["DECISIONE_PROPOSTA"] = item.Decision;
                row["MOTIVO_DECISIONE"] = item.Reason;
                row["TICKET_RIGUARDA_BLOCCHI"] = item.TicketMentionsBlocks ? "SI" : "NO";
                row["STUDENTE_HA_BLOCCHI"] = item.StudentHasBlocks ? "SI" : "NO";
                row["BLOCCHI_RILEVANTI"] = item.RelevantBlocks;
                row["BLOCCHI_NON_PERTINENTI"] = item.NonRelevantBlocks;
                row["CONTRADDIZIONI_RILEVATE"] = item.Contradictions;
                row["ARGOMENTI_SECONDARI"] = item.SecondaryTopics;
                row["INTENTO_RICHIESTA"] = item.Intent;
                row["SEGNALI_INTENTO"] = item.IntentSignals;
                row["ANNO_ACCADEMICO_RICHIESTA"] = item.RequestedAcademicYear;
                row["CATEGORIA_DOMANDE_STUDENTE"] = item.StudentApplicationCategory;
                row["ANNI_DOMANDE_RILEVATI"] = item.ParticipatedAcademicYears;
                row["AA_RECORD_OPERATIVO"] = item.OperationalAcademicYear;
                row["CRITERIO_SELEZIONE_RECORD"] = item.RecordSelectionCriterion;
                row["STATO_DOMANDA_INTERPRETATO"] = item.ApplicationState;
                row["CONFIDENZA_ARGOMENTO"] = item.TopicConfidence;
                row["CONFIDENZA_INTENTO"] = item.IntentConfidence;
                row["CONFIDENZA_DATI_OPERATIVI"] = item.OperationalDataConfidence;
                row["CONFIDENZA_DECISIONE"] = item.DecisionConfidence;
                row["CODFISC"] = item.FiscalCode;
                row["CODSTUD"] = item.StudentCode;
                row["STATO_LAVORAZIONE"] = "DA_LAVORARE";
                row["DECISIONE_OPERATORE"] = string.Empty;
                row["ASSEGNATARIO"] = string.Empty;
                row["NOTA_OPERATORE"] = string.Empty;
                row["DATA_LAVORAZIONE"] = DBNull.Value;
                row["ESITO_INVIATO"] = string.Empty;
                result.Rows.Add(row);
            }

            Logger.LogInfo(null, $"Coda operativa costruita. Ticket univoci: {result.Rows.Count}");
            return result;
        }

        public static DataTable BuildDashboard(DataTable queue)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            var result = new DataTable();
            result.Columns.Add("SEZIONE", typeof(string));
            result.Columns.Add("INDICATORE", typeof(string));
            result.Columns.Add("VALORE", typeof(int));

            AddMetric(result, "Generale", "Ticket totali", queue.Rows.Count);
            AddMetric(result, "Chiusura", "Chiudibili - situazione già risolta", CountByPrefix(queue, "DECISIONE_PROPOSTA", CloseResolved));
            AddMetric(result, "Decisione", "Chiudibili con risposta standard", CountByPrefix(queue, "DECISIONE_PROPOSTA", CloseStandard));
            AddMetric(result, "Decisione", "Riscontro assistito fuori perimetro", CountByPrefix(queue, "DECISIONE_PROPOSTA", ClosePersonalized));
            AddMetric(result, "Decisione", "Non chiudibili - azione operativa", CountByPrefix(queue, "DECISIONE_PROPOSTA", ActionRequired));
            AddMetric(result, "Decisione", "Non chiudibili - documento o dato mancante", CountByPrefix(queue, "DECISIONE_PROPOSTA", DataMissing));
            AddMetric(result, "Decisione", "Da verificare", CountByPrefix(queue, "DECISIONE_PROPOSTA", Verify));
            AddMetric(result, "Priorità chiusura", "P1 - chiusura proposta", CountExact(queue, "PRIORITA", "P1"));
            AddMetric(result, "Priorità chiusura", "P2 - controllo rapido", CountExact(queue, "PRIORITA", "P2"));
            AddMetric(result, "Priorità chiusura", "P3 - attività necessaria", CountExact(queue, "PRIORITA", "P3"));
            AddMetric(result, "Priorità chiusura", "P4 - verifica manuale", CountExact(queue, "PRIORITA", "P4"));
            AddMetric(result, "Ambiti richiesti", "Erogazione borsa", CountContains(queue, "AMBITI_RICHIESTI", "EROGAZIONE_BORSA"));
            AddMetric(result, "Ambiti richiesti", "Permesso di soggiorno", CountContains(queue, "AMBITI_RICHIESTI", "PERMESSO_SOGGIORNO"));
            AddMetric(result, "Ambiti richiesti", "Blocchi pratica", CountContains(queue, "AMBITI_RICHIESTI", "BLOCCHI_PRATICA"));
            AddMetric(result, "Ambiti richiesti", "Aggiornamento IBAN", CountContains(queue, "AMBITI_RICHIESTI", "AGGIORNAMENTO_IBAN"));
            AddMetric(result, "Ambiti richiesti", "Finestra compilazione domanda", CountContains(queue, "AMBITI_RICHIESTI", "FINESTRA_COMPILAZIONE_DOMANDA"));
            AddMetric(result, "Ambiti richiesti", "Domicilio/contratto", CountContains(queue, "AMBITI_RICHIESTI", "DOMICILIO_CONTRATTO"));
            AddMetric(result, "Situazioni risolte", "Blocco non più presente", CountExact(queue, "CONDIZIONE_RISOLTA", "BLOCCO_AMMINISTRATIVO_NON_PIU_PRESENTE_STESSO_AA"));
            AddMetric(result, "Situazioni risolte", "Pagamento borsa completato", CountPaymentResolved(queue));
            AddMetric(result, "Situazioni risolte", "Documenti permesso lavorati - status 05", CountExact(queue, "CONDIZIONE_RISOLTA", "DOCUMENTI_PERMESSO_LAVORATI_STATUS_05_STESSO_AA"));
            AddMetric(result, "Situazioni risolte", "Permesso lavorato e blocchi rimossi", CountExact(queue, "CONDIZIONE_RISOLTA", "PERMESSO_LAVORATO_E_BLOCCO_RIMOSSO_STESSO_AA"));
            AddMetric(result, "Situazioni risolte", "IBAN aggiornato dopo ticket", CountExact(queue, "CONDIZIONE_RISOLTA", "IBAN_AGGIORNATO_DOPO_TICKET"));
            AddMetric(result, "Situazioni risolte", "IBAN coincidente col ticket", CountExact(queue, "CONDIZIONE_RISOLTA", "IBAN_PRESENTE_NEL_TICKET_COINCIDE"));
            AddMetric(result, "Situazioni risolte", "Finestra domanda chiusa", CountExact(queue, "CONDIZIONE_RISOLTA", "FINESTRA_COMPILAZIONE_DOMANDA_CHIUSA"));
            AddMetric(result, "Situazioni risolte", "Domicilio/contratto valido", CountExact(queue, "CONDIZIONE_RISOLTA", "DOMICILIO_CONTRATTO_STATUS_B_VALIDO"));
            AddMetric(result, "Situazioni risolte", "Condizioni multiple tutte risolte", CountExact(queue, "CONDIZIONE_RISOLTA", "TUTTE_LE_CONDIZIONI_RICHIESTE_RISOLTE_STESSO_AA"));
            AddMetric(result, "Qualità", "Contraddizioni rilevate", CountNotEmpty(queue, "CONTRADDIZIONI_RILEVATE"));
            AddMetric(result, "Qualità", "Decisioni a confidenza bassa", CountExact(queue, "CONFIDENZA_DECISIONE", "BASSA"));
            foreach (IGrouping<string, DataRow> group in queue.AsEnumerable()
                .GroupBy(row => ReadString(row, "GRUPPO_VERIFICA"), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddMetric(result, "Gruppo verifica", group.Key, group.Count());
            }
            AddMetric(result, "Blocchi", "Ticket che riguardano blocchi", CountExact(queue, "TICKET_RIGUARDA_BLOCCHI", "SI"));
            AddMetric(result, "Blocchi", "Ticket con studente che ha blocchi", CountExact(queue, "STUDENTE_HA_BLOCCHI", "SI"));
            AddMetric(result, "Lavorazione", "Da lavorare", CountExact(queue, "STATO_LAVORAZIONE", "DA_LAVORARE"));
            AddMetric(result, "Lavorazione", "In lavorazione", CountExact(queue, "STATO_LAVORAZIONE", "IN_LAVORAZIONE"));
            AddMetric(result, "Lavorazione", "In attesa studente", CountExact(queue, "STATO_LAVORAZIONE", "IN_ATTESA_STUDENTE"));
            AddMetric(result, "Lavorazione", "In attesa ufficio", CountExact(queue, "STATO_LAVORAZIONE", "IN_ATTESA_UFFICIO"));
            AddMetric(result, "Lavorazione", "Chiusi", CountExact(queue, "STATO_LAVORAZIONE", "CHIUSO"));

            foreach (IGrouping<string, DataRow> group in queue.AsEnumerable()
                .GroupBy(row => ReadString(row, "CATEGORIA_DOMANDE_STUDENTE"), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                int students = group
                    .Select(row =>
                    {
                        string fiscalCode = ReadString(row, "CODFISC");
                        return string.IsNullOrWhiteSpace(fiscalCode)
                            ? ReadString(row, "ID_TICKET")
                            : fiscalCode;
                    })
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                AddMetric(
                    result,
                    "Studenti per categoria domanda",
                    string.IsNullOrWhiteSpace(group.Key) ? "Non classificabile" : group.Key,
                    students);
            }

            foreach (IGrouping<string, DataRow> group in queue.AsEnumerable()
                .GroupBy(row => ReadString(row, "ARGOMENTO"), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                AddMetric(result, "Argomento", string.IsNullOrWhiteSpace(group.Key) ? "Non classificato" : group.Key, group.Count());
            }

            return result;
        }

        public static DataTable BuildTicketsToVerify(DataTable queue)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));

            DataTable result = queue.Clone();
            foreach (DataRow row in queue.Rows)
            {
                string decision = ReadString(row, "DECISIONE_PROPOSTA");
                bool closable = string.Equals(ReadString(row, "CHIUDIBILE"), "SI", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(ReadString(row, "CHIUDIBILE"), "PROBABILE", StringComparison.OrdinalIgnoreCase);
                bool include = decision.StartsWith(Verify, StringComparison.OrdinalIgnoreCase) ||
                               (!closable &&
                                (!string.IsNullOrWhiteSpace(ReadString(row, "CONTRADDIZIONI_RILEVATE")) ||
                                 string.Equals(ReadString(row, "CONFIDENZA_DECISIONE"), "BASSA", StringComparison.OrdinalIgnoreCase)));
                if (include)
                    result.ImportRow(row);
            }

            return result;
        }

        public static DataTable BuildStudentCategorySummary(
            DataTable queue,
            string categoryLabel)
        {
            if (queue == null)
                throw new ArgumentNullException(nameof(queue));
            if (string.IsNullOrWhiteSpace(categoryLabel))
                throw new ArgumentException("La categoria domanda non è valida.", nameof(categoryLabel));

            DataTable result = CreateStudentCategorySummaryTable();
            IEnumerable<IGrouping<string, DataRow>> students = queue.AsEnumerable()
                .Where(row => string.Equals(
                    ReadString(row, "CATEGORIA_DOMANDE_STUDENTE"),
                    categoryLabel,
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(row =>
                {
                    string fiscalCode = ReadString(row, "CODFISC");
                    return string.IsNullOrWhiteSpace(fiscalCode)
                        ? $"__TICKET__{ReadString(row, "ID_TICKET")}" 
                        : fiscalCode;
                }, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, DataRow> student in students)
            {
                List<DataRow> tickets = student
                    .OrderBy(row => GetPriorityOrder(ReadString(row, "PRIORITA")))
                    .ThenByDescending(row => ReadNullableInt(row, "ETA_TICKET_GIORNI") ?? -1)
                    .ThenBy(row => ReadString(row, "ID_TICKET"), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                DataRow primaryTicket = tickets.First();

                DataRow row = result.NewRow();
                row["CATEGORIA_DOMANDE_STUDENTE"] = categoryLabel;
                row["CODFISC"] = ReadString(primaryTicket, "CODFISC");
                row["CODSTUD"] = ReadString(primaryTicket, "CODSTUD");
                row["ANNI_DOMANDE_RILEVATI"] = ReadString(primaryTicket, "ANNI_DOMANDE_RILEVATI");
                row["NUMERO_TICKET"] = tickets.Count;
                row["TICKET_P1"] = tickets.Count(ticket => string.Equals(ReadString(ticket, "PRIORITA"), "P1", StringComparison.OrdinalIgnoreCase));
                row["TICKET_CHIUDIBILI"] = tickets.Count(ticket =>
                    string.Equals(ReadString(ticket, "CHIUDIBILE"), "SI", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ReadString(ticket, "CHIUDIBILE"), "PROBABILE", StringComparison.OrdinalIgnoreCase));
                row["TICKET_DA_VERIFICARE"] = tickets.Count(ticket => ReadString(ticket, "DECISIONE_PROPOSTA").StartsWith(Verify, StringComparison.OrdinalIgnoreCase));
                row["TICKET_AZIONE_OPERATIVA"] = tickets.Count(ticket =>
                    ReadString(ticket, "DECISIONE_PROPOSTA").StartsWith(ActionRequired, StringComparison.OrdinalIgnoreCase) ||
                    ReadString(ticket, "DECISIONE_PROPOSTA").StartsWith(DataMissing, StringComparison.OrdinalIgnoreCase));
                row["ALMENO_UN_TICKET_RIGUARDA_BLOCCHI"] = tickets.Any(ticket =>
                    string.Equals(ReadString(ticket, "TICKET_RIGUARDA_BLOCCHI"), "SI", StringComparison.OrdinalIgnoreCase)) ? "SI" : "NO";
                row["STUDENTE_HA_BLOCCHI"] = tickets.Any(ticket =>
                    string.Equals(ReadString(ticket, "STUDENTE_HA_BLOCCHI"), "SI", StringComparison.OrdinalIgnoreCase)) ? "SI" : "NO";
                row["PRIORITA_MASSIMA"] = ReadString(primaryTicket, "PRIORITA");
                row["ID_TICKET_PRIORITARIO"] = ReadString(primaryTicket, "ID_TICKET");
                row["OGGETTO_TICKET_PRIORITARIO"] = ReadString(primaryTicket, "OGGETTO");
                row["DECISIONE_PROPOSTA_TICKET_PRIORITARIO"] = ReadString(primaryTicket, "DECISIONE_PROPOSTA");
                row["FOGLIO_DETTAGLIO"] = ReadString(primaryTicket, "FOGLIO_DETTAGLIO");
                DateTime? oldestCreationDate = tickets
                    .Select(ticket => ReadNullableDate(ticket, "DATA_APERTURA"))
                    .Where(date => date.HasValue)
                    .OrderBy(date => date.Value)
                    .FirstOrDefault();
                row["DATA_APERTURA_PIU_VECCHIA"] = oldestCreationDate.HasValue
                    ? (object)oldestCreationDate.Value
                    : DBNull.Value;
                result.Rows.Add(row);
            }

            return result;
        }

        public static DataTable BuildRulesAndResponseCodes()
        {
            var result = new DataTable();
            result.Columns.Add("CODICE_RISPOSTA", typeof(string));
            result.Columns.Add("CASO", typeof(string));
            result.Columns.Add("DECISIONE_PROPOSTA", typeof(string));
            result.Columns.Add("AZIONE_RICHIESTA", typeof(string));
            result.Columns.Add("RISCONTRO_BASE", typeof(string));

            AddRule(result, "GEN_01", "Nessuna domanda operativa", Verify,
                "Verificare codice fiscale, identità studente e presenza di una domanda.",
                "Non risultano dati operativi associabili con sufficiente certezza. È necessaria una verifica anagrafica e della domanda.");
            AddRule(result, "GEN_02", "Argomento classificato con confidenza insufficiente", Verify,
                "Leggere messaggio e oggetto; correggere l'argomento prima della chiusura.",
                "Il contenuto del ticket richiede una verifica manuale dell'argomento prima di predisporre il riscontro.");
            AddRule(result, "GEN_03", "Richiesta informativa o pre-domanda", ClosePersonalized,
                "Gestire nel flusso di riscontro assistito: non proporre la chiusura automatica.",
                "La richiesta è informativa. Può essere trattata con riscontro assistito, ma non è una situazione già risolta da chiudere automaticamente.");
            AddRule(result, "GEN_04", "Contestazione, rinuncia o caso non automatizzabile", Verify,
                "Confrontare il testo del ticket con i dati disponibili e applicare la procedura dedicata.",
                "La richiesta richiede una valutazione manuale; i dati operativi non consentono una chiusura automatica.");
            AddRule(result, "GEN_04A", "Contestazione o esito/importo contestato", Verify,
                "Gestire come contestazione: leggere il testo, confrontare dati ed esito e applicare la procedura dedicata.",
                "Il ticket contiene una contestazione. I dati operativi possono aiutare la verifica, ma non bastano per una chiusura automatica.");
            AddRule(result, "GEN_04B", "Richiesta non chiara o intento non univoco", Verify,
                "Rileggere il ticket e chiarire l'oggetto richiesto prima di decidere chiusura, risposta o lavorazione.",
                "Il testo non permette di capire con certezza quale problematica lo studente stia chiedendo di risolvere.");
            AddRule(result, "GEN_04C", "Richiesta informativa su ambito operativo", ClosePersonalized,
                "Predisporre un riscontro informativo: non usare i dati operativi come evidenza di chiusura automatica.",
                "Il ticket cita un ambito operativo, ma la richiesta è informativa o preventiva. Serve un riscontro, non una chiusura per situazione già risolta.");
            AddRule(result, "GEN_04D", "Rinuncia, recesso o cessazione", ActionRequired,
                "Applicare la procedura amministrativa di rinuncia, recesso o cessazione pertinente all'argomento.",
                "La richiesta richiede una lavorazione amministrativa dedicata e non può essere chiusa automaticamente.");
            AddRule(result, "GEN_04E", "Contenuto non compatibile con chiusura automatica", Verify,
                "Confrontare richiesta e dati disponibili: la condizione operativa non è sufficiente per proporre la chiusura.",
                "Il ticket non chiede semplicemente la verifica di una situazione già risolta; serve valutazione manuale.");
            AddRule(result, "GEN_05", "Contraddizione fra ticket e dati", Verify,
                "Verificare la discordanza indicata prima di predisporre o inviare il riscontro.",
                "Il testo del ticket e i dati operativi non sono pienamente coerenti. È necessaria una verifica puntuale.");
            AddRule(result, "GEN_06", "Anno accademico non identificato o non allineato", Verify,
                "Individuare nel ticket l'anno accademico e selezionare il record dello stesso anno prima di valutare la chiusura.",
                "La chiusura automatica richiede una corrispondenza esatta fra anno accademico del ticket e anno del record operativo.");
            AddRule(result, "GEN_07", "Più condizioni richieste, almeno una non risolta", ActionRequired,
                "Verificare separatamente tutti gli ambiti citati nel ticket e non chiudere finché ciascuno non risulta risolto.",
                "Il ticket contiene più richieste e almeno una condizione non risulta ancora risolta.");
            AddRule(result, "GEN_08", "Tutte le condizioni multiple risultano risolte", CloseResolved,
                "Confermare le evidenze dei singoli ambiti e chiudere il ticket.",
                "Tutti gli ambiti citati nel ticket risultano risolti. Il ticket può essere chiuso.");
            AddRule(result, "GEN_DOC_01", "Richiesta documento o attestazione", ActionRequired,
                "Gestire manualmente la produzione, il recupero o la verifica del documento richiesto.",
                "Il ticket richiede ricevute, attestazioni, certificati o documenti ufficiali: non è una chiusura automatica.");
            AddRule(result, "BLO_01", "Blocco pertinente all'argomento", ActionRequired,
                "Individuare e gestire la causa del blocco pertinente al ticket.",
                "La pratica presenta un blocco rilevante per l'argomento trattato. Il ticket non è chiudibile finché la causa non è verificata o risolta.");
            AddRule(result, "BLO_02", "Richiesta di rimozione blocco già risolta", CloseResolved,
                "Confermare che non risulti alcun blocco sulla domanda dello stesso anno e chiudere il ticket.",
                "Non risultano blocchi presenti sulla pratica per lo stesso anno accademico. La richiesta può essere chiusa.");
            AddRule(result, "DOM_01", "Domanda incompleta o stato sconosciuto", ActionRequired,
                "Verificare compilazione, trasmissione e stato effettivo della domanda.",
                "Lo stato disponibile della domanda non consente una chiusura automatica.");
            AddRule(result, "DOM_02", "Finestra compilazione domanda chiusa", CloseResolved,
                "Confermare l'anno accademico citato nel ticket e chiudere con riscontro sulla finestra ormai conclusa.",
                "La finestra di compilazione della domanda per l'anno accademico indicato risulta conclusa. Il ticket può essere chiuso con riscontro informativo standard.");
            AddRule(result, "DOM_03", "Finestra compilazione domanda attiva, futura o non riconosciuta", ActionRequired,
                "Verificare anno accademico e calendario; se la finestra è attiva o futura predisporre riscontro informativo.",
                "La richiesta riguarda la compilazione della domanda, ma la finestra non risulta chiusa o l'anno non è riconoscibile con certezza.");
            AddRule(result, "DOM_04", "Compilazione domanda superata", CloseResolved,
                "Confermare che la domanda risulti trasmessa/completa e senza blocchi pertinenti; poi chiudere il ticket.",
                "Il ticket riguardava come compilare o proseguire la domanda; la domanda risulta ora trasmessa/completa con esito idoneo/vincitore e senza blocchi.");
            AddRule(result, "PAG_01", "Pagamento completo della borsa", CloseResolved,
                "Confermare mandato, importo, anno accademico e assenza di storni; poi chiudere il ticket.",
                "Per lo stesso anno accademico indicato nel ticket, l'importo BS risulta interamente pagato e associato a mandati validi. Il ticket può essere chiuso.");
            AddRule(result, "PAG_02", "Prima rata della borsa pagata", CloseResolved,
                "Confermare mandato della prima rata, anno accademico e assenza di storni; poi chiudere il ticket.",
                "Per lo stesso anno accademico indicato nel ticket, la prima rata BS risulta pagata e associata a un mandato valido. Il ticket può essere chiuso.");
            AddRule(result, "PAG_03", "Saldo della borsa pagato", CloseResolved,
                "Confermare mandato del saldo, anno accademico e assenza di storni; poi chiudere il ticket.",
                "Per lo stesso anno accademico indicato nel ticket, il saldo BS risulta pagato e associato a un mandato valido. Il ticket può essere chiuso.");
            AddRule(result, "PAG_12", "Integrazione della borsa pagata", CloseResolved,
                "Confermare mandato dell'integrazione, anno accademico e assenza di storni; poi chiudere il ticket.",
                "Per lo stesso anno accademico indicato nel ticket, l'integrazione BS risulta pagata e associata a un mandato valido. Il ticket può essere chiuso.");
            AddRule(result, "PAG_15", "Pagamento completo della borsa non risolto", ActionRequired,
                "Verificare importo assegnato, pagamenti validi, residuo e mandati.",
                "I pagamenti della borsa citati nel ticket non risultano tutti completati. Il ticket non è chiudibile.");
            AddRule(result, "PAG_17", "Prima rata non riscontrata", ActionRequired,
                "Verificare la prima rata, i mandati, gli storni e l'anno accademico richiesto.",
                "Per l'anno accademico indicato nel ticket non risulta una prima rata BS valida con mandato. Il ticket non è chiudibile.");
            AddRule(result, "PAG_18", "Saldo non riscontrato", ActionRequired,
                "Verificare il saldo, i mandati, gli storni e l'anno accademico richiesto.",
                "Per l'anno accademico indicato nel ticket non risulta un saldo BS valido con mandato. Il ticket non è chiudibile.");
            AddRule(result, "PAG_19", "Integrazione non riscontrata", ActionRequired,
                "Verificare l'integrazione, i mandati, gli storni e l'anno accademico richiesto.",
                "Per l'anno accademico indicato nel ticket non risulta un'integrazione BS valida con mandato. Il ticket non è chiudibile.");
            AddRule(result, "PAG_16", "Classificazione pagamenti senza richiesta di erogazione", Verify,
                "Rileggere il ticket e distinguere erogazioni della borsa da tasse o pagamenti effettuati dallo studente.",
                "Il ticket è classificato nell'area pagamenti, ma il testo non contiene una richiesta di erogazione della borsa.");
            AddRule(result, "PAG_04", "IBAN assente", DataMissing,
                "Richiedere o verificare l'IBAN e la modalità di pagamento.",
                "Per procedere è necessario verificare o aggiornare l'IBAN associato alla domanda.");
            AddRule(result, "PAG_05", "Vincitore senza pagamenti", ActionRequired,
                "Verificare iter contabile, impegni e mandati.",
                "La domanda risulta vincitrice ma non emergono pagamenti BS. È necessaria una verifica contabile.");
            AddRule(result, "PAG_06", "Pagamento parziale", ActionRequired,
                "Verificare rate, residuo, reversali e detrazioni.",
                "Dai dati disponibili risulta un pagamento BS parziale. Occorre verificare il residuo e l'iter della rata successiva.");
            AddRule(result, "PAG_07", "Studente escluso", ClosePersonalized,
                "Predisporre riscontro con l'esito disponibile.",
                "La domanda risulta esclusa. Predisporre un riscontro coerente con l'esito e le informazioni disponibili.");
            AddRule(result, "PAG_08", "Dati contabili non conclusivi", Verify,
                "Confrontare richiesta, esito, importi, pagamenti e mandati disponibili.",
                "I dati contabili disponibili richiedono una verifica puntuale prima di predisporre il riscontro.");
            AddRule(result, "PAG_09", "Sollecito con pagamento già disposto", Verify,
                "Verificare data del mandato, canale di accredito e causale prima della chiusura.",
                "Risultano un pagamento BS e un mandato, ma il ticket segnala una mancata ricezione. È necessaria una verifica puntuale.");
            AddRule(result, "PAG_10", "Pagamento BS stornato", ActionRequired,
                "Verificare causa dello storno ed eventuale riemissione del pagamento.",
                "Il movimento risulta ritirato dall'azienda: è stornato e non deve essere considerato accreditato allo studente.");
            AddRule(result, "PAG_11", "Codice pagamento BS non classificato", Verify,
                "Verificare il significato del codice BS prima di calcolare importi, residui o chiusure.",
                "Sono presenti codici BS non inclusi nella mappa dei pagamenti. I relativi importi non sono inclusi nelle somme automatiche.");
            AddRule(result, "BEN_01", "Esito o importo beneficio disponibile", ClosePersonalized,
                "Confrontare testo del ticket con esito e importi estratti.",
                "Gli esiti e gli importi risultano disponibili. Predisporre un riscontro aderente alla richiesta dello studente.");
            AddRule(result, "BEN_02", "Importo esito diverso dalle specifiche", ActionRequired,
                "Verificare coerenza tra esito e impegni contabili.",
                "Risulta una differenza tra l'importo dell'esito e le specifiche degli impegni. È necessaria una verifica contabile.");
            AddRule(result, "ALL_01", "Istanza domicilio aperta", ActionRequired,
                "Verificare lavorazione dell'istanza di domicilio.",
                "L'istanza di domicilio risulta aperta. Il ticket richiede verifica prima della chiusura.");
            AddRule(result, "ALL_02", "Contratto domicilio non presente", DataMissing,
                "Verificare presenza, validità o integrazione del contratto.",
                "Non risulta un contratto di domicilio associato alla domanda. Occorre verificare la documentazione.");
            AddRule(result, "ALL_03", "Esclusione alloggio", ClosePersonalized,
                "Predisporre riscontro con esito alloggio disponibile.",
                "La domanda risulta esclusa dal posto alloggio. Predisporre un riscontro coerente con l'esito.");
            AddRule(result, "ALL_04", "Dati alloggio disponibili", ClosePersonalized,
                "Confrontare richiesta, esito alloggio e dati di domicilio.",
                "I dati di alloggio e domicilio risultano disponibili. Predisporre un riscontro aderente alla richiesta.");
            AddRule(result, "ALL_05", "Rinuncia o check-out", ActionRequired,
                "Applicare la procedura di rinuncia, cessazione o check-out.",
                "La richiesta riguarda una rinuncia o un check-out e richiede una lavorazione amministrativa dedicata.");
            AddRule(result, "ALL_06", "Istanza di domicilio già lavorata", CloseResolved,
                "Confermare istanza/contratto e status sede B; poi chiudere il ticket.",
                "La richiesta su domicilio o contratto risulta già risolta dai dati disponibili: istanza presente/lavorata oppure contratto valido e status sede B.");
            AddRule(result, "ALL_07", "Domicilio o contratto non ancora risolto", ActionRequired,
                "Verificare status sede B, istanza domicilio e copertura del contratto.",
                "I dati disponibili non dimostrano ancora status fuori sede B o copertura sufficiente del contratto.");
            AddRule(result, "MEN_01", "Monetizzazione mensa", ClosePersonalized,
                "Confrontare richiesta con monetizzazione e pagamenti BS disponibili.",
                "La monetizzazione mensa risulta concessa. Verificare l'importo e l'eventuale pagamento prima della chiusura.");
            AddRule(result, "MEN_02", "Monetizzazione mensa non concessa", ClosePersonalized,
                "Predisporre riscontro sulla monetizzazione non concessa.",
                "La monetizzazione mensa non risulta concessa per la domanda selezionata.");
            AddRule(result, "DOC_01", "Documenti permesso non trovati", DataMissing,
                "Verificare allegati e richiedere eventuale integrazione.",
                "Non risultano documenti del permesso di soggiorno nei dati disponibili. È necessaria una verifica documentale.");
            AddRule(result, "DOC_02", "ISEE non valorizzato", DataMissing,
                "Verificare DSU, acquisizione ISEE e possibili integrazioni.",
                "L'ISEE DSU non risulta valorizzato. È necessaria una verifica dei dati economici.");
            AddRule(result, "DOC_03", "Documenti e dati economici disponibili", ClosePersonalized,
                "Controllare stato dei documenti e dati economici dichiarati.",
                "I documenti e i dati economici risultano disponibili. Predisporre un riscontro aderente alla richiesta.");
            AddRule(result, "DOC_04", "Documenti permesso lavorati con status 05", CloseResolved,
                "Confermare lo stato 05 dei documenti richiesti e chiudere il ticket.",
                "I documenti del permesso di soggiorno risultano inseriti e lavorati con status 05. Il ticket può essere chiuso.");
            AddRule(result, "DOC_05", "Documenti permesso non tutti lavorati con status 05", ActionRequired,
                "Verificare i documenti 01, 02 e 03 e attendere che risultino tutti in status 05.",
                "I documenti del permesso di soggiorno non risultano tutti lavorati con status 05. Il ticket non è chiudibile.");
            AddRule(result, "DOC_BLO_01", "Permesso lavorato e blocchi rimossi", CloseResolved,
                "Confermare documenti in status 05 e assenza di blocchi, quindi chiudere il ticket.",
                "I documenti del permesso risultano lavorati e non risultano blocchi presenti. Il ticket può essere chiuso.");
            AddRule(result, "DOC_BLO_02", "Permesso lavorato ma blocco BPP presente", ActionRequired,
                "Rimuovere o verificare il blocco BPP/permesso prima della chiusura.",
                "I documenti del permesso risultano lavorati, ma è ancora presente un blocco legato al permesso di soggiorno.");
            AddRule(result, "DOC_RED_01", "Redditi esteri senza blocco", CloseResolved,
                "Confermare assenza del blocco BDR/redditi esteri e chiudere il ticket.",
                "Il ticket riguarda redditi esteri o ISEE parificato; non risulta più il blocco BDR/documentazione redditi esteri mancante.");
            AddRule(result, "IBAN_01", "IBAN presente", ClosePersonalized,
                "Verificare eventuali blocchi e mandati prima dell'invio.",
                "L'IBAN risulta presente. Predisporre un riscontro in base alla richiesta e ai mandati disponibili.");
            AddRule(result, "IBAN_02", "Aggiornamento IBAN richiesto", ActionRequired,
                "Verificare l'autorizzazione alla variazione e i dati bancari aggiornati.",
                "La richiesta riguarda una modifica delle coordinate di pagamento e richiede una lavorazione dedicata.");
            AddRule(result, "IBAN_03", "IBAN aggiornato dopo apertura ticket", CloseResolved,
                "Confermare data validità e validità formale dell'ultimo IBAN attivo; poi chiudere il ticket.",
                "L'ultimo IBAN attivo risulta valido e con data validità successiva o allineata all'apertura del ticket. La richiesta può essere chiusa.");
            AddRule(result, "IBAN_04", "IBAN attivo coincidente con quello indicato nel ticket", CloseResolved,
                "Confermare che l'IBAN riportato nel ticket coincida con l'ultimo IBAN attivo; poi chiudere il ticket.",
                "L'IBAN indicato nel ticket coincide con l'ultimo IBAN attivo registrato. La richiesta può essere chiusa.");
            AddRule(result, "IBAN_05", "Aggiornamento IBAN non dimostrato", ActionRequired,
                "Confrontare testo del ticket, IBAN attivo, validità formale e data validità prima di chiudere.",
                "Non risulta evidenza sufficiente che l'IBAN richiesto sia stato aggiornato o coincida con quello indicato nel ticket.");
            AddRule(result, "GRA_01", "Graduatoria definitiva disponibile", ClosePersonalized,
                "Confrontare beneficio richiesto, esito e graduatoria definitiva.",
                "La graduatoria definitiva risulta disponibile. Predisporre il riscontro con l'esito pertinente.");
            AddRule(result, "GRA_02", "Graduatoria definitiva non disponibile", ActionRequired,
                "Verificare pubblicazione, caricamento o stato del procedimento.",
                "Non risulta una graduatoria definitiva nei dati disponibili. È necessaria una verifica dell'iter.");
            AddRule(result, "MOB_01", "Contributo mobilità non richiesto", ClosePersonalized,
                "Confrontare richiesta dello studente con i benefici registrati.",
                "Il contributo di mobilità non risulta tra i benefici richiesti per la domanda selezionata.");
            AddRule(result, "MOB_02", "Contributo mobilità da verificare", Verify,
                "Verificare richiesta, esito CI e documentazione collegata.",
                "I dati disponibili non consentono di chiudere automaticamente la verifica del contributo mobilità.");
            AddRule(result, "MOB_03", "Contributo mobilità con dati disponibili", ClosePersonalized,
                "Confrontare esito e importo CI con quanto dichiarato nel ticket.",
                "Sono disponibili dati sul contributo mobilità richiesto. Predisporre un riscontro aderente alla richiesta.");
            AddRule(result, "POR_01", "Portale o accesso", Verify,
                "Verificare il problema tecnico descritto e lo stato della domanda.",
                "Il problema di accesso richiede verifica del messaggio e, se necessario, intervento tecnico o istruzioni mirate.");
            AddRule(result, "POR_02", "Problema compilazione superato", CloseResolved,
                "Confermare che la domanda risulti trasmessa o completa; poi chiudere il ticket.",
                "Il ticket segnalava un problema di compilazione/trasmissione, ma la domanda risulta ora trasmessa o completa.");
            AddRule(result, "CAR_01", "Carriera o iscrizione", Verify,
                "Verificare carriera, anno di corso, crediti e precedenti partecipazioni.",
                "La richiesta richiede una verifica puntuale della carriera e non è chiudibile automaticamente.");
            AddRule(result, "CAR_02", "Cambio corso/sede o passaggio", CloseResolved,
                "Rispondere che lo studente deve aprire l'istanza dedicata; poi chiudere il ticket.",
                "La richiesta riguarda cambio corso, sede, passaggio, trasferimento o abbreviazione: lo studente deve aprire l'istanza dedicata.");
            AddRule(result, "ALT_01", "Argomento non classificato", Verify,
                "Leggere il ticket e assegnare l'ufficio o argomento corretto.",
                "Il ticket non è classificato in modo affidabile. È necessaria una valutazione manuale.");

            return result;
        }

        public static void EnrichTopicSheet(DataTable topicData, DataTable queue)
        {
            if (topicData == null)
                throw new ArgumentNullException(nameof(topicData));
            if (queue == null || !topicData.Columns.Contains("ID_TICKET"))
                return;

            string[] columns =
            {
                "CHIUDIBILE",
                "PRIORITA",
                "PROSSIMO_CONTROLLO",
                "GRUPPO_VERIFICA",
                "AZIONE_RICHIESTA",
                "CODICE_RISPOSTA",
                "EVIDENZE_CHIAVE",
                "STATO_CHIUDIBILITA",
                "OGGETTO_RICHIESTO",
                "AA_COERENZA",
                "MOTIVO_NON_CHIUSURA",
                "AMBITI_RICHIESTI",
                "ESITO_CONTROLLI_CHIUSURA",
                "CONDIZIONE_RISOLTA",
                "DECISIONE_PROPOSTA",
                "TICKET_RIGUARDA_BLOCCHI",
                "STUDENTE_HA_BLOCCHI",
                "INTENTO_RICHIESTA",
                "CONFIDENZA_DECISIONE",
                "CONTRADDIZIONI_RILEVATE",
                "BLOCCHI_RILEVANTI"
            };

            foreach (string column in columns)
            {
                if (!topicData.Columns.Contains(column))
                    topicData.Columns.Add(column, typeof(string));
            }

            var queueById = queue.AsEnumerable()
                .Where(row => !string.IsNullOrWhiteSpace(ReadString(row, "ID_TICKET")))
                .GroupBy(row => ReadString(row, "ID_TICKET"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (DataRow row in topicData.Rows)
            {
                string ticketId = ReadString(row, "ID_TICKET");
                if (string.IsNullOrWhiteSpace(ticketId) || !queueById.TryGetValue(ticketId, out DataRow? queueRow))
                    continue;

                foreach (string column in columns)
                    row[column] = ReadString(queueRow, column);
            }
        }

        private static QueueItem BuildQueueItem(TicketTopicSheetContext context, DataRow source, int sourceIndex)
        {
            string fiscalCode = ReadString(source, "CODFISC");
            string topicCode = ReadString(source, "ARGOMENTO_PRIMARIO");
            string secondaryTopicCode = ReadString(source, "ARGOMENTO_SECONDARIO");
            string ticketId = ReadString(source, "ID_TICKET");
            string subject = ReadString(source, "OGGETTO");
            string studentMessage = ReadString(source, "PRIMO_MSG_STUDENTE");
            TicketRecordSelection selection = context.ResolveOperationalRecord(
                fiscalCode,
                ReadString(source, "AA_DICHIARATO_TICKET"),
                studentMessage);
            string requestedAcademicYear = selection.RequestedAcademicYear.HasValue
                ? FormatAcademicYear(selection.RequestedAcademicYear.Value)
                : string.Empty;
            TicketOfficeRecord? record = selection.Record;
            TicketIntentAssessment intent = TicketOperationalAnalysis.AnalyzeIntent(
                subject,
                studentMessage,
                topicCode);
            TicketResolutionScope resolutionScope = TicketOperationalAnalysis.AnalyzeResolutionScope(
                subject,
                studentMessage,
                topicCode,
                secondaryTopicCode,
                IsYes(source, "RIFERITO_A_BLOCCHI"),
                intent.Intent);
            bool ticketMentionsBlocks = resolutionScope.ApplicationBlocks;
            bool studentHasBlocks = record != null && !string.IsNullOrWhiteSpace(record.Blocks);
            PaymentRequestKind paymentRequest = TicketOperationalAnalysis.ClassifyPaymentRequest(
                subject,
                studentMessage,
                secondaryTopicCode);
            ApplicationState applicationState = record == null
                ? ApplicationState.SCONOSCIUTO
                : TicketOperationalAnalysis.ClassifyApplicationState(record.CompilationStatus);
            BlockAssessment blockAssessment = TicketOperationalAnalysis.AnalyzeBlocks(record?.Blocks ?? string.Empty, topicCode);
            IReadOnlyList<string> contradictions = TicketOperationalAnalysis.FindContradictions(intent, record, topicCode);
            DateTime? creationDate = ReadDate(source, "DATA_CREAZIONE", "DATA_APERTURA");
            DateTime? lastMessageDate = ReadDate(source, "DATA_ULTIMO_MESSAGGIO", "DATA_ULTIMO_MSG", "DATA_ULTIMA_MODIFICA");
            int? daysOpen = creationDate.HasValue ? Math.Max(0, (DateTime.Today - creationDate.Value.Date).Days) : null;

            bool textHasMultipleAcademicYears = TicketOperationalAnalysis.HasMultipleAcademicYears(subject, studentMessage);
            bool singleOperationalYear =
                !selection.HasRequestedAcademicYear &&
                context.GetValidAcademicYears(fiscalCode).Count == 1;
            bool exactSingleAcademicYear =
                (selection.IsExactAcademicYear || singleOperationalYear) &&
                !textHasMultipleAcademicYears;
            ResolutionAssessment resolution = EvaluateResolvedCondition(
                record,
                intent,
                resolutionScope,
                exactSingleAcademicYear,
                paymentRequest,
                creationDate);
            OperationalDecision decision = EvaluateDecision(
                source,
                selection,
                topicCode,
                intent,
                applicationState,
                blockAssessment,
                contradictions,
                resolution,
                exactSingleAcademicYear);

            string priority = GetPriority(decision);
            string studentApplicationCategory = context.GetStudentApplicationCategoryLabel(fiscalCode);
            string participatedAcademicYears = context.GetParticipatedAcademicYears(fiscalCode);
            string operationalAcademicYear = record == null ? string.Empty : FormatAcademicYear(record.AcademicYear);
            string topicConfidence = GetTopicConfidence(source, topicCode);
            string operationalDataConfidence = GetOperationalDataConfidence(selection, applicationState);
            string decisionConfidence = GetDecisionConfidence(
                decision,
                topicConfidence,
                intent.Confidence,
                operationalDataConfidence,
                contradictions,
                blockAssessment,
                resolution);

            return new QueueItem
            {
                TicketId = string.IsNullOrWhiteSpace(ticketId) ? $"__ROW__{sourceIndex}" : ticketId,
                FiscalCode = fiscalCode,
                StudentCode = ReadString(source, "CODSTUD"),
                Subject = subject,
                StudentMessage = studentMessage,
                Topic = GetTopicLabel(topicCode),
                SecondaryTopics = secondaryTopicCode,
                Intent = TicketOperationalAnalysis.FormatIntent(intent.Intent),
                IntentSignals = JoinEvidence(
                    intent.Signals,
                    resolutionScope.ScholarshipPayments
                        ? TicketOperationalAnalysis.FormatPaymentRequestKind(paymentRequest)
                        : string.Empty),
                RequestedScopes = resolutionScope.Signals,
                ClosureChecks = resolution.CheckSummary,
                RequestedAcademicYear = requestedAcademicYear,
                StudentApplicationCategory = studentApplicationCategory,
                ParticipatedAcademicYears = participatedAcademicYears,
                OperationalAcademicYear = operationalAcademicYear,
                RecordSelectionCriterion = selection.Criterion,
                ApplicationState = TicketOperationalAnalysis.GetApplicationStateLabel(applicationState),
                TopicConfidence = topicConfidence,
                IntentConfidence = intent.Confidence,
                OperationalDataConfidence = operationalDataConfidence,
                DecisionConfidence = decisionConfidence,
                CreationDate = creationDate,
                LastMessageDate = lastMessageDate,
                DaysOpen = daysOpen,
                Closable = GetClosableLabel(decision),
                ClosureStatus = GetClosureStatus(decision, resolution, selection),
                RequestedObject = GetRequestedObject(resolutionScope, paymentRequest),
                AcademicYearConsistency = GetAcademicYearConsistency(selection),
                NonClosureReason = GetNonClosureReason(decision, resolution),
                NextCheck = GetNextCheck(decision, resolution),
                VerificationGroup = GetVerificationGroup(decision, resolution),
                ResolvedCondition = resolution.Condition,
                Priority = priority,
                PriorityOrder = GetPriorityOrder(priority),
                PriorityReason = GetPriorityReason(decision, resolution),
                Decision = decision.Decision,
                RequiredAction = decision.RequiredAction,
                Reason = decision.Reason,
                ResponseCode = decision.ResponseCode,
                ResponseText = decision.ResponseText,
                Evidence = JoinEvidence(
                    resolution.Evidence,
                    BuildEvidence(record, topicCode, operationalAcademicYear, resolutionScope)),
                TicketMentionsBlocks = ticketMentionsBlocks,
                StudentHasBlocks = studentHasBlocks,
                RelevantBlocks = blockAssessment.RelevantBlocks,
                NonRelevantBlocks = blockAssessment.NonRelevantBlocks,
                Contradictions = string.Join(" | ", contradictions),
                DetailSheet = GetDetailSheet(topicCode)
            };
        }

        private static ResolutionAssessment EvaluateResolvedCondition(
            TicketOfficeRecord? record,
            TicketIntentAssessment intent,
            TicketResolutionScope scope,
            bool recordSelectionIsReliable,
            PaymentRequestKind paymentRequest,
            DateTime? ticketCreationDate)
        {
            if (!scope.HasConditions)
                return ResolutionAssessment.None;

            if (scope.RequiresOperationalRecord && record == null)
            {
                return PendingResolution(
                    scope,
                    "DATI OPERATIVI: NON DISPONIBILI",
                    "GEN_01",
                    "Verificare codice fiscale, identità studente e domanda prima di valutare le condizioni richieste.");
            }

            // Nessun fallback (anno corrente o ultima domanda) è ammesso per una chiusura automatica.
            if (scope.RequiresOperationalRecord && !recordSelectionIsReliable)
            {
                return PendingResolution(
                    scope,
                    "ANNO ACCADEMICO: NON IDENTIFICATO, NON ALLINEATO O MULTIPLO",
                    "GEN_06",
                    "Non chiudere automaticamente: il ticket deve riferirsi a un solo anno accademico, coincidente con il record selezionato.");
            }

            if (!IsResolutionCompatible(intent.Intent, scope))
            {
                return PendingResolution(
                    scope,
                    "CONTENUTO: RICHIESTA NON COMPATIBILE CON CHIUSURA AUTOMATICA",
                    GetIncompatibleResolutionCode(intent.Intent),
                    "La richiesta è informativa, tecnica, contestata o comunque non chiudibile automaticamente.");
            }

            // Pagamenti, Permesso e Blocchi validano autonomamente la condizione richiesta.
            // Qui rimane solo l'orchestrazione delle condizioni multiple sullo stesso ticket.
            IReadOnlyList<TicketDomainValidationResult> validations =
                TicketDomainModuleRegistry.ValidateRequestedScopes(scope, record, paymentRequest, ticketCreationDate);
            if (validations.Count == 0)
            {
                return PendingResolution(
                    scope,
                    "CONTROLLI MODULARI: NESSUN MODULO ATTIVO",
                    "GEN_08",
                    "Verificare manualmente l'ambito richiesto: non è disponibile un modulo di validazione.");
            }

            var checks = validations
                .Select(result => result.CheckSummary)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var unresolved = validations
                .Where(result => !result.IsResolved)
                .Select(result => result.UnresolvedReason)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var evidence = validations
                .Where(result => result.IsResolved)
                .Select(result => result.Evidence)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            string checkSummary = string.Join(" | ", checks);
            if (IsPermitBlockPaymentRequest(scope) &&
                record != null &&
                TicketDomainModuleRegistry.Payments.IsFullScholarshipPaid(record))
            {
                PaymentOperationalData paymentData = TicketDomainModuleRegistry.Payments.Extract(record);
                evidence.Add($"pagamenti BS completi per l'anno accademico: pagato {FormatAmount(paymentData.PaidAmount)}, residuo {FormatAmount(paymentData.ResidualAmount)}");

                return new ResolutionAssessment
                {
                    HasRequestedConditions = true,
                    IsResolved = true,
                    RequestedScopes = scope.Signals,
                    CheckSummary = JoinEvidence(checkSummary, "PAGAMENTO COMPLETO: RISOLTO - tutti i pagamenti BS risultano effettuati"),
                    Condition = "PERMESSO_BLOCCO_O_PAGAMENTO_RISOLTO_STESSO_AA",
                    ResponseCode = "DOC_BLO_01",
                    RequiredAction = "Confermare assenza di blocco BPP/permesso oppure pagamento completo dello stesso anno; poi chiudere il ticket.",
                    Reason = "La richiesta permesso/blocco/pagamento risulta superata: per lo stesso anno accademico tutti i pagamenti BS risultano effettuati.",
                    Evidence = string.Join(" | ", evidence)
                };
            }

            if (unresolved.Count > 0)
            {
                string responseCode = validations.Count == 1
                    ? validations[0].PendingResponseCode
                    : "GEN_07";
                if (string.IsNullOrWhiteSpace(responseCode))
                    responseCode = "GEN_08";

                return new ResolutionAssessment
                {
                    HasRequestedConditions = true,
                    RequestedScopes = scope.Signals,
                    CheckSummary = checkSummary,
                    UnresolvedConditions = string.Join("; ", unresolved),
                    ResponseCode = responseCode,
                    RequiredAction = $"Non chiudere: {string.Join("; ", unresolved)}.",
                    Reason = "La condizione richiesta per lo stesso anno accademico non risulta soddisfatta.",
                    Evidence = string.Join(" | ", evidence)
                };
            }

            TicketDomainValidationResult? singleValidation = validations.Count == 1
                ? validations[0]
                : null;
            string condition = singleValidation?.ResolvedCondition ?? string.Empty;
            string resolvedResponseCode = singleValidation?.ResolvedResponseCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(condition))
                condition = GetResolvedCondition(scope, paymentRequest);
            if (string.IsNullOrWhiteSpace(resolvedResponseCode))
                resolvedResponseCode = GetResolvedResponseCode(scope, paymentRequest);

            return new ResolutionAssessment
            {
                HasRequestedConditions = true,
                IsResolved = true,
                RequestedScopes = scope.Signals,
                CheckSummary = checkSummary,
                Condition = condition,
                ResponseCode = resolvedResponseCode,
                RequiredAction = "Confermare l'evidenza riferita allo stesso anno accademico e chiudere il ticket.",
                Reason = "La condizione specifica richiesta dal ticket risulta già soddisfatta sul record dello stesso anno accademico.",
                Evidence = string.Join(" | ", evidence)
            };
        }

        // Solo ticket che chiedono esplicitamente uno stato, un sollecito o il riscontro
        // di documenti possono essere chiusi sulla base di evidenze operative. Le richieste
        // PRE_DOMANDA e INFORMAZIONE richiedono un riscontro sul quesito, non una chiusura
        // determinata da dati amministrativi preesistenti.
        private static bool IsResolutionCompatible(TicketIntent intent, TicketResolutionScope scope)
        {
            if (TicketOperationalAnalysis.IsClosureCompatible(intent))
                return true;

            // Per i pagamenti il modulo dedicato riconosce già richieste esplicite
            // come "verifica erogazione", "accredito non ricevuto" o "pagamento risulta".
            // Se l'intento trasversale resta generico, non scartiamo prima del controllo
            // contabile; contestazioni, info, pre-domande e problemi tecnici restano esclusi.
            if (scope.ScholarshipPayments &&
                intent == TicketIntent.ALTRO_DA_VERIFICARE)
            {
                return true;
            }

            if (scope.IbanUpdates &&
                (intent == TicketIntent.AGGIORNAMENTO_DATI ||
                 intent == TicketIntent.INVIO_DOCUMENTO ||
                 intent == TicketIntent.STATO_PRATICA ||
                 intent == TicketIntent.ALTRO_DA_VERIFICARE))
            {
                return true;
            }

            if (scope.DomicileContract &&
                (intent == TicketIntent.INVIO_DOCUMENTO ||
                 intent == TicketIntent.STATO_PRATICA ||
                 intent == TicketIntent.SOLLECITO ||
                 intent == TicketIntent.ALTRO_DA_VERIFICARE))
            {
                return true;
            }

            return scope.ApplicationWindowInformation &&
                   (intent == TicketIntent.PRE_DOMANDA ||
                    intent == TicketIntent.INFORMAZIONE ||
                    intent == TicketIntent.ALTRO_DA_VERIFICARE);
        }

        private static string GetIncompatibleResolutionCode(TicketIntent intent) => intent switch
        {
            TicketIntent.CONTESTAZIONE => "GEN_04A",
            TicketIntent.INFORMAZIONE or TicketIntent.PRE_DOMANDA => "GEN_04C",
            TicketIntent.RINUNCIA_O_RECESSO => "GEN_04D",
            TicketIntent.ERRORE_TECNICO => "POR_01",
            TicketIntent.ALTRO_DA_VERIFICARE => "GEN_04B",
            _ => "GEN_04E"
        };

        private static ResolutionAssessment PendingResolution(
            TicketResolutionScope scope,
            string checkSummary,
            string responseCode,
            string action) => new()
        {
            HasRequestedConditions = true,
            RequestedScopes = scope.Signals,
            CheckSummary = checkSummary,
            ResponseCode = responseCode,
            RequiredAction = action,
            Reason = "Le condizioni richieste dal ticket non possono essere considerate risolte automaticamente."
        };

        private static string GetResolvedCondition(
            TicketResolutionScope scope,
            PaymentRequestKind paymentRequest)
        {
            if (scope.IbanUpdates &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.ApplicationWindowInformation &&
                !scope.DomicileContract)
            {
                return "IBAN_AGGIORNATO_O_COINCIDENTE";
            }
            if (scope.ApplicationWindowInformation &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.IbanUpdates &&
                !scope.DomicileContract)
            {
                return "FINESTRA_COMPILAZIONE_DOMANDA_CHIUSA";
            }
            if (scope.DomicileContract &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.IbanUpdates &&
                !scope.ApplicationWindowInformation)
            {
                return "DOMICILIO_CONTRATTO_STATUS_B_VALIDO";
            }
            if (scope.ScholarshipPayments && !scope.ResidencePermitDocuments && !scope.ApplicationBlocks && !scope.DomicileContract)
            {
                return paymentRequest switch
                {
                    PaymentRequestKind.PRIMA_RATA => "PRIMA_RATA_BORSA_PAGATA",
                    PaymentRequestKind.SALDO => "SALDO_BORSA_PAGATO",
                    PaymentRequestKind.INTEGRAZIONE => "INTEGRAZIONE_BORSA_PAGATA",
                    _ => "PAGAMENTO_COMPLETO_STESSO_AA"
                };
            }
            if (!scope.ScholarshipPayments && scope.ResidencePermitDocuments && !scope.ApplicationBlocks)
                return "DOCUMENTI_PERMESSO_LAVORATI_STATUS_05_STESSO_AA";
            if (!scope.ScholarshipPayments && !scope.ResidencePermitDocuments && scope.ApplicationBlocks)
                return "BLOCCO_AMMINISTRATIVO_NON_PIU_PRESENTE_STESSO_AA";
            if (!scope.ScholarshipPayments && scope.ResidencePermitDocuments && scope.ApplicationBlocks)
                return "PERMESSO_LAVORATO_E_BLOCCO_RIMOSSO_STESSO_AA";
            return "TUTTE_LE_CONDIZIONI_RICHIESTE_RISOLTE_STESSO_AA";
        }

        private static string GetResolvedResponseCode(
            TicketResolutionScope scope,
            PaymentRequestKind paymentRequest)
        {
            if (scope.IbanUpdates &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.ApplicationWindowInformation &&
                !scope.DomicileContract)
            {
                return "IBAN_03";
            }
            if (scope.ApplicationWindowInformation &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.IbanUpdates &&
                !scope.DomicileContract)
            {
                return "DOM_02";
            }
            if (scope.DomicileContract &&
                !scope.ScholarshipPayments &&
                !scope.ResidencePermitDocuments &&
                !scope.ApplicationBlocks &&
                !scope.IbanUpdates &&
                !scope.ApplicationWindowInformation)
            {
                return "ALL_06";
            }
            if (scope.ScholarshipPayments && !scope.ResidencePermitDocuments && !scope.ApplicationBlocks && !scope.DomicileContract)
            {
                return paymentRequest switch
                {
                    PaymentRequestKind.PRIMA_RATA => "PAG_02",
                    PaymentRequestKind.SALDO => "PAG_03",
                    PaymentRequestKind.INTEGRAZIONE => "PAG_12",
                    _ => "PAG_01"
                };
            }
            if (!scope.ScholarshipPayments && scope.ResidencePermitDocuments && !scope.ApplicationBlocks)
                return "DOC_04";
            if (!scope.ScholarshipPayments && !scope.ResidencePermitDocuments && scope.ApplicationBlocks)
                return "BLO_02";
            if (!scope.ScholarshipPayments && scope.ResidencePermitDocuments && scope.ApplicationBlocks)
                return "DOC_BLO_01";
            return "GEN_08";
        }

        private static OperationalDecision EvaluateDecision(
            DataRow source,
            TicketRecordSelection selection,
            string topicCode,
            TicketIntentAssessment intent,
            ApplicationState applicationState,
            BlockAssessment blockAssessment,
            IReadOnlyList<string> contradictions,
            ResolutionAssessment resolution,
            bool recordSelectionIsReliable)
        {
            TicketOfficeRecord? record = selection.Record;

            // Normalmente una classificazione ampia da verificare blocca la chiusura.
            // Eccezione circoscritta: una richiesta esplicita di rimozione del blocco per
            // permesso, con AA coincidente, documenti 01/02/03 in status 05 e nessun blocco
            // residuo, prevale sul topic generico/errato assegnato dal keyword engine.
            bool verifiedPermitBlockRemoval = IsVerifiedPermitBlockRemoval(intent, resolution);
            bool verifiedPaymentResolution = IsVerifiedPaymentResolution(resolution);
            bool verifiedIbanResolution = IsVerifiedIbanResolution(resolution);
            bool verifiedApplicationWindowResolution = IsVerifiedApplicationWindowResolution(resolution);
            bool verifiedDomicileContractResolution = IsVerifiedDomicileContractResolution(resolution);
            bool verifiedPortalSubmissionResolution = IsPortalSubmissionProblemResolved(source, topicCode, applicationState);
            bool verifiedManualDocumentRequest = IsManualDocumentOrAttestationRequest(source);
            bool verifiedClosedApplicationCompilation = IsClosedApplicationCompilationQuestion(source, record, recordSelectionIsReliable);
            bool verifiedSectionCompilationResolution = IsSectionCompilationResolved(source, topicCode, record, applicationState);
            bool verifiedPortalAlreadyWinnerResolution = IsPortalAlreadyWinnerMessageResolved(source, record);
            bool verifiedForeignIncomeResolution = IsForeignIncomeRequestResolved(source, record);
            bool verifiedCareerInstanceInstruction = IsCareerInstanceInstructionRequest(source, topicCode);
            if ((IsYes(source, "CLASSIFICAZIONE_DA_VERIFICARE") || string.IsNullOrWhiteSpace(topicCode)) &&
                !verifiedPermitBlockRemoval &&
                !verifiedPaymentResolution &&
                !verifiedIbanResolution &&
                !verifiedApplicationWindowResolution &&
                !verifiedDomicileContractResolution &&
                !verifiedPortalSubmissionResolution &&
                !verifiedManualDocumentRequest &&
                !verifiedClosedApplicationCompilation &&
                !verifiedSectionCompilationResolution &&
                !verifiedPortalAlreadyWinnerResolution &&
                !verifiedForeignIncomeResolution &&
                !verifiedCareerInstanceInstruction)
            {
                return Decision(Verify,
                    "Leggere messaggio e oggetto; correggere l'argomento prima della chiusura.",
                    "La classificazione automatica non è abbastanza affidabile per proporre una chiusura.",
                    "GEN_02");
            }

            if (resolution.IsResolved)
            {
                return Decision(
                    CloseResolved,
                    resolution.RequiredAction,
                    resolution.Reason,
                    resolution.ResponseCode);
            }

            if (IsPortalSubmissionProblemResolved(source, topicCode, applicationState))
            {
                return Decision(CloseResolved,
                    "Confermare che la domanda risulti trasmessa o completa; poi chiudere il ticket.",
                    "Il problema di compilazione o trasmissione indicato nel ticket risulta superato: la domanda è ora trasmessa/completa.",
                    "POR_02");
            }

            if (verifiedManualDocumentRequest)
            {
                return Decision(ActionRequired,
                    "Gestire manualmente la produzione, il recupero o la verifica del documento richiesto.",
                    "Il ticket richiede ricevute, attestazioni, certificati o documenti ufficiali e non va chiuso automaticamente.",
                    "GEN_DOC_01");
            }

            if (verifiedClosedApplicationCompilation)
            {
                return Decision(CloseResolved,
                    "Confermare l'anno accademico citato nel ticket e chiudere con riscontro sulla finestra ormai conclusa.",
                    "La richiesta riguarda compilazione, trasmissione, scadenze o informazioni del bando; la finestra dell'anno accademico selezionato risulta conclusa.",
                    "DOM_02");
            }

            if (verifiedSectionCompilationResolution)
            {
                return Decision(CloseResolved,
                    "Confermare domanda trasmessa/completa, esito idoneo/vincitore e assenza di blocchi; poi chiudere il ticket.",
                    "La richiesta riguarda come compilare o proseguire la domanda; la pratica è ora trasmessa/completa con esito coerente e senza blocchi.",
                    "DOM_04");
            }

            if (verifiedPortalAlreadyWinnerResolution)
            {
                return Decision(CloseResolved,
                    "Confermare esito idoneo/vincitore e assenza di blocchi; poi chiudere il ticket.",
                    "Il ticket segnala un messaggio del portale collegato a idoneità/vincita o stessa posizione accademica: la pratica risulta senza blocchi.",
                    "POR_02");
            }

            if (verifiedForeignIncomeResolution)
            {
                return Decision(CloseResolved,
                    "Confermare assenza del blocco BDR/redditi esteri e chiudere il ticket.",
                    "La richiesta riguarda redditi esteri o ISEE parificato e non risulta più il blocco pertinente.",
                    "DOC_RED_01");
            }

            if (verifiedCareerInstanceInstruction)
            {
                return Decision(CloseResolved,
                    "Rispondere che lo studente deve aprire l'istanza dedicata; poi chiudere il ticket.",
                    "La richiesta riguarda cambio corso, sede, passaggio, trasferimento o abbreviazione: non va lavorata sul ticket ma indirizzata all'istanza dedicata.",
                    "CAR_02");
            }

            if (intent.Intent == TicketIntent.PRE_DOMANDA || intent.Intent == TicketIntent.INFORMAZIONE)
            {
                return Decision(ClosePersonalized,
                    "Predisporre un riscontro informativo sul quesito espresso, senza usare pagamenti o mandati come evidenza di chiusura.",
                    "Il ticket è una richiesta informativa o pre-domanda: l'eventuale record esistente non dimostra che il quesito sia già risolto.",
                    "GEN_03");
            }

            if (record == null)
            {
                return Decision(Verify,
                    "Verificare codice fiscale, identità studente e presenza di una domanda.",
                    "Non è stata trovata una domanda operativa associabile al ticket.",
                    "GEN_01");
            }

            if (resolution.HasRequestedConditions && !recordSelectionIsReliable)
            {
                return Decision(
                    Verify,
                    "Individuare nel ticket l'anno accademico e verificare il record dello stesso anno prima della chiusura.",
                    "La chiusura automatica richiede una corrispondenza esatta fra anno accademico del ticket e record operativo.",
                    "GEN_06");
            }

            if (intent.Intent == TicketIntent.CONTESTAZIONE ||
                intent.Intent == TicketIntent.RINUNCIA_O_RECESSO ||
                intent.Intent == TicketIntent.ERRORE_TECNICO ||
                intent.Intent == TicketIntent.ALTRO_DA_VERIFICARE)
            {
                if (intent.Intent == TicketIntent.RINUNCIA_O_RECESSO &&
                    string.Equals(topicCode, "ALLOGGIO", StringComparison.OrdinalIgnoreCase))
                {
                    return Decision(ActionRequired,
                        "Applicare la procedura di rinuncia, cessazione o check-out.",
                        "La richiesta riguarda una rinuncia o un check-out e richiede una lavorazione amministrativa dedicata.",
                        "ALL_05");
                }

                if (intent.Intent == TicketIntent.CONTESTAZIONE)
                {
                    return Decision(Verify,
                        "Gestire come contestazione: confrontare il testo del ticket con dati, esiti e importi disponibili.",
                        "Il ticket contiene una contestazione: l'esito operativo non è sufficiente per una chiusura automatica.",
                        "GEN_04A");
                }

                if (intent.Intent == TicketIntent.RINUNCIA_O_RECESSO)
                {
                    return Decision(ActionRequired,
                        "Applicare la procedura amministrativa di rinuncia, recesso o cessazione pertinente all'argomento.",
                        "La richiesta riguarda una rinuncia, un recesso o una cessazione e richiede una lavorazione dedicata.",
                        "GEN_04D");
                }

                if (intent.Intent == TicketIntent.ERRORE_TECNICO ||
                    string.Equals(topicCode, "PORTALE_E_ACCESSO", StringComparison.OrdinalIgnoreCase))
                {
                    return Decision(Verify,
                        "Verificare il problema tecnico descritto e lo stato della domanda.",
                        "Il ticket riguarda un accesso o una funzionalità del portale e richiede verifica puntuale.",
                        "POR_01");
                }

                return Decision(Verify,
                    "Rileggere il ticket e chiarire l'oggetto richiesto prima di decidere chiusura, risposta o lavorazione.",
                    "Il ticket contiene un intento non univoco o non automatizzabile.",
                    "GEN_04B");
            }

            if (resolution.HasRequestedConditions)
            {
                return Decision(
                    ActionRequired,
                    resolution.RequiredAction,
                    resolution.Reason,
                    resolution.ResponseCode);
            }

            if (blockAssessment.HasRelevantBlocks)
            {
                return Decision(ActionRequired,
                    "Individuare e gestire la causa del blocco pertinente al ticket.",
                    "La domanda presenta uno o più blocchi rilevanti per l'argomento trattato.",
                    "BLO_01");
            }

            if (applicationState != ApplicationState.TRASMESSA_O_COMPLETA &&
                RequiresSubmittedApplication(topicCode))
            {
                return Decision(ActionRequired,
                    "Verificare compilazione, trasmissione e stato effettivo della domanda.",
                    "Lo stato disponibile della domanda non consente una chiusura automatica.",
                    "DOM_01");
            }

            if (contradictions.Count > 0)
            {
                if (intent.Intent == TicketIntent.SOLLECITO &&
                    (string.Equals(topicCode, "PAGAMENTI_E_TASSE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(topicCode, "BENEFICI_E_IMPORTI", StringComparison.OrdinalIgnoreCase)))
                {
                    return Decision(Verify,
                        "Verificare data del mandato, canale di accredito e causale prima della chiusura.",
                        "Il ticket segnala una mancata ricezione mentre risultano pagamento e mandato.",
                        "PAG_09");
                }

                return Decision(Verify,
                    "Verificare la discordanza indicata prima di predisporre o inviare il riscontro.",
                    "Il testo del ticket e i dati operativi non sono pienamente coerenti.",
                    "GEN_05");
            }

            if (intent.Intent == TicketIntent.AGGIORNAMENTO_DATI &&
                string.Equals(topicCode, "IBAN", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(ActionRequired,
                    "Verificare l'autorizzazione alla variazione e i dati bancari aggiornati.",
                    "La richiesta riguarda una modifica delle coordinate di pagamento e richiede una lavorazione dedicata.",
                    "IBAN_02");
            }

            if (string.Equals(topicCode, "PAGAMENTI_E_TASSE", StringComparison.OrdinalIgnoreCase))
            {
                return Decision(
                    Verify,
                    "Rileggere il ticket e distinguere erogazioni della borsa da tasse o pagamenti effettuati dallo studente.",
                    "Il testo non contiene una richiesta riconoscibile di erogazione della borsa.",
                    "PAG_16");
            }

            return Decision(TicketTopicModuleRegistry
                .Resolve(topicCode)
                .BuildFallbackDecision(record));
        }

        private static bool IsVerifiedPermitBlockRemoval(
            TicketIntentAssessment intent,
            ResolutionAssessment resolution)
        {
            return intent.Intent == TicketIntent.RIMOZIONE_BLOCCO &&
                   resolution.IsResolved &&
                   (string.Equals(
                        resolution.Condition,
                        "PERMESSO_LAVORATO_E_BLOCCO_RIMOSSO_STESSO_AA",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        resolution.Condition,
                        "PERMESSO_LAVORATO_E_BLOCCO_PERMESSO_ASSENTE_STESSO_AA",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        resolution.Condition,
                        "PERMESSO_BLOCCO_O_PAGAMENTO_RISOLTO_STESSO_AA",
                        StringComparison.OrdinalIgnoreCase)) &&
                   resolution.RequestedScopes.IndexOf("PERMESSO_SOGGIORNO", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (resolution.RequestedScopes.IndexOf("BLOCCHI_PRATICA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    resolution.RequestedScopes.IndexOf("PERMESSO_SOGGIORNO", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsVerifiedPaymentResolution(ResolutionAssessment resolution)
        {
            if (!resolution.IsResolved ||
                resolution.RequestedScopes.IndexOf("EROGAZIONE_BORSA", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return string.Equals(resolution.Condition, "PAGAMENTO_COMPLETO_STESSO_AA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(resolution.Condition, "PRIMA_RATA_BORSA_PAGATA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(resolution.Condition, "SALDO_BORSA_PAGATO", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(resolution.Condition, "INTEGRAZIONE_BORSA_PAGATA", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVerifiedIbanResolution(ResolutionAssessment resolution)
        {
            if (!resolution.IsResolved ||
                resolution.RequestedScopes.IndexOf("AGGIORNAMENTO_IBAN", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return string.Equals(resolution.Condition, "IBAN_AGGIORNATO_DOPO_TICKET", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(resolution.Condition, "IBAN_PRESENTE_NEL_TICKET_COINCIDE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVerifiedApplicationWindowResolution(ResolutionAssessment resolution)
        {
            return resolution.IsResolved &&
                   resolution.RequestedScopes.IndexOf("FINESTRA_COMPILAZIONE_DOMANDA", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   string.Equals(
                       resolution.Condition,
                       "FINESTRA_COMPILAZIONE_DOMANDA_CHIUSA",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVerifiedDomicileContractResolution(ResolutionAssessment resolution)
        {
            return resolution.IsResolved &&
                   resolution.RequestedScopes.IndexOf("DOMICILIO_CONTRATTO", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (string.Equals(resolution.Condition, "DOMICILIO_CONTRATTO_STATUS_B_VALIDO", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(resolution.Condition, "DOMICILIO_ISTANZA_APERTA_PRESENTE", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPermitBlockPaymentRequest(TicketResolutionScope scope)
        {
            if (scope == null || !scope.ResidencePermitDocuments)
                return false;

            string text = scope.Request.NormalizedText;
            return TicketDomainText.ContainsAny(text, "permesso", "titolo di soggiorno", "residence permit") &&
                   TicketDomainText.ContainsAny(text, "blocco", "sblocco", "pagamento", "saldo", "tranche", "rata", "block", "unblock", "payment");
        }

        private static bool IsPortalSubmissionProblemResolved(
            DataRow source,
            string topicCode,
            ApplicationState applicationState)
        {
            if (!string.Equals(topicCode, "PORTALE_E_ACCESSO", StringComparison.OrdinalIgnoreCase) ||
                applicationState != ApplicationState.TRASMESSA_O_COMPLETA)
            {
                return false;
            }

            string text = TicketDomainText.Normalize(
                $"{ReadString(source, "OGGETTO")} {ReadString(source, "PRIMO_MSG_STUDENTE")}");
            bool submissionProblem = TicketDomainText.ContainsAny(
                text,
                "trasmettere",
                "trasmissione",
                "inoltrare",
                "invio domanda",
                "inviare la domanda",
                "presentare la domanda",
                "proseguire",
                "andare avanti",
                "non mi da il tasto",
                "tasto per trasmettere",
                "compilazione domanda",
                "submit application",
                "send application",
                "proceed");
            bool excluded = TicketDomainText.ContainsAny(
                text,
                "spid",
                "password",
                "login",
                "credenziali",
                "accesso con",
                "allegato",
                "allegati",
                "documento",
                "documenti",
                "upload documento",
                "caricare documento");

            return submissionProblem && !excluded;
        }

        private static string GetTicketText(DataRow source) =>
            TicketDomainText.Normalize($"{ReadString(source, "OGGETTO")} {ReadString(source, "PRIMO_MSG_STUDENTE")}");

        private static bool IsApplicationCompilationQuestionText(string text)
        {
            if (HasPaymentOrAmountPriority(text))
                return false;

            bool applicationContext = TicketDomainText.ContainsAny(
                text,
                "domanda",
                "borsa di studio",
                "beneficio",
                "bando",
                "application");
            if (!applicationContext)
                return false;

            return TicketDomainText.ContainsAny(
                text,
                "come compilare",
                "compilazione domanda",
                "compilare la domanda",
                "come trasmettere",
                "trasmettere la domanda",
                "inoltrare la domanda",
                "invio domanda",
                "presentare la domanda",
                "come presentare",
                "cosa devo caricare",
                "cosa caricare",
                "dove devo inserire",
                "dove inserire",
                "quale documento",
                "quali documenti",
                "documentazione da caricare",
                "scadenza",
                "scadenze",
                "date del bando",
                "informazioni sul bando",
                "info bando",
                "how to apply",
                "how should i fill",
                "what should i upload",
                "which document");
        }

        private static bool HasPaymentOrAmountPriority(string text)
        {
            bool paymentOrAmount = TicketDomainText.ContainsAny(
                text,
                "contributo integrativo",
                "contributo straordinario",
                "erogazione",
                "erogazioni",
                "importo",
                "importi",
                "accredito",
                "pagamento",
                "pagamenti",
                "liquidazione",
                "rata",
                "rate",
                "saldo",
                "tranche",
                "mandato",
                "mandati",
                "rimborso",
                "tassa regionale",
                "mensa",
                "mobilita",
                "mobilità",
                "quota monetaria");
            if (!paymentOrAmount)
                return false;

            return !TicketDomainText.ContainsAny(
                text,
                "come compilare",
                "come posso compilare",
                "come fare domanda",
                "presentare domanda",
                "presentare la domanda",
                "fare domanda",
                "compilazione domanda",
                "compilazione della domanda",
                "compilare la domanda",
                "come trasmettere",
                "trasmettere la domanda",
                "invio domanda",
                "inoltrare domanda",
                "cosa devo caricare",
                "cosa caricare",
                "dove devo inserire",
                "dove inserire",
                "scadenza domanda",
                "termine domanda",
                "informazioni sul bando",
                "date del bando",
                "quando apre",
                "quando posso fare",
                "application window",
                "how to apply");
        }

        private static bool IsUploadDocumentProblemText(string text)
        {
            if (TicketDomainText.ContainsAny(text, "cosa devo caricare", "cosa caricare", "quale documento", "quali documenti"))
                return false;

            return TicketDomainText.ContainsAny(
                text,
                "upload",
                "allegato",
                "allegati",
                "caricare allegato",
                "caricare documento",
                "caricamento documento",
                "non riesco a caricare",
                "non riesco ad allegare",
                "errore caricamento",
                "file troppo grande",
                "formato file");
        }

        private static bool IsManualDocumentOrAttestationRequest(DataRow source)
        {
            string text = GetTicketText(source);
            if (IsApplicationCompilationQuestionText(text) || IsForeignIncomeText(text))
                return false;

            return TicketDomainText.ContainsAny(
                text,
                "ricevuta pagopa",
                "ricevute pagopa",
                "ricevuta iuv",
                "codice iuv",
                "attestazione",
                "certificato",
                "certificazione",
                "documento ufficiale",
                "prova dei mezzi",
                "certificato borsa",
                "attestazione vincita",
                "attestazione idoneita",
                "attestazione idoneità",
                "ricevuta tassa",
                "ricevuta pagamento",
                "esonero tassa",
                "rimborso tassa regionale",
                "questura");
        }

        private static bool IsClosedApplicationCompilationQuestion(
            DataRow source,
            TicketOfficeRecord? record,
            bool recordSelectionIsReliable)
        {
            if (record == null || !recordSelectionIsReliable)
                return false;

            string text = GetTicketText(source);
            if (!IsApplicationCompilationQuestionText(text) || IsUploadDocumentProblemText(text))
                return false;

            if (!TryGetAcademicYearStart(record.AcademicYear, out int startYear))
                return false;

            DateTime end = new(startYear, 8, 31);
            return DateTime.Today.Date > end;
        }

        private static bool IsSectionCompilationResolved(
            DataRow source,
            string topicCode,
            TicketOfficeRecord? record,
            ApplicationState applicationState)
        {
            if (record == null ||
                applicationState != ApplicationState.TRASMESSA_O_COMPLETA ||
                !string.IsNullOrWhiteSpace(record.Blocks))
            {
                return false;
            }

            string text = GetTicketText(source);
            if (!IsApplicationCompilationQuestionText(text) || IsUploadDocumentProblemText(text))
                return false;

            string outcome = (record.BsOutcome ?? string.Empty).Trim();
            if (string.Equals(topicCode, "ISCRIZIONE_E_CARRIERA", StringComparison.OrdinalIgnoreCase))
                return outcome == "1" || outcome == "2";

            return outcome == "2";
        }

        private static bool IsPortalAlreadyWinnerMessageResolved(
            DataRow source,
            TicketOfficeRecord? record)
        {
            if (record == null || !string.IsNullOrWhiteSpace(record.Blocks))
                return false;

            string text = GetTicketText(source);
            bool portalMessage = TicketDomainText.ContainsAny(
                text,
                "gia idoneo",
                "già idoneo",
                "gia vincitore",
                "già vincitore",
                "stesso anno di corso",
                "stessa tipologia di corso",
                "regolarizzare la propria posizione",
                "posizione non regolare");
            bool outcomeCompatible =
                string.Equals(record.BsOutcome?.Trim(), "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.BsOutcome?.Trim(), "2", StringComparison.OrdinalIgnoreCase);

            return portalMessage && outcomeCompatible;
        }

        private static bool IsForeignIncomeRequestResolved(
            DataRow source,
            TicketOfficeRecord? record)
        {
            if (record == null)
                return false;

            string text = GetTicketText(source);
            return IsForeignIncomeText(text) &&
                   !TicketDomainModuleRegistry.Blocks.HasForeignIncomeBlock(record.Blocks);
        }

        private static bool IsForeignIncomeText(string text)
        {
            bool directForeignIncome = TicketDomainText.ContainsAny(
                text,
                "redditi esteri",
                "documentazione definitiva redditi esteri",
                "isee parificato",
                "blocco bdr",
                "bdr");
            bool cafForeignIncomeContext =
                TicketDomainText.ContainsAny(text, "caf") &&
                TicketDomainText.ContainsAny(text, "isee", "redditi", "documentazione definitiva", "parificato");

            return directForeignIncome || cafForeignIncomeContext;
        }

        private static bool IsCareerInstanceInstructionRequest(DataRow source, string topicCode)
        {
            string text = GetTicketText(source);
            bool careerContext =
                string.Equals(topicCode, "ISCRIZIONE_E_CARRIERA", StringComparison.OrdinalIgnoreCase) ||
                TicketDomainText.ContainsAny(text, "corso", "sede", "carriera", "universita", "università");
            if (!careerContext)
                return false;

            return TicketDomainText.ContainsAny(
                text,
                "cambio sede",
                "cambiare sede",
                "modifica sede",
                "cambio corso",
                "cambiare corso",
                "passaggio",
                "trasferimento",
                "abbreviazione carriera",
                "rettifica universita",
                "rettifica università",
                "universita estera",
                "università estera");
        }

        private static bool TryGetAcademicYearStart(int academicYear, out int startYear)
        {
            startYear = 0;
            if (academicYear >= 20000000 && academicYear <= 20999999)
            {
                startYear = academicYear / 10000;
                return true;
            }

            if (academicYear >= 2000 && academicYear <= 2099)
            {
                startYear = academicYear;
                return true;
            }

            return false;
        }

        private static OperationalDecision Decision(string decision, string action, string reason, string responseCode) => new()
        {
            Decision = decision,
            RequiredAction = action,
            Reason = reason,
            ResponseCode = responseCode,
            ResponseText = GetResponseText(responseCode)
        };

        private static OperationalDecision Decision(TopicOperationalDecision decision) =>
            Decision(
                decision.Decision,
                decision.RequiredAction,
                decision.Reason,
                decision.ResponseCode);

        private static string GetResponseText(string code) =>
            ResponseTexts.Value.TryGetValue(code, out string? response)
                ? response
                : string.Empty;

        private static string FormatAmount(decimal amount) =>
            amount.ToString("N2", ItalianCulture);

        private static bool RequiresSubmittedApplication(string topicCode)
        {
            string topic = topicCode?.ToUpperInvariant() ?? string.Empty;
            return topic != "PORTALE_E_ACCESSO" && topic != "ALTRO";
        }

        private static string GetClosureStatus(
            OperationalDecision decision,
            ResolutionAssessment resolution,
            TicketRecordSelection selection)
        {
            if (resolution.IsResolved && decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
                return "CHIUDIBILE_EVIDENZA_FORTE";
            if (resolution.HasRequestedConditions && !selection.IsExactAcademicYear)
                return "NON_VALUTABILE_AA";
            if (resolution.HasRequestedConditions && !resolution.IsResolved)
                return "NON_RISOLTO";
            if (decision.Decision.StartsWith(ClosePersonalized, StringComparison.OrdinalIgnoreCase))
                return "FUORI_PERIMETRO_RISCONTRO_ASSISTITO";
            if (decision.Decision.StartsWith(ActionRequired, StringComparison.OrdinalIgnoreCase) ||
                decision.Decision.StartsWith(DataMissing, StringComparison.OrdinalIgnoreCase))
                return "NON_CHIUDIBILE";
            return "DA_VERIFICARE";
        }

        private static string GetRequestedObject(
            TicketResolutionScope scope,
            PaymentRequestKind paymentRequest)
        {
            var values = new List<string>();
            if (scope.ScholarshipPayments)
                values.Add($"Pagamento borsa {TicketOperationalAnalysis.FormatPaymentRequestKind(paymentRequest)}");
            if (scope.ResidencePermitDocuments)
                values.Add("Documenti permesso di soggiorno");
            if (scope.ApplicationBlocks)
                values.Add("Blocco amministrativo della domanda");
            if (scope.IbanUpdates)
                values.Add("Aggiornamento IBAN");
            if (scope.ApplicationWindowInformation)
                values.Add("Finestra compilazione domanda");
            if (scope.DomicileContract)
                values.Add("Domicilio/contratto fuori sede");
            return values.Count == 0
                ? "Nessun oggetto di chiusura automatica rilevato"
                : string.Join(" + ", values);
        }

        private static string GetAcademicYearConsistency(TicketRecordSelection selection)
        {
            if (!selection.HasRequestedAcademicYear)
            {
                return selection.HasRecord
                    ? "AA_NON_INDICATO_FALLBACK_ULTIMA_DOMANDA"
                    : "AA_NON_IDENTIFICATO_NEL_TICKET";
            }

            if (!selection.HasRecord)
                return "RECORD_OPERATIVO_ASSENTE";

            return selection.IsExactAcademicYear
                ? "AA_COINCIDENTE"
                : "AA_NON_COINCIDENTE";
        }

        private static string GetNonClosureReason(
            OperationalDecision decision,
            ResolutionAssessment resolution)
        {
            if (resolution.IsResolved && decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(resolution.UnresolvedConditions))
                return resolution.UnresolvedConditions;
            return decision.Reason;
        }

        private static string GetNextCheck(
            OperationalDecision decision,
            ResolutionAssessment resolution)
        {
            if (resolution.IsResolved &&
                decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
            {
                return "Confermare evidenza e anno accademico; poi chiudere il ticket.";
            }

            return decision.ResponseCode switch
            {
                "GEN_01" => "Verificare codice fiscale, identità studente e presenza domanda.",
                "GEN_02" => "Correggere o confermare l'argomento prima di valutare la chiusura.",
                "GEN_03" or "GEN_04C" => "Predisporre riscontro informativo; non chiudere per evidenza operativa.",
                "GEN_04A" => "Gestire come contestazione: confrontare testo, esiti, importi e note.",
                "GEN_04B" => "Chiarire l'oggetto richiesto leggendo messaggio e oggetto.",
                "GEN_04D" => "Applicare la procedura amministrativa di rinuncia/recesso/cessazione.",
                "GEN_04E" => "Verificare se la richiesta è compatibile con una chiusura per situazione già risolta.",
                "GEN_06" => "Individuare l'anno accademico corretto e verificare il record dello stesso AA.",
                "PAG_16" => "Distinguere erogazione borsa da tasse o pagamenti effettuati dallo studente.",
                "PAG_09" => "Controllare mandato, data accredito, canale e causale prima del riscontro.",
                "PAG_11" => "Classificare il codice BS anomalo prima di calcolare importi o residui.",
                "IBAN_05" => "Confrontare IBAN nel ticket, ultimo IBAN attivo e data validità prima della chiusura.",
                "DOM_03" => "Verificare anno accademico e finestra domanda; se attiva/futura predisporre riscontro informativo.",
                "BLO_01" => "Gestire il blocco rilevante prima di rispondere o chiudere.",
                "DOC_05" => "Verificare documenti permesso 01/02/03 e attendere status 05.",
                "POR_01" => "Verificare problema tecnico/accesso e stato domanda.",
                _ => decision.RequiredAction
            };
        }

        private static string GetVerificationGroup(
            OperationalDecision decision,
            ResolutionAssessment resolution)
        {
            if (resolution.IsResolved &&
                decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
            {
                return "01_CHIUSURA_PROPOSTA";
            }

            string code = decision.ResponseCode ?? string.Empty;
            if (string.Equals(code, "GEN_01", StringComparison.OrdinalIgnoreCase))
                return "02_DOMANDA_ASSENTE";
            if (string.Equals(code, "GEN_02", StringComparison.OrdinalIgnoreCase))
                return "03_CLASSIFICAZIONE";
            if (string.Equals(code, "GEN_06", StringComparison.OrdinalIgnoreCase))
                return "04_ANNO_ACCADEMICO";
            if (string.Equals(code, "GEN_04A", StringComparison.OrdinalIgnoreCase))
                return "05_CONTESTAZIONI";
            if (string.Equals(code, "GEN_04B", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "GEN_04E", StringComparison.OrdinalIgnoreCase))
            {
                return "06_RICHIESTA_NON_CHIARA";
            }
            if (string.Equals(code, "GEN_03", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "GEN_04C", StringComparison.OrdinalIgnoreCase) ||
                decision.Decision.StartsWith(ClosePersonalized, StringComparison.OrdinalIgnoreCase))
            {
                return "07_RISPOSTA_ASSISTITA";
            }
            if (string.Equals(code, "GEN_04D", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "ALL_05", StringComparison.OrdinalIgnoreCase))
            {
                return "08_RINUNCIA_RECESSO";
            }
            if (code.StartsWith("POR_", StringComparison.OrdinalIgnoreCase))
                return "09_PORTALE_ACCESSO";
            if (code.StartsWith("PAG_", StringComparison.OrdinalIgnoreCase))
                return "10_PAGAMENTI";
            if (code.StartsWith("IBAN_", StringComparison.OrdinalIgnoreCase))
                return "16_IBAN";
            if (code.StartsWith("DOM_", StringComparison.OrdinalIgnoreCase))
                return "17_DOMANDA_COMPILAZIONE";
            if (code.StartsWith("DOC", StringComparison.OrdinalIgnoreCase))
                return "11_DOCUMENTI_PERMESSO";
            if (code.StartsWith("BLO_", StringComparison.OrdinalIgnoreCase))
                return "12_BLOCCHI";
            if (decision.Decision.StartsWith(ActionRequired, StringComparison.OrdinalIgnoreCase))
                return "13_AZIONE_OPERATIVA";
            if (decision.Decision.StartsWith(DataMissing, StringComparison.OrdinalIgnoreCase))
                return "14_DATI_MANCANTI";
            return "15_VERIFICA_MANUALE";
        }

        private static string GetClosableLabel(OperationalDecision decision)
        {
            if (decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
                return "SI";
            if (decision.Decision.StartsWith(CloseStandard, StringComparison.OrdinalIgnoreCase))
                return "DA_VERIFICARE";
            if (decision.Decision.StartsWith(Verify, StringComparison.OrdinalIgnoreCase))
                return "DA_VERIFICARE";
            return "NO";
        }

        private static string GetPriority(OperationalDecision decision)
        {
            if (decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase))
                return "P1";
            if (decision.Decision.StartsWith(CloseStandard, StringComparison.OrdinalIgnoreCase))
                return "P2";
            if (decision.Decision.StartsWith(ActionRequired, StringComparison.OrdinalIgnoreCase) ||
                decision.Decision.StartsWith(DataMissing, StringComparison.OrdinalIgnoreCase))
                return "P3";
            return "P4";
        }

        private static string GetPriorityReason(
            OperationalDecision decision,
            ResolutionAssessment resolution)
        {
            if (resolution.IsResolved)
                return $"Situazione già risolta sullo stesso anno: {resolution.Condition}";
            if (decision.Decision.StartsWith(CloseStandard, StringComparison.OrdinalIgnoreCase))
                return "Controllo rapido prima della chiusura";
            if (decision.Decision.StartsWith(ClosePersonalized, StringComparison.OrdinalIgnoreCase))
                return "Riscontro assistito: non è una chiusura automatica";
            if (decision.Decision.StartsWith(ActionRequired, StringComparison.OrdinalIgnoreCase))
                return "Ticket non chiudibile: azione operativa necessaria";
            if (decision.Decision.StartsWith(DataMissing, StringComparison.OrdinalIgnoreCase))
                return "Ticket non chiudibile: dato o documento mancante";
            return "Classificazione, anno accademico o situazione da verificare manualmente";
        }

        private static int GetPriorityOrder(string priority) => priority switch
        {
            "P1" => 1,
            "P2" => 2,
            "P3" => 3,
            _ => 4
        };

        private static string GetTopicConfidence(DataRow row, string topicCode)
        {
            if (IsYes(row, "CLASSIFICAZIONE_DA_VERIFICARE") || string.IsNullOrWhiteSpace(topicCode))
                return "BASSA";

            int confidence = ReadNullableInt(row, "PUNTEGGIO_CONFIDENZA") ?? 0;
            if (confidence >= 80)
                return "ALTA";
            if (confidence >= 60)
                return "ORDINARIA";
            return "BASSA";
        }

        private static string GetOperationalDataConfidence(TicketRecordSelection selection, ApplicationState applicationState)
        {
            if (!selection.HasRecord || applicationState == ApplicationState.SCONOSCIUTO)
                return "BASSA";
            if (!selection.IsExactAcademicYear)
                return "BASSA";
            return "ALTA";
        }

        private static string GetDecisionConfidence(
            OperationalDecision decision,
            string topicConfidence,
            string intentConfidence,
            string operationalDataConfidence,
            IReadOnlyCollection<string> contradictions,
            BlockAssessment blocks,
            ResolutionAssessment resolution)
        {
            if (string.Equals(
                    resolution.Condition,
                    "PERMESSO_LAVORATO_E_BLOCCO_RIMOSSO_STESSO_AA",
                    StringComparison.OrdinalIgnoreCase) &&
                resolution.IsResolved &&
                contradictions.Count == 0 &&
                string.Equals(operationalDataConfidence, "ALTA", StringComparison.OrdinalIgnoreCase))
            {
                return "ALTA";
            }

            if (string.Equals(
                    resolution.Condition,
                    "FINESTRA_COMPILAZIONE_DOMANDA_CHIUSA",
                    StringComparison.OrdinalIgnoreCase) &&
                resolution.IsResolved &&
                contradictions.Count == 0)
            {
                return "ALTA";
            }

            if (resolution.IsResolved &&
                contradictions.Count == 0 &&
                !string.Equals(operationalDataConfidence, "BASSA", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(topicConfidence, "BASSA", StringComparison.OrdinalIgnoreCase)
                    ? "MEDIA"
                    : "ALTA";
            }

            if (contradictions.Count > 0 ||
                string.Equals(topicConfidence, "BASSA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(intentConfidence, "BASSA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operationalDataConfidence, "BASSA", StringComparison.OrdinalIgnoreCase))
            {
                return "BASSA";
            }

            if ((decision.Decision.StartsWith(CloseResolved, StringComparison.OrdinalIgnoreCase) ||
                 decision.Decision.StartsWith(CloseStandard, StringComparison.OrdinalIgnoreCase)) &&
                !blocks.HasRelevantBlocks &&
                !string.Equals(intentConfidence, "BASSA", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(operationalDataConfidence, "ALTA", StringComparison.OrdinalIgnoreCase))
            {
                return "ALTA";
            }

            return "MEDIA";
        }

        private static string BuildEvidence(
            TicketOfficeRecord? record,
            string topicCode,
            string operationalAcademicYear,
            TicketResolutionScope scope)
        {
            if (record == null)
                return "Nessuna domanda operativa trovata per il codice fiscale.";

            var values = new List<string>();
            if (!string.IsNullOrWhiteSpace(operationalAcademicYear))
                values.Add($"AA record {operationalAcademicYear}");
            if (!string.IsNullOrWhiteSpace(record.ApplicationNumber))
                values.Add($"domanda {record.ApplicationNumber}");

            // Quando un modulo di dominio ha riconosciuto l'oggetto richiesto, le sue
            // evidenze vengono già aggiunte dal ResolutionAssessment. Non aggiungere dati
            // del topic storico (ad esempio esito alloggio) se il ticket riguarda permesso
            // o blocchi: sarebbero veri ma non pertinenti alla decisione.
            if (scope != null && scope.HasConditions)
                return string.Join("; ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

            string topicEvidence = TicketTopicModuleRegistry.Resolve(topicCode).BuildEvidence(record);
            if (!string.IsNullOrWhiteSpace(topicEvidence))
                values.Add(topicEvidence);

            if (!string.IsNullOrWhiteSpace(record.Blocks))
                AddIfNotEmpty(values, "blocchi", Truncate(record.Blocks, 120));

            return string.Join("; ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static IEnumerable<(DataRow Row, int Index)> GetDistinctTicketRows(DataTable source)
        {
            if (!source.Columns.Contains("ID_TICKET"))
                return source.AsEnumerable().Select((row, index) => (row, index));

            return source.AsEnumerable()
                .Select((row, index) => (Row: row, Index: index))
                .GroupBy(item =>
                {
                    string ticketId = ReadString(item.Row, "ID_TICKET");
                    return string.IsNullOrWhiteSpace(ticketId) ? $"__ROW__{item.Index}" : ticketId;
                }, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
        }

        private static DataTable CreateStudentCategorySummaryTable()
        {
            var result = new DataTable();
            result.Columns.Add("CATEGORIA_DOMANDE_STUDENTE", typeof(string));
            result.Columns.Add("CODFISC", typeof(string));
            result.Columns.Add("CODSTUD", typeof(string));
            result.Columns.Add("ANNI_DOMANDE_RILEVATI", typeof(string));
            result.Columns.Add("NUMERO_TICKET", typeof(int));
            result.Columns.Add("TICKET_P1", typeof(int));
            result.Columns.Add("TICKET_CHIUDIBILI", typeof(int));
            result.Columns.Add("TICKET_DA_VERIFICARE", typeof(int));
            result.Columns.Add("TICKET_AZIONE_OPERATIVA", typeof(int));
            result.Columns.Add("ALMENO_UN_TICKET_RIGUARDA_BLOCCHI", typeof(string));
            result.Columns.Add("STUDENTE_HA_BLOCCHI", typeof(string));
            result.Columns.Add("PRIORITA_MASSIMA", typeof(string));
            result.Columns.Add("ID_TICKET_PRIORITARIO", typeof(string));
            result.Columns.Add("OGGETTO_TICKET_PRIORITARIO", typeof(string));
            result.Columns.Add("DECISIONE_PROPOSTA_TICKET_PRIORITARIO", typeof(string));
            result.Columns.Add("FOGLIO_DETTAGLIO", typeof(string));
            result.Columns.Add("DATA_APERTURA_PIU_VECCHIA", typeof(DateTime));
            return result;
        }

        private static int? ReadNullableInt(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value)
                return null;
            return int.TryParse(ReadString(row, columnName), NumberStyles.Integer, ItalianCulture, out int value)
                ? value
                : null;
        }

        private static DateTime? ReadNullableDate(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName))
                return null;
            object value = row[columnName];
            if (value == null || value == DBNull.Value)
                return null;
            if (value is DateTime date)
                return date;
            return DateTime.TryParse(Convert.ToString(value, ItalianCulture), ItalianCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed)
                ? parsed
                : null;
        }

        private static DataTable CreateQueueTable()
        {
            var result = new DataTable();
            result.Columns.Add("CHIUDIBILE", typeof(string));
            result.Columns.Add("PRIORITA", typeof(string));
            result.Columns.Add("PROSSIMO_CONTROLLO", typeof(string));
            result.Columns.Add("GRUPPO_VERIFICA", typeof(string));
            result.Columns.Add("AZIONE_RICHIESTA", typeof(string));
            result.Columns.Add("CODICE_RISPOSTA", typeof(string));
            result.Columns.Add("TESTO_RISCONTRO_BASE", typeof(string));
            result.Columns.Add("EVIDENZE_CHIAVE", typeof(string));
            result.Columns.Add("MOTIVO_NON_CHIUSURA", typeof(string));
            result.Columns.Add("FOGLIO_DETTAGLIO", typeof(string));
            result.Columns.Add("ID_TICKET", typeof(string));
            result.Columns.Add("ETA_TICKET_GIORNI", typeof(int));
            result.Columns.Add("DATA_APERTURA", typeof(DateTime));
            result.Columns.Add("DATA_ULTIMO_MESSAGGIO", typeof(DateTime));
            result.Columns.Add("ARGOMENTO", typeof(string));
            result.Columns.Add("OGGETTO", typeof(string));
            result.Columns.Add("PRIMO_MSG_STUDENTE", typeof(string));
            result.Columns.Add("STATO_CHIUDIBILITA", typeof(string));
            result.Columns.Add("OGGETTO_RICHIESTO", typeof(string));
            result.Columns.Add("AA_COERENZA", typeof(string));
            result.Columns.Add("AMBITI_RICHIESTI", typeof(string));
            result.Columns.Add("ESITO_CONTROLLI_CHIUSURA", typeof(string));
            result.Columns.Add("CONDIZIONE_RISOLTA", typeof(string));
            result.Columns.Add("MOTIVO_PRIORITA", typeof(string));
            result.Columns.Add("DECISIONE_PROPOSTA", typeof(string));
            result.Columns.Add("MOTIVO_DECISIONE", typeof(string));
            result.Columns.Add("TICKET_RIGUARDA_BLOCCHI", typeof(string));
            result.Columns.Add("STUDENTE_HA_BLOCCHI", typeof(string));
            result.Columns.Add("BLOCCHI_RILEVANTI", typeof(string));
            result.Columns.Add("BLOCCHI_NON_PERTINENTI", typeof(string));
            result.Columns.Add("CONTRADDIZIONI_RILEVATE", typeof(string));
            result.Columns.Add("ARGOMENTI_SECONDARI", typeof(string));
            result.Columns.Add("INTENTO_RICHIESTA", typeof(string));
            result.Columns.Add("SEGNALI_INTENTO", typeof(string));
            result.Columns.Add("ANNO_ACCADEMICO_RICHIESTA", typeof(string));
            result.Columns.Add("CATEGORIA_DOMANDE_STUDENTE", typeof(string));
            result.Columns.Add("ANNI_DOMANDE_RILEVATI", typeof(string));
            result.Columns.Add("AA_RECORD_OPERATIVO", typeof(string));
            result.Columns.Add("CRITERIO_SELEZIONE_RECORD", typeof(string));
            result.Columns.Add("STATO_DOMANDA_INTERPRETATO", typeof(string));
            result.Columns.Add("CONFIDENZA_ARGOMENTO", typeof(string));
            result.Columns.Add("CONFIDENZA_INTENTO", typeof(string));
            result.Columns.Add("CONFIDENZA_DATI_OPERATIVI", typeof(string));
            result.Columns.Add("CONFIDENZA_DECISIONE", typeof(string));
            result.Columns.Add("CODFISC", typeof(string));
            result.Columns.Add("CODSTUD", typeof(string));
            result.Columns.Add("STATO_LAVORAZIONE", typeof(string));
            result.Columns.Add("DECISIONE_OPERATORE", typeof(string));
            result.Columns.Add("ASSEGNATARIO", typeof(string));
            result.Columns.Add("NOTA_OPERATORE", typeof(string));
            result.Columns.Add("DATA_LAVORAZIONE", typeof(DateTime));
            result.Columns.Add("ESITO_INVIATO", typeof(string));
            return result;
        }

        private static void AddMetric(DataTable table, string section, string indicator, int value)
        {
            DataRow row = table.NewRow();
            row["SEZIONE"] = section;
            row["INDICATORE"] = indicator;
            row["VALORE"] = value;
            table.Rows.Add(row);
        }

        private static void AddRule(DataTable table, string code, string caseDescription, string decision, string action, string response)
        {
            DataRow row = table.NewRow();
            row["CODICE_RISPOSTA"] = code;
            row["CASO"] = caseDescription;
            row["DECISIONE_PROPOSTA"] = decision;
            row["AZIONE_RICHIESTA"] = action;
            row["RISCONTRO_BASE"] = response;
            table.Rows.Add(row);
        }

        private static int CountByPrefix(DataTable table, string columnName, string prefix) => table.AsEnumerable().Count(row =>
            ReadString(row, columnName).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        private static int CountExact(DataTable table, string columnName, string expected) => table.AsEnumerable().Count(row =>
            string.Equals(ReadString(row, columnName), expected, StringComparison.OrdinalIgnoreCase));

        private static int CountNotEmpty(DataTable table, string columnName) => table.AsEnumerable().Count(row =>
            !string.IsNullOrWhiteSpace(ReadString(row, columnName)));

        private static int CountContains(DataTable table, string columnName, string expected) => table.AsEnumerable().Count(row =>
            ReadString(row, columnName).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0);

        private static int CountPaymentResolved(DataTable table) => table.AsEnumerable().Count(row =>
        {
            string condition = ReadString(row, "CONDIZIONE_RISOLTA");
            return string.Equals(condition, "PAGAMENTO_COMPLETO_STESSO_AA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(condition, "PRIMA_RATA_BORSA_PAGATA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(condition, "SALDO_BORSA_PAGATO", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(condition, "INTEGRAZIONE_BORSA_PAGATA", StringComparison.OrdinalIgnoreCase);
        });

        private static void AddIfNotEmpty(List<string> values, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                values.Add($"{label}: {value}");
        }

        private static string JoinEvidence(params string[] values) =>
            string.Join(
                " | ",
                values.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()));

        private static string GetTopicLabel(string topicCode) =>
            TicketTopicModuleRegistry.Resolve(topicCode).Label;

        // L'instradamento ai fogli operativi dipende unicamente da ARGOMENTO_PRIMARIO.
        // Argomenti secondari, ambiti risolutivi e risultati dei moduli possono influire
        // sulla decisione, ma non possono spostare il ticket in un altro foglio.

        private static string GetDetailSheet(string topicCode) =>
            TicketTopicModuleRegistry.Resolve(topicCode).DetailSheet;

        private static DateTime? ReadDate(DataRow row, params string[] candidates)
        {
            foreach (string columnName in candidates)
            {
                if (!row.Table.Columns.Contains(columnName))
                    continue;
                object value = row[columnName];
                if (value == null || value == DBNull.Value)
                    continue;
                if (value is DateTime dateValue)
                    return dateValue;
                string text = Convert.ToString(value, ItalianCulture)?.Trim() ?? string.Empty;
                if (DateTime.TryParse(text, ItalianCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed) ||
                    DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        private static bool IsYes(DataRow row, string columnName)
        {
            string value = ReadString(row, columnName);
            return string.Equals(value, "SI", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(DataRow row, string columnName)
        {
            if (!row.Table.Columns.Contains(columnName))
                return string.Empty;
            object value = row[columnName];
            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value, ItalianCulture)?.Trim() ?? string.Empty;
        }

        private static string FormatAcademicYear(int? value)
        {
            if (!value.HasValue)
                return string.Empty;
            string raw = value.Value.ToString(CultureInfo.InvariantCulture);
            return raw.Length == 8 ? $"{raw[..4]}/{raw[4..]}" : raw;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value[..Math.Max(0, maxLength - 3)] + "...";
        }

        private sealed class OperationalDecision
        {
            public string Decision { get; init; } = string.Empty;
            public string RequiredAction { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string ResponseCode { get; init; } = string.Empty;
            public string ResponseText { get; init; } = string.Empty;
        }

        private sealed class ResolutionAssessment
        {
            public static ResolutionAssessment None { get; } = new();

            public bool HasRequestedConditions { get; init; }
            public bool IsResolved { get; init; }
            public string RequestedScopes { get; init; } = string.Empty;
            public string CheckSummary { get; init; } = string.Empty;
            public string UnresolvedConditions { get; init; } = string.Empty;
            public string Condition { get; init; } = string.Empty;
            public string ResponseCode { get; init; } = string.Empty;
            public string RequiredAction { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string Evidence { get; init; } = string.Empty;
        }

        private sealed class QueueItem
        {
            public string TicketId { get; init; } = string.Empty;
            public string FiscalCode { get; init; } = string.Empty;
            public string StudentCode { get; init; } = string.Empty;
            public string Subject { get; init; } = string.Empty;
            public string StudentMessage { get; init; } = string.Empty;
            public string Topic { get; init; } = string.Empty;
            public string SecondaryTopics { get; init; } = string.Empty;
            public string Intent { get; init; } = string.Empty;
            public string IntentSignals { get; init; } = string.Empty;
            public string RequestedAcademicYear { get; init; } = string.Empty;
            public string StudentApplicationCategory { get; init; } = string.Empty;
            public string ParticipatedAcademicYears { get; init; } = string.Empty;
            public string OperationalAcademicYear { get; init; } = string.Empty;
            public string RecordSelectionCriterion { get; init; } = string.Empty;
            public string ApplicationState { get; init; } = string.Empty;
            public string TopicConfidence { get; init; } = string.Empty;
            public string IntentConfidence { get; init; } = string.Empty;
            public string OperationalDataConfidence { get; init; } = string.Empty;
            public string DecisionConfidence { get; init; } = string.Empty;
            public DateTime? CreationDate { get; init; }
            public DateTime? LastMessageDate { get; init; }
            public int? DaysOpen { get; init; }
            public string Closable { get; init; } = string.Empty;
            public string ClosureStatus { get; init; } = string.Empty;
            public string RequestedObject { get; init; } = string.Empty;
            public string AcademicYearConsistency { get; init; } = string.Empty;
            public string NonClosureReason { get; init; } = string.Empty;
            public string NextCheck { get; init; } = string.Empty;
            public string VerificationGroup { get; init; } = string.Empty;
            public string RequestedScopes { get; init; } = string.Empty;
            public string ClosureChecks { get; init; } = string.Empty;
            public string ResolvedCondition { get; init; } = string.Empty;
            public string Priority { get; init; } = string.Empty;
            public int PriorityOrder { get; init; }
            public string PriorityReason { get; init; } = string.Empty;
            public string Decision { get; init; } = string.Empty;
            public string RequiredAction { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
            public string ResponseCode { get; init; } = string.Empty;
            public string ResponseText { get; init; } = string.Empty;
            public string Evidence { get; init; } = string.Empty;
            public bool TicketMentionsBlocks { get; init; }
            public bool StudentHasBlocks { get; init; }
            public string RelevantBlocks { get; init; } = string.Empty;
            public string NonRelevantBlocks { get; init; } = string.Empty;
            public string Contradictions { get; init; } = string.Empty;
            public string DetailSheet { get; init; } = string.Empty;
        }
    }
}
