# WP-029 — M3 LIGHTING & LOOK PASS: ADOBE LIGHTROOM İLE HEDEF GÖRÜNÜM, LUT, TONEMAP/SİS/GÖLGE/SSAO, LIGHTMAP + LIGHT PROBE BAKE

> Yürütme promptu (kullanıcı kararıyla C-00 doğrudan dağıttı, 2026-09-16 20:45; S-05'e cross-session mesajla ulaştı) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-029.

Önce oku: HIGGSFIELD-ONCELIK-DIREKTIFI.md §7 (Adobe), §11 (model), §13 (Lightroom ışık çalışması). Hemen başla; AP-005C teslimi gelince WP-020 (delta v3) araya girer — bu paketin bake adımı tekrarlanabilir olduğu için sorun yok.

OTURUM VE SAHİPLİK
Birincil sahip: S-05 — World, Environment & Lighting (tek writer, tek commit owner). S-07 (post-fx/GameFeel vinyet ile uyum — WP-019 paralel; `DD_Overcast_v1` profilini sen genişletirsin, S-07 dokunmaz), S-01 (RT/MSAA analizi WP-025 — sonucu bekle, uygulama bu pakette değil), S-09 (UI kontrastı LUT sonrası yeniden ölçer) danışılır. Paralel paketler kendi worktree'lerinde.
Dosya sahipliği: Assets/_Art/Lighting/**, Assets/_Art/StyleGuide/** (§5.2 ışık reçetesi), World_AP005.unity ışık/probe/reflection nesneleri, `DD_Overcast_v1.asset`, `ArtLightingBuilder.cs`, tools/bake-lighting.sh (yeni), kendi test dosyaların, planning/04 kendi bölümün, prompt kaydın. QualitySettings/URP asset: yalnız gölge kaskad/mesafe ve SSAO renderer feature (build-etkileyen → release build bir kez); MSAA/HDR değişikliği YOK (WP-025 analizi sonrası ayrı karar).
Kadro: art-director (görünüm hedefi, 3 aday, tek yargı; Fable 5.1), concept-artist (Lightroom aday reçeteleri; Fable 5.1), unity-specialist (LUT/post/ışık/bake/perf; Opus 5), csharp-developer (LUT üretim aracı, testler; Opus 5).
Model: görünüm tasarımı/yargı Fable 5.1; teknik Opus 5. Çalışma düzeni: HIZLI HAT; §5 sadakat (Unity ↔ Lightroom hedef karesi yan yana). MAKİNE KURALI: ≤ 2 Unity batch; bake/ölçüm kuyruğa; perf-clean.sh. Yalnız §9'da dur.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab (WP-020 araya girerse yeni main'e rebase) · etiket wp-017-m3-world-v2-armor-r2 · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · perf temiz 6,32 / 7,60 ms · kullanıcı verisi tabanı wp-010.
Branch/worktree: wp-029-m3-lighting-look · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-029/worktree/deaths-demolition · etiket önerisi wp-029-m3-lighting-look. Adobe: oturumunda `adobe_mandatory_init` → `get_account_type` doğrula; yükleme akışı init → curl PUT → finalize (§7.1); **yükleme engellenirse** kare listesini C-00'a ver — C-00 kullanıcı talimatıyla (§13) Lightroom adımını kendi oturumunda uygular ve sonuç dosyalarını/URL'leri sana döner.
Kaynaklar: DEC-068/076 (nihai palet: Presentation A, yağmur sonrası kapalı hava), DEC-077, REQ-103/104/105 · stil kılavuzu v1 final (§3 palet, §5 ışık dili, §5.1 Higgsfield referans kurulu) · WP-013 `DD_Overcast_v1`, post-fx (renderer postProcessData artık atanmış), sis 210 m · WP-015 doku politikası · WP-017 dünya v2 · Adobe Lightroom preset listesi (`image_list_presets`: aday başlangıçları — "Style: Cinematic II - CN12" muted greens/rich contrast, "Creative - Cool Shadows & Warm Highlights", "Creative - Soft Mist", "Creative - Desaturated Contrast", "Curve - Lift Shadows", "Grain - Light", "Vignetting - Light") + `image_apply_adjustments` (tempA/B, highlights/shadows/darks, vibrance, contrast) · AP-001/AP-005 Presentation renderleri (referans).
Amaç: Oyunun ışık/görünüm kalitesini komple yükseltmek: Lightroom ile hedef görünüm reçetesi (3 aday → kullanıcı seçer) → aynı reçete kimlik LUT şeridine → URP Color Lookup + tonemapping/exposure/sis/gölge/SSAO ayarı → dünya için lightmap + light probe + reflection probe bake (tekrarlanabilir betik) → motor içi görünüm Lightroom hedefiyle eşleşir; okunabilirlik DEC-077 korunur.

2. BAĞLAYICI KARARLAR
* Referans kareler: Unity'den 8 kare (5 bölge gameplay kamerası + Presentation A kadrajı + 40 zombi kalabalık + oyuncu yakın plan), 1920×1080, post-fx kapalı ham + mevcut post ile; PNG.
* Lightroom keşfi (Fable/concept-artist): 3 aday reçete (her biri preset + adjustments kombinasyonu; DEC-076 içinde: kapalı hava, sakura pembe–yeşil dengesi, taş grisi sıcaklığı, sis derinliği) 8 kareye uygulanır → kontak sayfası (3 aday × 8 kare) → **C-00'a 5 satır + kontak yolu → kullanıcı seçer** (tek karar; seçim gelene kadar teknik hazırlık: LUT aracı, bake betiği, SSAO/gölge).
* LUT: Unity nötr LUT şeridi (URP `ColorGradingLutSize` ile eş: 32 → 1024×32; ayrıca 64 seçenek) sRGB PNG üretilir → seçilen Lightroom reçetesi (yalnız global tone/renk ayarları; adaptive/maskeli presetler LUT'a uygulanmaz — README'de not) aynı parametrelerle şeride uygulanır → `Assets/_Art/Lighting/LUT/DD_Look_v1.png` (sRGB kapalı, mip yok, sıkıştırma yok) → Volume Color Lookup (LDR grading mode; mevcut post ayarlarıyla çift uygulama yok). Doğrulama: aynı ham kareye Lightroom ↔ Unity LUT sonucu ΔE ortalaması ≤ 6 (ölçüm aracı).
* Işık: sun/ambient/exposure/tonemapper (Neutral kalır — DEC-077 altın ayrışması korunur), sis 210 m + yükseklik sisi ayarı reçeteyle uyumlu; gölge kaskad/mesafe kalite (M1 Pro bütçesinde); SSAO renderer feature (düşük maliyet ayarı, ≤ 0,6 ms); lightmap bake (statik dünya v2 → v3 rebake betikle; Progressive GPU/CPU, çözünürlük bütçesi: lightmap toplam ≤ 64 MB, ADR-015-1'e "lightmap" sınıfı), light probe grubu (rota/spawn noktaları + bölge örgüsü), reflection probe'lar (5 bölge + gölet), karakter/zırh probe aydınlatması (skinned mesh ile).
* Okunabilirlik: LUT/ışık sonrası DEC-077 ölçümleri tekrar (oyuncu/çevre, zombi/çevre, zırh biçim/bant) — eşik altına düşerse reçete düzeltilir (kullanıcı seçimi korunarak parametre ince ayarı).
* Perf: Run+AP005 + 40 zırhlı zombi: ort. ≤ 7,0 ms, temiz p99 ≤ 10 ms (SSAO + LUT dâhil); build ≤ 450 MB + lightmap ≤ 64 MB.
* Paket/sürüm değişikliği yok; LFS aynen (LUT/lightmap exr/png pointer); push yok.

3. İZLENEBİLİRLİK
DEC-068/076/077 · REQ-103/104/105 · ADR-011-4, ADR-013-2, ADR-015-1/2, ADR-017-1 · stil kılavuzu §5 · WP-025 (RT/MSAA analizi — sonraki karar) · §13 direktifi.

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Görünüm reçetesi yok (grade WP-013'te minimal). 4.2 LUT yok. 4.3 Lightmap/probe/reflection bake yok (gerçek zamanlı gölge + ambient). 4.4 SSAO yok. 4.5 Lightroom ↔ Unity eşleşme ölçümü yok.

5. UYGULAMA KAPSAMI
A. Referans kare seti (Editor aracı, tekrarlanabilir). B. Lightroom 3 aday (Adobe MCP; engel varsa C-00) + kontak sayfası + C-00'a 5 satır. C. `LutBuilder` (nötr şerit üretimi + Lightroom reçetesi uygulama akışı + ΔE ölçüm aracı). D. Volume profili v2 (Color Lookup, tonemap/exposure/sis), gölge/SSAO, `tools/bake-lighting.sh` (lightmap + probe + reflection; batchmode), World_AP005 ışık nesneleri. E. Ölçüm: perf (temiz pencere), DEC-077 tekrar, ΔE, bake süresi/boyut. F. Görsel: 8 kare önce/sonra + Lightroom hedef ↔ Unity yan yana; AD tek yargı; C-00'a final kontak. G. Belge: planning/04 WP-029 + ADR-029-1 (görünüm reçetesi/LUT tek kaynak) + ADR-029-2 (bake politikası); stil kılavuzu §5.2 ışık reçetesi; prompt kaydı; Assets/_Art/Lighting/README.md.

6. DEĞİŞTİRİLMEYECEK KONULAR
DEC-076 palet kararı (reçete içinde kalır); MSAA/HDR (WP-025 sonrası); oynanış/UI kodu; VFX (S-07); Packages; Unity/URP sürümü; planning karar metinleri.

7. TEST MATRİSİ
EditMode: LUT boyut/format asset-gate, ΔE aracı, bake betiği çıktı testi (lightmap/probe varlığı), volume profili alan testi. PlayMode (Metal): perf temiz pencere, DEC-077 tekrar. Build/smoke: release build bir kez (URP asset/QualitySettings değişti) + e1/e4; restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase → tek commit (paket yolları, LFS doğrulama), mesaj "WP-029: M3 lighting & look pass — Lightroom look recipe → LUT, tonemap/fog/shadow/SSAO, lightmap & probe bake" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır. C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Kullanıcı aday seçimi gelmeden LUT finalize edilmez (teknik hazırlıkla bekle, durma değil) · perf bütçesi SSAO/bake ile tutmuyorsa (düşür, raporla) · Adobe hem oturumda hem C-00'da çalışmıyorsa (yerel eşdeğer: URP grading ile 3 aday, raporla).

10. DEFINITION OF DONE
3 aday sunuldu, kullanıcı seçti · LUT üretildi, ΔE ≤ 6 · volume v2 + gölge/SSAO · lightmap/probe/reflection bake betikle, boyut bütçede · DEC-077 ve perf notta · AD yargısı · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-029-1/2 + stil kılavuzu §5.2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
