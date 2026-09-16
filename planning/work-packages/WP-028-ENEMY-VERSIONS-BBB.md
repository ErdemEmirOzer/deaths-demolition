# WP-028 — Enemy Versions: Special/Elite/Ultra B·B·B uygulaması (davranış, zırh eğilimi, siluet parçası kancaları, Ultra saldırı placeholder)

Paket promptu (C-00, 2026-09-16, HIZLI HAT; S-03 sahip). Kısaltılmış kayıt — tam metin C-00 mesajındadır.

- **Başlangıç:** main `d5c4a6a8` (`wp-027-enemy-versions-prep`). Branch `wp-028-enemy-versions-bbb`.
- **Bağlayıcı kullanıcı kararı (B·B·B, 2026-09-16):** Special = mevcut Lunge, kısa hazırlık/uzun toparlanma vurgusu (zaman ölçeği + toparlanma parametresi), +1 siluet parçası kancası · Elite = gövde zırhı garanti eğilimi (DEC-021 tablo değeri değil, tier eğilim çarpanı — yeni parametre; gold DEC-069 değişmez), +1–2 siluet parçası kancası · Ultra = fit C zorunlu + üç bölge zırh eğilimi + 1 siluet parçası; Ultra saldırı = mevcut sweep klibi placeholder. Siluet parçaları `IEnemyVersionVisual` → `TierSilhouetteSlot` (ankraj baş/omuz/sırt; AP-014 mesh gelene kadar yer tutucu primitif); tint Mask.R tier rengi. Tanıtım/oran kuralları (DEC-022, REQ-071/072) değişmez.
- **Kapsam:** A `TierBehaviourProfile` + `EnemyVersionApplier` · B spawn bileşiminde tier eğilimi + simülatör önce/sonra (Elite dō ≥ %85, Ultra üç bölge ≥ %90 teknik varsayılan) · C siluet slot/kanca + yer tutucu + tint · D Ultra saldırı placeholder (faz oranları; DEC-029 pencereleri korunur) · E testler (EditMode profil/eğilim/dağılım + DEC-021 değişmedi; PlayMode tier spawn; parite Ultra dâhil) · F planning/04 + ADR-028-1 + AP tablosu + prompt kaydı.
- **Sınırlar:** DEC-021/022 tabloları, hasar/zırh kuralları, gold, animator/visual dosyaları (S-06/S-05), UI, Packages; yan yaklaşım steering / Ultra büyük gövde YOK (sonda revizyon).
- **Test/Build:** EditMode + PlayMode; Player kodu değişti → release build bir kez + e1/e4; kullanıcı verisi restore 6/6.
- **Durma:** tier eğilimi DEC-021 tablo değişikliği gerektirirse · Ultra klibi olmadan pencere paritesi bozulursa (placeholder + not).
