using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
        private sealed class SplitResult
        {
            public List<Target> OrigIT_CO { get; } = new();
            public List<Target> OrigIT_DO { get; } = new();     // IT dichiarato, ma INPS non ok -> usa vCertificaz_ISEE 'DO'
            public List<Target> OrigEE { get; } = new();        // redditi estero -> usa nucleo stranieri DO

            public List<Target> IntIT_CI { get; } = new();      // integrazione IT + INPS ok -> vCertificaz_ISEE 'CI'
            public List<Target> IntDI { get; } = new();         // integrazione estero o IT non ok -> nucleo stranieri 'DI'
        }

        private SplitResult LoadTipologieRedditiAndSplit(string aa)
        {
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi) + split stored-like.");

            var result = new SplitResult();

            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    tr.Tipo_redd_nucleo_fam_origine,
    tr.Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi,0) AS altri_mezzi
FROM #TargetsEconomici t
INNER JOIN Domanda d
    ON d.Anno_accademico = @AA
   AND d.Num_domanda = t.Num_domanda
INNER JOIN vTipologie_redditi tr
    ON d.Anno_accademico = tr.Anno_accademico
   AND d.Num_domanda     = tr.Num_domanda
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'lz'
ORDER BY t.Cod_fiscale, t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            int readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;

                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");
                if (string.IsNullOrWhiteSpace(codFiscale) || string.IsNullOrWhiteSpace(numDomanda)) continue;

                var target = new Target(codFiscale, numDomanda);
                if (!TryGetEconomicRow(codFiscale, numDomanda, out var economicRow)) continue;

                string tipoOrigine = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_origine"));
                string tipoIntegrazione = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_integr"));

                economicRow.TipoRedditoOrigine = tipoOrigine;
                economicRow.TipoRedditoIntegrazione = tipoIntegrazione;

                economicRow.AltriMezzi = reader.SafeGetDecimal("altri_mezzi");

                // === ORIGINE ===
                if (tipoOrigine.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    int statusInps = _statusInpsOrigineByKey.TryGetValue(BuildStudentKey(codFiscale, numDomanda), out var found) ? found : 0;
                    bool coOk = statusInps == 2 && (_coAttestazioneOkByKey.TryGetValue(BuildStudentKey(codFiscale, numDomanda), out var ok) && ok);

                    if (coOk) result.OrigIT_CO.Add(target);
                    else result.OrigIT_DO.Add(target);
                }
                else if (tipoOrigine.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.OrigEE.Add(target);
                }

                // === INTEGRAZIONE === (solo se nucleo = 'I' come stored)
                bool doIntegrazione = string.Equals(economicRow.TipoNucleo, "I", StringComparison.OrdinalIgnoreCase)
                                      && !string.IsNullOrWhiteSpace(tipoIntegrazione);

                if (doIntegrazione)
                {
                    if (tipoIntegrazione.Equals("it", StringComparison.OrdinalIgnoreCase))
                    {
                        int statusInpsI = _statusInpsIntegrazioneByCf.TryGetValue(codFiscale, out var foundI) ? foundI : 0;
                        if (statusInpsI == 2) result.IntIT_CI.Add(target);
                        else result.IntDI.Add(target);
                    }
                    else if (tipoIntegrazione.Equals("ee", StringComparison.OrdinalIgnoreCase))
                    {
                        result.IntDI.Add(target);
                    }
                }
            }

            Logger.LogInfo(33, $"Tipologie reddito lette: {readCount}");
            return result;
        }

        // =========================
        //  ESTRAZIONE ECONOMICI - IT (CO)
        // =========================

    }
}
