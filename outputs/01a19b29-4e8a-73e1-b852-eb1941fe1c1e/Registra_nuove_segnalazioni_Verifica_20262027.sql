-- Verifica: nuove causali assegnate dopo lettura ADISU del 2026-11-02.
-- Script preparato e NON eseguito. Inserisce soltanto le 22 nuove decodifiche.
-- Nessuna modifica alle causali esistenti, agli esiti o alle segnalazioni degli studenti.
-- 225-246 assenti in tutto lo storico Decod_segnalazioni alla data della lettura.
USE [ADISU];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @AnnoAccademico char(8) = '20262027';
DECLARE @Utente varchar(20) = 'VerificaCatalogo';
DECLARE @DataValidita datetime = GETDATE();

IF @AnnoAccademico LIKE '%[^0-9]%'
   OR TRY_CONVERT(int, LEFT(@AnnoAccademico, 4)) < 2026
   OR TRY_CONVERT(int, RIGHT(@AnnoAccademico, 4)) <> TRY_CONVERT(int, LEFT(@AnnoAccademico, 4)) + 1
    THROW 51000, 'Anno accademico non valido: usare 20262027 o successivi.', 1;

DECLARE @Nuove TABLE (
    Codice char(5) NOT NULL PRIMARY KEY,
    Regola varchar(10) NOT NULL,
    Descrizione varchar(255) NOT NULL,
    Categoria char(1) NULL
);
INSERT INTO @Nuove (Codice, Regola, Descrizione, Categoria)
VALUES
    (N'225', N'GEN003', N'Domanda presentata oltre il termine previsto dal bando', N'4'),
    (N'226', N'ISC001', N'Anno di immatricolazione mancante o non valido', N'3'),
    (N'227', N'ISC008', N'Anno di corso non classificabile', N'3'),
    (N'228', N'RED011', N'Valore ISEE assente o non valido', N'2'),
    (N'229', N'RED086', N'Stato ISEE non ammesso', N'2'),
    (N'230', N'RED031', N'Attestazione ISEE origine non adeguata: manca CO universitaria/ridotta/corrente; se l''ultima importata è ordinaria, il termine di regolarizzazione è il 10 dicembre', N'2'),
    (N'231', N'RED033', N'Termine scaduto: integrazione ISEE universitaria/corrente del nucleo di origine non importata entro il 31 dicembre dell''anno di avvio', N'2'),
    (N'232', N'MER001', N'Dati di merito assenti o non sufficienti per il calcolo', N'3'),
    (N'233', N'MER005', N'Crediti dichiarati incongruenti con il corso di studi', N'3'),
    (N'234', N'MER086', N'Utilizzo del bonus non ammesso in presenza di crediti riconosciuti non derivanti da trasferimento TS o con ripetenza', N'3'),
    (N'235', N'MER087', N'Utilizzo del bonus non ammesso per carriera pregressa CD/AT con tipologia corso 9 o sede istituzione universitaria 2', N'3'),
    (N'236', N'MER090', N'Titolo di accesso in attesa di conseguimento non valido dopo il 10/02', N'3'),
    (N'237', N'MER091', N'Titolo di accesso mancante per immatricolazione alla magistrale/specialistica', N'3'),
    (N'238', N'MER093', N'Iscrizione magistrale biennale non ammissibile: presente titolo triennale già conseguito e ulteriore titolo di ciclo unico/magistrale biennale in attesa', N'3'),
    (N'239', N'MER170', N'Iscrizione non ammessa per Sapienza in vecchio ordinamento', N'3'),
    (N'240', N'PA002', N'Comune della sede di studi non presente tra i pensionati attivi', NULL),
    (N'241', N'PA004', N'Beneficio già fruito e non restituito per il posto alloggio', NULL),
    (N'242', N'CI002', N'Corso specialistico di tipologia 7 non ammesso al contributo integrativo', NULL),
    (N'243', N'ISC009', N'Comune della sede di studi mancante o non classificabile', N'3'),
    (N'244', N'VAR021', N'Revoca per ISEE non presente in banca dati INPS in base ai riferimenti dichiarati nella domanda', N'2'),
    (N'245', N'BS002', N'Beneficio borsa già fruito e non restituito', NULL),
    (N'246', N'CI004', N'Beneficio già fruito e non restituito per il contributo integrativo', NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- Blocca anche collisioni introdotte dopo la lettura, in qualunque anno.
    IF EXISTS (
        SELECT 1
        FROM dbo.Decod_segnalazioni AS d WITH (UPDLOCK, HOLDLOCK)
        JOIN @Nuove AS n ON n.Codice = d.Cod_segnalazione
        WHERE RTRIM(d.Descrizione) <> n.Descrizione
           OR RTRIM(ISNULL(d.DescrizioneGrad, '')) <> n.Descrizione
           OR d.Tipo_segnalazione <> 'E'
           OR d.Iter_elaborativo <> '0'
           OR ISNULL(d.categoria, '') <> ISNULL(n.Categoria, '')
    )
        THROW 51001, 'Codice gia usato con causale diversa: nessuna decodifica inserita. Rivedere la mappatura.', 1;

    INSERT INTO dbo.Decod_segnalazioni (
        Cod_segnalazione, Anno_accademico, Tipo_segnalazione, Descrizione,
        Iter_elaborativo, Data_validita, DescrizioneGrad, Utente, categoria
    )
    SELECT n.Codice, @AnnoAccademico, 'E', n.Descrizione,
           '0', @DataValidita, n.Descrizione, @Utente, n.Categoria
    FROM @Nuove AS n
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Decod_segnalazioni AS d WITH (UPDLOCK, HOLDLOCK)
        WHERE d.Cod_segnalazione = n.Codice AND d.Anno_accademico = @AnnoAccademico
    );

    DECLARE @Inserite int = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @Inserite AS DecodificheInserite, @AnnoAccademico AS AnnoAccademico;
    SELECT n.Regola, RTRIM(d.Cod_segnalazione) AS Codice, d.Descrizione, d.DescrizioneGrad
    FROM @Nuove AS n
    JOIN dbo.Decod_segnalazioni AS d
      ON d.Cod_segnalazione = n.Codice AND d.Anno_accademico = @AnnoAccademico
    ORDER BY n.Codice;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
