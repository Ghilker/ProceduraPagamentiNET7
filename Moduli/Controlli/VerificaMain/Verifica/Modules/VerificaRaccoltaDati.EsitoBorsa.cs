using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string EsitoBorsaForzatureSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CONVERT(NVARCHAR(20), f.COD_FORZATURA) AS CodForzatura
FROM {TEMP_TABLE} t
JOIN FORZATURE f
  ON f.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL
  AND TRY_CONVERT(INT, f.COD_FORZATURA) > 200;";

        private const string EsitoBorsaForzatureRinunciaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale
FROM {TEMP_TABLE} t
JOIN FORZATURE_RINUNCIA f
  ON UPPER(LTRIM(RTRIM(f.COD_FISCALE))) = UPPER(LTRIM(RTRIM(t.CodFiscale)))
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL;";

        private const string EsitoBorsaCodTipoOrdinamentoSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale,
        COALESCE(t.TipoBando,'') AS TipoBando
    FROM {TEMP_TABLE} t
),
ISCR AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        CONVERT(NVARCHAR(50), i.Cod_tipo_ordinamento) AS Cod_tipo_ordinamento,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY COALESCE(CONVERT(NVARCHAR(50), i.Cod_tipo_ordinamento),'')) AS rn
    FROM D
    LEFT JOIN vIscrizioni i
      ON i.Anno_accademico = @AA
     AND i.Cod_fiscale = D.CodFiscale
     AND COALESCE(i.tipo_bando,'') = D.TipoBando
)
SELECT NumDomanda, CodFiscale, ISNULL(Cod_tipo_ordinamento,'') AS Cod_tipo_ordinamento
FROM ISCR
WHERE rn = 1;";

        private void LoadEsitoBorsaSupportFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.EsitoBorsaFactsByStudent.Clear();
            foreach (var key in context.Students.Keys)
                context.EsitoBorsaFactsByStudent[key] = new EsitoBorsaFacts();

            LoadEsitoBorsaForzature(context);
            LoadEsitoBorsaForzatureRinuncia(context);
            LoadEsitoBorsaCodTipoOrdinamento(context);
        }

        private void LoadEsitoBorsaForzature(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                string code = reader.SafeGetString("CodForzatura").Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    facts.ForzatureGenerali.Add(code);
            });
        }

        private void LoadEsitoBorsaForzatureRinuncia(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureRinunciaSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.ForzaturaRinunciaNoEsclusione = true;
            });
        }

        private void LoadEsitoBorsaCodTipoOrdinamento(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaCodTipoOrdinamentoSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.CodTipoOrdinamento = reader.SafeGetString("Cod_tipo_ordinamento").Trim();
            });
        }

        private static EsitoBorsaFacts GetOrCreateEsitoBorsaFacts(VerificaPipelineContext context, StudentKey key)
        {
            if (!context.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts) || facts == null)
            {
                facts = new EsitoBorsaFacts();
                context.EsitoBorsaFactsByStudent[key] = facts;
            }

            return facts;
        }
    }
}
