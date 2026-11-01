"""aldren-eval: false rejects on held-out recordings, false accepts per hour on 10.7 h of other audio."""

import argparse
import datetime
import json
from pathlib import Path

import numpy as np
from numpy.lib.stride_tricks import sliding_window_view

from aldren_wakeword import features, metrics, model
from aldren_wakeword.train import DATA, FEATURES, FIELD, SESSION

VALIDATION = DATA / "trainer-data" / "validation_set_features.npy"
FRAME_SECONDS = 0.08
REFRACTORY_FRAMES = 20  # 1.6 s: activations closer together count once
THRESHOLDS = [0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.98, 0.99]


def take_scores(net, extract, session: Path, files: list[str]) -> np.ndarray:
    """Best score per take, sliding the classifier over the take padded with silence as live audio would be."""
    best = []
    for f in files:
        take = features.read_wav(session / f)
        audio = np.concatenate([np.zeros(features.SAMPLE_RATE, np.float32), take,
                                np.zeros(features.SAMPLE_RATE // 2, np.float32)])
        frames = features.frame_embeddings(extract, audio)
        best.append(float(net(sliding_window_view(frames, (16, 96))[:, 0]).max()))
    return np.array(best)


def validation_scores(net, block: int = 20_000) -> tuple[np.ndarray, float]:
    """Scores on the second half of the validation stream; the first half picked the checkpoint during training."""
    full = np.load(VALIDATION, mmap_mode="r")
    stream = full[len(full) // 2:]
    scores = [net(sliding_window_view(np.asarray(stream[s:s + block + 15]), (16, 96))[:, 0])
              for s in range(0, len(stream) - 15, block)]
    return np.concatenate(scores), len(stream) * FRAME_SECONDS / 3600


KWS = DATA / "models" / "sherpa-onnx-kws-zipformer-gigaspeech-3.3M-2024-01-01"
# The spotter's BPE vocabulary has no word-initial "AL"; like its own examples ("▁ L IGHT"), a lone "▁" starts the
# word. Without sentencepiece the segmentations are written by hand, several variants at once.
KWS_KEYWORDS = ["▁ AL D R EN :2.0 #0.15 @ALDREN", "▁ AL D R IN :2.0 #0.15 @ALDREN", "▁ALL D R IN :2.0 #0.15 @ALDREN",
                "▁ALL D R EN :2.0 #0.15 @ALDREN"]


def kws_detections(session: Path, files: list[str], threshold: float = 0.15) -> int:
    """Baseline: how many takes the open-vocabulary keyword spotter fires on (no training)."""
    import sherpa_onnx

    keywords = FEATURES / "kws-keywords.txt"
    keywords.write_text("\n".join(KWS_KEYWORDS) + "\n", encoding="utf-8")
    spotter = sherpa_onnx.KeywordSpotter(
        tokens=str(KWS / "tokens.txt"), encoder=str(KWS / "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx"),
        decoder=str(KWS / "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx"),
        joiner=str(KWS / "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx"),
        keywords_file=str(keywords), keywords_threshold=threshold, num_threads=2)
    detected = 0
    for f in files:
        stream = spotter.create_stream()
        audio = np.concatenate([features.read_wav(session / f), np.zeros(features.SAMPLE_RATE, np.float32)])
        stream.accept_waveform(features.SAMPLE_RATE, audio)
        stream.input_finished()
        hit = False
        while spotter.is_ready(stream):
            spotter.decode_stream(stream)
            hit = hit or bool(spotter.get_result(stream))
        detected += hit
    return detected


PARAKEET = DATA / "models" / "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8"
# How Parakeet writes the wake word as the user says it ("al-drin"), seen on synthetic and recorded takes.
WAKE_SPELLINGS = ("aldrin", "aldren", "aldrine", "eldrin", "aldran", "oldrin", "alldrin", "al drin", "all drin")


def transcript_confirms(text: str) -> bool:
    """Second stage: the transcript of the triggering clip must contain the wake word, give or take a letter."""
    words = "".join(c if c.isalpha() or c == " " else " " for c in text.lower())
    joined = " ".join(words.split())
    if any(s in joined for s in WAKE_SPELLINGS):
        return True
    return any(_distance(w, "aldrin") <= 2 for w in joined.split())


def _distance(a: str, b: str) -> int:
    row = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        previous, row[0] = row[0], i
        for j, cb in enumerate(b, 1):
            previous, row[j] = row[j], min(row[j] + 1, row[j - 1] + 1, previous + (ca != cb))
    return row[-1]


def transcripts(session: Path, files: list[str]) -> list[str]:
    import sherpa_onnx

    asr = sherpa_onnx.OfflineRecognizer.from_transducer(
        encoder=str(PARAKEET / "encoder.int8.onnx"), decoder=str(PARAKEET / "decoder.int8.onnx"),
        joiner=str(PARAKEET / "joiner.int8.onnx"), tokens=str(PARAKEET / "tokens.txt"), num_threads=4,
        model_type="nemo_transducer")
    texts = []
    for f in files:
        audio = np.concatenate([np.zeros(4000, np.float32), features.read_wav(session / f), np.zeros(8000, np.float32)])
        stream = asr.create_stream()
        stream.accept_waveform(features.SAMPLE_RATE, audio)
        asr.decode_stream(stream)
        texts.append(stream.result.text)
    return texts


def evaluate(model_path: Path, out_dir: Path = Path(r"E:\AldrenData\bench\s4")) -> dict:
    net = model.WakeModel(model_path)
    split = json.loads((FEATURES / "split.json").read_text(encoding="utf-8"))
    extract = features.extractor()
    wake = take_scores(net, extract, SESSION, split["wake_test"])
    commands = take_scores(net, extract, SESSION, split["command_test"])
    stream, hours = validation_scores(net)

    rows = []
    for t in THRESHOLDS:
        rows.append({"threshold": t, "frr": metrics.false_reject_rate(wake, t),
                     "fa_per_hour": metrics.false_accepts_per_hour(stream, t, hours, REFRACTORY_FRAMES),
                     "own_commands_triggering": int((commands >= t).sum())})
    eligible = [r for r in rows if r["fa_per_hour"] <= 0.5]
    chosen = min(eligible, key=lambda r: (r["frr"], r["threshold"])) if eligible else None

    lines = [f"## Wake-word evaluation {datetime.datetime.now():%Y-%m-%d %H:%M} ({model_path.name})",
             f"- Held-out wake-word takes: {len(wake)}; held-out commands: {len(commands)}; validation audio: {hours:.1f} h",
             "", "| Threshold | False rejects | False accepts per hour | Held-out commands triggering |", "|---|---|---|---|"]
    lines += [f"| {r['threshold']} | {r['frr']:.1%} | {r['fa_per_hour']:.2f} | {r['own_commands_triggering']}/{len(commands)} |" for r in rows]
    lines.append("")
    lines.append(f"Chosen threshold: {chosen['threshold']} ({chosen['frr']:.1%} false rejects, {chosen['fa_per_hour']:.2f} false accepts/h)"
                 if chosen else "No threshold reaches 0.5 false accepts per hour.")
    lines.append(f"Missed takes at the chosen threshold: {[f for f, s in zip(split['wake_test'], wake) if chosen and s < chosen['threshold']]}")
    field_split = FEATURES / "field_split.json"
    if field_split.exists():
        held_out = json.loads(field_split.read_text(encoding="utf-8"))["test"]
        field = take_scores(net, extract, FIELD, held_out)
        lines.append(f"Held-out field false alarms (the newest {len(held_out)}, all of which fired model 3 at 0.9) still firing: "
                     + ", ".join(f"{int((field >= t).sum())}/{len(field)} at {t}" for t in (0.5, 0.9, 0.99)))
    wake_texts = transcripts(SESSION, split["wake_test"])
    command_texts = transcripts(SESSION, split["command_test"])
    confirmed_wake = np.array([transcript_confirms(t) for t in wake_texts])
    confirmed_commands = np.array([transcript_confirms(t) for t in command_texts])
    lines += ["", "Second stage, Parakeet transcript check on the clip that triggered:",
              f"- confirms {confirmed_wake.sum()}/{len(wake)} held-out wake-word takes and {confirmed_commands.sum()}/{len(commands)} held-out commands",
              f"- heard on missed takes: {[t for t, ok in zip(wake_texts, confirmed_wake) if not ok]}",
              "", "| Threshold | Cascade false rejects | Held-out commands passing both stages |", "|---|---|---|"]
    for t in THRESHOLDS:
        lines.append(f"| {t} | {1 - float(((wake >= t) & confirmed_wake).mean()):.1%} | {int(((commands >= t) & confirmed_commands).sum())}/{len(commands)} |")
    kws_wake = kws_detections(SESSION, split["wake_test"])
    kws_commands = kws_detections(SESSION, split["command_test"])
    lines.append(f"Baseline, sherpa-onnx keyword spotter (no training): fired on {kws_wake}/{len(wake)} held-out wake-word takes "
                 f"({1 - kws_wake / len(wake):.1%} false rejects) and {kws_commands}/{len(commands)} held-out commands")
    report = "\n".join(lines)
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / f"eval-{datetime.datetime.now():%Y%m%d-%H%M}.md").write_text(report, encoding="utf-8")
    print(report)
    return {"rows": rows, "chosen": chosen}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="aldren-eval", description="Evaluates the wake-word model.")
    parser.add_argument("--model", type=Path, default=DATA / "models" / "aldren-wakeword.npz")
    args = parser.parse_args(argv)
    evaluate(args.model)
    return 0
