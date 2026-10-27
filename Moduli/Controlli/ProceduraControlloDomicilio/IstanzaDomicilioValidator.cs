using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>
    /// Read-only projection of the complete request. Rules mirror the portal's
    /// IstanzaDomicilioLavorazioneRules/Periods; no source record is modified.
    /// </summary>
    internal static class IstanzaDomicilioValidator
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly string[] Enti = { "EP", "IR", "FA", "SE" };

        public static void Validate(IstanzaDomicilioRecord request,
            IReadOnlyDictionary<string, IstanzaDomicilioComune> comuni, DateTime today)
        {
            var operations = request.Operazioni.OrderBy(x => x.Ordine).ThenBy(x => x.Id).ToList();
            if (operations.Count == 0)
            {
                Error(request.State, "IST_001", "L'istanza non contiene operazioni.");
                return;
            }
            if (operations.Any(x => x.Id <= 0 || x.Ordine <= 0) ||
                operations.Select(x => x.Id).Distinct().Count() != operations.Count ||
                operations.Select(x => x.Ordine).Distinct().Count() != operations.Count)
            {
                Error(request.State, "IST_002", "Identificativi o ordine delle operazioni non validi o duplicati.");
                return;
            }

            var byId = operations.ToDictionary(x => x.Id);
            var domicili = request.DomiciliCorrenti.Select(Clone).ToList();
            var projectedByOperation = new Dictionary<int, object>();
            var payloads = new Dictionary<int, object>();
            var targets = new HashSet<string>();
            var series = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var op in operations)
            {
                if (op.Entita is < 1 or > 3 || op.Operazione is < 1 or > 3)
                {
                    Error(op.State, "IST_003", "Tipo di entità o operazione non valido.");
                    continue;
                }
                if (op.IdOperazionePadre.HasValue &&
                    (!byId.TryGetValue(op.IdOperazionePadre.Value, out var parent) || parent.Ordine >= op.Ordine ||
                     parent.Operazione != 1 || op.Operazione != 1 ||
                     !(op.Entita == 2 && parent.Entita == 1 || op.Entita == 3 && parent.Entita == 2)))
                {
                    Error(op.State, "IST_004", "Il collegamento con l'operazione padre non è valido.");
                    continue;
                }

                var sourceDomicile = request.DomiciliCorrenti.FirstOrDefault(x => x.Id == op.IdDomicilio);
                var sourceContract = sourceDomicile?.Contratti.FirstOrDefault(x => x.Id == op.IdContratto);
                var sourceExtension = sourceContract?.Proroghe.FirstOrDefault(x => x.Id == op.IdProroga);
                if (sourceDomicile != null &&
                    (sourceDomicile.NumDomanda != request.NumDomanda || sourceDomicile.AnnoAccademico != request.AnnoAccademico ||
                     !string.Equals(sourceDomicile.CodFiscale, request.CodFiscale, StringComparison.OrdinalIgnoreCase)))
                    Error(op.State, "IST_005", "Il dato originario non appartiene alla domanda dello studente.");
                if (op.Entita == 1 && op.Operazione != 1 && sourceDomicile == null ||
                    op.Entita == 2 && !op.IdOperazionePadre.HasValue && sourceDomicile == null ||
                    op.Entita == 2 && op.Operazione != 1 && sourceContract == null ||
                    op.Entita == 3 && !op.IdOperazionePadre.HasValue && sourceContract == null ||
                    op.Entita == 3 && op.Operazione == 2 && sourceExtension == null)
                    Error(op.State, "IST_005", "Il dato originario non è più disponibile per questa domanda.");
                ValidateVersion(op, sourceDomicile, sourceContract, sourceExtension);
                if (op.Operazione != 1)
                {
                    var id = op.Entita == 1 ? op.IdDomicilio : op.Entita == 2 ? op.IdContratto : op.IdProroga;
                    if (!targets.Add($"{op.Entita}:{id}"))
                        Error(op.State, "IST_007", "Sono presenti più modifiche incompatibili allo stesso dato.");
                }

                try
                {
                    switch (op.Entita)
                    {
                        case 1:
                        {
                            var p = Read<DomicilioPayload>(op);
                            payloads[op.Id] = p;
                            ValidateDomicile(op, p, sourceDomicile, operations, comuni, addresses);
                            var d = op.Operazione == 1 ? new DomicilioControlRecord
                            {
                                Id = -op.Id, AnnoAccademico = request.AnnoAccademico, NumDomanda = request.NumDomanda,
                                CodFiscale = request.CodFiscale, Studente = request.Studente
                            } : domicili.FirstOrDefault(x => x.Id == op.IdDomicilio);
                            if (d == null) break;
                            if (op.Operazione != 3)
                            {
                                d.CodComune = p.CodComuneDomicilio ?? "";
                                d.CodProvincia = p.CodProvinciaDomicilio ?? "";
                                d.Comune = comuni.TryGetValue(d.CodComune, out var comune) ? comune.Descrizione : "";
                                d.Indirizzo = p.IndirizzoDomicilio ?? "";
                                d.NumeroCivico = p.NumCivicoDomicilio ?? "";
                                d.Cap = p.CapDomicilio ?? "";
                                d.TipoDomicilio = p.TipoTitoloDomicilio?.ToString(CultureInfo.InvariantCulture) ?? "";
                                d.DataInizio = p.DataInizio;
                                d.InserimentoDatiContratto = p.InserimentoDatiContratto ?? false;
                            }
                            d.DataFine = p.DataFine;
                            if (op.Operazione == 1) domicili.Add(d);
                            projectedByOperation[op.Id] = d;
                            op.DatiProposti = $"Comune={d.Comune} ({d.CodComune}); Provincia={d.CodProvincia}; Indirizzo={d.Indirizzo} {d.NumeroCivico}; CAP={d.Cap}; Titolo={d.TitoloDescrizione}; Inizio={Date(d.DataInizio)}; Fine={Date(d.DataFine)}";
                            break;
                        }
                        case 2:
                        {
                            var p = Read<ContrattoPayload>(op);
                            payloads[op.Id] = p;
                            ValidateContract(op, p, sourceContract, series, today);
                            var d = op.IdOperazionePadre.HasValue
                                ? projectedByOperation.GetValueOrDefault(op.IdOperazionePadre.Value) as DomicilioControlRecord
                                : domicili.FirstOrDefault(x => x.Id == op.IdDomicilio);
                            if (d == null)
                            {
                                Error(op.State, "IST_005", "Il domicilio risultante del contratto non è disponibile.");
                                break;
                            }
                            var c = op.Operazione == 1 ? new ContrattoControlRecord { Id = -op.Id, IdDomicilio = d.Id }
                                : d.Contratti.FirstOrDefault(x => x.Id == op.IdContratto);
                            if (c == null) break;
                            // The backend treats subentry/cessation payloads as partial updates.
                            if (op.Operazione == 1 || p.TipoContrattoTitoloOneroso.HasValue)
                            {
                                c.TipoContratto = p.TipoContrattoTitoloOneroso ?? -1;
                                c.NumeroSerie = p.NumeroSerieContratto ?? "";
                                c.DataRegistrazione = p.DataRegistrazioneContratto;
                                c.DataInizio = p.DataInizioContratto ?? default;
                                c.DataFine = p.DataFineContratto;
                                c.TipoEnte = p.TipoEnte is >= 0 and < 4 ? Enti[p.TipoEnte.Value] : "";
                                c.DenominazioneEnte = p.DenominazioneEnte ?? "";
                                c.ImportoRata = p.ImportoMensileRata?.ToString(CultureInfo.InvariantCulture) ?? "";
                            }
                            if (p.AddSubentro)
                            {
                                c.NumeroSerieSubentro = p.NumeroSerieSubentro ?? "";
                                c.DataInizioSubentro = p.DataInizioSubentro;
                            }
                            if (p.AddCessazione || p.Chiusura || op.Operazione == 3) c.DataCessazione = p.DataCessazione;
                            if (op.Operazione == 1) d.Contratti.Add(c);
                            projectedByOperation[op.Id] = c;
                            op.DatiProposti = $"Tipo={c.TipoDescrizione}; Serie={c.NumeroSerie}; Registrazione={Date(c.DataRegistrazione)}; Inizio={Date(c.DataInizio)}; Fine={Date(c.DataFine)}; Subentro={c.NumeroSerieSubentro}; Data subentro={Date(c.DataInizioSubentro)}; Cessazione={Date(c.DataCessazione)}; Ente={c.TipoEnte} {c.DenominazioneEnte}; Rata={c.ImportoRata}";
                            break;
                        }
                        case 3:
                        {
                            var p = Read<ProrogaPayload>(op);
                            payloads[op.Id] = p;
                            ValidateExtension(op, p, sourceContract, operations, series);
                            var c = op.IdOperazionePadre.HasValue
                                ? projectedByOperation.GetValueOrDefault(op.IdOperazionePadre.Value) as ContrattoControlRecord
                                : domicili.FirstOrDefault(x => x.Id == op.IdDomicilio)?.Contratti.FirstOrDefault(x => x.Id == op.IdContratto);
                            if (c == null)
                            {
                                Error(op.State, "IST_005", "Il contratto risultante della proroga non è disponibile.");
                                break;
                            }
                            if (!c.IsLocazione || c.DataCessazione.HasValue)
                                Error(op.State, "IST_PRO_03", "Il contratto risultante non consente l'inserimento o la modifica di proroghe.");
                            var e = op.Operazione == 1 ? new ProrogaControlRecord { Id = -op.Id, IdContratto = c.Id }
                                : c.Proroghe.FirstOrDefault(x => x.Id == op.IdProroga);
                            if (e == null) break;
                            e.NumeroSerie = p.NumeroSerieProroga ?? "";
                            e.DataDecorrenza = p.DataDecorrenza ?? default;
                            e.DataScadenza = p.DataScadenza ?? default;
                            if (op.Operazione == 1) c.Proroghe.Add(e);
                            projectedByOperation[op.Id] = e;
                            op.DatiProposti = $"Serie={e.NumeroSerie}; Decorrenza={Date(e.DataDecorrenza)}; Scadenza={Date(e.DataScadenza)}; Contratto={c.NumeroSerie}";
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is JsonException || ex is NotSupportedException || ex is InvalidOperationException)
                {
                    Error(op.State, "IST_008", "I dati JSON salvati non sono leggibili; l'operazione non può essere applicata automaticamente.");
                }
            }

            foreach (var op in operations.Where(x => x.Entita != 1 && x.IdDomicilio.HasValue))
                if (operations.Any(x => x.IdDomicilio == op.IdDomicilio && x.Entita == 1 && x.Operazione == 3))
                    Error(op.State, "IST_009", "Non è possibile modificare contratti o proroghe di un domicilio chiuso nella stessa istanza.");

            // Evaluate the complete resulting hierarchy, rather than comparing each payload with stale data.
            ValidateResult(request, domicili, projectedByOperation, operations);
            foreach (var op in operations)
            {
                AddDocuments(op, request.CodFiscale, domicili, projectedByOperation, payloads);
                var declared = new[]
                {
                    (op.IdAllegatoContratto, "CONTRATTO"), (op.IdAllegatoSubentro, "SUBENTRO"),
                    (op.IdAllegatoProroga, "PROROGA"), (op.IdAllegatoLearningAgreement, "LEARNING_AGREEMENT")
                };
                if (declared.Any(x => x.Item1.HasValue && !op.Documenti.Any(d => d.IdAllegato == x.Item1 && d.Tipo == x.Item2)))
                    Warning(op.State, "IST_DOC_07", "L'operazione contiene allegati proposti non pertinenti ai documenti previsti; verificarli prima della lavorazione.");
            }
            foreach (var op in operations) request.State.Merge(op.State);
            request.ValidazioneCompletata = true;
        }

        private static void ValidateVersion(IstanzaDomicilioOperation op, DomicilioControlRecord? d,
            ContrattoControlRecord? c, ProrogaControlRecord? e)
        {
            DateTime? validita;
            DateTime? modifica;
            if (op.Operazione != 1)
            {
                validita = op.Entita == 1 ? d?.DataValidita : op.Entita == 2 ? c?.DataValidita : e?.DataValidita;
                modifica = op.Entita == 1 ? d?.DataModifica : op.Entita == 2 ? c?.DataModifica ?? d?.DataModifica : e?.DataModifica ?? c?.DataModifica;
            }
            else if (!op.IdOperazionePadre.HasValue && op.Entita != 1)
            {
                validita = op.Entita == 2 ? d?.DataValidita : c?.DataValidita;
                modifica = op.Entita == 2 ? d?.DataModifica : c?.DataModifica;
            }
            else return;
            if (!op.DataValiditaOrigine.HasValue || validita != op.DataValiditaOrigine || modifica != op.DataModificaOrigine)
                Error(op.State, "IST_006", "I dati registrati o il riferimento sono cambiati dopo la preparazione dell'istanza: verificare la versione corrente.");
        }

        private static void ValidateDomicile(IstanzaDomicilioOperation op, DomicilioPayload p,
            DomicilioControlRecord? source, List<IstanzaDomicilioOperation> operations,
            IReadOnlyDictionary<string, IstanzaDomicilioComune> comuni, HashSet<string> addresses)
        {
            Dates(op, p.DataInizio, p.DataFine);
            if (op.Operazione == 3)
            {
                if (!p.Chiusura || !p.DataFine.HasValue || source?.DataInizio > p.DataFine)
                    Error(op.State, "IST_DOM_01", "La chiusura richiede una data di fine coerente con l'inizio del domicilio.");
                return;
            }
            if (p.Chiusura) Error(op.State, "IST_DOM_01", "La chiusura non corrisponde al tipo di operazione.");
            if (p.TipoTitoloDomicilio is not (0 or 1)) Error(op.State, "IST_DOM_02", "Titolo del domicilio non valido.");
            Text(op, p.IndirizzoDomicilio, 75, "Indirizzo");
            Text(op, p.NumCivicoDomicilio, 40, "Numero civico");
            if (!Regex.IsMatch(p.NumCivicoDomicilio ?? "", @"^[A-Za-z0-9 ]{1,40}$"))
                Error(op.State, "IST_DOM_03", "Il numero civico può contenere lettere, numeri e spazi.");
            if (!Regex.IsMatch(p.CapDomicilio ?? "", @"^\d{5}$")) Error(op.State, "IST_DOM_04", "Il CAP deve contenere cinque cifre.");
            if (!comuni.TryGetValue(p.CodComuneDomicilio ?? "", out var comune) || comune.CodProvincia != p.CodProvinciaDomicilio)
                Error(op.State, "IST_DOM_05", "Provincia e comune non corrispondono o non sono presenti in anagrafica.");
            if (p.TipoTitoloDomicilio == 0)
            {
                if (!p.DataInizio.HasValue || !p.DataFine.HasValue || p.DataFine <= p.DataInizio)
                    Error(op.State, "IST_DOM_06", "Il domicilio gratuito richiede un periodo completo e valido.");
                if (operations.Any(x => x.IdOperazionePadre == op.Id && x.Entita == 2))
                    Error(op.State, "IST_DOM_07", "Il domicilio gratuito non può avere un nuovo contratto.");
                if (source?.Contratti.Any(c => !c.DataCessazione.HasValue && !operations.Any(x => x.IdContratto == c.Id && IsCessation(x))) == true)
                    Error(op.State, "IST_DOM_07", "Prima di rendere gratuito il domicilio occorre cessare tutti i contratti attivi nella stessa istanza.");
            }
            else if (op.Operazione == 1 && !operations.Any(x => x.IdOperazionePadre == op.Id && x.Entita == 2))
                Error(op.State, "IST_DOM_08", "Manca il contratto collegato al nuovo domicilio oneroso.");
            if (!addresses.Add(Address(p.CodComuneDomicilio, p.IndirizzoDomicilio, p.NumCivicoDomicilio, p.CapDomicilio)))
                Error(op.State, "IST_DOM_09", "Lo stesso domicilio è presente più volte nella richiesta.");
        }

        private static void ValidateContract(IstanzaDomicilioOperation op, ContrattoPayload p,
            ContrattoControlRecord? source, HashSet<string> series, DateTime today)
        {
            Dates(op, p.DataRegistrazioneContratto, p.DataInizioContratto, p.DataFineContratto, p.DataInizioSubentro, p.DataCessazione);
            if (source?.DataCessazione != null) Error(op.State, "IST_CON_01", "Il contratto risulta già cessato.");
            if (p.AddCessazione || p.Chiusura || op.Operazione == 3)
            {
                if (!p.AddCessazione || !p.DataCessazione.HasValue || op.Operazione == 1)
                    Error(op.State, "IST_CON_02", "La cessazione richiede un contratto esistente e una data valida.");
                return;
            }
            var full = op.Operazione == 1 || p.TipoContrattoTitoloOneroso.HasValue;
            if (full)
            {
                if (op.Operazione == 1 && !op.IdAllegatoContratto.HasValue)
                    Error(op.State, "IST_DOC_01", "Manca il PDF del contratto o della registrazione presso l'Agenzia delle Entrate.");
                if (p.TipoContrattoTitoloOneroso == 1 && p.TipoEnte == 3 &&
                    !op.IdAllegatoLearningAgreement.HasValue && source?.AllegatiLearningAgreement.Count is not > 0)
                    Error(op.State, "IST_DOC_02", "Manca il Learning Agreement in formato PDF.");
                if (op.IdAllegatoLearningAgreement.HasValue && (p.TipoContrattoTitoloOneroso != 1 || p.TipoEnte != 3))
                    Error(op.State, "IST_DOC_03", "Il Learning Agreement è previsto soltanto per Erasmus/Socrates.");
                if (p.TipoContrattoTitoloOneroso is not (0 or 1)) Error(op.State, "IST_CON_03", "Il tipo di contratto non è valido.");
                if (!p.DataInizioContratto.HasValue || !p.DataFineContratto.HasValue || p.DataFineContratto <= p.DataInizioContratto)
                    Error(op.State, "IST_CON_04", "Il periodo contrattuale non è completo o valido.");
                if (p.TipoContrattoTitoloOneroso == 0)
                {
                    Serie(op, p.NumeroSerieContratto, 100, series, "contratto");
                    if (!p.DataRegistrazioneContratto.HasValue || p.DataRegistrazioneContratto.Value.Date > today.Date)
                        Error(op.State, "IST_CON_05", "La data di registrazione è obbligatoria e non può essere futura.");
                }
                else
                {
                    if (p.TipoEnte is not (>= 0 and <= 3)) Error(op.State, "IST_CON_06", "Selezionare un tipo di ente valido.");
                    Text(op, p.DenominazioneEnte, 60, "Denominazione ente");
                    if (p.ImportoMensileRata is not > 0) Error(op.State, "IST_CON_07", "La rata mensile deve essere maggiore di zero.");
                    if (p.AddSubentro) Error(op.State, "IST_CON_08", "Il subentro è previsto soltanto per un contratto di locazione.");
                }
            }
            else if (!p.AddSubentro) Error(op.State, "IST_CON_09", "I dati della modifica contrattuale sono incompleti.");
            if (p.AddSubentro)
            {
                Serie(op, p.NumeroSerieSubentro, 100, series, "subentro");
                if (!p.DataInizioSubentro.HasValue || !op.IdAllegatoSubentro.HasValue)
                    Error(op.State, "IST_CON_10", "Il subentro richiede data e documento PDF.");
                if (!full && source?.IsLocazione == false)
                    Error(op.State, "IST_CON_08", "Il subentro è previsto soltanto per un contratto di locazione.");
            }
        }

        private static void ValidateExtension(IstanzaDomicilioOperation op, ProrogaPayload p,
            ContrattoControlRecord? source, List<IstanzaDomicilioOperation> operations, HashSet<string> series)
        {
            Dates(op, p.DataDecorrenza, p.DataScadenza);
            if (op.Operazione == 3) Error(op.State, "IST_PRO_01", "Una proroga registrata non può essere annullata.");
            if (p.Chiusura || !p.DataDecorrenza.HasValue || !p.DataScadenza.HasValue || p.DataScadenza <= p.DataDecorrenza)
                Error(op.State, "IST_PRO_02", "Il periodo della proroga non è valido.");
            Serie(op, p.NumeroSerieProroga, 50, series, "proroga");
            if (op.Operazione == 1 && !op.IdAllegatoProroga.HasValue) Error(op.State, "IST_DOC_04", "Manca il PDF della proroga.");
            if (source != null && (!source.IsLocazione || source.DataCessazione.HasValue || operations.Any(x => x.IdContratto == source.Id && IsCessation(x))))
                Error(op.State, "IST_PRO_03", "Non è possibile intervenire sulle proroghe di un contratto non di locazione, cessato o in cessazione.");
        }

        private static void ValidateResult(IstanzaDomicilioRecord request, List<DomicilioControlRecord> domicili,
            Dictionary<int, object> projected, List<IstanzaDomicilioOperation> operations)
        {
            var affected = projected.ToDictionary(x => x.Key, x => operations.Single(o => o.Id == x.Key));
            void Issue(object target, string code, string description, bool warning = false)
            {
                var related = projected.Where(x => ReferenceEquals(x.Value, target)).Select(x => affected[x.Key]).ToList();
                if (related.Count == 0) Add(request.State, code, description, warning);
                else foreach (var operation in related) Add(operation.State, code, description, warning);
            }
            foreach (var group in domicili.Where(d => !operations.Any(o => o.Entita == 1 && o.Operazione == 3 && o.IdDomicilio == d.Id))
                         .GroupBy(d => Address(d.CodComune, d.Indirizzo, d.NumeroCivico, d.Cap)).Where(g => g.Count() > 1))
                if (group.Any(d => projected.Values.Contains(d)))
                    foreach (var d in group.Where(d => projected.Values.Contains(d))) Issue(d, "IST_DOM_09", "Il domicilio proposto coincide con un altro domicilio registrato.");

            var allContracts = new List<(ContrattoControlRecord Contract, DateTime End)>();
            foreach (var d in domicili)
            {
                foreach (var c in d.Contratti)
                {
                    var label = $"contratto {c.NumeroSerie} (domicilio {d.Id})";
                    var cAffected = projected.Values.Contains(c) || c.Proroghe.Any(e => projected.Values.Contains(e)) ||
                        operations.Any(o => o.Entita == 1 && o.Operazione == 3 && o.IdDomicilio == d.Id);
                    if (!c.DataFine.HasValue)
                    {
                        Issue(c, "IST_PER_01", $"Il {label} non contiene una scadenza valida.");
                        continue;
                    }
                    var naturalEnd = c.Proroghe.Select(e => e.DataScadenza.Date).Append(c.DataFine.Value.Date).Max();
                    var effectiveEnd = c.DataCessazione?.Date ?? naturalEnd;
                    var domicileClosed = operations.Any(o => o.Entita == 1 && o.Operazione == 3 && o.IdDomicilio == d.Id);
                    if (domicileClosed && d.DataFine.HasValue && d.DataFine.Value.Date < effectiveEnd) effectiveEnd = d.DataFine.Value.Date;
                    if (effectiveEnd >= c.EffectiveStart) allContracts.Add((c, effectiveEnd));
                    if (c.DataCessazione.HasValue && !cAffected) continue;
                    if (c.DataFine <= c.DataInizio) Issue(c, "IST_PER_02", $"Il {label} deve avere scadenza successiva alla decorrenza.");
                    if (c.DataInizioSubentro.HasValue && (c.DataInizioSubentro.Value.Date < c.DataInizio.Date || c.DataInizioSubentro.Value.Date > naturalEnd))
                        Issue(c, "IST_PER_03", $"Il subentro del {label} deve essere compreso tra decorrenza e ultima scadenza.");
                    if (c.DataCessazione.HasValue && (c.DataCessazione.Value.Date < c.EffectiveStart || c.DataCessazione.Value.Date > naturalEnd))
                        Issue(c, "IST_PER_04", $"La cessazione del {label} deve essere compresa tra decorrenza effettiva e ultima scadenza.");
                    if (effectiveEnd < c.EffectiveStart) Issue(c, "IST_PER_05", $"Il periodo effettivo del {label} non è valido.");
                    if (!c.DataCessazione.HasValue && !domicileClosed)
                    {
                        var previousEnd = c.DataFine.Value.Date;
                        object previous = c;
                        foreach (var e in c.Proroghe.OrderBy(x => x.DataDecorrenza).ThenBy(x => x.DataScadenza))
                        {
                            if (e.DataScadenza <= e.DataDecorrenza) { Issue(e, "IST_PER_06", "La proroga deve avere scadenza successiva alla decorrenza."); continue; }
                            if ((previousEnd - e.DataDecorrenza.Date).TotalDays > 10)
                                Issue(e, "IST_PER_07", $"La proroga {e.NumeroSerie} si sovrappone per più di 10 giorni alla scadenza precedente del {label}.");
                            if (e.DataScadenza.Date <= previousEnd)
                                Issue(e, "IST_PER_08", $"La proroga {e.NumeroSerie} non estende la scadenza precedente del {label}.");
                            var gap = (e.DataDecorrenza.Date - previousEnd).Days - 1;
                            if (gap > 0 && (projected.Values.Contains(e) || projected.Values.Contains(previous)))
                                Issue(projected.Values.Contains(e) ? e : previous, "IST_PER_09", $"La proroga {e.NumeroSerie} lascia {gap} giorni non coperti dopo il {previousEnd:dd/MM/yyyy}.", true);
                            if (e.DataScadenza.Date > previousEnd) previousEnd = e.DataScadenza.Date;
                            previous = e;
                        }
                    }
                    var baseSerie = NormalizeSerie(c.NumeroSerie);
                    var subSerie = NormalizeSerie(c.NumeroSerieSubentro);
                    if (projected.Values.Contains(c) && baseSerie.Length > 0 && subSerie.Length > 0 &&
                        (baseSerie.Contains(subSerie) || subSerie.Contains(baseSerie)))
                        Issue(c, "IST_SER_03", "Gli estremi del subentro devono essere diversi da quelli del contratto.");
                    foreach (var other in d.Contratti.Where(x => x != c))
                        if (projected.Values.Contains(c) && new[] { baseSerie, subSerie }.Any(s => s.Length > 0 &&
                            (s == NormalizeSerie(other.NumeroSerie) || s == NormalizeSerie(other.NumeroSerieSubentro))))
                            Issue(c, "IST_SER_04", "Il numero di serie coincide con un altro contratto o subentro dello stesso domicilio.");
                    foreach (var e in c.Proroghe.Where(x => projected.Values.Contains(x)))
                        if (c.Proroghe.Any(other => other != e && NormalizeSerie(other.NumeroSerie) == NormalizeSerie(e.NumeroSerie)))
                            Issue(e, "IST_SER_05", "Esiste già una proroga con lo stesso numero di serie.");
                }
            }
            for (var i = 0; i < allContracts.Count; i++)
                for (var j = i + 1; j < allContracts.Count; j++)
                {
                    var a = allContracts[i]; var b = allContracts[j];
                    var start = a.Contract.EffectiveStart > b.Contract.EffectiveStart ? a.Contract.EffectiveStart : b.Contract.EffectiveStart;
                    var end = a.End < b.End ? a.End : b.End;
                    if ((end - start).TotalDays > 10)
                        Error(request.State, "IST_PER_10", $"I contratti {a.Contract.NumeroSerie} e {b.Contract.NumeroSerie} si sovrappongono per più di 10 giorni ({start:dd/MM/yyyy}–{end:dd/MM/yyyy}). Verificare le date o la cessazione del contratto precedente.");
                }
        }

        private static void AddDocuments(IstanzaDomicilioOperation op, string cf, List<DomicilioControlRecord> domicili,
            Dictionary<int, object> projected, Dictionary<int, object> payloads)
        {
            if (!projected.TryGetValue(op.Id, out var target) || !payloads.TryGetValue(op.Id, out var payload)) return;
            if (target is ContrattoControlRecord c && payload is ContrattoPayload p && !IsCessation(op))
            {
                var d = domicili.First(x => x.Contratti.Contains(c));
                var full = op.Operazione == 1 || p.TipoContrattoTitoloOneroso.HasValue;
                if (full) AddContractDocuments(op, cf, d, c, true, p.AddSubentro);
                else if (p.AddSubentro) AddDocument(op, "SUBENTRO", op.IdAllegatoSubentro,
                    new(DomicilioPdfDocumentType.Subentro, cf, c.NumeroSerieSubentro, null, c.DataInizioSubentro, null, c.NumeroSerie), null);
            }
            else if (target is ProrogaControlRecord e)
            {
                var c2 = domicili.SelectMany(x => x.Contratti).First(x => x.Proroghe.Contains(e));
                AddDocument(op, "PROROGA", op.IdAllegatoProroga,
                    new(DomicilioPdfDocumentType.Proroga, cf, e.NumeroSerie, null, e.DataDecorrenza, e.DataScadenza, c2.NumeroSerie),
                    op.Operazione == 2 ? e.Allegati : null);
            }
            else if (target is DomicilioControlRecord d && op.Operazione == 2)
            {
                // A changed municipality must also agree with the retained contract documents.
                foreach (var c2 in d.Contratti.Where(x => !x.DataCessazione.HasValue))
                {
                    var contractOperationId = projected.FirstOrDefault(x => ReferenceEquals(x.Value, c2)).Key;
                    var contractPayload = payloads.GetValueOrDefault(contractOperationId) as ContrattoPayload;
                    // A full contract operation already verifies its document against this final municipality.
                    if (contractPayload?.TipoContrattoTitoloOneroso.HasValue == true) continue;
                    if (contractPayload?.AddSubentro == true)
                        AddDocument(op, "CONTRATTO", null,
                            c2.IsLocazione ? new(DomicilioPdfDocumentType.Contratto, cf, c2.NumeroSerie, c2.DataRegistrazione, c2.DataInizio, c2.DataFine, null, false, d.Comune)
                                : new(DomicilioPdfDocumentType.ContrattoEnte, cf, null, null, c2.DataInizio, c2.DataFine, null,
                                    ComuneDomicilio: d.Comune, TipoEnte: c2.TipoEnte, DenominazioneEnte: c2.DenominazioneEnte,
                                    ImportoMensileRata: decimal.TryParse(c2.ImportoRata, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var oldAmount) ? oldAmount : null),
                            c2.AllegatiContratto);
                    else AddContractDocuments(op, cf, d, c2, false, false);
                }
            }
        }

        private static void AddContractDocuments(IstanzaDomicilioOperation op, string cf, DomicilioControlRecord d,
            ContrattoControlRecord c, bool proposed, bool newSubentry)
        {
            AddDocument(op, "CONTRATTO", proposed ? op.IdAllegatoContratto : null,
                c.IsLocazione ? new(DomicilioPdfDocumentType.Contratto, cf, c.NumeroSerie, c.DataRegistrazione, c.DataInizio, c.DataFine, null, !c.HasSubentro, d.Comune)
                    : new(DomicilioPdfDocumentType.ContrattoEnte, cf, null, null, c.DataInizio, c.DataFine, null,
                        ComuneDomicilio: d.Comune, TipoEnte: c.TipoEnte, DenominazioneEnte: c.DenominazioneEnte,
                        ImportoMensileRata: decimal.TryParse(c.ImportoRata, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null),
                !proposed || op.Operazione == 2 ? c.AllegatiContratto : null);
            if (c.HasSubentro)
                AddDocument(op, "SUBENTRO", proposed ? op.IdAllegatoSubentro : null,
                    new(DomicilioPdfDocumentType.Subentro, cf, c.NumeroSerieSubentro, null, c.DataInizioSubentro, null, c.NumeroSerie),
                    newSubentry ? null : c.AllegatiSubentro);
            if (c.TipoContratto == 1 && c.TipoEnte == "SE")
                AddDocument(op, "LEARNING_AGREEMENT", proposed ? op.IdAllegatoLearningAgreement : null,
                    new(DomicilioPdfDocumentType.LearningAgreement, cf, null, null, null, null, null),
                    !proposed || op.Operazione == 2 ? c.AllegatiLearningAgreement : null);
        }

        private static void AddDocument(IstanzaDomicilioOperation op, string tipo, decimal? id,
            DomicilioPdfExpectation? expectation, List<DomicilioAttachmentRecord>? stored)
        {
            if (id.HasValue || stored == null || stored.Count == 0)
            {
                op.Documenti.Add(new IstanzaDomicilioDocument { Tipo = tipo, IdAllegato = id, Expectation = expectation });
                if (!id.HasValue) Error(op.State, "IST_DOC_05", $"Il documento {tipo} necessario al controllo non è disponibile.");
                return;
            }
            if (stored.Count > 1) Warning(op.State, "IST_DOC_06", $"Sono presenti più documenti attivi di tipo {tipo}: identificare quello pertinente.");
            foreach (var attachment in stored)
                op.Documenti.Add(new IstanzaDomicilioDocument { Tipo = tipo, IdAllegato = attachment.IdAllegato, Expectation = expectation, Attachment = attachment });
        }

        private static DomicilioControlRecord Clone(DomicilioControlRecord d)
        {
            var clone = new DomicilioControlRecord
            {
                Id = d.Id, DataValidita = d.DataValidita, DataModifica = d.DataModifica, AnnoAccademico = d.AnnoAccademico,
                NumDomanda = d.NumDomanda, CodFiscale = d.CodFiscale, Studente = d.Studente, CodComune = d.CodComune,
                CodProvincia = d.CodProvincia, Comune = d.Comune, Indirizzo = d.Indirizzo, NumeroCivico = d.NumeroCivico,
                Cap = d.Cap, TipoDomicilio = d.TipoDomicilio, DataInizio = d.DataInizio, DataFine = d.DataFine,
                InserimentoDatiContratto = d.InserimentoDatiContratto
            };
            foreach (var c in d.Contratti)
            {
                var copied = new ContrattoControlRecord
                {
                    Id = c.Id, IdDomicilio = c.IdDomicilio, DataValidita = c.DataValidita, DataModifica = c.DataModifica,
                    TipoContratto = c.TipoContratto, NumeroSerie = c.NumeroSerie, DataRegistrazione = c.DataRegistrazione,
                    DataInizio = c.DataInizio, DataFine = c.DataFine, TipoEnte = c.TipoEnte, DenominazioneEnte = c.DenominazioneEnte,
                    ImportoRata = c.ImportoRata, NumeroSerieSubentro = c.NumeroSerieSubentro, DataInizioSubentro = c.DataInizioSubentro,
                    DataCessazione = c.DataCessazione
                };
                copied.AllegatiContratto.AddRange(c.AllegatiContratto.Select(Clone));
                copied.AllegatiSubentro.AddRange(c.AllegatiSubentro.Select(Clone));
                copied.AllegatiLearningAgreement.AddRange(c.AllegatiLearningAgreement.Select(Clone));
                foreach (var e in c.Proroghe)
                {
                    var extension = new ProrogaControlRecord { Id = e.Id, IdContratto = e.IdContratto, DataValidita = e.DataValidita,
                        DataModifica = e.DataModifica, NumeroSerie = e.NumeroSerie, DataDecorrenza = e.DataDecorrenza, DataScadenza = e.DataScadenza };
                    extension.Allegati.AddRange(e.Allegati.Select(Clone));
                    copied.Proroghe.Add(extension);
                }
                clone.Contratti.Add(copied);
            }
            return clone;
        }

        private static DomicilioAttachmentRecord Clone(DomicilioAttachmentRecord a) => new()
        {
            IdAllegato = a.IdAllegato, CodTipoAllegato = a.CodTipoAllegato, CodStatus = a.CodStatus,
            StatusDescription = a.StatusDescription, DataValidita = a.DataValidita, IdContratto = a.IdContratto,
            IdProroga = a.IdProroga, CodFiscale = a.CodFiscale, NumDomanda = a.NumDomanda,
            FileName = a.FileName, ContentType = a.ContentType
        };

        private static T Read<T>(IstanzaDomicilioOperation op) where T : class
            => JsonSerializer.Deserialize<T>(op.PayloadJson, JsonOptions) ?? throw new JsonException("Payload assente.");
        private static bool IsCessation(IstanzaDomicilioOperation op)
        {
            if (op.Entita != 2) return false;
            if (op.Operazione == 3) return true;
            try { var p = Read<ContrattoPayload>(op); return p.AddCessazione || p.Chiusura; }
            catch (JsonException) { return false; }
        }
        private static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        private static void Add(DomicilioControlState state, string code, string description, bool warning)
        { if (warning) Warning(state, code, description); else Error(state, code, description); }
        private static void Error(DomicilioControlState state, string code, string description)
            => state.Add(DomicilioControlSeverity.NonConforme, code, description, "Validare e correggere l'istanza prima dell'accoglimento.");
        private static void Warning(DomicilioControlState state, string code, string description)
            => state.Add(DomicilioControlSeverity.VerificaManuale, code, description, "Valutare manualmente il caso prima dell'accoglimento.");
        private static void Dates(IstanzaDomicilioOperation op, params DateTime?[] dates)
        {
            if (dates.Any(d => d.HasValue && (d.Value.Date < new DateTime(1900, 1, 1) || d.Value.Date > new DateTime(2900, 12, 31))))
                Error(op.State, "IST_DATE", "Le date devono essere comprese tra il 1900 e il 2900.");
        }
        private static void Text(IstanzaDomicilioOperation op, string? value, int max, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) Error(op.State, "IST_TEXT", $"{label} obbligatorio, massimo {max} caratteri.");
        }
        private static string Address(params string?[] values)
            => string.Join("|", values.Select(v => Regex.Replace(Regex.Replace((v ?? "").Trim().ToUpperInvariant(), @"\s+", " "), @"\s*([,./-])\s*", "$1")));
        private static string NormalizeSerie(string? value)
            => Regex.Replace(Regex.Replace((value ?? "").Trim().ToUpperInvariant(), @"\b(SERIE|NUMERO|NUM\.|NUM|N\.|NR\.|NR|PROTOCOLLO|PROT\.?)\b", "", RegexOptions.IgnoreCase), @"[^A-Z0-9]", "");
        private static void Serie(IstanzaDomicilioOperation op, string? value, int max, HashSet<string> seen, string label)
        {
            if (value?.Length > max || !ValidSerie(value)) Error(op.State, "IST_SER_01", $"Numero di serie del {label} non valido (massimo {max} caratteri).");
            else if (!seen.Add(NormalizeSerie(value))) Error(op.State, "IST_SER_02", "Il numero di serie è ripetuto nella richiesta.");
        }
        private static bool ValidSerie(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var serie = value.Trim().TrimEnd('.');
            const RegexOptions options = RegexOptions.IgnoreCase;
            var excluded = new[] { @"^\d{1,2}/\d{1,2}/\d{2,4}$", @"^\d{1,2}/\d{1,2}/\d{2,4}\s*[\-–]\s*\d{1,2}/\d{1,2}/\d{2,4}$",
                @"^dal\s+\d{1,2}/\d{1,2}/\d{2,4}\s+al\s+\d{1,2}/\d{1,2}/\d{2,4}$", @"^dal\s+\d{1,2}\s+\w+\s+\d{4}\s+al\s+\d{1,2}\s+\w+\s+\d{4}$" };
            if (excluded.Any(p => Regex.IsMatch(serie, p, options))) return false;
            var compact = Regex.Replace(serie, @"\s+", "").ToUpperInvariant();
            if (compact is "3T" or "SERIE3T") return false;
            if ((Regex.IsMatch(serie, @"\b(Foglio|part|sub|Cat)\b", options) || Regex.IsMatch(serie, @"\bPRENOTAZIONE\b", options) || Regex.IsMatch(serie, "automatico", options)) &&
                !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options)) return false;
            var valid = new[] { @"^(T|TRF|TEL)\s?[A-Z0-9]{10,50}\.?$", @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", @"^[\d/\s\-]{4,}$",
                @"^\d{1,20}([/\s\-]\d{1,20})+$", @"^(.*\b(serie\s*3\s*T|serie\s*3T|serie\s*T3|serie\s*T|serie\s*IT|3\s*T|3T|T3|3/T)\b.*)$",
                @"^QC([\s/]*\w+)+$", @"^(.*\b(Protocollo|PROT\.?|prot\.?n?\.?|Protocol-?)\b.*\d+.*)$", @"^(RA/|RM|FC/)\s*\S+$",
                @"^(?=.*[A-Za-z])(?=.*\d)[A-Za-z0-9/\s\-]{5,50}$", @"^3Tn\.[0-9]+$" };
            return valid.Any(p => Regex.IsMatch(serie, p, options));
        }

        private sealed class DomicilioPayload
        {
            public string? CodProvinciaDomicilio { get; set; }
            public string? CodComuneDomicilio { get; set; }
            public string? IndirizzoDomicilio { get; set; }
            public string? NumCivicoDomicilio { get; set; }
            public string? CapDomicilio { get; set; }
            [JsonNumberHandling(JsonNumberHandling.Strict)]
            public int? TipoTitoloDomicilio { get; set; }
            public DateTime? DataInizio { get; set; }
            public DateTime? DataFine { get; set; }
            public bool? InserimentoDatiContratto { get; set; }
            public bool Chiusura { get; set; }
        }
        private sealed class ContrattoPayload
        {
            [JsonNumberHandling(JsonNumberHandling.Strict)]
            public int? TipoContrattoTitoloOneroso { get; set; }
            public DateTime? DataRegistrazioneContratto { get; set; }
            public DateTime? DataInizioContratto { get; set; }
            public DateTime? DataFineContratto { get; set; }
            public string? NumeroSerieContratto { get; set; }
            [JsonNumberHandling(JsonNumberHandling.Strict)]
            public int? TipoEnte { get; set; }
            public string? DenominazioneEnte { get; set; }
            public int? ImportoMensileRata { get; set; }
            public bool AddSubentro { get; set; }
            public string? NumeroSerieSubentro { get; set; }
            public DateTime? DataInizioSubentro { get; set; }
            public bool AddCessazione { get; set; }
            public DateTime? DataCessazione { get; set; }
            public bool Chiusura { get; set; }
        }
        private sealed class ProrogaPayload
        {
            public string? NumeroSerieProroga { get; set; }
            public DateTime? DataDecorrenza { get; set; }
            public DateTime? DataScadenza { get; set; }
            public bool Chiusura { get; set; }
        }
    }
}
