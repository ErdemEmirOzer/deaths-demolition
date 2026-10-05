# B-03 — Sıcak yol ayırma (GC) incelemesi, 05.10 C# değişiklikleri

**Hazırlayan:** C-00B (ikinci ekip, bulut) · **Tarih:** 05.10.2026 · **Tür:** salt okuma. Kod değiştirilmedi.

**Kapsam:**
- main'e 05.10'da giren bütün runtime C# değişiklikleri.
- Aralık: `84941661..0b3ec358` (05.10'un ilk commit'i `0324d098`'in ebeveyni → main). 104 commit. **B-05 düzeltmesi:** taranan fark `a62d83db`'yi de içeriyordu; `0b3ec358..3e68bc08` eki §7'de.
- `Assets/_Prototype` altında, `Tests/` ve `Editor/` hariç 51 dosya, +9 979 / −633 satır.
- `_Art/Runtime` altında 05.10 değişikliği yok.

**Doğrulama:** yerelde, Unity Profiler ile, ilgili oturum sahipleri yapar (§5 ve `docs/c00/LOCAL-QUEUE.md`). Bu belgedeki bütün ayırma miktarları **kod okumasından hesaplanmış tahmindir, ölçüm değildir**.

**Kural tabanı:**
- Proje sistem promptu §11: sıcak yolda 0 ayırma; string, format, LINQ, closure, boxing ve `params` yok.
- Ayırma testi `Is.Not.AllocatingGCMemory()` ya da ProfilerRecorder "GC Allocated In Frame" ile yapılır.

## 1. Yöntem

1. Eklenen satırlar `git diff -U0` ile çıkarıldı ve şu kalıplarla tarandı:
   - `new` (değer tipleri hariç), string birleştirme, `$"…"`, `string.Format/Join`, `ToString`;
   - LINQ, lambda / `=>`, `foreach`, `GetComponent*`, `Find`, `Transform.name`;
   - `Debug.Log`, `Enum.*`, `Resources.Load`, `AddComponent`.
   - Sonuç: 152 aday satır.
2. Her aday satırın **hangi sıklıkta** çalıştığı çağıranlar izlenerek belirlendi: kare başına / atış ya da olay başına / doğuşta / bir kez. Yalnız kare, atış, doğuş ve reload yolundakiler bulgu sayıldı.
3. Bugünkü değişikliğin **çağrı sayısını artırdığı** eski kod da incelendi; ilgili yerde "önceden var" diye işaretlendi.

## 2. Özet

| # | Risk | Yol | Dosya:satır | Kısaca | Getiren commit | Sahip |
|---|---|---|---|---|---|---|
| G1 | **Orta** | zombi doğuşu (refill: 0,45 s'de ≤ 2 gövde) | `Characters/ZombieModuleLibrary.cs:152`, `Characters/ZombieSkinnedVisual.cs:260,268,279` | Her `MaterialsFor` çağrısı önbellek isabetinde bile anahtar string'i kuruyor (`string.Join` + 4 birleştirme). Her yuva için `Renderer.sharedMaterials` dizi kopyası alınıyor. WP-088 sonrası 16 renderer'ın hepsi bu yoldan geçiyor; doğuş başına en çok 3 kez çağrılıyor. | `e612d12b` (WP-088) | S-03 |
| G2 | **Orta (koşullu, gizli)** | her atış | `Audio/Runtime/AudioCatalog.cs:81` (çağıran: `WeaponFireAudio.cs:357,429,430,437,441,446,455,461`) | `TryGet` indeksi `_index.Count != _entries.Length` olunca yeniden kuruyor. Katalogda tek bir null ya da yinelenen giriş varsa her `IsProduced` / `LufsOf` çağrısı yeni bir `Dictionary` ayırır. v4 katmanlarıyla atış başına çağrı sayısı 2'den 6–9'a çıktı. | kod önceden var; çağrı artışı `fcaa9d29`, `3f74e3f4` (WP-085 B3/B5) | S-08 |
| G3 | Düşük | her reload başlangıcı (şarjörlü silahlar) | `Weapons/WeaponMagazineVisual.cs:36-38,93`, çağıran `Weapons/WeaponController.cs:716` | `Bind` aynı silah rig'i için bile her reload'da özyinelemeli `Find` yapıyor. Her düğümde `Transform.name` yeni bir managed string döndürür. | `97cb1b00` (WP-082 visual) | S-02 |
| G4 | Düşük | ilk atış / ilk kovan (tek sefer) | `Audio/Runtime/WeaponFireAudio.cs:148`, `Audio/Runtime/WeaponFoleyAudio.cs:188-189` | Statik tablolar ilk erişimde oyun içinde kuruluyor: 60 + 100 `Enum.TryParse` ve string birleştirme, ayrıca `AudioSoundId` ad önbelleği. İlk atış karesinde tek seferlik GC ve CPU sıçraması. | `fcaa9d29`, `b2a2a13e` (WP-085) | S-08 |
| G5 | Düşük (önceden var) | grip bulunamadığı sürece 0,25 s'de bir | `Characters/WeaponGripIk.cs:91,179` | Grip yoksa `FindGrip` / `FindDeep` her düğümde `t.name` okuyor. Bugün eklenen `WeaponHoldIk` destek noktasını bu bileşenden aldığı için listelendi. | WP-071 (05.10 değil) | S-06 |
| B1 | Bilgi (CPU, ayırma yok) | reload sürerken her animasyon olayı | `Weapons/WeaponMagazineOwnership.cs:124-134` | `TryStepFor` olayı adla eşliyor: en çok 9 string karşılaştırması. Bir `switch (CharacterAnimationEvent)` hem daha ucuz hem tip güvenli olur. | `6bf83707` | S-02 |
| B2 | Bilgi (CPU) | kovan başına (tüketici S-08 dalında) | `Feedback/Vfx/CasingEffect.cs:36` | `PredictLanding` her çağrıda `LayerMask.NameToLayer("Environment")` ile sorgu yapıyor. Örnek alanı `_mask` (satır 71) zaten önbellekli; statik sürüm de önbelleğe alınabilir. | `cc5e6246` | S-02 / S-08 |

Temiz bulunan sıcak yolların listesi §4'te.

## 3. Bulgular

### G1 — Zombi malzeme anahtarı ve `sharedMaterials` kopyası (orta)

**Nerede:**

```csharp
// Characters/ZombieModuleLibrary.cs:152 — önbellek İSABETİNDE de çalışır
string key = string.Join("|", materialNames) + "#" + tierIndex + "#" + (atlasOverride != null ? atlasOverride.GetInstanceID() : 0);

// Characters/ZombieSkinnedVisual.cs:279 — SameMaterials, yuva başına
Material[] current = r.sharedMaterials;   // Unity her okumada yeni dizi döndürür
```

**Neden şimdi:**
- R1'de (`84941661`) tek malzemeli yuvalar `r.sharedMaterial` ile, ayırmasız atanıyordu. Yalnız iki malzemeli giysiler `MaterialsFor` + `SameMaterials`'tan geçiyordu.
- WP-088 (`e612d12b`) havuzdaki eski ikinci malzemeyi temizlemek için **her** yuvayı `MaterialsFor(one, …)` + `SameMaterials`'tan geçiriyor (satır 268). Bu doğru bir düzeltme, ama ayırmayı da her yuvaya yaydı.
- `ApplyTierTint` artık `ApplyMaterial(…, body)` çağırıyor (`ZombieAppearance.cs:483`). Sonuç olarak tek doğuşta `ApplyMaterial` en çok 3 kez çalışıyor:
  - `ZombieSkinnedVisual.cs:209`: yeniden kurulan görünüm;
  - `ZombieAppearance.cs:194`: sürüm;
  - `ZombieAppearance.cs:483`: tier tonu.
- Her çağrı 4 LOD × 4 yuva = 16 renderer işliyor. S-03'ün WP-084 `SlotFace`'i 5. yuvayı ekleyince bu sayı 20 olacak.

**Tahmin (ölçüm değil):**
- Doğuş başına yaklaşık 48 anahtar string'i (`string.Join` ara dizisi dahil, her biri ~100–250 B) ve 48 `Material[]` kopyası. Toplam doğuş başına **~8–15 KB**.
- Refill hızında (≤ 4,4 doğuş/s) **~35–65 KB/s** eder.
- Kare başına değil, ama 40 zombilik dalgada sürekli tekrar ediyor. R1'deki "ilk ısınmadan sonra doğuşta ayırma yok" belgesi (`ZombieModuleLibrary.cs:144`) artık doğru değil.

**Öneri (S-03):**
1. Anahtarı string olmaktan çıkar. `materialNames` dizileri ya serileştirilmiş varlık dizileri ya da statik `ClothOnly` / `AtlasOnly`, yani başvuruları sabit. Önbelleği `(string[] başvurusu, tierIndex, atlasOverride başvurusu)` üçlüsüyle anahtarla:
   - bir `struct` anahtar ve `ReferenceEquals` kullanan bir `IEqualityComparer`;
   - ya da `Dictionary<string[], Material[]>[tier]` ile `ReferenceEqualityComparer`.
   - Eşit içerikli farklı dizi gelirse yalnız bir önbellek girdisi fazla oluşur; doğruluk bozulmaz.
2. `SameMaterials` içinde `r.sharedMaterials` yerine `r.GetSharedMaterials(List<Material>)` (Unity 2022.3) ile tek, yeniden kullanılan bir statik liste kullan.
3. EditMode/PlayMode ayırma testi: ısınmış havuzla `EnemyFactory.Spawn` + `ApplyVersion` + `ApplyTierTint` için `Is.Not.AllocatingGCMemory()` (pozitif kontrol: 64 KB yem). Z3b sahnesi bunun için uygun.

### G2 — `AudioCatalog.TryGet` indeksinin koşullu yeniden kurulması (orta, gizli)

**Nerede:** `Audio/Runtime/AudioCatalog.cs:81`

```csharp
if (_index == null || _index.Count != _entries.Length) { _index = new Dictionary<AudioSoundId, Entry>(_entries.Length); … }
```

- Döngü null girdileri ve yinelenen `Sound`'ları atlıyor. Katalogda tek bir null ya da yinelenen giriş varsa `_index.Count` hiçbir zaman `_entries.Length`'e eşit olmaz. O durumda **her** `TryGet` çağrısı katalog boyunda yeni bir `Dictionary` kurar.
- Bu kod 05.10'dan önce de vardı. Ama bugün atış yolu katalogu çok daha sık sorguluyor: `TryHandle` (357), `PlayVoice` (429, 430, 437, 441, 446 ×2) ve `PlayTail` (455, 461). Tek atışta 6–9 `IsProduced` / `LufsOf` çağrısı oluyor.
- `AudioCatalogBuilder` v4 ile v016 manifestleri arasında yineleneni ayıklıyor (`v4Sounds` HashSet), ama **v4 manifestinin kendi içinde** aynı ses iki kez geçerse ayıklamıyor. Bu yüzden durum, AP-027 overlay'lerinde (v003g gibi) bir manifest hatasıyla ortaya çıkabilir.

**Etki (koşul gerçekleşirse):** atış başına 6–9 sözlük kurulumu (yüzlerce girdi, onlarca KB). 900 RPM'de MB/s düzeyinde GC ve kare takılması. Bugün gerçekleştiğine dair kanıt yok. S2 kiti `gcBytesPerShot` ölçmüyor; S5 yalnız kovanı (`gcBytesPerCasing`) ölçüyor.

**Öneri (S-08):**
1. Yeniden kurma koşulunu sayıdan bağımsız yap: `[NonSerialized] bool _indexBuilt` alanı `Set()` içinde false yapılır; `TryGet` yalnız `!_indexBuilt` iken kurar.
2. EditMode testi: yüklü `AudioCatalog` girdilerinde null yok ve `Sound` değerleri tekil. Ayrıca builder'da v4 manifest içi yineleme bir hata olarak raporlansın.
3. İstenirse S2 kit testine atış başına GC baytı rapor anahtarı eklenebilir. Bu bir kapı değişikliği olur ve C-00 kaydı gerektirir.

### G3 — Reload başında şarjör parçası araması (düşük)

**Nerede:** `Weapons/WeaponMagazineVisual.cs:36-38` (`Bind` → `Find(weaponRoot, PartName)`), satır 93 (`root.name == name`). Çağıran `Weapons/WeaponController.cs:716`, her reload başlangıcında.

- `Transform.name` her okumada yeni bir managed string döndürür. Silah rig'inin bütün düğümleri gezildiği için reload başına onlarca string oluşur, tahminen **~1–3 KB**.
- `found == _part` erken çıkışı arama **bittikten sonra** çalışıyor, yani aynı silahta tekrar eden reload'lar da arama yapıyor.

**Öneri (S-02):** `_boundRoot` alanı tut ve `if (weaponRoot == _boundRoot && (_part != null || weaponRoot == null)) return;` ile aramayı silah değişimine indir. Ya da parçayı silah takıldığında (equip) bir kez bul.

### G4 — İlk atışta statik ses tablolarının kurulması (düşük, tek sefer)

**Nerede:**
- `Audio/Runtime/WeaponFireAudio.cs:148`: `s_layers = BuildLayers()`. 10 silah × 6 katman = 60 `LayerName` birleştirmesi ve 60 `Enum.TryParse`.
- `Audio/Runtime/WeaponFoleyAudio.cs:188-189`: `s_v4Casing` / `s_v4Link`, 2 × 10 × 5 = 100 birleştirme ve 100 `Enum.TryParse`.
- İlk `Enum.TryParse`, `AudioSoundId`'nin bütün adları için yansımayla bir ad önbelleği kurar.

**Etki:** tipin ilk kullanıldığı kare, yani ilk atış ve ilk kovan. Tek seferlik, birkaç on KB'lık ayırma ve JIT/CPU sıçraması. BEKLEYEN §9 / WP-087'nin `FeedbackPoolWarmer` ısındırmasıyla aynı türden bir sorun.

**Öneri (S-08):**
- `PrototypeAudioDirector` kurulumunda ya da `FeedbackPoolWarmer`'da iki tipe bir kez dokun (`WeaponAudioIdentity.Layer("AR1", WeaponLayer.Mechanical)` ve `WeaponFoleyV3.CasingV4("AR1", SurfaceKind.Stone, false)`).
- Ya da tabloları builder'da önceden üret.

### G5 — Grip araması (düşük, 05.10'dan önce var)

**Nerede:** `Characters/WeaponGripIk.cs:91` (`FindGrip`, `SearchInterval = 0.25 s`), satır 179 (`t.name == name`).

- Elde grip'i olmayan bir silah varken bu arama saniyede 4 kez çalışır. Örnekler: Special silahlar ya da modeli henüz yüklenmemiş bir silah.
- Arama `GripNames` × düğüm sayısı kadar `name` string'i ayırır.
- 05.10 değişikliği değil, ama 05.10'da eklenen `WeaponHoldIk` destek noktasını `TryGetSupportGoal` ile bu bileşenden alıyor (`PlayerSkinnedVisual.cs:356`). Varsayılan AÇIK kararı uygulanınca bu yol her karede aktif olacak.

**Öneri (S-06):**
- Aramayı silah takıldığında (equip olayı) bir kez yap; bulunamazsa sonucu o silah için hatırla.
- Ya da adları `CompareTag` / önceden hesaplanmış bir eşlemeyle (silah kimliği → grip Transform) değiştir.

## 4. Temiz bulunan sıcak yollar (05.10)

Aşağıdakilerde kare, atış ya da olay başına managed ayırma bulunmadı. Hepsi struct yükü, önceden ayrılmış dizi, `RaycastNonAlloc` ya da salt matematik kullanıyor:

- `Characters/WeaponHoldIk.cs` `LateUpdate`. `Func<>` çağrıları ayırmaz. `Func<Vector3?>` nullable struct döndürür, boxing yok.
- `PlayerSkinnedVisual.Attach` içindeki lambdalar (kapı ve destek, `:355-356`) ile `PlayerRigFactory.cs:227-228`: closure bağlanırken bir kez oluşur, karede yalnız çağrılır.
- `Characters/LocomotionV4.cs` `LocomotionV4Events.Feed` ve `PlayerSkinnedVisual` v4 beslemesi (`:883-885`, `SetInteger`).
  - `LocomotionV4.ClipName/StateName` string kurar ama yalnız Attach (`:327`) ve builder'da çağrılır.
  - `_animator.parameters` dizi ayırır, ama yalnız Attach'te (`:332`).
- `Characters/CharacterAnimationEvents.cs` `Raise` / `BeatFamily`: bit maskesi, ayırma yok.
- `Audio/Runtime/WeaponFireAudio.cs` `TryHandle` / `PlayVoice` / `PlayTail` / `Tick`, G2 koşulu dışında. `WeaponAudioIdentity.Layer/IndexOf/TryGet` doğrusal ordinal karşılaştırma yapar; `AudioPlacement` bir struct.
- `Audio/Runtime/WeaponFoleyAudio.cs` kovan kuyruğu: `Pending` / `Deferred` struct dizileri, `_launchSerial` / `_launchFrame` sabit diziler, `RaycastNonAlloc`.
- `Audio/Runtime/AudioVoicePool.cs` `PlayDelayed`, `AudioMixPlan.PlayerWeapon`.
- `Feedback/Vfx/CasingEvents.cs`: statik `event Action<CasingLaunchInfo>`, readonly struct yük. `CasingEffect.PredictLanding` statik `RaycastHit[1]` kullanır; CPU notu B2'de.
- `Feedback/Vfx/RocketVisualSystem.cs` ve `TracerRibbonRenderer.cs` catch-up: salt matematik, struct.
- `Feedback/FeedbackPoolWarmer.cs`: ısındırma, kurulumda çalışır.
- `Weapons/WeaponController.cs` reload sunumu ve `PublishMechanism`: struct olaylar. `Stepped += OnMagazineStep` aboneliği Awake'te bir kez yapılır.
- `Weapons/WeaponMagazineOwnership.cs` `Tick` / `OnEvent` (CPU notu B1) ve `ClassicWeaponRuntime` kabuk takvimi özellikleri.
- `Enemies/EnemyFactory.cs` / `ZombieEnemy.cs` (WP-088): `PurgeDestroyed` / `PruneDestroyedFromRoster` yalnız başarısız `Get` ve `ClearAll` yolunda çalışır.
- Menü v4 (`MainMenuScreen`, `UiIconButton`, `UiTooltip`): olay güdümlü. `Update` yalnız Cancel okuyor. `Narration` / `ShortLabel` birleştirmeleri odak değiştiğinde çalışır, kare başına değil.
- `UI/Runtime/Flow/KeyArtRotation.cs` ve `UiCurtain` rotasyonu: yükleme ekranında, bir kez.
- `Diagnostics/Qa/QaRunBot.cs` `TickInputHealth`: sayaçlar; günlük satırı tur başına en çok bir kez. `QaVirtualGamepad`: kurulumda.

**Not (GC değil, bellek):** `UiCurtain` 5 yükleme sanatını `Resources.Load` / `LoadAsync` ile döndürüyor ve projede `Resources.UnloadUnusedAssets` çağrısı yok. Single sahne yüklemesinde Unity kullanılmayanları kendiliğinden boşaltır. Yalnız güncel sanat bir başvuruda tutulduğu için sızıntı beklenmiyor, ama yerelde Memory Profiler ile bir oturumda 5 sanatın birlikte yerleşik kalmadığı teyit edilebilir (S-09, isteğe bağlı).

## 5. Yerelde doğrulama (Profiler, sahip oturumlar)

Her koşu `tools/userdata-guard.sh` penceresinde, Unity `tools/slot.sh` ile; disk ≥ 15 GB.

| # | Sahip | Ne | Nasıl | Beklenen (düzeltme sonrası) |
|---|---|---|---|---|
| G1 | S-03 | doğuş başına GC | Z3b sahnesi (AP005, 40 zombi). Refill sırasında Profiler "GC Alloc": `EnemyFactory.Spawn` → `ZombieAppearance.ApplyVersion` / `ApplyTierTint` → `ZombieSkinnedVisual.ApplyMaterial`. Ya da `Is.Not.AllocatingGCMemory()` testi. | bugün: doğuş başına KB düzeyinde (tahmin 8–15 KB); düzeltme sonrası ısınmış havuzla 0 B |
| G2 | S-08 | katalog tekilliği ve atış GC | EditMode: yüklü katalogda null yok, `Sound` tekil. PlayMode: ar_1 basılı tutmada 100 atış boyunca "GC Allocated In Frame" (pozitif kontrol 64 KB). | 0 null / 0 yineleme; atış başına 0 B |
| G3 | S-02 | reload başına GC | Şarjörlü 6 silahla 3'er reload; reload başlangıç karesinde ProfilerRecorder "GC Allocated In Frame". | bugün > 0; düzeltme sonrası aynı silahta tekrar reload 0 B |
| G4 | S-08 | ilk atış karesi | Taze oturum, ilk ar_1 atışı ve ilk kovan karesi: GC Alloc + CPU. | ısındırma sonrası ilk atış karesi diğer atışlardan farksız |
| G5 | S-06 | grip yokken periyodik GC | Raijū ya da Namekuji elde, TP, 10 s; GC Alloc 0,25 s aralıkla tekrar ediyor mu. | düzeltme sonrası 0 B |

Bu satırlar `docs/c00/LOCAL-QUEUE.md`'ye tek satır olarak eklendi.

## 6. Kapsam dışı ve sınırlar

- Unity koşturulmadı. Miktarlar tahmindir; "geçti" ya da "ölçüldü" denmiyor.
- Test (`Tests/`) ve Editor kodu incelenmedi; sıcak yol değiller.
- Şu oturumların commit'lenmemiş ya da dalda bekleyen işleri kapsam dışı: S-03 zombi v2, S-06 B2d/B2e, S-08 v003g ve `Launched` tüketicisi, S-09 sinematik. Main'e girdiklerinde aynı yöntemle yeniden taranmaları önerilir. Özellikle S-08'in `CasingEvents.Launched` tüketicisi kovan başına çalışacak; abonelik bir kez yapılmalı ve `PredictLanding` önbellekli maskeyle kullanılmalı (B2).

## 7. Ek (B-05, 05.10 ~20:30): `0b3ec358..3e68bc08` aralığı

Bu aralıkta runtime C# değişikliği tektir: `Audio/Runtime/WeaponFoleyAudio.cs` (`a62d83db`, +160/−54). `d0783d9b` shader'dır (kapsam dışı). `03c3b06b` yalnız `[Explicit]` tanı testleri ekler. Diğerleri belge commit'leridir.

**Aralık etiketi düzeltmesi.** §1–§4'ün taradığı fark, inceleme sırasında çekilen `origin/main`'e göreydi; o an `a62d83db`'yi zaten içeriyordu. Kanıt: §1 aday listesindeki `WeaponFoleyAudio.cs:330` (`_deferred`) ve `:334` (`_launchSerial`) satırları `a62d83db`'de eklenmiştir. Başlıktaki "`..0b3ec358`" etiketi bu yüzden eksik yazılmıştı. §4'teki "kovan kuyruğu temiz" hükmü bu kodu kapsıyor. Yine de `a62d83db` aşağıda ayrıca, aynı yöntemle incelendi.

**`a62d83db` — kovan sesi kendi görünen atışından (`CasingEvents.Launched` → `PredictLanding`):**

| Yol | Sıklık | Bulgu |
|---|---|---|
| `Enable` / `Disable` (`:378–394`): `WeaponEvents.Mechanism` ve `CasingEvents.Launched` abonelikleri | etkinleştirmede bir kez; `_subscribed` bayrağı çift aboneliği önler | Metot grubu → delegate ayrımı yalnız bu anda (2 nesne). Sıcak yolda değil. |
| `OnMechanism` → `Deferred` kuyruğu (`:408`) | kovan başına | `Deferred` bir struct; sabit dizi `new Deferred[4]` alan başlatmasında. Ayırma yok. |
| `OnLaunched` (`:411`) | görünen kovan başına | Halka tamponlar `_launchSerial` / `_launchFrame` (`int[8]`, alan başlatması), struct yük `CasingLaunchInfo`. Ayırma yok. |
| `Schedule` (`:450`) → `CasingEffect.PredictLanding` (`:456`) + `Physics.RaycastNonAlloc` | kovan başına | Ayırma yok. **B2 artık sıcak yolda:** `PredictLanding` her çağrıda `LayerMask.NameToLayer("Environment")` yapıyor; bu, kovan başına bir ad araması demek. Yalnız CPU maliyeti; maskeyi statik önbelleğe almak yeterli (S-02 `CasingEffect` sahibi, S-08 çağıran). |
| `Tick` (`:560–564`) içinde `Resolve` | kare başına, bekleyen slot varken | Struct kopyası + `RaycastNonAlloc`. Ayırma yok. |
| `NominalVelocity` (statik) | sahipsiz slotta | Salt matematik. `VfxWeaponProfiles.For` önceden de vardı. |

**Sonuç:** `a62d83db` yeni bir ayırma bulgusu getirmiyor. Yalnız B2'nin önceliği "tüketici dalda" durumundan "main'de, kovan başına" durumuna yükseldi. Sahip satırı `BEKLEYEN-MESAJLAR.md`'de. S5 ölçümü (`gcBytesPerCasing` 0, commit gövdesi: r2 S5 PASS, gc 0) bu sonucu destekliyor; o ölçüm yerelde, guard altında alınmıştı.

**r2 kapı denetleyicisi, main `3e68bc08` içeriği üzerinde** (`git archive` anlık görüntüsü, bulut): 15 yazıcı çağrısı · ERROR 1 · WARN 14 · INFO 14. Sonuç `0b3ec358` ile aynı. ERROR'ın düzeltmesi bu dalın sonraki commit'inde.
