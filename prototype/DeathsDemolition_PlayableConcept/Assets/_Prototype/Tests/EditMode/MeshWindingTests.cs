using System.Text;
using DD.Prototype.World;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// G-14 · KUSUR 2 ve KUSUR 3'ün ortak kök nedeninin nöbetçisi.
    ///
    /// NE ÖLÇÜYOR
    /// ----------
    /// <see cref="MeshBuilder"/>'ın ürettiği her KAPALI ilkelin her üçgeni DIŞARI
    /// bakmalı. Ölçüt tek satır: bir üçgenin ön yüz yönü Unity'de
    /// <c>Cross(b - a, c - a)</c>'dır; kapalı ve dışbükey bir gövdede bu vektörün
    /// üçgen merkezinden gövde merkezine giden yönle skaler çarpımı POZİTİF olmalıdır.
    ///
    /// NEDEN BU TEST VAR
    /// -----------------
    /// Bu koşul 2026-08 tarihinde AddBox / AddSlab / AddRoof / AddCylinder için
    /// SAĞLANMIYORDU: altı yüzün de sarımı içeri bakıyordu. Ölçülmüş sonuçları:
    ///
    ///   FİZİK  — Physics.queriesHitBackfaces=false olduğu için sur duvarının 44 m'deki
    ///            iç yüzü ışına görünmüyordu; ışın duvarın içinden geçip 45.3 m'deki
    ///            uzak yüzü (-1,0,0) normaliyle bildiriyordu. CharacterController temas
    ///            normalini ters alıp kapsülü duvarın İÇİNE itiyor, ondan sonra
    ///            Move() 60 karenin 60'ında 0.0000 m dönüyordu — oyuncu duvara
    ///            yapışıyor, duvar boyunca kayamıyor, geri bile çekilemiyordu.
    ///   GÖRSEL — her kutunun taban yüzü y=0'da ÖNE bakar hale geliyor, y=0'daki zemin
    ///            dörtgenleriyle tam çakışıp z-fighting üretiyordu; ayrıca yakın yüzler
    ///            kültendiği için nesneler uzak/iç yüzleriyle ve ters aydınlanmayla
    ///            çiziliyordu.
    ///
    /// Bu yüzden bu test bir "güzellik" testi değil; oynanışın ve okunabilirliğin
    /// taşıyıcı koşulu. Kırmızıya dönerse duvarlar tekrar yapışkan olur.
    /// </summary>
    [TestFixture]
    public sealed class MeshWindingTests
    {
        [Test]
        public void KapaliIlkellerinHerUcgeniDisariBakar()
        {
            var rapor = new StringBuilder();
            bool hepsiTemiz = true;

            hepsiTemiz &= Olc(rapor, "AddBox", b => b.AddBox(Vector3.zero, new Vector3(2f, 3f, 4f)));
            hepsiTemiz &= Olc(rapor, "AddBox(dondurulmus)",
                b => b.AddBox(Vector3.zero, new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 37f, 0f)));
            hepsiTemiz &= Olc(rapor, "AddSlab", b => b.AddSlab(-1f, -2f, 1f, 2f, 0f, 3f));
            hepsiTemiz &= Olc(rapor, "AddCylinder",
                b => b.AddCylinder(new Vector3(0f, -1f, 0f), 1f, 1f, 2f, 10));
            hepsiTemiz &= Olc(rapor, "AddCylinder(koni)",
                b => b.AddCylinder(new Vector3(0f, -1f, 0f), 1f, 0.4f, 2f, 10));
            hepsiTemiz &= Olc(rapor, "AddSphere",
                b => b.AddSphere(Vector3.zero, new Vector3(1f, 1.3f, 0.8f), 6, 10));
            hepsiTemiz &= Olc(rapor, "AddRoof",
                b => b.AddRoof(Vector3.zero, 4f, 4f, 1f, 0.3f, 0.3f));

            Debug.Log(rapor.ToString());
            Assert.IsTrue(hepsiTemiz,
                "MeshBuilder ilkelleri ic tarafa bakan ucgen uretiyor. Ayrinti:\n" + rapor);
        }

        /// <summary>
        /// Zemin dörtgeni açık (hacimsiz) bir yüzeydir, dışarı/içeri testi anlamsızdır;
        /// onun için ölçülen şey DEPOLANAN normalin sarımla uyuşmasıdır. Bu, gölge
        /// yönünün ve aydınlanmanın doğru çıkmasının koşulu.
        /// </summary>
        [Test]
        public void AcikYuzeydeDepolananNormalSarimlaUyusur()
        {
            var b = new MeshBuilder(32);
            b.AddQuad(
                new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f), Vector3.up, 0.25f);

            Mesh m = b.Build("zemin");
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            int[] t = m.triangles;

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 yuz = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                Assert.Greater(Vector3.Dot(yuz, n[t[i]]), 0.99f,
                    "Zemin dortgeninin sarimi depolanan normalle celisiyor; yuzey ters aydinlanir.");
                Assert.Greater(Vector3.Dot(yuz, Vector3.up), 0.99f,
                    "Zemin dortgeni yukari bakmiyor.");
            }

            Object.DestroyImmediate(m);
        }

        private static bool Olc(StringBuilder rapor, string ad, System.Action<MeshBuilder> kur)
        {
            var b = new MeshBuilder(256);
            kur(b);
            Mesh m = b.Build(ad);
            Assert.IsNotNull(m, ad + " hic ucgen uretmedi.");

            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            int[] t = m.triangles;

            Vector3 merkez = Vector3.zero;
            for (int i = 0; i < v.Length; i++)
            {
                merkez += v[i];
            }

            merkez /= Mathf.Max(1, v.Length);

            int ice = 0;
            int normalCelisen = 0;

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]];
                Vector3 bb = v[t[i + 1]];
                Vector3 c = v[t[i + 2]];

                Vector3 yuz = Vector3.Cross(bb - a, c - a);
                Vector3 disari = (a + bb + c) / 3f - merkez;

                if (Vector3.Dot(yuz, disari) <= 0f)
                {
                    ice++;
                }

                // Depolanan normal sarimla ayni yarim uzayda olmali, yoksa yuzey
                // gorunur ama ters aydinlanir (silindir kapaklarinda tam bu vardi).
                if (Vector3.Dot(yuz.normalized, n[t[i]]) < 0.5f)
                {
                    normalCelisen++;
                }
            }

            Object.DestroyImmediate(m);

            rapor.AppendLine("[SARIM] " + ad.PadRight(22) +
                             " ucgen=" + (t.Length / 3) +
                             "  iceBakan=" + ice +
                             "  normal-celisen=" + normalCelisen);

            return ice == 0 && normalCelisen == 0;
        }
    }
}
