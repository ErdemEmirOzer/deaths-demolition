# 03 — Yol Haritası

## Güncel durum

On ana yapısal karar alanının tamamı kararlaştırılmıştır. Implementasyon çalışma modeli, üretim aşamaları ve ilk çalışma paketi kullanıcı tarafından onaylanmıştır. Kesin süreler ve teslim tarihleri henüz belirlenmemiştir. Mevcut Unity prototipi doğrulanmış ve production baseline'a dönüştürülmüştür; aşamalı olarak nihai oyuna dönüştürülecektir. `M0 — Production baseline` aşaması 2026-09-10'da tamamlanmıştır (`WP-000`; baseline commit `4b01b630b9cadde94b646b7242192ab3d60a8e1e`, etiket `wp-000-baseline`). M1 hazırlığı `WP-001 — Lifecycle & Repository Hardening` ile başlamış ve 2026-09-10'da tamamlanmıştır (package commit `e3d3a98e66230c2fd48abc9269a8e82d6bf30f73`, kullanıcı kararıyla `main`'e fast-forward, etiket `wp-001-lifecycle`; S-10 PASS 13 · FAIL 0 · BLOCKED 0). M1 dikey kesitinin ilk üretim dilimi `WP-002 — M1 Core Gameplay & Combat Production Slice` 2026-09-11'de başlamış ve 2026-09-12'de tamamlanarak `main`'e entegre edilmiştir (package commit `8fd858670e4f5a77e6f12811436e2c5a846a510b`, kullanıcı onayıyla fast-forward, etiket `wp-002-m1-combat`; S-10 PASS 12 · FAIL 0 · BLOCKED 0; EditMode 192/192, PlayMode 48/48). İkinci dilim `WP-003 — M1 Rounds & Dynamic Spawning Slice` 2026-09-12'de başlamıştır (izole branch `wp-003-m1-rounds-spawning`, taban `8fd85867`).

## Onaylanan planlama sırası

Oyunun büyük konuları tek tek ve ayrıntılı biçimde konuşulacaktır.

| Sıra | Konu | Durum |
|---:|---|---|
| 1 | Oyuncu ve tek oyunculu yapı | Kararlaştırıldı |
| 2 | Dünya ve görsel kimlik | Kararlaştırıldı |
| 3 | Harita yapısı | Kararlaştırıldı |
| 4 | Karakter sistemi | Şimdilik kararlaştırıldı; tek karakter ve üç skin |
| 5 | Round yapısının ayrıntıları | Kararlaştırıldı; sayısal dengeleme değerleri sonraya bırakıldı |
| 6 | Hayatta kalma sistemi | Kararlaştırıldı; sayısal dengeleme değerleri sonraya bırakıldı |
| 7 | Meta akış | Kararlaştırıldı |
| 8 | Gold ekonomisi | Yapısal olarak kararlaştırıldı; sayısal dengeleme sonraya bırakıldı |
| 9 | İlk üretim kapsamı | Kararlaştırıldı; ilk paket Production Baseline Audit & Stabilization (`WP-000`, tamamlandı 2026-09-10) |
| 10 | Ses ve atmosfer ayrıntıları | Kararlaştırıldı; sayısal miks ve üretim tuning değerleri uygulama sırasında belirlenecek |

## Onaylanan implementasyon aşamaları

| Aşama | Amaç | Çıkış koşulu |
|---|---|---|
| M0 — Production baseline — **Tamamlandı (2026-09-10)** | Mevcut prototipi doğrulamak ve güvenli üretim tabanına dönüştürmek | Güncel build/test kanıtı, modül ve placeholder envanteri, güvenli başlangıç commit'i — karşılandı (`WP-000`; commit `4b01b630…`, etiket `wp-000-baseline`) |
| M1 — Oynanabilir dikey kesit | Ana koşu döngüsünü küçük fakat baştan sona üretim standardında doğrulamak | Hareket, kamera, bir klasik silah, Standard zombi, round, gold, geliştirme, ölüm ve sonuç akışı birlikte çalışır |
| M2 — Sistemleri tamamlama | Onaylanan oynanış ve meta sistemlerini tamamlamak | Zırh, patlama, düşman versiyonları, dinamik spawn, Special Weapon, profil ve kayıt entegredir |
| M3 — Üretim kalitesi | Nihai environment, asset, animasyon, ışık, VFX, ses ve UI üretimlerini entegre etmek | Onaylı stil ve okunabilirlik hedefleri oynanabilir yapıda karşılanır |
| M4 — Entegrasyon ve iyileştirme | Performans, kayıt güvenliği, input, game feel ve tam koşu deneyimini doğrulamak | Kabul testleri, performans kapıları ve regresyon paketi başarılıdır |

İlk çalışma paketi `WP-000 — Production Baseline Audit & Stabilization` olarak `04-IMPLEMENTATION-PLAN.md` içinde tanımlanmış ve 2026-09-10'da tamamlanmıştır. Kanıt: baseline commit `4b01b630b9cadde94b646b7242192ab3d60a8e1e`, etiket `wp-000-baseline`; S-10 nihai kapı kararı PASS 11 · FAIL 0 · BLOCKED 0; EditMode 175/175, PlayMode 21/21 (gerçek Metal GPU), Player build PASS, Player smoke PASS; Unity `2022.3.22f1` ve URP `14.0.10` değişmedi. WP-000 manuel test kitinden M4 regresyon paketine devredilen kalemler: gamepad doğrulaması, manuel round bitişi (B11), pencere modları (C2–C4).

M1 hazırlığı `WP-001 — Lifecycle & Repository Hardening` ile başlamış ve 2026-09-10'da tamamlanmıştır: package commit `e3d3a98e66230c2fd48abc9269a8e82d6bf30f73`, `main`'e fast-forward (etiket `wp-001-lifecycle`), S-10 kabul PASS 13 · FAIL 0 · BLOCKED 0; EditMode 175/175, PlayMode 28/28 (gerçek Metal GPU), release ve development Player build PASS, lifecycle probe PASS. M1'in ilk üretim dilimi `WP-002 — M1 Core Gameplay & Combat Production Slice` (hareket, nişan, kamera, klasik silah ateş döngüsü, Standard zombi savaşı, sağlık/regen/ölüm, collision/ayrışma, hit reaction sözleşmesi) 2026-09-11'de başlamış, 2026-09-12'de tamamlanmış ve `main`'e entegre edilmiştir (commit `8fd85867`, etiket `wp-002-m1-combat`; EditMode 192/192, PlayMode 48/48 gerçek Metal, release build ve Player smoke PASS, manuel klavye-fare smoke M1–M14 PASS). İkinci dilim `WP-003 — M1 Rounds & Dynamic Spawning Slice` (round state ve nüfus defteri, tasarımcı spawn noktaları, oyuncu konumuna bağlı dinamik ağırlık, hedef aktif ≠ teknik tavan, havuz tükenmesi ve son yaşayan düşmanla tek seferlik tamamlanma, pause/ölüm/yeni koşu güvenliği) 2026-09-12'de başlamıştır; paket `04-IMPLEMENTATION-PLAN.md` içinde tanımlıdır. Gold/upgrade ve meta UI dilimleri sonraki paketlerdedir.

## Ertelenen planlama alanları

- Hedef işletim sistemi, mağaza ve dağıtım ayrıntıları ürünün büyük bir bölümü üretildikten sonra ele alınacaktır.
- Online seçenek ancak oyunun son aşamalarında ayrı mod ve ayrı plan olarak değerlendirilebilir.
- Farklı konseptlerde ek haritalar mevcut oyun tamamlandıktan sonra değerlendirilecektir.
- Oynanabilir karakter çeşitliliği mevcut oyun tamamlandıktan sonra değerlendirilecektir.

## Kayıt kuralı

- Kritik kapsam, teknoloji, maliyet ve ürün kararları yalnızca kullanıcı tarafından verildiğinde veya açıkça onaylandığında eklenir.
- Onaylı aşamalar içindeki rutin teknik görevler çalışma paketlerinde ayrıntılandırılabilir ve raporlanır.
- Onaylanmamış kritik üretim sırası veya tarih bu dosyaya yazılmaz.
