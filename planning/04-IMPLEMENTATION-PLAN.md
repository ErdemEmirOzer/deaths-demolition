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

**Durum:** Yürütülüyor (S-03, 2026-09-12). İzole branch `wp-003-m1-rounds-spawning` (taban: `main` = `8fd858670e4f5a77e6f12811436e2c5a846a510b` = `wp-002-m1-combat`); tek yazma/commit sahibi S-03; S-01 entegrasyon sınırlarını salt okunur doğrular; S-10 kabul kapılarını bağımsız doğrular; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Başlangıç manifesti `b63ba343…` (669 yol) = WP-002 final manifesti; davranış değiştirilmeden baseline kapıları: import 0 hata, EditMode 192/192, PlayMode 48/48 (gerçek Metal).

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
