# Phase 2 acceptance

ALDREN is running from `E:\AldrenData\dev\aldren` with Qwen3.5-4B loaded on the GPU. Keep LM Studio's model unloaded. Press the hotkey (Ctrl+Alt+Shift+Space), type each line, press Enter. Note anything different.

| # | Type | Expected |
|---|---|---|
| 1 | `what time is it` · `what's the date` · `what's 12 * 7 + 3` | Instant answers: the time, today's date, "12 * 7 + 3 = 87." |
| 2 | `open notepad` | Notepad opens; reply "Opening Notepad." |
| 3 | Click another window, then `switch to notepad` | Notepad comes to the front (or flashes in the taskbar) |
| 4 | Type a few letters in Notepad, then `close notepad` | The island asks Yes/No. Yes: Notepad closes or asks to save |
| 5 | `volume 30` · `volume up` · `mute` · `unmute` | The volume goes to 30, then 40; mute and unmute work |
| 6 | Play something (Spotify, YouTube), then `pause`, `play`, `next song` | Playback pauses, resumes and skips |
| 7 | `set a timer for 10 seconds` | "Timer set for 10 seconds…"; 10 s later a Windows notification "Timer done" |
| 8 | `hi aldren, how are you?` · `tell me a short joke about cats` | Short, in-character replies within about a second |
| 9 | `I need some music, can you open spotify?` | The model opens Spotify (or says it isn't installed) |
| 10 | `look up the latest GPU news` · `what's on my screen?` | Replies that research arrives in Phase 6 and screen work in Phase 5 |
| 11 | `status` | Says the 4B is loaded on the GPU, gaming mode off, AC power, free VRAM |
| 12 | `gaming mode on`, then `status`; watch Task Manager > Performance > GPU memory | GPU memory drops by ~3.9 GB within a few seconds; status says unloaded |
| 13 | `tell me a fun fact` (still in gaming mode) | Answers more slowly (CPU model, first time ~5–10 s to load) |
| 14 | `gaming mode auto`, then launch a game fullscreen for ~10 s, then quit it | The GPU model unloads while the game is fullscreen (check with `status` afterwards: unloaded, then it reloads on the next question) |
| 15 | Tray > Settings > Permissions | Eight new permissions (open apps, switch, close, volume, media, timers, calculations, hand-offs), all on |
| 16 | Settings > Activity history | Every action above is listed, model-proposed ones included |

Then tell me "done" (or what failed). I read the timings from the log (time to first answer with the model warm, target < 500 ms) and the unload time for step 12 (target < 5 s).
