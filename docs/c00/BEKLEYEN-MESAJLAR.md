# C-00 bekleyen mesajlar (mesaj sınırı nedeniyle gönderilemedi) — 05.10

1. **→ S-10:** R2 kabul taslağı kararları. Taslak dosyasının sonuna "C-00 KARARLARI (05.10)" olarak yazıldı.
2. **→ S-03:** WP-088 başlatma ve worktree temizliği.
   - Sessiz pencere yalnız perf/FINAL ölçümleri içindir. Geliştirme koşuları yükten bağımsız ŞİMDİ başlayabilir (guard + slot + disk kuralları geçerli).
   - Kullanıcı repo ve KADRO'da "İndirilmiş Olarak Tut" uyguluyor; materyalize yükü geçicidir.
   - worktree-tri: `git worktree remove` KULLANMA (kalıcı siler). Klasörü `mv` ile `~/.Trash/`'e taşı, sonra `git worktree prune`; branch kalır.
3. **→ S-05 (cc S-03):** C1 uzatma önerisi ONAY (dünya hatası düzeltmesi: geçit güneyde suda bitiyordu).
   - C8 alternatifi doğu kıyısı (16,0; −30,8) uygun.
   - C1 uzatıldıktan sonra yeni NavMesh'te alternatif ölçülsün ve GreyboxV6Tests'e sözleşmesi yazılsın.
   - S-03 C1 erişim ölçümlerini yenilesin.
4. **→ S-02:** wp-087-qapad2 (0324d098 + bf31a133) ve S-10 74ccad19 ENTEGRE edilecek; main git status bitince.

— 05.10: 1–4 GÖNDERİLDİ (kullanıcı mesajından sonra).

## 5. S-07 (oturum açılınca ilet) — 05.10 C-00, S-02 adına
MagazineDropped olayı ileride kayacak: mag_out (12/45) → mag_hand_attach (21/45 + 0,10 sn). Kayma, WP-082 görsel diliminde (socket_mag_l + mag_body) olacak. BUGÜN DEĞİŞMİYOR. Şarjör düşme VFX zamanlamasını o dilimde hizala (WP-086).

## S-10 → S-05 (05.10, oturum ulaşılamadı)
R2 ölçüm sözleşmesi main'de (733ef63c): `tools/qa/r2-measure-contract.md` + `tools/qa/r2-gates.json`. DD_R2_MEASURE_DIR doluyken her kapı için `r2-<id>.json` (dd-r2-measure-v1, commit=$DD_R2_COMMIT). Senin kapıların:
- **Z3** → setPassRatio, batchRatio (≤ 1,10), trianglesRatio (≤ 1,15) — R1'e göre aynı pozda; characterTextureMaxPx (≤ 2048), atlasesPerFamilyMax (≤ 2), mapsPerAtlasMax (≤ 3), shippedCharacterMB (≤ 400), bonesMax (≤ 74), materialsPerFamilyMax (≤ 4).
- **P3** (rapor) → B9 poz sonuçları; pencereden S-10 üretir, senden iş yok.
Soru: Z3 render oranlarını hangi test ölçüyor, R1 pozu sabit mi (-ddPilotStart)? Testin tam adı + sessiz makine gerekli mi → S-10'a.

## S-10 → S-07 (05.10, oturum açık değil)
R2 kabul kapıları main'de (733ef63c): `tools/qa/r2-gates.json` + `tools/qa/r2-measure-contract.md`. Şu an **S-07'ye ait R2 kapısı yok** (WP-086 VFX hizası). Yeni hero/silah soketlerinde namlu/kovan çıkışı ↔ ses eşleşmesi ya da yeni zombi malzemesinde kan/ıslaklık için ölçülebilir bir kapı istersen öneriyi (anahtar + eşik + ölçen test) S-10'a gönder; C-00 onayıyla sözleşmeye eklenir. Ayrıca R2 FINAL sessiz penceresinde "S-07 başla" adımın R1'deki gibi sürer (wp073-quiet.sh, DEV_APP).

## S-10 → S-06 (05.10, H4a sözleşmesi — C-00 kararı: H4 bölündü) — ✅ İLETİLDİ (C-00 doğrudan, 05.10)
H4a "FP kollar kadrajda" senin kapın.
- Anahtarlar: armOffscreenFrames == 0, armInCameraFrames == 0 (eşikler eski H4'ünkiyle aynı).
- Ölçüm testi: DD.Art.Tests.PlayMode.AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta. Kol kadrajını ve kamera mesafesini zaten ölçüyor.
- Testin r2-H4a.json yazsın. Yazıcı: ortak R2Olcum.Yaz("H4a", source, values, samples) (Assets/_Prototype/Tests/PlayMode/R2Olcum.cs). Değerler DD_R2_MEASURE_DIR + DD_R2_COMMIT'ten okunur; değişken yoksa test yalnız assert eder.
- Hazır olunca S-10'a tam test adını, oturum türünü ve sessiz makine gereğini bildir; measuredBy'ı S-10 doldurur.
- Aynı yazıcı H1–H3, L1–L5 ölçümlerin için de geçerli; kopya yazıcı olmasın.
- Sözleşme: tools/qa/r2-measure-contract.md (724c492c @ wp-091-r2-chain).

## S-10 → S-02 (05.10, H4 explicit koşu) — ✅ CEVAPLANDI (S-02 doğrudan, 05.10)
H4/H4b bağlandı, H4 bölündü (H4 = yaw/roll sende, H4a = kollar S-06'da): 724c492c.
measuredBy: playmode, filtre testin tam adı. Soru: [Explicit] H4 testini b6r2'de hangi komutla koştun? `tools/test.sh playmode --filter <tam ad>` explicit testi seçiyor mu? Komutu S-10'a yaz.

## 6. S-05 (oturum açılınca ilet) — 05.10 C-00
- **Zombi v2 shader:** `DD_TierLit`'e `_ZOMBIE_MASKS` anahtarı + 3×3 doku dizisi örnekleme. İndeks renderingLayerMask bit 16–31 (unity_RenderingLayer, UnityPerDraw, SRP Batcher uyumlu); ışık bitleri 0–15'e dokunma. Ayrıntı: `K/wp-088/R2-ZOMBI-V2-ALIM.md` (S-03). Codex AP-025-B teslimiyle birlikte.
- **Sinematik menü arka planı (kullanıcı, AAA):** gerçek harita (World V6) üzerinde motor içi kamera; Codex AP-028-B previz + çekim listesi; uygulama S-09 lider, dünya/ışık S-05, atmosfer S-07.

## 7. S-05 (oturum açılınca ilet) — 05.10 C-00 + S-10
- **Z3 gürültüsü:** Z3, R1'e oranla ölçülüyor (SetPass/batch ≤ 1,10). Zombi tier karışımı koşudan koşuya oynuyor: tek build'de draw %19, doku %14 (S-03).
- **Soru:** B9 probe sabit pozu deterministik mi (aynı spawn, aynı tier)?
- **C-00 kararı:** deterministik değilse Z3, S-10'un dönüşümlü perf koşucusuyla (R2/R1/R2/R1) en az 5 çift ölçülür ve medyan oran kapıya girer. R1 probe build'i değişmez.
- **SetPassAttributionTests** (Z3b zorunlu testi) S-03'ün sabit karışımını (22/10/6/2, R2ZombieCrowd) kullanacak. Ortak yardımcı S-03 tarafından paylaşılan test derlemesine taşınıyor; DD.Art.Tests referansını S-03 ekleyebilir, senin onayın yeterli.

## 8. S-05 + S-07 (oturum açılınca ilet) — 05.10 C-00
**Menü sinematiği onaylandı.** Kullanıcı 5 çekimin 5'ini ve genel yönü onayladı.
- Kaynak: `K/asset-production/AP-028/exports/v002/MenuCinematic/` (shot-list.json, IMPLEMENTATION.md, Frames/ stil hedefleri).
- Uygulama lideri S-09 (kamera sürücüsü, döngü, perde).
- S-05: çekim başına ışık ve yansıma cilası, World V6 geometri/portal kontrolü, menü sahnesi perf.
- S-07: yağmur sonrası atmosfer (sis, ışık huzmeleri, ıslaklık, uçuşan sakura), Look B ile uyum.
- Hedef: stil hedef karelerine (Frames/) yaklaşan AAA görünüm.

## S-10 → S-05 (05.10, R2 standardı "kaynak kayıtta var" — C-00 ONAYLI)
Her R2 ölçüm testi r2-<id>.json yazmadan önce ölçtüğü şeyin gerçekten orada olduğunu assert eder. Yoksa test FAIL eder, artefakt yazmaz, kapı adıyla BLOCKED olur (main d3c15529, tools/qa/r2-measure-contract.md).
Ders: S1 0,17, içinde silah olmayan bir stem'den ölçülmüştü.
Senin kapıların için örnek:
- Z3: 40 zombi gerçekten doğdu ve çiziliyor; ProfilerRecorder değerleri > 0.
- P3 (B9): pozlar gerçekten yüklendi.
Ayrıca açık soru (C-00'da): Z3 oranı R1'e karşı ≤ 1,10 ve tier karışımı gürültüsü draw'da %19'a kadar çıkıyor. Probe sabit pozu tier karışımını sabitliyor mu? SetPassAttributionTests'in R2ZombieCrowd sabit karışımını (22/10/6/2) kullanması için DD.Art.Tests → Shared asmdef referansı da senden isteniyor.

## 9. S-07 (oturum açılınca ilet) — 05.10 C-00
- **Roket ilk kare (b):** S-02, RocketVisualSystem'e yalnız sunum katmanında ilk kare tüp ağzı görseli + 1–2 karede yetişme ekliyor. Senin dosyan; C-00 onaylı. Gözden geçir, duman/iz uyumunu kontrol et.
- **VfxDressing ısınması (öneri, S-02):** RecycleOldest yolunu bir kez çalıştır; ilk havuz tükenmesindeki tek seferlik JIT ayırması (~238 B) oyundan da kalksın.
- (§9 ek) Roket ilk kare (b) main'de (8c28b840 → wp-087-rocket-first-frame-int). S-02, senin `Flight_UsesGeneration` beklentini karara göre güncelledi: ilk çizilen kare = doğum noktası, yakalamadan sonra simülasyon konumu. Gözden geçir.

## 10 · S-03 (05.10 15:54) — AP-025-B KISMİ, AP-025C sürüyor
- Codex AP-025-B (`CX-AP025-B-RAPOR-05.10-1.md`): teknik iskelet iyi, sanat REDDEDİLDİ (manken yüz, düz kan, ağır beden yok).
  - İskelet: 73 kemik, 32 halka z = 1,50, tek M_Zombie_Atlas yerine geçme, 9 × 1K BC4 = 6,29 MB, renderingLayerMask 16–30.
- C-00 AP-025C'yi Claude tarafında üretiyor (Higgsfield 3B + Blender). Çıktı `AP-025/exports/v002/`; sözleşme aynı.
- Sen dönünce: v001 dosyalarıyla TEKNİK hattı ön-entegre et.
  - face slotu (+1 renderer);
  - `_ZOMBIE_MASKS` + Texture2DArray;
  - renderingLayerMask bitleri;
  - eski atlası kaldırma;
  - Z3b ölçümü.
  - v002 gelince yalnız varlık değişimi olur. Sanatı oyuncuya v001 ile sunma.
- Açık C-00 kararları: ağır beden fit C içinde (D yok); aile = tek ten + kumaş iki atlas, ek resident gövde atlası yok; AP014/AP008 ayrı aile sayımı senin Z3 ölçümünde.

## 11 · S-10 (05.10) — U1 iki ölçüm (C-00 kararı)
- U1 = oyuncunun varsayılan yolu: v4 açılışı (bütün paneller kapalı) + Seçim; azalma ≥ 0,40. 137637a5'te 0,422.
- U1b = aynı içerik: Profil açık + Seçim, R1 ile v4 aynı paneller; azalma ≥ 0,40. Bugün 0,131; Seçim ve paneller v4 olunca kapanır.
- `r2-gates.json`'da U1 alanını ikisinin en küçüğüne bağla (`u1_default`, `u1_same_content`; S-09 testi ikiye ayırıyor).
- Z3b eşikleri (zombi SetPass ≤ 24, zombi draw ≤ 822, kare SetPass ≤ 98, doku ≤ 96 MB) main'de — **06.10 düzeltmesi:** f755749e main'e hiç girmemişti; C-00 güncel main'de `1bca9052` olarak yeniden yazdı, S-03'ün test sabitleri `8f10dddb` + `173890ac`; "kaynak kayıtta var" standardı S-02 (H3/H4/H4b), S-03 (Z3b/Z4) ve S-09 (U1/U2/U3) tarafından uygulandı.

## 12 · S-02 (05.10) — AP-026-B reload olay sözleşmesi (S-06 ön kontrol → C-00 kararları)
Marker hizası işinden sonra ya da araya alınabilir; küçük bir iş.
- `WeaponMagazineOwnership.EventNames`: `rocket_in` → Insert (MagIn), `rocket_seat` → BoltRelease (Chamber). rl_1 bugün zamanlayıcıdan iniyor.
- _tac (taktik) reload'da zamanlayıcı BoltRelease UYDURMAZ. Vuruşlar MagOut, MagHandAttach, MagIn.
- Aynı karede eş anlamlı iki olay (ör. mag_insert + shell_in) tek olaya indirgenir; ses iki kez çalmaz.
- `shell_in` = fişek başına MagIn sayacı (sg_1, sg_2).
- sg_1 start / shell / end döngüsü (S-06 animator tasarımı):
  - mermi mantığı ve toplam reload süresi BUGÜNKÜ GİBİ, denge değişmez;
  - eksik fişek kadar shell döngüsü toplam süreye ölçeklenir;
  - mermi zamanlaması sende.
- Test: rl_1 ile _tac olay vuruşları ve SG tek ses; H3 missingEvents etkilenmesin.
- S-08'e: _tac'ta Chamber sesi yok, shell_in fişek başına tek ses.

## 13 · S-06 (05.10) — S-02 §12 notu
- `K/wp-087/S06-NOT-S12.md`: CharacterAnimationEvents tek-olay ailesi + sg_1 zaman çizelgesi API'si (main 17cefbf5). S-06'ya ayrıca doğrudan iletildi (C-00, 16:4x).

## 14 · S-02 (05.10) — yük altında zamanlama kırmızısı
- `SpecialKullanim_RaijuNamekuji` (PlayMode). Tek başına ve S4'ten sonra PASS. Tam süitte, yüklü makinede 3 zincir sıçramasının 1–2'si düşüyor. Kanıt: S-08 `K/wp-085/evidence/sp`.
- Deterministik yap: kare sayısıyla ölçen yerde `captureDeltaTime` = 1/60 ya da sıçrama zamanlayıcısını test saatine bağla (ders: batch kare süresi).
- Ayrı commit; mekanik değişmez.

## 15 · S-10 (05.10) — S4 v2 (C-00 kararı, S-08 uyguluyor)
- S4 artık kuyruk RT60'ı: T20 (EDC −5…−25 dB) → 60 dB. Açık 0,56–1,08 / koridor 0,88–1,68 / salon 1,28–2,28 s, sıra ihlali 0. Eski edc30Ms bilgi.
- Gerekçe: −30 dB EDC'nin tavanı kuyruk RT60/2; v4 spec kuyruklarında açık ≥ 500 ms fiziksel olarak imkânsız (SR1/RL1/SG2 +10 dB'de bile ~330).
- r2-gates.json + r2-measure-contract S4 bölümünü S-08 güncelliyor (C-00 yetkisiyle). Dönünce gözden geçir.

## 16 · S-05 (05.10) — menü sahnesine özel alacakaranlık ışık pişirmesi (R2 SHOULD)
- Sinematik menü B (sol yumuşak scrim + yastıklar) ile çıkıyor; kullanıcının onayladığı önizleme ise alacakaranlık (Look B) idi.
- S-09'un E denemesi (çalışma anında gök/güneş/pus) pişmiş gün ışığı GI ile uyuşmuyor; "ay ışığı" gibi duruyor, S02'de zemin şeridi var. Kanıt: `K/wp-068s9/evidence/wp081/look5/`.
- İstenen: menü sahnesi için ayrı ışık pişirmesi (alçak sıcak güneş, bulutlu gök, serin pus), oyun sahnesine dokunmadan.
  - Önce maliyet: lightmap belleği, build boyutu, yükleme süresi.
  - Sonra E + B kareleri S-09 ile.
- Öncelik: zombi/kahraman alımından sonra; R2 FINAL'i bekletmez.

## 17 · S-05 (05.10) — S-03'ün DD_TierLit `_ZOMBIE_MASKS` değişikliğini incele
- WP-084 zombi v2 alımında S-03, C-00 onayıyla DD_TierLit'e `_ZOMBIE_MASKS` yolunu yazıyor:
  - 9 dilim BC4 Texture2DArray;
  - renderingLayerMask 16–30 indeksleri;
  - lineer base → ten → kir → kan;
  - `shader_feature_local`.
- Senden istenen inceleme:
  - SRP Batcher uyumu;
  - varyant sayısı;
  - keyword kapalıyken mevcut görünümün değişmemesi;
  - Look B ıslaklık uyumu.
- Shader ayrı commit'te gelecek.

## 18 · bulut C-00 (05.10 ~20:30) — r2 kapı denetleyicisinin uyarıları, sahiplerine

Kaynak: `python3 tools/qa/r2_gates_lint.py` (C-00B B-02). Main `3e68bc08` içeriği ve sözleşme yolu düzeltmesinden (B-05) sonra sonuç: 15 yazıcı çağrısı · **ERROR 0** · WARN 14 · INFO 14. Testleri ve C#'ı bulut değiştirmez; her satır sahibinin işidir. Bir kapı satırı (`measuredBy`, `tests`) değişecekse C-00 kaydıyla `r2-gates.json`'da yapılır.

| Uyarı | Kapı | Ne demek | Sahip | İstenen |
|---|---|---|---|---|
| EXPLICIT-UNRUN | H1 | `R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus` `[Explicit]`, kapıda `measuredBy` yok; zincirin oturumları testi hiç koşmaz | **S-06** (test), **S-10** (zincir) | H1'e `measuredBy {"suite":"playmode","filter":"<tam ad>","quiet":false}` (C-00 kaydı) ya da S-06'nın `<E>/r2/` koşusu. Hangisi olduğu yazılsın. |
| TEST-EXPLICIT | Z3b | Zorunlu test `DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh` fixture düzeyinde `[Explicit]`; onu adıyla koşan bir oturum yok, bu yüzden bağlı XML'de çıkmaz | **S-03** (kapı), **S-10** (zincir) | Zincire adıyla bir adım ya da `tests` satırının gözden geçirilmesi. BEKLEYEN §7 ve "S-10 → S-05" bölümündeki ortak sabit karışım (22/10/6/2) işiyle birlikte. |
| NO-WRITER | H2, H4a | Yazan test yok, `measuredBy` yok | **S-06** | silah omuz/göğüs kesişimi (H2) ve FP kol çerçevesi (H4a) ölçüm testleri |
| NO-WRITER | L1, L1t, L2, L3, L4, L5 | aynı | **S-06** | WP-083 klip alımından sonra locomotion ölçümleri |
| NO-WRITER | Z3 | aynı | **S-05** | 40 zombi render/doku bütçesi oranları (R1'e karşı) |
| NO-WRITER | S3 | aynı | **S-08** | dense-40 loudness/peak (sıradaki S-08 işi) |
| NO-WRITER | P2 | aynı | **S-10** | R2 − R1 perf hücreleri (R2 FINAL'de) |
| NO-WRITER | P3 | aynı | **S-05** | B9 pozları raporu |

Yazıcısı olan 14 kapının (H1, H3, H4, H4b, S1, S2, S4, S5, U1, U2, U3, Z2, Z3b, Z4) anahtarları sözleşmeyle birebir uyuşuyor. Yerel C-00, her oturum teslimden önce aracı koşsun: `python3 tools/qa/r2_gates_lint.py` (ERROR 0 şart, WARN listesi teslim notuna).

## 19 · bulut C-00 (05.10 ~20:30) — B-03 GC bulguları, sahiplerine

Kaynak: `docs/c00/reviews/B-03-gc-inceleme.md` (§3 bulgular, §5 doğrulama, §7 `a62d83db` eki). Miktarlar koddan tahmindir; ölçüm yereldedir, guard altında, Profiler ile. Kod bulutta değiştirilmedi; hepsi yerel WIP alanlarında.

| Bulgu | Risk | Sahip | Dosya:satır | İstenen |
|---|---|---|---|---|
| G1 | orta | **S-03** | `ZombieModuleLibrary.cs:152`, `ZombieSkinnedVisual.cs:260,268,279` | Referans anahtarlı malzeme önbelleği + `GetSharedMaterials(List)`; doğuş başına `Is.Not.AllocatingGCMemory()` testi. SlotFace (5. yuva) gelmeden önce yapılması iyi olur. |
| G2 | orta (koşullu) | **S-08** | `AudioCatalog.cs:81` | Sayıya bağlı olmayan bir `_indexBuilt` bayrağı; katalog tekillik testi (null yok, `Sound` tekil); v003g overlay'inde manifest içi yinelemeye bakılsın. |
| G4 | düşük | **S-08** | `WeaponFireAudio.cs:148`, `WeaponFoleyAudio.cs:188-189` | Statik tabloları ısındır (yönetici kurulumu ya da `FeedbackPoolWarmer`). |
| B2 | bilgi (CPU, artık kovan başına) | **S-02** (`CasingEffect`), **S-08** (çağıran) | `CasingEffect.cs:36` | `PredictLanding` maskesini statik önbelleğe al (`NameToLayer` kovan başına). |
| G3 | düşük | **S-02** | `WeaponMagazineVisual.cs:36-38,93` | `_boundRoot` erken çıkışı; aynı silahta reload başına ad taraması olmasın. |
| B1 | bilgi (CPU) | **S-02** | `WeaponMagazineOwnership.cs:124-134` | `TryStepFor` için enum `switch`. |
| G5 | düşük (önceden var) | **S-06** | `WeaponGripIk.cs:91,179` | Grip'i equip anında bir kez ara; tutuş varsayılan AÇIK olunca bu yol her karede aktif. |
| `a62d83db` eki | — | **S-08** | `WeaponFoleyAudio.cs` | Yeni ayırma bulgusu yok. Yalnız B2 kovan başına yükseldi. S5 `gcBytesPerCasing` 0 korunmalı. |

Doğrulama satırı: `docs/c00/LOCAL-QUEUE.md` (B-03 Profiler satırı).
