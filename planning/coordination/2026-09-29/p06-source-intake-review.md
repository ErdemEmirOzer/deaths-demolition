# P06 AP022 anatomical source → S06 mapping intake denetimi

Durum: **SOURCE_INPUT_INTAKE_READY**. Paket kaynak sağ palmar ROI/anchor olarak S06'ya devredilebilir. Unity importer/posed correspondence, rendered contact ve üretim/AD kabulü UNKNOWN kalır.

Salt okunur bağımsız hash, binary FBX, JSON/NumPy ve görsel denetim yapıldı. Yeni Unity/Blender, build/test, asset save/export veya source/prod/main/lock yazımı yok. Yalnız bu `/private/tmp` raporu yazıldı.

## Teslim kimliği ve değişmez girdiler

Paket: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/05-blender/validation/AP022-s06-anatomical-source-v001/anatomical-source-package.json`

SHA256 `30ae6b3628b5bd77871d38491bbc9376fed1bcc1b4998f56e457ad7c97c2a244`, 43,697 byte. Kaynak durum satır5, selection satır63, domain/transforms satır909, acceptance satır1349, S06 handoff satır1379, artifacts satır1455. Main referansı3c0875e1; source observer8b12f158 tarihsel bağlamdır. Dış-Git A2 cache teslimidir.

Paketin **14/14 benzersiz artifact SHA256+size eşleşti**. `source-palmar-selection.json` SHA `af78572c38f37311b9b9bb7db15f6f4cc1cb5e6c13f38dfb3f1013d016e5c1f5`; selection JSON paket içindeki selection ile yapısal olarak TAM aynı. `source-arrays.json` SHA `28ef61d17e3b178902b5128b7307a0c5095163568367e43f785de441dd61b668` aynı, 15,607,907 byte.

Canonical girdiler canlı byte/hash ve size ile eşleşti:

- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/rig/v002/exports/player_base_v002.fbx` — SHA `26cff12942a4b99714aa4400f898b2fccbc8ae20249c92a5af2a03b908696d15`, 4,222,060 byte.
- `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-022/rig/v002/source/player-rig-v002.blend` — SHA `cb0bf60e45784559b0dda46ae81ced513f4160dcafa035e93a5954f1cb0abd7b`, 37,310,797 byte.

Paket/extraction before-after kayıtları aynı SHA çiftini korur. Kaynaklar değiştirilmedi. `select_surface.py` eski başarısız cache sürümünden güncellenmiş derived script; canonical FBX/Blend değişimi değildir.

## Bağımsız kaynak geometri/anchor doğrulaması

FBX7400 binary verisi yeni bir salt okunur parser ile doğrudan okundu; provided `extract.py`/`verify_source.py` çalıştırılmadı. Geometry437152064 `Player_LOD0.001`, model11615477 `Player_Body_LOD0` için **11119 CP,15682 ordered polygon,53544 corner** doğrulandı. Tüm CP koordinatları, signed polygon indices, ordered polygon CP ve corner dizileri `source-arrays.json` ile TAM aynı.

Anchor:

- Source polygon **14071**, ordered CP `[10024,10025,10023,10022]`, global corners `[47720,47721,47722,47723]` raw FBX ile eşleşir.
- Blender kaynak tessellation triangle **19578**, ordered CP `[10024,10025,10023]`, corners `[47720,47721,47722]`, aynı polygon'a bağlıdır. Bu ordinal **Unity triangle ordinal değildir**.
- Barycentric `[1/3,1/3,1/3]` toplam1; doğrudan raw CP'den centroid `[-0.6362197399139403,0.08031124124924341,1.040199836095174]m`. Kayıt farkı en çok1.11e-16m.
- Ordered winding normal `[0.6455024080192705,-0.14373058350977178,-0.7501120986927645]`; kayıtla tam aynı; twice-area0.00010275521596763138m², dejenere değil.
- Analyst ROI **31 triangle /28 CP**; mask tam triangle CP birleşimi, edge-connected tek component, anchor içeride. Centroid envelope tümünde sağlanır; minimum normal·palmarRef **0.6645950432966026≥0.65**. Bunlar adayın tutarlılık testleridir, kendi başına anatomik adlandırma otoritesi değildir.

## Domain, skin ve transform doğrulaması

Mesh world ve rig world matrixleri identity. Reference frame ortonormal fark2.22e-16, determinant1.0000000000000004. Raw FBX model literal LclRotation **[-90.00000933466734,0,0]°**, ideal -90 yaklaşımıyla karıştırılmıyor. Doğrudan dönüşüm anchor `[-0.6362197399139403,1.0401998230107983,-0.0803114107193065]m`; paket kaydıyla floating-point sınırında aynı. Geometrylocal→Blendrig/world identity; geometrylocal→FBXscene açık; bunlar Unity posed renderer world domain'ine doğrudan eşit sayılmaz.

77 cluster'ın raw binary Transform/TransformLink dizileri cache'le tam eşleşti. Column-major→row-major dönüşümüyle 77 bind/rest axis denklemi yeniden salt okunur hesaplandı: maksimum hatalar link−Arest1.7881393432617188e-7, Transform−inverseRest2.6663378704938623e-7, link·Transform−A1.8596684081551018e-7. Kaynak sayısal 1e-6 gate altında; anatomik hizalama veya Unity mapping PASS değildir.

Anchor üç CP raw skin ve Blend weight dizileri/embedded seçimi aynıdır. Toplamlar1.0000000261,1.0000000298,0.99999999255; kayıtlı RightPinky1/2/3 + lowerarm_twist_02_r etkileri korunmuş. Bu garip kaynak skin/rig konfigürasyonu onarılmış sayılmaz; P06 source rig/weights asset patch yapmadı.

## Narrow anatomy review ve uygulama sınırı

`source-palmar-selection.json:54` nomination açıkça **analyst-authored central ROI**, üretici orijinal maskesi/clinical crease değildir. Satır363 iki kayıtlı source anatomy reviewer (3d-modeller /root/fp_source_markers, character-animator /root/source_character_anatomy), actual_visual_inspected=true ve source palmar ACCEPT kararlarını içerir. Bu kayıtlar hash-bound pakette; reviewer oturumlarının mesajlarını ayrıca çekmedim.

`source-palmar-inspection.png` canlı hash `d5c085ac37d05799f7ef8a818ded7e18fb66ea63d639578d29bdb2e2a91b3a8b` ve üç ortografik kaynak görüntüsü açılıp incelendi: yeşil connected patch el merkezi/metakarpal yüzeyinde, kırmızı centroid içinde; bilek tüpü/parmak distal dallarını maske kapsamıyor. Kemiklerle kaynak mesh arasındaki yer farkı görünür; kaynak nomination bu farkı gizlemiyor/onarılmış saymıyor. Görsel ve numerical nomination tutarlı; anatomik maskeyi weapon grip/nearest-imported-triangle üzerinden türetmiyor.

`validation-ledger.json` SHA `f03024130412f4beaf7ff9339be0886e36addb2039d64bae38916236307361d4`: tarihsel owner source checker12/12 PASS,0FAIL; `validation.log` aynı ledger SHA'yı kaydeder. Ledger satır203 numerical check'in anatomical-sign/runtime acceptance vermediğini açıkça korur. Extraction log Blender5.2 source arrays hash/kapsamını kaydeder; owner özgün exit0 claim'i korunur, bu denetimde Blender yeniden çalıştırılmadı. Ayrı preflight dosyası yok; original guard session-exec kayıt referansı, full new-runtime guard PASS sayılmıyor.

## S06 intake koşulları / kalanlar

`/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-032h/evidence/s06-palm-one-8b12f158-proof.json` hash `6f54813238f57549fa2c61f6ff3ae6230159ec5fd8bb08ad24079d40602b1ec0` ve size313989 eşleşti. Tarihsel loaded mesh/Avatar identity canonical aynı bytes'a bağlanır: Body GUID261e740b51f494918aac84a67d803f63, localID-1683454014250015237; Avatar9000000; dependencyHash63d94acb46721e48d7ae5abeeb441642. Bu proof geçmiş nearest surface14472, frame25/invisible renderer bağlamındadır; yeni source anchor19578 ile aynı sayılmaz.

S06 kaynak **identity + ordered polygon/corner + barycentric + skin/bind/domain** girdilerini şimdi alabilir. Sonraki tek bounded iş:

1. Exact loaded Body hash/GUID/localID sabitken source CP/corner→Unity imported split/weld/reorder one-to-many map.
2. Polygon14071/corners47720–47723 → gerçek importer triangulation; Blender19578 ordinal eşitlemesi yok. Triangulation farklıysa kaynak yüzey anchor'ını açıkça temsil et.
3. Imported→baked map ve gerçek posed renderer/Avatar/clip/skin/model transform identity kanıtı.
4. Ancak bundan sonra mapped anatomical anchor ile actual authored grip/surface contact farkı hesaplanabilir.

**Kabul:** source input intake hazır. Source→Unity/baked mapping, posed contact, sign/cause/fix, AD/production acceptance UNKNOWN. FP OFF, TP HOLD, f44 WAIT, charge-human WAIT korunur. İlk bone-origin eligible0 başarısız varsayımı yeni kaynak ROI ile aşılmıştır; geçmiş failure silinmez ve source rig kök nedeni UNKNOWN kalır.286 reusable iddiası/sayısı yapılmaz.
