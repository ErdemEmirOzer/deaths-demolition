M4-R1 · WP-076 — AI v3: 5 saldırı, ölüm ve ragdoll kancaları, darbe itkisi, Special etkileşimleri, dünya v5 spawn
PAKET SAHİBİ: S-03 · ANA KOORDİNASYON: C-00
KAYIT: bu promptu birebir `planning/work-packages/WP-076-AI-V3.md` olarak kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`
- §0 kullanıcı notları birebir; bu paket N1, N3, N6 ve N11'in AI tarafıdır.
- §4 ortak kurallar geçerlidir.
- Dal: güncel main'den `wp-076-ai-v3`.

FAZ A — Fable 5.1 game-designer
Girdiler: S-06 WP-071 (klip ve olay sözleşmesi v3), S-02 WP-069 (Special mekanikleri + ADR-069-1 darbe verisi), S-05 WP-074 (yerleşim planı), S-01 WP-070 (CameraMode).
Çıktı: `WP-076-AI-V3-DIRECTION.md`.

1. **5 saldırı davranışı**
   - Seçim ölçütleri: tier, mesafe, açı, çeşitlilik.
   - Telegraph, erişim ve hasar pencereleri (DEC-029).
   - Kalabalık koordinasyonu: aynı anda aynı saldırı yığılmasın.
   - Special/Elite/Ultra kuralları.
2. **Ölüm**
   - Darbe verisi (yön, kuvvet, silah sınıfı, kemik) → S-06 ragdoll geçişine.
   - 4 ölüm klibinin seçimi.
   - WP-061K/L ödülsüz kaldırma kuralları, ragdoll'lu cesetle uyumlu olmalı.
3. **Hit tepkisi itkisi**
   - Projectile olaylarından: pompalı savurma, sniper sendeleme, LMG sürekli tepki.
4. **Special etkileşimleri**
   - Zincir yıldırım: hedef seçimi (en yakın N, yarıçap, görüş hattı?) ve elektrik çarpması durumu.
   - Jel birikintisi: kayıt, zombinin birikintiye girmesi → kayma → düşme → ölüm durum makinesi.
   - Sliquifier'da zombiler birikintiden kaçınmaz; bunu konsey onaylasın.
5. **Dünya v5**
   - Spawn noktaları, NavMesh, tehdit rotaları, büyütme etkisi.
6. **FP/TP farkındalık kancası**
   - Oyuncunun arkasındaki zombi → ses/HUD uyarısı (S-08, S-09).

FAZ B — Opus 5.5
- Uygulama + testler (değişen süit). Allocation 0 B.
- Kanıt: bot koşusu.

TESLİM: C-00'a ≤10 satır. Her dilim ayrı commit.
