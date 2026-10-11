using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using DD.Art.Pipeline.EditorTools;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Weapons;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §6 EditMode 1–4) · the AP-040 v001 EK1 weapon-attachment sockets.
    ///   1. DD_WeaponAttach = the copied sockets.json (10 rows, sha256, position ≤ 0.1 mm, angle ≤ 0.1°, the prefab slots, the bridges);
    ///   2. the table sits in the AP-021 model-root frame (frame lock: Muzzle 10/10, marker_mag = Mag on the 7 weapons with part_mag,
    ///      the ejection port never on −X);
    ///   3. the derived muzzles follow the plan's formula (60/60 ≤ 1 mm);
    ///   4. the E1–E6 prefabs sit in the card frame (LOD0 bounds = card [x, z, −y] ≤ 2 mm, E4 along +X, E2 / E6 hang along −Y).
    /// 1 and 4 need the built assets — the builder cannot run before S-02's shared AP-040 materials land — so they are ignored while the
    /// assets are absent; 2 and 3 read only the copied json and the AP-021 models.
    /// The table is parsed here with ArtJson (not the builder's JsonUtility DTOs), so a builder parse slip cannot hide in test 1.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4AttachSocketTests
    {
        private const float TabloKonumTol = 0.0001f;   // 0.1 mm
        private const float TabloAciTol = 0.1f;         // degrees
        private const float ModelTol = 0.001f;          // 1 mm
        private const float SinirTol = 0.002f;          // 2 mm

        /// <summary>CARD_double_barrel SOCKET_SecondaryMuzzle (Blender [0, −len, −drop] → Unity [x, z, −y]) per E3 size.</summary>
        private static readonly Vector3[] E3IkincilNamlu = { new Vector3(0f, -0.0995f, 0.15f), new Vector3(0f, -0.1045f, 0.22f), new Vector3(0f, -0.112f, 0.31f) };

        /// <summary>CARD_muzzle_power_ring SOCKET_PowerMuzzle length (Blender −Y → Unity +Z) per E5 variant.</summary>
        private static readonly float[] E5GucBoyu = { 0.032f, 0.047f };

        /// <summary>The plan's E5 seat offset (0, 0, .003).</summary>
        private static readonly Vector3 E5Oturma = new Vector3(0f, 0f, 0.003f);

        // ------------------------------------------------------------------ the copied table (ArtJson)

        private sealed class Kombinasyon
        {
            public int Sira, E3, E5;
            public Vector3 E3Ofs, E5Ofs, Ikincil, Guc;
            public float E5Olcek;
            public bool KopruGerekli;
            public float[] Raylar;
            public float Genislik, ArkaY, BasZ, SonZ;
        }

        private sealed class Silah
        {
            public string Anahtar, KaynakSha;
            public Vector3 Sarjor, Namlu, Yan;
            public Quaternion SarjorDonus, NamluDonus, YanDonus;
            public readonly List<Kombinasyon> Kombinasyonlar = new List<Kombinasyon>();
        }

        private static List<Silah> Tablo(out string sha)
        {
            string yol = ArtAp040WeaponAttachBuilder.SocketsJson;
            Assert.IsTrue(File.Exists(yol), "Soket tablosu yok: " + yol + " (AP-040 v001 weapon_attach/sockets.json kopyası)");
            sha = Sha256(yol);
            Dictionary<string, object> kok = ArtJson.AsObject(ArtJson.Parse(File.ReadAllText(yol)), "sockets.json");
            Assert.AreEqual(WeaponAttachLibrary.Schema, ArtJson.AsString(ArtJson.Require(kok, "schema")), "sockets.json şeması kütüphaneninki değil");
            var silahlar = new List<Silah>();
            foreach (object o in ArtJson.AsList(ArtJson.Require(kok, "weapons"), "weapons"))
            {
                Dictionary<string, object> w = ArtJson.AsObject(o, "weapon");
                var s = new Silah
                {
                    Anahtar = ArtJson.AsString(ArtJson.Require(w, "weapon_id")),
                    KaynakSha = ArtJson.Get(w, "source_sha256") as string,
                };
                Dictionary<string, object> mag = ArtJson.AsObject(ArtJson.Require(w, "SOCKET_Mag"), s.Anahtar + " SOCKET_Mag");
                Dictionary<string, object> muz = ArtJson.AsObject(ArtJson.Require(w, "SOCKET_Muzzle"), s.Anahtar + " SOCKET_Muzzle");
                Dictionary<string, object> yan = ArtJson.AsObject(ArtJson.Require(w, "SOCKET_Side"), s.Anahtar + " SOCKET_Side");
                s.Sarjor = Konum(mag);
                s.Namlu = Konum(muz);
                s.Yan = Konum(yan);
                s.SarjorDonus = Donus(mag);
                s.NamluDonus = Donus(muz);
                s.YanDonus = Donus(yan);
                int sira = 0;
                foreach (object co in ArtJson.AsList(ArtJson.Require(w, "combinations"), s.Anahtar + " combinations"))
                {
                    Dictionary<string, object> c = ArtJson.AsObject(co, "combination");
                    Dictionary<string, object> a = ArtJson.AsObject(ArtJson.Require(c, "adapter"), "adapter");
                    var raylar = new List<float>();
                    foreach (object r in ArtJson.AsList(ArtJson.Require(a, "rails_x_m"), "rails_x_m")) raylar.Add(ArtJson.AsFloat(r));
                    s.Kombinasyonlar.Add(new Kombinasyon
                    {
                        Sira = sira++,
                        E3 = ArtJson.AsInt(ArtJson.Require(c, "E3_variant")),
                        E5 = ArtJson.AsInt(ArtJson.Require(c, "E5_variant")),
                        E3Ofs = ArtJson.ToVector3(ArtJson.Require(c, "E3_offset_unity_m")),
                        E5Ofs = ArtJson.ToVector3(ArtJson.Require(c, "E5_offset_unity_m")),
                        Ikincil = Konum(ArtJson.AsObject(ArtJson.Require(c, "SOCKET_SecondaryMuzzle"))),
                        Guc = Konum(ArtJson.AsObject(ArtJson.Require(c, "SOCKET_PowerMuzzle"))),
                        E5Olcek = ArtJson.AsFloat(ArtJson.Require(c, "E5_uniformScale")),
                        KopruGerekli = ArtJson.AsBool(ArtJson.Require(a, "required")),
                        Raylar = raylar.ToArray(),
                        Genislik = ArtJson.AsFloat(ArtJson.Require(a, "width_m")),
                        ArkaY = ArtJson.AsFloat(ArtJson.Require(a, "rear_y_m")),
                        BasZ = ArtJson.AsFloat(ArtJson.Require(a, "from_z_m")),
                        SonZ = ArtJson.AsFloat(ArtJson.Require(a, "to_z_m")),
                    });
                }

                silahlar.Add(s);
            }

            return silahlar;
        }

        private static Vector3 Konum(Dictionary<string, object> soket) => ArtJson.ToVector3(ArtJson.Require(soket, "position_unity_m"), "position_unity_m");

        /// <summary>The table quaternion when given (Mag), else Euler(rotation_unity_deg).</summary>
        private static Quaternion Donus(Dictionary<string, object> soket)
        {
            object q = ArtJson.Get(soket, "rotation_unity_quaternion_xyzw");
            if (q != null) return ArtJson.ToQuaternion(q, "rotation_unity_quaternion_xyzw").normalized;
            return Quaternion.Euler(ArtJson.ToVector3(ArtJson.Require(soket, "rotation_unity_deg"), "rotation_unity_deg"));
        }

        private static string Sha256(string yol)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream f = File.OpenRead(yol))
            {
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string F(Vector3 v) => "(" + v.x.ToString("0.0000", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                                              v.z.ToString("0.0000", CultureInfo.InvariantCulture) + ")";

        private static ClassicWeaponId Kimlik(string anahtar)
        {
            Assert.IsTrue(WeaponIdMigration.TryGetRuntimeId(anahtar, out ClassicWeaponId id), anahtar + ": klasik v3 anahtarı değil");
            return id;
        }

        private static GameObject Ap021Modeli(ClassicWeaponId id, string anahtar)
        {
            GameObject model = ArtAp021WeaponBuilder.ModelFor(id);
            Assert.IsNotNull(model, anahtar + ": AP-021 modeli yok (" + ArtAp021WeaponBuilder.ModelPath(anahtar) + ")");
            return model;
        }

        // ------------------------------------------------------------------ 1

        [Test]
        public void Tablo_SocketsJson_IleBirebir()
        {
            List<Silah> tablo = Tablo(out string sha);
            var lib = AssetDatabase.LoadAssetAtPath<WeaponAttachLibrary>(ArtAp040WeaponAttachBuilder.LibraryPath);
            if (lib == null)
            {
                Assert.Ignore("DD_WeaponAttach henüz kurulmadı (" + ArtAp040WeaponAttachBuilder.LibraryPath + "): ArtAp040WeaponAttachBuilder.Build S-02'nin ortak AP-040 malzemelerini bekliyor (" +
                              ArtAp040WeaponAttachBuilder.SharedMaterialFolder + ")");
            }

            Assert.AreSame(lib, Resources.Load<WeaponAttachLibrary>(WeaponAttachLibrary.ResourcePath), "Kütüphane Resources/" + WeaponAttachLibrary.ResourcePath + " yolundan yüklenmiyor");
            Assert.AreEqual(sha, lib.sourceSha256, "Kütüphanenin sourceSha256'sı kopyalanan sockets.json'ınki değil (tablo değişti, builder yeniden koşmalı)");
            Assert.AreEqual(10, tablo.Count, "sockets.json 10 klasik silah taşımalı");
            Assert.AreEqual(10, lib.rows.Length, "Kütüphane 10 satır taşımalı");

            int pozlar = 0, kombinasyonlar = 0, kopruler = 0;
            foreach (Silah s in tablo)
            {
                ClassicWeaponId id = Kimlik(s.Anahtar);
                WeaponAttachLibrary.Row row = lib.Find(id);
                Assert.IsNotNull(row, s.Anahtar + ": kütüphane satırı yok");
                Assert.AreEqual(s.Anahtar, row.key, id + ": satır anahtarı");
                Poz(s.Anahtar + " Mag", row.mag, s.Sarjor, s.SarjorDonus);
                Poz(s.Anahtar + " Muzzle", row.muzzle, s.Namlu, s.NamluDonus);
                Poz(s.Anahtar + " Side", row.side, s.Yan, s.YanDonus);
                pozlar += 3;

                bool parca = WeaponSocketContract.Find(Ap021Modeli(id, s.Anahtar).transform, WeaponMagazineVisual.PartName) != null;
                Assert.AreEqual(parca, row.magOnPart, s.Anahtar + ": magOnPart AP-021 modelinin part_mag'iyle aynı değil");

                Assert.IsNotNull(row.combos, s.Anahtar + ": kombinasyon dizisi yok");
                Assert.AreEqual(6, row.combos.Length, s.Anahtar + ": 6 E3×E5 kombinasyonu bekleniyor");
                Assert.AreEqual(6, s.Kombinasyonlar.Count, s.Anahtar + ": sockets.json 6 kombinasyon taşımalı");
                foreach (Kombinasyon k in s.Kombinasyonlar)
                {
                    string at = s.Anahtar + " E3 v" + k.E3 + " / E5 v" + k.E5;
                    AttachCombo c = row.combos[WeaponAttachLibrary.ComboIndex(k.E3, k.E5)];
                    Assert.AreEqual(k.E3, c.e3, at + ": e3");
                    Assert.AreEqual(k.E5, c.e5, at + ": e5");
                    Yakin(at + " e3Offset", c.e3Offset, k.E3Ofs, TabloKonumTol);
                    Yakin(at + " e5Offset", c.e5Offset, k.E5Ofs, TabloKonumTol);
                    Yakin(at + " secondaryMuzzle", c.secondaryMuzzle, k.Ikincil, TabloKonumTol);
                    Yakin(at + " powerMuzzle", c.powerMuzzle, k.Guc, TabloKonumTol);
                    Assert.AreEqual(k.E5Olcek, c.e5Scale, 1e-5f, at + ": e5Scale");
                    if (k.KopruGerekli)
                    {
                        Assert.IsNotNull(c.bridge, at + ": köprü gerekli (adapter.required) ama mesh yok");
                        Kopru(at, c.bridge, k);
                        kopruler++;
                    }
                    else
                    {
                        Assert.IsNull(c.bridge, at + ": köprü gerekmiyor ama mesh var");
                    }

                    kombinasyonlar++;
                }
            }

            Assert.AreEqual(60, kombinasyonlar, "60 kombinasyon karşılaştırılmalı");
            foreach (ArtAp040WeaponAttachBuilder.Variant v in ArtAp040WeaponAttachBuilder.Variants)
            {
                GameObject slot = ArtAp040WeaponAttachBuilder.SlotOf(lib, v);
                Assert.IsNotNull(slot, v.Family + "[" + v.Index + "]: kütüphane yuvası boş (" + v.Name + ")");
                Assert.AreEqual(ArtAp040WeaponAttachBuilder.PrefabPath(v.Name), AssetDatabase.GetAssetPath(slot), v.Family + "[" + v.Index + "]: yanlış prefab");
            }

            Debug.Log("[DD-TEST] wp104b4.table rows=" + lib.rows.Length + " poses=" + pozlar + " combos=" + kombinasyonlar + " bridges=" + kopruler + " sha=" + sha);
        }

        private static void Poz(string at, AttachPose poz, Vector3 konum, Quaternion donus)
        {
            Yakin(at + " konum", poz.position, konum, TabloKonumTol);
            float aci = Quaternion.Angle(poz.rotation, donus);
            Assert.LessOrEqual(aci, TabloAciTol, at + ": dönüş tablodan " + aci.ToString("0.000", CultureInfo.InvariantCulture) + "° sapıyor");
        }

        private static void Yakin(string at, Vector3 gercek, Vector3 beklenen, float tol)
        {
            float d = Vector3.Distance(gercek, beklenen);
            Assert.LessOrEqual(d, tol, at + ": " + F(gercek) + " ≠ " + F(beklenen) + " (" + (d * 1000f).ToString("0.000", CultureInfo.InvariantCulture) + " mm)");
        }

        /// <summary>The bridge mesh in SOCKET_Muzzle's frame: one width × width rail per rails_x, at z = −rear_y, spanning y from_z..to_z.</summary>
        private static void Kopru(string at, Mesh kopru, Kombinasyon k)
        {
            float yarim = k.Genislik * 0.5f;
            float xMin = float.MaxValue, xMax = float.MinValue;
            foreach (float x in k.Raylar)
            {
                xMin = Mathf.Min(xMin, x - yarim);
                xMax = Mathf.Max(xMax, x + yarim);
            }

            var min = new Vector3(xMin, Mathf.Min(k.BasZ, k.SonZ), -k.ArkaY - yarim);
            var max = new Vector3(xMax, Mathf.Max(k.BasZ, k.SonZ), -k.ArkaY + yarim);
            Bounds b = kopru.bounds;
            Yakin(at + " köprü min", b.min, min, TabloKonumTol);
            Yakin(at + " köprü max", b.max, max, TabloKonumTol);
        }

        // ------------------------------------------------------------------ 2

        [Test]
        public void Model_UcSoket_TabloDonusumunde()
        {
            List<Silah> tablo = Tablo(out _);
            Assert.AreEqual(10, tablo.Count, "sockets.json 10 klasik silah taşımalı");
            int namlu = 0, parcali = 0, sarjor = 0, sagPort = 0, ayniKaynak = 0;
            foreach (Silah s in tablo)
            {
                ClassicWeaponId id = Kimlik(s.Anahtar);
                Transform kok = Ap021Modeli(id, s.Anahtar).transform;

                Transform muzzle = WeaponSocketContract.Find(kok, WeaponSocketContract.Muzzle);
                Assert.IsNotNull(muzzle, s.Anahtar + ": AP-021 modelinde Muzzle yok");
                Yakin(s.Anahtar + " Muzzle (model ↔ tablo)", kok.InverseTransformPoint(muzzle.position), s.Namlu, ModelTol);
                namlu++;

                // The Mag table is the marker_mag seat where the model carries part_mag (7/10); sg_1 / sg_2 / rl_1 get a new seat (plan §2).
                if (WeaponSocketContract.Find(kok, WeaponMagazineVisual.PartName) != null)
                {
                    parcali++;
                    Transform marker = WeaponSocketContract.Find(kok, WeaponSocketContract.MarkerMag);
                    Assert.IsNotNull(marker, s.Anahtar + ": part_mag var ama marker_mag yok");
                    Yakin(s.Anahtar + " marker_mag (model ↔ tablo Mag)", kok.InverseTransformPoint(marker.position), s.Sarjor, ModelTol);
                    sarjor++;
                }

                // A mirrored frame puts the port on −X. sg_2's break action ejects on the centre line (Ap021ClassicModelGateTests), so
                // every port is ≥ −1 mm and at least 9 of 10 sit clearly on +X.
                Transform port = WeaponSocketContract.Find(kok, WeaponSocketContract.Ejection);
                Assert.IsNotNull(port, s.Anahtar + ": AP-021 modelinde Ejection yok");
                Vector3 p = kok.InverseTransformPoint(port.position);
                Assert.GreaterOrEqual(p.x, -1e-3f, s.Anahtar + ": atım portu −X'te (aynalı çerçeve) — " + F(p));
                if (p.x > 0.01f) sagPort++;

                // The side socket is on the right of the barrel axis (+X) in the same frame.
                Assert.Greater(s.Yan.x, 0.01f, s.Anahtar + ": SOCKET_Side sağda (+X) olmalı — " + F(s.Yan));

                string fbx = ArtAp021WeaponBuilder.ModelPath(s.Anahtar);
                if (!string.IsNullOrEmpty(s.KaynakSha) && File.Exists(fbx) && Sha256(fbx) == s.KaynakSha) ayniKaynak++;
            }

            Assert.AreEqual(10, namlu, "Muzzle 10/10 tabloyla birebir olmalı");
            Assert.AreEqual(7, parcali, "Plan §2: 7/10 silah part_mag taşır (sg_1, sg_2, rl_1 hariç)");
            Assert.AreEqual(parcali, sarjor, "marker_mag = Mag, part_mag'li her silahta");
            Assert.GreaterOrEqual(sagPort, 9, "Atım portu en az 9/10 silahta +X'te olmalı");
            Debug.Log("[DD-TEST] wp104b4.frame muzzle=" + namlu + "/10 mag=" + sarjor + "/" + parcali + " portPlusX=" + sagPort + " sourceShaSame=" + ayniKaynak + "/10");
        }

        // ------------------------------------------------------------------ 3

        [Test]
        public void Kombinasyon_TuretilmisNamlular()
        {
            List<Silah> tablo = Tablo(out _);
            Assert.AreEqual(10, tablo.Count, "sockets.json 10 klasik silah taşımalı");
            int ikincil = 0, guc = 0, kopru = 0;
            foreach (Silah s in tablo)
            {
                Assert.AreEqual(6, s.Kombinasyonlar.Count, s.Anahtar + ": 6 kombinasyon bekleniyor");
                foreach (Kombinasyon k in s.Kombinasyonlar)
                {
                    string at = s.Anahtar + " E3 v" + k.E3 + " / E5 v" + k.E5;
                    Assert.IsTrue(k.E3 >= 1 && k.E3 <= 3 && k.E5 >= 1 && k.E5 <= 2, at + ": varyant aralık dışı");
                    Assert.AreEqual(k.Sira, WeaponAttachLibrary.ComboIndex(k.E3, k.E5), at + ": sockets.json sırası ComboIndex ile aynı değil");
                    // E3 only drops below the barrel (the measured fit offset).
                    Assert.IsTrue(Mathf.Abs(k.E3Ofs.x) < 1e-6f && Mathf.Abs(k.E3Ofs.z) < 1e-6f && k.E3Ofs.y <= 0f, at + ": E3 ofseti yalnız −Y düşüşü olmalı — " + F(k.E3Ofs));

                    // Plan §2: Secondary = Muzzle + E3ofs + (0, −.0995|−.1045|−.112, .15|.22|.31).
                    Yakin(at + " SecondaryMuzzle", k.Ikincil, s.Namlu + k.E3Ofs + E3IkincilNamlu[k.E3 - 1], ModelTol);
                    ikincil++;

                    // Plan §2: Power = Muzzle + (0, 0, .003) + s5·(0, 0, .032|.047).
                    Yakin(at + " PowerMuzzle", k.Guc, s.Namlu + E5Oturma + k.E5Olcek * new Vector3(0f, 0f, E5GucBoyu[k.E5 - 1]), ModelTol);
                    guc++;

                    // Plan §5 bridge: to_z = −.008 + the E3 drop, required once the drop is more than 1 mm (AP040_attach_fit.py).
                    Assert.AreEqual(-0.008f, k.BasZ, 1e-4f, at + ": köprü from_z");
                    Assert.AreEqual(-0.008f + k.E3Ofs.y, k.SonZ, 1e-4f, at + ": köprü to_z = −0,008 + E3 düşüşü");
                    Assert.AreEqual(k.E3Ofs.y < -0.001f, k.KopruGerekli, at + ": köprü gerekliliği E3 düşüşüyle uyuşmuyor");
                    if (k.KopruGerekli) kopru++;
                }
            }

            Assert.AreEqual(60, ikincil, "SecondaryMuzzle 60/60");
            Assert.AreEqual(60, guc, "PowerMuzzle 60/60");
            Debug.Log("[DD-TEST] wp104b4.combos secondary=" + ikincil + "/60 power=" + guc + "/60 bridgeCombos=" + kopru);
        }

        // ------------------------------------------------------------------ 4

        /// <summary>A CARD_*.json variant: Blender bounds (metres), the derived sockets (Blender), LOD0–2 triangles.</summary>
        private readonly struct Kart
        {
            public readonly string Ad;
            public readonly Vector3 Min, Max;
            public readonly (string ad, Vector3 blender)[] Soketler;
            public readonly int[] Ucgen;

            public Kart(string ad, Vector3 min, Vector3 max, int[] ucgen, params (string, Vector3)[] soketler)
            {
                Ad = ad;
                Min = min;
                Max = max;
                Ucgen = ucgen;
                Soketler = soketler;
            }
        }

        private static readonly Kart[] Kartlar =
        {
            new Kart("AP040_extended_magazine_V01", new Vector3(-0.0315f, -0.0465f, -0.2305f), new Vector3(0.0315f, 0.0455f, 0f), new[] { 272, 136, 62 }),
            new Kart("AP040_drum_magazine_V01", new Vector3(-0.0882f, -0.1326f, -0.3076f), new Vector3(0.0882f, 0.1326f, 0.0005f), new[] { 1396, 698, 348 }),
            new Kart("AP040_drum_magazine_V02", new Vector3(-0.1044f, -0.1632f, -0.3682f), new Vector3(0.1044f, 0.1632f, 0.0005f), new[] { 1396, 698, 348 }),
            new Kart("AP040_drum_magazine_V03", new Vector3(-0.1206f, -0.1938f, -0.4288f), new Vector3(0.1206f, 0.1938f, 0.0005f), new[] { 1396, 698, 338 }),
            new Kart("AP040_double_barrel_V01", new Vector3(-0.0225f, -0.15f, -0.122f), new Vector3(0.0225f, 0.065f, 0.00585f), new[] { 600, 300, 150 },
                ("SOCKET_SecondaryMuzzle", new Vector3(0f, -0.15f, -0.0995f))),
            new Kart("AP040_double_barrel_V02", new Vector3(-0.0275f, -0.22f, -0.132f), new Vector3(0.0275f, 0.065f, 0.00715f), new[] { 600, 300, 150 },
                ("SOCKET_SecondaryMuzzle", new Vector3(0f, -0.22f, -0.1045f))),
            new Kart("AP040_double_barrel_V03", new Vector3(-0.035f, -0.31f, -0.147f), new Vector3(0.035f, 0.065f, 0.0091f), new[] { 600, 300, 150 },
                ("SOCKET_SecondaryMuzzle", new Vector3(0f, -0.31f, -0.112f))),
            new Kart("AP040_element_capsule_V01", new Vector3(0f, -0.035f, -0.046f), new Vector3(0.127f, 0.035f, 0.0875f), new[] { 970, 484, 242 },
                ("SOCKET_ElementVFX", new Vector3(0.09f, 0f, 0.02f))),
            new Kart("AP040_element_capsule_V02", new Vector3(0f, -0.034f, -0.0375f), new Vector3(0.126f, 0.034f, 0.101f), new[] { 220, 110, 54 },
                ("SOCKET_ElementVFX", new Vector3(0.09f, 0f, 0.02f))),
            new Kart("AP040_element_capsule_V03", new Vector3(0f, -0.034f, -0.052f), new Vector3(0.1234f, 0.034f, 0.083f), new[] { 912, 456, 227 },
                ("SOCKET_ElementVFX", new Vector3(0.09f, 0f, 0.02f))),
            new Kart("AP040_element_capsule_V04", new Vector3(0f, -0.035f, -0.046f), new Vector3(0.127f, 0.035f, 0.0875f), new[] { 1096, 548, 274 },
                ("SOCKET_ElementVFX", new Vector3(0.09f, 0f, 0.02f))),
            new Kart("AP040_muzzle_power_ring_V01", new Vector3(-0.04427f, -0.038f, -0.04343f), new Vector3(0.04427f, 0f, 0.04343f), new[] { 816, 408, 204 },
                ("SOCKET_PowerMuzzle", new Vector3(0f, -0.032f, 0f))),
            new Kart("AP040_muzzle_power_ring_V02", new Vector3(-0.05457f, -0.053f, -0.05353f), new Vector3(0.05457f, 0f, 0.05353f), new[] { 816, 408, 204 },
                ("SOCKET_PowerMuzzle", new Vector3(0f, -0.047f, 0f))),
            new Kart("AP040_rocket_full_auto_box_V01", new Vector3(-0.184f, -0.2195f, -0.3875f), new Vector3(0.184f, 0.07f, 0f), new[] { 1388, 694, 346 },
                ("SOCKET_Rocket_1", new Vector3(-0.087f, -0.22f, -0.157f)), ("SOCKET_Rocket_2", new Vector3(0.087f, -0.22f, -0.157f)),
                ("SOCKET_Rocket_3", new Vector3(-0.087f, -0.22f, -0.292f)), ("SOCKET_Rocket_4", new Vector3(0.087f, -0.22f, -0.292f))),
        };

        /// <summary>Blender [x, y, z] → Unity [x, z, −y] (AP040-integration-v001.json coordinates.conversion).</summary>
        private static Vector3 Unity(Vector3 blender) => new Vector3(blender.x, blender.z, -blender.y);

        [Test]
        public void EkPrefab_Cercevesi()
        {
            Assert.AreEqual(ArtAp040WeaponAttachBuilder.Variants.Length, Kartlar.Length, "Kart tablosu builder'ın varyant listesiyle aynı boyda olmalı");
            var eksik = new List<string>();
            foreach (ArtAp040WeaponAttachBuilder.Variant v in ArtAp040WeaponAttachBuilder.Variants)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ArtAp040WeaponAttachBuilder.PrefabPath(v.Name)) == null) eksik.Add(v.Name);
            }

            if (eksik.Count == ArtAp040WeaponAttachBuilder.Variants.Length)
            {
                Assert.Ignore("Ek prefabları henüz kurulmadı (" + ArtAp040WeaponAttachBuilder.PrefabFolder + "): ArtAp040WeaponAttachBuilder.Build S-02'nin ortak AP-040 malzemelerini bekliyor (" +
                              ArtAp040WeaponAttachBuilder.SharedMaterialFolder + ")");
            }

            Assert.IsEmpty(eksik, "Yarım kurulum — eksik ek prefabı (" + eksik.Count + "): " + string.Join(", ", eksik));
            var shared = AssetDatabase.LoadAssetAtPath<Material>(ArtAp040WeaponAttachBuilder.SharedMaterialPath);
            var glow = AssetDatabase.LoadAssetAtPath<Material>(ArtAp040WeaponAttachBuilder.GlowMaterialPath);
            Assert.IsNotNull(shared, "Ortak malzeme yok: " + ArtAp040WeaponAttachBuilder.SharedMaterialPath);
            Assert.IsNotNull(glow, "Işıma malzemesi yok: " + ArtAp040WeaponAttachBuilder.GlowMaterialPath);

            int soketler = 0;
            foreach (ArtAp040WeaponAttachBuilder.Variant v in ArtAp040WeaponAttachBuilder.Variants)
            {
                Kart kart = Array.Find(Kartlar, k => k.Ad == v.Name);
                Assert.AreEqual(v.Name, kart.Ad, v.Name + ": kart satırı yok");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtAp040WeaponAttachBuilder.PrefabPath(v.Name));
                Transform kok = prefab.transform;

                Assert.IsEmpty(prefab.GetComponentsInChildren<LODGroup>(true), v.Name + ": ek prefabında LODGroup olmamalı (silahın tek LODGroup'una katılır)");
                Assert.IsNull(WeaponSocketContract.Find(kok, v.MountSocket), v.Name + ": montaj işareti " + v.MountSocket + " kökün kendisi, çocuk olarak kalmamalı");

                // LOD0–2 children keep the FBX names; the triangle counts are the card's (±1 % for import-side cleanup).
                string aile = v.Name.Substring("AP040_".Length);
                for (int lod = 0; lod < 3; lod++)
                {
                    Transform c = kok.Find("SM_" + aile + "_LOD" + lod);
                    Assert.IsNotNull(c, v.Name + ": SM_" + aile + "_LOD" + lod + " çocuğu yok");
                    var filter = c.GetComponent<MeshFilter>();
                    var renderer = c.GetComponent<MeshRenderer>();
                    Assert.IsTrue(filter != null && filter.sharedMesh != null && renderer != null, v.Name + " LOD" + lod + ": mesh / renderer yok");
                    int ucgen = Ucgen(filter.sharedMesh);
                    Assert.LessOrEqual(Mathf.Abs(ucgen - kart.Ucgen[lod]), Mathf.Max(2, kart.Ucgen[lod] / 100),
                        v.Name + " LOD" + lod + ": üçgen sayısı " + ucgen + ", kart " + kart.Ucgen[lod] + " (±%1) — yanlış FBX ya da LOD");
                    Material[] ms = renderer.sharedMaterials;
                    Assert.IsTrue(ms.Length >= 1 && ms.Length <= v.MaterialSlots, v.Name + " LOD" + lod + ": " + ms.Length + " malzeme yuvası (en çok " + v.MaterialSlots + ")");
                    Assert.AreSame(shared, ms[0], v.Name + " LOD" + lod + ": yuva 0 M_AP040_Shared değil");
                    if (ms.Length == 2) Assert.AreSame(glow, ms[1], v.Name + " LOD" + lod + ": yuva 1 M_AP040_Glow değil");
                }

                Bounds b = Lod0Sinir(kok);
                Vector3 min = Unity(kart.Min), max = Unity(kart.Max);
                var kartMin = Vector3.Min(min, max);
                var kartMax = Vector3.Max(min, max);
                SinirYakin(v.Name + " LOD0 min", b.min, kartMin);
                SinirYakin(v.Name + " LOD0 max", b.max, kartMax);
                if (v.Family == "E4")
                {
                    Assert.GreaterOrEqual(b.min.x, -SinirTol, v.Name + ": E4 +X'e uzanmalı (silahın sağı) — min " + F(b.min));
                    Assert.Greater(b.max.x, 0.1f, v.Name + ": E4 +X'e uzanmalı — max " + F(b.max));
                }

                if (v.Family == "E2" || v.Family == "E6")
                {
                    Assert.LessOrEqual(b.max.y, SinirTol, v.Name + ": montajın üstüne taşmamalı — max " + F(b.max));
                    Assert.Less(b.min.y, -0.2f, v.Name + ": −Y'ye sarkmalı — min " + F(b.min));
                }

                foreach ((string ad, Vector3 blender) in kart.Soketler)
                {
                    Transform t = kok.Find(ad);
                    Assert.IsNotNull(t, v.Name + ": " + ad + " çocuğu yok");
                    Vector3 gercek = kok.InverseTransformPoint(t.position), beklenen = Unity(blender);
                    if (v.Family == "E6")
                    {
                        // Unity's FBX import mirrors X against the Blender authoring frame; the box is symmetric, the rockets keep their FBX side.
                        gercek.x = Mathf.Abs(gercek.x);
                        beklenen.x = Mathf.Abs(beklenen.x);
                    }

                    Yakin(v.Name + " " + ad, gercek, beklenen, ModelTol);
                    float aci = Quaternion.Angle(t.localRotation, Quaternion.identity);
                    Assert.LessOrEqual(aci, TabloAciTol, v.Name + " " + ad + ": soket çerçevesi ek çerçevesi değil (+Z namlu) — " + aci.ToString("0.0", CultureInfo.InvariantCulture) + "°");
                    soketler++;
                }

                Assert.AreEqual(kart.Soketler.Length, v.Sockets.Length, v.Name + ": builder'ın soket listesi kartla aynı değil");
            }

            Debug.Log("[DD-TEST] wp104b4.prefabs variants=" + ArtAp040WeaponAttachBuilder.Variants.Length + " sockets=" + soketler);
        }

        private static void SinirYakin(string at, Vector3 gercek, Vector3 beklenen)
        {
            Vector3 d = gercek - beklenen;
            float m = Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Abs(d.z)));
            Assert.LessOrEqual(m, SinirTol, at + ": " + F(gercek) + " ≠ kart [x, z, −y] " + F(beklenen) + " (" + (m * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm)");
        }

        /// <summary>The LOD0 meshes' AABB in the prefab root's space (mesh bounds through each child's matrix).</summary>
        private static Bounds Lod0Sinir(Transform kok)
        {
            bool ilk = true;
            var b = new Bounds();
            foreach (MeshFilter f in kok.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || AuthoredWeaponModel.LodIndexOf(f.name) != 0) continue;
                Matrix4x4 m = kok.worldToLocalMatrix * f.transform.localToWorldMatrix;
                Bounds mb = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 kose = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(kose);
                    if (ilk) b = new Bounds(p, Vector3.zero);
                    else b.Encapsulate(p);
                    ilk = false;
                }
            }

            Assert.IsFalse(ilk, kok.name + ": LOD0 mesh yok");
            return b;
        }

        private static int Ucgen(Mesh mesh)
        {
            long indeks = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) indeks += mesh.GetIndexCount(s);
            return (int)(indeks / 3);
        }
    }
}
