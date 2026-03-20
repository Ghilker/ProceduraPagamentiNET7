namespace ProcedureNet7.Modules.Contracts
{
    internal interface IVerificaDataCollector<in TContext>
    {
        void Collect(TContext context);
    }
}
