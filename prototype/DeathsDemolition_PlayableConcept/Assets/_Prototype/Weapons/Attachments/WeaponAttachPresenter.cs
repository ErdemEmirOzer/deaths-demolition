using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Weapons.Attachments
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §3.4) · shows the upgrade tree's weapon attachments (AP-040 E1–E6) on the classic
    /// weapon in hand, TopDown only (C-00 Q1). WeaponController.Awake adds it (S-02 B2). It reacts to the tree's StatsChanged, the
    /// weapon equip and the camera-mode change — never per frame — and resolves the set with <see cref="WeaponAttachRules"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAttachPresenter : MonoBehaviour
    {
        private WeaponController _controller;
        private WeaponAttachMount _mount;
        private Action _onStats;
        private Action<ClassicWeaponId> _onEquip;
        private Action<CameraModeChange> _onCamera;

        /// <summary>The mount the presenter last applied (tests / probes); null without an attachment library or a classic model.</summary>
        public WeaponAttachMount Mount => _mount;

        private void Awake()
        {
            _controller = GetComponent<WeaponController>();
            _onStats = Refresh;   // delegates made once: subscribing never allocates again
            _onEquip = OnEquip;
            _onCamera = OnCamera;
        }

        private void OnEnable()
        {
            TreeEvents.StatsChanged += _onStats;
            WeaponEvents.WeaponEquipped += _onEquip;
            CameraModeService.Changed += _onCamera;
            Refresh();
        }

        private void OnDisable()
        {
            TreeEvents.StatsChanged -= _onStats;
            WeaponEvents.WeaponEquipped -= _onEquip;
            CameraModeService.Changed -= _onCamera;
            if (WeaponAttachments.Active == _mount) WeaponAttachments.Active = null;
        }

        private void OnEquip(ClassicWeaponId id) => Refresh();
        private void OnCamera(CameraModeChange change) => Refresh();

        /// <summary>Re-resolve and re-mount (an unchanged set costs one comparison).</summary>
        public void Refresh()
        {
            WeaponAttachLibrary lib = WeaponAttachLibrary.Current;
            Transform root = _controller != null ? _controller.ClassicModelRoot : null;   // S-02 B2: the root SetModel installed
            ClassicWeaponRuntime classic = _controller != null ? _controller.Classic : null;
            WeaponAttachLibrary.Row row = lib != null && root != null && classic != null ? lib.Find(classic.Id) : null;
            if (row == null)
            {
                if (WeaponAttachments.Active == _mount) WeaponAttachments.Active = null;
                _mount = null;
                return;
            }

            if (_mount == null || _mount.transform != root) _mount = WeaponAttachMount.Ensure(root, row);
            if (_mount == null) return;
            ref readonly TreeStats s = ref TreeRuntime.Current;
            _mount.Apply(WeaponAttachRules.Resolve(in s, row.id, WeaponAttachRules.DominantElement(in s)), lib);
            _mount.SetShown(CameraModeService.Current == CameraMode.TopDown);
            WeaponAttachments.Active = _mount;
        }
    }
}
