# CX-AP025-B RAPOR 1 — 05.10.2026

## ÖZET (≤ 10 satır)

Durum: **KISMİ** — üretim dosyaları ve mekanik kanıt paketi; AAA/oyun-hazır kabulü yok.
Ne üretildi: 3 anatomik yüz × 4 LOD, A/B/C gövde(ana O1 C),3 haritalı replacement atlas,9 adet 1K tek kanal maske, portre/tam boy/tier/40 proxy ve 100 px kanıt.
Teslim: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/exports/v001/`; manifest `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/manifest/AP025-manifest-v003.json`; SHA `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-025/manifest/v003/SHA256SUMS`.
Kalite: son lider kararı `FAIL — yakın plan, okunurluk ve ağır beden kabulü açık; KISMİ paket`; mekanik `PASS (mekanik); genel kabul FAIL/KISMİ`; 73 kemik,32 ortak halka, FBX7400, üçgen ve kan/doku veri kapıları aşağıda.
Bütçe:9 BC4 maske 6.291576 MB; yeni atlas + maskeler 18.874568 MB(mip dahil); 40 zombi gerçek≤96 MB **ölçülmedi**.
Açık: ağır O1/fit C çelişkisi, yakın plan sanat ve headwear uyumu, Unity alım/performans/DEC077 kapıları.
İstek: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/codex/requests/CX-AP025-B-ISTEK-1.md`; C-00 karar maddeleri§6.
Sonraki adım: C-00 görsel/ağır beden kararı; S-03/S-05/S-06 alım ölçümleri.

## 1. Yapılanlar

- Görev güncellemesi ve S-03 alım notu okundu. MPB kaldırılmış sözleşme, fit C,≤74 kemik,9×1K BC4≤12 MB, replacement atlas ve 40 zombi≤96 MB önceliklendirildi; eski ≤40 MB / fit D önerileri kullanılmadı.
- `dd_3d_modeller` mevcut AP007 Higgsfield/Tripo anatomik büstünden üç morfoloji(Gaunt, Swollen, Riven), analitik yara heykeli, bütçe kontrollü decimation retopoloji, UV/bake ve 4 LOD üretti. El retopolojisi iddia edilmez. Göz/diş yüz mesh'inde; saç kütlesi kaynak geometriden.
- `dd_technical_rigger` mevcut 73 kemik isim/parent/rest'ini korudu, A/B/C bedenlerden manken başını çıkardı,32 ortak halkayı kurdu ve mevcut C giysi zarfında güvenli dolgunluk ekledi. Yeni D/tier/kıyafet kaynağı yaratmadı.
- Lider rest-position ten alanını, anatomik bake ve korunmuş aksesuar UV'lerini tek replacement PBR sette birleştirdi; 9 varyasyon maskesi, kan çekirdek HSV ölçümü, shader/bit/dizi ve modül sözleşmesi üretti. `dd_researcher` resmî lisans ve URP/BC4 sözleşme kanıtını hazırladı.
- `dd_blender_specialist` gerçek sevk geometrisinden portre/tam boy/tier/40 kalabalık sahnesini kurdu. İlk görsel kontroldeki uzun boyun/UV gerilmesi üzerine ortak halka 1.58→1.50 m taşındı, baş geometrisi tek z dönüşümü ile yeniden oturtuldu, ortak boyun UV şeridi eklendi. Baş pozu yalnız kanıt sahnesinde düzeltildi; bind kemik değişmedi. Son renderda saptanan üst collar açıklığı ayrıca anatomik baş kesitine uyduruldu ve kapatıldı; bu birleşim alt ortak dikiş testinden ayrı ölçüldü (`validation/head-collar-join-v3.json`).
- Lider kaliteyi kendisi kontrol etti. Ayrı AD/oyun tasarımcısı incelemesi açılmadı; tek `dd_validator` aynı teslimin mekanik denetimini yaptı. Önceki Faz A manifestinin dosyaları korunur; B README ve LICENSES sürümlü yollardadır.

## 2. Üretilen dosyalar

Aşağıdaki yollar paket köküne göredir; tüm 106 kalem ve tam SHA manifestte. Sevk klasörünün SHA dosyası hariç boyutu **21.427 MB**,≤400 MB dosya boyutu kapısı PASS. `.blend`/kanıtlar oyuna sevkedilmez.

| Yol | Tür | Disk boyutu | SHA256 kısa |
|---|---|---:|---|
| `exports/v001/Bodies/Zombie_O1_Body_C.fbx` | .fbx | 1.137 MB | `65b11d8ba769` |
| `exports/v001/Heads/ZombieFace_Gaunt_LOD0.fbx` | .fbx | 1.298 MB | `2bcf6c2cb1e5` |
| `exports/v001/Heads/ZombieFace_Swollen_LOD0.fbx` | .fbx | 1.299 MB | `7cee1eb1afd2` |
| `exports/v001/Heads/ZombieFace_Riven_LOD0.fbx` | .fbx | 1.298 MB | `8dce034d6efb` |
| `exports/v001/Textures/Zombie_O1_BaseColor.png` | .png | 3.814 MB | `2e674cdef6c9` |
| `exports/v001/Textures/Zombie_O1_Normal.png` | .png | 4.144 MB | `3528553769dc` |
| `exports/v001/Textures/Zombie_O1_ORM.png` | .png | 0.840 MB | `84b2c5b4e8f2` |
| `exports/v001/README.md` | .md | 0.009 MB | `58bce70b2c88` |
| `exports/v001/material-contract.json` | .json | 0.008 MB | `d0dcf4c555e6` |
| `exports/v001/module-map.json` | .json | 0.005 MB | `fd054e8583da` |
| `06-renders/v001/hero-portrait.png` | .png | 4.457 MB | `6693f7bf59de` |
| `06-renders/v001/tier-lineup.png` | .png | 4.437 MB | `9209dc3b7bcb` |

- 15 FBX:3 body × 4 LOD mesh+12 tek face LOD; toplam 24 body/face mesh. Dokular 12 PNG:3 PBR + 9 maske. `exports/v001/SHA256SUMS` klasör-içi doğrulama; paket manifest/SHA diğer kanıtları da kapsar.
- Kaynak: `05-blender/body-o1-source.blend`, `head-production-v001.blend`, `render-production-v001.blend`; sculpt/retopo/collar kaynakları baş blend'inde. Yeniden üretim betikleri `04-tools/` ve manifesttedir. Geri dönüş ara dosyaları silinmedi, sevkedilmez.
- Görseller `06-renders/v001/`:portre 2048², tam boy 1440×1800, tier 2560×1440,40 proxy 2048×1536; üç baş+tek aktör 512² kaynaklardan 100² Lanczos türevleri ve contact sheet.

## 3. Kalite ve doğrulama

Mekanik: **PASS; genel kabul FAIL/KISMİ**. Son denetimde paket SHA 107/107, export SHA 31/31, eski Faz A SHA 27/27 eşleşti; 106 manifest kaydı ve 46 lisans satırı doğrulandı. Bağımsız ayrıntı `validation/v003/mechanical-validation.json`; son lider kararı `validation/v003/leader-quality.json`. SHA mekanik sonuç dosyasını içermez(döngüsel hash önlenir); denetçi mühürlenmiş manifest/SHA'yı okur.

| Kapı | Sonuç / kanıt |
|---|---|
| FBX / rig | 7400 binary, metre ölçek,73 kemik; body reimport pass=True, baş FBX/alt dikiş=True; bütün baş kabulü=False |
| Dikiş |32 vertex, aynı A/B/C ve 4 LOD; baş yeniden alım maksimum konum farkı 0.000280 mm; UV 0.00000000; poz farkı 0.000000 mm (float32 LBS) |
| Üst collar | 32 üçgen bağlı kapak; diğer açık kenar 0; LOD0–2 gerçek yüzey bindirmesi en az 4.172 mm. LOD3 anatomik çevresel kapalılık FAIL/LIMITATION; alt dikiş PASS buna genellenmez |
| Materyal / ağırlık | body/face başına 1 M_Zombie_Atlas; body≤4 etki, face≤2; ayrı göz/diş materyali 0; tier başına yeni materyal 0 tasarımı |
| 4 LOD | Baş kaynak üst sınırı 12646 / 5660 / 2360 / 807; FBX yeniden alım Gaunt 12636 / 5644 / 2330 / 774; importer düşürdüğü dejenere yüzler yüzünden bütçede yüksek değer kullanıldı |
| Maske |9 adet 1024²LPNG; hedef BC4; 6,291,576 byte=6.291576 MB≤12; gerçek BC4 Unity içe alımı yapılmadı |
| Atlas |2048²BaseColor+2048²Normal+1024²ORM; 3 harita/1 ten materyali; eski atlası kaldıran replacement sözleşmesi |
| Kan |86489/86489 saf pigment texeli PASS; H356.250–357.966; S0.796875–0.810127; V0.250980–0.447059 |
| 27 varyasyon |3×3×3 indeks ve bit roundtrip; maske sayısı 9; runtime dağılımı, bit buffer aktarımı ve SRPBatcher ölçülmedi |

Giysi BVH temas karşılaştırması `validation/body-clothing-intersections.json`: kaynak 41730 → yeni 41825  triangle intersection; bu penetrasyon derinliği değildir ve sıfır clipping iddiası değildir. C güvenli dolgunluk maksimum 8.932 mm, ortalama 2.823 mm. Mevcut kıyafet temasları ve omuz tier boşluğu raporda açık tutulur. `body-fbx-reimport.json` aşırı poz deformasyon ölçümlerini içerir; oyun animasyon kıyaslaması değildir.

Tam aktör ölçülü kaynak üst sınırları(T8+B6, fit C, H0, orijinal tier):

| Tier | LOD0 /1 /2 /3 | Sınır | Durum |
|---|---|---|---|
| Standard | 22690 / 10045 / 3970 / 1398 | 24000 / 12000 / 5000 / 1500 | PASS |
| Special | 24189 / 10643 / 4166 / 1476 | 28000 / 14000 / 6000 / 2000 | PASS |
| Elite | 23311 / 10666 / 4370 / 1558 | 32000 / 16000 / 7000 / 2500 | PASS |
| Ultra | 24189 / 10644 / 4128 / 1476 | 36000 / 18000 / 8000 / 3000 | PASS |

40 karakter karışımı 20/8/8/4 aynı LOD0..3: **930556 / 413948 / 164200 / 58136** üçgen. Faz A sahne dağılımları yeniden hesaplandı: Cinematic=315450, High=251927, Performance=136252. Fit C için opsiyonel hair/accessory üst sınırı da bütçe içinde. A/B/C genel muhafazakâr hesapta Standard B LOD0 + azami hair/accessory = 24,138; 24.000 sınırını 138 üçgen aşar. Bu birleşim onaylı değildir; mevcut seçim kurallarıyla gerçek modül toplamı alımda sınırlandırılmalı. Kaynak modül caps hesabı bütün kombinasyonların görsel sertifikası değildir. Shadow/depth/crossfade tekrarları dahil değil.

Liderin son görsel bulguları:

- PASS — Ortak boyun halkası z=1.50 m; önceki uzun boyun ve UV şerit gerilmesi düzeltildi. Portrede ve 50° karede üst collar kaynaklı siyah açıklık son geometriyle kapandı; ton/normal geçişi yakın planda hâlâ seçiliyor.
- FAIL / MUST — Yakın portrede saç/ten sınırında testere dişi gibi kenarlar, yüzey parçaları ve sert renk geçişleri görülüyor. Yara kenarı ve diş biçimleri yeterince rafine değil; AAA kahraman kalitesi kabulü verilmedi. Kan rengi veri kapısı bu sanat kusurunu kapatmıyor.
- FAIL / MUST — Gerçek 100×100 px yüz crop karelerinde kan kütlesi ve ince ağız çizgisi seçiliyor; göz kütleleri düşük kontrastlı, Gaunt/Swollen/Riven ayrımı zayıf. Tam aktör 100 px karede yüz ayrıntısı bu baş crop testinden çok daha küçük. Güçlü göz/ağız/yara okunurluğu kabulü tamamlanmadı.
- FAIL / MUST — Ana O1 fit C gövdesi korunmuş dar giysi zarfında kaldı. En fazla8.932 mm dolgunluk belirgin ağır, suyla şişmiş silueti karşılamıyor. Fit C kimliğini koruyarak giysi türevlerini birlikte yeniden fit etme kararı C-00’a bırakıldı; D üretilmedi.
- LIMITATION — LOD0–2 üst collar gerçek baş yüzeyine en az 4.172 mm giriyor ve 32 üçgen kapağı bağlı. LOD3 kaynak anatomik yüzeyinde radyal açıklık var; bütün çevrede kapalı anatomi iddiası yok. Alt ortak 32 halka/FBX PASS ile bütün baş all_pass:false ayrı tutuldu.
- PASS / ÖLÇÜLMEDİ — Standard/Special/Elite/Ultra kaynak parçaları ve Executioner 1.48 ölçeği korunuyor. Tier sırası görünür; Elite omuz parçalarında mevcut ayrıklık görülüyor. DEC-077≥%30 siluet hedefi Unity’de ölçülmedi.
- KANIT — 40 aktör,50° üst kamera ve27 ten/kir/kan indeks önizlemesi üretildi. Blender statik proxy, oyun içi shader veya performans testi değildir; cloth maskesi önizlemesi bu sahnede uygulanmadı. Materyal kopyaları yalnız Blender önizleme sahnesindedir, sevk FBXlerinde tek M_Zombie_Atlas kalır.
- AÇIK — H0–H7/helmet uyumu ve H0≤%25 dağılımı, tüm giysi kombinasyonları, Avatar, renderingLayerMask aktarımı, SRP Batcher,96 MB sahne belleği, draw/SetPass/frame testleri alım sahiplerinde. Kanıtlar bunlara otomatik PASS vermez.

Ölçülmeyen motor kapıları:≤96 MB resident zombie texture,≤14 SetPass,≤450 draw,≤88 frame, DEC077≥%30, Avatar valid/isHuman,200 seed dağılımı, H0≤%25, platform BC4/BC5 ve SRP Batcher. Blender statik proxy bunların yerine geçmez. Görevdeki aile ≤2 atlas / ≤3 harita / ≤4 materyal mevcut cloth/armor/tier ile birlikte S-03 alımında sayılmalıdır; yalnız AP025 PNG sayısından bütün oyun için PASS çıkarılmadı.

## 4. Kaynaklar ve lisanslar

`03-sources/v003/LICENSES.csv` ve eşkopya `exports/v001/LICENSES.csv` teslim dosyalarını/kanıt renderlarını kaydeder. Resmî koşul kanıtı `03-sources/AP025-B-license-evidence.md/.json`, baş provenance `03-sources/head-source-provenance.md`.

Mevcut AP007 Higgsfield işi `c399988f-294b-40bf-b991-f8a6a0943b25` anatomik büst kaynak olarak türetildi; AP007 73 kemikli rig/giysileri ve AP014 tier parçaları kullanıldı. [Higgsfield Terms§4.4](https://higgsfield.ai/terms-of-use-agreement) ticari çıktı kullanımını kısıtlamıyor; MCP kapsamı araştırma belgesinde. Doğrudan Tripo Free/CC0 iddiası yok. Eski AP007 üretim tarihinin bazı ledger alanları eksik; geçmiş hesap özel koşul snapshot'ı yok, mevcut kaynak zinciri ve güncel resmî koşul birlikte saklandı. Yeni indirilen dış sanat 0 byte; yeni başarılı Higgsfield üretimi 0; hesap/kurulum/satınalma yok.

## 5. Entegrasyon notları

- `exports/v001/README.md` dosya başına tam mip MB/MiB tablosu ve `module-map.json` slotları alımın başlangıcıdır. Yeni body+face aynı shared M_Zombie_Atlas; face +1 renderer. Blender boyun 1.50 m ve 32 halka; Unity ekseni importer dönüşümüyle kontrol edilir. Göz/diş Head'e bağlı, jaw/eye bone eklenmedi. Executioner 1.48 uniform.
- Ten atlası **eskinin yerine**; korunmuş aksesuar UV'leri yeni atlas içinde. Eski body/ID/TierMask, otomatik FBX authoring textureları ve gizli repack haritaları yüklenmesin. Doğrudan ORM shader okuması gerekir; standart URP Metallic Gloss slotuna ORM takılmasın.
- MPB 0; tek 9 dilim BC4 array, shared materials,_ZOMBIE_MASKS. renderingLayerMask 16–20 skin/21–25 dirt/26–30 blood,31 reserved, low 16 korunur. Pool tüm renderer/LOD indeksleri; linear base→skin→dirt→blood. Skin/cloth maske yarıları mipte ayrı filtrelenir. Wet ORM.G'den; PBR haritası sayısı 3.
- Yeni 18.874568 MB payload, yaklaşık 27 MB eski set çıkarımı varsayımıyla 68.874568–79.874568 MB sahne tahmini; birimler MB ise. Eski atlas bırakılırsa 95.874568–106.874568 MB ve en kötü durumda FAIL. Bu hesap Profiler ölçümü değildir.
- Mevcut AP026 varsayılan 77 kemik şeması görevin 73 kemik koruma sınırıyla çelişir; açık 73 kemik kuralı önceliklendirildi. Eski iskeletten bind korunması ölçüldü; Unity Avatar uyumu otomatik PASS değildir.

## 6. C-00'A SORULAR / 🔴 KARARLAR

1. **Ağır beden/fit C:** mevcut C giysi oranında sınırlı dolgunluğu kabul / aynı C kimliğinde beden + giysi türevlerini birlikte ağırlaştıracak sonraki üretim. Öneri ikinci yol; yeni fit D yok. Bekleyen: belirgin ağır O1 torso kabulü.
2. **Yakın plan/headwear:** son portre ve 100 px kanıt için sanat kararı; mevcut kaynağın saç/ten sınırını kabul / yeni temiz anatomik baş ve saç ayrımıyla ayrıca üretim. Öneri son görsel bulgudaki MUST'ları sonraki görevde kapatmak; tam AAA kabulü verilmesin. H0–H7/helmet kombinasyonları alımda ölçülsün.
3. **Aile kapsamı:** korunmuş AP014 tier/AP008 armor ayrı aile kabulü / bütün zombiyi tek ≤2 atlas ailesine yeniden atlaslama. Öneri S-03 mevcut aile tanımını açık ölçümlesin; AP025 tek ten + cloth iki atlas şemasına ek resident body atlas konmasın. Bekleyen: bütün aktör Z3 sayımı.

Araç/ödeme için yeni 🔴 yok. Bu maddeler nedeniyle kullanıcıya doğrudan proje kararı sorulmadı; somut paket C-00'a aktarılıyor. Alım isteği `c-00/codex/requests/CX-AP025-B-ISTEK-1.md`.

## 7. Araç / Higgsfield istekleri

Blender 5.2.0 LTS, Cycles/Metal, Python+NumPy+Pillow, mevcut kabuk/FBX araçları. Tüm Blender işleri slot.sh ile, disk ≥15 GB ve swap sınırı kontrollü; son render 109.7 s. Repo/main/Unity/Player'a yazma/çalıştırma yok. Kurulum talebi yok. Yeni Tripo üretim denemesi mevcut MCP'nin 3B çağrı eksikliğine takıldı; başarılı üretim/yeni kredi tüketimi olarak sayılmadı, mevcut lisanslı kaynak türetildi.

## 8. Sonraki önerilen çalışma

C-00 önce kısmi sanat/ağır beden kararını versin; S-03/S-05/S-06 manifesti doğrulayarak yukarıdaki ölçülmemiş alım kapılarını test etsin. AAA ve oyun-hazır onayı ancak açık görsel MUST'lar ve motor ölçümleri kapandıktan sonra verilsin. Bu turda üretilen varlıklar, kaynaklar, kanıtlar ve açıklar tamamı kaydedildi; ek üretim veya keşif başlatılmadı.
