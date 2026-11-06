using ProcedureNet7;
using ProcedureNet7.Verifica;

internal static class AlloggioStatusRegressionTests
{
    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        foreach (string benefit in new[] { "PA", "PK" })
        {
            CheckWinner($"vincitore {benefit} senza domicilio", info => SetOutcome(info, benefit, 2));
            CheckWinner($"vincitore {benefit} prevale sulla forzatura e sul corso telematico", info =>
            {
                SetOutcome(info, benefit, 2);
                info.InformazioniSede.ForzaturaStatusSede = "A";
                info.InformazioniSede.AlwaysA = true;
            });

            foreach (int? outcome in new int?[] { null, 0, 1, 3 })
            {
                Check($"esito {benefit} {outcome} non equivale a vincitore", info =>
                {
                    SetOutcome(info, benefit, outcome);
                    info.InformazioniSede.ForzaturaStatusSede = "A";
                    Assert(Status(info).SuggestedStatus == "A", "forzatura ignorata senza vincitore");
                    Assert(Status(info, ignoreForcing: true).SuggestedStatus == "D", "esito non vincitore promosso a fuori sede");
                });
            }
        }

        CheckWinner("segnale storico VincitorePA conservato", info => info.InformazioniBeneficio.VincitorePA = true);
        CheckWinner("segnale storico EsitoPA conservato", info => info.InformazioniBeneficio.EsitoPA = 2);
        CheckWinner("vincitore PK anche con PA escluso", info =>
        {
            SetOutcome(info, "PA", 0);
            SetOutcome(info, "PK", 2);
        });

        Check("idoneo alloggio segue il flag ordinario senza cambiare precedenza forzature", info =>
        {
            SetOutcome(info, "PK", 1);
            info.InformazioniSede.HasAlloggio12 = true;
            Assert(Status(info).SuggestedStatus == "B", "flag alloggio idoneo ignorato");
            info.InformazioniSede.ForzaturaStatusSede = "A";
            Assert(Status(info).SuggestedStatus == "A", "idoneo promosso alla precedenza del vincitore");
            Assert(Status(info, ignoreForcing: true).SuggestedStatus == "B", "confronto senza forzatura perde il flag alloggio");
        });

        Check("vincitore BS non equivale a vincitore alloggio", info =>
        {
            SetOutcome(info, "BS", 2);
            Assert(Status(info).SuggestedStatus == "D", "vincitore BS trattato come alloggio");
        });

        if (failures.Count > 0)
            throw new InvalidOperationException($"Falliti {failures.Count}/{checks} scenari status alloggio:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari status alloggio PA/PK: fuori sede, saldo e forzature. Nessuna connessione DB aperta.");

        void CheckWinner(string name, Action<StudenteInfo> configure)
        {
            Check(name, info =>
            {
                configure(info);
                string forcing = info.InformazioniSede.ForzaturaStatusSede;
                foreach (bool ignoreForcing in new[] { false, true })
                {
                    var status = Status(info, ignoreForcing);
                    Assert(status.SuggestedStatus == "B", "vincitore senza status B");
                    Assert(status.FuoriSedeCertoPerSaldo, "vincitore senza certezza per saldo");
                    Assert(!status.DomicilioPresente && !status.DomicilioValido, "domicilio inventato per il vincitore");
                }
                Assert(info.InformazioniSede.ForzaturaStatusSede == forcing, "forzatura originale modificata");
                Assert(!ControlloStatusSede.DevePagareComePendolarePerPagamento(
                    info, "20262027", "SA", null, new DateTime(2027, 9, 30), out var paidStatus),
                    "saldo vincitore ridotto a pendolare senza domicilio");
                Assert(paidStatus.SuggestedStatus == "B" && paidStatus.FuoriSedeCertoPerSaldo, "saldo perde lo status certo B");

                info.InformazioniSede.StatusSedeSuggerito = "D";
                Assert(!ControlloStatusSede.DevePagareComePendolareDaStatusCalcolato(info, out var cachedStatus),
                    "vincitore ridotto a pendolare da stato memorizzato");
                Assert(cachedStatus.SuggestedStatus == "B" && cachedStatus.FuoriSedeCertoPerSaldo,
                    "stato memorizzato non riconosce il vincitore");
            });
        }

        void Check(string name, Action<StudenteInfo> verify)
        {
            checks++;
            try
            {
                var info = new StudenteInfo();
                info.SetResidenza("", "F205", "MI", "", "F205");
                info.InformazioniIscrizione.ComuneSedeStudi = "H501";
                info.InformazioniIscrizione.ProvinciaSedeStudi = "RM";
                info.InformazioniSede.StatusSede = "B";
                info.InformazioniSede.UsaNuovaGestioneDomicili = true;
                verify(info);
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }
    }

    private static ControlloStatusSede.StatusSedeResult Status(StudenteInfo info, bool ignoreForcing = false)
        => ControlloStatusSede.ValutaStatusSede(info, "20262027", referenceDate: new DateTime(2027, 9, 30),
            faseElaborativa: VerificaFaseElaborativa.GraduatorieDefinitive, ignoraForzature: ignoreForcing);

    private static void SetOutcome(StudenteInfo info, string benefit, int? outcome)
        => info.EsitiConcorsoByBenefit[benefit] = new EsitoConcorsoBenefitRaw { CodTipoEsito = outcome };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
