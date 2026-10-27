using System.Data.SqlClient;
using System.Text.Json;
using ProcedureNet7;

TestAddressPolicy();
Console.WriteLine("PASS: sintassi PEC e dominio approvato coerenti con il portale.");
SqlFixtures.ValidateQueryComposition();
Console.WriteLine("PASS: fixture delle query reali composte soltanto da CTE e SELECT.");

if (args.Length == 0) return;
if (args.Length != 2 || args[0] != "--sql")
    throw new ArgumentException("Usare senza argomenti per i test locali oppure --sql configPath per fixture SQL di sola lettura.");
using var connection = OpenAuthorizedTestConnection(args[1]);
SqlFixtures.Run(connection);

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void TestAddressPolicy()
{
    var approved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pec.it", "legalmail.it" };
    var cases = new (string? Address, bool Expected)[]
    {
        ("studente@pec.it", true), ("STUDENTE@PEC.IT", true), ("studente@legalmail.it", true),
        ("studente+domicilio@pec.it", true), ("nome.cognome@pec.it", true),
        ("Mario Rossi <studente@pec.it>", true), ("  studente@pec.it  ", true),
        // MailAddress also permits an implicit display name without angle brackets.
        ("nome cognome@pec.it", true),
        (null, false), ("", false), ("   ", false), ("studente", false),
        ("studente@", false), ("studente@localhost", false), ("@pec.it", false),
        ("studente@@pec.it", false), ("studente@altro@pec.it", false),
        ("\"nome cognome\"@pec.it", false), ("studente@pec .it", false),
        ("studente@pec.it.evil.example", false), ("studente@example.org", false)
    };
    foreach (var test in cases)
        Assert(ControlloPecAddress.IsValid(test.Address, approved) == test.Expected,
            $"Classificazione PEC errata per il campione sintetico '{test.Address}'.");
    Assert(!ControlloPecAddress.IsValid("studente@pec.it", new HashSet<string>(StringComparer.OrdinalIgnoreCase)),
        "La sintassi valida non basta se il dominio non è nell'elenco approvato.");
}

static SqlConnection OpenAuthorizedTestConnection(string configPath)
{
    using var document = JsonDocument.Parse(File.ReadAllText(configPath), new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    });
    var connectionStrings = FindConnectionMain(document.RootElement).Distinct(StringComparer.Ordinal).ToArray();
    if (connectionStrings.Length != 1)
        throw new InvalidOperationException("La configurazione esterna deve contenere una sola connessione ConnectionMain.");
    var builder = new SqlConnectionStringBuilder(connectionStrings[0]);
    if (!string.Equals(builder.DataSource, "database-disco.ctayekcu0lnn.eu-south-1.rds.amazonaws.com", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(builder.InitialCatalog, "ADISU", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(builder.UserID, "Verifica", StringComparison.Ordinal) || builder.IntegratedSecurity || builder.AttachDBFilename.Length > 0)
        throw new InvalidOperationException("Connessione rifiutata: server, database o utente non corrispondono all'ambiente di test autorizzato.");
    var connection = new SqlConnection(builder.ConnectionString);
    connection.Open();
    return connection;
}

static IEnumerable<string> FindConnectionMain(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals("ConnectionMain") && property.Value.ValueKind == JsonValueKind.String)
                yield return property.Value.GetString()!;
            else foreach (var connection in FindConnectionMain(property.Value)) yield return connection;
        }
    else if (element.ValueKind == JsonValueKind.Array)
        foreach (var item in element.EnumerateArray())
            foreach (var connection in FindConnectionMain(item)) yield return connection;
}
