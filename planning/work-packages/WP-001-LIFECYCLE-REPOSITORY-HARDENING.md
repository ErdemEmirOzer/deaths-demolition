# WP-001 — Lifecycle & Repository Hardening

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-10'da `S-01 — Technical Foundation & Integration` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-001-lifecycle-repository-hardening` branch'inde (taban: `wp-000-baseline`) yapılır; `main`'e merge ve push ayrı kullanıcı kararıdır.

---

PAKET KİMLİĞİ: WP-001
PAKET ADI: Lifecycle & Repository Hardening
AŞAMA: M0 kapanışı → M1 hazırlığı
BİRİNCİL OTURUM: S-01 — Technical Foundation & Integration
BAĞIMSIZ DOĞRULAMA: S-10 — QA, Performance & Final Integration
KOORDİNASYON: C-00 — Deaths Demolition Main Coordination

ÇALIŞMA ALANI

Repo:
/Users/Erdem/Documents/Deaths Demolition

Unity projesi:
/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept

WP-000 kanıt alanı:
/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-000

Yetkili planlama kaynakları:

- planning/00-PROJECT-PLAN.md
- planning/01-REQUIREMENTS.md
- planning/02-FLOWS-ARCHITECTURE.md
- planning/03-ROADMAP.md
- planning/04-IMPLEMENTATION-PLAN.md

DOĞRULANMIŞ BAŞLANGIÇ NOKTASI

- Branch: main
- Baseline commit: 4b01b630b9cadde94b646b7242192ab3d60a8e1e
- Etiket: wp-000-baseline
- Manifest: aa67d79278afac5eaaa7027e49b847863d0e79893a960a2165d14d6c7ddd02c1
- HEAD içeriği: 640 dosya
- HEAD ↔ manifest eşleşmesi: 640/640
- Başlangıç çalışma ağacı: temiz
- Unity: 2022.3.22f1
- URP: 14.0.10
- WP-000 kapıları: PASS 11, FAIL 0, BLOCKED 0
- EditMode: 175/175
- PlayMode: 21/21, gerçek Metal GPU
- Player build: PASS
- Player smoke: PASS

wp-000-baseline etiketini ve baseline commit'ini değiştirme, taşıma veya yeniden yazma.

ANA AMAÇ

M1 dikey kesit çalışmalarına geçmeden önce repository entegrasyon güvenliğini ve onaylanmış profil/uygulama yaşam döngüsünü güçlendir.

Bu pakette yeni oynanış, içerik, denge, sanat veya ürün kararı verme.

Yalnız şu sonuçları üret:

1. WP-000 kapanış durumunu planlama belgelerine işle.
2. Unity Smart Merge sürücüsünü güvenli repo-local Git yapılandırmasıyla doğrula.
3. Normal tamamlanan koşu sonrasında profil kalıcılığını gerçek uygulama yeniden açılışı sınırında doğrula.
4. Uygulama yeniden açıldığında koşu verisinin sıfırlandığını doğrula.
5. Uygulama yeniden açıldığında DEC-039 gereği varsayılan skin ve klasik silah seçimine dönüldüğünü doğrula.
6. Ardışık koşu, pause ve statik servis/event temizliğinde regresyon olmadığını doğrula.
7. Sonraki paketler için temiz ve izlenebilir bir M1 başlangıç noktası hazırla.

ZORUNLU KADRO ORGANİZASYONU

Kadro sistemindeki mevcut ajan ve uzman havuzunu değerlendir.

En yüksek profesyonel kalite için gereken bütün uzmanları göreve dahil et. Ajan sayısını önceden sınırlama.

En az şu uzmanlıkları kapsa:

- git-repository-safety
- unity-specialist
- csharp-developer
- game-test-engineer
- engineering-manager
- code/dependency audit uzmanlığı
- technical-writer

Gerekirse profil yaşam döngüsü için S-04 alanından ve seçim/uygulama açılışı davranışı için S-09 alanından salt okunur danışmanlık al.

S-01 tek yazma, entegrasyon ve package-commit sahibi olacaktır.

S-10 bağımsız kabul doğrulaması yapacaktır.

Diğer Kadro ajanları, S-01 açıkça çakışmayan dosya alanı vermedikçe salt okunur çalışacaktır.

Aynı Unity proje yolunu aynı anda yalnız tek ajan açabilir, import edebilir, test edebilir veya build alabilir.

Ara raporu teslim sayma. Doğrulanabilir hataları düzeltip testleri yeniden çalıştırarak kabul kriterleri sağlanana kadar çalışmaya devam et.

Yalnız onaylanmış ürün kararını değiştirecek, maliyet doğuracak, geri dönüşü zor olacak veya güvenli biçimde aşılamayan gerçek kritik engeli kullanıcıya yükselt.

GIT VE WORKTREE KURALI

1. Çalışmaya başlamadan önce main branch'in tam olarak baseline commit'te ve temiz olduğunu doğrula.
2. WP-001 için baseline commit'ten izole bir branch ve worktree oluştur.
3. Branch/worktree adını ve mutlak yolunu raporla.
4. Ana çalışma ağacında hiçbir dosya değiştirme.
5. wp-000-baseline etiketine dokunma.
6. Force push, reset, rebase, amend veya shared-history değişikliği yapma.
7. Remote ekleme veya push yapma.
8. Her yazma öncesi ve sonrası dirty-state kaydet.
9. Yalnız S-01 stage ve commit yapabilir.
10. git add -A kullanma; doğrulanmış açık dosya listesini stage et.
11. S-10 doğrulaması geçmeden package commit oluşturma.
12. Testler geçerse izole package branch üzerinde tek ve atomik WP-001 commit'i oluşturulabilir.
13. Package branch'i main'e merge etme.
14. Commit SHA'sını ve ana main çalışma ağacının hâlâ baseline'da temiz olduğunu nihai rapora ekle.
15. Nihai entegrasyon kararı Codex/Pusula rapor kontrolünden sonra verilecektir.

AŞAMA 1 — BASELINE HANDOFF DOĞRULAMASI

1. HEAD, branch, tag, status ve worktree listesini kaydet.
2. wp-000-baseline etiketinin doğru commit'i gösterdiğini doğrula.
3. WP-000 manifest v2 ile HEAD yol ve SHA-256 eşleşmesini yeniden kontrol et.
4. Unity ve URP sürümlerinin değişmediğini doğrula.
5. WP-000 kanıt alanındaki nihai S-01 ve S-10 raporlarını oku.
6. Önceki test çıktılarını yalnız başlangıç kanıtı kabul et; WP-001 değişiklikleri sonrasında ilgili bütün kapıları yeniden çalıştır.
7. Ana worktree'nin temiz başlangıç durumunu kaydet.
8. İzole WP-001 worktree'sini oluşturduktan sonra yalnız orada çalış.

AŞAMA 2 — PLAN DURUMUNU GÜNCELLE

Planlama belgelerine yalnız doğrulanmış gerçekleri işle:

1. planning/03-ROADMAP.md içinde M0 durumunu tamamlandı olarak güncelle.
2. M0 kanıtı olarak şunları kaydet:
   - commit 4b01b630b9cadde94b646b7242192ab3d60a8e1e
   - etiket wp-000-baseline
   - PASS 11 / FAIL 0 / BLOCKED 0
3. M1 hazırlığının WP-001 ile başladığını belirt.
4. planning/04-IMPLEMENTATION-PLAN.md içinde WP-000 durumunu tamamlandı olarak güncelle.
5. WP-000 yürütme promptunu veya geçmiş kararlarını değiştirme.
6. WP-001'in kapsamını ve durumunu implementasyon planına kısa biçimde ekle.
7. Bu exact çalışma paketini planning/work-packages/WP-001-LIFECYCLE-REPOSITORY-HARDENING.md olarak kaydet.
8. Yeni DEC veya REQ üretme.
9. 04-READY-CHECK.md veya başka bir "hazır kontrolü" dosyası oluşturma.

AŞAMA 3 — UNITY SMART MERGE / GIT HARDENING

.gitattributes içinde merge=unityyamlmerge ataması mevcut fakat yerel sürücü henüz tanımlı değildir.

1. Kurulu Unity 2022.3.22f1 içindeki gerçek UnityYAMLMerge executable yolunu tespit et.
2. Binary'nin çalıştırılabilir olduğunu doğrula.
3. Repo-local Git config içinde merge.unityyamlmerge sürücüsünü güvenli biçimde tanımla.
4. Global Git config'i değiştirme.
5. Yapılandırma öncesi ve sonrası değerleri kaydet.
6. Secret, kullanıcı adı veya makine dışına taşınmaması gereken veri yazma.
7. Geçici ve zararsız Unity YAML örnekleriyle sürücünün çağrılabildiğini doğrula.
8. Test için gerçek production sahnesinde çakışma oluşturma.
9. Repo-local config'in başka makinede otomatik bulunmayacağını dokümante et.
10. Gerekli yeniden kurulum komutunu secret içermeden nihai rapora ekle.
11. Başarısız test veya yanlış config oluşursa önceki repo-local değere geri dön.
12. Bu işlem için remote veya push kullanma.

AŞAMA 4 — PROFİL VE UYGULAMA YAŞAM DÖNGÜSÜ

Yalnız onaylanmış şu davranışları uygula ve doğrula:

- Koşu sırasında toplanan gold, silah geliştirmeleri ve build koşu bitiminde sıfırlanır.
- Kalıcı profil seviyesi, istatistikler, açılan içerikler, başarımlar ve en yüksek round korunur.
- Normal ölüm ve Run Results akışından sonra profil kaydedilir.
- Oyunun yeniden açılması kalıcı profili silmez.
- Oyun yeniden açıldığında DEC-039 gereği varsayılan skin ve klasik silah seçimi kullanılır.
- Oyuncunun aynı anda yalnızca bir aktif koşusu bulunur.
- İkinci koşu temiz koşu durumuyla başlar.

Aşağıdaki kanıtları üret:

1. İzole bir test profili veya repo dışı yedek/geri yükleme düzeni kur.
2. Gerçek kullanıcı profilini ve PlayerPrefs verisini testlerden koru.
3. Test öncesi kullanıcı verisi SHA-256 snapshot'ı al.
4. Normal bir koşuyu Results aşamasına kadar tamamla.
5. Kalıcı profile eklenmesi gereken istatistiklerin kaydedildiğini doğrula.
6. Player sürecini normal biçimde kapat.
7. Yeni Player süreci başlat.
8. Kalıcı profil verisinin korunduğunu doğrula.
9. Önceki koşunun gold, build, upgrade ve geçici durumunun yüklenmediğini doğrula.
10. Skin ve klasik silah seçiminin uygulama açılışında varsayılanlara döndüğünü doğrula.
11. Aynı uygulama oturumundaki son seçim hatırlama davranışının DEC-037 ile bozulmadığını doğrula.
12. İkinci koşunun Round 1, sıfır gold, sıfır koşu geliştirmesi ve tam başlangıç canıyla başladığını doğrula.
13. Testten sonra gerçek kullanıcı verisini başlangıç hash'leriyle karşılaştır.
14. Kullanıcı verisinde test kaynaklı değişiklik oluşursa güvenli yedekten geri yükle ve tekrar doğrula.
15. Failure durumunda bile cleanup çalıştır.

Test altyapısı gerçek uygulama yeniden açılışını otomatik doğrulayamıyorsa güvenli DEVELOPMENT_BUILD veya test-only probe kullanılabilir.

Bu probe:

- Release davranışını değiştirmemeli.
- F1 debug paneli veya DirectToRun davranışı hakkında ürün kararı vermemeli.
- UNITY_EDITOR, DEVELOPMENT_BUILD veya UNITY_INCLUDE_TESTS gibi uygun teknik kapılarla sınırlandırılmalı.
- Release Player'da yeni debug erişimi oluşturmamalı.
- Secret veya gerçek profil yolu içermemeli.

AŞAMA 5 — EVENT, STATIC STATE VE ARDIŞIK KOŞU HARDENING

1. RunEvents, RunFlowClock, InputSourceRegistry ve mutable static servisleri yeniden incele.
2. Birinci koşu → Results → Main Menu → ikinci koşu zincirinde event subscriber birikmesi olmadığını kanıtla.
3. Pause/resume sonrasında RunFlowClock hold durumunun temizlendiğini doğrula.
4. Scene değişimlerinde statik olayların iki kez tetiklenmediğini doğrula.
5. İkinci koşuda önceki düşman, projectile, gold orb, upgrade, timer veya input state kalmadığını doğrula.
6. OnApplicationQuit ve normal Results kayıt yollarının iki kez profil uygulamadığını doğrula.
7. Kanıtlanmış bir lifecycle hatası bulunursa en küçük güvenli düzeltmeyi yap.
8. Her üretim kodu düzeltmesine önce başarısız test kanıtı ve sonra geçen regresyon testi eşlik etsin.
9. Kanıtlanmamış varsayıma göre geniş refactor yapma.
10. Sınıf-4 placeholder temizliğini topluca yapma.
11. Paket, dependency, assembly veya render pipeline değiştirme.

AŞAMA 6 — TAM REGRESYON

WP-001 final kaynak manifesti üzerinde:

1. Temiz ve ayrı doğrulama kopyası oluştur.
2. Ana Library veya UserSettings klasörünü silme.
3. Unity import ve compile çalıştır.
4. EditMode testlerinin tamamını çalıştır.
5. PlayMode testlerinin tamamını gerçek Metal GPU ile çalıştır.
6. Yeni lifecycle testlerinin keşfedildiğini ve geçtiğini sayılarla göster.
7. macOS doğrulama Player build'i oluştur.
8. Main Menu ve Run Player smoke testlerini çalıştır.
9. Normal Results → kapanış → yeni Player açılışı lifecycle kanıtını çalıştır.
10. Player loglarında exception, missing reference ve shader fallback ara.
11. Pause sırasında ateş edilememe regresyonunu yeniden doğrula.
12. İki ardışık koşu sıfırlama testini yeniden doğrula.
13. Kullanıcı profili izolasyonunu yeniden doğrula.
14. Git secret, generated dosya ve .meta/GUID bütünlüğü taramasını yeniden çalıştır.
15. Her kapıyı yalnız PASS, FAIL veya BLOCKED olarak işaretle.
16. FAIL düzeltilip yeniden test edilmeden paket tamamlanmaz.
17. BLOCKED başarı değildir.

TEST KANITI STANDARDI

Her otomatik veya manuel koşuda şunları kaydet:

- Kaynak commit veya manifest kimliği.
- Tarih ve saat.
- Tam komut.
- Unity executable yolu ve sürümü.
- OS.
- GPU ve grafik API.
- Test filter veya build target.
- Exit code.
- Keşfedilen test sayısı.
- Geçen test sayısı.
- Başarısız test sayısı.
- Atlanan test sayısı.
- XML ve log dosyalarının mutlak yolları.
- Manuel işlem varsa adım, beklenen sonuç, gerçekleşen sonuç ve ekran/video kanıtı.

KAPSAM DIŞI — DOKUNMA

Aşağıdaki konular bu pakette özellikle ertelenmiştir:

- InitTestScene639227508893790040.unity ve .meta dosyasının fiziksel silinmesi.
- DirectToRun PlayerPrefs kalıcılığının değiştirilmesi.
- F1 debug panelinin Player build davranışının değiştirilmesi.
- Abort edilen koşunun profile işlenip işlenmemesi.
- Sınıf-3 denge ve ürün karar paketleri A–H.
- Can, hasar, zırh, round, XP, gold ve Special Weapon sayıları.
- DEC-045, DEC-048, DEC-049, DEC-050, DEC-043 ve DEC-021 kod boşluklarının uygulanması.
- DEC-047 "kullanılabilir kart" tanımının yorumlanması.
- PostProcessData veya görsel pipeline değişikliği.
- Asset sözleşmesindeki 14 karar adayının karara bağlanması.
- Humanoid/Generic rig seçimi.
- TMP/font/paket değişiklikleri.
- Toplu _Prototype → _Game migration.
- Sınıf-4 placeholder'ların toplu temizliği.
- Paket kaldırma veya yükseltme.
- Remote ekleme.
- Push.
- Main branch'e merge.
- Gamepad doğrulaması.
- M4'e ertelenmiş pencere modu ve fiziksel testler.

Bu maddeler için mevcut davranışı ürün onayı sayan test ekleme.

KRİTİK DURMA KOŞULLARI

Yalnız şu durumlarda dur ve C-00 üzerinden kullanıcı kararına yükselt:

- Baseline commit veya tag beklenen değerle eşleşmiyor.
- Main çalışma ağacı başlangıçta temiz değil ve değişikliğin sahibi belirlenemiyor.
- Kullanıcı verisi güvenli biçimde yedeklenemiyor veya geri yüklenemiyor.
- Onaylanmış profil/selection davranışı başka bir onaylı kararla çelişiyor.
- Çözüm Unity veya URP sürüm değişikliği gerektiriyor.
- Çözüm ücretli araç, hesap, lisans veya dış servis gerektiriyor.
- Veri kaybı veya geri dönüşsüz Git işlemi gerekiyor.
- Test edilebilir güvenli çözüm olmadan ürün davranışına karar verilmesi gerekiyor.

KABUL KRİTERLERİ

- Çalışma baseline commit'ten oluşturulan izole branch/worktree'de yapılmıştır.
- Main branch hâlâ 4b01b630b9cadde94b646b7242192ab3d60a8e1e üzerindedir.
- wp-000-baseline etiketi değişmemiştir.
- Ana çalışma ağacı temizdir.
- Planlama belgelerinde M0 tamamlandı bilgisi doğru kanıtla güncellenmiştir.
- WP-001 promptu planning/work-packages altında kaydedilmiştir.
- UnityYAMLMerge repo-local config doğrulanmıştır.
- Kalıcı profil gerçek Player yeniden açılışında korunmuştur.
- Koşu gold'u, build ve geçici veriler yeni açılışta sıfırlanmıştır.
- Uygulama yeniden açılışında varsayılan skin ve silah seçimi uygulanmıştır.
- Aynı oturum içindeki son seçim hatırlama davranışı bozulmamıştır.
- İkinci koşu temiz başlamıştır.
- Event veya statik state birikmesi yoktur.
- Gerçek kullanıcı verisi korunmuş veya başlangıç hash'ine geri yüklenmiştir.
- Unity ve URP sürümleri değişmemiştir.
- Package ve ProjectSettings değişikliği yapılmamıştır.
- EditMode testlerinin tamamı geçmiştir.
- PlayMode testlerinin tamamı gerçek GPU ile geçmiştir.
- Player build başarılıdır.
- Player smoke ve lifecycle testleri geçmiştir.
- Player loglarında yeni exception veya shader fallback yoktur.
- Secret ve generated dosya bulunmamıştır.
- S-10 bağımsız sonucu PASS'tir.
- İzole package branch üzerinde tek atomik WP-001 commit'i oluşturulmuştur.
- Package branch main'e merge edilmemiştir.
- Remote eklenmemiş ve push yapılmamıştır.

NİHAİ RAPOR FORMATI

1. Yönetici özeti.
2. Başlangıç baseline doğrulaması.
3. Branch/worktree sahipliği.
4. Kadro ajanları ve görev dağılımı.
5. Planlama durum güncellemeleri.
6. UnityYAMLMerge yapılandırması ve kanıtı.
7. Profil/uygulama yeniden açılış senaryosu.
8. Koşu verisi sıfırlama kanıtı.
9. Skin ve silah seçim yaşam döngüsü kanıtı.
10. Event/static state denetimi.
11. Doğrulanan hatalar ve uygulanan düzeltmeler.
12. Değiştirilen dosyaların tam listesi.
13. Başlangıç ve final manifestleri.
14. Test/build komutları ve sonuçları.
15. PASS/FAIL/BLOCKED kapı tablosu.
16. Kullanıcı verisi hash/yedek/geri yükleme sonucu.
17. Secret/generated/.meta/GUID denetimi.
18. S-10 bağımsız kabul kararı.
19. Package commit SHA'sı.
20. Main branch ve wp-000-baseline değişmezlik kanıtı.
21. Ertelenen kararlar; hiçbirine karar verme.
22. Kalan riskler.
23. Sonraki çalışma paketi önerisi.

ÇALIŞMA SONU

WP-001 tamamlandığında nihai raporu C-00 ana koordinasyon oturumuna gönder.

C-00 raporu kullanıcıya ve Codex/Pusula koordinasyonuna aktaracaktır.

Main branch'e merge veya push yapma.

Kritik blocker yoksa kullanıcıdan ara onay bekleme. Hataları düzelt, testleri yeniden çalıştır ve bütün kabul kriterleri sağlanana kadar kesintisiz devam et.
