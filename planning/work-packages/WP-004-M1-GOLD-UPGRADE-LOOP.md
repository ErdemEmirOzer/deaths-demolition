# WP-004 — M1 Gold & Round Upgrade Loop

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-12'de `S-04 — Economy, Progression & Save` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-004-m1-gold-upgrade-loop` branch'inde (taban: `main` = `507c2176` = `wp-003-m1-rounds`) yapılır; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır.

---

# WP-004 — M1 Gold & Round Upgrade Loop

## Görev sahibi

Ana konu oturumu:
S-04 — Economy, Progression & Save

Ana koordinasyon:
C-00

Teknik entegrasyon danışmanı:
S-01

Round/spawn sözleşmesi danışmanı:
S-03

Savaş ve klasik silah sözleşmesi danışmanı:
S-02

İşlevsel UI danışmanı:
S-09

Bağımsız kabul ve kalite doğrulaması:
S-10

Kaynak proje:
/Users/Erdem/Documents/Deaths Demolition

Başlangıç noktası:
main commit `507c2176…`

Başlangıç etiketi:
`wp-003-m1-rounds`

Bu paket WP-000, WP-001, WP-002 ve WP-003 üzerine kurulacaktır.

## Ana hedef

Mevcut prototipteki gold ve round sonu geliştirme sistemini, M1 dikey kesitinin uçtan uca çalışmasını sağlayan güvenilir bir üretim dilimine dönüştür.

Paket tamamlandığında:

- Öldürülen her geçerli düşman tam olarak bir fiziksel gold paketi düşürmeli.
- Gold yalnız düşman öldürme sonucunda üretilmeli.
- Gold paketi dünya üzerinde görünür ve toplanabilir olmalı.
- Oyuncu yaklaştığında gold mıknatıs davranışıyla oyuncuya yönelmeli.
- Yerde bekleyen gold normal round akışı sırasında kendiliğinden kaybolmamalı.
- Round tamamlandığında yerde kalan bütün goldlar otomatik olarak toplanmalı.
- Otomatik toplama, round sonu geliştirme ekranındaki kullanılabilir bakiyeye dahil olmalı.
- Round sonu geliştirme ekranı güvenilir biçimde açılmalı.
- Oyuncu mevcut klasik silahı için koşuya özel geliştirmeler satın alabilmeli.
- Gold ve satın alınan geliştirmeler aynı koşunun roundları arasında korunmalı.
- Oyuncu öldüğünde gold, silah geliştirmeleri ve koşu build'i sıfırlanmalı.
- Gold kazanmak veya harcamak profil XP'si üretmemeli.
- Sistem daha sonra eklenecek düşman varyantları, zırhlar ve geliştirme dalları için genişletilebilir olmalı.
- Hiçbir yeni ürün veya sayısal denge kararı alınmamalı.

Bu paket nihai ekonomi dengesi veya final UI paketi değildir. Amacı M1 için güvenilir, oynanabilir ve test edilmiş gold → geliştirme → sonraki round döngüsünü oluşturmaktır.

## Kesintisiz çalışma kuralı

Görev başladıktan sonra bütün kabul kriterleri sağlanıncaya kadar çalışmaya devam et.

Normal teknik ayrıntılar için kullanıcıdan onay isteme.

Bir test başarısız olduğunda:

1. Kök nedeni belirle.
2. En küçük güvenli düzeltmeyi uygula.
3. İlgili testi tekrar çalıştır.
4. Bütün regresyon paketini yeniden çalıştır.
5. Sonuç temizleninceye kadar devam et.

Yalnız şu durumlarda C-00 üzerinden kullanıcıya dön:

- Onaylanmamış bir ürün davranışı seçilmeden güvenli biçimde ilerlenemiyorsa
- Mevcut kararlar birbiriyle gerçekten çelişiyorsa
- Veri kaybı veya destructive işlem gerekiyorsa
- Ücretli hizmet, harici hesap veya gizli bilgi gerekiyorsa
- Bütün güvenli alternatifler denendiği hâlde gerçek bir teknik engel çözülemiyorsa

## Kadro kullanımı

Görev için gereken uzman sayısını gerçek ihtiyaca göre belirle.

En az şu uzmanlıkları değerlendir:

- Run-economy engineer
- Reward/drop-system engineer
- Upgrade-system engineer
- Classic-weapon integration engineer
- Unity UI integration specialist
- Run-state and lifecycle specialist
- Deterministic testing specialist
- PlayMode test specialist
- Performance specialist
- Git/repository safety specialist
- S-01 integration reviewer
- S-10 independent QA reviewer

Ana üretim dosyaları için tek bir writer/entegrasyon sahibi belirle.

S-02, S-03 ve S-09 danışmanları varsayılan olarak salt okunur çalışmalı; açık bir dosya devri yapılmadıkça ortak dosyalara yazmamalıdır.

## Değiştirilemeyecek onaylanmış kararlar

### Gold kaynağı

- Her düşman öldüğünde gold düşürür.
- Her düşman tam olarak bir fiziksel gold paketi düşürür.
- Gold yalnız düşman öldürmekten kazanılır.
- Hasar vermek gold üretmez.
- Round tamamlamak ayrıca gold üretmez.
- Oynama süresi gold üretmez.
- Gold harcamak veya kazanmak profil XP'si üretmez.

### Gold paket değeri

Uzun vadeli değer sözleşmesi:

- Temel düşman değeri
- Düşman versiyonu katkısı
- Düşmanın zırh durumunun katkısı

üzerinden genişletilebilir.

WP-004 M1 kapsamında yalnız Standard ve zırhsız zombie bulunduğu için yalnız mevcut temel prototip değeri kullanılacaktır.

Special, Elite, Ultra veya armor için ürün değeri belirleme. Yalnız veri modelinin ileride bu katkıları kabul edebilmesini sağla.

### Fiziksel pickup

- Gold paketleri düşmanın öldüğü dünya konumunda oluşur.
- Paket fiziksel olarak dünya üzerinde görülebilir.
- Oyuncu yaklaştığında mıknatıs davranışıyla oyuncuya doğru hareket eder.
- Oyuncuya ulaştığında bakiye tam olarak bir kez artar.
- Aynı paket birden fazla kez toplanamaz.
- Toplanan paket dünyadan güvenli biçimde kaldırılır veya pool'a döner.

### Yerde kalma ve round sonu toplama

- Yerdeki gold normal round sırasında süre dolduğu için kaybolmaz.
- Round tamamlandığında yerde kalan bütün goldlar otomatik olarak toplanır.
- Otomatik toplama sırasında oyuncunun paketlere fiziksel olarak yürümesi gerekmez.
- Otomatik toplanan değer round sonu geliştirme ekranı açılmadan önce bakiyeye yansır.
- Round tamamlanması ile otomatik toplama aynı paket için birden fazla gelir üretmez.

### Koşu ekonomisi

- Gold yalnız mevcut koşu boyunca korunur.
- Roundlar arasında taşınır.
- Oyuncu öldüğünde sıfırlanır.
- Yeni koşu önceki koşudan gold devralmaz.
- Gold kalıcı profil para birimine dönüşmez.
- Başlangıç gold'u profil seviyesinden alınmaz.

### Koşu build'i

- Oyuncu koşunun başında bir klasik silah seçer.
- Aynı klasik silah koşu boyunca korunur.
- Round sonu geliştirmeleri bu klasik silahı farklı geliştirme yönleriyle dönüştürür.
- Oyuncunun o koşuya özgü belirgin bir build oluşturabilmesi desteklenir.
- Satın alınan geliştirmeler aynı koşunun sonraki roundlarında korunur.
- Oyuncu öldüğünde build ve silah geliştirmeleri sıfırlanır.
- Kalıcı profil seviyesi doğrudan hasar, sağlık veya başlangıç gold'u sağlamaz.
- Gelecekteki Special Weapon sistemi round sonu geliştirme ağacından etkilenmeyecektir.

WP-004 kapsamında Special Weapon uygulama.

## WP-003 entegrasyon sözleşmesi

WP-003 tarafından sağlanan şu noktaları kullan:

- `RoundCleared`
- `LastCompletedRound`
- Doğru yaşayan düşman sayısı
- Idempotent round completion
- Round sonunda donmuş dünya
- Round tamamlandığında yerde kalabilen cesetler
- Oyuncu ölümünde `Aborted` round durumu

Round sistemini atlayan ikinci bir round sonu tespit sistemi oluşturma.

Her kare:

- Yaşayan düşman arama
- Havuz sorgulama
- Bütün sahneyi tarama
- Round bitişi tahmin etme

gibi paralel bir completion sistemi kurma.

Önerilen güvenilir olay sırası:

1. Son yaşayan düşman ölür.
2. Düşmanın gold paketi oluşturulur.
3. WP-003 round ledger'ı roundu tamamlar.
4. `RoundCleared` tam olarak bir kez yayınlanır.
5. Yerdeki alınmamış gold paketleri otomatik toplanır.
6. Run wallet güncellenir.
7. Round sonu ekonomi/build snapshot'ı oluşturulur.
8. Geliştirme ekranı açılır.
9. Oyuncu mevcut prototip kurallarına göre alışverişini tamamlar.
10. Sonraki round isteği güvenli biçimde iletilir.

Bu sıralama aynı karede gerçekleşen olaylarda deterministik olmalıdır.

Round sonu ekranı açılmadan önce bütün geçerli round gold'u bakiyeye dahil edilmiş olmalıdır.

## Ölüm ve round bitişi yarışı

Oyuncunun ölümü ile son düşmanın ölümü aynı zaman aralığında gerçekleşebilir.

Mevcut `RunDirector`, `RoundLedger` ve lifecycle sözleşmelerini incele.

- Mevcut sistemde açık ve test edilmiş bir öncelik varsa onu koru.
- Aynı run için hem ölüm sonuç akışı hem upgrade akışı açılmasına izin verme.
- Run sona ermişse gecikmiş callback yeni bir upgrade ekranı açmamalı.
- Run sona erdikten sonra gold veya geliştirme yeni koşuya sızmamalı.
- Aynı event iki kez gelir üretmemeli.

Mevcut kodda sonuç belirsizse kendi başına yeni ürün önceliği oluşturma. Durumu teknik olarak güvenli ve idempotent hâle getir; hangi akışın mevcut authoritative lifecycle tarafından seçildiğini raporla.

## Gold reward sözleşmesi

Gold üretimini düşman görsel efekti içine gömme.

Önerilen sorumluluk ayrımı:

- Enemy death doğrulaması
- Reward eligibility
- Reward value calculation
- Physical packet spawn
- Pickup ownership/state
- Wallet transaction
- Round-clear collection
- Run reset

Mevcut mimariye uygun adlandırma kullan.

Bir düşman gold üretmeye yalnızca şu durumda hak kazanır:

- Geçerli bir run içinde bulunuyorsa
- Geçerli düşman olarak kayıtlıysa
- Alive durumundan geçerli death durumuna geçtiyse
- Ödülü daha önce üretilmediyse

Şunlar gold üretmemelidir:

- Aynı düşmana gelen ikinci death callback'i
- Cleanup/despawn
- Sahne unload
- Run abort cleanup
- Pool'a iade
- Initialize başarısızlığı
- Editör veya test amaçlı doğrudan destroy
- Önceki koşudan kalan gecikmiş event

Reward işlemi idempotent olmalıdır.

## Gold paket durumu

Her gold paketi için açık ve test edilebilir bir durum modeli oluştur.

Örnek sorumluluk durumları:

- Spawned
- Waiting
- Magnetizing
- Collected
- AutoCollected
- Recycled/Removed

İsimleri mevcut kod standardına uyarlayabilirsin.

Şu garantileri sağla:

- `Collected` veya `AutoCollected` yalnız bir kez wallet işlemi üretir.
- Manuel pickup ile round sonu auto-collect yarışı duplicate gelir oluşturmaz.
- Paket pool'a dönmeden önce sahiplik işlemi tamamlanır.
- Eski paketin callback'i yeni pool kullanımını etkileyemez.
- Paket yeni koşuya taşınmaz.
- Pause durumunda normal magnet hareketi ilerlemez.
- Round-clear auto-collect pause veya timescale nedeniyle kilitlenmez.
- Ceset collider'ları gold paketini ulaşılamaz hâle getirmez.
- Dünya donduğunda auto-collect fiziksel animasyon tamamlanmasını beklemek zorunda değildir.

Round sonunda görsel toplama animasyonu kullanılabilir; fakat wallet doğruluğu animasyona bağımlı olmamalıdır.

## Wallet ve run-state sözleşmesi

Wallet, koşuya ait authoritative bakiye kaynağı olmalıdır.

Asgari işlemler:

- InitializeForRun
- Credit
- TrySpend
- GetBalance veya immutable snapshot
- ResetForRunEnd
- ResetForNewRun

İsimleri mevcut mimariye göre düzenle.

Garantiler:

- Negatif bakiye oluşmaz.
- Aynı transaction iki kez uygulanmaz.
- Yetersiz bakiye satın alma gerçekleştirmez.
- Başarısız satın alma gold eksiltmez.
- Başarılı satın alma maliyeti tam olarak bir kez düşer.
- Wallet değişimi UI'a typed event veya snapshot ile ulaşır.
- UI kendi ayrı authoritative bakiye kopyasını tutmaz.
- Round değişimi wallet'ı sıfırlamaz.
- Run ölümü wallet'ı sıfırlar.
- Yeni run varsayılan koşu bakiyesiyle başlar.
- Profil save dosyasına run gold'u kalıcı ekonomi olarak yazılmaz.

Wallet transaction'larında neden/source bilgisi tut:

- EnemyPickup
- RoundClearAutoCollect
- UpgradePurchase
- RunInitialization
- RunReset

Yeni ürün kaynağı ekleme.

## Upgrade sistemi

Önce mevcut prototipteki upgrade yapılarını, tanımlarını, maliyetlerini, etkilerini, UI bağlantılarını ve run-state davranışını incele.

Mevcut çalışan sistemi gereksiz yere yeniden yazma.

Upgrade sistemi veri odaklı ve genişletilebilir olmalıdır.

Bir upgrade tanımı en az şu kavramları destekleyebilmelidir:

- Stable ID
- İngilizce görünen ad
- İngilizce açıklama
- Maliyet
- İlgili klasik silah
- Geliştirme kategorisi veya dal kimliği
- Mevcut rank
- Max rank
- Varsa prerequisite referansı
- Etki tanımı
- UI ikonu için referans alanı
- PrototypeOnly metadata

Ancak henüz onaylanmamış:

- Nihai geliştirme dalları
- Nihai seçenek sayısı
- Nihai rarity sistemi
- Nihai maliyetler
- Nihai max-rank değerleri
- Nihai prerequisite ağacı
- Reroll
- Lock
- Ban
- Skip zorunluluğu
- Upgrade kombinasyonları
- Sinergi bonusları

oluşturma.

Mevcut prototipte bunların çalışan bir karşılığı varsa yalnız M1'i çalıştırmak için koru ve `PrototypeOnly` say.

## Upgrade satın alma işlemi

Satın alma atomik olmalıdır.

İşlem sırası:

1. Upgrade tanımının geçerli olduğunu doğrula.
2. Teklifin mevcut round ekranına ait olduğunu doğrula.
3. Klasik silahla uyumunu doğrula.
4. Max-rank/prerequisite gibi mevcut prototip kurallarını doğrula.
5. Wallet bakiyesini doğrula.
6. Gold'u tam olarak bir kez harca.
7. Upgrade rank'ını tam olarak bir kez artır.
8. Etkiyi klasik silaha tam olarak bir kez uygula.
9. Build snapshot'ını güncelle.
10. UI'ı authoritative snapshot ile yenile.

Başarısız işlemlerde yarım durum bırakma.

Şunlara izin verme:

- Gold düşüp upgrade uygulanmaması
- Upgrade uygulanıp gold düşmemesi
- Çift tıklamayla iki kez işlem
- Eski ekran butonuyla yeni round satın alması
- Kapanan ekranın gecikmiş callback'i
- Yeni koşuya önceki build etkisinin sızması
- UI yeniden çizildiğinde aynı etkinin tekrar uygulanması
- Aynı rank'ın iki ayrı authoritative yerde tutulması

## Klasik silah entegrasyonu

WP-002'de doğrulanan mevcut klasik silah davranışını koru:

- Hareket ve nişan bağımsızlığı
- Otomatik ateş
- Namlu-önü güvenliği
- Pause davranışı
- Reload/dry-fire sözleşmesi
- Hit feedback sahipliği
- Konumsal ses
- Oyuncu ölümü

M1'de mevcut klasik silah ve mevcut prototip upgrade etkileri kullanılabilir.

Upgrade etkilerinin gerçekten oynanışta görülebilir olduğu en az bir uçtan uca testle kanıtlanmalıdır.

Ancak şu değerleri kalıcı denge olarak değiştirme:

- Damage
- Fire rate
- Magazine
- Reload duration
- Spread
- Projectile count
- Penetration
- Ricochet
- Explosion
- Chain effect
- Knockback
- Stagger

Mevcut değerleri yalnız PrototypeOnly olarak koru.

## Build state

Koşuya ait build ayrı ve authoritative bir state olmalıdır.

Takip edilecek asgari bilgiler:

- Seçili klasik silah
- Satın alınan upgrade ID'leri
- Her upgrade'in rank'ı
- Aktif etki snapshot'ı
- Harcanan toplam run gold'u
- Mevcut run kimliği
- Son güncellenen round

Build state:

- Roundlar arasında korunmalı.
- Ölümde sıfırlanmalı.
- Yeni run'da temiz başlamalı.
- Profil seviyesine doğrudan güç yazmamalı.
- Special Weapon sistemine uygulanmamalı.
- UI kapatılıp açıldığında iki kez etki uygulamamalı.

WP-002 teknik notunda belirtilen `DescribeBuild` veya eşdeğer build açıklaması her kare yeniden string üretmemelidir.

Build özeti:

- State değiştiğinde güncellenmeli
- Gerektiğinde cache edilmeli
- UI tarafından kare başına yeniden oluşturulmamalı

## Round sonu geliştirme ekranı

Bu paket final S-09 görsel tasarım paketi değildir; ancak M1 için çalışan, düzenli ve kaliteli bir işlevsel ekran oluşturmalıdır.

Ekran:

- Round tamamlandıktan sonra açılmalı.
- Dünya simülasyonu donmuş kalmalı.
- Oyuncuya saldırı veya yeni spawn gerçekleşmemeli.
- Güncel gold bakiyesini göstermeli.
- Mevcut prototip upgrade seçeneklerini göstermeli.
- Upgrade adlarını ve açıklamalarını İngilizce göstermeli.
- Maliyeti göstermeli.
- Satın alınabilir/satın alınamaz durumunu açıkça göstermeli.
- Satın alma sonrasında bakiye ve build durumunu anında yenilemeli.
- Klavye-fare ile kullanılabilmeli.
- Gamepad navigation altyapısını bozmamalı.
- Eski round snapshot'ını yeni roundda göstermemeli.
- Aynı round için iki kez açılmamalı.
- Run sona erdiyse açılmamalı.
- Sonraki round isteğini yalnız geçerli ekran oturumu üzerinden göndermeli.

Mevcut prototipin:

- Seçenek sayısı
- Satın alma sayısı
- Devam etme
- Atlatma
- Yenileme
- Ekran kapatma

davranışlarını incele ve M1 için çalışan mevcut sözleşmeyi koru. Onaylanmamış yeni kural ekleme.

UI kalitesini ihmal etme; fakat final görsel redesign, final animasyon ve final asset üretimi WP-005/S-09 kapsamındadır.

## İngilizce dil şartı

Oyunun bütün kullanıcıya görünen metinleri İngilizce olacaktır.

WP-004 kapsamında eklenen bütün kullanıcıya görünen metinler İngilizce yazılmalıdır.

Kod içi açıklamalar ve teknik belgeler proje standardında kalabilir.

## Profil ve XP sınırı

Bu pakette profil XP sistemi geliştirilmemelidir.

Kesin sözleşme:

- Gold kazanmak profil XP'si vermez.
- Gold toplamak profil XP'si vermez.
- Gold harcamak profil XP'si vermez.
- Upgrade satın almak profil XP'si vermez.
- Oynama süresi profil XP'si vermez.
- Profil seviyesi başlangıç gold'u vermez.
- Profil seviyesi doğrudan silah gücü vermez.

Gold ve upgrade olaylarının mevcut XP akışına yanlışlıkla event göndermediğini test et.

## PrototypeOnly değer politikası

Aşağıdaki alanlar henüz nihai karar değildir:

- Standard enemy gold değeri
- Variant gold bonusları
- Armor gold bonusları
- Magnet radius
- Magnet speed
- Pickup completion distance
- Upgrade maliyetleri
- Upgrade değerleri
- Upgrade rank limitleri
- Upgrade teklif sayısı
- Geliştirme ağacı yapısı
- Upgrade rarity
- Upgrade randomization
- Round sonu ekran süresi
- Sonraki round geçiş süresi

Mevcut prototip değerleri kullanılabilir.

Yeni geçici değer gerekiyorsa:

- Açıkça `PrototypeOnly` olarak işaretle.
- Nihai ürün kararı gibi belgeye yazma.
- Yeni DEC veya REQ oluşturma.
- Paket raporunda listele.
- Mevcut `[PrototypeOnly(...)]` standardını kullan.
- PrototypeOnly sayımının sebepsiz azalmasına izin verme.

## Paket kapsamı

Bu pakette yapılacaklar:

- Mevcut gold sisteminin incelenmesi
- Mevcut upgrade sisteminin incelenmesi
- Enemy death → reward sözleşmesi
- Her düşman için tek fiziksel gold paketi
- Gold değer hesaplama genişleme noktası
- Gold packet lifecycle
- Magnet pickup
- Manual pickup transaction
- Round-clear auto-collect
- Run wallet
- Wallet snapshot/event
- Run boyunca gold korunması
- Ölüm ve yeni koşuda gold reset
- Upgrade definition doğrulaması
- Upgrade purchase transaction
- Run build state
- Mevcut klasik silaha upgrade uygulama
- Roundlar arasında build korunması
- Ölüm ve yeni koşuda build reset
- Round sonu işlevsel upgrade ekranı
- RoundCleared entegrasyonu
- Sonraki rounda güvenli geçiş
- İngilizce kullanıcı metinleri
- `DescribeBuild` veya eşdeğer cache/performance düzeltmesi
- EditMode testleri
- PlayMode testleri
- Manuel fiziksel testler
- Performance ölçümü
- Clean Library build
- Player smoke
- İzlenebilirlik belgeleri
- Yalnız gerçekleşmiş WP durum güncellemeleri

## Kesinlikle kapsam dışında

Bu pakette aşağıdakileri uygulama:

- Profil XP hesaplaması
- Profil level curve
- Kalıcı achievement sistemi
- Kalıcı unlock sistemi
- Final Results ekranı
- Final Profile ekranı
- Final Statistics ekranı
- Final UI redesign
- Final UI animation
- Armor
- Bölgesel hasar
- Explosion armor sözleşmesi
- Special enemy
- Elite enemy
- Ultra enemy
- Special Weapon
- Yeni klasik silahlar
- Nihai upgrade dalları
- Nihai ekonomi dengesi
- Nihai upgrade dengesi
- Nihai round dengesi
- Loot crate
- Chest
- Shop reroll
- Shop lock
- Shop ban
- Kalıcı para birimi
- Başlangıç gold bonusu
- Battle pass
- Daily reward
- Online ekonomi
- Cloud save
- Leaderboard
- Netcode
- Final Japon haritası
- Final model, animasyon, VFX veya ses
- B3/B4/B5 kararları
- Sınıf-3 A–H kararlarını kalıcılaştırma
- REQ-068 istisnası
- REQ-071 tier debut kapanışı
- DEC-021 override kapsamını genişletme
- REQ-123 değerlerini değiştirme
- Remote/push
- Yayınlama

Kapsam dışı bir konu WP-004'ü gerçekten engelliyorsa C-00'a bildir. Kendiliğinden kapsam genişletme.

## Zorunlu EditMode testleri

En az aşağıdaki sözleşmeleri kapsa:

### Reward eligibility

- Geçerli Standard enemy ölümü bir reward üretir.
- Her düşman tam olarak bir gold paketi üretir.
- Duplicate death callback ikinci paket üretmez.
- Despawn gold üretmez.
- Cleanup gold üretmez.
- Abort cleanup gold üretmez.
- Initialize başarısızlığı gold üretmez.
- Önceki run event'i yeni run gold'u üretmez.
- Hasar verme gold üretmez.
- Round completion ayrıca gold üretmez.
- Gold olayı XP olayı üretmez.

### Reward value

- Standard/zırhsız düşman mevcut temel PrototypeOnly değeri üretir.
- Negatif gold değeri kabul edilmez.
- Gelecekteki variant/armor katkısı aynı değeri iki kez ekleyemez.
- M1'de bulunmayan variant/armor için ürün değeri uydurulmaz.

### Gold packet lifecycle

- Packet yalnız bir kez initialize edilir.
- Manuel collect yalnız bir wallet credit üretir.
- Auto-collect yalnız bir wallet credit üretir.
- Manuel collect/auto-collect yarışı duplicate credit üretmez.
- Collected packet tekrar toplanamaz.
- Pool reuse eski callback'i yeni packet'a taşımaz.
- Paket normal round sırasında süreyle despawn olmaz.
- Pause sırasında magnet clock ilerlemez.
- Round-clear collection timescale sıfırken tamamlanabilir.
- Run reset bütün kalan packet kayıtlarını temizler.

### Wallet

- Credit bakiyeyi doğru artırır.
- Negatif credit reddedilir.
- `TrySpend` yeterli bakiyede başarılıdır.
- `TrySpend` yetersiz bakiyede state değiştirmez.
- Aynı transaction ID iki kez uygulanmaz.
- Başarılı satın alma maliyeti bir kez düşer.
- Round değişimi wallet'ı korur.
- Ölüm wallet'ı sıfırlar.
- Yeni run wallet'ı sıfırlar.
- Profil seviyesi wallet'a başlangıç bonusu eklemez.
- Wallet UI snapshot'ı authoritative state ile eşleşir.

### Upgrade purchase

- Geçerli satın alma gold'u bir kez düşer.
- Geçerli satın alma rank'ı bir kez artırır.
- Etki klasik silaha bir kez uygulanır.
- Çift tıklama duplicate satın alma üretmez.
- Yetersiz gold upgrade uygulamaz.
- Geçersiz upgrade ID'si state değiştirmez.
- Eski round teklifinden satın alma yapılamaz.
- Max rank mevcut prototip kuralına göre korunur.
- Prerequisite mevcutsa doğrulanır.
- Başarısız işlem yarım wallet/build durumu bırakmaz.
- UI yeniden kurulduğunda etki tekrar uygulanmaz.
- Round değişimi build'i korur.
- Ölüm build'i sıfırlar.
- Yeni run build'i sıfırlar.
- Upgrade profil XP'si üretmez.
- Special Weapon için etki yolu oluşturulmaz.

### Round-clear ordering

- Son kill reward oluşturulmadan auto-collect başlamaz.
- `RoundCleared` bir kez işlendiğinde bütün outstanding paketler reconcile edilir.
- Upgrade snapshot'ı auto-collect sonrasındaki bakiyeyi kullanır.
- Aynı `RoundCleared` event'i ikinci kez auto-collect yapmaz.
- Aborted run upgrade ekranı açmaz.
- Run end sonrası gecikmiş event yeni run state'ini değiştirmez.

## Zorunlu PlayMode testleri

En az şu gerçek yaşam döngülerini doğrula:

- Standard zombie ölünce ölüm konumunda tek görünür gold paketi oluşur.
- Paket oyuncu yaklaşmadan yerde kalır.
- Oyuncu yaklaştığında magnet davranışı başlar.
- Paket oyuncuya ulaşınca bakiye bir kez artar.
- Aynı paket tekrar gelir üretmez.
- Birden fazla kill, düşman başına bir paket üretir.
- Cesetler gold pickup davranışını engellemez.
- Pause sırasında normal magnet hareketi donar.
- Pause sonrası magnet güvenli devam eder.
- Round sonuna kadar alınmayan paketler kaybolmaz.
- Son düşman öldüğünde son paket de auto-collect hesabına girer.
- Round tamamlanınca bütün kalan paketler otomatik toplanır.
- Upgrade ekranı doğru nihai bakiyeyle açılır.
- Upgrade ekranı sırasında yeni spawn oluşmaz.
- Upgrade ekranı sırasında düşman saldırısı veya oyuncu hasarı oluşmaz.
- Satın alma bakiyeyi doğru azaltır.
- Satın alma klasik silahın doğrulanabilir bir PrototypeOnly özelliğini değiştirir.
- Geliştirme sonraki roundda korunur.
- Kalan gold sonraki roundda korunur.
- Oyuncu öldüğünde gold ve build sıfırlanır.
- Yeni koşu temiz wallet ve build ile başlar.
- İkinci koşuda duplicate wallet, pickup handler veya upgrade controller oluşmaz.
- Eski ekran callback'i yeni run'a sızmaz.
- Gold toplamak veya harcamak profil XP'sini değiştirmez.
- Gerçek Metal yürütmesinde bütün testler geçer.

## Manuel fiziksel testler

En az şu kontrolleri gerçekleştir:

1. Bir Standard zombie öldür ve tek gold paketini gör.
2. Paketten uzaklaş ve kendiliğinden kaybolmadığını doğrula.
3. Magnet alanına gir ve paketin akıcı biçimde toplandığını doğrula.
4. Birden fazla düşman öldür ve düşman başına tek paket olduğunu doğrula.
5. Bazı goldları yerde bırak.
6. Roundu tamamla.
7. Yerde kalan goldların otomatik toplandığını doğrula.
8. Upgrade ekranındaki bakiyenin auto-collect değerini içerdiğini doğrula.
9. Satın alınabilir bir upgrade al.
10. Bakiyenin doğru azaldığını doğrula.
11. Upgrade etkisinin klasik silahta görüldüğünü doğrula.
12. Sonraki rounda geç.
13. Gold ve build'in korunduğunu doğrula.
14. Oyuncuyu öldür.
15. Yeni koşuda gold ve build'in sıfırlandığını doğrula.
16. Upgrade ekranında dünya simülasyonunun donduğunu doğrula.
17. Pause sırasında magnet ve ekonomi davranışını doğrula.
18. Klavye-fare ile upgrade ekranını kullan.
19. Gamepad ile mevcut destek düzeyini fiziksel olarak kontrol et.
20. Uygulamayı/oyunu ikinci kez başlat ve duplicate controller olmadığını doğrula.
21. Profil XP'sinin gold işlemlerinden değişmediğini doğrula.

Manuel testlerde profiler, log, screenshot, video veya uygun runtime probe kanıtı oluştur.

## Regresyon zorunluluğu

WP-003 sonundaki taban:

- EditMode 233/233
- PlayMode 56/56

korunmalıdır.

Yeni testler eklendikten sonra:

- Bütün EditMode testleri
- Bütün PlayMode testleri
- Gerçek Metal yürütmesi
- Clean Library import
- Release build
- Player smoke
- RoundProbe regresyonu
- Combat regresyonu
- Lifecycle regresyonu

yeniden çalıştırılmalıdır.

WP-002 veya WP-003 testleri yeni sistemi geçirmek için anlamsız biçimde gevşetilmemelidir.

## Performans kontrolleri

Ölç ve raporla:

- Bir düşman ölümü başına allocation
- Gold packet spawn maliyeti
- Magnet update maliyeti
- Çok sayıda yerde gold varken CPU davranışı
- Round-clear toplu auto-collect maliyeti
- Wallet transaction maliyeti
- Upgrade ekranı açılış maliyeti
- Build snapshot üretimi
- `DescribeBuild` veya eşdeğer metin üretimi
- Round geçişinde cleanup
- İkinci ve sonraki koşularda memory/state davranışı

Şunlardan kaçın:

- Her kare bütün gold paketlerini sahnede arama
- Her kare `DescribeBuild` string oluşturma
- Her kare LINQ allocation
- Her gold için gereksiz ağır coroutine
- Paket başına kontrolsüz material instance
- Round sonunda büyük ani GC
- Auto-collect için yüzlerce bağımsız pahalı event
- UI'nın authoritative wallet state'ini polling ile izlemesi

Mevcut pooling sistemi varsa güvenli biçimde kullan. Yoksa paket sayısı ve profil sonuçlarına göre en küçük uygun çözümü uygula; gereksiz framework kurma.

## Dokümantasyon

Yalnız gerçekleşmiş ve doğrulanmış durumları kaydet:

- WP-003 tamamlandı/entegre durumu
- WP-004 yürütülme/tamamlanma durumu
- Reward sözleşmesi
- Gold packet lifecycle
- Wallet sözleşmesi
- Round-clear auto-collect sırası
- Upgrade purchase sözleşmesi
- Run build state
- Karar → requirement → modül → test matrisi
- PrototypeOnly değerler
- Test sonuçları
- Performans sonuçları
- Ertelenen kararlar
- WP-005 için teknik entegrasyon notları

Yeni ürün kararı oluşturma.

Onay bekleyen önerileri karar dosyasına ekleme.

## Git güvenliği

- Başlangıç commitini ve etiketi doğrula.
- `main` çalışma ağacının temiz olduğunu doğrula.
- WP-004 için izole branch/worktree kullan.
- Tek writer/commit owner belirle.
- Başlangıç manifesti oluştur.
- Açık dosya sahipliği uygula.
- Diğer worktree'lere müdahale etme.
- Kullanıcının mevcut değişikliklerini koru.
- `git add -A` kullanma.
- Dosyaları açıkça stage et.
- Force push yapma.
- Rebase yapma.
- Destructive reset yapma.
- Remote ekleme veya push yapma.
- Library, Temp, Logs, build ve secret dosyalarını commit etme.
- `.meta` ve GUID bütünlüğünü doğrula.
- Testler geçmeden paket commitini oluşturma.
- Atomik ve geri alınabilir commit kullan.
- S-01 ve S-10 kabulü olmadan entegrasyonu başarı sayma.
- Entegrasyon sonrasında main, etiket ve manifesti tekrar doğrula.

Bütün kapılar temizse C-00 mevcut paket zinciriyle fast-forward entegrasyonu koordine edebilir. Push yine yapılmayacaktır.

## S-10 bağımsız kabulü

S-10 yalnız S-04 raporuna güvenmemeli.

Bağımsız olarak şunları doğrula:

- Her geçerli düşman ölümünde tek paket
- Duplicate reward engeli
- Gold'un yalnız kill kaynağından gelmesi
- Paketlerin normal roundda despawn olmaması
- Magnet pickup
- Manuel collect idempotency
- Round-clear auto-collect
- Son kill paketinin auto-collect'e dahil olması
- Wallet doğruluğu
- Upgrade satın alma atomikliği
- Build'in roundlar arasında korunması
- Gold'un roundlar arasında korunması
- Ölüm ve yeni koşuda reset
- Profil XP'sinin etkilenmemesi
- İngilizce kullanıcı metinleri
- WP-002 combat regresyonu
- WP-003 round/spawn regresyonu
- EditMode sonuçları
- PlayMode sonuçları
- Gerçek Metal testleri
- Clean Library import
- Release build
- Player smoke
- Performans ölçümleri
- PrototypeOnly işaretleri
- Git F0–F10
- Manifest bütünlüğü
- Remote/push yapılmaması

Sonuç:

- PASS
- FAIL
- BLOCKED

formatında verilmelidir.

Kanıtsız veya koşullu PASS verme.

## Nihai rapor formatı

C-00'a tek ve eksiksiz rapor sun:

1. Paket kimliği
2. Kaynak commit ve etiket
3. Sonuç commit
4. Branch/worktree
5. Katılan Kadro uzmanları
6. İncelenen mevcut gold/upgrade mimarisi
7. Yapılan üretim değişiklikleri
8. Değiştirilen dosyalar
9. Yeni dosyalar
10. Enemy death → reward sözleşmesi
11. Gold değer sözleşmesi
12. Gold packet lifecycle
13. Magnet davranışı
14. Manual pickup işlemi
15. Round-clear auto-collect sırası
16. Wallet sözleşmesi
17. Upgrade tanım sistemi
18. Upgrade purchase transaction
19. Run build state
20. Klasik silah entegrasyonu
21. Ölüm/yeni koşu reset davranışı
22. Profil XP izolasyonu
23. Round sonu ekran davranışı
24. İngilizce kullanıcı metinleri
25. Yeni EditMode testleri
26. Yeni PlayMode testleri
27. Bütün regresyon sonuçları
28. Manuel fiziksel testler
29. Clean Library import
30. Release build
31. Player smoke
32. Performance ölçümleri
33. PrototypeOnly değerler
34. Bilinen sorunlar
35. Ertelenen kararlar
36. WP-005 teknik bağımlılık notları
37. S-01 değerlendirmesi
38. S-10 sonucu
39. Git F0–F10
40. Manifest/hash sonucu
41. Main/worktree/etiket durumu
42. Remote/push durumu

## Tamamlanma tanımı

WP-004 yalnız şu durumda tamamlanmıştır:

- Her geçerli düşman tam olarak bir fiziksel gold paketi düşürüyor.
- Duplicate ölüm duplicate gold üretmiyor.
- Gold yalnız kill'den kazanılıyor.
- Paketler normal round sırasında kaybolmuyor.
- Magnet pickup güvenilir çalışıyor.
- Manuel pickup bir kez gelir üretiyor.
- Round sonunda bütün alınmamış gold otomatik toplanıyor.
- Son kill'in paketi kaybolmuyor.
- Upgrade ekranı nihai round bakiyesiyle açılıyor.
- Satın alma atomik çalışıyor.
- Upgrade mevcut klasik silaha uygulanıyor.
- Gold ve build roundlar arasında korunuyor.
- Ölüm ve yeni koşu gold/build state'ini sıfırlıyor.
- Gold ve upgrade profil XP'si üretmiyor.
- WP-002 combat sözleşmeleri korunuyor.
- WP-003 round/spawn sözleşmeleri korunuyor.
- Tüm yeni ve mevcut testler geçiyor.
- Gerçek Metal doğrulaması geçiyor.
- Clean Library release build ve Player smoke geçiyor.
- S-01 koşulsuz kabul veriyor.
- S-10 bağımsız PASS veriyor.
- Git ve manifest denetimleri temiz.
- Yeni ürün kararı oluşturulmamış.
- Bütün sayısal değerler PrototypeOnly korunmuş.
- Remote ve push yapılmamış.

Şimdi WP-004'ü başlat. Kritik bir engel oluşmadıkça bütün kabul kriterleri sağlanıncaya kadar kesintisiz çalış.
