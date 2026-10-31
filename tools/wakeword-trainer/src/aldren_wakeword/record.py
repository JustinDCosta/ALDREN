"""aldren-record: guided recording session for the wake word (S4) and voice (S3) tests."""

import argparse
import datetime
import json
import os
import sys
import wave
from collections.abc import Callable
from pathlib import Path
from typing import Protocol

import numpy as np

from aldren_wakeword.prompts import Prompt, session_prompts

SAMPLE_RATE = 16_000
CLIP_PEAK = 0.99

# Asked of Windows, which converts from the device's own format. Through PortAudio's WASAPI path the PD300X (192 kHz)
# arrived at about half speed (93k frames/s) whatever rate was requested, which squeezed every take of the 2026-10-31
# session 2x in time. MME goes through the Windows audio engine and delivers on time; every read is timed regardless.
CAPTURE_RATE = 48_000
DEFAULT_HOST_API = "MME"


class DeviceLost(Exception):
    """The pinned input device vanished or changed format mid-session."""


class DeliveryRateError(Exception):
    """The device delivers audio faster or slower than its format says, so takes would be distorted."""


class Recorder(Protocol):
    def record(self, seconds: float) -> np.ndarray:
        """Returns `seconds` of 16 kHz mono float32 audio in [-1, 1]."""
        ...


class Session:
    """A session folder of WAV takes plus manifest.jsonl, rewritten after every take so a crash loses nothing."""

    def __init__(self, out_root: Path, name: str) -> None:
        self.folder = out_root / name
        self.folder.mkdir(parents=True, exist_ok=True)
        self._manifest = self.folder / "manifest.jsonl"
        self.entries: dict[str, dict] = {}
        if self._manifest.exists():
            for line in self._manifest.read_text(encoding="utf-8").splitlines():
                if line.strip():
                    entry = json.loads(line)
                    self.entries[entry["file"]] = entry

    def save(self, file: str, prompt: Prompt, take: int, audio: np.ndarray) -> None:
        pcm = (np.clip(audio, -1.0, 1.0) * 32767).astype("<i2")
        with wave.open(str(self.folder / file), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(SAMPLE_RATE)
            w.writeframes(pcm.tobytes())

        self.entries[file] = {"file": file, "kind": prompt.kind, "text": prompt.text, "take": take, "cue": prompt.cue}
        temp = self._manifest.with_suffix(".tmp")
        temp.write_text("".join(json.dumps(e, ensure_ascii=False) + "\n" for e in self.entries.values()), encoding="utf-8")
        os.replace(temp, self._manifest)


def plan(prompts: list[Prompt]) -> list[tuple[Prompt, str, int]]:
    """Gives every prompt a stable file name and its take number among identical prompts, so sessions can resume."""
    per_kind: dict[str, int] = {}
    per_prompt: dict[Prompt, int] = {}
    planned = []
    for prompt in prompts:
        per_kind[prompt.kind] = per_kind.get(prompt.kind, 0) + 1
        per_prompt[prompt] = per_prompt.get(prompt, 0) + 1
        planned.append((prompt, f"{prompt.kind}-{per_kind[prompt.kind]:03d}.wav", per_prompt[prompt]))
    return planned


def describe(prompt: Prompt) -> str:
    match prompt.kind:
        case "wake":
            return f'Say "ALDREN" ({prompt.cue}).'
        case "command":
            return f'Say: "{prompt.text}"'
        case _:
            return f"Room noise for {prompt.seconds:.0f} s: {prompt.cue}."


def run(session: Session, recorder: Recorder, prompts: list[Prompt], ask: Callable[[str], str]) -> int:
    planned = plan(prompts)
    i = next((n for n, (_, file, _) in enumerate(planned) if file not in session.entries), len(planned))
    if i > 0:
        print(f"Resuming at take {i + 1} of {len(planned)}.")

    while i < len(planned):
        prompt, file, take = planned[i]
        print(f"\n[{i + 1}/{len(planned)}] {describe(prompt)}")
        answer = ask("Enter = record, r = redo the previous take, q = stop for now: ").strip().lower()
        if answer == "q":
            print(f"Stopped. {len(session.entries)} takes are saved in {session.folder}; run the same command to resume.")
            return 0
        if answer == "r" and i > 0:
            i -= 1
            continue

        print("Get ready...")
        try:
            audio = recorder.record(prompt.seconds)
        except DeviceLost as lost:
            print(f"Input device disappeared ({lost}). Recording stopped; the {len(session.entries)} takes so far are saved.",
                  file=sys.stderr)
            return 2

        if float(np.max(np.abs(audio), initial=0.0)) >= CLIP_PEAK:
            print("Clipped: too loud for the microphone gain. Retake.")
            continue

        session.save(file, prompt, take, audio)
        i += 1

    print(f"\nDone: {len(planned)} takes in {session.folder}.")
    return 0


def delivery_rate_ok(frames: int, seconds: float, nominal: int, tolerance: float = 0.1) -> bool:
    """True when a blocking read of `frames` took about as long as it should at `nominal` frames per second."""
    expected = frames / nominal
    return abs(seconds - expected) <= expected * tolerance


def pick_device(devices: list[dict], apis: list[dict], wanted: str, api: str) -> tuple[int, str, int]:
    """Exactly one input on the given host API must contain the wanted name; anything else is an error."""
    inputs = [(i, d) for i, d in enumerate(devices) if d["max_input_channels"] > 0]
    matches = [(i, d) for i, d in inputs if wanted.lower() in d["name"].lower() and apis[d["hostapi"]]["name"] == api]
    if len(matches) == 1:
        index, device = matches[0]
        return index, device["name"], int(device["default_samplerate"])

    found = matches or inputs
    names = "; ".join(f'{d["name"]} ({apis[d["hostapi"]]["name"]})' for _, d in found)
    if matches:
        raise LookupError(f'"{wanted}" matches {len(matches)} inputs on {api}; use a longer name. Matches: {names}')
    raise LookupError(f'No input device matching "{wanted}" on {api}. Inputs: {names}')


class Microphone:
    """The input device, pinned by name and host API. It never falls back to another device."""

    def __init__(self, name: str, host_api: str) -> None:
        import sounddevice  # imported here so the tests never need PortAudio

        self._sd = sounddevice
        self._wanted = (name, host_api)
        self.index, self.name, self.rate = self._find()
        self.channels = min(2, int(self._sd.query_devices(self.index)["max_input_channels"]))

        # A one-second self-test before anything is recorded: the 2026-10-31 session was lost to a bad rate.
        frames, seconds = self._read(1.0)
        if not delivery_rate_ok(frames, seconds, CAPTURE_RATE):
            raise DeliveryRateError(f"{self.name} delivered 1.0 s of audio in {seconds:.2f} s; refusing to record.")

    def _find(self) -> tuple[int, str, int]:
        wanted, api = self._wanted
        return pick_device(list(self._sd.query_devices()), list(self._sd.query_hostapis()), wanted, api)

    def _read(self, seconds: float, keep: list[np.ndarray] | None = None, announce: bool = False) -> tuple[int, float]:
        import time

        api = self._sd.query_hostapis(self._sd.query_devices(self.index)["hostapi"])["name"]
        settings = self._sd.WasapiSettings(auto_convert=True) if "WASAPI" in api else None
        with self._sd.InputStream(device=self.index, channels=self.channels, samplerate=CAPTURE_RATE, dtype="float32",
                                  extra_settings=settings) as stream:
            stream.read(CAPTURE_RATE // 10)  # let the stream settle before timing
            if announce:
                print(f"Speak now ({seconds:.1f} s).", flush=True)  # opening the stream takes ~1.5 s, so only now
            start = time.perf_counter()
            data, _ = stream.read(int(CAPTURE_RATE * seconds))
            elapsed = time.perf_counter() - start
        if keep is not None:
            keep.append(data)
        return data.shape[0], elapsed

    def record(self, seconds: float) -> np.ndarray:
        from scipy.signal import resample_poly

        # An unplugged device makes opening the stream fail; a reconfigured one fails the timing check below.
        # (Re-initialising PortAudio to refresh its device list took several seconds per take.)
        captured: list[np.ndarray] = []
        try:
            frames, elapsed = self._read(seconds, captured, announce=True)
        except self._sd.PortAudioError as error:
            raise DeviceLost(self.name) from error
        if not delivery_rate_ok(frames, elapsed, CAPTURE_RATE):
            raise DeviceLost(f"{self.name} delivered {seconds:.1f} s of audio in {elapsed:.2f} s")
        return resample_poly(captured[0].mean(axis=1), SAMPLE_RATE, CAPTURE_RATE).astype(np.float32)


def list_inputs() -> None:
    import sounddevice

    apis = sounddevice.query_hostapis()
    for device in sounddevice.query_devices():
        if device["max_input_channels"] > 0:
            print(f'{apis[device["hostapi"]]["name"]}: {device["name"]} ({int(device["default_samplerate"])} Hz)')


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="aldren-record", description="Guided recording session for ALDREN's voice tests.")
    parser.add_argument("--device", help='part of the input device name, for example "Maono"')
    parser.add_argument("--host-api", default=DEFAULT_HOST_API)
    parser.add_argument("--out", type=Path, default=Path(r"E:\AldrenData\voice\recordings"))
    parser.add_argument("--session", default=datetime.date.today().isoformat())
    parser.add_argument("--list-devices", action="store_true", help="list the input devices and exit")
    args = parser.parse_args(argv)

    if args.list_devices or not args.device:
        list_inputs()
        return 0 if args.list_devices else 1

    try:
        microphone = Microphone(args.device, args.host_api)
    except (LookupError, DeliveryRateError) as error:
        print(error, file=sys.stderr)
        return 1

    print(f"Recording from {microphone.name} ({args.host_api}, device at {microphone.rate} Hz, captured at "
          f"{CAPTURE_RATE} Hz through Windows, saved as 16 kHz mono) into {args.out / args.session}. Rate self-test passed.")
    try:
        return run(Session(args.out, args.session), microphone, session_prompts(), ask=input)
    except KeyboardInterrupt:
        print("\nStopped. Every finished take is saved; run the same command to resume.")
        return 130
