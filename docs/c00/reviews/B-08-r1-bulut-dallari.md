# B-08 — R1 bulut dallarının güncel main'e karşı durumu

**Hazırlayan:** git-repository-safety (KADRO, yerel C-00 alt ajanı) · **Tarih:** 06.10.2026 · **Tür:** salt okuma. Hiçbir dal birleştirilmedi, silinmedi, push edilmedi; ana depoda değişiklik yok.

**Taban:** `origin/main` = `main` = `18f3d01e3fff7d9169981848e66e069ed4a3b24d`. `git fetch` yapılmadı (C-00 yaptı); yerel ref'ler kullanıldı.

**Yöntem:**
- Her dal için tip, merge-base, `git rev-list --left-right --count origin/main...<dal>`, `git cherry -v origin/main <dal>` ve `git patch-id --stable` eşleşmesi alındı.
- Her dosyanın blob SHA'sı, main ağacında ve main geçmişinde (`git log --find-object`) arandı.
- Her bulgu güncel main'de kod okunarak (`git show origin/main:<yol>`, `git grep`) ve kapanış commit'i `git merge-base --is-ancestor <sha> origin/main` ile doğrulanarak sınıflandı.
- Unity, test ve Windows koşusu yapılmadı. "KAPANDI" = kod ya da karar main'de; çalışma zamanı kanıtı değildir.

**Yol kısaltmaları:**
- `P/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/`
- `PS/` = `prototype/DeathsDemolition_PlayableConcept/ProjectSettings/`
- `PSP` = `docs/c00/PROJE-SISTEM-PROMPTU-2026-09-29.md`
- `PLAN00` = `planning/00-PROJECT-PLAN.md`
- `DIR-069` = `planning/work-packages/WP-069-ARSENAL-V3-DIRECTION.md`
- `DIR-077` = `planning/work-packages/WP-077-PROGRESSION-TREE-V2-DIRECTION.md`
- `WP-066` = `planning/work-packages/WP-066-RELEASE-GOVERNANCE.md`

**Önem eşlemesi** (raporlar farklı ölçek kullanıyor; burada tek ölçeğe çevrildi, özgün etiket tablolarda korunur):

| Bu belgede | Özgün etiketler |
|---|---|
| **BLOCKER** | BLOCKER, CRIT |
| **MAJOR** | HIGH, MED-HIGH, MEDIUM, MED, 🔴, 🟠 |
| **MINOR** | MED-LOW, LOW-MED, LOW, 🟡, ⚪ |

Sahip oturumlar `PSP` §6 tablosundan (`PSP:183-192`): S-01 build/smoke/araçlar/release governance, S-02 silah/girdi, S-03 AI/round, S-04 ekonomi/kayıt, S-07 VFX/render, S-08 ses, S-09 UI, S-10 kabul kapıları.

---

## 1. Özet

### 1.1 Dal tablosu

| Dal | Tip SHA | Commit tarihi | Merge-base | main…dal (geride / ileride) | `git cherry` | Dosyalar | Main'deki karşılığı | Bulgu: toplam / açık |
|---|---|---|---|---|---|---|---|---|
| `cloud/windows-audit` | `5abbf8d33e` | 2026-09-24 20:13 UTC | `cf341808` | 772 / 1 | `+` | `planning/reviews/WINDOWS-PORTABILITY-AUDIT.md` (338 satır) | **YOK.** Blob `bcb0961d` ne main ağacında ne main geçmişinde. | 23 / **22** (2 BLOCKER) |
| `cloud/review-r1-a` | `35ba58e67b` | 2026-09-24 21:05 +0300 | `f52647f4` | 818 / 1 | `−` (patch-id = `a7f25570`) | `planning/reviews/CLOUD-REVIEW-R1-A.md` | Aynı blob `b3125cb4`, `a7f25570` ile | 19 / 2 (yalnız test boşluğu) |
| `cloud/review-r1-b` | `890eecf749` | 2026-09-24 20:16 UTC | `a2971897` | 773 / 1 | `+` (commit eşdeğeri yok) | `planning/reviews/CLOUD-REVIEW-R1-B.md` | **Aynı blob `4f179506`**, `38c72bdb` ile (30.09, "değiştirilmeden") | 37 (+2 yan not) / 0 |
| `cloud/balance-audit-r1` | `d61082818e` | 2026-09-24 18:20 UTC | `a7f25570` | 816 / 1 | `−` (patch-id = `ad42fc6e`) | rapor + `tools/balance/r1_audit.py` | Aynı bloblar (`7a8f7255`, `a445adf3`), `ad42fc6e` ile | 14 (1'i ✅) / 5 (belge ve oyun testi) |
| `cloud/balance-audit-r1-run2` | `b5fa840170` | 2026-09-24 18:23 UTC | `a7f25570` | 816 / 1 | `+` (yeniden adlandırma) | rapor + `tools/balance/r1_audit.py` | **Aynı bloblar başka adla:** `planning/reviews/CLOUD-BALANCE-AUDIT-R1-run2.md` (`c16cd0c5`) ve `tools/balance/r1_audit_run2.py` (`37ebc875`), `4854a756` ile | 11 / 3 |
| `cloud/content-pack-r1` | `19b984902f` | 2026-09-24 18:13 UTC | `f52647f4` | 818 / 1 | `−` (patch-id = `02aab39b`) | `planning/content/R1-CONTENT-PACK.md`, `planning/content/r1-strings.json` | Eklendi `02aab39b`; main'de gelişti (son değişiklik `d62a9b95`, 05.10) | Q1–Q14 belirsizlik listesi / 1 eylemli açık (Q7) |
| `cloud/portfolio-docs` | `cd83233599` | 2026-09-24 18:16 UTC | `a7f25570` | 816 / 1 | `−` (patch-id = `a99daea2`) | `README.md`, `docs/ARCHITECTURE.md`, `docs/SYSTEMS-INDEX.md` | Eklendi `a99daea2`; yenilendi `b99c3a54` → `68368cd5` → `66f0cd9f` | Bulgu yok |

**Ana sonuç:**
- Yedi daldan **yalnız `cloud/windows-audit`'in içeriği main'de yok.**
- `review-r1-b` ve `balance-audit-r1-run2` `git cherry`'de `+` görünüyor, ama dosyaları blob düzeyinde main'de:
  - review-r1-b: aynı yol, `38c72bdb`;
  - run2: `-run2` adlarıyla, `4854a756`.
- `PSP:366` ("Main'e alınmayan: `cloud/review-r1-b` …") ve `PSP:438` ("`cloud/review-r1-b` ve `cloud/windows-audit` (main'e alınmadı)") bu yüzden **bayat**: review-r1-b 30.09'dan beri main'de. Düzeltme `PSP`'nin sahibi C-00'a düşer (§8).
- Yerel bir `cloud/review-r1-a` dalı da var: `refs/heads/cloud/review-r1-a` = `35ba58e6`, upstream `origin/cloud/review-r1-a`.
- Hiçbir cloud tipini içeren etiket yok.

### 1.2 Açık bulgular sahip başına (bütün dallar, tekilleştirilmiş)

| Sahip | BLOCKER | MAJOR | MINOR | Toplam | Kalemler |
|---|---|---|---|---|---|
| **S-01** | 1 | 4 | 3 | **8** | W-B2, W-H4, W-H5, W-M5, W-M6, W-L5, W-L6, W-L8 |
| **S-04** | 1 | 3 | 3 | **7** | W-B1, W-H1, W-M1, W-M2, W-L7, BAL-XP (run2 #6), BAL-DOC-077 |
| **S-02** | 0 | 2 | 4 | **6** | W-M3, W-M4, BAL-SMG2 (run1 #8), BAL-DOC-069, BAL-DEC (run1 #12), R1A-#2 test |
| **S-09** | 0 | 1 | 3 | **4** | W-H2, W-L2, W-L3, CP-Q7 |
| **S-07** | 0 | 1 | 1 | **2** | W-H3, W-L1 |
| **S-03** | 0 | 0 | 1 | **1** | R1A-#11 test |
| **S-08** | 0 | 1 | 0 | **1** | W-M7 |
| **Kullanıcı (C-00 aracılığıyla)** | 0 | 0 | 1 | **1** | W-L4 (ürün adı, WP-066 🔴 1) |
| **Toplam** | **2** | **12** | **16** | **30** | |

- S-10 ortak sahip olarak iki BLOCKER'ın kapısını tutar (W-B1, W-B2); birincil sahip değildir.
- C-00 karar kalemleri (W-H4, W-M2, W-H5 politikası, R2 win64 etiketi) ilgili satırlarda geçer.

---

## 2. `cloud/windows-audit` — iki BLOCKER (ayrıntılı)

**Bağlam:**
- DEC-089 hedef platformları macOS + Windows yaptı (`PLAN00:161`; `PSP:80` kural 31).
- R2 planı WP-091'de "release (mac + win64)" içeriyor (`docs/c00/R2-PLAN.md:45`, `:77`). Yayın notu taslağının platform satırı "macOS + Windows 64" (`docs/release/R2-NOTES-draft.md:93`).
- İlk Windows release build'i derlendi, ama **hiç çalıştırılmadı.** WP-066 bunu açıkça yazıyor: "Çalıştırma/smoke: koşturulmadı … 'Hazır' sayılmaz" (`WP-066:96`).
- Raporun tabanı `cf341808`, main'in 772 commit gerisinde. Aşağıdaki satır numaraları **güncel main'dendir**; rapordakilerden kayabilir.

### 2.1 W-B1 — Windows'ta kayıt kilidi ve atomik değiştirme yolu hiç koşmadı; çapraz süreç testleri Windows'ta kendini kapatıyor → **AÇIK**

**Kod yolu (main):**

| Adım | Yer | Not |
|---|---|---|
| Kayıt zinciri belgesi | `P/Profile/ProfileStore.cs:79-92` | Adım 5 "one rename(2) via File.Replace" (`:85-86`) |
| Aday yazımı | `:534` | `FileShare.Read`, `Flush(true)` (`:544`) |
| Yedek yenileme | `:768-784` | `File.Replace(staging, backupPath, null)` (`:772`); yalnız `PlatformNotSupportedException` yakalanır (`:774-779`) |
| Primary değiştirme | `ReplacePrimary` `:622-644` | `File.Replace(candidatePath, path, null)` (`:635`); yorum "a single rename(2)" (`:632-634`); düşüş yalnız `PlatformNotSupportedException`'da (`:637-643`) |
| Hata sonrası kurtarma | `:592-607` → `RecoverAfterFailedReplace` `:656-706` | İşletim sisteminden bağımsız; eski primary'i geri koyar |
| Sahiplik (lease) belgesi | `:809-834` | "fcntl POSIX record lock: owned by the process … released by the kernel" (`:813-815`) |
| Lease alma | `:862-875` | `new FileStream(lockPath, OpenOrCreate, ReadWrite, FileShare.ReadWrite)` + `stream.Lock(0, 1)`; **her** `IOException` "başka kopya" sayılıp oturum salt okunur olur (`:867-874`) |
| Fault hook'ları | `:170-185`, `:611-620`, `:855` | `SaveStageHook`, `BackupWriteMidFault`, `LockAcquisitionFault`; yalnız `DEVELOPMENT_BUILD || UNITY_EDITOR` |

**Platform koşulu:**
- Yalnız Windows Player'da (Mono, x86_64, D3D11; `WP-066:94-97`) farklı davranır.
- Raporun tespiti (rapor bunu "(verify)" yerine kesin yazmış ama Windows'ta koşturulmamış):
  - `FileStream.Lock` Win32 bayt aralığı kilidine düşer. Bu kilit **zorunludur** ve süreç değil **tutamak** sahibidir.
  - `File.Replace` `ReplaceFileW` olur. `rename(2)` değildir; değiştirilen dosyada `FILE_SHARE_DELETE`'siz açık bir tutamak varsa reddeder.
- Kodda platforma özgü dal yok:
  - `git grep 'UNITY_STANDALONE_WIN|UNITY_EDITOR_WIN|RuntimePlatform.Windows|WindowsPlayer|taskkill|ReplaceFileW'` runtime ve araçlarda yalnız `P/Tests/EditMode/UiCursorSizeTests.cs:20-23`'ü buluyor.
  - WP-066 de aynısını söylüyor: "Platforma özgü kod … yok" (`WP-066:97`); "Profil kilidi … ve atomik replace Windows dosya sisteminde doğrulanmadı" (`WP-066:98`).

**Test durumu:**
- Çapraz süreç kanıtının tamamı Unix'e bağlı (`P/Tests/EditMode/ProfileStoreFaultTests.cs`, cf34180'dan beri değişmedi):
  - `:182` `/usr/bin/python3`;
  - `:209` `/bin/kill -9`;
  - `:358-360` `Assert.Ignore("kill -9 kanıtı için python3 ve /bin/kill gerekli")`;
  - `:611`, `:992`, `:1031` `Assert.Ignore("python3 yok …")`;
  - `:943-960` Python `fcntl.lockf` yardımcısı.
- Windows Editor'da bu testler **atlanır**, kırmızı olmaz. Yani "yeşil" görünür ama kanıt sıfırdır.
- **Rapordaki düzeltme:**
  - Rapor "lease fazı ekle" diyor, ama `P/Diagnostics/ProfilePersistenceProbe.cs` zaten iki gerçek süreçli **L1/L2** lease fazlarını içeriyor (`:201-202`, `:800-858`).
  - Probe yalnız dev build'de var (`#if DEVELOPMENT_BUILD || UNITY_EDITOR`, `:1`) ve Python'suz, taşınabilir C#.
  - Eksik olan, probe'u Windows'ta koşturacak **sürücü**: `tools/` altında `ddProfileProbe` geçen bir sürücü yok; repoda hiç `*.ps1` yok.
- **Küçük tutarsızlık:**
  - `ProfileStoreFaultTests.cs:943-946`, `:992`, `:1031` "probe E/F fazları"na atıf yapıyor;
  - probe'un fazları ise A/B/C/D/V2/L1/L2 (`ProfilePersistenceProbe.cs:195-203`).

**Neden BLOCKER:**
- WP-009H dayanıklılık sözleşmesinin (lease, kayıt ortasında kill, ileri/geri sarma) bir release platformunda hiç kanıtı yok.
- Raporda ilişkili iki somut risk var:
  - **W-H1:** yeniden deneme yok → Windows'ta en olası "ilerlemem kaydolmadı" şikâyeti;
  - **W-M2:** çökme sonrası bayat kilit → oturum salt okunur açılır.
- Bu ikisi kayıp yerine **kaydedilmeyen ilerleme** üretir. Kurtarma zinciri profili bozmaz (`:656-706`).

**Düzeltme önerisi:**
- **Sahip:** S-04 (kayıt dayanıklılığı). **Kapı:** S-10.
- **Bağımlılık:** Windows makinesi/CI kullanıcı kararı (`WP-066:112`, "Windows çalıştırma/smoke için makine/CI").

1. **Mac'te yapılabilir kod (Windows beklemez):**
   - a) `File.Replace` ve `File.Move` çağrılarını (`:626`, `:635`, `:642`, `:772`, `:783`, `:797`, `:1100`) sınırlı bir yeniden deneme sarmalayıcısına al.
     - Koşul: `IOException` ya da `UnauthorizedAccessException`, `HResult & 0xFFFF` ∈ {5, 32, 33, 1175, 1176, 1177}.
     - 6 deneme, 25 → 400 ms; deneme sayısı loglanır.
     - Yalnız hata durumunda bekler; en kötü durumda kayıt anında ≈ 0,8 s, kare yolunda değil. (W-H1)
   - b) Okumaları (`:400`, `:715`, `:721`) `FileShare.ReadWrite | FileShare.Delete` açan bir yardımcıdan geçir. (W-H1)
   - c) `Lock(0,1)` için ≈ 1,5 s yeniden deneme, ardından salt okunur; `HResult` 33 ile diğerlerini ayrı logla. (W-M2)
   - d) Mevcut `LockAcquisitionFault` kalıbıyla (`:185`, `:855`) yeni bir `ReplaceAttemptFault` hook'u ekle.
     - macOS'ta EditMode, `HResult` 32 benzetimiyle yeniden deneme yolunu kanıtlar.
     - Bu, mantık kanıtıdır; platform kanıtı değildir.
   - e) `:85-86`, `:632-634`, `:813-815` yorumlarını iki platformu da anlatacak şekilde düzelt.
2. **Testler:**
   - `Assert.Ignore` korumalarını `Application.platform` anahtarına çevir.
   - Windows'ta ikinci süreç = probe L1/L2 çalıştıran ikinci dev Player; öldürme = `Process.Kill()` (`/bin/kill` yerine).
   - `:943-946` "E/F" atfını L1/L2 yap. (W-L7 buraya bağlıdır.)
3. **Kanıt ve kapı (S-10, Windows host gelince):**
   - Dev build'de probe A–D + L1/L2;
   - sonuç kaydı sırasında `taskkill /F` ve hemen yeniden açılış (W-M2);
   - `prototype-profile.json` başka bir uygulamada açıkken kayıt (W-H1);
   - Defender açık.
   - Bu, WP-091 release kapısına "Windows profil kanıtı" satırı olarak yazılır.
4. **Önerilen paket:** "Windows profil dayanıklılığı" (S-04 + S-10). Numarayı C-00 verir. Kapsam: W-B1 + W-H1 + W-M1 yardımcısı + W-M2 + W-L7.

### 2.2 W-B2 — Windows smoke'u ve kullanıcı verisi guard'ı yok; release exe gerçek profile ve kayıt defterine yazar → **AÇIK**

**Kod ve araç yolu (main):**
- `tools/build.sh:9-10`: "win64 … no smoke (cannot run on macOS)". `:49`: `--target win64 --smoke` 64 koduyla reddedilir. (Raporda `:43`; satır kaydı.)
- `tools/smoke.sh:1-40`: zsh. Guard `defaults`, plist, `cfprefsd` ve `~/Library` üzerine kurulu.
- `tools/userdata-guard.sh:132`: `AS="$HOME/Library/Application Support"; PR="$HOME/Library/Preferences"`. Windows yolu yok.
- `tools/dd-env.sh:17`: Windows'a ait yalnız `DD_WIN_DIR` ve `DD_WIN_EXE` tanımlı.
- Repoda `*.ps1` yok (`git ls-tree -r origin/main tools`).
- `WP-066:99` aynı açığı kaydediyor: "`smoke.sh` macOS'a özgü … Windows smoke ancak Windows makinede/CI'da, ayrı betikle. `tools/qa/rc.sh` yalnız macOS."

**Platform koşulu:**
- Windows release Player'ının `-ddProfilePath` yönlendirmesi yok (`tools/smoke.sh:8-10`). Şu yerlere yazar:
  - `%USERPROFILE%\AppData\LocalLow\Deaths Demolition\Deaths Demolition - Playable Concept\`: profil, ayar JSON'ları, `telemetry\`, `Logs\`, ayrıca Windows'ta `Player.log`;
  - `HKCU\Software\Deaths Demolition\Deaths Demolition - Playable Concept` (`WP-066:98`).
- Ürün adı `PS/ProjectSettings.asset:16`'dan gelir.

**Test durumu:**
- Windows e1/e4 smoke'u, istisna taraması, guard ya da perf koşusu hiç yapılmadı (`WP-066:96`).

**Neden BLOCKER:**
- Proje kuralına göre **her** Player ve PlayMode koşusu guard'ın snapshot → koşu → restore akışı içinde olmalı (`PSP:104`, kural 49; `PSP:105`, kural 50).
- Windows'ta guard olmadığı için hiçbir oturum Windows Player'ını kurala uygun koşturamaz.
- Bu yüzden R2'nin "release (mac + win64)" adımı Windows tarafında **kanıtsız** kalır.

**Düzeltme önerisi:**
- **Sahip:** S-01 (build/smoke/araçlar, WP-025/066). **Kapı:** S-10.
- **Bağımlılık:** aynı kullanıcı kararı (`WP-066:112`).

1. **Mac'te şimdi yapılabilir:**
   - a) `build.sh --target win64` derleme logunda `Shader error in` görünce başarısız olsun. Bugün yalnız `error CS` sayılıyor ve `"result": "Succeeded"` aranıyor (`tools/build.sh:84`, `:87`). Bu, W-M6'yı kapatır ve koşturulmamış exe'nin pembe materyalle çıkma riskini azaltır.
   - b) win64 çıktısını zip'le, SHA-256'sını al ve bir "smoke kiti" (komut satırı + beklenen kareler) üret.
2. **PowerShell hattı** (raporun §4 maddeleri 3–5):
   - `dd-env.ps1`;
   - `smoke.ps1` (e1/e4, `-logFile`, `Stop-Process -Force`, `-ddUiCapture`);
   - `userdata-guard.ps1`: LocalLow dizini + `reg export`, geri yüklemede `reg delete` + `reg import`; `*.lock`, `Player.log`, `Player-prev.log` hariç; `Get-FileHash` ile SHA-256; yeni dosyalar `created\` altına taşınır, silinmez (W-L5, W-L6).
   - Host gelene kadar bu betikler **"yazıldı, koşturulmadı"** etiketini taşır; kanıt sayılmaz.
3. **C-00 kararı (öneri):** Windows host yokken R2 win64 artefaktı "derlendi, koşturulmadı" diye etiketlenir (`WP-066:96` dili). `docs/release/R2-NOTES-draft.md:93` platform satırı bu koşula bağlanır.
4. **Önerilen paket:** "Windows smoke ve kullanıcı verisi guard hattı" (S-01 + S-10). Kapsam: W-B2 + W-L5 + W-L6 + W-M6.

### 2.3 `cloud/windows-audit` — diğer bulgular

Kapanış kanıtı olarak "değişmedi" diyen satırlarda `git rev-list --count cf34180..origin/main -- <dosya>` = 0'dır.

| # | Özgün → norm | Konu | Main'deki durum ve kanıt | Sahip | Paket önerisi |
|---|---|---|---|---|---|
| H1 | HIGH → MAJOR | Değiştirme, taşıma ve yedekte Windows paylaşım ihlali için yeniden deneme yok | **AÇIK.** `ProfileStore.cs:622-644`, `:772`, `:797`, `:1100`'de yeniden deneme yok; `HResult` ya da `Retry` geçmiyor. Okumalar varsayılan `FileShare.Read` (`:400`, `:715`, `:721`). Dosyaya `cf34180`'dan beri yalnız 3 satırlık `HasPathOverride` eklendi (`e5888d47`). | S-04 | Windows profil dayanıklılığı |
| H2 | HIGH → MAJOR | Tam ekran, pencereli backbuffer boyutunu koruyor (verify) | **AÇIK.** `P/UI/Runtime/Kit/UiDisplayMode.cs:98-110` yalnız mod isteği yapar; `:112-151` geri yüklenen boyutu monitöre sıkıştırmaz. Dosya değişmedi. | S-09 | Windows ekran ve UI |
| H3 | HIGH → MAJOR | Donmuş kare bulanıklığının Y-çevirmesi yalnız Metal'de ölçüldü (verify) | **AÇIK.** `P/UI/Runtime/Fx/UiFrozenFrame.cs:294-299` (`FlipFor` `projectionFlipped`'i yok sayar), `:347-353` eski `cmd.Blit`. Dosya `cf34180`'dan beri değişmedi. | S-07 (+S-09) | Windows ekran ve UI |
| H4 | HIGH → MAJOR | Varsayılan 1920×1080 pencere yaygın Windows dizüstülerine sığmaz | **AÇIK.** `PS/ProjectSettings.asset:45-46` 1920×1080, `:80` `defaultIsNativeResolution: 0`, `:106` `fullscreenMode: 3`. İlk açılışta `UiSettings.cs:637-640` hiçbir şey yapmaz (`DisplayMode < 0`). | S-01 + **C-00 kararı** | Platform varsayılanları ve kültür |
| H5 | HIGH → MAJOR | tr-TR kültürü UI sayılarına ve CLI ayrıştırmasına ulaşıyor | **KISMEN.** Kapanan: `66eb3318` (M-65) UI sayılarını kültürsüz `UiStrings.Number`'a aldı (`UiStrings.cs:65-73`, `RunHudViewModel.cs:411-412`). Açık: (1) küresel `InvariantCulture` ataması yok (`DefaultThreadCurrentCulture` grep = 0); (2) `PauseScreen.cs:690`, `:702` `ToString("0.00")`; (3) `Rounds/UpgradeCard.cs:466-490` kültürsüz `string.Format`; bu metni `RoundUpgradeShop.cs:1646` doğrudan, `UiUpgradeCopy.cs:144` geri düşüş olarak kullanır; (4) CLI'da `PrototypeCaptureAgent.cs:94`, `UiCaptureAgent.cs:366`, `:829` kültürlü `float.TryParse`; (5) `PlayerDebugHooks.cs:131-181` (yalnız debug). | S-01 (küresel), S-09 (Pause), S-04 (UpgradeCard) | Platform varsayılanları ve kültür |
| M1 | MEDIUM → MAJOR | Ayar JSON'ları atomik olmayan yazımla kaydediliyor | **AÇIK.** `UiDataFiles.cs:96`, `Core/Input/InputPreferences.cs:340`, `Input/InputRemapService.cs:218` hâlâ `File.WriteAllText`. `AtomicTextFile` yok. | S-04 yardımcısı; S-09 ve S-02 benimser | Windows profil dayanıklılığı |
| M2 | MEDIUM → MAJOR | Çökme sonrası bayat kilit oturumu salt okunur yapar | **AÇIK.** `ProfileStore.cs:865-874`: tek deneme, her `IOException` salt okunur sayılır. `PS/ProjectSettings.asset:93` `forceSingleInstance: 0`. | S-04 + **C-00 kararı** (single instance) | Windows profil dayanıklılığı |
| M3 | MEDIUM → MAJOR | Windows'ta fare bakış hissi farklı (ham girdi) | **AÇIK.** `Input/PrototypeInputSource.cs:428` ham `Mouse.current.delta`. `Core/Input/InputPreferences.cs:54` varsayılan 0,08 (aralık 0,02–0,30). Platforma göre ölçek yok. | S-02 | Girdi Windows kalibrasyonu |
| M4 | MEDIUM → MAJOR | DirectInput'taki PlayStation kolunda Cancel = Cross | **AÇIK.** `UiInput.cs:23`, `:53`, `:64`, `:87`, `:106`, `:118` eski `Cancel`/`Submit` düğmelerini okur. `PS/InputManager.asset:189`, `:205`, `:255`, `:287`, `:335`, `:383` `joystick button 0/1`. **Not:** raporun önerisi (UI'da `Gamepad.current.buttonEast`) ADR-024-2'ye aykırı: UI `UnityEngine.InputSystem` okumaz (`PSP:322`, `planning/04-IMPLEMENTATION-PLAN.md:937`). Doğru yol, S-02'nin Core/Input'ta UI Submit/Cancel yüzeyi açması ve S-09'un bunu tüketmesi. | S-02 (+S-09) | Girdi Windows kalibrasyonu |
| M5 | MEDIUM → MAJOR | Crash log ev dizini maskesi Windows yazımlarını kaçırıyor | **AÇIK.** `Diagnostics/CrashLogReporter.cs:252-261` Ordinal karşılaştırır; önce `HOME`, sonra `UserProfile` okunur (`:295-298`). Dosya değişmedi. Kod WP-025'ten (S-01). | S-01 (raporda S-10) | Platform varsayılanları ve kültür |
| M6 | MEDIUM → MAJOR | D3D11 shader hatası build'i kırmıyor | **AÇIK.** `tools/build.sh:84`, `:87` yalnız `error CS` ve `"Succeeded"`'a bakar. `tools/qa/log_scan.py:21`'de desen var, ama o araç RC artefaktlarını tarar ve build kapısı değildir. | S-01 | Windows smoke ve guard hattı |
| M7 | MEDIUM → MAJOR | Cihaz değişiminden sonra ses döngüleri sessiz kalıyor | **AÇIK.** `Audio/Runtime/PrototypeAudioDirector.cs:774-777` yalnız sayaç artırır (`:309` abonelik); döngüler yeniden başlatılmaz. | S-08 | Ses cihaz değişimi (küçük) |
| L1 | LOW → MINOR | `half` hassasiyeti (D3D11 → float) | **AÇIK (görsel doğrulama).** Kod değişikliği beklenmiyor. Windows host gerekir. | S-07 | Windows ekran ve UI |
| L2 | LOW → MINOR | Alt+Enter ayarı bayatlatıyor | **AÇIK.** `PS/ProjectSettings.asset:105` `allowFullscreenSwitch: 1`. | S-09 | Windows ekran ve UI |
| L3 | LOW → MINOR | Donanım imleci boyutu | **AÇIK (doğrulama).** Boyut matematiği `UiCursorSizeTests.cs:20-23` ile kapalı; donanımda görülmedi. | S-09 | Windows ekran ve UI |
| L4 | LOW → MINOR | Yol uzunluğu ve `productName` değişimi | **AÇIK, karar.** `PS/ProjectSettings.asset:16`. WP-066 🔴 1 (`WP-066:111`, ürün adı + göç). | Kullanıcı | — |
| L5 | LOW → MINOR | PlayerPrefs kayıt defterinde | **AÇIK.** W-B2 guard'ının parçası. | S-01 | Windows smoke ve guard hattı |
| L6 | LOW → MINOR | `Player.log` `persistentDataPath` içinde | **AÇIK.** W-B2 guard'ının parçası. | S-01 | Windows smoke ve guard hattı |
| L7 | LOW → MINOR | Test taşınabilirliği | **AÇIK.** W-B1'in parçası. | S-04 (+S-10) | Windows profil dayanıklılığı |
| L8 | LOW → MINOR | Kozmetik saat ayırıcısı | **AÇIK.** `CrashLogReporter.cs:75`, `:155`. H5'in küresel düzeltmesi kapatır. | S-01 | Platform varsayılanları ve kültür |
| L9 | LOW | Tam sayı CLI ayrıştırması | **EYLEM YOK.** Rapor "listed for completeness" diyor; sayılmadı. | — | — |

---

## 3. `cloud/review-r1-a` — 19 bulgu (rapor main'de, `a7f25570`)

`PSP:366` "19 bulgu, düzeltildi" diyor. Bu belge her satırı commit ile doğruladı; R1-B'nin "R1-A fix verdicts" tablosu (`planning/reviews/CLOUD-REVIEW-R1-B.md:128-140`) ikinci kanıttır.

| # | Özgün → norm | Konu | Durum | Kapanış kanıtı |
|---|---|---|---|---|
| 1 | HIGH → MAJOR | TP/FP paralaks koruması yok | KAPANDI | `663a5845`; ayar düzeltmesi R1-B W1 `63e705dc` (`Weapons/PlayerWeaponBinding.cs:169`) |
| 2 | HIGH → MAJOR | İlk mermi segmenti namlu içindeki collider'ı kaçırıyor | KAPANDI (kod) · **test boşluğu** | `663a5845`. R1-B: "The test covers only the helper, not an end-to-end hit" (`CLOUD-REVIEW-R1-B.md:133`). Uçtan uca isabet testi açık → S-02 MINOR |
| 3 | HIGH → MAJOR | RagdollProxyPool sahneyle ölüyor | KAPANDI | `99716fba`; R1-B "Fixed … regression test added" (`:136`) |
| 4 | MED-HIGH → MAJOR | Tide Jewel havada iniyor | KAPANDI (kapsam değişti) | `99768948`: Kagura/Ofuda/Tide kaldırıldı (DEC-094, `PLAN00:166`); bakış modunda 3B atış `SpecialWeaponRuntime.cs:415-416` ("review R1-A #4") |
| 5 | MEDIUM → MAJOR | ProjectileEvents ortak anahtar yok | KAPANDI | `663a5845`; R1-B `:134` |
| 6 | MEDIUM → MAJOR | Dokunma kümesi tavanı 8 | KAPANDI | `663a5845`; R1-B `:135` (192) |
| 7 | MEDIUM → MAJOR | `IsTransitioning` küresel | KAPANDI | `3eb89551`; `Camera/CameraModeService.cs:213-215` |
| 8 | MED-LOW → MINOR | Pompalı çoklu sarsıntı | KAPANDI | `663a5845` |
| 9 | LOW-MED → MINOR | Yumuşak bant menzili | KAPANDI | `3a94ab0d`; R1-B `:137` |
| 10 | LOW → MINOR | Kapma kaçışı kısılmış itkiyi okuyor | KAPANDI | `3a94ab0d`; R1-B `:138` |
| 11 | LOW → MINOR | `GrabState.ResetStatics` aboneleri siliyor | KAPANDI (kod) · **test boşluğu** | `3a94ab0d`; `Enemies/AttackStyles.cs:287-301` (`RunEvents.RunStarted` sıfırlaması). Regresyon testi yok: AttackSlotBoard/GrabState testlerinde `RunStarted` geçmiyor; R1-B `:139` "no regression test" → S-03 MINOR |
| 12 | LOW → MINOR | Ragdoll tier tavanı / uzak LOD | KAPANDI | `99716fba`; AI6 notu R1-B'de, `c3b9cc52` ile kapandı |
| 13–16, 19 | LOW → MINOR | Yeniden giriş, sürücü, yerçekimi, bakış saçılması, `ClassicWeapons` | KAPANDI | `663a5845` (commit konusu: "#1 #2 #5 #6 #8 #13 #14 #15 #16 #19") |
| 17 | LOW → MINOR | Kamera geçişinde görünürlük t = 0'da | KAPANDI | `3eb89551`; `PrototypeCameraRig.cs:868`, `CameraOcclusionFader.cs:125` |
| 18 | LOW → MINOR | Ölüm kilidi bağlı değil | KAPANDI | `3eb89551`; `Feedback/DeathSequence.cs:44`, `:48` |

---

## 4. `cloud/review-r1-b` — 37 bulgu + 2 yan not (rapor main'de, `38c72bdb`, blob `4f179506`)

Bütün kapanış commit'leri `origin/main`'in atası (`git merge-base --is-ancestor`). 24.09'dan beri konularında "Revert" geçen commit yok.

| # | Özgün → norm | Durum | Kapanış kanıtı |
|---|---|---|---|
| RT1 | HIGH → MAJOR | KAPANDI | Düğüm satın alma `0db9f9ff` (`Rounds/RoundUpgradeShop.cs:650-678`). Special bağı `33bf0c0f` (`Weapons/SpecialLinks.cs:130`). Varsayılan AÇIK `8c146b06` (`P/Core/Resources/PrototypeConfig.asset:214` `RunTreeNodeCards: 1`) |
| RT2 | HIGH → MAJOR | KAPANDI (çağıran kapısı) | `0db9f9ff`: `PurchaseNode`, `IsOfferable` (= `Available`) olmadan harcamaz (`RoundUpgradeShop.cs:654`). `MarkOwned`'ın kendisi hâlâ korumasız (`RunTreeState.cs:647-656`). Tek üretim çağıranı korumalı; diğer çağıran capture ajanı (`UiCaptureAgent.cs:2373`, `:2379`) |
| RT3 | HIGH → MAJOR | KAPANDI (varsayılan yol) | `0db9f9ff`: düğüm teklifinde boş cephe, Ocak doluysa `PoolExhausted`, değilse `Ready` (`RoundUpgradeShop.cs:1032-1055`). Eski kart yolu (`:1015`) yalnız düğüm kartları kapalıyken çalışır; bu da yalnız test ve F1 |
| RT4 | HIGH → MAJOR | KAPANDI | `60a1df94`; `Rounds/UpgradeOfferPolicy.cs:282-329` (`keepSlot`) |
| W1 | MEDIUM → MAJOR | KAPANDI | `63e705dc` |
| W2 | MEDIUM → MAJOR | KAPANDI | `e0f9f9a7`; `ClassicWeaponRuntime.cs:194-195` |
| RT5 | MEDIUM → MAJOR | KAPANDI | `60a1df94`; `RunTreeSnapshot.cs:231` |
| RT6 | MEDIUM → MAJOR | KAPANDI | `2a87fab7`; `RunTreeState.cs:361-367` (`CanPin`); DEC-110 (`PLAN00:182`) |
| RT7 | MEDIUM → MAJOR | KAPANDI | `b9fd8769` |
| P1 | MEDIUM → MAJOR | KAPANDI | `dd835c0f`; `UI/Runtime/Presentation/ResultsViewModel.cs:185`, `:302-310` (`WeaponIdMigration.TryGetRuntimeId`) |
| A1, A2 | MEDIUM → MAJOR | KAPANDI | `f8d32e80` |
| AI1 | MEDIUM → MAJOR | KAPANDI | `6f40128a` + `26be3e5c` + `22689aab` |
| P2 | LOW → MINOR | KAPANDI | `41143861` + UI `3cc88f99` |
| P3–P6 | LOW → MINOR | KAPANDI | `41143861` |
| P7 | LOW → MINOR | KAPANDI (karar) | DEC-111 (`PLAN00:183`), `512eab2d` |
| RT8 | LOW → MINOR | KAPANDI | `512eab2d`; `RunTreeState.cs:199-200` |
| RT9 | LOW → MINOR | KAPANDI | `8c7c22c1` + `7995d7b3`; `UI/Runtime/Screens/UpgradeTree.cs:936-940`, `:1549-1551` |
| RT10 | LOW → MINOR | KAPANDI | `9782f5e5`; `RunTreeState.cs:670` |
| W3 / W4 / W7 | LOW → MINOR | KAPANDI | `4c4fc213` / `528fac3d` / `210f5bc5` |
| W5 | LOW → MINOR | KAPANDI | `ac98682d` + `7a806399` |
| W6 | LOW → MINOR | KAPANDI | `1aa69b6c` + `53d13ec6` |
| AI2, AI3, AI6 | LOW → MINOR | KAPANDI | `c3b9cc52` |
| AI4, AI5, AI7 | LOW → MINOR | KAPANDI | `26be3e5c` |
| A3, A4, A5 | LOW → MINOR | KAPANDI | `f8d32e80` |
| A6 | LOW → MINOR | KAPANDI | `23d026cd` (`f8d32e80` gövdesi: "A6 already on main") |
| Yan not 1 | — | KAPANDI | `EnemyCombatEvents.Raised` sıfırlaması: `Enemies/EnemyCombatEvents.cs:129-132` (`26be3e5c`, "combat bus reset") |
| Yan not 2 | — | KAPANDI | Shock → Electrocuted görseli: `Enemies/ZombieSpecialReactions.cs:85-91` |

**Sonuç:** R1-B'de açık bulgu yok. `PSP:366`'daki "ağaç/Hearth/Purse/reroll-pin HIGH bulguları" ifadesi bayat.

---

## 5. Denge denetimleri

### 5.1 `cloud/balance-audit-r1` (run1; main'de `ad42fc6e`, aynı bloblar)

| # | Özgün → norm | Konu | Durum | Kanıt |
|---|---|---|---|---|
| 1 | 🔴 → MAJOR | rl_1 zırh aritmetiği | KAPANDI (karar), belgede kalıntı | DEC-099 (7) "rl_1 doğrudan isabeti zırhı sayar (değişiklik yok)" (`PLAN00:171`). `DIR-069:123`'teki "1 doğrudan (blast tek başına 160 ≥ 155,6)" iddiası duruyor → BAL-DOC-069 |
| 2 | 🔴 → MAJOR | Kafa ve zırh çarpanları okunmuyor | KAPANDI | `4e448f41`: `Damage/RegionalDamageBody.cs:148`, `:173` ← `DamageHitContext.cs:56-58`. sr_1 ArmorBreak → ReloadSpeed: DEC-099 (4) |
| 3 | 🔴 → MAJOR | Baskın silahlar (ar_1, sg_1, lmg_1) | KAPANDI (karar) | DEC-099 (1)(2)(3) |
| 4 | 🟠 → MAJOR | Namekuji çok güçlü | KAPANDI | DEC-099 (5) |
| 5 | 🟠 → MAJOR | Kül temposu | KAPANDI | DEC-098 (3) "Kül temposu (denetim #5)" (`PLAN00:170`) |
| 6 | 🟠 → MAJOR | Doruk zamanlaması / pin | KAPANDI | DEC-098 (4) "(denetim #6)" |
| 7 | 🟠 → MAJOR | sr_1 hat delme iddiası | KAPANDI | DEC-099 (4) %90; DEC-105 hasar 80 (`PLAN00:177`); W5 `ac98682d` |
| 8 | 🟡 → MINOR | ar_1 ile smg_2'nin öldürme profili aynı | **AÇIK (ertelendi)** | DEC-099: "smg_2 önerisi … ilk oyun testine bırakıldı" (`PLAN00:171`) → S-02, BAL-SMG2 |
| 9 | 🟡 → MINOR | Erken eğri iddiası | **AÇIK (belge)** | `DIR-069:45` "1–5 mermi" duruyor → BAL-DOC-069 |
| 10 | 🟡 → MINOR | sg_1 tek atış menzili | **AÇIK (belge)** | Geometri DEC-099 (2) ile değişti (0,25 / 0,55), ama `DIR-069:118` "≥ 8 @ ≤ 6 m" duruyor ve yeniden ölçülmedi → BAL-DOC-069 |
| 11 | 🟡 → MINOR | Canlı veri v1 | KAPANDI | `4e448f41` (WP-069 B2 varlıkları) |
| 12 | 🟡 → MINOR | DEC numarası çakışması | **AÇIK (belge)** | `DIR-069:5` ve `:1108` hâlâ "DEC-096 (Arsenal denge v3)" diyor; gerçek numara DEC-097 (`PLAN00:169`) → S-02, BAL-DEC |
| 13 | ⚪ → MINOR | Küçük aritmetik kaymaları | **KISMEN (belge)** | WP-069 satırları duruyor (`DIR-069:120`, `:123`) → BAL-DOC-069. WP-077: `DIR-077:46` hâlâ `50 + 30·n` (n'nin 0 tabanlı olduğu yazılmamış) → S-04, BAL-DOC-077. Diğer iki WP-077 satırı burada yeniden kontrol edilmedi |
| 14 | ✅ | XP eğrisi L10/L20 hedeflerini tutuyor | Bulgu değil | DEC-098 (2) "denetim #14 … değişmez" |

### 5.2 `cloud/balance-audit-r1-run2` (main'de `-run2` adıyla, `4854a756`)

DEC-099'un bazı değerleri run2 önerileriyle birebir aynı:
- sg_1 halkaları 0,25 / 0,55 (run2 #5);
- Namekuji 12 s ve Guard R ≤ 10 (run2 #4).

Bu yüzden run2'nin main'de bulunması karar kökeni açısından değerli.

| # | Özgün → norm | Konu | Durum | Kanıt |
|---|---|---|---|---|
| 1 | CRIT → BLOCKER | Sonlu yedek cephanenin ikmal kuralı yok | KAPANDI | DEC-097 (2) (`PLAN00:169`); `4e448f41`: `Weapons/ClassicWeaponRuntime.cs:334-350` `RefillReserveForRound`, `WeaponController.cs:261` |
| 2 | HIGH → MAJOR | İkinci silah birinciyi eziyor | KAPANDI (karar) | DEC-099 (1)(2)(3); smg_2 kalemi run1 #8 ile birlikte açık |
| 3 | HIGH → MAJOR | rl_1 zırh aritmetiği | KAPANDI (karar), belgede kalıntı | DEC-099 (7) → BAL-DOC-069 |
| 4 | HIGH → MAJOR | Namekuji tur bitiriyor | KAPANDI | DEC-099 (5) |
| 5 | HIGH → MAJOR | sg_1 halka geometrisi | KAPANDI | DEC-099 (2) (önerilen 0,25 / 0,55) |
| 6 | MED → MAJOR | XP/s oranı belgenin kendi çapasıyla çelişiyor | **KISMEN → MINOR** | DEC-098 eğriyi korudu (`PLAN00:170`). Ölçüm planı ve aracı var (`56a3da17`, `tools/balance/xp_rate.py:2`: "the DEC-098 open item"), ama gerçek telemetri ölçümü yapılmadı. Kullanıcı telemetrisi §4.14 gereği yalnız C-00 izniyle okunur → S-04, BAL-XP |
| 7 | MED → MAJOR | Meta halka bütçeleri | KAPANDI | DEC-098 (3) |
| 8 | MED → MAJOR | Ocak temposu / pin doruk kapısını atlıyor | KAPANDI (karar) | DEC-098 (4); DEC-107 (`PLAN00:179`) |
| 9 | MED → MAJOR | §1.3 eğri iddiaları | **AÇIK (belge) → MINOR** | run1 #9 ile aynı → BAL-DOC-069 |
| 10 | LOW → MINOR | Belgelerdeki aritmetik kaymalar | **AÇIK (belge)** | run1 #13 ile aynı → BAL-DOC-069 |
| 11 | LOW → MINOR | Canlı veri M3 | KAPANDI | `4e448f41` |

---

## 6. `cloud/content-pack-r1` (içerik teslimi; main'de gelişti)

Bu dal bulgu raporu değil; `R1-CONTENT-PACK.md` §0'daki Q1–Q14 belirsizlik listesini taşıyor. Main sürümünde durum:

| Q | Durum | Kanıt |
|---|---|---|
| Q1, Q4, Q5, Q6 | KAPANDI | Main belgesinde "Resolved" işareti: `planning/content/R1-CONTENT-PACK.md:43`, `:46`, `:47`, `:48` |
| Q2 (TR glif kapsamı) | KAPANDI | DEC-109 (`PLAN00:181`) |
| Q3 (TR büyük harf) | KAPANDI | `UI/Runtime/Kit/UiStrings.cs:134-147`: tr'de `ToUpper(tr-TR)` |
| Q9 (ondalık ayırıcı) | KAPANDI | `66eb3318` (M-65) |
| **Q7 (WP-069 sonrası bayat ipuçları)** | **AÇIK → MINOR** | `UI/Runtime/Flow/UiLoadingTips.cs:59` "Every classic weapon is automatic." ve `:68` "A Special Weapon has no ammo and no reload…" hâlâ `UiLoadingTips.All` içinde. Metin `UI/Resources/Strings/r1-strings.json:2466-2469`, `:2518-2521`'de. Çelişki: DEC-097 (3) Special'a cephane + süre tavanı verdi (Raiju 3/9, Namekuji 8/16; `PLAN00:169`); ar_2 seri atışlı, sg_1 tekli, sr_1 sürgülü → S-09, CP-Q7 |
| Q8, Q10–Q14 | Bilgi | Eylem gerektirmiyor |

## 7. `cloud/portfolio-docs`

Bulgu yok. Main'deki sürüm R1 ve R2 durumuna yenilendi (`b99c3a54`, `68368cd5`, `66f0cd9f`). Dalın içeriği tarihsel.

---

## 8. Öneriler

### 8.1 Main'e alınması önerilen dosyalar

| Yol | Kaynak | Neden |
|---|---|---|
| `planning/reviews/WINDOWS-PORTABILITY-AUDIT.md` | `origin/cloud/windows-audit` `5abbf8d33e`, blob `bcb0961d0eaf0e8075d6b4e3c4cb8e93a9e5c00a` | Win64 release'in tek risk kaydı. 2 BLOCKER ve 12 MAJOR'ın kaynağı; WP-066 §4 aynı açıkları özetle anıyor. R2 planı win64 release içeriyor (`R2-PLAN.md:45`, `:77`). Yol main'de yok, çakışma olmaz. `38c72bdb`'deki R1-B örneği gibi **değiştirilmeden** alınmalı; güncel durum bu B-08 belgesindedir. |

- Başka dosya alınmasına gerek yok. Diğer altı dalın bütün dosyaları blob düzeyinde main'de (§1.1).
- **C-00 belge düzeltmesi:**
  - `PSP:366` ve `PSP:438` bayat (review-r1-b `38c72bdb` ile, run2 `4854a756` ile main'de).
  - `docs/c00/C00B-GOREVLER.md:20` B-08 tanımı da run2'yi alınmamış varsayıyor.
- Yöntem notu: dosya yeni olduğu için tek yol açıkça stage edilir. `git add -A` kullanılmaz.

### 8.2 Arşivlenebilecek dallar (öneri; silme kullanıcı/C-00 işi)

| Dal | Öneri | Gerekçe |
|---|---|---|
| `cloud/review-r1-a` (uzak + yerel `refs/heads/cloud/review-r1-a`) | Arşivlenebilir | Patch-eşdeğeri `a7f25570`, blob aynı; 19 bulgunun hepsi kodda kapalı |
| `cloud/review-r1-b` | Arşivlenebilir | Blob `38c72bdb` ile main'de; 37 bulgu kapalı |
| `cloud/balance-audit-r1` | Arşivlenebilir | Patch-eşdeğeri `ad42fc6e` |
| `cloud/balance-audit-r1-run2` | Arşivlenebilir | Bloblar `4854a756` ile main'de (`-run2` adları) |
| `cloud/content-pack-r1` | Arşivlenebilir | Patch-eşdeğeri `02aab39b`; main sürümü ileride |
| `cloud/portfolio-docs` | Arşivlenebilir | Patch-eşdeğeri `a99daea2`; main sürümü ileride |
| `cloud/windows-audit` | **Tut** | Dosya main'e alınana kadar tek kopya burada. Alındıktan sonra arşivlenebilir. |

- Silmeden önce her tipe salt ekleme yapan bir arşiv etiketi önerilir (ör. `archive/cloud/<ad>` → tip SHA). Bu geri dönüşsüz değildir.
- Uzak dal silme (`push --delete`) ve yerel `branch -d` kullanıcı/C-00 onayıyla yapılır. Bu ajan hiçbirini çalıştırmadı.

### 8.3 Önerilen iş paketleri ve sıra

Paket numaralarını C-00 verir; burada yalnız kapsam önerilir. Önce **Mac'te yapılabilenler**, sonra Windows host'una bağlı kanıtlar.

1. **Windows profil dayanıklılığı** (S-04 + S-10): W-B1 kod/test kısmı, W-H1, W-M1 yardımcısı (S-09/S-02 benimser), W-M2, W-L7.
   - Mac'te: yeniden deneme, paylaşımlı okuma, kilit yeniden denemesi, `ReplaceAttemptFault` testleri, platform anahtarı.
   - Windows'ta: probe A–D + L1/L2 + `taskkill`.
2. **Windows smoke ve guard hattı** (S-01 + S-10): W-B2, W-L5, W-L6, W-M6.
   - Mac'te hemen: shader hatası kapısı, zip/SHA, smoke kiti.
   - PowerShell betikleri host gelene dek "yazıldı, koşturulmadı".
3. **Platform varsayılanları ve kültür** (S-01; C-00 kararı H4/H5): W-H4, W-H5 (küresel `InvariantCulture` + tr-TR EditMode testi; kalıntılar S-09 Pause, S-04 UpgradeCard), W-M5, W-L8. Tamamı Mac'te yapılabilir.
4. **Windows ekran ve UI** (S-09 + S-07): W-H2, W-H3, W-L1, W-L2, W-L3. Kod Mac'te, doğrulama Windows'ta.
5. **Girdi Windows kalibrasyonu** (S-02 + S-09): W-M3, W-M4 (ADR-024-2 uyumlu: S-02 Core/Input yüzeyi, S-09 tüketir), W-M1'in girdi tarafı.
6. **Ses cihaz değişimi** (S-08): W-M7 + sahte olaylı test.
7. **Küçük temizlik:**
   - S-02: BAL-DOC-069, BAL-DEC, BAL-SMG2 (ilk oyun testinde), R1-A #2 uçtan uca testi;
   - S-03: R1-A #11 regresyon testi;
   - S-09: CP-Q7 ipuçları;
   - S-04: BAL-XP (C-00 izniyle telemetri ölçümü), BAL-DOC-077.

### 8.4 C-00'ın vereceği kararlar

1. Windows makinesi/CI. Kullanıcı 🔴; `WP-066:112`. İki BLOCKER'ın kanıtı buna bağlı.
2. R2 win64 artefaktının etiketi ("derlendi, koşturulmadı") ve `R2-NOTES-draft.md:93` platform satırı.
3. W-H4 ilk açılış ekran modu.
4. W-M2 `forceSingleInstance`.
5. W-H5 kültür politikası.
6. W-L4 ürün adı (kullanıcı 🔴, WP-066 🔴 1).

---

## 9. Kanıt komutları (yeniden üretim)

```
git -C "<repo>" rev-parse origin/main                                        # 18f3d01e…
git -C "<repo>" for-each-ref refs/remotes/origin/cloud/                      # 7 dal, tipler
git -C "<repo>" rev-list --left-right --count origin/main...origin/cloud/<d>
git -C "<repo>" cherry -v origin/main origin/cloud/<d>
git -C "<repo>" show origin/cloud/<d> | git patch-id --stable                # main commit'iyle eşleşme
git -C "<repo>" rev-parse origin/cloud/<d>:<yol>                             # blob; main ağacında ve --find-object ile geçmişte arandı
git -C "<repo>" merge-base --is-ancestor <kapanış-sha> origin/main           # her kapanış commit'i için
git -C "<repo>" rev-list --count cf34180..origin/main -- <dosya>             # windows-audit dosyaları değişti mi
git -C "<repo>" grep -n '<desen>' origin/main -- <yol>                       # kod kanıtları (satır numaraları main'den)
```
