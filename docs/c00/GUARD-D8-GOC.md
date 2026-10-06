# Guard D8 göç notu — oturumlar için (v2 · geçerli · C-00 06.10 onayı; D8 main'de)

> Geçerli kopya bu dosya; KADRO kopyası `K/c-00/R2/GUARD-D8-GOC.md` aynı metindir. Metni S-01 technical-writer yazdı (06.10, güncelleme 3). Geçişi C-00 06.10 07:16'da (+03) yaptı.
>
> **Kod kaynağı:** main `a9429acd`; kanonik guard sürücüsü de `a9429acd`'dedir. Metin önce dal `s01-g4-d8-queue-r2`'nin çalışma ağacından okundu (S-01 scratch `dev/tools/`, 06.10 01:53). Doğrulama koşusu o kopyanın bir anlık görüntüsüne karşı yapıldı; dosya sha256 değerleri `v2-sonuc.log`'un başında.
> - O ağaç `0b6e86fc`'yi kapsıyordu: D8 `d707b999` + kuyruk `225d0359` + 06.10 inceleme düzeltmeleri.
> - Üstüne iki ek getiriyordu: waiter'sız biletin budanması ve main-ci/rc.sh'de `& wait`. İkisi de main'e kuyruk commit'i `2a891ec8` ile geldi.
> - Bakılan dosyalar: `tools/userdata-guard.sh`, `tools/userdata_guard_io.py` (`ledger_begin`, `gone`, `retire_ticket`, `ledger_update`), `tools/guard_freshness.py`, `tools/main-ci.sh`, `tools/qa/rc.sh`, `tools/README.md`.
> - main'den: `tools/dd-env.sh`, `tools/slot.sh`, `tools/test.sh`, `tools/qa/oos_rule.py`, `tools/player-run.sh`.
>
> **Kurallar:** `docs/c00/SABIT-KURALLAR-R2-2026-10-06.md` §2.2–§2.14, §2.22, §2.23, §4.5, §13 Ç11 · `K/c-00/G4-GUARD-KARARLARI-2026-09-30.md` (Q4, Q5, Q8) · C-00 06.10 (`DD_GUARD_QUEUE_WAIT` ayrı değişken) · §5 için `K/wp-089/evidence/d8-queue-gitsafety-0610.md` §4.
>
> **Doğrulama:** §4 iskeleti bu kod kopyasına karşı guard'ın test düzeneğinde koşturuldu (sandbox; Unity ve Player yok). Betik: `K/wp-089/evidence/d9-dok/goc-iskelet-dogrula.py`. Sonuç: `v2-sonuc.log`. Koşulan senaryolar:
> - normal koşu; INT, TERM ve HUP sinyalleri;
> - kuyruk satırı ve `class=QUEUE_TIMEOUT`;
> - yanlış sıra 64; 32 ve `oos_rule`;
> - kuyrukta Ctrl-C; kuyrukta ölen çağıran 65;
> - tam saniye 64; yalnız player kapsamında geçen istisna;
> - `queue/done/`; kuyrukta yalnız betiğe gelen TERM; waiter'sız ve bozuk biletler.
>
> Bilgi satırı 8z (zombi çağıran, yani ölmüş ama ebeveyni tarafından henüz biçilmemiş çağıran) koşu sırasında açıktı. S-01 bunu kuyruk commit'i `9daa0f15`'te (main `2a891ec8`) kapattı: `gone()` `Z` durumunu ölü sayıyor ve bekleme döngüsü guard'ın ebeveynine bakıyor. Göç notunun metnini etkilemez.
>
> **Commit'ler:** D8 `2daa78fe` (kaynak `d707b999`) → kuyruk `2a891ec8` (kaynak `9daa0f15`) → gt düzeltmeleri `a9429acd` (kaynak `ef98f33b`). N1: iç içe pencerenin başka alandaki ata penceresi. N2: S10'dan önce çağıran denetimi. N3: bu §3 metni. Kod iddiaları son commit'e (`a9429acd`) karşı geçerlidir.

## Değişiklikler (v1 → v2)

Etiketler:
- **[kod]** `0b6e86fc` davranışı.
- **[kod r2]** `s01-g4-d8-queue-r2` çalışma ağacının ekleri (waiter'sız bilet, `& wait`).
- **[kural]** kural kitabı.
- **[git-safety]** lider ajanın §4 eklemeleri.
- **[denetim]** ilk denetim (`225d0359`'a karşı).

1. **Giriş [denetim].** Alanlar tabloyla tanımlandı: editor alanı = Editor'ün veri klasörü + Editor PlayerPrefs; player alanı = bundle klasörü + player PlayerPrefs. Hangi `--scope`'un neyi koruduğu yazıldı.
2. **§1 [kural Ç11].** `git merge main` satırı kaldırıldı. Yalnız `merge --ff-only main` kullanılır. ff olmuyorsa main'den yeni dal açılır ve kendi commit'leri `git cherry` ile bulunup cherry-pick edilir. Merge ve rebase yok.
3. **§1 [denetim + kod].**
   - Komutlar `<worktree>` ile mutlak yol kullanıyor.
   - Worktree'nin main'i içerdiği ayrıca kontrol ediliyor (`merge-base --is-ancestor`).
   - `guard_freshness` worktree'yi değil, sürücüyü kontrol eder. `0b6e86fc`'den beri `userdata_guard_io.py`'yi de karşılaştırır.
4. **§1 [denetim, kural §2.3].** Guard yalnız `"$DD_GUARD_CANON"` ile çağrılır. Kanon dışı yollar §5 Grup D'de.
5. **§2, 64 [kod].**
   - Kilit sırası istisnası artık **yalnız `--scope player`** için geçerli. editor ve all kapsamında unity tutarken her durumda 64 döner.
   - Yeni 64 nedeni: bekleme değişkeni tam saniye değil.
6. **§2, 65 [kod, gt r2].** Yeni neden: çağıran, snapshot bitmeden ölürse pencere açılmaz (`the caller pid … is gone — no window opened`). Bu kural kuyrukta, player slotu beklemesinde, S7 profil kilidi beklemesinde, kopya sırasında ve çağıran zombiyken geçerlidir.
7. **§2, 75 [kod].**
   - Kuyruk zaman aşımı satırı `… still after Ns (75) class=QUEUE_TIMEOUT` oldu. main-ci bu 75'i yeniden denemez.
   - Üç bekleme sınırı değişken adıyla yazıldı.
   - Snapshot 75'te yeniden deneme yeni dizinle yapılır (kural §2.6).
8. **§2, 32 [kural §2.7, §2.23].** Kendi koşucunuzda her 32'de `oos_rule.py` koşulur. rc 0 → bir uyarı satırıyla devam; değilse DUR. player kapsamında 32, editor alanının değiştiği anlamına gelir ve `oos_rule` bunu her zaman reddeder.
9. **§2, 30/31 restore [denetim].**
   - `retry=same-dir` (INTERRUPTED) olan 30 bir kez daha denenir.
   - Doğrulanamayan alan dirty kalır ve herkes o alanda 65 alır.
10. **§2, kuyruk satırı [kod].**
    - Tahmin biçimleri: `~N min` · `overdue: open X min, typical Y min[ — then ~Z min for the K ahead]` · `unknown (no closed window over these areas yet)`.
    - Engel restore ediliyorsa restore eden sürecin pid'i yazılır.
    - Yaş, pencerenin açıldığı andan sayılır.
    - Başlama zamanı bilinmeyen bekleyici için satıra `(no queue place yet: … — next poll)` eklenir.
    - Satır önce bir kez, sonra 60 saniyede bir yazılır.
11. **§2, 65 adımı [denetim; C-00'a iletildi].** `next:` komutu kanonik yolla çalıştırılır. Pencere başka bir oturumunsa önce o oturuma ve C-00'a haber verilir. **(C-00 06.10 kararı: kural §13 Ç-D8.)**
12. **§3 [kod + denetim].**
    - Canlılık K3 kuralıyla ölçülür: boş `ps` yanıtı canlı süreci düşürmez.
    - Sahip kuyrukta ölürse pencere açılmaz.
    - Ajan aracının süre sınırı ve WINDOW_LOST yazıldı.
    - Yanlış kalıplara `( cd … && guard … )` eklendi (kural §2.2).
13. **§4 [denetim + kod + kural].** İskelet yeniden yazıldı. v1'deki sorunlar ve düzeltmeleri:
    - `test.sh` ana depo yolundan çağrılıyordu → artık worktree'den.
    - `dd_guard_canon_check` yoktu → eklendi.
    - HUP tuzağı yoktu → eklendi.
    - Restore'dan önce `trap -` vardı → restore sinyaller yok sayılarak koşuyor.
    - 75'te DUR deniyordu → aynı dizinle en çok 3 deneme.
    - 32 koşulsuz kabul ediliyordu → `oos_rule` ve uyarı satırı.
    - Dizin adı saniye çözünürlüğündeydi → `-$$` eklendi.
    - **[kod r2]** Snapshot önplanda çağrılıyordu; kuyrukta beklerken betiğe gelen TERM, guard dönene kadar (≤ 3600 s) bekliyordu → snapshot artık `& wait` ile çağrılıyor. Sinyal gelince bekleyen guard'a TERM gönderiliyor. Pencere yine de açıldıysa (`before.txt`) restore ediliyor. Bu, main-ci ve rc.sh ile aynı kalıp.

    Eklenen kurallar:
    - Makine çapında sıra: player slotu → pencere → unity slotu.
    - Editor kapsamlı pencere içinde player slotu alınmaz. Uzun zincir player slotunu tutacaksa onu snapshot'tan önce alır.
    - ug dizini silinmez ve yeniden kullanılmaz [git-safety SHOULD-5].
    - Kalıcı kilit dosyası (GUARD-STOP) yok [kural §2.23].
14. **§5 [git-safety §4].** Liste dört gruba ayrıldı:
    - (A) Zaten 64 alanlar (all kapsamı, 30.09'dan beri).
    - (B) Rollout'ta yeni 64 alacaklar (editor kapsamı).
    - (C) Sınıflanmamış eski kanıt betikleri.
    - (D) Kanon dışı guard yolu kullanan 33 betik.

    Eklenenler: `wp-069b5/tools/chain3.sh` (06.10; ayrıca `rm -rf …-ug` yapıyor), `f41-pm.sh` notu, wp-083 dolaylı çağıran sayısı (~52, 06.10). Yanlış pozitifler (`wp-019r2/tools/wp073-*`, `wp-068/tools/*-caps.sh`, `wp068-r1a6/r1a8.sh`) listeye alınmadı. "Doğru sıra" örneklerinde `wp-088/run-r1.sh` için not var.
15. **§6 [kod + denetim].**
    - Sıralama alan bazında yapılır.
    - İç içe pencere istisnası yazıldı; atanın tuttuğu alanlardaki yabancı biletler de istisnaya dahil.
    - main-ci beklerken player slotunu ve lease'i tutar; QUEUE_TIMEOUT'ta yeniden denemez.
    - Bekleme değerleri tam saniye olmalı.
    - **"Hiçbir araç silmez" artık doğru:** biletler `queue/done/`'a taşınır. Klasör listesine `queue/done/` eklendi.
    - `closed` durumu ve `closed/`'a taşıma tek adımda yapılır.
    - Kaydın `utc` alanı pencerenin açılış anıdır.
    - `resolution.json`'ın kaynağı yazıldı (kural §2.14, G4 Q4); oturumlar deftere dokunmaz.
16. **§6 [kod r2] Bilet budama.**
    - `waiter` alanı olmayan bilet budanır ve `queue/done/…reaped.json`'a taşınır. Sahibi canlı olsa da sırayı tutmaz. Guard artık böyle bilet yazmaz; bu yalnız eski ya da elle yazılmış biletlerde görülür.
    - Bozuk JSON içeren bilet `…unreadable.json`'a taşınır.
    - `<ns>-<id>.json` biçiminde olmayan dosyalara dokunulmaz.
17. **§3, §4, §6 [kod r2] Arka planda snapshot.** main-ci ve rc.sh snapshot'ı arka planda (`& wait`) çağırıyor. Bu kalıp sahiplik kuralını bozmaz: guard'ın ebeveyni yine bekleyen kabuktur. §3'teki "arka planda alıp çıkmak" yanlışıyla karıştırılmaması için bu fark §3'e yazıldı.

---

# Guard D8 göç notu — oturumlar için (S-01 · C-00 05.10, v2 06.10 · rollout D9'da C-00 ilan eder)

D8 ile `userdata-guard.sh` bir **pencere defteri** tutar (`~/.dd-userdata-guard/windows/`). Aynı alanı koruyan iki pencereyi **aynı anda açmaz**. Bekleyen pencereler sıraya girer: ilk gelen önce açılır (FIFO kuyruğu). Alanlar:

| Alan | Klasör | PlayerPrefs |
|---|---|---|
| editor | `~/Library/Application Support/Deaths Demolition/Deaths Demolition - Playable Concept/` | `unity.Deaths Demolition.Deaths Demolition - Playable Concept` |
| player | `~/Library/Application Support/com.Deaths-Demolition.Deaths-Demolition---Playable-Concept/` | `com.Deaths-Demolition.Deaths-Demolition---Playable-Concept` |

Kapsamlar:
- `--scope editor`: yalnız editor alanını korur.
- `--scope player`: yalnız player alanını korur.
- `--scope all` (varsayılan): iki alanı birden korur.

Bu not rollout günü geçerli olur; o güne kadar hiçbir şey değişmez (kural §2.22).

## 1. Tazeleme (rollout duyurusundan sonra, bir kez)

1. **Sürücüyü C-00 taşır.** C-00 iki commit'i art arda entegre eder. Sonra kanonik guard sürücüsünü sessiz bir anda, bir kez main'e taşır.
2. **Worktree'nizi main'e getirin.** Önce kendi işinizi commit edin (`git add -A` yok). Sonra:
   ```
   git -C <worktree> merge --ff-only main
   ```
   ff olmuyorsa dalınız main'den ayrılmıştır. Bu durumda main'e merge etmeyin ve rebase yapmayın (kural §13 Ç11). main'den yeni bir dal açın ve kendi commit'lerinizi taşıyın:
   ```
   git -C <worktree> switch -c <yeni-dal> main
   git -C <worktree> cherry -v main <eski-dal>     # "+" ile başlayan satırlar main'de olmayan commit'lerinizdir
   git -C <worktree> cherry-pick <sha> …            # yalnız "+" satırları, eskiden yeniye
   ```
   Yeni dalın adı kural §3.3'e uyar.
3. **Kontrol edin.** İki komut da `0` yazmalı:
   ```
   git -C <worktree> merge-base --is-ancestor main HEAD; echo $?
   python3 <worktree>/tools/guard_freshness.py "$HOME/Desktop/KADRO/context/projects/deaths-demolition/wp-079/driver/tools/userdata-guard.sh"; echo $?
   ```
   - Birinci komut, worktree'nizin main'i içerdiğini gösterir.
   - İkinci komut, sürücüdeki `userdata-guard.sh`, `userdata_attrib.py` ve `userdata_guard_io.py`'nin main ile aynı olduğunu gösterir. 64 verirse sürücü henüz taşınmamıştır; C-00'a bildirin.
4. **KADRO'daki `tools/slot.sh` kopyası değişmez.** D8 `slot.sh`'ye dokunmaz.

**Kural: guard'ı yalnız `"$DD_GUARD_CANON"` ile çağırın** (kural §2.3, §2.11). Önce `source <worktree>/tools/dd-env.sh` çalıştırın. Değişkenin gösterdiği yol: `~/Desktop/KADRO/context/projects/deaths-demolition/wp-079/driver/tools/userdata-guard.sh`. Şu kopyalar kanonik değildir:
- Ana depo yolu (`/Users/Erdem/Documents/Deaths Demolition/tools/userdata-guard.sh`). Bu yol yeni guard'ı sürücüden önce, entegrasyon anında alır.
- Worktree'nizin kendi kopyası ve `wp-0xx/tools/userdata.sh` kopyaları. D8 öncesi bir kopya deftere yazmaz; açtığı pencere kuyrukta görünmez.

## 2. Çıkış kodları — ne demek, ne yapılır

| Kod | Nerede | Anlamı | Ne yapılır |
|---|---|---|---|
| 0 | snapshot / restore | Tamam. | — |
| **32** | restore | Kendi alanınız geri yüklendi ve doğrulandı (`verified=1`). Öbür alan değişti (`out-of-scope-changed.txt`); o değişiklik geri alınmaz. | **main-ci:** uyarı. **rc.sh:** editor aşamasında yalnız 30.09 kuralı tutarsa geçer; player aşamasında FAIL. **Kendi koşucunuz (kural §2.7, §2.23):** `python3 <worktree>/tools/qa/oos_rule.py <UG>` koşun. rc 0 → bir uyarı satırıyla devam. rc 0 değil → DURUN, sıfırdan farklı çıkın ve C-00'a bildirin. player kapsamında 32 = editor alanı değişti; `oos_rule` bunu her zaman reddeder. |
| **64** | snapshot | **Kilit sırası yanlış.** Snapshot'ı çağıran kabuk ya da atalarından biri unity slotu tutuyor. editor ve all kapsamında **her durumda** 64. Tek istisna `--scope player`: aynı zincirde player slotu da tutuluyorsa sıra doğru sayılır. Mesaj (stderr): `userdata-guard: lock order — take the guard snapshot (the window; player scope: the player slot) BEFORE the Unity slot` ve `userdata-guard: SLOT unity held by … — fix the order: release it, snapshot, then take the Unity slot again (exit 64)…` | Sırayı düzeltin (§4): player slotu (gerekiyorsa) → snapshot → unity. |
| 64 | snapshot | Başka nedenler: kullanım hatası; dizinde zaten snapshot var (`… exists — one snapshot per directory (64)`); dizin symlink; `HOME` ya da `DD_GUARD_ROOT` sorunu. Ayrıca bir bekleme değişkeni tam saniye değilse: `userdata-guard: DD_GUARD_QUEUE_WAIT=1h is not whole seconds — refused, nothing written (64)`. Bu kontrol `DD_GUARD_WAIT`, `DD_GUARD_QUEUE_WAIT` ve `DD_GUARD_SLOT_WAIT` için yapılır. Restore'da aynı durum yalnız uyarıdır ve varsayılan değer kullanılır. | Her koşuda yeni bir dizin kullanın (§4). Değeri saniye olarak verin (`3600`; `1h` değil). Diğer durumlarda DURUN ve C-00'a bildirin. |
| 64 | restore | Bağ yok, v1 izi var ya da snapshot başka bir kökte alınmış. Dizin defterde kayıtlıysa pencere **SNAPSHOT_MISSING** ile dirty işaretlenir. | DURUN ve C-00'a bildirin. |
| 30 | snapshot (player/all) | Bir Deaths Demolition Player'ı çalışıyor. | Player kapanınca yeniden deneyin. |
| 31 | snapshot (player/all) | Player ekran değerleri bilinen-iyi değerlerden sapmış (`known-good-deviation.txt`). Snapshot alınmaz. | DURUN ve C-00'a bildirin. |
| **65** | snapshot | Üç durumdan biri: (1) Defterde alanınızla örtüşen bir pencere **dirty**. (2) Örtüşen bir pencerenin sahibi (veya restore eden süreç) ölü, yani pencere terk edilmiş. Mesaj: `userdata-guard: REFUSED — ledger window <id>: DIR=<dizin> state=<durum> owner=<pid> alive=<0/1> age=<n>min next: "userdata-guard.sh restore <dizin> --scope <s>" — only once the run that opened it has ended (if it does not converge: C-00) (65)`. (3) Korunan dosya kümesinde sorun var (symlink, büyük/küçük harf farkı, okunamayan plist). | (1) ve (2): o pencereyi açan koşunun bittiğinden emin olun, sonra `"$DD_GUARD_CANON" restore <dizin> --scope <s>` çalıştırın. Pencere başka bir oturuma aitse önce o oturuma ve C-00'a haber verin. Yakınsamazsa C-00'a bildirin. (3): C-00'a bildirin. |
| 65 | snapshot | **Çağıranınız öldü.** Snapshot'ı çağıran süreç snapshot bitmeden öldü: kuyrukta, player slotu beklerken, profil kilidi beklerken (S7) ya da kopya sırasında. Zombi olarak bekleyen çağıran da ölü sayılır. Pencere açılmaz; o anda kayıt yazılmışsa `aborted/`'a taşınır. Geri yüklenecek bir şey yoktur. Guard satırı iki biçimden biridir: kuyrukta `userdata-guard: the caller pid <pid> is gone — no window opened for it, queue left (65)`, S10 commit'inden hemen önce `… is gone — no window opened for it (65)`. Yardımcının satırı: `… is gone — no window opened for it (DIR=…); nothing to restore`. | Betiğinizin neden öldüğüne bakın (§3). Yeni bir dizinle yeniden başlatın. |
| **75** | snapshot | Bekleme süresi doldu. (1) Pencere kuyruğu: `DD_GUARD_QUEUE_WAIT` (varsayılan 3600 s). Satır: `userdata-guard: queue position … — still after 3600s (75) class=QUEUE_TIMEOUT`. main-ci bu 75'i yeniden denemez. (2) Player slotu: `DD_GUARD_SLOT_WAIT` (4800 s, player/all). (3) Profil kilidi: `DD_GUARD_WAIT` (300 s). 75 iki durumda daha döner: kopyalama sırasında veri 3 kez değişti, ya da defter yardımcısı art arda 3 kez başarısız oldu. Kısmi dosyalar `<UG>/failed-<utc>/` klasörüne taşınır. | Sonra **yeni bir dizinle** yeniden deneyin (kural §2.6). |
| 75 | restore | BUSY (profil kilidi `DD_GUARD_WAIT` içinde bırakılmadı ya da bir Player çalışıyor), LOCKED (aynı dizinin başka bir restore'u sürüyor) ya da erken RACED. Özet satırında `retry=same-dir` yazar. | Restore'u **aynı dizinle** tekrarlayın. BUSY ya da RACED durumunda alan doğrulanana kadar kayıt dirty kalır; o sürede o alanın yeni snapshot'ları 65 alır. |
| 30 / 31 | restore | 30: yazma ya da doğrulama sorunu (STALE, MISMATCH, WINDOW_LOST, FOREIGN_WRITER…). 31: bilinen-iyi değerlerden sapma. | DURUN ve C-00'a bildirin. İstisna: özet satırında `retry=same-dir` varsa (INTERRUPTED: restore beklerken sinyal geldi) aynı dizinle bir kez daha restore deneyin; yine 30 ise DURUN. Doğrulanamayan alan dirty kalır ve herkes o alanda 65 alır, bu yüzden hemen bildirin. STALE'de kullanıcının sonraki kaydı korunur ve kendiliğinden restore yapılmaz (kural §2.8). |
| 129 / 130 / 143 | snapshot | Snapshot HUP, INT ya da TERM ile kesildi. Snapshot oluşmaz. Kısmi dosyalar `failed-<utc>/` klasörüne taşınır. Kuyruktaki bilet `queue/done/…exit<kod>.json`'a taşınır. | — |

### Kuyruk satırı

Snapshot kuyrukta beklerken guard bir satır yazar: önce bir kez, sonra 60 saniyede bir. Satır guard'ın stdout'una ve `<UG>/timeline.txt`'e gider. main-ci stdout'u `<UG>-snapshot.out` dosyasına yönlendirir.

```
userdata-guard: waiting in the window queue — queue position 1 — ahead: <dizin> (pid 35265, open, 12 min) — estimate ~31 min
```

- **`queue position n`:** önünüzdeki bekleyen bilet sayısının bir fazlası. Açık pencere bu sayıya katılmaz. `1` görüyorsanız sıradaki sizsiniz.
- **`ahead:`:** önünüzdeki engel.
  - Engel açık bir pencereyse parantezde `(pid, durum, yaş)` yazar. Yaş, pencerenin açıldığı andan sayılır. Durum `restoring` ise pid, restore eden sürecindir.
  - Engel bekleyen bir biletse `(pid …, queued n min)` yazar.
- **`estimate`:** aynı alanda kapanmış son en çok 20 pencerenin ortanca süresinden hesaplanır. Üç biçimi var:
  - `~N min`.
  - `overdue: open X min, typical Y min[ — then ~Z min for the K ahead]`: engel her zamankinden uzun sürüyor.
  - `unknown (no closed window over these areas yet)`: rollout'tan sonraki ilk günlerde `closed/` boş olduğu için bu yazar.
- **Satır sonunda `(no queue place yet: the waiter's start time is unknown — next poll)` varsa:** bekleyen guard'ın başlama zamanı o an okunamadı (K3). Bilet yazılmaz; guard sıra yerini bir sonraki yoklamada alır.

## 3. Sahip ömrü kuralı (bütün kapsamlar)

Pencerenin **sahibi**, snapshot'ı çağıran süreçtir (guard'ın ebeveyni; pid ve başlama zamanıyla kaydedilir). Sahip restore'a kadar **yaşamalıdır** (kural §2.2).

**Canlılık.** Ölçüm K3 kuralıyla yapılır; kural `slot.sh` ile aynıdır, yalnız zombi maddesi ektir. Bir süreç şu üç durumda ölü sayılır:
- `kill -0` başarısız olursa;
- iki başlangıç zamanı da biliniyor ve farklıysa (pid yeniden kullanılmış);
- süreç zombiyse: çıkmış ama ebeveyni onu henüz biçmemiş (`ps` durumu `Z`).

Boş bir `ps` yanıtı canlı bir süreci düşürmez.

Guard ayrıca kendi çağıranını iki yerde denetler: kuyrukta bekleme döngüsünün her turunda ve S10 commit'inden hemen önce. Çağıran ancak şu iki koşul birlikte tutarsa yerinde sayılır:
- `kill -0` yanıt veriyor;
- guard hâlâ onun çocuğu.

Çıkan bir çağıranın çocukları hemen launchd'ye geçer; çağıran zombi olarak beklerken de bu böyledir. Çağıran yerinde değilse guard pencere açmaz ve 65 ile biter; o anda kayıt yazılmışsa kayıt `aborted/`'a taşınır.

**Sahip ölürse ne olur:**
- Açık pencere "terk edilmiş" sayılır. Diğer oturumlar o alan için 65 alır.
- Pencere player ya da all kapsamlıysa (player slotu makbuzu varsa) restore yine yapılır, ama sonuç WINDOW_LOST (30) olur.
- Sahip, snapshot bitmeden ölürse pencere hiç açılmaz ve guard 65 ile biter (§2). Bu, kuyrukta, player slotu beklemesinde, profil kilidi beklemesinde (S7) ve kopya sırasında geçerlidir.

**Doğru:** tek betik. `snapshot → iş → restore` aynı kabukta çalışır (§4).

Snapshot'ı aynı kabukta `"$DD_GUARD_CANON" snapshot … & SNAP=$!; wait $SNAP` biçiminde arka planda çağırmak da doğrudur. main-ci, rc.sh ve §4 iskeleti böyle yapar. Guard'ın ebeveyni yine bekleyen bu kabuktur, sahip yaşar.

**Yanlış:**
- Snapshot'ı ayrı bir komut ya da araç çağrısıyla alıp çıkmak. Örnek: `zsh -c 'guard snapshot ug'`, sonra ayrı bir çağrıda Unity, sonra ayrı bir çağrıda restore. Çağrı bitince sahip ölür.
- `( guard snapshot ug & )` gibi bir alt kabukta arka plana atıp çıkmak. Alt kabuk hemen biter, sahip ölür.
- `( cd … && guard … )` alt kabuğu.
- `timeout guard snapshot …`.

**Ajan aracının süre sınırı.** Aracın ön plan komut süre sınırı (Claude Code'da en çok 10 dk) uzun bir koşuyu sonlandırabilir. O zaman tuzak çalışmayabilir ve pencere terk edilir. Uzun koşuları arka planda başlatın (Claude Code: `run_in_background`). Betik yine `snapshot → iş → restore` adımlarını kendi içinde yapar.

## 4. Doğru kilit sırasıyla koşucu iskeleti

**Makine çapında sıra:**

lease (yalnız CI) → **player slotu → pencere (snapshot, kuyruk) → unity slotu** → profil kilidi

D9'dan sonra bu sıra, kural §4.5'teki "player → unity" kuralının yerine geçer (kural §2.22, Ç10).

```zsh
#!/bin/zsh
# Sıra: pencere (guard snapshot) → unity → iş → unity bırakılır → restore. Hepsi BU kabukta; kabuk restore'a kadar yaşar.
WT="<worktree>"                                              # main'e getirilmiş worktree'niz (§1)
source "$WT/tools/dd-env.sh"                                 # DD_GUARD_CANON, DD_SLOT, dd_guard_canon_check
dd_guard_canon_check || exit $?                              # kanonik guard yok / bayat ya da DD_GUARD_ROOT dolu → 64
UG="<kanıt-dizini>/ug-$(date -u +%Y%m%dT%H%M%SZ)-$$"         # her koşuya yeni dizin; ug dizini silinmez, yeniden kullanılmaz
SNAP=""                                                      # kuyrukta bekleyen guard'ın pid'i
restore() {                                                  # 75 → aynı dizinle en çok 3 deneme (§2)
  local n g
  for n in 1 2 3; do
    "$DD_GUARD_CANON" restore "$UG" --scope editor; g=$?
    [ $g -eq 75 ] || break
    sleep 30
  done
  return $g
}
on_signal() {                                                # kuyruktaki guard'ı durdur; pencere açıldıysa (before.txt) restore
  trap '' INT TERM HUP
  [ -n "$SNAP" ] && { kill -TERM "$SNAP" 2>/dev/null; wait "$SNAP" 2>/dev/null; }
  [ -f "$UG/before.txt" ] && restore
  exit $1
}
trap 'on_signal 130' INT; trap 'on_signal 143' TERM; trap 'on_signal 129' HUP
"$DD_GUARD_CANON" snapshot "$UG" --scope editor & SNAP=$!    # 1) pencere; arka planda, çünkü kuyrukta ≤ 1 saat bekleyebilir
wait $SNAP; S=$?; SNAP=""                                    #    (sinyal wait'i keser, tuzak hemen çalışır)
[ $S -eq 0 ] || exit $S
# 2) Unity: tools/test.sh ve tools/build.sh unity slotunu kendileri alır; onları çağırıyorsanız slot almayın.
#    Unity'yi kendiniz başlatıyorsanız: "$DD_SLOT" acquire unity <etiket> … iş … "$DD_SLOT" release unity <etiket>
"$WT/tools/test.sh" playmode --filter '<…>' --out "<kanıt-dizini>/pm"; RC=$?
trap '' INT TERM HUP                                         # 3) restore'u sinyal kesmesin
restore; G=$?
if [ $G -eq 32 ]; then                                       # kendi alanınız doğrulandı, öbür alan değişti (§2)
  python3 "$WT/tools/qa/oos_rule.py" "$UG" && { echo "UYARI: guard 32 kabul edildi (oos_rule) — $UG"; G=0; }
fi
[ $G -eq 0 ] || { echo "guard restore $G — $UG — C-00'a bildirin"; exit $G; }
exit $RC
```

- **Tuzaklar snapshot'tan önce kurulur, snapshot arka planda (`& wait`) çağrılır.** Kuyrukta beklerken INT, TERM ya da HUP gelirse, sinyal yalnız betiğe gönderilmiş olsa bile tuzak hemen çalışır:
  - Bekleyen guard'a TERM gönderir. Guard kuyruktan çıkar ve biletini `queue/done/…exit143.json`'a taşır. Ctrl-C bütün gruba gittiyse guard sinyali kendisi de yakalamış olabilir; o zaman dosya adı `…exit130.json` olur.
  - Guard pencereyi yine de açtıysa (`before.txt` var) pencere restore edilir.
  - Betik 130, 143 ya da 129 ile çıkar.

  Snapshot önplanda çağrılırsa betiğe gelen TERM, guard dönene kadar (≤ 3600 s) bekler. main-ci ve rc.sh de bu yüzden aynı kalıbı kullanır.
- **Player koşusu.** Player başlatan bir koşu `--scope all` (varsayılan; `--scope` yazmayın) ya da `--scope player` kullanır. Guard player slotunu kendisi alır. Player'ı `tools/player-run.sh` ile başlatın (kural §2.5).
  - **Editor kapsamlı bir pencerenin içinde player slotu alınmaz.** `player-run.sh` de player slotu alır, bu yüzden editor penceresi içinde çağrılmaz.
  - Sebep: player/all kapsamlı bir bekleyici, player slotunu tutarak kuyrukta bekler. Editor penceresi içinde player slotu almak iki taraf arasında karşılıklı bekleme yaratır.
- **Player slotunu tutacak uzun PlayMode zinciri** (kural §2.23). Slotu snapshot'tan **önce** alın, main-ci gibi: `"$DD_SLOT" acquire player <etiket>` → snapshot → … → restore → `"$DD_SLOT" release player <etiket>`.
- **Unity slotunu tutan dış betik.** Dış betik unity slotunu tutup içteki bir `unity.sh` sarmalayıcısında snapshot alıyorsa, slot alımını sarmalayıcının içine, **snapshot'tan sonraya** taşıyın.
- **Kalıcı kilit dosyası yok** (kural §2.23). GUARD-STOP benzeri, elle kaldırılan latch dosyası kullanılmaz. Hata olursa zincir o adımda sıfırdan farklı çıkışla durur ve C-00'a yazılır.

## 5. Bilinen yanlış sıralı ve kanon dışı çağıranlar — S-01 taraması 05.10 + git-safety 06.10

`K` = `~/Desktop/KADRO/context/projects/deaths-demolition`. "U/S" = betikteki unity ve snapshot satırları.

**A. Zaten 64 alıyorlar** (30.09'dan beri, D6a; varsayılan `all` kapsamı). Rollout bunları değiştirmez. Yeniden koşulacaksa §4'e göre düzeltin.
- `K/wp-053d/tools/*-run.sh` (diag hariç): b2b, b2b2, b2c, b2r2, b2r3, b3, b3r2, b4, b4c, b4d, b4d2, b4r2, cired, links
- `K/wp-065b/tools/batch37, batch39, batch42…53, batch-template-guarded.sh`
- `K/wp-076/run.sh` (U11, S12)

**B. Rollout'ta yeni 64 alacaklar** (editor kapsamı).
- `K/wp-069b5/tools/chain.sh` (U33, S41), `chain3.sh` (06.10 yeni; U33, S41), `diag-run.sh` (U9, S11).
  - Bu üç betik ve `K/wp-053d/tools/diag-run.sh`, snapshot'tan önce `rm -rf …-ug` çalıştırıyor. Bu satır kaldırılmalı: ug dizini silinmez, her koşu yeni bir dizin açar.
- `K/wp-053d/tools/diag-run.sh`
- `K/wp-068/tools/wp068-tests.sh` (U10, S16)
- `K/wp-065b/tools/batch38, batch40, batch41` (karışık), `batch54…58`, `batch61…66`
- `K/wp-032h/tools/f41-pm.sh`, `f42…f45`.
  - `f41-pm.sh`'nin sırası player → unity → editor snapshot. `225d0359`'da "ata player slotunu tutuyor" istisnası yüzünden 64 almıyordu; kuyrukta unity'yi tutarak bekliyordu. `0b6e86fc`'den beri istisna yalnız `--scope player` için geçerli, bu yüzden artık **64 alır**.
- Dolaylı (dış betik unity'yi tutar, iç sarmalayıcı snapshot alır):
  - `K/wp-032h/tools/unity.sh` (~75 çağıran: f*, b*, m*, r*, t*, d*, w*, g*, s*, h*.sh)
  - `K/wp-071/tools/unity*.sh` (~40 çağıran: r1…r34, carry, deliver-r1c)
  - `K/wp-083/tools/unity.sh` (~52 çağıran, 06.10: b1, b2*, h1*, stk*, ci1, ci2, a1 …). S-06'nın aktif WP-083 zinciri rollout'ta durur.

**C. Sınıflanmamış eski kanıt betikleri.** Yeniden koşulacaksa §4'e göre düzeltin.
- `K/wp-032h/evidence/s06-*.sh`, `…/run-r2.sh`, `…/run-correspondence.sh`
- `K/wp-019r2/evidence/wp073-*/run-once.sh, run-tests.sh, build-once.sh` (ve kopyaları)
- `K/wp-053d/evidence/wp069-*/run-once.sh`, `…/launch-plan/launch.sh`
- `K/wp-077/evidence/pin-null-main-r2-877a9aef/physical-run*.py`

**D. Kanon dışı guard yolu** (G4 D4, kural §2.3). 33 betik ana depo yolunu (`/Users/Erdem/Documents/Deaths Demolition/tools/userdata-guard.sh`) çağırıyor. Bu betikler yeni guard'ı sürücü taşındığında değil, entegrasyon anında alır.
- 05.10 ve sonrası: `K/wp-088/run-r1.sh`, `K/wp-088/run-s03.sh`, `K/wp-088/safety-net.sh`, `K/wp-084/run-exec.sh`, `K/wp-083/tools/unity.sh`, `K/wp-077-b10/guarded.sh`, `K/wp-074/tools/wp088_chain.sh`, `K/wp-069b5/tools/chain3.sh`.
- 30.09–01.10: `K/wp-071/tools/unity*.sh`, `K/wp-074/tools/*chain*.sh`, `K/wp-068/tools/wp068-*.sh`, `K/wp-032h/tools/f4*.sh`, `K/wp-032h/tools/unity.sh`.
- Düzeltme: `source <worktree>/tools/dd-env.sh` ve `"$DD_GUARD_CANON"`.

**Doğru sıra örnekleri:**
- `K/wp-085/tools/*-unity.sh`
- `K/wp-076/run-b9.sh`
- `K/wp-078/tools/guarded-test.sh`
- `K/wp-068s9/tools/dec109-run.sh`
- `tools/qa/rc.sh`
- `tools/main-ci.sh`
- `K/wp-088/run-r1.sh`: sıra doğru, ama üstteki yorum "slot acquire → snapshot" diyor ve guard yolu kanon dışı (Grup D).

Eski (bitmiş) kanıt betikleri yeniden koşulmayacaksa düzeltme gerekmez. Yeniden koşulacaklar için §4'e bakın.

## 6. Davranış değişiklikleri (özet)

**Sıralama (INV-7).** Aynı alanı koruyan guard'lı pencereler artık **sırayla** açılır:
- editor alanında editor ve all kapsamları birbirini bekler;
- player alanında player ve all kapsamları birbirini bekler.

Bu, farklı oturumlar için de, aynı oturumun ayrı betikleri için de geçerlidir.

**İç içe pencere istisnası.** Snapshot'ı çağıran sürecin bir atası o alanda açık bir pencerenin sahibiyse, yeni pencere beklemeden açılır. Örnek: dış betiğin penceresi içinde çalışan bir iç guard. Atanın canlı bir penceresinin tuttuğu alanlardaki yabancı biletler de iç içe snapshot'ın önünde sayılmaz. Atanın penceresi başka bir alanda olsa da kural aynıdır; örnek: ata player penceresi açık, çocuk editor istiyor, yabancı bir all bileti atayı bekliyor (gt 06.10 N1). Sayılsaydı karşılıklı beklemeyle kilitlenme olurdu.

**main-ci.**
- Aynı kuyruktadır, önceliği yoktur.
- Penceresi açıkken (25–60 dk) diğerleri bekler.
- Kendisi beklerken player slotunu ve lease'i tutar.
- Kuyruk zaman aşımında (`class=QUEUE_TIMEOUT`) yeniden denemez. Bu yüzden en çok bir kuyruk beklemesi (3600 s) sürer.
- main-ci ve rc.sh snapshot'ı arka planda (`& wait`) çağırır. Kuyrukta beklerken gelen INT, TERM ya da HUP bekleyen guard'ı hemen durdurur; guard kuyruktan çıkar. Guard pencereyi yine de açtıysa (çıkış 0 ya da `before.txt` var) pencere normal yoldan restore edilir.
- Eskiden TERM, guard dönene kadar (≤ 3600 s) bekliyordu. main-ci'de bu gecikme ayrıca restore'suz, terk edilmiş bir pencere bırakıyordu.

**Bekleme süreleri.** Değerler tam saniye olmalıdır.

| Değişken | Ne için | Varsayılan |
|---|---|---|
| `DD_GUARD_QUEUE_WAIT` | Kuyruk | 3600 s |
| `DD_GUARD_WAIT` | Profil kilidi (snapshot ve restore) | 300 s |
| `DD_GUARD_SLOT_WAIT` | Player slotu | 4800 s |

player ve all kapsamları önce player slotunu alır, sonra pencere kuyruğuna girer ve beklerken slotu tutar.

**Defter** (`~/.dd-userdata-guard/`):

| Klasör / dosya | İçerik |
|---|---|
| `windows/` | Açık ve dirty pencereler. |
| `closed/` | Kapanmış pencereler. `closed` durumu ve buraya taşıma tek kilit adımında yapılır; bekleyen bir snapshot `windows/` içinde kapanmış bir kayıtla karşılaşmaz. |
| `aborted/` | Başarısız snapshot'lar. |
| `queue/` | Bekleyen biletler. |
| `queue/done/` | Kuyruktan çıkan biletler: `<ad>.<granted\|refused\|left\|reaped\|unreadable\|exit<kod>>.json`. |
| `quarantine/` + `INDEX.tsv` | Restore'un yerinden ettiği baytlar. |

Kaydın `utc` alanı pencerenin açıldığı andır. Pencere kuyrukta beklediyse kuyruğa giriş anı `queuedUtc` alanında tutulur.

**Bilet budama.** Bekleyen her snapshot, kuyruğa bakarken diğer biletleri de denetler:
- Bir bilet `queue/done/…reaped.json`'a taşınır (budanır) şu durumlarda:
  - bekleyen guard ölü;
  - guard'ın beklediği çağıran ölü;
  - bilette `waiter` alanı yok. Bu durumda sahibi canlı olsa da bilet sırayı tutmaz. Guard artık böyle bilet yazmaz; bu yalnız eski ya da elle yazılmış biletlerde görülür.
- Bozuk JSON içeren bilet `…unreadable.json`'a taşınır.
- `<ns>-<id>.json` biçiminde olmayan dosyalar (not, kopya) yok sayılır ve onlara dokunulmaz.

**Silinmeyen kayıtlar.** Defterde hiçbir araç dosya silmez (kural §2.14). Pencere kaydı `windows/`'tan `closed/` ya da `aborted/`'a taşınır. Bilet `queue/`'dan `queue/done/`'a taşınır. `tools/disk-report.sh` defterin boyutunu gösterir.

**Oturumlar deftere dokunmaz.** Defterde dosya silmeyin, taşımayın, düzenlemeyin. Takılan bir pencereyi standart yol çözer: aynı dizinle yeniden restore. Bu yolla çözülmeyen pencereyi yalnız C-00 kapatır: kayda `resolution.json` ekler ve kaydı `closed/`'a taşır. Bunu yapan bir araç yoktur; işlem elle yapılır (kural §2.14, G4 Q4).

**D8 öncesi pencereler.** Rollout'tan önce açılmış bir pencerenin defter kaydı yoktur. Restore'u normal çalışır ve olmayan kaydı atlar.
