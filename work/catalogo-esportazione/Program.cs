using System.Data;
using System.Reflection;
using System.Text.Json;
using ProcedureNet7.Verifica;

var catalog = (DataTable)typeof(Verifica).GetMethod("BuildCatalogoEsclusioniTable", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, null)!;
var data = catalog.Rows.Cast<DataRow>().Select(row => catalog.Columns.Cast<DataColumn>()
    .ToDictionary(column => column.ColumnName, column => row[column] is DBNull ? null : row[column]));
File.WriteAllText(args[0], JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Catalogo esportato: {catalog.Rows.Count} voci.");
