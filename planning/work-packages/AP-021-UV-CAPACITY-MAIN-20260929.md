# AP-021 — UV-CAPACITY · main çalışma dalgası 2026-09-29

Sahip: P-07. Tek entegrasyon sahibi: C-00 main.

C-00 main · 29.09.2026. Tek koordinatör: 01a0eb42-07c6-7e42-b18b-3c8005556f83.
Kullanıcının son talimatları: aktif koordinasyon ve iş paketi/rapor dağıtımı bu sohbetindir; önce oturumlara görev dağıtılırken C-00 bugünkü yararlı teslimleri değerlendirip alır. Eski C-00 arşivlendi.
Karar sırası: kullanıcının son açık talimatı → güncel planning/00 DEC → tarihli C-00 → paket → yön belgesi. Ürün/teknoloji/maliyet/geri dönüşsüz karar kullanıcıda. Yeni DEC/WP/AP numarası uydurma.
Frozen başlangıç main: 3c0875e1e259cebae34e2cb496d03bb4e8dd9045; origin/main ff06d5793e1e9b703fa128b4692c74f05a59b714. Başlamadan canlı main/dirty-state doğrula. Bugünkü kaynak/kanıt ve eski dal/worktree korunur; geçerli aktif worktree'yi yeniden adlandırma/yerinden taşıma.
Bu çalışma için Kadro sistemindeki mevcut ajan ve uzman havuzunu değerlendir. En yüksek profesyonel kaliteyi elde etmek için gereken bütün uzmanları göreve dahil et; ajan sayısını önceden sınırlama. Bağımsız görevleri paralel yürüt, aynı dosya veya sistemlere dokunan görevleri bir entegrasyon sorumlusu altında koordine et. Ara raporu nihai teslim sayma. Kabul kriterleri sağlanana, hatalar düzeltilene ve testler başarıyla tamamlanana kadar çalışmayı kesintisiz sürdür. Yalnızca onaylanmış proje kararını değiştirecek, geri dönüşü zor, maliyet doğuran veya sonucu esaslı biçimde etkileyen kritik bir durum varsa kullanıcı kararına yükselt.
Faz A: mevcut DIRECTION/kararları tüket, dar teknik plan/API/sahiplik ve kritik listeyi yaz; İLK TESLİM'i C-00'a bildir. Faz B: tek yazar ve izole güncel main tabanı; küçük dilimler/atomik commit. Ana repo/main ve diğer sahibin dosya/worktree/lease'ine yazma. C-00 promptları ve planning00/03/04 C-00'a aittir.
Test/Player/Unity/Blender yalnız slot altında; Player ve PlayMode snapshot→run→restore, hash/exit doğrulaması; gerçek profile dokunma. Disk<15GB veya swap>12GB iken Unity yok. Bilinmeyen lease/kilit silme yok. Perf kirliyse KİRLİ rapordur, kapı değildir.
Kredi tavanı0; yeni paket/sürüm/lisans/servis/hesap/yayın yok. Force push/reset/rebase/amend/git add -A ve geçmiş yeniden yazma yok. Taban ≥3 paket gerideyse güncel main üstünde yeni r2 dal/atomu; eski teslim kalır. Main'e merge/cherry-pick/tag/push C-00 dışında yok.
Kod var ≠ koşuldu ≠ kabul edildi. Kanıt yoksa DOĞRULANMADI; önceki PASS yeni source/SHA'ya aktarılmaz. Kabul/AD/MUST/HOLD ve kullanıcı kapıları korunur.
Teslim: ≤10 satır, paket/dilim · SHA@dal(tab an SHA) · dosyalar · XML/log/receipt/hash yolları · test sayısı/original exit/guard · kabul sınırı · açık/devir/karar/kredi0. Gerekirse tam manifest ve test ledger'i kendi evidence alanında. Raporu bu sohbetinde yaz; main koordinatörü okuyarak değerlendirecek.

## Amaç ve kullanıcıya görünen sonuç
Bugünkü RAM aday/IRON semantik çıkarımını koru; SMG2 junction için authoritative UV kapasitesini kanıtlayıp geçerli RAM aday üret.

## Kapsam / kapsam dışı ve bağımlılıklar
Yalnız AP-021 junction-stage-v001 proof-cache ve RAM. v003.1/3.2 .blend/atlas/model kaynakları read-only; source export/model soket/ADS üretim kabulünü sessizce değiştirme. Material IRON kaynağını görüntü/indeks/hashla doğrula.

## Kabul ölçütleri / teslimatlar
Üç iç UV dikişi, body/cavity/topoloji, coplanar kesişim ve packing ölçütleri geçer veya açık FAIL ölçülür. Küçük IRON chart'a sığmayan denemeyi veri değişmeden tekrarlama. LOD19671/5896/1782 aday manifest/provenansını koru; physicalReady ancak tüm fiziksel/AD kapısı varsa. Paidrender/export0.

## Test ve kanıt
Mevcut RAM/hash/readback analizleri; anlamlı yeni tek Blender readback slot altında, disk≥15GB ve overlap yok. Unity/main yazımı yok.

