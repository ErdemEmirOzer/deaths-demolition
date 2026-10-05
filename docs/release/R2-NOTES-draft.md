# Deaths Demolition — R2 release notes (DRAFT)

> **Status: draft, not for publishing.** Written by C-00B (B-04) on 05.10.2026 from `docs/c00/R2-PLAN.md` (N2-01…N2-07) and the handoff document §4 (`docs/c00/C00-DEVIR-2026-10-05.md`), checked against `main` 0b3ec358.
>
> Several items below are **not in the game on `main` yet** (they are on session branches or still in production). §1 lists every line with its real state. Before R2 ships, C-00 / S-10 deletes or rewords each line whose state is not "on main" at the R2 FINAL commit. The player-facing text in §2–§3 describes R2 as planned; it must not go out while any of its lines is still open.

---

## 1. Line-by-line state (internal, for C-00 / S-10)

| # | Player-visible change | User note | State on `main` 0b3ec358 | What must land before the line can ship |
|---|---|---|---|---|
| 1 | Main menu: fewer words, one rail of icons, a word appears beside the focused icon, no pane open on arrival, level top-right | N2-01 | **On main** (menu v4 phase A, U1 default path 0.422) | — (U1b: Selection + panes v4 still open) |
| 2 | A moving cinematic camera over the real map behind the menu (soft left scrim) | N2-01 add-on | **Branch** (S-09 `wp-081-v4a-r2`: `bcde16dc`, `8ba256ec` + uncommitted) | S-09 delivery, 5 aspect ratios + 60 s MP4 shown to the user, integrate |
| 3 | Icons with motion, depth and reflection | N2-01 add-on | **Not started in code** (AP-028-B spec + 58 icons delivered) | S-09 icon v2 (shader + motion) |
| 4 | Five loading-screen artworks that rotate and never repeat back to back | N2-02 | **Code on main** (`KeyArtRotation`, gate U3); upscaled arts accepted in production | S-09 intake of the v002 upscaled arts (q90) + LoadingKeyArtTests |
| 5 | New zombie look "Drowned Guards": faces with eyes and teeth (3 head types), textured wet skin, wet indigo clothing, masks, clearer tiers | N2-03 | **Branch / production** (S-03 WP-084 slice 1; AP-025C accepted; AP-025D clothing polish + jaw weights running) | S-03 slice 1 integrated, Z2/Z3/Z3b/Z4 measured, AP-025D accepted |
| 6 | New fantasy hero "Broken Seal" (Kırık Mühür), palette V1 with dark shoulder | N2-04 | **Production** (AP-024C: model, 77-bone rig, atlas; FBX export + 11-weapon grip check pending) | AP-024C checkpoint → C-00 visual approval → S-06/S-02 WP-082 import |
| 7 | Hands hold the weapon in front of the body, no longer on the shoulder; rocket launcher fired from the shoulder | N2-05 | **On main but off** (`WeaponHoldIk.Enabled = false`); per-weapon rows on S-06 branch `wp-083-b1` | S-06 B2d/B2e commit with the hold on by default, H1/H3 PASS, main-ci |
| 8 | Reload: the magazine leaves the gun, the spent one drops, the fresh one goes in from the hand; shotgun shells seat one by one; rocket goes into the tube | N2-05 | **On main** (WP-082 magazine contract + visual, §12 event contract) | Hero v2 clips for every weapon (the visual follows the clip's events) |
| 9 | Walking and running: real starts, stops, sharp turns and pivots in eight directions, body sway | N2-06 | **Skeleton on main** (LocomotionV4, 142 ids); clips produced (AP-026C, 142 transitions + 50 reloads) but not imported | S-06 WP-083 clip import + foot IK, L1–L6 measured |
| 10 | Zombie walks: new shamble / walk cycles | N2-06 | **Production** (12 zombie walks in AP-026C) | S-03 + S-06 zombie clip import |
| 11 | Every shot is a full gunshot (no thin looping sound under automatic fire) | N2-07 | **On main** (WP-085 B1) | — |
| 12 | Room echoes: open courtyard, corridor and hall each ring with their own longer tail; distant shots sound distant | N2-07 | **On main** (AP-027 v003f tails, S4 v2 PASS); v003g recut on S-08 branch | S-08 v003g overlay; ar_1 / sg_1 listening kit heard by the user ("is it echoing enough") |
| 13 | Spent casings are heard where they land, on the surface they land on, with a small bounce | N2-07 | **On main** (casing sound from the visible casing's ballistics; worst miss 48–64 cm) | Optional: S-08 reads `CasingEvents.Launched` (target ≤ 25 cm) |
| 14 | Tracers start from the visible muzzle; the rocket is drawn from the tube mouth on its first frame | R1 carry-over (WP-087) | **On main** | — |

Gate references: `tools/qa/r2-gates.json` (H1–H5, L1–L6, Z1–Z5, S1–S6, P2–P5, U1–U4). Human gates H5 / L6 / Z5 / S6 / U4 need an AD verdict.

---

## 2. English — short version (player-facing, draft)

**Deaths Demolition — R2 update**

- **A new look for the menu.** Fewer words, more icons: one clean rail of controls, each named when you focus it. Behind it, the camera drifts through the real battlefield.
- **Loading screens.** Five new artworks take turns, and the same one never shows twice in a row.
- **Zombies, remade.** The Drowned Guards have faces now, with eyes and teeth, three head types, wet skin and soaked indigo clothing. Each tier is easier to tell apart at a glance.
- **A new hero.** Meet the Broken Seal, a fantasy warrior in the game's own colours.
- **Weapons held right.** Both hands are on the gun, in front of the body. Reloads show the magazine coming out, the spent one dropping and the fresh one going in. Shotgun shells go in one by one, and the rocket slides into the tube.
- **Movement that feels alive.** New starts, stops, turns and pivots in every direction, with the body swaying as you run.
- **Gunfire you can feel.** Every shot is a full, real gunshot. Courtyards, corridors and halls each echo in their own way, and distant fire sounds distant. Spent casings ring where they land.

---

## 3. Türkçe — kısa sürüm (oyuncuya, taslak)

**Deaths Demolition — R2 güncellemesi**

- **Menüye yeni bir görünüm.** Daha az yazı, daha çok ikon: tek, sade bir kontrol sırası; her ikonun adı ona odaklanınca görünür. Arkada kamera gerçek savaş alanının üzerinde süzülür.
- **Yükleme ekranları.** Beş yeni sanat sırayla gelir; aynısı arka arkaya iki kez çıkmaz.
- **Zombiler baştan yapıldı.** Boğulmuş Muhafızların artık yüzü var: gözler, dişler, üç baş tipi, ıslak ten ve sırılsıklam çivit giysiler. Kademeler bir bakışta ayırt ediliyor.
- **Yeni kahraman.** Kırık Mühür: oyunun renklerine uygun, fantastik bir savaşçı.
- **Silahlar doğru tutuluyor.** İki el de silahın üzerinde, gövdenin önünde. Şarjör değiştirirken şarjör çıkıyor, boşu düşüyor, dolusu elden yerine giriyor. Pompalıya fişekler tek tek giriyor, roket tüpe sürülüyor.
- **Canlı hareket.** Her yöne yeni başlama, durma, dönüş ve pivot animasyonları; koşarken gövde sallanıyor.
- **Hissedilen silah sesleri.** Her atış tam ve gerçek bir silah sesi. Avlu, koridor ve salon kendi yankısıyla çınlıyor; uzaktaki ateş uzaktan geliyor. Boş kovanlar düştükleri yerde sesleniyor.

---

## 4. Visual list (which frame, from where)

Rules:
- **In-game frames only for player-facing use.** They must be taken on the flagless player path (project rules §9, §11) and inside the user-data guard (`tools/userdata-guard.sh`, `tools/player-run.sh`).
- **Production renders are not in-game frames.** AP renders (Blender) may support a dev blog only and must be labelled as renders.
- The R2 portfolio of 10 frames (Editor + debug panel) is S-10's WP-091 job after R2 FINAL. The list below feeds it.

`K` = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`.

| # | Line | Frame | Source | Kind | Who / when |
|---|---|---|---|---|---|
| V1 | 1 | Main menu on arrival, every pane closed (1080p and 21:9) | Player capture with `-ddUiCapture`, menu v4 capture mode (`UiCaptureAgent` "menuv4": arrival, rail word moved, Profile pane open) | in-game | S-09, after R2 FINAL commit |
| V2 | 2 | Menu over the moving map: 3 stills + a 10–15 s clip from the 60 s MP4 | S-09 cinematic delivery (5 aspect ratios + 60 s MP4); storyboard reference `K/asset-production/AP-028/exports/v002/MenuCinematic/Storyboard_ContactSheet.png` (previz, not in-game) | in-game (MP4) / previz | S-09, when line 2 lands |
| V3 | 3 | One icon in motion (focus → press) at 2× | S-09 icon v2 capture; spec `K/asset-production/AP-028/exports/v002/` (`Icons/README.md`) | in-game | S-09, when line 3 lands |
| V4 | 4 | The five loading arts as a strip | Accepted masters `K/asset-production/AP-028/exports/v002-upscale/Loading/` (L01 L02 L04 L05 L08); review page `Loading/review.html` | art (is the in-game image) | C-00 / S-09 |
| V5 | 5 | Zombie crowd from the default top-down camera at 40 zombies; one TP 3/4 mid shot | In-game: Z3b scene (AP005, fixed mix 22/10/6/2) with Player capture. Renders: `K/asset-production/AP-025/06-renders/v002/crowd-40-top50.png`, `tp-34-midshot.png` | in-game / render | S-03 after slice 1; renders only labelled |
| V6 | 5 | Faces: three heads side by side; tier lineup | `K/asset-production/AP-025/06-renders/v002/portrait-trio.png`, `tier-lineup.png`, `hero-portrait.png` (AP-025C) | render | C-00 (dev blog only) |
| V7 | 6 | The new hero: full body, TP over-shoulder, FP hands on weapon | AP-024C renders `K/asset-production/AP-024/06-renders/v002/` (50° / TP / FP, turntable, portrait; checkpoint pending). In-game after WP-082 import | render → in-game | AP-024C agent → S-06 |
| V8 | 7 | Before / after: R1 weapon on the shoulder vs R2 hands on the gun, TP side view | R1: R1 release build `84941661` capture. R2: S-06 B2e evidence (rl_1 TP + side before/after already requested) | in-game | S-06 |
| V9 | 8 | Reload sequence, 4 frames: magazine out → spent drops → fresh in hand → seated (ar_1, FP and TP) | Player capture during a reload (`-ddCameraMode fp` / `tp`); H3 test frames as reference | in-game | S-02 / S-06 |
| V10 | 9 | Run → sharp pivot, 6-frame strip; sprint stop | In-game after WP-083 import. Reference: AP-026C index `K/asset-production/AP-026/exports/v002/hero-v002c/` | in-game | S-06 |
| V11 | 11–12 | Audio: 15 s clip, ar_1 held fire in courtyard → corridor → hall (FP), and the ar_1 / sg_1 listening kit | S-08 listening kit (`Kit_` + `Stem_`, MP4 AAC — `.m4a` is not served on the artifact page) | in-game audio | S-08, then user listening |
| V12 | 13 | Casings landing on stone vs wood, close FP clip with sound | Player capture with `-ddVfxWeaponCapture <dir>` + audio capture (`-ddAudioCapture`) | in-game | S-08 / S-07 |
| V13 | 14 | Tracer from the muzzle; rocket leaving the tube, first 3 frames | `-ddVfxWeaponCapture <dir>` frames (WP-087 evidence) | in-game | S-02 / S-07 |
| V14 | all | Hero shot for the store / post header: TP, Cinematic tier, new hero + zombies + rain, World V6 | Editor + debug panel (WP-091 portfolio rule), after R2 FINAL; existing R1 references `docs/images/mode-thirdperson.jpg`, `v6-overview.jpg` (R1 look — do not reuse for R2) | in-game (Editor) | S-10 |

---

## 5. Open before publishing

- Every §1 line not "on main" at the R2 FINAL commit is removed from §2/§3, or its wording is reduced to what shipped. Line 7 is the main one: it ships only if the hold is on by default.
- Human gate verdicts (H5, L6, Z5, S6, U4) are recorded. Wording such as "AAA" stays out of player text; the notes describe what the player sees.
- The names "Broken Seal" and "Drowned Guards" are the working names from G1 / G3. C-00 confirms they are the in-game names (string table `r1-strings.json` en/tr) before the notes use them.
- Platform line (macOS + Windows 64) and build number come from the R2 release (`tools/build.sh release`, `build-info.json`).
