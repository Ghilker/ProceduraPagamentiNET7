import contextlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
from threading import Event, Lock
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "DrosoAssets" / "scripts"))
import infer_procedure as engine
from PIL import Image


class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.cache = self.root / "prepared"
        self.cache.mkdir()

    def tearDown(self):
        self.temporary.cleanup()

    def pdf(self, name):
        path = self.root / (name + ".pdf")
        path.write_bytes(b"%PDF-1.4 synthetic " + name.encode())
        return path

    def render(self, command, check_cancel, timeout):
        check_cancel()
        if Path(command[0]).name == "pdfinfo.exe":
            return "Pages: 2\n"
        for number in (1, 2):
            Image.new("RGB", (20, 30), (number * 50, 60, 70)).save(command[-1] + f"-{number}.ppm")
        return ""

    def test_verified_cache_reused_and_modified_or_incomplete_cache_regenerated(self):
        original = self.pdf("source")
        digest = engine.sha256(original)
        with patch.object(engine, "native", side_effect=self.render) as native:
            pdf, pages, reused = engine.prepare(original, digest, self.cache, self.root, lambda: None)
            self.assertEqual((pages, reused, native.call_count), (2, False, 2))
            self.assertFalse(list(self.cache.rglob("*.ppm")))
            for number in (1, 2):
                with Image.open(Path(str(pdf) + ".pages") / f"page-{number}.png") as image:
                    self.assertEqual(image.getpixel((0, 0)), (number * 50, 60, 70))
            self.assertEqual(engine.prepare(original, digest, self.cache, self.root, lambda: None), (pdf, 2, True))
            self.assertEqual(native.call_count, 2)
            image = Path(str(pdf) + ".pages") / "page-1.png"
            image.write_bytes(b"changed")
            self.assertFalse(engine.prepare(original, digest, self.cache, self.root, lambda: None)[2])
            self.assertEqual(native.call_count, 4)
            image.unlink()
            self.assertFalse(engine.prepare(original, digest, self.cache, self.root, lambda: None)[2])
            self.assertEqual(native.call_count, 6)
            record = Path(str(pdf) + ".pages.json")
            data = json.loads(record.read_text())
            data["RenderVersion"] = "obsolete"
            record.write_text(json.dumps(data))
            self.assertFalse(engine.prepare(original, digest, self.cache, self.root, lambda: None)[2])
            self.assertEqual(native.call_count, 8)

    def test_empty_or_missing_pdf_header_is_rejected_without_poppler(self):
        for content in (b"", b"this is not a PDF", b"x" * 1024 + b"%PDF-1.4"):
            original = self.pdf("bad-header")
            original.write_bytes(content)
            with self.subTest(size=len(content)), patch.object(engine, "native", side_effect=self.render) as native:
                with self.assertRaisesRegex(ValueError, "PDF non leggibile"):
                    engine.prepare(original, engine.sha256(original), self.cache, self.root, lambda: None)
                self.assertEqual(native.call_count, 0)

    def test_parallel_preparation_is_bounded_deduplicated_and_keeps_input_order(self):
        paths = [self.pdf(str(i)) for i in range(8)]
        paths += [paths[0], paths[6]]
        lock, ready = Lock(), Event()
        active = peak = calls = 0

        def prepare(path, digest, *args):
            nonlocal active, peak, calls
            with lock:
                active += 1
                calls += 1
                peak = max(peak, active)
                if active == 4:
                    ready.set()
            try:
                if not ready.wait(5):
                    raise RuntimeError("Preparation still sequential")
                time.sleep(.08 if path.stem == "0" else .01)
                if path.stem == "6":
                    raise engine.UnreadablePdfError("invalid PDF")
                return path, 2, path.stem == "1"
            finally:
                with lock:
                    active -= 1

        events = []
        with patch.object(engine, "prepare", side_effect=prepare), patch.object(engine, "emit", side_effect=lambda stage, **kw: events.append((stage, kw))):
            originals, prepared, failures = engine.prepare_all(paths, self.cache, self.root, lambda: None)
        self.assertEqual((peak, active, calls), (4, 0, 8))
        self.assertEqual([path for path, _ in originals], [path.resolve() for path in paths])
        self.assertEqual((len(prepared), len(failures)), (7, 1))
        self.assertIsInstance(failures[engine.sha256(paths[6])], engine.UnreadablePdfError)
        self.assertEqual(str(failures[engine.sha256(paths[6])]), "PDF non leggibile: invalid PDF")
        progress = [kw["current"] for stage, kw in events if stage == "Preparazione PDF"]
        self.assertEqual(progress, sorted(set(progress)))
        self.assertEqual(progress[-1], len(paths))

    def test_cancellation_stops_workers_without_publishing_results(self):
        paths = [self.pdf(str(i)) for i in range(12)]
        cancelled, lock = Event(), Lock()
        active = started = 0

        def check_cancel():
            if cancelled.is_set():
                raise InterruptedError("cancelled")

        def prepare(path, digest, root, poppler, worker_cancel):
            nonlocal active, started
            with lock:
                active += 1
                started += 1
                if active == 4:
                    cancelled.set()
            try:
                while True:
                    worker_cancel()
                    time.sleep(.01)
            finally:
                with lock:
                    active -= 1

        with patch.object(engine, "prepare", side_effect=prepare), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(InterruptedError):
                engine.prepare_all(paths, self.cache, self.root, check_cancel)
        self.assertEqual(active, 0)
        self.assertFalse(list(self.cache.iterdir()))

    def test_partial_render_is_not_reused(self):
        original = self.pdf("source")
        digest = engine.sha256(original)

        def interrupted(command, check_cancel, timeout):
            if Path(command[0]).name == "pdfinfo.exe":
                return "Pages: 2\n"
            Image.new("RGB", (20, 30)).save(command[-1] + "-1.ppm")
            raise InterruptedError("cancelled")

        with patch.object(engine, "native", side_effect=interrupted):
            with self.assertRaises(InterruptedError):
                engine.prepare(original, digest, self.cache, self.root, lambda: None)
        self.assertFalse(Path(str(self.cache / (digest + ".pdf")) + ".pages.json").exists())
        self.assertFalse(list(self.cache.rglob("*.ppm")))
        with patch.object(engine, "native", side_effect=self.render):
            self.assertFalse(engine.prepare(original, digest, self.cache, self.root, lambda: None)[2])

    def test_incomplete_raster_is_rejected_and_temporary_pages_removed(self):
        original = self.pdf("incomplete")
        digest = engine.sha256(original)

        def incomplete(command, check_cancel, timeout):
            if Path(command[0]).name == "pdfinfo.exe":
                return "Pages: 2\n"
            Image.new("RGB", (20, 30)).save(command[-1] + "-1.ppm")
            return ""

        with patch.object(engine, "native", side_effect=incomplete):
            with self.assertRaisesRegex(engine.UnreadablePdfError, "incompleto"):
                engine.prepare(original, digest, self.cache, self.root, lambda: None)
        self.assertFalse(list(self.cache.rglob("*.ppm")))
        self.assertFalse(list(self.cache.glob("*.pages.json")))

    @unittest.skipUnless(os.environ.get("DROSO_TEST_POPPLER"), "Poppler path not provided")
    def test_real_poppler_multiple_pages_and_second_run_uses_cache(self):
        original = Path(os.environ["DROSO_TEST_PDF"])
        poppler = Path(os.environ["DROSO_TEST_POPPLER"])
        digest = engine.sha256(original)
        with patch.object(engine, "native", wraps=engine.native) as native:
            pdf, pages, reused = engine.prepare(original, digest, self.cache, poppler, lambda: None)
            self.assertEqual((pages, reused, native.call_count), (2, False, 2))
            self.assertEqual(engine.prepare(original, digest, self.cache, poppler, lambda: None), (pdf, 2, True))
            self.assertEqual(native.call_count, 2)
            for image in Path(str(pdf) + ".pages").glob("*.png"):
                with Image.open(image) as bitmap:
                    self.assertEqual(max(bitmap.size), 2000)
        baseline = self.root / "baseline"
        baseline.mkdir()
        engine.native([str(poppler / "pdftoppm.exe"), "-f", "1", "-l", str(pages), "-forcenum",
                       "-scale-to", "2000", "-png", str(original), str(baseline / "page")], lambda: None, 120)
        for image in baseline.glob("*.png"):
            with Image.open(image) as old, Image.open(Path(str(pdf) + ".pages") / image.name) as new:
                self.assertEqual((new.mode, new.size, new.tobytes()), (old.mode, old.size, old.tobytes()))


if __name__ == "__main__":
    unittest.main(verbosity=2)
