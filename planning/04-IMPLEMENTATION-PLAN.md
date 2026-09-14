# 04 — Implementasyon Planı

## Amaç

Mevcut `prototype/DeathsDemolition_PlayableConcept` Unity projesini, onaylanmış ürün kararlarını koruyarak ve doğrulanabilir çalışma paketleriyle nihai oyuna dönüştürmek.

Bu dosya hazır kontrolü değildir. Projede `04-READY-CHECK.md` oluşturulmaz.

## Değişmez çalışma modeli

- Kullanıcı kritik ürün, kapsam, teknoloji, maliyet ve geri dönüşsüzlük kararlarının sahibidir.
- Ana Codex oturumu Pusula koordinasyonu, ilerleme kontrolü ve sonraki promptların hazırlanmasından sorumludur.
- Her uzmanlık alanının Codex ve Claude'da ayrı kalıcı konu oturumu bulunur; bunlar tasarım ile prompt/rapor giriş-çıkış pencereleridir.
- Her çalışma paketi tek hedefe, açık kabul kriterlerine ve testlere sahip olur.
- Kadro sisteminden gereken sayıda ve türde uzman göreve dahil edilir; önceden ajan sayısı sınırı konmaz.
- Bağımsız işler paralel, aynı dosya veya sisteme dokunan işler tek entegrasyon sorumlusu altında sıralı yürütülür.
- Ara rapor teslim sayılmaz. Ajanlar kabul kriterleri sağlanana kadar düzeltme ve yeniden test dahil çalışmayı sürdürür.
- Yalnızca kritik olaylar kullanıcıya yükseltilir.

## Konu oturumları

| ID | Codex oturumu | Thread ID | Sorumluluk |
|---|---|---|---|
| S-01 | Technical Foundation & Integration | `01a08b7c-b2b3-7141-ae12-d6e223262d48` | Unity temeli, sistem sınırları, veri akışı, input, kayıt sınırları, asset aktarımı ve entegrasyon |
| S-02 | Core Gameplay & Combat | `01a08b7c-eaab-7773-bbff-8566a8787a54` | Hareket, nişan, kamera, silahlar, sağlık ve hasar |
| S-03 | Enemies, AI, Rounds & Spawning | `01a08b7c-cfbc-77f0-be2f-e03b3fbcd5d4` | Zombi davranışları, zırh, versiyonlar, round ve dinamik spawn |
| S-04 | Economy, Progression & Save | `01a08b7c-c295-7c42-93d4-5bb396650345` | Gold, geliştirmeler, profil XP, istatistikler, açılan içerikler ve yerel kayıt |
| S-05 | World, Environment & Lighting | `01a08b7c-dc3a-72a0-9a1d-19da159a3873` | Harita, environment, asset, materyal, atmosfer ve ışık |
| S-06 | Animation | `01a08b7c-f98f-72e2-90c4-39dc6bf61208` | Oyuncu/zombi hareketi, saldırı, hit reaction, ölüm ve zırh kırılması |
| S-07 | VFX & Game Feel | `01a08b7d-04ad-7dd3-bc1b-87daf27e5454` | Silah, darbe, kan, zırh, patlama, kamera tepkisi ve efekt bütçesi |
| S-08 | Audio | `01a08b7d-1267-7301-a310-cbc645937082` | Silah, zombi, zırh, patlama, ortam, müzik ve UI sesleri |
| S-09 | UI, UX & Accessibility | `01a08b7d-27a5-76f1-bddb-06e09033723d` | Menü, seçim, HUD, geliştirme, sonuç, profil ve ayarlar |
| S-10 | QA, Performance & Final Integration | `01a08b7d-39a6-7672-a55c-5494070e8771` | Test, profiling, pooling, kayıt güvenliği, kontrol cihazları ve regresyon |

Claude tarafındaki eş oturumlar kullanıcı tarafından oluşturulacak ve ilgili Codex satırıyla aynı `S-XX` kimliğini kullanacaktır.

## Çalışma paketi akışı

1. Ana oturum onaylı kararları ve bağımlılıkları seçer.
2. İlgili Codex konu oturumu tasarım bağlamını korur.
3. İlgili Claude oturumu çalışma paketini Kadro için yürütülebilir prompta dönüştürür.
4. Prompt gerekli Kadro uzmanlarını ve tek entegrasyon sorumlusunu göreve çağırır.
5. Ajanlar uygulama, hata düzeltme ve test döngüsünü tamamlar.
6. Standart rapor ana oturuma iletilir.
7. Pusula karar uyumu, kapsam, test ve entegrasyon kanıtını kontrol eder.
8. Kritik olmayan eksikler için düzeltme promptu; kritik konular için kullanıcı kararı; başarılı paket için sonraki prompt hazırlanır.

## Her promptta bulunacak ajan kuralı

> Bu çalışma için Kadro sistemindeki mevcut ajan ve uzman havuzunu değerlendir. En yüksek profesyonel kaliteyi elde etmek için gereken bütün uzmanları göreve dahil et; ajan sayısını önceden sınırlama. Bağımsız görevleri paralel yürüt, aynı dosya veya sistemlere dokunan görevleri bir entegrasyon sorumlusu altında koordine et. Ara raporu nihai teslim sayma. Kabul kriterleri sağlanana, hatalar düzeltilene ve testler başarıyla tamamlanana kadar çalışmayı kesintisiz sürdür. Yalnızca onaylanmış proje kararını değiştirecek, geri dönüşü zor, maliyet doğuran veya sonucu esaslı biçimde etkileyen kritik bir durum varsa kullanıcı kararına yükselt.

## Kritik olaylar

- Onaylanmış oyun, ürün, kapsam veya görsel kimlik kararını değiştirmek.
- Ana teknoloji, Unity sürümü veya render pipeline değiştirmek.
- Ücretli araç, lisans, dış servis, hesap veya yayınlama kararı.
- Veri kaybı riski taşıyan ya da geri dönüşü zor işlem.
- Kalite veya performans hedefinden önemli taviz.
- Birbiriyle çelişen onaylı kararlar.
- Görevin sonucunu esaslı biçimde değiştiren ve güvenli biçimde aşılamayan gerçek engel.

Onaylı sınırlar içindeki kod organizasyonu, güvenli refactor, hata düzeltme, test ayrıntısı, performans iyileştirmesi ve tasarım yönü içindeki üretim iterasyonları kritik değildir; raporlanarak uygulanır.

## Standart çalışma paketi

- Paket kimliği ve konu oturumu
- Amaç ve kullanıcıya görünen sonuç
- Onaylı kararlar ve kaynak gereksinimler
- Kapsam ve kapsam dışı maddeler
- Bağımlılıklar ve korunacak arayüzler
- Gerekli uzmanlıklar ve entegrasyon sahibi
- Teslimatlar
- Kabul kriterleri
- Otomatik ve manuel testler
- Kritik durumda durma koşulu
- Standart rapor formatı

## Standart rapor

- Yapılan işler
- Değiştirilen dosyalar ve üretilen assetler
- Çalıştırılan testler ve sonuçları
- Bilinen sorunlar ve kalan riskler
- Entegrasyon etkileri
- Kritik kullanıcı kararı gereken noktalar
- Sonraki önerilen çalışma

## Git ve çoklu ajan güvenliği

- Her çalışma paketi benzersiz kimlik taşır.
- Her paket için branch/worktree sahibi ve tek commit sorumlusu belirlenir.
- Çalışma başlamadan ve tamamlandıktan sonra dirty-state kaydedilir.
- Paralel ajanlara çakışmayan dosya sahipliği atanır.
- Aynı dosyada paralel yazma gerekiyorsa işler sıralanır; sonuçları entegrasyon sorumlusu birleştirir.
- Commitler küçük, atomik, paket kimliğiyle izlenebilir ve geri döndürülebilir olur.
- Paylaşılan branch üzerinde force push, rebase, reset veya geçmiş değiştirme yapılmaz.
- Birleştirme/cherry-pick sırası entegrasyon kaydında tutulur.
- Derleme ve ilgili testler geçmeden commit entegrasyona alınmaz.
- MCP credential, token, lisans anahtarı ve diğer sırlar repoya yazılmaz.
- Kullanıcı çalışmaları silinmez veya resetlenmez.

## Üretim aşamaları

| Aşama | Sonuç |
|---|---|
| M0 | Doğrulanmış ve geri döndürülebilir production baseline — tamamlandı 2026-09-10 (`WP-000`; commit `4b01b630…`, etiket `wp-000-baseline`) |
| M1 | Küçük fakat baştan sona production-standard dikey kesit |
| M2 | Onaylı oynanış, düşman, ekonomi, profil ve kayıt sistemlerinin tamamlanması |
| M3 | Nihai environment, asset, animasyon, ışık, VFX, ses ve UI entegrasyonu |
| M4 | Performans, kalite, kayıt güvenliği ve regresyon doğrulaması |

## WP-000 — Production Baseline Audit & Stabilization

**Durum:** Tamamlandı (2026-09-10). Kanıt: baseline commit `4b01b630b9cadde94b646b7242192ab3d60a8e1e`, etiket `wp-000-baseline`, commit sonrası dirty-state boş; S-10 nihai kapı kararı PASS 11 · FAIL 0 · BLOCKED 0; EditMode 175/175, PlayMode 21/21 (gerçek Metal GPU), Player build PASS, Player smoke PASS; Unity `2022.3.22f1` ve URP `14.0.10` değişmedi. Nihai rapor: `S-01-WP-000-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-000/reports/`).

**Yürütme promptu:** `planning/work-packages/WP-000-PRODUCTION-BASELINE.md`

**Amaç:** Mevcut prototipin çalışan kısımlarını kanıtlamak, geçici kısımlarını sınıflandırmak ve güvenli başlangıç commit'ine hazır bir üretim tabanı oluşturmak.

**Kapsam:**

- Unity `2022.3.22f1` ve URP `14.0.10` ile temiz açılış/build doğrulaması.
- EditMode ve PlayMode testlerinin güncel kaynak üzerinde çalıştırılması.
- Önceki rendering/shader bulgularının yeniden doğrulanması.
- Modül, sahne, paket, bağımlılık ve runtime servis envanteri.
- `[PrototypeOnly]` değerlerinin `onaylı`, `rutin teknik tuning`, `ürün/balance kararı gerekiyor` olarak sınıflandırılması.
- Mevcut kodun gereksinimlerle izlenebilirlik eşlemesi.
- Toplu yeniden yazım yapmadan ilk refactor ve migration sırasının hazırlanması.
- Prototip belgelerinin yeni production-base kararına uyarlanması.
- Git başlangıç kapsamı, ignore kuralları, büyük dosya ve secret taraması.

**Çıkış koşulları:**

- Kaynak, build ve test durumu kanıtlıdır.
- Korunacak, düzeltilecek ve değiştirilecek modüller listelenmiştir.
- Placeholder envanteri sınıflandırılmıştır.
- Kritik açık kararlar ayrılmıştır.
- İlk güvenli baseline commit için içerik ve entegrasyon sırası hazırdır.
- Herhangi bir kullanıcı dosyası silinmemiş veya resetlenmemiştir.

**Çıkış koşullarının karşılanması (2026-09-10):** altı koşulun tamamı nihai raporda kanıtlıdır (kaynak/build/test kanıtı §4; korunacak/düzeltilecek/değiştirilecek modül listesi §8 ve §13; placeholder sınıflandırması §7, 491 giriş; kritik açık kararlar §12; baseline commit ve entegrasyon sırası §10 ve §10-EK; kullanıcı dosyası silinmedi/resetlenmedi §1). Manuel test kitinden M4 regresyon paketine devredilen kalemler: gamepad doğrulaması, manuel round bitişi (B11), pencere modları (C2–C4). Karar verilmemiş ve nihai rapor §12'de ayrılan açık kalemler (karar bu belgede yazılmaz; kullanıcı kararı bekler): `InitTestScene` fiziksel silme, B4 DirectToRun kalıcılığı ve F1 debug paneli, B5 abort edilen koşunun profile işlenmesi, sınıf-3 karar paketleri A–H, DEC-045/048/049/050/043/021 kod boşlukları, asset sözleşmesi 14 karar adayı.

## WP-001 — Lifecycle & Repository Hardening

**Durum:** Tamamlandı (2026-09-10). Kanıt: package commit `e3d3a98e66230c2fd48abc9269a8e82d6bf30f73` (izole branch `wp-001-lifecycle-repository-hardening`, taban `wp-000-baseline` = `4b01b630…`, tek atomik commit, 24 dosya); kullanıcı kararıyla `main`'e fast-forward (merge commit yok), etiket `wp-001-lifecycle`; S-10 bağımsız kabul PASS 13 · FAIL 0 · BLOCKED 0; EditMode 175/175, PlayMode 28/28 (gerçek Metal GPU, 7 yeni lifecycle testi), release Player build ve smoke PASS, development build + lifecycle probe A/B PASS; kullanıcı verisi taban ile 4/4 eşleşti; push yok (remote yok). Nihai rapor: `S-01-WP-001-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-001/reports/`). WP-001 worktree'si korunmaktadır.

**Yürütme promptu:** `planning/work-packages/WP-001-LIFECYCLE-REPOSITORY-HARDENING.md`

**Amaç:** M1 dikey kesit çalışmalarına geçmeden önce repository entegrasyon güvenliğini ve onaylanmış profil/uygulama yaşam döngüsünü güçlendirmek. Yeni oynanış, içerik, denge, sanat veya ürün kararı verilmez.

**Kapsam:**

- WP-000 kapanış durumunun planlama belgelerine işlenmesi ve yürütme promptunun `planning/work-packages/` altına aynen kaydı.
- Unity Smart Merge sürücüsünün (UnityYAMLMerge) güvenli repo-yerel Git yapılandırmasıyla doğrulanması (global config değil).
- Normal tamamlanan koşu sonrasında profil kalıcılığının gerçek uygulama yeniden açılışı sınırında doğrulanması.
- Uygulama yeniden açıldığında koşu verisinin sıfırlandığının doğrulanması.
- Uygulama yeniden açıldığında DEC-039 gereği varsayılan skin ve klasik silah seçimine dönüldüğünün doğrulanması (oturum içi hatırlama DEC-037 bozulmadan).
- Ardışık koşu, pause ve statik servis/event temizliğinde regresyon olmadığının doğrulanması.
- Sonraki paketler için temiz ve izlenebilir bir M1 başlangıç noktası (izole branch üzerinde tek atomik commit; `main`'e merge ve push ayrı kullanıcı kararı).

**Kapsam dışı:** Unity/URP sürüm değişikliği; paket, dependency, assembly veya render pipeline değişikliği; yeni ürün, kapsam veya balance kararı (WP-000 nihai rapor §12 açık kararları bu pakette verilmez); M4'e devredilen gamepad, B11 ve C2–C4 kalemleri; `main`'e merge ve push.

**Çıkış koşulları:**

- `03-ROADMAP.md` ve `04-IMPLEMENTATION-PLAN.md` içinde M0 durumu ve WP-001 kaydı güncellenmiş, yürütme promptu aynen kaydedilmiştir.
- UnityYAMLMerge repo-yerel yapılandırması kanıtlıdır.
- Profil/uygulama yeniden açılış, koşu verisi sıfırlama ve DEC-039/DEC-037 seçim yaşam döngüsü kanıtla doğrulanmıştır.
- Event/static durum sertleştirmesi test koruması altındadır.
- Tam regresyon geçmiştir: EditMode, PlayMode (gerçek GPU), Player build, Player smoke, lifecycle kanıtı.
- Çalışma izole branch'te kalmıştır; `main`'e merge ve push yapılmamıştır; kullanıcı dosyası silinmemiş veya resetlenmemiştir.

## WP-002 — M1 Core Gameplay & Combat Production Slice

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-12). Kanıt: package commit `8fd858670e4f5a77e6f12811436e2c5a846a510b` (izole branch `wp-002-m1-core-combat`, taban `main` = `e3d3a98…` = `wp-001-lifecycle`, tek atomik commit, 24 dosya); S-01 entegrasyon PASS; S-10 bağımsız kabul PASS 12 · FAIL 0 · BLOCKED 0; final manifest v2 `b63ba343…` (669 yol); sıfırdan Library ile EditMode 192/192, PlayMode 48/48 (gerçek Metal), release Player build ve smoke PASS; manuel klavye-fare smoke M1–M14 kullanıcı tarafından PASS; kullanıcı onayı ile `main`'e fast-forward (`e3d3a98..8fd8586`, merge commit yok), hafif etiket `wp-002-m1-combat`; push yok (remote yok). Nihai rapor: `S-02-WP-002-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-002/reports/`). WP-002 worktree'si korunmaktadır.

**Yürütme promptu:** `planning/work-packages/WP-002-M1-CORE-COMBAT.md`

**Amaç:** M1 dikey kesitinin temel savaş çekirdeğini production standardında kurmak ve testle doğrulamak. Bu paket tüm M1'i tamamlamaz; round/spawn, gold/upgrade ve meta UI dilimleri sonraki paketlerde ayrı oturumlarda yürütülür. Yeni ürün, kapsam, denge, sanat veya teknoloji kararı verilmez; mevcut sayısal değerler PrototypeOnly kalır.

**Kapsam:**

- Oyuncu hareketi (kamera-relative WASD, hareket–nişan bağımsızlığı, dash/dodge yokluğu).
- Fare ve gamepad nişan sözleşmesi (fiziksel gamepad testi M4'te; yalnız simüle sözleşme).
- Kamera davranışı (sabit açı/yön, yumuşak takip, sınırlı aim look-ahead, sabit zoom, occlusion fade).
- Mevcut prototip klasik silahıyla temel ateş döngüsü (hold-to-fire, pause/frozen kapısı, tek hedefe tek hit, pooling).
- Standard zombi ile temel savaş etkileşimi (hedef takibi, saldırı telegrafı ve aktif hit penceresi, hit reaction, ölüm ve despawn).
- Oyuncunun hasar alması, ölmesi ve otomatik can yenilenmesi (i-frame yok, kontrol kesintisi yok, tek can).
- Oyuncu/zombi fiziksel engellemesi ve zombiler arası yumuşak ayrışma.
- Temel hit reaction ve game-feel olay altyapısı (genişletilebilir, testli sözleşme; nihai VFX/animasyon/ses asset'i yok).
- Planlama durum kayıtları (WP-001 tamamlandı; WP-002 kaydı; yürütme promptunun aynen kaydı).

**Kapsam dışı:** round formülü ve havuzu; dinamik spawn yoğunluğu; gold, magnet ve round sonu toplama; upgrade kartları ve reroll; profil XP ve açılma ekonomisi; Run Results yeniden tasarımı; B3/B4/B5 açık kalemleri; sınıf-3 karar paketleri A–H; sayısal can/hasar/zırh/regen/silah dengesi; nihai klasik silah roster'ı; Special Weapon; Special/Elite/Ultra zombiler; bölgesel zırh ve patlama; nihai modeller, animasyonlar, VFX ve ses; PostProcessData ve render pipeline; `_Prototype → _Game` migration; paket veya Unity/URP sürüm değişikliği; remote, push, `main` merge; WP-001 worktree temizliği; fiziksel gamepad kabul testi.

**Çıkış koşulları:**

- Onaylı oynanış sözleşmesinin hareket, nişan, kamera, klasik silah, Standard zombi hasarı, sağlık/regen/ölüm, collision/ayrışma ve hit reaction maddeleri kod ve testle kanıtlıdır; yalnız kanıtlanmış uyumsuzluklar düzeltilmiştir.
- Hiçbir PrototypeOnly sayı ürün kararı olarak kilitlenmemiş; yeni DEC/REQ eklenmemiştir.
- Tam regresyon geçmiştir: import, EditMode, PlayMode (gerçek GPU), release Player build, klavye-fare combat smoke; Player loglarında yeni exception veya shader fallback yoktur.
- Unity/URP, Packages ve ProjectSettings değişmemiştir; secret/generated/.meta/GUID denetimi temizdir; gerçek kullanıcı profili korunmuştur.
- S-01 entegrasyon incelemesi ve S-10 bağımsız kabul PASS'tir; çalışma izole branch'te tek atomik commit olarak kalmıştır; `main`'e merge ve push yapılmamıştır.

## WP-003 — M1 Rounds & Dynamic Spawning Slice

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-12). Kanıt: package commit `507c217658aafce637262500e6fe8d03daff41d6` (izole branch `wp-003-m1-rounds-spawning`, taban `main` = `8fd85867` = `wp-002-m1-combat`, tek atomik commit, 26 dosya); S-01 koşulsuz PASS; S-10 bağımsız kabul PASS 14 · FAIL 0 · BLOCKED 0; final manifest `0b9707a5…` (686 yol); sıfırdan Library ile EditMode 233/233, PlayMode 56/56 (gerçek Metal), release Player build, Player smoke ve development-build RoundProbe 8/8 PASS; C-00 koordinasyonuyla `main`'e fast-forward (`8fd8586..507c217`, merge commit yok), hafif etiket `wp-003-m1-rounds`; push yok (remote yok). Nihai rapor: `S-03-WP-003-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-003/reports/`). WP-003 worktree'si korunmaktadır.

**Yürütme promptu:** `planning/work-packages/WP-003-M1-ROUNDS-SPAWNING.md`

**Amaç:** Mevcut `EnemySpawnDirector` üzerinden round ve dinamik spawn sistemini M1 dikey kesitinin test edilebilir, genişletilebilir üretim dilimine dönüştürmek. Yeni ürün, kapsam, denge, sanat veya teknoloji kararı verilmez; sayısal değerler PrototypeOnly kalır; yalnız Standard zombi kullanılır.

**Kapsam:**

- Round state makinesi (NotStarted / Starting / Active / PoolDepleted / Completing / Completed / Aborted) ve tek seferlik tamamlanma olayı.
- Round nüfus defteri: havuz toplamı, spawn edilmemiş, spawn edilen, yaşayan (cesetler hariç, REQ-012), gövde (cesetler dahil, tavan bunu sınırlar), öldürülen, hedef aktif (tempo), teknik tavan (güvenlik, REQ-072) — iki ayrı sayı.
- Düşman yaşam döngüsü sözleşmesi (bilet + `IEnemyLifecycleSink`: kayıt → aktivasyon → yaşam → ölüm → despawn → koşu sonu temizliği), aktivasyon başarısızlığının geri alınması ve raporlanması.
- Tasarımcı spawn noktaları (yalnız kayıtlı noktalar; rastgele dünya koordinatı yok), oyuncu konumuna bağlı dinamik ağırlık, çok uzak noktanın sıfır ağırlığı, saf ve deterministik seçim (`SpawnPointSelector`).
- Pause (REQ-006), oyuncu ölümü/abort, yeni round ve yeni koşu temizliği; sahne yeniden yüklemede tek director.
- Hibrit round ölçekleme için tek çözümleme noktası (`RoundPlan`), placeholder eklemeden.
- Development-build `RoundProbe` (gerçek Player'da deterministik round/spawn kanıtı), EditMode/PlayMode sözleşme testleri, performans/REQ-072 tempo gözlemi.
- Planlama durum kayıtları (WP-002 tamamlandı + entegre; WP-003 kaydı; yürütme promptunun aynen kaydı).

**Kapsam dışı:** gold/pickup/round sonu toplama; upgrade ekranı ve ekonomisi; profil XP, istatistik, achievement, unlock; armor, bölgesel hasar, patlama; Special/Elite/Ultra düşman ve Special Weapon; yeni klasik silah; nihai silah/round/spawn dengesi; nihai harita, model, animasyon, VFX, ses, UI; online/netcode/leaderboard/cloud; remote, push, yayın; B3/B4/B5 ve sınıf-3 A–H kararları; WP-002 temizlik adayları; ceset siper davranışı; pompa salvosu/knockback; stagger yeniden tasarımı; telegraph sesi; hit-reaction sözleşmesi genişletmesi; çoklu Player profil yazma politikası.

**Çıkış koşulları:**

- Round 1 gerçek sahnede güvenilir başlar; havuz doğru yönetilir; spawnlar yalnız authoring noktalarından; yoğunluk oyuncu konumuna göre değişir ve çok uzak nokta sıfır alır; hedef aktif korunur; teknik tavan asla aşılmaz; havuz bitince spawn durur; son yaşayan düşman ölünce round yalnız bir kez tamamlanır; pause/ölüm/yeni koşu state'i bozmaz; WP-002 sözleşmeleri korunur.
- Tam regresyon: import, EditMode, PlayMode (gerçek Metal), temiz Library release build, Player smoke; yeni ve mevcut tüm testler geçer.
- S-01 koşulsuz kabul, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; kullanıcı onayı olmayan yeni ürün kararı yok; çalışma izole branch'te tek atomik commit; `main`'e merge ve push yapılmamıştır.

## WP-004 — M1 Gold & Round Upgrade Loop

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-12). Kanıt: package commit `a284ffe2b3e9780b02d80e6812d3dd925e6cf656` (izole branch `wp-004-m1-gold-upgrade-loop`, taban `main` = `507c2176` = `wp-003-m1-rounds`, tek atomik commit, 44 dosya); S-01 koşulsuz PASS; S-10 bağımsız kabul PASS 16 · FAIL 0 · BLOCKED 0; final manifest Z `8e975f61…` (715 yol); sıfırdan Library ile EditMode 297/297, PlayMode 65/65 (gerçek Metal), release Player build, Player smoke, RoundProbe 8/8 ve GoldProbe 12/12 PASS; C-00 koordinasyonuyla `main`'e fast-forward (`507c217..a284ffe`, merge commit yok), hafif etiket `wp-004-m1-gold`; push yok (remote yok). DEC-048 (round başına tek satın alma) ve DEC-050 (ücretli reroll) kullanıcı kararıyla uygulanmadı; sonraki paket girdisi. Nihai rapor: `S-04-WP-004-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-004/reports/`). WP-004 worktree'si korunmaktadır.

**Yürütme promptu:** `planning/work-packages/WP-004-M1-GOLD-UPGRADE-LOOP.md`

**Amaç:** Mevcut prototipteki gold ve round sonu geliştirme sistemini M1 dikey kesitinin uçtan uca çalışan, güvenilir üretim dilimine dönüştürmek: her geçerli düşman ölümünde tam bir fiziksel gold paketi, süreyle kaybolmayan paket, mıknatıs toplama ve tek kredi, round tamamlanınca kalan paketlerin ekran açılmadan önce otomatik toplanması, koşu cüzdanı, atomik geliştirme satın alma, koşu build state'i, roundlar arası koruma, ölümde ve yeni koşuda sıfırlama, profil XP izolasyonu. Yeni ürün, kapsam, denge, sanat veya teknoloji kararı verilmez; sayısal değerler PrototypeOnly kalır; yalnız Standard ve zırhsız zombi kullanılır.

**Kapsam:**

- Enemy death → reward sözleşmesi (`EnemyDeathInfo.LifeId`, `GoldRewardLedger`: koşu canlı / bayat yaşam / tekrar / değersiz reddi) ve genişletilebilir değer sözleşmesi (`GoldRewardValue`: temel + versiyon + zırh yuvaları; M1'de yalnız temel değer).
- Gold paket yaşam döngüsü (`GoldPacketLifecycle`: Spawned / Waiting / Magnetizing / Collected / AutoCollected / Recycled; süreyle despawn yok; tek toplama), fiziksel paket (`GoldOrb`), round başına havuz boyutlandırma ve ısıtma.
- Koşu cüzdanı (`GoldWallet`: koşu kimliği, kaynaklı ve kimlikli işlemler, tekrar/negatif/yetersiz bakiye reddi, snapshot olayı), round sonu otomatik toplama (`GoldService.CollectOutstanding`, `RunDirector.OnRoundCleared` içinden deterministik sırayla), `RoundEndEconomySnapshot`.
- Atomik satın alma (`RoundUpgradeShop.TryPurchase(index, sessionId)`: kontroller harcamadan önce; refund dalı kaldırıldı; oturum kimliği ile bayat ekran reddi), koşu build state'i (`RunBuildState`, rank otoritesi `WeaponModifierState`'te), `DescribeBuild` önbelleği (kare başına 0 B), olay güdümlü `UpgradeScreen` (polling yok).
- Koşu kimliği (`RunDirector.RunId`), koşu başlangıç/bitiş sırası (cüzdan/ledger/paket/build), ölüm ↔ RoundCleared yarışında mevcut öncelik (ölüm terminal) korunur.
- EditMode/PlayMode sözleşme testleri, performans gözlemi, development-build `GoldProbe` (gerçek Player'da manuel kit kanıtı), planlama durum kayıtları.

**Kapsam dışı:** profil XP hesaplaması ve level curve; kalıcı achievement/unlock; final Results/Profile/Statistics ekranları; final UI redesign/animasyon; armor, bölgesel hasar, patlama zırh sözleşmesi; Special/Elite/Ultra düşman; Special Weapon; yeni klasik silah; nihai geliştirme dalları, rarity, reroll/lock/ban, nihai ekonomi/upgrade/round dengesi; loot crate/chest; kalıcı para birimi; başlangıç gold bonusu; battle pass/daily reward; online ekonomi/cloud save/leaderboard/netcode; final harita/model/animasyon/VFX/ses; B3/B4/B5 ve sınıf-3 A–H kararları; REQ-068/REQ-071/DEC-021/REQ-123 değişiklikleri; remote/push/yayın.

**Çıkış koşulları:**

- Her geçerli düşman tam bir fiziksel gold paketi düşürür; duplicate ölüm duplicate gold üretmez; gold yalnız kill'den; paketler normal round sırasında kaybolmaz; magnet pickup güvenilir; manuel pickup bir kez öder; round sonunda bütün alınmamış gold (son kill dahil) otomatik toplanır ve upgrade ekranı bu bakiyeyle açılır; satın alma atomik; upgrade mevcut klasik silaha uygulanır; gold ve build roundlar arası korunur, ölüm ve yeni koşu sıfırlar; gold/upgrade profil XP'si üretmez; WP-002 ve WP-003 sözleşmeleri korunur.
- Tam regresyon: import, EditMode, PlayMode (gerçek Metal), temiz Library release build, Player smoke, GoldProbe; yeni ve mevcut tüm testler geçer.
- S-01 koşulsuz kabul, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; yeni ürün kararı yok; sayısal değerler PrototypeOnly; çalışma izole branch'te tek atomik commit; `main`'e merge ve push yapılmamıştır.


## WP-005 — M1 Meta UI, Results & Profile Presentation

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-12): package commit `c3e9864` (branch `wp-005-m1-meta-ui-results-profile`, taban `a284ffe2`), fast-forward, etiket `wp-005-m1-meta-ui`; S-01 koşulsuz kabul, S-10 PASS 13 · FAIL 0 · BLOCKED 0 (manifest Y `d2292be2…`, 744 yol); EditMode 360/360, PlayMode 76/76 (gerçek Metal), temiz Library release build, Player smoke, RoundProbe 8/8, GoldProbe 12/12 PASS; manuel kit M11–M16 kullanıcı kararıyla beyan sınıfı (deterministik eşleri mevcut). Yürütme kaydı: tek yazma/commit sahibi S-09; S-01 entegrasyon sınırlarını salt okunur doğrular; S-02/S-03/S-04 salt okunur danışman; S-10 kabul kapılarını bağımsız doğrular; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Başlangıç manifesti `8e975f61…` (715 yol) = WP-004 final manifesti Z; davranış değiştirilmeden baseline kapıları: import 0 hata, EditMode 297/297, PlayMode 65/65 (gerçek Metal).

**Yürütme promptu:** `planning/work-packages/WP-005-M1-META-UI-RESULTS-PROFILE.md`

**Amaç:** M1 dikey kesitinin oyuncuya görünen meta akışını (Main Menu → Skin & Weapon Selection → Run/HUD → Round Cleared → Upgrade → Next Round → Death → Run Results → Main Menu; Main Menu'den Profile/Statistics/Achievements) authoritative snapshot'lardan beslenen, açık navigation state'li, presenter/view-model katmanlı, İngilizce, klavye/fare ile eksiksiz, erişilebilirlik temelli tek ve güvenilir kullanıcı akışına dönüştürmek. Yeni ürün, kapsam, denge, sanat veya teknoloji kararı verilmez; DEC-048/DEC-050 uygulanmaz; B5 (abort → profil) değiştirilmez; final art/ikon/font/animasyon M3'tedir.

**Kapsam:**

- UI navigation sözleşmesi (`UiNavigation`/`UiNavigationModel`: MainMenu · Selection · GameplayHud · RunResults tabanları; Profile · Statistics · Achievements · RoundUpgrade katmanları; Paused overlay; çelişen kombinasyonlar reddedilir, geçişler idempotent, yeni taban eski katman/overlay'i düşürür).
- Sunum katmanı (`RunHudViewModel`: yalnız değişen alan biçimlenir, sessiz kare 0 B; `ResultsViewModel`: immutable `RunRewardReport`/`RunSummary`'den; `ProfileViewModel`/`StatisticsViewModel`/`AchievementsViewModel`: kalıcı `ProfileData`'dan, `IProfileLookup` ile).
- HUD snapshot bağlantıları: `RunEvents.Current` + `RunEvents.GoldChanged` (aynı kare gold), `PlayerHealth` snapshot'ı `PlayerRegistry` üzerinden (sahne araması yok), `WeaponEvents.Classic`, build satırı `RunBuildState.DescribeBuild()` (Results ile aynı üretici); `RunStarted`'da presenter sıfırlama; Dead fazında son canlı değer korunur; `RunDirector.Awake` statik HUD snapshot'larını temizler.
- Upgrade ekranı: `LastRoundEnd` bağlam doğrulaması (run id + round), `GateForPause` → `UiNavigation.IsPaused` (kare başına arama yok), ikon yuvası + monogram fallback (`UpgradeIcons`, `Resources/UI/UpgradeIcons/<Branch>` isteğe bağlı yükleyici; config'e Sprite eklenmedi), DEC-048/050'ye nötr metin, sabit CONTINUE etiketi, pause sonrası odak.
- Pause: yalnız canlı koşuda ve HUD tabanında açılır (`PauseScreen.CanOpen`), onay diyaloğu modal (arkadaki panel etkileşimsiz), kapanınca odak iadesi; Results üstünde imkânsız.
- Main Menu pane'leri view model'den; achievement empty state; Results: başlık = bitiş, tek XL sayı (Reached Round), görülmeyen tier satırı üretilmez, tek eylem MAIN MENU; Selection: seçili skin'e ilk odak.
- EditMode/PlayMode sözleşme testleri, İngilizce metin taraması, performans gözlemi, planlama durum kayıtları.

**Kapsam dışı:** yeni karakter/skin/silah, Special/Elite/Ultra düşman, armor, DEC-048/DEC-050, B3/B4/B5 kararları, abort profil davranışı, online/cloud/leaderboard/hesap/telemetri, çoklu Player kilidi, final gamepad kabulü, final UI art/ikon/font/ses/VFX, UI Toolkit migration, remote/push/yayın.

**Çıkış koşulları:** paket promptundaki "Tamamlanma tanımı" maddeleri; tam regresyon (import, EditMode, PlayMode gerçek Metal, temiz Library release build, Player smoke, RoundProbe, GoldProbe); S-01 koşulsuz kabul, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; yeni ürün kararı yok; çalışma izole branch'te tek atomik commit; `main`'e merge ve push yapılmamıştır.


## M1 çıkış koşullarının durumu (2026-09-12, WP-005 sonrası)

`03-ROADMAP.md` M1 çıkış koşulu: "Hareket, kamera, bir klasik silah, Standard zombi, round, gold, geliştirme, ölüm ve sonuç akışı birlikte çalışır."

| Koşul | Kanıt (paket · kapı) | Durum |
|---|---|---|
| Hareket, kamera, bir klasik silah, Standard zombi savaşı, sağlık/regen/ölüm | WP-002 (`8fd85867`, `wp-002-m1-combat`): EditMode 192/192, PlayMode 48/48, manuel smoke M1–M14 | Karşılandı |
| Round state, dinamik spawn, havuz tükenmesi ve tek seferlik tamamlanma | WP-003 (`507c2176`, `wp-003-m1-rounds`): EditMode 233/233, PlayMode 56/56, RoundProbe 8/8 | Karşılandı |
| Gold paketi, cüzdan, round sonu otomatik toplama, atomik geliştirme, build koruma/sıfırlama | WP-004 (`a284ffe2`, `wp-004-m1-gold`): EditMode 297/297, PlayMode 65/65, GoldProbe 12/12 | Karşılandı |
| Ölüm → Results → Main Menu; Main Menu → Selection → Run; Profile/Statistics/Achievements; pause | WP-005 (`c3e9864`, `wp-005-m1-meta-ui`): EditMode 360/360, PlayMode 76/76, MetaUiSliceTests 11, S-10 PASS 13/0/0 | Karşılandı |
| Akışın baştan sona birlikte çalışması (tek koşu: menü → koşu → round → upgrade → ölüm → sonuç → menü) | GoldProbe 01–20 (gerçek Player, tek süreç), LifecycleProbe runA/runB (süreç sınırı), FlowSmoke PlayMode | Karşılandı |

M4'e devredilen (M1 çıkışını engellemeyen) kalemler: gamepad doğrulaması, manuel round bitişi (B11), pencere modları (C2–C4), çoklu Player örneği son-yazan-kazanır, WP-005 M11–M16 fiziksel manuel koşu. Karar bekleyen (bu belgede karar yazılmaz): B3/B4/B5, sınıf-3 A–H, DEC-048/DEC-050 sapmaları, REQ-123 marjı. M2 ilk dilimi `WP-006` aşağıda tanımlıdır.


## WP-006 — M2 Regional Armor & Explosion Damage Slice

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-13). Kanıt: package commit `b974529f86d65b986ad054e5762331da55311be4` (izole branch `wp-006-m2-regional-armor`, taban `main` = `c3e9864` = `wp-005-m1-meta-ui`, tek atomik commit, 24 dosya); S-01 koşulsuz PASS; S-10 bağımsız kabul PASS 12 · FAIL 0 · BLOCKED 0 (manifest Z `31268abe…`, 755 yol); sıfırdan Library ile EditMode 388/388, PlayMode 85/85 (gerçek Metal), release Player build, Player smoke, RoundProbe 8/8, GoldProbe 12/12 ve ArmorProbe 9/9 PASS; manuel kit M1–M10 kullanıcı kararıyla beyan sınıfı; C-00 koordinasyonuyla `main`'e fast-forward (`c3e9864..b974529`, merge commit yok), hafif etiket `wp-006-m2-armor`; push yok (remote yok). Nihai rapor: `S-03-WP-006-NIHAI-RAPOR.md` (Kadro çalışma alanı `context/projects/deaths-demolition/wp-006/reports/`). WP-006 worktree'si korunmaktadır. Yürütme kaydı: izole worktree/branch `wp-006-m2-regional-armor`; tek yazma/commit sahibi S-03; S-01 entegrasyon sınırlarını salt okunur doğrular; S-02/S-04/S-06/S-07/S-08/S-09 salt okunur danışman/inceleyici; S-10 kabul kapılarını bağımsız doğrular; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Başlangıç manifesti `d2292be2…` (744 yol) = WP-005 final manifesti Y; davranış değiştirilmeden baseline kapıları: import 0 hata, EditMode 360/360, PlayMode 76/76 (gerçek Metal). Paket promptunda "≥297/65" olarak verilen taban gerçek WP-005 tabanıyla (360/76) düzeltilmiştir.

**Yürütme promptu:** `planning/work-packages/WP-006-M2-REGIONAL-ARMOR-EXPLOSION.md`

**Amaç:** Standard zombiye yeni düşman türü eklemeden modüler bölgesel zırh (Helmet / Torso / Leg; 0/1/2/3 parça kombinasyonları) ve patlama hasar dağıtımı sözleşmesini üretim standardında, deterministik, tek kanonik hasar yoluyla ve olay verisi ayrımıyla tamamlamak. Yeni ürün, kapsam, denge, sanat veya teknoloji kararı verilmez; yeni DEC/REQ eklenmez; AP-001'e dokunulmaz; sayısal değerler PrototypeOnly'dir.

**Kapsam:**

- (A) Bölgesel isabet modeli: `HitboxRegistry` önce, collider adı, yükseklik oranı sırasıyla deterministik bölge çözümü (`DamageTargetResolver.ResolveRegion`); fallback uygulanır + sayılır (`RegionFallbackCount`, `LastResolution`) + ilk kez uyarılır; kayıtsız collider testi.
- (B) Zırh durumu: `ArmorSet` slot başına dayanıklılık, idempotent kırılma, havuz dönüşünde `Armor.Clear()`; görsel durum sözleşmesi `IArmorVisual` (Absent/Present/Damaged/Broken) ve `ArmorAnchors` (kabuto · dō/kote · haidate/suneate); placeholder plaka görselleri PrototypeOnly.
- (C) Hasar yönlendirme: doğrudan isabet yalnız vurulan bölgenin parçasına, taşma cana geçmez; patlama tek bütçe (25/45/30 PrototypeOnly paylar) üç bölgeye, korunan pay zırha / açık pay cana / taşma atılır, tam zırhlı beden can kaybetmez; ölü düşmana geç hasar boş sonuç (`DamageResults.IsEmpty`); tek kanonik patlama yolu (`HitscanEngine.Explode`; ölü `ExplosionUtility` kaldırıldı); `DamageTransaction` (atış/patlama/dış vuruş başına kimlik) ve alıcı tarafında aynı transaction'da ikinci uygulama reddi; `HitFeedbackDirector.ResolveTier` patlamada CriticalHit üretmez (REQ-089 hizalaması — iç tutarlılık, ürün kararı değil).
- (C-olay) `DamageEventMeta` (yaşam/transaction/olay kimliği, tür, slot, zırh/can önce-sonra, bölge kırılımı) `EnemyDamaged`/`ArmorBroken`/`EnemyDied` yüklerine ek ctor'larla; `DamageEventClassifier` on sınıf; frozen imzalar değişmedi; tek feedback sahibi korunur.
- (D) Round/spawn: 0/1/2/3 kompozisyon deterministik (`FitArmorComposition` test/probe harness'ı), DEC-021 hibrit iskeleti boş, `EnemySpawnDirector` tek spawn yolu.
- (F) Vuruş yolunda kare başına arama/string/ayırma yok (kaynak taraması + GC ölçümü), abone sayıları ikinci koşuda sabit, pause güvenli.
- EditMode/PlayMode sözleşme testleri (≥28 matris maddesi `[#n]` eşlemesiyle), development-build `ArmorProbe` (gerçek Player'da manuel kit kanıtı), planlama durum kayıtları.

**Kapsam dışı:** Special/Elite/Ultra düşman implementasyonu; yeni düşman yetenekleri; final zombi modeli/kıyafet ve final Japon zırh assetleri; Higgsfield/Adobe/Blender üretimi; final animasyon/ses/VFX; dismemberment; yeni silah/Special Weapon; profil seviyesi veya kalıcı stat değişikliği; yeni UI ekranı / sonuç ekranı alanı; yeni armor kill kategorisi ve zırhlı gold eki (DEC-043 kancası açılmadı); final sayısal denge; B3/B4/B5; REQ-123 marjı; sınıf-3 A–H; remote/push.

**Çıkış koşulları:** paket promptundaki "Tamamlanma tanımı" maddeleri (28 madde matris; olay verisi ayrımı; tek feedback; bütçe korunumu; ölü düşman; havuz temizliği); tam regresyon (import, EditMode, PlayMode gerçek Metal, temiz Library release build, Player smoke, RoundProbe, GoldProbe, ArmorProbe); S-01 koşulsuz kabul, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; yeni DEC/REQ yok; sayısal değerler PrototypeOnly; çalışma izole branch'te tek atomik commit; `main`'e merge ve push yapılmamıştır.

## WP-007 — M2 Special Weapon Lifecycle & Integration Hardening

**Durum:** Tamamlandı ve `main`'e entegre (2026-09-13). Kanıt: package commit `18a4cbf258b895c870153202ad2843ba4ebeb949` (izole branch `wp-007-m2-special-weapon-lifecycle`, taban `main` = `b974529` = `wp-006-m2-armor`), final manifest W `9eb555b5…` (788 yol); S-01 PASS; S-10 bağımsız kabul PASS 14 · FAIL 0 · BLOCKED 0; sıfırdan Library ile EditMode 420/420, PlayMode 122/122 (gerçek Metal), release build, Player smoke, RoundProbe 8/8, GoldProbe 12/12, ArmorProbe 9/9, SpecialWeaponProbe 10/10; C-00 koordinasyonuyla `main`'e fast-forward (merge commit yok), etiket `wp-007-m2-special-weapon`; push yok. (Yürütme kaydı: S-02, 2026-09-13.) İzole worktree/branch `wp-007-m2-special-weapon-lifecycle` (taban: `main` = `b974529` = `wp-006-m2-armor`); tek yazma/commit sahibi S-02; S-03/S-04/S-06/S-07/S-08/S-09 salt okunur inceleyici; S-01 mimari/entegrasyon incelemesi; S-10 kabul kapılarını bağımsız doğrular; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Başlangıç manifesti `31268abe…` (755 yol) = WP-006 final manifesti Z; davranış değiştirilmeden baseline kapıları: import 0 hata, EditMode 388/388, PlayMode 85/85 (gerçek Metal), ArmorProbe 9/9. Alt görev promptları KADRO prompt uzmanlarınca (`prompt-engineer`, `context-engineer`, `pra`) hazırlanıp bağımsız incelendi (`context/projects/deaths-demolition/wp-007/prompts/`).

**Yürütme promptu:** `planning/work-packages/WP-007-M2-SPECIAL-WEAPON-LIFECYCLE.md`

**Amaç:** Mevcut Special Weapon sistemini yeniden yazmadan, onaylı sözleşmelere (DEC-005/024/025/026, REQ-052..059, FLOW-005, WP-006 DamageTransaction) göre sağlamlaştırmak: round ilerlemesine bağlı planlama ve spawn yaşam döngüsü, geçerli/erişilebilir konum, yerde bekleme ile aktif kullanım sürelerinin ayrımı, tek eşzamanlı süreç ve parametrik sıralı ikinci spawn, pickup/kullanım/son saniye/otomatik bitiş, klasik silahın park edilip aynı build ile dönüşü, sınırsız mermi/reload'suz çalışma, klasik geliştirmelerden izolasyon, HUD/ışık/VFX/ses için gözlemlenebilir olay sözleşmesi, ölüm/abort/yeni koşu/pause/pooling güvenliği. Nihai Special Weapon türü, adı, hasar biçimi veya denge değeri belirlenmez; yeni DEC/REQ eklenmez; sayısal değerler PrototypeOnly'dir; U-1/U-2, round sınırı davranışı ve yarım-reload iptali değiştirilmez (yalnız karakterize edilir).

**Kapsam (gerçekleşen):**

- (A) Kanıt-önce: karakterizasyon testleri değişmemiş kod üzerinde koşuldu (EditMode 89/89, PlayMode 22/26 — dört kırmızıdan üçü gerçek AP-10 ihlali: ölüm/abort temizliği; biri test kurgusu, düzeltildi); hasar-çıktı paritesi üç prototip silah için refactor öncesi kilitlendi ve sonrasında aynen yeşil.
- (B) DamageTransaction: her bağımsız saldırı (dalga bandı, ağ çöküşü, kuyu pull tick'i) tek transaction (`HitscanEngine.BeginExternalAttack` scope; `ApplyExternalHit` açık transaction'a katılır); `Damage/DamageEventMeta.cs` değişmedi; NestedBegins 0, duplicate reddi 0.
- (C) Run yaşam döngüsü: `SpecialWeaponRoundBridge.RunStarted/RunEnded` + `RunDirector` kancaları; director `CancelAll` (scheduled/yerde/aktif), `SpecialWeaponPickup.Cancel`, `SpecialWeaponRuntime.Cancel`, `WeaponController.CancelSpecialWeapon`; abort yolunda beacon loop güvenliği; statik havuz şablonları (sahne başına havuz sızıntısı kapatıldı); duplicate director guard.
- (D) Konum: enjekte edilebilir `ISpecialDropValidator` (NavMesh varsayılan); geçerli konum yoksa güvenli iptal (ham koordinat yok), ilerleme değişince yeniden deneme, round bitince gerekçeli iptal; enjekte `ISpecialWeaponRandom` (Unity varsayılan) ve saf statik politika (`RollEligibility`, `TryComputeSpawnMark`, `PickWeighted`).
- (E) Olay sözleşmesi: mevcut altı olay ve payload'lar aynen; eklenen `SpecialWeaponScheduled`, `SpecialWeaponCancelled(reason, phase)`, `SpecialWeaponCleared`; `LifecycleTestKit` izlenen olaylar +20 (WeaponEvents 16 + köprü 4).
- (F) HUD: aktif timer ve marker mesafe metni yalnız görüntülenen değer değişince biçimlendirilir (ölçüm: aktif pencerede 600 karede yığın büyümesi ≈0 B/kare, `GC.GetTotalMemory` deltası + pozitif kontrol); ölüm vuruşunda (Dead fazı) Special "restored" haber banner'ı gösterilmez — HUD'ın mevcut Dead-fazı donma kalıbı.
- (G) Development-only `SpecialWeaponProbe` (`-ddSpecialWeaponProbe`, 10 aşama, gerçek Player); release Player'da sembol/argüman davranışı yok.
- Testler: EditMode +6 fixture (sözleşme, PrototypeOnly karakterizasyon, runtime, zamanlama politikası, konum politikası, kaynak taraması), PlayMode +6 fixture (yaşam döngüsü dilimi, PrototypeOnly davranış, hasar paritesi, hasar dilimi, koşu akışı, HUD allocation penceresi) + ortak kit; `SpecialWeaponChanceTests` kesin-değer assert'leri ikiye ayrıldı (config-relative sözleşme + `[Category("PrototypeOnly")]` karakterizasyon, adlar korundu).

**Kapsam dışı:** nihai Special Weapon türü/adı/hasar biçimi; Kagura Bell / Ofuda Loom / Tide Jewel'in canonical kabulü (fixture); U-1/U-2 kararı; nihai denge; final model/rig/animasyon/VFX/ses/UI; Special/Elite/Ultra düşmanlar; round formülü; DEC-021/043/048/050; profil XP eğrisi; achievement; B3/B4/B5; REQ-123; sınıf-3 A–H; Unity/URP/package; remote/push; AP-001.

**Çıkış koşulları:** paket promptundaki 23 çıkış kapısı ve 62 maddelik test matrisi; tam regresyon (import, EditMode, PlayMode gerçek Metal, temiz Library release build, Player smoke, RoundProbe, GoldProbe, ArmorProbe, SpecialWeaponProbe); S-01 koşulsuz PASS, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; yeni DEC/REQ yok; sayısal değerler PrototypeOnly; tek atomik commit; `main`'e merge ve push yapılmamıştır.

## WP-008 — M2 Economy & Round Upgrade Rules Completion

**Durum:** Tamamlandı ve `main`'e entegre edildi (2026-09-14). İzole worktree/branch `wp-008-m2-economy-upgrade-rules` (taban: `main` = `18a4cbf2` = `wp-007-m2-special-weapon`); package commit `0e2514a9691a9b92831d14107621905b7fd5cae9`, C-00 fast-forward, etiket `wp-008-m2-economy-upgrades`; tracked 803; final manifest `06806b50f42f62f1d630c2a8d7d38b13a00ba818d6013340bb6a0f55809cbe90`; final kapılar EditMode 474/474, PlayMode 127/127, RoundProbe 8/8, GoldProbe 12/12, ArmorProbe 9/9, SpecialWeaponProbe 10/10, EconomyUpgradeProbe 11/11; S-01 koşulsuz PASS, S-10 PASS 16/0/0; push yok. Başlangıç manifesti `9eb555b5…` (788 yol) = WP-007 final manifesti W; davranış değiştirilmeden baseline kapıları: import 0 hata, EditMode 420/420, PlayMode 123/123 (gerçek Metal).

**Yürütme promptu:** `planning/work-packages/WP-008-M2-ECONOMY-UPGRADE-RULES.md`

**Amaç:** WP-004 ile başlayan gold ve round sonu geliştirme döngüsünü kullanıcı kararlarıyla tamamlamak ve güvenilir bir üretim sözleşmesine dönüştürmek: düşman ödülünün `Base + Version Bonus + Spawn Armor Bonus` ile deterministik hesabı ve değişmez spawn snapshot'ı (DEC-043/069, REQ-074/148); round sonu ekranında tam üç farklı geçerli kart, pahalı kartın görünür kalması (DEC-047/070, REQ-078/149), kimlikli teklif oturumu ve round başına tek satın alma (DEC-048/071, REQ-150/151), bir kez ücretli ve 1–2 kart değiştiren yenileme (DEC-050/072, REQ-079/152), geçersiz teklifte gold kesmeyen güvenli hata durumu (DEC-073, REQ-153), atomik/tekrar-güvenli gold işlemleri (DEC-074, REQ-154), Results `Gold Spent = satın alma + yenileme` (DEC-075, REQ-155). Sayısal denge kararı yok; yeni sayılar merkezi config'te PrototypeOnly.

**Kapsam (gerçekleşen):**

- (A) Gold ödül modeli: `Economy/GoldRewardCalculator` (saf; base = Standard değeri, version bonus = aile değeri − base, zırh bonusu = spawn'daki Helmet/Body/Leg için `EconomyConfigSection.ArmorGoldBonus*`), `EnemyRewardSnapshot`/`SpawnArmorComposition` (ZombieEnemy `Initialise → RollArmor → TakeRewardSnapshot`; `OnSpawned` sıfırlar; `Die` yalnız snapshot'ı okur), `EnemyDeathInfo.Reward` (ek alan + yeni ctor, eski ctor'lar korundu), `GoldService.OnEnemyDied` snapshot'ı öder; `GoldRewardValue.Resolve(in snapshot, …)`; `LastRewardValue` readout.
- (B) Teklif oturumu: `Rounds/UpgradeOfferPolicy` (saf: uygunluk sınıflandırması, ağırlıklı iadesiz çekiliş, 1–2 slot reroll, doğrulama; enjekte `IUpgradeOfferRandom`), `RoundUpgradeShop` (run/round/SessionId/Revision, `PurchaseEntitlement`/`RerollEntitlement`, aynı (run, round) için teklif geri getirme, satın alma sırası session→entitlement→kart→bakiye→harcama→uygula→kilit, `TryPurchase(index, sessionId, revision)`, `Status`/`Fault` güvenli hata durumu, `PoolExhausted` uyarı seviyesi), `UpgradeCard` yeni sonuç/durum enum'ları, `RunDirector.DiscardOffer` koşu sınırlarında.
- (C) Yenileme: `RoundUpgradeShop.TryReroll(sessionId, revision)` (session→entitlement→fiyat→hesapla+doğrula→harcama→commit), `GoldSource.OfferReroll`, `EconomyConfigSection.RerollCost` (PrototypeOnly; `AllowReroll` kaldırıldı), alternatif aday yoksa `NoReplacementAvailable` (gold/hak korunur, kartlar kalır).
- (D) UI: `UpgradeScreen` gold şeridinde reroll bloğu (durum etiketi + sabit başlıklı düğme), OFFER LOCKED kart durumu, configuration-error/pool-exhausted güvenli mesajları, komutlar session+revision quote eder, reroll sonrası aynı karede satın alma kilidi, odak kuralları; F1 paneli reroll ve oturum readout'u.
- (E) Muhasebe: `GoldWallet.SpentOnUpgrades/SpentOnRerolls`, `RunStatsTracker.RecordRerollSpent`, `RunSummary.GoldSpent = purchases + rerolls` (+ `GoldSpentOnUpgrades/GoldSpentOnRerolls/RerollsUsed` alanları; Results'ta ayrı satır yok).
- (F) Testler: EditMode `GoldRewardBreakdownTests`, `UpgradeOfferPolicyTests`, `UpgradeOfferSessionTests` (+ `UpgradePurchaseContractTests`/`ProfileXpRulesTests`/`UiTextAuditTests` WP-008 güncellemeleri); PlayMode `EconomyUpgradeRulesSliceTests` (P1–P4) (+ `GoldUpgradeSliceTests` zırhlı Standard uyarlaması); development-build `EconomyUpgradeProbe` (`-ddEconomyProbe`, 11 aşama); `GoldProbe` uyarlaması.
- Belgeler: DEC-069..075, REQ-148..155, bu bölüm ve eşleme tablosu, `README`/`PROTOTYPE-ONLY` sayımları.

**DEC/REQ → runtime modülü → test eşlemesi** (Kaynak sütunu: kullanıcı promptu `work-packages/WP-008-M2-ECONOMY-UPGRADE-RULES.md` — "Bağlayıcı ürün kararları" madde no `K#` ve satır no `:n`; kapsam/kısıt bölümlerinden alınan ölçütler için satır no; C-00 koşulu 2026-09-13):

| Karar / gereksinim | Kaynak (prompt) | Runtime modülü | Test |
|---|---|---|---|
| DEC-041/042/044/045/046, REQ-073/075/076/077 (WP-004 tabanı) | K1 :103, K2 :105, K21 :165 (mevcut kayıtlar) | `GoldService`, `GoldPacketLifecycle`, `GoldWallet` | `GoldUpgradeSliceTests`, `GoldPacketLifecycleTests`, `GoldWalletContractTests` |
| DEC-043/069, REQ-074/148 (base + version + spawn zırh; snapshot değişmez) | K3 :107, K4 :113, K5 :115, K6 :121, K7 :123 | `GoldRewardCalculator`, `EnemyRewardSnapshot`, `ZombieEnemy.TakeRewardSnapshot`, `EnemyDeathInfo.Reward`, `GoldService.OnEnemyDied` | `GoldRewardBreakdownTests` (base, 4 versiyon, 8 zırh kombinasyonu, snapshot dondurma, LifeId tek ödül, replay), `EconomyUpgradeRulesSliceTests.P1`, `EconomyUpgradeProbe` 01–03 |
| DEC-047/070, REQ-078/149 (üç farklı geçerli kart; pahalı kart görünür) | K8 :125, K9 :127–134, K10 :136 | `UpgradeOfferPolicy.TryBuildOffer/Classify`, `RoundUpgradeShop.BuildOffer/IsPurchasable/CanAfford` | `UpgradeOfferPolicyTests`, `UpgradeOfferSessionTests.HerAcilis_…`, `KilitliVeMaxlanmis…`, `PahaliKart_…`, `ProfileXpRulesTests.DalUnlockSeviyesi_…`, `P2`, probe 04 |
| DEC-048/071, REQ-150/151 (tek satın alma; hak yenilenmez; oturum/revizyon) | K11 :138, K12 :140, K14 :144; REQ-150 ölçütleri kapsam §B :201–:205 (ilk teklif bir kez, reconnect'te ücretsiz teklif yok, güncel session/revision, replay, bayat komut reddi) | `RoundUpgradeShop.Open` (restore), `PurchaseEntitlement`, `TryPurchase(index, sessionId, revision)`, `RunDirector.DiscardOffer` | `UpgradeOfferSessionTests.RoundBasinaTek…`, `AyniRound_Yeniden…`, `AyniSatinAlmaKomutu_…`, `BayatOturumVeRevizyon_…`, `F1Overloadlari_…`, `UpgradePurchaseContractTests`, `P2` (çift input, pause, reopen, Continue), probe 06–08 |
| DEC-050/072, REQ-079/152 (bir kez ücretli yenileme; 1–2 kart) | K13 :142, K15 :146, K16 :148; REQ-152 ölçütleri kapsam §C :219–:220 (yetersiz gold ret; gold doğrulanmadan kesilmez) | `UpgradeOfferPolicy.TryReroll`, `RoundUpgradeShop.TryReroll/RerollAvailability`, `GoldSource.OfferReroll`, `EconomyConfigSection.RerollCost` | `UpgradeOfferPolicyTests.Reroll_…`, `UpgradeOfferSessionTests.BasariliTekReroll_…`, `Reroll_SatinAlmaSonrasi_…`, `Reroll_YetersizGold_…`, `RerollPahali_KartUcuz_…`, `P2`, probe 05 |
| DEC-073, REQ-153 (güvenli hata, gold kesilmez, Continue) | K17 :150–:157; kapsam §C :226 | `RoundUpgradeShop.Fail/Status/Fault`, `UpgradeScreen` notice | `UpgradeOfferSessionTests.GecerliUcKart…`, `TabloBos_…`, `TabloDuplicate_…`, `ConfigYok_…`, `HavuzTukendi_…`, `Reroll_AlternatifDalYok_…`, `Reroll_UcretSifir_…`, `P3` |
| DEC-074, REQ-154 (atomik, tekrar-güvenli) | K18 :159; REQ-154 ölçütleri kapsam §B :204 (replay) ve "Performans ve kod kalitesi" :378 (negatif bakiye oluşamaz) | `GoldWallet.TrySpend` (kimlikli işlem), `RoundUpgradeShop` sıralaması | `UpgradeOfferSessionTests.SatinAlmaSirasi_…`, `KapaliCuzdan_…`, `GoldRewardBreakdownTests.Cuzdan_AyniTransactionReplay_…`, `GoldWalletContractTests` |
| DEC-075, REQ-155 (Gold Spent = satın alma + yenileme) | K19 :161, K20 :163; REQ-155 ölçütleri kapsam §E :263 (replay/reconnect toplamı iki kez artırmaz) ve :267 (gold XP vermez) | `GoldWallet.SpentOnUpgrades/SpentOnRerolls`, `RunStatsTracker`, `RunDirector.BuildSummary`, `ResultsViewModel` | `UpgradeOfferSessionTests.GoldSpent_…`, `P2` (Results), probe 09–10 |
| REQ-018/021 (koşu-içi ekonomi/build ölümde sıfır) | K21 :165 (mevcut kayıt) | `RunDirector.EndRun/StartRun`, `GoldWallet.ResetForRunEnd`, `WeaponModifierState.ResetForNewRun` | `UpgradePurchaseContractTests.RoundDegisimi_…`, `P2` (yeni koşu), probe 11 |
| REQ-059 (Special Weapon izolasyonu) | K22 :167 (mevcut kayıt) | — (yapısal: `SpecialWeaponRuntime` teklif/build yüzeyine bağlı değil) | `UpgradeOfferSessionTests.SpecialWeapon_…`, `UpgradePurchaseContractTests.SpecialWeapon_IcinEtkiYoluYok`, `EconomyUpgradeRulesSliceTests.P4` |

**Yorumlar (kullanıcıya raporlanan, karar metninde açık olmayan noktalar):** "içerik açık" = mevcut `UpgradeBranchSettings.UnlockProfileLevel` (Explosion L2, Chain L3) — teklif kapısı artık `ProfileService.IsBranchUnlocked` okur (WP-005'te "karar bekliyor"); "silah/build'le uyumlu" = canlı tüketicisi olan ve tavanda olmayan dal (prototipte silah-tipine özel uyumluluk metadata'sı yok); yenileme için alternatif aday yoksa configuration error değil gerekçeli ret (gold ve hak korunur, kartlar kalır); kalan bütün dallar tavandaysa aynı güvenli durum uyarı seviyesiyle (`PoolExhausted`); yenilemede 1 ya da 2 kart seçimi enjekte RNG ile yazı-tura (50/50).

**Sınıf-3 devir (engel değil):** `Damage/DamageEvents.cs` artık `using DD.Prototype.Economy` (`EnemyDeathInfo.Reward`) → Damage↔Economy ad-alanı döngüsü (Economy→Damage zaten vardı); `EnemyRewardSnapshot`/`SpawnArmorComposition` alt/nötr bir katmana taşınırsa kapanır (S-01/S-03 notu).

**Kapsam dışı:** yeni silah/karakter/düşman; Special/Elite/Ultra düşman yetenekleri ve debut roundları; nihai gold/upgrade fiyat dengesi; yeni upgrade içeriği; Special Weapon upgrade ağacı; AP-001 aktarımı; Higgsfield/Adobe/Blender üretimi; yeni harita/sanat; gamepad; online/cloud/leaderboard/netcode; Unity/URP/package sürümü; remote/push; ilgisiz teknik borç.

**Çıkış koşulları:** paket promptundaki Definition of Done; tam regresyon (import, EditMode, PlayMode gerçek Metal, temiz Library release build, Player smoke, RoundProbe, GoldProbe, ArmorProbe, SpecialWeaponProbe, EconomyUpgradeProbe); S-01 koşulsuz PASS, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; sayısal değerler PrototypeOnly; tek atomik commit; `main`'e fast-forward ve etiket C-00/kullanıcı kararıyla; push yok.

## WP-009 — M2 Persistent Profile, XP, Statistics & Local Save Hardening

**Durum:** Yürütülüyor (S-04, 2026-09-14) — paket commit'i C-00 fast-forward kararı için hazırlanmaktadır; entegrasyon ve etiket (`wp-009-m2-profile-save`) yalnız C-00 doğrulamasından sonra. İzole worktree/branch `wp-009-m2-persistent-profile-save` (taban: `main` = `0e2514a9` = `wp-008-m2-economy-upgrades`, tracked 803, başlangıç manifesti `06806b50…`); tek yazma/commit sahibi S-04; S-01 mimari, S-09 UI veri bağlama, S-10 test/Git/teslim incelemeleri; S-02/S-03 yalnız RunSummary/RunDirector/RunEvents sınırı için salt-okunur danışma; push yok. Başlangıç kapıları (davranış değiştirilmeden): import 0 hata, EditMode 474/474, PlayMode 127/127, RoundProbe 8/8, GoldProbe 12/12, ArmorProbe 9/9, SpecialWeaponProbe 10/10, EconomyUpgradeProbe 11/11.

**Yürütme promptu:** `planning/work-packages/WP-009-M2-PERSISTENT-PROFILE-SAVE.md`

**Amaç:** WP-005 ile kurulan yerel profili (DEC-002/006/007/008/036/038; REQ-004/018/020–028/049/155) üretim sözleşmesine dönüştürmek: profil değişikliğinin transaction sınırı, uygulama yeniden açılışını aşan durable completion identity ile exactly-once, crash-safe ve fault-injection ile kanıtlanmış yerel kayıt zinciri, `SaveVersion` 1→2 kayıpsız migrasyon, kazanılmış unlock listelerinin erişim otoritesi olması, Results'ın yalnız immutable makbuz okuması. **Yeni ürün kararı (DEC/REQ) yoktur**; aşağıdaki maddeler teknik notlardır (ADR), sayısal değerler PrototypeOnly kalır.

**Teknik notlar (ADR — ürün kararı değildir):**

- ADR-009-1 *Completion identity:* `RunDirector.StartRun` her koşu için `Guid.NewGuid()` üretir; `RunSummary.CompletionId` (ek alan) taşır. Profil, uygulanan kimlikleri `ProfileData.AppliedCompletionIds` halkasında (en fazla 8, en eski atılır) saklar; run verisi değil, opaque anti-replay meta verisi; hiçbir yüzeyde gösterilmez. Kimliksiz özet reddedilir (`MissingIdentity`). Session-local `RunDirector.RunId` süreçle sıfırlanır ve kimlik değildir (probe faz B, PlayMode P4 kanıtı).
- ADR-009-2 *Transaction:* `ProfileService.ApplyRun` = girdi indirgeme (`RunRewardInput`: klamplı, yalnız iki XP kaynağı; `TotalXpEarned` okunmaz) → kimlik kontrolü → `Clone()` aday → `ProfileProgression` (saf) ile XP/seviye/istatistik/unlock/achievement → `Sanitize`+`IsValid` → `ProfileStore.Save` → yalnız başarıda canlı profil = aday, makbuz mühürlenir, `Changed` bir kez. Başarısızlıkta canlı profil = disk, kimlik kaydedilmez (retry güvenli), makbuz `SaveFailed`. Aynı kimlik: bellekteki orijinal makbuz döner ya da `Duplicate` makbuz; hiçbir şey değişmez. Commit otoritesi `ProfileRunCommitter` (RunEnded dinleyicisi); `PrototypeUiFlow.s_resultsScheduled` yalnız navigasyon koruması.
- ADR-009-3 *Kayıt zinciri:* serialize + bellek içi geri-parse → `.writing` adayı (iki parça yazım, `Flush(true)`=fsync) → geri okuma eşitliği → geçerliyse primary → `.bak` (fsync) → `File.Replace(writing, primary, null)` (tek rename) → primary geri okuma; hata/uyumsuzlukta `.bak` geri konur. Recovery: primary geçerli → kullan, orphan `.writing` sil; primary yok → doğrulanmış `.writing` ileri sar, yoksa doğrulanmış `.bak`, yoksa taze; primary bozuk → `.corrupt.<ticks>.json` olarak koru, `.writing` sil, yalnız doğrulanmış `.bak`; future version → dosyaya dokunma, oturum salt-okunur (`SaveLocked`). "Atomik" iddiası fault-injection (aşama başına hata + aşama başına çökme anlık görüntüsü) ile sınırlıdır: her noktada ya eski ya yeni eksiksiz profil.
- ADR-009-4 *Şema v2:* `UnattributedKills` (legacy `TotalKills > tier toplamı` farkı; tier'lara dağıtılmaz — kayıpsız invariant `TotalKills == Standard+Special+Elite+Ultra+Unattributed`), `AppliedCompletionIds`. v1 belgesi alan alan korunarak okunur; migrasyon idempotent; bilinmeyen achievement/silah/dal id'leri yerinde saklanır (karantina = yerinde tutma) ve hiçbir kapıyı açmaz (kapılar yalnız tanımlı enum adlarını kabul eder).
- ADR-009-5 *Entitlement:* `IsWeaponUnlocked/IsBranchUnlocked` kazanılmış listeyi okur (seviye karşılaştırması değil); merdiven yalnız yeni kazanımı belirler (`GrantUnlocksForLevel` yalnız ekler). Profile pane, Selection ve WP-008 teklif kapısı aynı otoriteyi okur. Yükleme anında seviyenin hak ettiği içerik sessizce (rapor edilmeden) listeye eklenir; bir sonraki kayıtla diske geçer.
- ADR-009-6 *Sahiplik:* `prototype-profile.json.lock` (pid + süreç adı). Canlı ve aynı adlı başka süreç kilidi tutuyorsa oturum salt-okunur ve Profile pane bunu söyler; ölü sürecin kilidi devralınır; aynı pid (Editor domain reload) her zaman sahiptir; çıkışta bırakılır. Oyuncu/düşman/hasar sistemleri profile bağlanmaz (kaynak taraması testi).
- B5 abort: değişmedi — pause → abort `RunEnded` yayınlamaz, `ApplyRun` çağrılmaz; RAM/disk hash ile sıfır delta (PlayMode P2, probe 11). WP-008 reroll 1–2 kart 50/50 davranışı PrototypeOnly/noncanonical kalır.

**Kapsam (gerçekleşen):**

- (A/B) `Profile/ProfileProgression` (yeni, saf), `Profile/ProfileRunCommitter` (yeni), `Profile/ProfileService` (transaction), `Profile/RunRewardReport` (immutable; `Draft.Seal()`, `ProfileCommitOutcome`), `Rounds/RunEvents.RunSummary.CompletionId`, `Rounds/RunDirector.CompletionId`, `UI/Runtime/Flow/PrototypeUiFlow` (ApplyRun çağrısı kaldırıldı; quit'te kilit bırakma; F1 readout).
- (C/D) `Profile/ProfileStore` (zincir, recovery, sahiplik, teşhis; test dikişleri `#if DEVELOPMENT_BUILD || UNITY_EDITOR`), `Profile/ProfileMigration` (yeni), `Profile/ProfileData` (v2).
- (E) Saturating aritmetik, sınırlı seviye döngüsü (`MaxLevelUpsPerTransaction`), enjekte `IUnlockLadder`, sentetik achievement tanımlarıyla test.
- (F) `ResultsViewModel` (makbuz `Outcome`'a göre dürüst dipnot/XP notu, uygulanmamışta kutlama yok), `ProfileViewModels` (unlock satırları entitlement; `IProfileLookup.PersistenceNotice`; `StatisticsViewModel` yalnız >0 iken "Recorded Before Tier Tracking" satırı).
- Testler: EditMode `ProfileProgressionTests`, `ProfileTransactionTests`, `ProfileStoreFaultTests` (yeni) + `ProfileXpRulesTests`/`ProfilePresentationTests`/`ResultsViewModelTests`/`SelectionMemoryTests`/`UpgradeOfferSessionTests` güncellemeleri; PlayMode `ProfilePersistenceSliceTests` (P1–P5) + `MetaUiSliceTests`/`SessionSelectionTests` kimlik eklemeleri; development-build `ProfilePersistenceProbe` (`-ddProfileProbe`, fazlar A/B/C/D, 14 aşama).

**REQ/DEC → runtime modülü → test eşlemesi:**

| Karar / gereksinim / teknik not | Runtime modülü | Test / kanıt |
|---|---|---|
| DEC-002, DEC-006, DEC-036 (yerel, offline, tek profil, tek aktif koşu) | `ProfileStore` (tek dosya, `persistentDataPath`), `ProfileService` | `ProfileStoreFaultTests`, `ProfileIsolationTests`, probe faz A–D |
| DEC-007, REQ-018, REQ-020 (koşu-kalıcı sınırı; run gold/build dosyaya sızmaz) | `ProfileData` (alan seti), `RunRewardInput` | `ProfileXpRulesTests.JsonAnahtarlari_RunGoldVeBuildIcermez`, `ProfileProgressionTests.GoldHarcamaUpgradeRerollSure_GirdideYok` |
| DEC-008, REQ-021..024 (yalnız kill + tamamlanan round XP; gold/harcama/upgrade/reroll/süre XP vermez; Total = round + kill) | `RunRewardInput`, `ProfileProgression.ApplyStatistics/ApplyXp` | `ProfileProgressionTests` (ToplamXp, Celiskili, Negatif, Tier parametrik), `ProfileXpRulesTests`, PlayMode `GoldUpgradeSliceTests`, probe 01–04 |
| REQ-025..028, DEC-038 (lifetime istatistikler, tier kırılımı, unlock listeleri; kill invariantı) | `ProfileData.Sanitize/IsValid`, `ProfileProgression.ApplyStatistics` | `ProfileProgressionTests.Sanitize_KillInvarianti…`, `OzetToplamKill…`, `ProfileStoreFaultTests.V1_V2…`, `ProfilePresentationTests.Istatistik_AtanmamisKill…` |
| REQ-004 / §2.3 (profil seviyesi güç vermez) | — (yapısal: Player/Weapons/Damage/Enemies/Economy/Spawn profili okumaz) | `ProfileTransactionTests.ProfilSeviyesi_CombatSistemleriTarafindanOkunmaz`, PlayMode `ProfilePersistenceSliceTests.P3` |
| REQ-049, ADR-009-5 (unlock otomatik, claim yok, entitlement otoritesi, eşik yükselse de açık) | `ProfileProgression.GrantUnlocksForLevel/IsWeaponEntitled/IsBranchEntitled`, `ProfileService.IsWeaponUnlocked/IsBranchUnlocked`, `ProfileViewModel`, `SelectionScreen`, `RoundUpgradeShop` yüklemi | `ProfileProgressionTests.Unlock_*`, `ProfileTransactionTests.Entitlement_*`, `ProfilePresentationTests.Profil_UnlockSatirlari_Entitlement…`, `ProfileXpRulesTests.DalUnlockSeviyesi_TeklifKapisi…`, `SessionSelectionTests` |
| ADR-009-1/2 (exactly-once, kimliksiz ret, retry) | `ProfileService.ApplyRun`, `ProfileRunCommitter`, `ProfileData.AppliedCompletionIds`, `RunDirector.CompletionId` | `ProfileTransactionTests` (AyniKimlik ×2, AyniVeri_FarkliKimlik, Kimliksiz, KimlikHalkasi, SaveBasarisiz ×9), PlayMode P1/P4/P5, probe 05/06/09/12 |
| ADR-009-3 (crash-safe zincir, recovery, future version) | `ProfileStore.Save/Load` | `ProfileStoreFaultTests` (HataEnjeksiyonu ×9, CokmeEnjeksiyonu, disk durumları ×7, Teshis, FutureVersion), PlayMode P5, probe 10a/10b |
| ADR-009-4 (v1→v2 kayıpsız, idempotent; bilinmeyen id) | `ProfileMigration`, `ProfileData.Sanitize` | `ProfileStoreFaultTests.V1_V2…`, `Migration_Idempotent…`, `CokluLoadSaveDongusu…`, `EksikAlanlar…`, `NegatifVeAralikDisi…`; `ProfileTransactionTests.BilinmeyenId…` |
| REQ-155 / §2.6 (Results immutable makbuz; save failure dürüst) | `RunRewardReport`, `ResultsViewModel.DescribeOutcome`, `ResultsScreen` | `ResultsViewModelTests.SonNotlar…`, `ProfileTransactionTests.Makbuz_Immutable…`, PlayMode P1/P5 |
| ADR-009-6 (çoklu Player sahipliği) | `ProfileStore.AcquireOwnership/ReleaseOwnership` | `ProfileStoreFaultTests.Sahiplik_*`, `ParseLock_*` |
| B5 (abort değişmedi) | `PrototypeUiFlow.AbortRunToMainMenu` (değişmedi) | `MetaUiSliceTests.Abort_…`, PlayMode `ProfilePersistenceSliceTests.P2`, probe 11 |

**Kapsam dışı:** final XP eğrisi/miktarları, maksimum seviye, unlock seviyeleri ve achievement içeriği; yeni silah/dal/karakter; kalıcı güç bonusu; run history/archive; currency/daily reward/battle pass/claim; cloud save/hesap/leaderboard/online/netcode; save slot; şifreleme/anti-cheat; telemetri; aktif koşuyu crash/quit anında yazmak; UI görsel yeniden tasarımı; yeni art/ses/animasyon; tier yetenekleri/debut; Unity/URP/package sürümü; AP-001/AP-002 üretimi; remote/push.

**Çıkış koşulları:** paket promptundaki Definition of Done; tam regresyon (import, EditMode, PlayMode gerçek Metal, temiz Library release build, Player smoke, RoundProbe, GoldProbe, ArmorProbe, SpecialWeaponProbe, EconomyUpgradeProbe, ProfilePersistenceProbe); gerçek kullanıcı profili değişmemiş; S-01 koşulsuz PASS, S-09 PASS, S-10 bağımsız PASS; Git F0–F10 ve manifest temiz; tek atomik commit; `main`'e fast-forward ve etiket C-00 kararıyla; push yok.
