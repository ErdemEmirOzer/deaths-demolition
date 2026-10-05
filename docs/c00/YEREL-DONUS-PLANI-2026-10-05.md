# YEREL DÖNÜŞ PLANI — yerel C-00 açılınca ilk okunacak belge (05.10.2026, bulut C-00)

**Kimden:** bulut C-00 (ikinci hesap, tarayıcı). **Kime:** Mac'te açılan yerel C-00 (birinci hesap limiti yenilenince, ~07.10 11:00 TR tahmini, ya da ikinci hesap Claude Desktop'ta açılınca).

**Ne zaman geçerli:** yerel C-00 açıldığı anda entegratörlük ona geçer. Bulut oturumu `docs/c00/BULUT-DEVIR-<gg.aa>.md` yazar ve C-00B rolüne döner.

**Önce:** bu belge → `BRIFING-YENI-HESAP-2026-10-05.md` §14 → `C00-DEVIR-2026-10-05.md` SON DURUM EKİ → `BEKLEYEN-MESAJLAR.md` §18–§19.

`K` = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition` · Repo = `/Users/Erdem/Documents/Deaths Demolition`.

---

## 0. Bulutta ne oldu (özet)

- **main'e bulutta HİÇBİR ŞEY alınmadı.** Bulut C-00, 16 commit'lik zinciri `git cherry-pick` + `git tag -a` ile almaya çalıştı. Bulut oturumunun izin sistemi işlemi reddetti ("merge without review"). Kurala göre bir oturumun izin sisteminin reddettiği işlem başka yoldan yapılmaz; karar kullanıcıya bırakıldı. Zincir olduğu gibi duruyor ve aşağıdaki §5 sırasıyla yerelde `tools/integrate.sh` ile alınacak.
- **GitHub main = `3e68bc08`.** Brifing yerel main'i `3bb501e7` diye anıyor; `3e68bc08` onun üstünde yalnız belge commit'idir. Yerel worktree'lerdeki WIP commit'ler GitHub'da yok.
- **C-00B / bulut C-00 teslimleri** (hepsi dalda, PR'da):
  - B-01…B-04: PR 1–4.
  - B-05 (bayat bilgi eşitlemesi + r2 sözleşme yolu + BEKLEYEN §18–§19 + bu plan): PR 5.
- **r2 kapı denetleyicisi** (`tools/qa/r2_gates_lint.py`), B-05 dalında: 15 yazıcı · **ERROR 0** · WARN 14 · INFO 14. Ayrıntı BEKLEYEN §18.

## 1. İlk 60 dakika (yerel C-00)

1. Durumu doğrula:
   ```bash
   cd "/Users/Erdem/Documents/Deaths Demolition" && git fetch -q origin && git log --oneline -3 main && git log --oneline -1 origin/main
   git worktree list
   df -h / "/Volumes/TOSHIBA EXT"
   ```
   Yerel main `3bb501e7` ya da üstü olmalı; `origin/main` `3e68bc08`. Yerel main `origin/main`'in gerisindeyse `git merge --ff-only origin/main`.
2. `tools/compile-check.sh main` (arka planda). `03c3b06b` ve `a62d83db` henüz derlenmedi; son PASS `d0783d9b`.
3. **Bulut zincirini entegre et** (§5). Hepsi belge ve Python; derleme etkisi yok. Bitince `python3 -m unittest tools/qa/tests/test_r2_gates_lint.py tools/qa/tests/test_r2_gates.py` ve `python3 tools/qa/r2_gates_lint.py` (ERROR 0 beklenir). Sonra `GIT_LFS_SKIP_PUSH=1 git push origin main` ve etiketler. PR 1–5'e "main'e alındı: <SHA>, etiket <etiket>" yazıp kapat.
4. Oturumları aç (§3 mesajları, sıra): S-06 → S-03 → S-08 → S-09 → S-02 → S-01; sonra S-05, S-10, S-07.
5. AP-024C ve AP-025D'nin diskteki durumuna bak; yarımsa §4 promptlarıyla yeniden başlat.
6. Kullanıcıya "devraldım" mesajı ve görselli durum sayfası.

## 2. Yerel kuyruk, sıralı (`LOCAL-QUEUE.md`'nin yürütme sırası)

Her Player/PlayMode koşusu `tools/userdata-guard.sh` penceresinde; exit 32'de `tools/qa/oos_rule.py`. Unity ve Blender `tools/slot.sh` ile. Disk ≥ 15 GB, swap ≤ 12 GB.

| Sıra | Sahip | İş | Beklenen | Not |
|---|---|---|---|---|
| 1 | C-00 | `tools/compile-check.sh main` | errorsCS 0 | `03c3b06b`, `a62d83db` ilk kez derlenir |
| 2 | C-00 | Bulut zinciri (§5) `integrate.sh` ile | zincirin tamamı main'de, `git cherry` "+" yok | belge + Python; `--allow-paths docs/` |
| 3 | S-06 | B2d/B2e: `dee54ffa` WIP'ini temiz commit'e çevir → C-00 entegre | H1 PASS, KundakOmuz PASS, ar_2 bilinen kırmızı | runner exit-32 değişikliğine dokunma (kullanıcı onayı yok) |
| 4 | C-00 | `tools/main-ci.sh <sha>` (S-06'dan sonra) | kırmızı yok ya da sahipli liste | sürücü `K/wp-079/driver` |
| 5 | S-03 | WP-084 dilim 1 alım gövdesi (intake, builder, SlotFace, RL bitleri, testler) → Z2/Z3/Z3b/Z4/DEC-077 | Z3b sınırları içinde | önce durum çıkarma (§3 S-03 mesajı); G1 düzeltmesi SlotFace'ten önce iyi olur |
| 6 | S-08 | AP-027 v003g overlay → S4 v2 → 3B mono ölçümü → allpass dinleme → **ar_1/sg_1 dinleme kiti** | S4 PASS, mono çentik hedefleri, kit silah ≥ 6 dB | LFS'li teslim: önce `git lfs push origin main` |
| 7 | S-09 | B + yastık teslimi: 5 oran + 60 s MP4 → kullanıcı; `bcde16dc` + `8ba256ec` + yeni commit | kontrast FAIL 0, kenar ≤ 1,5× | sonra ikon v2, yükleme alımı, U1b |
| 8 | S-02 | tutuş açıkken tepme pivota: sol el ≤ 1 cm (WIP `6ea4492a`) | auto 10 atış, iki el ≤ 1 cm | `WeaponHoldIk` S-06 dosyası: S-06 ile API kararı önce |
| 9 | P/ajan | AP-024C iş 2/3/5; AP-025D giysi + jaw (§4) | kontrol noktası kareleri | Blender slot kalıbı |
| 10 | S-01 | D8 kuyruk regresyonu → temiz commit → **D9 rollout** (dalga sakinleşince) | guard testleri yeşil | göç notu `docs/c00/GUARD-D8-GOC.md`; `DD_GUARD_QUEUE_WAIT` sapması C-00 onayı bekliyor |
| 11 | S-03 · S-08 · S-02 · S-06 | B-03 Profiler doğrulaması (rapor §5, BEKLEYEN §19) | bulgular ölçüldü; düzeltmeler sahiplerde | G1, G2 öncelikli |
| 12 | C-00 | `K/wp-068s9/builds` → harici disk | SHA256 doğrulandı | S-09 bitince |
| 13 | S-10 | R2 FINAL zinciri → release mac + win64 → portföy | — | üretim dalgası bitince, main-ci yeşil |

## 3. Oturum başlangıç mesajları (kopyala-yapıştır, brifing §7 kalıbı)

Ortak son paragraf (her mesajın sonuna ekle):
> Teslimleri C-00'a dal + SHA ile bildir; SHA `git rev-parse` çıktısından. Her Unity/Player koşusu guard'lı; exit 32'de `tools/qa/oos_rule.py`. Kapı 8: konu `WP-NNN …: … (C-00 dd.mm)`, gövdede `Test:`/`Guarded:`. Amend, rebase, reset ve `git add -A` yok. Teslimden önce `python3 tools/qa/r2_gates_lint.py` koş (ERROR 0) ve WARN listesini teslim notuna yaz.

### S-06 (animasyon)
> Sen S-06'sın (animasyon). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 senin satırın, `K/c-00/R2/prompts/WP-083.md` ve devir notunu (`docs/c00/devir-notlari/wp-083-DEVIR-05.10.md`, ayrıntı `K/wp-083/S06-DEVIR-05.10.md`) oku. Worktree'n `K/wp-083/worktree/deaths-demolition`, dal `wp-083-b1`, WIP `dee54ffa`. Sıradaki: B2d/B2e'yi temiz bir commit'e çevir (amend yok, yeni commit). H1 0,40 cm ve KundakOmuz PASS korunmalı. ar_2 atışı bilinen kırmızı (S-02 tepme→pivot işi). rl_1 nişangâh 6,4 cm bilinen sapma; sonraki iş prosedürel baş/boyun eğimi (göz↔nişangâh ≤ 2 cm, eğim ≤ 15°). Runner exit-32 değişikliğine dokunma. Sonra WP-083 kahraman klip alımı (`K/asset-production/AP-026/exports/v002/hero-v002c/` indeksi; `hero/` altındaki 24 eski start ALINMAZ). S-02'nin `WeaponHoldIk`'e dokunan WIP'i var (`RecoilSource`); API'yi onunla birlikte kararlaştırın. BEKLEYEN'de sana düşenler: §13, §18 (H1 measuredBy, H2, H4a, L1–L5 yazıcıları), §19 G5 (grip araması).

### S-03 (düşman, AI, zombi)
> Sen S-03'sün (düşman, AI, zombi). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 ve §14 (eksik devir notu), `K/c-00/R2/prompts/WP-084.md` (varsa) oku. **Devir notun yok.** Önce durumunu çıkar ve C-00'a yaz: worktree `K/wp-088/worktree-s03`, dal `wp-084-s03-zombie-v2`; `git status --short`, `git log -3`, `git cherry main wp-084-s03-r2gates` (tip `a42f539b`; `c37991f7` / `cf8346a0` eşdeğer içerik main'e girmiş olabilir, kontrol et). Ayrıca `K/wp-088/` ve `K/wp-084*` altında `DEVIR-05.10.md` ara. main'de: shader `d0783d9b` (`_ZOMBIE_MASKS`, keyword kapalı) ve tanı testleri `03c3b06b`. Sıradaki: dilim 1 alım gövdesi (intake v002 → v003 parametreli, builder, SlotFace, boş jaw dönüşümü + test Ignore, RL bitleri, testler) → Z2/Z3/Z3b/Z4/DEC-077 ölçümü (face slot draw farkı ayrı) → dilim 3 zombi klipleri (S-06 ile). BEKLEYEN'de sana düşenler: §10, §18 (Z3b zorunlu testi `[Explicit]`), §19 G1 (malzeme anahtarı + `sharedMaterials`; SlotFace'ten önce düzeltmek iyi olur).

### S-08 (ses)
> Sen S-08'sin (ses). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7, `K/c-00/R2/prompts/WP-085.md` ve devir notunu (`docs/c00/devir-notlari/wp-085-DEVIR-05.10.md`) oku. Worktree'n `K/wp-085/worktree/deaths-demolition`. Kovan sesi teslimin main'de (`a62d83db`, ses↔iniş 0,3–3,5 cm). Sıradaki: main'den yeni dal `wp-085-b5-v003g` → `git lfs ls-files | grep " - "` boş olmalı → `ap027_import.py` ile v003g 180 overlay → `v3f-unity.sh v3g` (katalog + EM Audio + S4 v2, guard'lı) → `mono_notch.py` → allpass yan kanal dinleme → commit (LFS 180 nesne bildir) → **ar_1/sg_1 dinleme kiti** (üstten + FP, `Kit_` + `Stem_`; yalnız sessiz makinede) → C-00 kullanıcıya sunar. Sonra S3 (dense-40). BEKLEYEN'de sana düşenler: §15, §18 (S3 yazıcısı), §19 G2 (`AudioCatalog` indeksi + tekillik testi; v003g overlay'inde manifest içi yinelemeye bak), G4 (statik tabloları ısındır), B2 (`PredictLanding` maskesi, S-02 ile).

### S-09 (UI/UX)
> Sen S-09'sun (UI/UX). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7, `K/c-00/R2/prompts/WP-081.md` ve devir notunu (`docs/c00/devir-notlari/wp-068s9-DEVIR-05.10.md`) oku. Worktree'n `K/wp-068/worktree/deaths-demolition`, dal `wp-081-v4a-r2`, son `4895ac1d` (WIP `a1f50b99` = B + yastıklar + S04; `4895ac1d` ikon v2 iskeleti AP-028-B'ye göre yeniden yazılacak). Bekleyen teslimler `bcde16dc` (sinematik) ve `8ba256ec` (U1 iki ölçüm). Sıradaki: dev build → 5 oran kareleri + 60 s MP4 (`cine-player.sh look6 / look6mp4`) → kontrast FAIL 0 ve kenar ≤ 1,5× ölçümü → a1f50b99'u temiz commit'e çevir → üçlü teslim C-00'a → MP4 kullanıcıya. Sonra ikon v2 (G1 birleştirme; HUD glifleri de değişiyor, C-00'a söyle), yükleme alımı (`v002-upscale/Loading`, q90 + LoadingKeyArtTests), U1b (Seçim + paneller v4). Menü maliyeti ms ölçümü sessiz pencerede. BEKLEYEN'de sana düşenler: §11 (U1 iki ölçüm), §16 (S-05 ile menü ışığı, sonra).

### S-02 (oynanış, silah)
> Sen S-02'sin (oynanış, silah). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7, `K/c-00/R2/prompts/WP-087.md` ve devir notunu (`docs/c00/devir-notlari/wp-087-DEVIR-05.10.md`) oku. Worktree'n `K/wp-069b5/worktree/deaths-demolition`, dal `wp-087-heldkick`, WIP `6ea4492a`. İş: tutuş açıkken tepme kollara (pivota). Sağ el ≤ 0,02 cm OK. Sol el henüz değil: lmg_2 9,6, sg_2 3,8, lmg_1 3,7, rl_1 3,3, ar_2 1,6 cm (hedef ≤ 1). Bakılacaklar notta: `HeldKickCap`, WeaponGripIk sırası, dirsek. WIP'in S-06'nın `WeaponHoldIk` dosyasına dokunuyor: önce S-06 ile API'yi kararlaştırın (S-06'nın `ArmsCarryKick` önerisi). rl_1 tek atış koşulu için C-00'a sor. BEKLEYEN'de sana düşenler: §12, §14, §19 G3 (`WeaponMagazineVisual.Bind` ad taraması), B1 (`TryStepFor` enum switch), B2 (`CasingEffect.PredictLanding` maske önbelleği).

### S-01 (altyapı, guard, CI)
> Sen S-01'sin (altyapı, guard, CI). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7, `K/c-00/R2/prompts/WP-089.md`, devir notunu (`docs/c00/devir-notlari/wp-089-DEVIR-05.10.md`) ve göç notunu (`docs/c00/GUARD-D8-GOC.md`) oku. Worktree'n `K/wp-089/worktree-d6`, dal `s01-g4-d8-queue`, WIP `225d0359` (D8 `d707b999` üstünde). Sıradaki: kuyruk dalında tam regresyon (`test_userdata_guard_v2.py` LedgerTests → tamamı; `test_guard_d6a`, `test_guard_d234`, `test_ci_signal_traps`, `test_rc_stage_guard`; yeni `test_Kuyruk_OlenBekleyicininBileti_SiradakiniTutmaz`) → bağımsız eşzamanlılık incelemesi (Opus) → WIP yerine gerçek commit → C-00'a sıralı SHA (d707b999 → kuyruk), "SÜRÜCÜ DOKUNUŞU VAR". `DD_GUARD_QUEUE_WAIT` sapması C-00 onayı bekliyor. D9 rollout'u C-00 ilan eder (S-06/S-03/S-08/S-09 teslimleri girdikten sonra). iCloud izleyicisi çalışıyor.

### S-05 (dünya, ışık)
> Sen S-05'sin (dünya, ışık). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 oku. Worktree'n `K/wp-038m` (dal `wp-088-shore`; ~29 commit'siz izlenen değişiklik var, dikkat: önce `git status` ve C-00'a özet). Sıradaki: BEKLEYEN §17 (S-03'ün `DD_TierLit` `_ZOMBIE_MASKS` değişikliğinin incelemesi: main `d0783d9b` + tanı testleri `03c3b06b`; SRP Batcher uyumu, varyant sayısı, keyword kapalı görünüm, Look B ıslaklık) → §16 (menüye özel alacakaranlık ışık pişirmesi; önce maliyet) → WP-090. Ayrıca BEKLEYEN §6, §7, §8, "S-10 → S-05" bölümleri ve §18 (Z3 ve P3 yazıcıları).

### S-10 (QA, kabul)
> Sen S-10'sun (QA, kabul). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 oku. Worktree'n `K/wp-078/worktree` (dal `wp-091-r2-h1-def`, temiz). Sıradaki: BEKLEYEN §11 (U1 iki ölçüm) ve §15 (S4 v2) gözden geçir; §18 r2 kapı denetleyicisi uyarıları (H1 `measuredBy`, Z3b `[Explicit]` zorunlu test, P2) — zincir tarafı senin. `tools/qa/r2_gates_lint.py`'yi R2 FINAL zincirine ön adım olarak eklemeyi değerlendir. R2 FINAL (`K/wp-078/R2-KABUL-ZINCIRI.md`) "başla" C-00'dan gelir: üretim dalgası bitince ve main-ci yeşilken. Sonra release mac + win64, portföy 10 kare (Editor + debug panel; görsel listesi `docs/release/R2-NOTES-draft.md` §4).

### S-07 (VFX)
> Sen S-07'sin (VFX). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 oku. Worktree'n `K/wp-019r2/worktree` (Library arşivlendi; Unity açınca yeniden kurar). Sıradaki: BEKLEYEN §5, §8, §9 ve "S-10 → S-07". WP-086 (namlu/kovan görsel ↔ ses, zombi kan/ıslaklık) kahraman ve zombi alımı sonrası. Kovan sesi artık görünen kovanın atışından (`a62d83db`); görsel değişiklik `CasingEvents.Launched` sözleşmesini bozmamalı.

## 4. Üretim ajanlarının yeniden başlatma promptları (yerelde, Blender CLI)

Her ağır Blender koşusu: `cd "/Users/Erdem/Documents/Deaths Demolition" && tools/slot.sh acquire blender <etiket> && <blender -b … --threads 4>; tools/slot.sh release blender <etiket>`. Ajan türü `general-purpose`, model `opus`.

### AP-024C — kahraman tamamlama
> AP-024C ajanısın (Opus 5.5). Paket `K/asset-production/AP-024/`. Önce oku: `K/c-00/codex/reports/CX-AP024-B-RAPOR-05.10-1.md` §8 ve `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §8.1. Tamamlananlar: `exports/v002/` 5 FBX (LOD0–3 + FPArms; FBX 7400, Y-up, 77 kemik, soketler; roundtrip PASS; kök Lcl Rotation X −90°; ORM elle bağlanır), `06-renders/v002/` (kontrol noktası onaylı). Kalan, sırayla: **iş 2** 12 silah tutuş/reload fiziksel tablosu (AR1 yöntemi `fit_hero_rig_grips_v014.py`, `validation/hero_rig_grip_v014_ar_1.json`; rl_1 omuzdan atış olarak işaretli; önce `validation/` altında hangi silah JSON'larının yazıldığına bak, kalanlarla devam et) → **iş 3** 10 kare × 5 mesh deform (LOD2/3 kenar uzaması) → **iş 5** `manifest/AP024-manifest-v004.json` + SHA256SUMS + `K/c-00/codex/reports/C00-AP024C-RAPOR.md`. Riskler: DEC-077 lineer ΔY < 0,06 (Unity'de ölçülecek); AP-024D cila adayı notu. Blender her zaman slot kalıbıyla. Hazır olmayanı hazır deme; her iddia bir JSON/kare kanıtına dayansın.

### AP-025D — zombi giysi AAA + çene
> AP-025D ajanısın (Opus 5.5). Paket `K/asset-production/AP-025/`. Önce oku: `docs/c00/reports/C00-AP025C-RAPOR.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §8.2. `exports/v003/` ve `06-renders/v003/` durumuna bak; yoksa baştan başla. İstenen: giysi cilası (ıslak indigo kumaş, normal + AO bake ile gerçek kıvrım, ıslaklık lekeleri, yırtık kenar, sökük dikiş, etek ve paçada çamur bandı, maskeyle kan lekeleri, obi/ip/sargı ayrıntısı); çıplak bacak ve ayakta O1 ten + kir + morluk; **yüzlere jaw kemiği ağırlıkları** (oyundaki WP-071 AP-022 rig v002 ile aynı ad, parent ve rest, Head çocuğu; kemik ≤ 74; alt çene, alt dişler, alt dudak jaw'a; 0°/15°/25° testi, 25°'de ağız içi temiz; LOD'lar dahil; FBX reimport doğrulaması). Sınırlar: UV/atlas sözleşmesi aynı (aile ≤ 2 atlas, ≤ 3 harita, ≤ 4 materyal), 32 boyun halkası, BVH kesişim artmaz, AP-014 ve AP-008 dokunulmaz, 40 zombi doku ≤ 96 MB. **Kontrol noktası:** 2 giysi varyantı + TP 3/4 önce/sonra → DUR → C-00 onayı. Çıktı `exports/v003/`, `06-renders/v003/`, manifest v005, `K/c-00/codex/reports/C00-AP025D-RAPOR.md`. Higgsfield ledger zorunlu; Seed Audio ve AI ses yasak (bu pakette ses yok).

## 5. Bulut zinciri: entegrasyon sırası (bulutta alınamadı; yerelde `integrate.sh`)

Her satır için önce dry-run `tools/integrate.sh <sha> --tag <etiket> --no-post --allow-paths docs/`, sonra aynı komut `--apply` ile. `tools/` yalnız olan satırlarda (2, 18) `--allow-paths` gerekmez. Sıra zorunlu: `C00B-TESLIM.md` satırları birbirine dayanır. Etiketlerin hepsi origin'de boş (05.10 ~20:30 kontrol edildi).

| # | SHA | Etiket | İçerik |
|---|---|---|---|
| 1 | `4edbba5b` | `wp-089-c00b-b02-start-int` | B-02 başla satırı |
| 2 | `b7737f04` | `wp-091-r2-gates-lint-int` | `tools/qa/r2_gates_lint.py` + 23 test |
| 3 | `a7cd3bd3` | `wp-089-c00b-b02-log-int` | B-02 bitir satırı |
| 4 | `ec546e12` | `wp-089-c00b-b01-start-int` | B-01 başla |
| 5 | `88da3107` | `wp-089-r2-system-docs-int` | ARCHITECTURE + SYSTEMS-INDEX R2 |
| 6 | `c5e0d905` | `wp-089-c00b-b01-log-int` | B-01 bitir |
| 7 | `ba448fe3` | `wp-089-c00b-b03-start-int` | B-03 başla |
| 8 | `1dff988b` | `wp-091-gc-review-int` | GC incelemesi + LOCAL-QUEUE satırı |
| 9 | `a593c8cf` | `wp-089-c00b-b03-log-int` | B-03 bitir |
| 10 | `fc305c2e` | `wp-089-c00b-b04-start-int` | B-04 başla |
| 11 | `e289b1a4` | `wp-091-r2-release-notes-draft-int` | yayın notu taslağı |
| 12 | `01f4b163` | `wp-089-c00b-b04-log-int` | B-04 bitir |
| 13 | `f8b898fa` | `wp-089-brifing-tamam-int` | brifing §0/§14 ilk ek (dal `claude/sharp-cray-jsvo9s`) |
| 14 | `825e2d26` | `wp-089-brifing-bulut-int` | bulut düzeni eki |
| 15 | `bd4ceac8` | `wp-089-brifing-bulut2-int` | C-00B teslimlerinin kontrolü, bulut devralma promptu |
| 16 | `bede1547` | `wp-089-brifing-bulut3-int` | brifing §0 yazım düzeltmesi (dalın ucu) |
| 17 | `1c147550` | `wp-089-c00b-b05-start-int` | B-05 görev ve başla satırı (dal `c00b/B-05-r2-docs-sync`) |
| 18 | `5b2b50d1` | `wp-089-r2-docs-sync-int` | B-05 belge eşitlemesi |
| 19 | `45be8acc` | `wp-091-r2-contract-path-int` | r2 sözleşmesi `R2Olcum.cs` yolu (lint ERROR 0) |
| 20 | `4c233054` | `wp-089-bekleyen-18-19-int` | BEKLEYEN §18–§19 |
| 21 | `3c648a24` | `wp-089-bekleyen-18-ref-int` | §18 bölüm atfı düzeltmesi |
| 22 | bu belgenin commit'i ve B-05 bitir satırı | `wp-089-yerel-donus-plani-int`, `wp-089-c00b-b05-log-int` | SHA'lar `C00B-TESLIM.md` ve PR 5'te |

**Not.** 13–16, `claude/sharp-cray-jsvo9s` dalının yalnız bu dört commit'idir. Daldaki kök belgeler (`C-00-*.md`, `DISK-*`, `PROJE-*`) kullanıcının arşividir; main'e alınmaz.

**Doğrula:**
```bash
git cherry -v main origin/c00b/B-05-r2-docs-sync      # "+" kalmamalı
git cherry -v main origin/claude/sharp-cray-jsvo9s    # docs/c00 commit'i kalmamalı
python3 -m unittest tools/qa/tests/test_r2_gates_lint.py tools/qa/tests/test_r2_gates.py
python3 tools/qa/r2_gates_lint.py                      # ERROR 0
```

## 6. Açık PR'lar (bulut, 05.10)

| PR | Dal | İçerik | Taban |
|---|---|---|---|
| [ErdemEmirOzer/deaths-demolition#1](https://github.com/ErdemEmirOzer/deaths-demolition/pull/1) | `c00b/B-02-r2-gates-lint` | B-02 r2 kapı denetleyicisi | main |
| [ErdemEmirOzer/deaths-demolition#2](https://github.com/ErdemEmirOzer/deaths-demolition/pull/2) | `c00b/B-01-docs-r2` | B-01 sistem belgeleri | PR 1 dalı |
| [ErdemEmirOzer/deaths-demolition#3](https://github.com/ErdemEmirOzer/deaths-demolition/pull/3) | `c00b/B-03-gc-review` | B-03 GC incelemesi | PR 2 dalı |
| [ErdemEmirOzer/deaths-demolition#4](https://github.com/ErdemEmirOzer/deaths-demolition/pull/4) | `c00b/B-04-release-notes` | B-04 yayın notu | PR 3 dalı |
| PR 5 | `c00b/B-05-r2-docs-sync` | B-05 eşitleme, sözleşme yolu, BEKLEYEN §18–§19, bu plan | PR 4 dalı |

## 7. Bulut C-00'ın verdiği kararlar (05.10)

1. B-05 dalı güncel main'den değil, B-04 ucundan açıldı. Düzelttiği dosyalar (`ARCHITECTURE`/`SYSTEMS-INDEX`'in R2 bölümleri, `R2-NOTES-draft.md`, `B-03-gc-inceleme.md`) main'e girmediği için başka yol yoktu. Zincir main'e girdikten sonra yeni işler main'den açılır.
2. r2 sözleşme metnindeki yol, kapı tanımı değil, yazım düzeltmesi sayıldı: eşik, anahtar, sahip ve `measuredBy` değişmedi.
3. WARN'lar (H1 `measuredBy`, Z3b zorunlu testi, 12 yazıcısız kapı) için kapı dosyası değiştirilmedi. Sahiplere iş olarak yazıldı (BEKLEYEN §18).
4. `a62d83db` ayrıca incelendi; yeni GC bulgusu yok. B2'nin önceliği arttı (kovan başına).
