# WP-016 — M3 CHARACTER INTEGRATION: OYUNCU (AP-006) VE ZOMBİ (AP-007) UNITY'YE, SKINNED IMPORT SÖZLEŞMESİ, RUN'DA GERÇEK KARAKTERLER

> Yürütme promptu (C-00, 2026-09-16) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-016.

OTURUM VE SAHİPLİK
Birincil sahip: S-05 — World, Environment & Lighting (tek writer, tek commit owner; art pipeline sürekliliği). S-02 oyuncu görsel bağlama noktası, S-03 `ZombieAppearance`/`IEnemyVersionVisual`/spawner, S-06 Animator/Humanoid avatar soruları için danışılır; kod değişiklikleri S-05 yapar. S-10 devrede değil.
Kadro: unity-specialist (skinned import, Humanoid avatar, LODGroup, GPU skinning, perf — ana yük), csharp-developer (görünüm bağlama, spawner varyasyon kuralı, testler), game-test-engineer (kalabalık/okunabilirlik koşuları), art-director (Unity'de karakter görünümü ve konsept sadakati tek yargı; Fable 5.1).
Model: sanat yargısı Fable 5.1; import, kod, test, Git Opus 5.
Çalışma düzeni: HIZLI HAT bağlayıcı; Higgsfield direktifi §5 (sadakat: Unity'de render ↔ P-05/P-06 konsept yan yana). Ajanlar görev tamamlanana kadar kesintisiz çalışır; yalnız §9'da dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD `0866162eec51fd8886772cb5515a8e759906218c` (WP-015 r3; etiketler `wp-015-m3-asset-budget`, `-r2`; 18 etiket) · tracked 2125 · LFS 305 · EditMode DD.Art+ProjectIntegrity 135/135 · PlayMode DD.Art 16/16 · perf AP005 temiz ort. 3,38 / p99 4,09 ms @ 40 düşman · kullanıcı verisi tabanı `wp-010/backup/userdata-postManual-wp010-20260914T230321Z` (SaveVersion 3).
Branch/worktree: `wp-016-m3-character-integration` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-016/worktree/deaths-demolition` · etiket önerisi `wp-016-m3-character-integration`.
Kaynaklar (salt okunur): AP-006 `exports/` (`player_base.fbx` rig 56 kemik + LOD0/1/2 + `socket_weapon_r/l`, `socket_back`; `textures/` 6 PNG + manifest v002 — 3 skin = albedo takası; `player_rigtest_idle.fbx`, `player_rigtest_walk.fbx`; `AP006-skins-v002.json`; README §handoff AP-015) · AP-007 v002 `exports/` (3 gövde FBX, 19 modül FBX, atlas PNG + ID/tint maske, `AP007-anchors.json`, modules json, rigtest FBX; README spawner oran kuralı: cw2 ≤ %30, T7 ≤ %10, H0 ≤ %25, T6+B5 birlikte değil) · WP-012 `ArtImportContract`, `ArtFamilyPrefabBuilder`; WP-013 `WorldWind`; WP-014 `World_AP005`, `PlacementImporter`; WP-015 stil kılavuzu · prototip `Assets/_Prototype/Player/`, `Enemies/` (`ZombieAppearance`, `IEnemyVersionVisual`, `IArmorVisual`, `ArmorAnchors`), `Camera/CameraOcclusionFader` · `planning/00` DEC-018/009/010/077, `01` REQ-029/038/063/104/105.
Amaç: Prototip kapsüllerinin yerine gerçek oyuncu ve zombi karakterlerini Run'a almak: Import Contract'ın skinned uzantısı, Humanoid avatar/Animator, LOD, skin seçimi, zombi modüler görünüm üretimi (deterministik, oran kurallı), ankraj noktaları (AP-008 için hazır), 40 zombiyle perf ve motor içi okunabilirlik.
2. BAĞLAYICI KARARLAR

* Import Contract v1 → v1.1 skinned uzantısı (ADR-016-1): Humanoid avatar (AP-006 iskeleti tek kaynak; zombi avatarı aynı şablondan), `bakeAxisConversion`, normal/tangent import, LODGroup (3 seviye, eşikler ADR-012-1 mantığı), ORM → MetallicSmoothness + AO, skin = albedo takası (aynı materyal örneği), GPU skinning açık; doku sınıfı "character" 2K (ADR-015-1 tablosuna eklenir).
* Animasyon: AP-009 henüz yok → Animator Controller minimal: rig test idle/walk klipleri (Humanoid retarget), hız parametresiyle blend; oyuncu dönüşü REQ-038 mevcut kodla; saldırı/ölüm/hit reaction mevcut placeholder davranışları skinned mesh'e uygulanır (ragdoll yok). Klipler AP-009'da değişecek — bağlama noktası `CharacterAnimationSet` ScriptableObject.
* Zombi görünümü: `ZombieAppearance` gerçek modüllerle (3 fit × modüller × 2 renk yolu), seed = spawn ordinal (deterministik, test edilebilir), oran kuralı (README), versiyon tint/emissive yalnız maske kanalları (IEnemyVersionVisual placeholder paritesi korunur); ankrajlar `ArmorAnchors` ile eşlenir (AP-008 gelince WP-017). Kalabalıkta malzeme ≤ 2/zombi.
* Oyuncu: skin seçimi (DEC-037/039) mevcut Skin & Weapon Selection akışına bağlanır (3 skin albedo seti; UI thumbnail S-09 sonraki); `socket_weapon_r` mevcut silah tutuş noktası olur (klasik silah placeholder'ı sokete).
* Kapsül/çarpışma: oynanış kapsülü aynen (1,8 × r 0,45), görsel skinned mesh child; NavMesh/agent parametreleri değişmez.
* Okunabilirlik DEC-077 motor içinde gerçek karakterlerle ölçülür (oyuncu/çevre ≥ 0,06; zombi/çevre eşik; oyuncu/zombi ayrımı) — AP-007'nin "motor içinde yeniden ölç" devri burada kapanır. Skin 2 kullanıcı kararı (2a/2b) geldiğinde tek doku takası; gelmezse 2a ile devam, notta belirt.
* Perf bütçesi (teknik varsayılan): Run+AP005 + 40 skinned zombi + oyuncu: ort. ≤ 6,0 ms, temiz p99 ≤ 10,0 ms; GPU skinning + LOD ile.
* AP-005B `placement-delta-v002.json` bu paket sürerken gelirse aynı pakette delta import (idempotan); gelmezse WP-017'ye.
* Paket/sürüm değişikliği yok; LFS aynen; push yok.

3. İZLENEBİLİRLİK
DEC-009/010/018/037/039/077 · REQ-029/038/046/063/104/105 · ADR-011-1, ADR-012-1/2, ADR-014-2, ADR-015-1 · AP-006/AP-007 handoff'ları · AP-009 (klipler), AP-008/WP-017 (zırh), AP-014 (versiyon), AP-012 (UI thumbnail).
4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Import Contract skinned mesh/avatar kapsamıyor. 4.2 Oyuncu ve zombi = kapsül + placeholder görünüm. 4.3 Animator yok. 4.4 Skin seçimi görsel etkisi placeholder renk. 4.5 Ankrajlar kodda adlandırılmış, modelle bağlanmamış. 4.6 Motor içi karakter okunabilirlik ölçümü yok.
5. UYGULAMA KAPSAMI
A. Import — `Assets/_Art/Characters/Player/` ve `Characters/Zombie/`; contract v1.1 + asset-gate testleri (avatar geçerli, kemik 56, LOD sayısı, doku sınıfı, maske kanalları).
B. Prefab üretimi — `ArtCharacterPrefabBuilder` (Editor): `Player_AP006` (LODGroup, Animator, soketler, skin seti), `Zombie_AP007_Base` + modül kütüphanesi asset'i (fit/modül/renk yolu); idempotan.
C. Bağlama — oyuncu görsel child + skin seçimi; `ZombieAppearance` gerçek modül üretimi + oran kuralı + seed; `ArmorAnchors` eşleme; `CharacterAnimationSet` + minimal Animator; `WorldWind` etkisi yok (karakterde rüzgâr yok).
D. Ölçüm — `WorldPerfProbe` 40 zombi skinned (temiz pencere kuralı: tools/perf-clean.sh), okunabilirlik (DEC-077, gerçek karakterler), varyasyon dağılımı testi (1000 spawn: oran kuralı içinde, tekrar oranı).
E. Görsel doğrulama — Unity'de oyuncu 3 skin + 40 zombi gameplay kadrajı ↔ P-05/P-06 konsept/render yan yana (§5 sadakat), art-director tek yargı.
F. (Koşullu) AP-005B delta import + NavMesh yeniden bake + parite testi.
G. Belge — `planning/04` WP-016 + ADR-016-1 (skinned contract) + ADR-016-2 (zombi görünüm üretim sözleşmesi) + AP tablosu (AP-006/007 Unity'de); prompt kaydı; Assets/_Art/Characters/README.md.
6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi/profil/UI kuralları (skin seçimi yalnız görsel bağlama); kapsül/NavMesh/spawn parametreleri; düşman AI; Import Contract v1 mevcut kuralları (uzatılır); Packages/ProjectSettings; Unity/URP sürümü; AP üretim klasörleri; planning karar metinleri.
7. TEST MATRİSİ
EditMode: contract v1.1 asset-gate (oyuncu + zombi), prefab builder idempotans, `ZombieAppearance` determinizm/oran kuralı, skin seti eşlemesi, ankraj eşlemesi, (koşullu) placement delta paritesi. PlayMode (Metal): Run'da oyuncu + 40 zombi 60 s (`WorldPerfProbe`, temiz pencere), okunabilirlik testi, Animator idle/walk geçişi, skin seçim akışı 1 tur, spawn/ölüm/gold akışı 1 round. Build/smoke: Player kodu + Run değiştiği için release build bir kez + e1/e4 smoke; kullanıcı verisi restore 6/6 son koşudan sonra. Zamanlamaya duyarlı koşular: perf-clean döngüsü.
8. GIT VE TESLİM
İzole dalda tek commit (paket yolları, `--pathspec-from-file`, `git add -A` yok, LFS doğrulaması: yeni FBX/PNG pointer); mesaj `WP-016: M3 character integration — AP-006 player & AP-007 zombie in Run, skinned import contract v1.1` + `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. C-00'a ≤15 satır: SHA, dosya/LFS, prefab/modül sayıları, testler, perf (temiz), okunabilirlik, sadakat yargısı, skin 2 durumu, delta import durumu, devirler. C-00 `--ff-only` merge + etiket.
9. KRİTİK DURMA KOŞULLARI
Animation Rigging/başka paket zorunluysa · Unity/URP sürümü · perf bütçesi GPU skinning + LOD ile tutmuyorsa (raporla; 40 tavanı DEC-023 teknik güvenlik tavanı kararı) · AP-006/007 export'u contract'a uymuyorsa (üretime devir, geçici çözüm yok) · Humanoid avatar üretilemiyorsa.
10. DEFINITION OF DONE
Contract v1.1 yazılı + testli · `Player_AP006` 3 skin ile Run'da, skin seçimi çalışıyor · `Zombie_AP007` 40'lık kalabalıkta deterministik varyasyonla spawn oluyor · ankrajlar eşli · Animator idle/walk · perf ve DEC-077 ölçümleri notta · art-director sadakat yargısı · (koşullu) delta import · testler yeşil, release build + smoke, restore 6/6 · `04` girişleri + ADR-016-1/2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
