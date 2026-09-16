/*
    Diagnostica del calcolo ISEE/ISEE DSU eseguito dalla verifica.

    La query non modifica dati.
    Valorizzare @AnnoAccademico e almeno uno tra @NumDomanda e @CodFiscale.

    Fonti gestite dalla verifica:
      CO = attestazione italiana di origine
      EE = redditi esteri di origine (Nucleo_fam_stranieri, tipologia DO)
      CI = attestazione italiana di integrazione
      DI = redditi esteri di integrazione

    Formule finali:
      ISR netto = MAX(ISR origine + ISR integrazione - detrazioni, 0)
      ISE DSU   = ISR netto + 20% di ISP DSU
      ISEE DSU  = ISE DSU / scala di equivalenza finale
      ISPE DSU  = ISP DSU / scala di equivalenza finale
*/

SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

DECLARE @AnnoAccademico char(8) = '20262027';
DECLARE @NumDomanda numeric(18,0) = NULL; -- esempio: 123456
DECLARE @CodFiscale varchar(16) = NULL;   -- esempio: 'RSSMRA...'
DECLARE @DataRiferimento date = GETDATE();

DECLARE @AnnoInizio int = TRY_CONVERT(int, LEFT(@AnnoAccademico, 4));
DECLARE @FirmataIlMax date = DATEFROMPARTS(@AnnoInizio, 12, 31);
DECLARE @ScadenzaIseeBase date = DATEFROMPARTS(@AnnoInizio, 7, 22);
DECLARE @ConsentiOrdinarioInAttesa bit =
    CASE WHEN @DataRiferimento <= DATEFROMPARTS(@AnnoInizio, 12, 10) THEN 1 ELSE 0 END;
DECLARE @EsercizioFinanziario int =
    CASE @AnnoAccademico
        WHEN '20252026' THEN 2023
        WHEN '20242025' THEN 2022
        WHEN '20232024' THEN 2021
        WHEN '20222023' THEN 2018
        WHEN '20212022' THEN 2019
        ELSE @AnnoInizio - 2
    END;

IF @NumDomanda IS NULL AND NULLIF(LTRIM(RTRIM(@CodFiscale)), '') IS NULL
    THROW 50001, 'Valorizzare @NumDomanda oppure @CodFiscale.', 1;

;WITH
DomandeRanked AS
(
    SELECT
        d.Anno_accademico,
        d.Num_domanda,
        d.Cod_fiscale,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.Anno_accademico, d.Num_domanda
            ORDER BY d.Data_validita DESC
        ) AS rn
    FROM Domanda d
    WHERE d.Anno_accademico = @AnnoAccademico
      AND (@NumDomanda IS NULL OR d.Num_domanda = @NumDomanda)
      AND
      (
          NULLIF(LTRIM(RTRIM(@CodFiscale)), '') IS NULL
          OR UPPER(d.Cod_fiscale) = UPPER(LTRIM(RTRIM(@CodFiscale)))
      )
),
Target AS
(
    SELECT Anno_accademico, Num_domanda, Cod_fiscale
    FROM DomandeRanked
    WHERE rn = 1
),
Parametri AS
(
    SELECT TOP (1)
        TRY_CONVERT(decimal(18,6), Franchigia) AS FranchigiaImmobiliare,
        TRY_CONVERT(decimal(18,6), tasso_rendimento_pat_mobiliare) AS TassoRendimentoPatrimonio,
        TRY_CONVERT(decimal(18,6), franchigia_pat_mobiliare) AS FranchigiaPatrimonioMobiliare,
        TRY_CONVERT(decimal(18,2), Soglia_Isee) AS SogliaIsee
    FROM DatiGenerali_con
    WHERE Anno_accademico = @AnnoAccademico
),
TipologieRanked AS
(
    SELECT
        tr.Num_domanda,
        UPPER(LTRIM(RTRIM(ISNULL(tr.Tipo_redd_nucleo_fam_origine, '')))) AS TipoRedditoOrigine,
        UPPER(LTRIM(RTRIM(ISNULL(tr.Tipo_redd_nucleo_fam_integr, '')))) AS TipoRedditoIntegrazione,
        TRY_CONVERT(decimal(18,2), ISNULL(tr.altri_mezzi, 0)) AS AltriMezzi,
        ROW_NUMBER() OVER
        (
            PARTITION BY tr.Num_domanda
            ORDER BY tr.data_validita DESC
        ) AS rn
    FROM Tipologie_redditi tr
    INNER JOIN Target t ON t.Num_domanda = tr.Num_domanda
    WHERE tr.Anno_accademico = @AnnoAccademico
),
Tipologie AS
(
    SELECT Num_domanda, TipoRedditoOrigine, TipoRedditoIntegrazione, AltriMezzi
    FROM TipologieRanked
    WHERE rn = 1
),
NucleoRanked AS
(
    SELECT
        nf.Num_domanda,
        TRY_CONVERT(int, ISNULL(nf.Num_componenti, 0)) AS NumeroComponenti,
        TRY_CONVERT(int, ISNULL(nf.Numero_conviventi_estero, 0)) AS NumeroConviventiEstero,
        nf.Cod_tipologia_nucleo,
        ROW_NUMBER() OVER
        (
            PARTITION BY nf.Num_domanda
            ORDER BY nf.data_validita DESC
        ) AS rn
    FROM Nucleo_familiare nf
    INNER JOIN Target t ON t.Num_domanda = nf.Num_domanda
    WHERE nf.Anno_accademico = @AnnoAccademico
),
Nucleo AS
(
    SELECT Num_domanda, NumeroComponenti, NumeroConviventiEstero, Cod_tipologia_nucleo
    FROM NucleoRanked
    WHERE rn = 1
),
IscrizioniRanked AS
(
    SELECT
        t.Num_domanda,
        TRY_CONVERT(int, ISNULL(i.CONFERMA_SEMESTRE_FILTRO, 0)) AS ConfermaSemestreFiltro,
        ROW_NUMBER() OVER
        (
            PARTITION BY t.Num_domanda
            ORDER BY i.DATA_VALIDITA DESC
        ) AS rn
    FROM Target t
    LEFT JOIN ISCRIZIONI i
        ON i.COD_FISCALE = t.Cod_fiscale
       AND i.ANNO_ACCADEMICO = @AnnoAccademico
       AND (i.TIPO_BANDO IS NULL OR i.TIPO_BANDO LIKE 'L%')
),
Iscrizione AS
(
    SELECT Num_domanda, ConfermaSemestreFiltro
    FROM IscrizioniRanked
    WHERE rn = 1
),
PagamentiDetraibili AS
(
    SELECT
        d.Cod_fiscale,
        SUM(TRY_CONVERT(decimal(18,2), ISNULL(p.imp_pagato, 0))) AS DetrazioniAdisu
    FROM Pagamenti p
    INNER JOIN Domanda d
        ON d.Anno_accademico = p.Anno_accademico
       AND d.Num_domanda = p.Num_domanda
    INNER JOIN Target t
        ON t.Cod_fiscale = d.Cod_fiscale
    WHERE p.Ritirato_azienda = 0
      AND p.Ese_finanziario = @EsercizioFinanziario
      AND
      (
          p.Cod_tipo_pagam IN ('01','06','09','34','39','41','R1','R3','R4','R9','RR','S0','S1','S3','S5')
          OR
          (
              TRY_CONVERT(int, @AnnoAccademico) >= 20252026
              AND
              (
                  p.Cod_tipo_pagam LIKE 'BSI%'
                  OR p.Cod_tipo_pagam LIKE 'BSS%'
                  OR p.Cod_tipo_pagam LIKE 'BSP%'
                  OR p.Cod_tipo_pagam LIKE 'PL%'
              )
          )
      )
    GROUP BY d.Cod_fiscale
),
AltreBorseValide AS
(
    SELECT
        vb.num_domanda,
        vb.importo_borsa
    FROM Importi_borsa_percepiti vb
    INNER JOIN Target t ON t.Num_domanda = vb.num_domanda
    INNER JOIN Allegati a
        ON a.anno_accademico = vb.anno_accademico
       AND a.num_domanda = vb.num_domanda
       AND a.cod_tipo_allegato = '07'
       AND a.data_fine_validita IS NULL
       AND a.data_validita =
       (
           SELECT MAX(a2.data_validita)
           FROM Allegati a2
           WHERE a2.id_allegato = a.id_allegato
             AND a2.data_fine_validita IS NULL
       )
    INNER JOIN Status_allegati sa
        ON sa.id_allegato = a.id_allegato
       AND sa.data_fine_validita IS NULL
       AND sa.cod_status IN ('03','05')
       AND sa.data_validita =
       (
           SELECT MAX(sa2.data_validita)
           FROM Status_allegati sa2
           WHERE sa2.id_allegato = sa.id_allegato
             AND sa2.data_fine_validita IS NULL
       )
    WHERE vb.anno_accademico = @AnnoAccademico
      AND vb.data_fine_validita IS NULL
      AND vb.data_validita =
      (
          SELECT MAX(vb2.data_validita)
          FROM Importi_borsa_percepiti vb2
          WHERE vb2.anno_accademico = vb.anno_accademico
            AND vb2.num_domanda = vb.num_domanda
            AND vb2.data_fine_validita IS NULL
      )
),
AltreBorse AS
(
    SELECT
        num_domanda,
        SUM(TRY_CONVERT(decimal(18,2), ISNULL(importo_borsa, 0))) AS DetrazioniAltreBorse
    FROM AltreBorseValide
    GROUP BY num_domanda
),
Base AS
(
    SELECT
        t.Anno_accademico,
        t.Num_domanda,
        t.Cod_fiscale,
        ISNULL(tp.TipoRedditoOrigine, '') AS TipoRedditoOrigine,
        ISNULL(tp.TipoRedditoIntegrazione, '') AS TipoRedditoIntegrazione,
        TRY_CONVERT(decimal(18,2), ISNULL(tp.AltriMezzi, 0)) AS AltriMezzi,
        CASE WHEN ISNULL(n.NumeroComponenti, 0) > 0 THEN n.NumeroComponenti ELSE 1 END AS NumeroComponenti,
        CASE WHEN ISNULL(n.NumeroConviventiEstero, 0) > 0 THEN n.NumeroConviventiEstero ELSE 0 END AS NumeroConviventiEstero,
        ISNULL(i.ConfermaSemestreFiltro, 0) AS ConfermaSemestreFiltro,
        ISNULL(p.FranchigiaImmobiliare, 0) AS FranchigiaImmobiliare,
        ISNULL(p.TassoRendimentoPatrimonio, 0) AS TassoRendimentoPatrimonio,
        ISNULL(p.FranchigiaPatrimonioMobiliare, 0) AS FranchigiaPatrimonioMobiliare,
        p.SogliaIsee,
        CASE
            WHEN ISNULL(tp.TipoRedditoOrigine, '') = 'IT'
            THEN TRY_CONVERT(decimal(18,2), ISNULL(pd.DetrazioniAdisu, 0))
            ELSE 0
        END AS DetrazioniAdisu,
        CASE
            WHEN ISNULL(tp.TipoRedditoOrigine, '') = 'IT'
            THEN TRY_CONVERT(decimal(18,2), ISNULL(ab.DetrazioniAltreBorse, 0))
            ELSE 0
        END AS DetrazioniAltreBorse
    FROM Target t
    LEFT JOIN Tipologie tp ON tp.Num_domanda = t.Num_domanda
    LEFT JOIN Nucleo n ON n.Num_domanda = t.Num_domanda
    LEFT JOIN Iscrizione i ON i.Num_domanda = t.Num_domanda
    LEFT JOIN PagamentiDetraibili pd ON pd.Cod_fiscale = t.Cod_fiscale
    LEFT JOIN AltreBorse ab ON ab.num_domanda = t.Num_domanda
    CROSS JOIN Parametri p
),
Fonti AS
(
    SELECT
        b.*,
        ISNULL(cert.HasIseeBaseEntroScadenza, 0) AS HasIseeBaseEntroScadenza,
        co.Num_domanda AS CoTrovata,
        co.Cod_tipo_attestazione AS CoTipoAttestazione,
        co.firmata_il AS CoFirmataIl,
        co.data_validita AS CoDataValidita,
        ISNULL(co.Somma_redditi, 0) AS CoSommaRedditi,
        ISNULL(co.ISR, 0) AS CoIsr,
        ISNULL(co.ISP, 0) AS CoIsp,
        ISNULL(co.Scala_equivalenza, 0) AS CoScala,
        ISNULL(co.Redd_fratelli_50, 0) AS CoReddFratelli50,
        ISNULL(co.Patr_fratelli_50, 0) AS CoPatrFratelli50,
        ISNULL(co.Patr_frat_50_est, 0) AS CoPatrFratelli50Estero,
        ISNULL(co.Redd_frat_50_est, 0) AS CoReddFratelli50Estero,
        ISNULL(co.Patr_fam_50_est, 0) AS CoPatrFamiglia50Estero,
        ISNULL(co.Metri_quadri, 0) AS CoMetriQuadri,
        ISNULL(co.Redd_fam_50_est, 0) AS CoReddFamiglia50Estero,
        ISNULL(co.patr_imm_50_frat_sor, 0) AS CoPatrimonioImmobiliareFratelli50,

        ci.Num_domanda AS CiTrovata,
        ci.Cod_tipo_attestazione AS CiTipoAttestazione,
        ci.firmata_il AS CiFirmataIl,
        ci.data_validita AS CiDataValidita,
        ISNULL(ci.ISR, 0) AS CiIsr,
        ISNULL(ci.ISP, 0) AS CiIsp,
        ISNULL(ci.Scala_equivalenza, 0) AS CiScala,
        ISNULL(ci.numero_componenti_attestazione, 0) AS CiNumeroComponenti,
        ISNULL(ci.Redd_fratelli_50, 0) AS CiReddFratelli50,
        ISNULL(ci.Patr_fratelli_50, 0) AS CiPatrFratelli50,
        ISNULL(ci.Patr_frat_50_est, 0) AS CiPatrFratelli50Estero,
        ISNULL(ci.Redd_frat_50_est, 0) AS CiReddFratelli50Estero,
        ISNULL(ci.Patr_fam_50_est, 0) AS CiPatrFamiglia50Estero,
        ISNULL(ci.Metri_quadri, 0) AS CiMetriQuadri,
        ISNULL(ci.Redd_fam_50_est, 0) AS CiReddFamiglia50Estero,

        origineEe.Num_domanda AS OrigineEeTrovata,
        ISNULL(origineEe.Numero_componenti, 0) AS OrigineEeNumeroComponenti,
        ISNULL(origineEe.Redd_complessivo, 0) AS OrigineEeRedditoComplessivo,
        ISNULL(origineEe.Patr_mobiliare, 0) AS OrigineEePatrimonioMobiliare,
        ISNULL(origineEe.Superf_abitaz_MQ, 0) AS OrigineEeSuperficieAbitazione,
        ISNULL(origineEe.Sup_compl_altre_MQ, 0) AS OrigineEeSuperficieAltriImmobili,
        ISNULL(origineEe.Sup_compl_MQ, 0) AS OrigineEeSuperficieFratelli50,
        ISNULL(origineEe.Redd_lordo_fratell, 0) AS OrigineEeRedditoFratelli,
        ISNULL(origineEe.Patr_mob_fratell, 0) AS OrigineEePatrimonioFratelli,

        integrazioneEe.Num_domanda AS IntegrazioneEeTrovata,
        ISNULL(integrazioneEe.Numero_componenti, 0) AS IntegrazioneEeNumeroComponenti,
        ISNULL(integrazioneEe.Redd_complessivo, 0) AS IntegrazioneEeRedditoComplessivo,
        ISNULL(integrazioneEe.Patr_mobiliare, 0) AS IntegrazioneEePatrimonioMobiliare,
        ISNULL(integrazioneEe.Superf_abitaz_MQ, 0) AS IntegrazioneEeSuperficieAbitazione,
        ISNULL(integrazioneEe.Sup_compl_altre_MQ, 0) AS IntegrazioneEeSuperficieAltriImmobili,
        ISNULL(integrazioneEe.Sup_compl_MQ, 0) AS IntegrazioneEeSuperficieFratelli50,
        ISNULL(integrazioneEe.Redd_lordo_fratell, 0) AS IntegrazioneEeRedditoFratelli,
        ISNULL(integrazioneEe.Patr_mob_fratell, 0) AS IntegrazioneEePatrimonioFratelli
    FROM Base b
    OUTER APPLY
    (
        SELECT
            MAX
            (
                CASE
                    WHEN UPPER(ISNULL(c.tipologia_certificazione, '')) IN ('CO','DO')
                     AND c.firmata_il IS NOT NULL
                     AND
                     (
                         UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%ORD%'
                         OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%UNIV%'
                         OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%RID%'
                         OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%CORRENTE%'
                     )
                    THEN 1 ELSE 0
                END
            ) AS HasIseeBaseEntroScadenza
        FROM Certificaz_ISEE c
        WHERE c.Anno_accademico = @AnnoAccademico
          AND c.Num_domanda = b.Num_domanda
          AND c.firmata_il <= CASE
                                 WHEN b.ConfermaSemestreFiltro = 1 THEN @FirmataIlMax
                                 ELSE @ScadenzaIseeBase
                             END
    ) cert
    OUTER APPLY
    (
        SELECT TOP (1) c.*
        FROM Certificaz_ISEE c
        WHERE c.Anno_accademico = @AnnoAccademico
          AND c.Num_domanda = b.Num_domanda
          AND UPPER(ISNULL(c.tipologia_certificazione, '')) = 'CO'
          AND c.firmata_il IS NOT NULL
          AND c.firmata_il <= @FirmataIlMax
          AND
          (
              UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%UNIV%'
              OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%RID%'
              OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%CORRENTE%'
              OR
              (
                  UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%ORD%'
                  AND
                  (
                      b.TipoRedditoIntegrazione = 'EE'
                      OR b.ConfermaSemestreFiltro = 1
                      OR @ConsentiOrdinarioInAttesa = 1
                  )
              )
          )
        ORDER BY
            CASE
                WHEN UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%UNIV%'
                  OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%RID%'
                  OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%CORRENTE%'
                THEN 0 ELSE 1
            END,
            c.firmata_il DESC,
            c.data_validita DESC
    ) co
    OUTER APPLY
    (
        SELECT TOP (1) c.*
        FROM Certificaz_ISEE c
        WHERE c.Anno_accademico = @AnnoAccademico
          AND c.Num_domanda = b.Num_domanda
          AND UPPER(ISNULL(c.tipologia_certificazione, '')) = 'CI'
          AND c.firmata_il IS NOT NULL
          AND c.firmata_il <= @FirmataIlMax
          AND
          (
              UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%UNIV%'
              OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%RID%'
              OR UPPER(ISNULL(c.Cod_tipo_attestazione, '')) LIKE '%CORRENTE%'
          )
        ORDER BY c.firmata_il DESC, c.data_validita DESC
    ) ci
    OUTER APPLY
    (
        SELECT TOP (1) nf.*
        FROM Nucleo_fam_stranieri nf
        WHERE nf.Anno_accademico = @AnnoAccademico
          AND nf.Num_domanda = b.Num_domanda
          AND nf.Tipologia_redditi = 'DO'
        ORDER BY nf.data_validita DESC
    ) origineEe
    OUTER APPLY
    (
        SELECT TOP (1) nf.*
        FROM Nucleo_fam_stranieri nf
        WHERE nf.Anno_accademico = @AnnoAccademico
          AND nf.Num_domanda = b.Num_domanda
          AND nf.Tipologia_redditi = 'DI'
        ORDER BY nf.data_validita DESC
    ) integrazioneEe
),
TerminiPatrimoniali AS
(
    SELECT
        f.*,
        CASE
            WHEN f.TipoRedditoOrigine = 'EE'
            THEN
                CASE
                    WHEN f.OrigineEePatrimonioMobiliare
                         + f.OrigineEePatrimonioFratelli * 0.5
                         - f.FranchigiaPatrimonioMobiliare > 0
                    THEN f.OrigineEePatrimonioMobiliare
                         + f.OrigineEePatrimonioFratelli * 0.5
                         - f.FranchigiaPatrimonioMobiliare
                    ELSE 0
                END
            ELSE 0
        END AS OrigineEePatrimonioMobiliareNetto,
        CASE
            WHEN f.TipoRedditoIntegrazione = 'EE'
            THEN
                CASE
                    WHEN f.IntegrazioneEePatrimonioMobiliare
                         + f.IntegrazioneEePatrimonioFratelli * 0.5
                         - f.FranchigiaPatrimonioMobiliare > 0
                    THEN f.IntegrazioneEePatrimonioMobiliare
                         + f.IntegrazioneEePatrimonioFratelli * 0.5
                         - f.FranchigiaPatrimonioMobiliare
                    ELSE 0
                END
            ELSE 0
        END AS IntegrazioneEePatrimonioMobiliareNetto
    FROM Fonti f
),
Indicatori AS
(
    SELECT
        p.*,
        CASE
            WHEN p.TipoRedditoOrigine = 'IT'
             AND p.HasIseeBaseEntroScadenza = 1
             AND p.CoTrovata IS NOT NULL
            THEN 1
            WHEN p.TipoRedditoOrigine = 'EE'
             AND p.OrigineEeTrovata IS NOT NULL
            THEN 1
            ELSE 0
        END AS CalcoloApplicabile,
        CASE
            WHEN p.TipoRedditoOrigine = 'IT'
             AND p.HasIseeBaseEntroScadenza = 1
             AND p.CoTrovata IS NOT NULL
            THEN p.CoIsr
                 - p.CoReddFratelli50
                 + p.CoReddFratelli50Estero
                 + p.CoReddFamiglia50Estero
                 + p.AltriMezzi
                 + (p.CoPatrFratelli50Estero - p.CoPatrFratelli50 + p.CoPatrFamiglia50Estero)
                   * p.TassoRendimentoPatrimonio
            WHEN p.TipoRedditoOrigine = 'EE' AND p.OrigineEeTrovata IS NOT NULL
            THEN p.OrigineEeRedditoComplessivo
                 + p.OrigineEeRedditoFratelli * 0.5
                 + p.OrigineEePatrimonioMobiliareNetto * p.TassoRendimentoPatrimonio
                 + p.AltriMezzi
        END AS IsrOrigine,
        CASE
            WHEN p.TipoRedditoOrigine = 'IT'
             AND p.HasIseeBaseEntroScadenza = 1
             AND p.CoTrovata IS NOT NULL
            THEN p.CoIsp
                 - p.CoPatrimonioImmobiliareFratelli50
                 + p.CoMetriQuadri * 500
            WHEN p.TipoRedditoOrigine = 'EE' AND p.OrigineEeTrovata IS NOT NULL
            THEN
                CASE
                    WHEN
                        (
                            p.OrigineEeSuperficieAbitazione
                            + p.OrigineEeSuperficieAltriImmobili
                            + p.OrigineEeSuperficieFratelli50 * 0.5
                        ) * 500 - p.FranchigiaImmobiliare > 0
                    THEN
                        (
                            p.OrigineEeSuperficieAbitazione
                            + p.OrigineEeSuperficieAltriImmobili
                            + p.OrigineEeSuperficieFratelli50 * 0.5
                        ) * 500 - p.FranchigiaImmobiliare
                    ELSE 0
                END + p.OrigineEePatrimonioMobiliareNetto
        END AS IspOrigine,
        CASE
            WHEN p.TipoRedditoIntegrazione = 'IT' AND p.CiTrovata IS NOT NULL
            THEN p.CiIsr
                 - p.CiReddFratelli50
                 + p.CiReddFratelli50Estero
                 + p.CiReddFamiglia50Estero
                 - (p.CiPatrFratelli50Estero - p.CiPatrFratelli50 + p.CiPatrFamiglia50Estero)
                   * p.TassoRendimentoPatrimonio
            WHEN p.TipoRedditoIntegrazione = 'EE' AND p.IntegrazioneEeTrovata IS NOT NULL
            THEN p.IntegrazioneEeRedditoComplessivo
                 + p.IntegrazioneEeRedditoFratelli * 0.5
                 + p.IntegrazioneEePatrimonioMobiliareNetto * p.TassoRendimentoPatrimonio
            ELSE 0
        END AS IsrIntegrazione,
        CASE
            WHEN p.TipoRedditoIntegrazione = 'IT' AND p.CiTrovata IS NOT NULL
            THEN p.CiIsp + p.CiMetriQuadri * 500
            WHEN p.TipoRedditoIntegrazione = 'EE' AND p.IntegrazioneEeTrovata IS NOT NULL
            THEN
                CASE
                    WHEN
                        (
                            p.IntegrazioneEeSuperficieAbitazione
                            + p.IntegrazioneEeSuperficieAltriImmobili
                            + p.IntegrazioneEeSuperficieFratelli50 * 0.5
                        ) * 500 - p.FranchigiaImmobiliare > 0
                    THEN
                        (
                            p.IntegrazioneEeSuperficieAbitazione
                            + p.IntegrazioneEeSuperficieAltriImmobili
                            + p.IntegrazioneEeSuperficieFratelli50 * 0.5
                        ) * 500 - p.FranchigiaImmobiliare
                    ELSE 0
                END + p.IntegrazioneEePatrimonioMobiliareNetto
            ELSE 0
        END AS IspIntegrazione,
        CASE
            WHEN p.TipoRedditoOrigine = 'IT' THEN p.CoScala
            WHEN p.TipoRedditoOrigine = 'EE'
            THEN
                CASE p.OrigineEeNumeroComponenti
                    WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
                    WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
                    ELSE 2.85 + (CASE WHEN p.OrigineEeNumeroComponenti > 5 THEN p.OrigineEeNumeroComponenti - 5 ELSE 0 END) * 0.35
                END
            ELSE 0
        END AS ScalaOrigine,
        CASE
            WHEN p.TipoRedditoIntegrazione = 'IT' AND p.CiTrovata IS NOT NULL THEN p.CiScala
            WHEN p.TipoRedditoIntegrazione = 'EE' AND p.IntegrazioneEeTrovata IS NOT NULL
            THEN
                CASE p.IntegrazioneEeNumeroComponenti
                    WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
                    WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
                    ELSE 2.85 + (CASE WHEN p.IntegrazioneEeNumeroComponenti > 5 THEN p.IntegrazioneEeNumeroComponenti - 5 ELSE 0 END) * 0.35
                END
            ELSE 0
        END AS ScalaIntegrazione,
        CASE
            WHEN p.TipoRedditoIntegrazione = 'IT' AND p.CiTrovata IS NOT NULL THEN TRY_CONVERT(int, p.CiNumeroComponenti)
            WHEN p.TipoRedditoIntegrazione = 'EE' AND p.IntegrazioneEeTrovata IS NOT NULL THEN TRY_CONVERT(int, p.IntegrazioneEeNumeroComponenti)
            ELSE 0
        END AS NumeroComponentiIntegrazione
    FROM TerminiPatrimoniali p
),
ComponentiScala AS
(
    SELECT
        i.*,
        CASE
            WHEN i.TipoRedditoOrigine = 'IT'
            THEN CASE
                    WHEN i.NumeroComponenti - i.NumeroConviventiEstero > 0
                    THEN i.NumeroComponenti - i.NumeroConviventiEstero
                    ELSE 1
                 END
            ELSE i.NumeroComponenti
        END AS ComponentiBaseStudente
    FROM Indicatori i
),
MaggiorazioniScala AS
(
    SELECT
        c.*,
        CASE c.ComponentiBaseStudente
            WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
            WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
            ELSE 2.85 + (CASE WHEN c.ComponentiBaseStudente > 5 THEN c.ComponentiBaseStudente - 5 ELSE 0 END) * 0.35
        END AS ScalaMinimaComponentiBase,
        CASE c.NumeroComponentiIntegrazione
            WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
            WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
            ELSE CASE
                    WHEN c.NumeroComponentiIntegrazione > 5
                    THEN 2.85 + (c.NumeroComponentiIntegrazione - 5) * 0.35
                    ELSE 1.00
                 END
        END AS ScalaMinimaComponentiIntegrazione,
        c.ComponentiBaseStudente + c.NumeroConviventiEstero AS ComponentiStudente
    FROM ComponentiScala c
),
ScalaFinalePreparazione AS
(
    SELECT
        m.*,
        CASE
            WHEN m.TipoRedditoOrigine = 'IT'
            THEN CASE WHEN m.ScalaOrigine > 0 THEN m.ScalaOrigine ELSE 0 END
                 - m.ScalaMinimaComponentiBase
            ELSE 0
        END AS MaggiorazioneStudente,
        CASE
            WHEN m.TipoRedditoIntegrazione = 'IT' AND m.NumeroComponentiIntegrazione > 0
            THEN CASE WHEN m.ScalaIntegrazione > 0 THEN m.ScalaIntegrazione ELSE 0 END
                 - m.ScalaMinimaComponentiIntegrazione
            ELSE 0
        END AS MaggiorazioneIntegrazione,
        m.ComponentiStudente + m.NumeroComponentiIntegrazione AS ComponentiTotaliScala
    FROM MaggiorazioniScala m
),
ScalaNonArrotondata AS
(
    SELECT
        s.*,
        CASE
            WHEN s.NumeroComponentiIntegrazione <= 0
            THEN
                CASE s.ComponentiStudente
                    WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
                    WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
                    ELSE 2.85 + (CASE WHEN s.ComponentiStudente > 5 THEN s.ComponentiStudente - 5 ELSE 0 END) * 0.35
                END + s.MaggiorazioneStudente
            ELSE
                CASE s.ComponentiTotaliScala
                    WHEN 1 THEN 1.00 WHEN 2 THEN 1.57 WHEN 3 THEN 2.04
                    WHEN 4 THEN 2.46 WHEN 5 THEN 2.85
                    ELSE 2.85 + (CASE WHEN s.ComponentiTotaliScala > 5 THEN s.ComponentiTotaliScala - 5 ELSE 0 END) * 0.35
                END + s.MaggiorazioneStudente + s.MaggiorazioneIntegrazione
        END AS ScalaEquivalenzaNonArrotondata
    FROM ScalaFinalePreparazione s
),
ScalaFinale AS
(
    SELECT
        s.*,
        ROUND
        (
            CASE WHEN s.ScalaEquivalenzaNonArrotondata > 0
                 THEN s.ScalaEquivalenzaNonArrotondata
                 ELSE 1
            END,
            2
        ) AS ScalaEquivalenzaFinale
    FROM ScalaNonArrotondata s
),
Calcoli AS
(
    SELECT
        s.*,
        ISNULL(s.IsrOrigine, 0) + ISNULL(s.IsrIntegrazione, 0) AS IsrLordoDsu,
        ISNULL(s.IspOrigine, 0) + ISNULL(s.IspIntegrazione, 0) AS IspDsu,
        s.DetrazioniAdisu + s.DetrazioniAltreBorse AS DetrazioniTotali,
        CASE
            WHEN ISNULL(s.IsrOrigine, 0) + ISNULL(s.IsrIntegrazione, 0)
                 - s.DetrazioniAdisu - s.DetrazioniAltreBorse > 0
            THEN ISNULL(s.IsrOrigine, 0) + ISNULL(s.IsrIntegrazione, 0)
                 - s.DetrazioniAdisu - s.DetrazioniAltreBorse
            ELSE 0
        END AS IsrNettoDsu
    FROM ScalaFinale s
),
Risultati AS
(
    SELECT
        c.*,
        CASE WHEN c.CalcoloApplicabile = 1
             THEN ROUND(c.IsrNettoDsu + 0.20 * c.IspDsu, 2)
        END AS IseDsuCalcolato,
        CASE WHEN c.CalcoloApplicabile = 1
             THEN ROUND
                  (
                      (c.IsrNettoDsu + 0.20 * c.IspDsu)
                      / NULLIF(CASE WHEN c.ScalaEquivalenzaFinale > 0 THEN c.ScalaEquivalenzaFinale ELSE 1 END, 0),
                      2
                  )
        END AS IseeDsuCalcolato,
        CASE WHEN c.CalcoloApplicabile = 1
             THEN ROUND
                  (
                      c.IspDsu
                      / NULLIF(CASE WHEN c.ScalaEquivalenzaFinale > 0 THEN c.ScalaEquivalenzaFinale ELSE 1 END, 0),
                      2
                  )
        END AS IspeDsuCalcolato
    FROM Calcoli c
),
ValoriAttualiRanked AS
(
    SELECT
        v.Num_domanda,
        v.ISPDSU,
        v.ISEDSU,
        v.ISEEDSU,
        v.ISPEDSU,
        v.SEQ,
        v.data_validita,
        ROW_NUMBER() OVER
        (
            PARTITION BY v.Num_domanda
            ORDER BY v.data_validita DESC
        ) AS rn
    FROM Valori_calcolati v
    INNER JOIN Target t ON t.Num_domanda = v.Num_domanda
    WHERE v.Anno_accademico = @AnnoAccademico
)
SELECT
    r.Anno_accademico,
    r.Num_domanda,
    r.Cod_fiscale,

    /* 1. Fonti e parametri */
    r.TipoRedditoOrigine,
    CASE r.TipoRedditoOrigine WHEN 'IT' THEN 'CO' WHEN 'EE' THEN 'DO' ELSE 'NON CALCOLABILE' END AS FonteOrigineUsata,
    r.HasIseeBaseEntroScadenza,
    r.CalcoloApplicabile,
    r.TipoRedditoIntegrazione,
    CASE r.TipoRedditoIntegrazione WHEN 'IT' THEN 'CI' WHEN 'EE' THEN 'DI' ELSE 'NESSUNA' END AS FonteIntegrazioneUsata,
    r.FranchigiaImmobiliare,
    r.FranchigiaPatrimonioMobiliare,
    r.TassoRendimentoPatrimonio,
    r.SogliaIsee,

    /* 2. Attestazioni effettivamente selezionate */
    r.CoTipoAttestazione,
    r.CoFirmataIl,
    r.CoDataValidita,
    r.CiTipoAttestazione,
    r.CiFirmataIl,
    r.CiDataValidita,

    /* 3. Formule applicate */
    CASE r.TipoRedditoOrigine
        WHEN 'IT' THEN 'CO.ISR - redditi fratelli 50% + redditi esteri + altri mezzi + rettifica patrimonio * tasso'
        WHEN 'EE' THEN 'reddito complessivo + 50% reddito fratelli + patrimonio mobiliare netto * tasso + altri mezzi'
    END AS FormulaIsrOrigine,
    CASE r.TipoRedditoOrigine
        WHEN 'IT' THEN 'CO.ISP - patrimonio immobiliare fratelli 50% + metri quadri * 500'
        WHEN 'EE' THEN 'MAX(superfici * 500 - franchigia immobiliare, 0) + patrimonio mobiliare netto'
    END AS FormulaIspOrigine,
    CASE r.TipoRedditoIntegrazione
        WHEN 'IT' THEN 'CI.ISR - redditi fratelli 50% + redditi esteri - rettifica patrimonio * tasso'
        WHEN 'EE' THEN 'reddito complessivo + 50% reddito fratelli + patrimonio mobiliare netto * tasso'
        ELSE 'nessuna integrazione'
    END AS FormulaIsrIntegrazione,
    'ISEE DSU = (MAX(ISR origine + ISR integrazione - detrazioni, 0) + 20% ISP DSU) / scala finale' AS FormulaIseeFinale,

    /* 4. Principali valori sorgente */
    r.AltriMezzi,
    r.CoIsr,
    r.CoIsp,
    r.CoScala,
    r.CoReddFratelli50,
    r.CoReddFratelli50Estero,
    r.CoReddFamiglia50Estero,
    r.CoPatrFratelli50,
    r.CoPatrFratelli50Estero,
    r.CoPatrFamiglia50Estero,
    r.CoMetriQuadri,
    r.CoPatrimonioImmobiliareFratelli50,
    r.OrigineEeRedditoComplessivo,
    r.OrigineEeRedditoFratelli,
    r.OrigineEePatrimonioMobiliareNetto,
    r.CiIsr,
    r.CiIsp,
    r.CiScala,
    r.IntegrazioneEeRedditoComplessivo,
    r.IntegrazioneEeRedditoFratelli,
    r.IntegrazioneEePatrimonioMobiliareNetto,

    /* 5. ISR */
    r.IsrOrigine,
    r.IsrIntegrazione,
    r.IsrLordoDsu,
    r.DetrazioniAdisu,
    r.DetrazioniAltreBorse,
    r.DetrazioniTotali,
    r.IsrNettoDsu,

    /* 6. ISP */
    r.IspOrigine,
    r.IspIntegrazione,
    r.IspDsu,

    /* 7. Scala di equivalenza */
    r.NumeroComponenti,
    r.NumeroConviventiEstero,
    r.NumeroComponentiIntegrazione,
    r.ScalaOrigine,
    r.ScalaIntegrazione,
    r.MaggiorazioneStudente,
    r.MaggiorazioneIntegrazione,
    r.ScalaEquivalenzaFinale,

    /* 8. Risultati ricalcolati */
    r.IseDsuCalcolato,
    r.IseeDsuCalcolato,
    r.IspeDsuCalcolato,
    CASE
        WHEN r.SogliaIsee > 0 AND r.IseeDsuCalcolato > r.SogliaIsee
        THEN 'SUPERATA'
        ELSE 'NON SUPERATA'
    END AS EsitoSogliaIsee,

    /* 9. Confronto con l'ultima riga già presente in Valori_calcolati */
    va.ISPDSU AS IspDsuSalvato,
    va.ISEDSU AS IseDsuSalvato,
    va.ISEEDSU AS IseeDsuSalvato,
    va.ISPEDSU AS IspeDsuSalvato,
    va.SEQ AS ScalaSalvata,
    r.IseeDsuCalcolato - TRY_CONVERT(decimal(18,2), va.ISEEDSU) AS DifferenzaIsee,
    va.data_validita AS DataValoriSalvati
FROM Risultati r
LEFT JOIN ValoriAttualiRanked va
    ON va.Num_domanda = r.Num_domanda
   AND va.rn = 1
ORDER BY r.Num_domanda;
