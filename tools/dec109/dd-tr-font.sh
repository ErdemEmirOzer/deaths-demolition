#!/bin/zsh
# DEC-109 (WP-068, C-00 30.09) · builds "Shippori Mincho B1 DD-TR" (Ğ ğ İ Ş ş, OFL 1.1 derivative) from the original
# Shippori Mincho B1 Bold. The fontTools interpreter is the caller's: --venv DIR names a venv that has fontTools; nothing
# is installed by this script, in the repo or in the system Python.
#
# Usage: tools/dec109/dd-tr-font.sh --venv DIR --src ORIGINAL.ttf [--report FILE]
#   ORIGINAL.ttf must be the upstream file (SHA-256 below; the 24.09 import, also kept in KADRO wp-068/fonts-src).
#   Writes Assets/_Prototype/UI/Art/Fonts/ShipporiMinchoB1/ShipporiMinchoB1DDTR-Bold.ttf and copies the G/S/I kerning
#   pairs to Ğ/Ş/İ in kerning.json beside it (idempotent). Then rebuild the Display atlas in Unity:
#   -executeMethod DD.Prototype.UI.EditorTools.UiFontBuilder.BuildDisplay
# Exit: 0 written and checked · 1 a check failed (nothing written) · 2 wrong source · 64 usage.
ORIGINAL_SHA=d20f3981afb8bceda5fdf8f0fb29ba51eb21518644612ccd8a183e5bd433e25a
VENV= SRC= REPORT=
while [ $# -gt 0 ]; do
  case "$1" in
    --venv) VENV="$2"; shift ;; --src) SRC="$2"; shift ;; --report) REPORT="$2"; shift ;;
    *) echo "usage: dd-tr-font.sh --venv DIR --src ORIGINAL.ttf [--report FILE]" >&2; exit 64 ;;
  esac
  shift
done
[ -n "$VENV" ] && [ -n "$SRC" ] || { echo "usage: dd-tr-font.sh --venv DIR --src ORIGINAL.ttf [--report FILE]" >&2; exit 64; }
PY="$VENV/bin/python"
[ -x "$PY" ] || { echo "no interpreter at $PY" >&2; exit 64; }
"$PY" -c 'import fontTools' 2>/dev/null || { echo "$PY has no fontTools" >&2; exit 64; }
SHA=$(shasum -a 256 "$SRC" | awk '{print $1}')
[ "$SHA" = "$ORIGINAL_SHA" ] || { echo "source is not the original Shippori Mincho B1 Bold (sha256 $SHA)" >&2; exit 2; }

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
DIR="$ROOT/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/UI/Art/Fonts/ShipporiMinchoB1"
set -- --src "$SRC" --out "$DIR/ShipporiMinchoB1DDTR-Bold.ttf" --kerning "$DIR/kerning.json"
[ -n "$REPORT" ] && set -- "$@" --report "$REPORT"
exec "$PY" "$ROOT/tools/dec109/dd_tr_font.py" "$@"
