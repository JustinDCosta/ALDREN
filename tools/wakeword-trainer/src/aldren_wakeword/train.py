"""aldren-train: builds embedding windows from the clips and recordings, then trains the wake-word classifier."""

import argparse
import json
import random
import time
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np

from aldren_wakeword import features, model

DATA = Path(r"E:\AldrenData\voice")
FEATURES = DATA / "features"
SESSION = DATA / "recordings" / "2026-11-01"
ACAV = DATA / "trainer-data" / "openwakeword_features_ACAV100M_2000_hrs_16bit.npy"
FIELD = DATA / "field"


@dataclass
class TrainReport:
    steps: int
    positives: int
    hard_negatives: int
    seconds: float
    final_loss: float
    model: str


def split_session(session: Path, seed: int = 7) -> dict[str, list[str]]:
    """Per delivery cue, 6 wake-word takes train and 3 are held out; a third of the commands are held out too."""
    entries = [json.loads(l) for l in (session / "manifest.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
    rng = random.Random(seed)
    split = {"wake_train": [], "wake_test": [], "command_train": [], "command_test": [], "noise": []}
    for cue in sorted({e["cue"] for e in entries if e["kind"] == "wake"}):
        takes = [e["file"] for e in entries if e["kind"] == "wake" and e["cue"] == cue]
        rng.shuffle(takes)
        split["wake_train"] += takes[:6]
        split["wake_test"] += takes[6:]
    commands = [e["file"] for e in entries if e["kind"] == "command"]
    rng.shuffle(commands)
    split["command_train"], split["command_test"] = commands[: len(commands) * 2 // 3], commands[len(commands) * 2 // 3:]
    split["noise"] = [e["file"] for e in entries if e["kind"] == "noise"]
    return split


def prepare(synthetic: Path = DATA / "synthetic", session: Path = SESSION, out: Path = FEATURES, copies: int = 100,
            personal_only: bool = False) -> None:
    """Writes the training windows (.npy, float16) and the held-out split (split.json)."""
    out.mkdir(parents=True, exist_ok=True)
    rng = np.random.default_rng(11)
    split = split_session(session)
    (out / "split.json").write_text(json.dumps(split, indent=1), encoding="utf-8")
    noise = np.concatenate([features.read_wav(session / f) for f in split["noise"]])
    augment = features.Augmenter(noise, rng)
    extract = features.extractor()

    def build(name: str, clips: list[np.ndarray]) -> None:
        batch = np.stack(clips)
        np.save(out / f"{name}.npy", features.windows(extract, batch))
        print(f"{name}: {len(batch)} windows")

    for kind in () if personal_only else ("wake", "near"):
        paths = sorted((synthetic / kind).glob("*.wav"))
        chunk = 4000
        parts = []
        for start in range(0, len(paths), chunk):
            clips = []
            for path in paths[start:start + chunk]:
                speech = features.read_wav(path)
                clips.append(augment(features.place(speech, rng, features.speech_end(speech))))
            parts.append(features.windows(extract, np.stack(clips)))
            print(f"{kind}_synth: {start + len(clips)}/{len(paths)}")
        np.save(out / f"{kind}_synth.npy", np.concatenate(parts))

    for name, files in (("wake_personal", split["wake_train"]), ("command_personal", split["command_train"])):
        clips = []
        for f in files:
            take = features.read_wav(session / f)
            end = features.speech_end(take)
            clips += [augment(features.place(take, rng, end)) for _ in range(copies)]
        build(name, clips)

    hop = features.SAMPLE_RATE // 4
    build("noise", [augment(noise[i:i + features.CLIP_SAMPLES]) for i in range(0, len(noise) - features.CLIP_SAMPLES, hop)])


def split_field(clips, holdout: float = 0.25) -> tuple[list[str], list[str]]:
    """The newest quarter of the field false alarms (by day folder, then time) is held out to measure the retrained model on."""
    ordered = sorted(clips)
    cut = len(ordered) - max(1, round(len(ordered) * holdout)) if ordered else 0
    return ordered[:cut], ordered[cut:]


def prepare_field(field: Path = FIELD, out: Path = FEATURES, copies: int = 4) -> None:
    """
    Windows from the field false alarms. Each clip is the 2 s around a trigger, recorded in the room on the real microphone,
    so the only augmentation is the level. The held-out clips are listed in field_split.json.
    """
    kept, held_out = split_field(p.relative_to(field).as_posix() for p in field.glob("*/trigger-*.wav"))
    (out / "field_split.json").write_text(json.dumps({"train": kept, "test": held_out}, indent=1), encoding="utf-8")
    rng = np.random.default_rng(13)
    clips = []
    for name in kept:
        audio = features.read_wav(field / name)[-features.CLIP_SAMPLES:]
        audio = np.pad(audio, (features.CLIP_SAMPLES - len(audio), 0))
        clips += [np.clip(audio * 10 ** (rng.uniform(-6, 6) / 20), -1, 1) for _ in range(copies)]
    np.save(out / "field.npy", features.windows(features.extractor(), np.stack(clips)))
    print(f"field: {len(kept)} clips kept ({len(clips)} windows), {len(held_out)} held out")


VALIDATION = DATA / "trainer-data" / "validation_set_features.npy"


def selection_stream() -> np.ndarray:
    """First half of the 10.7 h validation stream: picks checkpoints. The second half only ever reports."""
    stream = np.load(VALIDATION, mmap_mode="r")
    return np.asarray(stream[: len(stream) // 2])


def train(positive_dir: Path, negative_features: list[Path], out_model: Path, epochs: int) -> TrainReport:
    """
    openWakeWord's recipe: each batch is mostly general audio (1,024 windows from a 1M-window slice of the 2,000 h
    set) plus 64 hard negatives (near misses, the user's commands, room noise) and 64 positives; the negative weight
    ramps from 1 to 1,000. Three stages at falling learning rates; checkpoints are scored on half the validation
    stream (false accepts/h at 0.5) and on held-out synthetic positives, and the best one under 0.5/h is kept.
    `epochs` scales the first stage (1 epoch = 1,000 steps).
    """
    import copy

    import torch
    from numpy.lib.stride_tricks import sliding_window_view

    torch.manual_seed(5)
    started = time.time()
    rng = np.random.default_rng(3)
    synth = np.load(positive_dir / "wake_synth.npy")
    held = rng.choice(len(synth), 1000, replace=False)
    val_synth = torch.from_numpy(synth[held].astype(np.float32))
    synth = np.delete(synth, held, axis=0)
    # ALDREN only has to wake for its user, so half of every positive batch is the user's own (augmented) takes.
    personal = np.load(positive_dir / "wake_personal.npy")
    val_personal = torch.from_numpy(personal[rng.choice(len(personal), min(1000, len(personal)), replace=False)].astype(np.float32))
    positives = np.concatenate([synth, personal])
    hard_parts = [np.load(p) for p in negative_features if p != ACAV]
    # The user's own commands and the field false alarms are the closest negatives there are (same voice or room, same
    # microphone): weight them up.
    hard = np.concatenate([np.repeat(h, 8, axis=0) if "command" in str(p) or "field" in str(p) else h
                           for h, p in zip(hard_parts, [p for p in negative_features if p != ACAV])])
    acav_map = np.load(ACAV, mmap_mode="r")
    rows = np.sort(rng.choice(len(acav_map), 1_000_000, replace=False))
    general = np.asarray(acav_map[rows])
    print(f"loaded {len(general)} general-audio windows in {time.time() - started:.0f} s")
    selection = sliding_window_view(selection_stream(), (16, 96))[:, 0]  # a view: converted 50,000 windows at a time
    selection_hours = (len(selection) + 15) * 0.08 / 3600

    net = model.build_torch()
    best: tuple[float, float, dict] | None = None  # (recall, -fa_per_hour, state)

    def checkpoint(step_label: str) -> None:
        nonlocal best
        net.eval()
        with torch.no_grad():
            recall = 0.5 * (float((torch.sigmoid(net(val_synth)[:, 0]) >= 0.5).float().mean())
                            + float((torch.sigmoid(net(val_personal)[:, 0]) >= 0.5).float().mean()))
            scores = torch.cat([torch.sigmoid(net(torch.from_numpy(np.ascontiguousarray(selection[i:i + 50_000], dtype=np.float32)))[:, 0])
                                for i in range(0, len(selection), 50_000)]).numpy()
        from aldren_wakeword.metrics import false_accepts_per_hour

        fa = false_accepts_per_hour(scores, 0.5, selection_hours)
        net.train()
        if fa <= 0.5 and (best is None or recall > best[0]):
            best = (recall, -fa, copy.deepcopy(net.state_dict()))
        print(f"{step_label}: recall {recall:.3f}, false accepts {fa:.2f}/h{'  <- kept' if best and best[2] is not None and best[0] == recall else ''}")

    first = max(1, epochs) * 1000
    stages = [(first, 1e-4, 1000.0), (first // 10, 1e-5, 2000.0), (first // 10, 1e-6, 2000.0)]
    loss_value, total = 0.0, 0
    for stage, (steps, lr, max_weight) in enumerate(stages, start=1):
        optimizer = torch.optim.Adam(net.parameters(), lr=lr)
        weights = np.linspace(1.0, max_weight, steps)
        checks = set(np.linspace(steps * 3 // 4 if stage == 1 else 1, steps - 1, 8).astype(int))
        for step in range(steps):
            warm = min(1.0, (step + 1) / max(1, steps // 5))  # linear warm-up over the first fifth
            for group in optimizer.param_groups:
                group["lr"] = lr * warm
            x = np.concatenate([synth[rng.integers(0, len(synth), 32)], personal[rng.integers(0, len(personal), 32)],
                                hard[rng.integers(0, len(hard), 64)], general[rng.integers(0, len(general), 1024)]])
            y = np.concatenate([np.ones(64), np.zeros(64 + 1024)])
            xt, yt = torch.from_numpy(x.astype(np.float32)), torch.from_numpy(y.astype(np.float32))
            w = torch.where(yt > 0, torch.tensor(1.0), torch.tensor(float(weights[step])))
            loss = torch.nn.functional.binary_cross_entropy_with_logits(net(xt)[:, 0], yt, weight=w)
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            loss_value, total = float(loss), total + 1
            if step in checks:
                checkpoint(f"stage {stage} step {step}")

    if best is not None:
        net.load_state_dict(best[2])
    net.eval()
    model.save(net, out_model)
    report = TrainReport(total, len(positives), len(hard), time.time() - started, loss_value, str(out_model))
    out_model.with_suffix(".json").write_text(json.dumps(asdict(report) | {
        "selected_recall": best[0] if best else None, "selected_fa_per_hour": -best[1] if best else None}, indent=1), encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="aldren-train", description="Prepares windows and trains the wake-word model.")
    parser.add_argument("step", choices=["prepare", "prepare-field", "train"])
    parser.add_argument("--personal-only", action="store_true", help="rebuild only the recorded takes' windows")
    parser.add_argument("--epochs", type=int, default=50, help="first-stage length in thousands of steps")
    parser.add_argument("--out", type=Path, default=DATA / "models" / "aldren-wakeword.npz")
    args = parser.parse_args(argv)
    if args.step == "prepare":
        prepare(personal_only=args.personal_only)
        return 0
    if args.step == "prepare-field":
        prepare_field()
        return 0
    negatives = [FEATURES / n for n in ("near_synth.npy", "command_personal.npy", "noise.npy", "field.npy") if (FEATURES / n).exists()] + [ACAV]
    report = train(FEATURES, negatives, args.out, args.epochs)
    print(report)
    return 0
