# LOCAL-QUEUE — yalnız yerel Mac'te koşulabilen işler

Bulut C-00 ve oturumları buraya yazar. Yerel bir oturum (Unity/Blender/Player erişimli) koşar ve sonucu satıra işler.

**Biçim:** `| tarih | sahip | dal / SHA | komut ya da iş | beklenen | durum / sonuç |`

**Kurallar:**
- Her Player/PlayMode koşusu userdata-guard penceresinde; 32 gelirse `tools/qa/oos_rule.py`.
- Disk ≥ 15 GB.
- Unity ve Blender işleri `tools/slot.sh` ile.

| tarih | sahip | dal / SHA | iş | beklenen | durum |
|---|---|---|---|---|---|
| 05.10 | C-00 | main ≥ 97705c32 | `tools/compile-check.sh main` | errorsCS 0 | bekliyor |
| 05.10 | S-06 | `wp-083-b1` (worktree `K/wp-083/worktree`, commit'siz) | B2d/B2e bitir. b2i tanısıyla ar_2'yi düzelt. Kanıt: rl_1 TP + side önce/sonra. Ardından commit + integrate. | PM yeşil, H1/H3 PASS, KundakOmuz PASS | sürüyor |
| 05.10 | C-00 | main (S-06'dan sonra) | `tools/main-ci.sh <sha>` | kırmızı yok ya da sahipli liste | bekliyor |
| 05.10 | S-03 | `wp-084-s03-zombie-v2` | WP-084 dilim 1: shader commit'i (keyword kapalıyken piksel-eş), alım, sonra Z2/Z3/Z3b/Z4/DEC-077 ölçümü | Z3b sınırları içinde | sürüyor |
| 05.10 | S-08 | `wp-085-*` | AP-027 v003g overlay. S4 v2. 3B mono ölçüm. ar_1/sg_1 dinleme kiti (üstten + FP, Kit_ + Stem_). | S4 PASS, mono çentik ≤ −6 dB, kit kayıtta silah sesi ≥ 6 dB | bekliyor |
| 05.10 | S-09 | `wp-081-v4a-r2` (bcde16dc + 8ba256ec + kirli) | Sinematik B + yastık + S04 uyarlaması. 5 oran. 60 s MP4. | kontrast FAIL 0, kenar ≤ 1,5× | sürüyor |
| 05.10 | P/ajan | `K/asset-production/AP-024` | AP-024C: 5 FBX + roundtrip, 11 silah tutuşu, deform, renderlar | kontrol noktası renderları | sürüyor ya da yeniden başlatılacak |
| 05.10 | P/ajan | `K/asset-production/AP-025` | AP-025D: giysi AAA + yüz jaw ağırlıkları → `exports/v003/` | kontrol noktası: 2 giysi + TP önce/sonra | sürüyor ya da yeniden başlatılacak |
| 05.10 | S-01 | `s01-g4-d8-r2` d707b999 + ekler | D9 rollout: D8 + kuyruk + göç notu entegre, sürücü taşı, main-ci, duyuru | guard testleri yeşil | dalga bitince |
| 05.10 | C-00 | — | `K/wp-068s9/builds` → harici disk (`arsivle.py`), gerçek kazanç ~14,6 GB | SHA256 doğrulandı | S-09 bitince |
