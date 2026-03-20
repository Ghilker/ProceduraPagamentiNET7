namespace ProcedureNet7.Verifica
{
    // Kept only as a compatibility wrapper for callers that still expect a named result type.
    internal sealed class ValutazioneVerifica
    {
        public StudenteInfo Info { get; set; } = new StudenteInfo();

        public StudentKey Key => new StudentKey(
            Info.InformazioniPersonali.CodFiscale ?? string.Empty,
            Info.InformazioniPersonali.NumDomanda ?? string.Empty);
    }
}
