using ProcedureNet7.Modules.Contracts;
using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica.Modules
{
    internal sealed class IscrizioneVerificaModule : IVerificaModule<VerificaPipelineContext>
    {
        private const string BaseIscrizioneSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        UPPER(LTRIM(RTRIM(t.CodFiscale))) AS CodFiscale,
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
        TRY_CONVERT(INT, i.Anno_corso) AS Anno_corso,
        i.Cod_facolta,
        i.Cod_sede_studi,
        CONVERT(NVARCHAR(50), i.Cod_tipologia_studi) AS Cod_tipologia_studi,
        TRY_CONVERT(DECIMAL(18,2), i.Crediti_tirocinio) AS Crediti_tirocinio,
        TRY_CONVERT(DECIMAL(18,2), i.Crediti_riconosciuti) AS Crediti_riconosciuti,
        CAST(CASE WHEN UPPER(CONVERT(NVARCHAR(10), ISNULL(i.Iscritto_semestre_filtro,0))) IN ('1','S','Y','T','TRUE') THEN 1 ELSE 0 END AS BIT) AS Iscritto_semestre_filtro,
        ROW_NUMBER() OVER
        (
            PARTITION BY D.NumDomanda
            ORDER BY D.NumDomanda
        ) AS rn
    FROM D
    LEFT JOIN vIscrizioni i
      ON i.Anno_accademico = @AA
     AND UPPER(LTRIM(RTRIM(i.Cod_fiscale))) = D.CodFiscale
     AND COALESCE(i.tipo_bando,'') = D.TipoBando
),
APP AS
(
    SELECT
        D.NumDomanda,
        D.CodFiscale,
        D.TipoBando,
        a.Cod_sede_distaccata,
        a.Cod_ente,
        ROW_NUMBER() OVER
        (
            PARTITION BY D.NumDomanda
            ORDER BY COALESCE(a.Cod_sede_distaccata,''), COALESCE(a.Cod_ente,'')
        ) AS rn
    FROM D
    LEFT JOIN vAppartenenza a
      ON a.Anno_accademico = @AA
     AND UPPER(LTRIM(RTRIM(a.Cod_fiscale))) = D.CodFiscale
     AND COALESCE(a.tipo_bando,'') = D.TipoBando
),
MER AS
(
    SELECT
        D.NumDomanda,
        TRY_CONVERT(INT, m.Anno_immatricolaz) AS Anno_immatricolaz,
        TRY_CONVERT(INT, m.Numero_esami) AS Numero_esami,
        TRY_CONVERT(DECIMAL(18,2), m.Numero_crediti) AS Numero_crediti,
        TRY_CONVERT(DECIMAL(18,2), m.Somma_voti) AS Somma_voti,
        CAST(CASE WHEN UPPER(CONVERT(NVARCHAR(10), ISNULL(m.Utilizzo_bonus,0))) IN ('1','S','Y','T','TRUE') THEN 1 ELSE 0 END AS BIT) AS Utilizzo_bonus,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_utilizzati) AS Crediti_utilizzati,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_rimanenti) AS Crediti_rimanenti,
        TRY_CONVERT(DECIMAL(18,2), m.Crediti_riconosciuti_da_rinuncia) AS Crediti_riconosciuti_da_rinuncia,
        CONVERT(NVARCHAR(20), m.AACreditiRiconosciuti) AS AACreditiRiconosciuti,
        ROW_NUMBER() OVER
        (
            PARTITION BY D.NumDomanda
            ORDER BY D.NumDomanda
        ) AS rn
    FROM D
    LEFT JOIN vMerito m
      ON m.Anno_accademico = @AA
     AND m.Num_domanda = D.NumDomanda
)
SELECT
    D.NumDomanda,
    D.CodFiscale,
    D.TipoBando,
    I.Cod_corso_laurea,
    I.Anno_corso,
    I.Cod_facolta,
    I.Cod_sede_studi,
    I.Cod_tipologia_studi,
    I.Crediti_tirocinio,
    I.Crediti_riconosciuti,
    I.Iscritto_semestre_filtro,
    A.Cod_sede_distaccata,
    A.Cod_ente,
    M.Anno_immatricolaz,
    M.Numero_esami,
    M.Numero_crediti,
    M.Somma_voti,
    M.Utilizzo_bonus,
    M.Crediti_utilizzati,
    M.Crediti_rimanenti,
    M.Crediti_riconosciuti_da_rinuncia,
    M.AACreditiRiconosciuti
FROM D
LEFT JOIN ISCR I
  ON I.NumDomanda = D.NumDomanda
 AND I.rn = 1
LEFT JOIN APP A
  ON A.NumDomanda = D.NumDomanda
 AND A.rn = 1
LEFT JOIN MER M
  ON M.NumDomanda = D.NumDomanda
 AND M.rn = 1
ORDER BY D.CodFiscale, D.NumDomanda;";

        private const string CarrieraPregressaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        UPPER(LTRIM(RTRIM(t.CodFiscale))) AS CodFiscale,
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
    TRY_CONVERT(DATETIME, cp.Prima_immatricolaz) AS Prima_immatricolaz,
    CONVERT(NVARCHAR(150), cp.Tipologia_corso) AS Tipologia_corso,
    TRY_CONVERT(INT, cp.Durata_leg_titolo_conseguito) AS Durata_leg_titolo_conseguito,
    CAST(CASE WHEN UPPER(CONVERT(NVARCHAR(10), ISNULL(cp.Passaggio_corso_estero,0))) IN ('1','S','Y','T','TRUE') THEN 1 ELSE 0 END AS BIT) AS Passaggio_corso_estero,
    CONVERT(NVARCHAR(250), cp.Sede_istituzione_universitaria) AS Sede_istituzione_universitaria,
    CONVERT(NVARCHAR(250), cp.benefici_usufruiti) AS benefici_usufruiti,
    CONVERT(NVARCHAR(250), cp.importi_restituiti) AS importi_restituiti,
    TRY_CONVERT(DECIMAL(18,2), cp.numero_crediti) AS numero_crediti,
    TRY_CONVERT(INT, cp.anno_corso) AS anno_corso,
    CAST(CASE WHEN UPPER(CONVERT(NVARCHAR(10), ISNULL(cp.ripetente,0))) IN ('1','S','Y','T','TRUE') THEN 1 ELSE 0 END AS BIT) AS ripetente,
    CONVERT(NVARCHAR(250), cp.Ateneo) AS Ateneo,
    CONVERT(NVARCHAR(50), cp.CodComune_Ateneo) AS CodComune_Ateneo,
    CONVERT(NVARCHAR(50), cp.CodAteneo) AS CodAteneo,
    CAST(CASE WHEN UPPER(CONVERT(NVARCHAR(10), ISNULL(cp.Iscritto_semestre_filtroDI,0))) IN ('1','S','Y','T','TRUE') THEN 1 ELSE 0 END AS BIT) AS Iscritto_semestre_filtroDI
FROM D
JOIN vCARRIERA_PREGRESSA cp
  ON cp.Anno_accademico = @AA
 AND UPPER(LTRIM(RTRIM(cp.Cod_fiscale))) = D.CodFiscale
ORDER BY D.CodFiscale, D.NumDomanda, TRY_CONVERT(INT, cp.Anno_avvenimento), CONVERT(NVARCHAR(50), cp.Cod_avvenimento);";

        public string Name => "Iscrizione";

        public void Collect(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ResetStudents(context);
            LoadBaseIscrizione(context);
            LoadCarrieraPregressa(context);
            BuildCarrieraPregressaAggregate(context);
        }

        public void Calculate(VerificaPipelineContext context)
        {
        }

        public void Validate(VerificaPipelineContext context)
        {
        }

        private static void ResetStudents(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = string.Empty;
                iscr.CodEnte = string.Empty;
                iscr.CodSedeDistaccata = string.Empty;
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

        private static void LoadBaseIscrizione(VerificaPipelineContext context)
        {
            string sql = BaseIscrizioneSql.Replace("{TEMP_TABLE}", context.TempCandidatesTable);

            using var cmd = new SqlCommand(sql, context.Connection)
            {
                CommandTimeout = 999999
            };
            cmd.Parameters.AddWithValue("@AA", context.AnnoAccademico);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string cf = GetString(reader, "CodFiscale").Trim().ToUpperInvariant();
                string numDomanda = GetInt(reader, "NumDomanda")?.ToString(CultureInfo.InvariantCulture) ?? "";
                var key = new StudentKey(cf, numDomanda);
                if (!context.Students.TryGetValue(key, out var info))
                    continue;

                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = GetString(reader, "TipoBando");
                SetIfPresent(GetInt(reader, "Anno_corso"), value => iscr.AnnoCorso = value);
                iscr.CodCorsoLaurea = GetString(reader, "Cod_corso_laurea");
                iscr.CodFacolta = GetString(reader, "Cod_facolta");
                iscr.CodSedeStudi = GetString(reader, "Cod_sede_studi");
                SetIfPresent(GetString(reader, "Cod_tipologia_studi"), value =>
                {
                    if (TryParseInt(value, out var tipoCorso))
                        iscr.TipoCorso = tipoCorso;
                });
                iscr.CreditiTirocinio = GetDecimal(reader, "Crediti_tirocinio");
                iscr.CreditiRiconosciuti = GetDecimal(reader, "Crediti_riconosciuti");
                iscr.ConfermaSemestreFiltro = BoolToInt(GetBool(reader, "Iscritto_semestre_filtro"));
                iscr.CodSedeDistaccata = GetString(reader, "Cod_sede_distaccata");
                iscr.CodEnte = GetString(reader, "Cod_ente");
                iscr.AnnoImmatricolazione = GetInt(reader, "Anno_immatricolaz");
                iscr.NumeroEsami = GetInt(reader, "Numero_esami");
                iscr.NumeroCrediti = GetDecimal(reader, "Numero_crediti");
                iscr.SommaVoti = GetDecimal(reader, "Somma_voti");
                iscr.UtilizzoBonus = BoolToInt(GetBool(reader, "Utilizzo_bonus"));
                iscr.CreditiUtilizzati = GetDecimal(reader, "Crediti_utilizzati");
                iscr.CreditiRimanenti = GetDecimal(reader, "Crediti_rimanenti");
                iscr.CreditiRiconosciutiDaRinuncia = GetDecimal(reader, "Crediti_riconosciuti_da_rinuncia");
                iscr.AACreditiRiconosciuti = GetString(reader, "AACreditiRiconosciuti");
            }
        }

        private static void LoadCarrieraPregressa(VerificaPipelineContext context)
        {
            string sql = CarrieraPregressaSql.Replace("{TEMP_TABLE}", context.TempCandidatesTable);

            using var cmd = new SqlCommand(sql, context.Connection)
            {
                CommandTimeout = 999999
            };
            cmd.Parameters.AddWithValue("@AA", context.AnnoAccademico);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string cf = GetString(reader, "CodFiscale").Trim().ToUpperInvariant();
                string numDomanda = GetInt(reader, "NumDomanda")?.ToString(CultureInfo.InvariantCulture) ?? "";
                var key = new StudentKey(cf, numDomanda);
                if (!context.Students.TryGetValue(key, out var info))
                    continue;

                info.InformazioniIscrizione.CarrierePregresse.Add(new InformazioniCarrieraPregressa
                {
                    CodAvvenimento = GetString(reader, "Cod_avvenimento"),
                    AnnoAvvenimento = GetInt(reader, "Anno_avvenimento"),
                    UnivDiConseguim = GetString(reader, "Univ_di_conseguim"),
                    UnivProvenienza = GetString(reader, "Univ_provenienza"),
                    PrimaImmatricolaz = GetDateTime(reader, "Prima_immatricolaz"),
                    TipologiaCorso = GetString(reader, "Tipologia_corso"),
                    DurataLegTitoloConseguito = GetInt(reader, "Durata_leg_titolo_conseguito"),
                    PassaggioCorsoEstero = BoolToInt(GetBool(reader, "Passaggio_corso_estero")),
                    SedeIstituzioneUniversitaria = GetString(reader, "Sede_istituzione_universitaria"),
                    BeneficiUsufruiti = GetString(reader, "benefici_usufruiti"),
                    ImportiRestituiti = GetString(reader, "importi_restituiti"),
                    NumeroCrediti = GetDecimal(reader, "numero_crediti"),
                    AnnoCorso = GetInt(reader, "anno_corso"),
                    Ripetente = BoolToInt(GetBool(reader, "ripetente")),
                    Ateneo = GetString(reader, "Ateneo"),
                    CodComuneAteneo = GetString(reader, "CodComune_Ateneo"),
                    CodAteneo = GetString(reader, "CodAteneo"),
                    ConfermaSemestreFiltroDi = BoolToInt(GetBool(reader, "Iscritto_semestre_filtroDI"))
                });
            }
        }

        private static void BuildCarrieraPregressaAggregate(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                var items = iscr.CarrierePregresse;

                iscr.NumeroEventiCarrieraPregressa = items.Count;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = items
                    .Where(x => x.AnnoAvvenimento.HasValue)
                    .Select(x => x.AnnoAvvenimento!.Value)
                    .DefaultIfEmpty()
                    .Max();
                if (iscr.NumeroEventiCarrieraPregressa == 0)
                    iscr.UltimoAnnoAvvenimentoCarrieraPregressa = null;

                iscr.TotaleCreditiCarrieraPregressa = items.Sum(x => x.NumeroCrediti ?? 0m);
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = items.Any(x => x.PassaggioCorsoEstero != 0) ? 1 : 0;
                iscr.HaRipetenzaCarrieraPregressa = items.Any(x => x.Ripetente != 0) ? 1 : 0;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Join(
                    "|",
                    items
                        .Select(x => x.CodAvvenimento)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }

        private static int BoolToInt(bool? value) => value == true ? 1 : 0;

        private static void SetIfPresent<T>(T? value, Action<T> setter) where T : class
        {
            if (value != null)
                setter(value);
        }

        private static void SetIfPresent(int? value, Action<int> setter)
        {
            if (value.HasValue)
                setter(value.Value);
        }

        private static string GetString(IDataRecord record, string columnName)
        {
            int ordinal = TryGetOrdinal(record, columnName);
            if (ordinal < 0 || record.IsDBNull(ordinal))
                return string.Empty;

            return Convert.ToString(record.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static int? GetInt(IDataRecord record, string columnName)
        {
            int ordinal = TryGetOrdinal(record, columnName);
            if (ordinal < 0 || record.IsDBNull(ordinal))
                return null;

            object value = record.GetValue(ordinal);
            if (value is int i)
                return i;
            if (value is long l)
                return checked((int)l);
            if (value is short s)
                return s;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        private static decimal? GetDecimal(IDataRecord record, string columnName)
        {
            int ordinal = TryGetOrdinal(record, columnName);
            if (ordinal < 0 || record.IsDBNull(ordinal))
                return null;

            object value = record.GetValue(ordinal);
            if (value is decimal d)
                return d;
            if (value is double dbl)
                return Convert.ToDecimal(dbl, CultureInfo.InvariantCulture);
            if (value is float fl)
                return Convert.ToDecimal(fl, CultureInfo.InvariantCulture);
            if (value is int i)
                return i;
            if (decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        private static bool? GetBool(IDataRecord record, string columnName)
        {
            int ordinal = TryGetOrdinal(record, columnName);
            if (ordinal < 0 || record.IsDBNull(ordinal))
                return null;

            object value = record.GetValue(ordinal);
            if (value is bool b)
                return b;
            if (value is int i)
                return i != 0;
            if (value is short s)
                return s != 0;
            var text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (text == "1") return true;
            if (text == "0") return false;
            if (bool.TryParse(text, out var parsed)) return parsed;
            return null;
        }

        private static DateTime? GetDateTime(IDataRecord record, string columnName)
        {
            int ordinal = TryGetOrdinal(record, columnName);
            if (ordinal < 0 || record.IsDBNull(ordinal))
                return null;

            object value = record.GetValue(ordinal);
            if (value is DateTime dt)
                return dt;
            if (DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
            return null;
        }

        private static int TryGetOrdinal(IDataRecord record, string columnName)
        {
            for (int i = 0; i < record.FieldCount; i++)
            {
                if (string.Equals(record.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool TryParseInt(string? value, out int parsed)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }
    }
}
