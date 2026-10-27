using System.Data.Common;
using System.Text.Json;
using ProcedureNet7;
using Legacy = System.Data.SqlClient;
using Modern = Microsoft.Data.SqlClient;

using var config = JsonDocument.Parse(File.ReadAllText(args[0]), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
var connections = Find(config.RootElement).Distinct(StringComparer.Ordinal).ToArray();
if (connections.Length != 1) throw new InvalidOperationException("Expected one external connection.");
var builder = new Legacy.SqlConnectionStringBuilder(connections[0])
{
    PersistSecurityInfo = false,
    TrustServerCertificate = false,
    ApplicationName = "LazioDiscoDirStudio",
    ConnectTimeout = 10
};
builder.Remove("Encrypt");
var originalString = builder.ConnectionString;
using var original = new Legacy.SqlConnection(originalString);
Console.WriteLine($"original.beforeOpen.passwordPresent={HasPassword(original.ConnectionString)}");
if (!await SelectOne(original, "original")) return 1;
Console.WriteLine($"original.afterOpen.passwordPresent={HasPassword(original.ConnectionString)}");
var exported = DomicilioAutomaticConnectionSettings.FromOriginal(originalString);
var legacyExported = new Legacy.SqlConnectionStringBuilder(exported);
Console.WriteLine($"helper.passwordPresent={HasPassword(exported)}");
Console.WriteLine($"helper.encrypt={legacyExported.Encrypt}");
Console.WriteLine($"helper.trustServerCertificate={legacyExported.TrustServerCertificate}");
var directBuilder = new Modern.SqlConnectionStringBuilder(exported) { InitialCatalog = "ADISU_ALLEGATI" };
using var direct = new Modern.SqlConnection(directBuilder.ConnectionString);
return await SelectOne(direct, "files.productionHelper") ? 0 : 1;

static bool HasPassword(string connectionString) => !string.IsNullOrEmpty(new Legacy.SqlConnectionStringBuilder(connectionString).Password);
static async Task<bool> SelectOne(DbConnection connection, string label)
{
    try
    {
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = 10;
        var success = Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        Console.WriteLine($"{label}.selectOne={success}");
        return success;
    }
    catch (Legacy.SqlException ex) { Console.WriteLine($"{label}.sqlErrorNumber={ex.Number}"); }
    catch (Modern.SqlException ex) { Console.WriteLine($"{label}.sqlErrorNumber={ex.Number}"); }
    catch { Console.WriteLine($"{label}.otherFailure=True"); }
    return false;
}
static IEnumerable<string> Find(JsonElement element)
{
    if (element.ValueKind != JsonValueKind.Object) yield break;
    foreach (var property in element.EnumerateObject())
    {
        if (property.Name == "ConnectionMain" && property.Value.ValueKind == JsonValueKind.String)
            yield return property.Value.GetString()!;
        else
            foreach (var value in Find(property.Value)) yield return value;
    }
}
