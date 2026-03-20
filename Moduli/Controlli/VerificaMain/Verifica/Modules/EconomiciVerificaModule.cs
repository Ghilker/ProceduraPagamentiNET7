using ProcedureNet7.Modules.Contracts;
using System;

namespace ProcedureNet7.Verifica.Modules
{
    internal sealed class EconomiciVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private readonly VerificaControlliDatiEconomici _service;

        public EconomiciVerificaModule(VerificaControlliDatiEconomici service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Name => "Economici";

        public void Collect(VerificaPipelineContext context)
        {
            _service.Collect(
                context.AnnoAccademico,
                context.CandidateCfs,
                context.Students);
        }

        public void Calculate(VerificaPipelineContext context)
        {
            _service.Calculate();
        }

        public void Validate(VerificaPipelineContext context)
        {
            _service.Validate();
        }
    }
}
