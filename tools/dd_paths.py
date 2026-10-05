#!/usr/bin/env python3
"""WP-089 (C-00 05.10, iCloud move A) · the KADRO root for the Python tools — the shell side is dd-env.sh DD_KADRO_REL.

A constant relative to the home folder, never taken from the environment (the canonical guard path derives from it).
Move day: only KADRO_REL changes (Desktop/KADRO → Dev/KADRO), together with dd-env.sh DD_KADRO_REL and slot.sh's
fallback literal; tools/tests/test_dd_kadro.py pins the three together."""
from pathlib import Path

KADRO_REL = "Desktop/KADRO"
KADRO = Path.home() / KADRO_REL
KPROJ = KADRO / "context" / "projects" / "deaths-demolition"
