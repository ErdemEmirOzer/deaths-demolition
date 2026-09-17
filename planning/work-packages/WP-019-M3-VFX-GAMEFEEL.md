# WP-019 — M3 VFX & GAME FEEL: AP-010 EFEKTLERİNİN UNITY'YE BAĞLANMASI (VURUŞ, PATLAMA, ZIRH KIRILMA, ÖLÜM, GOLD, SPECIAL BEACON, HASAR)

> Yürütme promptu (kullanıcı kararıyla C-00 doğrudan dağıttı, 2026-09-16) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-019.

OTURUM VE SAHİPLİK
Birincil sahip: S-07 — VFX & Game Feel (tek writer, tek commit owner). S-02 (hitscan/patlama olayları), S-03 (zombi ölüm/zırh kırılma olayları), S-04 (gold paketi/çekilme), S-09 (HUD beacon işareti) sözleşme soruları için danışılır; kod S-07. S-06 aynı anda WP-018'de (animasyon; kendi worktree'si): dosya sahipliği — S-06: Characters/CharacterAnimationSet.cs, Characters/ProceduralPoseBridge.cs, Enemies/ZombieAnimator.cs, Player/PlayerRigFactory.cs, Animator/klip asset'leri; S-07: VFX/GameFeel dosyaları ve olay dinleyicileri. ZombieEnemy.cs/PlayerSkinnedVisual.cs'e yalnız olay yayınlama ekle, mantığı değiştirme; ikinci biten özel dalını main'e rebase eder (paylaşılmamış dal; izinli) ve değişen modül testlerini tekrar koşar. Zırh kırılma efekti WP-017'nin ArmorVisualBinder.AnyBroken olay kancasına bağlanır (main'de).
Kadro: unity-specialist (Particle System/VFX materyalleri, Shader Graph flipbook, perf — ana yük), animation-specialist (efekt zamanlaması, ekran sarsıntısı/hit-stop eğrileri), csharp-developer (olay bağlama, havuzlama, testler), game-test-engineer (kalabalık VFX perf, okunabilirlik), art-director (efekt görünümü ↔ AP-010 önizlemeleri sadakat tek yargı; Fable 5.1).
Model: sanat yargısı Fable 5.1; kod/perf/test Opus 5. Adobe (§7) yalnız gerekirse doku finisajı (S-07'de doğrulandı).
Çalışma düzeni: HIZLI HAT; §5 sadakat (Unity efekt ↔ AP-010 GIF/kompozit yan yana). Yalnız §9'da dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · perf temiz 40 zırhlı skinned zombi + oyuncu ort. 6,32 / p99 7,60 ms · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z.
Branch/worktree: wp-019-m3-vfx-gamefeel · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-019/worktree/deaths-demolition · etiket önerisi wp-019-m3-vfx-gamefeel.
Kaynaklar (salt okunur): AP-010 README + exports/AP010-vfx-manifest-v002.json (34 varlık: sheet ızgara/fps/süre/döngü, emissive additive, origin_uv, dünya boyutları, tint kuralları), 06-renders/previews/ (GIF + sahne kompozitleri) · WP-013 Assets/_Art/VFX/ (DD_BreakBurst, drift prefabları, README sözleşmesi), WorldWind · WP-016 ZombieSkinnedVisual, PlayerSkinnedVisual (olay noktaları), CharacterAnimationSet · WP-017 ArmorVisualBinder (AnyBroken) · prototip HitscanEngine.cs, PropImpacts, patlama hasar yolu (WP-006), gold paketi/çekilme (WP-004/008), Special Weapon yaşam döngüsü (WP-007: spawn/alma/süre bitimi olayları, REQ-053 ışık sütunu + HUD), hasar alma (REQ-044) · planning/00 DEC-041/044/053/067/068/076/077, 01 REQ-014/053/063/082–090/104/105 · stil kılavuzu v1 (beacon/altın rezervleri) · proje hedefi "vuruş hissi, hit reaction, game feel".
Amaç: AP-010 kaynaklarını oyun olaylarına bağlı, havuzlanmış, bütçeli efekt sistemine dönüştürmek ve game feel katmanını (hit-stop, ekran sarsıntısı, hasar vinyeti, beacon) kurmak.

2. BAĞLAYICI KARARLAR
* Yerleşik Particle System + Shader Graph flipbook (VFX Graph yok — paket ekleme kullanıcı kararı); tüm efektler havuzlu (VfxPool), instance başına ≤ 1 malzeme; sheet/emissive additive sözleşmesi manifestten okunur (VfxCatalog ScriptableObject, manifest → asset dönüştürücü Editor aracı, idempotan).
* Olay bağlama (kod sözleşmeleri değişmez, yalnız dinlenir): HitscanEngine vuruş (yüzey tipine göre 5 vuruş + dekal), namlu (ateş olayı), patlama (patlama hasar olayı + şok + duman + yanık dekalı), zırh kırılma (AnyBroken), zombi ölüm (ölüm olayı + splat), gold paketi (WP-004 drop → AP-010 mesh + parıltı; çekilme izi REQ-014), Special Weapon beacon (WP-007 spawn → sütun + halka; alma/süre bitimi → söndürme; HUD işareti S-09 arayüzüne gri + runtime tint), oyuncu hasar (vinyet + hafif sarsıntı).
* Game feel (teknik varsayılan, ölçülü): hit-stop ≤ 40 ms (yalnız zırh kırılma ve ölüm), ekran sarsıntısı ≤ 0,15 m genlik/≤ 0,2 s (patlama, oyuncu hasar), hasar vinyeti alfa ≤ 0,5; hepsi GameFeelSettings ScriptableObject'te, kapatılabilir.
* Okunabilirlik DEC-077: efektler kapsülü ≥ 0,3 s örtmez (manifest sınırları uygulanır), beacon/altın rezervleri yalnız kendi ailesinde; dekaller zemin okunurluğunu bozmaz (alfa/ölçek tavanı).
* Perf bütçesi: Run+AP005 + 40 zombi + yoğun efekt senaryosu (10 vuruş/s + 1 patlama + 5 ölüm/s): ort. ≤ 7,0 ms, temiz p99 ≤ 12 ms; efekt GPU ≤ 1,0 ms; havuz taşması 0, GC alloc/kare 0 (efekt yolu).
* Paket/sürüm değişikliği yok; LFS aynen (34 PNG/mesh yeni pointer); push yok.

3. İZLENEBİLİRLİK
DEC-041/044/053/067/068/076/077 · REQ-014/053/063/082–090/104/105 · WP-006/007/004/008 olayları · WP-013 VFX sözleşmesi · WP-016/017 karakter/zırh kancaları · AP-010 handoff · S-09 (HUD işareti), S-08 (ses eşlemesi AP-011).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Vuruş/patlama/ölüm efektleri placeholder veya yok. 4.2 Gold paketi placeholder mesh. 4.3 Special beacon REQ-053'te yalnız kod/HUD, görsel yok. 4.4 Hasar vinyeti/sarsıntı yok. 4.5 Efekt havuzu yok. 4.6 Yüzey tipi (et/zırh/taş/ahşap/toprak) vuruş noktasında çözülmüyor.

5. UYGULAMA KAPSAMI
A. Import + VfxCatalog (manifest → asset), materyaller (flipbook additive/alpha, dekal, emissive), asset-gate testleri. B. VfxPool + VfxPlayer (sheet oynatma, süre, dünya ölçeği, origin_uv). C. Yüzey tipi çözümleme (SurfaceType: karakter/zırh bölgesi/prop materyali) + vuruş/dekal bağlama. D. Patlama/şok/duman/yanık; zırh kırılma; zombi ölüm + splat; gold paketi + çekilme izi; beacon sütun/halka + HUD işareti API'si; oyuncu hasar vinyeti + sarsıntı; hit-stop. E. GameFeelSettings. F. Ölçüm: yoğun efekt senaryosu perf (temiz pencere perf-clean.sh; başka Unity süreci varsa bekle), havuz/GC testleri, okunabilirlik (efekt aktifken DEC-077). G. Görsel: her aile Unity'de ↔ AP-010 GIF/kompozit yan yana; AD tek yargı. H. Belge: planning/04 WP-019 + ADR-019-1 (VFX sözleşmesi/havuz) + ADR-019-2 (game feel parametreleri) + AP tablosu (AP-010 Unity'de); prompt kaydı planning/work-packages/WP-019-*.md; Assets/_Art/VFX/README.md güncelleme.

6. DEĞİŞTİRİLMEYECEK KONULAR
Hasar/ekonomi/Special Weapon/gold kuralları (yalnız olay dinlenir); zırh durum makinesi; karakter prefab'ları (yalnız olay noktası); NavMesh/spawn; Packages/ProjectSettings; Unity/URP; AP klasörleri; planning karar metinleri; S-06'nın animasyon dosyaları.

7. TEST MATRİSİ
EditMode: catalog dönüştürücü idempotans, asset-gate (34), havuz kapasitesi, yüzey eşlemesi tablosu, game feel sınırları. PlayMode (Metal): olay → efekt tetikleme (8 aile), yoğun senaryo perf (temiz pencere), GC 0, okunabilirlik efekt aktif, beacon yaşam döngüsü. Build/smoke: release build bir kez + e1/e4; restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase (WP-018 önce merge olduysa) → tek commit (paket yolları, --pathspec-from-file, LFS doğrulama), mesaj "WP-019: M3 VFX & game feel — AP-010 effects bound to gameplay events, pooled, budgeted" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır. C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
VFX Graph/paket zorunluysa · perf bütçesi havuz/LOD ile tutmuyorsa (raporla) · AP-010 sözleşmesi uygulanamıyorsa (üretime devir) · rebase çakışması çözülemiyorsa (C-00'a).

10. DEFINITION OF DONE
34 varlık VfxCatalog'da, 8 aile oyun olaylarına bağlı ve havuzlu · yüzey tipi vuruşları · beacon/gold/hasar/game feel çalışıyor, ayarlanabilir · perf/GC/okunabilirlik notta · AD sadakat yargısı · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-019-1/2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.

11. KAPSAM GÜNCELLEMESİ (C-00 → S-07, kullanıcı direktifi §10–§12, 2026-09-16 — WP-019'a bağlayıcı, DoD'ye eklenir)
§10 Kan sistemi (gore YÜKSEK, uzuv kopması yok): her et vuruşunda yönlü sıçrama (vuruş normali + mermi yönü) + GPU damlacık; zemin/duvar kan dekalı round boyunca kalıcı (havuz ≥ 128, LRU solma; uzakta birleşik LOD); ölümde büyük sıçrama + büyüyen kan gölü; ağır vuruş/zırh kırılmada çoklu sıçrama. Kaynak şimdilik AP-010 v002 et/ölüm sheet'leri; AP-010B (P-05: ≥ 2048 flipbook, Mantaflow, 6 sıçrama + 6 zemin + 4 duvar + damlacık + göl) VfxCatalog slotlarıyla değiştirilebilir. Okunabilirlik DEC-077: kan koyu kızıl h 355–5° s 0,70–0,85 v 0,25–0,45 (skin1 aksanı ve beacon/uyarı rezervinden ayrı — ADR-019-2), kapsülü ≥ 0,3 s örtmez, dekaller gold/rotayı örtmez (alfa ≤ 0,85; gold 0,6 m dışlama). Perf: efekt GPU ≤ 1,5 ms (önceki 1,0), yoğun senaryo ort. ≤ 7,0 / p99 ≤ 12; dekal havuzu GC 0. 8 aile "yüksek kalite" kapısı: AD sadakat + yakın/uzak kamera A/B, README kare hızı/çözünürlük tablosu.
§11 Model: tasarım/AD Fable 5.1 (`wp-019/evidence/blood-design-brief-fable.md`), kod/ölçüm/test/Git Opus 5.
§12 Olay kapsam matrisi: `planning/work-packages/EVENT-COVERAGE-MATRIX.md` VFX sütunu (S-08 ses sütunu, rebase'de birleşir); boş satır = DoD FAIL; eksik kaynaklar AP-010B listesine.
Makine slot kuralı (21:30): her Unity batch `tools/slot.sh acquire unity wp-019` içinde (`wp-019/tools/chain-wp019b.sh`, `perf-ab.sh`).

12. GİRDİ GÜNCELLEMESİ (C-00, 2026-09-17): AP-010B v003 TESLİM — `asset-production/AP-010/exports/AP010-vfx-manifest-v003.json` (108 varlık, 48,9 MB; v002'yi `supersedes`/`slot` alanlarıyla kapsar, 14 v002 girdisi korunmuş). HQ geçişi 8/8 (patlama 4096² 48 kare, namlu 8×8 @60 — grid/fps/frames manifestten), kan seti gore YÜKSEK (6 sıçrama, damlacık, 6 zemin + 4 duvar dekal albedo/normal/height-MRA, gölet 8 maske, 3 ölüm + 2 ağır vuruş; canlı dekal bütçesi ≤ 24 zemin + 8 duvar + 6 gölet), wave-2 20 kalem (27 dosya, LA gri-tint sözleşmesi). Unity hedefi `Assets/_Art/VFX/AP010B/`. WP-019 v003 seti üzerinden kapatılır; v002'ye bağlanan kalemler slot alanıyla eşlenir; teslim notunda v003 kapsamı belirtilir.
