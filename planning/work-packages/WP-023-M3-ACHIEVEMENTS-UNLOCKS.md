# WP-023 — M3 ACHIEVEMENTS & UNLOCKS: BAŞARIM SİSTEMİ, AÇILAN İÇERİK KATALOĞU, RUN RESULTS VERİSİ, PROFİL ŞEMASI v4

> Yürütme promptu (C-00, 2026-09-16, kullanıcı kararıyla doğrudan dağıtım) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-023.

OTURUM VE SAHİPLİK
Birincil sahip: S-04 — Economy, Progression & Save (tek writer, tek commit owner). S-09 (Run Results/başarım görünümü — WP-022 paralel; veri arayüzünü sen tanımlarsın, S-09 tüketir), S-03 (kill/versiyon sayaçları), S-02 (silah/geliştirme dalı olayları) danışılır. Paralel paketler kendi worktree'lerinde: WP-018/019/021/022/025/026.
Dosya sahipliği: Assets/_Prototype/Profile/**, Economy/** (veri/kural dosyaları), yeni Assets/_Prototype/Achievements/**, kendi test dosyaların, planning/04 kendi bölümün, prompt kaydın. UI dosyalarına dokunma (S-09); diğer modüllere yalnız olay dinleyicisi/arayüz; ProjectIntegrityTests.cs'e dokunma (S-01).
Kadro: game-designer (başarım/unlock tasarımı: oynanışı etkilemeyen, DEC-007/038; Opus 5), csharp-developer (kural motoru, şema, testler; ana yük), backend-architect (crash-safe kayıt/idempotans — WP-009/009H kalıplarıyla uyum), game-test-engineer (kabul testleri).
Model: tasarım/kod Opus 5 (sanat yok).
Çalışma düzeni: HIZLI HAT. MAKİNE KURALI: ≤ 2 Unity batch süreci (`ps -Ao comm | grep -c "MacOS/Unity$"`); ölçüm gerekmez (kod paketi). Yalnız §9'da dur. Yeni ürün kararı verme: başarım listesi teknik/tasarım varsayılan olarak yazılır, kullanıcı raporla görür (ürün kararı değil — planning'de "başarımlar kalıcı" var, liste yok).

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z (Player profili SaveVersion 3; probe'lar izole; gerçek profil yalnız göç testinde kopya üzerinde).
Branch/worktree: wp-023-m3-achievements-unlocks · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-023/worktree/deaths-demolition · etiket önerisi wp-023-m3-achievements-unlocks.
Kaynaklar: planning/00 DEC-007 (başarımlar/açılanlar/seviye/istatistik kalıcı), DEC-018 (3 skin baştan açık — unlock değil), DEC-035/036/038 (Results tek ekran; yeni içerik otomatik açılır, Claim yok, sonraki seçimde hazır), DEC-069–075 (ekonomi kuralları), profil XP kuralları (REQ-027/028) · planning/01 REQ-021 (en yüksek round/başarım/açılan içerik kalıcı), REQ-022 (istatistikler: toplam kill, tip/versiyon başına kill, koşu sonuçları), REQ-148–155 · WP-005 ProfileViewModels/ResultsScreen (arayüz), WP-009/009H (ProfileRunCommitter, CompletionAuthority, şema v3, crash-safe, exactly-once), WP-010 EnemyVersion istatistikleri, WP-008 upgrade dalları (unlock adayları için) · S-02'nin gelecek silah listesi (WP-024 öneri notu; silah unlock'ları liste kararına kadar veri-güdümlü boş).
Amaç: DEC-007/038/REQ-021'i çalışır hale getirmek: veri-güdümlü başarım kuralları (koşu içi sayaçlardan, deterministik, exactly-once commit), açılan içerik kataloğu (silah geliştirme dalı/kozmetik/gösterge tipleri; skinler hariç), Run Results için DEC-036 tam veri modeli, profil şeması v4 (geriye uyumlu göç, crash-safe).

2. BAĞLAYICI KARARLAR
* Başarımlar oynanışı etkilemez (DEC-007 ruhu; hasar/can/gold avantajı yok — kapsam dışı maddesi). Kural motoru veri-güdümlü (`AchievementDefinition` ScriptableObject/JSON: id, koşul türü, eşik, sayaç kaynağı, nadirlik; ilk set ≈ 24: round eşikleri, toplam kill, versiyon başına kill, zırh kırma, Special kullanımı, gold, hasarsız round, koşu sayısı, seviye).
* Unlock kataloğu (`UnlockCatalog`): içerik türleri ve açılma koşulu (başarım/seviye/round); DEC-038: koşu sonunda otomatik, Results'ta vurgulanır, Claim yok; skinler her zaman açık (DEC-018). Silah/dal unlock girdileri S-02 listesi gelene kadar boş şablon.
* Commit: koşu sonu tek `ProfileRunCommitter` yolunda, `CompletionAuthority` lineage/ordinal ile exactly-once (WP-009H); başarım/unlock çift açılmaz; çökme sonrası yeniden uygulama idempotan.
* Şema v4: v3 → v4 göç (başarım/unlock/istatistik alanları), geriye uyumlu okuma, `.bak` kuralı, pid kilidi aynen; ESKİ BUILD'LER GERÇEK PROFİLLE AÇILMAZ kuralı sürer.
* Results veri modeli (`RunResultsData`): DEC-036 alanları (round, kill, versiyon başına kill, XP dökümü, seviye ilerlemesi, açılanlar, başarımlar) — S-09 tüketir; arayüz ADR-023-1.
* Gold/XP kuralları değişmez (DEC-069–075; REQ-027).

3. İZLENEBİLİRLİK
DEC-007/018/035/036/038/069–075 · REQ-021/022/027/028/148–155 · WP-005/008/009/009H/010 · WP-022 (S-09 tüketici), WP-024 (silah listesi).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Başarım sistemi yok (UI'de referans/placeholder). 4.2 Unlock kataloğu yok. 4.3 Results verisi DEC-036'nın tamamını taşımıyor. 4.4 Şema v3'te başarım alanı yok.

5. UYGULAMA KAPSAMI
A. Tasarım notu (game-designer, 1 sayfa): ilk başarım seti (≈24) + unlock türleri; oynanış etkisi 0 doğrulaması. B. `AchievementEngine` (sayaç kaynakları: WP-003/010 round/kill, WP-006 zırh, WP-007 Special, WP-004/008 gold; koşu içi hesap, koşu sonu commit). C. `UnlockCatalog` + otomatik açılma + Results vurgusu verisi. D. Profil şeması v4 + göç + crash-safe testleri (WP-009 kalıbı: kesinti/yeniden uygulama). E. `RunResultsData` + `IRunResultsSource` (ADR-023-1). F. Belge: planning/04 WP-023 + ADR-023-1 (Results/başarım veri sözleşmesi) + ADR-023-2 (şema v4); prompt kaydı; README.

6. DEĞİŞTİRİLMEYECEK KONULAR
Gold/XP/upgrade kuralları; skin açıklık kuralı (DEC-018); UI dosyaları (S-09); kayıt kilidi/exactly-once mekanizması (uzatılır, değiştirilmez); Packages/ProjectSettings; planning karar metinleri.

7. TEST MATRİSİ
EditMode: kural motoru (24 tanım × tetik/eşik), unlock koşulları, exactly-once (aynı koşu iki kez commit → tek açılma), şema v3→v4 göç (kopya profil üzerinde; gerçek profile dokunma), crash-safe kesinti/replay (WP-009 kalıbı), Results veri modeli. PlayMode (Metal): 1 koşu → Results verisi doğru, çökme simülasyonu → kayıp 0. Build/smoke: Player kodu değiştiği için release build bir kez + e1/e4 (profil izole); kullanıcı verisi restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase → tek commit (paket yolları), mesaj "WP-023: M3 achievements & unlocks — rule engine, unlock catalog, run results data, profile schema v4" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır (SHA, dosya, başarım/unlock sayıları, göç testi, testler, devirler). C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Şema göçü veri kaybı riski çözülemiyorsa (raporla) · başarım tanımı oynanış etkisi gerektiriyorsa (üretme, raporla) · exactly-once ihlali.

10. DEFINITION OF DONE
≈24 başarım + unlock kataloğu veri-güdümlü · koşu sonu exactly-once commit · şema v4 göç + crash-safe testleri yeşil · RunResultsData sözleşmesi (S-09 için) · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-023-1/2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.

---
Not (S-04, teknik kararlar): §4.1 "başarım sistemi yok" kısmen doğru değildi — WP-005 dört tanımlı minimal değerlendirme (`ProfileProgression.EvaluateAchievements`, config listesi) vardı; WP-023 bunu veri-güdümlü kataloğa taşır, dört kimliği korur, config listesini kaynak olmaktan çıkarır (asset dokunulmadı). "Özel dal main'e rebase" uygulanmadı: dal taban 00bcde0c'nin doğrudan üstünde, rebase'e gerek yok (güvenlik sınırı: rebase yok).
