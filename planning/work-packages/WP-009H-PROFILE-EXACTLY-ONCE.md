WP-009H — PERSISTENT EXACTLY-ONCE REPLAY PROTECTION CLOSURE

PAKET SAHİBİ
S-04 — Economy, Progression & Save

ANA KOORDİNASYON
C-00 — Deaths Demolition Main Coordination

KAYIT NOTU
Aşağıdaki metin kullanıcının 2026-09-14 tarihinde S-04'e verdiği yürütme promptunun aynısıdır (paket kaydı). WP-009H yeni özellik/ürün paketi değil, WP-009'un ilan edilmiş exactly-once acceptance sözleşmesini gerçek uygulamayla uyumlu hale getiren teknik kapanış düzeltmesidir; yeni DEC/REQ yoktur (ADR correction/addendum planning/04 §WP-009H).

---

# WP-009H — Persistent Exactly-Once Replay Protection Closure

## 0. Görevin amacı

WP-009 main entegrasyonundan sonra bağımsız incelemede kanıtlanan acceptance defect'ini dar kapsamlı olarak düzelt.

Kanıtlanan kusur:

- `ProfileData.AppliedCompletionIdCapacity = 8`.
- `RecordAppliedCompletion`, kapasite aşıldığında en eski completion ID'yi siliyor.
- `ProfileData.Sanitize` listeyi aynı kapasiteye indiriyor.
- Mevcut testler eski ID'nin silinmesini beklenen davranış kabul ediyor.
- `ProfileService.ApplyRun` dokümantasyonu ve WP-009 Definition of Done ise aynı completion'ın process, reload ve relaunch sınırları dahil hiçbir zaman iki kez uygulanamayacağını söylüyor.
- Dokuz veya daha fazla başarılı koşudan sonra ilk koşunun ID'si listeden çıkar.
- Bu eski `RunSummary` yeniden sunulduğunda XP, kills, RunsPlayed, rounds ve diğer profile değerleri ikinci kez uygulanabilir.

Mevcut uygulama gerçek exactly-once sağlamıyor; yalnız son sekiz koşuyu kapsayan sınırlı replay koruması sağlıyor.

WP-009H yeni bir özellik veya ürün paketi değildir. WP-009'un ilan edilmiş acceptance sözleşmesini gerçek uygulamayla uyumlu hale getiren teknik kapanış düzeltmesidir.

WP-009H tamamen kapanmadan WP-010'a geçme.

---

## 1. Oturum, Kadro ve sahiplik

Birincil oturum, tek kaynak kod yazarı ve tek package commit owner:

- `S-04 — Economy, Progression & Save`

Zorunlu inceleme sahipleri:

- `S-01 — Architecture`
  - Completion identity authority
  - Exactly-once invariant
  - Save transaction
  - Concurrent writer lock
  - Schema migration
  - Profile reset lineage
- `S-10 — QA, Release & Git`
  - Kusurun bağımsız yeniden üretimi
  - Düzeltmenin bağımsız doğrulanması
  - Migration, concurrency ve kullanıcı verisi güvenliği
  - Git ve manifest kapıları
- `C-00`
  - Koordinasyon
  - Rapor toplama
  - Final fast-forward entegrasyonu
  - Etiketleme

Kadro sistemini kullan. En az şu uzmanlıkları çalışmaya dahil et:

- Prompt Engineer
- Context Engineer
- PRA / Prompt Risk & Accuracy
- Save/Persistence Architect
- Transaction and Idempotency Specialist
- Unity Lifecycle Engineer
- Data Migration Specialist
- Defensive Programming Specialist
- Concurrency/File-Locking Specialist
- Automated Test Engineer
- Fault-Injection QA
- Git and Repository Safety Specialist

Görev içinde ajanlara verilecek bütün alt promptlar Prompt Engineer tarafından hazırlanmalı ve PRA tarafından şu açılardan incelenmelidir:

- Kapsam doğruluğu
- Acceptance kriterlerinin eksiksizliği
- Yetkisiz ürün kararı
- Veri kaybı riski
- Yanlış exactly-once iddiası
- Test edilemeyen veya kanıtsız varsayımlar

S-04 dışındaki ajanlar açıkça yazma görevi verilmedikçe salt okunur çalışmalıdır.

Ajanlar bütün zorunlu kapılar kapanana kadar durmaksızın ilerlemelidir. Yalnız "Kritik durma koşulları" oluşursa kullanıcı kararı istenmelidir.

---

## 2. Doğrulanacak başlangıç durumu

Başlangıç tabanı:

- `main`:
  `45506612cf325110bccddb5eea7b4f1bc24b796e`
- Mevcut ve değiştirilmeyecek etiket:
  `wp-009-m2-profile-save`
- Tracked file:
  `820`
- WP-009 final manifest:
  `38f2a47efd7856f71584fc913986f9ba9475022e1c2a4f2cde96d696850386f4`
- Unity:
  `2022.3.22f1`
- URP:
  `14.0.10`
- Başlangıç çalışma ağacı:
  temiz
- Remote:
  yok
- Push:
  yapılmadı

Başlangıç doğrulama tabanı:

- EditMode: `564/564`
- PlayMode: `132/132`
- RoundProbe: `8/8`
- GoldProbe: `12/12`
- ArmorProbe: boş/idle makinede `9/9`
- SpecialWeaponProbe: `10/10`
- EconomyUpgradeProbe: `11/11`
- ProfilePersistenceProbe: `14/14`
- LifecycleProbe: A/B PASS

İzole branch/worktree adı:

- `wp-009h-profile-exactly-once`

C-00 entegrasyonundan sonra kullanılacak etiket:

- `wp-009h-profile-exactly-once`

Mevcut `wp-009-m2-profile-save` etiketini taşıma, silme veya yeniden oluşturma.

Başlangıçta şunları salt okunur doğrula:

1. main HEAD
2. Mevcut etiketler
3. Çalışma ağacı
4. Tracked file sayısı
5. WP-009 final manifest eşleşmesi
6. Unity ve URP sürümleri
7. Gerçek kullanıcı profilinin yolu, sürümü ve hash durumu
8. Bütün worktree'ler
9. Remote/push durumu
10. AP paketlerinin izolasyonu

Başlangıç manifesti ve kırmızı kusur kanıtı üretilmeden production kodunu değiştirme.

---

## 3. Bağlayıcı sınırlar

Aşağıdaki mevcut kurallar değiştirilemez:

- Aynı başarıyla tamamlanmış koşu profile en fazla bir kez uygulanabilir.
- Bu garanti process, scene rebind, Results yeniden açılması, profile reload ve uygulama relaunch sonrasında da geçerlidir.
- Oyuncunun aynı anda yalnızca bir aktif koşusu vardır.
- Profil tamamen local ve offline'dır.
- Kullanıcıya gösterilen kalıcı run history/archive sistemi bulunmaz.
- Gold, run build ve koşu upgrade'leri profile yazılmaz.
- Profil seviyesi doğrudan combat gücü sağlamaz.
- B5 abort davranışı değişmez.
- Save failure sonrasında canlı profil ile disk sessizce ayrışamaz.
- Başarısız save sonrasında aynı geçerli completion güvenli biçimde retry edilebilir.
- Claim sistemi eklenmez.
- UI/UX tasarımı değiştirilmez.
- XP, unlock ve achievement sayısal değerleri PrototypeOnly kalır.
- Anti-cheat, encryption, cloud save, account veya leaderboard eklenmez.
- Yeni ürün DEC/REQ oluşturulmaz.
- AP-001/AP-002 veya diğer asset-production paketlerine dokunulmaz.
- Unity, URP veya package sürümü değiştirilmez.
- Remote veya push yapılmaz.

Bu düzeltme kullanıcıya görünen yeni özellik, yeni içerik, yeni ekonomi davranışı veya yeni ilerleme kuralı üretmemelidir.

---

## 4. Kusuru önce bağımsız olarak yeniden üret

Mevcut main üzerinde, düzeltme yapmadan önce kırmızı karakterizasyon testi oluştur ve çalıştır.

Zorunlu yeniden üretim:

1. En az dokuz farklı geçerli completion başarıyla uygula.
2. İlk completion ID'nin sekiz elemanlı halkadan çıkarıldığını göster.
3. İlk koşunun orijinal `RunSummary` verisini yeniden `ProfileService.ApplyRun` veya gerçek production application path üzerinden sun.
4. Aşağıdaki değerlerin ikinci kez arttığını kanıtla:
   - RunsPlayed
   - Profile XP
   - Total kills
   - Tier kills
   - RoundsCompleted
   - İlgili diğer lifetime sayaçları
5. Aynı deneyi statik servis reseti ve diskten profile reload sonrasında tekrarla.
6. Mevcut sistemin kusuru hem aynı process içinde hem reload sonrasında üretebildiğini kanıtla.

Kanıt kaydında şunlar bulunmalıdır:

- Kullanılan completion kimlikleri
- Başlangıç profile hash'i
- İlk dokuz transaction sonrası profile hash'i
- Replay öncesi sayaçlar
- Replay sonrası sayaçlar
- Disk hash'i
- RAM/disk farkı
- `Changed` event sayısı
- Save çağrı sayısı
- Test sonucu

Mevcut `KimlikHalkasi` veya eşdeğer testin hatalı davranışı beklenen sonuç saydığını raporla.

Bu testi silme veya kusuru saklayacak şekilde yumuşatma. Düzeltme sonrasında aynı test gerçek sözleşmeyi doğrulayacak hale getirilmelidir.

---

## 5. Zorunlu çözüm invariantları

Yeni sistem aşağıdaki invariantların tamamını sağlamalıdır:

1. Başarıyla uygulanmış herhangi bir completion, aradan kaç koşu geçerse geçsin tekrar uygulanamaz.
2. Koruma yalnızca "son N completion" üyeliğine dayanamaz.
3. Kapasiteyi 8'den daha büyük bir sayıya çıkarmak çözüm değildir.
4. Başka bir sonlu ring, LRU, cache veya rolling list çözüm değildir.
5. Bloom filter veya false-positive üreten olasılıksal yapı kullanılamaz.
6. Bütün geçmiş completion ID'lerini sınırsız listede tutmak çözüm değildir.
7. Kullanıcı görünür veya görünmez sınırsız run history oluşturulamaz.
8. Persistent anti-replay state başarılı koşu sayısıyla büyümemeli; O(1) kalmalıdır.
9. Başka profile ait, eski formatlı, bozuk, stale, abort edilmiş veya authoritative lifecycle tarafından üretilmemiş token fail-closed reddedilmelidir.
10. Aynı istatistiklere sahip iki gerçek ayrı koşu ayrı ayrı tam bir kez uygulanabilmelidir.
11. Profile değerleri ve anti-replay state aynı save transaction içinde kalıcılaşmalıdır.
12. Save başarısızsa completion tüketilmiş sayılmamalıdır.
13. Save failure sonrası aynı live completion retry edilebilmelidir.
14. Save başarıyla tamamlandıktan sonra aynı completion'ın hiçbir retry'ı profile delta üretememelidir.
15. Sayaç veya ordinal overflow eski completion'ı yeniden geçerli hale getiremez.
16. `LastSavedUtc`, sistem saati, `RunId` veya `s_resultsScheduled` tek başına transaction identity olamaz.
17. Profile reset sonrası eski profile ait completion yeni profile uygulanamaz.
18. İki Player instance aynı profile path'inde aynı anda writer olamaz.
19. Lock edinme hatası fail-open yazmaya dönüşemez.
20. Read-only session save, reset veya profile completion commit yapamaz.

S-01, production kodu değiştirilmeden önce bu invariantları sağlayan teknik tasarımı incelemeli ve yazılı mimari PASS üretmelidir.

---

## 6. Uygulanacak mimari yön

S-01 aynı invariantları daha sade bir sistemle kanıtlayamadığı sürece aşağıdaki bounded metadata modelini uygula.

### 6.1 Profile replay otoritesi

Profile state içinde sabit boyutlu olarak en az şu kavramlar bulunmalıdır:

- `ProfileLineageId`
  - Profile ömrü boyunca değişmeyen rastgele 128-bit veya eşdeğer güçlü kimlik.
- `LastAppliedCompletionOrdinal`
  - Başarıyla uygulanmış son profile transaction'ın monoton ve overflow-safe sırası.

Opaque completion token dahili olarak şu kavramları taşımalıdır:

- Token schema version
- Profile lineage
- Pozitif monoton ordinal
- Random nonce veya eşdeğer tek-kullanımlık capability verisi

Token formatı yalnız ilgili identity authority tarafından üretilmeli ve ayrıştırılmalıdır. Diğer sistemler tokenı opaque değer olarak görmelidir.

En kritik kural:

Arbitrary GUID kabulünü devam ettirerek bounded metadata ile süresiz exactly-once iddiasında bulunma. Yeni lineage + monoton ordinal authority sözleşmesini uygula ve V2 GUID namespace'ini migration sınırında tamamen emekliye ayır.

### 6.2 Identity authority

`RunDirector` artık kendi başına rastgele GUID üretip bunu authoritative replay identity olarak kullanmamalıdır.

Dedicated bir completion identity authority veya eşdeğer sınır oluştur.

Tek aktif koşu kuralıyla önerilen akış:

1. Yeni profile oluşturulduğunda sabit bir lineage üretilir.
2. Yeni koşu için beklenen ordinal:
   `LastAppliedCompletionOrdinal + 1`
3. Identity authority bu lineage ve ordinal için tek live completion capability üretir.
4. Aynı koşunun save retry işlemi aynı capability'yi kullanır.
5. Yeni koşu başladığında önceki uncommitted/stale capability geçersiz hale gelir.
6. Abort edilen koşunun capability'si geçersizleşir fakat profile ordinal veya diğer değerler değişmez.
7. Save failure capability'yi tüketmez; aynı process içindeki güvenli retry mümkündür.
8. Successful save, profile değerleri ve ordinal'i aynı candidate transaction içinde kalıcılaştırır.
9. Save başarılı olduktan sonra capability tüketilir.
10. Relaunch sonrasında eski live capability bellekte otorite sahibi değildir ve profile high-watermark tarafından da reddedilir.

### 6.3 ApplyRun doğrulama sırası

Profile mutation başlamadan önce:

1. Token biçimini katı biçimde parse et.
2. Token schema sürümünü doğrula.
3. Token lineage ile profile lineage eşleşmesini doğrula.
4. Ordinal'in pozitif ve geçerli aralıkta olduğunu doğrula.
5. `ordinal <= LastAppliedCompletionOrdinal` ise duplicate/stale olarak reddet.
6. Ordinal beklenen sonraki sıra değilse reddet.
7. Token mevcut authoritative lifecycle tarafından gerçekten üretilmemişse reddet.
8. Nonce/capability eşleşmesini doğrula.
9. Ancak bütün doğrulamalar başarılıysa reward candidate hesapla.
10. Stats, XP, unlock, achievement ve yeni ordinal'i aynı candidate state içine yaz.
11. Candidate'ı doğrula.
12. Diske transaction olarak kaydet.
13. Yalnız save başarıyla sonuçlandığında canlı profile yayınla.
14. `Changed` ve immutable receipt'i yalnız bir kez üret.

Token alanlarını manuel olarak oluşturan normal production API bulunmamalıdır. Testler için açıkça test/development-only injection seam kullanılabilir.

Release build'de debug token issuer, arbitrary replay komutu veya test-only capability üretimi bulunmamalıdır.

---

## 7. Concurrent writer lock düzeltmesi

Mevcut profile ownership lock uygulamasında lock oluşturma veya alma sırasında exception oluştuğunda yazmaya devam edilen fail-open yol bulunuyor.

Ayrıca iki süreç aynı anda stale lock gördüğünde:

- İkisi de lock dosyasını silmeye çalışabilir.
- Biri `CreateNew` sırasında kaybedebilir.
- Kaybeden süreç exception yolundan yazmaya devam edebilir.

Bu durum exactly-once ve tek-writer sözleşmesini bozar.

WP-009H şu kuralları sağlamalıdır:

- Aynı profile path için yalnız bir write owner olabilir.
- Lock sahipliği süreç boyunca tutulan OS/file lease veya eşdeğer atomik mekanizmayla korunmalıdır.
- Lock dosyasının yalnız PID içeren varlığı tek başına write authority sayılmamalıdır.
- Lock oluşturma, açma, doğrulama veya sahiplik edinme başarısızsa session read-only olmalıdır.
- "Lock unavailable; continuing without it" davranışı kaldırılmalıdır.
- İki süreç eşzamanlı açılırsa yalnız biri writer olmalıdır.
- Kaybeden süreç profile save, reset veya completion commit yapamamalıdır.
- Stale lock reclaim yarışında yalnız gerçek kazanan writer olmalıdır.
- Lock release yalnız mevcut ownership token/lease sahibince yapılmalıdır.
- Başka bir sürecin lock dosyası silinmemelidir.
- Process crash sonrasında ownership güvenli şekilde geri alınabilmelidir.
- `SetPathOverride`, subsystem reset ve application quit lease lifecycle'ını doğru yönetmelidir.
- Lock edinilememesi oyunu çökertmemelidir.
- Mevcut İngilizce persistence uyarısı üzerinden progress'in kaydedilmeyeceği dürüstçe gösterilmelidir.
- Read-only session tarafından üretilen completion başka writer session'da geçerli kabul edilmemelidir.

Bu değişiklik yeni multiplayer veya online sistem değildir. Yalnız mevcut tek yerel profil yazıcısı sözleşmesini güvenilir hale getirir.

---

## 8. Save schema ve migration

Yeni alanlar nedeniyle `SaveVersion`ı uygun şekilde artır. Beklenen hedef v3'tür.

### 8.1 V3 authoritative alanları

V3 içinde replay otoritesi şu bounded state'e dayanmalıdır:

- ProfileLineageId
- LastAppliedCompletionOrdinal
- Gerekli sabit boyutlu schema metadata

Aşağıdakiler authoritative replay state olmaktan çıkarılmalıdır:

- `AppliedCompletionIds`
- `AppliedCompletionIdCapacity`
- Ring eviction
- Son N GUID üyeliği

### 8.2 V1/V2 → V3 migration

V1 ve V2'den migration sırasında şu player verilerini kayıpsız koru:

- ProfileLevel
- XpIntoLevel
- TotalXpEarned
- HighestRound
- RunsPlayed
- RoundsCompleted
- TotalKills
- StandardKills
- SpecialKills
- EliteKills
- UltraKills
- UnattributedKills mevcutsa
- ArmorPiecesBroken
- BestRunXp
- Achievements
- UnlockedWeapons
- UnlockedBranches
- LastSavedUtc'nin mevcut teşhis semantiği

V2'de evict edilmiş completion GUID'lerini geri çıkarmak mümkün değildir. Bunları ordinal'e dönüştürmeye veya eksik geçmişi tahmin etmeye çalışma.

Migration sınırı:

- V1/V2 profile için yeni, benzersiz ProfileLineageId oluştur.
- Yeni lineage içindeki LastAppliedCompletionOrdinal başlangıcını teknik olarak tutarlı biçimde oluştur.
- Bütün legacy raw-GUID tokenları yeni parser tarafından fail-closed reddedilsin.
- Eski sekiz GUID yeni replay otoritesi olarak kullanılmasın.
- Legacy teknik metadata'nın removal/quarantine politikasını raporla.
- Oyuncunun profile progression verisini teknik metadata ile birlikte kaybetme.
- Migration idempotent olsun.
- V3 olarak işaretlenmiş fakat lineage'ı eksik veya bozuk dosyayı sessizce yeni lineage ile onarma; corrupt/invalid profile prosedürüne yönlendir.
- Future version dosyasını değiştirme, downgrade etme veya overwrite etme.
- Persisted active-run/resume sistemi icat etme.

Zorunlu migration vakaları:

- V1 → V3
- V2 → V3
- V3 → load/save → V3
- Eksik alanlı V1/V2
- V2 boş AppliedCompletionIds
- V2 dolu sekiz AppliedCompletionIds
- On bir koşu yapılmış fakat yalnız son sekiz ID'yi taşıyan V2 fixture
- V3 eksik/bozuk lineage
- Malformed/truncated JSON
- Future V4+ JSON

---

## 9. Crash, transaction ve retry sözleşmesi

Stats, XP, unlock, achievement, entitlement ve replay ordinal tek transaction'dır.

Her save fault noktasında disk şu iki tam durumdan yalnız birinde kalmalıdır:

- Eski profile state + eski ordinal
- Yeni profile state + yeni ordinal

Şu durumlar yasaktır:

- Yeni stats + eski ordinal
- Eski stats + yeni ordinal
- RAM yeni, disk eski fakat sonuç Applied
- Anti-replay state kalıcı, ödüller eksik
- Ödüller kalıcı, anti-replay state eksik
- Belirsiz save sonrasında ikinci reward uygulaması

Save sonucu belirsizse primary yeniden okunmalıdır:

- Yeni candidate gerçekten primary olmuşsa transaction başarılı kabul edilir ve RAM aynı candidate'a yayınlanır.
- Eski profile korunmuşsa SaveFailed dönülür ve aynı live capability retry edilebilir.
- Relaunch sonrasında yeni ordinal görülüyorsa eski summary duplicate/stale olur.
- Backup recovery stats ve ordinal'i birlikte geri döndürmelidir.

WP-009'un mevcut crash-safe save, backup, candidate verification, corrupt preservation ve future-version read-only davranışları gerilememelidir.

Manuel save dosyası manipülasyonu, eski backup'ın kullanıcı tarafından elle geri yüklenmesi ve anti-cheat bu garantinin kapsamı dışındadır. Bunları exactly-once garantisinin parçasıymış gibi sunma.

---

## 10. Profile reset

Mevcut debug profile reset davranışını kullanıcıya görünür yeni özellik haline getirme.

Teknik olarak:

- Başarılı reset yeni ProfileLineageId üretmelidir.
- Ordinal başlangıç durumuna dönmelidir.
- Eski lineage'a ait bütün completion tokenları reddedilmelidir.
- Reset başarısızsa eski profil RAM ve diskte korunmalıdır.
- Primary'nin silindiği fakat fresh profile'ın henüz yazılmadığı açık veri kaybı penceresi bırakılmamalıdır.
- Başarılı reset sonrasında backup/recovery zinciri eski lineage'ı istemeden geri getirmemelidir.
- Reset, B5 abort davranışıyla ilişkilendirilmemelidir.
- Read-only ikinci instance reset yapamamalıdır.

---

## 11. Zorunlu test matrisi

### A. Kırmızı kusur testi

Düzeltme öncesinde:

- 9+ valid completion
- İlk completion replay
- İkinci profil uygulamasının kanıtı
- Aynı test reload sonrasında
- Sayaç, event ve hash farklarının kaydı

### B. Kapasite sınırını aşan kalıcı replay testleri

Production application path üzerinden en az şu serileri çalıştır:

- Eski kapasite + 1
- Eski kapasite × 2 + 3
- En az 64 ardışık completion
- Tercihen düşük maliyetli saf testte 10.000 completion

Her serinin ardından:

- İlk completion replay
- Ortadaki completion replay
- Son completion replay

Hiçbir replay şu değerleri değiştirmemelidir:

- RAM profile hash
- Disk profile hash
- XP
- RunsPlayed
- RoundsCompleted
- HighestRound
- Total/tier kills
- ArmorPiecesBroken
- Unlocks
- Achievements
- Changed event sayısı
- Save call sayısı

Profile dosyasının boyutu koşu sayısıyla doğrusal büyümemelidir.

### C. Reload ve relaunch

Uzun completion serisinden sonra:

- ProfileService statiklerini temizle.
- Profili diskten tekrar yükle.
- Gerçek Player process/relaunch probe'u çalıştır.
- İlk, orta ve son completion'ı tekrar sun.
- Üçü de reddedilmeli.
- Disk ve RAM hash'leri değişmemeli.

### D. Ayrı geçerli koşular

Aynı round, kill, XP, silah ve diğer summary alanlarına sahip iki ayrı gerçek koşu üret.

- İki koşu ayrı authoritative capability taşımalı.
- İkisi de tam birer kez uygulanmalı.
- Summary içeriğinin aynı olması duplicate nedeni olmamalı.

### E. Geçersiz tokenlar

Şunların tamamı profile mutation olmadan fail-closed reddedilmelidir:

- Boş identity
- Malformed token
- Eksik token bölümü
- Random/unissued token
- Legacy V2 raw GUID
- Başka profile lineage'ına ait token
- Abort edilmiş koşunun tokenı
- Yeni koşu başladıktan sonra sunulan stale token
- Daha önce uygulanmış token
- High-watermark gerisindeki ordinal
- Beklenmeyen ordinal gap
- Aynı ordinal fakat farklı nonce
- Ordinal 0
- Negatif ordinal
- `long.MaxValue` sınırı
- Overflow/wraparound adayı
- Future token schema version
- Reset öncesi lineage tokenı

### F. Save failure ve retry

WP-009'daki bütün save fault stage'leri için:

1. Save failure oluştur.
2. Canlı profile ve disk değişmeden kalmalı.
3. Completion uygulanmış kabul edilmemeli.
4. Aynı live token ile retry tam bir kez Applied olmalı.
5. Sonraki retry Duplicate/Stale olmalı.
6. Stats ve replay ordinal aynı transaction'da kalıcılaşmalı.
7. Process restart sonrasında aynı token tekrar uygulanamamalı.

Replace sonrası caller sonucunun belirsizleşebileceği crash penceresini ayrıca test et.

### G. Lifecycle

Test et:

- Duplicate `RunEnded`
- Fırlatan başka `RunEnded` abonesi
- Results yeniden açılması
- Scene rebind
- Static reset
- İki ardışık koşu
- SaveFailed sonrası retry
- SaveFailed sonrası yeni koşuya geçiş
- Application quit/relaunch
- İkinci read-only Player instance

### H. Abort/B5

Abort sonrasında:

- XP farkı 0
- Stats farkı 0
- RunsPlayed farkı 0
- Unlock/achievement farkı 0
- RAM profile hash farkı 0
- Disk profile hash farkı 0
- Results açılmaz
- Abort capability'si sonradan uygulanamaz
- Sonraki yeni koşu normal başlar

B5 davranışını değiştirme.

### I. Migration

Gerçekçi izole fixture'larla test et:

- V1 → V3
- V2 boş ID listesi → V3
- V2 sekiz ID → V3
- V2 on bir koşu/son sekiz ID → V3
- Legacy listedeki ilk/orta/son ID replay
- V2'de evict edilmiş eski GUID replay
- Migration'ın iki kez çalıştırılması
- İki load/save turu
- V3 invalid lineage
- Future version byte-for-byte korunumu
- Corrupt primary + valid backup recovery
- XP, level, stats, achievement ve entitlement eşitliği

### J. Concurrent writer

İki gerçek Player process'ini aynı izole profile path ile çalıştır:

- Yalnız biri writer olmalı.
- Kaybeden read-only olmalı.
- Kaybeden save yapamamalı.
- Kaybeden reset yapamamalı.
- Kaybeden completion commit yapamamalı.

Ayrıca test et:

- Simultaneous lock acquisition
- Lock creation exception
- Stale lock reclaim
- Owner crash
- PID reuse/name collision
- Ownership token mismatch
- Başka owner'ın lock'unu release etmeye çalışma
- `SetPathOverride`
- Subsystem reset
- Application quit
- Sonraki process tarafından güvenli yeniden edinme

---

## 12. ProfilePersistenceProbe güncellemesi

Development-only `ProfilePersistenceProbe`u genişlet.

Mevcut 14/14 aşamayı koru. Yeni aşamalar en az şunları fiziksel olarak kanıtlamalıdır:

1. Eski kapasiteden fazla gerçek completion uygulanması
2. İlk completion'ın aynı süreçte reddedilmesi
3. İlk completion'ın process/relaunch sonrasında reddedilmesi
4. Aynı summary değerleriyle yeni authoritative completion'ın uygulanması
5. Random/unissued token reddi
6. Legacy V2 GUID reddi
7. SaveFailed → retry → Applied → ikinci retry reddi
8. Abort tokenının geçersiz kalması
9. Reset öncesi tokenın yeni profile uygulanamaması
10. V2 migration sonrası legacy replay reddi
11. Duplicate vakalarında RAM ve disk hash eşitliği
12. İki process'ten yalnız birinin writer olması

Probe:

- Development-only olmalı.
- Release build'e sızmamalı.
- Release DLL içinde probe komutları bulunmamalı.
- Release DLL içinde debug token issuer bulunmamalı.
- Gerçek kullanıcı profile path'ini kullanmamalı.
- İzole save path kullanmalı.
- Test verilerini işlem sonunda temizlemeli.

---

## 13. Tam regresyon kapıları

Final stage manifest üzerinde çalıştır:

- EditMode: en az `564`, 0 FAIL
- PlayMode: en az `132`, 0 FAIL
- RoundProbe: `8/8`
- GoldProbe: `12/12`
- ArmorProbe: boş/idle makinede fiziksel `9/9`
- SpecialWeaponProbe: `10/10`
- EconomyUpgradeProbe: `11/11`
- ProfilePersistenceProbe: mevcut `14/14` + bütün yeni aşamalar
- LifecycleProbe: A/B
- Clean Library/import
- Release build
- Player smoke
- F0–F10 Git/repository safety
- Release DLL development-only seam taraması

ArmorProbe 8/9 bu kapanışta kabul edilmez.

ArmorProbe çalışırken:

- Blender renderı çalıştırma.
- Başka Unity instance çalıştırma.
- Ağır üretim işlemi çalıştırma.
- Makineyi mümkün olduğunca idle durumda tut.
- Testi geçmek için tolerans veya gameplay davranışını değiştirme.

Final testler stage edilecek kaynak manifestiyle birebir aynı içerikte çalışmalıdır. Final test sonrasında production veya test kaynağı değişirse etkilenen kapıları tekrar çalıştır.

---

## 14. Gerçek kullanıcı profili güvenliği

Her Unity Player çalışmasından önce:

- Gerçek kullanıcı profil dosyalarını ve sidecar dosyalarını listele.
- Path, size ve SHA-256 kaydı al.
- Repo dışında doğrulanmış backup oluştur.
- Testleri izole profile path'e yönlendir.
- Migration testini gerçek kullanıcı profile üzerinde çalıştırma.
- Gerçek profile'ın doğrulanmış kopyasını izole alanda kullan.
- WP-004–WP-008 build'lerini gerçek profile ile açma.
- V3 profile yazıldıktan sonra WP-009 build'ini gerçek profile üzerinde açma.

Son Player çalışmasından sonra:

- Gerçek kullanıcı profilini başlangıç durumuna geri getir.
- Başlangıç ve final path/size/SHA-256 değerlerini karşılaştır.
- `.writing`, `.bak`, `.lock`, `.corrupt`, `.restoring` veya V3 test artığı kalmadığını doğrula.
- Başlangıç envanteriyle tam eşleşme kanıtla.

Gerçek kullanıcı verisinde beklenmeyen değişiklik fark edilirse:

- Sonraki testi başlatma.
- Backup'ı koru.
- C-00 ve kullanıcıya bildir.
- Veri üstüne yazma.

---

## 15. Dokümantasyon

Yalnız doğrulanmış teknik gerçekleri yaz.

Güncellenmesi gereken yerler:

- `planning/04-IMPLEMENTATION-PLAN.md` içindeki son sekiz ID halkası açıklaması
- `ProfileData` anti-replay schema yorumları
- `ProfileService.ApplyRun` exactly-once açıklaması
- `RunSummary.CompletionId` açıklaması
- Eski kapasiteyi beklenen davranış kabul eden test açıklamaları
- WP-009 teknik raporuna correction/addendum
- WP-009H work-package durumu

Kusurun tarihini sessizce silme.

Correction kaydı şunları açıkça söylemelidir:

- WP-009'daki önceki implementation yalnız son sekiz ID için replay koruması sağlıyordu.
- Bu durum WP-009'un exactly-once acceptance sözleşmesiyle uyumsuzdu.
- WP-009H bounded lineage + monotonic authority ile bu açığı kapattı.
- V2 raw GUID namespace'i emekliye ayrıldı.
- Kullanıcı progression verileri migration sırasında korundu.

Yeni ürün DEC/REQ ekleme. Teknik düzeltmeyi ADR correction/addendum olarak kaydet.

Roadmap veya implementation status yalnız gerçek durumla güncellenmelidir. C-00 entegrasyonundan önce "tamamlandı ve entegre" yazma.

---

## 16. Kapsam dışı

Bu pakette yapılmayacaklar:

- XP miktarı veya curve değişikliği
- Unlock seviyesi değişikliği
- Achievement içeriği/eşiği değişikliği
- Gold/economy değişikliği
- Upgrade/reroll değişikliği
- UI/UX redesign
- Yeni oyuncu ekranı
- Run history/archive
- B5 abort ürün kararı
- SaveFailed için otomatik retry özelliği
- Cloud save
- Account
- Leaderboard
- Netcode
- Save slot
- Encryption veya anti-cheat
- Yeni silah
- Yeni karakter
- Yeni upgrade branch
- Yeni enemy
- Eski build geri uyumluluk özelliği
- Ayrı `.v1.json` backup özelliği
- Quit sırasında çift save devri
- SaveLock footer tasarım devri
- Directory fsync devri
- ArmorProbe tolerans veya davranış değişikliği
- AP-001/AP-002/AP-003/AP-005/AP-015
- Unity/URP/package değişikliği
- Remote/push

PrototypeOnly sayıları ve mevcut oyun dengesi değişmemelidir.

---

## 17. Kritik durma koşulları

Yalnız aşağıdaki durumlardan biri oluşursa kullanıcı onayı iste:

- Gerçek exactly-once ancak sınırsız completion/run geçmişi tutularak sağlanabiliyorsa.
- O(1) çözüm B5 abort davranışını değiştirmeyi gerektiriyorsa.
- V1/V2 progression verisi kayıpsız migrate edilemiyorsa.
- Existing kullanıcı save'ini resetlemek veya silmek gerekiyorsa.
- Save failure semantiğinin kullanıcıya görünen davranışını değiştirmek gerekiyorsa.
- Yeni ürün, ekonomi, UI veya progression kararı zorunluysa.
- Unity/URP/package sürümü değişikliği gerekiyorsa.
- AP veya başka WP kapsamına yazmak gerekiyorsa.
- Başlangıç main/tag/manifest doğrulanamıyorsa.
- Gerçek kullanıcı profile güvenli biçimde korunamıyorsa.

Sınıf adı, token serialization biçimi, test seam'i, lock implementation ve migration helper gibi kapsam içi mühendislik kararlarında kullanıcıyı durdurma; S-01 ve S-10 ile çöz.

---

## 18. Git ve worktree disiplini

- İzole worktree kullan.
- S-04 tek writer ve commit owner.
- Main'e doğrudan yazma.
- Mevcut `wp-009-m2-profile-save` etiketini değiştirme.
- Başka worktree'ye yazma.
- `git add -A` kullanma.
- İncelenmiş açık stage listesi kullan.
- Generated, build, Library, Temp, log, secret, user save veya `.claude` dosyası commit etme.
- Unity `.meta` ve GUID bütünlüğünü doğrula.
- Başlangıç ve final path/size/SHA-256 manifestleri üret.
- Final commit ağacı ile final manifest birebir eşleşmeli.
- Tek atomik package commit üret.
- Merge, rebase, reset veya clean yapma.
- Remote ekleme.
- Push yapma.
- Main fast-forward işlemini yalnız C-00 yapar.
- Yeni etiketi yalnız C-00, S-10 post-commit PASS sonrasında oluşturur.
- Worktree'yi ayrıca yetki verilmeden silme.

Commit mesajı kusuru açıkça belirtmelidir. Yalnız "capacity artırıldı" gibi eksik veya yanıltıcı açıklama kullanma.

---

## 19. Zorunlu nihai rapor

S-04 nihai raporda en az şunları sunmalıdır:

1. Başlangıç HEAD/tag/manifest/ağaç doğrulaması
2. Kusurun eski main'de kırmızı yeniden üretimi
3. Eski kapasite testinin neden yanlış olduğu
4. S-01 tarafından kabul edilen yeni invariant
5. Yeni identity authority mimarisi
6. Anti-replay state'in neden O(1) olduğu
7. Neden ring/LRU/Bloom/unbounded history kullanılmadığı
8. Token issuance ve validation akışı
9. Transaction/consume/retry akışı
10. Abort invalidation akışı
11. Profile reset lineage akışı
12. Concurrent writer lock mimarisi
13. Lock failure'ın neden fail-closed olduğu
14. Process/reload/relaunch kanıtları
15. V1/V2→V3 migration matrisi
16. Legacy GUID fail-closed davranışı
17. Save/crash/fault-injection sonuçları
18. Abort sıfır-delta kanıtı
19. EditMode/PlayMode sonuçları
20. Bütün probe sonuçları
21. ArmorProbe idle 9/9
22. Release build/smoke
23. Release DLL seam taraması
24. Gerçek kullanıcı profile önce/sonra hash sonucu
25. Değiştirilen dosyalar
26. PrototypeOnly sayım ve drift sonucu
27. Yeni DEC/REQ sayısı: 0
28. Başlangıç ve final manifestleri
29. Stage listesi
30. Git F0–F10
31. S-01 koşulsuz PASS
32. S-10 bağımsız PASS
33. Package commit hash
34. C-00 fast-forward ve tag talimatı
35. Kapsam dışı devirlerin değişmediği

"Tamamlandı" ifadesini yalnız bütün zorunlu kapılar PASS olduktan sonra kullan.

---

## 20. Definition of Done

WP-009H yalnız aşağıdakilerin tamamı sağlandığında tamamlanmış kabul edilir:

- 9+ run replay kusuru önce kırmızı testle kanıtlandı.
- Kök neden sekiz elemanlı ID halkası olarak doğrulandı.
- Sonlu completion üyelik listesi replay otoritesi olmaktan çıkarıldı.
- Yalnız kapasite büyütülmedi.
- Başka ring/LRU/cache kullanılmadı.
- Bloom filter kullanılmadı.
- Sınırsız ID listesi veya run history oluşturulmadı.
- Anti-replay state O(1).
- Profile lineage + monoton authority veya eşdeğer kanıtlı çözüm uygulandı.
- En az 64 completion sonrasında ilk/orta/son replay'ler reddedildi.
- Aynı test reload ve gerçek process relaunch sonrasında PASS.
- Profile dosya boyutu koşu sayısıyla doğrusal büyümedi.
- Duplicate uygulamada RAM ve disk hash'i değişmedi.
- Duplicate Changed/save/unlock/achievement bildirimi üretmedi.
- Aynı içerikli iki gerçek ayrı koşu tam birer kez uygulandı.
- Random, stale, aborted, legacy, başka lineage ve reset öncesi tokenlar fail-closed.
- SaveFailed → retry → Applied → duplicate sırası doğru.
- Stats ve replay state aynı transaction içinde kaydedildi.
- V1/V2→V3 migration kayıpsız.
- Future version overwrite edilmedi.
- WP-009 crash-safe save ve backup recovery davranışı gerilemedi.
- Concurrent lock acquisition fail-closed.
- İki gerçek process'ten yalnız biri writer.
- Read-only process save/reset/commit yapamadı.
- Profile reset yeni lineage oluşturdu.
- B5 abort davranışı değişmedi.
- XP/economy/unlock/achievement/UI davranışı değişmedi.
- PrototypeOnly değerleri değişmedi.
- Yeni ürün DEC/REQ oluşturulmadı.
- EditMode ve PlayMode tam PASS.
- Bütün mevcut probe'lar tam PASS.
- Genişletilmiş ProfilePersistenceProbe tam PASS.
- ArmorProbe idle ortamda 9/9.
- Release build/smoke PASS.
- Release DLL'de test/debug identity issuer yok.
- Gerçek kullanıcı profile başlangıç hash'leriyle tam eşleşti.
- Final manifest stage ve commit ağacıyla birebir.
- S-01 koşulsuz PASS.
- S-10 bağımsız PASS, FAIL 0, BLOCKED 0.
- Çalışma ağacı temiz.
- Remote/push yok.

Bütün kapılar PASS olduğunda S-04 tek package commit'i üretir ve C-00'a fast-forward entegrasyonu için teslim eder.

C-00 entegrasyonundan önce main'i veya etiketi değiştirme.
