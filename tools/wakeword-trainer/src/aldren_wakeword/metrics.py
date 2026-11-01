"""Wake-word evaluation metrics."""

import numpy as np


def false_reject_rate(scores: np.ndarray, threshold: float) -> float:
    """Share of wake-word clips whose best score stays below the threshold."""
    return float(np.mean(np.asarray(scores) < threshold))


def false_accepts_per_hour(scores: np.ndarray, threshold: float, hours: float, refractory_frames: int) -> float:
    """
    Activations per hour on audio without the wake word. Frames above the threshold that follow the previous one
    within `refractory_frames` belong to the same activation, so one long activation counts once.
    """
    events = 0
    previous = None
    for frame in np.flatnonzero(np.asarray(scores) >= threshold):
        if previous is None or frame - previous > refractory_frames:
            events += 1
        previous = frame
    return events / hours
