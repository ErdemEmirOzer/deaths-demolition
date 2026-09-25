using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DD.Prototype.World
{
    /// <summary>
    /// WP-075 C2 · The cloud shadow (direction Ek A §4): the sun's cookie is the cloud cover's shadow — AP-023's
    /// T_cloud_cookie, 240 × 240 m on the ground, drifting 1.3 m/s toward 52° with the cloud planes
    /// (<see cref="WorldAtmosphere.CloudShadow"/>). Added to the sun at runtime only (<see cref="Attach"/>): the
    /// lightmapper never sees it, so moving clouds are not baked into the indirect light.
    ///
    /// URP 14 projects a directional cookie in LIGHT space (LightCookieManager.SetupMainLight):
    ///     uv = (lightLocal(positionWS).xy − lightCookieOffset) / lightCookieSize + 0.5
    /// with the light's position and rotation. On the ground the light-space y axis is foreshortened by
    /// sin(elevation), so size.y = 240 · sin(elevation) gives the 240 m tile along the sun azimuth as well; the
    /// drift moves the offset by (drift · right, drift · up) per metre.
    ///
    /// The same projection is published for custom shaders — shaft cards and ground mist (C4 / C5), S-07's
    /// particles — as two rows, uv = (dot(float4(positionWS, 1), _DD_CloudCookieU), dot(…, _DD_CloudCookieV)):
    /// URP's own mapping, height included, so a point in the air is lit exactly when the ground at the end of
    /// its sun ray is (Assets/_Art/Shaders/DD_CloudCookie.hlsl). _DD_ShaftDir.xyz is the direction the light
    /// travels, .w the screen-space shaft strength (C4 writes <see cref="ShaftStrength"/>). With no driver the
    /// globals read a white cookie: everything lit, nothing modulated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudShadowDriver : MonoBehaviour
    {
        public static readonly int CookieId = Shader.PropertyToID("_DD_CloudCookie");
        public static readonly int CookieUId = Shader.PropertyToID("_DD_CloudCookieU");
        public static readonly int CookieVId = Shader.PropertyToID("_DD_CloudCookieV");
        public static readonly int ShaftDirId = Shader.PropertyToID("_DD_ShaftDir");

        /// <summary>Phase override for a Player / batch run ("0.25"): the DEC-077 gates run at 0 / 0.25 / 0.5 / 0.75.</summary>
        public const string PhaseEnvironmentVariable = "DD_CLOUD_PHASE";

        /// <summary>
        /// Captures and the DEC-077 gates (direction §4, four phases): a fixed drift phase — the fraction of one tile
        /// travelled along the drift — or null to drift in real time. Read every frame.
        /// </summary>
        public static float? ForcedPhase;

        public static CloudShadowDriver Active { get; private set; }

        /// <summary>C4 (TP/FP screen-space shafts, 0–1); published as _DD_ShaftDir.w.</summary>
        [NonSerialized] public float ShaftStrength;

        private Light _light;
        private UniversalAdditionalLightData _data;
        private Texture _cookie;
        private Vector2 _offset;

        public Light Sun => _light;
        public Texture Cookie => _cookie;
        public Vector2 CookieSize { get; private set; }
        public Vector2 CookieOffset => _offset;
        public Vector4 RowU { get; private set; }
        public Vector4 RowV { get; private set; }

        /// <summary>Unit vector the clouds drift toward on the ground (compass <see cref="WorldAtmosphere.CloudShadow.DriftCompassDegrees"/>).</summary>
        public static Vector3 DriftDirection
        {
            get
            {
                float a = WorldAtmosphere.CloudShadow.DriftCompassDegrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitGlobals()
        {
            ClearGlobals();
            string env = Environment.GetEnvironmentVariable(PhaseEnvironmentVariable);
            if (!string.IsNullOrEmpty(env) && float.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out float phase))
                ForcedPhase = Mathf.Repeat(phase, 1f);
        }

        /// <summary>White cookie, constant uv: custom shaders read "lit" when no world drives the clouds.</summary>
        public static void ClearGlobals()
        {
            Shader.SetGlobalTexture(CookieId, Texture2D.whiteTexture);
            Shader.SetGlobalVector(CookieUId, new Vector4(0f, 0f, 0f, 0.5f));
            Shader.SetGlobalVector(CookieVId, new Vector4(0f, 0f, 0f, 0.5f));
            Shader.SetGlobalVector(ShaftDirId, new Vector4(0f, -1f, 0f, 0f));
        }

        /// <summary>Puts the cloud shadow on <paramref name="sun"/> (play mode only). Null when the cookie resource is missing.</summary>
        public static CloudShadowDriver Attach(Light sun)
        {
            if (sun == null || !Application.isPlaying) return null;
            string name = WorldAtmosphere.CloudShadow.CookieResourceFor(WorldLayout.Current);   // DEC-101: v6 overcast
            var cookie = Resources.Load<Texture2D>(name);
            if (cookie == null)
            {
                Debug.LogWarning("[CloudShadow] Resources/" + name + " missing: no cloud shadow.");
                return null;
            }

            return Attach(sun, cookie);
        }

        /// <summary>Unchecked attach (EditMode tests): no play-mode guard, the caller's cookie.</summary>
        public static CloudShadowDriver Attach(Light sun, Texture cookie)
        {
            if (!sun.TryGetComponent(out CloudShadowDriver driver)) driver = sun.gameObject.AddComponent<CloudShadowDriver>();
            driver.Bind(sun, cookie);
            return driver;
        }

        private void Bind(Light sun, Texture cookie)
        {
            _light = sun;
            _cookie = cookie;
            _data = sun.GetUniversalAdditionalLightData();
            _light.cookie = _cookie;
            Apply();
        }

        private void OnEnable()
        {
            Active = this;
            if (_light != null) _light.cookie = _cookie;
        }

        private void OnDisable()
        {
            if (_light != null) _light.cookie = null;
            if (Active != this) return;
            Active = null;
            ClearGlobals();
        }

        private void LateUpdate()
        {
            if (_light == null) return;
            if (!ForcedPhase.HasValue)
            {
                float step = WorldAtmosphere.CloudShadow.SpeedFor(WorldLayout.Current) * Time.deltaTime;   // DEC-102 r2: v6 static (its sky is)
                Transform t = _light.transform;
                Vector3 d = DriftDirection;
                _offset += new Vector2(Vector3.Dot(d, t.right), Vector3.Dot(d, t.up)) * step;
            }

            Apply();
        }

        /// <summary>Recomputes size, offset, the URP light data and the shader globals from the sun's current transform.</summary>
        public void Apply()
        {
            Transform t = _light.transform;
            Vector3 right = t.right, up = t.up;
            var horizontal = new Vector3(t.forward.x, 0f, t.forward.z);
            float sinElevation = horizontal.sqrMagnitude > 1e-6f ? Mathf.Abs(Vector3.Dot(horizontal.normalized, up)) : 1f;
            float tile = WorldAtmosphere.CloudShadow.TileMeters;
            var size = new Vector2(tile, tile * Mathf.Max(sinElevation, 0.05f));
            CookieSize = size;

            if (ForcedPhase.HasValue)
            {
                Vector3 d = DriftDirection;
                _offset = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)) * (ForcedPhase.Value * tile);
            }

            _offset = new Vector2(Mathf.Repeat(_offset.x, size.x), Mathf.Repeat(_offset.y, size.y));
            _data.lightCookieSize = size;
            _data.lightCookieOffset = _offset;

            // VisibleLight.localToWorldMatrix carries position and rotation only.
            Matrix4x4 worldToLight = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            RowU = worldToLight.GetRow(0) / size.x + new Vector4(0f, 0f, 0f, 0.5f - _offset.x / size.x);
            RowV = worldToLight.GetRow(1) / size.y + new Vector4(0f, 0f, 0f, 0.5f - _offset.y / size.y);

            Shader.SetGlobalTexture(CookieId, _cookie);
            Shader.SetGlobalVector(CookieUId, RowU);
            Shader.SetGlobalVector(CookieVId, RowV);
            Vector3 f = t.forward;
            Shader.SetGlobalVector(ShaftDirId, new Vector4(f.x, f.y, f.z, ShaftStrength));
        }

        /// <summary>The cookie uv the shaders compute for <paramref name="positionWS"/> (unwrapped; the texture repeats).</summary>
        public Vector2 CookieUV(Vector3 positionWS)
        {
            var p = new Vector4(positionWS.x, positionWS.y, positionWS.z, 1f);
            return new Vector2(Vector4.Dot(p, RowU), Vector4.Dot(p, RowV));
        }
    }
}
