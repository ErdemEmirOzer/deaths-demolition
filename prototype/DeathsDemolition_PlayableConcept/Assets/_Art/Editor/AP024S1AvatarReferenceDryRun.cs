#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
// SOURCE ONLY. DryRun reads imported assets; it never applies the proposed policy or imports.
public static class AP024S1AvatarReferenceDryRun
{
 const string Project="/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/wp-082/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept";
 const string ClipPath="Assets/_Art/Characters/Animations/AP022_tp_lmg_2_reload_tac_v008.fbx";
 const string HeroPath="Assets/_Art/Characters/Player/HeroV2/Hero_v2_TD_LOD0.fbx";
 const string ClipGuid="91d21fc08d3234866bef95abc4b6c473",HeroGuid="96b52215e71f840c4a81e3b4bffddc89";
 const string SourceDependency="a1f3aa7662c0ec4b9110c9cac3ca0927",HeroDependency="bc7caa10d859f4e5ca5ef00f81dc6da4";
 const long ClipId=-623603649343465320L,AvatarId=9000000L;
 [Serializable] public class Identity {public bool present,valid,human; public string name,path,guid;public long local_id;}
 [Serializable] public class BoneRow {public string name,source_parent,hero_parent;public bool parent_equal;public Vector3 source_initial_position,source_initial_scale,hero_avatar_position,hero_avatar_scale;public Quaternion source_initial_q,hero_avatar_q;public double position_delta_mm,scale_max_abs_delta,rotation_delta_deg_information_only;}
 [Serializable] public class Readout
 {
  public string schema="AP024_S1_A_REFERENCE_DRYRUN_V1",phase="DRYRUN_ONLY_NO_IMPORT",selected_policy="A_EXACT_ONE_CLIP_COPY_FROM_PINNED_HERO";
  public string unity_version,utc,source_dependency,hero_dependency,proposed_avatar_setup="CopyFromOther",runtime_validity="UNKNOWN_UNTIL_SEPARATE_IMPORT";
  public string reference_definition="Imported model initial TRS versus Hero Avatar skeleton; initial FBX animation pose is not bind-pose proof; rotations descriptive only";
  public string effective_avatar_api="Imported GameObject.GetComponentsInChildren<Animator>(true)[0].avatar; compare exact pinned Hero reference after a separately authorized import";
  public string local_avatar_policy="Observed separately; missing/invalid local Avatar cannot establish validity or PASS";
  public bool human_description_rewrite=false,importer_serialization_unchanged,pins_unchanged,actual_clip_human_motion;
  public float clip_length,clip_frame_rate;public int bone_count;public bool source_hero_human_mapping_equal;
  public Identity[] all_local_avatar_subassets;public int imported_animator_count;public bool effective_equals_pinned_hero,effective_equals_importer_source_avatar,imported_animator_is_human_information_only,imported_animator_is_initialized_information_only;
  public string local_avatar_status,importer_avatar_setup,runtime_pose_validity="NOT_MEASURED";
  public Identity clip,source_local_avatar,source_importer_source_avatar,source_imported_animator_avatar,pinned_hero_avatar,hero_imported_animator_avatar;
  public BoneRow[] bones;
 }
 public static string DryRun()
 {
  Need(!EditorApplication.isPlayingOrWillChangePlaymode,"No PlayMode");Need(Path.GetFullPath(Application.dataPath)==Path.GetFullPath(Project+"/Assets"),"Wrong project");Pins();
  Need(AssetDatabase.AssetPathToGUID(ClipPath)==ClipGuid&&AssetDatabase.AssetPathToGUID(HeroPath)==HeroGuid,"GUID mismatch");
  Need(AssetDatabase.GetAssetDependencyHash(ClipPath).ToString()==SourceDependency&&AssetDatabase.GetAssetDependencyHash(HeroPath).ToString()==HeroDependency,"Dependency mismatch");
  var importer=AssetImporter.GetAtPath(ClipPath) as ModelImporter;Need(importer!=null,"Importer missing");
  Need(importer.animationType==ModelImporterAnimationType.Human&&importer.avatarSetup==ModelImporterAvatarSetup.CreateFromThisModel&&importer.sourceAvatar==null,"Pre-policy state changed");
  string before=EditorJsonUtility.ToJson(importer);
  var clip=AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>().Single(c=>Id(c).guid==ClipGuid&&Id(c).local_id==ClipId&&c.name=="AP022_tp_lmg_2_reload_tac");
  Need(clip.humanMotion&&Math.Abs(clip.length-5f)<1e-5f&&Math.Abs(clip.frameRate-30f)<1e-5f,"Clip contract changed");
  var sourceModel=AssetDatabase.LoadAssetAtPath<GameObject>(ClipPath);var heroModel=AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath);Need(sourceModel!=null&&heroModel!=null,"Model missing");
  var allLocalAvatars=AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<Avatar>().ToArray();var localAvatar=allLocalAvatars.SingleOrDefault();
  var hero=AssetDatabase.LoadAllAssetsAtPath(HeroPath).OfType<Avatar>().SingleOrDefault();var heroId=Id(hero);
  Need(hero!=null&&heroId.guid==HeroGuid&&heroId.local_id==AvatarId&&heroId.path==HeroPath&&heroId.name=="Hero_v2_TD_LOD0Avatar"&&hero.isValid&&hero.isHuman,"Pinned Hero identity invalid");
  var sourceAnimator=sourceModel.GetComponentsInChildren<Animator>(true);var heroAnimator=heroModel.GetComponentsInChildren<Animator>(true);Need(sourceAnimator.Length==1&&heroAnimator.Length==1,"One imported Animator per model required");
  var sourceTree=sourceModel.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name,StringComparer.Ordinal);
  var heroTree=heroModel.GetComponentsInChildren<Transform>(true).ToDictionary(t=>t.name,StringComparer.Ordinal);
  Transform armature;Need(heroTree.TryGetValue("PlayerRig",out armature),"Hero armature missing");
  var boneNames=armature.GetComponentsInChildren<Transform>(true).Where(t=>t!=armature).Select(t=>t.name).OrderBy(n=>n,StringComparer.Ordinal).ToArray();Need(boneNames.Length==77&&boneNames.Distinct().Count()==77,"Hero77 inventory mismatch");
  var skeleton=hero.humanDescription.skeleton.ToDictionary(b=>b.name,StringComparer.Ordinal);
  var rows=boneNames.Select(name=>{Transform t;Need(sourceTree.TryGetValue(name,out t),"Source bone missing: "+name);Need(skeleton.ContainsKey(name),"Hero Avatar skeleton missing: "+name);var r=skeleton[name];string sp=Parent(t,sourceModel.transform),hp=Parent(heroTree[name],heroModel.transform);return new BoneRow{name=name,source_parent=sp,hero_parent=hp,parent_equal=sp==hp,source_initial_position=t.localPosition,source_initial_scale=t.localScale,source_initial_q=t.localRotation,hero_avatar_position=r.position,hero_avatar_scale=r.scale,hero_avatar_q=r.rotation,position_delta_mm=(double)Vector3.Distance(t.localPosition,r.position)*1000d,scale_max_abs_delta=Math.Max(Math.Abs(t.localScale.x-r.scale.x),Math.Max(Math.Abs(t.localScale.y-r.scale.y),Math.Abs(t.localScale.z-r.scale.z))),rotation_delta_deg_information_only=Quaternion.Angle(t.localRotation,r.rotation)};}).ToArray();
  var template=DD.Art.Pipeline.EditorTools.ArtCharacterContract.TemplateHumanBones().Select(h=>h.humanName+"="+h.boneName).OrderBy(n=>n,StringComparer.Ordinal).ToArray();Need(template.Length==52&&Map(hero).SequenceEqual(template),"Hero exact52 mapping mismatch");
  var result=new Readout{unity_version=Application.unityVersion,utc=DateTime.UtcNow.ToString("O"),source_dependency=SourceDependency,hero_dependency=HeroDependency,actual_clip_human_motion=clip.humanMotion,clip_length=clip.length,clip_frame_rate=clip.frameRate,bone_count=rows.Length,bones=rows,all_local_avatar_subassets=allLocalAvatars.Select(a=>Id(a)).ToArray(),imported_animator_count=sourceAnimator.Length,effective_equals_pinned_hero=sourceAnimator[0].avatar==hero,effective_equals_importer_source_avatar=sourceAnimator[0].avatar==importer.sourceAvatar,imported_animator_is_human_information_only=sourceAnimator[0].isHuman,imported_animator_is_initialized_information_only=sourceAnimator[0].isInitialized,local_avatar_status=localAvatar==null?"UNKNOWN_MISSING":localAvatar.isValid&&localAvatar.isHuman?"VALID":"INVALID",importer_avatar_setup=importer.avatarSetup.ToString(),source_hero_human_mapping_equal=localAvatar!=null&&Map(localAvatar).SequenceEqual(Map(hero)),clip=Id(clip),source_local_avatar=Id(localAvatar),source_importer_source_avatar=Id(importer.sourceAvatar),source_imported_animator_avatar=Id(sourceAnimator[0].avatar),pinned_hero_avatar=heroId,hero_imported_animator_avatar=Id(heroAnimator[0].avatar)};
  Pins();Need(AssetDatabase.GetAssetDependencyHash(ClipPath).ToString()==SourceDependency&&AssetDatabase.GetAssetDependencyHash(HeroPath).ToString()==HeroDependency,"Dependency changed during reads");Need(EditorJsonUtility.ToJson(importer)==before,"Importer changed during reads");result.importer_serialization_unchanged=true;result.pins_unchanged=true;return JsonUtility.ToJson(result,true);
 }
 static string Parent(Transform t,Transform root){return t.parent==null?"NONE":t.parent==root?"$MODEL_ROOT":t.parent.name;}
 static string[] Map(Avatar a){return a.humanDescription.human.Select(h=>h.humanName+"="+h.boneName).OrderBy(n=>n,StringComparer.Ordinal).ToArray();}
 static Identity Id(Object o){if(o==null)return new Identity{present=false};string g;long l;Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out g,out l),"Asset identity unavailable");var a=o as Avatar;return new Identity{present=true,name=o.name,path=AssetDatabase.GetAssetPath(o),guid=g,local_id=l,human=a!=null&&a.isHuman,valid=a!=null&&a.isValid};}
 static void Pins(){Pin(ClipPath,1161660,"be4c805dd4d61bd3e92fe01df251d9ce28a23878a6fb2add3b6f13ae8ea44880");Pin(ClipPath+".meta",35896,"59fd6f423fc323b52d4ff64d281d6e44d4c346ea72568fd2fe11ee6e07422732");Pin(HeroPath,2923884,"1176e99b7bc2b5626e34ed264f5c6da7efe69d9df833b608cc8e7468baa14f06");Pin(HeroPath+".meta",33032,"c5b5078a60e6a53db6517385892ebfc44c3d204cb19aeb3ecdcfcaeea64eaf30");}
 static void Pin(string relative,long bytes,string sha){string p=Path.Combine(Project,relative);Need(new FileInfo(p).Length==bytes,"Bytes changed: "+relative);using(var h=SHA256.Create())using(var f=File.OpenRead(p))Need(BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant()==sha,"SHA changed: "+relative);}
 static void Need(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
}
#endif
