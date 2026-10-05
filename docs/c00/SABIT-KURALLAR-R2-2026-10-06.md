# DEATHS DEMOLITION — SABİT KURALLAR VE GELİŞTİRME PRENSİPLERİ (R2, BAĞLAYICI)

**Sürüm:** v1 · 06.10.2026 · hazırlayan: KADRO context-engineer · inceleyen ve onaylayan: C-00 (06.10) · kaynak tabanı: main `18f3d01e`, C-00 düzeltmeleri `19baeee9` sonrası · **durum: BAĞLAYICI.** Bütün oturumlar (S-01…S-10), üretim ajanları ve C-00 bu kitaba uyar. Bu kitap §0.9'daki kaynakların tekilleştirilmiş özetidir; **§13 C-00 kararları**, §12'de "BELİRSİZ" kalan her çelişkiyi çözer. Kitapla bir kaynak çelişirse kitabın tarihli maddesi geçerlidir; yeni bir çelişki görülürse C-00'a yazılır.

---

## 0. Kapsam, öncelik kuralı, sürüm

0.1 **Kapsam:** C-00 (yerel koordinatör, tek entegratör), S-01…S-10 konu oturumları, üretim ajanları (C-00'ın Claude alt ajanları, P-xx, Codex CX görevleri), C-00B ve bulut oturumları. Yalnız bir role özgü kural, maddede rolüyle yazılmıştır. [OA §0; BRF §0, §2; AGM]

0.2 Rolü verilmemiş oturum salt okunur kalır, hiçbir şeyi değiştirmez, C-00'a sorar. [PSP §0.1; OA §0.2]

0.3 **Karar önceliği (çelişkide üstteki kazanır):** (a) kullanıcının en son açık talimatı → (b) `planning/00` DEC tablosu (aynı konuda en yeni DEC) → (c) C-00'ın tarihli kararları ("C-00 gg.aa", commit gövdeleri) → (d) paketin birebir kayıtlı promptu → (e) yön belgesinin en son katmanı / "§0 S-xx kararları" → (f) eski metinler. Aynı kademede en yeni tarih kazanır. [PSP §0.3; görev tanımı 06.10]

0.4 **Belge önceliği:** BRF ve DEV, PSP'yi R2 için günceller; çelişkide daha yeni belge geçerlidir. KADRO'daki `K/c-00/…` kopyası ile depodaki `docs/c00/` ayrışırsa depodaki geçerlidir. [DEV §0.1; BRF §0; OA §0.12]

0.5 Çelişki görülürse uygulamadan önce C-00'a sorulur; C-00 değişen bir talimatı açıkça İPTAL eder. [M4R1 §4.14f; m:profesyonellik-standardi 25.09]

0.6 Yön belgeleri ve `planning/04`'ün M4 bölümleri yerinde yamalanır ve bayatlayabilir; güncel gerçek çoğu zaman commit gövdesindedir. [PSP §0.3]

0.7 "BELİRSİZ" etiketli maddeler, C-00 kararı gelene kadar önerilen varsayılanla uygulanır; karar belgeye yazılmaz. [PSP §8]

0.8 Bu kitap KADRO `context/memory/`'ye yazılmadı (G5 kapısı yok). KADRO Değişmezler Defteri boştur; buradaki kuralların hiçbiri kullanıcı onaylı "değişmez" olarak kayıtlı değildir. [KDR §8.3; DGM]

0.9 **Kaynak etiketleri** (kaynak listesi görev tanımından; ek olarak okunanlar: R2P, M4R1, CXS, AGM, git commit gövdeleri) [görev tanımı 06.10]:

| Etiket | Dosya |
|---|---|
| PSP | `Repo/docs/c00/PROJE-SISTEM-PROMPTU-2026-09-29.md` |
| BRF | `Repo/docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` |
| DEV | `Repo/docs/c00/C00-DEVIR-2026-10-05.md` |
| OA | `Repo/docs/c00/OTURUM-ACILIS-2026-10-06.md` |
| G4 | `Repo/docs/c00/G4-GUARD-KARARLARI-2026-09-30.md` |
| D8 | `Repo/docs/c00/GUARD-D8-GOC.md` |
| R2P | `Repo/docs/c00/R2-PLAN.md` |
| TR | `Repo/tools/README.md` (parantez içinde araç adı) |
| M4R1 | `K/m4-r1/M4-R1-REVIZYON-TURU.md` |
| CXS | `K/c-00/codex/CODEX-SISTEM-R2.md` |
| AGM | `Repo/AGENTS.md` |
| KDR | `~/Desktop/KADRO/KADRO.md` |
| DGM | `~/Desktop/KADRO/context/memory/degismezler.md` |
| EG | hafıza `entegrasyon-gunlugu-2026-09.md` (tarihli satır) |
| m:<ad> | hafıza dosyası `~/.claude/projects/-Users-Erdem-Documents-Deaths-Demolition/memory/<ad>.md` |

`Repo` = `/Users/Erdem/Documents/Deaths Demolition` · `K` = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`.

---

## 1. Kullanıcı ve yetki

1.1 Kullanıcı Erdem Emir Özer projenin tek sahibidir. [PSP §0.2]

1.2 **Dil:** kullanıcıyla, raporlarda ve planlama belgelerinde Türkçe; commit konuları/mesajları ve kod kimlikleri İngilizce (mevcut biçim); test adları Türkçe. Oyunun kaynak/varsayılan dili İngilizce, Türkçe Settings › LANGUAGE ile seçilir. [PSP §3.8; BRF §3; OA §0.1]

1.3 **Tam yetki (kullanıcı 05.10):** "hiçbir şey sorma, her şeyi yapman ve onaylaman için izin veriyorum." Kararları ve görsel onayları (görsel kapı seçimleri, önce/sonra onayları dahil) C-00 verir, sonra kullanıcıya görselle bildirir. Seçim sayfası açılabilir ama onay beklenmez. [BRF §3; DEV §1; m:feedback-sormadan-devam 05.10]

1.4 **Tam yetkinin istisnaları (her seferinde kullanıcıya):** satın alma / kredi ekleme / ödeme · hesap açma · şifre girme · kalıcı silme (Çöp'ü boşaltmak kullanıcının adımıdır) · başka bir oturumun izin sisteminin reddettiği işlemi onun yerine yapmak (kullanıcıya iletilir). [BRF §3; DEV §1; m:feedback-sormadan-devam 05.10]

1.5 Oturumlar ve ajanlar kullanıcıya soru sormaz (AskUserQuestion dahil). Karar ihtiyacı C-00 oturumuna mesajla gider (`SendMessage` ya da `send_message`; hedef C-00 `local_b4e17fbf-ec7d-4405-9b5b-63878ace8c15`); beklerken bağımsız iş sürer. Başka oturumla doğrudan varılan karar C-00'a da yazılır. [OA §0.2; M4R1 §4.14c]

1.6 C-00 tek karar kaynağıdır. [M4R1 §4.14f; PSP §6]

1.7 **Görsel sunum:** seçenekler ve sonuçlar her zaman görselli sunulur (Artifact sayfası, JPEG önizleme, karar kutusu; her seçeneğin büyük görseli ve kısa açıklaması aynı yerde). Metin-only seçim sorusunu kullanıcı reddetti; metin soru yalnız görsel olmayan konularda. [BRF §3; DEV §1; m:feedback-secenek-gorselli 05.10]

1.8 Kullanıcıya ses dinletme sayfasında `.mp4` (AAC) kullanılır; `.m4a` servis edilmiyor. [DEV §8.3; OA S-08 mesajı]

1.9 **"Ne durumdayız" yanıtı:** 1–2 satır genel durum → tablo (iş/paket · sahip · yapılan · kalan · durum `✅ bitti / 🔄 sürüyor / ⏳ bekliyor / 🔴 karar`) → aktif KADRO ajanları (oturum · ajan türü · model · görev; kaynak `K/c-00/aktif-ajanlar/*.md`) → kısa "sizden beklenen". Türkçe, teknik olmayan dil, SHA yok. [m:durum-raporu-tablo 30.09; BRF §3]

1.10 Bilgi mesajlarına "işlem gerekmiyor" yazılmaz. Kullanıcının fiziksel bir adımı gerekiyorsa mesaj başında `> 🔴 **YAPMAN GEREKEN:** tek cümle · nerede · nasıl bildireceği · süre`. `> 🔴 **KARAR GEREKİYOR:**` bloğu 05.10'dan beri yalnız §1.4 istisnalarında ve C-00'a yazılan karar notlarında kullanılır. [m:iletisim-tercihi-karar-isareti 12.09/25.09; BRF §3]

1.11 Onaylanmış bir sonucun icra ayrıntıları (komut, etiket adı, kim çalıştırır) kullanıcıya sorulmaz. [m:iletisim-tercihi-karar-isareti 12.09]

1.12 **Kullanıcının fiziksel adımları:** Claude Desktop'ta oturum açmak (C-00 başlık + klasör + hazır ilk mesaj verir), MCP/OAuth girişleri (C-00 hesap girişi yapmaz), Çöp'ü boşaltmak, iCloud "Mac Depolamasını Optimize Et" ayarı. [m:oturum-acma-kullanici-acar 06.10; DEV §7, §12; m:ders-icloud-dataless 01.10]

1.13 Kullanıcı yalnız kritik olaylarda durdurulur. Ara rapor teslim değildir; kabul kriterleri sağlanana, hatalar düzeltilene ve testler geçene kadar iş kesintisiz sürer. [PSP §0.5, §3.4.37]

1.14 **Mod OTOMATİK:** C-00 görevleri doğrudan dağıtır ve raporları toplar. Her teslim sonrası C-00 kabul eder, sıradaki işi belirler, promptu oturuma gönderir ve kullanıcıya kısa özet verir. Kullanıcı "manuele geçiyoruz" ya da "artık verme" derse durur. [PSP §3.4.39; m:prompt-uretimi-c00 16.09]

1.15 **Revizyon turu modeli:** kullanıcı release build'i oynar → N-kodlu notlar verir → C-00 paket promptlarına böler → paketler bitince yeni build → yeni tur. Tur sonunda release build + `open` komutu + bilinen eksikler verilir. [PSP §3.4.40; m:m4-calisma-modeli 17.09; m:r2-tur-plani 30.09]

1.16 🔴 maddeler varsayılmaz: karar gelene kadar önerilen varsayılanla iş sürer ama karar belgeye yazılmaz ("karar bekleyenler — karar yazılmaz"). [PSP §7.4, §8]

1.17 Kullanıcı bir manuel kit maddesi için "gerek yok / yapmışım gibi kabul et" derse madde kullanıcı kararıyla PASS sayılır; kanıt sınıfı dürüstçe yazılır ("beyan + deterministik eş"). [m:iletisim-tercihi-karar-isareti 12.09; PSP §8]

1.19 **Onay yok (kullanıcı 06.10):** "hiçbir aşamada onay isteme benden otomatik olarak devam et. diğer oturumlar da aynı şekilde" ve "hiçbir şeyi bana sormana gerek yok… tüm yetkileri sana veriyorum". Oturumlar otomatik izin modunda çalışır; kullanıcıya onay kartı, soru ya da seçim gönderilmez. Bir işlem oturumun kendi izin sistemince reddedilirse oturum DURUR, başka yoldan denemez ve C-00'a yazar; C-00 aynı işlemi o oturumun yerine yapmaz (§1.4). İstisnalar §1.4'te kalır; kullanıcının fiziksel adımları §1.12'dedir. [kullanıcı 06.10; m:feedback-sormadan-devam 06.10]

1.18 Prompt metinlerine "hedef: bugün" gibi süre ibaresi yazılmaz. Promptlar tam profesyonel yapıda, süreç yalın. [m:wp-012-ap-005-durumu]

---

## 2. Kullanıcı verisi kutsaldır (§4.14)

2.1 Profil, ayar ve kayıt asla kaybolmaz. Gerçek profile, ayar json'larına, plist'lere ve telemetri klasörüne hiçbir test, probe, bot ya da Player yazamaz. Kullanıcı dosyası silinmez, üzerine yazılmaz. [BRF §3; M4R1 §4.10, §4.14a; PSP §3.6.49]

2.2 **Tek betik kuralı:** her Player ve PlayMode koşusu `userdata-guard.sh snapshot → koşu → restore` penceresinde, tek betikte ve aynı kabukta yapılır. Pencerenin sahibi snapshot'ı çağıran süreçtir ve restore'a kadar yaşamalıdır. Yanlış kalıplar: snapshot'ı ayrı bir araç çağrısında alıp çıkmak, `( … & )` ile arka planda almak, `timeout guard …`, `( cd … && guard … )` alt kabuğu. [OA §0.6; D8 §3; m:kural-player-run 25.09; m:wp-074-075-durumu ders]

2.3 **Kanonik guard:** `DD_GUARD_CANON` = `K/wp-079/driver/tools/userdata-guard.sh` (dd-env.sh). Worktree ya da repo kopyası çağrılmaz; eski yol 64 döner. C-00 sürücüyü main'de detached tutar. [TR (userdata-guard, G4 D4); m:kural-userdata-guard 01.10; G4 M11]

2.4 **Kapsam:** günlük EditMode ve Editor PlayMode koşuları `--scope editor` ile (player slotu almaz, canlı Player'da reddetmez). Player, yakalama ve smoke `--scope player|all` ile. Kabul ve CI pencereleri (main-ci, wp078_accept A1 PlayMode) player slotunu tutar. [m:kural-kilit-sirasi 30.09; m:makine-slot-kurali 30.09; m:kural-player-run 26.09; TR (main-ci)]

2.5 **Player başlatma:** her Player yalnız `tools/player-run.sh <app> …` ile (tek `player` slotu; main-ci koşarken Player başlamaz) ve guard içinde başlatılır. Her build'de `-ddTelemetryDir <out>/telemetry`, dev build'de ayrıca `-ddProfilePath <out>/profile-sandbox/prototype-profile.json` verilir. player-run.sh kullanıcı verisini korumaz, yalnız slotu alır. [m:kural-player-run 25.09; m:kural-userdata-guard 25.09; PSP §3.6.50; TR (player-run)]

2.6 **Guard çıkış kodları** (öncelik 64 > 31 > 30 > 75 > 32 > 0):
- 0: tamam.
- 30/31: DUR + C-00.
- 32: §2.7.
- 64 snapshot'ta (kilit sırası, argüman ya da kök hatası, dolu dizin): sırayı/dizini düzelt.
- 64 restore'da (bağ yok, v1 izi, SNAPSHOT_MISSING): DUR + C-00.
- 65 (örtüşen dirty ya da terk edilmiş pencere): mesajdaki `next:` adımını yalnız o pencereyi açan koşu bittiyse uygula; yakınsamazsa C-00.
- 75 snapshot: sonra yeni dizinle yeniden dene.
- 75 restore (BUSY/LOCKED/erken RACED, `retry=same-dir`): aynı dizinle tekrarla.

[D8 §2; TR (userdata-guard D5–D7); G4 Q1]

2.7 **Exit 32 (OTHER):** `python3 tools/qa/oos_rule.py <ug>` koşulur; rc 0 ise uyarı satırıyla devam edilir (teslimde 1 satır), aksi hâlde DUR + C-00. Kural (C-00 30.09 KALICI): uyarı yalnız şu durumda geçerlidir: değişiklik yalnız player alanında ve `attribution.txt` yazarı, `restore.txt`'i verified=1 olan guard'lı bir player-run penceresi. Editor alanı değiştiyse, yazar atıfsızsa ya da verified≠1 ise DUR. main-ci 32'yi uyarı sayar; rc.sh editor aşamasında yalnız bu kural tutarsa geçer, player aşamasında FAIL verir. [OA §0.6; DEV §2; m:kural-guard-oos-player-uyari 30.09; m:kural-guard-out-of-scope-uyari 30.09; D8 §2; BRF §9]

2.8 **STALE:** otomatik geri yükleme yok, zorlamak için yeniden deneme yok → C-00. Kullanıcının baytları yerinde kalır; C-00 `guard-verdict.json` ve karantinadan elle çözer (veri kaybı olmaması, tutarlı restore'dan önce gelir). [OA §0.6; TR (userdata-guard, C-00 05.10)]

2.9 Restore sonrası verified≠1, kural dışı out-of-scope satırı ya da exit 30/31 çıkarsa DUR + C-00. [m:kural-userdata-guard 30.09; CXS §6]

2.10 Guard pencere dizini tek kullanımlıktır: her koşu yeni bir `<ug>` dizini alır (dolu dizinde snapshot 64 döner). Dizinde `timeline.txt` ve `restore.txt` bulunur: guard `DD_TIMELINE=<ug>/timeline.txt` ile çağrılır, restore satırı ve exit kodu `restore.txt`'e yazılır. [TR (userdata-guard D5); m:wp-085-durumu ders; m:kural-userdata-guard 30.09]

2.11 Eski `wp-*/tools/userdata.sh restore` kopyaları YASAK (sabit 14.09 tabanına döner, plist'i birleştirir). v1 (`68370f87`) ve 066M (`67a9fd82`) guard kopyası taşıyan worktree'lerle, ya da `74f62dd6`, `d6a73dc3`, `f386107b`, `8ccceae5`, `422de54a` (ve bunları içeren etiketlerin) checkout'undaki guard ile guard'lı koşu veya release yapılmaz. [M4R1 §4.14a; m:kural-userdata-guard 24.09; G4 Q9]

2.12 Kullanıcı verisini (profil, ayar, plist, telemetri karantinası dahil) değiştirmek ya da geri yüklemek yalnız C-00'ın işidir. Önce kaynak kanıtı alınır (zaman damgası ↔ test pencereleri, hash) ve karantina kopyası yapılır. Bir test penceresinde yazılmış durum, kullanıcı ilerlemesi diye geri yüklenmez. [m:profesyonellik-standardi 25.09; m:profil-geri-yukleme-karari 24.09; m:kural-kullanici-verisi-kutsal-414 25.09]

2.13 **Bilinen-iyi (KG) ekran değerleri:** tam ekran 1 · 1920×1080 · 0,0. Tek kaynağı sürücü guard'daki kod varsayılanıdır. Oturumlar `DD_GUARD_KNOWN_GOOD` ya da `DD_GUARD_ROOT` vermez; `DD_GUARD_ROOT` doluyken gerçek veriyi koruyan çağıranlar 64 döner. KG yalnız şu sırayla güncellenir: C-00'ın kanıtlı ve tarihli kararı → S-01'in tek satırlık commit'i → sürücü eşitlemesi. [G4 Q3, Q7; m:wp-068-durumu 24.09 (kullanıcı kararı)]

2.14 Guard karantinası ve defteri (`~/.dd-userdata-guard/`) hiçbir araçla silinmez ve süre sınırı yoktur. Takılan ya da dirty pencereyi yalnız C-00 kapatır (`resolution.json`). main-ci'nin `userdata-NOT-RESTORED.txt` işaretini yalnız C-00 `.resolved` yazarak çözer. [G4 Q4, Q5; D8 §6; TR (main-ci, D2 MUST-3a)]

2.15 **Guard'lı koşuyu iptal etme:** önce kendi sarmalayıcı pid'ini, sonra slot.sh bekleyicisini TERM et (bilet silinir), ardından standart restore'u koş. Snapshot süreci öldürüldüyse çocuğu olan `slot.sh acquire player` de öldürülür ve kuyruk `slot.sh status` ile doğrulanır. [m:wp-068-durumu 01.10; m:wp-078-r1-kabul ders]

2.16 Kullanıcı oyunu oynarken hiçbir guard'lı koşu başlatılmaz. [m:wp-078-r1-kabul 01.10]

2.17 Eski build'ler gerçek profille açılmaz (yeni şema eski build'de salt okunur/karantina riski taşır). [PSP §3.6.50, §10]

2.18 Yeni dilimler ve her PlayMode koşusu yalnız güncel main tabanından yapılır (izolasyon `a0db3c7` ve sonrasında var; eski taban gerçek telemetriye yazar). Kuyrukta eski tabanlı bir koşu varsa yalnız kendi pid'iyle iptal edilir. [M4R1 §4.14b; m:unity-batch-dersleri 24.09; m:wp-077-durumu ders]

2.19 **Test yalıtımı:**
- PlayMode testleri `TestProfileIsolation`/`QaTestProfileIsolation` altında koşar.
- Profil ya da ayar yazan fikstür süreç-yerel `dd-pid-<pid>` yolunu kullanır.
- `PlayerPrefs` yalnız izin listesinde kullanılır.
- `DirectToRun` PlayerPrefs anahtarı yasaktır (`-ddDirectToRun` bayrağı kullanılır).
- Smoke ve testler makine-paylaşımlı PlayerPrefs'e yazmaz.

[PSP §10; TR (test scratch, integrity, smoke); m:wp-052-durumu ders]

2.20 Yeni bir Player kullanıcı dosyası eklenirse guard'ın korunan kümesi, `smoke.sh` UIFILES ve S-10 soak listesi aynı işte güncellenir. [TR (smoke.sh); m:wp-066-durumu (WP-066H kuralı)]

2.21 Gerçek telemetri klasörüne düşen bir dosya koşuya kesin olarak bağlanamıyorsa taşınmaz (kullanıcı oynuyor olabilir). Elle temizlik main-ci koşmuyorken yapılır, çünkü CI guard'ı bunu geri alır. [m:wp-079-durumu dersler]

2.23 **Kalıcı latch dosyası yok (kullanıcı + C-00 06.10):** "GUARD-STOP" benzeri, elle kaldırılan kalıcı kilit dosyası proje standardı değildir (yalnız S-06'nın wp-071/wp-083 betiklerinde vardı). Standart: restore exit 32 → `oos_rule.py` rc 0 → uyarı + devam; gerçek hata (verified≠1, 30/31/7/64/65, STALE, MISMATCH, oos rc≠0) → zincir o adımda durur, sıfırdan farklı çıkışla biter ve C-00'a yazılır. Mevcut bir latch oos_rule rc 0 iken düşerse C-00 onu kullanıcının 06.10 talimatıyla `mv` ile yeniden adlandırır (silmez). Guard'lı pencereler başka oturumların Player'larıyla çakışmasın diye uzun PlayMode zincirleri Player slotunu da tutabilir (sıra: player → unity). [kullanıcı 06.10 "kilidi kaldır", "guard stop tarzı şeyler… bundan emin ol"; m:kural-guard-stop-latch-yok 06.10]

2.22 **D9 rollout'tan SONRA geçerli olacaklar** (rollout'u C-00 ilan eder; o güne kadar hiçbir şey değişmez):
- Guard pencere defteri, aynı alanı koruyan iki pencereyi aynı anda açmaz; bekleyenler FIFO kuyruğa girer.
- `DD_GUARD_QUEUE_WAIT` varsayılanı 3600 s (C-00 06.10 onayı); `DD_GUARD_WAIT` 300 s yalnız profil kilidi/BUSY beklemesi içindir.
- Kilit sırası tüm kapsamlarda guard → unity olur; unity tutarken snapshot 64 döner. Sarmalayıcılarda unity slotu snapshot'tan SONRA alınır.
- Tazeleme: worktree main'e getirilir ve `python3 tools/guard_freshness.py <sürücü guard>` 0 dönmelidir.
- main-ci de aynı kuyruktadır ve önceliği yoktur.

[D8 §1–§6; OA S-01 mesajı; BRF §10]

---

## 3. Git ve entegrasyon

3.1 Her paketin benzersiz kimliği, izole dalı/worktree'si ve tek yazma/commit sahibi vardır. Çalışma öncesi ve sonrası dirty-state kaydedilir; paralel ajanlara çakışmayan dosya sahipliği verilir. [PSP §3.5.45]

3.2 Oturumlar ana depoda (`Repo`) açılır. Ana worktree'de dosya düzenleme, commit, checkout ya da stash yapılmaz; main temiz kalır (integrate.sh kapısı). Bütün iş kendi worktree'nde, mutlak yollarla ve `git -C <worktree>` ile yapılır. [OA §0.13] (bkz. §12 Ç12)

3.3 Taban güncel main'dir. Dal adı `wp-NNN-kisa-ad` (revizyonda `-r2`, takip paketlerinde harf eki, dilimler B1…Bn). main dala merge edilmez (merge commit yok); eskiyen dal için main'den yeni dal açılır ve kendi commit'leri cherry-pick edilir. [PSP §9, §11; M4R1 §4.3; m:wp-068-durumu 24.09] (bkz. §12 Ç11)

3.4 **Yasaklar:**
- force push, `git reset`, rebase, amend ve her tür geçmiş yeniden yazımı;
- `git add -A`;
- ref/dal/etiket silme (kendi dalın dahil);
- oturumların push yapması;
- secret, credential, token, lisans anahtarı ya da kişisel verinin repoya veya çıktıya yazılması;
- kullanıcı çalışmalarının silinmesi, resetlenmesi ya da üzerine yazılması.

[PSP §3.5.47; M4R1 §4.3, §4.10; OA §0.5; m:wp-051-durumu ders]

3.5 Bildirilen SHA'ya dokunulmaz: ek iş yeni commit'tir (r2 = ayrı SHA, "r1 + r2 birlikte alınır"). WIP commit'ler main'e alınmaz; iş bitince yeni ve temiz bir commit yapılır. [m:teslim-commit-amend-yasagi 17.09; OA §0.5; DEV SON DURUM EKİ]

3.6 Dosyalar açık listeyle eklenir (zsh'de `git add $F` sözcüklere bölünmez → `--pathspec-from-file`). Commit öncesi dosya listesi `git show --stat` ile doğrulanır. [PSP §9.2; m:wp-076-durumu ders; m:wp-043-durumu DERS]

3.7 Commit'ler küçük, atomik, paket kimliğiyle izlenebilir ve geri alınabilir olur; her dilim ayrı commit'tir. Derleme ve ilgili testler geçmeden entegrasyona alınmaz. [PSP §3.5.46; M4R1 §4.3]

3.8 **Kapı 8:** konu `^(WP|AP)-[0-9]{3}[^:]*: .+ \(C-00 [0-9]{2}\.[0-9]{2}\)$` biçimine uyar:
- WP/AP numarasıyla başlar; numara ve etiketten sonra iki nokta şarttır.
- Tam olarak "(C-00 gg.aa)" ile biter; parantezin içine başka bir şey yazılmaz, ek bilgi gövdeye gider.
- Gövdede satır başında `Test:`, `Tests:` ya da `Guarded:` satırı bulunur; sahip satırı ayrı satırdadır. Yalnız belge değişen commit'te "Test: docs only" ya da "Test: yok (yalnız belge)" yazılır.
- Codex kod görevlerinde konu `AP-0NN <kısa>: <özet> (C-00 gg.aa)` biçimindedir.

[m:kural-commit-konu-kapisi 30.09; m:commit-bicimi-kapi8 05.10; m:teslim-commit-amend-yasagi; OA §0.5; BRF §10; CXS §5]

3.9 Commit mesajı `echo` ile kurulmaz (zsh `\n`'i satır sonuna çevirir); Python ya da `printf '%s'` ile yazılır ve `git log -1 --format=%s | cat -v` ile denetlenir. Teslim öncesi `for c in $(git rev-list base..HEAD)` ile kapı 8 denetimi yapılır. Kapıdan dönen commit amend edilmez: yeni commit yapılır, worktree meşgulse `git commit-tree <tree> -p <parent> -F msg` ile aynı ağaçtan yeni dal kurulur ve uç ağacının eşitliği doğrulanır. [m:commit-bicimi-kapi8 05.10; m:kural-commit-konu-kapisi 30.09]

3.10 Commit gövdesi İngilizcedir ve test satırını ("Guarded: EditMode x/x, PlayMode y/y, scope editor, no Player"), sahip/devir bilgisini ve DEC/REQ/ADR atıflarını içerir. Trailer'lar korunur (integrate `commit -C` kullanır). [PSP §9.5, §11, §17]

3.11 Teslim SHA'sı yalnız `git rev-parse` çıktısından kopyalanır; commit ile teslim mesajı aynı turda paralel atılmaz. [m:ders-sha-rev-parse-sonra 05.10; OA §0.4; BRF §10]

3.12 Teslimden önce `python3 "Repo/tools/qa/r2_gates_lint.py" --repo <worktree>` koşulur: ERROR 0 olmalı, WARN listesi teslim notuna yazılır. [OA §0.4]

3.13 main'e merge/cherry-pick, etiketleme, push ve `cleanup-merged.sh --apply` yalnız C-00'ın işidir; oturumlar main'e yazmaz ve push etmez. [PSP §3.5.48; OA §0.4; M4R1 §4.3] (cleanup için §12 Ç7)

3.14 **integrate.sh (yalnız C-00):** `tools/integrate.sh <sha> --tag <etiket> [--s01] [--allow-paths a,b] [--no-post] --apply` (varsayılan DRY-RUN).
- Kapılar: tek ebeveyn · kaynak worktree temiz · yol kapsamı (`Assets/_Prototype|_Art|TextMesh Pro`, proje README/PROTOTYPE-ONLY, `planning/`, `tools/`, `wp-*/README*`; ProjectSettings ve Packages yalnız `--allow-paths` ile; `ProjectIntegrityTests.cs` yalnız `--s01` ile; `docs/c00` için `--allow-paths docs/c00`) · eklenen satırlarda secret taraması · DEC 1..N ardışık, REQ tekil · etiket boş değil ve `/` içermiyor · main temiz · LFS nesne ≥ pointer · kapı 8.
- Çıkış kodları: 0 · 3 kapı · 4 çakışma (geri alındı) · 5 post · 6 derlemiyor (`--compile`) · 64.
- Hiçbir zaman reset, rebase, push, `add -A` ya da ref silme yapmaz.

[TR (integrate.sh); PSP §9; DEV §2; BRF §10; m:kural-commit-konu-kapisi]

3.15 integrate.sh yalnız verilen commit'i cherry-pick eder. Çok commit'li dalda her commit sırayla verilir; sonunda `git cherry main <tip>` çıktısındaki "+" sayısı 0 olmalıdır. Worktree'den koşulursa `--repo "/Users/Erdem/Documents/Deaths Demolition"` verilir (05.10'dan beri varsayılan zaten ana worktree). [BRF §10; DEV §2; m:ders-cherry-pick-entegrasyon-kontrolu 05.10; m:ders-player-yakalama-ve-olcum 05.10; m:wp-079g-durumu 05.10]

3.16 Tabanı main'den ≥ 3 paket geride olan dal cherry-pick edilmez. Sahip, güncel main üstünde kendi worktree'nde cherry-pick yaparak r2 teslim eder; sonra ff-only alınır. [TR (integrate.sh); PSP §9]

3.17 Etiketler açıklamalı `wp-*` biçimindedir, dal adından farklıdır ("refname is ambiguous" hatası), silinmez ve yalnız C-00 tarafından konur. [PSP §9; M4R1 §4.3; m:wp-079g-durumu 01.10]

3.18 Push yalnız C-00'ın işidir. LFS'li teslimde önce `git lfs push origin main`, sonra `GIT_LFS_SKIP_PUSH=1 git push origin main refs/tags/<etiket>`. Binary'ler Git LFS'te; ham üretim repo dışında tutulur. [BRF §10; DEV §2; m:m4-r1-revizyon-turu 24.09; PSP §3.3.36]

3.19 `GIT_LFS_SKIP_SMUDGE` ile dal değiştirildikten sonra `git lfs ls-files | grep " - "` boş olmalıdır; değilse `git lfs checkout <yollar>` koşulur (pointer kalan dosyalar testi ve derlemeyi bozar). [m:wp-085-durumu ders; OA S-08 mesajı]

3.20 **Sürücü taşıma:** guard ya da sürücü dosyasına dokunan bir commit main'e girince `git -C K/wp-079/driver checkout -q --detach main` yapılır, ardından `cd K/wp-079/driver && python3 tools/guard_freshness.py K/wp-079/driver/tools/userdata-guard.sh` 0 dönmelidir. Yapılmazsa bütün guard'lı koşular 64'te durur. Böyle bir teslim notunda "SÜRÜCÜ DOKUNUŞU VAR" yazılır. [BRF §10; DEV §2; m:wp-079g-durumu 05.10; OA S-01 mesajı]

3.21 "Dal main'de mi?" sorusu `git cherry main <dal>` ile yanıtlanır (satır başında "-" = karşılığı main'de). C-00 cherry-pick ettiği için `merge-base --is-ancestor` yanlışlıkla "değil" der; bu komut yalnız ff-uygunluğunu sınamak için kullanılır. [m:ders-cherry-pick-entegrasyon-kontrolu 01.10; m:wp-078-r1-kabul ders]

3.22 Toplu entegrasyondan sonra `tools/compile-check.sh [ref]` koşulur (yeşil = Unity exit 0 VE derleme izi), ardından `tools/main-ci.sh <sha>`. Derlenmeyen main'den build ya da CI koşulmaz ve kullanıcıya build verilmez. Bir şeyi kaldıran commit'lerde repo genelinde referans grep'i yapılır. [PSP §7.7, §9.6; m:ders-parti-derleme-kapisi 25.09; m:profesyonellik-standardi 25.09 (5)]

3.23 Kırmızıyı `ci-owners.tsv`'deki sahip düzeltir; commit gövdesi kaynağını yazar ("main-ci <sha> red …"), konu yine kapı 8 biçimindedir. [PSP §9.7; TR (main-ci)]

3.24 r2 cherry-pick'inden sonra, sahibi olmadığın dosyalarda `git diff main` boş olmalıdır. Zaten entegre edilmiş bir commit'in r2'si, main üstünde yalnız farkı içerir. [EG 17.09 KURAL; m:wp-019-durumu ders]

3.25 cherry-pick'te `-X ours` kullanılmaz; çakışma elle ve hunk bazında çözülür. `git stash` kullanılmaz (index'i düşürür; stash listesi worktree'ler arasında ortaktır). [m:wp-024-durumu ders; m:wp-034-durumu ders; m:wp-071-durumu ders; m:wp-038-durumu ders; m:wp-074-075-durumu ders; OA §0.13]

3.26 Commit anında main'e yeniden bakılır; teslim notu ile cherry-pick arasında main ilerler. [m:wp-034-durumu ders; m:wp-049-durumu ders]

3.27 Bir kapının düzeltmesi, main'in ESKİ kapısından da geçmelidir (test dizgileri çalışma zamanında birleştirilir). [EG 05.10; m:wp-079g-durumu 05.10]

3.28 Packages değişikliği `ProjectIntegrityTests` pinini aynı commit'te günceller. Unity/URP sürümü değiştirmek ve paket eklemek/yükseltmek ayrı bir kritik karardır; manifest ve lock hash'leri sabittir. [EG 05.10 DERS; TR (integrity); PSP §3.3.28] (bkz. §12 Ç20)

3.29 Her entegrasyondan sonra `post-integrate.sh` koşulur: iki geçişli bundle + LFS yedeği, cleanup dry-run, disk raporu, footprint Δ. [PSP §9.5; m:main-entegrasyon-durumu 17.09; TR (post-integrate)]

3.30 integrate.sh sonrasında `git log -1` ile main'in gerçekten ilerlediği doğrulanır; iCloud dataless araçlar boş okuyup rc=0 verebilir. [m:ders-icloud-dataless 01.10]

3.31 **Bulut ve C-00B:** C# değişiklikleri yerel derleme ve test olmadan main'e girmez (dal + PR'da bekler). Belge ve Python-testli araç değişiklikleri doğrudan entegre edilebilir. Unity, Blender, Player, KADRO ve harici disk işleri `docs/c00/LOCAL-QUEUE.md`'ye yazılır. [DEV §11; BRF §2; EG 05.10]

3.32 **Planlama belgeleri:**
- Her paketin promptu `planning/work-packages/WP-NNN-*.md` olarak birebir kaydedilir; kullanıcı kararları planning'e birebir yazılır.
- DEC numarasını yalnız C-00 verir, yazma anında (rezervasyon yok); başkasının satırı girmeden yazılmaz.
- DEC 1..N ve REQ ardışıklığı integrate ve `ProjectIntegrityTests` ile denetlenir.
- Yeni karar eskisinin yerine geçerse eski karar belgelerden çıkarılır (tarihçe notu kalabilir).

[PSP §3.4.41, §8; m:m4-r1-revizyon-turu 24.09; m:wp-077-durumu ders] (R2 promptlarının yeri için §12 Ç33)

3.33 **Release:** `v1.0-rc.N` etiketini yalnız C-00 koyar. Ölçüt: main temiz + `rc.sh` PASS + ayak izi ≤ tavan (5 GB) + lisans paketi. Onaylı RC aynı commit'e `v1.0` olur. [PSP §9, §3.3.33]

---

## 4. Makine

4.1 **Slotlar:** Unity, Blender, Player ve smoke işleri yalnız `slot.sh acquire|release <tür> <etiket>` altında yapılır (FIFO, en eski canlı bilet önce).
- Unity batch ≤ 2 (swap < 10 GB ve load < 24 iken 3), ağır Blender CLI 1, smoke 1, Player 1.
- Kuyruk `slot.sh status` ile görülür.
- Oturumlar KADRO kopyasını (`K/tools/slot.sh`) ya da dd-env'deki `$DD_SLOT`'u çalıştırır.
- `test.sh` ve `build.sh` Unity slotunu kendileri alır; onları çağırırken slot ayrıca alınmaz.

[OA §0.7; TR (slot.sh); D8 §4; m:makine-slot-kurali 17.09]

4.2 Kilitler sahibin pid'ini ve başlangıç zamanını taşır, çağıran kabuğa bağlıdır. Başkasının kilidi asla silinmez; bilinmeyen lease devralınmaz, bayat lease'i C-00 temizler. `release` etiketle eşleşen bütün kilitleri sildiği için paralel ajanlara farklı etiketler verilir. [TR (slot.sh); PSP §10; m:makine-slot-kurali 16.09]

4.3 **Blender:** `cd "Repo" && tools/slot.sh acquire blender <etiket> && <blender -b … --threads 4>; tools/slot.sh release blender <etiket>`.
- acquire, Blender ve release AYNI kabuk komutundadır; ayrı komutta alınan kira hemen düşer.
- Hafif Blender, Higgsfield, Adobe ya da PIL işleri slot gerektirmez.
- Uzun Blender işleri parçalanır; slot gereksiz tutulmaz.

[DEV §6.2; m:ders-blender-slot-ayni-kabuk 30.09; m:makine-slot-kurali 16.09; CXS §6] (thread sayısı için §12 Ç16)

4.4 **Disk/swap kapısı:**
- Yeni bir Unity koşusu (EditMode, PlayMode, build, Player) yalnız boş disk ≥ 15 GB ve swap ≤ 12 GB iken başlar.
- Her koşudan önce `df -k /` ve `sysctl vm.swapusage` kendin kontrol edilir. Eşik altındaysa koşulmaz, "NOT RUN: free disk" yazılır ve C-00'a bildirilir.
- Koşan iş yarıda kesilmez.
- slot.sh, unity/player acquire'ında `dd_disk_gate`'i otomatik uygular (`DD_SLOT_DISK_WAIT` 3600 s → 75).
- Swap > 12 GB iken kod Unity'siz yazılır, doğrulama sonra yapılır.

[m:kural-disk-kapisi 30.09; OA §0.7; PSP §10; TR (slot.sh, C-00 01.10); m:wp-074-075-durumu ders]

4.5 **Kilit sırası player → unity:**
- Player ve kabul pencerelerinde önce guard snapshot (player slotu) alınır, sonra Unity.
- Unity slotu tutarken player slotu asla beklenmez.
- Build, player oturumundan önce ve ayrı bir adımda yapılır; player oturumu içinde Unity batch başlatılmaz.
- Smoke slotu guard snapshot'ından SONRA alınır.
- D9'dan sonra bütün kapsamlarda sıra guard → unity olur (§2.22).

[m:kural-kilit-sirasi 30.09; m:kural-kilit-sirasi-player-unity 30.09; m:kural-userdata-guard 01.10; BRF §10; OA §0.7]

4.6 Başka oturumun süreci öldürülmez, kilidi silinmez, dosyasına ve worktree'sine dokunulmaz. `pkill -f "Playable Concept"`, `killall Unity` gibi geniş desenler yasaktır; yalnız kendi başlattığın pid (`$!`, slot bileti) sonlandırılır. Emin değilsen C-00'a sor. [PSP §3.4.44, §10; m:kural-baska-oturum-player-kapatma 24.09; M4R1 §4.13, §4.14e; OA §0.8]

4.7 `pgrep -f` deseni kendi kabuğunu eşleyebilir. Player denetimi `pgrep -f "MacOS/Deaths Demolition"` ya da comm ile yapılır. KADRO betiğini düzenlemeden önce "koşuyor mu" kontrolü ayrı bir komutta ve kendini eşlemeyen bir desenle yapılır; izleme pid ile yapılır. [m:kural-player-run 25.09; m:wp-071-durumu DERS; m:wp-079g-durumu 01.10]

4.8 Bekleyen ya da slot bekleyen bir compile-check durdurulmaz; lease slottan önce alındığı için bayat kira kalır. [OA §0.8; BRF §10; EG 05.10]

4.9 **CI lease:** compile-check ve main-ci aynı kanonik CI checkout'unu owner lease ile sırayla kullanır.
- Meşgulse 75 döner → beklenir; HELD-LIVE lease'e dokunulmaz.
- 3 dönerse (STALE / MALFORMED / SYMLINK) `compile-check.sh --lease-status` çıktısı C-00'a iletilir.
- Yalnız C-00, checkout'u açan Unity olmadığını doğruladıktan sonra lease'i `mv <lease> <lease>.quarantine-c00-<utc>` ile yeniden adlandırır (silmez) ve günlüğe yazar.

[TR (CI lease quarantine); PSP §10; BRF §10; DEV §2]

4.10 `dotnet build-server shutdown` kullanılmaz; diğer oturumların derleyen Roslyn sunucularını da öldürür. Yetim VBCSCompiler süreçleri ~10 dk sonra kendiliğinden kapanır. [m:unity-batch-dersleri 25.09]

4.11 Koşan bir betik düzenlenmez (zsh/bash betiği akış hâlinde okur → bozuk komut). Değişiklik yeni dosya + `mv` ile (atomik) yapılır ve yeni etiketle yeni koşu başlatılır. Unity batch koşarken worktree'de hiçbir dosya düzenlenmez; kuyrukta koşusu olan worktree'ye derlenmemiş dosya konmaz. [m:unity-batch-dersleri 17.09/30.09; m:makine-slot-kurali 17.09; m:wp-079g-durumu 01.10]

4.12 Proje/worktree başına tek Unity örneği çalışır. Aynı worktree'de ikinci zincir başlatılmaz (exit 134 ya da seconds=0–1 = sahte koşu); önce önceki zincirin `DONE` işareti beklenir. [m:unity-batch-dersleri 17.09; m:wp-023-durumu ders; m:wp-005-durumu ders]

4.13 **Zincir iptali:** önce ana betik (pid'i `pgrep -fl run-all.sh` ile bulunur), sonra `slot.sh acquire` çocuğu durdurulur. Runner'da `acquire … || exit` kullanılır. Öksüz kalan Unity de (kendi pid'in) durdurulur ve yarım yazılan sahne/materyal geri alınır. [m:unity-batch-dersleri 17.09; m:wp-074-075-durumu ders]

4.14 Unity adımları stall bekçisi altında koşar: `dd_unity_watched` (log `DD_STALL_MIN` = 20 dk büyümezse yalnız o pid ve çocukları durdurulur, bir kez yeniden denenir, sonra 124). Özel koşuculara da bekçi konur. Arka plan işi saatlerce kontrolsüz bırakılmaz; uzun zincirler tek bir arka plan komutuna sığdırılmaz. [TR (dd_unity_watched); m:unity-batch-dersleri 25.09; m:wp-069-durumu ders; m:wp-078-r1-kabul ders]

4.15 Player'lar ses aygıtını ve pencere odağını PlayMode testleriyle paylaşır. Bu yüzden main-ci koşarken Player başlatılmaz, Player ölçümü ile PlayMode yakalama aynı anda koşmaz ve Player koşturan her pakette soak/perf penceresine bakılır. [m:kural-player-run 25.09; m:wp-072-durumu ders; EG 23.09 KURAL]

4.16 **iCloud dataless:** entegrasyon, CI ya da Unity öncesinde `find "<repo>/tools" .git -flags +dataless | head` ile bakılır; dataless dosya varsa önce materyalize edilir, bitmeden git/integrate/Unity çalıştırılmaz. Repo dosyaları gerektiğinde `git show main:<yol>` ile okunur. Dataless dosya okuyan `git status`/`fsck` asılı kalabilir; zaman aşımı koy, lsof ile teşhis et. [m:ders-icloud-dataless 01.10; AGM; CXS §5–6; m:wp-079g-durumu 05.10]

4.17 Sessiz pencerede ve FINAL sırasında büyük `cp -c -R`, toplu `cat` ile materyalize ya da büyük kopya yapılmaz (fileproviderd yükü 15–50'ye çıkar). Büyük KADRO grep'i iCloud indirmesini tetikler; hedefli klasörde aranır. [m:ders-icloud-dataless 01.10; CXS §6; m:wp-068-durumu ders]

4.18 iCloud'un kalıcı çözümü kullanıcıdadır ("Mac Depolamasını Optimize Et" kapalı, disk ≥ 25 GB). Taşıma A (repo `~/Dev/Deaths Demolition`, KADRO `~/Dev/KADRO`, eski yollarda symlink, `DD_KADRO` tek kaynak) üretim dalgasından sonraki pencerede yapılır. Claude oturumları ESKİ yoldan (symlink) açılır, çünkü bellek klasörü yol adına bağlıdır. [m:ders-icloud-dataless 01.10; m:wp-079g-durumu 05.10; BRF §13; TR (DD_KADRO)]

4.19 Belge/araç commit'i için KADRO'ya tam LFS'li repo worktree'si açılmaz; `--no-checkout` + sparse + `GIT_LFS_SKIP_SMUDGE=1` ile bir scratch worktree kullanılır. [m:codex-r2-kurulum 05.10] (bkz. §12 Ç25)

4.20 **APFS:** Library klasörleri `cp -cR` klonudur; `du` yanıltır. Taşıma/silme adayı seçmeden önce `python3 tools/apfs_real_gain.py <yol…>` ile gerçek kazanç ölçülür (cleanup-merged ve disk-report `realGainMB` verir); Library taşımak yer açmaz. Yeni worktree'nin Library'si boştaki komşu worktree'den klonlanır (`cp -cR` ya da rsync; Bee/ScriptAssemblies hariç). [m:ders-apfs-klon-gercek-kazanc 05.10; BRF §10; TR (cleanup-merged); m:unity-batch-dersleri 23.09; m:wp-038-durumu ders]

4.21 Slot beklerken boş durulmaz: sıradaki Unity'siz iş (kod/belge) sürer. Makine paylaşımlıdır; slot beklemesi normaldir. [m:hiz-modu-2026-09-17; m:hizli-hat-direktifi 16.09; OA §0.7]

4.22 Codex ve üretim tarafı da disk < 15 GB ya da swap > 12 GB iken Unity/Blender başlatmaz; büyük çıktı üretmeden önce `df -h /`'e bakar. [CXS §6; BRF §10]

---

## 5. Kanıt ve ölçüm

5.1 Hazır olmayan hazır denmez. Kanıtsız PASS yoktur; AD/test kanıtı olmadan "hazır" denmez; açıklar dürüstçe yazılır. [BRF §3; DEV §1; OA §0.9; M4R1 §4.14d]

5.2 **Ölçüm önce, bulgu sonra:**
- Karar değiştiren bir bulgu C-00'a gitmeden sayısal kontrolle doğrulanır ve sayı mesaja yazılır; görsel izlenim (contact sheet) yetmez.
- Karar istenen bir maddenin tanımı önce kaynağından okunur.
- Çalışma zamanı değeri tahmin edilmez; o değeri yazan kod grep'lenir.
- "Fiziksel engel" demeden önce ilgili kod okunur; önce izleme eklenir, sonra düzeltilir.

[m:olcum-once-bulgu-sonra 25.09/05.10; OA §0.9; m:wp-074-075-durumu ders; m:wp-078-r1-kabul ders; m:wp-055-durumu ders]

5.3 **Kaynak kayıtta var:** ölçen test, ölçtüğü şeyin kayıtta olduğunu assert etmeden artefakt yazmaz (ses için: atış penceresi ≥ −30 dBFS ve ön pencereden ≥ 6 dB yüksek). Kaynağı kayıtta olmayan bir ölçüm de sayı üretir. [OA §0.9; DEV §3; m:ders-olcum-kaynak-kayitta-var 05.10; m:wp-091-durumu]

5.4 **Varsayılan yol kanıtı:** sevkiyat, görsel ve oynanış kanıtı oyuncunun kullandığı bayraksız yoldan alınır. Sanat, ışık ve oynanış teslimlerinde en az bir kare bayraksız Player'dan alınır. Release öncesi "varsayılan dünya/ayar hangisi" kontrol edilir. Dev build kareleri filigranlıdır. [PSP §9; m:ders-varsayilan-yol-kaniti 25.09; m:wp-078-r1-kabul 01.10]

5.5 Kit ve ölçüm silahı oyunun gerçek RPM'iyle (`WD_*.asset`) ateşlenir. Kullanıcı, oyunda duymadığı bir ses üzerinden karar vermeye yönlendirilmez. [m:ders-kit-gercek-rpm 01.10]

5.6 BLOCKED, SKIPPED ve 0 test PASS değildir. Durumlar yalnız `PASS | FAIL | BLOCKED | NOT_REQUIRED_FOR_<paket>` olarak yazılır; ölçülmeyen şey "ölçülmedi" diye yazılır. [PSP §8, §11]

5.7 "Hata sayısı 0" başarı demek değildir; aracın işi gerçekten yaptığının pozitif izi istenir (ör. compile-check: exit 0 + derleme izi; "Multiple Unity instances" = 75). [m:wp-043-durumu DERS; TR (compile-check)]

5.8 Kirli ölçüm "KİRLİ" damgası taşır: yalnız sıralama bilgisidir, kapı değildir; temiz ölçüm S-10'a devredilir. [PSP §7 (HIZ MODU), §10]

5.9 **Perf yalnız sessiz pencerede ölçülür:**
- Koşul: load1 < 6, yabancı Unity batch/Player/Blender 0 (`pgrep`); yük 5 s'de bir loglanır; varyantlar serpiştirilir (ABAB).
- `wp078_perf.py` player slotunu ve 3 Unity slotunu holder kabuklarla tutar; partiler ≤ 25 dk, aralarında 180 s.
- Sessiz pencere C-00'dan istenir. Pencere ancak C-00'ın açık "kapandı" mesajıyla ya da saat sınırıyla biter.
- Perf penceresiyle çakışan bir Player koşusu kabul edilmez.

[PSP §10; m:perf-olcumu-sessiz-makine 25.09; m:wp-078-r1-kabul 26.09/30.09; OA S-08/S-09 mesajları; m:wp-066-durumu ders]

5.10 Makine yük altındayken ms yerine yükten bağımsız sayaçlar (SetPass/batches/draws) raporlanır. [m:ders-player-yakalama-ve-olcum 05.10]

5.11 **Kanıt standardı:** commit/manifest, komut, Unity yolu, OS, GPU, exit code, sayılar, XML/log yolu. Kanıt `K/wp-NNN/evidence/` altında durur. [PSP §11]

5.12 **Görsel kanıt:** üç kamera modu (tepeden + TP 3 m + FP); 1080p + 21:9 + Mac 3024×1964 + TextScale 1,25 + renk körü; hedef görselle yan yana. [PSP §11]

5.13 "Kırmızı benden değil" iddiası main-ci rapor satırı ister. Ayrıca kırmızının işaret ettiği öğe `git log -S` ya da `git blame` ile izlenir; doğru ifade "bu dal getirmedi; kaynak X commit'i"dir. Main kırmızısı `ci/<sha>/summary.md`'den doğrulanır. [M4R1 §4.14d; m:ders-main-kirmizi-benden-degil-degil 25.09; m:wp-077-durumu ders]

5.14 "Tek başına" yalnız tek testlik filtreyle koşulmuş sonuca denir. CI kırmızısında önce testin kendi logundaki fiziksel izlere bakılır, sonra CI sırasıyla (önceki sınıflar + hedef test) yeniden üretilir. Yalnız tam süitte düşen testte hipotez yerine ölçüm yapılır (ITestRunCallback ile durum logu, fikstür kümelerini yarılama). [m:ders-test-secim-sizintisi 05.10; m:static-bus-rearm-dersi; m:unity-batch-dersleri 24.09; m:wp-072-durumu ders]

5.15 **Yakalama geçerliliği:** sahne ve pane durumu doğrulanır; yakalama süresince `BaseInputModule`'ler kapalıdır, menü değişirse `*-INVALID.txt` yazılır; JSON parse edilir; mürekkepli kelime çizildiği gibi ölçülür. [m:ders-player-yakalama-ve-olcum 05.10]

5.16 Bir AD bulgusu, kanıt karesinin build SHA'sı main ile karşılaştırılmadan iş açmaz. AD'nin kök neden iddiaları ve görüntüden derinlik tahminleri ölçümle doğrulanır. [EG 25.09 ders; m:wp-019-durumu ders; m:wp-068-s6-menu-plates ders]

5.17 Ölçüm aracı kapı değildir: `[Explicit]` ölçüm testleri CI dışındadır; bilinen açıklar "KNOWN OPEN reported, not asserted" olarak raporlanır. [PSP §11]

5.18 Oynak metriklerde tek örnekle build karşılaştırması yapılmaz. Deterministik olmayan ekranlar (HUD dünyası, zar, yaprak, yağmur) gürültüdür; düzen kanıtı deterministik ekranlardan alınır. [m:wp-038-durumu ders; m:wp-068-durumu ders]

5.19 Ayak izi (build boyutu) taze Library ile ölçülür. Kapı değeri BuildReport `totalSize`'dır (DEC-093 5 GB = 5 000 000 000 bayt). [m:wp-038-durumu ders; TR (build-report.sh)]

5.20 Kullanıcıya build, derleme ve CI durumu bilinerek verilir; bilinen kırmızılar açıkça söylenir. [m:profesyonellik-standardi 25.09]

---

## 6. Geliştirme prensipleri

### 6A · Paket yaşam döngüsü

6.1 Kullanıcı notu ya da onaylı karar → C-00 paket promptu → birebir kayıt. R2'de promptlar `K/c-00/R2/prompts/WP-08x.md` ve `docs/c00/prompts/` altındadır. [PSP §7.1; DEV §10] (yer çelişkisi: §12 Ç33)

6.2 **Standart paket içeriği:** kimlik ve konu oturumu · amaç ve kullanıcıya görünen sonuç · onaylı kararlar ve gereksinimler · kapsam / kapsam dışı · bağımlılıklar ve korunacak arayüzler · uzmanlıklar ve entegrasyon sahibi · teslimatlar · kabul kriterleri · otomatik ve manuel testler · kritik durma koşulu · standart rapor. Sayısal değer ve ürün kararı icat edilmez; bekleyen kararlar "icat edilmeyecek" listesine yazılır. [PSP §7; m:prompt-uretimi-c00 14.09]

6.3 **Faz A (tasarım):** ilgili uzman konseyi (lider + eş imza), sahip oturumun motor kararlarıyla birlikte `WP-NNN-*-DIRECTION.md` yazar: kusur analizi, kimlik/API, tüketici haritası, kabul ölçütleri, 🔴 listesi. "§0 S-xx kararları" çelişkide bağlayıcıdır; konsey kararları yeniden tartışılmaz. 06.10 itibarıyla yön/API kararı = konsey kısa karar notu. [PSP §7.2; OA §0.14; m:wp-068-durumu 24.09]

6.4 **İLK TESLİM** (kimlik sayfası, API taslağı) tüketici oturumlara mesajla, C-00'a tek satırla bildirilir; tüketiciler beklemeden başlar. [PSP §6, §7.3]

6.5 **Faz B:** iş dilim dilim ilerler (B1…Bn); her dilim ayrı commit + kanıttır; 🔴 kararlar gelene kadar önerilen varsayılan kullanılır. Dilim kapısının sırası: değişen süitler guard altında yeşil → kanıt (kare/GIF/ölçüm; üç kamera) → gerekiyorsa AD turu / ses ölçüm kapısı → gt → MUST'lar kapanır, SHOULD'lar devredilir. [PSP §7.4–7.5; OA §0.14]

6.6 **Sonrası:** teslim → C-00 integrate → compile-check → main-ci → gerekirse release + bayraksız smoke → S-10 bağımsız kabul (tur sonu FINAL) → kullanıcı oynar → sonraki tur. [PSP §7.6–7.9; OA S-10 mesajı]

6.7 **Kullanım limiti ya da durma:** tutarlı bir noktada WIP commit + `DEVIR-<gg.aa>.md` notu bırakılır. Devralan önce `git log -3` ve devir notunu okur. Commit'lenmemiş iş kaybedilmez, silinmez, sıfırlanmaz; önce `git status --short` özeti C-00'a gönderilir. [DEV SON DURUM EKİ; BRF §7; OA S-03/S-05 mesajları]

6.8 **Varsayma, uydurma:** kararlaştırılmamış bir ayrıntı varsayım olarak yazılmaz. Onaysız her sayı `[PrototypeOnly(...)]` taşır. Sayılar konsey önerisi → C-00 onayıyla DEC olur; ürün kuralını değiştiren sayı ya da kural kullanıcı kararıdır (05.10 tam yetkiyle ilişkisi: §12 Ç20). [PSP §0.4, §3.4.42, §11]

6.9 **Kalite:** "hiçbir şeyi atlamadan, kaliteden ödün vermeden". Perf aşımında önce ölçülür, raporlanır, kademeye bağlanır; görsel kısma kullanıcı/C-00 kararı olmadan yapılmaz. 5 GB'a kadar doku/mesh kalitesi boyut için kısılmaz. [PSP §3.3.33, §3.4.43; M4R1 §6]

6.10 Yeni ağır görsel özellikler kademeli yazılır (Sinematik/Yüksek/Performans) ve kademe başına ms maliyetini raporlar. [M4R1 §4.7]

6.11 Prototip terk edilmez ve topluca yeniden yazılmaz; çalışan modüller testlerle korunarak paket paket dönüştürülür. [PSP §3.3.29]

### 6B · Sahiplik ve el sıkışma

6.12 Bağımsız işler paralel yürür; aynı dosya ya da sisteme dokunan işler tek bir entegrasyon sahibi altında sırayla yapılır. Her paketin tek yazarı vardır; diğer oturumlar salt okunur danışmandır. [PSP §3.4.38, §6]

6.13 **El sıkışma:** başka bir sahibin modülüne yalnız tek ya da birkaç satır dokunulur; sahibi onaylar ve değişiklik aynı commit'te bildirilir. Daha büyük ihtiyaç "Devir" olarak sahibine gider (`→ S-xx: …`). [PSP §6]

6.14 **Olay tüketim sırası:** S-06 durum → S-02 sayım → S-03 karar → S-07/S-08 kozmetik. Aynı kanala iki kaynak yazmaz. [PSP §6]

6.15 Dünya JSON'larını (spawn-gates, regions, acoustic-spaces) S-05 üretir, S-03 ve S-08 tüketir; sıralama notlarına uyulur. Akustik harita her düzen değişiminde S-08 tarafından yeniden bake edilir. [PSP §6]

6.16 Her konunun tek bir sahibi vardır: tek tier otoritesi, tek ceset ömrü sahibi (S-03), global render ayarlarının tek yazarı (S-05 kalite modülü), kamera-yerel ayarlar (S-01), kullanıcı verisinin tek yazarı. [PSP §11]

6.17 Aynı varlık iki oturuma verildiyse önce C-00'a "kim import ediyor" diye sorulur. Üretim (P-xx/Codex) teslimlerini sahip S-oturumu dilim dilim, manifest + doğrulayıcı kapısıyla içe alır. [m:wp-059c-durumu ders; PSP §6]

6.18 Sahneler builder'larla kod içinden yeniden üretilir (`GreyboxV6Builder`, `WorldV5Dress`); sahneye elle yazılmaz ve iki oturum aynı sahne dosyasına dokunmaz. Paylaşılan bir asset'e yalnız kendi alanların yazılır (Unity SaveAssets bilinmeyen alanları varsayılanla serileştirir → main'in asset metni + yalnız kendi alanların). [PSP §11; m:wp-050-durumu ders]

6.19 Başka bir oturuma yazılan "açık" notunda ekran/eleman ve beklenen değer net yazılır. [m:wp-077-durumu ders]

### 6C · Kod kuralları

6.20 **Veri önce kod:** her ayar `PrototypeConfig` bölümü, ScriptableObject ya da JSON katalog olarak tutulur. Onaysız sayı `[PrototypeOnly(...)]` taşır (Inspector + F1 panelinde görünür; `PROTOTYPE-ONLY.md` sayımına girer). [PSP §11]

6.21 Olay veriyolları struct payload kullanır (`DamageEvents`, `RunEvents`, …); üreticiler tüketicileri bilmez. Assembly bağı tek yönlüdür: Core → Runtime → Art.Runtime → Qa. Yön kilitleri korunur (ör. `Spawn/` dosyaları `Enemies`'e dayanmaz). [PSP §11; m:wp-076-durumu 25.09 kural]

6.22 **Sıcak yolda 0 ayırma:**
- string/format/LINQ/closure/boxing/`params` yok (3 argümanlı `Mathf.Min/Max` = params dizisi); önbellek ve havuz kullanılır.
- Havuzlar kapasiteye kadar ısıtılır ve bir kez Get/Release edilir.
- Kare başına çalışan gövdelerde `OnControllerColliderHit` yasaktır (`CollisionFlags.Sides` + `Physics.CheckCapsule`).
- Paylaşılan multicast event'e `-=`/`+=` spawn'da yapılır, ölüm yolunda değil.

[PSP §11; m:unity-animator-culling-dersleri; m:unity-batch-dersleri 17.09; m:wp-071-durumu ders]

6.23 Statik durum `SubsystemRegistration`'da sıfırlanır (domain reload kapalıdır). Süreç boyu yaşayan (DDOL) dinleyiciler her sahne değişiminde yeniden abone olur (remove-then-add, `Rearm` kalıbı). [PSP §11; m:static-bus-rearm-dersi]

6.24 Kademe değeri yalnız `QualityTiers.Pick(p, h, c)` ile okunur; çağrı yerinde sabit yazılmaz. WP-062 sonrası kamera/nişan/sarsıntı sayılarının tek kaynağı FeelConfig/FeelRuntime'dır. [PSP §11; m:wp-062k-durumu ders]

6.25 Hit-stop `Time.timeScale`'e dokunmaz. Recoil nişanı oynatır (`AddRecoil`); bob, shake ve FOV punch kozmetiktir. [PSP §11]

6.26 UI `UnityEngine.InputSystem` okumaz. Cihaz/şema bilgisi `InputSourceRegistry.Current.ActiveScheme()` ile alınır (ADR-024-2, `InputParityContractTests`); yeni bir ihtiyaç doğarsa S-02'den API istenir. [PSP §11; m:kural-ui-input-system-yok 17.09]

6.27 **UI kit kuralları:**
- uGUI `Shadow/Outline/Mask/LayoutGroup/ContentSizeFitter` kullanılmaz; gizleme `Canvas.enabled` ile yapılır.
- Koşu içinde toast yok.
- Reduced motion, reduce flashes, TextScale 1,25 ve renk körü her öğede zorunludur.
- UI sıfır ayırmayla çalışır.
- Yarı saydam her token/katmanın kontrastı Linear renk uzayında `UiContrast` ile ölçülür.

[PSP §11; m:wp-068-ui-v3; m:ui-dogrusal-harmanlama]

6.28 İçerik paketi `planning/content/r1-strings.json` ile `…/UI/Resources/Strings/r1-strings.json` bayt-eşit kalır. `en` kaynak, `tr` paralel; Türkçe büyük harf için `UiStrings.Upper`. [PSP §11]

6.29 **Olay kapsam matrisi:** her olayın kendi sesi ve efekti vardır; boş ses satırı ya da VFX hücresi = DoD FAIL. Yeni olay için önce matris satırı, sonra dinleyici yazılır. Yeni bir `CharacterAnimationEvent` eklenirse `AudioEventMap` satırı (sessiz de olsa) aynı commit'te eklenir ya da S-08'e önceden haber verilir. [PSP §3.2.24, §11; m:ders-yeni-klip-olayi-ses-haritasi 05.10]

6.30 **Varlık hattı:** sürümlü Import Contract; manifest + sha doğrulamalı staging; doku adı `T_<rol>_<Harita>`; FBX'te gömülü doku yok; GUID sabit (`git mv`; churn karşılaştırmasında GUID'ye dokunulmaz); AP üretim klasörlerine dokunulmaz; ham üretim repo dışında; binary'ler LFS'te. [PSP §11; m:wp-071-durumu DERS]

6.31 Yeni bir Unity YAML uzantısı eklenirse `.gitattributes`'a text + unityyamlmerge kuralı ve `ArtRepoPolicyTests.MetinUzantilar`'a satır eklenir. `LightingData.asset` LFS `-text`, `NavMesh-*.asset` `-text` olarak tutulur. [m:ders-sevk-varligi-asset-gate 25.09; m:unity-lightmap-dersleri]

6.32 `ConfigureRenderer` ya da SSAO değişince bir dev build koşulur ve URP PipelineAsset (prefilter bayrakları) commit'e alınır. [m:wp-038-durumu KURAL; EG 23.09]

6.33 **Adlandırma:** test sınıfı İngilizce `…Tests`, test adı Türkçe `Konu_Durum_Beklenen`. Kimlik şeması DEC / REQ / ADR-<WP>-<n> / AP / Ö / K / N; ADR teknik sözleşmedir, ürün kararı değildir; kod yorumları bu kimlikleri anar. [PSP §5, §11]

### 6D · Test politikası

6.34 Değişen süitler `tools/test.sh` ile guard altında yeşil olmadan teslim yapılmaz; kırmızılar sahibine etiketlenir. [PSP §9.3, §11; M4R1 §4.14d; OA §0.14]

6.35 **Değişiklik-kapsamlı yeniden doğrulama:**
- Düzeltmeden sonra tam kapı zinciri koşulmaz; değişen modülün testleri + düzeltme testi koşulur.
- Build/smoke yalnız şunlar değişince yeniden koşulur: ProjectSettings, Packages, asmdef, shader/materyal/sahne/prefab/ScriptableObject, Editor build betikleri, koşullu derleme sembolleri ya da probe, Resources/StreamingAssets. Aksi hâlde raporda "build/smoke: ilk tam koşu geçerli" satırı yazılır.
- Korunan yüzeylere (Core/Input, RunEvents, RunFlowClock, RunDirector arayüzü, Profile, UI Flow, Boot) dokunulursa tam PlayMode istenebilir.

[m:kural-degisiklik-kapsamli-yeniden-dogrulama 12.09; m:degisiklik-kapsamli-yeniden-dogrulama 12.09; m:main-entegrasyon-durumu]

6.36 **Dokunulan alanın kapı süitleri** teslim filtresine eklenir (`git diff --name-only main` listesine bakılır):
- `Assets/_Art` PNG/FBX/Resources → `ArtAssetGateTests` (kütüphaneye dokunulduysa `ArtLibraryGateTests`).
- Sahne / Build Settings → `ArtRepoPolicyTests`.
- Animasyon/karakter teslimi → bütün `Animation*` PlayMode + `Art*` EditMode + Audio EditMode + `ArtCharacterTests`; durum/geçiş eklendiyse `IsName(...)` iddiaları taranır.
- HUD dilimi → `UiTokensV3Tests` (+ Lifecycle).
- Sayı sabitlenmiş testler (prop/örnek/seviye sayısı) grep'lenir.

[m:ders-sevk-varligi-asset-gate 25.09; m:wp-071-durumu KURAL/DERS; m:ders-yeni-klip-olayi-ses-haritasi; m:wp-068-durumu ders; EG ders; m:wp-049-durumu ders]

6.37 EditMode saf mantık ve sözleşmeyi, PlayMode gerçek sahnede dikey dilimi sınar. [PSP §11]

6.38 Ölçüm/tanı testleri `[Explicit]` + `[Category("Diagnostics")]` taşır ve CI dışındadır. main-ci `--category '!^Diagnostics$'` geçer (sınıf regex'i Explicit testleri de seçtiği için). `[Explicit]` testi tam adıyla çağrılır. Atlanma kuralının tek yeri `tools/qa/nunit_skips.py`'dir (Skipped ∧ Explicit ∧ Diagnostics). [TR (main-ci, C-00 05.10); EG 05.10; m:wp-085-durumu ders; m:wp-078-r1-kabul Y6]

6.39 **Ayırma testi:** `ProfilerRecorder("GC Allocated In Frame")` + pozitif kontrol (64 KB yem) + `Application.targetFrameRate = -1` (SetUp'ta; TearDown'da geri) + `Profiler.enabled`. `GC.GetAllocatedBytesForCurrentThread` (Mono'da hep 0) ve `GC.GetTotalMemory` kanıt sayılmaz. Alloc testleri zincirde tek başına koşar; tek-kare yükseltme + kontrol + çiftlerde minimum kullanılır. [PSP §11, §15; TR (ProfilerRecorder); m:unity-batch-dersleri 17.09; m:wp-059d-durumu ders]

6.40 **Deterministik testler:**
- Kare/süre/his ölçen PlayMode testinde `Time.captureDeltaTime = 1f/60f` konur ve finally'de geri verilir.
- İki saati karıştıran mekanizmada test saati olarak `Time.timeScale` kullanılır.
- Silah/kamera/seçim değiştiren test, başta önceki değeri alır ve finally'de geri verir.
- Teardown süreç-tekil kaynağı Clear etmez; önceki değeri geri koyar.

[m:ders-batch-kare-suresi 05.10; m:ders-test-secim-sizintisi 05.10; m:wp-079-durumu DERS]

6.41 Test beklentisi kök neden bulunmadan "düzeltilmez"; placeholder davranışı testle onaylanmaz; silinen test raporda adıyla yazılır. [PSP §11; m:wp-009h-durumu ders]

6.42 Test scratch yolu süreç başınadır (`temporaryCachePath/dd-pid-<pid>`). NUnit derleme başına yalnız tek namespace'siz `[SetUpFixture]` çalıştırır; ek fikstür `namespace DD.Prototype.Tests`'e konur. [TR (test scratch); m:wp-068-durumu ders]

6.43 **HIZLI HAT ve HIZ MODU yalnız C-00 ilan ederse uygulanır** (§12 Ç2, Ç3):
- HIZLI HAT: izole dal, atomik commit, yalnız durma koşullarında durma, yeni ürün kararı vermeme. §6: tek acquire / tek build / yalın zincir. §7: tek commit, değişen süit tek koşu, build yalnız sevk varlığında, perf/alloc ölçümü yok, tanı paketi yok.
- HIZ MODU: temiz perf penceresi beklenmez, ölçüm "KİRLİ" damgalanır.
- M4-R1/R2'nin varsayılanı: dilim başına commit + kapılar.

[PSP §7 (Hat modları); m:hizli-hat-direktifi 15–17.09; m:hizli-hat-7-uretim-oncelik 17.09]

---

## 7. KADRO kullanımı

7.1 **Ajan kuralı (her pakette aynen):** «Bu çalışma için Kadro sistemindeki mevcut ajan ve uzman havuzunu değerlendir. En yüksek profesyonel kaliteyi elde etmek için gereken bütün uzmanları göreve dahil et; ajan sayısını önceden sınırlama. Bağımsız görevleri paralel yürüt, aynı dosya veya sistemlere dokunan görevleri bir entegrasyon sorumlusu altında koordine et. Ara raporu nihai teslim sayma. Kabul kriterleri sağlanana, hatalar düzeltilene ve testler başarıyla tamamlanana kadar çalışmayı kesintisiz sürdür.» [OA §0.14; PSP §7] (PSP sürümündeki son cümle için §12 Ç19)

7.2 **Kapılar:**
- (a) Yön/API kararı → uzman konseyinin kısa karar notu (lider + eş imza).
- (b) Görsel dilim → `art-director` AD turu: ACCEPT / ACCEPT_WITH_FIXES / KOŞULLU / REJECT. MUST ve SHOULD ayrılır; her MUST bir ölçüte ve bir kareye bağlıdır; "kişisel beğeni gerekçe değil". MUST paket içinde kapanır ya da P-xx'e devredilir; SHOULD sonraki pakete gider.
- (c) Ses dilimi → `sound-designer` + `sound-engineer` ses ölçüm kapısı (rapor/spektrogram; PASS / PASS-KOŞULLU). İnsan dinleme ayrı bir kapıdır ve kullanıcıdadır.
- (d) Her teslimden önce `gt` bağımsız doğrulama (kırmızı bayrağı bağlayıcı) + oyuncuya dokunan değişiklikte manuel test kiti.

[OA §0.14; PSP §8]

7.3 **GT ile A2 farkı:** hücre içindeki A2 "yazdığımız ölçütü karşılıyor mu?" diye sorar ve veto yetkisi vardır. Hücre dışındaki GT "başkasının elinde gerçekten çalışıyor mu?" diye sorar: sıfırdan yeniden üretir, gerçek ortamda dener; kırmızı bayrağı açıkken teslim kapanmaz; manuel test kiti üretir. [KDR §3]

7.4 Alt ajanlar `subagent_type` adıyla ve `model: "opus"` ile çağrılır. MCP gereken işler (Higgsfield MCP, Adobe, Blender MCP) `general-purpose` türünde yapılır: KADRO ajanlarının araç listesi MCP'yi dışlar. Bash çoğu KADRO ajanında vardır (ör. `3d-modeller`, `blender-specialist`, `unity-specialist`); `graphic-designer`, `art-director`, `concept-artist`, `ui-ux-designer`, `ux-writer` gibi tasarım ajanlarında Bash yoktur (C-00 06.10 düzeltmesi: taslaktaki "3d-modeller'da Bash yok" yanlıştı). [OA §0.14; BRF §10; DEV §6.2; m:kadro-sistemi-ve-c00-oturum-modeli]

7.5 KADRO ajanları yalnız ana depodaki gitignore'lu `.claude/agents → ~/Desktop/KADRO/.claude/agents` bağıyla ve oturum başında yüklenir; worktree'de açılan bir oturum onları görmez. [OA §0.13; m:kadro-sistemi-ve-c00-oturum-modeli 10.09]

7.6 Her alt ajan aktif-ajanlar dosyasına yazar (§10.4). Teslim notunda `KADRO:` satırı bulunur: kullanılan uzmanlar + verdict'ler + kanıt yolları. [OA §0.3, §0.14]

7.7 **Takımlar (lider önce):**
- S-06: character-animator + animation-specialist, unity-specialist, csharp-developer, game-test-engineer
- S-03: unity-specialist, csharp-developer, game-developer, 3d-modeller, game-test-engineer, git-repository-safety
- S-08: sound-designer + sound-engineer, unity-specialist, csharp-developer, video-editor, game-test-engineer
- S-09: ui-ux-designer, interaction-designer, art-director, video-editor, design-systems-engineer, graphic-designer, ux-writer, unity-specialist, game-test-engineer
- S-02: game-developer, character-animator, unity-specialist, csharp-developer, game-test-engineer
- S-01: git-repository-safety, backend-architect, test-engineer, technical-writer
- S-05: unity-specialist, art-director, game-test-engineer, git-repository-safety
- S-10: test-engineer + game-test-engineer, gt, product-analyst, technical-writer
- S-07: art-director, unity-specialist, animation-specialist, game-test-engineer

S-04 ve P-xx için takım yazılı değil: BELİRSİZ. [OA §0.14]

7.8 **Model:** her oturum, alt ajan (`model: "opus"`) ve workflow ajanı Opus 5.5'tir (claude-opus-5-5). Fable ve "Sonnet yardımcı" kuralları yürürlükte değildir (§12 Ç1). [BRF §3; OA §0.1, §0.3; DEV §11–§12]

7.9 Tasarım ve kritik kararlarda "ultracode" (30.09): Agent aracıyla yazar + 2–3 çürütücü (en fazla 5 ajan, Opus); Workflow aracı kullanıcının kendi onayını ister. 06.10 konsey notu kapısıyla ilişkisi BELİRSİZ (§12 Ç32). [m:model-politikasi-v3 30.09; m:model-politikasi-v4 30.09; m:wp-078-r1-kabul 30.09]

7.10 KADRO dosyaları (ajan, protokol) değiştirilmez; genişleme G6 kullanıcı onayı ister. KADRO klasörü git altında değildir; betik düzenlemeden önce yedek alınır. [KDR §4; m:kadro-sistemi-ve-c00-oturum-modeli; m:wp-079g-durumu 01.10]

7.11 **KADRO değişmezleri (çekirdek):**
1. Tek geçit.
2. Köken işaretsiz satır yok.
3. Onaysız kalıcı yazma yok (`context/memory/` yalnız G5'ten geçer).
4. Kapsamsız iş yok.
5. Kanıtsız karar yok (ölçüm + artefakt + mesaj kimliği).
6. A2 vetosu ve GT kırmızı bayrağı bağlayıcıdır.
7. Yoğunluk serttir, uzunluk serbesttir (kalıcı bağlamda BYO ≥ 25).
8. İş bölünmez.
9. Değişmez yalnız kullanıcı açıkça geri alırsa düşer.
10. Yorum yok (söylenmemiş ihtiyaç üretilmez; öneri ayrı başlıkta).

Projede kullanıcıya tek temas noktası C-00'dır (§1.5). [KDR §8; OA §0.2]

7.12 KADRO Değişmezler Defteri boştur (06.10); proje kurallarının hiçbiri kullanıcı onaylı değişmez olarak kayıtlı değildir. [DGM]

7.13 **Bulut oturumları:** salt okunur kod incelemesi ve sayısal denetim yapar; bulgu-yalnız rapor verir (`cloud/*` dalında tek commit). Düzeltmeyi sahip oturum yapar; main'e C-00 alır. Bulut rutinlerine bağlayıcılar otomatik eklendiği için promptlara "kredi harcama yok" yazılır. [PSP §6, §8; m:m4-r1-revizyon-turu 24.09; EG 24.09]

7.14 **C-00B (ikinci ekip, bulut):** `c00b/*` dalları + PR ile çalışır; belge, Python aracı ve testi, inceleme, Unity'siz C# taslağı yapar. main'e alamaz; Unity, Blender, Player, KADRO ve harici diske erişimi yoktur. 06.10'dan beri bazı C-00B görevleri (B-07, B-08, B-09) yerel KADRO alt ajanlarıyla koşar. [BRF §2; git `18f3d01e` gövdesi]

7.15 **Codex:**
- Kota 12.10 09:52'ye kadar bitik; o tarihe kadar üretim Claude alt ajanlarındadır.
- Codex dönünce akış: görev dosyası `K/c-00/codex/prompts/CX-*.md` → kullanıcı Codex'e verir → rapor `K/c-00/codex/reports/`'a yazılır → C-00 okur.
- Codex'te her görev için Fast + GPT-6 Astra arayüzden seçilir; inceleme seremonisi yazılmaz; teslim öncesi tek `dd_validator` koşulur.
- `~/.codex/config.toml`'u kullanıcı değiştirir; `~/.codex/auth.json` asla okunmaz.

[DEV §0.6; BRF §13; m:kullanici-kararlari-2026-10-05-r2; m:codex-hiz-ayari 05.10; m:codex-r2-kurulum 05.10; AGM]

7.16 Aynı hesaptaki oturuma mesaj `SendMessage` (ad ya da `local_…` id) ya da `mcp__ccd_session_mgmt__send_message` ile gönderilir. Bu hesapta oturum açma aracı yoktur: oturumları kullanıcı açar, C-00 `list_sessions` ile bulup yönetir. [DEV §2; m:oturum-acma-kullanici-acar 06.10; OA §0]

---

## 8. Kaynaklar ve üretim

8.1 **Higgsfield ve Adobe sınırsız (kullanıcı 05.10):** tavan ve taban yoktur, ama ledger zorunludur. Her üretim `asset-production/AP-0NN/03-higgsfield/ledger.csv`'ye (tarih, model, adet, kredi, çıktı yolu) yazılır; provenance README'de tutulur. [m:kullanici-kararlari-2026-10-05-r2 05.10; BRF §3; DEV §1; CXS §7; m:higgsfield-oncelik-direktifi]

8.2 Ledger'a gerçek tutar yazılır: üretimden önce bakiye (`higgsfield account status`), sonra işlem kaydı (transactions) okunur. `get_cost` yalnız yaklaşıktır ve güvenilmez. [m:higgsfield-kredi-dogrulama 16–24.09; m:higgsfield-cli-kurulum 06.10; M4R1 §4.5]

8.3 Bu hesapta Higgsfield CLI her oturumda çalışır (`higgsfield …`, PATH `/opt/homebrew/bin`; "Private" workspace seçili). Higgsfield MCP (`https://mcp.higgsfield.ai/mcp`) ve Blender MCP (`blender-mcp`) 06.10'da kullanıcı düzeyinde (`~/.claude.json`) tanımlandı: yalnız o saatten sonra açılan oturumlar görür; Higgsfield MCP kullanıcının tek seferlik OAuth girişini ister. Adobe (claude.ai bağlayıcısı) her oturumda bağlıdır. CLI PATH'te bulunamazsa skill'lerin önerdiği `curl … install.sh | sh` kurulumu çalıştırılmaz. [OA §0.11; m:higgsfield-cli-kurulum 06.10]

8.4 Seed Audio ve her tür AI ses üretimi SFX'te YASAKTIR. higgsfield-generate ses için Seed Audio'yu varsayılan sayar; o yol kullanılmaz. [BRF §3; OA §0.11; DEV §1; m:higgsfield-cli-kurulum]

8.5 **Higgsfield öncelikli:** her yeni görsel öğede ilk adım Higgsfield'dır (konsept → multiview → Tripo H3.1 → Blender retopo/sözleşme; Meshy retexture; upscale); Adobe finisajda kullanılır. Elle sıfırdan üretim yalnız Higgsfield çıktısı kullanılamıyorsa ve README'de gerekçeyle yapılır. Seçilen ve reddedilen adaylar `03-higgsfield/` altında tutulur. [m:higgsfield-oncelik-direktifi 15.09; PSP §3.3.30]

8.6 Higgsfield çıktısı teslim değildir; sözleşmeye uydurulmuş Blender/Unity varlığı teslimdir. **Sadakat kuralı:** teslim, seçilen konsepte biçim, oran, malzeme ve renk olarak eşleşir; konsept ile render aynı açıdan yan yana karşılaştırılır; sapma listesi README'ye yazılır; gerekçesiz sapma ACCEPT_WITH_FIXES alır. [m:higgsfield-oncelik-direktifi §5 16.09]

8.7 Adobe araçları yerel dosya almaz: upload init → PUT → finalize, presigned URL ile işlenir, sonuç indirilir. Paylaşım URL'leri sonradan 403 verebilir (her oturumda yeniden yüklenir). Generative ücretler README'ye yazılır. Masaüstü Adobe uygulaması kurmak kullanıcı kararıdır. [m:higgsfield-oncelik-direktifi §7; m:unity-lightmap-dersleri; M4R1 §4.6]

8.8 **Lisans:** yalnız oyunda ticari kullanıma izin veren kaynaklar kullanılır. Her dış kaynak için `03-sources/LICENSES.csv`'ye satır yazılır (dosya, URL, lisans, tarih, kullanım); lisansı belirsiz kaynak kullanılmaz. Büyük ham kaynaklar iCloud dışında `/Users/Erdem/DD-Sources/<kaynak>/` altında durur; KADRO'ya yalnız kullanılan türevler girer. İndirmeden sonra boş alan 15 GB'ın altına inmemelidir. [CXS §6–7; AGM; BRF §4]

8.9 Ücretli araç, lisans, servis, hesap ve yayın kararı kullanıcınındır; ürün adı, imza ve mağaza hesaplarını kullanıcı açar. Kurulum (pip/brew/uygulama/Unity paketi) kendiliğinden yapılmaz; "ARAÇ İSTEĞİ" olarak raporlanır. [PSP §3.7.51; CXS §6; AGM; M4R1 §4.10]

8.10 AP paketleri repoya ve Unity projesine yazmaz. Ham üretim `K/asset-production/AP-0NN/` altındadır (tek kopya + harici yedek). Sahip S-oturumu teslimi dilim dilim, manifest + doğrulayıcı kapısıyla içe alır; Codex teslimi içe alınmadan önce `tools/codex_manifest_check.py <AP> [--version vNNN]` koşulur (0 = PASS). [PSP §6; BRF §4; R2P §2; TR (codex_manifest_check)]

8.11 **Teslim paketi:** `manifest/AP0NN-manifest-vNNN.json` + `SHA256SUMS`. Biçim kuralları:
- 3B: FBX 7.4 binary, metre, Y-up, `<Varlık>_<Parça>_LOD<n>`, çarpışma `COL_`.
- Rig: Humanoid'e eşlenebilir, en çok 4 etki/vertex.
- Doku: PBR (`_BaseColor` sRGB, `_Normal` OpenGL Y+, `_ORM`), PNG, güç-iki.
- Animasyon: 30 fps + `anim-events.json`.
- Ses: WAV 48 kHz / 24-bit, tepe ≤ −1 dBTP, baş/son sessizlik ≤ 5 ms.
- UI ikon: 64/128/256 px + SDF.

[CXS §4.1–4.2; AGM]

8.12 **Seçenek paketi:** `02-concept/options-vNNN/` (O1…On aynı çerçeve ve ışıkta, `options.md`, `contact-sheet.png`). Şıklar gerçekten farklı olmalıdır (siluet, palet, malzeme dili). [CXS §4.3]

8.13 Üretim ajanları (ör. AP-024C, AP-025D) C-00'ın arka planda çalışan `general-purpose` alt ajanlarıdır. Kontrol noktasında DURURLAR → C-00 görsel onayı verir → kalan iş sürer. Rapor `K/c-00/codex/reports/C00-<AP>-RAPOR.md`'ye yazılır. Yarım kalan ajan, çıktı klasörlerine bakılarak aynı kapsamla yeniden başlatılır. [DEV §6.2; BRF §8; m:oturum-acma-kullanici-acar 06.10]

8.14 **Teslim denetimleri:**
- Görsel: siluet (üstten kamera mesafesinde de), palet, topoloji/deformasyon, dikiş ve texel tutarlılığı, LOD geçişi.
- Animasyon: ayak kayması ≤ %5 (kalça hızına göre), kadans tablosu, üç kamera karesi.
- Ses: nesnel kapı (spektrum, LUFS, kuyruk) + kullanıcı dinleme kapısı (gerçek oyun hızında A/B).

[CXS §9]

8.15 Önceki teslim değiştirilmez ya da silinmez; başka paketin klasörüne yazılmaz; Unity, URP ve `Packages/manifest.json` değiştirilmez. [CXS §10]

8.16 **Codex teslimi:** manifest + SHA256SUMS; rapor `…/c-00/codex/reports/` altına yazılır; kullanıcıya son mesaj tek satırdır (rapor yolu); her teslimden önce `dd_validator` koşulur. [AGM]

---

## 9. Disk, arşiv, silme

9.1 Silme yalnız Çöp'e yapılır (Finder; iCloud masaüstünde Finder -8013 verirse `mv … ~/.Trash/`). Kalıcı silme ve Çöp'ü boşaltmak kullanıcının işidir. [OA §0.10; BRF §3, §10; m:kural-silme-dikkat 30.09]

9.2 Çöp'e yalnız doğrulanmış olanlar gider: temiz (`git status` 0) ve main'e girmiş (`git cherry` 0) worktree'ler, yerini yenisi almış eski release build'leri, fazladan yedekler (en yeni 3 ve korunanlar kalır). [m:kural-silme-dikkat 30.09]

9.3 **Asla silinmez ya da taşınmaz:** evidence/, reports/, prompts/, manifest/, backup/, asset-production/, planning, repo, kullanıcı profili, son doğrulanmış release, kullanıcının oynadığı build, commit'lenmemiş iş, bir oturumun kullandığı worktree (sahibine sorulmadan). [m:kural-silme-dikkat; PSP §9.8; TR (cleanup-merged)]

9.4 Build, perf ya da evidence klasörüne dokunmadan önce sahip oturuma sorulur ("eski" görünen tarih yeterli gerekçe değildir). Oturumlar tutulacak klasöre `.dd-keep` (sahip + amaç + tarih) koyar. cleanup-merged `.dd-keep`'i, kullanımdaki klasörleri ve son 24 saatte değişenleri korur. [m:kural-silme-dikkat 30.09; m:wp-073-durumu KURAL 30.09; TR (cleanup-merged)]

9.5 **Harici arşiv:** `/Volumes/TOSHIBA EXT/DD-ARSIV/` (APFS). Kullanıcı yetkisi (05.10): "temizliklerde kullanılmayan her şeyi harici diske alabilirsin" (disk takılıyken, onay sormadan). Sıra: `apfs_real_gain.py` ile gerçek kazanç → kopyala → SHA256 doğrula → asıl Çöp'e → `K/c-00/disk-arsiv/ARSIV-DIZINI.md`'ye yaz (betik: `arsivle.py --parti … --gerekce …`). [BRF §3; DEV §2, §9; m:harici-disk-arsiv 05.10]

9.6 Worktree'ler harici diske taşınmaz. Aktif ve kaynak AP'ler (007/014/018/019/021/022) yerinde kalır. Disk takılı değilse arşive başvuran araçlar çalışmaz; dizin dosyasına bakılır. [m:harici-disk-arsiv 05.10; DEV §9]

9.7 `cleanup-merged.sh` varsayılan olarak dry-run çalışır ve kirli ya da entegre olmamış bir worktree'yi asla silmez. `--apply` yalnız C-00'ın işidir (yetki ve kalıcı silme sorunu: §12 Ç7). [PSP §9.8; TR (cleanup-merged)]

9.8 **Disk izleme:** üretim saatte 5–7 GB yazar. `tools/disk-report.sh` (realGain sütunuyla) ve post-integrate disk raporu kullanılır. Yedekler `~/Backups/deaths-demolition/<utc>/` altındadır (bundle + LFS, son 3). [BRF §13; DEV §9; PSP §9; TR (backup-repo)]

---

## 10. Raporlama ve iletişim

10.1 **Teslim C-00'a ≤ 10 satır:**
```
WP-NNN <dilim> — <sha> @ <dal> (taban <main sha>)
Değişen: <sistemler / önemli dosyalar>
Kanıt: <K/wp-NNN/evidence/... yolu>
Test: EditMode a/a · PlayMode b/b (guarded, scope editor|player) · build/smoke: …
Perf / AD / ses kapısı: …
Açık / devir: → S-xx: … ; 🔴: … ; kredi: n kr (ledger no)
```
06.10 ekleri: `r2_gates_lint` sonucu (ERROR 0 + WARN listesi) ve `KADRO:` satırı. SHA rev-parse çıktısından alınır; guard uyarısı varsa tek satır yazılır. [PSP §7.6, §17; OA §0.4, §0.14; m:kural-guard-oos-player-uyari]

10.2 **Paket kapanış raporu (standart):** yapılan işler · değişen dosyalar ve üretilen varlıklar · çalıştırılan testler ve sonuçları · bilinen sorunlar ve kalan riskler · entegrasyon etkileri · kritik karar gereken noktalar · sonraki önerilen çalışma. [PSP §7, §17]

10.3 Rapor dili Türkçe, yoğun ve sayılıdır; sapmalar açıkça yazılır ("kirli okuma kanıt değil", "no third run — quota"). [PSP §11]

10.4 **Aktif ajan kaydı:** başlarken, bitirirken ve her teslimde `K/c-00/aktif-ajanlar/S-0N.md`'ye şu satır yazılır: `gg.aa SS:DD · rol · opus-5-5 · görev · dal · durum`. Açılan alt ajanlar da yazar. [OA §0.3] (eski biçim: §12 Ç13)

10.5 Claude oturumları nihai raporu ve manuel kit maddelerini mesaj/sohbet olarak teslim eder; dosya yolu yalnız referanstır. Codex ve C-00'ın üretim ajanları rapor dosyası yazar. [m:rapor-teslim-sohbetten 12.09; AGM; CXS §8; DEV §6.2]

10.6 Ara durum tek satırdır; onay ya da tekrar mesajı atılmaz; C-00 kullanıcıya kısa yazar. [M4R1 §4.12; m:hiz-modu-2026-09-17]

10.7 **Mesaj sınırı:** kullanıcı yazmadıkça Desktop oturum mesaj sınırı (10) dolabilir. Önemli mesajlar önce gönderilir, kalanlar `K/c-00/R2/BEKLEYEN-MESAJLAR.md`'ye yazılır. Mesaj gönderemeyen oturum `wp-0XX/OUTBOX-PENDING.md`'ye yazar. "Session limit" ile duran oturuma, reset sonrasında mesaj yeniden gönderilir. [m:feedback-sormadan-devam 05.10; EG 01.10; m:m4-r1-revizyon-turu 24.09]

10.8 **Oturum açılışı:** C-00 kullanıcıya başlık + klasör + hazır ilk mesaj verir. Her oturum önce `docs/c00/OTURUM-ACILIS-2026-10-06.md` §0'ı okur; sonra PSP, R2-PLAN, BRF §7 (kendi satırı) ve §9, BEKLEYEN-MESAJLAR (kendi bölümleri), paket promptu ve devir notunu okur. [OA §0.12, §1; m:oturum-acma-kullanici-acar 06.10]

10.9 Model ya da kredi sapması C-00 notuna yazılır. [PSP §8]

10.10 C-00 işi devralınca kullanıcıya "devraldım" mesajı ve görselli durum sayfası verir (R2 tablosu, yarım işler, bekleyen görseller). [BRF §12; DEV §0.7]

10.11 C-00 entegrasyonları, kararları ve lease karantinalarını entegrasyon günlüğüne (`memory/entegrasyon-gunlugu-2026-09.md`) satır olarak yazar. [DEV §5; BRF §4; TR (CI lease quarantine)]

---

## 11. Teknik dersler (tek satır + kaynak)

**Unity batch ve test koşucusu**
11.1 Batchmode PlayMode'da `WaitForEndOfFrame` hiç dönmez ve `ScreenCapture` çalışmaz; `cam.Render(); yield return null` ve testte `targetTexture` kullanılır. [m:unity-batch-dersleri; m:unity-shader-dersleri; m:wp-019-durumu ders]
11.2 Batch PlayMode kareleri 1–3 ms sürer; `captureDeltaTime` oyun saatini sabitler ama `unscaledTime` gerçek kalır. [m:ders-batch-kare-suresi 05.10]
11.3 `targetFrameRate` 60'ta kalmışsa `ProfilerRecorder.LastValue` Editor koşucusunda 0 okur; ölçüm öncesi −1 yapılır. [TR (ProfilerRecorder); m:wp-059-durumu ders]
11.4 `GC.GetAllocatedBytesForCurrentThread` Unity Mono'da hep 0 döner; `GC.GetTotalMemory` 4 KB granüler olduğundan küçük ayırmaları görmez. [m:unity-batch-dersleri 17.09]
11.5 Batchmode PlayMode'da `ProfilerDriver` kare kaydetmez (frames −1); GC.Alloc yığını çoğu koşuda alınamaz. [m:wp-053d-durumu ders; m:wp-069-durumu ders]
11.6 Testlerin `ResetStatics` çağrısı statik event'i siler; DDOL dinleyici yalnız tam sıralı koşuda sağırlaşır → her sahne değişiminde `Rearm`. [m:static-bus-rearm-dersi]
11.7 `EquipClassic` süreç çapındaki `WeaponRegistry.SelectedWeapon`'ı yazar; geri vermeyen test sonraki testlerin oyuncusunu başka silahla doğurur. [m:ders-test-secim-sizintisi 05.10]
11.8 Exit 134 + testler PASS + guard verified=1 + PhysicsManager NULL finalizer yığını = motor kapanış yarışı, kırmızı değil. C-00'a tek satır yazılır; yığın farklıysa ya da test FAIL ise DUR. [m:ders-exit134-kapanis-yarisi 05.10]
11.9 Exit 134 "Multiple Unity instances" ya da seconds=0/1 = aynı projede ikinci batch, yani sahte koşu. [m:unity-batch-dersleri 17.09; TR (compile-check)]
11.10 Editor açılışı log üretmeden saatlerce asılabilir; EditMode batch `UnloadUnusedAssetsOperation`'da takılabilir → bekçi koy, yalnız kendi pid'ini durdur, yeniden koş. [m:unity-batch-dersleri 25.09; m:wp-040-durumu ders]
11.11 NUnit derleme başına tek namespace'siz `[SetUpFixture]` çalıştırır. [m:wp-068-durumu ders]
11.12 EditMode'da düz MonoBehaviour için `OnEnable`/`OnDisable` çağrılmaz → açık Register/Unregister gerekir. [m:wp-076-durumu ders]
11.13 PlayMode tek play oturumudur: DDOL havuzları, önceki fikstürün MainCamera'sı ve kapatılmış bileşenler sonraki fikstüre taşınır. [m:unity-batch-dersleri 24.09; m:wp-019-durumu ders]
11.14 Havuzlu düşman testte `Object.Destroy` ile değil `Remove()` ile bitirilir. Spawn'a ateş eden testler kalkışı (DEC-081) ve `Physics.SyncTransforms`'u bekler. [m:wp-074-075-durumu ders; m:wp-049-durumu ders]
11.15 Unity `Temp/` çıkışta silinir; kanıt dosyaları oraya yazılmaz. [m:wp-042-durumu ders]
11.16 TMP fallback atlası dinamik glifle kirlenir ve tam PlayMode build'i `-dirty` yapar → commit'e alınmaz, test sonrası geri alınır. [m:wp-022-durumu ders; m:wp-064r2-durumu]
11.17 Unity Humanoid klipleri ekstra transform eğrilerini (fp_camera, socket) düşürür; bu anahtarlara güvenilmez. [m:wp-071-durumu DERS]

**Shader, render, materyal**
11.18 Shader özelliğini yeniden adlandırmak ve materyal üretmek aynı batch'te yapılmaz; materyal sonraki batch'te kurulur. [m:unity-shader-dersleri 16.09]
11.19 Metal'de `UnityPerMaterial` CBUFFER tek tip (float) tutulur, vertex rengi `TEXCOORDn`'de taşınır. ps_5_0'da sampler sınırı 16'dır; aynı import ayarlı dokular sampler paylaşır. [m:unity-shader-dersleri]
11.20 Resources dışındaki yeni shader build'e girmez (`Shader.Find` null döner). [m:wp-019-durumu ders]
11.21 Shader varsayılanını değiştirmek mevcut materyali güncellemez. Paylaşılan shader'a eklenen yeni özelliğin varsayılanı eski davranışı korumalıdır. [m:wp-074-075-durumu ders]
11.22 URP Lit içe alımı, alfa-kesimli opak materyale `_AlphaToMask = 1` yazar; GI EmissiveIsBlack iken `_EMISSION`'ı söker. [m:wp-038-durumu ders; m:wp-076-durumu DERS]
11.23 Raymarch'ta `_SHADOWS_SOFT` varyantı derlenmez (adım × tap sayısı patlar). [m:wp-074-075-durumu ders]
11.24 NPOT doku + mip zinciri sıkıştırılmaz (RGB32 kalır); NPOT UI plakalarında mip kapatılır ve build.log "Used Assets"e bakılır. [m:wp-068-s6-menu-plates ders]
11.25 Proje Linear renk uzayındadır: uGUI doğrusal ışıkta harmanlar; algısal α 0,10 ≈ doğrusal 0,02–0,03. [m:ui-dogrusal-harmanlama]
11.26 URP PipelineAsset prefilter bayrakları build sırasında yeniden yazılır; build-dev `DD_URP_Cinematic`'i kirletir → worktree'de geri al. [m:wp-038-durumu KURAL; m:wp-068-durumu ders]

**Lightmap, NavMesh, dünya**
11.27 `secondaryUVMinLightmapResolution` küçüldükçe margin genişler. `receiveGI` setter'ı ContributeGI'sız renderer'da no-op'tur → `m_ReceiveGI` SerializedObject ile yazılır. [m:unity-lightmap-dersleri]
11.28 Statik birleşim her LOD'un vertex'ini level dosyasına kopyalar; ağır prop aileleri birleşim dışında tutulur. [m:wp-038-durumu ders; m:wp-017-durumu ders]
11.29 NavMesh ↔ fizik ilkesi iki yönlüdür: görünmez çarpıştırıcı ekleyen her pakette geçiş testi yapılır. NavMeshModifier'ın kendi katmanı bake maskesinde olmalıdır; yarıçap ve tırmanış voksele yuvarlanır. [EG 24.09; m:wp-038-durumu ders; m:wp-074-075-durumu ders]
11.30 `ApplyAreaCosts` EditMode testi ProjectSettings'e yazar; asset diff'i commit'e girer. [m:wp-054-durumu ders]
11.31 Unity `SaveAssets`, asset'te olmayan kod alanlarını varsayılanla serileştirir → paralel paketler çakışır. [m:wp-050-durumu ders]
11.32 Yeniden üretilen controller'lar alt-varlık ID churn'ü üretir; değişmeyeni taban sürümüne geri al. Churn filtresinde `\bid:` kullanılır, GUID'ye dokunulmaz. [m:wp-032-durumu ders; m:wp-071-durumu DERS]

**Animasyon**
11.33 `CullUpdateTransforms` kemiğe bağlı gameplay soketini dondurur → gameplay düğümleri `Visual` köküne, gerekirse AlwaysAnimate, `ResetForSpawn`'da `Animator.Update(0)`. [m:unity-animator-culling-dersleri]
11.34 `ModelImporterClipAnimation.events` zamanı normalizedir (0..1); `AnimationClip`'te saniyedir. [m:unity-batch-dersleri]
11.35 Animation Rigging humanoid gövdede yalnız Animator'ın ilk değerlendirmesinden SONRA kurulursa çalışır; kurulumdan sonraki ağırlık değişimi akmaz → fade gerekiyorsa OnAnimatorIK. [m:unity-animation-rigging-dersleri 05.10]
11.36 AP-009 kökü −Z'ye bakar → `rotationOffset 180`. Blender `speed_mps` Unity hızına eşit değildir (StrideCalibrator); `SampleAnimation` hızı canlıdan %15–33 sapabilir. [m:unity-animator-culling-dersleri]
11.37 FBX importer: `bakeAxisConversion` ile Mesh (bx, by, −bz) + node X=+90°. [m:unity-batch-dersleri]
11.38 Animator Attach sırasında başlatılmamışsa `layerCount` 0 döner → katman indeksi tembel çözülür. [m:wp-071-durumu ders]

**Ses**
11.39 Mixer bus tabanı test başında okunup geri yazılmaz (snapshot geçişi sürüyor olabilir). Olay karesi için `FramesCaptured` kullanılır; `FramesWritten` ~0,9 s geride kalır. [m:ders-olcum-kaynak-kayitta-var 05.10]
11.40 Hibrit ateş penceresi 60/RPM×1,6'dır; yanlış RPM'li kit bu yolu hiç açmaz. [m:ders-kit-gercek-rpm 01.10]
11.41 EQ filtreleri kuyruğa çınlar → one-shot'larda `finish_silent` kullanılır. MasterVolume snapshot'ta etkisizdir. [m:wp-060-durumu ders; m:wp-072-durumu ders]

**Kabuk, git ve araç**
11.42 zsh `$VAR`'ı sözcüklere bölmez (dizi ya da `${=VAR}` kullan); çift tırnakta `$'..'` açılmaz; `echo` `\n`'i satır sonu yapar. [m:wp-076-durumu ders; m:wp-022-durumu ders; m:wp-079g-durumu 05.10; m:commit-bicimi-kapi8]
11.43 macOS `sed -i` ve BSD `\|` sessizce başarısız olur → python kullan. C locale'de python heredoc UTF-8 okuyamaz → `python3 -X utf8 dosya.py`. Tırnaksız heredoc `\\n`'i yutar → `<<'PY'`. [m:wp-056-durumu ders; m:wp-074-075-durumu ders; m:wp-077-durumu ders]
11.44 `open(p,'w').write(open(p).read()…)` dosyayı sıfırlar; tek oku-değiştir-yaz kullanılır. [m:wp-071-durumu ders]
11.45 `| head -1` SIGPIPE ile test edilen betiği öldürür; self-test çıktısı dosyaya alınır. [m:wp-066-durumu DERS]
11.46 `cd X && …; git …` zincirinde cd başarısızsa git ana repoda koşar. Dal başka bir worktree'de checkout'luysa `git switch` sessizce başarısız olur → `rev-parse HEAD` doğrula. [m:unity-batch-dersleri 23.09; m:wp-032-durumu ders; m:wp-068-durumu 01.10]
11.47 Bisect betiğindeki `git checkout -- <test>` çalışma ağacındaki düzenlemeyi siler. [m:wp-059c-durumu ders]
11.48 Player cwd = build klasörüdür → `-ddUiCaptureOut` ve `-logFile` mutlak yol olmalı; noktalı klasör adı `Path.HasExtension` true döndürür. [m:wp-068-durumu ders]
11.49 .NET `"{4:F2}}}"` biçimi geçersiz JSON üretir → `"{4:F2} }}"`. [m:ders-player-yakalama-ve-olcum 05.10]
11.50 İzni olmayan `screencapture` hata vermez, masaüstü döner; Mac kilitliyken Player pencere geçişi yapmaz. [m:wp-068-durumu ders; m:wp-022-durumu ders]
11.51 QaRunBot yalnız DEVELOPMENT_BUILD'de çalışır; release Player bot bayrağıyla sonsuz bekler (runner'a timeout koy). Bot'ta "stick 0" = girdi ulaşmadı imzası. [m:unity-batch-dersleri 17.09; m:wp-069-durumu ders]
11.52 macOS'ta `HOME` override persistentDataPath ve CFPreferences'ı yönlendirmez. cfprefsd plist'i geç yazar; smoke ile soak aynı anda koşmaz. [m:wp-025-durumu ders; m:wp-066-durumu ders]
11.53 Guard'a INT/TERM/HUP trap'i eklenmez (zsh, SIG_IGN olan sinyalin trap'ini yine çalıştırır). Yeni bir çıkış kodu eklenirken bütün çağıranlar bulunur. [TR (userdata-guard); m:wp-079g-durumu 01.10/05.10]
11.54 `grep && python` zincirinde grep eşleşmezse düzenleme sessizce atlanır → commit öncesi `git show --stat`. [m:wp-043-durumu DERS]

---

## 12. Çelişki çözüm tablosu

| # | Konu | Eski metin (tarih) | Yeni metin (tarih) | Geçerli | Gerekçe |
|---|---|---|---|---|---|
| Ç1 | Model politikası | M4R1 §4.1 + PSP §3.9: tasarım/AD Fable 5.1 (24.09); m:model-politikasi-v2: Fable yalnız yön/AD, tek konsey çağrısı (24.09); v3: hepsi Opus, Fable yok (30.09); v4 + m:model-kurali-sonnet-yardimci: okuma/mekanik yardımcılar Sonnet 5.5 (30.09); R2P §2 + CXS §9: "Fable AD/ses konseyi" (05.10 sabah) | BRF §3, DEV §11–§12: "her şey Opus 5.5; oturumlar, alt ajanlar (`model: "opus"`), workflow ajanları" (05.10 akşam); OA §0.1, §0.3 (06.10) | **Opus 5.5 her yerde.** AD ve ses kapıları KADRO ajanlarıyla Opus'ta. | En yeni kullanıcı tercihi (BRF §3 "kullanıcı tercihleri ve yetkiler"), 06.10'da C-00 tarafından tekrarlandı. |
| Ç2 | Tören/maliyet ↔ KADRO kapıları | m:maliyet-uretime-toren-yok (15.09): adım başına tek Opus ajanı, tören yok; HIZLI HAT (15.09): manuel kit, inceleme, PRA/kapsül yok; §7 (17.09): tanı paketi yok; kota modu (25.09): yardımcı ajan ≤2 / ≤1 / 0 | OA §0.14 (C-00 06.10, commit `18f3d01e`): ajan kuralı aynen, ajan sayısı sınırsız; konsey + AD + ses + gt + manuel kit; 05.10 kredi sınırsız | **06.10 KADRO kapıları.** HIZLI HAT ve kota modu yalnız C-00 ilan ederse. | Daha yeni C-00 kararı; dayandığı ajan kuralı PSP §7'ye göre "her promptta aynen" bulunan kullanıcı kuralıdır. Maliyet gerekçesi 05.10 "kredi sınırsız" ile zayıfladı. 06.10 kapılarının ayrıca açık bir kullanıcı talimatına dayanıp dayanmadığı kaynakta yazılı değil: BELİRSİZ. |
| Ç3 | AD turu sıklığı | HIZLI HAT §7 (17.09): AD yalnız yeni görsel öğede, türev/cila pakette yok | PSP §7 (M4-R1 dilim başına AD); OA §0.14 (06.10): görsel dilim → AD turu | **Her görsel dilimde AD turu.** | Daha yeni C-00 kuralı; §7 bir hat moduydu (PSP §7 "Hat modları"). |
| Ç4 | Teslim notu uzunluğu ve ara mesajlar | HIZLI HAT ≤15 satır (15.09); hız modu ≤10 (17.09); §7 ≤5 + "ara durum mesajı yok" (17.09) | M4R1 §4.12 ≤10 + "ara durumlar tek satır" (24.09); PSP §17 ≤10; OA §0.4/§0.14 ekleri (06.10) | **≤10 satır + `r2_gates_lint` + `KADRO:` satırı; ara durum tek satır.** | En yeni C-00 metinleri. |
| Ç5 | Higgsfield kredi tabanı/tavanı | taban 300 → 200 (16.09) → 100 → 30 (17.09) → R1 193,5 (24.09) → 150 (25.09); PSP §3.7.52 "son yazılı taban 30" (bayat), §15 "varsayılan 0"; direktif "1 aday (gerekirse 2)" | m:kullanici-kararlari-2026-10-05-r2: "kredi sınırını kaldırıyorum", Adobe sınırsız (05.10) | **Tavan/taban yok, ledger zorunlu.** Bakiye/işlem kaydı doğrulaması ledger doğruluğu için sürer. | En yeni kullanıcı kararı. "1 aday" kuralının hâlâ geçerli olup olmadığı BELİRSİZ (R2 seçenek paketleri bilerek çok şıklı: CXS §4.3). |
| Ç6 | Kullanıcıya soru | 🔴 blok (12.09); AskUserQuestion şıkları (24.09); oturumlar sormaz, C-00 sorar (25.09); 05.10 sabah "kritik dışında sorma" + R2P §2 "seçim gelmeden o yolda üretim ilerlemez" + "Codex tasarım seçimlerini şıklarla sun" | 05.10 öğleden sonra: "hiçbir şey sorma … onaylaman için izin veriyorum" (BRF §3; m:feedback-sormadan-devam ek) | **Soru yok; C-00 karar verir, görselle bildirir; yalnız §1.4 istisnaları kullanıcıya.** | En yeni kullanıcı talimatı. |
| Ç7 | Merge/push/cleanup onayı ve kalıcı silme | PSP §6/§8: merge/push ve temizlik onayı kullanıcıda; TR: `--apply` "user's approval" | m:codex-geri-devri (29.09): rutin işlem (integrate, etiket, cleanup `--apply` dahil) onaysız; 05.10 tam yetki | **Merge, etiket, push: C-00 onaysız yapar.** `cleanup-merged --apply`: BELİRSİZ. | 05.10 istisnası kalıcı silmeyi kullanıcıya bırakıyor; `git worktree remove` ve build klasörü silme Çöp'e gitmeyen kalıcı silmedir. C-00 kararı gerekli (öneri: `--apply` yerine Çöp'e taşıma, ya da kullanıcı onayıyla). |
| Ç8 | Guard yolu | worktree `tools/userdata-guard.sh` (24.09); yalnız ana repo yolu (30.09) | Kanonik sürücü `K/wp-079/driver/tools/userdata-guard.sh`; repo yolu 64 (01.10) | **Sürücü (`DD_GUARD_CANON`).** OA §0.6'daki "tools/userdata-guard.sh" ifadesi bu kanonik yolu kasteder. | En yeni C-00 kuralı; TR (G4 D4). |
| Ç9 | PlayMode ve player slotu | m:wp-078-r1-kabul DERS + m:kural-kilit-sirasi-player-unity ilk cümlesi: "PlayMode da player slotunu alır" (30.09) | Aynı gün netleştirme: editor-scope EditMode/PlayMode player slotu ALMAZ, o cümle GEÇERSİZ (m:makine-slot-kurali, m:kural-kilit-sirasi 30.09) | **Günlük EditMode/PlayMode `--scope editor`, player slotsuz; kabul/CI pencereleri player slotlu.** | Aynı günün daha sonraki C-00 teyidi. |
| Ç10 | Kilit sırası | player → unity (30.09) | D8: tüm kapsamlarda guard → unity, unity tutarken 64 (05.10, rollout bekliyor) | **D9 rollout'a kadar player → unity; sonra guard → unity.** | D8 §0: "rollout günü geçerli olur". |
| Ç11 | Worktree'yi main'e getirme | M4R1 §4.3 (24.09) ve m:wp-068-durumu (C-00 24.09): main dala merge edilmez; yeni dal + cherry-pick | D8 §1 (05.10): `git merge --ff-only main`, dal ileride ise `git -C <worktree> merge main` | **BELİRSİZ — C-00 kararı.** | ff-only çelişmez; ikinci yol merge commit üretir ve 24.09 kuralına aykırıdır. Öneri: ff olmuyorsa main'den yeni dal + cherry-pick. |
| Ç12 | Oturum klasörü | OA §1 tablosu (`25bf74cd`): "klasör = worktree" | OA §0.13 (`18f3d01e`, daha yeni): oturumlar ana depoda açılır, klasör değişmez | **§0.13 (ana depo).** | Daha yeni commit. 06.10'da worktree klasöründe açılmış oturumlar KADRO ajanlarını göremez; yeniden açılıp açılmayacakları BELİRSİZ, C-00 kararı gerekli. |
| Ç13 | Aktif ajan dosyası biçimi | 30.09: aktif ajan başına satır (`subagent_type · model · görev · başladı`), biten silinir, yoksa "aktif ajan yok — …" | OA §0.3 (06.10): `gg.aa SS:DD · rol · opus-5-5 · görev · dal · durum`, başta/sonda/teslimde | **06.10 biçimi.** | Daha yeni C-00 kuralı. |
| Ç14 | Amend | m:wp-076-durumu ders: "WIP commit bildirilmediyse amend serbest" | PSP §3.5.47, M4R1 §4.3, OA §0.5 (06.10): amend yok (mutlak) | **Amend hiç yok.** | Daha yeni ve mutlak kural. |
| Ç15 | Disk kapısı eşiği | m:wp-031-durumu (17.09): boş < 10 GB ise build yok | M4R1 §4.4 (24.09), m:kural-disk-kapisi (30.09): ≥ 15 GB + swap ≤ 12 GB | **15 GB / 12 GB.** | Daha yeni ve daha sıkı. |
| Ç16 | Blender thread sayısı | m:makine-slot-kurali (16.09), M4R1 §4.4: `--threads 6` | DEV §6.2 komut kalıbı (05.10): `--threads 4` | **4** (en yeni kalıp). | Açık bir kural değil, kalıp; C-00 teyidi önerilir. |
| Ç17 | Sessiz pencere yük eşiği | m:wp-012-durumu: load > 4 geçersiz (15.09); wp064_run preflight ≤ 4,0 | PSP §10 + m:wp-078-r1-kabul (26.09): load1 < 6 | **Genel kural < 6;** ≤ 4,0 yalnız wp064_run aracının kendi parametresi. | Daha yeni C-00 kuralı. |
| Ç18 | Higgsfield erişimi | BRF §11, DEV §12: Higgsfield MCP (ŞART) | OA §0.11, m:higgsfield-cli-kurulum (06.10): CLI, MCP yok | **CLI.** | Daha yeni; ortam gerçeği. |
| Ç19 | Ajan kuralının son cümlesi | PSP §7: "… kritik durumda kullanıcı kararına yükselt." | OA §0.14 (06.10): son cümle yok; §0.2: kararlar C-00'da | **Yükseltme C-00'a; kullanıcıya yalnız §1.4 istisnaları.** | 05.10 tam yetki + 06.10 metni. |
| Ç20 | DEC değiştiren/ürün kararları | PSP §3.4.42, §8: ürün kuralını değiştiren sayı/kural, Unity/URP/paket, veri kaybı riski kullanıcıda | 05.10 tam yetki | **C-00 verir, görselle bildirir.** | En yeni kullanıcı talimatı. Ancak tam yetkinin Unity/URP sürüm değişikliğini (DEC-058/059), gerçek kullanıcı verisine yazmayı ve kullanıcıya bırakılan S-06 runner exit-32 onayını (DEV §7) kapsayıp kapsamadığı BELİRSİZ; C-00 kararı gerekli. |
| Ç21 | Manuel test kiti | HIZLI HAT (15.09): kaldırıldı | OA §0.14 (06.10): oyuncuya dokunan değişiklikte gt kiti üretir | **Kit üretilir.** | Daha yeni. Kitin kim tarafından koşulacağı BELİRSİZ: kullanıcıya soru yok (Ç6), ama PSP §8 "kitler kullanıcıya aittir". |
| Ç22 | S-10 rolü | HIZLI HAT (15.09): yalnız aşama sonu regresyonu | PSP §7.8: bağımsız kabul; OA: S-10 C-00 haber verince (R2 FINAL) | **S-10 = tur sonu FINAL; paket başına bağımsız doğrulama gt ile.** | En yeni metinlerin birleşimi. |
| Ç23 | Rapor teslim biçimi | m:rapor-teslim-sohbetten (12.09): rapor sohbete, dosya değil | AGM/CXS: Codex rapor dosyası; DEV §6.2: C-00 üretim ajanları `C00-*-RAPOR.md` | **Rol bazlı:** Claude oturumları mesajla, Codex ve üretim ajanları dosyayla. | Gerçek bir çelişki değil, kapsam farkı. |
| Ç24 | Yardımcı ajan sayısı | kota modu (25.09): ≤2 / yarı hızda ≤1 / yok | OA §0.14 (06.10): "ajan sayısını önceden sınırlama" | **Sınır yok;** kota eşikleri yalnız C-00 ilan ederse. | Daha yeni. |
| Ç25 | Codex/belge worktree'si | CXS §5 (05.10): `git worktree add K/cx-<kimlik>/worktree …` (tam) | m:codex-r2-kurulum ders (05.10): KADRO'ya tam LFS'li worktree açılmaz, sparse + `GIT_LFS_SKIP_SMUDGE` | **BELİRSİZ — C-00 kararı.** | Aynı gün iki metin. Öneri: belge/araç işinde sparse kalıp. |
| Ç26 | Commit konusu biçimi | PSP §11: 28–29.09'da konvansiyonel önekler (`fix(spawn):`) | Kapı 8 (30.09/05.10) | **Kapı 8 biçimi.** | Araç kapısı ve daha yeni. |
| Ç27 | `--s01` kullanımı | TR (integrate): yalnız `ProjectIntegrityTests.cs` için (`tools/` zaten kapsamda) | BRF §10: "S-01 araçları için `--s01`" | **TR (aracın kendi belgesi).** | BRF ifadesi kısaltma olabilir; davranışın kaynağı araç belgesidir. BELİRSİZ, düşük önem. |
| Ç28 | Konsey biçimi | M4R1 §4.2 (24.09): KADRO konseyi Fable; v2 (24.09): konsey tek Fable çağrısı | OA §0.14 (06.10): konsey kısa karar notu (lider + eş imza), Opus | **06.10.** | Daha yeni. |
| Ç29 | Üretim tarafı | R2P §2 (05.10): tasarım/üretim Codex'te | DEV §0.6, BRF §13 (05.10 akşam): Codex kotası 12.10'a kadar bitik, üretim Claude alt ajanlarında | **12.10'a kadar Claude alt ajanları;** sonra R2P modeli. | Geçici durum. |
| Ç30 | Hızlı hat / hız modu | m:hizli-hat-direktifi (15–17.09) "tüm oturumlara hemen geçerli" | PSP §7 (29.09): hat modları; "M4-R1 paketleri dilim başına commit + AD turu ile yürür" | **Mod; varsayılan değil** (C-00 ilan ederse). | Daha yeni sistem metni. |
| Ç31 | Merge-base mi git cherry mi | m:wp-078-r1-kabul ders: "ff demeden önce merge-base --is-ancestor" | m:ders-cherry-pick-entegrasyon-kontrolu (01.10): "main'de mi" için `git cherry` | **İkisi farklı soruyu yanıtlar:** ff-uygunluğu için is-ancestor, "main'de mi" için git cherry. | Netleştirme. |
| Ç32 | Ultracode ↔ konsey | m:model-politikasi-v3/v4 (30.09): tasarım/kritik kararda yazar + 2–3 çürütücü | OA §0.14 (06.10): konsey kısa karar notu | **BELİRSİZ — C-00 kararı.** | 06.10 metni ultracode'u ne iptal ediyor ne de anıyor. |
| Ç33 | Prompt kayıt yeri | PSP §3.4.41: paket promptu `planning/work-packages/`'a birebir | R2 promptları yalnız `K/c-00/R2/prompts/` ve `docs/c00/prompts/` (WP-081…089); main'de `planning/work-packages/WP-08x` yok (06.10 doğrulandı) | **BELİRSİZ — C-00 kararı.** | Kural uygulanmamış görünüyor; ya `docs/c00/prompts/` kabul edilecek ya da planning'e kopyalanacak. (§3.32 ve §6.1 buraya bağlanır.) |

**C-00 kararı gereken açıklar (özet):** Ç2 (06.10 kapılarının kullanıcı dayanağı), Ç5 ("1 aday" kuralı), Ç7 (cleanup `--apply` ↔ kalıcı silme), Ç11 (D8 göçünde `merge main`), Ç12 (worktree'de açılmış oturumlar KADRO ajanlarını göremez), Ç16 (Blender thread), Ç20 (tam yetki ↔ Unity/URP/paket, gerçek profile yazma, S-06 runner onayı), Ç21 (manuel kiti kim koşar), Ç25 (Codex worktree kalıbı), Ç27 (`--s01`), Ç32 (ultracode), Ç33 (R2 prompt kaydı), §7.7 (S-04 ve P-xx takımları yazılı değil).

---

## 13. C-00 kararları (06.10) — §12'deki BELİRSİZ maddelerin çözümü

Kullanıcının 06.10 tam yetkisiyle (§1.19) C-00 verdi; kitabın bağlayıcı parçasıdır.

| # | Karar |
|---|---|
| Ç2 | KADRO kapıları (konsey notu, görsel dilimde AD turu, ses kapısı, teslim öncesi gt, manuel kit) **bağlayıcıdır.** Dayanak: kullanıcı 06.10 "kadro sistemini efektif kullandığımızdan emin ol", "gerekli hiçbir şeyi atlamadan… hem kadro ajanlarını hem alt ajanları kullanarak". HIZLI HAT / kota modu yalnız C-00 açıkça ilan ederse. |
| Ç5 | "1 aday (gerekirse 2)" kuralı **düştü** (kredi sınırsız, 05.10). Aday sayısı AD kararının gerektirdiği kadardır; seçenek paketleri çok şıklıdır (§8.12). Ledger ve bakiye/işlem doğrulaması sürer. |
| Ç7 | `cleanup-merged.sh --apply` **kullanılmaz** (kalıcı siler; kalıcı silme kullanıcınındır). Worktree temizliği: önce `apfs_real_gain.py`, sonra klasör `mv` ile `~/.Trash/`'e ve `git worktree prune` (dal ve commit'ler korunur). Build ve büyük klasörler önce harici arşive (kopyala → SHA256 → Çöp). Çöp'ü kullanıcı boşaltır. `git worktree remove` kullanılmaz. |
| Ç11 | **main dala merge edilmez.** `git merge --ff-only main` (merge commit üretmez) serbesttir; ff olmuyorsa main'den yeni dal açılır ve kendi commit'leri cherry-pick edilir (rebase yok). D8 göç notundaki `git -C <worktree> merge main` satırı D9 duyurusunda bu kurala göre düzeltilir (S-01). |
| Ç12 | Oturumlar **ana depoda** açılır (KADRO ajanları için); OA §1 tablosundaki "Klasör" sütunu çalışma worktree'sidir, oturum klasörü değildir (OA 06.10'da düzeltildi). 06.10'da açılan yedi oturum ana depodadır. |
| Ç16 | Blender `--threads 4` (makine paylaşımlı). |
| Ç20 | Tam yetki C-00'a ürün içi sayı ve teknik kararları verir; C-00 karar verir ve görselle bildirir. Ancak: (a) Unity/URP sürümü ve paket ekleme/yükseltme R2'de **yapılmaz** (ihtiyaç doğarsa C-00 karar verir, gerekçeli DEC ile); (b) gerçek kullanıcı verisine yazmak **hiçbir yetkiyle** yapılmaz (§2; yalnız C-00'ın kanıtlı geri yüklemesi); (c) S-06 runner exit-32 değişikliğini S-06'nın izin sistemi reddetti → yapılmadı; yerine §2.23 (latch'i C-00 kaldırır, uzun zincir Player slotunu tutar). |
| Ç21 | Manuel kitler üretilir ve kanıtla saklanır; deterministik eşlerini gt/S-10 koşar. Kullanıcıya tek tek sorulmaz: kitler R2 FINAL'de tek görselli sayfada toplanır; kullanıcı isterse koşar, istemezse "beyan + deterministik eş" sınıfı yazılır (§1.17). Kulak/göz kararları (S6 dinleme, görsel hedef) yine kullanıcıya görselli sayfayla sunulur ama süreç beklemez. |
| Ç25 | Belge/araç işleri için sparse worktree (`--no-checkout` + `sparse-checkout` docs/tools/planning + `GIT_LFS_SKIP_SMUDGE=1`); Unity gereken işlerde tam worktree (Library komşudan klonlanır). Codex 12.10'da dönünce aynı kural. |
| Ç27 | `--s01` yalnız commit `ProjectIntegrityTests.cs`'e dokunuyorsa verilir (integrate.sh belgesi); S-01'in diğer araç teslimleri `tools/` kapsamındadır. |
| Ç32 | Konsey notu = lider + eş imza; karar geri dönüşü zor ya da kritikse 1–2 çürütücü (refuter) ajan eklenir (eski "ultracode" kalıbı, Agent aracıyla). Workflow aracı yalnız kullanıcının kendi açık isteğiyle. |
| Ç33 | R2 paket promptlarının kanonik yeri `docs/c00/prompts/` (depo) + kopyası `K/c-00/R2/prompts/`. Eksik R2 promptları (WP-082, WP-084, WP-086, WP-090, WP-091) C-00 tarafından yazılır. `planning/work-packages/` M4-R1 ve öncesi için geçerli kalır. |
| §7.7 | **S-04:** game-designer (lider), product-analyst, game-test-engineer, gt. **Üretim ajanları (AP-xx):** yürütme `general-purpose` (MCP + Bash); inceleme varlık türüne göre: görsel → art-director (AD) + 3d-modeller / blender-specialist / concept-artist; animasyon → character-animator + animation-specialist; ses → sound-designer + sound-engineer; teslim öncesi gt. |
