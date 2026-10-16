"""Versioned training policy; existing checkpoints retain their original optimizer."""
import random

import torch


LEGACY_TRAINING_RECIPE = dict(version=1, warmup_epochs=1, brain_learning_rate=.0001,
                              scheduler_factor=.5, scheduler_patience=1,
                              reviewed_errato_max_repeats=4, reviewed_errato_extra_fraction=.1)
NEW_TRAINING_RECIPE = LEGACY_TRAINING_RECIPE | dict(version=2, reviewed_errato_max_repeats=2)
BRAIN_PARAMETERS = frozenset(("gain", "time_constant", "bias", "plasticity_source", "plasticity_target",
                              "taste_encoder.weight", "taste_encoder.bias"))
TASTE_PARAMETERS = frozenset(("taste_encoder.weight", "taste_encoder.bias"))


def make_training_optimizer(model, config):
    recipe = config.get("training_recipe")
    if not recipe:
        return torch.optim.AdamW([p for p in model.parameters() if p.requires_grad],
                                 lr=config["learning_rate"], weight_decay=.0001), None
    if recipe not in (LEGACY_TRAINING_RECIPE, NEW_TRAINING_RECIPE):
        raise ValueError("Ricetta di training non supportata: usare la versione compatibile del programma")
    if config.get("architecture") == "resnet18":
        optimizer = torch.optim.AdamW([
            dict(params=model.readout.parameters(), lr=config["learning_rate"], name="classifier"),
            dict(params=model.backbone.parameters(), lr=config["backbone_learning_rate"], name="backbone")],
            weight_decay=.0001)
        scheduler = torch.optim.lr_scheduler.ReduceLROnPlateau(
            optimizer, mode="max", factor=recipe["scheduler_factor"],
            patience=recipe["scheduler_patience"], threshold=0., min_lr=1e-6)
        return optimizer, scheduler
    if config.get("learned_visual_input", False):
        optimizer = torch.optim.AdamW([
            dict(params=[parameter for parameter in model.parameters() if parameter.requires_grad],
                 lr=config["learning_rate"], name="classifier")], weight_decay=.0001)
        scheduler = torch.optim.lr_scheduler.ReduceLROnPlateau(
            optimizer, mode="max", factor=recipe["scheduler_factor"],
            patience=recipe["scheduler_patience"], threshold=0., min_lr=1e-6)
        return optimizer, scheduler
    head, brain, taste = [], [], []
    for name, parameter in model.named_parameters():
        if name in BRAIN_PARAMETERS:
            if config["adaptive"]:
                # Keep historical optimizer group membership even when the
                # independent encoder flag freezes these two parameters. AdamW
                # skips parameters without gradients; resume keeps its layout.
                brain.append(parameter)
            elif name in TASTE_PARAMETERS and config.get("taste_adaptive", False):
                taste.append(parameter)
        else:
            head.append(parameter)
    groups = [dict(params=head, lr=config["learning_rate"], name="classifier")]
    if brain:
        brain_rate = recipe["brain_learning_rate"] * (.5 if config.get("prudent_adaptation", False) else 1)
        groups.append(dict(params=brain, lr=brain_rate, name="brain"))
    if taste:
        groups.append(dict(params=taste, lr=config["learning_rate"], name="gustatory"))
    optimizer = torch.optim.AdamW(groups, weight_decay=.0001)
    scheduler = torch.optim.lr_scheduler.ReduceLROnPlateau(
        optimizer, mode="max", factor=recipe["scheduler_factor"],
        patience=recipe["scheduler_patience"], threshold=0., min_lr=1e-6)
    return optimizer, scheduler


def set_training_phase(model, config, epoch):
    recipe = config.get("training_recipe")
    if config.get("architecture") == "resnet18":
        model.warming = epoch < recipe["warmup_epochs"]
        for parameter in model.backbone.parameters():
            parameter.requires_grad_(not model.warming)
        model.train(model.training)
        return "classifier-warmup" if model.warming else "backbone-finetuning"
    if config.get("learned_visual_input", False):
        return "visual-adapter-and-classifier"
    adapting = config["adaptive"]
    warming = adapting and recipe is not None and epoch < recipe["warmup_epochs"]
    # Keep model.adaptive true during warmup: its responses must never enter the
    # frozen-brain disk cache. Static optimizer groups make resume unambiguous.
    for name, parameter in model.named_parameters():
        if name in BRAIN_PARAMETERS:
            # The absent flag is the historical constructor behavior, while
            # new V4 models save an explicit independent encoder choice.
            trainable = config.get("taste_adaptive", adapting) if name in TASTE_PARAMETERS else adapting
            parameter.requires_grad_(trainable and not warming)
    return ("classifier-warmup" if warming else "adaptive" if adapting else
            "classifier-and-taste" if config.get("taste_adaptive", False) else "frozen")


def training_example_weight(document, difficult, config):
    if not config["hard_examples"] or document["sha256"] not in difficult:
        return 1.
    if config.get("training_recipe") and document.get("label_source") != "human-reviewed-training":
        return 1.
    return 1.5


def training_epoch_documents(training, rejected, config, epoch):
    seed = config["seed"] + epoch
    weak_order = list(range(len(rejected)))
    random.Random(seed).shuffle(weak_order)
    documents = training + [rejected[i] for i in weak_order[:len(training)]]
    order = list(range(len(documents)))
    recipe = config.get("training_recipe")
    if recipe:
        confirmed = [i for i, document in enumerate(training)
                     if document["label"] == "ERRATO"
                     and document.get("label_source") == "human-reviewed-training"]
        counts = [sum(d["label"] == label for d in training)
                  for label in {d["label"] for d in training}]
        errato_count = sum(d["label"] == "ERRATO" for d in training)
        # Preserve every original example; never promote an unlabelled 04 to ERRATO.
        # The saved recipe bounds exposures; existing models retain their schedule.
        # The shared extra-work budget remains at most 10% per epoch.
        budget = min(int(len(training) * recipe["reviewed_errato_extra_fraction"]),
                     max(0, max(counts, default=0) - errato_count))
        rng = random.Random(seed)
        for _ in range(recipe["reviewed_errato_max_repeats"] - 1):
            rng.shuffle(confirmed)
            extras = confirmed[:budget]
            order.extend(extras)
            budget -= len(extras)
            if budget <= 0:
                break
    random.Random(seed).shuffle(order)
    return documents, order
