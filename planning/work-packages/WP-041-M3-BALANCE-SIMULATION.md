# WP-041 — Economy & Progression Balance Simulation v1

> Yürütme promptu (C-00, 2026-09-17) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-041; öneri `WP-041-BALANCE-PROPOSAL.md`.

**Oturum:** S-04 · **Model:** Opus 5 teknik; denge "ana hatları"/öneri yorumu için Fable 5.1 game-designer tek tur (429 → Opus stand-in) · **Kredi:** 0 · **Taban:** main `a39c20c` (yeni worktree `wp-041/`; wp-035 worktree'sini kaldır) · **Slot:** `tools/slot.sh acquire unity wp-041` (FIFO), swap >12 GB ise Unity açma.

## Amaç
M3 içeriği (3 silah A/B/C + silaha özgü çekirdek dallar WP-034, tier B·B·B WP-028/036, zırh WP-006/008, Threat Director WP-033, Special Weapon WP-007, ekonomi DEC-069..075 WP-008, başarım/unlock WP-023/035) birlikte hiç simüle edilmedi. Kod değeri değiştirmeden **ölçülebilir denge tablosu** + öneri.

## Kapsam
A. **EconomySimulator** (`Diagnostics/Editor`, ADR-027-1 SpawnMixSimulator kalıbı; S-03'ün spawn karışımını yeniden yazma, çağır): R1–R15 deterministik koşular (seed), girdiler gerçek verilerden (WeaponDefinition, TierBehaviourProfile, ArmorChanceSettings, ekonomi ayarları, unlock kataloğu); modeller: hedefe göre TTK (tier × zırh durumu × silah/dal seviyesi), round başına altın kaynak/gider, upgrade satın alınabilirlik zamanı, Special Weapon kullanım oranı, "ölüm riski vekili" (gelen DPS / oyuncu HP); çıktı `wp-041/evidence/balance-v1.json` + markdown tablo/grafik (matplotlib PNG olabilir).
B. **Bant kontrolü:** DEC-021/029/069..075 bantlarına göre bayraklar; **bant içi** düzeltmeler (varsa) yalnız S-04 sahipliğindeki ekonomi/unlock verilerinde ve testli; silah/tier/zırh verileri değiştirilmez → S-02/S-03'e öneri satırı.
C. **Öneri belgesi** `planning/work-packages/WP-041-BALANCE-PROPOSAL.md`: bant dışı bulgular + seçenekler (kullanıcı kararı formatı: "öneriyi onaylıyorum" yeterli olacak şekilde), profil seviyesi eğrisi (B/C açılış seviyeleri) ve dal katalog girdileri önerisi.
D. **Regresyon:** simülatör EditMode testi (determinizm + bant assert'leri), değişen modül testleri.

## Teslim
Tek commit, ≤15 satır not (en önemli 5 bulgu + öneri linki), rebase yok; planning/04 yalnız kendi bölümün + ADR-041-1.

---
Not (S-04): kullanıcı kararı DEC-083 (A açık, B/C seviye, D devre dışı) simülatörde roster'dan okunur. Fable game-designer turu: model kısıtı nedeniyle Opus stand-in ile tek tur (yorum önerinin §Yorum bölümünde).
