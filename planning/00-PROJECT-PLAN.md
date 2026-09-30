# PUSULA — Deaths Demolition

## Belge kuralı

- Pusula yalnızca asistandır; kritik oyun, ürün ve kapsam kararlarını kullanıcı adına vermez.
- Kritik kararlar kullanıcının kontrolünden ve açık onayından geçer.
- Ana plan dosyalarına kullanıcı tarafından verilen veya açıkça onaylanan kritik kararlar yazılır.
- Onaylı sınırlar içindeki rutin teknik ve uygulama kararları uzman ajanlar tarafından alınabilir; bunlar çalışma raporlarında kaydedilir.
- Öneriler konuşma sırasında tartışılır; kritik öneriler onaylanmadan proje kararı olarak kaydedilmez.
- Yeni kararlar mevcut kararların yerine geçiyorsa eski karar belgelerden çıkarılır.

## Proje özeti

Deaths Demolition; bilgisayar için geliştirilecek, round tabanlı, tek oyunculu ve çevrimdışı oynanabilen bir hayatta kalma oyunudur. Kamera tam top-down değildir; oyuncuyu uzak ve yükseltilmiş üçüncü şahıs açısından izler. Oyuncu haritanın merkezinde Round 1'e başlar ve mümkün olan en yüksek rounda ulaşmaya çalışır.

Koşu boyunca oyuncu tek bir klasik silahı farklı geliştirme dallarıyla dönüştürür. Bazı roundlarda haritada geçici Special Weapon'lar ortaya çıkar. Düşmanlar aynı temel zombi ailesinin roundlar ilerledikçe zırh ve bileşenlerle güçlenen Standard, Special, Elite ve Ultra versiyonlarıdır.

## Proje hedefleri

- Oyuncunun ulaşabildiği en yüksek rounda ulaşması.
- Her koşuda farklı bir silah geliştirme paterni ve belirgin bir build kimliği kurulabilmesi.
- Kalıcı profilin doğrudan savaş gücü vermeden deneyimi, istatistikleri ve açılan seçenekleri göstermesi.
- Vuruş hissi, hit reaction, game feel ve game juice alanlarının titizlikle hazırlanması.

## Güncel kapsam

- Bilgisayar için tek oyunculu oyun.
- İnternet gerektirmeyen tam çevrimdışı oynanış.
- Tek oynanabilir karakter ve bu karakter için başlangıçtan itibaren açık, oynanışı etkilemeyen üç farklı skin.
- Tek harita.
- Tek temel zombi düşman ailesi ve bunun Standard, Special, Elite ve Ultra versiyonları.
- Round, koşu içi gold, round sonu geliştirme ve kalıcı profil ilerlemesi.
- Her round için belirlenmiş toplam düşman havuzu ve bu havuz bitene kadar kesintisiz dinamik spawn.
- Tek klasik silahın koşu boyunca geliştirilmesi ve geçici Special Weapon sistemi.
- Bölgesel zırh, doğrudan isabet ve patlama hasarı kuralları.
- Sabit noktaları kullanan fakat yoğunluğu oyuncuya göre değişen dinamik spawn sistemi.
- Klavye-fare ve gamepad kontrolü.
- Yerel profil, açılan içerik, başarım ve istatistik kaydı.
- Mevcut `prototype/DeathsDemolition_PlayableConcept` Unity projesinin aşamalı olarak nihai oyuna dönüştürülmesi.

## Güncel kapsam dışında

- Mobil ve iOS sürümü.
- Online oyun, netcode ve çok oyunculu mod.
- Birden fazla oynanabilir karakter.
- Online leaderboard.
- Bulut kayıt.
- Birden fazla harita.
- Büyük fizik tabanlı çevre yıkımı.
- Karmaşık çok katlı iç mekânlar.
- Profil seviyesinden gelen doğrudan hasar, sağlık veya başlangıç gold'u avantajı.

Gelecekte online özellik değerlendirilirse mevcut tek oyunculu mod değiştirilmeden ayrı bir mod ve ayrı bir plan olarak ele alınacaktır. Yeni haritalar da mevcut oyun tamamlandıktan sonra farklı konseptlerle ayrıca değerlendirilecektir.

## Teknolojiler

| Alan | Onaylanan seçim |
|---|---|
| Hedef ürün türü | Bilgisayar oyunu |
| Oyun motoru | Unity `2022.3.22f1` |
| Render pipeline | URP `14.0.10` |
| Modelleme, render ve asset üretimi | Blender |
| Üretken içerik araçları | Higgsfield; Adobe Firefly, Adobe Illustrator ve ihtiyaç duyulan diğer Adobe araçları |
| Üretim orkestrasyonu | Claude; Higgsfield, Adobe ve Blender MCP bağlantıları üzerinden verilen promptları ve üretim işlemlerini yönetecek |
| Oyun dili | Tamamen İngilizce |

Hedef işletim sistemi, mağaza ve dağıtım ayrıntıları ürünün büyük bir bölümü üretildikten sonra netleştirilecektir.

## Ana karar kaydı

| ID | Onaylanan karar |
|---|---|
| DEC-001 | Oyun bilgisayar için geliştirilecektir; mobil/iOS kararı geçersizdir. |
| DEC-002 | Oyun round tabanlı, tek oyunculu ve tamamen çevrimdışı olacaktır. |
| DEC-003 | Kamera tam top-down olmayan, uzak ve yükseltilmiş üçüncü şahıs görünümünde olacaktır. |
| DEC-004 | Oyuncunun hedefi mümkün olan en yüksek rounda ulaşmaktır. |
| DEC-005 | Oyuncu koşu boyunca tek bir klasik silahı geliştirecek; Special Weapon'lar geçici güç dönemi sağlayacaktır. |
| DEC-006 | Koşu içi gold, silah geliştirmeleri ve build ölümde sıfırlanacaktır. |
| DEC-007 | En yüksek round, başarımlar, açılan içerikler, profil seviyesi ve istatistikler kalıcı olacaktır. |
| DEC-008 | Kalıcı ilerleme doğrudan güç vermeyecek; mevcut kapsamda yeni silahlar ve geliştirme dalları açacaktır. |
| DEC-009 | Düşmanlar aynı temel zombi ailesinin Standard, Special, Elite ve Ultra versiyonları olacaktır. |
| DEC-010 | Düşman zırhı miğfer, gövde/kol zırhı ve bacak zırhından oluşan bölgesel parçalara ayrılacaktır. |
| DEC-011 | Spawn noktaları tasarımcı tarafından sabit olarak yerleştirilecek; kullanım yoğunlukları oyuncunun konumuna göre dinamik olacaktır. |
| DEC-012 | Oyunun genel görsel yaklaşımı stilize gerçekçilik olacaktır. |
| DEC-013 | İlk ve mevcut tek harita eski Japon kalesi, sarayı ve bahçeleri temasında olacaktır. |
| DEC-014 | Harita merkez avlu ve çevresindeki kesintisiz halka rota yapısını kullanacaktır. |
| DEC-015 | Vuruş hissi, hit reaction, game feel ve game juice prototipten itibaren temel kalite hedefidir. |
| DEC-016 | Unity ve Blender kullanılacak; Cascadeur teknoloji kapsamından kaldırılmıştır. |
| DEC-017 | Oyun dili tamamen İngilizce olacaktır. |
| DEC-018 | Mevcut oyunda tek bir oynanabilir karakter ve bu karakter için başlangıçtan itibaren açık, oynanışı etkilemeyen üç farklı skin olacaktır; karakter çeşitliliği oyun tamamlandıktan sonra ele alınacaktır. |
| DEC-019 | Her round belirli bir toplam düşman havuzuna sahip olacak; düşmanlar bu havuzdan dinamik sistemle kesintisiz spawn olacak; havuz tükenince yeni spawn duracak ve kalan düşmanlar öldürüldüğünde round bitecektir. |
| DEC-020 | Round sonu geliştirme ekranı kapandıktan sonra kısa bir geri sayım başlayacak ve sonraki round otomatik olarak başlayacaktır. |
| DEC-021 | Round zorluğu hibrit sistemle ölçeklenecek; temel artış formülle ilerlerken belirli roundlarda zırh oranları ile Special, Elite ve Ultra dağılımları elle tanımlanan değişiklikler alacaktır. |
| DEC-022 | Special, Elite ve Ultra düşman versiyonlarının her biri belirlenen bir roundda garantili olarak tanıtılacak; tanıtıldıktan sonra sonraki roundların ağırlıklı düşman havuzuna katılacaktır. |
| DEC-023 | Spawn sistemi rounda göre hedef aktif düşman sayısını koruyacak; düşman öldükçe toplam round havuzundan yenisini getirecek ve performans için hiçbir zaman aşılmayan ayrı bir teknik güvenlik tavanına uyacaktır. |
| DEC-024 | Special Weapon, uygun roundlarda round ilerlemesine bağlı geçerli bir aralık içinde rastgele bir anda spawn olacak; roundun hemen başında veya bitmeye çok yakın ortaya çıkmayacaktır. |
| DEC-025 | Special Weapon, belirlenen bir başlangıç roundundan sonra her round bir çıkma ihtimaline sahip olacaktır. |
| DEC-026 | Uygun bir roundda genellikle bir Special Weapon çıkacak; ileri roundlarda nadir ihtimalle iki tane çıkabilecektir. Aynı anda yalnızca bir Special Weapon yerde bulunacak ve önceki süreç bitmeden sonraki spawn olmayacaktır. |
| DEC-027 | Oyuncu belirli süre hasar almazsa canı otomatik olarak yenilenmeye başlayacaktır. |
| DEC-028 | Oyuncunun dash veya dodge hareketi olmayacaktır. |
| DEC-029 | Zombiler yalnızca saldırı animasyonuna bağlı vuruş oyuncuya isabet ettiğinde hasar verecek; yalnızca temas etmek hasar oluşturmayacaktır. |
| DEC-030 | Oyuncu hasar aldıktan sonra koruma süresi kazanmayacak; aynı anda isabet eden birden fazla saldırının hasarı üst üste uygulanabilecektir. |
| DEC-031 | Oyuncu hasar aldığında hareket, nişan ve ateş kontrolü kesilmeyecek; hasar görsel, işitsel ve kamera geri bildirimiyle belirtilecektir. |
| DEC-032 | Oyuncu zombilerin içinden geçemeyecek; zombiler oyuncunun yolunu fiziksel olarak kapatabilecek ve çevrelenmek gerçek bir tehdit oluşturacaktır. |
| DEC-033 | Zombiler birbirleriyle yumuşak ayrışma kullanacak; aynı noktada üst üste binmeden birbirlerini hafifçe iterek sürü halinde akacaklardır. |
| DEC-034 | Oyuncunun tek canı olacak ve yeniden canlanma bulunmayacak; can sıfıra ulaştığında koşu doğrudan sona erecek ve koşu sıfırlama kuralları uygulanacaktır. |
| DEC-035 | Meta akış `Main Menu → Skin & Weapon Selection → Run → Run Results → Main Menu` olacaktır; profil, istatistikler ve başarımlar ayrı menülerde bulunacak, akışın sadeliği UI tasarımı ve kalitesinden ödün verildiği anlamına gelmeyecektir. |
| DEC-036 | Run Results tek ve düzenli bir ekran olacak; ulaşılan round, genel kill, düşman versiyonu başına kill, ayrıntılı XP dökümü, profil seviyesi ilerlemesi, açılan içerikler ve başarımlar aynı ekranda gösterilecektir. |
| DEC-037 | Skin & Weapon Selection ekranı her koşudan önce açılacak; aynı oyun oturumu içinde son kullanılan skin ve klasik silah hazır seçili gelecek, oyuncu doğrudan başlayabilecek veya seçimleri değiştirebilecektir. |
| DEC-038 | Yeni içerikler koşu sonunda otomatik açılacak, Run Results ekranında vurgulanacak ve Claim işlemi gerektirmeden sonraki seçim ekranında kullanılabilir olacaktır. |
| DEC-039 | Oyun her yeniden açıldığında Skin & Weapon Selection varsayılan skin ve klasik silah seçimine dönecektir. |
| DEC-040 | Gold fiziksel olarak yere düşecek; oyuncu toplama mesafesine yaklaştığında kendisine çekilerek otomatik toplanacaktır. |
| DEC-041 | Her düşman öldüğünde garanti gold düşürecektir. |
| DEC-042 | Gold yalnızca öldürülen düşmanlardan kazanılacak; çevre objeleri ve round tamamlama ayrıca gold vermeyecektir. |
| DEC-043 | Gold değeri rastgele olmayacak; temel düşman değerine düşman versiyonu ve taşıdığı zırh parçalarına göre ek gold uygulanacaktır. |
| DEC-044 | Her düşman toplam ödül değerini taşıyan tek bir fiziksel gold paketi düşürecektir. |
| DEC-045 | Gold round sırasında zamanla kaybolmayacak; round tamamlandığında yerde kalan bütün gold otomatik olarak oyuncuya çekilip toplanacaktır. |
| DEC-046 | Harcanmayan gold aynı koşu içinde sonraki roundlara aktarılacak ve koşu bittiğinde sıfırlanacaktır. |
| DEC-047 | Round sonu geliştirme ekranında üç geçerli ve kullanılabilir geliştirme kartı gösterilecektir. |
| DEC-048 | Oyuncu her round en fazla bir geliştirme satın alabilecek veya satın almadan devam edebilecektir. |
| DEC-049 | Ön koşulu sağlanmayan veya mevcut build için kullanılamayan geliştirmeler teklif edilmeyecektir. |
| DEC-050 | Her round sonu geliştirme ekranında bir kez kullanılabilen ücretli yenileme bulunacaktır. |
| DEC-051 | Geliştirmeler belirlenen maksimum kademeye kadar tekrar alınabilecek; ileri kademeler daha pahalı olacaktır. |
| DEC-052 | Higgsfield ile Adobe Firefly, Adobe Illustrator ve ihtiyaç duyulan diğer Adobe araçları üretim teknolojilerine eklenmiştir. |
| DEC-053 | Claude; Higgsfield, Adobe ve Blender MCP bağlantıları üzerinden verilen promptlarla animasyon, ışık, ses, efekt, asset, grafik, evren ve ortam üretimlerini yönetecektir. |
| DEC-054 | Mevcut `prototype/DeathsDemolition_PlayableConcept` projesi terk edilmeyecek; aşamalı biçimde nihai oyuna dönüştürülecektir. |
| DEC-055 | Her uzmanlık alanı için Claude ve Codex'te ayrı kalıcı konu oturumları kullanılacak; bu oturumlar tasarım ile prompt/rapor giriş-çıkış penceresi olacaktır. Ana Codex oturumu merkezi koordinasyonu sürdürecektir. |
| DEC-056 | Her çalışma paketine en yüksek profesyonel kalite için gereken sayıda ve türde Kadro uzmanı dahil edilecek; bağımsız işler paralel, çakışan işler kontrollü yürütülecektir. |
| DEC-057 | Ajanlar kabul kriterleri sağlanana kadar çalışmayı sürdürecek; yalnızca kritik kapsam, karar, maliyet, geri dönüşsüzlük veya gerçek engel durumlarında kullanıcı onayı istenecektir. |
| DEC-058 | İlk sağlamlaştırma ve dikey kesit boyunca Unity `2022.3.22f1` korunacaktır. |
| DEC-059 | URP `14.0.10` korunacak; başka render pipeline'a geçiş yalnızca ayrı kritik kararla yapılacaktır. |
| DEC-060 | Prototip topluca yeniden yazılmayacak; çalışan modüller testlerle korunup geçici yapılar çalışma paketleri halinde dönüştürülecektir. |
| DEC-061 | İlk çalışma paketi `Production Baseline Audit & Stabilization` olacaktır. |
| DEC-062 | Doğrulanan mevcut durum için başlangıç commit'i oluşturulacak; sonraki çalışmalar ayrı ve geri döndürülebilir branch veya commit sınırlarında yürütülecektir. |
| DEC-063 | Çoklu oturum ve ajan çalışmasında Git durumu merkezi olarak koordine edilecek; branch/worktree sahipliği, tek commit sorumlusu, kontrollü entegrasyon, test kapısı ve geri dönüşsüz Git işlemlerinin yasaklanması değişmez kural olacaktır. |
| DEC-064 | Müzik; geleneksel Japon enstrüman karakterini modern sinematik vurmalılar, düşük frekanslı gerilim dokuları ve ölçülü elektronik katmanlarla birleştiren hibrit bir kimlik kullanacaktır. |
| DEC-065 | Müzik; aktif düşman sayısı, Special/Elite/Ultra varlığı, round ilerlemesi ve oyuncunun tehlike durumuna göre katmanlarını değiştirecek; round aralarında geri çekilecektir. |
| DEC-066 | Haritanın kuzey, doğu, güney ve batı bölgeleri ortak atmosfer içinde kendi çevresel ses katmanlarına sahip olacak ve bölgeler arasında yumuşak geçiş yapılacaktır. |
| DEC-067 | Zombi sesleri fiziksel ve gerçekçi temelde kalacak; doğaüstü/büyülü ses kimliği kullanılmayacak, güçlü versiyonlar ve saldırı hazırlıkları yön ve tehdit bilgisi verecektir. |
| DEC-068 | Yağmur sonrası, hafif sisli ve kapalı hava koşulu korunacak; tam gün/gece veya büyük hava değişimi olmayacak, round gerilimi ses ve kontrollü çevresel hareketle yükselirken okunabilirlik korunacaktır. |
| DEC-069 | Düşmanın gold ödülü deterministik olarak `Base Reward + Enemy Version Bonus + Spawn Armor Bonus` formülüyle hesaplanacak; "version" düşman ailesini (Standard/Special/Elite/Ultra) ifade eder, Special Weapon ile ilgisi yoktur. Zırh bonusu düşmanın DOĞDUĞU ANDA taşıdığı Helmet/Body/Leg parçalarına göre belirlenir ve değişmez bir spawn snapshot'ı olarak tutulur; dövüş sırasında zırhın hasar görmesi veya kırılması ödülü azaltmaz. (WP-008, 2026-09-13) |
| DEC-070 | Round sonu geliştirme ekranında kartın fiyatının oyuncunun mevcut gold'undan yüksek olması kartı havuzdan çıkarmaz; kart gösterilir, satın alma düğmesi devre dışı olur. (WP-008, 2026-09-13) |
| DEC-071 | Round başına tek satın alma hakkı; ekranın kapatılıp açılması, pause/resume, aynı round oturumunun yeniden bağlanması veya aynı komutun tekrar gönderilmesi hakkı yenilemez. Satın alma tamamlandığında teklif kilitlenir; yalnız Continue açık kalır. (WP-008, 2026-09-13; DEC-048'i tamamlar) |
| DEC-072 | Ücretli yenileme yalnız satın alma öncesinde ve round başına bir kez kullanılabilir; sonuçta tam olarak bir veya iki kart değişir, üç kartın tamamı asla değişmez ve sonuç yine üç farklı geçerli karttır. (WP-008, 2026-09-13; DEC-050'yi tamamlar) |
| DEC-073 | Üç kartlık geçerli bir teklif veya kurala uygun bir yenileme sonucu üretilemiyorsa oyuncudan gold kesilmez, duplicate/geçersiz kart uydurulmaz, sistem açık bir configuration error üretir, ekran güvenli hata durumuna geçer, Continue açık kalır ve oyun soft-lock/crash yaşamaz. (WP-008, 2026-09-13) |
| DEC-074 | Yenileme ücreti ve geliştirme satın alma bedeli atomik işlemlerdir; işlem başarısızsa gold kaybedilmez. (WP-008, 2026-09-13) |
| DEC-075 | Run Results ekranındaki `Gold Spent` geliştirme satın almalarına ve yenileme ücretlerine harcanan toplam gold'u birlikte gösterir; şimdilik ayrı bir yenileme harcaması satırı eklenmez. (WP-008, 2026-09-13) |
| DEC-076 | Nihai palet ve hava: Presentation A referansı ile DEC-068 (yağmur sonrası, hafif sisli, kapalı hava) nihai palettir; stil kılavuzu v1 bu palete göre sonlandırılır. (WP-015, 2026-09-15) — Not (WP-017, kullanıcı 2026-09-16 07:50, C-00 iletti): oyuncu skin 2 = **2b Steel/Indigo** (AP-006 v003; stil kılavuzu §3 satır 2). |
| DEC-077 | Okunabilirlik eşikleri resmidir: rota örnek noktalarında kapsül örtülmesi %50'yi aşan nokta sayısı 0 olacak; oyuncu/çevre kontrastı ≥ 0,06 olacaktır (WP-011/013/014 ölçüm yöntemi). Testler bu eşikleri doğrular; REQ-104/105 kabul ölçütleri buna göre ölçülebilir hâle getirilir. (WP-015, 2026-09-15) Ek (kullanıcı kararı 2026-09-16, WP-020 kaydı): "zırh: siluet değişimi ≥ %30 + parlaklık bandı min 0,08 / ort 0,12; lake açılmaz" — zırhlı zombinin okunurluk ölçütü ADR-017-1'in motor içi yöntemiyle ölçülür (zırhsız silüete göre siluet değişimi, zırh ↔ komşu zırhsız bant ΔV min/ort); S-10 kapı aracı bu değerleri okur. |
| DEC-078 | Harita çevre zemini: Unity stand-in zemin (±80 m) kalır; tam harita zemin/patika plakaları AP-015 kapsamında üretime devredilir. (WP-015, 2026-09-15) Durum: AP-015P ile kapandı — 9 zemin plakası (5 bölge + 4 dış bant ±80 m), 49 rota şeridi, 13 kaldırım ve 3 su ağı Unity'de stand-in zeminin yerini aldı (WP-038, 2026-09-17); karar metni değişmedi. |
| DEC-079 | Rota/köprü genişliği: güney köprüsü 1,2 m tek sıra ve kuzey 14 m koridor değişmez; playtest sonrası yeniden değerlendirilir. (WP-015, 2026-09-15) |
| DEC-080 | Windows hedefi süresiz ertelenmiştir; ayrı kullanıcı kararı gelene kadar yalnız macOS (M1 Pro) ölçülür. (WP-015, 2026-09-15; WP-011 §23 #3 kapanır) Durum: DEC-089 ile kaldırıldı. |
| DEC-081 | Spawn'da Rise melezdir: kalkış (rise) animasyonu yalnız kamera dışında doğan düşmanlarda oynar; kamera içinde doğan düşman kalkışsız (hazır) belirir. Kalkış süresi ve klip seçimi PrototypeOnly kalır. (kullanıcı, 2026-09-17; uygulama S-03 WP-040, animasyon S-06/P-06 AP-009C) |
| DEC-082 | Special Weapon alma sırasında hareket ve nişan kilitlenmez: alma, üst gövde katmanında oynayan bir klipten ibarettir (bacaklar taban katmanından); alma tetikte kesinleşir, klip kozmetiktir — vurulma almayı iptal etmez, ölüm koşuyu bitirir. (kullanıcı, 2026-09-17; WP-007 yaşam döngüsü + WP-042 S-02, klip S-06 WP-032C / P-06 AP-009C `player_pickup_upper`) |
| DEC-083 | Klasik silah listesi WP-024 önerisiyle aynen kararlaştırılmıştır: üç klasik silah — A tüfek tipi (varsayılan, açık), B karabina tipi, C pompalı/geniş ağızlı tipi; D ağır tek atış tabanca YOK (roster verisinde kapalı kalır, seçim/dükkânda gösterilmez, asset üretilmez). Dal ilkesi (a): her silahın silaha özgü 3 çekirdek dalı (A Piercing/FireRate/ReloadSpeed · B Chain/Ricochet/FireRate · C Explosion/Piercing/ReloadSpeed) + ortak 4 oyuncu dalı; pompalıya Chain/Ricochet verilmez. Açılış (a): A açık, B ve C profil seviyesiyle (DEC-008). Sayısal denge değerleri PrototypeOnly kalır; uygulama WP-034 roster verisi, görsel/soket sözleşmesi `WP-034-WEAPON-VISUAL-CONTRACT.md` (AP-013, ADR-042-1). (kullanıcı, 2026-09-17; WP-042) |
| DEC-084 | Lightroom ışık/LUT hedef görünümü B "Islak Arduvaz" olarak seçilmiştir; WP-029 (S-05) bu adayı nihai görünüm olarak uygular, diğer adaylar kapanır. (kullanıcı, 2026-09-17) |
| DEC-085 | Denge v1 (WP-041 simülasyonu + WP-045 kalibrasyonu): Ö-1 geliştirme kartı fiyat büyümesi tüm dallarda 2,0/tier (`CostGrowthPerTier`; DEC-070 açılış bandı değişmez, DEC-071 tek satın alma korunur; S-04 WP-049); Ö-2 zırh şansı büyümesi ×0,5 ve Standard/Runner tavanı 0,55 (S-03); Ö-3 silah B profil seviyesi 3, C 5 (S-04 WP-049 / S-02 WP-042); Ö-4 B ve C hasarı 7 → 8 (S-02 WP-042); Ö-5 DEC-021 elle tanımlı round satırları R8/R11/R14 (S-03; satırlar Ö-2 tavanını aşmaz). Sayılar PrototypeOnly kalır; simülatör aynı komutla (`-ddScenario`) yeniden koşulur. Ö-6a (round başına iki kart, DEC-071 değişikliği) ayrı kullanıcı kararıdır. (kullanıcı, 2026-09-17; WP-049) |
| DEC-086 | M3 release build tavanı 450 → 550 MB (kullanıcı 2026-09-17; ADR-015-1/2 v4, WP-038); tavan hedef değil büyüme kapısıdır, dağıtım kararıyla yeniden belirlenir. |
| DEC-087 | DEC-071 değişikliği: round sonu teklifinde round başına **iki** geliştirme kartı satın alınabilir (`EconomyConfigSection.PurchasesPerRound` = 2; sayaç limitte kilitlenir, yeniden açılış/pause hakkı yenilemez; DEC-050 ücretli reroll ilk satın almada kapanmaya devam eder). Gerekçe: WP-049 simülasyonu — Ö-1 sonrası harcama payı %20'de kalıyor, iki kartla %38–44, R15 ölüm riski 8,8 → 6,1. (kullanıcı, 2026-09-17; WP-049C S-04, UI S-09) |
| DEC-088 | M3 performans bütçesi: ortalama kare ≤ **8,0 ms** / temiz p99 ≤ 10 ms (M1 Pro 1920×1080, 40 düşman, 60 s pencere; ADR-011-3 v3). 7,0 ms look pass (WP-029/038) öncesi öneriydi; kapanış 4 ölçümü ort 7,5 / p99 8,4 ms. Kullanıcı 2026-09-17 (S-10 WP-044). |
| DEC-089 | Hedef platformlar macOS ve Windows; DEC-080'in Windows ertelemesi kaldırıldı. Kanal, imza, Windows referans donanımı/perf bütçesi ve IL2CPP ayrı kararlar (WP-066). (kullanıcı, 2026-09-23) |
| DEC-090 | Release build sıkıştırması LZ4HC (547→323 MB, ölçülebilir yükleme cezası yok; DEC-086 tavanı 550 aynı; kullanıcı 2026-09-24). macOS + Windows; `tools/build.sh release` varsayılanı, dev build sıkıştırmasız (WP-066E). |
| DEC-091 | WP-064 soak kapısı ikiye ayrılır: **kararlılık (MUST)** = ölümsüz (invulnerable) bot 2 tohum (61 + 26) × 12/12 round, zaman aşımı/takılma 0, Player logunda hata 0, yönetilen yığın/toplam ayırma büyümesi sınırlı (`tools/qa/soak_gate.py --stability`; `runMode=stability`, ayrı `stabilityPass`), kullanıcı verisi korunur, temiz host; **gerçek bot koşuları kapı değil, rapordur** (N ≥ 5 koşu, ulaşılan round dağılımı, ölüm round'u/nedeni — denge/zorluk verisi). Kullanıcı 2026-09-24 (C-00 iletti; S-10 WP-064B). |
| DEC-092 | M4 performans hedefi **60 fps**: Yüksek kalite kademesinde ortalama kare ≤ **16,6 ms** / p99 ≤ **20 ms** (1920×1080, referans makine = geliştirme Mac'i); ayrıca daha hafif bir Performans kademesi bulunur. DEC-088 (8,0 ms) M3 kapanış tabanı olarak tarihçede kalır; WP-064 kapanışı 8,0 ms kanıtıyla sürer. Kademe başına ölçüm protokolü WP-078 (S-10). (kullanıcı, 2026-09-24; M4-R1 §6) |
| DEC-093 | Release build boyutu tavanı **5 GB**; DEC-086'daki ve DEC-090'daki 550 MB tavanının yerine geçer. LZ4HC sıkıştırması (DEC-090) sürer. Doku/mesh kalitesi boyut için kısılmaz. Araç eşiklerini (build/smoke/build-report) S-01 günceller. (kullanıcı, 2026-09-24, M4-R1) |
| DEC-094 | M4 Special silahları **yalnız iki**: zincir yıldırım (CoD BO Zombies Wunderwaffe DG-2 benzeri) ve kaydırıcı jel (Sliquifier benzeri). Mekanik, mantık, his ve sanat yönü bu referanslara çok yakın tutulur, oynamalar yapılabilir. **Ad ve model özgündür**; orijinal ad ve modeller kullanılmaz. Mevcut Kagura, Ofuda ve Tide Special'ları kaldırılır. Tasarım WP-069 (S-02), görseller AP-021 (P-07). (kullanıcı, 2026-09-24, M4-R1) |
| DEC-095 | Gelişim ağacı **iki katmanlıdır**: (1) koşu içi dallanan ağaç — altınla alınır, tur sonu dükkânı düğümlere dönüşür; (2) kalıcı meta ağaç — koşular arasında kalır; silah kilitleri, Special eğilimleri, pasifler. Tasarım ve mantık WP-077 (S-04), ekran WP-068 R1 (S-09). (kullanıcı, 2026-09-24, M4-R1) |
| DEC-096 | WP-076 AI v3 kuralları: (1) **bite_grab** — oyuncuda aynı anda en çok **1** kapma; kapan bedene tek güçlü vuruş (etki ≥ 0,85) oyuncuyu bırakır ve ısırığın ikinci payı düşer; (2) jel Special birikintisinden zombiler **kaçınmaz** (konsey onaylı, referans mekaniğe sadık); (3) ölüm darbesi bölünmesi — S-03 `DeathImpulse` verir (yön, kuvvet, kaynak, silah sınıfı, bölge, nokta, hız, gövde durumu), ölüm ailesini S-06 seçer; ceset ömrünün tek sahibi S-03. (C-00 onayı, 2026-09-24, WP-076) |
| DEC-097 | Arsenal denge v3 (WP-069, DIRECTION §1/§6/§7): (1) on klasik silahın sayıları `WeaponDefinition` v3 varlıklarında (PrototypeOnly, `ArsenalAssetBuilder`): ar_1 Hinawa auto 600 RPM 12 hasar 30/210 · ar_2 Sanren burst 3 (0,05 / 0,26 s) 16 hasar 24/168, kafa ×1,15 · smg_1 Shippu 900 RPM 8 hasar 42/294 · smg_2 Tetsui 540 RPM 14 hasar 24/192 · sg_1 Rappa tekli 72 RPM 9 × 8 pellet (merkez + 3 @ 0,40 + 4 @ 1,00), 7/42 fişek fişek, tetik dolumu keser · sg_2 Nito tekli 200 RPM 11 × 10 Gauss (σ 0,45), 2/40 · lmg_1 Mukade 720 RPM 11 hasar 100/300 · lmg_2 Kurogane 420 RPM 24 hasar 60/180, oturma (taban hip'in %35'i) · sr_1 Tomi 48 RPM 55 hasar 5/40, 4 gövde delme (%80 korunur), kafa ×1,25, zırh ×1,6 · rl_1 Bohiya 110 doğrudan + 160 patlama (1,8 / 5,0 m), kendine hasar %45 tavan 72, kurma 4 m (ilk oynanışta ölüm oranı > %5 → tavan 50); ar_1 itkisi 0,13 (600 RPM'de REQ-123 korunur: koşan Standard 2,27 < 2,4); mermi / pellet yerçekimi 0 (WP-042 bölge paritesi); (2) yedek cephane: tur başında yedek × (1 + 0,12 × (r − 1)) (rl_1 0,06), asla azaltılmaz; şarjör ve yedek boşsa 4 s (LMG 8 s, rl_1 6 s) sonra +1 şarjör; tekli ateşte basılı tetik çevrim tavanında yeniden atar; (3) Special yaşam döngüsü **cephane + süre tavanı** — Raiju 3/9 tavan 40 s, Namekuji 8/16 tavan 45 s; ikisi de biterse Special düşer, klasik silah döner (DEC-026'nın 11 s sabit süresinin yerine; ortaya çıkış, 22 s yerde bekleme ve tek Special kuralı korunur); (4) tier direnci — Raiju: Executioner azami HP'nin %60'ı + 2,5 s sersemleme, zincir ondan sürer; Namekuji doğrudan isabet: Guard R ≤ 10 ölür, R > 10 %60 + 1,5 s devrilme, Executioner %25 + 1,5 s devrilme; birikintide kayma: Executioner %20 + 3 s bağışıklık, diğerleri ölür; (5) kilit: ar_1 açık, smg_1 / sg_1 DEC-085 profil seviyesi 3 / 5, diğer yedi silah DEC-095 meta ağacıyla (WP-077). (C-00 onayı, 2026-09-24, WP-069) |
| DEC-098 | Gelişim ağacı v2 kuralları ve ekonomi (WP-077, DIRECTION §2–§4; B3 simülatörü `EconomySimulator.TreeModel` + bulut denetimi #5 #6 #14): (1) **K1 hibrit teklif** (kullanıcı, 2026-09-24): tur sonu 3 kart ağaç cephesinden katalog ağırlığıyla, Nişan (pin) yolunun sıradaki adımı garanti; kart sınırı DEC-071/087 (2); DEC-047/050 korunur; (2) K2–K11 (C-00, 2026-09-24): iki şerit (silah 13 / beden 12 düğüm), **Ocak** kart dışı doğrudan alım 50 + 30·n, ≤ 3/round, ≤ 20, +%2 hasar; **Kese** round toplamasının +%10'u tur sonunda; bağlanmamış etkili düğüm KİLİTLİ (satılmaz); profil v5, Kül yalnız ApplyRun içinde (WP-009H), K11 göçte seviye düşmez + geriye dönük Kül; XP eğrisi `Req(L) = 100L + 10L²`, tavan L50 (denetim #14: L10 1,2–2,1 s, L20 7,2–12,7 s — değişmez); (3) **Kül temposu** (denetim #5): Kül = XP / **30** (40'tı); halka 1 = **150 Kül** (eğilim 1. düğüm 30, pasif 1. sütun 3 × 40), halka 2 pasif 70/100/140, Önsezi 70, Bağ II 110, Erken Çan 100, silahlar WP-069 bandı (ar_2 120 · lmg_1 160 · sr_1 200 · smg_2 240 · sg_2 300 · lmg_2 380 · rl_1 480); smg_1/sg_1 seviye merdiveninde Kül'süz (DEC-085); tüm meta 5 830 Kül — 4 500 XP/s referansında halka 1 1,0 s, halka 1+2 ≈ 11 s (tek seferliklerin yarısıyla; hedef ≤ 10 s), tümü ≈ 39 s; halka 1–2'de Kül bağlar, seviye kapısı halka 3'te; (4) **K6 doruk kapısı** (denetim #6): pinsiz doruk teklifi silah ustalığı **M1 (150 öldürme)** ile açılır (M3/900'dü); Nişan doruğu M1 öncesi de getirir; (5) §4.2 iki bot olarak (90 % toplama, seed 1–20 × ar_1/smg_1/sg_1): harcama-azami pay %96, Ocak 20 (R18–19'da dolar), R15 cüzdan medyan 233–259; pinli (acele) keystone R4, doruk medyan **R9** / p90 R10; insan (%30 yastık) pay %86–87, R10–R15 %90–91, R15 cüzdan medyan 819–888 (≤ 900) — p90 2 700–2 900 ve biriktirici (%50 yastık, ≤ 1 Ocak) medyan ≈ 3 400 bilgi (davranış kaynaklı; Ocak tavanı 30 R15'e etki etmez, 3. kart R10+ havuzu boşaltır); R20 öncesi havuz tükenmesi 0. Biriktirici cüzdanı yalnız bilgi: biriktirmek oyuncunun seçimi, altın → Kül çevirisi yok; test yalnız rapor (`Biriktirici_Cuzdan_YalnizRapor`). İçerik paketi Q6: "Every run starts with the Purse." / "Her koşu Kese ile başlar."; Q5 tr "Jel Hasadı". Açık: gerçek oyuncu XP/s ölçümü (round modeli bot hızıyla 2 600–3 300 XP/s verir). (C-00 onayı, 2026-09-25, WP-077 B3) **Not (kullanıcı 30.09, C-00 iletti; K10b S1 A):** bantlar %100 toplamaya göre (tur sonu süpürmesi dükkândan önce tüm paketleri toplar; simülatör varsayılanı 0,90 → 1,00); pinli botun ilk keystone bandı **R3–6**. Ölçüm (WP-077 B6, DEC-107 ile): pinli keystone R3, doruk R10 / p90 R10; insan R15 cüzdan medyanı 811 (M1 öncesi) / 803 (M1); R20 öncesi havuz tükenmesi 0. |
| DEC-099 | Arsenal denge v3 düzeltmesi (WP-069 B2c; `wp-069/balance/B2-REVERIFY.md`) — DEC-097'yi şu noktalarda değiştirir: (1) ar_2 Sanren şarjör / yedek 24/168 → **18/144** (ar_1 sürekli DPS ve tur başı cephanede öne geçer); (2) pompalı rolleri takas: sg_1 Rappa halkalar 0,40 / 1,00 → **0,25 / 0,55** koni (dar desen, uzanır), sg_2 Nito Gauss σ 0,45 → **0,70** (geniş saçma), reload 1,7 → **3,0 s**, yedek 40 → **24**; (3) lmg_2 Kurogane yedek 180 → **90**; (4) sr_1 Tomi delmede hasar korunumu %80 → **%90**, çekirdek üçüncü dalı ArmorBreak → **ReloadSpeed** (S-04 kararı; ArmorBreak lmg_2'de kalır); (5) Namekuji birikinti süresi 25 → **12 s**, eşzamanlı tavan 12 → **8**, Guard kayma ölümü yalnız **R ≤ 10** (sonra azami HP'nin %60'ı, doğrudan isabetle aynı); (6) elde tutulan Special **round sonunda biter** (yerdeki pickup round sınırını geçmeye devam eder); (7) rl_1 doğrudan isabeti zırhı **sayar** (değişiklik yok; B3'te hedef doğrudan isabeti ve kendi patlamasını sırayla alır). smg_2 önerisi (17 hasar / 440 RPM / 20/160) ilk oyun testine bırakıldı; smg_1 zırh ×0,7 korunur. (Kullanıcı kararı 2026-09-24, S-04 D2; WP-069) **Not:** sr_1 hasarı DEC-105 ile güncellendi (55 → 80). |
| DEC-100 | Varsayılan kalite kademesi Sinematik: 30 fps (ort ≤33,3 / p99 ≤40 ms, 1080p, referans Mac), kalite en üst düzey; Yüksek 60 fps (DEC-092) ve Performans seçilebilir (kullanıcı, 2026-09-25) |
| DEC-101 | Genel ton HF gibi soğuk: soğuk mavi-gri gölgeler, düşük doygunluk (ahşap turuncuya kaçmaz, yosun koyu zeytin), koyu ıslak zemin, soğuk beyaz noktası, kontrast yukarı, sütlü kalkış yok; sıcaklık yalnız huzmelerde ve güneş vuran vurgularda. Sıcak/açık "Açılma" tonu (WP-075 Ek A §6.1, LUT v2) varsayılan değil → LUT v2b. Sahne de bulutlu: zayıf dağınık güneş (×0,60, gölge 0,55), kapalı gök %15 açıklık (huzme yalnız açıklıktan), ağır bulutlu koyu gri-mavi gök, uzak katmanlarda sis/hava perspektifi, koyu zeytin ıslak yosun (çim yok), koyu bambu, koyu gri çatı, ıslak soluk gri-bej sıva, ayna-ıslak avlu (Ek A §1.4 canlı doygunluk ve AD S1 ≤ %8 spekular hedefleri bu kararla geçersiz). Ölçüm: ΔE kapısı + HF center/north/west karesine ortalama renk / L std / ΔE (kullanıcı, 2026-09-25, C-00) |
| DEC-102 | Gök parçalı bulutlu (%40–55 örtü), güneş yüksek (~55–65°), güneş diski ve halesi görünür; güneş güçlü ve sıcak, gölgeler soğuk mavi; yağmur sonrası parlak ıslak yüzeyler. Güneş huzmeleri gerçekçi hacimsel ışık (URP 14 raymarch: ana ışık gölge haritası × bulut cookie; kart hilesi değil), ağaç / bambu / saçak / kapı aralarından yere vurur, hafif bölgesel sis taşır; Sinematik tam, Yüksek düşük örnek, Performans kapalı. Sinematik perf payı (~20 ms) gölge, hacimsel ışık, yansıma, bloom / güneş parlamasına; kapı ort ≤33,3 / p99 ≤40. DEC-101'i günceller: ışık değişir (kapalı gök gider); ıslak arduvaz, koyu gri çatı, çim yok, koyu yosun, soğuk mavi gölgeler, HF malzeme kalitesi kalır. Yeni sayısal hedef: HF karelerinin "yağmur sonrası, parçalı bulut, yüksek güneş, hacimsel huzme" image-to-image varyantları (kullanıcı, 2026-09-25, C-00). **r2 (C-00 25.09):** sayısal hedef = `AP005-HF-{center,north,west}-v002-dec102.png` (arka ışık, altın, ayna-ıslak meydan, ağaç aralarından huzme); güneş 45–55° (50°); **güneş kuzeyde (NNW) — sanatsal tercih, HF'nin kuzeye bakan arka ışık kadrajı için**; DEC-101'den kalan tek ton kuralı: gölgeler soğuk mavi-gri, sıcaklık güneşte, huzmede ve yansımada; huzme üç kamerada okunur (bulut açıklığından sütunlar + dar huzmeler, g 0,6–0,7, bölgesel nem); meydan L ≈ 35, ıslak parlama kalır; gök HF 2:1 parçalı bulut panoraması (≤ 4 kr), cookie onunla eşleşir; **ufuk sisi etek: DEC-108** |
| DEC-103 | Namekuji jel rengi: sadakat, magenta #FF5FD2 ailesi kalır (DEC-094 gereği) (kullanıcı 30.09, C-00 iletti) |
| DEC-104 | Mermi izi katsayısı: Δ 0,020 s, sr_1 0,025 s, tavan 8/10 m kalır; oyun etkisi yok (kullanıcı 30.09, C-00 iletti) |
| DEC-105 | Tomi (sr_1) hasarı **55 → 80**; Kurogane'e (lmg_2) dokunulmaz. Gerekçe (S-04 K10b denetimi, `wp-077/KARAR-P1-P5.md` P1 seçenek A): Tomi her düşman türünde öldürmek için ortalamanın ~1,5–2 katı süre harcıyor ve kalabalığa karşı en zayıf silah; 80 ile birçok turda ortalamaya yaklaşır, sıradan zombiyi 12. tura kadar gövdeden tek atışta öldürür. Kurogane her türde en hızlı öldüren silah kalır, önce oyunda görülür. DEC-099'un sr_1 satırını günceller. Uygulama: `ArsenalAssetBuilder` → `WD_sr_1` (S-02, WP-069 B5); ilk oyun testinde tier başına TTK ile ölçülür (WP-069 B5-5 kiti). (kullanıcı 30.09, C-00 iletti) |
| DEC-106 | Bohiya'nın doruğu Kül Yağmuru: üç küçük patlamanın her biri **tam hasarla** vurur (`RocketSplitDamageScale` 0,5 → 1,0; yarıçap ×0,6 ve halka ×0,45 aynı). ×0,5'te doruk kalabalık hasarını ~%40 düşürüyordu; ×1,0'da ~+%8 (WP-077 B5 denetimi, K10b P2 A). (kullanıcı 30.09, C-00 iletti) |
| DEC-107 | Geç tur bonusu: **10.–13. turlarda turda 3 kart** alınır, diğer turlarda 2 (DEC-071/087'yi bu turlar için günceller); ekranda "geç tur bonusu" olarak gösterilir. Veri `EconomyConfigSection.LatePurchasesPerRound` 3 / `LatePurchaseFirstRound` 10 / `LatePurchaseLastRound` 13. DEC-098 simülatörü: insanın doruğa ulaşma oranı %21 → %48 (M1), R15 cüzdan medyanı 811 / 803, R20 öncesi havuz tükenmesi 0 (K10b P5 A). (kullanıcı 30.09, C-00 iletti) |
| DEC-108 | Gök panoraması ufuk sisi eteğinin altında görünür: yakın ufuk plakasının (`horizon_near_v2`) alt kenarı yumuşatılır ve altındaki gök panoramasını açar (dd4e5bff yönü: `DD_SkyPlate` `_BottomEdgeAA` / `_BottomPanoramaReveal`); eteğin altı düz gri bant olarak kalmaz. Kalan kusurlar (gri geçiş, NW etek, W dikişi, B6 kuzey galeri EDGE şeritleri) aynı AD turunda kapanır: önce/sonra kareleriyle panel (yazar + 2 çürütücü). DEC-102'nin gök maddesini tamamlar (kaynak: kullanıcı 29.09 yanıtı, 30.09 C-00 teyidi) |
| DEC-109 | Türkçe başlık yüzü **"Shippori Mincho B1 DD-TR"**: Shippori Mincho B1 Bold'a Ğ ğ İ Ş ş eklendi (OFL 1.1 türevi; lisansta Reserved Font Name yok; özgün telif ve OFL metni korunur, `ShipporiMinchoB1DDTR-NOTICE.txt`). Breve tasarımı panel kararı A (AD + tip + geometri çürütücüleri): 160° yay üzerinde modüleli hilal, kalınlık 60 / uç 24; ğ 280×122, Ğ 290×100. İ = yüzün Ü noktası; Ş ş = yüzün U+0327 sedili. Türkçe başlıklar Display yüzüyle çizilir (Codex 19cb757a'nın Text SDF geçişi kalktı, M-64 kapandı). Araç `tools/dec109/dd-tr-font.sh --venv DIR --src <özgün>` (özgün SHA denetimi, deterministik). Oyun içi TMP provası + AD imzası ACCEPT_WITH_FIXES (`K/wp-068s9/evidence/dec109`). (kullanıcı 30.09: Shippori'ye glif ekle; fonttools kullanıcı onayı 01.10; C-00 iletti) |
| DEC-110 | Nişan (pin): o turun atamasının gittiği hedefe temizledikten sonra **geri dönmek ücretsizdir** (hak harcamaz); başka hedefe geçiş kuralı aynı (turda `pin.perRound` atama, temizleme iade etmez — DEC-098 K2, M-33). (kullanıcı 30.09, C-00 iletti; Codex listesi M-34) |
| DEC-111 | Silah ustalığı (mastery, doruk kapısı M1 dahil) **yalnız koşunun başlangıç klasik silahının öldürmelerini** sayar; Special ve sahipsiz öldürmeler sayılmaz (öldüren isabetin `DamageHitContext`'i; `RunSummary.ClassicWeaponKills`). (kullanıcı 30.09, C-00 iletti; R1-B P7) |
| DEC-112 | Üç doruk hissedilir fark yaratır (K10b P4 A, `wp-077/KARAR-P4-MIKNATIS.md`): **Sanren (ar_2) Üçüncü Göz** — her serinin 3. mermisi **+%30** vurur ve yakındaki bir düşmana zincirlenir (kafa şartı kalkar; seri sırası `ProjectileState.TreeOrdinal`, gerçek seri indeksi S-02). **Tetsui (smg_2) Yıldırım Dişi** — isabetle sendeleyen gövde Tetsui'den **+%25** alır (ks_impact ile toplanır), her 3. isabet zinciri kalır. **Kurogane (lmg_2) Kara Demir** — plakasını kırdığı zombi **0,35 s** sendeler (aynı gövdede 1,5 s kilit, Ultra'da yarı süre; hasar eklemez, sendeleme bonusları görmez — `ZombieEnemy.ApplyTreeStagger`, S-03); eski tam hasarlı geçiş kalkar. Görünürlük `RunTreeHooks.CapstoneProc` olayıyla (S-02 iz, S-07 ton). Model R10: Sanren +%10 tek / +%16 kalabalık, Tetsui +%16 / +%18 (panel gerçekçi +%10–13). (kullanıcı 30.09, C-00 iletti) |
| DEC-113 | Meta yolu QoL satırında "Geniş Mıknatıs" (pa_qol_6, 28. seviye, 320 Kül) yerine **Tur Raporu**: her tur sonunda tek satır — türe göre öldürmeler, alınan hasar (oyuncunun gerçekten kaybettiği can), turda kazanılan altın ve Elite öldürme süresi (ilk isabet → ölüm ortalaması). Yalnız bilgi, güç vermez (DEC-008). Mıknatıs altın ya da güvenlik kazandırmıyordu (tur sonu süpürmesi her paketi toplar). Veri `RunStatsTracker.LastRound` (`RoundReport`, telemetriden bağımsız, tur sonu süpürmesinden sonra ve RoundCompleted'dan önce donar; `RunDirector.LastRoundReport`); efekt `meta.qol.roundreport`, düğüm S-09 arayüzü `MetaPathEffects` canlı kümesine ekleyene kadar kilitli (K10b P4 Mıknatıs A, `wp-077/KARAR-P4-MIKNATIS.md`). (kullanıcı 30.09, C-00 iletti) |

## Kararlaştırılması sonraya bırakılan alanlar

- Kesin can, hasar, zırh ve round ölçeklendirme değerleri.
- Patlama hasarının bölgelere dağılım oranları.
- Profil XP değerleri, formülü ve seviye eğrisi.
- Geliştirme kartlarının içerikleri, dalları, maksimum kademe değerleri ve gold ekonomisinin sayısal değerleri.
- Special Weapon türleri, çıkacağı roundlar ve süre değerleri.
- Otomatik can yenilenmesinin hasarsız bekleme süresi, yenilenme hızı ve sınırı.
- Hedef işletim sistemi, mağaza ve dağıtım modeli.

Henüz kararlaştırılmamış bir ayrıntı bu belgelerde varsayım olarak kabul edilmez.
