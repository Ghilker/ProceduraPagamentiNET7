using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed class IstanzaDomicilioData
    {
        private sealed record AttachmentMetadata(DomicilioAttachmentRecord Attachment, string AcademicYear, bool Active);

        private readonly Dictionary<decimal, AttachmentMetadata> _attachments = new();

        public IReadOnlyDictionary<string, IstanzaDomicilioComune> Comuni { get; private set; }
            = new Dictionary<string, IstanzaDomicilioComune>(StringComparer.OrdinalIgnoreCase);

        private const string RequestsSql = @"
;WITH richieste AS
(
    SELECT g.Num_istanza, g.Anno_accademico, g.Num_domanda, g.Cod_fiscale,
           st.data_validita AS DATA_PRESENTAZIONE, st.presa_carico, st.utente_presa_carico
    FROM dbo.Istanza_dati_generali g
    CROSS APPLY
    (
        SELECT TOP (1) s.data_validita, s.presa_carico, s.utente_presa_carico, s.Cod_status_istanza
        FROM dbo.Istanza_status s
        WHERE s.Num_istanza = g.Num_istanza AND s.data_fine_validita IS NULL
        ORDER BY s.data_validita DESC, s.Id_status_istanza DESC
    ) st
    WHERE g.Anno_accademico = @aa AND g.Data_fine_validita IS NULL
      AND g.esito_istanza IS NULL AND g.cod_tipo_istanza IN ('01', '04B')
      AND st.Cod_status_istanza = '01'
      AND EXISTS
          (SELECT 1 FROM dbo.Istanza_Domicilio_Operazioni o
           WHERE o.NUM_ISTANZA = g.Num_istanza AND o.DATA_FINE_VALIDITA IS NULL)
)
";

        public List<IstanzaDomicilioRecord> Load(SqlConnection connection, string academicYear, HashSet<string>? fiscalCodes)
        {
            _attachments.Clear();
            var sql = RequestsSql + @"
SELECT r.*, LTRIM(RTRIM(ISNULL(s.COGNOME, '') + ' ' + ISNULL(s.NOME, ''))) AS STUDENTE
FROM richieste r
LEFT JOIN dbo.Studente s ON s.COD_FISCALE = r.Cod_fiscale
ORDER BY r.DATA_PRESENTAZIONE, r.Num_istanza;
" + RequestsSql + @"
SELECT o.*
FROM dbo.Istanza_Domicilio_Operazioni o
INNER JOIN richieste r ON r.Num_istanza = o.NUM_ISTANZA
WHERE o.DATA_FINE_VALIDITA IS NULL
ORDER BY o.NUM_ISTANZA, o.ORDINE, o.ID;
" + RequestsSql + @"
SELECT r.Num_istanza, d.*, ISNULL(cm.DESCRIZIONE, '') AS COMUNE,
       ISNULL(cm.COD_PROVINCIA, '') AS COD_PROVINCIA
FROM dbo.Domicili d
INNER JOIN richieste r ON r.Cod_fiscale = d.COD_FISCALE
    AND r.Anno_accademico = d.ANNO_ACCADEMICO AND r.Num_domanda = d.NUM_DOMANDA
LEFT JOIN dbo.Comuni cm ON cm.COD_COMUNE = d.COD_COMUNE
WHERE d.TIPO_LUOGO = 'DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED = 0
ORDER BY r.Num_istanza, d.ID;
" + RequestsSql + @"
SELECT r.Num_istanza, c.*
FROM dbo.Contratti c
INNER JOIN dbo.Domicili d ON d.ID = c.ID_DOMICILIO
INNER JOIN richieste r ON r.Cod_fiscale = d.COD_FISCALE
    AND r.Anno_accademico = d.ANNO_ACCADEMICO AND r.Num_domanda = d.NUM_DOMANDA
WHERE d.TIPO_LUOGO = 'DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED = 0
  AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED = 0
ORDER BY r.Num_istanza, c.ID_DOMICILIO, c.ID;
" + RequestsSql + @"
SELECT r.Num_istanza, p.*
FROM dbo.Proroghe p
INNER JOIN dbo.Contratti c ON c.ID = p.ID_CONTRATTO
INNER JOIN dbo.Domicili d ON d.ID = c.ID_DOMICILIO
INNER JOIN richieste r ON r.Cod_fiscale = d.COD_FISCALE
    AND r.Anno_accademico = d.ANNO_ACCADEMICO AND r.Num_domanda = d.NUM_DOMANDA
WHERE d.TIPO_LUOGO = 'DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED = 0
  AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED = 0
  AND p.DATA_FINE_VALIDITA IS NULL AND p.IS_DELETED = 0
ORDER BY r.Num_istanza, p.ID_CONTRATTO, p.ID;
" + RequestsSql + @"
, contratti_correnti AS
(
    SELECT DISTINCT c.ID
    FROM richieste r
    INNER JOIN dbo.Domicili d ON d.COD_FISCALE = r.Cod_fiscale
        AND d.ANNO_ACCADEMICO = r.Anno_accademico AND d.NUM_DOMANDA = r.Num_domanda
    INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO = d.ID
    WHERE d.TIPO_LUOGO = 'DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED = 0
      AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED = 0
), ids_allegati AS
(
    SELECT v.ID
    FROM richieste r
    INNER JOIN dbo.Istanza_Domicilio_Operazioni o ON o.NUM_ISTANZA = r.Num_istanza
    CROSS APPLY (VALUES (o.ID_ALLEGATO_CONTRATTO), (o.ID_ALLEGATO_SUBENTRO),
                        (o.ID_ALLEGATO_PROROGA), (o.ID_ALLEGATO_LEARNING_AGREEMENT)) v(ID)
    WHERE o.DATA_FINE_VALIDITA IS NULL AND v.ID IS NOT NULL
    UNION
    SELECT a.id_allegato
    FROM contratti_correnti c
    INNER JOIN dbo.ALLEGATI a ON a.ID_CONTRATTO = c.ID
    WHERE a.data_fine_validita IS NULL AND a.cod_tipo_allegato IN ('05', '45', '34', '46')
    UNION
    SELECT a.id_allegato
    FROM contratti_correnti c
    INNER JOIN dbo.Proroghe p ON p.ID_CONTRATTO = c.ID
        AND p.DATA_FINE_VALIDITA IS NULL AND p.IS_DELETED = 0
    INNER JOIN dbo.ALLEGATI a ON a.ID_PROROGA = p.ID
    WHERE a.data_fine_validita IS NULL AND a.cod_tipo_allegato IN ('05', '45', '34', '46')
)
SELECT a.id_allegato, a.anno_accademico, a.cod_fiscale, a.num_domanda, a.cod_tipo_allegato,
       a.data_validita, a.data_fine_validita, a.ID_CONTRATTO, a.ID_PROROGA,
       ISNULL(st.cod_status, '') AS cod_status, ISNULL(st.DESCRIZIONE_STATUS, '') AS DESCRIZIONE_STATUS
FROM ids_allegati ids
INNER JOIN dbo.ALLEGATI a ON a.id_allegato = ids.ID
OUTER APPLY
(
    SELECT TOP (1) sa.cod_status, ISNULL(tsa.DESCRIZIONE, '') AS DESCRIZIONE_STATUS
    FROM dbo.STATUS_ALLEGATI sa
    LEFT JOIN dbo.TIPOLOGIE_STATUS_ALLEGATI tsa ON tsa.cod_status = sa.cod_status
    WHERE sa.id_allegato = a.id_allegato AND sa.data_fine_validita IS NULL
    ORDER BY sa.data_validita DESC, sa.Id_STATUS_ALLEGATI DESC
) st
ORDER BY a.id_allegato;

SELECT COD_COMUNE, ISNULL(DESCRIZIONE, '') AS DESCRIZIONE,
       ISNULL(COD_PROVINCIA, '') AS COD_PROVINCIA
FROM dbo.Comuni;
";

            using var command = new SqlCommand(sql, connection) { CommandTimeout = 900 };
            command.Parameters.Add("@aa", SqlDbType.Char, 8).Value = academicYear;
            var stageTimer = Stopwatch.StartNew();
            void LogLoaded(int progress, string description)
            {
                Logger.LogInfo(progress, $"{description} ({stageTimer.Elapsed.TotalSeconds:F1} s).");
                stageTimer.Restart();
            }
            using var reader = command.ExecuteReader();
            var requests = new List<IstanzaDomicilioRecord>();
            var requestById = new Dictionary<int, IstanzaDomicilioRecord>();
            var domicileById = new Dictionary<(int Request, int Id), DomicilioControlRecord>();
            var contractById = new Dictionary<(int Request, int Id), ContrattoControlRecord>();

            while (reader.Read())
            {
                var fiscalCode = ReadString(reader, "Cod_fiscale").ToUpperInvariant();
                if (fiscalCodes != null && !fiscalCodes.Contains(fiscalCode))
                    continue;
                var request = new IstanzaDomicilioRecord
                {
                    NumIstanza = ReadInt(reader, "Num_istanza"),
                    AnnoAccademico = ReadString(reader, "Anno_accademico"),
                    NumDomanda = ReadDecimal(reader, "Num_domanda"),
                    CodFiscale = fiscalCode,
                    Studente = ReadString(reader, "STUDENTE"),
                    DataPresentazione = ReadDate(reader, "DATA_PRESENTAZIONE") ?? DateTime.MinValue,
                    PresaCarico = ReadInt(reader, "presa_carico") == 1,
                    Operatore = ReadString(reader, "utente_presa_carico")
                };
                requests.Add(request);
                requestById.Add(request.NumIstanza, request);
            }
            LogLoaded(5, $"Caricate {requests.Count} istanze inviate");

            reader.NextResult();
            while (reader.Read())
            {
                if (!requestById.TryGetValue(ReadInt(reader, "NUM_ISTANZA"), out var request))
                    continue;
                request.Operazioni.Add(new IstanzaDomicilioOperation
                {
                    Id = ReadInt(reader, "ID"),
                    Ordine = ReadInt(reader, "ORDINE"),
                    Entita = ReadInt(reader, "ENTITA"),
                    Operazione = ReadInt(reader, "OPERAZIONE"),
                    IdOperazionePadre = ReadNullableInt(reader, "ID_OPERAZIONE_PADRE"),
                    IdDomicilio = ReadNullableInt(reader, "ID_DOMICILIO"),
                    IdContratto = ReadNullableInt(reader, "ID_CONTRATTO"),
                    IdProroga = ReadNullableInt(reader, "ID_PROROGA"),
                    IdAllegatoContratto = ReadNullableDecimal(reader, "ID_ALLEGATO_CONTRATTO"),
                    IdAllegatoSubentro = ReadNullableDecimal(reader, "ID_ALLEGATO_SUBENTRO"),
                    IdAllegatoProroga = ReadNullableDecimal(reader, "ID_ALLEGATO_PROROGA"),
                    IdAllegatoLearningAgreement = ReadNullableDecimal(reader, "ID_ALLEGATO_LEARNING_AGREEMENT"),
                    DataValiditaOrigine = ReadDate(reader, "DATA_VALIDITA_ORIGINE"),
                    DataModificaOrigine = ReadDate(reader, "DATA_MODIFICA_ORIGINE"),
                    PayloadJson = ReadString(reader, "PAYLOAD_JSON")
                });
            }
            LogLoaded(7, $"Caricate {requests.Sum(x => x.Operazioni.Count)} operazioni attive");

            reader.NextResult();
            while (reader.Read())
            {
                var requestId = ReadInt(reader, "Num_istanza");
                if (!requestById.TryGetValue(requestId, out var request))
                    continue;
                var domicile = new DomicilioControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    AnnoAccademico = request.AnnoAccademico,
                    NumDomanda = request.NumDomanda,
                    CodFiscale = request.CodFiscale,
                    Studente = request.Studente,
                    CodComune = ReadString(reader, "COD_COMUNE"),
                    CodProvincia = ReadString(reader, "COD_PROVINCIA"),
                    Comune = ReadString(reader, "COMUNE"),
                    Indirizzo = ReadString(reader, "INDIRIZZO"),
                    NumeroCivico = ReadString(reader, "NUMERO_CIVICO"),
                    Cap = ReadString(reader, "CAP"),
                    TipoDomicilio = ReadString(reader, "TIPO_DOMICILIO"),
                    DataInizio = ReadDate(reader, "DATA_INIZIO"),
                    DataFine = ReadDate(reader, "DATA_FINE"),
                    InserimentoDatiContratto = ReadNullableInt(reader, "INSERIMENTO_DATI_CONTRATTO") == 1,
                    DataValidita = ReadDate(reader, "DATA_VALIDITA") ?? DateTime.MinValue,
                    DataModifica = ReadDate(reader, "DATA_MODIFICA")
                };
                request.DomiciliCorrenti.Add(domicile);
                domicileById.Add((requestId, domicile.Id), domicile);
            }
            LogLoaded(9, $"Caricati {domicileById.Count} domicili correnti");

            reader.NextResult();
            while (reader.Read())
            {
                var requestId = ReadInt(reader, "Num_istanza");
                var domicileId = ReadInt(reader, "ID_DOMICILIO");
                if (!domicileById.TryGetValue((requestId, domicileId), out var domicile))
                    continue;
                var contract = new ContrattoControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    IdDomicilio = domicileId,
                    TipoContratto = ReadInt(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO"),
                    NumeroSerie = ReadString(reader, "N_SERIE_CONTRATTO"),
                    DataRegistrazione = ReadDate(reader, "DATA_REG_CONTRATTO"),
                    DataInizio = ReadDate(reader, "DATA_INIZIO") ?? DateTime.MinValue,
                    DataFine = ReadDate(reader, "DATA_FINE"),
                    TipoEnte = ReadString(reader, "TIPO_ENTE"),
                    DenominazioneEnte = ReadString(reader, "DENOM_ENTE"),
                    ImportoRata = ReadString(reader, "IMPORTO_RATA"),
                    NumeroSerieSubentro = ReadString(reader, "N_SERIE_SUBENTRO"),
                    DataInizioSubentro = ReadDate(reader, "DATA_INIZIO_SUBENTRO"),
                    DataCessazione = ReadDate(reader, "DATA_CESSAZIONE"),
                    DataValidita = ReadDate(reader, "DATA_VALIDITA") ?? DateTime.MinValue,
                    DataModifica = ReadDate(reader, "DATA_MODIFICA")
                };
                domicile.Contratti.Add(contract);
                contractById.Add((requestId, contract.Id), contract);
            }
            LogLoaded(11, $"Caricati {contractById.Count} contratti correnti");

            reader.NextResult();
            while (reader.Read())
            {
                var requestId = ReadInt(reader, "Num_istanza");
                var contractId = ReadInt(reader, "ID_CONTRATTO");
                if (!contractById.TryGetValue((requestId, contractId), out var contract))
                    continue;
                contract.Proroghe.Add(new ProrogaControlRecord
                {
                    Id = ReadInt(reader, "ID"),
                    IdContratto = contractId,
                    NumeroSerie = ReadString(reader, "N_SERIE_PROROGA"),
                    DataDecorrenza = ReadDate(reader, "DATA_DECORRENZA") ?? DateTime.MinValue,
                    DataScadenza = ReadDate(reader, "DATA_SCADENZA") ?? DateTime.MinValue,
                    DataValidita = ReadDate(reader, "DATA_VALIDITA") ?? DateTime.MinValue,
                    DataModifica = ReadDate(reader, "DATA_MODIFICA")
                });
            }
            LogLoaded(13, $"Caricate {contractById.Values.Sum(x => x.Proroghe.Count)} proroghe correnti");

            reader.NextResult();
            while (reader.Read())
            {
                var attachment = new DomicilioAttachmentRecord
                {
                    IdAllegato = ReadDecimal(reader, "id_allegato"),
                    CodTipoAllegato = ReadString(reader, "cod_tipo_allegato"),
                    CodFiscale = ReadString(reader, "cod_fiscale").ToUpperInvariant(),
                    NumDomanda = ReadNullableDecimal(reader, "num_domanda"),
                    IdContratto = ReadNullableInt(reader, "ID_CONTRATTO"),
                    IdProroga = ReadNullableInt(reader, "ID_PROROGA"),
                    DataValidita = ReadDate(reader, "data_validita") ?? DateTime.MinValue,
                    CodStatus = ReadString(reader, "cod_status"),
                    StatusDescription = ReadString(reader, "DESCRIZIONE_STATUS")
                };
                _attachments.Add(attachment.IdAllegato, new AttachmentMetadata(
                    attachment, ReadString(reader, "anno_accademico"), !ReadDate(reader, "data_fine_validita").HasValue));
            }
            LogLoaded(16, $"Caricati {_attachments.Count} metadati allegati");

            reader.NextResult();
            var comuni = new Dictionary<string, IstanzaDomicilioComune>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
                comuni[ReadString(reader, "COD_COMUNE")] = new IstanzaDomicilioComune(
                    ReadString(reader, "DESCRIZIONE"), ReadString(reader, "COD_PROVINCIA"));
            Comuni = comuni;
            LogLoaded(18, $"Caricati {Comuni.Count} comuni");
            BindCurrentAttachments(requests);
            return requests;
        }

        private void BindCurrentAttachments(IEnumerable<IstanzaDomicilioRecord> requests)
        {
            var current = _attachments.Values.Where(x => x.Active).Select(x => x.Attachment).ToList();
            var byContract = current.Where(x => x.IdContratto.HasValue).ToLookup(x => x.IdContratto!.Value);
            var byExtension = current.Where(x => x.IdProroga.HasValue).ToLookup(x => x.IdProroga!.Value);
            foreach (var request in requests)
            foreach (var contract in request.DomiciliCorrenti.SelectMany(x => x.Contratti))
            {
                foreach (var attachment in byContract[contract.Id])
                {
                    if (attachment.CodTipoAllegato == "05") contract.AllegatiContratto.Add(CloneAttachment(attachment));
                    else if (attachment.CodTipoAllegato == "45") contract.AllegatiSubentro.Add(CloneAttachment(attachment));
                    else if (attachment.CodTipoAllegato == "46") contract.AllegatiLearningAgreement.Add(CloneAttachment(attachment));
                }
                foreach (var extension in contract.Proroghe)
                foreach (var attachment in byExtension[extension.Id].Where(x => x.CodTipoAllegato == "34"))
                    extension.Allegati.Add(CloneAttachment(attachment));
            }
        }

        public void BindAttachments(IEnumerable<IstanzaDomicilioRecord> requests)
        {
            foreach (var request in requests)
            {
                var used = new HashSet<decimal>();
                var validProposed = new HashSet<(int Operation, decimal Attachment, string Type)>();
                foreach (var operation in request.Operazioni)
                {
                    var proposed = new[]
                    {
                        (operation.IdAllegatoContratto, "05"), (operation.IdAllegatoSubentro, "45"),
                        (operation.IdAllegatoProroga, "34"), (operation.IdAllegatoLearningAgreement, "46")
                    };
                    foreach (var (id, type) in proposed.Where(x => x.Item1.HasValue))
                    {
                        if (!used.Add(id!.Value))
                            AddFinding(operation, DomicilioControlSeverity.NonConforme, "IST_DOC_006",
                                $"L'allegato {id:0} è riutilizzato da più operazioni o per documenti diversi.");
                        if (ValidateAttachment(request, operation, id!.Value, type, proposed: true))
                            validProposed.Add((operation.Id, id.Value, type));
                    }
                }

                foreach (var operation in request.Operazioni)
                foreach (var document in operation.Documenti.Where(x => x.IdAllegato.HasValue))
                {
                    var id = document.IdAllegato!.Value;
                    if (document.Attachment != null)
                    {
                        if (!ValidateAttachment(request, operation, id, DocumentType(document.Tipo), proposed: false))
                            document.Attachment = null;
                    }
                    else if (validProposed.Contains((operation.Id, id, DocumentType(document.Tipo))))
                    {
                        document.Attachment = CloneAttachment(_attachments[id].Attachment);
                    }
                }
            }
        }

        private bool ValidateAttachment(IstanzaDomicilioRecord request, IstanzaDomicilioOperation operation,
            decimal id, string expectedType, bool proposed)
        {
            if (!_attachments.TryGetValue(id, out var metadata) || !metadata.Active)
            {
                AddFinding(operation, DomicilioControlSeverity.NonConforme, "IST_DOC_001",
                    $"L'allegato {id:0} è mancante o non più attivo.");
                return false;
            }
            var attachment = metadata.Attachment;
            if (!string.Equals(attachment.CodFiscale, request.CodFiscale, StringComparison.OrdinalIgnoreCase) ||
                attachment.NumDomanda != request.NumDomanda || metadata.AcademicYear != request.AnnoAccademico)
            {
                AddFinding(operation, DomicilioControlSeverity.NonConforme, "IST_DOC_002",
                    $"L'allegato {id:0} non appartiene allo studente, alla domanda e all'anno accademico dell'istanza.");
                return false;
            }
            if (attachment.CodTipoAllegato != expectedType)
            {
                AddFinding(operation, DomicilioControlSeverity.NonConforme, "IST_DOC_003",
                    $"La tipologia dell'allegato {id:0} ({attachment.CodTipoAllegato}) non corrisponde al documento richiesto ({expectedType}).");
                return false;
            }
            if (proposed ? attachment.IdContratto.HasValue || attachment.IdProroga.HasValue
                         : !IsCurrentAttachment(request, operation, attachment, expectedType))
            {
                AddFinding(operation, DomicilioControlSeverity.NonConforme, "IST_DOC_004",
                    proposed ? $"L'allegato proposto {id:0} è già associato a un contratto o a una proroga."
                             : $"L'allegato corrente {id:0} non è associato all'elemento di origine dell'operazione.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(attachment.CodStatus))
            {
                AddFinding(operation, DomicilioControlSeverity.VerificaManuale, "IST_DOC_005",
                    $"L'allegato {id:0} non ha uno stato attivo verificabile.");
                return false;
            }
            return true;
        }

        private static bool IsCurrentAttachment(IstanzaDomicilioRecord request, IstanzaDomicilioOperation operation,
            DomicilioAttachmentRecord attachment, string type)
        {
            var contracts = request.DomiciliCorrenti.SelectMany(x => x.Contratti);
            if (type == "34")
                return !attachment.IdContratto.HasValue && attachment.IdProroga.HasValue &&
                       (!operation.IdProroga.HasValue || operation.IdProroga == attachment.IdProroga) &&
                       contracts.SelectMany(x => x.Proroghe).Any(x => x.Id == attachment.IdProroga);
            return !attachment.IdProroga.HasValue && attachment.IdContratto.HasValue &&
                   (!operation.IdContratto.HasValue || operation.IdContratto == attachment.IdContratto) &&
                   contracts.Any(x => x.Id == attachment.IdContratto);
        }

        private static string DocumentType(string type) => type switch
        {
            "CONTRATTO" => "05", "SUBENTRO" => "45", "PROROGA" => "34", "LEARNING_AGREEMENT" => "46", _ => string.Empty
        };

        private static void AddFinding(IstanzaDomicilioOperation operation, DomicilioControlSeverity severity, string code, string description)
            => operation.State.Add(severity, code, description, "Verificare i metadati e l'associazione dell'allegato prima di lavorare l'istanza.");

        private static DomicilioAttachmentRecord CloneAttachment(DomicilioAttachmentRecord source) => new()
        {
            IdAllegato = source.IdAllegato, CodTipoAllegato = source.CodTipoAllegato,
            CodStatus = source.CodStatus, StatusDescription = source.StatusDescription,
            DataValidita = source.DataValidita, IdContratto = source.IdContratto, IdProroga = source.IdProroga,
            CodFiscale = source.CodFiscale, NumDomanda = source.NumDomanda
        };

        private static string ReadString(SqlDataReader reader, string column)
            => reader[column] == DBNull.Value ? string.Empty : Convert.ToString(reader[column], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        private static int ReadInt(SqlDataReader reader, string column)
            => Convert.ToInt32(reader[column], CultureInfo.InvariantCulture);
        private static int? ReadNullableInt(SqlDataReader reader, string column)
            => reader[column] == DBNull.Value ? null : Convert.ToInt32(reader[column], CultureInfo.InvariantCulture);
        private static decimal ReadDecimal(SqlDataReader reader, string column)
            => Convert.ToDecimal(reader[column], CultureInfo.InvariantCulture);
        private static decimal? ReadNullableDecimal(SqlDataReader reader, string column)
            => reader[column] == DBNull.Value ? null : Convert.ToDecimal(reader[column], CultureInfo.InvariantCulture);
        private static DateTime? ReadDate(SqlDataReader reader, string column)
            => reader[column] == DBNull.Value ? null : Convert.ToDateTime(reader[column], CultureInfo.InvariantCulture);
    }
}
