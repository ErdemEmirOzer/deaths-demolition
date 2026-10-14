# 02 — Akışlar, Mimari ve Teknolojiler

Bu dosyadaki diyagramlar yalnızca onaylanmış kararların ilişkisini gösterir. Sınıf yapısı, Unity paketi veya henüz seçilmemiş teknik çözüm tanımlamaz.

## Teknoloji sınırı

| Alan | Onaylanan seçim |
|---|---|
| Ürün | Bilgisayar oyunu |
| Oyun motoru | Unity `2022.3.22f1` |
| Render pipeline | URP `14.0.10` |
| Modelleme, render ve asset üretimi | Blender |
| İçerik kaynakları | Lisanslı kütüphaneler ve şirket içi üretim; Adobe Illustrator ve ihtiyaç duyulan diğer Adobe araçları |
| Üretim yönetimi | Alan paketleri ve otomatik kapılar |
| Ağ modeli | Güncel kapsamda ağ yok; tek oyunculu ve çevrimdışı |
| Kalıcı veri | Yerel profil, açılan içerik, başarım ve istatistikler |
| Oyun dili | İngilizce |

## FLOW-001 — Ana koşu döngüsü

```mermaid
flowchart TD
    A["Koşu başlar"] --> B["Klasik silah seçilir"]
    B --> C["Round 1: merkez saray avlusu"]
    C --> V["Temel formül + belirli roundlarda elle tanımlanan zırh ve düşman versiyonu dağılımı"]
    V --> W["Special / Elite / Ultra: belirlenen roundda garantili tanıtım, sonrasında ağırlıklı havuz"]
    W --> P["Roundun toplam düşman havuzu"]
    P --> D["Havuzdan kesintisiz dinamik spawn ve savaş"]
    D --> E{"Oyuncu öldü mü?"}
    E -- "Evet" --> F["Koşu biter; yeniden canlanma yok"]
    F --> G["Koşu istatistikleri ve XP dökümü"]
    G --> H["Gold, silah geliştirmeleri ve build sıfırlanır"]
    H --> I["Kalıcı profil, rekor, başarımlar ve açılan içerikler korunur"]
    E -- "Hayır" --> J{"Düşman havuzu tükendi mi?"}
    J -- "Hayır" --> D
    J -- "Evet" --> Q["Yeni spawn durur"]
    Q --> R{"Yaşayan düşman kaldı mı?"}
    R -- "Evet" --> S["Kalan düşmanlarla savaş"]
    S --> E
    R -- "Hayır" --> K["Round tamamlanır"]
    K --> L["Tamamlanan round ve kill XP hesaplanır"]
    L --> M["Round sonu geliştirme ekranı"]
    M --> N["Gold ile build geliştirilir"]
    N --> T["Geliştirme ekranı kapanır"]
    T --> U["Kısa geri sayım"]
    U --> O["Sonraki round otomatik başlar"]
    O --> V
```

## FLOW-002 — Koşu verisi ile kalıcı profil ayrımı

```mermaid
flowchart LR
    RUN["Aktif koşu"] --> RG["Gold"]
    RUN --> RW["Klasik silah geliştirmeleri"]
    RUN --> RB["Kurulan build"]
    DEATH["Oyuncu ölümü"] --> RESET["Koşu verisini sıfırla"]
    RG --> RESET
    RW --> RESET
    RB --> RESET

    RESULT["Koşu sonucu"] --> PROFILE["Yerel kalıcı profil"]
    PROFILE --> HR["En yüksek round"]
    PROFILE --> ACH["Başarımlar"]
    PROFILE --> UNLOCK["Açılan içerikler"]
    PROFILE --> LEVEL["Profil seviyesi"]
    PROFILE --> STATS["Genel ve düşman başına istatistikler"]
```

## FLOW-003 — Profil XP akışı

```mermaid
flowchart TD
    EVENT["Koşu olayları"] --> ROUND{"Round tamamlandı mı?"}
    ROUND -- "Evet" --> RXP["Round XP"]
    ROUND -- "Hayır" --> NORXP["Round XP yok"]
    EVENT --> KILL["Kill'ler"]
    KILL --> KXP["Kill XP ve istatistik"]
    EVENT --> GOLD["Gold kazanma veya harcama"]
    GOLD --> NOGXP["Profil XP yok"]
    EVENT --> TIME["Oynama süresi"]
    TIME --> NOTXP["Profil XP yok"]
    RXP --> SUMMARY["Koşu bitişi ayrıntılı XP dökümü"]
    KXP --> SUMMARY
    NORXP --> SUMMARY
```

## FLOW-004 — Hareket, nişan ve kamera

```mermaid
flowchart LR
    WASD["WASD / sol analog"] --> MOVE["Kamera yönüne göre hareket"]
    MOUSE["Fare imleci"] --> AIM["Dünya yönünde nişan"]
    STICK["Sağ analog"] --> AIM
    AIM --> ROTATE["Karakterin hedef yönüne dönmesi"]
    AIM --> OFFSET["Sınırlı kadraj kayması"]
    PLAYER["Oyuncu konumu"] --> FOLLOW["Yumuşak kamera takibi"]
    FOLLOW --> CAMERA["Sabit açı ve yön"]
    OFFSET --> CAMERA
    OCCLUDER["Araya giren büyük nesne"] --> FADE["Nesneyi saydamlaştır"]
    FADE --> CAMERA
```

Hareket ve nişan birbirinden bağımsızdır. Gamepad sağ analog hem nişanı hem de nişan yönündeki sınırlı kadraj kaymasını belirler.

## FLOW-005 — Silah durumu

```mermaid
stateDiagram-v2
    [*] --> KlasikSilah: Koşu başında seçim
    KlasikSilah --> KlasikSilah: Round sonu geliştirme
    KlasikSilah --> SpecialYerde: Başlangıç roundundan sonra ihtimal kontrolü; geçerli ilerleme aralığında rastgele spawn
    SpecialYerde --> KlasikSilah: Yerde bekleme süresi dolar
    SpecialYerde --> SpecialAktif: Oyuncu alır
    SpecialAktif --> KlasikSilah: Kullanım süresi dolar
```

- Special Weapon yerdeyken ayrı kaybolma süresi kullanır.
- Aktifken sınırsız mermiye sahiptir ve reload gerektirmez.
- Aktif süre boyunca klasik silah kaybolmaz, geçici olarak devre dışıdır.
- Round sonu geliştirmeleri Special Weapon'ı etkilemez.
- Uygun roundda genellikle bir, ileri roundlarda nadir ihtimalle iki Special Weapon çıkabilir.
- Aynı anda yalnızca bir Special Weapon yerde bulunur; yerde bekleyen veya aktif kullanılan Special Weapon süreci bitmeden sonraki spawn olmaz.

## FLOW-006 — Dinamik spawn noktası seçimi

```mermaid
flowchart TD
    TARGET["Roundun hedef aktif düşman sayısı"] --> NEED{"Havuzda düşman var, aktif sayı hedefin altında ve teknik tavan aşılmıyor mu?"}
    ACTIVE["Mevcut aktif düşman sayısı"] --> NEED
    CEILING["Aşılmayan teknik güvenlik tavanı"] --> NEED
    POOL["Round havuzunda kalan düşman sayısı"] --> NEED
    NEED -- "Hayır" --> HOLD["Yeni spawn oluşturulmaz"]
    NEED -- "Evet" --> POINTS["Tasarımcı tarafından yerleştirilen sabit spawn noktaları"]
    POINTS --> VALID{"Geçerli navigasyon yolu ve güvenli tepki mesafesi var mı?"}
    VALID -- "Hayır" --> ZERO["Bu seçimde kullanılmaz"]
    VALID -- "Evet" --> WEIGHT["Dinamik ağırlık"]
    PLAYER["Oyuncunun konumu, halka bölgesi ve hareket yönü"] --> WEIGHT
    PATH["Yürünebilir yol mesafesi"] --> WEIGHT
    HISTORY["Son kullanım ve nokta cooldownı"] --> WEIGHT
    DENSITY["Noktadan gelen ve çevredeki aktif düşman yoğunluğu"] --> WEIGHT
    BASE["Tasarımcı temel ağırlığı"] --> WEIGHT
    WEIGHT --> DISTRIBUTE["Uygun aktif noktalar arasında kontrollü dağılım"]
    DISTRIBUTE --> SPAWN["Seçilen sabit noktada spawn"]
    SPAWN --> ACTIVE
    KILL["Düşman ölümü"] --> ACTIVE
```

Çok uzak bir noktanın ağırlığı sıfır olabilir. Sistem bütün düşmanları sürekli aynı noktadan çıkarmaz ve oyuncunun hemen yanında spawn oluşturmaz. Düşman öldükçe aktif sayı azalır ve round havuzunda düşman kaldığı sürece sistem hedef sayıyı yeniden tamamlayabilir.

## FLOW-007 — Doğrudan isabet ve bölgesel zırh

```mermaid
flowchart TD
    HIT["Doğrudan isabet"] --> REGION{"Vurulan bölge"}
    REGION --> HEAD["Baş"]
    REGION --> UPPER["Gövde ve kollar"]
    REGION --> LOWER["Alt vücut ve bacaklar"]
    HEAD --> CHECK{"İlgili zırh var mı?"}
    UPPER --> CHECK
    LOWER --> CHECK
    CHECK -- "Hayır" --> HEALTH["Hasarı cana uygula"]
    CHECK -- "Evet" --> ARMOR["Hasarı yalnız ilgili zırha uygula"]
    ARMOR --> BREAK{"Zırh kırıldı mı?"}
    BREAK -- "Hayır" --> END["İsabet tamamlanır"]
    BREAK -- "Evet" --> DISCARD["Artan hasarı boşa çıkar"]
    DISCARD --> OPEN["Bölge sonraki isabetler için açık"]
```

## FLOW-008 — Patlama ve zırh

```mermaid
flowchart TD
    EXP["Patlamanın toplam hasarı"] --> SPLIT["Kafa, üst vücut ve alt vücut paylarına böl"]
    SPLIT --> H["Kafa payı"]
    SPLIT --> U["Üst vücut payı"]
    SPLIT --> L["Alt vücut payı"]
    H --> HC{"Miğfer var mı?"}
    U --> UC{"Gövde/kol zırhı var mı?"}
    L --> LC{"Bacak zırhı var mı?"}
    HC -- "Evet" --> HA["Payı miğfere uygula"]
    UC -- "Evet" --> UA["Payı gövde zırhına uygula"]
    LC -- "Evet" --> LA["Payı bacak zırhına uygula"]
    HC -- "Hayır" --> HP["Payı cana uygula"]
    UC -- "Hayır" --> HP
    LC -- "Hayır" --> HP
    HA --> OVER["Zırh kırılırsa artan pay boşa gider"]
    UA --> OVER
    LA --> OVER
```

- Toplam patlama hasarı zırh parçası sayısına göre çoğalmaz.
- Üç zırh da mevcutsa bir parça kırılana kadar patlama can hasarı oluşturmaz.
- Açık bölgeden cana giden patlama payında bölgesel kritik çarpan kullanılmaz.

## FLOW-009 — Haritanın topolojik yapısı

Bu diyagram ölçü veya kesin yerleşim planı değildir; yalnızca onaylanan bölge bağlantılarını gösterir.

```mermaid
flowchart TB
    N["Kuzey — Saray önü ve geniş taş yol"]
    E["Doğu — Sakura bahçesi"]
    S["Güney — Koi göleti, köprü ve alternatif kıyı yolu"]
    W["Batı — Bambu bahçesi ve kapalı koridor"]
    C["Merkez — Saray avlusu ve Round 1 başlangıcı"]

    N --- E
    E --- S
    S --- W
    W --- N
    N --- C
    E --- C
    S --- C
    W --- C
```

- Dış bağlantılar kesintisiz halka oluşturur.
- Merkez bağlantıları radyal kestirmelerdir.
- Merkez her zaman geçilebilir ve halka iki yönde dolaşılabilir.
- Harita Round 1'den itibaren tamamen açıktır.

## FLOW-010 — Oyuncu hasarı ve otomatik can yenilenmesi

```mermaid
flowchart TD
    TOUCH["Zombiyle yalnızca temas"] --> NODAMAGE["Hasar yok"]
    ATTACK["Zombi saldırı animasyonu"] --> HIT{"Saldırı oyuncuya isabet etti mi?"}
    HIT -- "Hayır" --> NODAMAGE
    HIT -- "Evet" --> DAMAGE["Oyuncuya hasar uygula"]
    DAMAGE --> WAIT["Hasarsız bekleme süresini yeniden başlat"]
    WAIT --> SAFE{"Belirlenen süre boyunca yeni hasar alınmadı mı?"}
    SAFE -- "Hayır" --> WAIT
    SAFE -- "Evet" --> REGEN["Otomatik can yenilenmesi başlar"]
    REGEN --> NEWDAMAGE{"Yeni hasar alındı mı?"}
    NEWDAMAGE -- "Evet" --> DAMAGE
    NEWDAMAGE -- "Hayır" --> REGEN
```

- Hasardan sonra koruma veya dokunulmazlık süresi yoktur.
- Aynı anda isabet eden birden fazla saldırının hasarı üst üste uygulanabilir.
- Oyuncu hasar aldığında hareket, nişan ve ateş kesilmez; görsel, işitsel ve kamera geri bildirimi verilir.
- Oyuncu zombilerin içinden geçemez; zombiler oyuncunun yolunu fiziksel olarak kapatabilir.
- Zombiler birbirleriyle yumuşak ayrışma kullanır; üst üste binmeden birbirlerini hafifçe iterek sürü halinde akar.

## FLOW-011 — Meta akış

```mermaid
flowchart LR
    MAIN["Main Menu"] --> SELECT["Skin & Weapon Selection"]
    SELECT --> RUN["Run"]
    RUN --> RESULTS["Run Results"]
    RESULTS --> MAIN
    MAIN --> PROFILE["Profile"]
    PROFILE --> MAIN
    MAIN --> STATS["Statistics"]
    STATS --> MAIN
    MAIN --> ACHIEVEMENTS["Achievements"]
    ACHIEVEMENTS --> MAIN
```

Akışın sade olması UI tasarımı ve kalite hedefini düşürmez.

- Skin & Weapon Selection her koşudan önce açılır.
- Aynı oyun oturumunda son kullanılan skin ve klasik silah hazır seçili gelir.
- Oyuncu doğrudan başlayabilir veya seçimleri değiştirebilir.
- Oyun yeniden açıldığında varsayılan skin ve klasik silah seçimine dönülür.

Run Results tek ekranda şunları gösterir:

- Ulaşılan round.
- Genel kill ve düşman versiyonu başına kill sayıları.
- Ayrıntılı XP dökümü.
- Profil seviyesi ilerlemesi.
- Açılan içerikler.
- Başarımlar.

Yeni içerikler Run Results ekranında vurgulanarak otomatik açılır; Claim işlemi gerekmez ve sonraki Skin & Weapon Selection ekranında kullanılabilir olur.

## FLOW-012 — Gold kazanma ve toplama

```mermaid
flowchart LR
    KILL["Düşman öldürülür"] --> VALUE["Temel değer + versiyon ve zırh ekleri"]
    VALUE --> DROP["Toplam değeri taşıyan tek gold paketi yere düşer"]
    DROP --> NEAR{"Oyuncu toplama mesafesinde mi?"}
    NEAR -- "Evet" --> MAGNET["Paket oyuncuya çekilir ve bakiyeye eklenir"]
    NEAR -- "Hayır" --> WAIT["Paket kaybolmadan yerde kalır"]
    WAIT --> END{"Round tamamlandı mı?"}
    END -- "Hayır" --> NEAR
    END -- "Evet" --> MAGNET
    MAGNET --> CARRY["Harcanmayan bakiye sonraki rounda aktarılır"]
```

Gold yalnızca düşman ölümlerinden gelir; çevre objeleri ve round tamamlama ek gold üretmez.

## FLOW-013 — Çalışma paketi ve rapor döngüsü

```mermaid
flowchart LR
    DEC["Onaylanmış kararlar"] --> MASTER["C-00 koordinasyonu"]
    MASTER --> OWNER["İlgili alan sahibi (S-XX)"]
    OWNER --> PACKAGE["Tek ve test edilebilir çalışma paketi"]
    PACKAGE --> TEAM["Gerekli uzmanlıklar"]
    TEAM --> REPORT["Standart rapor ve test kanıtı"]
    REPORT --> REVIEW{"Kabul kriterleri sağlandı mı?"}
    REVIEW -- "Hayır; kritik değil" --> PACKAGE
    REVIEW -- "Kritik karar gerekli" --> USER["Kullanıcı onayı"]
    USER --> MASTER
    REVIEW -- "Evet" --> NEXT["Sonraki çalışma paketi"]
    NEXT --> MASTER
```

## FLOW-014 — Paralel çalışma Git koordinasyonu

```mermaid
flowchart LR
    WP["Çalışma paketi kimliği"] --> OWNER["Branch/worktree ve commit sorumlusu"]
    OWNER --> FILES["Dosya sahipliği ve dirty-state kontrolü"]
    FILES --> WORK["Paralel veya sıralı uzman çalışması"]
    WORK --> INTEGRATE["Tek entegrasyon sorumlusu"]
    INTEGRATE --> TESTS["Derleme ve test kapısı"]
    TESTS --> COMMIT["Atomik ve geri döndürülebilir commit"]
    COMMIT --> QUEUE["Belgelenmiş entegrasyon sırası"]
```

Paylaşılan branch üzerinde force push, rebase veya reset yapılmaz; kimlik bilgileri ve sırlar repoya yazılmaz.

## FLOW-015 — Adaptif ses ve atmosfer

```mermaid
flowchart LR
    STATE["Round ilerlemesi, aktif tehdit ve oyuncu durumu"] --> MUSIC["Hibrit Japon-sinematik müzik katmanları"]
    REGION["Oyuncunun harita bölgesi"] --> AMBIENCE["Bölgesel çevresel ses katmanları"]
    ENEMY["Düşman versiyonu ve saldırı hazırlığı"] --> THREAT["Fiziksel, yönsel tehdit sesi"]
    WEATHER["Sabit yağmur sonrası kapalı hava"] --> ATMOS["Kontrollü rüzgâr, çevresel hareket ve sessizlik"]
    MUSIC --> MIX["Okunabilir savaş miksajı"]
    AMBIENCE --> MIX
    THREAT --> MIX
    ATMOS --> MIX
    BREAK["Round arası"] --> REST["Müzik ve gerilim geri çekilir"]
```

Büyük gün/gece veya hava dönüşümü kullanılmaz. Adaptif katmanlar oynanış okunabilirliğini ve önemli savaş uyarılarını bastırmaz.

## Sistem sınırları

```mermaid
flowchart LR
    INPUT["Kontrol"] --> PLAYER["Oyuncu hareketi ve nişan"]
    PLAYER --> CAMERA["Kamera takibi ve kadraj"]
    PLAYER --> WEAPON["Klasik / Special Weapon"]
    WEAPON --> DAMAGE["Hasar ve bölgesel zırh"]
    ROUND["Round durumu"] --> SPAWN["Dinamik spawn dağılımı"]
    SPAWN --> ENEMY["Zombi ve versiyonları"]
    ENEMY --> DAMAGE
    DAMAGE --> FEEDBACK["Hit reaction, VFX, SFX ve kamera tepkisi"]
    ROUND --> GOLD["Koşu gold'u ve geliştirme"]
    GOLD --> WEAPON
    ROUND --> RESULT["Koşu sonucu ve XP"]
    RESULT --> PROFILE["Yerel kalıcı profil"]
```

Bu sınırlar onaylanan sistem ilişkilerini gösterir; sınıf sayısı, veri formatı veya Unity uygulama yöntemi henüz kararlaştırılmamıştır.

## İçerik üretim akışı

```mermaid
flowchart LR
    BRIEF["Onaylı ürün kararları ve üretim paketi"] --> LIB["Lisanslı kütüphaneler"]
    BRIEF --> ADOBE["Adobe Illustrator ve diğer gerekli Adobe araçları"]
    BRIEF --> BLENDER["Blender"]
    LIB --> REVIEW["Üretim çıktısı, lisans kaydı ve rapor"]
    ADOBE --> REVIEW
    BLENDER --> REVIEW
    REVIEW --> UNITY["Unity — nihai oyun projesi"]
```

Cascadeur bu akıştan çıkarılmıştır. Aktarım formatları ve teknik import sözleşmeleri `Production Baseline Audit & Stabilization` kapsamında tanımlanacaktır.

## Kayıt kuralı

- Yeni akış veya mimari kararı yalnızca kullanıcı tarafından verildiğinde veya açıkça onaylandığında eklenir.
- Diyagramlar onaylanmış kuralların görsel karşılığıdır; boşlukları varsayımla doldurmaz.
