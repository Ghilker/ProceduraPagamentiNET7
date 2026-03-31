using ProcedureNet7.Verifica;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private readonly SqlConnection _conn;

        private readonly System.Collections.Generic.Dictionary<StudentKey, StudenteInfo> _studentsByKey = new();


        private readonly CalcParams _calc = new();
        private readonly System.Collections.Generic.HashSet<(string ComuneA, string ComuneB)> _comuniEquiparati = new();

        private const string VerificaCandidatesSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH
DomandeRaw AS
(
    SELECT
        CAST(d.Num_domanda AS INT) AS NumDomanda,
        UPPER(LTRIM(RTRIM(d.Cod_fiscale))) AS CodFiscale,
        COALESCE(d.Tipo_bando,'') AS TipoBando,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.Num_domanda
            ORDER BY d.Data_validita DESC, d.Tipo_bando
        ) AS rn
    FROM Domanda d
    WHERE d.Anno_accademico = @AA
      AND d.Tipo_bando = 'lz'
),
D0 AS
(
    SELECT NumDomanda, CodFiscale, TipoBando
    FROM DomandeRaw
    WHERE rn = 1
),
SC AS
(
    SELECT
        CAST(v.Num_domanda AS INT) AS NumDomanda,
        CAST(ISNULL(v.status_compilazione,0) AS INT) AS StatusCompilazione
    FROM vstatus_compilazione v
    WHERE v.anno_accademico = @AA
),
BS_LAST AS
(
    SELECT
        CAST(ec.Num_domanda AS INT) AS NumDomanda,
        CAST(ec.Cod_tipo_esito AS INT) AS CodTipoEsitoBS,
        ec.Imp_beneficio AS ImportoAssegnato,
        ROW_NUMBER() OVER
        (
            PARTITION BY ec.Num_domanda
            ORDER BY ec.Data_validita DESC
        ) AS rn
    FROM ESITI_CONCORSI ec
    JOIN D0 ON D0.NumDomanda = ec.Num_domanda
    WHERE ec.Anno_accademico = @AA
      AND UPPER(ec.Cod_beneficio) = 'BS'
),
BS AS
(
    SELECT NumDomanda, CodTipoEsitoBS, ImportoAssegnato
    FROM BS_LAST
    WHERE rn = 1
),
D AS
(
    SELECT
        d0.NumDomanda,
        d0.CodFiscale,
        d0.TipoBando,
        ISNULL(bs.CodTipoEsitoBS,0) AS CodTipoEsitoBS,
        ISNULL(bs.ImportoAssegnato,0) AS ImportoAssegnato,
        ISNULL(sc.StatusCompilazione,0) AS StatusCompilazione
    FROM D0 d0
    LEFT JOIN BS bs ON bs.NumDomanda = d0.NumDomanda
    LEFT JOIN SC sc ON sc.NumDomanda = d0.NumDomanda
    WHERE
        (@IncludeEsclusi = 1 OR ISNULL(bs.CodTipoEsitoBS,0) <> 0)
        AND
        (@IncludeNonTrasmesse = 1 OR ISNULL(sc.StatusCompilazione,0) >= 90)
)
SELECT
    NumDomanda,
    CodFiscale,
    TipoBando,
    CodTipoEsitoBS,
    ImportoAssegnato,
    StatusCompilazione
FROM D
ORDER BY CodFiscale, NumDomanda;
";


        public VerificaRaccoltaDati(SqlConnection conn)
        {
            _conn = conn ?? throw new ArgumentNullException(nameof(conn));
        }

        public void PopolaContesto(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var pipelineTable = CaricaCandidatiEInizializzaStudenti(context);
            if (context.Students.Count == 0)
            {
                context.CalcParams = _calc.Clone();
                return;
            }

            CreateTempPipelineTable(context.Connection, context.TempPipelineTable);
            BulkCopyPipelineTargets(context.Connection, context.TempPipelineTable, pipelineTable);

            try
            {
                RaccogliEconomiciDaContesto(context);
                context.CalcParams = _calc.Clone();

                RaccogliStatusSede(context);

                ResetIscrizioneState(context);
                LoadBaseIscrizione(context);
                LoadCarrieraPregressa(context);
                BuildCarrieraPregressaAggregate(context);
            }
            finally
            {
                DropTempPipelineTable(context.Connection, context.TempPipelineTable);
            }
        }

        private void ResetState()
        {
            _studentsByKey.Clear();
            _comuniEquiparati.Clear();
        }

        private void InitializeStudentsFromContext(System.Collections.Generic.IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            foreach (var pair in students)
            {
                var key = pair.Key;
                var info = pair.Value ?? new StudenteInfo();
                string cf = NormalizeCf(key.CodFiscale);
                string numDomanda = NormalizeDomanda(key.NumDomanda);

                info.InformazioniPersonali.CodFiscale = cf;
                info.InformazioniPersonali.NumDomanda = numDomanda;
                info.InformazioniEconomiche ??= new InformazioniEconomiche();
                var studentKey = new StudentKey(cf, numDomanda);
                _studentsByKey[studentKey] = info;
            }
        }

        private static string NormalizeCf(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim().ToUpperInvariant());

        private static string NormalizeDomanda(string? value)
            => Utilities.RemoveAllSpaces((value ?? "").Trim());

        private static StudentKey CreateStudentKey(string? codFiscale, string? numDomanda)
            => new StudentKey(NormalizeCf(codFiscale), NormalizeDomanda(numDomanda));

        private bool TryGetStudentInfo(SqlDataReader reader, out StudenteInfo info, string cfColumn = "Cod_fiscale", string domandaColumn = "Num_domanda")
        {
            var key = CreateStudentKey(reader.SafeGetString(cfColumn), reader.SafeGetString(domandaColumn));
            if (_studentsByKey.TryGetValue(key, out info!) && info != null)
            {
                info.InformazioniEconomiche ??= new InformazioniEconomiche();
                return true;
            }

            return false;
        }

        private InformazioniEconomiche GetEconomicInfo(StudentKey key)
        {
            if (_studentsByKey.TryGetValue(key, out var info) && info != null)
            {
                info.InformazioniEconomiche ??= new InformazioniEconomiche();
                return info.InformazioniEconomiche;
            }

            throw new InvalidOperationException($"Studente non trovato per chiave economica {key.CodFiscale}/{key.NumDomanda}.");
        }

        private void RaccogliStatusSede(VerificaPipelineContext context)
        {
            _comuniEquiparati.Clear();

            LoadStatusSedeFromTempCandidates(
                context.AnnoAccademico,
                context.TempPipelineTable,
                context.Students);

            foreach (var pair in LoadComuniEquiparatiFromDb())
                _comuniEquiparati.Add(pair);

            context.ComuniEquiparati.Clear();
            foreach (var pair in _comuniEquiparati)
                context.ComuniEquiparati.Add(pair);
        }


        private DataTable CaricaCandidatiEInizializzaStudenti(VerificaPipelineContext context)
        {
            context.Students.Clear();
            context.ComuniEquiparati.Clear();

            var table = BuildPipelineTargetsDataTable();

            using var cmd = new SqlCommand(VerificaCandidatesSql, _conn)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };

            cmd.Parameters.Add("@AA", SqlDbType.Char, 8).Value = context.AnnoAccademico;
            cmd.Parameters.Add("@IncludeEsclusi", SqlDbType.Bit).Value = context.IncludeEsclusi;
            cmd.Parameters.Add("@IncludeNonTrasmesse", SqlDbType.Bit).Value = context.IncludeNonTrasmesse;

            var filtroCf = context.CodiciFiscaliFiltro.Count == 0
                ? null
                : new HashSet<string>(context.CodiciFiscaliFiltro.Select(NormalizeCf), StringComparer.OrdinalIgnoreCase);

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Estrazione candidati | AA={context.AnnoAccademico} | IncludeEsclusi={context.IncludeEsclusi} | IncludeNonTrasmesse={context.IncludeNonTrasmesse}");

            using var reader = cmd.ExecuteReader();
            int read = 0;
            while (reader.Read())
            {
                int numDomanda = reader.SafeGetInt("NumDomanda");
                string codFiscale = NormalizeCf(reader.SafeGetString("CodFiscale"));
                if (string.IsNullOrWhiteSpace(codFiscale))
                {
                    read++;
                    continue;
                }

                if (filtroCf != null && !filtroCf.Contains(codFiscale))
                {
                    read++;
                    continue;
                }

                string tipoBando = (reader.SafeGetString("TipoBando") ?? "").Trim();
                int codTipoEsitoBs = reader.SafeGetInt("CodTipoEsitoBS");
                decimal importoAssegnato = reader["ImportoAssegnato"] is DBNull
                    ? 0m
                    : Convert.ToDecimal(reader["ImportoAssegnato"], CultureInfo.InvariantCulture);
                int statusCompilazione = reader.SafeGetInt("StatusCompilazione");
                string numDomandaText = numDomanda.ToString(CultureInfo.InvariantCulture);

                var info = new StudenteInfo
                {
                    TipoBando = tipoBando,
                    StatusCompilazione = statusCompilazione
                };
                info.InformazioniPersonali.CodFiscale = codFiscale;
                info.InformazioniPersonali.NumDomanda = numDomandaText;

                var key = new StudentKey(codFiscale, numDomandaText);
                context.Students[key] = info;

                table.Rows.Add(numDomanda, codFiscale, tipoBando, codTipoEsitoBs, importoAssegnato, statusCompilazione, false, false, false, false, false);

                read++;
                if (read % 5000 == 0)
                    Logger.LogInfo(null, $"[VerificaRaccoltaDati] Candidati letti... {read}");
            }

            Logger.LogInfo(null, $"[VerificaRaccoltaDati] Candidati utilizzati: {context.Students.Count}");
            return table;
        }

        private static DataTable BuildPipelineTargetsDataTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("NumDomanda", typeof(int));
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));
            dt.Columns.Add("ImportoAssegnato", typeof(decimal));
            dt.Columns.Add("StatusCompilazione", typeof(int));
            dt.Columns.Add("IsOrigIT_CO", typeof(bool));
            dt.Columns.Add("IsOrigIT_DO", typeof(bool));
            dt.Columns.Add("IsOrigEE", typeof(bool));
            dt.Columns.Add("IsIntIT_CI", typeof(bool));
            dt.Columns.Add("IsIntDI", typeof(bool));
            return dt;
        }

        private static void CreateTempPipelineTable(SqlConnection conn, string tempTableName)
        {
            string sql = $@"
IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL
    DROP TABLE {tempTableName};

CREATE TABLE {tempTableName}
(
    NumDomanda INT NOT NULL,
    CodFiscale NVARCHAR(32) NOT NULL,
    TipoBando NVARCHAR(16) NOT NULL,
    CodTipoEsitoBS INT NOT NULL,
    ImportoAssegnato DECIMAL(10,0) NOT NULL,
    StatusCompilazione INT NOT NULL,
    IsOrigIT_CO BIT NOT NULL,
    IsOrigIT_DO BIT NOT NULL,
    IsOrigEE BIT NOT NULL,
    IsIntIT_CI BIT NOT NULL,
    IsIntDI BIT NOT NULL
);

CREATE INDEX IX_{tempTableName.TrimStart('#')}_CF_ND ON {tempTableName}(CodFiscale, NumDomanda);
CREATE INDEX IX_{tempTableName.TrimStart('#')}_ND ON {tempTableName}(NumDomanda);";

            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void DropTempPipelineTable(SqlConnection conn, string tempTableName)
        {
            string sql = $"IF OBJECT_ID('tempdb..{tempTableName}') IS NOT NULL DROP TABLE {tempTableName};";
            using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 9999999 };
            cmd.ExecuteNonQuery();
        }

        private static void BulkCopyPipelineTargets(SqlConnection conn, string tempTableName, DataTable candidatesTable)
        {
            using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, externalTransaction: null)
            {
                DestinationTableName = tempTableName,
                BulkCopyTimeout = 9999999,
                BatchSize = 5000
            };

            bulk.ColumnMappings.Add("NumDomanda", "NumDomanda");
            bulk.ColumnMappings.Add("CodFiscale", "CodFiscale");
            bulk.ColumnMappings.Add("TipoBando", "TipoBando");
            bulk.ColumnMappings.Add("CodTipoEsitoBS", "CodTipoEsitoBS");
            bulk.ColumnMappings.Add("ImportoAssegnato", "ImportoAssegnato");
            bulk.ColumnMappings.Add("StatusCompilazione", "StatusCompilazione");
            bulk.ColumnMappings.Add("IsOrigIT_CO", "IsOrigIT_CO");
            bulk.ColumnMappings.Add("IsOrigIT_DO", "IsOrigIT_DO");
            bulk.ColumnMappings.Add("IsOrigEE", "IsOrigEE");
            bulk.ColumnMappings.Add("IsIntIT_CI", "IsIntIT_CI");
            bulk.ColumnMappings.Add("IsIntDI", "IsIntDI");
            bulk.WriteToServer(candidatesTable);
        }

        private static (string ComuneA, string ComuneB) NormalizeComunePair(string? comuneA, string? comuneB)
        {
            string a = (comuneA ?? "").Trim().ToUpperInvariant();
            string b = (comuneB ?? "").Trim().ToUpperInvariant();

            return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        }
    }

    internal sealed partial class VerificaRaccoltaDati
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
        TRY_CONVERT(INT, i.Conferma_semestre_filtro,0) AS Conferma_semestre_filtro,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY D.NumDomanda) AS rn
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
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY COALESCE(a.Cod_sede_distaccata,''), COALESCE(a.Cod_ente,'')) AS rn
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
        TRY_CONVERT(INT, m.Utilizzo_bonus,0) AS Utilizzo_bonus,
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
    I.Conferma_semestre_filtro,
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
LEFT JOIN ISCR I ON I.NumDomanda = D.NumDomanda AND I.rn = 1
LEFT JOIN APP A ON A.NumDomanda = D.NumDomanda AND A.rn = 1
LEFT JOIN MER M ON M.NumDomanda = D.NumDomanda AND M.rn = 1
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
    TRY_CONVERT(INT, cp.Prima_immatricolaz) AS Prima_immatricolaz,
    CONVERT(NVARCHAR(150), cp.Tipologia_corso) AS Tipologia_corso,
    TRY_CONVERT(INT, cp.Durata_leg_titolo_conseguito) AS Durata_leg_titolo_conseguito,
    TRY_CONVERT(INT, cp.Passaggio_corso_estero,0) AS Passaggio_corso_estero,
    CONVERT(NVARCHAR(250), cp.Sede_istituzione_universitaria) AS Sede_istituzione_universitaria,
    CONVERT(NVARCHAR(250), cp.benefici_usufruiti) AS benefici_usufruiti,
    CONVERT(NVARCHAR(250), cp.importi_restituiti) AS importi_restituiti,
    TRY_CONVERT(DECIMAL(18,2), cp.numero_crediti) AS numero_crediti,
    TRY_CONVERT(INT, cp.anno_corso) AS anno_corso,
    TRY_CONVERT(INT, cp.ripetente,0) AS ripetente,
    CONVERT(NVARCHAR(250), cp.Ateneo) AS Ateneo,
    CONVERT(NVARCHAR(50), cp.CodComune_Ateneo) AS CodComune_Ateneo,
    CONVERT(NVARCHAR(50), cp.CodAteneo) AS CodAteneo,
    TRY_CONVERT(INT, cp.Iscritto_semestre_filtroDI,0) AS Iscritto_semestre_filtroDI
FROM D
JOIN vCARRIERA_PREGRESSA cp
  ON cp.Anno_accademico = @AA
 AND UPPER(LTRIM(RTRIM(cp.Cod_fiscale))) = D.CodFiscale
ORDER BY D.CodFiscale, D.NumDomanda, TRY_CONVERT(INT, cp.Anno_avvenimento), CONVERT(NVARCHAR(50), cp.Cod_avvenimento);";

        private static void ResetIscrizioneState(VerificaPipelineContext context)
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
            string sql = BaseIscrizioneSql.Replace("{TEMP_TABLE}", context.TempPipelineTable);

            using var cmd = new SqlCommand(sql, context.Connection) { CommandTimeout = 999999 };
            cmd.Parameters.AddWithValue("@AA", context.AnnoAccademico);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string cf = reader.SafeGetString("CodFiscale").Trim().ToUpperInvariant();
                string numDomanda = reader.SafeGetInt("NumDomanda").ToString(CultureInfo.InvariantCulture) ?? "";
                var key = new StudentKey(cf, numDomanda);
                if (!context.Students.TryGetValue(key, out var info))
                    continue;

                var iscr = info.InformazioniIscrizione;
                iscr.TipoBando = reader.SafeGetString("TipoBando");
                SetIfPresent(reader.SafeGetInt("Anno_corso"), value => iscr.AnnoCorso = value);
                iscr.CodCorsoLaurea = reader.SafeGetString("Cod_corso_laurea");
                iscr.CodFacolta = reader.SafeGetString("Cod_facolta");
                iscr.CodSedeStudi = reader.SafeGetString("Cod_sede_studi");
                SetIfPresent(reader.SafeGetString("Cod_tipologia_studi"), value =>
                {
                    if (TryParseInt(value, out var tipoCorso))
                        iscr.TipoCorso = tipoCorso;
                });
                iscr.CreditiTirocinio = reader.SafeGetDecimal("Crediti_tirocinio");
                iscr.CreditiRiconosciuti = reader.SafeGetDecimal("Crediti_riconosciuti");
                iscr.ConfermaSemestreFiltro = reader.SafeGetInt("Conferma_semestre_filtro");
                iscr.CodSedeDistaccata = reader.SafeGetString("Cod_sede_distaccata");
                iscr.CodEnte = reader.SafeGetString("Cod_ente");
                iscr.AnnoImmatricolazione = reader.SafeGetInt("Anno_immatricolaz");
                iscr.NumeroEsami = reader.SafeGetInt("Numero_esami");
                iscr.NumeroCrediti = reader.SafeGetDecimal("Numero_crediti");
                iscr.SommaVoti = reader.SafeGetDecimal("Somma_voti");
                iscr.UtilizzoBonus = reader.SafeGetInt("Utilizzo_bonus");
                iscr.CreditiUtilizzati = reader.SafeGetDecimal("Crediti_utilizzati");
                iscr.CreditiRimanenti = reader.SafeGetDecimal("Crediti_rimanenti");
                iscr.CreditiRiconosciutiDaRinuncia = reader.SafeGetDecimal("Crediti_riconosciuti_da_rinuncia");
                iscr.AACreditiRiconosciuti = reader.SafeGetString("AACreditiRiconosciuti");
            }
        }

        private static void LoadCarrieraPregressa(VerificaPipelineContext context)
        {
            string sql = CarrieraPregressaSql.Replace("{TEMP_TABLE}", context.TempPipelineTable);

            using var cmd = new SqlCommand(sql, context.Connection) { CommandTimeout = 999999 };
            cmd.Parameters.AddWithValue("@AA", context.AnnoAccademico);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string cf = reader.SafeGetString("CodFiscale").Trim().ToUpperInvariant();
                string numDomanda = reader.SafeGetInt("NumDomanda").ToString(CultureInfo.InvariantCulture) ?? "";
                var key = new StudentKey(cf, numDomanda);
                if (!context.Students.TryGetValue(key, out var info))
                    continue;

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
            }
        }

        private static void BuildCarrieraPregressaAggregate(VerificaPipelineContext context)
        {
            foreach (var info in context.Students.Values)
            {
                var iscr = info.InformazioniIscrizione;
                var items = iscr.CarrierePregresse;

                iscr.NumeroEventiCarrieraPregressa = items.Count;
                iscr.UltimoAnnoAvvenimentoCarrieraPregressa = items.Where(x => x.AnnoAvvenimento.HasValue)
                    .Select(x => x.AnnoAvvenimento!.Value)
                    .DefaultIfEmpty()
                    .Max();
                if (iscr.NumeroEventiCarrieraPregressa == 0)
                    iscr.UltimoAnnoAvvenimentoCarrieraPregressa = null;

                iscr.TotaleCreditiCarrieraPregressa = items.Sum(x => x.NumeroCrediti ?? 0m);
                iscr.HaPassaggioCorsoEsteroCarrieraPregressa = items.Any(x => x.PassaggioCorsoEstero != 0) ? 1 : 0;
                iscr.HaRipetenzaCarrieraPregressa = items.Any(x => x.Ripetente != 0) ? 1 : 0;
                iscr.CodiciAvvenimentoCarrieraPregressa = string.Join("|", items
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

        private static bool TryParseInt(string? value, out int parsed)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }
    }
}
