# TEST PLANI · `G-deaths-demolition-10`

**Faz** F3 (icradan önce) · **Teslimat tipi** `oynanis` · **Üreten** `test-engineer` · **Denetleyen** `gt@G-deaths-demolition-10`
**Kaynak ölçüt** `talep.md` KABUL KRİTERLERİ 1–27 + `niyet.md §6` (başarı ölçüsü / S5 his imzası)
**Bağlayıcı sınırlar** `niyet.md §9` (a–e) · `planning/` ve `game/` ajanlar için salt-okunur (kullanıcı/Pusula güncellemeleri meşrudur; denetim paket manifestine göre, bkz. OTOMATIK-KAPI K6)

> Bu belge bir KADRO artefaktıdır, oyun içi metin değildir. Oyun içindeki her metin İngilizcedir.
> Bu belge Genel Test Algoritması'nın (`system/18`) sekiz adımını sırayla işletir.

---

## 0 · BAĞLAYICI DOĞRULAMA AYRIMI (S5)

| | Otomatik kapı | His imzası |
|---|---|---|
| Kapsam | derleme temiz · PlayMode duman testi · konsol hatası ve kopuk referans sıfır · akış kapanışı | öncelik **1–5**: kontrol tepkiselliği, vuruş hissi, sürü baskısı, okunabilirlik, atmosfer |
| Sahip | `A2` (T1/T2) + `GT` (T3/T5) | **kullanıcı** (T4) |
| Rapor durumu | `geçti` / `kaldı` | kullanıcı oynayana kadar **`doğrulanmadı`** |
| Yasak | — | öncelik 1–5 için `kapandı` yazmak **yasaktır** |

**S1 çakışma kuralı:** 27 madde hedeflenir. Ucuz bir kriter ile öncelik 1–5 çakışırsa emek 1–5'e gider; kapanmayan madde **gerekçesiyle açık bırakılır**, "kapandı" yazılmaz.

---

## 1 · ADIM 1–2 · ÖLÇÜT AYRIŞTIRMA VE TİP TAYİNİ

Birleşik ölçütler bölündü. `K05` donanım gerçeği nedeniyle ikiye ayrıldı (`niyet §9d`).
`HI1–HI5` yeni kriter değildir; `niyet §6`/S5'in adlandırılmış karşılığıdır.

| # | İfade (indirgenmiş iddia) | Kaynak | Tip | Kritiklik |
|---|---|---|---|---|
| K01 | Uygulama Main Menu ile açılır; başlangıç sahnesi tek tıkla girilir | talep 1 | ikili | 3 |
| K02 | Main Menu'den Skin & Weapon Selection ekranına geçilir | talep 2 | ikili | 2 |
| K03 | Üç skin baştan açık ve yalnız görsel; klasik silah seçimi yalnız **açılmış** silahlar arasından yapılır | talep 3 | ikili | 2 |
| K04 | Run başlar ve oyuncu **merkez avluda** Round 1'e girer | talep 4 | ikili + eşik | 3 |
| K05a | Klavye+fare ile hareket ve nişan **birbirinden bağımsız** çalışır | talep 5 · `niyet §9d` | ikili + deneyim | 3 |
| K05b | Gamepad ile hareket ve nişan bağımsız çalışır | talep 5 · `niyet §9d` | ikili + deneyim | 3 |
| K06 | Zombiler yalnız tanımlı noktalardan, dinamik ağırlıkla ve **kesintisiz** spawn olur | talep 6 | ikili + eşik | 3 |
| K07 | Toplam havuz, hedef aktif sayı ve güvenlik tavanı formüllere göre doğru çalışır | talep 7 | eşik | 3 |
| K08 | Bölgesel vuruş, zırh kırılması ve **artan hasarın boşa gitmesi** gözlemlenebilir | talep 8 | ikili | 3 |
| K09 | Patlama hasarı üç-bölge zırh modelini kullanır (25/45/30) | talep 9 | ikili | 3 |
| K10 | Gold kazanılır ve round sonunda geliştirme satın alınabilir | talep 10 | ikili | 2 |
| K11 | Seçilen klasik silah koşu boyunca korunur ve build kimliği oluşur | talep 11 | ikili | 2 |
| K12 | Special Weapon geçerli bir noktada ve uygun round ilerlemesinde çıkar | talep 12 | eşik | 2 |
| K13 | Special Weapon sınırsız mermiyle süreli çalışır, sonra klasik silaha dönülür | talep 13 | ikili + eşik | 2 |
| K14 | Havuz bittiğinde kalan düşmanlar temizlenince round tamamlanır | talep 14 | ikili | 3 |
| K15 | Round End Upgrade ekranı açılır ve simülasyon donar | talep 15 | ikili | 3 |
| K16 | Kısa geri sayım (3 sn) sonrası sonraki round otomatik başlar | talep 16 | eşik | 2 |
| K17 | Oyuncu hasar almayınca can yenilenir (4.5 sn gecikme, 12 HP/s, tam cana kadar) | talep 17 | eşik | 2 |
| K18 | Oyuncu ölünce revive olmadan koşu biter | talep 18 | ikili | 3 |
| K19 | Run Results **tek ekranda** onaylanan 12 alanı gösterir | talep 19 | ikili | 2 |
| K20 | Profil XP, istatistik, başarım ve açılan içerik yerel olarak korunur | talep 20 | ikili | 3 |
| K21 | Ölümde gold ve koşu build'i sıfırlanır | talep 21 | ikili | 2 |
| K22 | Main Menu'ye dönülür | talep 22 | ikili | 2 |
| K23 | Aynı oturumdaki sonraki seçim ekranında son skin ve silah hazır gelir | talep 23 | ikili | 1 |
| K24 | Uygulama yeniden açıldığında varsayılan seçimlere dönülür | talep 24 | ikili | 1 |
| K25 | Oyun internet bağlantısı olmadan çalışır | talep 25 | ikili | 2 |
| K26 | Pause bütün simülasyonu durdurur (oyuncu, düşman, mermi, zamanlayıcı, round) | talep 26 | ikili + eşik | 3 |
| K27 | Normal kullanımda Console error veya missing reference bulunmaz | talep 27 | ikili | 3 |
| HI1 | Kontrol **tepkisel** hissettiriyor | `niyet §6` öncelik 1 | deneyim | 3 |
| HI2 | Silah ve isabet **hissi** var | `niyet §6` öncelik 2 | deneyim | 3 |
| HI3 | Zombi sürüsü **baskı** kuruyor | `niyet §6` öncelik 3 | deneyim | 3 |
| HI4 | Kamera ve oynanış **okunabilir** | `niyet §6` öncelik 4 | deneyim | 3 |
| HI5 | Japon bahçesi **yumuşak fakat gergin** | `niyet §6` öncelik 5 | deneyim | 2 |

Toplam sınanabilir iddia: **34** (27 madde → 28 satır, K05 bölündü + 5 his imzası + `O34` DEC-021'in elle-tanımlı yarısı — kendi kapsam boşluğu `G-02` ile birlikte okunur).

---

## 2 · ADIM 3 · KAPSAM MATRİSİ (ölçüt × kat)

**Okuma:** hücrede `TS-xxx` varsa o katta test **var**. `— gN` = **gereksiz** (gerekçe kodu). `⊘ G-xx` = **kapsam boşluğu**.

**Gereksiz gerekçe kodları**

| Kod | Gerekçe |
|---|---|
| `g1` | Sayısal eşiği yok — ölçüt tek atımlık doğru/yanlış; sayı ölçmek bilgi üretmiyor |
| `g2` | Sınır yüzeyi yok — tek yönlü durum geçişi; uç/bozuk girdi kombinasyonu bulunmuyor |
| `g3` | İnsan yargısı gerekmiyor — doğru/yanlış makine ile kesin ayrılıyor |
| `g4` | Sözleşme yüzeyi yok — ölçüt bir veri veya arayüz biçimi tanımlamıyor |
| `g5` | Makine ölçümü anlamsız — ölçütün kendisi bir insan yargısı |
| `g6` | Yokluk iddiası — ölçüt bir işlevin *bulunmamasını* söylüyor; koşacak davranış yok |

| # | T1 SÖZLEŞME | T2 DAVRANIŞ | T3 EŞİK | T4 DENEYİM | T5 DAYANIKLILIK |
|---|---|---|---|---|---|
| K01 | TS-103 | TS-224 | TS-305 | — g3 | TS-501 |
| K02 | TS-103 | TS-224 | — g1 | TS-408 | — g2 |
| K03 | TS-106 | TS-209 · TS-220 | — g1 | TS-408 | **⊘ G-05** |
| K04 | TS-103 | TS-224 | TS-310 | TS-404 | TS-501 |
| K05a | TS-104 | TS-222 | **⊘ G-10** | TS-401 | TS-506 |
| K05b | TS-104 | **⊘ G-01** | **⊘ G-01** | **⊘ G-01** | **⊘ G-01** |
| K06 | TS-101 | TS-217 · TS-218 | TS-306 | TS-403 | TS-504 |
| K07 | TS-101 | TS-201 · TS-202 · TS-203 · TS-204 *(kısmi — bkz. G-02)* | TS-303 | TS-403 | TS-504 |
| K08 | TS-106 | TS-205 | — g1 | TS-406 | TS-512 |
| K09 | TS-101 | TS-206 | — g1 | TS-406 | TS-512 |
| K10 | TS-106 | TS-212 | — g1 | TS-408 | TS-507 |
| K11 | TS-106 | TS-225 | — g1 | TS-402 | TS-508 |
| K12 | TS-101 | TS-213 | TS-311 | TS-404 | TS-508 |
| K13 | TS-107 | TS-214 | TS-312 | TS-402 | TS-508 |
| K14 | — g4 | TS-215 | — g1 | — g3 | TS-504 |
| K15 | TS-103 | TS-215 | — g1 | TS-408 | TS-507 |
| K16 | — g4 | TS-215 | TS-312 | — g3 | TS-506 |
| K17 | TS-101 | TS-210 | TS-312 | — g3 | TS-506 |
| K18 | TS-102 | TS-211 | — g1 | — g3 | TS-508 |
| K19 | TS-108 | TS-207 | — g1 | TS-407 | TS-508 |
| K20 | TS-102 | TS-207 · TS-208 · TS-209 · TS-221 | — g1 | — g3 | TS-501 · TS-502 |
| K21 | TS-102 | TS-211 · TS-212 | — g1 | — g3 | TS-508 |
| K22 | TS-103 | TS-224 | — g1 | — g3 | TS-509 |
| K23 | TS-102 | TS-220 | — g1 | — g3 | TS-509 |
| K24 | TS-102 | TS-220 | — g1 | — g3 | TS-501 |
| K25 | TS-109 | — g6 | — g1 | — g3 | TS-503 |
| K26 | — g4 | TS-216 | TS-313 | — g3 | TS-506 |
| K27 | TS-105 · TS-110 | TS-224 | TS-309 | TS-410 | TS-505 |
| HI1 | TS-104 | TS-222 | **⊘ G-10** | TS-401 | TS-506 |
| HI2 | — g4 | TS-205 | — g5 | TS-402 | TS-504 |
| HI3 | — g4 | TS-217 | TS-302 · TS-304 | TS-403 | TS-510 |
| HI4 | — g4 | — g5 | TS-301 | TS-404 | TS-504 |
| HI5 | — g4 | — g5 | TS-301 | TS-405 | **⊘ G-07** |

**Matris dışı tek test — `TS-511`.** Tek bir ölçüte değil, **bütün kanıt setine** bağlıdır: `D3` sıfırdan yeniden üretim, diğer 67 testin ortam bağımlılığını sınar. Bu yüzden bir hücreye yazılmaz; kapının **üstünde** durur. Sonucu bir ölçüt kapatmaz, ancak sapma verirse ilgili testlerin kanıtını `kırılgan`a çevirir.

---

## 3 · KAPSAM BOŞLUĞU DEFTERİ

`kapsam_boslugu = 9` (8 hücre + 1 kısmi kapsam). Bu sayı doğrudan `G4` kararının ağırlığına eklenir (`16 §2.2`).

| Boşluk | Hücre(ler) | Ne sınanmıyor | Neden | Kapanma koşulu |
|---|---|---|---|---|
| **G-01** | K05b · T2/T3/T4/T5 (4 hücre) | Gamepad davranışı, eşik, deneyim ve sınır durumu | `niyet §9d` — makinede gamepad donanımı **yok**. Eşleme yazılır, sözleşmesi TS-104'te sınanır, **davranışı sınanmaz**. | Kullanıcı bir gamepad taktığında MANUEL-TEST-KITI ek adım GP-1..GP-4 koşulur |
| **G-02** | K07 · T2 (kısmi) | DEC-021'in **elle-tanımlı yarısı**: belirli roundlarda elle verilen zırh oranları ve Special/Elite/Ultra dağılımı | `niyet §9e` — `t-0002` ile yalnız formül tarafı uygulanıyor. Uygulanmayan yarı test edilemez. | Elle-tanımlı katman prototipe girerse TS-226 açılır |
| **G-05** | K03 · T5 | "Kilitli silah seçilemez" sınır durumunun **ikinci koşudan sonra** sınanması | `niyet §9a` — unlock merdiveni tek koşuda tükeniyor (Level 4 = 525 XP kümülatif, R8 koşusu ≈545–600 XP). İkinci koşudan sonra kilitli silah **kalmıyor**; sınır durumu doğal olarak yok oluyor. | Profil sıfırlanarak (TS-501 kayıt silme) yapay olarak yeniden üretilir — bu **D3** koşusudur |
| **G-07** | HI5 · T5 | Atmosferin sınır koşulda (40 düşman + patlama + sis + Special Weapon ışığı) bozulup bozulmadığının **otomatik** kaydı | Görsel regresyon (referans kare karşılaştırması) bu turda kurulmuyor; tek kanıt insan gözü. | Görsel diff hattı kurulursa TS-514 açılır |
| **G-10** | K05a · T3, HI1 · T3 (2 hücre) | **Girdi → ekran gecikmesi** (input latency) sayısal ölçümü | Yüksek hızlı kamera veya donanım gecikme ölçeri yok. Editör Play Mode zaten ek gecikme katıyor; ölçülse bile ürün değeri temsil etmez. | Build alınıp harici ölçüm donanımıyla ölçülürse TS-314 açılır |

**Boşluk gizlenmemiştir.** Rapor `G4`'e bu tabloyla gider.

---

## 4 · ADIM 4 · RİSK AĞIRLIKLANDIRMA

Formül (`18 §3 Adım 4`): `ağırlık = kritiklik×2 + 2 (ilk tur/yeni alan) + 2 (niyet §9'da işaretli) + 1 (tek makine/tek koşu)`, tavan **10**.
Bu ilk tur olduğu için **+2 yeni alan** her teste uygulanır; "geçmiş 3 turda geçti −1" hiçbir teste uygulanmaz.

### 4.1 · TUR-KIRAN ölçütler — kırılırsa turun tamamı düşer

| Ölçüt | Neden turu düşürür |
|---|---|
| **K27** Konsol hatası / kopuk referans | S5'in otomatik kapı tanımının doğrudan maddesi. Kırmızıysa kapı **hiç** açılmaz, diğer ölçütlerin ölçümü kanıt sayılmaz. |
| **K01+K04+K22** Akış kapanışı | Akış kapanmıyorsa 10-15 dk'lık oynanış hiç oluşmaz; öncelik 1–5'in tamamı ölçülemez. Manuel kit koşulamaz. |
| **K05a** Bağımsız hareket/nişan | Öncelik 1'in maddi temeli. Kırılırsa HI1 ve HI2 birlikte düşer. |
| **K07** Havuz / hedef aktif / güvenlik tavanı | Kırılırsa HI3 (sürü baskısı) ve tüm T3 performans ölçümleri anlamsızlaşır — 40 tavanı aşılırsa fps ölçümü hangi yükte alındığı bilinmez. |
| **K26** Pause | Ölçüm ortamının kendisi. Pause sızdırıyorsa profiler örneği, sayaç okuması ve manuel gözlem **kanıt değeri kaybeder**. |
| **K14+K15+K16** Round döngüsü kapanışı | R1'den ileri gidilemezse Runner/Guard/Executioner hiç görülmez; F1 sıçrama ön ayarı bile round döngüsüne yaslanır. |

### 4.2 · Koşu sırası — ağır önce

Bir tur kesilirse en değerli kanıt elde kalmalıdır. Sıra:

```
1. ağırlık 10 : TS-203 TS-302 TS-219 TS-309 TS-105
2. ağırlık  9 : TS-301 TS-303 TS-313 TS-401 TS-402 TS-403 TS-404 TS-511
3. ağırlık  8 : TS-201 TS-202 TS-205 TS-206 TS-215 TS-216 TS-222 TS-224
                TS-104 TS-208 TS-209 TS-501 TS-502 TS-503
4. ağırlık  7 : TS-306 TS-310 TS-311 TS-312 TS-504 TS-505 TS-506 TS-508 TS-510
5. ağırlık ≤6 : geri kalanı
```

---

## 5 · ADIM 5–7 · TEST KARTLARI (kanıt + çürütme)

Ortak ortam tanımı — **her koşuda rapora yazılır, yazılmayan ölçüm kanıt değildir**:

```
ORTAM = Unity 2022.3.22f1 (887be4894c44) · URP 14.0.10 · macOS Darwin 25.6.0 · Apple Silicon
        Test Framework 1.1.33 · Input System 1.7.0 · AI Navigation 1.1.5
        Çözünürlük 1920×1080 (referans; `PrototypeWindowFit` masaüstüne sığdırır) · Editör Play Mode + macOS doğrulama build'i (yalnız doğrulama, platform kararı değil — WP-000) · VSync KAPALI
        Koşu kimliği: <tarih>-<saat>-<kat>
```

**Bağımsızlık düzeyleri**
`D1` hücre içi (A2 aynı bağlamda koşar) · `D2` görev dışı (GT ortamı kendi kurar, hücrenin kanıtını kullanmaz) · `D3` sıfırdan yeniden üretim (ana projeye dokunmadan repo dışı doğrulama kopyasında Library'siz import; kayıt dosyası silinmez, testler izole profil yolu kullanır — WP-000)

---

### 5.1 · T1 SÖZLEŞME — sahip `A2` · bağımsızlık `D1` · EditMode

| ID | Ad | Ne ölçülür | Beklenen | Kanıt | Ağ. | **Çürütme koşulu** (bu gözlenirse test yalanlanmıştır) |
|---|---|---|---|---|---|---|
| TS-101 | `PrototypeConfig` alan sözleşmesi | Talepteki her PROTOTYPE-ONLY sayı config'te bir alana karşılık geliyor; her alan `[PrototypeOnly]` taşıyor ve `PendingDecision` anahtarı dolu | Eksik alan 0 · etiketsiz alan 0 | EditMode XML + alan sayım logu `[DD-TEST] config.fields=<n> untagged=0` | 6 | Runtime kaynağında config dışında sabit kodlanmış bir sayı bulunursa (grep `= 34f`, `= 0.55f`, `= 11f` vb. `_Prototype/` altında, `Config/` hariç) sözleşme iddiası düşer |
| TS-102 | Kayıt şema sözleşmesi | Profil JSON'unda 10 kalıcı alan (Highest Round, Profile Level, Total/Standard/Special/Elite/Ultra Kills, Achievements, Unlocked Weapons, Unlocked Branches) + şema sürüm alanı | Tam eşleşme, fazlalık yok | Kayıt dosyasının kopyası `kosu/kayit-ornegi.json` | 6 | Koşu verisi (gold, build, current round, run kills) **kalıcı** dosyada da yazılıysa şema ayrımı iddiası düşer |
| TS-103 | Sahne ve akış sözleşmesi | Build Settings sıra 0 = Boot/MainMenu; akıştaki her ekran sahne veya durum olarak erişilebilir; sahne adları sabitlerle eşleşiyor | 0 eksik sahne | EditMode XML + `[DD-TEST] scenes=<liste>` | 6 | Bir ekrana yalnız Editör'den elle sahne açarak ulaşılabiliyorsa (akış içinden ulaşılamıyorsa) iddia düşer |
| TS-104 | Girdi eylem sözleşmesi | `Move · Aim · Fire · Reload · Pause · DebugPanel` eylemleri **hem** `Keyboard&Mouse` **hem** `Gamepad` şemasında bağlı | 12/12 bağlama var | InputActions asset dökümü + EditMode XML | 8 | Gamepad şemasında bir eylem bağlıysa ama runtime kodu `Keyboard.current`/`Mouse.current` doğrudan okuyorsa soyutlama iddiası düşer — grep `Keyboard.current\|Mouse.current\|Gamepad.current` `Input/` dışında bulunmamalı |
| TS-105 | Kopuk referans sözleşmesi (statik) | Sahne/prefab/asset YAML'inde çözülmeyen GUID ve `m_Script: {fileID: 0}` | 0 bulgu | `kosu/kopuk-referans.txt` (boş) | 10 | Tarama boş ama PlayMode logunda `MissingReferenceException` görülürse tarama yöntemi yalanlanmıştır — yöntem genişletilir, test "geçti" sayılmaz |
| TS-106 | İçerik tablosu sözleşmesi | 3 klasik silah · 4 düşman versiyonu · 4 upgrade dalı · 6 utility kartı · 3 skin tanımlı ve alanları dolu | 20 kayıt, boş alan 0 | EditMode XML | 6 | Bir tablo satırı var ama runtime hiç okumuyorsa (referans sayısı 0) "tanımlı" iddiası düşer |
| TS-107 | HUD alan sözleşmesi | Health · Round · Enemies Remaining (havuz + yaşayan) · Gold · Weapon · Ammo/Reload · SW marker · SW ground timer · SW active timer · final-seconds warning | 10/10 bağlı bileşen | HUD prefab dökümü + ekran görüntüsü | 5 | Bir alan sahnede var ama hiçbir koşulda görünür olmuyorsa (opacity 0 / kapalı kalıyorsa) iddia düşer |
| TS-108 | Run Results alan sözleşmesi | Reached Round · Total/Standard/Special/Elite/Ultra Kills · Completed Round XP · Kill XP · Total XP · Level Progress · Newly Unlocked · Achievements Earned | 12/12 bağlı | Ekran görüntüsü + EditMode XML | 6 | Ekranda **kaydırma** gerekiyorsa "tek ekran" iddiası düşer (K19) |
| TS-109 | Çevrimdışılık sözleşmesi | Kaynakta ağ API kullanımı; manifest'te ağ paketi | 0 eşleşme | `kosu/cevrimdisi-tarama.txt` (boş) | 5 | Tarama temiz ama TS-503'te (uçak modu) davranış bozulursa statik tarama yöntemi yalanlanır |
| TS-110 | Kapı log sözleşmesi `PROTOTYPE-ONLY` | Otomatik kapının okuyacağı yapılandırılmış log satırları (`[DD-TEST] anahtar=değer`) tanımlı ve üretiliyor | Tanımlı anahtarların hepsi bir kez üretiliyor | `kosu/03-playmode.log` | 4 | Bir anahtar log'a yazılıyor ama değeri runtime durumundan değil sabitten geliyorsa (grep) kanıt değeri düşer |

> **`PROTOTYPE-ONLY` gerekçesi (TS-110):** 27 madde ve `niyet §6` böyle bir ölçüt içermez. Ancak S5'in "otomatik kapı" tanımı makine tarafından okunabilir bir kanıt yüzeyi gerektirir; bu yüzey olmadan "konsol hatası sıfır" dışında hiçbir madde otomatik doğrulanamaz. Yalnız editör log'unda yaşar, oyun içi hiçbir yüzeye çıkmaz, oyun metni değildir.

---

### 5.2 · T2 DAVRANIŞ — sahip `A2` · bağımsızlık `D1` · EditMode (saf) + PlayMode (entegre)

| ID | Ad | Ne ölçülür | Beklenen | Kanıt | Ağ. | **Çürütme koşulu** |
|---|---|---|---|---|---|---|
| TS-201 | Havuz formülü | `12 + 6(R−1) + floor(2.5(R−1)^1.15)`, R=1..15 | R1=12 · R2=20 · R3=28 · R5=45 · R8=72 · R10=88 (beklenen tablo koda değil **teste** gömülü) | EditMode XML | 8 | Beklenen tablo config'ten türetiliyorsa test kendi kendini doğruluyor demektir — iddia düşer. Tablo sabit yazılmalı |
| TS-202 | Hedef aktif sayı | `min(7 + 2R, 32)` | R1=9 · R5=17 · R12=31 · R13=32 · R20=32 | EditMode XML | 8 | R13+ değeri 32'de sabitlenmiyorsa iddia düşer |
| TS-203 | Güvenlik tavanı | F1 panelinden zorlamalı spawn ile bile aktif düşman sayısı | **hiçbir karede > 40 değil** | PlayMode sayacı + `[DD-TEST] enemies.activeMax=<n>` | **10** | Zorlama sırasında spawn kuyruğu **bekletiliyor** ama tavan aşılmıyor gibi görünüyorsa: kuyruk boşaldığı anda 40 aşılıp aşılmadığı ölçülmeli. Tek bir karede 41 görülürse iddia düşer |
| TS-204 | HP/hasar çarpanı | `1 + 0.12(R−1)` ve `1 + 0.08(R−1)` | R1=1.00/1.00 · R8=1.84/1.56 | EditMode XML | 6 | Çarpan spawn anında değil hasar anında uygulanıyorsa (round değişince yaşayan düşmanın canı değişiyorsa) iddia düşer |
| TS-205 | Doğrudan vuruş bölge çözümü | Zırhlı bölgeye vuruş → yalnız zırha; **artan hasar cana geçmez**; kırıldıktan sonra can hasarı; açık kafa ×2, bacak ×0.8 | Helmet 22 / Body 38 / Leg 28 dayanıklılığıyla senaryo tablosu | EditMode XML + `kosu/hasar-senaryolari.md` | 8 | Zırhı **tam** kıran tek vuruşta düşman canı 1 puan bile düşerse iddia düşer. Ayrıca kırılma vuruşunda `overflow` değişkeni log'a düşürülüp sıfırdan farklı olmalı — sıfırsa senaryo zırhı hiç aşmamış, test **boş geçmiş** demektir |
| TS-206 | Patlama üç-bölge modeli | 25/45/30 pay dağılımı; tam zırhta can hasarı 0; kısmi zırhta yalnız açık payların toplamı cana; cana giden payda kritik çarpan yok; parça sayısıyla toplam hasar çoğalmıyor | 5 senaryo tablosu (0/1/2/3 zırh + kırık zırh) | EditMode XML | 8 | 100 hasarlık bir patlamada üç zırhın aldığı hasar toplamı 100'ü **aşarsa** (çoğalma) iddia düşer. Ayrıca tam-zırhlı düşmanda can hasarı > 0 ise iddia düşer |
| TS-207 | XP hesabı | kill 1 · special +1 · elite +3 · ultra +8 · round `15 + 5R`; tamamlanmamış round XP vermez; ölüm roundundaki kill'ler sayılır; gold XP vermez | Sentetik koşu: R1-R3 tam + R4 yarım → beklenen toplam | EditMode XML | 6 | R4 yarım kaldığı halde `15+5×4=35` XP tabloda görünüyorsa iddia düşer |
| TS-208 | Seviye eşiği | `100 + 75(L−1)` kümülatif | L2=100 · L3=275 · **L4=525** · L5=850 | EditMode XML | **8** | `niyet §9a` iddiası bu testte doğrulanır. L4 kümülatifi 525'ten farklı çıkarsa **§9a'daki sınırlama tanımı yanlıştır** ve niyet kaydına geri döner |
| TS-209 | Unlock kuralı | SMG=L2 · Shotgun=L3 · Explosion=L2 · Chain=L3; kalıcı ilerleme başlangıç hasarı/canı/gold'u **vermiyor** | Eşleşme + 0 savaş avantajı | EditMode XML | **8** | Level 5 profil ile başlatılan koşuda oyuncunun başlangıç HP'si, hasarı veya gold'u Level 1 koşusundan farklıysa iddia düşer |
| TS-210 | Can yenilenmesi | 4.5 sn hasarsız → 12 HP/s, tam cana kadar; yeni hasar bekleme süresini sıfırlar | Zaman-değer eğrisi | PlayMode logu `[DD-TEST] hp.regen` | 6 | 4.49 sn'de yenilenme başlıyorsa veya hasar anında sayaç sıfırlanmıyorsa iddia düşer |
| TS-211 | Ölüm ve koşu sonu | HP 0 → koşu biter · revive yok · koşu verisi sıfır · kalıcı veri korunur | Durum anlık görüntüsü öncesi/sonrası | PlayMode XML | 6 | Ölümden sonra kalıcı profilde `Highest Round` **düşerse** veya koşu verisi ana menüde hâlâ okunabiliyorsa iddia düşer |
| TS-212 | Gold ekonomisi | Ödül 2/5/10/25 · ölümde sıfır · kart maliyeti 15–45 arası başlar ve tier ile artar · harcanmayan gold sonraki rounda taşınır · **reroll yok** | Ekonomi tablosu | EditMode + PlayMode XML | 6 | Round sonu ekranında üçüncü bir seçenek (reroll benzeri) varsa veya taşınan gold round geçişinde sıfırlanıyorsa iddia düşer |
| TS-213 | Special Weapon zamanlaması | R≤3 spawn yok · ihtimal %35 başlar, **%65'i aşmaz** · spawn zamanı round ilerlemesinin %30–70 aralığında · aynı anda tek örnek · R8+ %10 ile ikinci **sıralı** | Tohumlanmış 200 sanal round | EditMode XML + dağılım tablosu | 6 | İki Special Weapon aynı anda yerde/aktif görülürse iddia düşer. Ayrıca %30'dan önce veya %70'ten sonra tek bir spawn görülürse pencere iddiası düşer |
| TS-214 | Special Weapon durum makinesi | Yerde 22 sn → kaybolur · alındığında 11 sn aktif · klasik silah devre dışı → süre sonunda **otomatik** geri · upgrade'ler special'ı etkilemiyor | Durum geçiş dizisi | PlayMode logu | 6 | Piercing/Ricochet/Explosion/Chain kartı alınmış bir koşuda Special Weapon mermisi bu etkiyi gösteriyorsa iddia düşer (REQ-059) |
| TS-215 | Round yaşam döngüsü | Havuz tükendi **ve** son düşman öldü → round tamam → simülasyon donar → upgrade ekranı → 3 sn geri sayım → sonraki round otomatik | Tam döngü × 3 round | PlayMode XML + `[DD-TEST] round.state` dizisi | 8 | Havuz tükenmeden round tamamlanıyorsa, veya upgrade ekranı açıkken düşmanlar hareket ediyorsa iddia düşer |
| TS-216 | Pause dondurma | Pause'ta oyuncu konumu, düşman konumu, mermi konumu, SW zamanlayıcısı, regen sayacı, round zamanlayıcısı | 1 sn gerçek zamanda **her birinde delta = 0** | PlayMode XML + `[DD-TEST] pause.deltas` | 8 | Bir tek bileşen `Time.unscaledDeltaTime` kullanıyorsa delta > 0 çıkar ve iddia düşer. Özellikle Special Weapon süresi ve regen gecikmesi ayrı ayrı ölçülmeli — tek "timeScale=0" kanıtı yeterli değildir |
| TS-217 | Spawn kısıtları | Yalnız tanımlı 12 nokta · mesafe ≥ 12 m · tercih 18–35 m · aynı nokta cooldown ~4 sn · geçerli NavMesh yolu yoksa nokta elenir | 200 spawn örneği | PlayMode logu `[DD-TEST] spawn.point/dist` | 6 | 12 m'den yakın **tek** bir spawn görülürse iddia düşer. NavMesh yolu doğrulaması `CalculatePath` dönüş değeriyle değil `PathComplete` durumuyla yapılmalı — `PathPartial` kabul ediliyorsa iddia düşer |
| TS-218 | Spawn ağırlık dağılımı `PROTOTYPE-ONLY` | 200 spawn örneğinde nokta dağılımı | Tek noktanın payı **< %40** · en az 6 farklı nokta kullanılmış | Dağılım histogramı | 5 | Oyuncu hiç hareket etmeden ölçülürse dağılım yapay olarak dar çıkar; ölçüm oyuncu halka rotasında dolaşırken alınmalıdır. Sabit oyuncuyla alınmış ölçüm kanıt sayılmaz |
| TS-219 | Versiyon garantili tanıtım | R3'te ≥1 Runner · R5'te ≥1 Guard · R8'de ≥1 Executioner | 3/3 | PlayMode logu `[DD-TEST] enemy.firstSeen` | **10** | `niyet §9c` bağlantılı. **F1 sıçrama ön ayarıyla** R8'e gidilerek ölçülürse iddia yalnız "sıçrama yolu" için geçerlidir; doğal ilerlemeyle R8'e ulaşan bir koşuda Executioner görülmezse iddia düşer → **doğuştan kırılgan** |
| TS-220 | Oturum belleği | Aynı oturumda son skin/silah hazır seçili · uygulama yeniden açılınca varsayılan | 2 senaryo | PlayMode + D3 koşusu | 5 | "Yeniden açılış" Editör Play Mode durdur/başlat ile taklit edilirse statik alanlar hayatta kalabilir ve varsayılana dönüş yanlış görünebilir; **domain reload açık** olmalı, kapalıysa ölçüm kanıt sayılmaz |
| TS-221 | Kalıcılık turu | Profil verisi yaz → uygulama kapat → oku | Bit-eş kopya | Kayıt dosyası öncesi/sonrası hash | 6 | Kayıt yalnız `OnApplicationQuit` içinde yazılıyorsa Editör'de Play durdurma bunu çağırmayabilir; çökme sonrası veri kaybı sınanmadıysa iddia kısmi kalır |
| TS-222 | Bağımsız hareket/nişan | Hareket vektörü ile bakış vektörü ayrık · hasar alırken girdi kesilmiyor · **dash/dodge eylemi yok** | Sentetik girdi: kuzeye hareket + güneye ateş | PlayMode XML | 8 | Karakter ateş yönüne dönerken hareket yönü de değişiyorsa (hareket "yerel" eksende hesaplanıyorsa) iddia düşer. Ayrıca girdi haritasında dash'e karşılık gelen bir eylem bulunursa REQ-042 düşer |
| TS-223 | Temas hasarı yok | Saldırı aktif karesi dışında oyuncuya hasar uygulanmıyor · eşzamanlı iki isabet üst üste biniyor · dokunulmazlık süresi yok | 3 senaryo | PlayMode XML | 6 | Oyuncu 10 zombinin içinde 5 sn durduğunda hiçbir saldırı animasyonu tetiklenmeden HP düşerse iddia düşer |
| TS-224 | Akış kapanışı (**duman testi**) | MainMenu → Selection → Run (R1→R3) → ölüm → Run Results → MainMenu, tek PlayMode koşusunda | Kapanır · süre ≤ 6 dk · 0 hata | PlayMode XML + `[DD-TEST] flow.step` dizisi | 8 | Akış "geçti" ama adım dizisinde bir ekran atlanmışsa (örn. Run Results'a hiç girilmeden MainMenu'ye dönülmüşse) iddia düşer. Dizinin **tam sırayla** eşleşmesi aranır |
| TS-225 | Klasik silah kalıcılığı ve build kimliği | Silah koşu boyunca değişmiyor · upgrade dalları ayırt edilebilir build kimliği üretiyor | Aynı silah, 2 farklı dal → 2 farklı kimlik | PlayMode XML | 5 | İki farklı upgrade yolu aynı build kimliğini üretiyorsa REQ-017 iddiası düşer |

---

### 5.3 · T3 EŞİK — sahip **`GT`** · bağımsızlık `D2` (ortamı kendi kurar) · hücrenin ölçümü tek başına geçirmez

| ID | Ad | Ne ölçülür | Eşik | Kanıt | Ağ. | **Çürütme koşulu** |
|---|---|---|---|---|---|---|
| TS-301 | Kare süresi · doğal koşu | 1080p, Round 5, 90 sn savaş | `fps.p95 ≥ 60` | Profiler örneği `kosu/profil-r5.data` + fps histogramı | **9** | Ölçüm Editör Play Mode'da alınır; Editör ek yük taşır. **Editörde p95 ≥ 60 ise iddia güçlüdür; editörde < 60 ise build alınmadan "başarısız" denemez.** Ayrıca ölçüm sırasında F1 paneli açıksa (kendi çizim yükü var) ölçüm geçersizdir |
| TS-302 | Kare süresi · **40 düşman zorlamalı** | F1 panelinden 40 düşmana zorlanmış yük, 60 sn | `fps.p95 ≥ 60` hedeflenir; ölçülen **kaydedilir** | Profiler örneği + `[DD-TEST] enemies.active=40` | **10** | `niyet §9b` — **doğuştan kırılgan.** `min(7+2R,32)` tavanı 32 iken 40 yükü **doğal oyunda hiç oluşmuyor.** Bu ölçüm yalnız panel zorlamasıyla alınır ve "prototip 40 düşmanı kaldırıyor" iddiası **yalnız zorlamalı yol için** geçerlidir. Doğal koşuda 40 aktif düşman gözlenirse formül uygulaması yanlıştır ve TS-202 düşer |
| TS-303 | Aktif düşman tavanı ölçümü | 10 dk koşuda maksimum eşzamanlı aktif düşman; R≥13'te değer | `≤ 40` her zaman · R≥13'te `= 32` | Sayaç zaman serisi | **9** | Ölçüm örnekleme aralığı ≥ 1 sn ise tek karelik aşımlar kaçar. Örnekleme **her karede** olmalı; saniyelik örneklemeyle alınmış ölçüm bu testi geçiremez |
| TS-304 | GC allocation | Savaş sırasında kare başına yönetilen bellek ayırma | `≈ 0 B/kare` (kabul: ≤ 1 KB/kare) | Profiler `GC.Alloc` sütunu ekran görüntüsü | 7 | Ölçüm Deep Profile açıkken alınırsa sahte ayırma görünür; Deep Profile **kapalı** olmalı. Havuzlama (`pooling`) iddiası: düşman ölüp yenisi gelirken `Instantiate` çağrısı görülürse iddia düşer |
| TS-305 | Açılış süresi `PROTOTYPE-ONLY` | Play tuşu → Main Menu'de ilk etkileşim kabul edilen an | `≤ 10 sn` (editör, ilk derleme sonrası) | Stopwatch + log damgası | 4 | İlk Play'de shader derlemesi olur; ölçüm **ikinci** Play'de alınır. İlk Play ölçümü kanıt sayılmaz |
| TS-306 | Spawn kesintisizliği | Havuzda düşman varken aktif sayının hedefin altında kaldığı süre | `≤ 0.45 sn × 1.5 = 0.68 sn` | Zaman serisi | 7 | Oyuncu bir noktada duruyorsa spawn noktaları cooldown'a girip yapay boşluk yaratabilir; ölçüm hareket halinde alınır |
| TS-309 | Konsol temizliği ölçümü | 15 dk koşuda Error + Exception + Assert sayısı | `= 0` | Log grep sayımı | **10** | `Debug.LogWarning` ile bastırılmış hatalar sayıma girmez. Warning sayısı da kaydedilir; warning sayısı bir önceki koşuya göre **arttıysa** temizlik iddiası zayıflar |
| TS-310 | Başlangıç konumu | Oyuncunun R1 spawn noktasının merkez avlu hacmine uzaklığı | Avlu sınırları **içinde** (mesafe = 0) | Konum logu + tepeden ekran görüntüsü | 7 | Avlu sınırı testte elle tanımlanan bir kutuysa, kutunun haritayla uyumu ayrıca doğrulanmalı; kutu haritadan türetiliyorsa test kendi kendini doğrular ve iddia düşer |
| TS-311 | Special Weapon ihtimal dağılımı | 200 sanal round üzerinde spawn oranı | R4 ≈ %35 (±%8) · üst sınır **hiçbir roundda > %65** | Dağılım tablosu | 7 | Tohum sabitlenmişse dağılım tek bir diziyi yansıtır; **en az 5 farklı tohumla** koşulmalı. Tek tohumla alınan dağılım kanıt sayılmaz |
| TS-312 | Zamanlayıcı sapması | SW aktif 11 sn · SW yerde 22 sn · geri sayım 3 sn · regen gecikmesi 4.5 sn | Sapma `≤ %2` | Zaman damgası logu | 7 | Ölçüm 60 fps'te alınırsa sapma gizlenir. **Kare hızının 30'un altına düştüğü** yükte de ölçülmeli; yalnız 60 fps ölçümü kısmi kanıttır |
| TS-313 | Duraklama sızıntısı | Pause'ta 60 sn bekleyip her sayacın deltası | `= 0.000` (kayan nokta toleransı yok) | Delta tablosu | **9** | Yalnız `Time.timeScale` okunarak yapılan doğrulama kanıt değildir; **her** sayaç ayrı ayrı okunmalı. Bir sayaç `Time.realtimeSinceStartup` kullanıyorsa delta > 0 çıkar ve K26 düşer |

---

### 5.4 · T4 DENEYİM — sahip **kullanıcı** · otomatikleştirilemez · ayrıntı `MANUEL-TEST-KITI.md`

| ID | Ad | Ne sorulur | Kanıt | Ağ. | **Çürütme koşulu** |
|---|---|---|---|---|---|
| TS-401 | HI1 · Kontrol tepkiselliği | Tuşa bastığında karakter "hemen" gidiyor mu? Nişan fareyi takip ediyor mu, geriden mi geliyor? | Kullanıcı notu + 1-5 puan + ekran kaydı (varsa) | **9** | Kullanıcı yalnız boş alanda dener ve "iyi" derse iddia eksiktir: **20+ düşman baskısı altında** tekrar sorulmalı. Kalabalıkta puan düşerse tekil ölçüm yalanlanır |
| TS-402 | HI2 · Vuruş ve isabet hissi | Mermi hedefe değdiği **anlaşılıyor** mu? Zırh vuruşu ile et vuruşu ayrılıyor mu? Zırh kırılması olay gibi hissettiriyor mu? | Kullanıcı notu + 1-5 puan | **9** | Yalnız Assault Rifle ile denenirse iddia kısmi: Shotgun ve SMG ile de sorulmalı. Üç silahtan biri "hissiz" alırsa iddia düşer |
| TS-403 | HI3 · Sürü baskısı | Çevrelenme gerçek tehdit mi? Kaçış rotası bulmak zorunda kalıyor musun, yoksa kolayca sıyrılıyor musun? | Kullanıcı notu + 1-5 puan | **9** | Yalnız R1-R3'te sorulursa iddia geçersiz — baskı R5+ ve zorlamalı 40 yükünde sorulmalı |
| TS-404 | HI4 · Kamera ve okunabilirlik | Nereye ateş ettiğini görüyor musun? Kamera kayması nişanı bozuyor mu? Saydamlaşma çalışıyor mu? Gold ve SW işareti çevreden ayrılıyor mu? | Kullanıcı notu + 1-5 puan + ekran görüntüsü | **9** | Bambu koridoru ve köprü denenmeden verilen puan kısmi — kamera engeli olan iki bölgede ayrıca sorulur |
| TS-405 | HI5 · Atmosfer | "Güzel ama tehditkâr" oluşuyor mu? Sis/yaprak/partikül düşmanı veya nişanı kapatıyor mu? | Kullanıcı notu + 1-5 puan + ekran görüntüsü | 7 | Yalnız savaş dışında verilen puan kısmi — savaş ortasında da sorulmalı; savaşta "kapatıyor" cevabı gelirse HI4 de düşer |
| TS-406 | K08/K09 · Hasar modelinin gözlemlenebilirliği | Zırha vurduğunu **ekrandan** anlıyor musun? Zırh kırıldığı an belli mi? "Artan hasarın boşa gittiği" hissediliyor mu? | Kullanıcı notu | 7 | Bilgi yalnız hasar sayısı (damage number) ile veriliyorsa "gözlemlenebilir" iddiası zayıftır; sayı kapatıldığında da anlaşılmalı |
| TS-407 | K19 · Run Results okunabilirliği | 12 alan tek ekranda mı, kaydırma gerekiyor mu? Yeni açılanlar göze çarpıyor mu? Claim gerekiyor mu? | Ekran görüntüsü | 6 | Alanlar sığıyor ama okunacak kadar büyük değilse (kullanıcı yaklaşmak zorunda kalıyorsa) iddia düşer |
| TS-408 | UI sunum kalitesi (`niyet §5` öncelik 7) | Menüler "debug butonu yığını" gibi mi duruyor, yoksa tutarlı tipografi/boşluk/odak hiyerarşisi var mı? | Ekran görüntüsü seti | 6 | Yalnız Main Menu'ye bakılarak verilen puan kısmi — Selection, Upgrade ve Results ekranları da görülmeli |
| TS-410 | K27 · Kullanıcı oturumunda konsol denetimi | 15 dk oynanışın sonunda Console penceresinde kırmızı/sarı sayaç | Console ekran görüntüsü | 8 | Kullanıcı Console'u "Clear on Play" açıkken izlerse önceki hatalar silinir; ayar **kapalı** olmalı |
| TS-409 | K05b · Gamepad deneyimi | — | — | — | **⊘ G-01 · KOŞULAMAZ.** Donanım yok. Rapora `koşulamadı (donanım)` yazılır, `geçti` veya `kaldı` **yazılmaz** |

---

### 5.5 · T5 DAYANIKLILIK — sahip **`GT`** · bağımsızlık `D2`, TS-511 `D3`

| ID | Ad | Ne zorlanır | Beklenen | Kanıt | Ağ. | **Çürütme koşulu** |
|---|---|---|---|---|---|---|
| TS-501 | Soğuk açılış | Kayıt dosyası **yok** | Varsayılan profil, çökme yok, akış açılır | Log + ekran görüntüsü | 8 | Varsayılan profil kod içinde değil bir asset'ten geliyorsa asset eksikliği de sınanmalı; yalnız "dosya yok" senaryosu kısmi |
| TS-502 | Bozuk kayıt | JSON ortadan kırpılmış · alan tipi yanlış · sürüm alanı eski | Çökme yok · varsayılana düşer · **Error değil Warning** | Log | 8 | Bozuk kayıt sessizce silinip yerine yenisi yazılıyorsa kullanıcı verisi kaybı gizlenmiş olur; davranış log'a yazılmıyorsa iddia düşer |
| TS-503 | Çevrimdışı | Wi-Fi kapalı + uçak modu | Tam akış çalışır, gecikme yok | Ekran görüntüsü + log | 8 | Makine ağa kapalı ama **yerel** bir servis (localhost) kullanılıyorsa uçak modu bunu yakalamaz; TS-109 statik taraması bu boşluğu kapatır — ikisi birlikte kanıttır |
| TS-504 | Eşzamanlı tepe yük | 40 düşman + Special Weapon aktif + patlama + zırh kırılmaları aynı anda | Efekt/ses tavanı devrede · çökme yok · okunabilirlik korunuyor | Profiler + ekran görüntüsü | 7 | Efekt tavanı "en yeni efekti at" mantığıyla çalışıyorsa **oyuncunun kendi vuruşunun** geri bildirimi kaybolabilir; tavan öncelikli olmalı — kendi vuruş efekti düşerse HI2 de düşer |
| TS-505 | Uzun oturum | 15 dk kesintisiz oynanış | Bellek artışı sınırlı · fps düşmüyor · konsol temiz | Bellek zaman serisi | 7 | **Kırılgan:** 15 dk talebin oynanış süresidir, sızıntı eşiği değil. 15 dk'da görünmeyen bir sızıntı 60 dk'da görünebilir; bu iddia yalnız 15 dk için geçerlidir (`G-08` niteliğinde sınır) |
| TS-506 | Pause sınır durumları | Round geçişinde · geri sayım sırasında · SW aktifken · upgrade ekranı açıkken Pause | Durum bozulmuyor, çıkışta kontrol geri geliyor | PlayMode logu | 7 | "Pause içinde Pause" (upgrade ekranı + Escape) denenmezse kapsam eksik; iki katmanlı durum sınanmalı |
| TS-507 | Yetersiz gold | 0 gold ile upgrade ekranı | Satın alınamaz · geç çalışır · kilitlenme yok | Ekran görüntüsü | 5 | "Geç" düğmesi yoksa ve kartlar alınamıyorsa oyuncu kilitlenir; bu durum **çökme değil** ama K15 düşer |
| TS-508 | Ölüm yarış durumları | Round bitişiyle aynı karede ölüm · SW aktifken ölüm · upgrade ekranı açılırken ölüm | Run Results tutarlı, XP çift sayılmıyor | Sonuç ekranı görüntüsü + XP dökümü | 7 | Ölüm anında round tamamlanmışsa round XP'si verilmeli mi? **Kural:** talep "tamamlanmamış round XP vermez" der; tamamlanmış round + aynı karede ölüm durumunda XP **verilmeli**. Verilmiyorsa iddia düşer |
| TS-509 | Koşudan çıkış | Koşu ortasında Main Menu | Doğrulama diyaloğu · koşu düzgün sonlanır · veriler tutarlı | Ekran görüntüsü | 6 | Çıkışta koşu "iptal" sayılıp XP hiç verilmiyorsa, tamamlanmış roundların XP'si kaybolur; bu K20 ile çelişir |
| TS-510 | Dar geçitte sürü | Köprüde 25+ düşman | Trafik kilidi yok · oyuncu sıkışıp kalmıyor · yumuşak ayrışma çalışıyor | Ekran kaydı + konum logu | 7 | Zombiler birbirini itiyor ama **oyuncuyu** duvara sıkıştırıp hareketsiz bırakıyorsa REQ-046 ile REQ-047 çelişir; "çevrelenmek gerçek tehdit" ile "sıkışıp kalmak" ayrılmalı — oyuncu 3 sn'den uzun hiç hareket edemiyorsa iddia düşer |
| TS-511 | **Sıfırdan yeniden üretim** `D3` | Repo dışı doğrulama kopyası Library'siz kurulur · profil `SetPathOverride` ile izole (TestProfileIsolation) · otomatik kapı kopyada baştan koşar | Kapı aynı sonucu verir | Tam kapı çıktı seti | **9** | "Benim makinemde çalışıyor"un çürütmesi. Yeniden içe aktarma sonrası **tek bir** test farklı sonuç verirse, o testin tüm önceki kanıtı kırılgan işaretlenir |
| TS-512 | Aynı karede çoklu isabet | Shotgun 8 pellet aynı karede + patlama aynı karede + zırh tam | Toplam hasar çoğalmıyor · zırh kırılma sırası tutarlı · artan hasar cana geçmiyor | EditMode + PlayMode XML | 7 | Pelletler ayrı ayrı işleniyorsa her biri zırhı ayrı azaltır — bu **doğru**dur. Ancak patlama payı pellet başına yeniden hesaplanıyorsa çoğalma olur ve TS-206 düşer |

---

## 6 · NİYET §9 SINIRLAMALARININ ÖLÇÜM KARŞILIĞI

Bunlar **hata değildir**; ölçülüp doğrulanması gereken bulgulardır. Her biri bir teste bağlanmıştır.

| `niyet §9` | Bulgu olarak iddia | Doğrulayan test | Doğrulanırsa | Yalanlanırsa |
|---|---|---|---|---|
| (a) Unlock merdiveni tek koşuda tükeniyor | Level 4 kümülatif = **525 XP**; R8'e ulaşan koşu **545–600 XP** üretir | **TS-208** (525 doğrulaması) + **TS-207** (koşu XP toplamı) | Rapora "ölçüldü, düzeltilmedi" yazılır; `G-05` boşluğu gerekçelenir | §9a'daki sayı yanlıştır → **niyet kaydına geri döner**, plan revize edilir |
| (b) 40 yük doğal oyunda oluşmuyor | `min(7+2R,32)` tavanı 32; 40 yalnız F1 panelinden zorlanarak ölçülür | **TS-202** (32 tavanı) + **TS-302** (zorlamalı 40) + **TS-303** (gözlenen maksimum) | Performans iddiası **"yalnız zorlamalı yol"** kaydıyla raporlanır; `kırılgan` işaretlenir | Doğal koşuda 40 aktif gözlenirse formül uygulaması hatalıdır → TS-202 düşer, K07 kaldı |
| (c) Ultra R8'de, 10-15 dk'da görülmeyebilir | R1-R7 havuzu ≈271 düşman; Executioner R8'de | **TS-201** (havuz toplamı) + **TS-219** (garantili tanıtım) | Manuel kit R8'e **F1 sıçramasıyla** gider; TS-219 "sıçrama yolu ile doğrulandı" kaydıyla kapanır — doğal yol `doğrulanmadı` kalır | Doğal 15 dk'lık koşuda R8'e ulaşılıyorsa §9c fazla temkinlidir; manuel kit sadeleşir |
| (d) Gamepad doğrulanamaz | Donanım yok | **TS-104** (sözleşme var) + **G-01** (davranış boşluk) | K05 raporda **ikiye bölünür**: `K05a klavye+fare · geçti` / `K05b gamepad · koşulamadı (donanım)` | — |
| (e) DEC-021'in elle-tanımlı yarısı sınanmayacak | Yalnız formül tarafı uygulanıyor | **G-02** kapsam boşluğu | K07 "formül tarafı kapandı, hibrit tarafı kapsam dışı" olarak raporlanır | Elle-tanımlı katman prototipe girerse TS-226 açılır |

---

## 7 · KABUL ÖLÇÜTLERİNİN KAPI KARŞILIĞI (`10 §7`)

| # | Ölçüt | Bu projede karşılığı | Eşik | Not |
|---|---|---|---|---|
| O1 | Değişen kod kapsamı | Saf çekirdek (formül, hasar, XP, ekonomi) satır kapsamı | ≥ %80 | UI/görsel katman kapsam hedefi **yok** — gerekçe: T4 insana bırakıldı |
| O2 | Mutasyon skoru | Unity/C# için çevrimdışı mutasyon aracı **yok** → yerine **elle sabotaj seti** (`OTOMATIK-KAPI.md §6`) | 5/5 sabotaj yakalanır | Bu bir ikame; skor değil **yakalama sayımı** raporlanır |
| O3 | Kırılgan test oranı | Duman testi tekrar koşusu | 10 koşuda ≤ %2 → **bütçe gereği 3 koşu** | ⚠ zayıflatılmış ölçüm; `G-09` niteliğinde. 3 koşuda 1 sapma bile karantina açar |
| O4 | Hızlı kapı süresi | Derleme + EditMode + statik tarama | ≤ **8 dk** | Tam kapı (PlayMode dahil) ≤ 25 dk |
| O5 | Sözleşme testi kapsaması | Servis sınırı yok (tek işlem, çevrimdışı) → yerine **asmdef sınırları** | Core↔Runtime↔Tests sınırlarının 3/3'ü | `gereksiz` değil, uyarlanmış |
| O6 | Test izolasyonu | EditMode testleri rastgele sırada | 0 hata | `[SetUp]/[TearDown]` ile durum kurulur; statik durum sızıntısı yasak |
| O7 | Yalancı yeşil | Kasıtlı kırma denemesi | %100 yakalama | `OTOMATIK-KAPI.md §6` |

---

## 8 · ADIM 8 · MANUEL KİT

10 adım, ~15 dk, kullanıcı koşar. Ayrıntı: **`MANUEL-TEST-KITI.md`** (aynı klasör).
`kaldı` işaretlenen her adım otomatik `BULGU`ya çevrilir.
Öncelik 1-5 puanları **`doğrulanmadı` → `doğrulandı` / `kaldı`** geçişini yalnız kullanıcı yapar.

---

## 9 · PLAN ÖZETİ (`G2` kapısına giden satır)

```
tp-G-deaths-demolition-10-1 · teslimat_tipi: oynanis
ölçüt        : 34 (27 kabul kriteri → 28 satır, K05 bölündü · + 5 his imzası · + O34)
test         : 68  (T1:10 · T2:25 · T3:11 · T4:9 + 1 koşulamaz · T5:12)
sahiplik     : A2 → 35  ·  GT → 23  ·  kullanıcı → 9  ·  koşulamaz → 1
kapsam_boşluğu: 9   (G-01 ×4 · G-02 ×1 kısmi · G-05 ×1 · G-07 ×1 · G-10 ×2)
kırılgan (doğuştan): TS-219 (sıçrama yolu) · TS-302 (zorlamalı yük) · TS-505 (15 dk sınırı)
                     + O3 ölçümü 10 yerine 3 koşuyla alınıyor
tur-kıran ölçüt: K27 · K01+K04+K22 · K05a · K07 · K26 · K14+K15+K16
kit_adım     : 10 (+4 koşulamaz gamepad adımı)
manuel süre  : ~15 dk  ·  hızlı kapı ≤8 dk  ·  tam kapı ≤25 dk
```

**Bu plan `G2` kapısında kullanıcıya görünür.** `F4` icrası başlamadan önce göreve atanan GT bu planı denetler: her `var` hücresinin bağımsız yeniden üretilebilir olup olmadığına bakar.
