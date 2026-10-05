# S-02 devir notu · 05.10.2026 (hesap limiti, C-00 ACİL)

## Açık iş: WP-087 "tutuş açıkken tepmeyi kollar taşır"

İş C-00 05.10'da verildi; tanı S-06'nın, `K/wp-083/evidence/b2i`, `b2j`.

| | |
|---|---|
| Worktree | `K/wp-069b5/worktree/deaths-demolition` |
| Dal | `wp-087-heldkick`, main 0b3ec358 üstünde |
| Son SHA | **6ea4492a** (WIP; main'e ALINMAZ) |

### Yapılanlar (WIP, doğrulanmadı)

- **WeaponVisualRig** (S-02):
  - `HoldShare` (0–1): tutuşun ağırlığı.
  - Gövde tepmenin yalnız `1 − share` kadarını alır.
  - `HeldKick`: x = m geri, y = ° yukarı; tutuşun taşıdığı pay.
  - Tutuş payında birikim tavanı: `HeldKickCap` = 1,25 × tek atış.
  - Yay ve tutuş kapalı yol değişmedi.
- **WeaponHoldIk** (S-06'nın dosyası!):
  - `RecoilSource` (Func<Vector2>) ve `LastRecoil` eklendi.
  - Pivot, `aim × recoil.x` kadar geri gider ve recoil.y kadar namlu yukarı döner.
- **WeaponController:**
  - Her kare `ActiveRig.HoldShare = hold.Active ? hold.Weight : 0` (Special / klasik ayrı).
  - `hold.RecoilSource`'a bir kez delegate bağlanır.
- **Test:** `Tests/PlayMode/Wp087TutusTepmeTests` (yeni).
  - TutusAcik_OnAtis_IkiElTutamakta_TepmeKollarda: TP, tutuş açık, her klasik 10 atış, iki el ≤ 1 cm.
  - TutusKapali_GovdeEskiTepmede.

### Ölçüm, koşu b6hk (guard verified=1)

| silah | sağ el | sol el | pivot geri / yukarı |
|---|---|---|---|
| A | 0,00 | 0,32 | 8,5 cm / 7,6° |
| B | 0,00 | 0,00 | 4,7 / 2,8 |
| C | 0,00 | 0,89 | 6,1 / 5,5 |
| ar_2 | 0,00 | 1,59 (önce 12,6) | 10,5 / 9,4 |
| lmg_1 | 0,00 | 3,66 | 10,5 / 9,4 |
| sr_1 | 0,02 | 0,00 | 0,9 / 0,8 |
| smg_2 | 0,00 | 0,00 | 3,7 / 2,2 |
| sg_2 | 0,00 | 3,82 | 6,1 / 5,5 |
| lmg_2 | 0,00 | **9,58** | 8,4 / 7,6 |
| rl_1 | 0,00 | 3,31 | 8,4 / 7,6 |

- Sağ el her silahta düzeldi (≤ 0,02 cm).
- Tutuş kapalıyken gövde eskisi gibi teper (8,4 cm). Test PASS.
- WeaponFeelSliceTests yeşil (9/10 = bu test hariç).
- b6hk2 (H1 hold=on): PASS. maxCm 0,00, elbowMaxDeg 152,7; duruş ve yürüyüş tutuşu bozulmadı.

### Kalanlar

1. **Sol el ≤ 1 cm sağlanmadı:** lmg_2 9,6, sg_2 3,8, lmg_1 3,7, rl_1 3,3, ar_2 1,6 cm. Bakılacaklar:
   - (a) pivot geri ofseti 10,5 cm'de. AR his satırı tepme 0,10 m/atış; lmg'ler de aynı satırı kullanıyor. `HeldKickCap` 1,25 → ~0,6 ya da tutuş payına ayrı mutlak tavan (ör. 5 cm / 5°).
   - (b) WeaponGripIk aynı karede destek noktasını yakalıyor mu (sıra 910, pivot 890). LastError'un kaynağını ayır: erişim mi, gecikme mi.
   - (c) Tepme sırasında dirsek düzleşiyor mu (H1 elbowMaxDeg ile).
2. **Testin kaynak ölçütü:** rl_1 tek atışlık (şarjör 1). `shots < 2` koşulu rl_1 için `shots < 1` olmalı. rl_1 "omuz / eski yol" mu, tutuş içinde mi, C-00'a sor.
3. **S-06 ile API:** S-06 05.10'da ayrı bir öneri yaptı:
   - Öneri: `ArmsCarryKick` bool, `KickLocalOffset` / `KickLocalRotation`, WeaponHoldIk tarafını kendisi bağlayacak, ölçü Atis_SolEl testi.
   - Benim WIP'im WeaponHoldIk'e (S-06 dosyası) dokunuyor. S-06'nın bekleyen B2d/B2e commit'leriyle çakışabilir.
   - Devralan: S-06 ile hizalanın. Seçenekler:
     - (i) S-06'nın adlarına geç (rig = benim, tutuş = S-06);
     - (ii) bu WIP'i S-06'ya yama olarak verin.
   - Ölçüt C-00'ın: iki el ≤ 1 cm her kare, auto 10 atış, tüm klasikler; pivot ofseti / açı rapor.
4. Mekanik isabet ve sekme değişmedi (yalnız görsel). Ateş başlangıcı namluda; namlu tutuşla birlikte hareket ediyor.

### Sıradaki komut

```
cd ~/Desktop/KADRO/context/projects/deaths-demolition/wp-069b5
zsh tools/chain.sh <etiket> 'playmode|DD.Prototype.Tests.PlayMode.(Wp087TutusTepmeTests|WeaponFeelSliceTests)'
DD_HOLD_IK=1 zsh tools/chain.sh <etiket> 'playmode|DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus'
```

## Bugün main'e giren S-02 işleri

C-00 cherry-pick'leri:
- VurusYolu / H3 / kaynak assert / H1-B / roket (b) / hiza (sg_1 FP kovan + kapılar) / §12 / iz görseli / §9 havuz ısınma / §14 Raiju / hero v2 ölçüm;
- kovan fırlatma olayı (cc5e6246), H1 elbowMaxDeg (f7166ed3).

Kanıtlar: `K/wp-087/evidence/marker-align/`, `K/wp-082/evidence/hero-v2-reach/` (README'deki düzeltme dahil).

## Açık izlemeler

- S-08: CasingAudio'yu CasingEvents.Launched + CasingEffect.PredictLanding'e geçirecek. Sonra harness'te ses ↔ iniş ≤ 25 cm assert'i açılacak (Wp087IsaretHizasiTests.KovanOlayi_…).
- S-06: SgReloadDriver (DefaultExecutionOrder > 0; WeaponController sırası 0) hero alımında.
- H3 contactDeviationFramesMax = 16 (S-06 temas IK'sını bekliyor).

## Guard

- Bugünkü tüm koşular verified=1. b6hh2 OOS uyarısı oos_rule.py ile kapandı (K/wp-069b5/evidence/guard-pending.txt).
- Açık pencere yok. b6hk ve b6hk2 restore verified=1, OOS 0. Unity koşusu yok.
