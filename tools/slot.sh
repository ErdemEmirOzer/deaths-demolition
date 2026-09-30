#!/bin/bash
# Makine slot kilidi — C-00 (2026-09-16; v2 FIFO 2026-09-17; v3 dinamik 3. Unity slotu 2026-09-17). Kullanım (çağıran kabuğun pid'i kilide yazılır; kabuk yaşadıkça kilit geçerli):
#   tools/slot.sh acquire unity <etiket>    # bloklar; slot alınca 0 döner (2 Unity batch; swap <10 GB ve yük <24 iken 3)
#   tools/slot.sh acquire blender <etiket>  # en fazla 1 ağır Blender CLI (render/bake/sim)
#   tools/slot.sh acquire smoke <etiket>    # release smoke (gerçek Player profilini yazar) — tek slot, eşzamanlı smoke yok
#   tools/slot.sh acquire player <etiket>   # WP-043D: her Player başlatma (yakalama, bot, perf, smoke) — tek slot; main-ci
#                                           # koşu boyunca tutar (ses cihazı + odak paylaşılıyor: UiV3 ses testi kırıldı)
#   tools/slot.sh release unity|blender|smoke <etiket>
#   tools/slot.sh status
#   tools/slot.sh holds <kind> [<pid>]      # WP-079G D6a: salt okunur — <pid> (varsayılan: sahip) ya da atalarından biri canlı
#                                           # bir <kind> kilidi tutuyorsa 0 (+ tek satır), yoksa 1; hiçbir şey yazmaz/silmez
# Kural: Unity batch (test/build/probe/bake) ve ağır Blender CLI başlatmadan ÖNCE acquire (aynı kabukta), bitince release.
# v2: bekleyenler FIFO — acquire önce kuyruğa bilet yazar; boş slotu yalnız en eski canlı bilet alır (açlık yok).
# v4 (WP-043B, 2026-09-25 hız modu): (a) sınır (2/3) her turda yeniden hesaplanır — kuyruk başı, yük düştüğünde boşalan
#   3. slotu da alır (eskiden bekleyişin başındaki sınırla kalıyor, arkasındakileri de bekletiyordu); (b) kilit/bilet
#   içeriği sahibin başlangıç zamanını da taşır; pid yeniden kullanılırsa (farklı başlangıç) bayat sayılır; (c) release
#   önce yalnız kendi sahibinin (aynı kabuk) dosyasını siler, yoksa eski davranış (etikete göre). DD_SLOT_BASE = test dizini.
# K3 (WP-079G D1, C-00 30.09 M5): (a) clean bir kilidi/bileti yalnız kayıtlı ve güncel başlangıç zamanı ikisi de dolu ve
#   farklıyken siler (geçici boş `ps` yanıtı canlı kilidi silmez); (b) DD_SLOT_TIMEOUT=<s> (boş/0 = sınırsız, eski davranış):
#   bekleme bu süreye ulaşırsa bilet silinir, stderr'e `SLOT <kind> TIMEOUT after <n>s` + tutucular, çıkış 75; (c) bekleyen
#   acquire INT/TERM/HUP alınca biletini siler (130/143/129) — bekleme `sleep & wait` ile, tuzak hemen koşar.
set -u
BASE=${DD_SLOT_BASE:-/tmp/dd-slots}; [ "${1:-}" = holds ] || mkdir -p "$BASE/unity" "$BASE/blender" "$BASE/smoke" "$BASE/player" "$BASE/queue/unity" "$BASE/queue/blender" "$BASE/queue/smoke" "$BASE/queue/player"
# v3 (2026-09-17): dinamik 3. Unity slotu — swap kullanımı < 10 GB ve 1 dk yük < 24 iken 3, aksi hâlde 2.
swap_mb(){ sysctl -n vm.swapusage 2>/dev/null | sed -E 's/.*used = ([0-9.]+)M.*/\1/' | cut -d. -f1; }
load1(){ sysctl -n vm.loadavg 2>/dev/null | awk '{print int($2)}'; }
# Self-test hooks only: DD_SLOT_LIMIT_FILE (limit read every round) and DD_SLOT_POLL (seconds between rounds).
limit(){ [ -n "${DD_SLOT_LIMIT_FILE:-}" ] && [ -s "$DD_SLOT_LIMIT_FILE" ] && { cat "$DD_SLOT_LIMIT_FILE"; return; }
  case "$1" in
  unity) if [ "$(swap_mb)" -lt 10240 ] 2>/dev/null && [ "$(load1)" -lt 24 ] 2>/dev/null; then echo 3; else echo 2; fi ;;
  blender) echo 1;; smoke) echo 1;; player) echo 1;; *) echo 1;; esac; }
# D1 review MUST-1: lstart is locale/TZ-formatted — a caller with LANG=tr_TR or TZ=UTC would read every live lock as
# "started at another time" and delete it; one fixed format for every writer and reader.
pstart(){ env -u TZ LC_ALL=C ps -o lstart= -p "$1" 2>/dev/null | tr -s ' '; }
# A lock/ticket is stale when its pid is gone or (v4) the pid now belongs to a process started at another time.
# K3: both start times must be known — an empty `ps` answer (transient) never removes a live pid's lock.
clean(){ local k=$1 f pid st now; for f in "$BASE/$k"/* "$BASE/queue/$k"/*; do [ -e "$f" ] || continue; pid=${f##*.}
  if ! kill -0 "$pid" 2>/dev/null; then rm -f "$f"; continue; fi
  st=$(sed -n 's/^start=//p' "$f" 2>/dev/null); [ -n "$st" ] || continue
  now=$(pstart "$pid"); [ -n "$now" ] && [ "$st" != "$now" ] && rm -f "$f"; done; }
holders(){ local f; for f in "$BASE/$1"/*; do [ -e "$f" ] && echo "  $(basename "$f"): $(head -1 "$f")"; done; }
oldest(){ ls "$BASE/queue/$1" 2>/dev/null | sort | head -1; }
cmd=${1:-status}; kind=${2:-unity}; tag=${3:-anon}; owner=${DD_SLOT_OWNER:-$PPID}
# WP-066N: DD_SLOT_OWNER lets a tool take the lock for its caller's shell (userdata-guard holds 'player' from snapshot to
# restore); acquire is re-entrant — when the owner or one of its ancestors already holds a lock of this kind it returns at
# once (a Player launched inside a guarded window must not wait for its own guard).
kill -0 "$owner" 2>/dev/null || owner=$PPID
is_ancestor(){ local a=$1 p=$2 n=0; while [ -n "$p" ] && [ "$p" -gt 1 ] 2>/dev/null && [ $n -lt 64 ]; do [ "$p" = "$a" ] && return 0; p=$(ps -o ppid= -p "$p" 2>/dev/null | tr -d ' '); n=$((n+1)); done; return 1; }
case "$cmd" in
  acquire)
    to=${DD_SLOT_TIMEOUT:-0}
    case "$to" in *[!0-9]*) echo "SLOT: DD_SLOT_TIMEOUT must be whole seconds (got '$to')" >&2; exit 64 ;; esac
    to=$((10#$to))
    clean "$kind"
    for held in "$BASE/$kind"/*; do [ -e "$held" ] || continue
      if is_ancestor "${held##*.}" "$owner"; then echo "SLOT $kind reentrant -> $tag (held by ancestor pid ${held##*.}: $(basename "$held"))"; exit 0; fi
    done
    start=$(date +%s); ostart=$(pstart "$owner")
    ticket=$(printf '%012d-%06d.%s.%s' "$start" "$owner" "$tag" "$owner")
    printf '%s %s\nstart=%s\n' "$(date -u +%FT%TZ)" "$tag" "$ostart" > "$BASE/queue/$kind/$ticket"
    trap 'rm -f "$BASE/queue/$kind/$ticket"' EXIT
    # K3: a waiter killed with INT/TERM/HUP drops its ticket at once (the sleep runs as `& wait`, so the trap is not deferred).
    zz=""; drop(){ [ -n "$zz" ] && kill "$zz" 2>/dev/null; rm -f "$BASE/queue/$kind/$ticket"; exit "$1"; }
    trap 'drop 130' INT; trap 'drop 143' TERM; trap 'drop 129' HUP
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
      poll=${DD_SLOT_POLL:-20}
      if [ "$to" -gt 0 ]; then
        waited=$(( $(date +%s)-start ))
        if [ "$waited" -ge "$to" ]; then
          rm -f "$BASE/queue/$kind/$ticket"
          { echo "SLOT $kind TIMEOUT after ${waited}s ($tag, pid $owner; DD_SLOT_TIMEOUT=$to) — holders:"; holders "$kind"; } >&2
          exit 75
        fi
        [ $(( to-waited )) -lt "${poll%%.*}" ] 2>/dev/null && poll=$(( to-waited ))
      fi
      sleep "$poll" & zz=$!; wait "$zz"; zz=""
    done ;;
  release) own=$(ls "$BASE/$kind"/*."$tag"."$owner" 2>/dev/null)
    if [ -n "$own" ]; then rm -f $own; else rm -f "$BASE/$kind"/*."$tag".*; fi   # v4: own lock first; legacy: by tag
    echo "RELEASED $kind $tag"; exit 0 ;;
  holds)   # WP-079G D6a (C-00 30.09 kilit sırası player → unity): the acquire re-entrancy notion, read-only (no clean)
    p=${3:-$owner}
    case "$p" in ''|*[!0-9]*) echo "SLOT: holds <kind> [<pid>] — pid must be a number (got '$p')" >&2; exit 64 ;; esac
    for held in "$BASE/$kind"/*; do [ -e "$held" ] || continue; hp=${held##*.}
      kill -0 "$hp" 2>/dev/null || continue   # D6a review: a dead holder's lock never counts (clean() removes it later)
      is_ancestor "$hp" "$p" || continue
      # clean()'s stale rule (pid reused: both start times known and different) — skipped here, never removed
      st=$(sed -n 's/^start=//p' "$held" 2>/dev/null); now=$(pstart "$hp")
      [ -n "$st" ] && [ -n "$now" ] && [ "$st" != "$now" ] && continue
      echo "SLOT $kind held by pid $hp ($(basename "$held")), an ancestor of pid $p"; exit 0
    done; exit 1 ;;
  status)
    for k in unity blender smoke player; do clean "$k"
      echo "== $k ($(ls "$BASE/$k" | wc -l | tr -d ' ')/$(limit "$k"))"
      for f in "$BASE/$k"/*; do [ -e "$f" ] && echo "  $(basename "$f"): $(cat "$f")"; done
      q=$(ls "$BASE/queue/$k" 2>/dev/null | sort); [ -n "$q" ] && { echo "  kuyruk:"; for f in $q; do echo "    $f: $(cat "$BASE/queue/$k/$f")"; done; }
    done; exit 0 ;;
esac
