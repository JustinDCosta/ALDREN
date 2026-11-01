"""
Synthetic training speech for the wake word: positives (respellings) and near-miss negatives.

sherpa-onnx 2.0 no longer ships a phonemiser, and Supertonic mispronounces the name ("Al drum", "Bildroll"), so
clips come from Piper's LibriTTS-R model (904 speakers) fed hand-written phonemes. A check through Parakeet heard
"Aldrin" in 8 of 9 such clips.
"""

import argparse
import random
import wave
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass
from pathlib import Path

import numpy as np

# Spellings of the wake word ("al-drin") and the phonemes Piper says them with. "Aldrin" sounds exactly like the
# wake word as the user says it, so it is a positive here rather than a near miss.
WAKE_PHONEMES = {
    "Aldren": "ˈɔːldɹɪn",
    "ALDREN": "ˈɔːldɹɪn",
    "Al-drin": "ˈɑːldɹɪn",
    "All-drin": "ˈɔːldɹɪn",
    "Aldrin": "ˈɔːldɹɪn",
    "Al drin": "ˈæl dɹˈɪn",
    "Ahl-drin": "ˈɑːldɹɪn",
    "Aldren?": "ˈɔːldɹɪn?",
    "Aldren!": "ˈɔːldɹɪn!",
    "Aldren.": "ˈældɹɪn.",
}

# Words and phrases that sound close without being the wake word.
NEAR_MISS_PHONEMES = {
    "Alden": "ˈɔːldən",
    "all then": "ˈɔːl ðˈɛn",
    "Darren": "dˈæɹən",
    "children": "tʃˈɪldɹən",
    "Aldo": "ˈɑːldoʊ",
    "Aldi": "ˈɑːldi",
    "alder": "ˈɔːldɚ",
    "older": "ˈoʊldɚ",
    "all drink": "ˈɔːl dɹˈɪŋk",
    "all done": "ˈɔːl dˈʌn",
    "Allen": "ˈælən",
    "Elton": "ˈɛltən",
    "all in": "ˈɔːl ˈɪn",
    "Darwin": "dˈɑːɹwɪn",
    "golden": "ɡˈoʊldən",
    "seldom": "sˈɛldəm",
    "Holden": "hˈoʊldən",
    "Baldwin": "bˈɔːldwɪn",
    "Aldridge": "ˈɔːldɹɪdʒ",
    "Alder Lake": "ˈɔːldɚ lˈeɪk",
    "Aldous": "ˈɔːldəs",
    "Warren": "wˈɔːɹən",
    "Aaron": "ˈɛɹən",
    "all right then": "ˈɔːl ɹˈaɪt ðˈɛn",
    "Al and Darren": "ˈæl ænd dˈæɹən",
    "hold on": "hˈoʊld ˈɑːn",
    "all the drinks": "ˈɔːl ðə dɹˈɪŋks",
    "Albert": "ˈælbɚt",
}

WAKE_TEXTS = list(WAKE_PHONEMES)
NEAR_MISS_TEXTS = list(NEAR_MISS_PHONEMES)
PHONEMES = WAKE_PHONEMES | NEAR_MISS_PHONEMES
SAMPLE_RATE = 16_000


def wake_texts() -> list[str]:
    return list(WAKE_TEXTS)


def near_miss_texts() -> list[str]:
    return list(NEAR_MISS_TEXTS)


@dataclass(frozen=True)
class VoiceSet:
    """A Piper multi-speaker model and the ranges clips are drawn from."""

    model_dir: Path = Path(r"E:\AldrenData\voice\models\vits-piper-en_US-libritts_r-medium")
    speakers: int = 904
    speed_range: tuple[float, float] = (0.8, 1.2)
    threads: int = 2


def _render(job: tuple[list[str], Path, VoiceSet, int, int, int]) -> list[Path]:
    """Renders `count` clips starting at index `first` in one worker process (each worker loads the model once)."""
    import sherpa_onnx
    from scipy.signal import resample_poly

    texts, out_dir, voices, first, count, seed = job
    tts = sherpa_onnx.OfflineTts(sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(
            vits=sherpa_onnx.OfflineTtsVitsModelConfig(
                model=str(voices.model_dir / "en_US-libritts_r-medium.onnx"), tokens=str(voices.model_dir / "tokens.txt")),
            num_threads=voices.threads)))
    rng = random.Random(seed)
    paths = []
    for i in range(first, first + count):
        text = texts[i % len(texts)]
        config = sherpa_onnx.GenerationConfig()
        config.sid = rng.randrange(voices.speakers)
        config.speed = rng.uniform(*voices.speed_range)
        config.phoneme_codepoints = [[ord(c) for c in PHONEMES[text]]]
        audio = tts.generate("x", config)
        samples = resample_poly(np.asarray(audio.samples, dtype=np.float32), SAMPLE_RATE, audio.sample_rate)
        path = out_dir / f"{i:05d}.wav"
        with wave.open(str(path), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(SAMPLE_RATE)
            w.writeframes((np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes())
        paths.append(path)
    return paths


def generate(texts: list[str], out_dir: Path, voices: VoiceSet, count: int, workers: int = 3) -> list[Path]:
    """Writes `count` 16 kHz clips cycling through `texts` with random speakers and speeds."""
    out_dir.mkdir(parents=True, exist_ok=True)
    per_worker = -(-count // workers)
    jobs = [(texts, out_dir, voices, w * per_worker, min(per_worker, count - w * per_worker), 1000 + w)
            for w in range(workers) if w * per_worker < count]
    with ProcessPoolExecutor(max_workers=len(jobs)) as pool:
        return [p for paths in pool.map(_render, jobs) for p in paths]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="aldren-synth", description="Synthesises wake-word and near-miss clips.")
    parser.add_argument("kind", choices=["wake", "near"])
    parser.add_argument("--count", type=int, default=20_000)
    parser.add_argument("--out", type=Path, default=Path(r"E:\AldrenData\voice\synthetic"))
    parser.add_argument("--workers", type=int, default=3)
    args = parser.parse_args(argv)
    texts = wake_texts() if args.kind == "wake" else near_miss_texts()
    paths = generate(texts, args.out / args.kind, VoiceSet(), args.count, args.workers)
    print(f"{len(paths)} {args.kind} clips in {args.out / args.kind}")
    return 0
