"""Unreadable PDF content must stop before classification or alternate rendering."""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "DrosoAssets/scripts"))
import infer_procedure as engine
from PIL import Image


def synthetic_pdf():
    objects = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
               b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
               b"<< /Length 4 >>\nstream\nq Q\nendstream"]
    data = bytearray(b"%PDF-1.4\n")
    offsets = [0]
    for index, value in enumerate(objects, 1):
        offsets.append(len(data))
        data += f"{index} 0 obj\n".encode() + value + b"\nendobj\n"
    xref = len(data)
    data += b"xref\n0 5\n0000000000 65535 f \n"
    for offset in offsets[1:]:
        data += f"{offset:010d} 00000 n \n".encode()
    data += f"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode()
    return bytes(data)


class Process:
    def __init__(self, code=0, diagnostic=b"", output=b"Pages: 2\n"):
        self.returncode, self.diagnostic, self.output = code, diagnostic, output
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def communicate(self, timeout=None): return self.output, self.diagnostic
    def poll(self): return self.returncode


class PdfRejectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.cache = self.root / "prepared"
        self.cache.mkdir()
        self.original = self.root / "document.pdf"
        self.original.write_bytes(synthetic_pdf())

    def tearDown(self):
        self.temporary.cleanup()

    def prepare(self):
        return engine.prepare(self.original, engine.sha256(self.original), self.cache, self.root, lambda: None)

    def test_pdfinfo_failure_never_calls_renderer(self):
        with patch.object(engine, "native", side_effect=engine.UnreadablePdfError("truncated input")) as native:
            with self.assertRaises(engine.UnreadablePdfError):
                self.prepare()
        self.assertEqual(native.call_count, 1)
        self.assertEqual(Path(native.call_args.args[0][0]).name, "pdfinfo.exe")
        self.assertFalse(list(self.cache.glob("*.pages.json")))

    def test_partial_or_broken_raster_is_rejected_without_alternative(self):
        for mode in ("missing", "broken", "renderer-error"):
            def render(command, *args):
                if Path(command[0]).name == "pdfinfo.exe": return "Pages: 2\n"
                if mode == "renderer-error": raise engine.UnreadablePdfError("render failed")
                if mode == "missing": Image.new("RGB", (20, 30)).save(command[-1] + "-1.ppm")
                else:
                    for number in (1, 2): Path(command[-1] + f"-{number}.ppm").write_bytes(b"broken raster")
                return ""
            with self.subTest(mode=mode), patch.object(engine, "native", side_effect=render) as native:
                with self.assertRaises(engine.UnreadablePdfError): self.prepare()
                self.assertEqual(native.call_count, 2)
                self.assertFalse(list(self.cache.glob("*.pages.json")))
                self.assertFalse(list(self.cache.rglob("*.ppm")))

    def test_native_syntax_error_rejects_even_on_success_and_warnings_do_not(self):
        for code, diagnostic in ((0, b"Syntax Error: damaged cross-reference"), (1, b"Syntax Error: truncated file"),
                                 (3, b"Permission Error: encrypted document cannot be read")):
            with self.subTest(code=code), patch.object(engine.subprocess, "Popen", return_value=Process(code, diagnostic)):
                with self.assertRaises(engine.UnreadablePdfError): engine.native(["pdfinfo.exe"], lambda: None, 1)
        with patch.object(engine.subprocess, "Popen", return_value=Process(0, b"Syntax Warning: harmless metadata")):
            self.assertEqual(engine.native(["pdfinfo.exe"], lambda: None, 1), "Pages: 2\n")

    def test_native_runtime_and_output_failures_are_not_unreadable_content(self):
        for code, diagnostic in ((1, b"I/O Error: Permission denied"), (2, b"I/O Error: cannot open output file"),
                                 (99, b"Usage: unknown option"), (1, b"Configuration Error: runtime unavailable")):
            with self.subTest(code=code), patch.object(engine.subprocess, "Popen", return_value=Process(code, diagnostic)):
                with self.assertRaises(ValueError) as caught: engine.native(["pdfinfo.exe"], lambda: None, 1)
                self.assertNotIsInstance(caught.exception, engine.UnreadablePdfError)
        for error in (PermissionError("cannot execute runtime"), FileNotFoundError("runtime missing")):
            with self.subTest(error=type(error).__name__), patch.object(engine.subprocess, "Popen", side_effect=error):
                with self.assertRaises(type(error)): engine.native(["pdfinfo.exe"], lambda: None, 1)

    def test_successful_display_font_diagnostics_do_not_hide_real_syntax_errors(self):
        fonts = b"Syntax Error: No display font for Symbol\nSyntax Error: No display font for ArialUnicode\n"
        with patch.object(engine.subprocess, "Popen", return_value=Process(0, fonts)):
            self.assertEqual(engine.native(["pdfinfo.exe"], lambda: None, 1), "Pages: 2\n")
        for diagnostic in (b"Syntax Error: damaged cross-reference", fonts + b"Syntax Error: damaged cross-reference\n"):
            with self.subTest(mixed=diagnostic.startswith(fonts)), patch.object(engine.subprocess, "Popen", return_value=Process(0, diagnostic)):
                with self.assertRaises(engine.UnreadablePdfError): engine.native(["pdfinfo.exe"], lambda: None, 1)

    def test_operational_preparation_error_aborts_instead_of_creating_rejection_rows(self):
        with patch.object(engine, "prepare", side_effect=PermissionError("output denied")), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(PermissionError):
                engine.prepare_all([self.original], self.cache, self.root, lambda: None)
        digest = engine.sha256(self.original)
        self.original.write_bytes(b"changed contents")
        with self.assertRaisesRegex(ValueError, "modificato") as caught:
            engine.prepare(self.original, digest, self.cache, self.root, lambda: None)
        self.assertNotIsInstance(caught.exception, engine.UnreadablePdfError)

    def test_output_image_permission_error_remains_operational(self):
        def render(command, *args):
            if Path(command[0]).name == "pdfinfo.exe": return "Pages: 1\n"
            Image.new("RGB", (20, 30)).save(command[-1] + "-1.ppm")
            return ""
        original_save = Image.Image.save
        def save(bitmap, path, *args, **kwargs):
            if Path(path).suffix == ".png": raise PermissionError("output denied")
            return original_save(bitmap, path, *args, **kwargs)
        with patch.object(engine, "native", side_effect=render), patch.object(Image.Image, "save", side_effect=save, autospec=True):
            with self.assertRaises(PermissionError): self.prepare()

    def test_twenty_page_limit_keeps_existing_outcome_without_unreadable_marker(self):
        with patch.object(engine, "native", return_value="Pages: 21\n"), contextlib.redirect_stdout(io.StringIO()):
            _, prepared, failures = engine.prepare_all([self.original], self.cache, self.root, lambda: None)
        self.assertFalse(prepared)
        failure = failures[engine.sha256(self.original)]
        self.assertIsInstance(failure, engine.PdfLimitError)
        self.assertNotIsInstance(failure, engine.UnreadablePdfError)

    def test_all_unreadable_runs_without_importing_torch_cache_or_model(self):
        assets = self.root / "assets"
        (assets / "model").mkdir(parents=True)
        (assets / "model/best.pt").write_bytes(b"not a tensor: must never be loaded")
        checkpoint = engine.sha256(assets / "model/best.pt")
        engine.save_json(assets / "model/model.json", dict(version=1, engine_version=3, labels=["PASSAPORTO", "RICEVUTA", "PERMESSO_SOGGIORNO"]))
        engine.save_json(assets / "manifest.json", dict(Version=1, CheckpointSha256=checkpoint))
        empty, bad = self.root / "empty.pdf", self.root / "bad.pdf"
        empty.write_bytes(b""); bad.write_bytes(b"not a PDF")
        request = self.root / "request.json"
        values = [str(empty), str(bad), str(empty)]
        poppler, expected_native_calls = "nonexistent", 0
        marker = Path(os.environ.get("LOCALAPPDATA", "")) / "DrosoDocLab/tools/poppler/installed.txt"
        if marker.exists():
            managed = Path(marker.read_text(encoding="utf-8-sig").strip())
            if (managed / "pdfinfo.exe").is_file():
                poppler, expected_native_calls = str(managed), 1
                truncated = self.root / "truncated.pdf"; truncated.write_bytes(synthetic_pdf()[:140])
                values += [str(truncated), str(truncated)]
        engine.save_json(request, dict(Version=1, AssetsDirectory=str(assets), PopplerDirectory=poppler, Documents=values))
        script = """
import importlib.abc,sys
class Block(importlib.abc.MetaPathFinder):
    def find_spec(self,fullname,path=None,target=None):
        if fullname.split('.')[0] in {'torch','visual_brain','document_engine','document_model','cache_paths','resnet_document_model'}:
            raise AssertionError('Unexpected tensor/model/cache import')
sys.meta_path.insert(0,Block())
sys.path.insert(0,sys.argv[1])
from pathlib import Path
import infer_procedure,json
calls=[]
native=infer_procedure.native
def counted(*args,**kwargs):
    calls.append(args[0][0])
    return native(*args,**kwargs)
infer_procedure.native=counted
infer_procedure.run(Path(sys.argv[2]))
print(json.dumps({'native_calls':len(calls)}))
"""
        runtime = str(Path(engine.__file__).parent)
        result = subprocess.run([sys.executable, "-B", "-c", script, runtime, str(request)], capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout.splitlines()[-1])["native_calls"], expected_native_calls)
        rows = json.loads((self.root / "result.json").read_text())["Rows"]
        self.assertEqual([row["Path"] for row in rows], values)
        self.assertTrue(all(row.get("PdfUnreadable") is True and row["NeedsReview"] is False
                            and row["Predicted"] == "" and row["Confidence"] == 0 and row["Pages"] == 0
                            and row["Error"].startswith("PDF non leggibile: ") for row in rows))

    def test_real_poppler_truncated_pdf_is_rejected_before_rendering(self):
        marker = Path(os.environ.get("LOCALAPPDATA", "")) / "DrosoDocLab/tools/poppler/installed.txt"
        if not marker.exists(): self.skipTest("Managed Poppler is unavailable")
        poppler = Path(marker.read_text(encoding="utf-8-sig").strip())
        if not (poppler / "pdfinfo.exe").is_file(): self.skipTest("Managed Poppler executable is unavailable")
        self.original.write_bytes(synthetic_pdf()[:140])
        with patch.object(engine, "native", wraps=engine.native) as native:
            with self.assertRaises(engine.UnreadablePdfError):
                engine.prepare(self.original, engine.sha256(self.original), self.cache, poppler, lambda: None)
        self.assertEqual(native.call_count, 1)
        self.assertEqual(Path(native.call_args.args[0][0]).name, "pdfinfo.exe")


class MixedBatchTests(unittest.TestCase):
    def test_invalid_documents_never_reach_model_and_duplicates_are_prepared_once(self):
        # Reuse the existing tiny CPU model and authentication-free inference fixture.
        import test_classification as fixtures
        import cache_paths
        import document_engine
        import torch
        fixture = fixtures.ClassificationTests()
        fixture.setUpClass(); fixture.setUp()
        try:
            # The legacy mocked classification fixture did not need a real PDF
            # header. This test exercises preparation, so supply the exact magic.
            for index, (path, _) in enumerate(fixture.originals):
                path.write_bytes(b"%PDF-1.4 synthetic " + str(index).encode())
            fixture.originals = [(path, engine.sha256(path)) for path, _ in fixture.originals]
            bad = fixture.root / "unreadable.pdf"; bad.write_bytes(b"not a PDF")
            originals = [fixture.originals[0][0], bad, fixture.originals[1][0], bad]
            request = json.loads(fixture.request.read_text()); request["Documents"] = [str(path) for path in originals]
            engine.save_json(fixture.request, request)
            def render(command, *args):
                if Path(command[0]).name == "pdfinfo.exe": return "Pages: 2\n"
                for number in (1, 2): Image.new("RGB", (40, 30), (number * 40, 70, 100)).save(command[-1] + f"-{number}.ppm")
                return ""
            with (patch.object(torch.cuda, "is_available", return_value=False), patch.object(torch, "load", return_value={}),
                  patch.object(document_engine, "model_from_checkpoint", return_value=(fixture.model, fixture.config)),
                  patch.object(cache_paths, "response_cache_directory", return_value=fixture.root / "cache"),
                  patch.object(engine, "native", side_effect=render) as native,
                  patch.object(engine, "prepare", wraps=engine.prepare) as prepare,
                  patch.object(document_engine, "predictions", wraps=document_engine.predictions) as predict,
                  contextlib.redirect_stdout(io.StringIO())):
                engine.run(fixture.request)
            self.assertEqual((prepare.call_count, native.call_count, predict.call_count), (3, 4, 1))
            classified = predict.call_args.args[1]
            self.assertEqual({document["sha256"] for document in classified}, {digest for _, digest in fixture.originals})
            rows = json.loads((fixture.root / "result.json").read_text())["Rows"]
            self.assertEqual([row["Path"] for row in rows], [str(path) for path in originals])
            self.assertTrue(all(rows[index]["PdfUnreadable"] and not rows[index]["NeedsReview"] for index in (1, 3)))
            self.assertTrue(all(rows[index]["Predicted"] and rows[index]["Pages"] == 2 and rows[index]["Error"] == "" for index in (0, 2)))
        finally:
            fixture.tearDown()


if __name__ == "__main__": unittest.main(verbosity=2)
