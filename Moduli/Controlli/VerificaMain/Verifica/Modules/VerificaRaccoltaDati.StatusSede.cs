using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private static readonly object ComuniEquiparatiCacheLock = new();
        private static HashSet<(string ComuneA, string ComuneB)>? _comuniEquiparatiCache;
        private static DateTime _comuniEquiparatiCacheLoadedAtUtc;
        private static readonly TimeSpan ComuniEquiparatiCacheTtl = TimeSpan.FromMinutes(30);

        private HashSet<(string ComuneA, string ComuneB)> LoadComuniEquiparatiFromDb()
        {
            lock (ComuniEquiparatiCacheLock)
            {
                if (_comuniEquiparatiCache != null && (DateTime.UtcNow - _comuniEquiparatiCacheLoadedAtUtc) <= ComuniEquiparatiCacheTtl)
                    return new HashSet<(string ComuneA, string ComuneB)>(_comuniEquiparatiCache);
            }

            const string sql = @"
SELECT
    UPPER(Cod_Comune_A) AS Cod_Comune_A,
    UPPER(Cod_Comune_B) AS Cod_Comune_B
FROM dbo.STATUS_SEDE_COMUNI_EQUIVALENTI
WHERE Data_Fine_Validita IS NULL;";

            var result = new HashSet<(string ComuneA, string ComuneB)>();

            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.ComuniEquiparati", "cache-refresh");
            using var cmd = new SqlCommand(sql, _conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999,
            };

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string comuneA = reader.SafeGetString("Cod_Comune_A").Trim().ToUpperInvariant();
                string comuneB = reader.SafeGetString("Cod_Comune_B").Trim().ToUpperInvariant();

                if (comuneA.Length == 0 || comuneB.Length == 0)
                    continue;

                result.Add(NormalizeComunePair(comuneA, comuneB));
            }

            lock (ComuniEquiparatiCacheLock)
            {
                _comuniEquiparatiCache = new HashSet<(string ComuneA, string ComuneB)>(result);
                _comuniEquiparatiCacheLoadedAtUtc = DateTime.UtcNow;
            }

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Comuni equiparati caricati: {result.Count}");
            return result;
        }
    }
}
