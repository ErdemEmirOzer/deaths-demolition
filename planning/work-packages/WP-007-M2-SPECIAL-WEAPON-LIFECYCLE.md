WP-007 — M2 SPECIAL WEAPON LIFECYCLE & INTEGRATION HARDENING

PAKET SAHİBİ
S-02 — Core Gameplay & Combat

ANA KOORDİNASYON
C-00 — Deaths Demolition Main Coordination

AMAÇ

Mevcut prototipte bulunan Special Weapon sistemini topluca yeniden yazmadan incele, onaylanmış ürün sözleşmelerine göre sağlamlaştır ve M2 üretim standardına getir.

Bu paket şu alanları tamamlar:

- Round ilerlemesine bağlı Special Weapon planlama ve spawn yaşam döngüsü
- Geçerli ve erişilebilir spawn konumu seçimi
- Yerde bekleme süresi ile aktif kullanım süresinin ayrılması
- Tek eşzamanlı Special Weapon süreci
- İleri roundlarda parametrik ve sıralı ikinci spawn desteği
- Pickup, kullanım, son saniye uyarısı ve otomatik sona erme
- Klasik silahın geçici olarak devre dışı bırakılıp güvenli biçimde geri getirilmesi
- Sınırsız mermi ve reload’suz Special Weapon çalışma sözleşmesi
- Klasik silah geliştirmelerinden tam izolasyon
- HUD, ışık sütunu, VFX ve ses için güvenilir olay sözleşmeleri
- Ölüm, abort, yeni koşu, pause ve pooling güvenliği
- WP-006 DamageTransaction ve bölgesel zırh sistemine güvenli entegrasyon

Bu paket nihai Special Weapon türlerini, adlarını, hasar biçimlerini veya denge değerlerini belirlemez.

DOĞRULANACAK TABAN

Repository:

/Users/Erdem/Documents/Deaths Demolition

Unity projesi:

/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept

Beklenen main:

b974529f86d65b986ad054e5762331da55311be4

Beklenen etiket:

wp-006-m2-armor

Başlamadan önce salt okunur olarak doğrula:

- main HEAD
- çalışma ağacının temizliği
- wp-006-m2-armor etiketi
- WP-006 manifesti ve entegrasyon kaydı
- mevcut branch/worktree envanteri
- önceki yedi paketin commit zinciri
- başlangıçtaki EditMode ve PlayMode test toplamları
- kullanıcı profil verisinin mevcut durumu

Beklenen taban ile gerçek durum uyuşmuyorsa hiçbir dosyaya yazmadan C-00’a bildir.

ÇALIŞMA BRANCH’İ

Önerilen izole branch/worktree kimliği:

wp-007-m2-special-weapon-lifecycle

Main veya başka bir WP worktree’sinde doğrudan geliştirme yapma. Worktree yolunu mevcut KADRO konvansiyonuna göre C-00 doğrulasın.

KADRO PROMPT YÖNETİŞİMİ

C-00 ve S-02 alt görev promptlarını tek başına hazırlamayacaktır.

Uygulamadan önce gerçek KADRO sistemindeki şu prompt uzmanlarını görevlendir:

- prompt-engineer
- pra
- context-engineer

Bu uzmanlar:

1. Bu ana promptu bağlayıcı DEC, REQ ve FLOW maddelerine ayırmalı.
2. Her uzman için dar kapsamlı uygulama veya inceleme promptu oluşturmalı.
3. Mevcut prototip fikirlerinin onaylanmış ürün kararı gibi kullanılmadığını doğrulamalı.
4. Sayısal PrototypeOnly değerlerin final dengeye dönüşmesini engellemeli.
5. Kod, test ve gereksinim izlenebilirliğini korumalı.
6. Uygulama promptlarını başka bir prompt uzmanı veya bağlam uzmanına bağımsız olarak inceletmeli.
7. Kullanılan gerçek KADRO slug ve agent-instance kimliklerini raporlamalı.

KADRO registry’sinde gerçekten bulunan ajanları kullan; olmayan slug veya uzman katkısı icat etme.

UZMANLIK DAĞILIMI

- S-02: paket sahibi, tek yazma ve commit sorumlusu
- S-03: round ilerlemesi, spawn zamanlaması ve konum seçimi
- S-04: klasik silah build/upgrade izolasyonu
- S-06: silah geçişi ve animasyon olay sözleşmeleri
- S-07: beacon, son saniye uyarısı, kullanım ve bitiş VFX olayları
- S-08: spawn, pickup, active loop, warning ve expiration ses olayları
- S-09: HUD snapshot, dünya işareti ve timer sunumu
- S-01: mimari ve entegrasyon incelemesi
- S-10: bağımsız kabul ve regresyon
- game-developer
- unity-specialist
- game-test-engineer
- test-engineer
- gt
- git-repository-safety

Gereken uzman sayısını önceden sınırlandırma. Bağımsız analizleri paralel yürüt; aynı dosya veya sisteme dokunan işleri S-02 altında sırala.

Uygulamayı yazan ajan kendi çalışmasına nihai kabul veremez.

BAĞLAYICI KAYNAKLAR

Başlamadan önce bütünüyle incele:

- planning/00-PROJECT-PLAN.md
- planning/01-REQUIREMENTS.md
- planning/02-FLOWS-ARCHITECTURE.md
- planning/03-ROADMAP.md
- planning/04-IMPLEMENTATION-PLAN.md
- planning/work-packages/WP-002-M1-CORE-COMBAT.md
- planning/work-packages/WP-003-M1-ROUNDS-SPAWNING.md
- planning/work-packages/WP-004-M1-GOLD-UPGRADE-LOOP.md
- planning/work-packages/WP-005-M1-META-UI-RESULTS-PROFILE.md
- planning/work-packages/WP-006-M2-REGIONAL-ARMOR-EXPLOSION.md
- WP-006 nihai raporu
- S-01 WP-006 incelemesi
- S-10 WP-006 kabul kararı
- WP-006 entegrasyon kaydı
- Mevcut Special Weapon production ve test yüzeyi

Özellikle bağlayıcı maddeler:

- DEC-005
- DEC-024
- DEC-025
- DEC-026
- REQ-052
- REQ-053
- REQ-054
- REQ-055
- REQ-056
- REQ-057
- REQ-058
- REQ-059
- FLOW-005
- FLOW-001
- FLOW-006
- WP-006 DamageTransaction sözleşmesi

Mevcut kodda en az şu yüzeyleri incele:

- SpecialWeaponDirector
- SpecialWeaponRoundBridge
- SpecialWeaponSpawnPoint
- SpecialWeaponPickup
- SpecialWeaponRuntime
- WeaponController
- PlayerWeaponBinding
- WeaponEvents
- RunDirector
- RunEvents
- HitscanEngine
- DamageTransaction
- EnemyDamageInfo.Meta
- DamageEventClassifier
- RunHudScreen
- mevcut Special Weapon EditMode/PlayMode testleri
- yaşam döngüsü ve FlowRace testleri

MEVCUT PROTOTİP KİMLİKLERİ İÇİN KURAL

Mevcut prototipte görülen:

- Kagura Bell
- Ofuda Loom
- Tide Jewel

nihai veya kullanıcı tarafından onaylanmış Special Weapon türleri değildir.

Bunları:

- silme,
- yeniden adlandırma,
- final içerik olarak kaydetme,
- onaylanmış ürün kararı olarak gösterme,
- yeni planning kararlarına dönüştürme.

Yalnız mevcut prototipin uyumluluk/test fixture’ları olarak korunabilirler. Production yaşam döngüsü silah kimliği ve silaha özel saldırı davranışından ayrıştırılmalıdır.

Mevcut kesin değerleri doğrulayan testler varsa bunları ikiye ayır:

1. Onaylı ürün sözleşmesini doğrulayan config-relative testler
2. Açıkça PrototypeOnly olarak işaretlenen prototip karakterizasyon testleri

Prototip değerlerini REQ kabul kriteri veya nihai denge olarak sunma.

DEĞİŞMEZ SPECIAL WEAPON SÖZLEŞMESİ

1. Spawn uygunluğu

- Special Weapon yalnız belirlenen başlangıç roundundan sonra çıkma ihtimaline sahip olur.
- Uygun roundlarda çıkması garanti değildir; ihtimal üzerinden değerlendirilir.
- Başlangıç roundu ve ihtimal eğrisi henüz final değildir.
- Bu değerler merkezi PrototypeOnly konfigürasyondan veya enjekte test fixture’ından okunmalıdır.

2. Round ilerlemesi

- Spawn roundun hemen başında gerçekleşmez.
- Spawn round bitmeye çok yakın gerçekleşmez.
- Geçerli bir round-progress aralığı içinde rastgele bir noktada planlanır.
- Round ilerlemesi, mevcut RunDirector tarafından sağlanan authoritative ilerlemeden alınır.
- Zaman tabanlı gizli ikinci bir round-progress kaynağı oluşturulmaz.
- Aynı seed ve aynı girdiler aynı planlama sonucunu üretmelidir.

3. Eşzamanlılık

Aynı anda yalnızca tek bir Special Weapon süreci bulunabilir.

Şu durumlar birbirini dışlamalıdır:

- planlanmış
- yerde
- pickup işlemi
- aktif kullanım
- bitiş/temizleme

Bir Special Weapon yerde veya aktif kullanımdayken yenisi spawn olamaz.

İleri roundlarda ikinci Special Weapon’a izin verilebilecek parametrik mekanizma bulunabilir; ancak:

- ikinci spawn nadir bir olasılığa bağlıdır,
- ilk Special Weapon süreci tamamen bitmeden başlayamaz,
- aynı round içinde sıralı çalışır,
- toplam sayı yapılandırılmış sınırı aşmaz.

İkinci spawnın başlangıç roundu ve ihtimali final ürün değeri değildir.

4. Spawn konumu

- Spawn konumu harita üzerinde geçerli ve erişilebilir olmalıdır.
- Oyuncunun bulunduğu güvenli rotadan çıkmasını teşvik edebilmelidir.
- Oyuncunun hemen üzerinde veya tepki veremeyeceği bir noktada oluşmamalıdır.
- Mevcut SpecialWeaponSpawnPoint veya doğrulanmış konum adayları kullanılabilir.
- Kesin minimum mesafe, seçim ağırlığı veya rota formülü final karar olarak belirlenmez.
- Konum doğrulaması enjekte edilebilir ve test edilebilir olmalıdır.
- Geçerli konum yoksa sistem güvenli biçimde spawnı iptal etmeli; rastgele geçersiz dünya koordinatına düşmemelidir.
- Başarısız spawn sayaçları ve yaşam döngüsü kilidi bozulmamalıdır.

5. Yerde bekleme

- Special Weapon’ın ayrı bir yerde bekleme süresi vardır.
- Aktif kullanım süresi yerdeyken başlamaz.
- Yerde bekleme timer’ı pause sırasında ilerlemez.
- Süre dolarsa pickup temizlenir ve HUD/ışık/ses durumu bir kez kapatılır.
- Pickup ve ground-expiration aynı karede çift sonuç üretemez.

6. Pickup

Pickup yalnız bir kez gerçekleşmelidir.

Pickup olduğunda:

- yerde bekleme timer’ı durur,
- active timer sıfırdan başlamalı,
- klasik silah yok edilmemeli,
- klasik silah geçici olarak devre dışı kalmalı,
- devam eden klasik ateş veya reload güvenli biçimde kesilmeli,
- klasik build ve koşu içi geliştirmeler korunmalı,
- HUD durumu OnGround’dan Active’e tek bir geçiş yapmalı,
- beacon ve yerdeki loop feedback’i durmalı,
- aktif kullanım feedback’i başlamalıdır.

Klasik silahın yarım reload ilerlemesinin korunup korunmadığı onaylanmış ürün kararı değildir. Mevcut davranışı değiştirmeden koru ve raporda PrototypeOnly davranış olarak belirt. Değişiklik zorunluysa kullanıcı kararına yükselt.

7. Aktif kullanım

- Aktif kullanımda mermi tüketilmez.
- Reload yapılmaz.
- Aktif süre yalnız pickup sonrasında işler.
- Pause sırasında active timer ve saldırı saatleri ilerlemez.
- Special Weapon klasik silahın WeaponModifierState veya round upgrade değerlerinden etkilenmez.
- Klasik build snapshot’ı Special Weapon runtime değerlerini değiştiremez.
- Yeni input düğmesi veya kontrol şeması icat edilmez; mevcut ateş girişi korunur.

8. Son saniye uyarısı

- Kalan süre HUD’da açıkça gösterilir.
- Son saniye eşiğine girildiğinde görsel ve işitsel uyarı oluşturulabilir.
- Eşik geçişi aynı aktivasyonda yalnız bir kez olay üretmelidir.
- Timer her kare formatlanarak gereksiz allocation oluşturmamalıdır.
- Kesin uyarı süresi PrototypeOnly konfigürasyondur.

9. Bitiş

Aktif süre sona erdiğinde:

- Special Weapon saldırısı durur,
- Special Weapon runtime temizlenir,
- loop ses/VFX durur,
- HUD Special Weapon durumu kapanır,
- klasik silah otomatik olarak yeniden kullanılabilir olur,
- klasik silahın build’i kaybolmaz,
- aynı bitiş iki kez çalışmaz,
- ikinci spawn uygunsa ancak bu tam temizlemeden sonra değerlendirilebilir.

10. Run yaşam döngüsü

Şu durumlarda bütün Special Weapon state’i güvenli şekilde temizlenmelidir:

- oyuncu ölümü
- koşunun abort edilmesi
- yeni koşu başlangıcı
- scene teardown
- ilgili servis/director kapanışı
- havuz reseti
- statik event reseti

İkinci koşuya şunlar taşınmamalıdır:

- eski pickup
- active runtime
- timer
- callback
- beacon
- HUD snapshot
- loop audio
- scheduled spawn
- ikinci spawn hakkı
- event aboneliği

Round tamamlandığında yerdeki veya aktif Special Weapon’ın sonraki rounda geçip geçmemesi açık ürün kararı değildir. Mevcut davranışı değiştirmeden koru, testle karakterize et ve canonical karar olarak sunma. Paketi tamamlamak için davranış değişikliği zorunluysa C-00 üzerinden kullanıcıya yükselt.

WP-006 DAMAGE VE ZIRH ENTEGRASYONU

Her dış Special Weapon saldırısı WP-006’nın tek kanonik hasar yolunu kullanmalıdır.

Zorunlu kurallar:

- Her bağımsız saldırı için tek DamageTransaction açılır.
- Transaction try/finally ile mutlaka kapanır.
- Aynı saldırıdan etkilenen bütün hedefler aynı transaction kimliğini taşır.
- Hedef başına ayrı ve iç içe transaction açılmaz.
- DamageTransaction.NestedBegins artmamalıdır.
- Bir düşman aynı yaşam ve transaction içinde aynı saldırıyı iki kez almamalıdır.
- Ölü düşmana geç hasar yeni ölüm, kill, gold veya feedback üretmemelidir.
- EnemyDamageInfo.Meta kullanılmalıdır.
- DamageEventClassifier ile mevcut feedback sınıflandırması korunmalıdır.
- Yeni paralel hasar sistemi veya yeni global/statik damage event oluşturulmaz.
- Bölgesel zırh ve patlama sözleşmeleri değiştirilmez.
- Special Weapon saldırısı zırhı etkiliyorsa mevcut direct/explosion sözleşmelerinden uygun olanı kullanmalıdır; yeni üçüncü bir armor bypass yolu oluşturulmaz.

U-1 VE U-2 KURALI

WP-006 raporundaki şu davranışlar kullanıcı tarafından onaylanmış ürün kararı değildir:

- U-1: Kagura/Tide hasarının Direct olarak sınıflandırılması
- U-2: Doğrudan vurulan hedefin aynı saldırının patlamasından muaf olması

WP-007:

- bunları canonical karar yapmayacak,
- planning belgelerine karar olarak yazmayacak,
- yeni production mimarisini bunlara bağımlı kurmayacak,
- bunları değiştirmeyecek,
- gerekiyorsa yalnız PrototypeOnly karakterizasyon testiyle koruyacaktır.

U-1 veya U-2’yi değiştirmek gerekiyorsa bu yeni bir Special Weapon hasar tasarımı kararıdır ve kullanıcıya yükseltilmelidir.

UYGULAMA YAKLAŞIMI

Önce mevcut davranışı kanıtla.

Mevcut kod onaylı sözleşmeye uyuyorsa yeniden yazma. Bunun yerine:

- karakterizasyon testi ekle,
- state ownership’i netleştir,
- duplicate veya lifecycle riskini kapat,
- sayısal değerleri PrototypeOnly olarak sınıflandır,
- test edilebilir dependency’ler çıkar,
- silah kimliği ile genel yaşam döngüsü arasındaki bağı azalt.

Gerekli görülürse saf/test edilebilir bileşenlere ayrılabilecek alanlar:

- eligibility policy
- scheduling policy
- injected RNG
- round-progress source
- spawn-position validator/selector
- lifecycle state
- ground timer
- active timer
- second-spawn policy
- teardown/reset coordinator

Bunlar önerilen teknik sorumluluklardır; mevcut mimariyi gereksiz biçimde yeniden adlandırmak veya topluca değiştirmek zorunlu değildir.

Enjeksiyon noktaları Unity global random, scene search veya gerçek kare zamanına sıkı bağımlılığı azaltmalıdır.

OLAY VE SUNUM SÖZLEŞMESİ

Mevcut WeaponEvents.Special ve SpecialWeaponHudState tüketicilerini incele.

Mümkünse mevcut payload’ları koru. Değişiklik zorunluysa compatibility adapter ve regresyon testi sağla.

En az şu geçişler gözlemlenebilmelidir:

- spawn scheduled
- spawned/on ground
- pickup
- active
- final-seconds warning
- ground expired
- active expired
- cancelled by run end/death
- fully cleared

Spawn başına şu sinyaller tam bir kez üretilmelidir:

- belirgin ses olayı
- ışık sütunu/beacon olayı
- HUD/world-marker olayı

Bu paket final ses, ışık, VFX veya UI asset’i üretmez. Yalnız event, state ve placeholder bağlantı sözleşmelerini sağlamlaştırır.

PERFORMANS VE GÜVENİLİRLİK

- Normal gameplay Update yolunda scene search kullanma.
- Her kare string biçimlendirme yapma.
- LINQ veya gereksiz allocation ekleme.
- Kontrolsüz Physics overlap veya büyüyen koleksiyon kullanma.
- Pickup ve runtime nesnelerini güvenli pooling ile yönet.
- Event abonelikleri ikinci koşuda çoğalmamalıdır.
- Timer’lar authoritative pause durumuna bağlı olmalıdır.
- Aynı director veya runtime’ın birden fazla örneği sessizce oluşmamalıdır.
- Runtime duplicate bulursa güvenli davranmalı ve tanılama üretmelidir.
- Release build’e development probe veya debug-only davranış sızmamalıdır.

SAYISAL SINIR

Şunların hiçbirini final ürün değeri olarak belirleme:

- ilk uygun round
- round başına spawn ihtimali
- ihtimal eğrisi
- progress pencere sınırları
- ikinci spawn başlangıç roundu
- ikinci spawn ihtimali
- yerde bekleme süresi
- aktif kullanım süresi
- son saniye uyarı eşiği
- oyuncudan minimum mesafe
- silah seçim ağırlıkları
- hasar
- ateş hızı
- radius
- etki kuvveti
- menzil
- kamera tepkisi
- VFX veya ses bütçesi

Gereken değerler tek merkezde açıkça PrototypeOnly olmalı veya test fixture’ından enjekte edilmelidir.

Mevcut testlerde görülen Round 4, %35–65, %30–70, 22/11 saniye, Round 8 veya %10 gibi değerler final karar değildir.

ZORUNLU TEST MATRİSİ

Eligibility ve scheduling:

1. Enjekte edilen başlangıç roundundan önce spawn planlanmaz.
2. Başlangıç roundundan sonra yalnız enjekte edilen ihtimal sonucuna göre planlanır.
3. Başarısız roll state’i kilitlemez.
4. Aynı seed ve girdiler aynı schedule sonucunu üretir.
5. Spawn yalnız config-relative geçerli progress aralığında gerçekleşir.
6. Round başlangıcı ve bitimine yakın sınırlar ihlal edilmez.
7. Eski rounda ait schedule yeni round veya koşuya taşınmaz.

Tekillik ve ikinci spawn:

8. Scheduled durumunda ikinci eşzamanlı spawn yoktur.
9. OnGround durumunda ikinci eşzamanlı spawn yoktur.
10. Active durumda ikinci eşzamanlı spawn yoktur.
11. İkinci spawn yalnız config izin veriyorsa değerlendirilebilir.
12. İkinci spawn ilk sürecin tam temizliğinden önce başlayamaz.
13. Round başına yapılandırılmış üst sınır aşılamaz.

Spawn konumu:

14. Yalnız geçerli ve erişilebilir aday seçilir.
15. Geçersiz veya erişilemez aday reddedilir.
16. Geçerli aday yoksa hiçbir pickup oluşturulmaz.
17. Başarısız yer seçimi lifecycle sayacını bozmaz.
18. Aynı seed ve aday listesi deterministik seçim üretir.

Yerde bekleme ve pickup:

19. Ground timer yalnız spawn sonrasında başlar.
20. Active timer yerdeyken ilerlemez.
21. Pause sırasında ground timer delta üretmez.
22. Ground expiration yalnız bir kez temizleme yapar.
23. Pickup yalnız bir kez kabul edilir.
24. Pickup ve expiration yarışında tek sonuç oluşur.
25. Spawn sesi, beacon ve HUD marker tam bir kez başlar.
26. Pickup sırasında ground feedback tam bir kez kapanır.

Aktif kullanım:

27. Active timer pickup anında tam süreden başlar.
28. Special Weapon mermi tüketmez.
29. Special Weapon reload durumuna girmez.
30. Klasik silah yok edilmez; yalnız kullanılamaz hale gelir.
31. Klasik build snapshot’ı korunur.
32. Special Weapon, WeaponModifierState veya upgrade dallarından etkilenmez.
33. Pause sırasında active timer ve saldırı zamanı ilerlemez.
34. Final-seconds uyarısı eşik geçişinde yalnız bir kez oluşur.
35. Süre bitince klasik silah otomatik ve tek seferde döner.
36. Loop audio/VFX/HUD state bitişte temizlenir.

DamageTransaction:

37. Her Special Weapon saldırısı tek transaction kullanır.
38. Çoklu hedef aynı transaction kimliğini taşır.
39. Hedef başına nested transaction açılmaz.
40. NestedBegins başlangıç/bitiş deltası sıfırdır.
41. Aynı düşman/yaşam/transaction için duplicate apply oluşmaz.
42. Ölü hedefe geç hasar yeni death/kill/gold oluşturmaz.
43. Zırhlı hedef WP-006 direct/explosion sözleşmesini korur.
44. Yeni paralel damage event veya hasar yolu oluşmaz.

Lifecycle:

45. Oyuncu ölümünde bütün Special Weapon durumu temizlenir.
46. Abort sırasında bütün state temizlenir.
47. Yeni koşuda eski pickup, runtime, HUD veya callback bulunmaz.
48. Scene teardown sonrasında statik abonelik kalmaz.
49. Pool’dan yeniden kullanım eski timer veya callback taşımaz.
50. İkinci koşuda event sayısı birinci koşuyla aynı kalır.
51. Round-boundary mevcut davranışı değiştirilmeden karakterize edilir.

Regresyon:

52. WP-002 combat testleri kayıpsızdır.
53. WP-003 round/spawn testleri kayıpsızdır.
54. WP-004 gold/upgrade testleri kayıpsızdır.
55. WP-005 UI/lifecycle testleri kayıpsızdır.
56. WP-006 armor/damage testleri kayıpsızdır.
57. EditMode tam paket geçer.
58. PlayMode gerçek Metal tam paket geçer.
59. Release Player build ve smoke geçer.
60. Development Player SpecialWeaponProbe geçer.
61. RoundProbe, GoldProbe ve ArmorProbe regresyonu geçer.
62. Git F0–F10 ve manifest doğrulaması geçer.

SPECIAL WEAPON PROBE

Gerekirse development-build’e özel:

-ddSpecialWeaponProbe

girişli deterministik bir probe oluştur.

Probe en az şu akışı fiziksel Player’da kanıtlamalıdır:

- uygun round
- schedule
- geçerli progress noktasında spawn
- tek ses/beacon/HUD bildirimi
- yerde bekleme
- pickup
- klasik silahın park edilmesi
- aktif kullanım
- sınırsız mermi/reload’suz çalışma
- çoklu hedef transaction
- zırhlı hedef entegrasyonu
- pause sırasında timer donması
- final-seconds uyarısı
- otomatik bitiş
- aynı build ile klasik silaha dönüş
- ölüm veya teardown temizliği
- ikinci koşuda temiz başlangıç

Probe yalnız development build’de bulunmalı. Release assembly ve Player’da probe sembolleri/argüman davranışı bulunmamalıdır.

BAŞLANGIÇ KAPISI

WP-006 raporuna göre beklenen güncel taban:

- EditMode 388/388
- PlayMode 85/85
- ArmorProbe 9/9
- main ↔ manifest 755/755
- S-10 PASS 12 / FAIL 0 / BLOCKED 0

Bunları gerçek tabanda yeniden doğrula. Sayılar farklıysa rapor ve test kaynaklarından gerçek nedeni belirle; sessizce baseline düşürme.

ÇIKIŞ KAPILARI

WP-007 ancak şu koşulların tamamı sağlandığında tamamlanır:

- REQ-052–059 ve FLOW-005 için kod/test izlenebilirliği vardır.
- Genel Special Weapon yaşam döngüsü silah kimliğinden ayrılmıştır.
- Yerde bekleme ve aktif kullanım süreleri birbirine karışmaz.
- Aynı anda yalnız bir Special Weapon süreci bulunur.
- Parametrik, sıralı ikinci spawn mekanizması kanıtlıdır.
- Spawn yalnız geçerli ve erişilebilir aday konumda gerçekleşir.
- Klasik silah kaybolmaz ve aynı build ile geri döner.
- Special Weapon mermi/reload ve klasik upgrade durumundan izoledir.
- Spawn, pickup, warning ve bitiş olayları tam bir kez üretilir.
- Ölüm, abort, yeni koşu, pooling ve teardown güvenlidir.
- WP-006 DamageTransaction sözleşmesi korunur.
- U-1 ve U-2 canonical karara dönüştürülmemiştir.
- PrototypeOnly silah kimlikleri final içerik olarak sunulmamıştır.
- Hiçbir yeni DEC/REQ veya sayısal ürün kararı eklenmemiştir.
- Bütün otomatik testler geçmiştir.
- Gerçek Player SpecialWeaponProbe geçmiştir.
- Release build ve smoke geçmiştir.
- Player loglarında yeni exception veya shader fallback yoktur.
- S-01 koşulsuz PASS vermiştir.
- S-10 bağımsız PASS vermiştir.
- Git F0–F10 temizdir.
- Kullanıcı profil verisi korunmuştur.
- Final manifest commit ağacıyla birebir eşleşmektedir.

PLANLAMA GÜNCELLEMELERİ

Paket branch’i içinde yalnız gerçekleşen durumu kaydet:

- WP-006 → Tamamlandı + main’e entegre
- M2 → Devam ediyor
- WP-007 → Paket kaydı ve gerçekleşen durum
- planning/work-packages/WP-007-M2-SPECIAL-WEAPON-LIFECYCLE.md → kullanıcıya verilen bu ana promptun eksiksiz kaydı

Gerekli dosyalar:

- planning/03-ROADMAP.md
- planning/04-IMPLEMENTATION-PLAN.md
- planning/work-packages/WP-007-M2-SPECIAL-WEAPON-LIFECYCLE.md

Yeni ürün kararı ekleme. Prototip değerlerini plan belgelerine final değer olarak yazma.

AP-001 environment üretim alanına veya raporlarına dokunma.

KAPSAM DIŞI

- Nihai Special Weapon türü seçimi
- Nihai Special Weapon adları
- Kagura Bell, Ofuda Loom veya Tide Jewel’in canonical kabulü
- Yeni Special Weapon tasarlama
- Final saldırı/hasar biçimi
- U-1 veya U-2 kararı
- Nihai sayısal denge
- Nihai weapon model, rig veya animasyon
- Final VFX
- Final ses
- Final UI sanatı
- Higgsfield, Adobe veya Blender üretimi
- Special/Elite/Ultra düşmanlar
- Round formülü veya düşman dağılımı
- DEC-021 armor/tier override
- DEC-043 armor/tier gold değerlerinin etkinleştirilmesi
- DEC-048 round başına tek satın alma
- DEC-050 ücretli reroll
- Profil XP eğrisi
- Achievement/unlock içeriği
- B3, B4 veya B5
- REQ-123 tuning
- Sınıf-3 A–H temizliği
- Unity/URP veya package değişikliği
- Remote oluşturma veya push
- AP-001 çıktılarının Unity’ye alınması

GİT GÜVENLİĞİ

- Tek writer ve commit owner S-02’dir.
- Açık dosya sahipliği olmadan diğer ajanlar yazma yapamaz.
- `git add -A` kullanma; stage allowlist kullan.
- Commit öncesi stage listesi ile dirty-state birebir eşleşmelidir.
- `.meta` eşleri ve GUID’ler doğrulanmalıdır.
- Generated, Library, Temp, Logs, build çıktısı, secret veya `.claude` commit edilmemelidir.
- Amend, reset, clean, rebase ve force push yasaktır.
- Eski WP worktree’lerini temizleme.
- Kullanıcı dosyalarını silme veya geri alma.
- Tek atomik package commit üret.
- S-01 ve S-10 kapıları tamamlanınca C-00 mevcut süreçle `--ff-only` entegrasyonu yönetir.
- Merge commit oluşturma.
- Remote veya push yapma.
- Entegrasyon sonrasında önerilen canonical etiket:

wp-007-m2-special-weapon

NİHAİ RAPOR

C-00’a gönderilecek birleşik rapor en az şunları içermelidir:

1. Başlangıç ve final HEAD
2. Branch/worktree
3. Katılan gerçek KADRO ajanları ve görevleri
4. Prompt uzmanlarının hazırladığı alt promptlar ve QA sonuçları
5. İncelenen mevcut Special Weapon mimarisi
6. Korunan, değiştirilen ve PrototypeOnly bırakılan davranışlar
7. REQ/FLOW → kod → test matrisi
8. Yaşam döngüsü state modeli
9. Spawn uygunluk ve zamanlama kanıtı
10. Spawn konumu doğrulama kanıtı
11. Tekillik ve ikinci spawn kanıtı
12. Ground/active timer ayrımı
13. Klasik silah park/geri dönüş kanıtı
14. Upgrade izolasyonu
15. DamageTransaction ve armor entegrasyonu
16. U-1/U-2’nin değiştirilmediği ve canonical yapılmadığı kanıtı
17. Lifecycle/pooling/ikinci koşu kanıtı
18. Performance ve allocation sonuçları
19. EditMode/PlayMode test toplamları
20. SpecialWeaponProbe sonucu
21. RoundProbe, GoldProbe ve ArmorProbe regresyonu
22. Release build ve Player smoke
23. S-01 kararı
24. S-10 kararı
25. Git F0–F10
26. Manifest/hash
27. Kullanıcı verisi korunumu
28. Commit ve etiket
29. Main fast-forward entegrasyon kaydı
30. Kapsam dışında bırakılan ve karar bekleyen konular
31. Sonraki paket için yalnız teknik bağımlılık notları

ÇALIŞMA DAVRANIŞI

Ara rapor teslim sayılmaz.

Normal teknik sorunlarda kullanıcıdan onay isteme. Onaylı sınırlar içinde incele, uygula, test et, düzelt ve yeniden test et.

Yalnız şu durumlarda durup C-00 üzerinden kullanıcı kararına yükselt:

- U-1 veya U-2 davranışını değiştirme gereksinimi
- Round sınırındaki Special Weapon davranışını değiştirme gereksinimi
- Onaylı kararlarda gerçek çelişki
- Yeni Special Weapon türü veya hasar davranışı zorunluluğu
- Unity/URP sürüm değişikliği
- Veri kaybı veya destructive Git riski
- Kapsamı esaslı biçimde genişletme
- Ücretli servis veya lisans gereksinimi
- Geri döndürülemez işlem

Bunların dışında kabul ölçütleri tamamen sağlanana kadar çalışmayı kesintisiz sürdür.

Şimdi C-00 bağlantısını doğrula, taban denetimini yap, KADRO prompt uzmanlarına alt görev promptlarını hazırlat ve WP-007’yi başlat.
