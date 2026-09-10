# 03 — Yol Haritası

## Güncel durum

On ana yapısal karar alanının tamamı kararlaştırılmıştır. Implementasyon çalışma modeli, üretim aşamaları ve ilk çalışma paketi kullanıcı tarafından onaylanmıştır. Kesin süreler ve teslim tarihleri henüz belirlenmemiştir. Mevcut Unity prototipi doğrulanıp aşamalı olarak nihai oyuna dönüştürülecektir.

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
| 9 | İlk üretim kapsamı | Kararlaştırıldı; ilk paket Production Baseline Audit & Stabilization |
| 10 | Ses ve atmosfer ayrıntıları | Kararlaştırıldı; sayısal miks ve üretim tuning değerleri uygulama sırasında belirlenecek |

## Onaylanan implementasyon aşamaları

| Aşama | Amaç | Çıkış koşulu |
|---|---|---|
| M0 — Production baseline | Mevcut prototipi doğrulamak ve güvenli üretim tabanına dönüştürmek | Güncel build/test kanıtı, modül ve placeholder envanteri, güvenli başlangıç commit'i |
| M1 — Oynanabilir dikey kesit | Ana koşu döngüsünü küçük fakat baştan sona üretim standardında doğrulamak | Hareket, kamera, bir klasik silah, Standard zombi, round, gold, geliştirme, ölüm ve sonuç akışı birlikte çalışır |
| M2 — Sistemleri tamamlama | Onaylanan oynanış ve meta sistemlerini tamamlamak | Zırh, patlama, düşman versiyonları, dinamik spawn, Special Weapon, profil ve kayıt entegredir |
| M3 — Üretim kalitesi | Nihai environment, asset, animasyon, ışık, VFX, ses ve UI üretimlerini entegre etmek | Onaylı stil ve okunabilirlik hedefleri oynanabilir yapıda karşılanır |
| M4 — Entegrasyon ve iyileştirme | Performans, kayıt güvenliği, input, game feel ve tam koşu deneyimini doğrulamak | Kabul testleri, performans kapıları ve regresyon paketi başarılıdır |

İlk çalışma paketi `WP-000 — Production Baseline Audit & Stabilization` olarak `04-IMPLEMENTATION-PLAN.md` içinde tanımlanmıştır.

## Ertelenen planlama alanları

- Hedef işletim sistemi, mağaza ve dağıtım ayrıntıları ürünün büyük bir bölümü üretildikten sonra ele alınacaktır.
- Online seçenek ancak oyunun son aşamalarında ayrı mod ve ayrı plan olarak değerlendirilebilir.
- Farklı konseptlerde ek haritalar mevcut oyun tamamlandıktan sonra değerlendirilecektir.
- Oynanabilir karakter çeşitliliği mevcut oyun tamamlandıktan sonra değerlendirilecektir.

## Kayıt kuralı

- Kritik kapsam, teknoloji, maliyet ve ürün kararları yalnızca kullanıcı tarafından verildiğinde veya açıkça onaylandığında eklenir.
- Onaylı aşamalar içindeki rutin teknik görevler çalışma paketlerinde ayrıntılandırılabilir ve raporlanır.
- Onaylanmamış kritik üretim sırası veya tarih bu dosyaya yazılmaz.
