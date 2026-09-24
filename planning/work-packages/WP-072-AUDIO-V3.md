M4-R1 · WP-072 — Ses v3: silahlar, Special'lar, mekân ve perspektif
Paket sahibi: S-08 · Ana koordinasyon: C-00
Kayıt: bu promptu `planning/work-packages/WP-072-AUDIO-V3.md` dosyasına birebir kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`
- §0: kullanıcı notları birebir. Bu paket N17'yi ve N1/N12'nin ses tarafını karşılar.
- §4: ortak kurallar bağlayıcı.
- Taban: güncel main. İzole dal `wp-072-audio-v3`.

Kullanıcı isteği: "her silahın ve fonksiyonlarının sesleri de farklı ve çok daha gerçekçi metalik olacak."
- Bu not WP-065 dinleme kontrol listesinin silah maddesinin fiilî cevabı sayılır.
- Kalan dinleme soruları açık kalır.

FAZ A — tasarım
Fable 5.1 sound-designer lider; sound-engineer teknik standartları verir.
Girdi: S-02 WP-069 kimlik sayfası.
Çıktı: `planning/work-packages/WP-072-AUDIO-V3-DIRECTION.md`.

1. Silah başına ses kimliği (10 silah + Launcher + 2 Special). Katman modeli:
   - mekanik/tetik
   - gövde/punch
   - metalik mid/ring
   - tail (ortama bağlı)
   - sub
2. Reload foley olay başına: mag_out/in, sürgü, kovan/fişek, chamber. Ayrıca dry-fire, switch, ADS.
3. Kovan düşüşleri: yüzeye göre.
4. Darbe sesleri yüzeye göre: taş, ahşap, sıva, toprak, su, yaprak, et, zırh metali.
5. Launcher: fırlatma, roket uçuş döngüsü, yakın ve uzak patlama.
6. Special'lar:
   - DG-2 benzeri: şarj vınlaması, zap, zincir ark çatırtısı, elektrik çarpması vızıltısı.
   - Sliquifier benzeri: jel atışı, şap, kayma, birikinti fokurtusu.
7. Perspektif mix: FP yakın/geniş stereo, TP, tepeden. Kaynak: S-01 WP-070 CameraMode olayları.
8. Mekân tail'leri: açık avlu, koridor, kapalı salon. S-05 WP-074 dünya v5'te kapalı alanlar ekleniyor → bölge verisiyle reverb/tail seçimi.
9. Tekrar önleme: varyant, pitch ve filtre rastgeleliği.
10. Ses bütçesi: yüksek RPM'li LMG/SMG için ses çalma (voice stealing) ya da otomatik ateş döngüsü.

FAZ B — üretim ve entegrasyon
- Higgsfield Seed Audio katmanları: tavan 35 kr. Önce `balance`, sonra `transactions`. R1 tabanı 193,5.
- Sentez, işleme ve katmanlama: gerçekçi metalik karakter.
- AP-011 v016 ve sonrası; olay haritası; mixer. Mevcut LUFS/tepe standartları korunur.
- Eski 3 Special'ın sesleri S-02 kaldırmasıyla birlikte çıkar.
- Testler: değişen süit.
- Kullanıcı için kısa dinleme kiti: silah başına A/B kaydı.

TESLİM: C-00'a ≤10 satır — SHA @ dal, kimlik sayısı, kredi, test, dinleme yolu. Dilim başına ayrı commit.
