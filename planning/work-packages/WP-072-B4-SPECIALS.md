# WP-072 B4 — Special ses seti (SW_ARC / SW_GEL) · veri tarafı

Durum 2026-09-24: veri main'de (fc30530); kablolama + kaldırma S-02 WP-069 B4 (4654d4a5) üstünde yapıldı — SpecialAudio, eski 15 kimlik / 30 dosya silindi, manifest 329/773.

## Kimlik → olay (S-02 WP-069 §7)
| Kimlik | Olay | Bus | Not |
|---|---|---|---|
| SwArcCharge (2) | SwArcChargeStart | Weapon | 0,25 s şarj → yükselen tek-atış (döngü değil) |
| SwArcFire (4) | SwArcFire | Weapon | deşarj + cam "vınn" + hava |
| SwArcSphereLoop | SwArcSphereFlightStart→End (projectileId) | Impact 3D | bobin + titrek cam 2,4 k |
| SwArcChainJump (4) | SwArcChainJump (targetIndex) | Impact | indeks başına +1 st, 40 ms dizi; 6+ hedefte kümele |
| SwArcShockLoop / SwArcShockEnd (2) | SwArcShockStart→End | Target | gövde başına döngü, tavan 4 |
| SwArcCellOut / SwArcCellIn (2+2) | anim.cell_out / cell_in | Weapon | cam/pirinç + kıvılcım / kilit + bobin |
| SwArcReady | SwReadyTone | Weapon | bobin dolar + rin (pirinç yalnız Special'da) |
| SwGelFire (4) | SwGelFire | Weapon | bakır hazne "pşşt" + "plop" |
| SwGelFlightLoop | SwGelFlightStart→End (projectileId) | Impact 3D | < 1 s |
| SwGelHit (3) / SwGelHitFlesh (2) | SwGelHit (surface / flesh) | Impact | etde LP 1,5 k |
| SwGelPuddleCreate (2) / SwGelPuddleLoop / SwGelPuddleExpire | SwGelPuddleCreated / Expired | Impact (Special Field havuzu) | döngü tavanı 2 |
| SwGelSlideEnemy (2) / SwGelSlidePlayer (2) | SwGelSlideStart→End | Target / Weapon | kalabalıkta 60 ms kümeleme |
| SwGelCanisterOut / SwGelCanisterIn (2+2) | anim.canister_out / canister_in | Weapon | |
| SwGelReady | SwReadyTone | Weapon | hazne basınçlanır + rin |

Seviyeler: tek-atış −18/−20, döngü/yan katman −22 dosya (katalog tabanı); STANDARDS §2'nin −24/−26 hedefleri runtime kazancıyla (şarj −2, birikinti −4 dB).

## Kaldırma listesi (S-02 B4 ile aynı commit)
15 kimlik / 30 dosya: SwFire{Kagura,Ofuda,Tide} (310–312), SwField* (313–315), SwFieldEnd* (316–318), SwHit* (319–321), ZombieReact{Push,Web,Pull} (330–332).
Kod: AudioSoundId, AudioEventMap (WP-058/058C satırları + SpecialUsage()), AudioEventRouter (SpecialUsage / sw_push·web·pull), PrototypeAudioDirector (field loop slotları: Ofuda 1 + Tide 2), AudioRecipes (fallback dalları), Audio/README §5, AP011 WAV + manifest girdileri (ap011_manifest yeniden koşusu, v019 ile).
