"""Throttled summaries and complete, binary-encoded responses for the WPF display."""
import base64
import math
import time
from pathlib import Path

import torch

from visual_brain import emit


class VisualTelemetry:
    def __init__(self, neuron_ids, steps, send=emit, clock=time.monotonic, detail_file=None):
        self.ids = neuron_ids.detach().to(device="cpu", dtype=torch.int64).clone() if neuron_ids is not None else None
        self.steps, self.send, self.clock = steps, send, clock
        self.next_frame = 0.
        self.detail_file = Path(detail_file) if detail_file else None
        self.detail_token = None
        self.sent_ids = False
        self.encoded_ids = None
        self.next_insight = 0.
        self.insight_interval = 10.
        self.trace_buffer = None
        self.metrics = None

    def set_metrics(self, metrics):
        """Snapshot supplied by the training loop, never inferred from activity."""
        def finite(value):
            return value is None or isinstance(value, str) or (isinstance(value, (int, float)) and math.isfinite(value))
        self.metrics = {name: value for name, value in metrics.items() if finite(value)
                        and name in ("epoch", "epochs", "phase", "loss", "validation_f1", "taste_update", "classifier_update")}
        self.metrics["history"] = [{name: value for name, value in row.items()
                                    if name in ("epoch", "loss", "validation_f1") and finite(value)}
                                   for row in metrics.get("history", [])[-40:]]

    def capture_due(self):
        return self.ids is not None and self._detail_token() is not None and self.clock() >= self.next_insight

    def publish_prediction(self, states, cached, model, document, variant, view, logits):
        insight = None
        if view is not None and self.capture_due():
            from activity_insights import DeviceTraceBuffer, build_insight
            if self.trace_buffer is None or not self.trace_buffer.matches(model):
                self.trace_buffer = DeviceTraceBuffer(model)
            states, insight = build_insight(model, document, variant, view, logits, trace_buffer=self.trace_buffer)
            self.next_insight = self.clock() + self.insight_interval
            if self.metrics is not None:
                insight["metrics"] = self.metrics.copy()
        self._publish(states, cached, insight, prediction_context=True)

    def _detail_token(self):
        if self.detail_file is not None:
            try:
                status = self.detail_file.stat()
                return status.st_mtime_ns, status.st_ino
            except OSError:
                pass
        return None

    def __call__(self, states, cached):
        self._publish(states, cached)

    def _publish(self, states, cached, insight=None, *, prediction_context=False):
        now = self.clock()
        if self.ids is None or (now < self.next_frame and insight is None):
            return
        self.next_frame = now + .25
        token = self._detail_token()
        if token != self.detail_token:
            self.detail_token, self.sent_ids = token, False
        # Context-free legacy callbacks keep their old four-Hz detail protocol.
        # Document callbacks publish full vectors only with a matching insight.
        detailed = token is not None and (insight is not None or not prediction_context)
        # These are final graded responses, not spikes or a recording of dopamine.
        with torch.no_grad():
            magnitude = states.detach().abs().mean(0)
            active = int((magnitude > .01).sum())
            frame = dict(step=self.steps, total_steps=self.steps, active_count=active,
                         mean_absolute=float(magnitude.mean()),
                         details_included=detailed,
                         total_neurons=len(self.ids), cached=bool(cached),
                         kind="graded_final", activity_threshold=.01)
            if detailed and insight is not None:
                frame.update(insight=insight, kind="graded_view_final")
            if detailed:
                # At most four completed responses per second, without sorting or per-neuron objects.
                values = magnitude.to(device="cpu", dtype=torch.float32).numpy().astype("<f4", copy=False)
                frame.update(encoding="f32le-base64-v1",
                             magnitudes_f32le=base64.b64encode(values.tobytes()).decode("ascii"))
                if not self.sent_ids:
                    if self.encoded_ids is None:
                        self.encoded_ids = base64.b64encode(self.ids.numpy().astype("<i8", copy=False).tobytes()).decode("ascii")
                    frame["neuron_ids_i64le"] = self.encoded_ids
                self.sent_ids = True
            else:
                frame.update(neuron_ids=[], magnitudes=[])
        self.send("Attività visiva", activity=frame)
