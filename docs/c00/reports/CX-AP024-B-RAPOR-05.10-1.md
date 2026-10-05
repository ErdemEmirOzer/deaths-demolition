# CX-AP024-B RAPOR 1 — 05.10.2026
## ÖZET (≤10 satır)
Durum: KISMİ — kullanıcı kullanım limiti nedeniyle hemen bitirme ve rapor istedi.
Üretildi: rafine O1+3değer seçeneği, düzeltilmiş5profil Blender model/rig,3adet2048² PBR harita.
Son üçgenler: LOD0/1/2/3=34144/17802/7860/2958; FP8229, tüm bütçeler içinde.
Eksik: yeni5FBX export/roundtrip, düzeltilmiş120kare render, son12silahın11'i ve10frame son denetimi.
Eski5FBX ve120render korunur; son bilek geometrisinin kanıtı değildir.
Teslim: AP-024/; manifest/AP024-manifest-v003.json + SHA256SUMS.
G1b renk kararı açık; V1+koyu omuz geçici öneridir. Üretime hazır kabulü verilmez.
Repo/Unity/Player değiştirilmedi. Mekanik doğrulama sonucu aşağıda.

## 1. Yapılanlar
O1 Kırık Mühür için turnaround, maske, FP tutuş/reload, üstten görünüm ve3renk/değer varyantı üretildi. Higgsfield kaynak görseli/Meshy GLB temizlendi;13delik kapatılarak59602tri high kaynak, otomatikLOD'lar ve ayrıFP kollar elde edildi. Elle sculpt/quadretopoloji iddiası yoktur. UniqueUV ve high→low BaseColor/Normal/MR ile AO bake yapıldı.

AP026 default77 iskelet,30parmak,8yan dal twist,7secondary, anatomikREST ve soketler kuruldu. İlk FBX adayları gerçek reimport ile doğrulandı. Son incelemede el/önkol silah kesişmesi ve parmak ağırlıklı seramik katlanması tespit edilip bu adayların kabulü geri açıldı. Bilekte26mm gerçek kumaş aralığı, iki yanda5mmgeçiş ve2,5mm içe alma; Hand/LowerArm sert seramik bölgeleri üretildi. LOD3 merkez halkasıyla2958tri içinde kaldı.

Yeni v006 üçharita yalnız16949pikselde (%0,404) değişir; maske dışı değişen piksel0. İlk yanlış globalmaske adayı reddedilip korundu. Son AR1 tutuş/reload fiziksel kalibrasyonu hazır; diğer11silahın yeni kontrolleri bitmedi. Kullanıcının son talebiyle sıradaki uzun işler kapatıldı.

## 2. Üretilen dosyalar
Paket kökü: `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-024`.

| Son dosya | Boyut | SHA256 kısa |
|---|---:|---|
| `05-blender/hero_rig_wrist_atlas_v014.blend` | 31452830 B | 5edff4124877 |
| `05-blender/hero_rig_poses_final_v002.blend` | 32098050 B | 80e8dc5eee10 |
| `exports/v001/Textures/Hero_v2_BaseColor.png` | 5536206 B | 5d43c3e615f5 |
| `exports/v001/Textures/Hero_v2_Normal.png` | 5216228 B | d90f28083061 |
| `exports/v001/Textures/Hero_v2_ORM.png` | 4858394 B | 4d71792da7d0 |

Konsept: `02-concept/refined-v001/`. Son dokular: `exports/v001/Textures/`. **Bu klasörde yeni5FBX yoktur.** Önceki adaylar `validation/b-v001/pre-wrist-final-candidate-v001/exports/v001/`;120kare30fps dönüş ve50°/TP/FP kanıtı `validation/b-v001/pre-wrist-render-candidate-v001/` içinde. Bunlar açıkça önceki geometri/poza aittir. Tam SHA ve roller manifestte yer alır.

## 3. Kalite ve doğrulama
Son kaynak üçgen bütçeleri PASS:34144≤35000,17802≤18000,7860≤8000,2958≤3000,FP8229≤12000. Tam gövdelerde açık/nonmanifold/zeroarea0;FP119kasıtlı omuz kesiti. Max4normalize etki; kaynak toplam hata≤4,47e−8. Kaynak kemik ad/parent77korunur. Kanıt: `validation/hero_rig_wrist_v013.json`, `hero_rig_wrist_lod3_v015.json`, `hero_rig_wrist_material_binding_v014.json`. Son geometri yeniFBX'ten geri aktarılmadığı için exportPASS genellenmez.

ÖncekiFBX reimport:77ad/parent birebir, TP/FP importedbind fark0, thumbREST fiziksel drift≤0,000445mm ve açı≤0,000063°. Ham source→reimport inversebind fark2,84016e−5 sıfır değildir. Bu ölçümler eski adaylar içindir (`hero_rig_final_clean_fbx_roundtrip.json`, `hero_rig_final_validation_v003.json`).

UniqueUV başlangıç2048raster testinde0örtüşme,1538ada,10–11pxgutter,medyan772,263px/m. Sonbilek kesimleri UVinterpolasyonunu korur; subpixel çakışmasızlık garantisi yok. V006 üçatlas2048²,1materyal/1atlas; dışmaskede0piksel değişimi. Kumaş kanalı AO159–255,roughness214/255,metal0;36düşük-Z normaltexeli nötrleştirildi. BaseColor sRGB;Normal/ORMlinear etiketli,IDATkorundu. Kanıt `validation/model-wrist-material-v006/` ve `validation/b-v001/export-texture-tags.json`.

SonAR1: anatomikönkol/el sırtı bölgelerinde tutuş+reload yüzey kesişmesi0; gerçek temas yüzeyi aralıkları yaklaşık0,172/0,068mm,eldeşarjör0,089mm. Dipçik/omuz3,146mm,yanak10,496mm. El/socket→authoredmarker offsets fiziksel kalibrasyondur, IKerror değildir; AP021marker değiştirilmedi. Kanıt `validation/hero_rig_grip_v014_ar_1.json`. **Diğer11silah, tam parmak yüzeyleri, yeni materyalli yakınplan ve son10frame×5mesh kabulü tamamlanmadı.** Küçük açısal solbilek kumaş kıvrımı finalPBRkontrolü bekler.

KonseptV1 torso100/64 C=0,1835/0,1812 PASS. Önceki3B koyuomuz torso100/64 C=0,077534/0,077741 PASS; bu eski renderdır. Yöntem encoded-sRGBRec709 C=|Lp−Lg|/(Lp+Lg)≥0,06. Aynı eski3B lineer mutlakΔY=0,020252/0,017836; bunun için0,06PASS denmez. Sonpose yeniden ölçülmedi. Unity DEC077 ölçülmedi.

Secondary7kemikten sec_sash1–3 ağırlıklı; birleşikpantolon relief yüzeyi, serbestkumaşsimülasyonu değil. sec_band1/2,pouch,gourd geometri olmadığından unweighted. Facial/jaw ve runtime hareket kabulü yok. ÖncekiLOD2/3 crouch kenar uzama tanıları49,353/33,472mm; songeometri için yenidenölçüm bitmedi.

<!-- VALIDATOR_RESULT -->
Tek mekanik dd_validator sonucu ekleniyor.
<!-- /VALIDATOR_RESULT -->

## 4. Kaynaklar ve lisanslar
`03-sources/LICENSES.csv` günceldir. Higgsfield/Meshy üretimleri [Higgsfield koşulları](https://higgsfield.ai/terms-of-use-agreement) §4.4 ve [ticari kullanım açıklaması](https://higgsfield.ai/creator-hub/help-center/account/who-owns-my-generations-and-can-i-use-them-commercially); konseptler [OpenAI çıktı sahipliği koşulları](https://openai.com/policies/terms-of-use/) kapsamında kaydedildi. CC0/münhasır telif garantisi verilmez.12AP021silah mevcut proje kaynağıdır; HeroFBX'e dahil edilmedi. Zemin özgün prosedürelBlender üretimidir.

## 5. Entegrasyon notları
**Son kaynak henüz doğrudan Unity teslimi değildir.** Önce yeni5FBX export+roundtrip tamamlanmalı. Sonra S-06/S-02 WP-082 yeniHumanoidAvatar kurar; eskiAP022Avatar kopyalanmaz. FBX7400,metre,−ZForward/YUp,no leaf,max4weights; gerçekUnityekseni kontrol edilir. Default77listesi exportREADME'de.

`socket_weapon_r` RightHand; `socket_mag_l` LeftHand doğrudan çocuğu; `mag_socket` identityalias; `grip_l` silahdomainmarker. Kaynakoffsetler BlenderZup/−Yforward uzayındadır; Unity'ye ham kopyalanmaz. RuntimeIK, equip/cancel/reset, reloadcommit, sameframebridge,magazineevents ve çarpışmasızanimasyon süpürmesi ölçülmedi.

ORM R=AO,Groughness,Bmetallic;URPLit smoothness=1−G. BaseColor sRGB,NormalY+,ORMlinear. FP ortakmaterial/bind. Kaynakpose AP021dışdosyalarına referans verir.

## 6. C-00'A SORULAR / 🔴 KARARLAR
G1b finalrenk: V1+koyuomuz / V2 / V3; öneriV1+koyuomuz. `01-design/AP024-B-G1b.md` ve rafinecontactsheet. Bekleyen iş finalrenkkabulü; kullanıcının seçimi varsayılmadı. Kaynak paket kısmiolduğu için WP-082 import öncesi aşağıdaki tamamlama işleri gereklidir.

## 7. Araç / Higgsfield istekleri
4imagegenkonsept;1başarılıHiggsfieldkaynakgörseli (`9fe634c1-ab22-4861-b8a7-f96d7df30ec4`) ve1Meshy3D (`09840121-8c65-4b99-b0e5-b54456a2b4ca`). İlkHTTP503sonrası işgeçmişi kontrol edilip1tekrar. Requestednano_banana_2/returnednano_banana_flash farkı provenance'da. Ledger submitted/completed aynıIDolaylarıdır; kredi **not_observed**.

Blender5.2.0LTS,threads6,tek slot,disk≥15GiB/swap≤12GiB. Kaynak sınır üstündeyken işler başlamadı; son işler slotbekliyordu. Exportbirkez otomatikincelemede olasıoverwrite/yedekbelirsizliği nedeniyle reddedildi; yedekSHAeşitliği+boşhedef+overwriteguard doğrulanınca güvenlitekrar onaylandı fakat kullanıcı kapanışında henüz çalışmamıştı. Kurulum/satınalma/login/gizliveri yok. Üretimajanları dd_concept_artist/dd_3d_modeller/dd_technical_rigger; estetikliderde,tek mekanikvalidator.

## 8. Sonraki önerilen çalışma
1. `hero_rig_wrist_atlas_v014.blend` üzerinden5FBX export ve AP026roundtrip.
2. AR1 dışındaki11silahın yeni fiziksel tutuş/reloadkontrolleri; son10frame×5meshdeformtesti.
3. Frozenstage vev006atlas ile50°/TP/FP,120kare30fps,yeni100/64pxdeğerkanıtı.
4. Son görselkabul/G1bkararı ardından S-06/S-02 WP-082 ve S-05 DEC077.

Önceki adaylar, reddedilen teşhisler ve kaynaklar silinmedi. Bu rapor tamamlanmamış ölçütleri kapatmaz.
