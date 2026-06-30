using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    /*
        Prerequisito SQL Server, da eseguire una sola volta sul database:

        CREATE TYPE dbo.FiscalCodeList AS TABLE
        (
            Cod_fiscale VARCHAR(16) NOT NULL PRIMARY KEY
        );

        La query mantiene dbo.SlashDescrBlocchi perché il relativo schema sorgente
        non è disponibile in questo file. Per eliminare anche quell'ultimo costo
        per-riga, sostituire la funzione con una CTE/inline table-valued function.
    */

    internal sealed class TicketOfficeRecord
    {
        public string FiscalCode { get; set; } = string.Empty;
        public int AcademicYear { get; set; }
        public string ApplicationNumber { get; set; } = string.Empty;
        public int CompilationStatus { get; set; }
        public string BsOutcome { get; set; } = string.Empty;
        public string PaOutcome { get; set; } = string.Empty;
        public string CiOutcome { get; set; } = string.Empty;
        public decimal CiAmount { get; set; }
        public decimal BenefitAmount { get; set; }
        public decimal SpecificheAmount { get; set; }
        // I seguenti valori includono esclusivamente movimenti con Cod_tipo_pagam che inizia con "BS".
        public decimal BsPaidAmount { get; set; }
        public decimal BsReversalAmount { get; set; }
        public decimal DeductionAmount { get; set; }
        public string BsPaymentTypes { get; set; } = string.Empty;
        public string BsMandates { get; set; } = string.Empty;
        public string BsFirstInstallmentMandates { get; set; } = string.Empty;
        public string BsBalanceMandates { get; set; } = string.Empty;
        public string BsIntegrationMandates { get; set; } = string.Empty;
        public string BsFinancialYears { get; set; } = string.Empty;
        public string BsReversalTypes { get; set; } = string.Empty;

        // Pagamenti BS validi: Ritirato_azienda diverso da 1. I movimenti ritirati
        // sono esposti separatamente e non concorrono a importi ricevuti o residui.
        public decimal BsFirstInstallmentOriginalAmount { get; set; }       // BSP0
        public decimal BsFirstInstallmentReissueAmount { get; set; }        // BSP1, BSP2
        public decimal BsBalanceOriginalAmount { get; set; }                // BSS0
        public decimal BsBalanceReissueAmount { get; set; }                 // BSS1, BSS2
        public decimal BsIntegrationFirstInstallmentOriginalAmount { get; set; } // BSI0
        public decimal BsIntegrationFirstInstallmentReissueAmount { get; set; }  // BSI1, BSI2
        public decimal BsIntegrationBalanceOriginalAmount { get; set; }    // BSI9
        public decimal BsIntegrationBalanceReissueAmount { get; set; }     // BSIA, BSIB
        public decimal BsStornedAmount { get; set; }
        public string BsStornedPaymentTypes { get; set; } = string.Empty;
        public string BsStornedMandates { get; set; } = string.Empty;
        public decimal BsUnclassifiedAmount { get; set; }
        public string BsUnclassifiedPaymentTypes { get; set; } = string.Empty;

        public bool HasBsStornedPayments =>
            BsStornedAmount != 0m || !string.IsNullOrWhiteSpace(BsStornedPaymentTypes);

        public bool HasUnclassifiedBsPaymentCodes =>
            BsUnclassifiedAmount != 0m || !string.IsNullOrWhiteSpace(BsUnclassifiedPaymentTypes);

        public string FirstInstallmentCommitment { get; set; } = string.Empty;
        public string BalanceCommitment { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string Iban { get; set; } = string.Empty;
        public DateTime? IbanDataValidita { get; set; }
        public string Swift { get; set; } = string.Empty;
        public bool ForeignTransfer { get; set; }
        public string Blocks { get; set; } = string.Empty;
        public int CourseYear { get; set; }
        public string StudyType { get; set; } = string.Empty;
        public string DegreeCourse { get; set; } = string.Empty;
        public string StudyLocation { get; set; } = string.Empty;
        public int EnrollmentYear { get; set; }
        public int ExamCount { get; set; }
        public decimal CreditCount { get; set; }
        public decimal RecognizedCredits { get; set; }
        public int PreviousCareerEvents { get; set; }
        public decimal PreviousCareerCredits { get; set; }
        public string PreviousCareerCodes { get; set; } = string.Empty;
        public bool PreviousBenefitsUsed { get; set; }
        public bool PreviousAmountsReturned { get; set; }
        public decimal Isee { get; set; }
        public decimal Ispe { get; set; }
        public string IncomeSourceType { get; set; } = string.Empty;
        public string IncomeIntegrationType { get; set; } = string.Empty;
        public string CampusStatus { get; set; } = string.Empty;
        public string DomicileMunicipality { get; set; } = string.Empty;
        public string ContractSeries { get; set; } = string.Empty;
        public string ContractStart { get; set; } = string.Empty;
        public string ContractEnd { get; set; } = string.Empty;
        public bool ContractExtended { get; set; }
        public bool HasOpenDomicileRequest { get; set; }
        public bool HasWorkedDomicileRequest { get; set; }
        public string LastClosedDomicileOutcome { get; set; } = string.Empty;
        public bool CanteenMonetizationGranted { get; set; }
        public string ResidencePermitDocuments { get; set; } = string.Empty;
        public string ProvisionalRankings { get; set; } = string.Empty;
        public string FinalRankings { get; set; } = string.Empty;
        public bool MobilityBenefitRequested { get; set; }
    }

    internal static class TicketOfficeDataService
    {
        private const int BatchSize = 1000;
        private const string FiscalCodeListTypeName = "dbo.FiscalCodeList";

        public static Dictionary<string, List<TicketOfficeRecord>> Load(
            List<string> fiscalCodes,
            SqlConnection connection)
        {
            if (fiscalCodes == null)
                throw new ArgumentNullException(nameof(fiscalCodes));

            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            var result = new Dictionary<string, List<TicketOfficeRecord>>(
                StringComparer.OrdinalIgnoreCase);

            List<string> normalizedFiscalCodes = fiscalCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedFiscalCodes.Count == 0)
                return result;

            Logger.LogInfo(
                null,
                $"Avvio estrazione dati operativi VERIFICA/PAGAMENTI per {normalizedFiscalCodes.Count} studenti");

            for (int offset = 0; offset < normalizedFiscalCodes.Count; offset += BatchSize)
            {
                List<string> batch = normalizedFiscalCodes
                    .Skip(offset)
                    .Take(BatchSize)
                    .ToList();

                LoadBatch(batch, connection, result);

                Logger.LogInfo(
                    null,
                    $"Dati operativi estratti: {Math.Min(offset + batch.Count, normalizedFiscalCodes.Count)}/{normalizedFiscalCodes.Count} studenti");
            }

            foreach (List<TicketOfficeRecord> records in result.Values)
            {
                records.Sort((left, right) =>
                {
                    int yearComparison = right.AcademicYear.CompareTo(left.AcademicYear);
                    return yearComparison != 0
                        ? yearComparison
                        : string.Compare(
                            right.ApplicationNumber,
                            left.ApplicationNumber,
                            StringComparison.OrdinalIgnoreCase);
                });
            }

            Logger.LogInfo(
                null,
                $"Estrazione dati operativi completata. Studenti trovati: {result.Count}; domande: {result.Values.Sum(items => items.Count)}");

            return result;
        }

        private static void LoadBatch(
            IReadOnlyCollection<string> batch,
            SqlConnection connection,
            Dictionary<string, List<TicketOfficeRecord>> result)
        {
            if (batch.Count == 0)
                return;

            bool reopen = connection.State != ConnectionState.Open;
            if (reopen)
                connection.Open();

            try
            {
                using var command = new SqlCommand(OptimizedQuery, connection)
                {
                    CommandTimeout = 600
                };

                SqlParameter fiscalCodesParameter = command.Parameters.Add(
                    "@FiscalCodes",
                    SqlDbType.Structured);
                fiscalCodesParameter.TypeName = FiscalCodeListTypeName;
                fiscalCodesParameter.Value = CreateFiscalCodeTable(batch);

                using SqlDataReader reader = command.ExecuteReader();
                IReadOnlyDictionary<string, int> ordinals = CreateOrdinals(reader);

                while (reader.Read())
                {
                    TicketOfficeRecord record = Map(reader, ordinals);

                    if (!result.TryGetValue(record.FiscalCode, out List<TicketOfficeRecord>? records))
                    {
                        records = new List<TicketOfficeRecord>();
                        result[record.FiscalCode] = records;
                    }

                    records.Add(record);
                }
            }
            catch (SqlException ex)
            {
                Logger.LogError(
                    null,
                    "Estrazione dati operativi VERIFICA/PAGAMENTI interrotta per il batch: " + ex.Message);
                throw;
            }
            finally
            {
                if (reopen && connection.State == ConnectionState.Open)
                    connection.Close();
            }
        }

        private static DataTable CreateFiscalCodeTable(IEnumerable<string> fiscalCodes)
        {
            var table = new DataTable();
            table.Columns.Add("Cod_fiscale", typeof(string));

            foreach (string fiscalCode in fiscalCodes)
                table.Rows.Add(fiscalCode);

            return table;
        }

        private static IReadOnlyDictionary<string, int> CreateOrdinals(SqlDataReader reader)
        {
            var result = new Dictionary<string, int>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < reader.FieldCount; index++)
                result[reader.GetName(index)] = index;

            return result;
        }

        private static TicketOfficeRecord Map(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals) => new()
            {
                FiscalCode = ReadString(reader, ordinals, "Cod_fiscale"),
                AcademicYear = ReadInt(reader, ordinals, "Anno_accademico"),
                ApplicationNumber = ReadString(reader, ordinals, "Num_domanda"),
                CompilationStatus = ReadInt(reader, ordinals, "StatusCompilazione"),
                BsOutcome = ReadString(reader, ordinals, "EsitoBS"),
                PaOutcome = ReadString(reader, ordinals, "EsitoPA"),
                CiOutcome = ReadString(reader, ordinals, "EsitoCI"),
                CiAmount = ReadDecimal(reader, ordinals, "ImportoCI"),
                BenefitAmount = ReadDecimal(reader, ordinals, "ImportoBeneficio"),
                SpecificheAmount = ReadDecimal(reader, ordinals, "ImportoSpecifiche"),
                BsPaidAmount = ReadDecimal(reader, ordinals, "ImportoPagatoBS"),
                BsPaymentTypes = ReadString(reader, ordinals, "TipiPagamentoBS"),
                BsMandates = ReadString(reader, ordinals, "MandatiBS"),
                BsFirstInstallmentMandates = ReadString(reader, ordinals, "MandatiPrimaRataBS"),
                BsBalanceMandates = ReadString(reader, ordinals, "MandatiSaldoBS"),
                BsIntegrationMandates = ReadString(reader, ordinals, "MandatiIntegrazioniBS"),
                BsFinancialYears = ReadString(reader, ordinals, "EserciziFinanziariBS"),
                BsReversalAmount = ReadDecimal(reader, ordinals, "ImportoReversaliBS"),
                BsReversalTypes = ReadString(reader, ordinals, "TipiReversaleBS"),
                BsFirstInstallmentOriginalAmount = ReadDecimal(reader, ordinals, "ImportoBsp0"),
                BsFirstInstallmentReissueAmount = ReadDecimal(reader, ordinals, "ImportoBspRiemissioni"),
                BsBalanceOriginalAmount = ReadDecimal(reader, ordinals, "ImportoBss0"),
                BsBalanceReissueAmount = ReadDecimal(reader, ordinals, "ImportoBssRiemissioni"),
                BsIntegrationFirstInstallmentOriginalAmount = ReadDecimal(reader, ordinals, "ImportoBsi0"),
                BsIntegrationFirstInstallmentReissueAmount = ReadDecimal(reader, ordinals, "ImportoBsiRiemissioniPrimaRata"),
                BsIntegrationBalanceOriginalAmount = ReadDecimal(reader, ordinals, "ImportoBsi9"),
                BsIntegrationBalanceReissueAmount = ReadDecimal(reader, ordinals, "ImportoBsiRiemissioniSaldo"),
                BsStornedAmount = ReadDecimal(reader, ordinals, "ImportoPagamentiBSStornati"),
                BsStornedPaymentTypes = ReadString(reader, ordinals, "TipiPagamentoBSStornati"),
                BsStornedMandates = ReadString(reader, ordinals, "MandatiBSStornati"),
                BsUnclassifiedAmount = ReadDecimal(reader, ordinals, "ImportoBSNonClassificato"),
                BsUnclassifiedPaymentTypes = ReadString(reader, ordinals, "TipiPagamentoBSNonClassificati"),
                DeductionAmount = ReadDecimal(reader, ordinals, "ImportoDetrazioni"),
                FirstInstallmentCommitment = ReadString(reader, ordinals, "ImpegnoPrimaRata"),
                BalanceCommitment = ReadString(reader, ordinals, "ImpegnoSaldo"),
                PaymentMethod = ReadString(reader, ordinals, "ModalitaPagamento"),
                Iban = ReadString(reader, ordinals, "IBAN"),
                IbanDataValidita = ReadNullableDate(reader, ordinals, "IbanDataValidita"),
                Swift = ReadString(reader, ordinals, "Swift"),
                ForeignTransfer = ReadBool(reader, ordinals, "BonificoEstero"),
                Blocks = ReadString(reader, ordinals, "Blocchi"),
                CourseYear = ReadInt(reader, ordinals, "AnnoCorso"),
                StudyType = ReadString(reader, ordinals, "TipoStudi"),
                DegreeCourse = ReadString(reader, ordinals, "CorsoLaurea"),
                StudyLocation = ReadString(reader, ordinals, "SedeStudi"),
                EnrollmentYear = ReadInt(reader, ordinals, "AnnoImmatricolazione"),
                ExamCount = ReadInt(reader, ordinals, "NumeroEsami"),
                CreditCount = ReadDecimal(reader, ordinals, "NumeroCrediti"),
                RecognizedCredits = ReadDecimal(reader, ordinals, "CreditiRiconosciuti"),
                PreviousCareerEvents = ReadInt(reader, ordinals, "NumeroEventiCarrieraPregressa"),
                PreviousCareerCredits = ReadDecimal(reader, ordinals, "CreditiCarrieraPregressa"),
                PreviousCareerCodes = ReadString(reader, ordinals, "CodiciCarrieraPregressa"),
                PreviousBenefitsUsed = ReadBool(reader, ordinals, "BeneficiUsufruiti"),
                PreviousAmountsReturned = ReadBool(reader, ordinals, "ImportiRestituiti"),
                Isee = ReadDecimal(reader, ordinals, "ISEEDSU"),
                Ispe = ReadDecimal(reader, ordinals, "ISPEDSU"),
                IncomeSourceType = ReadString(reader, ordinals, "TipoRedditoOrigine"),
                IncomeIntegrationType = ReadString(reader, ordinals, "TipoRedditoIntegrazione"),
                CampusStatus = ReadString(reader, ordinals, "StatusSede"),
                DomicileMunicipality = ReadString(reader, ordinals, "ComuneDomicilio"),
                ContractSeries = ReadString(reader, ordinals, "SerieContratto"),
                ContractStart = ReadString(reader, ordinals, "DataDecorrenza"),
                ContractEnd = ReadString(reader, ordinals, "DataScadenza"),
                ContractExtended = ReadBool(reader, ordinals, "Prorogato"),
                HasOpenDomicileRequest = ReadBool(reader, ordinals, "HasIstanzaDomicilioAperta"),
                HasWorkedDomicileRequest = ReadBool(reader, ordinals, "HasIstanzaDomicilioLavorata"),
                LastClosedDomicileOutcome = ReadString(reader, ordinals, "EsitoUltimaIstanzaDomicilio"),
                CanteenMonetizationGranted = ReadBool(reader, ordinals, "ConcessaMonetizzazione"),
                ResidencePermitDocuments = ReadString(reader, ordinals, "DocumentiPermesso"),
                ProvisionalRankings = ReadString(reader, ordinals, "GraduatorieProvvisorie"),
                FinalRankings = ReadString(reader, ordinals, "GraduatorieDefinitive"),
                MobilityBenefitRequested = ReadBool(reader, ordinals, "MobilitaRichiesta")
            };

        private static string ReadString(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string columnName)
        {
            int ordinal = ordinals[columnName];
            return reader.IsDBNull(ordinal)
                ? string.Empty
                : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static int ReadInt(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string columnName)
        {
            int ordinal = ordinals[columnName];
            return reader.IsDBNull(ordinal)
                ? 0
                : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static decimal ReadDecimal(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string columnName)
        {
            int ordinal = ordinals[columnName];
            return reader.IsDBNull(ordinal)
                ? 0m
                : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static bool ReadBool(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string columnName)
        {
            int ordinal = ordinals[columnName];
            return !reader.IsDBNull(ordinal) &&
                   Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static DateTime? ReadNullableDate(
            SqlDataReader reader,
            IReadOnlyDictionary<string, int> ordinals,
            string columnName)
        {
            int ordinal = ordinals[columnName];
            if (reader.IsDBNull(ordinal))
                return null;

            object value = reader.GetValue(ordinal);
            if (value is DateTime date)
                return date;

            return DateTime.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime parsed)
                ? parsed
                : null;
        }

        private const string OptimizedQuery = @"
SET NOCOUNT ON;

/*
   Materializzare il TVP consente a SQL Server di disporre di statistiche sul batch.
   Il piano non dipende dalla stima fissa delle table variable/TVP.
*/
SELECT Cod_fiscale
INTO #FiscalCodes
FROM @FiscalCodes;

CREATE UNIQUE CLUSTERED INDEX IX_FiscalCodes
    ON #FiscalCodes (Cod_fiscale);

/*
   #Domande contiene una sola domanda valida per studente e anno accademico.
   Anno_accademico mantiene il tipo fisico della tabella Domanda per evitare
   conversioni implicite sulle colonne indicizzate delle tabelle collegate;
   Anno_accademicoInt viene usato soltanto nell'output finale.
   Tutte le elaborazioni successive lavorano in modo set-based sul batch,
   invece di rieseguire una OUTER APPLY per ogni riga restituita.
*/
;WITH DomandeStoriche AS
(
    SELECT
        d.Cod_fiscale,
        d.Anno_accademico,
        d.Num_domanda,
        d.Tipo_bando,
        d.Data_validita,
        d.DataCreazioneRecord,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.Cod_fiscale, d.Anno_accademico, d.Num_domanda
            ORDER BY d.Data_validita DESC, d.DataCreazioneRecord DESC
        ) AS rnDomanda
    FROM Domanda d WITH (READUNCOMMITTED)
    INNER JOIN #FiscalCodes f
        ON f.Cod_fiscale = d.Cod_fiscale
),
DomandeValide AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        Num_domanda,
        Tipo_bando,
        Data_validita,
        DataCreazioneRecord,
        ROW_NUMBER() OVER
        (
            PARTITION BY Cod_fiscale, Anno_accademico
            ORDER BY
                CASE WHEN Tipo_bando = 'LZ' THEN 0 ELSE 1 END,
                DataCreazioneRecord DESC,
                Num_domanda DESC
        ) AS rnStudenteAnno
    FROM DomandeStoriche
    WHERE rnDomanda = 1
)
SELECT
    Cod_fiscale,
    Anno_accademico,
    TRY_CONVERT(INT, Anno_accademico) AS Anno_accademicoInt,
    Num_domanda
INTO #Domande
FROM DomandeValide
WHERE rnStudenteAnno = 1
  AND TRY_CONVERT(INT, Anno_accademico) IS NOT NULL;

CREATE CLUSTERED INDEX IX_Domande_AnnoDomanda
    ON #Domande (Anno_accademico, Num_domanda, Cod_fiscale);

CREATE INDEX IX_Domande_FiscalCodeAnno
    ON #Domande (Cod_fiscale, Anno_accademico);

;WITH
Studenti AS
(
    SELECT DISTINCT Cod_fiscale
    FROM #Domande
),
StudentiAnno AS
(
    SELECT DISTINCT Cod_fiscale, Anno_accademico
    FROM #Domande
),
StatusCompilazione AS
(
    SELECT
        d.Anno_accademico,
        d.Num_domanda,
        MAX(TRY_CONVERT(INT, v.status_compilazione)) AS StatusCompilazione
    FROM #Domande d
    INNER JOIN vStatus_compilazione v WITH (READUNCOMMITTED)
        ON v.Anno_accademico = d.Anno_accademico
       AND v.Num_domanda = d.Num_domanda
    GROUP BY d.Anno_accademico, d.Num_domanda
),
EsitiRanked AS
(
    SELECT
        e.Anno_accademico,
        e.Num_domanda,
        e.Cod_beneficio,
        CONVERT(NVARCHAR(10), e.Cod_tipo_esito) AS CodTipoEsito,
        TRY_CONVERT(DECIMAL(18, 2), e.Imp_beneficio) AS ImportoBeneficio,
        ROW_NUMBER() OVER
        (
            PARTITION BY e.Anno_accademico, e.Num_domanda, e.Cod_beneficio
            ORDER BY e.Data_validita DESC
        ) AS rn
    FROM Esiti_concorsi e WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = e.Anno_accademico
       AND d.Num_domanda = e.Num_domanda
    WHERE e.Cod_beneficio IN ('BS', 'PA', 'CI')
),
Esiti AS
(
    SELECT
        Anno_accademico,
        Num_domanda,
        MAX(CASE WHEN Cod_beneficio = 'BS' THEN CodTipoEsito END) AS EsitoBS,
        MAX(CASE WHEN Cod_beneficio = 'PA' THEN CodTipoEsito END) AS EsitoPA,
        MAX(CASE WHEN Cod_beneficio = 'CI' THEN CodTipoEsito END) AS EsitoCI,
        MAX(CASE WHEN Cod_beneficio = 'BS' THEN ImportoBeneficio END) AS ImportoBeneficio,
        MAX(CASE WHEN Cod_beneficio = 'CI' THEN ImportoBeneficio END) AS ImportoCI
    FROM EsitiRanked
    WHERE rn = 1
    GROUP BY Anno_accademico, Num_domanda
),
SpecificheRanked AS
(
    SELECT
        s.Anno_accademico,
        s.Num_domanda,
        TRY_CONVERT(DECIMAL(18, 2), s.Importo_assegnato) AS ImportoSpecifiche,
        CONVERT(NVARCHAR(50), s.num_impegno_primaRata) AS ImpegnoPrimaRata,
        CONVERT(NVARCHAR(50), s.num_impegno_saldo) AS ImpegnoSaldo,
        ROW_NUMBER() OVER
        (
            PARTITION BY s.Anno_accademico, s.Num_domanda
            ORDER BY s.Data_validita DESC
        ) AS rn
    FROM Specifiche_impegni s WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = s.Anno_accademico
       AND d.Num_domanda = s.Num_domanda
    WHERE s.Cod_beneficio = 'BS'
      AND s.Data_fine_validita IS NULL
),
Specifiche AS
(
    SELECT
        Anno_accademico,
        Num_domanda,
        ImportoSpecifiche,
        ImpegnoPrimaRata,
        ImpegnoSaldo
    FROM SpecificheRanked
    WHERE rn = 1
),
PagamentiAggregati AS
(
    /*
       Sono contati come ricevuti esclusivamente i movimenti non ritirati dall'azienda
       (Ritirato_azienda <> 1) con uno dei codici noti:
       - BSP0: prima rata; BSP1/BSP2: riemissioni prima rata
       - BSS0: saldo; BSS1/BSS2: riemissioni saldo
       - BSI0: integrazione prima rata; BSI1/BSI2: relative riemissioni
       - BSI9: integrazione saldo; BSIA/BSIB: relative riemissioni.

       I movimenti Ritirato_azienda = 1 sono stornati, restano visibili nelle colonne
       dedicate ma non entrano in ImportoPagatoBS né nei residui stimati.
       Ogni altro codice BS* resta esposto come non classificato e non entra nelle somme.
    */
    SELECT
        p.Anno_accademico,
        p.Num_domanda,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                      AND p.Cod_tipo_pagam IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoPagatoBS,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam = 'BSP0'
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBsp0,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam IN ('BSP1','BSP2')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBspRiemissioni,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam = 'BSS0'
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBss0,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam IN ('BSS1','BSS2')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBssRiemissioni,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam = 'BSI0'
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBsi0,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam IN ('BSI1','BSI2')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBsiRiemissioniPrimaRata,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam = 'BSI9'
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBsi9,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1 AND p.Cod_tipo_pagam IN ('BSIA','BSIB')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBsiRiemissioniSaldo,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) = 1
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoPagamentiBSStornati,
        SUM(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                      AND p.Cod_tipo_pagam NOT IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                 THEN TRY_CONVERT(DECIMAL(18, 2), p.Imp_pagato) ELSE 0 END) AS ImportoBSNonClassificato,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_tipo_pagam) END, ' | ') AS TipiPagamentoBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_mandato) END, ' | ') AS MandatiBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSP0','BSP1','BSP2')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_mandato) END, ' | ') AS MandatiPrimaRataBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSS0','BSS1','BSS2')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_mandato) END, ' | ') AS MandatiSaldoBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_mandato) END, ' | ') AS MandatiIntegrazioniBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                         THEN CONVERT(NVARCHAR(MAX), p.Ese_finanziario) END, ' | ') AS EserciziFinanziariBS,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) = 1
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_tipo_pagam) END, ' | ') AS TipiPagamentoBSStornati,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) = 1
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_mandato) END, ' | ') AS MandatiBSStornati,
        STRING_AGG(CASE WHEN ISNULL(p.Ritirato_azienda, 0) <> 1
                              AND p.Cod_tipo_pagam NOT IN ('BSP0','BSP1','BSP2','BSS0','BSS1','BSS2','BSI0','BSI1','BSI2','BSI9','BSIA','BSIB')
                         THEN CONVERT(NVARCHAR(MAX), p.Cod_tipo_pagam) END, ' | ') AS TipiPagamentoBSNonClassificati
    FROM Pagamenti p WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = p.Anno_accademico
       AND d.Num_domanda = p.Num_domanda
    WHERE p.Cod_tipo_pagam LIKE 'BS%'
    GROUP BY p.Anno_accademico, p.Num_domanda
),
ReversaliAggregate AS
(
    -- Le reversali incidono sul residuo BS solo se riferite a un codice pagamento BS*.
    SELECT
        r.Anno_accademico,
        r.Num_domanda,
        SUM(TRY_CONVERT(DECIMAL(18, 2), r.Importo)) AS ImportoReversaliBS,
        STRING_AGG(CONVERT(NVARCHAR(MAX), r.Cod_tipo_pagam), ' | ') AS TipiReversaleBS
    FROM Reversali r WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = r.Anno_accademico
       AND d.Num_domanda = r.Num_domanda
    WHERE (r.Ritirato_azienda = 0 OR r.Ritirato_azienda IS NULL)
      AND r.Cod_tipo_pagam LIKE 'BS%'
    GROUP BY r.Anno_accademico, r.Num_domanda
),
Detrazioni AS
(
    SELECT
        d.Cod_fiscale,
        d.Anno_accademico,
        SUM(TRY_CONVERT(DECIMAL(18, 2), m.IMPORTO)) AS ImportoDetrazioni
    FROM #Domande d
    INNER JOIN MOVIMENTI_CONTABILI_ELEMENTARI m WITH (READUNCOMMITTED)
        ON m.ANNO_ACCADEMICO = d.Anno_accademico
       AND m.CODICE_FISCALE = d.Cod_fiscale
    WHERE (m.SEGNO = 0 OR m.SEGNO IS NULL)
      AND m.CODICE_MOVIMENTO IS NULL
    GROUP BY d.Cod_fiscale, d.Anno_accademico
),
ModalitaPagamentoRanked AS
(
    SELECT
        vmp.Cod_fiscale,
        CONVERT(NVARCHAR(100), vmp.modalita_pagamento) AS ModalitaPagamento,
        CONVERT(NVARCHAR(100), vmp.IBAN) AS IBAN,
        TRY_CONVERT(DATETIME2, vmp.Data_validita) AS IbanDataValidita,
        CONVERT(NVARCHAR(100), vmp.Swift) AS Swift,
        TRY_CONVERT(BIT, vmp.Bonifico_estero) AS BonificoEstero,
        ROW_NUMBER() OVER
        (
            PARTITION BY vmp.Cod_fiscale
            ORDER BY
                CASE WHEN NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(100), vmp.IBAN))), '') IS NULL THEN 1 ELSE 0 END,
                TRY_CONVERT(DATETIME2, vmp.Data_validita) DESC,
                CONVERT(NVARCHAR(100), vmp.IBAN) DESC,
                CONVERT(NVARCHAR(100), vmp.modalita_pagamento) ASC,
                CONVERT(NVARCHAR(100), vmp.Swift) ASC
        ) AS rn
    FROM vMODALITA_PAGAMENTO vmp WITH (READUNCOMMITTED)
    INNER JOIN Studenti s
        ON s.Cod_fiscale = vmp.Cod_fiscale
    WHERE vmp.Data_fine_validita IS NULL
),
ModalitaPagamento AS
(
    SELECT Cod_fiscale, ModalitaPagamento, IBAN, IbanDataValidita, Swift, BonificoEstero
    FROM ModalitaPagamentoRanked
    WHERE rn = 1
),
IscrizioniRanked AS
(
    SELECT
        i.Cod_fiscale,
        i.Anno_accademico,
        TRY_CONVERT(INT, i.Anno_corso) AS AnnoCorso,
        CONVERT(NVARCHAR(100), i.Cod_tipologia_studi) AS TipoStudi,
        CONVERT(NVARCHAR(100), i.Cod_corso_laurea) AS CorsoLaurea,
        ISNULL(ss.Descrizione, CONVERT(NVARCHAR(100), i.Cod_sede_studi)) AS SedeStudi,
        TRY_CONVERT(DECIMAL(18, 2), i.Crediti_riconosciuti) AS CreditiRiconosciuti,
        ROW_NUMBER() OVER
        (
            PARTITION BY i.Cod_fiscale, i.Anno_accademico
            ORDER BY i.Data_validita DESC
        ) AS rn
    FROM Iscrizioni i WITH (READUNCOMMITTED)
    INNER JOIN StudentiAnno sa
        ON sa.Cod_fiscale = i.Cod_fiscale
       AND sa.Anno_accademico = i.Anno_accademico
    LEFT JOIN Sede_studi ss WITH (READUNCOMMITTED)
        ON ss.Cod_sede_studi = i.Cod_sede_studi
),
Iscrizioni AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        AnnoCorso,
        TipoStudi,
        CorsoLaurea,
        SedeStudi,
        CreditiRiconosciuti
    FROM IscrizioniRanked
    WHERE rn = 1
),
MeritoRanked AS
(
    SELECT
        m.Anno_accademico,
        m.Num_domanda,
        TRY_CONVERT(INT, m.Anno_immatricolaz) AS AnnoImmatricolazione,
        TRY_CONVERT(INT, m.Numero_esami) AS NumeroEsami,
        TRY_CONVERT(DECIMAL(18, 2), m.Numero_crediti) AS NumeroCrediti,
        ROW_NUMBER() OVER
        (
            PARTITION BY m.Anno_accademico, m.Num_domanda
            ORDER BY m.Data_validita DESC
        ) AS rn
    FROM Merito m WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = m.Anno_accademico
       AND d.Num_domanda = m.Num_domanda
),
Merito AS
(
    SELECT
        Anno_accademico,
        Num_domanda,
        AnnoImmatricolazione,
        NumeroEsami,
        NumeroCrediti
    FROM MeritoRanked
    WHERE rn = 1
),
CarrieraPregressaRanked AS
(
    SELECT
        c.Cod_fiscale,
        c.Anno_accademico,
        c.Numero_crediti,
        c.Cod_avvenimento,
        c.Benefici_usufruiti,
        c.Importi_restituiti,
        ROW_NUMBER() OVER
        (
            PARTITION BY c.Cod_fiscale, c.Anno_accademico, c.Cod_avvenimento
            ORDER BY c.Data_validita DESC
        ) AS rn
    FROM Carriera_pregressa c WITH (READUNCOMMITTED)
    INNER JOIN StudentiAnno sa
        ON sa.Cod_fiscale = c.Cod_fiscale
       AND sa.Anno_accademico = c.Anno_accademico
    WHERE c.riga_valida = 0
       OR c.riga_valida IS NULL
),
CarrieraPregressa AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        COUNT(*) AS NumeroEventi,
        SUM(TRY_CONVERT(DECIMAL(18, 2), Numero_crediti)) AS CreditiPregressi,
        STRING_AGG(CONVERT(NVARCHAR(MAX), Cod_avvenimento), ' | ') AS CodiciAvvenimento,
        MAX(ISNULL(TRY_CONVERT(INT, Benefici_usufruiti), 0)) AS BeneficiUsufruiti,
        MAX(ISNULL(TRY_CONVERT(INT, Importi_restituiti), 0)) AS ImportiRestituiti
    FROM CarrieraPregressaRanked
    WHERE rn = 1
    GROUP BY Cod_fiscale, Anno_accademico
),
ValoriCalcolatiRanked AS
(
    SELECT
        v.Anno_accademico,
        v.Num_domanda,
        TRY_CONVERT(DECIMAL(18, 2), v.ISEEDSU) AS ISEEDSU,
        TRY_CONVERT(DECIMAL(18, 2), v.ISPEDSU) AS ISPEDSU,
        CONVERT(NVARCHAR(10), v.Status_sede) AS StatusSede,
        ROW_NUMBER() OVER
        (
            PARTITION BY v.Anno_accademico, v.Num_domanda
            ORDER BY v.Data_validita DESC
        ) AS rn
    FROM Valori_calcolati v WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = v.Anno_accademico
       AND d.Num_domanda = v.Num_domanda
),
ValoriCalcolati AS
(
    SELECT Anno_accademico, Num_domanda, ISEEDSU, ISPEDSU, StatusSede
    FROM ValoriCalcolatiRanked
    WHERE rn = 1
),
TipologieRedditiRanked AS
(
    SELECT
        t.Anno_accademico,
        t.Num_domanda,
        CONVERT(NVARCHAR(20), t.Tipo_redd_nucleo_fam_origine) AS TipoOrigine,
        CONVERT(NVARCHAR(20), t.Tipo_redd_nucleo_fam_integr) AS TipoIntegrazione,
        ROW_NUMBER() OVER
        (
            PARTITION BY t.Anno_accademico, t.Num_domanda
            ORDER BY t.Data_validita DESC
        ) AS rn
    FROM Tipologie_redditi t WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = t.Anno_accademico
       AND d.Num_domanda = t.Num_domanda
),
TipologieRedditi AS
(
    SELECT Anno_accademico, Num_domanda, TipoOrigine, TipoIntegrazione
    FROM TipologieRedditiRanked
    WHERE rn = 1
),
DomiciliRanked AS
(
    SELECT
        l.Cod_fiscale,
        l.Anno_accademico,
        CONVERT(NVARCHAR(100), l.Cod_comune) AS ComuneDomicilio,
        CONVERT(NVARCHAR(100), l.N_serie_contratto) AS SerieContratto,
        CONVERT(NVARCHAR(30), l.Data_decorrenza) AS DataDecorrenza,
        CONVERT(NVARCHAR(30), l.Data_scadenza) AS DataScadenza,
        TRY_CONVERT(BIT, l.Proroga) AS Prorogato,
        ROW_NUMBER() OVER
        (
            PARTITION BY l.Cod_fiscale, l.Anno_accademico
            ORDER BY l.Data_validita DESC
        ) AS rn
    FROM Luogo_reperibilita_studente l WITH (READUNCOMMITTED)
    INNER JOIN StudentiAnno sa
        ON sa.Cod_fiscale = l.Cod_fiscale
       AND sa.Anno_accademico = l.Anno_accademico
    WHERE l.Tipo_luogo = 'DOM'
),
Domicili AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        ComuneDomicilio,
        SerieContratto,
        DataDecorrenza,
        DataScadenza,
        Prorogato
    FROM DomiciliRanked
    WHERE rn = 1
),
IstanzeAperte AS
(
    SELECT DISTINCT
        idg.Cod_fiscale,
        idg.Anno_accademico,
        CAST(1 AS BIT) AS HasOpen
    FROM Istanza_dati_generali idg WITH (READUNCOMMITTED)
    INNER JOIN StudentiAnno sa
        ON sa.Cod_fiscale = idg.Cod_fiscale
       AND sa.Anno_accademico = idg.Anno_accademico
    INNER JOIN Istanza_status s WITH (READUNCOMMITTED)
        ON s.Num_istanza = idg.Num_istanza
       AND s.Data_fine_validita IS NULL
    WHERE idg.Cod_tipo_istanza = '01'
      AND idg.Data_fine_validita IS NULL
      AND idg.Esito_istanza IS NULL
),
IstanzeChiuseRanked AS
(
    SELECT
        idg.Cod_fiscale,
        idg.Anno_accademico,
        CONVERT(NVARCHAR(50), idg.Esito_istanza) AS EsitoUltimaChiusa,
        ROW_NUMBER() OVER
        (
            PARTITION BY idg.Cod_fiscale, idg.Anno_accademico
            ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC
        ) AS rn
    FROM Istanza_dati_generali idg WITH (READUNCOMMITTED)
    INNER JOIN StudentiAnno sa
        ON sa.Cod_fiscale = idg.Cod_fiscale
       AND sa.Anno_accademico = idg.Anno_accademico
    INNER JOIN Istanza_status s WITH (READUNCOMMITTED)
        ON s.Num_istanza = idg.Num_istanza
       AND s.Data_fine_validita IS NOT NULL
    WHERE idg.Cod_tipo_istanza = '01'
      AND idg.Data_fine_validita IS NOT NULL
      AND idg.Esito_istanza IS NOT NULL
),
IstanzeChiuse AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        MAX(CASE WHEN rn = 1 THEN EsitoUltimaChiusa END) AS EsitoUltimaChiusa,
        MAX(CASE WHEN EsitoUltimaChiusa IN ('1', '2') THEN 1 ELSE 0 END) AS HasWorked
    FROM IstanzeChiuseRanked
    GROUP BY Cod_fiscale, Anno_accademico
),
Mensa AS
(
    SELECT
        m.Anno_accademico,
        m.Num_domanda,
        MAX(CASE WHEN TRY_CONVERT(INT, m.Concessa_monetizzazione) = 1 THEN 1 ELSE 0 END) AS ConcessaMonetizzazione
    FROM vMonetizzazione_mensa m WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = m.Anno_accademico
       AND d.Num_domanda = m.Num_domanda
    GROUP BY m.Anno_accademico, m.Num_domanda
),
AllegatiPermesso AS
(
    SELECT DISTINCT a.Id_allegato
    FROM #Domande d
    INNER JOIN Allegati a WITH (READUNCOMMITTED)
        ON a.Cod_fiscale = d.Cod_fiscale
    INNER JOIN Specifiche_permesso_soggiorno sp WITH (READUNCOMMITTED)
        ON sp.Id_allegato = a.Id_allegato
       AND (sp.Anno_accademico IS NULL OR sp.Anno_accademico = d.Anno_accademico)
    WHERE sp.Tipo_documento IN ('01', '02', '03')
),
StatusAllegatiRanked AS
(
    SELECT
        s.Id_allegato,
        s.Cod_status,
        s.Data_validita,
        ROW_NUMBER() OVER
        (
            PARTITION BY s.Id_allegato
            ORDER BY s.Data_validita DESC
        ) AS rn
    FROM Status_allegati s WITH (READUNCOMMITTED)
    INNER JOIN AllegatiPermesso ap
        ON ap.Id_allegato = s.Id_allegato
),
PermessiRanked AS
(
    SELECT
        d.Cod_fiscale,
        d.Anno_accademico,
        sp.Tipo_documento AS TipoDocumento,
        sa.Cod_status AS CodStatus,
        ROW_NUMBER() OVER
        (
            PARTITION BY d.Cod_fiscale, d.Anno_accademico, sp.Tipo_documento
            ORDER BY sp.Data_validita DESC, sa.Data_validita DESC
        ) AS rn
    FROM #Domande d
    INNER JOIN Allegati a WITH (READUNCOMMITTED)
        ON a.Cod_fiscale = d.Cod_fiscale
    INNER JOIN Specifiche_permesso_soggiorno sp WITH (READUNCOMMITTED)
        ON sp.Id_allegato = a.Id_allegato
       AND (sp.Anno_accademico IS NULL OR sp.Anno_accademico = d.Anno_accademico)
    LEFT JOIN StatusAllegatiRanked sa
        ON sa.Id_allegato = sp.Id_allegato
       AND sa.rn = 1
    WHERE sp.Tipo_documento IN ('01', '02', '03')
),
Permessi AS
(
    SELECT
        Cod_fiscale,
        Anno_accademico,
        STRING_AGG(
            CONVERT(NVARCHAR(MAX), CONCAT(TipoDocumento, ':', ISNULL(CodStatus, 'ASSENTE'))),
            ' | ') AS DocumentiPermesso
    FROM PermessiRanked
    WHERE rn = 1
    GROUP BY Cod_fiscale, Anno_accademico
),
GraduatorieRanked AS
(
    SELECT
        g.Anno_accademico,
        g.Num_domanda,
        TRY_CONVERT(INT, g.Cod_tipo_graduat) AS CodTipoGraduatoria,
        CONCAT(g.Cod_beneficio, ':', ISNULL(CONVERT(NVARCHAR(10), g.Cod_tipo_esito), '')) AS Esito,
        ROW_NUMBER() OVER
        (
            PARTITION BY g.Anno_accademico, g.Num_domanda, g.Cod_beneficio, g.Cod_tipo_graduat
            ORDER BY g.Data_validita DESC
        ) AS rn
    FROM Graduatorie g WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = g.Anno_accademico
       AND d.Num_domanda = g.Num_domanda
),
Graduatorie AS
(
    SELECT
        Anno_accademico,
        Num_domanda,
        STRING_AGG(CASE WHEN CodTipoGraduatoria = 0 THEN Esito END, ' | ') AS GraduatorieProvvisorie,
        STRING_AGG(CASE WHEN CodTipoGraduatoria = 1 THEN Esito END, ' | ') AS GraduatorieDefinitive
    FROM GraduatorieRanked
    WHERE rn = 1
    GROUP BY Anno_accademico, Num_domanda
),
Mobilita AS
(
    SELECT
        b.Anno_accademico,
        b.Num_domanda,
        MAX(CASE WHEN b.Cod_beneficio = 'CI' THEN 1 ELSE 0 END) AS MobilitaRichiesta
    FROM Benefici_richiesti b WITH (READUNCOMMITTED)
    INNER JOIN #Domande d
        ON d.Anno_accademico = b.Anno_accademico
       AND d.Num_domanda = b.Num_domanda
    WHERE b.Data_fine_validita IS NULL
    GROUP BY b.Anno_accademico, b.Num_domanda
)
SELECT
    d.Cod_fiscale,
    d.Anno_accademicoInt AS Anno_accademico,
    d.Num_domanda,
    ISNULL(sc.StatusCompilazione, 0) AS StatusCompilazione,
    ISNULL(e.EsitoBS, '') AS EsitoBS,
    ISNULL(e.EsitoPA, '') AS EsitoPA,
    ISNULL(e.EsitoCI, '') AS EsitoCI,
    ISNULL(e.ImportoCI, 0) AS ImportoCI,
    ISNULL(e.ImportoBeneficio, 0) AS ImportoBeneficio,
    ISNULL(si.ImportoSpecifiche, 0) AS ImportoSpecifiche,
    ISNULL(pag.ImportoPagatoBS, 0) AS ImportoPagatoBS,
    ISNULL(pag.TipiPagamentoBS, '') AS TipiPagamentoBS,
    ISNULL(pag.MandatiBS, '') AS MandatiBS,
    ISNULL(pag.MandatiPrimaRataBS, '') AS MandatiPrimaRataBS,
    ISNULL(pag.MandatiSaldoBS, '') AS MandatiSaldoBS,
    ISNULL(pag.MandatiIntegrazioniBS, '') AS MandatiIntegrazioniBS,
    ISNULL(pag.EserciziFinanziariBS, '') AS EserciziFinanziariBS,
    ISNULL(pag.ImportoBsp0, 0) AS ImportoBsp0,
    ISNULL(pag.ImportoBspRiemissioni, 0) AS ImportoBspRiemissioni,
    ISNULL(pag.ImportoBss0, 0) AS ImportoBss0,
    ISNULL(pag.ImportoBssRiemissioni, 0) AS ImportoBssRiemissioni,
    ISNULL(pag.ImportoBsi0, 0) AS ImportoBsi0,
    ISNULL(pag.ImportoBsiRiemissioniPrimaRata, 0) AS ImportoBsiRiemissioniPrimaRata,
    ISNULL(pag.ImportoBsi9, 0) AS ImportoBsi9,
    ISNULL(pag.ImportoBsiRiemissioniSaldo, 0) AS ImportoBsiRiemissioniSaldo,
    ISNULL(pag.ImportoPagamentiBSStornati, 0) AS ImportoPagamentiBSStornati,
    ISNULL(pag.TipiPagamentoBSStornati, '') AS TipiPagamentoBSStornati,
    ISNULL(pag.MandatiBSStornati, '') AS MandatiBSStornati,
    ISNULL(pag.ImportoBSNonClassificato, 0) AS ImportoBSNonClassificato,
    ISNULL(pag.TipiPagamentoBSNonClassificati, '') AS TipiPagamentoBSNonClassificati,
    ISNULL(rev.ImportoReversaliBS, 0) AS ImportoReversaliBS,
    ISNULL(rev.TipiReversaleBS, '') AS TipiReversaleBS,
    ISNULL(det.ImportoDetrazioni, 0) AS ImportoDetrazioni,
    ISNULL(si.ImpegnoPrimaRata, '') AS ImpegnoPrimaRata,
    ISNULL(si.ImpegnoSaldo, '') AS ImpegnoSaldo,
    ISNULL(mp.ModalitaPagamento, '') AS ModalitaPagamento,
    ISNULL(mp.IBAN, '') AS IBAN,
    mp.IbanDataValidita AS IbanDataValidita,
    ISNULL(mp.Swift, '') AS Swift,
    ISNULL(mp.BonificoEstero, 0) AS BonificoEstero,
    ISNULL(dbo.SlashDescrBlocchi(d.Num_domanda, d.Anno_accademico, ''), '') AS Blocchi,
    ISNULL(isc.AnnoCorso, 0) AS AnnoCorso,
    ISNULL(isc.TipoStudi, '') AS TipoStudi,
    ISNULL(isc.CorsoLaurea, '') AS CorsoLaurea,
    ISNULL(isc.SedeStudi, '') AS SedeStudi,
    ISNULL(mer.AnnoImmatricolazione, 0) AS AnnoImmatricolazione,
    ISNULL(mer.NumeroEsami, 0) AS NumeroEsami,
    ISNULL(mer.NumeroCrediti, 0) AS NumeroCrediti,
    ISNULL(isc.CreditiRiconosciuti, 0) AS CreditiRiconosciuti,
    ISNULL(cp.NumeroEventi, 0) AS NumeroEventiCarrieraPregressa,
    ISNULL(cp.CreditiPregressi, 0) AS CreditiCarrieraPregressa,
    ISNULL(cp.CodiciAvvenimento, '') AS CodiciCarrieraPregressa,
    ISNULL(cp.BeneficiUsufruiti, 0) AS BeneficiUsufruiti,
    ISNULL(cp.ImportiRestituiti, 0) AS ImportiRestituiti,
    ISNULL(vc.ISEEDSU, 0) AS ISEEDSU,
    ISNULL(vc.ISPEDSU, 0) AS ISPEDSU,
    ISNULL(tr.TipoOrigine, '') AS TipoRedditoOrigine,
    ISNULL(tr.TipoIntegrazione, '') AS TipoRedditoIntegrazione,
    ISNULL(vc.StatusSede, '') AS StatusSede,
    ISNULL(dom.ComuneDomicilio, '') AS ComuneDomicilio,
    ISNULL(dom.SerieContratto, '') AS SerieContratto,
    ISNULL(dom.DataDecorrenza, '') AS DataDecorrenza,
    ISNULL(dom.DataScadenza, '') AS DataScadenza,
    ISNULL(dom.Prorogato, 0) AS Prorogato,
    ISNULL(ia.HasOpen, 0) AS HasIstanzaDomicilioAperta,
    ISNULL(ic.HasWorked, 0) AS HasIstanzaDomicilioLavorata,
    ISNULL(ic.EsitoUltimaChiusa, '') AS EsitoUltimaIstanzaDomicilio,
    ISNULL(me.ConcessaMonetizzazione, 0) AS ConcessaMonetizzazione,
    ISNULL(ps.DocumentiPermesso, '') AS DocumentiPermesso,
    ISNULL(gr.GraduatorieProvvisorie, '') AS GraduatorieProvvisorie,
    ISNULL(gr.GraduatorieDefinitive, '') AS GraduatorieDefinitive,
    ISNULL(mob.MobilitaRichiesta, 0) AS MobilitaRichiesta
FROM #Domande d
LEFT JOIN StatusCompilazione sc
    ON sc.Anno_accademico = d.Anno_accademico
   AND sc.Num_domanda = d.Num_domanda
LEFT JOIN Esiti e
    ON e.Anno_accademico = d.Anno_accademico
   AND e.Num_domanda = d.Num_domanda
LEFT JOIN Specifiche si
    ON si.Anno_accademico = d.Anno_accademico
   AND si.Num_domanda = d.Num_domanda
LEFT JOIN PagamentiAggregati pag
    ON pag.Anno_accademico = d.Anno_accademico
   AND pag.Num_domanda = d.Num_domanda
LEFT JOIN ReversaliAggregate rev
    ON rev.Anno_accademico = d.Anno_accademico
   AND rev.Num_domanda = d.Num_domanda
LEFT JOIN Detrazioni det
    ON det.Cod_fiscale = d.Cod_fiscale
   AND det.Anno_accademico = d.Anno_accademico
LEFT JOIN ModalitaPagamento mp
    ON mp.Cod_fiscale = d.Cod_fiscale
LEFT JOIN Iscrizioni isc
    ON isc.Cod_fiscale = d.Cod_fiscale
   AND isc.Anno_accademico = d.Anno_accademico
LEFT JOIN Merito mer
    ON mer.Anno_accademico = d.Anno_accademico
   AND mer.Num_domanda = d.Num_domanda
LEFT JOIN CarrieraPregressa cp
    ON cp.Cod_fiscale = d.Cod_fiscale
   AND cp.Anno_accademico = d.Anno_accademico
LEFT JOIN ValoriCalcolati vc
    ON vc.Anno_accademico = d.Anno_accademico
   AND vc.Num_domanda = d.Num_domanda
LEFT JOIN TipologieRedditi tr
    ON tr.Anno_accademico = d.Anno_accademico
   AND tr.Num_domanda = d.Num_domanda
LEFT JOIN Domicili dom
    ON dom.Cod_fiscale = d.Cod_fiscale
   AND dom.Anno_accademico = d.Anno_accademico
LEFT JOIN IstanzeAperte ia
    ON ia.Cod_fiscale = d.Cod_fiscale
   AND ia.Anno_accademico = d.Anno_accademico
LEFT JOIN IstanzeChiuse ic
    ON ic.Cod_fiscale = d.Cod_fiscale
   AND ic.Anno_accademico = d.Anno_accademico
LEFT JOIN Mensa me
    ON me.Anno_accademico = d.Anno_accademico
   AND me.Num_domanda = d.Num_domanda
LEFT JOIN Permessi ps
    ON ps.Cod_fiscale = d.Cod_fiscale
   AND ps.Anno_accademico = d.Anno_accademico
LEFT JOIN Graduatorie gr
    ON gr.Anno_accademico = d.Anno_accademico
   AND gr.Num_domanda = d.Num_domanda
LEFT JOIN Mobilita mob
    ON mob.Anno_accademico = d.Anno_accademico
   AND mob.Num_domanda = d.Num_domanda;
";
    }
}
