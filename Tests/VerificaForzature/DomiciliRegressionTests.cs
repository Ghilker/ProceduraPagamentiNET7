using ProcedureNet7;

internal static class DomiciliRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();

        Check("contratto annuale ordinario", new[] { Contract("2026-10-01", "2027-09-30") },
            "2026-10-01", "2027-09-30", 12, true);

        var cessation = Contract("2026-10-01", "2027-01-31", "2027-06-30");
        Extend(cessation, "2027-02-01", "2027-09-30");
        Check("cessazione durante proroga", new[] { cessation },
            "2026-10-01", "2027-06-30", 9, false);

        var takeover = Contract("2025-10-01", "2026-09-30", takeover: "2026-10-01");
        Extend(takeover, "2026-10-01", "2027-09-30");
        Check("subentro durante proroga dopo la scadenza base", new[] { takeover },
            "2026-10-01", "2027-09-30", 12, true);

        var partialMonth = Contract("2025-10-01", "2026-09-30", takeover: "2027-02-16");
        Extend(partialMonth, "2026-10-01", "2027-09-30");
        Check("nessun giorno della proroga prima del subentro", new[] { partialMonth },
            "2027-02-16", "2027-09-30", 7, false);

        var partialCessation = Contract("2025-10-01", "2026-09-30", "2026-11-14");
        Extend(partialCessation, "2026-10-01", "2027-09-30");
        Check("mese con soli quattordici giorni prima della cessazione", new[] { partialCessation },
            "2026-10-01", "2026-11-14", 1, false);

        var boundary = Contract("2026-10-01", "2026-12-31", "2027-01-01");
        Extend(boundary, "2027-01-01", "2027-09-30");
        Check("cessazione inclusiva al primo giorno della proroga", new[] { boundary },
            "2026-10-01", "2027-01-01", 3, false);

        var lastDay = Contract("2025-10-01", "2026-09-30", takeover: "2027-09-30");
        Extend(lastDay, "2026-10-01", "2027-09-30");
        Check("subentro inclusivo all'ultimo giorno della proroga", new[] { lastDay },
            "2027-09-30", "2027-09-30", 0, false);

        var both = Contract("2026-10-01", "2026-12-31", "2027-05-31", "2026-12-01");
        Extend(both, "2027-01-01", "2027-03-31");
        Extend(both, "2027-04-01", "2027-09-30");
        Check("subentro e cessazione limitano l'intera catena", new[] { both },
            "2026-12-01", "2027-05-31", 6, false);

        var cessationInGap = Contract("2026-10-01", "2026-12-31", "2027-01-10");
        Extend(cessationInGap, "2027-01-16", "2027-09-30");
        Check("cessazione nel buco non crea copertura inesistente", new[] { cessationInGap },
            "2026-10-01", "2026-12-31", 3, false);

        var acceptedGap = Contract("2026-10-01", "2027-01-31");
        Extend(acceptedGap, "2027-02-16", "2027-09-30");
        Check("proroga con interruzione di quindici giorni", new[] { acceptedGap },
            "2026-10-01", "2027-09-30", 11, true, uncoveredDays: 15, maximumGap: 15);

        var rejectedGap = Contract("2026-10-01", "2027-01-31");
        Extend(rejectedGap, "2027-02-17", "2027-09-30");
        Check("proroga con interruzione di sedici giorni esclusa", new[] { rejectedGap },
            "2026-10-01", "2027-01-31", 4, false, expectedAnomaly: true);

        Check("catena di contratti con quindici giorni di interruzione", new[]
            {
                Contract("2026-10-01", "2027-02-28"),
                Contract("2027-03-16", "2027-09-30")
            }, "2026-10-01", "2027-09-30", 12, true, uncoveredDays: 15, maximumGap: 15);

        Check("sedici giorni separano le catene di contratti", new[]
            {
                Contract("2026-10-01", "2027-02-28"),
                Contract("2027-03-17", "2027-09-30")
            }, "2027-03-17", "2027-09-30", 7, false);

        Check("copertura ritagliata sull'anno accademico", new[] { Contract("2025-10-01", "2027-12-31") },
            "2026-10-01", "2027-09-30", 12, true);

        var invalidExtension = Contract("2026-10-01", "2027-01-31");
        Extend(invalidExtension, "2027-02-01", "2027-09-30").NumeroSerieProroga = "";
        Check("proroga priva di estremi esclusa", new[] { invalidExtension },
            "2026-10-01", "2027-01-31", 4, false, expectedAnomaly: true);

        var invalidContract = Contract("2026-10-01", "2027-09-30");
        invalidContract.NumeroSerieContratto = "";
        Check("formalita del contratto conservate", new[] { invalidContract },
            null, null, 0, false, expectedAnomaly: true);

        Check("cessazione precedente al subentro esclusa", new[]
            { Contract("2026-10-01", "2027-09-30", "2027-01-31", "2027-02-01") },
            null, null, 0, false, expectedAnomaly: true);

        if (failures.Count > 0)
            throw new InvalidOperationException($"Falliti {failures.Count}/{checks} scenari domicili:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari domicili: subentro, cessazione, proroghe e copertura. Nessuna connessione DB aperta.");

        void Check(string name, IEnumerable<ContrattoDomicilioAnalisiInput> contracts,
            string? expectedStart, string? expectedEnd, int expectedMonths, bool expectedValid,
            int uncoveredDays = 0, int maximumGap = 0, bool expectedAnomaly = false)
        {
            checks++;
            try
            {
                var domicile = new DomicilioAnalisiInput
                {
                    Id = 1,
                    CodComune = "H501",
                    TipoDomicilio = TipoDomicilioAnalisi.Oneroso,
                    InserimentoDatiContratto = true,
                    Contratti = contracts.ToList()
                };
                var outcome = AnalizzatoreDomicili.Analizza(new[] { domicile }, "20262027", new()
                {
                    DataRiferimento = Date("2027-09-30"),
                    AbilitaFinestraProrogaTrentaGiorni = false,
                    ComuneAmmesso = comune => comune == "H501"
                });
                DateTime? start = expectedStart == null ? null : Date(expectedStart);
                DateTime? end = expectedEnd == null ? null : Date(expectedEnd);
                int days = start.HasValue && end.HasValue ? (end.Value - start.Value).Days + 1 - uncoveredDays : 0;
                if (outcome.Valido != expectedValid || outcome.ValidoCertoPerSaldo != expectedValid
                    || outcome.MesiCoperti != expectedMonths || outcome.GiorniCoperti != days
                    || outcome.DataInizioCoperturaAnnoAccademico != start
                    || outcome.DataFineCoperturaAnnoAccademico != end
                    || outcome.MassimoBucoGiorni != maximumGap
                    || (outcome.Anomalie.Count > 0) != expectedAnomaly)
                {
                    throw new InvalidOperationException(
                        $"attesi valido={expectedValid}, mesi={expectedMonths}, giorni={days}, periodo={expectedStart}/{expectedEnd}, " +
                        $"buco={maximumGap}, anomalie={expectedAnomaly}; ottenuti valido={outcome.Valido}, certo={outcome.ValidoCertoPerSaldo}, " +
                        $"mesi={outcome.MesiCoperti}, giorni={outcome.GiorniCoperti}, " +
                        $"periodo={outcome.DataInizioCoperturaAnnoAccademico:yyyy-MM-dd}/{outcome.DataFineCoperturaAnnoAccademico:yyyy-MM-dd}, " +
                        $"buco={outcome.MassimoBucoGiorni}, anomalie={string.Join("; ", outcome.Anomalie)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }
    }

    private static ContrattoDomicilioAnalisiInput Contract(string start, string end,
        string? cessation = null, string? takeover = null) => new()
    {
        Id = 10,
        TipoContratto = TipoContrattoDomicilioAnalisi.Registrato,
        NumeroSerieContratto = "BASE-AUDIT",
        DataRegistrazioneContratto = Date(start),
        DataInizio = Date(start),
        DataFine = Date(end),
        DataCessazione = cessation == null ? null : Date(cessation),
        DataInizioSubentro = takeover == null ? null : Date(takeover),
        NumeroSerieSubentro = takeover == null ? "" : "SUB-AUDIT"
    };

    private static ProrogaDomicilioAnalisiInput Extend(ContrattoDomicilioAnalisiInput contract, string start, string end)
    {
        var extension = new ProrogaDomicilioAnalisiInput
        {
            Id = 20 + contract.Proroghe.Count,
            NumeroSerieProroga = $"EXT-{contract.Proroghe.Count}",
            DataDecorrenza = Date(start),
            DataScadenza = Date(end)
        };
        contract.Proroghe.Add(extension);
        return extension;
    }

    private static DateTime Date(string value)
        => DateTime.ParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
