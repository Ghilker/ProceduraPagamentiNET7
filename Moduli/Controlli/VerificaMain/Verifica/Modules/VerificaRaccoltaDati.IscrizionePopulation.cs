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
        private const string IscrizioneCorePopulationSql = @"
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
        D.TipoBando,
        i.Cod_corso_laurea,
        cl.Durata_legale,
        TRY_CONVERT(INT, i.Anno_corso) AS Anno_corso,
        i.Cod_facolta,
        i.Cod_sede_studi,
        CONVERT(NVARCHAR(50), i.Cod_tipologia_studi) AS Cod_tipologia_studi,
        TRY_CONVERT(DECIMAL(18,2), i.Crediti_tirocinio) AS Crediti_tirocinio,
        TRY_CONVERT(DECIMAL(18,2), i.Crediti_riconosciuti) AS Crediti_riconosciuti,
        TRY_CONVERT(INT, i.Conferma_semestre_filtro) AS Conferma_semestre_filtro,
        CAST(ISNULL(cl.Corso_stem,0) AS BIT) AS Stem,
        COALESCE(NULLIF(cl.comune_sede_studi_status,''), cl.Comune_Sede_studi) AS ComuneSedeStudi,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY TRY_CONVERT(INT, i.Anno_corso) DESC, COALESCE(i.Cod_corso_laurea,''), COALESCE(i.Cod_facolta,'')) AS rn
    FROM D
    LEFT JOIN vIscrizioni i
      ON i.Anno_accademico = @AA
     AND i.Cod_fiscale = D.CodFiscale
     AND COALESCE(i.tipo_bando,'') = D.TipoBando
    LEFT JOIN Corsi_laurea cl
      ON i.Cod_corso_laurea     = cl.Cod_corso_laurea
     AND i.Anno_accad_inizio    = cl.Anno_accad_inizio
     AND i.Cod_tipo_ordinamento = cl.Cod_tipo_ordinamento
     AND i.Cod_facolta          = cl.Cod_facolta
     AND i.Cod_sede_studi       = cl.Cod_sede_studi
     AND i.Cod_tipologia_studi  = cl.Cod_tipologia_studi
)
SELECT
    I.NumDomanda,
    I.CodFiscale,
    I.TipoBando,
    I.Cod_corso_laurea,
    I.Durata_legale,
    I.Anno_corso,
    I.Cod_facolta,
    I.Cod_sede_studi,
    I.Cod_tipologia_studi,
    I.Crediti_tirocinio,
    I.Crediti_riconosciuti,
    ISNULL(I.Conferma_semestre_filtro, 0) AS Conferma_semestre_filtro,
    I.Stem,
    ISNULL(I.ComuneSedeStudi,'') AS ComuneSedeStudi,
    ISNULL(c.COD_PROVINCIA,'') AS ProvinciaSede
FROM ISCR I
LEFT JOIN COMUNI c
  ON c.COD_COMUNE = I.ComuneSedeStudi
WHERE I.rn = 1;";

        private const string AppartenenzaPopulationSql = @"
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
APP AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        CONVERT(NVARCHAR(50), a.Cod_sede_distaccata) AS Cod_sede_distaccata,
        CONVERT(NVARCHAR(50), a.Cod_ente) AS Cod_ente,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY COALESCE(a.Cod_sede_distaccata,''), COALESCE(a.Cod_ente,'')) AS rn
    FROM D
    LEFT JOIN vAppartenenza a
      ON a.Anno_accademico = @AA
     AND a.Cod_fiscale = D.CodFiscale
     AND COALESCE(a.tipo_bando,'') = D.TipoBando
)
SELECT NumDomanda, CodFiscale, ISNULL(Cod_sede_distaccata,'') AS Cod_sede_distaccata, ISNULL(Cod_ente,'') AS Cod_ente
FROM APP
WHERE rn = 1;";

        private const string MeritoPopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT CAST(t.NumDomanda AS INT) AS NumDomanda, t.CodFiscale
    FROM {TEMP_TABLE} t
),
MER AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        TRY_CONVERT(INT, m.Anno_immatricolaz) AS Anno_immatricolaz,
        TRY_CONVERT(INT, m.Numero_esami) AS Numero_esami,
        TRY_CONVERT(DECIMAL(18,2), m.Numero_crediti) AS Numero_crediti,
        TRY_CONVERT(DECIMAL(18,2), m.Somma_voti) AS Somma_voti,
        TRY_CONVERT(INT, m.Utilizzo_bonus) AS Utilizzo_bonus,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_utilizzati) AS Crediti_utilizzati,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_rimanenti) AS Crediti_rimanenti,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_riconosciuti_da_rinuncia) AS Crediti_riconosciuti_da_rinuncia,
        CONVERT(NVARCHAR(20), m.AACreditiRiconosciuti) AS AACreditiRiconosciuti,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY D.NumDomanda) AS rn
    FROM D
    LEFT JOIN vMerito m
      ON m.Anno_accademico = @AA
     AND m.Num_domanda = D.NumDomanda
)
SELECT
    NumDomanda,
    CodFiscale,
    Anno_immatricolaz,
    Numero_esami,
    Numero_crediti,
    Somma_voti,
    ISNULL(Utilizzo_bonus,0) AS Utilizzo_bonus,
    Crediti_utilizzati,
    Crediti_rimanenti,
    Crediti_riconosciuti_da_rinuncia,
    ISNULL(AACreditiRiconosciuti,'') AS AACreditiRiconosciuti
FROM MER
WHERE rn = 1;";

        private const string CarrieraPregressaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        t.CodFiscale,
        COALESCE(t.TipoBando,'') AS TipoBando
    FROM {TEMP_TABLE} t
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    D.TipoBando,
    CONVERT(NVARCHAR(50), cp.Cod_avvenimento) AS Cod_avvenimento,
    TRY_CONVERT(INT, cp.Anno_avvenimento) AS Anno_avvenimento,
    CONVERT(NVARCHAR(250), cp.Univ_di_conseguim) AS Univ_di_conseguim,
    CONVERT(NVARCHAR(250), cp.Univ_provenienza) AS Univ_provenienza,
    TRY_CONVERT(INT, cp.Prima_immatricolaz) AS Prima_immatricolaz,
    CONVERT(NVARCHAR(150), cp.Tipologia_corso) AS Tipologia_corso,
    TRY_CONVERT(INT, cp.Durata_leg_titolo_conseguito) AS Durata_leg_titolo_conseguito,
    TRY_CONVERT(INT, cp.Passaggio_corso_estero) AS Passaggio_corso_estero,
    CONVERT(NVARCHAR(250), cp.Sede_istituzione_universitaria) AS Sede_istituzione_universitaria,
    CONVERT(NVARCHAR(250), cp.benefici_usufruiti) AS benefici_usufruiti,
    CONVERT(NVARCHAR(250), cp.importi_restituiti) AS importi_restituiti,
    TRY_CONVERT(DECIMAL(18,2), cp.numero_crediti) AS numero_crediti,
    TRY_CONVERT(INT, cp.anno_corso) AS anno_corso,
    TRY_CONVERT(INT, cp.ripetente) AS ripetente,
    CONVERT(NVARCHAR(250), cp.Ateneo) AS Ateneo,
    CONVERT(NVARCHAR(50), cp.CodComune_Ateneo) AS CodComune_Ateneo,
    CONVERT(NVARCHAR(50), cp.CodAteneo) AS CodAteneo,
    TRY_CONVERT(INT, cp.Iscritto_semestre_filtroDI) AS Iscritto_semestre_filtroDI
FROM D
JOIN vCARRIERA_PREGRESSA cp
  ON cp.Anno_accademico = @AA
 AND cp.Cod_fiscale = D.CodFiscale;";

        private static void ResetIscrizioneState(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = string.Empty;
                iscr.AnnoCorso = 0;
                iscr.TipoCorso = 0;
                iscr.CodCorsoLaurea = string.Empty;
                iscr.CodSedeStudi = string.Empty;
                iscr.CodFacolta = string.Empty;
                iscr.CodEnte = string.Empty;
                iscr.CodSedeDistaccata = string.Empty;
                iscr.ComuneSedeStudi = string.Empty;
                iscr.ProvinciaSedeStudi = string.Empty;
                iscr.CorsoStem = false;
                iscr.CreditiTirocinio = null;
                iscr.CreditiRiconosciuti = null;
                iscr.ConfermaSemestreFiltro = 0;
                iscr.AnnoImmatricolazione = null;
                iscr.NumeroEsami = null;
                iscr.NumeroCrediti = null;
                iscr.SommaVoti = null;
                iscr.UtilizzoBonus = 0;
                iscr.CreditiUtilizzati = null;
                iscr.CreditiRimanenti = null;
                iscr.CreditiRiconosciutiDaRinuncia = null;
                iscr.AACreditiRiconosciuti = string.Empty;
                iscr.NumeroEventiCarrieraPregressa = 0;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = null;
                iscr.TotaleCreditiCarrieraPregressa = 0m;
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = 0;
                iscr.HaRipetenzaCarrieraPregressa = 0;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Empty;
                iscr.CarrierePregresse.Clear();
            }
        }

        private void LoadBaseIscrizione(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadBaseIscrizione", $"AA={context.AnnoAccademico}");
            LoadIscrizioneCore(context);
            LoadAppartenenza(context);
            LoadMerito(context);
        }

        private void LoadIscrizioneCore(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(IscrizioneCorePopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = reader.SafeGetString("TipoBando");
                iscr.AnnoCorso = reader.SafeGetInt("Anno_corso");
                iscr.CodCorsoLaurea = reader.SafeGetString("Cod_corso_laurea");
                iscr.CorsoMedicina = reader.SafeGetInt("Durata_legale") == 6;
                iscr.CodFacolta = reader.SafeGetString("Cod_facolta");
                iscr.CodSedeStudi = reader.SafeGetString("Cod_sede_studi");
                iscr.ComuneSedeStudi = reader.SafeGetString("ComuneSedeStudi").Trim();
                iscr.ProvinciaSedeStudi = reader.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();
                iscr.CorsoStem = reader.SafeGetBool("Stem");
                iscr.CreditiTirocinio = reader.SafeGetDecimal("Crediti_tirocinio");
                iscr.CreditiRiconosciuti = reader.SafeGetDecimal("Crediti_riconosciuti");
                iscr.ConfermaSemestreFiltro = reader.SafeGetInt("Conferma_semestre_filtro");

                if (TryParseInt(reader.SafeGetString("Cod_tipologia_studi"), out var tipoCorso))
                    iscr.TipoCorso = tipoCorso;
            });
        }

        private void LoadAppartenenza(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(AppartenenzaPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.CodSedeDistaccata = reader.SafeGetString("Cod_sede_distaccata");
                iscr.CodEnte = reader.SafeGetString("Cod_ente");
            });
        }

        private void LoadMerito(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(MeritoPopulationSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var iscr = info.InformazioniIscrizione;
                iscr.AnnoImmatricolazione = reader.SafeGetInt("Anno_immatricolaz");
                iscr.NumeroEsami = reader.SafeGetInt("Numero_esami");
                iscr.NumeroCrediti = reader.SafeGetDecimal("Numero_crediti");
                iscr.SommaVoti = reader.SafeGetDecimal("Somma_voti");
                iscr.UtilizzoBonus = reader.SafeGetInt("Utilizzo_bonus");
                iscr.CreditiUtilizzati = reader.SafeGetDecimal("Crediti_utilizzati");
                iscr.CreditiRimanenti = reader.SafeGetDecimal("Crediti_rimanenti");
                iscr.CreditiRiconosciutiDaRinuncia = reader.SafeGetDecimal("Crediti_riconosciuti_da_rinuncia");
                iscr.AACreditiRiconosciuti = reader.SafeGetString("AACreditiRiconosciuti");
            });
        }

        private void LoadCarrieraPregressa(VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep("VerificaRaccoltaDati.LoadCarrieraPregressa", $"AA={context.AnnoAccademico}");
            using var cmd = CreatePopulationCommand(CarrieraPregressaSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                info.InformazioniIscrizione.CarrierePregresse.Add(new InformazioniCarrieraPregressa
                {
                    CodAvvenimento = reader.SafeGetString("Cod_avvenimento"),
                    AnnoAvvenimento = reader.SafeGetInt("Anno_avvenimento"),
                    UnivDiConseguim = reader.SafeGetString("Univ_di_conseguim"),
                    UnivProvenienza = reader.SafeGetString("Univ_provenienza"),
                    PrimaImmatricolaz = reader.SafeGetInt("Prima_immatricolaz"),
                    TipologiaCorso = reader.SafeGetString("Tipologia_corso"),
                    DurataLegTitoloConseguito = reader.SafeGetInt("Durata_leg_titolo_conseguito"),
                    PassaggioCorsoEstero = reader.SafeGetInt("Passaggio_corso_estero"),
                    SedeIstituzioneUniversitaria = reader.SafeGetString("Sede_istituzione_universitaria"),
                    BeneficiUsufruiti = reader.SafeGetString("benefici_usufruiti"),
                    ImportiRestituiti = reader.SafeGetString("importi_restituiti"),
                    NumeroCrediti = reader.SafeGetDecimal("numero_crediti"),
                    AnnoCorso = reader.SafeGetInt("anno_corso"),
                    Ripetente = reader.SafeGetInt("ripetente"),
                    Ateneo = reader.SafeGetString("Ateneo"),
                    CodComuneAteneo = reader.SafeGetString("CodComune_Ateneo"),
                    CodAteneo = reader.SafeGetString("CodAteneo"),
                    ConfermaSemestreFiltroDi = reader.SafeGetInt("Iscritto_semestre_filtroDI")
                });
            });
        }

        private static void BuildCarrieraPregressaAggregate(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                var items = iscr.CarrierePregresse;

                int count = 0;
                int? lastYear = null;
                decimal totalCredits = 0m;
                int hasPassaggioEstero = 0;
                int hasRipetenza = 0;
                var codici = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    count++;

                    if (item.AnnoAvvenimento.HasValue && (!lastYear.HasValue || item.AnnoAvvenimento.Value > lastYear.Value))
                        lastYear = item.AnnoAvvenimento.Value;

                    totalCredits += item.NumeroCrediti ?? 0m;

                    if (item.PassaggioCorsoEstero != 0)
                        hasPassaggioEstero = 1;

                    if (item.Ripetente != 0)
                        hasRipetenza = 1;

                    if (!string.IsNullOrWhiteSpace(item.CodAvvenimento))
                        codici.Add(item.CodAvvenimento);
                }

                iscr.NumeroEventiCarrieraPregressa = count;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = count == 0 ? null : lastYear;
                iscr.TotaleCreditiCarrieraPregressa = totalCredits;
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = hasPassaggioEstero;
                iscr.HaRipetenzaCarrieraPregressa = hasRipetenza;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Join("|", codici);
            }
        }

        private static bool TryParseInt(string? value, out int parsed)
            => int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }
}
