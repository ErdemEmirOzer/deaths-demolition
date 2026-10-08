#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using DD.Art.Pipeline.EditorTools;
// SOURCE ONLY: separate exact helper intake + formal engine grant required.
public static class AP024S1CopyAvatarAImportDiagnostic
{
 const string K="/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition";
 const string Project=K+"/wp-082/worktree/deaths-demolition/prototype/DeathsDemolition_PlayableConcept";
 const string Own=K+"/asset-production/AP-024/04-tools/r3-s1-20261008/rev017-bounded-import-source-v003/runs/";
 const string S="Assets/_Art/Characters/Animations/AP022_tp_lmg_2_reload_tac_v008.fbx",H="Assets/_Art/Characters/Player/HeroV2/Hero_v2_TD_LOD0.fbx";
 const string SG="91d21fc08d3234866bef95abc4b6c473",HG="96b52215e71f840c4a81e3b4bffddc89",HD="bc7caa10d859f4e5ca5ef00f81dc6da4";
 [Serializable] public class Pin {public string path,sha256;public long bytes;}
 [Serializable] public class OfflineBefore {public string head;public Pin[] pins;public string[] controls;}
 [Serializable] public class Id {public bool present,valid,human;public string name,path,guid,dependency;public long local_id;public string[] human_mapping;}
 [Serializable] public class Dependency {public string path,hash,provenance="FIRST_OBSERVED_AFTER_EDITOR_STARTUP_NOT_OFFLINE_BASELINE";}
 [Serializable] public class Snapshot
 {
  public string importer_json,full_meta,clip_meta_block;public string[] four_file_sha256;public Id source_avatar;public Id[] local_avatars,imported_animator_avatars;
  public bool[] imported_animator_human_information_only,imported_animator_initialized_information_only;
  public string clip_take_name;public float first_frame,last_frame;public bool root_rotation_lock,root_height_lock,root_xz_lock;public int clip_settings_count;
  public Id clip;public bool human_motion;public float length,fps;public string clip_settings_json,events_json,motion_node;public bool resample,import_animation;public int compression;public float rotation_error,position_error,scale_error;
 }
 [Serializable] public class ClipSettings {public ModelImporterClipAnimation[] value;}
 [Serializable] public class EventRow {public float time,float_parameter;public int int_parameter,message_options;public string function,string_parameter;public Id object_parameter;}
 [Serializable] public class Events {public EventRow[] value;}
 [Serializable] public class Result
 {
  public string schema="AP024_S1_A_BOUNDED_IMPORT_DIAGNOSTIC_V1",status="UNKNOWN",runtime_pose="NOT_MEASURED",error,source_head,readonly_reference_dryrun_json,source_post_dependency_policy="OBSERVE_ONLY_NO_PREAPPROVED_POST_HASH";
  public int own_unity_pid;public string own_unity_start_utc;
  public string[] startup_changed_files,after_changed_files,validate_violations,control_dependency_changed_paths;public int explicit_save_and_reimport_calls;
  public Snapshot before,after;public Dependency[] control_dependencies_before,control_dependencies_after;
 }
 public static void Execute()
 {
  Need(!EditorApplication.isPlayingOrWillChangePlaymode,"No PlayMode");Need(Path.GetFullPath(Application.dataPath)==Path.GetFullPath(Project+"/Assets"),"Wrong project");
  Need(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DD_S06_S1_A_IMPORT_UNITY_GRANT")),"Separate formal ROOT grant absent");
  string run=Path.GetFullPath(Environment.GetEnvironmentVariable("DD_S1_A_RUN_DIR")??"");Need(run.StartsWith(Own,StringComparison.Ordinal)&&Directory.Exists(run),"Owned new run required");
  for(var p=new DirectoryInfo(run);p!=null;p=p.Parent)Need((p.Attributes&FileAttributes.ReparsePoint)==0,"Symlink output ancestor");
  string output=Path.Combine(run,"import-diagnostic.json");Need(!File.Exists(output),"New output required");var result=new Result();using(var process=System.Diagnostics.Process.GetCurrentProcess()){result.own_unity_pid=process.Id;result.own_unity_start_utc=process.StartTime.ToUniversalTime().ToString("O",System.Globalization.CultureInfo.InvariantCulture);}
  OfflineBefore offline=null;
  try
  {
   offline=JsonUtility.FromJson<OfflineBefore>(File.ReadAllText(Path.Combine(run,"offline-before.json")));Need(offline!=null&&offline.pins.Length==58&&offline.controls.Length==29,"Exact29asset58file inventory required");result.source_head=offline.head;
   result.startup_changed_files=Changed(offline);if(result.startup_changed_files.Length!=0)result.status=result.startup_changed_files.Contains(S+".meta")?"AUTO_IMPORT_OBSERVED_FAILCLOSED":"STARTUP_INPUT_CHANGE_FAILCLOSED";result.before=Read();result.control_dependencies_before=Deps(offline);
   if(result.startup_changed_files.Length!=0)
   {
    result.status=result.startup_changed_files.Contains(S+".meta")?"AUTO_IMPORT_OBSERVED_FAILCLOSED":"STARTUP_INPUT_CHANGE_FAILCLOSED";
    throw new InvalidOperationException("Offline before bytes differ after startup; zero explicit imports; no retry");
   }
   // DryRun intentionally requires exact original CreateFromThisModel/meta/dependencies.
   result.readonly_reference_dryrun_json=AP024S1AvatarReferenceDryRun.DryRun();
   Need(AssetDatabase.AssetPathToGUID(S)==SG&&AssetDatabase.AssetPathToGUID(H)==HG&&AssetDatabase.GetAssetDependencyHash(H).ToString()==HD,"Protected identity mismatch");
   var importer=AssetImporter.GetAtPath(S) as ModelImporter;Need(importer!=null&&importer.assetPath==S,"Exact importer required");
   ArtCharacterContract.Apply(importer);Need(importer.assetPath==S&&importer.avatarSetup==ModelImporterAvatarSetup.CopyFromOther,"Apply target/setup mismatch");
   result.explicit_save_and_reimport_calls++;importer.SaveAndReimport();
   result.status="IMPORT_OBSERVED_PENDING_VALIDATION_NO_RUNTIME_PASS";
  }
  catch(Exception e){result.error=e.ToString();if(result.status=="UNKNOWN")result.status="SOURCE_OR_IMPORT_DIAGNOSTIC_FAILED";}
  finally
  {
   try
   {
    if(offline!=null)
    {
     result.after=Read();result.after_changed_files=Changed(offline);result.control_dependencies_after=Deps(offline);
     result.control_dependency_changed_paths=result.control_dependencies_before==null?new string[0]:result.control_dependencies_after.Where((d,i)=>d.hash!=result.control_dependencies_before[i].hash).Select(d=>d.path).ToArray();
     var importer=AssetImporter.GetAtPath(S) as ModelImporter;result.validate_violations=importer==null?new[]{"Importer missing"}:ArtCharacterContract.Validate(importer).ToArray();
     Need(result.after_changed_files.All(p=>p==S+".meta"),"Collateral input byte changes");Need(result.control_dependency_changed_paths.All(p=>p==S),"Collateral control dependency changes");
     if(result.explicit_save_and_reimport_calls==1)
     {
      Need(result.after.clip_settings_count==1&&result.after.first_frame==0f&&result.after.last_frame==150f&&result.before.clip_take_name==result.after.clip_take_name&&result.after.root_rotation_lock&&result.after.root_height_lock&&result.after.root_xz_lock,"Exact imported settings span/take/rootlocks mismatch");
      Need(result.before.clip_settings_json==result.after.clip_settings_json&&result.before.clip_meta_block==result.after.clip_meta_block&&result.before.events_json==result.after.events_json,"Clip settings/events changed");
      Need(result.before.motion_node==result.after.motion_node&&result.before.resample==result.after.resample&&result.before.import_animation==result.after.import_animation&&result.before.compression==result.after.compression&&result.before.rotation_error==result.after.rotation_error&&result.before.position_error==result.after.position_error&&result.before.scale_error==result.after.scale_error,"Import motion/compression fields changed");
      Need(result.after.clip.guid==SG&&result.after.clip.local_id==-623603649343465320L&&result.after.clip.name=="AP022_tp_lmg_2_reload_tac"&&result.after.human_motion&&Math.Abs(result.after.length-5f)<1e-5f&&Math.Abs(result.after.fps-30f)<1e-5f,"Imported clip identity/humanMotion/span/fps changed");
      Need(result.validate_violations.Length==0,"Imported references remain invalid/UNKNOWN; preserve violations");
      Need(result.error==null,"Earlier diagnostic error");result.status="IMPORTED_REFERENCE_CHECKS_ONLY_RUNTIME_NOT_MEASURED";
     }
    }
   }
   catch(Exception e){result.error=(result.error??"")+"\nPOST: "+e;result.status=result.explicit_save_and_reimport_calls==0?result.status:"POST_IMPORT_VALIDATION_FAILED_NO_RUNTIME_PASS";}
   using(var f=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(f))writer.Write(JsonUtility.ToJson(result,true)+"\n");
  }
  if(result.error!=null)throw new InvalidOperationException("Import diagnostic failed; owned JSON preserved; no retry. "+result.status);
 }
 static Snapshot Read()
 {
  var importer=AssetImporter.GetAtPath(S) as ModelImporter;Need(importer!=null,"Importer missing");string meta=File.ReadAllText(Disk(S+".meta"));
  int a=meta.IndexOf("    clipAnimations:\n",StringComparison.Ordinal),b=meta.IndexOf("    isReadable:",a<0?0:a,StringComparison.Ordinal);Need(a>=0&&b>a,"Clip block missing");
  var settings=importer.clipAnimations.SingleOrDefault(c=>c.name=="AP022_tp_lmg_2_reload_tac");
  var clip=AssetDatabase.LoadAllAssetsAtPath(S).OfType<AnimationClip>().SingleOrDefault(c=>Identify(c).local_id==-623603649343465320L&&Identify(c).guid==SG);var model=AssetDatabase.LoadAssetAtPath<GameObject>(S);var animators=model==null?new Animator[0]:model.GetComponentsInChildren<Animator>(true);
  return new Snapshot{clip_settings_count=importer.clipAnimations.Length,clip_take_name=settings==null?null:settings.takeName,first_frame=settings==null?-1:settings.firstFrame,last_frame=settings==null?-1:settings.lastFrame,root_rotation_lock=settings!=null&&settings.lockRootRotation,root_height_lock=settings!=null&&settings.lockRootHeightY,root_xz_lock=settings!=null&&settings.lockRootPositionXZ,importer_json=EditorJsonUtility.ToJson(importer),full_meta=meta,clip_meta_block=meta.Substring(a,b-a),four_file_sha256=new[]{S,S+".meta",H,H+".meta"}.Select(p=>Hash(Disk(p))).ToArray(),source_avatar=Identify(importer.sourceAvatar),local_avatars=AssetDatabase.LoadAllAssetsAtPath(S).OfType<Avatar>().Select(v=>Identify(v)).ToArray(),imported_animator_avatars=animators.Select(v=>Identify(v.avatar)).ToArray(),imported_animator_human_information_only=animators.Select(v=>v.isHuman).ToArray(),imported_animator_initialized_information_only=animators.Select(v=>v.isInitialized).ToArray(),clip=Identify(clip),human_motion=clip!=null&&clip.humanMotion,length=clip==null?-1:clip.length,fps=clip==null?-1:clip.frameRate,clip_settings_json=JsonUtility.ToJson(new ClipSettings{value=importer.clipAnimations}),events_json=JsonUtility.ToJson(new Events{value=clip==null?new EventRow[0]:AnimationUtility.GetAnimationEvents(clip).Select(e=>new EventRow{time=e.time,function=e.functionName,string_parameter=e.stringParameter,float_parameter=e.floatParameter,int_parameter=e.intParameter,message_options=(int)e.messageOptions,object_parameter=Identify(e.objectReferenceParameter)}).ToArray()}),motion_node=importer.motionNodeName,resample=importer.resampleCurves,import_animation=importer.importAnimation,compression=(int)importer.animationCompression,rotation_error=importer.animationRotationError,position_error=importer.animationPositionError,scale_error=importer.animationScaleError};
 }
 static Id Identify(UnityEngine.Object o){if(o==null)return new Id();string guid;long id;Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out guid,out id),"Missing asset identity");string p=AssetDatabase.GetAssetPath(o);var av=o as Avatar;return new Id{present=true,name=o.name,path=p,guid=guid,local_id=id,dependency=AssetDatabase.GetAssetDependencyHash(p).ToString(),valid=av!=null&&av.isValid,human=av!=null&&av.isHuman,human_mapping=av==null?new string[0]:ArtCharacterContract.HumanMapping(av).ToArray()};}
 static Dependency[] Deps(OfflineBefore o){return o.controls.Select(p=>new Dependency{path=p,hash=AssetDatabase.GetAssetDependencyHash(p).ToString()}).ToArray();}
 static string[] Changed(OfflineBefore o){return o.pins.Where(p=>!File.Exists(Disk(p.path))||new FileInfo(Disk(p.path)).Length!=p.bytes||Hash(Disk(p.path))!=p.sha256).Select(p=>p.path).ToArray();}
 static string Disk(string p){Need(p.StartsWith("Assets/",StringComparison.Ordinal)&&!p.Contains(".."),"Bad asset path");return Path.Combine(Project,p);}
 static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static void Need(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
#endif
