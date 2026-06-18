using System;
using System.Globalization;
using ProcedureNet7.Verifica;

namespace ProcedureNet7
{
    internal sealed class CalcoloImportoBorsa : IVerificaModule
    {
        public string Name => "ImportoBorsa";

        public void Calculate(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var calc = context.CalcParams ?? new CalcParams();

            foreach (var info in context.Students.Values)
            {
                var imp = info.InformazioniImportoBorsa;
                string status = GetStatusSedeRiferimento(info.InformazioniSede);

                decimal? importoBase = GetImportoBaseByStatus(status, calc);
                decimal? isee = GetIseeRiferimento(info);
                decimal? importoFinale = null;

                if (importoBase.HasValue && importoBase.Value > 0m && isee.HasValue)
                {
                    decimal valoreFinale = ApplyIseeRule(importoBase.Value, isee.Value, calc.SogliaIsee);

                    if (IsDonnaStem(info))
                        valoreFinale += RoundMoney(importoBase.Value * 0.20m);

                    bool riduzioneMeta = DeveRidurreAMetaPerFuoriCorso(info);
                    if (riduzioneMeta)
                        valoreFinale = RoundMoney(valoreFinale / 2m);

                    if (HaMonetizzazioneMensa(info))
                        valoreFinale += riduzioneMeta ? 300m : 600m;

                    importoFinale = RoundMoney(valoreFinale);
                }

                imp.StatusSedeRiferimento = status;
                imp.ImportoBase = importoBase.HasValue ? RoundMoney(importoBase.Value) : null;
                imp.ImportoFinale = importoFinale;
                imp.CalcoloEseguito = importoFinale.HasValue;
            }
        }

        private static decimal? GetImportoBaseByStatus(string status, CalcParams calc)
        {
            switch ((status ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "A": return calc.ImportoBorsaA;
                case "B": return calc.ImportoBorsaB;
                case "C":
                case "D": return calc.ImportoBorsaC;
                default: return null;
            }
        }

        private static decimal ApplyIseeRule(decimal importoBase, decimal isee, decimal sogliaIsee)
        {
            if (importoBase <= 0m || sogliaIsee <= 0m)
                return RoundMoney(importoBase);

            decimal metaSoglia = sogliaIsee / 2m;
            decimal dueTerziSoglia = sogliaIsee * 2m / 3m;

            if (isee >= 0m && isee < metaSoglia)
                return RoundMoney(importoBase * 1.15m);

            if (isee >= sogliaIsee)
                return RoundMoney(importoBase * 0.50m);

            if (isee > dueTerziSoglia)
            {
                decimal ampiezza = sogliaIsee - dueTerziSoglia;
                if (ampiezza <= 0m)
                    return RoundMoney(importoBase * 0.50m);

                decimal progresso = (isee - dueTerziSoglia) / ampiezza;
                decimal coeff = 1m - (0.5m * progresso);
                return RoundMoney(importoBase * coeff);
            }

            return RoundMoney(importoBase);
        }

        private static decimal? GetIseeRiferimento(StudenteInfo info)
        {
            var eco = info.InformazioniEconomiche;

            if (eco?.Calcolate?.ISEEDSU is decimal ordinario)
                return ordinario;

            if (eco?.Attuali?.ISEEDSU is double attuale)
                return Convert.ToDecimal(attuale, CultureInfo.InvariantCulture);

            return null;
        }

        private static bool IsDonnaStem(StudenteInfo info)
        {
            bool donna = string.Equals((info.InformazioniPersonali?.Sesso ?? string.Empty).Trim(), "F", StringComparison.OrdinalIgnoreCase);
            bool stem = info.InformazioniIscrizione?.CorsoStem == true;
            return donna && stem;
        }

        private static bool DeveRidurreAMetaPerFuoriCorso(StudenteInfo info)
        {
            int annoCorso = info.InformazioniIscrizione?.AnnoCorso ?? 0;
            bool invalido = info.InformazioniPersonali?.Disabile == true;
            return (annoCorso == -1 && !invalido) || (annoCorso == -2 && invalido);
        }

        private static bool HaMonetizzazioneMensa(StudenteInfo info) => info.InformazioniBeneficio?.ConcessaMonetizzazioneMensa == true;

        private static string GetStatusSedeRiferimento(InformazioniSede sede)
        {
            if (!string.IsNullOrWhiteSpace(sede.StatusSedeSuggerito))
                return sede.StatusSedeSuggerito;
            return sede.StatusSede ?? string.Empty;
        }

        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
