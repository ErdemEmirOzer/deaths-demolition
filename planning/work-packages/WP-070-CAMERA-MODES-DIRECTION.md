# WP-070 — Kamera modları yön belgesi (Faz A tasarım)

Paket: WP-070 (S-01) · Belge sahibi: tasarım konseyi (game-designer + interaction-designer, Fable 5.1) · Tarih: 2026-09-24 · Durum: TASLAK v1 — S-02/S-05/S-06/S-07/S-09/S-03 görüşleri S-01 tarafından birleştirilecek.
Kaynak: M4-R1 §0 N2 + N18 (kullanıcı, birebir) · Taban: güncel main (`PrototypeCameraRig`, `PlayerAim`, `CameraShakeService`, `CameraOcclusionFader`, `FeelRuntime`).
Kural: "öneri" işaretli değerler sahip/kullanıcı onayı ister; işaretsiz değerler bağlayıcı tasarım kararıdır. Sayısal his değerlerinin nihai sahibi S-07 (FeelRuntime); bu belge başlangıç aralığını verir.

## 0 · Özet

1. Üç mod: **TopDown** (mevcut rig, değişmez), **ThirdPerson** (omuz üstü, 1:1 fare/çubuk bakış), **FirstPerson** (göz hizası, gövde görünür, baş ShadowsOnly, silah ayrı render katmanı).
2. Geçiş yalnız Pause → "Kamera" seçicisinden; seçim anında başlar, unscaled-time harmanla ≤ 0,55 s; harman sırasında ateş/doldurma girişi yutulur, hareket serbest.
3. Nişan tek kaynaktan: `PrototypeCameraRig.TryGetAimRay` (sallantısız). TopDown = imleç → nişan düzlemi (bugünkü), TP/FP = ekran merkezi ışını → dünya + EnemyHitbox. Atış namludan `HitPoint`'e; paralaks koruması §4.4.
4. His ayrımı: **recoil bakış durumunu oynatır** (oyun kuralı), **bob/shake/FOV punch çocuk düğümde** (kozmetik, nişanı asla oynatmaz). Mod başına ölçek `CameraModeFeel` (S-07).
5. FP'de arka yarımküre kör: `CameraView.IsRearBlind` → S-03 (spawn payı + ön uyarı), S-08 (konumsal ses), S-09 (HUD kenar yayı). Zorluk sayıları mod başına DEĞİŞMEZ; telemetriyle ölçülür.
6. Tepeden-özel sistemler (occluder/çatı, LOD, gölge, sis/uzak plan, culling) mod profiline bağlanır; dünya göz hizası gereksinimleri §9 → S-05 WP-074.
7. Perf: her mod × her kademe (Sinematik/Yüksek/Performans) ayrı ölçülür; Performans kademesi üç modda da 8,0 ms'ye sığar (DEC-088), Yüksek kademe hedefi 🔴 kullanıcı kararı (öneri 16,6 ms).
8. QA botu üç modda koşar (`-ddCameraMode topdown|tp|fp`), her moddan kanıt karesi; kabul ölçütleri §12.

## 1 · Hedef deneyim (oyuncu fantezisi, mod başına)

| Mod | Fantezi | Güç/zayıflık | Referans hissi |
|---|---|---|---|
| TopDown | "Avlu kumandanı": kalabalığı okur, kaçış hattını görür | Farkındalık en yüksek, mesafe isabeti imleçle kesin; zombi yüzü uzak, korku düşük | Mevcut oyun; bozulmaz |
| ThirdPerson | "Ronin sahnede": karakteri, kostümü (AP-006 3 skin), silah animasyonunu görür | Farkındalık orta (omuz üstü ~95° görüş + arka kör), kapalı alanda kamera itmesi | Resident Evil 4 Remake omuz kamerası, Dead Space itme mesafesi |
| FirstPerson | "Kuşatma altında": zombi yüzü yakın, recoil ve adım bedende hissedilir (N18) | İsabet en yüksek, farkındalık en düşük (arka kör), hareket hastalığı riski | Left 4 Dead 2 / Back 4 Blood: hafif bob, sıkı recoil, temiz merkez reticle |

D-1 Modlar "aynı oyun, farklı kamera": ekonomi, tur, düşman sayıları, ödül tabloları mod başına değişmez. Gerekçe: üç ayrı denge tablosu R1'de sürdürülemez; farkındalık farkı uyarı kancalarıyla (§7) telafi edilir, hasarla değil.
D-2 Mod tercihi profil değil oturum ayarıdır: ui-settings.json'a yazılır, koşular arası korunur, koşu sonucu satırına `cameraMode` alanı eklenir (telemetri, §7.4).

## 2 · Mod tanımları ve parametre tablosu

### 2.1 TopDown (değişmez)
Yükseklik 15 m, geri 13 m, FOV 42°, yaw yazar tarafından, ileri-bakış ≤ 2,5 m (nişana doğru), SmoothDamp 0,12 s, shake çocuk düğümde, `ScreenPointToAimPlane` sallantısız. Ölüm beat'i (DeathSequence) mevcut push-in. Bu belge TopDown'a tek bir değer bile eklemez.

### 2.2 ThirdPerson (omuz üstü)

| Parametre | Varsayılan | Aralık | Sahip | Not |
|---|---|---|---|---|
| Pivot | Göğüs kemiği + (0, 0,15, 0) m | — | S-01 | Kalça değil göğüs: eğilme/vuruş animasyonunda kamera zıplamaz |
| Omuz ofseti (yatay) | 0,55 m öneri | 0,40–0,70 | S-01/S-07 | `Shoulder` Right/Left işaret değiştirir |
| Geri mesafe | 2,8 m öneri | 2,2–3,4 | S-01/S-07 | Ateş sırasında −0,3 m sıkışma (S-07 FeelRuntime) |
| Pivot üstü yükseklik | 0,35 m | 0,25–0,50 | S-01 | Göz seviyesinin biraz üstü: kalabalık okunur |
| FOV (dikey) | 60° öneri | 55–70 | Ayar `ThirdPersonFov` | 16:9'da ≈ 91° yatay |
| Pitch sınırı | −55° … +70° | — | S-01 | Aşağı bakışta zemin/ayak görünür; yukarı bakış plaka sınırı (AP-019) |
| Konum takibi | SmoothDamp 0,06 s | 0,03–0,10 | S-07 | Dönüş takipsiz (1:1); gecikme yalnız konumda |
| Çarpışma | SphereCast r = 0,25 m, katmanlar Environment ∪ CameraOccluder | — | S-01 | Pivot→kamera; temas anında anlık, geri açılma 0,25 s |
| Min. mesafe (itme tabanı) | 0,60 m | 0,45–0,80 | S-01 | Bu mesafenin altında oyuncu tam soldurulur |
| Oyuncu soldurma | Dither başlangıç 1,20 m → tam 0,60 m | — | S-01/S-05 shader | Alfa değil dither (sıralama/gölge güvenli); gölge korunur |
| Omuz değiştirme | `ILookInputSource.ShoulderSwapPressed`: klavye **V** + fare orta tuşu, gamepad **sağ çubuk basma** (Q = Special, çakışır); yeniden atanamaz (Look gibi) — B2 | — | S-01 (S-02 girdi haritası bilgisi) | Otomatik duvar-omzu değiştirme kapalı (öneri; test sonrası açılabilir) |
| Kamera yakın kırpma | 0,05 m | — | S-01 | Duvar itmesiyle birlikte |

D-3 TP dönüşü doğrudan (yumuşatma yok), konum yumuşatılır. Gerekçe: bakışta yumuşatma = giriş gecikmesi hissi; AAA omuz kameralarının ortak kuralı.
D-4 Yakınlık soldurması dither'dır, alfa değildir; `CameraOcclusionFader`'ın alfa yolu oyuncuya uygulanmaz. Gerekçe: skinned mesh alfa sıralaması ve gölge kaybı.

### 2.3 FirstPerson (göz hizası)

| Parametre | Varsayılan | Aralık | Sahip | Not |
|---|---|---|---|---|
| Göz konumu | Head kemiği + yerel (0, 0,08, 0,06) m; yalnız **konum** kemikten, **dönüş** bakış durumundan | — | S-01/S-06 | Animasyon başı çevirse de kamera dönmez (hastalık) |
| Göz yüksekliği sabit fallback | 1,62 m (AP-006 ronin boyuna göre S-06 doğrular) | 1,55–1,70 | S-06 | Kemik yoksa/test sahnesi |
| FOV | 85 öneri (kullanıcı 80–90) — **yatay, 16:9 referans**; Unity dikeye çevrilir (85 yatay ≈ 55,6° dikey) | 80–90 kullanıcı bandı; erişilebilirlik ayarı 70–100 | Ayar `FirstPersonFov` | 🔴 Kullanıcı: 80–90 yatay mı dikey mi? Öneri yatay (sektör standardı) |
| Pitch sınırı | −80° … +80° | — | S-01 | Aşağıda gövde/ayak görünür (gövde görünür kararı) |
| Görüntü modeli katmanı | `CameraLayers.FirstPersonView`; URP RenderObjects: depth temizle → katmanı çiz (`ViewmodelFov`) | — | S-01 | Silah asla duvara girmez: ayrı derinlik |
| `ViewmodelFov` | Faz 1: dünya FOV'una **kilitli** (`CameraLayers.ViewmodelFovOverride = 0`); Faz 2 (FP kol meshi, S-06): 65° yatay öneri | 55–75 | S-06/S-07 | Faz 1'de eller gövde meshinde: FOV ayrışırsa bilek kopar |
| Duvar yakınlığı silah duruşu | İleri SphereCast r 0,15, 0,55 m altında "silah indir" pozu (S-06) + ateş engeli YOK | 0,40–0,70 | S-06 | Derinlik temizleme kırpmayı çözer, poz yalnız inandırıcılık |
| Baş gizleme | Baş renderer'ı `CameraLayers.FirstPersonHidden`, `ShadowCastingMode.ShadowsOnly` | — | S-06 | AP-006 meshinde baş ayrı submesh değilse: kemik ağırlık maskesi shader (S-06 karar) |
| Gövde görünürlüğü | Gövde/bacak/kollar görünür, dünya katmanı | — | S-06 | Kollar AimPitch IK ile silahı izler |
| Kamera yakın kırpma | 0,03 m dünya; görüntü modeli 0,01 m | — | S-01 | |
| Hareket bulanıklığı | FP'de varsayılan KAPALI | — | S-05 post | Hastalık |

D-5 (B3 durumu) Faz 1 = gövde ShadowsOnly (baş bölme P-06 AP-022 gelene kadar) + silah `FirstPersonView` katmanında, dünya kamerasına bağlı URP overlay kamerası çizer (derinlik temizlenir → duvara girmez; FOV dünyaya kilitli; MainCamera etiketi yok); göz yüksekliği girişte Humanoid baş kemiğinden kalibre (+6 cm, 1,40–1,85); duvar sondası 0,6/0,8 m → `PlayerSkinnedVisual.PlayLower/PlayRaise`. Faz 2: S-06 WP-071 kol örneği `ViewmodelAnchor` altına, `ViewmodelFovOverride` ile ayrı FOV. Özgün karar metni: FP'de gövde gerçek (true first-person body), ayrı kol meshi Faz 2. Gerekçe: kullanıcı "gövde görünür" dedi; kol meshi AP/S-06 üretimi ister, R1'de FOV kilidiyle temiz sonuç alınır.
D-6 Baş gizleme ShadowsOnly ile: gölge bütün kalır, ayna/yansıma yok. Kask/şapka gibi ekler (AP-007C sınıfı) aynı katmana.

## 3 · Kontroller ve erişilebilirlik

### 3.1 Girdi
- TopDown: mevcut imleç nişanı + çubuk nişanı; hiçbir şey değişmez (N21 imleç küçültme S-09).
- TP/FP: `ILookInputSource.LookDelta` (fare, piksel/kare) ve `LookStick`; imleç kilitli+gizli; UI yalnız `InputSourceRegistry` üzerinden okur (ADR-024-2), Input System'e dokunmaz.

| Ayar | Varsayılan | Aralık | Kalıcılık |
|---|---|---|---|
| `LookSensitivity` (°/piksel) | 0,08 öneri; UI'da 1–20 kaydırıcı (1 = 0,02, 20 = 0,30) | 0,02–0,30 | input-settings.json |
| `StickLookDegreesPerSecond` (yaw) | 180 öneri | 60–360 | input-settings.json |
| Çubuk pitch oranı | 0,7 × yaw | sabit | FeelRuntime |
| Çubuk tepki eğrisi | üs 1,6, deadzone `InputPreferences` | — | FeelRuntime |
| Çubuk ivmesi | kapalı öneri | — | — |
| `InvertY` | kapalı | — | input-settings.json |
| Fare yumuşatma/ivme | yok (ham delta, DPI bağımsız `°/px`) | — | — |
| Reticle üstü sürtünme (yalnız çubuk) | TP 0,6×, FP 0,5× hassasiyet | 0,4–0,8 | FeelRuntime AimAssist |

D-7 Hassasiyet TP ve FP için tek değer (AAA çoğu ayrı verir; R1'de tek, ayrışma ölçüm sonrası). Gerekçe: ayar yüzeyi küçük kalır, geçişte kas hafızası bozulmaz.
D-8 İmleç durumunun tek yazarı `UiCursor` kalır (S-09). `CameraModeService` yalnız `CursorIntent` (Locked/Free) yayınlar; `UiCursor` pause/menü önceliğiyle karar verir.
D-9 Gamepad paritesi: her işlev (bakış, omuz, mod seçimi, FOV kaydırıcı) gamepad'de eşdeğer; sanal imleç TP/FP'de yok.

### 3.2 Erişilebilirlik (hareket hastalığı)
| Ayar | Varsayılan | Etkisi |
|---|---|---|
| FOV kaydırıcı (FP 70–100 yatay, TP 55–70 dikey) | §2 | Düşük FOV = daha çok hastalık; UI uyarı metni |
| `HeadBobScale` 0–1 | 1,0 (öneri; "Azaltılmış hareket" 0) | Bob genliği çarpanı, FP+TP |
| Sarsıntı ölçeği (mevcut shake ayarı) | 1,0 | Shake/kick/FOV punch çarpanı |
| Azaltılmış hareket (ana anahtar) | kapalı | bob 0, punch 0, shake 0,3, geçiş anlık, TP sıkışma 0, FP ateşte FOV sabit |
| Bakış yumuşatma | kapalı | Açılırsa 40 ms — yalnız istekle |
| Merkez nokta (statik referans) | kapalı | Küçük sabit nokta reticle üstüne (hastalık azaltır) |

D-10 Azaltılmış hareket tek anahtarla tüm kozmetik kamera hareketini kapatır; recoil (oyun kuralı) kapanmaz, yalnızca görsel geri dönüş sertleşir.

## 4 · Hareket ve nişan

### 4.1 Hareket
- `PlanarForward/PlanarRight` her modda kamera yaw'ından türetilir (mevcut sözleşme); PlayerMotor değişmez.
- TP: gövde yaw'ı kamerayı izler; ateş etmezken ve dururken ±35° ölü bant (ayak dönüş animasyonu S-06), ateş/hareket anında `RotationSpeed` ile kilitlenir. FP: gövde yaw = kamera yaw, anında.
- Geri yürüme TP/FP'de %85 hız (öneri; TopDown'da yön kavramı yok, değişmez) — S-02 kararı.

### 4.2 Nişan ışını (tek kaynak)
`PrototypeCameraRig.TryGetAimRay(out CameraAimRay)` — sallantısız kararlı pozdan:
- TopDown: imleç → nişan düzlemi (`AimPlaneHeight`), bugünkü davranış; `HasHit=true`, `HitPoint` düzlem noktası.
- TP/FP: ekran merkezi ışını, maske Environment ∪ EnemyHitbox ∪ CameraOccluder(katı olanlar), menzil 120 m; isabet yoksa `HitPoint = Origin + Direction·120`.
- Işın **recoil dahil** bakış durumundan çıkar (recoil nişanı oynatır — oyun kuralı), **shake/bob/punch hariç** (kozmetik).

D-11 `PlayerAim.AimDirection` yatay kalır (mevcut testler y=0). Pitch yalnız `CameraAimRay`'de taşınır; gövde dönüşü ve TopDown yolu bozulmaz.
D-12 `PlayerWeaponBinding`: TopDown'da mevcut düzleştirme; TP/FP'de `AimWorldPoint = ray.HitPoint`, atış yönü = namlu → HitPoint (3B, düzleştirme yok). Hitscan ve WP-069 mermi aynı kuralı kullanır (ADR-069-1: mermi namludan doğar, yön HitPoint'e).

### 4.3 Nişan yardımı (mod başına; asla fare/imleç, yalnız çubuk — mevcut kural korunur)
| Mod | Koni | Menzil | Sürtünme | Mıknatıs |
|---|---|---|---|---|
| TopDown | ≤ 6° (mevcut) | ≤ 15 m | yok | yok |
| TP | 4° öneri (3–6) | 25 m | 0,6× | yok |
| FP | 3° öneri (2–5) | 30 m | 0,5× | yok |
"Light" seviyesi mevcut `lightAngleShare` ile ölçeklenir. Sahip: S-02 (kural) + S-07 (değer).

### 4.4 Paralaks koruması (namlu ≠ kamera)
1. Hedef = merkez ışını `HitPoint`. Namlu→hedef yönü ile kamera ışını arasındaki açı > 12° ise (hedef çok yakın, köşe arkası) atış **kamera ışını yönünde**, çıkış noktası namlu (görsel iz namludan, isabet kameradan).
2. Namlu ile `HitPoint` arasında engel varsa (SphereCast r 0,05) ve engel kameraya 0,8 m'den yakınsa: reticle "engelli" durumu (S-09), atış engelde biter (dürüst geri bildirim).
3. Reticle her zaman ekran merkezinde; U12a dürüst yayılma verisi (`CurrentSpreadDegrees`, `PelletRingDegrees`) halkayı sürer.

### 4.5 Reticle mod başına (S-09 uygular, S-02 veri)
- TopDown: mevcut imleç (N21 küçültme).
- TP: merkez nokta + yayılma halkası (min 14 px, HUD ink kuralı WP-068); düşman üstünde renk değişimi yok (dürüstlük), isabet işareti var.
- FP: TP'nin %80 boyutu, halka daha ince; doldurmada halka dolum animasyonu; engelli durumu = çapraz.
- Geçiş sırasında reticle gizli.

## 5 · Geçiş

### 5.1 Akış
1. Pause → "Kamera" seçicisi (segmentli: Tepeden / Üçüncü şahıs / Birinci şahıs; ikon + kısa açıklama; S-09 WP-068 diliyle görsel).
2. Seçim anında `CameraModeService.Request(mode)`; harman **unscaled time** ile pause menüsü arkasında başlar (menü yarı saydam, oyuncu değişimi görür).
3. Ayar yazımı anında (ui-settings.json, S-09 şeması; S-01 API). Kullanıcı verisi koruması: test kökü.
4. Devam edildiğinde harman bitmemişse kalan süre oyun içinde tamamlanır; `IsTransitioning=true` iken ateş/doldurma/Special tetiği yutulur, hareket ve bakış serbest, reticle gizli.

| Geçiş | Süre | Eğri |
|---|---|---|
| TopDown ↔ TP | 0,45 s | Konum smoothstep, dönüş slerp, FOV lerp |
| TP ↔ FP | 0,30 s | Aynı; son %20'de baş gizleme/gövde katman değişimi |
| TopDown ↔ FP | 0,55 s | Aynı; TP pozundan geçmez (doğrudan) |
| Azaltılmış hareket / `instant` | 0 s | Kesme + 0,12 s siyah-değil "beyaz vinyet" 0,25 alfa (öneri) |

D-13 Oyun içi kısayol yok (kullanıcı: Pause'dan). Yalnız QA/debug: `-ddCameraMode` başlangıç argümanı ve `Request(…, instant:true)`.
D-14 Harman sırasında nişan ışını **hedef modun** kurallarına geçer ama ateş yutulur; böylece "hangi kuralla vurdum" belirsizliği yok.

### 5.2 Kenar durumları
- Doldurma sürerken: devam eder, animasyon görünür kalır (TP/FP el animasyonu S-06).
- Special aktifken (WP-007 yaşam döngüsü): mod değişir, Special süresi/şarjı etkilenmez; Special VFX kamera-bağımsızdır (S-07 doğrular).
- Ölüm: `DeathSequence` kamerayı sahiplenir; `Request` ölüm boyunca kuyruklanır (`false` döner, `Target` güncellenir), sonuç ekranında uygulanır. Ölüm beat'i mod başına: TopDown mevcut push-in; TP yavaş orbit 1,5 s; FP → 0,4 s'de TP ölüm beat'ine geçer (öneri; FP'de yere düşme kamerası R2).
- Tur geçişi/sonuç: mod korunur; yeni koşu aynı modda başlar.
- Pause içinde ikinci seçim: harman mevcut pozdan yeniden hedeflenir (kuyruklanmaz).

## 6 · Game feel mod başına (değer sahibi S-07 — WP-073; kamera uygular)

Kanal ayrımı (bağlayıcı):
- **Recoil** → bakış durumu (pitch/yaw birikir, geri dönüş hızıyla söner); nişanı oynatır. `CameraReactions.AddRecoil`.
- **Kick / Shake / Bob / FOV punch** → çocuk düğüm; nişanı asla oynatmaz. `AddKick`, `CameraShakeService`, `SetLocomotionBob`, `FovPunch`.

| Kanal | TopDown | TP | FP | Not |
|---|---|---|---|---|
| Recoil (pitch/atış, öneri) | 0 (kick ile ifade) | 0,7× FP | Tabanca 0,6°, SMG 0,35°, tüfek 0,9°, pompalı 2,2°; yaw ±%30 rastgele; geri dönüş 8°/s | Silah tablosu S-02/S-07 |
| Recoil tavanı | — | 6° | 8° | Sürekli ateşte birikim sınırı |
| Kick (çeviri + roll) | mevcut Punch/Rumble/Bump, toplam tavan 0,22 m | 0,12 m + 1,0° | **çeviri yok**, yalnız dönüş; tavan 1,8° | FP'de çeviri = duvar içine girme |
| Head bob (yürüme) | 0 | 0,25× FP (kamera sway) | dikey 0,018 m / yanal 0,010 m @ 1,9 Hz; koşu 0,030 m @ 2,4 Hz | `HeadBobScale` çarpar; adım sesi faz kilidi (S-08) |
| FOV punch | 0 | ateş +0,8° 0,08 s; sprint +2° | ateş +1,5° 0,08 s, sönüm 0,15 s; sprint +4°; Special +6° 0,3 s | Azaltılmış hareket → 0 |
| Hasar alma | mevcut | roll ±0,8°, yön vinyeti | roll ±1,2°, yön vinyeti, 0,2 s | Yön göstergesi HUD (S-09) |
| Ölüm beat'i | mevcut push-in | orbit 1,5 s | TP beat'ine düşüş | §5.2 |
| Yere iniş / eğilme | — | 0,03 m dip | 0,05 m dip + 0,6° | |

D-15 FP'de shake çevirisi yasak, yalnız dönüş. Gerekçe: göz konumu duvara 0,05 m yakınken çeviri kırpma yaratır; dönüş aynı yoğunluğu verir.
D-16 Bob konumdan değil hız-01'den sürülür (`SetLocomotionBob(speed01, grounded)`), animasyon kemiğinden değil. Gerekçe: deterministik, ayarlanabilir, test edilebilir.
D-17 N18 gereği FP recoil "dikkatli": recoil desen tabanlı (silah başına sabit desen + küçük yaw jitter), rastgele değil; oyuncu öğrenir. Desen tablosu S-02/S-07.

## 7 · Oyun etkisi

### 7.1 Arka tehdit (FP; TP'de hafif)
- `CameraView.IsRearBlind` (FP true), `IsInView(pos, margin)` (TP/FP frustum + marj).
- S-03 (ThreatDirector/AI): FP'de arka yarımküreden gelen spawn payı ≤ %35 (öneri, 25–45); arkadan 6 m'ye giren zombi `RearThreat` olayı yayınlar (min 0,8 s ön uyarı, saldırıdan önce); Special/Elite arkadan sessiz yaklaşmaz (hırıltı zorunlu).
- S-08: konumsal hırıltı + "arkadan" stinger (WP-057 tehdit katmanı); HRTF yoksa arka için düşük geçiren filtre + −3 dB.
- S-09: HUD kenar yayı (görüş dışı tehdit yönü), yalnız `IsInView=false` iken; TopDown'da yok.
- TP: `IsRearBlind=false`, ancak `IsInView(pos, 10°)` dışındaki tehdit için aynı yay daha soluk (öneri).

### 7.2 Adalet
- Nişan yardımı yalnız çubuk (mevcut kural); FP fare avantajı kabul edilir (PvE).
- Zombi saldırı menzili/telegraph mod başına aynı; FP'de yakın saldırı telegraph'ı ekranın alt yarısında okunmalı (S-06/S-07 doğrular).
- Yakın-alan patlama (WP-006) FP'de kamerayı duvara itmez (§6 çeviri yasağı).

### 7.3 Denge riskleri (izlenir, R1'de sayı değişmez)
| Risk | Belirti | Eşik | Karşılık |
|---|---|---|---|
| FP ölüm oranı yüksek | tur/ölüm oranı | > 1,3× TopDown | Ön uyarı süresi ↑, arka spawn payı ↓ (S-03) — hasar değil |
| FP isabet oranı yüksek → ekonomi enflasyonu | altın/tur | > 1,2× TopDown | Recoil tavanı/yayılma FP'de ↑ (S-02), altın tablosu değil |
| TP kamera itmesi dar koridorda | bot takılma | ≥ 1/20 tur | Min mesafe/omuz oto-değişim (S-01) |

### 7.4 Telemetri (her hipotez bir olay)
`camera_mode_set(from,to,instant,source=pause|arg)`, `camera_transition_done(ms)`, `run_result.cameraMode`, `rear_threat_warned(mode,leadMs)`, `rear_hit_taken(mode,warned:bool)`, `shot_fired.mode`, `shot_hit.mode`, `camera_push_in(mode,depth)`. Şema: S-04/analytics; yazım kullanıcı ayar dosyasına değil.

## 8 · Tepeden-özel sistemlerin mod uyumu (`CameraModeProfile` — mod başına ScriptableObject)

| Sistem | TopDown (mevcut) | TP | FP | Sahip |
|---|---|---|---|---|
| `CameraOcclusionFader` | kamera→oyuncu, alfa 0,22 / 0,15 s | aynı servis, ışın ≤ 3,5 m, yalnız **Wall** sınıfı | kapalı | S-01 |
| Çatı soldurma | CameraOccluder çatılar solar | **solmaz** (opak) | solmaz | S-05: occluder sınıfı **Roof/Wall** ayrımı (tag/alt katman) |
| LOD bias | 1,0 (yazılmış eşikler tepeden) | 1,2 | 1,4 (Yüksek); Performans 1,0 | S-05 eşik doğrulama §9 |
| Gölge mesafesi / kaskad | mevcut (≈ 40 m / 2) | 60 m / 3 | 70 m / 4 (Sinematik), 50/2 (Performans) | S-05 |
| Uzak plan | mevcut (tepeden ~80 m yeter) | 450 m | 450 m (ufuk plakaları r 420 görünsün, AP-019) | S-01 profil / S-05 doğrular |
| Sis | mevcut | başlangıç 40 m, bitiş 220 m (öneri) | aynı | S-05 WP-075 bölgesel sis kademesi |
| Katman cull mesafeleri | — | küçük prop 45 m, bitki 60 m | aynı | S-05 |
| Occlusion culling | yok/gerekmez | bake gerekli | bake gerekli | S-05 WP-074 |
| Yansıma probu / SSAO | mevcut | probe gerekli (metal zırh) | aynı | S-05 |
| İleri-bakış (look-ahead) | ≤ 2,5 m | 0 | 0 | S-01 |
| `AimPlaneHeight` | kullanılır | kullanılmaz | kullanılmaz | S-01 |
| DeathSequence | push-in | orbit | TP beat | S-01/S-07 |
| Post: motion blur / DoF | mevcut | blur düşük | blur kapalı, DoF yok | S-05 |
| Müzik/mix snapshot | mevcut | aynı | "yakın" snapshot (öneri: düşman sesleri +2 dB) | S-08 |

D-17b (B6 durumu) `CameraOcclusionFader` politikayı `CameraModeService.CurrentProfile.OccluderPolicy`'den okur: TopDown FadeAll (değişmedi), TP FadeCameraLine (aynı kamera→oyuncu sondası, `CameraOccluderClass.Kind == Roof` işaretli olanlar solmaz; işaretsiz = Wall), FP None (sonda yok, solanlar geri döner). E-20 için S-05 çatılara `CameraOccluderClass` (Roof) ekler. TP kamera çarpışması CameraOccluder'ı da kapsar (bina içine girmez), bu yüzden TP'de soldurma nadiren devreye girer.
D-18 Uyumluluk profili mod geçişinde harmanla birlikte lerp edilir (LOD bias, gölge mesafesi, sis) — ani pop yok.
D-19 Her profil değeri ScriptableObject'te; sabit kodlanmış değer 0 (O5).

## 9 · Göz hizası gereksinim listesi → S-05 WP-074 (numaralı, kontrol edilebilir)

Kanıt: her madde için FP modunda ≥ 1 kanıt karesi (`-ddCameraMode fp`, bot rotası WP-078).
- E-1 Kapalı geometri: her duvar/çit/çatı meshinin arka yüzü ya kapalı ya çift taraflı; içeriden bakışta delik yok (bot rotasında 0 delik).
- E-2 İç mekân: kapı/pencereden görünen her iç hacim ya kapalı (kapı kanadı + collider) ya asgari döşenmiş (zemin, duvar, tavan, 1 ışık).
- E-3 Ufuk 360°: avludaki her ulaşılabilir noktadan 1,62 m göz yüksekliğinde gök bandı + 3 ufuk katmanı (AP-019) boşluksuz; uzak plan 450 m.
- E-4 Zemin texel yoğunluğu: 0,5 m mesafede bulanma yok (≥ 512 px/m veya detay normal haritası).
- E-5 Collider = görsel: görünmez duvar yok, yürünür görünen yere yürünür; ±45 m NavMesh halkası sınırı **görünür engel** (WP-038R kırpması görsel karşılık ister).
- E-6 Çatı altları ve saçaklar (kiremit alt yüzü, kiriş) modelli; TP kamera çatı altına girebildiğinde siyah yüzey yok.
- E-7 Prop arka yüzleri: fener, tapınak süsü, sandık her açıdan kapalı; tek yüzlü billboard'lar kameraya döner.
- E-8 LOD geçişleri: göz hizasında büyük prop LOD1 ≥ 25 m, LOD2 ≥ 50 m'de; pop kanıt karesi ile ≤ 2 px kenar kayması.
- E-9 Işık sızması: lightmap dikişleri ve duvar-zemin birleşimlerinde sızma yok (UV2 dersleri).
- E-10 Z-fighting: zemin dekalleri/çakışan düzlemler göz hizasında titremez.
- E-11 Havada duran/gömülü prop yok (tepeden fark edilmeyen 5 cm hatalar).
- E-12 Sis/atmosfer yatay bakışa göre kalibre (WP-075); ufuk plakası ile sis rengi uyumlu.
- E-13 Yansıma probları: en az 4 bölge probu; zırh/metal FP'de siyah yansımaz.
- E-14 Occlusion culling bake + bot koşusunda per-mode draw call raporu.
- E-15 Siper yükseklikleri TP için okunabilir (0,9–1,1 m alçak, ≥ 1,8 m yüksek); FP'de 1,62 m göz hizasını kapatan siper "yüksek" sınıfına girer.
- E-16 VFX (AP-010) yönelimi: yatay bakışta düz kalan billboard/dekal yok; kan/patlama sıralaması FP'de görüntü modelinin arkasında.
- E-17 Yükseklik farkları: merdiven/rampa yürünürken bob + kamera çarpışma titreşimi yok (collider basamak tavanı ≤ 0,3 m).
- E-18 Bitki: kamera 0,5 m yakınken yaprak alfa kesimi kaba görünmez (AP-015P yüzeyler doğrular).
- E-19 Dünya-uzay HUD işaretleri (varsa) FP frustumunda taşmaz (S-09 ile).
- E-20 Occluder sınıflandırması: CameraOccluder nesneleri Roof/Wall etiketli; TP'de yalnız Wall solar.

## 10 · API sözleşmesi (taslak v1 — kodlandı, dal `wp-070-camera-modes`; adlar bağlayıcı)

Dosyalar: `Assets/_Prototype/Core/Camera/CameraModeTypes.cs` (Core: enum/ayar/profil), `Assets/_Prototype/Camera/CameraModeService.cs`, `CameraView.cs`, `CameraReactions.cs` (Runtime), `Assets/_Prototype/Core/Input/ILookInputSource.cs` (Core), `PrototypeCameraRig.TryGetAimRay / ViewmodelAnchor`. EditMode sözleşme testi `Tests/EditMode/CameraModeServiceTests.cs`.

```
namespace DD.Prototype.CameraSystem
enum CameraMode { TopDown, ThirdPerson, FirstPerson }          enum CameraShoulder { Right, Left }
enum CameraOccluderPolicy { FadeAll, FadeCameraLine, None }     enum CameraCursorIntent { Free, Locked }
readonly struct CameraModeChange { From, To, Instant }

static class CameraModeService {
  CameraMode Current;            // oyun mantığı bu kareden itibaren buna uyar (geçişin BAŞINDA döner)
  CameraMode Previous, Target;   // Target = ölüm kilidi arkasında kuyruktaki istek, yoksa Current
  bool IsTransitioning; float Transition01; bool IsLookMode;     // IsLookMode = TP/FP
  event Action<CameraModeChange> Changed;                         // Current güncellendikten sonra, istek başına 1
  bool Request(CameraMode mode, bool instant = false);           // aynı mod / kilitli → false (kilitliyse kuyruk)
  void LockModeChanges(string id); void UnlockModeChanges(string id); bool IsModeChangeLocked;  // DeathSequence
  float TransitionSeconds(CameraMode from, CameraMode to);       // 0,45 / 0,30 / 0,55; ReducedMotion → 0
  CameraModeSettings Settings; void ApplySettings(settings, applyMode = true, instant = false); event Action SettingsChanged;
  CameraMode ResolveStartMode();                                  // -ddCameraMode topdown|tp|fp > kayıtlı tercih
  CameraModeProfile Profile(mode); CameraModeProfile CurrentProfile; void SetProfile(mode, p); event Action ProfilesChanged;
  bool IsLookBlocked; void AddLookBlock(string id); void RemoveLookBlock(string id);   // S-09: donmayan menüler
  CameraCursorIntent CursorIntent; bool CursorIntentHandledExternally;                // D-8: UiCursor tek yazar
}
class CameraModeSettings { Mode, LookSensitivity (°/px, 0,02–0,30, vars. 0,08), StickLookDegreesPerSecond (60–360, 180),
                           InvertY, FirstPersonFov (YATAY °, 16:9 referans Hor+, 70–110, 90 — C-00 24.09), ThirdPersonFov (dikey °, 55–70, 60),
                           Shoulder, HeadBobScale (0–1), ReducedMotion; Sanitize(); Clone() }
struct CameraModeProfile { LodBiasFactor, ShadowDistance, ShadowCascades, NearClip, FarClip, OccluderPolicy, ShakeScale }
                           // LodBiasFactor = S-05 kademe tabanı çarpanı: lodBias = max(1, taban × çarpan)
                           // TopDown 1,0/60/2/0,3/260/FadeAll · TP 0,6/60/3/0,05/450/FadeCameraLine · FP 0,7/70/4/0,03/450/None
struct CameraAimRay { Origin, Direction, HitPoint, HasHit, Distance }
bool PrototypeCameraRig.TryGetAimRay(Vector2 screenPoint, float aimPlaneY, out CameraAimRay ray);
     // TopDown = imleç → nişan düzlemi (ScreenPointToAimPlane ile birebir); TP/FP = ekran merkezi, maske AimBlock ∪ EnemyHitbox, 120 m
Transform PrototypeCameraRig.ViewmodelAnchor;                  // FP görüntü modeli kökü (dilim 3)
static class CameraReactions { AddRecoil(pitch, yaw); AddKick(localOffset, roll); SetLocomotionBob(speed01, grounded); FovPunch(delta, seconds); }
static class CameraView { Position; Forward; IsRearBlind; ViewmodelAnchor; IsInView(worldPos, marginDeg); Vector3 ViewmodelToWorld(vmPos); }
static class CameraLayers { const string FirstPersonViewName = "FirstPersonView"; int FirstPersonView;
                            float ViewmodelFovOverride /*0 = Faz 1 dünya FOV'una kilitli*/; float ViewmodelFov; }

namespace DD.Prototype.Core.InputAbstraction
interface ILookInputSource : IInputSource { Vector2 LookDelta; Vector2 LookStick; }
static LookInputExtensions { Vector2 ReadLookDelta(this IInputSource); Vector2 ReadLookStick(this IInputSource) /*yoksa AimStick*/ }
```
Kalıcılık: mod/FOV/omuz/headbob/ReducedMotion → ui-settings.json (S-09 şema); hassasiyet/InvertY/çubuk hızı → input-settings.json (`InputPreferences` v3 + Clamp göçü, S-02 kalıbı). Servis diske yazmaz (testler/bot kullanıcı verisine dokunamaz).
D-20 Recoil `TryGetAimRay`'e dahildir (bakış durumu); kick/bob/punch/shake hariçtir. Recoil'in **kaynağı S-02 desen verisidir** (WP-069: silah başına desen → `AddRecoil`); S-07 yalnız geri dönüş/yay ve kozmetik kanal değerlerinin sahibidir. Sözleşme testi: bob/punch/kick aktifken `Direction` değişmez; `AddRecoil(1,0)` sonrası pitch 1° değişir.

## 11 · Perf ve kalite kademeleri

Ölçüm: her mod × her kademe, bot rotası (WP-078), 1080p referans Mac; ort / p99 kare ms, GC B/kare, draw call, set-pass. Rapor `tools/build-report.sh` çıktısına mod ekiyle.

| Özellik | Sinematik | Yüksek | Performans |
|---|---|---|---|
| Gölge mesafe/kaskad (TP/FP) | 70 m / 4 | 60 m / 3 | 45 m / 2 |
| LOD bias (FP) | 1,6 | 1,4 | 1,0 |
| Uzak plan | 450 m | 450 m | 350 m (ufuk plakaları 330'a çekilir, S-05) |
| Görüntü modeli geçişi | ayrı geçiş + gölge | ayrı geçiş | ayrı geçiş (gölgesiz) |
| SSAO / yansıma | tam / probe | yarım / probe | kapalı / sabit |
| Occluder soldurma (TP) | var | var | var |
| Bob/punch | tam | tam | tam (CPU önemsiz) |

Beklenen ek maliyet (öneri, ölçülür): görüntü modeli geçişi ≈ 0,2–0,4 ms; TP çarpışma SphereCast ≈ 0,02 ms; FP uzak plan + gölge kaskad ≈ +1,5–3,0 ms (Yüksek).
D-21 Performans kademesi üç modda da DEC-088 8,0 ms'ye sığar (bağlayıcı, §4.8 kısma). Yüksek/Sinematik hedefi 🔴 kullanıcı kararı (öneri Yüksek ≤ 16,6 ms ort / p99 ≤ 20 ms). Karar gelene kadar Yüksek de 8,0 ms'ye kısılır, maliyet raporlanır.
D-22 Kademe seçimi ile mod birbirinden bağımsız; profil = mod × kademe matrisi (9 hücre), tümü ölçülür.

## 12 · Test ve QA

- Bot (QaRunBot, WP-026) `-ddCameraMode topdown|tp|fp` ile üç modda koşar; TP/FP'de bakış hedefi = en yakın tehdit (`CameraView` ile), atış `TryGetAimRay` üzerinden. S-10 WP-078 kanıt: mod başına ≥ 3 kare (avlu açık, koridor/çatı altı, ufuk).
- EditMode: `CameraModeService` durum makinesi (Request/ölüm kuyruğu/instant), ayar kalıcılığı (test kökü, kullanıcı dosyasına yazmaz), D-20 sözleşme testi, `PlayerAim.AimDirection` y=0 korunur, `CameraAimRay` TopDown = eski `ScreenPointToAimPlane` sonucu (regresyon).
- PlayMode: TP SphereCast duvar itmesi (kamera hiçbir karede duvar içinde değil), FP görüntü modeli derinlik temizleme (duvara 0,1 m'de silah tam görünür — kare karşılaştırma), baş ShadowsOnly (FP karede baş pikseli yok, gölgede var), geçiş süresi ±%10, harman sırasında ateş yutulur, imleç durumu (`UiCursor` tek yazar).
- Değişen süit tek koşu (HIZLI HAT §7); perf ölçümü yalnız §11 matrisinde, ayrı slotta.

Kabul ölçütleri (WP-070 Faz B):
| # | Ölçüt | Eşik |
|---|---|---|
| K1 | TopDown regresyonu | mevcut kamera/nişan testleri 0 değişiklik, kare karşılaştırma fark 0 |
| K2 | Üç modda bot 5 tur | takılma 0, kamera-duvar içi kare 0 |
| K3 | Geçiş | 3 çift × 2 yön; süre ±%10; ateş yutma doğrulandı |
| K4 | Nişan sözleşmesi | D-20 testleri PASS; paralaks §4.4 iki senaryo |
| K5 | Ayar kalıcılığı | 6 alan yazılır/okunur; test kökü; şema sürümü |
| K6 | FP görüntü modeli | duvar kırpma 0 kare; baş 0 piksel |
| K7 | Perf | Performans kademesi 3 modda ≤ 8,0 ms ort; matris raporu 9 hücre |
| K8 | Erişilebilirlik | azaltılmış hareket: bob/punch 0, geçiş anlık; FOV kaydırıcı aralıkta |
| K9 | Gamepad paritesi | her işlev çubuk/düğmeyle; UI Input System okumaz (ADR-024-2 tarama 0) |
| K10 | Sabit kodlanmış değer | profil/feel dışı 0 |

## 13 · Açık sorular / sahip

| # | Soru | Sahip | Gerekli tarih |
|---|---|---|---|
| A-1 | Omuz değiştirme ve bakış eylemleri girdi haritası (Q / sağ çubuk basma öneri); geri yürüme hızı; recoil deseni silah başına | S-02 | Faz B dilim 4 |
| A-2 | ADR-069-1 mermi: namlu→HitPoint ve §4.4 kamera-ışını fallback uyumu | S-02 (WP-069) | Faz B dilim 2 |
| A-3 | `CameraModeFeel` değerleri (§6 tablo), bob/recoil/punch; FP shake çeviri yasağı kabulü | S-07 (WP-073) | Faz B dilim 3 |
| A-4 | Pause "Kamera" seçicisi görseli, ui-settings.json şeması, reticle mod varyantları, kenar yayı, CursorIntent tüketimi | S-09 | Faz B dilim 5 |
| A-5 | Baş gizleme yöntemi (submesh mi kemik maskesi mi), AimPitch IK, ayak dönüşü ölü bant, "silah indir" pozu, göz yüksekliği doğrulama, Faz 2 FP kol meshi kapsamı | S-06 | Faz B dilim 3 |
| A-6 | Occluder Roof/Wall sınıflandırması, §9 E-1…E-20 planı, uzak plan 450 m + ufuk plakası, occlusion bake, mod×kademe profil değerleri | S-05 (WP-074/075) | E-20 dilim 6 öncesi |
| A-7 | Arka spawn payı tavanı, `RearThreat` olayı ve ön uyarı süresi, Elite sessiz yaklaşma yasağı | S-03 | Faz B sonrası R1 |
| A-8 | Arka stinger, konumsal filtre, FP mix snapshot | S-08 | R1 |
| A-9 | Üç mod bot rotası ve kanıt kare noktaları | S-10 (WP-078) | Faz B dilim 7 |
| A-10 | Telemetri şeması §7.4 | S-04 / analytics | R1 |

🔴 Kullanıcı kararları (C-00 iletir): (1) FOV 80–90 **yatay** yorumu (öneri evet); (2) Yüksek kademe perf hedefi (M4-R1 §6.1 ile aynı); (3) TP varsayılan omuz sağ, geri 2,8 m, FP FOV 85 varsayılanları; (4) FP ölümünde TP beat'e düşüş (öneri) mi, FP yere düşme mi (R2).

## 14 · Riskler

| Risk | Olasılık | Etki | Azaltım |
|---|---|---|---|
| Dünya göz hizasında "sahne arkası" görünür (arka yüz, boş iç mekân) | yüksek | yüksek | §9 listesi WP-074'e bağlayıcı; FP kanıt kareleri kapı ölçütü |
| FOV kilitli görüntü modeli (Faz 1) AAA hissini sınırlar | orta | orta | Faz 2 FP kol meshi (S-06/AP); Faz 1'de silah ölçeği/konumu S-07 ile |
| Hareket hastalığı şikâyeti | orta | yüksek | §3.2 varsayılanları, FOV kaydırıcı, azaltılmış hareket; playtest anketi |
| Perf: uzak plan + gölge kaskadı 8,0 ms'yi aşar | yüksek | orta | D-21 kısma; Performans kademesi zorunlu; matris raporu |
| Nişan sözleşmesi kırılması (y=0 testleri, WP-062K FeelRuntime yaw testi) | orta | yüksek | D-11/D-20; TopDown regresyon kare karşılaştırma |
| TP kamera dar koridorda oyuncuyu gizler | orta | orta | Dither + min mesafe; omuz oto-değişim yedekte |
| FP'de arka saldırı "haksız" hissi | yüksek | orta | §7.1 üç kancalı uyarı; telemetri eşikleri §7.3 |
| Ölüm/Special/doldurma sırasında geçiş tutarsızlığı | orta | düşük | §5.2 kuyruk + yutma kuralları; EditMode testleri |

## 15 · Oturum görüşleri ve birleştirme (S-01, 24.09)

Ham notlar: `~/Desktop/KADRO/context/projects/deaths-demolition/wp-070/evidence/faz-a-gorusler.md`.

| Oturum | Görüş (özet) | Birleştirme |
|---|---|---|
| S-02 | AimWorldPoint = merkez ışının ilk isabeti (yoksa maxRange); mermi namludan AimWorldPoint'e, ikinci ışın yok; TP/FP'de dikey nişan AÇIK; `PlayerWeaponBinding` y düzlemesini mod koşullu S-02 yapar (WP-069 B1); aim assist TP/FP'de yalnız çubuk, yapışkanlık + hafif mıknatıs, snap yok, ADS'de koni daralır; FP gövde yaw = kamera, TP'de ateş/ADS'de hızlı hizalama; ILookInputSource + `Look` eylemi (yeniden atanamaz); duyarlılık/Y-ters InputPreferences v3 | §4.2/§4.3/D-12 aynen; §4.4 madde 1 (12° fallback) S-02 ADR-069-1 ile teyit edilecek (A-2); §4.3 "mıknatıs yok" → **hafif mıknatıs** (S-02 kuralı, değerler S-07) |
| S-05 | Ufuk: plaka kenarı (±48 m) ile AP-019 silindirleri arasında sis bandı boşluğu → WP-074/075 halka dışı uzak zemin/ağaç halkası + yükseklik sisi, far clip ≥ 450 m; saray salonu içi yok, batı kairō kapalı-karanlık, TP kamera onları yutuyor; çatı altları/iç mekân ışıksız; tek yazar: global QualitySettings/URP = WP-075 kalite modülü (kademe × mod profili), kamera yalnız near/far + occluder; TP/FP gölge Yüksek-Sinematik 4 kaskat (ilk bölme 6–8 m), Performans 2/30 m; probe sıklaştırma | §8 tek-yazar kuralı API'ye işlendi (`CameraModeProfile`, `Changed`); §9 E-3/E-2/E-6 S-05 bulgularıyla teyitli; S-05 otomatik göz hizası taraması (1,6/2,2 m × 8 yön) E-listesini genişletecek |
| S-07 | Sink şekli uygun; değerler FeelRuntime `CameraModesFeel` (WP-073 B); görsel tepme ≠ nişan; mikro hit-stop her modda, FP tavan ≤ 35 ms, push-in yok; ölüm: TP geri çekilme + yan yörünge ~1,2 s, FP baş düşüşü (−1,2 m, roll 15–20°, FOV −10) 0,6 s → TP yörüngesi → mürekkep perdesi (`PlayDeathCamera(mode)` API S-01); FP namlu flaşı + izin ilk ~1,2 m'si + kovan görüntü modeli katmanında, sonra dünya izi `ViewmodelToWorld` noktasından | D-20 uzlaştırıldı (recoil kaynağı S-02, dönüş S-07; kick/bob/shake nişana dokunmaz); §5.2 ölüm beat'i S-07 sayılarıyla güncellenir (FP = baş düşüşü 0,6 s + TP yörüngesi; 🔴 4 bununla kapanır öneri); `ViewmodelToWorld` eklendi (Faz 1 FOV kilidinde özdeşlik) |
| S-08 | `Changed`, `Current`, FP görüntü modeli konumu (FP silah sesi kameraya yakın/geniş stereo); dinleyici tercihi | Yanıt: dinleyici konumu baş, yönelimi TP/FP'de kamera yaw'ı; API `CameraView.ViewmodelAnchor/Position/Forward` |
| S-09 | ui-settings.json alanları (CameraMode, FirstPersonFov, ThirdPersonFov, Shoulder, HeadBobScale + mevcut ReducedMotion) ve UI köprüsü (yüklemede + `UiSettings.Changed` → `ApplySettings`) S-09 N2 diliminde; Pause seçici + ayar paneli S-09; UiCursor `CursorIntent`'i tüketir (lockState dahil tek yazar) ve `CursorIntentHandledExternally = true` yapar; reticle TP/FP merkez + U12a yayılma FOV→px, geçişte gizli, FP/TP'de mürekkep paralaksı 0, FP'de oyuncu çevresi yaylar yerine kenar yayı; donmayan ekranlarda `AddLookBlock` S-09 | Faz B dilim 5 (ui-settings) S-09'a geçti; S-01 bakış alanlarını InputPreferences v3'e ekledi (B4) ve tek yönlü köprüyle servise bağladı |
| S-06 | (bekleniyor) baş gizleme yöntemi, FP kol/viewmodel, AimPitch | A-5 |
| S-03 | (bekleniyor) arka tehdit spawn/telegraph | A-7 |
| S-06 (A-5) | Tek gövde mesh'i (baş dahil); kemik ölçekleme yok; P-06 AP-022 başı ayrı SkinnedMeshRenderer olarak böler → FP'de o renderer ShadowsOnly; ara çözüm: göz baş kemiğinden ~0,10 m ileri + near 0,05–0,08, gövde tamamı ShadowsOnly. Görüntü modeli = URP kamera yığınında **overlay kamera** (kendi FOV), `FirstPersonView` katmanında ayrı kol örneği (aynı iskelet tipi, AP022_fp_* klipleri, soketler fp_socket_weapon_r / grip_l / fp_camera) — WP-071. `AimPitch` (+yukarı, ±60) + `AimYaw` ister. Duvar yakınlığı: mevcut `PlayerSkinnedVisual.PlayLower()/PlayRaise()`, sonda S-01'de, histerezis 0,6 m indir / 0,8 m kaldır | D-5 güncellendi: dilim 1 = FP'de gövde ShadowsOnly; görüntü modeli overlay kamerası + `ViewmodelAnchor` dilim 3 (S-01 altyapı, kollar S-06 WP-071); overlay kamera MainCamera etiketi ALMAZ (S-03: `Camera.main` dünya kamerası kalmalı). `CameraView.AimPitchUpDegrees` / `AimYawDegrees(body)` eklendi |
| S-03 (A-7) | Spawn görünürlüğü zaten kamera-bağımsız (`JudgeVisibility`/`IsOnScreen`, `Camera.main` viewport, %12 pay) → FP/TP'de kendiliğinden doğru; şart: `Camera.main` dünya kamerası. WP-076'da FP/TP arka spawn için asgari mesafe (≥ ~8 m). Telegraph süresi (DEC-029) değişmez; `ThreatHudFeed.RearDirection/RearIntensity/RearCount` + `RearAttack` mevcut, tüketiciler `IsInView(info.Position)` sorgular. A-7 ilke olarak kabul (≤ %35 arka pay, `RearThreat` ≥ 0,8 s, Special/Elite sessiz yaklaşmaz), sayılar WP-076 | §7.1 teyitli; `EnemyCombatEvents`'e mod eklenmez |
| C-00 (24.09) | FP FOV = yatay, 16:9 referanslı (Hor+), varsayılan 90°, aralık 70–110; TP omuz sağ, 2,8 m, omuz değiştirme tuşu olsun | Ayar/rig güncellendi (dilim 1); 🔴 1 ve 3 kapandı; omuz tuşu dilim 4 |
| S-05 (QUALITY-TIERS-V2 §3, `3812dc2`) | lodBias = max(1, taban × LodBiasFactor), taban 1,0/2,0/2,5 (Performans/Yüksek/Sinematik); gölge = min(kademe tavanı, mod değeri), tavan 45/2 · 60/4 · 80/4; farClip mod değeri (Performans'ta da 450 m). WP-074 yön belgesi `abc0f70`: E-1…E-20 dilimlere eşlendi (B3 occluder Roof/Wall = E-20, B6 göz hizası taraması) | Profil çarpanları TP 0,6 / FP 0,7 / Tepeden 1,0 (Yüksek → 1,2 / 1,4 / 2,0); gölge/far değerleri uyumlu; §11 Performans 350 m notu geçersiz → 450 m |

## 16 · Faz B durumu ve kanıt (S-01, 24.09)

Dilimler (dal `wp-070-camera-modes`): B1 `30fa406d` rig TP/FP + bakış + geçiş + merkez ışını · B2 `2fb7d976` omuz V/orta/R3 + TP ölü bant + S-05 LOD çarpanı · B3 `99f86361` FP overlay görüntü modeli + göz kalibrasyonu + duvar sondası · B4 `e476c7ed` InputPreferences v3 bakış alanları + tek yönlü köprü · B5 → S-09 (ui-settings, Pause seçici, imleç devri) · B6 `1acbed33` occluder politikası + `CameraOccluderClass` · B7 `98faa002` QaRunBot üç modda.

Kanıt (dev build, `PilotPerfProbe -ddPilotScene Run`, 40 düşman zorlanmış, 1920×1080, ısınma 8 s + pencere 30 s, `-ddCameraMode`; `~/Desktop/KADRO/context/projects/deaths-demolition/wp-070/evidence/modes/<topdown|tp|fp>/`, kareler `capture-gameplay-Run.png`, kare süreleri `frame-times-ms.csv`):

| Mod | ort ms (fps) | medyan | p99 / p99 süzülmüş | >33 ms takılma | ekranda / görünür düşman | ekran yüksekliği ort |
|---|---|---|---|---|---|---|
| TopDown | 16,55 (60,4) | 13,88 | 59,5 / 30,8 | 85 | 40 / 40 | %7,4 |
| TP | 14,35 (69,7) | 11,95 | 53,0 / 28,8 | 70 | 27 / 40 | %39,3 |
| FP | 12,14 (82,3) | 10,90 | 29,5 / 25,5 | 17 | 6 / 40 | %70,5 |

Okuma: bakış modları tepedenden ucuz (daha az görünür renderer); DEC-092 Yüksek hedefi (ort ≤ 16,6 ms) üç modda sağlanıyor, p99 ≤ 20 ms hiçbir modda (TopDown tabanı dahil) sağlanmıyor — kamera regresyonu değil, dev build + takılma tabanı (S-10 WP-078 release ölçümü karar verir). Mod profili global kalite değerleri (lodBias/gölge) henüz uygulanmadı (S-05 WP-075 kalite modülü) — sonda lodBias 2,0 sabitti. FP'de sondanın `08-occluder-fade` aşaması "FAIL (NO FADE)" beklenen sonuçtur (FP politikası None); sonda aşaması mod farkındalığı kazanmalı (sahip S-05/S-10). Kullanıcı verisi: yalnız oyun plist'i (Unity oturum sayacı) değişti, geri yüklendi, bayt eşit; profil/ayar dosyaları dokunulmadı.

Oynanabilirlik: Pause seçici (S-09) gelene kadar mod `-ddCameraMode topdown|tp|fp` ile seçilir (dev ve release; kayıtlı tercihi değiştirmez), ör. `open DeathsDemolition.app --args -ddCameraMode tp`. Açıklar: baş submesh (P-06/S-06 `HeadRenderers`) gelene kadar FP'de gövde ShadowsOnly; FP kolları S-06 WP-071; imleç kilidi UiCursor devralana dek rig; aim assist TP/FP yapışkanlığı, ateş yutma (IsTransitioning) ve dikey nişan S-02 WP-069 B1; `CameraModesFeel` değerleri + `PlayDeathCamera` S-07 WP-073; arka tehdit kancaları S-03/S-08/S-09.

