using System.Net.Http;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Tesseract;

namespace ProcedureNet7
{
    internal sealed class DomicilioPdfOcr : IDisposable
    {
        private const int Dpi = 300;
        private const long MaxPagePixels = 20_000_000;
        private const int MaxUnlimitedOcrImageBytes = 20 * 1024 * 1024;
        internal const float MinConfidence = 0.80f;
        internal const int MaxParallelDocuments = 4;
        private static readonly SemaphoreSlim Gate = new(MaxParallelDocuments, MaxParallelDocuments);

        private readonly byte[] _file;
        private readonly int _pageCount;
        private readonly HttpClient? _unlimitedOcrClient;
        private readonly string? _unlimitedOcrUrl;
        private readonly DomicilioUnlimitedOcrCache? _cache;
        private TesseractEngine? _engine;
        private IDocReader? _document;
        private bool _ownsGate;
        private string? _initializationError;
        private (string Text, string? Warning)[]? _unlimitedOcrPages;

        static DomicilioPdfOcr()
        {
            // Tesseract's default assembly-location lookup is empty in single-file builds.
            TesseractEnviornment.CustomSearchPath = AppContext.BaseDirectory;
        }

        internal bool UsedCachedTranscription { get; private set; }

        public DomicilioPdfOcr(byte[] file, int pageCount, string? unlimitedOcrUrl, HttpClient? unlimitedOcrClient)
            : this(file, pageCount, unlimitedOcrUrl, unlimitedOcrClient, null) { }

        internal DomicilioPdfOcr(byte[] file, int pageCount, string? unlimitedOcrUrl, HttpClient? unlimitedOcrClient,
            DomicilioUnlimitedOcrCache? cache)
        {
            _file = file;
            _pageCount = pageCount;
            _unlimitedOcrUrl = unlimitedOcrUrl;
            _unlimitedOcrClient = unlimitedOcrClient;
            _cache = cache;
        }

        public (string Text, string? Warning) ReadPage(int pageNumber)
        {
            if (!string.IsNullOrWhiteSpace(_unlimitedOcrUrl))
            {
                if (_unlimitedOcrPages == null)
                {
                    var cache = _cache ?? DomicilioUnlimitedOcrCache.ForEndpoint(_unlimitedOcrUrl, _unlimitedOcrClient);
                    if (cache == null) _unlimitedOcrPages = ReadWithUnlimitedOcr(_unlimitedOcrUrl);
                    else
                    {
                        _unlimitedOcrPages = cache.Read(_file, _pageCount, () => ReadWithUnlimitedOcr(_unlimitedOcrUrl), out var reused);
                        UsedCachedTranscription = reused;
                    }
                }
                return _unlimitedOcrPages[pageNumber - 1];
            }

            if (_initializationError != null) return (string.Empty, _initializationError);
            try
            {
                if (_engine == null)
                {
                    var dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
                    if (!File.Exists(Path.Combine(dataPath, "ita.traineddata")))
                    {
                        _initializationError = "Il modello OCR italiano tessdata/ita.traineddata non è disponibile.";
                        return (string.Empty, _initializationError);
                    }

                    try
                    {
                        OpenForRendering();
                        _engine = new TesseractEngine(dataPath, "ita", EngineMode.LstmOnly);
                    }
                    catch (Exception)
                    {
                        _initializationError = "Impossibile inizializzare OCR o rendering PDF: verificare modello italiano e librerie native distribuite.";
                        return (string.Empty, _initializationError);
                    }
                }

                byte[] png;
                using (var image = RenderPage(pageNumber))
                using (var buffer = new MemoryStream())
                {
                    if (image == null)
                        return (string.Empty, "La pagina supera il limite operativo di rendering OCR di 20 milioni di pixel a 300 DPI.");
                    image.SaveAsPng(buffer);
                    png = buffer.ToArray();
                }

                using var pix = Pix.LoadFromMemory(png);
                pix.XRes = pix.YRes = Dpi;
                using var recognized = _engine.Process(pix, PageSegMode.Auto);
                var text = recognized.GetText() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return (string.Empty, "L'OCR non ha riconosciuto testo nella pagina.");
                var confidence = recognized.GetMeanConfidence();
                return (text, !float.IsFinite(confidence) || confidence < MinConfidence
                    ? $"La confidenza media OCR ({confidence:P0}) è inferiore alla soglia operativa dell'80%."
                    : null);
            }
            catch (Exception)
            {
                return (string.Empty, "La pagina non è leggibile tramite OCR o rendering PDF.");
            }
        }

        private (string Text, string? Warning)[] ReadWithUnlimitedOcr(string endpoint)
        {
            const int maxPagesPerBatch = 3;
            var result = new (string Text, string? Warning)[_pageCount];
            try
            {
                OpenForRendering();
                var images = new List<byte[]>();
                var imageBytes = 0L;
                var firstPage = 0;
                void FlushBatch()
                {
                    if (images.Count == 0) return;
                    ReadUnlimitedBatch(endpoint, images, firstPage, result);
                    images.Clear();
                    imageBytes = 0;
                }

                for (var index = 0; index < _pageCount; index++)
                {
                    using var image = RenderPage(index + 1, fitPixelLimit: true);
                    if (image == null)
                    {
                        FlushBatch();
                        result[index] = (string.Empty, "Impossibile renderizzare la pagina entro il limite OCR di 20 milioni di pixel.");
                        continue;
                    }

                    using var buffer = new MemoryStream();
                    image.SaveAsJpeg(buffer, new JpegEncoder { Quality = 85 });
                    if (images.Count == maxPagesPerBatch || imageBytes + buffer.Length > MaxUnlimitedOcrImageBytes)
                        FlushBatch();
                    if (buffer.Length > MaxUnlimitedOcrImageBytes)
                    {
                        result[index] = (string.Empty, "L'immagine supera il limite di 20 MB per Unlimited-OCR.");
                        continue;
                    }
                    if (images.Count == 0) firstPage = index;
                    imageBytes += buffer.Length;
                    images.Add(buffer.ToArray());
                }
                FlushBatch();
            }
            catch (Exception)
            {
                for (var index = 0; index < result.Length; index++)
                    if (result[index].Text == null)
                        result[index] = (string.Empty, "Impossibile preparare la pagina del PDF per Unlimited-OCR.");
            }
            return result;
        }

        private void ReadUnlimitedBatch(string endpoint, List<byte[]> images, int firstPage,
            (string Text, string? Warning)[] result)
        {
            var (pages, warning, truncated) = DomicilioUnlimitedOcr.ReadDocument(endpoint, images, _unlimitedOcrClient);
            if (truncated && images.Count > 1)
            {
                var half = images.Count / 2;
                ReadUnlimitedBatch(endpoint, images.GetRange(0, half), firstPage, result);
                ReadUnlimitedBatch(endpoint, images.GetRange(half, images.Count - half), firstPage + half, result);
                return;
            }

            for (var index = 0; index < images.Count; index++)
            {
                var text = pages?[index];
                result[firstPage + index] = string.IsNullOrWhiteSpace(text)
                    ? (string.Empty, warning ?? "Unlimited-OCR non ha riconosciuto testo nella pagina.")
                    : (text, null);
            }
        }

        private void OpenForRendering()
        {
            Gate.Wait();
            _ownsGate = true;
            _document = DocLib.Instance.GetDocReader(_file, new PageDimensions(Dpi / 72d));
        }

        private Image<Bgra32>? RenderPage(int pageNumber, bool fitPixelLimit = false)
        {
            double scale;
            using (var page = _document!.GetPageReader(pageNumber - 1))
            {
                var width = page.GetPageWidth();
                var height = page.GetPageHeight();
                if (width <= 0 || height <= 0) return null;
                var pixels = (long)width * height;
                if (pixels <= MaxPagePixels) return ReadImage(page, width, height);
                if (!fitPixelLimit) return null;
                // Leave room for rounding of scaled page dimensions in PDFium.
                scale = (Dpi / 72d) * Math.Sqrt(MaxPagePixels * 0.99 / pixels);
            }

            using var reduced = DocLib.Instance.GetDocReader(_file, new PageDimensions(scale));
            using var reducedPage = reduced.GetPageReader(pageNumber - 1);
            var reducedWidth = reducedPage.GetPageWidth();
            var reducedHeight = reducedPage.GetPageHeight();
            return reducedWidth > 0 && reducedHeight > 0 && (long)reducedWidth * reducedHeight <= MaxPagePixels
                ? ReadImage(reducedPage, reducedWidth, reducedHeight) : null;
        }

        private static Image<Bgra32> ReadImage(IPageReader page, int width, int height)
        {
            return SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(page.GetImage(new NaiveTransparencyRemover()), width, height);
        }

        public void Dispose()
        {
            try
            {
                _document?.Dispose();
            }
            finally
            {
                try { _engine?.Dispose(); }
                finally
                {
                    if (_ownsGate)
                    {
                        _ownsGate = false;
                        Gate.Release();
                    }
                }
            }
        }
    }
}
