namespace ProcedureNet7;

internal static class PagamentoFlussoRules
{
    internal static MotivoEsclusionePagamento? ImpegnoNonElaborato(string? impegno,
        string impegnoSelezionato, IReadOnlyCollection<string> impegniDaElaborare)
    {
        // Use the same exact comparison as ProcessImpegno when selecting students.
        if (impegniDaElaborare.Contains(impegno, StringComparer.Ordinal)) return null;
        if (string.IsNullOrWhiteSpace(impegno))
            return new("Impegno di spesa non disponibile",
                "Lo studente non ha un impegno di spesa assegnato e non rientra in alcun gruppo per la generazione dei flussi.");
        if (impegnoSelezionato != "0000")
            return new("Impegno diverso da quello selezionato",
                $"Impegno dello studente: {impegno}. Impegno selezionato per questo pagamento: {impegnoSelezionato}. " +
                "I flussi vengono generati solo per l'impegno selezionato.");
        return new("Impegno non compreso tra quelli da elaborare",
            $"Impegno dello studente: {impegno}. " +
            (impegniDaElaborare.Count == 0
                ? "L'elenco degli impegni da elaborare è vuoto; non viene generato alcun flusso."
                : $"Impegni previsti per questo pagamento: {string.Join(", ", impegniDaElaborare)}. " +
                  "L'impegno dello studente non è nell'elenco e non viene elaborato."));
    }

    internal static MotivoEsclusionePagamento? AnnoCorsoNonElaborato(int annoCorso,
        bool processMatricole, bool processAnniSuccessivi)
    {
        bool matricola = annoCorso == 1;
        if (matricola ? processMatricole : processAnniSuccessivi) return null;
        return new("Anno di corso escluso dal filtro dei flussi",
            $"Anno di corso dello studente: {annoCorso} ({(matricola ? "matricola" : "anno successivo al primo")}). " +
            (processMatricole ? "La generazione dei flussi è limitata alle matricole."
                : processAnniSuccessivi ? "La generazione dei flussi è limitata agli anni successivi al primo."
                : "Non è stata abilitata la generazione dei flussi per alcun anno di corso."));
    }

    internal static MotivoEsclusionePagamento MancatoInserimento(bool completata)
        => completata
            ? new("Mancato inserimento da verificare",
                "I controlli sono terminati, ma non risulta un file di pagamento generato per lo studente né una causa registrata " +
                "di mancato inserimento. Il motivo non è determinabile dai dati disponibili; consultare il log di generazione dei flussi.")
            : new("Generazione del flusso non completata",
                "L'elaborazione si è interrotta prima dell'inserimento in un flusso; non è disponibile un esito definitivo. " +
                "Consultare il log della procedura per il punto di interruzione.");
}
