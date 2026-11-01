from aldren_wakeword import synth


def test_wake_texts_cover_respellings():
    texts = synth.wake_texts()
    assert {"Aldren", "Al-drin", "All-drin"} <= set(texts)


def test_near_misses_listed():
    assert {"Alden", "all then", "Darren", "children"} <= set(synth.near_miss_texts())


def test_homophone_aldrin_is_a_positive_not_a_near_miss():
    # The user says ALDREN as "al-drin", which is how "Aldrin" sounds: as a negative it would fight the wake word.
    assert "Aldrin" in synth.wake_texts()
    assert "Aldrin" not in synth.near_miss_texts()
