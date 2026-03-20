using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede
    {
        private readonly SqlConnection _conn;
        private List<StatusSedeStudent> _collectedInputs = new();
        private HashSet<(string ComuneA, string ComuneB)> _comuniEquiparati = new();
        private bool _collectionCompleted;
        private bool _calculationCompleted;
        private string _selectedAA = "";

        public ControlloStatusSede(SqlConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public DataTable OutputStatusSede { get; private set; } = BuildOutputTable();
        public IReadOnlyList<ValutazioneStatusSede> OutputStatusSedeList { get; private set; } = Array.Empty<ValutazioneStatusSede>();

        public DataTable Compute(string aa) => Compute(aa, includeEsclusi: false, includeNonTrasmesse: false);

        public DataTable Compute(string aa, bool includeEsclusi, bool includeNonTrasmesse)
        {
            Collect(aa, includeEsclusi, includeNonTrasmesse);
            Calculate();
            Validate();
            return OutputStatusSede;
        }

        public List<ValutazioneStatusSede> ComputeList(
            string aa,
            bool includeEsclusi,
            bool includeNonTrasmesse)
        {
            Collect(aa, includeEsclusi, includeNonTrasmesse);
            Calculate();
            Validate();
            return OutputStatusSedeList.ToList();
        }

        public List<ValutazioneStatusSede> ComputeListFromTempCandidates(
            string aa,
            string tempCandidatesTable,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey)
        {
            CollectFromTempCandidates(aa, tempCandidatesTable, infoByKey);
            Calculate();
            Validate();
            return OutputStatusSedeList.ToList();
        }

        public void Collect(string aa, bool includeEsclusi, bool includeNonTrasmesse)
        {
            ValidateSelectedAA(aa);
            _selectedAA = aa;

            var repo = new SqlStatusSedeRepository(_conn);
            _collectedInputs = repo.LoadInputs(aa, includeEsclusi, includeNonTrasmesse, infoByKey: null);
            _comuniEquiparati = repo.LoadComuniEquiparati();
            OutputStatusSedeList = Array.Empty<ValutazioneStatusSede>();
            OutputStatusSede = BuildOutputTable();
            _collectionCompleted = true;
            _calculationCompleted = false;
        }

        public void CollectFromTempCandidates(
            string aa,
            string tempCandidatesTable,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey)
        {
            ValidateSelectedAA(aa);
            _selectedAA = aa;

            var repo = new SqlStatusSedeRepository(_conn);
            _collectedInputs = repo.LoadInputsFromTempCandidates(aa, tempCandidatesTable, infoByKey);
            _comuniEquiparati = repo.LoadComuniEquiparati();
            OutputStatusSedeList = Array.Empty<ValutazioneStatusSede>();
            OutputStatusSede = BuildOutputTable();
            _collectionCompleted = true;
            _calculationCompleted = false;
        }

        public void Calculate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("La raccolta dati status sede deve essere eseguita prima del calcolo.");

            var evaluator = new StatusSedeEvaluator(_comuniEquiparati);
            var results = new List<ValutazioneStatusSede>(_collectedInputs.Count);
            var (aaStart, aaEnd) = GetAaDateRange(_selectedAA);

            foreach (var inputRow in _collectedInputs)
            {
                var decision = evaluator.Evaluate(inputRow, aaStart, aaEnd);

                inputRow.Info.InformazioniSede.StatusSedeSuggerito = decision.SuggestedStatus;
                inputRow.Info.InformazioniSede.MotivoStatusSede = decision.Reason;
                inputRow.Info.InformazioniSede.DomicilioPresente = decision.DomicilioPresente;
                inputRow.Info.InformazioniSede.DomicilioValido = decision.DomicilioValido;
                inputRow.Info.InformazioniSede.HasAlloggio12 = inputRow.HasAlloggio12;
                inputRow.Info.InformazioniSede.HasIstanzaDomicilio = inputRow.HasIstanzaDomicilio;
                inputRow.Info.InformazioniSede.CodTipoIstanzaDomicilio = inputRow.CodTipoIstanzaDomicilio;
                inputRow.Info.InformazioniSede.NumIstanzaDomicilio = inputRow.NumIstanzaDomicilio;
                inputRow.Info.InformazioniSede.HasUltimaIstanzaChiusaDomicilio = inputRow.HasUltimaIstanzaChiusaDomicilio;
                inputRow.Info.InformazioniSede.CodTipoUltimaIstanzaChiusaDomicilio = inputRow.CodTipoUltimaIstanzaChiusaDomicilio;
                inputRow.Info.InformazioniSede.NumUltimaIstanzaChiusaDomicilio = inputRow.NumUltimaIstanzaChiusaDomicilio;
                inputRow.Info.InformazioniSede.EsitoUltimaIstanzaChiusaDomicilio = inputRow.EsitoUltimaIstanzaChiusaDomicilio;
                inputRow.Info.InformazioniSede.UtentePresaCaricoUltimaIstanzaChiusaDomicilio = inputRow.UtentePresaCaricoUltimaIstanzaChiusaDomicilio;

                results.Add(CreateResult(inputRow, decision));
            }

            OutputStatusSedeList = results;
            _calculationCompleted = true;
        }

        public void Validate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("La raccolta dati status sede deve essere eseguita prima della validazione.");
            if (!_calculationCompleted)
                throw new InvalidOperationException("Il calcolo status sede deve essere eseguito prima della validazione.");

            OutputStatusSede = ToDataTable(OutputStatusSedeList);
        }
    }
}
