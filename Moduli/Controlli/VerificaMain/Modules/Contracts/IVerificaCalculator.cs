namespace ProcedureNet7.Modules.Contracts
{
    internal interface IVerificaCalculator<in TContext>
    {
        void Calculate(TContext context);
    }
}
