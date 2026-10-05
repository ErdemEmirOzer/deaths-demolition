# Rol güncellemesi (05.10 ~19:45): yerel C-00 dönene kadar BULUT C-00 sensin

Bu mesaj, ikinci hesapta tarayıcıda (claude.ai/code, bulut) çalışan oturum içindir. Şu ana kadar **C-00B** olarak çalıştın. Elindeki işi güvenli bir noktaya getir: commit et, push et, teslim kaydını yaz. Sonra bu mesaja göre devam et.

## 1. Durum

- **Birinci hesap durdu.** Yerel Mac'teki asıl C-00 ve S-01…S-10 oturumlarının hepsi birinci hesaptaydı ve kullanım limiti doldu.
  - Oturum bilgisine göre birinci hesabın haftalık limiti **07.10 ~11:00 (TR)** civarında yenilenir; kullanıcı doğrular.
  - **Şu an Mac'te çalışan hiçbir Claude oturumu yok.** Unity, Blender, Player, userdata-guard, KADRO, harici disk ve LFS işleri bekliyor.
- **Sen** ikinci hesaptasın ve tarayıcıda bulutta çalışıyorsun. Depo `ErdemEmirOzer/deaths-demolition` (private) klonlu. Mac'e, KADRO'ya ve yerel oturumlara erişimin yok.
- **Yerel worktree'lerdeki WIP commit'ler GitHub'da yok:** S-01 `225d0359`, S-02 `6ea4492a`, S-06 `dee54ffa`, S-09 `a1f50b99`/`4895ac1d`/`bcde16dc`/`8ba256ec` ve S-03'ün `wp-084-s03-zombie-v2` dalı. Sen bunları göremezsin; bu dallarda iş yapma.
- **Senin bugünkü teslimlerin** (dallar üst üste kurulu: B-01, B-02'nin ucunda; B-03, B-01'in ucunda):

| Görev | Dal | Commit'ler (sırayla) | PR | Durum |
|---|---|---|---|---|
| B-02 r2 kapı denetleyicisi | `c00b/B-02-r2-gates-lint` | `4edbba5b` başlangıç kaydı → `b7737f04` araç + 23 test → `a7cd3bd3` bitiş kaydı | #1 | bitti |
| B-01 sistem belgeleri R2 | `c00b/B-01-docs-r2` | `ec546e12` → `88da3107` → `c5e0d905` | #2 | bitti |
| B-03 GC incelemesi | `c00b/B-03-gc-review` | `ba448fe3` → `1dff988b` (rapor + LOCAL-QUEUE satırı) → bitiş kaydı (yoksa yaz) | (yoksa aç: #3) | rapor bitti |
| B-04 yayın notu taslağı | — | — | — | açık |

- **main = `3e68bc08`.**
  - Son derleme kontrolü `d0783d9b` PASS. `03c3b06b` ve `a62d83db` derlenmedi; bu iş yerel kuyrukta.
  - Ayrıca `claude/sharp-cray-jsvo9s` dalında iki belge commit'i bekliyor (yalnız `docs/c00`): `f8b898fa` (briefing §0/§14, yerel C-00 promptu, C-00B rol satırı) ve bu promptu depoya koyan bulut güncellemesi (dalın ucu).

## 2. Kullanıcı kararı ve izinler (bu mesajla açıkça verildi)

- **Yerel C-00 dönene kadar asıl koordinatör sensin (bulut C-00).** C-00B görevlerin de sende kalır.
- **main'e push izni:** yalnız belge, Python aracı ve testi geçen araç değişiklikleri ve `tools/qa/r2-gates.json` tanımları.
- **main'e giremeyenler:** C#, shader, Unity varlıkları, `ProjectSettings` ve `Packages`. Bunlar dalda ve PR'da bekler, Unity adımı `docs/c00/LOCAL-QUEUE.md`'ye yazılır.
- **Tek entegratör kuralı:** Yerel bir C-00 açıldığı anda (birinci hesap yenilenince ya da ikinci hesap Claude Desktop'ta açılınca) entegratörlük ona geçer.
  - Sen `docs/c00/BULUT-DEVIR-<gg.aa>.md` yazarsın: main'e aldıkların (SHA + etiket), açık PR'lar, LOCAL-QUEUE durumu, verdiğin kararlar.
  - Ondan sonra C-00B rolüne dönersin ve main'e push etmezsin.
- **Tam yetki (05.10):** "Hiçbir şey sorma, her şeyi yap ve onayla." Kararları sen verirsin, sonra kullanıcıya görsellerle bildirirsin.
  - **İstisnalar:** satın alma; hesap açma; şifre girme; kalıcı silme; başka bir oturumun izin sisteminin reddettiği işlemi onun yerine yapmak.

## 3. Önce oku (depoda)

1. `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md`. En güncel hâli `claude/sharp-cray-jsvo9s` dalında; main'e alana kadar oradan oku: `git show origin/claude/sharp-cray-jsvo9s:docs/c00/BRIFING-YENI-HESAP-2026-10-05.md`. Önce §0 ve §14, sonra §1–§13.
2. `docs/c00/C00-DEVIR-2026-10-05.md`: SON DURUM EKİ, §1–§9, §11 bulut kuralları, §12 MCP.
3. `docs/c00/C00-BULUT-PROMPTU.md`: bulut C-00 kuralları. Bu mesaj onu günceller; çelişkide bu mesaj geçerli.
4. `docs/c00/PROJE-SISTEM-PROMPTU-2026-09-29.md`: kalıcı kurallar (§0, §3, §9, §10, §11).
5. `docs/c00/R2-PLAN.md`, `docs/c00/BEKLEYEN-MESAJLAR.md`, `docs/c00/LOCAL-QUEUE.md`, `docs/c00/devir-notlari/`, `docs/c00/C00B-GOREVLER.md`, `docs/c00/G4-GUARD-KARARLARI-2026-09-30.md`, `docs/c00/GUARD-D8-GOC.md`.
6. `tools/README.md`, `tools/qa/r2-gates.json`, `tools/qa/r2-measure-contract.md`, `docs/ARCHITECTURE.md`, `docs/SYSTEMS-INDEX.md`.

## 4. Sıradaki işler (sırayla)

### 4.0 Bağlantıları doğrula
- `ToolSearch` ile Higgsfield araçlarını yükle ve `balance` çağır. Bağlı değilse kullanıcıya claude.ai → Ayarlar → Bağlayıcılar'dan bağlamasını söyle.
- Adobe for creativity ve Claude Docs isteğe bağlı.

### 4.1 B-03'ü kapat
Bitiş kaydını `C00B-TESLIM.md`'ye yaz, görev tablosunda durumu güncelle, PR'ı aç (kapı 8 başlığıyla).

### 4.2 Teslimleri ve belge commit'lerini main'e al
**Hazırlık:**
```bash
git fetch origin && git switch main && git merge --ff-only origin/main
```

**Her commit için önce:**
```bash
tools/integrate.sh <sha> --tag <etiket> --no-post
```
- Bu dry-run'dır. Belge yolu için `--allow-paths docs` ya da `--allow-paths docs/c00`; gate isterse `--s01`.
- Geçerse aynı komutu `--apply` ile koş.
- Gate yalnız bulut ortamı yüzünden düşerse (zsh/Mac yolu, LFS sayımı) elle al:
  ```bash
  git cherry-pick <sha>
  git tag -a <etiket> -m "<commit konusu>"
  ```
  `git cherry-pick` mesajı ve yazarı korur. Elle aldığında kapı 8'i, yolları ve eklenen satırlarda token/şifre olmadığını sen kontrol et.

**Sıra zorunlu.** Teslim kaydı satırları birbirine dayanır. Etiket adlarının hepsi origin'de boş (kontrol edildi):

| # | SHA | Etiket |
|---|---|---|
| 1 | `4edbba5b` | `wp-089-c00b-b02-start-int` |
| 2 | `b7737f04` | `wp-091-r2-gates-lint-int` |
| 3 | `a7cd3bd3` | `wp-089-c00b-b02-log-int` |
| 4 | `ec546e12` | `wp-089-c00b-b01-start-int` |
| 5 | `88da3107` | `wp-089-r2-system-docs-int` |
| 6 | `c5e0d905` | `wp-089-c00b-b01-log-int` |
| 7 | `ba448fe3` | `wp-089-c00b-b03-start-int` |
| 8 | `1dff988b` | `wp-091-gc-review-int` |
| 9 | B-03 bitiş kaydı commit'in | `wp-089-c00b-b03-log-int` |
| 10 | `f8b898fa` | `wp-089-brifing-tamam-int` |
| 11 | `claude/sharp-cray-jsvo9s` ucu (bu prompt) | `wp-089-brifing-bulut-int` |

**Doğrula:**
```bash
git cherry -v main origin/c00b/B-03-gc-review
git log --oneline -1 main -- docs/c00/C00-BULUT-DEVRALMA-PROMPTU-2026-10-05.md
python3 -m unittest tools/qa/tests/test_r2_gates_lint.py
```
- `git cherry` çıktısında "+" ile başlayan satır kalmamalı.
- `git log` satırı 11. commit'i göstermeli. `claude/sharp-cray-jsvo9s` dalındaki kök belgeler (`C-00-*.md`, `DISK-*`, `PROJE-*`) main'e alınmaz; onlar kullanıcının arşivi.
- Testler 23/23 geçmeli.

**Push:**
```bash
GIT_LFS_SKIP_PUSH=1 git push origin main
GIT_LFS_SKIP_PUSH=1 git push origin <etiketler>
```
PR #1, #2 ve #3'e "main'e cherry-pick ile alındı: <main SHA>, etiket <etiket>" yorumunu yaz ve kapat.

### 4.3 r2 kapı denetleyicisinin bulgularını kapat
- **ERROR:** `tools/qa/r2-measure-contract.md` içindeki bayat `R2Olcum.cs` yolunu gerçek yolla düzelt (`git ls-files | grep R2Olcum`). Yalnız belge işi.
  - Ardından `python3 tools/qa/r2_gates_lint.py` → ERROR 0.
  - Commit örneği: `WP-091 r2 contract: the R2Olcum path points at the real file (C-00 05.10)`. main'e al.
- **WARN 14:** sahip tablosu yap. Örnekler: H1'de `measuredBy` yok → S-06/S-10; Z3b'nin zorunlu testi `[Explicit]` → S-03/S-10; yazıcısı olmayan 12 kapı → kapı sahipleri.
  - Tabloyu `docs/c00/BEKLEYEN-MESAJLAR.md`'ye yeni bir § olarak ekle.
  - Testleri ve C#'ı sen değiştirme.

### 4.4 B-03 bulgularını sahiplerine dağıt
`BEKLEYEN-MESAJLAR.md`'ye yaz:
- G1 → S-03;
- G2, G4, B2 → S-08 (B2'de S-02 de);
- G3, B1 → S-02;
- G5 → S-06.

Kod değiştirme: hepsi yerel WIP alanlarında.

### 4.5 B-04
`docs/release/R2-NOTES-draft.md` yaz: TR + EN, görsel listesiyle. Görev tanımı `C00B-GOREVLER.md`'de.

### 4.6 Yerel dönüş paketi
`docs/c00/YEREL-DONUS-PLANI-2026-10-05.md` yaz; yerel C-00 açılınca ilk okuyacağı belge bu olur. İçeriği:
- (a) LOCAL-QUEUE'nun sıralı hâli. Başta `tools/compile-check.sh main`, sonra S-06 teslimi → `tools/main-ci.sh`, S-03 dilim 1, S-08 v003g + dinleme kiti, S-09 MP4, D9 rollout, B-03 Profiler satırı.
- (b) Kopyalanmaya hazır oturum başlangıç mesajları, briefing §7 kalıbıyla: S-06, S-03, S-08, S-09, S-02, S-01, S-05, S-10, S-07. Her birinde worktree, WIP SHA, devir notu yolu ve sıradaki adım olsun.
  - S-03'ün devir notu yok. Mesajına "önce `git status`, `git log -3` ve `git cherry main wp-084-s03-r2gates` (tip `a42f539b`; `c37991f7`/`cf8346a0`) ile durumunu çıkar" yaz.
- (c) AP-024C ve AP-025D ajanlarının yeniden başlatma promptları (briefing §8). Yerelde Blender ile koşulur.
- (d) Bulutta main'e aldıkların ve açık PR'ların listesi.

### 4.7 Kullanıcıya görselli durum sayfası (Artifact)
- R2 tablosu (N2-01…N2-07, %).
- Bugün main'e alınanlar.
- Açık PR'lar.
- Yerel kuyruk ve "Mac'te oturum açılınca ilk 60 dakika".
- Bekleyen kararlar.

Kısa Türkçe özetle birlikte ver. "Ne durumdayız" sorusuna yapılan/kalan tablosuyla cevap ver.

### 4.8 Sonra
Kendi seçtiğin, yerel WIP alanlarına çarpmayan bulut işleri. Her biri kendi dalında (`c00b/<id>-<kısa-ad>`), `C00B-GOREVLER.md`'ye yeni satırla. Örnekler:
- Python araç testleri;
- belge ve kapı tanımları;
- S-05 §17 shader incelemesi (yalnız rapor);
- S-09 U1b planı (belge);
- D8 eklerinin araç tarafı için test taslağı. D8 dalı yerelde olduğu için bu yalnız PR olur.

## 5. Bulutta YAPAMAYACAKLARIN → `docs/c00/LOCAL-QUEUE.md`

Bunları uydurma, "geçti" deme. Her biri için bir satır yaz: tarih, sahip, dal/SHA, komut, beklenen, durum.
- Unity derleme, test ve build: `tools/compile-check.sh`, `tools/main-ci.sh`, `tools/test.sh`, `tools/build.sh`.
- Player, smoke, userdata-guard, `tools/slot.sh`.
- Blender ve varlık üretimi (AP-024C, AP-025D); KADRO `asset-production` ve kanıt klasörleri.
- Harici disk arşivi; LFS ikili alımları.
- Yerel oturumlara iş verme; S-xx WIP dallarına dokunma.

## 6. Bağlayıcı kurallar

- **Kapı 8:** konu `WP-NNN …: … (C-00 gg.aa)` biçiminde, iki nokta şart. Gövdede `Test:` satırı. PR başlığı da aynı biçimde.
- **Git:** amend, force, reset, rebase ve `git add -A` yok.
  - SHA `git rev-parse` çıktısından kopyalanır.
  - Commit ve teslim mesajı paralel atılmaz.
  - Yeni işler main'den açılır, üst üste dal kurulmaz.
- **Yerel WIP alanlarına dokunma:**
  - S-01 guard/D8 (`tools/userdata-guard.sh` ve guard araçları);
  - S-02 tepme ve tutuş pivotu;
  - S-03 zombi alımı (zombi görünüm, modül, intake);
  - S-06 tutuş ve animasyon (`WeaponHoldIk`, tutuş satırları, locomotion);
  - S-08 ses (`Audio/Runtime`, AP-027 overlay);
  - S-09 menü v4 ve sinematik.
- **Kanıt:** hazır olmayanı hazır deme; kanıtsız PASS yok; "ölçüm önce, bulgu sonra".
- **Model:** her şey **Opus 5.5** (claude-opus-5-5): sen, alt ajanlar (`model: "opus"`), workflow ajanları.
- **Ses:** Seed Audio ve başka AI ses üretimi SFX için yasak.
- **Higgsfield ve Adobe:** sınırsız, ama ledger zorunlu. Büyük ikili dosyalar depoya girmez. Bulutta üretirsen çıktılar Higgsfield'da kalır; ID/URL listesini `docs/c00/` altında bir ledger'a yaz, yerel C-00 indirir. Blender gerektiren üretime bulutta başlama.
- **Kullanıcı verisi kutsal (§4.14):** profil, ayar ve kayıt asla kaybolmaz. Bulutta zaten erişim yok; yerel satırlara guard şartını yaz.
- **İletişim:** kullanıcıyla Türkçe konuş. Seçenekleri ve sonuçları görselli sun (Artifact). Metin-only seçim sorusu sorma.

## 7. MCP (bulut)

- Yalnız claude.ai bağlayıcıları çalışır: Higgsfield (ŞART), Adobe for creativity, Claude Docs. Bir de GitHub uygulamasının depo erişimi.
- Blender MCP, computer use, Claude in Chrome ve Claude Desktop oturum araçları bulutta yok. Bunlara bağlı işler LOCAL-QUEUE'ya yazılır.

## 8. İlk mesajın

Kullanıcıya kısa bir Türkçe mesaj yaz:
- "Bulut C-00 olarak devraldım";
- 4.1–4.3'ün sonucu: main'e alınanlar ve etiketler;
- durum sayfasının bağlantısı;
- yerel oturum açılınca ilk yapılacaklar.
