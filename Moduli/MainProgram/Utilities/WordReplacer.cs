#nullable enable

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;           // <-- for OpenXmlValidator
using W = DocumentFormat.OpenXml.Wordprocessing;   // <-- alias to avoid type clashes
using OpenXmlPowerTools;                           // OpenXmlRegex, GetXDocument/PutXDocument
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;                                 // DataTable
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Optional attribute to map a property to a specific Tag (or token) name in the .docx.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class TagAttribute : Attribute
{
    public string Name { get; }
    public TagAttribute(string name) => Name = name;
}

/// <summary>
/// Fills a Word (.docx) template:
/// 1) Replaces SDTs (content controls) by Tag or Title(Alias) with strings or DataTable.
/// 2) Replaces #tokens# (strings only) using OpenXmlPowerTools across runs.
/// 3) Validates the resulting package and throws with details if invalid.
/// </summary>
public static class WordTagReplacer
{
    public static void FillTemplate(string templatePath, string outputPath, object dataModel, CultureInfo? culture = null)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) throw new ArgumentException("Template path is required.", nameof(templatePath));
        if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path is required.", nameof(outputPath));
        if (!File.Exists(templatePath)) throw new FileNotFoundException("Template not found.", templatePath);
        if (dataModel is null) throw new ArgumentNullException(nameof(dataModel));

        culture ??= new CultureInfo("it-IT");

        // Build key→object dictionary from the model (keeps DataTable and scalars)
        var data = BuildObjectMapFromModel(dataModel);

        // Ensure destination folder & copy template → output
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.Copy(templatePath, outputPath, overwrite: true);

        using var doc = WordprocessingDocument.Open(outputPath, true);

        // 1) Replace SDTs (strings or DataTable) across parts
        ReplaceSdtTagsInPart(doc.MainDocumentPart, data, culture);
        foreach (var hp in doc.MainDocumentPart.HeaderParts)
            ReplaceSdtTagsInPart(hp, data, culture);
        foreach (var fp in doc.MainDocumentPart.FooterParts)
            ReplaceSdtTagsInPart(fp, data, culture);

        if (doc.MainDocumentPart.FootnotesPart is not null)
            ReplaceSdtTagsInPart(doc.MainDocumentPart.FootnotesPart, data, culture);
        if (doc.MainDocumentPart.EndnotesPart is not null)
            ReplaceSdtTagsInPart(doc.MainDocumentPart.EndnotesPart, data, culture);
        if (doc.MainDocumentPart.WordprocessingCommentsPart is not null)
            ReplaceSdtTagsInPart(doc.MainDocumentPart.WordprocessingCommentsPart, data, culture);

        // 2) Replace #tokens# (only string-like values) using PowerTools across runs
        ReplaceHashTokensWithPowerTools(doc.MainDocumentPart, data, culture);
        foreach (var hp in doc.MainDocumentPart.HeaderParts)
            ReplaceHashTokensWithPowerTools(hp, data, culture);
        foreach (var fp in doc.MainDocumentPart.FooterParts)
            ReplaceHashTokensWithPowerTools(fp, data, culture);

        if (doc.MainDocumentPart.FootnotesPart is not null)
            ReplaceHashTokensWithPowerTools(doc.MainDocumentPart.FootnotesPart, data, culture);
        if (doc.MainDocumentPart.EndnotesPart is not null)
            ReplaceHashTokensWithPowerTools(doc.MainDocumentPart.EndnotesPart, data, culture);
        if (doc.MainDocumentPart.WordprocessingCommentsPart is not null)
            ReplaceHashTokensWithPowerTools(doc.MainDocumentPart.WordprocessingCommentsPart, data, culture);

        // Final save of the main document
        doc.MainDocumentPart.Document.Save();

        // 3) Validate and throw if invalid (helps catch the exact location)
        ValidateOrThrow(doc);
    }

    // ---------------- model → Dictionary<string, object?> (keeps DataTable) ----------------

    private static IDictionary<string, object?> BuildObjectMapFromModel(object model)
    {
        if (model is IDictionary<string, object?> dictObjExact)
            return new Dictionary<string, object?>(dictObjExact, StringComparer.Ordinal);

        if (model is IDictionary dictObj)
        {
            var res = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry kv in dictObj)
            {
                var key = kv.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key)) continue;
                res[key!] = kv.Value;
            }
            return res;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead) continue;
            var tagAttr = prop.GetCustomAttribute<TagAttribute>();
            var key = (tagAttr?.Name ?? prop.Name).Trim();
            if (key.Length == 0) continue;

            var value = prop.GetValue(model);
            result[key] = value;
        }
        return result;
    }

    private static string ConvertScalarToString(object? value, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        return value switch
        {
            IFormattable f => f.ToString(null, culture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
        // NB: DataTable intentionally not handled here
    }

    // ---------------- SDT replacement (strings OR DataTable) ----------------
    private static void ReplaceSdtTagsInPart(OpenXmlPart? part, IDictionary<string, object?> data, CultureInfo culture)
    {
        var root = part?.RootElement;
        if (root is null) return;

        foreach (var sdt in root.Descendants<W.SdtElement>())
        {
            var props = sdt.SdtProperties;
            if (props is null) continue;

            // Prefer <w:tag w:val="...">, fall back to <w:alias w:val="..."> (Title)
            string? key =
                props.GetFirstChild<W.Tag>()?.Val?.Value ??
                props.GetFirstChild<W.SdtAlias>()?.Val?.Value;

            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!data.TryGetValue(key!, out var value)) continue;

            // If value is a DataTable => create a table (requires SdtBlock or SdtCell)
            if (value is DataTable dt)
            {
                var table = CreateTableFromDataTable(dt, culture);

                if (sdt is W.SdtBlock sdtBlock)
                {
                    var content = sdtBlock.SdtContentBlock;
                    content.RemoveAllChildren();
                    content.AppendChild(table);
                }
                else if (sdt is W.SdtCell sdtCell)
                {
                    var content = sdtCell.SdtContentCell;
                    content.RemoveAllChildren();
                    content.AppendChild(table);
                }
                else if (sdt is W.SdtRun sdtRun)
                {
                    InsertTableAfterParagraphAndMark(sdtRun, table);
                }
            }
            else if (TryGetStringList(value, out var lines))   
            {
                // One paragraph per entry
                if (sdt is W.SdtBlock sdtBlock)
                {
                    var content = sdtBlock.SdtContentBlock;
                    content.RemoveAllChildren();
                    foreach (var line in lines)
                        content.AppendChild(CreateParagraph(line ?? string.Empty));
                }
                else if (sdt is W.SdtCell sdtCell)
                {
                    var content = sdtCell.SdtContentCell;
                    content.RemoveAllChildren();
                    foreach (var line in lines)
                        content.AppendChild(CreateParagraph(line ?? string.Empty));
                }
                else if (sdt is W.SdtRun sdtRun)
                {
                    InsertParagraphsAfterParagraphAndMark(sdtRun, lines);
                }
            }
            else
            {
                var text = ConvertScalarToString(value, culture);
                SetSdtText(sdt, text);
            }
        }

        SavePart(part!);
    }
    private static void InsertTableAfterParagraphAndMark(W.SdtRun sdtRun, W.Table table)
    {
        // Find the ancestor paragraph
        var p = sdtRun.Ancestors<W.Paragraph>().FirstOrDefault();
        if (p == null)
        {
            // No paragraph ancestor: just replace with text to avoid corruption
            ReplaceSdtWithTextFallback(sdtRun, "");
            return;
        }

        // Insert the table after the paragraph, then replace the SDT with a small note
        p.InsertAfterSelf(table);
        ReplaceSdtWithTextFallback(sdtRun, "");
    }

    private static void ReplaceSdtWithTextFallback(W.SdtElement sdt, string text)
    {
        if (sdt is W.SdtRun sdtRun)
        {
            var content = sdtRun.SdtContentRun;
            content.RemoveAllChildren();
            content.AppendChild(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }
        else
        {
            if (sdt is W.SdtBlock sdtBlock)
            {
                var content = sdtBlock.SdtContentBlock;
                content.RemoveAllChildren();
                content.AppendChild(new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
            else if (sdt is W.SdtCell sdtCell)
            {
                var content = sdtCell.SdtContentCell;
                content.RemoveAllChildren();
                content.AppendChild(new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
        }
    }

    private static void SetSdtText(W.SdtElement sdt, string text)
    {
        if (sdt is W.SdtRun sdtRun)
        {
            var content = sdtRun.SdtContentRun;
            content.RemoveAllChildren();
            content.AppendChild(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }
        else if (sdt is W.SdtBlock sdtBlock)
        {
            var content = sdtBlock.SdtContentBlock;
            content.RemoveAllChildren();
            content.AppendChild(new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        }
        else if (sdt is W.SdtCell sdtCell)
        {
            var content = sdtCell.SdtContentCell;
            content.RemoveAllChildren();
            content.AppendChild(new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
        }
    }

    private static void SavePart(OpenXmlPart part)
    {
        switch (part)
        {
            case MainDocumentPart md when md.Document is not null:
                md.Document.Save();
                break;
            case HeaderPart hp when hp.Header is not null:
                hp.Header.Save();
                break;
            case FooterPart fp when fp.Footer is not null:
                fp.Footer.Save();
                break;
            case FootnotesPart fn when fn.Footnotes is not null:
                fn.Footnotes.Save();
                break;
            case EndnotesPart en when en.Endnotes is not null:
                en.Endnotes.Save();
                break;
            case WordprocessingCommentsPart cp when cp.Comments is not null:
                cp.Comments.Save();
                break;
        }
    }

    // ---------------- #token# replacement via OpenXmlPowerTools (strings only) ----------------
    private static void ReplaceHashTokensWithPowerTools(OpenXmlPart? part, IDictionary<string, object?> data, CultureInfo culture)
    {
        if (part is null) return;

        XDocument xDoc = part.GetXDocument();
        if (xDoc.Root is null) return;

        foreach (var kv in data)
        {
            if (kv.Value is DataTable) continue;
            if (kv.Value is IEnumerable<string>) continue;        // <--- NEW
            if (kv.Value is IEnumerable && kv.Value is not string) continue; // defensive

            var token = $"#{kv.Key}#";
            var replacement = ConvertScalarToString(kv.Value, culture);
            var regex = new Regex(Regex.Escape(token), RegexOptions.CultureInvariant);

            OpenXmlRegex.Replace(new[] { xDoc.Root }, regex, replacement, (elem, match) => true);
        }


        part.PutXDocument();
        SavePart(part);
    }

    // ---------------- Build a Word Table from DataTable (defensive) ----------------
    private static W.Table CreateTableFromDataTable(DataTable dt, CultureInfo culture)
    {
        // Defensive clone and ensure at least 1 column
        if (dt is null) dt = new DataTable();
        if (dt.Columns.Count == 0)
            dt.Columns.Add("Dato");

        // Sanitize all cell values to avoid illegal XML chars
        var safeDt = dt.Clone();
        foreach (DataColumn c in safeDt.Columns)
            c.DataType = typeof(string); // force string for safe serialization
        foreach (DataRow r in dt.Rows)
        {
            var nr = safeDt.NewRow();
            foreach (DataColumn c in dt.Columns)
            {
                var raw = r[c];
                var s = ConvertScalarToString(raw == DBNull.Value ? null : raw, culture);
                nr[c.ColumnName] = SanitizeXmlText(s);
            }
            safeDt.Rows.Add(nr);
        }

        var tbl = new W.Table();

        // 1) tblPr
        var tblProps = new W.TableProperties(
            new W.TableStyle { Val = "TableGrid" },
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 }
            )
        );
        tbl.AppendChild(tblProps);

        // 2) tblGrid (REQUIRED by the validator before any row)
        var grid = new W.TableGrid();
        // You can omit Width on each gridCol; Word will auto-size.
        // If you prefer fixed widths, set Width = "2400" (dxa) or similar.
        int colCount = safeDt.Columns.Count;
        for (int i = 0; i < colCount; i++)
            grid.AppendChild(new W.GridColumn());
        tbl.AppendChild(grid);

        // 3) Header row
        var header = new W.TableRow();
        foreach (DataColumn col in safeDt.Columns)
        {
            var p = new W.Paragraph(new W.Run(new W.Text(SanitizeXmlText(col.ColumnName)) { Space = SpaceProcessingModeValues.Preserve }));
            var run = p.GetFirstChild<W.Run>();
            if (run is not null)
                run.RunProperties = new W.RunProperties(new W.Bold());

            var tc = new W.TableCell(
                new W.TableCellProperties(),
                p
            );
            header.Append(tc);
        }
        tbl.Append(header);

        // Ensure at least one data row so older Word versions keep the table visible
        if (safeDt.Rows.Count == 0)
            safeDt.Rows.Add(safeDt.NewRow());

        // 4) Data rows
        foreach (DataRow row in safeDt.Rows)
        {
            var tr = new W.TableRow();
            foreach (DataColumn col in safeDt.Columns)
            {
                var text = row[col]?.ToString() ?? string.Empty;
                var tc = new W.TableCell(
                    new W.TableCellProperties(),
                    new W.Paragraph(new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }))
                );
                tr.Append(tc);
            }
            tbl.Append(tr);
        }

        return tbl;
    }

    private static string SanitizeXmlText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        // Keep only valid XML chars; allow TAB(0x09), LF(0x0A), CR(0x0D)
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            int code = ch;
            bool ok =
                code == 0x09 || code == 0x0A || code == 0x0D ||
                (code >= 0x20 && code <= 0xD7FF) ||
                (code >= 0xE000 && code <= 0xFFFD);

            if (ok) sb.Append(ch);
            else sb.Append(' '); // replace with space
        }
        return sb.ToString();
    }

    private static bool TryGetStringList(object? value, out List<string> list)
    {
        if (value is null) { list = new List<string>(); return false; }
        if (value is List<string> l1) { list = l1; return true; }
        if (value is IEnumerable<string> ie) { list = ie.ToList(); return true; }
        // Safeguard: accept IEnumerable<object> and ToString() each (but skip DataTable)
        if (value is IEnumerable ie2 && value is not string && value is not DataTable)
        {
            list = new List<string>();
            foreach (var v in ie2) list.Add(v?.ToString() ?? string.Empty);
            return true;
        }
        list = new List<string>();
        return false;
    }

    private static W.Paragraph CreateParagraph(string text)
    {
        return new W.Paragraph(
            new W.Run(
                new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }
            )
        );
    }

    private static void InsertParagraphsAfterParagraphAndMark(W.SdtRun sdtRun, IEnumerable<string> lines)
    {
        var p = sdtRun.Ancestors<W.Paragraph>().FirstOrDefault();
        if (p is null)
        {
            ReplaceSdtWithTextFallback(sdtRun, "");
            return;
        }

        // Insert in original order, each as its own paragraph
        OpenXmlElement last = p;
        foreach (var line in lines)
        {
            var para = CreateParagraph(line ?? string.Empty);
            last.InsertAfterSelf(para);
            last = para;
        }

        // Replace the inline SDT with a small note
        ReplaceSdtWithTextFallback(sdtRun, "");
    }


    // ---------------- Validation ----------------
    private static void ValidateOrThrow(WordprocessingDocument doc)
    {
        var validator = new OpenXmlValidator(FileFormatVersions.Office2013);
        var errors = validator.Validate(doc).ToList();
        if (errors.Count == 0) return;

        // Mostra fino a 10 errori con percorso e part coinvolto
        var lines = errors.Take(10).Select(e =>
        {
            var path = e.Path?.XPath ?? e.Path?.ToString() ?? "(no path)";
            var partUri = e.Part?.Uri?.ToString() ?? "(unknown part)";
            return $"- {e.Description} at {path} (Part: {partUri})";
        });

        var msg = "OpenXml validation failed:\n" + string.Join("\n", lines);
        throw new InvalidOperationException(msg);
    }
}

// ---------------- Sample model with DataTable ----------------
public sealed class DeterminaDati
{
    [Tag("tipoPagamento")] public string? TipoPagamento { get; init; }
    [Tag("tipoBeneficio")] public string? TipoBeneficio { get; init; }
    [Tag("numeroStudenti")] public string? NumeroStudenti { get; init; }
    [Tag("annoAccademico")] public string? AnnoAccademico { get; init; }
    [Tag("importoDaPagare")] public string? ImportoDaPagare { get; init; }
    [Tag("tipoIscrizione")] public string? TipoIscrizione { get; init; }
    [Tag("tipoFondo")] public string? TipoFondo { get; init; }
    [Tag("tipoFondoCompleto")] public string? TipoFondoCompleto { get; init; }
    [Tag("vistoEstratto")] public string? VistoEstratto { get; init; }
    [Tag("determinazioniEstratte")] public List<string>? DeterminazioniEstratte { get; init; }
    [Tag("listaImpegniConEsercizio")] public string? ListaImpegniConEsercizio { get; init; }
    [Tag("esercizioFinanziario")] public string? EsercizioFinanziario { get; init; }

    // DataTable: the replacer will inject a real Word table
    [Tag("tabellaRiepilogo")] public DataTable? TabellaRiepilogo { get; init; }
}
