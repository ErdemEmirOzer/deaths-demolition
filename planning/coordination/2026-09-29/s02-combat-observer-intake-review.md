# C-00 — S02 combat CI r2 bağımsız intake incelemesi

**Dar diagnostic intake kabulü önerilir.** Commit `1ea58480a92822cbb3a8debbedd97881b822d711`, parent tam `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`; iki test dosyası, +475/-1. Ürün düzeltmesi yoktur. Yeni gerçek targeted PlayMode **2/2 PASS**; özgün ff06 CI prefix/global seed/RNG durumu yeniden oluşturulmamış, cause closure **DOĞRULANMADI**. İnceleme salt okunur; gerçek Git mutation / Unity / test koşumu başlatılmadı. Tek yazım bu `/private/tmp` raporu.

İnceleme zamanı 2026-09-29 04:35 UTC / 07:35 TRT; son gözlenen main `a8dc1687baedc23da6ffd05f575210ae747a62e4`. Kaynak koşumları S02 isolated source SHA1ea üzerinde yapılmış; bu sonuçlar birleşik main'e otomatik aktarılmaz. Bu anda iki kapsam dosyası 1ea ile main arasında farklıydı; C-00 atom intake'ini ayrıca gerçekleştirmeli.

## Kaynak ve fixture/asertion sınırı

İki dosyanın committed ve canlı S02 bytes'ı eşit:

| Kaynak | SHA256 |
|---|---|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/PlayMode/CoreCombatSliceTests.cs` | `d7707b3248eb3bee3c7a5eca82caf23ef85ffe9e08abae59bd3a9d21404bd83d` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/PlayMode/WeaponFeelSliceTests.cs` | `83bf01fef8ede613f47183249445e5b9bca588413862d2c2d25a65d7c927fdbc` |

Bağımsız kontrol, paired `BEGIN/END S02_COTIMED/S02_R2` bloklarını çıkarıp yalnız `out RaycastHit gateQueryHit`→`out _` normalization yaparak C# lexical tokenlarını karşılaştırdı. **Tüm Core dosyası8782/8782 ve Weapon dosyası2145/2145 token eşit.** Bu bağımsız lex scheme tüm dosya kontrolüdür; sahibi tarafından source-contract-proof içinde verilen yöntem özel target-method2321/293 token eşliğinden ayrıdır. Her iki kontrol observer dışında fixture/assertion/query/formül sırasının korunmasını destekler.

Orijinal GameFeel `Collider.Raycast` çağrısı bir adet olarak korunuyor; named out yalnız mevcut sorgunun sonucunu gözlemlemeye izin veriyor, ek physics query yok. Orijinal CurrentSpread getter, range expression, hedef gövde koridoru, input/teleport/ateş/aim,20s timeout, DEC0.8s pencere, maxSpeed>0.5m/s ve displacement>0.3m assertion'ları korunuyor. Ek SyncTransforms/Simulate/ClosestPoint/Random.InitState/Random.Range/RNG state assignment yok. Added code'da Transform/collider/physics/control getter'ları dışında pose/shape/physics setter yok. Aynı kare gözlemi motor/aim/pose update veya native sync çağrısıyla zorlanmıyor.

Default tanı kapalıdır: `-ddGameFeelGateDiag` ve `-ddDec082Diag` yokken yeni diagnostic snapshot, assembly hash okuma ve final diagnostic JSON logları üretilmez. Mevcut test body/assertion çalışır. Flagsiz yeni default koşum ayrıca yapılmış değildir; default-off kontrolü kaynak üzerinden doğrulandı. CLI kontrolü, local observer değişkenleri ve bazı ray başına clock değişken deklarasyonları ek maliyet oluşturabilir; tam bitwise scheduling eşliği iddiası yapılmaz.

Opt-in overhead sınırlı: GameFeel ilk head-predicate rejection en çok1 tam snapshot, ilk ray failure en çok1 tam snapshot, sonunda tek JSON log; loop aggregate scalar/counters ve ilk failure bulunana kadar mevcut query öncesi/sonrası saat getter'ları var. DEC start/first/firstThresholdLow/end(failure-finally dahil) en çok4 tam snapshot, her iteration scalar getter/counter; tek final JSON log. Her frame tam serialization/log yok. Mevcut gerçek zaman pencerelerine bu tanı yine ek CPU/IO/GC/scheduling maliyeti katabilir; hiçbir ölçüm historik counterfactual veya perf acceptance sayılmaz. Observer exception'ları kendi bloklarında UNKNOWN olarak kaydediliyor ve orijinal assertion outcome'u değiştirmiyor.

## Yeni r2b koşum ve identity

Kanıt dizini: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929`.

XML gerçek iki case: GameFeel id1233 seed2091649818,04:22:00–04:22:05 UTC / DEC082 id1548 seed1202421149,04:22:05–04:22:09 UTC. Total2 / Passed2 / Failed0 / Skipped0, duration9.062583s. Unity2022.3.22f1, gerçek wp-053d proje yolu, iki regex hedefi ve diagnostic CLI unity.log'da mevcut. Original Unity exit0, userdata restore0. Compiler CS0 / HudStateStore.Load exception0 / NullReferenceException0. Bunlar tam suite veya cause closure değildir.

`input-manifest.json` SHA `89e685cd74bff8dfc99103944094342ae22cad88ff335e424dde10a814569344` actual log args ve her iki record ile eşit. SourceCommit1ea, executionId `s02-wp069-combat-ci-main-r2b-20260929`; Core file SHA d770, fixture method SHA `dd91dd8ccb3891b27bd979a6616e95385e88ec96e26a00fbe341c0467fd22cd1`; DEC file SHA83bf, fixture method SHA `68be334309f009ffb8b86c3e99bad680d7362365d1f83e75ec09053430f1564a`. Record test ID/full name XML ile eşleşiyor.

GameFeel JSON `[DD-GATE-COTIMED]` unity.log line1339'daki raw JSON ile parsed **eşit**; DEC `[DD-DEC082-DIAG]` line1449 raw JSON ile eşit. Compiled test DLL SHA `58253126afd6c07e15c351d26c50e9cd25f274568ce953b24c72038161d79424`, runtime DLL SHA `22add08b24569bad4a54731784129148397878123c1b3104f430480f41734c69`; arşiv byte hashleri ve iki kayıt içindeki assembly identity eşit.

Input manifestte68 source path'in canlı bytes SHA ve committed1ea blobları bağımsız kontrol edildi: **68/68 eşit**. Run-result artifactHashes7/7 eşit. Protected old R1 evidence6/6, bc49 evidence7/7, ilk aborted attempt5/5, orijinal ff06 CI evidence10/10 hashleri eşit. Tarihsel kanıtlar yeniden adlandırılmadı.

## Aynı karede gözlenenler ve gözlenmeyen native sınır

GameFeel21 sample; rise/head/ray reject0/0/16, ready5. İlk head rejection yok. İlk ray failure sample2/frame24, mevcut üst Collider.Raycast=false; queryBefore/frame24 ve queryImmediateAfter/frame24, fixedTime ikisinde0.5799999870. Snapshot sequential getter koleksiyonudur; atomik physics state snapshot değildir. False Raycast output hit alanları kullanılmaz: queryHitValidity NOT_VALID_QUERY_FALSE, queryHit=null.

Head Sphere(center0,radius.155) / upper Capsule(center0,radius.26,height.72,direction1), collider enabled/active/trigger=true; boundReferenceEquals, parentZombieReferenceEqualsTarget ve IsChildOfTarget=true. Attached Rigidbody observed-null. Actual source/scene/hierarchy/runtime instance kimliği verilmiş; persistent prefab/native actor ID ile karıştırılmıyor. autoSyncTransforms=false / autoSimulation=true / FixedUpdate / queriesHitTriggers=true gözlenmiş. `internalColliderPose`, lastNativeSync, lastSimulationTime ve actualInside **UNKNOWN**. Bounds.Contains yalnız AABB testidir.

Head getter bounds-centerZ3.83542609 vs Transform worldZ3.78241706; upper bounds-centerZ3.88856649 vs Transform worldZ3.83555722: yaklaşık **0.053009m** sequential getter farkı. Bu kendi yeni koşumunun gözlemidir; eski bc49 ~1.057666m farkı yeni koşuma aktarılmaz. Bounds getter ile Transform farkı tek başına native sync nedeni veya fix ihtiyacı kanıtı değildir.

DEC229 sample, lowSpeed167, controlClosed0, lowest scalar0/max6.2/originalMaxSpeed6.2, displacement(1.58865166,-0.54534793,0)m. Pickup Namekuji; eski Raiju/RNG seçimi eşliği yok. Snapshots start/first frame612, firstLow674, end841. FirstLow motor/aim control=true, planar velocity0, CharacterController velocity.x2.647114, collisionFlags5, grounded=true. Getter sırası atomik değil, pre/post CharacterController.Move gözlemi değil. Blocker/contact identity ve native pose UNKNOWN; kontrol kilitlenmesi nedensel kapanışı yapılmaz. FirstLow snapshot ilk <=.5 threshold frame'idir; tüm pencerenin global minimum frame snapshot'ı değildir.

Unity Random.state raw read-only değerleri XML realized seed ile aynı kavram değildir. Original CI prefix/global seed/predecessor state UNKNOWN/NOT_PROVEN. Frozen3c before/after physical cause pair NOT_RUN. İki PASS bu sınırları kapatmaz.

## İlk çağrı ve importer recovery

İlk çağrı dizini `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2-20260929`: UTF1.1.33 OrderedTestSuiteModifier, project SetUpFixture için NotImplementedException verdi; gerçek test0, XML yok, own cancellation ile original exit143, restore0. pretest-abort-result.json/log/outcome/restore/metadata manifest korunmuş ve5/5 hash eşleşiyor. r2b yalnız unsupported `orderedTestListFile` flagini kaldırmış; kaynak/testler aynı. Gerçek GameFeel→DEC sıra XML timestamps ile gözlenmiş, orijinal tüm prefix replay edilmemiş.

Recovery manifest `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/importer-generated-meta/manifest.json`, SHA **db8e9be4d9c5262342f062bc709cd6391c38529ac15c55a1f78b0fac068229b8**. İçindeki tam12 meta için archived modified SHA ve archived HEAD SHA, canlı checkout bytes ve Git1ea HEAD bytes bağımsız kontrol edildi:12/12 doğru. Tam12 diff yalnız boş `rigImportErrors` yerine `Avatar creation failed: Transform 'Hips' for human bone 'Hips' not found` text eklemesi. Raw modified/head kopyalar duruyor; actual source exact HEAD'e restore olmuş. Hips/Avatar import sorunu **OPEN**; reconciliation importer/Avatar fix değildir. Recovery engine run NONE; current metadata durumunu biz değiştirmedik.

Final-audit sourceStatus empty, own lease false / own engine processes empty bildiriyor; source two-file SHA ve68 input/12 metadata bytes canlı yeniden doğrulandı. Editor snapshot04:21:46→restore04:22:12 touched0/verified1/extraKeys0/outOfScopeChanged0. Before-editor/after-editor byte-equal, before-player/after-run-player byte-equal, out-of-scope marker0byte. Guard özel içerikleri bu rapora alınmadı.

## Tam receipt/proof yolları

| Mutlak dosya | SHA256 |
|---|---|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/final-audit.json` | `cc6939c027f62bce5bc01a1f8572626cbe58f872a2192f19529466aed5024ceb` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/run-result.json` | `b38bc9f141516bb8a02d6f6bd7696532202b966db84bcbf428d1f807c24ce856` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/input-manifest.json` | `89e685cd74bff8dfc99103944094342ae22cad88ff335e424dde10a814569344` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/source-contract-proof.json` | `373ab12677e379aab844507d1db1c7936aaa783cb863240ae5699578d194e460` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/playmode.xml` | `dd8bf9b05a7abc8f1b74947845044bffa80a95a75b7f4463076517870b8b8f85` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/unity.log` | `db89119c3f3457331d9413f4eb2244a2465f204547911c6333770548e8fa1708` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/gamefeel-records.json` | `87370ce5ce50fb0cb5d22ca264ec52fd99856e740774fada5be6b1be218280a0` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/wp069-combat-ci-main-r2b-20260929/dec082-records.json` | `0b3b67a1e4402729835f33afd2a88498fcd3f8916b2a35b3c4f24b8a35b31870` |

source-contract-proof `noEngineRun/engineTestBuildRun=NOT_RUN` ve input manifest `singleCaseFilter`/“One PlayMode/no DEC082 run” alanları ilk statik/phase-A sözleşme kayıtlarıdır; sonraki iki-target gerçek CLI ve XML bunların efektif koşum olmadığını açıklar. Yeni fullCI manifestinde etkili filter/platform/diagnostic flags/head/prefix tek anlamlı ve güncel kayıt olmalı. Bu metadata eski alanlarını gerçek iki-target çalışmamış diye veya cause closure lehine kullanmayın.

## Birleşik altı atom main-CI önerisi ve gereken flags

Current combined candidate CI çalıştırması gerekli ve uygun; GameFeel/DEC082 recordları yararlı olduğu için diagnostic flags açık olarak run config'e kaydedilmesi önerilir. Yeni birleşik HEAD için taze input manifest üretin; eski r2b manifest SHA/DLL/prefix sonuçlarını taşımayın. Flags:

```text
-ddGameFeelGateDiag
-ddDec082Diag
-ddGateDiagSourceCommit <tam-birlesik-candidate-SHA>
-ddGateDiagInputHash <bu-CI-run-icin-yeni-input-manifest-SHA256>
-ddGateDiagExecutionId <bu-CI-run-icin-benzersiz-id>
-ddGateDiagSourceHash <bu-candidate-CoreCombatSliceTests.cs-SHA256>
-ddGateDiagFixtureHash dd91dd8ccb3891b27bd979a6616e95385e88ec96e26a00fbe341c0467fd22cd1
-ddDecDiagSourceHash <bu-candidate-WeaponFeelSliceTests.cs-SHA256>
-ddDecDiagFixtureHash 68be334309f009ffb8b86c3e99bad680d7362365d1f83e75ec09053430f1564a
```

Fixture hashes yalnız assertion/fixture parent3c normalizasyonu hâlâ eşitse aynen kullanılmalı; candidate file hashleri bu rapordaki d770/83bf terminal bytes değişmediğinde eşit olur. Flags yoksa yeni kayıt üretilmez; yalnız flags verilirse source/hash argümanları eksik alanlar UNKNOWN olur. Profile/UI/telemetry izolasyonu, snapshot→run→restore, own checkout/slot ve tek writer koşulları mevcut CI workflow'unda korunmalı. Unsupported orderedTestListFile yeniden eklenmemeli; gerçek XML case order/seeds ve run CLI sonradan bağlanmalı. Biz bu çalıştırmayı başlatmadık.

Tam CI new candidate gate sonucunu doğrulayabilir; özgün tarihsel failure cause kapanışını tek başına ispatlamaz. Diagnostic overhead aktiftir ve gerçek zaman ölçüm/prefix semantics ile birlikte raporlanmalıdır. Ürün fix/AD/perf/release kabulü **DOĞRULANMADI**.
