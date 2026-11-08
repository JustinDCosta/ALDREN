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

Notable transitive packages: the Microsoft.Extensions 10.0.14 family, Serilog.Extensions.Logging 10.0.0, SQLitePCLRaw 2.1.13 (native SQLite), MessagePack, Nerdbank.MessagePack, Nerdbank.Streams, Newtonsoft.Json, Microsoft.VisualStudio.Threading and Microsoft.VisualStudio.Validation (via StreamJsonRpc), and CsWin32's Win32 metadata packages (build only).

After changing a reference, run `dotnet restore ALDREN.sln --force-evaluate` and commit the updated lock files.
