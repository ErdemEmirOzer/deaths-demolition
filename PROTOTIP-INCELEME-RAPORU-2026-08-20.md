# Deaths Demolition — Prototip İnceleme Raporu

**Tarih:** 20 Ağustos 2026  
**İncelenen proje:** `prototype/DeathsDemolition_PlayableConcept`  
**Sonuç:** Prototipin sistem kapsamı güçlü, ancak mevcut macOS oyuncu derlemesi görsel kabul için uygun değil. Açılıştaki ağır bozulmanın en güçlü ve doğrudan kanıtlanan kök nedeni, URP shader'larının oyuncu derlemesinde bulunmaması ve kodun URP ile uyumsuz `Standard` shader'a düşmesidir.

## Yönetici özeti

- Proje Unity `2022.3.22f1`, URP `14.0.10` ve Input System `1.7.0` kullanıyor.
- `Assets/_Prototype` altında 226 C# dosyası ve yaklaşık 62.764 satır kod var.
- Beş sahneli meta akış Build Settings'te doğru sırada: `Boot → MainMenu → Selection → Run → Results`.
- 99 test metodu bulunuyor: 81 EditMode ve 18 PlayMode. Ancak bu inceleme sırasında Unity Editor çalıştırılabilir dosyası sistemde bulunamadığından güncel kaynak üzerinde testler yeniden koşturulamadı; proje altında güncel bir `TestOutput` kanıtı da yok.
- Hazır `.app` açılıyor ve Metal/Apple M1 Pro ile başlatılıyor, fakat `Player.log` açıkça şu uyarıyı üretiyor: `[Zombie] URP Lit shader not found. Falling back to Standard.`
- Projede hiçbir `.mat`, `.shader` veya `.shadervariants` asset'i yok. Dünya, oyuncu, düşman ve efekt materyalleri çalışma zamanında `Shader.Find(...)` ile kuruluyor. Bu yapı, yalnızca kod içindeki shader adı nedeniyle shader'ın oyuncu derlemesine alınacağını garanti etmiyor.
- Mevcut `.app` güncel kaynak koddan eski: derlenmiş `DD.Prototype.Runtime.dll` 19 Ağustos 2026 09:04; kaynakta 20 Ağustos 2026 01:35'e kadar değişiklik var. Bu nedenle mevcut build güncel kod için geçerli doğrulama kanıtı değildir.

## Öncelikli bulgular

### P0 — Oyuncu derlemesinde URP shader stripping / eksik shader

**Oyuncunun gördüğü:** Dünya, karakter veya düşman materyallerinde pembe/magenta, yanlış renk, bozuk yüzey ya da genel olarak ağır görsel bozulma oluşabilir.

**Doğrudan kanıt:**

- Oyuncu günlüğü: `/Users/Erdem/Library/Logs/Deaths Demolition/Deaths Demolition - Playable Concept/Player.log`
- Günlük satırı: `[Zombie] URP Lit shader not found. Falling back to Standard.`
- `Assets/_Prototype/Enemies/ZombiePalette.cs:119-130`, URP Lit ve Simple Lit bulunamayınca `Standard` shader'a düşüyor.
- `Assets/_Prototype/World/WorldPalette.cs:286-295` ve `Assets/_Prototype/Player/PlayerRigFactory.cs:304-310` aynı deseni sessizce uyguluyor.
- Efekt ve atmosfer tarafında da `Shader.Find` kullanılıyor: `Feedback/FeedbackMaterials.cs`, `Enemies/EnemyEffects.cs`, `World/WorldAtmosphere.cs`.
- `Assets` altında shader'ı seri hale getirilmiş bir materyal, shader registry veya shader variant collection bulunmuyor.
- `ProjectSettings/GraphicsSettings.asset` URP pipeline asset'ini doğru bağlıyor; sorun pipeline atamasından çok shader'ların build'e güvenilir biçimde dahil edilmemesiyle uyumlu.

**Neden önemli:** `Standard` shader Built-in Render Pipeline içindir. URP oyuncu derlemesinde buna düşmek güvenilir bir fallback değildir ve error/magenta materyal üretebilir. Üstelik dünya ve oyuncu fallback'leri uyarı vermediği için günlükte görünen tek zombi uyarısı gerçek kapsamı olduğundan küçük gösterebilir.

**Sahip alt sistem:** Rendering / build pipeline.

### P1 — Dağıtılan `.app` güncel kaynakla eşleşmiyor

- Hazır derlemenin runtime DLL'i: 19 Ağustos 2026 09:04.
- Daha yeni kaynak değişiklikleri: 20 Ağustos 2026 01:35'e kadar.
- Özellikle `PlayerMotor.cs`, oyuncu hareket testleri ve config asset'i build'den sonra değişmiş.

**Etkisi:** Hazır uygulamada görülen davranış güncel kaynak için kesin doğrulama sayılamaz. Shader düzeltmesinden sonra temiz ve yeni bir build zorunludur.

### P1 — Güncel otomatik kapı kanıtı yok

Projede kapsamlı test kodu ve ayrıntılı test planı var; ancak `TestOutput` sonucu yok ve bu makinede Unity Editor çalıştırılabilir dosyası bulunamadı. Bu nedenle 99 testin varlığı doğrulandı, başarı durumu doğrulanmadı.

**Gerekli kapı:** Derleme logu, EditMode sonucu, PlayMode Smoke sonucu ve Player build günlüğü aynı kaynak revizyonundan üretilmelidir.

### P2 — Başlangıç pencere boyutu ek bir risk

`PrototypeBuild.cs` başlangıç penceresini sabit `1920×1080` oluşturuyor. Daha küçük mantıksal masaüstlerinde pencere taşabilir veya UI kırpılmış algılanabilir. Ana kanıtlanan hata shader olsa da onarım turunda `1280×720`, `1600×900`, `1920×1080` ve macOS Retina ölçeklerinde UI ayrıca kontrol edilmelidir.

### P3 — Dokümantasyon sayıları güncel değil

README 211 C# dosyası bildiriyor; mevcut sayı 226. Bu çalışmayı engellemez, ancak teslimat raporlarının otomatik üretilmesi veya güncellenmesi gerekir.

## Güçlü taraflar

- Sistemler modüler klasörlere ayrılmış: input, player, weapons, damage, enemies, spawn, navigation, rounds, economy, UI, profile, audio ve feedback.
- Build Settings'te beş sahne mevcut ve etkin.
- Statik taramada kopuk `m_Script: {fileID: 0}` referansı bulunmadı.
- Prototip çalışma kodunda ağ API kullanımı bulunmadı; çevrimdışı hedefle uyumlu.
- Yerel profil yazımı geçici dosya üzerinden ilerliyor ve bozuk kaydı koruyan fallback içeriyor.
- Test paketi geniş: formül, bölgesel hasar, proje bütünlüğü, akış, hareket, duvar teması, HUD senkronu ve dünya z-fight kontrolleri var.
- Prototip varsayımları `[PrototypeOnly]` ile açıkça etiketlenmiş; plan dokümanları prototipten ayrılmış.

## Onarım kabul ölçütleri

1. Güncel macOS Player build'inde `Universal Render Pipeline/Lit`, `Universal Render Pipeline/Unlit` ve `Universal Render Pipeline/Particles/Unlit` çalışma zamanında null olmamalı.
2. Normal açılış ve Run sahnesinde Player.log içinde `URP Lit shader not found`, `Falling back to Standard`, `error shader`, pembe/magenta materyal veya shader compile hatası bulunmamalı.
3. Runtime materyal üreticileri `Standard` shader'ı URP fallback'i olarak kullanmamalı.
4. Shader bağımlılıkları yalnız string tabanlı `Shader.Find` çağrılarına bırakılmamalı; seri hale getirilmiş asset referansları veya eşdeğer güvenilir build-inclusion mekanizması kullanılmalı.
5. Yeni build, en son kaynak dosyasından sonra üretilmiş olmalı.
6. EditMode ve PlayMode Smoke testleri sıfır hata ile tamamlanmalı.
7. Açılış, menü ve Run ekranları en az `1280×720`, `1600×900` ve `1920×1080` boyutlarında görsel olarak kontrol edilmeli.
8. `planning/` ve `game/` değiştirilmemeli; düzeltme yalnız prototip klasöründe kalmalı.

## Doğrulama sınırı

Bu incelemede hazır `.app` başlatıldı ve Player.log alındı; fakat çalışma ortamının ekran yakalama izni olmadığı için görsel ekran görüntüsü üretilemedi. Kullanıcının gördüğü ağır bozulma bildirimi ile shader günlüğü yüksek güvenle örtüşüyor. Yine de düzeltme turunda önce/sonra ekran görüntüsü zorunlu kabul ölçütü olarak korunmalıdır.
