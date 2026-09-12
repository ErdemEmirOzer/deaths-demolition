# WP-002 — M1 Core Gameplay & Combat Production Slice

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-11'de `S-02 — Core Gameplay & Combat` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-002-m1-core-combat` branch'inde (taban: `main` = `e3d3a98` = `wp-001-lifecycle`) yapılır; `main`'e merge ve push ayrı kullanıcı kararıdır.

---

PAKET KİMLİĞİ: WP-002
PAKET ADI: M1 Core Gameplay & Combat Production Slice
AŞAMA: M1 — Oynanabilir Dikey Kesit / Bölüm 1
BİRİNCİL OTURUM: S-02 — Core Gameplay & Combat
TEKNİK ENTEGRASYON: S-01 — Technical Foundation & Integration
BAĞIMSIZ DOĞRULAMA: S-10 — QA, Performance & Final Integration
KOORDİNASYON: C-00 — Deaths Demolition Main Coordination

AMAÇ

Deaths Demolition’ın production-standard M1 dikey kesitindeki temel savaş çekirdeğini oluştur ve doğrula.

Bu paket yalnız şu alanları kapsar:

- Oyuncu hareketi.
- Fare ve gamepad nişan sözleşmesi.
- Kamera davranışı.
- Bir geçici klasik silahla temel ateş döngüsü.
- Standard zombi ile temel savaş etkileşimi.
- Oyuncunun hasar alması, ölmesi ve otomatik can yenilenmesi.
- Oyuncu/zombi fiziksel engellemesi.
- Zombiler arası yumuşak ayrışma.
- Temel hit reaction ve game-feel olay altyapısı.

Bu paket tüm M1’i tamamlamaz. Round/spawn, gold/upgrade ve meta UI paketleri daha sonra ayrı oturumlarda yürütülecektir.

DOĞRULANMIŞ BAŞLANGIÇ NOKTASI

Repo:
/Users/Erdem/Documents/Deaths Demolition

Unity projesi:
/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept

main HEAD:
e3d3a98e66230c2fd48abc9269a8e82d6bf30f73

WP-001 etiketi:
wp-001-lifecycle

WP-000 etiketi:
wp-000-baseline

WP-001 final manifest:
72b3a0aaf8daf5d8b24628b894c3f1e17b3f2c0070d47063e7055f278c665eff

Başlangıç durumu:

- 658 tracked dosya.
- Çalışma ağacı temiz.
- Unity 2022.3.22f1.
- URP 14.0.10.
- EditMode 175/175.
- PlayMode 28/28, gerçek Metal GPU.
- WP-001 S-10 sonucu PASS 13 / FAIL 0 / BLOCKED 0.
- Remote yok.
- Push yapılmadı.
- WP-001 worktree’si KADRO alanında korunuyor; silme.

YETKİLİ PLANLAMA KAYNAKLARI

- planning/00-PROJECT-PLAN.md
- planning/01-REQUIREMENTS.md
- planning/02-FLOWS-ARCHITECTURE.md
- planning/03-ROADMAP.md
- planning/04-IMPLEMENTATION-PLAN.md
- planning/work-packages/WP-000-PRODUCTION-BASELINE.md
- planning/work-packages/WP-001-LIFECYCLE-REPOSITORY-HARDENING.md

Planlama belgeleri ürün kararları için tek yetkili kaynaktır.

KADRO ORGANİZASYONU

Kadro sistemindeki mevcut uzman ve ajan havuzunu değerlendir. En yüksek profesyonel kalite için gereken tüm uzmanları göreve dahil et; ajan sayısını önceden sınırlama.

En az şu uzmanlıkları kapsa:

- game-developer
- unity-specialist
- csharp-developer
- game-designer
- game-test-engineer
- git-repository-safety
- animation systems uzmanı
- VFX/game-feel uzmanı
- audio integration uzmanı
- performance uzmanı

Nihai animasyon, VFX veya ses asset’i üretme. İlgili uzmanlar yalnız sistem arayüzü, olay sözleşmesi, placeholder geri bildirim ve ilerideki üretim ihtiyaçlarının doğruluğu için çalışsın.

S-02 package execution ve tek yazma sahibidir.

S-01 mimari ve entegrasyon sınırlarını salt okunur doğrular.

S-10 kabul kapılarını bağımsız doğrular.

Diğer ajanlar, S-02 açıkça çakışmayan bir alan vermedikçe repo üzerinde yazma yapmaz.

Aynı Unity proje yolunu aynı anda yalnız tek ajan açabilir, import edebilir, test edebilir veya build alabilir.

Ara raporu teslim sayma. Kabul kriterleri tamamlanana, doğrulanmış hatalar düzeltilene ve testler yeniden geçene kadar çalışmayı sürdür.

GIT VE WORKTREE KURALI

1. main branch’in e3d3a98e66230c2fd48abc9269a8e82d6bf30f73 üzerinde ve temiz olduğunu doğrula.
2. WP-002 için bu commit’ten izole branch ve worktree oluştur.
3. Branch/worktree kimliğini C-00 ve S-01’e bildir.
4. Ana çalışma ağacında değişiklik yapma.
5. WP-000 ve WP-001 etiketlerine dokunma.
6. WP-001 worktree’sini silme veya değiştirme.
7. Force push, reset, rebase, amend veya geçmiş değiştiren işlem kullanma.
8. Remote ekleme veya push yapma.
9. Yalnız S-02 stage ve package commit yapabilir.
10. git add -A kullanma.
11. Değiştirilen açık dosya listesini inceleyerek stage et.
12. S-10 PASS vermeden package commit oluşturma.
13. Testler tamamlanırsa izole WP-002 branch’inde tek atomik commit oluştur.
14. main’e merge etme.
15. Commit SHA’sını, manifesti ve main değişmezlik kanıtını raporla.

PLAN DURUMU

Paket başlangıcında:

1. planning/03-ROADMAP.md içinde WP-001’in tamamlandığını doğrulanmış bilgilerle belirt.
2. planning/04-IMPLEMENTATION-PLAN.md içinde WP-001 durumunu tamamlandı olarak güncelle:
   - Commit e3d3a98e66230c2fd48abc9269a8e82d6bf30f73
   - Etiket wp-001-lifecycle
   - S-10 PASS 13 / FAIL 0 / BLOCKED 0
3. WP-002’yi M1 Core Gameplay & Combat paketi olarak kaydet.
4. Bu promptu planning/work-packages/WP-002-M1-CORE-COMBAT.md altında aynen sakla.
5. Yeni DEC veya REQ oluşturma.
6. Ürün kararlarını değiştirme.
7. 04-READY-CHECK.md veya başka bir “hazır kontrolü” dosyası oluşturma.

ONAYLANMIŞ OYNANIŞ SÖZLEŞMESİ

Aşağıdaki kararları uygula ve testle doğrula:

HAREKET VE NİŞAN

- WASD hareketi kamera yönüne göre çalışır.
- Karakter fare imlecinin dünyadaki yönüne döner.
- Oyuncu hareket yönünden farklı bir yöne nişan alabilir.
- Oyuncu bir yöne hareket ederken başka bir yöne ateş edebilir.
- Sol tık basılı tutulduğunda otomatik silah ateş etmeyi sürdürür.
- Gamepad sözleşmesinde sol analog hareketi, sağ analog nişanı yönetir.
- Oyuncuda dash veya dodge bulunmaz.

KAMERA

- Kamera uzak ve yükseltilmiş üçüncü şahıs görünümündedir.
- Kamera açısı ve yönü sabittir.
- Oyuncu kamerayı elle döndürmez.
- Kamera oyuncuyu yumuşak biçimde takip eder.
- Oyuncu normalde ekran merkezine yakın kalır.
- Fare yönüne doğru sınırlı kadraj kayması uygulanır.
- Gamepad’de kadraj kaymasını sağ analog yönü belirler.
- Savaş sırasında zoom mesafesi büyük ölçüde sabit kalır.
- Kamera ile oyuncu arasına duvar veya büyük nesne girdiğinde nesne saydamlaştırılır.
- Kamera engel nedeniyle aniden oyuncuya yaklaşmaz.

KLASİK SİLAH

- Oyuncu koşunun başında bir klasik silah seçer.
- Klasik silah bütün koşu boyunca oyuncuda kalır.
- Special Weapon aktif olmadığında klasik silah kullanılabilir durumdadır.
- Bu pakette yalnız mevcut prototip klasik silahı test aracı olarak kullanılabilir.
- Mevcut Assault Rifle adı veya değerleri nihai silah roster’ı, varsayılan silah ya da balance kararı sayılmaz.
- Silahın mevcut sayısal hasar, ateş hızı, şarjör ve reload değerleri PrototypeOnly kalır.
- Bu değerleri planning kararına dönüştürme veya testlerle final sayı olarak kilitleme.

OYUNCU HASARI VE HAYATTA KALMA

- Zombiye yalnız temas etmek oyuncuya hasar vermez.
- Hasar yalnız zombinin saldırı animasyonuna bağlı saldırı hacmi oyuncuya isabet ederse oluşur.
- Oyuncu saldırıyı görüp hareketle kaçabilir.
- Oyuncu hasar aldıktan sonra koruma veya dokunulmazlık süresi kazanmaz.
- Aynı anda isabet eden birden fazla saldırının hasarı üst üste uygulanabilir.
- Hasar alındığında hareket, nişan ve ateş kontrolü kesilmez.
- Hasar görsel, işitsel ve kamera geri bildirimiyle bildirilir.
- Oyuncu belirli süre hasar almazsa can yenilenmeye başlar.
- Can yenilenmesinin sayısal bekleme süresi, hızı ve sınırı bu pakette final karar değildir.
- Oyuncunun tek canı vardır.
- Yeniden canlanma bulunmaz.
- Can sıfıra ulaştığında koşu sona erer.

FİZİKSEL ETKİLEŞİM

- Oyuncu zombilerin içinden geçemez.
- Zombiler oyuncunun yolunu kapatabilir.
- Çevrelenmek gerçek tehdittir.
- Zombiler aynı noktada üst üste binmez.
- Zombiler birbirlerini hafifçe iterek sürü halinde akabilir.
- Yumuşak ayrışma kalabalığı tamamen dağıtmamalı veya geçitleri yapay biçimde kilitlememelidir.

STANDARD ZOMBİ

- Bu paketin düşman kapsamı yalnız Standard zombidir.
- Special, Elite ve Ultra sürümler bu pakette uygulanmaz veya yeniden tasarlanmaz.
- Bölgesel zırh ve patlama sistemi bu paketin kapsamında değildir.
- Standard zombi fiziksel, büyüsüz ve okunabilir bir zombi davranışı kullanır.
- Temel hareket, hedef takibi, saldırı hazırlığı, saldırı isabet penceresi, hit reaction ve ölüm zinciri doğrulanır.
- Nihai model, skin, animasyon veya ses asset’i üretilmez.

GAME FEEL VE HIT REACTION

- Vuruş hissi prototipten itibaren temel kalite hedefidir.
- Flesh hit, ölüm ve oyuncu hasarı olayları ayrı ve okunabilir sinyaller üretmelidir.
- Hit reaction; yön, vuruş bölgesi, silah gücü ve düşmanın mevcut hareket bağlamını taşıyabilecek genişletilebilir bir sözleşmeye sahip olmalıdır.
- Hafif darbeler zombiyi sürekli durdurmamalıdır.
- Sürekli stun-lock engellenmelidir.
- Otomatik silahın her mermisi global hit-stop üretmemelidir.
- Kamera tepkisi nişanı kullanılmaz hale getirmemelidir.
- Nihai VFX, animasyon, ses ve sayısal şiddet ayarları sonraki üretim paketlerine bırakılır.
- Bu pakette yalnız anlaşılır placeholder feedback ve doğru event/interface altyapısı üret.

AŞAMA 1 — MEVCUT DURUM VE İZLENEBİLİRLİK

1. Hareket, input, kamera, silah, oyuncu sağlığı, düşman saldırısı, collision, separation, hit reaction ve ölüm modüllerini envanterle.
2. İlgili DEC ve REQ maddelerini kod, veri sahibi ve testlerle eşleştir.
3. Her davranışı şu statülerden biriyle sınıflandır:
   - Uygulanmış ve testli.
   - Uygulanmış fakat testsiz.
   - Kısmen uygulanmış.
   - Onaylı kararla çelişiyor.
   - PrototypeOnly.
   - Bu paketin kapsamı dışında.
4. Mevcut sayısal değerleri ürün kararı sayma.
5. Önce mevcut davranışı değiştirmeden başlangıç test ve Player kanıtını üret.
6. Yalnız kanıtlanmış uyumsuzlukları düzelt.

AŞAMA 2 — INPUT VE HAREKET

1. Input yaşam döngüsünü ve action map kullanımını doğrula.
2. WASD kamera-relative hareketini test et.
3. Çapraz hareketin istemeden daha hızlı olmadığını doğrula.
4. Fare dünya konumu hesabının zemin, kamera açısı ve ekran kenarlarında kararlı olduğunu doğrula.
5. Hareket ve nişan yönlerinin birbirinden bağımsız olduğunu kanıtla.
6. Hareket ederken ters veya yan yöne ateş edilebildiğini kanıtla.
7. Sol tık hold davranışının tek basış değil sürekli ateş ürettiğini doğrula.
8. Pause, Results veya simülasyon donma durumunda input’un ateş üretmediğini doğrula.
9. Kontroller scene değişiminden sonra iki kez bağlanmamalıdır.
10. Gamepad fiziksel cihaz testi M4’e ertelenmiştir; yalnız sol/right stick sözleşmesini simüle testlerle koru.
11. Sprint, jump, crouch veya ADS hakkında yeni karar verme.
12. Mevcut davranış varsa PrototypeOnly olarak sınıflandır; yeni özellik ekleme.

AŞAMA 3 — KAMERA

1. Sabit açı ve yön sözleşmesini doğrula.
2. Kameranın kullanıcı tarafından döndürülemediğini doğrula.
3. Smooth follow davranışının frame-rate bağımsız olduğunu doğrula.
4. Oyuncunun temel merkez konumunu ve sınırlı aim offset davranışını test et.
5. Fare ve gamepad aim offset yollarının aynı sınır sözleşmesini kullandığını doğrula.
6. Savaş sırasında istenmeyen zoom değişimi olmadığını doğrula.
7. Occlusion fader’ın oyuncu ile kamera arasındaki doğru nesneleri saydamlaştırdığını doğrula.
8. Birden fazla engelde state’in doğru temizlendiğini doğrula.
9. Engel kalkınca materyal ve saydamlık durumunun geri döndüğünü doğrula.
10. Kamera engel nedeniyle oyuncuya ani yaklaşmamalıdır.
11. Kamera sarsıntısının aim offset veya takip sisteminde kalıcı drift üretmediğini doğrula.
12. Nihai kamera sayıları PrototypeOnly kalır.

AŞAMA 4 — KLASİK SİLAH VE ATEŞ DÖNGÜSÜ

1. Mevcut klasik silahın input → fire eligibility → shot → hit → feedback zincirini çıkar.
2. Sol tık hold ateşini doğrula.
3. Hareket ve ateşin birbirini gereksiz yere kilitlemediğini doğrula.
4. Pause ve frozen simulation kapısını yeniden test et.
5. Merminin aynı hedefe istemeden birden fazla hit üretmediğini doğrula.
6. Pooling kullanılan projectile veya feedback nesnelerinin koşular arasında sızmadığını doğrula.
7. Ateş event’inin VFX, ses, kamera ve hit reaction tüketicileri için temiz bir sözleşme sunduğunu doğrula.
8. Mevcut silah sayısal değerlerini değiştirmek yalnız test çalıştırmak için zorunluysa yapılabilir; final karar olarak kaydedilmez.
9. Yeni klasik silah ekleme.
10. Silah roster’ı, varsayılan silah, ammo ekonomisi, reload tasarımı, ADS ve upgrade dalları hakkında karar verme.
11. Special Weapon sistemini değiştirme.

AŞAMA 5 — STANDARD ZOMBİ SAVAŞ DÖNGÜSÜ

1. Standard zombinin hedef bulma ve oyuncuya yaklaşma zincirini doğrula.
2. Yalnız temasın oyuncuya hasar vermediğini test et.
3. Saldırı animasyonu veya saldırı zaman çizelgesindeki aktif hit penceresini açık biçimde tanımla.
4. Hit penceresi dışında hasar oluşmadığını test et.
5. Hit penceresi içinde gerçek menzil/hacim kesişmesi yoksa hasar oluşmadığını test et.
6. Bir saldırının aynı oyuncuya istemeden birden fazla kez uygulanmadığını doğrula.
7. Farklı zombilerin eşzamanlı saldırılarının üst üste hasar verebildiğini test et.
8. Oyuncu hasar aldığında hareket, nişan ve ateşin kesilmediğini doğrula.
9. Zombi flesh hit, hit reaction, ölüm ve despawn zincirini doğrula.
10. Hafif tekrar vuruşlarında sonsuz stun-lock oluşmadığını doğrula.
11. Ölümden sonra zombi saldırısı veya collision etkisi kalmadığını doğrula.
12. Nihai animasyon ve sayı değerlerini kilitleme.

AŞAMA 6 — SAĞLIK, REGEN VE ÖLÜM

1. Oyuncunun tek health state kaynağını doğrula.
2. Hasar sonrasında health değerinin doğru güncellendiğini doğrula.
3. Koruma süresi olmadığını test et.
4. Eşzamanlı farklı saldırıların hasarlarının üst üste uygulandığını test et.
5. Regen’in hasar alınca durduğunu veya gecikmesinin yeniden başladığını doğrula.
6. Hasarsız süre sonrasında regen başladığını doğrula.
7. Regen’in maksimum canı aşmadığını test et.
8. Regen sayıları PrototypeOnly kalmalıdır.
9. Can sıfıra ulaştığında yalnız bir kez death/end-run olayı üretildiğini doğrula.
10. Yeniden canlanma yolu bulunmadığını doğrula.
11. Oyuncu ölümünden sonra hareket, ateş, düşman hasarı ve regen’in doğru şekilde durduğunu doğrula.
12. Results/meta akışını değiştirme; yalnız mevcut death handoff sözleşmesini koru.

AŞAMA 7 — COLLISION VE YUMUŞAK AYRIŞMA

1. Oyuncunun canlı zombilerin içinden geçemediğini doğrula.
2. Zombilerin oyuncunun yolunu fiziksel olarak kapatabildiğini doğrula.
3. Ölü veya despawn edilmiş zombilerin oyuncuyu engellemediğini doğrula.
4. Zombilerin aynı merkez noktasında üst üste yığılmasını engelle.
5. Separation davranışının sürü görünümünü bozmadığını doğrula.
6. Birden fazla zombinin dar alanda kalıcı fizik patlaması veya sonsuz jitter üretmediğini doğrula.
7. NavMesh ve collision hareketinin birbirleriyle savaşmadığını doğrula.
8. Separation ve collision tuning değerlerini PrototypeOnly tut.
9. Harita, spawn yoğunluğu veya aktif düşman sayısı formüllerini değiştirme.

AŞAMA 8 — TEST VE PLAYTEST

Final kaynak manifesti üzerinde:

1. Temiz doğrulama kopyası oluştur.
2. Ana Library ve UserSettings klasörlerini silme.
3. Unity import/compile çalıştır.
4. EditMode testlerinin tamamını çalıştır.
5. PlayMode testlerinin tamamını gerçek Metal GPU ile çalıştır.
6. Yeni core combat testlerinin keşfedildiğini ve geçtiğini kanıtla.
7. Release Player build üret.
8. Main Menu → Selection → Run akışını Player üzerinde aç.
9. Klavye-fare ile şu manuel combat smoke testini yürüt:
   - WASD hareketi.
   - Fareyle bağımsız nişan.
   - Hareket ederken farklı yöne ateş.
   - Sol tık hold auto-fire.
   - Kamera takip ve aim offset.
   - Kamera occlusion.
   - Standard zombi saldırısından hareketle kaçma.
   - Temasın tek başına hasar vermemesi.
   - Gerçek saldırı isabetinde hasar.
   - Kontrol kesintisi olmaması.
   - Regen.
   - Çevrelenme ve collision.
   - Zombi hit reaction ve ölüm.
   - Oyuncu ölümü ve tek end-run olayı.
10. Player loglarında exception, MissingReference, shader fallback ve tekrarlı event hataları ara.
11. Frame time, GC allocation, aktif collider/agent ve pool sayılarını kısa bir gözlemle raporla.
12. Henüz onaylı performans hedefi olmadığı için ölçümleri ürün kabul sayısına dönüştürme.
13. Gerçek kullanıcı profilini testlerden izole et.
14. Test sonrası kullanıcı verisi hash’lerini doğrula.
15. Her kapıyı PASS, FAIL veya BLOCKED olarak raporla.
16. FAIL düzeltilip yeniden test edilmeden paket kapanmaz.
17. BLOCKED başarı değildir.

KAPSAM DIŞI

Bu pakette aşağıdakilere dokunma:

- Round formülü ve round havuzu productionization.
- Dinamik spawn yoğunluğu.
- Gold düşürme, magnet ve round sonu toplama.
- Round sonu upgrade kartları.
- Reroll.
- Profil XP ve açılma ekonomisi.
- Run Results içeriğinin yeniden tasarımı.
- B3 InitTestScene fiziksel silme.
- B4 DirectToRun ve F1 debug davranışı.
- B5 abort edilen koşunun profile işlenmesi.
- Sınıf-3 karar paketleri A–H.
- Sayısal can, hasar, zırh, regen veya silah dengesi.
- Nihai klasik silah roster’ı.
- Special Weapon.
- Special, Elite ve Ultra zombiler.
- Bölgesel zırh.
- Patlama hasarı.
- Nihai modeller, skinler veya animasyonlar.
- Nihai VFX ve ses asset’leri.
- PostProcessData ve render pipeline değişikliği.
- _Prototype → _Game toplu migration.
- Package veya Unity/URP sürüm değişikliği.
- Remote, push veya main merge.
- WP-001 worktree temizliği.
- Fiziksel gamepad kabul testi.

KRİTİK DURMA KOŞULLARI

Yalnız şu durumlarda dur:

- main başlangıçta beklenen commit’te veya temiz değilse.
- Onaylı kararlar birbiriyle çelişiyorsa.
- Çözüm yeni ürün davranışı seçmeyi gerektiriyorsa.
- Çözüm Unity/URP/package değişikliği gerektiriyorsa.
- Kullanıcı verisi güvenli biçimde korunamıyorsa.
- Geri dönüşsüz Git işlemi gerekiyorsa.
- Ücretli araç, hesap veya lisans gerekiyorsa.
- Mevcut sistemi koruyan test edilebilir çözüm bulunamıyorsa.

KABUL KRİTERLERİ

- WP-002 izole branch/worktree’de yürütülmüştür.
- main ve önceki etiketler değişmemiştir.
- WP-001 planlama durumu doğru biçimde tamamlandı olarak güncellenmiştir.
- WP-002 promptu planlama klasörüne kaydedilmiştir.
- Hareket kamera-relative çalışmaktadır.
- Hareket ve nişan birbirinden bağımsızdır.
- Hareket ederken farklı yöne ateş edilebilmektedir.
- Hold-to-fire çalışmaktadır.
- Pause/frozen simulation ateşi engellemektedir.
- Kamera sabit yön, smooth follow, sınırlı aim offset ve occlusion fade sözleşmesini karşılamaktadır.
- Yalnız temas oyuncuya hasar vermemektedir.
- Standard zombi yalnız saldırı hit penceresiyle hasar vermektedir.
- Eşzamanlı farklı saldırıların hasarı üst üste uygulanmaktadır.
- Hasar oyuncunun hareket, nişan ve ateş kontrolünü kesmemektedir.
- Hasarsız süreden sonra regen başlamaktadır.
- Oyuncu canı sıfırda tek kez ölmektedir; revive yoktur.
- Oyuncu canlı zombilerin içinden geçememektedir.
- Zombiler yumuşak ayrışma kullanmaktadır.
- Hit reaction ve game-feel event sözleşmeleri genişletilebilir ve testlidir.
- Hiçbir PrototypeOnly sayı ürün kararı olarak kilitlenmemiştir.
- Unity ve URP değişmemiştir.
- Packages ve ProjectSettings değişmemiştir veya zorunlu teknik değişiklik kanıt ve onay olmadan yapılmamıştır.
- EditMode testlerinin tamamı geçmiştir.
- PlayMode testlerinin tamamı gerçek GPU ile geçmiştir.
- Release Player build başarılıdır.
- Klavye-fare combat smoke testi geçmiştir.
- Player loglarında yeni exception veya shader fallback yoktur.
- Gerçek kullanıcı profili korunmuştur.
- Secret, generated dosya ve GUID problemi yoktur.
- S-01 entegrasyon incelemesi PASS’tir.
- S-10 bağımsız kabul sonucu PASS’tir.
- İzole WP-002 branch’inde tek atomik commit oluşturulmuştur.
- main’e merge veya push yapılmamıştır.

NİHAİ RAPOR

Şu sırayla raporla:

1. Yönetici özeti.
2. Başlangıç Git ve baseline doğrulaması.
3. Branch/worktree ve dosya sahipliği.
4. Kadro ajanları ve görev dağılımı.
5. DEC/REQ → modül → test izlenebilirlik matrisi.
6. Başlangıç davranış durumu.
7. Doğrulanan uyumsuzluklar.
8. Uygulanan düzeltmeler.
9. Hareket ve input kanıtı.
10. Kamera kanıtı.
11. Klasik silah ve ateş kanıtı.
12. Standard zombi saldırı ve hasar kanıtı.
13. Sağlık, regen ve ölüm kanıtı.
14. Collision ve separation kanıtı.
15. Hit reaction ve game-feel sözleşmesi.
16. Değiştirilen dosyaların tam listesi.
17. Başlangıç/final manifestleri.
18. Test/build komutları ve sonuçları.
19. Manuel Player smoke sonucu.
20. Performans gözlemleri.
21. Kullanıcı verisi koruma sonucu.
22. Secret/generated/.meta/GUID denetimi.
23. S-01 entegrasyon kararı.
24. S-10 bağımsız kabul kararı.
25. Package commit SHA’sı.
26. main ve önceki etiketlerin değişmezlik kanıtı.
27. Ertelenen kararlar; hiçbirine karar verme.
28. WP-003 için yalnız teknik bağımlılık notları.

ÇALIŞMA SONU

WP-002 tamamlandığında nihai raporu C-00’a gönder.

C-00 raporu Codex/Pusula koordinasyonuna aktaracaktır.

main’e merge veya push yapma.

Kritik blocker yoksa kullanıcıdan ara onay bekleme. Hataları düzelt, testleri yeniden çalıştır ve bütün kabul kriterleri sağlanana kadar kesintisiz devam et.
