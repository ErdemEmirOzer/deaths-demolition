"""Independent synthetic A8 acceptance tests; no game/runtime execution."""
import copy
import hashlib
import importlib.util
import json
import subprocess
import sys
import struct
import inspect
import zlib
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
FINAL = 'a' * 40
OTHER = 'b' * 40
AREAS = 'projectile penetration launcher arsenal gel chain ragdoll zombieAttacks animation camera world aiFlags progression audio ui readability'.split()
DOMAIN_IDS = {'projectile': ['DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.HizliMermi_InceDuvardan_Gecemez_TekUzunAdimdaBile', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.KlasikSilah_Projectile_Atar_VeHedefiVurur', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermi_UcusSuresindenSonraHasarVerir_TekIsabet', 'DD.Prototype.Tests.PlayMode.ProjectileSliceTests.Mermiler_UcarkenVeCarparken_KareBasinaAyirmaYok'], 'penetration': ['DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitliCollider_RegistryOnce', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.BolgeCozumu_KayitsizCollider_AcikFallback', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventClassifier_OnSinifTablosu', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageEventMeta_BeforeAfterVeKirilim', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.DamageTransaction_MonotonVeIcIceSayilir', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.IsEmpty_YalnizHicbirSeyOlmadiginda', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.KaynakTaramasi_HasarYolundaKareBasinaAramaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kirilma_Idempotent_IkinciCagriKirmaz', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Kritik_YalnizAcikKafaDogrudan_PatlamadaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.OluBeden_IkinciOlumVeKirilmaYok', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Parca_YalnizKendiBolgesiniKorur', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_EnjekteOranlar_ToplamKorunurDeterministik', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_IkiParca_IkiPayZirhaAcikPayCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_KirilmaTasmasi_AyniPatlamadaCanaGecmez', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_TekParca_KorunanPayZirhaAcikPaylarCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Patlama_UcParca_SonraBiriKirik_AcikPayCana', 'DD.Prototype.Tests.EditMode.RegionalArmorSliceTests.Tasma_HerSlottaCanaGecmez', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.AyniTransaction_IkinciUygulama_Reddedilir', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.DogrudanIsabet_GercekHitbox_BolgeSlotYonlendirmesi', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.IkinciRunSahnesi_HasarAboneleriCogalmaz', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Kompozisyon0123_Deterministik_HavuzIkinciYasam_Pause', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.OluDusman_GecHasar_YeniOlumGoldKillYok_StandardKill', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_AyriCanYoluYok_BirincilHedefTekOlay', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.Patlama_DusmanBasinaTekUygulama_TekDressing_ButceCogalmaz', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.PatlamaylaOlum_TekOlum_KritikDegil', 'DD.Prototype.Tests.PlayMode.RegionalArmorSliceTests.VurusYolu_SifirAyirma'], 'launcher': ['DD.Prototype.Tests.EditMode.RocketMathTests.Patlama_DogrusalDusus_VeKendineHasar', 'DD.Prototype.Tests.EditMode.RocketMathTests.Rl1_Etkin_RoketBlogu_VeDallari', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Cluster_Bombaciklari_PatlamaEtrafinda', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.DogrudanIsabet_VePatlama_AyriIslemde_HedefIkisiniDeAlir', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Kontrolcu_Rl1_RoketAtar', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.KurmaMesafesiIcinde_Dud_30Hasar_PatlamaYok', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Oyuncu_KendiPatlamasindan_Yuzde45_Alir', 'DD.Prototype.Tests.PlayMode.RocketSliceTests.Ucus_Itki_VeHavadaPatlama_70m'], 'arsenal': ['DD.Prototype.Tests.EditMode.ArsenalB2Tests.Burst_UcAtis_005Aralik_SonraGecikme026', 'DD.Prototype.Tests.EditMode.ArsenalB2Tests.TekFisek_KabukKabukDoler_TetikKeser_YuklenenlerKalir', 'DD.Prototype.Tests.EditMode.ArsenalB2Tests.Yedek_TurBasiDolar_AsagiInmez_BosKalincaTabanBirSarjor', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Havuz_TavanDolunca_EnEskiEmekli_SayilirSayim', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Islem_SurdurmeKimligi_AcmadanVerilir_DevamEder', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Katalog_OnTanim_Gecerli_KimliklerBenzersiz', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.KonfigKopyasi_TanimSayilariniTasir_KonseySayilari', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_Deterministik_AyniGirdi_AyniYol', 'DD.Prototype.Tests.EditMode.ArsenalV3Tests.Mermi_MenzilSonundaSoner_TamMenzilKatEder', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Egri_TabandanBaslar_MonotonArtar_TavandaSabitlenir', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.GecSinir_PencereUstundenBuyuk_RoundBitisindenKucuk', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IkinciSpawn_ConfigtenOkunur_IhtimalAralikta', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanItibaren_IhtimalPozitif_GarantiDegil', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.IlkUygunTurdanOnce_IhtimalSifir', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.Nobetci_KasitliSapmayi_Yakalar', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.RoundKoprusu_IlerlemeyiKirpar_SaglayiciYokkenBagliDegil', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SabitTazelik_SpecialWeaponConfig_SevkiyatVarsayilanlaAyni', 'DD.Prototype.Tests.EditMode.SpecialWeaponContractTests.SpawnPenceresi_RoundIcinde_BaslangicVeBitisDisinda'], 'gel': ['DD.Prototype.Tests.EditMode.GelStateTests.DeganlerinSuresiEsitlenir_Tavan_EnEskiSoner_Expire', 'DD.Prototype.Tests.EditMode.GelStateTests.Icerisi_YukseklikBandi_EnYeniKazanir', 'DD.Prototype.Tests.PlayMode.GelSliceTests.Jel_KayarDuserOlur_Kaldirma0_UltraKalkar_Devrilme'], 'chain': ['DD.Prototype.Tests.EditMode.ChainTargetTests.KalkanKayanAtlanir_UltraZinciriDevamEttirir_Duvar', 'DD.Prototype.Tests.EditMode.ChainTargetTests.Sira_EnYakin_HopYaricapi_Tavan_AramaSiniriYok'], 'ragdoll': ['DD.Art.Tests.PlayMode.AnimationRagdollTests.Havuz_200Dongu_SizintiYok', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.Olum_ItkiYonundeDuser_Yerlesir_PozKalir_HavuzaDoner', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.SahneDegisimi_DusenRagdoll_HatasizBirakilir', 'DD.Art.Tests.PlayMode.AnimationRagdollTests.Tavan_Dolunca_YaziliDusus_HepsiBiter_SonraYenidenDogar'], 'zombieAttacks': ['DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.BiteGrabKapisi_ArkaDonukVeyaDuran_TekKapma', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.Inceleme_R1A_SertMenzil_AboneKorunur_AgirlikYonetimOncesi', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.SlotTahtasi_AgirStillerTekil_WindupTavani_Yayilim', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.TierIzinleri_ImzaStilNormalizasyonu', 'DD.Prototype.Tests.EditMode.AttackSelectorTests.UcKezAyniStilYok_ElDegisir_Deterministik', 'DD.Prototype.Tests.PlayMode.AttackVarietySliceTests.AltiStandard_YirmiSaniye_StilCesitliligi_UcTekrarYok_WindupTavani_TekKapma'], 'animation': ['DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz', 'DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_DonusteIceEgilir_DuzdeDik', 'DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_KadansBandi_KalabalikFarkliFaz_Kayar'], 'camera': ['DD.Prototype.Tests.PlayMode.CameraContractTests.KameraYawIle_HareketVeAnalogNisan_KameraTabaninaGore', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.DuvarIciSayaci_PozitifKontrol', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.TP_DuvarItmesi_KameraAslaDuvarIcindeDegil', 'DD.Prototype.Tests.PlayMode.CameraModesPlayTests.Tepeden_NisanIsiniEskiDuzlemIzdusumuyleAyni'], 'world': ['DD.Art.Tests.EditMode.NavMeshConnectivityTests.KopruGuvertesi_BedenKoridoruAcik_CitSokulmez_EtekBirlesik', 'DD.Art.Tests.EditMode.NavMeshConnectivityTests.NavMesh_TekParca_BedenErisilebilirAdacikYok_IsaretliNoktalarBaslangicaUlasir', 'DD.Art.Tests.EditMode.WorldBoundaryTests.HavuzCitleri_YurunebilirNavMeshiKesmez_BedenAdimiUstuPropUstundeNavMeshYok', 'DD.Art.Tests.EditMode.WorldBoundaryTests.Havuzlar_KenardanIceriKapsulTaramasi_CitVeyaGecitteDurur', 'DD.Art.Tests.EditMode.WorldBoundaryTests.OynanabilirAlan_ZeminDelikleriYalnizHavuzlarda', 'DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_CevreBoyuncaKapsulTaramasi_BoslukYok', 'DD.Art.Tests.EditMode.WorldBoundaryTests.SinirHalkasi_DortDuvar_IgnoreRaycast_OyuncuVeDusmaniDurdurur_AtisVeKamerayiEtkilemez'], 'aiFlags': ['DD.Prototype.Tests.EditMode.AttackSelectorTests.AiV3Kapali_YalnizImzaStil'], 'progression': ['DD.Prototype.Tests.EditMode.MetaPathTests.AnlikGoruntu_S09', 'DD.Prototype.Tests.EditMode.MetaPathTests.Katalog_Gecerli_Sayfalar_ve_Silahlar', 'DD.Prototype.Tests.EditMode.MetaPathTests.KayitHatasi_RamDegismez', 'DD.Prototype.Tests.EditMode.MetaPathTests.KosuDegistiricileri_DukkanVeAgac', 'DD.Prototype.Tests.EditMode.MetaPathTests.Respec_SilahlarKalir_IlkTam_SonraYuzde75', 'DD.Prototype.Tests.EditMode.MetaPathTests.SilahKilidi_Secim_SatiriSorgusu', 'DD.Prototype.Tests.EditMode.MetaPathTests.SilahSatinAlma_Atomik_Idempotan_SilahAcar', 'DD.Prototype.Tests.EditMode.MetaPathTests.TazeProfil_Durumlar', 'DD.Prototype.Tests.EditMode.MetaPathTests.YetersizKul_KilitliEtki_OnKosul_HicbirSeyYazmaz', 'DD.Prototype.Tests.EditMode.RunTreeTests.AnlikGoruntu_SatinAlinanKart_TekliftenDuser', 'DD.Prototype.Tests.EditMode.RunTreeTests.Anlik_Goruntu_S09_Alanlari_YeniAcilan_Yol', 'DD.Prototype.Tests.EditMode.RunTreeTests.Doruk_M1Oncesi_YalnizPinleTeklife_Girer', 'DD.Prototype.Tests.EditMode.RunTreeTests.Dukkan_AgaciKosuBasinaKurar_KeseyiOder_OcakKomutu', 'DD.Prototype.Tests.EditMode.RunTreeTests.EtkisiBaglanmamis_Kilitli_Satilmaz_Teklife_Girmez', 'DD.Prototype.Tests.EditMode.RunTreeTests.IcerikPaketi_HerDugumunAnahtari_Var', 'DD.Prototype.Tests.EditMode.RunTreeTests.Izgara_IkiSerit_4x4_BenzersizHucre_CekirdekDallarRoster', 'DD.Prototype.Tests.EditMode.RunTreeTests.KademeKisaAdlari_Benzersiz_TamKelime_OnIkiAlti', 'DD.Prototype.Tests.EditMode.RunTreeTests.Katalog_Gecerli_OnSilah_KisaAd12', 'DD.Prototype.Tests.EditMode.RunTreeTests.Kese_TurSonuYuzde10_YalnizSahipken', 'DD.Prototype.Tests.EditMode.RunTreeTests.Keystone_Kavsak_Dislama_Doruk_Kurallari', 'DD.Prototype.Tests.EditMode.RunTreeTests.Nisan_Yol_VeTeklifGarantisi', 'DD.Prototype.Tests.EditMode.RunTreeTests.Ocak_KartDisi_Fiyat_TurSiniri_Tavan_Hasar', 'DD.Prototype.Tests.EditMode.RunTreeTests.YeniDallar_EnumSozlesme_CekiliseGirmez'], 'audio': ['DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AkustikAlanlar_V6Haritasi_SalonKoridor_Histerezis_SahneYoksaOpen', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AnimasyonOlaylari_HerKlipOlayininSatiriVar_SilahFoleyAyrik', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AtesV3_KimlikTablosu_WP069AudioIdleri_HibritUcSilah_KuyrukMatrisi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.AyakSesi_PlakaVaryantlari_AdKurali', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_BesPoligon_HerMerkezKendiBolgesineCozulur', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.BolgeHaritasi_NoktaPoligonTesti', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.DarbeV3_YuzeyKalibreMatrisi_AdlarVeKazanc', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Firlatici_UcusDongusuEslemi_PitchRampasi_PatlamaKatmani', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.FoleyV3_SilahBasinaMekanizmaSeti_AileVeKovanEslemi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestLufsVeTepe_StandartIcinde_VeYenidenOlcumTutuyor', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Katalog_ManifestVar_DonusturucuIdempotan_VeYuklenebilir', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_AP017AileAdlari_KendiSesineCozulur', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Kirilabilirler_EventIdVeriden_AltiOlayKendiSesi_AdOlayIcindeInceltir', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.LoudnessMeter_TamOlcekSinus_BS1770Referansi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.Mixer_AltiGrupDortSnapshot_AcikParametreler_KopruEslemesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerCombatVeRunOlayiIcinSatirVar', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_HerSesinRecipeVeyaKatalogKarsiligiVar', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.OlayHaritasi_OyuncuUiAmbiyansMuzikSatirlari_TamVeTekil', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SilahFoleyV3_KlipSetineGore_MekanizmaEslemesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.SpecialKullanim_RaijuNamekuji_Satirlari_EskiKimliklerYok', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.TierVokalSeti_HerTierKendiKimligi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiOlaylari_HerUiEventUyesininSatiriVar_RouterHaritayaEsit', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UiV3_Sozlesme_YeniOlaylarBagli_RunCuelariDuckYok_SliderHizi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.UstKalite_24BitKaynak_GurultuTabani_Klip_DC_ImporterKalitesi', 'DD.Prototype.Tests.EditMode.Audio.AudioSliceEditModeTests.YuzeyEtiketleri_HerPlacementAilesininKuraliVar', 'DD.Prototype.Tests.PlayMode.AudioSliceTests.HavuzTavani_AsimdaDusurur_AktifSesKapasiteyiAsmaz', 'DD.Prototype.Tests.PlayMode.AudioSliceTests.SesYolu_KararliDurumda_SifirAyirma'], 'ui': ['DD.Prototype.Tests.EditMode.InputParityContractTests.InputSystem_YalnizInputKlasorunde', 'DD.Prototype.Tests.EditMode.UiCameraModesTests.KameraSatirlari_Sinirlanir_Kalici_EskiDosyaVarsayilan'], 'readability': ['DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet']}
AI_FLAGS = ("-ddAiV3", "-ddRagdoll", "-ddCorpseLinger", "-ddChainHops", "-ddGelPuddles", "-ddCrowdNeighbours", "-ddSpawnLos")
GUARD = dict(snapshotExit=0, restoreExit=0, verified=1, outOfScopeChanged=0)

class EvidenceFixture:
    def __init__(self, root):
        self.root = root
        self.serial = 0
        self.targets = json.loads((HERE / 'perf-targets.json').read_text())
        self.binaries = {role: self.put(role + '.bin', role.encode()) for role in ('dev', 'release')}
        self.manifest = dict(schema='dd-wp078-acceptance-v1', commit=FINAL, final=True,
            assetsTree='c'*40, buildHashes={k:v['artifactSha256'] for k,v in self.binaries.items()}, checklist={})
        for n in range(8):
            self.manifest['checklist']['A'+str(n)] = self.step(n)
        c = self.manifest['checklist']
        c['A0']['library'] = self.library()
        c['A1']['xml'] = {k:self.xml(k) for k in ('editMode', 'playMode')}
        c['A2']['runs'] = [self.run(m,61,5) for m in ('topdown','tp','fp')]
        c['A3']['checks'] = {area:self.step(3) for area in AREAS}
        for area,row in c['A3']['checks'].items():row['evidence'].append(self.domain_xml(area))
        c['A3']['checks']['aiFlags']['flagRuns']=[self.flag_run(flag,value) for flag in AI_FLAGS for value in (0,1)]
        c['A4'].update(perfRoot='perf', buildRole='dev')
        self.perf()
        c['A5']['runs'] = [self.run('topdown',s,12,True) for s in (61,26)]
        self.manifest['reportRuns'] = [self.run(m,61,5) for m,n in [('topdown',5),('tp',2),('fp',2)] for _ in range(n)]
        c['A5']['reportRuns'] = self.manifest['reportRuns']
        c['A6']['builds'] = {role:dict(library=self.library(), binary=ref, guard=copy.deepcopy(GUARD),
            provenance=self.provenance(role,compression='LZ4HC'),originalExit=0,
            buildReport=self.put_json(dict(result='Succeeded',mode=role,totalErrors=0,totalSize=1000,
                stamp=dict(stamped=True,commit=FINAL,describe=FINAL,mode=role)),role+'-build-report.json'),
            report=self.put_json({'ok':True,'sourceCommit':FINAL},role+'-report.json'))
            for role,ref in self.binaries.items()}
        c['A7'].update(byteEqual=True, integrityXml=self.xml('ProjectIntegrityTests.Check'))
        c['A4']['attempts']=[dict(path=str(p.relative_to(self.root)),artifactSha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted((self.root/'perf').glob('*/attempt.json'))]
        self.anchor(self.manifest)
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
        r=self.run('topdown',61,1);r.pop('frames')
        r.update(flag=flag,value=value,command=self.put_json(['DeathsDemolition',flag,str(value)]))
        return r
    def provenance(self, role='dev', **extra):
        return self.put_json(dict(status='complete',guard=copy.deepcopy(GUARD),executionCommit=FINAL,sourceCommit=FINAL,testedCommit=FINAL,exitCode=0,
            binarySha256=self.binaries[role]['artifactSha256'],hostClean=True,worldHalfExtent=55,**extra))
    def step(self,n):
        p=self.provenance()
        return dict(status='PASS',owner='S-10',executionCommit=FINAL,originalExit=0,provenance=p,
            evidence=[p]+([self.put_json({'pass':True,'executionCommit':FINAL})] if n==3 else []),guard=copy.deepcopy(GUARD))
    def run(self, mode, seed, count, stability=False):
        records=[dict(round=n,completed=True,outcome='completed',memMonoUsedMB=100,memTotalAllocatedMB=200,memRssMB=300) for n in range(1,count+1)]
        summary=dict(cameraMode=mode,qualityTier='Yuksek',seed=seed,roundsRequested=count,roundsCompleted=count,
            rounds=records,runMode='stability' if stability else 'normal',pass_=True,stabilityPass=True,deaths=0,
            timedOut=False,passAll=True,passed=True,stuckRecoveries=0)
        summary['pass']=True
        p=self.provenance()
        log='\n'.join(f'[Camera] inside-collider round={n} frames=0/60' for n in range(1,count+1))
        return dict(provenance=p,originalExit=0,guard=copy.deepcopy(GUARD),summary=self.put_json(summary),
            log=self.put(f'run-{self.serial}.log',log.encode()),buildRole='dev',
            frames={point:self.png(f'frame-{self.serial}-{mode}-{point}.png') for point in ('courtyard','corridor','horizon')})
    def png(self,name):
        def chunk(tag,data):return struct.pack('>I',len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
        raw=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\x00\xff\x00\x00'))+chunk(b'IEND',b'')
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
                    command=self.put_json({'argv':['synthetic']},d+'/command.json')
                    self.put_json(dict(executionCommit=FINAL,status='complete',exitCode=0,hostClean=True,timedOut=False,
                        binarySha256=self.binaries['dev']['artifactSha256'],guard=copy.deepcopy(GUARD),
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
                        if 'path' in v and 'artifactSha256' in v and v['path']!=value['provenance']['path']:refs[v['path']]=v['artifactSha256']
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

class AcceptanceTests(unittest.TestCase):
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
        self.f.replace_json(self.m['checklist']['A5']['runs'][0]['summary'],lambda x:x.update(stuckRecoveries=1));self.f.anchor(self.m);self.assert_closed()
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
        self.m['checklist']['A2']['runs'][0]['frames'].pop('horizon');self.f.anchor(self.m);self.assert_closed()
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
        self.f.replace_json(ar,lambda x:x['artifactSha256'].update({'04-window.json':wr['artifactSha256']}))
        self.m['checklist']['A4']['attempts'].append(ar);self.f.anchor(self.m);self.assert_closed()
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
        run['log']=self.f.put('benign-stability.log',b'[QaRunBot] round complete; pool exhausted at intended round end\n[Projectile] round=3 overflow=0\nJoint Rigidbody configured successfully')
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

if __name__=='__main__':unittest.main()
