# BRİFİNG — Yeni hesaba tam devir (Deaths Demolition, 05.10.2026 akşamı)

**Kimden:** C-00 (birinci hesap, yerel Mac; kullanım limiti doldu).
**Kime:** ikinci hesaptaki C-00 ve C-00B oturumları.
**Amaç:** Projenin bugünkü durumunu, verilen bütün kararları, yarım işleri, kuralları ve sıradaki adımları eksiksiz aktarmak. Bu belge ile `docs/c00/C00-DEVIR-2026-10-05.md` birlikte okunur. Çelişki olursa bu belgedeki **daha yeni** bilgi geçerlidir.

---

## 0. Kim, nerede, hangi promptla başlar

| Rol | Nerede | Başlangıç promptu | Durum (05.10 ~19:40) |
|---|---|---|---|
| **C-00** (asıl koordinatör, tek entegratör) | İkinci hesap, bu Mac'te **yerel** (Claude Desktop) | `docs/c00/C00-DEVIR-BASLANGIC-PROMPTU.md` | açılacak |
| **C-00B** (ikinci ekip) | İkinci hesap, **bulut** | `docs/c00/C00B-IKINCI-HESAP-PROMPTU.md` | çalışıyor; B-02 teslim edildi (§14) |
| Bulut C-00 (yalnız yerel C-00 açılamazsa) | İkinci hesap, bulut | `docs/c00/C00-BULUT-PROMPTU.md` | yedek |

**Okuma sırası:**
1. Bu belge (önce §14 son durum).
2. `C00-DEVIR-2026-10-05.md` (SON DURUM EKİ, §0–§9, §12).
3. `PROJE-SISTEM-PROMPTU-2026-09-29.md`.
4. `R2-PLAN.md`, `BEKLEYEN-MESAJLAR.md`, `LOCAL-QUEUE.md`, `devir-notlari/`.

KADRO'daki `K/c-00/...` kopyaları ile depodaki `docs/c00/` kopyaları ayrışırsa **main'deki `docs/c00/` geçerlidir**. Son ekler yalnız orada.

---

## 1. Durum özeti (tek paragraf)

Proje M4 sonrası **R2 revizyon turunda**. Bu tur kullanıcının yedi notunu (N2-01…N2-07) uyguluyor. Bugün yapılanlar:
- 10 silahın yeni gerçekçi ve yankılı sesleri oyuna girdi.
- Silah hizası, roket, iz ve şarjör olayları düzeltildi.
- Menü v4'ün ilk aşaması ve kullanıcı verisi korumasının D7 aşaması main'e girdi.
- Yükleme sanatları netleştirildi ve kabul edildi.
- Zombiler (yeni yüzler, ağır beden) ve animasyon paketi (142 hareket geçişi, 50 şarjör klibi) üretildi ve kabul edildi.

Kahraman ve zombi giysisi üretimi ile silah tutuşu, zombi alımı, ses son ayarı ve menü sinematiği **yarım**. Yarım işlerin hepsi kendi dallarında WIP commit'i ve devir notuyla kayıtlı. main `3bb501e7` ve son derleme kontrolü PASS. Codex kotası 12.10'a kadar bitik. Birinci hesap durdu.

## 2. Roller: iki hesap nasıl çalışacak

| Rol | Nerede | Ne yapar | Ne yapamaz |
|---|---|---|---|
| **C-00 (asıl koordinatör, tek entegratör)** | **İkinci hesap, bu Mac'te YEREL** (Claude Desktop ya da `CLAUDE_CONFIG_DIR=~/.claude-hesap2 claude`) | Oturumları (S-01…S-10) açar ve yönetir. `tools/integrate.sh` ile main'e alır. Unity, Blender ve Player koşturur. KADRO'yu ve harici diski yönetir. Alt ajan başlatır. Kullanıcıya görselli rapor verir. | — |
| **C-00B (ikinci ekip)** | İkinci hesap, **bulut** (claude.ai/code, depo `ErdemEmirOzer/deaths-demolition`) | `c00b/*` dalları ve PR'lar: belge, Python araçları ve testleri, kod incelemesi, Unity'siz C# taslakları. Görevler `docs/c00/C00B-GOREVLER.md`'de. | main'e almak, Unity/Blender/Player, KADRO, harici disk |

**Kritik:** Unity'yi çalıştırabilen bir yerel C-00 oturumu **olmadan** derleme, test, zombi alımı, ses ölçümü ve build ilerleyemez. Kullanıcı ikinci hesabı yerelde de açmalı. Bulut oturumu tek başınaysa:
- yalnız kod, belge ve inceleme yapar;
- Unity gerektiren her şeyi `docs/c00/LOCAL-QUEUE.md`'ye yazar;
- kullanıcıya yerel oturumu açması gerektiğini söyler.

## 3. Kullanıcı tercihleri ve yetkiler (bağlayıcı)

- **Dil:** kullanıcıyla Türkçe. Commit konuları İngilizce, mevcut biçimde.
- **Tam yetki (05.10):** "Hiçbir şey sorma, her şeyi yap ve onayla." Kararları ve görsel onayları C-00 verir, sonra kullanıcıya **görsellerle** bildirir.
  - **İstisnalar:** satın alma; hesap açma; şifre girme; **kalıcı silme** (Çöp'ü boşaltmak kullanıcının işi); başka bir oturumun izin sisteminin reddettiği işlemi onun yerine yapmak (bu durumda kullanıcıya iletilir).
- **Görsel sunum:** Seçenekler ve sonuçlar her zaman görselli sunulur. Biçim: Artifact sayfası, JPEG önizleme, karar kutusu. Metin-only seçim sorusu kullanıcı tarafından reddedildi.
- **Durum sorusu:** "Ne durumdayız" → yapılan/kalan tablosu.
- **Model:** her şey **Opus 5.5** (claude-opus-5-5); bütün oturumlar, alt ajanlar (`model: "opus"`) ve workflow ajanları.
- **Kullanıcı verisi kutsal (§4.14):** profil, ayar ve kayıt asla kaybolmaz. Her Player/PlayMode koşusu `tools/userdata-guard.sh` penceresinde yapılır.
- **Krediler:** Higgsfield ve Adobe sınırsız, ancak ledger zorunlu. **Seed Audio ve başka AI ses üretimi SFX için yasak.**
- **Disk:** "Temizliklerde kullanılmayan her şeyi harici diske alabilirsin" (05.10). Kural: önce gerçek kazanç ölçülür, sonra kopyala → SHA256 → asıl Çöp'e; Çöp'ü kullanıcı boşaltır.
- **Kanıt:** Hazır olmayanı hazır deme; kanıtsız PASS yok. "Ölçüm önce, bulgu sonra."

## 4. Yerler

| Ne | Yol |
|---|---|
| Depo (ana worktree) | `/Users/Erdem/Documents/Deaths Demolition` — GitHub `ErdemEmirOzer/deaths-demolition` (private), dal `main` |
| Unity projesi | `prototype/DeathsDemolition_PlayableConcept` (Unity 2022.3.22f1, URP 14) |
| KADRO (K) | `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition` (iCloud Masaüstü; dataless 0) |
| Koordinasyon | `K/c-00/` (R2 klasörü, aktif-ajanlar, codex istemleri ve raporları, disk-arsiv) |
| Varlık üretimi | `K/asset-production/AP-0NN/` (depoda değil, tek kopya + harici disk yedeği) |
| Worktree'ler | `K/wp-*/worktree*/deaths-demolition` (devir §6 tablosu) |
| CI | sürücü `K/wp-079/driver`, CI worktree `K/wp-079/ci-worktree`, sonuçlar `K/ci/` |
| Harici disk | `/Volumes/TOSHIBA EXT/DD-ARSIV/` (APFS 931 GB) |
| Ham kaynaklar | `/Users/Erdem/DD-Sources/` (sonniss, mixamo) |
| Hafıza | `~/.claude/projects/-Users-Erdem-Documents-Deaths-Demolition/memory/` (`MEMORY.md` dizin; `entegrasyon-gunlugu-2026-09.md` günlük) |
| Depodaki belge kopyaları | `docs/c00/` (devir, prompts, raporlar, kuyruklar) |

## 5. main ve bugün entegre edilenler

**main = `3bb501e7`.** Derleme kontrolleri: `d0783d9b` PASS (errorsCS 0). Sonrası yalnız tanı testleri (`03c3b06b`), belgeler ve S-08 kovan sesi (`a62d83db`) — **ilk iş `tools/compile-check.sh main`**. Son tam main-ci `09a21785` → yeni main-ci S-06 teslimi sonrası.

Bugünkü entegrasyonlar (eskiden yeniye, özet):
1. **S-09:** menü v4 faz A (`d62a9b95`, `09a21785`); Rezervler düzeltmesi (`d8ca7089`); U1/U2/U3 "kaynak kayıtta var" (`137637a5`).
2. **S-01 araçları:**
   - manifest denetleyicisi yeni türler (`df59297a`);
   - D7 guard sınıfları (`eb632a7c`);
   - integrate varsayılan ana worktree (`e7c17017`);
   - rc.sh 32 kuralı (`6837484b`);
   - guard satırı `$'\n'` düzeltmesi (`2f1ab726`).
3. **S-02 (WP-087):**
   - roket ilk kare (`8c28b840`);
   - FP sg_1 kovan (`2ed0e67c`);
   - namlu/kovan hiza kapıları (`d018a0f3`);
   - reload olay sözleşmesi (`17cefbf5`);
   - üstten iz namludan başlar (`b2eff22e`);
   - Raiju testi deterministik (`8d8a5e85`);
   - VfxDressing ısınması (`3e6464a5`);
   - hero v2 erişim ölçümü (`29725393`);
   - kovan fırlatma olayı CasingEvents.Launched (`cc5e6246`);
   - H1 elbowMaxDeg report-only (`f7166ed3`).
4. **S-08 (WP-085):**
   - B5 dilim 1 AR1/SG1/SMG1 (`b2a2a13e`) ve dilim 2, kalan 7 silah (`31359b92`);
   - mix + S4 v2 (`3f74e3f4`);
   - kovan balistiği (`dfc9935c`);
   - v003f kuyruklar (`97705c32`);
   - kovan sesi Launched + PredictLanding (`a62d83db`).
5. **S-03 (WP-084):** `_ZOMBIE_MASKS` shader (`d0783d9b`); tanı testleri (`03c3b06b`).
6. **C-00 belgeleri:** `docs/c00/` (`d5bd2b78` … `3bb501e7`).

Etiketlerin tam listesi için: `git tag --sort=-creatordate | head -40`.

## 6. R2 durumu (N2 notları)

| Not | Bugünkü durum | Kalan | ~% |
|---|---|---|---|
| **N2-01 Menü** | v4 faz A main. U1 (varsayılan yol) 0,422 PASS. Sinematik kararı **B + yastıklar** (sol yumuşak scrim). S-09 WIP'te kod hazır (b6 EM 41/41). | 5 oran + 60 s MP4 kanıtı → kullanıcıya göster. `8ba256ec` (U1 iki ölçüm) ve `bcde16dc` teslimi. U1b ≥ 0,40 (Seçim + paneller v4). İkon v2 (derinlik/yansıma/motion). Menü ms ölçümü. S-05 menüye özel ışık pişirmesi (SHOULD). | 60 |
| **N2-02 Yükleme** | 5 sanat netleştirildi, kabul (`AP-028/exports/v002-upscale/Loading/`, intake 5/5). | S-09 alımı + LoadingKeyArtTests. | 85 |
| **N2-03 Zombiler** | AP-025C kabul (3 baş, ağır beden H2, O1 atlas, 9 maske). Shader main'de. | S-03 alım gövdesi (intake, builder, SlotFace, RL bitleri, testler) → Z ölçümleri. AP-025D (giysi AAA + yüz jaw ağırlıkları). Zombi klipleri (S-03 + S-06). | 60 |
| **N2-04 Kahraman** | G1b = **V1 + koyu omuz**. AP-024C: 5 FBX + roundtrip PASS, renderlar (kontrol noktası onaylı). | AP-024C iş 2 (12 silah tutuş tablosu), iş 3 (deform), iş 5 (manifest + rapor). WP-082 alım. AP-024D cila adayı. | 55 |
| **N2-05 Tutuş + şarjör** | WeaponHoldIk varsayılan açık kararı. H1 0,40 cm, KundakOmuz hepsi PASS. Olay sözleşmeleri main. | S-06 WIP (`dee54ffa`) teslimi. ar_2 atış: bilinen kırmızı → S-02 tepme→pivot (WIP `6ea4492a`; sol el henüz > 1 cm). rl_1 nişangâh 6,4 cm → baş eğimi işi. | 75 |
| **N2-06 Rig + locomotion** | AP-026C tamam (142 geçiş kapılardan geçti, `hero-v002c` indeksi). | S-06 WP-083 alımı + L1–L6 oyun içi. sg_1 döngüsü + LhIk (hero v2 ile). Zombi klipleri. | 55 |
| **N2-07 Silah sesi + kovan** | 10 silah v4 oyunda. S4 v2 PASS (açık 0,72–0,81 / koridor 1,13–1,32 / salon 1,62–1,86 s). Kovan sesi ↔ iniş 0,3–3,5 cm. v003g (mono düzeltmesi) hazır. | S-08: v003g overlay + S4 + mono ölçüm → **ar_1/sg_1 dinleme kiti kullanıcıya** → dense-40 / S3. | 88 |

R1 devirleri:
- WP-089 (S-01): D8 + kuyruk ekleri WIP → **D9 rollout**.
- WP-086 (S-07): kahraman ve zombi sonrası.
- WP-090 (S-05): düşük öncelik.
- WP-091 (S-10): R2 FINAL + release mac/win64 + portföy.

## 7. Oturumların son durumu (WIP ve devir notları)

| Oturum | Dal / son SHA | Devir notu | Durum | Sıradaki |
|---|---|---|---|---|
| **S-01** altyapı | `s01-g4-d8-queue` WIP `225d0359` (D8 `d707b999` üstünde) | `K/wp-089/DEVIR-05.10.md`, göç notu `K/c-00/R2/GUARD-D8-GOC.md` | D8 ledger teknik kabul. Ekler: kilit sırası guard → unity (unity tutarken 64), FIFO uzun bekleme + kuyruk satırı. Açık konu: `DD_GUARD_WAIT` yerine `DD_GUARD_QUEUE_WAIT` (gerekçe notta). | Ekleri bitir → **D9:** dalga sakinleşince D8 + ekler tek adımda entegre → sürücü taşı → main-ci → tüm oturumlara göç notuyla duyuru. |
| **S-02** oynanış | `wp-087-heldkick` WIP `6ea4492a` | `K/wp-087/DEVIR-05.10.md` | Tutuş açıkken tepme pivota: sağ el ≤ 0,02 cm OK; sol el lmg_2 9,6 / sg_2 3,8 / lmg_1 3,7 / rl_1 3,3 / ar_2 1,6 cm (hedef ≤ 1). | Sol el destek hedefini tepmeli pivottan al; S-06 ile. Ölçüt: auto 10 atış, tüm klasikler, iki el ≤ 1 cm. |
| **S-03** zombi | `wp-084-s03-zombie-v2` (shader ve tanı testleri main'de) | not bekleniyordu (`K/wp-088/` ya da worktree) | Alım gövdesi sürüyor. Dal tabanındaki `c37991f7` / `cf8346a0` (Z3b/Z4 kaynak-kayıtta + Z3b sabit sınırlar) `git cherry`'ye göre main'de değil; eşdeğer içerik girmiş olabilir → kontrol et. | Dilim 1: intake (v002 → v003 parametreli), builder, SlotFace, boş jaw dönüşümü (test Ignore), RL bitleri, testler → Z2/Z3/Z3b/Z4/DEC-077 (face slot draw farkı ayrı) → dilim 3 zombi klipleri (S-06). |
| **S-06** animasyon | `wp-083-b1` WIP `dee54ffa` | `K/wp-083/DEVIR-05.10.md` | b2: H1 0,40 cm; KundakOmuz PASS; C dipçik satırı; sg_2 HoldDelta; rl_1 omuz satırı (omuz 0,6 / baş 3,5 cm; nişangâh 6,4 cm bilinen sapma); ar_2 bilinen kırmızı. Runner exit-32 değişikliği **kullanıcı onayı yok** (dokunma). | B2d/B2e temiz commit + SHA → C-00 entegre → main-ci. Sonra WP-083 kahraman klip alımı (`AP-026/exports/v002/hero-v002c/` indeksi; `hero/` altındaki 24 eski start ALINMAZ). rl_1 baş eğimi (≤ 2 cm, ≤ 15°). sg_1 + LhIk. |
| **S-08** ses | `55a933cf` main'de | `K/wp-085/DEVIR-05.10.md` | Bitti: kovan sesi Launched + PredictLanding. | v003g overlay → S4 → 3B mono ölçüm → allpass yan kanal dinleme → **ar_1/sg_1 dinleme kiti** (üstten + FP, Kit_ + Stem_) → dense-40/S3. |
| **S-09** UI | `wp-081-v4a-r2` son `4895ac1d` (WIP `a1f50b99`) + bekleyen `8ba256ec`, `bcde16dc` | `K/wp-068s9/DEVIR-05.10.md` | B + yastıklar + S04 eşiği kodu hazır. 5 oran ve MP4 eksik. | Tek teslim → MP4 kullanıcıya → ikon v2 → U1b → yükleme alımı → `wp-068s9/builds` harici diske (~14,6 GB gerçek kazanç). |
| **S-04** ekonomi | — | — | R2'de açık iş yok. | R2 FINAL denge kontrolü. |
| **S-05** dünya | `K/wp-038m` (`wp-088-shore`, ~29 commit'siz değişiklik, dikkat) | — | Kapalı. | BEKLEYEN §17 (S-03 shader incelemesi) → §16 (menü ışık pişirmesi, önce maliyet) → WP-090. |
| **S-07** VFX | — | — | Kapalı. | WP-086 (kahraman ve zombi sonrası). BEKLEYEN §5, §9. |
| **S-10** QA | `K/wp-078` (temiz) | — | Kapalı. | BEKLEYEN §11 (U1 iki ölçüm), §15 (S4 v2) gözden geçir → R2 FINAL (`K/wp-078/R2-KABUL-ZINCIRI.md`) → release → portföy. |

**Devir notlarının depo kopyaları:** `docs/c00/devir-notlari/` altında:
- S-01 `wp-089-DEVIR-05.10.md`;
- S-02 `wp-087-DEVIR-05.10.md`;
- S-06 `wp-083-DEVIR-05.10.md`;
- S-08 `wp-085-DEVIR-05.10.md`;
- S-09 `wp-068s9-DEVIR-05.10.md`.

Guard göç notu `docs/c00/GUARD-D8-GOC.md`. **S-03'ün notu yok** (§14).

**Oturum açma kalıbı:** "Sen S-0N'sin (<rol>). `K/c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`, `K/c-00/R2/R2-PLAN.md`, `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §7 senin satırın, `K/c-00/R2/prompts/<WP>.md` ve devir notunu oku. Worktree'n <yol>; WIP commit'in <SHA>; kaldığın yerden devam et. Teslimleri C-00'a dal + SHA ile bildir (SHA `git rev-parse` çıktısından). Her Unity/Player koşusu guard'lı; exit 32'de `tools/qa/oos_rule.py`."

## 8. Üretim ajanları (yeniden başlatma bilgisi)

### 8.1 AP-024C — kahraman tamamlama
- **Paket:** `K/asset-production/AP-024/`.
- **Codex kısmi raporu:** `CX-AP024-B-RAPOR-05.10-1.md`.
- **Tamam:**
  - `exports/v002/`: 5 FBX (LOD0–3 + FPArms), FBX 7400, Y-up, 77 kemik, soketler. Roundtrip PASS. Kök Lcl Rotation X −90°.
  - ORM elle bağlanır (README'de yazılı).
  - `06-renders/v002/` renderları ve değer kanıtı (oyun ışığında torso C 0,215).
- **Kalan:**
  - iş 2: 12 silah tutuş/reload fiziksel tablosu; AR1 yöntemi `fit_hero_rig_grips_v014.py`; rl_1 omuzdan atış olarak işaretli.
  - iş 3: 10 kare × 5 mesh deform; LOD2/3 kenar uzaması.
  - iş 5: `manifest/AP024-manifest-v004.json` + SHA256SUMS + `K/c-00/codex/reports/C00-AP024C-RAPOR.md`.
- **Riskler:** DEC-077 lineer ΔY < 0,06 (Unity'de ölçülecek). AP-024D cila adayı: maske yüzeyi düzleştirme, simetrik etek plakaları, yakın plan malzeme.
- **Yeniden başlatma:** `general-purpose` ajan, Blender CLI slot kalıbıyla. Önce `validation/` altında hangi silah JSON'larının yazıldığına bak, kalan silahlarla devam et.

### 8.2 AP-025D — zombi giysi AAA + çene
- **Paket:** `K/asset-production/AP-025/`. Önceki tur: `C00-AP025C-RAPOR.md`.
- **İstenen:**
  - giysi cilası: ıslak indigo kumaş, gerçek kıvrım (normal + AO bake), ıslaklık lekeleri, yırtık kenar, sökük dikiş, etek ve paçada çamur bandı, kan lekeleri maskeyle, obi/ip/sargı ayrıntısı;
  - çıplak bacak ve ayakta O1 ten + kir + morluk;
  - **yüzlere jaw kemiği ağırlıkları:** oyundaki WP-071 AP-022 rig v002 ile aynı ad, parent ve rest (Head çocuğu); kemik ≤ 74; alt çene, alt dişler, alt dudak jaw'a; 0°/15°/25° testi; 25°'de ağız içi temiz (dil, diş eti, koyu ağız); LOD'lar dahil; FBX reimport doğrulaması.
- **Sınırlar:** UV/atlas sözleşmesi ve kumaş atlası sayısı aynı (aile ≤ 2 atlas, ≤ 3 harita, ≤ 4 materyal); 32 boyun halkası; BVH kesişim artmaz; AP-014 ve AP-008 dokunulmaz; 40 zombi doku ≤ 96 MB.
- **Kontrol noktası:** 2 giysi varyantı + TP 3/4 önce/sonra → DUR → C-00 onayı.
- **Çıktı:** `exports/v003/`, `06-renders/v003/`, manifest v005, `C00-AP025D-RAPOR.md`.
- **Yeniden başlatma:** `exports/v003/` ve `06-renders/v003/` durumuna bak; yoksa baştan aynı kapsamla başlat (Higgsfield + Blender CLI).

### 8.3 Bitenler (bilgi)
- AP-026C (animasyon; rapor `C00-AP026C-RAPOR.md`).
- AP-027 v003 … v003g (ses; raporlar `C00-AP027-V003*-RECUT-RAPOR.md`).
- AP-028 v002-upscale (yükleme sanatları).
- Disk envanteri (`K/c-00/disk-arsiv/ENVANTER-05.10.md`).

## 9. Bugünkü kararların kaydı (kısa, kronolojik değil, konuya göre)

**Ses (N2-07)**
- G4 = C (Codex v4 yönü).
- Hibrit kaldırıldı.
- Açık alan WetDb +3 dB ("yankılı", −12 → −9); salon −7.
- S4 v2 = kuyruk RT60 (T20). Eşikler: açık 0,56–1,08, koridor 0,88–1,68, salon 1,28–2,28 s, sıra ihlali 0. Eski edc30 yalnız bilgi.
- Kuyruklar uzatıldı (v003f) ve mono düzeltildi (v003g; sağ kanal 0,77 ms gecikmeli kopyaydı → M/S).
- ar1 Mechanical = v003-recut 114 ms (sürgü dönüşü olayın parçası).
- SG uzak = SPAS 1 m → 50 m fiziksel vekil (v003e).
- lmg2 Fire/FireTP/FireFP_01 rastgele havuzdan çıkar.
- Kovan sesi görünen kovanın iniş noktasında.
- Overlay sırası: v002 → v003-recut → v003b (ar1 Mechanical hariç) → v003c (DF04/06) → v003d → v003e → v003f → v003g.

**Tutuş ve silah (N2-05)**
- WeaponHoldIk varsayılan AÇIK.
- H1 biçimi B (GripOffset sabit, referans mount.TransformPoint).
- H3 temas IK (b).
- Silah başına tutuş satırları: C yanal +6 cm; rl_1 omuz satırı.
- ar_2 bilinen kırmızı, çözümü S-02 tepme→pivot.
- rl_1 nişangâh 6,4 cm bilinen sapma; sonraki iş baş eğimi.
- Reload olayları:
  - rocket_in → Insert, rocket_seat → BoltRelease;
  - _tac 3 vuruş;
  - aynı karede eş anlamlılar tek olay;
  - shell_in fişek başı;
  - sg_1 döngüsü (denge ve süre aynı; faz kaydırma tasarımı onaylı);
  - lh_ik_curve: min(kapı, LhIk).
- TwistCorrector esas.
- Roket ilk kare (b) ve üstten iz namludan başlar (yalnız sunum).
- H1 elbowMaxDeg report-only.

**Zombiler (N2-03)**
- AP-025-B sanatı RED, teknik sözleşme korunur.
- AP-025C kabul: Gaunt/Swollen/Riven, Tripo H3.1, ağır beden H2.
- Swollen boyun halkası değişmez (animasyonda gözlem).
- SlotFace mimarisi onaylı.
- `_ZOMBIE_MASKS` `shader_feature_local_fragment`; keyword kapalıyken piksel-eş.
- jaw: (a) şimdi boş dönüşüm, (b) AP-025D'de ağırlık.
- Alım betiği v002 → v003 parametreli.

**Kahraman (N2-04)**
- G1b = V1 + koyu omuz.
- Erişim değişmiyor (default77 kol = şablon); GripOffset değişmez.

**Menü ve yükleme (N2-01/02)**
- Omurga scrim α 0,55 yerine sinematik için **B + yastıklar**. A, C, D ve E reddedildi; E "ay ışığı" etkisi ve zemin şeridi nedeniyle.
- S04 çekimi yerel uyarlama (4 aday, kriterler).
- U1 = varsayılan yol ve aynı içerik, ikisi de ≥ 0,40.
- Yükleme upscale 5/5 kabul.
- S-05'e menüye özel ışık pişirmesi (SHOULD).

**Altyapı**
- rc.sh 32: editor aşamasında 3 koşul tutarsa uyarı; Player aşamasında katı.
- §6(i): M-05 geçerli, otomatik kira geri alma yok.
- D8 teknik kabul, entegrasyon D9'da.
- Kanonik manifest denetleyicisine yeni türler eklendi.
- Kullanıcı S-06'nın GUARD-STOP kilidini bir kez açık talimatla kaldırdı. Runner değişikliğine onay YOK.

**Disk**
- TOSHIBA EXT APFS'ye biçimlendirildi (kullanıcı onayı).
- 26,4 GB taşındı (Çöp boşaltıldı, tek kopya harici diskte). 15,3 GB AP yedeği alındı.
- Library klonları yer açmıyor (APFS klon dersi).

## 10. Kurallar ve dersler (uygulama için)

- **Kapı 8:** konu `WP-NNN …: … (C-00 dd.mm)`. İki nokta şart ve tam "(C-00 dd.mm)" ile biter; gövdede `Test:` (ya da `Guarded:`) satırı. Uymayan teslim yeni commit'le düzeltilir; amend yok.
- **integrate.sh** yalnız verilen commit'i cherry-pick eder. Çok commit'li dalda her commit sırayla verilir; sonra `git cherry main <tip>` "+" = 0 olmalı.
  - LFS'li teslimde önce `git lfs push origin main`, sonra `GIT_LFS_SKIP_PUSH=1 git push origin main refs/tags/<t>`.
  - docs/c00 için `--allow-paths docs/c00`; S-01 araçları için `--s01`.
- **Sürücü:** guard dosyasına dokunan commit'ten sonra `git -C K/wp-079/driver checkout -q --detach main` ve `cd K/wp-079/driver && python3 tools/guard_freshness.py K/wp-079/driver/tools/userdata-guard.sh` → 0.
- **compile-check:** bekleyen bir compile-check'i durdurma; bayat kira bırakır. Bayat kira olursa: Unity yoksa kirayı `.quarantine-c00-<ts>` olarak yeniden adlandır.
- **Guard 32:** S-09 Player pencereleri diğer oturumlarla çakışıyor. `tools/qa/oos_rule.py <ug>` ile doğrula; rc 0 ise uyarı.
- **Slotlar:**
  - Blender tek slot; acquire + Blender + release aynı kabukta.
  - Disk ≥ 15 GB, swap ≤ 12 GB.
  - Kilit sırası player → unity; D8 sonrası guard → unity.
- **APFS:** taşımadan önce `tools/apfs_real_gain.py`.
- **Ajan türü:** `general-purpose` (MCP ve Bash erişimi). `graphic-designer` / `3d-modeller` türlerinde MCP yok, Bash yok.
- **SHA:** `git rev-parse` çıktısından kopyalanır. Commit ve teslim mesajı paralel atılmaz.
- **Silme:** yalnız Çöp'e. Kalıcı silme kullanıcının işi.
- **Aktif ajan kaydı:** her ajan ve oturum işe başlarken ve bitirirken `K/c-00/aktif-ajanlar/<oturum>.md`'ye satır yazar.

## 11. MCP ve ortam

**Kullanıcının kuracakları:**
- **Higgsfield** (ŞART), **Adobe for creativity**, isteğe bağlı **Claude Docs**: claude.ai → Ayarlar → Bağlayıcılar.
- Yerelde:
  - Claude Desktop yerleşik oturum araçları (`ccd_session_mgmt`; oturum açma ve mesajlaşma için ŞART);
  - computer use (macOS Erişilebilirlik + Ekran Kaydı izinleri);
  - isteğe bağlı Blender MCP ve Claude in Chrome.
- Bulutta: GitHub uygulamasının depoya erişimi.

**Senin yapacağın:** ilk iş Higgsfield bağlantısını `balance` ile doğrula. Gerekmeyenler: n8n, tasarım eklentisinin asana/figma/notion vb. sunucuları, iOS Simulator. Ayrıntı devir §12.

## 12. İlk 60 dakikanın sırası (yerel C-00)

1. `git -C "/Users/Erdem/Documents/Deaths Demolition" log --oneline -5 main` (3bb501e7 ya da üstü), `git worktree list`, `df -h / "/Volumes/TOSHIBA EXT"`.
2. `tools/compile-check.sh main` (arka planda).
3. Oturumları aç (kalıp §7): S-06 → S-03 → S-08 → S-09 → S-02 → S-01; sonra S-05 (§17 inceleme), S-10, S-07.
4. AP-024C ve AP-025D'nin durumuna bak, yarımsa yeniden başlat (§8).
5. `gh pr list` ile C-00B PR'larına bak. Belge ve Python testli olanları doğrula ve entegre et. Yeni görevleri `docs/c00/C00B-GOREVLER.md`'ye yaz.
6. Kullanıcıya "devraldım" + görselli durum sayfası (R2 tablosu, yarım işler, bekleyen görseller).
7. **Sonra:**
   - S-06 teslimi → main-ci;
   - S-03 dilim 1;
   - S-08 dinleme kiti → kullanıcı;
   - S-09 MP4 → kullanıcı;
   - AP-024C/AP-025D renderları → kullanıcı;
   - D9 rollout;
   - WP-082/083 alımları;
   - R2 FINAL.

## 13. Açık riskler

- **Makine yükü:** swap 12 GB kapısı sık aşılıyor; aynı anda en çok 3–4 Unity koşusu.
- **Disk:** saatte 5–7 GB doluyor; şu an yaklaşık 70–78 GB boş.
- **Guard çakışmaları:** D9 ile kuyruğa alınacak. O zamana kadar oos_rule.
- **DEC-077:** kahraman torso lineer ΔY < 0,06.
- **Menü maliyeti:** canlı dünya SetPass 142, plakalı menü 19.
- **iCloud:** KADRO iCloud'da; dataless izleyici şu an 0. Taşıma penceresi runbook'ta (`K/wp-089/evidence/ICLOUD-TASIMA-RUNBOOK.md`).
- **Codex:** kota 12.10'da yenilenir; o zamana kadar üretim Claude'da.

## 14. Devirden sonraki son durum (05.10 ~19:40, bulut eki)

Bu bölüm, birinci hesabın limiti dolduktan sonra depodan doğrulanarak eklendi. §1–§13 aynen geçerlidir.

**main**
- main `3e68bc08` (bu belgenin ilk hâli).
- `a62d83db`'den sonra main'e yalnız belge commit'leri girdi (`3bb501e7`, `3e68bc08`).
- Son derleme kontrolü hâlâ `d0783d9b` (PASS). `03c3b06b` ve `a62d83db` derlenmedi. İlk iş: `tools/compile-check.sh main`.

**C-00B (bulut) teslimi**
- **B-02 BİTTİ:** dal `c00b/B-02-r2-gates-lint`, kod commit'i `b7737f04`, teslim kaydı commit'i `a7cd3bd3`. PR: https://github.com/ErdemEmirOzer/deaths-demolition/pull/1
- `tools/qa/r2_gates_lint.py` + `tools/qa/tests/test_r2_gates_lint.py`: 23 birim testi, bulutta 23/23.
- Aracın main `0b3ec358` raporu:
  - **ERROR 1:** `r2-measure-contract.md`'de `R2Olcum.cs` yolu bayat.
  - **WARN 14:** H1 `[Explicit]` ama `measuredBy` yok; Z3b'nin zorunlu testi `[Explicit]`; 12 kapının yazıcısı yok.
- **Yerel C-00 yapar:**
  1. `git fetch origin c00b/B-02-r2-gates-lint`, sonra dalın checkout'unda `python3 -m unittest tools/qa/tests/test_r2_gates_lint.py`.
  2. Üç commit'i sırayla al; teslim kaydı satırları birbirine dayandığı için sıra önemli. Gate isterse `--s01` ekle.
     - `tools/integrate.sh 4edbba5b --tag wp-089-c00b-b02-start-int --allow-paths docs/c00 --no-post --apply`
     - `tools/integrate.sh b7737f04 --tag wp-091-r2-gates-lint-int --no-post --apply`
     - `tools/integrate.sh a7cd3bd3 --tag wp-089-c00b-b02-log-int --allow-paths docs/c00 --no-post --apply`
  3. `git cherry main origin/c00b/B-02-r2-gates-lint` → "+" sayısı 0.
- ERROR ve WARN listesini S-10'a (kapı sahibi) ya da yeni bir C-00B görevine ver. Sözleşme yolu yalnız belge işi; `[Explicit]` ve yazıcı eksikleri test sahiplerinin işi.
- B-01, B-03 ve B-04 açık; C-00B sırayla sürdürüyor.

**Eksik devir notu: S-03 (WP-084)**
- `docs/c00/devir-notlari/` altında S-03 notu yok; §7'de "not bekleniyordu" yazıyor.
- Yerel C-00, S-03'ü açmadan önce `K/wp-088/worktree-s03` ve `K/wp-084*` altında `DEVIR-05.10.md` arar.
- Not yoksa durumu kendisi çıkarır ve S-03'ün başlangıç mesajına yazar:
  - `git -C <worktree> status --short`;
  - `git log -3`;
  - `git cherry main wp-084-s03-r2gates` (tip `a42f539b`; `c37991f7` / `cf8346a0` eşdeğerliği).

**Başlangıç promptları güncellendi**
- `C00-DEVIR-BASLANGIC-PROMPTU.md` artık ikinci hesaptaki yerel C-00 içindir. Önce bu belgeyi ve depodaki `docs/c00/` kopyalarını okutur.
- `C00B-IKINCI-HESAP-PROMPTU.md` artık asıl C-00'ı birinci hesapta değil, ikinci hesapta yerel olarak gösterir.
