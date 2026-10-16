"""Apply the bundled, checkpoint-bound policy used by Droso's review service."""
import hashlib
import json
import math
from pathlib import Path

def sha256(path):
    with open(path, "rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest().upper()


LABELS = ("PERMESSO_SOGGIORNO", "RICEVUTA", "PASSAPORTO", "ERRATO")
MINIMUM_MARGIN = .15


def canonical(label):
    return "RICEVUTA" if label == "RICEVUTA_PERMESSO" else label


def probability(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) and 0 <= value <= 1


def supported_metadata(metadata):
    version = metadata.get("engine_version", 1)
    if (metadata.get("version") != 1 or type(version) is not int or version not in (1, 2, 3, 4)
            or metadata.get("architecture", "droso") != "droso" or metadata.get("use_ocr", False)
            or metadata.get("input_mode", "visual_only") != "visual_only"):
        raise ValueError("Versione o architettura del modello integrato non supportata: è richiesto Droso solo visivo V1–V4.")
    if metadata.get("model_family") or metadata.get("member_role"):
        raise ValueError("Il modello integrato deve essere singolo; i gruppi di modelli specialistici non sono supportati.")
    if version == 4 and not metadata.get("learned_visual_input", False):
        raise ValueError("Questo pacchetto Droso V4 richiede l'encoder visivo RGB integrato.")
    labels = [canonical(label) for label in metadata.get("labels", LABELS[:3])]
    supported = [canonical(label) for label in metadata.get("supported_labels", labels)]
    if (not labels or len(labels) != len(set(labels)) or any(label not in LABELS for label in labels)
            or not supported or len(supported) != len(set(supported)) or any(label not in labels for label in supported)):
        raise ValueError("Categorie del modello integrato non valide.")
    return supported


def validate_temperature(calibration, checkpoint=None, supported=None):
    def digest(value):
        return isinstance(value, str) and len(value) == 64 and all(c in "0123456789abcdefABCDEF" for c in value)

    labels = calibration.get("ActiveLabels")
    before, after = calibration.get("NllBefore"), calibration.get("NllAfter")
    temperature = calibration.get("Temperature")
    if (calibration.get("Version") != 1 or calibration.get("Method") != "temperature-scaling-v1"
            or not isinstance(temperature, (int, float)) or not math.isfinite(temperature) or not .05 <= temperature <= 20
            or not digest(calibration.get("Checkpoint")) or not digest(calibration.get("ValidationReportSha256"))
            or calibration.get("InputMode") != "visual_only" or not isinstance(labels, list) or len(labels) < 2
            or len(labels) != len(set(labels)) or any(label not in LABELS for label in labels)
            or not isinstance(before, (int, float)) or not isinstance(after, (int, float))
            or not math.isfinite(before) or not math.isfinite(after) or before < 0 or after < 0 or after > before + 1e-12
            or calibration.get("ExampleCount", 0) < 20 or calibration.get("ExcludedUnsupportedTruthCount", -1) < 0
            or checkpoint is not None and calibration.get("Checkpoint") != checkpoint
            or supported is not None and labels != supported):
        raise ValueError("Calibrazione della fiducia non valida o incompatibile con il modello integrato.")


def load_policy(assets, checkpoint):
    assets = Path(assets)
    manifest = json.loads((assets / "manifest.json").read_text(encoding="utf-8-sig"))
    path = assets / "model/decision-visual-only.json"
    if manifest.get("Version") != 1 or manifest.get("CheckpointSha256") != checkpoint:
        raise ValueError("Politica di decisione o checkpoint integrato non corrispondente al manifest.")
    metadata = json.loads((assets / "model/model.json").read_text(encoding="utf-8-sig"))
    supported = supported_metadata(metadata)
    if not manifest.get("DecisionPolicySha256") and not metadata.get("learned_visual_input", False):
        return None, None  # Previous V3 bundles retain the fixed .75 threshold and ERRATO review.
    if not manifest.get("DecisionPolicySha256") or not path.is_file() or manifest["DecisionPolicySha256"] != sha256(path):
        raise ValueError("Politica di decisione integrata assente o non corrispondente al manifest.")
    policy = json.loads(path.read_text(encoding="utf-8-sig"))
    if policy.get("Checkpoint") != checkpoint or policy.get("InputMode") != "visual_only":
        raise ValueError("Politica di decisione non corrispondente al checkpoint integrato.")
    thresholds = {canonical(label): value for label, value in policy.get("Thresholds", {}).items()}
    thresholds.setdefault("ERRATO", None)
    if any(label not in thresholds for label in LABELS) or any(value is not None and not probability(value) for value in thresholds.values()):
        raise ValueError("Soglie della politica integrata non valide.")
    policy["Thresholds"] = thresholds
    if policy.get("ConfidenceCalibration") is not None:
        validate_temperature(policy["ConfidenceCalibration"], checkpoint, supported)
    return policy, manifest["DecisionPolicySha256"]


def apply_temperature(rows, calibration):
    if calibration is not None:
        validate_temperature(calibration)
        labels = calibration["ActiveLabels"]
        for row in rows:
            if row.get("error", ""):
                continue
            logits = {canonical(label): value for label, value in (row.get("raw_logits") or {}).items()}
            if (row.get("confirmation_required", False) or set(logits) != set(labels)
                    or any(not isinstance(value, (int, float)) or not math.isfinite(value) for value in logits.values())
                    or canonical(row.get("predicted", "")) not in logits
                    or logits[canonical(row["predicted"])] != max(logits.values())):
                raise ValueError("Logits originali o categorie non validi per la calibrazione della fiducia.")
    for row in rows:
        if row.get("raw_confidence") is not None:
            row["confidence"], row["margin"] = row["raw_confidence"], row.get("raw_margin")
            if row.get("raw_scores") is not None:
                row["scores"] = dict(row["raw_scores"])
        if calibration is None or row.get("error", ""):
            continue
        if row.get("raw_confidence") is None:
            row["raw_confidence"] = row["confidence"]
        if row.get("raw_margin") is None:
            row["raw_margin"] = row.get("margin")
        if row.get("raw_scores") is None:
            row["raw_scores"] = dict(row.get("scores", {}))
        logits = {canonical(label): value for label, value in row["raw_logits"].items()}
        maximum = max(logits.values())
        values = [math.exp((logits[label] - maximum) / calibration["Temperature"]) for label in labels]
        total = sum(values)
        values = [value / total for value in values]
        row["scores"] = {label: 0. for label in LABELS} | dict(zip(labels, values))
        ordered = sorted(values, reverse=True)
        row["confidence"], row["margin"] = ordered[0], ordered[0] - ordered[1]


def confirmation_failure(row):
    if not row.get("confirmation_required", False):
        return ""
    general, specialist, margin = (row.get(key) for key in ("generalist_confidence", "specialist_confidence", "specialist_margin"))
    if (row["predicted"] not in LABELS[:3] or canonical(row.get("specialist_label", "")) != row["predicted"]
            or row.get("pages", 0) < 1 or not all(probability(value) for value in (general, specialist, margin))
            or abs(row["confidence"] - min(general, specialist)) > 1e-9):
        return "confirmation_missing"
    return "specialist_not_confirmed" if not row.get("specialist_confirmed", False) or specialist <= .5 or margin < MINIMUM_MARGIN else ""


def apply_decisions(rows, policy, *, threshold=.75, abstain=True):
    if not probability(threshold):
        raise ValueError("Soglia fra 0 e 1 richiesta.")
    calibration = policy.get("ConfidenceCalibration") if policy else None
    if calibration is not None:
        validate_temperature(calibration, policy["Checkpoint"])
    apply_temperature(rows, calibration)
    for row in rows:
        for name in ("predicted", "truth", "declared_label"):
            row[name] = canonical(row.get(name, ""))
        predicted = row["predicted"]
        required = policy["Thresholds"].get(predicted) if policy is not None else threshold
        margin = row.get("margin")
        ambiguous = row.get("decision_version", 0) >= 3 and (not probability(margin) or margin < MINIMUM_MARGIN)
        confirmation = confirmation_failure(row)
        reason = ("errore_elaborazione" if row.get("error", "") else
                  "categoria_non_valida" if predicted not in LABELS else
                  "punteggio_non_valido" if not probability(row.get("confidence")) else
                  "categoria_senza_esempi_supervisionati" if not row.get("supported_label", True) else
                  confirmation if confirmation else
                  "margine_insufficiente" if ambiguous else
                  "errato_senza_validazione_sufficiente" if predicted == "ERRATO" and (policy is None or required is None) else
                  "soglia_non_raggiunta" if (abstain or row.get("confirmation_required", False) or predicted == "ERRATO")
                  and (required is None or row["confidence"] < required) else "")
        row["review_reason"], row["review"] = reason, bool(reason)
        row["decision"] = canonical(row.get("corrected_label", "")) or ("DA_VERIFICARE" if reason else predicted)
