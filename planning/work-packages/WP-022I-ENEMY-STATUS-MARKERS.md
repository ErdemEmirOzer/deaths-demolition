# WP-022I — Düşman durum işaretleri (WP-055) + r8/r12 & Special kareleri AD turu

**Sahip:** S-09 · **Taban:** main `5e417fc` (WP-055) · **Dal:** `wp-022i-enemy-status` · **Kredi:** 0 · **Slot:** `wp-022i` · **Hat:** HIZLI HAT §6 · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "WP-055 main'de: `ISpecialReactionReceiver`/`ZombieSpecialReactions` (Netted/Dazed/Pulled durumları, süreler) → WP-022I düşman durum işaretleri + r8/r12 & Special kareleri (S-10 run4) AD turu."

## Kapsam
- A. `UiEnemyStatusMarkers`: HUD marker canvas'ında 16'lık çip havuzu; canlı zombiler (`ZombieEnemy.Live`) için `IsNetted` → "NET" (StateWarning), `IsDazed` → "DAZED" (StateDanger) baş üstü çipi; ekran dışı/ölü atlanır; kullanılmayan çip park (SetActive yok); metin yalnız durum değişince (literal); kare başı 0 ayırma (test).
- B. Pulled: WP-055 kalıcı durum sunmuyor (`LastSpecialReaction` anlık) → işaret yok (devir: S-03 `IsPulled`/süre gelirse tek satır).
- C. AD turu: S-10 run4 r8/r12 kareleri henüz yok (run4 3. roundda öldü) → AD turu bu paket dışına (kareler gelince tek tur: r8/r12 + Special + durum işaretleri).
