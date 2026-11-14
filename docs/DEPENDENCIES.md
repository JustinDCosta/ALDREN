# Dependencies

Direct packages of the Phase 1 solution (`src/` and its tests). Versions for `src/` are set in `src/Directory.Packages.props`; test projects pin theirs in their project files. Every project under `src/` and `tests/` commits a `packages.lock.json` and restores in locked mode, so the exact version and SHA-512 of every package, transitive ones included, are in those lock files.

| Package | Version | Licence | Used by | Purpose |
|---|---|---|---|---|
| Microsoft.Extensions.Hosting | 10.0.14 | MIT | Core, App | Generic host, DI, configuration, logging |
| Microsoft.Win32.SystemEvents | 10.0.14 | MIT | Core | Logoff and shutdown notifications |
| Serilog | 4.4.0 | Apache-2.0 | Common | Structured logging |
| Serilog.Extensions.Hosting | 10.0.0 | Apache-2.0 | Common | Serilog in the generic host |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Common | Rolling log files |
| Serilog.Formatting.Compact | 3.0.0 | Apache-2.0 | Common | Compact JSON log format |
| StreamJsonRpc | 2.26.150 | MIT | Contracts | IPC between Core and the App (ADR-0003) |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | Security | Audit log in `aldren.db` |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | Security | Logging from the Tool Gateway |
| Microsoft.Windows.CsWin32 | 0.3.367 | MIT | Security, Core, App (build only) | Generated Win32 declarations |
| Microsoft.WindowsAppSDK | 2.6.4 | MIT | App | WinUI 3 (self-contained) |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2957 | MIT | App | Windows SDK build tools |
| Microsoft.Extensions.TimeProvider.Testing | 10.11.0 | MIT | tests | Fake clock |
| xunit | 2.9.3 | Apache-2.0 | tests | Test framework |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | tests | Test runner |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | tests | Test host |
| coverlet.collector | 6.0.4 | MIT | tests | Coverage |
| NAudio | 3.3.0 | MIT | Voice | Microphone capture (WASAPI), playback, audio-session ducking |
| org.k2fsa.sherpa.onnx | 2.0.1 | Apache-2.0 | Voice | Silero VAD, Parakeet speech recognition, Supertonic speech; ships onnxruntime.dll 1.28.2 |
| Microsoft.ML.OnnxRuntime.Managed | 1.28.0 | MIT | Voice | Wake-word feature models (openWakeWord); uses the onnxruntime.dll above. Added 2026-11-08 with your approval; lock hash `4ecKjwAS…1UlA==` |
| FlaUI.UIA3 | 5.0.0 | MIT | Perception | UI Automation snapshots and control actions (Phase 5; lock hash `s1Yy1RsRJ0OX…`) |
| PdfPig | 0.1.16 | Apache-2.0 | Windows (files) | Reading PDFs locally (`qQcbsNMYVY+K…`) |
| DocumentFormat.OpenXml | 3.5.1 | MIT | Windows (files) | Reading DOCX locally (`zxdOf5VVCe/u…`) |
| System.Data.OleDb | 10.0.14 | MIT | Windows (files) | Read-only queries to the Windows Search index (`xYI6L1KwgZMk…`) |
| AngleSharp | 1.10.0 | MIT | Research | Extracting the main text of fetched web pages (Phase 6; `jibGB16fKOT+…`) |
| Google.Apis.Auth | 1.78.0 | Apache-2.0 | Integrations.Google | OAuth for your own Google client; token kept in Credential Manager (Phase 7; `C+Jzd1tTqZ3/…`, nupkg SHA-256 `e37ffa5aac6fe4fcea3348f2833a16cb35bb8353b875adc01056524a1c37b6ee`) |
| Google.Apis.Calendar.v3 | 1.78.0.4285 | Apache-2.0 | Integrations.Google | Calendar agenda, create, delete (Phase 7; `6OzXfEYUzZz7…`, SHA-256 `2fa670248dc1a1bc7355ac8de96c0e1b8b2904bc3f786064ebf3a1d5d22bda1a`) |
| Google.Apis.Drive.v3 | 1.78.0.4313 | Apache-2.0 | Integrations.Google | Drive search, download, upload, rename (Phase 7; `aQ6ZvjFdEKQH…`, SHA-256 `1616cf52cbd034433e0eb193ee6ff3fba2a6889bfdb2965530d676b2f10ef535`) |
| Velopack | 1.2.161 | MIT | Core | Install, update and uninstall hooks (Phase 9; `OpHNacIumnkh…`, SHA-256 `d57275692b299751a12ee3e18283b5f7fe3e55339f96b1623345da095dc484ed`) |

Outside NuGet (Phase 6): portable Node.js 24.22.2 LTS for win-x64 in `E:\AldrenData\gateway\node-v24.22.2-win-x64`, SHA-256 `ca1bff5d8ead13c9f2ea404a176db46bd430704d5b5113734049fa365e7799e2` (nodejs.org SHASUMS256.txt), running the pinned FreeLLMAPI 0.16.14 in `E:\AldrenData\gateway\freellmapi-0.16.14`.

Outside the solution (Phase 9): the Velopack CLI `vpk` 1.2.161, a .NET tool in `E:AldrenData	oolsvpk` (nupkg SHA-256 `ac9be73846d9a7a76483f8684abd1503cd50703b992727429dcdc045589c5e08`), used only by `toolseleasepack.ps1`. The Chrome extension in `extensions/chrome` has no third-party code.

Notable transitive packages: the Microsoft.Extensions 10.0.14 family, Serilog.Extensions.Logging 10.0.0, SQLitePCLRaw 2.1.13 (native SQLite), MessagePack, Nerdbank.MessagePack, Nerdbank.Streams, Newtonsoft.Json, Microsoft.VisualStudio.Threading and Microsoft.VisualStudio.Validation (via StreamJsonRpc), and CsWin32's Win32 metadata packages (build only).

After changing a reference, run `dotnet restore ALDREN.sln --force-evaluate` and commit the updated lock files.
