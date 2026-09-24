M4-R1 · WP-070 — Kamera modları: Tepeden / Üçüncü şahıs / Birinci şahıs
Paket sahibi: S-01 · Ana koordinasyon: C-00
Kayıt: bu promptu birebir `planning/work-packages/WP-070-CAMERA-MODES.md` olarak kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`
- §0: kullanıcı notları birebir. Bu paket N2'yi ve N18'in kamera API'sini karşılar.
- §4: ortak kurallar bağlayıcı.
- Taban: güncel main. İzole dal `wp-070-camera-modes` + worktree.

KULLANICI İSTEĞİ:
"third person ve first person modları olacak oyun esnasında kullanıcı pause edip mevcut kamera açımız (top-down), third person ve first person arasında geçiş yapabilecek."

FAZ A — tasarım (Fable 5.1: game-designer + interaction-designer)
S-02, S-07, S-09, S-06, S-05 ve S-03'ün görüşünü send_message ile al. Çıktı: `planning/work-packages/WP-070-CAMERA-MODES-DIRECTION.md`. Kapsam:

1. Mod tanımları
   - Tepeden: mevcut, değişmez.
   - TP: omuz üstü ofset, omuz değiştirme, FOV, çarpışma/itme, yakınken oyuncu saydamlaşması.
   - FP: göz yüksekliği, FOV 80–90 (ayarlanabilir). Gövde görünür, baş gizli (ShadowsOnly). Görüntü modeli katmanında ayrı FOV; silah duvara girmesin.
2. Kontroller
   - Tepeden: mevcut imleç nişanı.
   - TP/FP: fare bakışı, imleç kilitli, gamepad sağ çubuk, hassasiyet ve Y-ters ayarı.
   - UI Input System okumaz; yalnız InputSourceRegistry kullanılır (ADR-024-2).
3. Hareket
   - TP/FP'de hareket kameraya göre olur, gövde yaw'ı kamerayı izler.
   - Nişan/ateş: namludan çıkış, kamera merkezine doğru, paralaks çözümü (S-02 WP-069 ADR-069-1 ile).
4. Geçiş
   - Pause menüsünde "Kamera" seçimi: UI S-09'da, API ve ayar kalıcılığı sende.
   - Oyun içinde yumuşak geçiş.
5. Oyun etkisi
   - FP'de arkadan gelen zombi için ses/HUD uyarı kancası (S-03, S-08, S-09).
   - Nişan yardımı ve reticle mod başına.
6. Tepeden moda özgü sistemlerin mod uyumu
   - CameraOccluder / çatı soldurma, LOD eşikleri / LOD bias, gölge mesafesi, culling, sis / uzak plan.
   - Dünya tepeden kamera için kuruldu. Göz hizası gereksinim listesini S-05'e ver (WP-074): arka yüzler, iç mekân, ufuk.

İLK TESLİM: CameraMode API taslağı + direction belgesi → S-07, S-09, S-06, S-05, S-03 ve S-02'ye yolu bildir. API içeriği:
- mod durumu ve olayları
- FP görüntü modeli render katmanı
- nişan ışını
- kamera tepki kancaları (S-07 FeelRuntime)

FAZ B — uygulama (Opus 5.5)
Faz A'dan sonra beklemeden geç.
1. CameraMode servis + olaylar.
2. TP rig + çarpışma.
3. FP rig + baş gizleme + görüntü modeli (URP renderer feature, ayrı FOV).
4. Girdi haritası: fare bakışı / gamepad, hassasiyet.
5. Ayar kalıcılığı: ui-settings.json şeması. Test kökü ve smoke kullanıcı-verisi korumaları sürer.
6. Mod başına LOD / gölge / occluder profili.
7. QaRunBot üç modda koşabilsin (S-10 WP-078 için).
8. Testler: değişen süit.
Mevcut kamera kodunun (WP-062K kamera yaw, FeelRuntime) sahipleriyle (S-02, S-07) koordine ol.

Perf: §4.8 (ölç, raporla, kısma).

TESLİM: C-00'a (`local_127f5c6d-7e29-42ba-9ed7-95d2aacfb6f8`) ≤10 satır — SHA @ dal, değişenler, kanıt kareleri (3 mod), test, açıklar. Dilim başına ayrı commit.
