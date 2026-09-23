# WP-067A — AP Source Freeze & Backup (PASS)

S-01, koşu `5088627-20260923T1939Z` (taban main `5088627`), 2026-09-23T19:39:48Z → 2026-09-23T19:53:38Z (13 dk). Makine-okunur kayıt: `WP-067A-AP-SOURCE-FREEZE.json`. Varlık kopyası repoda yok.

| Ölçü | Değer |
|---|---|
| Kaynak | `~/Desktop/KADRO/context/projects/deaths-demolition/asset-production` |
| Kanonik dosya / bayt | 15 457 / 60 259 050 326 B (60.26 GB) |
| Dışlanan | `.DS_Store`, `AP-020/.DS_Store` (kurallar: tam ad `.DS_Store`; sonek ~, .tmp, .temp, .bak, .backup, .swp, .autosave) |
| **Kök digest** | `a44d767c63444ef6e0c568f7689605bb373ea6023263c567faf085096ff8eee2` |
| Digest tanımı | sha256 over UTF-8 lines '<sha256>  <size>  <rel>\n' sorted by UTF-8 rel path |
| Dış manifest | `~/Desktop/KADRO/context/projects/deaths-demolition/wp-067a/run-5088627-20260923T1939Z/manifest-start.tsv` sha256 `1f938b15f8436ad8d0a0a814b45a2604456d3429f733fd167e0a85b41e24bf4f` |
| Yedek hedefi | `~/Backups/DeathsDemolition/WP-067A/AP-source-freeze-5088627-20260923T1939Z` — clonefile(2) per canonical file, no fallback, 3 s |
| Bağımsızlık | logical-independent-apfs-cow (same APFS volume/device; NOT hardware-failure independent) |
| Readback | eşit = **True** (eksik 0, fazla 0, fark 0; digest `a44d767c63444ef6e0c568f7689605bb373ea6023263c567faf085096ff8eee2`; 14 s) |
| Restore tatbikatı | 8/8 ok — plain byte copy from backup (copyfileobj), not clonefile |
| Kaynak kararlılığı | eşit = **True** (son digest `a44d767c63444ef6e0c568f7689605bb373ea6023263c567faf085096ff8eee2`; 14 s) |

Readback bir APFS klonunu okur: mantıksal kopyanın doğruluğunu kanıtlar, fiziksel blokları kaynakla paylaşır (donanım arızasına karşı bağımsız değil). **Harici disk yedeği ayrı kullanıcı işidir.**

## AP başına

| Kök | Dosya | Bayt | Digest |
|---|---:|---:|---|
| AP-001 | 3157 | 19 680 787 397 | `f190f14e5293de88…` |
| AP-002 | 980 | 9 213 897 098 | `284251b7924f1671…` |
| AP-003 | 1934 | 17 538 498 377 | `5e8478133f6ea9c5…` |
| AP-003B | 473 | 3 705 154 063 | `45cec62d53619435…` |
| AP-005 | 375 | 3 100 532 992 | `ac0ff037fecab485…` |
| AP-006 | 232 | 567 873 118 | `60a20ff480594c66…` |
| AP-007 | 456 | 927 895 122 | `d9082046b681f1f6…` |
| AP-008 | 540 | 742 862 159 | `2b8eb1911212c503…` |
| AP-009 | 4122 | 1 052 046 593 | `a9208b3fb42b9e6c…` |
| AP-010 | 1333 | 224 066 005 | `5a82db803952d357…` |
| AP-011 | 599 | 321 034 922 | `3e6d56e93780f6ae…` |
| AP-011B | 233 | 70 743 750 | `256c5df46284654b…` |
| AP-012 | 38 | 53 245 050 | `b10a667c2419df94…` |
| AP-013 | 100 | 67 410 126 | `2c5e3d6646ed3f9c…` |
| AP-014 | 213 | 444 323 195 | `a4c2e360441fd402…` |
| AP-015 | 197 | 206 707 809 | `e677b35bed396462…` |
| AP-017 | 329 | 962 954 794 | `2b7115ed98dd12e5…` |
| AP-018 | 24 | 6 957 102 | `2e9856b891453bc2…` |
| AP-019 | 25 | 33 509 271 | `c96f10f0acd33140…` |
| AP-020 | 95 | 45 242 657 | `bf4e9c703c979bc3…` |
| showcase | 2 | 1 293 308 726 | `149f8ffb6cdf2b8d…` |

## Restore tatbikatı

| Dosya | Bayt | SHA-256 eşit |
|---|---:|---|
| `AP-003/05-blender/validation/AP003-roundtrip-runner-v007.out` | 39 | True |
| `AP-001/05-blender/validation/v009/GoldenSlice_fbx/GoldenSlice.fbx` | 943 442 188 | True |
| `AP-010/05-blender/wave2/frames/water_splash/meta.json` | 393 | True |
| `AP-001/04-adobe/source/AP001-material-bark-v001/bark-studio.blend` | 20 020 688 | True |
| `AP-006/06-renders/AP006-rigtest-walk-08.png` | 1 513 813 | True |
| `AP-011/exports/ImpactDirt_01.wav` | 117 788 | True |
| `AP-013/06-renders/in-hand/contact/AP009_player_B_switch_in_f02.png` | 342 226 | True |
| `AP-017/06-renders/gameplay/AP017-p2b-south-ponds-eevee-v007.png` | 3 019 149 | True |

## Notlar

- 22.09 Codex preflight'ına göre (15 376 dosya / 60 114 747 388 B, BLOCKED — tam SHA-256 zinciri başlatılamamıştı) kaynak büyüdü: 23.09 kanonik 15 457 dosya (AP-017 Faz 2b vb.). Dışlama kuralları aynı; yalnız 2 `.DS_Store` dışlandı; `.blend1` dosyaları (24) kanonik. Bu kayıt BLOCKED raporunun yerini alır.
- Hash aşamaları S-10 WP-064B soak'ı bitene kadar bekledi (780 s); her aşama öncesi `soak.sh`/`rc.sh`/`regression.sh` yokluğu denetlendi. Kaynak üç kez tam okundu (başlangıç, readback, son), ~180 GB.
- Araçlar ve ham kanıt repo dışında: `wp-067a/tools/freeze.py`, `publish.py`; `wp-067a/run-<koşu>/` (`summary.json`, `manifest-start.tsv`, `manifest-readback.tsv`, `backup.log`); restore kopyaları `wp-067a/restore-drill/<koşu>/`.
