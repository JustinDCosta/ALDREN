# Phase 1 acceptance

Run these on the laptop, in order. Each step says what you should see. Note anything different and tell me; STATUS moves a feature to **Tested** only once its steps pass here.

The development build lives in `E:\AldrenData\dev\aldren`. Logs are in `%LOCALAPPDATA%\Aldren\logs` (Settings > Diagnostics > Open the log folder).

| # | Step | Expected |
|---|---|---|
| 1 | Quit ALDREN if it runs (tray > Quit). In a terminal at the repo: `dotnet build ALDREN.sln`, then `powershell -ExecutionPolicy Bypass -File tools\dev\publish-local.ps1` | 0 warnings, 0 errors; "Published to E:\AldrenData\dev\aldren" |
| 2 | Double-click `E:\AldrenData\dev\aldren\Aldren.Core.exe` in Explorer | No window. The ALDREN tray icon (dark disc, cyan ring) appears within a few seconds |
| 3 | Press the hotkey (Settings > General shows it; on this laptop Ctrl+Alt+Shift+Space), type `what time is it`, Enter | The island opens at the top of the screen you're working on, with the caret in the box; the reply gives the local time |
| 4 | Type `test confirmation` three times: answer Yes once, No once, and leave it unanswered for 60 s once | Yes: "Confirmation test passed." No: "You declined…". Unanswered: the island closes after 60 s and the reply says you declined |
| 5 | Settings > Permissions: switch off "Run the confirmation test" is optional; switch off "Read the system clock", then ask the time again. Open Activity history | The time is refused with "Permission 'system.clock' is off. You can turn it on in Settings." Activity history shows `set_permission` (system.clock: off) and `get_time` PermissionDenied. Switch it back on |
| 6 | Task Manager > Details: end `Aldren.App.exe` | The tray icon comes back within about 2 s |
| 7 | End `Aldren.Core.exe` | The App starts a new Core within about 2 s (Settings > Diagnostics shows a new Core process ID) |
| 8 | Tray > Quit, then wait 30 s | The icon disappears; Task Manager shows no `Aldren.Core` or `Aldren.App`, and nothing comes back |
| 9 | Start ALDREN, tray > Start with Windows (it gets a check mark). Sign out and back in | About 15 s after the desktop appears, the tray icon shows, with no window. Afterwards keep Start with Windows on, or switch it off (that removes the `HKCU\…\Run\ALDREN` value) |
| 10 | Leave ALDREN idle (island hidden, no windows open). In a terminal: `dotnet run --project tools\bench -c Release -- idle --minutes 10` | Average CPU under 1% and peak memory under 300 MB, both "pass"; the result is appended to `E:\AldrenData\bench\phase1\idle.md` |
| 11 | Settings > Diagnostics > Write a redaction test line to the log, then open the newest `app-*.log` | The line reads `Redaction test: apiKey=***`; the key itself is nowhere in the file |
| 12 | Credential Manager | Already checked by hand in Task 8 (one `Aldren/test-<guid>` credential written, read back and deleted, nothing left). Nothing to do unless you want to repeat it: `cmdkey /list` shows no `Aldren/` entry |

Not covered by an automated test (your decision for Phase 1): everything except the Tool Gateway, which has 43 unit tests (`dotnet test tests\Aldren.Security.Tests`).
