#!/usr/bin/env python3
"""Inspect source-bound WP-078 evidence; never execute a gate or a game."""
import argparse
import hashlib
import importlib.util
import json
import math
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
AI_FLAGS = ("-ddAiV3", "-ddRagdoll", "-ddCorpseLinger", "-ddChainHops",
            "-ddGelPuddles", "-ddCrowdNeighbours", "-ddSpawnLos")


def _helper(name):
    spec = importlib.util.spec_from_file_location("_a8_" + name, HERE / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


PERF = _helper("wp078_perf_report")
SOAK = _helper("soak_gate")
GATE = _helper("wp064_gate")


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
        self.report_runs = []
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
        for field in ("snapshotExit", "restoreExit", "outOfScopeChanged"):
            require(integer(data.get(field)) and data[field] == 0, "guard " + field + " is not verified zero")
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

    def provenance(self, ref, original, role=None, balance=False, executed=True):
        path, data = self.obj(ref)
        self.source(data)
        self.original_exit(original, (0, 1) if balance else (0,))
        if executed:
            require("exitCode" in data, "missing native original exitCode", "BLOCKED")
            if "timedOut" in data:
                require(data["timedOut"] is False, "native attempt timed out or has invalid timeout metadata")
        if "exitCode" in data:
            require(integer(data["exitCode"]) and data["exitCode"] == original, "native exit contradicts original exit")
        if "status" in data:
            allowed = (("complete",) if original == 0 else ("failed",)) if balance else ("complete",)
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
        require(counts["total"] > 0 and counts["passed"] == counts["total"] == len(leaves), "empty or inconsistent NUnit counts")
        require(all(counts[key] == 0 for key in ("failed", "skipped", "inconclusive")), "NUnit failed/skipped/inconclusive")
        require(root.get("result") == "Passed" and all(case.get("result") == "Passed" for case in leaves), "NUnit leaf did not pass")
        names = [case.get("fullname") or case.get("name") for case in leaves]
        require(all(names) and len(set(names)) == len(names), "missing or duplicate NUnit test identity")
        if integrity:
            require(any("ProjectIntegrityTests." in name for name in names), "no actual ProjectIntegrityTests leaf")
        if identities is not None:
            # NUnit parameterized instances append arguments to the same native
            # declared method; never allow an arbitrary class or invented method.
            observed = {name.split("(", 1)[0] for name in names}
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

    def run(self, run, stability=False, report=False):
        require(isinstance(run, dict), "missing physical run", "BLOCKED")
        proof = self.provenance(run.get("provenance"), run.get("originalExit"), run.get("buildRole", "dev"), balance=report)
        self.bind(run["provenance"], proof, [run.get("summary"), run.get("log"), run.get("frames")])
        self.guard(run.get("guard"))
        require(proof.get("guard") == run["guard"], "run guard not bound to execution")
        require(proof.get("status") == ("failed" if report and run.get("originalExit") == 1 else "complete"), "missing native run completion", "BLOCKED")
        _, summary = self.obj(run.get("summary"))
        log, data = self.file(run.get("log"), read=True)
        require(bool(data.strip()), "empty Player log", "BLOCKED")
        require(not SOAK.log_failures(log), "runtime error in Player log")
        hits = self.required_log_failures(data)
        require(not hits, "WP-078 required runtime log failure: " + (hits[0][:240] if hits else ""))
        require(all(field not in summary or summary[field] is False for field in ("timedOut", "artifactWriteFailed")), "run timed out, artifacts failed or invalid native boolean")
        mode = MODES.get(summary.get("cameraMode"))
        require(mode is not None, "unknown camera mode")
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
            require(run["originalExit"] == (0 if summary["pass"] else 1), "real bot verdict contradicts original exit")
            require(summary["pass"] or summary["deaths"] > 0, "failed real bot is not a documented balance death")
            return mode, summary, proof
        count = 12 if stability else 5
        records = summary.get("rounds")
        require(isinstance(records, list) and all(isinstance(record, dict) for record in records), "missing ordered rounds")
        require(all(integer(summary.get(key)) for key in ("roundsRequested", "roundsCompleted")) and all(integer(record.get("round")) for record in records), "non-integer round counts")
        require(SOAK.exact_rounds(summary, count)[0], "incomplete or unordered rounds")
        require(integer(summary.get("seed")), "missing run seed")
        if stability:
            require(mode == "topdown" and summary.get("qualityTier") == "Yuksek" and summary["seed"] in (61, 26), "wrong DEC-091 stability scope")
            require(proof.get("worldHalfExtent") == 55 and number(proof.get("worldHalfExtent")) and proof.get("hostClean") is True, "missing stability world/host proof")
            require(summary.get("runMode") == "stability" and summary.get("stabilityPass") is True, "stability did not pass")
            require(integer(summary.get("stuckRecoveries")) and summary["stuckRecoveries"] == 0, "stability stuck counter missing or nonzero")
            for record in records:
                require(all(number(record.get(key)) and record[key] >= 0 for key in ("memMonoUsedMB", "memTotalAllocatedMB", "memRssMB")), "missing or non-finite memory sample")
            require(SOAK.memory_checks(records)[0], "stability memory limit failed")
        else:
            require(summary["seed"] == 61 and summary.get("runMode") == "normal" and summary.get("pass") is True, "wrong normal-bot scope or verdict")
            require(integer(summary.get("deaths")) and summary["deaths"] == 0, "normal bot death")
            require(integer(summary.get("stuckRecoveries")) and summary["stuckRecoveries"] == 0, "stuck counter missing or nonzero")
            camera = re.findall(r"\[Camera\] inside-collider round=(\d+)[^\n]*?frames=(\d+)/(\d+)", data.decode("utf-8", errors="replace"))
            require(len(camera) == 5 and sorted(int(row[0]) for row in camera) == list(range(1, 6)) and all(int(row[1]) == 0 and int(row[2]) > 0 for row in camera), "camera inside-collider proof incomplete or failed")
            frames = run.get("frames")
            require(isinstance(frames, dict) and {"courtyard", "corridor", "horizon"}.issubset(frames), "missing fixed-route evidence frames", "BLOCKED")
            unique = set()
            for point, ref in frames.items():
                path, image = self.file(ref, read=True)
                require(png_complete(image), "invalid PNG evidence: " + point)
                require(str(path) not in unique, "same evidence frame reused for multiple route points")
                unique.add(str(path))
        return mode, summary, proof

    def A0(self, step):
        self.library(step.get("library"))

    def A1(self, step):
        refs = step.get("xml", {})
        for kind in ("editMode", "playMode"):
            self.xml(refs.get(kind))

    def A2(self, step):
        runs = step.get("runs")
        require(isinstance(runs, list) and len(runs) == 3, "missing three normal-bot modes", "BLOCKED")
        require(sorted(self.run(run)[0] for run in runs) == ["fp", "topdown", "tp"], "duplicate or missing normal-bot mode")

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
        require(isinstance(runs, list) and len(runs) == 2 * len(AI_FLAGS), "missing seven flags x0/1 native Player smoke matrix", "BLOCKED")
        seen, execution_paths = set(), set()
        for run in runs:
            require(isinstance(run, dict) and run.get("flag") in AI_FLAGS and integer(run.get("value")) and run["value"] in (0, 1), "invalid native flag smoke identity")
            key = (run["flag"], run["value"])
            require(key not in seen, "duplicate native flag smoke")
            seen.add(key)
            execution_path = str(self.path(run.get("provenance", {}).get("path")))
            require(execution_path not in execution_paths, "one native execution reused across flag smoke rows")
            execution_paths.add(execution_path)
            proof = self.provenance(run.get("provenance"), run.get("originalExit"), run.get("buildRole", "dev"))
            self.guard(run.get("guard"))
            require(proof.get("status") == "complete" and proof.get("guard") == run["guard"], "flag run completion/guard unproven")
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

    def A4(self, step):
        root = self.path(step.get("perfRoot"))
        require(root.is_dir(), "missing perf directory", "BLOCKED")
        refs = step.get("attempts")
        require(isinstance(refs, list) and refs, "missing immutable perf attempt intake", "BLOCKED")
        anchored = {}
        for ref in refs:
            path, _ = self.file(ref)
            require(str(path) not in anchored, "duplicate perf attempt reference")
            anchored[str(path)] = ref
        seen = set()
        role = step.get("buildRole", "dev")
        for directory in sorted(root.iterdir()):
            match = PERF.CELL.fullmatch(directory.name)
            if not match:
                continue
            require(directory.resolve().is_relative_to(self.root), "perf directory escapes evidence root")
            require(match["tier"] in self.targets["tiers"] and match["mode"] in self.targets["modes"], "unknown perf cell")
            attempt_path = self.path(str((directory / "attempt.json").relative_to(self.root)))
            require(attempt_path.is_file(), "missing perf attempt", "BLOCKED")
            require(str(attempt_path) in anchored, "unanchored perf attempt", "BLOCKED")
            ref = anchored[str(attempt_path)]
            _, attempt = self.obj(ref)
            seen.add(str(attempt_path))
            require(isinstance(attempt, dict), "invalid perf attempt")
            proof = self.provenance(ref, attempt.get("exitCode"), role)
            require(proof.get("status") == "complete" and proof.get("hostClean") is True, "incomplete perf or dirty host")
            self.guard(proof.get("guard"))
            for field in ("resolution", "activeEnemies", "warmupSeconds", "windowSeconds"):
                require(proof.get(field) == self.targets[field] and not isinstance(proof.get(field), bool), "perf scope missing or wrong: " + field, "BLOCKED")
            filename = self.targets["probeKeys"]["file"]
            mapping = proof.get("artifactSha256")
            require(isinstance(mapping, dict) and filename in mapping and "commandSha256" in proof, "missing native perf resource hashes", "BLOCKED")
            _, window = self.obj({"path": str((directory / filename).relative_to(self.root)), "artifactSha256": mapping[filename]})
            keys = self.targets["probeKeys"]
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
            require(GATE.stall_exclusion(window, samples)[0], "CSV stall exclusion disagrees or exceeds cap")
            filtered = sorted(value for value in samples if value <= self.targets["stallMs"])
            require(bool(filtered), "no non-stall CSV frames")
            mean = sum(samples) / len(samples)
            p99 = filtered[min(len(filtered) - 1, math.ceil(0.99 * len(filtered)) - 1)]
            require(abs(mean - window[keys["mean"]]) <= 0.001 and abs(p99 - window[keys["p99Filtered"]]) <= 0.001, "CSV mean/p99 disagree with native window")
            limits = self.targets["tiers"][match["tier"]]
            require(mean <= limits["meanMs"] and p99 <= limits["p99FilteredMs"], "raw CSV performance limit failed")
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
        require(seen == set(anchored), "perf attempt intake/disk mismatch")
        self.perf = PERF.report(root, self.targets)
        require(len(self.perf["cells"]) == 9 and all(row["role"] == "gate" and row["pass"] for row in self.perf["cells"]), "perf matrix incomplete or a window failed")

    def A5(self, step):
        runs = step.get("runs")
        require(isinstance(runs, list) and len(runs) == 2, "stability seeds incomplete", "BLOCKED")
        seeds = []
        for run in runs:
            _, summary, _ = self.run(run, stability=True)
            seeds.append(summary["seed"])
            self.soak.append(summary)
        require(sorted(seeds) == [26, 61], "duplicate or missing stability seed")
        reports = step.get("reportRuns")
        require(isinstance(reports, list), "missing real-bot reports", "BLOCKED")
        paths, counts = set(), dict.fromkeys(("topdown", "tp", "fp"), 0)
        for run in reports:
            mode, summary, _ = self.run(run, report=True)
            path = str(self.path(run["summary"]["path"]))
            require(path not in paths, "duplicate real-bot report")
            paths.add(path)
            counts[mode] += 1
            self.report_runs.append({"mode": mode, "summary": summary, "originalExit": run["originalExit"], "path": run["summary"]["path"]})
        require(all(counts[mode] >= minimum for mode, minimum in (("topdown", 5), ("tp", 2), ("fp", 2))), "real-bot report coverage incomplete", "BLOCKED")

    def A6(self, step):
        builds = step.get("builds")
        require(isinstance(builds, dict) and {"dev", "release"}.issubset(builds) and set(builds).issubset({"dev", "release", "probe"}), "missing dev/release build", "BLOCKED")
        hashes = self.manifest.get("buildHashes", {})
        require(set(hashes) == set(builds), "build role/hash coverage mismatch")
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
            require(native.get("mode") == ("dev" if role == "probe" else role) and native.get("result") == "Succeeded" and integer(native.get("totalErrors")) and native["totalErrors"] == 0, "native build failed or wrong mode")
            size = native.get("totalSize")
            require(integer(size) and size > 0, "missing build footprint")
            _, smoke = self.obj(build.get("report"))
            for key in ("executionCommit", "head", "sourceCommit", "testedCommit"):
                if key in smoke:
                    require(smoke[key] == self.commit, "mixed smoke " + key)
            require(smoke.get("ok") is True, "build smoke failed")
            if role == "release":
                require(proof.get("compression") == "LZ4HC" and size <= 5_000_000_000, "release compression or 5GB cap failed")

    def A7(self, step):
        require(step.get("byteEqual") is True, "user data restoration not byte equal")
        self.xml(step.get("integrityXml"), integrity=True)


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
            "soak": inspector.soak, "reportRuns": inspector.report_runs,
            "evidence": sorted(inspector.evidence.values(), key=lambda row: row["path"])}


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
