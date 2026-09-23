# WP-068 — UI v3 · Faz A denetimi (ekran/durum envanteri)

**Sahip:** S-09 (lider) · **Taban:** main `75ce517` · **Dal:** `wp-068-ui-v3` · **Tarih:** 2026-09-24
**Kullanıcı talebi (aynen):** "UI/UX elementleri daha derinlikli, kaliteli, AAA düzeyinde ve çok daha çeşitli ve animasyonlu elementler içersin. KADRO sistemindeki ajanlar aralarında tartışarak tüm oyun çapında genel UI iyileştirmesi ve cilası yapılacak. Hiçbir şeyi atlamadan detaylıca uygulansın."

Bu belge tasarım konseyinin (Faz B) ortak girdisidir: **ne var, nerede, hangi kanıtla, hangi açık sorunla, v3'te hangi fırsat var.** Çözüm önermez; soruları sorar.

## 0. Kaynaklar

| ne | yol |
|---|---|
| UI kodu (tek sahip S-09) | `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/UI/Runtime/` — `Kit/` (tokenlar, fabrika, hareket, girdi, ses, ayarlar), `Screens/` (6 ekran), `Flow/` (sahne akışı, yakalama ajanı), `Presentation/` (view-model'ler), `Navigation/` |
| UI sanatı | `Assets/_Prototype/UI/Art/Resources/UI/AP012` (ikon 32 + durum 16 + çerçeve 12 + menü plakası + skin portreleri + HUD işaretçisi), `AP013` (silah glifleri), `AP014` (Special glifleri), `UiTheme.asset` |
| Paket kaynakları | `~/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-012/` (README, manifest, `tools/build_atlas.py`, `build_v003.py`, Higgsfield defteri) |
| AD kararları | `asset-production/AP-012/06-renders/AP012-art-verdict-v001…v007.md` (her birinde "Uygulama kaydı") |
| Stil kılavuzu | `Assets/_Art/StyleGuide/style-guide-v1.md` (DEC-076 palet; §2.2 ayrılmış oynanış renkleri; §2.4 rezerv kuralı) |
| Kararlar | `planning/00-PROJECT-PLAN.md` DEC-076 (palet/hava), DEC-077 (okunabilirlik), DEC-084 (Look B LUT), DEC-087 (Ö-6a 2 kart/round), DEC-088 (8,0 ms bütçe) |
| Mevcut kareler (17.09, Player 1920×1080 / 2560×1440 / 2560×1080) | `~/Desktop/KADRO/context/projects/deaths-demolition/wp-022/evidence/captures-{b,c,c-v2}`, `wp-022d/evidence/captures-{d,d-v2,d-v3,e,f,f-v2,g,g-v2,h,l}` (bot HUD kareleri `captures-f`, `captures-l/bot`); `wp-022m/display-*.png` (23.09) |
| Yakalama araçları | `-ddUiCapture mainmenu\|settings\|selection\|pause\|results\|profile\|achievements\|statistics\|controls\|accessibility\|display\|hud` (`UiCaptureAgent`), `wp-068/tools/ui-captures.sh`, `bot-hud-captures.sh` |

Not: 17.09 kareleri Look B (DEC-084, WP-029) ve World v4 (WP-038) öncesi arka planları gösterir; UI katmanı o tarihten beri yalnız WP-022H…M ile değişti. Güncel taban kareleri C-00 Player penceresi verince eklenecek (§6).

## 1. Sistem katmanı (tüm ekranların altındaki kit)

| # | öğe | bugün | kanıt / açık | v3 sorusu |
|---|---|---|---|---|
| K1 | Token sistemi `UiTokens` | renk (Presentation A; `Palette` Default / ColorBlindSafe), tip ölçeği (`UiType` H1…Caption, NumericL), boşluk (`UiSpace` 4-tabanlı), yarıçap, `TextScale`, `FlashScale`, sıralama katmanları; WCAG tablo testleri (EditMode) | AD v004–v006 SHOULD: iki yeşil, sakura+gold istisnası; CB paletinde gök mavisi "jenerik", amber ↔ para altını ΔL* ≈ 5 | token v3: malzeme (lake/varak/washi) ve derinlik (gölge/yükselti) tokenları, hareket tokenları |
| K2 | Tipografi | tek yüz: LiberationSans SDF (TMP varsayılanı); `UiFonts` proje yüzü yuvası (`UI/Fonts/UiPrimary SDF`) boş | **serif/küçük-büyük harf başlık yüzü = açık kullanıcı kararı** (AD v004+ SHOULD-1) | başlık/rakam/gövde üçlüsü; lisans (OFL); TMP atlas bütçesi |
| K3 | Fabrika `UiFactory` | Label, Numeric (autoSize kapalı HUD), Chip, Button (Primary/Secondary/Danger + seçim), Bar, FramedCard, Divider, Heading | AP-012 `Frame.Chip` ve `Ornament.Divider` hâlâ kullanılmıyor (README bilinen sorun 1) | bileşen kütüphanesi v3: kart katmanları, sekme, liste satırı, stepper, anahtar, toast, tooltip, modal, ilerleme halkası |
| K4 | Hareket `UiMotion` | `UiPulse`, `UiDrift` (menü yaprakları), `UiScreenFade` (ekran girişi), `UiStaggerIn` (liste girişi), `UiMotionCurve`; `ReducedMotion` hepsini tutar | tek eğri ailesi; ekran **çıkışı** yok; sahneler arası geçiş yalnız karartma; UiDrift ≈1,2 ayırma/kare (uGUI rebuild, WP-022K) | hareket dili: eğri/süre ailesi, koreografi, paylaşılan öğe geçişleri, 0-ayırma tween çekirdeği |
| K5 | Buton `UiButton` | Selectable; hover/focus/pressed/disabled/selected; odak halkası + hale (gold), `SetOnClick`, submit | basma geri bildirimi yalnız renk; AD v006 MUST-8 odak görünürlüğü ayarlarda uygulandı | mikro-etkileşim: basma derinliği, mürekkep dalgası, gamepad odak geçiş animasyonu |
| K6 | Girdi glifleri | `UiInputGlyphs`/`UiPrompt`/`UiPromptDriver` (klavye kapağı / gamepad diski, şema `InputSourceRegistry` üzerinden, ADR-024-2) | — | glif sanatı (şu an metin kapak/disk), şema değişiminde geçiş animasyonu |
| K7 | Ses `UiSound` + `UiEvents` | 39 UiEvent → S-08 UI sesleri (WP-021C) | — | v3 hareket anlarına ses eşleşmesi (S-08 dilimi) |
| K8 | Ayarlar `UiSettings` | ui-settings.json: UiScale, MasterVolume, Quality, ReducedMotion, Telemetry, ColorPalette, TextScale, ReduceFlashes, DisplayMode (+pencere boyutu) | — | her yeni animasyon ReducedMotion/FlashScale/TextScale/CB paletine uymalı |
| K9 | Tema/sanat `UiTheme`/`UiArt` | AP-012/013/014 atlasları; `RosterGlyph`, `SpecialIcon`; `UiThemeBuilder.Build` (Player'da serileştirilmiş tema otorite) | `Cursor.Arrow` OS imleci olarak uygulanmadı (README 2) | imleç, malzeme dokuları (washi lif, lake, varak) |
| K10 | Sahne akışı `PrototypeUiFlow` | Boot → MainMenu → Selection → Run ↔ Pause/Upgrade → Results → MainMenu; `UiScreenFade` girişte | yükleme ekranı YOK (sahne yüklemesi karartma arkasında); S-03 WP-061J Single yüklemede ölüm→Results geçişini iptal eder (korunacak) | yükleme/geçiş ekranı (ipucu, ilerleme), sahneler arası koreografi |
| K11 | Genişlik/ölçek | `UiMaxWidth` (içerik tavanı, 21:9 yakınsama), UiScale 0,9–1,2, 1080p/1440p/21:9 kareleri | 2560 yineleme çökmesi WP-022C'de kapandı | 4K/16:10 (Mac 3024×1964) doğrulaması |

## 2. Ekranlar ve durumlar

Durum kodları: **D** = varsayılan, **B** = boş, **Y** = yükleniyor, **H** = hata, **K** = kilitli/devre dışı, **O** = odak (gamepad), **RM** = reduced motion.

| # | ekran / durum | kaynak | bugünkü öğeler | kareler | açıklar (AD/README) | v3 fırsatı (konsey sorusu) |
|---|---|---|---|---|---|---|
| E1 | **Boot / ilk kare** | `PrototypeBootstrapper`, `PrototypeUiFlow` | logo/başlık yok; doğrudan MainMenu karartmadan | — | ilk izlenim yok | marka girişi (logo/mühür animasyonu), atlanabilir, ReducedMotion'da statik |
| E2 | **MainMenu** D/O | `MainMenuScreen.cs` | AP-012 menü plakası (statik görsel, sol üçte bir kararmış), başlık + alt başlık, PLAY (vermilion Primary), PROGRESS sekmeleri (Profile/Statistics/Achievements), SETTINGS/CONTROLS/ACCESSIBILITY, sakura yaprak drift, gold odak | captures-c-v2, d, g, l | AD v004–v006 kabul; menü "statik afiş" hissi; drift tek hareket | **3B/parallax menü arka planı** (saray avlusu, yağmur, fener), katmanlı derinlik, logo animasyonu, sekme geçiş koreografisi |
| E3 | MainMenu › **Profile** D/B | `MainMenuScreen` + `ProfileViewModels` | unvan/rozet seçici (WP-022D), seviye, son koşular | captures-d | boş profil metinle anlatılır | kimlik kartı (mühür/hanko), rozet vitrin animasyonu |
| E4 | MainMenu › **Statistics** D/B | aynı | sayısal tablo | captures-d | "duvar dolusu rakam" riski | grafik/sparkline, sayaç animasyonu |
| E5 | MainMenu › **Achievements** D/K/B + sayfalayıcı | aynı | 27 başarım, kilitli/açık, NEW işareti, sayfalama | captures-d-v2/v3 | — | madalya (Medal.Common/Rare/Epic) kademesi, açılma animasyonu, ilerleme halkası |
| E6 | MainMenu › **Settings** D/O | aynı | UI scale / master volume / quality slider, DISPLAY (fullscreen/windowed), RUN TELEMETRY | captures-g, l | AD v006 SHOULD-5 slider tutamacı 40→28 px | ayar önizlemeleri, bölüm başlıkları, stepper bileşeni |
| E7 | MainMenu › **Controls** D/dinleme/override | aynı | klavye + gamepad sütunları, tuş atama (dinleme durumu), ölü bölge, nişan yardımı, rumble, sıfırla | captures-g-v2 | SHOULD-6 satır ızgarası iki sütun arasında; glifler metin | gerçek tuş/tuş takımı glif sanatı, cihaz şeması illüstrasyonu |
| E8 | MainMenu › **Accessibility** | aynı | palet (CB), metin ölçeği + canlı örnek, flaş azaltma, reduced motion + önizleme çipleri | captures-g-v2, l | SHOULD-3/4 CB mavi/amber | önizleme paneli genişletme (hareket/flaş örneği) |
| E9 | **Selection** (skin + silah) D/K/Y | `SelectionScreen.cs` | 3 skin portresi (AP-006 turnaround, stüdyo ışığı), A/B/C silah kartları (AP-013 glif, stat satırı, CORE dal karoları), SELECTED/LOCKED çipleri; D hiç gösterilmez | captures-c-v2, d | portre ışığı DEC-076 değil (README 5); kartlar düz | **3B karakter/silah turntable** önizleme, kart hover derinliği, stat karşılaştırma çubukları |
| E10 | **Run HUD** — can/regen | `RunHudScreen.cs` + `RunHudViewModel` | çerçeveli panel, kalp (düşükte çatlak kalp, v003), BarHealth, "recovery in 4.5s", düşük can kenar vinyeti | captures-f, l/bot | — | segmentli/malzemeli bar, hasar "chip" gecikmeli iz, regen dalgası |
| E11 | HUD — mermi/reload | aynı | silah glifi (AP-013), şarjör sayısı, pellet (pompalı), reload | captures-f | — | mermi sayacı animasyonu, reload halkası, boş şarjör uyarısı |
| E12 | HUD — gold | aynı | coin ikon + sayı; `GoldCollected` olayında pulse | captures-f | — | sayaç tırmanışı, altın varak parıltısı (rezerv renk korunur) |
| E13 | HUD — round/threat | aynı | round paneli, düşman sayısı/havuz, SURROUNDED/BEHIND YOU/ELITE NEARBY çipleri (v003 glifler) | captures-f, l/bot | AD v007 MUST'lar uygulandı; Surrounded/Elite karesi yok | round başı/sonu afiş koreografisi, tehdit çipi giriş/çıkış animasyonu, yön işareti |
| E14 | HUD — Special | aynı | halka + "<SW> ONLINE" afişi, kullanım satırı (FIELD 4s · 2 HIT), AP-014 glif, dünya işaretçisi (ekran kenarı okları) | captures-f, h | — | Special hazır olma "kıvılcımı", afiş ink-brush girişi, halka dolum animasyonu |
| E15 | HUD — reticle | aynı (`BuildReticle`/`UpdateReticle`) | AP-012 Reticle.Ticks/Ring, nişan noktasında | captures-f | — | isabet/yayılım/reload durumlarına göre reticle hareketi |
| E16 | HUD — isabet işareti | `Feedback/HitMarkerLayer.cs` (S-02/S-07) | ayrı katman | captures-f | UI kitinin dışında → görsel dil birliği | UI v3 token/eğri ile hizalama (S-02 dilimi) |
| E17 | HUD — hasar yönü | `Kit/UiHitDirection.cs` (`UiHitDirectionLayer`) | yön yayları | — | — | yay malzemesi, flaş azaltma uyumu |
| E18 | HUD — hasar sayıları | `Feedback/DamageNumberEffect.cs` (dünya TextMesh, S-07) | dünya uzayında sayılar | captures-f | UI tipografisinden farklı yüz | UI v3 tipografi + animasyon eğrileri (S-07 dilimi) |
| E19 | HUD — düşman durum çipleri | `Kit/UiEnemyStatusMarkers.cs` | NET/DAZED çip + v003 glif, havuzlu, 0 ayırma | — (bot karesi yok) | enemy-status karesi bekleniyor | yarı-diegetik (düşman üstü) mürekkep damgası |
| E20 | HUD — ad plakası / unvan | `RunHudScreen` (WP-022E) | oyuncu adı + unvan | captures-e | — | — |
| E21 | HUD — dünya işaretleri (pickup/beacon/gold) | `Feedback/BeaconEffect.cs` (S-07), Special işaretçisi (UI), gold pickup | beacon VFX + UI kenar işaretçisi | captures-f, h | UI/VFX işaret dili ayrı | ortak işaret dili (diegetik ışık + UI kenar oku) |
| E22 | HUD — ölüm overlay | `DamageVignetteLayer.DeathOverlayT` + HUD sönümü (WP-022J) | HUD solar, vinyet | — | Results'a geçiş kopuk hissedebilir | ölüm → Results koreografisi (yavaşlama, mürekkep sızması) |
| E23 | HUD — prompt'lar | `UiPrompt` (reload/special/pickup) | glif + fiil | captures-f | — | — |
| E24 | **Upgrade dükkânı** D/B/H/K (DEC-087: 2 seçim) | `UpgradeScreen.cs` | 3 kart (glif, dal, tier, fiyat), reroll, gold şeridi, geri sayım (380×56), "1 of 2 picks" altbilgi, OFFER LOCKED/PURCHASED/MAX TIER çipleri | captures-c-v2, f (upgrade.png) | — | kart çevirme/dağıtma animasyonu, satın almada varak damgası, fiyat sayacı, tier ilerleme pipleri |
| E25 | **Pause** D + **onay diyaloğu** | `PauseScreen.cs` | panel, RESUME/SETTINGS?/MAIN MENU, END RUN onayı (Danger) + scrim | captures-c-v2 | tek modal türü | pause arka plan bulanıklığı/desat, modal bileşeni v3 |
| E26 | **Results** D/B | `ResultsScreen.cs` + `ResultsViewModel` | koşu raporu, madalyalar, silah/upgrade glifleri, yeni başarımlar | captures-c-v2 | — | sayaç tırmanışı, madalya damgası sırası, "yeni rekor" anı |
| E27 | **Toast / başarım bildirimi (oyun içi)** | — | **YOK** (başarımlar Results/Profile'da görünür) | — | boşluk | koşu içinde kısa, dikkat dağıtmayan başarım/unlock toast'ı |
| E28 | **Tooltip** | — | **YOK** (açıklamalar kalıcı caption) | — | boşluk | stat/dal/başarım tooltip'i (gamepad: odakla) |
| E29 | **Yükleme / sahne geçişi** | `UiScreenFade` | karartma | — | boşluk | yükleme ekranı (ipucu, sanat, ilerleme), geçiş perdesi (shōji/fusuma kapanışı?) |
| E30 | **İmleç** | OS imleci | AP-012 `Cursor.Arrow` hazır ama uygulanmadı | — | README 2 | oyun imleci + hover durumu |
| E31 | Debug paneli (F1) | `Core/Debug/PrototypeDebugPanel.cs` (S-01) | geliştirici aracı | — | kapsam DIŞI (oyuncu yüzü değil) | — |

## 3. Çapraz kesit kontrol listesi (her v3 öğesi için zorunlu)

- ReducedMotion: hareket kapanır veya statik eşdeğere düşer; FlashScale: parlama/flaş ≤ ayar; TextScale 1–1,25 taşma yok; CB paleti: durum = kelime + şekil.
- Gamepad + klavye tam gezinme (odak zinciri, geri, sekme), fare hover.
- 1920×1080 / 2560×1440 / 2560×1080 (21:9) / 3024×1964 (Mac) kareleri; `UiMaxWidth` içerik tavanı.
- Performans: UI sıcak yol 0 ayırma (`MetaUiSliceTests`, `SpecialWeaponHudAllocationTests`, `UiArtSliceTests` tanıları), DEC-088 8,0 ms içinde UI payı; canvas batch / setPass artışı ölçülür (S-10 kapanışı §7 kuralıyla).
- Stil kılavuzu §2.2/§2.4 rezervleri: gold (para/Special), beacon, vermilion (CTA), uyarı/tehlike renkleri başka anlamda kullanılmaz; DEC-077 okunabilirlik.
- Oyun kuralı, denge ve metin anlamı DEĞİŞMEZ (dükkân cümleleri politika okur — WP-022L/049C2).
- UI modülü `UnityEngine.InputSystem` okumaz (ADR-024-2 parite testi).
- `Assets/TextMesh Pro/` değişmez; yeni yüz `UI/Fonts/` altına (OFL lisansı yanında).

## 4. Sahiplik haritası (dilim önerisi için girdi)

| alan | sahip | not |
|---|---|---|
| UI kit, ekranlar, HUD UI katmanı, UI sanatı içe alma | S-09 | tek yazar |
| Hasar sayıları, beacon, isabet işaretleri, UI shader/VFX (bulanıklık, varak parıltısı, mürekkep maskesi) | S-07 (+S-02 isabet işareti) | UI tokenlarını tüketir |
| UI sesleri v3 | S-08 | UiEvents eşlemesi |
| Savaş HUD geri bildirimi (hit marker, yön, reticle davranışı) | S-02 | S-09 görsel dili sağlar |
| 3B menü/seçim sahnesi (gerekirse) | S-05 (sahne) + S-06 (turntable anim) | S-09 kamera/UI katmanı |
| Varlık üretimi (Higgsfield/Adobe) | S-09 (UI sanatı) / P-05, P-06 (3B) | tavan C-00 onayıyla |

## 5. Bilinen açık kullanıcı kararları

- Serif/küçük-büyük harf başlık yüzü (AD v004 SHOULD-1'den beri açık) — konsey öneri sunar, karar kullanıcıda.

## 6. Güncel taban kareleri (main 75ce517, 2026-09-23 22:05–22:17Z, S-10 penceresi)

`~/Desktop/KADRO/context/projects/deaths-demolition/wp-068/evidence/baseline/` — 10 ekran × 1920×1080 / 2560×1440 / 2560×1080 (mainmenu, settings, controls, accessibility, profile, statistics, achievements, selection, pause, results; 30/30 exit 0) + `bot/` (hud-r1, hud-r4, threat, low-health, special-ground, upgrade; bot 8 round öncesi öldü → r8/enemy-status yok). Konsey için küçük JPEG'ler: `wp-068/council/baseline-small/`.

Taban karelerinden yeni bulgular:
- **E11 kusur:** HUD silah panelinde dal satırı "P1 / FR1 / RD1" tür satırı "RIFLE · HITSCAN" ile çakışıyor (bot-hud-r4, 1080p) — v3'te düzeltilecek.
- E2: ana menü arka planı artık Look B saray fotoğrafı (AP-012 plakası); yaprak drift ve profil paneli aynı katmanda, derinlik yok.
- E10–E13: köşe panelleri aynı çerçeve/ağırlıkta; bilgi önceliği (can vs round vs gold) görsel olarak eşit.
