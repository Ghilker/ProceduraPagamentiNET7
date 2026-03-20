namespace ProcedureNet7.Modules.Contracts
{
    internal interface IVerificaModule<in TContext>
    {
        string Name { get; }
        void Collect(TContext context);
        void Calculate(TContext context);
        void Validate(TContext context);
    }
}
