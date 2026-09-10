# Deaths Demolition — Prototip Onarım Promptu

Aşağıdaki metni diğer yapay zekâya aynen iletin:

```text
Deaths Demolition Unity prototipindeki açılış görsel bozulmasını kök nedenden düzelt.

Çalışma alanı:
/Users/Erdem/Documents/Deaths Demolition

Unity projesi:
/Users/Erdem/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept

Sürüm kısıtı:
- Unity 2022.3.22f1
- URP 14.0.10
- Mevcut plan kararlarını değiştirme.
- planning/ ve game/ klasörlerine kesinlikle dokunma.
- Düzeltmeleri yalnız prototype/DeathsDemolition_PlayableConcept altında yap.

Kanıtlanan arıza:
Hazır macOS build açıldığında ekran ciddi biçimde bozuk görünüyor. Player log şu satırı üretiyor:
[Zombie] URP Lit shader not found. Falling back to Standard.

Log yolu:
/Users/Erdem/Library/Logs/Deaths Demolition/Deaths Demolition - Playable Concept/Player.log

Kök neden için doğrulanmış kod noktaları:
- Assets/_Prototype/Enemies/ZombiePalette.cs:119-130
- Assets/_Prototype/World/WorldPalette.cs:286-295
- Assets/_Prototype/Player/PlayerRigFactory.cs:304-310
- Assets/_Prototype/Feedback/FeedbackMaterials.cs
- Assets/_Prototype/Enemies/EnemyEffects.cs
- Assets/_Prototype/World/WorldAtmosphere.cs

Bu kodlar çalışma zamanında Shader.Find(...) kullanıyor ve URP shader bulunamayınca Built-in Render Pipeline'a ait Standard/Sprites/Default gibi fallback'lere düşüyor. Assets altında .mat, .shader veya .shadervariants asset'i yok; dolayısıyla gerekli URP shader'ları Player build'e güvenilir biçimde dahil edilmiyor. GraphicsSettings URP pipeline asset'ini doğru bağlıyor; yalnızca pipeline atamasını yeniden yapmak çözüm değildir.

İstenen çalışma:
1. Sorunu önce güncel kaynak üzerinde yeniden üret; açılış, Main Menu ve Run sahnesinden ekran görüntüsü ile Player.log kanıtı al.
2. Tüm Shader.Find çağrılarını ve çalışma zamanında yaratılan materyalleri envanterle.
3. Gerekli shader'ları Player build'e güvenilir biçimde dahil eden, seri hale getirilmiş referanslara dayalı küçük bir rendering altyapısı kur. Tercih edilen çözüm:
   - Assets/_Prototype/Rendering altında bir PrototypeShaderLibrary/ShaderRegistry ScriptableObject ve Resources asset'i oluştur.
   - URP Lit, URP Unlit ve URP Particles/Unlit shader referanslarını asset üzerinde seri hale getir.
   - Dünya, oyuncu, zombi, atmosfer ve feedback materyal fabrikaları shader'ları bu registry'den alsın.
   - Gerekliyse ShaderVariantCollection veya build preprocessing ile kullanılan varyantları koru.
   - Asset üretimini/atamasını Unity Editor API ile yap; .asset YAML veya GUID değerlerini elle uydurma.
4. URP altında Standard shader'a düşmeyi kaldır. Fallback gerekiyorsa açıkça referanslanan URP uyumlu bir shader kullan. Eksik registry/shader durumunda anlaşılır ve tekil bir hata üret; sessizce bozuk materyal yaratma.
5. Bir editor/build doğrulayıcısı ekle: build başlamadan shader registry ve gerekli shader referansları eksikse build'i anlaşılır mesajla durdursun.
6. Sorunun geri gelmesini önleyen test ekle. EditMode testi registry'nin dolu olduğunu; mümkünse Player build smoke kontrolü gerekli shader'ların null olmadığını ve logda fallback uyarısı bulunmadığını doğrulasın.
7. PrototypeBuild.cs içindeki sabit 1920x1080 başlangıç penceresini de kontrol et. Küçük/Retina masaüstünde taşma veya UI kırpılması varsa güvenli bir başlangıç boyutu ya da ekrana sığdırma uygula; referans çözünürlüğü 1920x1080 kalabilir.
8. Mevcut Build/DeathsDemolition.app güncel değil. Düzeltmeden sonra temiz, yeni bir macOS build üret. Build'in timestamp'i en yeni kaynak değişikliğinden sonra olmalı.
9. Aşağıdaki doğrulamaları çalıştır ve sonuçları dosya yollarıyla raporla:
   - batchmode derleme kapısı
   - tüm EditMode testleri
   - PlayMode Smoke testi (grafik aygıtıyla, -nographics kullanmadan)
   - macOS Player açılışı ve Run sahnesi
   - Player.log shader/error/exception taraması
   - 1280x720, 1600x900 ve 1920x1080 görsel kontrol

Kabul ölçütleri:
- Görselde pembe/magenta/error materyal yok.
- Player.log içinde "URP Lit shader not found", "Falling back to Standard", "error shader", shader compile error, NullReferenceException veya MissingReferenceException yok.
- URP shader bağımlılığı yalnız string tabanlı Shader.Find'e bağlı değil.
- Dünya, oyuncu, zombi, atmosfer ve feedback doğru shader'la çiziliyor.
- Main Menu ve Run UI belirtilen üç çözünürlükte kırpılmıyor/üst üste binmiyor.
- EditMode ve PlayMode Smoke testleri sıfır hata.
- planning/ ve game/ değişmemiş.

Sadece teşhis yazma; düzeltmeyi uygula, yeni build üret, test et ve sonunda değişen dosyaları, test sonuçlarını ve kalan riskleri kısa bir raporla ver. Mevcut kullanıcı çalışmalarını silme veya resetleme.
```
