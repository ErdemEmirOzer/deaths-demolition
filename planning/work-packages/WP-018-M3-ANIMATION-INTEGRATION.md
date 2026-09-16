# WP-018 — M3 ANIMATION INTEGRATION: AP-009 KLİP SETİ, ANIMATOR/BLEND TREE, OLAY SÖZLEŞMESİ, HIT REACTION VE ÖLÜM

> Yürütme promptu (C-00, 2026-09-16, kullanıcı kararıyla doğrudan dağıtım) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-018.

OTURUM VE SAHİPLİK
Birincil sahip: S-06 — Animation (tek writer, tek commit owner). S-02 (oyuncu kontrol/nişan/ateş olayları), S-03 (zombi durum makinesi, saldırı vuruş penceresi DEC-029), S-05 (karakter prefab/contract v1.1) sözleşme soruları için danışılır; kod S-06. S-07 aynı anda WP-019'da (efekt bağlama; kendi worktree'si): dosya sahipliği — S-06: Characters/CharacterAnimationSet.cs, Characters/ProceduralPoseBridge.cs, Enemies/ZombieAnimator.cs, Player/PlayerRigFactory.cs, Animator Controller/klip asset'leri; S-07: VFX/GameFeel dosyaları ve olay dinleyicileri. ZombieEnemy.cs/PlayerSkinnedVisual.cs'e ikiniz de dokunacaksanız yalnız olay yayınlama ekleyin, mantığı değiştirmeyin; ikinci biten özel dalını main'e rebase eder (paylaşılmamış dal; izinli) ve değişen modül testlerini tekrar koşar.
Kadro: unity-specialist (Humanoid import, Animator/blend tree, IK/katman, perf — ana yük), character-animator (klip zamanlaması, geçişler, olay eşlemesi), csharp-developer (olay sözleşmesi, hit reaction/ölüm kodu, testler), game-test-engineer (kalabalık animasyon perf, olay doğrulama), art-director (hareket dili sadakati ↔ AP-009 GIF'leri tek yargı; Fable 5.1).
Model: sanat yargısı Fable 5.1; kod/perf/test Opus 5.
Çalışma düzeni: HIZLI HAT; §5 sadakat (Unity hareket ↔ AP-009 kontak/GIF yan yana). Yalnız §9'da dur. Yeni ürün kararı verme.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · perf temiz 40 zırhlı skinned zombi + oyuncu ort. 6,32 / p99 7,60 ms · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z (SaveVersion 3; probe'lar izole; gerçek profile dokunulmaz).
Branch/worktree: wp-018-m3-animation-integration · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-018/worktree/deaths-demolition · etiket önerisi wp-018-m3-animation-integration. Başlangıçta doğrula: HEAD/etiket, porcelain 0, LFS pointer bütünlüğü.
Kaynaklar (salt okunur): ~/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-009/README.md + exports/AP009-clips-manifest.json (schema, fps 30, skeleton, events, layers, speed_reference, 30 clips: oyuncu idle / walk_fwd / walk_back / strafe_left / strafe_right / fire_recoil / hit_light / death_fwd / death_back / pickup_special; zombi idle ×2, shamble ×3, run, attack lunge/sweep/swipe, hit_light L/R, hit_heavy_stagger, bölgesel ×3, death forward/backward/side/kneel, spawn/rise), 30 FBX (yalnız iskelet + animasyon), 06-renders/ kontak sayfaları/GIF · WP-016 ArtCharacterContract v1.1 (Humanoid avatar, 52 kemik şablonu, klip import kuralları), CharacterAnimationSet, ProceduralPoseBridge (A-pose köprüsü — kapanacak), PlayerSkinnedVisual, ZombieSkinnedVisual; WP-017 ArmorVisualBinder (Broken/AnyBroken olayı → ağır sendeleme), zırh mesh'leri (deformasyon kontrolü) · prototip Enemies/ZombieAnimator.cs, ZombieEnemy.cs (durum makinesi), Player/ (hareket 8-yön, REQ-038 fare yönü, ateş, hasar alma REQ-044), HitscanEngine · planning/00 DEC-021/029/032/033, 01 REQ-038/043/044/046/047 · proje hedefi "vuruş hissi, hit reaction, game feel".
Amaç: AP-009 setini Unity Humanoid'e almak, oyuncu ve zombi Animator'larını blend tree/katman/olaylarla kurmak, saldırı vuruş penceresini animasyon olayına bağlamak (DEC-029), hit reaction/ölüm çeşitliliğini ve zırh kırılma sendelemesini çalıştırmak; WP-016'nın A-pose köprüsünü ve alçak silah tutuşunu kapatmak.

2. BAĞLAYICI KARARLAR
* Import: contract v1.1 klip kuralları (Humanoid, root motion kapalı, döngü bayrakları manifestten, olaylar manifestten AnimationEvent'e dönüştürülür — AnimationClipEventImporter Editor aracı, idempotan); klipler Assets/_Art/Characters/Animations/.
* Oyuncu Animator: locomotion 2D blend tree (ileri/geri/sol/sağ + idle; hız parametresi speed_reference ile ölçekli), üst gövde aim_layer (avatar maskesi; nişan yönü REQ-038, kodla sürülen dönüş korunur), additive fire_recoil, hit_light (katman, hareketi kesmez — REQ-044), ölüm ×2 (rastgele, kök sabit), pickup_special (WP-007 alma olayı; kısa kilit). Silah tutuşu socket_weapon_r ile hizalanır (alçak tutuş devri kapanır).
* Zombi Animator: idle ×2 / shamble ×3 (fit A/B/C eşlemesi) / run (Special/Elite/Ultra hız katmanı, DEC-021 zaman ölçeği), attack ×3 (rastgele; hit_window_start/end olayları hasar penceresini sürer — kod zamanlayıcısı kaldırılır, DEC-029/REQ-043), hit_light L/R (vuruş yönüne göre; katman), bölgesel ×3 (baş/gövde/bacak; HitscanEngine bölge bilgisi), hit_heavy_stagger (yalnız AnyBroken), death ×4 (rastgele, kök sabit, ragdoll yok, death_end olayında havuza dönüş), spawn/rise (12 spawn noktasında opsiyonel; DEC-023 aktif düşman sayımı rise sırasında da sayılır).
* Kalabalık perf: Animator culling (görünmeyen → CullUpdateTransforms), LOD'a bağlı güncelleme (LOD2+ 15 fps), GPU skinning; bütçe: Run+AP005 v2 + 40 zırhlı zombi + oyuncu animasyonlu: ort. ≤ 7,0 ms, temiz p99 ≤ 10 ms; GC alloc/kare 0 (animasyon yolu).
* Olay sözleşmesi (ADR-018-1): CharacterAnimationEvents (fire, hit_window_start/end, footstep, death_end, pickup, breath) — S-07 (efekt) ve S-08 (ses, AP-011) bu olayları dinler; ProceduralPoseBridge kaldırılır.
* Paket/sürüm değişikliği yok (Animation Rigging yok — IK gerekiyorsa yerleşik Humanoid IK); LFS aynen (30 FBX yeni pointer); push yok.

3. İZLENEBİLİRLİK
DEC-021/023/029/032/033/077 · REQ-038/043/044/046/047 · ADR-016-1 (contract v1.1) · WP-017 AnyBroken · AP-009 handoff · S-07 WP-019 (olay dinleyicileri), S-08 AP-011 (ayak sesi/saldırı sesi olayları), AP-014 (versiyon hız/animasyon varyantları).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Yalnız rig test idle/walk; ProceduralPoseBridge A-pose köprüsü. 4.2 Zombi saldırı hasarı zamanlayıcıyla, animasyona bağlı değil. 4.3 Hit reaction/ölüm görsel yok. 4.4 Oyuncu nişan katmanı yok; silah tutuşu alçak. 4.5 Kalabalık animasyon culling yok.

5. UYGULAMA KAPSAMI
A. Klip import + olay dönüştürücü + asset-gate testleri (30 klip, döngü/olay/uzunluk manifestle eş). B. Oyuncu Animator Controller + blend tree + aim katmanı + additive recoil + ölüm/pickup; PlayerSkinnedVisual bağlama; socket_weapon_r hizası. C. Zombi Animator Controller + fit/hız eşlemesi + saldırı penceresi olayları (ZombieEnemy hasar yolu olaya bağlanır) + hit/bölgesel/stagger/ölüm/rise; ZombieSkinnedVisual/ZombieAnimator bağlama. D. CharacterAnimationEvents sözleşmesi + ProceduralPoseBridge kaldırma. E. Perf: culling/LOD güncelleme, WorldPerfProbe animasyonlu kalabalık (temiz pencere: tools/perf-clean.sh kalıbı — Blender ve başka Unity < %50 60 s; koşuda >%50 → at, 5 dk sonra tekrar, ≤6 deneme), GC testi. F. Doğrulama: DEC-029 penceresi testi (vuruş yalnız pencere içinde), ölüm→havuz, hit reaction hareketi kesmiyor (REQ-044), okunabilirlik DEC-077 hareketli (anticipation 19.9 m'de görünür). G. Görsel: Unity 6 kare kontak ↔ AP-009 kontak sayfaları; AD tek yargı. H. Belge: planning/04 WP-018 + ADR-018-1 (olay sözleşmesi) + ADR-018-2 (Animator/katman/culling politikası) + AP tablosu (AP-009 Unity'de); prompt kaydı planning/work-packages/WP-018-*.md; Assets/_Art/Characters/README.md güncelleme.

6. DEĞİŞTİRİLMEYECEK KONULAR
Hasar/zırh/ekonomi kuralları (yalnız pencere kaynağı animasyona geçer, sayısal değerler aynı); hız/kapsül/NavMesh parametreleri; spawn kuralları; Import Contract mevcut kuralları; Packages/ProjectSettings; Unity/URP; AP klasörleri; planning karar metinleri; S-07'nin VFX dosyaları.

7. TEST MATRİSİ
EditMode: klip asset-gate (30), olay dönüştürücü idempotans, Animator parametre/durum tablosu, fit/hız eşlemesi, CharacterAnimationEvents sözleşme testi. PlayMode (Metal): saldırı penceresi (pencere dışı vuruş 0), hit reaction hareket kesmez, ölüm→havuz, pickup akışı, aim katmanı yön testi, animasyonlu kalabalık perf (temiz pencere), GC 0. Build/smoke: release build bir kez + e1/e4; kullanıcı verisi restore 6/6 son koşudan sonra.

8. GIT VE TESLİM
Özel dal main'e rebase (WP-019 önce merge olduysa) → tek commit (paket yolları, --pathspec-from-file, git add -A yok, LFS doğrulama), mesaj "WP-018: M3 animation integration — AP-009 clips, Animator/blend trees, animation event contract, hit reactions & deaths" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır (SHA, dosya/LFS, klip/Animator sayıları, testler, perf, DEC-029 testi, AD yargısı, devirler). C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Animation Rigging/başka paket zorunluysa · Unity/URP · perf bütçesi culling ile tutmuyorsa (raporla) · klipler contract'a uymuyorsa (üretime devir) · saldırı penceresi animasyona bağlanınca hasar sayıları değişiyorsa (S-03 ile dengeleme; raporla, değeri değiştirme).

10. DEFINITION OF DONE
30 klip import + olaylar · oyuncu/zombi Animator'ları blend tree/katmanlarla çalışıyor · saldırı penceresi animasyon olayında (DEC-029) · hit reaction/bölgesel/stagger/ölüm ×4/rise/pickup çalışıyor · ProceduralPoseBridge kaldırıldı, silah tutuşu düzgün · perf/GC/okunabilirlik notta · AD sadakat yargısı · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-018-1/2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
