#!/bin/zsh
# WP-112 (C-00 12.10, user decision: no prototype trace in the release build) · tools/app-display-name.sh <app> <name>
#
# Sets the name macOS shows for a built macOS player — Dock, app switcher, menu bar, About and the window title all read
# Info.plist CFBundleName (measured on a dev player whose Info.plist alone was changed: localizedName, the menu-bar owner and
# the window title all became the new name — K/wp-112/evidence/s01-appname/probe.txt). Only CFBundleName and
# CFBundleDisplayName change: productName (the executable, Application.productName), CFBundleIdentifier and companyName stay,
# so the user's data does not move (PlayerPrefs / Screenmanager keys in ~/Library/Preferences/<bundle id>.plist, the profile in
# ~/Library/Application Support/<company>/<productName>; the same run wrote only the bundle-id plist, guard verified=1) and the
# guard, player-run and every tool keep their paths. Unity seals the Info.plist in its ad-hoc signature, so the bundle is
# re-signed ad-hoc and verified (--deep --strict). Exit: 0 · 1 Info.plist or codesign failed · 64 usage
set -u
[ $# -eq 2 ] && [ -n "$2" ] && [ -f "$1/Contents/Info.plist" ] || { echo "usage: app-display-name.sh <app with Contents/Info.plist> <name>" >&2; exit 64; }
APP=$1; NAME=$2; PL="$APP/Contents/Info.plist"; PB=/usr/libexec/PlistBuddy
"$PB" -c "Set :CFBundleName $NAME" "$PL" || exit 1
"$PB" -c "Set :CFBundleDisplayName $NAME" "$PL" 2>/dev/null || "$PB" -c "Add :CFBundleDisplayName string $NAME" "$PL" || exit 1
[ "$("$PB" -c "Print :CFBundleName" "$PL")" = "$NAME" ] && [ "$("$PB" -c "Print :CFBundleDisplayName" "$PL")" = "$NAME" ] || exit 1
codesign --force --sign - "$APP" 2>/dev/null || exit 1
codesign --verify --deep --strict "$APP" 2>/dev/null || exit 1
echo "display name \"$NAME\" · bundle id $("$PB" -c "Print :CFBundleIdentifier" "$PL") · executable $("$PB" -c "Print :CFBundleExecutable" "$PL") · re-signed ad-hoc"
