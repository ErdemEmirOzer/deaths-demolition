# WP-085 devir notu — S-08 (05.10)

**Durum:** Unity/Player/Blender koşusu YOK. Son guard pencereleri restore edildi (verified=1, OOS 0). Worktree temiz.
Worktree: `K/wp-085/worktree/deaths-demolition` · dal `wp-085-casing-launch`

## Teslim edilmiş, C-00 entegrasyonu bekleyen
- **55a933cf** @ `wp-085-casing-launch` — üstü 79819ca7 (S-02 WP-087 kovan olayı; ÖNCE o entegre edilmeli).
  - Kovan sesi CasingEvents.Launched + PredictLanding ile yerleşiyor.
  - Görünen iniş ↔ ses 0,3–3,5 cm (≤ 25 cm assert). LFS 0.
  - Test: EM Audio 31/31 · PM KovanV2/FoleyV3_Kovan 2/2 · Wp087 KovanOlayi 1/1. Kanıt: `evidence/cl`, `evidence/cl2`.
  - cl koşusundaki 3 EM + S4 kırmızısı LFS pointer kaynaklıydı. Düzeltildi; cl2 temiz.

## Main'de olanlar (05.10)
- v003f 97705c32
- kovan balistik dfc9935c
- mix + S4 v2 3f74e3f4

## Kalanlar (sıra)
1. **AP-027 v003g overlay** (C-00 05.10 duyurdu: 180 kuyruk, M/S mono düzeltmesi; SHA256SUMS 181/181 OK).
   - Yeni dal main'den (97705c32+):
     `cd K/wp-085/worktree/deaths-demolition && git switch -c wp-085-b5-v003g main`
   - Switch'ten sonra `git lfs ls-files | grep " - "` BOŞ olmalı. Değilse `git lfs checkout <yollar>` (ders: SKIP_SMUDGE pointer bırakır).
   - İçe alım:
     `python3 K/wp-085/tools/ap027_import.py <worktree> ar1 ar2 smg1 smg2 sg1 sg2 lmg1 lmg2 sr1 rl1`
     (v003g importer'da hazır; beklenen v003g 180 overlay)
2. **Unity koşusu:**
   - `K/wp-085/tools/v3f-unity.sh v3g` (katalog build + EM Audio + S4 v2; guard dahil).
   - Önce disk ≥ 16 GB, swap ≤ 12 GB.
   - Hall WetDb −7, Corridor −8, Open −9 kalsın. LUFS'yi codexLufs (stereo kanal toplamı) ile yeniden kontrol et; −7 kararı v003f'e göre.
3. **Oyun içi mono ölçümü:**
   - `python3 K/wp-085/tools/mono_notch.py "<temporaryCachePath>/wp085-edc"` (Tail_*.wav; S4 koşusu üretir).
   - Taban v003f (`evidence/v3f/tails`): açık −16,0 dB @ 630 Hz · koridor −5,3 · salon −6,6.
   - C-00 dosya hedefi: açık −4,8 / koridor −1,2 / salon −2,4.
4. **Allpass yan kanalı** geçicilerde dinle (C-00 isteği).
5. **Commit:** konu "(C-00 05.10)" ile biter; gövdede Test:/Guarded:; LFS 180 nesne C-00'a bildirilir.
6. **ar_1/sg_1 dinleme kiti** (üstten + FP, Kit_ + Stem_, silah kapısı ≥ 6 dB).
   - `SilahKiti_GercekRpm_OyunIci_Kayit` [Explicit], DD_AP027_DIR; yalnız sessiz makinede (yığılma kapısı yük altında düşer).
   - Çıktı `K/wp-085/evidence/b4/kit-v4/` + HAZIR.md; C-00 kullanıcıya sunar.
7. **Açık (sonra):**
   - S3 (dense-40 Player, r2_s3.py) önerildi, yazılmadı.
   - Pump/Cock foley eşlemesi ertelendi.

## Notlar
- Sınıf filtresi (AudioSliceTests) [Explicit] testleri de seçer.
  - S4 + kit uzun sürer; kovan/odak testi için regex kullan: `AudioSliceTests.(KovanV2|FoleyV3_Kovan).*`.
- Guard her koşuda yeni evidence dizini ister (tek snapshot).
- Kurallar: kapı 8, amend/rebase/reset yok, `add -A` yok, SHA yalnız rev-parse'tan sonra.
