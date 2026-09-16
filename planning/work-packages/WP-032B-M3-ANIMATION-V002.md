# WP-032B — ANIMATION V002: AP-009B SETİ, PICKUP ÜST GÖVDE (DEC-082), RISE MELEZ EL SIKIŞMASI, ULTRA OVERHEAD, KUYRUK IDLE

> Yürütme promptu (C-00, 2026-09-17, kullanıcı kararlarının S-06'ya dağıtımı; WP-032 teslim notunda WP-032B olarak ayrıldı) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-032B.

Sahip: S-06 (tek writer). Taban: WP-032 commit (`wp-032-m3-animation-polish`, main 7924e69 üstünde). Branch: `wp-032b-animation-v002`.

**C-00 → S-06 · Kullanıcı kararları (2026-09-17), WP-032 kapsamına al (kapsam dışıysa teslim notunda "WP-032B" olarak devret):**
1. **Special Weapon alma sırasında hareket kilidi = YOK** — pickup animasyonu üst gövde katmanında (avatar mask), alt gövde locomotion kesintisiz; DEC kaydı S-02/S-06 ortak (planning/00 DEC-082: "pickup hareket kilidi yok, üst gövde katmanı").
2. **Spawn'da yerden kalkma = melez** — rise klibi yalnız kamera dışı spawn'larda; kamera içi spawn doğrudan idle/locomotion pozunda (S-03 WP-040 `RiseOnSpawn` melez + `spawn_rise/spawn_land` olayları; animasyon kapısı sende: rise → locomotion geçişi, olaylar klipten).
3. AP-009B v002 seti (36 klip: koşu ×4, Ultra overhead, kuyruk idle, C11 anticipation, ayak olayları) `AP-009/exports/v002` — WP-032'de controller'ları v002 ile yeniden üret; Ultra overhead klibi S-03'ün `AttackClipPlaceholder` geçişini açar (S-03'e teslimde bildir).
Ayrıca: main `7ff05a4d` (WP-036 tier siluet parçaları + Tier Lit shader girdi — skinned tier parçaları rig_template_v001 üzerinde; retarget etkilenmez), `tools/slot.sh` v2 FIFO (bekleyen acquire'ı yeniden başlat; S-05 WP-020 önde).

**C-00 → S-06 · AP-009B v002 bulgusu (P-06):** v001 manifestinde tüm locomotion kliplerinde `foot_l/foot_r` olay adları TERS yazılmıştı (foot_r=0 aslında sol topuk); v002'de ölçülerek düzeltildi. WP-032'de controller/olayları **v002 manifestinden** yeniden üret (v001 olay adlarını taşıma); ayak dekal/SFX yönü S-07/S-08'de aynalıydı → düzeltmeyi teslim notunda belirt. Ayrıca v002: `player_run_*` 14 f uçuş fazlı (6,2 m/s'de sürücü oranı ×1,35–1,40; oran tavanı ×1,6 önerisi), `zombie_attack_overhead` telegraph 1,167 s (kod 1,15; toparlanma 24 kare → sürücü son kareyi 0,3 s tutar), `zombie_idle_queue` 90 f örnek başına rastgele faz gerekir. S2/S3 düzeltmeleri AP-009C v003'te gelecek.

Test/Build: EditMode/PlayMode değişen modüller; Player kodu değişti → release build + e1/e4 (slot); restore 6/6. Teslim: tek commit + Co-Authored-By; C-00'a ≤15 satır; ff-merge + etiket.
