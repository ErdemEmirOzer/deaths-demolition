# CX-AP025-A RAPOR 1 — 05.10.2026
## ÖZET (≤ 10 satır)
Durum: **KAPI — G3 seçimi bekliyor.**
Ne üretildi: kanıtlı teşhis, dört zombi yönü, karşılaştırma ve sayısal üretim taslağı.
Seçenek paketi: [options-v002](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/options.md).
Manifest: [AP025-manifest-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/manifest/AP025-manifest-v002.json), SHA `870ea9d73e44`.
Kalite: lider karar paketi incelemesi ACCEPT_WITH_FIXES; mekanik doğrulama **PASS — 27/27 SHA, 9/9 görsel lisans kapsamı**.
Açık: O3/O4 bol gore eksik, O3 uzun Elite frame; runtime, DEC-077 ve FPS ölçülmedi.
🔴 / sorular: 1 — G3 yön seçimi; öneri O1 → O4 → O2 → O3.
İstek: [CX-AP025-A-ISTEK-1.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP025-A-ISTEK-1.md) — atlas runtime doğrulaması ve B kapsamı.
Sonraki: C-00 seçimi sonrası B; burada duruldu.

## 1. Yapılanlar

AP-024 iptal talimatında alt ajanlar durduruldu; o alandaki dosyalar silinmedi ve yeni AP-024 işi yapılmadı. CODEX-SISTEM-R2, CX-AP025-A ve KADRO HIZ MODU okundu. AP-007/AP-007C, AP-014, AP-011B, AP-008 ve repo görüntüleri salt okunur incelendi. İki konsept üreticisi paralel dört yönü, mevcut araştırma ajanı teknik teşhis dosyalarını üretti. Araştırma ajanının rol adı dd_game_designer olsa da AP025 işi teknik araştırma üretimidir; ayrı estetik/oynanış incelemesi yaptırılmadı.

Lider özgün dört PNG’yi, baş projeksiyonunu, UV/atlas teşhis görselini ve dünya karelerini gördü. Dört farklı yön ıslak gri-mor, kuru kül, soluk sivil keten ve koyu ağır indigo kütleleriyle ayrılır. HIZ MODU sonrası konsept revizyonu yok; yalnız dört yönün karşılaştırma özet görseli derlendi. v001 yerine v002 güncel pakettir; önceki taslak ve kullanılmayan O3 editi korunur.

Teşhisin ana sonuçları:

- Kaynak AP007 v003 başı Head ağırlık≥.5 seçimiyle **267 vertex / 453 tri**; 2K gövde atlasında baş UV alanı **%1.482**, raster union **%1.495**. Normal ortalama #8080FF’e yakın; ayrıntılı yüz geometrisi ve yüz bake’i zayıf. AP007 README:71/109 yüzsüz AP006 mannequin başını açıkça bildirir.
- SkinMask R>127 alanında kaynak ortalama ten **#7A8271**, baş **#838C75**; fallback ten **#7E8674**. Yeşilimsi taban ve tarihsel doku ayrıntısı birlikte etkili. Ölçüm albedo kaynağından; oyun ışığı bu değerle aynı değildir.
- `ZombieAppearance.cs:475–493` ApplyTierTint, daha önce body/cloth ayrılmış rendererların sharedMaterialını tier body malzemesine çevirir. T1 cloth primitive aynı UV alanında doğru cloth atlasından **#2B2F42**, body atlasından **#61675A** okur. Statik atama zinciri doğrulandı; şikâyetin gerçek Player’daki payı **ölçülmedi**. Aktif malzemelerde BaseMap/BumpMap var; “bütün dokular eksik” sonucu desteklenmiyor.
- Mevcut ORM repack doğru kanalları kullanıyor: R=metal, G=AO, A=1−roughness. AP007 iki 2K atlas/8 PNG, **35,704,798 byte**; mevcut Standard tam LOD0 yaklaşık **11,900 tri**, 40 kişi **476,000 tri** yalnız geometri hesabı. GPU zamanı değildir.

Ayrıntılı kaynak yolları/satırlar/SHA ve yöntem [teşhis](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/01-design/AP025-A-teshis.md) ve [kanıt JSON](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/validation/diagnosis-evidence.json) içindedir. CPU baş/UV görselleri Unity screenshotu değildir. Repo main **e0ec412060b3eb1d2c0ba3944a9cb24703552e9c** incelendi; tarihsel screenshotların çekildiği commit bilinmiyor.

## 2. Üretilen dosyalar

| Yol | Tür / özellik | SHA256 kısa |
|---|---|---|
| [02-concept/options-v002/O1.png](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/O1.png) | 1536×1024 konsept | `3acd32181d10` |
| [02-concept/options-v002/O2.png](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/O2.png) | 1536×1024 konsept | `db23d7a5568a` |
| [02-concept/options-v002/O3.png](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/O3.png) | 1536×1024 konsept | `217ae8ae4111` |
| [02-concept/options-v002/O4.png](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/O4.png) | 1536×1024 konsept | `14528d7c3ba0` |
| [02-concept/options-v002/contact-sheet.png](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/02-concept/options-v002/contact-sheet.png) | 1536×1024 konsept | `1bc34bcb3b09` |
| [01-design/AP025-A-teshis.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/01-design/AP025-A-teshis.md) | Faz A belge/kanıt | `0791f8e8fa37` |
| [01-design/AP025-A-uretim-spesifikasyonu.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/01-design/AP025-A-uretim-spesifikasyonu.md) | Faz A belge/kanıt | `03e1a9fcaae9` |
| [validation/diagnosis-evidence.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/validation/diagnosis-evidence.json) | Faz A belge/kanıt | `b7aae0f8f431` |
| [validation/leader-quality-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/validation/leader-quality-v002.json) | Faz A belge/kanıt | `9227b0e45b60` |
| [03-sources/LICENSES.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/03-sources/LICENSES.csv) | Faz A belge/kanıt | `a54b889d53b8` |

Manifest 26 sabit dosyayı; SHA256SUMS bunlar + manifest olmak üzere 27 dosyayı kapsar. Hash listesi [SHA256SUMS](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/manifest/SHA256SUMS). Tam promptlar `01-design/concept-O1-O2.json` / `concept-O3-O4.json`; çağrı kayıtları `03-sources/imagegen-*-ledger.json`. Sayısal hesap aracı `04-tools/measure_plan.py`, dünya ROI sonuçları `validation/reference-palette-v001.json`, bütçe hesabı `validation/production-budget-v001.json`. Mekanik denetim çıktısı hash kapsamı dışındaki son kayıt olarak [mechanical-validation-v002.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/validation/mechanical-validation-v002.json) yoluna yazılır.

## 3. Kalite ve doğrulama

Lider görsel kontrolü: her şıkta yakın anatomik yüz, iki tam boy görünüş, üç deri tonu, dört tier ve tam **10 zombi**; dağılım 4/2/2/2. Kalabalıklar üstten eğik konsept proxy’si, aynı ıslak kale zemin dili. Uzuvlar bağlı, gözler ışıldamıyor. O1/O2 yara/kanlı yönü temsil ediyor; final kan yüzey bandı ve miktarı motor varlığında test edilmedi.

**Açık kusurlar:** O3/O4 bol yara/kan gereksinimini karşılamıyor; 3 imagegen çıktı filtresi reddi sonrası non-graphic sayfalar kullanıldı. O3 seçili doğal yüzlü sayfanın Elite yari uçları crown üstünde; kısa alternatif yüzü bozduğundan teslim dışı. O1 istenen ağır şişkin beden kadar hacimli değil. O3 raster footer kan swatch merkezinin S değeri bant dışı; hedef #661418 üretim JSON’undan okunacak, raster dokuya aktarılmayacak. Diğer raster kan yüzeylerinin HSV’si ölçülmedi. Contact-sheet küçük metni yeniden rasterleştirildi; özgün O1…O4 PNG/JSON bağlayıcı.

B üretim bütçesi **taslak**, performans PASS iddiası yok:

| Tier | LOD0 / 1 / 2 / 3 toplam actor tri |
|---|---|
| Standard | 24,000 / 12,000 / 5,000 / 1,500 |
| Special | 28,000 / 14,000 / 6,000 / 2,000 |
| Elite | 32,000 / 16,000 / 7,000 / 2,500 |
| Ultra / Executioner | 36,000 / 18,000 / 8,000 / 3,000 |

40 kişi (20/8/8/4 tier karışımı) önerilen LOD dağılımıyla Cinematic **422,000**, High **343,000**, Performance **190,500 tri**; gölge/depth/geçiş çift çizimi hariç. Tam mipli paylaşılan BC7/BC5 doku hesabı **154.7 / 90.7 / 26.7 MiB**; sınıf sınırı **192 / 128 / 64 MiB**. Baş/gövde/kıyafet ayrı 2K–4K; LOD ekran yüksekliği .20/.08/.03, materyal slotu yakın≤5 / uzak≤2. Toplam frame hedefleri Cine 33.3ms, High 16.6ms, Perf 8ms; bunlar cihaz profiliyle B’de sınanacak **hedeflerdir**.

DEC-077 siluet≥%30, zırh parlaklık min.08/ort.12; oyuncu/zemin ΔY≥.06, gerçek camera ölçeği, rig deformasyonu, collision, LOD geçişi ve GPU/CPU/FPS **ölçülmedi / açık**. Phase A kabulü G3’te yön seçimine yeterli karar bilgisi içindir; tüm görsel ve motor üretim ölçütleri kapanmış sayılmaz.

Tek mekanik dd_validator denetimi **PASS**: 26 manifest item + manifest = **27/27 SHA**, seçili 5/5 RGB PNG1536×1024, toplam 9/9 görsel decode ve lisans kapsamı, 10 JSON/2 CSV parse, 8/8 rapor bölümü; kritik hata 0. Estetik inceleme liderde; ayrı AD/game/ikinci validator yok. Mekanik PASS gore kusurunu kapatmaz. Önceki teslimlerin değişmediği bağımsız başlangıç hashleriyle ölçülmedi; kullanıcı iptalinden sonra AP-024 işi yapılmadı.

## 4. Kaynaklar ve lisanslar

Dört özgün yön ve özet Codex yerleşik **imagegen** aracıyla üretildi. Toplam **13 çağrı / 10 başarılı çıktı / 3 çıktı filtresi reddi**, seçili 5 görsel. Model kimliği, seed ve kredi tutarı araç bildirmedi; maliyet ölçülmedi. Higgsfield/Adobe 0 çağrı. Kullanılan skill: [imagegen SKILL.md](/Users/Erdem/.codex/skills/.system/imagegen/SKILL.md). Dış sanat/paket indirilmedi; mevcut proje varlıkları iç referans olarak okundu.

[LICENSES.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/03-sources/LICENSES.csv) her seçili görsel/kanıt türevini ve belge referanslarını ayrı kaydeder. Üretilen output sahipliği [OpenAI Terms of Use](https://openai.com/policies/terms-of-use/) koşullarına bağlıdır; CC0 diye etiketlenmedi. Mevcut proje referanslarının lisansı değişmedi; bu rapor yeni yeniden dağıtım hakkı vermez.

URP14 Lit/PBR, normal ve smoothness planı [Unity Lit](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/lit-shader.html) belgesine dayanır. SkinnedMeshRenderer için otomatik GPU instancing varsayılmadı ([Unity GPU Instancing](https://docs.unity3d.com/2022.3/Documentation/Manual/GPUInstancing.html)); MaterialPropertyBlock SRP Batcher uyumunu bozabildiğinden sınırlı materyal paleti/MPB A/B ölçümü önerildi ([Unity SRP Batcher](https://docs.unity3d.com/2022.3/Documentation/Manual/SRPBatcher.html)). Öneriler uygulanmış shader veya ölçülmüş hızlanma değildir.

## 5. Entegrasyon notları

Bu fazda FBX/PBR eksportu, yeni rig, Unity importu ve Player koşusu yok; 1536×1024 PNGler §4.3 konsept, 2K/4K PBR doku değildir. Paket repo/main/Unityye yazmaz. Sahip oturum önce tier body/cloth atamasını runtime doğrulamalı; yeni baş geometri/bake işi bundan bağımsız gereklidir.

B taslağı üç modüler baş, anatomik sculpt/retopo ve ayrı baş UV/bake; ORM kaynak R=AO/G=rough/B=metal, Unity repack sözleşmesi ayrı. Wet/blood/skin/dirt maskeleri, seçici göz parlaklığı ve deneysel thickness/backlight SSS yaklaşımı URP14 sınırında tanımlandı; gerçek SSS teslim edilmedi. Kan rengi skin tintten sonra uygulanır. 3 ten×3 kir×3 kan=27 varyasyon taslağı.

AP026 rig kararı bekleniyor; geçiş mevcut **73 joint (56 ana +17 ikincil)**, vertex başına≤4 ağırlık. Executioner beden ölçeği **1.48**; ×.75 kadans notudur, beden küçültme veya bütün kliplere sabit hız komutu değildir. AP014 tier parçaları korunur; crown sınırı ve 1.60m/1.52m mevcut ekipman zarfı B’de fit edilir. Hero render/30fps turntable/4-tier sıra/40-crowd planı yazıldı, render üretimi G3 sonrasındadır.

## 6. C-00'A SORULAR / 🔴 KARARLAR

**1 — G3:** O1, O2, O3, O4 veya açıkça tarif edilen malzeme karması? Önerim **O1**; koyu ten nüfus varyasyonu için O4 malzemesi sonradan ortak sisteme eklenebilir. O3/O4 seçimi yüksek gore eksikliğiyle, O3 ayrıca crown kusuruyla birlikte yapılmalıdır. Bekleyen iş: seçilen tek aileyi CX-AP025-B’de model/doku üretimine geçirmek. Karar gelmeden B başlamaz.

## 7. Araç / Higgsfield istekleri

[CX-AP025-A-ISTEK-1.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP025-A-ISTEK-1.md) runtime atlas kontrolünü ve seçilen yönün B’de açık gore/crown ölçütünü kapatmasını ister. Higgsfield erişimsizliği iddia edilmez; yeni filtre aşma/hesap/kurulum talebi yok. Yerleşik çıktı filtresi yetersizliği dürüstçe kaydedildi; HIZ MODU gereği üretim uzatılmadı. `03-higgsfield/ledger.csv` 0 çağrı nedeniyle yalnız başlık içerir.

## 8. Sonraki önerilen çalışma

C-00 G3’te seçsin; sahip oturum atlas atamasını doğrulasın; yeni CX-AP025-B seçilen yönün yüz/deri/tier üretimini başlatsın. Açık kusurlar, motor ölçümleri ve cila o fazın kabul kapsamıdır. **Faz A burada durdu.**
