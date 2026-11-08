# Phase 5 plan: Action Brain (milestone M2)

Status: approved 2026-11-08 ("move to phase 5 and do that whole thing; you are allowed to download stuff if needed"). Short record; acceptance afterwards.

## Downloads and system changes
| Item | Why | Pin |
|---|---|---|
| NuGet `FlaUI.UIA3` 5.0.0 (already cached from S5) | UI Automation snapshots and element actions | lock file |
| NuGet `PdfPig` 0.1.16 | Reading PDFs locally | lock file |
| NuGet `DocumentFormat.OpenXml` 3.5.1 | Reading DOCX locally | lock file |
| NuGet `System.Data.OleDb` 10.0.14 | Querying the Windows Search index (read-only) | lock file |

No Windows settings change. Core becomes per-monitor DPI aware (a process setting, nothing persistent) and targets the Windows 10 SDK API surface (net10.0-windows10.0.26100.0) for Windows Graphics Capture and Windows OCR.

Deferred: the GUI-grounding model and stronger-OCR evaluations (experiments on your screenshots, ADR-0013), clipboard change monitoring (processing only on request needs none), restoring deleted files from the Recycle Bin by voice.

## What gets built
| Piece | Where | Notes |
|---|---|---|
| Window manager | `Aldren.Windows` | Monitors and work areas; snap left/right/top/bottom, maximise, minimise, restore, centre, move to the other monitor; named layouts saved per monitor configuration, applied by moving windows and launching missing apps; a built-in "Coding" layout |
| File service | `Aldren.Windows` | Windows Search index, Recent items, Downloads, VS Code recent workspaces, PDF/DOCX/text readers, duplicates (size then SHA-256), move and rename with an undo journal, deletes to the Recycle Bin only, system folders refused, overwrites and bulk changes previewed and confirmed |
| Perception | `Aldren.Perception` | Scoped UI Automation snapshot of the window you're using (ALDREN's own windows skipped), element IDs tied to the snapshot (stale IDs refused), focused element and element under the cursor ("this", "that"), Windows Graphics Capture (the capture border stays as the "looking" indicator), Windows OCR, numbered marks over a short candidate list, local vision through the resident Qwen3.5-4B projector |
| Name-first resolution | `Aldren.Perception` | ADR-0013: match the request against element names, automation IDs and roles; one clear match needs no model; otherwise vision chooses among at most eight marked candidates |
| UI actions | Core tools | Invoke, set text (never into password fields), select, toggle, expand, scroll, each checked afterwards; controls whose names suggest sending, buying, deleting or signing out ask first; refused in gaming mode |
| Action Brain | Core | Routed from "on my screen", "this error", "click…" or the Quick Brain's hand-off; observes, plans with tools, acts through the Tool Gateway with the screen marked untrusted (tier 2+ always confirmed), re-observes after each step, at most five steps |
| Clipboard | Core tools | Read on request (refused when a password manager marks the clipboard private), copy text; contents treated as untrusted and sensitive |

## Exit criteria (checked later, with you)
The brief's example commands pass a 50-task local benchmark (task list in the acceptance file); every action is logged; destructive actions are gated.
