# LOCAL-QUEUE — yalnız yerel Mac'te koşulabilen işler

Bulut C-00 ve oturumları buraya yazar. Yerel bir oturum (Unity/Blender/Player erişimli) koşar ve sonucu satıra işler.

**Biçim:** `| tarih | sahip | dal / SHA | komut ya da iş | beklenen | durum / sonuç |`

**Kurallar:**
- Her Player/PlayMode koşusu userdata-guard penceresinde; 32 gelirse `tools/qa/oos_rule.py`.
- Disk ≥ 15 GB.
- Unity ve Blender işleri `tools/slot.sh` ile.

| tarih | sahip | dal / SHA | iş | beklenen | durum |
|---|---|---|---|---|---|
| 05.10 | C-00 | main ≥ 97705c32 | `tools/compile-check.sh main` | errorsCS 0 | **PASS 06.10** (`97a4bd56`, errorsCS 0) |
| 05.10 | S-06 | `wp-083-b1` (worktree `K/wp-083/worktree`, commit'siz) | B2d/B2e bitir. b2i tanısıyla ar_2'yi düzelt. Kanıt: rl_1 TP + side önce/sonra. Ardından commit + integrate. | PM yeşil, H1/H3 PASS, KundakOmuz PASS | sürüyor |
| 05.10 | C-00 | main (S-06'dan sonra) | `tools/main-ci.sh <sha>` | kırmızı yok ya da sahipli liste | bekliyor |
| 05.10 | S-03 | `wp-084-s03-zombie-v2` | WP-084 dilim 1: shader commit'i (keyword kapalıyken piksel-eş), alım, sonra Z2/Z3/Z3b/Z4/DEC-077 ölçümü | Z3b sınırları içinde | sürüyor |
| 05.10 | S-08 | `wp-085-*` | AP-027 v003g overlay. S4 v2. 3B mono ölçüm. ar_1/sg_1 dinleme kiti (üstten + FP, Kit_ + Stem_). | S4 PASS, mono çentik ≤ −6 dB, kit kayıtta silah sesi ≥ 6 dB | bekliyor |
| 05.10 | S-09 | `wp-081-v4a-r2` (bcde16dc + 8ba256ec + kirli) | Sinematik B + yastık + S04 uyarlaması. 5 oran. 60 s MP4. | kontrast FAIL 0, kenar ≤ 1,5× | sürüyor |
| 05.10 | P/ajan | `K/asset-production/AP-024` | AP-024C: 5 FBX + roundtrip, 11 silah tutuşu, deform, renderlar | kontrol noktası renderları | **06.10 yeniden başlatıldı** (C-00 alt ajanı; iş 2 pass1 8/12 PASS → pass2) |
| 05.10 | P/ajan | `K/asset-production/AP-025` | AP-025D: giysi AAA + yüz jaw ağırlıkları → `exports/v003/` | kontrol noktası: 2 giysi + TP önce/sonra | **06.10 yeniden başlatıldı** (C-00 alt ajanı) |
| 05.10 | S-01 | `s01-g4-d8-r2` d707b999 + ekler | D9 rollout: D8 + kuyruk + göç notu entegre, sürücü taşı, main-ci, duyuru | guard testleri yeşil | dalga bitince |
| 05.10 | C-00 | — | `K/wp-068s9/builds` → harici disk (`arsivle.py`), gerçek kazanç ~14,6 GB | SHA256 doğrulandı | S-09 bitince |
| 05.10 | S-03 · S-08 · S-02 · S-06 (C-00B B-03) | main 0b3ec358 · rapor `docs/c00/reviews/B-03-gc-inceleme.md` (dal `c00b/B-03-gc-review`) | Profiler GC doğrulaması §5: G1 zombi doğuşu (Z3b sahnesi, `ApplyMaterial`), G2 `AudioCatalog` tekillik + atış başına GC, G3 reload başlangıcı (`WeaponMagazineVisual.Bind`), G4 ilk atış karesi, G5 grip yokken (Raijū/Namekuji) | bugünkü değerler kayıtta; düzeltme sonrası G1–G3, G5 0 B, G4 sıçrama yok | bekliyor |
| 05.10 | C-00 (bulut C-00 adına) | dallar `c00b/B-02…B-05` + `claude/sharp-cray-jsvo9s` (4 commit) | Bulut zincirini `tools/integrate.sh` ile main'e al: sıra ve etiketler `docs/c00/YEREL-DONUS-PLANI-2026-10-05.md` §5. Bulutta izin sistemi cherry-pick'i reddetti; bulutta alınmadı. | `git cherry -v main origin/c00b/B-05-r2-docs-sync` boş; `r2_gates_lint` ERROR 0; lint testleri 23/23 | **ALINDI 06.10** (24/24, main `97a4bd56`; cherry "+" 0; testler 44/44; lint ERROR 0) |
| 05.10 | C-00 | main `3e68bc08` (`03c3b06b`, `a62d83db` derlenmedi) | `tools/compile-check.sh main` (yukarıdaki 97705c32 satırının güncel hâli) | errorsCS 0 | **PASS 06.10** (`97a4bd56`) |
| 06.10 | S-06/S-03/S-08/S-09/S-02/S-01/S-05 | ikinci hesap oturumları | Kullanıcı Claude Desktop'ta açtı (`docs/c00/OTURUM-ACILIS-2026-10-06.md`); KADRO uzman takımları ve kapıları mesajla verildi | teslimler C-00'a dal + SHA | sürüyor |
| 06.10 | C-00 KADRO alt ajanları | B-07 / B-08 / B-09 (`C00B-GOREVLER.md`) | R2 kabul matrisi; R1 bulut dalları; log_scan/aggregate testleri | dal + SHA → integrate | sürüyor |
