"""Frozen ImageNet vision features adapted to Droso's sensory neurons."""
import copy
import hashlib
import json

import torch
from torch import nn
from torch.nn import functional as F

from resnet_document_model import PRETRAINING, torchvision_resnet18


INPUT_VERSION = "droso-resnet18-rgb384-grid2x2-adapter64-v1"
FEATURE_DIMENSION = 2048
ADAPTER_HIDDEN = 64


def ancestry_sha256(ancestry):
    return hashlib.sha256(json.dumps(ancestry, sort_keys=True, separators=(",", ":")).encode()).hexdigest().upper()


def visual_metadata(input_count, encoder_source=None):
    metadata = dict(input_version=INPUT_VERSION, encoder="resnet18", pretraining=PRETRAINING,
                    feature_dimension=FEATURE_DIMENSION, spatial_pool=[2, 2],
                    adapter_hidden=ADAPTER_HIDDEN, input_count=input_count,
                    frozen_encoder=True, no_ocr=True)
    if encoder_source is not None:
        metadata["encoder_source"] = copy.deepcopy({key: value for key, value in encoder_source.items()
                                                    if key != "ancestry"})
        if "ancestry" in encoder_source:
            metadata["encoder_source"]["ancestry_sha256"] = ancestry_sha256(encoder_source["ancestry"])
    return metadata


class LearnedVisualInput(nn.Module):
    """Keep every RGB view and its coarse layout, then learn a bounded drive."""

    def __init__(self, input_count: int, pretrained_state=None, *, backbone_state=None, encoder_source=None):
        super().__init__()
        if not isinstance(input_count, int) or isinstance(input_count, bool) or input_count <= 0:
            raise ValueError("Learned visual input requires a positive sensory neuron count")
        self.input_count = input_count
        self.encoder_source = copy.deepcopy(encoder_source)
        if pretrained_state is not None and backbone_state is not None:
            raise ValueError("Specify one pretrained encoder state")
        network = torchvision_resnet18()
        if pretrained_state is not None:
            network.load_state_dict(pretrained_state, strict=True)
        # Exclude ResNet's global pooling and classification head. The 2x2 grid
        # keeps top/bottom and left/right features from the entire 384px view.
        self.backbone = nn.Sequential(*list(network.children())[:-2])
        if backbone_state is not None:
            self.backbone.load_state_dict(backbone_state, strict=True)
        self.backbone.requires_grad_(False)
        self.backbone.eval()
        self.pool = nn.AdaptiveAvgPool2d((2, 2))
        self.register_buffer("rgb_mean", torch.tensor([.485, .456, .406]).view(1, 3, 1, 1))
        self.register_buffer("rgb_std", torch.tensor([.229, .224, .225]).view(1, 3, 1, 1))
        self.adapter = nn.Sequential(
            nn.Linear(FEATURE_DIMENSION, ADAPTER_HIDDEN),
            nn.ReLU(),
            nn.Linear(ADAPTER_HIDDEN, input_count),
            nn.Tanh(),
        )

    def train(self, mode=True):
        super().train(mode)
        # Both pretrained weights and running BatchNorm statistics stay fixed.
        self.backbone.eval()
        return self

    def encode(self, pages):
        """Return detached channel-major features: TL, TR, BL, BR per channel."""
        if not isinstance(pages, torch.Tensor) or pages.ndim != 4 or \
                pages.shape[0] == 0 or tuple(pages.shape[1:]) != (4, 384, 384):
            raise ValueError("Learned visual input requires pages shaped [N, 4, 384, 384]")
        if not torch.is_floating_point(pages):
            raise ValueError("Learned visual input requires floating point pixels in [0, 1]")
        if not bool(torch.isfinite(pages).all()):
            raise ValueError("Learned visual input requires finite pixels")
        if bool((pages < 0).any()) or bool((pages > 1).any()):
            raise ValueError("Learned visual input requires pixels in [0, 1]")
        with torch.no_grad():
            rgb = pages[:, 1:4].to(dtype=self.rgb_mean.dtype)
            rgb = (rgb - self.rgb_mean) / self.rgb_std
            features = self.pool(self.backbone(rgb)).flatten(1)
            return F.layer_norm(features, (FEATURE_DIMENSION,))

    def forward(self, pages):
        return self.adapter(self.encode(pages))

    def metadata(self):
        return visual_metadata(self.input_count, self.encoder_source)
