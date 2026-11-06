using ProcedureNet7;
using System.Reflection;

internal static class AssegnazioniPaRegressionTests
{
    internal static int Run(ProceduraPagamenti procedure)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ProceduraPagamenti);
        (StudentePagamenti Studente, object? Esclusione, string? Nota) Apply(string mode, string start, string end,
            string reference = "15/03/2026", bool fuoriCorso = false, StudentePagamenti? student = null, string fine = "")
        {
            type.GetField("selectedTipoProcedura", flags)!.SetValue(procedure, mode);
            type.GetField("selectedDataRiferimento", flags)!.SetValue(procedure, reference);
            student ??= new StudentePagamenti();
            var data = new AssegnazionePaDto
            {
                CodPensionato = "01", CodStanza = "101", TipoStanza = "01", ImportoMensileStr = "300",
                DataDecorrenza = start, DataFineAssegnazione = end, IdAssegnazionePa = "PA-1", CodFineAssegnazione = fine
            };
            object?[] parameters = { student, data, new DateTime(2025, 10, 1), new DateTime(2026, 7, 31), fuoriCorso, null };
            object? exclusion = type.GetMethod("AddAssegnazioneSaldo", flags)!.Invoke(procedure, parameters);
            return (student, exclusion, parameters[5] as string);
        }

        var future = Apply("5", "01/10/2025", "31/07/2026");
        var assignment = future.Studente.InformazioniPagamento.Assegnazioni.Single();
        Check(future.Esclusione == null && assignment.dataFineAssegnazione == new DateTime(2026, 3, 15),
            "La simulazione PA tollerante deve calcolare solo fino alla data di estrazione, anche se l'assegnazione termina dopo.");
        Check(Math.Abs(assignment.costoTotale - 165 * 300 / 30.4375) < 0.000001,
            "La trattenuta deve conservare la tariffa giornaliera ordinaria e usare solo i giorni maturati.");
        Check(future.Nota?.Contains("31/07/2026") == true && future.Nota.Contains("15/03/2026"),
            "Il report deve conservare sia le date originali sia quelle impiegate nella simulazione.");

        foreach (string end in new[] { "", "non valida" })
        {
            var open = Apply("5", "01/10/2025", end);
            Check(open.Esclusione == null && open.Studente.InformazioniPagamento.Assegnazioni.Single().dataFineAssegnazione == new DateTime(2026, 3, 15)
                && open.Nota != null, "La fine assente o non valida deve usare la data di estrazione senza escludere lo studente.");
        }
        foreach (string start in new[] { "", "non valida", "01/09/2025" })
        {
            var missing = Apply("5", start, "28/02/2026");
            Check(missing.Esclusione == null && missing.Studente.InformazioniPagamento.Assegnazioni.Single().dataDecorrenza == new DateTime(2025, 10, 1),
                "La decorrenza mancante o precedente al periodo PA deve usare il limite iniziale del periodo.");
        }
        var earlierEnd = Apply("5", "01/10/2025", "28/02/2026");
        Check(earlierEnd.Esclusione == null && earlierEnd.Studente.InformazioniPagamento.Assegnazioni.Single().dataFineAssegnazione == new DateTime(2026, 2, 28)
            && earlierEnd.Nota == null, "Una fine valida precedente all'estrazione deve restare invariata.");
        var periodEnd = Apply("5", "01/10/2025", "31/12/2026", "31/08/2026");
        Check(periodEnd.Esclusione == null && periodEnd.Studente.InformazioniPagamento.Assegnazioni.Single().dataFineAssegnazione == new DateTime(2026, 7, 31),
            "La simulazione deve fermarsi alla fine del periodo PA quando precede l'estrazione.");
        var openAfterPeriod = Apply("5", "01/10/2025", "", "31/08/2026");
        Check(openAfterPeriod.Esclusione == null && openAfterPeriod.Studente.InformazioniPagamento.Assegnazioni.Single().dataFineAssegnazione == new DateTime(2026, 7, 31),
            "Anche un'assegnazione aperta deve rispettare la fine del periodo PA.");

        foreach (var dates in new[] {
            ("01/03/2026", "01/02/2026", "15/03/2026"), // invertite
            ("01/02/2026", "01/02/2026", "15/03/2026"), // coincidenti
            ("01/04/2026", "31/07/2026", "15/03/2026"), // non ancora iniziata
            ("01/08/2025", "31/08/2025", "15/03/2026"), // prima del periodo PA
            ("01/10/2025", "31/07/2026", "01/09/2025") // estrazione prima del periodo PA
        })
        {
            var empty = Apply("5", dates.Item1, dates.Item2, dates.Item3);
            var a = empty.Studente.InformazioniPagamento.Assegnazioni.Single();
            Check(empty.Esclusione == null && a.costoTotale == 0 && a.dataDecorrenza == a.dataFineAssegnazione && empty.Nota != null,
                "Un periodo senza giorni maturati deve avere trattenuta zero, senza esclusione né durata negativa.");
        }
        foreach (string fine in new[] { "02", "03" })
        {
            var inclusive = Apply("5", "01/03/2026", "31/07/2026", fine: fine);
            Check(Math.Abs(inclusive.Studente.InformazioniPagamento.Assegnazioni.Single().costoTotale - 15 * 300 / 30.4375) < 0.000001,
                "La nuova modalità deve mantenere il giorno finale incluso per i codici di fine 02 e 03.");
        }
        var first = Apply("5", "01/10/2025", "01/01/2026", "30/06/2026", true);
        var second = Apply("5", "01/01/2026", "31/07/2026", "30/06/2026", true, first.Studente);
        Check(first.Esclusione == null && second.Esclusione == null &&
            Math.Abs(second.Studente.InformazioniPagamento.Assegnazioni.Sum(a => a.costoTotale) - 165 * 300 / 30.4375) < 0.000001,
            "La simulazione deve mantenere il limite complessivo di 165 giorni dei fuori corso su più assegnazioni.");

        foreach (string mode in new[] { "0", "3", "4" })
        {
            Check(Apply(mode, "01/10/2025", "").Esclusione != null, "Le modalità precedenti devono continuare a escludere la fine mancante.");
            Check(Apply(mode, "", "28/02/2026").Esclusione != null, "Le modalità precedenti devono continuare a escludere l'inizio mancante.");
            Check(Apply(mode, "01/03/2026", "01/02/2026").Esclusione != null, "Le modalità precedenti devono continuare a escludere date invertite.");
            Check(Apply(mode, "01/09/2025", "28/02/2026").Esclusione != null, "Le modalità precedenti devono continuare a segnalare l'inizio fuori periodo.");
            Check(Apply(mode, "01/10/2025", "31/12/2026").Esclusione != null, "Le modalità precedenti devono continuare a segnalare la fine fuori periodo.");
            var unchanged = Apply(mode, "01/10/2025", "31/07/2026");
            Check(unchanged.Esclusione == null && unchanged.Nota == null &&
                unchanged.Studente.InformazioniPagamento.Assegnazioni.Single().dataFineAssegnazione == new DateTime(2026, 7, 31),
                "Il calcolo delle modalità precedenti deve restare invariato, senza applicare il nuovo limite di estrazione.");
        }
        return checks;
    }
}
