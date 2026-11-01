"""openWakeWord embedding windows for training and scoring, plus the augmentation that makes synthetic speech look real."""

import wave
from pathlib import Path

import numpy as np
from scipy.signal import butter, fftconvolve, sosfilt

MODELS = Path(r"E:\AldrenData\voice\models\openwakeword")
SAMPLE_RATE = 16_000
CLIP_SAMPLES = 2 * SAMPLE_RATE  # a 2 s example yields the classifier's 16 embedding frames (80 ms each)
WINDOW = 16


def extractor(ncpu: int = 4):
    from openwakeword.utils import AudioFeatures

    return AudioFeatures(melspec_model_path=str(MODELS / "melspectrogram.onnx"),
                         embedding_model_path=str(MODELS / "embedding_model.onnx"), ncpu=ncpu)


def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        return np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768.0


def speech_end(audio: np.ndarray) -> int:
    """Sample index just after the last loud 10 ms frame (the end of the spoken word)."""
    hop = SAMPLE_RATE // 100
    rms = np.array([np.sqrt(np.mean(audio[i:i + hop] ** 2)) for i in range(0, len(audio) - hop, hop)])
    if rms.size == 0:
        return len(audio)
    loud = np.flatnonzero(rms > max(np.percentile(rms, 20) * 8, 10 ** (-45 / 20)))
    return len(audio) if loud.size == 0 else min(len(audio), (loud[-1] + 1) * hop)


def place(speech: np.ndarray, rng: np.random.Generator, end: int | None = None, jitter: float = 0.2) -> np.ndarray:
    """A 2 s example whose speech ends 0–`jitter` s before the end, as the classifier sees it when it should fire."""
    end = len(speech) if end is None else end
    gap = int(rng.uniform(0, jitter) * SAMPLE_RATE)
    clip = np.zeros(CLIP_SAMPLES, np.float32)
    piece = speech[max(0, end - (CLIP_SAMPLES - gap)):end]
    clip[CLIP_SAMPLES - gap - len(piece):CLIP_SAMPLES - gap] = piece
    return clip


class Augmenter:
    """Room noise, reverb, microphone EQ and level changes, each applied with some probability."""

    def __init__(self, noise: np.ndarray, rng: np.random.Generator) -> None:
        self.noise = noise
        self.rng = rng

    def __call__(self, clip: np.ndarray) -> np.ndarray:
        rng = self.rng
        x = clip.copy()
        if rng.random() < 0.4:
            x = self._reverb(x)
        if rng.random() < 0.5:
            x = self._eq(x)
        peak = np.max(np.abs(x)) or 1.0
        x *= 10 ** (rng.uniform(-24, -3) / 20) / peak
        x = self._noise(x)
        return np.clip(x, -1.0, 1.0).astype(np.float32)

    def _reverb(self, x: np.ndarray) -> np.ndarray:
        rt60 = self.rng.uniform(0.15, 0.6)
        n = int(rt60 * SAMPLE_RATE)
        rir = self.rng.standard_normal(n) * np.exp(-6.9 * np.arange(n) / n)
        rir[0] = 1.0
        wet = fftconvolve(x, rir / np.max(np.abs(rir)))[: len(x)]
        return wet / (np.max(np.abs(wet)) or 1.0) * (np.max(np.abs(x)) or 1.0)

    def _eq(self, x: np.ndarray) -> np.ndarray:
        if self.rng.random() < 0.7:
            x = sosfilt(butter(2, self.rng.uniform(80, 300), "highpass", fs=SAMPLE_RATE, output="sos"), x)
        if self.rng.random() < 0.7:
            x = sosfilt(butter(2, self.rng.uniform(3500, 7500), "lowpass", fs=SAMPLE_RATE, output="sos"), x)
        return x.astype(np.float32)

    def _noise(self, x: np.ndarray) -> np.ndarray:
        signal = np.sqrt(np.mean(x ** 2)) or 1e-4
        if self.rng.random() < 0.7 and len(self.noise) > len(x):
            start = self.rng.integers(0, len(self.noise) - len(x))
            noise = self.noise[start:start + len(x)]
            snr = self.rng.uniform(5, 30)
        else:
            noise = self.rng.standard_normal(len(x)).astype(np.float32)
            snr = self.rng.uniform(20, 45)
        level = np.sqrt(np.mean(noise ** 2)) or 1e-4
        return x + noise * (signal / level) * 10 ** (-snr / 20)


def windows(features, clips: np.ndarray, batch_size: int = 256) -> np.ndarray:
    """(N, 32000) float clips -> (N, 16, 96) float16 embedding windows."""
    pcm = (np.clip(clips, -1, 1) * 32767).astype(np.int16)
    embeddings = features.embed_clips(pcm, batch_size=batch_size, ncpu=4)
    return embeddings[:, -WINDOW:, :].astype(np.float16)


def frame_embeddings(features, audio: np.ndarray) -> np.ndarray:
    """All embedding frames of one longer clip, for sliding the classifier over it."""
    pcm = (np.clip(audio, -1, 1) * 32767).astype(np.int16)[None, :]
    return features.embed_clips(pcm, batch_size=1, ncpu=4)[0]
