# WP-013 — M3 ATMOSFER VE BİTKİ VFX: RÜZGÂR/FOLIAGE SHADER, SİS, POLEN, SU v1 (AP-004 UNITY TARAFI)

> Yürütme promptu (kullanıcı, 2026-09-15) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-013.

OTURUM VE SAHİPLİK
Birincil sahip: S-07 — VFX & Game Feel (tek writer, tek commit owner). S-05 bu pakette Unity'ye dokunmaz (WP-014'e kadar bekliyor); yalnız `ArtMaterialBuilder`/volume profili hakkında soru olursa danışılır. S-10 devrede değil.
Kadro: unity-specialist (Shader Graph/URP 14, Particle System, volume, perf — ana yük), animation-specialist (rüzgâr hareket dili, polen sürüklenme eğrileri), art-director (atmosfer görünümü ve tek nihai yargı; Fable 5.1), csharp-developer (Editor testleri, WorldAtmosphere bağlaması).
Model politikası: atmosfer/rüzgâr/polen görünüm tasarımı ve sanat yargısı Fable 5.1; shader, parçacık, ölçüm, test, Git işleri Opus 5.
Çalışma düzeni: HIZLI HAT direktifi bağlayıcıdır — manifest/PRA/kit/inceleme turu yok; yalnız değişen modül testleri; tek commit; ≤15 satır not. Ajanlar görev tamamlanana kadar kesintisiz çalışır; yalnız §9 durma koşullarında dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç noktası: main HEAD `c5d19fad71837a26e3bf7501c50ff78c92a8dda2` · etiket `wp-012-m3-library-import` · tracked 1970 · LFS 293 · EditMode DD.Art 89/89 + ProjectIntegrity 11/11 · PlayMode DD.Art 5/5 · PilotPerf tabanı ort. 2,37 ms / p99 2,86 ms @ 40 düşman (1080p, M1 Pro) · kullanıcı verisi tabanı `wp-010/backup/userdata-postManual-wp010-20260914T230321Z` (SaveVersion 3; probe'lar izole).
Branch/worktree: `wp-013-m3-atmosphere-vfx` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-013/worktree/deaths-demolition` · etiket önerisi `wp-013-m3-atmosphere-vfx`.
Başlangıçta doğrula: HEAD/etiket, porcelain 0, LFS pointer bütünlüğü, tam EditMode/PlayMode sayılarını bir kez ölç ve nota yaz (taban).
Kaynaklar: WP-012: `ArtMaterialBuilder`, `ArtLightingBuilder`, `DD_Overcast_v1.asset`, `DD_Sky_Overcast_v1.mat`, `WorldAtmosphere.cs`, `PilotPerfProbe`, `AP_LibraryShowcase`, ADR-012-1/2 · AP-003 rüzgâr şeması (`_AP003_WIND` vertex rengi: R sway / G phase / B flutter / A stiffness; 183 taşıyıcı renderer) · AP-003B `pond_water_surface` · AP-003B `small_breakable` `_BROKEN_LOD` mesh'leri · AP-001 atmosfer kanıt renderleri (`AP-001/06-renders`: fog on/off, ring pollen kareleri) · `planning/00` DEC-068, `01` REQ-104/105/111/113, stil kılavuzu v1 taslağı.
Amaç: AP-004'ün Unity tarafını üretmek — bitki rüzgâr/foliage shader'ı, DEC-068 sis/atmosfer, polen/sakura yaprağı parçacıkları, su yüzeyi v1 ve kırılabilir bağlaması — WP-012 perf tabanını koruyarak.
2. BAĞLAYICI KARARLAR

* URP 14.0.10 yerleşik araçlar: Shader Graph + Particle System + Volume. VFX Graph, Volumetric paketi veya başka paket eklenmez (paket ekleme kullanıcı kararıdır, §9).
* `_AP003_WIND` şeması tüketilir, değiştirilmez; rüzgâr parametreleri tek global (`Shader.SetGlobalVector`) ve `WorldAtmosphere` üzerinden sürülür.
* Atmosfer DEC-068 (yağmur sonrası, kapalı hava, hafif sis). Nihai palet kararı kullanıcıda; bu paket palet önerisi yapmaz, mevcut `DD_Overcast_v1` profilini genişletir.
* Okunabilirlik REQ-104/105 bağlayıcı: sis ve parçacıklar 1.8 m kapsül siluetini ve gold/Special renk bandını gameplay kamerasında zayıflatmaz (ölçülür).
* Perf bütçesi: tüm WP-013 etkileri birlikte, pilot sahne 40 düşmanda ort. ≤ +1,0 ms ve p99 ≤ 5,0 ms; parçacık CPU ≤ 0,2 ms. Bütçe aşılırsa yoğunluk düşürülür, özellik iptal edilmez.
* Yeni Unity paketi/sürüm değişikliği yok; LFS kuralları aynen; push yok.

3. İZLENEBİLİRLİK
DEC-068 · REQ-104/105 (okunabilirlik), REQ-111 (koridor çatı saydamlaşma ile uyum), REQ-113 (kırılabilir) · ADR-011-4 (gök, kapandı), ADR-012-1/2 · WP-012 devirleri: rüzgâr/foliage/su shader'ı, sakura kart arka yüzü karanlık, uzak yaprak seyrelmesi, small_breakable kırılma bağlaması · AP-004 (Codex AP planı: sis/polen/atmosfer VFX, S-05+S-07).
4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Bitki materyalleri URP Lit; rüzgâr vertex rengi taşınıyor ama tüketilmiyor. 4.2 Sakura kartlarının arka yüzü karanlık (tek yüz aydınlatma). 4.3 Uzakta yaprak kartları seyreliyor (mip kapsama importer tarafı eklendi, shader tarafı yok). 4.4 Sis yok; post-fx fiilen kapalı. 4.5 Polen/yaprak parçacığı yok. 4.6 `pond_water_surface` düz opak disk. 4.7 `small_breakable` BROKEN mesh'leri hiçbir sisteme bağlı değil.
5. UYGULAMA KAPSAMI
A. Foliage/rüzgâr shader'ı — Assets/_Art/Shaders/DD_Foliage_Wind.shadergraph: vertex animasyonu (`_AP003_WIND` R/G/B/A + global rüzgâr vektörü/zaman), alpha clip, çift yüz + arka yüz normal düzeltmesi, mip kapsama (alpha-to-coverage veya alpha ölçekleme), SRP Batcher uyumlu; `ArtMaterialBuilder` bitki ailelerini (AP-003 6 + AP-003B bambu/lily) bu shader'a bağlayacak şekilde genişletilir, idempotan. Gövde/kaya/mimari Lit kalır.
B. Atmosfer — `DD_Overcast_v1` profiline mesafe + yükseklik sisi (URP Fog + gerekiyorsa hafif ekran-alanı yükseklik sisi tam-ekran shader'ı), hafif renk derecelendirme; `WorldAtmosphere` rüzgâr/sis parametrelerini tek yerden sürer; Run ve pilot aynı profili kullanır (sahne dosyasına ek bileşen yerine kodla bağlama tercih edilir).
C. Parçacıklar — Assets/_Art/VFX/DD_Pollen.prefab ve `DD_SakuraPetals.prefab`: oyuncu çevresinde sınırlı hacim, rüzgârla sürüklenme, düşük alfa/küçük boyut (okunabilirlik), GPU instanced billboard; bölge etiketiyle yoğunluk (sakura yaprağı yalnız Doğu/avlu; polen genel) — bölge verisi WP-014'te gelir, şimdilik parametre.
D. Su v1 — `DD_Water_v1.shadergraph`: saydam, normal kaydırma ×2, derinlik solması, reflection probe/gök yansıması, kıyı köpüğü yok; AP-003B `pond_water_surface` materyaline bağlanır (AP-015 tam su shader'ı ayrı).
E. Kırılabilir — mevcut hasar/çarpma yoluna küçük bir `BreakableProp` bileşeni: vuruşta intact → `_BROKEN_LOD` mesh + tek parçacık patlaması + collider güncellemesi; mevcut prop hasar sistemi varsa ona bağlanır, yoksa minimal olay tabanlı bağlama (oynanış kuralı eklenmez).
F. Görsel doğrulama — `AP_LibraryShowcase` ve pilot sahnede 4 sabit kamera (rüzgâr açık/kapalı, sis açık/kapalı) görsel regresyon referansı; art-director tek yargı; okunabilirlik: gameplay kamerasında kapsül örtülme ve renk bandı kontrast ölçümü sis/parçacık açıkken.
G. Belge — `planning/04` WP-013 girişi + ADR-013-1 (atmosfer/rüzgâr parametre sözleşmesi); prompt kaydı `planning/work-packages/WP-013-*.md`; kısa README (Assets/_Art/VFX/README.md: parametreler, bütçe, bölge yoğunluk tablosu).
6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi/profil/UI kodu; prosedürel dünya builder'ı; spawn/silah düzeni; Import Contract v1 ve prefab üretim sözleşmesi (ADR-012-2) — yalnız materyal ataması genişler; Packages/ProjectSettings; Unity/URP sürümü; AP üretim klasörleri; planning karar metinleri.
7. TEST MATRİSİ (yalnız işle ilgili)
EditMode: materyal kapı testleri (bitki materyalleri `DD_Foliage_Wind`, eksik shader/keyword yok, SRP Batcher uyumlu), parçacık bütçe testi (maks. parçacık/emisyon sınırı), volume profili alan testi, `BreakableProp` durum testi, `ArtMaterialBuilder` idempotans. PlayMode (Metal): `PilotPerfProbe` tek koşu (WP-013 açık/kapalı A/B), okunabilirlik ölçümü, kırılabilir vuruş testi. Build/smoke: Player kodu değiştiği için release build bir kez + e1/e4 smoke; kullanıcı verisi restore 6/6 son koşudan sonra. Zamanlamaya duyarlı koşular (PilotPerf, build) öncesi C-00'a tek satır — P-05'in Blender batch'iyle çakışma kontrolü.
8. GIT VE TESLİM
İzole dalda tek commit, yalnız paket yolları (`--pathspec-from-file`), `git add -A` yok, LFS doğrulaması; mesaj `WP-013: M3 atmosphere & foliage VFX — wind/foliage shader, DEC-068 fog, pollen/petals, water v1, breakables` + `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. C-00'a ≤15 satır: SHA, dosya/LFS sayıları, shader/prefab listesi, test sonuçları, perf A/B (ort./p99 önce–sonra), okunabilirlik sonucu, art-director yargısı, devirler. C-00 `--ff-only` merge + etiket.
9. KRİTİK DURMA KOŞULLARI
VFX Graph/volumetric/başka paket zorunlu hale gelirse · Unity/URP sürümü · perf bütçesi yoğunluk düşürmeyle tutmuyorsa (ölçümü raporla) · rüzgâr şeması değişikliği gerekiyorsa (üretime devir) · okunabilirlik eşiği sis olmadan da tutmuyorsa (kullanıcı kararı §23 #4'e bağla, durma yerine raporla).
10. DEFINITION OF DONE
Bitki aileleri rüzgârla hareket ediyor, arka yüz aydınlanıyor, uzak yaprak kapsama korunuyor · sis DEC-068 görünümünde ve Run/pilot ortak · polen + sakura yaprağı parçacıkları bütçe içinde · su v1 gölette · `BreakableProp` çalışıyor · perf A/B bütçe içinde (sayılar notta) · okunabilirlik ölçümü PASS · art-director ACCEPT · testler yeşil, build + smoke geçti, restore 6/6 · `04` girişi + ADR-013-1 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
