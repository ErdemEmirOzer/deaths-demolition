# WP-012 — M3 ART LIBRARY IMPORT: AP-002/AP-003/AP-003B → UNITY, PİLOT TAKILMA KÖK NEDENİ, DEC-068 GÖK

> Yürütme promptu (C-00, 2026-09-15) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-012.

OTURUM VE SAHİPLİK
Birincil sahip: S-05 — World, Environment & Lighting (tek writer, tek commit owner). S-01 yalnız asmdef/klasör sınırı sorusu olursa; S-03 yalnız NavMesh/spawn paritesi sorusu olursa; S-10 bu pakette devrede değil (tam regresyon M3 kapanışında).
Kadro: unity-specialist (importer, prefab üretimi, profiler, URP — ana yük), blender-specialist (gömülü dokulu FBX'lerin embed-kapalı yeniden export'u, background CLI), csharp-developer (Editor araçları ve testler), art-director (URP'de kütüphane görünümü tek yargı; Fable 5.1).
Model politikası: sanat yargısı Fable 5.1; import, prefab, profiler, shader/volume, test, Git işleri Opus 5.
Çalışma düzeni: HIZLI HAT direktifi bağlayıcıdır — manifest/PRA/kit/inceleme turu/tam sıfırdan zincir yok; yalnız değişen modül testleri; tek commit; ≤15 satır not. Ajanlar görev tamamlanana kadar kesintisiz çalışır; yalnız §9 durma koşullarında dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç noktası:

* main HEAD: `27432f598a0da7b67f5669df9fe2b9aafec64608` · etiket `wp-011-m3-pipeline-pilot`
* Tracked 1198 · `git lfs ls-files` 87 · EditMode 711/711 · PlayMode 146/146
* Probe tabanı: Round 8/8 · Gold 12/12 · Armor 9/9 · Special 10/10 · Economy 11/11 · Profile 29/29 · EnemyVersion 10/10 · PilotPerf 9/9
* Kullanıcı verisi tabanı: `wp-010/backup/userdata-postManual-wp010-20260914T230321Z` (Player profili SaveVersion 3) — probe'lar izole; gerçek profile dokunulmaz.
Branch/worktree: `wp-012-m3-library-import` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-012/worktree/deaths-demolition` · etiket önerisi `wp-012-m3-library-import`.
Başlangıçta doğrula: HEAD/etiket, porcelain 0, LFS pointer bütünlüğü, Unity 2022.3.22f1 + URP 14.0.10, Import Contract v1 testleri yeşil.
Kaynaklar (salt okunur): `asset-production/AP-002/06-blender/exports/v002/` (12 aile LOD0/1/2 + COLLIDERS) · `AP-003/05-blender/exports/selected/` (6 aile, 43 varyasyon) · `AP-003B/05-blender/exports/selected/` (11 aile, 53 FBX + `textures/` 32 PNG + doku manifesti; GLB'ler kullanılmaz) · WP-011: `ArtImportContract` + postprocessor, `ArtTextureRepack`, `Assets/_Art/` düzeni, `PilotPerfProbe`, görsel regresyon aracı, stil kılavuzu v1 taslağı, ADR-011-1..4 · WP-011 raporu §19 (pilot takılma ölçümü) ve §23.
Amaç: Üç üretim kütüphanesini Import Contract v1 ile Unity'ye aile/varyasyon prefab'ı olarak almak; pilot sahnedeki periyodik ~23 ms takılmanın kök nedenini kapatmak; Run.unity gök davranışını DEC-068 ile hizalamak; WP-014 (AP-005 yerleşimi) için prefab kütüphanesini hazır etmek.

2. BAĞLAYICI KARARLAR

* Import Contract v1 tek kabul edilen biçimdir: FBX embed kapalı + ayrı PNG, `bakeAxisConversion`, `materialImportMode None`, BC7 sRGB / BC5 normal, packed → MetallicSmoothness. Gömülü dokulu FBX (AP-002/AP-003) Unity'de "extract" ile değil, blender-specialist'in background CLI yeniden export'uyla `wp-012/derived/` altına embed-kapalı + PNG olarak üretilir; üretim klasörlerine yazılmaz.
* Bitki ailelerinde vertex renk (`_AP003_WIND`) import'ta korunur ve testle doğrulanır (WP-013 rüzgâr shader'ı bunu tüketecek).
* LOD eşikleri: ADR-011-3 önerisi uygulanır (sözleşme eşikleri yarıya, lodBias sahibin ölçümle belirlediği değer); `04`'e ADR-012-1 olarak kaydedilir. Perf bütçesi onayı kullanıcıya gitmez; ölçüm raporda.
* Gök: DEC-068 (yağmur sonrası kapalı hava) bağlayıcıdır; nihai palet kararı kullanıcıda, bu paket yalnız çelişkiyi giderir.
* LFS kuralları aynen; yalnız seçili asset'ler. Remote/push yok. Yeni Unity paketi ve sürüm değişikliği yok.

3. İZLENEBİLİRLİK
DEC-012/013/014/068 · REQ-100–114, REQ-104/105, REQ-111 · ADR-011-1 (Import Contract v1), ADR-011-2 (LFS/klasör), ADR-011-3 (perf bütçesi), ADR-011-4 (gök) · WP-011 devirleri: pilot takılma kök nedeni, LOD2 gölge, AP-015 OPEN #7 katman politikası.
4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Unity'de yalnız AP-001'in 18 ailesi var; AP-002 (12), AP-003 (6×varyasyon), AP-003B (11×varyasyon) yok. 4.2 AP-002/AP-003 FBX'lerinin embed durumu doğrulanmamış. 4.3 Pilot sahnede p99 21,5 ms, ≈4 Hz periyodik takılma (Run sahnesinde yok); kök neden bilinmiyor. 4.4 Run.unity prosedürel gök yatay kamerada gün batımı (DEC-068 çelişkisi); post-fx fiilen kapalı. 4.5 Prefab üretimi elle; varyasyon sayısı (≈108) elle üretimi dışlar.
5. UYGULAMA KAPSAMI
A. Kaynak hazırlığı — embed denetimi; gerekli yeniden export'lar `wp-012/derived/<AP>/` (FBX + PNG + kısa manifest); AP-003B doğrudan kaynaktan.
B. Import — `Assets/_Art/Families/<AP-XXX>/<aile>/` (LODn, Colliders, Textures, Materials); contract ihlalleri düzeltilmez, ≤5 satır C-00'a (üretime döner).
C. Prefab üretimi — Editor aracı `ArtFamilyPrefabBuilder` (aile/varyasyon başına: LODGroup, eşikler ADR-012-1, collider/proxy, CameraOccluder katmanı, LOD2 gölge kapalı, materyal bağlama); çok parçalı aileler (köprü deck/railing/post, koridor zemin/çatı/panel) tek prefab altında alt-objeler; koridor çatısı REQ-111 için ayrı renderer.
D. Kütüphane sahnesi — Assets/_Art/Scenes/AP_LibraryShowcase.unity (tüm aileler tek sıra, etiketli; build listesine eklenmez) + 1 görsel regresyon referansı; art-director tek yargı burada.
E. Pilot takılma — batchmode Profiler capture (`Profiler.logFile`), kök neden (LODGroup/NavMesh/GC/gölge kaskadı/probe kaynaklı vb.), düzeltme, `PilotPerfProbe` tek koşu: hedef p99 < 16,7 ms @ 40 düşman; kök neden paket dışıysa gerekçe + devir.
F. Gök — Run.unity prosedürel gök DEC-068'e hizalanır; pilot ve Run aynı volume profil varlığını (Assets/_Art/Lighting/DD_Overcast_v1.asset) kullanır; ADR-011-4 kapanış notu.
G. Belge — `planning/04`'e WP-012 girişi, ADR-012-1 (LOD eşikleri/lodBias), ADR-012-2 (prefab üretim sözleşmesi); `planning/work-packages/WP-012-*.md` prompt kaydı.
6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi/profil/UI kodu; prosedürel dünya builder'ı; spawn/silah nokta düzeni; Packages/ProjectSettings (yeni sahne/asset ve gök düzeltmesinin gerektirdiği volume hariç); Unity/URP sürümü; AP üretim klasörleri; planning karar/kapsam metinleri (yeni ADR/WP girişi eklenir, mevcut karar değiştirilmez).
7. TEST MATRİSİ (yalnız işle ilgili)
EditMode `-testFilter Art`: asset-gate testleri tüm aileler için (contract ayarı, doku formatı, LOD sayısı, collider/proxy varlığı, vertex renk varlığı — bitki, GUID tekliği, LFS pointer); `ArtFamilyPrefabBuilder` idempotans testi. PlayMode (Metal): `PilotPerfProbe` tek koşu; NavMesh/spawn paritesi 12/12 pilot sahnede. Build/smoke: Run.unity değiştiği için release build bir kez + e1/e4 smoke; başka tam zincir yok. Gerçek profil: smoke sonrası restore 6/6 (son Unity koşusundan sonra).
8. GIT VE TESLİM
İzole dalda tek commit, yalnız paket yolları (`--pathspec-from-file`), `git add -A` yok; LFS pointer doğrulaması commit öncesi; mesaj `WP-012: M3 art library import — AP-002/AP-003/AP-003B families, pilot stall fix, DEC-068 sky` + `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. C-00'a ≤15 satır: SHA, dosya sayısı, LFS sayısı, aile/prefab sayıları, test sonuçları, p99 önce/sonra, kök neden, gök, devirler, contract ihlalleri. C-00 `--ff-only` merge + etiket.
9. KRİTİK DURMA KOŞULLARI
Yeni Unity paketi zorunlu hale gelirse · Unity/URP sürümü · takılma kök nedeni motor/URP sürüm değişikliği gerektiriyorsa (ölçümü raporla) · contract üretim değişikliği olmadan karşılanamıyorsa (üretime devir, geçici çözüm yok) · secret/lisans içeren dosya.
10. DEFINITION OF DONE
AP-002/AP-003/AP-003B tüm aileler Unity'de contract uyumlu · prefab'lar araçla üretilmiş (sayılar notta) · `AP_LibraryShowcase` + art-director yargısı · asset-gate ve builder testleri yeşil · pilot p99 < 16,7 ms veya gerekçeli devir · Run.unity gök DEC-068 uyumlu, ortak volume profili · release build + e1/e4 smoke geçti, profil restore 6/6 · `04` girişleri ve prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
