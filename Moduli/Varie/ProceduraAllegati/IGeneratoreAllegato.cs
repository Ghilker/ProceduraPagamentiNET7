namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal interface IGeneratoreAllegato
    {
        string Codice { get; }

        string Descrizione { get; }

        void Generate(AllegatoContext context);
    }
}
