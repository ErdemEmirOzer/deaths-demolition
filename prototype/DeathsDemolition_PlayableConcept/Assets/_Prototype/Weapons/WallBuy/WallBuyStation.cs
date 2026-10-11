using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4): one wall buy point.
    /// - WP-107B: with the AP-040 library (<see cref="WallBuyStationLibrary"/>) it is the Codex station body - the freestanding
    ///   board (D1 V02) or, for the rocket launcher, the shrine (D3) - with the weapon's own AP-013 / AP-021 model at
    ///   SOCKET_Weapon and its silhouette decal at SOCKET_Decal; the box proxy (*_COL) becomes the collider and never renders;
    ///   the lanterns glow by emission only (no real-time light).
    /// - Without the library it is the WP-107 placeholder rack read from above: two posts, a shelf with the weapon lying on its
    ///   side, a sign board behind it - red lacquer on the rocket launcher's rack.
    /// One box collider for the whole station (the player walks around it, rounds stop on it).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallBuyStation : MonoBehaviour
    {
        public const float ShelfHeight = 0.95f, RackWidth = 1.3f, RackDepth = 0.42f, SignHeight = 0.55f;
        private static readonly float[] s_modelLodHeights = { 0.12f, 0.05f, 0.01f };   // WeaponController's classic model LODs

        public string Id { get; private set; }
        public ClassicWeaponId Weapon { get; private set; }
        public string WeaponKey { get; private set; }

        /// <summary>The weapon model on the station (null when the weapon library has no model for it).</summary>
        public GameObject Model { get; private set; }

        /// <summary>WP-107B: the AP-040 station body (null on the placeholder rack).</summary>
        public GameObject Body { get; private set; }

        /// <summary>WP-107B: the weapon's silhouette decal quad (null on the placeholder rack or without a decal).</summary>
        public Renderer Decal { get; private set; }

        /// <summary>Metres from the station's pivot to its front face; the player stands past it to use the station.</summary>
        public float FrontDepth { get; private set; } = RackDepth * 0.5f;

        public void Init(string id, ClassicWeaponId weapon, string weaponKey, bool premium, WallBuyStationLibrary library,
            Material wood, Material plank, Material sign, Material lacquer)
        {
            Id = id;
            Weapon = weapon;
            WeaponKey = weaponKey;
            name = "WallBuy_" + id;

            WallBuyStationLibrary.StationModel art = library != null ? library.ModelFor(premium) : null;
            if (art != null && BuildBody(art, library.DecalFor(weapon)))
            {
                return;
            }

            BuildRack(premium, wood, plank, sign, lacquer);
        }

        // ------------------------------------------------------------------ AP-040 body (WP-107B)

        private bool BuildBody(WallBuyStationLibrary.StationModel art, Material decal)
        {
            GameObject body = Instantiate(art.model, transform, false);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            Transform weaponSocket = Find(body.transform, WallBuyStationLibrary.WeaponSocket);
            Transform decalSocket = Find(body.transform, WallBuyStationLibrary.DecalSocket);
            if (weaponSocket == null)
            {
                Destroy(body);
                return false;
            }

            Body = body;
            FrontDepth = Mathf.Max(0.1f, art.frontDepth);

            // The box proxy becomes the collider and never renders; the body's other renderers cast the station's shadow.
            var box = gameObject.AddComponent<BoxCollider>();
            bool sized = false;
            MeshRenderer[] renderers = body.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                if (r.gameObject.name.EndsWith(WallBuyStationLibrary.ColliderSuffix))
                {
                    r.enabled = false;
                    MeshFilter f = r.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null && !sized)
                    {
                        // The proxy node carries the FBX's -90° X (its mesh is Z-up): the eight corners into station space.
                        Bounds b = f.sharedMesh.bounds, local = default;
                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                            Vector3 p = transform.InverseTransformPoint(r.transform.TransformPoint(corner));
                            if (c == 0) local = new Bounds(p, Vector3.zero); else local.Encapsulate(p);
                        }

                        box.center = local.center;
                        box.size = local.size;
                        sized = true;
                    }

                    continue;
                }

                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }

            if (!sized)
            {
                box.center = new Vector3(0f, 0.8f, 0f);
                box.size = new Vector3(1.2f, 1.6f, FrontDepth * 2f);
            }

            LODGroup lods = body.GetComponentInChildren<LODGroup>();
            if (lods != null && art.lodScreenHeights != null)
            {
                LOD[] levels = lods.GetLODs();
                for (int i = 0; i < levels.Length && i < art.lodScreenHeights.Length; i++)
                {
                    levels[i].screenRelativeTransitionHeight = art.lodScreenHeights[i];
                }

                lods.SetLODs(levels);
                lods.RecalculateBounds();
            }

            Model = MountOnSocket(Weapon, weaponSocket, art.weaponScale);
            if (decalSocket != null && decal != null)
            {
                Decal = BuildDecal(decalSocket, decal, art.decalSize);
            }

            return true;
        }

        private GameObject MountOnSocket(ClassicWeaponId weapon, Transform socket, float scale)
        {
            GameObject model = InstantiateWeaponModel(weapon);
            if (model == null) return null;
            model.name = "Model";
            model.transform.SetParent(transform, false);
            // AP040-integration-v001.json: the weapon display rotation is +90° about the station's up axis (Blender's Z display
            // rotation) - taken from the station root's basis, not the socket's (an identity socket would leave it sideways).
            model.transform.position = socket.position;
            model.transform.rotation = transform.rotation * Quaternion.Euler(0f, 90f, 0f);
            model.transform.localScale = Vector3.one * Mathf.Max(0.05f, scale);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            return model;
        }

        private Renderer BuildDecal(Transform socket, Material decal, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Decal";
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.SetParent(transform, false);
            go.transform.position = socket.position;
            // A Unity quad faces -Z; the station's front is +Z.
            go.transform.rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = decal;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private static Transform Find(Transform root, string childName)
        {
            if (root.name == childName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = Find(root.GetChild(i), childName);
                if (t != null) return t;
            }

            return null;
        }

        // ------------------------------------------------------------------ WP-107 placeholder rack

        private void BuildRack(bool premium, Material wood, Material plank, Material sign, Material lacquer)
        {
            FrontDepth = RackDepth * 0.5f;
            Part("PostL", new Vector3(-RackWidth * 0.42f, ShelfHeight * 0.5f, 0f), new Vector3(0.11f, ShelfHeight, 0.11f), wood, false);
            Part("PostR", new Vector3(RackWidth * 0.42f, ShelfHeight * 0.5f, 0f), new Vector3(0.11f, ShelfHeight, 0.11f), wood, false);
            Part("Shelf", new Vector3(0f, ShelfHeight, 0f), new Vector3(RackWidth, 0.06f, RackDepth), plank, false);
            Part("Sign", new Vector3(0f, ShelfHeight + SignHeight * 0.5f + 0.03f, -RackDepth * 0.5f), new Vector3(RackWidth, SignHeight, 0.05f),
                premium ? lacquer : sign, true);

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (ShelfHeight + SignHeight) * 0.5f, -0.02f);
            box.size = new Vector3(RackWidth, ShelfHeight + SignHeight + 0.05f, RackDepth + 0.05f);

            Model = MountOnShelf(Weapon);
        }

        private void Part(string partName, Vector3 localPosition, Vector3 size, Material material, bool shadow)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
        }

        private GameObject MountOnShelf(ClassicWeaponId weapon)
        {
            GameObject model = InstantiateWeaponModel(weapon);
            if (model == null) return null;

            model.name = "Model";
            model.transform.SetParent(transform, false);
            // Barrel along the shelf, lying on its side: from above the player sees the weapon's profile.
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            model.transform.localPosition = Vector3.zero;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                Vector3 centre = transform.InverseTransformPoint(b.center);
                float halfHeight = b.extents.y;
                model.transform.localPosition = new Vector3(-centre.x, ShelfHeight + 0.03f + halfHeight - centre.y, -centre.z);
                for (int i = 0; i < renderers.Length; i++) renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            return model;
        }

        private static GameObject InstantiateWeaponModel(ClassicWeaponId weapon)
        {
            ClassicWeaponVisualLibrary library = ClassicWeaponVisualLibrary.Current;
            if (library == null) return null;
            ClassicWeaponVisualLibrary.Entry entry = library.Find(weapon);
            WeaponDefinition def = Arsenal.Get(weapon);
            if ((entry == null || !entry.HasModel) && def != null) entry = library.Find(def.presentation.placeholderVisual);
            return ClassicWeaponVisualLibrary.Instantiate(entry, library.material, s_modelLodHeights);
        }
    }
}
