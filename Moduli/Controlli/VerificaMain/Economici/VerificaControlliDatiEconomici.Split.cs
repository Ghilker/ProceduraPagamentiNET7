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
            public List<string> OrigIT_CO { get; } = new();
            public List<string> OrigIT_DO { get; } = new();     // IT dichiarato, ma INPS non ok -> usa vCertificaz_ISEE 'DO'
            public List<string> OrigEE { get; } = new();        // redditi estero -> usa nucleo stranieri DO

            public List<string> IntIT_CI { get; } = new();      // integrazione IT + INPS ok -> vCertificaz_ISEE 'CI'
            public List<string> IntDI { get; } = new();         // integrazione estero o IT non ok -> nucleo stranieri 'DI'
        }

        private SplitResult LoadTipologieRedditiAndSplit(string aa)
        {
            Logger.LogInfo(30, "Esecuzione query tipologie reddito (vTipologie_redditi) + split stored-like.");

            var result = new SplitResult();

            const string sql = @"
SELECT
    d.Cod_fiscale,
    tr.Tipo_redd_nucleo_fam_origine,
    tr.Tipo_redd_nucleo_fam_integr,
    ISNULL(tr.altri_mezzi,0) AS altri_mezzi
FROM Domanda d
INNER JOIN #CFEstrazione cfe
    ON UPPER(LTRIM(RTRIM(d.Cod_fiscale))) = cfe.Cod_fiscale
INNER JOIN vTipologie_redditi tr
    ON d.Anno_accademico = tr.Anno_accademico
   AND d.Num_domanda     = tr.Num_domanda
WHERE d.Anno_accademico = @AA
  AND d.Tipo_bando = 'lz'
ORDER BY d.Cod_fiscale;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            int readCount = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                readCount++;

                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                string tipoOrigine = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_origine"));
                string tipoIntegrazione = Utilities.RemoveAllSpaces(reader.SafeGetString("Tipo_redd_nucleo_fam_integr"));

                economicRow.TipoRedditoOrigine = tipoOrigine;
                economicRow.TipoRedditoIntegrazione = tipoIntegrazione;

                economicRow.AltriMezzi = reader.SafeGetDecimal("altri_mezzi");

                // === ORIGINE ===
                if (tipoOrigine.Equals("it", StringComparison.OrdinalIgnoreCase))
                {
                    int statusInps = _statusInpsOrigineByCf.TryGetValue(codFiscale, out var found) ? found : 0;
                    bool coOk = statusInps == 2 && (_coAttestazioneOkByCf.TryGetValue(codFiscale, out var ok) && ok);

                    if (coOk) result.OrigIT_CO.Add(codFiscale);
                    else result.OrigIT_DO.Add(codFiscale);
                }
                else if (tipoOrigine.Equals("ee", StringComparison.OrdinalIgnoreCase))
                {
                    result.OrigEE.Add(codFiscale);
                }

                // === INTEGRAZIONE === (solo se nucleo = 'I' come stored)
                bool doIntegrazione = string.Equals(economicRow.TipoNucleo, "I", StringComparison.OrdinalIgnoreCase)
                                      && !string.IsNullOrWhiteSpace(tipoIntegrazione);

                if (doIntegrazione)
                {
                    if (tipoIntegrazione.Equals("it", StringComparison.OrdinalIgnoreCase))
                    {
                        int statusInpsI = _statusInpsIntegrazioneByCf.TryGetValue(codFiscale, out var foundI) ? foundI : 0;
                        if (statusInpsI == 2) result.IntIT_CI.Add(codFiscale);
                        else result.IntDI.Add(codFiscale);
                    }
                    else if (tipoIntegrazione.Equals("ee", StringComparison.OrdinalIgnoreCase))
                    {
                        result.IntDI.Add(codFiscale);
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
