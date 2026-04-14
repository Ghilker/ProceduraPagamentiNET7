using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string EsitoBorsaForzatureSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CONVERT(NVARCHAR(20), f.COD_FORZATURA) AS CodForzatura
FROM {TEMP_TABLE} t
JOIN FORZATURE f
  ON f.NUM_DOMANDA = CAST(t.NumDomanda AS INT)
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL
  AND TRY_CONVERT(INT, f.COD_FORZATURA) > 200;";

        private const string EsitoBorsaForzatureRinunciaSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT DISTINCT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale
FROM {TEMP_TABLE} t
JOIN FORZATURE_RINUNCIA f
  ON UPPER(LTRIM(RTRIM(f.COD_FISCALE))) = UPPER(LTRIM(RTRIM(t.CodFiscale)))
WHERE f.ANNO_ACCADEMICO = @AA
  AND f.DATA_FINE_VALIDITA IS NULL;";

        private const string EsitoBorsaCodTipoOrdinamentoSql = @"
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
        CONVERT(NVARCHAR(50), i.Cod_tipo_ordinamento) AS Cod_tipo_ordinamento,
        ROW_NUMBER() OVER (PARTITION BY D.NumDomanda ORDER BY COALESCE(CONVERT(NVARCHAR(50), i.Cod_tipo_ordinamento),'')) AS rn
    FROM D
    LEFT JOIN vIscrizioni i
      ON i.Anno_accademico = @AA
     AND i.Cod_fiscale = D.CodFiscale
     AND COALESCE(i.tipo_bando,'') = D.TipoBando
)
SELECT NumDomanda, CodFiscale, ISNULL(Cod_tipo_ordinamento,'') AS Cod_tipo_ordinamento
FROM ISCR
WHERE rn = 1;";

        private static readonly string[] FactFirmataCandidates = { "Firmata", "firmata" };
        private static readonly string[] FactFotocopiaCandidates = { "Fotocopia", "Fotocopia_documento", "fotocopia" };
        private static readonly string[] FactCartaceoCandidates = { "CartaceoInviato", "Cartaceo_inviato", "cartaceo_inviato" };
        private static readonly string[] FactFuoriTermineCandidates = { "Fuori_termine", "FuoriTermine", "isFuoriTermine" };
        private static readonly string[] FactIscrFuoriTermineCandidates = { "Iscrizione_fuori_termine", "IscrizioneFuoriTermine", "isIscrizioneFuoriTermine" };
        private static readonly string[] FactDocConsolareCandidates = { "Doc_Consolare", "DocConsolare", "doc_consolare" };
        private static readonly string[] FactPermessoCandidates = { "Permesso_Sogg", "PermessoSogg", "Permesso_di_soggiorno", "permesso_sogg" };
        private static readonly string[] FactRinunciaBeneficiCandidates = { "RinunciaBenefici", "Rinuncia_benefici", "rinuncia_benefici" };
        private static readonly string[] FactDomandaTrasmessaCandidates = { "DomandaTrasmessa", "Domanda_trasmessa", "domanda_trasmessa", "Trasmessa" };
        private static readonly string[] FactDomandaPinCandidates = { "DomandaTrasmessa_PIN", "Domanda_trasmessa_pin", "Trasmessa_pin" };
        private static readonly string[] FactConfermaRedditoCandidates = { "Conferma_Reddito", "ConfermaReddito", "conferma_reddito" };
        private static readonly string[] FactStatusIseeCandidates = { "StatusIsee", "Status_Isee", "status_isee" };
        private static readonly string[] FactTipoCertificazioneCandidates = { "tipo_certificazione", "Tipo_certificazione", "TipoCertificazione" };
        private static readonly string[] FactTitoloConseguitoCandidates = { "TitoloAccademicoConseguito", "Titolo_accademico_conseguito", "titolo_accademico_conseguito" };
        private static readonly string[] FactAttesaTitoloCandidates = { "Attesa_TitoloAccademicoConseguito", "Attesa_titolo_accademico_conseguito", "attesa_titolo_accademico_conseguito" };
        private static readonly string[] FactConfermaCandidates = { "isConferma", "Conferma", "is_conferma", "conferma" };
        private static readonly string[] FactStranieroCandidates = { "Straniero", "straniero" };
        private static readonly string[] FactCittadinanzaUeCandidates = { "CittadinanzaUE", "Cittadinanza_Ue", "Cittadinanza_UE", "cittadinanza_ue" };
        private static readonly string[] FactResidenzaUeCandidates = { "ResidenzaUE", "Residenza_Ue", "Residenza_UE", "residenza_ue" };
        private static readonly string[] FactFamigliaItaliaCandidates = { "isFamigliaResidenteItalia", "Famiglia_residente_italia", "famiglia_residente_italia" };
        private static readonly string[] FactTipologiaTitoloCandidates = { "TipologiaStudi_TitConseguito", "Tipologia_studi_tit_conseguito", "tipologia_studi_tit_conseguito" };
        private static readonly string[] FactDurataTitoloCandidates = { "Durata_leg_titolo_conseguito", "Durata_Leg_Titolo_Conseguito", "durata_leg_titolo_conseguito" };
        private static readonly string[] MeritoPassaggioTrasferimentoCandidates = { "Passaggio_trasferimento", "PassaggioTrasferimento", "passaggio_trasferimento" };
        private static readonly string[] MeritoRipetenteDaPassaggioCandidates = { "Ripetente_da_passaggio", "RipetenteDaPassaggio", "ripetente_da_passaggio" };
        private static readonly string[] MeritoPassaggioVecchioNuovoCandidates = { "Passaggio_vecchio_nuovo", "PassaggioVecchioNuovo", "passaggio_vecchio_nuovo" };
        private static readonly string[] MeritoEsameComplementareCandidates = { "Esame_complementare", "EsameComplementare", "esame_complementare" };

        private static readonly string[] CarrieraTitoloAvvenimentoMarkers =
        {
            "CONSEG", "LAUR", "DIPL", "TITOLO", "ABIL"
        };

        private void LoadEsitoBorsaSupportFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.EsitoBorsaFactsByStudent.Clear();
            foreach (var key in context.Students.Keys)
                context.EsitoBorsaFactsByStudent[key] = new EsitoBorsaFacts();

            LoadEsitoBorsaForzature(context);
            LoadEsitoBorsaForzatureRinuncia(context);
            LoadEsitoBorsaCodTipoOrdinamento(context);
            LoadEsitoBorsaMeritoFlags(context);
            LoadEsitoBorsaGeneralFacts(context);
            LoadEsitoBorsaRedditoUeFacts(context);
            LoadEsitoBorsaLaureaSpecFacts(context);
            BuildEsitoBorsaPregressaFacts(context);
            BuildEsitoBorsaGeneralFactsFromCarrieraPregressa(context);
        }

        private void LoadEsitoBorsaForzature(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                string code = reader.SafeGetString("CodForzatura").Trim();
                if (!string.IsNullOrWhiteSpace(code))
                    facts.ForzatureGenerali.Add(code);
            });
        }

        private void LoadEsitoBorsaForzatureRinuncia(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaForzatureRinunciaSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.ForzaturaRinunciaNoEsclusione = true;
            });
        }

        private void LoadEsitoBorsaCodTipoOrdinamento(VerificaPipelineContext context)
        {
            using var cmd = CreatePopulationCommand(EsitoBorsaCodTipoOrdinamentoSql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.CodTipoOrdinamento = reader.SafeGetString("Cod_tipo_ordinamento").Trim();
            });
        }

        private void LoadEsitoBorsaMeritoFlags(VerificaPipelineContext context)
        {
            var columns = GetObjectColumns(context.Connection, "vMerito");
            if (columns.Count == 0)
                return;

            string sql = $@"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    {BuildNullableBitExpression("m", columns, MeritoPassaggioTrasferimentoCandidates)} AS PassaggioTrasferimento,
    {BuildNullableBitExpression("m", columns, MeritoRipetenteDaPassaggioCandidates)} AS RipetenteDaPassaggio,
    {BuildNullableBitExpression("m", columns, MeritoPassaggioVecchioNuovoCandidates)} AS PassaggioVecchioNuovo,
    {BuildNullableBitExpression("m", columns, MeritoEsameComplementareCandidates)} AS EsameComplementare
FROM {{TEMP_TABLE}} t
LEFT JOIN vMerito m
  ON m.Anno_accademico = @AA
 AND m.Num_domanda = t.NumDomanda;";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.PassaggioTrasferimento = GetNullableBool(reader, "PassaggioTrasferimento");
                facts.RipetenteDaPassaggio = GetNullableBool(reader, "RipetenteDaPassaggio");
                facts.PassaggioVecchioNuovo = GetNullableBool(reader, "PassaggioVecchioNuovo");
                facts.EsameComplementare = GetNullableBool(reader, "EsameComplementare");
                if (info?.InformazioniIscrizione != null)
                    facts.CodTipoOrdinamentoPassaggio = info.InformazioniIscrizione.CodTipoOrdinamentoPassaggio ?? string.Empty;
            });
        }

        private void LoadEsitoBorsaGeneralFacts(VerificaPipelineContext context)
        {
            var columns = GetObjectColumns(context.Connection, "vDATIGENERALI_dom");
            if (columns.Count == 0)
                return;

            string sql = $@"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    {BuildNullableBitExpression("v", columns, FactCartaceoCandidates)} AS CartaceoInviato,
    {BuildNullableBitExpression("v", columns, FactFirmataCandidates)} AS Firmata,
    {BuildNullableBitExpression("v", columns, FactFotocopiaCandidates)} AS FotocopiaDocumento,
    {BuildNullableBitExpression("v", columns, FactFuoriTermineCandidates)} AS FuoriTermine,
    {BuildNullableBitExpression("v", columns, FactIscrFuoriTermineCandidates)} AS IscrizioneFuoriTermine,
    {BuildNullableBitExpression("v", columns, FactDocConsolareCandidates)} AS DocConsolare,
    {BuildNullableBitExpression("v", columns, FactPermessoCandidates)} AS PermessoSoggiorno,
    {BuildNullableBitExpression("v", columns, FactRinunciaBeneficiCandidates)} AS RinunciaBenefici,
    {BuildNullableBitExpression("v", columns, FactDomandaTrasmessaCandidates)} AS DomandaTrasmessa,
    {BuildNullableBitExpression("v", columns, FactDomandaPinCandidates)} AS DomandaTrasmessaPin,
    {BuildNullableBitExpression("v", columns, FactConfermaRedditoCandidates)} AS ConfermaReddito,
    {BuildNullableIntExpression("v", columns, FactStatusIseeCandidates)} AS StatusIsee,
    {BuildNullableStringExpression("v", columns, FactTipoCertificazioneCandidates)} AS TipoCertificazione,
    {BuildNullableBitExpression("v", columns, FactTitoloConseguitoCandidates)} AS TitoloAccademicoConseguito,
    {BuildNullableBitExpression("v", columns, FactAttesaTitoloCandidates)} AS AttesaTitoloAccademicoConseguito,
    {BuildNullableBitExpression("v", columns, FactConfermaCandidates)} AS IsConferma,
    {BuildNullableBitExpression("v", columns, FactStranieroCandidates)} AS Straniero,
    {BuildNullableBitExpression("v", columns, FactCittadinanzaUeCandidates)} AS CittadinanzaUe,
    {BuildNullableBitExpression("v", columns, FactResidenzaUeCandidates)} AS ResidenzaUe,
    {BuildNullableBitExpression("v", columns, FactFamigliaItaliaCandidates)} AS FamigliaResidenteItalia,
    {BuildNullableIntExpression("v", columns, FactTipologiaTitoloCandidates)} AS TipologiaStudiTitoloConseguito,
    {BuildNullableIntExpression("v", columns, FactDurataTitoloCandidates)} AS DurataLegTitoloConseguito
FROM {{TEMP_TABLE}} t
LEFT JOIN vDATIGENERALI_dom v
  ON v.Num_domanda = t.NumDomanda
 AND v.Anno_accademico = @AA;";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);

                facts.CartaceoInviato = GetNullableBool(reader, "CartaceoInviato");
                facts.Firmata = GetNullableBool(reader, "Firmata");
                facts.FotocopiaDocumento = GetNullableBool(reader, "FotocopiaDocumento");
                facts.FuoriTermine = GetNullableBool(reader, "FuoriTermine");
                facts.IscrizioneFuoriTermine = GetNullableBool(reader, "IscrizioneFuoriTermine");
                facts.DocConsolare = GetNullableBool(reader, "DocConsolare");
                facts.PermessoSoggiorno = GetNullableBool(reader, "PermessoSoggiorno");
                facts.RinunciaBenefici = GetNullableBool(reader, "RinunciaBenefici");
                facts.DomandaTrasmessa = GetNullableBool(reader, "DomandaTrasmessa");
                facts.DomandaTrasmessaPin = GetNullableBool(reader, "DomandaTrasmessaPin");
                facts.ConfermaReddito = GetNullableBool(reader, "ConfermaReddito");
                facts.StatusIsee = GetNullableInt(reader, "StatusIsee");
                facts.TipoCertificazione = reader.SafeGetString("TipoCertificazione").Trim();
                facts.TitoloAccademicoConseguito = GetNullableBool(reader, "TitoloAccademicoConseguito");
                facts.AttesaTitoloAccademicoConseguito = GetNullableBool(reader, "AttesaTitoloAccademicoConseguito");
                facts.IsConferma = GetNullableBool(reader, "IsConferma");
                facts.Straniero = GetNullableBool(reader, "Straniero");
                facts.CittadinanzaUe = GetNullableBool(reader, "CittadinanzaUe");
                facts.ResidenzaUe = GetNullableBool(reader, "ResidenzaUe");
                facts.FamigliaResidenteItalia = GetNullableBool(reader, "FamigliaResidenteItalia");
                facts.TipologiaStudiTitoloConseguito = GetNullableInt(reader, "TipologiaStudiTitoloConseguito");
                facts.DurataLegTitoloConseguito = GetNullableInt(reader, "DurataLegTitoloConseguito");
            });
        }

        private void LoadEsitoBorsaRedditoUeFacts(VerificaPipelineContext context)
        {
            if (!ObjectExists(context.Connection, "vNucleo_fam_stranieri_DO") || !ObjectExists(context.Connection, "Cittadinanze_Ue"))
                return;

            string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    CAST(t.NumDomanda AS INT) AS NumDomanda,
    t.CodFiscale,
    CAST(
        CASE WHEN EXISTS
        (
            SELECT 1
            FROM vNucleo_fam_stranieri_DO n
            INNER JOIN Cittadinanze_Ue cu
                ON cu.codice = n.Cod_stato_dic
            WHERE n.Anno_accademico = @AA
              AND n.Num_domanda = CAST(t.NumDomanda AS INT)
        )
        THEN 1 ELSE 0 END
    AS BIT) AS RedditoUe
FROM {TEMP_TABLE} t;";

            using var cmd = CreatePopulationCommand(sql, context);
            ReadAndMergeByStudentKey(cmd, (reader, info) =>
            {
                var key = CreateStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);
                var facts = GetOrCreateEsitoBorsaFacts(context, key);
                facts.RedditoUe = GetNullableBool(reader, "RedditoUe") ?? false;
            });
        }

        private void LoadEsitoBorsaLaureaSpecFacts(VerificaPipelineContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!ObjectExists(context.Connection, "CONTROLLO_SPECIALISTICA"))
                return;

            const string sql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT
    UPPER(LTRIM(RTRIM(ISNULL(COD_ENTE,'')))) AS CodEnte,
    UPPER(LTRIM(RTRIM(ISNULL(COD_CORSO_LAUREA,'')))) AS CodCorsoLaurea
FROM CONTROLLO_SPECIALISTICA
WHERE ANNO_ACCADEMICO = @AA
  AND DATA_FINE_VALIDITA IS NULL;";

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(sql, context.Connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            })
            {
                AddAaParameter(cmd, context.AnnoAccademico);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string ente = reader.SafeGetString("CodEnte").Trim().ToUpperInvariant();
                    string corso = reader.SafeGetString("CodCorsoLaurea").Trim().ToUpperInvariant();
                    if (ente.Length == 0 || corso.Length == 0)
                        continue;

                    allowed.Add($"{ente}|{corso}");
                }
            }

            if (allowed.Count == 0)
                return;

            foreach (var pair in context.Students)
            {
                var iscr = pair.Value?.InformazioniIscrizione;
                if (iscr == null)
                    continue;

                string ente = (iscr.CodEnte ?? string.Empty).Trim().ToUpperInvariant();
                string corso = (iscr.CodCorsoLaurea ?? string.Empty).Trim().ToUpperInvariant();
                if (ente.Length == 0 || corso.Length == 0)
                    continue;

                if (!allowed.Contains($"{ente}|{corso}"))
                    continue;

                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                facts.RichiedeControlloLaureaSpec = true;
            }
        }

        private void BuildEsitoBorsaPregressaFacts(VerificaPipelineContext context)
        {
            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                facts.UsufruitoBeneficioBorsaNonRestituito = false;
                facts.RinunciaBorsa = false;

                var items = info.InformazioniIscrizione?.CarrierePregresse;
                if (items == null || items.Count == 0)
                    continue;

                foreach (var item in items)
                {
                    string benefici = NormalizeUpper(item?.BeneficiUsufruiti);
                    string restituzioni = NormalizeUpper(item?.ImportiRestituiti);
                    string codAvvenimento = NormalizeUpper(item?.CodAvvenimento);

                    bool hasBorsa = HasBorsaMarker(benefici);
                    bool hasRestituzione = HasMeaningfulRestitution(restituzioni);

                    if (hasBorsa && !hasRestituzione)
                        facts.UsufruitoBeneficioBorsaNonRestituito = true;

                    if (IsRinunciaBorsa(codAvvenimento, benefici))
                        facts.RinunciaBorsa = true;
                }
            }
        }

        private void BuildEsitoBorsaGeneralFactsFromCarrieraPregressa(VerificaPipelineContext context)
        {
            foreach (var pair in context.Students)
            {
                var info = pair.Value;
                var facts = GetOrCreateEsitoBorsaFacts(context, pair.Key);
                var items = info.InformazioniIscrizione?.CarrierePregresse;
                if (items == null || items.Count == 0)
                    continue;

                InformazioniCarrieraPregressa? best = null;
                int bestYear = int.MinValue;

                foreach (var item in items)
                {
                    if (item == null)
                        continue;

                    bool looksLikeTitle = LooksLikeCareerTitleRecord(item);
                    if (!looksLikeTitle)
                        continue;

                    int year = item.AnnoAvvenimento ?? int.MinValue;
                    if (best == null || year > bestYear)
                    {
                        best = item;
                        bestYear = year;
                    }
                }

                if (best == null)
                    continue;

                if (!facts.TitoloAccademicoConseguito.HasValue)
                    facts.TitoloAccademicoConseguito = true;

                if (!facts.TipologiaStudiTitoloConseguito.HasValue && TryParseCareerTitleType(best.TipologiaCorso, out int tipologiaTitolo))
                    facts.TipologiaStudiTitoloConseguito = tipologiaTitolo;

                if (!facts.DurataLegTitoloConseguito.HasValue && best.DurataLegTitoloConseguito.HasValue)
                    facts.DurataLegTitoloConseguito = best.DurataLegTitoloConseguito.Value;
            }
        }

        private static bool LooksLikeCareerTitleRecord(InformazioniCarrieraPregressa item)
        {
            if (item == null)
                return false;

            if (item.DurataLegTitoloConseguito.HasValue && item.DurataLegTitoloConseguito.Value > 0)
                return true;

            string code = NormalizeUpper(item.CodAvvenimento);
            foreach (string marker in CarrieraTitoloAvvenimentoMarkers)
            {
                if (code.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryParseCareerTitleType(string? value, out int tipologia)
        {
            tipologia = 0;
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tipologia);
        }

        private static bool IsRinunciaBorsa(string codAvvenimento, string benefici)
        {
            bool rinuncia = codAvvenimento.Contains("RIN", StringComparison.OrdinalIgnoreCase)
                            || benefici.Contains("RINUNC", StringComparison.OrdinalIgnoreCase);
            return rinuncia && (HasBorsaMarker(benefici) || codAvvenimento.Contains("BS", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasBorsaMarker(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return Regex.IsMatch(value, @"(^|[^A-Z])(BS|BORSA)([^A-Z]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                   || value.Contains("BORSA DI STUDIO", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasMeaningfulRestitution(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (char ch in value)
            {
                if (ch >= '1' && ch <= '9')
                    return true;
            }

            return value.Contains("RESTIT", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUpper(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();

        private static EsitoBorsaFacts GetOrCreateEsitoBorsaFacts(VerificaPipelineContext context, StudentKey key)
        {
            if (!context.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts) || facts == null)
            {
                facts = new EsitoBorsaFacts();
                context.EsitoBorsaFactsByStudent[key] = facts;
            }

            return facts;
        }

        private static HashSet<string> GetObjectColumns(SqlConnection connection, string objectName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (connection == null || string.IsNullOrWhiteSpace(objectName))
                return result;

            const string sql = @"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.name = @ObjectName
  AND o.type IN ('U','V');";

            using var cmd = new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };
            cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 128).Value = objectName.Trim();

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(Convert.ToString(reader[0], CultureInfo.InvariantCulture) ?? string.Empty);

            return result;
        }

        private static bool ObjectExists(SqlConnection connection, string objectName)
        {
            if (connection == null || string.IsNullOrWhiteSpace(objectName))
                return false;

            const string sql = @"
SELECT TOP (1) 1
FROM sys.objects
WHERE name = @ObjectName
  AND type IN ('U','V');";

            using var cmd = new SqlCommand(sql, connection)
            {
                CommandType = CommandType.Text,
                CommandTimeout = 9999999
            };
            cmd.Parameters.Add("@ObjectName", SqlDbType.NVarChar, 128).Value = objectName.Trim();
            object? value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        private static string BuildNullableBitExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS BIT)";

            return $@"CAST(
CASE
    WHEN TRY_CONVERT(INT, {alias}.[{column}]) = 1 THEN 1
    WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), {alias}.[{column}] )))) IN ('1','TRUE','T','S','SI','Y','YES') THEN 1
    WHEN TRY_CONVERT(INT, {alias}.[{column}]) = 0 THEN 0
    WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), {alias}.[{column}] )))) IN ('0','FALSE','F','N','NO') THEN 0
    ELSE NULL
END
AS BIT)";
        }

        private static string BuildNullableIntExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS INT)";

            return $"TRY_CONVERT(INT, {alias}.[{column}])";
        }

        private static string BuildNullableStringExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS NVARCHAR(100))";

            return $"NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(100), {alias}.[{column}] ))), '')";
        }

        private static string BuildNullableDecimalExpression(string alias, HashSet<string> columns, params string[] candidates)
        {
            string? column = GetFirstAvailableColumn(columns, candidates);
            if (string.IsNullOrWhiteSpace(column))
                return "CAST(NULL AS DECIMAL(18,4))";

            return $"TRY_CONVERT(DECIMAL(18,4), {alias}.[{column}])";
        }

        private static string? GetFirstAvailableColumn(HashSet<string> columns, params string[] candidates)
        {
            if (columns == null || candidates == null)
                return null;

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && columns.Contains(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool? GetNullableBool(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            object value = reader.GetValue(ordinal);
            if (value is bool b)
                return b;

            if (value is byte by)
                return by != 0;

            if (value is short sh)
                return sh != 0;

            if (value is int i)
                return i != 0;

            if (value is long l)
                return l != 0;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("si", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("s", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("y", StringComparison.OrdinalIgnoreCase))
                return true;

            if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("n", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private static int? GetNullableInt(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(ordinal))
                return null;

            object value = reader.GetValue(ordinal);
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed
                    : null;
            }
        }
    }
}
