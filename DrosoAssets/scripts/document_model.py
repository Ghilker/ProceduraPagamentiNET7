"""Visual document inputs, adaptive connectome and versioned frozen responses."""
import hashlib
import io
import json
import os
from collections import deque
from concurrent.futures import ThreadPoolExecutor, TimeoutError
from contextlib import contextmanager
from pathlib import Path
import pickle
import random
from threading import Event
import uuid
from datetime import datetime, timezone

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter, ImageOps
import torch
from torch import nn
from torch.nn import functional as F
from torch.utils.checkpoint import checkpoint

from visual_brain import LABELS, VisualBrain, sha256

ENGINE_VERSION = 4
# Historical format identifier retained for existing visual-only checkpoints/cache keys.
LEGACY_INPUT_VERSION = "global-quadrants-overlap-384-ocr-charword-unitnorm-v1"
INPUT_VERSION = "global-content-overlap3x3-384-receptive-fields-v3"
V4_INPUT_VERSION = "global-content-overlap3x3-384-visual-gustatory-v4"
COLOR_INPUT_VERSION = "global-content-overlap3x3-384-visual-gustatory-rgb-v4"


def input_version(version, color_features=False, learned_visual_input=False):
    if learned_visual_input:
        from learned_visual_input import INPUT_VERSION as learned_input_version
        return learned_input_version
    return (COLOR_INPUT_VERSION if color_features else V4_INPUT_VERSION) if version >= 4 else INPUT_VERSION if version >= 3 else LEGACY_INPUT_VERSION
DOCUMENT_LABELS = ["PERMESSO_SOGGIORNO", "RICEVUTA", "PASSAPORTO", "ERRATO"]
LEGACY_VIEW_NAMES = ("pagina", "alto-sinistra", "alto-destra", "basso-sinistra", "basso-destra")
VIEW_NAMES = ("pagina", "documento") + tuple(f"dettaglio-{row + 1}-{col + 1}"
                                             for row in range(3) for col in range(3))


def canonical_label(label):
    return "RICEVUTA" if label == "RICEVUTA_PERMESSO" else label


def content_crop(page):
    """Trim only near-white margins; the separate full-page view always preserves context."""
    pixels = np.asarray(page.convert("L") if page.mode != "L" else page)
    background = float(np.percentile(pixels, 95))
    if background < 220:
        return page
    foreground = pixels < min(235, background - 20)
    ys, xs = np.nonzero(foreground)
    if len(xs) < max(32, pixels.size * .001):
        return page
    left, top, right, bottom = int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1
    if right - left < 16 or bottom - top < 16:
        return page
    padding = max(4, round(.03 * max(right - left, bottom - top)))
    return page.crop((max(0, left - padding), max(0, top - padding),
                      min(page.width, right + padding), min(page.height, bottom + padding)))


def prepare_document(row, *, verifier=None, check_cancel=lambda: None):
    row = dict(row)
    def digest_file(path):
        check_cancel()
        value = verifier.sha256(path, check_cancel=check_cancel) if verifier is not None else sha256(path)
        check_cancel()
        return value

    check_cancel()
    pdf = Path(row["path"]).resolve()
    digest = digest_file(pdf)
    if row.get("sha256", digest).upper() != digest:
        raise ValueError("PDF modificato: " + str(pdf))
    record = json.loads(Path(str(pdf) + ".pages.json").read_text(encoding="utf-8-sig"))
    check_cancel()
    if record.get("Version") != 1 or record["PdfSha256"].upper() != digest or not record["Images"]:
        raise ValueError("Immagini non corrispondenti al PDF: " + str(pdf))
    paths, hashes = [], []
    for image in record["Images"]:
        if Path(image["File"]).name != image["File"]:
            raise ValueError("Nome immagine non valido")
        path = Path(str(pdf) + ".pages") / image["File"]
        actual = digest_file(path)
        if actual != image["Sha256"].upper():
            raise ValueError("Immagine modificata: " + str(path))
        paths.append(str(path)); hashes.append(actual)
    row.update(path=str(pdf), sha256=digest, images=paths, image_hashes=hashes)
    return row


def page_views(path, variant=0, seed=0, engine_version=ENGINE_VERSION, *, augmentation_seed=None, color_features=False):
    """Keep full-page context beside a content crop and overlapping detail views."""
    if engine_version not in (2, 3, ENGINE_VERSION):
        raise ValueError("Versione delle viste non supportata")
    with Image.open(path) as source:
        page = (ImageOps.exif_transpose(source) if engine_version >= 3 else source).convert("RGB" if color_features else "L")
        white = (255, 255, 255) if color_features else 255
        if variant:
            varied = augmentation_seed is not None
            rng = random.Random(f"{seed}:{augmentation_seed}" if varied else seed)
            angle = 2 if varied else 1.2
            page = page.rotate(rng.uniform(-angle, angle), resample=Image.Resampling.BICUBIC,
                               expand=True, fillcolor=white)
            page = ImageEnhance.Contrast(page).enhance(rng.uniform(.85, 1.15) if varied else rng.uniform(.9, 1.1))
            page = ImageEnhance.Brightness(page).enhance(rng.uniform(.93, 1.07) if varied else rng.uniform(.95, 1.05))
            if varied and rng.random() < .35:
                page = page.filter(ImageFilter.GaussianBlur(rng.uniform(.15, .6)))
            if varied and rng.random() < .35:
                with io.BytesIO() as compressed:
                    page.save(compressed, format="JPEG", quality=rng.randint(76, 95))
                    compressed.seek(0)
                    with Image.open(compressed) as decoded:
                        page = decoded.copy()
        if engine_version == 2:
            w, h = page.size
            boxes = [(0, 0, w, h), (0, 0, .6*w, .6*h), (.4*w, 0, w, .6*h),
                     (0, .4*h, .6*w, h), (.4*w, .4*h, w, h)]
            crops = [page.crop(tuple(round(x) for x in box)) for box in boxes]
        else:
            content = content_crop(page)
            w, h = content.size
            crops = [page, content] + [content.crop(tuple(round(x) for x in
                (col * .25*w, row * .25*h, (col * .25 + .5)*w, (row * .25 + .5)*h)))
                for row in range(3) for col in range(3)]
        # Assemble once on the CPU: repeated torch.cat/stack here would dispatch
        # thread pools for small copies while the GPU waits for its next page.
        views = np.empty((len(crops), 4 if color_features else 1, 384, 384), dtype=np.float32)
        for index, crop in enumerate(crops):
            view = ImageOps.pad(crop, (384, 384),
                                method=Image.Resampling.LANCZOS, color=white)
            views[index, 0] = np.array(view.convert("L"), dtype=np.float32) / 255
            if color_features:
                views[index, 1:] = (np.array(view, dtype=np.float32) / 255).transpose(2, 0, 1)
        return torch.from_numpy(views)


class _FixedGraphMM(torch.autograd.Function):
    """Differentiate only the dense state of an immutable anatomical graph.

    Native CSR backward converts the transposed graph back to CSR at each step.
    Reusing that transpose gives the same dX = A.T @ dY without repeated sorting.
    """
    @staticmethod
    def forward(ctx, adjacency, transpose, values):
        ctx.save_for_backward(transpose)
        return torch.sparse.mm(adjacency, values)

    @staticmethod
    def backward(ctx, gradient):
        (transpose,) = ctx.saved_tensors
        return None, None, torch.sparse.mm(transpose, gradient)


class DocumentBrain(VisualBrain):
    # Preserve the direct V3 constructor API; the engine always passes its saved version.
    def __init__(self, graph, steps=12, adaptive=False, engine_version=3, labels=None,
                 *, color_features=False, taste_adaptive=None, learned_visual_input=False, visual_pretrained_state=None,
                 visual_backbone_state=None, visual_encoder_source=None):
        if engine_version not in (2, 3, ENGINE_VERSION):
            raise ValueError("Versione del modello non supportata")
        super().__init__(graph, steps, adaptive)
        self.adaptive = adaptive
        self.learned_visual_input = bool(learned_visual_input)
        if self.learned_visual_input and (engine_version != 4 or not color_features or adaptive or taste_adaptive):
            raise ValueError("Input visivo appreso: usare Droso V4 a colori, neuroni fissi ed encoder gustativo fisso, in una nuova cartella")
        self.taste_adaptive = bool(adaptive if taste_adaptive is None else taste_adaptive) if engine_version >= 4 else False
        self.dynamic_responses = adaptive or self.taste_adaptive or self.learned_visual_input
        self.color_features = bool(color_features)
        if self.color_features and engine_version < 4:
            raise ValueError("I colori gustativi richiedono un modello V4")
        self.engine_version = engine_version
        self.labels = list(labels if labels is not None else LABELS if engine_version == 2 else DOCUMENT_LABELS)
        self.view_names = LEGACY_VIEW_NAMES if engine_version == 2 else VIEW_NAMES
        n = graph["neurons"]
        if engine_version >= 3:
            self.readout = nn.Linear(n, len(self.labels))
        self.attention = nn.Linear(n, 1)
        self.position_bias = nn.Parameter(torch.zeros(len(self.view_names)))
        # Runtime acceleration only: never changes the checkpoint or graph identity.
        self.register_buffer("_adjacency_transpose", None, persistent=False)
        # Engineering plasticity, not reconstructed anatomical synapses. Factorization
        # connects every neuron with O(n * rank) storage instead of an O(n²) matrix.
        self.plasticity_rank = min(8, n) if adaptive and engine_version >= 3 else 0
        if self.plasticity_rank:
            self.plasticity_source = nn.Parameter(torch.randn(n, self.plasticity_rank) * .05)
            self.plasticity_target = nn.Parameter(torch.randn(n, self.plasticity_rank) * .05)
        if self.learned_visual_input:
            from learned_visual_input import LearnedVisualInput
            self.visual_encoder = LearnedVisualInput(len(self.input_indices), visual_pretrained_state,
                                                    backbone_state=visual_backbone_state, encoder_source=visual_encoder_source)
            self.visual_input_metadata = self.visual_encoder.metadata()
        elif engine_version >= 4:
            from gustatory import FEATURE_VERSION, COLOR_FEATURE_VERSION, feature_count, taste_metadata
            self.feature_version = COLOR_FEATURE_VERSION if self.color_features else FEATURE_VERSION
            self.gustatory_metadata = taste_metadata(graph, self.feature_version)
            self.register_buffer("taste_input_indices", graph["taste_input_indices"], persistent=False)
            self.register_buffer("taste_input_channels", graph["taste_input_channels"], persistent=False)
            self.taste_encoder = nn.Linear(feature_count(self.feature_version), 4)
            self.taste_encoder.requires_grad_(self.taste_adaptive)

    @property
    def plasticity_metadata(self):
        return dict(kind="bounded-low-rank" if self.plasticity_rank else "none",
                    rank=self.plasticity_rank, neurons=len(self.gain),
                    maximum_additional_drive=.35 if self.plasticity_rank else 0,
                    anatomical_graph_preserved=True)

    def receptor_samples(self, pages):
        grid = self.grid.view(1, 1, -1, 2).expand(len(pages), -1, -1, -1)
        fine = F.grid_sample(pages, grid, align_corners=True)[:, 0, 0, :]
        if self.engine_version == 2:
            return fine
        # Replication avoids inventing dark borders. Fine structure and neighborhood
        # densities both reach each receptor, including ink between sampling points.
        def receptive_field(size):
            radius = size // 2
            padded = F.pad(pages, (radius, radius, radius, radius), mode="replicate")
            return F.avg_pool2d(F.avg_pool2d(padded, (1, size), stride=1), (size, 1), stride=1)
        local = receptive_field(9)
        context = receptive_field(33)
        return (.5 * fine + .3 * F.grid_sample(local, grid, align_corners=True)[:, 0, 0, :]
                + .2 * F.grid_sample(context, grid, align_corners=True)[:, 0, 0, :])

    def encode(self, pages, taste_features=None, *, taste_enabled=True, trace=None):
        if self.engine_version == 2:
            return super().encode(pages)
        drive = pages.new_zeros((len(pages), len(self.gain)))
        if self.learned_visual_input:
            drive[:, self.input_indices] = self.visual_encoder(pages)
        else:
            samples = self.receptor_samples(pages[:, :1])
            drive[:, self.input_indices] = 2 * samples - 1
        if self.engine_version >= 4 and taste_enabled and not self.learned_visual_input:
            from gustatory import visual_features, MAXIMUM_DRIVE
            features = visual_features(pages, self.feature_version) if taste_features is None else taste_features
            intensities = MAXIMUM_DRIVE * self.taste_encoder(2 * features - 1).sigmoid()
            drive[:, self.taste_input_indices] = intensities[:, self.taste_input_channels]
        state = torch.zeros_like(drive)
        gain = .5 + 1.5 * self.gain.sigmoid()
        alpha = .05 + .9 * self.time_constant.sigmoid()
        if self.plasticity_rank:
            source = self.plasticity_source.tanh() / len(self.gain) ** .5
            target = self.plasticity_target.tanh().T / self.plasticity_rank
        for _ in range(self.steps):
            if (self.adjacency.is_cuda and self.adjacency.layout == torch.sparse_csr
                    and not self.adjacency.requires_grad and torch.is_grad_enabled() and state.requires_grad):
                if self._adjacency_transpose is None:
                    self._adjacency_transpose = self.adjacency.transpose(0, 1).to_sparse_csr()
                current = _FixedGraphMM.apply(self.adjacency, self._adjacency_transpose, state.T).T
            else:
                current = torch.sparse.mm(self.adjacency, state.T).T
            if self.plasticity_rank:
                current = current + .35 * ((state @ source).tanh() @ target)
            state = state + alpha * (torch.tanh(gain * (current + drive) + self.bias) - state)
            if trace is not None:
                # Diagnostics explicitly request one view; never retained by training.
                if hasattr(trace, "capture"):
                    trace.capture(state[0].detach())
                else:
                    trace.append(state[0].detach().to(device="cpu", copy=True))
        return state

    def regularization_loss(self):
        if not self.plasticity_rank:
            return self.gain.new_zeros(())
        loss = (self.gain.square().mean() + self.time_constant.square().mean()
                + self.bias.square().mean() + self.plasticity_source.square().mean()
                + self.plasticity_target.square().mean())
        if self.engine_version >= 4:
            loss = loss + self.taste_encoder.weight.square().mean() + self.taste_encoder.bias.square().mean()
        return loss

    def classify_states(self, states):
        # Keep update magnitude independent of the 139k-dimensional neuron population.
        normalized = F.normalize(F.layer_norm(states, (states.shape[-1],)), dim=-1)
        positions = torch.arange(len(states), device=states.device) % len(self.view_names)
        weights = (self.attention(normalized).squeeze(-1) + self.position_bias[positions]).softmax(0)
        pooled = (normalized * weights[:, None]).sum(0)
        logits = self.readout(pooled)
        return logits, weights


class _PagePrefetch:
    """One CPU worker, at most two page tensors ahead, no dataset-sized allocation."""
    def __init__(self, requests, prepare, check_cancel):
        self.requests = iter(requests)
        self.prepare, self.check_cancel = prepare, check_cancel
        self.pending = deque()
        self.stopped = Event()
        self.executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="droso-pages")
        try:
            self._fill()
        except BaseException:
            self.close()
            raise

    def _prepare(self, request):
        if self.stopped.is_set():
            return None
        return self.prepare(request)

    def _fill(self):
        while len(self.pending) < 2:
            self.check_cancel()
            request = next(self.requests, None)
            if request is None:
                break
            self.pending.append((request, self.executor.submit(self._prepare, request)))

    def take(self, request):
        self.check_cancel()
        if not self.pending or self.pending[0][0] != request:
            raise ValueError("Ordine di preparazione delle pagine diverso dal training")
        _, future = self.pending.popleft()
        while True:
            self.check_cancel()
            try:
                result = future.result(timeout=.1)
                break
            except TimeoutError:
                if future.done():
                    result = future.result()
                    break
                continue
        self.check_cancel()
        self._fill()
        return result

    def close(self):
        self.stopped.set()
        self.executor.shutdown(wait=True, cancel_futures=True)
        self.pending.clear()


class Responses:
    def __init__(self, model, config, directory, device, check_cancel=lambda: None, on_activity=None,
                 *, view_batch_size=None):
        self.model, self.config, self.device = model, config, device
        # Legacy responses-v2 paths are redirected before any native read/write.
        from cache_paths import resolve_response_directory
        self.directory = None if model.learned_visual_input else resolve_response_directory(directory)
        self.check_cancel = check_cancel
        self.hits = self.misses = 0
        self.on_activity = on_activity
        self.last_cached = False
        self._capture_activity = False
        self._activity_view = None
        self._prefetch = None
        self.engine_version = model.engine_version
        self.representation_size = model.representation_size if hasattr(model, "representation_size") else len(model.gain)
        self.feature_cache = None
        if model.color_features and config.get("feature_cache") and not model.learned_visual_input:
            from gustatory_cache import GustatoryFeatureCache
            self.feature_cache = GustatoryFeatureCache(config["feature_cache"],
                input_version(self.engine_version, True), model.feature_version)
        # Dispatch independent views together on CUDA; keep the optimizer's PDF
        # minibatch and the cache/checkpoint identity unchanged.
        if view_batch_size is None:
            view_batch_size = len(model.view_names) if torch.device(device).type == "cuda" else 1
        if type(view_batch_size) is not int or not 1 <= view_batch_size <= len(VIEW_NAMES):
            raise ValueError("Il gruppo di viste deve contenere da 1 a 11 elementi")
        self.view_batch_size = min(view_batch_size, len(model.view_names))
        self.identity = (dict(input=config["input_version"]) if getattr(model, "architecture", None) == "visual-control" else
                         dict(input=input_version(self.engine_version, model.color_features, model.learned_visual_input),
                              graph=config["graph_fingerprint"], steps=config["steps"]))
        if self.engine_version >= 4 and not model.dynamic_responses:
            digest = hashlib.sha256()
            for parameter in model.taste_encoder.parameters():
                digest.update(parameter.detach().cpu().contiguous().numpy().tobytes())
            self.identity.update(taste=digest.hexdigest(), mapping=model.gustatory_metadata["mapping_fingerprint"])

    def cache_path(self, document, variant):
        if self.model.learned_visual_input:
            raise ValueError("La cache delle risposte è disabilitata per l'input visivo appreso")
        data = self.identity | dict(images=document["image_hashes"], variant=variant,
                                    seed=self.config["seed"] if variant else 0)
        key = hashlib.sha256(json.dumps(data, sort_keys=True).encode()).hexdigest()
        return self.directory / (key + ".pt")

    def _page_request(self, document, page, variant, augmentation_seed):
        seed = int(document["image_hashes"][page][:16], 16) ^ self.config["seed"]
        return (document["images"][page], variant, seed, self.engine_version,
                augmentation_seed if self.model.dynamic_responses and variant else None)

    def _prepare_page(self, request):
        args, augmentation_seed = request[:4], request[4]
        options = dict(color_features=True) if self.model.color_features else {}
        views = (page_views(*args, **options) if augmentation_seed is None else
                 page_views(*args, augmentation_seed=augmentation_seed, **options))
        return views.pin_memory() if torch.device(self.device).type == "cuda" else views

    @contextmanager
    def prefetch(self, plan):
        """Prepare (document, variant, augmentation_seed) in exact prediction order.

        Frozen responses keep their original cache path and avoid unnecessary image I/O.
        Closing the context on cancellation or failure also joins the bounded CPU worker.
        """
        if not self.model.dynamic_responses:
            yield self
            return
        if self._prefetch is not None:
            raise RuntimeError("Preparazione anticipata già attiva")
        requests = (self._page_request(document, page, variant, augmentation_seed)
                    for document, variant, augmentation_seed in plan
                    for page in range(len(document["images"])))
        with_prefetch = _PagePrefetch(requests, self._prepare_page, self.check_cancel)
        self._prefetch = with_prefetch
        try:
            yield self
        finally:
            self._prefetch = None
            with_prefetch.close()

    def states(self, document, variant=0, *, augmentation_seed=None):
        self.check_cancel()
        self.last_cached = False
        path = None if self.model.learned_visual_input else self.cache_path(document, variant)
        expected = (len(self.model.view_names) * len(document["images"]), self.representation_size)
        if not self.model.dynamic_responses and path.exists():
            try:
                cached = torch.load(path, map_location="cpu", weights_only=True)
                value = cached["states"]
                if cached["key"] == path.stem and tuple(value.shape) == expected and torch.isfinite(value).all():
                    self.hits += 1
                    self.last_cached = True
                    if self._capture_activity:
                        request = self._page_request(document, 0, variant, augmentation_seed)
                        self._activity_view = self._prepare_page(request)[:1].clone()
                    return value.to(self.device, dtype=torch.float32)
            except (OSError, RuntimeError, ValueError, KeyError, EOFError, pickle.UnpicklingError):
                pass  # Incomplete/corrupt private cache is regenerated from verified images.
        if not self.model.learned_visual_input:
            self.misses += 1
        states = []
        for page in range(len(document["images"])):
            self.check_cancel()
            request = self._page_request(document, page, variant, augmentation_seed)
            views = (self._prefetch.take(request) if self._prefetch is not None else
                     self._prepare_page(request))
            if self._capture_activity and page == 0:
                self._activity_view = views[:1].clone()
            features = (self.feature_cache.get(document["image_hashes"][page], views)
                        if self.feature_cache is not None and variant == 0 else None)
            # At most one page at a time, in original view order. Checkpoint each
            # group so longer PDFs do not retain all intermediate neuron states.
            for offset, view in enumerate(views.split(self.view_batch_size)):
                self.check_cancel()
                view = view.to(self.device, non_blocking=view.is_pinned())
                arguments = (view,)
                if features is not None:
                    start = offset * self.view_batch_size
                    arguments += (features[start:start + len(view)].to(self.device),)
                elif self.model.color_features and not self.model.learned_visual_input:
                    from gustatory import visual_features
                    arguments += (visual_features(view, self.model.feature_version),)
                if self.model.dynamic_responses and torch.is_grad_enabled():
                    result = checkpoint(self.model.encode, *arguments, use_reentrant=False)
                else:
                    with torch.no_grad():
                        result = self.model.encode(*arguments)
                self.check_cancel()
                states.append(result)
        result = torch.cat(states)
        if not self.model.dynamic_responses:
            self.directory.mkdir(parents=True, exist_ok=True)
            # Same quantization on a miss and a hit; half precision halves disk consumption.
            compact = result.detach().cpu().half()
            temporary = path.with_suffix("." + uuid.uuid4().hex + ".tmp")
            try:
                torch.save(dict(key=path.stem, states=compact), temporary)
                os.replace(temporary, path)
            finally:
                temporary.unlink(missing_ok=True)
            result = compact.to(self.device, dtype=torch.float32)
        return result

    def predict(self, document, variant=0, *, augmentation_seed=None):
        capture_due = getattr(self.on_activity, "capture_due", None)
        self._capture_activity = bool(self.engine_version >= 3 and capture_due is not None and capture_due())
        self._activity_view = None
        try:
            states = self.states(document, variant, augmentation_seed=augmentation_seed)
            prediction = self.model.classify_states(states)
            publish = getattr(self.on_activity, "publish_prediction", None)
            if publish is not None:
                publish(states, self.last_cached, self.model, document, variant, self._activity_view, prediction[0])
            elif self.on_activity is not None:
                self.on_activity(states, self.last_cached)
            return prediction
        finally:
            self._capture_activity = False
            self._activity_view = None

    def _batch_memory_plan(self, total_views):
        """Bound dispatch and saved activations independently of the PDF minibatch.

        Leave room for the graph, gradients, optimizer and sparse-kernel workspace.
        Estimates deliberately include more intermediates than the current encoder;
        they are a runtime policy, never part of a model or cache identity.
        """
        cuda = torch.device(self.device).type == "cuda"
        budget = 64 * 1024**2
        if cuda:
            free, _ = torch.cuda.mem_get_info(self.device)
            reusable = max(0, torch.cuda.memory_reserved(self.device) - torch.cuda.memory_allocated(self.device))
            budget = min(8 * 1024**3, max(0, int((free + reusable - 1024**3) * .4)))
        channels = 4 if self.model.color_features else 1
        image_bytes = channels * 384 * 384 * 4
        state_bytes = self.representation_size * 4
        encoder_grad = torch.is_grad_enabled() and any(
            parameter.requires_grad for name, parameter in self.model.named_parameters()
            if name not in ("readout.weight", "readout.bias", "attention.weight", "attention.bias", "position_bias"))
        retained_bytes = image_bytes * 3 + state_bytes * (12 * self.model.steps + 24 if encoder_grad else 12)
        retain = total_views * retained_bytes <= budget
        per_view = retained_bytes if retain else image_bytes * 3 + state_bytes * 24
        # On the measured CUDA CSR path, wider backward products are slower than
        # eleven views. Warmup/inference can still combine up to four pages.
        maximum = self.view_batch_size * ((1 if encoder_grad else 4) if cuda else 2)
        return min(total_views, maximum, max(1, budget // max(1, per_view))), retain

    def _encode_batch_group(self, views, features, retain):
        """Retry only allocation failure, without changing inputs or learned state."""
        self.check_cancel()
        arguments = (views,) if features is None else (views, features)
        try:
            if retain or not torch.is_grad_enabled():
                return self.model.encode(*arguments)
            return checkpoint(self.model.encode, *arguments, use_reentrant=False)
        except torch.cuda.OutOfMemoryError:
            # Exit the exception scope before retrying, releasing its traceback and
            # any partially built activation graph. No optimizer step happened.
            if not retain and len(views) == 1:
                raise
        self.check_cancel()
        if views.is_cuda:
            torch.cuda.empty_cache()
        if retain:
            return self._encode_batch_group(views, features, False)
        middle = len(views) // 2
        parts = [self._encode_batch_group(views[start:end],
                     None if features is None else features[start:end], False)
                 for start, end in ((0, middle), (middle, len(views)))]
        return torch.cat(parts)

    def predict_batch(self, documents, variants=None, *, augmentation_seed=None):
        """Encode independent views together, then pool each PDF separately.

        Callers must backpropagate the mean document loss once: documents can share
        an encoder graph, but neither their attention nor their targets are mixed.
        Frozen responses retain the existing per-document cache and quantization.
        """
        self.check_cancel()
        documents = list(documents)
        variants = [0] * len(documents) if variants is None else list(variants)
        if len(variants) != len(documents):
            raise ValueError("Una variante deve corrispondere a ogni documento")
        if not documents:
            return []
        if not self.model.dynamic_responses:
            return [self.predict(document, variant, augmentation_seed=augmentation_seed)
                    for document, variant in zip(documents, variants)]
        counts = [len(document["images"]) * len(self.model.view_names) for document in documents]
        if any(count == 0 for count in counts):
            raise ValueError("Un documento deve contenere almeno una pagina")
        group_size, retain = self._batch_memory_plan(sum(counts))
        capture_due = getattr(self.on_activity, "capture_due", None)
        capture = bool(self.engine_version >= 3 and capture_due is not None and capture_due())
        activity_views = [None] * len(documents)
        states, pending_views, pending_features = [], [], []
        pending_count = 0

        def flush():
            nonlocal pending_count
            self.check_cancel()
            # These are already on the device; concatenation does not turn a
            # pinned transfer into a synchronous pageable-memory copy.
            views = torch.cat(pending_views) if len(pending_views) > 1 else pending_views[0]
            features = torch.cat(pending_features) if pending_features else None
            states.append(self._encode_batch_group(views, features, retain))
            self.check_cancel()
            pending_views.clear()
            pending_features.clear()
            pending_count = 0

        for index, (document, variant) in enumerate(zip(documents, variants)):
            for page in range(len(document["images"])):
                self.check_cancel()
                request = self._page_request(document, page, variant, augmentation_seed)
                views = self._prefetch.take(request) if self._prefetch is not None else self._prepare_page(request)
                self.check_cancel()
                if capture and page == 0:
                    activity_views[index] = views[:1].clone()
                features = (self.feature_cache.get(document["image_hashes"][page], views)
                            if self.feature_cache is not None and variant == 0 else None)
                for start in range(0, len(views), group_size):
                    page_views_chunk = views[start:start + group_size]
                    chunk = page_views_chunk.to(self.device, non_blocking=page_views_chunk.is_pinned())
                    chunk_features = None
                    if features is not None:
                        chunk_features = features[start:start + len(chunk)].to(self.device)
                    elif self.model.color_features and not self.model.learned_visual_input:
                        from gustatory import visual_features
                        chunk_features = visual_features(chunk, self.model.feature_version)
                    consumed = 0
                    while consumed < len(chunk):
                        end = min(len(chunk), consumed + group_size - pending_count)
                        pending_views.append(chunk[consumed:end])
                        if chunk_features is not None:
                            pending_features.append(chunk_features[consumed:end])
                        pending_count += end - consumed
                        consumed = end
                        if pending_count == group_size:
                            flush()
            if not self.model.learned_visual_input:
                self.misses += 1
        if pending_count:
            flush()
        document_states = torch.cat(states).split(counts)
        predictions = []
        publish = getattr(self.on_activity, "publish_prediction", None)
        for index, (document, variant, values) in enumerate(zip(documents, variants, document_states)):
            self.check_cancel()
            prediction = self.model.classify_states(values)
            self.last_cached = False
            if publish is not None:
                publish(values, False, self.model, document, variant, activity_views[index], prediction[0])
            elif self.on_activity is not None:
                self.on_activity(values, False)
            predictions.append(prediction)
        return predictions


def input_fingerprint(documents):
    # Constant legacy field preserves signatures of visual-only v2 models; no text is read.
    values = [{k: d[k] for k in ("sha256", "label", "group_id", "image_hashes")}
              | dict(ocr_hash=hashlib.sha256(b"").hexdigest()) for d in documents]
    return hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()


def review_identities(reviews_path):
    """Retain identity history separately from the latest trainable correction."""
    if not reviews_path:
        return []
    path = Path(reviews_path)
    rows = json.loads(path.read_text(encoding="utf-8-sig"))
    history = Path(str(path) + ".identities.json")
    if history.exists():
        rows += json.loads(history.read_text(encoding="utf-8-sig"))
    return [dict(sha256=row["sha256"].upper(), group_id=row.get("group_id", "")) for row in rows]


def apply_training_reviews(training, validation, reviews_path, protected_test=(), engine_version=ENGINE_VERSION,
                           *, prepare_many=None):
    if not reviews_path:
        return training
    reviews = json.loads(Path(reviews_path).read_text(encoding="utf-8-sig"))
    # Validation/test feedback must never silently become training examples or labels.
    allowed = {d["sha256"].upper() for d in training}
    holdout = list(validation) + list(protected_test)
    protected = {d["sha256"].upper() for d in holdout}
    protected_groups = {d["group_id"] for d in holdout}
    identities = review_identities(reviews_path)
    # Use all historical identities before choosing the latest labels. A held-out
    # PDF submitted under another student ID also protects that student's other PDFs.
    changed = True
    while changed:
        before = (len(protected), len(protected_groups))
        for row in identities:
            digest, group = row["sha256"].upper(), row.get("group_id")
            if digest in protected or group in protected_groups:
                protected.add(digest)
                if group:
                    protected_groups.add(group)
        changed = before != (len(protected), len(protected_groups))
    if any(d["sha256"].upper() in protected or d["group_id"] in protected_groups for d in training):
        raise ValueError("Contaminazione: le revisioni collegano studenti di training e validazione/test")
    latest = {}
    for row in sorted(reviews, key=lambda row: datetime.fromisoformat(row["reviewed_at"]) if row.get("reviewed_at") else datetime.min.replace(tzinfo=timezone.utc)):
        latest[row["sha256"].upper()] = row
    corrections = {}
    additions = {}
    unreadable = set()
    for row in latest.values():
        digest = row["sha256"].upper()
        if row.get("split") not in ("training", "inference") or digest in protected or row.get("group_id") in protected_groups:
            continue
        if row.get("document_annotation", {}).get("readability") == "unreadable":
            unreadable.add(digest)
            continue
        if row.get("include_in_training", True) is False:
            continue
        if row["corrected_label"] == "DA_VERIFICARE":
            continue
        label = canonical_label(row["corrected_label"])
        if engine_version == 2 and label == "RICEVUTA":
            label = "RICEVUTA_PERMESSO"
        if label not in (LABELS if engine_version == 2 else DOCUMENT_LABELS):
            raise ValueError("Categoria della revisione non valida")
        if digest in allowed:
            corrections[digest] = label
        elif row.get("include_in_training") is True:
            group = row.get("group_id", "")
            if not group or group in protected_groups:
                continue
            additions[digest] = dict(path=row.get("prepared_path") or row["path"], sha256=digest,
                group_id=group, label=label, label_source="human-reviewed-training")
    added = (prepare_many(list(additions.values())) if prepare_many is not None
             else [prepare_document(row) for row in additions.values()])
    result = [d | dict(label=corrections[d["sha256"].upper()], label_source="human-reviewed-training")
              if d["sha256"].upper() in corrections else d for d in training
              if d["sha256"].upper() not in unreadable] + added
    from visual_brain import ensure_disjoint
    ensure_disjoint(result, holdout)
    return result
