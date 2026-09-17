# WP-038 — World v4: Real Ground/Route Plates, Water v2, Build Footprint

> Yürütme promptu (C-00 dağıttı, 2026-09-17 ~03:00, cross-session mesaj; "ayrı prompt bekleme, WP-029 tesliminden sonra doğrudan başla") — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-038.

C-00 → S-05 · AP-015P TESLİM (P-05): `asset-production/AP-015/` — manifest `exports/AP015-surfaces-manifest-v001.json` (dd-surfaces-v1, 74 FBX 8,1 MB: 9 zemin plakası 5 bölge LOD0/LOD1/COL + 4 dış bant, 49 rota şeridi routes[] birebir z 0,010–0,016, 27 kerb, 13 kaldırım, 3 su ağı; su v2 girdileri 7 PNG; sevk ≈4,3 MB); yükseklik sözleşmesi ve `DD/GroundBlend` / `DD/Water v2` parametreleri README'de. Sıran: WP-020 → WP-029 (Lightroom B) → **WP-038** (aşağıda) — ayrı prompt bekleme, WP-029 tesliminden sonra doğrudan başla.

**Oturum:** S-05 · **Model:** Opus 5 teknik; su/zemin görünüm kararları için Fable AD tek tur · **Kredi:** 0 · **Taban:** o anki main · **Slot:** FIFO.

## Kapsam
A. **Plakalar:** AP-015P identity import (Import Contract v1, `Assets/_Art/Environment/Ground/AP015/`), stand-in zemin + `ROUTE_SURFACES` + Unity havuz tabanları kalkar (DEC-078 kapanışı → planning/00 satırı "AP-015P ile kapandı", karar değişikliği değil); `DD/GroundBlend` shader (vertex maske R earth/G gravel/B stone/A ıslak, `_GravelTint` 0,60, wet smoothness ≤ 0,60, ADR-015-1 Ground dokuları); NavMesh yeniden bake (WP-030 bölge profilleri/boğaz kuyruğu korunur; rise/ayak ışını 0,4/1,2 m testleri); 5 bölge + 90 m kadraj karşılaştırması (WP-017 kalıbı) + DEC-077 ölçümü.
B. **Su v2:** `DD/Water v2` (P-05 parametreleri: deep/shallow sRGB, fade 0,6 m, Fresnel 4, normal 4 m/1 m hız 0,03/0,06, akış cw 0,04 m/s, köpük/parıltı/kostik/halka), 3 koi havuzu kıyı ağı; DD_Water_v1 devre dışı (dosya kalır).
C. **Build ayak izi (ölçüm + görsel kayıpsız kazanç):** WP-039 dev build raporu: Textures 193 MB (%65), Meshes 92 MB (%31), Sounds 7 MB. Uygula: Unity mesh compression (Medium; skinned'da normal/tangent hassasiyeti testli), `MetallicSmoothness`/`DetailMask` türev dokularını 1024² sınıfına (DEC-077 zırh/zombi okunabilirlik ölçümü korunursa), 2048² Ground yalnız earth/gravel/stone tile; release boyutu önce/sonra tablosu → tavan kararı (453 > 450; kullanıcıda, öneri 500) ne olursa olsun düşüş raporlanır. ADR-015-1 v4 satırı.
D. **SurfaceTag:** S-08 WP-021C prefab etiket kuralını plakalara uygula (stone/earth/gravel/wood/water/bridge).
E. Devirler kapanışı: WP-014/017 "çakıl şeritleri/güney havuz spekülerleri/su placeholder" satırları; AP-017 (P-05) placement delta v004 gelirse aynı pakette değil, WP-038B.

## Teslim
Değişen modül testleri + NavMesh/placement testleri + perf temiz pencere (S-10 kalıbı) · tek commit, ≤15 satır not, rebase yok; planning/04 yalnız kendi bölümün + ADR-038-1.

## Ekler (C-00, aynı gün)
- KULLANICI ONAYI: **M3 release build tavanı 450 → 550 MB** (2026-09-17) → WP-038'de ADR-015-1/2 v4 + planning/00 DEC-086 satırı (S-02 081–084, S-04 085 sonrası; erken girersen referans + metin hazır) + `tools/build-report.sh --cap` varsayılanı 550 (S-01'e tek satır). Ayak izi indirimi (mesh compression, türev dokular 1024²) yine WP-038 C'de ölçülür.
- HIZ MODU (kullanıcı ~03:20): slot v3 (3 Unity), slot beklerken sonraki paketin Unity'siz işi, §6 yalın zincir (tek build, perf kirli damga), teslimde rebase yok (C-00 integrate.sh). AP-015P plakaları + su v2 + AP-017 (prop/breakable delta v004, P-05'te) WP-038'de birlikte girecek.
- Taban: WP-029 dalı a516849 (= main b497a6f + WP-029) — WP-029 entegrasyonundan sonra cherry-pick.
