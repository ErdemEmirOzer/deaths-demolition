# Deaths Demolition — Paket hizalama ve bugünkü çıktı koruma danışmanlığı

Tarih: 2026-09-29. Yerel main okuma tabanı: `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`.
Bu denetimde yalnız bu rapor yazıldı. Fetch, test, build, Unity/Blender, entegrasyon, kaynak, worktree veya lease mutasyonu yapılmadı. Git status okuması LFS clean filtresinin sandbox dışı tmp yazısını reddetmesiyle sonuçsuz kaldı; tekrar denenmedi. HEAD salt-okuma komutu hâlâ 3c0875e1 döndü.

## Paket tablosu

| Paket / sahip | Kanıtlı güncel fark | Kalan / sınır |
|---|---|---|
| WP-068 / S-09 | HUD cold-start c6fc439e ve Controls/TR ödül metni 3c0875e1 main'de; hedefli 2/2 + 2/2 kanıtı | Hedefli/offscreen kanıt tam ekran veya nihai UI perf/AD kabulü değil; eski iki CI sözleşme hatası kapanmış sayılmaz |
| WP-069 / S-02, P-07 | AP-021 v003.1 main'de | v003.2 fiziksel model/soket/ADS8 adaylarının exact intake ve kabulü açık; launcher self-damage ve smg2 insan oyun testi açık |
| WP-070 / S-01 | Kamera kodu ve 070I/J main'de | FP death camera, nihai mod kabulü ve global perf açık; eski dev p99 nihai kabul değil |
| WP-071 / S-06, P-06 | FP arms v2, gripOffset re-export, shotgun ve Special klipleri üretim tarafında mevcut; AP022 manifestinde gerçek 146 klip; dar TP Special kod b9eb99fd main'de | Büyük fpik16@61613885 zinciri main'de değil; f40 HOLD/default OFF/f44 visual RED; TP Special sağ tutuş, FP kesik önkol, ar2/smg2 rest parmak kusurları açık; AR/smg1 TPv3 kapalı; loop/bolt/locomotion/ms kabulü açık |
| WP-072 / S-08 | Silah A/B TD +1.684 / FP +1.529 dB mevcut | NOT READY; eski kuş proxy full5/6 ve _06 +2.8568 FAIL; .35 insan dinlemesi yok; izole kuş/eşzamanlı bed yeni tanım sonucu UNKNOWN; güncel R1 dense40 kanıtı bulunmadı |
| WP-073 / S-07 | fire10 40 ilk +8 etkilenen gerçek PNG ve AD; casing eb2f8bb1 main'de | B3 Launcher/ExplosionDirector açılmamış; tier-ms yok; tracer/port/default görsel PASS açık; yeni observer dalı ayrı intake/kabul |
| WP-074 / S-05, P-05 | v013+köprü main'de; 92232235 üç spawn taşınması; postmove 182/2734=%6.6569, 22 reachable | C1 KnownOpen; Waterfall muafiyeti kaldırılmış ama global stall0 iddiası yok; LOD3/2/2, göz hizası/occluder/nihai world perf açık; son sky dört-yol intake lock nedeniyle durdu |
| WP-075 / S-05 | DEC100/101/102 uygulanmış kod/kademe verisi; bugünkü panorama kaynak adayı mevcut | Nihai C8/9 hücre ve HF/görsel artıklar açık; sky adayı main'de değil, ayrı r2 işi sevk edilmiş |
| WP-076 / S-03 | Spawn7% hedefi ölçümle sağlanmış; c34e48f8/3c7b4872 source threshold .07; e5888d47 sayım/Progress main'de | .07 değiştirilmiş source için yeni runtime/compile NOT RUN; C1/AI B8 ve FINAL bot/alloc açık |
| WP-077 / S-04 | K10a/K10b/audit main'de; A10 57/57 geçmiş kanıt | P1–P5 kararsız; Mıknatıs +0.5m ürün kararı; gerçek XP/s; DEC008/meta doğrudan güç gerilimi |
| WP-078 / S-10 | 26.09 KISMİ rapor A0/A1/A7/TD3 round içerir; önceki başka SHA TP/FP2 round | Tek FINAL commit A0–A8 yok; A4 perf/A5 soak ölçülmemiş; A8 generator yok; perf_report ayrı schema |
| WP-066 / S-01 | Windows 23.09 release build exit0/522.1MB/dirty mevcut | Exe-run/smoke/input/perf/save doğrulanmamış; ürün/kanal/yasal/sürüm kararları açık |

## Düzeltilecek iddialar ve kaynaklar

- Spawn <=7 açık ifadesi bayat. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-fixed-three-postmove-20260929/summary.md:3–11`: actual postmove 182/2734=.06656912948061448; original test1/1 (.10 assert), target .07 bağımsız raporlanmış. Receipt'in beş kaynak SHA256'sı canlı main ile MATCH; binding.log/coverage.log/coverage.xml/preflight.json hashleri MATCH. Pre-move 183/2734=.06693489 ayrı kanıt. Main `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/EditMode/SpawnGateCoverageTests.cs:32,136` artık .07.
- C1 ve Waterfall aynı açık durum değil. Main `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/PlayMode/V6AiRoundTests.cs:37`: KnownOpen yalnız C1. S05 handoff:103 Waterfall strict stall0/C1 reached4 ve R4 TeaHouse observer stall1 gösterir; global stall0 değil.
- AP022 teslimleri yok sanılmamalı. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/README.md:3–9`:146 clips/203 SHA/204 files, TP right-grip ve FP cut-forearm MUST açık; f40 HOLD/default OFF. `05-blender/validation/AP022-README-history-20260928.md:60–67`: per-weapon gripOffset ve FPv2/FPv004b/shotgun/Special üretimleri. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/handoff-codex-2026-09-28/S-06.md:124–152`:f40 Player8 PNG, f41 lmg31EM/9PM, f44 preview2 PNG, f45 attribution; f40 HOLD/f44 AD RED. Main PlayerSkinnedVisual.cs:353–373 flags OFF, beş TP set ON. Üretim, Unity intake ve görsel kabul ayrı.
- Fire10 hiç koşulmadı ifadesi bayat. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/handoff-codex-2026-09-28/S-07.md:82–98`:40+8 capture, PM casing3/3, guards restore0, B3/ms açılmadı. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-019r2/evidence/wp073-fire10-3/review.md:4–16`:sg1 FP visibility/tracer/port/default PASS açık.
- Audio kit NOT READY. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-072/listening/OKU-BENI.md:9`; S08 handoff:124–128 yeni isolated/concurrent tanım feasibility, actual result UNKNOWN. Main `planning/work-packages/WP-072-AUDIO-V3-STANDARDS.md:133` dense40 gereği. Eski WP065 dense40@1df0bf0 R1 yerine kullanılamaz.
- K10b kararları tamam değil. Main `planning/work-packages/WP-077-K10B-BALANCE-AUDIT.md:68–80` P1–P5; `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-077/KARAR-P1-P5.md:3–5` karar bekler; main `planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md:205` Mıknatıs ürün kararı.
- WP078 hiç koşulmadı denemez ama acceptance yok. `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-078/evidence/r1-820a885f/R1-KABUL-RAPORU.md:4–24` KISMİ. Main plan:122 A8 `dd-wp078-acceptance-v1`; tools/qa/wp078_perf_report.py:77,91 yalnız `dd-wp078-perf-report-v1`/wp078-perf-report.json. tools/qa/perf-targets.json:13–15 9cell*3=27 windows/hepsi gate; eski plan73/75 22/28 rapor ve24 window bayat.
- Windows build yapılmadı ifadesi build ile exe-run'ı karıştırır. Main `planning/work-packages/WP-066-RELEASE-GOVERNANCE.md:95–96`:build var, exe-run/smoke yok.

## Devir ve kimlikler

Combat özgün CI kırmızı/model/ADS: WP069/S02; P07 AP021 exact accept → S02 intake → S07 yalnız etkilenen VFX capture. FP source/retarget: WP071/P06 → S06; flags OFF/HOLD korunur. Downed/GettingUp kullanıcı ertelemesi korunur. B3 Launcher ve VFX tier-ms: WP073/S07. Audio R1 dense40 ve insan dinleme: WP072/S08. C1/LOD/göz hizası/world: WP074/S05/P05; atmosphere/perf WP075/S05. K10b DEC değişiklikleri: WP077/C00 kullanıcı kararı → S04/S02. FINAL exact SHA kabul: WP078/S10; özgün FAIL ve quiet/SERBEST koşullarıyla.

Plan kayıtlarında üst sınırlar WP078/DEC102/REQ158/AP023. WP079 docs/SYSTEMS-INDEX:55 ve tools/README:21'de mevcut. WP080B main0af8269a commitinde mevcut. WP079/080 yeni iş için tekrar ayrılmaz; bugünkü işler mevcut paketlerinin altında.

## S05 panorama seçimi ve dört-yol current-hash sınırı

Kesin kayıt: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-038m/evidence/s05-sky-panorama-production-20260929/decision-record.md:3–9`.
Kullanıcı cevabı: “Uzak gökyüzü panoraması görünsün”. Kayıt doğrudan userMessage `01a0eb1f-168b-7432-8843-1bf75b75d15f`, turn `01a0eb1c-cf7e-7373-b16d-079ed0814744`, C00 `019fbe65-4b53-7421-a61c-aa48a8d26ae4` ve question call `call_UazhbKrEVcCRUh8Hhj50sq1u` gösteriyor. Bu denetim kayıt/provenansı okudu; sohbet mesajını yeniden çekmedi.

Zincir: a025 kabul edilmiş world → ac7825bb AA → 1ffa763c metadata → 5c89a999 panorama reveal. Owner checkout HEAD hâlâ5c89a999. Receipt'teki dört kaynak fiziksel owner dosyası ile MATCH, canlı main ile dörtte dört DIFF:

| Yol sonu | Tested/owner SHA256 |
|---|---|
| Editor/WorldV5Sky.cs | 3e723bb038e28041edac45bb357a393048856e7c62022c11c9ee7531322f9af7 |
| Environment/World/V6/Materials/V5_Sky_horizon_near_v2.mat | b963673bb378169772979b61fbcd9fc578cd54f23eba4c1d9a410ce194635191 |
| Shaders/DD_SkyPlate.shader | 452632f621ab35582193c21effcae4c1463c6b451a5172c27ead21ca12625dfa |
| Tests/PlayMode/WorldV3CaptureTests.cs | 43ee8ed700329e62f7baedfeab5c64958eba89a4a811db2e8ea993e0cc3e8887 |

Aynı klasörde receipt.json, decision-record.md, source.diff, render.xml/log, runtime-render-bindings.json ve render/world-v3/sugi-W60.png korunmalı. PNG `a738a264daf320d6c0738019cc2dd5d77b28f957825a7e7e07945f445df539b3`; runtime JSON `9e7fe14fc4d041d0769c8c29b61e76f6f3da746154bf14717271c5da8a7d32c5`; canlı hashleri MATCH. Tek Metal W60/PM1/1; guard editor restore0, verified1, outOfScopeChanged1 diğer Player'a atfedilmiş; tam izolasyon PASS değil. Gri eğri/NW footer ve genel AD/perf OPEN.

`planning/work-packages/WP-075-TODAY-SKY-INTAKE-MAIN-20260929.md:17–26` mevcut ayrı S05 r2 işini tarif ediyor: güncel main tabanlı dört dosya, değişen test bir kez ve gerekli tek W60; spawn/world/bake/AP023 üretimi yok. Bu danışmanlık r2 işini üstlenmez veya genişletmez; yeni r2 source/SHA önceki5c89 PASS'ini otomatik devralmaz. Original dört-yol kanıt, yeni r2 diff ve yeni kabul ledger'i ayrı tutulmalı.

Eski intake STOP: S10 handoff:138 ve `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-078/evidence/s10-index-provenance-20260929T033845Z.fuzhp6rc/report.md:1`: APPLY öncesi index.lock ownership UNKNOWN; empty lsof/timestamp owner kanıtı değil. Bu rapor lock'a dokunmadı.

## P06/AP022 bugünkü cache: yeniden kullanılabilir bilgi, anatomy kabulü değil

Kesin kök: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/05-blender/validation/AP022-s06-anatomical-source-v001/`.

| Dosya | Byte | Bu denetimde SHA256 |
|---|---:|---|
| source-arrays.json | 15607907 | 28ef61d17e3b178902b5128b7307a0c5095163568367e43f785de441dd61b668 |
| source-hand-overview.png | 134199 | b0ad7eb62d6bcfb30a47a2e87de57a7fd66463ed9039ea97579bbc6a3eaf4b97 |
| extract.py | 6100 | a11810b7020292a09093b982b6f5c5b1cbfeb63204029b1c5b8d8876210ddce2 |
| select_surface.py | 7387 | 95107df6ac42ff16435dac5b68062e4f97e933239b03ce6411d02999c30e36fc |
| inspect_geometry.py | 1940 | fcfed02795e0ec373185bf2371eddb139c36c0b307c251b89fb02537787e8dce |
| main-wave-plan.json | 3186 | 1ea5775f443f33fc3d6bf097b07a8844d5741a9749600630a3b130d692bfc8ab |

source-arrays schema AP022.exact-tp-source-arrays.v001, UTC03:45:28Z. Gerçek raw CP11119/polygon15682/cluster77, Blender-derived triangle22180. CP ve ordered polygon correspondence exact; max coordinate delta0. Kaynak girişleri bugün canlı hash ile eş:
- rig/v002/exports/player_base_v002.fbx:26cff12942a4b99714aa4400f898b2fccbc8ae20249c92a5af2a03b908696d15.
- rig/v002/source/player-rig-v002.blend:cb0bf60e45784559b0dda46ae81ced513f4160dcafa035e93a5954f1cb0abd7b.

Sınır: yalnız raw rest mesh/CP/poligon/bind bilgisi; Unity split/weld/reorder, baked pose veya anatomical mask kabulü değil. main-wave-plan.json eski bone-origin selection eligible0'ı FAIL olarak tutuyor. select_surface.py henüz çıktı vermiş kabul edilmemeli; bu snapshot'ta source-palmar-selection.json yoktu. 286 benzeri reusable/anatomical count uydurulmadı. P06 işi aktif olduğundan yeni dosyalar daha sonra eklenebilir; bu manifest yalnız gözlem anını tanımlar.

## P07/AP021 bugünkü RAM/proof: frozen sayısal aday, physicalReady=false

Kesin kök: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-021/05-blender/work/p07-codex-20260928/smg_2/junction-stage-v001/`.

En yararlı eşlikçi çiftler ve kanıtlar:
- junction-complete-saved-basis-iron-ram-v002.json:281854byte/SHA7d78badadb6ae7fbdbaaffa9bae45e581c74a993f4ae2bcd7537dea813c8d547. Gösterdiği junction-complete-saved-basis-iron-ram-v001.npz:2727200byte/SHA62b544768746523ebb7c8b22f6d3d8509ab33f9e028d3a677132f650d8a9d529, canlı MATCH.
- Eski junction-complete-saved-basis-iron-ram-v001.json:241113byte/SHA1f8e9c3e266c11ca6509914524961c03d761c6682e7b75b078806a2ccd8ba18b. v002 yalnız metadata/count düzeltmesi; NPZ aynı. Önceki manifest silinmemeli.
- junction-complete-continuous-iron-uv-ram-v001.json:312046byte/SHA1d45969656fc6e201a5a5c3ee0fe8fd383e392312c9c43cf59344090dc7a1b7b. Ayrı junction-complete-continuous-iron-uv-ram-v001.npz:2727152byte/SHA6c23611dcc7e0b7c7d513af5edff002a95729b85f99ee141bc2c8bce1bc8f6db, canlı MATCH. Bu yeni two-parent continuous UV delta adayıdır; eski v003 checker sonucu buna aktarılmaz.
- modelci-independent-complete-candidate-check-v003.json:59018byte/SHA73a10c22a6fe4f2d227659e5e59414a0d517dcdb64f167c08afef83d2dd3c96e. Saved-basis NPZ ve v002 manifestini bağlar; budget true, üç shared edge conformity true; internal target child UV tested3/continuous0/discontinuous3. Body/cavity, global intersections, coplanar/boundary, atlas/style ve physical acceptance OPEN.
- saved-fixed-readback-20260929-v001.json + .npz; saved-fixed-readback-20260929-receipt-v001.json SHA91550c5a5b7fac1f9060c09beaafb7456949e1832174dd9cad60b932a0b6a690; action-receipt-v001.json SHA cb73193f0007a016afe1b5490152efc62821cb5ab839398701f34fea78406327; readback-only-v001.py ve Blender/gates logları. Bunlar actual saved18 mesh readback kökenidir.
- junction-new-iron-uv-ram-candidate-v001.json/.npz; junction-new-iron-conform-neighbor-ram-v001.json/.npz; junction-p07-iron-semantic-eligible-resource-v001.json/.npz; junction-authoritative-iron-semantic-resource-v001.json/.npz ve junction-iron-actual-parent-bind-20260929-v001.json dependency zinciri olarak tutulmalı.

Aday manifest gerçek all18 meshes,440 unchanged arrays, LOD totals19671/5896/1782 (budget20000/6000/1800) kaydeder. Readback baseline19671/5890/1782'dir. Bu sayılar mesh triangle/array sayılarıdır, reusable output/adet kabulü değildir. RAM durumu NPZ+JSON'a yazılmıştır; çalışan Python/Blender belleğinden yeni dump çıkarıldığı iddiası yok. physicalReady false tüm ilgili manifestlerde korunur.

## Kaynakları taşımadan/silmeden koruma yolu

Mevcut P06/P07/S05 owner alanları yerinde kalmalı; source FBX/Blend/exports üzerine yazılmamalı. C00 mevcut yetkisiyle ayrı tarihli output/archive dizinine içerik-adresli kopya veya native CoW snapshot alabilir; hardlink kaynakla aynı inode olduğu için immutable arşiv yerine geçmez. Bu danışmanlık kopya veya arşiv oluşturmadı.

Koruma manifestinde her öğe için absolute source path, SHA256, size, role (source/cache/candidate/proof), input hashes, source SHA/branch, original exit/test/guard, measured acceptance ve açık sınırlar bulunmalı. Snapshot sonrası destination SHA/source SHA yeniden karşılaştırılmalı. Kaynak owner aktifse stable-before/after kontrolüyle aynı generation tutulmalı; drift olursa yeni ayrı generation kaydı, önceki kayıt silinmeden. Private guard snapshot kullanıcı dosyaları genel salvage paketine eklenmemeli; yalnız redakte edilmiş summary/exit/hash manifestleri yeterli.

Yararlı kod/üretim adayı çekme sırası: mevcut cached/proof artefact'ları ayrı koru → S05 güncel main r2 dört-yol exact teslimini bekle → C00 kaynak/kanıt eşliğini kabul et → yalnız kabul edilmiş production farkı main'e al. P06 anatomy cache veya P07 RAM adayı bu sırada Unity Assets'e ready varlık diye eklenmemeli. Yanlış geçmiş PASS'i yeni aday/source'a taşımadan yeniden kullanılabilir bilgi korunur.
