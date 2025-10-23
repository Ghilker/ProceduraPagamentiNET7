// KeywordEngineV6.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    public enum Topic
    {
        PERMESSO,
        ISCRIZIONE,
        TASSE,
        CREDITI,
        TIROCINIO,
        ALLOGGIO,
        CONTRATTO,
        ISEE_REDDITI,
        CARRIERA,
        PORTALE,
        PEC_EMAIL,
        INDIPENDENTE,
        DEBITORIA,
        IMPORTI,
        GRADUATORIA,
        BLOCCHI,              // <- non diventa mai primario/secondario, solo terziario
        MENSA,
        QR_MENSA,
        IBAN_CODICEFISCALE,
        CODICE_FISCALE,       // <- nuovo topic specifico
        CONTRIBUTO_ALLOGGIO,
        RIMBORSO_TASSA,
        DEPOSITO_ALLOGGIO,
        DOV_CIMEA,
        PASSAGGIO_TRASF,
        RINUNCIA_REVOCA,
        MOBILITA_ERASMUS
    }

    public enum Lang { UNKNOWN, IT, EN, MIXED }

    public sealed class ExtractionV6
    {
        public Dictionary<Topic, int> Counts { get; } = new();
        public string TopicPrimary { get; set; } = "";
        public string TopicSecondary { get; set; } = "";
        public string TopicTertiary { get; set; } = "";

        public Lang DetectedLanguage { get; set; } = Lang.UNKNOWN;
        public string OriginalText { get; set; } = "";
        public string NormalizedText { get; set; } = "";

        public bool MentionsBlocks { get; set; } = false;
        public bool MentionsOwnPosition { get; set; } = false;
        public bool BlocksOnOwnPosition => MentionsBlocks && MentionsOwnPosition;
    }

    public static class KeywordEngineV6
    {
        private const int PHRASE_WEIGHT = 2;
        private const int WORD_WEIGHT = 1;
        private const int BOOST_STRONG = 6;
        private const int BOOST_MED = 3;

        private static readonly Topic[] PRECEDENCE =
        {
            Topic.TASSE, Topic.ISCRIZIONE, Topic.CREDITI, Topic.TIROCINIO,
            Topic.IMPORTI, Topic.ALLOGGIO, Topic.CONTRIBUTO_ALLOGGIO, Topic.CONTRATTO,
            Topic.ISEE_REDDITI, Topic.MENSA, Topic.QR_MENSA, Topic.DEBITORIA,
            Topic.PASSAGGIO_TRASF, Topic.CARRIERA, Topic.RINUNCIA_REVOCA,
            Topic.PERMESSO, Topic.DOV_CIMEA, Topic.MOBILITA_ERASMUS,
            Topic.IBAN_CODICEFISCALE, Topic.CODICE_FISCALE, Topic.RIMBORSO_TASSA, Topic.DEPOSITO_ALLOGGIO,
            Topic.PORTALE, Topic.PEC_EMAIL, Topic.GRADUATORIA,
            Topic.BLOCCHI // sempre escluso da primary/secondary
        };

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.ToLowerInvariant();

            var norm = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(text.Length);
            foreach (var c in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            text = sb.ToString().Normalize(NormalizationForm.FormC);

            text = text.Replace('’', '\'').Replace('‘', '\'')
                       .Replace('“', '"').Replace('”', '"')
                       .Replace('–', '-').Replace('—', '-');

            text = Regex.Replace(text, @"[|/\\]+", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static readonly Dictionary<Topic, (string[] multi, string[] single)> Dict = BuildDict();

        private static Dictionary<Topic, (string[] multi, string[] single)> BuildDict()
        {
            return new()
            {
                [Topic.PERMESSO] = (new[] { "permesso di soggiorno", "carta di soggiorno", "ricevuta permesso", "rinnovo permesso", "in fase di rinnovo", "impronte digitali", "appuntamento questura", "residence permit", "permit receipt", "fingerprinting appointment", "police appointment" },
                                     new[] { "permesso", "soggiorno", "questura", "impronte", "rinnovo", "residence", "permit", "immigration", "receipt" }),
                [Topic.ISCRIZIONE] = (new[] { "verifica iscrizione", "certificato di iscrizione", "rinnovo iscrizione", "prima immatricolazione", "anno accademico", "attesa iscrizione accademia", "matricole specialistica non in possesso del titolo", "verify enrollment", "check enrollment", "enrollment certificate", "first enrollment", "academic year", "waiting for enrollment" },
                                       new[] { "iscrizione", "immatricolazione", "matricola", "iscritto", "iscritta", "enrollment", "enrolled", "matriculation" }),
                [Topic.TASSE] = (new[] { "tassa regionale", "pagamento tassa regionale", "mancato pagamento tassa regionale", "imposta regionale", "bollettino pagopa", "regional tax", "unpaid regional tax", "pagopa notice", "tax payment" },
                                  new[] { "tassa", "regionale", "pagopa", "iuv", "bollettino", "imposta", "tax", "payment" }),
                [Topic.CREDITI] = (new[] { "crediti insufficienti", "numero crediti maggiore rispetto al dpcm", "incongruenza tra crediti dichiarati e crediti riscontrati", "verifica crediti riconosciuti", "abbreviazione di carriera", "insufficient credits", "credits greater than dpcm", "inconsistency between declared credits and verified credits", "recognized credits check", "career shortening" },
                                    new[] { "crediti", "cfu", "dpcm", "verbalizzati", "riconoscimento", "credits", "recognized" }),
                [Topic.TIROCINIO] = (new[] { "attestazione tirocini mancante", "documentazione di tirocinio", "esame di tirocinio", "tirocinio convalidato", "crediti derivanti da tirocinio", "internship certificate missing", "internship documentation", "internship exam", "internship credits" },
                                      new[] { "tirocinio", "ade", "attestazione", "stage", "internship", "certificate" }),
                [Topic.ALLOGGIO] = (new[] { "posto alloggio", "assegnazione alloggio", "accettazione posto alloggio", "pre-accettazione", "check-in dormitorio", "residenza universitaria", "housing place", "dormitory check-in", "residence assignment", "accept housing" },
                                     new[] { "alloggio", "residenza", "dormitorio", "campus", "check-in", "housing", "dorm" }),
                [Topic.CONTRATTO] = (new[] { "contratto di locazione", "atto di subentro", "proroga del contratto", "registrazione del contratto", "agenzia delle entrate", "canone mensile", "cointestato", "rental contract", "lease contract", "lease renewal", "contract registration", "revenue agency", "monthly rent", "co-signed" },
                                      new[] { "contratto", "locazione", "subentro", "proroga", "registrazione", "canone", "lease", "rent", "renewal" }),
                [Topic.ISEE_REDDITI] = (new[] { "isee universita", "isee universitario", "iseeup", "dsu", "isee non presente in banca dati", "isee difforme", "isee corrente", "redditi esteri", "documentazione definitiva redditi esteri mancante", "ispdsu oltre il limite", "ispe oltre il limite", "isee parificato", "university isee", "dsu/isee", "foreign income documentation missing", "assets over the limit", "parificato isee" },
                                         new[] { "isee", "dsu", "caf", "redditi", "parificato", "ispe", "isp", "ispdsu", "income" }),
                [Topic.CARRIERA] = (new[] { "carriera pregressa", "verifica carriera pregressa", "riconoscimento cfu", "prima immatricolazione errata", "previous career check", "prior career", "recognition of credits", "wrong first enrollment year" },
                                     new[] { "pregressa", "trasferimento", "riconoscimento", "recognition", "transfer" }),
                [Topic.PORTALE] = (new[] { "http error 500", "problema tecnico sul sito", "impossibile generare qrcode", "nessuna variazione rilevata", "pagina non disponibile", "non consente di caricare", "sezione chiusa", "richiesta troppo grande", "il sito non funziona", "site not working", "cannot upload", "section closed", "request entity too large", "server error", "unable to generate qrcode" },
                                    new[] { "portale", "errore", "http", "bug", "bloccato", "server", "error", "site", "upload" }),
                [Topic.PEC_EMAIL] = (new[] { "indirizzo pec", "invio tramite pec", "accettate questa pec", "certified email" },
                                      new[] { "pec", "email", "posta", "certificata", "mail" }),
                [Topic.INDIPENDENTE] = (new[] { "condizione studente indipendente irregolare", "studente indipendente", "irregular independent student" },
                                         new[] { "indipendente", "independence", "independent" }),
                [Topic.DEBITORIA] = (new[] { "posizione debitoria nei confronti di discolazio", "posizione debitoria", "debt position" },
                                      new[] { "debitoria", "debito", "debts", "debt" }),
                [Topic.IMPORTI] = (new[] { "importo assegnato", "erogazione seconda rata", "saldo borsa di studio", "liquidazione del saldo", "differenza di importo", "bonus 20%", "quando verrà erogata la seconda rata", "second installment payment", "scholarship balance", "when will the second installment be paid", "amount difference", "disbursement" },
                                    new[] { "importo", "erogazione", "rata", "saldo", "pagamento", "liquidazione", "installment", "amount", "payment" }),
                [Topic.GRADUATORIA] = (new[] { "graduatoria definitiva", "graduatoria provvisoria", "idoneo e non vincitore", "scorrimento graduatoria", "quando usciranno le graduatorie definitive", "final ranking", "provisional ranking", "eligible not winner", "rank movement", "when will the final rankings be published" },
                                        new[] { "graduatoria", "idoneo", "vincitore", "scorrimento", "posizione", "ranking", "eligible", "winner" }),
                [Topic.BLOCCHI] = (new[] { "blocco pagamenti", "rimuovere il blocco", "sono presenti dei blocchi", "incongruenza tra", "mancato pagamento tassa regionale", "verifica iscrizione", "posizione debitoria", "condizione studente indipendente irregolare", "attestazione tirocini mancante", "remove the block", "there are blocks", "inconsistency", "unpaid regional tax", "verify enrollment", "debt position", "independent student irregular", "internship certificate missing" },
                                    new[] { "blocco", "blocchi", "sblocco", "incongruenza", "incongruenze", "minorenne", "block", "blocks", "unpaid", "inconsistency" }),
                [Topic.MENSA] = (new[] { "monetizzazione della mensa", "due pasti gratuiti", "tariffa massima", "fascia mensa", "servizio mensa", "rimborso mensa", "mensa gratuita", "canteen monetization", "two free meals", "maximum fee", "meal band", "canteen service", "free canteen" },
                                  new[] { "mensa", "monetizzazione", "pasto", "fascia", "tariffa", "accreditamento", "canteen", "meal" }),
                [Topic.QR_MENSA] = (new[] { "impossibile generare qrcode", "non riesco a creare il qr code", "tessera mensa scaduta", "problema qr code", "scanner per la mensa da errore", "cannot generate qrcode", "qr code problem", "card expired", "scanner error" },
                                     new[] { "qr", "qrcode", "app", "tessera", "scanner", "accreditamento", "code" }),
                [Topic.IBAN_CODICEFISCALE] = (new[] { "correzione del codice fiscale", "codice fiscale errato", "aggiornare il mio iban", "posso cambiare il mio iban", "wrong tax code", "update my iban", "can i change my iban" },
                                               new[] { "iban", "codice fiscale", "fiscale", "tax code" }),
                [Topic.CODICE_FISCALE] = ( // nuovo topic
                    new[]
                    {
                        "correzione codice fiscale",
                        "codice fiscale errato",
                        "codice fiscale non valido",
                        "variazione del codice fiscale",
                        "aggiornare il codice fiscale",
                        "omocodia codice fiscale",
                        "codice fiscale provvisorio",
                        "tax code correction",
                        "wrong tax code",
                        "invalid tax code",
                        "update the tax code",
                        "change my tax code",
                        "fiscal code change"
                    },
                    new[]
                    {
                        "codicefiscale",
                        "cf",
                        "taxcode",
                        "fiscalcode"
                    }
                ),
                [Topic.CONTRIBUTO_ALLOGGIO] = (new[] { "contributo alloggio", "bando contributo alloggio", "liquidazione del contributo alloggio", "saldo del contributo alloggio", "canone di locazione mensile", "cointestato", "quota a mio carico", "housing contribution", "rent contribution", "housing grant payment", "monthly rent", "my share" },
                                                new[] { "contributo", "alloggio", "canone", "mensile", "fatture", "trn", "cro", "rent", "contribution" }),
                [Topic.RIMBORSO_TASSA] = (new[] { "richiesta di rimborso della tassa regionale", "rimborso tassa regionale", "tempistiche rimborso tassa regionale", "regional tax refund request", "refund timing regional tax" },
                                           new[] { "rimborso", "tassa", "regionale", "restituzione", "refund", "regional" }),
                [Topic.DEPOSITO_ALLOGGIO] = (new[] { "rimborso del deposito cauzionale", "bagagli nel magazzino della residenza", "ritirare i bagagli dopo il 15 ottobre", "security deposit refund", "luggage in storage", "pick up luggage" },
                                              new[] { "deposito", "cauzionale", "bagagli", "magazzino", "deposit", "luggage" }),
                [Topic.DOV_CIMEA] = (new[] { "dichiarazione di valore", "verifica dichiarazione di valore", "cimea statement of comparability", "titolo estero", "statement of comparability", "declaration of value", "foreign qualification" },
                                      new[] { "dov", "cimea", "comparability", "qualifica", "titolo", "estero", "foreign", "recognition" }),
                [Topic.PASSAGGIO_TRASF] = (new[] { "passaggio o trasferimento in corso", "trasferimento di ateneo", "modifica dell'universita", "cambio facolta", "cambio corso", "transfer in progress", "change of university", "course change", "faculty change" },
                                            new[] { "passaggio", "trasferimento", "cambio", "transfer", "switch" }),
                [Topic.RINUNCIA_REVOCA] = (new[] { "rinuncia ai benefici", "revoca della borsa di studio", "restituzione della borsa di studio", "rateizzare la restituzione", "riammissione dopo rinuncia", "waiver of benefits", "scholarship revocation", "repayment of scholarship", "installments repayment", "readmission after waiver" },
                                            new[] { "rinuncia", "revoca", "restituzione", "rateizzazione", "riammissione", "waiver", "revocation", "repayment" }),
                [Topic.MOBILITA_ERASMUS] = (new[] { "contributo per la mobilita internazionale", "studente erasmus", "learning agreement", "arrival certificate", "acceptance letter", "compatibile con il premio di laurea", "erasmus mobility contribution", "mobility grant", "is it compatible with graduation award" },
                                             new[] { "erasmus", "mobilita", "mobility", "learning", "arrival", "laurea", "award" }),
            };
        }

        private static readonly (string bad, string good)[] Canon =
        {
            ("sogiorno", "soggiorno"),
            ("isee universita", "isee università"),
            ("resident permit", "residence permit"),
            ("qr code", "qrcode")
        };

        private static Lang DetectLang(string norm)
        {
            if (string.IsNullOrEmpty(norm)) return Lang.UNKNOWN;

            string[] itHints = { " il ", " la ", " lo ", " gli ", " le ", " un ", " una ", " di ", " che ", " non ", " per ", " con ", " alla ", " della ", "tassa", "iscrizione", "credito", "graduatoria", "borsa" };
            string[] enHints = { " the ", " a ", " an ", " of ", " for ", " with ", " and ", " but ", " block", " blocks", " enrollment", " scholarship", " refund", " permit" };

            int itScore = itHints.Sum(h => Regex.Matches(" " + norm + " ", h, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
            int enScore = enHints.Sum(h => Regex.Matches(" " + norm + " ", h, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);

            if (itScore == 0 && enScore == 0) return Lang.UNKNOWN;
            if (itScore > 0 && enScore > 0) return Lang.MIXED;
            return itScore > enScore ? Lang.IT : Lang.EN;
        }

        public static ExtractionV6 Extract(string raw, Lang? preferred = null)
        {
            var ex = new ExtractionV6 { OriginalText = raw ?? "" };
            if (string.IsNullOrWhiteSpace(raw)) return ex;

            var text = Normalize(raw);
            ex.NormalizedText = text;

            foreach (var (bad, good) in Canon) text = text.Replace(bad, good);

            ex.DetectedLanguage = preferred is { } p && p != Lang.UNKNOWN
                                  ? (p == Lang.MIXED ? DetectLang(text) : p)
                                  : DetectLang(text);

            var tokens = new HashSet<string>(
                Regex.Matches(text, @"[\p{L}\p{N}]+", RegexOptions.IgnoreCase)
                     .Select(m => m.Value)
                     .Where(t => t.Length > 1)
            );

            foreach (var kv in Dict)
            {
                var topic = kv.Key;
                int count = 0;

                foreach (var phrase in kv.Value.multi)
                {
                    if (string.IsNullOrWhiteSpace(phrase)) continue;
                    var pat = Regex.Escape(phrase);
                    var rx = new Regex($@"(?<![\p{{L}}\p{{N}}]){pat}(?![\p{{L}}\p{{N}}])",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    var matches = rx.Matches(text);
                    if (matches.Count > 0) count += PHRASE_WEIGHT * matches.Count;
                }

                foreach (var w in kv.Value.single)
                {
                    if (string.IsNullOrWhiteSpace(w)) continue;
                    if (tokens.Contains(w)) count += WORD_WEIGHT;
                }

                ex.Counts[topic] = count;
            }

            ex.MentionsBlocks = Regex.IsMatch(
                text,
                @"\b(blocchi?|sblocco|incongruen[zt]e?|mancato pagamento|verifica iscrizione|posizione debitoria|indipendente\s+irregolare|attestazione\s+tirocini\s+mancante|block(s)?|unpaid|inconsisten(cy|cies)|verify\s+enrollment)\b",
                RegexOptions.IgnoreCase);

            ex.MentionsOwnPosition = Regex.IsMatch(
                text,
                @"\b(io|mio|mia|miei|mie|la mia posizione|nel mio profilo|nella mia area|risulto|sono idone[oa]|sono vincitor[ei]|mi risulta|i\s+am|i'm|my\s+position|my\s+profile|in\s+my\s+area|i\s+am\s+eligible|i\s+am\s+a\s+winner)\b",
                RegexOptions.IgnoreCase);

            void Boost(Topic t, int amount) =>
                ex.Counts[t] = ex.Counts.TryGetValue(t, out var v) ? v + amount : amount;

            if (ex.MentionsBlocks) Boost(Topic.BLOCCHI, BOOST_STRONG);

            if (Regex.IsMatch(text, @"\bmancato\s+pagamento\s+tassa\s+regionale\b|tassa\s+regionale\b|unpaid\s+regional\s+tax|regional\s+tax", RegexOptions.IgnoreCase))
                Boost(Topic.TASSE, BOOST_MED);

            if (Regex.IsMatch(text, @"\bverifica\s+iscrizione\b|certificat[oi]\s+di\s+iscrizione|immatricolazione|verify\s+enrollment|enrollment\s+certificate", RegexOptions.IgnoreCase))
                Boost(Topic.ISCRIZIONE, BOOST_MED);

            if (Regex.IsMatch(text, @"\b(incongruen[zt]e?|numero\s+crediti|cfu|dpcm|credits|inconsisten(cy|cies))\b", RegexOptions.IgnoreCase))
                Boost(Topic.CREDITI, BOOST_MED);

            if (Regex.IsMatch(text, @"\b(qr|qrcode|tessera\s+mensa|qr\s+code)\b", RegexOptions.IgnoreCase))
                Boost(Topic.QR_MENSA, BOOST_MED);

            if (Regex.IsMatch(text, @"\bmensa|pasto|monetizzazione|canteen|meal\b", RegexOptions.IgnoreCase))
                Boost(Topic.MENSA, WORD_WEIGHT);

            if (Regex.IsMatch(text, @"\bcontributo\s+alloggio|canone|locazione|housing\s+contribution|rent\b", RegexOptions.IgnoreCase))
                Boost(Topic.CONTRIBUTO_ALLOGGIO, WORD_WEIGHT);

            if (Regex.IsMatch(text, @"\bcodice\s*fiscale\b|tax\s*code|fiscal\s*code", RegexOptions.IgnoreCase))
                Boost(Topic.CODICE_FISCALE, BOOST_MED);

            var rankable = ex.Counts.ToDictionary(k => k.Key, v => v.Value);
            if (ex.MentionsBlocks && rankable.ContainsKey(Topic.BLOCCHI))
                rankable[Topic.BLOCCHI] = int.MinValue;

            var ranked = rankable
                .OrderByDescending(x => x.Value)
                .ThenBy(x => Array.IndexOf(PRECEDENCE, x.Key))
                .ToList();

            var positive = ranked.Where(r => r.Value > 0).ToList();

            if (positive.Count > 0)
            {
                ex.TopicPrimary = positive[0].Key.ToString();
                if (positive.Count > 1) ex.TopicSecondary = positive[1].Key.ToString();

                if (positive.Count > 1)
                {
                    var a = positive[0]; var b = positive[1];
                    if (Math.Abs(a.Value - b.Value) <= 1)
                    {
                        var winner = (Array.IndexOf(PRECEDENCE, a.Key) <= Array.IndexOf(PRECEDENCE, b.Key)) ? a : b;
                        ex.TopicPrimary = winner.Key.ToString();

                        var newOrder = positive
                            .OrderByDescending(x => x.Key == winner.Key)
                            .ThenByDescending(x => x.Value)
                            .ThenBy(x => Array.IndexOf(PRECEDENCE, x.Key))
                            .ToList();

                        if (newOrder.Count > 1)
                            ex.TopicSecondary = newOrder[1].Key.ToString();
                    }
                }
            }

            if (ex.MentionsBlocks)
            {
                ex.TopicTertiary = "SI";
            }
            else
            {
                ex.TopicTertiary = "";
            }

            return ex;
        }
    }
}
