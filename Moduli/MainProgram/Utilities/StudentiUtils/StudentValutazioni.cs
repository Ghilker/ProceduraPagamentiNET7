namespace ProcedureNet7
{
    /// <summary>
    /// Chiave normalizzata per l'identificazione univoca dello studente (CF + NumDomanda).
    /// </summary>
    public readonly record struct StudentKey
    {
        public string CodFiscale { get; }
        public string NumDomanda { get; }

        public StudentKey(string codFiscale, string numDomanda)
        {
            CodFiscale = NormalizeCf(codFiscale);
            NumDomanda = NormalizeNd(numDomanda);
        }

        public static StudentKey From(StudenteInfo info)
            => new StudentKey(info?.InformazioniPersonali?.CodFiscale ?? "", info?.InformazioniPersonali?.NumDomanda ?? "");

        public static StudentKey From(string codFiscale, string numDomanda)
            => new StudentKey(codFiscale, numDomanda);

        private static string NormalizeCf(string? cf)
            => Utilities.RemoveAllSpaces((cf ?? string.Empty)).Trim().ToUpperInvariant();

        private static string NormalizeNd(string? nd)
            => Utilities.RemoveAllSpaces((nd ?? string.Empty)).Trim();

        public override string ToString() => $"{CodFiscale}|{NumDomanda}";
    }

    public sealed class ValutazioneEconomici
    {
        public StudenteInfo Info { get; init; } = new StudenteInfo();

        public string TipoRedditoOrigine { get; init; } = string.Empty;
        public string TipoRedditoIntegrazione { get; init; } = string.Empty;
        public int? CodTipoEsitoBS { get; init; }

        public decimal? ISR { get; init; }
        public decimal? ISP { get; init; }
        public decimal? Detrazioni { get; init; }

        public decimal? ISEDSU { get; init; }
        public decimal? ISEEDSU { get; init; }
        public decimal? ISPEDSU { get; init; }

        public decimal? ISPDSU { get; init; }
        public decimal? SEQ { get; init; }

        public decimal? ISEDSU_Attuale { get; init; }
        public decimal? ISEEDSU_Attuale { get; init; }
        public decimal? ISPEDSU_Attuale { get; init; }
        public decimal? ISPDSU_Attuale { get; init; }
        public decimal? SEQ_Attuale { get; init; }

        public StudentKey Key => StudentKey.From(Info);
    }

    public sealed class ValutazioneStatusSede
    {
        public StudenteInfo Info { get; init; } = new StudenteInfo();

        public string StatoSuggerito { get; init; } = string.Empty;
        public string Motivo { get; init; } = string.Empty;

        public bool DomicilioPresente { get; init; }
        public bool DomicilioValido { get; init; }

        public bool HasAlloggio12 { get; init; }

        public StudentKey Key => StudentKey.From(Info);

        public bool HasIstanzaDomicilio { get; set; }
        public string CodTipoIstanzaDomicilio { get; set; } = "";
        public int NumIstanzaDomicilio { get; set; }

        public bool HasUltimaIstanzaChiusaDomicilio { get; set; }
        public string CodTipoUltimaIstanzaChiusaDomicilio { get; set; } = "";
        public int NumUltimaIstanzaChiusaDomicilio { get; set; }
        public string EsitoUltimaIstanzaChiusaDomicilio { get; set; } = "";
        public string UtentePresaCaricoUltimaIstanzaChiusaDomicilio { get; set; } = "";
    }

    public sealed class ValutazioneVerifica
    {
        public StudenteInfo Info { get; init; } = new StudenteInfo();

        public ValutazioneEconomici? Economici { get; init; }
        public ValutazioneStatusSede? StatusSede { get; init; }

        public StudentKey Key => StudentKey.From(Info);
    }
}
