# WP-011 — M3 CONTENT PIPELINE PILOT: AP-001 GOLDEN SLICE → UNITY

**PAKET SAHİBİ:** S-05 — World, Environment & Lighting
**ANA KOORDİNASYON:** C-00 — Deaths Demolition Main Coordination
**KAYIT NOTU:** Aşağıdaki metin kullanıcının 2026-09-15 tarihinde S-05'e verdiği yürütme promptunun aynısıdır. Prompt yeni ürün DEC/REQ maddesi icat etmemeyi buyurur; import sözleşmesi, bütçe ve LFS kararları kullanıcı kararı olarak §2'de verilmiştir, teknik ayrıntıları `planning/04 §WP-011` altında ADR olarak kaydedilir.

---

WP-011 — M3 Content Pipeline Pilot: AP-001 Golden Slice → Unity

OTURUM VE SAHİPLİK
Birincil sahip: S-05 — World, Environment & Lighting (tek writer, tek commit owner). S-01 mimari/entegrasyon (asset klasör yapısı, asmdef sınırı, sahne/prefab sözleşmesi, LFS), S-10 bağımsız test/Git/performans kabulü. S-03 yalnız NavMesh/spawn noktası paritesi için salt okunur danışman. AP-001/002/003 üretim alanları salt okunur kaynaktır; AP-003B üretim oturumu paralel çalışmaktadır — onun alanına dokunma.
Kadro: unity-specialist (importer/URP/LOD/perf — ana yük), blender-specialist (export tarafı doğrulama, yeniden export gerekirse), art-director (URP'de görsel hedefle karşılaştırma; Fable 5.1), game-test-engineer (okunabilirlik + kalabalık performans ölçümü), test-engineer (Editor asset-gate testleri), git-repository-safety (LFS + binary politika, F0–F10), prompt-engineer/pra tek tur. Gözlemci/denetçi ajan ekleme; ART incelemesi tek tur.
Model politikası: sanat kararı (URP görsel eşdeğerlik, palet, okunabilirlik yargısı) Fable 5.1; import, shader dönüşümü, LOD, performans, test, Git işleri Opus 5.
Ajanlar görev tamamlanana kadar kesintisiz çalışır; yalnız §14 durma koşullarında dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç noktası:

* main HEAD: `11bf00943304c8d5d617e68262f2a14fc96cec48`
* Başlangıç etiketi: `wp-010-m2-enemy-versions`
* Tracked: 842 · EditMode 634/634 · PlayMode 142/142
* WP-010 final manifest: `50fcdc00ca86ee3528efef4d715d3593e2e8f6a299af3a86317ed6a2ef1b88e8`
* Probe tabanı: Round 8/8 · Gold 12/12 · Armor 9/9 · Special 10/10 · Economy 11/11 · Profile 29/29 · EnemyVersion 10/10 · Lifecycle A/B
* Kullanıcı verisi tabanı: `wp-010/backup/userdata-postManual-wp010-20260914T230321Z` (Player profili SaveVersion 3)

Branch/worktree: `wp-011-m3-content-pipeline-pilot` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-011/worktree/deaths-demolition` · etiket önerisi `wp-011-m3-pipeline-pilot`.
Başlangıçta doğrula: HEAD/etiket/porcelain 0/tracked/manifest/Unity 2022.3.22f1 + URP 14.0.10/EditMode-PlayMode/probe'lar/diğer worktree izolasyonu/`git lfs version` (3.7.1 kurulu)/AP-001 kaynak hash'leri (AP-001/07-manifests/SHA256SUMS-final-v001.txt).
Amaç: AP-001 golden slice'ın 18 aile FBX çıktısını Unity'ye alıp import sözleşmesini, URP materyal dönüşümünü, LOD/collider/NavMesh davranışını, okunabilirliği ve performansı ölçmek; sonucu üretim hattına "böyle üret" sözleşmesi ve bütçe olarak döndürmek. Oynanış kodu değişmez.

2. BAĞLAYICI KARARLAR (kullanıcı, 2026-09-15)

* Hedef ölçüm cihazı: Apple M1 Pro (mevcut geliştirme makinesi), 1920×1080, hedef 60 fps; ölçüm senaryosu 40 aktif düşman (teknik tavan) + oyuncu + golden slice; Windows hedefi sonraki karardır.
* Binary asset politikası: Git LFS. `.gitattributes`'a `*.fbx *.png *.tga *.exr *.wav *.ogg *.blend` (ve gerekli diğerleri) LFS izleme; LFS yalnız bu pakette kurulur ve yalnız seçilen asset'ler eklenir. Remote yok; push yok.
* Import formatı: FBX (Unity yerleşik importer). glTF importer paketi eklenmez — paket ekleme ayrı kullanıcı kararıdır (§14).
* Unity/URP sürümü değişmez. Yeni Unity paketi yüklenmez.
* Onaylı dünya kararları aynen (DEC-012/013/014/068; REQ-100–114; REQ-104/105 okunabilirlik).
* Presentation A yalnız çalışma referansıdır; nihai palet/hava sistemi kararı bu paketin çıktısıyla kullanıcıya sunulur, paket karar vermez.

3. İZLENEBİLİRLİK
DEC-012, DEC-013, DEC-014, DEC-016, DEC-053, DEC-054, DEC-058, DEC-059, DEC-068 · REQ-100–114, REQ-131, REQ-134–136 · FLOW-009 · AP-001 final raporu §26–30 (riskler, entegrasyon adayları) · AP-002/AP-003 → AP-015 handoff'ları (12 açık runtime kararı). Yeni DEC/REQ yok; teknik sözleşmeler ADR-011-x.

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Unity projesinde hiçbir dış asset yok; `Assets/_Prototype` kodla kurulu prosedürel dünya (`World` builder: 49 segment, NavMesh 2187 üçgen, 12 spawn + 12 silah noktası). 4.2 glTF importer yok; FBX importer varsayılan ayarları (ölçek, eksen, normal/teğet, materyal çıkarımı) sözleşmesiz. 4.3 URP shader/smoothness dönüşümü, 16-bit map importer davranışı, LODGroup mesafeleri, static batching/instancing, occluder saydamlaşma, lightmap/UV1 padding — hiçbiri ölçülmedi (AP-001 §26). 4.4 LFS yok; `.gitattributes` yalnız metin kuralları. 4.5 Görsel regresyon: e1/e4 yakalamaları var, karşılaştırma aracı yok. 4.6 Performans bütçesi tanımsız (M4'e devir).

5. UYGULAMA KAPSAMI
A. Depo ve LFS
`git lfs install` (yerel), `.gitattributes` LFS kuralları, `Assets/_Art/` kök klasör yapısı (Environment/Families/<family>/LODn, Materials, Textures, Prefabs, Scenes/Pilot), `.meta` ve GUID politikası; asset adlandırma sözleşmesi; LFS izlenen dosyaların manifestte ayrı listelenmesi. Asset kaynak provenance'ı: her import edilen FBX/PNG için AP-001 SHA-256 → Unity yolu eşlemesi (wp-011/manifest/asset-provenance.json).
B. Import sözleşmesi
AP-001 seçili 18 aile (`unity-integration-candidates-v002.json`) için FBX import ayarları (scale factor, convert units, bake axis, normals/tangents import vs compute, mesh compression, read/write, UV1 lightmap), doku import ayarları (sRGB/Non-Color, sıkıştırma, mip, max size), URP Lit materyal dönüşümü (BaseColor/Normal/Packed → Metallic/Smoothness kanalı, roughness→smoothness ters çevirme), çift yüz/alpha yok (mimari). Sonuç ADR-011-1 "Import Contract v1" — üretim hattı bunu okur.
C. Pilot sahne
`Scenes/Pilot/AP001_GoldenSlice` — golden slice yerleşimi (AP-001 kaynak dönüşümleri), LODGroup'lar (mesafe adayı ölçülerek), collider proxy'ler (29), NavMesh bake (mevcut agent ayarlarıyla) ve spawn/silah noktası paritesi (WP-003 düzeni bozulmaz; prosedürel dünya ile pilot sahne yan yana, prosedürel sahne değişmez), oyuncu + düşman sistemleri mevcut `RunSystemsInstaller` ile pilot sahnede çalışır (kod değişikliği yok; sahne/prefab/asset ile). Büyük engel saydamlaşması (REQ-035/111) mevcut `CameraOccluder` mekanizmasıyla.
D. Işık/atmosfer temel
URP volume: kapalı hava, hafif sis (uzak), yağmur sonrası yumuşak ışık; Presentation A ile yan yana karşılaştırma (aynı kamera açısı Unity'de yeniden kurulur). Polen/parçacık yok (AP-004 kapsamı). Reflection/lightmap: yalnız ölçüm, bake kararı raporda.
E. Ölçüm ve kapılar

* Performans: 1920×1080, 40 düşman, 60 s pencere; ortalama/1 % low kare süresi, draw call, set-pass, üçgen, VRAM, GC (pozitif kontrollü); prosedürel dünya ile karşılaştırma (delta).
* Okunabilirlik: gameplay kamerasında düşman/oyuncu/gold/Special siluetleri; kapsül örtülme ışın testi (AP-001 yöntemi Unity'de).
* Görsel regresyon aracı: sabit kamera yakalamaları + fark ölçümü (Editor test), e1/e4 kalıbı.
* Editor asset-gate testleri: import ayarı, doku formatı, GUID tekliği, LFS işaretçisi bütünlüğü, poligon tavanı, materyal shader tipi.

F. Sözleşme ve belge çıktıları
ADR-011-1 Import Contract v1 · ADR-011-2 Asset Folder & LFS Policy · ADR-011-3 Performance Budget v1 (ölçülen; öneri olarak kullanıcıya) · Stil kılavuzu v1 taslağı (art-director, Fable): palet, ışık dili, okunabilirlik kuralları, siluet dili — kullanıcı onayına sunulur · `planning/04` "Asset üretim paketleri (AP-001…AP-015)" tablosu (kullanıcının verdiği plan + güncel durum) ve WP-011 kaydı; WP-010 durum satırı "Tamamlandı + entegre"; M3 aşama girişi.

6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi/profil/UI kodu; prosedürel dünya builder'ı ve mevcut sahneler (pilot ayrı sahne); spawn/silah nokta düzeni; Packages/ProjectSettings (LFS `.gitattributes` ve yeni sahne/asset hariç); Unity/URP sürümü; AP üretim alanları; Special Weapon/düşman görselleri (AP-014); polen/sis parçacıkları (AP-004).

7. TEST MATRİSİ
EditMode: asset-gate testleri (her FBX/PNG: ayar, format, tavan, GUID, LFS), provenance eşlemesi 18/18, Import Contract doğrulayıcı. PlayMode (Metal): pilot sahnede oyuncu + 40 düşman koşusu (kare bütçesi ölçümü), NavMesh erişilebilirlik 12/12 spawn + 12/12 silah noktası, saydamlaşma, LOD geçişi (mesafe adayları), okunabilirlik kapsül testi, mevcut tüm testler değişmeden PASS.

8. REGRESYON KAPILARI
Tam zincir sıfırdan Library (asset/sahne değişimi build-etkileyen): import 0, EditMode 634+, PlayMode 142+, release build + e1/e4 + pilot sahne smoke, 7 probe, release dll taraması; build boyutu ve süresi raporda (LFS asset etkisi). Değişiklik-kapsamlı kural sonraki düzeltmelerde; manifest tek ilan (LFS pointer'ları manifestte içerik hash'iyle).

9–13. Diğer kurallar
Performans/yaşam döngüsü (kare başı ayırma 0 kuralı sahne nesnelerine uygulanmaz, koda uygulanır) · gerçek kullanıcı verisi (probe'lar izole; release smoke sonrası restore 6/6) · Git (tek writer S-05, atomik commit, `--pathspec-from-file`, LFS pointer'lar staged, `-A` yok, ff-merge/etiket C-00, push yok) · diğer worktree ve AP-003B üretim alanı izolasyonu.

14. KRİTİK DURMA KOŞULLARI
Yeni Unity paketi (glTF importer, Addressables vb.) zorunlu hale gelirse · URP/Unity sürümü · 60 fps hedefi golden slice ile 40 düşmanda tutmuyorsa ve asset bütçesini değiştirmeden çözüm yoksa (ölçümü raporla, kararı kullanıcıya bırak) · LFS repo boyutu beklenmedik büyüklüğe çıkıyorsa (>2 GB) · AP-001 kaynak hash'leri uyuşmuyorsa · oynanış kodunu değiştirmek gerekiyorsa.

15. TESLİM RAPORU (numaralı)
1 başlangıç · 2 LFS/klasör/adlandırma · 3 Import Contract v1 · 4 18 aile import sonucu (aile başına ayar/sonuç) · 5 URP materyal dönüşümü ve Presentation A karşılaştırması · 6 pilot sahne + NavMesh/spawn paritesi · 7 LOD mesafe adayları · 8 saydamlaşma · 9 performans ölçümü (prosedürel vs pilot) · 10 okunabilirlik · 11 görsel regresyon aracı · 12 asset-gate testleri · 13 EditMode/PlayMode/probe/build · 14 build boyutu/süresi · 15 stil kılavuzu v1 taslağı · 16 üretim hattına dönecek "böyle üret" notu (AP-003B/004/005/006+ için bütçe ve ayarlar) · 17 planning güncellemesi (AP tablosu, WP-011, M3) · 18 manifest zinciri · 19 Git F0–F10 + LFS · 20 S-01 · 21 S-10 · 22 varsayımlar · 23 kullanıcı kararı gerektiren konular (nihai palet, performans bütçesi onayı, Windows hedefi) · 24 commit · 25 ff-merge talimatı.

16. DEFINITION OF DONE
LFS kurulu ve yalnız seçili asset'ler izleniyor · Import Contract v1 yazılı ve testli · 18 aile Unity'de LOD+collider ile · pilot sahnede oyuncu + 40 düşman koşusu ölçüldü (sayılar raporda) · NavMesh/spawn paritesi 12/12 · okunabilirlik testi PASS · görsel regresyon aracı çalışıyor · tüm mevcut testler/probe'lar PASS · stil kılavuzu v1 taslağı + bütçe önerisi kullanıcıya sunulmuş · planning AP tablosu + WP-011 kaydı · S-01/S-10 PASS · tek atomik commit · push yok.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
