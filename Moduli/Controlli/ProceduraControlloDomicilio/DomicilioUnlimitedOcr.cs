using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal static class DomicilioUnlimitedOcr
    {
        private const string ModelName = "Unlimited-OCR-2";
        private const int MaxResponseBytes = 8 * 1024 * 1024;
        private const string InvalidResponse = "La risposta Unlimited-OCR è incompleta o non rispetta il formato e il numero delle pagine attesi. Verificare manualmente il documento.";
        private static readonly HttpClient Client = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        }) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxResponseBytes };
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        internal static Uri? GetEndpoint(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) &&
                uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
                uri.AbsolutePath.EndsWith("/v1/chat/completions", StringComparison.Ordinal)
                ? uri : null;
        }

        internal static async Task<bool> IsModelAvailableAsync(Uri endpoint, HttpClient? client = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var http = client ?? Client;
                if (endpoint.IsLoopback)
                {
                    // The model list is available before local SGLang finishes its warmup.
                    using var ready = await http.GetAsync(new Uri(endpoint, "../../ready"), timeout.Token).ConfigureAwait(false);
                    if (!ready.IsSuccessStatusCode) return false;
                }

                using var response = await http.GetAsync(new Uri(endpoint, "../models"), timeout.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return false;
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
                return json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("data", out var models) && models.ValueKind == JsonValueKind.Array &&
                    models.EnumerateArray().Any(model => model.ValueKind == JsonValueKind.Object &&
                        model.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == ModelName);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException)
            {
                return false;
            }
        }

        internal static (string[]? Pages, string? Warning, bool Truncated) ReadDocument(string endpoint,
            IReadOnlyList<byte[]> jpegPages, HttpClient? client = null)
        {
            var uri = GetEndpoint(endpoint);
            if (uri == null)
                return (null, "L'URL Unlimited-OCR deve terminare con /v1/chat/completions e usare HTTPS o HTTP locale. Verificare manualmente il documento.", false);
            if (jpegPages.Count == 0 || jpegPages.Any(page => page == null || page.Length == 0))
                return (null, "Le immagini delle pagine per Unlimited-OCR sono mancanti. Verificare manualmente il documento.", false);

            try
            {
                var content = new List<object>();
                foreach (var page in jpegPages)
                    content.Add(new { type = "image_url", image_url = new { url = "data:image/jpeg;base64," + Convert.ToBase64String(page) } });
                content.Add(new { type = "text", text = string.Concat(Enumerable.Repeat("<PAGE><image>", jpegPages.Count)) + "Multi page parsing." });
                var payload = new
                {
                    model = ModelName,
                    messages = new[] { new { role = "user", content } },
                    max_tokens = (int)Math.Min(32768L, Math.Max(4096L, jpegPages.Count * 2048L)),
                    temperature = 0,
                    skip_special_tokens = false,
                    images_config = new { crop_mode = true },
                    stream = false
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, uri)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                using var response = (client ?? Client).SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    return (null, "Il servizio Unlimited-OCR ha restituito un errore HTTP. Verificare manualmente il documento.", false);
                if (response.Content.Headers.ContentLength > MaxResponseBytes)
                    return (null, InvalidResponse, false);
                using var stream = response.Content.ReadAsStreamAsync(cancellation.Token).GetAwaiter().GetResult();
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = stream.ReadAsync(chunk.AsMemory(), cancellation.Token).GetAwaiter().GetResult()) != 0)
                {
                    if (buffer.Length + count > MaxResponseBytes) return (null, InvalidResponse, false);
                    buffer.Write(chunk, 0, count);
                }
                using var json = JsonDocument.Parse(buffer.ToArray());
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
                    return (null, InvalidResponse, false);
                var choice = choices[0];
                if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("finish_reason", out var finish) ||
                    finish.ValueKind != JsonValueKind.String)
                    return (null, InvalidResponse, false);
                if (finish.GetString() == "length")
                    return (null, "La risposta Unlimited-OCR è incompleta perché il modello ha raggiunto il limite di testo. Verificare manualmente il documento.", true);
                if (finish.GetString() != "stop" ||
                    !choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                    !message.TryGetProperty("content", out var answer) || answer.ValueKind != JsonValueKind.String)
                    return (null, InvalidResponse, false);
                var pages = ParsePages(answer.GetString()!, jpegPages.Count);
                return pages == null ? (null, InvalidResponse, false) : (pages, null, false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or
                JsonException or RegexMatchTimeoutException)
            {
                return (null, "Impossibile completare la lettura Unlimited-OCR del documento. Verificare manualmente il documento.", false);
            }
        }

        private static string[]? ParsePages(string raw, int expectedPages)
        {
            raw = Replace(raw, @"<[|｜]/?[a-zA-Z_]+[|｜]>", string.Empty).Trim();
            var markers = Regex.Matches(raw, @"<PAGE>", Options, RegexTimeout);
            if (markers.Count != expectedPages || raw[..markers[0].Index].Trim().Length != 0)
                return null;
            var pages = new string[expectedPages];
            for (var page = 0; page < pages.Length; page++)
            {
                var start = markers[page].Index + markers[page].Length;
                var end = page + 1 < pages.Length ? markers[page + 1].Index : raw.Length;
                var segment = raw[start..end];
                var blocks = Regex.Split(segment, @"<BLOCK>", Options, RegexTimeout);
                if (Replace(blocks[0], @"<MERGE>", string.Empty).Trim().Length != 0) return null;
                var text = new List<string>();
                foreach (var block in blocks.Skip(1))
                {
                    var fork = block.IndexOf("<FORK>", StringComparison.OrdinalIgnoreCase);
                    if (fork < 0) return null;
                    var header = Regex.Match(block[..fork],
                        @"^\s*(?<label>[a-z_]+)(?:\s*(?:<LEVEL\d+>|\[\s*-?\d+(?:\s*[,，]\s*-?\d+){3,}\s*\]))*\s*$", Options, RegexTimeout);
                    if (!header.Success) return null;
                    var label = header.Groups["label"].Value.ToLowerInvariant();
                    if (label.StartsWith("cross_")) label = label[6..];
                    else if (label.StartsWith("append_")) label = label[7..];
                    if (label is "image" or "figure" or "picture" or "chart" or "img" or "seal") continue;
                    if (label is not ("text" or "title" or "header" or "footer" or "page_number" or "table" or
                        "image_caption" or "image_footnote" or "page_footnote" or "equation" or "ref_text" or "aside_text" or "algorithm" or "other"))
                        return null;
                    var rawBody = block[(fork + 6)..];
                    var body = Replace(rawBody, @"<MERGE>", string.Empty);
                    if (Regex.IsMatch(body, @"</?(?:PAGE|BLOCK|FORK|LEVEL)\b", Options, RegexTimeout)) return null;
                    var subjectTable = label == "table" && Regex.IsMatch(body,
                        @"\b(?:RUOLO|LOCATORI|CONDUTTORI)\b", Options, RegexTimeout);
                    var roles = subjectTable ? ReadTableRoles(rawBody) : Array.Empty<string>();
                    body = Replace(body, @"</t[dh]\s*>", " ");
                    body = Replace(body, @"<br\s*/?>|</(?:tr|p|div)\s*>", "\n");
                    body = WebUtility.HtmlDecode(Replace(body, @"<[^>]*>", " "));
                    body = Replace(body, @"[ \t\f\v]+", " ").Trim();
                    // ReadTableRoles proves roles from cells. A flattened table row must not
                    // become a second role claim when an empty or merged cell lost its position.
                    if (label == "table")
                        body = Replace(body, @"(?m)^[ \t]*(?=(?:\d{3}\b|N[.°º]?[ \t]+CODICE\b|(?:LOCATORE|CONDUTTORE)[ \t]*:))", "| ");
                    if (body.Length > 0 && body.ToLowerInvariant() is not ("[non-text]" or "[unreadable]" or "[image]" or "[figure]"))
                        text.Add(body);
                    text.AddRange(roles);
                }
                // Cross/append blocks remain on their original page: no Markdown merge.
                pages[page] = string.Join("\n", text);
            }
            return pages;
        }

        private static IEnumerable<string> ReadTableRoles(string html)
        {
            // Accept just a complete, rectangular table with plain cells and explicit role headings.
            // In particular, colspan and rowspan would make column positions unreliable.
            const string cells = @"(?:<td>[^<]*</td>|<th>[^<]*</th>)";
            const string row = @"<tr>\s*(?:" + cells + @"\s*)+</tr>";
            if (!Regex.IsMatch(html, @"^\s*<table>\s*(?:" + row + @"\s*)+</table>\s*$", Options, RegexTimeout))
                yield break;

            var rows = Regex.Matches(html, row, Options, RegexTimeout).Cast<Match>()
                .Select(match => Regex.Matches(match.Value, @"<t[dh]>([^<]*)</t[dh]>", Options, RegexTimeout)
                    .Cast<Match>().Select(cell => WebUtility.HtmlDecode(cell.Groups[1].Value).Trim()).ToArray())
                .ToArray();
            var headings = rows[0];
            if (rows.Any(columns => columns.Length != headings.Length)) yield break;
            const string fiscalCode = @"^[A-Z]{6}[0-9]{2}[A-Z][0-9]{2}[A-Z][0-9]{3}[A-Z]$";
            if (Regex.IsMatch(string.Join("|", headings),
                @"^N[.°º]?\|CODICE\s+FISCALE\|RUOLO(?:\|N[.°º]?\|CODICE\s+FISCALE\|RUOLO)?$", Options, RegexTimeout))
            {
                foreach (var values in rows.Skip(1))
                {
                    if (values.Any(value => value.Length > 0 && !Regex.IsMatch(value, fiscalCode, Options, RegexTimeout) &&
                        !Regex.IsMatch(value, @"^(?:\d{3}|\d{11}|[ABCD])$", Options, RegexTimeout))) yield break;
                    for (var column = 0; column < values.Length; column += 3)
                        if (Regex.IsMatch(values[column], @"^\d{3}$", Options, RegexTimeout) &&
                            Regex.IsMatch(values[column + 1], fiscalCode, Options, RegexTimeout) &&
                            Regex.IsMatch(values[column + 2], @"^[ABCD]$", Options, RegexTimeout))
                            yield return string.Join(" ", values, column, 3);
                }
                yield break;
            }
            if (headings.Count(value => value.Equals("Locatori", StringComparison.OrdinalIgnoreCase)) != 1 ||
                headings.Count(value => value.Equals("Conduttori", StringComparison.OrdinalIgnoreCase)) != 1)
                yield break;

            var lessor = Array.FindIndex(headings, value => value.Equals("Locatori", StringComparison.OrdinalIgnoreCase));
            var tenant = Array.FindIndex(headings, value => value.Equals("Conduttori", StringComparison.OrdinalIgnoreCase));
            foreach (var values in rows.Skip(1))
            {
                if (values.Any(value => value.Length > 0 && !Regex.IsMatch(value, fiscalCode, Options, RegexTimeout) &&
                    !Regex.IsMatch(value, @"^\d{11}$", Options, RegexTimeout))) yield break;
                if (Regex.IsMatch(values[lessor], fiscalCode, Options, RegexTimeout)) yield return $"Locatore: {values[lessor]}";
                if (Regex.IsMatch(values[tenant], fiscalCode, Options, RegexTimeout)) yield return $"Conduttore: {values[tenant]}";
            }
        }

        private static string Replace(string value, string pattern, string replacement) =>
            Regex.Replace(value, pattern, replacement, Options, RegexTimeout);
    }
}
