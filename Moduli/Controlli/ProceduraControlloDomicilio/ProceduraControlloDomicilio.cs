using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    /// <summary>
    /// Controllo diagnostico, esclusivamente in lettura, dei domicili dall'A.A. 2026/2027.
    /// La procedura non modifica dati e non invia comunicazioni: produce un CSV operativo
    /// con una riga per domicilio, contratto, subentro e proroga.
    /// </summary>
    internal sealed class ControlloDomicilio : BaseProcedure<ArgsControlloDomicilio>
    {
        private const int MinAcademicYear = 20262027;
        private const int TemporalToleranceDays = 10;
        private const int PdfProgressInterval = 160;
        private const int PdfMaxParallelism = 16;
        private const int PdfQueueCapacityPerWorker = 1;
        private const int PdfSqlDownloadMaxParallelism = 4;
        private const string AllegatiDatabase = "ADISU_ALLEGATI";
        private const string TipoAllegatoContratto = "05";
        private const string TipoAllegatoProroga = "34";
        private const string TipoAllegatoSubentro = "45";

        private static readonly DateTime MinDate = new(1900, 1, 1);
        private static readonly DateTime MaxDate = new(2900, 12, 31);

        public string selectedAA = string.Empty;

        private sealed record PdfWorkItem(
            DomicilioAttachmentRecord Attachment,
            DomicilioPdfExpectation Expectation,
            DomicilioControlState DestinationState);

        private sealed record LoadedPdf(
            decimal IdAllegato,
            byte[]? Content,
            string FileName,
            string ContentType);

        private sealed record PdfDescriptor(
            decimal IdAllegato,
            decimal? PhysicalFileId,
            long ContentLength,
            string FileName,
            string ContentType);

        private sealed record PdfStreamMetrics(
            int Documents,
            long Bytes,
            long QueueWaitTicks);

        private sealed record CompletedPdfAnalysis(
            PdfWorkItem WorkItem,
            DomicilioPdfAnalysis Analysis,
            string FileName,
            string ContentType);

        public ControlloDomicilio(MasterForm? masterForm, SqlConnection? connection)
            : base(masterForm, connection)
        {
        }

        public override void RunProcedure(ArgsControlloDomicilio args)
        {
            ValidateArguments(args);
            selectedAA = args._selectedAA.Trim();

            EnsureConnectionOpen();
            Directory.CreateDirectory(args._folderPath);

            var allowedFiscalCodes = args._codiciFiscali == null
                ? null
                : new HashSet<string>(
                    args._codiciFiscali.Select(NormalizeFiscalCode).Where(x => x.Length > 0),
                    StringComparer.OrdinalIgnoreCase);

            Logger.LogInfo(2, $"Avvio controllo domicili per l'anno accademico {selectedAA}.");
            Logger.LogInfo(5, "Caricamento di domicili, contratti, proroghe e metadati degli allegati...");

            var domiciles = LoadHierarchy(selectedAA, allowedFiscalCodes);
            Logger.LogInfo(20, $"Caricati {domiciles.Count} domicili, " +
                $"{domiciles.Sum(x => x.Contratti.Count)} contratti e " +
                $"{domiciles.SelectMany(x => x.Contratti).Sum(x => x.Proroghe.Count)} proroghe.");

            ValidateHierarchy(domiciles);
            ValidateContractOverlaps(domiciles);

            var pdfWork = BuildPdfWork(domiciles);
            Logger.LogInfo(35, $"Individuati {pdfWork.Count} documenti PDF da controllare.");
            AnalyzePdfDocuments(pdfWork);

            var rows = BuildCsvRows(domiciles);
            var outputPath = ExportCsv(rows, args._folderPath, selectedAA);

            var nonConform = rows.Count(x => x.Esito == "NON_CONFORME");
            var manual = rows.Count(x => x.Esito == "VERIFICA_MANUALE");
            Logger.LogInfo(100,
                $"Controllo completato. CSV: {outputPath}. " +
                $"Righe: {rows.Count}; non conformi: {nonConform}; da verificare: {manual}.");
        }

        private static void ValidateArguments(ArgsControlloDomicilio args)
        {
            ArgumentNullException.ThrowIfNull(args);

            if (!Regex.IsMatch(args._selectedAA?.Trim() ?? string.Empty, @"^\d{8}$") ||
                !int.TryParse(args._selectedAA, NumberStyles.None, CultureInfo.InvariantCulture, out var aa))
                throw new ArgumentException("L'anno accademico deve contenere otto cifre nel formato xxxxyyyy.");

            var firstYear = aa / 10000;
            var secondYear = aa % 10000;
            if (secondYear != firstYear + 1)
                throw new ArgumentException("L'anno accademico deve indicare due anni consecutivi (es. 20262027).");
            if (aa < MinAcademicYear)
                throw new ArgumentException("La nuova gestione dei domicili è supportata a partire dall'anno accademico 20262027.");
            if (string.IsNullOrWhiteSpace(args._folderPath))
                throw new ArgumentException("Selezionare la cartella in cui generare il CSV.");
        }

        private void EnsureConnectionOpen()
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("La connessione al database principale non è disponibile.");
            if (CONNECTION.State != ConnectionState.Open)
                CONNECTION.Open();
        }

        private List<DomicilioControlRecord> LoadHierarchy(
            string academicYear,
            HashSet<string>? allowedFiscalCodes)
        {
            const string sql = @"
SET NOCOUNT ON;

SELECT
    d.ID,
    d.ANNO_ACCADEMICO,
    d.NUM_DOMANDA,
    d.COD_FISCALE,
    LTRIM(RTRIM(ISNULL(s.COGNOME, '') + ' ' + ISNULL(s.NOME, ''))) AS STUDENTE,
    d.COD_COMUNE,
    ISNULL(cm.DESCRIZIONE, '') AS COMUNE,
    ISNULL(d.INDIRIZZO, '') AS INDIRIZZO,
    ISNULL(d.NUMERO_CIVICO, '') AS NUMERO_CIVICO,
    ISNULL(d.CAP, '') AS CAP,
    ISNULL(d.TIPO_DOMICILIO, '') AS TIPO_DOMICILIO,
    d.DATA_INIZIO,
    d.DATA_FINE
FROM dbo.Domicili d
LEFT JOIN dbo.Studente s ON s.COD_FISCALE = d.COD_FISCALE
LEFT JOIN dbo.Comuni cm ON cm.COD_COMUNE = d.COD_COMUNE
WHERE d.ANNO_ACCADEMICO = @aa
  AND d.DATA_FINE_VALIDITA IS NULL
  AND d.IS_DELETED = 0
  AND d.TIPO_DOMICILIO = '1'
  AND EXISTS
  (
      SELECT 1
      FROM dbo.Contratti cx
      WHERE cx.ID_DOMICILIO = d.ID
        AND cx.DATA_FINE_VALIDITA IS NULL
        AND cx.IS_DELETED = 0
  )
ORDER BY d.COD_FISCALE, d.DATA_INIZIO, d.ID;

SELECT
    c.ID,
    c.ID_DOMICILIO,
    c.TIPO_CONTRATTO_TITOLO_ONEROSO,
    ISNULL(c.N_SERIE_CONTRATTO, '') AS N_SERIE_CONTRATTO,
    c.DATA_REG_CONTRATTO,
    c.DATA_INIZIO,
    c.DATA_FINE,
    ISNULL(c.TIPO_ENTE, '') AS TIPO_ENTE,
    ISNULL(c.DENOM_ENTE, '') AS DENOM_ENTE,
    ISNULL(c.IMPORTO_RATA, '') AS IMPORTO_RATA,
    ISNULL(c.N_SERIE_SUBENTRO, '') AS N_SERIE_SUBENTRO,
    c.DATA_INIZIO_SUBENTRO,
    c.DATA_CESSAZIONE
FROM dbo.Contratti c
INNER JOIN dbo.Domicili d ON d.ID = c.ID_DOMICILIO
WHERE d.ANNO_ACCADEMICO = @aa
  AND d.DATA_FINE_VALIDITA IS NULL
  AND d.IS_DELETED = 0
  AND d.TIPO_DOMICILIO = '1'
  AND c.DATA_FINE_VALIDITA IS NULL
  AND c.IS_DELETED = 0
ORDER BY c.ID_DOMICILIO, c.DATA_INIZIO, c.ID;

SELECT
    p.ID,
    p.ID_CONTRATTO,
    ISNULL(p.N_SERIE_PROROGA, '') AS N_SERIE_PROROGA,
    p.DATA_DECORRENZA,
    p.DATA_SCADENZA
FROM dbo.Proroghe p
INNER JOIN dbo.Contratti c ON c.ID = p.ID_CONTRATTO
INNER JOIN dbo.Domicili d ON d.ID = c.ID_DOMICILIO
WHERE d.ANNO_ACCADEMICO = @aa
  AND d.DATA_FINE_VALIDITA IS NULL
  AND d.IS_DELETED = 0
  AND d.TIPO_DOMICILIO = '1'
  AND c.DATA_FINE_VALIDITA IS NULL
  AND c.IS_DELETED = 0
  AND p.DATA_FINE_VALIDITA IS NULL
  AND p.IS_DELETED = 0
ORDER BY p.ID_CONTRATTO, p.DATA_DECORRENZA, p.ID;

SELECT
    a.id_allegato,
    a.cod_tipo_allegato,
    a.data_validita,
    a.ID_CONTRATTO,
    a.ID_PROROGA,
    a.cod_fiscale,
    a.num_domanda,
    ISNULL(st.cod_status, '') AS cod_status,
    ISNULL(st.DESCRIZIONE_STATUS, '') AS DESCRIZIONE_STATUS
FROM dbo.ALLEGATI a
OUTER APPLY
(
    SELECT TOP (1)
        sa.cod_status,
        ISNULL(tsa.DESCRIZIONE, '') AS DESCRIZIONE_STATUS
    FROM dbo.STATUS_ALLEGATI sa
    LEFT JOIN dbo.TIPOLOGIE_STATUS_ALLEGATI tsa ON tsa.cod_status = sa.cod_status
    WHERE sa.id_allegato = a.id_allegato
      AND sa.data_fine_validita IS NULL
    ORDER BY sa.data_validita DESC, sa.Id_STATUS_ALLEGATI DESC
) st
WHERE a.anno_accademico = @aa
  AND a.data_fine_validita IS NULL
  AND a.cod_tipo_allegato IN ('05', '34', '45')
  AND
  (
      EXISTS
      (
          SELECT 1
          FROM dbo.Contratti cx
          INNER JOIN dbo.Domicili dx ON dx.ID = cx.ID_DOMICILIO
          WHERE cx.ID = a.ID_CONTRATTO
            AND cx.DATA_FINE_VALIDITA IS NULL
            AND cx.IS_DELETED = 0
            AND dx.ANNO_ACCADEMICO = @aa
            AND dx.TIPO_DOMICILIO = '1'
            AND dx.DATA_FINE_VALIDITA IS NULL
            AND dx.IS_DELETED = 0
      )
      OR EXISTS
      (
          SELECT 1
          FROM dbo.Proroghe px
          INNER JOIN dbo.Contratti cx ON cx.ID = px.ID_CONTRATTO
          INNER JOIN dbo.Domicili dx ON dx.ID = cx.ID_DOMICILIO
          WHERE px.ID = a.ID_PROROGA
            AND px.DATA_FINE_VALIDITA IS NULL
            AND px.IS_DELETED = 0
            AND cx.DATA_FINE_VALIDITA IS NULL
            AND cx.IS_DELETED = 0
            AND dx.ANNO_ACCADEMICO = @aa
            AND dx.TIPO_DOMICILIO = '1'
            AND dx.DATA_FINE_VALIDITA IS NULL
            AND dx.IS_DELETED = 0
      )
  )
ORDER BY a.id_allegato;";

            using var command = new SqlCommand(sql, CONNECTION) { CommandTimeout = 900 };
            command.Parameters.Add("@aa", SqlDbType.Char, 8).Value = academicYear;

            var domiciles = new List<DomicilioControlRecord>();
            var domicileById = new Dictionary<int, DomicilioControlRecord>();
            var contractById = new Dictionary<int, ContrattoControlRecord>();
            var extensionById = new Dictionary<int, ProrogaControlRecord>();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var fiscalCode = NormalizeFiscalCode(ReadString(reader, "COD_FISCALE"));
                if (allowedFiscalCodes != null && !allowedFiscalCodes.Contains(fiscalCode))
                    continue;

                var domicile = new DomicilioControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    AnnoAccademico = ReadString(reader, "ANNO_ACCADEMICO"),
                    NumDomanda = ReadDecimal(reader, "NUM_DOMANDA"),
                    CodFiscale = fiscalCode,
                    Studente = ReadString(reader, "STUDENTE"),
                    CodComune = ReadString(reader, "COD_COMUNE"),
                    Comune = ReadString(reader, "COMUNE"),
                    Indirizzo = ReadString(reader, "INDIRIZZO"),
                    NumeroCivico = ReadString(reader, "NUMERO_CIVICO"),
                    Cap = ReadString(reader, "CAP"),
                    TipoDomicilio = ReadString(reader, "TIPO_DOMICILIO"),
                    DataInizio = ReadNullableDate(reader, "DATA_INIZIO"),
                    DataFine = ReadNullableDate(reader, "DATA_FINE")
                };
                domiciles.Add(domicile);
                domicileById[domicile.Id] = domicile;
            }

            reader.NextResult();
            while (reader.Read())
            {
                var domicileId = ReadInt(reader, "ID_DOMICILIO");
                if (!domicileById.TryGetValue(domicileId, out var domicile))
                    continue;

                var contract = new ContrattoControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    IdDomicilio = domicileId,
                    TipoContratto = ReadInt(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO"),
                    NumeroSerie = ReadString(reader, "N_SERIE_CONTRATTO"),
                    DataRegistrazione = ReadNullableDate(reader, "DATA_REG_CONTRATTO"),
                    DataInizio = ReadNullableDate(reader, "DATA_INIZIO") ?? DateTime.MinValue,
                    DataFine = ReadNullableDate(reader, "DATA_FINE"),
                    TipoEnte = ReadString(reader, "TIPO_ENTE"),
                    DenominazioneEnte = ReadString(reader, "DENOM_ENTE"),
                    ImportoRata = ReadString(reader, "IMPORTO_RATA"),
                    NumeroSerieSubentro = ReadString(reader, "N_SERIE_SUBENTRO"),
                    DataInizioSubentro = ReadNullableDate(reader, "DATA_INIZIO_SUBENTRO"),
                    DataCessazione = ReadNullableDate(reader, "DATA_CESSAZIONE")
                };
                domicile.Contratti.Add(contract);
                contractById[contract.Id] = contract;
            }

            reader.NextResult();
            while (reader.Read())
            {
                var contractId = ReadInt(reader, "ID_CONTRATTO");
                if (!contractById.TryGetValue(contractId, out var contract))
                    continue;

                var extension = new ProrogaControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    IdContratto = contractId,
                    NumeroSerie = ReadString(reader, "N_SERIE_PROROGA"),
                    DataDecorrenza = ReadNullableDate(reader, "DATA_DECORRENZA") ?? DateTime.MinValue,
                    DataScadenza = ReadNullableDate(reader, "DATA_SCADENZA") ?? DateTime.MinValue
                };
                contract.Proroghe.Add(extension);
                extensionById[extension.Id] = extension;
            }

            reader.NextResult();
            while (reader.Read())
            {
                var attachment = new DomicilioAttachmentRecord
                {
                    IdAllegato = ReadDecimal(reader, "id_allegato"),
                    CodTipoAllegato = ReadString(reader, "cod_tipo_allegato"),
                    DataValidita = ReadNullableDate(reader, "data_validita") ?? DateTime.MinValue,
                    IdContratto = ReadNullableInt(reader, "ID_CONTRATTO"),
                    IdProroga = ReadNullableInt(reader, "ID_PROROGA"),
                    CodFiscale = NormalizeFiscalCode(ReadString(reader, "cod_fiscale")),
                    NumDomanda = ReadNullableDecimal(reader, "num_domanda"),
                    CodStatus = ReadString(reader, "cod_status"),
                    StatusDescription = ReadString(reader, "DESCRIZIONE_STATUS")
                };

                if (attachment.CodTipoAllegato == TipoAllegatoProroga &&
                    attachment.IdProroga.HasValue &&
                    extensionById.TryGetValue(attachment.IdProroga.Value, out var extension))
                {
                    extension.Allegati.Add(attachment);
                }
                else if (attachment.IdContratto.HasValue &&
                         contractById.TryGetValue(attachment.IdContratto.Value, out var contract))
                {
                    if (attachment.CodTipoAllegato == TipoAllegatoContratto)
                        contract.AllegatiContratto.Add(attachment);
                    else if (attachment.CodTipoAllegato == TipoAllegatoSubentro)
                        contract.AllegatiSubentro.Add(attachment);
                }
            }

            return domiciles;
        }

        private static void ValidateHierarchy(List<DomicilioControlRecord> domiciles)
        {
            foreach (var domicile in domiciles)
            {
                ValidateDomicile(domicile);
                foreach (var contract in domicile.Contratti)
                    ValidateContract(domicile, contract);

                if (domicile.TipoDomicilio == "1" && domicile.Contratti.Count > 0)
                    ValidateDomicileCoverage(domicile);
            }
        }

        private static void ValidateDomicile(DomicilioControlRecord domicile)
        {
            if (!Regex.IsMatch(domicile.CodFiscale, @"^[A-Z0-9]{16}$"))
                AddNonConform(domicile.State, "DOM_001", "Il codice fiscale dello studente è assente o formalmente non valido.", "Verificare l'anagrafica dello studente.");
            if (string.IsNullOrWhiteSpace(domicile.CodComune) || string.IsNullOrWhiteSpace(domicile.Comune))
                AddNonConform(domicile.State, "DOM_002", "Il comune del domicilio è assente o non riconosciuto.", "Correggere il comune del domicilio.");
            if (string.IsNullOrWhiteSpace(domicile.Indirizzo))
                AddNonConform(domicile.State, "DOM_003", "L'indirizzo del domicilio è assente.", "Verificare e integrare l'indirizzo.");
            if (string.IsNullOrWhiteSpace(domicile.NumeroCivico))
                AddManual(domicile.State, "DOM_004", "Il numero civico del domicilio è assente.", "Verificare se l'indirizzo è effettivamente privo di numero civico.");
            if (!Regex.IsMatch(domicile.Cap, @"^\d{5}$"))
                AddNonConform(domicile.State, "DOM_005", "Il CAP del domicilio è assente o formalmente non valido.", "Correggere il CAP.");
            if (domicile.TipoDomicilio is not ("0" or "1"))
                AddNonConform(domicile.State, "DOM_006", "Il titolo del domicilio non è riconosciuto.", "Impostare il domicilio a titolo gratuito o oneroso.");
            if (!IsValidDate(domicile.DataInizio) || !IsValidDate(domicile.DataFine))
                AddNonConform(domicile.State, "DOM_007", "Una data del domicilio non è compresa tra il 01/01/1900 e il 31/12/2900.", "Correggere le date del domicilio.");
            if (domicile.DataInizio.HasValue && domicile.DataFine.HasValue && domicile.DataInizio > domicile.DataFine)
                AddNonConform(domicile.State, "DOM_008", "La data iniziale del domicilio è successiva alla data finale.", "Correggere il periodo del domicilio.");

            if (domicile.TipoDomicilio == "0")
            {
                if (!domicile.DataInizio.HasValue || !domicile.DataFine.HasValue)
                    AddNonConform(domicile.State, "DOM_009", "Il domicilio a titolo gratuito deve avere data iniziale e data finale.", "Integrare il periodo dichiarato dallo studente.");
                if (domicile.Contratti.Count > 0)
                    AddManual(domicile.State, "DOM_010", "Il domicilio è gratuito ma contiene uno o più contratti.", "Verificare il titolo del domicilio e i contratti associati.");
            }
            else if (domicile.TipoDomicilio == "1" && domicile.Contratti.Count == 0)
            {
                AddNonConform(domicile.State, "DOM_011", "Il domicilio a titolo oneroso non contiene alcun contratto.", "Richiedere o associare il contratto relativo al domicilio.");
            }
        }

        private static void ValidateContract(DomicilioControlRecord domicile, ContrattoControlRecord contract)
        {
            if (contract.TipoContratto is not (0 or 1))
                AddNonConform(contract.State, "CON_001", "La tipologia del contratto non è riconosciuta.", "Correggere la tipologia del contratto.");
            if (!IsValidDate(contract.DataInizio))
                AddNonConform(contract.State, "CON_002", "La data di decorrenza del contratto non è valida.", "Correggere la decorrenza del contratto.");
            if (!IsValidDate(contract.DataFine))
                AddNonConform(contract.State, "CON_003", "La data di scadenza del contratto non è valida.", "Correggere la scadenza del contratto.");
            if (contract.DataFine.HasValue && contract.DataInizio.Date > contract.DataFine.Value.Date)
                AddNonConform(contract.State, "CON_004", "La decorrenza del contratto è successiva alla scadenza.", "Correggere il periodo del contratto.");

            if (contract.IsLocazione)
            {
                if (!DomicilioUtils.IsValidSerie(contract.NumeroSerie))
                    AddNonConform(contract.State, "CON_005", "Gli estremi di registrazione del contratto sono assenti o formalmente non validi.", "Confrontare gli estremi con il documento registrato.");
                if (!IsValidDate(contract.DataRegistrazione))
                    AddNonConform(contract.State, "CON_006", "La data di registrazione del contratto è assente o non valida.", "Confrontare la data con il documento registrato.");
                if (!contract.DataFine.HasValue)
                    AddNonConform(contract.State, "CON_007", "Il contratto di locazione non ha una data di scadenza.", "Integrare la scadenza contrattuale.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(contract.DenominazioneEnte))
                    AddNonConform(contract.State, "CON_008", "La denominazione dell'ente è assente.", "Integrare la denominazione dell'ente.");
                if (string.IsNullOrWhiteSpace(contract.TipoEnte))
                    AddManual(contract.State, "CON_009", "La tipologia dell'ente è assente.", "Verificare la natura dell'ente.");
                if (string.IsNullOrWhiteSpace(contract.ImportoRata) || !TryPositiveAmount(contract.ImportoRata))
                    AddManual(contract.State, "CON_010", "L'importo della rata dell'ente è assente o non interpretabile.", "Verificare l'importo dichiarato.");
            }

            ValidateSubentry(contract);
            ValidateExtensions(contract);
            ValidateCessation(contract);

            if (domicile.TipoDomicilio == "0")
                AddManual(contract.State, "CON_011", "Il contratto appartiene a un domicilio dichiarato gratuito.", "Verificare il titolo del domicilio.");
        }

        private static void ValidateSubentry(ContrattoControlRecord contract)
        {
            if (!contract.HasSubentro)
                return;

            if (string.IsNullOrWhiteSpace(contract.NumeroSerieSubentro) || !contract.DataInizioSubentro.HasValue)
                AddNonConform(contract.SubentroState, "SUB_001", "Il subentro è incompleto: estremi e data devono essere entrambi presenti.", "Integrare i dati del subentro.");
            if (!string.IsNullOrWhiteSpace(contract.NumeroSerieSubentro) && !DomicilioUtils.IsValidSerie(contract.NumeroSerieSubentro))
                AddNonConform(contract.SubentroState, "SUB_002", "Gli estremi del subentro non sono formalmente validi.", "Confrontare gli estremi con il documento di subentro.");
            if (!IsValidDate(contract.DataInizioSubentro))
                AddNonConform(contract.SubentroState, "SUB_003", "La data del subentro non è valida.", "Correggere la data del subentro.");
            if (contract.DataInizioSubentro.HasValue && contract.DataInizioSubentro.Value.Date < contract.DataInizio.Date)
                AddNonConform(contract.SubentroState, "SUB_004", "Il subentro precede la decorrenza originaria del contratto.", "Correggere la data del subentro.");
            if (contract.DataInizioSubentro.HasValue && contract.DataFine.HasValue && contract.DataInizioSubentro.Value.Date > contract.DataFine.Value.Date)
                AddNonConform(contract.SubentroState, "SUB_005", "Il subentro è successivo alla scadenza originaria del contratto.", "Correggere la data del subentro.");
        }

        private static void ValidateExtensions(ContrattoControlRecord contract)
        {
            DateTime? previousEnd = contract.DataFine?.Date;
            foreach (var extension in contract.Proroghe.OrderBy(x => x.DataDecorrenza).ThenBy(x => x.Id))
            {
                if (!DomicilioUtils.IsValidSerie(extension.NumeroSerie))
                    AddNonConform(extension.State, "PRO_001", "Gli estremi della proroga sono assenti o formalmente non validi.", "Confrontare gli estremi con il documento di proroga.");
                if (!IsValidDate(extension.DataDecorrenza) || !IsValidDate(extension.DataScadenza))
                    AddNonConform(extension.State, "PRO_002", "Una data della proroga non è valida.", "Correggere il periodo della proroga.");
                if (extension.DataDecorrenza.Date > extension.DataScadenza.Date)
                    AddNonConform(extension.State, "PRO_003", "La decorrenza della proroga è successiva alla scadenza.", "Correggere il periodo della proroga.");
                if (string.Equals(NormalizeReference(extension.NumeroSerie), NormalizeReference(contract.NumeroSerie), StringComparison.OrdinalIgnoreCase))
                    AddManual(extension.State, "PRO_004", "Gli estremi della proroga coincidono con quelli del contratto.", "Verificare che siano stati indicati gli estremi corretti della proroga.");

                if (previousEnd.HasValue)
                {
                    var overlapDays = (previousEnd.Value.Date - extension.DataDecorrenza.Date).TotalDays;
                    if (overlapDays > TemporalToleranceDays)
                        AddNonConform(extension.State, "PRO_005", $"La proroga si sovrappone al periodo precedente per più di {TemporalToleranceDays} giorni.", "Correggere la decorrenza della proroga.");
                    else if (extension.DataDecorrenza.Date > previousEnd.Value.Date.AddDays(1))
                        AddManual(extension.State, "PRO_006", $"Tra il periodo precedente e la proroga c'è un'interruzione dal {previousEnd.Value.AddDays(1):dd/MM/yyyy} al {extension.DataDecorrenza.AddDays(-1):dd/MM/yyyy}.", "Verificare l'interruzione; è consentita ma va valutata dall'ufficio.");
                }

                previousEnd = !previousEnd.HasValue || extension.DataScadenza.Date > previousEnd.Value.Date
                    ? extension.DataScadenza.Date
                    : previousEnd;
            }
        }

        private static void ValidateCessation(ContrattoControlRecord contract)
        {
            if (!contract.DataCessazione.HasValue)
                return;

            var cessation = contract.DataCessazione.Value.Date;
            var naturalEnd = new[] { contract.DataFine }
                .Concat(contract.Proroghe.Select(x => (DateTime?)x.DataScadenza))
                .Where(x => x.HasValue)
                .Select(x => x!.Value.Date)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            if (!IsValidDate(cessation))
                AddNonConform(contract.State, "CES_001", "La data di cessazione non è valida.", "Correggere la data di cessazione.");
            if (cessation < contract.EffectiveStart)
                AddNonConform(contract.State, "CES_002", "La cessazione precede la decorrenza effettiva del contratto.", "Correggere la data di cessazione.");
            if (naturalEnd != DateTime.MinValue && cessation > naturalEnd)
                AddNonConform(contract.State, "CES_003", "La cessazione è successiva all'ultima scadenza del contratto, comprese le proroghe.", "Correggere la data di cessazione.");
        }

        private static void ValidateDomicileCoverage(DomicilioControlRecord domicile)
        {
            var starts = domicile.Contratti.Select(x => x.EffectiveStart).Where(IsValidDate).ToList();
            var ends = domicile.Contratti.Select(x => x.EffectiveEnd).Where(x => x.HasValue).Select(x => x!.Value.Date).ToList();
            if (starts.Count == 0 || ends.Count == 0)
                return;

            var calculatedStart = starts.Min();
            var calculatedEnd = ends.Max();
            if (domicile.DataInizio.HasValue && Math.Abs((domicile.DataInizio.Value.Date - calculatedStart).TotalDays) > TemporalToleranceDays)
                AddManual(domicile.State, "DOM_012", $"L'inizio del domicilio ({domicile.DataInizio:dd/MM/yyyy}) non coincide con la prima copertura contrattuale ({calculatedStart:dd/MM/yyyy}).", "Verificare il periodo complessivo del domicilio.");
            if (domicile.DataFine.HasValue && Math.Abs((domicile.DataFine.Value.Date - calculatedEnd).TotalDays) > TemporalToleranceDays)
                AddManual(domicile.State, "DOM_013", $"La fine del domicilio ({domicile.DataFine:dd/MM/yyyy}) non coincide con l'ultima copertura contrattuale ({calculatedEnd:dd/MM/yyyy}).", "Verificare il periodo complessivo del domicilio.");
        }

        private static void ValidateContractOverlaps(List<DomicilioControlRecord> domiciles)
        {
            foreach (var studentGroup in domiciles.GroupBy(x => x.CodFiscale, StringComparer.OrdinalIgnoreCase))
            {
                var contracts = studentGroup
                    .SelectMany(d => d.Contratti.Select(c => (Domicile: d, Contract: c)))
                    .OrderBy(x => x.Contract.EffectiveStart)
                    .ThenBy(x => x.Contract.Id)
                    .ToList();

                for (var i = 0; i < contracts.Count; i++)
                {
                    for (var j = i + 1; j < contracts.Count; j++)
                    {
                        var first = contracts[i];
                        var second = contracts[j];
                        if (!first.Contract.EffectiveEnd.HasValue || !second.Contract.EffectiveEnd.HasValue)
                        {
                            if (!first.Contract.EffectiveEnd.HasValue)
                                AddManual(first.Contract.State, "CON_012", "Non è possibile verificare le sovrapposizioni perché manca la scadenza effettiva del contratto.", "Verificare manualmente il periodo contrattuale.");
                            if (!second.Contract.EffectiveEnd.HasValue)
                                AddManual(second.Contract.State, "CON_012", "Non è possibile verificare le sovrapposizioni perché manca la scadenza effettiva del contratto.", "Verificare manualmente il periodo contrattuale.");
                            continue;
                        }

                        if (!PeriodsOverlapBeyondTolerance(
                                first.Contract.EffectiveStart,
                                first.Contract.EffectiveEnd.Value,
                                second.Contract.EffectiveStart,
                                second.Contract.EffectiveEnd.Value))
                            continue;

                        var message = $"Il contratto {first.Contract.Id} del domicilio {first.Domicile.Id} e il contratto {second.Contract.Id} del domicilio {second.Domicile.Id} si sovrappongono per più di {TemporalToleranceDays} giorni.";
                        AddNonConform(first.Contract.State, "CON_013", message, "Risoluzione necessaria: uno studente può avere un solo contratto alla volta considerando tutti i domicili.");
                        AddNonConform(second.Contract.State, "CON_013", message, "Risoluzione necessaria: uno studente può avere un solo contratto alla volta considerando tutti i domicili.");
                    }
                }
            }
        }

        private static List<PdfWorkItem> BuildPdfWork(List<DomicilioControlRecord> domiciles)
        {
            var work = new List<PdfWorkItem>();
            foreach (var domicile in domiciles)
            {
                foreach (var contract in domicile.Contratti)
                {
                    if (contract.IsLocazione)
                    {
                        AddRequiredDocuments(
                            work,
                            contract.AllegatiContratto,
                            contract.State,
                            new DomicilioPdfExpectation(
                                DomicilioPdfDocumentType.Contratto,
                                domicile.CodFiscale,
                                contract.NumeroSerie,
                                contract.DataRegistrazione,
                                contract.DataInizio,
                                contract.DataFine,
                                null,
                                RequireFiscalCode: !contract.HasSubentro),
                            "DOC_001",
                            "Non risulta un PDF attivo del contratto di locazione.",
                            "Recuperare o richiedere il contratto allo studente.");
                    }

                    if (contract.HasSubentro)
                    {
                        AddRequiredDocuments(
                            work,
                            contract.AllegatiSubentro,
                            contract.SubentroState,
                            new DomicilioPdfExpectation(
                                DomicilioPdfDocumentType.Subentro,
                                domicile.CodFiscale,
                                contract.NumeroSerieSubentro,
                                null,
                                contract.DataInizioSubentro,
                                null,
                                contract.NumeroSerie),
                            "DOC_002",
                            "Non risulta un PDF attivo del subentro.",
                            "Recuperare o richiedere il documento di subentro allo studente.");
                    }

                    foreach (var extension in contract.Proroghe)
                    {
                        AddRequiredDocuments(
                            work,
                            extension.Allegati,
                            extension.State,
                            new DomicilioPdfExpectation(
                                DomicilioPdfDocumentType.Proroga,
                                domicile.CodFiscale,
                                extension.NumeroSerie,
                                null,
                                extension.DataDecorrenza,
                                extension.DataScadenza,
                                contract.NumeroSerie),
                            "DOC_003",
                            "Non risulta un PDF attivo della proroga.",
                            "Recuperare o richiedere il documento di proroga allo studente.");
                    }
                }
            }

            return work;
        }

        private static void AddRequiredDocuments(
            List<PdfWorkItem> work,
            List<DomicilioAttachmentRecord> attachments,
            DomicilioControlState destination,
            DomicilioPdfExpectation expectation,
            string missingCode,
            string missingMessage,
            string missingAction)
        {
            if (attachments.Count == 0)
            {
                AddNonConform(destination, missingCode, missingMessage, missingAction);
                return;
            }

            if (attachments.Count > 1)
                AddManual(destination, "DOC_004", $"Sono presenti {attachments.Count} allegati attivi per lo stesso elemento.", "Verificare quale documento debba essere considerato valido.");

            foreach (var attachment in attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.CodStatus))
                    AddManual(destination, "DOC_005", $"L'allegato {attachment.IdAllegato:0} non ha uno stato attivo.", "Verificare lo stato dell'allegato.");
                if (!string.Equals(attachment.CodFiscale, expectation.CodFiscale, StringComparison.OrdinalIgnoreCase))
                    AddNonConform(destination, "DOC_006", $"L'allegato {attachment.IdAllegato:0} è associato a un codice fiscale diverso.", "Correggere l'associazione dell'allegato.");
                work.Add(new PdfWorkItem(attachment, expectation, destination));
            }
        }

        private void AnalyzePdfDocuments(List<PdfWorkItem> work)
        {
            if (work.Count == 0)
                return;

            var byAttachment = work
                .GroupBy(x => x.Attachment.IdAllegato)
                .ToDictionary(x => x.Key, x => x.ToList());
            var ids = byAttachment.Keys.OrderBy(x => x).ToList();
            var parallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, PdfMaxParallelism));
            var queueCapacity = Math.Max(parallelism, parallelism * PdfQueueCapacityPerWorker);
            Logger.LogInfo(35,
                $"Selezione SQL in un'unica scansione; download BLOB fino a {PdfSqlDownloadMaxParallelism} connessioni e " +
                $"analisi iText con {parallelism} processi paralleli (coda massima {queueCapacity} PDF).");

            CreateAndPopulatePdfIdTable(ids);

            using var queue = new BlockingCollection<LoadedPdf>(queueCapacity);
            var completed = new ConcurrentBag<CompletedPdfAnalysis>();
            var elapsed = Stopwatch.StartNew();
            var progressLock = new object();
            var processedAttachments = 0;
            var lastLogged = 0;
            long analyzedDocuments = 0;
            long totalPagesRead = 0;
            long totalPages = 0;

            void AnalyzeLoadedPdf(LoadedPdf file)
            {
                foreach (var workItem in byAttachment[file.IdAllegato])
                {
                    DomicilioPdfAnalysis analysis;
                    try
                    {
                        analysis = ControlloDomicilioPdfAnalyzer.Analyze(file.Content, workItem.Expectation);
                    }
                    catch (Exception)
                    {
                        analysis = new DomicilioPdfAnalysis();
                        analysis.State.Add(
                            DomicilioControlSeverity.VerificaManuale,
                            "PDF_099",
                            "Si è verificato un errore imprevisto durante l'analisi automatica del PDF.",
                            "Aprire e verificare manualmente il documento.");
                    }

                    completed.Add(new CompletedPdfAnalysis(
                        workItem,
                        analysis,
                        file.FileName,
                        file.ContentType));
                    Interlocked.Increment(ref analyzedDocuments);
                    Interlocked.Add(ref totalPagesRead, analysis.PagesRead);
                    Interlocked.Add(ref totalPages, analysis.PageCount);
                }

                var processed = Interlocked.Increment(ref processedAttachments);
                if (processed % PdfProgressInterval != 0 && processed != ids.Count)
                    return;

                lock (progressLock)
                {
                    if (processed <= lastLogged)
                        return;
                    lastLogged = processed;
                    var rate = processed / Math.Max(0.001d, elapsed.Elapsed.TotalSeconds);
                    var remainingSeconds = rate <= 0d ? 0d : (ids.Count - processed) / rate;
                    var progress = 35 + (int)(45d * processed / ids.Count);
                    var documentCount = Math.Max(1L, Interlocked.Read(ref analyzedDocuments));
                    var averagePages = (double)Interlocked.Read(ref totalPagesRead) / documentCount;
                    Logger.LogInfo(progress,
                        $"Analizzati {processed} di {ids.Count} allegati ({rate:F1} PDF/s; " +
                        $"{averagePages:F1} pagine lette/PDF; coda {queue.Count}/{queueCapacity}; " +
                        $"tempo residuo stimato {FormatDuration(remainingSeconds)}).");
                }
            }

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Factory.StartNew(
                    () =>
                    {
                        foreach (var file in queue.GetConsumingEnumerable())
                            AnalyzeLoadedPdf(file);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            try
            {
                StreamPdfDocuments(byAttachment, queue);
            }
            finally
            {
                queue.CompleteAdding();
                Task.WaitAll(workers);
            }

            // Il merge è sequenziale e ordinato: nessuna struttura di esito viene
            // modificata dai worker e il CSV rimane deterministico.
            foreach (var result in completed
                         .OrderBy(x => x.WorkItem.Attachment.IdAllegato)
                         .ThenBy(x => x.WorkItem.Expectation.Type))
            {
                result.WorkItem.Attachment.FileName = result.FileName;
                result.WorkItem.Attachment.ContentType = result.ContentType;
                result.WorkItem.Attachment.Analysis = result.Analysis;
                result.WorkItem.DestinationState.Merge(result.Analysis.State);
            }

            var finalDocumentCount = Math.Max(1L, analyzedDocuments);
            Logger.LogInfo(80,
                $"Analisi PDF completata in {FormatDuration(elapsed.Elapsed.TotalSeconds)}: {ids.Count} allegati, " +
                $"media {(ids.Count / Math.Max(0.001d, elapsed.Elapsed.TotalSeconds)):F1} PDF/s; " +
                $"lette {(double)totalPagesRead / finalDocumentCount:F1} pagine/PDF su " +
                $"{(double)totalPages / finalDocumentCount:F1} presenti.");
        }

        private void CreateAndPopulatePdfIdTable(IReadOnlyCollection<decimal> ids)
        {
            using (var command = CONNECTION!.CreateCommand())
            {
                command.CommandTimeout = 120;
                command.CommandText = @"
IF OBJECT_ID('tempdb..#DOMICILIO_PDF_IDS') IS NOT NULL
    DROP TABLE #DOMICILIO_PDF_IDS;

CREATE TABLE #DOMICILIO_PDF_IDS
(
    ID_ALLEGATO DECIMAL(18, 0) NOT NULL PRIMARY KEY
);";
                command.ExecuteNonQuery();
            }

            using var table = new DataTable();
            table.Columns.Add("ID_ALLEGATO", typeof(decimal));
            foreach (var id in ids)
                table.Rows.Add(id);

            using var bulk = new SqlBulkCopy(CONNECTION!, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "#DOMICILIO_PDF_IDS",
                BatchSize = 2000,
                BulkCopyTimeout = 120,
                EnableStreaming = true
            };
            bulk.ColumnMappings.Add("ID_ALLEGATO", "ID_ALLEGATO");
            bulk.WriteToServer(table);
        }

        private void StreamPdfDocuments(
            IReadOnlyDictionary<decimal, List<PdfWorkItem>> byAttachment,
            BlockingCollection<LoadedPdf> destination)
        {
            var streamElapsed = Stopwatch.StartNew();
            var descriptors = LoadPdfDescriptors(byAttachment);
            var downloadable = descriptors.Where(x => x.PhysicalFileId.HasValue).ToList();
            long missingQueueWaitTicks = 0;

            foreach (var descriptor in descriptors.Where(x => !x.PhysicalFileId.HasValue))
            {
                var queueWaitStart = Stopwatch.GetTimestamp();
                destination.Add(new LoadedPdf(
                    descriptor.IdAllegato,
                    null,
                    descriptor.FileName,
                    descriptor.ContentType));
                missingQueueWaitTicks += Stopwatch.GetTimestamp() - queueWaitStart;
            }

            var requestedConnections = Math.Max(
                1,
                Math.Min(PdfSqlDownloadMaxParallelism, downloadable.Count));
            var connections = new List<SqlConnection>(requestedConnections);
            var metrics = new List<PdfStreamMetrics>();

            try
            {
                for (var index = 0; index < requestedConnections; index++)
                {
                    var clonedConnection = (SqlConnection)((ICloneable)CONNECTION!).Clone();
                    clonedConnection.Open();
                    connections.Add(clonedConnection);
                }
            }
            catch (Exception ex)
            {
                foreach (var connection in connections)
                    connection.Dispose();
                connections.Clear();

                Logger.LogWarning(null,
                    $"Download SQL parallelo non disponibile ({ex.GetBaseException().Message}). " +
                    "La procedura prosegue con la connessione principale.");
            }

            if (connections.Count == 0)
            {
                metrics.Add(StreamPdfShard(CONNECTION!, downloadable, destination));
            }
            else
            {
                var shards = PartitionPdfDescriptors(downloadable, connections.Count);
                Logger.LogInfo(null,
                    $"Download di {downloadable.Count} BLOB distribuito su {connections.Count} connessioni SQL parallele.");

                try
                {
                    var downloadTasks = connections
                        .Select((connection, index) => Task.Run(() =>
                            StreamPdfShard(connection, shards[index], destination)))
                        .ToArray();
                    Task.WaitAll(downloadTasks);
                    metrics.AddRange(downloadTasks.Select(x => x.Result));
                }
                finally
                {
                    foreach (var connection in connections)
                        connection.Dispose();
                }
            }

            var streamedDocuments = metrics.Sum(x => x.Documents) + descriptors.Count(x => !x.PhysicalFileId.HasValue);
            var totalBytes = metrics.Sum(x => x.Bytes);
            var queueWaitTicks = metrics.Sum(x => x.QueueWaitTicks) + missingQueueWaitTicks;
            var queueWait = TimeSpan.FromSeconds((double)queueWaitTicks / Stopwatch.Frequency);
            var effectiveReadSeconds = Math.Max(0.001d, streamElapsed.Elapsed.TotalSeconds);
            var megabytes = totalBytes / 1024d / 1024d;
            Logger.LogInfo(null,
                $"Flusso SQL completato: {streamedDocuments} allegati, {megabytes:F1} MB; " +
                $"tempo totale {FormatDuration(effectiveReadSeconds)} ({megabytes / effectiveReadSeconds:F1} MB/s), " +
                $"attesa cumulativa della coda iText {FormatDuration(queueWait.TotalSeconds)}.");
        }

        private List<PdfDescriptor> LoadPdfDescriptors(
            IReadOnlyDictionary<decimal, List<PdfWorkItem>> byAttachment)
        {
            using var command = CONNECTION!.CreateCommand();
            command.CommandTimeout = 3600;
            command.CommandText = $@"
;WITH RANKED_ALLEGATI AS
(
    SELECT
        a.id_allegato AS ID_FILE,
        a.Id_Allegato_Adisu AS ID_ALLEGATO,
        ROW_NUMBER() OVER
        (
            PARTITION BY a.Id_Allegato_Adisu
            ORDER BY a.data_validita DESC, a.id_allegato DESC
        ) AS RN,
        COUNT_BIG(*) OVER (PARTITION BY a.Id_Allegato_Adisu) AS ACTIVE_COUNT
    FROM [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a
    INNER JOIN #DOMICILIO_PDF_IDS richiesti
        ON richiesti.ID_ALLEGATO = a.Id_Allegato_Adisu
    WHERE a.data_fine_validita IS NULL
)
SELECT
    richiesti.ID_ALLEGATO AS Id_Allegato_Adisu,
    ISNULL(selezionato.ACTIVE_COUNT, 0) AS ACTIVE_COUNT,
    selezionato.ID_FILE,
    ISNULL(a.doc_name, '') AS doc_name,
    ISNULL(a.doc_type, '') AS doc_type,
    ISNULL(DATALENGTH(a.[file]), 0) AS CONTENT_LENGTH
FROM #DOMICILIO_PDF_IDS richiesti
LEFT JOIN RANKED_ALLEGATI selezionato
    ON selezionato.ID_ALLEGATO = richiesti.ID_ALLEGATO
   AND selezionato.RN = 1
LEFT JOIN [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a
    ON a.id_allegato = selezionato.ID_FILE
ORDER BY richiesti.ID_ALLEGATO
OPTION (RECOMPILE);";

            var descriptors = new List<PdfDescriptor>(byAttachment.Count);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = ReadDecimal(reader, "Id_Allegato_Adisu");
                var activeCount = Convert.ToInt64(reader["ACTIVE_COUNT"], CultureInfo.InvariantCulture);
                var fileName = ReadString(reader, "doc_name");
                var contentType = ReadString(reader, "doc_type");
                var physicalFileId = ReadNullableDecimal(reader, "ID_FILE");
                var contentLength = Convert.ToInt64(reader["CONTENT_LENGTH"], CultureInfo.InvariantCulture);

                if (activeCount > 1 && byAttachment.TryGetValue(id, out var duplicatedItems))
                {
                    foreach (var item in duplicatedItems)
                        AddManual(item.DestinationState, "DOC_007", $"In ADISU_ALLEGATI esistono {activeCount} file attivi per l'allegato {id:0}.", "Verificare e mantenere un solo file attivo.");
                }

                descriptors.Add(new PdfDescriptor(
                    id,
                    physicalFileId,
                    contentLength,
                    fileName,
                    contentType));
            }

            return descriptors;
        }

        private static List<List<PdfDescriptor>> PartitionPdfDescriptors(
            IReadOnlyCollection<PdfDescriptor> descriptors,
            int partitionsCount)
        {
            var partitions = Enumerable.Range(0, partitionsCount)
                .Select(_ => new List<PdfDescriptor>())
                .ToList();
            var partitionSizes = new long[partitionsCount];

            foreach (var descriptor in descriptors
                         .OrderByDescending(x => x.ContentLength)
                         .ThenBy(x => x.IdAllegato))
            {
                var target = 0;
                for (var index = 1; index < partitionSizes.Length; index++)
                {
                    if (partitionSizes[index] < partitionSizes[target])
                        target = index;
                }

                partitions[target].Add(descriptor);
                partitionSizes[target] += descriptor.ContentLength;
            }

            return partitions;
        }

        private static PdfStreamMetrics StreamPdfShard(
            SqlConnection connection,
            IReadOnlyCollection<PdfDescriptor> descriptors,
            BlockingCollection<LoadedPdf> destination)
        {
            if (descriptors.Count == 0)
                return new PdfStreamMetrics(0, 0, 0);

            using (var command = connection.CreateCommand())
            {
                command.CommandTimeout = 120;
                command.CommandText = @"
IF OBJECT_ID('tempdb..#DOMICILIO_PDF_FILES') IS NOT NULL
    DROP TABLE #DOMICILIO_PDF_FILES;

CREATE TABLE #DOMICILIO_PDF_FILES
(
    ID_ALLEGATO DECIMAL(18, 0) NOT NULL PRIMARY KEY,
    ID_FILE DECIMAL(18, 0) NOT NULL
);";
                command.ExecuteNonQuery();
            }

            using (var table = new DataTable())
            {
                table.Columns.Add("ID_ALLEGATO", typeof(decimal));
                table.Columns.Add("ID_FILE", typeof(decimal));
                foreach (var descriptor in descriptors)
                    table.Rows.Add(descriptor.IdAllegato, descriptor.PhysicalFileId!.Value);

                using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
                {
                    DestinationTableName = "#DOMICILIO_PDF_FILES",
                    BatchSize = 2000,
                    BulkCopyTimeout = 120,
                    EnableStreaming = true
                };
                bulk.ColumnMappings.Add("ID_ALLEGATO", "ID_ALLEGATO");
                bulk.ColumnMappings.Add("ID_FILE", "ID_FILE");
                bulk.WriteToServer(table);
            }

            using var readCommand = connection.CreateCommand();
            readCommand.CommandTimeout = 3600;
            readCommand.CommandText = $@"
SELECT
    richiesti.ID_ALLEGATO,
    a.[file]
FROM #DOMICILIO_PDF_FILES richiesti
LEFT JOIN [{AllegatiDatabase}].DIRSTUDIO.ALLEGATI a
    ON a.id_allegato = richiesti.ID_FILE
ORDER BY richiesti.ID_ALLEGATO;";

            var byId = descriptors.ToDictionary(x => x.IdAllegato);
            long queueWaitTicks = 0;
            long totalBytes = 0;
            var documents = 0;
            using var reader = readCommand.ExecuteReader(CommandBehavior.SequentialAccess);
            while (reader.Read())
            {
                var id = ReadDecimal(reader, "ID_ALLEGATO");
                var descriptor = byId[id];
                var content = ReadBytes(reader, "file");
                totalBytes += content?.LongLength ?? 0L;
                documents++;

                var queueWaitStart = Stopwatch.GetTimestamp();
                destination.Add(new LoadedPdf(
                    id,
                    content,
                    descriptor.FileName,
                    descriptor.ContentType));
                queueWaitTicks += Stopwatch.GetTimestamp() - queueWaitStart;
            }

            return new PdfStreamMetrics(documents, totalBytes, queueWaitTicks);
        }

        private static List<DomicilioCsvRow> BuildCsvRows(List<DomicilioControlRecord> domiciles)
        {
            var rows = new List<DomicilioCsvRow>();
            foreach (var domicile in domiciles.OrderBy(x => x.CodFiscale).ThenBy(x => x.Id))
            {
                // La propagazione è esclusivamente discendente e segue ogni singolo
                // ramo: DOMICILIO -> SUBENTRO -> CONTRATTO -> PROROGA.
                // Un'anomalia di un figlio non può quindi peggiorare il proprio padre.
                var domicileState = domicile.State.Copy();
                rows.Add(CreateRow(domicile, null, null, null, "DOMICILIO", domicileState));

                foreach (var contract in domicile.Contratti.OrderBy(x => x.EffectiveStart).ThenBy(x => x.Id))
                {
                    DomicilioControlState? subentryState = null;
                    if (contract.HasSubentro)
                    {
                        subentryState = ApplyHierarchicalOutcome(
                            contract.SubentroState,
                            domicileState,
                            "DOMICILIO",
                            "GER_001");
                        AddRowsForAttachments(
                            rows,
                            domicile,
                            contract,
                            null,
                            contract.AllegatiSubentro,
                            "SUBENTRO",
                            subentryState);
                    }

                    var contractParentState = subentryState ?? domicileState;
                    var contractState = ApplyHierarchicalOutcome(
                        contract.State,
                        contractParentState,
                        contract.HasSubentro ? "SUBENTRO" : "DOMICILIO",
                        contract.HasSubentro ? "GER_002" : "GER_001");
                    AddRowsForAttachments(
                        rows,
                        domicile,
                        contract,
                        null,
                        contract.AllegatiContratto,
                        "CONTRATTO",
                        contractState);

                    foreach (var extension in contract.Proroghe.OrderBy(x => x.DataDecorrenza).ThenBy(x => x.Id))
                    {
                        var extensionState = ApplyHierarchicalOutcome(
                            extension.State,
                            contractState,
                            "CONTRATTO",
                            "GER_003");
                        AddRowsForAttachments(
                            rows,
                            domicile,
                            contract,
                            extension,
                            extension.Allegati,
                            "PROROGA",
                            extensionState);
                    }
                }
            }

            return rows;
        }

        private static DomicilioControlState ApplyHierarchicalOutcome(
            DomicilioControlState ownState,
            DomicilioControlState parentState,
            string parentGroup,
            string hierarchyCode)
        {
            var effectiveState = ownState.Copy();
            if (parentState.Severity <= effectiveState.Severity)
                return effectiveState;

            effectiveState.Add(
                parentState.Severity,
                hierarchyCode,
                $"L'esito del gruppo è stato limitato a {parentState.Outcome} dall'esito del livello {parentGroup}.",
                $"Risolvere prima le anomalie presenti nel livello {parentGroup}.");
            return effectiveState;
        }

        private static void AddRowsForAttachments(
            List<DomicilioCsvRow> rows,
            DomicilioControlRecord domicile,
            ContrattoControlRecord contract,
            ProrogaControlRecord? extension,
            List<DomicilioAttachmentRecord> attachments,
            string element,
            DomicilioControlState state)
        {
            if (attachments.Count == 0)
            {
                rows.Add(CreateRow(domicile, contract, extension, null, element, state));
                return;
            }

            foreach (var attachment in attachments.OrderByDescending(x => x.DataValidita))
                rows.Add(CreateRow(domicile, contract, extension, attachment, element, state));
        }

        private static DomicilioCsvRow CreateRow(
            DomicilioControlRecord domicile,
            ContrattoControlRecord? contract,
            ProrogaControlRecord? extension,
            DomicilioAttachmentRecord? attachment,
            string element,
            DomicilioControlState state)
        {
            var analysis = attachment?.Analysis;
            var status = attachment == null
                ? string.Empty
                : string.IsNullOrWhiteSpace(attachment.StatusDescription)
                    ? attachment.CodStatus
                    : $"{attachment.StatusDescription} ({attachment.CodStatus})";

            return new DomicilioCsvRow
            {
                AnnoAccademico = domicile.AnnoAccademico,
                NumDomanda = DomicilioCsvRow.Decimal(domicile.NumDomanda),
                CodFiscale = domicile.CodFiscale,
                Studente = domicile.Studente,
                Elemento = element,
                IdDomicilio = domicile.Id.ToString(CultureInfo.InvariantCulture),
                ComuneDomicilio = domicile.Comune,
                IndirizzoDomicilio = BuildAddress(domicile),
                TitoloDomicilio = domicile.TitoloDescrizione,
                DataInizioDomicilio = DomicilioCsvRow.Date(domicile.DataInizio),
                DataFineDomicilio = DomicilioCsvRow.Date(domicile.DataFine),
                IdContratto = contract?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                TipoContratto = contract?.TipoDescrizione ?? string.Empty,
                SerieContratto = contract?.NumeroSerie ?? string.Empty,
                DataRegistrazioneContratto = DomicilioCsvRow.Date(contract?.DataRegistrazione),
                DataDecorrenzaContratto = DomicilioCsvRow.Date(contract?.DataInizio),
                DataScadenzaContratto = DomicilioCsvRow.Date(contract?.DataFine),
                SerieSubentro = contract?.NumeroSerieSubentro ?? string.Empty,
                DataSubentro = DomicilioCsvRow.Date(contract?.DataInizioSubentro),
                DataCessazione = DomicilioCsvRow.Date(contract?.DataCessazione),
                IdProroga = extension?.Id.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                SerieProroga = extension?.NumeroSerie ?? string.Empty,
                DataDecorrenzaProroga = DomicilioCsvRow.Date(extension?.DataDecorrenza),
                DataScadenzaProroga = DomicilioCsvRow.Date(extension?.DataScadenza),
                IdAllegato = attachment?.IdAllegato.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty,
                StatoAllegato = status,
                NomeFile = attachment?.FileName ?? string.Empty,
                PdfValido = analysis == null ? string.Empty : DomicilioCsvRow.Bool(analysis.PdfValid),
                PdfLeggibile = analysis == null ? string.Empty : DomicilioCsvRow.Bool(analysis.Readable),
                TipoDocumentoRiconosciuto = analysis == null ? string.Empty : DomicilioCsvRow.Bool(analysis.TypeRecognized),
                CodiceFiscaleNelPdf = analysis == null ? string.Empty : DomicilioCsvRow.Bool(analysis.FiscalCodePresent),
                DatiPdfCongruenti = analysis == null ? string.Empty : DomicilioCsvRow.Bool(analysis.DataCoherent),
                DatiEstrattiDalPdf = analysis?.ExtractedData ?? string.Empty,
                Esito = state.Outcome,
                Priorita = state.Priority,
                CodiciAnomalia = state.Codes,
                Anomalie = state.Descriptions,
                AzioneUfficio = state.OfficeActions
            };
        }

        private static string ExportCsv(List<DomicilioCsvRow> rows, string folderPath, string academicYear)
        {
            var fileName = $"Controllo_Domicili_{academicYear}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var outputPath = Path.Combine(folderPath, fileName);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine(string.Join(";", DomicilioCsvRow.Headers.Select(EscapeCsv)));
            foreach (var row in rows)
                writer.WriteLine(string.Join(";", row.Values.Select(EscapeCsv)));
            return outputPath;
        }

        private static string EscapeCsv(string? value)
        {
            var text = value ?? string.Empty;
            if (!text.Contains(';') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
                return text;
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        private static bool PeriodsOverlapBeyondTolerance(DateTime firstStart, DateTime firstEnd, DateTime secondStart, DateTime secondEnd)
        {
            var overlapStart = firstStart.Date > secondStart.Date ? firstStart.Date : secondStart.Date;
            var overlapEnd = firstEnd.Date < secondEnd.Date ? firstEnd.Date : secondEnd.Date;
            return overlapEnd >= overlapStart && (overlapEnd - overlapStart).TotalDays > TemporalToleranceDays;
        }

        private static bool IsValidDate(DateTime? value)
            => !value.HasValue || (value.Value.Date >= MinDate && value.Value.Date <= MaxDate);

        private static bool IsValidDate(DateTime value)
            => value.Date >= MinDate && value.Date <= MaxDate;

        private static bool TryPositiveAmount(string value)
        {
            var normalized = value.Trim().Replace("€", string.Empty).Replace(" ", string.Empty);
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("it-IT"), out var italian))
                return italian > 0;
            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant) && invariant > 0;
        }

        private static string NormalizeFiscalCode(string? value)
            => Regex.Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, @"\s+", string.Empty);

        private static string NormalizeReference(string? value)
            => Regex.Replace(value?.ToUpperInvariant() ?? string.Empty, @"[^A-Z0-9]", string.Empty);

        private static string BuildAddress(DomicilioControlRecord domicile)
            => string.Join(", ", new[]
            {
                domicile.Indirizzo.Trim(),
                domicile.NumeroCivico.Trim(),
                domicile.Cap.Trim()
            }.Where(x => !string.IsNullOrWhiteSpace(x)));

        private static void AddNonConform(DomicilioControlState state, string code, string description, string action)
            => state.Add(DomicilioControlSeverity.NonConforme, code, description, action);

        private static void AddManual(DomicilioControlState state, string code, string description, string action)
            => state.Add(DomicilioControlSeverity.VerificaManuale, code, description, action);

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d)
                return "meno di 1 secondo";

            var duration = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
            if (duration.TotalHours >= 1d)
                return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m";
            if (duration.TotalMinutes >= 1d)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
            return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))}s";
        }

        private static byte[]? ReadBytes(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
                return null;

            var length = reader.GetBytes(ordinal, 0, null, 0, 0);
            if (length > int.MaxValue)
                throw new InvalidDataException($"L'allegato supera la dimensione massima gestibile ({length} byte).");
            if (length == 0)
                return Array.Empty<byte>();

            var content = GC.AllocateUninitializedArray<byte>((int)length);
            long offset = 0;
            while (offset < length)
            {
                var count = (int)Math.Min(81920L, length - offset);
                var read = reader.GetBytes(ordinal, offset, content, (int)offset, count);
                if (read <= 0)
                    throw new EndOfStreamException("Il contenuto del PDF è terminato prima della dimensione dichiarata.");
                offset += read;
            }

            return content;
        }

        private static string ReadString(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static int ReadInt(SqlDataReader reader, string column)
            => Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

        private static int? ReadNullableInt(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static decimal ReadDecimal(SqlDataReader reader, string column)
            => Convert.ToDecimal(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

        private static decimal? ReadNullableDecimal(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static DateTime? ReadNullableDate(SqlDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture).Date;
        }
    }
}
