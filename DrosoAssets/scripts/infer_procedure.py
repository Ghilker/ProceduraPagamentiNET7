"""Inference owned by ProcedureNet7: immutable bundled model, JSON progress on stdout."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time
import traceback
from concurrent.futures import ThreadPoolExecutor, as_completed
from threading import Event
from PIL import Image, UnidentifiedImageError

PREPARATION_WORKERS = 4
RENDER_VERSION = "poppler-2000-png-strict-v2"


class UnreadablePdfError(ValueError):
    def __init__(self, message):
        super().__init__("PDF non leggibile: " + message)


class PdfLimitError(ValueError):
    """Preserve the existing per-document outcome for the 20-page limit."""


def emit(stage, **values):
    print(json.dumps(dict(stage=stage, **values), ensure_ascii=False), flush=True)


def sha256(path):
    with open(path, "rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest().upper()


def save_json(path, value):
    path = Path(path)
    temporary = Path(str(path) + ".tmp")
    temporary.write_text(json.dumps(value, indent=2), encoding="utf-8")
    os.replace(temporary, path)


def direct_path(path):
    for item in (path, *path.parents):
        try:
            info = item.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("La preparazione richiede percorsi senza collegamenti: " + str(item))


def cached_pages(pdf, digest, check_cancel):
    record_path = Path(str(pdf) + ".pages.json")
    try:
        direct_path(pdf)
        direct_path(record_path)
        record = json.loads(record_path.read_text(encoding="utf-8-sig"))
        images = record["Images"]
        if (record.get("Version") != 1 or record.get("RenderVersion") != RENDER_VERSION
                or record["PdfSha256"] != digest or not 1 <= len(images) <= 20 or sha256(pdf) != digest):
            return None
        for number, entry in enumerate(images, 1):
            check_cancel()
            name = entry["File"]
            if not re.fullmatch(r"page-\d+\.png", name) or int(name[5:-4]) != number:
                return None
            image = Path(str(pdf) + ".pages") / name
            direct_path(image)
            if sha256(image) != entry["Sha256"]:
                return None
        return len(images)
    except InterruptedError:
        raise
    except (OSError, ValueError, KeyError, TypeError):
        return None


def native(command, check_cancel, timeout):
    with subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0) as process:
        deadline = time.monotonic() + timeout
        try:
            while True:
                check_cancel()
                if time.monotonic() >= deadline:
                    raise TimeoutError("Tempo di preparazione PDF superato")
                try:
                    output, error = process.communicate(timeout=.2)
                    break
                except subprocess.TimeoutExpired:
                    continue
            diagnostic = error.decode("utf-8", errors="replace").strip()
            if process.returncode == 0:
                # Missing display fonts are runtime notices, not damaged PDF syntax.
                diagnostic = "\n".join(line for line in diagnostic.splitlines() if not re.match(
                    r"^\s*Syntax Error:\s*No display font for\s+", line, re.IGNORECASE))
            # Poppler can salvage a damaged PDF and exit successfully. Reject
            # syntax errors, while ordinary warnings remain non-fatal.
            operational = process.returncode == 2 or any(text in diagnostic.lower() for text in
                ("i/o error", "permission denied", "access denied", "no such file", "cannot open output",
                 "fontconfig error", "configuration error", "error while loading shared libraries", "usage:"))
            if not operational and (process.returncode in (1, 3) or re.search(r"\bSyntax Error\b", diagnostic, re.IGNORECASE)):
                raise UnreadablePdfError(diagnostic or "Poppler non riesce a leggere il documento")
            if process.returncode or operational:
                raise ValueError(diagnostic or "Errore operativo durante la preparazione PDF")
            return output.decode("utf-8", errors="replace")
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()


def prepare(original, digest, root, poppler, check_cancel):
    check_cancel()
    if sha256(original) != digest:
        raise ValueError("PDF modificato prima della preparazione")
    with open(original, "rb") as source:
        header = source.read(1024)
    if not header:
        raise UnreadablePdfError("file vuoto")
    if b"%PDF-" not in header:
        raise UnreadablePdfError("intestazione %PDF- assente nei primi 1024 byte")
    pdf = root / (digest + ".pdf")
    images_directory = Path(str(pdf) + ".pages")
    record_path = Path(str(pdf) + ".pages.json")
    for path in (root, pdf, images_directory, record_path):
        direct_path(path)
    pages = cached_pages(pdf, digest, check_cancel)
    if pages is not None:
        return pdf, pages, True
    record_path.unlink(missing_ok=True)
    shutil.copyfile(original, pdf)
    if sha256(pdf) != digest:
        raise ValueError("PDF modificato durante la preparazione")
    info = native([str(poppler / "pdfinfo.exe"), "-enc", "UTF-8", str(pdf)], check_cancel, 120)
    match = re.search(r"(?m)^Pages:\s+(\d+)", info)
    if not match or int(match[1]) < 1:
        raise UnreadablePdfError("numero di pagine non leggibile")
    if int(match[1]) > 20:
        raise PdfLimitError("PDF oltre il limite operativo di 20 pagine")
    pages = int(match[1])
    images_directory.mkdir(exist_ok=True)
    for image in (*images_directory.glob("page-*.png"), *images_directory.glob("page-*.ppm")):
        check_cancel()
        direct_path(image)
        image.unlink()
    # Poppler's PNG writer is expensive. Its RGB raster is identical in PPM;
    # Pillow encodes it losslessly much faster, preserving the existing cache format.
    try:
        native([str(poppler / "pdftoppm.exe"), "-f", "1", "-l", str(pages), "-forcenum", "-scale-to", "2000",
                str(pdf), str(images_directory / "page")], check_cancel, 120 * pages)
        rasters = sorted(images_directory.glob("page-*.ppm"), key=lambda path: int(path.stem.rsplit("-", 1)[1]))
        if [int(path.stem.rsplit("-", 1)[1]) for path in rasters] != list(range(1, pages + 1)):
            raise UnreadablePdfError("rendering delle pagine incompleto")
        for raster in rasters:
            check_cancel()
            direct_path(raster)
            try:
                bitmap = Image.open(raster)
            except UnidentifiedImageError as error:
                raise UnreadablePdfError("pagina prodotta dal renderer non leggibile") from error
            with bitmap:
                try:
                    bitmap.load()
                except OSError as error:
                    if error.errno is None and any(text in str(error).lower() for text in
                            ("image file is truncated", "not enough image data", "broken data stream")):
                        raise UnreadablePdfError("pagina prodotta dal renderer incompleta") from error
                    raise
                bitmap.save(raster.with_suffix(".png"), format="PNG", compress_level=3)
            raster.unlink()
    finally:
        for raster in images_directory.glob("page-*.ppm"):
            direct_path(raster)
            raster.unlink()
    images = sorted(images_directory.glob("page-*.png"), key=lambda path: int(path.stem.rsplit("-", 1)[1]))
    if [int(path.stem.rsplit("-", 1)[1]) for path in images] != list(range(1, pages + 1)):
        raise UnreadablePdfError("rendering delle pagine incompleto")
    check_cancel()
    save_json(record_path, dict(Version=1, RenderVersion=RENDER_VERSION, PdfSha256=digest,
              Images=[dict(File=path.name, Sha256=sha256(path)) for path in images]))
    return pdf, pages, False


def prepare_all(values, root, poppler, check_cancel):
    direct_path(root)
    root.mkdir(exist_ok=True)
    originals, unique, prepared, failures = [], {}, {}, {}
    for index, value in enumerate(values):
        check_cancel()
        path = Path(value).resolve()
        if path.suffix.lower() != ".pdf" or path.stat().st_size > 30 * 1024 * 1024:
            raise ValueError(f"Percorso PDF non valido o file oltre 30 MB: {path}")
        digest = sha256(path)
        originals.append((path, digest))
        unique.setdefault(digest, []).append(path)
        if index == 0 or (index + 1) % 25 == 0 or index + 1 == len(values):
            emit("Verifica PDF", current=index + 1, total=len(values))
    started = time.monotonic()
    completed = reused = 0
    stopped = Event()

    def worker_cancel():
        if stopped.is_set():
            raise InterruptedError("Preparazione interrotta")
        check_cancel()

    emit("Preparazione PDF", current=0, total=len(values),
         message=f"fino a {PREPARATION_WORKERS} PDF contemporaneamente; {len(unique)} contenuti distinti")
    pool = ThreadPoolExecutor(max_workers=PREPARATION_WORKERS)
    try:
        pending = {pool.submit(prepare, paths[0], digest, root, poppler, worker_cancel): digest
                   for digest, paths in unique.items()}
        for future in as_completed(pending):
            check_cancel()
            digest = pending[future]
            try:
                pdf, pages, cached = future.result()
                prepared[digest] = (pdf, pages)
                reused += int(cached)
            except InterruptedError:
                raise
            except (UnreadablePdfError, PdfLimitError) as error:
                failures[digest] = error
                emit("PDF rifiutato" if isinstance(error, UnreadablePdfError) else "PDF da controllare",
                     message=f"{unique[digest][0].name}: {error}")
            completed += len(unique[digest])
            emit("Preparazione PDF", current=completed, total=len(values),
                 message=f"riutilizzati {reused} PDF distinti; {time.monotonic() - started:.1f} s")
    finally:
        stopped.set()
        pool.shutdown(wait=True, cancel_futures=True)
    check_cancel()
    emit("Preparazione PDF terminata", current=completed, total=len(values),
         message=f"{len(prepared)} pronti, {reused} riutilizzati, {len(failures)} con errori in {time.monotonic() - started:.1f} s")
    return originals, prepared, failures


def run(request_path):
    request = json.loads(request_path.read_text(encoding="utf-8-sig"))
    if request.get("Version") != 1 or not request.get("Documents"):
        raise ValueError("Richiesta di analisi non valida")
    directory = request_path.parent
    assets = Path(request["AssetsDirectory"])
    model_directory = assets / "model"
    poppler = Path(request["PopplerDirectory"])
    cancelled = directory / "cancel.request"

    def check_cancel():
        if cancelled.exists():
            raise InterruptedError("Analisi annullata")

    check_cancel()
    checkpoint = sha256(model_directory / "best.pt")
    from inference_decisions import load_policy, apply_decisions
    policy, policy_hash = load_policy(assets, checkpoint)
    # Prepared images belong to the session, so repeated analyses can reuse verified renders.
    originals, prepared, failures = prepare_all(request["Documents"], directory.parent / "prepared", poppler, check_cancel)
    rows = []

    if prepared:
        import torch
        from cache_paths import response_cache_directory
        from document_engine import model_from_checkpoint, predictions, prepare_documents
        from document_model import Responses
        from file_verification import VerificationCache

        check_cancel()
        device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        emit("GPU disponibile" if device.type == "cuda" else "Analisi su CPU", message=str(device))
        # Load the already built graph; inference never creates locks or files alongside the model.
        graph = torch.load(assets / "brain" / "visual-graph.pt", map_location="cpu", weights_only=True)
        model, config = model_from_checkpoint(model_directory, graph, device)
        cache = None if model.learned_visual_input else response_cache_directory()
        if cache is not None:
            emit("Cache delle risposte", message=str(cache))
        responses = Responses(model, config, cache, device, check_cancel)
        with VerificationCache(directory / "verification.sqlite3", check_cancel) as verifier:
            documents = prepare_documents([dict(path=str(pdf)) for pdf, _ in prepared.values()],
                                          check_cancel, verifier=verifier, stage="classificazione")
            started = time.monotonic()
            # Overlap CPU page preparation with inference, keeping at most two pages ahead.
            # Prediction order and aggregation of all pages into one document are unchanged.
            with responses.prefetch((document, 0, None) for document in documents):
                result = predictions(model, documents, device, check_cancel, responses, .75, "inference")
            emit("Classificazione terminata", current=len(documents), total=len(documents),
                 message=f"{time.monotonic() - started:.1f} s")
        predicted = {row["sha256"].upper(): row for row in result["rows"]}
        apply_decisions(result["rows"], policy)
    else:
        predicted = {}

    for path, digest in originals:
        check_cancel()
        if sha256(path) != digest:
            raise ValueError(f"PDF modificato durante l'analisi: {path}")
        if digest in failures:
            failure = failures[digest]
            unreadable = isinstance(failure, UnreadablePdfError)
            rows.append(dict(Path=str(path), Sha256=digest, Predicted="", Confidence=0, Margin=None,
                             NeedsReview=not unreadable, Pages=0, Error=str(failure)))
            if unreadable:
                rows[-1]["PdfUnreadable"] = True
            continue
        row = predicted[digest]
        label = "RICEVUTA" if row["predicted"] == "RICEVUTA_PERMESSO" else row["predicted"]
        rows.append(dict(Path=str(path), Sha256=digest, Predicted=label, Confidence=row["confidence"],
                         Margin=row["margin"], NeedsReview=row["review"],
                         Pages=prepared[digest][1], Error=""))
    if sha256(model_directory / "best.pt") != checkpoint:
        raise ValueError("Checkpoint modificato durante l'analisi")
    if policy_hash is not None and sha256(model_directory / "decision-visual-only.json") != policy_hash:
        raise ValueError("Politica di decisione modificata durante l'analisi")
    check_cancel()
    save_json(directory / "result.json", dict(Version=1, CheckpointSha256=checkpoint, DecisionPolicySha256=policy_hash, Rows=rows))
    emit("Analisi completata", current=len(rows), total=len(rows))


if __name__ == "__main__":
    request_path = Path(sys.argv[1]).resolve()
    try:
        run(request_path)
    except InterruptedError:
        emit("Analisi annullata")
        sys.exit(2)
    except Exception:
        details = traceback.format_exc()
        try:
            (request_path.parent / "error.txt").write_text(details, encoding="utf-8")
        except OSError:
            pass  # stderr preserves the original traceback even if the run folder is not writable.
        sys.stderr.write(details)
        sys.exit(1)
