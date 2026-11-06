using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

if (args.Length != 2) throw new ArgumentException("Usare configPath reviewFolder. Soltanto SELECT; nessuna lavorazione delle istanze.");
var folder = Path.GetFullPath(args[1]);
using var analysis = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "analysis.json")));
var priority = new[] { 17594, 17677, 17840, 19267 };
var additional = new HashSet<int> { 17465,17522,17552,17437,17763,17770,17616,17624,17713,17580,18027,18115 };
var selected = analysis.RootElement.GetProperty("requests").EnumerateArray()
    .Where(r => r.GetProperty("category").GetString() == "Solo date o estremi discordanti" || additional.Contains(r.GetProperty("id").GetInt32()))
    .Select(r => new Request(r.GetProperty("id").GetInt32(), r.GetProperty("category").GetString()!,
        r.GetProperty("codes").EnumerateArray().Select(c => c.GetString() ?? "").ToArray(),
        r.GetProperty("operations").EnumerateArray().SelectMany(o => Regex.Matches(o.GetProperty("ID_ALLEGATI").GetString() ?? "", @"\d+")
            .Cast<Match>().Select(m => decimal.Parse(m.Value, CultureInfo.InvariantCulture))).Distinct().ToArray()))
    .OrderBy(r => Array.IndexOf(priority, r.Id) is var i && i >= 0 ? i : 100).ThenBy(r => r.Id).ToArray();
using var config = JsonDocument.Parse(File.ReadAllText(args[0]), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
var builder = new SqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionMain").GetString());
if (!string.Equals(builder.DataSource, "10.9.20.70", StringComparison.OrdinalIgnoreCase) ||
    !string.Equals(builder.InitialCatalog, "ADISU", StringComparison.OrdinalIgnoreCase) || builder.UserID != "Verifica" || builder.IntegratedSecurity || builder.AttachDBFilename.Length > 0)
    throw new InvalidOperationException("Allowlist esplicita fallita: connessione esterna cambiata o non autorizzata.");
builder.ApplicationName = "DomicilioReviewReadOnly20261103";
await using var connection = new SqlConnection(builder.ConnectionString);
await connection.OpenAsync();
var idsJson = JsonSerializer.Serialize(selected.Select(r => r.Id));
var headerRows = await Rows(@"
SELECT g.Num_istanza,g.Anno_accademico,g.Num_domanda,g.Cod_tipo_istanza,g.Esito_istanza,g.Data_fine_validita,g.Utente
FROM dbo.Istanza_dati_generali g WHERE g.Num_istanza IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids)) ORDER BY g.Num_istanza;", idsJson);
var statusRows = await Rows(@"
SELECT s.* FROM dbo.Istanza_status s WHERE s.Num_istanza IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids))
AND s.data_fine_validita IS NULL ORDER BY s.Num_istanza,s.data_validita;", idsJson);
var operationRows = await Rows(@"
SELECT o.* FROM dbo.Istanza_Domicilio_Operazioni o WHERE o.NUM_ISTANZA IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids))
ORDER BY o.NUM_ISTANZA,o.ORDINE,o.ID;", idsJson);
SaveJson("db-status.json", new { Requests = selected, Headers = headerRows, ActiveStatuses = statusRows, Operations = operationRows });
var domicileRows = await Rows(@"
SELECT g.Num_istanza,d.ID AS DomicilioId,d.DATA_INIZIO,d.DATA_FINE
FROM dbo.Istanza_dati_generali g INNER JOIN dbo.Domicili d ON d.COD_FISCALE=g.Cod_fiscale AND d.ANNO_ACCADEMICO=g.Anno_accademico AND d.NUM_DOMANDA=g.Num_domanda
WHERE g.Num_istanza IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids)) AND d.TIPO_LUOGO='DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED=0
ORDER BY g.Num_istanza,d.ID;",idsJson);
var contractRows = await Rows(@"
SELECT g.Num_istanza,c.ID AS ContrattoId,c.ID_DOMICILIO,c.N_SERIE_CONTRATTO,c.DATA_REG_CONTRATTO,c.DATA_INIZIO,c.DATA_FINE,c.DATA_CESSAZIONE,c.N_SERIE_SUBENTRO,c.DATA_INIZIO_SUBENTRO
FROM dbo.Istanza_dati_generali g INNER JOIN dbo.Domicili d ON d.COD_FISCALE=g.Cod_fiscale AND d.ANNO_ACCADEMICO=g.Anno_accademico AND d.NUM_DOMANDA=g.Num_domanda
INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID
WHERE g.Num_istanza IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids)) AND d.TIPO_LUOGO='DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED=0
AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 ORDER BY g.Num_istanza,c.ID;",idsJson);
var extensionRows = await Rows(@"
SELECT g.Num_istanza,p.ID AS ProrogaId,p.ID_CONTRATTO,p.N_SERIE_PROROGA,p.DATA_DECORRENZA,p.DATA_SCADENZA
FROM dbo.Istanza_dati_generali g INNER JOIN dbo.Domicili d ON d.COD_FISCALE=g.Cod_fiscale AND d.ANNO_ACCADEMICO=g.Anno_accademico AND d.NUM_DOMANDA=g.Num_domanda
INNER JOIN dbo.Contratti c ON c.ID_DOMICILIO=d.ID INNER JOIN dbo.Proroghe p ON p.ID_CONTRATTO=c.ID
WHERE g.Num_istanza IN (SELECT CONVERT(int,[value]) FROM OPENJSON(@ids)) AND d.TIPO_LUOGO='DOM' AND d.DATA_FINE_VALIDITA IS NULL AND d.IS_DELETED=0
AND c.DATA_FINE_VALIDITA IS NULL AND c.IS_DELETED=0 AND p.DATA_FINE_VALIDITA IS NULL AND p.IS_DELETED=0
ORDER BY g.Num_istanza,p.ID;",idsJson);
SaveJson("current-graph.json",new { Domicili=domicileRows,Contratti=contractRows,Proroghe=extensionRows });
Console.WriteLine(JsonSerializer.Serialize(new
{
    SelectedRequests = selected.Length, DateReferenceRequests = selected.Count(r => r.Category == "Solo date o estremi discordanti"),
    Headers = headerRows.Count, ActiveStatuses = statusRows.Count, Operations = operationRows.Count,
    HeaderGroups = headerRows.GroupBy(r => new { Closed = r["Data_fine_validita"] != null, Outcome = r["Esito_istanza"] }).Select(g => new { g.Key, Count = g.Count() }),
    StatusGroups = statusRows.GroupBy(r => new { Code = r["Cod_status_istanza"], Claim = r["presa_carico"] }).Select(g => new { g.Key, Count = g.Count() })
}));
Console.Out.Flush();
var attachmentIds = selected.SelectMany(r => r.Attachments).Distinct().ToArray();
var metadata = await Rows(@"
SELECT a.Id_Allegato_Adisu AS AttachmentId,a.id_allegato AS PhysicalId,a.data_validita AS ValidFrom,
ISNULL(a.doc_name,'') AS OriginalName,ISNULL(a.doc_type,'') AS ContentType,ISNULL(DATALENGTH(a.[file]),0) AS Bytes
FROM [ADISU_ALLEGATI].DIRSTUDIO.ALLEGATI a
WHERE a.data_fine_validita IS NULL AND a.Id_Allegato_Adisu IN (SELECT CONVERT(decimal(18,0),[value]) FROM OPENJSON(@ids))
ORDER BY a.Id_Allegato_Adisu,a.data_validita DESC,a.id_allegato DESC;", JsonSerializer.Serialize(attachmentIds));
var files = metadata.Select(row => new FileEntry(
    Convert.ToDecimal(row["AttachmentId"], CultureInfo.InvariantCulture), Convert.ToDecimal(row["PhysicalId"], CultureInfo.InvariantCulture),
    Convert.ToInt64(row["Bytes"], CultureInfo.InvariantCulture), row["OriginalName"]?.ToString() ?? "", row["ContentType"]?.ToString() ?? "",
    row["ValidFrom"] as DateTime?, selected.Where(r => r.Attachments.Contains(Convert.ToDecimal(row["AttachmentId"], CultureInfo.InvariantCulture))).Select(r => r.Id).ToArray()))
    .OrderBy(f => f.RequestIds.Select(id => Array.IndexOf(priority,id)).Where(i => i >= 0).DefaultIfEmpty(100).Min())
    .ThenBy(f => f.AttachmentId).ThenBy(f => f.PhysicalId).ToList();
Directory.CreateDirectory(Path.Combine(folder, "pdfs"));
var priorityAnnounced = new HashSet<int>();
var missing = attachmentIds.Except(files.Select(f => f.AttachmentId)).ToArray();
foreach (var file in files)
{
    if (file.Bytes <= 0 || file.Bytes > 20971520) file.State = file.Bytes <= 0 ? "EMPTY" : "OVER_20MB";
    else
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 180;
        command.CommandText = @"
SELECT a.[file] FROM [ADISU_ALLEGATI].DIRSTUDIO.ALLEGATI a
WHERE a.id_allegato=@physical AND a.Id_Allegato_Adisu=@logical AND a.data_fine_validita IS NULL
AND DATALENGTH(a.[file]) BETWEEN 1 AND 20971520;";
        command.Parameters.Add("@physical", SqlDbType.Decimal).Value = file.PhysicalId;
        command.Parameters.Add("@logical", SqlDbType.Decimal).Value = file.AttachmentId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        if (await reader.ReadAsync())
        {
            file.Path = Path.Combine(folder, "pdfs", $"{file.AttachmentId}_{file.PhysicalId}.pdf");
            if (!File.Exists(file.Path) || new FileInfo(file.Path).Length != file.Bytes)
            {
                await using var target = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.Read | FileShare.Delete);
                await using var stream = reader.GetStream(0); await stream.CopyToAsync(target);
            }
            using var hashStream = File.OpenRead(file.Path);
            file.Sha256 = Convert.ToHexString(SHA256.HashData(hashStream));
            file.State = new FileInfo(file.Path).Length == file.Bytes ? "DOWNLOADED" : "LENGTH_CHANGED";
        }
        else file.State = "NO_LONGER_ACTIVE";
    }
    SaveManifest();
    foreach (var id in priority.Where(id => !priorityAnnounced.Contains(id) && files.Where(f => f.RequestIds.Contains(id)).All(f => f.State != "PENDING")))
    {
        priorityAnnounced.Add(id);
        Console.WriteLine($"PRIORITY_READY request={id} files={files.Count(f => f.RequestIds.Contains(id) && f.State == "DOWNLOADED")} manifest={Path.Combine(folder,"pdf-manifest.json")}");
        Console.Out.Flush();
    }
}
SaveManifest();
Console.WriteLine(JsonSerializer.Serialize(new { Completed = true, Requests = selected.Length, LogicalAttachments = attachmentIds.Length,
    PhysicalFiles = files.Count, MissingAttachments = missing.Length, FileGroups = files.GroupBy(f => f.State).Select(g => new { State = g.Key, Count = g.Count() }),
    BytesDownloaded = files.Where(f => f.State == "DOWNLOADED").Sum(f => f.Bytes), Manifest = Path.Combine(folder,"pdf-manifest.json") }));

void SaveManifest() => SaveJson("pdf-manifest.json", new { Requests = selected, Files = files, MissingAttachmentIds = missing });
void SaveJson(string name, object value)
{
    var path = Path.Combine(folder,name); var temp = path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    for (var attempt=0;;attempt++)
    {
        try { File.Move(temp,path,true); break; }
        catch(Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt<29) { Thread.Sleep(100); }
    }
}
async Task<List<Dictionary<string,object?>>> Rows(string sql, string ids)
{
    await using var command = connection.CreateCommand(); command.CommandText=sql; command.CommandTimeout=180;
    command.Parameters.Add("@ids",SqlDbType.NVarChar,-1).Value=ids;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Dictionary<string,object?>>();
    while(await reader.ReadAsync())
    {
        var row=new Dictionary<string,object?>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<reader.FieldCount;i++) row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);
        rows.Add(row);
    }
    return rows;
}
sealed record Request(int Id,string Category,string[] Codes,decimal[] Attachments);
sealed class FileEntry
{
    public FileEntry(decimal attachmentId,decimal physicalId,long bytes,string originalName,string contentType,DateTime? validFrom,int[] requestIds)
    { AttachmentId=attachmentId;PhysicalId=physicalId;Bytes=bytes;OriginalName=originalName;ContentType=contentType;ValidFrom=validFrom;RequestIds=requestIds; }
    public decimal AttachmentId {get;} public decimal PhysicalId {get;} public long Bytes {get;} public string OriginalName {get;}
    public string ContentType {get;} public DateTime? ValidFrom {get;} public int[] RequestIds {get;}
    public string State {get;set;}="PENDING";public string? Path {get;set;} public string? Sha256 {get;set;}
}
