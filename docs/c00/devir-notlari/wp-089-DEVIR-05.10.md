# S-01 devir notu — 05.10.2026 (hesap değişimi)

## Durum özeti
- **Main'de (05.10):**
  - WP-089 G4 D5 (463ea5ab), D6 (52b7a00f), D7 (eb632a7c);
  - codex_manifest_check türleri (df59297a);
  - integrate.sh varsayılan repo (e7c17017);
  - rc.sh 32 kuralı (6837484b, qa/oos_rule.py);
  - guard newline düzeltmesi (2f1ab726);
  - qa aiflags skip (6421bc25), README STALE (0040c85f), Diagnostics hariç (ce51fe37).
- Sürücü (`K/wp-079/driver`) main 2f1ab726'da. D8 main'e GİRMEDİ.

## Açık iş: G4 D8 + kuyruk + göç notu → D9 rollout (yeni C-00 karar verir)
- **D8 (teslim, main'de değil):** `d707b9990bfc952511ae943bb53b15dbc7a88a79`, dal `s01-g4-d8-r2`.
  - Pencere defteri `L/windows/<id>.json`; 65/75; R12 finalize/close.
  - İki inceleme yapıldı, MUST/SHOULD'lar düzeltildi. Tam regresyon yeşildi (v2 98/98, d6a 11/11 …).
- **Kuyruk WIP:** `225d0359eaa49ac238dbf5023b82f851a5d08330`, dal `s01-g4-d8-queue`, worktree `K/wp-089/worktree-d6` (tools-only sparse). Üstü d707b999. C-00 05.10 isteği:
  - kilit sırası: pencere ÖNCE, unity SONRA, bütün kapsamlarda → 64;
  - FIFO kuyruk `L/queue/`, `DD_GUARD_QUEUE_WAIT` 3600 s, satır "queue position n — ahead — estimate".
- **Göç notu:** `K/c-00/R2/GUARD-D8-GOC.md` (yazıldı). İçinde bilinen yanlış sıralı çağıranlar listesi var. Önemli olanlar: wp-083, wp-071 ve wp-032h `tools/unity.sh` sarmalayıcıları; S-06'nın aktif WP-083'ü etkilenir.
- **Kalan:**
  1. Kuyruk dalında tam regresyon (hesap değişimi için yarıda durduruldu). Yeni test `test_Kuyruk_OlenBekleyicininBileti_SiradakiniTutmaz` henüz koşmadı.
  2. Kuyruğa bağımsız eşzamanlılık incelemesi (Opus).
  3. Yeşilse WIP'in yerine gerçek commit (yeni commit; amend yok). Konu `WP-089 G4 D8 queue: …(C-00 05.10)`, gövdede Test:/Guarded:.
  4. C-00'a tek SHA (ya da sıralı: d707b999 → kuyruk commit'i). Teslimde "SÜRÜCÜ DOKUNUŞU VAR" yaz.
- **C-00'ya bildirilecek sapma:** kuyruk süresi için C-00 `DD_GUARD_WAIT ≥ 3600` istedi. Ben ayrı `DD_GUARD_QUEUE_WAIT` (3600) yaptım, çünkü `DD_GUARD_WAIT` profil kilidi/BUSY beklemesini de yönetiyor ve 1 saate çıkarsa restore 1 saat bekler. Onay alınmalı.

## Sıradaki komut (yeni oturum)
```zsh
cd ~/Desktop/KADRO/context/projects/deaths-demolition/wp-089/worktree-d6   # dal s01-g4-d8-queue @ 225d0359
python3 tools/tests/test_userdata_guard_v2.py LedgerTests          # ~6 dk, sandbox'lı fikstür
python3 tools/tests/test_userdata_guard_v2.py                      # ~25 dk
python3 -m unittest tools.tests.test_guard_d6a tools.tests.test_guard_d234 tools.tests.test_ci_signal_traps tools.tests.test_rc_stage_guard
```
- Not: d6a/FOREIGN_WRITER zaman aşımı ya da flake görülürse önce makinedeki gerçek Unity/Player yüküne bakın: `pgrep -fl "Unity.app/Contents/MacOS/Unity -batchmode"`. Test ps shim'i gerçek Player'ları gizler; slot testleri gerçek Unity sayısını görür.

## Açık ölçümler / arka plan
- **iCloud:**
  - Repo dataless 0 (11:45Z). Aside klasörü `~/Documents/DD-dataless-c00-20261005T111551Z` (3.979 dataless; kullanıcı onayıyla Çöp'e).
  - Saatlik izleyici `K/wp-089/evidence/icloud-watch.sh` çalışıyor (log `icloud-watch.log`; durdurmak için `icloud-watch.stop` dosyası).
  - Son tur 14:43Z 0/0/0. Disk saatte ~5–7 GiB düşüyor (78 GiB).
  - Taşıma penceresi (runbook `K/wp-089/evidence/ICLOUD-TASIMA-RUNBOOK.md` §2–§5) C-00'ın ilanını bekliyor.
- **Guard:** bu oturumun açık guard penceresi YOK. Unity/Player/Blender koşusu YOK. Fikstür testleri durduruldu.
- **Dallar** (hepsi teslim ya da main'de):
  - s01-ci-diagnostics(-r2);
  - s01-oos-newline-fix(-r2, -r3; e5ac3916/ec3cb3d1 kullanılmadı);
  - s01-codex-check-types, s01-integrate-main-default, s01-rc-oos-rule, s01-qa-aiflags-skip;
  - s01-g4-d6/-r2, s01-g4-d7/-r2, s01-g4-d8/-r2, s01-g4-d8-queue.
- **Kalanlar listesi:** `K/wp-079g/evidence/S01-KALANLAR.md`. Düşük öncelik: ölü yardımcının temp dosyası pid yeniden kullanımı.
