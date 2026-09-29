# C-00 — S05/S06 current-main r2 dar intake incelemesi

Denetim zamanı: 2026-09-29 04:11 UTC / 07:11 TRT. İncelenen yerel main: **3c0875e1e259cebae34e2cb496d03bb4e8dd9045**. Bu inceleme salt okunurdur; test/build/fetch/commit/cherry-pick başlatılmadı. Tek yazım bu `/private/tmp` raporudur. KADRO kanıtları ve iki sahibi tarafından tamamlanmış fiziksel koşumlar incelendi.

**Öneri: f0fb8aa7 ve 2ed77017 için kapsamı sınırlı teknik intake kabulü uygundur.** S05 kullanıcı tarafından seçilmiş panoramayı açan üç ürün dosyası ile bir gözlemci test dosyasını içerir. S06 tek test dosyasında opt-in tanı sağlar; bayrak yokken SKIP doğru normal davranıştır. Ürün görsel/AD/performance kabulü, tam CI ve release kabulü bu kanıtların kapsamı dışındadır; DOĞRULANMADI. C-00 kabul kararı henüz bu denetimle verilmiş sayılmaz.

## Kaynak / koşum / kabul ayrı durumu

| Teslim | Kaynak ve kapsam | Yeni kaynak gerçekten koşuldu mu? | Kabul önerisi ve açık sınır |
|---|---|---|---|
| S05 / WP-075 `f0fb8aa7cebe11173d31becd6b1744b1aae3587e` | Parent tam `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`; dört yol, +246/-2. Git blob, canlı worktree dosyası ve manifest SHA eşit. Dört terminal dosya eski `5c89a9995c89ae6868588841a76d7c673a48bef4` ile byte-equal. | **EVET:** yeni r2 XML PlayMode 1/1 PASS, 04:03:07–04:03:12 UTC. Yeni W60 PNG ve aynı render olayındaki metadata, head `f0fb8aa7`, frame29. Orijinal Unity exit0. | Dar kaynak + tek W60 teknik kabulü önerilir. Gri yumuşak eğri geçiş ve NW landmark kartının dikdörtgen footer'ı **OPEN**. World READY / AD / perf / diğer backend / genel suite DOĞRULANMADI. |
| S06 / WP-071 `2ed77017b7a3fabcea8368e8f780d82e1f22a624` | Parent tam `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`; yalnız AnimationTests.cs +318. Git blob, canlı dosya ve manifest SHA eşit; eski `ccb970b80f593ae20689dd15c053163c79abd7bd` terminal dosyası ile byte-equal. | **EVET:** aynı hedefin iki yeni fiziksel modu. Default **0 PASS / 1 SKIP / 0 FAIL** 04:04:35 UTC. Opt-in **1 PASS / 0 SKIP / 0 FAIL** 04:04:54–04:05:01 UTC. İki orijinal Unity exit0. | Tanı ve flagsiz SKIP davranışı için dar teknik kabul önerilir. PASS gözlem üretimini doğrular; anatomy/source correspondence/rendered contact/fix kabulü değildir. FP OFF / TP Special HOLD / right-grip OPEN / f44 AD RED korunur. |

Her iki r2 yalnız bugünkü `3c0875e1` baseline'ında eksik kapsamı taşır. S05 önceki AA + capture metadata + panorama zincirini dört dosyanın birikimli farkında içerir; yalnız eski 5c89 son commitini çekmek eksik kalır. S06 TP ailesini yeniden taşımaz; kabul edilmiş `b9eb99fdf963dc271bebf560bab47d44c8892d9c` zaten main ancestor'dır. Intake için bu iki r2 commitin dar atomları tercih edilir; eski branch bütünlüğüyle değiştirme önerilmez. C-00 önce doküman/audit commitleri eklerse yeni head ayrıca kontrol edilmeli; bu raporun fiziksel koşumları birleşik yeni main SHA'sına otomatik taşınamaz.

## S05 — ürün davranışı ve gerçek r2 kanıtı

Çalışma ağacı: `/Users/Erdem/.codex/worktrees/wp075-sky-intake-r2/Deaths Demolition`.

Kanıt ana dizini: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929`.

| Committeki mutlak kaynak yolu | Doğrulanan SHA256 | Git blob |
|---|---|---|
| `/Users/Erdem/.codex/worktrees/wp075-sky-intake-r2/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Editor/WorldV5Sky.cs` | `3e723bb038e28041edac45bb357a393048856e7c62022c11c9ee7531322f9af7` | `4c0a3315705d72f5101e6f9c36b4e46987e414cf` |
| `/Users/Erdem/.codex/worktrees/wp075-sky-intake-r2/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Environment/World/V6/Materials/V5_Sky_horizon_near_v2.mat` | `b963673bb378169772979b61fbcd9fc578cd54f23eba4c1d9a410ce194635191` | `de549cb2277908af3f762e6a8b0f8f360892dc58` |
| `/Users/Erdem/.codex/worktrees/wp075-sky-intake-r2/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Shaders/DD_SkyPlate.shader` | `452632f621ab35582193c21effcae4c1463c6b451a5172c27ead21ca12625dfa` | `2aaab69b8fc5855029092ec63610ce2a04c45124` |
| `/Users/Erdem/.codex/worktrees/wp075-sky-intake-r2/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/WorldV3CaptureTests.cs` | `43ee8ed700329e62f7baedfeab5c64958eba89a4a811db2e8ea993e0cc3e8887` | `38ad76428e5c55d275c38e97a2b7a687323e3c55` |

`_BottomEdgeAA` ve `_BottomPanoramaReveal` shader varsayılanı0; builder yalnız `horizon_near_v2` rolü için1 atar. Mevcut near_v2 materyalinde ikisi1. Mevcut authored footer V0..0.16 üzerinden alpha smoothstep uygulanır; bağımsız yeni ürün genişliği seçimi yoktur. Gözlemci `DD_ART_CAPTURE_BINDING_RENDERED`/preflight gibi opt-in env girdileriyle çalışır. Ürün geometry/texture/scene/bake değişikliği yoktur.

Yeni r2 render logu gerçek yeni proje yolunu ve `DD.Art.Tests.PlayMode.WorldV3CaptureTests.SadakatKareleri_AP005C_4Kamera_2Oynanis_1080p` hedefini gösterir. XML'nin tek test-case'i gerçek Passed; sayı geçmiş rapordan alınmadı. Logda C# derleme isteği ve `Compiling Scripts` var, `error CS`0 / `Shader error`0. Library APFS cache reuse edilmiş; bu koşumda DD_SkyPlate için fresh import kaydı **yok**. Dolayısıyla yeni C# derlemesi ve yeni render vardır; fresh shader-import doğrulandı denemez.

Runtime JSON sourceHead/branch, preflight SHA ve PNG SHA bağını taşıyor. Gerçek sahne World_V6_Greybox; gerçek skybox DD_Sky_v3; near materyal V5_Sky_horizon_near_v2 ve DD/SkyPlate shader, SM_horizon_near mesh ile T_horizon_near_v2_Sheet texture. Tüm16 preflight payload canlı r2 dosya byte SHA, boyut ve `.meta` GUID ile bağımsız eşleştirildi. Near property block boş; iki near flag sharedMaterial'dan1. Kamera instance `-190076`, render target `-190080`, frame29, 1920×1080, Metal/Unity2022.3.22f1, UTC04:03:11.4752810. `SugiCaptureLodRecorder.OnWillRenderObject` kabul edilen olay; raw SRP callback1 / renderer callback601, accepted observation1. Camera.Render donmuş timeScale0 ile çağrıldığı için cameraEnabled=false kaydı tek başına eksik render kanıtı değildir; captureCompleted=true ve PNG hash bağı var.

PNG ayrıca görsel olarak açıldı: panorama görünür, üstte yumuşak gri eğri bant ve NW kart footer'ı görünür. Görsel kaynak/draw/per-pixel nedensellik kanıtı değildir. Built-in Skybox/Panoramic payload SHA bilinmiyor; GPU mip içeriği, draw sırası, per-pixel katkı ve tarihsel karşılaştırma counterfactual'ı **UNKNOWN**.

| Mutlak kanıt yolu | Bağımsız doğrulanan SHA256 |
|---|---|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/receipt.json` | `23a970fd5367fb883decbc07655ab62c74f2d0ab9e16692601961a23b290d776` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/source-manifest.json` | `701c9241067517c6d01e2713ee1ebd66621beccedb1bfaefd5b1188c1b6b9f1b` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/source.diff` | `85494b639ffb1d5900b8ee3d8e0a59624df23768d2d14aa1529b9e6ae034e67b` — tam parent→commit Git diff ile byte-equal |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/render.xml` | `a14f5dc649754e6dfc282c5dc59bc752eb1152c0be5b9fe2fc878304ee44449a` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/render.log` | `7534673e71c9b88926aa3753ceec98a3ae11545f512651019c281550b3d16f68` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/asset-preflight.json` | `8283d0bd2a2fba5ab14ec4a299176135b8064fec2f1f679747abee59bec63457` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/runtime-render-bindings.json` | `47d85a6774177b5bcfcf027a40d32f93330c4d53afce20002ce613588f889aef` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/render/world-v3/sugi-W60.png` | `37ce2f986451ab7a1dc7a095f66b945b506ce693343690974c632d38e96e8e63` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/immutable-before.json` | `83ce58d249c7d06009815bdd8ebac117518fd67d7e943db4e8a5b2a9ca57fe33` |

Receipt evidenceHashes içindeki8 dosya8/8 eşit; PNG ayrıca doğrulandı. Immutable-before içindeki **8.781 yol / 3.629.515.133 byte** canlı worktree'den bağımsız yeniden hash'lendi: drift0. Guard snapshot04:02:23 / restore04:03:16 editor scope touched0 verified1 extraKeys0 outOfScopeChanged0; before/after editor byte-equal, before-player/after-run-player byte-equal, kapsam dışı marker0byte. Guard dosyaları:

- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/guard-render-snapshot.txt`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/guard-render-restore.txt`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-intake-r2-20260929/guard-render/out-of-scope-changed.txt`

Own Unity ve Player acquire çıktıları slot.txt/player-slot.txt'de mevcut; receipt release bildirir. Kaynak/dizinlerin tümü salt okunur incelendi, mevcut slot state'ine yeni müdahale yapılmadı. Resource-monitor minimum disk23.682.436KiB (>15GiB), maksimum swap617MiB (<12GiB). Bu gözlemler perf benchmark değildir.

## S06 — default SKIP ve opt-in gözlem

Çalışma ağacı: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/worktree/deaths-demolition`.

Tam kanıt ana dizini: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929`.

Tek kaynak yolu `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/AnimationTests.cs`; SHA256 **517a165d8972bfaaee728d52e6bc49e1fe21ba0b39ed2c90bfee80dc30d15493**, Git blob **41d1d1943e10fd6c54ff664e30eb4cdaf1e4f677**. Ürün/Avatar/clip/FP/assets değişikliği yok. Diagnostic method line337; flagsiz guard line339–341. Bayrak yokken `Assert.Ignore` UI override, Boot, ilk yield ve observer oluşturmadan önce gelir. Fixture SetUp/UnityTearDown yine koşabilir ve MainMenu'ye dönebilir; sıfır fixture side-effect iddiası yapılmaz.

Default logunda `-ddS06PalmProof` yok; optin logunda proof path ve `-ddS06PalmSourceSha 2ed77017b7a3fabcea8368e8f780d82e1f22a624` var. İki log gerçek wp-032h proje yolundan aynı hedefi seçer. Default logunda AnimationTests.cs yeni import + C# derleme isteği var; iki log `error CS`0 / Shader error0. **Normal CI'da flagsiz bu case için 1 SKIP doğrudur; 1 PASS olarak gösterilmemeli. Bu yalnız tek case target koşumudur, normal CI'nin tümü koşulmadı.**

Optin proof UTC04:04:59.5571400, head2ed, frame25, LateUpdate order20000. Normal Boot→MainMenu→Selection→Run, actual ar1/Hinawa legacy Aim_0, stateTransition=false / param0, authored classic model count1, actualLoadedLegacyIdentityKnown=true, Special=false, FP=false, TPSpecialDefault=false. Runtime hash bulunan beş benzersiz payload ve `.meta` GUID canlı checkout ile bağımsız eşleşti: player_base.fbx, AP021_ar_1.fbx, Player_AP006.prefab, WD_ar_1.asset, AP009_player_A_idle_aim_v006.fbx. Avatar/body/LOD0 aynı body SHA `26cff12942a4b99714aa4400f898b2fccbc8ae20249c92a5af2a03b908696d15`; kaynak Aim clip SHA `deb612c88da8bf66f0bb7df330a48e87a6fddeb926b65598e02ee6a38f8fa327`.

CPU BakeMesh(false) renderer-local→localToWorld bir kez dönüşümüyle dünya metrelerinde en yakın tüm-LOD0 üçgen ölçümü: vertex21038 / bone77, triangle14472, ordered indices[12711,12714,12715], barycentric[0.4860231280,0,0.5139768720], exact ties2, distance **45.4290434718132mm**, baked snapshot SHA `2a310440916b5a7ee3829eab8ea0be82ee9b604eef0ad2241f7204e8a0410ca5`. Renderer enabled/active=true fakat isVisible=false. Bu anatomik sağ avuç seçimi değildir; görünür temas veya right-grip bug fix kanıtı değildir. CP11119→imported21038 split/weld/reorder, imported→baked ordinal correspondence ve anatomy mask **UNKNOWN**. Active clip state normalized time×duration yalnız clock estimate; source sample correspondence değildir.

P06 handoff `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/05-blender/validation/AP022-s06-bound-palm-input-handoff-v001.json` mevcut ve SHA256 `1a03336c75fe12899c71d354623ed7e03a0f5bd8df8dee1d9f8a8e629698b2dd` eşleşiyor. Sealed anatomical mask/ordered triangle/barycentric/rest-domain/source-to-Unity map/imported-to-baked map mevcut değil; source-mapping-ledger bunu alan alan UNKNOWN bildirir. Yeni source üretimi/parse/export/intake yapılmadı.

| Mutlak kanıt yolu | Bağımsız doğrulanan SHA256 |
|---|---|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/final-receipt.json` | **`a7f23f1ca0c151e7feb5bc99a64ac6ff2249e94b157e5dacd26f5a7011d31fcd`** — lease/review eklenmiş güncel receipt |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/source-receipt.json` | `b4e886b3adff40b2d4fa37faff194cfa0c59b49c432ebd04f2255371216072eb` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/candidate-from-main.diff` | `27b1d59e6ae2363667d58e8c0ce543f010e31d9806d5b1a938e97339f517a2ef` — tam parent→commit Git diff ile byte-equal |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/default.xml` | `54bb5bf35875c56b1d2279ff7d80951b1242b16bfebb6a17feab4948619f8c24` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/default.log` | `7e2a6eb5de2d81a746f1c2ae61b4f675b0c3513de645dcf43486a061936b621e` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/optin.xml` | `f99b8ce0c4a38e007a7f6febfd36f34501e8f59577e08a3f90cb9b3ef0e2f000` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/optin.log` | `7f677cbb1b39c14af9f51515a6e2bd36e89d45591835df8d2b056d8fec8671e4` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/optin-proof.json` | `cc25776b79b250835b0175d588a97514a7c20e82f594780f6695a6a14851e105` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/source-mapping-ledger.json` | `3cb4145166ee83655b8bd8b31b35ca0889152ddf5147d32d94d87ad3f31f10bf` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/r2-immutable-before.json` | `8c1bd9b08196672021f8bfd3534d942b3f8debc963b1039ab6aed742322315bb` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/lease-receipt.json` | `9cddf05f6c01a96cafcc6d11e98e0f5278f829ff02bdccd74f9f5d831c0920d5` |

Güncel final-receipt files listesindeki **18/18** dosya hashleri eşleşti. Kaynak receipt/phase-A `running/not yet` alanları tarihsel ilk aşama kayıtlarıdır; final-receipt ve gerçek XML daha sonraki sonucu taşır. Önceki final-receipt hash `d7d2f08f…`, lease ve bağımsız reviewer eklentilerinden önceki sürümdü; final-report.md artık güncel `a7f23f1c…` SHA'yı doğru bildirir.

Immutable-before içindeki **6.893 yol / 3.263.619.790 byte** canlı worktree'den bağımsız tekrar hash'lendi: drift0. İki editor guard snapshot→restore default04:04:11–04:04:37, optin04:04:38–04:05:05; restore0/touched0/verified1/extraKeys0/outOfScopeChanged0. Her iki before-editor/after-editor ve before-player/after-run-player byte-equal, iki out-of-scope marker0byte. Mutlak guard yolları:

- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/default-guard.txt`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/optin-guard.txt`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/ug-default/out-of-scope-changed.txt`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-animation-today-r2-20260929/ug-optin/out-of-scope-changed.txt`

Lease receipt acquisition/release çıktıları aynı owner `s06-animation-today-r2-37283`, Unity+Player held/released ve post-run own leases/processes boş olarak kayıtlı. Resource-preflight6 örnek; minimum22.504486GiB / maksimum swap593.31MiB. Bunlar fiziksel perf/acceptance ölçümü değildir.

## Önceki PASS ile yeni PASS ayrımı

S05 eski `5c89a999` XML hash `f3fab0a7cadbfc474848e0cdb98003f6b8abaa3bb16ecef7ccdcecc03accf4e9` / PNG `a738a264…`; yeni r2 XML `a14f5dc6…` / PNG `37ce2f98…`, yeni UTC/head/checkout/runtime identity. Kod byte-equal olması eski test sonucunun yeniden adlandırılması anlamına gelmez; burada yeni gerçek koşum vardır.

S06 eski `8b12f158` XML hash `30677348dc2c242cf371c1112185948eec7dd8455b92e8b5997b16e9ce08ea8b`, mesafe45.427635mm idi. Yeni r2 optin XML `f99b8ce0…`, proof `cc25776b…`, observer head2ed, mesafe45.429043mm. Eski ccb default skip kaynak kontrolü daha önce NOT RUN idi; **bu r2 ile gerçek flagsiz SKIP ve yeni kaynak derlemesi doğrulandı**. Mesafe farkı fix/iyileşme veya aynı-state causal karşılaştırma kabulü değildir.

## Dar C-00 kabul cümlesi önerisi

“WP-075 f0fb8aa7 dört kapsam dosyası ve current3c tabanlı yeni W60 1/1 PASS kanıtı için teknik olarak intake'e kabul edildi; gri geçiş/NW kart footer, AD/perf ve World READY açık kaldı. WP-071 2ed77017 tek opt-in diagnostic dosyası, flagsiz SKIP1 ve yeni optin PASS1 için intake'e kabul edildi; FP OFF/TP Special HOLD/right-grip OPEN/f44 AD RED ve anatomical correspondence UNKNOWN değişmedi. Birleşik integration main için tam suite sonuçları ayrıca DOĞRULANMADI.”

Bu cümle öneridir; C-00 kendi kabul kaydını ve integration head'ini ayrı oluşturmalıdır. Test counts explicit tutulmalı: S05 PASS1; S06 default SKIP1, opt-in PASS1. SKIP'i PASS'a veya bu2 hedef PASS'ı genel CI PASS'a toplamak yanıltıcıdır.

Denetim yöntemi notu: ilk `git status --short` mevcut LFS clean filter nedeniyle `.git/lfs/tmp` yazımı tetikledi ve sandbox izin hatasıyla durdu; repo değişikliği yapılmadı. Ardından `git show/rev-parse/diff-tree/diff/worktree list` ve dosya-byte okumaları kullanıldı. Main ve diğer worktree'lere yazım yapılmadı; guard özel içerikleri rapora taşınmadı.
