# WP-045 — Run Telemetry & Simulator Calibration v1

> Yürütme promptu (C-00, 2026-09-17) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-045.

**Oturum:** S-04 · **Model:** Opus 5; olay taksonomisi için analytics-engineer tek tur · **Kredi:** 0 · **Taban:** main `282ea60` (yeni worktree `wp-045/`) · **Slot:** FIFO (PlayMode/bot koşusu için).

## Amaç
Denge önerileri simülasyona dayanıyor; gerçek koşu verisiyle kalibre edilmeden ürün kararına güven düşük. Yerel, çevrimdışı koşu telemetrisi + simülatör↔gerçek karşılaştırma.

## Kapsam
A. **RunTelemetry (ADR-045-1):** koşu başına `persistentDataPath/telemetry/run-<utc>.json` (`dd-run-telemetry-v1`): round başına altın kaynak/gider, cüzdan, kart/reroll, silah/dal seviyeleri, TTK örnekleri (tier × zırh), Special kullanım, alınan hasar/ölüm, spawn karışımı, süre; 0 ayırma sıcak yolda (ADR-037-1: struct tampon, yazım round sonunda), boyut ≤ 200 KB/koşu, profil/kimlik verisi yok, kapatılabilir (`-ddTelemetry 0`).
B. **Kalibrasyon aracı** (`Diagnostics/Editor`): S-10 `QaRunBot` (WP-026) koşuları → telemetri → EconomySimulator tahminiyle karşılaştırma (altın eğrisi, TTK, seviye zamanı; sapma %); rapor `wp-045/evidence/calibration-v1.md`; simülatörde bant dışı sapmalar için model düzeltmesi (veri değeri değişmez).
C. **QaRunBot entegrasyonu:** `tools/qa/regression.sh` çıktısına telemetri özetinin eklenmesi S-10 ile tek satır eşgüdüm (S-10 WP-044'te; dosya kesişimi yok — sen `Diagnostics/Telemetry/`, S-10 `Diagnostics/Qa/`).
D. Testler: telemetri şema/0-alloc/kapatma testleri; batch koşu kanıtı (bot 4 round).

## Teslim
Tek commit, ≤15 satır not (kalibrasyon sapma tablosu + öneri etkisi), rebase yok; planning/04 yalnız kendi bölümün.

---
Not (S-04, olay taksonomisi — analytics-engineer turu Opus stand-in): kaynak olaylar `RunEvents.{RunStarted,RoundStarted,RoundCompleted,GoldChanged,RunEnded}`, `DamageEvents.{EnemyDamaged,EnemyDied,PlayerAttacked}`, `WeaponEvents.{SpecialWeaponSpawned,SpecialWeaponPickedUp}`; TTK = aynı `LifeId` için ilk oyuncu vuruşu → ölüm; kart alımı = round başı/sonu `WeaponModifierState` tier farkı; reroll sayısı yalnız koşu toplamı (özet). Spawn karışımı = kill karışımı (tamamlanan round'da eşdeğer). Bot dükkânı atlar → satın alınabilirlik kalibre edilmez (raporda not).
