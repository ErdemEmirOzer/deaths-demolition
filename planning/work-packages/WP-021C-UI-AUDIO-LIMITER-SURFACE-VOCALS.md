# WP-021C — UI Audio, Master Limiter, Surface Tags & Special Vocals

> Yürütme promptu (C-00, 2026-09-17) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-021C.

**Oturum:** S-08 · **Model:** ses tasarımı/üretimi Fable 5.1 (429 → Opus stand-in, README notu), teknik bağlama Opus 5 · **Kredi:** 0 (Higgsfield yok; mevcut AP-011 + WP-021B klipleri, Blender/ffmpeg/sox katmanlama) · **Taban:** main `48292245` (yeni worktree `wp-021c/`) · **Slot:** `tools/slot.sh acquire unity wp-021c` (≤2 Unity), swap >12 GB ise Unity açma · **Direktif:** HIGGSFIELD-ONCELIK §9 (48k/24-bit, derinlik/berraklık/katman, A/B kapısı), §12 (boş satır = FAIL).

## Kapsam
A. **UI sesleri:** S-09 `UiEvents` (39 olay, `planning/work-packages/EVENT-COVERAGE-MATRIX-UI.md`) → `AudioEventMap` satırları + SFX anahtarları; her olayın kendi kimliği (hover/confirm/back/tab/slider/toggle/error/unlock/new-best/threat çipleri SURROUNDED · BEHIND YOU · ELITE|ULTRA NEARBY). Sessiz olay yok; olay↔matris enum testi S-09'unkiyle uyumlu.
B. **Mixer:** `PrototypeAudio.MasterVolume` / `MasterVolumeChanged` → mixer "MasterVolume" bağlaması; Master bus limiter (true-peak ≤ −1 dBTP, LUFS hedefi WP-021B'deki ledger), snapshot geçişlerinde tıklama yok.
C. **SurfaceTag:** `SurfaceResolver` (ADR-021-1 v2) için `SurfaceTag` bileşeni + WP-014/017 dünya prefab sınıfları (stone/wood/earth/gravel/water/bridge) — S-05 ile tek mesaj eşgüdüm (prefab etiketleme kuralını README'ye yaz; sahne dosyasına dokunma, prefab/veri ile çöz); AP-015P plakaları gelince aynı kural uygulanacak.
D. **Special/Elite/Ultra vokal seti:** tier kimlikleri (B·B·B, WP-028 `TierBehaviourProfile`) için ayrı idle/aggro/attack/hurt/death vokalleri (pitch/formant/katman ile Standard'dan türetilmiş, kendi kimliği), `EVENT-COVERAGE-MATRIX.md` satırları doldurulur.

## Kapılar
Değişen modül testleri (Audio + UI olay eşleme) + A/B dinleme kapısı (WP-021B kalıbı: önce/sonra WAV, ölçüm tablosu) · katmanlı klipler 48k/24-bit · limiter ölçümü (loudness/true-peak raporu) · tek commit, ≤15 satır not, rebase yok (main ilerlerse C-00 cherry-pick eder). Planning/04'e yalnız kendi bölümün.

## S-09 uzlaşısı (WP-022B, önceden)
`UiSettings.MasterVolume` → `PrototypeAudio.MasterVolume` (tek sahip S-08) + `MasterVolumeChanged(float)`; mixer parametre adı yalnız S-08'de. `UiEvents.Raised(UiEvent, detail)` 39 olay; mevcut 4 `PrototypeAudio.Navigate/Confirm/Back/Denied` `UiSound` üzerinden sürer, çift çalmamak için S-08 sahibi seçer.
