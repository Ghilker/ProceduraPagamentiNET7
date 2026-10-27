using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DomicilioAutomation
{
    /// <summary>Binds a positive analysis to the exact database data and PDF bytes that preceded it.</summary>
    public static class DomicilioAutomationSnapshot
    {
        public static string Capture(DbConnection connection, DbTransaction? transaction, int requestId)
        {
            using var command = CreateCommand(connection, transaction, "g.Num_istanza = @id");
            AddParameter(command, "@id", DbType.Int32, requestId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Istanza non più disponibile.");
            return Fingerprint(reader);
        }

        public static Dictionary<int, string> CaptureFree(DbConnection connection, string academicYear)
        {
            using var command = CreateCommand(connection, null, @"
g.Anno_accademico = @aa AND g.Data_fine_validita IS NULL AND g.Esito_istanza IS NULL
AND g.cod_tipo_istanza IN ('01','04B')
AND EXISTS (SELECT 1 FROM dbo.Istanza_Domicilio_Operazioni op WHERE op.NUM_ISTANZA=g.Num_istanza AND op.DATA_FINE_VALIDITA IS NULL)
AND (SELECT COUNT(*) FROM dbo.Istanza_status s WHERE s.Num_istanza=g.Num_istanza AND s.data_fine_validita IS NULL)=1
AND EXISTS (SELECT 1 FROM dbo.Istanza_status s WHERE s.Num_istanza=g.Num_istanza AND s.data_fine_validita IS NULL AND s.Cod_status_istanza='01' AND s.presa_carico=0)");
            AddParameter(command, "@aa", DbType.AnsiString, academicYear, 8);
            var result = new Dictionary<int, string>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) result.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture), Fingerprint(reader));
            return result;
        }

        private static string Fingerprint(DbDataReader reader)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var column = 1; column < reader.FieldCount; column++)
            {
                var text = reader.IsDBNull(column) ? "null" : reader.GetString(column);
                var bytes = Encoding.UTF8.GetBytes(text);
                // Length-delimited JSON sections prevent ambiguous concatenation.
                hash.AppendData(Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
                hash.AppendData(bytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static void AddParameter(DbCommand command, string name, DbType type, object value, int size = 0)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value;
            if (size > 0) parameter.Size = size;
            command.Parameters.Add(parameter);
        }

        private static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string filter)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 180;
            const string domicileFilter = @"d.COD_FISCALE=g.Cod_fiscale AND d.NUM_DOMANDA=g.Num_domanda AND d.ANNO_ACCADEMICO=g.Anno_accademico
AND d.TIPO_LUOGO='DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED=0";
            var attachmentIds = $@"
SELECT v.ID FROM dbo.Istanza_Domicilio_Operazioni o
CROSS APPLY (VALUES(o.ID_ALLEGATO_CONTRATTO),(o.ID_ALLEGATO_SUBENTRO),(o.ID_ALLEGATO_PROROGA),(o.ID_ALLEGATO_LEARNING_AGREEMENT)) v(ID)
WHERE o.NUM_ISTANZA=g.Num_istanza AND o.DATA_FINE_VALIDITA IS NULL AND v.ID IS NOT NULL
UNION
SELECT a.id_allegato FROM dbo.Domicili d INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID
INNER JOIN dbo.ALLEGATI a ON a.ID_CONTRATTO=c.ID
WHERE {domicileFilter} AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 AND a.data_fine_validita IS NULL AND a.cod_tipo_allegato IN ('05','45','34','46')
UNION
SELECT a.id_allegato FROM dbo.Domicili d INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID
INNER JOIN dbo.Proroghe p ON p.ID_CONTRATTO=c.ID INNER JOIN dbo.ALLEGATI a ON a.ID_PROROGA=p.ID
WHERE {domicileFilter} AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 AND p.DATA_FINE_VALIDITA IS NULL AND p.IS_DELETED=0
AND a.data_fine_validita IS NULL AND a.cod_tipo_allegato IN ('05','45','34','46')";
            command.CommandText = $@"
SELECT g.Num_istanza,
    CONVERT(nvarchar(256),SERVERPROPERTY('ServerName'))+N'/'+DB_NAME() AS DatabaseIdentity,
    (SELECT h.* FROM dbo.Istanza_dati_generali h WHERE h.Num_istanza=g.Num_istanza FOR JSON PATH, INCLUDE_NULL_VALUES) AS HeaderJson,
    (SELECT s.* FROM dbo.Istanza_status s WHERE s.Num_istanza=g.Num_istanza AND s.data_fine_validita IS NULL ORDER BY s.Id_status_istanza FOR JSON PATH, INCLUDE_NULL_VALUES) AS StatusJson,
    (SELECT o.* FROM dbo.Istanza_Domicilio_Operazioni o WHERE o.NUM_ISTANZA=g.Num_istanza AND o.DATA_FINE_VALIDITA IS NULL ORDER BY o.ORDINE,o.ID FOR JSON PATH, INCLUDE_NULL_VALUES) AS OperationsJson,
    (SELECT d.* FROM dbo.Domicili d WHERE {domicileFilter} ORDER BY d.ID FOR JSON PATH, INCLUDE_NULL_VALUES) AS DomicilesJson,
    (SELECT c.* FROM dbo.Domicili d INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID
     WHERE {domicileFilter} AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 ORDER BY c.ID FOR JSON PATH, INCLUDE_NULL_VALUES) AS ContractsJson,
    (SELECT p.* FROM dbo.Domicili d INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID INNER JOIN dbo.Proroghe p ON p.ID_CONTRATTO=c.ID
     WHERE {domicileFilter} AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 AND p.DATA_FINE_VALIDITA IS NULL AND p.IS_DELETED=0 ORDER BY p.ID FOR JSON PATH, INCLUDE_NULL_VALUES) AS ExtensionsJson,
    (SELECT cm.COD_COMUNE,cm.DESCRIZIONE,cm.COD_PROVINCIA FROM dbo.Comuni cm
     WHERE cm.COD_COMUNE IN (
         SELECT d.COD_COMUNE FROM dbo.Domicili d WHERE {domicileFilter}
         UNION
         SELECT CONVERT(nvarchar(100),j.[value]) FROM dbo.Istanza_Domicilio_Operazioni o
         CROSS APPLY OPENJSON(CASE WHEN ISJSON(o.PAYLOAD_JSON)=1 THEN o.PAYLOAD_JSON ELSE N'{{}}' END) j
         WHERE o.NUM_ISTANZA=g.Num_istanza AND o.DATA_FINE_VALIDITA IS NULL
           AND LOWER(j.[key])='codcomunedomicilio' AND j.[type]=1)
     ORDER BY cm.COD_COMUNE,cm.DESCRIZIONE,cm.COD_PROVINCIA FOR JSON PATH, INCLUDE_NULL_VALUES) AS MunicipalitiesJson,
    (SELECT a.* FROM dbo.ALLEGATI a INNER JOIN ({attachmentIds}) ids ON ids.ID=a.id_allegato ORDER BY a.id_allegato FOR JSON PATH, INCLUDE_NULL_VALUES) AS AttachmentsJson,
    (SELECT s.* FROM dbo.STATUS_ALLEGATI s INNER JOIN ({attachmentIds}) ids ON ids.ID=s.id_allegato
     WHERE s.data_fine_validita IS NULL ORDER BY s.id_allegato,s.Id_STATUS_ALLEGATI FOR JSON PATH, INCLUDE_NULL_VALUES) AS AttachmentStatusJson,
    (SELECT f.id_allegato,f.Id_Allegato_Adisu,f.data_validita,f.doc_name,f.doc_type,DATALENGTH(f.[file]) AS FileLength,
            CASE WHEN DATALENGTH(f.[file])<=20971520 THEN CONVERT(varchar(64),HASHBYTES('SHA2_256',f.[file]),2) ELSE NULL END AS FileSha256
     FROM [ADISU_ALLEGATI].DIRSTUDIO.ALLEGATI f INNER JOIN ({attachmentIds}) ids ON ids.ID=f.Id_Allegato_Adisu
     WHERE f.data_fine_validita IS NULL ORDER BY f.Id_Allegato_Adisu,f.id_allegato FOR JSON PATH, INCLUDE_NULL_VALUES) AS FilesJson
FROM dbo.Istanza_dati_generali g WHERE {filter} ORDER BY g.Num_istanza;";
            return command;
        }
    }
}
