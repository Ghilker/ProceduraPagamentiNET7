using System.Globalization;
using System.Text;
using System.Text.Json;
using DomicilioAutomation;

namespace ProcedureNet7
{
    internal sealed record DomicilioAutomaticCorrectionPlan(IstanzaDomicilioRecord Candidate,
        IReadOnlyList<DomicilioAutomationOperationCorrection> Corrections);

    /// <summary>Builds a separately validated proposal. Registered data and the original request remain unchanged.</summary>
    internal static class DomicilioAutomaticCorrectionPlanner
    {
        private static readonly HashSet<string> CorrectableCodes = new(StringComparer.Ordinal)
            { "PDF_010", "PDF_011", "PDF_012", "PDF_013", "PDF_014", "PDF_015", "PDF_017" };

        public static DomicilioAutomaticCorrectionPlan? Plan(IstanzaDomicilioRecord request,
            IReadOnlyDictionary<string, IstanzaDomicilioComune> comuni, DateTime today)
        {
            if (!request.ValidazioneCompletata || request.Operazioni.Count == 0) return null;
            var documents = request.Operazioni.SelectMany(x => x.Documenti).ToList();
            if (documents.Count == 0 || documents.Any(d => !Complete(d))) return null;
            var findings = request.State.Findings.Concat(request.Operazioni.SelectMany(o => o.State.Findings))
                .Concat(documents.SelectMany(d => d.Attachment!.Analysis!.State.Findings)).ToList();
            if (!findings.Any(f => f.Severity == DomicilioControlSeverity.NonConforme) ||
                findings.Any(f => f.Severity != DomicilioControlSeverity.NonConforme || !CorrectableCodes.Contains(f.Code)))
                return null;

            var corrections = new List<DomicilioAutomationOperationCorrection>();
            foreach (var operation in request.Operazioni.OrderBy(x => x.Ordine).ThenBy(x => x.Id))
            {
                var mismatched = operation.Documenti.Where(d => d.Attachment!.Analysis!.State.Findings.Count > 0).ToList();
                if (mismatched.Count == 0) continue;
                if (operation.Operazione is not (1 or 2) || operation.Entita is not (2 or 3)) return null;
                try
                {
                    using var json = JsonDocument.Parse(operation.PayloadJson);
                    var properties = Properties(json.RootElement);
                    if (properties == null || Flag(properties, "Chiusura") != false ||
                        operation.Entita == 2 && Flag(properties, "AddCessazione") != false) return null;
                    var fields = new List<DomicilioAutomationFieldCorrection>();
                    foreach (var document in mismatched)
                    {
                        var analysis = document.Attachment!.Analysis!;
                        if (!analysis.CanProposeCorrections || !ProposedSource(request, operation, document)) return null;
                        var mapping = Mapping(operation, document, properties);
                        if (mapping == null) return null;
                        var evidence = analysis.DocumentedCorrections.GroupBy(e => e.Field, StringComparer.Ordinal).ToList();
                        if (evidence.Count == 0 || evidence.Any(g => !mapping.ContainsKey(g.Key) ||
                            g.Select(e => e.Value).Distinct(StringComparer.Ordinal).Count() != 1 ||
                            g.Any(e => e.Page <= 0 || e.Page > analysis.PageCount || string.IsNullOrWhiteSpace(e.Label)))) return null;
                        foreach (var finding in analysis.State.Findings)
                        {
                            var match = mapping.SingleOrDefault(p => p.Value.Code == finding.Code);
                            if (match.Key == null) return null;
                            var source = evidence.SingleOrDefault(g => g.Key == match.Key)?.OrderBy(e => e.Page).First();
                            var key = properties.Keys.SingleOrDefault(k => string.Equals(k, match.Value.Property, StringComparison.OrdinalIgnoreCase));
                            if (source == null || key == null || properties[key].ValueKind != JsonValueKind.String ||
                                string.IsNullOrWhiteSpace(properties[key].GetString()) || string.IsNullOrWhiteSpace(source.Value)) return null;
                            if (match.Key != "Reference" && !DateTime.TryParseExact(source.Value, "yyyy-MM-dd",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;
                            if (fields.Any(f => string.Equals(f.Field, key, StringComparison.OrdinalIgnoreCase))) return null;
                            fields.Add(new(key, properties[key].GetString()!, source.Value, source.Page,
                                document.IdAllegato!.Value, source.Label));
                        }
                    }
                    if (fields.Count == 0) return null;
                    var correction = new DomicilioAutomationOperationCorrection(operation.Id, operation.PayloadJson,
                        Rewrite(json.RootElement, fields), fields);
                    DomicilioAutomationCorrectionPatch.Apply(operation.PayloadJson, correction, operation.Entita, operation.Operazione);
                    corrections.Add(correction);
                }
                catch (JsonException) { return null; }
                catch (ArgumentException) { return null; }
            }
            if (corrections.Count == 0) return null;
            var candidate = Clone(request, corrections);
            IstanzaDomicilioValidator.Validate(candidate, comuni, today);
            // Invalid resulting periods or other aggregate rules remain visible on the proposal.
            return new(candidate, corrections);
        }

        private static bool Complete(IstanzaDomicilioDocument document)
        {
            var expectation = document.Expectation;
            var analysis = document.Attachment?.Analysis;
            return expectation != null && (expectation.Type is DomicilioPdfDocumentType.Contratto or
                DomicilioPdfDocumentType.Subentro or DomicilioPdfDocumentType.Proroga) &&
                analysis is { FileAvailable: true, PdfValid: true, Readable: true, TypeRecognized: true, DataCoherent: not null } &&
                analysis.PageCount > 0 && analysis.PagesRead == analysis.PageCount &&
                (!expectation.RequireFiscalCode || analysis.FiscalCodePresent == true);
        }

        private static bool ProposedSource(IstanzaDomicilioRecord request, IstanzaDomicilioOperation operation,
            IstanzaDomicilioDocument document)
        {
            var proposedId = document.Tipo switch
            {
                "CONTRATTO" when document.Expectation!.Type == DomicilioPdfDocumentType.Contratto => operation.IdAllegatoContratto,
                "SUBENTRO" when document.Expectation!.Type == DomicilioPdfDocumentType.Subentro => operation.IdAllegatoSubentro,
                "PROROGA" when document.Expectation!.Type == DomicilioPdfDocumentType.Proroga => operation.IdAllegatoProroga,
                _ => null
            };
            return proposedId is > 0 && proposedId == document.IdAllegato && proposedId == document.Attachment!.IdAllegato &&
                string.Equals(document.Expectation!.CodFiscale, request.CodFiscale, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(document.Attachment.CodFiscale, request.CodFiscale, StringComparison.OrdinalIgnoreCase) &&
                document.Attachment.NumDomanda == request.NumDomanda;
        }

        private static Dictionary<string, (string Property, string Code)>? Mapping(IstanzaDomicilioOperation operation,
            IstanzaDomicilioDocument document, Dictionary<string, JsonElement> properties)
        {
            var type = properties.GetValueOrDefault("TipoContrattoTitoloOneroso");
            var locazione = type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var value) && value == 0;
            if (operation.Entita == 2 && document.Tipo == "CONTRATTO" && locazione)
                return new(StringComparer.Ordinal)
                {
                    ["Reference"] = ("NumeroSerieContratto", "PDF_017"),
                    ["RegistrationDate"] = ("DataRegistrazioneContratto", "PDF_010"),
                    ["StartDate"] = ("DataInizioContratto", "PDF_011"),
                    ["EndDate"] = ("DataFineContratto", "PDF_012")
                };
            if (operation.Entita == 2 && document.Tipo == "SUBENTRO" && Flag(properties, "AddSubentro") == true &&
                (locazione || type.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
                return new(StringComparer.Ordinal)
                {
                    ["Reference"] = ("NumeroSerieSubentro", "PDF_017"),
                    ["StartDate"] = ("DataInizioSubentro", "PDF_013")
                };
            if (operation.Entita == 3 && document.Tipo == "PROROGA")
                return new(StringComparer.Ordinal)
                {
                    ["Reference"] = ("NumeroSerieProroga", "PDF_017"),
                    ["StartDate"] = ("DataDecorrenza", "PDF_014"),
                    ["EndDate"] = ("DataScadenza", "PDF_015")
                };
            return null;
        }

        private static Dictionary<string, JsonElement>? Properties(JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in json.EnumerateObject())
                if (!result.TryAdd(property.Name, property.Value)) return null;
            return result;
        }

        private static bool? Flag(Dictionary<string, JsonElement> properties, string key)
        {
            if (!properties.TryGetValue(key, out var value)) return false;
            return value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        }

        private static string Rewrite(JsonElement payload, List<DomicilioAutomationFieldCorrection> fields)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in payload.EnumerateObject())
                {
                    var correction = fields.SingleOrDefault(f => string.Equals(f.Field, property.Name, StringComparison.Ordinal));
                    if (correction != null) writer.WriteString(property.Name, correction.After);
                    else property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static IstanzaDomicilioRecord Clone(IstanzaDomicilioRecord source,
            List<DomicilioAutomationOperationCorrection> corrections)
        {
            var clone = new IstanzaDomicilioRecord
            {
                NumIstanza = source.NumIstanza, AnnoAccademico = source.AnnoAccademico, NumDomanda = source.NumDomanda,
                CodFiscale = source.CodFiscale, Studente = source.Studente, DataPresentazione = source.DataPresentazione,
                PresaCarico = source.PresaCarico, Operatore = source.Operatore, ExpectedFingerprint = source.ExpectedFingerprint
            };
            foreach (var operation in source.Operazioni)
                clone.Operazioni.Add(new IstanzaDomicilioOperation
                {
                    Id = operation.Id, Ordine = operation.Ordine, Entita = operation.Entita, Operazione = operation.Operazione,
                    IdOperazionePadre = operation.IdOperazionePadre, IdDomicilio = operation.IdDomicilio,
                    IdContratto = operation.IdContratto, IdProroga = operation.IdProroga,
                    IdAllegatoContratto = operation.IdAllegatoContratto, IdAllegatoSubentro = operation.IdAllegatoSubentro,
                    IdAllegatoProroga = operation.IdAllegatoProroga, IdAllegatoLearningAgreement = operation.IdAllegatoLearningAgreement,
                    DataValiditaOrigine = operation.DataValiditaOrigine, DataModificaOrigine = operation.DataModificaOrigine,
                    PayloadJson = corrections.SingleOrDefault(c => c.OperationId == operation.Id)?.AfterPayload ?? operation.PayloadJson
                });
            clone.DomiciliCorrenti.AddRange(source.DomiciliCorrenti.Select(Clone));
            return clone;
        }

        private static DomicilioControlRecord Clone(DomicilioControlRecord source)
        {
            var clone = new DomicilioControlRecord
            {
                Id = source.Id, DataValidita = source.DataValidita, DataModifica = source.DataModifica,
                AnnoAccademico = source.AnnoAccademico, NumDomanda = source.NumDomanda, CodFiscale = source.CodFiscale,
                Studente = source.Studente, InserimentoDatiContratto = source.InserimentoDatiContratto,
                CodProvincia = source.CodProvincia, CodComune = source.CodComune, Comune = source.Comune,
                Indirizzo = source.Indirizzo, NumeroCivico = source.NumeroCivico, Cap = source.Cap,
                TipoDomicilio = source.TipoDomicilio, DataInizio = source.DataInizio, DataFine = source.DataFine
            };
            foreach (var contract in source.Contratti)
            {
                var copied = new ContrattoControlRecord
                {
                    Id = contract.Id, IdDomicilio = contract.IdDomicilio, DataValidita = contract.DataValidita,
                    DataModifica = contract.DataModifica, TipoContratto = contract.TipoContratto, NumeroSerie = contract.NumeroSerie,
                    DataRegistrazione = contract.DataRegistrazione, DataInizio = contract.DataInizio, DataFine = contract.DataFine,
                    TipoEnte = contract.TipoEnte, DenominazioneEnte = contract.DenominazioneEnte, ImportoRata = contract.ImportoRata,
                    NumeroSerieSubentro = contract.NumeroSerieSubentro, DataInizioSubentro = contract.DataInizioSubentro,
                    DataCessazione = contract.DataCessazione
                };
                copied.AllegatiContratto.AddRange(contract.AllegatiContratto.Select(Clone));
                copied.AllegatiSubentro.AddRange(contract.AllegatiSubentro.Select(Clone));
                copied.AllegatiLearningAgreement.AddRange(contract.AllegatiLearningAgreement.Select(Clone));
                foreach (var extension in contract.Proroghe)
                {
                    var extensionClone = new ProrogaControlRecord
                    {
                        Id = extension.Id, IdContratto = extension.IdContratto, DataValidita = extension.DataValidita,
                        DataModifica = extension.DataModifica, NumeroSerie = extension.NumeroSerie,
                        DataDecorrenza = extension.DataDecorrenza, DataScadenza = extension.DataScadenza
                    };
                    extensionClone.Allegati.AddRange(extension.Allegati.Select(Clone));
                    copied.Proroghe.Add(extensionClone);
                }
                clone.Contratti.Add(copied);
            }
            return clone;
        }

        private static DomicilioAttachmentRecord Clone(DomicilioAttachmentRecord source) => new()
        {
            IdAllegato = source.IdAllegato, CodTipoAllegato = source.CodTipoAllegato, CodStatus = source.CodStatus,
            StatusDescription = source.StatusDescription, DataValidita = source.DataValidita, IdContratto = source.IdContratto,
            IdProroga = source.IdProroga, CodFiscale = source.CodFiscale, NumDomanda = source.NumDomanda,
            FileName = source.FileName, ContentType = source.ContentType
        };
    }
}
