# WP-034 — CLASSIC WEAPON ROSTER (prompt kaydı, C-00 dağıtımı 2026-09-16, HIZLI HAT)

Sahip S-02 (tek writer). Taban main 0eb43922. Danışılan: S-04 (unlock kataloğu verisi; S-04 dosyalarına dokunma), S-09 (seçim verisi), S-07 (VFX kimlikleri), S-08 (SFX kimlikleri), P-05 (AP-013 tüketici). Makine: slot.sh. Dosya sahipliği: Weapons/ (Special hariç), Core/Weapons verisi, kendi testler, planning/04 kendi bölümü, prompt kaydı, `WP-034-WEAPON-VISUAL-CONTRACT.md`.

Kaynaklar: WP-024 öneri notu (A tüfek / B karabina / C pompalı / D ağır tabanca; dal ilkesi (a); Chain/Ricochet pompalıya verilmez; açılış (a) A açık, B/C profil seviyesiyle DEC-008), DEC-005/008/037/039, REQ-017/050/051/059, WP-002 `ClassicWeaponRuntime`/`HitscanEngine`/`BuildShot`, WP-004/008 `UpgradeBranch`, WP-005 seçim ekranı, WP-023 unlock kataloğu şablonu, AP-006 `socket_weapon_r` (1,37 m), AP-010 namlu/vuruş sheet'leri. Kullanıcı notu: silahlar sonda birlikte revize edilecek.

Amaç: silah listesini veri-güdümlü yapmak (tanım asset'leri, dal setleri, açılış sırası, seçim verisi), pellet başına dal çözümleme bulgusunu sözleşmeye bağlamak, AP-013 görsel/soket/efekt/ses sözleşmesini yazmak — karar gelince yalnız veri değişir.

Bağlayıcı kararlar: `ClassicWeaponDefinition` (id, tür sınıfı, siluet sınıfı, atış modeli, PrototypeOnly değerlere erişim, çekirdek dal ×3, ortak oyuncu dalları, açılış koşulu, soketler `socket_weapon_r` + `muzzle` + `ejection`, VFX/SFX/HUD kimlikleri) — varsayılan A/B/C + D `enabled=false`; pellet çarpanı sözleşmesi (Piercing pellet başına, Explosion pellet-normalize, Chain/Ricochet atış başına) testle; `IWeaponRosterSource` (S-09); unlock girdileri S-04 kataloğuna veri (JSON); Special Weapon değişmez (REQ-059).

Kapsam A–F; sınırlar: hasar/tempo değerleri, Special Weapon, UI dosyaları, unlock kodu, Packages. Test/Build: Player kodu değişti → release build + e1/e4 (slot); restore. Teslim: tek commit "WP-034: classic weapon roster — data-driven definitions, per-weapon core branches, selection/unlock data, AP-013 visual contract"; C-00'a ≤15 satır; ff-merge + etiket.
