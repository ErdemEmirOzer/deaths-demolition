#!/usr/bin/env python3
"""Inspect source-bound WP-078 evidence; never execute a gate or a game."""
import argparse
import hashlib
import importlib.util
import json
import math
import os
import re
import struct
import sys
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCHEMA = "dd-wp078-acceptance-v1"
AREAS = "projectile penetration launcher arsenal gel chain ragdoll zombieAttacks animation camera world aiFlags progression audio ui readability".split()
MODES = {"topdown": "topdown", "TopDown": "topdown", "tp": "tp", "ThirdPerson": "tp", "fp": "fp", "FirstPerson": "fp"}
SHA = re.compile(r"[0-9a-f]{40}")
DIGEST = re.compile(r"[0-9a-f]{64}")

# Exact existing owner-suite test identities traced to the current A3 matrix.
# These are native NUnit method identities, never caller-provided area labels.
A3_REQUIRED_TEST_IDS = {
    "projectile": [
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder",
        "DD.Prototype.Tests.PlayMode.ProjectileSliceTests.HizliMermi_InceDuvardan_Gecemez_TekUzunAdimdaBile",
        "DD.Prototype.Tests.PlayMode.ProjectileSliceTests.KlasikSilah_Projectile_Atar_VeHedefiVurur",
        "DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermi_UcusSuresindenSonraHasarVerir_TekIsabet",
        "DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermiler_UcarkenVeCarparken_KareBasinaAyirmaYok"
    ],
    "penetration": [
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitliCollider_RegistryOnce",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitsizCollider_AcikFallback",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventClassifier_OnSinifTablosu",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventMeta_BeforeAfterVeKirilim",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageTransaction_MonotonVeIcIceSayilir",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.IsEmpty_YalnizHicbirSeyOlmadiginda",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.KaynakTaramasi_HasarYolundaKareBasinaAramaYok",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kirilma_Idempotent_IkinciCagriKirmaz",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kritik_YalnizAcikKafaDogrudan_PatlamadaYok",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.OluBeden_IkinciOlumVeKirilmaYok",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Parca_YalnizKendiBolgesiniKorur",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_EnjekteOranlar_ToplamKorunurDeterministik",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_IkiParca_IkiPayZirhaAcikPayCana",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_KirilmaTasmasi_AyniPatlamadaCanaGecmez",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_TekParca_KorunanPayZirhaAcikPaylarCana",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_UcParca_SonraBiriKirik_AcikPayCana",
        "DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Tasma_HerSlottaCanaGecmez",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.AyniTransaction_IkinciUygulama_Reddedilir",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.DogrudanIsabet_GercekHitbox_BolgeSlotYonlendirmesi",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.IkinciRunSahnesi_HasarAboneleriCogalmaz",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Kompozisyon0123_Deterministik_HavuzIkinciYasam_Pause",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.OluDusman_GecHasar_YeniOlumGoldKillYok_StandardKill",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_AyriCanYoluYok_BirincilHedefTekOlay",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_DusmanBasinaTekUygulama_TekDressing_ButceCogalmaz",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.PatlamaylaOlum_TekOlum_KritikDegil",
        "DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.VurusYolu_SifirAyirma"
    ],
    "launcher": [
        "DD.Prototype.Tests.EditMode.RocketMathTests.Patlama_DogrusalDusus_VeKendineHasar",
        "DD.Prototype.Tests.EditMode.RocketMathTests.Rl1_Etkin_RoketBlogu_VeDallari",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.Cluster_Bombaciklari_PatlamaEtrafinda",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.DogrudanIsabet_VePatlama_AyriIslemde_HedefIkisiniDeAlir",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.Kontrolcu_Rl1_RoketAtar",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.KurmaMesafesiIcinde_Dud_30Hasar_PatlamaYok",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.Oyuncu_KendiPatlamasindan_Yuzde45_Alir",
        "DD.Prototype.Tests.PlayMode.RocketSliceTests.Ucus_Itki_VeHavadaPatlama_70m"
    ],
    "arsenal": [
        "DD.Prototype.Tests.EditMode.ArsenalB2Tests.Burst_UcAtis_005Aralik_SonraGecikme026",
        "DD.Prototype.Tests.EditMode.ArsenalB2Tests.TekFisek_KabukKabukDoler_TetikKeser_YuklenenlerKalir",
        "DD.Prototype.Tests.EditMode.ArsenalB2Tests.Yedek_TurBasiDolar_AsagiInmez_BosKalincaTabanBirSarjor",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Katalog_OnTanim_Gecerli_KimliklerBenzersiz",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.KonfigKopyasi_TanimSayilariniTasir_KonseySayilari",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol",
        "DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Egri_TabandanBaslar_MonotonArtar_TavandaSabitlenir",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.GecSinir_PencereUstundenBuyuk_RoundBitisindenKucuk",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IkinciSpawn_ConfigtenOkunur_IhtimalAralikta",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanItibaren_IhtimalPozitif_GarantiDegil",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanOnce_IhtimalSifir",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Nobetci_KasitliSapmayi_Yakalar",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.RoundKoprusu_IlerlemeyiKirpar_SaglayiciYokkenBagliDegil",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SabitTazelik_SpecialWeaponConfig_SevkiyatVarsayilanlaAyni",
        "DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SpawnPenceresi_RoundIcinde_BaslangicVeBitisDisinda"
    ],
    "gel": [
        "DD.Prototype.Tests.EditMode.GelStateTests.DeganlerinSuresiEsitlenir_Tavan_EnEskiSoner_Expire",
        "DD.Prototype.Tests.EditMode.GelStateTests.Icerisi_YukseklikBandi_EnYeniKazanir",
        "DD.Prototype.Tests.PlayMode.GelSliceTests.Jel_KayarDuserOlur_Kaldirma0_UltraKalkar_Devrilme"
    ],
    "chain": [
        "DD.Prototype.Tests.EditMode.ChainTargetTests.KalkanKayanAtlanir_UltraZinciriDevamEttirir_Duvar",
        "DD.Prototype.Tests.EditMode.ChainTargetTests.Sira_EnYakin_HopYaricapi_Tavan_AramaSiniriYok"
    ],
    "ragdoll": [
        "DD.Art.Tests.PlayMode.AnimationRagdollTests.Havuz_200Dongu_SizintiYok",
        "DD.Art.Tests.PlayMode.AnimationRagdollTests.Olum_ItkiYonundeDuser_Yerlesir_PozKalir_HavuzaDoner",
        "DD.Art.Tests.PlayMode.AnimationRagdollTests.SahneDegisimi_DusenRagdoll_HatasizBirakilir",
        "DD.Art.Tests.PlayMode.AnimationRagdollTests.Tavan_Dolunca_YaziliDusus_HepsiBiter_SonraYenidenDogar"
    ],
    "zombieAttacks": [
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil",
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.BiteGrabKapisi_ArkaDonukVeyaDuran_TekKapma",
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.Inceleme_R1A_SertMenzil_AboneKorunur_AgirlikYonetimOncesi",
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.SlotTahtasi_AgirStillerTekil_WindupTavani_Yayilim",
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.TierIzinleri_ImzaStilNormalizasyonu",
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.UcKezAyniStilYok_ElDegisir_Deterministik",
        "DD.Prototype.Tests.PlayMode.AttackVarietySliceTests.AltiStandard_YirmiSaniye_StilCesitliligi_UcTekrarYok_WindupTavani_TekKapma"
    ],
    "animation": [
        "DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz",
        "DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_DonusteIceEgilir_DuzdeDik",
        "DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_KadansBandi_KalabalikFarkliFaz_Kayar"
    ],
    "camera": [
        "DD.Prototype.Tests.PlayMode.CameraContractTests.KameraYawIle_HareketVeAnalogNisan_KameraTabaninaGore",
        "DD.Prototype.Tests.PlayMode.CameraModesPlayTests.DuvarIciSayaci_PozitifKontrol",
        "DD.Prototype.Tests.PlayMode.CameraModesPlayTests.TP_DuvarItmesi_KameraAslaDuvarIcindeDegil",
        "DD.Prototype.Tests.PlayMode.CameraModesPlayTests.Tepeden_NisanIsiniEskiDuzlemIzdusumuyleAyni"
    ],
    "world": [
        "DD.Art.Tests.EditMode.NavMeshConnectivityTests.KopruGuvertesi_BedenKoridoruAcik_CitSokulmez_EtekBirlesik",
        "DD.Art.Tests.EditMode.NavMeshConnectivityTests.NavMesh_TekParca_BedenErisilebilirAdacikYok_IsaretliNoktalarBaslangicaUlasir",
        "DD.Art.Tests.EditMode.WorldBoundaryTests.HavuzCitleri_YurunebilirNavMeshiKesmez_BedenAdimiUstuPropUstundeNavMeshYok",
        "DD.Art.Tests.EditMode.WorldBoundaryTests.Havuzlar_KenardanIceriKapsulTaramasi_CitVeyaGecitteDurur",
        "DD.Art.Tests.EditMode.WorldBoundaryTests.OynanabilirAlan_ZeminDelikleriYalnizHavuzlarda",
        "DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_CevreBoyuncaKapsulTaramasi_BoslukYok",
        "DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_DortDuvar_IgnoreRaycast_OyuncuVeDusmaniDurdurur_AtisVeKamerayiEtkilemez"
    ],
    "aiFlags": [
        "DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil"
    ],
    "progression": [
        "DD.Prototype.Tests.EditMode.MetaPathTests.AnlikGoruntu_S09",
        "DD.Prototype.Tests.EditMode.MetaPathTests.Katalog_Gecerli_Sayfalar_ve_Silahlar",
        "DD.Prototype.Tests.EditMode.MetaPathTests.KayitHatasi_RamDegismez",
        "DD.Prototype.Tests.EditMode.MetaPathTests.KosuDegistiricileri_DukkanVeAgac",
        "DD.Prototype.Tests.EditMode.MetaPathTests.Respec_SilahlarKalir_IlkTam_SonraYuzde75",
        "DD.Prototype.Tests.EditMode.MetaPathTests.SilahKilidi_Secim_SatiriSorgusu",
        "DD.Prototype.Tests.EditMode.MetaPathTests.SilahSatinAlma_Atomik_Idempotan_SilahAcar",
        "DD.Prototype.Tests.EditMode.MetaPathTests.TazeProfil_Durumlar",
        "DD.Prototype.Tests.EditMode.MetaPathTests.YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz",
        "DD.Prototype.Tests.EditMode.RunTreeTests.AnlikGoruntu_SatinAlinanKart_TekliftenDuser",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Anlik_Goruntu_S09_Alanlari_YeniAcilan_Yol",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Doruk_M1Oncesi_YalnizPinleTeklife_Girer",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Dukkan_AgaciKosuBasinaKurar_KeseyiOder_OcakKomutu",
        "DD.Prototype.Tests.EditMode.RunTreeTests.EtkisiBaglanmamis_Kilitli_Satilmaz_Teklife_Girmez",
        "DD.Prototype.Tests.EditMode.RunTreeTests.IcerikPaketi_HerDugumunAnahtari_Var",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Izgara_IkiSerit_4x4_BenzersizHucre_CekirdekDallarRoster",
        "DD.Prototype.Tests.EditMode.RunTreeTests.KademeKisaAdlari_Benzersiz_TamKelime_OnIkiAlti",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Katalog_Gecerli_OnSilah_KisaAd12",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Kese_TurSonuYuzde10_YalnizSahipken",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Keystone_Kavsak_Dislama_Doruk_Kurallari",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Nisan_Yol_VeTeklifGarantisi",
        "DD.Prototype.Tests.EditMode.RunTreeTests.Ocak_KartDisi_Fiyat_TurSiniri_Tavan_Hasar",
        "DD.Prototype.Tests.EditMode.RunTreeTests.YeniDallar_EnumSozlesme_CekiliseGirmez"
    ],
    "audio": [
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AkustikAlanlar_V6Haritasi_SalonKoridor_Histerezis_SahneYoksaOpen",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AnimasyonOlaylari_HerKlipOlayininSatiriVar_SilahFoleyAyrik",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AtesV3_KimlikTablosu_WP069AudioIdleri_HibritUcSilah_KuyrukMatrisi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AyakSesi_PlakaVaryantlari_AdKurali",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_BesPoligon_HerMerkezKendiBolgesineCozulur",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_NoktaPoligonTesti",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.DarbeV3_YuzeyKalibreMatrisi_AdlarVeKazanc",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Firlatici_UcusDongusuEslemi_PitchRampasi_PatlamaKatmani",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.FoleyV3_SilahBasinaMekanizmaSeti_AileVeKovanEslemi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestLufsVeTepe_StandartIcinde_VeYenidenOlcumTutuyor",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestVar_DonusturucuIdempotan_VeYuklenebilir",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_AP017AileAdlari_KendiSesineCozulur",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_EventIdVeriden_AltiOlayKendiSesi_AdOlayIcindeInceltir",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.LoudnessMeter_TamOlcekSinus_BS1770Referansi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Mixer_AltiGrupDortSnapshot_AcikParametreler_KopruEslemesi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerCombatVeRunOlayiIcinSatirVar",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerSesinRecipeVeyaKatalogKarsiligiVar",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_OyuncuUiAmbiyansMuzikSatirlari_TamVeTekil",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SilahFoleyV3_KlipSetineGore_MekanizmaEslemesi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SpecialKullanim_RaijuNamekuji_Satirlari_EskiKimliklerYok",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.TierVokalSeti_HerTierKendiKimligi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiOlaylari_HerUiEventUyesininSatiriVar_RouterHaritayaEsit",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiV3_Sozlesme_YeniOlaylarBagli_RunCuelariDuckYok_SliderHizi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UstKalite_24BitKaynak_GurultuTabani_Klip_DC_ImporterKalitesi",
        "DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.YuzeyEtiketleri_HerPlacementAilesininKuraliVar",
        "DD.Prototype.Tests.PlayMode.AudioSliceTests.HavuzTavani_AsimdaDusurur_AktifSesKapasiteyiAsmaz",
        "DD.Prototype.Tests.PlayMode.AudioSliceTests.SesYolu_KararliDurumda_SifirAyirma"
    ],
    "ui": [
        "DD.Prototype.Tests.EditMode.InputParityContractTests.InputSystem_YalnizInputKlasorunde",
        "DD.Prototype.Tests.EditMode.UiCameraModesTests.KameraSatirlari_Sinirlanir_Kalici_EskiDosyaVarsayilan"
    ],
    "readability": [
        "DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet"
    ]
}
# C-00 30.09 K4 (c-00/WP-078-K-KARARLARI-2026-09-30.md): the A3 aiFlags Player smoke matrix is the AI argv flags the
# FINAL C# source actually parses — -ddAiV3 (AttackStyles.ReadFlag) and, since S-06's R2 reached main, -ddRagdoll
# (RagdollProxyPool.ReadFlag; 1 = default ragdoll, 0 = every death keeps its clip). tests/test_wp078_ai_flags.py compares
# this tuple with the C# sources; a new parsed AI flag must grow it (and the matrix), never silently stay outside.
AI_FLAGS = ("-ddAiV3", "-ddRagdoll")
# The v1 plan flags that the specification names but this FINAL's C# source does not parse (K4). They are listed
# on the A3 aiFlags row with their C-00 30.09 disposition and never counted as PASS. When one is parsed on main it
# moves into AI_FLAGS (tests/test_wp078_ai_flags.py fails until it does) and the matrix grows by two runs.
AI_FLAGS_NOT_PASS = {
    "-ddChainHops": ("RETIRED", "retired (DEC-097/099)"),
    "-ddGelPuddles": ("RETIRED", "retired (DEC-097/099)"),
    "-ddCorpseLinger": ("WITHDRAWN", "withdrawn in writing (S-03, C-00 30.09)"),
    "-ddCrowdNeighbours": ("WITHDRAWN", "withdrawn in writing (S-03, C-00 30.09)"),
    "-ddSpawnLos": ("WITHDRAWN", "withdrawn in writing (S-03, C-00 30.09)"),
}
NOT_IN_CODE_REASON = "spesifikasyonda var, kodda yok"
# C-00 30.09 K1: bot runs (A2, A5, flagRuns) use the dev build — QaRunBot is compiled only under
# DEVELOPMENT_BUILD || UNITY_EDITOR; the A4 perf gate uses the release-probe build (release options + DD_PERF_PROBE).
BOT_BUILD_ROLE = "dev"
PERF_BUILD_ROLE = "probe"
BUILD_MODES = {"dev": "dev", "release": "release", "probe": "release-probe"}
# K6: A2 + A5 stability runs whose Player logs feed the A3 `world` same-spot stall measure (tools/qa/stuck_clusters.py).
WORLD_LOGS_EXPECTED = 3 + 2
# C-00 30.09 R1–R3: A2 route-run evidence frames (QaRunBot -ddQaBotRouteTour). The top-down camera's yaw is fixed north
# and its top ray is ~28° below the horizon, so topdown horizon is notApplicable; E-3 eye-level horizon is proven by FP.
# K5 (C-00 30.09): QaRunBot.DefaultRoundTimeout; the soak reports each run's slowest round against it, threshold unchanged.
ROUND_TIMEOUT_SECONDS = 420.0
ROUTE_KEYS = ("courtyard", "corridor", "kairo", "horizon")
ROUTE_ROOF_KEYS = ("corridor", "kairo")
ROUTE_TOPDOWN_HORIZON = "topdown-fixed-camera"
# WP-091 (C-00 05.10): perf roots the chain itself writes beside the single A4 gate root (K8), each at one fixed place and
# never counted for the gate — the R1 FINAL probe build alternating with FINAL's in an R2 round (P2) and S-05's B9 poses.
R1_BASELINE_ROOT, B9_ROOT = "a4/perf-r1", "b9"     # wp078_accept writes there (one source: it imports these)
Z3_ROOT, Z3_R1_ROOT = "z3/perf", "z3/perf-r1"       # the Z3 fixed-pose pairs (S-05 r2_z3.py Z3_R2 / Z3_R1 must agree)
POSE_NAME = r"[a-z0-9][a-z0-9-]{0,40}"              # a B9 pose name = its <E>/b9/<name> folder
POSE_START = re.compile(r"-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?")
SECONDARY_PERF_ROOTS = {"r1-baseline": re.compile(re.escape(R1_BASELINE_ROOT) + "|" + re.escape(Z3_R1_ROOT)),
                        "b9-pose": re.compile(re.escape(B9_ROOT) + "/" + POSE_NAME),
                        "z3-pose": re.compile(re.escape(Z3_ROOT))}
# a pose or crowd variant makes a probe window another measurement: never an A4 gate window (KADRO test-engineer MUST-2)
VARIANT_FLAGS = ("-ddPilotStart", "-ddArmor")


def _read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def _helper(name):
    spec = importlib.util.spec_from_file_location("_a8_" + name, HERE / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PERF = _helper("wp078_perf_report")
SOAK = _helper("soak_gate")
SKIPS = _helper("nunit_skips")
GATE = _helper("wp064_gate")
STUCK = _helper("stuck_clusters")
R2 = _helper("r2_gates")


def percentile(ordered, fraction):
    """Nearest-rank percentile of an ascending list (the probe's and wp064_gate's definition)."""
    return ordered[min(len(ordered) - 1, max(0, math.ceil(fraction * len(ordered)) - 1))]


def tier_stall_ms(targets, tier):
    """C-00 30.09 K9: the stall threshold is per tier (tiers.<tier>.stallMs: Sinematik 40, Yüksek/Performans 33.33);
    an older targets file without it falls back to the top-level stallMs."""
    value = targets["tiers"][tier].get("stallMs", targets.get("stallMs"))
    require(number(value) and value > 0, "perf-targets has no valid stallMs for " + tier, "BLOCKED")
    return value


def window_metrics(samples, limits, stall_ms, native_stall_ms, fraction_max):
    """K9: stall count and filtered p99 recomputed from the hash-bound frame-times-ms.csv with the tier's stallMs,
    plus the raw distribution that C-00 escalates when it hides behind the filter."""
    ordered = sorted(samples)
    count = len(ordered)
    filtered = [value for value in ordered if value <= stall_ms]
    require(bool(filtered), "no non-stall CSV frames")
    mean = sum(ordered) / count
    stalls = count - len(filtered)
    filtered_p99 = percentile(filtered, 0.99)
    raw_p99 = percentile(ordered, 0.99)
    limit = limits["p99FilteredMs"]
    passed = mean <= limits["meanMs"] and filtered_p99 <= limit and stalls <= fraction_max * count
    return {"frames": count, "meanMs": round(mean, 4), "stallMs": stall_ms, "stalls": stalls,
            "stallFraction": round(stalls / count, 6), "p99FilteredMs": filtered_p99, "rawP99Ms": raw_p99,
            "fractionOver33ms": round(sum(1 for value in ordered if value > native_stall_ms) / count, 6),
            "p999Ms": percentile(ordered, 0.999), "maxMs": ordered[-1],
            "limitMeanMs": limits["meanMs"], "limitP99Ms": limit, "pass": passed,
            # K9: raw p99 over the tier's DEC p99 while the filtered p99 passes → C-00 carries it to the user (🔴).
            "rawP99OverLimit": raw_p99 > limit and filtered_p99 <= limit}


class ProofError(ValueError):
    def __init__(self, message, status="FAIL"):
        super().__init__(message)
        self.status = status


def require(condition, message, status="FAIL"):
    if not condition:
        raise ProofError(message, status)


def integer(value):
    return type(value) is int


def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def acceptance_closed(data):
    value = data.get("acceptance")
    if isinstance(value, dict):
        value = value.get("status")
        require(value is not None, "unknown native acceptance object", "BLOCKED")
    require(value is None or value in ("PASS", "READY", "ACCEPTED"), "acceptance remains open or unknown")


def png_complete(data):
    """Validate capture container/compression without making an AD judgment."""
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        return False
    offset, compressed, header = 8, bytearray(), None
    try:
        while offset < len(data):
            length = struct.unpack_from(">I", data, offset)[0]
            kind = data[offset + 4:offset + 8]
            payload = data[offset + 8:offset + 8 + length]
            crc = struct.unpack_from(">I", data, offset + 8 + length)[0]
            if len(payload) != length or zlib.crc32(kind + payload) != crc:
                return False
            offset += length + 12
            if header is None:
                if kind != b"IHDR" or length != 13:
                    return False
                header = struct.unpack(">IIBBBBB", payload)
                if header[0] == 0 or header[1] == 0:
                    return False
            if kind == b"IDAT":
                compressed.extend(payload)
            if kind == b"IEND":
                if length != 0 or offset != len(data) or not compressed:
                    return False
                # Native screenshots use non-interlaced PNG. A nonempty decoded
                # stream establishes a real complete capture, not a magic prefix.
                decoder = zlib.decompressobj()
                decoded = decoder.decompress(bytes(compressed), 128 * 1024 * 1024)
                width, height, depth, color, compression, filtering, interlace = header
                channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}.get(color)
                if not channels or depth not in (1, 2, 4, 8, 16) or (compression, filtering, interlace) != (0, 0, 0):
                    return False
                stride = 1 + math.ceil(width * channels * depth / 8)
                return (decoder.eof and not decoder.unused_data and len(decoded) == stride * height
                        and all(decoded[row * stride] <= 4 for row in range(height)))
    except (struct.error, zlib.error):
        return False
    return False


def strict_json(data):
    def reject(value):
        raise ValueError("non-finite JSON number " + value)
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("duplicate JSON key " + key)
            result[key] = value
        return result
    return json.loads(data, parse_constant=reject, object_pairs_hook=pairs)


class Inspector:
    def __init__(self, manifest, root, targets):
        self.manifest = manifest
        self.root = Path(root).resolve()
        self.commit = manifest.get("commit")
        self.targets = targets
        self.evidence = {}
        self.perf = None
        self.soak = []
        self.timeout_margin = []
        self.report_runs = []
        self.bot_runs = []
        self.route_runs = []
        self.world_logs = []
        self.build_parity = None
        self.active_owner = "UNKNOWN"

    def path(self, relative):
        require(isinstance(relative, str) and relative and not Path(relative).is_absolute(), "missing or absolute evidence path")
        path = (self.root / relative).resolve()
        require(path.is_relative_to(self.root), "evidence path escapes root")
        return path

    def file(self, ref, read=False):
        require(isinstance(ref, dict), "missing file reference", "BLOCKED")
        digest = ref.get("artifactSha256")
        require(isinstance(digest, str) and DIGEST.fullmatch(digest), "missing or invalid artifact hash", "BLOCKED")
        path = self.path(ref.get("path"))
        require(path.is_file(), "missing evidence: " + str(ref.get("path")), "BLOCKED")
        hasher, size = hashlib.sha256(), 0
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                hasher.update(chunk)
                size += len(chunk)
        require(hasher.hexdigest() == digest, "artifact hash mismatch: " + ref["path"])
        data = path.read_bytes() if read else None
        if read:
            require(hashlib.sha256(data).hexdigest() == digest, "artifact changed during inspection: " + ref["path"])
        self.evidence[str(path.relative_to(self.root))] = {"path": str(path.relative_to(self.root)), "artifactSha256": digest, "bytes": size}
        return path, data

    def obj(self, ref):
        path, data = self.file(ref, read=True)
        try:
            result = strict_json(data)
        except (ValueError, UnicodeError) as error:
            raise ProofError("invalid JSON " + ref["path"] + ": " + str(error)) from error
        require(isinstance(result, dict), "evidence JSON must be an object")
        return path, result

    def source(self, data):
        source = data.get("executionCommit", data.get("head"))
        require(source == self.commit and isinstance(source, str) and SHA.fullmatch(source), "native source is not full FINAL SHA")
        for field in ("head", "executionCommit", "sourceCommit", "testedCommit"):
            if field in data:
                require(data[field] == self.commit, "mixed native " + field)

    def guard(self, data):
        require(isinstance(data, dict), "missing guard receipt", "BLOCKED")
        for field in ("snapshotExit", "restoreExit"):
            require(integer(data.get(field)) and data[field] == 0, "guard " + field + " is not verified zero")
        # wp078_bot writes outOfScopeChanged as a boolean, older receipts as integer 0.
        value = data.get("outOfScopeChanged")
        require(value is False or (integer(value) and value == 0), "guard outOfScopeChanged is not verified zero")
        value = data.get("verified")
        require(value is True or (integer(value) and value == 1), "guard restoration unverified")

    def bind(self, ref, proof, selected):
        """Selected outputs must have been hashed by this execution, not just intake."""
        mapping = proof.get("artifactSha256")
        require(isinstance(mapping, dict) and mapping, "missing execution artifact map", "BLOCKED")
        parent = self.path(ref["path"]).parent
        own = self.path(ref["path"])
        def visit(value):
            if isinstance(value, dict):
                if "path" in value and "artifactSha256" in value:
                    path = self.path(value["path"])
                    if path != own:
                        matching = [digest for relative, digest in mapping.items() if self.path(str(parent.relative_to(self.root) / relative)) == path]
                        require(matching == [value["artifactSha256"]], "selected resource not bound to execution: " + value["path"])
                else:
                    for item in value.values():
                        visit(item)
            elif isinstance(value, list):
                for item in value:
                    visit(item)
        visit(selected)

    def original_exit(self, value, allowed=(0,)):
        require(integer(value) and value in allowed, "original exit is not an allowed actual integer")

    def provenance(self, ref, original, role=None, balance=False, executed=True, route=False):
        path, data = self.obj(ref)
        self.source(data)
        # route (prova 30.09): QaRunBot exits 3 when a route point is blocked; route_run turns that into a named BLOCKED
        self.original_exit(original, (0, 1, 3) if balance else (0, 3) if route else (0,))
        if executed:
            require("exitCode" in data, "missing native original exitCode", "BLOCKED")
            if "timedOut" in data:
                require(data["timedOut"] is False, "native attempt timed out or has invalid timeout metadata")
        if "exitCode" in data:
            require(integer(data["exitCode"]) and data["exitCode"] == original, "native exit contradicts original exit")
        if "status" in data:
            allowed = (("complete",) if original == 0 else ("failed",)) if balance or route else ("complete",)
            require(data["status"] in allowed, "native attempt did not complete")
        for field in ("causalClosure", "originalFullCiBlockedOutcomeReproduced"):
            if not balance and field in data:
                require(data[field] is True, "targeted receipt does not close " + field)
        acceptance_closed(data)
        if role is not None:
            expected = self.manifest.get("buildHashes", {}).get(role)
            require(isinstance(expected, str) and DIGEST.fullmatch(expected), "missing expected binary hash", "BLOCKED")
            require(data.get("binarySha256") == expected, "mixed or missing run binary hash")
        mapping = data.get("artifactSha256")
        if mapping is not None:
            require(isinstance(mapping, dict) and mapping, "invalid native artifact hash map")
            for relative, digest in mapping.items():
                full = self.path(str(path.parent.relative_to(self.root) / relative))
                self.file({"path": str(full.relative_to(self.root)), "artifactSha256": digest})
        if "commandSha256" in data:
            self.file({"path": str((path.parent / "command.json").relative_to(self.root)), "artifactSha256": data["commandSha256"]})
        return data

    def common(self, step, guarded=True):
        owner = step.get("owner") if isinstance(step, dict) else None
        self.active_owner = owner if isinstance(owner, str) and owner else "UNKNOWN"
        require(isinstance(step, dict), "missing checklist step", "BLOCKED")
        status = step.get("status")
        require(status == "PASS", "step status " + str(status), "FAIL" if status == "FAIL" else "BLOCKED")
        require(step.get("executionCommit") == self.commit, "step source is not FINAL SHA")
        acceptance_closed(step)
        for key in ("causalClosure", "originalFullCiBlockedOutcomeReproduced"):
            if key in step:
                require(step[key] is True, "targeted receipt does not close " + key)
        proof = self.provenance(step.get("provenance"), step.get("originalExit"), executed=guarded)
        self.bind(step["provenance"], proof, step)
        refs = step.get("evidence")
        require(isinstance(refs, list) and refs, "missing step evidence", "BLOCKED")
        for ref in refs:
            self.file(ref)
        if guarded:
            self.guard(step.get("guard"))
            require(proof.get("status") == "complete", "missing native completion", "BLOCKED")
            require(proof.get("guard") == step["guard"], "guard receipt not bound to execution")
        return proof

    def xml(self, ref, integrity=False, identities=None):
        _, data = self.file(ref, read=True)
        try:
            root = ET.fromstring(data)
            counts = {key: int(root.attrib[key]) for key in ("total", "passed", "failed", "skipped", "inconclusive")}
        except (ET.ParseError, KeyError, ValueError) as error:
            raise ProofError("invalid or incomplete NUnit XML") from error
        leaves = root.findall(".//test-case")
        require(root.tag == "test-run", "unsupported NUnit root/schema")
        # C-00 30.09 Y6: a skip counts only as Skipped ∧ Explicit ∧ Diagnostics (nunit_skips); an Ignore skip is red
        allowed, _, unexpected = SKIPS.classify(SKIPS.skipped_cases_from_root(root))
        require(not unexpected, "unexpected NUnit skip (Y6: only [Explicit] + Diagnostics may skip): " + ", ".join(unexpected))
        require(counts["total"] > 0 and counts["passed"] + len(allowed) == counts["total"] == len(leaves), "empty or inconsistent NUnit counts")
        require(counts["skipped"] == len(allowed) and all(counts[key] == 0 for key in ("failed", "inconclusive")), "NUnit failed/skipped/inconclusive")
        allowed_names = set(allowed)
        require(root.get("result") == "Passed" and all(case.get("result") == "Passed" or (case.get("fullname") or case.get("name")) in allowed_names
                                                       for case in leaves), "NUnit leaf did not pass")
        names = [case.get("fullname") or case.get("name") for case in leaves]
        require(all(names) and len(set(names)) == len(names), "missing or duplicate NUnit test identity")
        if integrity:
            require(any("ProjectIntegrityTests." in name for name in names), "no actual ProjectIntegrityTests leaf")
        if identities is not None:
            # NUnit parameterized instances append arguments to the same native
            # declared method; never allow an arbitrary class or invented method.
            observed = {name.split("(", 1)[0] for name in names if name not in allowed_names}   # Y6: a skipped diagnostic is not run
            missing = set(identities) - observed
            require(not missing, "native A3 domain test identities missing: " + ", ".join(sorted(missing)), "BLOCKED")
        return counts

    def required_log_failures(self, data):
        """WP-078 §5.5 additions absent from the legacy generic helper."""
        patterns = (
            r"Failed to create agent\b",
            r"SetDestination[^\n]*not[^\n]*NavMesh",
            r"SetDestination[^\n]*(?:can only be called on an active agent|failed|error|invalid)",
            r"\[Pool\][^\n]*exceeded its capacity of \d+\. Growing\.",
            r"\b(?:ConfigurableJoint|CharacterJoint|HingeJoint|FixedJoint|Joint|Rigidbody|RigidBody)\b[^\n]*(?:error|failed|failure|invalid|cannot|can't|must have|requires)",
            r"(?:error|failed|failure|invalid|cannot|can't)[^\n]*\b(?:ConfigurableJoint|CharacterJoint|HingeJoint|FixedJoint|Joint|Rigidbody|RigidBody)\b",
        )
        text = data.decode("utf-8", errors="replace")
        hits = [line for line in text.splitlines() if any(re.search(pattern, line, re.IGNORECASE) for pattern in patterns)]
        for line in text.splitlines():
            if not re.search(r"\[Projectile\]\s+round=\d+", line):
                continue
            value = re.search(r"\boverflow=(\d+)\b", line)
            if value and int(value[1]) > 0:
                hits.append(line)
        return hits

    def library(self, ref):
        _, data = self.obj(ref)
        self.source(data)
        require(data.get("schema") == "dd-wp064-library-provenance-v1" and data.get("phase") == "post-build", "Library import phase unproven", "BLOCKED")
        assets = self.manifest.get("assetsTree")
        require(isinstance(assets, str) and SHA.fullmatch(assets) and data.get("assetsTree") == assets, "Library imported Assets tree mismatch")
        require(data.get("porcelain") in ("", []), "Library source not verified clean")
        for field in ("lfsPointersOnDisk", "lfsMissing"):
            require(integer(data.get(field)) and data[field] == 0, "unmaterialized or missing LFS: " + field)
        size = data.get("library", {}).get("artifactDbBytes")
        require(integer(size) and size > 0, "missing Library provenance")
        mtime = data.get("library", {}).get("artifactDbMtime")
        require(number(mtime) and mtime > 0, "missing Library import timestamp")

    def bot_guard(self, run, proof, kind):
        """The bot run's own attempt.json (wp078_bot, wp064_run shape) carries the guard receipt; a manifest copy may
        only repeat it. kind/buildRole/mode in the attempt, when present, must match the step that uses the run."""
        receipt = proof.get("guard")
        require(isinstance(receipt, dict), "bot attempt.json has no guard receipt", "BLOCKED")
        self.guard(receipt)
        copy = run.get("guard")
        if copy is not None:
            require(isinstance(copy, dict) and all(copy.get(field) == receipt.get(field) for field in ("snapshotExit", "restoreExit", "verified", "outOfScopeChanged")),
                    "manifest guard differs from the attempt's guard receipt")
        if "kind" in proof:
            require(proof["kind"] in kind, "attempt kind " + str(proof["kind"]) + " used as " + "/".join(kind))
        if "buildRole" in proof:
            require(proof["buildRole"] == BOT_BUILD_ROLE, "attempt buildRole " + str(proof["buildRole"]) + " is not dev")

    def bot_role(self, run):
        role = run.get("buildRole", BOT_BUILD_ROLE)
        require(role == BOT_BUILD_ROLE, "bot runs use the dev build (QaRunBot is DEVELOPMENT_BUILD-only; C-00 30.09 K1), not " + str(role))
        return role

    def command_argv(self, path, data):
        """The runner's hash-bound command.json next to the provenance (wp064_run writes a string array)."""
        require("commandSha256" in data, "missing native command hash", "BLOCKED")
        _, raw = self.file({"path": str((path.parent / "command.json").relative_to(self.root)), "artifactSha256": data["commandSha256"]}, read=True)
        try:
            command = strict_json(raw)
        except (ValueError, UnicodeError) as error:
            raise ProofError("invalid native command.json: " + str(error)) from error
        require(isinstance(command, list) and all(isinstance(arg, str) for arg in command), "native command is not argv string list")
        return command

    def input_measurable(self, run):
        """S-02 01.10 QA input-loss contract: a run whose bot input never reached the game (virtual pad disabled, or the
        pointer holding the scheme after the pad took it) is UNMEASURED — named BLOCKED, never PASS or FAIL. Read from the
        hash-verified native summary before the exit/status checks, which an unmeasurable run fails for that reason only."""
        if not isinstance(run, dict) or not isinstance(run.get("summary"), dict):
            return
        _, summary = self.obj(run.get("summary"))
        if summary.get("inputMeasurable") is False:
            require(False, "input lost (unmeasurable, rerun): pad disabled " + str(summary.get("padDisabledFrames")) + " / pointer after pad "
                    + str(summary.get("pointerAfterPadFrames")) + " / unfocused " + str(summary.get("unfocusedFrames")) + " frames", "BLOCKED")

    def run(self, run, stability=False, report=False, step=None):
        require(isinstance(run, dict), "missing physical run", "BLOCKED")
        self.input_measurable(run)
        proof = self.provenance(run.get("provenance"), run.get("originalExit"), self.bot_role(run), balance=report)
        self.bind(run["provenance"], proof, [run.get("summary"), run.get("log"), run.get("frames")])
        self.bot_guard(run, proof, ("report",) if report else ("stability",) if stability else ("gameplay",))
        require(proof.get("status") == ("failed" if report and run.get("originalExit") in (1, 3) else "complete"), "missing native run completion", "BLOCKED")
        _, summary = self.obj(run.get("summary"))
        log, data = self.file(run.get("log"), read=True)
        require(bool(data.strip()), "empty Player log", "BLOCKED")
        # K6: every hash-verified A2/A5 Player log feeds the A3 world same-spot stall measure (report runs: observed only).
        self.world_logs.append({"step": step or ("A5-report" if report else "A5" if stability else "A2"),
                                "gate": not report, "log": run["log"]["path"],
                                "cameraMode": summary.get("cameraMode"), "seed": summary.get("seed"),
                                "measure": STUCK.clusters(data.decode("utf-8", errors="replace"))})
        require(not SOAK.log_failures(log), "runtime error in Player log")
        hits = self.required_log_failures(data)
        require(not hits, "WP-078 required runtime log failure: " + (hits[0][:240] if hits else ""))
        require(all(field not in summary or summary[field] is False for field in ("timedOut", "artifactWriteFailed")), "run timed out, artifacts failed or invalid native boolean")
        mode = MODES.get(summary.get("cameraMode"))
        require(mode is not None, "unknown camera mode")
        require("mode" not in proof or MODES.get(proof["mode"]) == mode, "attempt mode contradicts the bot summary camera mode")
        if any(key in summary for key in ("executionCommit", "head", "sourceCommit", "testedCommit")):
            # Native summaries may carry optional extra SHA fields; none may differ.
            for key in ("executionCommit", "head", "sourceCommit", "testedCommit"):
                if key in summary:
                    require(summary[key] == self.commit, "mixed summary " + key)
        if "executionCommit" in summary:
            self.source(summary)
        if report:
            require(summary.get("runMode") == "normal", "real bot report is not normal mode")
            require(integer(summary.get("roundsCompleted")) and summary["roundsCompleted"] >= 0 and integer(summary.get("deaths")) and summary["deaths"] >= 0, "missing real bot outcome")
            require(isinstance(summary.get("pass"), bool), "missing real bot verdict")
            # QaRunBot exits 3 when the real bot fails (older receipts recorded 1); pass is exit 0.
            require((run["originalExit"] == 0) == summary["pass"], "real bot verdict contradicts original exit")
            require(summary["pass"] or summary["deaths"] > 0, "failed real bot is not a documented balance death")
            return mode, summary, proof
        # K6: stuckRecoveries is the bot's own route-recovery counter — mandatory integer, reported with unstickSweeps
        # per run, never a gate (DEC-091 "takılma 0" is the round timeout; same-spot stalls are the A3 world measure).
        require(integer(summary.get("stuckRecoveries")), "native stuckRecoveries counter missing or not an integer", "BLOCKED")
        sweeps = summary.get("unstickSweeps")
        record = {"step": step or ("A5" if stability else "A2"), "cameraMode": mode, "seed": summary.get("seed"),
                  "summary": run["summary"]["path"], "stuckRecoveries": summary["stuckRecoveries"],
                  "unstickSweeps": sweeps if integer(sweeps) else None,
                  "qualityTier": summary.get("qualityTier"), "worldHalfExtent": summary.get("worldHalfExtent")}
        self.bot_runs.append(record)
        count = 12 if stability else 5
        records = summary.get("rounds")
        require(isinstance(records, list) and all(isinstance(record, dict) for record in records), "missing ordered rounds")
        require(all(integer(summary.get(key)) for key in ("roundsRequested", "roundsCompleted")) and all(integer(record.get("round")) for record in records), "non-integer round counts")
        require(SOAK.exact_rounds(summary, count)[0], "incomplete or unordered rounds")
        require(integer(summary.get("seed")), "missing run seed")
        if stability:
            require(mode == "topdown" and summary["seed"] in (61, 26), "wrong DEC-091 stability scope")
            # K5: expected tier = perf-targets defaultTier (DEC-100 Sinematik), read from the bot's own summary; the
            # world ring is the bot's own worldHalfExtent (V6 55). Argv or labels are not evidence of either.
            default_tier = self.targets.get("defaultTier")
            require(default_tier in self.targets["tiers"], "perf-targets defaultTier missing", "BLOCKED")
            expected_tier = self.targets["tiers"][default_tier]["probeValue"]
            require("qualityTier" in summary, "bot summary lacks native qualityTier (K5)", "BLOCKED")
            require(summary["qualityTier"] == expected_tier, "stability ran tier " + str(summary["qualityTier"]) + ", DEC-100 default is " + expected_tier)
            require("worldHalfExtent" in summary, "bot summary lacks native worldHalfExtent (K5)", "BLOCKED")
            require(number(summary["worldHalfExtent"]) and summary["worldHalfExtent"] == 55, "stability world ring is not V6 55: " + str(summary["worldHalfExtent"]))
            require("hostClean" in proof, "missing stability host proof", "BLOCKED")
            require(proof["hostClean"] is True, "stability host was not clean")
            command = self.command_argv(self.path(run["provenance"]["path"]), proof)
            tier_arg = GATE.flag_value(command, "-ddQualityTier")
            require(isinstance(tier_arg, str) and tier_arg.lower() == default_tier.lower(),
                    "stability command.json does not give -ddQualityTier " + default_tier + " exactly once")
            require(summary.get("runMode") == "stability" and summary.get("stabilityPass") is True, "stability did not pass")
            for record in records:
                require(all(number(record.get(key)) and record[key] >= 0 for key in ("memMonoUsedMB", "memTotalAllocatedMB", "memRssMB")), "missing or non-finite memory sample")
            require(SOAK.memory_checks(records)[0], "stability memory limit failed")
        else:
            require(summary["seed"] == 61 and summary.get("runMode") == "normal" and summary.get("pass") is True, "wrong normal-bot scope or verdict")
            require(integer(summary.get("deaths")) and summary["deaths"] == 0, "normal bot death")
            camera = re.findall(r"\[Camera\] inside-collider round=(\d+)[^\n]*?frames=(\d+)/(\d+)", data.decode("utf-8", errors="replace"))
            require(len(camera) == 5 and sorted(int(row[0]) for row in camera) == list(range(1, 6)) and all(int(row[1]) == 0 and int(row[2]) > 0 for row in camera), "camera inside-collider proof incomplete or failed")
            # C-00 30.09 R1–R3: evidence frames come from the separate A2 route runs, not from gameplay runs.
        return mode, summary, proof

    def route_run(self, run):
        """C-00 30.09 R1–R3: one QaRunBot ROUTE run per camera mode (-ddQaBotRouteTour <runOut>/frames, dev build).
        No rounds/deaths gate (it quits after the tour); original exit 0, verified guard, native bot-summary
        routeFrames cross-check of every bound frame. A missing frame key is BLOCKED, never substituted."""
        require(isinstance(run, dict), "missing route run", "BLOCKED")
        proof = self.provenance(run.get("provenance"), run.get("originalExit"), self.bot_role(run), route=True)
        self.bind(run["provenance"], proof, [run.get("summary"), run.get("log"), run.get("frames")])
        self.bot_guard(run, proof, ("route",))
        _, summary = self.obj(run.get("summary"))
        if run.get("originalExit") != 0:
            # prova 30.09: a blocked point ends the route run with QaRunBot exit 3 — BLOCKED, naming the point and its
            # native reason; exit 3 with no blocked point is a real failure
            blocked = [str(row.get("key")) + ": " + str(row.get("reason")) for row in summary.get("routeFrames") or []
                       if isinstance(row, dict) and row.get("status") == "blocked"]
            require(bool(blocked), "route run exit " + str(run.get("originalExit")) + " without a blocked route point")
            require(False, "route run blocked " + "; ".join(blocked), "BLOCKED")
        log, data = self.file(run.get("log"), read=True)
        require(bool(data.strip()), "empty route Player log", "BLOCKED")
        require(not SOAK.log_failures(log) and not self.required_log_failures(data), "runtime error in route Player log")
        for key in ("executionCommit", "head", "sourceCommit", "testedCommit"):
            if key in summary:
                require(summary[key] == self.commit, "mixed route summary " + key)
        mode = MODES.get(summary.get("cameraMode"))
        require(mode is not None, "unknown route camera mode")
        require("mode" not in proof or MODES.get(proof["mode"]) == mode, "attempt mode contradicts the route summary camera mode")
        require(summary.get("runMode") == "route", "summary is not a native route run (runMode=" + str(summary.get("runMode")) + ")")
        native = summary.get("routeFrames")
        require(isinstance(native, list) and all(isinstance(row, dict) for row in native), "bot summary lacks native routeFrames", "BLOCKED")
        by_key = {}
        for row in native:
            require(row.get("key") not in by_key, "duplicate native routeFrames key " + str(row.get("key")))
            by_key[row.get("key")] = row
        frames = run.get("frames")
        require(isinstance(frames, dict), "missing route frames", "BLOCKED")
        run_dir = self.path(run["provenance"]["path"]).parent
        digests, record = set(), {"cameraMode": mode, "summary": run["summary"]["path"], "frames": {}}
        for key in ROUTE_KEYS:
            require(key in frames, "route frame " + key + " missing for " + mode, "BLOCKED")
            ref = frames[key]
            row = by_key.get(key)
            require(isinstance(row, dict), "native routeFrames has no " + key + " for " + mode, "BLOCKED")
            if mode == "topdown" and key == "horizon":
                require(isinstance(ref, dict) and ref.get("notApplicable") == ROUTE_TOPDOWN_HORIZON and set(ref) == {"notApplicable"},
                        "topdown horizon must be recorded notApplicable " + ROUTE_TOPDOWN_HORIZON)
                require(row.get("status") == "notApplicable" and str(row.get("reason", "")).startswith(ROUTE_TOPDOWN_HORIZON),
                        "native topdown horizon is not notApplicable " + ROUTE_TOPDOWN_HORIZON)
                record["frames"][key] = {"notApplicable": ROUTE_TOPDOWN_HORIZON}
                continue
            require(isinstance(ref, dict) and "notApplicable" not in ref, "route frame " + key + " for " + mode + " must be a captured frame")
            path, image = self.file(ref, read=True)
            require(row.get("status") == "captured", "native route frame " + key + " status " + str(row.get("status")))
            expected = "frames/route-" + key + ".png"
            require(row.get("path") == expected and path == run_dir / expected,
                    "route frame " + key + " path is not the native " + expected + " of this run")
            require(MODES.get(row.get("mode")) == mode, "route frame " + key + " captured in camera mode " + str(row.get("mode")))
            if key in ROUTE_ROOF_KEYS:
                require(row.get("roofAbove") is True, "route frame " + key + " is not under a roof (roofAbove)")
            require(png_complete(image), "invalid PNG evidence: " + key)
            require(ref["artifactSha256"] not in digests, "same frame bytes reused for two route points in " + mode)
            digests.add(ref["artifactSha256"])
            record["frames"][key] = {"path": ref["path"], "artifactSha256": ref["artifactSha256"], "roofAbove": row.get("roofAbove")}
        self.route_runs.append(record)
        return mode

    def A0(self, step):
        self.library(step.get("library"))

    def A1(self, step):
        refs = step.get("xml", {})
        for kind in ("editMode", "playMode"):
            self.xml(refs.get(kind))
        # R2 (C-00 01.10): main-ci parity — every ci-isolated `alone` test has its own session XML (assembler lists them from
        # the tree's ci-isolated.txt, so a missing session is a missing file → BLOCKED); a manifest without the key is older
        alone = refs.get("playModeAlone", [])
        require(isinstance(alone, list), "A1 playModeAlone is not a list of session XMLs")
        for ref in alone:
            self.xml(ref)

    def A2(self, step):
        runs = step.get("runs")
        require(isinstance(runs, list) and len(runs) == 3, "missing three normal-bot modes", "BLOCKED")
        require(sorted(self.run(run, step="A2")[0] for run in runs) == ["fp", "topdown", "tp"], "duplicate or missing normal-bot mode")
        routes = step.get("routeRuns")
        require(isinstance(routes, list) and len(routes) == 3, "missing three A2 route runs (topdown/tp/fp)", "BLOCKED")
        paths = {str(self.path(run.get("provenance", {}).get("path"))) for run in routes if isinstance(run, dict) and isinstance(run.get("provenance"), dict)}
        require(len(paths) == 3, "one route execution reused for several camera modes")
        require(sorted(self.route_run(run) for run in routes) == ["fp", "topdown", "tp"], "duplicate or missing route-run camera mode")

    def A3(self, step):
        checks = step.get("checks")
        require(isinstance(checks, dict) and set(checks) == set(AREAS), "regression matrix incomplete", "BLOCKED")
        for area, row in checks.items():
            self.common(row)
            xml_refs = []
            for ref in row["evidence"]:
                _, data = self.file(ref, read=True)
                if data.lstrip().startswith(b"<"):
                    self.xml(ref)
                    xml_refs.append(ref)
                elif data.lstrip().startswith(b"{"):
                    _, verdict = self.obj(ref)
                    for key in ("executionCommit", "head", "sourceCommit", "testedCommit"):
                        if key in verdict:
                            require(isinstance(verdict[key], str) and verdict[key] == self.commit,
                                    area + " mixed supplemental " + key)
                    for key in ("pass", "ok"):
                        if key in verdict:
                            require(verdict[key] is True, area + " supplemental " + key + " is not boolean true")
                    if "result" in verdict:
                        require(isinstance(verdict["result"], str) and verdict["result"] in ("Passed", "PASS", "Succeeded"),
                                area + " supplemental result is not an accepted success verdict")
                    for key in ("causalClosure", "originalFullCiBlockedOutcomeReproduced"):
                        if key in verdict:
                            require(verdict[key] is True, area + " original defect remains open")
                    acceptance_closed(verdict)
                    # Build/summary/custom receipt JSON remains supplementary;
                    # it cannot establish a domain test merely by saying PASS.
            require(xml_refs, area + " missing native NUnit domain evidence; JSON verdicts are supplementary", "BLOCKED")
            observed = set()
            for ref in xml_refs:
                _, data = self.file(ref, read=True)
                root = ET.fromstring(data)
                observed.update((case.get("fullname") or case.get("name")).split("(", 1)[0] for case in root.findall(".//test-case"))
            missing = set(A3_REQUIRED_TEST_IDS[area]) - observed
            require(not missing, area + " native A3 domain test identities missing: " + ", ".join(sorted(missing)), "BLOCKED")
            if area == "aiFlags":
                self.flag_runs(row.get("flagRuns"))

    def flag_runs(self, runs):
        require(isinstance(runs, list) and len(runs) == 2 * len(AI_FLAGS),
                "A3 aiFlags needs exactly " + str(2 * len(AI_FLAGS)) + " native Player smoke runs (" + ", ".join(AI_FLAGS) + " x0/1; C-00 30.09 K4)", "BLOCKED")
        seen, execution_paths = set(), set()
        for run in runs:
            require(isinstance(run, dict) and run.get("flag") in AI_FLAGS and integer(run.get("value")) and run["value"] in (0, 1), "invalid native flag smoke identity")
            key = (run["flag"], run["value"])
            require(key not in seen, "duplicate native flag smoke")
            seen.add(key)
            execution_path = str(self.path(run.get("provenance", {}).get("path")))
            require(execution_path not in execution_paths, "one native execution reused across flag smoke rows")
            execution_paths.add(execution_path)
            self.input_measurable(run)
            proof = self.provenance(run.get("provenance"), run.get("originalExit"), self.bot_role(run))
            self.bot_guard(run, proof, ("flag",))
            require(proof.get("status") == "complete", "flag run completion unproven")
            self.bind(run["provenance"], proof, [run.get("summary"), run.get("log"), run.get("command")])
            _, raw_command = self.file(run.get("command"), read=True)
            command = strict_json(raw_command)
            require(isinstance(command, list) and all(isinstance(arg, str) for arg in command), "native flag command is not argv string list")
            positions = [i for i, arg in enumerate(command) if arg == run["flag"]]
            require(len(positions) == 1 and positions[0] + 1 < len(command) and command[positions[0] + 1] == str(run["value"]), "flag argv does not match smoke identity")
            _, summary = self.obj(run.get("summary"))
            count = summary.get("roundsRequested")
            require(integer(count) and count >= 1 and integer(summary.get("roundsCompleted")) and isinstance(summary.get("rounds"), list) and all(isinstance(record, dict) and integer(record.get("round")) for record in summary["rounds"]), "flag smoke has no actual rounds")
            require(SOAK.exact_rounds(summary, count)[0] and summary.get("runMode") == "normal" and summary.get("pass") is True and all(field not in summary or summary[field] is False for field in ("timedOut", "artifactWriteFailed")), "native flag smoke did not complete")
            for field in ("executionCommit", "head", "sourceCommit", "testedCommit"):
                if field in summary:
                    require(summary[field] == self.commit, "flag summary mixed SHA")
            log, data = self.file(run.get("log"), read=True)
            require(data.strip() and not SOAK.log_failures(log) and not self.required_log_failures(data), "native flag smoke runtime log failed")

    def ledger_rows(self, root, step):
        """K8: the single FINAL perf root's attempts-ledger.jsonl, hash-bound through the manifest (wp064_run appends
        start/end rows per attemptId; read_ledger merges them). classify_perf_ledger is deliberately not used."""
        ref = step.get("ledger")
        require(isinstance(ref, dict), "missing attempts-ledger.jsonl reference (K8)", "BLOCKED")
        path = self.path(ref.get("path"))
        require(path == root / "attempts-ledger.jsonl", "A4 ledger is not the single perf root's attempts-ledger.jsonl", "BLOCKED")
        self.file(ref)
        problems = []
        rows = GATE.read_ledger(path, lambda name, ok, value: problems.append(str(value)) if not ok else None)
        require(rows is not None, "unreadable attempts ledger: " + "; ".join(problems), "BLOCKED")
        by_id = {}
        for row in rows:
            require(row.get("kind") == "perf", "non-perf row in the perf attempts ledger: " + str(row.get("attemptId")), "BLOCKED")
            require(PERF.CELL.fullmatch(row["dir"]) is not None, "ledger row outside the perf cell layout: " + row["dir"], "BLOCKED")
            by_id[row["attemptId"]] = row
        return by_id

    def single_perf_root(self, root, step=None):
        """K8: one root per FINAL — a perf window directory anywhere else under the evidence root is not accepted, except
        in a secondary root the manifest declares at its fixed place (secondary_perf_roots)."""
        allowed = {root} | set(self.secondary_perf_roots(step or {}))
        stray = []
        builds = {(self.root / ("build-" + role)).resolve() for role in BUILD_MODES}
        for current, directories, _ in os.walk(self.root):
            # only the chain's own builds are skipped (<E>/build-<role>/*.app); any other .app is searched like a folder
            directories[:] = [name for name in directories if not (name.endswith(".app") and Path(current).resolve() in builds)]
            for name in directories:
                if PERF.CELL.fullmatch(name) and Path(current).resolve() not in allowed:
                    stray.append(str((Path(current) / name).relative_to(self.root)))
        require(not stray, "perf windows outside the single FINAL perf root: " + ", ".join(sorted(stray)[:5]), "BLOCKED")

    def secondary_perf_roots(self, step):
        """WP-091 (C-00 05.10): the perf roots the chain writes next to the gate root, never counted for the gate — the R1
        FINAL probe build alternating with FINAL's in an R2 round (P2, role r1-baseline, a4/perf-r1) and S-05's B9 pose
        windows (role b9-pose, b9/<pose>, declared with the start the B9 record ran). Each sits at its one fixed place and
        its windows prove what they are: a baseline window ran under the declared R1 commit and build (never FINAL's); a B9
        window ran FINAL with -ddPilotStart <its pose's start> (one that never started a Player has no argv and must not be
        complete; an unreadable one is BLOCKED). The other direction — no pose window in the gate root — is A4's own check
        (VARIANT_FLAGS)."""
        declared = step.get("secondaryRoots", [])
        require(isinstance(declared, list), "A4 secondaryRoots is not a list")
        roots = []
        for entry in declared:
            role, relative = (entry.get("role"), entry.get("path")) if isinstance(entry, dict) else (None, None)
            pattern = SECONDARY_PERF_ROOTS.get(role)
            require(pattern is not None and isinstance(relative, str) and pattern.fullmatch(relative) is not None,
                    "secondary perf root is not a known role at its fixed place: " + json.dumps(entry, sort_keys=True))
            path = self.path(relative)
            require(path.is_dir(), "missing secondary perf root: " + relative, "BLOCKED")
            commit = entry.get("commit")
            binary, start = entry.get("binarySha256"), entry.get("start")
            if role == "r1-baseline":
                require(isinstance(commit, str) and SHA.fullmatch(commit) is not None and commit != self.manifest.get("commit"),
                        "the R1 baseline root needs its own 40-hex commit, never FINAL")
                require(isinstance(binary, str) and DIGEST.fullmatch(binary) is not None
                        and binary != self.manifest.get("buildHashes", {}).get(PERF_BUILD_ROLE),
                        "the R1 baseline root needs its own binary sha256, never FINAL's probe")
            else:
                require(isinstance(start, str) and POSE_START.fullmatch(start) is not None,
                        "a pose root is declared with the start its record ran (x,z,yaw): " + relative)
            for directory in sorted(p for p in path.iterdir() if p.is_dir() and PERF.CELL.fullmatch(p.name)):
                where = relative + "/" + directory.name
                attempt = _read_json(directory / "attempt.json")
                if role == "r1-baseline":
                    require(isinstance(attempt, dict) and attempt.get("executionCommit") == commit,
                            "R1 baseline window not run under the declared R1 commit: " + where)
                    require(attempt.get("binarySha256") == binary, "R1 baseline window not run on the declared R1 build: " + where)
                    continue
                require(isinstance(attempt, dict), "unreadable pose-window attempt: " + where, "BLOCKED")
                require(attempt.get("executionCommit") == self.manifest.get("commit"), "B9 window not run at FINAL: " + where)
                argv = _read_json(directory / "command.json")
                if argv is None:
                    require(attempt.get("status") != "complete", "complete B9 window without its Player argv: " + where)
                else:
                    at = argv.index("-ddPilotStart") if isinstance(argv, list) and "-ddPilotStart" in argv else -1
                    require(at >= 0, "B9 pose window without -ddPilotStart: " + where)
                    require(at + 1 < len(argv) and argv[at + 1] == start, f"B9 window ran another start than its pose {start}: " + where)
                    if role == "z3-pose":   # C-00 06.10: both builds measure the full-armour crowd
                        armor = argv.index("-ddArmor") if "-ddArmor" in argv else -1
                        require(armor >= 0 and armor + 1 < len(argv) and argv[armor + 1].lower() == "all",
                                "Z3 window without -ddArmor all: " + where)
            roots.append(path)
        return roots

    @staticmethod
    def discard_kind(row):
        """K8 mechanical discard rule (wp064_run exit 75 / exit 76 meaning); every other non-complete attempt is FAIL."""
        if row.get("status") == "preflight-failed" and row.get("exitCode") == 75 and type(row.get("exitCode")) is int:
            return "preflight-failed"
        if (row.get("status") == "failed" and row.get("hostClean") is False and row.get("timedOut") is False
                and type(row.get("exitCode")) is int and row.get("exitCode") == 0 and row.get("missingArtifacts") == []):
            return "host-contaminated"
        return None

    def A4(self, step):
        # K1: the perf gate runs on release-probe (Development off, LZ4HC, only DD_PERF_PROBE differs); no dev default.
        role = step.get("buildRole")
        require(role is not None, "A4 buildRole missing (must be probe; C-00 30.09 K1)", "BLOCKED")
        require(role == PERF_BUILD_ROLE, "A4 perf gate must use the release-probe build, not buildRole " + str(role))
        probe_hash = self.manifest.get("buildHashes", {}).get(PERF_BUILD_ROLE)
        require(isinstance(probe_hash, str) and DIGEST.fullmatch(probe_hash), "missing buildHashes.probe", "BLOCKED")
        root = self.path(step.get("perfRoot"))
        require(root.is_dir(), "missing perf directory", "BLOCKED")
        self.single_perf_root(root, step)
        ledger = self.ledger_rows(root, step)
        refs = step.get("attempts")
        require(isinstance(refs, list) and refs, "missing immutable perf attempt intake", "BLOCKED")
        anchored = {}
        for ref in refs:
            path, _ = self.file(ref)
            require(str(path) not in anchored, "duplicate perf attempt reference")
            anchored[str(path)] = ref
        seen, disk_ids = set(), {}
        keys = self.targets["probeKeys"]
        native_stall = self.targets.get("stallMs", GATE.STALL_MS)
        cells = {(tier, mode): {"tier": tier, "mode": mode, "windows": [], "discarded": [], "failedAttempts": []}
                 for tier in self.targets["tiers"] for mode in self.targets["modes"]}
        for directory in sorted(root.iterdir()):
            match = PERF.CELL.fullmatch(directory.name)
            if not match or not directory.is_dir():
                continue
            require(directory.resolve().is_relative_to(self.root), "perf directory escapes evidence root")
            require(match["tier"] in self.targets["tiers"] and match["mode"] in self.targets["modes"], "unknown perf cell")
            cell = cells[(match["tier"], match["mode"])]
            attempt_path = self.path(str((directory / "attempt.json").relative_to(self.root)))
            require(attempt_path.is_file(), "missing perf attempt", "BLOCKED")
            require(str(attempt_path) in anchored, "unanchored perf attempt", "BLOCKED")
            ref = anchored[str(attempt_path)]
            _, attempt = self.obj(ref)
            seen.add(str(attempt_path))
            attempt_id = attempt.get("attemptId")
            require(isinstance(attempt_id, str) and attempt_id, "perf attempt.json has no attemptId: " + directory.name, "BLOCKED")
            require(attempt_id not in disk_ids, "one attemptId in two perf directories: " + attempt_id)
            disk_ids[attempt_id] = directory.name
            row = ledger.get(attempt_id)
            if row is None or row.get("dir") != directory.name:
                continue    # reported below as the missing/orphan ledger set (BLOCKED)
            for field in ("status", "exitCode", "hostClean", "timedOut", "missingArtifacts"):
                if field in row and field in attempt:
                    require(row[field] == attempt[field], "ledger contradicts attempt.json " + field + ": " + directory.name)
            entry = {"dir": directory.name, "attemptId": attempt_id, "status": row.get("status"), "exitCode": row.get("exitCode"),
                     "hostClean": row.get("hostClean"), "timedOut": row.get("timedOut"), "missingArtifacts": row.get("missingArtifacts")}
            discard = self.discard_kind(row)
            if discard:
                # A discard is still FINAL/probe-bound evidence; a Player that actually ran keeps its guard proof.
                self.source(attempt)
                require(attempt.get("binarySha256") == probe_hash, "discarded perf attempt ran another binary: " + directory.name)
                mapping = attempt.get("artifactSha256")
                if mapping is not None:
                    require(isinstance(mapping, dict), "invalid native artifact hash map")
                    for name, digest in mapping.items():
                        self.file({"path": str((directory / name).relative_to(self.root)), "artifactSha256": digest})
                if discard == "host-contaminated":
                    self.guard(attempt.get("guard"))
                cell["discarded"].append(dict(entry, discard=discard))
                continue
            if not (row.get("status") == "complete" and row.get("hostClean") is True):
                cell["failedAttempts"].append(entry)
                continue
            proof = self.provenance(ref, attempt.get("exitCode"), PERF_BUILD_ROLE)
            require(proof.get("status") == "complete" and proof.get("hostClean") is True, "incomplete perf or dirty host")
            variant = [flag for flag in VARIANT_FLAGS if flag in self.command_argv(attempt_path, proof)]
            require(not variant, "a gate perf window carries " + "/".join(variant) + " (a B9/Z3 measurement, never A4): " + directory.name)
            self.guard(proof.get("guard"))
            for field in ("resolution", "activeEnemies", "warmupSeconds", "windowSeconds"):
                require(proof.get(field) == self.targets[field] and not isinstance(proof.get(field), bool), "perf scope missing or wrong: " + field, "BLOCKED")
            filename = keys["file"]
            mapping = proof.get("artifactSha256")
            require(isinstance(mapping, dict) and filename in mapping and "commandSha256" in proof, "missing native perf resource hashes", "BLOCKED")
            _, window = self.obj({"path": str((directory / filename).relative_to(self.root)), "artifactSha256": mapping[filename]})
            require(window.get(keys["tier"]) == self.targets["tiers"][match["tier"]]["probeValue"] and window.get(keys["mode"]) == PERF.MODE_NAMES[match["mode"]], "complete perf window ran wrong tier or mode")
            for field in ("mean", "p99Filtered", "frames", "stallsExcluded"):
                require(number(window.get(keys[field])) and window[keys[field]] >= 0, "invalid perf numeric sample")
            require(integer(window[keys["frames"]]) and window[keys["frames"]] > 0 and integer(window[keys["stallsExcluded"]]) and window[keys["stallsExcluded"]] <= window[keys["frames"]], "invalid perf frame counts")
            def native_file(name):
                require(name in mapping, "missing native perf resource: " + name, "BLOCKED")
                return {"path": str((directory / name).relative_to(self.root)), "artifactSha256": mapping[name]}
            csv_path, _ = self.file(native_file("frame-times-ms.csv"))
            samples, error = GATE.csv_metrics(csv_path)
            require(not error and len(samples) == window[keys["frames"]], "invalid CSV or frame count disagreement")
            # K9 cross-check only: the probe's native 33.33 ms fields must equal the CSV (probe integrity), they no
            # longer gate — the tier's own stallMs does, below.
            native_filtered = sorted(value for value in samples if value <= native_stall)
            require(window[keys["stallsExcluded"]] == len(samples) - len(native_filtered), "native stall count disagrees with CSV")
            require(bool(native_filtered) and abs(percentile(native_filtered, 0.99) - window[keys["p99Filtered"]]) <= 0.001, "CSV p99 disagrees with native window")
            require(abs(sum(samples) / len(samples) - window[keys["mean"]]) <= 0.001, "CSV mean disagrees with native window")
            # CSV is written to four decimals in milliseconds. Allow 0.1s for
            # window-boundary/serialization rounding, never a partial window.
            require(sum(samples) >= self.targets["windowSeconds"] * 1000 - 100, "partial CSV measurement window")
            _, scene = self.obj(native_file("01-scene-ready.json"))
            require(scene.get("scene") == "Run" and [scene.get("screenWidth"), scene.get("screenHeight")] == self.targets["resolution"], "wrong native perf scene/resolution")
            _, force = self.obj(native_file("02-force-40.json"))
            require(force.get("pass") is True and all(integer(force.get(key)) and force[key] == self.targets["activeEnemies"] for key in ("configuredSafetyCeiling", "ceiling", "forcedTarget", "achievedCount")), "native forced load not exact40")
            _, warm = self.obj(native_file("03-warmup.json"))
            require(warm.get("pass") is True and number(warm.get("warmupSeconds")) and warm["warmupSeconds"] >= self.targets["warmupSeconds"], "native warmup incomplete")
            require(window.get("pass") is True and number(window.get("windowSeconds")) and window["windowSeconds"] >= self.targets["windowSeconds"], "native perf window incomplete")
            require(all(integer(window.get(key)) and window[key] == self.targets["activeEnemies"] for key in ("activeMin", "activeMax", "liveBodiesMin", "liveBodiesMax", "crowdMemberMin", "crowdMemberMax", "loadIntegrityFloor")) and number(window.get("loadIntegrityFraction")) and window["loadIntegrityFraction"] == 1.0, "native window load not exact40")
            if match["mode"] != "topdown":
                require(window.get(keys["sweep"]) is True, "TP/FP camera sweep missing")
            metrics = window_metrics(samples, self.targets["tiers"][match["tier"]], tier_stall_ms(self.targets, match["tier"]),
                                     native_stall, self.targets["stallFractionMax"])
            cell["windows"].append(dict(entry, **metrics))
        require(seen == set(anchored), "perf attempt intake/disk mismatch")
        missing = sorted(disk_ids[key] for key in disk_ids if key not in ledger or ledger[key].get("dir") != disk_ids[key])
        orphan = sorted(row["dir"] + ":" + key for key, row in ledger.items() if key not in disk_ids or disk_ids[key] != row["dir"])
        rows = []
        for (tier, mode), cell in cells.items():
            limits = self.targets["tiers"][tier]
            counted = len(cell["windows"])
            rows.append(dict(cell, role=limits["role"], dec=limits.get("dec"), limitMeanMs=limits["meanMs"],
                             limitP99Ms=limits["p99FilteredMs"], stallMs=tier_stall_ms(self.targets, tier),
                             minWindows=limits["minWindows"], counted=counted, discards=len(cell["discarded"]),
                             failures=len(cell["failedAttempts"]),
                             pass_=counted >= int(limits["minWindows"]) and not cell["failedAttempts"] and all(w["pass"] for w in cell["windows"])))
        for row in rows:
            row["pass"] = row.pop("pass_")
        try:
            cross = PERF.report(root, self.targets)
            mine = {(row["tier"], row["mode"]): row["pass"] for row in rows}
            agrees = all(mine.get((row.get("tier"), row.get("mode"))) == row.get("pass") for row in cross.get("cells", []))
        except Exception as error:   # the helper is maintained separately; its verdict is a cross-check, not the gate
            cross, agrees = {"error": type(error).__name__ + ": " + str(error)}, False
        self.perf = {"schema": "dd-wp078-a4-v2", "buildRole": role, "binarySha256": probe_hash,
                     "root": str(root.relative_to(self.root)), "ledger": step["ledger"]["path"],
                     "pass": all(row["pass"] for row in rows if row["role"] == "gate"), "cells": rows,
                     "discards": [dict(d, tier=r["tier"], mode=r["mode"]) for r in rows for d in r["discarded"]],
                     "failedAttempts": [dict(d, tier=r["tier"], mode=r["mode"]) for r in rows for d in r["failedAttempts"]],
                     "escalations": [dict(tier=r["tier"], mode=r["mode"], dir=w["dir"], rawP99Ms=w["rawP99Ms"], limitP99Ms=w["limitP99Ms"])
                                     for r in rows for w in r["windows"] if w["rawP99OverLimit"]],
                     "ledgerMissing": missing, "ledgerOrphans": orphan,
                     "perfReportCrossCheck": {"agrees": agrees, "report": cross}}
        require(not missing and not orphan, "ledger/disk attempt mismatch — missing " + ", ".join(missing) + "; orphan " + ", ".join(orphan), "BLOCKED")
        failed = self.perf["failedAttempts"]
        require(not failed, "non-discardable perf attempts (K8: FAIL, to owner): " + ", ".join(d["dir"] + " " + str(d["status"]) + " exit=" + str(d["exitCode"]) for d in failed))
        bad = [w["dir"] for r in rows for w in r["windows"] if not w["pass"]]
        require(not bad, "a counted perf window failed its tier limit (tier stallMs, K9): " + ", ".join(bad))
        require(len(rows) == 9 and all(row["role"] == "gate" and row["pass"] for row in rows), "perf matrix incomplete: "
                + ", ".join(r["tier"] + "/" + r["mode"] + " " + str(r["counted"]) + "/" + str(r["minWindows"]) for r in rows if not r["pass"]), "BLOCKED")

    def A5(self, step):
        runs = step.get("runs")
        require(isinstance(runs, list) and len(runs) == 2, "stability seeds incomplete", "BLOCKED")
        seeds = []
        for run in runs:
            _, summary, _ = self.run(run, stability=True, step="A5")
            seeds.append(summary["seed"])
            self.soak.append(summary)
            self.timeout_margin.append(round_timeout_margin(summary))
        require(sorted(seeds) == [26, 61], "duplicate or missing stability seed")
        reports = step.get("reportRuns")
        require(isinstance(reports, list), "missing real-bot reports", "BLOCKED")
        paths, counts = set(), dict.fromkeys(("topdown", "tp", "fp"), 0)
        for run in reports:
            mode, summary, _ = self.run(run, report=True, step="A5-report")
            path = str(self.path(run["summary"]["path"]))
            require(path not in paths, "duplicate real-bot report")
            paths.add(path)
            counts[mode] += 1
            self.report_runs.append({"mode": mode, "summary": summary, "originalExit": run["originalExit"], "path": run["summary"]["path"]})
        require(all(counts[mode] >= minimum for mode, minimum in (("topdown", 5), ("tp", 2), ("fp", 2))), "real-bot report coverage incomplete", "BLOCKED")

    def A6(self, step):
        builds = step.get("builds")
        # K1: the release-probe build is mandatory — it carries the A4 perf gate.
        require(isinstance(builds, dict) and set(builds) == set(BUILD_MODES), "A6 needs dev, release and release-probe (probe) builds (C-00 30.09 K1)", "BLOCKED")
        hashes = self.manifest.get("buildHashes", {})
        require(set(hashes) == set(builds), "build role/hash coverage mismatch")
        natives = {}
        for role, build in builds.items():
            require(isinstance(build, dict), "invalid build receipt")
            proof = self.provenance(build.get("provenance"), build.get("originalExit"), role)
            self.bind(build["provenance"], proof, [build.get(key) for key in ("library", "binary", "buildReport", "report")])
            self.guard(build.get("guard"))
            require(proof.get("status") == "complete" and proof.get("guard") == build["guard"], "build completion/guard not bound")
            self.library(build.get("library"))
            self.file(build.get("binary"))
            require(build["binary"]["artifactSha256"] == hashes[role], "actual build binary differs from run hash")
            _, native = self.obj(build.get("buildReport"))
            stamp = native.get("stamp", {})
            short = stamp.get("commit")
            require(isinstance(short, str) and re.fullmatch(r"[0-9a-f]{12,40}", short) and self.commit.startswith(short) and stamp.get("stamped") is True, "build stamp missing, dirty or mixed")
            describe = stamp.get("describe")
            require(isinstance(describe, str) and describe not in ("", "unknown") and "-dirty" not in describe and stamp.get("mode") == native.get("mode"), "build describe/mode stamp dirty or unproven")
            require(native.get("mode") == BUILD_MODES[role] and native.get("result") == "Succeeded" and integer(native.get("totalErrors")) and native["totalErrors"] == 0,
                    "native build failed or wrong mode (" + role + " must be " + BUILD_MODES[role] + ")")
            size = native.get("totalSize")
            require(integer(size) and size > 0, "missing build footprint")
            _, smoke = self.obj(build.get("report"))
            for key in ("executionCommit", "head", "sourceCommit", "testedCommit"):
                if key in smoke:
                    require(smoke[key] == self.commit, "mixed smoke " + key)
            require(smoke.get("ok") is True, "build smoke failed")
            if role == "release":
                require(proof.get("compression") == "LZ4HC" and size <= 5_000_000_000, "release compression or 5GB cap failed")
            natives[role] = native
        self.parity(natives)

    def parity(self, natives):
        """K1 parity evidence from the native build reports: release-probe differs from release only by DD_PERF_PROBE —
        Development off, LZ4HC, same enabled scene list, same scripting backend, same target. A field the native
        report does not carry is BLOCKED (named), never assumed from the build mode."""
        release, probe = natives["release"], natives["probe"]
        failures, missing, rows = [], [], {}
        for role, native in (("release", release), ("probe", probe)):
            compression = native.get("compression")
            rows[role + ".compression"] = compression
            if compression is None:
                missing.append(role + ".compression")
            elif not (isinstance(compression, str) and compression.lower() == "lz4hc"):
                failures.append(role + " compression " + str(compression) + " is not LZ4HC")
            development = native.get("development")
            rows[role + ".development"] = development
            if development is None:
                missing.append(role + ".development")
            elif development is not False:
                failures.append(role + " is a Development build")
        if release.get("target") != probe.get("target"):
            failures.append("release/probe target differ")
        counts = (release.get("scenes"), probe.get("scenes"))
        rows["scenes"] = list(counts)
        if all(integer(value) for value in counts) and counts[0] != counts[1]:
            failures.append("release/probe enabled scene counts differ " + str(counts))
        for field in ("sceneList", "scriptingBackend"):
            values = (release.get(field), probe.get(field))
            rows[field] = list(values)
            absent = [role + "." + field for role, value in zip(("release", "probe"), values) if value is None]
            if absent:
                missing.extend(absent)
            elif field == "sceneList" and not all(scene_entries(v) is not None for v in values):
                failures.append("native sceneList is not a nonempty list of scene paths ({path, guid} entries)")
            elif field == "scriptingBackend" and not all(isinstance(v, str) and v for v in values):
                failures.append("native scriptingBackend is not a string")
            elif field == "sceneList" and scene_entries(values[0]) != scene_entries(values[1]):
                failures.append("release/probe sceneList differ (build order, path + guid)")
            elif field == "scriptingBackend" and values[0] != values[1]:
                failures.append("release/probe " + field + " differ")
        self.build_parity = {"pass": not failures and not missing, "failures": failures, "missingNativeFields": missing, "observed": rows}
        require(not failures, "release-probe parity failed (K1): " + "; ".join(failures))
        require(not missing, "native build report lacks parity field(s) (K1): " + ", ".join(missing), "BLOCKED")

    def world_measure(self):
        """K6: A3 `world` = same-spot stalls ≥ 5 s (tools/qa/stuck_clusters.py) in every A2 and A5 stability Player log.
        A cluster is FAIL (C-00 30.09: package gate); a missing or unmeasurable log is NOT_RUN ("ölçülmedi"), never PASS.
        Real-bot report runs are measured and listed, not gated."""
        gated = [row for row in self.world_logs if row["gate"]]
        runs = [{"step": row["step"], "log": row["log"], "cameraMode": row["cameraMode"], "seed": row["seed"], "gate": row["gate"],
                 "measurable": row["measure"]["measurable"], "clusters": row["measure"]["clusters"],
                 "reason": row["measure"].get("reason")} for row in self.world_logs]
        clusters = [dict(cluster, log=row["log"], step=row["step"]) for row in gated for cluster in row["measure"]["clusters"]]
        unmeasured = [row["log"] for row in gated if not row["measure"]["measurable"]]
        if clusters:
            status, reason = "FAIL", "same-spot stall ≥ 5 s in " + ", ".join(sorted({c["log"] for c in clusters}))
        elif unmeasured or len(gated) < WORLD_LOGS_EXPECTED:
            status = "NOT_RUN"
            reason = "ölçülmedi: " + (("unmeasurable log " + ", ".join(unmeasured)) if unmeasured else
                                      str(len(gated)) + "/" + str(WORLD_LOGS_EXPECTED) + " A2/A5 logs inspected")
        else:
            status, reason = "PASS", None
        return {"status": status, "reason": reason, "measure": "stuck_clusters min_seconds=5 radius=1.0",
                "clusters": clusters, "runs": runs}

    def A7(self, step):
        require(step.get("byteEqual") is True, "user data restoration not byte equal")
        self.xml(step.get("integrityXml"), integrity=True)

    def R2(self, step):
        """WP-091 (C-00 05.10): the R2 gates of tools/qa/r2-gates.json, each from its bound measurement / review
        artifact and the bound NUnit XMLs (required test identities). Returns the gate rows; never raises on a gate."""
        contract = R2.load_contract((HERE / "r2-gates.json").read_text(encoding="utf-8"))
        step = step if isinstance(step, dict) else {}
        cases = None
        refs = step.get("xml")
        if isinstance(refs, list) and refs:
            cases = {}
            for ref in refs:
                _, data = self.file(ref, read=True)
                root = ET.fromstring(data)
                for case in root.iter("test-case"):
                    name = (case.get("fullname") or case.get("name") or "").split("(", 1)[0]
                    if name:
                        cases[name] = case.get("result") or "Unknown"
        bound = step.get("gates") if isinstance(step.get("gates"), dict) else {}
        rows = []
        for gate in contract["gates"]:
            entry = bound.get(gate["id"]) if isinstance(bound.get(gate["id"]), dict) else {}
            measure = self.obj(entry["measure"])[1] if isinstance(entry.get("measure"), dict) else None
            review = self.obj(entry["review"])[1] if isinstance(entry.get("review"), dict) else None
            rows.append(R2.evaluate_gate(gate, self.commit, measure=measure, review=review, cases=cases))
        return rows


def generate_report(manifest, evidence_root, targets=None):
    """Return a fail-closed report. Only complete, verified FINAL evidence passes."""
    if targets is None:
        targets = strict_json((HERE / "perf-targets.json").read_bytes())
    manifest = manifest if isinstance(manifest, dict) else {}
    inspector = Inspector(manifest, evidence_root, targets)
    rows, failures = {}, []
    def issue(step, owner, error):
        failures.append({"step": step, "owner": owner, "status": error.status, "reason": str(error)})
    try:
        require(manifest.get("schema") == SCHEMA, "invalid manifest schema")
        require(isinstance(manifest.get("commit"), str) and SHA.fullmatch(manifest["commit"]), "missing full FINAL commit")
        require(manifest.get("final") is True, "FINAL SHA not assigned", "BLOCKED")
        # Optional test injection may tighten targets but cannot relax live DEC rules.
        canonical = strict_json((HERE / "perf-targets.json").read_bytes())
        require(targets == canonical, "targets differ from current authoritative perf-targets")
    except ProofError as error:
        issue("A8", "S-10", error)
    checklist = manifest.get("checklist")
    checklist = checklist if isinstance(checklist, dict) else {}
    for index in range(8):
        name = "A" + str(index)
        step = checklist.get(name)
        owner = step.get("owner", "UNKNOWN") if isinstance(step, dict) else "UNKNOWN"
        owner = owner if isinstance(owner, str) and owner else "UNKNOWN"
        row = {"owner": owner, "reportedStatus": step.get("status") if isinstance(step, dict) else "NOT_RUN", "status": "PASS"}
        if isinstance(step, dict):
            row.update({key: step.get(key) for key in ("executionCommit", "originalExit", "guard", "provenance")})
        try:
            inspector.common(step, guarded=index != 0)
            getattr(inspector, name)(step)
        except (ProofError, OSError, ValueError, TypeError, KeyError, AttributeError) as error:
            proof_error = error if isinstance(error, ProofError) else ProofError("invalid evidence: " + str(error))
            row["status"] = proof_error.status
            row["reason"] = str(proof_error)
            failure_owner = inspector.active_owner if name == "A3" else owner
            row["failureOwner"] = failure_owner
            issue(name, failure_owner, proof_error)
        rows[name] = row
    # K6: the A3 world area is measured on the A2/A5 logs, so it is folded into A3 after those steps ran.
    world = inspector.world_measure()
    checks = checklist.get("A3", {}).get("checks") if isinstance(checklist.get("A3"), dict) else None
    world_owner = checks.get("world", {}).get("owner") if isinstance(checks, dict) and isinstance(checks.get("world"), dict) else None
    world_owner = world_owner if isinstance(world_owner, str) and world_owner else "UNKNOWN"
    # K4: the six specification flags with no C# parser are listed on the aiFlags row (retired / withdrawn /
    # KNOWN OPEN) — never PASS.
    not_pass = [{"flag": flag, "status": status, "label": label, "reason": NOT_IN_CODE_REASON, "pass": False}
                for flag, (status, label) in AI_FLAGS_NOT_PASS.items()]
    rows["A3"]["areas"] = {"world": world, "aiFlags": {"flags": list(AI_FLAGS), "flagRuns": 2 * len(AI_FLAGS), "notPass": not_pass}}
    if world["status"] != "PASS":
        world_status = "FAIL" if world["status"] == "FAIL" else "BLOCKED"
        issue("A3", world_owner, ProofError("A3 world " + world["status"] + ": " + world["reason"], world_status))
        if rows["A3"]["status"] == "PASS" or world_status == "FAIL":
            rows["A3"]["status"] = world_status
            rows["A3"]["reason"] = "A3 world " + world["status"] + ": " + world["reason"]
            rows["A3"]["failureOwner"] = world_owner
    # WP-091: an R2 manifest also carries the R2 gates (tools/qa/r2-gates.json); an R1 manifest has none
    if manifest.get("round") == "R2":
        try:
            r2_rows = inspector.R2(checklist.get("R2"))
            r2_status = R2.summarize(r2_rows)
            rows["R2"] = {"owner": "S-10", "status": r2_status, "gates": r2_rows}
            for gate_row in r2_rows:
                if gate_row["kind"] != "report" and gate_row["status"] in ("FAIL", "BLOCKED"):
                    issue("R2." + gate_row["id"], gate_row["owner"], ProofError(gate_row["id"] + " " + gate_row["title"] + ": " + str(gate_row["reason"]), gate_row["status"]))
        except (ProofError, OSError, ValueError, TypeError, KeyError) as error:
            proof_error = error if isinstance(error, ProofError) else ProofError("invalid R2 evidence: " + str(error))
            rows["R2"] = {"owner": "S-10", "status": proof_error.status, "reason": str(proof_error)}
            issue("R2", "S-10", proof_error)
    try:
        for ref in list(inspector.evidence.values()):
            inspector.file(ref)
    except (ProofError, OSError) as error:
        issue("A8", "S-10", error if isinstance(error, ProofError) else ProofError("evidence changed during inspection: " + str(error)))
    passed = not failures
    status = "PASS" if passed else ("FAIL" if any(row["status"] == "FAIL" for row in failures) else "BLOCKED")
    rows["A8"] = {"owner": "S-10", "status": status, "derived": True}
    return {"schema": SCHEMA, "commit": manifest.get("commit"), "final": manifest.get("final") is True,
            "pass": passed, "status": status, "checklist": rows, "failures": failures,
            "buildHashes": manifest.get("buildHashes", {}), "perf": inspector.perf,
            "soak": inspector.soak, "soakRoundTimeoutMargin": inspector.timeout_margin, "reportRuns": inspector.report_runs, "botRuns": inspector.bot_runs, "routeRuns": inspector.route_runs,
            "buildParity": inspector.build_parity,
            "evidence": sorted(inspector.evidence.values(), key=lambda row: row["path"])}


def round_timeout_margin(summary):
    """K5 report line: the slowest round of a stability run and its margin to the 420 s round timeout (not a gate)."""
    seconds = [r.get("seconds") for r in summary.get("rounds") or [] if isinstance(r, dict) and isinstance(r.get("seconds"), (int, float))]
    slowest = max(seconds) if seconds else None
    return {"seed": summary.get("seed"), "qualityTier": summary.get("qualityTier"), "timeoutSeconds": ROUND_TIMEOUT_SECONDS,
            "slowestRoundSeconds": slowest,
            "marginSeconds": None if slowest is None else round(ROUND_TIMEOUT_SECONDS - slowest, 1),
            "marginFraction": None if slowest is None else round(1.0 - slowest / ROUND_TIMEOUT_SECONDS, 3)}


def scene_entries(value):
    """Native sceneList in build order as [(path, guid)] — DdBuild writes [{path, guid}] (S-01 06b7d19c, WP-079G A6);
    a plain path list (older reports) maps to guid None. None when the value is not a nonempty list of valid entries."""
    if not isinstance(value, list) or not value:
        return None
    entries = []
    for item in value:
        if isinstance(item, str) and item:
            entries.append((item, None))
        elif (isinstance(item, dict) and isinstance(item.get("path"), str) and item["path"]
              and isinstance(item.get("guid"), str) and re.fullmatch(r"[0-9a-f]{32}", item["guid"])):
            entries.append((item["path"], item["guid"]))
        else:
            return None
    return entries


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--evidence-root", required=True)
    parser.add_argument("--out", required=True)
    try:
        args = parser.parse_args(argv)
        manifest = strict_json(Path(args.manifest).read_bytes())
        if not isinstance(manifest, dict) or not Path(args.evidence_root).is_dir():
            raise ValueError("manifest must be an object and evidence root must exist")
        out = Path(args.out)
        # Reserve one new directory, then exclusive-create each report. No overwrite.
        out.mkdir(parents=True, exist_ok=False)
        result = generate_report(manifest, args.evidence_root)
        text = ["WP-078 " + result["status"] + " " + str(result["commit"])]
        text += [name + " " + row["status"] + " owner=" + row["owner"] for name, row in result["checklist"].items()]
        with (out / "wp078-report.json").open("x", encoding="utf-8") as handle:
            handle.write(json.dumps(result, indent=2, allow_nan=False) + "\n")
        with (out / "wp078-report.txt").open("x", encoding="utf-8") as handle:
            handle.write("\n".join(text) + "\n")
        print(text[0])
        return 0 if result["pass"] else 1
    except SystemExit as error:
        return 0 if error.code == 0 else 64
    except (OSError, ValueError, TypeError) as error:
        print("WP-078 input/output error: " + str(error), file=sys.stderr)
        return 64


if __name__ == "__main__":
    raise SystemExit(main())
