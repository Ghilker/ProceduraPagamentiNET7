"""Experimental rate network on every edge of a frozen FAFB v783 export.

Column coordinates are anatomical; grayscale drive, neurotransmitter signs,
normalization and rate dynamics are modelling assumptions, not measured physiology.
"""
import array
import csv
import gzip
import hashlib
import json
import os
from pathlib import Path
import time
import urllib.request

import numpy as np
from PIL import Image, ImageOps
import torch
from torch import nn
from torch.nn import functional as F

VERSION = 1
LABELS = ["PERMESSO_SOGGIORNO", "RICEVUTA_PERMESSO", "PASSAPORTO"]
BASE_URL = "https://storage.googleapis.com/flywire-data/codex/data/fafb/783"
FILES = ["neurons.csv.gz", "connections_princeton_no_threshold.csv.gz", "column_assignment.csv.gz"]


def emit(stage, **values):
    print(json.dumps(dict(stage=stage, **values), ensure_ascii=False), flush=True)


def sha256(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def save_json(path, value):
    path = Path(path)
    temporary = Path(str(path) + ".tmp")
    temporary.write_text(json.dumps(value, indent=2), encoding="utf-8")
    os.replace(temporary, path)


def save_torch(path, value):
    torch.save(value, str(path) + ".tmp")
    os.replace(str(path) + ".tmp", path)


def rows(path):
    with gzip.open(path, "rt", encoding="utf-8-sig", newline="") as stream:
        yield from csv.DictReader(stream)


def download_brain(directory, check_cancel=lambda: None):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    provenance_path = directory / "sources.json"
    previous = json.loads(provenance_path.read_text()) if provenance_path.exists() else {}
    sources = {}
    for name in FILES:
        check_cancel()
        path = directory / name
        expected = previous.get(name, {}).get("sha256")
        if not path.exists() or expected is None or sha256(path) != expected:
            url = BASE_URL + "/" + name
            request = urllib.request.Request(url, headers={"User-Agent": "DrosoDocLab/2.0"})
            with urllib.request.urlopen(request, timeout=60) as response, open(str(path) + ".part", "wb") as output:
                total = int(response.headers.get("Content-Length", "0"))
                done, last = 0, 0.0
                while block := response.read(1024 * 1024):
                    check_cancel()
                    output.write(block)
                    done += len(block)
                    if time.monotonic() - last > 1:
                        emit("Download connettoma", file=name, bytes=done, total=total)
                        last = time.monotonic()
            actual = sha256(str(path) + ".part")
            if expected is not None and actual != expected:
                raise ValueError("L'esportazione FlyWire è cambiata: usare una nuova cartella del connettoma per mantenerne la versione.")
            os.replace(str(path) + ".part", path)
        sources[name] = dict(url=BASE_URL + "/" + name, sha256=sha256(path))
        # Preserve completed downloads if a later download is interrupted.
        save_json(provenance_path, previous | sources)
    return sources


def build_graph(directory, sources, check_cancel=lambda: None):
    directory = Path(directory)
    fingerprint = hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest()
    cache = directory / "visual-graph.pt"
    if cache.exists():
        graph = torch.load(cache, map_location="cpu", weights_only=True)
        if graph.get("version") == VERSION and graph.get("fingerprint") == fingerprint:
            return graph
    emit("Costruzione grafo senza soglia minima")
    neuron_rows = list(rows(directory / FILES[0]))
    ids = [int(row["root_id"]) for row in neuron_rows]
    if len(set(ids)) != len(ids):
        raise ValueError("ID neuronali duplicati")
    index = {root: i for i, root in enumerate(ids)}
    n = len(ids)
    source, target, weights = array.array("q"), array.array("q"), array.array("f")
    synapses = 0
    for i, row in enumerate(rows(directory / FILES[1])):
        if i % 250000 == 0:
            check_cancel()
            emit("Importazione di tutte le connessioni", rows=i)
        pre, post = int(row["pre_root_id"]), int(row["post_root_id"])
        count = float(row["syn_count"])
        if pre not in index or post not in index or not np.isfinite(count) or count <= 0:
            raise ValueError("Connessione non valida: nessun arco viene scartato silenziosamente")
        nt = row.get("nt_type", "").upper()
        sign = -1 if nt in {"GABA", "GLUT", "GLUTAMATE"} else 1
        source.append(index[pre]); target.append(index[post]); weights.append(sign * count)
        synapses += int(count)
    source = torch.from_numpy(np.frombuffer(source, dtype=np.int64).copy())
    target = torch.from_numpy(np.frombuffer(target, dtype=np.int64).copy())
    weight = torch.from_numpy(np.frombuffer(weights, dtype=np.float32).copy())
    if weight.numel() == 0:
        raise ValueError("Grafo privo di connessioni")
    incoming = torch.zeros(n).scatter_add_(0, target, weight.abs()).clamp_min(1)
    # Several neuropil rows for a pair are summed; no anatomical edge threshold/pruning.
    adjacency = torch.sparse_coo_tensor(torch.stack([target, source]), weight / incoming[target], (n, n)).coalesce().to_sparse_csr()
    receptors = [r for r in rows(directory / FILES[2]) if r["type"] in {"R7", "R8"}]
    if not receptors or any(int(r["root_id"]) not in index for r in receptors):
        raise ValueError("Mappa dei fotorecettori assente o incompatibile")
    coordinates = np.array([[float(r["x"]), float(r["y"])] for r in receptors], dtype=np.float32)
    for side in {r["hemisphere"] for r in receptors}:
        mask = np.array([r["hemisphere"] == side for r in receptors])
        points = coordinates[mask]
        span = np.maximum(points.max(0) - points.min(0), 1)
        points = 2 * (points - points.min(0)) / span - 1
        points[:, 1] *= -1
        if side == "left":
            points[:, 0] *= -1
        coordinates[mask] = points
    graph = dict(version=VERSION, fingerprint=fingerprint, ids=torch.tensor(ids), adjacency=adjacency,
                 input_indices=torch.tensor([index[int(r["root_id"])] for r in receptors]),
                 grid=torch.from_numpy(coordinates), neurons=n, connection_rows=len(weight),
                 pairs=adjacency.values().numel(), synapses=synapses, sources=sources)
    save_torch(cache, graph)
    save_json(directory / "graph-summary.json", {k: graph[k] for k in ["version", "fingerprint", "neurons", "connection_rows", "pairs", "synapses", "sources"]} | {
        "input_receptors": len(receptors), "input": "R7/R8, both hemispheres, annotated column x/y; grayscale drive; R1-6 not reconstructed as an input pathway",
        "model_assumptions": "Row-normalized signed weights; GABA/GLUT inhibitory, others positive; bounded rate dynamics. All exported neurons, including isolated ones. No pruning. Not a validated biological brain simulation."})
    return graph


class VisualBrain(nn.Module):
    def __init__(self, graph, steps=12, adaptive=False):
        super().__init__()
        n = graph["neurons"]
        self.steps = steps
        self.register_buffer("adjacency", graph["adjacency"], persistent=False)
        self.register_buffer("input_indices", graph["input_indices"], persistent=False)
        self.register_buffer("grid", graph["grid"], persistent=False)
        self.gain = nn.Parameter(torch.zeros(n), requires_grad=adaptive)
        self.time_constant = nn.Parameter(torch.zeros(n), requires_grad=adaptive)
        self.bias = nn.Parameter(torch.zeros(n), requires_grad=adaptive)
        self.readout = nn.Linear(n, len(LABELS))

    def encode(self, pages):
        samples = F.grid_sample(pages, self.grid.view(1, 1, -1, 2).expand(len(pages), -1, -1, -1), align_corners=True)
        drive = pages.new_zeros((len(pages), len(self.gain)))
        drive[:, self.input_indices] = 2 * samples[:, 0, 0, :] - 1
        state = torch.zeros_like(drive)
        gain = 0.5 + 1.5 * self.gain.sigmoid()
        alpha = 0.05 + 0.9 * self.time_constant.sigmoid()
        for _ in range(self.steps):
            current = torch.sparse.mm(self.adjacency, state.T).T
            state = state + alpha * (torch.tanh(gain * (current + drive) + self.bias) - state)
        return state

    def forward(self, pages, page_counts):
        states = self.encode(pages)
        # One training target per PDF, regardless of its page count.
        pooled = torch.stack([part.mean(0) for part in states.split(page_counts)])
        return self.readout(pooled)


def load_manifest(path):
    path = Path(path).resolve()
    with path.open(encoding="utf-8-sig", newline="") as stream:
        documents = list(csv.DictReader(stream))
    if not documents:
        raise ValueError("Manifest vuoto")
    seen = set()
    for row in documents:
        pdf = (path.parent / row["path"]).resolve()
        if row["label"] not in LABELS or not row.get("group_id"):
            raise ValueError("Categoria o gruppo studente assente")
        digest = sha256(pdf)
        if digest != row["sha256"].upper() or digest in seen:
            raise ValueError("PDF cambiato o duplicato nel manifest")
        seen.add(digest)
        row["sha256"] = digest
        row["path"] = str(pdf)
        record = json.loads(Path(str(pdf) + ".pages.json").read_text(encoding="utf-8-sig"))
        if record.get("Version") != 1 or record["PdfSha256"] != digest or not record["Images"]:
            raise ValueError("Preparazione delle immagini assente o obsoleta")
        images = []
        for image in record["Images"]:
            if Path(image["File"]).name != image["File"]:
                raise ValueError("Percorso immagine non valido")
            image_path = Path(str(pdf) + ".pages") / image["File"]
            if sha256(image_path) != image["Sha256"]:
                raise ValueError("Immagine modificata: ripreparare il PDF")
            images.append(str(image_path))
        row["images"] = images
    return documents


def ensure_disjoint(first, second):
    for field in ["sha256", "group_id"]:
        if {d[field] for d in first} & {d[field] for d in second}:
            raise ValueError("Contaminazione fra insiemi: " + field)


def load_pages(documents, device):
    pages, counts = [], []
    for document in documents:
        counts.append(len(document["images"]))
        for path in document["images"]:
            with Image.open(path) as image:
                image = ImageOps.pad(image.convert("L"), (192, 192), method=Image.Resampling.LANCZOS, color=255)
                pages.append(torch.from_numpy(np.array(image, dtype=np.float32) / 255).unsqueeze(0))
    return torch.stack(pages).to(device), counts


def report(documents, probabilities, threshold=0.75):
    matrix = np.zeros((3, 3), dtype=int)
    accepted = correct_accepted = correct = 0
    details = []
    for document, scores in zip(documents, probabilities, strict=True):
        actual = LABELS.index(document["label"])
        predicted = int(np.argmax(scores))
        confidence = float(scores[predicted])
        matrix[actual, predicted] += 1
        correct += int(actual == predicted)
        accepted += int(confidence >= threshold)
        correct_accepted += int(confidence >= threshold and actual == predicted)
        details.append(dict(path=document["path"], truth=LABELS[actual], predicted=LABELS[predicted], confidence=confidence,
                            review=confidence < threshold, label_source=document.get("label_source", "office")))
    metrics = []
    for i, label in enumerate(LABELS):
        precision = int(matrix[i, i]) / max(1, int(matrix[:, i].sum()))
        recall = int(matrix[i, i]) / max(1, int(matrix[i].sum()))
        metrics.append(dict(label=label, precision=precision, recall=recall, f1=2*precision*recall/max(1e-12, precision+recall)))
    return dict(total=len(documents), office_label_agreement=correct/len(documents), macro_f1=sum(m["f1"] for m in metrics)/3,
                confusion_matrix=matrix.tolist(), matrix_orientation="rows=reference, columns=prediction", classes=metrics,
                threshold=threshold, coverage=accepted/len(documents), accuracy_on_accepted=correct_accepted/accepted if accepted else None,
                calibration="Softmax scores are uncalibrated; threshold is not an administrative guarantee", rows=details)
