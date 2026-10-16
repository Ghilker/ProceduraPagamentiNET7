"""Exercise Procedure's inference entry point with real page inputs and a tiny CPU model."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
from threading import Event, current_thread
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "DrosoAssets" / "scripts"))
import numpy as np
from PIL import Image
import torch

import cache_paths
import document_engine
import document_model
import infer_procedure as engine
from document_model import DocumentBrain, Responses, page_views, prepare_document


class ClassificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        torch.set_num_threads(1)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        assets = self.root / "assets"
        (assets / "model").mkdir(parents=True)
        (assets / "model/best.pt").write_bytes(b"test checkpoint")
        self.assets = assets
        self.policy = dict(Checkpoint=engine.sha256(assets / "model/best.pt"), InputMode="visual_only",
                           Thresholds=dict(PERMESSO_SOGGIORNO=.75, RICEVUTA=.75, PASSAPORTO=.75, ERRATO=.90))
        engine.save_json(assets / "model/model.json", dict(version=1, engine_version=3, use_ocr=False,
                         labels=list(document_model.DOCUMENT_LABELS), supported_labels=list(document_model.DOCUMENT_LABELS)))
        self.save_policy()
        self.originals, self.prepared = [], {}
        y, x = np.indices((120, 96))
        for index, count in enumerate((2, 3)):
            pdf = self.root / f"document-{index}.pdf"
            pdf.write_bytes(b"%PDF synthetic " + str(index).encode())
            digest = engine.sha256(pdf)
            self.originals.append((pdf, digest))
            self.prepared[digest] = (pdf, count)
            pages = Path(str(pdf) + ".pages")
            pages.mkdir()
            entries = []
            for number in range(count):
                path = pages / f"page-{number + 1}.png"
                pixels = ((3 * x + (number + 1) * y + 57 * index) % 256).astype(np.uint8)
                Image.fromarray(pixels).save(path)
                entries.append(dict(File=path.name, Sha256=engine.sha256(path)))
            engine.save_json(Path(str(pdf) + ".pages.json"), dict(Version=1, PdfSha256=digest, Images=entries))
        self.documents = [prepare_document(dict(path=str(pdf))) for pdf, _ in self.originals]
        self.request = self.root / "request.json"
        engine.save_json(self.request, dict(Version=1, AssetsDirectory=str(assets), PopplerDirectory="unused",
                                           Documents=[str(pdf) for pdf, _ in self.originals]))
        adjacency = torch.sparse_coo_tensor(torch.tensor([[1, 2, 3], [0, 1, 2]]),
                                            torch.tensor([.8, -.6, .7]), (9, 9)).coalesce().to_sparse_csr()
        graph = dict(neurons=9, adjacency=adjacency, input_indices=torch.tensor([0, 2]),
                     grid=torch.tensor([[-.4, .2], [.5, -.6]]), fingerprint="classification-test")
        torch.manual_seed(42)
        self.model = DocumentBrain(graph, 3, True, engine_version=3)
        self.config = dict(graph_fingerprint=graph["fingerprint"], steps=3, seed=42)
        self.responses = []

    def tearDown(self):
        self.temporary.cleanup()

    def save_policy(self):
        path = self.assets / "model/decision-visual-only.json"
        engine.save_json(path, self.policy)
        engine.save_json(self.assets / "manifest.json", dict(Version=1, CheckpointSha256=self.policy["Checkpoint"],
                         DecisionPolicySha256=engine.sha256(path)))

    def run_inference(self, cache_error=None):
        def create(*args, **kwargs):
            response = Responses(*args, **kwargs)
            self.responses.append(response)
            return response

        with (patch.object(torch.cuda, "is_available", return_value=False),
              patch.object(torch, "load", return_value={}),
              patch.object(document_engine, "model_from_checkpoint", return_value=(self.model, self.config)),
              patch.object(cache_paths, "response_cache_directory", return_value=self.root / "cache", side_effect=cache_error),
              patch.object(engine, "prepare_all", return_value=(self.originals, self.prepared, {})),
              patch.object(document_model, "Responses", side_effect=create),
              contextlib.redirect_stdout(io.StringIO())):
            engine.run(self.request)

    def test_pages_are_prepared_ahead_and_multipage_predictions_are_unchanged(self):
        response = Responses(self.model, self.config, self.root / "cache", "cpu")
        with contextlib.redirect_stdout(io.StringIO()):
            expected = document_engine.predictions(self.model, self.documents, "cpu", lambda: None, response)
        second_ready = Event()
        prepared = []
        workers = []

        def prepare(*args, **kwargs):
            views = page_views(*args, **kwargs)
            prepared.append(str(args[0]))
            workers.append(current_thread())
            if str(args[0]) == self.documents[0]["images"][1]:
                second_ready.set()
            return views

        encode = self.model.encode
        first = True

        def encode_while_preparing(views):
            nonlocal first
            if first:
                first = False
                self.assertTrue(second_ready.wait(5), "Next page was not prepared during inference")
                self.assertLessEqual(len(prepared), 3, "More than two pages prepared ahead")
            return encode(views)

        with (patch.object(document_model, "page_views", side_effect=prepare),
              patch.object(self.model, "encode", side_effect=encode_while_preparing)):
            self.run_inference()
        self.assertEqual(prepared, [image for doc in self.documents for image in doc["images"]])
        self.assertTrue(all(worker is not current_thread() and not worker.is_alive() for worker in workers))
        self.assertIsNone(self.responses[0]._prefetch)
        actual = json.loads((self.root / "result.json").read_text())["Rows"]
        self.assertEqual(len(actual), 2)
        for row, reference, (pdf, pages) in zip(actual, expected["rows"], self.prepared.values()):
            self.assertEqual(row, dict(Path=str(pdf), Sha256=reference["sha256"],
                                      Predicted=reference["predicted"], Confidence=reference["confidence"],
                                      Margin=reference["margin"], NeedsReview=reference["review"], Pages=pages, Error=""))

    def test_bundled_policy_controls_review_instead_of_fixed_threshold_or_blanket_errato(self):
        self.policy["Thresholds"]["RICEVUTA"] = .50
        self.save_policy()
        rows = [dict(sha256=digest, predicted=label, confidence=confidence, margin=.25,
                     supported_label=True, decision_version=3, review=True)
                for (_, digest), label, confidence in zip(self.originals, ("RICEVUTA", "ERRATO"), (.60, .95))]
        with patch.object(document_engine, "predictions", return_value=dict(rows=rows)):
            self.run_inference()
        result = json.loads((self.root / "result.json").read_text())["Rows"]
        self.assertEqual([row["NeedsReview"] for row in result], [False, False])

    def test_learned_visual_model_never_initializes_the_response_cache(self):
        self.model.learned_visual_input = True
        rows = [dict(sha256=digest, predicted="RICEVUTA", confidence=.8, margin=.3,
                     supported_label=True, decision_version=4, review=True) for _, digest in self.originals]
        with patch.object(document_engine, "predictions", return_value=dict(rows=rows)):
            self.run_inference(AssertionError("Learned RGB inference must not touch the D: response cache"))
        self.assertIsNone(self.responses[0].directory)
        self.assertTrue((self.root / "result.json").exists())

    def test_missing_policy_for_new_model_fails_before_preparing_pdf_or_loading_model(self):
        engine.save_json(self.assets / "model/model.json", dict(version=1, engine_version=4,
                         learned_visual_input=True, labels=list(document_model.DOCUMENT_LABELS)))
        engine.save_json(self.assets / "manifest.json", dict(Version=1, CheckpointSha256=self.policy["Checkpoint"]))
        with patch.object(engine, "prepare_all", side_effect=AssertionError("No preparation before policy verification")):
            with self.assertRaisesRegex(ValueError, "Politica"):
                engine.run(self.request)
        self.assertFalse((self.root / "result.json").exists())

    def test_policy_modified_during_inference_does_not_publish_results(self):
        def changed(*args, **kwargs):
            path = self.assets / "model/decision-visual-only.json"
            path.write_text(path.read_text() + " ")
            return dict(rows=[dict(sha256=digest, predicted="RICEVUTA", confidence=.8, margin=.3,
                                  supported_label=True, decision_version=3, review=False) for _, digest in self.originals])
        with patch.object(document_engine, "predictions", side_effect=changed):
            with self.assertRaisesRegex(ValueError, "Politica.*modificata"):
                self.run_inference()
        self.assertFalse((self.root / "result.json").exists())

    def test_cancellation_joins_worker_and_does_not_publish_partial_results(self):
        workers = []

        def cancel_during_preparation(*args, **kwargs):
            workers.append(current_thread())
            views = page_views(*args, **kwargs)
            (self.root / "cancel.request").touch()
            return views

        with patch.object(document_model, "page_views", side_effect=cancel_during_preparation):
            with self.assertRaises(InterruptedError):
                self.run_inference()
        self.assertTrue(workers)
        self.assertTrue(all(not worker.is_alive() for worker in workers))
        self.assertIsNone(self.responses[0]._prefetch)
        self.assertFalse((self.root / "result.json").exists())

    def test_failed_page_joins_worker_and_does_not_publish_partial_results(self):
        workers = []

        def fail_on_second_page(*args, **kwargs):
            workers.append(current_thread())
            if str(args[0]) == self.documents[0]["images"][1]:
                raise OSError("unreadable page")
            return page_views(*args, **kwargs)

        with patch.object(document_model, "page_views", side_effect=fail_on_second_page):
            with self.assertRaisesRegex(OSError, "unreadable page"):
                self.run_inference()
        self.assertTrue(workers)
        self.assertTrue(all(not worker.is_alive() for worker in workers))
        self.assertIsNone(self.responses[0]._prefetch)
        self.assertFalse((self.root / "result.json").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
