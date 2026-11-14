# Phase 9 acceptance

| # | Do | Expected |
|---|---|---|
| 1 | "switch to sarcastic mode", then "how's the weather looking for productivity today?" | A drier, sarcastic reply; Settings > Modes shows sarcastic |
| 2 | "be more concise", ask anything | Short answers |
| 3 | Settings > Modes: friendly, Save | Back to normal |
| 4 | "silent mode on", then a voice request | Text in the island only, no speech |
| 5 | "silent mode off" | Spoken replies again |
| 6 | "create a routine called study time: open notepad, then set a timer for 1 minute" | "Saved the study time routine with 2 steps…" |
| 7 | "study time" | Notepad opens, then the timer starts; "list my routines" names it |
| 8 | Click into Notepad. Push-to-talk: "start dictation", then say a sentence, then "stop dictation" | The sentence is typed into Notepad; "Dictation off." |
| 9 | Click into a password field and dictate | Refused: never into password fields |
| 10 | With a repository open in VS Code: "what's the git status of this repo?" | Branch, changed files and the last commits |
| 11 | Start OBS, wait for a reminder | The notification says "You have a reminder…" without its text (unless "Show ALDREN on stream" is on) |
| 12 | `powershell -File tools\release\pack.ps1 -Version 0.9.1` | `E:\AldrenData\releases` has `Aldren-win-Setup.exe` and the 0.9.1 package |
| 13 | Optional: run `Aldren-win-Setup.exe` | Installs per user (no admin prompt) under `%LOCALAPPDATA%\Aldren`, a Start menu shortcut, ALDREN starts. Uninstall from Settings > Apps. Note: this uses C: (about 300 MB) |
| 14 | Soak: `dotnet run --project tools\bench -c Release -- idle --minutes 1440` while ALDREN runs for a day | Idle CPU and memory within the targets (≤ 0.5 GB idle) |
