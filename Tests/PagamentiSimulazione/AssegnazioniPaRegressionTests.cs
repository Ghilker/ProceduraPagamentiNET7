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
        return checks + RunAcconto(procedure);
    }

    private static int RunAcconto(ProceduraPagamenti procedure)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(ProceduraPagamenti);
        var fields = new[] { "categoriaPagam", "isIntegrazione", "isRiemissione", "studentiDaPagare" }
            .Select(name => type.GetField(name, flags)!).ToDictionary(field => field, field => field.GetValue(procedure));
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        StudentePagamenti Student(int esito = 2)
        {
            var student = new StudentePagamenti();
            student.InformazioniPersonali.CodFiscale = "RSSMRA00A01H501A";
            student.InformazioniBeneficio.EsitoPA = esito;
            student.InformazioniIscrizione.AnnoCorso = 1;
            return student;
        }
        bool Populate(StudentePagamenti student, string category = "PR", bool integration = false, bool reissue = false,
            IEnumerable<AssegnazionePaDto>? assegnazioni = null)
        {
            var students = new Dictionary<string, StudentePagamenti> { [student.InformazioniPersonali.CodFiscale] = student };
            type.GetField("categoriaPagam", flags)!.SetValue(procedure, category);
            type.GetField("isIntegrazione", flags)!.SetValue(procedure, integration);
            type.GetField("isRiemissione", flags)!.SetValue(procedure, reissue);
            type.GetField("studentiDaPagare", flags)!.SetValue(procedure, students);
            assegnazioni ??= new[] { new AssegnazionePaDto { CodFiscale = student.InformazioniPersonali.CodFiscale } };
            bool handled = (bool)type.GetMethod("PopulateAccontoPA", flags)!.Invoke(procedure, new object[] { 600d, 300d, assegnazioni })!;
            Check(students.Count == 1 && ReferenceEquals(students.Values.Single(), student),
                "Il caricamento dell'acconto PA non deve escludere lo studente per lo stato delle assegnazioni.");
            return handled;
        }
        try
        {
            var withoutAssignment = Student();
            Check(Populate(withoutAssignment, assegnazioni: Array.Empty<AssegnazionePaDto>())
                && withoutAssignment.InformazioniPagamento.Detrazioni.Count == 0,
                "PR senza assegnazione deve mantenere pagabile il vincitore senza aggiungere l'acconto PA.");
            var importedWithoutAssignment = Student();
            importedWithoutAssignment.AddDetrazione("01", 123, "Acconto importato dai movimenti contabili", true);
            var imported = importedWithoutAssignment.InformazioniPagamento.Detrazioni.Single();
            Check(Populate(importedWithoutAssignment, assegnazioni: Array.Empty<AssegnazionePaDto>())
                && ReferenceEquals(importedWithoutAssignment.InformazioniPagamento.Detrazioni.Single(), imported)
                && imported.importo == 123 && imported.needUpdate,
                "PR senza assegnazione deve conservare la detrazione 01 già importata, con identità, importo e needUpdate invariati.");
            var winner = Student();
            Check(Populate(winner) && winner.InformazioniPagamento.Detrazioni.Single().importo == 600
                && winner.InformazioniPagamento.Detrazioni.Single().codReversale == "01"
                && !winner.InformazioniPagamento.Detrazioni.Single().needUpdate,
                "PR deve applicare l'acconto fisso al vincitore con assegnazione, senza richiedere camera o tariffa.");
            Check(Populate(winner) && winner.InformazioniPagamento.Detrazioni.Count == 1,
                "Ripetere il caricamento PR non deve duplicare la detrazione 01.");
            var incomplete = Student();
            var assignment = new AssegnazionePaDto { CodFiscale = incomplete.InformazioniPersonali.CodFiscale };
            Check(Populate(incomplete, assegnazioni: new[] { assignment })
                && incomplete.InformazioniPagamento.Detrazioni.Single().importo == 600
                && incomplete.InformazioniPagamento.Assegnazioni.Count == 0,
                "Un'assegnazione senza dettagli di camera, date o tariffa deve consentire l'acconto fisso PR senza calcolare il saldo.");
            foreach (var unrelated in new[]
            {
                new AssegnazionePaDto { CodFiscale = "BNCLGU00A01H501B" },
                new AssegnazionePaDto { CodFiscale = incomplete.InformazioniPersonali.CodFiscale, CodFineAssegnazione = "03" }
            })
            {
                var excludedAssignment = Student();
                Check(Populate(excludedAssignment, assegnazioni: new[] { unrelated })
                    && excludedAssignment.InformazioniPagamento.Detrazioni.Count == 0,
                    "Un'assegnazione di un altro CF o con fine 03 non deve generare l'acconto PR.");
            }
            var duplicates = Student();
            Check(Populate(duplicates, assegnazioni: new[] { assignment, assignment })
                && duplicates.InformazioniPagamento.Detrazioni.Single().importo == 600,
                "Assegnazioni duplicate dello stesso CF devono produrre una sola detrazione 01.");

            foreach (int esito in new[] { 0, 1 })
            {
                var nonWinner = Student(esito);
                Check(Populate(nonWinner) && nonWinner.InformazioniPagamento.Detrazioni.Count == 0,
                    "L'acconto fisso PR spetta solo a EsitoPA 2.");
            }
            foreach (var (year, disabled, amount) in new[] { (-1, false, 300d), (-2, true, 300d), (-1, true, 600d), (-2, false, 600d) })
            {
                var fuoriCorso = Student();
                fuoriCorso.InformazioniIscrizione.AnnoCorso = year;
                fuoriCorso.InformazioniPersonali.Disabile = disabled;
                Check(Populate(fuoriCorso) && fuoriCorso.InformazioniPagamento.Detrazioni.Single().importo == amount,
                    "La quota fuori corso PR deve mantenere le soglie -1 non disabile e -2 disabile.");
            }
            var existing = Student();
            existing.AddDetrazione("01", 123, "Acconto già presente", true);
            var original = existing.InformazioniPagamento.Detrazioni.Single();
            Check(Populate(existing) && ReferenceEquals(existing.InformazioniPagamento.Detrazioni.Single(), original)
                && original.importo == 123 && original.needUpdate,
                "Una detrazione 01 esistente deve conservare importo, needUpdate e identità.");
            var reversal = Student();
            reversal.AddReversale("01", 123, "Acconto già reversato", "BSP0", "BSS0");
            Check(Populate(reversal) && reversal.InformazioniPagamento.Detrazioni.Count == 0,
                "Una reversale 01 senza detrazioni deve impedire un nuovo acconto PR.");
            var reissue = Student();
            Check(Populate(reissue, reissue: true) && reissue.InformazioniPagamento.Detrazioni.Count == 0,
                "La riemissione senza detrazioni o reversali deve conservare la guardia esistente.");

            // Le guardie originali sono alternative: detrazioni, poi reversali, poi riemissione.
            var deductionsFirst = Student();
            deductionsFirst.AddDetrazione("03", 10, "Altra detrazione");
            deductionsFirst.AddReversale("01", 123, "Acconto reversato", "BSP0", "BSS0");
            Check(Populate(deductionsFirst, reissue: true)
                && deductionsFirst.InformazioniPagamento.Detrazioni.Single(d => d.codReversale == "01").importo == 600,
                "Detrazioni non vuote devono mantenere la precedenza sulle guardie reversali e riemissione.");
            var reversalsFirst = Student();
            reversalsFirst.AddReversale("03", 10, "Altra reversale", "BSP0", "BSS0");
            Check(Populate(reversalsFirst, reissue: true) && reversalsFirst.InformazioniPagamento.Detrazioni.Single().importo == 600,
                "Reversali non vuote senza 01 devono mantenere la precedenza sulla guardia riemissione.");
            var integration = Student();
            Check(Populate(integration, integration: true) && integration.InformazioniPagamento.Detrazioni.Count == 0,
                "L'integrazione PR deve essere gestita senza aggiungere l'acconto fisso.");
            var balance = Student();
            balance.AddDetrazione("03", 10, "Detrazione saldo");
            Check(!Populate(balance, "SA") && balance.InformazioniPagamento.Detrazioni.Single().codReversale == "03"
                && balance.InformazioniPagamento.Detrazioni.Single().importo == 10,
                "Il helper acconto deve lasciare SA al percorso del saldo senza modificare detrazioni.");
        }
        finally
        {
            foreach (var (field, value) in fields) field.SetValue(procedure, value);
        }
        return checks;
    }
}
