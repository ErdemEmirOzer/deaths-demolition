using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// WP-107 (R4): one wall buy point. Until P-05's station asset (AP-040) it is a placeholder weapon rack read from above:
    /// two posts, a shelf with the weapon's own AP-013 / AP-021 model lying on its side (its profile is the top-down
    /// silhouette), and a sign board behind it - red lacquer on the rocket launcher's rack. One box collider for the whole
    /// rack (the player walks around it, rounds stop on it); the parts cast no shadow but the sign.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallBuyStation : MonoBehaviour
    {
        public const float ShelfHeight = 0.95f, RackWidth = 1.3f, RackDepth = 0.42f, SignHeight = 0.55f;
        private static readonly float[] s_modelLodHeights = { 0.12f, 0.05f, 0.01f };   // WeaponController's classic model LODs

        public string Id { get; private set; }
        public ClassicWeaponId Weapon { get; private set; }
        public string WeaponKey { get; private set; }

        /// <summary>The weapon model on the shelf (null when the library has no model for it).</summary>
        public GameObject Model { get; private set; }

        public void Init(string id, ClassicWeaponId weapon, string weaponKey, bool premium, Material wood, Material plank, Material sign, Material lacquer)
        {
            Id = id;
            Weapon = weapon;
            WeaponKey = weaponKey;
            name = "WallBuy_" + id;

            Part("PostL", new Vector3(-RackWidth * 0.42f, ShelfHeight * 0.5f, 0f), new Vector3(0.11f, ShelfHeight, 0.11f), wood, false);
            Part("PostR", new Vector3(RackWidth * 0.42f, ShelfHeight * 0.5f, 0f), new Vector3(0.11f, ShelfHeight, 0.11f), wood, false);
            Part("Shelf", new Vector3(0f, ShelfHeight, 0f), new Vector3(RackWidth, 0.06f, RackDepth), plank, false);
            Part("Sign", new Vector3(0f, ShelfHeight + SignHeight * 0.5f + 0.03f, -RackDepth * 0.5f), new Vector3(RackWidth, SignHeight, 0.05f),
                premium ? lacquer : sign, true);

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (ShelfHeight + SignHeight) * 0.5f, -0.02f);
            box.size = new Vector3(RackWidth, ShelfHeight + SignHeight + 0.05f, RackDepth + 0.05f);

            Model = MountModel(weapon);
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

        private GameObject MountModel(ClassicWeaponId weapon)
        {
            ClassicWeaponVisualLibrary library = ClassicWeaponVisualLibrary.Current;
            if (library == null) return null;
            ClassicWeaponVisualLibrary.Entry entry = library.Find(weapon);
            WeaponDefinition def = Arsenal.Get(weapon);
            if ((entry == null || !entry.HasModel) && def != null) entry = library.Find(def.presentation.placeholderVisual);
            GameObject model = ClassicWeaponVisualLibrary.Instantiate(entry, library.material, s_modelLodHeights);
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
    }
}
