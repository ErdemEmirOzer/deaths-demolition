using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// Nöbetçi: prototipin URP shader'larının Player build'ine gerçekten girdiğini sınar.
    ///
    /// Bu testlerin var olma sebebi somut bir arıza: hazır macOS build açıldığında ekran
    /// bozuk geliyordu ve Player.log tek satır bırakıyordu —
    /// "[Zombie] URP Lit shader not found. Falling back to Standard."
    /// Sebep, bütün materyallerin çalışma zamanında Shader.Find ile kurulmasıydı. Editör'de
    /// her paketin her shader'ı yüklü olduğu için çalışıyor; build'de ise yalnız BİR ŞEYİN
    /// REFERANS VERDİĞİ shader'lar bulunur ve hiçbir şey URP'ninkilere referans vermiyordu.
    /// Shader.Find null dönüyor, her sistem kendi yedeğine (Standard / Sprites-Default)
    /// düşüyor, URP bunları çizemiyordu.
    /// </summary>
    [TestFixture]
    public sealed class ShaderLibraryTests
    {
        private const string UretimKok = "Assets/_Prototype";

        [Test]
        public void Kitaplik_ResourcestenYuklenir()
        {
            var kitaplik = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);
            Assert.IsNotNull(kitaplik,
                "Resources/" + PrototypeShaderLibrary.ResourcePath + " yok. Bu asset build'in " +
                "URP shader'larına tek tutamağı; olmadan oyun shader'sız çıkar. " +
                "Tools > Deaths Demolition > Rebuild Shader Library.");
        }

        [Test]
        public void Kitaplik_HicbirAlaniBosDegil()
        {
            var kitaplik = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);
            Assert.IsNotNull(kitaplik, "kitaplik yok");
            Assert.IsNull(kitaplik.DescribeMissing(),
                "Kitaplikta atanmamis referans var: " + kitaplik.DescribeMissing());
        }

        /// <summary>Doğru shader'lar mı? Boş olmaması, doğru olması demek değil.</summary>
        [Test]
        public void Kitaplik_URPShaderlariniIsaretEder()
        {
            var k = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);
            Assert.IsNotNull(k, "kitaplik yok");

            Assert.AreEqual("Universal Render Pipeline/Lit", k.Lit.name);
            Assert.AreEqual("Universal Render Pipeline/Simple Lit", k.SimpleLit.name);
            Assert.AreEqual("Universal Render Pipeline/Unlit", k.Unlit.name);
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", k.ParticlesUnlit.name);
            Assert.AreEqual("Skybox/Procedural", k.Skybox.name);
        }

        /// <summary>Shader'in kendisi build'e girse de kullanilan VARYANT strip edilebilir.</summary>
        [Test]
        public void VaryantCivileri_SaydamKombinasyonlariTutar()
        {
            var k = Resources.Load<PrototypeShaderLibrary>(PrototypeShaderLibrary.ResourcePath);
            Assert.IsNotNull(k, "kitaplik yok");
            Assert.Greater(k.VariantPins.Length, 0,
                "Varyant civisi yok. URP'nin saydamlik keyword'leri shader_feature'dir: " +
                "hicbir materyal ASSET'i kullanmiyorsa build onlari atar ve calisma zamaninda " +
                "acilan _SURFACE_TYPE_TRANSPARENT / _ALPHABLEND_ON varyantlari build'de bulunmaz.");

            var saydam = new List<string>();
            foreach (Material m in k.VariantPins)
            {
                Assert.IsNotNull(m, "bos varyant civisi");
                Assert.IsNotNull(m.shader, "civinin shader'i yok: " + m.name);
                if (m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                {
                    saydam.Add(m.name);
                }
            }

            Assert.Greater(saydam.Count, 0,
                "Hicbir civi saydam varyant tutmuyor; saydam efektler build'de opak cizilir.");
        }

        /// <summary>
        /// Asil nöbetçi. Kitaplik dolu olsa bile biri yeniden Shader.Find yazarsa arıza
        /// aynen geri gelir — üstelik yine sessizce, yine yalnız build'de.
        /// </summary>
        [Test]
        public void UretimKodu_ShaderFindKullanmaz()
        {
            var desen = new Regex(@"Shader\s*\.\s*Find\s*\(", RegexOptions.Compiled);
            var suclu = new List<string>();

            foreach (string yol in Directory.GetFiles(UretimKok, "*.cs", SearchOption.AllDirectories))
            {
                string norm = yol.Replace('\\', '/');

                // Editör araci kitapligi KURARKEN Shader.Find kullanmak zorunda: asset'i
                // uretmenin baska yolu yok ve o kod Player'a hic girmiyor.
                if (norm.Contains("/Rendering/Editor/")) { continue; }
                // Testin kendi deseni.
                if (norm.EndsWith("/ShaderLibraryTests.cs")) { continue; }

                foreach (string satir in File.ReadAllLines(yol))
                {
                    string kirp = satir.TrimStart();
                    if (kirp.StartsWith("//") || kirp.StartsWith("///") || kirp.StartsWith("*"))
                    {
                        continue;
                    }

                    if (desen.IsMatch(satir))
                    {
                        suclu.Add(norm + " :: " + kirp);
                    }
                }
            }

            Assert.IsEmpty(suclu,
                "Uretim kodunda Shader.Find geri geldi. Build'de null doner ve sistem " +
                "sessizce bozuk materyal yaratir. PrototypeShaders uzerinden al:\n  " +
                string.Join("\n  ", suclu));
        }

        /// <summary>Desen gercekten yakaliyor mu? Bos gecen bir tarama kanit degildir.</summary>
        [Test]
        public void ShaderFindDeseni_KendiniKanitlar()
        {
            var desen = new Regex(@"Shader\s*\.\s*Find\s*\(", RegexOptions.Compiled);
            Assert.IsTrue(desen.IsMatch("Shader.Find(\"x\")"), "duz yazim kacti");
            Assert.IsTrue(desen.IsMatch("s = Shader . Find ( \"x\" );"), "bosluklu yazim kacti");
            Assert.IsFalse(desen.IsMatch("ShaderFind(\"x\")"), "yanlis eslesme");
        }

        [Test]
        public void Proje_URPPipelineIleYapilandirilmis()
        {
            Assert.IsNotNull(GraphicsSettings.currentRenderPipeline,
                "Graphics ayarlarinda Render Pipeline Asset atanmamis.");
        }
    }
}

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>Açılış penceresinin masaüstüne sığdırılması — aritmetik nöbetçisi.</summary>
    [TestFixture]
    public sealed class WindowFitTests
    {
        [Test]
        public void BuyukEkran_TasarimBoyutunuAsmaz()
        {
            Vector2Int b = DD.Prototype.Core.Boot.PrototypeWindowFit.SigdirilmisBoyut(3440, 1440);
            Assert.AreEqual(1920, b.x);
            Assert.AreEqual(1080, b.y);
        }

        [Test]
        public void DizustuMasaustu_SigacakSekildeKuculur()
        {
            // 14" MacBook Pro: masaüstü ~1512x982 nokta, %90 payla 1361x884.
            Vector2Int b = DD.Prototype.Core.Boot.PrototypeWindowFit.SigdirilmisBoyut(1361, 884);
            Assert.LessOrEqual(b.x, 1361, "pencere genişlik payını aşıyor");
            Assert.LessOrEqual(b.y, 884, "pencere yükseklik payını aşıyor");
            Assert.AreEqual(16f / 9f, (float)b.x / b.y, 0.01f, "en-boy oranı 16:9 kalmalı");
        }

        [Test]
        public void CokKucukEkran_OkunabilirTabaninAltinaInmez()
        {
            Vector2Int b = DD.Prototype.Core.Boot.PrototypeWindowFit.SigdirilmisBoyut(800, 600);
            Assert.GreaterOrEqual(b.x, 1280);
            Assert.GreaterOrEqual(b.y, 720);
        }
    }
}

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// Backing ölçeği nöbetçisi. Bu aritmetik bir kez yanlış yazıldı ve pencere
    /// masaüstünden büyük açılmaya devam etti: eşik eksen başına uygulanınca
    /// 3024x1964'ün yüksekliği (1964 &lt; 2560) dönüştürülmüyor ve masaüstü
    /// 1512x1964 sanılıyordu.
    /// </summary>
    [TestFixture]
    public sealed class BackingScaleTests
    {
        [Test]
        public void Retina_GenislikVeYukseklikAyniOlcegiAlir()
        {
            int o = DD.Prototype.Core.Boot.PrototypeWindowFit.BackingOlcegi(3024, 1964);
            Assert.AreEqual(2, o);
            Assert.AreEqual(1512, 3024 / o);
            Assert.AreEqual(982, 1964 / o, "yükseklik de ölçeklenmeli");
        }

        [Test]
        public void GercekIkiBinBesYuzAltmis_HiDPISayilmaz()
        {
            Assert.AreEqual(1, DD.Prototype.Core.Boot.PrototypeWindowFit.BackingOlcegi(2560, 1440));
        }

        [Test]
        public void BesKEkran_HiDPISayilir()
        {
            Assert.AreEqual(2, DD.Prototype.Core.Boot.PrototypeWindowFit.BackingOlcegi(5120, 2880));
        }

        [Test]
        public void SigdirilanPencere_OlculenBozulmaEsiginAltindaKalir()
        {
            // 14" MBP: masaüstü 1512x982 nokta. Pencere ekrana sığmalı ama gereksiz
            // küçük de olmamalı - bir önceki sürüm blanket %10 kesip alanın beşte birini
            // yiyordu.
            int o = DD.Prototype.Core.Boot.PrototypeWindowFit.BackingOlcegi(3024, 1964);
            Vector2Int b = DD.Prototype.Core.Boot.PrototypeWindowFit.SigdirilmisBoyut(
                3024 / o - 16, 1964 / o - 64);
            Assert.LessOrEqual(b.x, 1512, "pencere masaüstü genişliğini aşıyor");
            Assert.LessOrEqual(b.y, 982 - 53, "pencere menü çubuğu + başlık çubuğuna giriyor");
            Assert.Greater(b.x, 1440, "pencere gereksiz küçük");
        }
    }
}
