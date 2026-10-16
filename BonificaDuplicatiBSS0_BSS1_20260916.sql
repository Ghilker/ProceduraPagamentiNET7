-- Bonifica delle sole 6 coppie BSS0/BSS1 verificate il 24/09/2026.
-- Conserva la copia con ID minore. Non modifica BSI9.
USE ADISU;
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Applica bit = 0; -- 0 = anteprima; 1 = elimina e conferma

IF @@TRANCOUNT <> 0
    THROW 50000, 'Eseguire senza altre transazioni aperte.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    SELECT e.ID_MOVIMENTO AS IdElementareDaEliminare,
           g.CODICE_MOVIMENTO AS DaEliminare,
           r.CODICE_MOVIMENTO AS DaConservare,
           e.CODICE_FISCALE, e.IMPORTO, g.COD_MANDATO
    INTO #BonificaBSS
    FROM (VALUES
        (4092971, 4092944, 'BSS0', '10'),
        (4092972, 4092945, 'BSS0', '10'),
        (4092973, 4092946, 'BSS0', '10'),
        (4092974, 4092947, 'BSS0', '10'),
        (4092982, 4092977, 'BSS1', 'R10'),
        (4092983, 4092978, 'BSS1', 'R10')
    ) p(Elimina, Conserva, TipoPagamento, Causale)
    JOIN dbo.MOVIMENTI_CONTABILI_GENERALI g WITH (UPDLOCK, HOLDLOCK)
      ON g.CODICE_MOVIMENTO = p.Elimina
    JOIN dbo.MOVIMENTI_CONTABILI_GENERALI r WITH (UPDLOCK, HOLDLOCK)
      ON r.CODICE_MOVIMENTO = p.Conserva
    JOIN dbo.MOVIMENTI_CONTABILI_ELEMENTARI e WITH (UPDLOCK, HOLDLOCK)
      ON e.CODICE_MOVIMENTO = g.CODICE_MOVIMENTO
    JOIN dbo.MOVIMENTI_CONTABILI_ELEMENTARI er WITH (UPDLOCK, HOLDLOCK)
      ON er.CODICE_MOVIMENTO = r.CODICE_MOVIMENTO
    WHERE g.COD_MANDATO = p.TipoPagamento + '_200926_2526_1367_00'
      AND r.COD_MANDATO = g.COD_MANDATO
      AND g.ID_CAUSALE_MOVIMENTO_GENERALE = p.TipoPagamento
      AND r.ID_CAUSALE_MOVIMENTO_GENERALE = p.TipoPagamento
      AND g.DATA_VALIDITA_MOVIMENTO_GENERALE = CONVERT(date, '20260916', 112)
      AND r.DATA_VALIDITA_MOVIMENTO_GENERALE = g.DATA_VALIDITA_MOVIMENTO_GENERALE
      AND r.IMPORTO_MOVIMENTO = g.IMPORTO_MOVIMENTO
      AND e.ANNO_ACCADEMICO = '20252026'
      AND er.ANNO_ACCADEMICO = e.ANNO_ACCADEMICO
      AND e.ID_CAUSALE = p.Causale AND er.ID_CAUSALE = e.ID_CAUSALE
      AND e.SEGNO = '1' AND er.SEGNO = '1'
      AND e.STATO = '2' AND er.STATO = '2'
      AND er.CODICE_FISCALE = e.CODICE_FISCALE
      AND er.IMPORTO = e.IMPORTO
      AND NOT EXISTS (
          SELECT 1 FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s WITH (UPDLOCK, HOLDLOCK)
          WHERE s.CODICE_MOVIMENTO IN (g.CODICE_MOVIMENTO, r.CODICE_MOVIMENTO)
            AND (s.ID_STATO IS NULL OR s.ID_STATO <> '2'));

    IF (SELECT COUNT(*) FROM #BonificaBSS) <> 6
       OR (SELECT COUNT(DISTINCT DaEliminare) FROM #BonificaBSS) <> 6
       OR (SELECT COUNT(DISTINCT CODICE_FISCALE) FROM #BonificaBSS) <> 6
       OR (SELECT SUM(IMPORTO) FROM #BonificaBSS) <> 11954.24
        THROW 50001, 'Le sei coppie attese non coincidono. Nessuna cancellazione.', 1;

    -- Esattamente un elemento e uno stato per ciascuna copia, anche quella conservata.
    IF EXISTS (
        SELECT 1
        FROM #BonificaBSS b
        CROSS APPLY (VALUES (b.DaEliminare), (b.DaConservare)) m(Codice)
        WHERE (SELECT COUNT(*) FROM dbo.MOVIMENTI_CONTABILI_ELEMENTARI e WITH (UPDLOCK, HOLDLOCK)
               WHERE e.CODICE_MOVIMENTO = m.Codice) <> 1
           OR (SELECT COUNT(*) FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s WITH (UPDLOCK, HOLDLOCK)
               WHERE s.CODICE_MOVIMENTO = m.Codice) <> 1)
        THROW 50002, 'Elementi o stati collegati diversi da quelli verificati.', 1;

    SELECT * FROM #BonificaBSS ORDER BY DaEliminare;

    IF @Applica = 1
    BEGIN
        DELETE e FROM dbo.MOVIMENTI_CONTABILI_ELEMENTARI e
        JOIN #BonificaBSS b ON b.IdElementareDaEliminare = e.ID_MOVIMENTO;
        IF @@ROWCOUNT <> 6 THROW 50003, 'Numero elementi eliminati inatteso.', 1;

        DELETE s FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s
        JOIN #BonificaBSS b ON b.DaEliminare = s.CODICE_MOVIMENTO;
        IF @@ROWCOUNT <> 6 THROW 50004, 'Numero stati eliminati inatteso.', 1;

        DELETE g FROM dbo.MOVIMENTI_CONTABILI_GENERALI g
        JOIN #BonificaBSS b ON b.DaEliminare = g.CODICE_MOVIMENTO;
        IF @@ROWCOUNT <> 6 THROW 50005, 'Numero movimenti eliminati inatteso.', 1;

        DROP TABLE #BonificaBSS;
        COMMIT;
        PRINT 'Eliminate 4 copie BSS0 e 2 copie BSS1. Conservato un pagamento per coppia.';
    END
    ELSE
    BEGIN
        ROLLBACK;
        PRINT 'Sola anteprima: nessun dato modificato. Per applicare impostare @Applica = 1.';
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;

