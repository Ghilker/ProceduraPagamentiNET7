using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProcedureNet7
{
    public sealed class DrosoDocument
    {
        public long IdAllegato { get; set; }
        public long? FileId { get; set; }
        public long Bytes { get; set; }
        public string CodiceFiscale { get; set; } = "";
        public string Coorte { get; set; } = "Locale";
        public string DeclaredLabel { get; set; } = "";
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public DateTime? SubmittedAt { get; set; }
        public string Predicted { get; set; } = "";
        public double? Confidence { get; set; }
        public double? Margin { get; set; }
        public int Pages { get; set; }
        public bool NeedsReview { get; set; } = true;
        public string Error { get; set; } = "";
        public string CorrectedLabel { get; set; } = "";
        public bool AnalysisCompleted { get; set; }
        public bool PdfUnreadable { get; set; }
        public long? SelectedStatusId { get; set; }
        public string AppliedStatus { get; set; } = "";
        public long? AppliedStatusId { get; set; }
        public DateTime? AppliedAt { get; set; }
        public string StatusUpdateNote { get; set; } = "";
        public DateTime? MessageSentAt { get; set; }
        public string MessageUpdateNote { get; set; } = "";
        public string ProposedStatus => !AnalysisCompleted ? ""
            : !PdfUnreadable && Error.Length == 0 && Predicted is "PASSAPORTO" or "RICEVUTA" or "PERMESSO_SOGGIORNO"
                && Confidence is double score && double.IsFinite(score) && score > .90 && score <= 1
                ? "02" : "04";
        public string StatusReason => ProposedStatus == "04" ? (PdfUnreadable ? "PDF non leggibile" : "non riconosciuto") : "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public string CategoriaCaricata => DrosoSession.Label(DeclaredLabel);
        public string CategoriaProposta => DrosoSession.Label(Predicted);
        public string Revisione => DrosoSession.Label(CorrectedLabel);
        public string Esito => AppliedStatus == "02" ? "02 — In lavorazione"
            : AppliedStatus == "04" ? "04 — Respinto: " + (PdfUnreadable ? "PDF non leggibile" : "non riconosciuto")
            : StatusUpdateNote.Length > 0 ? StatusUpdateNote
            : ProposedStatus == "02" ? "02 — In lavorazione (da salvare)"
            : ProposedStatus == "04" ? "04 — Respinto: " + StatusReason + " (da salvare)"
            : Error.Length > 0 ? "Errore: elaborazione non completata" : "Da analizzare";

        public void RejectUnreadablePdf(string reason)
        {
            PdfUnreadable = true;
            AnalysisCompleted = true;
            Predicted = "";
            Confidence = null;
            Margin = null;
            Pages = 0;
            NeedsReview = false;
            Error = reason;
        }
    }

    public sealed class DrosoSession
    {
        public int Version { get; set; } = 1;
        public string Directory { get; set; } = "";
        public string ModelDirectory { get; set; } = "";
        public string CheckpointSha256 { get; set; } = "";
        public string SourceDatabase { get; set; } = "";
        public string SourceServer { get; set; } = "";
        public string FilesDatabase { get; set; } = "";
        public List<DrosoDocument> Documents { get; set; } = new();
        public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static string Label(string value) => value switch
        {
            "01" or "PASSAPORTO" => "Passaporto",
            "02" or "RICEVUTA" => "Richiesta/ricevuta",
            "03" or "PERMESSO_SOGGIORNO" => "Permesso di soggiorno",
            "ERRATO" => "Documento non pertinente",
            "DA_VERIFICARE" => "Da verificare",
            _ => ""
        };

        public static string Category(string code) => code switch
        {
            "01" => "PASSAPORTO", "02" => "RICEVUTA", "03" => "PERMESSO_SOGGIORNO", _ => ""
        };

        public static string Hash(string path)
        {
            using var input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input));
        }

        public void Save()
        {
            System.IO.Directory.CreateDirectory(Directory);
            string target = System.IO.Path.Combine(Directory, "session.json");
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(this, JsonOptions));
            File.Move(target + ".tmp", target, true);
        }

        public static DrosoSession Load(string path)
        {
            var result = JsonSerializer.Deserialize<DrosoSession>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("Sessione vuota.");
            if (result.Version != 1 || result.Documents == null || result.Documents.Any(d => d == null))
                throw new InvalidDataException("Formato sessione non supportato.");
            result.Directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
            return result;
        }

        public void ApplyResult(string json, string expectedCheckpoint, IReadOnlyList<DrosoDocument> inputs)
        {
            var result = JsonSerializer.Deserialize<DrosoBatchResult>(json, JsonOptions)
                ?? throw new InvalidDataException("Risposta Droso vuota.");
            if (result.Version != 1 || !string.Equals(result.CheckpointSha256, expectedCheckpoint, StringComparison.OrdinalIgnoreCase)
                || result.Rows == null || result.Rows.Length != inputs.Count)
                throw new InvalidDataException("Risposta incompleta o checkpoint diverso da quello richiesto.");
            var expected = inputs.ToDictionary(d => System.IO.Path.GetFullPath(d.Path), StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Validate the entire response before changing any row.
            foreach (var row in result.Rows)
            {
                if (row == null || string.IsNullOrEmpty(row.Path) || !seen.Add(System.IO.Path.GetFullPath(row.Path))
                    || !expected.TryGetValue(System.IO.Path.GetFullPath(row.Path), out var document)
                    || !string.Equals(row.Sha256, document.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Identità dei documenti nella risposta non corrispondenti.");
                if (string.IsNullOrEmpty(row.Error) && (row.Predicted is not ("PASSAPORTO" or "RICEVUTA" or "PERMESSO_SOGGIORNO" or "ERRATO")
                    || !double.IsFinite(row.Confidence) || row.Confidence < 0 || row.Confidence > 1
                    || row.Margin is not double margin || !double.IsFinite(margin) || margin < 0 || margin > 1
                    || row.Pages < 1 || row.Pages > 20))
                    throw new InvalidDataException("Punteggi o pagine nella risposta Droso non validi.");
                if (row.PdfUnreadable && string.IsNullOrWhiteSpace(row.Error))
                    throw new InvalidDataException("Motivo del rifiuto PDF assente.");
            }
            foreach (var row in result.Rows)
            {
                var document = expected[System.IO.Path.GetFullPath(row.Path)];
                if (row.PdfUnreadable)
                {
                    document.RejectUnreadablePdf(row.Error!);
                    continue;
                }
                document.Predicted = row.Predicted ?? "";
                document.Confidence = row.Confidence;
                document.Margin = row.Margin;
                document.Pages = row.Pages;
                document.NeedsReview = row.NeedsReview || document.Predicted == "ERRATO" || row.Confidence < .75 || row.Margin < .15;
                document.Error = row.Error ?? "";
                document.AnalysisCompleted = true;
            }
            CheckpointSha256 = expectedCheckpoint;
        }

        public void ExportCsv(string path)
        {
            static string Cell(string value)
            {
                if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            using var output = new StreamWriter(path, false, new UTF8Encoding(true));
            output.WriteLine("IdAllegato;CodiceFiscale;Coorte;File;CategoriaCaricata;CategoriaProposta;Punteggio;Pagine;Esito;RevisioneLocale;Errore;Sha256;StatoProposto;Motivo;StatoSalvato;IdStatoSalvato;MessaggioInviatoIl;NotaMessaggio");
            foreach (var d in Documents)
                output.WriteLine(string.Join(";", new[] { d.IdAllegato.ToString(CultureInfo.InvariantCulture), d.CodiceFiscale,
                    d.Coorte, d.Path, d.CategoriaCaricata, d.CategoriaProposta, d.Confidence?.ToString("R", CultureInfo.InvariantCulture) ?? "",
                    d.Pages.ToString(CultureInfo.InvariantCulture), d.Esito, d.Revisione, d.Error, d.Sha256,
                    d.ProposedStatus, d.StatusReason, d.AppliedStatus, d.AppliedStatusId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    d.MessageSentAt?.ToString("O", CultureInfo.InvariantCulture) ?? "", d.MessageUpdateNote }.Select(Cell)));
        }
    }

    public sealed class DrosoBatchResult
    {
        public int Version { get; set; }
        public string CheckpointSha256 { get; set; } = "";
        public DrosoBatchRow[] Rows { get; set; } = Array.Empty<DrosoBatchRow>();
    }
    public sealed class DrosoBatchRow
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Predicted { get; set; } = "";
        public double Confidence { get; set; }
        public double? Margin { get; set; }
        public bool NeedsReview { get; set; }
        public int Pages { get; set; }
        public string Error { get; set; } = "";
        public bool PdfUnreadable { get; set; }
    }
}
