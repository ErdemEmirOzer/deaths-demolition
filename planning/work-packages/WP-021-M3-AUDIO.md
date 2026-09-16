# WP-021 — M3 AUDIO: SES SİSTEMİ, OLAY HARİTASI, BÖLGESEL AMBİYANS, HİBRİT MÜZİK KATMANLARI (AP-011 ÜRETİMİ DÂHİL)

> Yürütme promptu (C-00, 2026-09-16, kullanıcı kararıyla doğrudan dağıtım) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-021.

OTURUM VE SAHİPLİK
Birincil sahip: S-08 — Audio (tek writer, tek commit owner; üretim + entegrasyon aynı pakette). S-07 (VFX olayları), S-06 (animasyon olay sözleşmesi ADR-018-1: fire, hit_window, footstep, death_end, pickup), S-03 (spawn/round olayları), S-04 (gold/upgrade olayları) sözleşme soruları için danışılır. Paralel paketler: WP-018 (S-06), WP-019 (S-07), WP-022 (S-09), WP-023 (S-04), WP-025 (S-01), WP-026 (S-10) — kendi worktree'lerinde.
Dosya sahipliği: yalnız Assets/_Prototype/Audio/**, Assets/_Art/Audio/**, yeni test dosyaların, planning/04 kendi bölümün, prompt kaydın. Diğer modüllere yalnız olay dinleyicisi ekle (mantık değiştirme); ProjectIntegrityTests.cs'e dokunma (S-01).
Kadro: sound-designer (ses paleti, olay haritası, SFX üretim yönü; Fable 5.1), sound-engineer (LUFS/tepe/bus/mekânsal standart; Opus 5), unity-specialist (AudioMixer, havuzlama, mekânsal ses, perf; Opus 5), csharp-developer (olay bağlama, testler), art-director yerine sound-designer tek yargı.
Model: ses kimliği/yargı Fable 5.1; kod/ölçüm Opus 5.
Çalışma düzeni: HIZLI HAT; Higgsfield-first: SFX/ambiyans/müzik katmanı adayları `generate_audio` / `generate_audio_batch` (modeli `models_explore recommend` ile seç), işleme `sandbox_exec` (ffmpeg/sox: normalize, trim, loop, ogg), Adobe `media_enhance_speech` gerekirse (oturumunda `adobe_mandatory_init` → `get_account_type` ile doğrula; yükleme engellenirse Adobe'siz devam, README notu). Kredi bütçesi ≤ 60 (bakiye 338, taban 200 — S-09 ile paylaşımlı), yalnız gerekli üretim (aday 1, gerekirse 2).
MAKİNE KURALI: aynı anda ≤ 2 Unity batch süreci — başlatmadan önce `ps -Ao comm | grep -c "MacOS/Unity$"` ≥ 2 ise 5 dk bekle; ölçümler tools/perf-clean.sh kalıbı. Yalnız §9'da dur.

1. PAKET KİMLİĞİ
Başlangıç: main HEAD 00bcde0c1ba0a9305c5dba53b60b6de64227f1ab · etiket wp-017-m3-world-v2-armor-r2 (21 etiket) · tracked 2472 · LFS 399 · EditMode 158/158 · PlayMode 23/23 · kullanıcı verisi tabanı wp-010/backup/userdata-postManual-wp010-20260914T230321Z (probe'lar izole).
Branch/worktree: wp-021-m3-audio · ~/Desktop/KADRO/context/projects/deaths-demolition/wp-021/worktree/deaths-demolition · etiket önerisi wp-021-m3-audio. Üretim kökü: ~/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-011/ (README, 03-higgsfield ledger, exports/).
Kaynaklar: planning/00 DEC-053, DEC-064 (hibrit Japon-sinematik müzik), DEC-065 (dinamik katmanlar: aktif düşman sayısı, Special/Elite/Ultra varlığı, round ilerlemesi, tehlike; round arası geri çekilme), DEC-066 (K/D/G/B bölgesel ambiyans, yumuşak geçiş), DEC-067 (zombi sesleri fiziksel, doğaüstü yok; güçlü versiyon/saldırı hazırlığı yön+tehdit bilgisi), DEC-068 (yağmur sonrası atmosfer) · planning/01 REQ-053 (Special Weapon spawn: belirgin ses), REQ-143 (müzik kimliği) ve ses ile ilgili diğer REQ'ler (grep) · mevcut Assets/_Prototype/Audio/ (Runtime/Synthesis/Library — prosedürel yer tutucu) · WP-014 RegionTag/AtmosphereDrift bölge verisi · WP-018 ADR-018-1 olay sözleşmesi (main'e gelince bağla; gelene kadar mevcut prototip olaylarına) · WP-019 VFX olayları · Special Weapon (WP-007), gold (WP-004/008), round/spawn (WP-003/010) olayları · kullanıcı kararı (2026-09-16): ara katman = Unity Audio (FMOD/Wwise yok).
Amaç: Oyunun ses omurgasını kurmak (mixer/bus, olay haritası, havuzlu 3D SFX, bölgesel ambiyans, dinamik müzik katmanları) ve AP-011 ilk ses setini Higgsfield ile üretip bağlamak.

2. BAĞLAYICI KARARLAR
* Unity Audio + AudioMixer (bus: Master / Music / Ambience / SFX-World / SFX-Player / UI; snapshot'lar: Run, RoundBreak, Pause, Menu); ses havuzu (≤ 32 eşzamanlı 3D kaynak, öncelik/uzaklık kuralı); mekânsal: 3D uzak-kamera dinleyici (kamera değil, oyuncu odaklı dinleyici konumu — teknik karar ADR-021-2).
* Olay haritası (ADR-021-1): ses olayı adları ↔ oyun olayları (silah ateşi, vuruş yüzey tipi ×5, zombi saldırı hazırlığı/vuruş/hit/ölüm, zırh kırılma, gold düşme/çekilme/toplama, Special spawn/alma/bitim, round başı/sonu, upgrade, UI); WP-018 olayları geldiğinde ayak sesi/saldırı penceresi buna eşlenir.
* Bölgesel ambiyans: 5 bölge (merkez/kuzey/doğu/güney/batı) + ortak atmosfer katmanı (yağmur sonrası damla, rüzgâr, bambu, su); geçiş crossfade ≥ 2 s (DEC-066); bölge verisi WP-014 RegionTag/placement poligonları.
* Müzik: DEC-064/065 — 4 katman (temel doku, vurmalı, gerilim, Special/Elite/Ultra vurgusu) + round arası geri çekilme; katman karışımı parametrik (`MusicIntensity` 0–1 = aktif düşman/tehdit; tier varlığı ayrı katman); ilk sürüm 2 dakikalık döngü seti (Higgsfield müzik üretimi; kalite yeterli değilse prosedürel yer tutucu + README).
* Standartlar (sound-engineer): -16 LUFS integrated müzik, -18 SFX ortalama, tepe ≤ -1 dBTP; ogg vorbis q6 (SFX), streaming müzik; sevk ≤ 40 MB.
* Okunabilirlik (DEC-067/REQ-104 işitsel): zombi saldırı hazırlığı sesi ≥ 0,3 s önce ve yönlü; Special spawn sesi rezerv (başka olayda kullanılmaz).
* Paket/sürüm değişikliği yok; LFS aynen (ogg/wav uzantıları .gitattributes'ta — kontrol); push yok.

3. İZLENEBİLİRLİK
DEC-053/064–068 · REQ-053/143 · ADR-018-1 (WP-018) · WP-019 VFX olayları · WP-014 bölge verisi · AP-011 (Codex planı: ses, S-08).

4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Ses prosedürel yer tutucu; mixer/bus yok. 4.2 Olay haritası yok. 4.3 Bölgesel ambiyans/müzik yok. 4.4 Üretilmiş SFX yok.

5. UYGULAMA KAPSAMI
A. AP-011 üretim: ses paleti sayfası (sound-designer, Fable), Higgsfield üretim listesi (öncelik: silah ateşi ×2, vuruş ×5 yüzey, zombi hazırlık/saldırı/hit/ölüm ×3'er, zırh kırılma, gold ×3, Special beacon, round başı/sonu, UI ×4, ambiyans 5 bölge + ortak, müzik 4 katman) → sandbox_exec işleme → exports/ + AP011-audio-manifest.json (olay adı, dosya, LUFS, döngü, 3D/2D, bus). B. AudioMixer + bus + snapshot + `AudioCatalog` (manifest → asset, idempotan Editor aracı) + `SfxPool`. C. Olay bağlama (`AudioEventRouter`), yüzey tipi eşlemesi (S-07 SurfaceType ile aynı tablo — gelene kadar yerel eşleme). D. `RegionAmbience` (5 bölge + crossfade) + `MusicDirector` (katman/intensity/round-break). E. Ölçüm: eşzamanlı kaynak tavanı testi, LUFS/tepe kontrol betiği (ffmpeg loudnorm), perf (audio thread/CPU ≤ 1 ms), GC 0. F. Sound-designer tek yargı: olay haritası dinleme turu (10 olay) ↔ DEC-067 kimliği. G. Belge: planning/04 WP-021 + ADR-021-1/2 + AP tablosu (AP-011 üretildi/entegre); prompt kaydı; Assets/_Art/Audio/README.md.

6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi kuralları; olay kaynakları (yalnız dinlenir); Packages/ProjectSettings (AudioManager ayarları hariç: sanal ses sayısı); Unity sürümü; diğer oturumların dosyaları; planning karar metinleri.

7. TEST MATRİSİ
EditMode: catalog dönüştürücü idempotans, olay haritası tamlık testi (her oyun olayı için ses girdisi), LUFS/tepe manifest testi, bus/snapshot tablosu. PlayMode (Metal): olay → ses tetikleme (10 olay), bölge geçişi crossfade, müzik intensity tepkisi, havuz tavanı, GC 0. Build/smoke: release build bir kez + e1/e4 (ses çıkışı log doğrulaması); restore 6/6.

8. GIT VE TESLİM
Özel dal main'e rebase (başkası önce merge olduysa) → tek commit (paket yolları, --pathspec-from-file, LFS doğrulama), mesaj "WP-021: M3 audio — mixer/buses, event map, regional ambience, hybrid music layers, AP-011 first sound set" + "Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>". C-00'a ≤15 satır (SHA, dosya/LFS, ses sayısı, olay kapsamı, LUFS, perf, kredi, yargı, devirler). C-00 ff-merge + etiket.

9. KRİTİK DURMA KOŞULLARI
Bakiye 200 · FMOD/Wwise/paket zorunluysa · müzik üretimi kalite eşiğini tutmuyorsa (prosedürel yer tutucu + raporla, durma) · olay sözleşmesi çakışması (S-06/S-07 ile uzlaş; C-00'a).

10. DEFINITION OF DONE
AP-011 ilk set (≥ 40 SFX + 6 ambiyans + 4 müzik katmanı) manifestli ve normalize · mixer/bus/snapshot · olay haritası tam, 10 olay dinleme turu ACCEPT · bölgesel ambiyans + müzik direktörü çalışıyor · perf/GC notta · testler yeşil, build + smoke, restore 6/6 · 04 girişleri + ADR-021-1/2 + prompt kaydı · tek commit + C-00 notu.
