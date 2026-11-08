# Phase 3 acceptance

ALDREN runs from `E:\AldrenData\dev\aldren`. Push-to-talk is **Ctrl+Num ***: press it, speak, stop talking. Use the PD300X unless a step says otherwise. Note anything different.

| # | Do | Expected |
|---|---|---|
| 1 | Press push-to-talk, say "what time is it" | The island shows "Listening…", then your words, then ALDREN says the time aloud. The Windows microphone indicator shows only while it listens |
| 2 | Right after it answers, say "and what's the date" (no hotkey) | The 8 s follow-up window catches it and ALDREN answers |
| 3 | Push-to-talk, "open notepad" | Notepad opens; ALDREN says so |
| 4 | Play music, then push-to-talk, "tell me a short story about a robot" | The music ducks while ALDREN speaks and comes back after |
| 5 | While it is telling the story, press push-to-talk | Speech stops at once |
| 6 | Push-to-talk, then say nothing for 6 s | The island closes on its own |
| 7 | Push-to-talk, "how are you", then in the follow-up window say "stop" | ALDREN goes quiet and the island closes |
| 8 | `gaming mode on` (typed), then push-to-talk, "what's two plus two" | The answer shows in the island, nothing is spoken. Then `gaming mode auto` |
| 9 | Settings > Voice & wake word: switch on the wake word, Save. Wait 5 s, say "ALDREN" … "what time is it" | It wakes and answers. Note how often it wakes by itself over the next ~10 minutes of normal use (it should rarely, with the double-check on) |
| 10 | Switch the wake word off again, Save | The microphone indicator goes away within a few seconds |
| 11 | S3 checks: repeat step 1 on the laptop microphone (unplug the PD300X first) | ALDREN picks the laptop microphone and still understands you |
| 12 | Start push-to-talk on the PD300X and unplug it while speaking | The island says it lost the microphone; nothing crashes |
| 13 | With a Bluetooth headset connected, step 1 again | ALDREN never uses the headset microphone (the headset's music quality doesn't drop) |

Then tell me "done" or what failed. I read the time from the end of your speech to the first audio from the log (target: median ≤ 1.5 s).
