WP-009 — M2 PERSISTENT PROFILE, XP, STATISTICS & LOCAL SAVE HARDENING

PAKET SAHİBİ
S-04 — Economy, Progression & Save

ANA KOORDİNASYON
C-00 — Deaths Demolition Main Coordination

KAYIT NOTU
Aşağıdaki metin kullanıcının 2026-09-14 tarihinde S-04'e verdiği yürütme promptunun aynısıdır (paket kaydı). Prompt yeni ürün DEC/REQ maddesi icat etmemeyi buyurur; teknik invariant, migration ve transaction kararları planning/04 §WP-009 altında ADR/teknik not olarak kaydedilir.

---

# WP-009 — M2 Persistent Profile, XP, Statistics & Local Save Hardening

## OTURUM VE SAHİPLİK

Bu çalışma paketinin birincil sahibi:

S-04 — Economy, Progression & Save

S-04; profil ilerlemesi, XP, kalıcı istatistikler, unlock entitlement'ları, save transaction'ları, migration ve immutable reward receipt'in tek yazma sahibidir.

Bu paketi Kadro sistemiyle yürüt.

Başlangıçta görevin gerektirdiği uzmanları belirle ve çalışmaya dahil et. En az şu uzmanlıklar temsil edilmelidir:

- Prompt Engineer
- Context Engineer
- PRA / Prompt Risk & Accuracy
- Progression Systems Designer
- Save/Persistence Architect
- Data Migration Specialist
- Unity Gameplay Engineer
- Defensive Programming Specialist
- UI Data-Binding Specialist
- Automated Test Engineer
- Fault-Injection / Reliability QA
- Git and Version-Control Specialist

Uzman ve ajan sayısını görevin gerçek ihtiyacına göre artırabilirsin. Gereksiz ajan ekleme; fakat profesyonel ve eksiksiz üretim için gereken uzmanlığı da eksiltme.

S-01 mimariyi, S-09 kullanıcıya sunulan profil ve Results yüzeylerini, S-10 bağımsız test/Git/teslim kapılarını incelemelidir. S-02 ve S-03 yalnız RunSummary, RunDirector, RunEvents ve koşu kimliği sınırları için salt okunur danışman olarak kullanılabilir.

S-04 dışındaki ajanlar, açıkça yazma yetkisi verilmedikçe kaynak kodu veya paket dosyalarını değiştiremez. Tek writer ve tek commit owner S-04'tür.

Ajanlar görev tamamlanana kadar kesintisiz çalışmalıdır. Yalnızca ürün davranışını değiştiren, kullanıcı kararını gerektiren, veri kaybı riski taşıyan veya mevcut kapsamla çözülemeyen kritik konularda durup onay iste.

Yeni ürün kararı verme. Sayısal denge değerlerini kesinleştirme.

---

## 1. PAKET KİMLİĞİ

Paket:

WP-009 — M2 Persistent Profile, XP, Statistics & Local Save Hardening

Başlangıç noktası:

- main HEAD:
  0e2514a9691a9b92831d14107621905b7fd5cae9
- Başlangıç etiketi:
  wp-008-m2-economy-upgrades
- Başlangıç tracked file sayısı:
  803
- EditMode:
  474/474
- PlayMode:
  127/127
- WP-008 final manifest:
  06806b50f42f62f1d630c2a8d7d38b13a00ba818d6013340bb6a0f55809cbe90

Önerilen izole çalışma dalı/worktree adı:

wp-009-m2-persistent-profile-save

Paket başarıyla tamamlandıktan sonra C-00 tarafından kullanılacak etiket:

wp-009-m2-profile-save

Başlangıçta aşağıdakileri bağımsız olarak doğrula:

- main HEAD
- başlangıç etiketi
- temiz çalışma ağacı
- tracked file sayısı
- manifest eşleşmesi
- Unity ve URP sürümleri
- EditMode ve PlayMode başlangıç sonuçları
- mevcut bütün probe sonuçları
- AP-002 ve diğer worktree'lerin izolasyonu

Doğrulama sonuçlarını kanıt dosyalarına kaydet. Başlangıç durumu verilen bilgilerle uyuşmuyorsa kullanıcıya ürün kararı sormadan önce C-00 ve S-10 ile teknik olarak incele.

---

## 2. BAĞLAYICI ÜRÜN KARARLARI

Aşağıdaki kararlar kullanıcı tarafından onaylanmıştır ve değiştirilemez.

### 2.1 Yerel profil

- Oyun tamamen offline oynanabilir.
- Profil, açılan içerikler, başarımlar ve istatistikler yerel olarak kaydedilir.
- İnternet, hesap, online servis veya bağlantı bağımlılığı bulunmaz.
- Oyuncunun tek bir yerel profili vardır.
- Aynı anda yalnızca bir aktif koşu bulunur.
- Bulut kayıt, leaderboard, hesap sistemi ve online profil bu paketin kapsamı dışındadır.

### 2.2 Koşu ve kalıcılık sınırı

Koşu boyunca geçerli olup koşu sonunda sıfırlananlar:

- Gold
- Satın alınan upgrade'ler
- Silah geliştirmeleri
- Kurulan build
- Run-scoped ekonomi durumu

Kalıcı kalanlar:

- Profile level
- Profile XP
- Highest Round
- Achievements
- Açılmış silahlar
- Açılmış geliştirme dalları
- Genel ve düşman versiyonu bazındaki istatistikler
- Diğer onaylanmış lifetime istatistikleri

Run gold, run build veya koşu upgrade'leri hiçbir şekilde kalıcı profil JSON'una sızmamalıdır.

### 2.3 Profil seviyesinin etkisi

Profil seviyesi:

- Oyuncunun deneyimini ve kalıcı ilerlemesini gösterir.
- Yeni silahların açılmasında kullanılabilir.
- Yeni geliştirme dallarının açılmasında kullanılabilir.
- Onaylanmış diğer seçeneklerin erişim kapısı olabilir.

Profil seviyesi şunları sağlamaz:

- Doğrudan hasar
- Doğrudan sağlık
- Başlangıç gold'u
- Savunma bonusu
- Ateş hızı
- Hareket hızı
- Başka herhangi bir doğrudan oyun gücü

Yüksek seviyeli profil ile Level 1 profilin aynı silah ve koşu koşullarındaki başlangıç health, damage ve gold değerleri eşit kalmalıdır.

### 2.4 XP kaynakları

Yalnızca şu iki kaynak profil XP'si verir:

1. Kill XP
2. Tamamlanmış round XP'si

Kurallar:

- Yalnızca gerçekten tamamlanmış roundlar round XP verir.
- Oyuncunun öldüğü tamamlanmamış round, round XP vermez.
- Ölüm gerçekleşen tamamlanmamış rounddaki kill'ler istatistiklere eklenir.
- Bu kill'ler kill XP'ye de eklenir.
- Gold kazanmak XP vermez.
- Gold harcamak XP vermez.
- Upgrade satın almak XP vermez.
- Reroll yapmak XP vermez.
- Oynama süresi XP vermez.
- Run summary içindeki başka veya çelişkili bir toplam değer, bu iki kaynak dışında XP üretemez.

Temel invariant:

Total XP awarded = Completed Round XP + Kill XP

XP miktarları, XP eğrisi, level gereksinimleri, unlock seviyeleri ve maksimum seviye bu pakette ürün kararı haline getirilmeyecektir. Mevcut değerler yalnızca PrototypeOnly olarak kalır.

### 2.5 İstatistikler

En az aşağıdaki lifetime değerler korunmalıdır:

- Runs Played
- Rounds Completed
- Highest Round
- Total Kills
- Standard Enemy Kills
- Special Enemy Kills
- Elite Enemy Kills
- Ultra Enemy Kills
- Armor Pieces Broken
- Best Run XP
- Profile Level
- Current/Accumulated Profile XP
- Achievements
- Unlocked Weapons
- Unlocked Upgrade Branches

Total kill ile tier breakdown arasında açık, test edilmiş bir invariant bulunmalıdır.

Legacy veya bozuk bir profilde TotalKills değeri tier toplamından büyükse eksik kill'leri rastgele tier'lara dağıtarak veri uydurma. Kayıpsız teknik çözüm mümkün değilse bunu kritik veri-migration konusu olarak yükselt.

### 2.6 Results sunumu

Run Results ekranı aşağıdakileri tek ve düzenli sonuç yüzeyinde gösterebilmelidir:

- Ulaşılan round
- Toplam kill
- Düşman versiyonu başına kill
- Completed-round XP
- Kill XP
- Total XP
- Profil seviyesinin önceki ve sonraki durumu
- Level progress
- Yeni açılan içerikler
- Yeni başarımlar
- İlgili run istatistikleri

Unlock'lar otomatik gerçekleşir. Claim düğmesi veya ayrıca ödül toplama işlemi bulunmaz.

Results yalnızca o profil transaction'ı tarafından üretilmiş immutable receipt'i göstermelidir. UI kendi XP hesabını yapmamalı, profil deltasını tekrar oluşturmamalı ve authoritative save sahibi olmamalıdır.

Kullanıcıya görünen bütün metin İngilizce olmalıdır.

---

## 3. İZLENEBİLİRLİK

Mevcut karar ve gereksinimlerle izlenebilirliği koru:

- DEC-002
- DEC-006
- DEC-007
- DEC-008
- DEC-036
- DEC-038
- REQ-004
- REQ-018
- REQ-020
- REQ-021
- REQ-022
- REQ-023
- REQ-024
- REQ-025
- REQ-026
- REQ-027
- REQ-028
- REQ-049
- REQ-155

Yeni ürün DEC veya REQ maddesi icat etme.

Teknik invariant, migration ve transaction kararlarını ADR/technical note olarak kaydedebilirsin; bunları kullanıcı tarafından alınmış yeni oyun tasarımı kararları gibi sunma.

Her değişiklik için aşağıdaki zincir kurulmalıdır:

Approved requirement → implementation module → automated test/probe → evidence

---

## 4. MEVCUT TEKNİK BOŞLUKLAR

Başlangıç karakterizasyonu ile aşağıdaki boşlukları repo üzerinde doğrula. Kanıtlamadan varsayım olarak kabul etme.

### 4.1 Duplicate profile application

Mevcut RunSummary kalıcı bir completion identity taşımıyor.

ProfileService.ApplyRun aynı sonucu ikinci kez aldığında şu değerleri yeniden artırabiliyor:

- XP
- Kills
- Rounds
- Runs Played
- Diğer lifetime sayaçları

Mevcut duplicate testi yalnız unlock ve achievement string tekrarlarını kontrol ediyor olabilir.

Bu paket profile commit için durable exactly-once koruması sağlamalıdır.

### 4.2 UI guard'ın yetersizliği

PrototypeUiFlow.s_resultsScheduled yalnız RAM içi navigation guard'dır.

Bu alan:

- Profil işleminin exactly-once garantisi olamaz.
- Scene rebind sonrasında güvenilir değildir.
- Uygulama yeniden açıldığında kalıcı değildir.
- Duplicate RunEnded olayına karşı authoritative koruma değildir.

Profil commit otoritesi UI'dan ayrılmalıdır.

### 4.3 Save crash penceresi

Mevcut save işlemi primary dosyayı silip temp dosyasını taşıyorsa, bu iki işlem arasında primary save'in bulunmadığı bir crash penceresi oluşur.

Ayrıca:

- Orphan `.writing` dosyası reconcile edilmiyor olabilir.
- Doğrulanmış last-known-good backup bulunmayabilir.
- Primary bozulduğunda güvenilir recovery zinciri olmayabilir.
- "Atomic" yorumu gerçek işlem davranışıyla uyuşmayabilir.

### 4.4 Memory/disk ayrışması

ProfileService canlı ProfileData'yı disk yazımından önce mutate ediyorsa save failure sonrasında:

- RAM profili ilerlemiş,
- diskteki profil eski kalmış,
- retry/replay ise duplicate ilerleme üretmiş

olabilir.

Bu durum transaction sınırıyla kapatılmalıdır.

### 4.5 Yetkisiz XP fallback'i

Round XP ve kill XP sıfır olduğunda RunSummary.TotalXpEarned benzeri bir fallback kullanılıyorsa kaldırılmalı veya onaylı iki XP kaynağının dışına çıkamayacak şekilde güvenli hale getirilmelidir.

Summary içindeki negatif, çelişkili veya manipüle edilmiş alanlar ekstra XP üretemez.

### 4.6 Kalıcı entitlement

UnlockedWeapons ve UnlockedBranches diskte tutulduğu halde erişim yalnız mevcut level/config eşiklerinden hesaplanıyorsa, daha önce açılmış içerik config değişiminde yeniden kilitlenebilir.

Kazanılmış unlock listesi authoritative entitlement olmalıdır.

Level/config yalnızca yeni unlock kazanımını belirler. Önceden kazanılmış geçerli entitlement sessizce geri alınamaz.

### 4.7 Overflow ve patolojik girdiler

Aşağıdaki işlemlerde overflow ve geçersiz config güvenliği sağlanmalıdır:

- XP toplamları
- Kill toplamları
- Round toplamları
- Level-up döngüsü
- Tier kill toplamı
- Armor break toplamı
- Best Run XP
- Diğer kalıcı sayaçlar

Negatif değerler, aşırı büyük değerler, sıfır veya negatif XP requirement ve patolojik level config sonsuz döngü ya da wraparound oluşturmamalıdır.

---

## 5. UYGULAMA KAPSAMI

### A. Durable run-completion identity

Her tamamlanmış koşu sonucu için uygulama yeniden açıldığında da ayırt edilebilen opaque ve kalıcı bir completion identity oluştur.

Kurallar:

- Session-local RunDirector.RunId tek başına durable identity kabul edilmez.
- Aynı completion identity ikinci kez profile uygulanamaz.
- Aynı verilere sahip fakat farklı identity taşıyan iki gerçek koşu ayrı koşular olarak kabul edilir.
- Duplicate RunEnded, Results ekranının yeniden açılması, scene rebind veya retry ikinci profile commit üretemez.
- Teknik anti-replay metadata'sı oyuncuya gösterilen kalıcı run history değildir.
- Kalıcı run archive/history sistemi oluşturma.
- Kimlik üretimi ve saklama yöntemi Unity lifecycle ve uygulama yeniden açılışı için test edilmelidir.
- LastSavedUtc veya ekranda gösterilen timestamp transaction identity olarak kullanılmamalıdır.

Tek aktif koşu modelini aşan bir ürün sistemi kurma. Gerekli minimum idempotency metadata'sını kullan ve teknik tasarımı raporda açıkla.

### B. Transactional profile application

ProfileService içinde profile değişikliğini transaction olarak ele al.

Beklenen akış:

1. Run reward girdilerini doğrula.
2. Durable completion identity'yi kontrol et.
3. Mevcut profilden izole bir candidate state üret.
4. XP, stats, unlock ve achievement değişikliklerini candidate üzerinde hesapla.
5. Candidate state'i sanitize ve validate et.
6. Candidate state'i güvenli save zinciriyle diske yaz.
7. Yalnız başarılı persistence sonrasında candidate state'i authoritative canlı profil olarak yayınla.
8. O transaction'a ait immutable RunRewardReport/receipt üret.
9. Changed/UI olaylarını yalnız bir kez yayınla.

Save başarısızlığında:

- UI profili kaydedilmiş gibi göstermemeli.
- Canlı authoritative profil ile disk sessizce ayrışmamalı.
- Hata oyunu çökertmemeli.
- Aynı completion identity ile güvenli retry duplicate ilerleme üretmemeli.
- Run sonuçları ile kalıcı profile başarıyla işlenmiş değerler birbirinden dürüst biçimde ayrılmalıdır.

Mevcut public API'leri gereksiz yere bozma. Gerekiyorsa geriye uyumlu ve sınırlı additive API kullan.

### C. Crash-safe local save

Aynı dizinde çalışan doğrulanabilir bir transaction zinciri kur.

En az şu kavramları kapsamalıdır:

- Primary profile
- Same-directory temporary/writing candidate
- Doğrulanmış last-known-good backup
- Candidate serialize
- Flush/durable write
- Candidate read-back ve validation
- Platformun güvenli atomic replace/rename imkânı
- Recovery sırasında deterministik aday seçimi
- Bozuk dosyaların kanıt için korunması
- Valid primary'nin doğrulanmamış adayla overwrite edilmemesi

Test edilen hedef filesystem üzerinde şunlardan biri her zaman korunmalıdır:

- Eski eksiksiz profil
- Yeni eksiksiz profil

Yarım yazılmış, boş veya rastgele sıfırlanmış profil geçerli sonuç kabul edilmez.

"Atomic" kelimesini yalnız gerçek işlem zinciri ve fault-injection kanıtıyla destekleniyorsa kullan.

### D. Save schema ve migration

SaveVersion alanını gerçek migration zincirine dönüştür.

Paket sırasında şema değişmesi gerekiyorsa:

- Mevcut Version 1 profilini kayıpsız okuyabil.
- Version 1 → yeni sürüm migration'ını açık ve test edilebilir yap.
- Eksik alanları güvenli varsayılanlarla ele al.
- Future version dosyasını sessizce downgrade veya overwrite etme.
- Malformed, truncated ve empty JSON'u ayırt et.
- Primary bozuksa yalnız doğrulanmış last-known-good adaydan recovery yap.
- Bozuk orijinal bytes'ı kanıt/recovery incelemesi için koru.
- Migration idempotent olmalı.
- Bir profil birden fazla load/save döngüsünde veri kaybetmemeli.

Unknown achievement, weapon veya branch ID:

- Tanımlı olmayan içeriği açmamalı.
- Erişim kapıları yalnız tanımlı ve geçerli ID'leri kabul etmeli.
- Unknown ID'nin saklama/quarantine politikası teknik olarak belgelenmeli.
- Kullanıcıya ait geçerli ve bilinen entitlement'lar kaybolmamalı.

### E. XP, level ve stat güvenliği

Aşağıdakileri merkezi ve test edilebilir hale getir:

- Completed-round XP hesabı
- Kill XP hesabı
- Total awarded XP
- Çoklu level-up
- Profile progress
- Lifetime stats
- Per-tier kill stats
- Unlock evaluation
- Achievement evaluation

Saturating veya eşdeğer güvenli arithmetic kullan. Sayaçlar wraparound ile negatife dönemez.

Level-up işlemi patolojik config'te sonsuz döngüye giremez.

Mevcut XP sayıları, curve ve unlock seviyeleri:

- PrototypeOnly kalmalı.
- Final balance olarak tanımlanmamalı.
- Kullanıcı kararı veya request value olarak yanlış etiketlenmemeli.

Achievement altyapısını generic/sentetik test tanımlarıyla test et. Mevcut achievement isimlerini, açıklamalarını veya eşiklerini final içerik kabul etme.

### F. UI ve authoritative binding

S-09 ile salt okunur inceleme yaparak aşağıdaki yüzeyleri aynı authoritative profile state'e bağla:

- Results
- Profile
- Statistics
- Achievements
- Weapon Selection
- Upgrade branch offer filtering

Kurallar:

- Results immutable receipt okur.
- Profile/Statistics/Achievements lifetime aggregate okur.
- Selection ve upgrade offer sistemi aynı entitlement otoritesini kullanır.
- Unlock otomatik uygulanır.
- Claim API veya Claim button bulunmaz.
- Aynı unlock Results'ta yalnız onu ilk kez üreten başarılı transaction'da "new" olarak görünür.
- Save/reload sonrasında kazanılmış unlock açık kalır.
- Config threshold sonradan yükselse bile kazanılmış geçerli entitlement tekrar kilitlenmez.
- UI save işleminin authoritative gameplay sahibi değildir.
- Mevcut UI tasarımını yeniden tasarlama; yalnız doğruluk, binding ve save failure sunumunu düzelt.
- Bütün player-facing metinler İngilizcedir.

---

## 6. DEĞİŞTİRİLMEYECEK KONULAR

Aşağıdakiler kapsam dışıdır:

- Final XP curve
- Final kill XP miktarı
- Final round XP miktarı
- Maximum profile level
- Final unlock seviyeleri
- Final achievement listesi
- Achievement isimleri ve açıklamalarının içerik onayı
- Yeni silah veya geliştirme dalı eklemek
- Yeni karakter eklemek
- Kalıcı güç bonusu
- Başlangıç hasarı, sağlık veya gold bonusu
- Persistent run history/archive
- Currency sistemi
- Daily reward
- Battle pass
- Claim sistemi
- Cloud save
- Kullanıcı hesabı
- Online leaderboard
- Online servis
- Netcode
- Save slot sistemi
- Encryption veya anti-cheat
- Telemetry
- Aktif koşuyu crash/quit anında profile yazmak
- UI'nin görsel olarak yeniden tasarlanması
- Yeni UI art, ses veya animasyon üretimi
- Düşman tier ability'leri
- Tier debut roundları
- Unity/URP/package sürümleri
- AP-001/AP-002 üretim içerikleri
- Remote oluşturmak veya push yapmak

WP-008'deki reroll 1 veya 2 kart değişimini seçen 50/50 davranış:

- PrototypeOnly/noncanonical kalacaktır.
- WP-009 bunu değiştirmeyecektir.
- Bunu kullanıcı onaylı karar gibi planlama dosyalarına yazmayacaktır.

---

## 7. B5 ABORT SINIRI

Abort davranışı hâlâ açık ürün kararıdır.

Mevcut davranışı başlangıçta karakterize et ve regresyonla koru:

- Pause → Abort → Main Menu
- Results açılmaz
- ProfileService.ApplyRun çağrılmaz
- LastReport oluşmaz
- Profile Changed yayınlanmaz
- Runs Played artmaz
- Kill/stat/XP değişmez
- Disk profile değişmez

WP-009:

- Abort'u ölüm ile eşitlemeyecek.
- Abort sırasında profile yazmayacak.
- Yeni abort ödül semantiği belirlemeyecek.
- B5 hakkında canonical DEC/REQ oluşturmayacak.

Bu davranışı değiştirmeden paketi tamamlamak imkânsız hale gelirse dur ve kullanıcıya kritik karar olarak yükselt.

---

## 8. TEST MATRİSİ

### 8.1 XP ve istatistik testleri

Aşağıdakileri otomatik test et:

- Tamamlanan N round yalnızca bu roundların round XP'sini verir.
- Ölümle yarım kalan round kill ve kill XP verir fakat round XP vermez.
- Gold earned XP farkı oluşturmaz.
- Gold spent XP farkı oluşturmaz.
- Upgrade purchase XP farkı oluşturmaz.
- Reroll XP farkı oluşturmaz.
- Çok uzun playtime XP farkı oluşturmaz.
- Total XP = completed-round XP + kill XP.
- Çelişkili TotalXpEarned alanı ekstra XP üretemez.
- Negatif summary alanları ekstra XP üretemez.
- Standard/Special/Elite/Ultra girdileri parametrik test edilir.
- Bu testler yeni enemy implementation gerektirmez.
- Zero-run.
- Zero-kill run.
- Çoklu level-up.
- Boundary XP.
- Patolojik XP config.
- Overflow girdileri.
- Total/tier kill invariantı.

### 8.2 Exactly-once testleri

- Aynı durable completion identity aynı süreçte iki kez uygulanır: bütün profil deltaları yalnız bir kez.
- Statikler temizlenip profil diskten yeniden yüklendikten sonra aynı identity replay edilir: yine yalnız bir kez.
- Aynı verilere sahip iki farklı identity: iki ayrı koşu.
- Duplicate RunEnded: tek commit.
- Results yeniden açılması: ikinci commit yok.
- Scene rebind: ikinci commit yok.
- Duplicate işlem Changed event'ini tekrar yayınlamaz.
- Duplicate işlem unlock/achievement "new" listesini tekrar üretmez.
- Session-local RunDirector.RunId'nin tek başına kalıcı identity olmadığı kanıtlanır.

### 8.3 Save ve migration testleri

Enjekte edilebilir fault noktalarıyla test et:

- Temp write öncesi hata
- Temp write sırasında hata
- Flush sırasında hata
- Candidate validation hatası
- Replace öncesi hata
- Replace sırasında hata
- Backup adımında hata
- Replace sonrası doğrulama hatası
- Orphan writing/temp dosyası
- Missing primary
- Empty primary
- Truncated JSON
- Malformed JSON
- Future SaveVersion
- Eksik alan
- Negative/out-of-range sayaç
- Duplicate ID
- Corrupt primary + valid backup
- Valid primary + corrupt temp
- Valid primary + daha yeni fakat doğrulanmamış temp

Her yeniden açılışta ya eski eksiksiz profil ya yeni eksiksiz profil elde edilmelidir.

### 8.4 Unlock ve achievement testleri

- Level-up sonrası unlock otomatik gerçekleşir.
- Claim çağrısı gerekmez.
- Aynı unlock yalnız bir transaction'da "new" olarak görünür.
- Save/reload sonrası entitlement korunur.
- Config unlock threshold yükselse bile kazanılmış entitlement açık kalır.
- Kilitli branch WP-008 offer filtresine girmez.
- Açılmış branch offer sisteminde kullanılabilir.
- Kilitli/açılmış silah Selection ekranında authoritative profile göre görünür.
- Unknown ID hiçbir içeriği açmaz.
- Duplicate entitlement oluşturulmaz.
- Achievement testleri generic fixture kullanır.
- Achievement PrototypeOnly eşikleri final içerik haline gelmez.

### 8.5 Güç eşitliği testi

Level 1 profil ile yüksek seviyeli profil aynı run başlangıcında karşılaştırılır:

- Starting health eşit
- Starting damage eşit
- Starting gold eşit
- Başka bir doğrudan combat power bonusu yok

### 8.6 Abort testi

Abort öncesi ve sonrası:

- RAM profile hash
- Disk profile hash
- XP
- Stats
- Unlocks
- Achievements
- Results state

karşılaştırılır ve sıfır profil deltası kanıtlanır.

---

## 9. DEVELOPMENT-ONLY PROFILE PROBE

Development-only bir ProfilePersistenceProbe veya eşdeğer doğrulama yüzeyi oluştur.

Probe en az şunları gerçek Player çalıştırmasında kanıtlamalıdır:

1. Tamamlanan round XP verir.
2. Tamamlanmamış son round round XP vermez.
3. Ölüm gerçekleşen yarım rounddaki kill'ler ve kill XP korunur.
4. Gold ve playtime XP vermez.
5. Aynı completion identity iki kez uygulanmaz.
6. Uygulama yeniden açıldıktan sonra replay yine reddedilir.
7. Farklı identity taşıyan ikinci koşu uygulanır.
8. Level ve unlock diskten geri yüklenir.
9. Yeni unlock/achievement ikinci kez "new" olarak gösterilmez.
10. Interrupted write sonrasında eski veya yeni tam profil geri gelir.
11. Abort hiçbir profile delta üretmez.
12. İkinci temiz koşu birinci koşudan bağımsız doğru çalışır.

Probe:

- Release build davranışına sızmamalı.
- Release DLL'de development-only argüman/probe davranışı bulunmamalı.
- Gerçek kullanıcı profil yolunu kullanmamalı.
- İzole test path kullanmalı.
- Her testten sonra kendi verisini temizlemeli.

---

## 10. GERÇEK KULLANICI SAVE KORUMASI

Unity testi, Player probe veya manuel doğrulama çalıştırmadan önce gerçek kullanıcı profilinin:

- Yolunu belirle.
- Varsa binary/file SHA-256 değerini al.
- Güvenli backup oluştur.
- Test ve probe'ları izole path'e yönlendir.

Bütün doğrulamalar bittikten sonra:

- Gerçek profil dosyasının önceki ve sonraki SHA-256 değerini karşılaştır.
- Dosya yoksa yokluk durumunun değişmediğini kanıtla.
- Primary, temp, backup ve yan dosyalar bakımından gerçek kullanıcı alanında test artığı kalmadığını doğrula.

Gerçek kullanıcı profilini resetleme, overwrite etme veya test verisiyle değiştirme.

---

## 11. PLANLAMA DOSYASI DÜZELTMESİ

`planning/03-ROADMAP.md` ve `planning/04-IMPLEMENTATION-PLAN.md` dosyalarında WP-008 hâlâ "başladı" veya "yürütülüyor" olarak görünüyorsa yalnızca doğrulanmış gerçeklerle düzelt:

- WP-008 tamamlandı.
- main'e entegre edildi.
- Commit:
  0e2514a9691a9b92831d14107621905b7fd5cae9
- Tag:
  wp-008-m2-economy-upgrades
- Tracked file:
  803
- EditMode:
  474/474
- PlayMode:
  127/127
- Final manifest:
  06806b50f42f62f1d630c2a8d7d38b13a00ba818d6013340bb6a0f55809cbe90

Tarih, başarı veya kullanıcı kararı uydurma.

WP-009 için yalnız gerçek ilerleme durumunu kaydet. Tamamlanmadan "completed" yazma.

Dosyalara yalnız kullanıcı tarafından onaylanmış ürün kararlarını ekle. Öneri, varsayım veya pending tasarım fikrini karar gibi kaydetme.

---

## 12. REGRESYON KAPILARI

Başlangıç ve finalde şu mevcut doğrulamaları çalıştır:

- EditMode testleri
- PlayMode testleri
- RoundProbe 8/8
- GoldProbe 12/12
- ArmorProbe 9/9
- SpecialWeaponProbe 10/10
- EconomyUpgradeProbe 11/11
- Release build
- Player smoke
- Clean Library/import doğrulaması
- F0–F10 Git denetimi

Minimum final eşikleri:

- EditMode: en az 474 PASS, 0 FAIL
- PlayMode: en az 127 PASS, 0 FAIL
- Bütün eski probe'lar: mevcut sayılarıyla tam PASS
- Yeni ProfilePersistenceProbe: tam PASS
- Release build/smoke: PASS
- Gerçek kullanıcı profili: değişmemiş
- PrototypeOnly sayımı: açıklanmış ve beklenmeyen drift yok

Her yeni test için hangi REQ/DEC veya teknik invariantı kanıtladığını raporla.

---

## 13. PERFORMANS VE YAŞAM DÖNGÜSÜ

- Her frame disk IO yapma.
- Her frame JSON serialize/deserialize yapma.
- Her frame string allocation veya profil taraması ekleme.
- Unlock ve achievement değerlendirmesini yalnız ilgili profile transaction'larında çalıştır.
- Scene reload, statik reset, iki ardışık run ve uygulama yeniden açılışını test et.
- Birden fazla Player örneği açıldığında profile yazma sahipliğini güvenli hale getir.
- Bu sahiplik oyuncu, düşman ve damage sistemlerini gereksiz biçimde tek global Player'a bağlamamalıdır.
- Mevcut tek oyunculu ürün davranışı korunmalıdır.

---

## 14. AP-002 İZOLASYONU

AP-002, Codex DD-S05 üretim oturumunda ayrı staging alanında devam etmektedir.

WP-009 sırasında:

- AP-002 staging alanını okuma.
- AP-002 çıktısını Unity projesine alma.
- AP-002 dosyalarını değiştirme.
- AP-002 manifest veya raporlarını WP-009 commit'ine ekleme.
- Blender/Adobe/Higgsfield üretim sürecini tetikleme.
- AP-002 worktree/branch/commit geçmişine dokunma.

WP-009 yalnız mevcut Unity main tabanından ilerler.

---

## 15. GIT VE WORKTREE KURALLARI

- İzole worktree kullan.
- S-04 tek writer ve commit owner'dır.
- Başlangıç commit'ini doğrulamadan değişiklik yapma.
- Kullanıcıya ait mevcut değişiklikleri overwrite etme.
- Başka paketin worktree'sine yazma.
- Generated, build, log, Library, Temp, user-save, secret veya `.claude` içeriğini commit etme.
- Unity `.meta` ve GUID bütünlüğünü doğrula.
- Başlangıç ve final manifest üret.
- Commit ağacı ile final manifest birebir eşleşmeli.
- Tek, atomik ve açıklayıcı package commit üret.
- Merge commit oluşturma.
- main'e doğrudan commit atma.
- main fast-forward entegrasyonunu yalnız C-00 yapar.
- Final etiketi yalnız C-00 doğrulamasından sonra oluşturulur.
- Remote ekleme.
- Push yapma.
- Worktree temizliğini ayrıca yetki verilmeden yapma.
- F0–F10 Git kontrollerini S-10 bağımsız olarak çalıştırmalıdır.

---

## 16. KRİTİK DURMA KOŞULLARI

Yalnız aşağıdakilerden biri gerçekleşirse kullanıcı kararı iste:

- B5 abort davranışını değiştirmeden paket tamamlanamıyorsa
- Legacy kill verisini kayıpsız korumak teknik olarak mümkün değilse
- Gerçek kullanıcı profilinde geri alınamayacak veri kaybı riski oluşuyorsa
- Save schema değişikliği mevcut kullanıcı verisini güvenli migrate edemiyorsa
- Yeni ürün davranışı seçmek zorunlu hale geldiyse
- XP curve, unlock seviyesi veya achievement içeriğini kesinleştirmek gerekiyorsa
- AP-002 veya başka paketin alanına müdahale etmek zorunlu hale geldiyse
- Unity/URP/package sürümü değişikliği zorunluysa

Normal implementasyon ayrıntıları, test düzeni, sınıf yapısı ve güvenli refactor için kullanıcıyı durdurma; Kadro uzmanlarıyla çöz ve raporla.

---

## 17. TESLİM RAPORU

S-04 nihai raporda en az şunları sunmalıdır:

1. Başlangıç doğrulaması
2. Değiştirilen dosyalar
3. Mimari özet
4. Profile transaction sınırı
5. Durable completion identity tasarımı
6. Exactly-once kanıtları
7. Save/write/replace/recovery zinciri
8. Migration matrisi
9. XP ve stat invariantları
10. Unlock entitlement modeli
11. Results immutable receipt akışı
12. Save failure davranışı
13. Abort davranışının değişmediğine dair kanıt
14. Test sonuçları
15. Probe sonuçları
16. Release build/smoke
17. Gerçek kullanıcı save hash koruması
18. Performance/allocation değerlendirmesi
19. PrototypeOnly değişim raporu
20. REQ/DEC → code → test matrisi
21. Başlangıç ve final manifestleri
22. Git F0–F10 sonuçları
23. S-01 mimari kararı
24. S-09 UI-binding incelemesi
25. S-10 bağımsız teslim kararı
26. Ertelenen konular
27. Bir sonraki pakete teknik handoff
28. Commit hash
29. C-00 fast-forward için açık entegrasyon talimatı

"Tamamlandı" ifadesini yalnız bütün zorunlu kapılar PASS olduktan sonra kullan.

---

## 18. DEFINITION OF DONE

WP-009 yalnız aşağıdakilerin tamamı sağlandığında tamamlanmış kabul edilir:

- Onaylı profile, XP ve lifetime statistics kuralları uygulanmış.
- Round XP yalnız tamamlanmış roundlardan geliyor.
- Ölüm gerçekleşen yarım rounddaki kill ve kill XP korunuyor.
- Gold, upgrade, reroll ve playtime XP üretmiyor.
- Total XP yalnız completed-round XP + kill XP.
- Profile level doğrudan güç sağlamıyor.
- Run gold/build profile save'e sızmıyor.
- Profile commit süreç içinde exactly-once.
- Profile commit save/reload sonrasında exactly-once.
- Durable completion identity doğrulanmış.
- UI profile commit otoritesi değil.
- Results immutable receipt kullanıyor.
- Live memory ve disk save failure sırasında ayrışmıyor.
- Crash-safe replace/recovery zinciri fault injection ile kanıtlanmış.
- Version 1 migration kayıpsız.
- Future/corrupt/truncated save güvenli ele alınıyor.
- Kalıcı entitlement yeniden kilitlenmiyor.
- Unknown ID içerik açamıyor.
- Achievement ve unlock "new" bildirimi yalnız bir kez çıkıyor.
- Overflow ve patolojik config korumaları PASS.
- B5 abort davranışı değişmemiş.
- Reroll 50/50 davranışı PrototypeOnly/noncanonical kalmış.
- AP-002'ye dokunulmamış.
- EditMode ve PlayMode regresyonları tam PASS.
- Bütün mevcut probe'lar PASS.
- Yeni ProfilePersistenceProbe PASS.
- Release build/smoke PASS.
- Gerçek kullanıcı save'i değişmemiş.
- Planlama dosyalarındaki WP-008 durum drift'i doğrulanmış gerçeklerle düzeltilmiş.
- S-01 koşulsuz mimari PASS vermiş.
- S-09 UI-binding incelemesi PASS vermiş.
- S-10 bağımsız test ve Git kapısı PASS vermiş.
- Final manifest package commit ağacıyla birebir eşleşmiş.
- Çalışma ağacı temiz.
- Remote/push yapılmamış.

Bütün koşullar sağlandığında tek package commit üret ve C-00'a fast-forward entegrasyonu için teslim et. C-00 doğrulamasından önce main'i veya etiketi değiştirme.
