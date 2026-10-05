# C00-AP025C RAPOR — O1 "Boğulmuş Muhafızlar" AAA turu — 05.10.2026

Üretici: general-purpose (3B), Opus oturumu · Paket: `K/asset-production/AP-025` · Sürüm: exports **v002**, manifest **v004**
Durum: **varlık tarafı teslim edildi; Unity tarafı ÖLÇÜLMEDİ.** Bu nedenle "oyuna hazır" denmiyor (bkz. §6).

## ÖZET (≤ 10 satır)

- Üç yeni baş tipi (Gaunt v4a / Swollen v3 / Riven v2) Higgsfield konseptinden Tripo H3.1 Multiview ile 3B'ye çıkarıldı. Her biri radyal ızgara retopoyla 4 LOD'a indirildi, kafes bake yapıldı ve O1 paletine göre derecelendirildi. LOD üçgenleri 11.888 / 5.360 / 2.216 / 752.
- 32'lik ortak boyun halkası birebir korundu: FBX'ten geri okumada konum farkı en çok 0,00028 mm, UV farkı 0. 73 kemik, FBX 7400. Başlarda en çok 2, bedende en çok 4 etki var.
- Ağır beden H2 fit C'ye uygulandı. Gövde ile birlikte 24 giysi modülü ve 4 tier parçası yeniden oturtuldu. Kesişme 15.449 (B'de 41.825).
- Tek `M_Zombie_Atlas` (2048 BaseColor + 2048 Normal + 1024 ORM, 12,58 MB) eski gövde atlasının yerine geçiyor. 9 BC4 maske 6,29 MB, ≤12 MB kapısı PASS.
- Kan pigmenti texellerinin tamamı (23.973 / 23.973) H355–5 / S0,70–0,85 / V0,25–0,45 bandında.
- C-00'ın üç MUST'ı karşılandı:
  - Boyun "silindiri" giderildi, TP 3/4 planda dikiş fark edilmiyor.
  - Beden teni başla aynı O1 karosunu, lekeleri, morlukları ve yara/kan izlerini kullanıyor.
  - Portre ışığı daha karanlık ve kontrastlı.
- Kabul renderları üretildi: portre (3 baş + 1 kahraman), tam beden, tier sırası, 50°'den 40 zombi kalabalığı, B ile yan yana 100 px sayfa, TP 3/4 orta plan.
- Manifest v004 235 öğe içeriyor. İki SHA256SUMS dosyası yeniden doğrulandı: 59/59 ve 236/236 OK, 0 hata.
- Higgsfield harcaması 165,75 kredi (29 defter satırı).
- Açık kalanlar §6'da. En önemlileri:
  - Unity'de hiçbir ölçüm yapılmadı.
  - Yakın portrede z=1,50/1,53'te silik çizgiler kalıyor.
  - Sözleşme halkası Swollen'ın kalın boynuna göre dar.
  - dd_validator koşulmadı.

## 1. Yapılanlar

### 1.1 Konsept ve 3B kaynak (kontrol noktası 1, C-00 onaylı)

- Nano Banana Pro ve GPT Image 2.5 ile her baş için ön, 3/4 ve dönüş görselleri üretildi. Seçimler: Gaunt v4a, Swollen v3, Riven v2.
- Tripo H3.1 Multiview ile 3B'ye çıkarıldı. Görünüm sırası ön / sol / arka / sağ.
- Aynı girdilerle Hunyuan3D v3 de denendi (3 × 19 kredi). Karşılaştırmada Tripo seçildi; Hunyuan çıktıları kullanılmadı.
- Ağır beden için H1 ve H2 seviyeleri sınandı; C-00 H2'yi onayladı. A ve B fitleri değişmedi.

### 1.2 Başlar (`04-tools/v002/head_build_v2.py`, `head_grid.py`, `head_finalize_v2.py`)

- **Retopo:**
  - Baş merkezine doğru içe yönelen ışınlarla bir enlem-boylam ızgarası kuruldu; dış zarf bu ışınların ilk çarptığı noktalardan oluşuyor.
  - Boylam yüze doğru sıklaşıyor; enlem yoğunluğu yüz ve boyun bandında tepe yapıyor.
  - Saç bölgesinde gri tonlu kapatma, yüz bölgesinde sivri uç temizliği (4 mm) uygulandı.
  - LOD ızgaraları 104×56, 72×36, 52×20 ve 30×11.
- **UV:** Her baş kendi atlas bölgesinde tek UV adası. Göz, diş ve dişeti aynı ada ve aynı ağ içinde. Tüm LOD'lar aynı UV düzenini paylaşıyor.
- **Bake:** Kafes ile seçiliden aktife (yüz +3 mm, saç +20 mm). Pişirilen kanallar: Diffuse, tanjant Normal, Roughness, AO ve konum.
- **Boyun:**
  - Göz yüksekliği 1,672 m'ye sabitlendi; boyun 1,8 cm kısaldı.
  - Daraltma katsayısı Swollen'da 1,18, diğerlerinde 1,10. Swollen'ın gıdı kütlesi korunuyor.
  - z=1,53 kesiminden 32'lik halkaya fermuar kolye bağlandı. Halkanın konum, UV, normal ve ağırlık değerleri sözleşmeyle birebir.
  - Ağırlıklar Head/Neck rampası (≤2 etki). Köşe rengi `Color.R` ten bayrağını taşıyor.
- **Islak ve toplu saç:** Kaynaktaki ince kanatçıklar ızgara kapatmasıyla katı kütleye çevrildi. Alfa yok, bu yüzden testere dişi kenar da yok.

### 1.3 Atlas ve maskeler (`atlas_build_v2.py`)

- **Ten:** O1 parlaklık rampası `#4A4552 → #69666D → #8C9292 → #B5B6AD` uygulandı. Mor yalnız damar ve benek kromasında kalıyor.
- **100 px okunurluğu için koyulaştırmalar:**
  - AO kovuk koyulaştırması;
  - göz çukuru halkası;
  - ağız boşluğu.
- **Gözler:** Katarakt, iris/sklera ayrımı kaynaktan geliyor. V ≤ 0,62, emisyon 0, roughness 0,36.
- **Dişler:** Kirli sarı-kahve, çekilmiş dişeti.
- **Kan iki katman:**
  - kurumuş koyu çekirdek, roughness 0,55;
  - ıslak parlak kenar, roughness 0,18.
  - Pigment texelleri ölçülen banda sıkıştırıldı. Riven'ın çene damlası korundu.
- **Seçici ıslaklık:** Saç 0,40; yara kenarı, dudak ve göz düşük roughness. Ten roughness medyanı 0,65.
- **Boyun şeridi:** Halkanın üstündeki satırlar alttaki beden satırlarından aynalandı (normal G çevrildi). Baş tabanında silindirik karo harmanı var.
- **Maskeler:**
  - Sol yarı yeni baş bölgeleri ve H2 beden konumları için yeniden üretildi.
  - Sağ yarı (giysi) v001'den aynen kopyalandı.
  - Üç ten tonu mevcut Skin_00..02 tint sistemiyle sağlanıyor.

### 1.4 Ağır beden H2 ve giysiler (`heavy_field.py`, `body_build_v2.py`)

- **Deformasyon:** Kemik ağırlıklı analitik yer değiştirme ve Laplace yumuşatma, H2 = taban parametreler × 1,45.
- **Aktarım:** Giysi ve tier ağlarına k=12 Gauss enterpolasyonla taşındı. Üst ve alt giysilerde çarpışma çözümü uygulandı (+2,5 mm, en fazla 20 mm; en büyük düzeltme 19,95 mm).
- **Değişmeyenler:** Halka ve köprü bandı sabit, kemikler ve ağırlıklar aynı. Executioner ölçeği 1,48 korundu. Fit D açılmadı.

### 1.5 Beden teni (`skin_tile_prep.py`, `body_tex_bake.py`)

- **Temel:** Higgsfield karo B dikişsiz hâle getirildi, alçak frekans eğimi temizlendi ve O1 rampasına eşlendi. Başla aynı karo kullanılıyor.
- **Ayrıntılar:**
  - benekleşme, livor ve morluklar;
  - 8 yara (çekirdek + kenar + hale + damla);
  - göğüs kan izleri.
- **Yüzey:** Karo yüksekliğinden bump alındı; roughness 0,46–0,72.
- **Kapsam:** Göbek bandı çıplak O1 teni; göğüs dekoltesi de aynı ayrıntıyı taşıyor.

### 1.6 Renderlar (`render_v2.py`, `readability_page.py`)

- Poz B ile aynı "shamble" duruşu; giysiler cw1 çivit.
- Portre ışık düzeni, C-00 MUST 3 üzerine karartıldı: dünya 0,16, pozlama −0,45, anahtar ışık daha sert.
- Kalabalık renderında kenar ışığı kapatıldı. Önceki sürümde sıcak nokta vardı; son renderda yok (görsel olarak kontrol edildi).

## 2. Üretilen dosyalar

Kök: `K/asset-production/AP-025/`

| Yol | İçerik |
|---|---|
| `exports/v002/Heads/` | ZombieFace_{Gaunt,Swollen,Riven}_LOD0..3.fbx (12 dosya) |
| `exports/v002/Bodies/` | Zombie_O1_Body_C.fbx (H2, 4 LOD + anchor_*). A/B, v001 ile bayt-özdeş |
| `exports/v002/Garments_C_H2/` | 24 giysi FBX'i (AP-007 nesne adları korunuyor) |
| `exports/v002/Tiers_C_H2/` | 4 AP-014 tier FBX'i |
| `exports/v002/Textures/` | Zombie_O1_BaseColor / Normal / ORM |
| `exports/v002/Masks/` | 9 × 1024² tek kanal |
| `exports/v002/README.md` | Dosya başına MB tablosu ve doku belleği hesabı |
| `exports/v002/material-contract.json`, `module-map.json` | v002 sözleşmesi |
| `exports/v002/LICENSES.csv`, `SHA256SUMS` | Lisanslar ve sağlama toplamları |
| `06-renders/v002/hero-portrait.png` | 2048², oyun ışığına yakın kahraman portre |
| `06-renders/v002/portrait-trio.png` | 3 baş yan yana (3072×1280) |
| `06-renders/v002/hero-fullbody.png` | Tam beden (1440×1800) |
| `06-renders/v002/tier-lineup.png` | Standard / Special / Elite / Ultra |
| `06-renders/v002/crowd-40-top50.png` | 40 zombi, 50° yukarıdan |
| `06-renders/v002/readability-100px.png` | B v001 ile yan yana 100 px sayfa (+ tekil 100 px dosyalar) |
| `06-renders/v002/tp-34-midshot.png` | TP kamera 3/4 orta plan (1920×1080) |
| `06-renders/v002/checkpoint1/` | Kontrol noktası 1 sayfaları |
| `validation/v004/` | heads-v002, atlas-v002, body-h2, fbx-validation, lod-budget, texture-memory, readability-100px, head-texel-stats, render-v002, checkpoint1-* |
| `manifest/AP025-manifest-v004.json` | 235 öğe; dosya SHA256 `c3f015ed…25b7` |
| `manifest/v004/SHA256SUMS` | Manifest öğelerinin sağlama toplamları |
| `03-sources/v004/LICENSES.csv` | Lisans kaydı |
| `03-higgsfield/ledger.csv`, `03-higgsfield/v002/` | Kredi defteri, istemler (`prompts-v002.json`), konsept/mv-input/glb/tiles |
| `04-tools/v002/*.py` | Tüm üretim betikleri (yeniden üretilebilir) |
| `05-blender/v002/*.blend` | Baş H1 kaynakları, heads-v002, body-o1-h2-source, render-v002, bake npz'leri |

**Değişmeyenler:** v001 ve diğer paketlerin dosyalarına dokunulmadı. Oyun deposuna, `main`'e ve Unity'ye yazılmadı; git işlemi yapılmadı.

## 3. Kalite ve doğrulama (kanıt: `validation/v004/`)

| Kapı | Sonuç | Kanıt |
|---|---|---|
| FBX yeniden içe alım | 43 dosya, 0 sorun; hepsi 7400 ikili, hepsi 73 kemik, yaprak kemik yok | fbx-validation.json |
| Etki sayısı | Baş ≤2, beden/giysi ≤4 | fbx-validation.json |
| Ortak halka (baş ve beden C) | 32/32 köşe bulundu; en büyük konum farkı 0,00028 mm; UV farkı 0; beden halkası H2 sonrası fark 0,0 m | fbx-validation.json, heads-v002.json, body-h2.json |
| Baş LOD üçgenleri (3 baş aynı) | 11.888 / 5.360 / 2.216 / 752; hedef ≤12.600 / 5.700 / 2.400 / 820 | heads-v002.json |
| Tam aktör LOD tavanları | 16/16 PASS (en ağır üst + alt giysi senaryosu da 16/16 PASS) | lod-budget.json |
| Kesişme (BVH) | H2 15.449 (LOD0..3: 3.673 / 2.948 / 4.004 / 4.824); B 41.825 | body-h2.json |
| Kan HSV | Pigment texellerinin 23.973 / 23.973'ü bantta; H −3,5…+3,1°, S 0,72–0,83, V 0,28–0,43 | atlas-v002.json |
| Doku belleği | Atlas 12.582.992 B, 9 maske 6.291.576 B (≤12 MB PASS), toplam 18.874.568 B | texture-memory.json |
| Sağlama toplamları | exports/v002 59/59 OK; manifest/v004 236/236 OK | SHA256SUMS |

**Tam aktör LOD toplamları** (en ağır baş + beden C + T8/B6 cw1 + tier):

| Tier | LOD0 | LOD1 | LOD2 | LOD3 |
|---|---|---|---|---|
| Standard | 21.932 / 24k | 9.745 / 12k | 3.826 / 5k | 1.343 / 1,5k |
| Special | 23.427 / 28k | 10.339 / 14k | 4.022 / 6k | 1.421 / 2k |
| Elite | 22.553 / 32k | 10.366 / 16k | 4.226 / 7k | 1.503 / 2,5k |
| Ultra | 23.431 / 36k | 10.344 / 18k | 3.984 / 8k | 1.421 / 3k |

**Ağır beden kesitleri** (beden C LOD0, çıplak, v001'e göre):

| z (m) | Genişlik | Derinlik | Göbek öne çıkıntısı |
|---|---|---|---|
| 1,00 | +%23 | +%34 | 61 mm |
| 1,05 | +%33 | +%41 | 75 mm |
| 1,10 | 0,408 m (+%41) | +%55 | 96 mm |

Not: Bu oranlar ilk brifteki "+%10–15" bandının üstünde. Ölçü çıplak bedende alınıyor; giysili siluet farkı daha küçük. Bu seviye kontrol noktası 1'de C-00'ın onayladığı H2 seviyesidir.

**100 px okunurluk** (merkez 50×50 px, koyu kütle oranı V<0,2):

| | B v001 | v002 |
|---|---|---|
| Gaunt | 0,44 | 0,70 |
| Swollen | 0,40 | 0,41 |
| Riven | 0,44 | 0,62 |
| Tam aktör | 0,18 | 0,47 |

- Göz çukuru, ağız ve yara kütleleri v002'de daha belirgin.
- p5–p95 değer aralığı daralıyor (yüz 0,58 → 0,48). O1 teni B'nin açık lavantasından koyu olduğu için bu beklenen bir sonuç.
- Swollen'da koyu kütle B ile aynı düzeyde kaldı; şişkin yüzde açık ten alanı geniş.

**Baş texel sınıfları** (V medyanı / V maks.; head-texel-stats.json):

| Sınıf | Gaunt | Swollen | Riven |
|---|---|---|---|
| Ten | 0,42 / 0,63 | 0,49 / 0,65 | 0,45 / 0,63 |
| Göz | 0,41 / 0,54 | 0,44 / 0,55 | 0,41 / 0,49 |
| Ağız | 0,11 | 0,20 | 0,25 |
| Saç (medyan) | 0,04–0,06 | 0,04–0,06 | 0,04–0,06 |

- Gözler parlamıyor.
- Saçta V maks. Gaunt ve Riven'da 0,58: tekil aykırı texeller (§6).
- Swollen'ın kan sınıfında V maks. 0,58: bunlar tene harmanlanan geçiş texelleri. Bant yalnız saf pigment texelleri için ölçüldü.

**Görsel inceleme:** Bütün PNG'ler Read ile tek tek incelendi.

- TP 3/4 orta planda boyun dikişi fark edilmiyor.
- Kahraman portrede z=1,50 ve 1,53'te silik çizgiler görülüyor (§6).

## 4. Kaynaklar ve lisanslar

- Higgsfield (ticari plan): 29 defter satırı, **165,75 kredi**. Ayrıntı `03-higgsfield/ledger.csv` dosyasında (tarih, model, iş kimliği, amaç, istem, durum).
  - 2k görseller 2 kredi, 4k görseller 4 kredi, GPT Image 2.5 high 2,75 kredi.
  - Tripo H3.1 MV 3 × 18 kredi; Hunyuan3D v3 3 × 19 kredi (kullanılmadı).
  - Karolar 2 × 2 kredi.
  - Bir GPT isteği 429 hızlanma sınırına takıldı; 0 kredi.
- Diğer kaynaklar: AP-007 v003 (giysiler), AP-014 (tier) ve AP-025 v001 (iskelet, halka, atlas sağ yarısı). Hepsi proje içi. Dış kütüphane, kurulum, satın alma veya hesap kullanılmadı.
- `03-sources/v004/LICENSES.csv` 178 satır içeriyor; kopyası `exports/v002/LICENSES.csv`.

## 5. Entegrasyon notları (Unity tarafı için)

- **Baş prefab'ları:** `Heads/ZombieFace_<V>_LOD<n>.fbx` dosyalarındaki nesne adları v001 ile aynı. Göz ve dişler yüz ağının içinde, ayrı nesne değil.
- **Fit C:** O1 fit C için `Bodies/Zombie_O1_Body_C.fbx` ile birlikte **mutlaka** `Garments_C_H2/*` ve `Tiers_C_H2/*` kullanılmalı. AP-007 v003'ün fit C giysileri ağır bedenin içinden geçer. A ve B fitleri v001 ile aynı.
- **Atlas:** v002 atlas eski gövde atlasının yerine geçer. Eski body/ID/TierMask haritaları yüklü kalmamalı. UV bölgeleri `material-contract.json` içindeki `atlas_uv_layout` alanında.
- **Değişmeyen sözleşme:** Maske dizisi sırası, renk sırası (taban → ten tint T/Tref → kir → kan), renderingLayerMask bit sözleşmesi ve "MPB yok" kuralı v001 ile aynı.
- **Köşe rengi:** `Color.R` ten bayrağı (saç, göz, diş, ağız ve kan için 0). Shader, ten tint'ini bu bayrakla sınırlamalı.

## 6. Açık kalanlar (dürüst liste)

1. **Unity'de ölçülmeyenler (oyuna hazır değil):**
   - Avatar/humanoid eşlemesi;
   - SRP Batcher ve renderingLayerMask bit aktarımı;
   - 40 zombi ≤96 MB sahne bütçesi;
   - SetPass, draw call ve kare süresi;
   - DEC-077;
   - H0–H7 başlık/saç modülleriyle çakışma;
   - animasyon pozlarında giysi kesişmesi. Kesişme yalnız rest pozda ölçüldü.
2. **Swollen boynu:** 32'lik sözleşme halkası Swollen'ın kalın boynuna göre dar. Daraltma yumuşak, ancak hareket hâlinde boyunda kırışma görülebilir.
3. **Yakın portrede dikiş çizgileri:** z=1,50 (halka) ve z=1,53 (kolye) hizasında silik çizgiler var. TP 3/4 planda görünmüyor; kahraman portrede yakından fark ediliyor.
4. **Izgara kaynaklı kusurlar:**
   - Ense ve saç çizgisinde doku gerilmesi;
   - kulaklar yaklaşık bir şekil;
   - yer yer koyu lekeler.
   Retopo radyal ızgara ile yapıldı; elle akıtılmış quad topolojisi değil. Bu yüzden yüz ifadesi/blendshape deformasyonu için uygun değil.
5. **Saç aykırı texelleri:** Gaunt ve Riven saçında tekil açık texeller var (V maks. 0,58). Medyan çok koyu ve 100 px'te görünmüyorlar.
6. **Gaunt konsept açısı:** 3/4 konseptin dönüş açısı dar (~15°). Profil bilgisi kısmen ayrı bir sol profil görselinden geldi; arka kafa şekli daha az kesin.
7. **Elite sode boşluğu:** Önceden var olan bir durum; AP-014'ten geliyor ve bu turda düzeltilmedi.
8. **AP-007 giysi dokuları değişmedi:** Giysi dokuları yeniden üretilmedi; atlasın sağ yarısı v001 ile aynı. Islak çivit kumaş görünümü yalnız shader/tint düzeyinde.
9. **Ağır beden oranı:** Göbek +%41 (çıplak) ile ilk brifteki +%10–15 bandının üstünde; C-00 H2'yi onayladı. Daha hafif bir seviye istenirse H1 hazır: `05-blender/v002/checkpoint1-heavy-body.blend`.
10. **Bağımsız doğrulama yok:** dd_validator veya bağımsız bir doğrulayıcı koşulmadı. Bu turdaki doğrulamaların hepsi üreticinin kendi betikleriyle yapıldı: `validate_v2.py`, SHA kontrolleri ve görsel inceleme.

## 7. 🔴 C-00'A KARARLAR

- Unity alım ve ölçüm paketi (madde 6.1) için bir WP açılması gerekiyor. Varlık tarafı hazır, motor tarafı ölçülmedi.
- Swollen için daha geniş bir boyun halkası istenirse bu sözleşme değişikliğidir ve bütün fitleri ve LOD'ları etkiler. Önerim şimdilik değiştirmemek ve önce animasyonda gözlemlemek.

## 8. Araç / Higgsfield istekleri

Yok.

## 9. Sonraki önerilen çalışma

1. Unity alımı ve ölçüm WP'si: Avatar, SRP Batcher, sahne bellek bütçesi ve animasyonda kesişme.
2. dd_validator veya bağımsız 3B incelemesi.
3. İsteğe bağlı cila: dikiş çizgilerini yumuşatmak için halka bandında normal/AO harmanı; kulak ve ense için elle düzeltilmiş UV adası.
