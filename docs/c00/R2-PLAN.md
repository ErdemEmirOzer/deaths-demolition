# DEATHS DEMOLITION — M4-R2 REVİZYON TURU PLANI (C-00, taslak v1, 05.10.2026)

> Durum: **ONAYLANDI 05.10.2026** (kullanıcı). K1 Mixamo + elle cila · K2 Animation Rigging kurulur · K3 Sonniss GDC ücretsiz · K4 HF/Adobe sınırsız · K5 "İndirilmiş Olarak Tut" · K6 geri ×0,65 kalır. Açık karar yok.
> Taban: R1 release 84941661 (FINAL'' faz 1 PASS; faz 2 perf/soak ölçümü yapılmadı → Faz 0'da).
> Kaynaklar: kullanıcı R2 notları (05.10, aşağıda §1, birebir); R1'den devreden adaylar (`memory/r2-tur-plani`); `c-00/PROJE-SISTEM-PROMPTU-2026-09-29.md`; `m4-r1/M4-R1-REVIZYON-TURU.md`.

---

## 1. Kullanıcı notları (birebir, N2 kodlu)

| Kod | Not |
|---|---|
| **N2-01** | Menülerde yazıları azaltıp ikonları artırmak; görsel sunumu AAA kalitesine çıkarmak. |
| **N2-02** | Yükleme ekranlarındaki arka plan resimleri 4–5 farklı resim olsun; hep aynı resim çıkmasın. |
| **N2-03** | Zombiler yeşil, yüzsüz ve detaysız görünüyor. Zombilere doku uygulansın, AAA oyun kalitesinde olsunlar; gerekli işlemler eklenip sinematik zombi figürleri oluşturulsun. |
| **N2-04** | Ana karakter tamamen değişecek: daha fantastik, kurgusal, renk paleti oyuna uygun. Kullanıcıyla **interaktif ve seçmeli şıklarla birlikte** tasarlanacak. |
| **N2-05** | Silahlar şu an karakterin omzunda ve yanlış animasyonda duruyor; reload sırasında şarjör değiştirme animasyonu doğru çalışmıyor. Karakter tasarlandıktan sonra animasyonlar hatasız yapılacak. |
| **N2-06** | Karakterin ve zombilerin yürüme/koşma animasyonları çok amatör. Rigging ve animasyonlar AAA kalitesinde araştırılıp tasarlansın ve uygulansın. Yürürken/koşarken vücudun tüm bölgeleri gerçekçi animasyona sahip olacak (örn. koşarken gövdenin sallanması); hiçbir detay atlanmayacak. |
| **N2-07** | Yeni silah sesleri kötü; eskisinden de iyi olacak. Yankılı, ultra gerçekçi silah sesleri — çok önemli; titizlikle, profesyonelce tasarlanıp uygulansın. Kovanın yere düşme sesi arka planda uygulansın. |

---

## 2. Çalışma modeli (R2'de yeni: Claude + Codex)

| Taraf | Kim | İş | Haberleşme |
|---|---|---|---|
| **C-00** | Claude ana koordinasyon | Plan, paket promptları (Claude + Codex), karar aktarımı, entegrasyon (`integrate.sh`), main-ci, kabul zinciri | — |
| **Claude oturumları** | S-01…S-10 (+ Fable AD/ses konseyi) | **Unity entegrasyonu**, runtime kodu, testler, AD turu, ses ölçüm kapısı, kabul | **Otomatik** (C-00 ↔ oturum mesajı) |
| **Codex oturumları** | Kullanıcının açtığı Codex görevleri (CX-…) | **Tasarım, araştırma, varlık üretimi** (konsept, model, doku, rig, klip, ses, UI sanatı) | **Manuel**: C-00 prompt dosyası yazar → kullanıcı Codex'e verir → Codex rapor dosyası yazar → kullanıcı "rapor hazır" der → C-00 okur |

- Codex işleri repo/Unity'ye doğrudan yazmaz (üretim paketleri); çıktıları KADRO `asset-production/` + manifest ile teslim eder, Claude sahip oturumu manifest kapısıyla içe alır. Ayrıntı: `c-00/codex/CODEX-SISTEM-R2.md`.
- Kullanıcının seçmesi gereken her tasarım (karakter, zombi görünümü, ikon dili, yükleme sanatları, ses A/B) **seçenek paketi** olarak gelir; C-00 bunları kullanıcıya görsel/interaktif şıklarla sunar (sayfa + çoktan seçmeli soru); seçim gelmeden üretim o yolda ilerlemez.
- Codex görevleri paralel çalışır; makine slot kuralları (Unity 2, Blender 1, Player 1) ve disk/iCloud kuralları Codex için de geçerlidir.

---

## 3. Fazlar

| Faz | İçerik | Kim | Kullanıcı kapısı |
|---|---|---|---|
| **0 — Hazırlık** (onaydan hemen sonra, ~½ gün) | R1 FINAL'' faz 2 (perf/soak baseline, sessiz pencere); Codex sistemi kurulumu; R1 devir kalemleri (bot girdi yaması vb.) main'e; kararlar §5 | C-00, S-10, S-02, S-01 | §5 kararları |
| **1 — Yön ve tasarım** (paralel, ~1–2 gün) | Karakter konsept şıkları · zombi AAA yön şıkları · AAA animasyon araştırması + rig standardı · silah sesi kaynak/tasarım + A/B kiti · ikon dili + 5 yükleme sanatı şıkları | **Codex** (CX-AP024…028 faz A) | **Seçim kapıları G1–G5** |
| **2 — Üretim** (paralel, ~3–5 gün) | Seçilen karakterin modeli/dokusu/rig'i/FP kolları; zombi model/doku/tier/sinematik figürler; hero + zombi locomotion/silah/reload klipleri; ses seti + kovan sesleri; ikon seti + menü mockup'ları + yükleme sanatları | **Codex** (faz B/C) + Fable AD (Claude) | Ara onaylar (G6–G8) |
| **3 — Entegrasyon** (Faz 1 sonundan itibaren dilim dilim, ~3–4 gün) | Menü v4 + yükleme rotasyonu; locomotion runtime (foot IK, ikincil hareket); hero değişimi + silah tutuşu + reload; zombi görünüm + locomotion; ses v4 + kovan sistemi; VFX hizaları | **Claude** S-09/S-06/S-02/S-03/S-08/S-07 | — |
| **4 — Kabul ve teslim** (~1 gün + sessiz pencere) | main-ci → R2 FINAL (S-10) → release (mac + win64) → portföy kareleri (Editor) → kullanıcı oynar → R3 notları | S-10, C-00 | Oyun testi |

Süreler tahmindir; Codex'in hızı ve seçim kapılarının ne kadar sürede kapandığına bağlıdır.

---

## 4. İş paketleri

### 4.1 Codex — tasarım ve üretim (AP = asset production)

| Paket | Başlık | Notlar | Fazlar | Teslim yeri | Tüketen (Claude) |
|---|---|---|---|---|---|
| **AP-024** | **Yeni ana karakter** (Hero v2) | N2-04, N2-05 | A: 4–6 konsept şıkkı (G1) → B: rafine konsept sayfası, palet, malzeme, ön/yan/arka (G2) → C: 3D model + PBR doku + LOD + AAA rig (AP-026 rig standardı) + FP kollar (aynı kimlik) + silah tutma/şarjör soketleri → AD | `asset-production/AP-024/` | S-06, S-02, S-07 |
| **AP-025** | **Zombi AAA görünüm** | N2-03 | A: yön şıkları — yüz/anatomi, deri, yara, kıyafet, tier farkı (G3) → B: Standard + tier (Special/Elite/Ultra/Executioner) model/yüz/göz/diş/PBR deri ve ıslaklık, LOD, varyant modülleri → C: **sinematik zombi figürleri** (hero render/turntable, pazarlama) → AD | `asset-production/AP-025/` | S-03, S-06, S-05 |
| **AP-026** | **AAA rig + animasyon** | N2-05, N2-06 | A: araştırma raporu (AAA locomotion: ağırlık, ikincil hareket, stride/orientation warping, foot IK, dur-kalk/dönüş, silah katmanları, reload anatomisi) + **rig standardı** (insan/zombi; twist, parmak, IK hedefleri, ikincil zincirler) + klip listesi ve kalite çıtası → B: hero locomotion seti (idle/walk/jog/run/sprint, 8 yön, start/stop/pivot, geri koşu) + silah tutuş pozları + her silah için reload (şarjör çıkar/tak olaylarıyla) → C: zombi locomotion setleri (tier başına shamble/walk/run/lunge, kadans çeşitliliği) → AD + kayma ölçümü | `asset-production/AP-026/` | S-06 (runtime), S-02, S-03 |
| **AP-027** | **Silah sesleri v4 + kovan** | N2-07 | A: kaynak araştırması (lisanslı/ücretsiz kütüphaneler, kayıt kalitesi) + tasarım spesifikasyonu (katmanlar, mekân kuyrukları, uzak ses, iç/dış) + 2 silahta prototip → **gerçek RPM'li A/B kiti** (G4) → B: 10 silah tam set (ateş katmanları, mekanik, kuyruklar açık/koridor/salon, uzak) + **kovan düşme seti** (yüzey × kalibre × varyasyon) + reload/mekanik foley | `asset-production/AP-027/` | S-08, S-07 |
| **AP-028** | **UI ikon dili + yükleme sanatları** | N2-01, N2-02 | A: ikon dili şıkları (3 stil, aynı 12 ikonla) + menü yeniden tasarım mockup şıkları ("az yazı, çok ikon") (G5) + 5 yükleme anahtar sanatı adayı (8–10 aday arasından seçim, G5) → B: tam ikon seti (tüm menü/ayar/HUD eylemleri, SDF/PNG çoklu boyut), menü ekran mockup'ları (her ekran), 5 yükleme sanatı final | `asset-production/AP-028/` | S-09 |

### 4.2 Claude — entegrasyon, runtime, kabul (WP)

| Paket | Sahip | Başlık | Bağımlılık |
|---|---|---|---|
| **WP-081** | S-09 | **Menü v4**: yazı azaltma, ikon tabanlı düzen, AAA sunum (geçişler, mikro-etkileşim), erişilebilirlik korunur (ikon + tooltip/okuyucu etiketi) · **yükleme ekranı rotasyonu** (5 sanat, tekrarsız karıştırma) | AP-028 B (rotasyon kodu hemen başlayabilir) |
| **WP-082** | S-06 lider, S-02 eş | **Hero v2 entegrasyonu**: yeni model/rig, FP kollar, **silah tutuşu düzeltmesi** (omuz değil eller; iki kemikli el IK, tutamak soketleri), **reload şarjör olayları** (şarjör çıkar/elde/takar soketleri, animasyon olayı ↔ silah durumu) | AP-024 C, AP-026 B |
| **WP-083** | S-06 | **AAA locomotion runtime**: Animator v4 (8 yön + start/stop/pivot), foot IK + ayak kilidi, stride/kadans eşleme, gövde/baş/kol ikincil hareketi (prosedürel yay katmanı), kayma ölçüm kapısı; hero + zombi | AP-026 A (hemen mevcut rig'lerle başlar), B/C |
| **WP-084** | S-03 lider, S-06 eş | **Zombi v2 entegrasyonu**: yeni model/doku/tier görünümü, okunabilirlik eşikleri (DEC-077), zombi locomotion setleri, perf bütçesi | AP-025 B, AP-026 C |
| **WP-085** | S-08 | **Silah ses v4 entegrasyonu**: hibrit döngünün kaldırılması (her atış tam ses), mekân kuyrukları, uzak ses, **kovan sistemi** (atış → gecikmeli, yüzeye göre, havuzlu 3B kovan düşme sesi), mix/loudness, gerçek RPM A/B kiti | AP-027 B (kovan sistemi kodu hemen) |
| **WP-086** | S-07 | **VFX hizası**: yeni hero/silah tutuşunda namlu/kovan çıkış soketleri, kovan görsel fırlatma (varsa) ↔ ses eşleşmesi, zombi yeni malzemelerinde kan/ıslaklık uyumu | WP-082, WP-084 |
| **WP-087** | S-02 | **R1 his kalemleri**: geri ×0,65 kararı (kullanıcı), FP yaw/roll fazlası, roket ilk kare, **bot girdi yaması** (qapad 8c547fee/0ad379f9 + S-10 74ccad19) | §5 K6 |
| **WP-088** | S-03 + S-05 + S-04 | **R1 teknik devir**: köprü C8/C1 kıyı alternatifi, hayalet zombi roster sağlamlığı, Flank eşiği, GoldUpgrade P10 + ProfilePersistence tekil doğrulama | — |
| **WP-089** | S-01 | **Altyapı**: KADRO'yu iCloud dışına taşıma (K5), guard G4 D5–D8, driver tazelik kontrolü, Codex teslim alma aracı (manifest doğrulayıcı) | §5 K5 |
| **WP-090** | S-05 | **Dünya SHOULD'ları** (R1 AD sentezi: yakın çam iğne rolü, galN translucency, pus, etek A/B, köprü kıyı noktası builder'ı) | — (düşük öncelik) |
| **WP-091** | S-10 | **R2 kabul**: R1 perf/soak baseline (Faz 0) → R2 FINAL → release mac + win64 → portföy 10 kare (Editor, debug panel) | hepsi |

### 4.3 Bağımlılık özeti

```
AP-028 A ─G5─> AP-028 B ──> WP-081
AP-024 A ─G1─> AP-024 B ─G2─> AP-024 C ──┐
AP-026 A (rig standardı) ────────────────┼─> AP-026 B (hero klipler) ──> WP-082 + WP-083
AP-025 A ─G3─> AP-025 B ──> AP-026 C (zombi klipler) ──> WP-084
AP-027 A ─G4─> AP-027 B ──> WP-085 ──> WP-086
WP-083 / WP-085 / WP-081 runtime hazırlığı Faz 1'de mevcut varlıklarla başlar (bekleme yok)
```

---

## 5. Kullanıcı kararları (🔴) — onayla birlikte istenecek

| # | Karar | Seçenekler | C-00 önerisi |
|---|---|---|---|
| ~~K1~~ | Animasyon veri kaynağı | **KARARLANDI 05.10: Mixamo (Adobe hesabı) tabanı + elle cila + prosedürel katman** | — |
| ~~K2~~ | Unity Animation Rigging paketi | **KARARLANDI 05.10: kurulur** (S-01 + S-06, WP-083 ilk dilim) | — |
| ~~K3~~ | Silah sesi kaynağı | **KARARLANDI 05.10: Sonniss GDC ücretsiz paketleri** (seçili dosyalar, iCloud dışı kaynak klasörü) | — |
| ~~K4~~ | Higgsfield kredi tavanı | **KARARLANDI 05.10: sınır yok; Adobe da sınırsız** (defter tutulur) | — |
| ~~K5~~ | iCloud dataless sorunu | **KARARLANDI 05.10: önce "İndirilmiş Olarak Tut"; repo .git/Library bulutta kaldığı için kalıcı taşıma A (~/Dev + symlink) ilk sakin anda, S-01 runbook** | — |
| ~~K6~~ | Geri koşu ×0,65 | **KARARLANDI 05.10: böyle kalsın** (tüm kameralar) | — |

---

## 6. R1'den devreden ve bu turda kapanan kalemler

- **R1 FINAL'' faz 2** (soak + perf penceresi): C-00 kararı 05.10 — ayrıca koşulmaz; R2 FINAL'de (WP-091) R1 tabanıyla karşılaştırmalı ölçülür. Makine Codex üretimine açılır.
- **WP-087:** bot girdi yaması (odak kaybı → "ölçülemez"), FP yaw/roll, roket ilk kare, geri ×0,65.
- **WP-088:** köprü kıyı alternatifi, roster sağlamlığı, Flank, GoldUpgrade P10, ProfilePersistence tekil.
- **WP-089:** iCloud taşıma, guard D5–D8, driver tazelik.
- **WP-090:** dünya SHOULD'ları.
- **Silah sesi R2 notu:** `wp-072/R2-SILAH-SESI.md` (hibrit kök, gerçek RPM kiti dersi) → AP-027 girdisi.
- **Karakterle birlikte kendiliğinden çözülecekler:** player_base REST iskeleti, TP tutuş soket sınıfları, 8 yön halkası, Executioner kadans klibi, Ultra overhead klibi (AP-024/026 kapsamında).
- **Portföy kareleri:** R2 sonunda (yeni görsellerle), Editor + debug panel (WP-091).

---

## 7. Riskler

| Risk | Etki | Önlem |
|---|---|---|
| AI 3B üretim (Tripo vb.) AAA çıtasına yetmez | Karakter/zombi "amatör" kalır | Taban model + Blender retopo/sculpt detay + elle doku katmanları; AD turu ölçütlü; gerekirse ücretli kaynak kararı kullanıcıya |
| Rig değişimi tüm klipleri etkiler | Uzun zincir | Rig standardı Faz 1'de kilitlenir; hero ve zombi ayrı standartlar; retarget doğrulayıcı |
| Codex ↔ Claude aynı makinede | Slot/disk/iCloud çakışması | Codex de `slot.sh`, `userdata-guard.sh`, disk kapısı kullanır; büyük kopya yasağı sessiz pencerede |
| Manuel aktarım gecikmesi | Kapılar uzar | Codex raporları dosyada; kullanıcı yalnız "hazır" der; C-00 dosyadan okur |
| Ses kalitesinin kulakla onayı | Tekrar | Gerçek RPM'li kit; nesnel ölçüt (400 Hz altı ≥ %20, LUFS, kuyruk süresi) + insan dinleme kapısı |
| Perf bütçesi (yeni dokular/rig) | FPS düşer | Bütçeler paket içinde (doku boyutu, kemik sayısı, LOD); S-10 perf kapısı |

## 05.10 ek — G1/G3/G5 seçimleri ve kullanıcı eklemeleri
- Seçimler: G1 Hero **O1 Kırık Mühür**, G3 zombi **O1 Boğulmuş Muhafızlar**, G5 ikon/menü **O1 Fırça mürekkebi**, yükleme **L01 L02 L04 L05 L08**, Profil ilk açılışta **kapalı**.
- **N2-01 ek (kullanıcı):**
  - Ana menü arka planında **oyunun gerçek haritası üzerinde AAA sinematik, hareketli kamera**. Motor içi gerçek zamanlı; tasarım/previz AP-028-B, uygulama S-09 + S-05 + S-07.
  - **Tüm ikonlara motion design + derinlik + yansıma (AAA).** Spesifikasyon AP-028-B; uGUI shader ve animasyon S-09.
- Codex Faz B görevleri: `c-00/codex/prompts/CX-AP024-B.md` … `CX-AP028-B.md`.
- Kullanıcı adımı: Mixamo M01–M23 indirmesi (`AP-026/exports/v001/AP026-mixamo-downloads-v1.csv`) → `/Users/Erdem/DD-Sources/mixamo/`.
