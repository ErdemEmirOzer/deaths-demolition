# WP-015 — M3 ASSET BÜTÇESİ VE BUILD POLİTİKASI: DOKU POLİTİKASI, BUILD BOYUTU, KULLANICI KARARLARININ KAYDI, STİL KILAVUZU v1 FİNAL

> Yürütme promptu (C-00, 2026-09-15) — birebir kayıt. Sonuç kaydı: `planning/04-IMPLEMENTATION-PLAN.md` §WP-015.

OTURUM VE SAHİPLİK
Birincil sahip: S-05 — World, Environment & Lighting (tek writer, tek commit owner). S-01 yalnız build/ProjectSettings sınırı sorusu olursa; S-07 yalnız sis/post-fx parametre sorusu olursa; S-10 devrede değil.
Kadro: unity-specialist (doku import politikası, streaming, build analizi — ana yük), csharp-developer (asset-gate testleri, override aracı), art-director (stil kılavuzu v1 final, sis ağırlığı yargısı; Fable 5.1), technical-writer (planning DEC/REQ kayıtlarının birebir yazımı).
Model politikası: stil kılavuzu ve sanat yargısı Fable 5.1; import, build, test, Git işleri Opus 5.
Çalışma düzeni: HIZLI HAT direktifi bağlayıcıdır — manifest/PRA/kit/inceleme turu yok; yalnız değişen modül testleri; tek commit; ≤15 satır not. Ajanlar görev tamamlanana kadar kesintisiz çalışır; yalnız §9 durma koşullarında dur. Yeni ürün kararı verme; §2'deki kullanıcı kararları birebir kaydedilir.

1. PAKET KİMLİĞİ
Başlangıç noktası: main HEAD `034f3668b96e8b744c1e57af04b8f63aaae6a988` · etiket `wp-014-m3-world-assembly` (16 etiket) · tracked 2115 · LFS 305 · EditMode DD.Art+ProjectIntegrity 116/116 · PlayMode DD.Art 16/16 · Run+AP005 perf ort. 3,23 / p99 4,04 ms @ 40 düşman 1080p · release build 654,7 MB · doku belleği 521,6 MB (AP005) / 206,9 MB (prosedürel) · kullanıcı verisi tabanı `wp-010/backup/userdata-postManual-wp010-20260914T230321Z` (SaveVersion 3; probe'lar izole).
Branch/worktree: `wp-015-m3-asset-budget` · `~/Desktop/KADRO/context/projects/deaths-demolition/wp-015/worktree/deaths-demolition` · etiket önerisi `wp-015-m3-asset-budget`.
Başlangıçta doğrula: HEAD/etiket, porcelain 0, LFS pointer bütünlüğü, son DEC/REQ numaraları (`planning/00` DEC-075, `01` REQ-155 — doğrula).
Kaynaklar: WP-011 ADR-011-3 (perf bütçesi, doku 128 MB), stil kılavuzu v1 taslağı wp-011/reports/art-11-style-guide-v1-draft.md · WP-012 ADR-012-1/2, `ArtImportContract` · WP-013 ADR-013-1/2, `DD_Overcast_v1.asset`, WP-013 devri "90 m'de sis ağırlığı" · WP-014 ADR-014-1/2, `PlacementImporter`, `World_AP005.unity`, `DD_WorldSource.asset`, WP-014 devirleri (doku bütçesi, build boyutu, 3 bambu gövdesi i1535/i1605/i1532, stand-in zemin) · AP-006 Faz 1 oyuncu işareti kuralı (`AP-006/02-concept/`, P-05 notu: bel bandı + baş aksanı, aksan v ≥ 0,55 & s ≤ 0,55; zombi kumaşından Δv ≥ 0,3) · `planning/00/01/04`.
Amaç: Gerçek sanat dünyasının doku/build maliyetini bütçeye çekmek, kullanıcının 2026-09-15 kararlarını planning'e kaydetmek, nihai palete göre stil kılavuzu v1'i sonlandırmak ve WP-013/014 devirlerini kapatmak.
2. BAĞLAYICI KARARLAR (kullanıcı, 2026-09-15 — birebir kaydedilir)

* Palet/hava nihai: Presentation A referansı + DEC-068 (yağmur sonrası, hafif sisli, kapalı hava) nihai palettir. → `planning/00`'a yeni DEC (numara devam eder) + stil kılavuzu v1 final.
* Okunabilirlik eşikleri resmi: kapsül örtülme >%50 olan rota örnek noktası = 0; oyuncu/çevre kontrastı ≥ 0,06 (WP-011/013/014 yöntemi). → REQ-104/105 kabul ölçütlerine ölçülebilir ek + yeni DEC; testler bu eşikleri assert eder.
* Harita çevre zemini: Unity stand-in zemin (±80 m) kalır; tam harita zemin/patika plakaları AP-015 kapsamında üretime devredilir. → DEC.
* Rota/köprü genişliği: güney köprüsü 1,2 m tek sıra ve kuzey 14 m koridor değişmez; playtest sonrası yeniden değerlendirilir. → DEC.
* Windows hedefi: süresiz ertelendi; ayrı kullanıcı kararı gelene kadar yalnız macOS (M1 Pro) ölçülür. → DEC (WP-011 §23 #3 kapanır).
* AP-006 karakter yönü C (ronin, epik kostüm) → `planning/04` AP tablosu notu (DEC yok; üretim kaydı).
Teknik varsayılanlar (sahip kararı, ADR olarak yazılır): doku politikası hedefi AP005 dünyasında doku belleği ≤ 160 MB ve release build ≤ 400 MB (ADR-011-3 bu ölçümle güncellenir); Unity/URP sürümü ve paket değişikliği yok; LFS aynen; push yok.

3. İZLENEBİLİRLİK
DEC-012/013/014/068 · REQ-100–114 (özellikle 104/105) · ADR-011-3, ADR-012-1/2, ADR-013-1/2, ADR-014-1/2 · WP-011 §23 #1/#3/#4 (kapanış) · WP-013/WP-014 devirleri · AP-015 (Unity entegrasyonu: doku politikası bu paketle başlar, skinned import AP-006 sonrası).
4. MEVCUT BOŞLUKLAR (doğrula)
4.1 Doku import ayarları kaynak çözünürlüğünde (2K–4K) → 521 MB; mipmap streaming kapalı. 4.2 Dünya sahnesi 107 prefab'ın tamamını build'e alıyor; pilot ve showcase sahnelerinin build listesi durumu netleştirilmemiş. 4.3 Kullanıcı kararları planning'de yok. 4.4 Stil kılavuzu taslak; oyuncu işareti ve zombi palet rezervi yazılı değil. 4.5 3 bambu gövdesi kamera düzleminde; sis 90 m'de ağır. 4.6 Stand-in zemin geçici materyalde.
5. UYGULAMA KAPSAMI
A. Doku politikası (ADR-015-1) — mesafe sınıfına göre maksimum çözünürlük (teknik varsayılan: zemin/paving 2048 tiling, yapı 1024, bitki/atlas 1024, prop ≤ 512, kırılabilir ≤ 512), BC7/BC5 kalite, mipmap streaming açık + bütçe, `ArtImportContract`'a sınıf tablosu; asset-gate testi her dokuyu sınıfına göre doğrular; ölçüm: doku belleği ve build boyutu önce/sonra.
B. Build politikası — release build listesi: Run + gerekli sahneler; pilot/showcase yalnız dev build (ADR-015-2); build içeriği analizi (Editor log) nota; hedef ≤ 400 MB.
C. Planning kayıtları — `planning/00`'a §2 kararları DEC olarak (numara devam), `01`'e REQ-104/105 ölçülebilir kabul ölçütleri, `04`'e WP-015 girişi + ADR-015-1/2 + WP-011 §23 kapanış notları + AP tablosu (AP-006 yön C); metinler kullanıcı kararının birebir çevirisi, yorum eklenmez.
D. Stil kılavuzu v1 final (Fable) — Assets/_Art/StyleGuide/style-guide-v1.md: nihai palet (Presentation A/DEC-068 değerleri), ışık/sis dili, oyuncu işareti kuralı (AP-006 Faz 1), zombi palet rezervi (gri-yeşil/soluk ten aralığı, oyuncudan Δv ≥ 0,3), gold/Special/uyarı beacon renkleri (REQ-105), okunabilirlik eşikleri; WP-011 taslağındaki açık maddeler kapatılır.
E. Devir kapanışları — 90 m sis ağırlığı: nihai palete göre mesafe/yükseklik sisi ayarı (Fable yargısı, ölçüm: okunabilirlik eşiği korunur); 3 bambu gövdesi: `placement-overrides.json` (Unity tarafı, AP-005 kaynağına dokunmadan; importer override'ları uygular ve nota yazar); stand-in zemin: `hero_earth` materyaline eş, dikişsiz, gölge/lightmap tutarlı.
F. Ölçüm — `WorldPerfProbe` tek koşu (doku belleği, ort./p99, draw), okunabilirlik testi (eşikler artık resmi), görsel regresyon 5 kadraj (doku düşüşü görünür kalite kaybı yaratmıyor mu — Fable yargısı).
G. Belge — prompt kaydı `planning/work-packages/WP-015-*.md`; Assets/_Art/README.md doku sınıf tablosu.
6. DEĞİŞTİRİLMEYECEK KONULAR
Oynanış/ekonomi/profil/UI kodu; prosedürel dünya; spawn/rota/marker düzeni; placement.json kaynağı (override dosyası ayrı); Import Contract v1 eksen/materyal kuralları (yalnız doku sınıf tablosu eklenir); Packages/ProjectSettings (build listesi hariç); Unity/URP sürümü; AP üretim klasörleri; §2 dışındaki planning metinleri.
7. TEST MATRİSİ (yalnız işle ilgili)
EditMode: doku politikası asset-gate (sınıf/çözünürlük/format/streaming), build listesi testi (release sahneleri), placement override testi (3 id uygulanmış, digest), planning bütünlük testi (DEC/REQ numara tekliği, mevcut ProjectIntegrity kalıbı). PlayMode (Metal): `WorldPerfProbe` tek koşu, okunabilirlik eşik testi. Build/smoke: release build bir kez + e1/e4 smoke (AP005 WorldSource); kullanıcı verisi restore 6/6 son koşudan sonra. Zamanlamaya duyarlı koşular öncesi C-00'a tek satır (P-05 Faz 2 pişirme batch'leriyle çakışma kontrolü).
8. GIT VE TESLİM
İzole dalda tek commit, yalnız paket yolları (`--pathspec-from-file`), `git add -A` yok, LFS doğrulaması (doku kaynak PNG'leri değişmez; yalnız .meta import ayarları); mesaj `WP-015: M3 asset budget & build policy — texture policy, build list, user decisions DEC/REQ, style guide v1 final` + `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`. C-00'a ≤15 satır: SHA, dosya sayısı, doku belleği/build boyutu önce–sonra, perf, okunabilirlik, DEC/REQ numaraları, stil kılavuzu yolu, Fable yargısı, devirler. C-00 `--ff-only` merge + etiket.
9. KRİTİK DURMA KOŞULLARI
Doku bütçesi görünür kalite kaybı olmadan (Fable REJECT) tutmuyorsa (ölçümü ve iki seçeneği raporla) · Addressables/streaming paketi zorunlu görünüyorsa · Unity/URP sürümü · §2 kararlarıyla mevcut DEC metinleri çelişiyorsa (çelişkiyi raporla, eskisini silme).
10. DEFINITION OF DONE
Doku politikası uygulanmış, doku belleği ≤ 160 MB ve release build ≤ 400 MB (sayılar notta) veya gerekçeli sapma · build listesi politikası · §2 kararları DEC/REQ olarak planning'de · stil kılavuzu v1 final repo'da · sis ayarı, bambu override'ları, stand-in zemin kapalı · testler yeşil, perf/okunabilirlik eşikleri PASS, release build + smoke, restore 6/6 · `04` girişleri + ADR-015-1/2 + prompt kaydı · tek commit + C-00 notu.
Şimdi çalışmaya başla. Kritik kullanıcı kararı gerektirmeyen hiçbir noktada durma.
