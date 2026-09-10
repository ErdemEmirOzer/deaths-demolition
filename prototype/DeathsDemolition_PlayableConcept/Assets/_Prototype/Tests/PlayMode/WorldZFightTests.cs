using System.Collections;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// G-14 · KUSUR 3 regresyon testi — "yüzeylerde ve cisimlerde çok fazla görsel titreme".
    ///
    /// ÖLÇÜLEN BÜYÜKLÜK
    /// ----------------
    /// Zemin dörtgenleri tam olarak y = <see cref="WorldLayout.GroundY"/> düzlemindedir.
    /// Dünyadaki her yapı (sur, saray, kule, kapı, bahçe duvarı, avlu sekisi, taş fener,
    /// ağaç gövdesi ...) bir kutu/silindir olarak y = 0'dan başlar, yani TABAN YÜZÜ
    /// zeminle tam çakışıktır. Bu çakışık yüzey iki durumdan birindedir:
    ///
    ///   TABAN AŞAĞI BAKIYORSA  arka yüz kültenir, hiç çizilmez -> titreme yok.
    ///   TABAN YUKARI BAKIYORSA zeminle aynı derinlikte iki ön yüz kalır -> Z-FIGHTING.
    ///
    /// Sarım hatası düzeltilmeden önce tabanlar YUKARI bakıyordu (bkz. MeshWindingTests),
    /// yani bu çakışık alanın tamamı ders kitabı z-fighting'iydi. Derinlik tamponu
    /// bu ayrımı yapamaz: 24 bit ters-Z ile 20 m'de bir adım 0.079 mm, 50 m'de 0.50 mm
    /// olduğundan 0.000 mm'lik bir fark hiçbir mesafede çözülemez; hangi yüzeyin
    /// kazandığı kameranın milimetrik hareketiyle değişir, gözde "titreme" budur.
    ///
    /// Bu test o alanı m² olarak ölçer ve YUKARI bakan payının sıfır olmasını şart koşar.
    /// </summary>
    [TestFixture]
    public sealed class WorldZFightTests
    {
        /// <summary>Zemin düzlemine "çakışık" sayılma toleransı: 0.1 mm.</summary>
        private const float DuzlemToleransi = 0.0001f;

        [UnityTest]
        [Category("Regression")]
        [Timeout(300000)]
        public IEnumerator ZeminDuzlemindeYukariBakanYuzKalmaz()
        {
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene(PrototypeScenes.Run, LoadSceneMode.Single);
            yield return null;

            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 12f)
            {
                yield return null;
            }

            GameObject kok = WorldBootstrap.Last.Root;
            Assert.IsNotNull(kok, "Dunya kurulmadi; WorldBootstrap.Last.Root bos.");

            MeshFilter[] parcalar = kok.GetComponentsInChildren<MeshFilter>(true);
            Assert.Greater(parcalar.Length, 10, "Dunyada beklenen kadar parca yok.");

            float yukariAlan = 0f;
            float asagiAlan = 0f;
            int yukariUcgen = 0;
            int asagiUcgen = 0;
            var suclular = new StringBuilder();

            for (int p = 0; p < parcalar.Length; p++)
            {
                MeshFilter mf = parcalar[p];
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    continue;
                }

                // Zeminin kendisi elbette zemin duzlemindedir; olculen sey zeminin
                // USTUNE oturan govdelerin taban yuzudur.
                if (mf.name.StartsWith("Ground_") || mf.name == "PondWater" ||
                    mf.name == "PondBasin")
                {
                    continue;
                }

                Bounds b = mf.GetComponent<Renderer>() != null
                    ? mf.GetComponent<Renderer>().bounds
                    : mesh.bounds;
                if (b.min.y > WorldLayout.GroundY + 0.01f || b.max.y < WorldLayout.GroundY - 0.01f)
                {
                    continue;
                }

                Transform tr = mf.transform;
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;

                float parcaYukari = 0f;

                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = tr.TransformPoint(v[t[i]]);
                    Vector3 bb = tr.TransformPoint(v[t[i + 1]]);
                    Vector3 c = tr.TransformPoint(v[t[i + 2]]);

                    if (Mathf.Abs(a.y - WorldLayout.GroundY) > DuzlemToleransi ||
                        Mathf.Abs(bb.y - WorldLayout.GroundY) > DuzlemToleransi ||
                        Mathf.Abs(c.y - WorldLayout.GroundY) > DuzlemToleransi)
                    {
                        continue;
                    }

                    // Unity'de on yuz yonu Cross(b-a, c-a)'dir.
                    Vector3 yuz = Vector3.Cross(bb - a, c - a);
                    float alan = yuz.magnitude * 0.5f;

                    if (yuz.y > 0f)
                    {
                        yukariAlan += alan;
                        parcaYukari += alan;
                        yukariUcgen++;
                    }
                    else
                    {
                        asagiAlan += alan;
                        asagiUcgen++;
                    }
                }

                if (parcaYukari > 0.0001f)
                {
                    suclular.AppendLine("    " + mf.name + " : " +
                                        parcaYukari.ToString("F2") + " m2 yukari bakan cakisik yuz");
                }
            }

            var rapor = new StringBuilder();
            rapor.AppendLine("[DD-TEST] zfight.zemin y=" + WorldLayout.GroundY +
                             " duzlemi (tolerans " + (DuzlemToleransi * 1000f) + " mm)");
            rapor.AppendLine("  asagi bakan (kultenir, guvenli) : " +
                             asagiAlan.ToString("F1") + " m2 / " + asagiUcgen + " ucgen");
            rapor.AppendLine("  yukari bakan (Z-FIGHTING)       : " +
                             yukariAlan.ToString("F1") + " m2 / " + yukariUcgen + " ucgen");
            rapor.AppendLine("  derinlik cozunurlugu 24 bit ters-Z, near=0.3 far=260:");
            rapor.AppendLine("    z=20 m -> 0.079 mm/adim,  z=50 m -> 0.496 mm/adim");
            rapor.AppendLine("    cakisik yuzeylerin fark; 0.000 mm -> hicbir mesafede cozulmez");
            if (suclular.Length > 0)
            {
                rapor.AppendLine("  suclu parcalar:");
                rapor.Append(suclular);
            }

            Debug.Log(rapor.ToString());

            Assert.AreEqual(0f, yukariAlan, 0.0001f,
                "Zemin duzleminde YUKARI bakan " + yukariAlan.ToString("F1") +
                " m2 cakisik yuzey var. Bu alanin tamami zeminle z-fighting yapar ve " +
                "kamera her milimetre kimildadiginda yuzey rengi takla atar.\n" + rapor);

            Assert.Greater(asagiAlan, 1f,
                "Zemin duzleminde hic asagi bakan taban yuzu bulunamadi; olcum " +
                "muhtemelen calismiyor (mesh okunabilir degil?).\n" + rapor);
        }
    }
}
