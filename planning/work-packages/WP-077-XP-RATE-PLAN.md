# WP-077 B5 · Gerçek XP/saat ölçüm planı (S-04, 30.09)

**Amaç.** DEC-098'in açık kalemi: Kül temposu 4 500 XP/saat referansıyla ayarlandı; bu tempoyla halka 1 ≈ 1 saat,
halka 1+2 ≈ 11 saat (hedef ≤ 10), bütün meta ≈ 39 saat sürüyor. Round modeli (TreeModel, bot hızı) ise
2 640–3 260 XP/saat veriyor: bu hızla halka 1 1,4–1,7 saat, bütün meta 54–66 saat. Hangisinin doğru olduğunu
**gerçek oyuncu koşuları** gösterir.

## 1. Veri kaynağı — yeni kod gerekmiyor
- `RunTelemetry` (WP-045) her koşunun sonunda `persistentDataPath/telemetry/run-<utc>.json` yazar. Release build'de
  de açıktır: Ayarlar'dan kapatılabilir, `-ddTelemetry 0` ile de kapanır.
- Kullanılan alanlar: `runSeconds` (koşu başı → son; round sonu dükkânı dahil, Pause hariç), `completedRoundXp`,
  `killXp`, `reachedRound`, `weapon`, `build`. Belgede profil/kimlik bilgisi yoktur.
- Kapsam dışı: başarım, ilk-R10 ve ilk-doruk gibi tek seferlik Kül'ler telemetride yok. Bunlar TreeModel.Tempo'nun
  "tek seferlik havuz"undan ayrıca eklenir.

## 2. Kullanıcı verisi (§4.14)
Kullanıcının telemetri klasörü kullanıcı verisidir. **Yalnız C-00 çalıştırır.** Araç salt okur, yalnız toplam yazar
(zaman damgası, dosya adı, koşu listesi çıktıda yoktur), hiçbir şey kopyalamaz ve varsayılan yolu yoktur.

```bash
python3 tools/balance/xp_rate.py "<kullanıcı telemetri klasörü>" --json <KADRO>/wp-077-b5/evidence/xp-rate-user.json
```

Araç doğrulaması: WP-045 bot telemetrisinde (2 koşu, R2) 2 006 XP/saat verir
(`wp-077-b5/evidence/xp-rate-bot-wp045.txt`).

## 3. Adımlar
1. C-00 klasördeki koşu sayısına bakar (salt okuma). **En az 10 koşu ve 1 saat** yoksa ölçüm ertelenir; kullanıcı
   normal oynamaya devam eder.
2. C-00 aracı çalıştırır. Çıktılar: toplam XP/saat (Σ XP / Σ süre), koşu başı medyan ve p25/p75, ulaşılan tura göre
   bantlar (R1–5 / R6–9 / R10–14 / R15+), halka süreleri.
3. S-04 sonucu kararla birlikte C-00'a yazar (aşağıdaki kural). Karar kullanıcınındır; DEC-098 güncellemesini C-00
   yapar.

## 4. Karar kuralı (önceden yazılı)
- Toplam XP/saat ≥ 3 600 (referansın %80'i): DEC-098 halka maliyetleri korunur.
- Toplam XP/saat < 3 600: halka 1+2 ≤ 10 saat hedefi için iki seçenek hesaplanıp kullanıcıya sunulur:
  - **(a) Kül böleni:** 30 → ⌊XP/saat × 10 / (2 080 − tek seferliklerin yarısı)⌋.
  - **(b) Halka 2 fiyatları:** fiyatlar aynı oranda düşürülür.

  Önerilen varsayılan: (a). Tek bir sayıyla değişir ve seviye eğrisine dokunmaz.
- Bant farkı büyükse (ör. R1–5 koşuları R10+ koşularının yarısı hızındaysa) yeni oyuncunun temposu ayrıca raporlanır.

## 5. Bilinen sınırlar
- `runSeconds` menü, seçim ve sonuç ekranlarındaki zamanı içermez; ölçülen "oyun içi saat"tir, oturum saati değildir.
- `build` alanı her release'de "1.0" gösterir. Build ayrımı için `Application.version`'a git SHA eklenmesi önerilir
  (S-01, küçük iş). O zamana kadar koşular dosya tarihine göre build'e bağlanır.
- Az koşuyla (< 10) medyan kararsızdır; karar kuralı toplam değeri kullanır.
