"""
aldren-listen: the wake-word field test. Listens on the pinned microphone during normal use, logs every trigger,
the hours monitored and its own CPU use, and keeps only 2 s of audio around each trigger. Ctrl+C stops it.
"""

import argparse
import collections
import datetime
import json
import os
import sys
import time
import wave
from pathlib import Path

import numpy as np

from aldren_wakeword import features, model
from aldren_wakeword.record import DEFAULT_HOST_API, delivery_rate_ok, pick_device

BLOCK = 1280  # 80 ms at 16 kHz, one embedding frame
FIELD = Path(r"E:\AldrenData\voice\field")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="aldren-listen", description="Wake-word field test.")
    parser.add_argument("--device", default="PD300X")
    parser.add_argument("--host-api", default=DEFAULT_HOST_API)
    parser.add_argument("--model", type=Path, default=Path(r"E:\AldrenData\voice\models\aldren-wakeword.npz"))
    parser.add_argument("--threshold", type=float, required=True)
    parser.add_argument("--minutes", type=float, default=0, help="stop after this long (0 = until Ctrl+C)")
    args = parser.parse_args(argv)
    deadline = time.time() + args.minutes * 60 if args.minutes > 0 else None

    import sounddevice as sd

    import sherpa_onnx

    from aldren_wakeword.evaluate import PARAKEET, transcript_confirms

    index, name, _ = pick_device(list(sd.query_devices()), list(sd.query_hostapis()), args.device, args.host_api)
    net = model.WakeModel(args.model)
    # Every trigger is also transcribed, so the two-stage cascade can be measured on real-life false alarms.
    asr = sherpa_onnx.OfflineRecognizer.from_transducer(
        encoder=str(PARAKEET / "encoder.int8.onnx"), decoder=str(PARAKEET / "decoder.int8.onnx"),
        joiner=str(PARAKEET / "joiner.int8.onnx"), tokens=str(PARAKEET / "tokens.txt"), num_threads=2,
        model_type="nemo_transducer")
    extract = features.extractor(ncpu=1)
    day = FIELD / datetime.date.today().isoformat()
    day.mkdir(parents=True, exist_ok=True)
    log = day / "log.jsonl"
    ring = collections.deque(maxlen=3 * features.SAMPLE_RATE // BLOCK)  # 3 s of blocks
    pending: list[tuple[int, str]] = []  # (blocks still to collect after a trigger, file name)
    triggers, cooldown, blocks = 0, 0, 0
    started, cpu_started, last_summary = time.time(), time.process_time(), time.time()

    def write(record: dict) -> None:
        with log.open("a", encoding="utf-8") as f:
            f.write(json.dumps(record) + "\n")

    def summary() -> dict:
        wall = time.time() - started
        cpu = (time.process_time() - cpu_started) / max(wall, 1e-6) / (os.cpu_count() or 1) * 100
        return {"time": datetime.datetime.now().isoformat(timespec="seconds"), "hours": round(blocks * BLOCK / features.SAMPLE_RATE / 3600, 3),
                "triggers": triggers, "cpu_percent": round(cpu, 2)}

    try:
        with sd.InputStream(device=index, channels=1, samplerate=features.SAMPLE_RATE, dtype="int16", blocksize=BLOCK) as stream:
            stream.read(BLOCK * 5)
            t = time.perf_counter()
            stream.read(features.SAMPLE_RATE)
            if not delivery_rate_ok(features.SAMPLE_RATE, time.perf_counter() - t, features.SAMPLE_RATE):
                print(f"{name} delivers audio at the wrong speed; not listening.", file=sys.stderr)
                return 1
            print(f"Listening on {name} (threshold {args.threshold}). Logging to {log}. Ctrl+C stops.")
            write({"event": "start", "device": name, "threshold": args.threshold, "model": str(args.model)})
            while deadline is None or time.time() < deadline:
                block, _ = stream.read(BLOCK)
                pcm = block[:, 0]
                ring.append(pcm.copy())
                blocks += 1
                extract(pcm)
                cooldown = max(0, cooldown - 1)
                if extract.feature_buffer.shape[0] >= features.WINDOW and cooldown == 0:
                    score = float(net(extract.get_features(features.WINDOW))[0])
                    if score >= args.threshold:
                        triggers += 1
                        cooldown = 25  # 2 s
                        stamp = datetime.datetime.now()
                        pending.append((6, f"trigger-{stamp:%H%M%S}.wav"))  # 0.5 s more audio, then save
                        write({"event": "trigger", "time": stamp.isoformat(timespec="seconds"), "score": round(score, 3)})
                        print(f"{stamp:%H:%M:%S} trigger (score {score:.2f})")
                for i, (remaining, file) in enumerate(list(pending)):
                    if remaining <= 1:
                        clip = np.concatenate(list(ring)[-25:])  # 2 s: 1.5 s before the trigger, 0.5 s after
                        with wave.open(str(day / file), "wb") as w:
                            w.setnchannels(1)
                            w.setsampwidth(2)
                            w.setframerate(features.SAMPLE_RATE)
                            w.writeframes(clip.astype("<i2").tobytes())
                        recognition = asr.create_stream()
                        recognition.accept_waveform(features.SAMPLE_RATE, clip.astype(np.float32) / 32768.0)
                        asr.decode_stream(recognition)
                        text = recognition.result.text
                        write({"event": "transcript", "file": file, "text": text, "confirmed": transcript_confirms(text)})
                    pending[i] = (remaining - 1, file)
                pending = [p for p in pending if p[0] > 0]
                if time.time() - last_summary >= 600:
                    write({"event": "summary", **summary()})
                    last_summary = time.time()
        raise KeyboardInterrupt  # time is up: stop the same way as Ctrl+C
    except sd.PortAudioError as error:
        print(f"Input device disappeared ({error}).", file=sys.stderr)
        write({"event": "device_lost", **summary()})
        return 2
    except KeyboardInterrupt:
        final = summary()
        write({"event": "stop", **final})
        print(f"Stopped after {final['hours']} h: {final['triggers']} triggers, {final['cpu_percent']}% CPU.")
        return 0
