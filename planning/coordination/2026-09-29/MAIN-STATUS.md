> TARİHSEL (C-00 30.09): Codex dönemi kaydı, süreç benimsenmedi.

# Deaths Demolition — main koordinasyon kaydı

Güncelleme: 29.09.2026, ilk hizalama ve dispatch dalgası. Tek C-00: main `01a0eb42-07c6-7e42-b18b-3c8005556f83`.

Kullanıcı iki kaynak promptun okunmasını, buranın koordinatör olmasını, önce oturumlara görev dağıtılmasını ve bugünkü yararlı çalışmaların korunup alınmasını istedi. Son talimat: rutin onay beklemeden tam otomatik devam. Kaynak promptlar ve eski sohbet/worktree/commitler korunur. S-01–10 ve P-05–07 normal kullanıcı yetkili koordinasyon akışındadır; yeni sohbet açılmadı.

## Doğrulanmış başlangıç

- Local main/HEAD: `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`; fetch sonrası origin/main `ff06d5793e1e9b703fa128b4692c74f05a59b714`; ahead6/behind0. Bu değerler ilk snapshot'tır; aşağıdaki intake günlüğü ve canlı Git sonraki durumu gösterir.
- 23 commit için [WP/sahip/kanıt eşlemesi](commit-audit.md), [paket açıkları](package-audit.md), [CI denetimi](ci-audit.md) korunmuştur. Mevcut üretim bugünkü işlerle kaybolmamıştır; izole adayların yararlı atomları r2 üzerinde toplanır.
- Current3c0875 full-CI NOTRUN. ff06 RED: EM1473+1skip, PM179/181; eec recovery başkaSHA PM180/181. LatestGREEN a17cb73d. Eski sonucu yeniSHA'ya taşımak yasaktır.
- `92232235` postmove spawn182/2734=%6,6569/22reachable gerçek dar ölçümü; .07 sourceassert yeni koşum NOTRUN. UI coldstart/followup ve TP Special atomları main'de; FP OFF/TPHOLD/right-gripOPEN/f44ADRED sürüyor.
- Disk07:02TR22,69GiB; swap562MB; S05 W60 r2 koşumundan sonra slotları bıraktı. Yeni Unity işinden önce canlı disk≥15GiB/swap≤12GiB/slot+lease+guard kontrolü gerekir.
- Root index obstruction dar bakımla giderildi: [receipt](index-maintenance-receipt.json), korunmuş lock `/private/tmp/dd-main-index-maintenance-20260929-f865cogv/index.lock.quarantined`; originalownerUNKNOWN, unchangedHEAD/index. CI lease değişmedi.

## İş dağılımı

13 paket gerçek tool teslimiyle sevk edildi; ilk snapshot'ta 13 sahibi de aktif doğrulandı. Tamamlanma anında yeni sonuç/sonraki dar iş aşağıdaki kayıtla güncellenir. FazA ilk teslimi nihai kabul değildir.

| Sahip | İş | Prompt | Sohbet kimliği |
|---|---|---|---|
| S-01 | RC kapılarının onaylı DEC-091/093 ile uyumunu düzelt; bugünkü S-01 test/preview/diagnostic çıktılarının işe yarayanlarını kaynak ve kanıt olarak koru. | [Paket](../../work-packages/WP-079-RC-TOOLS-MAIN-20260929.md) | 01a08b7c-b2b3-7141-ae12-d6e223262d48 |
| S-02 | Özgün GameFeel ve DEC082 CI kırmızılarının nedenini kanıtla; bugünkü bc49f301 observer çıktısını koruyup güncel main üzerinde güvenli tanı/düzeltme teslim et. | [Paket](../../work-packages/WP-069-COMBAT-CI-CAUSE-MAIN-20260929.md) | 01a08b7c-eaab-7773-bbff-8566a8787a54 |
| S-03 | Bugünkü bbf9049e C1 observer atomunu koru; yararlı tanı kodunun main'e alınabilecek sınırını ve gerçek C1 collider nedenini belirle. | [Paket](../../work-packages/WP-076-C1-CAUSE-INTAKE-MAIN-20260929.md) | 01a08b7c-cfbc-77f0-be2f-e03b3fbcd5d4 |
| S-04 | Bugünkü A10/P1-P5 karar tablosunu koru; cloud/review-r1-b bulgularını mevcut kodla doğrulayıp uygulanabilir bug ile ürün kararı ayrımını çıkar. | [Paket](../../work-packages/WP-077-TREE-REVIEW-TRIAGE-MAIN-20260929.md) | 01a08b7c-c295-7c42-93d4-5bb396650345 |
| S-05 | Bugün tamamlanan dört-yol panorama teslimini kaybetmeden güncel main'e alınabilir temiz r2 teslimine dönüştür. | [Paket](../../work-packages/WP-075-TODAY-SKY-INTAKE-MAIN-20260929.md) | 01a08b7c-dc3a-72a0-9a1d-19da159a3873 |
| S-06 | Bugünkü 1eb3d4d1/8b12f158/ccb970b8 teslim zincirini ayrıştırıp zaten main'deki b9eb99fd ile farkı doğrula; yararlı opt-in gözlemi güncel tabanda sınırlandır. | [Paket](../../work-packages/WP-071-ANIMATION-TODAY-INTAKE-MAIN-20260929.md) | 01a08b7c-f98f-72e2-90c4-39dc6bf61208 |
| S-07 | WP-073 B3 launcher boşluğunu mevcut onaylı yönde kapat; bugünkü fire10 observer ve casing/tracer çıktısını koru. | [Paket](../../work-packages/WP-073-LAUNCHER-B3-MAIN-20260929.md) | 01a08b7d-04ad-7dd3-bc1b-87daf27e5454 |
| S-08 | Mevcut A/B dinleme kitini ve bugünkü ölçüm/provenans çıktısını koru; NOT READY nedenlerini teknik olarak kapatacak sınırlı dilimleri hazırla. | [Paket](../../work-packages/WP-072-AUDIO-KIT-CLOSURE-MAIN-20260929.md) | 01a08b7d-1267-7301-a310-cbc645937082 |
| S-09 | Bugünkü c6fc439e/3c0875e1 UI teslimlerini ve testleri koru; yükleme watchdog/hata modalı ve nihai UI kabul borcunu sınırlı bir dilimle doğrula. | [Paket](../../work-packages/WP-068-UI-CLOSURE-MAIN-20260929.md) | 01a08b7d-27a5-76f1-bddb-06e09033723d |
| S-10 | Bugünkü kabul/intake kayıtlarını koru ve güncel main için bağımsız kabul dalgasını hazırla; eksik A8 rapor üreticisini mevcut kabul planı çerçevesinde tasarla. | [Paket](../../work-packages/WP-078-MAIN-ACCEPTANCE-PREP-MAIN-20260929.md) | 01a08b7d-39a6-7672-a55c-5494070e8771 |
| P-05 | Bugünkü C1 slab kaynak yardımcı raporunu ve AP-023 v013 doğrulamalarını kaybetmeden manifestli devir seti hazırla. | [Paket](../../work-packages/AP-023-TODAY-SOURCE-ARCHIVE-MAIN-20260929.md) | 01a0e8f1-4d06-75c2-978a-da5dc9e78116 |
| P-06 | Bugünkü kaynak çıkarımı, 146 klip/203 SHA ve gripOffset/FP arms teslimlerini koru; eligible=0 anatomik seçim engelini somut kaynak eşlemesiyle çöz. | [Paket](../../work-packages/AP-022-ANATOMICAL-SOURCE-BIND-MAIN-20260929.md) | 01a0e8f1-2eaa-7810-a330-91c678272bd3 |
| P-07 | Bugünkü RAM aday/IRON semantik çıkarımını koru; SMG2 junction için authoritative UV kapasitesini kanıtlayıp geçerli RAM aday üret. | [Paket](../../work-packages/AP-021-UV-CAPACITY-MAIN-20260929.md) | 01a0e8f1-3ef3-79a3-9401-dde8ad2b65c2 |

Ana repo/main entegrasyonu yalnız C-00. Aynı dosyanın tek yazarı; her sahibin eski worktree/dal/kanıtı korunur. Current-main r2 atomları dışında eskimiş tüm dal taşınmaz. World production/bake30.09'a kadar duraklatılmış; bugün skyintake mevcut kullanıcı seçiminin kurtarılmasıdır. Kredi0, ürün/AD/MUST/HOLD kapıları korunur.

S04/S07 eski koordinatöre yönelen mesajlarını düzeltmeleri için yeniden hizalandı; eski sohbet yeniden arşivlendi. Diğer sahiplerin tamamına tekmainID tekrar bildirildi; eski019fbe65... sohbetine yeni iş/rapor gönderilmez.

## Otomatik takip

`deaths-demolition-main-koordinasyonu` heartbeat ACTIVE, bu main oturumuna bağlı, 10 dakikada bir teslim/izleme/sonraki dar iş/kanıtlı entegrasyon takibi. Aynı durum tekrar bildirilmez; değişiklik/tamamlanma/hata veya gerçekten kullanıcı müdahalesi gerektiğinde bildirilir. M4-R1 kabul kapıları tamamlandığında veya kullanıcı iptal ettiğinde durur. Otomasyon promptu bu dosyayı ve mevcut paketleri tüketir. Makine açık ve uygulama çalışıyor olmalıdır ([resmi scheduled tasks rehberi](https://learn.chatgpt.com/docs/automations?surface=app)).

## Intake kuyruğu ve kayıt

- S05 `f0fb8aa7`: parent3c0875, 4path eski5c89 terminaliyle blobexact; yeni W60 XML1/1PASS/guard0 bildirildi. Bağımsız hash/log/guard review sürüyor; main intake henüz yapılmadı. Gri eğri/NW landmark footer görsel açık.
- S06 `2ed77017`: parent3c0875, AnimationTests.cs eskiccb terminaliyle blobexact; defaultskip/optin yeni XML review sürüyor. Anatomik/right-grip kabulü değildir.
- S01 `e8dc425b`: parent3c0875, RC5path/Python67/67PASS/exit0 bildirildi; bağımsız intake review gerekli. integrate.sh status failure failclosed ekatomu ayrı hazırlanıyor.
- P05 TODAY-SOURCE-ARCHIVE manifest `e5b978ff...` 50link; source363/363 + consumer5FBX/42model tarihsel kanıt indekslenmiş, yeni runtime yok. Bağımsız hashreview sürüyor.
- P07 continuous IRON RAM receipt `9305b9b2...`, NPZ `6c23611d...`: narrowUV3/3 continuity iddiası; physicalReadyfalse/globaltopology/AD açık. Bağımsız hash/source immutable review sürüyor.

Her kabul kaydı sourceSHA/base/changedpaths/receipt/XML/log/hash/originalexit/guard/narrowlimit içerir. `integrate.sh` cleanpipe exit bug'u nedeniyle root ayrıca main/source `git status --porcelain` exit0 doğrular; tüm C00 untracked belgeleri önce açık dosya listesiyle commit edilir. Dirtygate bypass/force/reset/rebase/amend/add-A yok. Batch sonrası exact finalSHA compile ve main-ci, backup/cleanupdryrun/disk/LFS sonuçları ayrı kaydedilir.


## 29.09 ikinci kayıt — C00 dar intake ve sonraki iş

Dört atom resmi dry-run/apply/clean-exit0/sourceblobexact kapılarıyla main'e alınmıştır; [intake ledger](intake-ledger.json). Son codehead `641d1858126b19b80130f1f0a8543700f69e6706`; tüm yerel tagged kaynaklar ve eski adaylar korunur.

| Paket | Source → main | Dar kabul | Açık sınır |
|---|---|---|---|
| WP075/S05 | f0fb8aa7 → dd4e5bff | 4path panorama; r2W60 1/1PASS guard0 | Greycurve/NWfooter; AD/perf/worldREADY yok |
| WP071/S06 | 2ed77017 → 5f40e9c6 | Diagnostic1path; default1SKIP/optin1PASS | Source→imported→baked/contact UNKNOWN; FPOFF/TPHOLD |
| WP076/S03 | f802c784 → 51209880 | Editoronly actualMove/contact hook; 1/1PASS | Movinggroundcontact, C1causeUNKNOWN |
| WP079/S01 | 1466da4e → 641d1858 | Statusfailure failclosed2tools,14behavioraltests | Cleanpreflight+singlewriter; concurrency garantisi değil |

Bağımsız [S05/S06 review](s05-s06-intake-review.md), [S01/S03 review](s01-s03-intake-review.md), [statusatomreview](s01-integrate-status-review.md). Root birleşikstatus suite14/14PASS/exit0; Unity/fullCI/releasebuhead üzerinde henüzNOTRUN. S01e8RC5path67/67 evidenceverified fakat eksikmandatory M3/closure receipt ilePASS üretilebildiğinden HOLD; owner strictcontractchildfix sürüyor.

P05manifest e5b978ff50/50+9indexhash ve P07continuousUV9305b9b2/6c23611d11artifact+12input+514array whitelist bağımsızmatched; yalnız kaynakprovenans/darUV kabul. P06 [review](p06-source-intake-review.md):14artifact/canonicalFBX/Blend/CP/polygon/bind exact, sourceROI/anchor sourceinput intake-ready; Unity/posed/contact/AD değildir.

Sonraki offline işler sevkli: S03B8allocminimumcapacitybugprep, S04pincommand1/round+nullHearth atomları, S05sky residualcause, S06source→imported→bakedmap, S09OcakUIcauseplan, P05LOD/consumerdebtmatrix, P06TPspecialHipsimporterror source-onlytriage, P07RAMglobaltopology. S01strictRChardening,S02originalCIcause,S07B3implementation,S08kitprovenance,S10A8generator ilkdalga devam. Yeni Unity/Player başlatmaları C00 batchcompile/mainCI penceresi için koordineli; offlineişler sürer.

S07 yanlışcwd taslakları sahibiACK sonrası altıdosya/hash+patch ile korunmuş; iki trackedrootfile exactHEAD restore, dört untrackedoriginal kanıt klasörüne taşınmış. [Receipt](s07-writer-preservation-receipt.json). Ownerabsolutecwd'de devam; taslakproductionintake/PASS yapılmadı.

## 29.09 üçüncü kayıt — beş atom / birleşik kabul penceresi

S09 WP068 Boot watchdog `88c6f69c` → main `c29b410840f453758c70aec2eb0e74559fd885d4`,3pathblobexact, tag `wp068-boot-watchdog-main-20260929`; [bağımsız review](s09-ui-closure-intake-review.md). Kaynak4/4actualclock/asyncstall/recovery ve17artifactSHA/guard0 kabul; nativefailure/fallback/hardware Escape-B/actualPlayerQUIT/fullUI/perf açık. Beş source intake kaydı [ledger](intake-ledger.json) içinde.

C00 şimdi bu birleşik kaynak için canonicalwarmCI checkoutta compile→fullmainCI→releaseLZ4HC/smoke kapılarını yürütür; mevcut GameFeel/DEC082 causeUNKNOWN kapıları sonuçtan önce kapanmaz. Yeni fizikselowner koşumları batchpenceresi bitene kadar başlatılmaz; kod/static/offline kaynak işleri sürer.

Default post-integrate eski tam yedekleri keep3 ile silebildiği için automatic approval review reddetti; bu işlem çalışmadı. Güvenli alternatif: yeni `/Users/Erdem/Backups/deaths-demolition/c00-main-preserved-20260929` hedefi, backup-repo `--keep999999`, mevcut hiçbir yedek budanmadan doğrulama. Worktree cleanup yalnız dry-run/disk/LFS raporu ayrı; sonucu C00 harici devir kaydında tutulur.

Birleşik exactSHA doğrulama sonuçlarının kalıcı güncel yolu: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/c-00/main-20260929/`. Test/CI/build/smoke sonuçları bu klasörde ayrı manifestle bağlanacak; bu kayıt henüz koşuldu/PASS demiyor. Otomasyon önce bu klasörün latest-status.json dosyası varsa okumalı; yeni exactmainSHA ve canlı kaynak/status da doğrulanır.

## 29.09 dördüncü kayıt — yedi atom / ilk baseline freeze

S02 observer1ea→53f74cd7 ve S03 B8allocation0bf→c8b7adae main'e resmi dry/apply/strictclean/sourceblobexact ile alındı. [S02review](s02-combat-observer-intake-review.md), [S03review](s03-b8alloc-intake-review.md). Yedi atom ledgerde; eski tag/freeze a8dc korunur. İlk fullmainCI baseline diagnosticflagsOFF, tools/test.sh standardargv. Baseline fullsuite result/sourceassembly/XMLseeds sonra S02 causeplanına bağlanır; targeted2/2 tarihi failure kapanışı değildir. Main freeze'den sonraki üretim adayları ayrı ikinci dalgada toplanır.

S01RCee545 parentc29 strictmandatory/replay kapıları+87/87PASS doğrulandı fakat raw exit.txt=137 ile attempt.exitCode=0 çelişkisi sentetiknativeartifact yenidenhashlenince RC PASS; root independentcounterexample sonrası HOLD ve minimalfixowner'a sevkli. S10A850395 parent64149/49PASS fakat A3 unrelatedJSON/UnrelatedTestsXML ve A5 mandatoryNavMeshlogerror ile PASS; HOLD/mustfixownersevki. Bunlar main'e alınmadı, eski kaynak+kanıt korunur; PythonPASS gerçekRC/M4PASS değildir.

S04 RT6+RT10 7fe035e1/980f17ba13authoredcase; S09 RT9limitUI authoring; S07B3 offlinecode+physicalproofpending; S08source/kitdebt; S06importedmapping; P07RAMglobaltopology ikinci dalga. S01argsproposal7identityvalues+2bool ayrıtools-only, baselineoff bytepreserve, gerçekengine0. P05LOD51input/5FBX42model, P06Hips12matrixhashler rootmatched; Hips pointer defect observedS02snapshot, originalrunpayloadhashyok→INFERENCE. S02 localrehydrate2206/2206payloadSHA/OID/size, metadata/headclean; freshAvatarNOTRUN. P05consumerbindingprep/P06S06rawsourcecontract işlerinde aktif. Worldnewproduction/bake30.09'a kadar0, kredi0.

Korunanbackup a8dc exact: /Users/Erdem/Backups/deaths-demolition/c00-main-preserved-20260929/20260929T042941Z,3819LFS/6.37GBverified; yeni yedekte tümbytes/OIDkorunarak bağımsızAPFSCOWinodephysicalstorage düzenlendi, hardlink/symlink0, historicaldelete0; disk20.65GiBüstünde. Yeni freeze için ekGitbundle ayrı oluşturulacak; eski repo.bundle/BACKUP.json dokunulmaz. Safe postreceipt/cleanupDRYRUN/disk/LFS ve compile→mainCI→releaseLZ4HC+smoke tümoriginalexit/guard/status externalC00latest-status.json'da kayıtlı olacak. rootcleanfrozenSHA değişmeden koşum, noownerphysicaluntilSERBEST.


## 29.09 beşinci kayıt — 13 atom / ikinci kaynak dalgası

İlk exact877 baseline tamamlandı: compile0; EM1476PASS/0FAIL/1optionalSKIP; PM180PASS/1GameFeelFAIL/1optionalSKIP; release LZ4HC0/smoke0, footprint1127.1MB/5120MB. Input8992drift0, app150hash verified; kullanıcıverisi restore0/verified1/extra0/outscope0. Bu ilk koşunun kaynak ve kanıtları değişmez; PM RED ve gerçeknedenUNKNOWN. DEC082 bu koşudaPassed, tarihselnedeni hâlâUNKNOWN. Sonuçlar C00 external FINAL-REPORT.md ve verify-877a9aefb0bc/baseline-artifact-receipt.json içinde.

İkinci dalgada altı sourceatom officialdry/apply0/strictrootclean/sourceblobexact ile alındı: RC0cd→f794a92c; argv623→418daef0; RT10 968→9782f5e5; RT6 b5→2a87fab7; UI575→8c7c22c1; A8r6 58→5e71a508. Toplam13atom [ledger](intake-ledger.json); [ikinci dalga review](second-wave-intake-review.md). Yeni combinedmain compile/mainCI bu kayıt anında NOTRUN; ilk877 release yeniSHA diye etiketlenmez. Freeze tag wp078-main-salvage-r3-20260929 bu doküman commitine işaret edecektir.

S04 b5 gerçekEditMode39/39+13newcases/native0/guardrestore0verified1extra0outscope0/allsourcehashbyteexact. S09 575 gerçekPlayMode3/3/native0/guardverified/493dependency+3owned+2206payloadbeforeafterexact. C00 her iki originalXML/artifacthash/guardbyteequality ve actualprocessNone bağımsız doğruladı. İlkS04preflight1/UnityguardNOTRUN ayrı değişmezkanıttır; 2206payload/index/HEADpointer eşliği ve ownindexrefresh sonrası sourceclean, tarihselcachedstat mekanizması güçlüINFERENCE olarak kalır.

S08 022 provenanceutility HOLD: partialPCM16frame silentlydropsbytes ve sameXMLtwo-read inconsistenthash,9suitePASS bu iki açığı kapatmıyor; minimalownerfixactive. S07 güncel35e8 emissionbranch GCfixture+strictXMLhelper44offlinecases; actualPM27/build/capture NOTRUN/HOLD. S06 yeni482 fresh877 aynırawcaptureblob; standalonewrapper oldSHA/statusfailure/sourcehash gapsHOLD, S01safeS06argv extensionpending. Capture actualmatcher değildir, source19578/imported14472 TRIANGLEIDs; anatomicalmapping/rightgripUNKNOWN. S02 new62d boundedread-only observerR3 source2files reviewactive ve bu yeni finalmain üzerinde byteexactfreshtransfer gerekli; fullsameprefixnative diagnostic ayrıwindow. S05/P05 readonlyGUID/fileID/submesh/LOD consumer fixture authoring; P06 source/Avatar/correspondence support; P07 A/B localweld variants boundaryworse/linkcountunchanged, physicalReadyfalse, boundedfullstrip connectivitynext. S09 disabledactivePINnavigation bug childfix+EN/TR Axis/EventSystemregressions authoring.

10dkheartbeat ACTIVE; gerçekthreadIDs tablosu düzeltildi. Güncel ownerjob/window/cursor kaydı externalowner-register.json, next-source-wave.json ve HANDOFF.md. NativeUnity/Player yalnız explicit C00SERBEST tekpencere+ownlease+userdata guard; main frozenpipeline esnasında değişmez. Dünya üretim/bakepause30.09 ve kredi0 geçerli. Geçmişbackuppruning0; yeni kaynaklar append-only Gitbundle+preservedverified3819LFS bağıyla korunur. M4perf/soak/AD/importedcontact/sourcebinding kapıları açık; Python/testedsource!=actualacceptedbuild.
