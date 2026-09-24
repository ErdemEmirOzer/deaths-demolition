M4-R1 · WP-071 — Animasyon v3 (AAA): analiz ve revizyon, motor tarafı
PAKET SAHİBİ: S-06 · ANA KOORDİNASYON: C-00 · KAYIT: bu promptu birebir `planning/work-packages/WP-071-ANIMATION-V3.md` olarak kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`
- §0: kullanıcı notları birebir. Bu paket N5, N6, N7 ve N11'i karşılar; N12 reload ve N16 etkileşim animasyonlarını da kapsar.
- §4: ortak kurallar bağlayıcı.
- Taban: güncel main. İzole dal `wp-071-animation-v3` + ev worktree'n.

KULLANICI İSTEĞİ (özet):
- Zombi ve oyuncu hareket/tepki animasyonları gerçekçi ve akıcı olacak.
- Hız ile ayak hareketi eşit olmak zorunda değil: ayak ve gövde daha yavaş hareket edip kayarak mesafe alabilir.
- Zombilerin 4 farklı ölümü olacak; yerinde çökmeyecek, gerçekçi düşecek.
- Zombilerin 5 farklı saldırısı olacak.
- Rig noktaları ve hareket ultra sinematik olacak.
- Tüm animasyonlar AAA düzeyinde analiz edilip revize edilecek; vuruş, oyuncunun darbe alması ve reload profesyonel ve detaylı olacak.

FAZ A — tasarım (Fable 5.1: animation-specialist + character-animator)
Paydaşlar: S-02, S-03, S-07, S-01, P-06. Çıktı: `planning/work-packages/WP-071-ANIMATION-V3-DIRECTION.md`.

A1 · AAA analiz
- Mevcut 86 klip (AP-009 v007) + Animator controller'lar + oyun içi kayıt.
- Bakış açıları: tepeden, TP ve FP. TP/FP'de her şey çok daha yakından görünecek.
- Madde madde kusur listesi: ağırlık, zamanlama, kayma, geçiş, IK, poz.

A2 · Locomotion v3 (oyuncu + zombi)
- Kadans ve adım hızdan yavaş olabilir: oynatma hızı tavanı, az ya da hiç stride warping, mesafe kayarak alınır.
- Başlangıç, duruş ve dönüş; lean; zemine ayak IK'sı.
- Zombi yürüyüş çeşitliliği.

A3 · Zombi ölümleri
- 4 farklı klip.
- Darbe yönü ve kuvvetiyle güdümlü ragdoll geçişi: kısmi/aktif → tam ragdoll → yerleşme → dondurma.
- Çevreyle çarpışır; yerinde çökme olmaz.
- Eşzamanlı ragdoll tavanı ve kalite kademeleri (§4.7); ceset ömrü.

A4 · Zombi saldırıları
- 5 farklı saldırı; türleri konsey seçer.
- Telegraph/vuruş penceresi olayları; DEC-029 pencere sözleşmesi korunur.
- Special/Elite/Ultra varyant kuralları.

A5 · Tepkiler
- Zombi hit: yön, bölge, silah sınıfı itkisi — pompalı savurur, sniper sendeletir, LMG sürekli tepki verir.
- Oyuncu darbe alma: yönlü ve ağır varyantlar.
- Special tepkileri: elektrikle çarpılma ve jelde kayıp düşme.

A6 · Silah animasyonları
- 10 silahın her biri için: idle, fire additive, reload taktik + boş, switch in/out, ADS.
- TP tüm gövde + FP kol seti; FP görüntü modeli S-01 WP-070'te.
- 2 Special: tutma, ateş, reload.
- Sol el IK silahta; nişan/bakış IK.
- Hareketli parça olayları: şarjör, kilit/sürgü, kovan, roket.

A7 · Rig
- Twist kemikleri, parmaklar (silah tutuşu), ikincil zincirler (WP-032F), el/ayak IK hedefleri.
- Rig güncellemesi gerekiyorsa P-06 yapar.

A8 · Klip listesi + olay sözleşmesi v3
Bu belge P-06 AP-022'nin üretim listesidir. Yayımlayınca P-06, S-03, S-07 ve S-08'e bildir; C-00'a 1 satır yaz. Silah klipleri için S-02 WP-069 kimlik sayfasını ve P-07 AP-021 soketlerini bekle; zombi ve oyuncu klipleri beklemez.

FAZ B — uygulama (Opus 5.5)
Faz A'dan sonra beklemeden geç.
- Animator v3 (üretici betikle) ve ragdoll sistemi.
- Locomotion v3.
- Ölüm/saldırı/hit seçim kancaları (S-03 WP-076 tüketir).
- FP kol entegrasyonu (S-01).
- 0 B/kare; testler değişen süitte.
- P-06 klipleri geldikçe dilim dilim içe al.

KANIT: tepeden, TP ve FP kayıt kareleri/GIF'leri + Fable AD turu. Perf: §4.8.

TESLİM: C-00'a ≤10 satır — SHA @ dal, değişenler, kanıt, test, açıklar. Dilim başına ayrı commit.
