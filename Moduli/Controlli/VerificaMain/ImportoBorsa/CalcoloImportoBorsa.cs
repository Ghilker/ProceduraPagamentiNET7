using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed class CalcoloImportoBorsa
    {
        private readonly Dictionary<StudentKey, StudenteInfo> _students = new();

        private CalcParams _params = new();

        private bool _collectionCompleted;
        private bool _calculationCompleted;

        public void Collect(CalcParams calcParams, IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            if (calcParams == null)
                throw new ArgumentNullException(nameof(calcParams));

            _students.Clear();
            foreach (var pair in students)
                _students[pair.Key] = pair.Value;

            _params = calcParams;

            ResetStudentState();

            _collectionCompleted = true;
            _calculationCompleted = false;
        }

        public void Calculate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("Collect deve essere eseguito prima di Calculate.");

            foreach (var info in _students.Values)
            {
                var imp = info.InformazioniImportoBorsa;
                string status = GetStatusSedeRiferimento(info.InformazioniSede);

                decimal importoBase = GetImportoBaseByStatus(status);
                decimal importoFinale = importoBase;

                decimal isee = GetIseeRiferimento(info);

                if (importoFinale > 0m)
                {
                    importoFinale = ApplyIseeRule(importoFinale, isee, _params.SogliaIsee);

                    if (IsDonnaStem(info))
                        importoFinale += RoundMoney(importoBase * 0.20m);

                    bool riduzioneMeta = DeveRidurreAMetaPerFuoriCorso(info);
                    if (riduzioneMeta)
                        importoFinale = RoundMoney(importoFinale / 2m);

                    if (HaMonetizzazioneMensa(info))
                        importoFinale += riduzioneMeta ? 300m : 600m;
                }

                imp.StatusSedeRiferimento = status;
                imp.ImportoBase = RoundMoney(importoBase);
                imp.ImportoFinale = RoundMoney(importoFinale);
                imp.CalcoloEseguito = true;
            }

            _calculationCompleted = true;
        }

        public void Validate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("Collect deve essere eseguito prima di Validate.");
            if (!_calculationCompleted)
                throw new InvalidOperationException("Calculate deve essere eseguito prima di Validate.");

            foreach (var info in _students.Values)
            {
                var imp = info.InformazioniImportoBorsa;

                if (imp.ImportoBase < 0m)
                    imp.ImportoBase = 0m;

                if (imp.ImportoFinale < 0m)
                    imp.ImportoFinale = 0m;
            }
        }

        private void ResetStudentState()
        {
            foreach (var info in _students.Values)
            {
                var imp = info.InformazioniImportoBorsa;
                imp.StatusSedeRiferimento = string.Empty;
                imp.ImportoBase = 0m;
                imp.ImportoFinale = 0m;
                imp.CalcoloEseguito = false;
            }
        }

        private decimal GetImportoBaseByStatus(string status)
        {
            switch ((status ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "A":
                    return _params.ImportoBorsaA;

                case "B":
                    return _params.ImportoBorsaB;

                case "C":
                case "D":
                    return _params.ImportoBorsaC;

                default:
                    return 0m;
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

        private static decimal GetIseeRiferimento(StudenteInfo info)
        {
            var eco = info.InformazioniEconomiche;

            if (TryReadDecimal(eco?.ISEEDSU, out var ricalcolato) && ricalcolato > 0m)
                return ricalcolato;

            if (TryReadDecimal(eco?.ISEEDSU_Attuale, out var attuale) && attuale > 0m)
                return attuale;

            return 0m;
        }

        private static bool IsDonnaStem(StudenteInfo info)
        {
            bool donna = string.Equals(
                (info.InformazioniPersonali?.Sesso ?? string.Empty).Trim(),
                "F",
                StringComparison.OrdinalIgnoreCase);

            bool stem = info.InformazioniIscrizione?.CorsoStem == true;

            return donna && stem;
        }

        private static bool DeveRidurreAMetaPerFuoriCorso(StudenteInfo info)
        {
            int annoCorso = info.InformazioniIscrizione?.AnnoCorso ?? 0;
            bool invalido = info.InformazioniPersonali?.Disabile == true;

            return (annoCorso == -1 && !invalido)
                || (annoCorso == -2 && invalido);
        }

        private static bool HaMonetizzazioneMensa(StudenteInfo info)
        {
            return info.InformazioniBeneficio?.ConcessaMonetizzazioneMensa == true;
        }

        private static string GetStatusSedeRiferimento(InformazioniSede sede)
        {
            if (!string.IsNullOrWhiteSpace(sede.StatusSedeSuggerito))
                return sede.StatusSedeSuggerito;

            return sede.StatusSede ?? string.Empty;
        }

        private static bool TryReadDecimal(object? value, out decimal result)
        {
            result = 0m;

            if (value == null || value == DBNull.Value)
                return false;

            try
            {
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}