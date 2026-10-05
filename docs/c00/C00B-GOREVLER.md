# C00B-GOREVLER — asıl C-00'ın ikinci ekibe (C-00B) verdiği işler

Bu dosyayı asıl C-00 yazar. C-00B yalnız "durum" sütununu kendi PR'ında günceller.
Her görevin sahip dosyaları dışına çıkılmaz. Hepsi Opus 5.5.

| id | görev | sahip dosyalar | çıktı | doğrulama | durum |
|---|---|---|---|---|---|
| B-01 | **Sistem belgeleri R2 güncellemesi.** R2'de eklenenleri kaynak koddan okuyarak `docs/ARCHITECTURE.md` ve `docs/SYSTEMS-INDEX.md`'ye işle: WeaponHoldIk ve tutuş satırları, LocomotionV4, CharacterAnimationEvents BeatFamily ve reload olayları, AP-027 ses hattı (katman, kuyruk, mesafe, kovan, CasingEvents.Launched), ZombieLook v2 (SlotFace, maskeler, RL bitleri; S-03 dalı girdikçe), menü sinematiği (MenuLook, scrim, yastık), R2 kapıları. | `docs/ARCHITECTURE.md`, `docs/SYSTEMS-INDEX.md` | PR | belge; yerel doğrulama yok | açık |
| B-02 | **R2 kapı tutarlılık denetleyicisi.** `tools/qa/r2-gates.json` ↔ `tools/qa/r2-measure-contract.md` ↔ test adları (repo C# test sınıflarında `R2Olcum.Yaz` ve gate anahtarları) arasındaki uyumu kontrol eden bir Python aracı yaz: eksik ya da fazla anahtar, sahipsiz kapı, report-only işaretleri. `tools/qa/tests/` altına birim testi ekle. | `tools/qa/r2_gates_lint.py` (yeni), `tools/qa/tests/test_r2_gates_lint.py` (yeni) | PR + çıktı raporu | `python3 -m unittest`; yerel doğrulama yok | PR #1 (c00b/B-02-r2-gates-lint), yerel entegrasyon bekliyor |
| B-03 | **Sıcak yol ayırma (GC) incelemesi, salt okuma.** Bugün main'e giren C# değişikliklerinde (05.10 etiketleri, devir §5) kare başına ayırma riski, LINQ, boxing, closure, string. Bulgular için dosya:satır, risk ve öneri. Kod değiştirme; yalnız rapor. | `docs/c00/reviews/B-03-gc-inceleme.md` (yeni) | PR (belge) | yerelde Profiler ile S-sahipleri doğrular | açık |
| B-04 | **Portföy ve kabul belgesi taslağı.** R2'nin oyuncuya görünen değişiklikleri için yayın notu taslağı. Kaynaklar: `docs/c00/R2-PLAN.md` N2 listesi ve devir §4. İngilizce ve Türkçe kısa sürüm; görsel listesi (hangi kare nereden alınacak) dahil. | `docs/release/R2-NOTES-draft.md` (yeni) | PR | belge | açık |

Yeni görevler bu tabloya eklenir. C-00B bir görevi bitirince PR açar ve `C00B-TESLIM.md`'ye yazar.
