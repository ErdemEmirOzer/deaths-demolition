# WP-026 — M3 CLOSURE AUTOMATION: OTOMATİK KOŞU BOTU, TEMİZ-MAKİNE PERF/OKUNABİLİRLİK KAPISI, GERÇEK SPAWN KARIŞIMI ÖLÇÜMÜ (kapanış koşusu daha sonra)

> Yürütme promptu (C-00, 2026-09-16, HIZLI HAT) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-026.

OTURUM VE SAHİPLİK
Birincil sahip: S-10 — QA, Performance & Final Integration (tek writer, tek commit owner). S-01 (build/smoke betikleri — WP-025 paralel; onun `tools/build.sh` arayüzünü tüketirsin, gelene kadar mevcut e1/e4), S-03 (spawner karışımı), S-05/S-07 (perf probe'ları) danışılır. Paralel paketler: WP-018/019/021/022/023/025 kendi worktree'lerinde.
Dosya sahipliği: Assets/_Prototype/Diagnostics/Qa/** (yeni), tools/qa/**, kendi test dosyaların, planning/04 kendi bölümün, prompt kaydın. Oynanış koduna dokunma; probe'lar yalnız gözlemci.
Kadro: game-test-engineer (bot, soak, kapı ölçütleri — ana yük), test-engineer (CI kapısı/rapor şeması), unity-specialist (batchmode/Metal ölçüm, profiler capture), csharp-developer.
Model: Opus 5. Çalışma düzeni: HIZLI HAT — bu paket araç yazımıdır; koşular kısa doğrulama dışında makine boşken (gece) yapılır. MAKİNE KURALI: ≤ 2 Unity batch süreci; perf ölçümü tools/perf-clean.sh kalıbı (Blender ve diğer Unity < %50). Yalnız §9'da dur.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · perf temiz (WP-017): 40 zırhlı skinned zombi + oyuncu ort. 6,32 / p99 7,60 · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z (probe'lar izole).
Branch/worktree: wp-026-m3-closure-automation · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-026/worktree/deaths-demolition · etiket önerisi wp-026-m3-closure-automation.
Kaynaklar: PilotPerfProbe/WorldPerfProbe (WP-011–017), perf-clean.sh, e1/e4 smoke, WP-014/016/017 okunabilirlik ölçüm aracı (DEC-077: kapsül örtülme, kontrast; zırh biçim testi ≥ %30 + bant min 0,08/ort 0,12 — kullanıcı 2026-09-16 onayı), spawner (WP-003/010: TierSelectionPolicy, EnemySpawnDirector, DEC-021/022/023), AP-007 spawner oran kuralları + AP-008 dışlamaları (WP-016/017 ZombieLookSelector), WP-013 devri "ham p99 harici sunum çekişmesi (WindowServer/animasyonlu duvar kâğıdı)", WP-017 devri "Procedural A/B + gerçek spawner karışımı ölçümü S-10".
Amaç: M3 kapanış regresyonunu tek komutla koşulabilir hale getirmek: otomatik oyuncu botu ile N-round koşu (gerçek karakterler/zırh/efekt), temiz-makine perf kapısı, DEC-077 okunabilirlik kapısı (gerçek spawn karışımı, zırh biçim testi), spawner karışım istatistiği, rapor şeması. Kapanış koşusunun kendisi bu pakette değil (tüm M3 paketleri merge olunca C-00 tetikler).

2. BAĞLAYICI KARARLAR
* Bot (`QaRunBot`): batchmode PlayMode'da oyuncuyu sürer (nişan en yakın zombi, ateş, hareket: rota grafı üzerinde dolaşım, hasar alınca geri çekilme), N round (varsayılan 8), tohumlu (seed) ve deterministik olmaya çalışır (rastgelelik kaynakları tohumlanır); oynanış kodunu değiştirmez (yalnız Input System üzerinden sanal girdi).
* Perf kapısı: temiz pencere (perf-clean.sh: Blender/diğer Unity < %50, 60 s), ölçüm 60 s × 3 koşu, rapor ort./medyan/p99/maks/stall + draw/üçgen/doku/RT bellek + GC; kapı değerleri: ort. ≤ 7,0 / temiz p99 ≤ 10 ms (ADR-011-3 v2 ile uyumlu; değeri sen değiştirme — raporla). Harici sunum çekişmesi tespiti (Gfx.WaitForPresent > 10 ms oranı) ayrı satır.
* Okunabilirlik kapısı (DEC-077): gerçek spawner karışımıyla (versiyon/zırh/giysi dağılımı DEC-021/022 tablosundan, round 1/4/8) kapsül örtülme >%50 = 0, oyuncu/çevre ≥ 0,06, oyuncu↔zombi ≥ 0,06 (skin 1/2b/3), zırh biçim testi ≥ %30 + bant min 0,08 / ort 0,12; sonuç JSON.
* Spawner karışım istatistiği: 1000 spawn simülasyonu round 1–10: versiyon dağılımı DEC-022 garanti tanıtımları, zırh bileşimi oranları, giysi/zırh kombinasyon yasakları 0, AP-007 oran kuralları (cw2 ≤ %30, T7 ≤ %10, H0 ≤ %25) — rapor.
* Rapor şeması (`qa-report.json` + ≤ 20 satır özet): kapı sonuçları PASS/FAIL, pencere/yük, build sürümü (WP-025 build-info varsa).
* Paket/sürüm değişikliği yok; push yok.

3. İZLENEBİLİRLİK
DEC-021/022/023/077 · ADR-011-3, ADR-015-1/2, ADR-017-1 · WP-013/017 devirleri · HIZLI HAT S-10 rolü (M3 kapanış tam regresyonu).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Otomatik oyuncu botu yok (probe'lar statik/senaryo). 4.2 Perf kapısı elle. 4.3 Okunabilirlik ölçümü sabit örneklerle, spawn karışımı yok. 4.4 Rapor şeması yok.

5. UYGULAMA KAPSAMI
A. `QaRunBot` + senaryo tanımı (N round, seed). B. `tools/qa/regression.sh` (build → bot koşusu → perf → okunabilirlik → spawner istatistiği → rapor; WP-025 build.sh gelince onu çağırır). C. Okunabilirlik kapı aracı (mevcut ölçümü gerçek karışımla genişlet). D. Spawner karışım simülatörü (EditMode, hızlı). E. Kısa doğrulama koşusu: 2 round bot + 1 perf koşusu (temiz pencere) — çalıştığını kanıtla; tam 8-round/3×60 s koşu C-00 tetikleyince. F. Belge: planning/04 WP-026 + ADR-026-1 (kapanış kapıları ve rapor şeması); prompt kaydı; tools/qa/README.md.

6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/AI/spawn kodu; kapı değerleri (ADR'lerden okunur); Packages/ProjectSettings; diğer oturumların dosyaları; kullanıcı verisi (probe izole); planning karar metinleri.

7. TEST MATRİSİ
EditMode: spawner simülatörü (dağılım/yasak 0), rapor şeması, bot girdi eşlemesi. PlayMode (Metal): 2-round bot koşusu (batchmode; başka Unity ≥2 ise bekle), perf 1 koşu temiz pencere, okunabilirlik 1 karışım. Build: dev build bir kez (mevcut betik); restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase → tek commit (paket yolları), mesaj "WP-026: M3 closure automation — QA run bot, clean-machine perf & readability gates, spawner mix stats, report schema" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır. C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Bot oynanış kodu değişikliği gerektiriyorsa (yalnız test kancası; S-02/S-03'e sor, mantık değiştirme) · determinizm sağlanamıyorsa (tohum raporla, durma) · makine yükü ölçümü imkânsızsa (gece koşusu, raporla).

10. DEFINITION OF DONE
Bot + regression.sh + kapı araçları + simülatör · kısa doğrulama koşusu PASS (2 round, 1 perf, 1 okunabilirlik) · rapor şeması · testler yeşil · 04 girişi + ADR-026-1 + prompt kaydı · tek commit + C-00 notu.
