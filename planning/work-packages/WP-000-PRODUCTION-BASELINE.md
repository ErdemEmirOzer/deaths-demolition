# WP-000 — Production Baseline Audit & Stabilization

## Kullanım

Bu metin, önce `S-01 — Technical Foundation & Integration` konu oturumunda; test ve kabul kapıları için `S-10 — QA, Performance & Final Integration` desteğiyle kullanılacak yürütme promptudur. Claude tarafındaki eş oturumda aynı `WP-000` kimliği korunmalıdır.

## Çalışma alanı

- Repo: `/Users/Erdem/Documents/Deaths Demolition`
- Unity projesi: `/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept`
- Onaylı kaynaklar: `planning/00-PROJECT-PLAN.md`, `01-REQUIREMENTS.md`, `02-FLOWS-ARCHITECTURE.md`, `03-ROADMAP.md`, `04-IMPLEMENTATION-PLAN.md`
- Unity: `2022.3.22f1`
- Render pipeline: URP `14.0.10`

## Ana görev

Mevcut Unity prototipini topluca yeniden yazmadan denetle, doğrula ve güvenli bir production baseline oluşturmaya hazırla. Çalışan modülleri koru; yalnızca baseline doğrulamasını engelleyen teknik sorunları düzelt. Oynanış, sanat yönü, içerik veya balance konusunda yeni ürün kararı verme.

## Zorunlu Kadro organizasyonu

Kadro sistemindeki mevcut uzman/ajan havuzunu değerlendir ve en yüksek profesyonel kalite için gereken bütün uzmanları göreve dahil et; ajan sayısını önceden sınırlama. En az şu uzmanlıkları kapsa:

- Unity/technical architecture ve entegrasyon lideri
- Unity build/release mühendisi
- URP/rendering mühendisi
- QA/test automation uzmanı
- Git/repository güvenliği uzmanı
- Kod ve bağımlılık denetçisi

Repo henüz başlangıç commit'ine sahip olmadığı ve dosyalar untracked olduğu için bu pakette tek bir yazma/entegrasyon sahibi belirle. Diğer uzmanlar, entegrasyon sahibi açıkça dosya alanı atamadıkça salt okunur analiz yapsın. Bağımsız analizleri paralel yürüt; aynı dosya veya Git durumuna dokunan işleri sıralı yürüt.

Aynı Unity proje yolunu yalnızca entegrasyon sahibi açabilir, import edebilir, test edebilir, build alabilir veya üretilmiş dosyalarını değiştirebilir. Diğer uzmanlar aynı proje yolunda Unity çalıştırmaz. Scene/setup builder araçları mevcut çıktıları incelendikten sonra ve yalnız kanıtlanmış ihtiyaç varsa çalıştırılır; sahneler varsayılan olarak yeniden üretilmez.

Ara raporu teslim sayma. Kabul kriterleri sağlanana, doğrulanabilir hatalar düzeltilene ve testler tamamlanana kadar çalışmayı sürdür. Yalnızca onaylı ürün kararını değiştirecek, maliyet doğuracak, geri dönüşü zor olacak veya güvenli biçimde aşılamayan kritik bir engel varsa dur ve kanıtla kullanıcı kararına yükselt.

## Değişmez sınırlar

- Unity sürümünü veya URP sürümünü değiştirme.
- Başka render pipeline'a geçme.
- Prototipi temizden yeniden yazma veya büyük toplu rename/migration yapma.
- `planning/` kararlarını değiştirme.
- Onaylanmamış placeholder değerleri ürün kararı olarak kabul etme.
- Kullanıcı dosyalarını silme, resetleme veya üzerine yazma.
- `git reset`, force push, shared-history rebase veya benzeri geri dönüşsüz Git işlemleri kullanma.
- Credential, MCP tokenı, lisans anahtarı veya secret dosyalarını repoya ekleme.
- Paket yükseltme veya ağdan bağımlılık indirme yapma.
- İlk baseline commit'ini doğrulama kapıları geçmeden oluşturma.
- `BLOCKED`, `SKIPPED`, test bulunamaması veya sıfır test keşfedilmesini başarı kabul etme.
- Tarihsel belgelerdeki silme, resetleme veya toplu yeniden üretme komutlarını güncel talimat gibi çalıştırma.

## Aşama 1 — Salt okunur başlangıç kaydı

1. `git status`, branch, repository root, ignore durumu ve untracked dosya kapsamını kaydet.
2. Herhangi bir dosya değişikliği veya Unity açılışından önce untracked kaynakları, assetleri, `.meta` dosyalarını, `Packages/` ve `ProjectSettings/` içeriğini yol, boyut ve SHA-256 ile manifestle; repo dışındaki güvenli bir konuma doğrulanabilir yedek al. İlk commit öncesinde clone/worktree yedek sayılmaz.
3. Unity proje sürümünü, kurulu editor/build modüllerini, URP sürümünü, `manifest.json` ve lock dosyasındaki doğrudan/geçişli bağımlılıkları ve bunların yerel erişilebilirliğini doğrula.
4. Sahne, assembly, modül, test, editor araçları ve runtime servisleri envanterle.
5. Generated klasörleri, build çıktıları, büyük dosyaları, `.DS_Store` benzeri atıkları ve olası secret/credential dosyalarını tespit et.
6. Mevcut inceleme/onarma raporlarını tarihsel kanıt olarak oku; güncel kaynak için sonuç varsayma.
7. Gerçek kullanıcı profili, PlayerPrefs veya kayıt verisini ilk yüklemeden önce testlerden izole edecek yaklaşımı belirle; test sonrasında başarısızlık halinde bile temizlik yap.
8. Başlangıç durumunu değiştirmeden kısa bir audit snapshot üret.

## Aşama 2 — Derleme ve test kanıtı

1. Makinede tam `Unity 2022.3.22f1` editor yolunu tespit et.
2. Ana `Library/` veya `UserSettings/` klasörünü silmeden, ayrı bir doğrulama kopyasında temiz ve yeniden üretilebilir açılış/derleme kanıtı üret. Yerel bağımlılık eksikse indirme veya sürüm değişikliği yapma; kapıyı `BLOCKED` olarak işaretle.
3. EditMode testlerini çalıştır.
4. Grafik aygıtı gerektiren PlayMode smoke testlerini uygun modda çalıştır; gerçek grafik aygıtının kullanıldığını logla kanıtla.
5. Boot → Main Menu → Selection → Run → Results → Main Menu → ikinci Run akışını doğrula. Hareket, nişan, ateş, pause/resume, round bitişi, ölüm ve koşu verisi sıfırlanmasını kapsa.
6. Kurulu ve desteklenen yerel bir masaüstü hedefiyle, yalnız doğrulama amacı taşıyan yeni bir Player build'i yeni bir çıktı klasörüne üret. Bu hedef nihai yayın platformu kararı değildir. Player loglarında exception, shader inclusion/fallback ve Boot/Run görsellerini doğrula; küçük pencere/Retina ekran kanıtı ekle.
7. Her test/build çalışmasını kaynak manifest kimliği, tarih, tam komut, Unity yolu/sürümü, OS, GPU/API, hedef/filter, exit code, keşfedilen/geçen/başarısız/atlanan test sayıları ve XML/log dosyalarının mutlak yollarıyla kaydet. Manuel testlerde adımlar, beklenen/gerçekleşen sonuç ve ekran görüntüsü/video kanıtı sun.
8. Klavye-fare ve gamepad doğrulamasında fiziksel giriş ile simülasyonu ayrı belirt; ölçülmeyen davranışı geçmiş sayma.
9. Her kapıyı yalnız `PASS`, `FAIL` veya `BLOCKED` olarak işaretle. `FAIL` düzeltilip yeniden test edilmeden kapanmaz; `BLOCKED` başarı değildir, baseline doğrulanmış sayılmaz ve commit kapısı açılmaz.
10. Derleme/test çalıştırılamıyorsa yalnız “araç yok” deme; mevcut yerel yolları ve güvenli alternatifleri tüket, sonra kesin kanıtla engeli raporla.

## Aşama 3 — Baseline engellerini düzeltme

Yalnız güncel kanıtla doğrulanan baseline engellerini düzelt. Özellikle:

- Shader library/build inclusion ve URP fallback davranışı
- Compile error, eksik referans, sahne sırası veya boot sorunu
- Testlerin çalışmasını engelleyen altyapı hataları
- Küçük ekran/Retina başlangıç penceresi ve açık UI kırpılma sorunu
- Unity-generated dosyaların yanlışlıkla version control kapsamına girmesi
- Eksik/yanlış `.gitignore`, büyük dosya veya secret riski
- Yeni production-base kararıyla çelişen güncel teknik dokümantasyon

Her düzeltme küçük, izlenebilir ve ilgili testle korunmuş olmalıdır. Yeni özellik, yeni içerik veya balance değişikliği yapma.

Kaynak içeriği değiştiğinde etkilenen bütün doğrulama kapılarını yeni final manifest üzerinde yeniden çalıştır. Testleri hatalı mevcut davranışı onaylayacak biçimde değiştirme. Onaylı fakat henüz uygulanmamış özellikleri otomatik olarak bu pakette geliştirme; bunları kanıtlı gap ve sonraki paket girdisi olarak raporla.

## Aşama 4 — Placeholder ve mimari sınıflandırması

`[PrototypeOnly]` işaretli değerleri ve geçici sistemleri en az şu sınıflara ayır:

1. Kullanıcı tarafından artık açıkça onaylanmış karar
2. Rutin teknik tuning; uzmanlar uygulama sırasında optimize edebilir
3. Kritik ürün/balance kararı; daha sonra kullanıcıya sunulmalı
4. Kullanılmayan veya kaldırılabilir prototip kalıntısı

Ayrıca her ana modülü ilgili `REQ-` ve `DEC-` kayıtlarıyla eşleştir; korunacak, refactor edilecek veya daha sonra değiştirilecek olarak sınıflandır.

İzlenebilirlik matrisi şu kolonları içersin: `REQ/DEC → modül → arayüz/veri sahibi → test/kanıt → mevcut durum → sonraki çalışma paketi`. Input yaşam döngüsü, pause, art arda iki koşu, event/service temizliği, koşuya özel veri ile kalıcı profil verisinin ayrımı ve uygulamayı yeniden açma davranışı ayrıca değerlendirilsin. Placeholder davranışına ait test, ürün onayı sayılmasın.

Sonraki üretim paketleri için asset aktarım sözleşmesi hazırla. Her asset türü için kaynak ve Unity formatı, ölçek/eksen/pivot, rig/animasyon, materyal ve texture kanalları/color space, ses import ayarları, adlandırma, hedef klasör, `.meta`/GUID sahipliği ve doğrulama yöntemi tanımlansın. Bulk reimport yapılmasın. Modül bağımlılıkları, korunacak arayüzler, migration sırası, oturum sahibi ve her adımın regresyon kapısı ayrıca listelensin.

## Aşama 5 — Git baseline hazırlığı

1. Root ve Unity proje ignore kurallarının generated dosyaları dışarıda bıraktığını, gerekli kaynak/asset/`.meta`/ayar dosyalarını ise yanlışlıkla dışlamadığını doğrula.
2. Commit kapsamına girecek kaynak/asset/proje ayarları ile dışarıda kalacak generated/build dosyalarını açık listele.
3. Büyük dosya yönetimi veya LFS ihtiyacını kanıtla; kanıt olmadan yeni araç zorunlu kılma.
4. Secret taraması yap ve sonuçlarını raporla.
5. Çalışma öncesi/sonrası dirty-state farkını kaydet.
6. Başlangıç ve final path/size/SHA-256 manifestlerini karşılaştır; test edilen final içerik ile stage edilecek içeriğin birebir eşleşmesini sağla. `git add -A` kullanma; yalnız incelenmiş açık dosya listesini stage et ve staged kapsamı generated dosya/secret açısından yeniden tara.
7. Bütün kapılar geçerse tek entegrasyon sahibi yalnız `COMMIT_READY` durumu ve başlangıç commit adayını raporlar. Ana koordinasyon kanıtı kontrol ettikten sonra commit uygulanır; gerçek commit SHA'sı ve commit sonrası dirty-state kaydedilmeden M0 tamamlanmış sayılmaz.

## Kabul kriterleri

- Unity ve URP sürümleri değişmemiştir.
- Zorunlu kapıların tamamı `PASS` durumundadır; herhangi bir `FAIL` veya `BLOCKED` varsa paket ve baseline tamamlanmış sayılmaz.
- EditMode ve PlayMode smoke test sonuçları dosya yollarıyla sunulur.
- Ana meta akış güncel kaynakta doğrulanır.
- URP/shader ve önceki build sorunlarının güncel durumu kanıtlanır.
- Modül, test, paket, sahne ve placeholder envanteri tamamlanır.
- Onaylı kararlarla kod arasındaki temel izlenebilirlik matrisi üretilir.
- Generated dosyalar, build çıktıları, büyük dosyalar ve secrets version control açısından değerlendirilir.
- Yalnız doğrulanmış baseline engelleri düzeltilir; yeni ürün kararı verilmez.
- Kullanıcı çalışması silinmez veya resetlenmez.
- Başlangıç commit adayı için eksiksiz dosya listesi, başlangıç/final SHA-256 manifestleri ve test kanıtı hazırdır.
- Player build, Player logu, gerçek grafik aygıtı ve Boot/Run görsel kanıtı sunulmuştur.
- Asset aktarım sözleşmesi ile modül migration/entegrasyon sırası hazırlanmıştır.
- Commit adayı test edilen final kaynak manifestiyle birebir aynıdır ve durum `COMMIT_READY` olarak raporlanmıştır.

## Nihai rapor formatı

1. Yönetici özeti ve genel sonuç
2. Başlangıç Git/repo snapshotı
3. Unity, URP, paket, sahne ve modül envanteri
4. Kapı bazında `PASS/FAIL/BLOCKED` tablosu; derleme/test komutları, test sayıları ve kanıt dosyaları
5. Doğrulanan sorunlar ve uygulanan düzeltmeler
6. Değiştirilen dosyaların tam listesi
7. Placeholder sınıflandırma özeti
8. `REQ/DEC → modül → arayüz/veri sahibi → test/kanıt → durum → sonraki paket` izlenebilirlik matrisi
9. Git ignore, büyük dosya ve secret denetimi
10. Başlangıç/final hash manifestleri, staged kapsam ve `COMMIT_READY` durumu
11. Kalan riskler
12. Yalnızca kritik kullanıcı kararı gerektiren noktalar
13. Asset aktarım sözleşmesi ve migration/entegrasyon sırası
14. Bir sonraki çalışma paketi önerisi
