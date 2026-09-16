# WP-035 — Kozmetik ve Başarım İlerlemesi Veri Sözleşmesi (ADR-035-1) — S-04 → S-09 (WP-022C/D)

Tüm arayüzler `DD.Prototype.Achievements` (ilerleme/kozmetik) ve `DD.Prototype.Profile` (Results) ad alanında; UI yalnız okur ve seçim çağırır, hiçbir sayıyı yeniden hesaplamaz.

## 1. Run Results — `IRunResultsSource` (WP-023, ADR-023-1) + WP-035 ekleri
`ProfileService.ResultsSource.Current` → `RunResultsData`: koşu (round/tamamlanan/süre/silah/build/bitiş/KilledBy), kill dökümü (toplam + Standard/Special/Elite/Ultra + `HighestTierKilled` + zırh), feat'ler (`SpecialWeaponPickups`, `FlawlessRounds`), **ekonomi** (`GoldEarned`, `GoldSpent`, `GoldSpentOnUpgrades`, `GoldSpentOnRerolls`, `GoldUnspent`, `UpgradesPurchased`, `RerollsUsed` — hepsi WP-008 özetinden, profile yazılmaz), XP dökümü, seviye (önce/sonra/ilerleme/gereksinim), ömür boyu anlık görüntü + `IsNewHighestRound`/`IsBestRunXp`, `Applied`/`Outcome`/`RecordNotice`, `NewAchievements` (id/başlık/açıklama/nadirlik), `NewUnlocks` (id/tür/başlık/detay — tür artık `Title|Badge|Indicator|Weapon|Branch|Cosmetic`). Kaydedilmeyen koşuda ödül listeleri boş.

## 2. Başarım ilerlemesi — `IAchievementProgressSource`
`ProfileService.AchievementProgressSource`:
- `Entries` (katalog sırası): `AchievementProgressEntry { Id, Title, Description, Rarity, Scope, Earned, IsNew, Current, Threshold, Progress01 }` — `Current` ömür boyu sayaç (eşiğe kırpılmış; kazanılmışsa = eşik; tek koşu kapsamında kazanılmadıysa 0), `Progress01 = Current/Threshold`.
- `EarnedCount`, `TotalCount`, `NewCount` (son bakıştan beri kazanılan), `LastEarnedId`.
- `MarkAchievementsSeen()` — pane açılınca çağır; "yeni" bayrakları söner, sonraki save ile kalıcı (`ProfileData.AchievementsSeen`).
Listeler her erişimde canlı profilden kurulur: girdileri tut, kaynağı tutma; `ProfileService.Changed` sonrası yeniden oku.

## 3. Kozmetikler — `ICosmeticsSource`
`ProfileService.CosmeticsSource`:
- `Titles`, `Badges`: `CosmeticEntry { Id, Kind, Title, Detail, Unlocked, IsNew, Selected }` — katalogdaki `Title`/`Badge` türü girdiler (`unlock-catalog.json`); `Unlocked` = `ProfileData.UnlockedContent` içinde; `Selected` = profilin seçimi.
- `Skins`: AP-006 paletleri (`PlayerSkins.All`, id = `PlayerSkin` adı; skin 2 = 2b AP-006 v003), **her zaman `Unlocked`** (DEC-018); `Selected` oturum seçimini yansıtır (DEC-039: kalıcı değil) — UI akışı `ProfileService.SessionSkinProvider = () => PrototypeUiFlow.SelectedSkin` atar (S-09).
- `SelectedTitle`, `SelectedBadge` (boş = yok); `SelectTitle(id)` / `SelectBadge(id)` → bool (açılmamış/yanlış tür/salt-okunur oturum → false, seçim değişmez; boş id temizler; başarıda profil kaydedilir ve `Changed` yayınlanır).
- `NewCount` (son bakıştan beri açılan içerik: unvan+rozet+gösterge), `MarkContentSeen()`.
Profil alanları (şema v4, versiyon artmaz; eski v4 dosya anahtarsız yüklenir): `SelectedTitle`, `SelectedBadge`, `AchievementsSeen`, `ContentSeen`. `Sanitize` açılmamış bir seçimi boşa çeker; kazanım listeleri değişmez.

## 4. Silah unlock girdileri (roster referansı)
`UnlockCatalog.Entries` = JSON (unvan/rozet/gösterge) + **türetilmiş** `Weapon` girdileri: `WeaponRoster.Enabled` içindeki `HasRuntimeId && UnlockKind == ProfileLevel` silahlar için `Condition = Level`, `NumericKey = ClassicWeaponDefinition.UnlockLevel(config)` (= `ClassicWeaponSettings.UnlockProfileLevel` = `ProfileService.UnlockLevelFor`). Katalog roster değerini değiştirmez, kopyalamaz. Kullanıcı kararı (DEC-083, S-02 kaydeder): roster A/B/C etkin, D devre dışı; A varsayılan (açık), B/C profil seviyesiyle — katalog yalnız bunu yansıtır (D için girdi yok). Merdiven (WP-009) aynı transaction'da önce açar; katalog aynı id'yi ikinci kez "new" yapmaz. Dükkân dal kapısı (`WeaponRoster.IsBranchOffered`) silah açılışından bağımsızdır.

## 5. UI için notlar
Results'ta `NewUnlocks` türüne göre ikon/etiket (`Title`/`Badge`/`Indicator`/`Weapon`); Profile paneline unvan/rozet seçici (`SelectTitle`/`SelectBadge`) ve "yeni" rozeti (`NewCount`); Achievements paneline ilerleme çubuğu (`Progress01`) ve nadirlik; pane açılışında `MarkAchievementsSeen()`/`MarkContentSeen()`. Skin seçimi mevcut akışta kalır.
