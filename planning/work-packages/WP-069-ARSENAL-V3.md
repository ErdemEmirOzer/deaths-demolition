M4-R1 · WP-069 — Arsenal v3: 10 klasik silah + 2 Special + projectile sistemi
PAKET SAHİBİ: S-02 · ANA KOORDİNASYON: C-00 · KAYIT: bu promptu birebir `planning/work-packages/WP-069-ARSENAL-V3.md` olarak kaydet.

ÖNCE OKU: `~/Desktop/KADRO/context/projects/deaths-demolition/m4-r1/M4-R1-REVIZYON-TURU.md`. §0'da kullanıcının 24.09 oyun notları birebir var. Bu paket N1, N8, N12 ve N16'nın mantık tarafını karşılar. §4 ortak kurallar bağlayıcıdır. §6'da 🔴 bekleyen kararlar var; karar gelene kadar öneri varsayılan sayılır. Taban: güncel main (97f904a veya üstü), izole dal `wp-069-arsenal-v3` + worktree.

KULLANICI İSTEĞİ (özet, birebir metin §0'da):
- Silahlar: 2 AR, 2 SMG, 2 Shotgun, 2 LMG, 1 Sniper, 1 Launcher (RPG benzeri). Her birinin kendi mermisi, animasyonu, reload'u, mermi hızı ve özellikleri olacak.
- Mermiler projectile olacak. Ateş, gidiş ve çarpma profesyonel olacak; mermi hızı silahtan silaha değişecek.
- Yalnız 2 Special olacak: CoD BO Zombies Wunderwaffe DG-2 benzeri (zincir yıldırım) ve Sliquifier benzeri (kaydırıcı jel). Oynamalar yapılabilir ama mekanik, mantık ve sanat yönü çok benzer kalacak. Tasarım ve efekt üst düzey olacak.

FAZ A — tasarım (Fable 5.1 konseyi)
Konsey: game-designer (lider), art-director, sound-designer, animation-specialist. S-07, S-08, S-06, S-04 ve P-07'nin görüşünü send_message ile al.
Çıktı: `planning/work-packages/WP-069-ARSENAL-V3-DIRECTION.md`.

A1 · Arsenal kimlik sayfası (10 silah). Her silah için:
- Özgün ad, rol ve his. Dünya ve tema diliyle uyumlu olmalı; AP-013 A tüfek / B karabina / C pompalı görsel dili devralınabilir.
- Hasar, RPM, şarjör/yedek, reload (taktik ve boş).
- Mermi: hız (m/s), düşüş/yerçekimi, penetrasyon.
- Dağılım: kalça ve nişan, üç kamera modu (tepeden/TP/FP) için ayrı.
- Recoil deseni ve toparlanma, nişan süresi, hareket çarpanı, menzil düşüşü.
- Bölgesel zırh etkileşimi (WP-006) ve kafa çarpanı.
- Profiller: mermi görseli (iz rengi, uzunluk, kalınlık, her N'de iz; yavaş mermide mesh), namlu, kovan, yüzeye göre darbe.
- Kimlikler: ses, animasyon (idle / fire / reload taktik+boş / switch / ADS; TP tüm gövde + FP kol) ve kamera tepkisi (mod başına).
- Dükkân/kilit: S-04 WP-077 ile birlikte.
- Launcher için ek olarak: roket itkisi, uçuş, iz dumanı, patlama yarıçapı/düşüşü, kendine hasar kuralı.

A2 · İki Special
(i) Zincir yıldırım — DG-2 mekaniği:
- Elektrik küresi ilk hedefi öldürür, yakındaki N zombiye sıçrar.
- Elektrik çarpması tepkisi, düşük şarjör ve yedek, yakın mesafe riski.
- Sanat yönü retro-teknik: cam tüpler, bobinler, pirinç ve ahşap.

(ii) Kaydırıcı jel — Sliquifier mekaniği:
- Jel doğrudan isabette öldürür veya devirir.
- Yüzeye yapışıp kaygan birikinti bırakır; üstüne basan zombi kayar, düşer ve ölür (zincirleme).
- Oyuncu birikintide hızlanarak kayar.
- Birikinti süresi ve sayı tavanı tanımlanır.
- Sanat yönü: canlı, parlak jel ve cam hazneli silah.

Ad ve tasarım (🔴 #3): varsayılan özgün ad + kendi modelimiz. Belgede iki sütun tut: özgün öneri · orijinal referans.

Yaşam döngüsü: WP-007 (pickup/beacon/süre) ve DEC-024..026 korunur ya da gerekçesiyle revize edilir.

Mevcut 3 Special kaldırılır: KaguraWave, OfudaSealWeb, TideWell/Tide Jewel. Kod, asset, UI, ses, VFX ve test referanslarıyla birlikte temiz kaldır; git geçmişinde kalır. Tüketicileri (S-03 WP-055, S-07, S-08, S-09) önceden bilgilendir.

A3 · ADR-069-1 projectile mimarisi
- Mermi başına GameObject yok; havuzlu struct dizisi.
- Sabit adım + alt adım (hızlı mermi tünellemesi olmayacak), süpürmeli ışın/küre testi.
- Deterministik test saati, 0 B/kare ayırma, eşzamanlı mermi tavanı.
- Mevcut hasar işlemi korunur: DamageTarget, TransactionId, ADR-034-1 pellet, ADR-056-1 SpecialUsageEvents.
- S-07'ye görsel olay yüzeyi: doğma / uçuş / çarpma (konum, normal, yüzey etiketi, hız, silah kimliği).
- S-06/S-03'e darbe verisi: yön, kuvvet, kemik, silah sınıfı.
- TP ve FP'de nişan ışını kamera merkezinden çıkar (S-01 WP-070 CameraMode API); namlu paralaksı düzeltilir.

A4 · WeaponDefinition v3 veri sözleşmesi
- SO; sayılar veride tutulur.
- A/B/C'den ve veri-yalnız D'den göç planı.

İLK TESLİM: A1 + A4. P-07, S-06, P-06, S-07, S-08, S-04 ve S-09 bunu bekliyor. Yayımlayınca bu oturumlara send_message ile yolu bildir, C-00'a da 1 satır yaz. Kalan A2/A3 hemen ardından gelsin.

FAZ B — uygulama (Opus 5.5)
Faz A'dan sonra C-00'ı beklemeden başla; itiraz gelirse uyarla. Dilimler (her biri ayrı commit, her biri C-00'a teslim):
- B1: sözleşme v3 + projectile çekirdeği + A/B/C göçü.
- B2: 10 silahın verisi ve davranışı. Görseller (P-07 AP-021) gelene kadar AP-013 blockout veya yer tutucu kullan, oynanabilir kalsın. Alım dilimi P-07 teslim edince yapılır.
- B3: Launcher (roket, patlama, kendine hasar).
- B4: iki Special mantığı + eski üç Special'ın kaldırılması.
Seçim ve dükkân veri yüzeyleri S-09 ve S-04 ile birlikte yapılır. Testler: değişen süitlerin EditMode + PlayMode koşusu.

KISITLAR
- Denge sayıları Fable konseyinin önerisi olarak yazılır; C-00 onayıyla DEC-09x olur. Numarayı C-00'dan iste.
- Perf ve boyut: §4.8 — ölç, raporla, kademeye bağla; kısma.

TESLİM: C-00'a ≤10 satır (SHA @ dal, değişenler, kanıt, test, açıklar).
