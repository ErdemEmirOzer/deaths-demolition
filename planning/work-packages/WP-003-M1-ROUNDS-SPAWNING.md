# WP-003 — M1 Rounds & Dynamic Spawning Slice

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-12'de `S-03 — Enemies, AI, Rounds & Spawning` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-003-m1-rounds-spawning` branch'inde (taban: `main` = `8fd85867` = `wp-002-m1-combat`) yapılır; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır.

---

# WP-003 — M1 Rounds & Dynamic Spawning Slice

## Görev sahibi

Ana konu oturumu:
S-03 — Enemies, AI, Rounds & Spawning

Ana koordinasyon:
C-00

Teknik entegrasyon danışmanı:
S-01

Bağımsız kabul ve kalite doğrulaması:
S-10

Kaynak proje:
/Users/Erdem/Documents/Deaths Demolition

Başlangıç noktası:
main commit `8fd85867…`

Başlangıç etiketi:
`wp-002-m1-combat`

Bu paket WP-002 üzerine kurulacaktır. WP-002'de doğrulanan oyuncu, kamera, nişan, ateş, hasar, ölüm, ceset çarpışması ve geri bildirim sözleşmeleri korunacaktır.

## Ana hedef

Mevcut prototipteki round ve spawn sistemini, M1 dikey kesitinin ihtiyaçlarını karşılayan, test edilebilir ve genişletilebilir bir üretim dilimine dönüştür.

Bu paket tamamlandığında oyun:

- Round 1'i güvenilir biçimde başlatabilmeli.
- Round için belirlenmiş toplam düşman havuzunu yönetebilmeli.
- Düşmanları yalnız tasarımcı tarafından yerleştirilen spawn noktalarından üretmeli.
- Spawn noktalarının kullanım yoğunluğunu oyuncunun haritadaki konumuna göre dinamik biçimde değiştirebilmeli.
- Hedef aktif düşman sayısını korumalı.
- Teknik aktif düşman tavanını hiçbir zaman aşmamalı.
- Round havuzu tükendiğinde yeni spawnları durdurmalı.
- Sahnedeki son yaşayan düşman öldürüldüğünde roundu tamamlamalı.
- Sonraki sistemlerin bağlanabileceği açık round yaşam döngüsü sinyalleri üretmeli.
- Pause, oyuncu ölümü, sahne yaşam döngüsü ve yeniden koşu başlatma davranışlarında güvenli kalmalı.

Bu paketin amacı nihai dengeyi belirlemek değildir. Amaç, onaylanmış round ve dinamik spawn davranışının sağlam teknik omurgasını oluşturmaktır.

## Kesintisiz çalışma kuralı

Görev başladıktan sonra kabul kriterleri sağlanıncaya kadar çalışmaya devam et.

Normal teknik ayrıntılar için kullanıcıdan onay isteme. Kadro içerisinden gereken uzmanları göreve dahil et. Her uzmana somut sorumluluk ve dosya sınırı ver.

Bir test başarısız olursa yalnız raporlayıp durma:

1. Kök nedeni bul.
2. Düzelt.
3. İlgili testi yeniden çalıştır.
4. Regresyon paketini tekrar çalıştır.
5. Sonuç temizleninceye kadar devam et.

Yalnız gerçek bir kritik ürün kararı, destructive işlem, ücretli servis, veri kaybı riski veya güvenli alternatiflerle çözülemeyen engel varsa C-00 üzerinden kullanıcıya dön.

## Kadro kullanımı

Bu çalışma için gereken uzman sayısını görevin gerçek ihtiyacına göre belirle.

En az şu uzmanlıkları değerlendir:

- Round-system gameplay engineer
- Spawn-system engineer
- Enemy lifecycle/AI engineer
- Unity architecture specialist
- Deterministic testing specialist
- PlayMode test specialist
- Performance specialist
- Git/repository safety specialist
- S-01 integration reviewer
- S-10 independent QA reviewer

Ana üretim dosyalarının tek yazarı/entegrasyon sahibi S-03 tarafından açıkça belirlenmelidir.

Danışman ajanlar varsayılan olarak salt okunur çalışmalıdır. Aynı dosyada kontrolsüz paralel yazım yapılmamalıdır.

## Değiştirilemeyecek onaylanmış kararlar

### Round başlangıcı

- Oyuncu koşuya Round 1'den başlar.
- Oyuncu haritanın merkezindeki başlangıç konumunda başlar.
- Her round için belirli bir toplam düşman sayısı/havuzu bulunur.

### Kesintisiz düşman havuzu

- Round başladığında düşmanlar tek seferde topluca spawn edilmez.
- Düşmanlar round boyunca dinamik spawn sistemi üzerinden kesintisiz biçimde gelir.
- Round sistemi hedef aktif düşman sayısını korumaya çalışır.
- Düşman öldükçe, round havuzunda düşman kaldığı sürece yeni düşman getirilebilir.
- Round havuzu tükendiğinde yeni spawn durur.
- Sahnedeki bütün yaşayan düşmanlar öldürüldüğünde round tamamlanır.

### Teknik güvenlik sınırı

- Hedef aktif düşman sayısından ayrı bir teknik aktif düşman tavanı bulunur.
- Spawn sistemi bu teknik tavanı hiçbir koşulda aşamaz.
- Hedef aktif düşman sayısı tempo hedefidir.
- Teknik tavan performans ve güvenlik sınırıdır.
- Bu iki kavram tek değişken hâline getirilmemelidir.

### Spawn noktaları

- Spawn noktalarını tasarımcı harita üzerinde açıkça belirler.
- Düşmanlar yalnız bu sabit spawn noktalarından üretilebilir.
- Sistem çalışma anında rastgele dünya koordinatı oluşturarak düşman spawn etmemelidir.
- NavMesh üzerinde rastgele nokta seçmek, sabit spawn noktalarının yerini almamalıdır.
- Spawn noktaları Inspector veya uygun authoring bileşeni üzerinden yönetilebilmelidir.

### Dinamik yoğunluk

- Bütün spawn noktaları her anda eşit sayıda düşman üretmez.
- Spawn yoğunluğu oyuncunun haritadaki mevcut konumuna göre noktadan noktaya değişir.
- Oyuncuya göre çok uzakta kalan bir spawn noktası belirli durumda hiç seçilmeyebilir.
- Oyuncu hareket ettikçe uygun spawn noktalarının ağırlıkları yeniden değerlendirilebilir.
- Dinamik sistem tasarımcının yerleştirdiği noktaların dışına çıkamaz.

### Round tamamlanması

Bir round yalnız şu iki koşul birlikte sağlandığında tamamlanır:

1. Roundun henüz spawn edilmemiş düşman havuzu tamamen tükenmiştir.
2. Rounda ait yaşayan düşman sayısı sıfırdır.

Yalnız havuzun tükenmesi roundu bitirmez.

Yalnız geçici olarak yaşayan düşman sayısının sıfıra düşmesi de havuzda düşman kaldığı sürece roundu bitirmez.

Ölü düşman cesetleri yaşayan düşman sayısına dahil edilmez.

## Mevcut sistemlerle korunacak sözleşmeler

WP-002'de doğrulanan aşağıdaki davranışları bozma:

- Ölü düşman cesetleri oyuncu hareketini engellemez.
- Ölü düşman cesetleri mermileri kesmez.
- Ölü düşman tekrar hasar olayı üretmez.
- Oyuncu öldüğünde geçerli ölüm snapshot'ı HUD'a ulaşır.
- Pause sırasında savaş davranışları ilerlemez.
- Donmuş simülasyonda reload ve dry-fire davranışı oluşmaz.
- `AimDirection` namlu-önü güvenlik kısıtı korunur.
- İmleç karakter üzerinde veya namlu arkasında olduğunda ters yöne ateş oluşmaz.
- Hit-feedback sahipliği yeniden çoğaltılmaz.
- Konumsal ses sözleşmeleri bozulmaz.
- Oyuncu ölümü sonrası round/spawn sistemi kontrolsüz biçimde düşman üretmeye devam etmez.
- Yeni koşu önceki koşudan yaşayan düşman veya round state'i devralmaz.

Sabit adımlı testlerde kullanılan ölçeksiz saat yollarının gerçek kare ilerlemesi gerektirdiğine ilişkin WP-002 teknik notunu dikkate al.

## Kullanılacak entegrasyon noktası

Round/spawn dilimini mevcut `EnemySpawnDirector` üzerinden geliştir.

Yeni ve paralel ikinci bir spawn yöneticisi oluşturarak mevcut sistemi atlama.

Önce mevcut `EnemySpawnDirector` davranışını, bağımlılıklarını, çağrı zincirini ve test kapsamını incele. Gerekiyorsa sorumluluklarını küçük bileşenlere ayır; ancak gereksiz temiz yeniden yazım yapma.

Mevcut prototip nihai oyuna geliştirilecek ana kod tabanıdır.

## Gerekli sistem sorumlulukları

Tek bir dev sınıf içinde bütün davranışları toplama. Mevcut mimariye uygun olduğu ölçüde aşağıdaki sorumlulukları açık biçimde ayır:

### Round state

Asgari durumlar:

- NotStarted
- Starting
- Active
- PoolDepleted
- Completing
- Completed
- Aborted veya RunEnded ile güvenli bağlantı

İsimleri mevcut kod standardına göre uyarlayabilirsin. Ancak aynı olayın birden fazla kez tetiklenmesini engelle.

### Round population state

Takip edilmesi gereken asgari bilgiler:

- Round numarası
- Roundun toplam düşman havuzu
- Henüz spawn edilmemiş düşman sayısı
- Round boyunca spawn edilen toplam düşman
- Mevcut yaşayan düşman sayısı
- Öldürülen düşman sayısı
- Hedef aktif düşman sayısı
- Teknik aktif düşman tavanı
- Round tamamlanma durumu

Aynı düşman ölümü iki kez sayılmamalıdır.

Spawn edilen fakat başlatılamayan/initialize edilemeyen bir düşman havuzdan sessizce kaybolmamalıdır. Hata davranışı belirlenebilir, test edilebilir ve raporlanabilir olmalıdır.

### Spawn point authoring

Her spawn noktası en az:

- Benzersiz kimlik
- Transform/konum
- Aktif veya devre dışı durumu
- Tasarımcı ağırlığı
- İlgili authoring bilgileri
- Runtime uygunluk durumu

taşıyabilmelidir.

Yeni ürün davranışı yaratmadan güvenli teknik metadata eklenebilir.

### Spawn selection

Seçim sistemi:

- Yalnız kayıtlı ve aktif spawn noktalarını değerlendirmeli.
- Oyuncunun konumunu hesaba katmalı.
- Çok uzak noktaların ağırlığını sıfıra indirebilmeli.
- Uygun noktalar arasından ağırlıklı seçim yapabilmeli.
- Hiç uygun nokta yoksa kontrolsüz hata veya sonsuz döngü üretmemeli.
- Aynı karede teknik tavanı aşan toplu spawn üretmemeli.
- Oyuncu referansı geçici olarak yoksa güvenli davranmalı.
- Pause sırasında seçim/spawn ilerletmemeli.
- Koşu veya round bittikten sonra gecikmiş spawn üretmemeli.

Kesin uzaklık eşikleri ve ağırlık eğrileri ürün kararı değildir. Bunları kalıcı denge olarak kilitleme.

### Enemy lifecycle registration

Düşman yaşam döngüsü round sistemiyle açık bir sözleşme üzerinden haberleşmeli:

- Spawn/registration
- Successful activation
- Alive state
- Death
- Despawn/cleanup
- Run termination cleanup

Polling ile her kare bütün düşmanları aramak yerine olay tabanlı veya mevcut mimariye uygun güvenilir bir kayıt sistemi tercih et.

Ancak mevcut çalışan mimariyi gereksiz yere yeniden kurma.

## Hybrid round scaling hazırlığı

Onaylanmış uzun vadeli sistem:

- Temel round artışı formülle ilerler.
- Belirli roundlarda zırh oranları ve Standard/Special/Elite/Ultra dağılımları elle belirlenen değişimler alabilir.
- Yeni düşman veya varyant belirli bir roundda garantili tanıtılabilir.
- Sonraki roundlarda ağırlıklı havuza katılabilir.

WP-003 M1 kapsamında yalnız Standard zombie kullanılacaktır.

Bununla birlikte mimari daha sonra:

- Formül tabanlı temel artış
- Round bazlı authoring override
- Düşman havuzu ağırlıkları
- Garantili tanıtım
- Aktif düşman hedefi
- Teknik tavan

eklenmesine izin vermelidir.

Special, Elite, Ultra veya armor davranışlarını bu pakette üretme.

Gelecekte kullanılacak alanları gereksiz placeholder sınıflarla doldurma. Yalnız açık genişleme noktaları oluştur.

## PrototypeOnly değer politikası

Aşağıdaki sayısal değerler henüz nihai ürün kararı değildir:

- Round başına toplam düşman sayısı
- Round büyüme formülü
- Hedef aktif düşman sayısı
- Teknik aktif düşman tavanı
- Spawn aralığı
- Spawn burst miktarı
- Spawn uzaklık eşikleri
- Spawn ağırlık eğrileri
- Standard zombie sağlık, hasar ve hız değerleri
- REQ-123'e bağlı tempo değerleri

Mevcut değerler gerekiyorsa kullanılabilir veya yalnız testi mümkün kılmak için geçici değerler belirlenebilir.

Fakat:

- Açıkça `PrototypeOnly` olarak işaretlenmeli.
- Nihai denge kararı gibi belgelendirilmemeli.
- Yeni DEC veya ürün requirement'ı oluşturulmamalı.
- Paket raporunda topluca listelenmeli.
- Mevcut `[PrototypeOnly(...)]` standardıyla uyumlu olmalı.

## REQ-123 gözlem noktası

WP-002 raporunda koşan Standard zombie'ye karşı sürekli AR skorunun:

- Ölçülen değer: 2,33
- Mevcut prototip eşik: 2,4
- Fark: yaklaşık %3

olduğu bildirilmiştir.

Bu paket bu farkı kendi başına denge kararıyla çözmeyecek.

Ancak round/spawn yükü altında:

- Oyuncunun Standard zombielere karşı pratik hayatta kalma kapasitesini
- Aktif düşman hedefinin savaş temposuna etkisini
- Sürü yoğunluğunun sonucu nasıl değiştirdiğini
- Performans düşüşünün test sonucunu etkileyip etkilemediğini

ölç ve raporla.

REQ-123'ü geçmek amacıyla gizlice oyuncu hasarı, zombie sağlığı, zombie hızı veya spawn değerlerini değiştirme.

## Paket kapsamı

Bu pakette yapılacaklar:

- Mevcut round sisteminin incelenmesi
- Mevcut spawn sisteminin incelenmesi
- `EnemySpawnDirector` üzerinden güvenilir entegrasyon
- Round state yönetimi
- Toplam düşman havuzu
- Henüz spawn edilmemiş sayı takibi
- Yaşayan düşman kayıt sistemi
- Hedef aktif düşman sayısı
- Teknik aktif düşman tavanı
- Tasarımcı tarafından belirlenen spawn point sistemi
- Oyuncu konumuna bağlı dinamik ağırlık
- Çok uzak noktaların devre dışı kalabilmesi
- Kesintisiz population replenishment
- Havuz tükenmesi
- Son düşman ölümünde round bitişi
- Pause/ölüm/abort güvenliği
- Yeni koşuda state temizliği
- Genişletilebilir fakat sade round yapılandırması
- Otomatik testler
- Manuel fiziksel testler
- Performans ölçümü
- Dokümantasyon ve izlenebilirlik
- Planning dosyalarında yalnız gerçekleşmiş WP durumu güncellemesi

## Kesinlikle kapsam dışında

Bu pakette aşağıdakileri uygulama:

- Gold drop veya pickup
- Round sonu gold otomatik toplama
- Upgrade ekranı
- Upgrade ekonomisi
- Build sistemi
- Profil XP
- Kalıcı istatistik sistemi genişletmesi
- Achievement sistemi
- Unlock sistemi
- Armor
- Bölgesel hasar
- Patlama hasarı sözleşmesi
- Special enemy
- Elite enemy
- Ultra enemy
- Special Weapon
- Yeni klasik silah
- Nihai silah dengesi
- Nihai round dengesi
- Nihai spawn dengesi
- Nihai Japon haritası
- Final model, animasyon, VFX veya ses
- Final UI/UX tasarımı
- Online sistem
- Netcode
- Leaderboard
- Cloud save
- Remote repository
- Push
- Yayınlama
- B3/B4/B5 kararları
- Sınıf-3 A–H kararlarını kalıcılaştırma
- WP-002'de listelenen bağımsız temizlik adayları
- Ceset siper davranışını değiştirme
- Pompa salvosu veya knockback
- Stagger sistemini yeniden tasarlama
- Telegraph sesi
- Hit-reaction sözleşmesini genişletme
- Çoklu Player profil yazma politikasını değiştirme

Kapsam dışı bir hata WP-003'ü gerçekten engelliyorsa, en küçük güvenli düzeltmeyi öner ve C-00'a bildir. Kendiliğinden kapsam genişletme.

## Round sonu entegrasyon sınırı

WP-004 henüz yapılmadığı için round sonunda nihai gold/upgrade akışını üretme.

WP-003 şunları sağlamalı:

- Round tamamlandı olayı
- Tamamlanan round numarası
- Round sonuç snapshot'ı için gerekli temel savaş verisi
- Bir sonraki sisteme geçiş yapılabilecek açık entegrasyon noktası
- Tekrarlanmayan, idempotent completion olayı

Mevcut prototipte bir upgrade veya geçiş ekranı varsa onu gereksiz yere silme. Ancak WP-004 davranışlarını genişletme veya nihai kabul etme.

## Zorunlu otomatik testler

Mevcut testlere ek olarak aşağıdaki sözleşmeleri kapsa.

### EditMode

- Round state geçişleri geçerlidir.
- Round tamamlanma olayı yalnız bir kez yayınlanır.
- Toplam havuz doğru initialize edilir.
- Spawn ile havuz sayısı doğru azalır.
- Aynı düşman iki kez yaşayan sayıya eklenemez.
- Aynı düşman ölümü iki kez kill veya alive azalması üretmez.
- Havuz sıfır fakat yaşayan düşman varken round bitmez.
- Yaşayan düşman sıfır fakat havuzda düşman varken round bitmez.
- Havuz ve yaşayan sayı sıfır olduğunda round biter.
- Hedef aktif sayı teknik tavanı aşamaz.
- Geçersiz configuration güvenli biçimde reddedilir veya normalize edilir.
- Boş spawn point listesi sonsuz döngü üretmez.
- Devre dışı spawn noktası seçilmez.
- Kayıtlı olmayan dünya koordinatından spawn oluşturulmaz.
- Çok uzak nokta sıfır ağırlık alabilir.
- Oyuncu konumu değiştiğinde ağırlık sonucu değişebilir.
- Bütün ağırlıklar sıfır olduğunda güvenli fallback uygulanır.
- Pause durumunda spawn clock ilerlemez.
- Run end durumunda pending spawn iptal edilir.
- Yeni round önceki roundun yaşayan kayıtlarını taşımaz.
- Yeni run önceki run state'ini taşımaz.
- Spawn initialize başarısızlığı sayaçları bozmaz.
- PrototypeOnly round değerleri işaretlidir.

### PlayMode

- Round 1 gerçek sahnede başlar.
- Oyuncu merkez başlangıç davranışını korur.
- Düşman yalnız authoring spawn noktalarından görünür.
- Aktif düşman sayısı hedefe doğru tamamlanır.
- Teknik tavan gerçek sahnede hiçbir zaman aşılmaz.
- Bir düşman öldüğünde havuz kaldıysa yenisi üretilebilir.
- Havuz bittikten sonra yeni düşman üretilmez.
- Son yaşayan düşman öldürüldüğünde round tamamlanır.
- Cesetler yaşayan düşman sayısına dahil edilmez.
- Cesetler WP-002 hareket/mermi sözleşmesini bozmaz.
- Pause sırasında yeni spawn oluşmaz.
- Pause açılıp kapandığında sayaçlar bozulmaz.
- Oyuncu öldüğünde yeni spawn üretimi durur.
- Ölümden sonra gecikmiş completion/spawn callback'i yeni koşuya sızmaz.
- Yeni koşu temiz round state'iyle başlar.
- Sahne yeniden yüklenince duplicate director oluşmaz.
- Çok uzak spawn noktası uygun durumda kullanılmaz.
- Oyuncu haritada hareket ettiğinde spawn dağılımı değişir.
- Gerçek Metal yürütmesinde testler geçer.

Testleri sahte unit kanıtlarıyla sınırlandırma. Gerçek Unity yaşam döngüsünü ve sahneyi kapsa.

## Regresyon zorunluluğu

WP-002 sonunda bildirilen:

- EditMode 192/192
- PlayMode 48/48

test tabanı korunmalıdır.

Yeni testler eklendikten sonra:

- Tüm EditMode paketi
- Tüm PlayMode paketi
- Temiz Library import
- Release build
- Player smoke
- Manuel fiziksel kontroller

yeniden çalıştırılmalıdır.

Mevcut bir test yalnız yeni davranışı geçirebilmek için anlamsız biçimde gevşetilmemelidir.

Test sabiti değişirse:

- Sevkiyat configuration'ıyla ilişkisi açıklanmalı.
- Değişikliğin ürün dengesi oluşturmadığı kanıtlanmalı.
- Önce/sonra sonucu raporlanmalı.

## Manuel fiziksel kontroller

En az şu manuel kontrolleri gerçekleştir ve kanıtla:

1. Round 1 başlangıcı
2. Oyuncunun merkez başlangıcı
3. Kesintisiz düşman gelişi
4. Spawnların yalnız yerleştirilmiş noktalardan gerçekleşmesi
5. Oyuncu konumu değişince yoğunluk dağılımının değişmesi
6. Çok uzak noktanın kullanılmaması
7. Hedef aktif düşman sayısının korunması
8. Teknik tavanın aşılmaması
9. Havuz tükenince spawnın durması
10. Son düşman ölümünde round bitişi
11. Pause sırasında spawnın tamamen donması
12. Oyuncu ölümünde spawnın durması
13. Yeni koşuda eski düşmanların bulunmaması
14. Cesetlerin oyuncu ve mermi davranışını bozmaması
15. Klavye-fare savaş akışı
16. Gamepad savaş akışı
17. İkinci uygulama/oyun başlangıcında duplicate sistem oluşmaması

Manuel kontroller yalnız "bakıldı" biçiminde raporlanmamalı. Uygun profil, log, ekran görüntüsü, video veya deterministik runtime kanıtı üret.

## Performans kontrolü

Bu paket nihai performans bütçesi belirlemeyecek; ancak teknik tavan ve spawn sistemi için temel ölçüm sağlayacak.

Ölç:

- Spawn anındaki frame spike
- Spawn sırasında allocation
- Aktif düşman sayısı arttığında CPU davranışı
- NavMesh/pathfinding etkisi
- Enemy registration maliyeti
- Düşman ölüm/cleanup maliyeti
- Director Update maliyeti
- Ağırlık hesaplama sıklığı
- Büyük spawn-point listesi davranışı
- Uzun round sonunda bellek/state temizliği

Kare başına:

- Bütün sahnede pahalı object search
- Gereksiz string üretimi
- LINQ allocation
- Yeni koleksiyon oluşturma
- Kontrolsüz log üretimi

olmadığını kontrol et.

WP-002'de belirtilen CODE-08 genel temizlik adayını kapsam dışı tut; ancak WP-003 içinde yeni kare başına string veya allocation üretme.

## Dokümantasyon

Yalnız doğrulanmış uygulama durumunu kaydet.

Güncellenmesi gerekenler:

- WP-002'nin tamamlandığı ve `main`e entegre edildiği gerçek durum
- WP-003'ün yürütülme/tamamlanma durumu
- Round/spawn modül sınırları
- Karar → requirement → modül → test izlenebilirliği
- PrototypeOnly değer listesi
- Test sonuçları
- Performans gözlemleri
- Ertelenen kararlar
- Sonraki WP-004 entegrasyon notları

Yeni oyun tasarım kararı oluşturma.

Kullanıcının onaylamadığı bir öneriyi karar dosyasına ekleme.

## Git güvenliği

- Başlangıç commitini doğrula.
- `main` çalışma ağacının temizliğini doğrula.
- WP-003 için izole branch/worktree oluştur.
- Tek writer/commit owner belirle.
- Worktree başlangıç manifestini al.
- Kullanıcının veya başka paketlerin değişikliklerine dokunma.
- Açık dosya sahipliği uygula.
- `git add -A` kullanma.
- Dosyaları açıkça stage et.
- Force push yapma.
- Rebase yapma.
- Destructive reset yapma.
- Remote ekleme veya push yapma.
- Secret, log, build, Library, Temp veya generated çıktı commit etme.
- `.meta` ve GUID bütünlüğünü kontrol et.
- Commit öncesi tüm test kapılarını geçir.
- Paket commitini branch üzerinde oluştur.
- C-00 koordinasyonu ve S-10 kabulü tamamlanmadan `main` entegrasyonunu nihai kabul etme.
- Entegrasyon biçimini ve sonuç commitini raporla.

## S-10 bağımsız kabulü

S-10 yalnız S-03 raporuna güvenmemeli.

Bağımsız olarak:

- Commit farkını
- Kapsamı
- Karar uyumunu
- PrototypeOnly işaretlerini
- Round sayaçlarını
- Spawn-point sınırını
- Dinamik konum ağırlığını
- Aktif hedefi
- Teknik tavanı
- Round tamamlanmasını
- Pause/ölüm/yeni koşu davranışını
- EditMode sonuçlarını
- PlayMode sonuçlarını
- Clean Library importunu
- Release buildi
- Player smoke testini
- Git F0–F10 güvenlik denetimini
- Manifest bütünlüğünü

doğrulasın.

Sonuç şu formatta olmalıdır:

- PASS
- FAIL
- BLOCKED

Koşullu veya kanıtsız PASS verme.

## Nihai rapor formatı

C-00'a tek ve eksiksiz nihai rapor sun:

1. Paket kimliği
2. Kaynak commit ve etiket
3. Sonuç commit
4. Branch/worktree
5. Katılan Kadro uzmanları ve sorumlulukları
6. İncelenen mevcut round/spawn mimarisi
7. Yapılan üretim değişiklikleri
8. Değiştirilen dosyalar
9. Yeni dosyalar
10. Round state sözleşmesi
11. Havuz ve sayaç sözleşmesi
12. Spawn-point authoring sözleşmesi
13. Dinamik ağırlık davranışı
14. Hedef aktif sayı ve teknik tavan davranışı
15. Pause/ölüm/abort/yeni koşu davranışı
16. WP-002 regresyon sonuçları
17. Yeni EditMode testleri
18. Yeni PlayMode testleri
19. Manuel fiziksel testler
20. Clean Library import
21. Release build
22. Player smoke
23. Performans ölçümleri
24. PrototypeOnly değerler
25. REQ-123 gözlemleri
26. Bilinen sorunlar
27. Ertelenen kararlar
28. WP-004 için teknik bağımlılık notları
29. S-01 değerlendirmesi
30. S-10 bağımsız kapı sonucu
31. Git F0–F10 sonucu
32. Manifest/hash sonucu
33. Repository ve worktree durumu
34. Remote/push durumu

## Tamamlanma tanımı

WP-003 yalnız şu durumda tamamlanmıştır:

- Round 1 güvenilir biçimde başlıyor.
- Toplam round havuzu doğru yönetiliyor.
- Spawnlar yalnız sabit authoring noktalarından yapılıyor.
- Yoğunluk oyuncu konumuna göre dinamik değişiyor.
- Çok uzak nokta sıfır kullanım alabiliyor.
- Hedef aktif düşman sayısı korunuyor.
- Teknik tavan asla aşılmıyor.
- Havuz bitince yeni spawn duruyor.
- Son yaşayan düşman ölünce round yalnız bir kez tamamlanıyor.
- Pause, oyuncu ölümü ve yeni koşu state'i bozmuyor.
- WP-002 sözleşmeleri korunuyor.
- Yeni ve mevcut tüm testler geçiyor.
- Gerçek Metal PlayMode doğrulaması geçiyor.
- Clean Library release build ve Player smoke geçiyor.
- S-01 koşulsuz kabul veriyor.
- S-10 bağımsız PASS veriyor.
- Git ve manifest denetimleri temiz.
- Kullanıcı onayı olmayan yeni ürün kararı oluşturulmamış.
- Remote ve push yapılmamış.

Şimdi WP-003'ü başlat. Kritik bir engel oluşmadıkça kabul kriterleri tamamen sağlanıncaya kadar kesintisiz çalış.
