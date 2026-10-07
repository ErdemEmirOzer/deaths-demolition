# EVENT-COVERAGE-MATRIX — UI satırları (WP-022B, S-09)

Direktif §12 tamlık kuralı: her UI etkileşiminin sesi ve efekti. Bu dosya `EVENT-COVERAGE-MATRIX.md`'nin (S-08/S-07) **UI bölümüdür**; S-08 matrisi main'e geldiğinde satırlar oraya taşınır, olay adları değişmez. Yayın sözleşmesi: `UiEvents.Raise(UiEvent, detail)` (`Assets/_Prototype/UI/Runtime/Kit/UiEvents.cs`); S-08 `UiEvents.Raised` olayına abone olur ve `AudioEventSource.Ui` bağlamalarını `UiEvent.<ad>` anahtarıyla ekler (mevcut dört `PrototypeAudio.*` bağlaması `UiSound` üzerinden aynen sürer). `UiEventsTests` bu tablo ile `UiEvent` enum'unu birebir tutar (eksik/fazla satır = FAIL).

> **WP-021C (S-08):** ses sütunu `EVENT-COVERAGE-MATRIX.md` §8'e taşındı ve orada nihaidir (bağlı satırlar AP-011 v005 türetilmiş kimlikler, sahibi-başka satırlar tek ses); bu tablo `UiEvent` enum sözleşmesi olarak (`UiEventsTests`) aynen kalır.

Not (WP-022F): aktif cihaz değişimi (`UiInputScheme.Changed`) bir UiEvent değildir — sessiz, yalnız istem glifleri yeniden çizilir (`UiPrompt`).

Sütunlar: olay adı · ne zaman · SFX (AP-011 ses olayı adı; S-08 doldurur, "n/a" gerekçeli) · VFX / mikro-animasyon (UI tarafı, mevcut) · kaynak.

| UiEvent | Ne zaman | SFX (AudioEventSource.Ui anahtarı) | VFX / mikro-animasyon | Kaynak |
|---|---|---|---|---|
| FocusMoved | Odak başka kontrole geçti (klavye/pad/fare) | `UiEvent.FocusMoved` → `UiNavigate` (mevcut `PrototypeAudio.Navigate`) | Odak halkası (BorderFocus + halo) anında; hover yıkaması `MotionFast` | `UiSound.Play(Move)`, `UiButton` |
| ButtonHover | İşaretçi kontrol üstüne geldi | `UiEvent.ButtonHover` → hafif tık (AP-011, S-08 seçer) | Krem yıkama %8 `MotionFast` | `UiButton.OnPointerEnter` |
| ButtonPress | Etkin kontrol basıldı | `UiEvent.ButtonPress` (Confirm ile aynı kare; S-08 birini seçer) | Basılı dolgu koyulaşır (`AccentPrimaryPressed`/`SurfacePanel`) `MotionInstant` | `UiButton.Activate` |
| Confirm | Eylem kabul edildi | `UiConfirm` (mevcut) | — (basılı dolgu) | `UiSound.Play(Confirm)` |
| Back | Geri / iptal | `UiBack` (mevcut) | Ekran değişimi fade (`UiScreenFade`) | `UiSound.Play(Back)`, Selection/Results/MainMenu Cancel |
| Denied | Kilitli/erişilemez kontrol | `UiDenied` (mevcut) | Sabit (yıkama yok); durum çipi "LOCKED / TOO EXPENSIVE" | `UiButton.Activate` (interactable=false) |
| ScreenEntered | Ekran kuruldu (MainMenu/Selection/Run HUD/Results/Pause/Upgrade) | `UiEvent.ScreenEntered` + detail=ekran adı → geçiş vuruşu (AP-011) | Canvas fade 0→1 `MotionScreen` 0,28 s (reduced motion: anında) | `UiScreen.Awake` |
| ScreenLeft | Ekran yok ediliyor | n/a (bir sonraki ScreenEntered taşır) | — | `UiScreen.OnDestroy` |
| PaneOpened | Ana menü paneli (Profile/Statistics/Achievements/Settings); başarım sayfası çevrildi (detail AchievementsPage, WP-022D) | `UiEvent.PaneOpened` → hafif kâğıt sesi (AP-011) | Panel yeniden kurulur; seçili sekme raised + krem kenar + krem tik | `MainMenuScreen.ShowPane`, `TurnAchievementPage` |
| ChoiceSelected | Skin / silah kartı seçildi; profil unvanı / rozeti seçildi (WP-022D) | `UiEvent.ChoiceSelected` (detail Skin/Weapon/Title/Badge) → seçim tık | Kart raised + SELECTED rozeti; seçenek satırında krem işaret | `SelectionScreen.SelectSkin/SelectWeapon`, `MainMenuScreen.AddCosmeticOption` |
| SliderChanged | Ayar kaydırıcısı değişti | `UiEvent.SliderChanged` → tik (adım başına) | Okuma metni yerinde güncellenir | `MainMenuScreen.AddSettingSlider` |
| ToggleChanged | Reduced motion / run telemetry (WP-022E) açık/kapalı | `UiEvent.ToggleChanged` (detail ReducedMotion/Telemetry) → tık | Düğme etiketi ON/OFF | `MainMenuScreen` Settings |
| DialogOpened | Pause → "koşuyu bırak" onayı açıldı | `UiEvent.DialogOpened` → kısa vurgu | Alt katman `OpacityScrimUnderDialog`, odak diyaloğa | `PauseScreen.OpenConfirm` |
| DialogClosed | Onay kapandı | `UiBack` (mevcut) | Odak iadesi | `PauseScreen.CloseConfirm` |
| ScreenExiting | Ekran çıkışı başladı (Exit 140) | `UiTransitionOut` (v015) | ExitScreen | WP-068 v3 kit (S-09 dilim 1+) |
| CurtainClosed | Perde kapandı, `LoadScene`'den ≥ 1 kare önce | `UiCurtainClose` (v015) | UiCurtain kapan 280 | `UiCurtain` (S-09 dilim 10) |
| CurtainOpened | Yeni sahnenin ilk render karesi | `UiCurtainOpen` (v015) | UiCurtain aç 280 | `UiCurtain` (S-09 dilim 10) |
| PressDown | Primary/Secondary pointer-down | `UiPressDown` (v015) | PressDepth | `UiButton` v3 (S-09) |
| BootLogo | Boot mührü teması (U7, her açılış) | `UiBootSeal` (v015; atlamada 120 ms fade) | InkBleed + StampIn | Boot (S-09 dilim 10) |
| TimelineSkipped | Kahraman çizelgesi atlandı (`Complete()`) | n/a — kontrol: UI döngüleri 30 ms, boot 120 ms fade | cue'lar son cue'ya çöker | `UiMotion` zaman çizelgesi (S-09) |
| HudRoundStarted | Round başladı | `UiEvent.HudRoundStarted` (Run.RoundStarted ile aynı olay; S-08 tek kaynak seçer) | "ROUND N" banner 2,4 s | `RunHudScreen.OnRoundStarted` |
| HudRoundCompleted | Round temizlendi | `UiEvent.HudRoundCompleted` | "ROUND COMPLETE" banner | `RunHudScreen.OnRoundCompleted` |
| HudPoolExhausted | Havuz bitti, harita temizleniyor | `UiEvent.HudPoolExhausted` | Banner | `RunHudScreen.OnPoolExhausted` |
| HudLowHealthEntered | Can eşiğin altına indi (`LowHealthFraction`) | `UiEvent.HudLowHealthEntered` → kalp/uyarı vuruşu | Can çubuğu + değer + kalp glifi `BarHealthLow`/`StateDanger` | `RunHudScreen.ApplyHealth` |
| HudLowHealthLeft | Can eşiğin üstüne çıktı | n/a (sessiz; görsel yeterli) | Renk geri döner | `RunHudScreen.ApplyHealth` |
| HudMagazineEmpty | Şarjör boş | `UiEvent.HudMagazineEmpty` (DryFire ile çakışmamalı; S-08 seçer) | "MAGAZINE EMPTY · RELOAD" `StateDanger` | `RunHudScreen.ApplyWeaponFields` |
| HudSpecialSpawned | Special Weapon yere düştü | n/a (beacon spawn sesi dünya tarafı) | Dünya işareti + yer-fazı pip blink (GroundBlinkClock) | `RunHudScreen.OnSpecialSpawned` |
| HudSpecialPickedUp | Special alındı | n/a (pickup sesi dünya tarafı) | Radyal halka dolu, sayaç, SUSPENDED çipi | `RunHudScreen.OnSpecialPickedUp` |
| HudSpecialFinalSeconds | Son saniyeler (REQ-058) | `UiEvent.HudSpecialFinalSeconds` → uyarı vuruşu (görsel+işitsel) | Kenar vinyeti pulse + "SPECIAL WEAPON ENDING" banner + halka `StateDanger` | `RunHudScreen.OnSpecialFinalSeconds` |
| HudSpecialEnded | Special bitti | n/a (dünya tarafı) | "CLASSIC WEAPON RESTORED" banner | `RunHudScreen.OnSpecialEnded` |
| HudSpecialExpired | Yerde süresi doldu | n/a | İşaret kalkar | `RunHudScreen.OnSpecialGroundExpired` |
| HudThreatEncircled | Kuşatıldı (WP-033) | `UiEvent.HudThreatEncircled` → tehdit vuruşu | "SURROUNDED" çipi (kafatası glifi + metin, `StateDanger`) | `RunHudScreen.UpdateThreat` |
| HudThreatRearAttack | Arkadan saldırı | `UiEvent.HudThreatRearAttack` | "BEHIND YOU" çipi (aşağı chevron + metin) | `RunHudScreen.UpdateThreat` |
| HudThreatStrongVersion | Elite/Ultra yakında | `UiEvent.HudThreatStrongVersion` | "ELITE/ULTRA NEARBY" çipi (`StateWarning`) | `RunHudScreen.UpdateThreat` |
| HudThreatWindup | Yakında saldırı hazırlığı sayısı arttı | `UiEvent.HudThreatWindup` (S-08: zombi windup sesi zaten var → n/a olabilir) | — (dünya tarafı telegraf) | `RunHudScreen.UpdateThreat` |
| HudDamageEdge | Oyuncu hasar aldı, kenar yandı | n/a (PlayerDamage sesi Damage kaynağında) | Yönlü kenar flaşı 0,92 α, 0,06 s tut + 0,42 s sön | `UiHitDirectionLayer.OnDamaged` |
| HudNewBestRound | NEW BEST ROUND şeridi koşu içinde (U6) | `HudNewBestRound` (v015; çip varken ≤ 3 s bekler) | RibbonIn | `RunHudScreen` (S-09) |
| HudEliteDown | ELITE DOWN damgası | `HudEliteDown` (v015; duck yok) | 0,6 s damga | `RunHudScreen` (S-09) |
| HudUltraDown | ULTRA DOWN damgası | `HudUltraDown` (v015) | damga + mürekkep | `RunHudScreen` (S-09) |
| UpgradePurchased | Upgrade satın alındı | `UiEvent.UpgradePurchased` → satın alma sesi | Kart PURCHASED çipi, gold şeridi güncellenir | `UpgradeScreen` (TryPurchase) |
| UpgradeRerolled | Teklif yenilendi | `UiEvent.UpgradeRerolled` → kart karıştırma | Üç kart yeniden çizilir | `UpgradeScreen` (TryReroll) |
| UpgradeOfferLocked | Round başına tek satın alma → teklif kilitlendi | `UiEvent.UpgradeOfferLocked` → kilit tık | OFFER LOCKED çipleri, REROLL · LOCKED | `UpgradeScreen` |
| ResultsRevealed | Sonuç ekranında kutlanacak şey var | `UiReveal` (mevcut Reveal) | NEW rozeti pulse | `UiSound.Play(Reveal)` |
| LevelUp | Profil seviyesi arttı | `UiEvent.LevelUp` → seviye vuruşu | LEVEL UP çipi + ilerleme çubuğu | `ResultsScreen.Start` |
| UnlockRevealed | Yeni içerik açıldı | `UiEvent.UnlockRevealed` | Rare madalyon + NEW pulse | `ResultsScreen.Start` |
| AchievementRevealed | Başarım kazanıldı | `UiEvent.AchievementRevealed` | Nadirlik madalyonu (Common/Rare/Epic) + NEW pulse | `ResultsScreen.Start` |
| NewBestRound | Profil en yüksek roundu aşıldı (WP-023) | `UiEvent.NewBestRound` → rekor vuruşu | "NEW BEST ROUND" çipi pulse | `ResultsScreen.BuildPerformanceColumn` |
| SettingsChanged | Herhangi bir ayar kaydedildi | n/a (Slider/Toggle olayı taşır) | — | `UiSettings.Persist` |
| CardDealt | Kart dağıtıldı (detay "0/1/2", "last") | `UiCardDealt` (v015; +0/+2/+4 %) | CardDeal stagger 60 | `UpgradeScreen` (S-09) |
| CardFlipped | Değişen kart döndü (reroll) | `UiCardFlipped` (v015) | CardWipe | `UpgradeScreen` (S-09) |
| StampLanded | Hanko temas karesi (detay rare/epic; WP-094: detay "round" = HUD raund mührünün temas karesi, HAREKET-HUD R3) | `UiStampLanded` / Rare / Epic (v015); "round" S-08 ses noktası | StampIn | Shop / Results / Profile / Achievements (S-09); HUD raund geçişi (S-09, WP-094) |
| CounterRolling | Sayaç rulosu başladı | `UiCounterRoll` (v015; ≤ 0,6 s) | CounterRoll | `UiCounter` (S-09) |
| CounterSettled | Sayaç yerleşti | `UiCounterSettled` (v015) | Stamp Base | `UiCounter` (S-09) |
| RerollHoldStarted | Hold-to-confirm başladı (U5) | `UiHoldRub` (v015; Confirm/Back biter) | HoldFill | `UpgradeScreen` / Controls (S-09) |
