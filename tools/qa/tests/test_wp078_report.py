"""Independent synthetic A8 acceptance tests; no game/runtime execution."""
import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import struct
import inspect
import os
import zlib
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]   # tools/qa (C-00 30.09 M-15: the test lives in tools/qa/tests so discover finds it)
FINAL = 'a' * 40
OTHER = 'b' * 40
AREAS = 'projectile penetration launcher arsenal gel chain ragdoll zombieAttacks animation camera world aiFlags progression audio ui readability'.split()
DOMAIN_IDS = {'projectile': ['DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.HizliMermi_InceDuvardan_Gecemez_TekUzunAdimdaBile', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.KlasikSilah_Projectile_Atar_VeHedefiVurur', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermi_UcusSuresindenSonraHasarVerir_TekIsabet', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermiler_UcarkenVeCarparken_KareBasinaAyirmaYok'], 'penetration': ['DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitliCollider_RegistryOnce', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitsizCollider_AcikFallback', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventClassifier_OnSinifTablosu', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventMeta_BeforeAfterVeKirilim', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageTransaction_MonotonVeIcIceSayilir', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.IsEmpty_YalnizHicbirSeyOlmadiginda', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.KaynakTaramasi_HasarYolundaKareBasinaAramaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kirilma_Idempotent_IkinciCagriKirmaz', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kritik_YalnizAcikKafaDogrudan_PatlamadaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.OluBeden_IkinciOlumVeKirilmaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Parca_YalnizKendiBolgesiniKorur', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_EnjekteOranlar_ToplamKorunurDeterministik', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_IkiParca_IkiPayZirhaAcikPayCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_KirilmaTasmasi_AyniPatlamadaCanaGecmez', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_TekParca_KorunanPayZirhaAcikPaylarCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_UcParca_SonraBiriKirik_AcikPayCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Tasma_HerSlottaCanaGecmez', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.AyniTransaction_IkinciUygulama_Reddedilir', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.DogrudanIsabet_GercekHitbox_BolgeSlotYonlendirmesi', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.IkinciRunSahnesi_HasarAboneleriCogalmaz', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Kompozisyon0123_Deterministik_HavuzIkinciYasam_Pause', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.OluDusman_GecHasar_YeniOlumGoldKillYok_StandardKill', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_AyriCanYoluYok_BirincilHedefTekOlay', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_DusmanBasinaTekUygulama_TekDressing_ButceCogalmaz', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.PatlamaylaOlum_TekOlum_KritikDegil', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.VurusYolu_SifirAyirma'], 'launcher': ['DD.Prototype.Tests.EditMode.RocketMathTests.Patlama_DogrusalDusus_VeKendineHasar', 'DD.Prototype.Tests.EditMode.RocketMathTests.Rl1_Etkin_RoketBlogu_VeDallari', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Cluster_Bombaciklari_PatlamaEtrafinda', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.DogrudanIsabet_VePatlama_AyriIslemde_HedefIkisiniDeAlir', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Kontrolcu_Rl1_RoketAtar', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.KurmaMesafesiIcinde_Dud_30Hasar_PatlamaYok', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Oyuncu_KendiPatlamasindan_Yuzde45_Alir', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Ucus_Itki_VeHavadaPatlama_70m'], 'arsenal': ['DD.Prototype.Tests.EditMode.ArsenalB2Tests.Burst_UcAtis_005Aralik_SonraGecikme026', 'DD.Prototype.Tests.EditMode.ArsenalB2Tests.TekFisek_KabukKabukDoler_TetikKeser_YuklenenlerKalir', 'DD.Prototype.Tests.EditMode.ArsenalB2Tests.Yedek_TurBasiDolar_AsagiInmez_BosKalincaTabanBirSarjor', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Katalog_OnTanim_Gecerli_KimliklerBenzersiz', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.KonfigKopyasi_TanimSayilariniTasir_KonseySayilari', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Egri_TabandanBaslar_MonotonArtar_TavandaSabitlenir', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.GecSinir_PencereUstundenBuyuk_RoundBitisindenKucuk', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IkinciSpawn_ConfigtenOkunur_IhtimalAralikta', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanItibaren_IhtimalPozitif_GarantiDegil', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanOnce_IhtimalSifir', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Nobetci_KasitliSapmayi_Yakalar', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.RoundKoprusu_IlerlemeyiKirpar_SaglayiciYokkenBagliDegil', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SabitTazelik_SpecialWeaponConfig_SevkiyatVarsayilanlaAyni', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SpawnPenceresi_RoundIcinde_BaslangicVeBitisDisinda'], 'gel': ['DD.Prototype.Tests.EditMode.GelStateTests.DeganlerinSuresiEsitlenir_Tavan_EnEskiSoner_Expire', 'DD.Prototype.Tests.EditMode.GelStateTests.Icerisi_YukseklikBandi_EnYeniKazanir', 'DD.Prototype.Tests.PlayMode.GelSliceTests.Jel_KayarDuserOlur_Kaldirma0_UltraKalkar_Devrilme'], 'chain': ['DD.Prototype.Tests.EditMode.ChainTargetTests.KalkanKayanAtlanir_UltraZinciriDevamEttirir_Duvar', 'DD.Prototype.Tests.EditMode.ChainTargetTests.Sira_EnYakin_HopYaricapi_Tavan_AramaSiniriYok'], 'ragdoll': ['DD.Art.Tests.PlayMode.AnimationRagdollTests.Havuz_200Dongu_SizintiYok', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.Olum_ItkiYonundeDuser_Yerlesir_PozKalir_HavuzaDoner', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.SahneDegisimi_DusenRagdoll_HatasizBirakilir', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.Tavan_Dolunca_YaziliDusus_HepsiBiter_SonraYenidenDogar'], 'zombieAttacks': ['DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.BiteGrabKapisi_ArkaDonukVeyaDuran_TekKapma', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.Inceleme_R1A_SertMenzil_AboneKorunur_AgirlikYonetimOncesi', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.SlotTahtasi_AgirStillerTekil_WindupTavani_Yayilim', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.TierIzinleri_ImzaStilNormalizasyonu', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.UcKezAyniStilYok_ElDegisir_Deterministik', 'DD.Prototype.Tests.PlayMode.AttackVarietySliceTests.AltiStandard_YirmiSaniye_StilCesitliligi_UcTekrarYok_WindupTavani_TekKapma'], 'animation': ['DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz', 'DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_DonusteIceEgilir_DuzdeDik', 'DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_KadansBandi_KalabalikFarkliFaz_Kayar'], 'camera': ['DD.Prototype.Tests.PlayMode.CameraContractTests.KameraYawIle_HareketVeAnalogNisan_KameraTabaninaGore', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.DuvarIciSayaci_PozitifKontrol', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.TP_DuvarItmesi_KameraAslaDuvarIcindeDegil', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.Tepeden_NisanIsiniEskiDuzlemIzdusumuyleAyni'], 'world': ['DD.Art.Tests.EditMode.NavMeshConnectivityTests.KopruGuvertesi_BedenKoridoruAcik_CitSokulmez_EtekBirlesik', 'DD.Art.Tests.EditMode.NavMeshConnectivityTests.NavMesh_TekParca_BedenErisilebilirAdacikYok_IsaretliNoktalarBaslangicaUlasir', 'DD.Art.Tests.EditMode.WorldBoundaryTests.HavuzCitleri_YurunebilirNavMeshiKesmez_BedenAdimiUstuPropUstundeNavMeshYok', 'DD.Art.Tests.EditMode.WorldBoundaryTests.Havuzlar_KenardanIceriKapsulTaramasi_CitVeyaGecitteDurur', 'DD.Art.Tests.EditMode.WorldBoundaryTests.OynanabilirAlan_ZeminDelikleriYalnizHavuzlarda', 'DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_CevreBoyuncaKapsulTaramasi_BoslukYok', 'DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_DortDuvar_IgnoreRaycast_OyuncuVeDusmaniDurdurur_AtisVeKamerayiEtkilemez'], 'aiFlags': ['DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil'], 'progression': ['DD.Prototype.Tests.EditMode.MetaPathTests.AnlikGoruntu_S09', 'DD.Prototype.Tests.EditMode.MetaPathTests.Katalog_Gecerli_Sayfalar_ve_Silahlar', 'DD.Prototype.Tests.EditMode.MetaPathTests.KayitHatasi_RamDegismez', 'DD.Prototype.Tests.EditMode.MetaPathTests.KosuDegistiricileri_DukkanVeAgac', 'DD.Prototype.Tests.EditMode.MetaPathTests.Respec_SilahlarKalir_IlkTam_SonraYuzde75', 'DD.Prototype.Tests.EditMode.MetaPathTests.SilahKilidi_Secim_SatiriSorgusu', 'DD.Prototype.Tests.EditMode.MetaPathTests.SilahSatinAlma_Atomik_Idempotan_SilahAcar', 'DD.Prototype.Tests.EditMode.MetaPathTests.TazeProfil_Durumlar', 'DD.Prototype.Tests.EditMode.MetaPathTests.YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz', 'DD.Prototype.Tests.EditMode.RunTreeTests.AnlikGoruntu_SatinAlinanKart_TekliftenDuser', 'DD.Prototype.Tests.EditMode.RunTreeTests.Anlik_Goruntu_S09_Alanlari_YeniAcilan_Yol', 'DD.Prototype.Tests.EditMode.RunTreeTests.Doruk_M1Oncesi_YalnizPinleTeklife_Girer', 'DD.Prototype.Tests.EditMode.RunTreeTests.Dukkan_AgaciKosuBasinaKurar_KeseyiOder_OcakKomutu', 'DD.Prototype.Tests.EditMode.RunTreeTests.EtkisiBaglanmamis_Kilitli_Satilmaz_Teklife_Girmez', 'DD.Prototype.Tests.EditMode.RunTreeTests.IcerikPaketi_HerDugumunAnahtari_Var', 'DD.Prototype.Tests.EditMode.RunTreeTests.Izgara_IkiSerit_4x4_BenzersizHucre_CekirdekDallarRoster', 'DD.Prototype.Tests.EditMode.RunTreeTests.KademeKisaAdlari_Benzersiz_TamKelime_OnIkiAlti', 'DD.Prototype.Tests.EditMode.RunTreeTests.Katalog_Gecerli_OnSilah_KisaAd12', 'DD.Prototype.Tests.EditMode.RunTreeTests.Kese_TurSonuYuzde10_YalnizSahipken', 'DD.Prototype.Tests.EditMode.RunTreeTests.Keystone_Kavsak_Dislama_Doruk_Kurallari', 'DD.Prototype.Tests.EditMode.RunTreeTests.Nisan_Yol_VeTeklifGarantisi', 'DD.Prototype.Tests.EditMode.RunTreeTests.Ocak_KartDisi_Fiyat_TurSiniri_Tavan_Hasar', 'DD.Prototype.Tests.EditMode.RunTreeTests.YeniDallar_EnumSozlesme_CekiliseGirmez'], 'audio': ['DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AkustikAlanlar_V6Haritasi_SalonKoridor_Histerezis_SahneYoksaOpen', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AnimasyonOlaylari_HerKlipOlayininSatiriVar_SilahFoleyAyrik', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AtesV3_KimlikTablosu_WP069AudioIdleri_HibritUcSilah_KuyrukMatrisi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AyakSesi_PlakaVaryantlari_AdKurali', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_BesPoligon_HerMerkezKendiBolgesineCozulur', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_NoktaPoligonTesti', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.DarbeV3_YuzeyKalibreMatrisi_AdlarVeKazanc', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Firlatici_UcusDongusuEslemi_PitchRampasi_PatlamaKatmani', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.FoleyV3_SilahBasinaMekanizmaSeti_AileVeKovanEslemi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestLufsVeTepe_StandartIcinde_VeYenidenOlcumTutuyor', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestVar_DonusturucuIdempotan_VeYuklenebilir', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_AP017AileAdlari_KendiSesineCozulur', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_EventIdVeriden_AltiOlayKendiSesi_AdOlayIcindeInceltir', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.LoudnessMeter_TamOlcekSinus_BS1770Referansi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Mixer_AltiGrupDortSnapshot_AcikParametreler_KopruEslemesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerCombatVeRunOlayiIcinSatirVar', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerSesinRecipeVeyaKatalogKarsiligiVar', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_OyuncuUiAmbiyansMuzikSatirlari_TamVeTekil', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SilahFoleyV3_KlipSetineGore_MekanizmaEslemesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SpecialKullanim_RaijuNamekuji_Satirlari_EskiKimliklerYok', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.TierVokalSeti_HerTierKendiKimligi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiOlaylari_HerUiEventUyesininSatiriVar_RouterHaritayaEsit', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiV3_Sozlesme_YeniOlaylarBagli_RunCuelariDuckYok_SliderHizi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UstKalite_24BitKaynak_GurultuTabani_Klip_DC_ImporterKalitesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.YuzeyEtiketleri_HerPlacementAilesininKuraliVar', 'DD.Prototype.Tests.PlayMode.AudioSliceTests.HavuzTavani_AsimdaDusurur_AktifSesKapasiteyiAsmaz', 'DD.Prototype.Tests.PlayMode.AudioSliceTests.SesYolu_KararliDurumda_SifirAyirma'], 'ui': ['DD.Prototype.Tests.EditMode.InputParityContractTests.InputSystem_YalnizInputKlasorunde', 'DD.Prototype.Tests.EditMode.UiCameraModesTests.KameraSatirlari_Sinirlanir_Kalici_EskiDosyaVarsayilan'], 'readability': ['DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet']}
# C-00 30.09 K4: the Player smoke matrix is the AI flags the C# source parses (tests/test_wp078_ai_flags.py).
AI_FLAGS = ("-ddAiV3", "-ddRagdoll")
MODE_NAMES = {'topdown':'TopDown','tp':'ThirdPerson','fp':'FirstPerson'}
ROUTE_KEYS = ('courtyard','corridor','kairo','horizon')
KNOWN_OPEN = ("-ddCorpseLinger", "-ddChainHops", "-ddGelPuddles", "-ddCrowdNeighbours", "-ddSpawnLos")
BUILD_MODES = {"dev": "dev", "release": "release", "probe": "release-probe"}
SCENES = ["Assets/_Prototype/Scenes/Boot.unity", "Assets/_Prototype/Scenes/Run.unity"]
# WP-079G A6 (S-01 06b7d19c): DdBuild's native sceneList is [{path, guid}] in build order
SCENE_LIST = [{"path": p, "guid": format(i + 1, "032x")} for i, p in enumerate(SCENES)]
# K6: a timed 1 Hz QaRunBot trace that keeps moving (measurable, no same-spot cluster).
TRACE = "\n".join(f"[QaRunBot] t pos={2.0*n:.1f},0.0,1.0 time={float(n):.1f} mode=patrol near=0" for n in range(12))
GUARD = dict(snapshotExit=0, restoreExit=0, verified=1, outOfScopeChanged=0)

class EvidenceFixture:
    def __init__(self, root):
        self.root = root
        self.serial = 0
        self.targets = json.loads((HERE / 'perf-targets.json').read_text())
        self.binaries = {role: self.put(role + '.bin', role.encode()) for role in ('dev', 'release', 'probe')}
        self.manifest = dict(schema='dd-wp078-acceptance-v1', commit=FINAL, final=True,
            assetsTree='c'*40, buildHashes={k:v['artifactSha256'] for k,v in self.binaries.items()}, checklist={})
        for n in range(8):
            self.manifest['checklist']['A'+str(n)] = self.step(n)
        c = self.manifest['checklist']
        c['A0']['library'] = self.library()
        c['A1']['xml'] = {k:self.xml(k) for k in ('editMode', 'playMode')}
        c['A2']['runs'] = [self.run(m,61,5) for m in ('topdown','tp','fp')]
        c['A2']['routeRuns'] = [self.route_run(m) for m in ('topdown','tp','fp')]
        c['A3']['checks'] = {area:self.step(3) for area in AREAS}
        for area,row in c['A3']['checks'].items():row['evidence'].append(self.domain_xml(area))
        c['A3']['checks']['aiFlags']['flagRuns']=[self.flag_run(flag,value) for flag in AI_FLAGS for value in (0,1)]
        c['A4'].update(perfRoot='perf', buildRole='probe')
        self.perf()
        c['A5']['runs'] = [self.run('topdown',s,12,True) for s in (61,26)]
        self.manifest['reportRuns'] = [self.run(m,61,5) for m,n in [('topdown',5),('tp',2),('fp',2)] for _ in range(n)]
        c['A5']['reportRuns'] = self.manifest['reportRuns']
        c['A6']['builds'] = {role:dict(library=self.library(), binary=ref, guard=copy.deepcopy(GUARD),
            provenance=self.provenance(role,compression='LZ4HC'),originalExit=0,
            buildReport=self.put_json(dict(result='Succeeded',mode=BUILD_MODES[role],totalErrors=0,totalSize=1000,scenes=len(SCENES),
                **({} if role=='dev' else dict(compression='lz4hc')),development=role=='dev',sceneList=copy.deepcopy(SCENE_LIST),scriptingBackend='Mono2x',
                stamp=dict(stamped=True,commit=FINAL,describe=FINAL,mode=BUILD_MODES[role])),role+'-build-report.json'),
            report=self.put_json({'ok':True,'sourceCommit':FINAL},role+'-report.json'))
            for role,ref in self.binaries.items()}
        c['A7'].update(byteEqual=True, integrityXml=self.xml('ProjectIntegrityTests.Check'))
        c['A4']['attempts']=[dict(path=str(p.relative_to(self.root)),artifactSha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted((self.root/'perf').glob('*/attempt.json'))]
        c['A4']['ledger']=self.write_ledger()
        self.anchor(self.manifest)
    def write_ledger(self, extra=()):
        # wp064_run: one start row and one end row per attemptId in <perf root>/attempts-ledger.jsonl.
        lines=[]
        for p in sorted((self.root/'perf').glob('*/attempt.json')):
            a=json.loads(p.read_text());d=p.parent.name
            lines.append(dict(attemptId=a['attemptId'],kind='perf',status='starting',dir=d))
            end=dict(attemptId=a['attemptId'],kind='perf',status=a['status'],exitCode=a['exitCode'],dir=d)
            end.update({k:a[k] for k in ('hostClean','timedOut','missingArtifacts') if k in a})
            lines.append(end)
        lines+=list(extra)
        return self.put('perf/attempts-ledger.jsonl',''.join(json.dumps(x,sort_keys=True)+'\n' for x in lines).encode())
    def put(self, name, data):
        p=self.root/name; p.parent.mkdir(parents=True,exist_ok=True); p.write_bytes(data)
        return dict(path=name,artifactSha256=hashlib.sha256(data).hexdigest())
    def put_json(self, data, name=None):
        self.serial+=1
        return self.put(name or f'artifact-{self.serial}.json',json.dumps(data).encode())
    def library(self):
        return self.put_json(dict(schema='dd-wp064-library-provenance-v1',phase='post-build',assetsTree='c'*40,head=FINAL,porcelain='',lfsPointersOnDisk=0,lfsMissing=0,library={'artifactDbBytes':1,'artifactDbMtime':1}))
    def xml(self, name):
        return self.put(f'{name}.xml',f'<test-run total="1" passed="1" failed="0" skipped="0" inconclusive="0" result="Passed"><test-case fullname="{name}" name="{name}" result="Passed"/></test-run>'.encode())
    def domain_xml(self,area):
        import xml.etree.ElementTree as ET
        ids=DOMAIN_IDS[area];root=ET.Element('test-run',total=str(len(ids)),passed=str(len(ids)),failed='0',skipped='0',inconclusive='0',result='Passed')
        for name in ids:ET.SubElement(root,'test-case',fullname=name,name=name,result='Passed')
        return self.put('domain-'+area+'.xml',ET.tostring(root))
    def flag_run(self,flag,value):
        r=self.run('topdown',61,1)
        r.update(flag=flag,value=value,command=self.put_json(['DeathsDemolition',flag,str(value)]))
        return r
    def provenance(self, role='dev', name=None, **extra):
        return self.put_json(dict(status='complete',guard=copy.deepcopy(GUARD),executionCommit=FINAL,sourceCommit=FINAL,testedCommit=FINAL,exitCode=0,
            binarySha256=self.binaries[role]['artifactSha256'],hostClean=True,**extra),name)
    def step(self,n):
        p=self.provenance()
        return dict(status='PASS',owner='S-10',executionCommit=FINAL,originalExit=0,provenance=p,
            evidence=[p]+([self.put_json({'pass':True,'executionCommit':FINAL})] if n==3 else []),guard=copy.deepcopy(GUARD))
    def run(self, mode, seed, count, stability=False):
        records=[dict(round=n,completed=True,outcome='completed',memMonoUsedMB=100,memTotalAllocatedMB=200,memRssMB=300) for n in range(1,count+1)]
        summary=dict(cameraMode=mode,qualityTier='Sinematik',worldHalfExtent=55.0,seed=seed,roundsRequested=count,roundsCompleted=count,
            rounds=records,runMode='stability' if stability else 'normal',pass_=True,stabilityPass=True,deaths=0,
            timedOut=False,passAll=True,passed=True,stuckRecoveries=0,unstickSweeps=0)
        summary['pass']=True
        if stability:
            # wp064_run layout: attempt.json + command.json share the attempt directory (K5 argv cross-check).
            self.serial+=1;d=f'soak-{seed}-{self.serial}'
            command=self.put_json(['Player','-ddQaBot','out','-ddQaBotRounds',str(count),'-ddQaBotSeed',str(seed),'-ddQaBotStability','-ddQualityTier','sinematik'],d+'/command.json')
            p=self.provenance(name=d+'/attempt.json',commandSha256=command['artifactSha256'])
        else:
            p=self.provenance()
        log='\n'.join(f'[Camera] inside-collider round={n} frames=0/60' for n in range(1,count+1))+'\n'+TRACE
        return dict(provenance=p,originalExit=0,guard=copy.deepcopy(GUARD),summary=self.put_json(summary),
            log=self.put(f'run-{self.serial}.log',log.encode()),buildRole='dev')
    def route_run(self, mode):
        # C-00 30.09 R1–R3: QaRunBot -ddQaBotRouteTour <runOut>/frames; bot-summary routeFrames is the native record.
        self.serial+=1;d=f'route-{mode}-{self.serial}';native=MODE_NAMES[mode];frames={};route=[]
        for key in ROUTE_KEYS:
            if mode=='topdown' and key=='horizon':
                frames[key]={'notApplicable':'topdown-fixed-camera'}
                route.append(dict(key=key,status='notApplicable',reason='topdown-fixed-camera: yaw fixed north, the top ray is ~28° below the horizon (E-3 by FP)',path='',mode=native,roofAbove=False))
                continue
            frames[key]=self.png(f'{d}/frames/route-{key}.png')
            route.append(dict(key=key,status='captured',reason='',path=f'frames/route-{key}.png',mode=native,roofAbove=key in ('corridor','kairo')))
        summary=self.put_json(dict(cameraMode=native,runMode='route',routeFrames=route,gatePass=True,roundsCompleted=0,**{'pass':True}),d+'/bot-summary.json')
        log=self.put(d+'/player.log',b'[QaRunBot] route-tour done frames=4\n')
        return dict(provenance=self.provenance(name=d+'/attempt.json'),originalExit=0,guard=copy.deepcopy(GUARD),
            summary=summary,log=log,buildRole='dev',frames=frames)
    def png(self,name):
        self.serial+=1;pixel=bytes([self.serial%256,(self.serial//256)%256,7])
        def chunk(tag,data):return struct.pack('>I',len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
        raw=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00'+pixel))+chunk(b'IEND',b'')
        return self.put(name,raw)
    def perf(self):
        for tier,limits in self.targets['tiers'].items():
            for mode,native in [('topdown','TopDown'),('tp','ThirdPerson'),('fp','FirstPerson')]:
                for n in range(1,4):
                    d=f'perf/perf-{tier}-{mode}-{n}'
                    window=self.put_json(dict(qualityTier=limits['probeValue'],cameraMode=native,cameraSweep=True,
                        frameMeanMs=5,frameP99FilteredMs=5,framesExcludedAsStalls=0,frameCount=12000,
                        pass_=True,windowSeconds=60,activeMin=40,activeMax=40,liveBodiesMin=40,liveBodiesMax=40,
                        crowdMemberMin=40,crowdMemberMax=40,loadIntegrityFloor=40,loadIntegrityFraction=1.0),d+'/04-window.json')
                    self.replace_json(window,lambda x:x.update({'pass':True}))
                    resources=[window,self.put(d+'/frame-times-ms.csv',b'5.0000\n'*12000),
                        self.put_json(dict(scene='Run',screenWidth=1920,screenHeight=1080),d+'/01-scene-ready.json'),
                        self.put_json(dict(configuredSafetyCeiling=40,ceiling=40,forcedTarget=40,achievedCount=40,**{'pass':True}),d+'/02-force-40.json'),
                        self.put_json(dict(warmupSeconds=10,**{'pass':True}),d+'/03-warmup.json')]
                    command=self.put_json(['synthetic','-ddQualityTier',tier,'-ddCameraMode',mode],d+'/command.json')   # wp064_run's argv list
                    self.put_json(dict(attemptId=f'id-{tier}-{mode}-{n}',executionCommit=FINAL,status='complete',exitCode=0,hostClean=True,timedOut=False,missingArtifacts=[],
                        binarySha256=self.binaries['probe']['artifactSha256'],guard=copy.deepcopy(GUARD),
                        resolution=[1920,1080],activeEnemies=40,warmupSeconds=10,windowSeconds=60,
                        commandSha256=command['artifactSha256'],artifactSha256={Path(r['path']).name:r['artifactSha256'] for r in resources}),d+'/attempt.json')
    def anchor(self, value):
        if isinstance(value,list):
            for v in value:self.anchor(v)
        elif isinstance(value,dict):
            for k,v in list(value.items()):
                if k!='provenance':self.anchor(v)
            if 'provenance' in value:
                refs={}
                def collect(v):
                    if isinstance(v,list):
                        for x in v:collect(x)
                    elif isinstance(v,dict):
                        if 'path' in v and 'artifactSha256' in v and v['path']!=value['provenance']['path']:refs[os.path.relpath(v['path'],os.path.dirname(value['provenance']['path']) or '.')]=v['artifactSha256']
                        else:
                            for k,x in v.items():
                                collect(x)
                for k,v in value.items():
                    if k!='provenance':collect(v)
                self.replace_json(value['provenance'],lambda x:x.update(artifactSha256=refs or {'dev.bin':self.binaries['dev']['artifactSha256']}))
                if value.get('evidence'):
                    value['evidence'][0]=value['provenance']
    def replace_json(self, ref, mutate):
        data=json.loads((self.root/ref['path']).read_text()); mutate(data)
        fresh=self.put_json(data,ref['path']); ref.update(fresh)

class Fixture(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        spec=importlib.util.spec_from_file_location('wp078_report',HERE/'wp078_report.py')
        cls.module=importlib.util.module_from_spec(spec);spec.loader.exec_module(cls.module)
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.f=EvidenceFixture(self.root);self.m=self.f.manifest
    def result(self):return self.module.generate_report(self.m,self.root,targets=self.f.targets)
    def assert_closed(self):
        name=inspect.stack()[1].function
        semantic={'test_guard_unverified','test_guard_restore_failure','test_snapshot_failure',
            'test_boolean_exit_not_integer_zero','test_original_exit_not_hidden_by_restore',
            'test_native_exit_contradiction','test_provenance_not_relabelable','test_mixed_binary',
            'test_hold_targeted_pass','test_causal_closure_false_is_not_acceptance',
            'test_sinematik_is_gate','test_nan_perf','test_csv_drift','test_wrong_native_load'}
        if name in semantic:
            if name in {'test_guard_unverified','test_guard_restore_failure','test_snapshot_failure'}:
                for step in self.m['checklist'].values():
                    self.f.replace_json(step['provenance'],lambda x,step=step:x.update(guard=copy.deepcopy(step['guard'])))
            for ref in self.m['checklist']['A4']['attempts']:
                path=self.root/ref['path'];data=json.loads(path.read_text())
                for filename in data['artifactSha256']:
                    data['artifactSha256'][filename]=hashlib.sha256((path.parent/filename).read_bytes()).hexdigest()
                fresh=self.f.put_json(data,ref['path']);ref.update(fresh)
            self.f.anchor(self.m)
        r=self.result();self.assertIs(r['pass'],False);self.assertNotEqual(r['status'],'PASS');self.assertTrue(r['failures'])
    def assert_step(self,step,status,reason=None):
        r=self.result();self.assertFalse(r['pass'])
        rows=[x for x in r['failures'] if x['step']==step]
        self.assertTrue(rows,r['failures'])
        if reason is not None:
            rows=[x for x in rows if reason.lower() in x['reason'].lower()];self.assertTrue(rows,r['failures'])
        self.assertEqual(rows[0]['status'],status,rows)
        return r
    def rehash_perf(self):
        # Re-anchor every perf attempt after its native resources changed (the runner hashes them at completion).
        for ref in self.m['checklist']['A4']['attempts']:
            path=self.root/ref['path'];data=json.loads(path.read_text())
            for filename in data.get('artifactSha256',{}):
                data['artifactSha256'][filename]=hashlib.sha256((path.parent/filename).read_bytes()).hexdigest()
            ref.update(self.f.put_json(data,ref['path']))
        self.m['checklist']['A4']['ledger']=self.f.write_ledger();self.f.anchor(self.m)
    def set_csv(self,cell,values,**window):
        # Rewrite one window's CSV and its native (33.33 ms) probe fields consistently.
        d=self.root/'perf'/cell;n=len(values);native=sorted(v for v in values if v<=33.33)
        (d/'frame-times-ms.csv').write_text(''.join(f'{v:.4f}\n' for v in values))
        p99=native[min(len(native)-1,-(-99*len(native)//100)-1)]
        data=json.loads((d/'04-window.json').read_text())
        data.update(frameCount=n,frameMeanMs=sum(values)/n,frameP99FilteredMs=p99,framesExcludedAsStalls=n-len(native));data.update(window)
        (d/'04-window.json').write_text(json.dumps(data))
    def add_attempt(self,cell,**fields):
        # A non-complete attempt directory exactly as wp064_run leaves it (attempt.json + ledger rows).
        d=self.root/'perf'/cell;d.mkdir(parents=True)
        pre=self.f.put('perf/'+cell+'/preflight.json',b'{"pass": false}')
        data=dict(attemptId='id-'+cell,schema='dd-wp064-attempt-v1',kind='perf',executionCommit=FINAL,
            binarySha256=self.f.binaries['probe']['artifactSha256'],artifactSha256={'preflight.json':pre['artifactSha256']},**fields)
        ref=self.f.put_json(data,'perf/'+cell+'/attempt.json')
        self.m['checklist']['A4']['attempts'].append(ref);self.rehash_perf()

class AcceptanceTests(Fixture):
    def test_complete_source_bound_final(self):
        r=self.result();self.assertTrue(r['pass'],r['failures']);self.assertEqual(r['schema'],'dd-wp078-acceptance-v1');self.assertEqual(r['commit'],FINAL);self.assertEqual(set(r['checklist']),{'A'+str(i) for i in range(9)})
    def test_missing_and_nonexecuted_steps(self):
        for state in (None,'SKIPPED','BLOCKED','NOT_RUN'):
            with self.subTest(state=state):
                original=copy.deepcopy(self.m['checklist']['A1'])
                if state is None:del self.m['checklist']['A1']
                else:self.m['checklist']['A1']['status']=state
                self.assert_closed();self.m['checklist']['A1']=original
    def test_final_required(self):self.m['final']=False;self.assert_closed()
    def test_mixed_execution_sha(self):self.m['checklist']['A2']['executionCommit']=OTHER;self.assert_closed()
    def test_provenance_not_relabelable(self):
        self.f.replace_json(self.m['checklist']['A1']['provenance'],lambda x:x.update(testedCommit=OTHER));self.assert_closed()
    def test_missing_xml(self): (self.root/self.m['checklist']['A1']['xml']['editMode']['path']).unlink();self.assert_closed()
    def test_invalid_xml(self):self.m['checklist']['A1']['xml']['editMode']=self.f.put('broken.xml',b'<bad');self.assert_closed()
    def test_empty_xml(self):self.m['checklist']['A1']['xml']['editMode']=self.f.put('empty.xml',b'<test-run total="0" passed="0" result="Passed"/>');self.assert_closed()
    def test_original_exit_not_hidden_by_restore(self):self.m['checklist']['A1']['originalExit']=1;self.assert_closed()
    def test_boolean_exit_not_integer_zero(self):self.m['checklist']['A1']['originalExit']=False;self.assert_closed()
    def test_guard_unverified(self):self.m['checklist']['A1']['guard']['verified']=0;self.assert_closed()
    def test_hold_targeted_pass(self):self.m['checklist']['A3']['acceptance']='HOLD';self.assert_closed()
    def test_stale_pass_without_resources(self):self.m['checklist']['A1']['evidence']=[];self.assert_closed()
    def test_hash_mismatch(self):self.m['checklist']['A1']['xml']['editMode']['artifactSha256']='0'*64;self.assert_closed()
    def test_mixed_binary(self):
        self.f.replace_json(self.m['checklist']['A2']['runs'][0]['provenance'],lambda x:x.update(binarySha256='f'*64));self.assert_closed()
    def test_incomplete_perf(self):
        p=self.root/'perf/perf-yuksek-tp-3/attempt.json';p.unlink();self.assert_closed()
    def test_sinematik_is_gate(self):
        ref=dict(path='perf/perf-sinematik-fp-1/04-window.json')
        self.f.replace_json(ref,lambda x:x.update(frameMeanMs=1000));self.assert_closed()
    def test_nan_perf(self):
        ref=dict(path='perf/perf-yuksek-tp-1/04-window.json')
        self.f.replace_json(ref,lambda x:x.update(frameMeanMs=float('nan')));self.assert_closed()
    def test_incomplete_soak(self):self.m['checklist']['A5']['runs'].pop();self.assert_closed()
    def test_duplicate_soak_seed(self):
        self.m['checklist']['A5']['runs'][1]=copy.deepcopy(self.m['checklist']['A5']['runs'][0]);self.assert_closed()
    def test_missing_integrity_leaf(self):self.m['checklist']['A7']['integrityXml']=self.f.xml('UnrelatedTests.Check');self.assert_closed()
    def test_guard_restore_failure(self):self.m['checklist']['A1']['guard']['restoreExit']=1;self.assert_closed()
    def test_native_exit_contradiction(self):
        self.f.replace_json(self.m['checklist']['A1']['provenance'],lambda x:x.update(exitCode=2));self.assert_closed()
    def test_report_runs_incomplete(self):
        self.m['reportRuns'][:]=self.m['reportRuns'][:5];self.assert_closed()
    def test_duplicate_camera_mode(self):
        self.m['checklist']['A2']['runs'][2]=copy.deepcopy(self.m['checklist']['A2']['runs'][0]);self.assert_closed()
    def test_build_binary_missing(self):
        (self.root/self.m['checklist']['A6']['builds']['release']['binary']['path']).unlink();self.assert_closed()
    def test_snapshot_failure(self):self.m['checklist']['A7']['guard']['snapshotExit']=1;self.assert_closed()
    def test_failed_and_skipped_xml(self):
        original=copy.deepcopy(self.m['checklist']['A1']['xml']['playMode'])
        for result,field in [('Failed','failed'),('Skipped','skipped')]:
            with self.subTest(result=result):
                raw=f'<test-run total="1" passed="0" {field}="1" result="{result}"><test-case fullname="Some.Check" result="{result}"/></test-run>'
                self.m['checklist']['A1']['xml']['playMode']=self.f.put('bad-leaf.xml',raw.encode());self.assert_closed()
        self.m['checklist']['A1']['xml']['playMode']=original
    def test_unknown_stuck_counter(self):
        self.f.replace_json(self.m['checklist']['A2']['runs'][0]['summary'],lambda x:x.pop('stuckRecoveries'));self.assert_closed()
    def test_causal_closure_false_is_not_acceptance(self):
        self.f.replace_json(self.m['checklist']['A3']['checks']['projectile']['evidence'][1],lambda x:x.update(causalClosure=False));self.assert_closed()
    def test_real_bot_death_exit_three(self):
        # QaRunBot's native exit for a failed real-bot run is 3 (ExitCode); it stays a report.
        run=self.m['reportRuns'][1];run['originalExit']=3
        self.f.replace_json(run['provenance'],lambda x:x.update(exitCode=3,status='failed'))
        self.f.replace_json(run['summary'],lambda x:x.update({'pass':False,'deaths':1}))
        self.f.anchor(self.m);r=self.result();self.assertTrue(r['pass'],r['failures'])
    def test_original_balance_exit_preserved(self):
        run=self.m['reportRuns'][0];run['originalExit']=1
        self.f.replace_json(run['provenance'],lambda x:x.update(exitCode=1,status='failed'))
        self.f.replace_json(run['summary'],lambda x:x.update({'pass':False,'deaths':1,'deathRound':5,'deathKilledBy':'test-balance-cause'}))
        self.f.anchor(self.m)
        r=self.result();self.assertTrue(r['pass'],r['failures']);self.assertEqual(r['reportRuns'][0]['originalExit'],1)
    def cli(self,out):
        manifest=self.root/'input.json';manifest.write_text(json.dumps(self.m))
        return subprocess.run([sys.executable,str(HERE/'wp078_report.py'),'--manifest',str(manifest),
            '--evidence-root',str(self.root),'--out',str(out)],capture_output=True,text=True)
    def test_cli_complete_and_no_overwrite(self):
        out=self.root/'output';first=self.cli(out);self.assertEqual(first.returncode,0,first.stderr+first.stdout)
        files={p.name:p.read_bytes() for p in out.iterdir()};self.assertEqual(set(files),{'wp078-report.json','wp078-report.txt'})
        second=self.cli(out);self.assertEqual(second.returncode,64,second.stderr)
        self.assertEqual(files,{p.name:p.read_bytes() for p in out.iterdir()})
    def test_cli_blocked_honest_exit(self):
        self.m['checklist']['A1']['status']='BLOCKED';out=self.root/'blocked-output'
        proc=self.cli(out);self.assertEqual(proc.returncode,1,proc.stderr+proc.stdout)
        r=json.loads((out/'wp078-report.json').read_text());self.assertFalse(r['pass'])
    def test_rehashed_xml_without_execution_anchor(self):
        self.m['checklist']['A1']['xml']['editMode']=self.f.xml('Different.Pass');self.assert_closed()
    def test_summary_mixed_source(self):
        self.f.replace_json(self.m['checklist']['A2']['runs'][0]['summary'],lambda x:x.update(sourceCommit=OTHER));self.f.anchor(self.m);self.assert_closed()
    def test_stability_stuck_recovery(self):
        # C-00 30.09 K6: stuckRecoveries is the bot's own route recovery — reported per run with unstickSweeps, not a gate.
        self.f.replace_json(self.m['checklist']['A5']['runs'][0]['summary'],lambda x:x.update(stuckRecoveries=27,unstickSweeps=3))
        self.f.replace_json(self.m['checklist']['A2']['runs'][1]['summary'],lambda x:x.update(stuckRecoveries=15))
        self.f.anchor(self.m);r=self.result();self.assertTrue(r['pass'],r['failures'])
        rows={(x['step'],x['seed'],x['cameraMode']):x for x in r['botRuns']}
        self.assertEqual((rows[('A5',61,'topdown')]['stuckRecoveries'],rows[('A5',61,'topdown')]['unstickSweeps']),(27,3))
        self.assertEqual(rows[('A2',61,'tp')]['stuckRecoveries'],15)
    def test_incomplete_library_phase(self):
        self.f.replace_json(self.m['checklist']['A0']['library'],lambda x:x.update(phase='pre-build'));self.f.anchor(self.m);self.assert_closed()
    def test_unanchored_perf_attempt(self):
        ref=self.m['checklist']['A4']['attempts'][0]
        data=json.loads((self.root/ref['path']).read_text());data['hostClean']=False
        (self.root/ref['path']).write_text(json.dumps(data));self.assert_closed()
    def test_build_smoke_mixed_source(self):
        ref=self.m['checklist']['A6']['builds']['release']['report']
        self.f.replace_json(ref,lambda x:x.update(sourceCommit=OTHER));self.f.anchor(self.m);self.assert_closed()
    def test_missing_route_frames(self):
        # R1–R3: a missing route key is BLOCKED, never FAIL and never substituted.
        for index,key in ((1,'horizon'),(2,'kairo'),(0,'courtyard')):
            with self.subTest(key=key):
                self.setUp();self.m['checklist']['A2']['routeRuns'][index]['frames'].pop(key);self.f.anchor(self.m)
                self.assert_step('A2','BLOCKED','route frame '+key+' missing')
    def test_missing_csv(self):
        (self.root/'perf/perf-yuksek-tp-1/frame-times-ms.csv').unlink();self.assert_closed()
    def test_csv_drift(self):
        (self.root/'perf/perf-yuksek-tp-1/frame-times-ms.csv').write_text('9\n'*12000);self.assert_closed()
    def test_wrong_native_load(self):
        p=self.root/'perf/perf-yuksek-tp-1/02-force-40.json';data=json.loads(p.read_text());data['achievedCount']=39;p.write_text(json.dumps(data));self.assert_closed()
    def test_dirty_build_stamp(self):
        self.f.replace_json(self.m['checklist']['A6']['builds']['release']['buildReport'],lambda x:x['stamp'].update(describe=FINAL+'-dirty'));self.f.anchor(self.m);self.assert_closed()
    def test_object_hold(self):self.m['checklist']['A3']['acceptance']={'status':'HOLD'};self.assert_closed()
    def test_causal_zero_not_true(self):
        self.f.replace_json(self.m['checklist']['A3']['checks']['projectile']['evidence'][1],lambda x:x.update(causalClosure=0));self.f.anchor(self.m);self.assert_closed()
    def test_actual_fail_owner(self):
        row=self.m['checklist']['A3']['checks']['projectile'];row['owner']='S-02';row['status']='FAIL'
        r=self.result();self.assertFalse(r['pass']);self.assertTrue(any(x['step']=='A3' and x['owner']=='S-02' for x in r['failures']))
    def test_fourth_wrong_tier_window_not_discarded(self):
        import shutil
        old=self.root/'perf/perf-yuksek-tp-3';new=self.root/'perf/perf-yuksek-tp-4';shutil.copytree(old,new)
        window=json.loads((new/'04-window.json').read_text());window['qualityTier']='Sinematik'
        wr=self.f.put_json(window,str((new/'04-window.json').relative_to(self.root)))
        ar=dict(path=str((new/'attempt.json').relative_to(self.root)))
        self.f.replace_json(ar,lambda x:(x['artifactSha256'].update({'04-window.json':wr['artifactSha256']}),x.update(attemptId='id-fourth')))
        self.m['checklist']['A4']['attempts'].append(ar);self.m['checklist']['A4']['ledger']=self.f.write_ledger();self.f.anchor(self.m);self.assert_closed()
    def test_unrelated_succeeded_json_cannot_prove_domains(self):
        row=self.m['checklist']['A3']['checks']['projectile']
        row['evidence']=[row['provenance'],self.f.put_json(dict(schema='unrelated-json',executionCommit=FINAL,result='Succeeded'))]
        self.f.anchor(self.m);self.assert_closed();self.assertTrue(any('missing native NUnit domain evidence' in x['reason'] for x in self.result()['failures']))
    def test_unrelated_nunit_leaf_cannot_prove_domain(self):
        row=self.m['checklist']['A3']['checks']['projectile'];row['evidence']=[row['provenance'],self.f.xml('UnrelatedTests.Green')]
        self.f.anchor(self.m);self.assert_closed()
    def test_missing_native_exit_code(self):
        self.f.replace_json(self.m['checklist']['A1']['provenance'],lambda x:x.pop('exitCode'))
        self.f.anchor(self.m);self.assert_closed()
    def test_plan_stability_failure_patterns(self):
        for message in ['Failed to create agent because there is no valid NavMesh',
                'SetDestination can only be called on an active agent that has been placed on a NavMesh',
                '[Projectile] round=3 active=40 overflow=1', '[Pool] Ragdoll exceeded its capacity of 10. Growing.',
                'Joint error: connected Rigidbody invalid']:
            with self.subTest(message=message):
                run=self.m['checklist']['A5']['runs'][0]
                run['log']=self.f.put('stability-errors.log',message.encode());self.f.anchor(self.m);self.assert_closed()
    def test_benign_stability_log_pass(self):
        run=self.m['checklist']['A5']['runs'][0]
        run['log']=self.f.put('benign-stability.log',b'[QaRunBot] round complete; pool exhausted at intended round end\n[Projectile] round=3 overflow=0\nJoint Rigidbody configured successfully\n'+TRACE.encode())
        self.f.anchor(self.m);r=self.result();self.assertTrue(r['pass'],r['failures'])
    def test_ai_flags_missing_smoke(self):
        self.m['checklist']['A3']['checks']['aiFlags']['flagRuns'].pop();self.f.anchor(self.m);self.assert_closed()
    def test_ai_flag_wrong_native_argv(self):
        r=self.m['checklist']['A3']['checks']['aiFlags']['flagRuns'][0]
        r['command']=self.f.put_json(['DeathsDemolition',r['flag'],'1']);self.f.anchor(self.m);self.assert_closed()
    def test_ai_flag_incomplete_round(self):
        r=self.m['checklist']['A3']['checks']['aiFlags']['flagRuns'][0]
        self.f.replace_json(r['summary'],lambda x:x.update(roundsCompleted=0));self.f.anchor(self.m);self.assert_closed()
    def test_native_domain_one_required_leaf_missing(self):
        import xml.etree.ElementTree as ET
        row=self.m['checklist']['A3']['checks']['projectile'];ref=row['evidence'][-1]
        root=ET.fromstring((self.root/ref['path']).read_bytes());root.remove(root.findall('test-case')[0]);n=len(root.findall('test-case'));root.set('total',str(n));root.set('passed',str(n))
        ref.update(self.f.put(ref['path'],ET.tostring(root)));self.f.anchor(self.m);self.assert_closed()
    def test_native_timeout_contradiction(self):
        self.f.replace_json(self.m['checklist']['A1']['provenance'],lambda x:x.update(timedOut=True));self.f.anchor(self.m)
        self.assert_closed();self.assertTrue(any('timed out' in x['reason'] for x in self.result()['failures']))
    def supplementary(self,payload):
        row=self.m['checklist']['A3']['checks']['projectile']
        row['evidence'][1]=self.f.put_json(payload)
        self.f.anchor(self.m)
    def assert_supplement_rejected(self,reason):
        r=self.result();self.assertFalse(r['pass']);self.assertTrue(any(x['step']=='A3' and reason.lower() in x['reason'].lower() for x in r['failures']),r['failures'])
        self.assertFalse(any('hash' in x['reason'].lower() for x in r['failures']),r['failures'])
    def test_supplement_negative_verdicts(self):
        for payload in [{'pass':False},{'ok':False},{'result':'Failed'},{'result':'UNKNOWN'}]:
            with self.subTest(payload=payload):
                self.supplementary(payload);self.assert_supplement_rejected('supplement')
    def test_supplement_invalid_pass_ok_types(self):
        for field in ('pass','ok'):
            for value in (0,1,'true','false',None):
                with self.subTest(field=field,value=value):
                    self.supplementary({field:value});self.assert_supplement_rejected('supplemental '+field)
    def test_supplement_invalid_result_types_and_tokens(self):
        for value in (False,True,0,1,None,'passed','pass','succeeded','Pass','Success','Passed extra'):
            with self.subTest(value=value):
                self.supplementary({'result':value});self.assert_supplement_rejected('supplemental result')
    def test_supplement_foreign_or_invalid_source_fields(self):
        for field in ('executionCommit','head','sourceCommit','testedCommit'):
            for value in (OTHER,OTHER[:8],None):
                with self.subTest(field=field,value=value):
                    self.supplementary({field:value});self.assert_supplement_rejected('mixed supplemental '+field)
    def test_supplement_optional_metadata_and_positive_verdicts(self):
        for payload in ({},{'note':'optional metadata'},{'pass':True},{'ok':True},
                {'pass':True,'ok':True},{'result':'Passed'},{'result':'PASS'},{'result':'Succeeded'},
                {'testedCommit':FINAL},{'sourceCommit':FINAL}):
            with self.subTest(payload=payload):
                self.supplementary(payload);r=self.result();self.assertTrue(r['pass'],r['failures'])
    def test_positive_supplement_cannot_replace_native_xml(self):
        self.supplementary({'pass':True,'ok':True,'result':'Succeeded','executionCommit':FINAL})
        row=self.m['checklist']['A3']['checks']['projectile'];row['evidence'].pop();self.f.anchor(self.m)
        r=self.result();self.assertFalse(r['pass']);self.assertTrue(any('missing native NUnit' in x['reason'] for x in r['failures']))
    def test_evidence_path_escape(self):self.m['checklist']['A1']['provenance']['path']='../elsewhere.json';self.assert_closed()



class K1ReleaseProbeTests(Fixture):
    """C-00 30.09 K1: A4 perf on release-probe, A6 probe mandatory with native parity; bots stay on dev."""
    def test_a4_dev_role_fails(self):
        self.m['checklist']['A4']['buildRole']='dev';self.assert_step('A4','FAIL','release-probe')
    def test_a4_missing_role_blocked(self):
        del self.m['checklist']['A4']['buildRole'];self.assert_step('A4','BLOCKED','buildRole missing')
    def test_a4_window_on_dev_binary_fails(self):
        ref=self.m['checklist']['A4']['attempts'][0]
        self.f.replace_json(ref,lambda x:x.update(binarySha256=self.f.binaries['dev']['artifactSha256']))
        self.m['checklist']['A4']['ledger']=self.f.write_ledger();self.f.anchor(self.m);self.assert_step('A4','FAIL','binary hash')
    def test_a6_probe_mandatory(self):
        del self.m['checklist']['A6']['builds']['probe'];self.assert_step('A6','BLOCKED','release-probe')
    def test_a6_probe_mode_must_be_release_probe(self):
        b=self.m['checklist']['A6']['builds']['probe']
        self.f.replace_json(b['buildReport'],lambda x:(x.update(mode='dev'),x['stamp'].update(mode='dev')));self.f.anchor(self.m)
        self.assert_step('A6','FAIL','release-probe')
    def test_a6_parity_missing_native_fields_blocked(self):
        for field in ('development','sceneList','scriptingBackend','compression'):
            with self.subTest(field=field):
                self.setUp();b=self.m['checklist']['A6']['builds']['probe']
                self.f.replace_json(b['buildReport'],lambda x:x.pop(field));self.f.anchor(self.m)
                r=self.assert_step('A6','BLOCKED','probe.'+field);self.assertIn('probe.'+field,r['buildParity']['missingNativeFields'])
    def test_a6_parity_violations_fail(self):
        for field,value,reason in (('development',True,'Development'),('compression','lz4','LZ4HC'),('scriptingBackend','IL2CPP','scriptingBackend'),
                                   ('sceneList',[{'path':'Assets/Other.unity','guid':'f'*32}],'sceneList'),('scenes',5,'scene counts'),
                                   ('sceneList',[SCENE_LIST[1],SCENE_LIST[0]],'sceneList'),
                                   ('sceneList',[dict(SCENE_LIST[0],guid='e'*32),SCENE_LIST[1]],'sceneList'),
                                   ('sceneList',[{'path':SCENES[0]}],'sceneList')):
            with self.subTest(field=field):
                self.setUp();b=self.m['checklist']['A6']['builds']['probe']
                self.f.replace_json(b['buildReport'],lambda x:x.update({field:value}));self.f.anchor(self.m)
                self.assert_step('A6','FAIL',reason)
    def test_bot_runs_stay_dev(self):
        run=self.m['checklist']['A2']['runs'][0];run['buildRole']='probe'
        self.f.replace_json(run['provenance'],lambda x:x.update(binarySha256=self.f.binaries['probe']['artifactSha256']));self.f.anchor(self.m)
        self.assert_step('A2','FAIL','dev build')


class K4AiFlagTests(Fixture):
    """C-00 30.09 K4: -ddAiV3 and -ddRagdoll x0/1 (both parsed on main); the five specification-only flags are listed
    with their disposition, never PASS."""
    def test_module_flags(self):
        self.assertEqual(self.module.AI_FLAGS,AI_FLAGS)
        self.assertEqual(set(self.module.AI_FLAGS_NOT_PASS),set(KNOWN_OPEN))
    def test_spec_flags_listed_never_pass(self):
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        rows={x['flag']:x for x in r['checklist']['A3']['areas']['aiFlags']['notPass']}
        self.assertEqual(set(rows),set(KNOWN_OPEN))
        self.assertTrue(all(x['pass'] is False and x['status']!='PASS' and x['reason']=='spesifikasyonda var, kodda yok' for x in rows.values()))
        self.assertEqual({f:rows[f]['label'] for f in ('-ddChainHops','-ddGelPuddles')},dict.fromkeys(('-ddChainHops','-ddGelPuddles'),'retired (DEC-097/099)'))
        self.assertTrue(all(rows[f]['label']=='withdrawn in writing (S-03, C-00 30.09)' for f in ('-ddCorpseLinger','-ddCrowdNeighbours','-ddSpawnLos')))
        self.assertNotIn('-ddRagdoll',rows)
        self.assertEqual(r['checklist']['A3']['areas']['aiFlags']['flagRuns'],4)
    def test_exactly_four_runs(self):
        runs=self.m['checklist']['A3']['checks']['aiFlags']['flagRuns']
        self.assertEqual(len(runs),4)
        runs.append(self.f.flag_run('-ddAiV3',0));self.f.anchor(self.m);self.assert_step('A3','BLOCKED','exactly 4')
    def test_unparsed_flag_cannot_replace_a_listed_one(self):
        runs=self.m['checklist']['A3']['checks']['aiFlags']['flagRuns']
        runs[1]=self.f.flag_run('-ddCorpseLinger',1);self.f.anchor(self.m);self.assert_closed()


class K5SoakTierTests(Fixture):
    """C-00 30.09 K5: A5 at perf-targets defaultTier (Sinematik) from the bot summary; world 55 from the summary."""
    def summary(self,mutate):
        self.f.replace_json(self.m['checklist']['A5']['runs'][0]['summary'],mutate);self.f.anchor(self.m)
    def test_missing_quality_tier_blocked(self):
        self.summary(lambda x:x.pop('qualityTier'));self.assert_step('A5','BLOCKED','qualityTier')
    def test_wrong_quality_tier_fails(self):
        self.summary(lambda x:x.update(qualityTier='Yuksek'));self.assert_step('A5','FAIL','Sinematik')
    def test_missing_world_extent_blocked(self):
        self.summary(lambda x:x.pop('worldHalfExtent'));self.assert_step('A5','BLOCKED','worldHalfExtent')
    def test_wrong_world_extent_fails(self):
        self.summary(lambda x:x.update(worldHalfExtent=45.0));self.assert_step('A5','FAIL','55')
    def test_provenance_world_extent_is_not_evidence(self):
        run=self.m['checklist']['A5']['runs'][0]
        self.f.replace_json(run['provenance'],lambda x:x.update(worldHalfExtent=55));self.summary(lambda x:x.pop('worldHalfExtent'))
        self.assert_step('A5','BLOCKED','worldHalfExtent')
    def command(self,argv):
        run=self.m['checklist']['A5']['runs'][0];d=str(Path(run['provenance']['path']).parent)
        ref=self.f.put_json(argv,d+'/command.json')
        self.f.replace_json(run['provenance'],lambda x:x.update(commandSha256=ref['artifactSha256']));self.f.anchor(self.m)
    def test_command_needs_sinematik(self):
        self.command(['Player','-ddQaBotStability','-ddQualityTier','yuksek']);self.assert_step('A5','FAIL','-ddQualityTier')
        self.command(['Player','-ddQaBotStability']);self.assert_step('A5','FAIL','-ddQualityTier')
    def test_command_case_insensitive(self):
        self.command(['Player','-ddQaBotStability','-ddQualityTier','Sinematik']);r=self.result();self.assertTrue(r['pass'],r['failures'])
    def test_dirty_host_fails(self):
        self.f.replace_json(self.m['checklist']['A5']['runs'][0]['provenance'],lambda x:x.update(hostClean=False));self.f.anchor(self.m)
        self.assert_step('A5','FAIL','host')


class K6WorldStallTests(Fixture):
    """C-00 30.09 K6: same-spot stall clusters in A2/A5 logs gate A3 world; unmeasurable = ölçülmedi, never PASS."""
    STUCK='\n'.join(f'[QaRunBot] t pos=10.0,0.0,5.0 time={30.0+n:.1f} mode=patrol near=0' for n in range(8))+'\n[QaRunBot] stall-recovery mode=patrol pos=10.0,0.0,5.0 time=33.0 near=0 goal=1,0,1'
    def log(self,step,index,text):
        run=self.m['checklist'][step]['runs'][index]
        run['log']=self.f.put(f'world-{step}-{index}.log',text.encode());self.f.anchor(self.m)
    def test_cluster_fails_world(self):
        self.log('A5',1,TRACE+'\n'+self.STUCK)
        r=self.assert_step('A3','FAIL','same-spot stall')
        self.assertEqual(r['checklist']['A3']['areas']['world']['status'],'FAIL')
        self.assertEqual(r['checklist']['A3']['areas']['world']['clusters'][0]['seconds'],7.0)
    def test_unmeasurable_log_not_run(self):
        self.log('A2',0,'\n'.join(f'[Camera] inside-collider round={n} frames=0/60' for n in range(1,6)))
        r=self.assert_step('A3','BLOCKED','ölçülmedi')
        self.assertEqual(r['checklist']['A3']['areas']['world']['status'],'NOT_RUN')
        self.assertNotEqual(r['checklist']['A3']['status'],'PASS')
    def test_missing_logs_never_pass(self):
        self.m['checklist']['A5']['runs'].pop();r=self.result()
        self.assertEqual(r['checklist']['A3']['areas']['world']['status'],'NOT_RUN');self.assertNotEqual(r['checklist']['A3']['status'],'PASS')
    def test_report_run_cluster_is_observed_only(self):
        run=self.m['reportRuns'][0];run['log']=self.f.put('world-report.log',(TRACE+'\n'+self.STUCK).encode());self.f.anchor(self.m)
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        self.assertTrue(any(x['clusters'] and not x['gate'] for x in r['checklist']['A3']['areas']['world']['runs']))
    def test_missing_stuck_counter_blocked(self):
        self.f.replace_json(self.m['checklist']['A5']['runs'][0]['summary'],lambda x:x.pop('stuckRecoveries'));self.f.anchor(self.m)
        self.assert_step('A5','BLOCKED','stuckRecoveries')


class K8LedgerTests(Fixture):
    """C-00 30.09 K8: mechanical discard rule from the hash-bound attempts ledger; single root."""
    def test_preflight_discard_listed(self):
        self.add_attempt('perf-yuksek-tp-4',status='preflight-failed',exitCode=75)
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        self.assertEqual([(d['dir'],d['discard']) for d in r['perf']['discards']],[('perf-yuksek-tp-4','preflight-failed')])
        cell=[c for c in r['perf']['cells'] if (c['tier'],c['mode'])==('yuksek','tp')][0];self.assertEqual((cell['counted'],cell['discards']),(3,1))
    def test_host_contaminated_discard(self):
        self.add_attempt('perf-sinematik-fp-4',status='failed',exitCode=0,hostClean=False,timedOut=False,missingArtifacts=[],guard=copy.deepcopy(GUARD))
        r=self.result();self.assertTrue(r['pass'],r['failures']);self.assertEqual(r['perf']['discards'][0]['discard'],'host-contaminated')
    def test_non_discardable_attempts_fail(self):
        for n,fields in enumerate((dict(status='failed',exitCode=1,hostClean=False,timedOut=False,missingArtifacts=[]),
                                   dict(status='failed',exitCode=0,hostClean=False,timedOut=True,missingArtifacts=[]),
                                   dict(status='failed',exitCode=0,hostClean=False,timedOut=False,missingArtifacts=['04-window.json']),
                                   dict(status='failed',exitCode=0,hostClean=True,timedOut=False,missingArtifacts=[]),
                                   dict(status='preflight-failed',exitCode=0),dict(status='starting',exitCode=0))):
            with self.subTest(fields=fields):
                self.setUp();self.add_attempt(f'perf-performans-topdown-{4+n}',**fields)
                self.assert_step('A4','FAIL','non-discardable')
    def test_ledger_missing_attempt_blocked(self):
        lines=(self.root/self.m['checklist']['A4']['ledger']['path']).read_text().splitlines()
        kept=[l for l in lines if 'id-yuksek-fp-2' not in l]
        self.m['checklist']['A4']['ledger']=self.f.put('perf/attempts-ledger.jsonl',('\n'.join(kept)+'\n').encode());self.f.anchor(self.m)
        self.assert_step('A4','BLOCKED','ledger/disk')
    def test_ledger_orphan_blocked(self):
        self.m['checklist']['A4']['ledger']=self.f.write_ledger([dict(attemptId='ghost',kind='perf',status='complete',exitCode=0,dir='perf-yuksek-fp-9')])
        self.f.anchor(self.m);self.assert_step('A4','BLOCKED','orphan')
    def test_ledger_hash_bound(self):
        path=self.root/self.m['checklist']['A4']['ledger']['path'];path.write_text(path.read_text()+'\n');self.assert_step('A4','FAIL','hash')
    def test_second_root_blocked(self):
        import shutil
        shutil.copytree(self.root/'perf/perf-yuksek-tp-1',self.root/'other/perf-yuksek-tp-1');self.assert_step('A4','BLOCKED','single FINAL perf root')
    def test_ledger_must_be_root_ledger(self):
        self.m['checklist']['A4']['ledger']=self.f.put('elsewhere/attempts-ledger.jsonl',(self.root/'perf/attempts-ledger.jsonl').read_bytes())
        self.f.anchor(self.m);self.assert_step('A4','BLOCKED','single perf root')


class SecondaryPerfRootTests(Fixture):
    """WP-091 (C-00 05.10): the chain's own perf roots beside the gate root — the R1 baseline of an R2 round (P2) and
    S-05's B9 poses — are accepted only where declared at their fixed place, and only when no FINAL gate window hides there."""
    R1BIN='c'*64
    def copy_cell(self, rel, commit=FINAL, argv=None, status=None, binary=None):
        import shutil
        d=self.root/rel/'perf-yuksek-tp-1';shutil.copytree(self.root/'perf/perf-yuksek-tp-1',d)
        data=json.loads((d/'attempt.json').read_text());data['executionCommit']=commit
        if binary:data['binarySha256']=binary
        if status:data['status']=status
        (d/'attempt.json').write_text(json.dumps(data))
        if argv is None:(d/'command.json').unlink()
        else:(d/'command.json').write_text(json.dumps(argv))
    def declare(self,*entries):
        self.m['checklist']['A4']['secondaryRoots']=list(entries)
    def test_declared_r1_baseline_root_is_accepted(self):
        self.copy_cell('a4/perf-r1',commit=OTHER,argv=['probe','-ddQualityTier','yuksek'],binary=self.R1BIN)
        self.declare(dict(role='r1-baseline',path='a4/perf-r1',commit=OTHER,binarySha256=self.R1BIN))
        r=self.result();self.assertTrue(r['pass'],r['failures'])
    def test_a_baseline_window_on_finals_build_fails(self):
        self.copy_cell('a4/perf-r1',commit=OTHER,argv=['probe'])   # the R1 commit label, but FINAL's probe binary
        self.declare(dict(role='r1-baseline',path='a4/perf-r1',commit=OTHER,binarySha256=self.R1BIN))
        self.assert_step('A4','FAIL','declared R1 build')
    def test_the_baseline_build_is_never_finals_probe(self):
        self.copy_cell('a4/perf-r1',commit=OTHER,argv=['probe'])
        self.declare(dict(role='r1-baseline',path='a4/perf-r1',commit=OTHER,binarySha256=self.f.binaries['probe']['artifactSha256']))
        self.assert_step('A4','FAIL',"never FINAL's probe")
    def test_undeclared_r1_baseline_root_is_a_stray_root(self):
        self.copy_cell('a4/perf-r1',commit=OTHER,argv=['probe'])
        self.assert_step('A4','BLOCKED','single FINAL perf root')
    def test_a_final_window_in_the_baseline_root_fails(self):
        self.copy_cell('a4/perf-r1',commit=FINAL,argv=['probe'],binary=self.R1BIN)
        self.declare(dict(role='r1-baseline',path='a4/perf-r1',commit=OTHER,binarySha256=self.R1BIN))
        self.assert_step('A4','FAIL','declared R1 commit')
    def test_the_baseline_commit_is_never_final(self):
        self.copy_cell('a4/perf-r1',commit=FINAL,argv=['probe'],binary=self.R1BIN)
        self.declare(dict(role='r1-baseline',path='a4/perf-r1',commit=FINAL,binarySha256=self.R1BIN))
        self.assert_step('A4','FAIL','own 40-hex commit, never FINAL')
    def test_a_secondary_root_off_its_fixed_place_fails(self):
        self.copy_cell('other',commit=OTHER,argv=['probe'])
        for entry in (dict(role='r1-baseline',path='other',commit=OTHER),dict(role='spare',path='other'),dict(role='b9-pose',path='b9/../other')):
            with self.subTest(entry=entry):
                self.declare(entry);self.assert_step('A4','FAIL','fixed place')
    START='-48,-48,45'
    def test_b9_pose_root_needs_its_recorded_start_on_every_window(self):
        self.copy_cell('b9/sw-corner',argv=['probe','-ddPilotStart',self.START])
        self.declare(dict(role='b9-pose',path='b9/sw-corner',start=self.START))
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        (self.root/'b9/sw-corner/perf-yuksek-tp-1/command.json').write_text(json.dumps(['probe','-ddQualityTier','yuksek']))
        self.assert_step('A4','FAIL','-ddPilotStart')
    def test_b9_window_at_another_start_than_its_pose_fails(self):
        self.copy_cell('b9/sw-corner',argv=['probe','-ddPilotStart','0,-4,0'])
        self.declare(dict(role='b9-pose',path='b9/sw-corner',start=self.START))
        self.assert_step('A4','FAIL','another start')
    def test_b9_root_is_declared_with_its_start(self):
        self.copy_cell('b9/sw-corner',argv=['probe','-ddPilotStart',self.START])
        self.declare(dict(role='b9-pose',path='b9/sw-corner'))
        self.assert_step('A4','FAIL','declared with the start')
    def test_b9_window_not_run_at_final_fails(self):
        self.copy_cell('b9/sw-corner',commit=OTHER,argv=['probe','-ddPilotStart',self.START])
        self.declare(dict(role='b9-pose',path='b9/sw-corner',start=self.START))
        self.assert_step('A4','FAIL','not run at FINAL')
    def test_b9_window_that_never_started_a_player_is_accepted_but_a_complete_one_needs_its_argv(self):
        self.copy_cell('b9/plaza-n',argv=None,status='preflight-failed')
        self.declare(dict(role='b9-pose',path='b9/plaza-n',start='0,30,180'))
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        data=json.loads((self.root/'b9/plaza-n/perf-yuksek-tp-1/attempt.json').read_text());data['status']='complete'
        (self.root/'b9/plaza-n/perf-yuksek-tp-1/attempt.json').write_text(json.dumps(data))
        self.assert_step('A4','FAIL','without its Player argv')
    def test_an_unreadable_b9_attempt_is_blocked(self):
        self.copy_cell('b9/plaza-n',argv=None,status='preflight-failed')
        (self.root/'b9/plaza-n/perf-yuksek-tp-1/attempt.json').write_text('{torn')
        self.declare(dict(role='b9-pose',path='b9/plaza-n',start='0,30,180'))
        self.assert_step('A4','BLOCKED','unreadable pose-window attempt')
    def test_z3_pose_root_needs_its_start_and_the_full_armour_crowd(self):
        self.copy_cell('z3/perf',argv=['probe','-ddPilotStart','0,-4,0','-ddArmor','all'])
        self.copy_cell('z3/perf-r1',commit=OTHER,argv=['probe','-ddPilotStart','0,-4,0','-ddArmor','all'],binary=self.R1BIN)
        self.declare(dict(role='z3-pose',path='z3/perf',start='0,-4,0'),
                     dict(role='r1-baseline',path='z3/perf-r1',commit=OTHER,binarySha256=self.R1BIN))
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        (self.root/'z3/perf/perf-yuksek-tp-1/command.json').write_text(json.dumps(['probe','-ddPilotStart','0,-4,0']))
        self.assert_step('A4','FAIL','-ddArmor all')
    def test_a_declared_root_that_is_not_there_is_blocked(self):
        self.declare(dict(role='b9-pose',path='b9/gone',start=self.START))
        self.assert_step('A4','BLOCKED','missing secondary perf root')
    def test_a_pose_window_in_the_gate_root_fails(self):
        # KADRO test-engineer MUST-2: a B9 root moved into the gate root must not pass A4 — the gate reads its hash-bound argv
        for flag,value in (('-ddPilotStart',self.START),('-ddArmor','all')):
            with self.subTest(flag=flag):
                self.setUp()
                cell=self.root/'perf/perf-yuksek-tp-2'
                (cell/'command.json').write_text(json.dumps(['probe','-ddQualityTier','yuksek',flag,value]))
                data=json.loads((cell/'attempt.json').read_text());data['commandSha256']=hashlib.sha256((cell/'command.json').read_bytes()).hexdigest()
                (cell/'attempt.json').write_text(json.dumps(data));self.rehash_perf()
                self.assert_step('A4','FAIL',flag)
    def test_a_window_hidden_in_an_app_folder_outside_the_builds_is_a_stray_root(self):
        import shutil
        shutil.copytree(self.root/'perf/perf-yuksek-tp-1',self.root/'hidden.app/perf/perf-yuksek-tp-1')
        self.assert_step('A4','BLOCKED','single FINAL perf root')


class K9StallTests(Fixture):
    """C-00 30.09 K9: per-tier stallMs recomputed from the CSV; native 33.33 is a cross-check; raw p99 escalation."""
    def test_sinematik_frames_between_33_and_40_pass(self):
        values=[30.0]*11700+[36.0]*300           # 2.5 % over 33.33 ms, none over the Sinematik stallMs 40
        self.set_csv('perf-sinematik-tp-1',values,frameMeanMs=sum(values)/len(values));self.rehash_perf()
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        w=[w for c in r['perf']['cells'] for w in c['windows'] if w['dir']=='perf-sinematik-tp-1'][0]
        self.assertEqual((w['stallMs'],w['stalls'],w['fractionOver33ms'],w['rawP99Ms'],w['maxMs']),(40.0,0,0.025,36.0,36.0))
        self.assertFalse(w['rawP99OverLimit']);self.assertIn('p999Ms',w)
    def test_sinematik_stalls_over_40_cap(self):
        values=[30.0]*11800+[45.0]*200           # 1.7 % over 40 ms
        self.set_csv('perf-sinematik-fp-2',values);self.rehash_perf();self.assert_step('A4','FAIL','tier limit')
    def test_raw_p99_over_limit_flag(self):
        values=[10.0]*11860+[25.0]*80+[40.0]*60   # raw p99 25 > 20, filtered (≤ 33.33) p99 10 ≤ 20, stalls 0.5 %
        self.set_csv('perf-yuksek-topdown-1',values);self.rehash_perf()
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        self.assertEqual([(e['dir'],e['rawP99Ms']) for e in r['perf']['escalations']],[('perf-yuksek-topdown-1',25.0)])
    def test_native_cross_check_still_binds(self):
        values=[10.0]*12000
        self.set_csv('perf-yuksek-fp-1',values,framesExcludedAsStalls=3);self.rehash_perf();self.assert_step('A4','FAIL','native stall count')
    def test_tier_stall_fallback(self):
        targets=copy.deepcopy(self.f.targets);self.assertEqual(self.module.tier_stall_ms(targets,'sinematik'),40.0)
        for tier in targets['tiers'].values():tier.pop('stallMs',None)
        self.assertEqual(self.module.tier_stall_ms(targets,'sinematik'),targets['stallMs'])


class RouteRunTests(Fixture):
    """C-00 30.09 R1–R3: A2 frames come from one QaRunBot route run per camera mode."""
    def summary(self,index,mutate):
        run=self.m['checklist']['A2']['routeRuns'][index]
        def change(x):
            for row in x['routeFrames']:mutate(row)
        self.f.replace_json(run['summary'],change);self.f.anchor(self.m)
    def test_gameplay_runs_need_no_frames(self):
        self.assertTrue(all('frames' not in run for run in self.m['checklist']['A2']['runs']))
        r=self.result();self.assertTrue(r['pass'],r['failures']);self.assertEqual(len(r['routeRuns']),3)
    def test_missing_route_runs_blocked(self):
        self.m['checklist']['A2']['routeRuns'].pop();self.assert_step('A2','BLOCKED','route runs')
    def test_topdown_horizon_must_be_not_applicable(self):
        run=self.m['checklist']['A2']['routeRuns'][0];run['frames']['horizon']=self.f.png('route-extra.png');self.f.anchor(self.m)
        self.assert_step('A2','FAIL','notApplicable')
    def test_native_status_path_mode_roof(self):
        for mutate,reason in ((lambda r:r.update(status='blocked') if r['key']=='kairo' else None,'status'),
                              (lambda r:r.update(path='frames/route-x.png') if r['key']=='courtyard' else None,'path'),
                              (lambda r:r.update(mode='TopDown') if r['key']=='corridor' else None,'camera mode'),
                              (lambda r:r.update(roofAbove=False) if r['key']=='corridor' else None,'roof')):
            with self.subTest(reason=reason):
                self.setUp();self.summary(1,mutate);self.assert_step('A2','FAIL',reason)
    def test_native_route_key_missing_blocked(self):
        run=self.m['checklist']['A2']['routeRuns'][2]
        self.f.replace_json(run['summary'],lambda x:x.update(routeFrames=[r for r in x['routeFrames'] if r['key']!='horizon']));self.f.anchor(self.m)
        self.assert_step('A2','BLOCKED','no horizon')
    def test_duplicate_frame_bytes(self):
        run=self.m['checklist']['A2']['routeRuns'][1]
        data=(self.root/run['frames']['courtyard']['path']).read_bytes()
        run['frames']['corridor']=self.f.put(run['frames']['corridor']['path'],data);self.f.anchor(self.m)
        self.assert_step('A2','FAIL','same frame bytes')
    def test_route_exit_and_guard(self):
        run=self.m['checklist']['A2']['routeRuns'][0];run['originalExit']=3;self.assert_closed()
    def test_input_lost_gameplay_run_is_named_blocked(self):
        # S-02 01.10 input-loss contract: inputMeasurable=false → BLOCKED "input lost", before any exit/status rule
        run=self.m['checklist']['A2']['runs'][2]
        self.f.replace_json(run['summary'],lambda x:x.update(inputMeasurable=False,padDisabledFrames=0,pointerAfterPadFrames=1587,unfocusedFrames=9,**{'pass':False}))
        self.f.replace_json(run['provenance'],lambda x:x.update(status='unmeasurable',exitCode=3));run['originalExit']=3;self.f.anchor(self.m)
        self.assert_step('A2','BLOCKED','input lost (unmeasurable, rerun): pad disabled 0 / pointer after pad 1587')
    def test_route_guard_unverified(self):
        run=self.m['checklist']['A2']['routeRuns'][0];run['guard']['verified']=0
        self.f.replace_json(run['provenance'],lambda x:x.update(guard=copy.deepcopy(run['guard'])));self.f.anchor(self.m)
        self.assert_step('A2','FAIL','guard')
    def test_attempt_guard_is_authoritative(self):
        # wp078_bot receipts: verified bool, outOfScopeChanged bool; a manifest copy must repeat the attempt's receipt.
        run=self.m['checklist']['A2']['routeRuns'][1]
        receipt=dict(snapshotExit=0,restoreExit=0,verified=True,outOfScopeChanged=False,touched='3',verdict='ok')
        self.f.replace_json(run['provenance'],lambda x:x.update(guard=receipt,kind='route',buildRole='dev',mode='tp'));run.pop('guard');self.f.anchor(self.m)
        r=self.result();self.assertTrue(r['pass'],r['failures'])
        run['guard']=dict(GUARD,verified=0);self.assert_step('A2','FAIL','guard')
    def test_attempt_kind_must_match(self):
        run=self.m['checklist']['A2']['routeRuns'][1]
        self.f.replace_json(run['provenance'],lambda x:x.update(kind='gameplay'));self.f.anchor(self.m);self.assert_step('A2','FAIL','attempt kind')
    def test_blocked_point_run_names_the_point(self):
        # prova 30.09: QaRunBot exits 3 when a route point is blocked; the BLOCKED reason names the point and its native reason
        run=self.m['checklist']['A2']['routeRuns'][1]
        self.f.replace_json(run['summary'],lambda x:[r.update(status='blocked',path='',reason='point not reached in 45 s') for r in x['routeFrames'] if r['key']=='kairo'])
        self.f.replace_json(run['provenance'],lambda x:x.update(status='failed',exitCode=3));run['originalExit']=3;self.f.anchor(self.m)
        self.assert_step('A2','BLOCKED','blocked kairo: point not reached in 45 s')
    def test_route_exit_3_without_blocked_point_fails(self):
        run=self.m['checklist']['A2']['routeRuns'][1]
        self.f.replace_json(run['provenance'],lambda x:x.update(status='failed',exitCode=3));run['originalExit']=3;self.f.anchor(self.m)
        self.assert_step('A2','FAIL','without a blocked route point')
    def test_route_needs_native_route_mode(self):
        run=self.m['checklist']['A2']['routeRuns'][2]
        self.f.replace_json(run['summary'],lambda x:x.update(runMode='normal'));self.f.anchor(self.m);self.assert_step('A2','FAIL','route run')


if __name__=='__main__':unittest.main()


class RoundTimeoutMarginTests(unittest.TestCase):
    """K5 (C-00 30.09): the soak's slowest round against the 420 s round timeout is a report line, never a gate."""

    def test_margin_is_measured_from_the_slowest_round(self):
        sys.path.insert(0, str(HERE))
        import wp078_report as report
        row = report.round_timeout_margin({"seed": 61, "qualityTier": "Sinematik",
                                            "rounds": [{"round": 1, "seconds": 80.0}, {"round": 12, "seconds": 315.0}]})
        self.assertEqual((315.0, 105.0, 0.25), (row["slowestRoundSeconds"], row["marginSeconds"], row["marginFraction"]))
        self.assertEqual(420.0, row["timeoutSeconds"])

    def test_no_round_seconds_leaves_the_margin_unmeasured(self):
        sys.path.insert(0, str(HERE))
        import wp078_report as report
        row = report.round_timeout_margin({"seed": 26, "rounds": []})
        self.assertIsNone(row["marginSeconds"])


class R2RoundTests(Fixture):
    """WP-091 (C-00 05.10): an R2 manifest also carries the r2-gates.json gates; an R1 manifest has no R2 row."""

    def test_r1_manifest_has_no_r2_row(self):
        r = self.result()
        self.assertTrue(r['pass'], r['failures'])
        self.assertNotIn('R2', r['checklist'])

    def test_r2_manifest_without_artifacts_is_blocked_per_gate_with_owner(self):
        self.m['round'] = 'R2'
        r = self.result()
        self.assertFalse(r['pass'])
        self.assertEqual('BLOCKED', r['checklist']['R2']['status'])
        steps = {x['step']: x['owner'] for x in r['failures']}
        self.assertEqual('S-06', steps['R2.L1'])
        self.assertEqual('S-08', steps['R2.S1'])
        self.assertNotIn('R2.P2', steps, 'report entries never fail')

    def test_bound_measurement_closes_its_gate_and_a_bad_one_fails(self):
        self.m['round'] = 'R2'
        ok = self.f.put_json({'schema': 'dd-r2-measure-v1', 'gate': 'L1', 'commit': FINAL, 'values': {'avg': 0.03, 'p95': 0.07}}, 'r2-L1.json')
        bad = self.f.put_json({'schema': 'dd-r2-measure-v1', 'gate': 'S1', 'commit': FINAL, 'values': {'below400ShareMin': 0.12}}, 'r2-S1.json')
        self.m['checklist']['R2'] = {'gates': {'L1': {'measure': ok}, 'S1': {'measure': bad}}}
        self.f.anchor(self.m)
        r = self.result()
        rows = {g['id']: g for g in r['checklist']['R2']['gates']}
        self.assertEqual('PASS', rows['L1']['status'])
        self.assertEqual('FAIL', rows['S1']['status'])
        self.assertEqual('FAIL', r['checklist']['R2']['status'])

    def test_a_quiet_gate_on_a_busy_host_keeps_its_status_and_says_kirli(self):
        # KADRO game-test-engineer SHOULD-6 / S-08 06.10: KİRLİ is a note; the owner's quality gates decide the value
        self.m['round'] = 'R2'
        values = {'openRt60MinS': 0.6, 'openRt60MaxS': 1.0, 'corridorRt60MinS': 0.9, 'corridorRt60MaxS': 1.6,
                  'hallRt60MinS': 1.3, 'hallRt60MaxS': 2.2, 'orderViolations': 0}
        s4 = self.f.put_json({'schema': 'dd-r2-measure-v1', 'gate': 'S4', 'commit': FINAL, 'values': values}, 'r2-S4.json')
        dirty = self.f.put_json({'schema': 'dd-r2-host-v1', 'quietAtStart': False, 'samples': 9, 'load1Max': 12.0,
                                 'foreignPlayerMax': 0, 'dirty': True, 'dirtyWhy': ['start not quiet after 1800 s: load1 12.00']},
                                'host-dirty.json')
        clean = self.f.put_json({'schema': 'dd-r2-host-v1', 'quietAtStart': True, 'samples': 9, 'dirty': False, 'dirtyWhy': []},
                                'host-clean.json')
        rows = {}
        for name, entry in (('none', {'measure': s4}), ('dirty', {'measure': s4, 'host': dirty}),
                            ('clean', {'measure': s4, 'host': clean})):
            self.m['checklist']['R2'] = {'gates': {'S4': entry}}
            self.f.anchor(self.m)
            rows[name] = {g['id']: g for g in self.result()['checklist']['R2']['gates']}['S4']
        self.assertEqual(['PASS'] * 3, [rows[n]['status'] for n in ('none', 'dirty', 'clean')])
        self.assertEqual('KİRLİ: start not quiet after 1800 s: load1 12.00', rows['dirty']['note'])
        self.assertEqual((True, 12.0), (rows['dirty']['host']['dirty'], rows['dirty']['host']['load1Max']))
        self.assertEqual({'foreignUnityMax', 'foreignPlayerMax', 'foreignBlenderMax', 'foreignFfmpegMax'},
                         {k for k in rows['dirty']['host'] if k.startswith('foreign')})   # all four kinds (gt Ek 4)
        self.assertNotIn('note', rows['clean'])
        self.assertEqual('KİRLİ: no host record bound for this quiet measurement', rows['none']['note'])
        # the text report names it too
        self.m['checklist']['R2'] = {'gates': {'S4': {'measure': s4, 'host': dirty}}}
        self.f.anchor(self.m)
        manifest = Path(self.temp.name) / 'manifest-kirli.json'
        manifest.write_text(json.dumps(self.m))
        out = Path(self.temp.name) / 'report-kirli'
        self.module.main(['--manifest', str(manifest), '--evidence-root', str(self.root), '--out', str(out)])
        self.assertIn('R2.S4 KİRLİ: start not quiet after 1800 s: load1 12.00', (out / 'wp078-report.txt').read_text().splitlines())

    def test_tampered_measurement_is_refused(self):
        self.m['round'] = 'R2'
        ref = self.f.put_json({'schema': 'dd-r2-measure-v1', 'gate': 'L1', 'commit': FINAL, 'values': {'avg': 0.03, 'p95': 0.07}}, 'r2-L1.json')
        self.m['checklist']['R2'] = {'gates': {'L1': {'measure': ref}}}
        self.f.anchor(self.m)
        (self.root / ref['path']).write_text(json.dumps({'schema': 'dd-r2-measure-v1', 'gate': 'L1', 'commit': FINAL, 'values': {'avg': 0.5, 'p95': 0.9}}))
        r = self.result()
        self.assertFalse(r['pass'])
        self.assertNotEqual('PASS', r['checklist']['R2']['status'])


class ModesTests(Fixture):
    """C-00 06.10 (user: top down only, tests too): the manifest's modes (wp078_accept --modes) narrow A2 and the R2
    gates; the left-out modes and the FP / TP-only gates are OUT_OF_SCOPE, never FAIL or BLOCKED."""
    OUT = ('H1b', 'H1c', 'H4', 'H4a', 'H4b', 'H6')

    def topdown_only(self):
        self.m['modes'] = ['topdown']
        c = self.m['checklist']['A2']
        c['runs'], c['routeRuns'] = c['runs'][:1], c['routeRuns'][:1]

    def test_a_topdown_run_passes_a2_on_the_two_topdown_runs_and_names_tp_fp(self):
        self.topdown_only()
        r = self.result()
        self.assertEqual('PASS', r['checklist']['A2']['status'], r['failures'])
        self.assertNotIn('A2', [x['step'] for x in r['failures']])
        self.assertEqual(['topdown'], r['modes'])
        out = {'modes': ['tp', 'fp'], 'reason': self.module.R2.SCOPE_REASON}
        self.assertEqual((out, out), (r['outOfScope'], r['checklist']['A2']['outOfScope']))

    def test_a_topdown_run_with_tp_and_fp_runs_bound_is_not_a_pass(self):
        self.m['modes'] = ['topdown']
        self.assert_step('A2', 'BLOCKED', 'normal-bot modes')

    def test_every_mode_is_the_default_and_names_nothing_out_of_scope(self):
        r = self.result()
        self.assertTrue(r['pass'], r['failures'])
        self.assertEqual(['topdown', 'tp', 'fp'], r['modes'])
        self.assertNotIn('outOfScope', r)

    def test_malformed_modes_close_the_report(self):
        for bad in (['td'], [], ['tp', 'tp'], 'topdown'):
            with self.subTest(modes=bad):
                self.setUp()
                self.m['modes'] = bad
                self.assert_step('A8', 'FAIL', 'manifest modes')

    def test_r2_fp_and_tp_only_gates_are_out_of_scope_not_blocked(self):
        self.topdown_only()
        self.m['round'] = 'R2'
        r = self.result()
        rows = {g['id']: g for g in r['checklist']['R2']['gates']}
        self.assertEqual({g: 'OUT_OF_SCOPE' for g in self.OUT}, {g: rows[g]['status'] for g in self.OUT})
        steps = [x['step'] for x in r['failures']]
        self.assertFalse([s for s in steps if s in ['R2.' + g for g in self.OUT]], steps)
        self.assertIn('R2.L1', steps)                                   # a TD gate with no artifact is still BLOCKED
        self.assertEqual((['topdown'], ['tp']), (rows['Z3']['modes'], rows['Z3']['outOfScopeModes']))
