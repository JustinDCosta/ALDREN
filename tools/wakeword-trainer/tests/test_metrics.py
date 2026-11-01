import numpy as np

from aldren_wakeword import metrics


def test_frr_counts_misses():
    assert metrics.false_reject_rate(np.array([0.9, 0.2, 0.8, 0.1]), threshold=0.5) == 0.5


def test_fa_per_hour_merges_consecutive_frames():
    scores = np.zeros(1000)
    scores[[10, 11, 12, 500]] = 0.9

    assert metrics.false_accepts_per_hour(scores, threshold=0.5, hours=1.0, refractory_frames=20) == 2.0


def test_fa_per_hour_counts_a_long_activation_once():
    scores = np.zeros(1000)
    scores[100:160] = 0.9  # longer than the refractory window, but one continuous activation

    assert metrics.false_accepts_per_hour(scores, threshold=0.5, hours=0.5, refractory_frames=20) == 2.0
