# WP-027 · Tier Kimlik & Yetenek — ÖNERİ NOTU (karar kullanıcıya aittir)

Yazar: game-designer (hızlı hat) · Tarih: 2026-09-16 · Durum: **SEÇİLDİ: B / B / B (kullanıcı kararı 2026-09-16, C-00 iletti)** — Special B (mevcut Lunge + kısa hazırlık/uzun toparlanma vurgusu + 1 siluet parçası), Elite B (gövde zırhı garanti eğilimi + 1–2 siluet parçası), Ultra B (fit C + üç bölge zırh + 1 siluet parçası); sayılar kesinleşmemiştir (WP-028 uygular, AP-014 üretir); §8 açık ürün soruları (yan yaklaşım steering, Ultra büyük gövde, gold 5/10/25 ↔ DEC-069) kullanıcının "sonda birlikte revizyon" listesinde — şimdi uygulama yok · §5 = `WP-027-TIER-VISUAL-CUES.md` (concept-artist).

## 1 · Amaç ve sınırlar
- Bu not tek zombi ailesinin Special / Elite / Ultra versiyonları (DEC-009, REQ-062) için **davranış + görsel/ses işaret + maliyet** alternatifleri sunar; isim/lore önermez (`DisplayName` "Runner/Guard/Executioner" PrototypeOnly kozmetiktir, `EnemyStatSnapshot.Tier` kanonik kimliktir).
- Değişmez: DEC-021/022 tablo değerleri (tanıtım roundu 3/5/8, ağırlık 18→45 / 9→26 / 3→12, gold 5/10/25), DEC-023 tavan, DEC-029 savaş sözleşmesi (**yalnız animasyona bağlı vuruş hasar verir**), DEC-032/033 (fiziksel engel + yumuşak ayrışma), DEC-067 (fiziksel ses, doğaüstü yok), DEC-069 gold formülü.
- Mevcut saldırı stilleri (`ZombieAttackStyle` Swipe/Lunge/Sweep/Overhead) korunur; alternatifler bunların **üzerine** ne eklediğini/eklemediğini yazar. "Yeni sistem gerekir" ibaresi olan satırlar ek geliştirme paketi ister.

Mevcut PrototypeOnly profil (referans, `EnemyConfigSection.cs:222–330`): Standard 2.7 m/s · 10 hasar · HP ×1 · stagger 1 · r 0.45 · Swipe; Special 4.6 · 9 · ×0.85 · 1 · 0.40 · Lunge (telegraph 0.35); Elite 2.2 · 18 · ×2.2 · 0.45 · 0.62 · Sweep (0.8) · zırh şansı 0.45/0.5/0.35; Ultra 1.7 · 34 · ×5 · 0.12 · 0.90 · Overhead (1.15) · zırh 0.6/0.7/0.5.

## 2 · Special (tanıtım R3, hızlı/kırılgan)
| Boyut | **A — Kanat koşucusu** (mevcut Lunge + yan yaklaşım) | **B — Tempo koşucusu** (yalnız mevcut Lunge, parametre vurgusu) |
|---|---|---|
| Davranış | Lunge kalır. Ekler: hedefe düz hat yerine ±45–70° yan açıdan yaklaşma (steering offset); düşük `SeparationMass` (0.8) ile Standard kalabalığının arasından sızar (DEC-033). Eklemez: yeni saldırı, yeni animasyon. | Lunge kalır. Ekler: hiçbir yeni davranış; telegraph kısa (0.35 bandı), recovery uzun (0.55+ eğilimi) → Lunge sonrası açık pencere. Sürü etkisi: Standard ile aynı hat, önden gelir. |
| Sayısal eğilim (DEC-021 uyumlu) | HP ↓ (×0.85 bandı), hasar ≈ Standard, zırh şansı 0 kalır; hız 4.6 bandı. | HP ↓, hasar ≈ Standard, zırh 0; Lunge menzili 2.6 bandı, cooldown ↑ (1.9+). |
| Görsel işaret (kısa) | Modül alt kümesi: çıplak/ince üst (T-less veya hafif üst), H0/H1 baş; kumaş tint `M_zombie_atlas_Special` (yalnız kumaş, `_DetailMask`.a). Ayrıntı §5. | Aynı alt küme + **1 siluet parçası** (ör. `anchor_torso` üzerinde hareketle okunan hafif parça). Ayrıntı §5. |
| Ses / yön (DEC-067) | Hızlı çift-adım tempo (Standard'ın ~1.7×), Lunge öncesi tek kısa nefes/hırıltı; yan açıdan geldiği için **stereo pan** birincil yön bilgisi. | Aynı adım temposu; Lunge telegraph'ında kumaş/ayak kayma sesi; önden geldiği için yön bilgisi ikincil. |
| Okunabilirlik riski | **Orta-yüksek**: r 0.40 en küçük gövde, kalabalık içinden geçer; DEC-077 kapsül kontrastı ≥ 0.06 tek başına yetmez, hareket hızı ayırt ettirir. | **Orta**: siluet parçası + hız; önden geldiği için kalabalıkta gizlenme az. |
| Üretim maliyeti (AP-014) | **0 yeni mesh**, 0 yeni doku (mevcut 2K atlas, tint alt-dokusu mevcut). | **+1 mesh × 4 LOD** (tek siluet parçası), mevcut atlas UV'sinde yer bulunursa 0 yeni doku; yoksa AP-014 küçük ek atlas. |
| Teknik giriş noktası | `EnemyTierSettings` (mevcut alanlar) + `IEnemyVersionVisual.ApplyVersion` (modül alt kümesi kısıtı: `ZombieLookConstraints` benzeri tier kuralı — mevcut yapı, yeni kural satırı). **Yan yaklaşım steering: yeni sistem gerekir** (NavMesh hedef offset). | Yalnız `EnemyTierSettings` parametreleri + `ApplyVersion` (modül kısıtı + siluet parçası slotu: `ZombieModuleLibrary`'ye yeni modül kaydı — mevcut pipeline). Yeni sistem yok. |

## 3 · Elite (tanıtım R5, dayanıklı/engelleyici)
| Boyut | **A — Zırhlı duvar** (mevcut Sweep + zırh ağırlığı) | **B — Öncü/çapa** (mevcut Sweep + garantili gövde zırhı + siluet) |
|---|---|---|
| Davranış | Sweep kalır. Ekler: yeni davranış yok; `SeparationMass` yüksek (2.2 bandı) → Standard'lar etrafından akar, Elite koridor kapatır (DEC-032). Stagger ↓ (0.45 bandı). | Sweep kalır. Ekler: yeni davranış yok; `BodyChance` → 1.0 eğilimi (gövde zırhı her zaman), miğfer/bacak tablo şansı; kütle yüksek. Oyuncu gövde zırhını kırmadan (REQ-061/063) hasar ekonomisi uzar. |
| Sayısal eğilim | HP ×2.2 bandı, hasar ~1.8× Standard, zırh şansı 0.45/0.5/0.35 + round artışı (tablo). | HP ×2.2 bandı; **gövde zırhı garanti**, diğerleri tablo; toplam efektif HP ↑ (zırh) → ham HP çarpanı ↓ eğilimi (DEC-021 bandı içinde dengelenir). |
| Görsel işaret | AP-008 zırh lake tint `M_zombie_armor_Elite` (Mask.R) + kumaş tint; zırh varlığı/kırılması REQ-063 ile zaten okunur. Ayrıntı §5. | Aynı tint + **1–2 siluet parçası** (`anchor_torso`/omuz, AP-008 zırh kütüphanesi üzerinden). Ayrıntı §5. |
| Ses / yön (DEC-067) | Ağır tek-adım tempo, zırh şakırtısı (metal/kayış), Sweep telegraph'ında iki-kollu geri çekilme sesi; **düşük frekanslı adım** = tehdit + yön. | Aynı + gövde zırhı garantili olduğundan şakırtı sabit bir kimlik sesi olur. |
| Okunabilirlik riski | **Düşük-orta**: r 0.62, zırh siluetleri; zırhsız Elite (şans 0.2–0.55 düşmesi) Standard'dan yalnız tint ile ayrılır → risk noktası. | **Düşük**: gövde zırhı + siluet parçası her örnekte; zırhsız-Elite durumu yok. |
| Üretim maliyeti | **0 yeni mesh**, 0 yeni doku (zırh atlası + tint mevcut). | **+1–2 mesh × 4 LOD × 3 fit** (zırh kütüphanesi fit kuralı gereği), mevcut zırh atlası; UV alanı yoksa AP-014 ek atlas. |
| Teknik giriş noktası | `ArmorChanceSettings` + `EnemyTierSettings` + `ApplyVersion` (tier materyali). Yeni sistem yok. | `ArmorChanceSettings.BodyChance` tablosu + `ZombieArmorLibrary` yeni parça + `ArmorVisualBinder` slotu (mevcut). Yeni sistem yok. |

## 4 · Ultra (tanıtım R8, ağır/nadir)
| Boyut | **A — Kütle gövdesi** (mevcut Overhead + büyük gövde fit'i) | **B — Zırh yığını** (mevcut Overhead + üç bölge garantili zırh + siluet) |
|---|---|---|
| Davranış | Overhead kalır (telegraph 1.15 bandı). Ekler: yeni davranış yok; `SeparationMass` 5 bandı → sürüyü fiziksel iter (DEC-033), r 0.90. Stagger 0.12 bandı. | Overhead kalır. Ekler: yeni davranış yok; zırh şansı üç bölgede 1.0 eğilimi → oyuncu için "bölge soyma" hedef sırası (DEC-010); kütle 5 bandı. |
| Sayısal eğilim | HP ×5 bandı, hasar ~3.4× Standard; zırh tablo (0.6/0.7/0.5). | HP çarpanı ↓ eğilimi (zırh efektif HP'yi taşır), hasar aynı bant; zırh 1.0/1.0/1.0 eğilimi; DEC-069 zırh bonusu spawn snapshot'ında sabit → gold ↑ (tablo kararı). |
| Görsel işaret | Ayrı gövde ölçeği (fit D veya ölçek çarpanı), kumaş/zırh tint `_Ultra`. Ayrıntı §5. | Fit C (mevcut en büyük) + tam zırh + **1 siluet parçası** (`anchor_helmet` veya sırt). Ayrıntı §5. |
| Ses / yön (DEC-067) | Overhead telegraph'ında derin nefes + zemin darbesi (vuruşta); adım yer titreşimi düşük bant; yön: adım aralığı en uzun (1.7 m/s). | Aynı + üç bölge zırh şakırtısı; kırılan her bölge ayrı kırılma sesi (`ArmorVisualBinder.Broken` olayı mevcut). |
| Okunabilirlik riski | **Düşük**: boyut + yavaşlık; risk yalnız uzak mesafede sis bandında (52 m üstü). | **Düşük-orta**: Elite-B ile karışma riski (ikisi de zırhlı, siluet parçası ayrıştırır). |
| Üretim maliyeti | **En yüksek**: +1 gövde × 4 LOD; 19 modülün yeni fit'e uyarlanması (modül × fit matrisi büyür) veya modül alt kümesi; ölçek çarpanı yolu seçilirse 0 mesh ama rig/animasyon/hitbox ölçek doğrulaması. Doku: mevcut atlas. | **+1 mesh × 4 LOD** (siluet parçası), zırh atlası mevcut; zırh üretimi AP-008'den hazır. |
| Teknik giriş noktası | Ölçek çarpanı: `ApplyVersion` + hitbox/`BodyRadius` uyumu (mevcut alanlar; **skinned ölçek + NavMeshAgent yarıçapı: doğrulama gerekir**, yeni sistem değil). Yeni fit: `ZombieModuleLibrary` genişler (mevcut pipeline). | `ArmorChanceSettings` + `ZombieArmorLibrary` + `ApplyVersion`. Yeni sistem yok. |

## 5 · Görsel işaret dili — concept-artist bölümü

Ayrı dosya: **`WP-027-TIER-VISUAL-CUES.md`** (her tier A/B: siluet işareti, renk bandı — kumaş tint kanalı / zırh lake Mask.R, rezerv palet çakışma kontrolü —, 30 m/kalabalık okunabilirlik, üretim maliyeti sınıfı, mevcut kanca). Özet: 0-mesh küme (Std A + Sp B + El A + Ul A) yalnız mevcut `ApplyMaterial(tier)` varyantlarıyla çalışır; siluet-öncelikli küme ≈ 24 mesh + 1 doku ve görünüm seçicisine tier girdisi / ek renderer bağlaması ister; Ultra'da `WithoutMinoCape` çelişkisi ve tier↔tier kontrast eşiği (DEC-077 yalnız zombi/çevre) açık sorular.

## 6 · Karşılaştırma matrisi (1 = düşük, 3 = yüksek)
| Alternatif | Okunabilirlik | Üretim maliyeti | Oynanış farkı | Teknik risk |
|---|---|---|---|---|
| Special A | 1 | 1 | 3 | 3 (yeni steering) |
| Special B | 2 | 2 | 2 | 1 |
| Elite A | 2 | 1 | 2 | 1 |
| Elite B | 3 | 2 | 2 | 1 |
| Ultra A | 3 | 3 | 3 | 2 |
| Ultra B | 2 | 2 | 2 | 1 |

Tasarımcı varsayılan önerisi (kullanıcı kararı değil): **Special B · Elite B · Ultra B** — 0 yeni sistem, toplam +3–4 siluet mesh, mevcut atlaslar; oynanış farkı mevcut stat/stil profilinden gelir. Ultra A, "büyük gövde" isteniyorsa tek başına bir AP paketi maliyetindedir.

## 7 · Kullanıcı karar formu
| Tier | Karar |
|---|---|
| Special | A / B / karma (belirt) |
| Elite | A / B / karma (belirt) |
| Ultra | A / B / karma (belirt) |

Karar sonrası AP-014'e giden çıktı listesi: (1) tier başına modül alt kümesi kuralı (izinli top/bottom/head listesi), (2) tier başına siluet parçası sayısı + ankraj (`anchor_helmet/torso/legs`) + tri bütçesi, (3) tint hedefleri (kumaş `M_zombie_atlas_<tier>`, lake `M_zombie_armor_<tier>` Mask.R) ve §4 zarfı içinde HSV bandı (s ≤ 0.45, v ≤ 0.60), (4) ses listesi: tier başına adım/telegraph/kırılma olayı (DEC-067), (5) `ArmorChanceSettings` tablo eğilimi (WP-028 parametre paketi), (6) Ultra A seçilirse fit/ölçek kararı ve modül uyarlama listesi.

## 8 · Açık sorular (ürün kararı)
1. Special için yan yaklaşım (yeni steering sistemi) istenir mi, yoksa Special yalnız hız/tempo ile mi ayrışsın?
2. Elite/Ultra için zırh "garanti" (1.0) kabul edilir mi; DEC-021 elle tanımlı round tablosu bu garantiyi round'a göre mi açsın?
3. Ultra için büyük gövde (yeni fit veya ölçek çarpanı) istenir mi; istenirse mevcut 19 modül alt kümeye mi indirilir?
4. Zırhlı Elite-B ile zırhlı Ultra-B'nin ayrımı yalnız siluet parçası + boyutla mı sağlansın, yoksa Ultra'ya ayrı tint bandı mı ayrılsın (§4 zarfı içinde)?
5. Gold tablosu (5/10/25) zırh garantisiyle artan DEC-069 zırh bonusunu telafi edecek şekilde yeniden dengelensin mi (WP-028)?
