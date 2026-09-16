#!/bin/bash
# Makine slot kilidi — C-00 (2026-09-16; v2 FIFO 2026-09-17). Kullanım (çağıran kabuğun pid'i kilide yazılır; kabuk yaşadıkça kilit geçerli):
#   tools/slot.sh acquire unity <etiket>    # bloklar; slot alınca 0 döner (en fazla 2 Unity batch)
#   tools/slot.sh acquire blender <etiket>  # en fazla 1 ağır Blender CLI (render/bake/sim)
#   tools/slot.sh release unity|blender <etiket>
#   tools/slot.sh status
# Kural: Unity batch (test/build/probe/bake) ve ağır Blender CLI başlatmadan ÖNCE acquire (aynı kabukta), bitince release.
# v2: bekleyenler FIFO — acquire önce kuyruğa bilet yazar; boş slotu yalnız en eski canlı bilet alır (açlık yok).
set -u
BASE=/tmp/dd-slots; mkdir -p "$BASE/unity" "$BASE/blender" "$BASE/queue/unity" "$BASE/queue/blender"
limit(){ case "$1" in unity) echo 2;; blender) echo 1;; *) echo 1;; esac; }
clean(){ local k=$1; for f in "$BASE/$k"/* "$BASE/queue/$k"/*; do [ -e "$f" ] || continue; pid=${f##*.}; kill -0 "$pid" 2>/dev/null || rm -f "$f"; done; }
oldest(){ ls "$BASE/queue/$1" 2>/dev/null | sort | head -1; }
cmd=${1:-status}; kind=${2:-unity}; tag=${3:-anon}; owner=$PPID
case "$cmd" in
  acquire)
    max=$(limit "$kind"); start=$(date +%s)
    ticket=$(printf '%012d-%06d.%s.%s' "$start" "$owner" "$tag" "$owner")
    echo "$(date -u +%FT%TZ) $tag" > "$BASE/queue/$kind/$ticket"
    trap 'rm -f "$BASE/queue/$kind/$ticket"' EXIT
    while :; do
      clean "$kind"
      [ -e "$BASE/queue/$kind/$ticket" ] || echo "$(date -u +%FT%TZ) $tag" > "$BASE/queue/$kind/$ticket"
      if [ "$(oldest "$kind")" = "$ticket" ]; then
        for i in $(seq 1 "$max"); do
          if ! ls "$BASE/$kind"/$i.* >/dev/null 2>&1; then
            f="$BASE/$kind/$i.$tag.$owner"
            if ( set -o noclobber; echo "$(date -u +%FT%TZ) $tag" > "$f" ) 2>/dev/null; then
              rm -f "$BASE/queue/$kind/$ticket"
              echo "SLOT $kind#$i -> $tag (pid $owner; bekleme $(( $(date +%s)-start )) s)"; exit 0
            fi
          fi
        done
      fi
      sleep 20
    done ;;
  release) rm -f "$BASE/$kind"/*."$tag".*; echo "RELEASED $kind $tag"; exit 0 ;;
  status)
    for k in unity blender; do clean "$k"
      echo "== $k ($(ls "$BASE/$k" | wc -l | tr -d ' ')/$(limit "$k"))"
      for f in "$BASE/$k"/*; do [ -e "$f" ] && echo "  $(basename "$f"): $(cat "$f")"; done
      q=$(ls "$BASE/queue/$k" 2>/dev/null | sort); [ -n "$q" ] && { echo "  kuyruk:"; for f in $q; do echo "    $f: $(cat "$BASE/queue/$k/$f")"; done; }
    done; exit 0 ;;
esac
