# WP-036 — Enemy Versions Visuals: AP-014 tier siluet parçaları, tint kimliği (lerp maske shader), Ultra saldırı (AP-009B gelince), WP-030 v3 yeniden doğrulama (WP-020 gelince)

Paket promptu (C-00, 2026-09-16/17, HIZLI HAT; S-03 sahip). Kısaltılmış kayıt — tam metin C-00 mesajındadır.

- **Başlangıç:** main `f44dfdbe`. Branch `wp-036-enemy-version-visuals`.
- **Kaynaklar:** `asset-production/AP-014/` (4 tier FBX: Special hemp collar, Elite sode + backframe, Ultra yoke; 3 fit × LOD0–3 skinned; 1K tier atlas; `AP014-manifest.json` anchor/slot eşlemesi, tint tablosu lerp(base, tint, Mask·k): ash #585F64 k0,42 / plum #4A3448 k0,70 / rust #6B5140 k0,72; bilinen: LOD2/3 tri bütçe üstü, plum lake üstünde okunmuyor → lake tabanı v 0,35–0,40; Special pantolon cw1 kuralı), WP-028 kancaları, WP-016/017 kütüphaneler, DEC-077.
- **Kapsam:** A import + `ZombieTierLibrary` + gerçek mesh bağlama · B tint shader/materyal yolu lerp (çarpımsal değil), lake tabanı, tier renk tablosu, DEC-077 ölçümü · C Ultra saldırı (AP-009B gelince) · D WP-020 v3 yeniden doğrulama · E testler (import gate, slot/fit/LOD, tint tablosu, okunabilirlik, 40 zombi perf) + release build/e1/e4 · F planning/04 + ADR-036-1 + AP tablosu + prompt kaydı.
- **Sınırlar:** DEC-021 tabloları; AP-014 kaynak dosyaları; Special Weapon modelleri (S-02 WP-037); sahne (S-05). S-05 shader sözleşmesi: yeni shader varyantı, S-05 dosyasına dokunmadan (tek satır uzlaşı: `ArtImportContract` doku rolü `zombie_tier`).
