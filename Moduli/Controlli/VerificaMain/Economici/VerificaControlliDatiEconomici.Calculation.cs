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
        private void CalcoloDatiEconomici()
        {
            foreach (var economicRow in _rows.Values)
            {
                economicRow.SEQ = ComputeSeqFinal(economicRow);

                economicRow.ISRDSU = Math.Max(economicRow.ISRDSU - economicRow.Detrazioni, 0m);

                decimal isedsu = economicRow.ISRDSU + 0.2m * economicRow.ISPDSU;
                decimal iseed = economicRow.SEQ > 0 ? isedsu / economicRow.SEQ : isedsu;
                decimal ispe = (economicRow.ISPDSU > 0 && economicRow.SEQ > 0) ? economicRow.ISPDSU / economicRow.SEQ : 0m;

                economicRow.ISEDSU = RoundSql(isedsu, 2);
                economicRow.ISEEDSU = RoundSql(iseed, 2);
                economicRow.ISPEDSU = RoundSql(ispe, 2);
            }
        }

        private static double CalculateSEQ(int numComponenti)
        {
            if (numComponenti < 1) return 1;

            double seq = numComponenti switch
            {
                1 => 1.00,
                2 => 1.57,
                3 => 2.04,
                4 => 2.46,
                5 => 2.85,
                _ => 2.85 + (numComponenti - 5) * 0.35
            };

            return Math.Round(seq, 2);
        }

        // =========================
        //  TEMP TABLE CF + BULK
        // =========================

    }
}
