using System.Collections.Generic;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-068 slice 12b · run HUD lane: the geometry rules that need no scene. The live HUD half of each item sits in the
    /// PlayMode suites (EnemyStatusStampsTests, UiArtSliceTests, CameraModesHudTests).
    /// </summary>
    public sealed class RunHud12bTests
    {
        private static readonly (string Name, float W, float H, float Tall)[] Canvases =
        {
            ("1080", 1920f, 1080f, 1f), ("21:9", 2560f, 1080f, 1f), ("mac", 1600f, 1039f, 1.25f)
        };

        /// <summary>
        /// B1 (AD 8c, new for 12b) + B2 (AD R1-B SHOULD-4): a stamp over any head point on the canvas never meets an ink
        /// cluster + 12 px or the player figure + 24 px, and it keeps its body's column (x unchanged). Top-down and third
        /// person carry a figure, first person does not; the Special block counts as a cluster while it shows.
        /// </summary>
        [Test]
        public void DurumDamgasi_KumelereVeFigureGirmez_SutunuKorur()
        {
            var failures = new List<string>();
            foreach (var c in Canvases)
            {
                float hw = c.W * 0.5f, hh = c.H * 0.5f;
                foreach (string variant in new[] { "td", "td-special", "tp", "tp-special", "fp", "fp-special" })
                {
                    var rects = new List<Rect>
                    {
                        Rect.MinMaxRect(-hw + 32f, hh - 32f - 36f, -hw + 32f + 170f, hh - 32f),                  // gold
                        Rect.MinMaxRect(-120f, hh - 24f - 56f, 120f, hh - 24f),                                    // seal
                        Rect.MinMaxRect(-hw + 32f, -hh + 32f, -hw + 32f + 380f, -hh + 32f + 110f * c.Tall),       // health
                        variant.StartsWith("fp")
                            ? Rect.MinMaxRect(-hw + 32f, -hh + RunHudScreen.FpWeaponClusterBottom, -hw + 432f, -hh + RunHudScreen.FpWeaponClusterBottom + 140f * c.Tall)
                            : Rect.MinMaxRect(hw - 432f, -hh + 32f, hw - 32f, -hh + 32f + 140f * c.Tall),         // weapon
                    };
                    if (variant.EndsWith("special"))
                    {
                        rects.Add(Rect.MinMaxRect(-110f, -hh + 24f, 110f, -hh + 24f + 260f));                   // Special block
                    }

                    bool hasFigure = !variant.StartsWith("fp");
                    Rect figure = variant.StartsWith("td")
                        ? Rect.MinMaxRect(-30f * c.Tall, -45f * c.Tall, 30f * c.Tall, 45f * c.Tall)             // top-down: the figure at the centre
                        : Rect.MinMaxRect(-hw * 0.30f, -hh - 40f, -hw * 0.08f, 60f);                             // third person: over the shoulder, cut by the bottom edge
                    Rect[] avoid = rects.ToArray();
                    foreach (Vector2 chip in new[] { new Vector2(110f, 28f), new Vector2(140f, 36f) })
                    {
                        for (float x = -hw; x <= hw; x += 24f)
                        {
                            for (float y = -hh; y <= hh; y += 24f)
                            {
                                Vector2 at = UiEnemyStatusMarkers.Resolve(new Vector2(x, y), chip, avoid, avoid.Length, hasFigure, figure);
                                if (at.x != x)
                                {
                                    failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): x moved to " + at.x);
                                }

                                var rect = new Rect(at.x - chip.x * 0.5f, at.y, chip.x, chip.y);
                                for (int i = 0; i < avoid.Length; i++)
                                {
                                    if (rect.Overlaps(Grown(avoid[i], UiEnemyStatusMarkers.ClusterClearance - 0.5f)))
                                    {
                                        failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): stamp " + rect + " meets cluster " + i);
                                    }
                                }

                                if (hasFigure && rect.Overlaps(Grown(figure, UiEnemyStatusMarkers.FigureClearance - 0.5f)))
                                {
                                    failures.Add(c.Name + "/" + variant + " (" + x + ", " + y + "): stamp " + rect + " meets the figure");
                                }
                            }
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, failures.Count + " failures:\n" + string.Join("\n", failures.GetRange(0, Mathf.Min(20, failures.Count))));

            // The Mac case AD R1-B SHOULD-4 measured (SHOCK across the figure): the stamp lifts to the band above it, x unchanged.
            var body = Rect.MinMaxRect(-30f, -45f, 30f, 46f);
            Vector2 lifted = UiEnemyStatusMarkers.Resolve(new Vector2(5f, 30f), new Vector2(174f, 33f), new Rect[0], 0, true, body);
            Assert.AreEqual(5f, lifted.x, 0.001f, "x stays over the body");
            Assert.AreEqual(body.yMax + UiEnemyStatusMarkers.FigureClearance, lifted.y, 0.001f, "the band above the figure + 24");

            // A stamp with nothing in the way stands where its body puts it.
            var free = new Vector2(400f, 120f);
            Assert.AreEqual(free, UiEnemyStatusMarkers.Resolve(free, new Vector2(110f, 28f), new Rect[0], 0, true, body), "no drift");
        }

        private static Rect Grown(Rect r, float by)
        {
            return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        }
    }
}
