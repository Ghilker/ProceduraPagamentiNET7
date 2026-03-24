using ProcedureNet7.Modules.Contracts;
using System;

namespace ProcedureNet7.Verifica.Modules
{
    internal sealed class ImportoBorsaVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private readonly CalcoloImportoBorsa _service;

        public ImportoBorsaVerificaModule(CalcoloImportoBorsa service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Name => "ImportoBorsa";

        public void Collect(VerificaPipelineContext context)
        {
            _service.Collect(context.CalcParams, context.Students);
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