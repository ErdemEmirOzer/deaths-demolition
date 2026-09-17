# WP-042 — WEAPON ROSTER FINALIZATION & WEAPON FEEL v1 (prompt kaydı, C-00 dağıtımı 2026-09-17, HIZLI HAT)

Sahip S-02 (Opus 5 teknik; silah hissi ana hatları Fable 5.1 game-designer tek tur). Kredi 0. Taban main `7924e69` + WP-037B eki (AP-014 modelleri; aynı dosyalar). Slot: `tools/slot.sh acquire unity wp-042`; swap > 12 GB iken kod/veri Unity açılmadan yazılır, testler slot FIFO + swap < 12 GB olunca koşar.

Kullanıcı kararları (C-00 aktarımı, 2026-09-17): **3 silah A/B/C, D yok (veri kalır, etkin değil), dal ilkesi (a), açılış (a) → DEC-083; pickup hareket kilidi YOK → DEC-082 (S-06 animasyon tarafı).**

Kapsam:
- A. Kararların kaydı: DEC-083 + DEC-082 planning/00 + REQ satırları (01); WP-034 verisinde D `enabled=false` kalır, seçim/dükkân D'yi hiç göstermez (S-09 UI'da kilitli kart değil, yok); testler.
- B. Pickup kilidi yok: WP-007 Special Weapon lifecycle'da pickup sırasında hareket/nişan kilidi kaldırılır; pickup üst gövde katmanı olayları (`pickup_grab`/`pickup_done`, AP-009C klibi gelince) `SpecialWeaponUsageIds` ile; kesinti kuralları (pickup sırasında vurulma/ölüm) test.
- C. Silah hissi v1 (A/B/C): DEC bantları içinde geri tepme/yayılma/ateş temposu/şarjör/reload süreleri, pompalı pellet dağılımı; WP-034 pellet sözleşmesi; namlu/impact/kovan/şarjör düşme olayları → AP-010B v003 slotları (`AP010_fb_muzzle`, `AP010B_mag_drop`, `spark`) ve WP-021B/021C SFX kimlikleri (`EVENT-COVERAGE-MATRIX.md` silah satırları dolu); gamepad titreşim profilleri silah başına (WP-024 rumble, 0 ayırma).
- D. WeaponSocket sözleşmesi (ADR-042-1): grip/foregrip/muzzle/eject/mag transform'ları + LOD/collider beklentisi — AP-013 blockout (P-06 AP-009C içinde) ve nihai modeller (AP-013, kredi sonrası) bu sözleşmeyle bağlanır; yer tutucu prop'lar sahnede.
- E. Ölçüm: TTK tier×zırh tablosu (S-04 WP-041 simülatörüyle uyumlu veri, kopya yok), hot-path 0 ayırma sözleşmesi korunur (ADR-037-1 testleri).

Teslim: tek commit, ≤15 satır not, rebase yok; planning/04 yalnız kendi bölümün. Silah/Special Weapon tasarımı proje sonunda kullanıcıyla ortak revize edilecek — şimdi tam uygulanır.
