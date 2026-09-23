# WP-066 — Release Governance & Distribution (teknik taslak, WP-066T)

**Durum:** Taslak (S-01, 2026-09-23, taban `main @ 1df0bf0`). Kod/ayar değişikliği yok; dış eylem yok (push, mağaza, ücretli hesap, imzalama, yükleme yapılmadı). Bu belge karar vermez: kararlaştırılmamış her başlık §6'da 🔴 olarak durur ve varsayım sayılmaz (planning/00 "Kararlaştırılması sonraya bırakılan alanlar": hedef işletim sistemi, mağaza ve dağıtım modeli; DEC-080 Windows ertelendi — DEC-089 ile kaldırıldı, 2026-09-23; DEC-086 tavanı dağıtım kararıyla yeniden belirlenir).

## 1. Mevcut macOS release zinciri envanteri

### 1.1 Araçlar (`tools/`, ayrıntı `tools/README.md`)

| Adım | Araç | Bugünkü davranış |
|---|---|---|
| Build | `tools/build.sh {dev\|release} [--smoke] [--target mac\|win64]` | slot alır → `DdBuild.Build` (`-ddMode`, `BuildOptions.None` release / `Development` dev) → `footprint.json\|md` (tavan 550 MB, yalnız uyarı) → `report.json`; çıkış 0/10/20/30/64/75 |
| Smoke | `tools/smoke.sh <app> <out> --mode …` | e1 ana menü + e4 `-ddDirectToRun` kareleri, istisna taraması; gerçek kullanıcı verisi yedek→geri yükle→SHA doğrula (`userdata.verified`) |
| Ayak izi | `tools/build-report.sh` | Unity Build Report kategorileri/top-N/klasör; son release **545,7 MB** (WP-065, ≤ 550) |
| Entegrasyon | `tools/integrate.sh` + `post-integrate.sh` | C-00 kapısı, açıklamalı etiket, yedek (bundle + LFS), temizlik dry-run |
| RC kabul/regresyon | `tools/qa/rc.sh [out] [--rounds ≥12] [--seed] [--perf-runs] [--window] [--diagnostic-continue]` (WP-061) | tam EditMode + PlayMode (`test.sh`) → `build.sh release --smoke` → m3-regression (build/spawnmix/bot/perf/readability, Ultra kanıtı zorunlu) → soak (tam round) → ekonomi kabulü + pencere modları (dev app) → log taraması → `rc_aggregate.py` hükmü; varsayılan fail-fast, `--diagnostic-continue` bağımsız kanıt aşamalarını sürdürür (hüküm yine FAIL). Eksik olan yalnız etiket/sürüm alanı adımı (§2) |

### 1.2 Sürüm damgası

- `BuildStamp` (Editor) build öncesi `Assets/_Prototype/Core/Resources/build-info.json`'a `git describe --tags --always --dirty`, commit, dal, UTC, mod, Unity sürümünü yazar; build sonrası yer tutucuya (`stamped=false`) döner. Çalışma zamanı: `BuildInfo.Read().Label` / `.Describe()`; damgasız → `dev-unstamped`.
- Not: `describe` en yakın etiketi seçer; bugün bu bir `wp-*` paket etiketidir. `v*` etiketleri gelince release etiketinin damgaya girmesi için `--match 'v*'` gerekir (§2, küçük kod değişikliği — bu pakette yapılmadı).
- Player Settings `bundleVersion: 1.0`, `buildNumber Standalone: 0`; `Application.version` = "1.0" ve telemetri belgesinin `build` alanı bunu yazar (git damgasını değil).

### 1.3 Player Settings — mevcut kimlik değerleri (`ProjectSettings/ProjectSettings.asset`)

| Alan | Değer | Not |
|---|---|---|
| companyName | `Deaths Demolition` | persistentDataPath klasörünü belirler |
| productName | `Deaths Demolition - Playable Concept` | .app/ikili adı; "Playable Concept" ürün adı değildir |
| applicationIdentifier | `{}` (ayarlanmamış) | Unity türetir: `com.Deaths-Demolition.Deaths-Demolition---Playable-Concept` (`tools/dd-env.sh` `DD_BUNDLE_ID`) |
| bundleVersion / buildNumber | `1.0` / `0` | CFBundleShortVersionString / CFBundleVersion kaynağı |
| macOSTargetOSVersion | `10.13.0` | Unity 2022.3 alt sınırı; gerçek hedef ayrı karar |
| macAppStoreCategory | `public.app-category.games` | |
| useMacAppStoreValidation | `0` | |
| Pencere | 1920×1080, `fullscreenMode: 3` (pencereli), yeniden boyutlanabilir, Retina açık, `runInBackground: 1` | |
| Mimari | izlenen ayarda yok (`platformArchitecture: {}`; seçim `Library/EditorUserBuildSettings`) | diskte build yok → `lipo -archs` ile ilk RC'de doğrulanacak |
| Splash | `m_ShowUnitySplashScreen: 1`, logo `1` | lisans katmanına bağlı |
| Unity servisleri | Analytics/CrashReporting/Performance/Ads/IAP `m_Enabled: 0` (`UnityConnectSettings.asset`) | `submitAnalytics: 1` (donanım istatistikleri alanı) — anlamı ve etkisi RC'de doğrulanacak |
| İmza | Unity varsayılanı (ad-hoc/imzasız); Developer ID yok | §3 |

### 1.4 Kullanıcı verisi yolları (macOS, release)

| Dosya | Yol | Sahibi |
|---|---|---|
| Profil | `~/Library/Application Support/Deaths Demolition/Deaths Demolition - Playable Concept/prototype-profile.json` (+ `.bak`, `.corrupt.N.json`, `.writing`, `.lock`) | `ProfileStore` (WP-009H) |
| UI ayarları | aynı klasör `ui-settings.json` | `UiSettings` (WP-022B) |
| Girdi | aynı klasör `input-settings.json`, `input-overrides.json` | WP-024 |
| Telemetri | aynı klasör `telemetry/run-<utc>.json` | `RunTelemetry` (WP-045) |
| Crash log | aynı klasör `Logs/dd-crash-<utc>.txt` (en yeni 10) | `CrashLogReporter` (WP-025) |
| PlayerPrefs | `~/Library/Preferences/<bundle id>.plist` (izin listesi: `UiSettings` eski anahtar, `PrototypeBootFlags.DirectToRun`) | |

Tüm yollar `companyName`/`productName`/bundle id'den türer: **bunlardan birini değiştirmek mevcut oyuncu profilini "kaybettirir"** (yeni boş klasör). Ad/kimlik kararı (🔴 1) bir göç adımıyla birlikte verilmelidir.

### 1.5 Telemetri ve crash log (yerel)

- `RunTelemetry` (`dd-run-telemetry-v1`): run sonunda tek JSON (≤ ~200 KB); hiçbir ağ çağrısı yok (runtime'da `UnityWebRequest`/`System.Net` kullanımı yok). **Budama yok** — her run bir dosya ekler (crash log'da 10 dosya sınırı var).
- `FeelTelemetry`: bellek içi iz halkası (2048), dosya yazmaz. `AudioPlaybackTelemetry`: yalnız `DEVELOPMENT_BUILD || UNITY_EDITOR`.
- `CrashLogReporter`: yalnız Player; ev dizini ve hesap adı maskeli; `-ddNoCrashLog` kapatır; yükleme yok.
- Dev-only bayraklar (`-ddRtProbe`, `-ddProfilePath`) release'te etkisiz; `-ddDirectToRun`, `-ddTelemetry 0`, `-ddTelemetryDir` release'te de okunur.

### 1.6 Lisans / atıf dosyaları

| Kaynak | Dosya / durum |
|---|---|
| LiberationSans (TextMesh Pro varsayılan fontu) | `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt` — SIL OFL 1.1; dağıtımda lisans metni paketle birlikte verilmeli |
| EmojiOne sprite'ları (TMP örnek) | `Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt` — build'e girip girmediği doğrulanmalı; girmiyorsa atıf gerekmez |
| Unity paketleri | `Packages/manifest.json` (URP 14.0.10, Input System 1.7.0, AI Navigation 1.1.5, ProBuilder 5.2.2, TMP 3.0.6, Cinemachine 2.9.5, Test Framework 1.1.33) — Unity Companion License; ayrı atıf dosyası yok |
| AI-üretim varlıklar | Higgsfield (image/Tripo 3B/Seed Audio vb.) ve yerel Blender/prosedürel üretim; kayıtlar `asset-production/AP-*` manifestlerinde (repo dışı, WP-067A yedeği BLOCKED). **Depoda birleşik beyan/atıf dosyası yok**; sağlayıcı lisans koşulları (ticari kullanım, atıf) doğrulanmadı |
| Oyun lisansı / EULA / kredi ekranı | yok |

## 2. Sürüm / etiket politikası (taslak)

- Paket etiketleri (`wp-*`) aynen sürer; release etiketleri ayrı ad alanı: `v1.0-rc.N` (N = 1, 2, …) → onaylı RC aynı commit'e `v1.0` olarak yeniden etiketlenir (yeni build yok; aynı ikili).
- Etiketler açıklamalı, eğik çizgisiz (integrate.sh kapısıyla uyumlu), yalnız C-00 atar; silinmez/taşınmaz — reddedilen RC'den sonra N artar.
- RC ölçütleri (öneri): main temiz; `tools/qa/rc.sh` hükmü PASS (tam test süitleri, release build+smoke `userdata.verified`, m3-regression/perf DEC-088, soak, Ultra, pencere modları, log taraması — bilinen FAIL listesi boş ya da kullanıcı onaylı); ayak izi ≤ o anki tavan; §1.6 lisans paketi hazır. Etiket, PASS veren `rc.sh` koşusunun commit'ine atanır.
- Sürüm eşlemesi: `bundleVersion` = `1.0` (etiketin semver çekirdeği), `buildNumber` = RC sayacı ya da build sayısı (tekdüze artan; mağazalar ister). Bunların etiketten otomatik yazılması ve `BuildStamp`'ın `--match 'v*'` kullanması küçük bir S-01 kod paketi (WP-066 uygulaması, onay sonrası).
- RC artefaktı: `.app` + `build-info.json` + `footprint.json` + `smoke-report.json` + SHA-256 listesi; yeri `~/Desktop/KADRO/context/projects/deaths-demolition/releases/<etiket>/` (öneri; repo dışı, LFS'e girmez).

## 3. Dağıtım paketleme seçenekleri

| Seçenek | Oyuncu deneyimi (macOS) | Gerekenler | Maliyet (doğrulanmalı) | Kullanıcı kararı |
|---|---|---|---|---|
| A. İmzasız zip (ad-hoc) | Gatekeeper engeller. macOS 15+'ta "sağ tık → Aç" artık yetmiyor: Sistem Ayarları → Gizlilik ve Güvenlik → "Yine de Aç" gerekir (macOS 14 ve öncesi sağ tık → Aç). Karantina bitli indirmede App Translocation | `ditto -c -k --keepParent` zip, SHA-256, okuma notu | 0 | Hedef kitlenin bu sürtünmeyi kabul etmesi (dar test grubu için uygun) |
| B. Developer ID imza + notarizasyon | Uyarısız açılır (stapled bilet çevrimdışı da çalışır) | Apple Developer Program üyeliği, Developer ID Application sertifikası, hardened runtime + Mono için entitlement seti, `codesign` → `notarytool submit` → `stapler staple`; kimlik bilgileri keychain'de, repoya asla | ~99 USD/yıl | Hesap açma/ücret (kullanıcı), yasal ad/ekip kimliği; sertifika yönetimi kimde |
| C. itch.io + butler | İndirme A veya B gibi davranır (imza itch'ten bağımsız); itch app ile güncelleme | itch hesabı, oyun sayfası, `butler push` (API anahtarı keychain'de), kanal adı (`mac`) | Hesap ücretsiz; gelir payı satıcı seçimi (varsayılan %10) — doğrulanmalı | Hesap, fiyat modeli (ücretsiz/ücretli/PWYW), sayfa içeriği, B ile birleştirme |
| D. Steam | Steam istemcisi başlatır; macOS için imza/notarizasyon önerilir | Steamworks ortaklığı (vergi/banka formları), uygulama ücreti, depot/SteamPipe, mağaza varlıkları, inceleme süreci; Steamworks SDK entegrasyonu = yeni paket (başarımlar/bulut kayıt isteniyorsa) | 100 USD/uygulama (gelir eşiğinde iade), gelir payı %30 kademeli — doğrulanmalı | Hesap/ücret/vergi, SDK paketi (paket değişikliği kullanıcı kararı), başarım/bulut kayıt kapsamı, çıkış tarihi |

Notlar: (i) A–D birbirini dışlamaz (ör. B + C). (ii) Mac App Store bu tabloda yok: sandbox, profil yolu değişimi ve inceleme ayrı bir kapsam — istenirse ayrı karar. (iii) Her seçenekte ürün adı/bundle id (🔴 1) önce gelir; mağazaya giren kimlik sonradan değişmez.

## 4. Windows hattı (DEC-089: hedef macOS + Windows; WP-066W)

- **Karar:** kullanıcı 2026-09-23 — hedef platformlar macOS ve Windows (DEC-089; DEC-080 ertelemesi kaldırıldı). Kanal, imza, Windows referans donanımı/perf bütçesi ve IL2CPP ayrı kararlar (§6).
- **Unity modülü:** 2022.3.22f1 kurulumunda `PlaybackEngines/WindowsStandaloneSupport` zaten var (yalnız Mono: win32/win64 dev + non-dev). Yeni modül/paket kurulmadı, Unity sürümü değişmedi. IL2CPP Windows, Windows ana makine + ek modül ister (🔴).
- **Build (WP-066W):** `tools/build.sh {dev|release} --target win64` → `<out>/DeathsDemolition-win64/DeathsDemolition.exe` + `DeathsDemolition_Data/` + `build.log` / `build-report.json` (`"target": "win64"`) / `report.json` / `footprint.json|md` (etiket `release-win64`). Editor tarafı `DdBuild -ddTarget mac|win64` (varsayılan mac; kurulu olmayan hedefte açık hata). `--smoke` Windows'ta 64 ile reddedilir. **macOS hattı değişmedi:** varsayılan komut satırı ve `report.json` şeması birebir aynı (sahte Unity ile argv/rapor karşılaştırması, `wp-066w/evidence/`).
- **İlk Windows release build:** 2026-09-23, taban main `6e13689` (+ bu paketin commit'lenmemiş değişiklikleri → damga `…-dirty`), temiz Library: `build.sh release --target win64` exit 0, 562 s, `error CS` 0; `DeathsDemolition.exe` (PE32+ x86-64 GUI) + `DeathsDemolition_Data/` (482 MB) + `UnityPlayer.dll` 30 MB + `MonoBleedingEdge` 8,8 MB + `UnityCrashHandler64.exe`; shader'lar d3d11. Ayak izi **522,1 MB** (log 521,8; Textures 284,7 / Meshes 71,4 / Sounds 9,3 MB) ≤ 550 — aynı içerikte macOS release 545,7 MB. `DeathsDemolition_BurstDebugInformation_DoNotShip/` (28 KB) dağıtım paketine girmemeli (paketleme adımı dışlar). Kanıt `wp-066w/build-win64/` (build.log, footprint.json|md, report.json).
- **Çalıştırma/smoke: koşturulmadı.** Bu makinede Windows yok; exe hiç açılmadı, e1/e4 kareleri, kullanıcı verisi koruması, perf ve girdi doğrulanmadı. "Hazır" sayılmaz.
- **Windows'a özgü ayar farkları (mevcut ProjectSettings, değiştirilmedi):** grafik API listesi boş (`m_BuildTargetGraphicsAPIs: []`) → Unity 2022.3 otomatik = **Direct3D 11**; `useFlipModelSwapchain: 1`, `forceSingleInstance: 0`, `visibleInBackground: 1` (yalnız Windows), `fullscreenMode: 3` (pencereli), `runInBackground: 1`. Mimari x86_64, betik arka ucu Mono. Platforma özgü kod (`UNITY_STANDALONE_*`, `RuntimePlatform`) yok.
- **Kullanıcı verisi yolları (Windows):** `%USERPROFILE%\AppData\LocalLow\Deaths Demolition\Deaths Demolition - Playable Concept\` altında `prototype-profile.json` (+ yan dosyalar), `ui-settings.json`, `input-settings.json`, `input-overrides.json`, `telemetry\run-<utc>.json`, `Logs\dd-crash-<utc>.txt`; PlayerPrefs `HKCU\Software\Deaths Demolition\Deaths Demolition - Playable Concept`. Profil kilidi (`.lock`, fcntl lease, WP-009H) ve atomik replace Windows dosya sisteminde doğrulanmadı. Crash log maskesi ev dizinini `\` biçimiyle de değiştirir (Windows'ta koşturulmadı).
- **Araç açıkları:** `smoke.sh` macOS'a özgü (`defaults`, plist, `~/Library`); Windows smoke ancak Windows makinede/CI'da, ayrı betikle. `tools/qa/rc.sh` yalnız macOS.
- **Grafik/girdi:** URP D3D11 shader varyantları ve ayak izi ayrı ölçüldü (yukarıda); gamepad paritesi (WP-024/059, `InputSourceRegistry`) XInput'ta doğrulanmadı.
- **İmza:** Authenticode sertifikası yoksa SmartScreen uyarısı; sertifika ücretli (OV/EV) → 🔴. Steam/itch Windows depotları §3 kanal kararına bağlı.

## 5. Gizlilik / telemetri beyan taslağı

> **Deaths Demolition ne kaydeder?** Oyun, internete hiçbir veri göndermez. Her oyun turu bittiğinde, dengeleme çalışmaları için bilgisayarınızda yerel bir özet dosyası yazar: `~/Library/Application Support/Deaths Demolition/<ürün>/telemetry/`. Bu dosya yalnız tur içi olayları içerir — round süreleri, kazanılan/harcanan altın, öldürme sayıları (düşman türüne göre), alınan hasar, satın alınan geliştirme kartları, silah, düşmanı öldürme süreleri, oyunun sürümü ve UTC zamanı. Adınız, hesap bilgileriniz, profil seviyeniz, başarımlarınız, donanım kimliğiniz veya konumunuz kaydedilmez. Kayıt varsayılan olarak **açıktır**; Ayarlar → Telemetri ile kapatabilir, klasörü istediğiniz zaman silebilirsiniz. Oyun hata verdiğinde aynı klasörün `Logs/` altına ev dizini ve kullanıcı adı maskelenmiş bir hata günlüğü yazar (en yeni 10 dosya); bu da gönderilmez.

Beyanın doğru kalması için açık uçlar: (a) varsayılan açık/kapalı (🔴 6); (b) telemetri klasörü budanmıyor — sınır/yaş kuralı eklenmesi önerilir (küçük S-01 kod paketi); (c) Unity motor düzeyi veri (`submitAnalytics`, lisans katmanının motor istatistikleri) RC'de doğrulanıp beyana yansıtılmalı; (d) bir mağaza SDK'sı (Steam) eklenirse beyan güncellenir; (e) beyan metnini oyunda nereye koyacağımız (Ayarlar/kredi ekranı/okuma notu) UI kararı (S-09).

## 6. 🔴 Kullanıcı kararları (hiçbiri varsayılmadı)

1. 🔴 **Ürün adı ve kimlik:** nihai `productName`, `companyName` (yasal/yayıncı adı), bundle id (ör. ters DNS alan adı) — değişim profil yolunu değiştirir; göç adımı onayı ile birlikte.
2. ✅ **Hedef platform(lar): KAPANDI** — macOS + Windows (kullanıcı 2026-09-23, DEC-089). Açık kalan Windows alt kararları: Windows referans donanımı ve perf bütçesi (DEC-088 yalnız M1 Pro), Authenticode imza, IL2CPP (Windows ana makine + modül), Windows çalıştırma/smoke için makine/CI.
3. 🔴 **Dağıtım kanalı:** §3 A / B / C / D ya da birleşimi; ilk kanal hangi kitle (kapalı test grubu mu, halka açık mı).
4. 🔴 **Ücretli hesaplar ve sertifikalar:** Apple Developer Program, Steamworks, itch; hesap sahibi, yasal ad, vergi/banka bilgileri — hepsi kullanıcının kendisi tarafından.
5. 🔴 **Fiyat/iş modeli:** ücretsiz, ücretli, PWYW; itch gelir payı oranı.
6. 🔴 **Telemetri varsayılanı:** açık kalsın mı (bugün açık), yoksa ilk açılışta sorulsun mu/kapalı mı başlasın; yerel budama sınırı.
7. 🔴 **AI-üretim varlık beyanı ve lisans doğrulaması:** sağlayıcı koşullarının (ticari kullanım/atıf) kontrolü, mağaza AI beyanı (Steam içerik anketi AI bölümü), oyun içi/okuma notu metni; WP-067A kaynak yedeğinin açılması bu beyanın ön koşulu.
8. 🔴 **Unity lisans katmanı ve splash:** mevcut lisans (Personal/Pro) dağıtım gelir eşiği ile uyumlu mu; splash ekranı kalsın mı (Pro gerektirir).
9. 🔴 **Minimum macOS sürümü ve mimari:** 10.13 alt sınırı mı yoksa daha yeni (ör. 12+) ve Universal mı / yalnız Apple silicon mı; ilk RC'de ölçülüp onaylanır.
10. 🔴 **Release ayak izi tavanı:** DEC-086 (550 MB) dağıtım kararıyla yeniden belirlenir — kanal/indirme boyutu hedefi.
11. 🔴 **Sürüm politikası onayı:** §2 `v1.0-rc.N` → `v1.0` şeması, RC ölçütleri ve `bundleVersion`/`buildNumber` eşlemesi; ardından S-01 küçük kod paketi (`--match 'v*'`, sürüm alanlarının etiketten yazılması, telemetri budama).
12. 🔴 **Yasal metinler:** EULA/kullanım koşulları, kredi ekranı (OFL metni + AI araç atıfları + ekip), gizlilik beyanının yayın yeri.
13. ✅ **Release sıkıştırması: KAPANDI** — LZ4HC, macOS + Windows (kullanıcı 2026-09-24, DEC-090; WP-066E): `tools/build.sh release` varsayılanı `--compression lz4hc`, dev sıkıştırmasız, `--compression none` eski davranış. Tavan 550 aynı (DEC-086).

## 7. Release sıkıştırma ölçümü (WP-066C, 2026-09-24)

Soru (WP-068 UI v3 konseyi): release 547,3/550 MB; `BuildOptions.CompressWithLz4/Lz4HC` ne kadar küçültür, açılış ne kadar uzar? Araç: `DdBuild -ddCompression none|lz4|lz4hc` / `tools/build.sh --compression …` — **bayrak verilmezse bugünkü davranış** (sıkıştırmasız; varsayılan komut satırı ve rapor birebir aynı, sahte-Unity karşılaştırması). Varsayılan değiştirilmedi — karar C-00/kullanıcı. **Durum: kullanıcı 2026-09-24 LZ4HC'yi seçti (DEC-090, WP-066E) — `build.sh release` varsayılanı lz4hc.**

Ölçüm: main `75ce517`, macOS release, aynı Library, S-10 soak'ları arasındaki sessiz pencerede (22:30–22:54Z); her varyanta 2 release smoke. Yükleme süresi = Player log dosyasının oluşumundan (≈ süreç başlangıcı) işaret satırına: e1 ana menü `[Audio] ready`, e4 doğrudan Run `[AP005] World ready.` (10 ms yoklama). Dosyalar build'den hemen sonra okunduğu için **sıcak disk önbelleği**; soğuk açılış (ilk indirme sonrası) ölçülmedi.

| Varyant | .app | Data | e1 menü (s, 2 koşu) | e4 Run dünyası (s, 2 koşu) |
|---|---:|---:|---|---|
| none (bugün) | 547,2 MB | 482 MB | 4,00 / 3,63 (ort 3,81) | 4,43 / 4,15 (ort 4,29) |
| lz4 | 351,1 MB (−%36) | 286 MB | 3,62 / 3,55 (ort 3,58) | 4,45 / 3,77 (ort 4,11) |
| lz4hc | 322,7 MB (−%41) | 258 MB | 3,61 / 3,56 (ort 3,59) | 4,15 / 4,01 (ort 4,08) |

Sonuç: LZ4HC kurulu boyutu ~225 MB düşürür (550 tavanına ~227 MB pay); sıcak önbellekte ölçülebilir açılış cezası yok (farklar ±0,4 s gürültü içinde; LZ4 açma M1 Pro'da bant genişliğinden hızlı). LZ4HC build süresi LZ4'ten uzun ama tam build'e göre küçük (artımlı 45 s). İndirme boyutu farkı daha küçük olur (sıkıştırmasız build zip/dmg içinde zaten sıkışır) — ölçülmedi. Windows varyantı ölçülmedi (aynı seçenek `--target win64` ile çalışır).

Yan bulgu: 6 smoke'tan 1'i (lz4hc #2) `userdata` korumasında exit 30 verdi — oyun verisi (profil, `.bak`, ana plist) birebir geri yüklendi; fark yalnız Unity'nin `unity.<şirket>.<ürün>.plist` oturum sayacı (`player_session_count` 76→77), Player çıkışının cfprefsd yazımı geri yüklemeden sonra diske düştü. Seri sonunda plist seri öncesi yedeğe döndürüldü, bayt eşit. smoke.sh koruması bu yarışa açık (küçük S-01 düzeltmesi: geri yükleme öncesi kısa bekleme / tekrar doğrulama).
