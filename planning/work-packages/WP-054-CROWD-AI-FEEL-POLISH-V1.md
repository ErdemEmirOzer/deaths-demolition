# WP-054 — Crowd/AI feel polish v1

Paket promptu (C-00, 2026-09-17, HIZ MODU / HIZLI HAT §6; S-03 sahip; kredi 0; game-designer Fable tek tur). Kısaltılmış kayıt.

- **Taban:** main `1832481` (WP-052 entegre). Branch `wp-054-crowd-feel`, worktree `wp-054/`. Tek commit, not ≤ 10 satır.
- **Kapsam:** sürü yönlendirme çeşitliliği (kanat açma/yan yaklaşma oranı, duvar/çit sürtünmesi, kapı eşiği itişi SeparationMass), kuyruk/bekleme davranış çeşitliliği (idle_queue faz + Wait), ölü gövde havuzu zamanlamaları ve kalkış görünürlük seçimleri (Threat Director ile kamera dışı spawn tercihi), köprü/kıyı akışı (WP-020 v3 NavMesh alan maliyetleri ince ayarı), 40 zombi perf tek koşu (kirli damga), QaRunBot kanıt kareleri; AP-015P plakaları WP-038 ile gelince NavMesh yeniden doğrulama aynı pakette (küçük ek).
- **Game-designer turu (Fable, 2026-09-17):** kanat payı Standard %35 / Special %60 / Elite %10 / Ultra %0, açı ±35–70°, düz dönüş 3,0 m; duvar sürtünme 0,80 (giriş 0,10 / çıkış 0,35 s); kapı kütlesi ağır ×1,5 / hafif ×0,6; takılma eşiği 0,45–0,85 s, kıpırdanma 0,08 m/s 1,4–2,2 s ≤ 0,12 m yalnız hafif tier; ceset 0,55 / 0,8 / 1,5 / 3,0 s ± %15, tavan−2'de ×0,4, Ultra tabanı 1,2; kamera içi spawn ağırlığı katman 0–1 0,35 / 2 0,20 / 3 0,10; kalkış klibi değişmez.
