namespace ProcedureNet7
{
    internal static class ControlloPecQueries
    {
        // Per gli AA precedenti resta valida la fonte storica. Dalla nuova
        // gestione contano soltanto dati correnti della domanda, non istanze.
        private const string RegisteredContracts = @"
;WITH ContrattiEsentiPec AS
(
    SELECT vd.ANNO_ACCADEMICO, vd.COD_FISCALE
    FROM dbo.vDomicilio vd
    WHERE vd.ANNO_ACCADEMICO >= @AnnoDa AND vd.ANNO_ACCADEMICO < '20262027'
      AND vd.Titolo_oneroso = 1 AND NULLIF(LTRIM(RTRIM(vd.N_serie_contratto)), '') IS NOT NULL
    UNION
    SELECT dom.ANNO_ACCADEMICO, dom.COD_FISCALE
    FROM dbo.Domicili dom
    WHERE dom.ANNO_ACCADEMICO >= @AnnoDa AND dom.ANNO_ACCADEMICO >= '20262027'
      AND dom.TIPO_LUOGO = 'DOM' AND dom.TIPO_DOMICILIO = '1'
      AND dom.DATA_FINE_VALIDITA IS NULL AND dom.IS_DELETED = 0
      AND EXISTS
      (
          SELECT 1 FROM dbo.Domanda d
          WHERE d.Anno_accademico = dom.ANNO_ACCADEMICO AND d.Num_domanda = dom.NUM_DOMANDA
            AND d.Cod_fiscale = dom.COD_FISCALE AND d.Tipo_bando = 'lz'
      )
      AND EXISTS
      (
          SELECT 1 FROM dbo.Contratti c
          WHERE c.ID_DOMICILIO = dom.ID AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED = 0
            AND c.TIPO_CONTRATTO_TITOLO_ONEROSO = 0
            AND NULLIF(LTRIM(RTRIM(c.N_SERIE_CONTRATTO)), '') IS NOT NULL
      )
), DomiciliAdeguatiPec AS
(
    SELECT DISTINCT dom.ANNO_ACCADEMICO, dom.COD_FISCALE, dom.NUM_DOMANDA
    FROM dbo.Domicili dom
    INNER JOIN dbo.Comuni cm ON cm.COD_COMUNE = dom.COD_COMUNE
    WHERE dom.ANNO_ACCADEMICO >= @AnnoDa AND dom.ANNO_ACCADEMICO >= '20262027'
      AND dom.TIPO_LUOGO = 'DOM' AND dom.DATA_FINE_VALIDITA IS NULL AND dom.IS_DELETED = 0
      AND NULLIF(LTRIM(RTRIM(dom.INDIRIZZO)), '') IS NOT NULL
      AND UPPER(LTRIM(RTRIM(dom.INDIRIZZO))) NOT IN ('ROMA', 'CASSINO', 'FROSINONE')
      AND NULLIF(LTRIM(RTRIM(cm.COD_PROVINCIA)), '') IS NOT NULL AND cm.COD_PROVINCIA <> 'EE'
      AND EXISTS
      (
          SELECT 1 FROM dbo.Domanda d
          WHERE d.Anno_accademico = dom.ANNO_ACCADEMICO AND d.Num_domanda = dom.NUM_DOMANDA
            AND d.Cod_fiscale = dom.COD_FISCALE AND d.Tipo_bando = 'lz'
      )
)
";

        public const string ApprovedDomains = @"
SELECT DISTINCT LTRIM(RTRIM(Dominio)) AS Dominio
FROM dbo.Domini_PEC
WHERE Approvato = 1 AND NULLIF(LTRIM(RTRIM(Dominio)), '') IS NOT NULL;";

        public static string RemovalCandidates => RegisteredContracts + @"
SELECT ANNO_ACCADEMICO, COD_FISCALE FROM ContrattiEsentiPec
UNION
SELECT ANNO_ACCADEMICO, COD_FISCALE FROM DomiciliAdeguatiPec
ORDER BY COD_FISCALE, ANNO_ACCADEMICO;";

        // La PEC viene validata in C# con le stesse regole del portale, anche
        // quando contiene un display name: il dominio non si estrae dal raw SQL.
        public static string BlockCandidates => Candidates(paymentExtraction: false);

        public static string PaymentCandidates => Candidates(paymentExtraction: true);

        private static string Candidates(bool paymentExtraction) => RegisteredContracts + $@"
SELECT DISTINCT LRS.ANNO_ACCADEMICO, LRS.COD_FISCALE, p.Indirizzo_PEC, vb.Cod_tipo_esito
FROM dbo.LUOGO_REPERIBILITA_STUDENTE LRS
{(paymentExtraction ? "INNER JOIN #CFEstrazione cfe ON cfe.Cod_fiscale = LRS.COD_FISCALE" : string.Empty)}
LEFT JOIN dbo.vProfilo p ON p.Cod_Fiscale = LRS.COD_FISCALE
INNER JOIN dbo.Domanda d ON d.Anno_accademico = LRS.ANNO_ACCADEMICO
    AND d.Cod_fiscale = LRS.COD_FISCALE AND d.Tipo_bando = 'lz'
INNER JOIN dbo.vEsiti_concorsiBS vb ON vb.Anno_accademico = d.Anno_accademico AND vb.Num_domanda = d.Num_domanda
WHERE LRS.ANNO_ACCADEMICO {(paymentExtraction ? "=" : ">=")} @AnnoDa AND LRS.tipo_bando = 'lz'
  AND LRS.TIPO_LUOGO = 'DOL' AND LRS.DATA_FINE_VALIDITA IS NULL
  AND (LRS.INDIRIZZO = '' OR LRS.INDIRIZZO IN ('ROMA', 'CASSINO', 'FROSINONE'))
  AND EXISTS
  (
      SELECT 1 FROM dbo.vResidenza vr WHERE vr.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO
        AND vr.COD_FISCALE = LRS.COD_FISCALE AND vr.provincia_residenza = 'EE'
  )
  AND
  (
      (LRS.ANNO_ACCADEMICO < '20262027' AND EXISTS
      (
          SELECT 1 FROM dbo.vDomicilio vd
          WHERE vd.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO AND vd.COD_FISCALE = LRS.COD_FISCALE
            AND (vd.Indirizzo_domicilio = '' OR vd.Indirizzo_domicilio IN ('ROMA', 'CASSINO', 'FROSINONE') OR vd.prov = 'EE')
      ))
      OR
      (LRS.ANNO_ACCADEMICO >= '20262027' AND NOT EXISTS
      (
          SELECT 1 FROM DomiciliAdeguatiPec dom
          WHERE dom.ANNO_ACCADEMICO = d.Anno_accademico AND dom.NUM_DOMANDA = d.Num_domanda
            AND dom.COD_FISCALE = d.Cod_fiscale
      ))
  )
  AND NOT EXISTS
  (
      SELECT 1 FROM ContrattiEsentiPec e
      WHERE e.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO AND e.COD_FISCALE = LRS.COD_FISCALE
  )
  AND vb.Cod_tipo_esito <> 0
ORDER BY LRS.COD_FISCALE, LRS.ANNO_ACCADEMICO;";
    }
}
