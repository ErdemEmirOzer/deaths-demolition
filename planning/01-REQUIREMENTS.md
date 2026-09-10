# 01 — Gereksinimler

Bu dosyada yalnızca kullanıcı tarafından verilen veya açıkça onaylanan kararların test edilebilir karşılıkları bulunur. Sayısal değeri henüz kararlaştırılmamış gereksinimler sayı uydurulmadan kaydedilmiştir.

## Ürün ve çalışma modeli

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-001 | Oyun bilgisayar oyunu olacaktır. | Projenin güncel hedefi mobil/iOS olarak tanımlanmaz; işletim sistemi ve mağaza daha sonra seçilir. |
| REQ-002 | Oyun tamamen İngilizce olacaktır. | Oyuncuya gösterilen oyun metinleri İngilizcedir. |
| REQ-003 | Oyun tek oyunculu olacaktır. | Güncel oyun akışı ikinci bir oyuncu veya online oturum gerektirmez. |
| REQ-004 | Oyun internet olmadan tamamen oynanabilecektir. | Yeni koşu başlatma, oynama, bitirme ve kalıcı ilerleme internet servisi olmadan çalışır. |
| REQ-005 | Oyuncunun aynı anda yalnızca bir aktif koşusu olacaktır. | Sistem eşzamanlı birden fazla aktif koşu oluşturmaz. |
| REQ-006 | Duraklatma bütün oyun simülasyonunu donduracaktır. | Duraklatıldığında oyuncu, düşman, mermi, zamanlayıcı ve round simülasyonu ilerlemez. |
| REQ-007 | Online servisler güncel kapsamın dışında olacaktır. | Netcode, online leaderboard, bulut kayıt ve servis bağımlılığı mevcut plana dahil değildir. |
| REQ-008 | Oynanış sistemleri gereksiz biçimde tek bir global oyuncuya kilitlenmeyecektir. | Oyuncu, düşman ve hasar sistemlerinin kuralları zorunlu bir global oyuncu referansına dayanmaz. |
| REQ-009 | Sade meta akış UI tasarımı ve kalite hedefini düşürmeyecektir. | Menü sayısının ve gezinme adımlarının az olması, UI tasarımından veya sunum kalitesinden ödün verilmesi olarak uygulanmaz. |

## Ana koşu ve round döngüsü

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-010 | Oyuncu Round 1'e haritanın merkezinde başlayacaktır. | Koşu başlangıç konumu merkez saray avlusudur. |
| REQ-011 | Amaç ulaşılabilen en yüksek round olacaktır. | Koşu ve profil sonuçlarında ulaşılan round izlenir. |
| REQ-012 | Bir round, düşman havuzu tükendikten sonra kalan bütün düşmanlar öldürülünce tamamlanacaktır. | Havuzda spawn edilmemiş düşman ve haritada yaşayan düşman kalmadığında round bitişi tetiklenir. |
| REQ-013 | Roundlar ilerledikçe tehdit hibrit sistemle artacaktır. | Düşman sayısı, dayanıklılığı ve hasarı temel formülle ilerler; belirli roundlarda zırh oranları ile Special, Elite ve Ultra dağılımları elle tanımlanan değişiklikler alır. |
| REQ-014 | Her düşman öldüğünde tek bir fiziksel gold paketi düşürecek ve oyuncu bunu yakınlıkla toplayacaktır. | Her düşman garanti gold oluşturur; oyuncu belirlenen toplama mesafesine yaklaştığında paket kendisine çekilir ve mevcut koşunun gold bakiyesine otomatik eklenir. |
| REQ-015 | Her round sonunda geliştirme ekranı açılacaktır. | Tamamlanan rounddan sonra oyuncu geliştirme ekranına girer; ekran kapandığında kısa bir geri sayım gösterilir ve sonraki round otomatik başlar. |
| REQ-016 | Oyuncu gold ile koşu içi geliştirmeler alacaktır. | Satın alma mevcut koşunun gold'unu kullanır ve mevcut build'i değiştirir. |
| REQ-017 | Koşu içi geliştirmeler farklı gelişim paternleri kurabilmelidir. | Aynı klasik silah farklı geliştirme dalları üzerinden farklı build kimliklerine ilerleyebilir. |
| REQ-018 | Gold ve koşu içi build yalnızca mevcut koşuda korunacaktır. | Oyuncu öldüğünde gold, silah geliştirmeleri ve build sıfırlanır. |
| REQ-019 | Her round için belirli bir toplam düşman havuzu bulunacaktır. | Düşmanlar havuz tükenene kadar dinamik spawn sisteminden kesintisiz gelir; havuz tükendiğinde yeni spawn durur. |
| REQ-073 | Gold'un tek kaynağı öldürülen düşmanlar olacaktır. | Çevre objeleri ve round tamamlama ayrıca gold kazandırmaz. |
| REQ-074 | Gold ödülü rastgele olmayacaktır. | Düşmanın temel gold değerine versiyonu ve taşıdığı zırh parçaları için belirlenen ek değerler uygulanır. |
| REQ-075 | Her düşman tek bir gold paketi düşürecektir. | Paketin değeri düşmanın hesaplanan toplam ödülüne eşittir; aynı ölüm birden fazla gold nesnesi üretmez. |
| REQ-076 | Yerdeki gold round sırasında zamanla kaybolmayacaktır. | Toplanmayan paket round bitene kadar yerde kalır; round tamamlandığında kalan bütün paketler otomatik olarak oyuncuya çekilip toplanır. |
| REQ-077 | Harcanmayan gold aynı koşu içinde korunacaktır. | Round sonunda kalan bakiye sonraki rounda aktarılır; koşu bittiğinde sıfırlanır. |
| REQ-078 | Round sonu geliştirme ekranı üç geçerli kart sunacaktır. | Oyuncu en fazla bir kart satın alabilir veya pas geçebilir; ön koşulu sağlanmayan ve kullanılamayan seçenekler gösterilmez. |
| REQ-079 | Geliştirme ekranı bir ücretli yenileme ve kademeli geliştirmeler kullanacaktır. | Her round ekranı bir kez yenilenebilir; geliştirmeler maksimum kademeye kadar tekrar alınabilir ve ileri kademeler daha pahalıdır. |

## Kalıcı profil, istatistik ve XP

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-020 | Oyuncunun kalıcı bir yerel profili olacaktır. | Profil internet gerektirmeden yerel olarak saklanır. |
| REQ-021 | En yüksek round, başarımlar ve açılan içerikler kalıcı olacaktır. | Yeni koşu başlatıldığında bu veriler sıfırlanmaz. |
| REQ-022 | Profil genel ve koşu bazlı istatistikleri tutacaktır. | Toplam kill, düşman tipi/versiyonu başına kill ve koşu sonuçları kaydedilebilir. |
| REQ-023 | Profil seviyesi deneyimi gösterecek ve yeni seçenekler açacaktır. | Mevcut kapsamda seviye ilerlemesi yeni silahlar ve geliştirme dalları açabilir. |
| REQ-024 | Profil seviyesi doğrudan savaş gücü vermeyecektir. | Profil seviyesi hasar, sağlık veya başlangıç gold'u sağlamaz. |
| REQ-025 | Yalnızca tamamlanan roundlar round XP'si verecektir. | Tamamlanmamış round için round tamamlama XP'si hesaplanmaz. |
| REQ-026 | Ölümle yarım kalan rounddaki kill'ler korunacaktır. | Bu kill'ler istatistiklere ve kill XP'sine eklenebilir. |
| REQ-027 | Gold işlemleri ve oynama süresi profil XP'si vermeyecektir. | Gold kazanma, gold harcama ve yalnızca geçen süre XP kaynağı değildir. |
| REQ-028 | XP koşu bitişinde ayrıntılı gösterilecektir. | Koşu bitiş ekranı kazanılan XP'nin dökümünü gösterir. |

## Karakter kapsamı

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-029 | Mevcut oyunda tek bir oynanabilir karakter ve bu karakter için üç farklı skin olacaktır. | Üç skin de başlangıçtan itibaren açıktır ve oynanışı etkilemez; ek oynanabilir karakterler oyun tamamlandıktan sonra ele alınır. |

## Kontrol ve kamera

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-030 | Kamera uzak ve yükseltilmiş üçüncü şahıs açısında olacaktır. | Oynanış tam top-down açı kullanmaz. |
| REQ-031 | Kamera yönü ve açısı sabit olacaktır. | Oyuncu kamerayı elle döndüremez. |
| REQ-032 | Kamera oyuncuyu yumuşak biçimde takip edecektir. | Oyuncu normalde ekran merkezine yakın kalır ve takip ani sıçrama yapmaz. |
| REQ-033 | Kamera nişan yönüne doğru sınırlı kadraj kayması yapacaktır. | Fare imleci veya sağ analog yönü kadrajı belirlenen sınır içinde kaydırır. |
| REQ-034 | Savaş sırasında zoom büyük ölçüde sabit kalacaktır. | Normal çatışmada sürekli veya ani zoom değişimi yapılmaz. |
| REQ-035 | Büyük görüş engelleri saydamlaşacaktır. | Kamera ile oyuncu arasındaki duvar/büyük nesne saydamlaşır; kamera aniden yaklaşmaz. |
| REQ-036 | WASD hareketi kamera yönüne göre çalışacaktır. | Hareket eksenleri sabit kamera yönüne göre hesaplanır. |
| REQ-037 | Hareket ve nişan birbirinden bağımsız olacaktır. | Oyuncu bir yöne hareket ederken başka bir yöne ateş edebilir. |
| REQ-038 | Karakter fare imlecinin dünyadaki yönüne dönecektir. | Fare konumu oyun dünyasına yön olarak çevrilir ve karakter bu yöne bakar. |
| REQ-039 | Otomatik silahlar sol tık tutulduğunda ateşe devam edecektir. | Sol tık bırakılana kadar otomatik ateş sürer. |
| REQ-040 | Gamepad çift analog kontrolü destekleyecektir. | Sol analog hareketi, sağ analog nişanı ve kadraj kayma yönünü yönetir. |

## Hayatta kalma sistemi

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-041 | Oyuncunun canı hasar alınmayan bir sürenin ardından otomatik yenilenecektir. | Oyuncu belirlenen süre boyunca hasar almazsa can yenilenmesi başlar. |
| REQ-042 | Oyuncunun dash veya dodge hareketi olmayacaktır. | Klavye-fare ve gamepad kontrollerinde dash, dodge veya bunlara karşılık gelen bir savunma hareketi bulunmaz. |
| REQ-043 | Zombi teması tek başına oyuncuya hasar vermeyecektir. | Hasar yalnızca zombinin saldırı animasyonuna bağlı vuruş oyuncuya isabet ettiğinde uygulanır. |
| REQ-044 | Oyuncu hasar aldıktan sonra hasar koruması kazanmayacaktır. | Genel veya düşman başına dokunulmazlık süresi uygulanmaz; aynı anda isabet eden saldırıların hasarları üst üste uygulanabilir. |
| REQ-045 | Oyuncu hasar aldığında kontrol kesintisi yaşamayacaktır. | Hareket, nişan ve ateş devam eder; isabet görsel, işitsel ve kamera geri bildirimiyle belirtilir. |
| REQ-046 | Oyuncu ile zombiler arasında katı çarpışma olacaktır. | Oyuncu zombilerin içinden geçemez ve zombiler oyuncunun hareket yolunu fiziksel olarak kapatabilir. |
| REQ-047 | Zombiler birbirleriyle yumuşak ayrışma kullanacaktır. | Zombiler aynı noktada üst üste binmez; birbirlerini hafifçe iterek sürü halinde akar ve dar geçitlerin tamamen tıkanma riskini azaltır. |
| REQ-048 | Oyuncunun tek canı olacak ve yeniden canlanma bulunmayacaktır. | Oyuncu canı sıfıra ulaştığında koşu doğrudan biter; gold, silah geliştirmeleri ve build için mevcut koşu sıfırlama kuralları uygulanır. |

## Meta akış

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-049 | Koşuya giriş ve çıkış akışı hızlı ve doğrudan olacaktır. | Aynı oyun oturumunda son skin ve silah hazır seçili gelir; oyun yeniden açıldığında varsayılan seçimlere dönülür; yeni içerikler Run Results ekranında otomatik açılarak sonraki seçim ekranında kullanılabilir olur. |

## Silahlar

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-050 | Oyuncu koşu başında bir klasik silah seçecektir. | Koşuya seçilen tek klasik silahla başlanır. |
| REQ-051 | Klasik silah bütün koşu boyunca korunacaktır. | Geliştirmeler silahı dönüştürür; klasik silah kalıcı olarak kaybolmaz. |
| REQ-052 | Belirlenen başlangıç roundundan sonra her round Special Weapon çıkma ihtimaline sahip olacaktır. | Uygun roundda genellikle bir, ileri roundlarda nadir ihtimalle iki Special Weapon çıkabilir; aynı anda yalnızca biri yerde bulunur ve yerde bekleyen veya kullanımı süren Special Weapon bitmeden sonraki spawn olmaz. |
| REQ-053 | Special Weapon spawnı açıkça bildirilecektir. | Belirgin ses, ışık sütunu ve HUD işareti birlikte kullanılır. |
| REQ-054 | Special Weapon oyuncuyu güvenli rotasından çıkmaya teşvik edecektir. | Seçilen nokta erişilebilirdir fakat oyuncunun bulunduğu güvenli bölgenin dışında olabilir. |
| REQ-055 | Special Weapon'ın kullanım ve yerde bekleme süreleri ayrı olacaktır. | Kullanım süresi yalnızca alındığında başlar; yerdeyken ayrı kaybolma süresi işler. |
| REQ-056 | Special Weapon kullanımında mermi ve yeniden doldurma gerekmeyecektir. | Aktif süre boyunca mermi sınırsızdır ve reload yapılmaz. |
| REQ-057 | Special Weapon klasik silahı geçici olarak devre dışı bırakacaktır. | Klasik silah kaybolmaz; süre bitince otomatik olarak yeniden etkinleşir. |
| REQ-058 | Special Weapon süresi açıkça gösterilecektir. | HUD kalan süreyi gösterir; son saniyelerde görsel ve işitsel uyarı verir. |
| REQ-059 | Special Weapon round sonu geliştirme ağacından etkilenmeyecektir. | Klasik silah build'i Special Weapon özelliklerini değiştirmez. |

## Düşman ve spawn sistemi

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-060 | Temel düşman ailesi zombi olacaktır. | Mevcut haritadaki düşman kimliği farklı kıyafetlere sahip zombilerden oluşur. |
| REQ-061 | Aynı temel düşman roundlarla modüler biçimde güçlenecektir. | Miğfer, gövde/kol zırhı, bacak zırhı veya onaylı bileşenler aynı düşman üzerine eklenebilir. |
| REQ-062 | Düşmanın Standard, Special, Elite ve Ultra versiyonları olacaktır. | Her versiyon kendi görünüm ve özel yeteneğiyle ayrılabilir ancak zombi kimliğini korur. |
| REQ-063 | Düşman görünümü oynanış durumunu yansıtacaktır. | Mevcut zırh parçaları ve kırılma durumları düşman modelinde görülebilir. |
| REQ-064 | Düşman hareketlerinde çeşitlilik olacaktır. | Yürüme, koşma, aksama ve sendeleme animasyonları kullanılabilir. |
| REQ-065 | Spawn noktaları tasarımcı tarafından sabit yerleştirilecektir. | Düşmanlar yalnızca tanımlanmış spawn noktalarından çıkar. |
| REQ-066 | Spawn noktalarının yoğunluğu oyuncuya göre dinamik olacaktır. | Her nokta eşit sayıda düşman üretmez; çok uzak bir noktanın ağırlığı sıfıra düşebilir. |
| REQ-067 | Spawn ağırlığı onaylanan konumsal etkenleri kullanacaktır. | Oyuncunun halka bölgesi, hareket yönü, yol mesafesi, nokta cooldownı, son kullanım, gelen düşman ve yerel yoğunluk dikkate alınabilir. |
| REQ-068 | Spawn geçerli navigasyon yolu gerektirecektir. | Oyuncuya ulaşan geçerli yol bulunmayan nokta spawn için kullanılmaz. |
| REQ-069 | Spawnlar adil tepki mesafesini koruyacaktır. | Düşman oyuncunun hemen yanında veya tepki veremeyeceği bir konumda belirmez. |
| REQ-070 | Aktif spawnlar kontrollü dağıtılacaktır. | Bütün düşmanlar sürekli tek noktadan çıkmaz; nokta cooldownı ve yerel yoğunluk kullanılabilir. |
| REQ-071 | Special, Elite ve Ultra versiyonlar kontrollü biçimde tanıtılacaktır. | Her versiyon belirlenen ilk roundunda garantili görünür; sonraki roundlarda ağırlıklı düşman havuzuna katılır. |
| REQ-072 | Spawn sistemi hedef aktif düşman sayısı ve teknik güvenlik tavanı kullanacaktır. | Sistem rounda ait hedef aktif sayıyı toplam düşman havuzundan tamamlar; hiçbir durumda ayrı teknik güvenlik tavanını aşmaz. |

## Bölgesel zırh ve hasar

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-080 | Zırh üç bağımsız bölgeden oluşacaktır. | Miğfer başı, gövde zırhı gövde/kolları, bacak zırhı alt vücudu korur. |
| REQ-081 | Zırh parçalarının farklı kombinasyonları kullanılabilecektir. | Düşman bir, iki, üç veya hiçbir zırh parçasına sahip olabilir. |
| REQ-082 | Doğrudan vuruş yalnızca vurulan bölgeyi değerlendirecektir. | Zırhlı bölge zırh hasarı, açık bölge doğrudan can hasarı alır. |
| REQ-083 | Zırh kırılma artan hasarı cana aktarmayacaktır. | Tek vuruş zırhı aşsa bile kalan hasar aynı vuruşta düşman canına geçmez. |
| REQ-084 | Zırh kırıldıktan sonra bölge açık hale gelecektir. | Sonraki doğrudan vuruşlar o bölgede can hasarı oluşturabilir. |
| REQ-085 | Patlama hasarı kafa, üst ve alt vücut arasında oranlarla paylaştırılacaktır. | Tek patlamanın toplam hasarı parça sayısına göre çoğalmaz. |
| REQ-086 | Patlamanın zırhlı bölge payı ilgili zırha gidecektir. | Bir patlama mevcut bütün zırh parçalarına kendi bölge paylarıyla hasar verebilir. |
| REQ-087 | Patlamanın açık bölge payı doğrudan cana gidecektir. | Kısmen zırhlı düşman açık bölgelerin toplam payı kadar can hasarı alabilir. |
| REQ-088 | Tam zırhlı düşman patlamadan önce zırh hasarı alacaktır. | Miğfer, gövde ve bacak zırhı birlikte mevcutken bir parça kırılana kadar patlama can hasarı oluşturmaz. |
| REQ-089 | Patlamanın can hasarında bölgesel kritik çarpan olmayacaktır. | Cana ulaşan kafa, üst ve alt vücut payları ayrıca kritik çarpanla büyütülmez. |
| REQ-090 | Patlamada da zırh artan hasarı cana geçirmeyecektir. | Bir bölge payı zırhı kırarsa o payın kalanı aynı patlamada cana aktarılmaz. |

## Dünya, harita ve görsel kimlik

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-100 | Oyunun genel görsel tarzı stilize gerçekçilik olacaktır. | Karakter, düşman, çevre ve efektler bu yaklaşım içinde kalır. |
| REQ-101 | Güncel kapsam tek haritadır. | İlk ürün kapsamına başka konsept harita eklenmez. |
| REQ-102 | Harita eski Japon kalesi, sarayı ve bahçeleri temasında olacaktır. | Mimari ve çevre bu konsepte bağlıdır. |
| REQ-103 | Atmosfer yumuşak fakat gergin olacaktır. | Pembe/mor sakura, yoğun yeşillik, seyrek polen/yaprak, hafif sis ve yağmur sonrası hissi birlikte kullanılabilir. |
| REQ-104 | Atmosfer oynanış okunabilirliğini bozmamalıdır. | Sis, parçacık ve bitkiler düşmanları veya nişan yönünü kapatmaz. |
| REQ-105 | Oyuncu için önemli öğeler çevreden ayrılacaktır. | Gold, Special Weapon ve savaş uyarıları çevre paletinde görünür kalır. |
| REQ-106 | Harita merkez ve çevresindeki halka rotadan oluşacaktır. | Merkez saray avlusu, kesintisiz dış halka ve merkeze bağlanan radyal kestirmeler bulunur. |
| REQ-107 | Harita Round 1'den itibaren tamamen açık olacaktır. | Büyük kapatma sistemi kullanılmaz; merkez sürekli geçilebilir ve halka iki yönde dolaşılabilir. |
| REQ-108 | Kuzey bölgesi saray önü ve geniş taş yol olacaktır. | Bölge açık, geniş ve uzun görüşlüdür; doğal kapı noktaları bulunabilir. |
| REQ-109 | Doğu bölgesi sakura bahçesi olacaktır. | Orta genişlikte yollar, ağaçlar, fenerler ve görüşü tamamen kapatmayan alçak duvarlar kullanılabilir. |
| REQ-110 | Güney bölgesi koi göleti ve köprü hattı olacaktır. | Dar ve riskli köprüye paralel alternatif kıyı yolu bulunur. |
| REQ-111 | Batı bölgesi bambu bahçesi ve kapalı koridor olacaktır. | En az iki bağlantı bulunur; çatılar ve bambular görüşü kapatırsa saydamlaşır. |
| REQ-112 | Halka rota sürekli aynı döngüyü teşvik etmeyecek şekilde çeşitlenecektir. | Merkez kestirmeleri, değişen yol genişlikleri, engeller, görüş çizgileri ve Special Weapon konumları rota seçimini etkiler. |
| REQ-113 | Çevre büyük ölçüde statik olacaktır. | Küçük kırılabilir objeler ve tepkisel bitkiler kullanılabilir; büyük fizik yıkımı temel sistem değildir. |
| REQ-114 | Harita sınırlı yükseklik ve belirgin landmark'lar kullanacaktır. | Karmaşık çok katlı iç mekânlar kullanılmaz ve bölgeler görsel olarak ayırt edilebilir. |
| REQ-115 | Japon zırh görünümü bölgesel sistemi yansıtacaktır. | Kabuto/maske başı, dō/kote üst vücudu, haidate/suneate alt vücudu temsil edebilir. |

## Vuruş hissi, animasyon, efekt ve ses

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-120 | Şiddet seviyesi orta düzeyde stilize olacaktır. | Kan, görünür yara, güçlü hit reaction ve sınırlı parçalanma kullanılabilir. |
| REQ-121 | Vuruş geri bildirimi katmanlı olacaktır. | Ateş hareketi/sesi, projectile izi, yüzey darbesi, hit reaction, zırh tepkisi, ölüm tepkisi, kamera ve katmanlı ses birlikte değerlendirilir. |
| REQ-122 | Hit reaction darbenin bağlamına göre değişecektir. | Bölge, yön, silah gücü, zırh durumu, düşman versiyonu, mevcut hareket ve tekrarlı darbe yoğunluğu tepkiyi etkiler. |
| REQ-123 | Hafif darbeler her zaman düşmanı durdurmayacaktır. | Hafif silahlar küçük tepkiler üretirken güçlü silahlar sendeleme veya geri savrulma yaratabilir. |
| REQ-124 | Sürekli stun-lock önlenecektir. | Tekrarlı vuruşlar düşmanı süresiz tepkide tutmaz. |
| REQ-125 | Geri bildirim gücü olayın önemine göre artacaktır. | Normal flesh hit, armor hit, armor break, critical hit, death ve Elite/Ultra death artan geri bildirim hiyerarşisine sahiptir. |
| REQ-126 | Kamera tepkisi kontrollü ve ayarlanabilir olacaktır. | Sarsıntı nişanı bozacak düzeye çıkmaz ve ayarlardan azaltılabilir. |
| REQ-127 | Hit-stop yalnızca önemli darbelerde kısa kullanılacaktır. | Otomatik silahın her mermisi global hit-stop oluşturmaz; güçlü darbe, zırh kırılması veya güçlü ölüm kullanabilir. |
| REQ-128 | Kalabalık savaşlarda efekt ve ses yoğunluğu sınırlandırılacaktır. | Eşzamanlı geri bildirim okunabilirliği ve önemli olayların algılanmasını korur. |
| REQ-129 | Zırh hasarı görsel ve işitsel olarak izlenebilecektir. | Darbe, çatlama, deformasyon, parça kopması ve kırılma sesleri zırh durumuna göre kullanılabilir. |

## Ses ve atmosfer ayrıntıları

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-143 | Müzik hibrit Japon-sinematik kimlik kullanacaktır. | Geleneksel Japon enstrüman karakteri modern sinematik vurmalılar, düşük frekanslı gerilim dokuları ve ölçülü elektronik katmanlarla birleşir; tek taraf baskın hale gelmez. |
| REQ-144 | Müzik oynanış durumuna adaptif tepki verecektir. | Aktif düşman sayısı, güçlü düşman varlığı, round ilerlemesi ve oyuncunun tehlike durumu müzik katmanlarını etkiler; round arasında yoğunluk geri çekilir. |
| REQ-145 | Harita bölgeleri ayrı çevresel ses katmanlarına sahip olacaktır. | Kuzeyde saray/avlu, doğuda sakura/doğa, güneyde su/köprü, batıda bambu/koridor kimliği duyulur ve bölge geçişleri yumuşaktır. |
| REQ-146 | Düşman ses kimliği fiziksel ve okunabilir olacaktır. | Doğaüstü/büyülü sesler kullanılmaz; nefes, seslenme, beden, ayak, kıyafet ve zırh katmanları kullanılır; güçlü sürümler ve saldırı hazırlıkları yön/tehdit bilgisi verir. |
| REQ-147 | Atmosfer kontrollü biçimde gerilecektir. | Yağmur sonrası kapalı hava korunur; tam gün/gece veya büyük hava dönüşümü yapılmaz; ses, rüzgâr, uzak olaylar, çevresel hareket ve sessizlik anları okunabilirliği bozmadan yoğunlaşabilir. |

## Üretim araçları

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-130 | Oyun Unity ile geliştirilecektir. | Çalışan oyun projesi Unity tabanlıdır. |
| REQ-131 | Modelleme, render ve asset üretiminde Blender kullanılacaktır. | Bu üretim alanlarının kaynak çalışmaları Blender iş akışında yürütülebilir. |
| REQ-132 | Higgsfield ve Adobe araçları içerik üretim teknolojileri arasında yer alacaktır. | Adobe Firefly, Adobe Illustrator ve ihtiyaç duyulan diğer Adobe araçları animasyon, ışık, ses, efekt, asset, grafik, evren ve ortam üretimlerinde kullanılabilir; Cascadeur kullanılmaz. |
| REQ-133 | Claude üretim araçlarını MCP bağlantıları üzerinden yönetecektir. | Claude, verilen proje promptlarıyla Higgsfield, Adobe araçları ve Blender üzerinde üretim ve gerekli işlemleri yürütebilir. |

## Implementasyon ve çoklu ajan yönetimi

| ID | Gereksinim | Kabul ölçütü |
|---|---|---|
| REQ-134 | Mevcut Unity prototipi nihai oyunun başlangıç kod tabanı olacaktır. | Çalışma `prototype/DeathsDemolition_PlayableConcept` üzerinde ilerler; toplu temizden yeniden yazım yapılmaz. |
| REQ-135 | İlk sağlamlaştırma ve dikey kesit Unity `2022.3.22f1` ve URP `14.0.10` üzerinde yürütülecektir. | Sürüm veya render pipeline değişikliği ayrı kritik kullanıcı kararı olmadan yapılmaz. |
| REQ-136 | Prototip üretim tabanına aşamalı dönüştürülecektir. | Çalışan modüller testle korunur; geçici adlar, değerler ve yapılar çalışma paketleri halinde ele alınır. |
| REQ-137 | On uzmanlık alanı için Claude ve Codex'te ayrı konu oturumları bulunacaktır. | Her oturum kendi alanının tasarım ve prompt/rapor giriş-çıkış penceresi olarak kullanılır; ana Codex oturumu merkezi koordinasyonu yapar. |
| REQ-138 | Her çalışma paketi ihtiyaç duyduğu Kadro uzmanlarını kullanacaktır. | Prompt gerekli uzmanları sayı sınırı olmadan göreve dahil eder; bağımsız işler paralel, dosya veya sistem çakışmaları entegrasyon sorumlusu altında yürütülür. |
| REQ-139 | Kullanıcı yalnızca kritik olaylarda durdurulacaktır. | Onaylı kapsam içindeki rutin teknik kararlar raporlanarak uygulanır; kapsam/karar değişikliği, maliyet, geri dönüşsüzlük, ciddi kalite tavizi veya gerçek engel kullanıcıya yükseltilir. |
| REQ-140 | Ajanlar görev tamamlanana kadar çalışmayı sürdürecektir. | Ara rapor nihai teslim sayılmaz; hata düzeltme ve yeniden test kabul kriterleri sağlanana kadar devam eder veya kritik engel kanıtıyla durur. |
| REQ-141 | Her çalışma paketi standart rapor üretecektir. | Yapılan işler, değişen dosyalar/assetler, test sonuçları, bilinen sorunlar, entegrasyon etkileri ve karar gerektiren noktalar raporlanır. |
| REQ-142 | Git çoklu oturum ve ajan kullanımına karşı korunacaktır. | Her paket benzersiz kimlik, branch/worktree sahibi ve commit sorumlusu taşır; dirty-state kontrolü, dosya sahipliği, test kapısı ve kontrollü entegrasyon uygulanır; force push, paylaşılan branch'te rebase/reset ve credential commit yasaktır. |

## Henüz sayısallaştırılmayan kabul ölçütleri

Aşağıdaki konular kararlaştırılana kadar gereksinimlerde kesin sayı kullanılmaz:

- Can, hasar ve zırh değerleri.
- Round başına düşman artış formülleri.
- Patlama bölge oranları.
- Profil XP ve seviye eğrisi.
- Gold maliyetleri ve geliştirme değerleri.
- Special Weapon süreleri ve spawn roundları.
- Otomatik can yenilenmesinin hasarsız bekleme süresi, hızı ve sınırı.

## Kayıt kuralı

- Yeni gereksinimler yalnızca kullanıcı tarafından verildiğinde veya kullanıcı açıkça onayladığında eklenir.
- Pusula önerileri kullanıcı tarafından onaylanmadan bu dosyaya yazılmaz.
