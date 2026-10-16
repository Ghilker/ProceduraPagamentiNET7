"""Visual-connectome training, evaluation and inference, without SQL writes."""
import csv
from contextlib import nullcontext
import hashlib
import json
import math
from pathlib import Path
import time
from concurrent.futures import ThreadPoolExecutor, wait, FIRST_COMPLETED
from threading import Event

import torch
from torch.nn import functional as F
from visual_telemetry import VisualTelemetry
from file_verification import VerificationCache
from cache_paths import response_cache_directory
from training_recipe import (NEW_TRAINING_RECIPE, make_training_optimizer, set_training_phase,
                             training_epoch_documents, training_example_weight)

from visual_brain import (LABELS, VisualBrain, emit, ensure_disjoint, load_pages,
                          save_json, save_torch, sha256)
from document_model import (ENGINE_VERSION, INPUT_VERSION, LEGACY_INPUT_VERSION, DOCUMENT_LABELS,
                            canonical_label, DocumentBrain, Responses, input_version,
                            apply_training_reviews, input_fingerprint, prepare_document, review_identities)


def prepare_documents(rows, check_cancel=lambda: None, *, verifier=None, stage="training", workers=4):
    """Bound disk concurrency and preserve manifest order, including on warm-cache runs."""
    if workers < 1:
        raise ValueError("Serve almeno un lettore per la verifica")
    rows = list(rows)
    documents = [None] * len(rows)
    stopped = Event()
    started, last_progress = time.monotonic(), 0.
    initial_hits = getattr(verifier, "hits", 0)
    initial_misses = getattr(verifier, "misses", 0)
    completed = 0

    def worker_check():
        if stopped.is_set():
            raise InterruptedError("Verifica interrotta")
        check_cancel()

    def progress(force=False):
        nonlocal last_progress
        now = time.monotonic()
        if force or now - last_progress >= .2:
            emit("Verifica PDF e immagini · " + stage, verification=True,
                 current=completed, total=len(rows),
                 verification_reused=getattr(verifier, "hits", 0) - initial_hits,
                 verification_hashed=getattr(verifier, "misses", 0) - initial_misses,
                 verification_elapsed_seconds=round(now - started, 2))
            last_progress = now

    check_cancel()
    progress(True)
    if not rows:
        return documents
    executor = ThreadPoolExecutor(max_workers=min(workers, len(rows)), thread_name_prefix="verify-pdf")
    pending, next_index = {}, 0
    try:
        while completed < len(rows):
            worker_check()
            while next_index < len(rows) and len(pending) < workers:
                future = executor.submit(prepare_document, rows[next_index], verifier=verifier,
                                         check_cancel=worker_check)
                pending[future] = next_index
                next_index += 1
            done, _ = wait(pending, timeout=.1, return_when=FIRST_COMPLETED)
            for future in done:
                documents[pending.pop(future)] = future.result()
                completed += 1
            progress(completed == len(rows))
        check_cancel()
        return documents
    finally:
        stopped.set()
        executor.shutdown(wait=True, cancel_futures=True)


def load_documents(path, check_cancel=lambda: None, engine_version=ENGINE_VERSION,
                   *, verifier=None, stage="training", workers=4):
    path = Path(path).resolve()
    with path.open(encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.DictReader(stream))
    if not rows:
        raise ValueError("Manifest vuoto")
    seen = set()
    for row in rows:
        check_cancel()
        row["label"] = canonical_label(row.get("label", ""))
        if engine_version < 3 and row["label"] == "RICEVUTA":
            row["label"] = "RICEVUTA_PERMESSO"
        labels = DOCUMENT_LABELS if engine_version >= 3 else LABELS
        if row["label"] not in labels or not row.get("group_id") or not row.get("sha256"):
            raise ValueError("Categoria, gruppo o hash assente dal manifest")
        row["path"] = str(path.parent / row["path"])
        digest = row["sha256"].upper()
        if digest in seen:
            raise ValueError("PDF duplicato nel manifest")
        seen.add(digest)
    return prepare_documents(rows, check_cancel, verifier=verifier, stage=stage, workers=workers)


def load_rejected(path, training, holdout, check_cancel=lambda: None, identities=(),
                  *, verifier=None, workers=4):
    """04 is a complementary label, never a positive ERRATO training target."""
    if not path:
        return [], 0
    emit("Selezione respinti 04", verification=True, current=0, total=0)
    path = Path(path).resolve()
    with path.open(encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.DictReader(stream))
    protected_hashes = {d["sha256"].upper() for d in holdout}
    protected_groups = {d["group_id"] for d in holdout}
    positive_hashes = {d["sha256"].upper() for d in training}
    for row in rows:
        check_cancel()
        if (row.get("status") != "04" or row.get("label") or not row.get("group_id")
                or not row.get("sha256") or canonical_label(row.get("declared_label", "")) not in DOCUMENT_LABELS[:3]):
            raise ValueError("Respinti: servono stato 04, sezione, gruppo e hash; nessuna categoria positiva")
    # Protect transitive student/hash links before opening any rejected PDF/images.
    changed = True
    while changed:
        check_cancel()
        before = (len(protected_hashes), len(protected_groups))
        for row in [*rows, *identities]:
            if row["sha256"].upper() in protected_hashes or row.get("group_id") in protected_groups:
                protected_hashes.add(row["sha256"].upper())
                if row.get("group_id"):
                    protected_groups.add(row["group_id"])
        changed = before != (len(protected_hashes), len(protected_groups))
    if any(d["group_id"] in protected_groups or d["sha256"].upper() in protected_hashes for d in training):
        raise ValueError("Contaminazione: i respinti collegano studenti di training e validazione/test; ricreare le partizioni")
    declarations = {}
    for row in rows:
        declarations.setdefault(row["sha256"].upper(), set()).add(canonical_label(row["declared_label"]))
    rejected, seen = [], set()
    for row in rows:
        check_cancel()
        digest = row["sha256"].upper()
        if (digest in protected_hashes or row["group_id"] in protected_groups or digest in positive_hashes
                or digest in seen or len(declarations[digest]) != 1):
            continue
        row["path"] = str(path.parent / row["path"])
        row["declared_label"] = canonical_label(row["declared_label"])
        rejected.append(row); seen.add(digest)
    documents = prepare_documents(rejected, check_cancel, verifier=verifier, stage="respinti 04", workers=workers)
    return documents, len(rows)-len(documents)


def supported_logits(logits, labels, supported):
    # An unobserved ERRATO class must not become an easy sink for weak negatives.
    mask = torch.tensor([label in supported for label in labels], device=logits.device)
    return logits.masked_fill(~mask, -10000.)


LEGACY_OBJECTIVE = "confidence-cost-complementary-v1"
BALANCED_OBJECTIVE = "confidence-cost-errato-balanced-v2"


def training_objective(previous, request):
    selected = (previous.get("objective_version", LEGACY_OBJECTIVE) if previous is not None
                else request.get("objective_version", BALANCED_OBJECTIVE))
    if selected not in (LEGACY_OBJECTIVE, BALANCED_OBJECTIVE):
        raise ValueError("Obiettivo di training non supportato")
    if previous is not None and request.get("objective_version", selected) != selected:
        raise ValueError("Obiettivo cambiato: scegliere una nuova cartella modello")
    return selected


def classification_loss(logits, target, class_weight=1., objective_version=LEGACY_OBJECTIVE):
    """Strong bounded error costs; correct evidence is rewarded by lower log loss."""
    probabilities = logits.softmax(0)
    correct = probabilities[target]
    wrong = probabilities.masked_fill(torch.arange(len(logits), device=logits.device) == target, -1).max()
    loss = F.cross_entropy(logits[None], torch.tensor([target], device=logits.device))
    loss = loss * (1 + 2 * (1-correct).square()) + .5 * F.relu(.25-correct+wrong)
    if len(logits) == 4 and target != 3:
        loss = loss + 2 * probabilities[3].square()  # False rejection is especially costly.
    elif len(logits) == 4 and target == 3 and objective_version == BALANCED_OBJECTIVE:
        # Mirror the valid-to-ERRATO cost: penalize total probability assigned
        # to any valid type for a confirmed ERRATO, without inventing a type.
        loss = loss + 2 * (1 - probabilities[3]).square()
    return loss * class_weight


def rejection_loss(logits, declared):
    # Stable -log(1-p(declared)); gives no invented target among remaining classes.
    alternatives = logits.masked_fill(torch.arange(len(logits), device=logits.device) == declared, -torch.inf)
    return .25 * (torch.logsumexp(logits, 0) - torch.logsumexp(alternatives, 0))


def backward_training_batch(model, responses, documents, variants, weak_examples,
                            class_weights, difficult, config, augmentation_seed=None):
    """One shared forward/backward; retain the per-document objective and reporting order."""
    labels = model.labels
    predictions = responses.predict_batch(documents, variants=variants, augmentation_seed=augmentation_seed)
    losses, logits_batch = [], []
    regularization = (config["plasticity_regularization"] * model.regularization_loss()
                      if config["engine_version"] >= 3 else None)
    for document, weak, (logits, _) in zip(documents, weak_examples, predictions):
        logits = supported_logits(logits, labels, model.supported_labels)
        if weak:
            loss = rejection_loss(logits, labels.index(document["declared_label"]))
        else:
            target = labels.index(document["label"])
            weight = training_example_weight(document, difficult, config)
            loss = (classification_loss(logits, target, class_weights[target], config["objective_version"])
                    if config["engine_version"] >= 3 else
                    F.cross_entropy(logits[None], torch.tensor([target], device=logits.device)) * class_weights[target]) * weight
        losses.append(loss + regularization if regularization is not None else loss)
        logits_batch.append(logits)
    losses = torch.stack(losses)
    # Loss and class scores share a single device-to-host copy, including the finite
    # check. Reporting does not force a separate synchronization for each PDF.
    report = torch.cat((losses.detach()[:, None], torch.stack(logits_batch).detach().softmax(1)), dim=1).cpu().tolist()
    if any(not math.isfinite(row[0]) for row in report):
        raise ValueError("Loss non finita")
    losses.mean().backward()
    return [(row[0], row[1:]) for row in report]


def classification_metrics(rows, labels):
    matrix = [[0 for _ in labels] for _ in labels]
    for row in rows:
        matrix[labels.index(row["truth"])][labels.index(row["predicted"])] += 1
    classes = []
    for i, label in enumerate(labels):
        support = sum(matrix[i]); predicted = sum(row[i] for row in matrix)
        precision, recall = matrix[i][i]/max(1, predicted), matrix[i][i]/max(1, support)
        classes.append(dict(label=label, support=support, precision=precision, recall=recall,
                            f1=2*precision*recall/max(1e-12, precision+recall)))
    measured = [c for c in classes if c["support"] or any(r["predicted"] == c["label"] for r in rows)]
    reference_classes = [c for c in classes if c["support"]]
    accepted = [r for r in rows if not r["review"]]
    result = dict(confusion_matrix=matrix, matrix_orientation="rows=reference, columns=prediction", classes=classes,
                macro_f1=sum(c["f1"] for c in measured)/max(1, len(measured)),
                reference_macro_f1=sum(c["f1"] for c in reference_classes)/max(1, len(reference_classes)),
                unobserved_predictions=sum(r["predicted"] not in {c["label"] for c in reference_classes} for r in rows),
                office_label_agreement=sum(r["truth"] == r["predicted"] for r in rows)/max(1, len(rows)),
                coverage=len(accepted)/max(1, len(rows)),
                accuracy_on_accepted=sum(r["truth"] == r["predicted"] for r in accepted)/len(accepted) if accepted else None)
    if "ERRATO" in labels:
        index = labels.index("ERRATO")
        reference = sum(matrix[index])
        other_reference = len(rows) - reference
        false_positives = {label: matrix[i][index] for i, label in enumerate(labels) if i != index}
        false_count = sum(false_positives.values())
        result["errato"] = dict(reference_documents=reference, detected=matrix[index][index],
                               missed=reference-matrix[index][index], false_positives=false_count,
                               other_reference_documents=other_reference,
                               false_positive_rate=false_count/other_reference if other_reference else None,
                               false_positives_by_reference=false_positives)
    return result


def require_visual_model(metadata):
    # Old v2 models without a flag used the removed text branch. Never silently convert them.
    if metadata.get("use_ocr", metadata.get("engine_version", 1) >= 2):
        raise ValueError("Modello misto con OCR non supportato: addestrare un modello solo visivo in una nuova cartella")


def model_from_checkpoint(directory, graph, device):
    directory = Path(directory)
    metadata = json.loads((directory / "model.json").read_text(encoding="utf-8-sig"))
    require_visual_model(metadata)
    if metadata.get("architecture") == "resnet18":
        from resnet_document_model import model_from_checkpoint as load_resnet
        return load_resnet(directory, metadata, device)
    if metadata.get("architecture") == "visual-control":
        from visual_control_model import model_from_checkpoint as load_control
        return load_control(directory, metadata, device)
    if metadata.get("architecture", "droso") != "droso":
        raise ValueError("Architettura del modello non supportata")
    if metadata.get("model_family"):
        raise ValueError("Il gruppo di quattro modelli richiede la classificazione con conferma specialistica")
    version = metadata.get("engine_version", 1)
    if metadata.get("version") != 1 or version not in (1, 2, 3, ENGINE_VERSION) or metadata["graph_fingerprint"] != graph["fingerprint"]:
        raise ValueError("Versione o connettoma incompatibile con il modello")
    learned_visual = bool(metadata.get("learned_visual_input", False))
    if learned_visual and version != ENGINE_VERSION:
        raise ValueError("L'input visivo appreso richiede il modello Droso V4")
    if version >= 2 and metadata.get("input_version") != input_version(version, metadata.get("color_features", False), learned_visual):
        raise ValueError("Pre-elaborazione incompatibile: addestrare il modello in una nuova cartella")
    member_labels = None
    if metadata.get("member_role"):
        from document_ensemble import member_labels as labels_for_member
        member_labels = labels_for_member(metadata)
        if metadata.get("labels") != member_labels:
            raise ValueError("Categorie dello specialista o del generalista non coerenti")
    model = (DocumentBrain(graph, metadata["steps"], metadata["adaptive"], engine_version=version, labels=member_labels,
                           color_features=metadata.get("color_features", False), taste_adaptive=metadata.get("taste_adaptive"),
                           learned_visual_input=learned_visual, visual_encoder_source=metadata.get("encoder_provenance"))
             if version >= 2 else VisualBrain(graph, metadata["steps"], metadata["adaptive"])).to(device)
    if learned_visual and metadata.get("visual_input") != model.visual_input_metadata:
        raise ValueError("Encoder o adattatore visivo incompatibile con il checkpoint")
    if version >= 4 and not learned_visual and metadata.get("gustatory") != model.gustatory_metadata:
        raise ValueError("Mappa o codifica gustativa incompatibile con il checkpoint V4")
    state = torch.load(directory / "best.pt", map_location=device, weights_only=True)
    if state["signature"] != metadata["signature"]:
        raise ValueError("Checkpoint e metadati non corrispondono")
    model.load_state_dict(state["model"]); model.eval()
    model.supported_labels = metadata.get("supported_labels", getattr(model, "labels", LABELS))
    return model, metadata


def model_responses(model, config, cache, device, check_cancel, telemetry):
    if config.get("architecture") == "resnet18":
        from resnet_document_model import ResNetResponses
        return ResNetResponses(model, config, cache, device, check_cancel, telemetry)
    return Responses(model, config, cache, device, check_cancel,
                     None if config.get("architecture") == "visual-control" else telemetry)


def model_telemetry(config, graph, request):
    if config.get("architecture") in ("resnet18", "visual-control"):
        from resnet_document_model import ModelTelemetry
        return ModelTelemetry()
    return VisualTelemetry(graph.get("ids"), config["steps"], detail_file=request.get("activity_detail_file"))


def predictions(model, documents, device, check_cancel, responses=None, threshold=.75, split="inference"):
    probabilities, evidence, raw_logits = [], [], []
    labels = getattr(model, "labels", LABELS)
    version = getattr(model, "engine_version", 1)
    supported = getattr(model, "supported_labels", labels)
    is_resnet = getattr(model, "architecture", None) == "resnet18"
    started = time.perf_counter()
    model.eval()
    with torch.no_grad():
        for i, document in enumerate(documents):
            check_cancel()
            if responses:
                logits, attention = responses.predict(document)
                scores = supported_logits(logits, labels, supported).softmax(0).cpu().tolist()
                weights = attention.cpu().tolist()
                top = max(range(len(weights)), key=weights.__getitem__)
                views = model.view_names
                evidence.append(None if getattr(model, "architecture", None) == "resnet18" else
                                dict(page=top//len(views)+1, view=views[top%len(views)],
                                     weight=weights[top], meaning="Peso interno di aggregazione, non una spiegazione causale"))
            else:
                pages, counts = load_pages([document], device)
                batch_logits = model(pages, counts)
                logits = batch_logits[0]
                scores = batch_logits.softmax(1)[0].cpu().tolist()
                evidence.append(None)
            if not is_resnet:
                values = logits.detach().cpu().tolist()
                original = {canonical_label(label): value for label, value in zip(labels, values) if label in supported}
                if not all(math.isfinite(value) for value in original.values()):
                    raise ValueError("Logit non finito: classificazione arrestata")
                raw_logits.append(original)
            else:
                raw_logits.append(None)
            probabilities.append(scores)
            emit("Classificazione" if split=="inference" else "Valutazione " + split, current=i+1, total=len(documents),
                 predicted=labels[max(range(len(scores)), key=scores.__getitem__)], confidence=max(scores))
    rows = []
    for doc, scores, focus, original in zip(documents, probabilities, evidence, raw_logits):
        index = max(range(len(scores)), key=scores.__getitem__)
        ordered = sorted(scores, reverse=True)
        margin = ordered[0]-ordered[1]
        reason = ("ambiguous_margin" if version >= 3 and margin < .15 else
                  "errato_requires_validation_policy" if labels[index] == "ERRATO" else
                  "low_confidence" if scores[index] < threshold else "")
        rows.append(dict(path=doc["path"], sha256=doc["sha256"], group_id=doc.get("group_id", ""), split=split,
                         truth=doc.get("label", ""), predicted=labels[index], confidence=scores[index],
                         margin=margin, scores=dict(zip(labels, scores)), review=bool(reason),
                         decision_version=version, review_reason=reason, supported_label=labels[index] in supported,
                         decision="DA_VERIFICARE" if reason else canonical_label(labels[index]),
                         evidence=focus, corrected_label=""))
        if not is_resnet:
            # Retain finite pre-softmax evidence even when float probabilities
            # saturate to zero. Temperature is fitted/applied by decision policy.
            rows[-1]["raw_logits"] = original
        if getattr(model, "architecture", None) == "resnet18":
            rows[-1]["evidence_text"] = f"Media uniforme di tutte le {len(doc['images'])} pagine, con viste pagina intera e contenuto"
    result = dict(rows=rows, total=len(rows), threshold=threshold, seconds=time.perf_counter()-started,
                  input_mode="visual_only", labels=labels, decision_version=version,
                  calibration="Punteggi non calibrati: soglia da scegliere sulla validazione")
    if getattr(model, "architecture", None) == "resnet18":
        result["architecture"] = "resnet18"
    elif getattr(model, "architecture", None) == "visual-control":
        result["architecture"] = "visual-control"
    if split != "inference":
        result = classification_metrics(rows, labels) | result
        result["difficult_subset"] = dict(total=sum(r["predicted"]!=r["truth"] or r["review"] for r in rows),
                                         definition="Errori e punteggi sotto soglia: coda di revisione, non benchmark indipendente")
    result["seconds_per_document"] = result["seconds"] / max(1, len(rows))
    return result


def load_activity_history(directory, selection_metric, completed=1000):
    history = []
    for path in sorted(Path(directory).glob("validation-[0-9][0-9][0-9].json"))[-40:]:
        try:
            report = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            continue  # Optional display history must not prevent inference or a checkpoint resume.
        if 0 < report.get("epoch", 0) <= completed and "training_loss" in report and selection_metric in report:
            history.append(dict(epoch=report["epoch"], loss=report["training_loss"], validation_f1=report[selection_metric]))
    return history


def write_report(directory, name, metrics):
    output = Path(directory) / (name + ".json")
    save_json(output, metrics)
    with open(output.with_suffix(".csv"), "w", newline="", encoding="utf-8-sig") as stream:
        columns = ["path", "sha256", "group_id", "split", "truth", "predicted", "confidence", "margin", "review", "decision", "review_reason"]
        if metrics.get("model_family"):
            columns += ["generalist_confidence", "specialist_label", "specialist_confidence", "specialist_margin",
                        "specialist_confirmed", "confirmation_required", "pages"]
        writer = csv.DictWriter(stream, columns, extrasaction="ignore"); writer.writeheader()
        for row in metrics["rows"]:
            # Text cells remain inert when opened in spreadsheet applications.
            writer.writerow({k: "'"+v if isinstance(v, str) and v.startswith(("=", "+", "-", "@")) else v for k, v in row.items()})
    emit("Report disponibile", report=str(output))
    return output


def run(request, graph, device, check_cancel):
    if request.get("use_ocr", False):
        raise ValueError("OCR rimosso: è disponibile solo il percorso visivo")
    from document_ensemble import handles, run as run_ensemble
    if handles(request):
        return run_ensemble(request, graph, device, check_cancel)
    model_directory = Path(request.get("model_directory", request["output_directory"]))
    with VerificationCache(model_directory / "verification-v1.sqlite3", check_cancel) as verifier:
        result = _run(request, graph, device, check_cancel, verifier)
        result.update(verification_cache_hits=verifier.hits, verification_cache_misses=verifier.misses,
                      verification_bytes_read=verifier.bytes_read)
        return result


def _run(request, graph, device, check_cancel, verifier):
    from audited_inputs import validate_audited_inputs
    audited = validate_audited_inputs(request)
    directory = Path(request["output_directory"])
    model_directory = Path(request.get("model_directory", directory))
    threshold = float(request.get("threshold", .75))
    if not 0 <= threshold <= 1:
        raise ValueError("Soglia fuori intervallo")
    action = request["action"]
    saved_metadata = model_directory / "model.json"
    cache_definition = json.loads(saved_metadata.read_text(encoding="utf-8-sig")) if saved_metadata.exists() else request
    cache_architecture = cache_definition.get("architecture", "droso")
    # CNN features change during fine tuning and have no frozen-brain cache.
    cache = None if cache_architecture in ("resnet18", "visual-control") or cache_definition.get("learned_visual_input", False) else response_cache_directory()
    if cache is not None:
        emit(f"Cache risposte: {cache}", response_cache=str(cache))
    if action in ("predict", "test", "evaluate"):
        model, config = model_from_checkpoint(model_directory, graph, device)
        if audited:
            validate_audited_inputs(request, config)
        split = "inference" if action=="predict" else "test" if action=="test" else "validation"
        if action == "predict":
            documents = prepare_documents([dict(path=path) for path in request["documents"]], check_cancel,
                                          verifier=verifier, stage="classificazione")
        else:
            documents = load_documents(request["test"] if action=="test" else request["validation"], check_cancel,
                                       config.get("engine_version", 1), verifier=verifier,
                                       stage="test" if action == "test" else "validazione")
            if action == "test":
                ensure_disjoint(config["used_documents"], documents)
            if config.get("encoder_provenance"):
                from visual_encoder_source import validate_encoder_holdouts
                validate_encoder_holdouts(config["encoder_provenance"], documents if action == "evaluate" else [],
                                          documents if action == "test" else [], check_cancel)
            if config.get("member_role"):
                from document_ensemble import member_documents
                documents = member_documents(documents, config)
            if action != "test" and not audited and config.get("engine_version", 1) >= 2 and input_fingerprint(documents) != config["validation_inputs"]:
                raise ValueError("Validazione diversa da quella del modello")
        responses = model_responses(model, config, cache, device, check_cancel,
                                    model_telemetry(config, graph, request)) if isinstance(model, DocumentBrain) or config.get("architecture") in ("resnet18", "visual-control") else None
        if responses:
            history = load_activity_history(model_directory, config.get("selection_metric", "macro_f1"))
            if responses.on_activity is not None:
                responses.on_activity.set_metrics(dict(phase="prediction" if action=="predict" else split, history=history,
                    epoch=history[-1]["epoch"] if history else 0, epochs=int(request.get("epochs", 0))))
            emit(f"Elaborazione {device}: fino a {responses.view_batch_size} viste per gruppo")
        result = predictions(model, documents, device, check_cancel, responses, threshold, split)
        result.update(model_directory=str(model_directory.resolve()), checkpoint_sha256=sha256(model_directory / "best.pt"),
                      timing_scope=("Classificazione ResNet18 di tutte le pagine; preparazione PDF e caricamento modello esclusi"
                                    if config.get("architecture") == "resnet18" else
                                    "Classificazione del controllo visivo senza connettoma; preparazione PDF e caricamento modello esclusi"
                                    if config.get("architecture") == "visual-control" else
                                    "Classificazione Droso con input visivo appreso, senza cache risposte; preparazione PDF e caricamento modello esclusi"
                                    if config.get("learned_visual_input", False) else
                                    "Classificazione incluse risposte dalla cache; preparazione PDF e caricamento modello esclusi"))
        write_report(directory, "predictions" if action=="predict" else split+"-"+time.strftime("%Y%m%d-%H%M%S"), result)
        return dict(documents=len(documents), response_view_batch_size=responses.view_batch_size if responses else 1,
                    response_cache_hits=responses.hits if responses else 0,
                    response_cache_misses=responses.misses if responses else 0)

    previous = None
    if (directory / "model.json").exists():
        previous = json.loads((directory / "model.json").read_text(encoding="utf-8-sig"))
        require_visual_model(previous)
    architecture = previous.get("architecture", "droso") if previous else request.get("architecture", "droso")
    is_resnet = architecture == "resnet18"
    is_control = architecture == "visual-control"
    learned_visual = is_control or not is_resnet and bool(previous.get("learned_visual_input", False) if previous else request.get("learned_visual_input", False))
    encoder_source_kind = (previous.get("visual_encoder_source", "imagenet") if previous else request.get("visual_encoder_source", "imagenet")) if learned_visual else "imagenet"
    if learned_visual and encoder_source_kind not in ("imagenet", "resnet18-checkpoint"):
        raise ValueError("Origine dell'encoder visivo non supportata")
    if architecture not in ("droso", "resnet18", "visual-control"):
        raise ValueError("Architettura del modello non supportata")
    if previous and request.get("architecture", architecture) != architecture:
        raise ValueError("Architettura cambiata: scegliere una nuova cartella modello")
    if (is_resnet or is_control) and (request.get("model_family") or request.get("member_role")):
        raise ValueError("Questo motore usa un modello singolo, senza specialisti Droso")
    version = previous.get("engine_version", 1) if previous else int(request.get("engine_version", ENGINE_VERSION))
    if is_resnet or is_control:
        if previous is not None and version != ENGINE_VERSION:
            raise ValueError("Versione del controllo visivo non supportata" if is_control else "Versione ResNet18 non supportata")
        version = ENGINE_VERSION
    if version not in (2, 3, ENGINE_VERSION):
        raise ValueError("Per il nuovo training scegliere una nuova cartella modello")
    if version < 3 and request.get("rejected_training"):
        raise ValueError("I respinti richiedono il motore v3: scegliere una nuova cartella modello")
    labels = DOCUMENT_LABELS if version >= 3 else LABELS
    training = load_documents(request["training"], check_cancel, version, verifier=verifier, stage="training")
    validation = load_documents(request["validation"], check_cancel, version, verifier=verifier, stage="validazione")
    ensure_disjoint(training, validation)
    protected_test = []
    if request.get("training_reviews") or request.get("rejected_training") or request.get("member_role") or encoder_source_kind == "resnet18-checkpoint":
        if not request.get("test"):
            raise ValueError("Manifest test necessario per proteggere gli studenti dalle revisioni")
        # Read identities only; test images, predictions and scores are never used for training.
        with open(request["test"], encoding="utf-8-sig", newline="") as stream:
            protected_test = list(csv.DictReader(stream))
        if any(not row.get("sha256") or not row.get("group_id") for row in protected_test):
            raise ValueError("Identità del test mancanti")
        ensure_disjoint(training + validation, protected_test)
    if request.get("training_reviews"):
        emit("Selezione revisioni", verification=True, current=0, total=0)
    training = apply_training_reviews(training, validation, request.get("training_reviews"), protected_test, engine_version=version,
        prepare_many=lambda rows: prepare_documents(rows, check_cancel, verifier=verifier, stage="revisioni"))
    if any(not set(labels[:3]).issubset({d["label"] for d in dataset}) for dataset in (training, validation)):
        raise ValueError("Training e validazione devono contenere tutte e tre le categorie")
    if request.get("member_role"):
        from document_ensemble import member_documents, member_labels
        labels = member_labels(request)
        training = member_documents(training, request)
        validation = member_documents(validation, request)
        if request.get("rejected_training"):
            raise ValueError("I quattro modelli richiedono categorie note, non lo stato di rifiuto 04")
    rejected, rejected_excluded = load_rejected(request.get("rejected_training"), training, validation+protected_test, check_cancel,
                                               review_identities(request.get("training_reviews")), verifier=verifier)
    encoder_provenance, visual_backbone = None, None
    if learned_visual and encoder_source_kind == "resnet18-checkpoint":
        from visual_encoder_source import load_checkpoint_encoder, validate_encoder_holdouts
        if previous is not None:
            encoder_provenance = previous.get("encoder_provenance")
            if not encoder_provenance:
                raise ValueError("Provenienza dell'encoder assente dal checkpoint: scegliere una nuova cartella modello")
        else:
            if (directory / "last.pt").exists():
                raise ValueError("Metadati dell'encoder assenti: scegliere una nuova cartella modello")
            visual_backbone, encoder_provenance = load_checkpoint_encoder(request, check_cancel)
        validate_encoder_holdouts(encoder_provenance, validation, protected_test, check_cancel)
    seed, steps, epochs = int(request.get("seed", 12345)), int(request.get("steps", 12)), int(request.get("epochs", 20))
    if not 1 <= steps <= 100 or not 1 <= epochs <= 1000:
        raise ValueError("Passi o epoche fuori intervallo")
    color_features = not is_resnet and bool(previous.get("color_features", False) if learned_visual and previous
                                          else request.get("color_features", learned_visual))
    if color_features and version < 4:
        raise ValueError("Le caratteristiche a colori richiedono V4")
    config = dict(version=1, engine_version=version, seed=seed,
                  training=sha256(request["training"]), validation=sha256(request["validation"]),
                  training_inputs=input_fingerprint(training), validation_inputs=input_fingerprint(validation),
                  augmentation=bool(request.get("augmentation", True)), hard_examples=bool(request.get("hard_examples", True)),
                  batch_size=2, learning_rate=.001)
    if is_resnet:
        from resnet_document_model import INPUT_VERSION as RESNET_INPUT_VERSION, PRETRAINING
        config.update(architecture="resnet18", input_version=RESNET_INPUT_VERSION, pretraining=PRETRAINING,
                      adaptive=False, backbone_learning_rate=.0001)
    elif is_control:
        from learned_visual_input import visual_metadata
        if not color_features or bool(previous.get("adaptive", False) if previous else request.get("adaptive", False)) or bool(previous.get("taste_adaptive", False) if previous else request.get("taste_adaptive", False)):
            raise ValueError("Il controllo visivo richiede immagini a colori, encoder fisso e nessun training di neuroni/gusto")
        config.update(architecture="visual-control", learned_visual_input=True, color_features=True,
                      adaptive=False, taste_adaptive=False, representation_size=64,
                      input_version=input_version(version, True, True),
                      visual_input=visual_metadata(64, encoder_source=encoder_provenance))
    else:
        config.update(input_version=input_version(version, color_features), steps=steps,
                      adaptive=bool(previous["adaptive"] if learned_visual and previous else request.get("adaptive", False if learned_visual else version >= 3)),
                      graph_fingerprint=graph["fingerprint"])
        if learned_visual:
            taste_learning = bool(previous.get("taste_adaptive", False) if previous else request.get("taste_adaptive", False))
            if version != 4 or not color_features or config["adaptive"] or taste_learning:
                raise ValueError("Input visivo appreso: scegliere Droso V4, attivare i colori e disattivare training di neuroni/gusto, in una nuova cartella")
            from learned_visual_input import visual_metadata
            config.update(learned_visual_input=True, visual_input=visual_metadata(len(graph["input_indices"]), encoder_source=encoder_provenance),
                          input_version=input_version(version, color_features, True), color_features=True, taste_adaptive=False)
        elif previous is not None:
            if "prudent_adaptation" in previous:
                config["prudent_adaptation"] = bool(previous["prudent_adaptation"])
        elif request.get("prudent_adaptation", False):
            if version < 3:
                raise ValueError("Adattamento prudente disponibile per i nuovi modelli Droso V3/V4")
            config["prudent_adaptation"] = True
    if encoder_provenance is not None:
        config.update(visual_encoder_source="resnet18-checkpoint", encoder_provenance=encoder_provenance,
                      visual_encoder_model=previous.get("visual_encoder_model", encoder_provenance["path"]) if previous else str(Path(request["visual_encoder_model"]).resolve()),
                      visual_encoder_epoch=encoder_provenance["epoch"],
                      visual_encoder_checkpoint_sha256=encoder_provenance["checkpoint_sha256"])
    if version >= 4 and not is_resnet and not learned_visual:
        from gustatory import taste_metadata, FEATURE_VERSION, COLOR_FEATURE_VERSION
        if color_features:
            config["color_features"] = True
        if previous is None:
            config["taste_adaptive"] = bool(request.get("taste_adaptive", False))
        elif "taste_adaptive" in previous:
            config["taste_adaptive"] = bool(previous["taste_adaptive"])
        # Historical adaptive V4 models omitted this flag. Leave it absent so
        # their signature, encoder behavior and optimizer state remain intact.
        if request.get("feature_cache"):
            config["feature_cache"] = str(Path(request["feature_cache"]).resolve())
        config["gustatory"] = taste_metadata(graph, COLOR_FEATURE_VERSION if color_features else FEATURE_VERSION)
    if request.get("model_name"):
        config["model_name"] = str(request["model_name"])
    # Retain the format flag to reject old mixed models and resume visual-only checkpoints.
    config["use_ocr"] = False
    # Existing checkpoints keep their selection rule and optimizer history.
    if previous is None or "selection_metric" in previous:
        config["selection_metric"] = previous.get("selection_metric", "reference_macro_f1") if previous else "reference_macro_f1"
    if request.get("member_role"):
        config.update(member_role=request["member_role"], specialist_label=request.get("specialist_label", ""), labels=labels)
    if version >= 3:
        config.update(supported_labels=[label for label in labels if any(d["label"] == label for d in training)],
                      objective_version=training_objective(previous, request), rejection_weight=.25, plasticity_regularization=.0001,
                      rejected_inputs=hashlib.sha256(json.dumps([
                          {key: d[key] for key in ("sha256", "group_id", "image_hashes", "declared_label", "status")}
                          for d in rejected], sort_keys=True).encode()).hexdigest())
        if previous is None or "training_recipe" in previous:
            config["training_recipe"] = dict(previous["training_recipe"] if previous else NEW_TRAINING_RECIPE)
            # Even confirming an unchanged label changes human-aware weighting.
            config["supervision_inputs"] = hashlib.sha256(json.dumps([
                (d["sha256"], d.get("label_source", "office")) for d in training],
                sort_keys=True).encode()).hexdigest()
    signature = hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()
    torch.manual_seed(seed)
    if is_resnet:
        from resnet_document_model import ResNetDocumentModel, ensure_pretrained_weights, pretrained_cache
        last_path = directory / "last.pt"
        if previous is not None and not last_path.exists():
            raise ValueError("Checkpoint di ripresa ResNet18 assente: scegliere una nuova cartella modello")
        if last_path.exists():
            state = torch.load(last_path, map_location="cpu", weights_only=True)
            if state["signature"] != signature:
                raise ValueError("Ingressi, revisioni o parametri cambiati: scegliere una nuova cartella modello")
            model = ResNetDocumentModel().to(device)
        else:
            check_cancel()
            model = ResNetDocumentModel(ensure_pretrained_weights(pretrained_cache(request), check_cancel)).to(device)
    else:
        visual_state = None
        if learned_visual:
            from resnet_document_model import ensure_pretrained_weights, pretrained_cache
            resume_path = directory / "last.pt"
            if previous is not None and not resume_path.exists():
                kind = "del controllo visivo" if is_control else "Droso con input appreso"
                raise ValueError("Checkpoint di ripresa " + kind + " assente: scegliere una nuova cartella modello")
            if resume_path.exists():
                saved = torch.load(resume_path, map_location="cpu", weights_only=True)
                if saved["signature"] != signature:
                    raise ValueError("Ingressi o parametri cambiati: scegliere una nuova cartella modello")
            elif encoder_source_kind == "imagenet":
                visual_state = ensure_pretrained_weights(pretrained_cache(request), check_cancel)
        if is_control:
            from visual_control_model import VisualControlModel
            model = VisualControlModel(provenance=encoder_provenance, pretrained_state=visual_state,
                                       backbone_state=visual_backbone).to(device)
        else:
            model = DocumentBrain(graph, steps, config["adaptive"], engine_version=version,
                                  labels=labels if request.get("member_role") else None,
                                  color_features=color_features, taste_adaptive=config.get("taste_adaptive"),
                                  learned_visual_input=learned_visual, visual_pretrained_state=visual_state,
                                  visual_backbone_state=visual_backbone, visual_encoder_source=encoder_provenance).to(device)
    model.supported_labels = config.get("supported_labels", labels)
    telemetry = model_telemetry(config, graph, request)
    responses = model_responses(model, config, cache, device, check_cancel, telemetry)
    emit(f"Elaborazione {device}: viste raggruppate per minibatch, memoria gestita automaticamente")
    optimizer, scheduler = make_training_optimizer(model, config)
    start, best, stale, difficult = 0, -1., 0, set()
    last_path = directory / "last.pt"
    if last_path.exists():
        state = torch.load(last_path, map_location=device, weights_only=True)
        if state["signature"] != signature:
            raise ValueError("Ingressi, revisioni o parametri cambiati: scegliere una nuova cartella modello; i PDF restano riutilizzabili")
        model.load_state_dict(state["model"]); optimizer.load_state_dict(state["optimizer"])
        if scheduler is not None:
            if "scheduler" not in state:
                raise ValueError("Stato della velocità di apprendimento assente dal checkpoint")
            scheduler.load_state_dict(state["scheduler"])
        start, best, stale = state["epoch"], state["best_f1"], state["stale"]
        difficult = set(state["difficult"])
        emit("Ripresa training del modello", completed_epochs=start)
    activity_history = load_activity_history(directory, config.get("selection_metric", "macro_f1"), start)
    used = [{k: d.get(k, "office") for k in ("sha256", "group_id", "label_source")} for d in training + validation + rejected]
    label_counts = {label: sum(d["label"] == label for d in training) for label in labels}
    reviewed_counts = {label: sum(d["label"] == label and d.get("label_source") == "human-reviewed-training"
                                 for d in training) for label in labels}
    capacity = dict(trainable_parameters=sum(parameter.numel() for parameter in model.parameters() if parameter.requires_grad),
                    total_parameters=sum(parameter.numel() for parameter in model.parameters())) if learned_visual else {}
    save_json(directory / "model.json", config | dict(signature=signature, labels=labels, used_documents=used,
              training_count=len(training), validation_count=len(validation), rejected_count=len(rejected),
              training_reviews=request.get("training_reviews", ""), training_label_counts=label_counts,
              reviewed_training_counts=reviewed_counts,
              rejected_excluded=rejected_excluded, plasticity=getattr(model, "plasticity_metadata", {}),
              limitations=("ResNet18 pretrained on ImageNet; whole-page and content RGB views from every page, mean document pooling. Classifies document type, without OCR or an accuracy guarantee."
                           if is_resnet else "Frozen ResNet18 RGB features, a compact learned adapter and document attention/classifier, without connectome or neuron activity. No OCR or accuracy guarantee."
                           if is_control else "Frozen ResNet18 RGB features and a learned visual receptor adapter; fixed anatomical graph and neuron parameters. No gustatory drive or OCR. No accuracy guarantee."
                           if learned_visual else "Anatomical graph retained; bounded engineering plasticity. V4 gustatory drive encodes image features, not biological taste or reward. No accuracy guarantee.")) | capacity)
    if is_resnet:
        emit("ResNet18 · classificatore documenti da pesi ImageNet", architecture="resnet18", pretraining=config["pretraining"])
    elif learned_visual:
        emit(("Controllo visivo senza connettoma" if is_control else "Droso con input visivo appreso")
             + " · encoder fisso · adattatore e classificatore addestrabili",
             learned_visual_input=True, visual_input=config["visual_input"])
    elif version >= 4:
        emit("V4 · input visivo e gustativo attivo · "
             + ("encoder gustativo addestrabile" if model.taste_adaptive else "encoder gustativo fisso"),
             gustatory=config["gustatory"], taste_adaptive=model.taste_adaptive,
             gustatory_input_enabled=True)
    emit("Training pronto: " + ", ".join(f"{label} {count}" for label, count in label_counts.items())
         + f" · {sum(reviewed_counts.values())} revisioni umane incluse",
         training_label_counts=label_counts, reviewed_training_counts=reviewed_counts,
         adaptive=config["adaptive"], training_recipe=config.get("training_recipe"),
         objective_version=config.get("objective_version"))
    if config.get("training_recipe"):
        if not is_resnet and config.get("prudent_adaptation", False):
            adapting_neurons = any(group["name"] == "brain" for group in optimizer.param_groups)
            emit("Adattamento prudente · " + ("velocità iniziale dei neuroni dimezzata" if adapting_neurons else "neuroni fissi")
                 + " · classificatore invariato", prudent_adaptation=True,
                 learning_rates=[group["lr"] for group in optimizer.param_groups],
                 optimizer_groups=[group["name"] for group in optimizer.param_groups])
        emit("ERRATO confermati: al massimo "
             + str(config["training_recipe"]["reviewed_errato_max_repeats"])
             + " presentazioni per documento in ogni epoca; gli altri esempi originali restano inclusi")
    if config.get("objective_version") == BALANCED_OBJECTIVE:
        emit("Costo ERRATO bilanciato: penalizzo anche gli ERRATI scambiati per documenti validi",
             objective_version=BALANCED_OBJECTIVE)
    counts = torch.tensor([sum(d["label"]==label for d in training) for label in labels], device=device)
    class_weights = (len(training)/(int((counts>0).sum())*counts.clamp_min(1))).clamp(.5, 2.)
    for epoch in range(start, epochs):
        if stale >= 5:
            break
        check_cancel(); model.train()
        phase = set_training_phase(model, config, epoch)
        telemetry.set_metrics(dict(epoch=epoch+1, epochs=epochs, phase=phase, history=activity_history[-40:]))
        learning_rates = [group["lr"] for group in optimizer.param_groups]
        emit("Assestamento del classificatore" if phase == "classifier-warmup" else
             ("Apprendimento adattatore visivo e classificatore · encoder CNN fisso" if is_control else
              "Apprendimento adattatore visivo e classificatore · neuroni ed encoder CNN fissi") if phase == "visual-adapter-and-classifier" else
             "Apprendimento classificatore ed encoder gustativo · altri neuroni fissi" if phase == "classifier-and-taste" else
             "Apprendimento classificatore e neuroni · encoder gustativo fisso" if phase == "adaptive" and version >= 4 and not model.taste_adaptive else
             "Apprendimento classificatore, neuroni ed encoder gustativo" if phase == "adaptive" and version >= 4 else "Apprendimento " + phase,
             epoch=epoch+1, training_phase=phase, learning_rates=learning_rates)
        epoch_documents, order = training_epoch_documents(training, rejected, config, epoch)
        loss_sum, next_difficult, train_rows = 0., set(), {}
        training_correct = training_labeled = 0
        correct_evidence = 0.
        epoch_started = time.perf_counter()
        modern_adaptive = bool(config.get("training_recipe") and model.dynamic_responses)
        augmentation_seed = seed + epoch if modern_adaptive and config["augmentation"] else None
        plan = ((epoch_documents[index], (epoch+index)%2 if config["augmentation"] else 0, augmentation_seed)
                for index in order)
        with responses.prefetch(plan) if modern_adaptive else nullcontext():
            for offset in range(0, len(order), config["batch_size"]):
                check_cancel(); optimizer.zero_grad(set_to_none=True)
                indices = order[offset:offset+config["batch_size"]]
                batch_documents = [epoch_documents[index] for index in indices]
                variants = [(epoch+index)%2 if config["augmentation"] else 0 for index in indices]
                batch_results = backward_training_batch(model, responses, batch_documents, variants,
                    [index >= len(training) for index in indices], class_weights, difficult, config,
                    augmentation_seed=augmentation_seed)
                for index, (loss_value, scores) in zip(indices, batch_results):
                    document = epoch_documents[index]
                    weak = index >= len(training)
                    if not weak:
                        target = labels.index(document["label"])
                        training_labeled += 1
                    loss_sum += loss_value
                    predicted = labels[max(range(len(labels)), key=scores.__getitem__)]
                    training_correct += int(not weak and predicted == document["label"])
                    if not weak:
                        correct_evidence += scores[target]
                        if predicted != document["label"]:
                            next_difficult.add(document["sha256"])
                    presentations = train_rows.get(document["sha256"], {}).get("training_presentations", 0) + 1
                    train_rows[document["sha256"]] = dict(path=document["path"], sha256=document["sha256"], group_id=document["group_id"],
                                           split="training", truth=document.get("label", ""), predicted=predicted, confidence=max(scores),
                                           review=weak or predicted!=document["label"], margin=sorted(scores)[-1]-sorted(scores)[-2],
                                           declared_label=document.get("declared_label", ""), status=document.get("status", "05"),
                                           label_source=document.get("label_source", "office"), training_presentations=presentations,
                                           decision_version=version, supported_label=predicted in model.supported_labels,
                                           review_reason="rejected_without_reason" if weak else "", corrected_label="")
                torch.nn.utils.clip_grad_norm_(model.parameters(), 1., error_if_nonfinite=True)
                report_update = offset % 20 == 0 or offset+len(indices) == len(order)
                observed_weights = {"classifier_update": model.readout.weight}
                if hasattr(model, "taste_encoder"):
                    observed_weights["taste_update"] = model.taste_encoder.weight
                before_update = {name: parameter.detach().clone() for name, parameter in observed_weights.items()} if report_update else {}
                optimizer.step()
                if report_update:
                    updates = dict(zip(observed_weights, torch.stack([
                        (parameter.detach()-before_update[name]).norm()
                        for name, parameter in observed_weights.items()]).cpu().tolist()))
                    telemetry.set_metrics(dict(epoch=epoch+1, epochs=epochs, phase=phase, history=activity_history[-40:],
                        loss=loss_sum/(offset+len(indices)), **updates))
                    emit("Training del modello", epoch=epoch+1, current=offset+len(indices), total=len(order),
                         loss=loss_sum/(offset+len(indices)), cache_hits=responses.hits, cache_misses=responses.misses,
                         epochs=epochs, training_correct=training_correct, training_labeled=training_labeled,
                         training_phase=phase, learning_rates=learning_rates,
                         correct_evidence=correct_evidence/max(1, training_labeled), weak_examples=min(len(rejected), len(training)),
                         predicted=predicted, confidence=max(scores),
                         feature_cache_hits=responses.feature_cache.hits if responses.feature_cache else 0,
                         feature_cache_misses=responses.feature_cache.misses if responses.feature_cache else 0)
        telemetry.set_metrics(dict(epoch=epoch+1, epochs=epochs, phase="validation", loss=loss_sum/len(order), history=activity_history[-40:]))
        metrics = predictions(model, validation, device, check_cancel, responses, threshold, "validation")
        metrics.update(epoch=epoch+1, training_loss=loss_sum/len(order), epoch_seconds=time.perf_counter()-epoch_started,
                       training_phase=phase, learning_rates=learning_rates, training_presentations=len(order),
                       training_unique_documents=len(epoch_documents),
                       training_correct_evidence=correct_evidence/max(1, training_labeled), rejected_used=min(len(rejected), len(training)))
        write_report(directory, f"validation-{epoch+1:03}", metrics)
        if "errato" in metrics:
            rejection = metrics["errato"]
            emit(f"Validazione · ERRATO rilevati {rejection['detected']}/{rejection['reference_documents']}"
                 f" · altre categorie scambiate per ERRATO {rejection['false_positives']}/{rejection['other_reference_documents']}",
                 epoch=epoch+1, errato=rejection)
        write_report(directory, "training-review", dict(rows=list(train_rows.values()), epoch=epoch+1,
                     note="Predizioni durante il training, anche su varianti: usare per revisione, non come misura di generalizzazione"))
        score = metrics[config.get("selection_metric", "macro_f1")]
        activity_history.append(dict(epoch=epoch+1, loss=metrics["training_loss"], validation_f1=score))
        activity_history = activity_history[-40:]
        telemetry.set_metrics(dict(epoch=epoch+1, epochs=epochs, phase="validation", loss=metrics["training_loss"],
            validation_f1=score, history=activity_history))
        if score > best:
            best, stale = score, 0
            save_torch(directory / "best.pt", dict(model=model.state_dict(), epoch=epoch+1, signature=signature))
            save_json(directory / "best-validation.json", metrics)
        else:
            stale += 1
        difficult = next_difficult
        if scheduler is not None:
            scheduler.step(score)
            if learning_rates != [group["lr"] for group in optimizer.param_groups]:
                emit("Velocità di apprendimento ridotta dopo la validazione", epoch=epoch+1,
                     learning_rates=[group["lr"] for group in optimizer.param_groups])
        state = dict(model=model.state_dict(), optimizer=optimizer.state_dict(), epoch=epoch+1, signature=signature,
                     best_f1=best, stale=stale, difficult=sorted(difficult))
        if scheduler is not None:
            state["scheduler"] = scheduler.state_dict()
        save_torch(last_path, state)
        emit("Validazione epoca", epoch=epoch+1, agreement=metrics["office_label_agreement"], macro_f1=score,
             best_macro_f1=best)
    # Evaluate the selected visual checkpoint, not the last-epoch weights.
    selected, selected_config = model_from_checkpoint(directory, graph, device)
    selected_responses = model_responses(selected, selected_config, cache, device, check_cancel, telemetry)
    selected_metrics = predictions(selected, validation, device, check_cancel, selected_responses, threshold, "validation")
    selected_metrics["selection"] = "Visual model selected by validation " + config.get("selection_metric", "macro_f1") + "; test remains unused"
    write_report(directory, "best-validation", selected_metrics)
    emit("Training completato", model=str(directory/"best.pt"), best_macro_f1=best, test_used=False)
    return dict(training_documents=len(training), validation_documents=len(validation),
                rejected_documents=len(rejected), rejected_excluded=rejected_excluded,
                response_view_batch_size=responses.view_batch_size,
                response_cache_hits=responses.hits+selected_responses.hits, response_cache_misses=responses.misses+selected_responses.misses,
                cache_bytes=sum(p.stat().st_size for p in cache.glob("*.pt")) if cache is not None and cache.exists() else 0)
