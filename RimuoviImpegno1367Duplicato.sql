-- Elimina UNA SOLA delle due righe identiche, senza usare la griglia SSMS.
USE ADISU;
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Applica bit = 0; -- 0 = anteprima; 1 = elimina una copia

IF @@TRANCOUNT <> 0
    THROW 50000, 'Eseguire senza altre transazioni aperte.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    SELECT *
    FROM dbo.impegni WITH (UPDLOCK, HOLDLOCK)
    WHERE anno_accademico = '20252026'
      AND num_impegno = 1367
      AND categoria_pagamento = 'SA'
      AND cod_beneficio = 'BS'
      AND descr = 'FSE - Saldo'
      AND id_coppia_impegni IS NULL
      AND anno_esercizio_finanziario = '2025'
      AND Tipo_fondo = 'FSE';

    IF @@ROWCOUNT <> 2
        THROW 50001, 'Attese esattamente due righe identiche: nessuna cancellazione.', 1;

    IF @Applica = 1
    BEGIN
        DELETE TOP (1) FROM dbo.impegni
        WHERE anno_accademico = '20252026'
          AND num_impegno = 1367
          AND categoria_pagamento = 'SA'
          AND cod_beneficio = 'BS'
          AND descr = 'FSE - Saldo'
          AND id_coppia_impegni IS NULL
          AND anno_esercizio_finanziario = '2025'
          AND Tipo_fondo = 'FSE';
        IF @@ROWCOUNT <> 1 THROW 50002, 'Numero righe eliminate inatteso.', 1;
        COMMIT;
        PRINT 'Eliminata una copia dell''impegno; l''altra rimane.';
    END
    ELSE
    BEGIN
        ROLLBACK;
        PRINT 'Sola anteprima. Impostare @Applica = 1 per eliminare una copia.';
    END;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;

