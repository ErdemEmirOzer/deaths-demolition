# OTOMATİK KAPI · `G-deaths-demolition-10`

**Ne bu:** CI yok. Tek makinede, kopyala-çalıştır düzeyinde bir kapı.
**S5 tanımı — kapı tam olarak şunu doğrular, fazlasını değil:**

```
1  derleme temiz
2  PlayMode duman testi geçer (akış kapanır)
3  konsol hatası = 0  ·  kopuk referans = 0
4  akış kapanışı: MainMenu → Selection → Run → Results → MainMenu
```

**Kapının göremediği:** öncelik 1-5'in his imzası. O `MANUEL-TEST-KITI.md`'dedir ve kullanıcınındır.
Kapı yeşilken bile HI1-HI5 raporda `doğrulanmadı` kalır.

**Süre bütçesi:** hızlı kapı (K1-K2-K5-K6-K7) **≤ 8 dk** · tam kapı (K3 dahil) **≤ 25 dk**.

---

## 0 · ORTAK DEĞİŞKENLER

Her oturumda bir kez çalıştır. **Yolda boşluk var** (`Deaths Demolition`) — tırnakları silme.

```bash
export UNITY="/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity"
# WP-000 (2026-09-10): kapı ANA PROJE YOLUNDA DEĞİL, repo dışı Library'siz doğrulama kopyasında koşar.
# Kopya: Assets + Packages + ProjectSettings rsync ile; kanıt repo dışına yazılır.
export PROJ="/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-000/verify/DeathsDemolition_PlayableConcept"
export STAMP="$(date +%Y%m%d-%H%M%S)"
export OUT="/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-000/verify/$STAMP"
mkdir -p "$OUT"
echo "OUT=$OUT"
```

Kanıt **repo dışına** yazılır — bilerek. `Assets/` içinde olsaydı Unity her koşuda log dosyalarını içe aktarırdı;
repo içinde olsaydı başlangıç manifestini bozardı. Savunma olarak kök `.gitignore` Unity üretim klasörlerini ve `TestOutput/` benzeri çıktıları zaten dışlar.

**Ortam damgası — her koşuda ilk iş, yazılmayan ölçüm kanıt değildir:**

```bash
{
  echo "koşu      : $STAMP"
  echo "unity     : $(cat "$PROJ/ProjectSettings/ProjectVersion.txt" | head -1)"
  echo "makine    : $(uname -a)"
  echo "cpu       : $(sysctl -n machdep.cpu.brand_string)"
  echo "bellek    : $(( $(sysctl -n hw.memsize) / 1073741824 )) GB"
  echo "paketler  :"; sed -n 's/^ *"com.unity/  com.unity/p' "$PROJ/Packages/manifest.json"
} > "$OUT/00-ortam.txt"
cat "$OUT/00-ortam.txt"
```

---

## K0 · ÖNKOŞUL · Test Framework manifest'te açıkça var (kapalı)

Tarihsel durum (2026-08-17): `com.unity.test-framework` yalnız dolaylı bağımlılıktı. Kapatıldı: `Packages/manifest.json`
artık `"com.unity.test-framework": "1.1.33"` satırını içeriyor; WP-000 Aşama 2 (2026-09-10) 48 paketin tamamının
ağ olmadan çözüldüğünü kanıtladı (`Registered 48 packages`).

**Doğrulama (bu satır boş dönerse kapı başlatılmaz):**

```bash
grep -n "com.unity.test-framework" "$PROJ/Packages/manifest.json" \
  || echo "K0 KIRMIZI · test-framework manifest'te yok — kapı koşulamaz"
```

---

## K1 · DERLEME KAPISI · ~3 dk

```bash
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PROJ" \
  -logFile "$OUT/01-derleme.log"
echo "exit=$?"
```

**Denetim — çıktı boş olmalı:**

```bash
grep -nE "error CS[0-9]{4}|Compilation failed|Scripts have compiler errors|Assembly .* will not be loaded" \
  "$OUT/01-derleme.log" | tee "$OUT/01-derleme-bulgu.txt"
[ -s "$OUT/01-derleme-bulgu.txt" ] && echo "K1 KIRMIZI" || echo "K1 YEŞİL"
```

**Neden yalnız exit koduna bakmıyoruz:** Unity `-quit` ile derleme hatasında da 0 döndürebiliyor. **Log grep'i asıl kanıttır**, exit kodu ikincildir.

---

## K2 · EDITMODE TESTLERİ · ~2 dk · T1 + T2 saf çekirdek

`-runTests` ile **`-quit` KULLANMA** — Unity testler bitince kendi kapanır; `-quit` testleri yarıda keser.

```bash
"$UNITY" -batchmode -nographics \
  -projectPath "$PROJ" \
  -runTests -testPlatform EditMode \
  -testResults "$OUT/02-editmode.xml" \
  -logFile "$OUT/02-editmode.log"
echo "exit=$?"
```

**Sonuç okuma:**

```bash
xmllint --xpath 'concat("sonuç=", /test-run/@result, " toplam=", /test-run/@total,
                        " geçti=", /test-run/@passed, " kaldı=", /test-run/@failed,
                        " atlandı=", /test-run/@skipped)' \
        "$OUT/02-editmode.xml"; echo
```

**Kaldı olanların adları:**

```bash
xmllint --xpath '//test-case[@result="Failed"]/@fullname' "$OUT/02-editmode.xml" 2>/dev/null \
  | tr ' ' '\n' | sed '/^$/d' | tee "$OUT/02-kaldi.txt"
```

**Eşik:** `failed=0` **ve** `total` = envanterdeki beklenen sayı (WP-000 Aşama 2/3: EditMode **175**,
PlayMode **21**; sapma raporda açıklanır, "≥" kabul edilmez). `total` beklenenden farklıysa
**keşif eksik/fazla demektir**, "yeşil" yanıltıcıdır:

```bash
EXPECTED=175
T=$(xmllint --xpath 'string(/test-run/@total)' "$OUT/02-editmode.xml")
F=$(xmllint --xpath 'string(/test-run/@failed)' "$OUT/02-editmode.xml")
if [ "$F" = "0" ] && [ "$T" -eq "$EXPECTED" ]; then echo "K2 YEŞİL ($T test)"; else echo "K2 KIRMIZI (total=$T beklenen=$EXPECTED failed=$F)"; fi
```

**Tek bir testi koşmak** (hata ayıklarken):

```bash
"$UNITY" -batchmode -nographics -projectPath "$PROJ" \
  -runTests -testPlatform EditMode \
  -testFilter "DD.Prototype.Tests.Rounds.EnemyPoolTests" \
  -testResults "$OUT/02-tekil.xml" -logFile "$OUT/02-tekil.log"
```

---

## K3 · PLAYMODE DUMAN TESTİ · ~10 dk · **kapının kalbi**

### 3.1 · Komut — `-nographics` YOK

```bash
"$UNITY" -batchmode \
  -projectPath "$PROJ" \
  -runTests -testPlatform PlayMode \
  -testCategory "Smoke" \
  -testResults "$OUT/03-playmode.xml" \
  -logFile "$OUT/03-playmode.log"
echo "exit=$?"
```

**`-nographics` bilerek yok.** Proje URP kullanıyor; grafiksiz batchmode'da URP hattı kurulmaz ve duman testi sahte biçimde ya çöker ya geçer. Grafik aygıtı gerekiyor.

**Batchmode PlayMode başarısız olursa** (macOS'ta bazı sürümlerde pencere oluşturamaz) → düşme yolu:
Unity Editor → `Window > General > Test Runner` → `PlayMode` sekmesi → `Run All`.
Bu durumda rapora **`ortam: editör-elle (batchmode PlayMode kurulamadı)`** yazılır. Ortamı gizlenmiş ölçüm kanıt değildir.

### 3.2 · PlayMode duman testi ne yapar (kod bu fazda yazılmaz — tarifi budur)

Tek bir `[UnityTest]`, kategori `Smoke`, azami süre 6 dk. Adımları:

| # | Ne yapar | Ne doğrular | Ürettiği log satırı |
|---|---|---|---|
| 1 | Boot sahnesini yükler, ilk etkileşilebilir kareyi bekler | K01 | `[DD-TEST] flow.step=MainMenu` |
| 2 | `Play` eylemini kod üzerinden tetikler | K02 | `[DD-TEST] flow.step=Selection` |
| 3 | Skin 0 + Assault Rifle seçer, Run başlatır | K03 | `[DD-TEST] loadout=skin0/AR` |
| 4 | Oyuncunun merkez avlu hacminde doğduğunu doğrular | K04 · TS-310 | `[DD-TEST] spawn.player=<x,y,z> inCourtyard=true` |
| 5 | Sentetik girdi: kuzeye hareket + güneye ateş, 2 sn | K05a · TS-222 | `[DD-TEST] input.moveDir / input.aimDir` |
| 6 | Round 1'i hızlandırılmış olarak bitirir (`F1` API'sinden `KillAll`) | K14 · TS-215 | `[DD-TEST] round.state=Complete round=1` |
| 7 | Her karede aktif düşman sayısını örnekler | K07 · TS-203 · TS-303 | `[DD-TEST] enemies.activeMax=<n>` |
| 8 | Upgrade ekranını açar, kart alır, geri sayımı bekler | K15 · K16 | `[DD-TEST] flow.step=Upgrade` → `Countdown` |
| 9 | Round 2 ve 3'ü aynı biçimde kapatır | K16 | `[DD-TEST] round.state=Complete round=3` |
| 10 | 1 sn Pause uygular, tüm sayaç deltalarını okur | K26 · TS-216 · TS-313 | `[DD-TEST] pause.deltas=player:0,enemy:0,sw:0,regen:0,round:0` |
| 11 | Oyuncuyu öldürür | K18 | `[DD-TEST] run.end=death revive=false` |
| 12 | Run Results'ta 12 alanın dolu olduğunu doğrular | K19 · TS-108 | `[DD-TEST] results.fields=12/12` |
| 13 | Main Menu'ye döner | K22 | `[DD-TEST] flow.step=MainMenu` |
| 14 | Adım dizisini beklenen sırayla karşılaştırır | TS-224 | `[DD-TEST] flow.sequence=OK` |

**Kritik tasarım kuralı:** her adım kendi durumunu kurar ve yıkar (`18 §izolasyon`). Duman testi tek bir `[UnityTest]`'tir çünkü akış **sıralıdır**; ama T1/T2 testleri hiçbir koşulda sıralı bağımlılık kurmaz.

**Duman testi asla `Assert.Pass()` ile erken çıkmaz.** Erken çıkış = yalancı yeşil.

### 3.3 · Sonuç okuma

```bash
xmllint --xpath 'concat("sonuç=", /test-run/@result, " kaldı=", /test-run/@failed)' \
        "$OUT/03-playmode.xml"; echo
grep -c "\[DD-TEST\] flow.step" "$OUT/03-playmode.log"
grep "\[DD-TEST\] flow.sequence" "$OUT/03-playmode.log"
```

**Eşik:** `failed=0` **ve** `flow.sequence=OK` **ve** `flow.step` satır sayısı ≥ 5.

---

## K4 · KONSOL HATASI TARAMASI · saniyeler

```bash
grep -nE "NullReferenceException|MissingReferenceException|MissingComponentException|UnassignedReferenceException|IndexOutOfRangeException|ArgumentException|InvalidOperationException|Assertion failed|The referenced script .* is missing|Missing Prefab|Look rotation viewing vector is zero|SetDestination.*not.*NavMesh|Failed to create agent" \
  "$OUT/03-playmode.log" | tee "$OUT/04-konsol-bulgu.txt"
wc -l < "$OUT/04-konsol-bulgu.txt"
[ -s "$OUT/04-konsol-bulgu.txt" ] && echo "K4 KIRMIZI" || echo "K4 YEŞİL"
```

**Warning sayımı da kaydedilir** (eşik değil, eğilim ölçüsü — `TS-309` çürütme koşulu):

```bash
grep -cE "^\s*(Warning|WARNING)|\[Warning\]" "$OUT/03-playmode.log" > "$OUT/04-warning-sayisi.txt"
cat "$OUT/04-warning-sayisi.txt"
```

> `Debug.LogWarning` ile bastırılmış hatalar bu sayıma girer. Warning sayısı bir önceki koşuya göre **arttıysa** temizlik iddiası zayıflar ve rapora yazılır.

---

## K5 · KOPUK REFERANS · statik tarama · Unity açmadan · ~30 sn

### 5.1 · Kopuk script referansı

```bash
grep -rn "m_Script: {fileID: 0}" "$PROJ/Assets" \
  --include=*.unity --include=*.prefab --include=*.asset \
  | tee "$OUT/05-kopuk-script.txt"
[ -s "$OUT/05-kopuk-script.txt" ] && echo "K5a KIRMIZI" || echo "K5a YEŞİL"
```

### 5.2 · Çözülmeyen GUID — asıl kanıt

Sahne/prefab/asset içinde **atıf yapılan** her GUID'in projede bir `.meta` karşılığı olmalı.

```bash
# 1) atıf yapılan guid'ler
grep -rhoE "guid: [0-9a-f]{32}" "$PROJ/Assets" \
  --include=*.unity --include=*.prefab --include=*.asset --include=*.mat --include=*.inputactions \
  | sed 's/guid: //' | sort -u > "$OUT/05-atif-guid.txt"

# 2) projede var olan guid'ler (Assets + çözülmüş paketler)
{ grep -rhoE "^guid: [0-9a-f]{32}" "$PROJ/Assets" --include=*.meta
  grep -rhoE "^guid: [0-9a-f]{32}" "$PROJ/Library/PackageCache" --include=*.meta 2>/dev/null
} | sed 's/guid: //' | sort -u > "$OUT/05-var-guid.txt"

# 3) fark = kopuk referans (Unity yerleşik asset guid'leri elenir)
comm -23 "$OUT/05-atif-guid.txt" "$OUT/05-var-guid.txt" \
  | grep -vE "^0{16}[0-9a-f]{16}$" \
  | tee "$OUT/05-cozulmeyen-guid.txt"
[ -s "$OUT/05-cozulmeyen-guid.txt" ] && echo "K5b KIRMIZI" || echo "K5b YEŞİL"
```

**Bir GUID'in kime ait olduğunu bulmak:**

```bash
G=$(head -1 "$OUT/05-cozulmeyen-guid.txt")
grep -rln "guid: $G" "$PROJ/Assets"
```

**Çürütme:** tarama boş ama K4'te `MissingReferenceException` çıkarsa **tarama yöntemi yalanlanmıştır** — yöntem genişletilir, K5 "geçti" sayılmaz.

---

## K6 · ÇEVRİMDIŞILIK VE İZOLASYON TARAMASI · ~10 sn

```bash
# ağ API kullanımı — çıktı boş olmalı
grep -rnE "UnityWebRequest|System\.Net|HttpClient|TcpClient|UdpClient|Socket\(|WebSocket|Analytics\.|Application\.OpenURL|Ping\(" \
  "$PROJ/Assets/_Prototype" --include=*.cs | tee "$OUT/06-ag-kullanimi.txt"

# planning/ ve game/ AJANLAR tarafından değiştirilmemiş mi — paket başlangıç manifestine göre
# (kullanıcı/Pusula güncellemeleri meşrudur; ölçüt tarih değil, manifest SHA-256 farkıdır.
#  $MANIFEST_PLANNING = paket başında alınan "sha256  yol" listesi, repo dışında.)
shasum -a 256 -c "$MANIFEST_PLANNING" 2>&1 | grep -v ': OK$' | tee "$OUT/06-izolasyon-ihlali.txt"

[ -s "$OUT/06-ag-kullanimi.txt" ] || [ -s "$OUT/06-izolasyon-ihlali.txt" ] \
  && echo "K6 KIRMIZI" || echo "K6 YEŞİL"
```

> Paket süresince `planning/` SHA-256 farkı = 0 beklenir. Fark varsa önce kullanıcı/Pusula değişikliği mi diye sorulur; **ajan kaynaklı fark kapıyı değil, görevi durdurur.**

---

## K7 · `PROTOTYPE-ONLY` KÖKEN ETİKETİ DENETİMİ (S6) · ~10 sn

```bash
# config alanlarının kaçı [PrototypeOnly] taşıyor
CFG="$PROJ/Assets/_Prototype/Core/Config"
echo "etiketli alan : $(grep -rc "\[PrototypeOnly" $CFG/*.cs | awk -F: '{s+=$2} END {print s}')"
echo "serialize alan: $(grep -rcE "^\s*(public|\[SerializeField\])" $CFG/*.cs | awk -F: '{s+=$2} END {print s}')"

# köken anahtarı boş bırakılmış olan var mı — çıktı boş olmalı
grep -rn "\[PrototypeOnly(\s*)\]\|\[PrototypeOnly(\"\")\]" $CFG/*.cs | tee "$OUT/07-kokensiz.txt"

# config dışında sabit kodlanmış sihirli sayı — TS-101 çürütme koşulu
grep -rnE "=\s*(34|2\.7|10|0\.55|11|8\.5|30|1\.5|7|13|42|1\.7|1\.2|22|38|28|16|15|11|12|3\.5|6\.2|28|720|4\.5|12)f?\s*;" \
  "$PROJ/Assets/_Prototype" --include=*.cs \
  | grep -v "/Config/" | grep -v "/Tests/" | tee "$OUT/07-sihirli-sayi.txt" | head -30
```

`07-sihirli-sayi.txt` boş olmayabilir (döngü sayaçları vb. yakalanır) — **elle gözden geçirilir**, otomatik kırmızı değildir. `07-kokensiz.txt` **boş olmak zorundadır**.

---

## K8 · KAPI ÖZETİ

```bash
{
  echo "KAPI ÖZETİ · $STAMP"
  echo "K0 test-framework : $(grep -qc "com.unity.test-framework" "$PROJ/Packages/manifest.json" && echo VAR || echo YOK)"
  echo "K1 derleme        : $([ -s "$OUT/01-derleme-bulgu.txt" ] && echo KIRMIZI || echo YEŞİL)"
  echo "K2 editmode       : $(xmllint --xpath 'concat(/test-run/@passed,"/",/test-run/@total," kaldı=",/test-run/@failed)' "$OUT/02-editmode.xml" 2>/dev/null)"
  echo "K3 playmode       : $(xmllint --xpath 'concat(/test-run/@result," kaldı=",/test-run/@failed)' "$OUT/03-playmode.xml" 2>/dev/null)"
  echo "K3 akış dizisi    : $(grep -c "flow.sequence=OK" "$OUT/03-playmode.log" 2>/dev/null)"
  echo "K4 konsol hatası  : $(wc -l < "$OUT/04-konsol-bulgu.txt" 2>/dev/null | tr -d ' ')"
  echo "K4 warning        : $(cat "$OUT/04-warning-sayisi.txt" 2>/dev/null)"
  echo "K5 kopuk script   : $(wc -l < "$OUT/05-kopuk-script.txt" 2>/dev/null | tr -d ' ')"
  echo "K5 çözülmeyen guid: $(wc -l < "$OUT/05-cozulmeyen-guid.txt" 2>/dev/null | tr -d ' ')"
  echo "K6 ağ kullanımı   : $(wc -l < "$OUT/06-ag-kullanimi.txt" 2>/dev/null | tr -d ' ')"
  echo "K6 izolasyon ihl. : $(wc -l < "$OUT/06-izolasyon-ihlali.txt" 2>/dev/null | tr -d ' ')"
  echo "K7 kökensiz alan  : $(wc -l < "$OUT/07-kokensiz.txt" 2>/dev/null | tr -d ' ')"
} | tee "$OUT/KAPI-OZETI.txt"
```

**KAPI YEŞİL koşulu — hepsi birden:**

```
K0 = VAR
K1 = YEŞİL
K2 : kaldı = 0  ve  total ≥ 40
K3 : kaldı = 0  ve  akış dizisi = 1
K4 : konsol hatası = 0
K5 : kopuk script = 0  ve  çözülmeyen guid = 0
K6 : ağ kullanımı = 0  ve  izolasyon ihlali = 0
K7 : kökensiz alan = 0
```

Bir tanesi bile tutmuyorsa **KAPI KIRMIZI**. Kırmızı kapıyla manuel kit koşulmaz.

---

## 6 · YALANCI YEŞİL SINAMASI · **kapı kendini kanıtlar** · `O7` · ~15 dk

> **WP-000'de ÇALIŞTIRILMAZ.** Untracked repoda geri alma garantisi yok; başlangıç commit'i sonrasında S-10 kararıyla ayrı branch'te.

Unity/C# için çevrimdışı çalışan bir mutasyon aracı yok. Yerine **elle sabotaj seti** koşulur: kapı yeşilken beş kasıtlı kırık sokulur ve kapının **hepsini** yakalaması beklenir.

**Kural:** her sabotajdan önce dosyanın kopyası alınır, sonra **aynen** geri konur. Depoda commit yok, `git checkout` yok.

```bash
export SAB="$OUT/sabotaj"; mkdir -p "$SAB"
yedekle() { cp "$1" "$SAB/$(basename "$1").orig"; }
geri()    { cp "$SAB/$(basename "$1").orig" "$1"; }
```

| # | Sabotaj | Nereye | Yakalaması gereken | Yakalanmazsa |
|---|---|---|---|---|
| **S-1** | `min(7 + 2R, 32)` → `7 + 2R` (tavan kaldırılır) | round/spawn formül dosyası | **K2** (TS-202 + TS-203) | Tavan iddiası hiç test edilmiyor — `K07` kanıtı **geçersiz** |
| **S-2** | Bir prefab referansı Inspector'da boşaltılır (örn. Enemy prefab) | sahne veya prefab | **K5a/K5b** veya **K4** | Kopuk referans taraması kör — `K27` kanıtı **geçersiz** |
| **S-3** | Boot script'inin `Awake`'ine `Debug.LogError("sabotaj");` eklenir | Core/Boot | **K4** | Konsol taraması kör — S5'in üçüncü maddesi doğrulanamıyor |
| **S-4** | Pause'ta Special Weapon zamanlayıcısı `Time.unscaledDeltaTime` ile beslenir | Special Weapon zamanlayıcısı | **K3** adım 10 (`pause.deltas`) | `K26` yalnız `timeScale` bakarak "geçmiş" — tur-kıran ölçüt kör |
| **S-5** | Patlamada zırhı aşan pay cana aktarılır (`overflow → health`) | hasar çözüm dosyası | **K2** (TS-206) | Patlama modelinin **en ayırt edici** kuralı test edilmiyor — `K09` kanıtı geçersiz |

**Koşu biçimi (her sabotaj için):**

```bash
yedekle "<dosya>"
# ... düzenle ...
# yalnız ilgili kapıyı koş (tam kapı gerekmez)
"$UNITY" -batchmode -nographics -projectPath "$PROJ" -runTests -testPlatform EditMode \
  -testResults "$OUT/sab-S1.xml" -logFile "$OUT/sab-S1.log"
xmllint --xpath 'string(/test-run/@failed)' "$OUT/sab-S1.xml"   # > 0 BEKLENİR
geri "<dosya>"
```

**Sonuç kartı:**

```
YALANCI YEŞİL SINAMASI · $STAMP
  S-1 tavan kaldırıldı ........ yakalandı / KAÇTI
  S-2 referans boşaltıldı ..... yakalandı / KAÇTI
  S-3 LogError eklendi ........ yakalandı / KAÇTI
  S-4 pause sızdırıldı ........ yakalandı / KAÇTI
  S-5 patlama taşması ......... yakalandı / KAÇTI
  yakalama oranı: __/5     eşik: 5/5
```

**`5/5` altındaki her sonuç kapının kendisini kırmızıya çevirir.** Yakalayamadığı sabotaj için önce **test yazılır**, sonra kapı yeniden koşulur.

---

## 7 · KIRILGANLIK ÖLÇÜMÜ · `O3`

Standart 10 koşudur. **Burada 3 koşu alınıyor** — gerekçe: tam kapı ~25 dk, 10 koşu ~4 saat, bu turun bütçesinde yok.
Bu bir **zayıflatılmış ölçümdür ve raporda böyle geçer.**

```bash
for i in 1 2 3; do
  "$UNITY" -batchmode -projectPath "$PROJ" \
    -runTests -testPlatform PlayMode -testCategory "Smoke" \
    -testResults "$OUT/kirilgan-$i.xml" -logFile "$OUT/kirilgan-$i.log"
  echo "koşu $i: $(xmllint --xpath 'concat(/test-run/@result," kaldı=",/test-run/@failed)' "$OUT/kirilgan-$i.xml")"
done
```

**Eşik:** 3 koşunun üçü de aynı sonucu vermeli.
**3 koşuda 1 sapma bile → karantina.** Kör yeniden koşu yasak; sapan test `karantina.md`'ye kök nedeniyle yazılır ve kapıdan geçici olarak çıkarılır (çıkarıldığı kapsam boşluğu olarak sayılır).

---

## 8 · `D3` · SIFIRDAN YENİDEN ÜRETİM · TS-511 · ~20 dk

"Benim makinemde çalışıyor"un çürütmesi. **GT koşar, hücrenin kanıtını kullanmaz.**

```bash
# WP-000 (2026-09-10): D3 = ANA PROJEYE DOKUNMADAN, repo dışı ayrı doğrulama kopyasında sıfırdan import.
# Ana Library/ ve kullanıcı profili SİLİNMEZ (WP-000 Aşama 2.2 / 1.7, DEC-063).
# 1) Library'siz kopya kur (kaynak: ana proje; hedef: repo dışı)
SRC="/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept"
V="$PROJ"   # doğrulama kopyası yolu (yukarıdaki export)
rsync -a --exclude .DS_Store "$SRC/Assets/" "$V/Assets/"; rsync -a "$SRC/Packages/" "$V/Packages/"; rsync -a "$SRC/ProjectSettings/" "$V/ProjectSettings/"

# 2) soğuk profil: testler TestProfileIsolation ([SetUpFixture]) ile ProfileStore.SetPathOverride üzerinden
#    geçici izole yol kullanır; gerçek profil dosyasına dokunulmaz, silinmez.

# 3) kapıyı KOPYADA baştan koş: K1 → K2 → K3 → K4 → K5 → K6 → K7 (PROJ=$V, OUT repo dışı)
```

**Eşik:** K1-K7 aynı sonucu verir.
**Çürütme:** tek bir test farklı sonuç verirse, o testin önceki tüm kanıtı **kırılgan** işaretlenir ve panelde kırılganlık listesine girer.

---

## 9 · KAPININ SINIRI — bilerek yapılmayanlar

| Yapılmıyor | Neden | Nereye düşüyor |
|---|---|---|
| Mutasyon skoru | Unity/C# için çevrimdışı araç yok | §6 elle sabotaj seti (ikame) |
| 10 koşuluk kırılganlık | Süre bütçesi | §7 · 3 koşu · zayıflatılmış, raporda işaretli |
| Gamepad davranışı | Donanım yok (`niyet §9d`) | `⊘ G-01` · `K05b koşulamadı` |
| Girdi gecikmesi ölçümü | Ölçüm donanımı yok | `⊘ G-10` |
| Görsel regresyon (referans kare) | Bu turda kurulmuyor | `⊘ G-07` |
| Player build ve Player.log taraması | WP-000 G6–G9 kapsamında (doğrulama build'i, `wp-000/verify-build/`; platform kararı değil) | WP-000 kanıt paketi (d), (e1), (e4) |
| **His imzası (öncelik 1-5)** | **S5 — otomatikleştirilemez** | **`MANUEL-TEST-KITI.md` · kullanıcı** |

**Kapı yeşilse söylenen tek şey şudur:** *prototip derleniyor, akış kapanıyor, konsol temiz ve referanslar bağlı.*
**Kapı yeşilse söylenmeyen şey:** *prototip iyi hissettiriyor.* Bunu yalnız kullanıcı söyler.
