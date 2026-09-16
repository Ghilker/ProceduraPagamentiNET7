#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    /// <summary>
    /// Valori presenti in Domicili.TIPO_DOMICILIO.
    /// </summary>
    public enum TipoDomicilioAnalisi
    {
        Gratuito = 0,
        Oneroso = 1
    }

    /// <summary>
    /// Valori presenti in Contratti.TIPO_CONTRATTO_TITOLO_ONEROSO.
    /// </summary>
    public enum TipoContrattoDomicilioAnalisi
    {
        Registrato = 0,
        Ente = 1
    }

    public enum EsitoAnalisiDomicili
    {
        Assente = 0,
        NonValido = 1,
        ValidoProvvisorioSenzaContratto = 2,
        ValidoInFinestraProroga = 3,
        Valido = 4
    }

    /// <summary>
    /// Rappresenta una riga attiva di Domicili e i relativi contratti.
    /// DATA_INIZIO e DATA_FINE sono informative per il domicilio gratuito e
    /// non limitano le date dei contratti onerosi.
    /// </summary>
    public sealed class DomicilioAnalisiInput
    {
        public long Id { get; set; }
        public string CodComune { get; set; } = string.Empty;
        public string Indirizzo { get; set; } = string.Empty;
        public string NumeroCivico { get; set; } = string.Empty;
        public string Cap { get; set; } = string.Empty;
        public TipoDomicilioAnalisi TipoDomicilio { get; set; }
        public DateTime? DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public bool InserimentoDatiContratto { get; set; }
        public List<ContrattoDomicilioAnalisiInput> Contratti { get; set; } = new();
    }

    /// <summary>
    /// Rappresenta una riga attiva di Contratti e le relative proroghe.
    /// Le date di subentro, quando valorizzate, sostituiscono singolarmente
    /// DATA_INIZIO e DATA_FINE.
    /// </summary>
    public sealed class ContrattoDomicilioAnalisiInput
    {
        public long Id { get; set; }
        public TipoContrattoDomicilioAnalisi TipoContratto { get; set; }
        public string NumeroSerieContratto { get; set; } = string.Empty;
        public DateTime? DataRegistrazioneContratto { get; set; }
        public string DenominazioneEnte { get; set; } = string.Empty;
        public decimal? ImportoRata { get; set; }
        public string TipoEnte { get; set; } = string.Empty;
        public string NumeroSerieSubentro { get; set; } = string.Empty;
        public DateTime? DataInizioSubentro { get; set; }
        public DateTime? DataCessazione { get; set; }
        public DateTime? DataInizio { get; set; }
        public DateTime? DataFine { get; set; }
        public List<ProrogaDomicilioAnalisiInput> Proroghe { get; set; } = new();
    }

    public sealed class ProrogaDomicilioAnalisiInput
    {
        public long Id { get; set; }
        public string NumeroSerieProroga { get; set; } = string.Empty;
        public DateTime? DataDecorrenza { get; set; }
        public DateTime? DataScadenza { get; set; }
    }

    public sealed class OpzioniAnalisiDomicili
    {
        public bool GraduatoriaProvvisoria { get; set; }
        public int MesiMinimi { get; set; } = 10;
        public int GiorniMassimiInterruzione { get; set; } = 15;
        public DateTime DataRiferimento { get; set; } = DateTime.Today;
        public bool AbilitaFinestraProrogaTrentaGiorni { get; set; } = true;

        /// <summary>
        /// Filtro opzionale applicato al comune del domicilio. Il contratto
        /// Erasmus (TipoEnte=SE) resta ammesso anche quando il comune non passa
        /// il filtro, in continuità con ControlloStatusSede.
        /// </summary>
        public Func<string, bool>? ComuneAmmesso { get; set; }
    }

    /// <summary>
    /// Outcome contrattuale del domicilio. Quando viene fornito il predicato
    /// ComuneAmmesso, la catena scelta contiene soltanto domicili compatibili
    /// con la sede di studi/equiparati (salvo il caso Erasmus).
    /// </summary>
    public sealed class OutcomeAnalisiDomicili
    {
        public EsitoAnalisiDomicili Esito { get; internal set; }
        public bool Presente { get; internal set; }
        public bool Valido { get; internal set; }
        public bool Provvisorio { get; internal set; }
        public bool ValidoCertoPerSaldo { get; internal set; }

        /// <summary>
        /// Estremi originali della catena scelta, prima del taglio sull'AA.
        /// </summary>
        public DateTime? DataInizioTotale { get; internal set; }
        public DateTime? DataFineTotale { get; internal set; }

        /// <summary>
        /// Estremi della copertura considerata dentro l'anno accademico.
        /// </summary>
        public DateTime? DataInizioCoperturaAnnoAccademico { get; internal set; }
        public DateTime? DataFineCoperturaAnnoAccademico { get; internal set; }

        public int MesiCoperti { get; internal set; }
        public int GiorniCoperti { get; internal set; }
        public int MassimoBucoGiorni { get; internal set; }
        public bool ContieneContrattoEnte { get; internal set; }
        public bool ContieneContrattoErasmus { get; internal set; }
        public string Motivo { get; internal set; } = string.Empty;

        public List<string> ComuniCoinvolti { get; internal set; } = new();
        public List<long> IdDomiciliUtilizzati { get; internal set; } = new();
        public List<long> IdContrattiUtilizzati { get; internal set; } = new();
        public List<long> IdProrogheUtilizzate { get; internal set; } = new();
        public List<string> Anomalie { get; internal set; } = new();
    }

    /// <summary>
    /// Analizza esclusivamente la struttura Domicili -> Contratti -> Proroghe,
    /// valida per gli anni accademici maggiori o uguali a 20262027.
    /// </summary>
    public static class AnalizzatoreDomicili
    {
        public const string PrimoAnnoAccademicoNuovaGestione = "20262027";

        public static bool UsaNuovaGestione(string annoAccademico)
        {
            ParseAnnoAccademico(annoAccademico);
            return ConfrontaAnnoAccademico(
                       annoAccademico,
                       PrimoAnnoAccademicoNuovaGestione) >= 0;
        }

        public static OutcomeAnalisiDomicili Analizza(
            IEnumerable<DomicilioAnalisiInput>? domicili,
            string annoAccademico,
            OpzioniAnalisiDomicili? opzioni = null)
        {
            opzioni ??= new OpzioniAnalisiDomicili();
            ValidaOpzioni(opzioni);

            var (inizioAa, fineAa) = ParseAnnoAccademico(annoAccademico);
            if (!UsaNuovaGestione(annoAccademico))
            {
                throw new InvalidOperationException(
                    $"AnalizzatoreDomicili è utilizzabile solo da AA {PrimoAnnoAccademicoNuovaGestione}.");
            }

            var righe = domicili?.Where(x => x != null).ToList()
                        ?? new List<DomicilioAnalisiInput>();

            var anomalie = new List<string>();
            var onerosi = righe
                .Where(x => x.TipoDomicilio == TipoDomicilioAnalisi.Oneroso)
                .ToList();

            foreach (var domicilio in righe.Where(x =>
                         x.TipoDomicilio != TipoDomicilioAnalisi.Gratuito
                         && x.TipoDomicilio != TipoDomicilioAnalisi.Oneroso))
            {
                anomalie.Add(
                    $"Domicilio {domicilio.Id}: TIPO_DOMICILIO non riconosciuto ({(int)domicilio.TipoDomicilio}).");
            }

            if (onerosi.Count == 0)
            {
                return new OutcomeAnalisiDomicili
                {
                    Esito = EsitoAnalisiDomicili.Assente,
                    Presente = false,
                    Valido = false,
                    Provvisorio = false,
                    ValidoCertoPerSaldo = false,
                    Motivo = "Nessun domicilio oneroso attivo.",
                    Anomalie = anomalie
                };
            }

            bool haOnerosoSenzaContrattoPerProvvisoria = false;
            var comuniProvvisori = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var idDomiciliProvvisori = new HashSet<long>();
            var intervalli = new List<IntervalloCopertura>();

            foreach (var domicilio in onerosi)
            {
                string comune = Normalizza(domicilio.CodComune);
                if (comune.Length == 0)
                {
                    anomalie.Add($"Domicilio {domicilio.Id}: COD_COMUNE mancante.");
                    continue;
                }

                if (!domicilio.InserimentoDatiContratto)
                {
                    if (domicilio.Contratti.Count > 0)
                    {
                        anomalie.Add(
                            $"Domicilio {domicilio.Id}: INSERIMENTO_DATI_CONTRATTO=0 ma sono presenti contratti.");
                    }

                    if (opzioni.GraduatoriaProvvisoria)
                    {
                        if (opzioni.ComuneAmmesso == null || opzioni.ComuneAmmesso(comune))
                        {
                            haOnerosoSenzaContrattoPerProvvisoria = true;
                            comuniProvvisori.Add(comune);
                            idDomiciliProvvisori.Add(domicilio.Id);
                        }
                        else
                        {
                            anomalie.Add(
                                $"Domicilio {domicilio.Id}: comune {comune} non compatibile con la sede di studi.");
                        }
                    }

                    continue;
                }

                if (domicilio.Contratti.Count == 0)
                {
                    anomalie.Add(
                        $"Domicilio {domicilio.Id}: inserimento contratto dichiarato, ma nessun contratto attivo trovato.");
                    continue;
                }

                foreach (var contratto in domicilio.Contratti)
                {
                    AggiungiIntervalliContratto(
                        domicilio,
                        contratto,
                        comune,
                        inizioAa,
                        fineAa,
                        opzioni.GiorniMassimiInterruzione,
                        opzioni.ComuneAmmesso,
                        intervalli,
                        anomalie);
                }
            }

            var catene = CostruisciCatene(intervalli, opzioni.GiorniMassimiInterruzione);
            var migliore = catene
                .OrderByDescending(x => x.MesiCoperti >= opzioni.MesiMinimi)
                .ThenByDescending(x => x.MesiCoperti)
                .ThenByDescending(x => x.GiorniCoperti)
                .ThenBy(x => x.DataInizioAa)
                .ThenByDescending(x => x.DataFineAa)
                .FirstOrDefault();

            if (migliore != null && migliore.MesiCoperti >= opzioni.MesiMinimi)
            {
                return CreaOutcomeDaCatena(
                    migliore,
                    EsitoAnalisiDomicili.Valido,
                    valido: true,
                    provvisorio: false,
                    validoCertoPerSaldo: true,
                    motivo:
                        $"Copertura valida: {migliore.MesiCoperti} mesi, minimo richiesto {opzioni.MesiMinimi}, " +
                        $"interruzione massima {migliore.MassimoBucoGiorni} giorni.",
                    anomalie);
            }

            if (haOnerosoSenzaContrattoPerProvvisoria)
            {
                var outcome = migliore != null
                    ? CreaOutcomeDaCatena(
                        migliore,
                        EsitoAnalisiDomicili.ValidoProvvisorioSenzaContratto,
                        valido: true,
                        provvisorio: true,
                        validoCertoPerSaldo: false,
                        motivo:
                            "Graduatoria provvisoria: domicilio oneroso senza inserimento dei dati contrattuali; " +
                            "requisito domicilio considerato valido in via provvisoria.",
                        anomalie)
                    : new OutcomeAnalisiDomicili
                    {
                        Esito = EsitoAnalisiDomicili.ValidoProvvisorioSenzaContratto,
                        Presente = true,
                        Valido = true,
                        Provvisorio = true,
                        ValidoCertoPerSaldo = false,
                        Motivo =
                            "Graduatoria provvisoria: domicilio oneroso senza inserimento dei dati contrattuali; " +
                            "requisito domicilio considerato valido in via provvisoria.",
                        Anomalie = anomalie
                    };

                outcome.ComuniCoinvolti = outcome.ComuniCoinvolti
                    .Concat(comuniProvvisori)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                outcome.IdDomiciliUtilizzati = outcome.IdDomiciliUtilizzati
                    .Concat(idDomiciliProvvisori)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                return outcome;
            }

            if (migliore != null
                && opzioni.AbilitaFinestraProrogaTrentaGiorni
                && migliore.DataInizioAa.Date <= inizioAa.Date
                && migliore.DataFineAa.Date < fineAa.Date
                && opzioni.DataRiferimento.Date <= migliore.DataFineAa.Date.AddDays(30))
            {
                return CreaOutcomeDaCatena(
                    migliore,
                    EsitoAnalisiDomicili.ValidoInFinestraProroga,
                    valido: true,
                    provvisorio: true,
                    validoCertoPerSaldo: false,
                    motivo:
                        $"Copertura inferiore al minimo ({migliore.MesiCoperti}/{opzioni.MesiMinimi}), " +
                        $"ma ancora nella finestra di proroga di 30 giorni dalla scadenza " +
                        $"{migliore.DataFineAa:dd/MM/yyyy}.",
                    anomalie);
            }

            if (migliore != null)
            {
                return CreaOutcomeDaCatena(
                    migliore,
                    EsitoAnalisiDomicili.NonValido,
                    valido: false,
                    provvisorio: false,
                    validoCertoPerSaldo: false,
                    motivo:
                        $"Copertura insufficiente: {migliore.MesiCoperti} mesi, " +
                        $"minimo richiesto {opzioni.MesiMinimi}.",
                    anomalie);
            }

            return new OutcomeAnalisiDomicili
            {
                Esito = EsitoAnalisiDomicili.NonValido,
                Presente = true,
                Valido = false,
                Provvisorio = false,
                ValidoCertoPerSaldo = false,
                Motivo = "Domicilio oneroso presente, ma nessun periodo contrattuale valido nell'anno accademico.",
                Anomalie = anomalie
            };
        }

        private static void AggiungiIntervalliContratto(
            DomicilioAnalisiInput domicilio,
            ContrattoDomicilioAnalisiInput contratto,
            string comune,
            DateTime inizioAa,
            DateTime fineAa,
            int giorniMassimiInterruzione,
            Func<string, bool>? comuneAmmesso,
            ICollection<IntervalloCopertura> destinazione,
            ICollection<string> anomalie)
        {
            DateTime? dataInizio = contratto.DataInizioSubentro ?? contratto.DataInizio;
            DateTime? dataFine = contratto.DataCessazione ?? contratto.DataFine;

            if (!DataValida(dataInizio) || !DataValida(dataFine))
            {
                anomalie.Add(
                    $"Contratto {contratto.Id}, domicilio {domicilio.Id}: DATA_INIZIO/DATA_FINE mancanti o non valide.");
                return;
            }

            if (dataFine!.Value.Date < dataInizio!.Value.Date)
            {
                anomalie.Add(
                    $"Contratto {contratto.Id}, domicilio {domicilio.Id}: DATA_FINE precedente a DATA_INIZIO.");
                return;
            }

            if (!FormalmenteValido(contratto, dataFine.Value, domicilio.Id, anomalie))
                return;

            bool contrattoErasmus =
                contratto.TipoContratto == TipoContrattoDomicilioAnalisi.Ente
                && string.Equals(Normalizza(contratto.TipoEnte), "SE", StringComparison.OrdinalIgnoreCase);

            if (!contrattoErasmus && comuneAmmesso != null && !comuneAmmesso(comune))
            {
                anomalie.Add(
                    $"Contratto {contratto.Id}, domicilio {domicilio.Id}: comune {comune} " +
                    "non compatibile con la sede di studi.");
                return;
            }

            var intervalliContratto = new List<IntervalloCopertura>
            {
                CreaIntervallo(
                    domicilio,
                    contratto,
                    proroga: null,
                    comune,
                    dataInizio.Value.Date,
                    dataFine.Value.Date,
                    inizioAa,
                    fineAa)
            };

            DateTime fineCatenaContratto = dataFine.Value.Date;
            foreach (var proroga in contratto.Proroghe
                         .OrderBy(x => x.DataDecorrenza ?? DateTime.MaxValue)
                         .ThenBy(x => x.Id))
            {
                if (!FormalmenteValida(proroga, contratto, domicilio.Id, anomalie))
                    continue;

                DateTime decorrenza = proroga.DataDecorrenza!.Value.Date;
                DateTime scadenza = proroga.DataScadenza!.Value.Date;
                int buco = CalcolaBucoGiorni(fineCatenaContratto, decorrenza);

                if (buco > giorniMassimiInterruzione)
                {
                    anomalie.Add(
                        $"Proroga {proroga.Id}, contratto {contratto.Id}: interruzione di {buco} giorni, " +
                        $"superiore al massimo di {giorniMassimiInterruzione}.");
                    continue;
                }

                intervalliContratto.Add(
                    CreaIntervallo(
                        domicilio,
                        contratto,
                        proroga,
                        comune,
                        decorrenza,
                        scadenza,
                        inizioAa,
                        fineAa));

                if (scadenza > fineCatenaContratto)
                    fineCatenaContratto = scadenza;
            }

            foreach (var intervallo in intervalliContratto.Where(x => x.IntersecaAnnoAccademico))
                destinazione.Add(intervallo);
        }

        private static bool FormalmenteValido(
            ContrattoDomicilioAnalisiInput contratto,
            DateTime dataFineEffettiva,
            long idDomicilio,
            ICollection<string> anomalie)
        {
            if (contratto.TipoContratto == TipoContrattoDomicilioAnalisi.Registrato)
            {
                if (string.IsNullOrWhiteSpace(contratto.NumeroSerieContratto))
                {
                    anomalie.Add(
                        $"Contratto {contratto.Id}, domicilio {idDomicilio}: numero serie contratto mancante.");
                    return false;
                }

                if (!DataValida(contratto.DataRegistrazioneContratto))
                {
                    anomalie.Add(
                        $"Contratto {contratto.Id}, domicilio {idDomicilio}: data registrazione mancante o non valida.");
                    return false;
                }

                if (contratto.DataRegistrazioneContratto!.Value.Date > dataFineEffettiva.Date)
                {
                    anomalie.Add(
                        $"Contratto {contratto.Id}, domicilio {idDomicilio}: registrazione successiva alla fine del contratto.");
                    return false;
                }

                return true;
            }

            if (contratto.TipoContratto == TipoContrattoDomicilioAnalisi.Ente)
            {
                if (string.IsNullOrWhiteSpace(contratto.DenominazioneEnte))
                {
                    anomalie.Add(
                        $"Contratto {contratto.Id}, domicilio {idDomicilio}: denominazione ente mancante.");
                    return false;
                }

                if (!contratto.ImportoRata.HasValue || contratto.ImportoRata.Value <= 0m)
                {
                    anomalie.Add(
                        $"Contratto {contratto.Id}, domicilio {idDomicilio}: importo rata ente nullo o negativo.");
                    return false;
                }

                return true;
            }

            anomalie.Add(
                $"Contratto {contratto.Id}, domicilio {idDomicilio}: tipo contratto non riconosciuto " +
                $"({(int)contratto.TipoContratto}).");
            return false;
        }

        private static bool FormalmenteValida(
            ProrogaDomicilioAnalisiInput proroga,
            ContrattoDomicilioAnalisiInput contratto,
            long idDomicilio,
            ICollection<string> anomalie)
        {
            if (string.IsNullOrWhiteSpace(proroga.NumeroSerieProroga))
            {
                anomalie.Add(
                    $"Proroga {proroga.Id}, contratto {contratto.Id}, domicilio {idDomicilio}: numero serie mancante.");
                return false;
            }

            if (!DataValida(proroga.DataDecorrenza) || !DataValida(proroga.DataScadenza))
            {
                anomalie.Add(
                    $"Proroga {proroga.Id}, contratto {contratto.Id}, domicilio {idDomicilio}: date mancanti o non valide.");
                return false;
            }

            if (proroga.DataScadenza!.Value.Date < proroga.DataDecorrenza!.Value.Date)
            {
                anomalie.Add(
                    $"Proroga {proroga.Id}, contratto {contratto.Id}, domicilio {idDomicilio}: " +
                    "scadenza precedente alla decorrenza.");
                return false;
            }

            string serieContratto = Normalizza(contratto.NumeroSerieContratto);
            string serieProroga = Normalizza(proroga.NumeroSerieProroga);
            if (serieContratto.Length > 0
                && serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                anomalie.Add(
                    $"Proroga {proroga.Id}, contratto {contratto.Id}: serie proroga uguale o contenente " +
                    "la serie del contratto.");
                return false;
            }

            return true;
        }

        private static IntervalloCopertura CreaIntervallo(
            DomicilioAnalisiInput domicilio,
            ContrattoDomicilioAnalisiInput contratto,
            ProrogaDomicilioAnalisiInput? proroga,
            string comune,
            DateTime dataInizio,
            DateTime dataFine,
            DateTime inizioAa,
            DateTime fineAa)
        {
            DateTime inizioCopertura = dataInizio > inizioAa ? dataInizio : inizioAa;
            DateTime fineCopertura = dataFine < fineAa ? dataFine : fineAa;

            return new IntervalloCopertura
            {
                IdDomicilio = domicilio.Id,
                IdContratto = contratto.Id,
                IdProroga = proroga?.Id,
                Comune = comune,
                DataInizioOriginale = dataInizio,
                DataFineOriginale = dataFine,
                DataInizioAa = inizioCopertura,
                DataFineAa = fineCopertura,
                IntersecaAnnoAccademico = inizioCopertura <= fineCopertura,
                ContrattoEnte = contratto.TipoContratto == TipoContrattoDomicilioAnalisi.Ente,
                ContrattoErasmus =
                    contratto.TipoContratto == TipoContrattoDomicilioAnalisi.Ente
                    && string.Equals(
                        Normalizza(contratto.TipoEnte),
                        "SE",
                        StringComparison.OrdinalIgnoreCase)
            };
        }

        private static List<CatenaCopertura> CostruisciCatene(
            IReadOnlyCollection<IntervalloCopertura> intervalli,
            int giorniMassimiInterruzione)
        {
            var ordinati = intervalli
                .OrderBy(x => x.DataInizioAa)
                .ThenBy(x => x.DataFineAa)
                .ThenBy(x => x.IdDomicilio)
                .ThenBy(x => x.IdContratto)
                .ToList();

            var result = new List<CatenaCopertura>();
            CatenaCopertura? corrente = null;

            foreach (var intervallo in ordinati)
            {
                if (corrente == null)
                {
                    corrente = new CatenaCopertura(intervallo);
                    continue;
                }

                int buco = CalcolaBucoGiorni(corrente.DataFineAa, intervallo.DataInizioAa);
                if (buco <= giorniMassimiInterruzione)
                {
                    corrente.Aggiungi(intervallo, buco);
                    continue;
                }

                corrente.CalcolaMetriche();
                result.Add(corrente);
                corrente = new CatenaCopertura(intervallo);
            }

            if (corrente != null)
            {
                corrente.CalcolaMetriche();
                result.Add(corrente);
            }

            return result;
        }

        private static OutcomeAnalisiDomicili CreaOutcomeDaCatena(
            CatenaCopertura catena,
            EsitoAnalisiDomicili esito,
            bool valido,
            bool provvisorio,
            bool validoCertoPerSaldo,
            string motivo,
            List<string> anomalie)
        {
            return new OutcomeAnalisiDomicili
            {
                Esito = esito,
                Presente = true,
                Valido = valido,
                Provvisorio = provvisorio,
                ValidoCertoPerSaldo = validoCertoPerSaldo,
                DataInizioTotale = catena.DataInizioOriginale,
                DataFineTotale = catena.DataFineOriginale,
                DataInizioCoperturaAnnoAccademico = catena.DataInizioAa,
                DataFineCoperturaAnnoAccademico = catena.DataFineAa,
                MesiCoperti = catena.MesiCoperti,
                GiorniCoperti = catena.GiorniCoperti,
                MassimoBucoGiorni = catena.MassimoBucoGiorni,
                ContieneContrattoEnte = catena.Intervalli.Any(x => x.ContrattoEnte),
                ContieneContrattoErasmus = catena.Intervalli.Any(x => x.ContrattoErasmus),
                ComuniCoinvolti = catena.Intervalli
                    .Select(x => x.Comune)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                IdDomiciliUtilizzati = catena.Intervalli
                    .Select(x => x.IdDomicilio)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                IdContrattiUtilizzati = catena.Intervalli
                    .Select(x => x.IdContratto)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                IdProrogheUtilizzate = catena.Intervalli
                    .Where(x => x.IdProroga.HasValue)
                    .Select(x => x.IdProroga!.Value)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList(),
                Motivo = motivo,
                Anomalie = anomalie
            };
        }

        private static int CalcolaBucoGiorni(DateTime finePrecedente, DateTime inizioSuccessivo)
        {
            if (inizioSuccessivo.Date <= finePrecedente.Date.AddDays(1))
                return 0;

            return (inizioSuccessivo.Date - finePrecedente.Date).Days - 1;
        }

        private static bool DataValida(DateTime? value)
            => value.HasValue && value.Value != DateTime.MinValue && value.Value.Year >= 1900;

        private static string Normalizza(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static void ValidaOpzioni(OpzioniAnalisiDomicili opzioni)
        {
            if (opzioni.MesiMinimi <= 0)
                throw new ArgumentOutOfRangeException(nameof(opzioni.MesiMinimi));

            if (opzioni.GiorniMassimiInterruzione < 0)
                throw new ArgumentOutOfRangeException(nameof(opzioni.GiorniMassimiInterruzione));
        }

        private static (DateTime Inizio, DateTime Fine) ParseAnnoAccademico(string annoAccademico)
        {
            string aa = (annoAccademico ?? string.Empty).Trim();
            if (aa.Length != 8
                || !int.TryParse(aa.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int annoInizio)
                || !int.TryParse(aa.Substring(4, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int annoFine)
                || annoFine != annoInizio + 1)
            {
                throw new ArgumentException(
                    "Anno accademico non valido. Formato atteso: YYYYYYYY, ad esempio 20262027.",
                    nameof(annoAccademico));
            }

            return (new DateTime(annoInizio, 10, 1), new DateTime(annoFine, 9, 30));
        }

        private static int ConfrontaAnnoAccademico(string primo, string secondo)
        {
            int annoPrimo = int.Parse(primo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            int annoSecondo = int.Parse(secondo.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);
            return annoPrimo.CompareTo(annoSecondo);
        }

        private sealed class IntervalloCopertura
        {
            public long IdDomicilio { get; set; }
            public long IdContratto { get; set; }
            public long? IdProroga { get; set; }
            public string Comune { get; set; } = string.Empty;
            public DateTime DataInizioOriginale { get; set; }
            public DateTime DataFineOriginale { get; set; }
            public DateTime DataInizioAa { get; set; }
            public DateTime DataFineAa { get; set; }
            public bool IntersecaAnnoAccademico { get; set; }
            public bool ContrattoEnte { get; set; }
            public bool ContrattoErasmus { get; set; }
        }

        private sealed class CatenaCopertura
        {
            public CatenaCopertura(IntervalloCopertura primo)
            {
                Intervalli.Add(primo);
                DataInizioOriginale = primo.DataInizioOriginale;
                DataFineOriginale = primo.DataFineOriginale;
                DataInizioAa = primo.DataInizioAa;
                DataFineAa = primo.DataFineAa;
            }

            public List<IntervalloCopertura> Intervalli { get; } = new();
            public DateTime DataInizioOriginale { get; private set; }
            public DateTime DataFineOriginale { get; private set; }
            public DateTime DataInizioAa { get; private set; }
            public DateTime DataFineAa { get; private set; }
            public int MesiCoperti { get; private set; }
            public int GiorniCoperti { get; private set; }
            public int MassimoBucoGiorni { get; private set; }

            public void Aggiungi(IntervalloCopertura intervallo, int buco)
            {
                Intervalli.Add(intervallo);

                if (intervallo.DataInizioOriginale < DataInizioOriginale)
                    DataInizioOriginale = intervallo.DataInizioOriginale;
                if (intervallo.DataFineOriginale > DataFineOriginale)
                    DataFineOriginale = intervallo.DataFineOriginale;
                if (intervallo.DataInizioAa < DataInizioAa)
                    DataInizioAa = intervallo.DataInizioAa;
                if (intervallo.DataFineAa > DataFineAa)
                    DataFineAa = intervallo.DataFineAa;
                if (buco > MassimoBucoGiorni)
                    MassimoBucoGiorni = buco;
            }

            public void CalcolaMetriche()
            {
                var unione = UnisciIntervalliEffettivamenteCoperti(
                    Intervalli.Select(x => (x.DataInizioAa, x.DataFineAa)));

                GiorniCoperti = unione.Sum(x => (x.Fine - x.Inizio).Days + 1);

                var giorniPerMese = new Dictionary<int, int>();
                foreach (var intervallo in unione)
                {
                    var mese = new DateTime(intervallo.Inizio.Year, intervallo.Inizio.Month, 1);
                    var ultimoMese = new DateTime(intervallo.Fine.Year, intervallo.Fine.Month, 1);
                    while (mese <= ultimoMese)
                    {
                        DateTime inizioMese = mese;
                        DateTime fineMese = mese.AddMonths(1).AddDays(-1);
                        DateTime inizioCoperto =
                            intervallo.Inizio > inizioMese ? intervallo.Inizio : inizioMese;
                        DateTime fineCoperta =
                            intervallo.Fine < fineMese ? intervallo.Fine : fineMese;
                        int giorni = (fineCoperta - inizioCoperto).Days + 1;
                        int chiaveMese = (mese.Year * 100) + mese.Month;

                        giorniPerMese.TryGetValue(chiaveMese, out int giorniEsistenti);
                        giorniPerMese[chiaveMese] = giorniEsistenti + giorni;
                        mese = mese.AddMonths(1);
                    }
                }

                // Mantiene la regola della verifica legacy: un mese concorre
                // solo quando risultano coperti almeno 15 giorni.
                MesiCoperti = giorniPerMese.Count(x => x.Value >= 15);
            }

            private static List<(DateTime Inizio, DateTime Fine)> UnisciIntervalliEffettivamenteCoperti(
                IEnumerable<(DateTime Inizio, DateTime Fine)> periodi)
            {
                var ordinati = periodi
                    .OrderBy(x => x.Inizio)
                    .ThenBy(x => x.Fine)
                    .ToList();

                var result = new List<(DateTime Inizio, DateTime Fine)>();
                foreach (var periodo in ordinati)
                {
                    if (result.Count == 0)
                    {
                        result.Add(periodo);
                        continue;
                    }

                    var ultimo = result[result.Count - 1];
                    if (periodo.Inizio <= ultimo.Fine.AddDays(1))
                    {
                        if (periodo.Fine > ultimo.Fine)
                            result[result.Count - 1] = (ultimo.Inizio, periodo.Fine);
                        continue;
                    }

                    result.Add(periodo);
                }

                return result;
            }
        }
    }
}
