using System;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal sealed class GeneratoriAllegatiFactory
    {
        private readonly Dictionary<string, IGeneratoreAllegato> generatori;

        public GeneratoriAllegatiFactory(SqlConnection connection)
        {
            generatori = new Dictionary<string, IGeneratoreAllegato>(StringComparer.OrdinalIgnoreCase);

            Register(new GeneratoreAllegatoDecadenza(connection, GeneratoreAllegatoDecadenza.Modalita.SenzaRecuperoSomme));
            Register(new GeneratoreAllegatoDecadenza(connection, GeneratoreAllegatoDecadenza.Modalita.ConRecuperoSomme));
            Register(new GeneratoreAllegatoRiammissioneVincitore(connection));
            Register(new GeneratoreAllegatoRiammissioneVincitore(
                connection,
                GeneratoreAllegatoRiammissioneVincitore.Modalita.IdoneoAVincitore));
            Register(new GeneratoreAllegatoRiammissioneIdoneo(connection));
            Register(new GeneratoreAllegatoModificaImporto(connection));
            Register(new GeneratoreAllegatoModificaImporto(
                connection,
                GeneratoreAllegatoModificaImporto.Modalita.CambioStatusSede));
        }

        public IGeneratoreAllegato GetRequired(string codice)
        {
            string key = codice?.Trim() ?? string.Empty;

            if (generatori.TryGetValue(key, out IGeneratoreAllegato? generatore))
                return generatore;

            throw new InvalidOperationException(
                $"Tipo allegato '{codice}' non gestito dalla procedura allegati.");
        }

        private void Register(IGeneratoreAllegato generatore)
        {
            generatori[generatore.Codice] = generatore;
        }
    }
}
