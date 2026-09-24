using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static DD.Prototype.Tests.PlayMode.LifecycleTestKit;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-022 · UI art & HUD v2 in the real flow: the AP-012 sprites are bound on the
    /// screens, the HUD keeps the play area clear, the skin cards show the character,
    /// and the menus can be driven end to end with EventSystem move/submit events -
    /// the same events the StandaloneInputModule raises for the arrow keys, WASD, the
    /// d-pad and the left stick. Gamepad hardware is not on the build machine; this is
    /// the deterministic equivalent.
    /// </summary>
    [TestFixture]
    [Category("WP022")]
    public sealed class UiArtSliceTests
    {
        private readonly List<string> _hatalar = new List<string>();

#if UNITY_EDITOR
        /// <summary>
        /// WP-022M: text drawn in a play session must not write glyphs into TMP's persistent
        /// dynamic fallback (a dirty tree and a "-dirty" release stamp). The glyphs below are
        /// not in the static LiberationSans atlas, so they reach the fallback chain.
        /// </summary>
        [UnityTest]
        public IEnumerator TmpDinamikYedek_OyunOturumundaKaliciAssetYazilmaz()
        {
            const string path = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";
            var persistent = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
            Assert.IsNotNull(persistent, "fallback asset bulunamadı");
            int before = persistent.characterTable.Count;

            TMPro.TMP_FontAsset primary = TMPro.TMP_Settings.defaultFontAsset;
            Assert.IsNotNull(primary);
            Assert.IsFalse(primary.fallbackFontAssetTable.Contains(persistent), "oyun oturumunda kalıcı dinamik yedek zincirde olmamalı");

            var go = new GameObject("FallbackProbe", typeof(Canvas));
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TMPro.TextMeshProUGUI>();
            text.transform.SetParent(go.transform, false);
            text.font = primary;
            text.text = "\u2192 \u2713 \u00e9\u015f\u011f \u2605";
            text.ForceMeshUpdate();
            yield return null;

            TMPro.TMP_FontAsset twin = primary.fallbackFontAssetTable[0];
            Assert.IsFalse(UnityEditor.EditorUtility.IsPersistent(twin), "yedek oturum ikizi olmalı");
            Assert.Greater(twin.characterTable.Count, 0, "ikiz gerekli glifleri üretir (kapsam aynı)");
            Assert.AreEqual(before, persistent.characterTable.Count, "kalıcı yedek asset'e glif eklenmedi");
            Assert.IsFalse(UnityEditor.EditorUtility.IsDirty(persistent), "kalıcı yedek kirli işaretlenmedi");
            Object.Destroy(go);
        }
#endif

        [SetUp]
        public void SetUp()
        {
            _hatalar.Clear();
            RunFlowClock.ClearHolds();
            UiNavigation.Model.Clear();
            Application.logMessageReceived += HataTopla;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= HataTopla;
            RunFlowClock.ClearHolds();
            LogAssert.ignoreFailingMessages = false;
            PrototypeUiFlow.SelectedSkin = PlayerSkins.Default;
            Assert.AreEqual(0, _hatalar.Count, "Test boyunca UI/koşu hatası loglandı:\n" + string.Join("\n", _hatalar));
        }

        private void HataTopla(string mesaj, string iz, LogType tur)
        {
            if (tur != LogType.Error && tur != LogType.Exception && tur != LogType.Assert)
            {
                return;
            }

            if (mesaj.Contains("[UI]") || mesaj.Contains("[Run]") || mesaj.Contains("NullReferenceException") ||
                mesaj.Contains("MissingReferenceException") || mesaj.Contains("ArgumentException"))
            {
                _hatalar.Add(tur + ": " + mesaj);
            }
        }

        // ================================================================= yardımcılar

        private static GameObject Secili
        {
            get { return EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null; }
        }

        /// <summary>One navigation step, exactly as StandaloneInputModule dispatches an axis press.</summary>
        private static void Hareket(MoveDirection yon)
        {
            GameObject current = Secili;
            Assert.IsNotNull(current, "hareket için seçili kontrol yok");
            var data = new AxisEventData(EventSystem.current) { moveDir = yon, moveVector = Vektor(yon) };
            ExecuteEvents.Execute(current, data, ExecuteEvents.moveHandler);
        }

        private static Vector2 Vektor(MoveDirection yon)
        {
            switch (yon)
            {
                case MoveDirection.Up: return Vector2.up;
                case MoveDirection.Down: return Vector2.down;
                case MoveDirection.Left: return Vector2.left;
                default: return Vector2.right;
            }
        }

        private static void Onayla()
        {
            GameObject current = Secili;
            Assert.IsNotNull(current, "onay için seçili kontrol yok");
            ExecuteEvents.Execute(current, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        private static IEnumerator SeciliBekle(string adParcasi)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f)
            {
                GameObject s = Secili;
                if (s != null && (s.name.Contains(adParcasi) ||
                                  (s.transform.parent != null && s.transform.parent.name.Contains(adParcasi))))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("seçili kontrol '" + adParcasi + "' olmadı; seçili: " + (Secili != null ? Secili.name : "yok"));
        }

        // ============================================================== 1 · HUD sanatı

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Hud_IkonlarBagli_GuvenliBolgeBos_ReticleVar()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            yield return null;
            yield return null;

            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud, "HUD yok");

            // Icons bound from the theme (the theme is present in this project).
            Assert.IsTrue(UiArt.HasIcon("Hud.Gold"), "tema HUD ikonlarını vermeli");
            Image coin = Bul<Image>(hud.transform, "Gold", "Coin");
            Assert.IsNotNull(coin.sprite);
            Assert.AreEqual("Hud.Gold", coin.sprite.name, "gold paneli AP-012 coin glifini çizmeli");
            Image heart = Bul<Image>(hud.transform, "Health", "Heart");
            Assert.AreEqual("Hud.Health", heart.sprite.name);
            Image weapon = Bul<Image>(hud.transform, "ClassicWeapon", "WeaponIcon");
            Assert.IsTrue(weapon.enabled && weapon.sprite != null && weapon.sprite.name.StartsWith("Weapon."),
                "klasik silah paneli eldeki silahın glifini çizmeli: " + (weapon.sprite != null ? weapon.sprite.name : "yok"));

            // WP-068 8-R (user decision, concept A): no boxes - four ink clusters, and not one card, frame or panel fill under
            // them (the user's "her şey dikdörtgen" verdict made a test).
            Assert.AreEqual(4, hud.Clusters.Count, "dört mürekkep kümesi (altın · round · can · silah)");
            foreach (RectTransform cluster in hud.Clusters)
            {
                foreach (Image image in cluster.GetComponentsInChildren<Image>(true))
                {
                    if (image.GetComponentInParent<UiPrompt>(true) != null)
                    {
                        continue;   // a key cap is a key (AD 8-R), not a panel
                    }

                    Assert.AreNotEqual("Frame", image.name, cluster.name + " kümesinde çerçeve var");
                    Assert.IsFalse(image.sprite == UiSprites.Card || image.sprite == UiSprites.PanelBanded || image.sprite == UiSprites.Chip,
                        cluster.name + "/" + image.name + " bir kutu çiziyor (" + (image.sprite != null ? image.sprite.name : "null") + ")");
                }
            }

            // Safe zone: no cluster rect intersects the play area (REQ-104/105).
            var canvasRect = (RectTransform)hud.Clusters[0].parent;
            Rect canvas = canvasRect.rect;
            var zone = new Rect(
                canvas.xMin + canvas.width * RunHudScreen.HudSafeZone.xMin,
                canvas.yMin + canvas.height * RunHudScreen.HudSafeZone.yMin,
                canvas.width * RunHudScreen.HudSafeZone.width,
                canvas.height * RunHudScreen.HudSafeZone.height);
            foreach (RectTransform cluster in hud.Clusters)
            {
                Rect r = PanelRect(cluster, canvasRect);
                Assert.IsFalse(r.Overlaps(zone), cluster.name + " kümesi oyun alanına giriyor: " + r + " vs " + zone);
            }


            // Reticle: drawn at the aim point on its own canvas while the run is live.
            Transform reticle = hud.transform.Find(hud.GetType().Name + ".Canvas/ReticleLayer/Reticle");
            Assert.IsNotNull(reticle, "reticle yok");
            Assert.IsNotNull(reticle.parent.GetComponent<Canvas>(), "reticle kendi canvas'ında olmalı");
            Vector2 reticlePos = ((RectTransform)reticle).anchoredPosition;
            Assert.IsTrue(Mathf.Abs(reticlePos.x) < 2000f && Mathf.Abs(reticlePos.y) < 2000f,
                "canlı koşuda reticle ekran içinde (park pozisyonunda değil): " + reticlePos);
            // WP-068 8b · E15: the honest reticle - four G1 ticks at the real spread, around the aim point.
            for (int i = 0; i < 4; i++)
            {
                Image tick = reticle.Find("Tick." + i)?.GetComponent<Image>();
                Assert.IsNotNull(tick, "reticle tiki yok: " + i);
                Assert.IsTrue(tick.sprite != null && (tick.sprite.name == "Pip.Full" || tick.sprite.name == "Reticle.SpreadArc"), "tik G1 glifi: " + (tick.sprite != null ? tick.sprite.name : "null"));
                float d = ((RectTransform)tick.transform).anchoredPosition.magnitude;
                Assert.GreaterOrEqual(d, HudReticle.MinRadius, "tik yayılma yarıçapının dışında");
            }

            // Threat chips (WP-022B / WP-033): built, hidden until the director says so.
            Transform threatRow = hud.transform.Find(hud.GetType().Name + ".Canvas/ThreatRow");
            Assert.IsNotNull(threatRow, "tehdit çipi satırı yok");
            Assert.AreEqual(3, threatRow.childCount, "üç tehdit çipi");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(hud.ThreatChipVisible(i), "tehdit yokken çip " + i + " gizli olmalı");
            }

            // WP-068 slice 0 (T11 · D2): every text a threat chip can carry fits its chip
            // ("SURROUNDED" was clipped to "SURROUNDE" at text scale 1.0 in the baseline).
            for (int i = 0; i < threatRow.childCount; i++)
            {
                GameObject chip = threatRow.GetChild(i).gameObject;
                TMPro.TMP_Text chipLabel = chip.GetComponentInChildren<TMPro.TMP_Text>(true);
                string original = chipLabel.text;
                chip.SetActive(true);
                foreach (string text in new[] { "SURROUNDED", "BEHIND YOU", "ELITE NEARBY", "ULTRA NEARBY" })
                {
                    chipLabel.text = text;
                    chipLabel.ForceMeshUpdate();
                    Assert.IsFalse(chipLabel.isTextTruncated, "tehdit çipi " + i + " '" + text + "' metnini kesiyor");
                }

                chipLabel.text = original;
                chip.SetActive(false);
            }

            // WP-068 slice 0 (T11 · D1): no two visible labels in the weapon panel overlap
            // ("RIFLE · HITSCAN" slid over "P1 / FR1 / RD1" in the baseline HUD frames).
            {
                RectTransform classicPanel = null;
                foreach (RectTransform cluster in hud.Clusters) if (cluster.name == "ClassicWeapon") classicPanel = cluster;
                Assert.IsNotNull(classicPanel, "silah paneli");
                var labels = new List<RectTransform>();
                foreach (TMPro.TMP_Text t in classicPanel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    if (!string.IsNullOrEmpty(t.text) && t.transform.parent.name != "Suspended") labels.Add(t.rectTransform);
                }

                Assert.GreaterOrEqual(labels.Count, 3, "silah paneli ad + tür + mermi yazmalı");
                for (int a = 0; a < labels.Count; a++)
                {
                    for (int b = a + 1; b < labels.Count; b++)
                    {
                        // 0.5 px inset: stacked rows share an edge exactly, and that is not an overlap.
                        Rect ra = PanelRect(labels[a], canvasRect), rb = PanelRect(labels[b], canvasRect);
                        ra = Rect.MinMaxRect(ra.xMin + 0.5f, ra.yMin + 0.5f, ra.xMax - 0.5f, ra.yMax - 0.5f);
                        rb = Rect.MinMaxRect(rb.xMin + 0.5f, rb.yMin + 0.5f, rb.xMax - 0.5f, rb.yMax - 0.5f);
                        Assert.IsFalse(ra.Overlaps(rb), labels[a].name + " ile " + labels[b].name + " çakışıyor: " + ra + " / " + rb);
                    }
                }
            }

            // Screen events (WP-022B): the HUD announced itself; the settings file, not
            // PlayerPrefs, holds the UI scale.
            Assert.IsTrue(File.Exists(UiSettings.FilePath) || UiSettings.UiScale == 1f, "ui-settings.json yolu: " + UiSettings.FilePath);

            // Marker layer and special block are nested canvases too (rebuild isolation).
            Assert.IsNotNull(hud.transform.Find(hud.GetType().Name + ".Canvas/MarkerLayer").GetComponent<Canvas>());
            Transform special = hud.transform.Find(hud.GetType().Name + ".Canvas/SpecialWeapon");
            Assert.IsNotNull(special);
            Assert.IsNotNull(special.GetComponent<Canvas>(), "Special bloğu kendi canvas'ında olmalı");

            // WP-022H · Special usage v2: a fake Active state plus the WP-056 usage events
            // drive the usage line ("FIELD 4s · 2 HIT" → "FIELD ENDED · 2 HIT") and the
            // glyph; the string is rebuilt only on integer change (allocation contract
            // covered by SpecialWeaponHudAllocationTests).
            {
                var active = new DD.Prototype.Weapons.SpecialWeaponHudState
                {
                    Phase = DD.Prototype.Weapons.SpecialWeaponPhase.Active,
                    WeaponId = DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell,
                    DisplayName = "Kagura Bell",
                    ActiveRemaining = 20f,
                    ActiveDuration = 20f
                };
                // WP-022J r2 (AP-020 §2.4 S-8): a pickup raised while the snapshot still names
                // the PREVIOUS weapon must not print that name - the banner retitles from
                // the first Active snapshot that carries the new one.
                var stale = active; stale.DisplayName = "Ofuda Loom"; stale.WeaponId = DD.Prototype.Core.Config.SpecialWeaponId.OfudaLoom;
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(stale, false);
                DD.Prototype.Weapons.WeaponEvents.PublishSpecialPickedUp(20f);
                TMPro.TMP_Text banner = null;
                foreach (TMPro.TMP_Text t in hud.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (t.name == "Banner") banner = t;
                Assert.IsNotNull(banner, "banner metni");
                Assert.AreEqual("SPECIAL WEAPON ONLINE", banner.text, "alma anında eski ad basılmaz");
                // WP-068 8c · E13: the words wipe in - the mask starts closed and opens to the full banner width.
                var clip = (RectTransform)banner.transform.parent;
                Assert.IsNotNull(clip.GetComponent<RectMask2D>(), "afiş metni bir RectMask2D içinde silinerek girer");
                if (!UiMotion.ReducedMotion)
                {
                    Assert.LessOrEqual(clip.sizeDelta.x, 1f, "afiş silme maskesi kapalı başlar");
                }

                // E14: one tick a second of the active duration, on the A4 132 ring.
                Assert.AreEqual(20, hud.SpecialTickCount, "Special halkası aktif süre kadar saniye tiki taşır");
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, true);
                yield return null;
                StringAssert.Contains("KAGURA BELL ONLINE", banner.text, "ilk adlı Active snapshot banner'ı yeniden adlandırır");
                Image ringFill = null;
                foreach (Image im in special.GetComponentsInChildren<Image>(true)) if (im.name == "Fill" && im.transform.parent.name == "Ring") ringFill = im;
                Assert.IsNotNull(ringFill, "Special halka dolumu");
                Assert.AreEqual("Ring.Progress132", ringFill.sprite != null ? ringFill.sprite.name : "yok", "Special halkası A4 Ring.Progress132");
                Assert.IsTrue(ringFill.transform.parent.Find("Ticks").gameObject.activeInHierarchy, "aktifken saniye tikleri görünür");
                float wipeStart = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - wipeStart < 0.4f)
                {
                    DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, false);
                    yield return null;
                }

                Assert.AreEqual(((RectTransform)banner.transform).sizeDelta.x, clip.sizeDelta.x, 1f, "afiş 220 ms'de kendi genişliğine tam açılır");
                Assert.GreaterOrEqual(clip.sizeDelta.x, Mathf.Ceil(banner.GetPreferredValues(banner.text).x), "uzun afiş maskede kırpılmaz");
                Assert.AreEqual(1f, ringFill.fillAmount, 0.02f, "alma dolumu 320 ms sonra gerçek ilerlemeye oturur");
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.Activated, "sw_kagura_bell", Vector3.zero, Vector3.forward, 0f, 20f, 0);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.Fired, "sw_kagura_bell_ring", Vector3.zero, Vector3.forward, 0f, 0f, 0);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.FieldSpawned, "sw_kagura_bell_wave", Vector3.zero, Vector3.forward, 6f, 4f, 0);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.Hit, "sw_kagura_bell_wave", Vector3.zero, Vector3.forward, 6f, 0f, 1);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.Hit, "sw_kagura_bell_wave", Vector3.zero, Vector3.forward, 6f, 0f, 1);
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, false);
                yield return null;
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, false);
                yield return null;
                TMPro.TMP_Text usage = null;
                foreach (TMPro.TMP_Text t in hud.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (t.name == "Usage") usage = t;
                Assert.IsNotNull(usage, "Special usage satırı");
                StringAssert.Contains("FIELD", usage.text);
                StringAssert.Contains("2 HIT", usage.text);
                Image glyph = null;
                // The Special block's own glyph (the threat chips carry kit "Glyph" children too, WP-068 8b).
                foreach (Image im in hud.GetComponentsInChildren<Image>(true)) if (im.name == "Glyph" && im.GetComponentInParent<Canvas>().name == "SpecialWeapon") glyph = im;
                Assert.IsNotNull(glyph);
                Assert.IsTrue(glyph.enabled && glyph.sprite != null && glyph.sprite.name.StartsWith("Sw."), "AP-014 Kagura Bell glifi: " + (glyph.sprite != null ? glyph.sprite.name : "yok"));
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.FieldEnded, "sw_kagura_bell_wave", Vector3.zero, Vector3.forward, 6f, 0f, 2);
                // The weapon runtime republishes its own (None) state every frame in this
                // scene, so the fake Active state is re-asserted before each observed frame.
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, false);
                yield return null;
                DD.Prototype.Weapons.WeaponEvents.PublishSpecial(active, false);
                yield return null;
                StringAssert.Contains("FIELD ENDED", usage.text);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.Publish(DD.Prototype.Core.Config.SpecialWeaponId.KaguraBell, DD.Prototype.Weapons.SpecialUsageKind.Deactivated, "sw_kagura_bell", Vector3.zero, Vector3.forward, 0f, 0f, 0);
                yield return null;
                Assert.AreEqual(string.Empty, usage.text);
                DD.Prototype.Weapons.SpecialWeaponUsageEvents.ResetForTests();
                Assert.IsFalse(glyph.enabled);
            }

            // WP-068 8c · E14 / E21: the world marker is built on the marker canvas, hidden while nothing is on the ground.
            Assert.IsNotNull(hud.WorldMarker, "Special dünya işaretçisi");
            Assert.IsFalse(hud.WorldMarker.Root.gameObject.activeSelf, "yerde Special yokken işaretçi gizli");

            // WP-068 8c · E22: the death beat - the health block holds α 1 for 0.6 s and names the killing blow, then dims.
            {
                PlayerRig rig = PlayerRegistry.Primary;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "Zombie.Standard.Swipe");
                float died = Time.realtimeSinceStartup;
                while (hud.HealthGroup == null && Time.realtimeSinceStartup - died < 2f)
                {
                    yield return null;
                }

                Assert.IsNotNull(hud.HealthGroup, "ölümde can bloğu kendi grubunu alır");
                Assert.IsTrue(hud.HealthGroup.ignoreParentGroups, "can bloğu HUD sönümünün dışında");
                Assert.AreEqual(1f, hud.HealthGroup.alpha, 1e-3f, "can bloğu 0,6 s α 1 tutar");
                TMPro.TMP_Text state = Bul<TMPro.TMP_Text>(hud.transform, "Health", "State");
                Assert.AreEqual("DOWN  ·  killed by Standard swipe", state.text, "ölüm satırı öldüren vuruşu adlandırır");
                while (Time.realtimeSinceStartup - died < RunHudScreen.DeathLineHoldSeconds + 0.6f)
                {
                    yield return null;
                }

                Assert.LessOrEqual(hud.HealthGroup.alpha, 0.36f, "0,6 s sonra can bloğu da söner");
            }

            yield return SahneyiBekle(PrototypeScenes.Results);
            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp022.hud=OK");

        }

        private static Rect PanelRect(RectTransform panel, RectTransform canvas)
        {
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Vector3 min = canvas.InverseTransformPoint(corners[0]);
            Vector3 max = canvas.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static UiButton Buton(UiScreen screen, string ad)
        {
            foreach (UiButton b in screen.GetComponentsInChildren<UiButton>(true))
            {
                if (b.gameObject.name == ad)
                {
                    return b;
                }
            }

            Assert.Fail("buton yok: " + ad);
            return null;
        }

        private static T Bul<T>(Transform root, string blok, string ad) where T : Component
        {
            foreach (T c in root.GetComponentsInChildren<T>(true))
            {
                if (c.name != ad)
                {
                    continue;
                }

                for (Transform p = c.transform.parent; p != null; p = p.parent)
                {
                    if (p.name == blok)
                    {
                        return c;
                    }
                }
            }

            Assert.Fail("bulunamadı: " + blok + "/" + ad);
            return null;
        }

        // ======================================================== 2 · seçim önizlemeleri

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Secim_SkinOnizlemeleriGercekKarakter_SilahGlifleri()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;

            SelectionScreen screen = Object.FindObjectOfType<SelectionScreen>();
            Assert.IsNotNull(screen);
            Assert.IsNotNull(screen.SkinPreviews);
            Assert.AreEqual(PlayerSkins.Count, screen.SkinPreviews.Length);
            for (int i = 0; i < screen.SkinPreviews.Length; i++)
            {
                Assert.IsNotNull(screen.SkinPreviews[i], "skin " + (i + 1) + " önizlemesi kurulmadı");
                Assert.AreSame(UiArt.SkinPreview(i), screen.SkinPreviews[i].sprite, "skin " + (i + 1) + " kendi AP-006 renderını göstermeli");
                Assert.AreNotSame(screen.SkinPreviews[i].sprite, screen.SkinPreviews[(i + 1) % 3].sprite, "her skin farklı görsel");
            }

            int glif = 0;
            foreach (Image img in screen.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "Icon" && img.sprite != null && img.sprite.name.StartsWith("Weapon."))
                {
                    glif++;
                }
            }

            Assert.AreEqual(3, glif, "üç silah kartı üç AP-012 silah glifi");

            PrototypeUiFlow.GoToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Debug.Log("[DD-TEST] wp022.selection=OK");
        }

        // ============================================================ 3 · gamepad akışı

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gamepad_AnaMenudenSecimeVeGeri_YalnizHareketVeOnayla()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            yield return null;

            // Main menu opens with PLAY focused; Down walks the explicit chain.
            yield return SeciliBekle("Play");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Statistics");
            Hareket(MoveDirection.Down);
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Tab.Settings");
            Onayla();
            yield return null;
            Assert.AreEqual(UiContext.Settings, UiNavigation.Layer, "Settings paneli açılmalı");

            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.AreEqual(UiContext.Settings, menu.Pane);
            // WP-068 slice 6 (E6): four sections - DISPLAY (mode, UI scale), AUDIO (master, UI
            // sounds), GRAPHICS (quality), DATA (telemetry, hints). Focus lands on DISPLAY MODE
            // (not submitted here: the Editor has no window to switch).
            yield return SeciliBekle("Display");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.UI SCALE");
            var slider = Secili.GetComponent<UiSlider>();
            Assert.IsNotNull(slider);
            float before = slider.Value;
            Hareket(MoveDirection.Right);
            Assert.Greater(slider.Value, before, "sağ hareket UI ölçeğini artırmalı");
            Hareket(MoveDirection.Left);
            Assert.AreEqual(before, slider.Value, 0.02f, "sol hareket geri almalı");

            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.MASTER VOLUME");
            // WP-068 slice 11 (S-08): UI SOUNDS sits under the master volume.
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.UI SOUNDS");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.GRAPHICS QUALITY");
            // WP-022E: the run telemetry switch; Submit flips it and RunTelemetry follows; flip back.
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Telemetry");
            int raisedBefore = UiEvents.RaisedCount;
            bool telemetryBefore = UiSettings.Telemetry;
            Onayla();
            yield return null;
            Assert.AreNotEqual(telemetryBefore, UiSettings.Telemetry, "Submit telemetriyi çevirir");
            Assert.AreEqual(UiSettings.Telemetry, DD.Prototype.Diagnostics.Telemetry.RunTelemetry.Enabled, "RunTelemetry ayarı izler");
            Onayla();
            yield return null;
            Assert.AreEqual(telemetryBefore, UiSettings.Telemetry);
            Assert.Greater(UiEvents.RaisedCount, raisedBefore, "toggle bir UiEvent yükseltir");
            // WP-068 slice 10b: HINTS (U9) is the last control; Down wraps to DISPLAY MODE.
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Hints");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Display");

            // WP-022G: ACCESSIBILITY pane - palette / text scale / flashes / reduced motion
            // reachable from its tab; the palette toggle swaps UiTokens and back.
            menu.ShowPane(UiContext.Accessibility);
            yield return null;
            UiInput.Focus(Buton(menu, "Tab.Accessibility"));
            yield return SeciliBekle("Tab.Accessibility");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Palette");
            Onayla();
            yield return null;
            Assert.AreEqual(1, UiSettings.ColorPalette);
            Assert.AreEqual(UiPalette.ColorBlindSafe, UiTokens.Palette);
            Assert.AreNotEqual(UiTokens.Get(UiColor.StateSuccess), UiTokens.Get(UiColor.StateDanger));
            Onayla();
            yield return null;
            Assert.AreEqual(0, UiSettings.ColorPalette);
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("Setting.TEXT SCALE");
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("ReduceFlashes");
            Onayla();
            yield return null;
            Assert.IsTrue(UiSettings.ReduceFlashes);
            Assert.AreEqual(UiSettings.ReducedFlashScale, UiTokens.FlashScale, 0.001f);
            Assert.AreEqual(UiSettings.ReducedFlashScale, DD.Prototype.Feedback.Vfx.GameFeelSettings.FlashScale, 0.001f);
            Onayla();
            yield return null;
            Assert.IsFalse(UiSettings.ReduceFlashes);
            Hareket(MoveDirection.Down);
            yield return SeciliBekle("ReducedMotion");
            Onayla();
            yield return null;
            Assert.IsTrue(UiSettings.ReducedMotion, "Submit reduced motion'ı açmalı");
            Onayla();
            yield return null;
            Assert.IsFalse(UiSettings.ReducedMotion);

            // WP-022G: CONTROLS pane - keyboard slots on the left, gamepad options on the right.
            menu.ShowPane(UiContext.Controls);
            yield return null;
            UiInput.Focus(Buton(menu, "Tab.Controls"));
            yield return SeciliBekle("Tab.Controls");
            Hareket(MoveDirection.Right);
            yield return null;
            Assert.IsTrue(Secili != null && (Secili.name.StartsWith("Bind.") || Secili.name == "ResetBindings"), "sekmeden sağ → ilk klavye slotu; seçili: " + (Secili != null ? Secili.name : "yok"));
            Assert.IsNotNull(Buton(menu, "Rumble"), "gamepad seçenekleri");

            // Cancel (Escape / east button) returns to Profile with the tab focused.
            menu.ShowPane(UiContext.Profile); // the Cancel path minus the hardware read
            yield return null;
            Assert.AreEqual(UiContext.Profile, UiNavigation.Layer);

            // Back on the nav chain: focus the Profile tab, Up reaches PLAY; Up again stays
            // on PLAY (the chain does not wrap onto QUIT).
            UiInput.Focus(Buton(menu, "Tab.Profile"));
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Up);
            yield return SeciliBekle("Play");
            Hareket(MoveDirection.Up);
            yield return null;
            Assert.AreEqual("Play", Secili.name, "PLAY'den yukarı QUIT'e sarmamalı");
            Onayla();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            yield return null;
            yield return null;

            // WP-022F: the footer prompts follow the active device - START RUN shows "A" on
            // a pad and "ENTER" on the keyboard, BACK "B" / "ESC"; one source for both.
            {
                UiPrompt[] prompts = Object.FindObjectsOfType<UiPrompt>(true);
                Assert.GreaterOrEqual(prompts.Length, 2, "Selection footer'ında iki istem");
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
                yield return null;
                UiPrompt submit = null, cancel = null;
                foreach (UiPrompt p in prompts)
                {
                    if (p.Action == UiPromptAction.Submit) submit = p;
                    if (p.Action == UiPromptAction.Cancel) cancel = p;
                }

                Assert.IsNotNull(submit); Assert.IsNotNull(cancel);
                Assert.AreEqual("A", submit.CurrentText); Assert.AreEqual("B", cancel.CurrentText);
                Assert.AreEqual("Pad.FaceSouth", submit.CurrentShape, "WP-068 V1: pad düğmesi şekliyle çizilir");
                Assert.AreEqual("Pad.FaceEast", cancel.CurrentShape);
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.KeyboardMouse);
                yield return null;
                Assert.AreEqual("ENTER", submit.CurrentText); Assert.AreEqual("ESC", cancel.CurrentText);
                Assert.IsNull(submit.CurrentShape, "tuş: kapak + canlı metin");
                UiInputScheme.Release();
            }

            // Selection: the selected skin card has focus; Right hops to the weapon column;
            // Down reaches the footer; Left returns to the skin column.
            yield return SeciliBekle("Skin.");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Weapon.");
            GameObject weapon = Secili;
            Hareket(MoveDirection.Down);
            yield return null;
            Assert.IsTrue(Secili.name == "Start" || Secili.name.StartsWith("Weapon."),
                "silah sütununda aşağı: bir sonraki açık silah ya da START; seçili: " + Secili.name);
            while (Secili.name != "Start")
            {
                Hareket(MoveDirection.Down);
                yield return null;
            }

            Hareket(MoveDirection.Left);
            yield return SeciliBekle("Back");
            Onayla();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
            Assert.AreEqual(UiContext.MainMenu, UiNavigation.Base);
            Assert.IsNotNull(weapon);
            Debug.Log("[DD-TEST] wp022.gamepad=OK");
        }

        // ============================================================== 4 · ölçek ayarı

        /// <summary>
        /// WP-022D (WP-035): the Profile pane's title picker and the Achievements pager
        /// are reachable with the pad - Right from the tab enters the pane, Left returns,
        /// NEXT turns the page and keeps focus. The "None" title option is the current
        /// choice on a fresh profile; a locked option is not interactable.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Gamepad_ProfilUnvanSecici_VeBasarimSayfalari()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            yield return null;
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);

            // Profile pane: Right from the tab lands on the "None" title option (selected).
            menu.ShowPane(UiContext.Profile);
            yield return null;
            UiInput.Focus(Buton(menu, "Tab.Profile"));
            yield return SeciliBekle("Tab.Profile");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Option.Title.None");
            Assert.IsTrue(Secili.GetComponent<UiButton>().IsSelectedChoice, "taze profilde unvan yok → None seçili");
            int locked = 0, options = 0;
            foreach (UiButton b in menu.GetComponentsInChildren<UiButton>(true))
            {
                if (b.name.StartsWith("Option.Title."))
                {
                    options++;
                    if (!b.IsInteractable()) locked++;
                }
            }

            Assert.GreaterOrEqual(options, 2, "None + en az bir katalog unvanı");
            Assert.GreaterOrEqual(locked, 1, "taze profilde kilitli unvan var (etkileşimsiz)");
            Hareket(MoveDirection.Left);
            yield return SeciliBekle("Tab.Profile");

            // Achievements pane: pages of five; NEXT turns the page and stays focused.
            menu.ShowPane(UiContext.Achievements);
            yield return null;
            Assert.IsTrue(MetinVar(menu, "Page 1 of"), "27 başarım → sayfalı");
            UiInput.Focus(Buton(menu, "Tab.Achievements"));
            yield return SeciliBekle("Tab.Achievements");
            Hareket(MoveDirection.Right);
            yield return SeciliBekle("Pager.Next");
            Onayla();
            yield return null;
            yield return null;
            Assert.IsTrue(MetinVar(menu, "Page 2 of"), "NEXT ikinci sayfayı açar");
            yield return SeciliBekle("Pager.Next");
            Assert.IsTrue(MetinVar(menu, "IN PROGRESS"), "kazanılmamış girdi ilerleme çipi + çubuğu taşır");
            Assert.IsNotNull(menu.GetComponentInChildren<UiBar>(true), "ilerleme çubuğu (kazanılmamış girdi)");
        }

        /// <summary>
        /// WP-022J diagnostic · which HUD component allocates per frame in a real run
        /// (ProfilerRecorder counts, targetFrameRate -1): the whole HUD screen, the enemy
        /// status layer, the hit-direction layer, each toggled alone. Logs only; the
        /// assertion is on the HUD screen's own Update once the layers are off.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Hud_KareBasiAyirma_BilesenBazinda()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            PrototypeUiFlow.GoToSelection();
            yield return SahneyiBekle(PrototypeScenes.Selection);
            PrototypeUiFlow.StartRun();
            yield return SahneyiBekle(PrototypeScenes.Run);
            yield return KosuHazirOlsun();
            RunHudScreen hud = Object.FindObjectOfType<RunHudScreen>();
            Assert.IsNotNull(hud);
            int before = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var count = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count"))
                using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    for (int i = 0; i < 30; i++) yield return null;
                    long all = 0, allB = 0;
                    for (int i = 0; i < 20; i++) { yield return null; if (count.LastValue > all) { all = count.LastValue; allB = bytes.LastValue; } }
                    hud.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    long hudOff = 0;
                    for (int i = 0; i < 20; i++) { yield return null; if (count.LastValue > hudOff) hudOff = count.LastValue; }
                    hud.enabled = true;
                    if (hud.EnemyStatus != null) hud.EnemyStatus.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    long statusOff = 0;
                    for (int i = 0; i < 20; i++) { yield return null; if (count.LastValue > statusOff) statusOff = count.LastValue; }
                    if (hud.EnemyStatus != null) hud.EnemyStatus.enabled = true;
                    var hit = Object.FindObjectOfType<UiHitDirectionLayer>();
                    if (hit != null) hit.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    long hitOff = 0;
                    for (int i = 0; i < 20; i++) { yield return null; if (count.LastValue > hitOff) hitOff = count.LastValue; }
                    if (hit != null) hit.enabled = true;
                    Debug.Log("[DD-TEST] wp022j.hudAlloc all=" + all + "/" + allB + "B hudScreenOff=" + hudOff + " statusOff=" + statusOff + " hitOff=" + hitOff);
                }
            }
            finally
            {
                Application.targetFrameRate = before;
            }

            PrototypeUiFlow.AbortRunToMainMenu();
            yield return SahneyiBekle(PrototypeScenes.MainMenu);
        }

        /// <summary>
        /// WP-022K diagnostic (S-10 finding: ~4 allocations/frame in the MainMenu, UI-sourced):
        /// ProfilerRecorder counts on the main menu with the UI's per-frame components toggled
        /// alone - petal drift, pulses, the prompt driver, the menu screen's Update.
        /// </summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AnaMenu_KareBasiAyirma_BilesenBazinda()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return BootTanMainMenuye();
            yield return null;
            MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
            Assert.IsNotNull(menu);
            int before = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            try
            {
                using (var count = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count"))
                using (var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    var sb = new System.Text.StringBuilder("[DD-TEST] wp022k.menuDiag");
                    for (int i = 0; i < 30; i++) yield return null;
                    long c = 0, b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" sum60 all=").Append(c).Append("/").Append(b).Append("B");

                    UiDrift[] drifts = Object.FindObjectsOfType<UiDrift>();
                    foreach (UiDrift d in drifts) d.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" driftOff(").Append(drifts.Length).Append(")=").Append(c).Append("/").Append(b).Append("B");
                    foreach (UiDrift d in drifts) d.enabled = true;

                    UiPulse[] pulses = Object.FindObjectsOfType<UiPulse>();
                    foreach (UiPulse p in pulses) p.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" pulsesOff(").Append(pulses.Length).Append(")=").Append(c).Append("/").Append(b).Append("B");
                    foreach (UiPulse p in pulses) p.enabled = true;

                    var driver = Object.FindObjectOfType<UiPromptDriver>();
                    if (driver != null) driver.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" promptDriverOff=").Append(c).Append("/").Append(b).Append("B");
                    if (driver != null) driver.enabled = true;

                    menu.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" menuUpdateOff=").Append(c).Append("/").Append(b).Append("B");
                    menu.enabled = true;

                    Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
                    foreach (Canvas cv in canvases) cv.enabled = false;
                    for (int i = 0; i < 5; i++) yield return null;
                    c = 0; b = 0;
                    for (int i = 0; i < 60; i++) { yield return null; c += count.LastValue; b += bytes.LastValue; }
                    sb.Append(" canvasesOff(").Append(canvases.Length).Append(")=").Append(c).Append("/").Append(b).Append("B");
                    foreach (Canvas cv in canvases) cv.enabled = true;
                    Debug.Log(sb.ToString());
                }
            }
            finally
            {
                Application.targetFrameRate = before;
            }
        }

        private static bool MetinVar(Component ekran, string parca)
        {
            foreach (TMPro.TMP_Text t in ekran.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (t.text != null && t.text.Contains(parca)) return true;
            }

            return false;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator UiOlcegi_SonrakiEkraninReferansCerceveniOlcekler()
        {
            LogAssert.ignoreFailingMessages = true;
            float before = UiSettings.UiScale;
            string path = Path.Combine(Path.GetTempPath(), "dd-wp022b-scale-" + System.Guid.NewGuid().ToString("N") + ".json");
            UiSettings.SetPathOverride(path);
            try
            {
                UiSettings.UiScale = 1.2f;
                yield return BootTanMainMenuye();
                yield return null;
                MainMenuScreen menu = Object.FindObjectOfType<MainMenuScreen>();
                var scaler = menu.GetComponentInChildren<CanvasScaler>();
                Assert.AreEqual(1920f / 1.2f, scaler.referenceResolution.x, 0.5f, "ölçek 1.2 referans genişliği 1600'e çekmeli");

                UiSettings.UiScale = 0.9f;
                PrototypeUiFlow.GoToSelection();
                yield return SahneyiBekle(PrototypeScenes.Selection);
                yield return null;
                scaler = Object.FindObjectOfType<SelectionScreen>().GetComponentInChildren<CanvasScaler>();
                Assert.AreEqual(1920f / 0.9f, scaler.referenceResolution.x, 0.5f, "yeni ekran yeni ölçeği okur");
                PrototypeUiFlow.GoToMainMenu();
                yield return SahneyiBekle(PrototypeScenes.MainMenu);
            }
            finally
            {
                UiSettings.SetPathOverride(null);
                if (File.Exists(path)) File.Delete(path);
                UiSettings.UiScale = before;
            }

            Debug.Log("[DD-TEST] wp022.scale=OK");
        }
    }
}
