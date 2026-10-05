# AGENTS.md — Deaths Demolition (Codex)

This repository is a Unity 2022.3.22f1 / URP 14 game (`prototype/DeathsDemolition_PlayableConcept`). It is coordinated by **C-00** (Claude). Codex works on design, research and asset-production tasks given as task files.

## Before any work, read

1. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/CODEX-SISTEM-R2.md`: binding rules, folders, delivery and report format. Written in Turkish; reports are written in Turkish.
2. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/KADRO-CODEX-AJANLARI.md`: the expert roster and how to use sub-agents.
3. The task file you were given: `…/c-00/codex/prompts/CX-*.md`.

## Hard rules (summary; the files above are authoritative)

- **Repository and main:**
  - Production work writes only to `…/deaths-demolition/asset-production/AP-0NN/`, never to this repo or to `main`.
  - Code tasks use their own worktree and branch. No push, reset, rebase, amend or force. No `git add -A`.
- **Shared machine:**
  - Blender, Unity and Player runs go through `tools/slot.sh`.
  - Every Player or PlayMode run is wrapped in the user-data guard.
  - Do not start Unity or Blender with less than 15 GB free disk.
  - No bulk copies. Files may be iCloud "dataless": read repo files with `git show main:<path>`.
- **No installs, purchases, account creation or logins.** Use only commercially licensed sources and log them in `LICENSES.csv`. Never write secrets.
- **Delivery:**
  - Put a manifest and SHA256SUMS in every delivery.
  - Write a report to `…/c-00/codex/reports/`. The final message to the user is one line: the report path.
  - Never claim "ready" without evidence.

## Sub-agents

Project agents live in `.codex/agents/` (`dd_*`): art director, game designer, concept artist, 3D modeller, technical rigger, character animator, animation specialist, Blender specialist, sound designer, sound engineer, UI/UX designer, graphic designer, Unity specialist, researcher, validator.

Delegate to them by name. Run `dd_validator` before every delivery.
