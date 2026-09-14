# WP-010 — M2 Enemy Versions & Hybrid Round Difficulty

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-14'te `S-03 — Enemies, AI, Rounds & Spawning` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-010-m2-enemy-versions` branch'inde (taban: `main` = `69daa7fd` = `wp-009h-profile-exactly-once`) yapılır; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Durum kaydı: `planning/04-IMPLEMENTATION-PLAN.md` → "WP-010".

---

WP-010 — M2 Enemy Versions & Hybrid Round Difficulty
OTURUM VE SAHİPLİK
Bu çalışma paketinin birincil sahibi:
S-03 — Enemies, AI, Rounds & Spawning
S-03; düşman versiyonu (tier) seçimi, garantili tanıtım, round zorluk ölçekleme, DEC-021 override sözleşmesi, tier stat profili, spawn kompozisyonu, tier görünüm sözleşmesi ve ilgili development-only probe'ların tek yazma sahibidir.
Bu paketi Kadro sistemiyle yürüt.
Başlangıçta görevin gerektirdiği uzmanları belirle ve çalışmaya dahil et. En az şu uzmanlıklar temsil edilmelidir:

* Prompt Engineer
* Context Engineer
* PRA / Prompt Risk & Accuracy
* Game Systems Designer (salt okunur: karar üretmez, karar metniyle kod arasındaki çelişkileri bulur)
* Spawn / AI Systems Engineer
* Deterministic Simulation Specialist
* Unity Gameplay Engineer
* Config / Data Contract Specialist
* Automated Test Engineer
* Game Test Engineer
* Git and Version-Control Specialist

Uzman ve ajan sayısını görevin gerçek ihtiyacına göre artırabilirsin. Gereksiz ajan ekleme; fakat profesyonel ve eksiksiz üretim için gereken uzmanlığı da eksiltme.
S-01 mimariyi ve entegrasyonu (Spawn, RunDirector, RunEvents, Core/Config korunan yüzeyleri), S-02 savaş sözleşmelerini (tier saldırı, hit reaction, ayrışma davranışlarının değişmediği), S-04 ekonomi ve profil sınırını (tier gold bonusu, tier kill istatistikleri diff 0), S-09 HUD/Results/Statistics üzerindeki tier satırlarını, S-10 bağımsız test/Git/teslim kapılarını incelemelidir.
S-03 dışındaki ajanlar, açıkça yazma yetkisi verilmedikçe kaynak kodu veya paket dosyalarını değiştiremez. Tek writer ve tek commit owner S-03'tür.
Ajanlar görev tamamlanana kadar kesintisiz çalışmalıdır. Yalnızca ürün davranışını değiştiren, kullanıcı kararını gerektiren, veri kaybı riski taşıyan veya mevcut kapsamla çözülemeyen kritik konularda durup onay iste.
Yeni ürün kararı verme. Sayısal denge değerlerini kesinleştirme. Tier'lara özel yetenek, saldırı veya davranış icat etme.
1. PAKET KİMLİĞİ
Paket:
WP-010 — M2 Enemy Versions & Hybrid Round Difficulty
Başlangıç noktası:

* main HEAD:
69daa7fd075371c5f2dcca98a79120a51f937a28
* Başlangıç etiketi:
wp-009h-profile-exactly-once
* Başlangıç tracked file sayısı:
827
* EditMode:
600/600
* PlayMode:
133/133
* WP-009H final manifest:
d5128ffe459bd73652fa9cc0ba4f39536d0975b62281f9551950654d87b0d90e
* Mevcut probe tabanı:
RoundProbe 8/8 · GoldProbe 12/12 · ArmorProbe 9/9 (idle) · SpecialWeaponProbe 10/10 · EconomyUpgradeProbe 11/11 · ProfilePersistenceProbe 29/29 · LifecycleProbe runA/runB

Önerilen izole çalışma dalı/worktree adı:
wp-010-m2-enemy-versions
Paket başarıyla tamamlandıktan sonra C-00 tarafından kullanılacak etiket:
wp-010-m2-enemy-versions
Başlangıçta aşağıdakileri bağımsız olarak doğrula:

* main HEAD
* başlangıç etiketi ve 11 etiketlik zincir
* temiz çalışma ağacı
* tracked file sayısı
* manifest eşleşmesi
* Unity 2022.3.22f1 ve URP 14.0.10
* EditMode ve PlayMode başlangıç sonuçları
* mevcut bütün probe sonuçları
* diğer worktree'lerin (wp-001 … wp-009h) izolasyonu
* gerçek kullanıcı verisi tabanı: `wp-009h/backup/userdata-postManual-wp009h-20260914T093543Z` (gerçek Player profili SaveVersion 3; Editor profili v1)

Doğrulama sonuçlarını kanıt dosyalarına kaydet. Başlangıç durumu verilen bilgilerle uyuşmuyorsa kullanıcıya ürün kararı sormadan önce C-00 ve S-10 ile teknik olarak incele.
Paketin amacı, Standard zombinin Special, Elite ve Ultra versiyonlarını ve DEC-021 hibrit round zorluğunu üretim standardında, deterministik, tek otoriteli ve testle kilitli bir sözleşmeye dönüştürerek M2 çıkış koşulunun son dilimini kapatmaktır. Bu paket bir denge paketi değildir; eksik sayısal değerler yalnız merkezi config içinde `PrototypeOnly` olarak tutulabilir ve nihai ürün kararı olarak belgelenemez.
2. BAĞLAYICI ÜRÜN KARARLARI
Aşağıdaki kararlar kullanıcı tarafından onaylanmıştır ve değiştirilemez.
2.1 Düşman ailesi ve versiyonlar

* Temel düşman ailesi zombidir.
* Düşmanın Standard, Special, Elite ve Ultra versiyonları vardır.
* Her versiyon zombi kimliğini korur.
* Aynı temel düşman roundlarla modüler biçimde güçlenir; zırh parçaları ve onaylı bileşenler aynı düşmanın üzerine eklenir.
* Versiyonların nihai görünümü ve özel yetenekleri henüz kararlaştırılmamıştır. Bu pakette versiyonlar yalnız stat profiliyle (can, hız, hasar, zamanlama, kütle, zırh olasılığı) ayrışır. Yeni yetenek, saldırı, davranış veya nihai görünüm üretilmez.

2.2 Garantili tanıtım

* Special, Elite ve Ultra versiyonlarının her biri belirlenen bir roundda garantili olarak en az bir kez görünür.
* Tanıtıldıktan sonra sonraki roundların ağırlıklı düşman havuzuna katılır.
* Tanıtım roundundan önce hiçbir koşulda spawn olmaz.
* Garanti, roundun havuz büyüklüğünden bağımsız olarak sağlanır; havuz tükenmek üzereyse kalan slotlar garantiye zorlanır.
* Tanıtım roundları (bugün config'te 3 / 5 / 8) ve ağırlıklar `PrototypeOnly` değerlerdir; nihai değerler kararlaştırılmamıştır.

2.3 Hibrit round zorluğu

* Round zorluğu hibrit sistemle ölçeklenir: temel artış formülle ilerler; belirli roundlarda zırh oranları ile Special, Elite ve Ultra dağılımları elle tanımlanan override'larla değişir.
* Override tablosunun kapsamı yalnız bu alanlardır: round bazlı zırh olasılıkları, round bazlı tier ağırlıkları, round bazlı zorunlu tier adetleri.
* Override satırlarının hangi roundlarda ve hangi değerlerle olacağı kararlaştırılmamıştır. Bu pakette sevk edilen config'in override tablosu boş kalır; mekanizma test ve probe'a enjekte edilen satırlarla kanıtlanır.
* Round havuzu, hedef aktif düşman sayısı ve teknik güvenlik tavanı WP-003 sözleşmesine göre aynen çalışır.

2.4 Ekonomi ve profil sınırı

* Düşmanın gold ödülü `Base Reward + Enemy Version Bonus + Spawn Armor Bonus` formülüyle spawn anında dondurulur (WP-008). "Version" = tier.
* Tier bazlı kill istatistikleri kalıcı profile yazılır; `TotalKills = Standard + Special + Elite + Ultra + UnattributedKills` invariantı korunur (WP-009).
* Gold, upgrade, reroll ve süre XP üretmez.
* Bu paket ekonomi, XP ve profil kurallarına dokunmaz.

2.5 Savaş sınırı

* Vuruş hissi ve hit reaction temel kalite hedefidir.
* Hafif darbelerin düşmanı durdurma eşiği kararlaştırılmamıştır; tier `StaggerSusceptibility` değerleri ve hit reaction sözleşmesi bu pakette değiştirilmez.
* Zombiler yalnız saldırı animasyonuna bağlı vuruş isabet ettiğinde hasar verir; temas hasar vermez. Tier'lar bu kuralı değiştirmez.

2.6 Dil

* Oyun dili ve oyuncuya görünen bütün metinler İngilizce'dir.

3. İZLENEBİLİRLİK
Mevcut karar ve gereksinimlerle izlenebilirliği koru:

* DEC-015
* DEC-019
* DEC-021
* DEC-022
* DEC-023
* DEC-029
* DEC-033
* DEC-043 / DEC-069
* REQ-060
* REQ-061
* REQ-062
* REQ-063 (yalnız görünüm sözleşmesi; nihai görsel M3)
* REQ-071
* REQ-072
* REQ-123 (dokunulmaz; karar bekliyor)
* REQ-148
* FLOW-001
* FLOW-006

Yeni ürün DEC veya REQ maddesi icat etme.
Tier seçim otoritesi, garanti yerleşimi, override önceliği, validation kuralları ve stat snapshot kararlarını ADR/technical note olarak kaydedebilirsin; bunları kullanıcı tarafından alınmış yeni oyun tasarımı kararları gibi sunma.
Her değişiklik için aşağıdaki zincir kurulmalıdır:
Approved requirement → implementation module → automated test/probe → evidence
4. MEVCUT TEKNİK BOŞLUKLAR
Başlangıç karakterizasyonu ile aşağıdaki boşlukları repo üzerinde doğrula. Kanıtlamadan varsayım olarak kabul etme; yanlış olan maddeyi raporda düzelt.
4.1 İki tier otoritesi
Enemies/EnemyTierScheduler.cs (EnemyFactory'de statik örnek; enjekte `EnemyRandom`; garantiyi roundun havuzu içinde rastgele bir indekse yerleştirir) ve Spawn/SpawnTierSelector.cs (gerçek koşuda `EnemySpawnDirector` tarafından kullanılan; `UnityEngine.Random.value` doğrudan; pacing ve "runningOut" tabanlı garanti) aynı gereksinimi (REQ-071) iki farklı semantikle iddia eder. F1 "Tier schedule" readout'u `EnemyFactory.Scheduler`'ı okur; bu gerçek koşunun otoritesi değildir. Hangi yolun gerçek koşuda çalıştığını, diğerinin çağıranlarını ve iki semantiğin hangi noktalarda ayrıştığını kanıtla.
4.2 DEC-021 override iskeleti boş ve boş olmaya zorlanmış
Core/Config/RoundConfigSection.cs içinde `RoundDifficultyOverride` (`OverrideArmorChances`, `OverrideTierWeights`, `OverrideForcedCounts`) tanımlıdır; `PrototypeConfig.ArmorChance` ve `TierSpawnWeight` override'ı formülden önce okur; Core/Editor/PrototypeCoreValidation.cs "DEC-021 override table is empty" ve `TryGetRoundOverride(5) == false` şartını zorunlu kılar. Öncelik, kısmi satır, aynı round için çakışan satır, tanıtılmamış tier'a ağırlık verilmesi ve `OverrideForcedCounts` tüketicisi test edilmemiştir; zorunlu adet mekanizmasının üretim çağıranı yoktur.
4.3 Determinizm
`SpawnTierSelector` RNG'si enjekte edilemez. WP-007/WP-008'de kurulan kalıp (enjekte RNG, saf politika sınıfı, aynı seed aynı sonuç) tier seçimine uygulanmamıştır. Aynı seed ile aynı round kompozisyonu üretildiği kanıtlanmamıştır.
4.4 Garanti sınır durumları
Havuz garanti sayısından küçük veya ona eşitken, round havuzu tükenirken, ölüm veya abort round ortasında geldiğinde, ikinci koşuda sayaç/garanti sıfırlamasında ve aynı roundda birden fazla tier tanıtıldığında davranış kanıtsızdır. Garantinin roundun ilk düşmanı olma zorunluluğu yoktur; fakat garantinin hiçbir koşulda kaybolmadığı kanıtlanmalıdır.
4.5 Round ölçekleme uygulaması
`RoundConfigSection.EnemyHealthMultiplier/EnemyDamageMultiplier` ile `EnemyTierSettings.HealthMultiplier/AttackDamage/MoveSpeed` çarpımının tek yerde ve spawn anında çözülüp çözülmediği, round geçişinde canlı düşmanın değişip değişmediği kanıtsızdır.
4.6 Tier kimliği ve görünen ad
`EnemyTierSettings.DisplayName` yer tutucudur (Runner / Guard / Executioner). İstatistik, gold, HUD, Results ve Statistics ekranlarının kanonik `EnemyTier` enum'unu mu yoksa DisplayName'i mi okuduğu doğrulanmamıştır; tutarsızlık riski vardır.
4.7 Tier ve zırh etkileşimi
Tier başına `ArmorChanceSettings` (round büyümesi ve tavan) ve override'ın WP-006 kompozisyonu ve WP-008 gold snapshot'ıyla tek yolda çözüldüğü doğrulanmalıdır.
4.8 ArmorProbe aşama 09
Diagnostics/ArmorProbe.cs aşama 09 (`gen0Total == 0`) makine yüküne duyarlıdır; WP-009/WP-009H'de yük altında tekrarlanan yanlış negatif üretmiştir. D-1 devri bu pakete aittir.
5. UYGULAMA KAPSAMI
A. Tek tier otoritesi
Gerçek koşunun tier otoritesi tek olmalıdır.
Kurallar:

* `EnemySpawnDirector`'ın kullandığı yol otorite kabul edilir; diğer yol kaldırılır ya da otoriteye delege eden ince adaptöre indirgenir. Kaldırılan/indirgenen her public üye raporda adıyla listelenir (WP-006 emsali).
* F1 "Tier schedule" readout'u ve debug spawn yolu aynı otoriteyi okur.
* Tier seçimi saf bir politika sınıfına ayrılır (ör. `TierSelectionPolicy`): girdi = round, havuz durumu (toplam, kalan, bu roundda spawn edilen), config, enjekte RNG; çıktı = tier ve "guaranteed" bayrağı. Politika Unity nesnesine bağımlı değildir.
* RNG enjekte edilebilir (`EnemyRandom` ya da eşdeğer arayüz); üretim yolu bugünkü kaynakla aynı dağılımı verir.
* Seçim yolunda kare başına arama, LINQ, string ve ayırma yoktur; statikler `ResetStatics` ile sıfırlanır.

B. Garantili tanıtım sözleşmesi
Kurallar:

* `IntroductionRound = r` olan tier r roundunda en az bir kez kesin görünür.
* Havuz garanti sayısından büyükse garanti rastgele/pacing'li bir konuma yerleşir; roundun ilk düşmanı olmak zorunda değildir.
* Havuz tükenmek üzereyse (kalan ≤ bekleyen garanti) kalan slotlar garantiye zorlanır.
* Havuz bekleyen garanti sayısından küçükse mümkün olan kadarı yerleştirilir ve durum raporda/log'da açıkça belirtilir; sessiz kayıp yoktur.
* Tanıtım öncesi roundda ilgili tier hiçbir koşulda (override dahil) spawn olmaz; tanıtılmamış tier'a ağırlık veya zorunlu adet veren override konfigürasyon hatasıdır.
* Aynı roundda birden fazla tier tanıtılıyorsa sıra deterministiktir.
* Round bitişi, ölüm, abort ve yeni koşu bekleyen garantiyi düşürür; ikinci koşu temiz başlar.
* Pause altında seçim ve garanti sayaçları ilerlemez.

C. DEC-021 hibrit sözleşmesi
Kurallar:

* Öncelik: override satırı > formül. Satır kısmi olabilir (yalnız zırh, yalnız ağırlık, yalnız zorunlu adet); tanımsız alan formüle döner.
* Aynı round için birden fazla satır konfigürasyon hatasıdır.
* Zırh olasılıkları [0, 1] aralığında; zorunlu adet toplamı roundun havuzunu aşamaz; tanıtılmamış tier'a ağırlık/adet verilemez.
* `OverrideForcedCounts` tüketicisi kurulur: round havuzunda tier başına zorunlu adet, garantiyle birleşik ve havuzu aşmayan biçimde uygulanır.
* `PrototypeCoreValidation`'daki "tablo boş olmalı" şartı, "satırlar (varsa) geçerli" şartıyla değiştirilir.
* Sevk edilen `PrototypeConfig.asset` override tablosu bu pakette boş kalır. Boş tablo = formülle bit-eşit davranış (parite testi).
* Formül tarafı (`PoolBase`, `PoolPerRound`, `PoolPowerScale`, `PoolPowerExponent`, `ActiveBase`, `ActivePerRound`, `ActiveCap`, `AbsoluteSafetyCeiling`, `HealthMultiplierPerRound`, `DamageMultiplierPerRound`) değişmez; monotonluk ve sınır testleri eklenir.

D. Tier stat profili ve round ölçekleme
Kurallar:

* Tier stat'ları (can çarpanı, hız, hasar, telegraph/active/recovery/range/cooldown, gövde yarıçapı, kütle, `StaggerSusceptibility`) ve round çarpanları spawn anında tek yerde çözülür ve düşmanın yaşamı boyunca değişmez (`EnemyRewardSnapshot` kalıbıyla `EnemyStatSnapshot` ya da eşdeğeri). Round geçişi canlı düşmanı değiştirmez.
* Kanonik kimlik `EnemyTier` enum'udur; `DisplayName` yalnız kozmetik ve `PrototypeOnly`'dir. İstatistik, gold, HUD, Results ve Statistics enum'u okur; okumuyorsa düzeltilir.
* Yeni yetenek, yeni davranış, yeni saldırı yoktur. Saldırı, hit reaction ve ayrışma sözleşmeleri (WP-002) diff 0 kalır; S-02 doğrular.
* Havuzdan yeniden çıkan düşman önceki tier'ın stat'ını taşımaz (WP-006 `Armor.Clear()` emsali).

E. Görünüm sözleşmesi
Kurallar:

* `IArmorVisual` emsaliyle tier görünümü için bir sözleşme (ör. `IEnemyVersionVisual`: tier → placeholder siluet/palet/ölçek) tanımlanır; mevcut `ZombieAppearance` uygular.
* Mevcut placeholder görünüm davranışı korunur (parite); nihai model, kıyafet ve animasyon M3'tedir.
* Sözleşme yalnız M3 için bağlama noktasıdır; yeni sanat üretilmez.

F. Tier kill istatistiği ve gold tutarlılığı

* Tier kill'lerinin `RunStatsTracker` → `RunSummary` → profil istatistiklerine (Standard/Special/Elite/Ultra, `UnattributedKills` 0) doğru aktığı ve gold version bonusunun tier'a göre doğru olduğu yalnız doğrulanır. Ekonomi ve profil kodu diff 0.

G. ArmorProbe aşama 09 ölçüm sertleştirmesi (D-1)

* Diagnostics/ArmorProbe.cs aşama 09: ölçüm öncesi `GC.Collect()` ve kısa ısınma; ölçüm penceresine bir gen0 toplaması denk gelirse pencere bir kez yenilenir; şart yığın deltası (pozitif kontrollü) ile birlikte değerlendirilir.
* Aşama 01–08 ve üretim kodu değişmez. Sonuç, makine boş olmadan da tekrarlanabilir olmalıdır; bu paketin final zincirinde ArmorProbe 9/9 idle şartı olmadan alınır.

H. Belgeleme

* planning/03-ROADMAP.md ve planning/04-IMPLEMENTATION-PLAN.md: WP-009 ve WP-009H durum satırları "Tamamlandı ve `main`'e entegre" (commit, etiket, kapılar); WP-010 kaydı (durum, amaç, kapsam, DEC/REQ → modül → test tablosu, ADR-010-x teknik notlar); D-1 devir satırının kapanış kaydı.
* `README` ve `PROTOTYPE-ONLY.md` sayımı güncellenir.
* Yeni DEC/REQ eklenmez; karar metinleri değişmez.

6. DEĞİŞTİRİLMEYECEK KONULAR

* Tier özel yetenekleri, saldırıları ve davranışları (karar yok).
* Nihai tier görünümü, animasyon çeşitliliği (REQ-064), ses ve VFX (M3).
* Hit reaction sözleşmesi, `StaggerSusceptibility` değerleri ve REQ-123 marjı.
* Spawn noktası seçimi, ağırlıkları ve FLOW-006 politikası (WP-003) — yalnız tier sınırında dokunulur.
* Round formülü sayıları ve DEC-021 override satır değerleri.
* Gold, upgrade, reroll, XP, unlock, achievement ve profil kuralları (WP-008/WP-009/WP-009H diff 0).
* Special Weapon (WP-007).
* B5 abort davranışı.
* HUD/Results/Statistics tasarımı (S-09 yalnız mevcut tier satırlarının doğruluğunu inceler).
* Packages, ProjectSettings, asmdef, sahne, prefab, Unity/URP sürümü.
* Remote, push, yayın.

7. ÖZEL YETENEK SINIRI
REQ-062 "her versiyon kendi görünüm ve özel yeteneğiyle ayrılabilir" der; yetenekler kararlaştırılmamıştır. Bu paket:

* Yetenek uydurmaz.
* Yetenek için altyapı, arayüz veya boş kanca eklemez (YAGNI; gelecek paket kendi sözleşmesini getirir).
* Stat profiliyle ayrışmayı deterministik ve testli hale getirir.

Kullanıcı yetenekleri kararlaştırdığında ayrı bir paket açılır.
8. TEST MATRİSİ
8.1 Politika testleri (EditMode, saf)

* Aynı seed aynı kompozisyon (en az 3 seed × 3 round).
* Tanıtım öncesi roundda ilgili tier 0 (1000 çekiliş).
* Tanıtım roundunda garanti tam bir kez; fazlası ağırlıktan.
* Garanti konumu her zaman ilk düşman değil (dağılım testi).
* Havuz = 1 ve havuz = 2 durumlarında zorlama.
* Havuz < bekleyen garanti sayısı: mümkün olan kadarı ve uyarı; sessiz kayıp yok.
* Aynı roundda iki tanıtım: deterministik sıra.
* Havuz tükenirken bekleyen garanti zorlanır.
* Ağırlık büyümesi ve tavanı monoton.
* Boş override tablosu = formül paritesi (aynı seed ile bit-eşit çekiliş dizisi).
* Override: tam satır, kısmi satır, yalnız zırh, yalnız ağırlık, yalnız zorunlu adet.
* Override: aynı round iki satır → hata; tanıtılmamış tier'a ağırlık/adet → hata; olasılık aralık dışı → hata; zorunlu adet > havuz → hata.
* Zorunlu adet + garanti birleşimi havuzu aşmaz.
* Formül monotonluk/sınır: round 1, round 50, int sınırları, negatif/NaN config girdileri.
* Stat snapshot: spawn'da dondurulur; round çarpanı değişse de snapshot değişmez.
* `DisplayName` bağımsızlığı: istatistik/gold/UI enum otoritesini okur.
* Kaynak taraması: seçim yolunda `UnityEngine.Random`, `FindObjectOfType`, LINQ ve kare başı string yok.
* ArmorProbe 09 ölçüm yardımcısı için pozitif kontrol testi.

8.2 Sahne testleri (PlayMode, gerçek Run sahnesi, Metal)

* F1 "Skip to next round" ile tanıtım roundlarında (config: 3 / 5 / 8) ilgili tier gerçek spawn'da en az bir kez.
* Tanıtım öncesi roundda ilgili tier 0.
* Havuz tükenirken bekleyen garanti zorlanır.
* Ölüm round ortasında: bekleyen garanti düşer; ikinci koşu temiz; abone ve statik sayıları koşu 1 = koşu 2; `LifecycleHardeningTests` tablosu güncel.
* Round geçişinde canlı düşman stat'ı sabit.
* Tier kill → `RunStatsTracker` → `RunSummary` → profil özeti tutarlı; `UnattributedKills` 0.
* Test-only config klonuna enjekte override satırı gerçek spawn'da uygulanır; asset dokunulmaz.
* Pause altında seçim/garanti donar.
* Havuzdan yeniden çıkan düşman önceki tier'ın stat'ını taşımaz.

8.3 Ekonomi ve profil paritesi

* Tier gold version bonusu WP-008 ile birebir (parametrik dört tier).
* Profil istatistik alanları WP-009 ile birebir; kayıt şeması değişmez.

9. DEVELOPMENT-ONLY ENEMY VERSION PROBE
Development-only bir `EnemyVersionProbe` (`-ddEnemyVersionProbe <outDir> -ddProfilePath <izole>`) oluştur. Probe en az şunları gerçek Player çalıştırmasında kanıtlamalıdır:

1. Tanıtım öncesi roundda tier yok.
2. Tanıtım roundunda en az bir garanti; garanti roundun ilk düşmanı değil (N tekrar).
3. Küçük havuzda zorlama.
4. Tanıtım sonrası ağırlıklı dağılımda her tanıtılmış tier'ın sıfır olmayan payı.
5. Enjekte override satırı: ağırlık, zırh olasılığı ve zorunlu adet uygulanır; satır kaldırılınca formül geri döner.
6. Aynı seed aynı kompozisyon.
7. Stat snapshot round geçişinde sabit.
8. Ölüm ortasında bekleyen garanti düşer; ikinci koşu temiz.
9. Tier kill istatistiği ve gold version bonusu tutarlı.
10. Abone ve statik sayıları koşu 1 = koşu 2; duplicate director/selector 0.

Probe:

* Release build davranışına sızmamalı; release DLL'de probe/argüman adı 0 (ASCII + UTF-16, pozitif kontrollü tarama).
* Gerçek kullanıcı profil yolunu kullanmamalı; izole path kullanmalı; kendi verisini temizlemeli.
* Makine yükünden bağımsız deterministik olmalı (GC pencere ölçümü varsa pozitif kontrollü yığın deltası).

10. GERÇEK KULLANICI SAVE KORUMASI

* Gerçek Player profili SaveVersion 3'tür; taban `wp-009h/backup/userdata-postManual-wp009h-20260914T093543Z`.
* Her Editor/Player koşusundan önce `userdata.sh backup`; probe'lar yalnız izole profil.
* Release smoke (e1/e4/e5) gerçek profil yolunu kullanır ve yalnız `LastSavedUtc`/oturum sayaçlarını değiştirir; bunu rapora baştan yaz.
* Son Player/Unity koşusundan sonra `userdata.sh restore` + hash 4/4 + yan dosya envanteri (`.bak` ve `.lock` tabanın parçasıdır).
* Eski build'ler (wp-004 … wp-009 verify-build-*) gerçek profille açılmaz.
* Gerçek kullanıcı profilini resetleme, overwrite etme veya test verisiyle değiştirme.

11. PLANLAMA DOSYASI DÜZELTMESİ

* WP-009 ve WP-009H durum satırları "Tamamlandı ve `main`'e entegre" (WP-009: `45506612`, `wp-009-m2-profile-save`; WP-009H: `69daa7fd`, `wp-009h-profile-exactly-once`; kapı sonuçları).
* WP-010 kaydı: durum, yürütme promptu yolu, amaç, kapsam (A–H), kapsam dışı, çıkış koşulları, DEC/REQ → modül → test tablosu, ADR-010-1 (tek otorite), ADR-010-2 (garanti sözleşmesi), ADR-010-3 (override önceliği/validation), ADR-010-4 (stat snapshot).
* D-1 ArmorProbe 09 devir satırı kapanış kaydı.
* M2 çıkış koşullarının durumu tablosu (M1 emsali): beş dilim + hotfix kanıtlarıyla; karar bekleyenler ayrı listede; karar yazılmaz.
* Yeni DEC/REQ yok; karar/gereksinim metinleri değişmez; Türkçe belgelerde oyun dilinin İngilizce olduğu kuralı korunur.

12. REGRESYON KAPILARI
Tam zincir paket başına bir kez, sıfırdan Library ile:

* Import 0 hata.
* EditMode: taban 600 + yeni testler, kayıp 0.
* PlayMode: taban 133 + yeni testler, gerçek Metal, `-nographics` yok.
* Release build; e1/e4 Player smoke; e5 bayrak sızıntısı.
* Development build.
* RoundProbe 8/8 · GoldProbe 12/12 · ArmorProbe 9/9 (§5.G sonrası, idle şartı olmadan) · SpecialWeaponProbe 10/10 · EconomyUpgradeProbe 11/11 · ProfilePersistenceProbe 29/29 · EnemyVersionProbe 10/10 · LifecycleProbe runA/runB.
* Release DLL sembol taraması.

Sonraki düzeltmelerde değişiklik-kapsamlı yeniden doğrulama: yalnız değişen modülün testleri (`-testFilter`); build/smoke yalnız build'i etkileyen dosya değişirse; raporda "tam kapı manifesti / kapsamlı manifest / fark / koşulan filtreler" tablosu. Final manifest tek ilan; zorunlu düzeltmede SUPERSEDED kaydı ve fark dosyası. Tek Unity örneği; kapı ve probe koşuları inceleyici ajanlarla eşzamanlı çalıştırılmaz.
13. PERFORMANS VE YAŞAM DÖNGÜSÜ

* Tier seçimi ve stat çözümü kare başına 0 ayırma; ölçüm `GC.GetTotalMemory` deltası + pozitif kontrol (WP-005 yöntemi).
* Seçim yolunda sahne araması, LINQ, string biçimleme yok.
* Abone sayıları koşu 1 = koşu 2; yeni statikler `ResetStatics`'li; havuz şablonları tekil.
* Pause güvenli.

14. DİĞER WORKTREE'LERİN İZOLASYONU
wp-001 … wp-009h worktree'leri korunur; hiçbirine yazılmaz. Ana ağaca yalnız C-00 fast-forward ile dokunulur. Unity yalnız bu paketin verify kopyalarında açılır.
15. GIT VE WORKTREE KURALLARI

* İzole worktree kullan.
* S-03 tek writer ve commit owner'dır.
* Başlangıç commit'ini doğrulamadan değişiklik yapma.
* Kullanıcıya ait mevcut değişiklikleri overwrite etme.
* Başka paketin worktree'sine yazma.
* Generated, build, log, Library, Temp, user-save, secret veya `.claude` içeriğini commit etme.
* Unity `.meta` ve GUID bütünlüğünü doğrula.
* Başlangıç ve final manifest üret.
* Commit ağacı ile final manifest birebir eşleşmeli.
* Tek, atomik ve açıklayıcı package commit üret; `git add --pathspec-from-file` ile allowlist; `git add -A` yok.
* Merge commit oluşturma.
* main'e doğrudan commit atma.
* main fast-forward entegrasyonunu yalnız C-00 yapar.
* Final etiketi yalnız C-00 doğrulamasından sonra oluşturulur.
* Remote ekleme; push yapma.
* Worktree temizliğini ayrıca yetki verilmeden yapma.
* F0–F10 Git kontrollerini S-10 bağımsız olarak çalıştırmalıdır.

16. KRİTİK DURMA KOŞULLARI
Yalnız aşağıdakilerden biri gerçekleşirse kullanıcı kararı iste:

* REQ-071 garantisi mevcut havuz formülüyle bir roundda sağlanamıyorsa (havuz, o roundda tanıtılan tier sayısından küçük).
* DEC-021 kapsamı dışında bir override alanı zorunlu hale geliyorsa.
* Tier'lara davranış veya yetenek eklemeden paket tamamlanamıyorsa.
* WP-002 savaş sözleşmelerini (saldırı, hit reaction, ayrışma) değiştirmek gerekiyorsa.
* Save şeması veya profil istatistik alanları değişmek zorundaysa.
* Yeni ürün davranışı seçmek zorunlu hale geldiyse.
* Unity/URP/package sürümü değişikliği zorunluysa.

Normal implementasyon ayrıntıları, test düzeni, sınıf yapısı ve güvenli refactor için kullanıcıyı durdurma; Kadro uzmanlarıyla çöz ve raporla.
17. TESLİM RAPORU
S-03 nihai raporda en az şunları sunmalıdır:

1. Başlangıç doğrulaması
2. Değiştirilen dosyalar
3. Tek tier otoritesi mimarisi ve kaldırılan/indirgenen yol
4. Garanti sözleşmesi ve sınır durumu kanıtları
5. DEC-021 override sözleşmesi: öncelik, validation, boş tablo paritesi
6. Stat snapshot tasarımı
7. Tier kimliği ve DisplayName bağımsızlığı
8. Görünüm sözleşmesi ve placeholder paritesi
9. EnemyVersionProbe sonuçları
10. ArmorProbe 09 sertleştirmesi ve 9/9
11. EditMode/PlayMode sonuçları
12. Release build, smoke, DLL taraması
13. Gerçek kullanıcı verisi hash koruması
14. Performans/allocation değerlendirmesi
15. PrototypeOnly değişim raporu
16. DEC/REQ → code → test matrisi
17. Manifest zinciri (tam kapı / kapsamlı / fark / koşulan filtreler)
18. Git F0–F10 sonuçları
19. S-01 mimari kararı
20. S-02 savaş sınırı incelemesi
21. S-04 ekonomi/profil sınırı incelemesi
22. S-09 tier satırları incelemesi
23. S-10 bağımsız teslim kararı
24. Varsayımlar (?U) ve C-00 kararları
25. Ertelenen konular (tier yetenekleri, override satır değerleri, REQ-123, M3 görünüm)
26. M2 çıkış koşullarının durumu
27. Bir sonraki pakete teknik handoff
28. Commit hash
29. C-00 fast-forward için açık entegrasyon talimatı

"Tamamlandı" ifadesini yalnız bütün zorunlu kapılar PASS olduktan sonra kullan.
18. DEFINITION OF DONE
WP-010 yalnız aşağıdakilerin tamamı sağlandığında tamamlanmış kabul edilir:

* Gerçek koşuda tek tier otoritesi; ikinci yol kaldırılmış veya adaptöre indirgenmiş.
* Tier seçimi enjekte RNG ile deterministik; aynı seed aynı kompozisyon.
* Her tier tanıtım roundunda garantili (küçük havuz ve havuz tükenmesi dahil); tanıtım öncesi asla.
* DEC-021 override mekanizması (öncelik, kısmi satır, zorunlu adet, validation) testle kilitli; sevk edilen tablo boş ve formülle bit-eşit.
* Tier stat snapshot spawn'da dondurulmuş; round geçişinde sabit; havuz dönüşünde temiz.
* Tier kimliği `EnemyTier` enum otoritesi; DisplayName yalnız kozmetik.
* Görünüm sözleşmesi hazır; placeholder paritesi korunmuş.
* Tier kill istatistiği ve gold version bonusu WP-008/WP-009 ile birebir; ekonomi/profil kodu diff 0.
* Savaş sözleşmeleri diff 0; hit reaction değerleri değişmemiş.
* EnemyVersionProbe 10/10; ArmorProbe 9/9 idle şartı olmadan.
* Tam zincir sıfırdan Library ile PASS; bütün probe'lar PASS; release DLL temiz.
* Yeni DEC/REQ 0; yeni yetenek 0; sayısal değer değişimi 0 (yalnız etiket/yorum).
* Gerçek kullanıcı verisi taban ile 4/4.
* S-01, S-02, S-04, S-09 PASS; S-10 bağımsız PASS; Git F0–F10 temiz.
* Tek atomik commit; main'e dokunulmamış; push yok.
* M2 çıkış koşullarının durumu belgelenmiş.

Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma. Sorun bulursan nedenini analiz et, kapsam içindeki güvenli çözümü uygula, test et ve kapılar geçene kadar devam et.
