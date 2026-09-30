# WP-076 · AI v3 — Yön belgesi (Faz A, game-designer konseyi)

Sahip: S-03 · Tarih: 2026-09-24 · Girdi: `M4-R1-REVIZYON-TURU.md` §0 (N1/N3/N6/N11), §4 · Uygulama: Faz B (Opus 5.5).
Her sayı **PROTOTYPE-ONLY** ve `[PrototypeOnly]` tablo değeridir; sabit kod yok (O5). Kod kimlikleri İngilizce.

Yayınlanmış ve bağlanmış sözleşmeler (§8.1): S-01 WP-070 (f23f38e7), S-06 WP-071 (1980e0f), S-05 WP-074 (abc0f70) ve QUALITY-TIERS-V2 (3812dc2). Varsayımla tasarlanan (§8.2): S-02 WP-069/ADR-069-1 ve S-05 greybox veri dosyaları. DEC numarası atanmaz; gerekenler "DEC (C-00'dan istenecek)" olarak işaretlidir. AI tarafı veri güdümlüdür: sözleşme gelince tablo/enum bağlanır, tasarım değişmez.

Değişmeyen kararlar: DEC-023 (40 beden tavanı, cesetler sayılır) · DEC-029 (hasar yalnız görünür saldırının Active penceresinde; pencere klip olayından, zamanlayıcı yedek) · DEC-032 (ağır tier düz gelir, canlı duvar) · DEC-081 (kalkış yalnız kamera dışı doğuşta) · DEC-021 tier tablosu (`EnemyConfigSection`) · ADR-040-1 tek beat yüzeyi.

Bağlayıcı kullanıcı kararları (C-00, 24.09): **DEC-092** perf hedefi 60 fps (Yüksek kademe ort ≤ 16,6 ms / p99 ≤ 20 ms, 1080p, referans Mac) + Performans kademesi → AI tarafı ms bütçesi §7. **DEC-093** build tavanı 5 GB (AI tarafını etkilemez; ragdoll/klip verisi S-06'da). **DEC-094** iki Special özgün ad + kendi modelimiz; mekanik/his/sanat yönü referans silahlara çok yakın. Bu belgede çalışma adları **"zincir yıldırım Special'ı"** ve **"jel Special'ı"**; referans oyunun adları hiçbir kimlikte (enum, olay, dosya, satır adı) geçmez. Kod kimlikleri: `ChainLightningSpecial` / `GelSpecial` (S-02'nin özgün adı gelince `SpecialWeaponId` değerleri onunla değişir; S-03 yalnız `SpecialWeaponId` üzerinden bilir).

---

## 1 · Beş saldırı davranışı (N11) — S-06 WP-071 A4 klipleriyle hizalı

### 1.1 Roster — `ZombieAttackStyle` artımlı genişler, adlar S-06'nın

S-06 sözleşmesi (1980e0f, A4): `attack_swipe_R` / `attack_swipe_L` (ayna) · `attack_lunge` · `attack_bite_grab` · `attack_sweep` (+ `attack_sweep_ultra`) · `attack_overhead_slam`. Karar: **swipe_R/L tek stildir** (`Swipe`, `SwipeMirror` bayrağı ile; aynı el iki kez üst üste yasak, el seçimi §1.3) — beş stil beş davranıştır. Enum: `Swipe=0, Lunge=1, Sweep=2, Overhead=3, BiteGrab=4`. Tier→stil sabit eşlemesi (`EnemyAttackState.ApplyConfig` switch) kalkar; `AttackStyleProfileSet` (`DD_AttackStyles`, kod varsayılanlı) + saf `AttackSelector`.

| # | Stil → klip | Rol | Erişim (S-06) / commit menzili | Vuruş yarıçapı (×BodyRadius) | Yay (±°) | Klip pencere (f, S-06) | Hasar ×tier (imza = 1,00) | Cooldown ×tier | Kök adım |
|---|---|---|---|---|---|---|---|---|---|
| A1 | **Swipe** → `swipe_R` / `swipe_L` | taban, en sık | 1,1 m / 1,0 × AttackRange | 1,7 | 35 | 15–19 / 36 f, `attack_recover@30` | 1,00 | 1,00 | +0,35 m (`attack_step` f13) |
| A2 | **Lunge** → `lunge` | mesafe kapatır; ıskalarsa `stumble` | 1,9 m / 1,8–2,6 m bandı | 1,9 | 25 | 12–15 / 32 f, `attack_recover@26` | 0,90 | 1,00 (Special taban ≥ 1,9 s) | +1,4 m (f9–16; Standard ×0,8) |
| A3 | **Sweep** → `sweep` / `sweep_ultra` | geniş yay, yandan | 1,3 m yay 150° / 1,0 × AttackRange | 2,1 | 75 | 22–27 / 48 f (Ultra 26–32 / 56 f), `attack_recover@40/46` | 1,80 | 1,20 | +0,25 m + 30° yaw (`root_yaw`) |
| A4 | **Overhead** → `overhead_slam` | en ağır; `ground_impact@33` | 1,4 m / 1,0 × AttackRange | 1,8 | 20 | 30–35 / 64 f, `attack_recover@52` | 3,40 | 1,50 | +0,5 m (f28–33) |
| A5 | **BiteGrab** → `bite_grab` | yakın kapma + ısırık; §1.4 | 0,8 m / 0,65 × AttackRange | 1,2 | 15 | 14–18 / 40 f, `grab_start@13`, `grab_end@21`, `attack_recover@32` | 1,60 (%60 vuruşta + %40 `grab_end`de) | 1,60 | +0,25 m (`attack_step` f12) |

Zamanlama tek kaynak: **klip olayları** (DEC-029). Telegraph = tier `TelegraphTime` × stil çarpanı → klip rate (S-06: rate = telegraph_base / telegraph_tier); Active = `hit_window_start→end`; Recovery = `hit_window_end → attack_recover` (S-06'nın son 6 f "interruptible" kuralı: `attack_recover` gelince faz None, cooldown başlar). Zamanlayıcı yedekleri (tier tablosu + `WindowStartGrace/EndGrace` 0,5 s) kalır. Stil telegraph çarpanları: Swipe 1,00 · Lunge 0,85 · Sweep 1,45 · Overhead 2,10 · BiteGrab 1,30; imza stilde 1,00'a normalize (Standard Swipe = 0,55 s; Elite Sweep = 0,8; Ultra Overhead = 1,15; WP-052 parite testi 6 klip satırına çıkar, sapma > %15 not, > %50 FAIL). Hasar normalizasyonu: Ultra Overhead 34, Ultra Sweep 18; Elite Sweep 18, Elite Overhead 34, Elite BiteGrab 16; Standard Swipe 10, BiteGrab 16, Sweep 18, Lunge 9.

Kök hareketi: klipler root keyli, `root_delta_xz` manifestte (S-06). **S-03 uygular**: `attack_step` olayında (Swipe/BiteGrab) ve Lunge'ın Active penceresinde manifest deltasını CharacterController `Move` ile (NavMesh kırpması, DEC-032 kalabalık); Animator root motion **kapalı** kalır (koordinasyon kalemi §3.4). Lunge'ın eski `ForwardImpulse` (1,85 × MoveSpeed) yerine manifest +1,4 m / pencere süresi.

Oyuncuya geri itme oyuncu tarafında yok; `EnemyAttackLandedInfo`'ya `Style` + `KnockbackScale` (Swipe 0 / Lunge 0,6 / Sweep 0,8 / Overhead 1,0 / BiteGrab 0) eklenir — S-02/S-07 (FP kamera darbesi) tüketir.

### 1.2 Tier izinleri ve ağırlıklar (`AttackStyleProfile.TierWeight[4]`; S-06 A4 dağılım önerisi kabul)

| Tier | Swipe | Lunge | Sweep | Overhead | BiteGrab | Klip varyantı (S-06) |
|---|---|---|---|---|---|---|
| Standard | 0,55 | 0,05 (1,8–2,2 m, rate 0,85, kök ×0,8) | 0,10 (rate 1,1) | 0 | 0,30 (§1.4 koşulu yoksa 0) | — |
| Special (Runner) | 0,40 (rate 1,25) | 0,60 (1,8–2,6 m bandı) | 0 | 0 | 0 (S-06: Runner kullanmaz) | — |
| Elite (Guard) | 0 | 0 | 0,45 | 0,25 (ön ±20°, ≤ 0,8 × menzil; rate 1,15) | 0,30 (rate 0,85) | swipe yok (S-06 Elite rate 0,9 tanımlı ama dağılımda yok → ağırlık 0, veri ile açılabilir) |
| Ultra (Executioner) | 0 | 0 | 0,40 (`sweep_ultra`, yandan 30–70°) | 0,60 | 0 | — |

Special/Elite/Ultra kuralları: Special Lunge cooldown tabanı 1,9 s (WP-040) korunur, ıskalayan Lunge `attack_miss` → S-06 `stumble` geçişi (S-03 `PlayStumble` kancası mevcut); Elite `HoldsChokepoints` korunur; Ultra "fark etti" anı (WP-052 Noticed) = ilk Overhead telegraph'ındaki `roar` olayı (S-08 çalar, S-03 yalnız beat'e `Seconds` yazar).

### 1.3 Seçim ölçütü (`AttackSelector.Select(in ctx, ref random)` — saf, sabit tampon)

Girdi: tier, oyuncuya düz mesafe, oyuncuya açı, oyuncunun bize açısı ("arkası dönük" = > 110°), oyuncu hızı, son 2 stil + son el, slot tahtası, aktif kapma var mı.
Skor(stil) = TierWeight × RangeFit × AngleFit × VarietyPenalty × SlotFactor:
- RangeFit: menzil bandı içinde 1; bandın %20 dışında 0 (Lunge 1,8–2,6 / Standard 1,8–2,2; BiteGrab ≤ 0,8 m; diğerleri ≤ menzil).
- AngleFit: |açı| ≤ yay → 1; yay+15° içinde 0,5 (Windup'ta yaw 240°/s); üstü 0. Sweep 30–70° yan açıda ×1,3.
- BiteGrab koşulu: oyuncu arkası dönük **veya** oyuncu hızı < 1,0 m/s → ×1, aksi ×0; oyuncuda aktif kapma varsa ×0.
- VarietyPenalty: son stil aynı → ×0,25; son iki aynı → ×0 (**üç kez yasak**; Ultra: Overhead-Overhead-Sweep zorunlu). Swipe içinde el: oyuncu bedenin solundaysa `swipe_L`, sağındaysa `swipe_R`, aynı hizadaysa son elin tersi — **aynı el iki kez üst üste yasak**.
- SlotFactor: §1.5'te slot yoksa ×0.
Seçim: ağırlıklı deterministik çekiliş (`EnemyRandom`, spawn tohumu); toplam 0 → saldırı yok, beden baskı yapar. `stackalloc float[5]`.

### 1.4 `bite_grab` kuralları (grapple mekaniği S-03'te; S-06 klibi tek ısırık, kapma penceresi `grab_start@13 → grab_end@21`)

| Kural | Değer |
|---|---|
| Kim kapabilir | Standard, Elite (Guard). Special/Ultra asla |
| Tetik | mesafe ≤ 0,8 m, açı ≤ 15°, oyuncu arkası dönük (> 110°) **veya** oyuncu hızı < 1,0 m/s; slot boş; oyuncuda aktif kapma yok |
| Oyuncuda aynı anda kapma | **en çok 1** (slot 1; `GrabState.Active` küresel) |
| Kapma başlangıcı | klip `grab_start` (f13) **ve** `hit_window` bağlantısı → `GrabEvents.Started(body, lifeId, tier, seconds)`; bağlantı yoksa `grab_start` boşa gider (kapma yok, klip devam eder = ıska) |
| Oyuncu tarafı (S-02 tüketir) | hareket hızı ×0,25, nişan serbest (A5.5: nişan asla 120 ms'den uzun bloke değil); FP/TP kamera çekmesi S-07; HUD istem S-09 |
| Hasar | ısırık hasarı 1,60 × tier: **%60 `hit_window` bağlantısında**, **%40 `grab_end`de** yalnız kapma kaçılmadıysa (Standard 16 → 10 + 6; Elite 16 → 10 + 6) — "zamana yayılan" pay, kaçış ödülü |
| Süre | klip `grab_end` (f21; Standard rate 1,0 → 0,27 s; Elite rate 0,85 → 0,31 s); zamanlayıcı yedek `GrabMaxSeconds` 0,6 s |
| Kaçış | kapan bedene `ImpactForce ≥ 0,85` tek vuruş (bir saçma yeter) → anında bırakma: sentetik `grab_end` + `attack_recover`, %40 hasar iptal, beden `hit_stagger` (REQ-124 kapısı bağımsız: kaçış kapıyı tüketmez). Ek girdi (itme/melee) R1'de yok |
| Kalabalık kapma sırasında | toplam Windup tavanı 4 → **2**; BiteGrab slotu dolu; Lunge slotu kapalı (yavaşlamış oyuncuya dalış adaletsiz); Swipe/Sweep serbest; kapan bedenin `SteeringTarget = konum` (kalabalık etrafından akar) |
| Kapan bedenin ölümü / sendelemesi | `GrabEvents.Ended(reason = Killed/Escaped/Timer/Staggered)`; %40 iptal |
| Kamera modu | kural aynı; FP'de "arkadan kapma" `rear_threat` lead'ine (§6) tabi |
| Beat | `attack_commit/hit/miss` mevcut + `grab_start`/`grab_end` klip olayları S-08/S-07'ye doğrudan (S-06 A8.2); S-03 `GrabEvents` yalnız oyuncu durumu için |

### 1.5 Kalabalık koordinasyonu — `AttackSlotBoard` (statik, sabit diziler)

| Stil | Eşzamanlı Windup slotu | Commit sonrası tür cooldown'u (küresel) | Windup başlangıç yayılımı |
|---|---|---|---|
| Swipe | 3 | 0,15 s | başka Windup < 0,12 s önce başladıysa 0,08–0,20 s ertele |
| Lunge | 1 | 0,60 s | kapma aktifken kapalı |
| Sweep | 1 | 0,80 s | — |
| Overhead | 1 | 1,20 s | — |
| BiteGrab | 1 | 1,50 s | — |
| Toplam Windup tavanı | **4** (kapma aktifken 2) | | |

Slot alınır `Begin()`; bırakılır `attack_recover` / `Cancel()` / ölüm. Veri: `int[5] active`, `float[5] lastCommit`, `int total`; 0 ayırma; `ResetStatics`.

### 1.6 Olaylar ve kaynak kimliği

S-06 A8.2 yeni olaylarının S-03 kullanımı: `attack_recover` → Recovery biter, slot bırakılır · `grab_start/grab_end` → §1.4 · `attack_step` → manifest kök adımı uygulanır · `roar` → S-08 doğrudan; S-03 Ultra Noticed anı olarak işaretler · `ground_impact` (Overhead f33) → S-07/S-08 doğrudan, S-03 kullanmaz · `stumble` geçişi S-06'da, S-03 `attack_miss` verir. `SourceId(tier, style)` 20 girdilik statik tablo (`"Zombie.Elite.BiteGrab"`); `EnemyCombatEventInfo.Style` 5 stil (S-07/S-08 stil satırları).

---

## 2 · Ölüm (N6): S-03 darbe verisini verir, S-06 aileyi seçer (WP-071 A3, 1980e0f; S-06 ile uzlaşıldı)

Bölünme (sözleşme): **S-06** ölüm ailesini (`death_fall_back` / `death_fall_fwd` / `death_spin_side` / `death_legs_out` + `death_ultra_timber` + Special `death_arc_electro` / `death_gel_slip`) yön/kuvvet/silah sınıfından seçer; onset klibi + `ragdoll_handoff` + powered ragdoll + settle + freeze + havuz sıfırlamasını yürütür (A3.1–A3.6). **S-03** mevcut `Die()` çağrısı üzerinden yalnız `DeathImpulse` verir ve ölümden sonra bedene **hiçbir oynanış bağlamaz** (A3.7). Mevcut `ZombieAnimator.BeginDeath(dir, intensity, region, heavy, …)` içindeki `ZombieDeathStyle` seçimi ve 8 klip indeksi **kaldırılır**; sürücü girişi S-06'nın `SetDeathImpulse(in DeathImpulse)` çağrısıdır (blok rig için S-03 tarafında eski prosedürel düşüş kalır).

### 2.1 S-03'ün garanti ettiği `DeathImpulse` (`DD.Prototype.Damage.DeathImpulse`, readonly struct, `Assets/_Prototype/Damage/DeathImpulse.cs`; adlar S-06 ile bağlayıcı)

| Alan | Tür | Kaynak | Garanti |
|---|---|---|---|
| `Direction` | Vector3 (birim, dünya; yatay bileşen korunur) | `DamageHit.Direction` (atıcı→hedef); patlamada merkez→beden | asla sıfır (yedek: beden ileri) |
| `Force` | float (DamageForce birimi, **governor sonrası**) | `DamageHit.ImpactForce` | ≥ 0; S-06 A3.2 `I_weapon` N·s satırını `WeaponClass`'tan seçer, `Force` ölçek/falloff bilgisidir |
| `Source` | `DamageSourceKind` (+`Gel`) | mevcut | |
| `WeaponClass` | `WeaponClass` geçici enum `Unknown / Rifle / Smg / Shotgun / Lmg / Sniper / Launcher / Special` (S-02 WP-069 gelene kadar; sonra ADR-069-1 enum'u) | ADR-069-1 yoksa §3.3 sınıflandırıcı (AR → `Rifle`, Chain/Gel → `Special`) | `Unknown` yalnız debug `Kill()` |
| `Region` | `BodyRegion` | `DamageResult.Region` | Head/UpperBody/LowerBody |
| `Point` | Vector3 (dünya) | `DamageHit.Point` | S-06 en yakın ragdoll kemiğini çözer; S-03 kemik bilmez |
| `BodySpeed` | float (m/s, yatay) | locomotion + `_knockbackVelocity` | Runner momentum ölümü (A3.5 D3 "hız > 1,5 m/s") |
| `State` | `ZombieBodyState` `Standing / Staggered / Sliding / Downed / Electrocuted` | ölüm anındaki durum | A3.5 S1/S2 aileleri (`Sliding/Downed` → slip, `Electrocuted` → convulse); kapma sırasında ölüm = `Standing` (kapma S-03'te `GrabEvents.Ended(Killed)`) |

Tier/ağırlık sürücüde zaten bilinir (`ResetForSpawn(tier, …)`), struct'a girmez. Ek alan (overkill, LMG akış) **yok**; S-06 A3.5 D4 "LMG akışı" ölçütünü kendi A5.3 akümülatöründen alır.

### 2.2 S-03'ün S-06'ya sunduğu eşik önerileri (karar S-06'nın; A3.5 tablosuna girdi)

| Öneri | Değer | Gerekçe |
|---|---|---|
| Onset kısaltma ("direkt ragdoll" hissi) | `Force ≥ 3,0` (Sniper / Shotgun ≤ 4 m / Launcher) veya `Source = Explosion` → onset ≤ 4 f | yazılı niyet değil fizik okunmalı; D2 Knockback ailesi |
| Momentum ailesi | `BodySpeed ≥ 1,5 m/s` → D3 Stumble-fall (S-06 eşiğiyle aynı) | Lunge ortası ölüm |
| Jel / şok | `State ∈ {Sliding, Downed}` → S1 slip (itki 0, sürtünme 0,05); `Electrocuted` → S2 convulse | §4 |
| Ultra | her zaman `death_ultra_timber` (yön yok; arkadan itkide 180° yaw) — S-06 kararı kabul | |
| Baş | `Region = Head` ve `Force < 1,6` → D1 Drop; ≥ 1,6 → D4 Spin (sniper baş) | |
| Sendelerken ölüm | `State = Staggered` → D2/D3 ailesi mevcut sendeleme yönünde (klip kesilmez, `ragdoll_handoff` erken) | okunurluk |

### 2.3 Ölümden sonra S-03'te kalanlar (A3.7 ile uyumlu)

- Onset karesinde (S-03 `Die()`): `_alive=false`, ledger `NotifySpawnLifecycleDied`, `DamageTargetRegistry.Unregister`, kalabalık kaydı çıkışı, `_attack.Cancel()` (+ slot bırak; DEC-029 tüketicisi için sentetik `hit_window_end` S-06'da), kapma varsa `GrabEvents.Ended(Killed)`, hitbox kapalı, CharacterController kapalı, altın/XP/`death_*` beat — hepsi **şimdi** (mevcut). Ardından `SetDeathImpulse(in DeathImpulse)`.
- `TickDying` artık cesedi **hareket ettirmez** (`_knockbackVelocity` kayması kalkar; ragdoll/yazılı klip kökü S-06'nın): yalnız despawn geri sayımı + zemin bekçisi (§2.4) + `DeathFinished` (S-06 `death_end` / freeze).
- Ceset ömrü ve havuz: **tek sahip S-03** (`CrowdFeelSettings.CorpseLinger` = DEC-023 ledger'a dönük ömür; havuz bileti); değerler S-06 A3.1/A3.6 tablosuyla hizalanır: Yüksek Standard/Special **4,0 s**, Elite 5,0, Ultra 6,0 (×1,5); Sinematik ×1,25; Performans ×0,6 (`QualityTiers.Pick`); jitter ±%15; tavana yakın (Occupied ≥ Ceiling − 2) ×0,4, Ultra tabanı 1,2 s. Despawn anında S-06 "gömülme" (y −0,4 + fade 0,6 s) görselini oynatır ve `RagdollReset` yapar. Ceset tavanı (S-06 16/10/6) ile S-03 ömrü aynı sayacı sürer: tavan aşımında S-03 en eski cesedi despawn eder (S-06'nın "en eski gömülür" kuralıyla aynı olay). **Koordinasyon kalemi (C-00):** ceset ömrü/tavanı tek sahipte (öneri S-03, ledger nedeniyle), S-06 yalnız görsel.
- Aktif ragdoll tavanı (Sinematik 12 / Yüksek 6 / Performans 2) ve "kap doluysa tam yazılı klip" **S-06'nın**; S-03 kademe/kap için veri geçmez.

### 2.4 WP-061K/L ödülsüz kaldırma ile uyum (değişmez kurallar)

1. **Kaldırılan beden asla ragdoll olmaz, asla ödemez:** `RemoveOutOfWorld` → `Despawn()`; `Die()` çağrılmaz → `SetDeathImpulse` çağrılmaz; `Despawn`/`RagdollReset` rigidbody'leri kinematik + collider kapalı bırakır.
2. **Ragdoll ceset dünyadan düşerse kaldırma sayılmaz:** `TickOutOfWorld/TickStuck` yalnız canlı bedende (mevcut `!_alive || _dying → false`). `TickDying` kendi bekçisi: S-06 kalça y < zemin −0,3 settle kuralı (A3.1 madde 4) dondurur; S-03 ek olarak kök/kalça y < `AbsoluteFloorY` (−50) ya da son NavMesh y − `FallDepth` (6 m) → **sessiz despawn** (ödül ödendi, `OutOfWorldRemoved` yükselmez, sayaç artmaz; telemetri `corpse_fell_off_world`).
3. **Kayan (jel) canlı beden dünyayı terk ederse** WP-061K ağı aynen işler (despawn, ödülsüz); S-05 halka/havuz çitleri (WP-038M/R) fiziksel engel.
4. Stuck watchdog muafiyetleri: `State ∈ {Sliding, Downed, Electrocuted}` ve `IsGrabbing`.
5. Kaldırma olayına `ZombieBodyState` eklenir (telemetri; hedef: jel/şok/kapma sırasında kaldırma 0).

DEC (C-00'dan istenecek): "ölüm darbesi bölünmesi — S-03 `DeathImpulse` verir, S-06 aile/ragdoll seçer; ölümden sonra oynanış bedene bağlanmaz; ceset ömrü/tavanı tek sahip".

---

## 3 · Projectile darbe tepkisi (WP-069 mermileri)

### 3.1 Silah sınıfı profilleri — `HitReactionProfile[WeaponClass]` (DamageForce ölçeği; 1 = AR mermisi; governor + akümülatör mevcut)

| Sınıf (`WeaponClass`) | Mermi başına ImpactForce | Atış temposu (varsayım) | Akümülatör kararlı durumu* | Sendeleme (REQ-124 kapısı, S-03) | Ek davranış (S-03) | Ölümde `DeathImpulse` → S-06 aile önerisi (§2.2) |
|---|---|---|---|---|---|---|
| AR ×2 (`Rifle`) | 1,0 | 600–700 rpm | ≈ 2,3 (< 2,4) | asla (REQ-123) | yönlü flinch (S-06 override/additive) | yöne göre fall_back / fall_fwd; baş → D1 |
| SMG ×2 (`Smg`) | 0,55 | 850–950 rpm | ≈ 1,7 | asla | hızlı küçük flinch (RepeatFalloff) | aynı |
| Shotgun ×2 (`Shotgun`) | saçma 0,85 × 8 (≤ 3 kare içinde) | — | 6,8 tek pencere | **her zaman** (≥ 2,4); itme ≥ 4,5 → `KnockbackProfile` 0,6 m / 0,25 s (≤ 4 m; §3.4) | ≤ 4 m'de tüm saçmalar → `Force` ≥ 3,0 | D2 Knockback (onset ≤ 4 f önerisi) |
| LMG ×2 (`Lmg`) | 0,80 | 700–800 rpm | ≈ 2,1 (sendeleme yok) | asla | **Suppression**: 0,35 s yarı ömürlü tekrar sayacı ≥ 4 → hız ×0,70 (son vuruştan 0,30 s), her vuruş `Direction × 0,35 m/s` sürekli itme (tavan 1,2 m/s, sendelemeden bağımsız, Elite ×0,45 / Ultra 0); görsel sarsılma S-06 A5.3 | D4 Spin (S-06 A5.3 akümülatörü > 0,6) ya da D3 (`BodySpeed` ≥ 1,5) |
| Sniper (`Sniper`) | 3,2 | tek | 3,2 | **her zaman** (yoğunluk 1,33, 0,35 s; `z_hit_stagger_<dir>`), itme yok (< 4,5) | Elite'te kapı kapalı → S-06 override ağırlık 1,0 (A5.4) | `Force` ≥ 3,0 → D4 Spin (yan/baş) veya D2 (ön), onset ≤ 4 f |
| Launcher (`Launcher`) | merkez 6,0, kenar 1,5 (S-02 falloff) | tek | 6,0 | her zaman; `KnockbackProfile` 1,2 m / 0,25 s (≤ 2 m) | Elite/Ultra sağ kalırsa: sendeleme + profil ×TierScale | D2 Knockback (havaya kalkış S-06 A3.2), bacak → D3 legs_out |
| Zincir yıldırım (`Special`, Source Chain) | 0,25 (ölümü §4.1 devralır) | — | — | asla (stun-lock yok) | Electrocuted durumu | S2 convulse (`State = Electrocuted`) |
| Jel (`Special`, Source Gel) | 0 | — | — | — | Slide/Fall | S1 slip (`State = Sliding/Downed`) |

\* yarı ömür 0,12 s: kararlı toplam = f / (1 − 0,5^(Δt/0,12)).

### 3.2 REQ-124 governor ve tier direnci

- `StaggerGovernor` (0,15 s pencere, tavan 9, aşan %15) **değişmez**; projectile vuruşları uçuş anında gelir, hedef kimliğiyle anahtarlanır — çalışır. Çift pompalı (13,6) ikinci şeli %15'e düşürür: istenen (stun-lock yok).
- Saçmalar: bir şelin 8 saçması **aynı bedende ≤ 3 kare içinde ve ayrı `DamageTransaction` kimlikleriyle** inmeli (aksi `RefuseDuplicateTransaction` 7'sini reddeder) — §8 S-02 şartı.
- Susceptibility (DEC-021): Standard 1 / Special 1 / Elite 0,45 / Ultra 0,12; itme tabanı 0,2 → Ultra hiç itilmez; Elite sniper'da sendeler (3,2 × 0,45 = 1,44 < 2,4 → **sendelemez**; pompalı 6,8 × 0,45 = 3,06 → sendeler, itilmez). Kabul: Elite'e sniper "ağır flinch" (HeavyBodyFlinchFloor) — istenen okunurluk; Ultra'ya yalnız pompalı+windup bonusu (6,8 × 0,12 × 1,35 = 1,1) → asla; Ultra'yı yalnız zırh kırılması sendeletir (mevcut).
- Ölümde `HitStop` kuralları aynı (REQ-127).

### 3.3 Sınıflandırıcı (ADR-069-1 gelene kadar)

`WeaponClass Classify(Source, ImpactForce)` (geçici enum `Unknown/Rifle/Smg/Shotgun/Lmg/Sniper/Launcher/Special`, `DeathImpulse.cs`): Pellet→`Shotgun`; Explosion→`Launcher`; Chain/Gel→`Special`; Bullet ≥ 3,0→`Sniper`; Bullet ≥ 0,7 ve < 0,95→`Lmg`; < 0,7→`Smg`; aksi `Rifle`; debug `Kill()`→`Unknown`. Sözleşme gelince `DamageHit.WeaponClass` doğrudan okunur, sınıflandırıcı testte kalır.

### 3.4 Knockback / darbe sahipliği — S-06 ile UZLAŞILDI (C-00 kararı gerekmez)

| Sorumluluk | Sahip | Ne verir / ne okur |
|---|---|---|
| Darbe **verisi**: silah sınıfı itkisi (§3.1), REQ-124 governor (9/0,15 s), tier direnci, stagger **doğrusu** (akümülatör 2,4, cooldown 1,1 s, süre ≤ 0,45 s) | **S-03** (`EnemyHitReactionState`) | `HitReaction` (flinch, stagger bool/süre/yoğunluk, knockback) — mevcut |
| Yer değiştirme: CharacterController `Move`, NavMesh kırpma, kalabalık/DEC-032, dünya-dışı ağ | **S-03** (`ZombieEnemy`) | S-06 A5.4 hissi **veri** olarak: `KnockbackProfile[WeaponClass] {Distance, Seconds, EaseOut, TierScale}` = 0,6 m / 0,25 s ease-out (Shotgun ≤ 4 m), 1,2 m (Launcher ≤ 2 m), Guard ×0,5, Executioner 0; hız-başına-aşım formülü (0,9 m/s, tavan 4,5) bu profille değişir |
| Poz / animasyon tepkisi: `z_hit_knockback_<tier>` klip ağırlığı, `knockback_end`, `override_upper` ağırlığı (`impulse01`), `additive_impulse` yayı, LMG sarsılma akümülatörü (A5.3), stagger klipleri | **S-06** (`ZombieSkinnedVisual`) | S-03'ten `KnockbackState {Active, Direction, Speed, Seconds, Progress01}` (kare başına okunur, olay yok) + `HitReaction`; `impulse01` görsel ağırlıktır, oynanış kapısı değildir (kapı kapalıysa override ağırlık 1,0 ile oynar — A5.4 madde 45) |
| Saldırı kök adımı (`attack_step`, Lunge +1,4 m; manifest `root_delta_xz`) | **S-03** uygular (aynı ilke: transform'a tek yazar), Animator root motion kapalı | S-06 kök deltasını manifestte verir |

Gerekçe: S-03 hareketi NavMesh/kalabalık/tavan kısıtlarıyla tek yerde çözer (WP-053/055E ayırma ve K1 dersleri); S-06'nın istediği his veri olarak aynen korunur; stagger tek doğrusu S-03 (REQ-124 süiti mevcut).

---

## 4 · Special etkileşimleri (N1) — yalnız iki Special

Eski `ZombieSpecialReactions` (Kagura/Ofuda/Tide), `SpecialReactionKind.Push/Web/Pull`, `sw_push/web/pull` satırları ve WP-022I Netted/Dazed işaretleri **kaldırılır** (S-02'nin silah kaldırma dilimiyle aynı entegrasyon penceresi). Yeni beat türleri eklenir: `SpecialShock=12 (sw_shock)`, `SpecialSlide=13 (sw_slide)`, `SpecialFall=14 (sw_fall)`, `RearThreat=15 (rear_threat, §6)` → KindCount 16; eski 9–11 kaldırma diliminde (B4) silinir ve enum yeniden numaralanır (KindCount 13). S-07/S-08 `Kind` adıyla bağlanır, tam sayı ile değil; `RowRoots` dizisi tek kaynak.

### 4.1 Zincir yıldırım Special'ı (ad/tasarım S-02, DEC-094)

**Hedef seçimi** (`ChainTargetResolver.Resolve(origin, buffer)` — saf, `DamageTarget[24]` sabit tampon, S-02'nin projectile ilk temasından çağrılır; sonucu S-02 hasar uygular, S-03 durumu uygular):

| Parametre | Değer | Gerekçe |
|---|---|---|
| İlk hedef | projectile'ın temas ettiği beden (S-02) | |
| Hop sayısı N | Sinematik **10** / Yüksek 8 / Performans 6 | referans silahın 10 zinciri; VFX ark maliyeti kademeli |
| Arama yarıçapı (ilk hedef merkez) | 8 m | |
| Hop menzili (ardışık) | 5 m | |
| Sıra | açgözlü en yakın ziyaret edilmemiş | deterministik |
| Görüş hattı | **evet**, göğüs→göğüs `Physics.Linecast` Environment maskesi; hop başına 1 (≤ 10 linecast/atış) | duvar arkasına arklanmaz (kapalı alanlarda okunur) |
| Hop gecikmesi | 35 ms/hop (görsel ardışıklık; hasar S-02 aynı gecikmeyle) | |
| Cesetler / kalkan (IsRising) / sliding | hedef değil (`DamageTargetRegistry` zaten cesedi düşürür) | |
| Tier | Standard/Special: hedef + iletir · Elite: hedef + iletir, şok ×0,6 · **Ultra: yalnız doğrudan (ilk) hedef olabilir, zinciri sonlandırır (iletmez), şok ×0,4, ölüm garantisi yok** (hasar S-02) | Ultra "yıldırıma direnir" |

**Electrocuted durumu** (`ZombieEnemy` yeni durum; `IsElectrocuted`):

| | Standard | Special | Elite | Ultra |
|---|---|---|---|---|
| Süre | 1,2 s | 1,0 s | 0,7 s | 0,5 s |
| Hareket / saldırı | 0 / iptal (`_attack.Cancel`, slot bırak) | | | |
| Hitbox | açık (vurulabilir) | | | |
| Animasyon kancası | `SetSpecialVisual(ZombieSpecialVisual.Electrocuted)` → S-06 `hit_electro_loop` (24 f döngü, A5.3); gelene kadar `Dazed` görseli | | | |
| Ölüm | hasar öldürdüyse: `_alive=false` **hemen** (ledger/ödül/`death_*` beat şimdi), hitbox kapalı, döngü **0,6 s** (Ultra 0,4; `ShockDeathHoldSeconds`) sürer, sonra `Die()` → `DeathImpulse.State = Electrocuted` → S-06 `death_arc_electro` (S2 convulse, `ragdoll_handoff@8`) | | | |
| Beat | `sw_shock` (Seconds = süre, Position göğüs) | | | |
| Tekrar | aynı `TransactionId` bir kez; ardışık atışta süre uzar, yığılmaz | | | |
| Jel ile | şok kaymayı dondurur (hız 0), şok bitince Downed'a düşer | | | |

Şok, REQ-124 sendeleme sayacına **yazmaz** (Chain 0,25 zaten eşiğin altında; stun-lock kilidi `StaggerCooldown` şok tarafından tüketilmez).

**§4.1a · S-02 uzlaşısı (WP-069 DIRECTION §7a, 24.09; yukarıdaki tabloların yerine geçer):**
- İlk hedef orb'un çarptığı bedendir; yere çarpan orb söner (S-02). İlk hedef etrafında 8 m arama yok.
- Hop yarıçapı veriden gelir (`WeaponArcChainBlock.jumpRadius` = 6 m), hop menzili 5 m kalkar.
- Hedef sayısı oyun kuralıdır: `maxTargets` 8 = 1 + 7 hop. Kademeden bağımsızdır; kademe yalnız görsel arkları seyreltir (S-07). §7.1'deki hops 6/8/10 satırı görsele taşınır.
- Ultra zincirin herhangi bir yerinde olabilir ve zincir ondan devam eder (🔴 #2 varsayılanı).
- Şok süresini S-02 geçirir: `ApplyShock(transactionId, seconds)`, `shockSeconds` 1,2 s, Ultra 2,5 s. Tier tablosu kalkar.
- S-02 öldürücü hasarı titremenin sonunda indirir. Ölüm, skor ve altın o anda sayılır; düşüş hemen `State = Electrocuted` ile başlar. Titreme ortasında gelen öldürücü zincir vuruşu, düşüşü kalan süre kadar bekletir.
- API: `ChainTargetResolver.Resolve(first, origin, maxTargets, hopRadius, buffer)`.

### 4.2 Jel Special'ı (kaydırıcı birikinti; ad/tasarım S-02, DEC-094)

**Kayıt** — `GelPuddleRegistry` (statik, sabit halka tamponu **24** birikinti; S-02 `Register(in GelPuddle)`/`Expire(id)` çağırır):
`GelPuddle { int Id; Vector3 Center; float Radius; float SurfaceY; float CreatedAt; float Lifetime; int SourceTransaction; float Strength01 }`. Birleşen birikintiler S-02'de tek kayıt olabilir; AI için "içeride" = XZ mesafe² ≤ R² ve |y − SurfaceY| ≤ 0,6. Sorgu: beden başına 2 karede bir (faz yayılımı), ≤ 24 karşılaştırma, 0 ayırma. Kademe: Sinematik 24 / Yüksek 16 / Performans 10 aktif birikinti (S-02 tavanı aynı).

**Durum makinesi** (`ZombieEnemy` `GelState`: None → Sliding → Falling → Downed → GettingUp → None):

| Geçiş | Koşul | Davranış | Beat |
|---|---|---|---|
| None → Sliding | birikinti içinde ve yatay hız ≥ 0,6 m/s | traksiyon 0: yön = mevcut hız yönü (kilit), NavMesh/kalabalık yönlendirmesi kapalı, hız sönümü yalnız 0,15 m/s² (momentum korunur), saldırı iptal + slot bırak, `SteeringTarget = konum` (kalabalık etrafından akar), animator `Slide` pozu (bacaklar açılır) | `sw_slide` |
| Sliding → None | birikintiden çıktı **ve** düşmedi | 0,25 s "toparlanma" hız ×0,5 | — |
| Sliding → Falling | 0,15–0,35 s kayma sonrası olasılık: Standard 1,0 · Special 1,0 · Elite 0,70 · Ultra 0,35 (her 0,2 s'de yeniden atılır); ayrıca **domino**: 0,9 m içinde Falling/Downed başka beden varsa 0,6 ek şans | S-06 `hit_gel_slip` (30 f, root +0,6 m kayma yönünde; `ground_impact@12`, Sinematik `ragdoll_handoff@9`); Falling = f0–12 (0,4 s); hitbox açık; kayma hızı düşerken devam eder | `sw_fall` |
| Falling → **Ölüm** | `SlipDamage` öldürdüyse (hasar `ground_impact` anında) | `SlipDamage = MaxHealth × SlipFraction[tier] × RoundScale` — SlipFraction Standard **1,0** / Special 1,0 / Elite 0,35 / Ultra 0,15; `RoundScale` S-02'nin jel gücü (varsayım: r ≤ 12'de 1,0, sonra Standard'da da doygunluk); domino +%10 · Source `Gel` · `Die()` → `State = Sliding/Downed` → S-06 `death_gel_slip` (S1 slip; aynı klip, `death_end@29`) · ödül/beat normal | `death_*` |
| Falling → Downed | sağ kaldı | yerde (klip f13–29 çırpınma + S-06 `hit_gel_slide_loop` / ragdoll kayması): Standard 1,6 s · Special 1,2 · Elite 2,2 · Ultra 2,8; hitbox açık (yerde vurulabilir; region çözümü kemik hitbox'larından — mevcut); birikinti hâlâ aktifse her 1,0 s yerde **+%5 MaxHealth** ek ("çırpınma" hasarı, referansın "jelde kalan ölür" hissi) | — |
| Downed → GettingUp | süre doldu | S-06 `getup_back` (kalça arkaya düşer, tek yön) 0,9 s; ağır 1,2 s; sırasında yürümez/saldırmaz | — |
| GettingUp → None / Sliding | hâlâ birikintideyse ve hızı ≥ 0,6 → **yeniden Sliding** (döngü referans hissin özü; birikinti ömrü döngüyü sınırlar) | | |

Tier farkı: Elite/Ultra jelde hız ×0,6 ("bata çıka", düşme şansı düşük ama yavaş), Standard/Special tam hız → uzun kayış, kesin düşüş. Special koşarken (4,6 m/s) girerse kayış mesafesi ~3–4 m: kalabalık dominoyu tetikler.

**Kaçınma kararı (konsey): zombiler birikintiden KAÇINMAZ — onaylandı.** Gerekçe: (1) referans silahın mekaniği ve hissi (kullanıcı: "çok benzer") jelin *tuzak* olmasına dayanır; kaçınma silahın amacını siler. (2) Oyuncu ustalığı = boğaza/kapı eşiğine jel koymak; kaçınma bunu şansa çevirir. (3) Teknik: dinamik NavMesh alan maliyeti/carving birikinti başına yeniden bake/`NavMeshObstacle` ister — kapalı alan/koridor dünyasında (v5) yol kopmaları ve perf riski; 0-ayırma bütçesine sığmaz. (4) Zombi fantezisi: bilinçsiz koşu inandırıcı, "jel görüp dolanan zombi" değil. Tek yumuşatma: **Ultra** birikintiye girince hızını ×0,6 düşürür (kütle), yine de girer. Telemetri `gel_enter_count / gel_fall_count / gel_kill_count` (tier) ile r1–r12 oranı izlenir; jel öldürme payı > %60 olursa `SlipFraction` düşürülür (denge dilimi, tasarım değişmez). DEC (C-00'dan istenecek): "jel Special'ında zombiler birikintiden kaçınmaz; tier farkı yalnız hız/düşme şansı".

---

## 5 · Dünya v5 spawn / NavMesh / tehdit (N3) — S-05 WP-074 (abc0f70) Ek B'ye karşı

Bağlı S-05 kararları: oynanır **±55 m** (yedek ±50; 110×110), vista 55–85 m yürünmez, halka ±55, NavMesh ±54,5 (WP-038R kuralı), tırmanma 0,30, adacık bağlayıcı kalır; odalar R1–R7 (her biri ≥ 2 çıkış, ≥ 6 m ayrık, aynı duvarda değil); 8 darboğaz (§3.3); 12 kiting halkası (§3.5); 18 spawn kapısı **önerisi** (§3.4 — karar S-03'ün). Greybox B1 (collider + NavMesh v5 + `spawn-gates-v6.json` + `regions-v6`) 25–27.09: **veri dosyaları beklemede** (§8.2).

### 5.1 Spawn kapıları — karar: 18 kapı önerisi **kabul** (12 kenar + 6 orta), eski 12 ad korunur

| Konu | Karar | Gerekçe |
|---|---|---|
| Sayı | **18** (S-03'ün 16–20 bandı içinde); eski 12 kimlik `SpawnPointMarker` eşlemesiyle aynı kalır (testler/konfig kırılmaz), yeni ad görünen ad | 12 kenar kapı ±55 halkasında ≥ 60 m yol → tek başına ilk temas ≥ 20 s; 6 orta kapı (34–62 m yol) tempo tabanını taşır |
| Tercih bandı | **22–45 m** (S-05 önerisi kabul); `NearDistanceFloor` 12 m altı 0, 12–22 rampa; 45–75 m 1 → 0,15 doğrusal | `SpawnWeightTuning` v2 alanları: `PreferBandMin/Max`, `MaxUsefulDistance` |
| `MaxUsefulDistance` | **75 m** (62 → 75) | ±55'te 12 kenar kapının çoğu 60–85 m yol; 62'de yarısı ölü kalırdı |
| Tier mesafe kapısı | Elite/Ultra yalnız oyuncuya **yol ≤ 55 m** kapılardan (S-05 §3.4 kuralı kabul); Special ≤ 75; Standard sınırsız | Executioner ilk temas ≤ 35 s (S-05 B5) |
| Orta kapı (`Kind = Interior`) | görüş engeli arkasında (S-05 kuralı); ek S-03 kuralı: aynı orta kapıdan ≤ 1 doğuş / 4 s (`PointCooldown` mevcut), oyuncu kapıya ≤ 10 m yolda ise ağırlık ×0,25 (yanıbaşında doğmaz), FP/TP'de §5.2 arka/LOS kuralına tabi | "pusu" hissi var, "burnumun dibinde" yok |
| Odalar içi kapı | yok (R1–R7 içinde spawn noktası **yok**; I_HallRear salon arka kapısı *içi* = kapının dış tarafı, oda dışı) | oda-çıkış grafı bot/AI için temiz kalır |
| Damlama | 18 kapı × 4 s = 4,5 spawn/s tavanı; `SpawnRefillClock` değişmez | S-05 hesabı doğrulandı |
| Halka payı (kenar/orta) | hedef 65/35 ± 10 = K10, pencere 24 doğuş ortalaması ≤ 0,45 (C-00 30.09 M-26). ~~orta payı > 50 olursa orta ×0,7~~ yerine: orta ağırlığı hedef 0,35'e odds² ile yönlendirilir (taban 0,1, 6 sahte-doğuş önseli; `SpawnWorldProfile.InnerSteer`); kesin %50 tavan yalnız yedek — Y5 merdiveninde InnerQuota olarak Reachability'den önce gevşer (C-00 30.09). Simülasyon: K10 çıplak orta kütlesi ≈ %65'e kadar tutar; üstü `[SpawnV6] innerMass` ölçümüyle karara bağlı (KNOWN OPEN). Ölçüm 30.09 (V6AiRoundTests istasyonları, oda/iç kapı yakını = **üst sınır**): innerMass 0,65–0,98, innerShare24 tavanda 0,50. K10 kararı bot round'unda (≥ 3 round, innerShare24 + TierGateFallbacks + tavan oranı; C-00 30.09): ≤ 0,45 → değişiklik yok; > 0,45 → gevşetme kullanıcıya R1 raporu kalemi; o zamana dek %50 kesin tavan tek garanti | tehdit yönü çeşitliliği korunur |

Kamera modu kuralları (`SpawnWeightTuning` v2; mod `CameraModeService.Current` (S-01 f23f38e7), `Changed` olayında tablo satırı değişir; servis yoksa TopDown):

| | TopDown | TP | FP |
|---|---|---|---|
| "Ekranda" testi (DEC-081 kalkış + `OnScreenFactor`) | `JudgeVisibility` viewport bandı (mevcut, `RiseViewMargin` 0,12) | **frustum + LOS**: `CameraView.IsInView(nokta+göğüs, 0)` **ve** `Physics.Linecast(kamera, nokta göğüs, Environment)` engelsiz → OnScreen; duvar arkası orta kapı 20 m içinde de OffScreen (S-05 (b) kabul) | aynı; FP'de ek `IsRearBlind` (§5.2) |
| `BehindCameraRejectDistance` (arka nokta ret) | 15 m | 16 m | **18 m** |
| Min doğuş mesafesi | 12 m (kalır, S-05) | 14 m | 16 m |
| `OnScreenFactor` | 0,35 | 0,30 | 0,25 |
| **Arka yarımküre doğuş payı tavanı** (S-01 A-7 → **%35**) | yok | %45 | **%35**; kayan pencere son 8; pencere ≥ tavan → arka ×0 (yol-hazır başka nokta yoksa ×0,25); altında ×0,7 |

"Arka yarımküre" noktası = `IsRearBlind && !IsInView(nokta, 0)` ve görüş ilerisine göre arka yarı düzlem (FP); TP `!IsInView(nokta, 10)` ve arka yarı düzlem. LOS linecast kapı başına refill tick'inde 1 (≤ 18/tick, `PathQueriesPerTick` ile aynı bütçe sınıfı; sonuç 0,5 s önbellek — `PathCacheInterval` deseni). Gerekçe %35: FP'de arka yarımküre alanın yarısı; %50 kör noktaya rastgele, %25 arkadan tehdit yok. Denge izleme (WP-070 §7.3): FP ölüm > 1,3× TopDown → tavan 35 → 25, §6 lead 0,8 → 1,0 s. Telemetri `spawn_rear_share(mode, window8)`, `spawn_gate_use(id, tier, pathMetres)`.

### 5.2 NavMesh / bölge profili (`RegionNavProfile` v6)

| S-05 öğesi | S-03 karşılığı | Kapasite / maliyet |
|---|---|---|
| Odalar R1–R7 (oda-çıkış grafı §2) | **`NavRegionKind.Room=4`** hacmi (id = oda, `exits[]` = kapı hacimleri) | kapasite = taban alanı ÷ 2,5 m² (tavan 8: R1 salon 8, R2/R3 6, R4 4, R5/R6 3, R7 açık avlu 8); maliyet 1,0 (oda kaçınılmaz değil); Elite `HoldsChokepoints` odada kapı eşiğini tutar |
| 8 darboğaz C1–C8 (iç ≤ 2,5 m) | `Gate` hacmi (C2–C7 kapılar), `Bridge` (C8 köprü 3,0 m, 21,5 m koşu), C1 taş atlama `Corridor` (2,0 m, 2 × 6 m) | Gate kapasite 2 (2,0–2,2 m) / 3 (2,4 m), maliyet 1,15, alternatif = S-05 kuralı "15 m içinde ≥ 3,5 m" hacmi; Bridge kapasite 2, maliyet 1,35 (WP-054), alternatif doğu kıyı; C1 kapasite 1, maliyet 1,3, alternatif köprü |
| Koridorlar K1–K6 (iç 4,0–4,5 m) | `Corridor` hacmi yalnız **K1** (tam kapalı 36 m; kapasite 6, maliyet 1,3, alternatif bambu bypass); açık yanlı kairō K2–K6 `Open` (4 m, direkler dışta) | 40 beden 4,5 m'de akar (S-05 K3) |
| 12 kiting halkası L1–L12 | `ThreatRoute` düğüm listesi (spawn seçici + bot); S-03 her halka için "giriş kapısı" eşlemesi (halka üstündeki kapılar ×1,2 oyuncu o halkada iken) | veri, maliyet yok |
| Yükseltilmiş zemin 0,35 / dais +0,30 | tırmanma 0,30 kuralı, off-mesh link **yok** (kayma/ragdoll link geçemez) | `SampleRadius` 2 m kalır |
| Halka ±55 / NavMesh 54,5 | `WorldBoundary` yeniden kurulur (S-05); WP-061K `FallDepth` 6 / `AbsoluteFloorY` −50 kalır; `StuckWindowSeconds` 9 s kalır | WP-038R adacık ölçütü v5 bake'inde zorunlu |
| Kapılar ≥ 2,2 m (2 zombi yan yana) | Ultra (r 0,9) tek başına geçer, yan yana geçmez; oda başına en az bir ≥ 3 m kapı (R1 ön 3 m; R2/R3/R4 2,2 → **S-05'e istek: Ultra için oda başına bir 3 m kapı ya da Ultra odaya girmez kuralı** (öneri: Ultra R2/R3/R4'e girmez, kapıda bekler = DEC-032 duvar)) | koordinasyon |
| Büyütme | `PathQueriesPerTick` 4 → **6**, `RecentUseMemory` 12 → 16 s, tehdit `NearRadius` 6 m sabit, ceiling 40 sabit (DEC-023) | nav+crowd bütçesi §7 |

Kayma (jel) NavMesh dışına taşarsa `ClampToWalkable` kaymayı bozmaz: Sliding'de clamp kapalı, çıkışta 1 kez.

### 5.3 Tehdit rotaları

Her oyuncu-durulabilir bölgeye ≥ 2 giriş (REQ-111; S-05 oda kuralı sağlar). Spawn seçici oyuncunun bulunduğu odanın çıkışlarını **dönüşümlü** ağırlıklandırır (`DoorSideBalance`: son 3 doğuşun çıkış payı > %70 ise diğer çıkış(lar) ×1,5); oda içindeyken orta kapılar (34–62 m) ×1,2 (basınç), kenar kapılar ×0,9. Kanat açma (WP-054) odada ve K1'de kapalı, açık alanda aynı. Tehdit katmanı ↔ ekran içi çarpanı (0,35/0,35/0,20/0,10) korunur. Bot oda kuralı (S-05 (f)): 2 çıkışlı oda = halka düğümü; köprü yasağı (iki iniş 15 m içinde ≥ 6 zombi) `QaBotBrain`'e S-10 ile.

### 5.4 Büyütme etkisi ve simülasyon

±45 → ±55: ilk temas Standard +3,6 s (S-05 §1.2), 12 turda ≈ +45 s (%3). S-03 doğrulaması: greybox gelince `SpawnMixSimulator` v6 (2 m ızgara, 18 kapı, band 22–45): **S-05 B4/B5 eşikleri** (her ızgara noktasından ≥ 4 kapı 22–45 m ve ≥ 8 kapı ≤ 75 m, ihlal ≤ %3; ilk temas medyan Standard ≤ 20 s / Runner ≤ 12 s / Executioner ≤ 35 s). Kalkış payı (`OffScreenSpawnShare` ≥ 0,60) FP'de LOS kuralıyla ≥ 0,75 beklenir. Yedek ±50 tetiklenirse band 20–42, MaxUseful 70 (tablo değeri, tasarım aynı).

### 5.5 S-03'ün S-05'ten beklediği (greybox teslimiyle, §8.2)

`spawn-gates-v6.json` (18 kapı, eski→yeni ad eşlemesi, Kind kenar/orta), `regions-v6` poligonları (5 bölge + hizmet avlusu), oda-çıkış grafı veri dosyası (R1–R7: id, çıkış hacimleri, taban alanı), 8 darboğaz hacmi (merkez/boyut/genişlik), 12 halka düğüm listesi, NavMesh v5 bake (tek parça, 54,5), Ultra kapı kararı (§5.2), collider'lı greybox sahne (25–27.09) → S-03 spawn simülasyonu + S-10 bot soak.

---

## 6 · FP/TP farkındalık kancası (S-08 ses, S-09 HUD)

Veri **mevcut** `ThreatSnapshot` alanlarıyla: `RearDirection`, `RearIntensity`, `RearBearingDegrees`, `RearCount`, `RearAttack`. Eklenen (aynı yapı, aynı 0-ayırma döngüsü): `RearNearestDistance`, `RearNearestTier`, `RearNearestLifeId`, `RearNearestWindingUp`, `RearLevel` (enum None/Aware/Warn/Alert).

Sözleşme: S-01 WP-070 §7.1/§10 (f23f38e7) — `CameraView.IsRearBlind` (FP true), `CameraView.IsInView(pos, marginDeg)`, `CameraModeService.Current/Changed`. Bir beden "arka"dır ⇔ `!IsInView(göğüs, marginDeg)` (FP marj 0°, TP 10°) **ve** görüş ilerisine göre arka yarı düzlemde; servis yoksa açı eşiği (aşağıdaki `RearAngleDegrees`, mevcut hesap) yedek. Mod değişiminde `RearHold` sıfırlanır, tablo satırı değişir.

Mod başına eşikler (`ThreatSettings.RearByMode[3]`):

| | TopDown | TP | FP |
|---|---|---|---|
| `RearAngleDegrees` (yedek açı) | 100 (mevcut) | 110 | **75** (S-01 FP görüş 85° yatay: yarı 42,5° + 32,5° marj) |
| Aware (S-08 hafif nefes/ayak, HUD yok) | — | RearCount ≥ 1 ve mesafe ≤ 6 m | RearCount ≥ 1 ve ≤ 7 m |
| Warn (HUD yay, S-08 belirgin) | RearIntensity ≥ 0,40 | RearIntensity ≥ 0,25 veya (RearCount ≥ 2 ve ≤ 5 m) | RearIntensity ≥ 0,15 veya ≤ 4 m |
| Alert (HUD sert, S-08 stinger) | `RearAttack` (mevcut) | `RearAttack` | `RearAttack` **veya** arkadaki en yakın beden ≤ 2,2 m (BiteGrab bandı) |
| Histerezis | `FlagHoldSeconds` 0,6 | 0,6 | 0,45 (daha hızlı düşer; FP'de dönünce temizlenmeli) |

**Beden başına `RearThreat` olayı (S-01 A-7 → karar: kabul, beat yüzeyinde):** `EnemyCombatEventKind.RearThreat = 15` (`rear_threat` satırı; Position göğüs, Direction beden→oyuncu, Tier, LifeId, Seconds = tahmini saldırıya kalan süre). Yükselme: beden "arka" iken oyuncuya **6 m** içine girince (Special/Elite/Ultra **8 m** — hız/ağırlık payı); yaklaşma başına bir kez, 9 m'ye çıkınca ya da görüşe girince sıfırlanır; oyuncu dönüp bedeni arkada bırakırsa (arka'ya geçiş) beden ≤ 6 m ise **anında** yükselir. TopDown'da yükselmez (REQ-105 mevcut uyarılar yeter).

**Ön uyarı garantisi (S-01: ≥ 0,8 s saldırıdan önce → karar: kabul, mod başına tablo):** `RearWarnLeadSeconds` TopDown 0 / TP 0,5 / **FP 0,8**. Arka bedende `CanStartAttack` ek koşul: `now − _rearWarnedAt ≥ RearWarnLeadSeconds` (Windup'a girmiş saldırı kesilmez; slot tahtasıyla bağımsız). Mesafe doğal olarak çoğu durumda yeter (Special 8 m → menzil 2,6: 1,17 s + telegraph 0,30; Standard 6 m → 1,7: 1,6 s); kural yalnız "dönünce arkada kalan" bedende devreye girer. BiteGrab (arkadan tercihli) telegraph'ı Standard 0,72 s + lead 0,8 = en az 1,5 s okunur uyarı.

**Sessiz yaklaşma yasağı (S-01 → karar: kabul, S-08'e veri):** `rear_threat` beat'i tier taşır; Special/Elite/Ultra için S-08 hırıltıyı **bütçe kırpmasız** çalar (S-08 kuralı); Standard için S-08 kararı (öneri: 2'de 1). AI tarafı ek gizlilik/"sinsi" davranışı **eklemez** (yakın FP'de sessiz Runner = adaletsiz).

Denge izleme (WP-070 §7.3): telemetri `rear_threat_warned(mode, leadMs)` (S-03: beat → o bedenin `attack_hit` arası ms) ve `rear_hit_taken(mode, warned)` (S-03: `EnemyAttackLandedInfo` + beden `_rearWarnedAt` > 0). FP ölüm > 1,3× TopDown → lead 0,8 → 1,0 s, §5.1 tavan 35 → 25; hasar/telegraph/menzil **asla** (S-01 §7.2 adalet).

Toplu olay: `ThreatDirector.RearChanged(ThreatSnapshot)` yalnız `RearLevel` değişince (kare başına değil; `Changed` ile aynı desen). `ThreatHudFeed.RearLevel / RearNearestDistance` eklenir; S-09 kenar yayı yalnız `IsInView=false` bedenler için (S-01 §7.1), TopDown'da yok. Kişi başı konumsal ses: `rear_threat` + `telegraph_start` (konum + stil) beat'leri; S-08 arkadaki Windup'ı 3B konumdan çalar. Tepeden modda ses/HUD davranışı **değişmez** (mevcut REQ-105). `IsInView` çağrısı beden başına kare başına 1 (≤ 40; `NearRadius` içindekiler için) — ThreatDirector bütçesi §7'de.

---

## 7 · 0-ayırma ve kalite kademeleri

**0-ayırma (tüm kare yolu):** durum makineleri enum+float alanlar; seçici `stackalloc float[5]`; slot tahtası statik `int[5]/float[5]`; zincir hedef tamponu `DamageTarget[24]` statik; jel kayıt halkası `GelPuddle[24]`; string kimlikleri 20 girdilik statik tablo; ragdoll rigidbody/collider dizileri prefab'da (`ZombieBodyBuilder` bir kez); olaylar readonly struct (`Raised` tek invoke); Linecast/OverlapSphereNonAlloc. Ölçüm: `AllocationProbeTests` 3 faz (Kalabalık40 + 6 ragdoll + 2 birikinti + 1 zincir) **0 B/kare**.

| Özellik | Sinematik | Yüksek | Performans | Sahip / maliyet raporu |
|---|---|---|---|---|
| Aktif ragdoll (S-06 A3.6) | 12 | 6 | 2 | S-06 (fizik ms/ragdoll) |
| Ceset tavanı (S-06 A3.6 ile aynı sayı; despawn S-03 ledger) | 16 | 10 | 6 | S-03 ömür / S-06 görsel |
| Ceset ömrü çarpanı (§2.3) | ×1,25 | ×1,0 | ×0,6 | S-03 |
| Zincir hop | 10 | 8 | 6 | S-03 linecast + S-07 ark |
| Aktif birikinti | 24 | 16 | 10 | S-03 sorgu ≤ 0,02 ms/beden |
| Şok/kayma klip katmanı | tam | tam | `hit_gel_slide_loop` (ragdoll yok) | S-06 |

Kademe sözleşmesi: S-05 `QUALITY-TIERS-V2.md` (3812dc2) — `QualityTiers.Current / Changed / Pick(performans, yuksek, sinematik)` (`DD.Prototype.Core`); S-03 değerleri `DD_AiQuality` ScriptableObject'ten `Pick` ile okur, `Changed`'e abone olur, sabit kodlu kademe değeri 0.

### 7.1 Kalite kademesi satırı (S-05 §4.2'ye — S-03 WP-076)

| Özellik (S-03) | Performans (≤ 8,0 ms) | Yüksek (DEC-092) | Sinematik | A/B bayrağı |
|---|---|---|---|---|
| AI tick bütçesi (40 beden; seçici + slot + jel sorgusu + nav/crowd + tehdit, ragdoll hariç) | 0,55 ms | 0,75 ms | 0,85 ms | `-ddAiV3 0|1` (v3 seçici/slot/kapma kapalı → WP-040 davranışı) |
| Eşzamanlı sıcak ragdoll / aktif fizik cesedi (S-06 tavanıyla aynı; S-03 ölümde `Pick` ile okur, S-06 uygular; Sinematik 10 = S-05/S-06 uzlaşması, C-00 24.09) | 2 | 6 | 10 | `-ddRagdoll 0|1` (S-06 bayrağı; 0 = tam yazılı klip) |
| Ceset ömrü çarpanı (`CorpseLinger` × ; tavan 6 / 10 / 16) | ×0,6 | ×1,0 | ×1,25 | ~~`-ddCorpseLinger 0|1`~~ geri çekildi (C-00 30.09); kademe satırı uygulandı (`CrowdFeelSettings.CorpseCeilingFor(QualityTiers.Current)`) |
| Zincir yıldırım hop sayısı | 6 | 8 | 10 | `-ddChainHops 0|1` (0 = 4 hop, LOS yok) |
| Jel birikinti tavanı (kayıt halkası) | 10 | 16 | 24 | `-ddGelPuddles 0|1` (0 = 6, domino yok) |
| Kalabalık ayrışma komşu tavanı (`CrowdSpatialGrid` beden başına) | 6 | 10 | 14 | ~~`-ddCrowdNeighbours 0|1`~~ geri çekildi (C-00 30.09). **Kademe satırı uygulanmadı — KNOWN OPEN, R2'de perf ölçümüne bağlı** (bugün tek `CrowdTuning.MaxNeighborsPerMember`) |
| `PathQueriesPerTick` / spawn LOS linecast | 4 / 9 | 6 / 18 | 6 / 18 | ~~`-ddSpawnLos 0|1`~~ geri çekildi (C-00 30.09). **Kademe satırı uygulanmadı — KNOWN OPEN, R2'de perf ölçümüne bağlı** (bugün TP/FP'de nokta başına tek Linecast, kademeden bağımsız) |

C-00 30.09 (S-10 K4): A/B anahtarı yalnız `-ddAiV3 0|1` (AttackStyles.cs); `-ddChainHops` / `-ddGelPuddles` DEC-097/099 ile emekli. Ölçüm S-05 §5 protokolü: PilotPerfProbe Run, 1080p, 10 s ısınma + 60 s pencere, tam 40, `-ddCameraMode` üç mod, kademe × mod ≥ 2 pencere; rapor dilim B8.

**AI tarafı ms bütçesi (DEC-092: Yüksek ort ≤ 16,6 ms / p99 ≤ 20 ms kare; AI payı = ana iş parçacığı, 40 beden, 1080p referans Mac; M3 tabanı: nav+crowd 0,112 ms, ThreatDirector ≤ 0,05 ms):**

| Kalem | Sinematik | Yüksek | Performans | Ölçüm noktası |
|---|---|---|---|---|
| Zombi tick (durum makineleri, seçici, slot tahtası, jel sorgusu) 40 beden | 0,45 | 0,40 | 0,30 | `PilotPerfProbe` AI sayacı |
| Nav + crowd (WP-030/054, `PathQueriesPerTick` 6/4/4) | 0,20 | 0,15 | 0,12 | `RegionNavSliceTests.Stres_40Zombi` |
| ThreatDirector + `IsInView` ≤ 40 | 0,08 | 0,08 | 0,06 | `ThreatDirectorTests` |
| Ragdoll fiziği (S-06 A3.6 tahmini 0,09–0,13 ms/aktif, 11 beden) — **S-06 bütçesi**, burada bilgi | 10 × 0,11 ≈ 1,1 | 6 × 0,11 ≈ 0,7 | 2 × 0,11 ≈ 0,2 | S-06 ölçer |
| Zincir çözümü (bir atış: ≤ 10 linecast + sıralama) | 0,06 tepe | 0,05 | 0,04 | `ChainTargetTests` stopwatch |
| **S-03 AI toplam tavan (ragdoll hariç)** | **0,85 ms** | **0,75 ms** | **0,55 ms** | kademe raporu, dilim B8 |
| AI + ragdoll (bilgi) | ≈ 2,0 ms | ≈ 1,5 ms | ≈ 0,8 ms | |

Tavan aşılırsa sıra: hop ↓ → aktif birikinti ↓ → komşu tavanı ↓ → `PathQueriesPerTick` ↓ → (S-06) aktif ragdoll ↓; tasarım kuralları (DEC-029 pencereleri, slot tahtası, jel kaçınmama) kademeyle **değişmez**.

---

## 8 · Yayınlanmış sözleşmeler ve varsayımlar

### 8.1 Yayınlanmış sözleşmeler (bağlı; artık varsayım değil)

| Sahip · paket · commit | Bağlanan | S-03'ün beklediği kalan kalemler |
|---|---|---|
| **S-01 · WP-070** CameraMode · `f23f38e7` | `CameraModeService.Current/Changed(CameraModeChange)`, `CameraView.IsRearBlind`, `CameraView.IsInView(pos, marginDeg)`, `CameraView.Forward`; FP görüş 85° yatay; A-7 kararları §5.1/§6 (pay %35, `rear_threat` beat, lead 0,8 s, hırıltı verisi) | `IsInView` maliyeti (≤ 40 + 18 çağrı/kare); QaRunBot üç modda (`-ddCameraMode`) B8 kanıtı; `rear_threat_warned/rear_hit_taken` şemasının S-04 kaydı |
| **S-06 · WP-071** Animasyon v3 · `1980e0f` | 5 saldırı klibi + pencere kareleri (A4 → §1.1), olaylar `attack_recover / grab_start / grab_end / attack_step / roar / ground_impact / ragdoll_handoff` (A8.2), ölüm ailelerini S-06 seçer + A3.7 (§2), `hit_electro_loop / death_arc_electro / hit_gel_slip / hit_gel_slide_loop / death_gel_slip / getup_back` (§4), ragdoll/ceset tavanları A3.6 (§7), hit katmanları A5 | `SetDeathImpulse(in DeathImpulse)` (adlar bağlayıcı, §2.1); `KnockbackState` okuma + `z_hit_knockback_<tier>` bağlaması (§3.4 uzlaşıldı); `ZombieSpecialVisual.{Electrocuted, Sliding, Downed}` eşlemesi; `attack_step` kök adımını S-03 uygular (§3.4, uzlaşıldı); `grab_start/grab_end` kare onayı (13/21); WP-052 parite testi 6 satır |
| **S-05 · WP-074** Dünya v5 · `abc0f70` | ±55 oynanır / vista 55–85 / NavMesh 54,5 / tırmanma 0,30 / adacık bağlayıcı; R1–R7 oda-çıkış grafı; 8 darboğaz; 12 halka; 18 kapı önerisi → **§5.1 kararı: kabul**, band 22–45, MaxUseful 75; TP/FP "ekranda" = frustum + LOS | **Greybox veri dosyaları beklemede** (§8.2): `spawn-gates-v6.json`, `regions-v6`, oda/darboğaz/halka verisi, NavMesh v5 bake, collider'lı sahne (25–27.09); Ultra kapı kararı (§5.2) |
| **S-05 · WP-075** Kalite kademeleri v2 · `3812dc2` | `QualityTiers.Current / Changed / Pick`; Sinematik / Yüksek (DEC-092) / Performans (≤ 8,0 ms); §4.2 satırı → §7.1; A/B `-dd<Feature> 0|1` | `DD_AiQuality` ScriptableObject'in kalite modülü şablonuna uyumu; ölçüm protokolü §5 pencereleri |

### 8.2 Varsayımlar / sözleşme bekleyen kalemler

| Sahip · paket | Varsayım (S-03 buna göre kodlar) | S-03'ün beklediği |
|---|---|---|
| **S-05 · WP-074 greybox B1** (veri) | 18 kapı §3.4 koordinatları, oda/darboğaz ölçüleri Ek B'deki gibi | `spawn-gates-v6.json` (Kind kenar/orta, eski→yeni ad), `regions-v6`, oda-çıkış grafı dosyası (R1–R7 id/çıkış/alan), 8 darboğaz hacmi, 12 halka düğüm listesi, NavMesh v5, Ultra kapı kararı |
| **S-02 · WP-069 / ADR-069-1** | `DamageHit` +`WeaponClass` (gelene kadar S-06 ile ortak geçici enum `Unknown/Rifle/Smg/Shotgun/Lmg/Sniper/Launcher/Special`, `DeathImpulse.cs`); `DamageSourceKind.Gel` eklenir; DamageForce değerleri §3.1 tablosu (S-02 son sözü söyler, S-03 profil tablosuyla uyar); bir şelin saçmaları ≤ 3 kare, ayrı transaction | `ChainTargetResolver.Resolve` çağrı noktası (projectile ilk temas) ve hasar/gecikme sırası; jel `GelPuddleRegistry.Register/Expire` çağrıları + `Lifetime/Strength01/RoundScale`; `SlipDamage`'ın kimin uyguladığı (öneri: S-03 uygular, `Source=Gel`, S-02'nin `RoundScale`'i ile); eski Kagura/Ofuda/Tide kaldırma diliminin zamanı (S-03 `ZombieSpecialReactions` silme dilimi aynı pencerede); `EnemyAttackLandedInfo.Style/KnockbackScale` tüketimi (FP darbe) |
| **S-08 · WP-072** | `RearLevel` + konumsal beat'lerle yetinir; `roar/grab_start/grab_end` klip olaylarını doğrudan okur | `sw_shock/sw_slide/sw_fall` cue'ları; BiteGrab telegraph sesi; hırıltı bütçe kuralı (§6) |
| **S-09 · WP-068 R1** | `ThreatHudFeed.RearLevel/RearNearestDistance`; `GrabEvents` HUD istemi | FP/TP yay eşiklerinin HUD tarafı |
| **S-07 · WP-073** | beat türleri `Kind` ile bağlanır (tam sayı değil); `ground_impact`/`impact_land` S-06 olayından | `sw_*` yeni satırlar, gömülme/dissolve (S-06 ile), kapma kamera çekmesi |
| **S-02 · oyuncu tarafı** | `GrabEvents.Started/Ended` → hareket ×0,25 (nişan serbest) | tüketim onayı |
| **C-00** | DEC (C-00'dan istenecek), numara atanmadı: (a) `bite_grab` kapma kuralları (§1.4), (b) jel kaçınmama (§4.2), (c) ölüm darbesi bölünmesi + ceset ömrü tek sahip (§2) | numara + §2.3 ceset ömrü sahiplik kararı |

---

## 9 · Faz B dilim planı (her dilim = 1 commit + testi)

| Dilim | İçerik | Test (değişen süit, tek koşu) |
|---|---|---|
| B1 | `ZombieAttackStyle.BiteGrab` + `SwipeMirror`, `AttackStyleProfileSet` (kod varsayılanları; S-06 klip adları/pencereleri), `AttackSelector`, `AttackSlotBoard`, `attack_recover/attack_step` tüketimi, `GrabEvents` + kapma kuralları, 20'lik SourceId tablosu, `DD_AiQuality` + `-ddAiV3`; klip gelene kadar mevcut AP-009 klipleri (BiteGrab → swipe yer tutucu) | EditMode `AttackSelectorTests` (tier izinleri, üç kez aynı yasak, el değişimi, açı/hız BiteGrab kapısı, slot ret, kapma tavanı 2, determinizm), `GrabRulesTests` (%60/%40, kaçış ≥ 0,85, tek kapma), `TierExperienceTests` parite 6 satır; PlayMode `AttackVarietySliceTests` (6 Standard 20 s: Windup ≤ 4, Overhead tekil, dağılım ±%20, kapma sırasında Lunge 0) |
| B2 | `DD.Prototype.Damage.DeathImpulse` + sürücüye `SetDeathImpulse(in DeathImpulse)` (S-06 aileyi seçer; blok rig eski yol), `ZombieDeathStyle`/8-indeks seçiminin kaldırılması, `TickDying` hareketsiz + zemin bekçisi, ceset ömrü v2 (S-06 tablosu, ledger tek sahip, `Pick`), WP-061K/L muafiyet + `BodyState` | EditMode `DeathImpulseTests` (alan garantileri, sınıflandırıcı → `WeaponClass`, `State`), `OutOfWorldTests` (ceset düşüşü = sayaçsız despawn, kaldırılan beden `SetDeathImpulse` almaz); PlayMode `CorpseLifetimeSliceTests` (40 tavan altında 16→10→6, Ultra tabanı, despawn = tek olay) |
| B3 | `HitReactionProfile[WeaponClass]`, sınıflandırıcı, LMG suppression, sniper ağır sendeleme kancası, launcher dikey, `KnockbackProfile` (S-06 mesafe/süre verisi) + `KnockbackState` okuma yüzeyi (§3.4 uzlaşıldı) | EditMode `HitReactionContractTests` genişler (kararlı durum tablosu §3.1; Ultra asla; governor çift şel; 0,6 m/0,25 s profili); PlayMode `ProjectileReactionSliceTests` (S-02 projectile geldiyse gerçek, yoksa `ApplyHit` doğrudan) |
| B4 | Eski Special tepkilerinin silinmesi + `sw_shock/slide/fall` beat'leri + `Electrocuted` durumu + `ChainTargetResolver` | EditMode `ChainTargetTests` (N/yarıçap/hop/LOS/Ultra sonlandırır/ceset dışlanır/0 B), `SpecialReactionTests` yeniden; PlayMode `ShockSliceTests` (ölüm ertelemesi: ödül anında, ragdoll 0,6 s sonra) |
| B5 | `GelPuddleRegistry` + Sliding/Falling/Downed/GettingUp + domino + SlipDamage | EditMode `GelStateTests` (tablo geçişleri, tier olasılıkları deterministik, döngü, şok önceliği); PlayMode `GelSliceTests` (8 Standard koridorda birikintiye: ≥ 6 düşer, domino ≥ 1, kaldırma 0) |
| B6 | Spawn v6: 18 kapı (`spawn-gates-v6.json`, `Kind` kenar/orta), band 22–45 / MaxUseful 75, tier yol kapısı ≤ 55 m, TP/FP frustum+LOS "ekranda", kamera modu tablosu, `Room` bölge türü + darboğaz hacimleri (`regions-v6`), kapı dengesi, `SpawnMixSimulator` v6 (2 m ızgara) | EditMode `SpawnSelectionContractTests` (+mod tablosu, orta kapı 4 s / ≤ 10 m ×0,25, tier yol kapısı), `RegionNavProfileTests` (Room kapasite, C1–C8), `SpawnGateCoverageTests` (S-05 B4/B5: ≥ 4 kapı 22–45, ≥ 8 ≤ 75, ihlal ≤ %3; ilk temas ≤ 20/12/35 s); PlayMode `SpawnTuningSliceTests` (greybox: FP arka 18 m ret, LOS duvar arkası OffScreen, OffScreenShare ≥ 0,75) |
| B7 | `RearLevel` + mod eşikleri + `RearChanged` + `rear_threat` beat + `RearWarnLeadSeconds` kapısı + arka doğuş payı tavanı + HUD/ses feed alanları | EditMode `ThreatDirectorTests` (mod tablosu, histerezis, Alert ≤ 2,2 m FP, pay penceresi 8), `AttackSelectorTests` (+lead kapısı); PlayMode `RearAwarenessSliceTests` (FP: arkadan yaklaşan Standard: `rear_threat` 6 m'de bir kez → Aware→Warn→Alert, ilk `attack_hit` ≥ 0,8 s sonra; dönünce 0,45 s'de temizlenir; Special 8 m'de beat; 24 doğuşta arka payı ≤ %35) |
| B8 | Kanıt: `QaRunBot` r1–r12 üç kamera modu (S-01 bot desteğiyle; yoksa TopDown), `-ddQaBotSpecial` iki yeni Special, `AllocationProbeTests` 3 faz, perf tablosu kademe başına | bot özeti: bot.rounds ≥ 4, kaldırma 0, ragdoll ≤ kademe, jel/zincir beat sayıları, `gcAlloc/kare 0` |

Perf (§4.8, DEC-092): her dilim kendi ms maliyetini ölçer ve kademeye bağlar; hedef Yüksek kademe (ort ≤ 16,6 ms / p99 ≤ 20 ms), Performans kademesi ≤ 8,0 ms ayrıca raporlanır.

## 10 · Kabul ölçütleri (O1–O7 eşlemesi)

| # | Ölçüt | Eşik |
|---|---|---|
| K1 | 5 stil, her tier ≥ 2 stil, üç kez aynı stil 0 (bot r1–r12 telemetri) | 0 ihlal |
| K2 | Eşzamanlı Windup ≤ 4 (kapma aktifken 2), Overhead/BiteGrab/Sweep/Lunge tekil, oyuncuda kapma ≤ 1 | 0 ihlal |
| K3 | Ölüm: her ölümde `DeathImpulse` tam (alan garantileri); S-06 aile dağılımında 4 ailenin her biri r1–r12 botunda ≥ %10 (S-06 ile ortak kanıt) | tablo |
| K4 | Kaldırılan beden `SetDeathImpulse`/ödül 0; ceset düşüşü kaldırma sayacına 0; ölümden sonra S-03 transform yazımı 0 | 0 |
| K10 | Spawn v6: S-05 B4/B5 eşikleri (kapsama, ilk temas), kenar/orta payı 65/35 ± 10 | PASS |
| K5 | Zincir: hop ≤ kademe, Ultra iletmez, LOS ihlali 0 | 0 |
| K6 | Jel: kaçınma yok (rota sapması 0), r1–r12 jel öldürme payı ≤ %60 | bant |
| K7 | FP arka uyarı: `rear_threat` → `attack_hit` lead ≥ 0,8 s (%100), Alert ≤ 2,2 m, yanlış pozitif (önde) 0, arka doğuş payı ≤ %35 (pencere 8) | 0 ihlal |
| K8 | GC 0 B/kare, 40 beden + 6 ragdoll + 2 birikinti + 1 zincir | 0 B |
| K9 | ±%20 parametre oynatması (telegraph, slot, SlipFraction, hop) sistem kırılmaz (EditMode duyarlılık testi) | PASS |
