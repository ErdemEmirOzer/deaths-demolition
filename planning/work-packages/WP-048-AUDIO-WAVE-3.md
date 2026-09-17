# WP-048 — Audio Wave 3 (kredisiz kısım)

> C-00 dağıtımı (2026-09-17, sohbet; HIZ MODU) — birebir kayıt. Sonuç: `planning/04-IMPLEMENTATION-PLAN.md` §WP-048.

Sıradaki paket **WP-048 — Audio Wave 3** kredisiz kısmıyla şimdi başlar (ayrı prompt yok):
A. **Olay bağlama:** S-03 WP-040 `EnemyCombatEvents` (telegraph/attack_hit/stagger/armor_break_<region>/death_<tier>/spawn_rise) → tier vokalleri (WP-021C seti), zırh kırılma bölge sesleri; S-06 WP-032C klip olayları (eject/chamber/mag_out/mag_in/switch/lowered/raised/pickup_grab/pickup_done) → silah A/B/C foley (v006 ateş sesleri + reload/mekanik katmanları kredisiz), pickup üst gövde sesleri.
B. **Yüzeyler:** AP-015P plakaları (stone/earth/gravel/wood/bridge/water) `SurfaceTag` → ayak sesi seti tamamlama (su sığ/derin, çakıl, kaldırım), S-05 WP-038 ile.
C. **Kırılabilirler:** AP-017 aileleri (fener taş/kağıt, planter, çit, balya, bank, tabela, shoji) kırılma sesleri — kredisiz katmanlama (mevcut ahşap/seramik/kumaş kaynakları), kalite kapısı §9; kredi gerekirse listele (harcama yok, kredi yükleme sonrası).
D. **Look B ambiyansı:** ıslak arduvaz/yağmur sonrası damlama, uzak gök gürültüsü katmanı, koi havuzu su ambiyansı (`AmbienceDirector` bölge katmanı).
Kapılar: matris boş satır yok (§12), 48k/24-bit, limiter, değişen süitler; §6 yalın zincir; tek commit.

Ekler: Look B tanımı `wp-029/evidence/look-candidates/` ("B — Islak Arduvaz"); main 56ebec9 (WP-040 + WP-032B + WP-037B) ile olay bağlama şimdi; AP-017 olay tablosu `asset-production/AP-017/exports/AP017-breakable-event-table-v001.md` (BreakStone 12 · BreakCeramic 6 · BreakWood 10 · BreakBamboo 9 · BreakPaper 6 · BreakStraw 5); slot v4: release smoke öncesi `tools/slot.sh acquire smoke <etiket>`.
