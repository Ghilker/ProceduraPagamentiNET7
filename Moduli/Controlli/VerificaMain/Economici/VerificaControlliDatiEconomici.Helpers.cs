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
        private void LoadInpsAndAttestazioni_StoredLike(string aa, List<Target> targets)
        {
            EnsureTempTargetsTableAndFill(targets);

            _statusInpsOrigineByKey.Clear();
            _statusInpsIntegrazioneByCf.Clear();
            _coAttestazioneOkByKey.Clear();

            // ORIGINE: come stored (NOT IN ('CI','DI') + filtro num_domanda)
            const string sqlOrig = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.num_domanda     = t.Num_domanda
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz NOT IN ('CI','DI');";

            using (var command = new SqlCommand(sqlOrig, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    string numDomanda = reader.SafeGetString("Num_domanda");
                    if (string.IsNullOrWhiteSpace(codFiscale) || string.IsNullOrWhiteSpace(numDomanda)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    _statusInpsOrigineByKey[BuildStudentKey(codFiscale, numDomanda)] = statusInps;
                }
            }

            // INTEGRAZIONE: come stored (IN ('CI','DI') e senza filtro num_domanda)
            const string sqlInt = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    si.status_inps
FROM #TargetsEconomici t
LEFT JOIN vStatus_INPS si
    ON si.anno_accademico = @AA
   AND si.cod_fiscale     = t.Cod_fiscale
   AND si.data_fine_validita IS NULL
   AND si.tipo_certificaz IN ('CI','DI');";

            using (var command = new SqlCommand(sqlInt, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    if (string.IsNullOrWhiteSpace(codFiscale)) continue;

                    int statusInps = reader.SafeGetInt("status_inps");
                    _statusInpsIntegrazioneByCf[codFiscale] = statusInps;
                }
            }

            // CO attestazione: come stored (per 20242025+ basta “non vuoto”)
            const string sqlAtt = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    LTRIM(RTRIM(ISNULL(cte.Cod_tipo_attestazione,''))) AS Cod_tipo_attestazione
FROM #TargetsEconomici t
LEFT JOIN vCertificaz_ISEE cte
    ON cte.Anno_accademico = @AA
   AND cte.Num_domanda     = t.Num_domanda
   AND cte.tipologia_certificazione = 'CO';";

            using (var command = new SqlCommand(sqlAtt, _conn))
            {
                command.Parameters.AddWithValue("@AA", aa);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                    string numDomanda = reader.SafeGetString("Num_domanda");
                    if (string.IsNullOrWhiteSpace(codFiscale) || string.IsNullOrWhiteSpace(numDomanda)) continue;

                    string tipoAttestazione = reader.SafeGetString("Cod_tipo_attestazione");
                    _coAttestazioneOkByKey[BuildStudentKey(codFiscale, numDomanda)] = !string.IsNullOrWhiteSpace(tipoAttestazione);
                }
            }
        }

        private static decimal RoundSql(decimal value, int decimals) =>
            Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        private static decimal ScalaMin(int componentCount)
        {
            if (componentCount < 1) componentCount = 1;
            return componentCount switch
            {
                1 => 1.00m,
                2 => 1.57m,
                3 => 2.04m,
                4 => 2.46m,
                5 => 2.85m,
                _ => 2.85m + (componentCount - 5) * 0.35m
            };
        }

        private static int GetEseFinanziario(string aa) => aa switch
        {
            "20252026" => 2023,
            "20242025" => 2022,
            "20232024" => 2021,
            "20222023" => 2018,
            "20212022" => 2019,
            _ => int.TryParse(aa.Substring(0, 4), out var year) ? year - 2 : 0
        };

        private static string GetFiltroCodTipoPagam(string aa) =>
            aa == "20252026"
                ? "(p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5') OR p.Cod_tipo_pagam LIKE 'BSA%' OR p.Cod_tipo_pagam LIKE 'BSI%' OR p.Cod_tipo_pagam LIKE 'BSS%' OR p.Cod_tipo_pagam LIKE 'PL%')"
                : "p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5')";

        private void LoadCalcParams(string aa)
        {
            const string sql = @"
SELECT Franchigia, tasso_rendimento_pat_mobiliare, franchigia_pat_mobiliare, Importo_borsa_A, Importo_borsa_B, Importo_borsa_C, Soglia_Isee
FROM DatiGenerali_con
WHERE Anno_accademico = @AA;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                _calc.Franchigia = reader.SafeGetDecimal("Franchigia");
                _calc.RendPatr = reader.SafeGetDecimal("tasso_rendimento_pat_mobiliare");
                _calc.FranchigiaPatMob = reader.SafeGetDecimal("franchigia_pat_mobiliare");
                _calc.ImportoBorsaA = reader.SafeGetDecimal("Importo_borsa_A");
                _calc.ImportoBorsaB = reader.SafeGetDecimal("Importo_borsa_B");
                _calc.ImportoBorsaC = reader.SafeGetDecimal("Importo_borsa_C");
                _calc.SogliaIsee = reader.SafeGetDecimal("Soglia_Isee");
            }
        }

        private void LoadNucleoFamiliare(string aa)
        {
            const string sql = @"
SELECT
    t.Cod_fiscale,
    t.Num_domanda,
    ISNULL(nf.Num_componenti, 0) AS Num_componenti,
    ISNULL(nf.Cod_tipologia_nucleo, '') AS Cod_tipologia_nucleo,
    ISNULL(nf.Numero_conviventi_estero, 0) AS Numero_conviventi_estero
FROM #TargetsEconomici t
INNER JOIN vNucleo_familiare nf
    ON nf.Anno_accademico = @AA
   AND nf.Num_domanda     = t.Num_domanda;";

            using var command = new SqlCommand(sql, _conn);
            command.Parameters.AddWithValue("@AA", aa);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string codFiscale = Utilities.RemoveAllSpaces(reader.SafeGetString("Cod_fiscale").ToUpperInvariant());
                string numDomanda = reader.SafeGetString("Num_domanda");
                if (!TryGetEconomicRow(codFiscale, numDomanda, out var economicRow)) continue;

                economicRow.NumeroComponenti = reader.SafeGetInt("Num_componenti");
                economicRow.TipoNucleo = reader.SafeGetString("Cod_tipologia_nucleo");
                economicRow.NumeroConviventiEstero = reader.SafeGetInt("Numero_conviventi_estero");
            }
        }

        private decimal ComputeSeqFinal(EconomicRow economicRow)
        {
            // maggiorazioni studente
            int componentiTotali = economicRow.NumeroComponenti > 0 ? economicRow.NumeroComponenti : 1;
            int conviventiEstero = Math.Max(economicRow.NumeroConviventiEstero, 0);

            decimal maggiorazioneStudente = 0m;
            int componentiStudente = componentiTotali;

            if (string.Equals(economicRow.TipoRedditoOrigine, "it", StringComparison.OrdinalIgnoreCase))
            {
                int baseComponenti = Math.Max(componentiTotali - conviventiEstero, 1);
                maggiorazioneStudente = (economicRow.SEQ_Origine > 0 ? economicRow.SEQ_Origine : 0m) - ScalaMin(baseComponenti);
                componentiStudente = baseComponenti + conviventiEstero; // come stored
            }

            // integrazione
            int componentiIntegrazione = Math.Max(economicRow.NumeroComponentiIntegrazione, 0);
            if (componentiIntegrazione <= 0)
            {
                var seqSoloStudente = ScalaMin(componentiStudente) + maggiorazioneStudente;
                return RoundSql(seqSoloStudente <= 0 ? 1m : seqSoloStudente, 2);
            }

            decimal maggiorazioneIntegrazione = 0m;
            if (string.Equals(economicRow.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase))
                maggiorazioneIntegrazione = (economicRow.SEQ_Integrazione > 0 ? economicRow.SEQ_Integrazione : 0m) - ScalaMin(componentiIntegrazione);

            int componentiTot = componentiStudente + componentiIntegrazione;
            decimal seq = ScalaMin(componentiTot) + maggiorazioneStudente +
                          (string.Equals(economicRow.TipoRedditoIntegrazione, "it", StringComparison.OrdinalIgnoreCase) ? maggiorazioneIntegrazione : 0m);

            return RoundSql(seq <= 0 ? 1m : seq, 2);
        }
    }
}