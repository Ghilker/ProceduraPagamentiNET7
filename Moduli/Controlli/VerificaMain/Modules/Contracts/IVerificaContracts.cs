namespace ProcedureNet7.Modules.Contracts
{
    internal interface IVerificaCalculator<in TContext>
    {
        void Calculate(TContext context);
    }

    internal interface IVerificaValidator<in TContext>
    {
        void Validate(TContext context);
    }

    internal interface IVerificaDataCollector<in TContext>
    {
        void Collect(TContext context);
    }

    internal interface IVerificaModule<in TContext> : IVerificaCalculator<TContext>, IVerificaValidator<TContext>
    {
        string Name { get; }
    }
}
