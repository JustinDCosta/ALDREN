import json
import wave

import numpy as np
import pytest

from aldren_wakeword import record
from aldren_wakeword.prompts import Prompt

RATE = 16_000
PROMPTS = [
    Prompt("wake", "ALDREN", "normal", 0.5),
    Prompt("command", "Open Spotify.", "en", 0.5),
]


class FakeRecorder:
    """Hands out prepared takes instead of opening a microphone."""

    def __init__(self, takes):
        self._takes = list(takes)

    def record(self, seconds: float) -> np.ndarray:
        take = self._takes.pop(0)
        if isinstance(take, Exception):
            raise take
        return take


def tone(peak: float, seconds: float = 0.5) -> np.ndarray:
    t = np.arange(int(RATE * seconds)) / RATE
    return (peak * np.sin(2 * np.pi * 440 * t)).astype(np.float32)


def manifest(tmp_path):
    text = (tmp_path / "2026-11-01" / "manifest.jsonl").read_text(encoding="utf-8")
    return [json.loads(line) for line in text.splitlines()]


def always_record(_question: str) -> str:
    return ""


def test_manifest_line_written_per_take(tmp_path):
    session = record.Session(tmp_path, "2026-11-01")

    code = record.run(session, FakeRecorder([tone(0.5), tone(0.5)]), PROMPTS, ask=always_record)

    assert code == 0
    lines = manifest(tmp_path)
    assert [(l["kind"], l["text"], l["take"], l["cue"]) for l in lines] == [
        ("wake", "ALDREN", 1, "normal"),
        ("command", "Open Spotify.", 1, "en"),
    ]
    for line in lines:
        with wave.open(str(tmp_path / "2026-11-01" / line["file"]), "rb") as w:
            assert (w.getframerate(), w.getnchannels(), w.getsampwidth()) == (16000, 1, 2)


def test_clipped_take_is_rejected(tmp_path, capsys):
    session = record.Session(tmp_path, "2026-11-01")

    code = record.run(session, FakeRecorder([tone(1.0), tone(0.5), tone(0.5)]), PROMPTS, ask=always_record)

    assert code == 0
    assert len(manifest(tmp_path)) == 2
    assert "retake" in capsys.readouterr().out.lower()


APIS = [{"name": "MME"}, {"name": "Windows WASAPI"}]
DEVICES = [
    {"name": "Microphone (Maono PD300X Microphone)", "hostapi": 0, "max_input_channels": 2, "default_samplerate": 44100.0},
    {"name": "Microphone (Maono AI Microphone)", "hostapi": 1, "max_input_channels": 1, "default_samplerate": 48000.0},
    {"name": "Microphone (Maono PD300X Microphone)", "hostapi": 1, "max_input_channels": 2, "default_samplerate": 192000.0},
    {"name": "Speakers (Maono PD300X)", "hostapi": 1, "max_input_channels": 0, "default_samplerate": 48000.0},
]


def test_device_picked_by_name_and_host_api():
    assert record.pick_device(DEVICES, APIS, "pd300x", "Windows WASAPI") == (2, "Microphone (Maono PD300X Microphone)", 192000)


def test_ambiguous_device_name_is_rejected():
    with pytest.raises(LookupError, match="matches 2 inputs"):
        record.pick_device(DEVICES, APIS, "Maono", "Windows WASAPI")


def test_device_loss_aborts_with_exit_code_2(tmp_path, capsys):
    session = record.Session(tmp_path, "2026-11-01")

    code = record.run(session, FakeRecorder([record.DeviceLost("Maono PD300X")]), PROMPTS, ask=always_record)

    assert code == 2
    assert "Input device disappeared" in capsys.readouterr().err
