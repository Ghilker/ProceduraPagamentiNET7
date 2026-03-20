namespace ProcedureNet7.Modules.Contracts
{
    internal interface IVerificaValidator<in TContext>
    {
        void Validate(TContext context);
    }
}
