# WP-022D — UI/HUD v2.3: Achievements paneli, unvan/rozet seçici, Results unlock türleri (WP-035 bağlama)

**Sahip:** S-09 · **Taban:** main `46db3ef` (WP-022C `06199dc` dâhil) · **Dal:** `wp-022d-ui-achievements` · **Kredi:** 0 · **Slot:** `wp-022d` · **Hat:** HIZLI HAT §6 (değişen süitler + tek dev build, tek acquire) · **Not:** ≤10 satır.

C-00 talimatı (2026-09-17): "WP-022C'yi bitirdikten sonra WP-022D (Achievements paneli + unvan/rozet seçici + Results tür ikonları + ilerleme çubuğu, taban a39c20c veya üstü) doğrudan devam — ayrı prompt beklemeden; §14 kalite kapısı aynı." Sözleşme: `planning/work-packages/WP-035-COSMETICS-CONTRACT.md` (ADR-035-1, S-04).

## Kapsam
- A. Achievements paneli: `ProfileService.AchievementProgressSource` → katalog sırası, nadirlik madalyonu, ilerleme çubuğu + "7 / 50", NEW çipi (pulse), 5'li sayfalar (PREVIOUS/NEXT, pad ile), açılışta `MarkAchievementsSeen()`.
- B. Profil paneli: `ProfileService.CosmeticsSource` → "PROFILE TITLE" / "PROFILE BADGE" seçenek satırları (None + katalog), kilitli = etkileşimsiz + koşul çipi, NEW çipi; `SelectTitle/SelectBadge`; açılışta `MarkContentSeen()`; `SessionSkinProvider` UI akışından.
- C. Results: `NewUnlocks` türüne göre glif (silah ikonu / dal ikonu / unvan çelengi / rozet sakura / HUD halka) + tür etiketi.
- D. Testler: EditMode view-model (sahte kaynaklar), PlayMode pad yolu (seçici + sayfalayıcı), mevcut pane testi NEW çipine uyarlandı.
- E. Kanıt: dev build yakalamaları (profile/achievements/results 1080p), AD (Fable) tek tur, planning/04 §WP-022D.
