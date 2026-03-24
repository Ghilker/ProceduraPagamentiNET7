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
        private void LoadValoriCalcolatiAttuali(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
SELECT
    t.Cod_fiscale,
    vv.ISPEDSU,
    vv.ISEDSU,
    vv.SEQ,
    vv.ISPDSU,
    vv.ISEEDSU
FROM #TargetsEconomici t
LEFT JOIN vValori_calcolati vv
    ON vv.Anno_accademico = @AA
   AND vv.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                economicRow.ISPEDSU_Attuale = reader.SafeGetDouble("ISPEDSU");
                economicRow.ISEDSU_Attuale = reader.SafeGetDouble("ISEDSU");
                economicRow.SEQ_Attuale = reader.SafeGetDouble("SEQ");
                economicRow.ISPDSU_Attuale = reader.SafeGetDouble("ISPDSU");
                economicRow.ISEEDSU_Attuale = reader.SafeGetDouble("ISEEDSU");
            }
        }

        // =========================
        //  ESITO CONCORSO BS (vEsiti_concorsi)
        // =========================

        private void LoadEsitoBorsaStudio(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            const string sql = @"
WITH EsitoBS AS
(
    SELECT
        ec.Anno_accademico,
        ec.Num_domanda,
        MAX(ec.Cod_tipo_esito) AS Cod_tipo_esito,
        MAX(ec.imp_beneficio) AS imp_assegnato
    FROM vEsiti_concorsi ec
    WHERE ec.Anno_accademico = @AA
      AND ec.Cod_beneficio = 'BS'
    GROUP BY ec.Anno_accademico, ec.Num_domanda
)
SELECT
    t.Cod_fiscale,
    e.Cod_tipo_esito,
    e.imp_assegnato
FROM #TargetsEconomici t
LEFT JOIN EsitoBS e
    ON e.Anno_accademico = @AA
   AND e.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                if (codFiscale == debugCF) { string _ = ""; }

                if (!_rows.TryGetValue(codFiscale, out var economicRow)) continue;

                object rawEsito = reader["Cod_tipo_esito"];
                int? codTipoEsito = rawEsito is DBNull or null ? (int?)null : Convert.ToInt32(rawEsito, CultureInfo.InvariantCulture);

                economicRow.CodTipoEsitoBS = codTipoEsito;

                double importoAssegnato = Utilities.SafeGetDouble(reader, "imp_assegnato");
                economicRow.ImportoAssegnato = importoAssegnato;
            }
        }

        // =========================
        //  TIPOLOGIE REDDITI + SPLIT
        // =========================

    }
}
