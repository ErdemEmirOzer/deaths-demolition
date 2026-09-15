using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Import study. Dumps what the Unity importer actually produced for every
    /// FBX and PNG under <c>Assets/_Art</c>: importer settings, sub-assets, mesh
    /// bounds/counts/attributes, texture format. The output is the measurement that the
    /// Import Contract (ADR-011-1) is written against — nothing in here changes an asset.
    ///
    /// Headless: <c>-executeMethod DD.Art.Pipeline.EditorTools.ArtImportStudy.Run -ddOut /abs/path.json</c>
    /// </summary>
    public static class ArtImportStudy
    {
        private const string ArtRoot = "Assets/_Art";

        [Serializable]
        public sealed class MeshRecord
        {
            public string name;
            public int vertexCount;
            public int subMeshCount;
            public int triangleCount;
            public string indexFormat;
            public bool hasNormals;
            public bool hasTangents;
            public bool hasUv0;
            public bool hasUv1;
            public bool hasColors;
            public bool isReadable;
            public Vector3 boundsMin;
            public Vector3 boundsMax;
        }

        [Serializable]
        public sealed class NodeRecord
        {
            public string path;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public Vector3 localScale;
            public string meshName;
        }

        [Serializable]
        public sealed class ModelRecord
        {
            public string assetPath;
            public string guid;
            public long bytes;
            public float globalScale;
            public bool useFileScale;
            public float fileScale;
            public bool bakeAxisConversion;
            public string importNormals;
            public string importTangents;
            public string meshCompression;
            public bool isReadable;
            public string indexFormat;
            public bool optimizeMeshPolygons;
            public bool optimizeMeshVertices;
            public bool weldVertices;
            public bool generateSecondaryUV;
            public bool swapUVChannels;
            public string materialImportMode;
            public string materialLocation;
            public string animationType;
            public bool importAnimation;
            public bool importBlendShapes;
            public bool importCameras;
            public bool importLights;
            public bool importVisibility;
            public bool preserveHierarchy;
            public bool importSettingsMissing;
            public List<string> subAssets = new List<string>();
            public List<MeshRecord> meshes = new List<MeshRecord>();
            public List<NodeRecord> nodes = new List<NodeRecord>();
        }

        [Serializable]
        public sealed class TextureRecord
        {
            public string assetPath;
            public string guid;
            public long bytes;
            public string textureType;
            public bool sRGBTexture;
            public bool mipmapEnabled;
            public bool streamingMipmaps;
            public int maxTextureSize;
            public string textureCompression;
            public bool crunchedCompression;
            public string filterMode;
            public string wrapMode;
            public int anisoLevel;
            public bool alphaIsTransparency;
            public string alphaSource;
            public string standaloneFormat;
            public bool standaloneOverridden;
            public int importedWidth;
            public int importedHeight;
            public string importedFormat;
            public int importedMipCount;
            public long importedRuntimeBytes;
            public bool importSettingsMissing;
        }

        [Serializable]
        public sealed class Report
        {
            public string utc;
            public string unityVersion;
            public string projectPath;
            public List<ModelRecord> models = new List<ModelRecord>();
            public List<TextureRecord> textures = new List<TextureRecord>();
        }

        public static void Run()
        {
            string outPath = Arg("-ddOut");
            if (string.IsNullOrEmpty(outPath))
            {
                throw new InvalidOperationException("[ArtImportStudy] -ddOut <file.json> is required.");
            }

            Report report = Build();
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, JsonUtility.ToJson(report, true));
            Debug.Log("[ArtImportStudy] models=" + report.models.Count + " textures=" + report.textures.Count + " -> " + outPath);
        }

        public static Report Build()
        {
            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath
            };

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                var m = new ModelRecord
                {
                    assetPath = path,
                    guid = guid,
                    bytes = new FileInfo(path).Length,
                    globalScale = importer.globalScale,
                    useFileScale = importer.useFileScale,
                    fileScale = importer.fileScale,
                    bakeAxisConversion = importer.bakeAxisConversion,
                    importNormals = importer.importNormals.ToString(),
                    importTangents = importer.importTangents.ToString(),
                    meshCompression = importer.meshCompression.ToString(),
                    isReadable = importer.isReadable,
                    indexFormat = importer.indexFormat.ToString(),
                    optimizeMeshPolygons = importer.optimizeMeshPolygons,
                    optimizeMeshVertices = importer.optimizeMeshVertices,
                    weldVertices = importer.weldVertices,
                    generateSecondaryUV = importer.generateSecondaryUV,
                    swapUVChannels = importer.swapUVChannels,
                    materialImportMode = importer.materialImportMode.ToString(),
                    materialLocation = importer.materialLocation.ToString(),
                    animationType = importer.animationType.ToString(),
                    importAnimation = importer.importAnimation,
                    importBlendShapes = importer.importBlendShapes,
                    importCameras = importer.importCameras,
                    importLights = importer.importLights,
                    importVisibility = importer.importVisibility,
                    preserveHierarchy = importer.preserveHierarchy,
                    importSettingsMissing = importer.importSettingsMissing
                };

                foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                {
                    if (sub == null)
                    {
                        continue;
                    }

                    m.subAssets.Add(sub.GetType().Name + ":" + sub.name);
                    if (sub is Mesh mesh)
                    {
                        m.meshes.Add(Describe(mesh));
                    }
                }

                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root != null)
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        var filter = t.GetComponent<MeshFilter>();
                        m.nodes.Add(new NodeRecord
                        {
                            path = PathOf(t, root.transform),
                            localPosition = t.localPosition,
                            localEulerAngles = t.localEulerAngles,
                            localScale = t.localScale,
                            meshName = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : ""
                        });
                    }
                }

                report.models.Add(m);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                report.textures.Add(new TextureRecord
                {
                    assetPath = path,
                    guid = guid,
                    bytes = new FileInfo(path).Length,
                    textureType = importer.textureType.ToString(),
                    sRGBTexture = importer.sRGBTexture,
                    mipmapEnabled = importer.mipmapEnabled,
                    streamingMipmaps = importer.streamingMipmaps,
                    maxTextureSize = importer.maxTextureSize,
                    textureCompression = importer.textureCompression.ToString(),
                    crunchedCompression = importer.crunchedCompression,
                    filterMode = importer.filterMode.ToString(),
                    wrapMode = importer.wrapMode.ToString(),
                    anisoLevel = importer.anisoLevel,
                    alphaIsTransparency = importer.alphaIsTransparency,
                    alphaSource = importer.alphaSource.ToString(),
                    standaloneFormat = standalone != null ? standalone.format.ToString() : "",
                    standaloneOverridden = standalone != null && standalone.overridden,
                    importedWidth = tex != null ? tex.width : 0,
                    importedHeight = tex != null ? tex.height : 0,
                    importedFormat = tex != null ? tex.format.ToString() : "",
                    importedMipCount = tex != null ? tex.mipmapCount : 0,
                    importedRuntimeBytes = tex != null ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) : 0,
                    importSettingsMissing = importer.importSettingsMissing
                });
            }

            report.models.Sort((a, b) => string.CompareOrdinal(a.assetPath, b.assetPath));
            report.textures.Sort((a, b) => string.CompareOrdinal(a.assetPath, b.assetPath));
            return report;
        }

        public static MeshRecord Describe(Mesh mesh)
        {
            int tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                tris += (int)(mesh.GetIndexCount(i) / 3);
            }

            return new MeshRecord
            {
                name = mesh.name,
                vertexCount = mesh.vertexCount,
                subMeshCount = mesh.subMeshCount,
                triangleCount = tris,
                indexFormat = mesh.indexFormat.ToString(),
                hasNormals = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal),
                hasTangents = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent),
                hasUv0 = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0),
                hasUv1 = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),
                hasColors = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color),
                isReadable = mesh.isReadable,
                boundsMin = mesh.bounds.min,
                boundsMax = mesh.bounds.max
            };
        }

        private static string PathOf(Transform t, Transform root)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null && t != root)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }

            return sb.ToString();
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
