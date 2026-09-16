# WP-017 — M3 WORLD v2 + ARMOR: AP-005B DELTA IMPORT, AP-008 ZIRH DURUM BAĞLAMA, DOKU BÜTÇESİ v2

> Yürütme promptu (C-00, 2026-09-16) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-017.

OTURUM VE SAHİPLİK
Birincil sahip: S-05 (tek writer, tek commit owner). S-03 `IArmorVisual`/`ArmorSet`/spawner kuralları için, S-07 kırılma efekti bağlantı noktası için danışılır; kodu S-05 yazar. S-10 devrede değil.
Kadro: unity-specialist (import, prefab, LOD, perf — ana yük), csharp-developer (zırh görsel durum makinesi, spawner kural motoru, testler), game-test-engineer (kalabalık/okunabilirlik/durum koşuları), art-director (motor içi zırh okunurluğu ve sadakat tek yargı; Fable açılırsa Fable, değilse Opus — sapma notta).
Çalışma düzeni: HIZLI HAT; Higgsfield §5 sadakat (Unity render ↔ AP-005B/AP-008 renderleri yan yana). Yalnız §9'da dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD `5e35041b2fabdbe78c00bc60987913f28b4de313` · etiket `wp-016-m3-character-integration` (19 etiket) · tracked 2275 · LFS 344 · EditMode 146/146 · PlayMode 20/20 · perf temiz 40 skinned zombi + oyuncu ort. 5,14 / p99 5,91 · doku sevk 158,7 / 160 MB · kullanıcı verisi tabanı `wp-010/backup/userdata-postManual-wp010-20260914T230321Z`.
Branch/worktree: `wp-017-m3-world-v2-armor` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-017/worktree/deaths-demolition` · etiket önerisi `wp-017-m3-world-v2-armor`.
Kaynaklar (salt okunur): AP-005 README v002 eki + exports/placement-delta-v002.json (+202 / 9 değişen / −169; `material_override` eşlemeleri), `exports/families/` 6 yeni prop ailesi (FBX + 18 PNG; Tripo ORM: metalik 0 / AO yok), AP-002 kullanılmamış 8 ailenin prefab'ları (WP-012'de var) · AP-008 README + exports/AP008-armor-manifest.json (aile/varyant/durum/ankraj/mesh; IArmorVisual eşlemesi; dışlama kuralları), 18 FBX + atlas · WP-016 `ArtCharacterContract` v1.1, `ArtCharacterPrefabBuilder`, `ZombieModuleLibrary`, `ZombieLookSelector`, `ZombieSkinnedVisual` · WP-006 `ArmorSet`/`IArmorVisual`/`ArmorAnchors` · WP-014 `PlacementImporter`, `World_AP005` · ADR-015-1 doku tablosu · `planning/00` DEC-010/077, `01` REQ-063/080–084.
Amaç: Unity dünyasını AP-005B v2'ye getirmek (saray çatısı, duvarlar, kapılar, pavyonlar, prop'lar), AP-008 zırhını `ArmorSet` durumlarına bağlayıp kalabalıkta çalıştırmak, doku bütçesini karakter+zırh gerçeğine göre güncellemek.
2. BAĞLAYICI KARARLAR

* Delta import idempotan: v1 instance'ları korunur; −169 kaldırma listesi uygulanır; `material_override` eşlemesi; NavMesh yeniden bake; 12/12 + 12/12 + 49/49 parite testleri tekrar.
* Zırh: `Zombie_AP007` prefab'ına 3 ankraj altında aile/varyant mesh'leri; `IArmorVisual` Absent/Present/Damaged/Broken → mesh/materyal takası (Damaged = durum maskesi; Broken = artık mesh + bölge açık); spawn'da `ArmorSet` bileşimi (WP-006/010 kuralları) → görsel; varyant seçimi `ZombieLookSelector` seed'iyle deterministik; AP-008 dışlama kuralları kıyafet seçimiyle birlikte çözülür (Body ⇒ T7/T6/T2 yok…). Kırılma anında olay (`ArmorBroken(region)`) S-07 efektine açık kanca; efekt bu pakette yok.
* Doku bütçesi (ADR-015-1 v2, teknik karar): karakter sınıfı ayrı bütçe — sevk toplamı ≤ 200 MB (dünya ≤ 120 + karakter/zırh ≤ 80); aşarsa zırh atlası 1K'ya iner, ölçüm nota.
* Zırh okunurluğu DEC-077 motor içi: gameplay kamerasında zırhlı/zırhsız bölge ayrımı ΔV ölçülür; AD önerisi (min 0,10 / ort 0,15 + 88 px biçim testi) uygulanır; lake açılmaz; sonuç ADR-017-1 olarak yazılır, S-10 M3 kapanışında doğrulanır.
* Perf bütçesi: Run+AP005 v2 + 40 zırhlı skinned zombi + oyuncu: ort. ≤ 6,5 ms, temiz p99 ≤ 10 ms (zırh malzemesi ≤ 1 ek draw/zombi).
* Paket/sürüm değişikliği yok; LFS aynen; push yok.

3. İZLENEBİLİRLİK
DEC-010/077/078/079 · REQ-063/080–084/104/105 · ADR-014-1/2, ADR-015-1, ADR-016-1/2 · AP-005B / AP-008 handoff · AP-014 (tint Mask.R), S-07 (kırılma efekti), S-10 (motor içi ΔV doğrulama).
4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Unity dünyası v1 (2354); saray çatısız, duvar/kapı yok. 4.2 6 prop ailesi Unity'de yok; AP-002'nin 8 ailesi prefab olarak var ama yerleşmemiş. 4.3 Zırh görseli yok; `IArmorVisual` placeholder. 4.4 Doku sevk bütçesi dolu. 4.5 Zırh dışlama kuralları kıyafet seçicide yok.
5. UYGULAMA KAPSAMI
A. Prop aileleri import (contract v1; Tripo ORM eksikleri: metalik 0 sabit, AO yok — nota) + prefab. B. Delta import + kaldırma + override + NavMesh + parite. C. AP-008 import (contract v1.1 skinned/rijit) + `ArmorVisualBinder` (durum makinesi) + `ZombieLookSelector` dışlama kuralları + spawn bileşimi bağlama. D. Doku bütçesi v2 + streaming; ölçüm. E. Ölçüm: `WorldPerfProbe` temiz pencere (zırhlı kalabalık), DEC-077 motor içi (oyuncu/çevre, zombi/çevre, zırhlı/zırhsız bölge), durum geçiş testi (Present→Damaged→Broken, olay). F. Görsel: 5 bölge gameplay kadrajı ↔ AP-005B v003 renderleri; zırhlı kalabalık ↔ AP-008 kompoziti; AD tek yargı. G. Belge: `planning/04` WP-017 + ADR-017-1 (zırh okunurluk ölçütü) + ADR-015-1 v2 notu + AP tablosu (AP-005B, AP-008 Unity'de); prompt kaydı; README güncellemeleri.
6. DEĞİŞTİRİLMEYECEK KONULAR
Zırh hasar kuralları (`ArmorSet` mantığı — yalnız görsel bağlanır); oynanış/ekonomi/profil/UI; kapsül/NavMesh agent parametreleri; spawn/rota/marker düzeni; Import Contract mevcut kuralları (uzatılır); Packages/ProjectSettings (QualitySettings streaming bütçesi hariç); Unity/URP sürümü; AP klasörleri; planning karar metinleri.
7. TEST MATRİSİ
EditMode: delta paritesi (2387, kaldırma listesi, override), prop/zırh asset-gate, `ArmorVisualBinder` durum tablosu, dışlama kuralı testi (1000 spawn: yasak kombinasyon 0, oran kuralları), doku bütçesi testi. PlayMode (Metal): NavMesh/spawn/rota paritesi, `WorldPerfProbe` temiz pencere, DEC-077 ölçümü, zırh kırılma görsel geçişi (hasar yoluyla) 1 round. Build/smoke: release build bir kez + e1/e4; restore 6/6.
8. GIT VE TESLİM
Tek commit (paket yolları, `--pathspec-from-file`, LFS doğrulama), mesaj `WP-017: M3 world v2 & armor — AP-005B delta, AP-008 armor states, texture budget v2` + `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. C-00'a ≤15 satır. C-00 ff-merge + etiket.
9. KRİTİK DURMA KOŞULLARI
Paket/sürüm · perf bütçesi zırhla tutmuyorsa (raporla; 40 tavanı DEC-023) · delta şeması uyumsuz (üretime devir) · zırh mesh'leri ankrajlara uymuyorsa (AP-008 sürüm, P-06) · doku bütçesi 1K ile de tutmuyorsa (raporla).
10. DEFINITION OF DONE
Dünya v2 (2387) NavMesh/parite ile · 6 prop ailesi + AP-002 mimari Unity'de · zırh durumları çalışıyor (spawn bileşimi, kırılma geçişleri, olay kancası), dışlama kuralları · doku bütçesi v2 ölçülü · perf ve DEC-077 (zırh dâhil) notta · AD yargısı · testler yeşil, build + smoke, restore 6/6 · `04` girişleri + ADR-017-1 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
