#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
// Source-staged observation only; separate canonical ROOT batch grant required.
public static class AP024S1NonNeutralCapture
{
 const string K="/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition";
 const string Project=K+"/wp-082/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept";
 const string Own=K+"/asset-production/AP-024/04-tools/r3-s1-20261008/rev007-nonneutral-capture-unity";
 const string PlanPath=K+"/asset-production/AP-024/04-tools/r3-s1-20261008/rev006-nonneutral-basis-plan/revision-v002/manifest.json";
 const string PlanHash="4fded0d5fcbedb426301dac5d95c11a0cb9e9a776082ac080425e55a587e4dc2";
 const string ClipPath="Assets/_Art/Characters/Animations/AP022_tp_lmg_2_reload_tac_v008.fbx";
 const string ModelPath="Assets/_Art/Characters/Player/HeroV2/Hero_v2_TD_LOD0.fbx";
 const string ClipGuid="91d21fc08d3234866bef95abc4b6c473", ModelGuid="96b52215e71f840c4a81e3b4bffddc89";
 const long ClipId=-623603649343465320L;
 const string TargetDependency="bc7caa10d859f4e5ca5ef00f81dc6da4";
 static readonly HumanBodyBones[] Bones={HumanBodyBones.LeftShoulder,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand};
 [Serializable] public class Pin {public string path,sha256;public long bytes;}
 [Serializable] public class Plan {public string schema;public Pin[] pins;}
 [Serializable] public class Identity {public string name,path,guid,dependency;public long local_id;public bool human,valid;}
 [Serializable] public class Bone
 {
  public string human_bone,name,parent_name;
  public Quaternion local_q,world_q,parent_world_q;
  public float[] local_matrix,world_matrix,parent_world_matrix,animator_root_relative_matrix;
  public Vector3 initial_local_position,initial_local_scale,skeleton_rest_position,skeleton_rest_scale;
  public Quaternion initial_local_q,skeleton_rest_local_q,initial_relative_current_q,skeleton_relative_current_q;
 }
 [Serializable] public class Axis
 {
  public string human_bone,status,muscle_name,limit_source;
  public int axis,muscle_index;public bool use_default,has_value;
  public float serialized_min,serialized_max,serialized_center;
  public float[] effective_range,actual_muscle;
 }
 [Serializable] public class AvatarRest
 {
  public string human_bone,rig_name;public Vector3 position,scale;public Quaternion rotation;
 }
 [Serializable] public class Readout
 {
  public string schema="AP024_S1_NONNEUTRAL_CAPTURE_V2",status="OBSERVATION_ONLY";
  public string source_axis_equivalence="NOT_ESTABLISHED",runtime_contact="NOT_MEASURED";
  public string context="Actual imported hero clone, single imported clip, manual clock1s, no production writer/controller/IK";
  public string utc,unity_version,plan_hash,source_dependency_policy,source_dependency_pre,source_dependency_post,source_dependency_expected;
  public Pin[] pins;public Identity clip,source_avatar,target_model,target_avatar;
  public int source_frame=30,unity_frame;
  public double requested_seconds=1d,actual_playable_seconds,normalized_ratio_information_only;
  public float actual_clip_length,actual_clip_frame_rate;
  public bool foot_ik=false,playable_ik=false,apply_root_motion=false;
  public float[] clone_root_before,clone_root_after,animator_root_before,animator_root_after;
  public bool clone_root_unchanged,animator_root_unchanged,pose_changed_from_initial_information_only;
  public string reference_definition="Initial=clone imported local TRS before Rebind; skeleton=target Avatar.humanDescription.skeleton; relative q=inverse(reference local q)*current local q. Neither is source FBX axes nor HumanPose zero.";
  public Bone[] bones;public AvatarRest[] source_avatar_rest,target_avatar_rest;
  public Axis[] axes;public AP024S1WristDiagnostic.WristReadout wrist;
  public string[] all_muscle_names;public float[] all_muscles;
  public Vector3 human_body_position;public Quaternion human_body_rotation;
 }
 class Initial {public string name;public Vector3 p,s;public Quaternion q;}
 public static void Execute()
 {
  Need(!EditorApplication.isPlayingOrWillChangePlaymode,"No PlayMode in this diagnostic");
  Need(Path.GetFullPath(Application.dataPath)==Path.GetFullPath(Project+"/Assets"),"Wrong project");
  string output=Environment.GetEnvironmentVariable("DD_S1_NONNEUTRAL_OUT");Need(!string.IsNullOrWhiteSpace(output),"Output required");output=Path.GetFullPath(output);
  Need(output.StartsWith(Path.GetFullPath(Own+"/runs")+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&Path.GetExtension(output)==".json","Output outside owned runs");
  Need(!File.Exists(output)&&Directory.Exists(Path.GetDirectoryName(output)),"New output in existing owned run required");
  for(var p=new DirectoryInfo(Path.GetDirectoryName(output));p!=null;p=p.Parent)Need((p.Attributes&FileAttributes.ReparsePoint)==0,"Output symlink ancestor");
  var plan=Pins();Need(AssetDatabase.AssetPathToGUID(ClipPath)==ClipGuid&&AssetDatabase.AssetPathToGUID(ModelPath)==ModelGuid,"GUID mismatch");
  string sourceExpected=Environment.GetEnvironmentVariable("DD_S1_SOURCE_DEPENDENCY_HASH");
  if(!string.IsNullOrEmpty(sourceExpected)){Need(sourceExpected.Length==32&&sourceExpected.All(Uri.IsHexDigit),"Bad expected source dependency");sourceExpected=sourceExpected.ToLowerInvariant();}
  string sourcePre=AssetDatabase.GetAssetDependencyHash(ClipPath).ToString();Dependencies(sourceExpected,sourcePre);
  var clip=AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<AnimationClip>().SingleOrDefault(c=>PinnedClip(c));
  Need(clip!=null&&clip.humanMotion,"Pinned Humanoid clip missing");
  Need(Math.Abs(clip.length-5f)<1e-5f&&Math.Abs(clip.frameRate-30f)<1e-5f,"Actual clip span/fps mismatch");
  var model=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);Need(model!=null,"Target model missing");
  var source=AssetDatabase.LoadAllAssetsAtPath(ClipPath).OfType<Avatar>().SingleOrDefault(a=>a.isValid&&a.isHuman);
  var target=AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().SingleOrDefault(a=>a.isValid&&a.isHuman);
  Need(source!=null&&target!=null&&target.name=="Hero_v2_TD_LOD0Avatar","Actual Avatar mismatch");
  var result=new Readout{utc=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture),unity_version=Application.unityVersion,plan_hash=PlanHash,pins=plan.pins,
   clip=Identify(clip),source_avatar=Identify(source),target_model=Identify(model),target_avatar=Identify(target),
   actual_clip_length=clip.length,actual_clip_frame_rate=clip.frameRate,normalized_ratio_information_only=1d/clip.length,
   source_dependency_expected=sourceExpected??"",source_dependency_pre=sourcePre,
   source_dependency_policy=string.IsNullOrEmpty(sourceExpected)?"FIRST_OBSERVED_NO_PRIOR_BASELINE; exact bytes/meta/GUID/localID pinned; pre/post equality required":"EXPECTED_BASELINE_STRICT",
   source_avatar_rest=Rest(source.humanDescription),target_avatar_rest=Rest(target.humanDescription)};
  Scene preview=default(Scene);GameObject instance=null;PlayableGraph graph=default(PlayableGraph);
  try
  {
   preview=EditorSceneManager.NewPreviewScene();instance=PrefabUtility.InstantiatePrefab(model,preview) as GameObject;Need(instance!=null,"Preview clone failed");
   Need(instance.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Unexpected clone script writer");
   var animators=instance.GetComponentsInChildren<Animator>(true);Need(animators.Length==1,"One Animator required");var animator=animators[0];Need(animator.avatar==target&&animator.isHuman,"Clone Avatar changed");
   instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);instance.transform.localScale=Vector3.one;
   animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.fireEvents=false;animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   var desc=target.humanDescription;var initials=Bones.Select(b=>InitialBone(instance.transform,desc,b)).ToArray();
   animator.Rebind();result.clone_root_before=M(instance.transform.localToWorldMatrix);result.animator_root_before=M(animator.transform.localToWorldMatrix);
   graph=PlayableGraph.Create("AP024-S1-frame30-readonly");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
   var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);playable.SetSpeed(0d);playable.SetTime(1d);
   var port=AnimationPlayableOutput.Create(graph,"one-clip",animator);port.SetSourcePlayable(playable);graph.Evaluate(0f);
   result.actual_playable_seconds=playable.GetTime();Need(Math.Abs(result.actual_playable_seconds-1d)<1e-7,"Clock mismatch");result.unity_frame=Time.frameCount;
   result.clone_root_after=M(instance.transform.localToWorldMatrix);result.animator_root_after=M(animator.transform.localToWorldMatrix);
   result.clone_root_unchanged=result.clone_root_before.SequenceEqual(result.clone_root_after);result.animator_root_unchanged=result.animator_root_before.SequenceEqual(result.animator_root_after);
   result.bones=Bones.Select((b,i)=>SampleBone(animator,desc,b,initials[i])).ToArray();
   result.pose_changed_from_initial_information_only=result.bones.Any(b=>Quaternion.Angle(b.initial_local_q,b.local_q)>1e-5f);
   if(!result.clone_root_unchanged||!result.animator_root_unchanged)result.status="ROOT_MOVED_DIAGNOSTIC_ONLY";
   if(!result.pose_changed_from_initial_information_only)result.status="NO_OBSERVED_POSE_CHANGE_DIAGNOSTIC_ONLY";
   result.wrist=AP024S1WristDiagnostic.CaptureCurrent(animator,"preview_manual_clip_1s_after_evaluate");Need(result.wrist.valid&&result.wrist.unity_frame==result.unity_frame,"Wrist capture invalid/frame changed");
   var handler=new HumanPoseHandler(target,animator.transform);
   try{var pose=new HumanPose();handler.GetHumanPose(ref pose);Need(pose.muscles!=null&&pose.muscles.Length==HumanTrait.MuscleCount,"Muscle inventory mismatch");
    result.all_muscle_names=HumanTrait.MuscleName;result.all_muscles=(float[])pose.muscles.Clone();result.human_body_position=pose.bodyPosition;result.human_body_rotation=pose.bodyRotation;result.axes=Axes(desc,pose.muscles);
   }finally{handler.Dispose();}
  }
  finally{try{if(graph.IsValid())graph.Destroy();}finally{try{if(instance!=null)Object.DestroyImmediate(instance);}finally{if(preview.IsValid())EditorSceneManager.ClosePreviewScene(preview);}}}
  Pins();result.source_dependency_post=AssetDatabase.GetAssetDependencyHash(ClipPath).ToString();Dependencies(sourceExpected,sourcePre);
  Need(result.source_dependency_post==sourcePre,"Source dependency changed during capture");
  string json=Json(result);using(var fs=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(fs,new UTF8Encoding(false)))writer.Write(json+"\n");
 }
 static bool PinnedClip(AnimationClip c){string guid;long id;return c.name=="AP022_tp_lmg_2_reload_tac"&&AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out guid,out id)&&guid==ClipGuid&&id==ClipId;}
 static Identity Identify(Object a){string guid;long id;Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a,out guid,out id),"Asset identity missing");string path=AssetDatabase.GetAssetPath(a);var av=a as Avatar;return new Identity{name=a.name,path=path,guid=guid,local_id=id,dependency=AssetDatabase.GetAssetDependencyHash(path).ToString(),human=av!=null&&av.isHuman,valid=av!=null&&av.isValid};}
 static void Dependencies(string expected,string pre){Need(AssetDatabase.GetAssetDependencyHash(ModelPath).ToString()==TargetDependency,"Target dependency mismatch");string current=AssetDatabase.GetAssetDependencyHash(ClipPath).ToString();Need(current==pre,"Source dependency changed");if(!string.IsNullOrEmpty(expected))Need(current==expected,"Source expected dependency mismatch");}
 static Plan Pins(){Need(SHA(PlanPath)==PlanHash,"Plan SHA mismatch");var p=JsonUtility.FromJson<Plan>(File.ReadAllText(PlanPath));Need(p!=null&&p.schema=="AP024_S1_NONNEUTRAL_BASIS_PLAN_V2"&&p.pins!=null&&p.pins.Length==16&&p.pins.Select(x=>x.path).Distinct().Count()==16,"Pin inventory mismatch");foreach(var x in p.pins)Need(File.Exists(x.path)&&new FileInfo(x.path).Length==x.bytes&&SHA(x.path)==x.sha256,"Pin changed: "+x.path);return p;}
 static string SHA(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static HumanBone Human(HumanDescription d,HumanBodyBones b){var found=d.human.Where(h=>h.humanName.Replace(" ","")==b.ToString()).ToArray();Need(found.Length==1,"Human mapping missing: "+b);return found[0];}
 static SkeletonBone Skeleton(HumanDescription d,string name){var found=d.skeleton.Where(s=>s.name==name).ToArray();Need(found.Length==1,"Skeleton reference missing: "+name);return found[0];}
 static AvatarRest[] Rest(HumanDescription d){return Bones.Select(b=>{var h=Human(d,b);var s=Skeleton(d,h.boneName);return new AvatarRest{human_bone=b.ToString(),rig_name=h.boneName,position=s.position,rotation=s.rotation,scale=s.scale};}).ToArray();}
 static Initial InitialBone(Transform root,HumanDescription d,HumanBodyBones b){string name=Human(d,b).boneName;var all=root.GetComponentsInChildren<Transform>(true).Where(t=>t.name==name).ToArray();Need(all.Length==1,"Initial bone ambiguous: "+name);var t=all[0];return new Initial{name=name,p=t.localPosition,q=t.localRotation,s=t.localScale};}
 static Bone SampleBone(Animator a,HumanDescription d,HumanBodyBones b,Initial i){var t=a.GetBoneTransform(b);Need(t!=null&&t.name==i.name,"Sampled bone mismatch");var s=Skeleton(d,i.name);return new Bone{human_bone=b.ToString(),name=t.name,parent_name=t.parent==null?"NONE":t.parent.name,local_q=t.localRotation,world_q=t.rotation,parent_world_q=t.parent==null?Quaternion.identity:t.parent.rotation,
  local_matrix=M(Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale)),world_matrix=M(t.localToWorldMatrix),parent_world_matrix=t.parent==null?new float[0]:M(t.parent.localToWorldMatrix),animator_root_relative_matrix=M(a.transform.worldToLocalMatrix*t.localToWorldMatrix),
  initial_local_position=i.p,initial_local_scale=i.s,initial_local_q=i.q,skeleton_rest_position=s.position,skeleton_rest_scale=s.scale,skeleton_rest_local_q=s.rotation,initial_relative_current_q=Quaternion.Inverse(i.q)*t.localRotation,skeleton_relative_current_q=Quaternion.Inverse(s.rotation)*t.localRotation};}
 static Axis[] Axes(HumanDescription d,float[] muscles){return Bones.Skip(1).SelectMany(b=>{var h=Human(d,b);int bone=Array.FindIndex(HumanTrait.BoneName,n=>n.Replace(" ","")==b.ToString());Need(bone>=0,"HumanTrait bone missing");return Enumerable.Range(0,3).Select(axis=>{int id=HumanTrait.MuscleFromBone(bone,axis);Need(id>=-1&&id<muscles.Length,"Bad muscle index");bool present=id>=0;var l=h.limit;return new Axis{human_bone=b.ToString(),axis=axis,muscle_index=id,status=present?"MUSCLE":"NO_MUSCLE",muscle_name=present?HumanTrait.MuscleName[id]:"",use_default=l.useDefaultValues,serialized_min=l.min[axis],serialized_max=l.max[axis],serialized_center=l.center[axis],has_value=present,
 limit_source=!present?"NO_MUSCLE":l.useDefaultValues?"HumanTrait default":"actual Avatar custom",effective_range=!present?new float[0]:l.useDefaultValues?new[]{HumanTrait.GetMuscleDefaultMin(id),HumanTrait.GetMuscleDefaultMax(id)}:new[]{l.min[axis],l.max[axis]},actual_muscle=present?new[]{muscles[id]}:new float[0]};});}).ToArray();}
 static float[] M(Matrix4x4 m){return Enumerable.Range(0,16).Select(i=>m[i/4,i%4]).ToArray();}
 static string Json(object o){if(o==null)return "null";if(o is string)return Quote((string)o);if(o is bool)return (bool)o?"true":"false";if(o is float){float f=(float)o;Need(!float.IsNaN(f)&&!float.IsInfinity(f),"Nonfinite float");return f.ToString("R",CultureInfo.InvariantCulture);}if(o is double){double f=(double)o;Need(!double.IsNaN(f)&&!double.IsInfinity(f),"Nonfinite double");return f.ToString("R",CultureInfo.InvariantCulture);}if(o is int||o is long)return Convert.ToString(o,CultureInfo.InvariantCulture);if(o is IEnumerable)return "["+string.Join(",",((IEnumerable)o).Cast<object>().Select(Json).ToArray())+"]";var fields=o.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance).OrderBy(f=>f.Name,StringComparer.Ordinal).ToArray();Need(fields.Length>0,"Unsupported JSON type");return "{"+string.Join(",",fields.Select(f=>Quote(f.Name)+":"+Json(f.GetValue(o))).ToArray())+"}";}
 static string Quote(string s){var b=new StringBuilder("\"");foreach(char c in s){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));else b.Append(c);}return b.Append('"').ToString();}
 static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
#endif
