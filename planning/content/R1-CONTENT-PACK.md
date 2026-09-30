# M4-R1 · Content pack R1 (UX writing + game writing)

Owner: UXW (content session) · Date 2026-09-24 · Base: `main` · Consumers: **S-09** (UI, WP-068 R1 + WP-070 Pause selector) and **S-04** (progression, WP-077 B1/B2/B4).
Sources: `WP-069-ARSENAL-V3-DIRECTION.md` (§0 table, §1 §2 identity, §1 §6 seals, §1 §7 Specials) · `WP-077-PROGRESSION-TREE-V2-DIRECTION.md` (§2 in-run tree, §3 PATH, §5 achievements) · `WP-068-UI-V3-DIRECTION.md` (§1 tone, §2.2 typography, §5 E9/E24/E25, §15.4 tips) · `WP-070-CAMERA-MODES-DIRECTION.md` §5.1 · voice from `UiLoadingTips.cs`, `PauseScreen.cs`, `RunHudScreen.cs`, `achievement-catalog.json`.
Machine-readable twin: [`r1-strings.json`](r1-strings.json) — flat `{"key": {"en": "…", "tr": "…"}}`, 483 keys, generated from one data set together with the tables below, so keys and text match.

Docs only: nothing here touches a Unity asset or a C# file. All numbers that WP-069/WP-077 mark PROTOTYPE-ONLY are left as placeholders; the only literal counts are identity facts the weapon is built on (Sanren's three-round burst, Nito's two barrels, the ten classic weapons).

## Counts

| Section | Count | JSON keys |
|---|---|---|
| Weapons + Specials (name, role tag, 2-sentence description, flavour line, class, fire mode) | 10 + 2 | 72 (`weapon.<id>.*`) |
| Shared weapon labels: classes 7, fire modes 8, stat labels 20, stat formats 8 | 43 | 43 |
| In-run tree nodes (name + effect): branch tiers 10 · branch keystones 9 · Hearth 1 · Special links 4 · forks 20 · capstones 10 · seals 10 · body lane 12 | **76** | 152 (`tree.run.*`) |
| In-run tree labels (kinds 8, lanes / states / flags / actions / captions 24) | 32 | 32 |
| Meta PATH nodes: WEAPONS 10 · SPECIAL 10 · PASSIVES 18 | **38** | 76 (`tree.meta.*`) |
| PATH labels | 28 | 28 (`path.*`) |
| Loading-screen tips | **30** | 30 (`tips.001`–`tips.030`) |
| Camera-mode labels (Pause selector + camera settings) | 15 | 15 (`camera.*`) |
| HUD status stamps + gel hint (S-09 R1-B) + the ammo LOW word (S-09 12b) | 5 | 5 (`hud.*`) |
| Selection stage labels (S-09 R1-B) | 7 | 7 (`selection.*`) |
| New achievements (weapons + Specials) | **12** | 24 (`achievement.*`) |
| **Total** | | **483** |

## 0 · Assumptions and ambiguities (read first)

**Assumptions**

- **A1 · Language.** The shipped UI is English (every literal in `Assets/_Prototype/UI`), so `en` is the source string and `tr` is a parallel translation. The direction docs are Turkish; where they give a Turkish node name (WP-077 §2.3/§2.4/§3.3) the `tr` value keeps it verbatim and the `en` value is a new translation.
- **A2 · Voice.** Matches the current HUD/menu copy: labels and buttons in CAPS, body text in short declarative sentences that state the rule first ("Unspent gold carries into the next round."), no exclamation marks, no second-person hype, British spelling in body text ("armour", "colour", "centre") as in `UiLoadingTips.cs`. Flavour lines are the only place for imagery; they stay two short beats, calm-but-lethal.
- **A3 · Numbers.** Every tunable value is a placeholder. Glossary: `{value}` primary number (percent sign sits in the string: `+{value}%`), `{base}` the number before the change, `{count}` a whole count, `{seconds}`, `{range}` / `{radius}` in metres, `{max}` a cap, `{round}`, `{level}`, `{kul}`, `{reserve}`, `{pellets}`, `{node}` another node's display name, `{label}` / `{before}` / `{after}` for the E24 before → after line. Every placeholder in `en` exists in `tr` (checked). S-09 formats decimals with the existing invariant `.` in `en`; the `tr` column expects `,` (see Q9).
- **A4 · Node ids.** WP-077 defines node *kinds* and the grid but no ids (§7.1 lists an `id` field only). This pack proposes the ids in the tables (`branch.<enum>`, `key.<enum>`, `fork.<slot>.x|y`, `cap.<slot>`, `seal.<slot>`, `link.<special>.1|2`, `hearth`, `body.*`; meta `meta.weapon.<slot>`, `meta.special.*`, `meta.passive.*`). Branch ids use the `UpgradeBranch` enum names in snake case, including the three new ones (`armor_break`, `impact`, `cluster`). If S-04 picks other ids, only the key prefix maps; titles/descs stay (`titleKey`/`descKey` in §7.1 are the join).
- **A5 · Weapon lane is a template.** The 13 weapon-lane nodes are assembled per weapon: its three `coreBranches` tiers + the matching three keystones + its fork pair + capstone + seal + the shared Hearth + the Special links of the Special in play. So the pack writes each piece once (76 unique nodes) instead of 10 × 13 copies.
- **A6 · Rocket wording.** rl_1's Explosion branch uses the separate `branch.blast` string ("Blast") per WP-069 §6 / WP-077 §2.3; the Explosion keystone (Ash Cloud) keeps one string for both meanings.
- **A7 · Seal names in ASCII.** WP-069 writes Kōdō and Sōhatsu with macrons. The Text atlas is Latin-1 and the Display atlas holds ≈ 62 caps glyphs (WP-068 §2.2), so ō would render as a fallback box. The strings use **Kodo** / **Sohatsu**; the macron spelling stays in design docs only. Kabuto-wari keeps its hyphen.
- **A8 · "Special".** In new copy, "Special" always means a Special weapon (Raiju, Namekuji). Enemies are always named by type (Runner, Guard, Executioner), never by tier word, because the existing tier word "Special" (Runner) would collide.
- **A9 · Ash / Kül.** *Decided (C-00, 2026-09-25): the UI is English, so the meta currency is **"Ash"** in `en` and **"Kül"** in `tr`* ("PATH · 120 Ash · L11" / "YOL · 120 Kül · L11"). No Turkish-character allowance is needed in the English UI.
- **A10 · Achievements.** WP-069/WP-077 name no weapon/Special achievements (WP-077 §5's ten new ones are tree achievements and are out of scope here). The 12 proposed here follow the catalog's style (Title Case name, one-sentence description ending in a period) and read their threshold from the catalog through `{count}` / `{round}`; the threshold column is a proposal. Every condition is new to `AchievementCondition` (S-04 B4).

**Ambiguities and conflicts found in the direction docs**

- **Q1 · Body-lane effects are undefined.** *Resolved (C-00, 2026-09-25): the proposal is accepted (S-04 catalog a011f747); the nodes stay LOCKED until their hooks land.* WP-077 §2.2 names Escape (Kaçış), Thick Skin (Kalın Deri), Breath (Nefes), Blood Step (Kanlı Adım), the fork Ash Step (Kül Adımı) / Iron Knee (Demir Diz) and the capstone Second Wind (İkinci Nefes), but gives no effects (only §2.4's "İkinci Nefes lethal-hit catch" hint). The effect lines marked *PROPOSED* are the writer's suggestion: each keystone amplifies its row (move / health / regen delay / regen rate) and the fork is move-versus-hold damage reduction. **S-04 must confirm or replace them.**
- **Q2 · Glyph coverage for Turkish.** ğ, ı, ş, İ, Ş, Ğ are not in Latin-1, so the whole `tr` column needs the Text atlas extended to Latin Extended-A, and CAPS labels also need the Display atlas extended (it holds caps only; `Ü`, `Ç`, `Ö`, `İ` are missing). WP-068 §2.2 sizes the atlases for English only. This is a build-size item for S-09, not a copy change.
- **Q3 · Turkish capitalisation.** Uppercasing in code with `ToUpperInvariant()` (as `RunHudScreen` does for `DisplayName + " ONLINE"`) turns `i` into `I`, not `İ`. The `tr` labels are therefore authored already in CAPS; code must not uppercase `tr` strings (or must use the tr-TR culture).
- **Q4 · Special links "while carried".** *Resolved (S-02, 2026-09-25): the link is live from the Raiju pickup to the end of THAT round (a held Special ends at round end, DEC-099); the Namekuji links use the same window.* WP-077 §2.4 Conductor says "while the Special is in hand the classic weapon's Chain +1 jump", but WP-069 §7c suspends the classic weapon while a Special is active. The string says "While you carry Raiju", i.e. from pickup until the Special is spent or dropped, and assumes the classic weapon benefits between Special shots or once it returns. S-04/S-02 should state which one it is.
- **Q5 · Namekuji link II name.** *Resolved (C-00, 2026-09-25): tr "Jel Hasadı".* WP-077 calls it "Donma" (freezing), but the effect is "an enemy that dies in gel gives +1 gold". The `tr` keeps "Donma" as the doc says; `en` uses **Gel Harvest**, which describes the effect. Rename the Turkish too if it was a slip.
- **Q6 · "Kese açılışta".** *Resolved (C-00, 2026-09-25): the Purse is owned from the start; the strings say so.* WP-077 §3.3 Economy K4 can read as "the Purse is free at the start" or "the Purse can be offered from round 1". The string uses the second reading ("can be offered from round 1"); flag if the first was meant.
- **Q7 · Existing strings made stale by WP-069.** These `UiLoadingTips.cs` lines will be false once Arsenal v3 lands and should be retired when the new tips go in: `loading.tip.ctrl.01` ("Every classic weapon is automatic": Sanren bursts, Rappa pumps, Tomi is bolt-action, Bohiya is single-shot), `loading.tip.sw.01` ("A Special Weapon has no ammo and no reload": Specials now have ammo, a reload and a time cap, §7c), and `sw.03`, `sw.04`, `sw.05`, `tier.04` (Kagura Bell, Ofuda Loom and Tide Jewel are removed). The HUD banner "SPECIAL WEAPON ONLINE" / "<NAME> ONLINE" still fits. The 11 s ring copy in E14 (FIELD 4s) changes with the ammo/time-cap HUD; that string is S-09's once the layout is set.
- **Q8 · Tip-count mismatch.** WP-068 §15.4 plans 27 tips (24 + 3 binding hints) under `loading.*` keys and a 9-bit HINT mask. This pack delivers 30 new tips under `tips.NNN` as the content brief asks; 7 are HINT class; together with the 4 existing HINTs that stay true (`ctrl.02`, `tier.03`, `tier.05`, `surv.04`) that makes 11, beyond the 9-bit mask, so S-09 widens the mask or demotes some HINTs to FACT. Keys can be re-prefixed to `loading.tip.*` at wiring time; the text does not change.
- **Q9 · Decimal separator.** The current UI writes `1.5 s`. Turkish uses `1,5 sn`. The `tr` formats (`stat.fmt.seconds`) use `sn`; the separator comes from the number formatter, not from the string.
- **Q10 · Spelling inconsistency already in the catalog.** Names say "Armor Breaker" while descriptions say "armour". New names avoid the word; new body text uses "armour"; the branch label is "Armour Break" (the enum stays `ArmorBreak`).
- **Q11 · Lock order.** WP-069 §6 proposes sr_1 5th and smg_2 6th (0-based); WP-077 §3.3 says "S-02 proposal accepted: sr_1 6th, smg_2 7th" (1-based) and its table matches WP-069. Same order, different counting; the WEAPONS page follows the table.
- **Q12 · Numbers the docs fix vs. PROTOTYPE-ONLY.** WP-069 marks all its numbers PROTOTYPE-ONLY (to become DEC-096), and WP-077 likewise (DEC-097+). So even "fixed-looking" values (chain 8, Hearth ≤ 3 per round, magazine 100, Kōdō 6 shots) are placeholders here. The only exceptions are the identity counts named above. Mukade's flavour line ("A hundred legs") is the meaning of the name (centipede), not a magazine claim.
- **Q13 · Cosmetic bond page.** WP-077 §3.3 allows ≤ 12 cosmetic nodes but lists none, so only the page label (`path.page.cosmetic`) is written.
- **Q14 · Role tags vs. ranges.** WP-069 §1 gives role ranges in metres ("4–30 m"). They are left out of the ≤ 32-character tags because they are prototype values and would crowd a StatStrip that already shows RANGE.

## Voice rules used (for the next writer)

1. The rule first, the flavour second. A description says what the weapon *does* and what it *costs*, one sentence each.
2. Effect lines are data sentences: `Stat ±{value}%.`, no "increases your", no "by up to". Behaviour nodes (keystone, fork, capstone, seal) get one plain sentence of before → after.
3. Tips are ≤ 90 characters, two short sentences: a fact, then what to do about it. No strategy beyond what the rules imply; no numbers.
4. No real firearm, brand or game names. Specials use only Raiju and Namekuji.

<!-- TABLES:BEGIN (generated from the same data as r1-strings.json) -->

## 1 · Weapons and Specials

Key prefix `weapon.<id>.` → `name` · `class` · `firemode` · `role` · `desc` · `flavor`. The StatStrip column is wiring guidance for the Selection card (E9, two rows of two); it is not a string.

### `ar_1` · HINAWA — ASSAULT RIFLE · AUTO

| field | en | tr |
| --- | --- | --- |
| role tag (28 / 26 ch) | Steady pressure at any range | Her menzilde sürekli baskı |
| description | A matchlock rifle that keeps firing while the trigger is held. It is never the best answer and never a wrong one. | Tetik basılı kaldıkça ateş eden fitilli bir tüfek. Hiçbir zaman en iyi cevap değildir, hiçbir zaman yanlış da. |
| flavour | *The cord burns down. The rhythm stays.* | *Fitil kısalır. Ritim kalır.* |
| StatStrip | DAMAGE · FIRE RATE · MAGAZINE · RANGE | HASAR · ATIŞ HIZI · ŞARJÖR · MENZİL |

### `ar_2` · SANREN — ASSAULT RIFLE · BURST ×{count}

| field | en | tr |
| --- | --- | --- |
| role tag (30 / 24 ch) | Three-round burst for the head | Kafaya üç mermilik salvo |
| description | Each pull fires a tight burst of three. It pays more for a headshot than any other rifle and bites into armour. | Her tetik üç mermilik sıkı bir salvo atar. Kafa isabetini her tüfekten fazla ödüllendirir ve zırhı ısırır. |
| flavour | *One breath, three answers.* | *Tek nefes, üç cevap.* |
| StatStrip | DAMAGE · FIRE RATE · HEADSHOT · VS ARMOUR | HASAR · ATIŞ HIZI · KAFA ATIŞI · ZIRHA KARŞI |

### `smg_1` · SHIPPU — SUBMACHINE GUN · AUTO

| field | en | tr |
| --- | --- | --- |
| role tag (30 / 27 ch) | Close-range volume on the move | Hareket hâlinde yakın hacim |
| description | A light repeater that pours out shots and barely slows your feet. Plates shrug its rounds off, so pick the bare targets. | Mermi yağdıran, adımını neredeyse hiç yavaşlatmayan hafif bir tekrarlı. Plakalar mermisini siler; zırhsız hedefleri seç. |
| flavour | *Keep moving. The rain does.* | *Durma. Yağmur da durmuyor.* |
| StatStrip | FIRE RATE · MAGAZINE · HANDLING · VS ARMOUR | ATIŞ HIZI · ŞARJÖR · KULLANIM · ZIRHA KARŞI |

### `smg_2` · TETSUI — SUBMACHINE GUN · AUTO

| field | en | tr |
| --- | --- | --- |
| role tag (28 / 25 ch) | Stops a Runner in its tracks | Runner'ı yerinde durdurur |
| description | Heavy pistol rounds at a measured pace. Every hit shoves the target back, and a Runner hit in quick succession stops running. | Ölçülü hızda ağır tabanca mermisi. Her isabet hedefi geri iter; art arda vurulan Runner koşmayı bırakır. |
| flavour | *Each hit a nail. Each nail driven home.* | *Her vuruş bir çivi. Her çivi yerine.* |
| StatStrip | DAMAGE · STAGGER · MAGAZINE · FIRE RATE | HASAR · SENDELETME · ŞARJÖR · ATIŞ HIZI |

### `sg_1` · RAPPA — SHOTGUN · PUMP

| field | en | tr |
| --- | --- | --- |
| role tag (31 / 24 ch) | Clears the cone in front of you | Önündeki koniyi temizler |
| description | A bell-mouthed pump gun that throws a wide handful of shot. It loads shell by shell, and firing stops the reload with the loaded shells kept. | Geniş bir avuç saçma savuran, çan ağızlı bir pompalı. Fişekleri tek tek doldurur; ateş etmek dolumu keser, giren fişekler kalır. |
| flavour | *Close enough to hear them breathe.* | *Nefeslerini duyacak kadar yakın.* |
| StatStrip | DAMAGE · PELLETS · RANGE · RELOAD | HASAR · SAÇMA · MENZİL · DOLUM |

### `sg_2` · NITO — SHOTGUN · DOUBLE BARREL

| field | en | tr |
| --- | --- | --- |
| role tag (30 / 26 ch) | Two barrels, then you are open | İki namlu, sonra açıktasın |
| description | A break-action double that empties both barrels in a heartbeat, the hardest single blow in the arsenal. Then it must be broken open and reloaded while the crowd closes in. | İki namluyu bir anda boşaltan kırma çifte; cephaneliğin en sert tek darbesi. Sonra kalabalık yaklaşırken kırılıp doldurulmalıdır. |
| flavour | *Both blades drawn. There is no third.* | *İki kılıç çekildi. Üçüncüsü yok.* |
| StatStrip | DAMAGE · PELLETS · RELOAD · STAGGER | HASAR · SAÇMA · DOLUM · SENDELETME |

### `lmg_1` · MUKADE — LIGHT MACHINE GUN · AUTO

| field | en | tr |
| --- | --- | --- |
| role tag (27 / 25 ch) | Holds a corridor on one box | Tek kutuyla koridor tutar |
| description | A belt-fed gun with a deep ammunition box that keeps a lane shut for a long time. It is heavy to move and slow to reload. | Derin cephane kutulu, şeritli bir silah; bir hattı uzun süre kapalı tutar. Taşıması ağır, doldurması yavaştır. |
| flavour | *A hundred legs, all marching one way.* | *Yüz bacak, hepsi tek yöne.* |
| StatStrip | MAGAZINE · FIRE RATE · HANDLING · RELOAD | ŞARJÖR · ATIŞ HIZI · KULLANIM · DOLUM |

### `lmg_2` · KUROGANE — LIGHT MACHINE GUN · AUTO

| field | en | tr |
| --- | --- | --- |
| role tag (25 / 25 ch) | Breaks armour, settles in | Zırh kırar, ateşte oturur |
| description | Its first shots scatter, then the barrel settles and every round flies true. Each round punches through armour and into the body behind. | İlk atışları dağılır; sonra namlu oturur ve her mermi dosdoğru gider. Her mermi zırhı deler, arkadaki gövdeye geçer. |
| flavour | *Iron does not hurry. Iron arrives.* | *Demir acele etmez. Demir varır.* |
| StatStrip | DAMAGE · VS ARMOUR · PENETRATION · HANDLING | HASAR · ZIRHA KARŞI · DELME · KULLANIM |

### `sr_1` · TOMI — SNIPER RIFLE · BOLT

| field | en | tr |
| --- | --- | --- |
| role tag (31 / 20 ch) | One shot through the whole line | Tek atış, bütün sıra |
| description | A long bolt-action rifle that drives each shot through several bodies in a row. From the hip it is almost useless; aimed, it rarely misses. | Her atışı art arda birkaç gövdeden geçiren uzun, sürgülü bir tüfek. Kalçadan neredeyse işe yaramaz; nişanlıyken nadiren ıskalar. |
| flavour | *Silence first. Then the slap.* | *Önce sessizlik. Sonra tokat.* |
| StatStrip | DAMAGE · PENETRATION · HEADSHOT · ACCURACY | HASAR · DELME · KAFA ATIŞI · İSABET |

### `rl_1` · BOHIYA — LAUNCHER · SINGLE

| field | en | tr |
| --- | --- | --- |
| role tag (28 / 25 ch) | Area blast for packed crowds | Kalabalığa alan patlaması |
| description | A shoulder tube that launches a fire arrow, which speeds up in flight and bursts on contact. Too close and it will not arm; a little further and the blast reaches you too. | Uçarken hızlanan ve temasta patlayan bir ateş oku fırlatan omuz borusu. Çok yakında kurulmaz; biraz uzakta ise patlama sana da ulaşır. |
| flavour | *Light the fuse. Step back.* | *Fitili yak. Geri çekil.* |
| StatStrip | DAMAGE · BLAST RADIUS · SELF DAMAGE · RELOAD | HASAR · PATLAMA ÇAPI · KENDİNE HASAR · DOLUM |

### `sw_arc` · RAIJU — SPECIAL WEAPON · CHARGE

| field | en | tr |
| --- | --- | --- |
| role tag (31 / 27 ch) | Chain lightning through a crowd | Kalabalıkta zincir yıldırım |
| description | A charged orb that kills what it strikes, then leaps to the nearest untouched enemy, again and again. Stand too near the chain and it bites you as well. | Çarptığını öldüren, sonra en yakın dokunulmamış düşmana tekrar tekrar sıçrayan şarjlı bir küre. Zincire fazla yakın durursan seni de çarpar. |
| flavour | *The thunder beast does not choose. It spreads.* | *Yıldırım canavarı seçmez. Yayılır.* |
| StatStrip | CHAIN TARGETS · CHARGE · MAGAZINE · TIME LIMIT | ZİNCİR HEDEF · ŞARJ · ŞARJÖR · SÜRE SINIRI |

### `sw_gel` · NAMEKUJI — SPECIAL WEAPON · LOB

| field | en | tr |
| --- | --- | --- |
| role tag (32 / 29 ch) | Slick gel that topples the crowd | Kalabalığı deviren kaygan jel |
| description | Lobs a violet gel that spreads into a slick puddle where it lands. Enemies that step in slip and do not get up, and every fall leaves more gel. | Düştüğü yerde kaygan bir birikintiye yayılan mor bir jel fırlatır. İçine basan düşmanlar kayar ve kalkmaz; her düşüş yeni jel bırakır. |
| flavour | *The slug leaves a trail. The trail does the rest.* | *Sümüklü iz bırakır. Gerisini iz yapar.* |
| StatStrip | MAGAZINE · PUDDLE TIME · FIRE RATE · TIME LIMIT | ŞARJÖR · BİRİKİNTİ SÜRESİ · ATIŞ HIZI · SÜRE SINIRI |

### Shared labels

| key | en | tr |
| --- | --- | --- |
| `weapon.class.ar` | ASSAULT RIFLE | SALDIRI TÜFEĞİ |
| `weapon.class.smg` | SUBMACHINE GUN | MAKİNELİ TABANCA |
| `weapon.class.sg` | SHOTGUN | POMPALI |
| `weapon.class.lmg` | LIGHT MACHINE GUN | HAFİF MAKİNELİ TÜFEK |
| `weapon.class.sr` | SNIPER RIFLE | KESKİN NİŞANCI TÜFEĞİ |
| `weapon.class.rl` | LAUNCHER | ROKETATAR |
| `weapon.class.sw` | SPECIAL WEAPON | ÖZEL SİLAH |
| `weapon.firemode.auto` | AUTO | OTOMATİK |
| `weapon.firemode.burst` | BURST ×{count} | SALVO ×{count} |
| `weapon.firemode.pump` | PUMP | POMPA |
| `weapon.firemode.double` | DOUBLE BARREL | ÇİFTE NAMLU |
| `weapon.firemode.bolt` | BOLT | SÜRGÜ |
| `weapon.firemode.single` | SINGLE | TEK ATIŞ |
| `weapon.firemode.charge` | CHARGE | ŞARJ |
| `weapon.firemode.lob` | LOB | YAY ATIŞ |
| `stat.damage` | DAMAGE | HASAR |
| `stat.pellets` | PELLETS | SAÇMA |
| `stat.fire_rate` | FIRE RATE | ATIŞ HIZI |
| `stat.magazine` | MAGAZINE | ŞARJÖR |
| `stat.reserve` | RESERVE | YEDEK |
| `stat.reload` | RELOAD | DOLUM |
| `stat.range` | RANGE | MENZİL |
| `stat.accuracy` | ACCURACY | İSABET |
| `stat.handling` | HANDLING | KULLANIM |
| `stat.stagger` | STAGGER | SENDELETME |
| `stat.penetration` | PENETRATION | DELME |
| `stat.armour` | VS ARMOUR | ZIRHA KARŞI |
| `stat.headshot` | HEADSHOT | KAFA ATIŞI |
| `stat.velocity` | SHOT SPEED | MERMİ HIZI |
| `stat.blast_radius` | BLAST RADIUS | PATLAMA ÇAPI |
| `stat.self_damage` | SELF DAMAGE | KENDİNE HASAR |
| `stat.chain` | CHAIN TARGETS | ZİNCİR HEDEF |
| `stat.puddle` | PUDDLE TIME | BİRİKİNTİ SÜRESİ |
| `stat.charge` | CHARGE | ŞARJ |
| `stat.time_cap` | TIME LIMIT | SÜRE SINIRI |
| `stat.fmt.rpm` | `{value} RPM` | `{value} RPM` |
| `stat.fmt.seconds` | `{value} s` | `{value} sn` |
| `stat.fmt.metres` | `{value} m` | `{value} m` |
| `stat.fmt.mag_reserve` | `{value} / {reserve}` | `{value} / {reserve}` |
| `stat.fmt.pellets` | `{value} × {pellets}` | `{value} × {pellets}` |
| `stat.fmt.bodies` | `{value} BODIES` | `{value} GÖVDE` |
| `stat.fmt.multiplier` | `×{value}` | `×{value}` |
| `stat.fmt.before_after` | `{label} {before} → {after}` | `{label} {before} → {after}` |

## 2 · In-run tree (WP-077 §2)

Key: `tree.run.<id>.name` / `.desc`. Names ≤ 22 characters in both languages (checked). Weapon lane = 3 branch tiers (the weapon's `coreBranches`) + 3 branch keystones + fork X/Y + capstone + Hearth + Special link I/II + seal = 13 per weapon, assembled from the rows below. Body lane = 12 fixed nodes.

### 2.1 Branch tiers (weapon lane K1)

| id | kind | name en | name tr | effect en | effect tr | source |
| --- | --- | --- | --- | --- | --- | --- |
| `branch.piercing` | Tier | Piercing | Delme | Pierce count +{value}. | Delme sayısı +{value}. | UpgradeBranch.Piercing |
| `branch.ricochet` | Tier | Ricochet | Sekme | Ricochet count +{value}. | Sekme sayısı +{value}. | UpgradeBranch.Ricochet |
| `branch.explosion` | Tier | Explosion | Patlama | {value}% chance that a hit detonates. | İsabetin patlama şansı %{value}. | UpgradeBranch.Explosion (chance) |
| `branch.blast` | Tier | Blast | Patlama Gücü | Blast radius +{radius} m, blast damage +{value}%. | Patlama yarıçapı +{radius} m, patlama hasarı +%{value}. | rl_1 Explosion = Blast (WP-069 §6, WP-077 §2.3) |
| `branch.chain` | Tier | Chain | Zincir | Chain jumps +{value}. | Zincir sıçraması +{value}. | UpgradeBranch.Chain |
| `branch.fire_rate` | Tier | Fire Rate | Atış Hızı | Fire rate +{value}%. | Atış hızı +%{value}. | UpgradeBranch.FireRate |
| `branch.reload_speed` | Tier | Reload Speed | Dolum Hızı | Reload time −{value}%. | Dolum süresi −%{value}. | UpgradeBranch.ReloadSpeed |
| `branch.armor_break` | Tier | Armour Break | Zırh Kırma | Damage against armour +{value}%. | Zırha karşı hasar +%{value}. | new, WP-069 §6 (enum 10) |
| `branch.impact` | Tier | Impact | Darbe | Stagger +{value} per hit. | İsabet başına sendeletme +{value}. | new, WP-069 §6 (enum 11) |
| `branch.cluster` | Tier | Cluster | Salkım | The rocket scatters {count} bomblets, {value} damage each. | Roket her biri {value} hasarlı {count} bombacık saçar. | new, WP-069 §6 (enum 12) |

### 2.2 Branch keystones (weapon lane K2)

| id | kind | name en | name tr | effect en | effect tr | source |
| --- | --- | --- | --- | --- | --- | --- |
| `key.piercing` | BranchKeystone | Clean Line | Hat | Piercing loses no damage through the first {count} bodies. | Delme, ilk {count} gövdede hasar kaybetmez. | WP-077 §2.3 Hat |
| `key.ricochet` | BranchKeystone | Ricochet Master | Sekme Ustası | Ricochets search twice as far for their next target. | Sekmeler bir sonraki hedefi iki kat uzakta arar. | WP-077 §2.3 Sekme Ustası |
| `key.explosion` | BranchKeystone | Ash Cloud | Kül Bulutu | Explosions leave ground that burns for {seconds} s at {value}% of the blast per second. | Patlamalar {seconds} sn yanan bir alan bırakır; saniyede patlamanın %{value}'i. | WP-077 §2.3 Kül Bulutu |
| `key.chain` | BranchKeystone | Web | Ağ | The chain's first jump splits in {count}. | Zincirin ilk sıçraması {count}'ye çatallanır. | WP-077 §2.3 Ağ |
| `key.fire_rate` | BranchKeystone | Warm-Up | Isınma | Hold the trigger for {seconds} s and fire rate rises by {value}%. | Tetiği {seconds} sn basılı tut; atış hızı %{value} artar. | WP-077 §2.3 Isınma |
| `key.reload_speed` | BranchKeystone | Brimming Magazine | Dolu Şarjör | A reload from empty fills that magazine {value}% past full. | Boş şarjörden dolum, o şarjörü %{value} fazla doldurur. | WP-077 §2.3 Dolu Şarjör |
| `key.armor_break` | BranchKeystone | Plate Splitter | Zırh Sökücü | The first hit on each armour plate deals ×{value} damage. | Her zırh plakasına ilk isabet ×{value} hasar verir. | WP-077 §2.3 Zırh Sökücü |
| `key.impact` | BranchKeystone | Off Balance | Sendele | Staggered targets take {value}% more damage. | Sendeleyen hedefler %{value} fazla hasar alır. | WP-077 §2.3 Sendele |
| `key.cluster` | BranchKeystone | Seeds | Tohum | +{count} bomblet. Each one carries your Blast tier. | +{count} bombacık. Her biri Patlama Gücü kademeni taşır. | WP-077 §2.3 Tohum |

### 2.3 Hearth and Special links

| id | kind | name en | name tr | effect en | effect tr | source |
| --- | --- | --- | --- | --- | --- | --- |
| `hearth` | Hearth | Hearth | Ocak | Weapon damage +{value}% per stoke, up to {max} stokes. Bought outside the cards. | Her yakışta silah hasarı +%{value}, en çok {max} yakış. Kart dışında alınır. | WP-077 §2.1 Hearth |
| `link.raiju.1` | SpecialLink | Conductor | İletken | For the round you pick up Raiju, your classic weapon's Chain gains +{count} jump. | Raiju'yu aldığın round boyunca klasik silahının Zinciri +{count} sıçrama kazanır. | WP-077 §2.4 Bağ I (Q4: S-02 2026-09-25, window = the pickup round) |
| `link.raiju.2` | SpecialLink | Storm | Fırtına | Each Raiju kill refills {value}% of your classic magazine. | Her Raiju öldürmesi klasik şarjörünün %{value}'ini doldurur. | WP-077 §2.4 Bağ II |
| `link.namekuji.1` | SpecialLink | Slick Ground | Kaygan Zemin | Slowed or sliding enemies take {value}% more damage. | Yavaşlamış ya da kayan düşmanlar %{value} fazla hasar alır. | WP-077 §2.4 Bağ I |
| `link.namekuji.2` | SpecialLink | Gel Harvest | Jel Hasadı | An enemy that dies in gel drops +{value} gold. | Jelde ölen düşman +{value} altın bırakır. | WP-077 §2.4 Bağ II (Q5: C-00 2026-09-25, "Donma" → "Jel Hasadı") |

### 2.4 Per-weapon forks, capstones and seals

| id | kind | name en | name tr | effect en | effect tr | source |
| --- | --- | --- | --- | --- | --- | --- |
| `fork.ar_1.x` | Fork | Long Breath | Uzun Nefes | Bloom −{value}%. | Yayılma artışı −%{value}. | WP-077 §2.4 fork X |
| `fork.ar_1.y` | Fork | Short Barrel | Kısa Namlu | Fire rate +{value}%, bloom grows faster. | Atış hızı +%{value}, yayılma daha hızlı artar. | WP-077 §2.4 fork Y |
| `cap.ar_1` | Capstone | Ronin's Mark | Ronin Nişanı | One round in every {count} ignores armour and pierces without limit. | Her {count}. mermi zırhı yok sayar ve sınırsız deler. | WP-077 §2.4 capstone |
| `seal.ar_1` | WeaponSeal | Kodo | Kodo | After {count} shots in a row, bloom −{value}%. | Art arda {count} atıştan sonra yayılma artışı −%{value}. | WP-069 §6 keystone |
| `fork.ar_2.x` | Fork | Tight Burst | Sıkı Salvo | Delay between bursts −{value}%. | Salvolar arası bekleme −%{value}. | WP-077 §2.4 fork X |
| `fork.ar_2.y` | Fork | Long Burst | Uzun Salvo | Each burst fires {value} rounds instead of {base}. | Her salvo {base} yerine {value} mermi atar. | WP-077 §2.4 fork Y |
| `cap.ar_2` | Capstone | Third Eye | Üçüncü Göz | When a burst's third round hits the head, it ricochets on down the line. | Salvonun üçüncü mermisi kafaya isabet ederse zincirleme seker. | WP-077 §2.4 capstone |
| `seal.ar_2` | WeaponSeal | Kubi | Kubi | Headshot bonus ×{base} → ×{value}. | Kafa isabeti ek çarpanı ×{base} → ×{value}. | WP-069 §6 keystone |
| `fork.smg_1.x` | Fork | Twin Grip | Çift Kabza | Magazine ×{value}. | Şarjör ×{value}. | WP-077 §2.4 fork X |
| `fork.smg_1.y` | Fork | Hot Barrel | Kızgın Namlu | Warm-Up starts after {seconds} s of fire. | Isınma {seconds} sn ateşten sonra başlar. | WP-077 §2.4 fork Y |
| `cap.smg_1` | Capstone | Lead Rain | Kurşun Yağmuru | Ricochets bounce {count} times. | Sekmeler {count} kez seker. | WP-077 §2.4 capstone |
| `seal.smg_1` | WeaponSeal | Kaze | Kaze | Move speed while firing ×{value}. | Ateş ederken hareket hızı ×{value}. | WP-069 §6 keystone |
| `fork.smg_2.x` | Fork | Heavy Core | Ağır Çekirdek | Stagger lasts {value}% longer. | Sendeleme %{value} uzun sürer. | WP-077 §2.4 fork X |
| `fork.smg_2.y` | Fork | Quick Hammer | Hızlı Çekiç | Fire rate +{value}%. | Atış hızı +%{value}. | WP-077 §2.4 fork Y |
| `cap.smg_2` | Capstone | Lightning Fang | Yıldırım Dişi | One hit in every {count} on a staggered target is a sure Chain. | Sendeleyen hedefe her {count}. isabet kesin Zincir olur. | WP-077 §2.4 capstone |
| `seal.smg_2` | WeaponSeal | Tsuchi | Tsuchi | Stagger {base} → {value}. A Runner hit hard goes down. | Sendeletme {base} → {value}. Sert vurulan Runner yere düşer. | WP-069 §6 keystone |
| `fork.sg_1.x` | Fork | Tight Choke | Sıkı Saçma | Spread −{value}%. | Yayılma −%{value}. | WP-077 §2.4 fork X |
| `fork.sg_1.y` | Fork | Heavy Load | Geniş Saçma | +{count} pellets per shell. | Fişek başına +{count} saçma. | WP-077 §2.4 fork Y |
| `cap.sg_1` | Capstone | Ash Wall | Kül Duvarı | Shots within {range} m knock enemies back and break {count} plate. | {range} m içindeki atış düşmanı geri iter ve {count} plaka kırar. | WP-077 §2.4 capstone |
| `seal.sg_1` | WeaponSeal | Manin | Manin | Pellets {base} → {value}. | Saçma {base} → {value}. | WP-069 §6 keystone |
| `fork.sg_2.x` | Fork | Fire Mouth | Ateş Ağzı | Close-range damage +{value}%. | Yakın mesafe hasarı +%{value}. | WP-077 §2.4 fork X |
| `fork.sg_2.y` | Fork | Quick Break | Hızlı Kırma | Reload time −{value}%. | Dolum süresi −%{value}. | WP-077 §2.4 fork Y |
| `cap.sg_2` | Capstone | Two Swords | İki Kılıç | When both barrels hit one target, the second shell explodes. | İki namlu aynı hedefe isabet ederse ikinci fişek patlar. | WP-077 §2.4 capstone |
| `seal.sg_2` | WeaponSeal | Sohatsu | Sohatsu | Both barrels fire on one pull, {count} pellets at once. | Tek tetikte iki namlu birden, aynı anda {count} saçma. | WP-069 §6 keystone |
| `fork.lmg_1.x` | Fork | Bipod | Sehpa | Damage +{value}% while standing still. | Dururken hasar +%{value}. | WP-077 §2.4 fork X |
| `fork.lmg_1.y` | Fork | Sling | Kayış | No move speed penalty while firing. | Ateş ederken hareket cezası yok. | WP-077 §2.4 fork Y |
| `cap.lmg_1` | Capstone | Unbroken | Kesintisiz | After {count} hits, no reload is needed for {seconds} s. | {count} isabetten sonra {seconds} sn dolum gerekmez. | WP-077 §2.4 capstone |
| `seal.lmg_1` | WeaponSeal | Nagaya | Nagaya | Magazine {base} → {value}. | Şarjör {base} → {value}. | WP-069 §6 keystone |
| `fork.lmg_2.x` | Fork | Hot Box | Kızgın Kutu | Fire rate +{value}%. | Atış hızı +%{value}. | WP-077 §2.4 fork X |
| `fork.lmg_2.y` | Fork | Cold Barrel | Soğuk Namlu | The first {count} rounds after a reload deal +{value}%. | Dolumdan sonraki ilk {count} mermi +%{value} hasar verir. | WP-077 §2.4 fork Y |
| `cap.lmg_2` | Capstone | Black Iron | Kara Demir | A round that breaks a plate carries full damage into the body behind. | Plaka kıran mermi arkadaki gövdeye tam hasarla geçer. | WP-077 §2.4 capstone |
| `seal.lmg_2` | WeaponSeal | Ishizue | Ishizue | Settles in {value} shots instead of {base}, and tighter. | {base} yerine {value} atışta ve daha sıkı oturur. | WP-069 §6 keystone |
| `fork.sr_1.x` | Fork | Hold Breath | Nefes Tut | Wait {seconds} s before the shot for ×{value} damage. | Atıştan önce {seconds} sn bekle, ×{value} hasar. | WP-077 §2.4 fork X |
| `fork.sr_1.y` | Fork | Quick Bolt | Hızlı Sürgü | Fire rate +{value}%. | Atış hızı +%{value}. | WP-077 §2.4 fork Y |
| `cap.sr_1` | Capstone | One Bullet | Tek Kurşun | The first target's armour is ignored and the shot pierces without limit. | İlk hedefin zırhı yok sayılır, atış sınırsız deler. | WP-077 §2.4 capstone |
| `seal.sr_1` | WeaponSeal | Kabuto-wari | Kabuto-wari | Headshots ignore armour. | Kafa isabetleri zırhı yok sayar. | WP-069 §6 keystone |
| `fork.rl_1.x` | Fork | Short Fuse | Yakın Fitil | Smaller blast, damage +{value}%. | Daha küçük patlama, hasar +%{value}. | WP-077 §2.4 fork X |
| `fork.rl_1.y` | Fork | Long Fuse | Uzun Fitil | Blast radius +{value}%. | Patlama çapı +%{value}. | WP-077 §2.4 fork Y |
| `cap.rl_1` | Capstone | Ash Rain | Kül Yağmuru | The blast splits into {count} smaller blasts. | Patlama {count} küçük patlamaya bölünür. | WP-077 §2.4 capstone |
| `seal.rl_1` | WeaponSeal | Hibana | Hibana | The blast leaves ground burning for {seconds} s, {value} HP per second. | Patlama {seconds} sn yanan bir alan bırakır, saniyede {value} HP. | WP-069 §6 keystone |

### 2.5 Body lane (12)

| id | kind | name en | name tr | effect en | effect tr | source |
| --- | --- | --- | --- | --- | --- | --- |
| `body.move` | Tier | Movement | Hareket | Move speed +{value}%. | Hareket hızı +%{value}. | WP-077 §2.2 row 1 K1 |
| `body.key.escape` | BranchKeystone | Escape | Kaçış | After a hit, move speed +{value}% for {seconds} s. | Darbe aldıktan sonra {seconds} sn hareket hızı +%{value}. | WP-077 §2.2 row 1 K2 — effect PROPOSED |
| `body.fork.ash_step` | Fork | Ash Step | Kül Adımı | Take {value}% less damage while moving. | Hareket ederken %{value} az hasar al. | WP-077 §2.2 row 1 K3 — effect PROPOSED |
| `body.max_health` | Tier | Max Health | Azami Can | Maximum health +{value}. | Azami can +{value}. | WP-077 §2.2 row 2 K1 |
| `body.key.thick_skin` | BranchKeystone | Thick Skin | Kalın Deri | At full health, the next hit deals {value}% less. | Can tamken sonraki darbe %{value} az hasar verir. | WP-077 §2.2 row 2 K2 — effect PROPOSED |
| `body.fork.iron_knee` | Fork | Iron Knee | Demir Diz | Take {value}% less damage while standing still. | Dururken %{value} az hasar al. | WP-077 §2.2 row 2 K3 — effect PROPOSED |
| `body.cap.second_wind` | Capstone | Second Wind | İkinci Nefes | Once per run, a killing blow leaves you at {value} health instead. | Koşuda bir kez, öldürücü darbe seni {value} canla bırakır. | WP-077 §2.2 row 2 K4, §2.4 note (lethal-hit catch) |
| `body.regen_delay` | Tier | Regen Delay | Yenilenme Gecikmesi | Health starts to regenerate {value} s sooner. | Can {value} sn daha erken yenilenmeye başlar. | WP-077 §2.2 row 3 K1 |
| `body.key.breath` | BranchKeystone | Breath | Nefes | Each kill cuts the regeneration wait by {value} s. | Her öldürme yenilenme beklemesini {value} sn kısaltır. | WP-077 §2.2 row 3 K2 — effect PROPOSED |
| `body.purse` | Purse | Purse | Kese | Gold +{value}% for the rest of the run. | Koşunun kalanında altın +%{value}. | WP-077 §2.1 Purse |
| `body.regen_rate` | Tier | Regen Rate | Yenilenme Hızı | Health regenerates {value}% faster. | Can %{value} daha hızlı yenilenir. | WP-077 §2.2 row 4 K1 |
| `body.key.blood_step` | BranchKeystone | Blood Step | Kanlı Adım | Each kill restores {value} health. | Her öldürme {value} can yeniler. | WP-077 §2.2 row 4 K2 — effect PROPOSED |

### 2.6 Tree labels

| key | en | tr |
| --- | --- | --- |
| `tree.kind.Tier` | TIER | KADEME |
| `tree.kind.BranchKeystone` | KEYSTONE | KİLİT TAŞI |
| `tree.kind.Fork` | FORK | KAVŞAK |
| `tree.kind.WeaponSeal` | SEAL | MÜHÜR |
| `tree.kind.Capstone` | CAPSTONE | DORUK |
| `tree.kind.SpecialLink` | SPECIAL LINK | ÖZEL BAĞ |
| `tree.kind.Hearth` | HEARTH | OCAK |
| `tree.kind.Purse` | PURSE | KESE |
| `tree.lane.weapon` | WEAPON | SİLAH |
| `tree.lane.body` | BODY | BEDEN |
| `tree.state.locked` | LOCKED | KİLİTLİ |
| `tree.state.prereq_missing` | NEEDS {node} | ÖNCE {node} |
| `tree.state.available` | AVAILABLE | ALINABİLİR |
| `tree.state.unaffordable` | {value} GOLD SHORT | {value} ALTIN EKSİK |
| `tree.state.purchased` | OWNED | ALINDI |
| `tree.state.excluded` | CLOSED BY {node} | {node} KAPATTI |
| `tree.state.max_tier` | MAX | AZAMİ |
| `tree.flag.new` | NEW | YENİ |
| `tree.flag.pinned` | PINNED | İŞARETLİ |
| `tree.flag.not_offered` | NOT IN THIS OFFER | BU TEKLİFTE YOK |
| `tree.action.pin` | PIN | İŞARETLE |
| `tree.action.unpin` | UNPIN | BIRAK |
| `tree.action.stoke` | STOKE THE HEARTH | OCAĞI YAK |
| `tree.action.undo` | UNDO LAST | GERİ AL |
| `tree.action.reforge` | REFORGE | DÖV |
| `tree.hearth.count` | HEARTH {value} / {max} | OCAK {value} / {max} |
| `tree.hearth.round` | {value} of {max} stokes this round | Bu round {max} yakıştan {value} |
| `tree.pin.caption` | A pinned node comes in the next offer once it is reachable. | İşaretli düğüm erişilebilir olunca sonraki teklifte gelir. |
| `tree.undo.caption` | Full refund. Available until you continue. | Tam iade. Devam edene kadar geçerli. |
| `tree.reforge.caption` | Swap this fork's side once per run. The new side is paid in full. | Bu kavşağın tarafını koşuda bir kez değiştir. Yeni taraf tam ödenir. |
| `tree.fork.closed` | Taking one side closes the other for this run. | Bir tarafı almak diğerini bu koşu için kapatır. |
| `tree.path_total` | Path total {value} gold | Yol toplamı {value} altın |

## 3 · Meta PATH (WP-077 §3)

Key: `tree.meta.<id>.name` / `.desc` (the `meta.` prefix of the id is dropped in the key).

### PATH › WEAPONS

| id | name en | name tr | description en | description tr | source |
| --- | --- | --- | --- | --- | --- |
| `meta.weapon.ar_1` | Hinawa | Hinawa | Your first rifle. Open from the start. | İlk tüfeğin. Baştan açıktır. | WP-077 §3.3 order 0 |
| `meta.weapon.smg_1` | Shippu | Shippu | Unlocks Shippu for selection before a run. | Shippu koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.sg_1` | Rappa | Rappa | Unlocks Rappa for selection before a run. | Rappa koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.ar_2` | Sanren | Sanren | Unlocks Sanren for selection before a run. | Sanren koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.lmg_1` | Mukade | Mukade | Unlocks Mukade for selection before a run. | Mukade koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.sr_1` | Tomi | Tomi | Unlocks Tomi for selection before a run. | Tomi koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.smg_2` | Tetsui | Tetsui | Unlocks Tetsui for selection before a run. | Tetsui koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.sg_2` | Nito | Nito | Unlocks Nito for selection before a run. | Nito koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.lmg_2` | Kurogane | Kurogane | Unlocks Kurogane for selection before a run. | Kurogane koşu öncesi seçime açılır. | WP-077 §3.3 |
| `meta.weapon.rl_1` | Bohiya | Bohiya | Unlocks Bohiya for selection before a run. | Bohiya koşu öncesi seçime açılır. | WP-077 §3.3 |

### PATH › SPECIAL

| id | name en | name tr | description en | description tr | source |
| --- | --- | --- | --- | --- | --- |
| `meta.special.raiju.lure` | Raiju Lure | Çekim I · Raiju | Raiju drops {value}% more often. | Raiju %{value} daha sık düşer. | WP-077 §3.3 Special K1 |
| `meta.special.namekuji.lure` | Namekuji Lure | Çekim I · Namekuji | Namekuji drops {value}% more often. | Namekuji %{value} daha sık düşer. | WP-077 §3.3 Special K1 |
| `meta.special.raiju.foresight` | Raiju Foresight | Önsezi · Raiju | Raiju's first link can be offered before you pick Raiju up. | Raiju'nun ilk bağı Raiju'yu almadan önce teklif edilebilir. | WP-077 §3.3 Special K2 |
| `meta.special.namekuji.foresight` | Namekuji Foresight | Önsezi · Namekuji | Namekuji's first link can be offered before you pick Namekuji up. | Namekuji'nin ilk bağı Namekuji'yi almadan önce teklif edilebilir. | WP-077 §3.3 Special K2 |
| `meta.special.early_bell` | Early Bell | Erken Çan | The first Special of a run drops by round {value}. | Koşunun ilk Özel silahı en geç {value}. roundda düşer. | WP-077 §3.3 Special K2 shared |
| `meta.special.raiju.link2` | Raiju: Second Link | Bağ II · Raiju | Raiju's second link can appear in the run tree. | Raiju'nun ikinci bağı koşu ağacında çıkabilir. | WP-077 §3.3 Special K3 |
| `meta.special.namekuji.link2` | Namekuji: Second Link | Bağ II · Namekuji | Namekuji's second link can appear in the run tree. | Namekuji'nin ikinci bağı koşu ağacında çıkabilir. | WP-077 §3.3 Special K3 |
| `meta.special.raiju.apex` | Raiju Apex | Raiju Doruğu | Raiju stays in hand {value}% longer. One apex is active per run. | Raiju %{value} daha uzun elde kalır. Koşuda tek doruk etkindir. | WP-077 §3.3 Special K4 |
| `meta.special.namekuji.apex` | Namekuji Apex | Namekuji Doruğu | Namekuji puddles cover {value}% more ground. One apex is active per run. | Namekuji birikintileri %{value} daha geniş alan kaplar. Koşuda tek doruk etkindir. | WP-077 §3.3 Special K4 |
| `meta.special.twin_bell` | Twin Bell | Çift Çan | The other Special can also drop in the same run. | Aynı koşuda diğer Özel silah da düşebilir. | WP-077 §3.3 Special K4 shared |

### PATH › PASSIVES

| id | name en | name tr | description en | description tr | source |
| --- | --- | --- | --- | --- | --- |
| `meta.passive.econ.second_pin` | Second Pin | Nişan II | Pin {value} nodes at once in the run tree. | Koşu ağacında aynı anda {value} düğüm işaretle. | WP-077 §3.3 Economy K1 |
| `meta.passive.econ.cheap_reroll` | Cheaper Reroll | Ucuz Reroll | Reroll costs {value} gold instead of {base}. | Reroll {base} yerine {value} altın. | WP-077 §3.3 Economy K2 |
| `meta.passive.econ.hearth_slope` | Banked Hearth | Ocak Eğimi | Each Hearth stoke costs {value} more than the last, instead of {base}. | Her Ocak yakışı öncekinden {base} yerine {value} fazla tutar. | WP-077 §3.3 Economy K3 |
| `meta.passive.econ.early_purse` | Purse from the Start | Açılışta Kese | Every run starts with the Purse. | Her koşu Kese ile başlar. | WP-077 §3.3 Economy K4 (Q6: C-00 2026-09-25, the Purse is owned from the start) |
| `meta.passive.econ.wide_offer` | Wider First Offer | Geniş İlk Teklif | The round 1 offer shows {value} cards. | 1. round teklifi {value} kart gösterir. | WP-077 §3.3 Economy K5 |
| `meta.passive.econ.capstone_discount` | Capstone Discount | Doruk İndirimi | Capstones cost {value}% less. | Doruklar %{value} daha ucuz. | WP-077 §3.3 Economy K6 |
| `meta.passive.body.1` | Light Feet I | Hafif Adım I | Move speed +{value}%. | Hareket hızı +%{value}. | WP-077 §3.3 Body K1 |
| `meta.passive.body.2` | Hardy I | Dayanıklı I | Maximum health +{value}. | Azami can +{value}. | WP-077 §3.3 Body K2 |
| `meta.passive.body.3` | Quick Recovery I | Çabuk Toparlanma I | Health starts to regenerate {value} s sooner. | Can {value} sn daha erken yenilenmeye başlar. | WP-077 §3.3 Body K3 |
| `meta.passive.body.4` | Light Feet II | Hafif Adım II | Move speed +{value}%. | Hareket hızı +%{value}. | WP-077 §3.3 Body K4 |
| `meta.passive.body.5` | Hardy II | Dayanıklı II | Maximum health +{value}. | Azami can +{value}. | WP-077 §3.3 Body K5 |
| `meta.passive.body.6` | Quick Recovery II | Çabuk Toparlanma II | Health starts to regenerate {value} s sooner. | Can {value} sn daha erken yenilenmeye başlar. | WP-077 §3.3 Body K6 |
| `meta.passive.qol.tree_preview` | Tree Preview | Ağaç Önizleme | Browse any weapon's run tree from the main menu. | Her silahın koşu ağacına ana menüden göz at. | WP-077 §3.3 QoL K1 |
| `meta.passive.qol.offer_history` | Offer History | Teklif Geçmişi | See every card offered so far in this run. | Bu koşuda şimdiye dek teklif edilen her kartı gör. | WP-077 §3.3 QoL K2 |
| `meta.passive.qol.run_stats` | Run Ledger | Koşu İstatistikleri | A run statistics panel on the round-end screen. | Round sonu ekranında koşu istatistik paneli. | WP-077 §3.3 QoL K3 — placement ASSUMED |
| `meta.passive.qol.before_after` | Before and After | Önce ve Sonra | Cards show the full before → after line for every value. | Kartlar her değer için tam önce → sonra satırını gösterir. | WP-077 §3.3 QoL K4 |
| `meta.passive.qol.path_tracker` | Path Tracker | Yol Göstergesi | The HUD marks your pinned node and what it still needs. | HUD işaretli düğümünü ve eksiğini gösterir. | WP-077 §3.3 QoL K5 |
| `meta.passive.qol.magnet` | Wider Magnet | Geniş Mıknatıs | Gold pickup reach +{value} m. | Altın toplama menzili +{value} m. | WP-077 §3.3 QoL K6 (🔴 product decision) |

### PATH labels

| key | en | tr |
| --- | --- | --- |
| `path.title` | PATH | YOL |
| `path.currency` | Ash | Kül |
| `path.currency.amount` | {value} Ash | {value} Kül |
| `path.currency.earned` | +{value} Ash | +{value} Kül |
| `path.page.weapons` | WEAPONS | SİLAHLAR |
| `path.page.special` | SPECIAL | ÖZEL |
| `path.page.passives` | PASSIVES | PASİFLER |
| `path.page.cosmetic` | COSMETIC | KOZMETİK |
| `path.row.economy` | ECONOMY | EKONOMİ |
| `path.row.body` | BODY | BEDEN |
| `path.row.comfort` | COMFORT | KOLAYLIK |
| `path.lock.line` | PATH · {kul} Ash · L{level} | YOL · {kul} Kül · L{level} |
| `path.lock.ready` | PATH › ready | YOL › hazır |
| `path.gate.level` | Opens at level {level} | {level}. seviyede açılır |
| `path.next` | NEXT | SIRADAKİ |
| `path.next.line` | Next: {node} · {value} Ash away | Sıradaki: {node} · {value} Kül kaldı |
| `path.owned` | OWNED | SAHİPSİN |
| `path.buy.hold` | HOLD TO TAKE | ALMAK İÇİN BASILI TUT |
| `path.respec` | RESET PASSIVES | PASİFLERİ SIFIRLA |
| `path.respec.body` | Passives and the Special leaning are refunded at {value}%. Weapons stay unlocked. | Pasifler ve Özel eğilim %{value} iade edilir. Silahlar açık kalır. |
| `path.respec.first_free` | The first reset is free. | İlk sıfırlama ücretsizdir. |
| `path.rule` | The PATH never adds damage. It opens weapons, Special leanings and options. | YOL hasar eklemez. Silahları, Özel eğilimleri ve seçenekleri açar. |
| `path.apex.choose` | Choose one apex for this run. | Bu koşu için bir doruk seç. |
| `path.mastery` | MASTERY {value} | USTALIK {value} |
| `path.mastery.m2` | A second fork option appears for this weapon. | Bu silah için ikinci kavşak seçeneği görünür. |
| `path.mastery.m3` | This weapon's capstone can now appear in offers. | Bu silahın doruğu artık tekliflerde çıkabilir. |
| `path.mastery.m5` | Cosmetic seal earned for this weapon. | Bu silah için kozmetik mühür kazanıldı. |
| `path.results.delta` | PATH +{value} Ash | YOL +{value} Kül |

## 4 · Loading-screen tips (30)

Key `tips.NNN`. Group / class follow `UiLoadingTips` (FACT = rule, HINT = onboarding, shown while hints are on). Length checked ≤ 90 in both languages.

| key | group | class | en | ch | tr | ch |
| --- | --- | --- | --- | --- | --- | --- |
| `tips.001` | Controls | HINT | Change the camera from the Pause menu: top-down, third-person or first-person. | 78 | Kamerayı Pause menüsünden değiştir: tepeden, üçüncü şahıs ya da birinci şahıs. | 78 |
| `tips.002` | Controls | FACT | Top-down reads the crowd best. First-person aims best but sees nothing behind you. | 82 | Tepeden kalabalığı en iyi okur. Birinci şahıs en iyi nişan alır ama arkanı görmez. | 82 |
| `tips.003` | Controls | HINT | In top-down, hold focus to tighten your spread. You move slower while focused. | 78 | Tepeden modda odak tuşunu basılı tut, yayılma daralır. Odaktayken yavaşlarsın. | 78 |
| `tips.004` | Controls | FACT | In third- and first-person, recoil really lifts your aim. Pull down to hold it. | 79 | Üçüncü ve birinci şahısta geri tepme nişanı gerçekten kaldırır. Aşağı çekerek tut. | 82 |
| `tips.005` | Controls | FACT | Rounds, enemies and rewards are the same in every camera mode. | 62 | Roundlar, düşmanlar ve ödüller her kamera modunda aynıdır. | 58 |
| `tips.006` | Controls | FACT | Every shot is a travelling round. Slower guns need a lead on a distant Runner. | 78 | Her atış yol alan bir mermidir. Yavaş silahlar uzaktaki Runner'ın önüne nişan ister. | 84 |
| `tips.007` | Tiers | FACT | Armour covers the head, the upper body and the legs. Each plate breaks on its own. | 82 | Zırh kafayı, üst gövdeyi ve bacakları örter. Her plaka ayrı kırılır. | 68 |
| `tips.008` | Tiers | FACT | Shoot where the armour is not. A bare region takes the hit in full. | 67 | Zırhın olmadığı yere ateş et. Açık bölge darbenin tamamını alır. | 64 |
| `tips.009` | Tiers | FACT | A piercing round melts the armour it passes, but each body still counts. | 72 | Delen mermi geçtiği zırhı eritir, ama her gövde yine sayılır. | 61 |
| `tips.010` | Tiers | FACT | A blast is split across head, body and legs. Armoured regions soak their share. | 79 | Patlama kafa, gövde ve bacaklara bölünür. Zırhlı bölgeler kendi payını yutar. | 77 |
| `tips.011` | Special | FACT | A Special carries its own ammunition. When it runs dry, your classic weapon returns. | 84 | Özel silah kendi cephanesini taşır. Bitince klasik silahın geri döner. | 70 |
| `tips.012` | Special | FACT | A Special also has a time limit. Spend its ammunition before the limit does. | 76 | Özel silahın bir süre sınırı da vardır. Cephanesini süre bitmeden harca. | 72 |
| `tips.013` | Special | FACT | One Special at a time. Picking up another drops the first and its ammunition. | 77 | Aynı anda tek Özel silah. Diğerini almak ilkini cephanesiyle birlikte düşürür. | 78 |
| `tips.014` | Special | HINT | Raiju's chain leaps to the nearest untouched enemy. Keep the chain off yourself. | 80 | Raiju zinciri en yakın dokunulmamış düşmana sıçrar. Zinciri kendinden uzak tut. | 79 |
| `tips.015` | Special | HINT | Release fire while Raiju is charging and the shot is cancelled. No ammo is spent. | 81 | Raiju şarj olurken tetiği bırakırsan atış iptal olur. Cephane harcanmaz. | 72 |
| `tips.016` | Special | HINT | Namekuji gel topples whatever steps in, and every fall leaves more gel behind. | 78 | Namekuji jeli içine basanı devirir; her düşüş arkasında yeni jel bırakır. | 73 |
| `tips.017` | Special | FACT | You slide fast on Namekuji gel and steer poorly. Use it to escape, not to aim. | 78 | Namekuji jelinde hızlı kayar, zor dönersin. Kaçmak için kullan, nişan için değil. | 81 |
| `tips.018` | Economy | HINT | The whole run tree is visible from round 1. Plan a path, then pin its next node. | 80 | Koşu ağacının tamamı 1. rounddan görünür. Bir yol planla, sonraki düğümünü işaretle. | 84 |
| `tips.019` | Economy | FACT | A pinned node comes in the next offer once it is reachable. Rerolls keep it. | 76 | İşaretli düğüm erişilebilir olunca sonraki teklifte gelir. Reroll onu değiştirmez. | 82 |
| `tips.020` | Economy | FACT | The Hearth sits outside the cards and never runs out. Stoke it when gold piles up. | 82 | Ocak kartların dışındadır ve tükenmez. Altın birikince yak. | 59 |
| `tips.021` | Economy | FACT | Forks are either-or. Taking one side closes the other for the rest of the run. | 78 | Kavşaklar ya o ya bu. Bir tarafı almak diğerini koşunun kalanında kapatır. | 74 |
| `tips.022` | Economy | FACT | A wrong buy can be undone on the same screen, as long as you have not continued. | 80 | Yanlış alım aynı ekranda geri alınabilir, yeter ki devam etmemiş ol. | 68 |
| `tips.023` | Economy | FACT | Each weapon has its own seal. A keystone on the lane opens the way to it. | 73 | Her silahın kendi mührü vardır. Şeritteki bir kilit taşı ona yol açar. | 70 |
| `tips.024` | Economy | FACT | Every run earns Ash for the PATH. Your level decides which gates are open. | 74 | Her koşu YOL için Kül kazandırır. Hangi kapıların açık olduğunu seviyen belirler. | 81 |
| `tips.025` | Economy | FACT | The PATH never adds damage. It opens weapons, Special leanings and options. | 75 | YOL hasar eklemez. Silahları, Özel eğilimleri ve seçenekleri açar. | 66 |
| `tips.026` | Survival | FACT | Bohiya will not arm up close. Fired too near, the rocket drops and fizzles. | 75 | Bohiya yakında kurulmaz. Çok yakına atılan roket düşer ve söner. | 64 |
| `tips.027` | Survival | FACT | Bohiya's blast can reach you too. Give a nearby pack some room before firing. | 77 | Bohiya patlaması sana da ulaşabilir. Yakındaki sürüye ateşten önce mesafe ver. | 78 |
| `tips.028` | Survival | FACT | Rappa reloads shell by shell. Fire at any moment and the loaded shells stay. | 76 | Rappa fişek fişek dolar. İstediğin an ateş et; giren fişekler kalır. | 68 |
| `tips.029` | Survival | FACT | Kurogane starts wide and settles as you hold fire. Short bursts waste the settle. | 81 | Kurogane geniş başlar, ateş sürdükçe oturur. Kısa atışlar oturmayı boşa harcar. | 79 |
| `tips.030` | Survival | HINT | Tomi drives each shot through a line of bodies. Let them queue up, then fire. | 77 | Tomi her atışı bir sıra gövdeden geçirir. Sıraya girmelerini bekle, sonra ateş et. | 82 |

## 5 · Camera-mode labels (Pause selector, WP-070 §5.1)

| key | en | tr |
| --- | --- | --- |
| `camera.title` | CAMERA | KAMERA |
| `camera.mode.top_down` | TOP-DOWN | TEPEDEN |
| `camera.mode.third_person` | THIRD-PERSON | ÜÇÜNCÜ ŞAHIS |
| `camera.mode.first_person` | FIRST-PERSON | BİRİNCİ ŞAHIS |
| `camera.mode.top_down.desc` | The whole courtyard in view. Aim with the cursor. | Bütün avlu görünür. İmleçle nişan al. |
| `camera.mode.third_person.desc` | Over the shoulder. Aim from the centre of the screen. | Omuz üstünden. Ekranın ortasından nişan al. |
| `camera.mode.first_person.desc` | Eye level. The sharpest aim; threats behind you show at the edge. | Göz hizası. En keskin nişan; arkandaki tehdit kenarda görünür. |
| `camera.caption` | The view changes as soon as you choose. Rounds and rules stay the same. | Görünüm seçer seçmez değişir. Roundlar ve kurallar aynı kalır. |
| `camera.fov.third_person` | THIRD-PERSON FOV | ÜÇÜNCÜ ŞAHIS GÖRÜŞ ALANI |
| `camera.fov.first_person` | FIRST-PERSON FOV | BİRİNCİ ŞAHIS GÖRÜŞ ALANI |
| `camera.shoulder` | SHOULDER | OMUZ |
| `camera.shoulder.left` | LEFT | SOL |
| `camera.shoulder.right` | RIGHT | SAĞ |
| `camera.head_bob` | HEAD BOB | KAFA SALLANTISI |
| `camera.focus` | FOCUS | ODAK |

S-09 (R1-A, 24.09):
- `camera.mode.first_person.desc` now names the rear-edge arc, which marks a threat behind in first person (WP-070 §7.1, AD R1-A SHOULD-2); "nothing behind you" contradicted it.
- `camera.fov.*` `en` is shortened to "… FOV" so the Settings label column (240 px) holds it at TextScale 1.25. The `tr` column is unchanged.

## 6 · Achievements for the new weapons and Specials (12)

Key `achievement.<id>.name` / `.desc`. Threshold column is a proposal for S-04 (catalog JSON owns the number; the string reads it through `{count}` / `{round}`).

| id | name en | name tr | description en | description tr | scope | rarity | proposed threshold | condition (new) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `raiju_full_chain` | Thunder Beast | Yıldırım Canavarı | Hit {count} enemies with one Raiju shot. | Tek Raiju atışıyla {count} düşman vur. | SingleRun | Uncommon | 8 (= chain max) | new: SpecialChainHits |
| `raiju_kills_100` | Storm Keeper | Fırtına Bekçisi | Defeat {count} enemies with Raiju in total. | Raiju ile toplam {count} düşman yen. | Lifetime | Rare | 100 | new: SpecialKills(sw_arc) |
| `raiju_grounded` | Grounded | Topraklı | Empty a full Raiju without shocking yourself. | Kendini çarpmadan bir Raiju'yu baştan sona boşalt. | SingleRun | Uncommon | — | new: SpecialSpentNoSelfShock |
| `namekuji_puddles` | Slick Courtyard | Kaygan Avlu | Have {count} gel puddles on the ground at once. | Yerde aynı anda {count} jel birikintisi olsun. | SingleRun | Rare | 12 (= puddle cap) | new: ConcurrentPuddles |
| `namekuji_executioner` | Down Goes the Axe | Balta Yere | Knock down an Executioner with Namekuji gel. | Bir Executioner'ı Namekuji jeliyle devir. | Lifetime | Uncommon | 1 | new: SpecialKnockdown(Ultra) |
| `namekuji_skater` | Rain Skater | Yağmur Patencisi | Defeat {count} enemies while they slide on gel in one run. | Bir koşuda jelde kayan {count} düşman yen. | SingleRun | Rare | 25 | new: KillsWhileSliding |
| `tomi_line` | String of Pearls | İnci Dizisi | Defeat {count} enemies with one Tomi shot. | Tek Tomi atışıyla {count} düşman yen. | SingleRun | Rare | 4 | new: KillsPerShot(sr_1) |
| `bohiya_one_rocket` | Fire Arrow | Ateş Oku | Defeat {count} enemies with one Bohiya rocket. | Tek Bohiya roketiyle {count} düşman yen. | SingleRun | Uncommon | 6 | new: KillsPerShot(rl_1) |
| `nito_double` | Both Blades | İki Kılıç | Defeat a Guard with both of Nito's barrels in one breath. | Nito'nun iki namlusuyla bir nefeste bir Guard yen. | Lifetime | Uncommon | 1 | new: KillWithinWindow(sg_2, 2 shots) |
| `kurogane_plates` | Settled Iron | Oturmuş Demir | Break {count} armour pieces with one Kurogane magazine. | Tek Kurogane şarjörüyle {count} zırh parçası kır. | SingleRun | Rare | 10 | new: PlatesPerMagazine(lmg_2) |
| `tetsui_runners` | Hammer Down | Çekiç İndi | Stop {count} Runners with Tetsui in one run. | Bir koşuda Tetsui ile {count} Runner durdur. | SingleRun | Uncommon | 15 | new: TierKills(Special) per weapon |
| `full_arsenal` | Ten Answers | On Cevap | Reach round {round} with each of the ten classic weapons. | On klasik silahın her biriyle {round}. rounda ulaş. | Lifetime | Epic | 10 | new: ReachRoundPerWeapon |

## 7 · HUD status stamps, the gel hint and the ammo word (S-09, R1-B / 12b)

| key | en | tr |
| --- | --- | --- |
| `hud.status.shock` | SHOCK | ŞOK |
| `hud.status.slide` | SLIDE | KAYMA |
| `hud.status.down` | DOWN | YERDE |
| `hud.slick` | SLICK | KAYGAN |
| `hud.ammo.low` | LOW AMMO | AZ MERMİ |

S-09 (R1-B, 25.09):
- Added for N19 / DEC-094. The stamps replace NET / DAZED, which went with the old Specials. SHOCK marks a body held by Raiju's chain. SLIDE marks a body the gel is carrying. DOWN marks a body that falls, lies or gets up.
- `hud.slick` is the player's own hint in the bottom-centre prompt slot while they stand in gel (N19-DIRECTION §2). One word, like the stamps. It gives no duration, because the puddle has none to show.
- The stamp chip is as wide as its longest word; `tr` YERDE and KAYMA fit the same chip at TextScale 1.25.

S-09 (12b, 25.09, AD FP cluster SHOULD-2):
- `hud.ammo.low` replaces the bare "LOW" over the magazine count, in every camera mode. In first person the weapon cluster stands on the health block, so the health LOW (coral) and the ammo word (amber) share one column; the noun keeps one word from meaning two things in one group. Health keeps its bare LOW beside the heart.
- It sits in the weapon cluster's 220 px word slot, the same slot as EMPTY and SUSPENDED; LOW AMMO fits it at TextScale 1.25.

## 8 · Selection (S-09, R1-B)

| key | en | tr |
| --- | --- | --- |
| `selection.specials.title` | SPECIAL WEAPONS | ÖZEL SİLAHLAR |
| `selection.specials.caption` | Found on the field, never chosen here. | Sahada bulunur, burada seçilmez. |
| `selection.stats.caption` | Bars compare with the other classic weapons. Longer is better. | Çubuklar diğer klasik silahlarla karşılaştırır. Uzun olan daha iyidir. |
| `selection.core` | CORE | ÇEKİRDEK |
| `selection.carried` | Carried for the whole run. It cannot be swapped. | Koşu boyunca taşınır. Değiştirilemez. |
| `selection.lock.level` | Reach profile level {level} · you are level {current} | Profil seviyesi {level} gerekir · şu an {current} |
| `selection.not_in_build` | Not in this build. | Bu sürümde yok. |

S-09 (R1-B, 25.09):
- The Selection stage (E9 v3) shows one weapon at a time, the one under focus. These lines are the stage's own labels; the weapon texts are section 1.
- `selection.stats.caption` explains the StatStrip bars. Each bar compares the value with the classic roster, and for RELOAD, ACCURACY and SELF DAMAGE less is better, so longer always means better. A stat only one weapon has (BLAST RADIUS, SELF DAMAGE) prints its number with no bar.
- `selection.lock.level` is the Ladder line (GD P14) until S-04 B2b's `ProfileService.WeaponLock` lands. After that, a meta-PATH weapon reads `path.lock.line` / `path.lock.ready`.


S-09 (R1-B, 25.09, C-00 decision): the meta currency is **Ash** in `en` and **Kül** in `tr`. The UI shows English only, and the English-text test takes no exception. Every `en` value above that read "Kül" now reads "Ash"; `tr` is unchanged. A9 is superseded for `en`.
## 9 · Tree screen (S-09, R1-C)

| key | en | tr |
| --- | --- | --- |
| `tree.flag.offer` | OFFER | TEKLİF |
| `tree.lock.profile` | Earn it on the PATH first. | Önce YOL'da kazan. |
| `tree.lock.weapon` | Not a branch of the weapon you carry. | Taşıdığın silahın dalı değil. |
| `tree.lock.special` | Opens when you pick up a Special this run. | Bu koşuda bir Special aldığında açılır. |
| `tree.lock.pending` | Not in this build yet. | Bu sürümde henüz yok. |
| `tree.picks_used` | This round's picks are used. | Bu round'un seçimleri kullanıldı. |
| `tree.refuse.gold` | Not enough gold for that yet. Nothing was taken. | Bunun için altın henüz yetmiyor. Hiçbir şey alınmadı. |
| `tree.refuse.limit` | No more of that this round. Nothing was taken. | Bu round bundan fazlası yok. Hiçbir şey alınmadı. |
| `tree.refuse.not_offered` | That is not for sale in this offer. Nothing was taken. | Bu teklifte satılmıyor. Hiçbir şey alınmadı. |
| `tree.refuse.nothing_to_undo` | There is nothing to undo. | Geri alınacak bir şey yok. |
| `tree.refuse.undo_closed` | Undo has closed for this offer. | Bu teklif için geri alma kapandı. |
| `tree.refuse.reforge_used` | The run's one Reforge is spent. | Koşunun tek Yeniden Dövme hakkı kullanıldı. |
| `tree.refuse.reforge_early` | Reforge opens from round 5. | Yeniden Dövme 5. round'dan itibaren açılır. |
| `tree.refuse.no_run` | No run is in progress. | Süren bir koşu yok. |
| `tree.state.prereq_keystone` | NEEDS A KEYSTONE | ÖNCE BİR KİLİT TAŞI |
| `tree.action.stoke_short` | STOKE | YAK |
| `tree.gate.shut` | ANY KEYSTONE | HERHANGİ BİR KİLİT TAŞI |
| `tree.gate.open` | KEYSTONE OWNED | KİLİT TAŞI VAR |

S-09 (R1-C, 25.09):
- The in-run tree screen (N14) marks the nodes this offer sells with an OFFER chip and a gold ring (`tree.flag.offer`); a node that is open but not offered reads `tree.flag.not_offered` in the detail card.
- `tree.lock.*` is the detail card's sentence for a LOCKED node, one per RunTreeLockReason (ProfileLocked, NotOnThisWeapon, NoSpecialYet, EffectPending).
- `tree.state.prereq_keystone` is the Hearth's NEEDS word: its prerequisite is any weapon-lane keystone, not one parent.
- `tree.action.stoke_short` replaces `tree.action.stoke` on BUY only when the detail card is too narrow for the full verb (Mac 1.2 × 1.25); the card's kind line and name already say HEARTH.
- `tree.gate.*` is the word at the head of the keystone gate (the line before forks / seal / Hearth, which open on any keystone of the lane, WP-077 §2.2): shut / open. The gated nodes read `tree.state.prereq_keystone` (AD R1-C MUST-3b).
- `tree.refuse.*` is the footer sentence when a tree command is refused (RunTreeCommandResult). A refused card purchase keeps the shop's existing sentence for its cause.

## 10 · Pause · run so far (S-09, R1-C)

| key | en | tr |
| --- | --- | --- |
| `pause.run.title` | RUN SO FAR | ŞU ANA KADAR |
| `pause.run.round` | ROUND | ROUND |
| `pause.run.time` | TIME | SÜRE |
| `pause.run.kills` | KILLS | ÖLDÜRME |
| `pause.run.gold` | GOLD | ALTIN |
| `pause.run.upgrades` | UPGRADES | GELİŞTİRME |
| `pause.run.build_none` | no upgrades | geliştirme yok |

S-09 (R1-C, 25.09):
- Direction E25's read-only strip on the Pause card: five value-over-label cells and the build line under them. The values come from the producers the HUD and Results already quote (RunEvents, the wallet, RunBuildState), so the three screens agree.
- `pause.run.upgrades` counts the upgrades bought this run. The build line reads "WEAPON · build" and falls back to `pause.run.build_none`.

## 11 · Results v3 (S-09, R1-D)

| key | en | tr |
| --- | --- | --- |
| `results.tile.round` | ROUND | ROUND |
| `results.tile.kills` | KILLS | ÖLDÜRME |
| `results.tile.gold` | GOLD | ALTIN |
| `results.tile.time` | TIME | SÜRE |
| `results.report` | RUN REPORT | KOŞU RAPORU |
| `results.combat` | COMBAT | ÇATIŞMA |
| `results.run` | RUN | KOŞU |
| `results.rewards` | REWARDS | ÖDÜLLER |
| `results.xp` | EXPERIENCE | DENEYİM |
| `results.new_best` | NEW BEST · ROUND {value} | YENİ REKOR · ROUND {value} |
| `results.empty` | No new unlocks this run. | Bu koşuda yeni açılan yok. |
| `results.unlocked_count` | {value} new, already added to your profile | {value} yeni, profiline zaten eklendi |
| `results.next_unlock` | Next unlock: {name} at level {level} · {away} levels away | Sıradaki açılış: {name}, seviye {level} · {away} seviye kaldı |
| `results.next_unlock_one` | Next unlock: {name} at level {level} · 1 level away | Sıradaki açılış: {name}, seviye {level} · 1 seviye kaldı |
| `results.killer.standard` | STANDARD ZOMBIE | STANDART ZOMBİ |
| `results.killer.special` | SPECIAL ZOMBIE | ÖZEL ZOMBİ |
| `results.killer.elite` | ELITE ZOMBIE | ELİT ZOMBİ |
| `results.killer.ultra` | ULTRA ZOMBIE | ULTRA ZOMBİ |

S-09 (R1-D, 25.09):
- The Results v3 blocks (direction E26): four hero tiles (Round · Kills · Gold · Time), RUN REPORT (COMBAT / RUN), and REWARDS (EXPERIENCE, PATH line `path.results.delta`, unlocks and achievements).
- `results.new_best` is the sakura ribbon beside the title, the last beat of the choreography.
- `results.empty` replaces the two old empty lines when the run unlocked nothing.
- `results.killer.*` (12b): the killing blow in the Results H1 by the attacker's tier (EnemyAttackState's "Zombie.<Tier>.<Style>" ids), never the raw id.
- `results.next_unlock` (12b, AD R1-D SHOULD-1): the column's forward look under the XP caption - the lowest weapon or branch unlock above the level the run ended on; absent once every unlock is owned.

## 12 · Language setting and the UI through the pack (S-09, WP-068 TR)

| key | en | tr |
| --- | --- | --- |
| `access.caption` | These change how the game is presented, never how it plays. | Bunlar oyunun nasıl sunulduğunu değiştirir, nasıl oynandığını asla. |
| `access.note.flashes` | Damage and final-seconds flashes at 40%. Blood and the world are not changed. Camera shake has its own slider on the pause screen. | Hasar ve son saniye parlamaları %40'ta. Kan ve dünya değişmez. Kamera sarsıntısının duraklatma ekranında kendi kaydırıcısı var. |
| `access.note.palette` | Colour-blind safe moves health, earned and unlocked states from green to blue. Danger stays red; every state also has a shape and a word. | Renk körü dostu palet can, kazanılmış ve açık durumları yeşilden maviye taşır. Tehlike kırmızı kalır; her durumun ayrıca bir şekli ve bir kelimesi vardır. |
| `access.note.reduced_motion` | Pulses hold still, screen fades are instant and the menu petals stop drifting. | Nabızlar durur, ekran geçişleri anlık olur ve menü yaprakları süzülmeyi bırakır. |
| `access.note.text_scale` | Body, label and caption text. It applies to the next screen you open. | Gövde, etiket ve açıklama metni. Açtığın sonraki ekranda uygulanır. |
| `access.palette.colour_blind` | COLOUR-BLIND SAFE | RENK KÖRÜ DOSTU |
| `access.palette.default` | DEFAULT | VARSAYILAN |
| `access.reduced_motion.off` | REDUCED MOTION · OFF | AZALTILMIŞ HAREKET · KAPALI |
| `access.reduced_motion.on` | REDUCED MOTION · ON | AZALTILMIŞ HAREKET · AÇIK |
| `access.row.palette` | PALETTE | PALET |
| `access.row.reduce_flashes` | REDUCE FLASHES | PARLAMALARI AZALT |
| `access.row.reduced_motion` | REDUCED MOTION | AZALTILMIŞ HAREKET |
| `access.row.text_scale` | TEXT SCALE | METİN ÖLÇEĞİ |
| `access.sample` | SAMPLE | ÖRNEK |
| `access.sample.caption` | Caption text at {value}. | Açıklama metni {value}. |
| `access.sample.edge` | Damage edge and final-seconds bands at their peak. | Hasar kenarı ve son saniye bantları en yüksek hâlinde. |
| `access.section.colour` | COLOUR | RENK |
| `access.section.motion` | MOTION | HAREKET |
| `access.section.text` | TEXT | METİN |
| `access.swatch.danger` | DANGER | TEHLİKE |
| `access.swatch.warning` | WARNING | UYARI |
| `ach.auto_note` | Achievements unlock by themselves at the end of a run. There is nothing to claim. | Başarımlar koşu sonunda kendiliğinden açılır. Alınacak bir şey yok. |
| `ach.earned` | EARNED | KAZANILDI |
| `ach.empty.body` | This build does not define any achievements. When it does, they will unlock automatically at the end of a run and appear here. | Bu sürüm hiç başarım tanımlamıyor. Tanımladığında, koşu sonunda otomatik açılıp burada görünecekler. |
| `ach.empty.title` | No achievements yet | Henüz başarım yok |
| `ach.in_progress` | IN PROGRESS | SÜRÜYOR |
| `ach.next` | NEXT | SONRAKİ |
| `ach.none_defined` | No achievements are defined in this build yet. | Bu sürümde henüz tanımlı başarım yok. |
| `ach.page` | Page {value} of {total} | Sayfa {value} / {total} |
| `ach.previous` | PREVIOUS | ÖNCEKİ |
| `ach.progress` | {value} of {total} earned | {total} içinden {value} kazanıldı |
| `ach.progress_new` |   ·  {value} new |   ·  {value} yeni |
| `common.main_menu` | MAIN MENU | ANA MENÜ |
| `common.off` | OFF | KAPALI |
| `common.on` | ON | AÇIK |
| `controls.aim_assist` | AIM ASSIST | NİŞAN YARDIMI |
| `controls.deadzone` | STICK DEAD ZONE | ÇUBUK ÖLÜ BÖLGESİ |
| `controls.file` | Overrides: {value} | Değişiklikler: {value} |
| `controls.gamepad` | GAMEPAD | OYUN KUMANDASI |
| `controls.intro` | Select a slot and press the key or button you want. Escape cancels. Overrides are saved on this machine. | Bir yuva seç ve istediğin tuşa ya da düğmeye bas. Escape iptal eder. Değişiklikler bu makinede kaydedilir. |
| `controls.keyboard` | KEYBOARD | KLAVYE |
| `controls.listening` | PRESS A KEY | BİR TUŞA BAS |
| `controls.no_service` | Input service not running. | Girdi servisi çalışmıyor. |
| `controls.reset` | HOLD TO RESET ALL BINDINGS | TÜM ATAMALARI SIFIRLAMAK İÇİN BASILI TUT |
| `controls.rumble` | RUMBLE | TİTREŞİM |
| `hud.ammo.empty` | EMPTY | BOŞ |
| `hud.banner.classic_restored` | CLASSIC WEAPON RESTORED | KLASİK SİLAH GERİ DÖNDÜ |
| `hud.banner.last` | CLEAR THE LAST OF THEM | SONUNCULARI TEMİZLE |
| `hud.banner.round` | ROUND {value} | ROUND {value} |
| `hud.banner.round_complete` | ROUND {value} COMPLETE | ROUND {value} TAMAMLANDI |
| `hud.banner.special_field` | SPECIAL WEAPON ON THE FIELD | ÖZEL SİLAH SAHADA |
| `hud.banner.special_named` | {value} ONLINE | {value} DEVREDE |
| `hud.banner.special_online` | SPECIAL WEAPON ONLINE | ÖZEL SİLAH DEVREDE |
| `hud.enemies.breakdown` | {value} left to arrive  ·  {alive} on the map | {value} daha gelecek  ·  haritada {alive} |
| `hud.enemies.pool_empty` | pool empty  ·  {value} still on the map | havuz boş  ·  haritada hâlâ {value} |
| `hud.enemies.waiting` | waiting for the round to start | roundun başlaması bekleniyor |
| `hud.health` | HEALTH | CAN |
| `hud.health.low` | LOW | DÜŞÜK |
| `hud.health.recovering` | RECOVERING | TOPARLANIYOR |
| `hud.health.recovery_in` | recovery in {value}s | {value} sn içinde toparlanma |
| `hud.special.active` | ACTIVE | AKTİF |
| `hud.special.ending` | ENDING | BİTİYOR |
| `hud.special.ending_banner` | SPECIAL WEAPON ENDING | ÖZEL SİLAH BİTİYOR |
| `hud.special.generic` | Special Weapon | Özel Silah |
| `hud.special.hits` | HITS {value} | İSABET {value} |
| `hud.special.on_field` | ON THE FIELD | SAHADA |
| `hud.special.reloading` | RELOADING | DOLDURULUYOR |
| `hud.special.shots` | SHOTS {value} | ATIŞ {value} |
| `hud.special.suspended` | SUSPENDED | ASKIDA |
| `hud.threat.behind` | BEHIND YOU | ARKANDA |
| `hud.threat.elite` | ELITE NEARBY | ELİT YAKINDA |
| `hud.threat.surrounded` | SURROUNDED | KUŞATILDIN |
| `hud.threat.ultra` | ULTRA NEARBY | ULTRA YAKINDA |
| `loading.dest.main_menu` | the main menu | ana menüyü |
| `loading.dest.next` | the next screen | sonraki ekranı |
| `loading.dest.results` | the results screen | sonuç ekranını |
| `loading.dest.run` | the run | koşuyu |
| `loading.dest.selection` | the selection screen | seçim ekranını |
| `loading.entering` | ENTERING | GİRİLİYOR |
| `loading.entering.full` | ENTERING · 100% | GİRİLİYOR · %100 |
| `loading.error.body` | Deaths Demolition could not open {value}. {reason} Your profile was not changed. | Deaths Demolition {value} açamadı. {reason} Profilin değişmedi. |
| `loading.error.missing` | The scene is missing from this build. | Sahne bu sürümde yok. |
| `loading.error.retry` | TRY AGAIN | TEKRAR DENE |
| `loading.error.stalled` | Loading stopped making progress. | Yükleme ilerlemeyi bıraktı. |
| `loading.error.title` | THE NEXT SCREEN DID NOT LOAD | SONRAKİ EKRAN YÜKLENMEDİ |
| `loading.error.to_menu` | RETURN TO MAIN MENU | ANA MENÜYE DÖN |
| `loading.percent` | LOADING · {0}% | YÜKLENİYOR · %{0} |
| `loading.reduced_motion` | Loading. The screen changes on its own; no input is needed. | Yükleniyor. Ekran kendiliğinden değişir; bir şeye basman gerekmez. |
| `loading.stalled` | Loading is taking longer than usual. | Yükleme her zamankinden uzun sürüyor. |
| `loading.tip.ctrl.01` | Holding fire keeps firing. Every classic weapon is automatic. | Ateşi basılı tutmak ateşe devam ettirir. Her klasik silah otomatiktir. |
| `loading.tip.ctrl.02` | Holding fire on an empty magazine starts the reload. | Boş şarjörde ateşi basılı tutmak dolumu başlatır. |
| `loading.tip.ctrl.03` | Keys and buttons can be remapped under CONTROLS. Overrides stay on this machine. | Tuşlar ve düğmeler KONTROLLER altında değiştirilebilir. Değişiklikler bu makinede kalır. |
| `loading.tip.ctrl.04` | There is no dash and no dodge. Footwork is the only evasion. | Atılma da kaçınma da yok. Tek kaçış ayak oyunudur. |
| `loading.tip.econ.01` | Each round ends with three upgrade cards. Up to two of them can be bought. | Her round üç geliştirme kartıyla biter. En fazla ikisi alınabilir. |
| `loading.tip.econ.02` | One paid reroll per round, only before a purchase. It swaps one or two cards. | Round başına bir ücretli reroll, yalnızca alımdan önce. Bir ya da iki kartı değiştirir. |
| `loading.tip.econ.03` | Unspent gold carries into the next round. | Harcanmayan altın sonraki rounda geçer. |
| `loading.tip.econ.04` | Gold still on the ground when a round is cleared is collected on its own. | Round temizlendiğinde yerde kalan altın kendiliğinden toplanır. |
| `loading.tip.econ.05` | Armour an enemy spawned with adds gold. Breaking the plate never lowers the reward. | Düşmanın doğduğu zırh altın ekler. Plakayı kırmak ödülü asla düşürmez. |
| `loading.tip.surv.01` | Health regenerates after a few seconds without damage. A new hit resets the wait. | Birkaç saniye hasar almazsan can yenilenir. Yeni bir darbe beklemeyi sıfırlar. |
| `loading.tip.surv.02` | There is no invulnerability window. Two hits in one moment both count in full. | Dokunulmazlık aralığı yok. Aynı anda gelen iki darbe de tam sayılır. |
| `loading.tip.surv.03` | One life. When health reaches zero, the run ends and the results screen follows. | Tek can. Can sıfıra inince koşu biter ve sonuç ekranı gelir. |
| `loading.tip.surv.04` | During NEXT ROUND IN the world keeps moving. The gap is for repositioning. | SONRAKİ ROUND sırasında dünya hareket etmeyi sürdürür. Ara, yer değiştirmek içindir. |
| `loading.tip.sw.01` | A Special Weapon has no ammo and no reload. Its timer starts on pickup. | Özel silahın cephanesi ve dolumu yoktur. Süresi alındığı anda başlar. |
| `loading.tip.sw.02` | Round-end upgrades change the classic weapon only. A Special Weapon is untouched. | Round sonu geliştirmeleri yalnızca klasik silahı değiştirir. Özel silaha dokunmaz. |
| `loading.tip.sw.03` | The Kagura Bell rings outward in every direction. It does not read the aim. | Kagura Çanı her yöne dışa doğru çalar. Nişana bakmaz. |
| `loading.tip.sw.04` | Ofuda seals do little damage. With enough of them, a shot into one collapses the net. | Ofuda mühürleri az hasar verir. Yeterince varsa birine atılan atış ağı çökertir. |
| `loading.tip.sw.05` | The Tide Jewel opens a well that drags the crowd inward, then implodes on the pile. | Gelgit Mücevheri kalabalığı içeri çeken bir kuyu açar, sonra yığının üstüne çöker. |
| `loading.tip.sw.06` | A drop's beacon blinks faster as its ground timer runs out. | Yerdeki silahın işareti, süresi azaldıkça daha hızlı yanıp söner. |
| `loading.tip.tier.01` | A zombie deals damage only when its swing lands. Contact alone does nothing. | Zombi yalnızca savuruşu isabet edince hasar verir. Temas tek başına bir şey yapmaz. |
| `loading.tip.tier.02` | Special, Elite and Ultra versions each arrive on a set round, then join the pool. | Özel, Elit ve Ultra sürümler belli bir roundda gelir, sonra havuza katılır. |
| `loading.tip.tier.03` | An exposed head takes more damage than the body. A helmet covers it until it breaks. | Açıktaki kafa gövdeden fazla hasar alır. Miğfer kırılana dek onu örter. |
| `loading.tip.tier.04` | Elites and Ultras resist the Bell's push. The Tide slows an Ultra instead of pulling it. | Elitler ve Ultralar Çan'ın itişine direnir. Gelgit bir Ultra'yı çekmez, yavaşlatır. |
| `loading.tip.tier.05` | BEHIND YOU means an attack is winding up outside your view. | ARKANDA, görüşünün dışında bir saldırının hazırlandığını gösterir. |
| `loading.word` | LOADING | YÜKLENİYOR |
| `menu.build_prefix` | Prototype build ·  | Prototip sürüm ·  |
| `menu.eyebrow` | PLAYABLE CONCEPT · PROTOTYPE | OYNANABİLİR KONSEPT · PROTOTİP |
| `menu.play` | PLAY | OYNA |
| `menu.play_hint` | Choose a skin and a classic weapon, then fight for the highest round you can reach. | Bir kostüm ve bir klasik silah seç, sonra ulaşabildiğin en yüksek round için savaş. |
| `menu.progress` | PROGRESS | İLERLEME |
| `menu.quit` | QUIT | ÇIKIŞ |
| `menu.subtitle` | An old palace, a garden, and a crowd that never stops arriving. | Eski bir saray, bir bahçe ve gelmeyi hiç bırakmayan bir kalabalık. |
| `menu.tab.accessibility` | ACCESSIBILITY | ERİŞİLEBİLİRLİK |
| `menu.tab.achievements` | ACHIEVEMENTS | BAŞARIMLAR |
| `menu.tab.controls` | CONTROLS | KONTROLLER |
| `menu.tab.profile` | PROFILE | PROFİL |
| `menu.tab.settings` | SETTINGS | AYARLAR |
| `menu.tab.statistics` | STATISTICS | İSTATİSTİKLER |
| `pause.caption` | The whole simulation is stopped: enemies, projectiles, timers and the round clock. | Bütün simülasyon durdu: düşmanlar, mermiler, sayaçlar ve round saati. |
| `pause.confirm.body` | Leaving now ends the run. The current round, all unspent gold and every weapon upgrade you bought are lost. Profile level, statistics and achievements you have already earned are kept. | Şimdi çıkarsan koşu biter. Mevcut round, harcanmamış tüm altın ve aldığın her silah geliştirmesi kaybolur. Zaten kazandığın profil seviyesi, istatistikler ve başarımlar korunur. |
| `pause.confirm.end` | END RUN | KOŞUYU BİTİR |
| `pause.confirm.keep` | KEEP PLAYING | OYNAMAYA DEVAM |
| `pause.confirm.title` | END THIS RUN? | BU KOŞU BİTSİN Mİ? |
| `pause.resume` | RESUME | DEVAM ET |
| `pause.shake` | CAMERA SHAKE INTENSITY | KAMERA SARSINTISI ŞİDDETİ |
| `pause.shake.caption` | 0 disables shake completely; 1 is the default. Shake never moves your aim at any setting. | 0 sarsıntıyı tamamen kapatır; 1 varsayılandır. Sarsıntı hiçbir ayarda nişanını oynatmaz. |
| `pause.title` | PAUSED | DURAKLATILDI |
| `profile.ach_summary` | {value} of {total} achievements earned | {total} başarımdan {value} kazanıldı |
| `profile.badges_label` | PROFILE BADGE | PROFİL ROZETİ |
| `profile.branch_line` | {value} branch | {value} dalı |
| `profile.level` | LEVEL {value} | SEVİYE {value} |
| `profile.level_label` | PROFILE LEVEL | PROFİL SEVİYESİ |
| `profile.none` | None | Yok |
| `profile.notice.recovered` | The previous profile file could not be read, so this profile started fresh. The unreadable file was kept next to the save. | Önceki profil dosyası okunamadı, bu yüzden bu profil sıfırdan başladı. Okunamayan dosya kaydın yanında saklandı. |
| `profile.pick_note` | Locked titles and badges open through achievements and unlocks. | Kilitli unvanlar ve rozetler başarımlar ve açılışlarla açılır. |
| `profile.progress` | {value} XP towards level {next} | {value} XP · {next}. seviyeye |
| `profile.rule` | Persistent progress unlocks content only. It never grants starting damage, health or gold. | Kalıcı ilerleme yalnızca içerik açar. Başlangıç hasarı, can ya da altın asla vermez. |
| `profile.titles_label` | PROFILE TITLE | PROFİL UNVANI |
| `profile.unlocked` | UNLOCKED | AÇIK |
| `profile.unlocked_content` | UNLOCKED CONTENT | AÇILAN İÇERİK |
| `results.achievements_empty` | No new achievements this run. | Bu koşuda yeni başarım yok. |
| `results.armour_broken` | Armour Broken | Kırılan Zırh |
| `results.best_round_chip` | NEW BEST ROUND | YENİ EN İYİ ROUND |
| `results.combat_note.all` | Every kill counts towards kill XP and statistics. | Her öldürme, öldürme XP'sine ve istatistiklere sayılır. |
| `results.combat_note.partial` | Kills in your final round count. Round XP is granted only for completed rounds. | Son roundundaki öldürmeler sayılır. Round XP'si yalnızca tamamlanan roundlar için verilir. |
| `results.empty.body` | This screen is filled in the moment a run ends. Start one from the main menu and it will come back here by itself. | Bu ekran bir koşu bittiği anda dolar. Ana menüden bir koşu başlat, buraya kendiliğinden döner. |
| `results.empty.title` | There is no finished run to report. | Raporlanacak bitmiş bir koşu yok. |
| `results.ending.abandoned` | RUN ABANDONED IN ROUND {value} | KOŞU {value}. ROUNDDA BIRAKILDI |
| `results.ending.defeated` | DEFEATED IN ROUND {value} | {value}. ROUNDDA YENİLDİN |
| `results.ending.ended` | RUN ENDED IN ROUND {value} | KOŞU {value}. ROUNDDA BİTTİ |
| `results.footer.none` | Rewards are granted automatically. There is nothing to claim. | Ödüller otomatik verilir. Alınacak bir şey yok. |
| `results.gold_collected` | Gold Collected | Toplanan Altın |
| `results.gold_spent` | Gold Spent | Harcanan Altın |
| `results.level_line` | LEVEL {from}  →  LEVEL {to} | SEVİYE {from}  →  SEVİYE {to} |
| `results.level_up` | LEVEL UP | SEVİYE ATLADIN |
| `results.level_up_many` | LEVEL UP  x{value} | SEVİYE ATLADIN  x{value} |
| `results.no_run` | NO RUN RECORDED | KAYITLI KOŞU YOK |
| `results.outcome.applied` | Everything above is already saved to your profile. There is nothing to claim. | Yukarıdaki her şey profiline zaten kaydedildi. Alınacak bir şey yok. |
| `results.outcome.duplicate` | This run was already recorded in your profile. Nothing was added twice. | Bu koşu profiline zaten kaydedilmişti. Hiçbir şey iki kez eklenmedi. |
| `results.outcome.invalid` | This run could not be recorded in your profile. Your saved profile is unchanged. | Bu koşu profiline kaydedilemedi. Kayıtlı profilin değişmedi. |
| `results.outcome.missing_identity` | This run could not be recorded in your profile: it carried no run identity. | Bu koşu profiline kaydedilemedi: bir koşu kimliği taşımıyordu. |
| `results.outcome.write_failed` | This run could not be written to your profile file. Your saved profile is unchanged. | Bu koşu profil dosyana yazılamadı. Kayıtlı profilin değişmedi. |
| `results.overflow` | and {value} more, already added to your profile | ve {value} tane daha, profiline zaten eklendi |
| `results.played` | {value} played | {value} oynandı |
| `results.reached_round` | REACHED ROUND | ULAŞILAN ROUND |
| `results.rounds_completed` | {value} rounds completed | {value} round tamamlandı |
| `results.rounds_completed.one` | 1 round completed | 1 round tamamlandı |
| `results.title` | RUN RESULTS | KOŞU SONUÇLARI |
| `results.unlock.badge` | BADGE | ROZET |
| `results.unlock.branch` | BRANCH | DAL |
| `results.unlock.hud` | HUD | HUD |
| `results.unlock.title` | TITLE | UNVAN |
| `results.unlocks_empty` | No new content this run. | Bu koşuda yeni içerik yok. |
| `results.upgrades_bought` | Upgrades Bought | Alınan Geliştirme |
| `results.xp.kills` | Kills | Öldürmeler |
| `results.xp.other` | Gold, Upgrades, Time | Altın, Geliştirme, Süre |
| `results.xp.rounds` | Completed Rounds | Tamamlanan Roundlar |
| `results.xp_note.applied` | Profile progress unlocks content only. It never raises your starting damage, health or gold. | Profil ilerlemesi yalnızca içerik açar. Başlangıç hasarını, canını ya da altınını asla artırmaz. |
| `results.xp_note.not_applied` | This XP was not added to your profile. Profile progress unlocks content only; it never raises your starting damage, health or gold. | Bu XP profiline eklenmedi. Profil ilerlemesi yalnızca içerik açar; başlangıç hasarını, canını ya da altınını asla artırmaz. |
| `selection.back` | BACK | GERİ |
| `selection.eyebrow` | BEFORE THE RUN | KOŞUDAN ÖNCE |
| `selection.selected` | SELECTED | SEÇİLDİ |
| `selection.skin.caption` | All three are available from the start and none of them changes a gameplay value. | Üçü de baştan açıktır ve hiçbiri bir oynanış değerini değiştirmez. |
| `selection.skin.detail` | Cosmetic only | Yalnızca görünüş |
| `selection.skin.title` | CHARACTER SKIN | KARAKTER KOSTÜMÜ |
| `selection.start` | START RUN | KOŞUYU BAŞLAT |
| `selection.subtitle` | The skin is cosmetic. The classic weapon is carried for the whole run and cannot be swapped. | Kostüm yalnızca görünüştür. Klasik silah koşu boyunca taşınır ve değiştirilemez. |
| `selection.title` | PREPARE YOUR RUN | KOŞUNU HAZIRLA |
| `selection.unavailable` | UNAVAILABLE | KULLANILAMAZ |
| `selection.weapon.title` | CLASSIC WEAPON | KLASİK SİLAH |
| `settings.caption` | Device preferences. They are saved on this machine and never touch your profile. | Cihaz tercihleri. Bu makinede kaydedilir ve profiline asla dokunmaz. |
| `settings.display.fullscreen` | FULLSCREEN | TAM EKRAN |
| `settings.display.windowed` | WINDOWED | PENCERE |
| `settings.language.en` | ENGLISH | ENGLISH |
| `settings.language.tr` | TÜRKÇE | TÜRKÇE |
| `settings.note.camera` | Third and first person only; switch the view in Pause. A low field of view causes more motion sickness; Reduced Motion (Accessibility) sets head bob to 0 and cuts the blends. | Yalnızca üçüncü ve birinci şahısta; görünümü Pause'da değiştir. Dar görüş alanı daha çok baş dönmesi yapar; Azaltılmış Hareket (Erişilebilirlik) kafa sallantısını 0'a çeker ve geçişleri keser. |
| `settings.note.hints` | Brief hints during the first three runs, in play and on loading screens. Off leaves the rules. | İlk üç koşuda, oyunda ve yükleme ekranlarında kısa ipuçları. Kapalıyken kurallar kalır. |
| `settings.note.hud_frame` | On a screen wider than 16:9, the HUD's corners move in to a centred 16:9 frame. | 16:9'dan geniş bir ekranda HUD'ın köşeleri ortalanmış bir 16:9 çerçeveye çekilir. |
| `settings.note.telemetry` | Run telemetry writes one local file per run (rounds, kills, weapon use) for tuning. It never contains your profile or name. | Koşu telemetrisi ayar için her koşuda tek bir yerel dosya yazar (roundlar, öldürmeler, silah kullanımı). Profilini ya da adını asla içermez. |
| `settings.note.ui_scale` | UI scale applies to the next screen you open. | Arayüz ölçeği açtığın sonraki ekranda uygulanır. |
| `settings.preview` | PREVIEW | ÖNİZLEME |
| `settings.preview.caption` | UI SCALE {value}. It applies to the next screen you open. | ARAYÜZ ÖLÇEĞİ {value}. Açtığın sonraki ekranda uygulanır. |
| `settings.quality.cinematic` | CINEMATIC | SİNEMATİK |
| `settings.quality.default` | DEFAULT | VARSAYILAN |
| `settings.quality.high` | HIGH | YÜKSEK |
| `settings.quality.performance` | PERFORMANCE | PERFORMANS |
| `settings.row.centre_dot` | CENTRE DOT | ORTA NOKTA |
| `settings.row.display_mode` | DISPLAY MODE | EKRAN MODU |
| `settings.row.hints` | HINTS | İPUÇLARI |
| `settings.row.hud_frame` | HUD IN A 16:9 FRAME | HUD 16:9 ÇERÇEVEDE |
| `settings.row.invert_look` | INVERT LOOK | BAKIŞI TERS ÇEVİR |
| `settings.row.language` | LANGUAGE | DİL |
| `settings.row.look_sensitivity` | LOOK SENSITIVITY | BAKIŞ HASSASİYETİ |
| `settings.row.master_volume` | MASTER VOLUME | ANA SES |
| `settings.row.quality` | QUALITY | KALİTE |
| `settings.row.stick_look_speed` | STICK LOOK SPEED | ÇUBUKLA BAKIŞ HIZI |
| `settings.row.telemetry` | RUN TELEMETRY | KOŞU TELEMETRİSİ |
| `settings.row.ui_scale` | UI SCALE | ARAYÜZ ÖLÇEĞİ |
| `settings.row.ui_sounds` | UI SOUNDS | ARAYÜZ SESLERİ |
| `settings.section.audio` | AUDIO | SES |
| `settings.section.data` | DATA | VERİ |
| `settings.section.display` | DISPLAY | GÖRÜNTÜ |
| `settings.section.graphics` | GRAPHICS | GRAFİK |
| `shop.card.utility` | UTILITY | DESTEK |
| `shop.card.weapon_core` | WEAPON CORE | SİLAH ÇEKİRDEĞİ |
| `shop.continue` | CONTINUE | DEVAM ET |
| `shop.eyebrow` | ROUND COMPLETE | ROUND TAMAMLANDI |
| `shop.fault.body_suffix` | {value} No gold was taken and nothing is for sale this round. Continue when ready. | {value} Altın alınmadı ve bu round satışta bir şey yok. Hazır olunca devam et. |
| `shop.fault.duplicate` | The upgrade table lists the same upgrade twice. | Geliştirme tablosu aynı geliştirmeyi iki kez listeliyor. |
| `shop.fault.exhausted.title` | Nothing left to upgrade | Geliştirilecek bir şey kalmadı |
| `shop.fault.invalid.title` | No valid upgrade offer this round | Bu round geçerli bir geliştirme teklifi yok |
| `shop.fault.maxed` | Every upgrade you can still take is already at its maximum tier. | Alabileceğin her geliştirme zaten azami kademesinde. |
| `shop.fault.no_table` | The upgrade table is not loaded. | Geliştirme tablosu yüklü değil. |
| `shop.fault.other` | The upgrade offer could not be built. | Geliştirme teklifi oluşturulamadı. |
| `shop.fault.pool` | Not enough upgrades are available for your weapon and build right now. | Şu an silahın ve yapın için yeterli geliştirme yok. |
| `shop.gold_amount` | {value} GOLD | {value} ALTIN |
| `shop.gold_available` | GOLD AVAILABLE | MEVCUT ALTIN |
| `shop.intro.one` | Buy one upgrade this round, or reroll the offer once first. Unspent gold carries into the next round. | Bu round bir geliştirme al ya da önce teklifi bir kez reroll et. Harcanmayan altın sonraki rounda geçer. |
| `shop.maxed.body` | Every upgrade branch is already at its maximum tier, so this round has no card to offer. Your gold carries into the next round. | Her geliştirme dalı zaten azami kademesinde, bu yüzden bu round sunulacak kart yok. Altının sonraki rounda geçer. |
| `shop.maxed.title` | Every branch is at maximum tier | Her dal azami kademede |
| `shop.next_round_in` | NEXT ROUND IN | SONRAKİ ROUND |
| `shop.no_table.body` | PrototypeConfig is not loaded, so there is no upgrade table to draw cards from. The run continues normally. | PrototypeConfig yüklü değil, bu yüzden kart çekilecek bir geliştirme tablosu yok. Koşu normal devam eder. |
| `shop.no_table.title` | No upgrade table is loaded | Yüklü geliştirme tablosu yok |
| `shop.note.another` | Choose another upgrade, or continue. | Başka bir geliştirme seç ya da devam et. |
| `shop.note.applied_many` | Upgrades applied. Continue when ready. | Geliştirmeler uygulandı. Hazır olunca devam et. |
| `shop.note.applied_one` | Upgrade applied. Continue when ready. | Geliştirme uygulandı. Hazır olunca devam et. |
| `shop.note.choose` | Choose what to strengthen, then continue. | Neyi güçlendireceğini seç, sonra devam et. |
| `shop.note.choose_one` | Choose one upgrade, or reroll once, then continue. | Bir geliştirme seç ya da bir kez reroll yap, sonra devam et. |
| `shop.note.no_gold` | No gold collected this round, so nothing can be bought yet. A new offer appears next round. | Bu round altın toplanmadı, bu yüzden henüz bir şey alınamaz. Sonraki round yeni bir teklif gelir. |
| `shop.note.nothing` | Nothing to spend it on this round. It carries over. | Bu round harcayacak bir şey yok. Sonraki rounda geçer. |
| `shop.note.unaffordable` | None of these is affordable yet. Unspent gold carries into the next round. | Bunların hiçbirine henüz yetmiyor. Harcanmayan altın sonraki rounda geçer. |
| `shop.preparing.body` | The round-end offer is still being built. It will appear in a moment; continuing now simply starts the next round. | Round sonu teklifi hâlâ hazırlanıyor. Birazdan görünecek; şimdi devam etmek yalnızca sonraki roundu başlatır. |
| `shop.preparing.title` | Preparing the offer | Teklif hazırlanıyor |
| `shop.purchased` | PURCHASED | ALINDI |
| `shop.refuse.apply` | That upgrade could not be applied to your weapon, so no gold was spent. | O geliştirme silahına uygulanamadı, bu yüzden altın harcanmadı. |
| `shop.refuse.changed` | The offer changed. Prices and tiers were refreshed; nothing was spent. | Teklif değişti. Fiyatlar ve kademeler yenilendi; hiçbir şey harcanmadı. |
| `shop.refuse.closed` | This offer is no longer open. Nothing was spent. | Bu teklif artık açık değil. Hiçbir şey harcanmadı. |
| `shop.refuse.gold` | Not enough gold for that upgrade. Nothing was spent. | O geliştirme için altın yetmiyor. Hiçbir şey harcanmadı. |
| `shop.refuse.max` | That upgrade is already at its maximum tier. Nothing was spent. | O geliştirme zaten azami kademesinde. Hiçbir şey harcanmadı. |
| `shop.refuse.no_offer` | There is no valid offer this round. Nothing was spent. | Bu round geçerli bir teklif yok. Hiçbir şey harcanmadı. |
| `shop.refuse.one_per_round` | One upgrade per round. The offer is locked; nothing was spent. | Round başına bir geliştirme. Teklif kilitli; hiçbir şey harcanmadı. |
| `shop.refuse.other` | That purchase could not be completed. Nothing was spent. | O alım tamamlanamadı. Hiçbir şey harcanmadı. |
| `shop.refuse.owned` | You already own that card. Nothing was spent. | O karta zaten sahipsin. Hiçbir şey harcanmadı. |
| `shop.refuse.reroll_failed` | The reroll could not be built. Nothing was spent and your reroll is kept. | Reroll oluşturulamadı. Hiçbir şey harcanmadı ve reroll hakkın duruyor. |
| `shop.refuse.reroll_gold` | Not enough gold to reroll. Nothing was spent. | Reroll için altın yetmiyor. Hiçbir şey harcanmadı. |
| `shop.refuse.reroll_locked` | The offer is locked after a purchase. Nothing was spent. | Teklif bir alımdan sonra kilitlenir. Hiçbir şey harcanmadı. |
| `shop.refuse.reroll_nothing` | There is no other upgrade to swap in. Nothing was spent and your reroll is kept. | Yerine konacak başka geliştirme yok. Hiçbir şey harcanmadı ve reroll hakkın duruyor. |
| `shop.refuse.reroll_other` | That reroll could not be completed. Nothing was spent. | O reroll tamamlanamadı. Hiçbir şey harcanmadı. |
| `shop.refuse.reroll_used` | You already rerolled this round. Nothing was spent. | Bu round zaten reroll yaptın. Hiçbir şey harcanmadı. |
| `shop.reroll` | REROLL | REROLL |
| `shop.reroll.cost` | REROLL · {value} GOLD · ONCE | REROLL · {value} ALTIN · BİR KEZ |
| `shop.reroll.locked` | REROLL · LOCKED | REROLL · KİLİTLİ |
| `shop.reroll.need` | REROLL · NEED {value} GOLD | REROLL · {value} ALTIN GEREK |
| `shop.reroll.nothing` | REROLL · NOTHING TO SWAP | REROLL · DEĞİŞECEK KART YOK |
| `shop.reroll.unavailable` | REROLL · UNAVAILABLE | REROLL · KULLANILAMAZ |
| `shop.reroll.used` | REROLL · USED THIS ROUND | REROLL · BU ROUND KULLANILDI |
| `shop.spent` | SPENT THIS ROUND | BU ROUND HARCANAN |
| `shop.state.expensive` | TOO EXPENSIVE | ÇOK PAHALI |
| `shop.state.leaves` | Leaves you {value} gold. | Geriye {value} altın kalır. |
| `shop.state.locked` | OFFER LOCKED | TEKLİF KİLİTLİ |
| `shop.state.max` | MAX TIER | AZAMİ KADEME |
| `shop.state.max.body` | This branch is already at its maximum tier. | Bu dal zaten azami kademesinde. |
| `shop.state.need_gold` | You need {value} more gold for this one. | Bunun için {value} altın daha gerek. |
| `shop.state.one_per_round` | One upgrade per round. Continue to the next round. | Round başına bir geliştirme. Sonraki rounda devam et. |
| `shop.state.picks_used` | All picks for this round are used. Continue to the next round. | Bu round'un tüm seçimleri kullanıldı. Sonraki rounda devam et. |
| `shop.state.purchased` | Bought this round. It is already applied to your weapon. | Bu round alındı. Silahına zaten uygulandı. |
| `shop.tier.not_owned` | NOT OWNED | SAHİP DEĞİLSİN |
| `shop.title` | SPEND YOUR GOLD | ALTININI HARCA |
| `stats.armour_broken` | Armour Pieces Broken | Kırılan Zırh Parçası |
| `stats.best_run_xp` | Best Single Run XP | Tek Koşuda En İyi XP |
| `stats.empty` | No runs recorded yet. Finish a run and every line below starts filling in. | Henüz kayıtlı koşu yok. Bir koşu bitir, aşağıdaki her satır dolmaya başlar. |
| `stats.highest_round` | Highest Round | En Yüksek Round |
| `stats.lifetime_xp` | Lifetime XP | Toplam XP |
| `stats.note` | Gold and time played are recorded per run only. Neither contributes profile XP. | Altın ve oynama süresi yalnızca koşu başına kaydedilir. İkisi de profil XP'si vermez. |
| `stats.profile_level` | Profile Level | Profil Seviyesi |
| `stats.rounds_completed` | Rounds Completed | Tamamlanan Round |
| `stats.runs_played` | Runs Played | Oynanan Koşu |
| `stats.tier.elite` | Elite | Elit |
| `stats.tier.special` | Special | Özel |
| `stats.tier.standard` | Standard | Standart |
| `stats.tier.ultra` | Ultra | Ultra |
| `stats.tier_caption` | Tiers recorded at zero have not appeared in a run yet. | Sıfır görünen kademeler henüz bir koşuda çıkmadı. |
| `stats.total_kills` | Total Kills | Toplam Öldürme |
| `stats.unattributed` | Recorded Before Tier Tracking | Kademe Takibinden Önce Kaydedilen |
| `tree.action.buy` | BUY | AL |

S-09 (WP-068 TR, 26.09):
- Settings › DISPLAY › LANGUAGE (`settings.row.language`) switches the column every screen reads (`UiSettings.Language`, saved in ui-settings.json; an older file reads English). The Settings pane and the menu's own words redraw at once; every other screen reads the language when it is next built.
- `settings.language.en` / `settings.language.tr`: each language names itself, so both columns carry the same word.
- `settings.quality.*` names the three quality tiers in Turkish; English keeps the QualitySettings level name.
- `loading.tip.*` are the U9 loading tips (UiLoadingTips keys; the English column is the code's text, ≤ 90 characters in both columns).
- Keys are namespaced by screen: `menu.*`, `settings.*`, `access.*`, `controls.*`, `profile.*`, `stats.*`, `ach.*`, `pause.*`, `selection.*`, `results.*`, `loading.*`, `hud.*`, `shop.*`, `common.*`. `{value}`, `{total}`, `{from}`, `{to}`, `{next}`, `{alive}`, `{reason}` and TMP's `{0}` are filled in code and stay verbatim.
- Upper-casing on screen follows the language (`UiStrings.Upper`: Turkish rules under `tr`, so i → İ).

<!-- TABLES:END -->

## S-09 visible Turkish slice — C-00 28.09

UI only: live shop counts and tier details; remap action/composite labels; assist levels; HUD known killer names reuse results.killer.*. Physical binding keycaps, special names, units and dev strings keep their source. Tree names/descriptions use existing StringKey stems and live args. Turkish Display uses the existing Text SDF for the complete row; English keeps Display.

| Key | English | Turkish |
|---|---|---|
| shop.count.one | one | bir |
| shop.count.two | two | iki |
| shop.count.three | three | üç |
| shop.intro.many | Buy up to {count} upgrades this round, or reroll the offer once before the first. Unspent gold carries into the next round. | Bu round en çok {count} geliştirme al ya da ilkinden önce teklifi bir kez reroll et. Harcanmayan altın sonraki rounda geçer. |
| shop.note.choose_many | Choose up to {count} upgrades, or reroll once first, then continue. | En çok {count} geliştirme seç ya da önce bir kez reroll et, sonra devam et. |
| shop.refuse.limit | All {count} picks for this round are used. The offer is locked; nothing was spent. | Bu roundun {count} seçimi de kullanıldı. Teklif kilitli; hiçbir şey harcanmadı. |
| shop.footer.none | Nothing bought yet. All {gold} gold carries into the next round. | Henüz bir şey alınmadı. {gold} altının tümü sonraki rounda geçer. |
| shop.footer.remaining | Applied immediately. {used} of {max} picks used, {left} left. {gold} gold carries into the next round. | Hemen uygulandı. {max} seçimden {used} kullanıldı, {left} kaldı. {gold} altın sonraki rounda geçer. |
| shop.footer.locked_many | Applied immediately. All picks used, so the offer is now locked. {gold} gold carries into the next round. | Hemen uygulandı. Tüm seçimler kullanıldı; teklif artık kilitli. {gold} altın sonraki rounda geçer. |
| shop.footer.locked_one | Applied immediately. One upgrade per round, so the offer is now locked. {gold} gold carries into the next round. | Hemen uygulandı. Round başına tek geliştirme; teklif artık kilitli. {gold} altın sonraki rounda geçer. |
| controls.action_part | {action} {part} | {part} {action} |
| controls.assist.off | OFF | KAPALI |
| controls.assist.light | LIGHT | HAFİF |
| controls.assist.full | FULL | TAM |
| hud.death.killed_by | {down}  ·  killed by {source} | {down} · kaynak: {source} |
| tree.link.unbound_1 | Special Link I | Özel Bağ I |
| tree.link.unbound_2 | Special Link II | Özel Bağ II |
| shop.detail.Piercing | Bullets pass through {count} more enemy per shot ({total} total), {damage}% damage kept per pass. | Atışta {count} ek düşman delinir (toplam {total}); geçişte %{damage} hasar kalır. |
| shop.detail.Ricochet | {chance}% of hits bounce to another enemy within {radius} m, up to {count} time(s), at {damage}% damage. | İsabetlerin %{chance} kadarı {radius} m içindeki başka bir düşmana %{damage} hasarla en çok {count} kez seker. |
| shop.detail.Explosion | {chance}% of hits detonate for {radius} m at {scale}x the hit's damage. | İsabetlerin %{chance} kadarı, {scale} katı hasarla {radius} m yarıçapta patlar. |
| shop.detail.Chain | Damage jumps to {count} more enem(y/ies) within {radius} m at {damage}% damage, falling {falloff}% per jump. | Hasar {radius} m içindeki {count} ek düşmana %{damage} hasarla sıçrar; her sıçramada %{falloff} azalır. |
| shop.detail.FireRate | +{value}% fire rate ({total}x total, capped at {max}x). | Atış hızı +%{value} (toplam {total}x, üst sınır {max}x). |
| shop.detail.ReloadSpeed | -{value}% reload time ({total}x total, floor {min}x). | Dolum süresi -%{value} (toplam {total}x, alt sınır {min}x). |
| shop.detail.MovementSpeed | +{value}% movement speed ({total}x total, capped at {max}x). | Hareket hızı +%{value} (toplam {total}x, üst sınır {max}x). |
| shop.detail.MaximumHealth | +{value}% maximum health ({total}x total{health}). Current health keeps its share. | Azami can +%{value} (toplam {total}x{health}). Mevcut can oranı korunur. |
| shop.detail.RegenerationDelay | -{value}% wait before health regenerates ({total}x total{seconds}). | Can yenilenmeden önceki bekleme -%{value} (toplam {total}x{seconds}). |
| shop.detail.RegenerationRate | +{value}% health regeneration speed ({total}x total{rate}). | Can yenilenme hızı +%{value} (toplam {total}x{rate}). |
| controls.action.Move | Move | Hareket |
| controls.action.Aim | Aim | Nişan |
| controls.action.AimStick | Aim (Stick) | Çubukla Nişan |
| controls.action.Fire | Fire | Ateş |
| controls.action.Reload | Reload | Doldur |
| controls.action.Pause | Pause | Duraklat |
| controls.action.Special | Special | Özel Silah |
| controls.action.Interact | Interact | Etkileşim |
| controls.action.Focus | Focus | Odaklan |
| controls.part.up | Up | Yukarı |
| controls.part.down | Down | Aşağı |
| controls.part.left | Left | Sol |
| controls.part.right | Right | Sağ |
| tree.link.unbound_desc | Links the Special you pick up this run to your classic weapon. | Bu koşuda aldığın Özel silahı klasik silahına bağlar. |

## S-09 · Existing Profile and Results copy continuation

Existing 882 keys and values are unchanged. 50 new bilingual UI-only keys; catalog identity names and gameplay data remain unchanged. Counts are read from the existing catalog thresholds.

R1-E E1 (S-09, C-00 30.09): 21 tr lines below revised for grammar and pack terminology (doruk, kilit taşı, yak, şerit, Özel silah, yen); every change was proposed by one reviewer and checked by a second, adversarial one (`K/wp-068e/evidence/e1-tr-review.md`, `e1-refute.md`). The category-then-explanation lines keep " - ", because the Results card already joins its type word with "  ·  ". `achievement.namekuji_skater.desc` (§6) now says in both languages that the enemies slide, as the catalog and FeatTracker do.

| Key | en | tr |
|---|---|---|
| `profile.cosmetic.title_format` | Title: {name} | Unvan: {name} |
| `profile.cosmetic.badge_format` | Badge: {name} | Rozet: {name} |
| `results.reward.weapon_selectable` | Classic weapon - selectable before your next run | Klasik silah - sonraki koşudan önce seçilebilir |
| `results.reward.branch_offered` | Upgrade branch - can now be offered at the end of a round | Geliştirme dalı - artık round sonunda sunulabilir |
| `results.reward.weapon_roster` | Classic weapon - opens at profile level {level} (weapon roster) | Klasik silah - {level}. profil seviyesinde açılır (silah listesi) |
| `profile.content.title_first_blood.detail` | Profile title for defeating your first enemy | İlk düşmanını yenince kazanılan profil unvanı |
| `profile.content.title_survivor.detail` | Profile title for reaching round {count} | {count}. rounda ulaşınca kazanılan profil unvanı |
| `profile.content.title_veteran.detail` | Profile title for reaching level {count} | {count}. seviyeye ulaşınca kazanılan profil unvanı |
| `profile.content.title_executioner.detail` | Profile title for defeating an Executioner | Bir Executioner yenince kazanılan profil unvanı |
| `profile.content.title_ironclad.detail` | Profile title for breaking {count} armour pieces | {count} zırh parçası kırınca kazanılan profil unvanı |
| `profile.content.badge_ghost.detail` | Profile badge for {count} flawless rounds | Hasarsız {count} round için profil rozeti |
| `profile.content.indicator_kill_streak.detail` | HUD indicator variant - available once {count} enemies are defeated | HUD gösterge türü - {count} düşman yenince kullanılabilir |
| `profile.content.indicator_round_timer.detail` | HUD indicator variant - available from round {count} | HUD gösterge türü - {count}. rounddan itibaren kullanılabilir |
| `profile.content.indicator_gold_ticker.detail` | HUD indicator variant - available after a {count}-gold run | HUD gösterge türü - {count} altınlık bir koşudan sonra kullanılabilir |
| `achievement.first_blood.desc` | Defeat your first enemy. | İlk düşmanını yen. |
| `achievement.armor_breaker.desc` | Break an armour piece off an enemy. | Bir düşmandan bir zırh parçası kır. |
| `achievement.reach_round_5.desc` | Survive until round {count}. | {count}. rounda kadar hayatta kal. |
| `achievement.defeat_an_ultra.desc` | Bring down an Executioner. | Bir Executioner yen. |
| `achievement.reach_round_10.desc` | Survive until round {count}. | {count}. rounda kadar hayatta kal. |
| `achievement.reach_round_15.desc` | Survive until round {count}. | {count}. rounda kadar hayatta kal. |
| `achievement.reach_round_20.desc` | Survive until round {count}. | {count}. rounda kadar hayatta kal. |
| `achievement.rounds_completed_50.desc` | Complete {count} rounds in total. | Toplam {count} round tamamla. |
| `achievement.kills_50.desc` | Defeat {count} enemies in total. | Toplam {count} düşman yen. |
| `achievement.kills_250.desc` | Defeat {count} enemies in total. | Toplam {count} düşman yen. |
| `achievement.kills_1000.desc` | Defeat {count} enemies in total. | Toplam {count} düşman yen. |
| `achievement.special_hunter.desc` | Defeat {count} Runners in total. | Toplam {count} Runner yen. |
| `achievement.elite_hunter.desc` | Defeat {count} Guards in total. | Toplam {count} Guard yen. |
| `achievement.ultra_hunter.desc` | Defeat {count} Executioners in total. | Toplam {count} Executioner yen. |
| `achievement.armor_breaker_100.desc` | Break {count} armour pieces in total. | Toplam {count} zırh parçası kır. |
| `achievement.special_pickup_first.desc` | Pick up a special weapon. | Bir Özel silah al. |
| `achievement.special_pickup_10.desc` | Pick up {count} special weapons in total. | Toplam {count} Özel silah al. |
| `achievement.run_gold_150.desc` | Pick up {count} gold in a single run. | Tek koşuda {count} altın topla. |
| `achievement.run_gold_400.desc` | Pick up {count} gold in a single run. | Tek koşuda {count} altın topla. |
| `achievement.run_upgrades_5.desc` | Buy {count} upgrade cards in a single run. | Tek koşuda {count} geliştirme kartı al. |
| `achievement.flawless_round.desc` | Complete a round without taking a hit. | Hasar almadan bir round tamamla. |
| `achievement.flawless_rounds_10.desc` | Complete {count} rounds without taking a hit, in total. | Hasar almadan toplam {count} round tamamla. |
| `achievement.runs_played_10.desc` | Finish {count} runs. | {count} koşu bitir. |
| `achievement.runs_played_50.desc` | Finish {count} runs. | {count} koşu bitir. |
| `achievement.level_5.desc` | Reach profile level {count}. | {count}. profil seviyesine ulaş. |
| `achievement.level_10.desc` | Reach profile level {count}. | {count}. profil seviyesine ulaş. |
| `achievement.run_kills_40.desc` | Defeat {count} enemies in a single run. | Tek koşuda {count} düşman yen. |
| `achievement.first_capstone.desc` | Buy a capstone in the run tree. | Koşu ağacında bir doruk al. |
| `achievement.three_keystones_one_run.desc` | Own three keystones in one run. | Tek koşuda üç kilit taşına sahip ol. |
| `achievement.hearth_10_one_run.desc` | Stoke the Hearth {count} times in one run. | Tek koşuda Ocağı {count} kez yak. |
| `achievement.hearth_20.desc` | Stoke the Hearth {count} times in one run. | Tek koşuda Ocağı {count} kez yak. |
| `achievement.full_lane.desc` | End a run owning a lane's capstone with one of its tiers at maximum. | Bir şeridin doruğuna ve azami kademedeki bir dalına sahipken koşu bitir. |
| `achievement.capstones_5_weapons.desc` | End runs owning the capstone with five different weapons. | Beş farklı silahın her biriyle doruğuna sahipken bir koşu bitir. |
| `achievement.both_special_links.desc` | Own both links of a Special in one run. | Tek koşuda bir Özel silahın iki bağına da sahip ol. |
| `achievement.mastery_5_any.desc` | Reach mastery {count} with any weapon. | Bir silahta {count}. ustalık seviyesine ulaş. |
| `results.reward.achievement.namekuji_skater.desc` | Defeat {count} enemies while they slide on gel in one run. | Bir koşuda jelde kayan {count} düşman yen. |

## S-09 · Slice 9 remainder (E3, C-00 30.09)

`controls.reset` changed in place: RESET ALL is now a 500 ms hold (direction E7), so the label says so, following `path.buy.hold`. The three new keys are the Controls pane's dead-zone sentence, the override mark's legend (the kit never uses a shape alone) and the binding-conflict notice (direction E7: conflict → Warning notice). The `profile.locked.*` keys are a locked title / badge row's condition (direction E3: LOCKED = lock + condition; T1), and `profile.content.title_first_blood.detail` replaces a stale developer line ("…once the profile pane supports titles") with the condition, as its siblings read. M-69 (Codex fix list, rows updated in place in the visible-Turkish section): `controls.action_part` TR reads part first (Yukarı Hareket), `controls.action.AimStick` EN is "Aim (Stick)", and `shop.detail.Explosion` TR loses the doubled "{scale}x katıyla".

| Key | en | tr |
|---|---|---|
| `controls.deadzone.note` | How far a stick moves before it counts. Raise it if your aim drifts on its own. | Çubuk bu kadar itilmeden hareket sayılmaz. Nişanın kendi kendine kayıyorsa artır. |
| `controls.override_legend` | A marked slot is changed from its default. | İşaretli yuva varsayılandan değiştirildi. |
| `controls.conflict` | {key} is bound to more than one action: {actions}. Rebind one of them. | {key} birden fazla eyleme atanmış: {actions}. Birini yeniden ata. |
| `profile.locked.round` | LOCKED · ROUND {value} | KİLİTLİ · {value}. ROUND |
| `profile.locked.level` | LOCKED · LEVEL {value} | KİLİTLİ · SEVİYE {value} |
| `profile.locked.achievement` | LOCKED · {name} | KİLİTLİ · {name} |
