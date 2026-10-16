"""Local ResNet18 document classifier; checkpoints run without network access."""
from contextlib import contextmanager
import hashlib
import os
from pathlib import Path
import random
import time
import urllib.request

from filelock import FileLock, Timeout
import numpy as np
from PIL import Image, ImageEnhance, ImageOps
import torch
from torch import nn
from torch.utils.checkpoint import checkpoint

from document_model import DOCUMENT_LABELS, _PagePrefetch, content_crop
from visual_brain import emit

ARCHITECTURE = "resnet18"
INPUT_VERSION = "resnet18-rgb-full-content-pad224-imagenet-mean-v1"
PRETRAINING = "IMAGENET1K_V1"
WEIGHTS_URL = "https://download.pytorch.org/models/resnet18-f37072fd.pth"
WEIGHTS_FILE = "resnet18-f37072fd.pth"
WEIGHTS_HASH = "f37072fd"


def torchvision_resnet18():
    try:
        from torchvision.models import resnet18
    except (ImportError, RuntimeError) as error:
        raise RuntimeError("ResNet18 richiede torchvision compatibile con PyTorch; usare la preparazione automatica del programma") from error
    return resnet18(weights=None)


def pretrained_cache(request):
    fallback = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "DrosoDocLab" / "models"
    return Path(request.get("pretrained_cache") or fallback)


def _valid_weights(path, check_cancel):
    if not path.is_file():
        return False
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while data := stream.read(1024 * 1024):
            check_cancel()
            digest.update(data)
    return digest.hexdigest().startswith(WEIGHTS_HASH)


def ensure_pretrained_weights(directory, check_cancel=lambda: None):
    """Cancellable download with a shared lock and torchvision's official hash."""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / WEIGHTS_FILE
    lock = FileLock(str(directory / "resnet18-download.lock"))
    announced = False
    while True:
        check_cancel()
        try:
            lock.acquire(timeout=.2)
            break
        except Timeout:
            if not announced:
                emit("ResNet18: attendo il download già in corso", architecture=ARCHITECTURE)
                announced = True
    try:
        if not _valid_weights(target, check_cancel):
            temporary = directory / (WEIGHTS_FILE + ".partial")
            emit("Download dei pesi ufficiali ResNet18 · ImageNet", architecture=ARCHITECTURE,
                 download=True, current=0, total=0)
            try:
                request = urllib.request.Request(WEIGHTS_URL, headers={"User-Agent": "DrosoDocLab"})
                with urllib.request.urlopen(request, timeout=30) as source, temporary.open("wb") as destination:
                    total, received, next_progress = int(source.headers.get("Content-Length", 0)), 0, 0.
                    while True:
                        check_cancel()
                        chunk = source.read(256 * 1024)
                        if not chunk:
                            break
                        destination.write(chunk)
                        received += len(chunk)
                        now = time.monotonic()
                        if now >= next_progress:
                            emit("Download ResNet18", architecture=ARCHITECTURE, download=True,
                                 current=received, total=total)
                            next_progress = now + .25
                check_cancel()
                if not _valid_weights(temporary, check_cancel):
                    raise ValueError("Pesi ResNet18 scaricati non integri: verifica SHA256 fallita")
                temporary.replace(target)
            finally:
                temporary.unlink(missing_ok=True)
        check_cancel()
        # The cancellable transfer above fills this cache first. Keep the PyTorch
        # hash-check contract, and verify existing cached files ourselves as well.
        state = torch.hub.load_state_dict_from_url(WEIGHTS_URL, model_dir=str(directory),
            map_location="cpu", progress=False, check_hash=True, weights_only=True)
        emit("Pesi ResNet18 pronti nella cache locale", architecture=ARCHITECTURE, pretrained_cache=str(directory))
        return state
    finally:
        lock.release()


def page_views(path, variant=0, seed=0, *, augmentation_seed=None):
    """Preserve the whole page alongside its content; no centre crop."""
    with Image.open(path) as source:
        page = ImageOps.exif_transpose(source).convert("RGB")
        if variant:
            rng = random.Random(f"{seed}:{augmentation_seed}")
            page = page.rotate(rng.uniform(-2, 2), resample=Image.Resampling.BICUBIC,
                               expand=True, fillcolor=(255, 255, 255))
            page = ImageEnhance.Contrast(page).enhance(rng.uniform(.85, 1.15))
            page = ImageEnhance.Brightness(page).enhance(rng.uniform(.93, 1.07))
        views = np.empty((2, 3, 224, 224), dtype=np.float32)
        mean = np.array([.485, .456, .406], dtype=np.float32)[:, None, None]
        std = np.array([.229, .224, .225], dtype=np.float32)[:, None, None]
        for index, crop in enumerate((page, content_crop(page))):
            view = ImageOps.pad(crop, (224, 224), method=Image.Resampling.LANCZOS, color=(255, 255, 255))
            pixels = np.asarray(view, dtype=np.float32).transpose(2, 0, 1) / 255
            views[index] = (pixels - mean) / std
    return torch.from_numpy(views)


class ResNetDocumentModel(nn.Module):
    engine_version = 4
    architecture = ARCHITECTURE
    dynamic_responses = True
    view_names = ("pagina", "documento")

    def __init__(self, pretrained_state=None):
        super().__init__()
        network = torchvision_resnet18()
        if pretrained_state is not None:
            network.load_state_dict(pretrained_state, strict=True)
        self.backbone = nn.Sequential(*list(network.children())[:-1])
        self.readout = nn.Linear(network.fc.in_features, len(DOCUMENT_LABELS))
        self.labels = list(DOCUMENT_LABELS)
        self.supported_labels = self.labels
        self.warming = False

    def encode(self, views):
        return self.backbone(views).flatten(1)

    def regularization_loss(self):
        return self.readout.weight.new_zeros(())

    def train(self, mode=True):
        super().train(mode)
        if mode and self.warming:
            self.backbone.eval()
        elif mode:
            # Small document batches and checkpoint recomputation must not move
            # ImageNet running statistics twice. Affine BN weights still learn.
            for module in self.backbone.modules():
                if isinstance(module, nn.modules.batchnorm._BatchNorm):
                    module.eval()
        return self


class ResNetResponses:
    """All pages contribute equally to one document prediction, in bounded groups."""
    def __init__(self, model, config, directory, device, check_cancel=lambda: None, on_activity=None):
        self.model, self.config, self.device, self.check_cancel = model, config, device, check_cancel
        self.hits, self.misses, self.view_batch_size, self.feature_cache = 0, 0, 8, None
        self.on_activity = on_activity
        self._prefetch = None

    def _request(self, document, path, variant, augmentation_seed):
        seed = int(hashlib.sha256(f"{self.config['seed']}:{document['sha256']}:{path}".encode()).hexdigest()[:16], 16)
        return path, variant, seed, augmentation_seed

    def _prepare(self, request):
        self.check_cancel()
        path, variant, seed, augmentation_seed = request
        return page_views(path, variant, seed, augmentation_seed=augmentation_seed)

    @contextmanager
    def prefetch(self, plan):
        requests = (self._request(document, path, variant, augmentation_seed)
                    for document, variant, augmentation_seed in plan for path in document["images"])
        pending = _PagePrefetch(requests, self._prepare, self.check_cancel)
        self._prefetch = pending
        try:
            yield self
        finally:
            self._prefetch = None
            pending.close()

    def predict_batch(self, documents, variants=None, *, augmentation_seed=None):
        variants = variants if variants is not None else [0] * len(documents)
        if len(variants) != len(documents):
            raise ValueError("Varianti ResNet18 diverse dal numero di documenti")
        predictions = []
        for document, variant in zip(documents, variants):
            if not document["images"]:
                raise ValueError("Documento senza pagine")
            chunks, pending = [], []
            def flush():
                if not pending:
                    return
                self.check_cancel()
                views = torch.cat(pending).to(self.device)
                # Recompute backbone activations in backward for long PDFs; every
                # page stays in the objective without retaining full CNN maps.
                if self.model.training and not self.model.warming and torch.is_grad_enabled():
                    encoded = checkpoint(self.model.encode, views, use_reentrant=False)
                else:
                    encoded = self.model.encode(views)
                chunks.append(encoded)
                pending.clear()
            for path in document["images"]:
                request = self._request(document, path, variant, augmentation_seed)
                pending.append(self._prefetch.take(request) if self._prefetch else self._prepare(request))
                if len(pending) * len(self.model.view_names) >= self.view_batch_size:
                    flush()
            flush()
            features = torch.cat(chunks)
            logits = self.model.readout(features.mean(0))
            weights = features.new_full((len(features),), 1 / len(features))
            predictions.append((logits, weights))
        return predictions

    def predict(self, document, variant=0, *, augmentation_seed=None):
        return self.predict_batch([document], [variant], augmentation_seed=augmentation_seed)[0]


class ModelTelemetry:
    """Metric sink without fabricated neuron activity for the CNN."""
    def set_metrics(self, metrics):
        pass


def model_from_checkpoint(directory, metadata, device):
    if (metadata.get("version") != 1 or metadata.get("engine_version") != 4
            or metadata.get("input_version") != INPUT_VERSION or metadata.get("pretraining") != PRETRAINING
            or metadata.get("labels") != DOCUMENT_LABELS or metadata.get("model_family")):
        raise ValueError("Versione, categorie o pre-elaborazione ResNet18 incompatibili")
    model = ResNetDocumentModel().to(device)
    state = torch.load(Path(directory) / "best.pt", map_location=device, weights_only=True)
    if state["signature"] != metadata["signature"]:
        raise ValueError("Checkpoint e metadati ResNet18 non corrispondono")
    model.load_state_dict(state["model"], strict=True)
    model.supported_labels = metadata.get("supported_labels", model.labels)
    model.eval()
    return model, metadata
