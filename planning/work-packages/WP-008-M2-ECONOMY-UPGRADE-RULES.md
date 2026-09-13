WP-008 — M2 ECONOMY & ROUND UPGRADE RULES COMPLETION

PAKET SAHİBİ
S-04 — Economy, Progression & Save

ANA KOORDİNASYON
C-00 — Deaths Demolition Main Coordination

KAYIT NOTU
Aşağıdaki metin kullanıcının 2026-09-13 tarihinde S-04'e verdiği yürütme promptunun aynısıdır (paket kaydı; planning/04 §WP-008 durum ve eşleme tablosunu, planning/00 DEC-069..075 ve planning/01 REQ-148..155 kayıtlarını bu prompt üretmiştir).

---

WP-008 — M2 Economy & Round Upgrade Rules Completion

HEDEF OTURUM
S-04 — Economy, Progression & Save

ROL VE YETKİ
Bu paketin uygulama sahibisin. C-00 ana koordinasyon merkezi, S-01 mimari ve entegrasyon denetçisi, S-10 Git/teslim güvenliği sorumlusudur.

Çalışmaya başlamadan önce C-00 ile çift yönlü iletişimi doğrula. Gerekli raporları C-00'a aktar ve paket boyunca koordinasyonu onun üzerinden sürdür.

Kadro sistemini zorunlu olarak kullan. Görevin gerektirdiği sayıda ve türde uzmanı çalışmaya dahil et. Sabit bir ajan sayısıyla sınırlı değilsin. En azından şu uzmanlıkların kapsandığını doğrula:

- Oyun ekonomisi ve run progression
- Unity/C# gameplay mimarisi
- Transactional state ve veri bütünlüğü
- Upgrade/build sistemi
- UI/UX ve erişilebilirlik
- Test otomasyonu ve edge-case analizi
- Performans ve allocation denetimi
- Save/profile uyumluluğu
- Git, worktree, manifest ve entegrasyon güvenliği

S-04 birincil yazma sahibidir. Birden fazla ajanın aynı dosyalara kontrolsüz biçimde yazmasına izin verme. Ek yazma ajanları gerekiyorsa dosya sahipliğini açıkça böl, ayrı worktree kullan ve C-00/S-10 koordinasyonunu koru. Salt okunur inceleme ve test ajanlarını paralel çalıştırabilirsin.

Ajanlar yalnız öneri üretip durmayacak; görev bütün kabul kapıları geçene kadar çalışmaya devam edecek. Kullanıcıdan yalnız şu durumlarda onay istenecek:

- Yeni ürün veya oynanış kararı gerekiyorsa
- Kalıcı sayısal denge değeri belirlenecekse
- Kapsam genişletilecekse
- Save uyumluluğunu bozacak bir değişiklik gerekiyorsa
- Unity/URP/package sürümü değişecekse
- Destructive Git veya veri işlemi gerekiyorsa
- Mevcut kullanıcı kararları arasında çözülemeyen bir çelişki bulunuyorsa

Bunların dışındaki teknik uygulama kararlarını ekip kendi uzmanlığıyla çözüp raporlasın.

BAŞLANGIÇ NOKTASI

Repository:
`/Users/Erdem/Documents/Deaths Demolition`

Beklenen main HEAD:
`18a4cbf258b895c870153202ad2843ba4ebeb949`

Beklenen taban etiketi:
`wp-007-m2-special-weapon`

Paket branch önerisi:
`wp-008-m2-economy-upgrade-rules`

Nihai etiket:
`wp-008-m2-economy-upgrades`

Unity:
`2022.3.22f1`

URP:
`14.0.10`

Önce şunları doğrula:

1. `main` beklenen WP-007 commit'inde olmalı.
2. Çalışma ağacı temiz olmalı.
3. WP-000–WP-007 etiketleri korunmalı.
4. WP-007 test tabanı en az:
   - EditMode: 420/420
   - PlayMode: 122/122
5. Mevcut oyun akışı açılıp temel smoke kontrolünden geçmeli.
6. AP-001 staging üretimlerine dokunulmamalı.
7. Remote/push işlemi yapılmamalı.
8. Doğrudan `main` üzerinde geliştirme yapılmamalı.

Beklenen durumla uyuşmazlık varsa kullanıcıya hemen dönme. Önce C-00 ve S-10 ile incele. Güvenli biçimde çözülemeyen kritik bir fark varsa kullanıcıya tek ve net bir engel raporu gönder.

PAKETİN AMACI

WP-008'in amacı, WP-004 ile başlayan gold ve round sonu upgrade döngüsünü ürün kararlarıyla tamamlamak ve güvenilir bir üretim sözleşmesine dönüştürmektir.

Paket şu iki alanı birlikte kapatacaktır:

1. Düşman ödülünün base + version + başlangıç zırhı üzerinden deterministik hesaplanması
2. Round sonu upgrade ekranında üç kart, round başına tek satın alma ve tek ücretli reroll kuralının eksiksiz uygulanması

Bu paket sayısal denge paketi değildir. Eksik sayısal değerler yalnız merkezi config içinde `PrototypeOnly` olarak tutulabilir. Bunlar nihai ürün kararı olarak belgelenemez.

BAĞLAYICI ÜRÜN KARARLARI

Aşağıdaki kararlar kullanıcı tarafından onaylanmıştır ve değiştirilemez:

1. Her düşman gold düşürür.

2. Gold fiziksel paket olarak dünyaya düşer. Paketler süre nedeniyle kaybolmaz ve round sonunda mevcut deterministik otomatik toplama davranışı korunur.

3. Düşmanın gold ödülü deterministik olarak şu bileşenlerden oluşur:

   `Total Gold = Base Reward + Enemy Version Bonus + Spawn Armor Bonus`

   Rastgele gold miktarı kullanılmaz.

4. Enemy version, Standard/Special/Elite/Ultra düşman versiyonunu ifade eder. Special Weapon sistemiyle karıştırılmamalıdır.

5. Armor bonusu, düşmanın doğduğu anda üzerinde bulunan zırh parçalarına göre belirlenir:

   - Helmet
   - Body Armor
   - Leg Armor

6. Dövüş sırasında zırhın hasar görmesi veya tamamen kırılması gold ödülünü azaltmaz. Ödül hesabı spawn anındaki değişmez armor snapshot'ını kullanır.

7. Bir düşman yaşam kimliği için gold yalnız bir kez üretilebilir. Tekrarlanan ölüm olayı, despawn, corpse state veya event replay ikinci ödül oluşturmamalıdır.

8. Her round sonu upgrade ekranında tam olarak üç farklı ve geçerli kart sunulur.

9. "Kullanılabilir/geçerli kart" şu anlama gelir:

   - Oyuncunun mevcut klasik silahı ve build'iyle uyumlu
   - İlgili içerik açıksa erişilebilir
   - Önkoşulları sağlanmış
   - Seçildiğinde geçerli bir upgrade uygulayabilir

   Kartın fiyatının oyuncunun mevcut gold miktarından yüksek olması, kartı havuzdan çıkarmaz. Böyle bir kart gösterilebilir fakat satın alma düğmesi devre dışı olmalıdır.

10. Bir offer içinde aynı upgrade kimliği iki kez gösterilemez.

11. Oyuncu her roundda en fazla bir upgrade satın alabilir. Hiç satın almadan Continue seçebilmesi mümkündür.

12. Tek satın alma hakkı ekran kapatılıp açılarak, pause/resume yapılarak, aynı round oturumu yeniden bağlanarak veya aynı komut tekrar gönderilerek yenilenemez.

13. Oyuncu satın alma yapmadan önce yalnız bir kez ücretli reroll kullanabilir.

14. Satın alma tamamlandıktan sonra reroll ve diğer satın alma seçenekleri kapanır; yalnız Continue davranışı açık kalır.

15. Reroll sonucunda üç kart yine birbirinden farklı ve geçerli olmalıdır.

16. Reroll en az bir kartı değiştirmeli, fakat üç kartın tamamını değiştirmemelidir. Başka bir ifadeyle sonuçta tam olarak bir veya iki slot değişmeli ve en az bir önceki kart korunmalıdır.

17. Üç kartlık geçerli bir offer veya kurala uygun reroll sonucu üretilemiyorsa:

   - Oyuncudan gold kesilmez.
   - Duplicate veya geçersiz kart uydurulmaz.
   - Sistem açık bir configuration error üretir.
   - Ekran güvenli bir hata durumuna geçer.
   - Continue davranışı açık kalır.
   - Oyun soft-lock veya crash yaşamaz.

18. Reroll ücreti ve upgrade satın alma bedeli atomik işlemlerdir. İşlem başarısızsa gold kaybedilmez.

19. Run Results ekranındaki `Gold Spent`, upgrade satın almalarına ve reroll ücretlerine harcanan toplam gold'u birlikte gösterir.

20. Şimdilik Run Results ekranına ayrı bir reroll harcaması satırı eklenmez.

21. Upgrade'ler yalnız mevcut koşu boyunca korunur ve oyuncu öldüğünde sıfırlanır.

22. Special Weapon'lar round sonu gelişim ağacından etkilenmez. WP-007'de kurulan izolasyon korunmalıdır.

UYGULAMA KAPSAMI

A. Gold reward modeli

- Mevcut `GoldRewardValue.Resolve` ve ilişkili ölüm/ödül akışını incele.
- Gold hesabını tek ve kanonik bir servis veya saf hesaplama katmanında topla.
- Base, version ve armor katkılarını ayrı ayrı izlenebilir kıl.
- Toplamla birlikte breakdown sunabilecek veri sözleşmesi oluştur.
- Spawn anında enemy version ve armor slotlarının immutable reward snapshot'ını üret.
- Ölüm anında canlı armor component durumunu okuyarak ödülü yeniden hesaplama.
- LifeId veya mevcut eşdeğer yaşam kimliği üzerinden idempotent ödül üretimi sağla.
- Standard düşman davranışını geriye dönük bozma.
- Henüz oynanışta aktif olmayan tier'lar için yeni AI, görünüm, yetenek veya spawn kuralı üretme. Yalnız mevcut metadata/sözleşme yüzeyini kullan.
- Değerleri Inspector'a dağılmış magic number hâline getirme.
- Gerekirse bütün geçici değerleri merkezi ve açık biçimde `PrototypeOnly` olarak tut.

B. Upgrade offer/session modeli

Round sonu ekranını basit bir rastgele liste yerine kimlikli bir upgrade session olarak ele al.

Session en az şu kimlikleri güvenilir biçimde taşımalıdır:

- Run identity
- Round identity
- Offer/session identity
- Offer revision
- Purchase entitlement state
- Reroll entitlement state
- Gösterilen upgrade kimlikleri

Beklenen davranışlar:

- İlk offer aynı round için yalnız bir kez oluşturulur.
- UI yeniden bağlandığında yeni bir ücretsiz offer üretilmez.
- Bir purchase command yalnız güncel session/revision üzerinde çalışabilir.
- Aynı komutun tekrar oynatılması ikinci harcamaya veya ikinci upgrade'e yol açmaz.
- Stale offer veya stale revision komutları güvenli biçimde reddedilir.
- Satın alma tek atomik işlem olmalıdır:
  1. Session doğrulama
  2. Entitlement doğrulama
  3. Kart uygunluğu doğrulama
  4. Bakiye doğrulama
  5. Gold harcama
  6. Upgrade uygulama
  7. Session kilitleme
- Herhangi bir adım tamamlanamazsa sessiz gold yakma veya yarım uygulanmış upgrade oluşmamalıdır.

C. Reroll işlemi

- Reroll yalnız satın alma öncesi ve round başına bir kez kullanılabilir.
- Yeterli gold yoksa düğme devre dışı veya işlem güvenli ret durumunda olmalıdır.
- Gold, yeni offer başarıyla doğrulanmadan kalıcı olarak kesilmemelidir.
- Yeni offer eski offer'dan tam olarak bir veya iki slot açısından farklı olmalıdır.
- Sonuç yine üç farklı geçerli kart içermelidir.
- RNG bağımlılığı enjekte edilebilir ve testlerde deterministik olmalıdır.
- Küçük upgrade havuzlarında sonsuz arama döngüsü oluşturma.
- Uygun kombinasyon yoksa configuration error yolunu kullan.
- Reroll sonucu üretilemezse reroll hakkı ve gold kaybı yaşanmamalıdır.

D. UI/UX

Mevcut WP-005 kalite ve mimari sözleşmelerini koru. Büyük bir görsel yeniden tasarım yapma; yalnız bu paketin ihtiyaç duyduğu UI davranışlarını profesyonelce tamamla.

Upgrade ekranında:

- Tam olarak üç kart alanı
- Kart adı ve mevcut açıklama sistemi
- Fiyat
- Satın alınabilirlik durumu
- Devre dışı satın alma geri bildirimi
- Reroll düğmesi
- Reroll maliyeti
- Reroll hakkının kullanıldığını gösteren açık durum
- Satın alma sonrası kilitlenmiş offer
- Continue davranışı
- Configuration error için güvenli İngilizce durum mesajı

bulunmalıdır.

Bütün oyuncuya görünen metinler İngilizce olacaktır.

UI:

- Event-driven çalışmalı.
- Her kare object search, string üretimi veya gereksiz allocation yapmamalı.
- Mouse ve mevcut desteklenen navigation akışıyla çalışmalı.
- Gamepad M4 kapsamı olarak ertelenmişse bu pakette kapsam genişletme.
- Pause ve timeScale=0 durumlarında yanlış işlem üretmemeli.
- Çift tıklama veya çok hızlı komutlarda çift harcamaya izin vermemeli.

E. Results ve muhasebe

- Run boyunca yapılan upgrade harcamaları ve reroll ücretleri güvenilir ledger/transaction kayıtlarıyla takip edilmeli.
- Results ekranındaki `Gold Spent` ikisinin toplamı olmalı.
- Transaction replay veya UI yeniden bağlanması toplamı iki kez artırmamalı.
- Mevcut gold earned, remaining gold, build summary ve profile presentation alanlarını bozma.
- Ayrı `Reroll Spent` satırı ekleme.
- Profile XP kurallarını değiştirme.
- Gold kazanmak veya harcamak profile XP vermemelidir.

F. Dokümantasyon

Yalnız kullanıcı tarafından onaylanan yukarıdaki kararları mevcut kanonik karar/gereksinim mimarisine ekle.

- Bekleyen öneri veya varsayımı karar gibi yazma.
- Yeni sayısal değerleri ürün kararı gibi kaydetme.
- Kullanılan geçici sayıları `PrototypeOnly` olarak işaretle.
- DEC-048 ve DEC-050 dahil bu paketle kapanan mevcut karar/kod boşluklarını izlenebilir hâle getir.
- İlgili DEC/REQ → runtime module → test mapping tablosunu güncelle.
- Belgelerde oyun dilinin İngilizce olduğu kuralını koru.

KAPSAM DIŞI

Bu pakette şunları yapma:

- Yeni silah, karakter veya düşman tasarlama
- Special/Elite/Ultra düşman yeteneklerini uygulama
- Düşman tier debut roundlarını belirleme
- Nihai gold veya upgrade fiyat dengesi yapma
- Yeni upgrade içerikleri uydurma
- Special Weapon upgrade ağacı oluşturma
- AP-001 assetlerini Unity'ye aktarma
- Higgsfield, Adobe veya Blender üretimi yapma
- Yeni map veya sanat yönü oluşturma
- Gamepad kapsamını genişletme
- Online servis, leaderboard, cloud save veya netcode ekleme
- Unity/URP/package sürümünü değiştirme
- Remote ekleme veya push yapma
- İlgisiz eski teknik borçları pakete çekme

TEST VE KANIT KAPILARI

Önce baseline testlerini çalıştır, ardından yeni testleri ekle.

EditMode testleri en az şunları kapsamalı:

1. Base reward hesabı
2. Standard/Special/Elite/Ultra version bonus breakdown'ı
3. Sekiz armor kombinasyonu:
   - none
   - helmet
   - body
   - legs
   - helmet + body
   - helmet + legs
   - body + legs
   - all three
4. Zırh kırıldıktan sonra ödülün değişmemesi
5. Aynı LifeId için tek gold ödülü
6. Duplicate death/despawn event idempotency
7. Tam üç farklı kart
8. Kilitli veya build'e aykırı kartların filtrelenmesi
9. Pahalı fakat uygun kartın gösterilmesi
10. Yetersiz bakiyede satın alma reddi
11. Round başına yalnız bir satın alma
12. Reopen/pause ile hakkın yenilenmemesi
13. Aynı purchase command replay güvenliği
14. Stale offer/revision reddi
15. Başarılı tek reroll
16. İkinci reroll reddi
17. Purchase sonrası reroll reddi
18. Reroll sonucunda yalnız bir veya iki slotun değişmesi
19. Reroll sonucunda üç benzersiz kart
20. Geçerli üç kart üretilemediğinde no-charge güvenli hata
21. Reroll başarısızlığında gold ve hakkın korunması
22. Special Weapon izolasyonu
23. `Gold Spent = purchases + rerolls`
24. Results toplamının replay ile iki kez artmaması

PlayMode testleri en az şunları kapsamalı:

- Gerçek round sonu ekranında üç kart görünmesi
- Unaffordable kartın görünür fakat disabled olması
- Tek satın alma sonrası kart ve reroll kontrollerinin kilitlenmesi
- Continue akışının çalışması
- Bir kez ücretli reroll
- UI reopen sonrası offer/session korunması
- Configuration error durumunda soft-lock olmaması
- Results ekranında birleşik Gold Spent
- Special Weapon aktif/pasif durumunun upgrade etkisinden bağımsız kalması
- Pause/timeScale davranışı
- Hızlı/çift input altında tek transaction

Gerçek Player üzerinde ayrı bir Economy/Upgrade probe hazırla. Probe en az şu uçtan uca akışı kanıtlamalı:

Enemy kill
→ immutable reward snapshot
→ tek physical gold packet
→ pickup veya round-end sweep
→ wallet
→ üç kartlık offer
→ ücretli reroll
→ tek purchase
→ Continue
→ sonraki round
→ run death
→ Results içindeki doğru Gold Spent/build özeti
→ yeni run'da run-only build ve gold sıfırlanması

Test sayısını yapay biçimde hedefleme. Her davranışı kapsayan anlamlı testler yaz. Mevcut 420 EditMode ve 122 PlayMode testinin tamamı korunmalı ve yeni testlerle birlikte yüzde yüz geçmelidir.

PERFORMANS VE KOD KALİTESİ

- Sıcak gameplay/UI yolunda gereksiz LINQ ve allocation kullanma.
- Her kare string üretme veya scene search yapma.
- Offer hesaplamasını event/session sınırlarında yap.
- RNG'yi enjekte edilebilir tut.
- Ekonomi kurallarını MonoBehaviour içinden ayrıştırılmış saf/test edilebilir katmanlarda uygula.
- Para işlemlerinde floating point kullanma.
- Negatif bakiye oluşamaz.
- Overflow ve geçersiz config durumlarını güvenli biçimde ele al.
- Public event/snapshot sözleşmelerini gerekmedikçe bozma.
- Mevcut save/profile formatını bozma; gerekiyorsa geriye uyumlu genişlet.
- Yeni global singleton bağımlılığı oluşturma.
- Birden fazla Player instance konusunda WP-002'den kalan profile save kararını bu pakette gizlice çözme.

GIT VE ENTEGRASYON KURALLARI

- Paket için ayrı branch ve worktree kullan.
- Direct main write yapma.
- Her dosyanın yazma sahibini net tut.
- Kullanıcının ilgisiz değişikliklerini değiştirme veya geri alma.
- Rebase/reset/force push gibi destructive işlemler yapma.
- Remote/push yapma.
- S-10 F0–F10 denetimlerini çalıştırsın.
- Secret, generated, Library, Logs, Temp, build çıktısı veya `.claude` commit etme.
- Manifest v2 üret ve commit ağacıyla birebir doğrula.
- Entegrasyon yalnız S-01 koşulsuz PASS ve S-10 PASS sonrasında fast-forward olarak yapılmalı.
- Merge commit üretme.
- Nihai etiket yalnız doğrulanmış entegre commit'e konulmalı.
- Önceki etiketleri değiştirme.

TESLİM RAPORU

C-00'a tek ve eksiksiz nihai rapor gönder. Raporda en az şunlar bulunmalı:

- Sonuç: TAMAMLANDI / BLOCKED
- Başlangıç ve nihai commit
- Branch ve etiket
- Main entegrasyon yöntemi
- Git status
- Manifest dosyası ve SHA-256
- Değişen üretim dosyaları
- Değişen test dosyaları
- Değişen karar/gereksinim belgeleri
- Kadroya dahil edilen uzmanlar ve görevleri
- Gold reward breakdown mimarisi
- Immutable spawn armor snapshot çözümü
- Transaction/session/idempotency çözümü
- Reroll algoritması ve hata davranışı
- UI davranışları
- Results muhasebesi
- EditMode sonuçları
- PlayMode sonuçları
- Player probe sonuçları
- Release build ve smoke sonucu
- S-01 mimari kararı
- S-10 Git kararı
- Yeni PrototypeOnly değerler
- Ertelenen konular
- Bilinen riskler
- Remote/push yapılmadığının teyidi

DEFINITION OF DONE

WP-008 yalnız şu koşulların tamamında bitmiş sayılır:

- Base + Version + Spawn Armor reward modeli çalışıyor.
- Zırh kırılması gold ödülünü değiştirmiyor.
- Her LifeId yalnız bir gold paketi üretiyor.
- Her round tam üç farklı geçerli kart sunuluyor.
- Unaffordable fakat geçerli kart doğru biçimde gösteriliyor.
- Round başına en fazla bir satın alma yapılabiliyor.
- Satın alma öncesi yalnız bir ücretli reroll yapılabiliyor.
- Reroll bir veya iki kartı değiştiriyor.
- Geçersiz offer durumunda gold kaybı ve soft-lock yaşanmıyor.
- Gold işlemleri atomik ve replay-safe.
- Results `Gold Spent`, upgrade ve reroll harcamalarını birlikte gösteriyor.
- Special Weapon izolasyonu korunuyor.
- Run-only ekonomi/build ölümde sıfırlanıyor.
- Bütün oyuncu metinleri İngilizce.
- Tüm eski ve yeni testler geçiyor.
- Gerçek Player probe geçiyor.
- Release build/smoke geçiyor.
- S-01 koşulsuz PASS veriyor.
- S-10 bütün Git kapılarına PASS veriyor.
- Manifest commit ağacıyla birebir eşleşiyor.
- Main temiz ve fast-forward entegre.
- `wp-008-m2-economy-upgrades` etiketi doğru commit'i gösteriyor.
- Remote ve push yok.
- C-00 nihai raporu teslim almış durumda.

Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma. Sorun bulursan nedenini analiz et, kapsam içindeki güvenli çözümü uygula, test et ve kapılar geçene kadar devam et.
