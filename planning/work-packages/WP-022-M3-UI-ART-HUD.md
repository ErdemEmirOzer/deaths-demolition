# WP-022 — M3 UI ART & HUD v2: AP-012 UI SANATI, HUD, SKIN & WEAPON SELECTION, RUN RESULTS, MENÜLER

> Yürütme promptu (C-00, 2026-09-16, kullanıcı kararıyla doğrudan dağıtım) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-022.

Önce oku: ~/Desktop/KADRO/context/projects/deaths-demolition/HIZLI-HAT-DIREKTIFI.md ve HIGGSFIELD-ONCELIK-DIREKTIFI.md (§2 UI, §3 kredi tabanı 200, §5 sadakat, §7 Adobe — oturumunda doğrulandı). Hemen başla.

OTURUM VE SAHİPLİK
Birincil sahip: S-09 — UI, UX & Accessibility (tek writer, tek commit owner; üretim AP-012 + entegrasyon aynı pakette). S-04 (Run Results/başarım verisi — WP-023 paralel), S-07 (Special beacon HUD işareti API'si — WP-019 paralel), S-02 (silah seçimi verisi), S-08 (UI sesleri) danışılır. Paralel paketler kendi worktree'lerinde: WP-018/019/021/023/025/026.
Dosya sahipliği: yalnız Assets/_Prototype/UI/**, Assets/_Art/UI/**, yeni test dosyaların, planning/04 kendi bölümün, prompt kaydın. Diğer modüllere yalnız veri okuma arayüzü ekle; ProjectIntegrityTests.cs'e dokunma (S-01). WP-005 presenter/navigation sözleşmeleri korunur (uzatılır).
Kadro: ui-ux-designer (ekran düzeni, tipografi ölçeği, WCAG kontrast; Fable 5.1), graphic-designer (ikon seti/çerçeve/HUD dili; Fable 5.1), ux-writer (mikro-metin; Opus 5), frontend-developer/csharp-developer (UGUI/TMP uygulaması, testler; Opus 5), interaction-designer (geçişler, odak sırası, gamepad gezinme; Opus 5), art-director tek yargı (Fable).
Model: tasarım/sanat yargısı Fable 5.1; kod/test Opus 5.
Çalışma düzeni: HIZLI HAT; Higgsfield-first: ikon/çerçeve/HUD öğeleri `generate_image` (+ `remove_background`, `upscale_image`) → Adobe finisaj (`image_apply_adjustments`, `image_remove_background`; yükleme init→curl PUT→finalize; engellenirse README notu) → `sandbox_exec` sprite atlas paketleme; kredi bütçesi ≤ 50 (S-08 ile paylaşımlı; taban 200), yalnız gerekli üretim. §5 sadakat: Unity ekran ↔ Higgsfield/Fable tasarım kompoziti yan yana.
MAKİNE KURALI: aynı anda ≤ 2 Unity batch süreci — `ps -Ao comm | grep -c "MacOS/Unity$"` ≥ 2 ise 5 dk bekle; ölçümler tools/perf-clean.sh kalıbı. Yalnız §9'da dur.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · TMP 3.0.6 ve Input System 1.7.0 zaten kurulu (paket ekleme yok) · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z.
Branch/worktree: wp-022-m3-ui-art-hud · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-022/worktree/deaths-demolition · etiket önerisi wp-022-m3-ui-art-hud. Üretim kökü: asset-production/AP-012/ (README, 03-higgsfield ledger, exports/ atlas + manifest).
Kaynaklar: planning/00 DEC-035 (meta akış), DEC-036 (Run Results tek ekran: round, kill, versiyon başına kill, XP dökümü, seviye, açılan içerik, başarımlar), DEC-037/039 (Skin & Weapon Selection), DEC-038 (yeni içerik otomatik açılır, Results'ta vurgulanır), DEC-053 (HUD beacon işareti), DEC-076 palet, DEC-077 okunabilirlik · planning/01 REQ-009 (sade ama kaliteli), REQ-053 (Special HUD işareti), REQ-104/105 (HUD öğeleri çevre paletinden ayrılır), REQ-015 (round sonu geliştirme ekranı) · WP-005 UiNavigation/presenter'lar/ekranlar (mevcut), WP-008 RoundUpgradeShop UI · stil kılavuzu v1 (Assets/_Art/StyleGuide/style-guide-v1.md: palet, beacon/altın/uyarı rezervleri, oyuncu işareti) · AP-006 3 skin turnaround (Skin seçimi önizlemesi: gerçek karakter renderı — P-05 renderleri `AP-006/06-renders/turnaround/*_v002.png`) · AP-010 HUD beacon işareti çizimi (gri, runtime tint) · WP-019 beacon HUD API'si (main'e gelince bağla).
Amaç: UI'yi placeholder'dan ürün kalitesine taşımak: HUD (can, gold, round/havuz, klasik silah + geliştirme durumu, Special Weapon süresi/beacon işareti, hasar vinyeti ile uyum), Skin & Weapon Selection (gerçek skin önizlemeleri), Run Results (DEC-036 tam), ana menü/pause/ayarlar iskeleti, upgrade ekranı görsel dili — AP-012 ikon/çerçeve seti ile; gamepad gezinme ve erişilebilirlik (kontrast, ölçek).

2. BAĞLAYICI KARARLAR
* UGUI + TMP (mevcut); tek `UiTheme` ScriptableObject (palet DEC-076, tipografi ölçeği, kenar/çerçeve stili); sprite atlas'lar `Assets/_Art/UI/` (AP-012 exports'tan, ADR-015-1 UI sınıfı ≤ 1024²; toplam UI sevk ≤ 12 MB).
* HUD okunabilirlik (DEC-077/REQ-105): HUD öğeleri kontrast ≥ 4,5:1 (WCAG AA) çevre üstünde; beacon/altın/uyarı rezerv renkleri yalnız kendi öğesinde; HUD kapsül alanını örtmez (güvenli bölge kuralı).
* Gamepad gezinme (Input System mevcut): tüm ekranlar odak sırası + gamepad ile tamamlanabilir; klavye-fare eşdeğer.
* Skin önizlemesi: 3 skin için hazır render (AP-006 turnaround v002 → 512² kırpım, Adobe finisaj) — runtime 3D önizleme WP-018 sonrası opsiyonel.
* Run Results: DEC-036 alanları; başarımlar/açılanlar WP-023'ün veri arayüzüyle (gelene kadar mevcut ProfileViewModels verisi + stub).
* AP-012 üretimi: ikon seti (silah/geliştirme dalları ≥ 12, gold, Special, round, can, ayarlar ≥ 8, başarım çerçeve ×3 nadirlik), HUD çerçeve/dolgu parçaları (9-slice), menü arka plan plakası (DEC-076 atmosferi, düşük doygunluk), imleç/nişangâh ×2; Higgsfield → remove_background → upscale → Adobe kontrast/hizalama → atlas + AP012-ui-manifest.json.
* Paket/sürüm değişikliği yok; LFS aynen (png pointer); push yok.

3. İZLENEBİLİRLİK
DEC-035–039/053/076/077 · REQ-009/015/053/104/105 · WP-005 (meta UI), WP-008 (upgrade UI), WP-019 (beacon API), WP-023 (başarım verisi) · AP-012 (Codex planı: UI, S-09).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 UI placeholder (WP-005 iskeleti). 4.2 İkon/çerçeve yok. 4.3 Skin seçimi görsel önizlemesiz. 4.4 Run Results DEC-036 alanları kısmen. 4.5 Gamepad gezinme test edilmemiş. 4.6 HUD kontrast/güvenli bölge ölçülmemiş.

5. UYGULAMA KAPSAMI
A. UI dili sayfası (ui-ux + graphic, Fable): HUD/menü kompozitleri (1080p), tipografi ölçeği, ikon dili, çerçeve 9-slice planı; kullanıcı kararı gerekmez (C-00'a 5 satır + kompozit yolu, bir kez gösterilir). B. AP-012 üretim + atlas + manifest. C. `UiTheme` + 9-slice kit + ikon bağlama. D. HUD v2 (+ beacon işareti API bağlantısı, Special süre), Skin & Weapon Selection v2, Run Results v2, Main/Pause/Settings iskeleti (ses/grafik ayarları S-01/S-08 ile veri arayüzü), Upgrade ekranı görsel dili. E. Gamepad gezinme + odak + erişilebilirlik (kontrast ölçüm betiği, UI ölçek 0,9–1,2). F. Ölçüm: HUD kontrast testi (5 bölge kadrajı üstünde), güvenli bölge testi, UI GC/alloc (HUD güncelleme 0 alloc; WP-007 HUD 62 KB/kare devri kapanır), perf. G. Görsel: Unity ekran ↔ tasarım kompoziti yan yana; AD tek yargı. H. Belge: planning/04 WP-022 + ADR-022-1 (UI tema/atlas sözleşmesi) + AP tablosu (AP-012); prompt kaydı; Assets/_Art/UI/README.md.

6. DEĞİŞTİRİLMEYECEK KONULAR
Meta akış kuralları (DEC-035/037/039); ekonomi/upgrade kuralları; WP-005 navigation sözleşmesi (uzatılır); Packages/ProjectSettings; Unity sürümü; diğer oturumların dosyaları; planning karar metinleri.

7. TEST MATRİSİ
EditMode: atlas/manifest asset-gate, tema tablosu, presenter/view-model testleri (mevcut kalıp), kontrast hesap testi. PlayMode (Metal): ekran akışı 1 tur (Menu → Selection → Run → Results → Menu) klavye ve gamepad, HUD güncelleme alloc 0, beacon işareti tetikleme (WP-019 API varsa), skin seçimi görsel takas. Build/smoke: release build bir kez + e1/e4; restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase → tek commit (paket yolları, --pathspec-from-file, LFS doğrulama), mesaj "WP-022: M3 UI art & HUD v2 — AP-012 icon/frame set, HUD, selection, results, menus, gamepad navigation" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır. C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Bakiye 200 · UI Toolkit/paket değişimi zorunluysa · kontrast ölçütü tema ile tutmuyorsa (raporla) · WP-019/023 arayüzleri gelmeden bloklanıyorsa (stub + devir, durma).

10. DEFINITION OF DONE
AP-012 atlas + manifest · UiTheme + 9-slice kit · HUD v2, Selection v2 (3 skin önizleme), Results v2 (DEC-036), menü iskeleti · gamepad gezinme tam · kontrast/güvenli bölge/alloc ölçümleri notta · AD sadakat yargısı · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-022-1 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
