# WP-025 — M3 BUILD PIPELINE & SAFETY: RELEASE BUILD OTOMASYONU, YEREL LFS/REPO YEDEKLEME, RENDER-TEXTURE TABANI ANALİZİ, ÇÖKME/LOG RAPORLAMA

> Yürütme promptu (C-00, kullanıcı kararıyla doğrudan dağıtım, 2026-09-16) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-025. Çalışma düzeni: `HIZLI-HAT-DIREKTIFI.md`.

OTURUM VE SAHİPLİK
Birincil sahip: S-01 — Technical Foundation & Integration (tek writer, tek commit owner). S-07 (MSAA/HDR kararı — WP-019 paralel; sen analiz + öneri yazarsın, uygulama S-07/S-05 sonraki pakette), S-10 (regresyon otomasyonu WP-026 — build betiklerini o tüketir; arayüzü sen tanımlarsın). Paralel paketler: WP-018/019/021/022/023/026 kendi worktree'lerinde.
Dosya sahipliği: tools/**, Assets/_Prototype/Core/Editor/** (build), Assets/_Prototype/Diagnostics/CrashLog* (yeni), ProjectIntegrityTests.cs (yalnız sen), planning/04 kendi bölümün, prompt kaydın. Diğer modüllere dokunma.
Kadro: unity-specialist (build pipeline, IL2CPP/Mono ayarı doğrulama, quality/render analizi), git-repository-safety (yedekleme, LFS bütünlüğü, sürüm damgası), csharp-developer (log/çökme raporlama, testler), engineering-manager (teslim akışı ölçütleri — kısa).
Model: Opus 5. Çalışma düzeni: HIZLI HAT. MAKİNE KURALI: ≤ 2 Unity batch süreci; build'ler kuyruğa (`ps` kontrolü). Yalnız §9'da dur.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · `git lfs ls-files` 399 · `.git/lfs` ≈ 1,5 GB (yalnız yerel, remote yok) · release build 425,5 MB (ADR-015-2 tavanı 450) · EditMode 158/158 · PlayMode 23/23.
Branch/worktree: wp-025-m3-build-pipeline · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-025/worktree/deaths-demolition · etiket önerisi wp-025-m3-build-pipeline.
Kaynaklar: mevcut build/smoke betikleri (wp-0xx/tools, `LifecycleProbeBuild.cs`, e1/e4 smoke, `perf-clean.sh`, PilotPerfProbe/WorldPerfProbe), ADR-011-2 (LFS politikası), ADR-015-1/2 (doku/build tavanları), WP-013/017 devri "RT tabanı 203 MB (1080p MSAA 4× derinlik 79 + 2×39,6 renk)", DEC-080 (Windows süresiz ertelendi — yalnız macOS), kullanıcı verisi kuralları (WP-009: ESKİ BUILD GERÇEK PROFİLLE AÇILMAZ; probe izolasyonu; restore 6/6).
Amaç: Tek komutla tekrarlanabilir release/dev build + smoke (S-10 ve tüm paketler için ortak), sürüm damgası, repo+LFS yerel yedekleme (veri kaybı riski — remote yok), render-texture tabanı için ölçülü karar analizi, oyuncu çökme/log raporlama temeli.

2. BAĞLAYICI KARARLAR
* `tools/build.sh {dev|release} [--smoke]`: batchmode build (macOS, mevcut Player ayarları; IL2CPP/Mono mevcut ne ise), sürüm damgası (git describe + tarih → `Resources/build-info.json` + ana menü köşe metni için okunur), build boyutu/süre kaydı, e1/e4 smoke, kullanıcı verisi izolasyonu (probe profil dizini; gerçek profile dokunmaz; otomatik restore doğrulaması), çıkış kodu sözleşmesi (S-10 kullanır).
* Yedekleme (ADR-025-1): `tools/backup-repo.sh` → `git bundle --all` + `.git/lfs/objects` rsync → hedef klasör (varsayılan `~/Backups/deaths-demolition/<tarih>/`; kullanıcı dosyalarına dokunma, yalnız yeni dizin yaz), doğrulama (`git bundle verify`, LFS nesne sayısı 399 eş), tutma politikası (son 3); manuel tetikleme; remote/push YOK (kullanıcı kararı). Bugün bir kez koş ve sonucu nota yaz.
* RT tabanı analizi (ADR-025-2 önerisi, uygulama değil): MSAA 4× → 2×/kapalı + FXAA/SMAA, HDR açık/kapalı, render scale; her seçenek için tahmini bellek/kalite/perf etkisi ve DEC-077 okunabilirlik riski; öneri tek satır; uygulama S-07/S-05 sonraki pakete (ProjectSettings/URP asset değişikliği bu pakette YAPILMAZ).
* Çökme/log raporlama: `Application.logMessageReceived` + `UnhandledException` → yerel `Logs/dd-crash-<tarih>.txt` (kişisel veri yok, yol/hesap adı maskelenir), koşu kimliği/round/sürüm damgası; yükleme/telemetri YOK (çevrimdışı oyun).
* Paket/sürüm değişikliği yok; ProjectSettings yalnız `build-info` üretimi için değil (Resources asset üretimi Editor'dan); push yok.

3. İZLENEBİLİRLİK
ADR-011-2, ADR-015-1/2 · DEC-080 · WP-009 kullanıcı verisi kuralları · WP-013/017 RT devri · WP-026 (S-10 tüketici).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Build/smoke betikleri paket klasörlerine dağınık, ortak komut yok. 4.2 Sürüm damgası yok. 4.3 Repo+LFS yedeği yok (tek kopya). 4.4 RT tabanı kararı açık. 4.5 Çökme/log raporlama yok.

5. UYGULAMA KAPSAMI
A. `tools/build.sh` + `tools/smoke.sh` (mevcut e1/e4'ü sarar) + çıkış kodu/rapor JSON. B. Sürüm damgası Editor aracı + menü metni kancası (S-09 tüketir; UI dosyasına dokunma — `BuildInfo.Read()` API). C. `tools/backup-repo.sh` + doğrulama + ilk yedek. D. RT tabanı analiz notu (ölçüm: mevcut build'de RenderTexture bellek dökümü, 3 senaryo). E. Çökme/log raporlayıcı + test. F. ProjectIntegrityTests: build-info ve betik varlığı testi. G. Belge: planning/04 WP-025 + ADR-025-1 (yedekleme) + ADR-025-2 (RT önerisi) + tools/README.md; prompt kaydı.

6. DEĞİŞTİRİLMEYECEK KONULAR
ProjectSettings/QualitySettings/URP asset'leri (analiz yalnız); oynanış kodu; diğer oturumların dosyaları; kullanıcı verisi; remote/push (yok); planning karar metinleri.

7. TEST MATRİSİ
EditMode: build-info üretimi, çökme raporlayıcı maskeleme testi, ProjectIntegrity ek. Betik testleri: `build.sh dev --smoke` tam tur (kuyruk kuralıyla), `backup-repo.sh` verify. Build/smoke: release bir kez (betikle); restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase → tek commit (paket yolları), mesaj "WP-025: M3 build pipeline & safety — build/smoke scripts, version stamp, local repo+LFS backup, RT analysis, crash log" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır (SHA, betikler, yedek konumu/doğrulama, build süre/boyut, RT önerisi tek satır). C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Yedekleme hedefi diskte yer yoksa (raporla, hedef sor) · build betiği mevcut e1/e4 ile çelişiyorsa (mevcut korunur) · ProjectSettings değişikliği zorunlu görünüyorsa (yapma, raporla).

10. DEFINITION OF DONE
build/smoke tek komut + rapor JSON · sürüm damgası · ilk yedek alındı ve doğrulandı · RT analizi + öneri · çökme/log raporlayıcı · testler yeşil, release build + smoke, restore 6/6 · 04 girişleri + ADR-025-1/2 + prompt kaydı · tek commit + C-00 notu.
