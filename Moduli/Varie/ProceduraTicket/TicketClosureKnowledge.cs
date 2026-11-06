using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>Vocabolario deterministico e codici verificati nel catalogo ADISU.</summary>
    internal static class TicketClosureKnowledge
    {
        public const string Version = "2026.11.02.2";
        private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);
        internal sealed record BlockFamily(string Code, string Pattern, string Codes);
        internal static readonly BlockFamily[] BlockFamilies =
        {
            new("ISCRIZIONE", @"\biscrizion\w*|\bimmatricol\w*|\benrol\w*|\bregistration\b", "BVI ACD VII ISP IMR TRI VSF PTC ACH IDT VST BLS IAD IAI ITD VAI VDI"),
            new("TASSA", @"tass[ae] regional[ei]|regional tax|\bBTR\b", "BTR IDT"),
            new("PERMESSO", @"permesso (?:di )?soggiorno|titolo di soggiorno|residen(?:ce|t) permit|\bpds\b|\bquestura\b|\bBPP\b", "BPP ACA ACO BIP BRP CA8 MDP PS1 PSD PSR VPS"),
            new("REDDITI", @"\bisee\w*|\bisse\b|\bispe\w*|\breddit\w*|\bcaf\b|foreign income|income document\w*|\bBDR\b", "BDR AC4 BRE AC1 AC2 BDF BDI BRI BSD BVE INU IOD RDC VBS VDA VIS VMB VMN"),
            new("MERITO", @"\bcfu\b|\bcredit[io]\b|\bcredits?\b|\besam\w*|\bexams?\b|tirocinio|internship|(?:requisit\w*|dati|verifica|blocco) (?:di |del |sul )?merito|merit requirements", "ADE BBD BCC BCR BET BIC BMI CMA CRC IMD RCP VCR VPM VPT VTR BIT BME BRT BVB CEE MOD VAR VES"),
            new("INDIPENDENTE", @"studente indipendente|independent student|\bBSI\b", "BSI"),
            new("DOV", @"\bdov\b|\bcimea\b|dichiarazione di valore|comparabilita|career shortening|foreign qualification|abbreviazione di carriera", "DV1 DV2 DV3 VTC VRA"),
            new("TITOLO", @"matricol\w* specialistic\w*|non in possesso del titolo|conseguimento (?:del )?titolo|bachelor.{0,20}(?:degree|certificate)|titolo triennale", "BAT BIS BRT BST CA3 IAT VTC"),
            new("CONTRATTO", @"contratto.{0,30}(?:scadenz\w*|valid\w*)|(?:scadenz\w*|valid\w*).{0,30}contratto|\bVVC\b|\bVCA\b", "VCA VVC CA5 CA2"),
            new("IBAN", @"\biban\b|bank account|modalita di pagamento", "ACC BS1 BSS BNL AC7 ACI NOL"),
            new("STATUS_SEDE", @"status.{0,15}sede|fuori\s?sede|pendolare|residenza", "ACE BVS VCP RSS BSD CA5"),
            new("CARRIERA", @"carriera pregressa|anno di corso|tipologia di corso|course year|previous career", "BCP CNG IAD IAI ITD CEO VAI VAR VCS VDI VVO RIB"),
            new("ALTRA_BORSA", @"altra borsa|altre borse|borsa.{0,12}inps|another scholarship", "ACL BAR PAB VB1 VBB VAB VCB"),
            new("DISABILITA", @"disabil\w*|invalid\w*|disability", "BCD DSD VSD"),
            new("PREMIO_LAUREA", @"premio (?:di )?laurea|graduation (?:award|bonus)", "PL1 PL2 PL3"),
            new("PEC", @"\bpec\b|posta elettronica certificata", "PEC")
        };
        // Gli altri codici del catalogo hanno significati distinti dalle famiglie sopra.
        private static readonly HashSet<string> KnownCodes = new(BlockFamilies.SelectMany(f => f.Codes.Split(' ')).Concat(
            "AC3 AC5 AC6 AC8 AC9 ACB ACF ACG ACM ACN ACP BBP BCS BDP BDS BGP BID BIF BIZ BKL BMP BOR BPA BPB BPC BPD BPR BPS BRB BRM BSM CA1 CA4 CA6 CA7 CC1 CCD CVS DIP DPV EPA ESL IPA LIQ PAC PD1 PD2 PDM PMP PN1 PN2 PN3 PN4 PN5 PRC PRI PRT RAR RDV ROT RSU SL1 SLA URC VBE VBL VCD VCF VCT".Split(' ')), StringComparer.Ordinal);

        public static string Normalize(string value)
        {
            string text = WebUtility.HtmlDecode(value ?? "");
            text = Regex.Replace(text, @"<br\s*/?>|</(?:p|div)\s*>", "; ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ").Replace('|', ';').Replace('’', '\'').Replace('‘', '\'');
            // Riparare solo gli apostrofi fra prefisso italiano e parola: i veri '?' restano separatori.
            text = Regex.Replace(text, @"\b(dell|all|nell|sull|dall|d|l|un|quest)\?(?=[a-zà-ÿ])", "$1'", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\ba\s*\.\s*a\s*\.", "anno accademico", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:gent\.le|spett\.le)", "gentile", RegexOptions.IgnoreCase);
            return Regex.Replace(TicketDomainText.Normalize(text), @"[\t ]+", " ").Trim();
        }

        public static bool Match(string text, string pattern) => Patterns.GetOrAdd(pattern,
            p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).IsMatch(text);
        public static IEnumerable<string> Codes(TicketOfficeRecord record) => record.ActiveBlockCodes
            .Split(new[] { '|', ',', ';', ' ', '#' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToUpperInvariant()).Distinct();
        public static bool Absent(TicketOfficeRecord? record, BlockFamily family) => record?.HasBlockCodeData == true && Codes(record).All(KnownCodes.Contains) &&
            !Codes(record).Intersect(family.Codes.Split(' '), StringComparer.Ordinal).Any();
        public static string BlockEvidence(TicketOfficeRecord? record, BlockFamily? family)
        {
            if (record?.HasBlockCodeData != true) return "Dati dei codici blocco non disponibili per l'anno richiesto.";
            string active = string.Join(", ", Codes(record));
            string unknown = string.Join(", ", Codes(record).Where(c => !KnownCodes.Contains(c)));
            if (unknown.Length > 0) return "Codici blocco non classificati: " + unknown + "; verificare la pertinenza prima della chiusura.";
            string relevant = family == null ? active : string.Join(", ", Codes(record).Intersect(family.Codes.Split(' '), StringComparer.Ordinal));
            return $"Codici attivi: {(active.Length == 0 ? "nessuno" : active)}; pertinenti{(family == null ? "" : " a " + family.Code)}: {(relevant.Length == 0 ? "nessuno" : relevant)}.";
        }
        public static bool Technical(string text) => Match(text,
            @"\b(?:error\w*|impossibil\w*|unable|cannot|bug)\b|non (?:riesc|riusc)\w*|non (?:mi |me lo |me la |lo |la )?(?:permett\w*|consent\w*)|non funzion\w*|non compar\w*|non appar\w*|non esce|non.{0,35}(?:disponibile|presente|possibile)|non ho trovato.{0,25}possibilita|non (?:mi )?da (?:l'|la |il |alcun)|non posso.{0,25}(?:caricar|inserir|modificar|allegar|acceder)|(?:soltanto|solo).{0,25}(?:consult\w*|rinunci\w*)|problematiche|can(?:not|'t) (?:upload|submit|change)");
        public static bool GeneralQuestion(string text) => Match(text,
            @"\bquando\b(?!\s+(?:ho|avevo|compilai|mi sono|presentai)\b)|\b(?:quali|quanti|when|how)\b|\bcome (?:funzion\w*|posso|si |fare|faccio|devo|viene|vengono)|(?:informazion\w*|sapere|chiedo|richiedo|conoscere).{0,60}(?:requisiti|scadenze|tempistiche|scorrimenti)|informazioni generali");
        public static bool FinancialDispute(string text) => Match(text,
            @"(?:revisione|riesame|ricalcol\w*|maggiorazion\w*|non.{0,12}maggiorat\w*).{0,50}(?:importo|borsa|rata)|(?:importo|borsa|rata).{0,65}(?:errat\w*|inferiore|sbagliat\w*|divers\w*|maggiorat\w*|revisione)|meno del previsto|(?:errore|error\w*).{0,30}(?:calcolo|trattenut\w*|detrazion\w*)|(?:trattenut\w*|detrazion\w*).{0,40}(?:errat\w*|sbagliat\w*|stran\w*)|misura inferiore|integrare.{0,25}importo|manca.{0,30}monetizzazione|(?:wrong|incorrect|lower).{0,20}amount|amount.{0,25}(?:wrong|incorrect|lower)|less than (?:expected|due)|(?:review|recalculate).{0,30}(?:amount|scholarship)");
        public static bool PersonalAnomaly(string text) => FinancialDispute(text) || Match(text,
            @"risult\w*.{0,25}(?:esclus\w*|ancora idone\w*)|(?:mia|mio|my).{0,40}(?:errat\w*|sbagliat\w*|discrepanz\w*)|non risult\w*.{0,25}(?:graduator\w*|registrat\w*|iscritt\w*)|non (?:mi )?e chiaro|incongruen\w*|discrepanz\w*|ricorso|contest\w*|riesamin\w*|non corrispond\w*|non.{0,25}riconosciut\w*|risult\w*.{0,25}pendolare");
    }
}
