# WP-006 — M2 Regional Armor & Explosion Damage Slice

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-12'de `S-03 — Enemies, AI, Rounds & Spawning` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-006-m2-regional-armor` branch'inde (taban: `main` = `c3e9864` = `wp-005-m1-meta-ui`) yapılır; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır. Durum kaydı: `planning/04-IMPLEMENTATION-PLAN.md` → "WP-006".

---

WP-006 — M2 REGIONAL ARMOR & EXPLOSION DAMAGE SLICE

OTURUM SAHİBİ
S-03 — Enemies, AI, Rounds & Spawning

ANA KOORDİNASYON
C-00 — Deaths Demolition Main Coordination

AMAÇ

WP-005 ile tamamlanan M1 dikey kesitini bozmadan M2’nin ilk üretim dilimini gerçekleştir:

- Standard zombiye modüler bölgesel zırh sistemi eklemek.
- Doğrudan isabetlerin vücut bölgesi ve zırh parçasına göre çözülmesini sağlamak.
- Patlama hasarını onaylanmış bölgesel dağıtım kurallarına göre çözmek.
- Zırh hasarı, zırh kırılması ve açık bölge durumlarını animasyon, VFX, ses ve ilerideki gerçek asset üretimi için güvenilir olay sözleşmeleriyle sunmak.
- Spawn/pooling döngüsünde zırh durumunun güvenli ve deterministik şekilde kurulup sıfırlanmasını sağlamak.

Bu paket yeni düşman türü, yeni yetenek, yeni denge değeri veya yeni ürün kararı oluşturmayacaktır.

DOĞRULANACAK TABAN

Repository:
 /Users/Erdem/Documents/Deaths Demolition

Unity projesi:
 /Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept

Beklenen main tabanı:
 c3e9864e03d5bbed1eb9da55d7e8dd1c2392e22d

Beklenen etiket:
 wp-005-m1-meta-ui

Başlangıçta bunları varsayma; salt okunur Git kontrolleriyle doğrula:

- branch
- HEAD
- tag
- çalışma ağacı temizliği
- WP-005 entegrasyonu
- mevcut worktree ve paket zinciri

Taban farklıysa kod yazmaya başlamadan C-00’a bildir.

KADRO VE PROMPT YÖNETİŞİMİ

C-00 ve S-03 uygulama alt görevlerini tek başına doğrudan dağıtmayacak.

Önce gerçek KADRO sistemindeki şu prompt uzmanlarını kullan:

- prompt-engineer
- pra
- context-engineer

Bu uzmanlar:

1. Bu ana promptu bağlayıcı kaynaklara göre ayrıştırmalı.
2. Her uzman için dar kapsamlı görev promptları hazırlamalı.
3. Promptların yeni ürün kararı veya sayısal denge üretmediğini denetlemeli.
4. Gereksinim, uygulama, test ve kanıt bağlantısını korumalı.
5. Nihai görev promptlarını bağımsız incelemeden geçirmeli.

Gerekli alan uzmanlarını iş kapsamına göre görevlendir. Asgari değerlendirme kadrosu:

- S-03: paket sahibi ve tek yazma sorumlusu
- S-02: silah, projectile, hit detection ve damage entegrasyonu
- S-01: mimari ve entegrasyon incelemesi
- S-04: mevcut gold/kill hattının korunması
- S-06: hit reaction ve armor-break animasyon sözleşmeleri
- S-07: flesh hit, armor hit ve armor-break VFX sözleşmeleri
- S-08: konumsal armor hit/break ses olayları
- S-09: yalnız mevcut UI/HUD sözleşmesi etkileniyorsa inceleme
- S-10: bağımsız test ve kabul
- unity-specialist
- game-developer
- game-test-engineer
- test-engineer
- gt
- git-repository-safety

Gereken uzman ve ajan sayısında yapay sınır kullanma. Ancak aynı dosya üzerinde birden fazla eşzamanlı yazıcı bulunmasın. S-03 paket ve commit sahibidir; diğer uzmanlar salt okunur inceleme, öneri ve test kanıtı üretir.

Ajansal çalışma, kabul koşulları sağlanana kadar kesintisiz devam etmelidir. Yalnız kritik durma koşullarında kullanıcı onayı istenir.

BAĞLAYICI KAYNAKLAR

Başlamadan önce bütünüyle incele:

- planning/00-PROJECT-PLAN.md
- planning/01-REQUIREMENTS.md
- planning/02-FLOWS-ARCHITECTURE.md
- planning/03-ROADMAP.md
- planning/04-IMPLEMENTATION-PLAN.md
- planning/work-packages altındaki WP-000–WP-005 kayıtları
- WP-005 nihai raporu
- Mevcut damage, projectile, enemy, round, spawn, pooling, gold, hit-feedback ve test kodları

Özellikle REQ-080–REQ-090, REQ-063, REQ-115 ve REQ-129 ile FLOW-007/FLOW-008’i doğrula.

Belgelerde açıkça onaylanmamış bir öneriyi karar olarak kullanma. Çelişki bulursan kendi yorumunla çözme; C-00 üzerinden kullanıcıya yükselt.

DEĞİŞMEZ ÜRÜN SÖZLEŞMELERİ

1. Armored Enemy ayrı bir temel düşman türü değildir.

Standard zombiye aynı düşman üzerinde modüler bileşenler eklenir. Aynı temel düşman:

- zırhsız,
- tek parçalı,
- iki parçalı,
- üç parçalı

zırh kombinasyonlarında bulunabilir.

Special, Elite ve Ultra bu paketin kapsamında değildir.

2. Üç bağımsız koruma bölgesi bulunur.

- Helmet: kafa bölgesini korur.
- Torso Armor: gövde ve kolları korur.
- Leg Armor: alt vücudu korur.

Bir zırh parçası yalnızca bağlı olduğu bölgeyi korur.

3. Doğrudan isabet kuralı

Doğrudan mermi/isabet yalnız vurulan bölge üzerinden çözülür.

- Bölgede zırh varsa hasar o zırha gider.
- Bölgede zırh yoksa hasar doğrudan düşmanın canına gider.
- Bir bölgenin zırhı başka bölgeyi korumaz.
- Zırh kırıldığında artan hasar aynı vuruş içinde cana aktarılmaz.
- Zırh dayanıklılığını aşan hasar boşa gider.
- Zırh kırıldıktan sonraki yeni isabetler o bölgeden cana hasar verebilir.
- Onaylanmamış headshot veya bölgesel kritik hasar çarpanı eklenmez.

4. Patlama hasarı kuralı

Patlamanın toplam hasarı kafa, üst vücut ve alt vücut arasında yapılandırılabilir oranlarla dağıtılır.

- Bölge paylarının toplamı patlamanın orijinal toplam hasarına eşit olmalıdır.
- Hasar zırh parçası sayısına bağlı olarak çoğaltılmamalıdır.
- Korunan bölgenin payı yalnız o bölgenin zırhına gider.
- Açık bölgenin payı doğrudan düşmanın canına gider.
- Bir zırh parçası kendi payıyla kırılırsa artan hasar aynı patlamada cana aktarılmaz.
- Patlama üzerinden bölgesel kritik hasar çarpanı uygulanmaz.
- Üç zırh parçasının tamamı mevcut ve sağlamken patlama cana hasar vermez.
- Parçalardan biri kırıldıktan sonra sonraki patlamalarda açık bölgenin payı cana ulaşabilir.
- Kısmi zırh kombinasyonlarında açık bölgelerin payları cana, zırhlı bölgelerin payları ilgili zırhlara uygulanır.

Patlamanın çarpışma noktası bu sözleşmeyi geçersiz kılan ayrı bir “tam can hasarı” yolu oluşturmamalıdır.

5. Görsel ve işitsel takip edilebilirlik

Sistem en az şu durumları birbirinden ayırabilecek olay verisi üretmelidir:

- flesh hit
- helmet armor hit
- torso armor hit
- leg armor hit
- armor damaged
- armor broken
- direct health damage
- explosion armor damage
- explosion health damage
- enemy death

Olaylar mümkün olduğunca şunları taşımalıdır:

- düşman/yaşam kimliği
- damage source
- damage kind
- body region
- armor slot
- armor before/after
- absorbed damage
- health damage
- armor broken flag
- world position
- hit direction veya normal
- event/transaction identity

Aynı fiziksel hasar için çift ses, çift kan efekti veya çift hit reaction oluşmamalıdır. WP-002’de kurulan tek geri bildirim sahipliği korunmalıdır.

PAKET İÇİ UYGULAMA

A. Bölgesel hit modeli

- Kafa, üst vücut ve alt vücut bölgelerini güvenilir biçimde tanımla.
- Collider/hitbox ile gameplay damage modelini gevşek bağlı tut.
- Görsel mesh veya kemik düzeninin hasar mantığının tek doğruluk kaynağı olmasına izin verme.
- Hitbox eksikliği veya geçersiz bölge sessizce yanlış bölgeye hasar vermemeli.
- Gerekli fallback davranışı açık, deterministik ve testli olmalıdır.

B. Zırh durumu

- Üç slot birbirinden bağımsız çalışmalıdır.
- Her slot için varlık, sağlamlık, kırılma ve koruma durumu modellenmelidir.
- Zırh kırılması idempotent olmalıdır.
- Aynı olay veya bilet iki kez işlendiğinde tekrar kırılma, tekrar ödül veya tekrar feedback oluşmamalıdır.
- Pool’a dönen düşmanın bütün zırh, hitbox ve olay durumu güvenli biçimde sıfırlanmalıdır.
- Yeniden kullanılan düşman önceki yaşamına ait zırh durumunu taşımamalıdır.

C. Damage routing

- Direct hit ve explosion çözüm yolları açıkça ayrılmalı fakat ölüm, health ve event sistemleriyle ortak sözleşmede birleşmelidir.
- Mevcut düşman ölümü, kill, gold drop ve round ledger akışları bozulmamalıdır.
- Ölü düşmana gelen geç hasar yeni ölüm, gold, kill veya armor-break olayı üretmemelidir.
- Tek bir hasar olayı tek bir canonical çözüm sonucuna sahip olmalıdır.

D. Round/spawn entegrasyonu

- Standard zombiye 0/1/2/3 zırh bileşimi uygulanabilmelidir.
- Sistem ileride round bazlı oranlar ve elle belirlenen hibrit override’lar alabilecek şekilde yapılandırılmalıdır.
- Exact debut round, oran, ağırlık ve dayanıklılık değerleri bu pakette ürün kararı olarak belirlenmez.
- Oynanabilir/test edilebilir geçici değer gerekiyorsa tek merkezde ve açıkça PrototypeOnly olarak işaretlenir.
- Spawn yalnız mevcut EnemySpawnDirector ve kayıtlı spawn noktaları üzerinden devam etmelidir.
- Spawn davranışı, aktif düşman hedefi, teknik tavan ve round havuzu kuralları değiştirilmemelidir.

E. Görsel bileşen bağlantıları

İleride şu Japon konseptli parçaların bağlanabilmesi için açık attachment/state sözleşmesi hazırla:

- kabuto veya maske türü baş koruması
- dō/kote türü gövde ve kol koruması
- haidate/suneate türü alt vücut koruması

Bu paket final modelleri üretmez ve AP-001 staging alanından asset almaz.

Placeholder ile doğrulama gerekiyorsa:

- açıkça PrototypeOnly olmalı,
- final sanat yönü olarak sunulmamalı,
- gerçek asset pipeline’ını sabit bir prefab adına kilitlememeli,
- armor present, damaged ve broken durumlarını test edilebilir kılmalıdır.

F. Performans ve yaşam döngüsü

- Vuruş başına gereksiz allocation oluşturma.
- Frame başına scene search, string oluşturma veya component keşfi ekleme.
- Event aboneliklerinin spawn/despawn ve ikinci koşu sırasında çoğalmasını engelle.
- Pause sırasında armor/damage simülasyonu ilerlememelidir.
- Mevcut tek oyunculu yapı korunmalı, fakat sistem gereksiz biçimde tek bir global Player nesnesine kilitlenmemelidir.

SAYISAL SINIR

Şunları final değer olarak belirleme:

- düşman canı
- zırh dayanıklılıkları
- silah hasarları
- headshot katsayısı
- patlama toplam hasarı
- üç patlama bölgesinin dağıtım oranları
- armored spawn başlangıç roundu
- armored spawn ihtimali
- round bazlı zırh artış eğrisi
- zırhlı düşman gold miktarı

Test veya oynanabilir doğrulama için gereken sayılar açıkça PrototypeOnly olmalı, merkezi konfigürasyonda bulunmalı ve karar belgelerine ürün değeri olarak yazılmamalıdır.

TEST MATRİSİ

En az şu otomatik ve fiziksel senaryoları doğrula:

Direct hit:

1. Zırhsız kafa isabeti canı azaltır.
2. Zırhsız üst vücut isabeti canı azaltır.
3. Zırhsız alt vücut isabeti canı azaltır.
4. Helmet varken kafa isabeti yalnız helmet’a gider.
5. Torso Armor varken gövde/kol isabeti yalnız torso armor’a gider.
6. Leg Armor varken alt vücut isabeti yalnız leg armor’a gider.
7. Bir bölgenin zırhı başka bölgeyi korumaz.
8. Zırh dayanıklılığından büyük doğrudan hasarda overflow cana geçmez.
9. Zırh kırıldıktan sonraki yeni isabet cana ulaşır.
10. Aynı kırılma olayı iki kez üretilmez.

Explosion:

11. Zırhsız düşmanda bölge paylarının toplamı toplam patlama hasarına eşittir.
12. Tek parça zırhta korunan pay zırha, iki açık pay cana gider.
13. İki parça zırhta iki pay zırhlara, açık pay cana gider.
14. Üç sağlam parçalı düşman aynı patlamadan can hasarı almaz.
15. Üç parçadan biri önceden kırılmışsa açık bölgenin payı cana gider.
16. Aynı patlama bütün ilgili zırh parçalarını etkiler.
17. Toplam hasar parça sayısıyla çoğalmaz.
18. Zırh kırılmasındaki overflow aynı patlamada cana geçmez.
19. Patlama paylarına bölgesel kritik çarpan uygulanmaz.
20. Enjekte edilen oranların toplamı korunur ve deterministiktir.

Lifecycle/integration:

21. Pool’dan yeniden kullanılan düşman eski armor durumunu taşımaz.
22. Ölü düşmanda armor-break, death, kill veya gold tekrarlanmaz.
23. Kill hâlâ Standard enemy kill olarak işlenir; yeni düşman tipi icat edilmez.
24. Gold drop ve round ledger tek kez güncellenir.
25. Pause sırasında ilgili zaman tabanlı durum ilerlemez.
26. İkinci koşuda event abonelikleri çoğalmaz.
27. 0/1/2/3 armor kombinasyonlarının tamamı spawn/test harness üzerinden doğrulanır.
28. Flesh, armor hit ve armor break feedback olayları tam birer kez ulaşır.

Testler:

- EditMode
- PlayMode
- Gerçek Player/manual probe
- Tam regression
- Release build
- Player smoke
- Temiz Library doğrulaması, mevcut süreç gerektiriyorsa
- Git F0–F10 denetimi

WP-005 tabanında raporlanan test sayıları en az EditMode 297/297 ve PlayMode 65/65’tir. Gerçek taban raporundan doğrula; yeni testlerle toplam sayı artmalı ve hiçbir mevcut test kaybolmamalıdır.

PLANLAMA BELGELERİ

Yalnız gerçekleşen durumu güncelle:

- WP-005: tamamlandı ve entegre
- M1 dikey kesit: doğrulanan exit koşulları karşılanıyorsa tamamlandı
- WP-006: paket durumu ve gerçekleşen kapsam

Yeni ürün kararı, yeni DEC veya yeni REQ üretme.

WP-005 raporuyla planning dosyaları arasındaki eski durum farkını düzeltmek yeni karar değildir; gerçekleşen entegrasyonun durum kaydıdır.

AP-001 ile ilgili hiçbir dosyaya dokunma. Codex DD-world/environment hattındaki asset üretimi bağımsız ilerlemektedir.

KAPSAM DIŞI

- Special, Elite veya Ultra implementasyonu
- Yeni düşman yetenekleri
- Final zombi modeli veya kıyafet üretimi
- Final Japon zırh assetleri
- Higgsfield, Adobe veya Blender üretimi
- Final animasyon, ses veya VFX assetleri
- Dismemberment
- Yeni silah veya Special Weapon
- Profil seviyesi veya kalıcı stat değişikliği
- Yeni UI ekranı
- Yeni sonuç ekranı alanı
- Yeni armor kill kategorisi
- Final sayısal denge
- B3, B4, B5 kararlarını sessizce çözmek
- REQ-123 marjını değiştirmek
- Sınıf-3 A–H temizliklerini kapsama almak
- Remote oluşturmak veya push yapmak

GİT VE WORKTREE KURALLARI

- Mevcut WP worktree düzenini C-00’dan doğrula.
- WP-006 için izole branch/worktree kullan.
- Main çalışma ağacında doğrudan geliştirme yapma.
- Tek yazma ve commit sahibi S-03’tür.
- Başka paketin worktree’sine yazma.
- Kullanıcıya ait değişiklikleri temizleme veya geri alma.
- reset, clean, rebase, force push ve history rewrite yapma.
- Mevcut etiketleri değiştirme.
- Generated, Library, Temp, Logs, build çıktısı, secret veya `.claude` içeriğini commit’e alma.
- Unity `.meta` dosyalarını koru.
- Manifest v2 ve SHA-256 dosya doğrulaması üret.
- Remote/push yapma.
- S-01 ve S-10 kabulünden sonra C-00 mevcut süreçle fast-forward entegrasyonu yönetebilir.
- Merge commit oluşturma.
- Başarılı entegrasyondan sonra önerilen etiket:
  wp-006-m2-armor

Etiket adı mevcut konvansiyonla çelişirse C-00 geçerli konvansiyonu uygular.

KRİTİK DURMA KOŞULLARI

Yalnız şu durumlarda kullanıcı onayı için dur:

- Onaylı gereksinimler arasında çözülemeyen gerçek çelişki
- Yeni ürün kararı gerektiren davranış
- Unity veya URP sürümü değişikliği
- Veri kaybı veya kullanıcı dosyası riski
- Destructive Git işlemi gereksinimi
- WP-006 kapsamının dışına çıkmayı zorunlu kılan mimari engel
- Geri döndürülemez değişiklik
- Ücretli servis veya lisans gereksinimi

Bunların dışında teknik ayrıntıları mevcut sözleşmeler içinde çöz, test et ve paket tamamlanana kadar çalışmayı sürdür. Ara rapor veya ilk çalışan sürüm teslim sayılmaz.

KABUL

Paket ancak şu koşulların tamamı sağlandığında tamamlanmıştır:

- Üç bölge ve üç armor slotu doğru çalışıyor.
- 0/1/2/3 armor kombinasyonları destekleniyor.
- Direct hit routing sözleşmesi eksiksiz.
- Overflow hasarı cana sızmıyor.
- Explosion dağıtımı toplam hasarı çoğaltmıyor.
- Tam zırhlı düşman, bütün parçalar sağlamken patlamadan can hasarı almıyor.
- Kısmi zırh açık bölgelerin önemini koruyor.
- Pooling ve ikinci koşu state sızıntısı üretmiyor.
- Death, kill, gold ve round akışlarında tekrar oluşmuyor.
- Hit/armor-break event sözleşmeleri S-06/S-07/S-08 tarafından kullanılabilir.
- Mevcut M1 davranışı ve testleri bozulmuyor.
- Tam otomatik test, build, smoke ve manuel probe kanıtı bulunuyor.
- S-01 koşulsuz PASS veriyor.
- S-10 bağımsız PASS veriyor.
- Git ve manifest denetimleri temiz.
- Main entegrasyon sonrası temiz ve manifestle eşleşiyor.
- Yeni ürün kararı eklenmiyor.

NİHAİ RAPOR

C-00’a birleşik nihai rapor gönder. Rapor en az şunları içersin:

- Başlangıç ve bitiş HEAD
- Branch/worktree bilgisi
- Katılan gerçek KADRO ajanları ve görevleri
- Prompt uzmanlarının hazırladığı görev promptları ve inceleme sonucu
- Değişen production dosyaları
- Eklenen testler
- REQ/FLOW → kod → test izlenebilirlik matrisi
- Direct hit test matrisi
- Explosion test matrisi
- Pooling/lifecycle kanıtı
- Manual Player probe sonucu
- EditMode ve PlayMode toplamları
- Build ve smoke sonucu
- Performans/allocation değerlendirmesi
- S-01 kararı
- S-10 kararı
- Manifest ve hash
- Commit ve etiket
- Main fast-forward entegrasyon kaydı
- Repo temizliği
- Kapsam dışında bırakılan konular
- WP-007 için yalnız teknik bağımlılık notları; yeni kapsam veya ürün kararı önerme

Şimdi C-00 bağlantısını ve tabanı doğrula, KADRO prompt uzmanlarına alt görev promptlarını hazırlat ve WP-006’yı başlat.
