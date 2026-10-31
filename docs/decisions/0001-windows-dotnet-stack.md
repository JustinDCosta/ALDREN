# ADR-0001: Windows-only, C#/.NET 10; Python only for offline tools

- **Status:** Accepted (2026-10-31)

## Context
ALDREN targets one Windows 11 laptop and needs deep OS integration (UI Automation, WinRT, Win32, Core Audio, Credential Manager), a low-overhead always-on runtime and a polished native UI. The user already ships C#/WPF apps.

## Decision
- One language and runtime for everything that runs: C# on .NET 10 (LTS), for both the background runtime and the UI.
- Native components are used as libraries or child processes: llama.cpp (llama-server), sherpa-onnx (C# bindings), ONNX Runtime.
- Python is used only for offline tooling (wake-word training, benchmarks), in an isolated environment, never in the runtime.

## Alternatives considered
- **Rust + Tauri:** excellent performance, but two languages (Rust and TypeScript), weaker libraries for UI Automation and Google APIs, and harder maintenance for the user.
- **Python runtime:** the best ML ecosystem, but heavy memory use (especially with PyTorch), slow startup and painful packaging for an always-on app.
- **Electron/TypeScript:** too heavy for an always-on assistant.

## Consequences
- Direct access to Windows APIs (CsWin32, WinRT projections), FlaUI, Microsoft.Extensions.AI, the official Google .NET clients and Playwright for .NET.
- The .NET 10 SDK must be installed (only runtimes are present today).
- ML features without a .NET path run as separate native processes behind HTTP or C APIs.

## Revisit if
A critical capability has no viable .NET or native path.
