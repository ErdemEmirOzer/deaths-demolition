# WP-035 — Achievements: Run Results/Economy Bindings, Cosmetic Titles & Badges, Weapon Unlocks

> Yürütme promptu (C-00, 2026-09-17, kullanıcı kararıyla doğrudan dağıtım) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-035; sözleşme `WP-035-COSMETICS-CONTRACT.md`.

**Oturum:** S-04 · **Model:** Opus 5 (teknik) · **Kredi:** 0 · **Taban:** main `48292245` (yeni worktree `wp-035/`) · **Slot:** `tools/slot.sh acquire unity wp-035` (≤2 Unity; şu an 2/2 dolu — kuyruk), swap >12 GB ise Unity açma.

## Kapsam
A. **Run Results/ekonomi bağlantıları:** `RunTracker` → `RunResultsData` alanlarının tamamı gerçek koşuda dolar (altın kazanımı/harcaması, upgrade sayısı, Special Weapon kullanımı, tier öldürmeleri, en iyi round); `AchievementEngine` ekonomi olaylarına (WP-008 DEC-069..075) bağlanır; exactly-once profil yazımı (WP-009H) korunur.
B. **Kozmetik unvan/rozet uygulaması (veri katmanı):** unlock kataloğundaki unvan/rozet/skin (AP-006 3 skin: skin 2 = 2b) → `ProfileCosmetics` (seçili unvan/rozet/skin, şema v4 alanı; göç testi) + `IAchievementProgressSource` (ilerleme yüzdesi, son açılan, "yeni" bayrağı). UI ekranları S-09'un (WP-022C/D) — `Assets/_Prototype/UI/` dosyalarına dokunma; arayüz sözleşmesini `planning/work-packages/WP-035-COSMETICS-CONTRACT.md`'ye yaz.
C. **Silah unlock girdileri:** WP-034 roster'ından (`WeaponDefinition` A/B/C + D verisi) unlock kataloğu girdileri (profil seviyesi açılışı roster verisine göre; sıra kararı kullanıcıda — mevcut veri değerlerini değiştirme, katalog yalnız referans verir); dükkân dal kapısı ile çelişki testi.
D. **AchievementProgress veri kaynağı** WP-022'nin Results/Achievements paneli için hazır (S-09'a bildirim notu).

## Kapılar
Değişen modül testleri (Achievements/Profile/Economy + roster çelişki testi) · şema v4 göç testi (gerçek profil tabanı bozulmaz; test profili per-process) · tek commit, ≤15 satır not (arayüz sözleşmesi ekiyle), rebase yok. Planning/04 yalnız kendi bölümün. `ProjectIntegrityTests.cs` S-01'in.

---
Not (S-04, teknik kararlar): skin seçimi profile yazılmadı (DEC-039 oturum hafızası korunur; `ICosmeticsSource.Skins` oturum seçimini `SessionSkinProvider` ile yansıtır). Gold/harcama profile girmez (REQ-018, WP-009 sınır testleri) — ekonomi alanları Results modelinde özetten okunur. Şema v4 versiyon numarası artmadı: yeni alanlar isteğe bağlı anahtarlar.
