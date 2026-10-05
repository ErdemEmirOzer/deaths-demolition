# S-09 · WP-081 devir notu — 2026-10-05 (hesap devri)

## Dal ve SHA'lar

- Worktree: `K/wp-068/worktree/deaths-demolition` (Library'li).
- Dal: **wp-081-v4a-r2**. Son SHA **4895ac1d**. Çalışma ağacı temiz.
- Main'de (C-00 entegre etti):
  - 7c85a9da → d8ca7089 (rezerv renk düzeltmesi)
  - 4f359358 → 137637a5 (R2 U1–U3 kaynak assert'leri)
- Dalda, entegre edilmemiş commit'ler (eskiden yeniye):

| SHA | İçerik | Durum |
|---|---|---|
| **bcde16dc** | Sinematik menü v1: dünya, kamera, WorldViewer, perde bekleme | dry-run PASS. C-00 karar gelene kadar bekletti; aşağıdaki B ile tek teslim. |
| **8ba256ec** | U1 iki ölçüm (u1_default 0,422 PASS; u1_same_content 0,131 tasarım gereği kırmızı) | dry-run PASS, cherry-pick. C-00'a bildirilmedi; bu notla bildiriliyor. |
| **a1f50b99** | **WIP** — B + yastıklar (C-00 kararı), yumuşak dünya perdesi, S04 yerel uyarlaması, ölçüm araçları | Kod test edildi (b6: EM 41/41, PM dünya 1/1). Kanıt eksik. |
| **4895ac1d** | **WIP** — ikon v2 iskeleti (eski tasarım) | AP-028-B'ye göre yeniden yazılacak. |

## Yapılanlar (bu oturum)

- **v4 Faz A:** main'de.
- **Sinematik menü (onaylı 5 çekim):** dünya menünün altında yalnız görüntü olarak.
  - Görünüm C-00 kararı **B + yastıklar**: sol üçte bir koyu, dünya tam görünür.
  - Yastıklar: odak kelimesi + alt satır, LEVEL, dev build satırı.
- **Seçenek kanıtı:** `K/wp-068s9/evidence/wp081/look5/OPTIONS.md` (A–EC, gerçek kareler).
- **Perde rampası hatası düzeltildi:** cover-fit rampayı kırpıyordu (4:3 ve Mac'te sert kenar). Regresyon testi var.
- **S04:**
  - Sorun: tasarım pozu V6'da salonun perdesine bakıyordu, kare simsiyahtı.
  - Yerel uyarlama (C-00 onaylı kriterler): eşikte dolly, min ortalama luminans 0,087 ≥ 0,069.
  - Kanıt: `look6/S04-candidates/README.md`.

## Kalanlar — sıradaki komutlar

1. **B teslim kanıtı (C-00 şartı: 5 oran + 60 s MP4):**
   - Dev build: `cd K/wp-068/worktree/deaths-demolition && tools/build.sh dev --out K/wp-068s9/builds/b4-dev`.
     b4-dev bu notu yazarken derleniyordu; tamamlandıysa kullan.
   - Oranlar: `NOREF=1 K/wp-068s9/tools/cine-player.sh look6 <app> frames`
   - MP4: `NOREF=1 K/wp-068s9/tools/cine-player.sh look6mp4 <app> mp4`
   - Ölçüm (kriterler: p99 FAIL 0, kenar ≤ 1,5×):
     - kontrast: `python3 K/wp-068s9/tools/cine-contrast.py <klasör>`
     - kenar: `python3 K/wp-068s9/tools/cine-edge.py <now ref> <B klasörü>`
     - now referansları: 1080 → `look4/now`; 16:10 / 21:9 → `cine1/16x10`, `cine1/21x9`; 4:3 / Mac → `bfinal/now-4x3`, `bfinal/now-tr-mac`.
   - Beklenti: kontrast her oranda FAIL 0 (bfinal2'de 21:9 hariç hepsi 0'dı; 21:9 kayan yastık ölçüm düzeltmesiyle kapanmalı). 4:3 ve Mac kenarı cover-fit düzeltmesiyle ≤ 1,5× olmalı.
   - Sonra: a1f50b99'u WIP olmayan bir commit'le tamamla (amend yok). Konu "(C-00 dd.mm)", gövde Test:/Guarded:. bcde16dc + 8ba256ec + yeni commit → C-00 (integrate.sh --repo "/Users/Erdem/Documents/Deaths Demolition").
2. **Maliyet (ms):** sessiz pencerede ölç (C-00: S-10 FINAL'e kadar).
   - Komut: `K/wp-068s9/tools/cine-player.sh <label> <app> perf` → `perf-world` ve `perf-plates`.
   - Şimdiye kadar yüklü makinede: menü dünyası SetPass 142 / batch ~3.200 / draw ~4.570, plakalı menü SetPass 19. GPU 40 ms ortalama ölçüldü ama yük 50–77 — güvenilmez.
3. **İkon v2 (C-00 sırası 3):** AP-028-B (`K/asset-production/AP-028/exports/v002/Icons`).
   - G1 birleştirme: `K/wp-068s9/tools/merge_o1_g1.py <G1 klasörü> <çıktı>`. Taslak çıktı `devir-05.10/g1-o1/`: 29 değiştirilen, 29 eklenen anahtar; HUD glifleri de değişiyor — C-00'a söyle. Sonra repodaki `UI/Art/Resources/UI/G1/`'e kopyala.
   - Malzeme ve hareket planı: 4895ac1d gövdesinde. Taslak `devir-05.10/icon/UiIconMotion.cs`, durum bağlama taslağı `iconstate.txt`.
4. **Yükleme alımı (C-00 sırası 4):**
   - Kaynak: `K/asset-production/AP-028/exports/v002-upscale/Loading/` (5 master, C-00 HAZIR).
   - Komut: `tools/wallpaper/wallpaper_intake.py --text-safe` (q90) + LoadingKeyArtTests.
5. **Seçim ve paneller v4 (U1b):** AP-028-B ekran taslakları `exports/v002/Screens/`.

## Açık ölçümler

- B'nin 5 oran kanıtı ve MP4'ü (1. madde).
- Maliyet ms (2. madde).
- U1b (u1_same_content ≥ 0,40): Seçim ve paneller v4 olunca.

## Guard / koşu durumu

- Bütün guard pencereleri kapandı (son: b6 verified=1 outOfScopeChanged=0).
- Açık Player koşusu yok.
- Arka planda yalnız `tools/build.sh dev --out K/wp-068s9/builds/b4-dev` (pid 7903) kendi başına bitiyor. Guard'sız; slotunu kendi bırakır.

## Dersler

`memory/ders-player-yakalama-ve-olcum.md`:
- yakalamada giriş modüllerini kapat;
- .NET `"{n:F2}}}"` biçim hatası;
- küçük metne underlay mürekkep;
- inkli metni çizildiği gibi ölç;
- integrate.sh --repo.
