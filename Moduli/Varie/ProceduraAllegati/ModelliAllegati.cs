using System;
using System.Collections.Generic;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    /// <summary>
    /// Definisce le sole colonne che l'operatore deve compilare per un tipo di allegato.
    /// I dati già determinabili dal tipo di allegato, dall'anno accademico o dal beneficio
    /// non sono richiesti nel modello Excel.
    /// </summary>
    internal sealed record ModelloAllegatoDefinition(
        string Codice,
        string Descrizione,
        string NomeFoglio,
        string NomeFile,
        IReadOnlyList<string> Colonne);

    internal static class CatalogoModelliAllegati
    {
        public const string CodiceFiscale = "Codice fiscale";
        public const string MotivoDecadenza = "Motivo decadenza";
        public const string Motivo = "Motivo";
        public const string ImportoBorsa = "Importo borsa";
        public const string NumeroImpegnoPrimaRata = "Numero Impegno prima rata";
        public const string NumeroImpegnoSaldo = "Numero Impegno saldo";
        public const string NumeroImpegnoPrimaRataOpzionale = "Numero Impegno prima rata (opzionale)";
        public const string NumeroImpegnoSaldoOpzionale = "Numero Impegno saldo (opzionale)";
        public const string MotivazioneRiammissione = "Motivazione riammissione";
        public const string MotivazioneModificaImporto = "Motivazione modifica importo";
        public const string MotivazioneModifica = "Motivazione modifica";
        public const string MotivazioneModificaStatusSede = "Motivazione modifica status sede";

        private static readonly IReadOnlyDictionary<string, ModelloAllegatoDefinition> Modelli =
            new Dictionary<string, ModelloAllegatoDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["01"] = Crea("01", "Riammissione come vincitore", "RiammissioneVincitore", "RiammissioneVincitore",
                    CodiceFiscale, NumeroImpegnoPrimaRata, NumeroImpegnoSaldo, MotivazioneRiammissione),
                ["02"] = Crea("02", "Riammissione come idoneo", "RiammissioneIdoneo", "RiammissioneIdoneo",
                    CodiceFiscale, Motivo),
                ["03"] = Crea("03", "Revoca senza recupero somme", "RevocaSenzaRecupero", "RevocaSenzaRecupero", CodiceFiscale, Motivo),
                ["40"] = Crea("40", "Decadenza senza recupero somme", "DecadenzaSenzaRecupero", "DecadenzaSenzaRecupero", CodiceFiscale, MotivoDecadenza),
                ["41"] = Crea("41", "Decadenza con recupero somme", "DecadenzaConRecupero", "DecadenzaConRecupero", CodiceFiscale, MotivoDecadenza),
                ["05"] = Crea("05", "Modifica importo", "ModificaImporto", "ModificaImporto",
                    CodiceFiscale, NumeroImpegnoPrimaRataOpzionale, NumeroImpegnoSaldoOpzionale, MotivazioneModificaImporto),
                ["06"] = Crea("06", "Revoca con recupero somme", "RevocaConRecupero", "RevocaConRecupero", CodiceFiscale, Motivo),
                ["09"] = Crea("09", "Da idoneo a vincitore", "IdoneoAVincitore", "IdoneoAVincitore",
                    CodiceFiscale, NumeroImpegnoPrimaRata, NumeroImpegnoSaldo, MotivazioneModifica),
                ["10"] = Crea("10", "Rinuncia con recupero somme", "RinunciaConRecupero", "RinunciaConRecupero", CodiceFiscale),
                ["11"] = Crea("11", "Rinuncia senza recupero somme", "RinunciaSenzaRecupero", "RinunciaSenzaRecupero", CodiceFiscale),
                ["13"] = Crea("13", "Cambio status sede", "CambioStatusSede", "CambioStatusSede",
                    CodiceFiscale, NumeroImpegnoPrimaRataOpzionale, NumeroImpegnoSaldoOpzionale, MotivazioneModificaStatusSede)
            };

        public static bool TryGet(string? codice, out ModelloAllegatoDefinition? modello)
        {
            return Modelli.TryGetValue(codice?.Trim() ?? string.Empty, out modello);
        }

        private static ModelloAllegatoDefinition Crea(
            string codice,
            string descrizione,
            string nomeFoglio,
            string nomeFile,
            params string[] colonne)
        {
            return new ModelloAllegatoDefinition(codice, descrizione, nomeFoglio, nomeFile, colonne);
        }
    }
}
