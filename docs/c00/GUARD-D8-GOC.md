# Guard D8 göç notu — oturumlar için (S-01 · C-00 05.10 · rollout D9'da C-00 ilan eder)

D8 ile `userdata-guard.sh` bir **pencere defteri** tutar (`~/.dd-userdata-guard/windows/`) ve aynı alanı (editor: Editor PlayerPrefs + oyuncu klasörü; player: bundle klasörü + player PlayerPrefs) koruyan iki pencereyi **aynı anda açmaz**. Bekleyenler **FIFO kuyruğa** girer. Bu not rollout günü geçerli olur; o güne kadar hiçbir şey değişmez.

## 1. Tazeleme (rollout duyurusundan sonra, bir kez)
- Sürücü (kanonik guard) C-00 tarafından main'e taşınır. Siz yalnız kendi worktree'nizi main'e getirin (yeni çağıranlar: main-ci, rc.sh, guard_receipt, oos_rule): `git -C <worktree> merge --ff-only main` (dalınız ileride ise `git -C <worktree> merge main`; rebase/force yok).
- Kontrol: `python3 tools/guard_freshness.py ~/Desktop/KADRO/context/projects/deaths-demolition/wp-079/driver/tools/userdata-guard.sh` → `0`.
- KADRO'daki `tools/slot.sh` kopyası değişmez.

## 2. Çıkış kodları — ne demek, ne yapılır
| Kod | Nerede | Anlamı | Ne yapılır |
|---|---|---|---|
| 0 | snapshot / restore | tamam | — |
| **32** | restore | kendi alanınız geri yüklendi ve doğrulandı (`verified=1`), **öbür alan** değişti (`out-of-scope-changed.txt`, geri alınmaz) | main-ci: uyarı. rc.sh editor aşaması: yalnız 30.09 kuralı tutarsa geçer (yalnız player alanı + yazar guard'lı bir Player penceresi + onun restore'u verified=1, `qa/oos_rule.py`); player aşaması: FAIL. Kendi koşucunuzda: `python3 tools/qa/oos_rule.py <ug>` → 0 ise uyarı satırıyla devam, değilse DUR + C-00. |
| **64** | snapshot | **kilit sırası yanlış**: unity slotu tutarken snapshot (bütün kapsamlar) | Sırayı düzeltin (§4): önce snapshot, sonra unity. |
| 64 | restore | bağ yok / v1 izi / başka kök; defterde kayıtlıysa **SNAPSHOT_MISSING** (pencere dirty) | DUR + C-00. |
| **65** | snapshot | defterde örtüşen pencere **dirty** ya da **sahibi ölü** (terk edilmiş). Mesaj pencereyi ve sıradaki adımı yazar: `next: "userdata-guard.sh restore <DIR> --scope <s>"` | O adımı **yalnız o pencereyi açan koşu bittiyse** uygulayın; yakınsamazsa C-00. Korunan küme sorunu (symlink, büyük/küçük harf, okunamayan plist) da 65'tir → C-00. |
| **75** | snapshot | kuyrukta `DD_GUARD_QUEUE_WAIT` (varsayılan 3600 s) doldu; ya da profil kilidi / player slotu zaman aşımı | Daha sonra yeniden deneyin (yeni dizinle). Bekleme sırasında satır: `queue position n — ahead: <DIR> (pid …) — estimate ~Y min`. |
| 75 | restore | BUSY / LOCKED / erken RACED (`retry=same-dir`) | **Aynı dizinle** restore'u tekrarlayın. Pencere kapanana kadar o alanın yeni snapshot'ları 65 alır. |
| 30 / 31 | restore | yazım/doğrulama sorunu (STALE, MISMATCH, WINDOW_LOST, FOREIGN_WRITER…) / KG sapması | DUR + C-00. STALE: kullanıcının sonraki kaydı korunur, kendiliğinden restore yok. |

## 3. Sahip ömrü kuralı (bütün kapsamlar)
Pencerenin **sahibi = snapshot'ı çağıran süreç** (guard'ın ebeveyni, pid + başlama zamanı). Sahip restore'a kadar **yaşamalı**. Sahibi ölmüş açık pencere "terk edilmiş" sayılır ve diğer oturumlar o alan için 65 alır.
- **Doğru:** tek betik — `snapshot → iş → restore` aynı kabukta (§4).
- **Yanlış:** snapshot'ı ayrı bir komut / araç çağrısı olarak alıp çıkan kabuk (`zsh -c 'guard snapshot ug'` sonra ayrı çağrıda Unity, sonra ayrı çağrıda restore), `( guard snapshot ug & )` gibi arka planda alıp çıkmak, `timeout guard snapshot …`. Bunlarda sahip hemen ölür.

## 4. Doğru kilit sırasıyla koşucu iskeleti
```zsh
#!/bin/zsh
# window (guard snapshot) → unity → run → unity release → restore — hepsi BU kabukta
source "/Users/Erdem/Documents/Deaths Demolition/tools/dd-env.sh"      # DD_GUARD_CANON, DD_SLOT
UG="<kanıt-dizini>/ug-$(date -u +%Y%m%dT%H%M%SZ)"
"$DD_GUARD_CANON" snapshot "$UG" --scope editor || exit $?             # 1) pencere (kuyrukta bekleyebilir)
restore() { "$DD_GUARD_CANON" restore "$UG" --scope editor; }
trap 'restore; exit 130' INT; trap 'restore; exit 143' TERM
# 2) Unity: tools/test.sh / build.sh unity slotunu KENDİ alır — onları çağırıyorsanız slot almayın.
#    Unity'yi kendiniz başlatıyorsanız: "$DD_SLOT" acquire unity <tag> … iş … "$DD_SLOT" release unity <tag>
"/Users/Erdem/Documents/Deaths Demolition/tools/test.sh" playmode --filter '<…>' --out "<kanıt>/pm"; RC=$?
trap - INT TERM
restore; G=$?                                                          # 3) restore — 0 / 32 (§2) / diğerleri DUR
[ $G -eq 0 ] || [ $G -eq 32 ] || { echo "guard restore $G — tell C-00"; exit $G; }
exit $RC
```
Dış betik unity slotunu tutup içteki bir "unity.sh" sarmalayıcısında snapshot alıyorsa: slot alımını sarmalayıcının içine, **snapshot'tan sonra** taşıyın.

## 5. Bilinen yanlış sıralı çağıranlar (unity → snapshot; rollout'ta 64 alırlar) — S-01 taraması 05.10
`K` = `~/Desktop/KADRO/context/projects/deaths-demolition`. Doğrudan:
- `K/wp-069b5/tools/chain.sh` (U33, S41), `K/wp-069b5/tools/diag-run.sh` (U9, S11)
- `K/wp-076/run.sh` (U11, S12)
- `K/wp-068/tools/wp068-tests.sh` (U10, S16)
- `K/wp-053d/tools/*-run.sh` (b2b, b2b2, b2c, b2r2, b2r3, b3, b3r2, b4, b4c, b4d, b4d2, b4r2, cired, diag, links)
- `K/wp-065b/tools/batch37…53, 61…66, batch-template-guarded.sh, batch54…58`
- `K/wp-032h/tools/f41-pm.sh, f42…f45`, `K/wp-032h/evidence/s06-*.sh`, `…/run-r2.sh`, `…/run-correspondence.sh`
- `K/wp-019r2/evidence/wp073-*/run-once.sh, run-tests.sh, build-once.sh` (ve kopyaları)
- `K/wp-053d/evidence/wp069-*/run-once.sh`, `…/launch-plan/launch.sh`
- `K/wp-077/evidence/pin-null-main-r2-877a9aef/physical-run*.py`
Dolaylı (dış betik unity'yi tutar, iç sarmalayıcı snapshot alır):
- `K/wp-032h/tools/unity.sh` (~75 çağıran: f*, b*, m*, r*, t*, d*, w*, g*, s*, h*.sh)
- `K/wp-071/tools/unity*.sh` (~40 çağıran: r1…r34, carry, deliver-r1c)
- `K/wp-083/tools/unity.sh` (~49 çağıran: b1, b2*, h1*, stk*, ci1, ci2, a1)
Doğru sıra örnekleri: `K/wp-085/tools/*-unity.sh`, `K/wp-076/run-b9.sh`, `K/wp-088/run-r1.sh`, `K/wp-078/tools/guarded-test.sh`, `K/wp-068s9/tools/dec109-run.sh`, `tools/qa/rc.sh`, `tools/main-ci.sh`.
Eski (bitmiş) kanıt betikleri yeniden koşulmayacaksa düzeltme gerekmez; tekrar koşulacaklar için §4.

## 6. Davranış değişiklikleri (özet)
- Farklı oturumların editor-scope guard'lı koşuları artık **sırayla** (INV-7). main-ci de aynı kuyrukta, önceliksiz; penceresi açıkken (25–60 dk) diğerleri bekler.
- Kuyruk bekleme üst sınırı `DD_GUARD_QUEUE_WAIT` (varsayılan 3600 s); `DD_GUARD_WAIT` (300 s) yalnız profil kilidi beklemesi için kalır.
- Pencereler `~/.dd-userdata-guard/` altında kalıcıdır (`windows/`, `closed/`, `aborted/`, `queue/`, `quarantine/`, `INDEX.tsv`); hiçbir araç silmez. Takılan pencereyi C-00 çözer (`resolution.json`).
