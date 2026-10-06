#!/usr/bin/env python3
"""WP-089 (C-00 05.10, iCloud move A) · the KADRO root for the Python tools — the shell side is dd-env.sh DD_KADRO_REL.

A constant relative to the home folder, never taken from the environment (the canonical guard path derives from it).
Moved 06.10 (C-00, user: no iCloud work on this machine): Desktop/KADRO → Dev/KADRO, outside iCloud's Desktop &
Documents. Only KADRO_REL spells it, together with dd-env.sh DD_KADRO_REL and slot.sh's fallback literal;
tools/tests/test_dd_kadro.py pins the three together and keeps Desktop/ and Documents/ out of the tools' code."""
from pathlib import Path

KADRO_REL = "Dev/KADRO"
KADRO = Path.home() / KADRO_REL
KPROJ = KADRO / "context" / "projects" / "deaths-demolition"
