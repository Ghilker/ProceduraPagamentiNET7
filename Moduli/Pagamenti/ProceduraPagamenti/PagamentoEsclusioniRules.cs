using System.Globalization;

namespace ProcedureNet7;

internal static class PagamentoEsclusioniRules
{
    private static string Euro(double value) => value.ToString("C2", CultureInfo.GetCultureInfo("it-IT"));

    internal static string AnnoAccademico(string anno)
        => anno.Length == 8 ? $"{anno[..4]}/{anno[4..]}" : anno;

    internal static MotivoEsclusionePagamento DifferenzaEntroSoglia(double dovuto, double pagato, string descrizione)
    {
        double differenza = Math.Round(dovuto - pagato, 2);
        string motivo = differenza == 0 ? "Nessuna differenza rispetto al già pagato"
            : differenza < 0 ? "Differenza negativa entro 5 €" : "Residuo inferiore a 5 €";
        return new(motivo,
            $"{descrizione}: {Euro(dovuto)}. Già pagato: {Euro(pagato)}. Differenza: {Euro(differenza)}. " +
            (differenza == 0 ? "Gli importi coincidono."
                : differenza < 0 ? "Il già pagato supera l'importo di riferimento di meno di 5,00 €. Non viene emesso un nuovo pagamento."
                : "Il residuo è inferiore alla soglia minima di pagamento di 5,00 €."), differenza);
    }

    internal static MotivoEsclusionePagamento ImportoNonLiquidabile(double netto, string dettaglio)
        => new(netto < 0 ? "Importo netto negativo" : netto == 0 ? "Nessun importo residuo" : "Residuo inferiore a 5 €",
            $"Importo netto da liquidare: {Euro(netto)}. " +
            (netto > 0 ? "Il residuo è inferiore alla soglia minima di pagamento di 5,00 €. " : "") + dettaglio, netto);

    internal static string DettaglioCalcolo(double primaDelleTrattenute, double pagato, double alloggio, double detrazioni, double reversali)
    {
        var parti = new List<string> { $"Importo prima delle trattenute: {Euro(primaDelleTrattenute)}" };
        if (pagato != 0) parti.Add($"già pagato: {Euro(pagato)}");
        if (alloggio != 0) parti.Add($"trattenuta posto alloggio: {Euro(alloggio)}");
        if (detrazioni != 0) parti.Add($"altre detrazioni: {Euro(detrazioni)}");
        if (reversali != 0) parti.Add($"reversali: {Euro(reversali)}");
        return string.Join("; ", parti) + ".";
    }

    internal static MotivoEsclusionePagamento ImportiNonCoincidenti(double? beneficio, double? assegnato)
    {
        if (!beneficio.HasValue || !assegnato.HasValue)
            return new("Importi da confrontare incompleti",
                $"Importo del beneficio: {(beneficio.HasValue ? Euro(beneficio.Value) : "non registrato")}. " +
                $"Importo assegnato contabilmente: {(assegnato.HasValue ? Euro(assegnato.Value) : "non registrato")}. " +
                "Il confronto necessario per questo pagamento non può essere completato.");
        return new("Importo del beneficio diverso dall'assegnato",
            $"Importo del beneficio: {Euro(beneficio.Value)}. Importo assegnato contabilmente: {Euro(assegnato.Value)}. " +
            $"Differenza: {Euro(beneficio.Value - assegnato.Value)}. Per questo pagamento i due importi devono coincidere.");
    }

    internal static MotivoEsclusionePagamento RiemissioneNonAmmessa(string prefisso, string progressivo,
        IEnumerable<Pagamento>? pagamenti, Func<string, string> descrizione)
    {
        string? precedente = progressivo switch { "1" => "0", "7" => "6", "A" => "9", "2" => "1", "B" => "A", "8" => "7", _ => null };
        if (precedente == null)
            return new("Riemissione non prevista", "La tipologia selezionata non ha un pagamento precedente previsto dalla sequenza delle riemissioni.");
        string codice = prefisso + precedente;
        var presenti = pagamenti?.Where(p => p.codTipoPagam == codice).ToList() ?? new List<Pagamento>();
        return presenti.Count == 0
            ? new("Pagamento precedente assente",
                $"Non risulta «{descrizione(codice)}», il pagamento che deve precedere la riemissione selezionata.")
            : new("Pagamento precedente non stornato",
                $"Risulta {PagamentiDescritti(presenti, descrizione)}, senza storno (ritiro da parte dell'azienda). " +
                "La riemissione richiede che il pagamento precedente sia stornato (ritirato dall'azienda).");
    }

    internal static MotivoEsclusionePagamento IntegrazioneNonAmmessa(string beneficio, string tipo,
        IEnumerable<Pagamento>? pagamenti, Func<string, string> descrizione)
    {
        string[] precedenti = tipo switch
        {
            "I0" => new[] { "P0", "P1", "P2" }, "I9" => new[] { "S0", "S1", "S2" }, "II" => new[] { "I9" }, _ => Array.Empty<string>()
        };
        if (precedenti.Length == 0)
            return new("Integrazione non prevista", "La tipologia selezionata non ha un pagamento precedente previsto per l'integrazione.");
        var codici = precedenti.Select(p => beneficio + p).ToHashSet();
        var presenti = pagamenti?.Where(p => codici.Contains(p.codTipoPagam)).ToList() ?? new List<Pagamento>();
        string necessario = tipo switch
        {
            "I0" => "una prima rata, anche riemessa", "I9" => "un saldo, anche riemesso", _ => "un'integrazione del saldo"
        };
        return presenti.Count == 0
            ? new("Pagamento da integrare assente", $"Per questa integrazione è necessario che risulti {necessario}. Il pagamento richiesto non è presente.")
            : new("Pagamento da integrare stornato",
                $"Pagamenti precedenti: {PagamentiDescritti(presenti, descrizione)}. Risultano stornati (ritirati dall'azienda), quindi non possono essere integrati.");
    }

    private static string PagamentiDescritti(IEnumerable<Pagamento> pagamenti, Func<string, string> descrizione)
        => string.Join("; ", pagamenti.Select(p => $"«{descrizione(p.codTipoPagam)}» di {Euro(p.importoPagamento)}").Distinct());

    internal static bool VincitoreSenzaAssegnazione(int esitoPA, bool haAssegnazione)
        => esitoPA == 2 && !haAssegnazione;

    internal static MotivoEsclusionePagamento TassaRegionaleNonAmmessa(IEnumerable<Pagamento> pagamenti,
        int annoCorso, bool superamentoBorsa, bool superamentoTassa)
    {
        var precedenti = pagamenti.Where(p => p.codTipoPagam is "BSP0" or "BSP1" or "BSP2" or "BSS0" or "BSS1" or "BSS2").ToList();
        var validi = precedenti.Where(p => !p.ritiratoAzienda).ToList();
        if (validi.Count == 0)
            return new(precedenti.Count == 0 ? "Pagamento necessario al rimborso assente" : "Pagamento necessario al rimborso stornato",
                precedenti.Count == 0
                    ? "Non risulta una prima rata o un saldo della borsa ammesso al rimborso della tassa regionale."
                    : "Le prime rate e i saldi della borsa risultano tutti stornati (ritirati dall'azienda). Non è presente un pagamento valido per il rimborso della tassa regionale.");

        bool haSaldo = validi.Any(p => p.codTipoPagam.StartsWith("BSS", StringComparison.Ordinal));
        string dettaglio = haSaldo
            ? $"Risulta un saldo della borsa non stornato. Anno di corso registrato: {annoCorso}. " +
              $"Superamento esami per la borsa: {(superamentoBorsa ? "registrato" : "non registrato")}; " +
              $"per la tassa regionale: {(superamentoTassa ? "registrato" : "non registrato")}. " +
              "Con questa posizione di iscrizione il rimborso richiede la registrazione di almeno uno dei due requisiti di merito."
            : "Risulta una prima rata della borsa non stornata. Per il rimborso basato sulla prima rata manca il superamento degli esami specifico per la tassa regionale.";
        return new("Merito non registrato per il rimborso", dettaglio);
    }

    internal static MotivoEsclusionePagamento AssegnazioneMancante(string annoAccademico)
        => new("Nessuna assegnazione utilizzabile per il pagamento",
            $"Lo studente è vincitore di posto alloggio per l'A.A. {AnnoAccademico(annoAccademico)}, " +
            "ma non risulta un'assegnazione accettata e attiva da utilizzare per questo pagamento.");
}
