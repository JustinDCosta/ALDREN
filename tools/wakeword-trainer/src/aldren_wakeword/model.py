"""The wake-word classifier: 16 embedding frames (16 x 96) -> probability. Saved as .npz and run with numpy."""

from pathlib import Path

import numpy as np

HIDDEN = 128


def build_torch():
    import torch.nn as nn

    # openWakeWord's shape. The dropout layers are kept (at 0) so the saved layer indices stay stable.
    return nn.Sequential(
        nn.Flatten(),
        nn.Linear(16 * 96, HIDDEN), nn.LayerNorm(HIDDEN), nn.ReLU(), nn.Dropout(0.0),
        nn.Linear(HIDDEN, HIDDEN), nn.LayerNorm(HIDDEN), nn.ReLU(), nn.Dropout(0.0),
        nn.Linear(HIDDEN, 1),
    )


def save(torch_model, path: Path) -> None:
    state = {k.replace(".", "_"): v.detach().cpu().numpy() for k, v in torch_model.state_dict().items()}
    np.savez(path, **state)


class WakeModel:
    """Numpy forward pass of the saved network (dropout is inactive at inference)."""

    def __init__(self, path: Path) -> None:
        w = np.load(path)
        self.layers = [(w["1_weight"], w["1_bias"], w["2_weight"], w["2_bias"]),
                       (w["5_weight"], w["5_bias"], w["6_weight"], w["6_bias"])]
        self.out = (w["9_weight"], w["9_bias"])

    def __call__(self, windows: np.ndarray) -> np.ndarray:
        x = np.asarray(windows, dtype=np.float32).reshape(len(windows), -1)
        for weight, bias, gamma, beta in self.layers:
            x = x @ weight.T + bias
            x = (x - x.mean(axis=1, keepdims=True)) / np.sqrt(x.var(axis=1, keepdims=True) + 1e-5) * gamma + beta
            x = np.maximum(x, 0.0)
        logits = x @ self.out[0].T + self.out[1]
        return 1.0 / (1.0 + np.exp(-logits[:, 0]))
