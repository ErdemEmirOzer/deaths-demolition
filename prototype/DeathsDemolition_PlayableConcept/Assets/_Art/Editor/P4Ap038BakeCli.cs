using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using DD.Prototype.Core;

namespace DD.Art.Pipeline.EditorTools
{
    // SOURCE ONLY: requires separately committed, already materialized exact-seven AP038 world.
    // Does not build the world, replace instances, change AP023 assets or run PlayMode.
    public static class P4Ap038BakeCli
    {
        const string PrefabPath = "Assets/_Art/Environment/Families/AP-038/kairo_door/SM_kairo_door_V02.prefab";
        static readonly string[] Names = { "B1_01_kairo_door_V01", "B2_02_kairo_door_V01", "B3_01_kairo_door_V01", "B4_02_kairo_door_V01", "B5_01_kairo_door_V01", "B6_01_kairo_door_V01", "K1_WestKairo_Door_06" };
        static readonly Vector2[] Pivots = { new Vector2(25.37f,-51.2f),new Vector2(29.97f,51.2f),new Vector2(-51.83f,-51.2f),new Vector2(-51.83f,-33.8f),new Vector2(35.37f,51.2f),new Vector2(11.57f,-51.2f),new Vector2(-33f,0f) };
        [Serializable] internal sealed class Receipt { public string geometryBefore,geometryAfter; public string status = "FAILED_AP038_BAKE_ONLY", error, bakeReport; public int doors, physicalBoxes, headers, renderers; public string P4Runtime = "NOT_RUN"; }
        static void Require(bool ok,string why) { if(!ok) throw new InvalidOperationException("P4AP038 bake: "+why); }
        internal static void Validate(GameObject door,Receipt r)
        {
            Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(door)==PrefabPath,"actual AP038 prefab instance required "+door.name);
            var visual=door.GetComponentsInChildren<MeshRenderer>(true);
            Require(visual.Length==15,"15 actual LOD renderers "+door.name);
            foreach(var mr in visual)
            {
                var mf=mr.GetComponent<MeshFilter>();
                Require(mf!=null && mf.sharedMesh!=null && AssetDatabase.GetAssetPath(mf.sharedMesh).EndsWith("/AP-038/kairo_door/AP023_kairo_door_V02.fbx",StringComparison.Ordinal),"allLOD mesh source "+mr.name);
                Require(mr.sharedMaterials.All(m=>m!=null && AssetDatabase.GetAssetPath(m).StartsWith("Assets/_Art/Materials/M_ap023_",StringComparison.Ordinal)),"existing material role "+mr.name);
            }
            // Only the known, separately authored roof camera occluder is outside physical shells.
            var all=door.GetComponentsInChildren<Collider>(true);
            var camera=all.Where(c=>c.gameObject.layer==PrototypeLayers.CameraOccluder).ToArray();
            Require(camera.All(c=>c.name==WorldV5Dress.RoofOccluderName && c is BoxCollider),"unexpected camera collider");
            var physical=all.Where(c=>c.gameObject.layer!=PrototypeLayers.CameraOccluder).ToArray();
            Require(physical.Length==21,"21 actual physical shells "+door.name);
            int headers=0;
            foreach(var c in physical)
            {
                Require(c is BoxCollider && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger,"disabled/inactive/nonBox/trigger shell "+c.name);
                var box=(BoxCollider)c; Vector3 lo=Vector3.one*float.PositiveInfinity, hi=Vector3.one*float.NegativeInfinity;
                for(int corner=0;corner<8;corner++)
                {
                    var s=new Vector3((corner&1)==0?-.5f:.5f,(corner&2)==0?-.5f:.5f,(corner&4)==0?-.5f:.5f);
                    var q=door.transform.InverseTransformPoint(box.transform.TransformPoint(box.center+Vector3.Scale(box.size,s)));
                    lo=Vector3.Min(lo,q);hi=Vector3.Max(hi,q);
                }
                if(Mathf.Abs(lo.y-3.25f)<.002f && Mathf.Abs(hi.y-3.65f)<.002f && Mathf.Abs(lo.x+1.065f)<.002f && Mathf.Abs(hi.x-1.065f)<.002f && Mathf.Abs(lo.z+2.305f)<.002f && Mathf.Abs(hi.z+2.165f)<.002f) headers++;
                Require(!(lo.x<.998f && hi.x>-.998f && lo.z<-1.9f && hi.z>-2.9f && lo.y<3.248f && hi.y>.252f),"physical clear opening "+c.name);
            }
            Require(headers==1,"exactly one physical raised header "+door.name);
            r.doors++;r.physicalBoxes+=physical.Length;r.headers+=headers;r.renderers+=visual.Length;
        }
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("DD_P4_AP038_OUT");
            Require(!string.IsNullOrEmpty(output)&&Directory.Exists(output),"external output required");
            string path=Path.Combine(output,"bake-receipt.json"); Require(!File.Exists(path),"immutable output already exists");
            var receipt=new Receipt();int code=1;
            try
            {
                var main=EditorSceneManager.OpenScene(GreyboxV6Builder.ScenePath,OpenSceneMode.Single);
                if(File.Exists(WorldFptpSplit.ScenePath)) EditorSceneManager.OpenScene(WorldFptpSplit.ScenePath,OpenSceneMode.Additive);
                var objects=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).SelectMany(s=>s.GetRootGameObjects()).SelectMany(go=>go.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject).ToArray();
                Physics.SyncTransforms();
                for(int i=0;i<Names.Length;i++)
                {
                    var matches=objects.Where(go=>go.name==Names[i]).ToArray();Require(matches.Length==1,"unique exact door "+Names[i]);var go=matches[0];
                    Require(Mathf.Abs(go.transform.position.x-Pivots[i].x)<.002f && Mathf.Abs(go.transform.position.z-Pivots[i].y)<.002f && Mathf.Abs(go.transform.position.y)<.002f,"unchanged exact pivot "+go.name);
                    Validate(go,receipt);
                }
                Require(objects.Count(go=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)==PrefabPath && PrefabUtility.IsAnyPrefabInstanceRoot(go))==7,"exact seven AP038 door instances only");
                var roots=main.GetRootGameObjects().Where(go=>go.name==Ap005WorldBootstrap.RootName).ToArray();Require(roots.Length==1,"main world root");
                // Only existing scene lighting is baked. No GreyboxV6Builder.Build/BuildCli or library/material build.
                receipt.geometryBefore=P4Ap038SceneCli.GeometryDigest();
                receipt.bakeReport=WorldV5Lighting.Bake(main,roots[0].transform);
                receipt.geometryAfter=P4Ap038SceneCli.GeometryDigest();
                Require(receipt.geometryBefore==receipt.geometryAfter,"existing geometry/materials/transforms/NavMesh changed during lighting bake");
                receipt.status="PASS_AP038_BAKE_ONLY";code=0;
            }
            catch(Exception e) { receipt.error=e.ToString();Debug.LogException(e); }
            finally { File.WriteAllText(path,JsonUtility.ToJson(receipt,true)+"\n"); }
            EditorApplication.Exit(code);
        }
    }
}
