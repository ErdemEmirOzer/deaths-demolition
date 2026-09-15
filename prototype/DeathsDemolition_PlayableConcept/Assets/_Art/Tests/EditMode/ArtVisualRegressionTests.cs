using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-011 · <see cref="ArtVisualRegression"/> karşılaştırıcısının birim nöbetçisi. Render yok:
    /// 64×64 sentetik dokular üzerinde MeanAbs / MaxAbs / ChangedFraction'ın tanımı sınanır —
    /// eşik "büyüktür" (eşit değil), alfa yok sayılır, boyut uyuşmazlığı "hepsi değişti"dir.
    /// Kapı bu sayıları okur; tanım kayarsa görsel regresyon eşiği sessizce anlamını yitirir.
    ///
    /// Beklenen değerler TÜRETİLİR: 4096 pikselin yarısında tek kanal +40 → MeanAbs =
    /// 40·2048 / (3·4096) = 40/3/2 = 6.667; ChangedFraction = 0.5.
    /// </summary>
    [TestFixture]
    [Category("WP011")]
    public sealed class ArtVisualRegressionTests
    {
        private const int Boyut = 64;
        private const byte Esik = 20;
        private const byte Delta = 40;

        private readonly List<Object> _gecici = new List<Object>();
        private readonly List<string> _geciciDosyalar = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _gecici)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _gecici.Clear();

            foreach (string yol in _geciciDosyalar)
            {
                try
                {
                    if (File.Exists(yol)) File.Delete(yol);
                }
                catch (IOException)
                {
                    // Geçici önbellek; bir sonraki koşu üzerine yazar.
                }
            }

            _geciciDosyalar.Clear();
        }

        // ------------------------------------------------------------------ üreticiler

        /// <summary>Deterministik, düz olmayan bir desen: piksel (x, y) → (x·3, y·3, (x+y)) — sıkıştırma artefaktı yok, RGB24.</summary>
        private Texture2D Desen(int genislik = Boyut, int yukseklik = Boyut)
        {
            var t = new Texture2D(genislik, yukseklik, TextureFormat.RGB24, false);
            var px = new Color32[genislik * yukseklik];
            for (int y = 0; y < yukseklik; y++)
            {
                for (int x = 0; x < genislik; x++)
                {
                    px[y * genislik + x] = new Color32((byte)(x * 3), (byte)(y * 3), (byte)(x + y), 255);
                }
            }

            t.SetPixels32(px);
            t.Apply(false, false);
            _gecici.Add(t);
            return t;
        }

        /// <summary>Kaynağın kopyası; alt yarıdaki (y &lt; h/2) piksellerin tek kanalına +delta (taşma yok: kanal ≤ 189+40).</summary>
        private Texture2D YarisiKaydirilmis(Texture2D kaynak, byte delta, int kanal)
        {
            var t = new Texture2D(kaynak.width, kaynak.height, TextureFormat.RGB24, false);
            Color32[] px = kaynak.GetPixels32();
            int yarim = kaynak.height / 2;
            for (int y = 0; y < yarim; y++)
            {
                for (int x = 0; x < kaynak.width; x++)
                {
                    int i = y * kaynak.width + x;
                    Color32 c = px[i];
                    if (kanal == 0) c.r = (byte)(c.r + delta);
                    else if (kanal == 1) c.g = (byte)(c.g + delta);
                    else c.b = (byte)(c.b + delta);
                    px[i] = c;
                }
            }

            t.SetPixels32(px);
            t.Apply(false, false);
            _gecici.Add(t);
            return t;
        }

        private string GeciciPng(string ad)
        {
            string yol = Path.Combine(Application.temporaryCachePath, "wp011-vr-" + ad + ".png");
            _geciciDosyalar.Add(yol);
            return yol;
        }

        // ------------------------------------------------------------------ testler

        [Test]
        public void Ayni_SifirFark()
        {
            Texture2D a = Desen();
            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, a, Esik);
            Assert.AreEqual(0d, d.MeanAbs, 0d, "MeanAbs");
            Assert.AreEqual(0d, d.MaxAbs, 0d, "MaxAbs");
            Assert.AreEqual(0d, d.ChangedFraction, 0d, "ChangedFraction");
            Assert.AreEqual(Boyut, d.Width);
            Assert.AreEqual(Boyut, d.Height);
        }

        [Test]
        public void AyniIcerikFarkliNesne_SifirFark()
        {
            Texture2D a = Desen();
            Texture2D b = Desen();
            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, b, Esik);
            Assert.AreEqual(0d, d.MeanAbs, 0d);
            Assert.AreEqual(0d, d.ChangedFraction, 0d);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void YarisindaTekKanalArti40_Esik20_YarisiDegisti_MeanAbs40Bolu6(int kanal)
        {
            Texture2D a = Desen();
            Texture2D b = YarisiKaydirilmis(a, Delta, kanal);

            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, b, Esik);

            double beklenenMean = Delta / 3d / 2d; // 40/3/2 = 6.667
            Assert.AreEqual(0.5d, d.ChangedFraction, 1e-9, "ChangedFraction (kanal " + kanal + ")");
            Assert.AreEqual(beklenenMean, d.MeanAbs, 1e-9, "MeanAbs (kanal " + kanal + ")");
            Assert.AreEqual(Delta, d.MaxAbs, 0d, "MaxAbs (kanal " + kanal + ")");

            // Simetri: a↔b sırası sonucu değiştirmez.
            ArtVisualRegression.Diff ters = ArtVisualRegression.Compare(b, a, Esik);
            Assert.AreEqual(d.MeanAbs, ters.MeanAbs, 1e-12);
            Assert.AreEqual(d.ChangedFraction, ters.ChangedFraction, 1e-12);
        }

        [Test]
        public void EsikSiniri_EsikKadarFarkDegismisSayilmaz_BirFazlasiSayilir()
        {
            Texture2D a = Desen();
            Texture2D b = YarisiKaydirilmis(a, Esik, 1);

            ArtVisualRegression.Diff tam = ArtVisualRegression.Compare(a, b, Esik);
            Assert.AreEqual(0d, tam.ChangedFraction, 0d, "Fark == eşik → değişmiş sayılmaz (kural: m > threshold).");
            Assert.AreEqual(Esik / 3d / 2d, tam.MeanAbs, 1e-9, "MeanAbs eşikten bağımsızdır.");

            ArtVisualRegression.Diff birAlt = ArtVisualRegression.Compare(a, b, (byte)(Esik - 1));
            Assert.AreEqual(0.5d, birAlt.ChangedFraction, 1e-9, "Fark == eşik+1 → değişmiş.");

            ArtVisualRegression.Diff sifir = ArtVisualRegression.Compare(a, b, 0);
            Assert.AreEqual(0.5d, sifir.ChangedFraction, 1e-9, "Eşik 0: her gerçek fark sayılır, farksız yarı sayılmaz.");
        }

        [Test]
        public void BoyutUyusmazligi_HepsiDegisti()
        {
            Texture2D a = Desen();
            Texture2D b = Desen(Boyut, Boyut / 2);

            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, b, Esik);
            Assert.AreEqual(1d, d.ChangedFraction, 0d, "ChangedFraction");
            Assert.AreEqual(255d, d.MaxAbs, 0d, "MaxAbs");
            Assert.AreEqual(Boyut, d.Width, "Width birinci dokuyu raporlar.");
            Assert.AreEqual(Boyut, d.Height);

            ArtVisualRegression.Diff ters = ArtVisualRegression.Compare(b, a, Esik);
            Assert.AreEqual(1d, ters.ChangedFraction, 0d);
        }

        [Test]
        public void AlfaFarki_YokSayilir()
        {
            Texture2D a = Desen();
            var b = new Texture2D(Boyut, Boyut, TextureFormat.RGBA32, false);
            Color32[] px = a.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                px[i].a = 0;
            }

            b.SetPixels32(px);
            b.Apply(false, false);
            _gecici.Add(b);

            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, b, 0);
            Assert.AreEqual(0d, d.ChangedFraction, 0d, "Yalnız alfa değişince hiçbir piksel değişmiş sayılmaz.");
            Assert.AreEqual(0d, d.MeanAbs, 0d);
        }

        [Test]
        public void TamKaplama_HerPikselDegisti_Mean255e3()
        {
            var a = Desen();
            var b = new Texture2D(Boyut, Boyut, TextureFormat.RGB24, false);
            Color32[] px = a.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32((byte)(255 - px[i].r), px[i].g, px[i].b, 255);
            }

            b.SetPixels32(px);
            b.Apply(false, false);
            _gecici.Add(b);

            // r → 255−r: |fark| = |255 − 2r|, r = 3x ∈ [0, 189]; ortalama tek kanal üzerinden 3'e bölünür.
            double toplam = 0;
            int degisen = 0;
            for (int i = 0; i < px.Length; i++)
            {
                int r = 3 * (i % Boyut);
                int fark = System.Math.Abs(255 - 2 * r);
                toplam += fark;
                if (fark > Esik) degisen++;
            }

            ArtVisualRegression.Diff d = ArtVisualRegression.Compare(a, b, Esik);
            Assert.AreEqual(toplam / (3d * px.Length), d.MeanAbs, 1e-9, "MeanAbs türetilen değer");
            Assert.AreEqual(degisen / (double)px.Length, d.ChangedFraction, 1e-12, "ChangedFraction türetilen değer");
            Assert.AreEqual(255d, d.MaxAbs, 0d, "MaxAbs (r = 0 → 255)");
        }

        [Test]
        public void ComparePng_KendisiyleSifir_FarkliDosyaYakalanir()
        {
            Texture2D a = Desen();
            Texture2D b = YarisiKaydirilmis(a, Delta, 2);

            string ya = GeciciPng("a");
            string yb = GeciciPng("b");
            File.WriteAllBytes(ya, a.EncodeToPNG());
            File.WriteAllBytes(yb, b.EncodeToPNG());

            ArtVisualRegression.Diff ayni = ArtVisualRegression.ComparePng(ya, ya, Esik);
            Assert.AreEqual(0d, ayni.ChangedFraction, 0d, "PNG kendisiyle 0 olmalı (PNG kayıpsız).");
            Assert.AreEqual(0d, ayni.MeanAbs, 0d);
            Assert.AreEqual(Boyut, ayni.Width);

            ArtVisualRegression.Diff farkli = ArtVisualRegression.ComparePng(ya, yb, Esik);
            Assert.AreEqual(0.5d, farkli.ChangedFraction, 1e-9, "PNG gidiş-dönüşü farkı korumalı (kayıpsız).");
            Assert.AreEqual(Delta / 3d / 2d, farkli.MeanAbs, 1e-9);
        }

        [Test]
        public void ComparePng_DosyaYoksaFirlatir()
        {
            string yok = GeciciPng("yok-boyle-dosya");
            if (File.Exists(yok)) File.Delete(yok);
            Assert.Throws<FileNotFoundException>(() => ArtVisualRegression.ComparePng(yok, yok, Esik));
        }

        [Test]
        public void StandartPozlar_AdlarTekilVeGameplayPozlariRigGeometrisinde()
        {
            var adlar = new HashSet<string>();
            ArtVisualRegression.CameraPose courtyard = null;
            foreach (ArtVisualRegression.CameraPose p in ArtVisualRegression.StandardPoses)
            {
                Assert.IsTrue(adlar.Add(p.name), "Poz adı yineleniyor: " + p.name);
                Assert.Greater(p.verticalFov, 0f, p.name + ": FOV");
                Assert.Greater((p.lookAt - p.position).magnitude, 0.1f, p.name + ": lookAt konumla çakışıyor.");
                if (p.name == "gameplay_courtyard") courtyard = p;
            }

            Assert.IsNotNull(courtyard, "'gameplay_courtyard' pozu yok; PlayMode yakalama ve probe bu ada bağlıdır.");
            // Rig geometrisi: 15 m yukarı, 13 m geri → lookAt = pos + (0, −15, +13), FOV 42.
            Assert.AreEqual(new Vector3(7f, 16f, -13f), courtyard.position);
            Assert.Less((courtyard.lookAt - (courtyard.position + new Vector3(0f, -15f, 13f))).magnitude, 1e-4f, "gameplay_courtyard lookAt rig ofsetiyle uyuşmuyor.");
            Assert.AreEqual(42f, courtyard.verticalFov, 1e-4f);
        }
    }
}
