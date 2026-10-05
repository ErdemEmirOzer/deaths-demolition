# C00B-TESLIM — ikinci ekibin teslim kaydı

C-00B her görev için başla/bitir satırı yazar. Dosya C-00B'nin dalında güncellenir; asıl C-00 PR'ları `git fetch` ile okur.

| tarih saat | görev id | dal | SHA | ne yapıldı | doğrulanan | yerelde doğrulanacak | PR |
|---|---|---|---|---|---|---|---|
| 05.10 19:15 | B-02 | c00b/B-02-r2-gates-lint | taban 0b3ec358 | BAŞLADI: r2-gates.json ↔ r2-measure-contract.md ↔ R2Olcum.Yaz denetleyicisi | — | — | — |
| 05.10 19:24 | B-02 | c00b/B-02-r2-gates-lint | b7737f04 | BİTTİ: tools/qa/r2_gates_lint.py + 23 birim testi. main 0b3ec358 raporu: ERROR 1 (sözleşmede R2Olcum.cs yolu bayat), WARN 14 (H1 [Explicit] measuredBy'sız; Z3b zorunlu test [Explicit]; 12 kapının yazıcısı yok) | python3 -m unittest test_r2_gates_lint 23/23 OK (bulut) | Unity gerekmez; isteğe bağlı Mac'te aynı çıktı + test_integrate_status (zsh) | https://github.com/ErdemEmirOzer/deaths-demolition/pull/1 |
| 05.10 19:24 | B-01 | c00b/B-01-docs-r2 | taban a7cd3bd3 | BAŞLADI: ARCHITECTURE.md + SYSTEMS-INDEX.md R2 güncellemesi | — | — | — |
| 05.10 19:29 | B-01 | c00b/B-01-docs-r2 | 88da3107 | BİTTİ: ARCHITECTURE §3.12–3.17, §4 olay tablosu, §8.1 R2 kapıları; SYSTEMS-INDEX M4-R2 + 'Branch' durumu + 10 satır. Main'de olmayan (S-03 zombi v2, S-06 silah satırları, S-08 Launched ucu, S-09 sinematik) dal adıyla işaretli | eklenen P/ A/ yolları checkout'ta var (eksik 0) | Unity gerekmez; S-03/S-06/S-08/S-09 dalları girince satırlar güncellenecek | https://github.com/ErdemEmirOzer/deaths-demolition/pull/2 |
| 05.10 19:29 | B-03 | c00b/B-03-gc-review | taban c5e0d905 | BAŞLADI: 05.10 C# değişikliklerinde sıcak yol ayırma incelemesi (salt okuma) | — | — | — |
| 05.10 19:37 | B-03 | c00b/B-03-gc-review | 1dff988b | BİTTİ: 05.10 runtime C# GC incelemesi (104 commit, 51 dosya). G1 orta (zombi malzeme anahtarı + sharedMaterials kopyası, doğuş başına), G2 orta-gizli (AudioCatalog indeksi null/yinelemede her çağrıda yeniden), G3–G5 düşük, B1–B2 CPU notu; temiz yollar listelendi. LOCAL-QUEUE'ya 1 satır | salt okuma; miktarlar tahmin | Profiler: G1 S-03, G2/G4 S-08, G3 S-02, G5 S-06 (rapor §5) | https://github.com/ErdemEmirOzer/deaths-demolition/pull/3 |
| 05.10 19:37 | B-04 | c00b/B-04-release-notes | taban a593c8cf | BAŞLADI: R2 yayın notu taslağı (EN + TR, görsel listesi) | — | — | — |
| 05.10 19:39 | B-04 | c00b/B-04-release-notes | e289b1a4 | BİTTİ: docs/release/R2-NOTES-draft.md — 14 satır gerçek durumuyla (main / main'de kapalı / dal / üretim), EN + TR kısa sürüm, 14 görsellik liste (oyun içi vs render, kaynak yolu, sahip) | belge; durumlar kaynak + devir §4'ten | görseller satırlar main'e girince ilgili oturumlarca; yayından önce §5 | https://github.com/ErdemEmirOzer/deaths-demolition/pull/4 |
| 05.10 20:00 | B-05 | c00b/B-05-r2-docs-sync | taban 01f4b163 (B-04 ucu) | BAŞLADI (bulut C-00): bayat bilgi eşitlemesi, lint ERROR, sahip tabloları, yerel dönüş planı | — | — | — |
