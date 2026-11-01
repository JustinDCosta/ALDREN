import json

import numpy as np

from aldren_wakeword import features, model, train


def test_numpy_model_matches_torch(tmp_path):
    import torch

    net = model.build_torch()
    net.eval()
    x = np.random.default_rng(0).standard_normal((5, 16, 96)).astype(np.float32)
    with torch.no_grad():
        expected = torch.sigmoid(net(torch.from_numpy(x))[:, 0]).numpy()

    model.save(net, tmp_path / "m.npz")

    assert np.allclose(model.WakeModel(tmp_path / "m.npz")(x), expected, atol=1e-5)


def test_place_ends_speech_near_the_end_of_the_window():
    rng = np.random.default_rng(1)
    speech = np.ones(8000, np.float32)  # 0.5 s

    clip = features.place(speech, rng, end=8000, jitter=0.2)

    voiced = np.flatnonzero(clip)
    assert len(clip) == features.CLIP_SAMPLES
    assert len(voiced) == 8000
    assert features.CLIP_SAMPLES - 0.2 * features.SAMPLE_RATE <= voiced[-1] + 1 <= features.CLIP_SAMPLES


def test_speech_end_finds_the_last_loud_frame():
    audio = np.zeros(32000, np.float32)
    audio[8000:16000] = 0.5 * np.sin(np.arange(8000) / 3)

    assert abs(features.speech_end(audio) - 16000) <= 160


def test_split_holds_out_three_takes_per_cue_and_a_third_of_commands(tmp_path):
    lines = [{"file": f"wake-{c}-{t}.wav", "kind": "wake", "text": "ALDREN", "take": t, "cue": f"cue{c}"} for c in range(9) for t in range(9)]
    lines += [{"file": f"command-{i}.wav", "kind": "command", "text": "x", "take": 1, "cue": "en"} for i in range(30)]
    lines += [{"file": "noise-001.wav", "kind": "noise", "text": "", "take": 1, "cue": "quiet"}]
    (tmp_path / "manifest.jsonl").write_text("\n".join(json.dumps(l) for l in lines), encoding="utf-8")

    split = train.split_session(tmp_path)

    assert len(split["wake_test"]) == 27 and len(split["wake_train"]) == 54
    assert all(sum(f.startswith(f"wake-{c}-") for f in split["wake_test"]) == 3 for c in range(9))
    assert len(split["command_test"]) == 10 and not set(split["command_test"]) & set(split["command_train"])
