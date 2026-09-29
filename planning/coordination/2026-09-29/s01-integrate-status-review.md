# C-00 — S01 integrate-status atom bağımsız incelemesi

**Dar teknik intake kabulü önerilir.** İncelenen atom `1466da4ed594746ff5b84daaf9ef0301179107ca`, parent `3c0875e1e259cebae34e2cb496d03bb4e8dd9045`. Yalnız `tools/integrate.sh` ve yeni `tools/qa/tests/test_integrate_status.py`: +143/-6. Source/main status komutunun hata durumunu artık dirty/clean çıktısından bağımsız ele alıyor; status edinilemediğinde apply ve temizlik duruyor. Bu incelemede gerçek Git mutation, test, build veya main yazımı yapılmadı. Tek yazım bu `/private/tmp` raporu.

İnceleme sırasında güncel main `512098809ed07d34f1c92338d6a8101d94b72e8a` idi. `git diff --name-only 3c0875e1 main -- tools/integrate.sh tools/qa/tests/test_integrate_status.py` boş: atomun iki yolunda parent sonrası main commit değişikliği yok. Bu commit-tree karşılaştırmasıdır; güncel çalışma ağacının temiz olduğunu iddia etmez.

## Kod var / koşuldu / kabul

| Boyut | Doğrulanan durum |
|---|---|
| Kod var | EVET. Parent ve iki-path scope Git show ile doğrulandı; committed source byte SHA'ları test/delivery manifestleriyle eşleşiyor. |
| Koşuldu | EVET. Gerçek Python unittest logu14 `ok`; XML14 test-case / 0 failure / 0 error / 0 skip. Testler zsh ile üretim scriptinin kopyasını çalıştırıyor ve hiç gerçek Git'e delege etmeyen fake Git kullanıyor. |
| Test edilen kaynak kimliği | Testler committen önce 3c HEAD üzerinde değiştirilmiş dosyalarla 04:15:50 UTC kaydedilmiş; `testedHead=3c` bu nedenle doğru. Test receipt source SHA'ları commit1466 kaynak bytes'ıyla bağımsız eşleşti. Eski main tool bytes'ının PASS'ı atom için aktarılmadı; test edilen atom byte'ları hash-bound. |
| Kabul | Teknik atom kabulü ÖNERİ. Gerçek repo integration/rollback uygulamasının başarılı olduğuna dair yeni koşum yapılmadı; Unity/Player/profile için guard N/A doğru. Concurrent editor/git işlemleri altında rollback kapsamı bu atomla yeniden tasarlanmadı. |

## Gate ve rollback davranışı

Önceki main/source gate `git status --porcelain | wc -l | tr ...` pipeline'ında Git başarısız olsa dahi boş stdout sayımı0 ile “clean” kabul edilebilirdi. Yeni `if D=$(git ... status); then` ve `if MD=$(... status); then` Git exit code'u doğrudan kontrol eder. Exit0+empty clean; exit0+dirty fail; exit128+empty veya partial dirty fail. `FAILS>0` apply bölümünden önce exit3 verir. Main/source worktree status hata mesajları ayrı ve nettir. Gate-fail yolunda merge/cherry-pick/tag/commit/restore/rm/add çağrısı yoktur.

`abort_pick` artık üç status checkpoint'inin her birinde edinim hatasını yakalar:

1. İlk NUL status hatası: exit4 / `ROLLBACK UNVERIFIED: status failed; no cleanup`. `cherry-pick --quit` dahi çağrılmaz.
2. İlk başarı ve sequencer quit sonrası ikinci NUL status hatası: yalnız `cherry-pick --quit` olabilir; path restore/rm yok. Exit4 / “no path cleanup”.
3. Path restore sonrası status hatası: daha önce doğrulanmış listedeki restore gerçekleşmiş olabilir, fakat sonraki temiz durum doğrulanmış sayılmaz; exit4 / “post-restore status failed”. `aborted: 0 dirty paths` çıktısı yok.

`verified_status` yerel değişkendir. Üretim scripti `#!/bin/zsh`; `print -rn -- "$verified_status"` NUL kayıtlarını Python parser'a taşıyarak önceki path parsing'i sürdürür. Existing parser newline ile path çıktı üretir; newline içeren filename ve race redesign bu atomun kapsamına alınmamıştır. Başarısız durumlarda yeni unsafe overwrite yolu tespit edilmedi.

Mevcut rollback tüm o anda dirty yolları restore/drop eder. Bu güvenlik varsayımı **clean preflight + tek main yazarı**dır. İlk gate, entegrasyon öncesindeki dirty tracked/untracked içerikleri reddeder; status okunamıyorsa da reddeder. Gate sonrası eş zamanlı başka yazarın editinin rollback'te korunması bu atomun sunduğu bir garanti değildir. C-00'nun önceden dirty S07 dosyalarını koruma/owner redirect/restoration işlemi parent tarafından ayrıca bildirildi; bu rapor o operasyonun kanıtlarını yeniden denetlemedi ve S01 kod kabulüyle birleştirmedi.

## 14 adversarial testin anlamlı kapsamı

| Grup | Adet | Gerçek behavioral assertion |
|---|---:|---|
| Main status | 3 | Empty+128, dirty+128, dirty+0 → exit3/GATE FAIL; mutating Git call listesi boş. |
| Source status | 3 | Empty+128, dirty+128, dirty+0 → exit3/GATE FAIL; mutating Git call listesi boş. |
| Clean controls | 2 | Exit0+empty → fake merge `--ff-only` ve annotated tag çağrıları; `integrated:` sonucu. Her çağrıyı sadece reddeden vacuous fixture değildir. |
| Rollback checkpoint failure | 6 | İlk/ikinci/post-restore checkpoint × empty/dirty partial stdout +128 → exit4/ROLLBACK UNVERIFIED, false-clean mesajı yok; mutation çağrılarının tam sırası/izin verilen kısmı doğrulanır. |

Fixture PATH'in başına kendi `git` executable'ını yerleştirir; bu fake hiçbir komutta subprocess/real Git çağırmaz. Full script kopyası için dd-env stub, repo/source/common alt dizinleri ve temp sentinels oluşturur; Unity/Player/profile/gerçek git index/ref kullanmaz. Rollback için gerçek production `abort_pick` fonksiyonu kaynak kopyasından çekilir; beklenen string'in kopyasıyla test yapılmaz.

Her test tracked edit sentinel ile untracked sentinel'in byte içeriklerini ve repo dizinindeki path setini karşılaştırır. Gate-fail no-mutation kanıtında bu anlamlıdır. Ancak fake `restore` no-op'tur; rollback fixture'daki “pre-existing tracked edit” sentinel'in değişmemesi **gerçek git restore'un bu editi koruduğunu ispatlamaz**. Rollback testleri status-failure call boundary ve false-clean engelini doğrular. Bu sınırlama kabulde açık tutulmalıdır.

Test suite zsh shell'i gerçekten çalıştırdığı için exit-code başarısızlığı ve NUL-path geçişi için yalnız source metni eşitliği kontrolünden daha güçlüdür. Mevcut behavior korunurken hata yolları ve pozitif kontrol birlikte sınanmış; yeni üretim code-change için uygun dar test kapsamı vardır. Suite tam integration planner, gerçek LFS/gate çalışma ağacı veya concurrency testi değildir.

## Kaynak ve tam manifest kanıtları

Kanıt dizini: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z`.

| Yol | Doğrulanan SHA256 |
|---|---|
| Commit1466 `tools/integrate.sh` | `dcac5bc577fad27bce4edccec97abb5419aa0b60d0d62cbf088e4ccdc139f53c` |
| Commit1466 `tools/qa/tests/test_integrate_status.py` | `fdeeeb9e47abf66f9b48db9b20ac2a33055b883452f9368fc2a760f935d6843b` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/delivery-receipt.json` | `d53be2ea3795ea2167b1aa999238d17058390d5d98fee995922c84d268011701` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/test-receipt.json` | `18112bd7c3dd2c18dd5337e6292154d4f1f2b20bc0986591293db30c7b5572d6` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/python-tests.xml` | `ac59bd71f560353d8b1cdd3d94d2f5fa2a3f05b3660b4f03e4e4e43441e66aff` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/python-tests.log` | `a101e29bd3cd5bebd3a78a95640f5c4ddfee08ee46c9106cba3dc4dca14bc58a` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/shell-static.log` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/wp-079/integrate-status-atom-20260929T041512Z/diff-check.log` | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |

5/5 artifact hashes +2/2 committed source hashes eşleşti. XML ve log14/14 sonucunu doğrular; original Python exit0 / overall exit0, zsh -n exit0, diff-check exit0 receipt'te mevcut. İki boş static log exit durumunu tek başına ispatlamaz; exitler hash-bound test receipt kaydıdır. Fiziksel koşum9.797s, no fail/error/skip. Credit0. Bu bağımsız inceleme testleri tekrar çalıştırmadı.

## C-00 kabul cümlesi önerisi

“S01 integrate-status atom1466da4e, yalnız tools/integrate.sh ve14 fakeGit behavioral tests kapsamında teknik intake'e kabul edildi. Git status edinim hatası main/source gate'de exit3, rollback checkpoint'lerinde exit4/UNVERIFIED üretir; gate fail sırasında main mutation yok ve false-clean rollback sonucu yok. Commit source hashes test edilen bytes ile eşit,14/14 gerçek fakeGit shell testPASS. Gerçek Git rollback/concurrency/LFS suite sonuçları ayrıca DOĞRULANMADI; clean-preflight ve tek main yazarı şartı korunur.”
