# C-00 — S03 B8ALLOC bağımsız kaynak incelemesi

**GO: 0bf4632c için sonraki batch'e dar source intake önerilir.** Değişim büyümüş spatial-grid bucket kapasitesinin crowd12↔40 churn'de tekrar küçülüp büyümesini engeller. Ürün tuning/query/neighbor davranışını değiştiren yeni bir bulgu yok. C# compile, üç yeni EditMode vaka ve runtime0B/frame/K8 kabulü **NOT RUN / DOĞRULANMADI**. GO yalnız kaynak atomu içindir; mevcut runtime/CI sonucu gibi sunulmamalı.

Commit: `0bf4632cdee9bbd267642710e515312b25894ac5`; **gerçek parent `641d1858126b19b80130f1f0a8543700f69e6706`**, branch `codex/s03-b8alloc-r2`. İlk callgraph/model reference51209880, teslim parent641d; base-verification bunu doğru ayırır. Git show ile doğrulanan scope tam iki dosya, +69/-1:

- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Navigation/CrowdSpatialGrid.cs`
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/EditMode/CrowdSeparationContractTests.cs`

Repo/main/source/KADRO evidence mutation veya Unity başlatılmadı. Yetkili offline modeli arşiv writer satırlarını bellekte çıkararak tekrar koştum; hiçbir evidence dosyası yazılmadı. Tek dosya yazımı bu `/private/tmp` raporudur.

## Semantik kontrol

CrowdSolver.Tick→Gather/Solve/Dispatch gerçek yolu her Solve'da `_grid.Configure(_count, Tuning.CellSize)`, Clear ve0..count-1 Insert yapar. `_next` ve `_cellKey` zaten growth-only yüksek kapasiteyi tutuyordu; eski `_bucketHead.Length != buckets`40→12 sırasında256→64 ve12→40 sırasında64→256 yeni array oluşturuyordu. Yeni `< buckets` condition bu yüksek kapasiteyi tutar. Bu, Configure'ın mevcut “only reallocates on growth” API sözleşmesini uygular.

Mask invariant korunur: `_bucketMask` yalnız allocation'da `buckets-1` atanır; bucket dizisi tutulduğunda eski daha büyük dizinin mask'i de tutulur. `BucketOf` mevcut `_bucketMask` kullanır; güncel count'ın hesaplanan küçük bucket sayısını mask'e ayrıca atayan kod yok. Diziler power-of-two olduğu için indeks mevcut retained dizi sınırında kalır. `_next`/`_cellKey` capacity≥count; kayıt/member arrays'i değiştirilmez.

Clear `_bucketHead.Length` boyunca her head'i-1 yapar; küçük count'tan eski item head kalmaz. Her aktif index Insert sırasında `_cellKey[index]` ve `_next[index]` güncellenir. First/Next aynı packed key ile exact filtre uygular. Büyük bucket tablosu unrelated hash-collision chain'lerini değiştirebilir, fakat bir exact-key grubunun reverse-insertion sırası değişmez. Her gerçek hücre için aynı member index dizisi elde edilir; solver'ın j>i pair filtresi,9 hücre traversal sırası ve maxNeighbors erken bitişi aynı kalır. CellSize/invCellSize, FloorToInt, PackKey, hash çarpanları, collision filtering ve query koduna dokunulmamış.

Bu semantik değerlendirme mevcut crowd safety ceiling40 ve mevcut packed-cell sözleşmesi içindedir; Python model engine coordinate conversion/native physics testi değildir. Mevcut16-bit packed-key alias tasarımı yeniden tasarlanmadı; bu atom yeni tuning veya oyun semantiği iddiası taşımaz. C# float coordinate conversion için eski/yeni kod zaten eşittir.

Tradeoff40→12:256 bucket yerine eski64'e düşülmez; **768B fazladan int payload tutulur**, Clear başına **192 ek int write**. Array/header/CPU/cache maliyeti runtime ölçülmedi. Allocation churn kaynağını kapatması CPU/perf bütçesinin geçtiğini kanıtlamaz.

## Test kaynakları anlamlı mı?

Yeni kaynak3 vaka: bir churn testi ve12/40 iki parametreli warm Tick vaka. Ölçülen bloklar doğrudan gerçek CrowdSolver.Tick/Gather/Solve/Dispatch çağırır; RegionNav cost-only veya no-op fallback ölçümü değildir.

`CrowdSolver_12_40_Churn_RetainsGridBuffers`40 pre-created fake ICrowdMember ile warm Tick yapar; üç grid buffer kimliğini reflection ile ölçüm dışında saklar.64KiB explicit byte allocation pozitif kontrolü var. Sonra8cycle gerçek Unregister(28member)→Tick12 / Register→Tick40 çalışır; exact12/40 count ve LastContactCount>0 booleans toplanır, assertion'lar ölçüm dışındadır. En sonda MemberCount40 ve `_bucketHead`/`_next`/`_cellKey` array reference identity eşitliği assert edilir. Eski kaynak bucket reallocations nedeniyle GC negatif constraint ve buffer identity kontrolünde başarısız olmalıdır; bu counterfactual **C# fiziksel olarak koşulmadı**, kaynak/modelden çıkarsamadır.

Steady warm12/40 vaka:3 warm Tick, separation contact>0 ön koşulu ve64KiB pozitif kontrol ardından200 gerçek Tick'i calling-thread GC constraint içinde çalıştırır. Static compact üyeler overlapped olduğundan separation çözümü etkin; ApplySteering fake üyelerde scalar/Vector3 alanlarına yazılır. ICrowdMember fake olduğu için gerçek CharacterController.Move, zombie attack/nav/world/skin binding/globalframe maliyetini kapsamaz. Tüm sahne hareketi sonucu yerine synchronous managed solver tahsis kontratı hedeflenmiştir.

Yerel authoritative UTF1.1.33 implementation ayrıca okundu:
`/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept/Library/PackageCache/com.unity.test-framework@1.1.33/UnityEngine.TestRunner/Assertions/AllocatingGCMemoryConstraint.cs`.

Constraint `Recorder.Get("GC.Alloc")` disable/flush, non-WebGL `FilterToCurrentThread()`, enable→action→disable yapıp `sampleBlockCount` kontrol eder; bu **allocation count**, byte budget/per-frame/natives ölçümü değildir. Pozitif kontrolün zorunlu PASS'ı unavailable/invalid recorder'dan vacuous no-allocation PASS alınmasını engeller. `UnityEngine.TestTools.Constraints` importu `.Not.AllocatingGCMemory()` extension için mevcut; bütün Is kullanımları fully qualified olduğundan NUnit/Unity Is isim ambiguity eklenmemiş. `System.GC.KeepAlive` kontrol/member referanslarını yaşatır. C# compilation henüz yapılmadı; static API uyumu compiled PASS gibi gösterilmez.

## Bağımsız offline model yeniden koşumu

Arşiv model `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/grid-model.py` okundu. Model source bytes'ının eski source'ta yalnız `!=`→`<` replacement olduğunu assert ediyor.0member için gerçek Tick early-return modelleniyor. Positive/negative coordinates, dense compact crowd, unrelated hash collisions ve counts[40,12,40,7,1,16,17,39,40,12,40] kullanıyor; aynı PackKey/hash/chain ordering algoritmasıyla ordered cell results karşılaştırıyor. Python arbitrary-int hash ve signed C# low31-bit maskeleme mevcut mask domaininde aynı düşük bitleri üretir; runtime allocation ölçümü olarak yorumlanmaz.

Bağımsız rerun, son iki artifact-write çağrısı bellekte çıkarılarak stdout sonuç üretti. Model çıktısı arşiv JSON ile **tam dict-equal**:

| Model doğrulaması | Sonuç |
|---|---:|
| Veri kümeleri |50|
| Model frame |550|
| Sıralı hücre sorgusu |118800|
| Eski/yeni query sonuçları |eşit|
| Warm40 ardından8cycle12↔40 eski bucket array allocation sitesi |16|
| Eski int payload alt sınırı |10240B|
| Yeni model bucket allocation sitesi |0|

Python clear/list işlemleri kendisi allocate eder; bu bilinçli model code'udur. Model sonuç0, Unity managed GC0 veya gerçek0B/frame değildir. Model C# solver ResolvePair çıktısını, native allocations veya profiler GC latency'yi ölçmez. Kaynak semantik inceleme + ordered query eşliği yalnız değiştirilmiş grid davranışını dar doğrular.

## Base ve hash kanıtları

20 live source file SHA + delivered Git blob bağımsız doğrulandı: **20/20 eşit**. İlk ref512→gerçek parent641d için20 incelenen source blob bağımsız aynı; Git diff tam yalnız `tools/integrate.sh` ve `tools/qa/tests/test_integrate_status.py`. Böylece eski sourcegraph/model reference parent gecikmesi bu20 yol için çözülmüş; actual base641d ayrı kaydedilir.

`artifact-hashes.json` listesindeki byte boyutu/SHA11/11 eşit, offline rerun sonrası da arşiv değişmedi. Source manifest teslim dosyaları:

| Kaynak | SHA256 | Git blob |
|---|---|---|
| CrowdSpatialGrid.cs |`09d03c250826164bea7a74b2744caac1e548c80aafdb7320434342ef18b05b83`|`9edaf13776bfd5d21b3bc728f67a8d95178c86ed`|
| CrowdSeparationContractTests.cs |`1ff639a4126080a2b1979ee2961550050fcb1d25cc59cffe4aec23c25555e620`|`4f7521c4bd47eafe68ff1c06321e494882ddb013`|

Tam kanıt yolları ve doğrulanan hashes:

| Mutlak yol | SHA256 |
|---|---|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/receipt.json` |`225ac30d743cc4142d25b07d7bac3529262aab0b3ebac435c9401a878e85ee46`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/source-manifest.json` |`02542bb661e6a35043170a8b5ea375cb0ccd4e6ca495791c5c19ba99861fbee7`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/base-verification.json` |`9e339651388df49f5398a2696c6839a1dad0569ff4631ad013cfcbf7992e1921`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/grid-model.py` |`2a9d762e80ad5c7f0fafe6ab53d0fb3def3ae803b26296422ee6572f2c8966f3`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/python-grid-equivalence.json` |`6579bb55b751ad238197a6e8f40c9049c5867e44b2577ba102738cd91be927d9`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/measurement-plan.md` |`9647564ef794ce80a88ecfd2a7b6eb97abfe32ac906dd4019efa12c341c792d6`|
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-b8alloc-offline-20260929T042518Z/source-change.patch` |`b173b7e9fdf29f2f965f6851f31f535c4049f0cbcff30509acf26e7844371ffb`|

Receipt diffCheck exit0/PASS; C# compile/tests NOT RUN, XML/log null, no Unity/Player0, slot/lease0, credit0 ve clean bildirir. Biz whole worktree git status/LFS clean filter çalıştırmadık;20 source byte doğrulaması ve commit-tree kapsamı kullandık. C1 f802c784 branch/evidence korunmuş; main integration bu incelemede yapılmadı.

## Sonraki batch kapsamı

Kaynak intake önünde yeni must-fix blocker bulunmadı. Sonraki candidate batch'te exact source hashes ve üç yeni EditMode case dahil mevcut CrowdSeparationContractTests suite'i C# compile/targeted tests ile doğrulanmalı.0B/frame/K8 acceptance için gerçek live12/40 movement, pooled refill/BeginRound,40+6ragdoll+2gel+1chain composed scene ve valid recorder/source/guard window ayrıca gerekir. Allocation/perf penceresinde opt-in C1/GameFeel/DEC diagnostic logging etkin olmamalı; onların serialization/IO gideri bu testin hedefi değildir.

Round-first-spawn JsonUtility config copy, per-life Transform[] skin/tier binding, RegionNavRouter delegate lowering ve native/runtime global allocations açık kalır. Yeni Grid fix bu ayrı giderleri kapatmaz; ölçüm veya norm/threshold değişikliği yapılmadı. Kullanılacak kabul dili: **“B8ALLOC source atom GO; grid count-churn allocation sitesi kaynak/model düzeyinde giderildi; compile/new tests/runtime allocation/K8 sonucu DOĞRULANMADI.”**
