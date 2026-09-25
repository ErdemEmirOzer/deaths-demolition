#!/bin/bash
# Makine slot kilidi — C-00 (2026-09-16; v2 FIFO 2026-09-17; v3 dinamik 3. Unity slotu 2026-09-17). Kullanım (çağıran kabuğun pid'i kilide yazılır; kabuk yaşadıkça kilit geçerli):
#   tools/slot.sh acquire unity <etiket>    # bloklar; slot alınca 0 döner (2 Unity batch; swap <10 GB ve yük <24 iken 3)
#   tools/slot.sh acquire blender <etiket>  # en fazla 1 ağır Blender CLI (render/bake/sim)
#   tools/slot.sh acquire smoke <etiket>    # release smoke (gerçek Player profilini yazar) — tek slot, eşzamanlı smoke yok
#   tools/slot.sh release unity|blender|smoke <etiket>
#   tools/slot.sh status
# Kural: Unity batch (test/build/probe/bake) ve ağır Blender CLI başlatmadan ÖNCE acquire (aynı kabukta), bitince release.
# v2: bekleyenler FIFO — acquire önce kuyruğa bilet yazar; boş slotu yalnız en eski canlı bilet alır (açlık yok).
# v4 (WP-043B, 2026-09-25 hız modu): (a) sınır (2/3) her turda yeniden hesaplanır — kuyruk başı, yük düştüğünde boşalan
#   3. slotu da alır (eskiden bekleyişin başındaki sınırla kalıyor, arkasındakileri de bekletiyordu); (b) kilit/bilet
#   içeriği sahibin başlangıç zamanını da taşır; pid yeniden kullanılırsa (farklı başlangıç) bayat sayılır; (c) release
#   önce yalnız kendi sahibinin (aynı kabuk) dosyasını siler, yoksa eski davranış (etikete göre). DD_SLOT_BASE = test dizini.
set -u
BASE=${DD_SLOT_BASE:-/tmp/dd-slots}; mkdir -p "$BASE/unity" "$BASE/blender" "$BASE/smoke" "$BASE/queue/unity" "$BASE/queue/blender" "$BASE/queue/smoke"
# v3 (2026-09-17): dinamik 3. Unity slotu — swap kullanımı < 10 GB ve 1 dk yük < 24 iken 3, aksi hâlde 2.
swap_mb(){ sysctl -n vm.swapusage 2>/dev/null | sed -E 's/.*used = ([0-9.]+)M.*/\1/' | cut -d. -f1; }
load1(){ sysctl -n vm.loadavg 2>/dev/null | awk '{print int($2)}'; }
# Self-test hooks only: DD_SLOT_LIMIT_FILE (limit read every round) and DD_SLOT_POLL (seconds between rounds).
limit(){ [ -n "${DD_SLOT_LIMIT_FILE:-}" ] && [ -s "$DD_SLOT_LIMIT_FILE" ] && { cat "$DD_SLOT_LIMIT_FILE"; return; }
  case "$1" in
  unity) if [ "$(swap_mb)" -lt 10240 ] 2>/dev/null && [ "$(load1)" -lt 24 ] 2>/dev/null; then echo 3; else echo 2; fi ;;
  blender) echo 1;; smoke) echo 1;; *) echo 1;; esac; }
pstart(){ ps -o lstart= -p "$1" 2>/dev/null | tr -s ' '; }
# A lock/ticket is stale when its pid is gone or (v4) the pid now belongs to a process started at another time.
clean(){ local k=$1 f pid st; for f in "$BASE/$k"/* "$BASE/queue/$k"/*; do [ -e "$f" ] || continue; pid=${f##*.}
  if ! kill -0 "$pid" 2>/dev/null; then rm -f "$f"; continue; fi
  st=$(sed -n 's/^start=//p' "$f" 2>/dev/null); [ -n "$st" ] && [ "$st" != "$(pstart "$pid")" ] && rm -f "$f"; done; }
oldest(){ ls "$BASE/queue/$1" 2>/dev/null | sort | head -1; }
cmd=${1:-status}; kind=${2:-unity}; tag=${3:-anon}; owner=$PPID
case "$cmd" in
  acquire)
    start=$(date +%s); ostart=$(pstart "$owner")
    ticket=$(printf '%012d-%06d.%s.%s' "$start" "$owner" "$tag" "$owner")
    printf '%s %s\nstart=%s\n' "$(date -u +%FT%TZ)" "$tag" "$ostart" > "$BASE/queue/$kind/$ticket"
    trap 'rm -f "$BASE/queue/$kind/$ticket"' EXIT
    while :; do
      clean "$kind"
      [ -e "$BASE/queue/$kind/$ticket" ] || printf '%s %s\nstart=%s\n' "$(date -u +%FT%TZ)" "$tag" "$ostart" > "$BASE/queue/$kind/$ticket"
      max=$(limit "$kind")   # v4: re-evaluated every round
      if [ "$(oldest "$kind")" = "$ticket" ]; then
        for i in $(seq 1 "$max"); do
          if ! ls "$BASE/$kind"/$i.* >/dev/null 2>&1; then
            f="$BASE/$kind/$i.$tag.$owner"
            if ( set -o noclobber; printf '%s %s\nstart=%s\n' "$(date -u +%FT%TZ)" "$tag" "$ostart" > "$f" ) 2>/dev/null; then
              rm -f "$BASE/queue/$kind/$ticket"
              echo "SLOT $kind#$i -> $tag (pid $owner; bekleme $(( $(date +%s)-start )) s)"; exit 0
            fi
          fi
        done
      fi
      sleep "${DD_SLOT_POLL:-20}"
    done ;;
  release) own=$(ls "$BASE/$kind"/*."$tag"."$owner" 2>/dev/null)
    if [ -n "$own" ]; then rm -f $own; else rm -f "$BASE/$kind"/*."$tag".*; fi   # v4: own lock first; legacy: by tag
    echo "RELEASED $kind $tag"; exit 0 ;;
  status)
    for k in unity blender smoke; do clean "$k"
      echo "== $k ($(ls "$BASE/$k" | wc -l | tr -d ' ')/$(limit "$k"))"
      for f in "$BASE/$k"/*; do [ -e "$f" ] && echo "  $(basename "$f"): $(cat "$f")"; done
      q=$(ls "$BASE/queue/$k" 2>/dev/null | sort); [ -n "$q" ] && { echo "  kuyruk:"; for f in $q; do echo "    $f: $(cat "$BASE/queue/$k/$f")"; done; }
    done; exit 0 ;;
esac
