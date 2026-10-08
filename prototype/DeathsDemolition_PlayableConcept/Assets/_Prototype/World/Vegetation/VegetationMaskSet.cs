using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DD.Prototype.World.Vegetation
{
    /// <summary>
    /// WP-093 · S-05's semantic masks (MASK-CONTRACT.md, schema <see cref="Schema"/>) read from
    /// Resources/VegetationMasks/&lt;scene&gt;/ (veg_mask_a, veg_mask_b, masks.json, optional veg_clear.json), and S-05's road
    /// network (Resources/WorldRoads/&lt;scene&gt;.json, wp092.roads.v1). Both merge INTO the physics probe's grid (union for
    /// exclusions, override for corridor / zone), so a missing mask can never put grass through a wall. Load time only.
    /// </summary>
    public sealed class VegetationMaskSet
    {
        public const string Schema = "wp093.vegmask.v1";
        public const string RoadsSchema = "wp092.roads.v1";
        public const string Folder = "VegetationMasks/";
        public const string RoadsFolder = "WorldRoads/";

        [Serializable]
        public sealed class Sidecar
        {
            public string schema = "";
            public string version = "";
            public string scene = "";
            public string source = "";
            public float minX, minZ, maxX, maxZ;
            public int width, height;
        }

        [Serializable]
        public sealed class ClearFile
        {
            public ClearEntry[] points = Array.Empty<ClearEntry>();
        }

        [Serializable]
        public sealed class ClearEntry
        {
            public float x, z, r;
            public string kind = "spawn";
        }

        public Texture2D MaskA;
        public Texture2D MaskB;
        public float MinX, MinZ, MaxX, MaxZ;
        public string Version = "";
        public string Scene = "";
        private ClearEntry[] _clear = Array.Empty<ClearEntry>();

        /// <summary>The scene's mask set, or null (no masks: the fallback builder runs alone). Wrong schema → null + one error line.</summary>
        public static VegetationMaskSet TryLoad(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            string folder = Folder + sceneName + "/";
            var json = Resources.Load<TextAsset>(folder + "masks");
            if (json == null) return null;
            Sidecar side;
            try
            {
                side = JsonUtility.FromJson<Sidecar>(json.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[Vegetation] " + folder + "masks.json unreadable (" + e.Message + "); masks ignored, fallback builder only.");
                return null;
            }

            if (side == null || side.schema != Schema)
            {
                Debug.LogError("[Vegetation] " + folder + "masks.json schema '" + (side != null ? side.schema : "null") + "' is not " + Schema + "; masks ignored, fallback builder only.");
                return null;
            }

            var set = new VegetationMaskSet
            {
                MaskA = Resources.Load<Texture2D>(folder + "veg_mask_a"),
                MaskB = Resources.Load<Texture2D>(folder + "veg_mask_b"),
                MinX = side.minX, MinZ = side.minZ, MaxX = side.maxX, MaxZ = side.maxZ,
                Version = side.version, Scene = side.scene
            };
            if ((set.MaskA != null && !set.MaskA.isReadable) || (set.MaskB != null && !set.MaskB.isReadable))
            {
                Debug.LogError("[Vegetation] " + folder + " masks are not Read/Write enabled (MASK-CONTRACT §4); masks ignored.");
                return null;
            }

            var clear = Resources.Load<TextAsset>(folder + "veg_clear");
            if (clear != null)
            {
                ClearFile f = JsonUtility.FromJson<ClearFile>(clear.text);
                if (f != null && f.points != null) set._clear = f.points;
            }

            return set;
        }

        /// <summary>Merges the masks into the probe grid (MASK-CONTRACT §2 / §5).</summary>
        public void ApplyTo(VegetationGrid grid)
        {
            Color32[] a = MaskA != null ? MaskA.GetPixels32() : null;
            Color32[] b = MaskB != null ? MaskB.GetPixels32() : null;
            float sx = MaxX - MinX, sz = MaxZ - MinZ;
            if (sx <= 0f || sz <= 0f) return;
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 c = grid.CellCenter(i);
                float u = (c.x - MinX) / sx, v = (c.y - MinZ) / sz;
                if (u < 0f || v < 0f || u >= 1f || v >= 1f) continue;
                if (a != null)
                {
                    Color32 p = a[Pixel(MaskA, u, v)];
                    if (p.r >= 128) grid.Set(i, VegetationGrid.Flag.Path);
                    if (p.g >= 128) grid.Set(i, VegetationGrid.Flag.Excluded);
                    if (p.b >= 128) grid.Set(i, VegetationGrid.Flag.Water);
                    if (p.a > 0) grid.Corridor[i] = p.a >= 128 ? (byte)2 : (byte)1;
                }

                if (b != null)
                {
                    Color32 p = b[Pixel(MaskB, u, v)];
                    if (p.r > 0 && !VegetationGrid.IsSolid((VegetationObstacle)grid.Obstacle[i]))
                    {
                        grid.Obstacle[i] = (byte)VegetationObstacle.Wall;
                        grid.ObstacleHeight[i] = p.r * 0.1f;
                    }

                    if (p.g > 0) grid.Zone[i] = p.g;
                    grid.DensityMul[i] = p.b;
                    grid.HeightMul[i] = p.a;
                }
            }

            for (int k = 0; k < _clear.Length; k++)
            {
                ClearEntry e = _clear[k];
                if (e == null) continue;
                VegetationGrid.ClearKind kind = e.kind == "weapon" ? VegetationGrid.ClearKind.Weapon : e.kind == "door" ? VegetationGrid.ClearKind.Door : VegetationGrid.ClearKind.Spawn;
                grid.ClearPoints.Add(new VegetationGrid.ClearPoint { Position = new Vector2(e.x, e.z), Radius = e.r, Kind = kind });
            }
        }

        private static int Pixel(Texture2D t, float u, float v)
        {
            int x = Mathf.Clamp((int)(u * t.width), 0, t.width - 1);
            int y = Mathf.Clamp((int)(v * t.height), 0, t.height - 1);
            return y * t.width + x;
        }

        // ------------------------------------------------------------------ roads.json (wp092.roads.v1)

        /// <summary>Resources/WorldRoads/&lt;scene&gt;.json (a repo copy of K/wp-092/layout/roads.json), or null.</summary>
        public static List<VegetationRoad> TryLoadRoads(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            var text = Resources.Load<TextAsset>(RoadsFolder + sceneName);
            if (text == null) return null;
            List<VegetationRoad> roads = ParseRoads(text.text, out string error);
            if (error != null) Debug.LogError("[Vegetation] " + RoadsFolder + sceneName + ".json: " + error + "; roads ignored.");
            return roads;
        }

        /// <summary>Parses wp092.roads.v1. Null + error when the schema or the shape is wrong.</summary>
        public static List<VegetationRoad> ParseRoads(string json, out string error)
        {
            error = null;
            object root;
            try
            {
                int pos = 0;
                root = MiniJson.Value(json, ref pos);
            }
            catch (Exception e)
            {
                error = "unparseable (" + e.Message + ")";
                return null;
            }

            var obj = root as Dictionary<string, object>;
            if (obj == null) { error = "root is not an object"; return null; }
            if (!obj.TryGetValue("schema", out object schema) || !(schema is string s) || s != RoadsSchema)
            {
                error = "schema is not " + RoadsSchema;
                return null;
            }

            var list = new List<VegetationRoad>(32);
            if (!obj.TryGetValue("roads", out object roadsObj) || !(roadsObj is List<object> roads)) return list;
            for (int r = 0; r < roads.Count; r++)
            {
                if (!(roads[r] is Dictionary<string, object> ro)) continue;
                var road = new VegetationRoad();
                if (ro.TryGetValue("id", out object id) && id is string ids) road.Id = ids;
                if (ro.TryGetValue("type", out object type) && type is string ts) road.Type = ts;
                if (ro.TryGetValue("width", out object width) && width is double wd) road.Width = (float)wd;
                if (ro.TryGetValue("points", out object pts) && pts is List<object> pl)
                {
                    var points = new List<Vector2>(pl.Count);
                    for (int k = 0; k < pl.Count; k++)
                    {
                        if (pl[k] is List<object> xz && xz.Count >= 2 && xz[0] is double x && xz[1] is double z) points.Add(new Vector2((float)x, (float)z));
                    }

                    road.Points = points.ToArray();
                }

                if (road.Points.Length >= 2) list.Add(road);
            }

            return list;
        }

        /// <summary>Just enough JSON for roads.json: objects, arrays, numbers, strings, true / false / null.</summary>
        private static class MiniJson
        {
            public static object Value(string s, ref int p)
            {
                Skip(s, ref p);
                if (p >= s.Length) throw new FormatException("unexpected end");
                char c = s[p];
                if (c == '{') return Obj(s, ref p);
                if (c == '[') return Arr(s, ref p);
                if (c == '"') return Str(s, ref p);
                if (c == 't' && Lit(s, ref p, "true")) return true;
                if (c == 'f' && Lit(s, ref p, "false")) return false;
                if (c == 'n' && Lit(s, ref p, "null")) return null;
                return Num(s, ref p);
            }

            private static Dictionary<string, object> Obj(string s, ref int p)
            {
                var d = new Dictionary<string, object>();
                p++;
                Skip(s, ref p);
                if (p < s.Length && s[p] == '}') { p++; return d; }
                while (true)
                {
                    Skip(s, ref p);
                    string key = Str(s, ref p);
                    Skip(s, ref p);
                    if (p >= s.Length || s[p] != ':') throw new FormatException("':' expected at " + p);
                    p++;
                    d[key] = Value(s, ref p);
                    Skip(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == '}') { p++; return d; }
                    throw new FormatException("',' or '}' expected at " + p);
                }
            }

            private static List<object> Arr(string s, ref int p)
            {
                var l = new List<object>();
                p++;
                Skip(s, ref p);
                if (p < s.Length && s[p] == ']') { p++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref p));
                    Skip(s, ref p);
                    if (p < s.Length && s[p] == ',') { p++; continue; }
                    if (p < s.Length && s[p] == ']') { p++; return l; }
                    throw new FormatException("',' or ']' expected at " + p);
                }
            }

            private static string Str(string s, ref int p)
            {
                if (p >= s.Length || s[p] != '"') throw new FormatException("string expected at " + p);
                p++;
                var sb = new StringBuilder();
                while (p < s.Length)
                {
                    char c = s[p++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && p < s.Length)
                    {
                        char e = s[p++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (p + 4 <= s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(p, 4), 16)); p += 4; }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                throw new FormatException("unterminated string");
            }

            private static double Num(string s, ref int p)
            {
                int start = p;
                while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) p++;
                if (p == start) throw new FormatException("value expected at " + p);
                return double.Parse(s.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private static bool Lit(string s, ref int p, string lit)
            {
                if (string.CompareOrdinal(s, p, lit, 0, lit.Length) != 0) throw new FormatException("bad literal at " + p);
                p += lit.Length;
                return true;
            }

            private static void Skip(string s, ref int p)
            {
                while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            }
        }
    }
}
