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

        public void Calculate(VerificaPipelineContext context)
        {
            _service.Calculate();
        }

        public void Validate(VerificaPipelineContext context)
        {
            _service.Validate();
        }
    }

    internal sealed class StatusSedeVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private readonly ControlloStatusSede _service;

        public StatusSedeVerificaModule(ControlloStatusSede service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Name => "StatusSede";

        public void Calculate(VerificaPipelineContext context)
        {
            _service.SetReferenceDate(context.ReferenceDate);
            _service.Calculate();
        }

        public void Validate(VerificaPipelineContext context)
        {
            _service.Validate();
        }
    }

    internal sealed class ImportoBorsaVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private readonly CalcoloImportoBorsa _service;

        public ImportoBorsaVerificaModule(CalcoloImportoBorsa service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public string Name => "ImportoBorsa";

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
