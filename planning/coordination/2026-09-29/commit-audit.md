# C-00 main hizalama denetimi — 2026-09-29

İncelenen main: `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`.
28.09.2026'dan beri 23 commit; `ff06d579` sonrası 6 commit. Commit içerikleri ve değişen yollar salt okunur denetlendi. Repo veya başka worktree'ye yazılmadı, fetch/test/build başlatılmadı. Bu rapor /private/tmp altında yetkili takip isteğiyle yazıldı.

Kayıt borcu: 18/23 commit WP'siz; 20/23 tarihli C-00 referansı olmadan; 23/23 S-XX sahibi belirtilmeden yazılmış. Commit gövdelerinde doğrudan kullanılabilir XML/log kanıt yolları yok. Dış KADRO kayıtlarında gerçek test XML'leri, sahip teslimleri ve dar teknik kabul kayıtları var.

Tüm aşağıdaki değişiklikler commit'te var ve DOĞRULANDI. WP mesajda yoksa eşleme öneridir. Dar teknik kabul, paket/oyun/release kabulü anlamına gelmez.

| TRT commit tarihi | SHA | Özet | WP / sahip | Koşuldu / kabul / durum |
|---|---|---|---|---|
| 28.09 18:46 | 0af8269a | Slot v4 + Player dokümantasyonu | Mesaj WP-080B; öneri S-01 | Exact CI: EM1461/1462,1skip; PM178/181,3FAIL. SORUNLU CI; doküman kabulü DOĞRULANMADI; WP-080B prompt dosyası bulunmadı. |
| 28.09 20:07 | e5888d47 | Bot spawn sayımı, run kapanışı, QA profil izolasyonu | Mesaj WP-076; S-03, QA S-10/profil S-04 sınırı | Exact CI GREEN: EM1461/1462+1skip; PM181/181. C-00 entegrasyon/kabul kaydı var. |
| 28.09 20:19 | 92eb636e | Perf guard scope/çıkış kodları | Mesaj WP-078; S-10 | S-10/C-00 kaydında syntax+5stub teknik kabulü var; ham stub sayısı bu denetimde DOĞRULANMADI. Canlı perf kabulü yok. |
| 28.09 20:28 | 2b574fb2 | Üç Türkçe buton etiketi | Mesaj WP-068; S-09 | Capture/JSON kabulü C-00 günlüğünde; commit'e özel XML yok. Tam UI kabulü değil. |
| 28.09 20:28 | 9b0a396e | Opt-in VFX capture agent | Mesaj WP-073; S-07 | Mesaj dev build exit0 der; doğrudan build yolu yok. Bu SHA capture koşusu DOĞRULANMADI. C-00 teknik intake kayıtlı. |
| 28.09 20:28 | eb2f8bb1 | Launcher suppression/shotgun kovanı | Öneri WP-073; S-07 | Compile PASS; CI PM180/181,TemasFAIL. Teknik intake kayıtlı; SORUNLU CI. |
| 28.09 22:40 | 19cb757a | Görünür TR UI/display fontu | Öneri WP-068; S-09 | Gerçek seçili XML38/38; kaynak12/12hash eş. Exact CI GREEN,PM181/181. Dar C-00 görsel/teknik kabulü var. |
| 28.09 23:10 | 7bcb94fd | Havuz guard/köprü döşemeleri | Öneri WP-074; S-05 | Kaynak37badf64: EM18/18,PM2/2 kayıtlı; Waterfallstall1 açık. Sonraki birleşik a17CI GREEN. Tam dünya kapanışı yok. |
| 28.09 23:10 | 4934e80f | NavMesh köşe dönüşleri | Öneri WP-076; S-03 | Orijinalf9c3d747 XML0/1FAIL,Waterfallstall12; tek başına kabul yok. 12c7215c ile birleşik kabul zinciri. |
| 28.09 23:10 | 12c7215c | Varılan dönüş/yürünebilir flank | Öneri WP-076; S-03 | Gerçek birleşik XML1/1PASS,Waterfallstall0;3kaynak hash eş. Parti intake kayıtlı. C1KnownOpen sürüyor. |
| 28.09 23:10 | a17cb73d | Temas testi sağlık tabanı | Öneri WP-069; S-02 | CompilePASS; CI GREEN EM1468/1469+1skip PM181/181. Runtime kök neden düzeltmesi iddia edilmez. |
| 28.09 23:44 | 78eae80d | AP-023v013/köprü geometrisi | Öneri WP-074; S-05/P-05 | XML26EM+1strictPM+1capturePASS;18/18hash eş. C-00 teknik kabul+FF/push var. Exact CI RED1; fullvisual/perf/releaseOPEN. |
| 29.09 00:56 | 030cf3f4 | Rolling inner spawn kotası | Öneri WP-076; S-03 | Gerçek XML35PASS/36,1optinskip;4/4hash eş. CompilePASS; dar teknik kabul/intake kayıtlı. |
| 29.09 01:23 | 122cf7d1 | Canonical checkout lease | Öneri WP-031; S-01 | CompilePASS; günlükte source/syntax+50stub kabulü; ham stub sayısı DOĞRULANMADI. |
| 29.09 01:50 | 69d46a07 | Sabit üç kapı coverage sondası | Öneri WP-076; S-03 | XML1/1PASS;222/2734=%8,11997. Sonda teknik kabul; ≤%7 hedefi bu koşuda FAIL; production hareketi yok. |
| 29.09 01:59 | df082391 | GameFeel gerçek UpperBody fixture | Öneri WP-069; S-02 | Günlükte kaynak dar XML1/1 kabulü; exactff06 CI sonrasında FAIL. CI kapanışı değil. |
| 29.09 01:59 | ff06d579 | DamageRegion namespace | Öneri WP-069; S-02 | CompilePASS; exactCIRED2 EM1473/1474+1skip PM179/181. GameFeel/DEC082FAIL. |
| 29.09 04:41 | c6fc439e | HUD Awake/Build initialization | Öneri WP-068; S-09 | ColdEN/TR XML2/2PASS,guardrestore0;4/4hash f9631e19 ile eş. Dar source/gate review; original combatCI kapanışı yok. |
| 29.09 04:42 | 92232235 | Üç production spawn kapısı/tek ağaç | Öneri WP-074; WP-076 bağımlılığı; S-05 | PostmoveXML1/1PASS;182/2734=%6,65691295,22reachable;5/5hash a0250899ile eş. Tam dünya/görsel kabulü yok. |
| 29.09 05:09 | b9eb99fd | TP Special bindings | Öneri WP-071; S-06/P-06 | ImportedboundaryXML1/1PASS;35/35blob1eb3d4d1ile eş;S-10technicalintake var. TPSpecialHOLD/FPOFF/rightgripvisualOPEN. |
| 29.09 05:31 | c34e48f8 | Coverage .10→.07 | Öneri WP-076; S-03 | Staticdiff/sourceintake doğrulandı; değişen test NOTRUN. Eski ölçüm mekanik olarak .07 altında. |
| 29.09 05:31 | 3c7b4872 | Coverage FAIL metni %7 | Öneri WP-076; S-03 | Staticintake; değişen test NOTRUN. ExactSHAdevpreviewbuild var;smoke/test/Player yok. |
| 29.09 05:47 | 3c0875e1 | Controls footer/ProfileResults TR | Öneri WP-068; S-09 | OffscreenEN/TRXML2/2PASS;9/9testhash eş;S-10conditionalaccepted intake. Fullscene traversal/visual kabulü yok. |

## Düzeltilen başlangıç iddiaları

1. "Son commitler CI koşulmadı" genellemesi yanlış: exact tarihsel CI XML'leri mevcut. Güncel3c0875e1 fullCI DOĞRULANMADI.
2. "Üç kapı uygulanmadı/≤%7 hedefi açık" dar production kapsamı için eski:92232235 gerçek postmove ölçümü%6,6569. .07 sınırına çevrilen test kaynağı NOTRUN.
3. "AP-023v013 kabul kaydı yok" dar teknik kapsam için yanlış:C-00günlük67/73 ve3gerçekXML var. Tam görsel kabulü OPEN.
4. "Tek oturum tüm alanları değiştirdi" commit yollarından çıkarılamaz:gerçek dış kayıtlardaS05/S03/S09/S10 izole kaynak/teslim zincirleri var. Doğrulanmış sapma:sahipliğin commit/planning'e taşınmaması.
5. 4934e80f tek başına FAIL;12c7215c ile birleşikPASS geçmiş ilk denemeye uygulanamaz.
6. SonS05dört-yolintake index.lock nedeniyle APPLY öncesinde durmuş;main'de yapılmış sayılmaz.
7. Planning/04 ileriye dönükhash→WP→sahip→kanıt→dar kabul eşlemesiyle düzeltilmeli; geçmiş değiştirilmemeli.

## Doğrulanan kanıt yolları

- C-00 tarihli kayıt: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/handoff-codex-2026-09-28/CODEX-GUNLUK.md` satır21–24,45,56,67,73,75,79,83,87.
- ff06exactCI: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/ci/ff06d5793e1e9b703fa128b4692c74f05a59b714/{summary.md,editmode/editmode.xml,playmode/playmode.xml}`.
- 19cb38/38: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/tr-visible-editmode.xml`; source+kabul `tr-visible-owner-handoff.json`.
- KöşeFAIL: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-waterfall-corner-20260928T174550Z-16301/playmode.xml`.
- BirleşikWaterfallPASS: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-waterfall-walkable-flank-20260928T193344Z-52790/playmode.xml`.
- Innerquota: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-inner-cap-20260928T214903Z-89531/editmode.xml`.
- AP023v013receipt: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-v013-compose-receipt-20260928.json`; XML'ler `s05-v013-compose-edit-20260928/editmode.xml`, `s05-v013-compose-strict-20260928/playmode.xml`, `s05-v013-compose-capture-test-20260928/playmode.xml`.
- Sabitset: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-fixed3-20260928T225155Z-10324/{editmode.xml,verdict.json}`.
- HUD: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/hud-init-r2-playmode.xml`; `hud-init-owner-handoff.json`.
- Productionpostmove: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-fixed-three-postmove-20260929/{coverage.xml,receipt.json}`.
- TPSpecial: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/tpsw-adapt-boundary.xml`; `s06-tpsw-adapt-review.json`; intake `/Users/Erdem/Backups/deaths-demolition/s10-s06-20260929T020857Z.jkxqybi1/integration-result.json`.
- .07NOTRUN: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-coverage-seven-message-static-20260929T021324Z/receipt.json`; intake `/Users/Erdem/Backups/deaths-demolition/s10-s03-20260929T023058Z.ra1f_u1o/integration-result.json`.
- UIfollowup: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/ui-followup-r3-target.xml`; `ui-followup-r3-target-receipt.json`; intake `/Users/Erdem/Backups/deaths-demolition/s10-s09-20260929T024730Z.xkputapk/integration-result.json`.
- Devpreview: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/preview-3c7b4872-r1/final-report.md`.
- S05intakeSTOP: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/handoff-codex-2026-09-28/S-10.md` satır138.

## Takip: Bugünkü korunacak teslimler ve dar intake değerlendirmesi

Bu bölüm git worktree/log/cherry/diff ile salt okunur hazırlanmıştır. Eski kaynak veya worktree değişmedi. Aktif main yine `3c0875e1e259cebae34e2cb496d03bb4e8dd9045` idi. S sahiplerinin yeni r2 işi paralel ilerlediği için yeni commitler bu denetim esnasında görülmüştür; aşağıdaki yeni kaynak doğrulaması runtime kabulü değildir.

### Öncelikli dört teslim

| Sahip / eski terminal | Tür ve varsayılan davranış | Bağımlılık / main'de bulunan | Kanıt sınırı | Intake önerisi |
|---|---|---|---|---|
| S-02 `bc49f3014275f7ff0803d0c8f8eab6135c8e7ae1` | Yalnız test diagnostic; production fix yok. GameFeel içinde `-ddGameFeelGateDiag` ile capsule/assembly/physics/time gözlemi. Parent eec genel iki fixture'a sayaç/log ekler; eşik/input/yield/assertion korunur. | Zincir `eecfe54c` → `6c6b759f` → `bc49f301`; toplam iki test dosyası. Başlangıç `ff06d579`; ilgili iki main dosyası başlangıç bloblarıyla eş. TP `733e8d50`/`5afc91dc` ayrı zincirdir ve capsule intake için gerekmiyor. | Gerçek XML1/1 PASS, mevcut GameFeel tek case. HUD-load UnityException1+NRE1 ignore edilmiş; PASS startup exception-free veya original full-CI neden kapanışı değil. Kaynak hash ve XML hash bağımsız doğrulandı. | Current-main r2 yalnız iki sahipli test dosyasını taşısın. Diagnostic olarak kayıt edilsin; GameFeel production bug veya CI kapanışı diye sunulmasın. |
| S-03 `bbf9049e62f83bbc10d61af61fb2e9f422a5179e` | `UNITY_EDITOR` hareket hook'u + `DD_WP076_C1_CONTACT=1` opt-in test. Flag yokken normal tüm istasyonlar/Waterfall ve mevcut assert akışı korunur. Release tarafında Move sonucunu local'e alma dışında davranış aynı. | Parent `183b1a53`; .07 kaynakları c34e/3c7 ile main'de patch-equivalent. Tek yeni bbf atomu iki dosya değiştirir. İki main preimage parent'la eş. | Gerçek C1 diagnostic XML1/1 PASS; spawned12/reached6/stall0. Hook hiç arm olmadı; actual Move/contact event yolu NOT EXERCISED. C1 KnownOpen kapanmadı. | İki dosyalı current-main r2 diagnostic intake; false-flag compile ve normal fixture korunumu doğrulansın. C1/geometri düzeltmesi olarak etiketlenmesin. |
| S-06 `ccb970b80f593ae20689dd15c053163c79abd7bd` | Tek AnimationTests.cs içinde legacy ar1 imported skin/grip gözlemi. `-ddS06PalmProof` yokken yeni test Assert.Ignore; Boot/observer gövdesinden önce durur, fixture setup/teardown yine çalışabilir. Production/Avatar/TP/FP varlık değişikliği yok. | `1eb3d4d1` → `8b12f158` → `ccb970b8`; 1eb source zaten b9 main'de. Yeni iki atom yalnız bir test dosyasıdır. Main preimage 1eb ile eş. | Parent 8b12 actual XML1/1 PASS; CPU nearest skin yüzeyi 45.427635 mm, anatomy/CP mapping UNKNOWN, rendered/contact proof değil. ccb güvenli kayıt değişiminin compile/runtime NOT RUN. | Eski 1eb yeniden alınmasın; yalnız terminal test-file current-main r2. Yeni `2ed77017b7a3fabcea8368e8f780d82e1f22a624` parent exact3c087; terminal dosya ccb ile byte-equal. Yeni r2 runtime bu denetimde doğrulanmadı. |
| S-05 `5c89a9995c89ae6868588841a76d7c673a48bef4` | Gerçek production değişimi: near horizon tabanındaki panorama reveal/AA. Üç production dosyası shader/material/builder + bir opt-in test gözlem dosyası. Yalnız horizon_near_v2 için property1, diğer sky rolleri default0. | `a0250899` → `ac7825bb` → `1ffa763c` → `5c89a999`; a025 zaten 922 main'de. Yeni üç atom toplam dört dosyaya birleşir; dört main preimage a025 ile eş. | Eski 5c89 exact source manifest4/4 ve gerçek Metal W60 capture XML1/1 PASS doğrulandı. Panorama seçimi kullanıcı kararıyla kayıtlı. Yumuşak gri geçiş/NW landmark footer/full appearance OPEN. Editor guard outOfScopeChanged1 ayrı S07 Player'a atfedilmiş, kendi scope restore0. | En somut ürün düzeltmesi; current-main r2 dört dosya intake. Yeni `f0fb8aa7cebe11173d31becd6b1744b1aae3587e` parent exact3c087; terminal dört dosya eski tested5c89 ile byte-equal. Yeni r2 compile/runtime bu denetimde doğrulanmadı. |

### Gerçek kanıt ve kaynak hash kontrolü

- S-02 kaynak CoreCombatSliceTests.cs SHA256 `7b4f9f9548e40b73abd01b257dcc933b6abfcaa8d8a854e565465a847531d86e` git bc49 blob'uyla eş. XML SHA256 `31129b55c4be2a2bce0d4e5b697ec8a9bea7a935a3a5010baaf490743473650d`, gerçek1/1 PASS. Yol: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-053d/evidence/gamefeel-capsule-provenance-r1-20260929/playmode.xml`; exact input/source/diff sözleşmesi aynı dizinde `input-manifest.json`, `source-contract-proof.json`, `source-proof-before-run.json`, `final-report.md`.
- S-03 XML SHA256 `e328b109ecee92404335a908654959f2442b92bd29a0329d47f3563dbca1c9ba`, gerçek1/1 PASS. Yol: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-076/evidence/s03-c1-contact-20260929T024748Z-88020/playmode.xml`; kaynak `input-bindings.json`, sourceSha bbf, `tested-change.patch`, `receipt.json`. Contact hook NOT EXERCISED.
- S-06 terminal ccb source SHA256 `517a165d8972bfaaee728d52e6bc49e1fe21ba0b39ed2c90bfee80dc30d15493` git blob'uyla eş. Parent XML SHA256 `30677348dc2c242cf371c1112185948eec7dd8455b92e8b5997b16e9ce08ea8b`, gerçek1/1 PASS. Yol: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-palm-one-8b12f158.xml`; kaynak/kayıt `s06-palm-one-source-receipt.json`, `s06-palm-safe-registration.json`. Bu XML ccb veya2ed'in yeni koşusu değildir.
- S-05 dört terminal source hash, `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-panorama-production-20260929/receipt.json` exactSourceManifest ile git5c89 bloblarında eş. Gerçek render XML SHA256 `f3fab0a7cadbfc474848e0cdb98003f6b8abaa3bb16ecef7ccdcecc03accf4e9`,1/1 PASS; aynı dizinde `render.xml`, `runtime-render-bindings.json`, `decision-record.md`, `render/world-v3/sugi-W60.png`. Source manifest sırasıyla builder `3e723bb038e28041edac45bb357a393048856e7c62022c11c9ee7531322f9af7`, material `b963673bb378169772979b61fbcd9fc578cd54f23eba4c1d9a410ce194635191`, shader `452632f621ab35582193c21effcae4c1463c6b451a5172c27ead21ca12625dfa`, capture test `43ee8ed700329e62f7baedfeab5c64958eba89a4a811db2e8ea993e0cc3e8887`.

### Bugünkü commitlerin patch-equivalence envanteri

`git log --all --not main --since='2026-09-29 00:00:00 +0300'` + her commit için `git cherry main <sha> <sha>^` kullanıldı. SHA'nın main ancestry'de bulunmaması, değişimin eksik olduğu anlamına gelmez.

| Grup | SHA'lar | Durum / taşıma gereği |
|---|---|---|
| Zaten main'de eş patch | e0d771a0,183b1a53,c0d89be9,1eb3d4d1,f9631e19,a0250899,eb0ad997,2527b629,dadaf433 | Yeniden alınmaz; sırasıyla3c0875,3c7b,c34e,b9eb,c6fc,922,ff06,df08,030cf ile temsil ediliyor. |
| Current-main r2 hazırlanmış | f0fb8aa7,2ed77017 | Source parent exact3c087; eski testedterminal dosyalar byte-equal; yeni runtime kabulü ayrı. |
| S02 capsule diagnostic | eecfe54c,6c6b759f,bc49f301 | İlgili iki test dosyası için tek bir current-main r2 scope kullanılmalı. |
| S03 C1 diagnostic | bbf9049e | Tek atom/iki dosya; event yolunun exercised olmaması açık yazılmalı. |
| S06 imported skin diagnostic | 8b12f158,ccb970b8 | Bir test dosyası; current-main r2 2ed bu terminali temsil ediyor. |
| S05 panorama | ac7825bb,1ffa763c,5c89a999 | Dört birleşik dosya; current-main r2 f0fb bu terminali temsil ediyor. |
| S07 normal ribbon observer | 2ab61632,82f82827 | İki diagnostic atom; passive observer + gerekli classic namespace import. Production launcherB3/perf kapısı değil. S07 sahibi ayrı değerlendirmeli. |
| S02 TP mount trace | 733e8d50,5afc91dc | Ayrı diagnostic zinciri; capsule intake bağımlılığı değil. Eski kaydı koru; current-main r2 gereksiz yere kapsamına ekleme. |
| Eski S02 diagnostic baseline | cd3a16fa | eecfe54c ölçüm zincirinin önceki realization'ı; branch eşitliği/son kaynak yerine kullanılmaz. |
| Eski TP Special family | 15441b55 | Frozen baseline için yeniden uyarlanmış1eb source b9main'de; eski hidden-parent zincirini main'e taşıma. |
| Eski refined fixed-set probe | e08ecabc | Production kapı değişimi a025/922 ile yapılmış; eski premove ölçüm kanıtı korunur. Yeni ürün fix değildir. |
| Cloud belge işi | ff57cee9,93b36d65,31f63eda,bb4781fa | Sistem promptu/disk rehberi/C00 promptu/operasyon yetkisi belgeleri. Ayrı belge intake; runtime paketlerle topluca merge edilmez. |

Öneri: sahiplerin current-main r2 kaynaklarını dar scope ile sıraya al; kaynak/test hash ve kanıt sınırları korunarak teknik kabul yap. Bu dört teslimden yalnız S05 doğrudan görsel ürün davranışını değiştirir. Diagnostic1/1 PASS'lar original GameFeel/C1/rightgrip veya release açıklarının kapanışı değildir.
