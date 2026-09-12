# WP-005 — M1 Meta UI, Results & Profile Presentation

## Kullanım

Bu metin, kullanıcı tarafından 2026-09-12'de `S-09 — UI, UX & Accessibility` oturumuna verilen çalışma paketi promptunun aynen kaydıdır. Yürütme izole `wp-005-m1-meta-ui-results-profile` branch'inde (taban: `main` = `a284ffe2` = `wp-004-m1-gold`) yapılır; `main`'e merge ve push ayrı C-00/kullanıcı kararıdır.

---

# WP-005 — M1 Meta UI, Results & Profile Presentation

## Görev sahibi

Ana konu oturumu:
S-09 — UI, UX & Accessibility

Ana koordinasyon:
C-00

Teknik entegrasyon danışmanı:
S-01

Savaş/HUD danışmanı:
S-02

Round yaşam döngüsü danışmanı:
S-03

Ekonomi/profil veri danışmanı:
S-04

Bağımsız kabul ve kalite doğrulaması:
S-10

Kaynak proje:
/Users/Erdem/Documents/Deaths Demolition

Başlangıç commit’i:
`a284ffe2b3e9780b02d80e6812d3dd925e6cf656`

Başlangıç etiketi:
`wp-004-m1-gold`

Bu paket WP-000, WP-001, WP-002, WP-003 ve WP-004 üzerine kurulacaktır.

## Ana hedef

M1 dikey kesitinin oyuncuya görünen ana menü, seçim, HUD, pause, round sonu geliştirme, ölüm, sonuç ve kalıcı profil sunumunu tek ve güvenilir bir kullanıcı akışına dönüştür.

Paket tamamlandığında şu akış baştan sona çalışmalıdır:

Main Menu
→ Skin & Weapon Selection
→ Run
→ HUD
→ Round Cleared
→ Upgrade Screen
→ Next Round
→ Player Death
→ Run Results
→ Main Menu

Main Menu üzerinden ayrıca şu bağımsız alanlara erişilebilmelidir:

- Profile
- Statistics
- Achievements

Bu paket yalnız ekranların görünmesi değildir. UI tarafından gösterilen bütün bilgiler authoritative gameplay, run, economy ve profile snapshot’larından gelmelidir.

UI hiçbir oynanış değerinin ikinci sahibi olmamalıdır.

## Ürün kalitesi ilkesi

Hızlı ve sade menü akışı, düşük kaliteli veya geçici görünen bir UI anlamına gelmez.

M1 kapsamında:

- Düzenli
- Okunaklı
- Tutarlı
- Profesyonel
- Akıcı
- Güvenilir
- İngilizce
- Klavye/fare ile eksiksiz kullanılabilir
- Gamepad entegrasyonuna uygun
- Erişilebilirlik temeli bulunan

bir UI sistemi oluştur.

Ancak final sanat yönü, final ikon seti, final font lisansı, final UI animasyonları ve Adobe/Higgsfield üretimleri bu pakette yapılmayacaktır.

Final görsel üretim M3 kapsamındadır.

WP-005’in görevi, final sanatın daha sonra üzerine uygulanabileceği sağlam ve kaliteli UI mimarisini oluşturmaktır.

## Kesintisiz çalışma kuralı

Görev başladıktan sonra bütün kabul kriterleri sağlanıncaya kadar çalışmaya devam et.

Normal teknik ayrıntılar için kullanıcıdan onay isteme.

Test başarısızlığında:

1. Kök nedeni belirle.
2. En küçük güvenli düzeltmeyi uygula.
3. Testi yeniden çalıştır.
4. İlgili regresyon paketini tekrar çalıştır.
5. Sonuç temizleninceye kadar devam et.

Yalnız gerçek bir kritik ürün kararı, destructive işlem, veri kaybı, ücretli servis veya güvenli biçimde aşılamayan teknik engel varsa C-00 üzerinden kullanıcıya dön.

## Kadro kullanımı

İhtiyaç duyulan uzman sayısını görevin gerçek kapsamına göre belirle.

En az şu uzmanlıkları değerlendir:

- Unity UI architect
- UI/UX designer
- Interaction designer
- UI state-management engineer
- Results/profile presentation engineer
- Save/profile integration engineer
- Accessibility specialist
- Localization-readiness specialist
- UI performance specialist
- PlayMode/UI test specialist
- Git/repository safety specialist
- S-01 integration reviewer
- S-10 independent QA reviewer

Ana UI üretim dosyaları için tek writer/entegrasyon sahibi belirle.

S-02, S-03 ve S-04 danışmanları açık bir devir yapılmadıkça salt okunur çalışmalıdır.

## Değiştirilemeyecek onaylanmış akış

Ana oyun akışı:

Main Menu
→ Skin & Weapon Selection
→ Run
→ Run Results
→ Main Menu

Bu akış gereksiz ara ekranlarla uzatılmayacaktır.

Profile, Statistics ve Achievements ana koşu akışını kesmeyen ayrı menü alanlarıdır.

Run Results ekranından ana dönüş hedefi Main Menu’dür.

Kullanıcıdan zorunlu Claim işlemi istenmeyecektir.

## Ana menü

Main Menu M1 kapsamında en az şu işlevlere erişim sağlamalıdır:

- Play
- Profile
- Statistics
- Achievements
- Mevcut çalışan Settings/Quit işlevleri varsa bunları koruma

Onaylanmamış yeni menü alanları ekleme.

Ana menü:

- Oyunun temel giriş noktası olmalı.
- Önceki run UI state’ini göstermemeli.
- Önceki Results snapshot’ını istemeden tekrar açmamalı.
- Uygulama açılışında duplicate event listener oluşturmamalı.
- İkinci veya sonraki menü dönüşlerinde duplicate button callback üretmemeli.
- Kullanıcıya görünen bütün metinleri İngilizce göstermeli.

## Tek karakter ve skin seçimi

Şimdilik yalnız bir oynanabilir karakter bulunacaktır.

Karakter çeşitlendirmesi oyun tamamlandıktan sonra değerlendirilecektir.

Mevcut karakter için:

- Üç farklı skin bulunur.
- Üç skin de baştan açıktır.
- Skinlerin oynanışa hiçbir etkisi yoktur.
- Skin seçimi hasar, sağlık, hareket, gold, XP, silah veya upgrade değerini değiştirmez.
- Skin seçimi yalnız görseldir.

Yeni karakter sistemi oluşturma.

Skinlere onaylanmamış final ad, hikâye, rarity, fiyat veya unlock koşulu ekleme.

Mevcut prototip isimleri varsa M1 için korunabilir. Yoksa açıkça geçici teknik tanımlayıcı kullan.

## Silah seçimi

Oyuncu koşunun başında bir klasik silah seçer.

Aynı klasik silah koşu boyunca korunur ve round sonu geliştirmeleri bu silahı dönüştürür.

M1 kapsamında yalnız mevcut prototipte kullanılabilir olan klasik silahları göster.

Yeni silah tasarlama.

Silah rarity, fiyat, unlock seviyesi veya final stat sunumu oluşturma.

Silah seçimi ekranındaki değerler gerçek runtime tanımlarından gelmeli; UI içinde ayrı hasar, fire-rate veya magazine değeri hardcode edilmemelidir.

## Seçim ekranı davranışı

Skin & Weapon Selection ekranı her koşudan önce açılacaktır.

Aynı uygulama oturumu içinde:

- Son seçilen skin hatırlanır.
- Son seçilen klasik silah hatırlanır.
- Seçim ekranı yine açılır.
- Önceki seçimler hazır olarak görünür.
- Oyuncu doğrudan koşuyu başlatabilir veya seçimi değiştirebilir.

Uygulama tamamen kapatılıp yeniden açıldığında:

- Varsayılan skin seçimine dönülür.
- Varsayılan silah seçimine dönülür.
- Kalıcı profil verileri silinmez.
- İstatistikler silinmez.
- Başarımlar silinmez.
- Unlock verileri silinmez.
- En yüksek round silinmez.
- Profil seviyesi silinmez.

Session selection ile permanent profile aynı kayıt alanına dönüştürülmemelidir.

WP-001’de doğrulanan DEC-037 ve DEC-039 yaşam döngüsü davranışlarını koru.

## Run HUD

HUD yalnız oyuncunun o anda ihtiyaç duyduğu bilgileri göstermelidir.

Mevcut prototip HUD alanlarını incele ve güvenilir snapshot kaynaklarına bağla.

M1’de gösterilebilecek bilgiler:

- Current Round
- Remaining/active enemy bilgisi mevcut onaylanmış sunuma uygunsa
- Player Health
- Classic Weapon
- Ammo
- Reload state
- Run Gold
- Aktif build’in kısa özeti
- Special Weapon alanı mevcut prototipte bulunuyorsa pasif/boş güvenli durum
- Gerekli mevcut combat feedback alanları

Yeni ürün davranışı yaratacak HUD metriği ekleme.

HUD:

- Gameplay state’i polling ile yeniden hesaplamamalı.
- Typed event veya immutable snapshot kullanmalı.
- Oyuncu öldüğünde son geçerli state’i güvenilir biçimde gösterebilmeli.
- Yeni koşuda eski gold/build/round/weapon snapshot’ını göstermemeli.
- Pause ve upgrade ekranı sırasında hatalı input almamalı.
- Aynı event’e duplicate abone olmamalı.
- Sahne reload sonrasında duplicate HUD controller üretmemeli.

## WP-004 UI entegrasyon noktaları

Aşağıdaki authoritative kaynakları kullan:

- `GoldWallet.Snapshot`
- `RunDirector.LastRoundEnd`
- `RunBuildSnapshot`
- `GoldService.AutoCollected`
- `RunSummary` gold/build alanları

UI kendi gold, build veya round sonucunu hesaplamamalıdır.

Round sonu upgrade ekranında:

- `GoldService.AutoCollected` reconciliation tamamlanmış olmalı.
- Gösterilen bakiye `GoldWallet.Snapshot` ile eşleşmeli.
- Gösterilen build `RunBuildSnapshot` ile eşleşmeli.
- Ekran yalnız geçerli `LastRoundEnd` bağlamı için açılmalı.
- Eski round butonları yeni round transaction’ı başlatmamalı.
- Aynı round için iki ekran instance’ı açılmamalı.

## Upgrade ekranı iyileştirmesi

WP-004’te oluşturulan olay güdümlü UpgradeScreen davranışını koru.

Bu pakette:

- Görsel hiyerarşiyi düzenle.
- Güncel gold bakiyesini belirgin göster.
- Upgrade adı ve açıklamasını İngilizce göster.
- Maliyeti göster.
- Satın alınabilir/satın alınamaz durumunu anlaşılır göster.
- Rank bilgisini mevcut prototip destekliyorsa göster.
- Mevcut build bağlamını okunaklı sun.
- Satın alma geri bildirimini netleştir.
- Devam/sonraki round eylemini mevcut prototip sözleşmesine göre sun.
- Focus ve selection durumlarını belirginleştir.
- Eksik ikon için tutarlı fallback oluştur.

WP-004 devir notundaki upgrade ikon alanını teknik olarak kapat:

- Upgrade tanımı için isteğe bağlı ikon referansı desteklenebilir.
- Final ikon üretme.
- İkon yoksa profesyonel, tutarlı ve hafif fallback kullan.
- Missing icon UI’yı bozmamalı.
- İkon alanı gameplay tanımını UI framework’üne gereksiz biçimde kilitlememeli.

### Açık kalan kararlar

DEC-048:
Round başına tek satın alma davranışı uygulanmayacaktır.

DEC-050:
Ücretli reroll davranışı uygulanmayacaktır.

Bu iki karar için:

- Yeni buton ekleme.
- Gizli davranış ekleme.
- Placeholder işlem oluşturma.
- Economy koduna kural ekleme.
- Karar dosyasına onaylanmış gibi yazma.

Mevcut prototipte bunlara ait işlevsiz veya yanıltıcı UI kalıntısı varsa kaldırmak yerine güvenli biçimde görünmez/devre dışı tut ve raporla. Destructive temizlik yapma.

## Pause menüsü

Pause menüsü bütün oyun simülasyonunu dondurmalıdır.

Pause sırasında:

- Oyuncu hareket etmez.
- Oyuncu ateş etmez.
- Reload ve dry-fire ilerlemez.
- Düşmanlar hareket etmez.
- Düşmanlar saldırmaz.
- Spawn clock ilerlemez.
- Gold magnet hareketi ilerlemez.
- Round progression ilerlemez.

Pause UI:

- Açıldığında gameplay input’unu yakalamalı.
- Kapandığında focus ve kontrolü güvenli biçimde oyuna döndürmeli.
- Upgrade ekranıyla aynı anda kontrol çakışması yaratmamalı.
- Results ekranı üzerinde açılamamalı.
- İkinci koşuda duplicate pause handler oluşturmamalı.

WP-004 devir notundaki `GateForPause` her-kare arama sorununu kapat:

- Kare başına sahne veya obje araması yapılmamalı.
- Gerekli referans lifecycle sırasında cache edilmeli veya olay üzerinden sağlanmalı.
- UI açma/kapama için sahne taraması kullanılmamalı.
- Sonuç profil kanıtıyla doğrulanmalı.

Pause içeriğini onaylanmamış seçeneklerle genişletme.

## Oyuncu ölümü ve Results geçişi

Oyuncunun tek canı vardır ve yeniden canlanma yoktur.

Can sıfıra ulaştığında:

1. Run ölümü authoritative sistem tarafından doğrulanır.
2. Yeni combat/spawn/economy işlemleri durur.
3. Run sonuç snapshot’ı, run state sıfırlanmadan önce alınır.
4. Kalıcı profile uygulanacak geçerli istatistik ve XP değişiklikleri tam olarak bir kez hesaplanır.
5. Kalıcı profil güncellenir.
6. Run’a ait gold/build/upgrade state’i sıfırlanır.
7. Results ekranı immutable sonuç snapshot’ıyla açılır.
8. Results ekranından Main Menu’ye dönülür.

UI doğrudan profile değer yazmamalıdır.

UI yalnız application/use-case katmanının ürettiği sonucu sunmalıdır.

Results ekranının tekrar açılması aynı run’ın XP, kill veya round verisini ikinci kez profile eklememelidir.

## Run Results ekranı

Tek ve düzenli bir Results ekranı kullanılacaktır.

Aynı ekranda en az şu bilgiler sunulmalıdır:

- Reached Round
- Completed Rounds
- Total Kills
- Enemy type/variant başına kill
- Ayrıntılı XP breakdown
- Profil seviyesi ilerlemesi
- Açılan içerikler
- Açılan başarımlar
- Gerekli mevcut RunSummary alanları
- M1 için anlamlıysa run gold/build özeti

Ekran içerik yoğunluğunu iyi yönetmeli.

Bilgileri rastgele kartlara bölme. Güçlü bir görsel hiyerarşi oluştur:

- Run sonucu
- Combat özeti
- XP/profil ilerlemesi
- Unlock/achievement olayları
- İkincil ayrıntılar
- Main Menu eylemi

Results verileri canlı run state’inden okunmamalıdır. Immutable `RunSummary` veya eşdeğer snapshot’tan gelmelidir.

Run state Results açılmadan önce sıfırlansa bile ekrandaki bilgiler kaybolmamalıdır.

## Profil XP kuralları

Kesin kurallar:

- Yalnız tamamlanan roundlar round XP’si verir.
- Ölüm gerçekleşen tamamlanmamış round round XP’si vermez.
- Ölüm gerçekleşen yarım rounddaki geçerli kill’ler istatistiğe eklenebilir.
- Bu kill’ler kill XP’sine eklenebilir.
- Gold kazanmak XP vermez.
- Gold toplamak XP vermez.
- Gold harcamak XP vermez.
- Upgrade satın almak XP vermez.
- Oynama süresi XP vermez.
- XP breakdown Results ekranında ayrıntılı gösterilir.
- Profil seviyesi doğrudan hasar sağlamaz.
- Profil seviyesi doğrudan sağlık sağlamaz.
- Profil seviyesi başlangıç gold’u sağlamaz.
- Profil seviyesi oyuncunun deneyimini ve açılan seçenekleri temsil eder.

Kesin XP miktarları, XP eğrisi ve seviye eşikleri henüz nihai ürün kararı değildir.

Mevcut prototip değerlerini `PrototypeOnly` olarak koru.

Yeni denge değeri belirleme.

## Kalıcı profil

Oyun internet bağlantısı olmadan tamamen oynanabilmelidir.

Şu bilgiler yerel olarak kalıcı olmalıdır:

- Highest Round
- Rounds Completed
- Total Kills
- Enemy type/variant başına kills
- Achievements
- Unlocked Content
- Profile XP
- Profile Level
- Mevcut onaylanmış genel istatistikler

Şu bilgiler run sonunda kalıcı güç olarak tutulmamalıdır:

- Run Gold
- Run Build
- Weapon Upgrade Ranks
- Run-only effects
- Geçici upgrade teklifleri
- Aktif round state’i
- Aktif enemy state’i
- Aktif pickup state’i

Oyuncunun aynı anda yalnız bir aktif koşusu bulunur.

Çoklu Player instance “last writer wins” konusu M4’e ertelenmiştir. Bu pakette yeni lock veya çoklu instance ürün politikası oluşturma.

## Profile ekranı

Profile ekranı en az şu bilgileri sunabilmelidir:

- Profile Level
- Current XP
- Next Level Progress
- Highest Round
- Total Kills
- Rounds Completed
- Açılmış içeriklerin özeti
- Mevcut achievement özeti

Profil seviyesi bir güç artırımı gibi sunulmamalıdır.

UI’da:

- Damage bonus
- Health bonus
- Starting gold bonus
- Permanent combat boost

gibi onaylanmamış göstergeler bulunmamalıdır.

XP curve PrototypeOnly ise bunu debug etiketiyle oyuncuya göstermene gerek yoktur; ancak teknik kayıtlarda açıkça belirtilmelidir.

## Statistics ekranı

Statistics ekranı mevcut gerçek verilere dayanmalıdır.

Asgari alanlar:

- Total Runs mevcut profilde güvenilir biçimde tutuluyorsa
- Highest Round
- Rounds Completed
- Total Kills
- Standard kills
- Special kills için schema mevcutsa sıfır/boş durum
- Elite kills için schema mevcutsa sıfır/boş durum
- Ultra kills için schema mevcutsa sıfır/boş durum
- Enemy type başına kills
- Enemy variant başına kills

Henüz oyunda bulunmayan varyantları sanki tamamlanmış içerikmiş gibi öne çıkarma.

Schema geleceğe hazır olabilir fakat UI M1’de yalnız anlamlı ve gerçek verileri göstermelidir.

İstatistik değeri bilinmiyorsa `0` ile `unknown` kavramlarını karıştırma.

## Achievements ekranı

Achievements sistemi için yeni başarımlar icat etme.

Mevcut prototipte tanımlı ve doğrulanmış achievement verileri varsa bunları göster.

Tanımlı başarımlar yoksa:

- Hata verme.
- Sahte achievement oluşturma.
- Profesyonel empty state göster.
- Gelecekte veri geldiğinde çalışacak yapı kur.

Achievement açıldığında:

- Otomatik açılır.
- Claim düğmesi gerekmez.
- Results ekranında vurgulanabilir.
- Sonraki ekranlarda unlocked olarak görünür.
- Aynı achievement ikinci kez açılma ödülü üretmez.

## Unlock sunumu

Profil seviyesi ilerledikçe gelecekte:

- Yeni silahlar
- Yeni karakterler
- Yeni geliştirme dalları

açılabilir.

Ancak şimdilik:

- Tek karakter vardır.
- Üç skin baştan açıktır.
- Yeni silah listesi kararlaştırılmamıştır.
- Yeni karakterler oyun sonrasına ertelenmiştir.
- Geliştirme dallarının kesin içeriği belirlenmemiştir.
- Unlock seviyeleri belirlenmemiştir.

Yeni unlock içeriği veya seviye eşiği oluşturma.

Mevcut ve doğrulanmış bir unlock gerçekleşirse otomatik uygula ve Results ekranında göster.

`Claim` butonu ekleme.

WP-004’ten devreden çağıransız `UnlockProfileLevel` veya eşdeğer yapıyı sırf mevcut olduğu için rastgele bir akışa bağlama. Önce mevcut niyetini, çağrı zincirini ve veri sözleşmesini incele.

Onaylanmış unlock içeriği yoksa çağıransız kalmasını raporla; ürün davranışı icat etme.

## Abort davranışı

B5 — Abort → Profile davranışı henüz kararlaştırılmamıştır.

Bu pakette:

- Pause menüsünden run abort edildiğinde profile hangi istatistiklerin yazılacağına dair yeni karar alma.
- Mevcut davranışı sessizce değiştirme.
- Abort’u ölümle otomatik olarak aynı sayma.
- Abort’u başarıyla tamamlanmış run sayma.
- Abort için yeni XP kuralı oluşturma.

Mevcut lifecycle davranışını güvenli biçimde koru.

Abort UI’nın duplicate Results, XP veya save üretmediğini test et.

Açık ürün kararı gerektiriyorsa ertelenen konu olarak raporla.

## UI state mimarisi

Her ekranın açık/kapalı durumunu bağımsız boolean’larla kontrol ederek çelişkili kombinasyonlar oluşturma.

Açık ve test edilebilir bir UI/navigation state kullan.

Asgari bağlamlar:

- MainMenu
- Selection
- GameplayHUD
- Paused
- RoundUpgrade
- RunResults
- Profile
- Statistics
- Achievements

İsimleri mevcut mimariye göre düzenle.

Garantiler:

- Aynı anda birbiriyle çelişen modal durumlar açılmaz.
- Results ile Upgrade aynı anda açılmaz.
- Pause ile Results aynı anda açılmaz.
- Main Menu arkasında gameplay input çalışmaz.
- Selection ekranında run başlamadan gameplay input çalışmaz.
- UI geçişleri idempotenttir.
- Aynı event ikinci ekran instance’ı üretmez.
- Sahne reload duplicate presenter oluşturmaz.
- Yeni run eski ekran state’ini taşımaz.

Mevcut scene/lifecycle sistemini gereksiz yere yeniden yazma.

## Sunum katmanı

UI presenter/view-model katmanını gameplay domain nesnelerinden ayır.

Tercih edilen veri akışı:

Authoritative gameplay/profile state
→ Immutable snapshot
→ Presenter/ViewModel
→ View

View doğrudan:

- EnemySpawnDirector
- PlayerHealth
- Weapon runtime internals
- Gold packet listesi
- Save dosyası
- Scene search

üzerinden veri toplamamalıdır.

UI için ayrı mutable oyun gerçeği oluşturma.

## UI teknoloji sınırı

Mevcut Unity UI teknolojisini incele.

Projede çalışan sistem:

- uGUI
- UI Toolkit
- TextMeshPro
- Mevcut navigation framework

hangisiyse gereksiz migration yapma.

UI Toolkit ↔ uGUI migration’ı kritik mimari değişikliktir ve bu pakette yapılmamalıdır.

Yeni ağır UI framework’ü ekleme.

## Görsel sistem

Final art direction belirlemeden tutarlı bir M1 UI temel sistemi oluştur.

Tanımla:

- Typography hierarchy
- Spacing scale
- Panel hierarchy
- Button states
- Focus states
- Disabled states
- Selected states
- Positive/negative feedback
- Status colors
- Layout margins
- Screen-safe regions
- Modal dimming
- Transition durations
- Icon fallback
- Empty state
- Loading state
- Error state

Final renk paleti veya marka sistemi kararlaştırma.

Mevcut prototip yönünü temiz ve tutarlı hâle getir. M3’te yeniden skin edilebilir yap.

## Animasyon ve geçiş

UI geçişleri:

- Kısa
- Akıcı
- Kontrolü geciktirmeyen
- State ile senkron
- Timescale sıfırken çalışabilir
- Duplicate callback üretmeyen

yapıda olmalıdır.

Ölüm → Results geçişi:

- Gecikmiş lifecycle yarışına açık olmamalı.
- WP-001’de kapatılan delayed Results problemini yeniden oluşturmamalı.
- Unscaled-time kullanımı gerekiyorsa test edilmelidir.
- Yeni koşuya eski transition callback’i sızmamalıdır.

Aşırı final animasyon üretme. Hareketin final polish’i M3’tedir.

## Dil

Oyunun dili tamamen İngilizce olacaktır.

Bu pakette kullanıcıya görünen bütün metinleri denetle:

- Button labels
- Screen titles
- HUD labels
- Results labels
- Profile labels
- Statistics labels
- Achievement labels
- Empty states
- Error states
- Tooltips
- Upgrade labels
- Pause labels

Türkçe veya karışık dil bırakma.

Yeni localization sistemi zorunlu değildir; fakat metinleri ileride localization yapılmasını imkânsızlaştıracak biçimde gameplay koduna dağıtma.

## Erişilebilirlik temeli

Kesin erişilebilirlik seçeneklerinin tamamı daha sonra genişletilebilir; ancak M1’de şu temeli oluştur:

- Okunaklı metin boyutları
- Yeterli kontrast
- Belirgin selection/focus
- Rengin tek bilgi taşıyıcısı olmaması
- Button disabled durumunun anlaşılması
- Klavye ile tam ana akış
- Mouse ile tam ana akış
- Resolution değişiminde kırılmayan layout
- Uzun metinlerde taşma kontrolü
- Sonuç ve istatistik değerlerinde okunabilir hiyerarşi

Gamepad’in tam fiziksel kabulü M4’e ertelenmiştir. Ancak mevcut gamepad navigation altyapısını bozma ve UI bileşenlerini daha sonra bağlanamayacak şekilde tasarlama.

## PrototypeOnly politikası

Aşağıdaki alanlar nihai ürün kararı değildir:

- XP miktarları
- XP level curve
- Level thresholds
- Unlock levels
- Achievement listesi
- Achievement şartları
- Upgrade fiyatları
- Upgrade ikonları
- Upgrade rarity
- Sonuç ekranı animasyon süreleri
- Final UI renkleri
- Final font
- Final sesler
- Final transition süreleri
- Nihai çözünürlük hedefleri
- Nihai gamepad navigation şeması

Mevcut prototip değerleri kullanılabilir.

Yeni geçici değer gerekirse:

- `PrototypeOnly` olarak işaretle.
- Yeni DEC/REQ oluşturma.
- Nihai karar gibi belgeleme.
- Paket raporunda listele.
- PrototypeOnly sayısını açıklamasız düşürme.

## Paket kapsamı

Bu pakette yapılacaklar:

- Mevcut UI mimarisini inceleme
- Main Menu akışı
- Skin & Weapon Selection akışı
- Tek karakter/üç açık skin sunumu
- Session-only son seçim hafızası
- Relaunch sonrası varsayılan seçim
- HUD snapshot bağlantıları
- Gold HUD bağlantısı
- Build HUD bağlantısı
- Upgrade ekranı sunum iyileştirmesi
- Upgrade icon reference/fallback
- `GateForPause` her-kare arama düzeltmesi
- Pause menüsü lifecycle güvenliği
- Oyuncu ölümü → Results geçişi
- Immutable RunSummary sunumu
- Tek ve düzenli Results ekranı
- XP breakdown
- Profile level progress
- Kill istatistikleri
- Enemy type/variant kill sunumu
- Unlock/achievement highlight alanı
- Claim gerektirmeyen unlock sunumu
- Profile ekranı
- Statistics ekranı
- Achievements ekranı veya güvenli empty state
- Local profile persistence bağlantısı
- Run reset/profile persistence ayrımı
- İngilizce metin denetimi
- Temel klavye/mouse accessibility
- Event/presenter mimarisi
- UI performans düzeltmeleri
- EditMode testleri
- PlayMode testleri
- Manuel fiziksel testler
- Release build ve Player smoke
- İzlenebilirlik ve paket raporu
- Yalnız gerçekleşmiş WP durum güncellemeleri

## Kesinlikle kapsam dışında

Bu pakette uygulama:

- Yeni karakter
- Dördüncü skin
- Skin unlock sistemi
- Skin fiyatı
- Skin rarity
- Yeni klasik silah
- Special Weapon
- Armor
- Special/Elite/Ultra düşman
- Nihai XP değerleri
- Nihai level curve
- Yeni achievement içeriği
- Yeni unlock içeriği
- Nihai upgrade dalları
- DEC-048
- DEC-050
- Ücretli reroll
- Round başına tek satın alma kuralı
- B3/B4/B5 kararlarını kapatma
- Abort profile davranışını değiştirme
- Online profile
- Cloud save
- Leaderboard
- Account sistemi
- Telemetry servisi
- Çoklu Player save lock
- Final gamepad kabulü
- Final UI art direction
- Higgsfield üretimi
- Adobe üretimi
- Final ikon seti
- Final font lisansı
- Final UI sesleri
- Final UI VFX
- Remote/push
- Yayınlama

Kapsam dışı bir konu paketi gerçekten engelliyorsa C-00’a bildir. Kendiliğinden kapsam genişletme.

## Zorunlu EditMode testleri

En az aşağıdaki sözleşmeleri kapsa.

### Navigation

- Main Menu → Selection geçişi
- Selection → Run geçişi
- Death → Results geçişi
- Results → Main Menu geçişi
- Main Menu → Profile ve geri dönüş
- Main Menu → Statistics ve geri dönüş
- Main Menu → Achievements ve geri dönüş
- Çelişkili iki ana UI state aynı anda açılamaz
- Duplicate event duplicate ekran açmaz
- Yeni run eski UI state’ini taşımaz

### Selection memory

- Üç skin de available durumundadır.
- Skin seçimi gameplay stat değiştirmez.
- Aynı app session’da son skin hatırlanır.
- Aynı app session’da son weapon hatırlanır.
- Seçim ekranı her run öncesi yine açılır.
- App relaunch varsayılan skin’e döner.
- App relaunch varsayılan weapon’a döner.
- App relaunch permanent profile’ı sıfırlamaz.

### Results snapshot

- Results immutable RunSummary kullanır.
- Run reset Results verisini silmez.
- Results tekrar açılması profile ikinci kez veri yazmaz.
- Reached Round doğru gösterilir.
- Completed Rounds doğru gösterilir.
- Total Kills doğru gösterilir.
- Standard kills doğru gösterilir.
- Bilinmeyen varyant verisi uydurulmaz.
- Gold/build özeti varsa RunSummary ile eşleşir.

### XP

- Tamamlanan round round XP’si verir.
- Tamamlanmamış ölüm roundu round XP’si vermez.
- Tamamlanmamış rounddaki geçerli kill kill XP’sine dahil olabilir.
- Bu kill kalıcı istatistiğe dahil olabilir.
- Gold kazanma XP vermez.
- Gold toplama XP vermez.
- Gold harcama XP vermez.
- Upgrade satın alma XP vermez.
- Oynama süresi XP vermez.
- Aynı run iki kez XP üretmez.
- Profile level combat stat değiştirmez.
- Profile level başlangıç gold’u değiştirmez.

### Profile persistence

- Highest Round kalıcıdır.
- Completed Rounds kalıcıdır.
- Total Kills kalıcıdır.
- Variant kill schema güvenli çalışır.
- Profile XP kalıcıdır.
- Profile Level kalıcıdır.
- Run Gold kalıcı profile yazılmaz.
- Run Build kalıcı profile yazılmaz.
- Weapon upgrade rank’ları yeni run’a taşınmaz.
- Missing save güvenli default üretir.
- İkinci run önceki run sonuçlarını yanlışlıkla tekrar eklemez.

### Unlock/Achievement

- Unlock varsa otomatik uygulanır.
- Claim işlemi gerekmez.
- Aynı unlock iki kez işlenmez.
- Achievement yoksa ekran hata vermez.
- Empty state doğru görünür.
- Çağıransız `UnlockProfileLevel` onaylanmamış içeriğe bağlanmaz.

### Pause/UI

- Pause gameplay UI state’ini doğru kapılar.
- Pause ile Results aynı anda açılamaz.
- Pause ile Upgrade kontrol çakışması üretmez.
- `GateForPause` kare başına sahne araması yapmaz.
- Timescale sıfırken UI geçişleri güvenli çalışır.
- Delayed Results yeni koşuya sızmaz.

## Zorunlu PlayMode testleri

En az şu gerçek akışları kapsa:

- Uygulama Main Menu ile açılır.
- Play, Selection ekranını açar.
- Üç skin görünür ve seçilebilir.
- Skin seçimi player statlarını değiştirmez.
- Mevcut weapon seçilebilir.
- Start Run doğru koşuyu başlatır.
- HUD Round 1’i doğru gösterir.
- HUD health/ammo/gold snapshot’larını doğru gösterir.
- Gold toplandığında HUD event ile güncellenir.
- Round bitince Upgrade ekranı bir kez açılır.
- Upgrade ekranı auto-collect sonrası doğru bakiyeyi gösterir.
- Satın alma sonrası gold/build sunumu doğru yenilenir.
- Next Round HUD state’ini doğru yeniler.
- Oyuncu öldüğünde Upgrade açılmaz.
- Oyuncu öldüğünde Results bir kez açılır.
- Results run reset sonrası doğru veriyi korur.
- Results reached round bilgisini doğru gösterir.
- Results kill istatistiklerini doğru gösterir.
- Results XP breakdown’ı doğru gösterir.
- Gold işlemleri XP’ye yansımaz.
- Results → Main Menu çalışır.
- Profile yeni kalıcı değerleri gösterir.
- Statistics yeni kill/round değerlerini gösterir.
- Achievements tanımsızsa güvenli empty state gösterir.
- Aynı app session’daki ikinci run’da seçimler hatırlanır.
- Seçim ekranı ikinci run öncesi yine açılır.
- Tam app relaunch sonrası varsayılan seçimler geri gelir.
- Tam app relaunch sonrası profile/statistics korunur.
- İkinci run duplicate presenter/listener üretmez.
- Pause bütün simülasyonu dondurur.
- Pause kapanınca doğru focus döner.
- Bütün kullanıcı metinleri İngilizcedir.
- Gerçek Metal yürütmesinde testler geçer.

## Manuel fiziksel kontroller

En az şu akışı gerçek Player üzerinde tamamla:

1. Uygulamayı aç.
2. Main Menu’yu doğrula.
3. Play’e bas.
4. Selection ekranının açıldığını doğrula.
5. Üç skinin açık olduğunu doğrula.
6. Skin değiştir.
7. Weapon seç.
8. Run başlat.
9. HUD bilgilerini doğrula.
10. Gold topla ve HUD değişimini doğrula.
11. Roundu tamamla.
12. Upgrade ekranının nihai bakiyeyle açıldığını doğrula.
13. Upgrade satın al.
14. Build ve bakiyenin doğru güncellendiğini doğrula.
15. Sonraki rounda geç.
16. Gold ve build’in korunduğunu doğrula.
17. Pause aç ve simülasyonun donduğunu doğrula.
18. Pause kapat.
19. Oyuncuyu öldür.
20. Results ekranını doğrula.
21. Reached Round bilgisini doğrula.
22. Kill değerlerini doğrula.
23. XP breakdown’ı doğrula.
24. Gold’un XP üretmediğini doğrula.
25. Main Menu’ye dön.
26. Profile ekranındaki kalıcı değişimi doğrula.
27. Statistics ekranındaki kalıcı değişimi doğrula.
28. Achievements ekranını/empty state’i doğrula.
29. Yeni run başlat ve seçimlerin session içinde hatırlandığını doğrula.
30. Uygulamayı tamamen kapatıp yeniden aç.
31. Seçimlerin varsayılana döndüğünü doğrula.
32. Profile/statistics verilerinin korunduğunu doğrula.
33. Offline durumda ana akışın çalıştığını doğrula.
34. Bütün kullanıcı metinlerinin İngilizce olduğunu doğrula.
35. Farklı desteklenen pencere boyutlarında taşma olmadığını doğrula.

Gamepad fiziksel kabulü M4’tedir; ancak mümkünse navigation uyumluluğuna ilişkin gözlem raporla.

## Regresyon zorunluluğu

WP-004 tabanı:

- EditMode 297/297
- PlayMode 65/65
- RoundProbe 8/8
- GoldProbe 12/12

korunmalıdır.

Yeni testler sonrasında:

- Bütün EditMode
- Bütün PlayMode
- Gerçek Metal
- Combat regression
- Lifecycle regression
- RoundProbe
- GoldProbe
- Clean Library import
- Release build
- Player smoke

yeniden çalıştırılmalıdır.

Mevcut testi yalnız yeni UI davranışını geçirmek için anlamsız biçimde gevşetme.

## UI performans kontrolleri

Profil ve ölç:

- HUD canvas rebuild
- TextMeshPro string/allocation davranışı
- Gold text update sıklığı
- Build description update sıklığı
- `GateForPause`
- Screen transition allocation
- Results ekranı açılışı
- Büyük statistics listesi
- Event subscription/unsubscription
- İkinci run sonrası duplicate callback
- Scene reload sonrası kalıcı presenter sayısı
- UI kapalıyken devam eden Update çağrıları

Kaçın:

- Kare başına `FindObjectOfType`
- Kare başına scene search
- Kare başına `DescribeBuild`
- Kare başına string interpolation
- Sürekli bütün profile verisini yeniden çizme
- Her frame layout rebuild
- Kapanan ekranların event aboneliğini koruması
- Run state’i UI üzerinden polling
- Gereksiz material instance
- Aynı Canvas’ın küçük HUD değişiminde tamamen rebuild olması

## Dokümantasyon

Yalnız gerçekleşmiş durumları kaydet:

- WP-004 tamamlandı/entegre durumu
- WP-005 yürütülme/tamamlanma durumu
- UI navigation sözleşmesi
- Selection session/persistence sözleşmesi
- HUD snapshot kaynakları
- Results snapshot sözleşmesi
- XP breakdown sözleşmesi
- Profile persistence sınırı
- Unlock/Achievement sunum sınırı
- Pause UI sözleşmesi
- Karar → requirement → ekran/modül → test matrisi
- PrototypeOnly alanlar
- Test sonuçları
- Performans sonuçları
- Ertelenen kararlar
- WP-006 entegrasyon notları

Yeni ürün kararı oluşturma.

Onay bekleyen önerileri karar dosyasına ekleme.

## Git güvenliği

- Başlangıç commit ve etiketini doğrula.
- `main` ağacının temizliğini doğrula.
- WP-005 için izole branch/worktree kullan.
- Tek writer/commit owner belirle.
- Başlangıç manifesti oluştur.
- Dosya sahipliğini açıkça koordine et.
- Diğer worktree’lere müdahale etme.
- Kullanıcının değişikliklerini koru.
- `git add -A` kullanma.
- Dosyaları açıkça stage et.
- Force push yapma.
- Rebase yapma.
- Destructive reset yapma.
- Remote ekleme veya push yapma.
- Library, Temp, Logs, build ve secret dosyalarını commit etme.
- `.meta` ve GUID bütünlüğünü doğrula.
- Test geçmeden commit oluşturma.
- Atomik ve geri alınabilir commit kullan.
- S-01 ve S-10 kabulü olmadan başarı ilan etme.
- Entegrasyon sonrası main, etiket, manifest ve temiz ağacı tekrar doğrula.

Bütün kapılar temizse C-00 mevcut paket zinciriyle fast-forward entegrasyonu koordine edebilir. Push yapılmayacaktır.

## S-10 bağımsız kabulü

S-10 yalnız S-09 raporuna güvenmemelidir.

Bağımsız olarak doğrula:

- Main Menu akışı
- Selection ekranı
- Tek karakter
- Üç açık skin
- Skinlerin oynanışa etkisizliği
- Session içinde seçim hafızası
- Relaunch sonrası varsayılan seçim
- Permanent profile korunması
- HUD snapshot doğruluğu
- Upgrade ekranı bakiyesi
- Upgrade icon fallback
- `GateForPause` performansı
- Pause simülasyon donması
- Ölüm → Results geçişi
- Results immutable snapshot
- Reached Round
- Completed Rounds
- Kill/variant kill sunumu
- XP breakdown
- Gold/time XP izolasyonu
- Profile level sunumu
- Profile persistence
- Run gold/build reset
- Claim gerektirmeyen unlock davranışı
- Achievement empty state
- İngilizce metinler
- Klavye/fare ana akışı
- UI lifecycle ve duplicate handler kontrolü
- EditMode sonuçları
- PlayMode sonuçları
- Gerçek Metal
- RoundProbe
- GoldProbe
- Clean Library import
- Release build
- Player smoke
- Git F0–F10
- Manifest bütünlüğü
- Remote/push yapılmaması

Sonuç:

- PASS
- FAIL
- BLOCKED

formatında verilmelidir.

Kanıtsız veya koşullu PASS verme.

## Nihai rapor formatı

C-00’a tek ve eksiksiz nihai rapor sun:

1. Paket kimliği
2. Kaynak commit ve etiket
3. Sonuç commit
4. Branch/worktree
5. Katılan Kadro uzmanları
6. İncelenen mevcut UI mimarisi
7. Yapılan üretim değişiklikleri
8. Değiştirilen dosyalar
9. Yeni dosyalar
10. Navigation state sözleşmesi
11. Main Menu
12. Selection ekranı
13. Session selection davranışı
14. Relaunch default davranışı
15. Tek karakter/üç skin doğrulaması
16. Weapon selection
17. HUD snapshot bağlantıları
18. Upgrade ekranı iyileştirmeleri
19. Upgrade icon fallback
20. `GateForPause` düzeltmesi
21. Pause menüsü
22. Death → Results lifecycle
23. RunSummary sözleşmesi
24. Results ekranı
25. XP breakdown
26. Profile ekranı
27. Statistics ekranı
28. Achievements/empty state
29. Unlock sunumu
30. Claim olmaması
31. Local persistence
32. Run/permanent state ayrımı
33. İngilizce metin denetimi
34. Accessibility temeli
35. UI performans ölçümleri
36. Yeni EditMode testleri
37. Yeni PlayMode testleri
38. Regresyon sonuçları
39. Manuel fiziksel testler
40. Clean Library import
41. Release build
42. Player smoke
43. PrototypeOnly alanlar
44. Bilinen sorunlar
45. Ertelenen kararlar
46. WP-006 teknik bağımlılık notları
47. S-01 değerlendirmesi
48. S-10 sonucu
49. Git F0–F10
50. Manifest/hash sonucu
51. Main/worktree/etiket durumu
52. Remote/push durumu

## Tamamlanma tanımı

WP-005 yalnız şu durumda tamamlanmıştır:

- Main Menu → Selection → Run → Results → Main Menu çalışıyor.
- Profile, Statistics ve Achievements ayrı menülerden erişilebilir.
- Tek karakter ve üç açık skin doğru sunuluyor.
- Skinler oynanışa etki etmiyor.
- Seçimler aynı app session’da hatırlanıyor.
- Selection her run öncesi yine açılıyor.
- App relaunch varsayılan seçimlere dönüyor.
- Permanent profile relaunch sonrasında korunuyor.
- HUD authoritative snapshot’ları gösteriyor.
- Upgrade ekranı doğru bakiyeyle açılıyor.
- `GateForPause` kare başına arama yapmıyor.
- Pause bütün simülasyonu donduruyor.
- Ölüm Results ekranını yalnız bir kez açıyor.
- Results run reset sonrasında doğru veriyi koruyor.
- Reached Round ve kill istatistikleri doğru.
- Yarım round kill’leri kill XP/statistiğine girebiliyor.
- Yarım round round XP’si vermiyor.
- Gold, upgrade ve oynama süresi XP vermiyor.
- Profil seviyesi doğrudan güç vermiyor.
- Run gold/build ölümde sıfırlanıyor.
- Unlocklar Claim gerektirmiyor.
- Tanımlı olmayan achievement içerikleri uydurulmuyor.
- Bütün kullanıcı metinleri İngilizce.
- Klavye/fare ana akışı eksiksiz.
- UI event/lifecycle sızıntısı yok.
- Tüm yeni ve mevcut testler geçiyor.
- Gerçek Metal doğrulaması geçiyor.
- RoundProbe ve GoldProbe geçiyor.
- Clean Library release build ve Player smoke geçiyor.
- S-01 koşulsuz PASS veriyor.
- S-10 bağımsız PASS veriyor.
- Yeni ürün kararı oluşturulmamış.
- DEC-048 ve DEC-050 uygulanmamış.
- B5 değiştirilmemiş.
- Git ve manifest denetimleri temiz.
- Remote ve push yapılmamış.

Şimdi WP-005’i başlat. Kritik bir engel oluşmadıkça bütün kabul kriterleri sağlanıncaya kadar kesintisiz çalış.
