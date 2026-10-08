using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Art.Pipeline.EditorTools
{
    // SOURCE ONLY. No BuildCli, library/material build, NavMesh rebuild, Lighting or PlayMode.
    // Stable serialized IDs and bindings must additionally pass the external saved-YAML audit.
    public static class P4Ap038SceneCli
    {
        const string OldPath="Assets/_Art/Environment/Families/AP-023/kairo_door/SM_kairo_door_V01.prefab";
        const string NewPath="Assets/_Art/Environment/Families/AP-038/kairo_door/SM_kairo_door_V02.prefab";
        static readonly string[] Names={"B1_01_kairo_door_V01","B2_02_kairo_door_V01","B3_01_kairo_door_V01","B4_02_kairo_door_V01","B5_01_kairo_door_V01","B6_01_kairo_door_V01","K1_WestKairo_Door_06"};
        static readonly Vector2[] Pivots={new Vector2(25.37f,-51.2f),new Vector2(29.97f,51.2f),new Vector2(-51.83f,-51.2f),new Vector2(-51.83f,-33.8f),new Vector2(35.37f,51.2f),new Vector2(11.57f,-51.2f),new Vector2(-33f,0f)};
        [Serializable] sealed class Binding {public int owner,target;public string property;}
        [Serializable] sealed class Node {public string path,name,globalID;public int instanceID,parent,sibling;public Vector3 localPosition,localScale;public Quaternion localRotation;public int[] components;public string[] componentTypes;}
        [Serializable] sealed class DoorRecord {public string name,source;public Node[] nodes;public string[] addedPaths,removedPaths;public string overrides;}
        [Serializable] sealed class Snapshot {public DoorRecord[] doors;public Binding[] inboundBindings;public string otherObjectsSha256;public int totalObjects;}
        [Serializable] sealed class Receipt {public string status="FAILED_AP038_MATERIALIZATION_ONLY",error;public int exactDoors;public bool saved,liveIdentityPreserved;public string savedYamlAudit="REQUIRED_EXTERNAL_NOT_REPLACED_BY_LIVE_IDS",bake="NOT_RUN",P4Runtime="NOT_RUN";}
        static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException("P4AP038 scene: "+why);}
        static string Hash(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
        static string Rel(Transform t,Transform root){if(t==root)return "";Require(t.IsChildOf(root),"relative path");var names=new List<string>();while(t!=root){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
        static GameObject[] Objects(){return Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).SelectMany(s=>s.GetRootGameObjects()).SelectMany(go=>go.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();}
        internal static string GeometryDigest()
        {
            var geometries=Objects().Where(go=>go.GetComponent<Collider>()!=null || go.GetComponent<MeshFilter>()!=null || go.GetComponent<MeshRenderer>()!=null).ToArray();
            var transforms=new HashSet<Transform>();foreach(var go in geometries){var at=go.transform;while(at!=null){transforms.Add(at);at=at.parent;}}
            var rows=new List<string>();
            foreach(var tr in transforms.OrderBy(x=>x.GetInstanceID()))rows.Add("T|"+tr.GetInstanceID()+"|"+tr.name+"|"+(tr.parent!=null?tr.parent.GetInstanceID():0)+"|"+tr.GetSiblingIndex()+"|"+JsonUtility.ToJson(new PoseRecord{position=tr.localPosition,rotation=tr.localRotation,scale=tr.localScale}));
            foreach(var go in geometries.OrderBy(x=>x.GetInstanceID()))
            {
                rows.Add("G|"+go.GetInstanceID()+"|"+go.activeSelf+"|"+go.layer);
                foreach(var c in go.GetComponents<Collider>())rows.Add("C|"+c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c));
                foreach(var mf in go.GetComponents<MeshFilter>())rows.Add("M|"+mf.GetInstanceID()+"|"+AssetRef(mf.sharedMesh));
                foreach(var mr in go.GetComponents<MeshRenderer>())rows.Add("R|"+mr.GetInstanceID()+"|"+mr.enabled+"|"+string.Join(";",mr.sharedMaterials.Select(AssetRef)));
            }
            if(File.Exists(GreyboxV6Builder.NavMeshPath))rows.Add("NAV|"+Hash(Convert.ToBase64String(File.ReadAllBytes(GreyboxV6Builder.NavMeshPath))));
            return Hash(string.Join("\n",rows));
        }
        static string AssetRef(UnityEngine.Object o){if(o==null)return "null";string guid;long id;Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out guid,out id),"persistent geometry/material ref required");return guid+":"+id;}
        [Serializable] sealed class PoseRecord{public Vector3 position,scale;public Quaternion rotation;}
        static DoorRecord Record(GameObject root)
        {
            var nodes=root.GetComponentsInChildren<Transform>(true).Select(t=>new Node{path=Rel(t,root.transform),name=t.name,globalID=GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(),instanceID=t.gameObject.GetInstanceID(),parent=t.parent!=null?t.parent.gameObject.GetInstanceID():0,sibling=t.GetSiblingIndex(),localPosition=t.localPosition,localRotation=t.localRotation,localScale=t.localScale,components=t.GetComponents<Component>().Select(c=>{Require(c!=null,"missing component");return c.GetInstanceID();}).ToArray(),componentTypes=t.GetComponents<Component>().Select(c=>c.GetType().FullName).ToArray()}).OrderBy(n=>n.path,StringComparer.Ordinal).ToArray();
            var added=PrefabUtility.GetAddedGameObjects(root).Select(x=>Rel(x.instanceGameObject.transform,root.transform)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var removed=PrefabUtility.GetRemovedComponents(root).Select(x=>Rel(x.assetComponent.transform,AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)).transform)+"|"+x.assetComponent.GetType().FullName).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var mods=PrefabUtility.GetPropertyModifications(root)??new PropertyModification[0];
            var assetRoot=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
            string Key(PropertyModification m)
            {
                Transform target=(m.target as GameObject)?.transform ?? (m.target as Component)?.transform;Require(target!=null,"override target missing");
                string type=m.target is GameObject?"GameObject":m.target.GetType().FullName;
                return Rel(target,assetRoot.transform)+"|"+type+"|"+m.propertyPath;
            }
            // Only the explicitly raised physical header enable override may differ.
            string overrides=string.Join("\n",mods.Where(m=>Key(m)!="Geometry/COL_12|UnityEngine.BoxCollider|m_Enabled").Select(m=>Key(m)+"="+m.value+"|"+(m.objectReference!=null?AssetDatabase.GetAssetPath(m.objectReference)+"#"+m.objectReference.GetInstanceID():"")).OrderBy(x=>x,StringComparer.Ordinal));
            return new DoorRecord{name=root.name,source=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root),nodes=nodes,addedPaths=added,removedPaths=removed,overrides=overrides};
        }
        static Snapshot Capture(GameObject[] doors)
        {
            var objects=Objects();var owned=new HashSet<int>(doors.SelectMany(go=>go.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject.GetInstanceID()));
            var targets=new HashSet<int>(doors.SelectMany(go=>go.GetComponentsInChildren<Transform>(true)).SelectMany(t=>t.GetComponents<Component>().Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[]{t.gameObject})).Select(o=>o.GetInstanceID()));
            var refs=new List<Binding>();var other=new List<string>();
            foreach(var go in objects)
            {
                if(!owned.Contains(go.GetInstanceID()))other.Add("GO|"+go.GetInstanceID()+"|"+EditorJsonUtility.ToJson(go));
                foreach(var c in go.GetComponents<Component>())
                {
                    Require(c!=null,"missing scene component");if(!owned.Contains(go.GetInstanceID()))other.Add("C|"+c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c));
                    var so=new SerializedObject(c);var property=so.GetIterator();
                    while(property.Next(true)) if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue!=null && targets.Contains(property.objectReferenceValue.GetInstanceID()))refs.Add(new Binding{owner=c.GetInstanceID(),target=property.objectReferenceValue.GetInstanceID(),property=property.propertyPath});
                }
            }
            return new Snapshot{doors=doors.Select(Record).ToArray(),inboundBindings=refs.OrderBy(x=>x.owner).ThenBy(x=>x.property,StringComparer.Ordinal).ToArray(),otherObjectsSha256=Hash(string.Join("\n",other.OrderBy(x=>x,StringComparer.Ordinal))),totalObjects=objects.Length};
        }
        static void SameLiveIdentity(Snapshot before,Snapshot after)
        {
            Require(before.totalObjects==after.totalObjects && before.otherObjectsSha256==after.otherObjectsSha256,"other scene object changed (includes C8/C1 bridges)");
            Require(JsonUtility.ToJson(new BindingList{items=before.inboundBindings})==JsonUtility.ToJson(new BindingList{items=after.inboundBindings}),"inbound/caller reference changed");
            for(int i=0;i<7;i++)
            {
                var a=before.doors[i];var b=after.doors[i];Require(a.name==b.name && a.nodes.Length==b.nodes.Length,"root name/node count changed");
                Require(a.addedPaths.SequenceEqual(b.addedPaths) && a.removedPaths.SequenceEqual(b.removedPaths),"added RoofOccluder/removed Roof component changed");
                Require(a.overrides==b.overrides,"non-header semantic property override changed");
                for(int j=0;j<a.nodes.Length;j++)
                {
                    var x=a.nodes[j];var y=b.nodes[j];Require(x.path==y.path && x.name==y.name && x.instanceID==y.instanceID && x.parent==y.parent && x.sibling==y.sibling && x.localPosition==y.localPosition && x.localRotation==y.localRotation && x.localScale==y.localScale && x.components.SequenceEqual(y.components) && x.componentTypes.SequenceEqual(y.componentTypes),"live identity/TRS/component changed "+x.path);
                }
            }
        }
        [Serializable] sealed class BindingList {public Binding[] items;}
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("DD_P4_AP038_OUT");Require(!string.IsNullOrEmpty(output)&&Directory.Exists(output),"external output required");
            string receiptPath=Path.Combine(output,"materialize-receipt.json");Require(!File.Exists(receiptPath),"immutable output exists");var receipt=new Receipt();int code=1;
            try
            {
                var main=EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath,OpenSceneMode.Single);
                if(File.Exists(WorldFptpSplit.ScenePath))EditorSceneManager.OpenScene(WorldFptpSplit.ScenePath,OpenSceneMode.Additive);
                var objects=Objects();var doors=new GameObject[7];
                for(int i=0;i<7;i++)
                {
                    var m=objects.Where(go=>go.name==Names[i]).ToArray();Require(m.Length==1,"unique exact old node "+Names[i]);var go=m[0];doors[i]=go;
                    Require(PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)==OldPath,"actual old source instance "+go.name);
                    Require(Mathf.Abs(go.transform.position.x-Pivots[i].x)<.002f && Mathf.Abs(go.transform.position.z-Pivots[i].y)<.002f && Mathf.Abs(go.transform.position.y)<.002f,"exact pivot "+go.name);
                    var physical=go.GetComponentsInChildren<Collider>(true).Where(c=>c.gameObject.layer!=DD.Prototype.Core.PrototypeLayers.CameraOccluder).ToArray();Require(physical.Length==21,"21 old shells");
                    Require(physical.Count(c=>!c.enabled)==1 && !go.transform.Find("Geometry/COL_12").GetComponent<BoxCollider>().enabled,"only old header disabled");
                }
                var before=Capture(doors);File.WriteAllText(Path.Combine(output,"scene-live-before.json"),JsonUtility.ToJson(before,true)+"\n");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(NewPath);Require(prefab!=null,"actual AP038 prefab required");
                var settings=new PrefabReplacingSettings{objectMatchMode=ObjectMatchMode.ByHierarchy,prefabOverridesOptions=PrefabOverridesOptions.KeepAllPossibleOverrides,changeRootNameToAssetName=false,logInfo=true};
                foreach(var door in doors)
                {
                    PrefabUtility.ReplacePrefabAssetOfPrefabInstance(door,prefab,settings,InteractionMode.AutomatedAction);
                    var header=door.transform.Find("Geometry/COL_12").GetComponent<BoxCollider>();Require(header!=null,"header mapped");
                    header.enabled=true;PrefabUtility.RecordPrefabInstancePropertyModifications(header);
                }
                Physics.SyncTransforms();var check=new P4Ap038BakeCli.Receipt();foreach(var go in doors)P4Ap038BakeCli.Validate(go,check);
                var after=Capture(doors);File.WriteAllText(Path.Combine(output,"scene-live-after.json"),JsonUtility.ToJson(after,true)+"\n");SameLiveIdentity(before,after);
                receipt.liveIdentityPreserved=true;receipt.exactDoors=7;
                // Failures above exit without saving; no automatic revert/commit. Persist only exact modified scenes.
                for(int i=0;i<SceneManager.sceneCount;i++){var scene=SceneManager.GetSceneAt(i);if(scene.isDirty)Require(EditorSceneManager.SaveScene(scene),"scene save");}
                receipt.saved=true;receipt.status="PASS_LIVE_EXACT7_PENDING_SAVED_YAML_AUDIT";code=0;
            }
            catch(Exception e){receipt.error=e.ToString();Debug.LogException(e);}
            finally{File.WriteAllText(receiptPath,JsonUtility.ToJson(receipt,true)+"\n");}
            EditorApplication.Exit(code);
        }
    }
}
