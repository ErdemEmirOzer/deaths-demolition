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
| DEC-076 | Nihai palet ve hava: Presentation A referansı ile DEC-068 (yağmur sonrası, hafif sisli, kapalı hava) nihai palettir; stil kılavuzu v1 bu palete göre sonlandırılır. (WP-015, 2026-09-15) |
| DEC-077 | Okunabilirlik eşikleri resmidir: rota örnek noktalarında kapsül örtülmesi %50'yi aşan nokta sayısı 0 olacak; oyuncu/çevre kontrastı ≥ 0,06 olacaktır (WP-011/013/014 ölçüm yöntemi). Testler bu eşikleri doğrular; REQ-104/105 kabul ölçütleri buna göre ölçülebilir hâle getirilir. (WP-015, 2026-09-15) |
| DEC-078 | Harita çevre zemini: Unity stand-in zemin (±80 m) kalır; tam harita zemin/patika plakaları AP-015 kapsamında üretime devredilir. (WP-015, 2026-09-15) |
| DEC-079 | Rota/köprü genişliği: güney köprüsü 1,2 m tek sıra ve kuzey 14 m koridor değişmez; playtest sonrası yeniden değerlendirilir. (WP-015, 2026-09-15) |
| DEC-080 | Windows hedefi süresiz ertelenmiştir; ayrı kullanıcı kararı gelene kadar yalnız macOS (M1 Pro) ölçülür. (WP-015, 2026-09-15; WP-011 §23 #3 kapanır) |

## Kararlaştırılması sonraya bırakılan alanlar

- Kesin can, hasar, zırh ve round ölçeklendirme değerleri.
- Patlama hasarının bölgelere dağılım oranları.
- Profil XP değerleri, formülü ve seviye eğrisi.
- Geliştirme kartlarının içerikleri, dalları, maksimum kademe değerleri ve gold ekonomisinin sayısal değerleri.
- Special Weapon türleri, çıkacağı roundlar ve süre değerleri.
- Otomatik can yenilenmesinin hasarsız bekleme süresi, yenilenme hızı ve sınırı.
- Hedef işletim sistemi, mağaza ve dağıtım modeli.

Henüz kararlaştırılmamış bir ayrıntı bu belgelerde varsayım olarak kabul edilmez.
