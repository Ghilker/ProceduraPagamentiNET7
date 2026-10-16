"""Policy boundaries and parity with the completed model's C# decision reports."""
import copy
import json
import math
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "DrosoAssets/scripts"))
from inference_decisions import LABELS, apply_decisions, load_policy, supported_metadata
from visual_brain import save_json, sha256


CHECKPOINT = "A" * 64


def policy():
    return dict(Checkpoint=CHECKPOINT, InputMode="visual_only", Thresholds=dict(
        PERMESSO_SOGGIORNO=.6029657125473022, RICEVUTA=.5125231742858887,
        PASSAPORTO=.46222713589668274, ERRATO=.8895891904830933))


def row(**changes):
    return dict(predicted="RICEVUTA", confidence=.60, margin=.25,
                decision_version=4, supported_label=True, review=True, review_reason="old") | changes


def calibration(**changes):
    return dict(Version=1, Method="temperature-scaling-v1", Temperature=2., Checkpoint=CHECKPOINT,
                InputMode="visual_only", ValidationReportSha256="B" * 64, ActiveLabels=list(LABELS),
                NllBefore=2., NllAfter=1.9, ExampleCount=20, ExcludedUnsupportedTruthCount=0) | changes


class DecisionTests(unittest.TestCase):
    def test_twenty_review_cases_match_csharp_priority_and_boundaries(self):
        cases = [
            (row(), ""),
            (row(confidence=policy()["Thresholds"]["RICEVUTA"]), ""),
            (row(confidence=.50), "soglia_non_raggiunta"),
            (row(predicted="ERRATO", confidence=.95), ""),
            (row(predicted="ERRATO", confidence=.88), "soglia_non_raggiunta"),
            (row(supported_label=False, confidence=.99), "categoria_senza_esempi_supervisionati"),
            (row(predicted="UNKNOWN"), "categoria_non_valida"),
            (row(confidence=float("nan")), "punteggio_non_valido"),
            (row(margin=None), "margine_insufficiente"),
            (row(margin=.15), ""),
            (row(margin=.149), "margine_insufficiente"),
            (row(margin=1.01), "margine_insufficiente"),
            (row(confidence=float("inf")), "punteggio_non_valido"),
            (row(error="unreadable", predicted="UNKNOWN"), "errore_elaborazione"),
            (row(supported_label=False, margin=None), "categoria_senza_esempi_supervisionati"),
            (row(decision_version=2, margin=None), ""),
            (row(confirmation_required=True), "confirmation_missing"),
            (row(predicted="RICEVUTA_PERMESSO"), ""),
            (row(predicted="PASSAPORTO", confidence=.50), ""),
            (row(predicted="PERMESSO_SOGGIORNO", confidence=.60), "soglia_non_raggiunta"),
        ]
        for index, (example, reason) in enumerate(cases):
            with self.subTest(case=index):
                apply_decisions([example], policy())
                self.assertEqual(example["review_reason"], reason)
                self.assertEqual(example["review"], bool(reason))
                self.assertEqual(example["decision"], "DA_VERIFICARE" if reason else example["predicted"])

    def test_errato_without_validated_threshold_always_requires_review(self):
        for current in (None, policy() | dict(Thresholds=policy()["Thresholds"] | dict(ERRATO=None))):
            example = row(predicted="ERRATO", confidence=1., margin=1.)
            apply_decisions([example], current, abstain=False)
            self.assertEqual(example["review_reason"], "errato_senza_validazione_sufficiente")

    def test_uncalibrated_scores_and_raw_logits_remain_exactly_unchanged(self):
        example = row(scores=dict(RICEVUTA=.60), raw_logits=dict(RICEVUTA=123.))
        original = copy.deepcopy(example)
        apply_decisions([example], policy())
        for name in ("confidence", "margin", "scores", "raw_logits"):
            self.assertEqual(example[name], original[name])

    def test_temperature_preserves_prediction_and_is_idempotent(self):
        example = row(predicted="PERMESSO_SOGGIORNO", confidence=.9, margin=.8,
                      raw_logits=dict(zip(LABELS, (2., 1., 0., -1.))))
        current = policy() | dict(ConfidenceCalibration=calibration())
        apply_decisions([example], current)
        expected = math.exp(1.) / sum(math.exp(value / 2.) for value in (2., 1., 0., -1.))
        self.assertAlmostEqual(example["confidence"], expected)
        self.assertEqual(example["predicted"], "PERMESSO_SOGGIORNO")
        self.assertEqual(example["raw_confidence"], .9)
        scores = dict(example["scores"])
        apply_decisions([example], current)
        self.assertEqual(example["scores"], scores)

    def test_invalid_logits_do_not_partially_transform_rows(self):
        valid = row(predicted="PERMESSO_SOGGIORNO", raw_logits=dict(zip(LABELS, (2., 1., 0., -1.))))
        for changes in (dict(raw_logits=None), dict(raw_logits=dict(RICEVUTA=1.)),
                        dict(raw_logits=dict(zip(LABELS, (2., float("nan"), 0., -1.)))),
                        dict(predicted="RICEVUTA"), dict(confirmation_required=True)):
            rows = [copy.deepcopy(valid), valid | changes]
            before = copy.deepcopy(rows[0])
            with self.subTest(changes=list(changes)), self.assertRaisesRegex(ValueError, "Logits"):
                apply_decisions(rows, policy() | dict(ConfidenceCalibration=calibration()))
            self.assertEqual(rows[0], before)

    def test_temperature_requires_matching_checkpoint_and_supported_labels(self):
        for changes in (dict(Checkpoint="C" * 64), dict(Temperature=0.), dict(Temperature=float("nan")),
                        dict(ActiveLabels=["RICEVUTA", "UNKNOWN"]), dict(NllAfter=3.)):
            with self.subTest(changes=list(changes)), self.assertRaisesRegex(ValueError, "Calibrazione"):
                apply_decisions([], policy() | dict(ConfidenceCalibration=calibration(**changes)))

    def test_unknown_model_versions_and_groups_are_rejected_clearly(self):
        base = dict(version=1, engine_version=4, learned_visual_input=True, labels=list(LABELS))
        self.assertEqual(supported_metadata(base), list(LABELS))
        for changes in (dict(engine_version=5), dict(architecture="resnet18"), dict(use_ocr=True),
                        dict(model_family="ensemble"), dict(member_role="specialist")):
            with self.subTest(changes=changes), self.assertRaisesRegex(ValueError, "non.*supportat"):
                supported_metadata(base | changes)


class PolicyIntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.assets = Path(self.temporary.name)
        (self.assets / "model").mkdir()
        self.path = self.assets / "model/decision-visual-only.json"
        self.current = policy()
        save_json(self.assets / "model/model.json", dict(version=1, engine_version=4,
                  learned_visual_input=True, labels=list(LABELS), supported_labels=list(LABELS)))
        self.save()

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        save_json(self.path, self.current)
        save_json(self.assets / "manifest.json", dict(Version=1, CheckpointSha256=CHECKPOINT,
                  DecisionPolicySha256=sha256(self.path)))

    def test_policy_file_change_is_rejected_even_when_thresholds_look_valid(self):
        self.path.write_text(self.path.read_text() + " ")
        with self.assertRaisesRegex(ValueError, "manifest"):
            load_policy(self.assets, CHECKPOINT)

    def test_checkpoint_mismatch_is_rejected_even_when_policy_hash_matches(self):
        self.current["Checkpoint"] = "D" * 64
        self.save()
        with self.assertRaisesRegex(ValueError, "checkpoint"):
            load_policy(self.assets, CHECKPOINT)

    def test_legacy_three_class_policy_cannot_authorize_errato(self):
        self.current["Thresholds"].pop("ERRATO")
        self.save()
        loaded, digest = load_policy(self.assets, CHECKPOINT)
        self.assertIsNone(loaded["Thresholds"]["ERRATO"])
        self.assertEqual(digest, sha256(self.path))

    def test_invalid_thresholds_are_rejected(self):
        for value in (-.01, 1.01, float("nan")):
            self.current["Thresholds"]["RICEVUTA"] = value
            self.save()
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, "Soglie"):
                load_policy(self.assets, CHECKPOINT)

    def test_old_v3_without_policy_preserves_fixed_threshold_and_errato_review(self):
        save_json(self.assets / "model/model.json", dict(version=1, engine_version=3, labels=list(LABELS)))
        save_json(self.assets / "manifest.json", dict(Version=1, CheckpointSha256=CHECKPOINT))
        self.path.unlink()
        loaded, digest = load_policy(self.assets, CHECKPOINT)
        self.assertEqual((loaded, digest), (None, None))
        examples = [row(confidence=.60), row(predicted="ERRATO", confidence=.99)]
        apply_decisions(examples, loaded)
        self.assertEqual([example["review_reason"] for example in examples],
                         ["soglia_non_raggiunta", "errato_senza_validazione_sufficiente"])

    def test_new_learned_v4_without_policy_is_rejected(self):
        save_json(self.assets / "manifest.json", dict(Version=1, CheckpointSha256=CHECKPOINT))
        self.path.unlink()
        with self.assertRaisesRegex(ValueError, "Politica"):
            load_policy(self.assets, CHECKPOINT)


class CompletedReportParityTests(unittest.TestCase):
    def test_final_validation_and_test_decisions_match_without_reading_documents(self):
        # Optional local integration evidence; portable boundary tests above use no D: data.
        source = Path(r"D:\DrosoDocLab\V1-20261013-085314-3355")
        reports = [source / "evaluations/20261013-085357-workflow-61e2de/validation/validation-20261014-064936-decisions.json",
                   source / "evaluations/20261013-085357-workflow-61e2de/test/test-20261014-070749-decisions.json"]
        if not all(path.exists() for path in reports):
            self.skipTest("Completed reference reports are unavailable on this machine")
        assets = Path(__file__).resolve().parents[2] / "DrosoAssets"
        manifest = json.loads((assets / "manifest.json").read_text())
        current, _ = load_policy(assets, manifest["CheckpointSha256"])
        for path in reports:
            reference = json.loads(path.read_text(encoding="utf-8-sig"))
            self.assertEqual(reference["checkpoint_sha256"], current["Checkpoint"])
            rows = copy.deepcopy(reference["rows"])
            apply_decisions(rows, current)
            mismatches = sum(any(a.get(key) != b.get(key) for key in
                             ("predicted", "confidence", "margin", "review", "review_reason", "decision"))
                             for a, b in zip(rows, reference["rows"]))
            self.assertEqual(mismatches, 0, "Decision parity failed; document details intentionally omitted")
            if path.parent.name == "test":
                accepted = [row for row in rows if not row["review"]]
                self.assertEqual((len(rows), len(accepted), len(rows) - len(accepted)), (3591, 3584, 7))
                self.assertEqual(sum(row["truth"] != row["predicted"] for row in accepted), 37)
                self.assertEqual(sum(row["predicted"] == "ERRATO" and row["truth"] != "ERRATO" for row in accepted), 0)
                self.assertEqual(sum(row["truth"] == "ERRATO" and row["predicted"] != "ERRATO" for row in accepted), 5)


if __name__ == "__main__":
    unittest.main(verbosity=2)
