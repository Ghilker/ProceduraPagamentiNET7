import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUTPUT = ROOT / 'outputs/01a19b29-4e8a-73e1-b852-eb1941fe1c1e/Registra_nuove_segnalazioni_Verifica_20262027.sql'
entries = json.loads((HERE / 'mappatura-decisa.json').read_text(encoding='utf-8'))
new = sorted((r for r in entries if r['Stato'] == 'DaRegistrare'), key=lambda r: r['CodiceSegnalazione'])
assert len(new) == 22
def literal(value):
    return 'NULL' if value is None else "N'" + str(value).replace("'", "''") + "'"
values = ',\n'.join('    (' + ', '.join(literal(r[k]) for k in ('CodiceSegnalazione', 'CodiceRegola', 'DescrizioneSegnalazione', 'Categoria')) + ')' for r in new)
sql = """-- Verifica: nuove causali assegnate dopo lettura ADISU del 2026-11-02.
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
""" + values + """;

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
"""
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(sql, encoding='utf-8-sig', newline='\r\n')
print(f'Script SQL scritto, non eseguito: {len(new)} nuove decodifiche.')
