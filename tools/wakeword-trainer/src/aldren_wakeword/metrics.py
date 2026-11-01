"""Wake-word evaluation metrics."""

import numpy as np

# The live listener re-arms this many 80 ms frames (2 s) after a trigger; offline counts use the same rule.
LISTENER_COOLDOWN_FRAMES = 25


def false_reject_rate(scores: np.ndarray, threshold: float) -> float:
    """Share of wake-word clips whose best score stays below the threshold."""
    return float(np.mean(np.asarray(scores) < threshold))


def false_accepts_per_hour(scores: np.ndarray, threshold: float, hours: float,
                           refractory_frames: int = LISTENER_COOLDOWN_FRAMES) -> float:
    """
    Triggers per hour on audio without the wake word, counted the way the live listener fires: after a trigger, frames
    are ignored for `refractory_frames`, then the next frame above the threshold triggers again. A long activation
    therefore counts once per cooldown, as it does live.
    """
    events = 0
    last = None
    for frame in np.flatnonzero(np.asarray(scores) >= threshold):
        if last is None or frame - last >= refractory_frames:
            events += 1
            last = frame
    return events / hours
