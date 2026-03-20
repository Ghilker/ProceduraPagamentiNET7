using ProcedureNet7.Modules.Contracts;
using System;

namespace ProcedureNet7.Verifica.Modules
{
    internal sealed class StatusSedeVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private readonly ControlloStatusSede _service;

        public StatusSedeVerificaModule(ControlloStatusSede service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Name => "StatusSede";

        public void Collect(VerificaPipelineContext context)
        {
            _service.CollectFromTempCandidates(
                context.AnnoAccademico,
                context.TempCandidatesTable,
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
