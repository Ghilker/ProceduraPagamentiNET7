-- Bonifica circoscritta alle 28 coppie BSI9 verificate il 24/09/2026.
-- @Applica = 0: sola anteprima; @Applica = 1: cancellazione e COMMIT.
-- Non riguarda BSS0/BSS1: entrambe le copie sono prive di ritorno.
USE ADISU;
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Applica bit = 0;

IF @@TRANCOUNT <> 0
    THROW 50000, 'Eseguire senza altre transazioni aperte.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    SELECT e.ID_MOVIMENTO AS IdElementareDaEliminare,
           g.CODICE_MOVIMENTO AS MovimentoDaEliminare,
           r.CODICE_MOVIMENTO AS MovimentoDaConservare,
           e.CODICE_FISCALE, e.IMPORTO,
           g.COD_MANDATO AS MandatoProvvisorio,
           r.COD_MANDATO AS MandatoConRitorno
    INTO #BonificaBSI9
    FROM dbo.MOVIMENTI_CONTABILI_GENERALI g WITH (UPDLOCK, HOLDLOCK)
    JOIN dbo.MOVIMENTI_CONTABILI_ELEMENTARI e WITH (UPDLOCK, HOLDLOCK)
      ON e.CODICE_MOVIMENTO = g.CODICE_MOVIMENTO
    JOIN dbo.MOVIMENTI_CONTABILI_GENERALI r WITH (UPDLOCK, HOLDLOCK)
      ON r.CODICE_MOVIMENTO = g.CODICE_MOVIMENTO + 29
    JOIN dbo.MOVIMENTI_CONTABILI_ELEMENTARI er WITH (UPDLOCK, HOLDLOCK)
      ON er.CODICE_MOVIMENTO = r.CODICE_MOVIMENTO
    WHERE g.CODICE_MOVIMENTO BETWEEN 4092992 AND 4093019
      AND g.COD_MANDATO = 'BSI9_200926_2526_1367_00'
      AND g.ID_CAUSALE_MOVIMENTO_GENERALE = 'BSI9'
      AND g.DATA_VALIDITA_MOVIMENTO_GENERALE = CONVERT(date, '20260916', 112)
      AND e.ANNO_ACCADEMICO = '20252026'
      AND e.ID_CAUSALE = '11' AND e.SEGNO = '1' AND e.STATO = '2'
      AND er.CODICE_FISCALE = e.CODICE_FISCALE
      AND er.ANNO_ACCADEMICO = e.ANNO_ACCADEMICO
      AND er.ID_CAUSALE = e.ID_CAUSALE
      AND er.SEGNO = e.SEGNO AND er.IMPORTO = e.IMPORTO AND er.STATO = '1'
      AND r.ID_CAUSALE_MOVIMENTO_GENERALE = g.ID_CAUSALE_MOVIMENTO_GENERALE
      AND r.IMPORTO_MOVIMENTO = g.IMPORTO_MOVIMENTO
      AND r.DATA_VALIDITA_MOVIMENTO_GENERALE = g.DATA_VALIDITA_MOVIMENTO_GENERALE
      AND TRY_CONVERT(int, r.COD_MANDATO) = 71204 + g.CODICE_MOVIMENTO - 4092992
      AND EXISTS (
          SELECT 1 FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s WITH (UPDLOCK, HOLDLOCK)
          WHERE s.CODICE_MOVIMENTO = r.CODICE_MOVIMENTO AND s.ID_STATO = '1')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s WITH (UPDLOCK, HOLDLOCK)
          WHERE s.CODICE_MOVIMENTO = g.CODICE_MOVIMENTO
            AND (s.ID_STATO IS NULL OR s.ID_STATO <> '2'));

    IF (SELECT COUNT(*) FROM #BonificaBSI9) <> 28
       OR (SELECT COUNT(DISTINCT MovimentoDaEliminare) FROM #BonificaBSI9) <> 28
       OR (SELECT COUNT(DISTINCT CODICE_FISCALE) FROM #BonificaBSI9) <> 28
       OR (SELECT SUM(IMPORTO) FROM #BonificaBSI9) <> 79793.16
        THROW 50001, 'Le 28 coppie attese non coincidono: nessuna cancellazione.', 1;

    -- Se compaiono ulteriori elementi o stati, richiedere una nuova verifica.
    IF (SELECT COUNT(*) FROM dbo.MOVIMENTI_CONTABILI_ELEMENTARI e WITH (UPDLOCK, HOLDLOCK)
        JOIN #BonificaBSI9 b ON b.MovimentoDaEliminare = e.CODICE_MOVIMENTO) <> 28
       OR (SELECT COUNT(*) FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s WITH (UPDLOCK, HOLDLOCK)
           JOIN #BonificaBSI9 b ON b.MovimentoDaEliminare = s.CODICE_MOVIMENTO) <> 28
        THROW 50002, 'Elementi o stati collegati diversi da quelli verificati.', 1;

    SELECT * FROM #BonificaBSI9 ORDER BY MovimentoDaEliminare;

    IF @Applica = 1
    BEGIN
        DELETE e
        FROM dbo.MOVIMENTI_CONTABILI_ELEMENTARI e
        JOIN #BonificaBSI9 b ON b.IdElementareDaEliminare = e.ID_MOVIMENTO;
        IF @@ROWCOUNT <> 28 THROW 50003, 'Numero elementi eliminati inatteso.', 1;

        DELETE s
        FROM dbo.STATI_DEL_MOVIMENTO_CONTABILE s
        JOIN #BonificaBSI9 b ON b.MovimentoDaEliminare = s.CODICE_MOVIMENTO;
        IF @@ROWCOUNT <> 28 THROW 50004, 'Numero stati eliminati inatteso.', 1;

        DELETE g
        FROM dbo.MOVIMENTI_CONTABILI_GENERALI g
        JOIN #BonificaBSI9 b ON b.MovimentoDaEliminare = g.CODICE_MOVIMENTO;
        IF @@ROWCOUNT <> 28 THROW 50005, 'Numero movimenti eliminati inatteso.', 1;

        DROP TABLE #BonificaBSI9;
        COMMIT;
        PRINT 'Eliminate 28 copie senza ritorno. Conservati i movimenti con ritorno.';
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

