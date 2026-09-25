using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditorInternal;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// GT · bagimsiz dogrulama · DEVIR BUTUNLUGU (G-deaths-demolition-17).
    ///
    /// YAKALADIGI KUSUR SINIFI
    /// -----------------------
    /// Bu projede ayni imzayi tasiyan uc kusur PlayMode'a kadar hayatta kaldi:
    ///
    ///   1. RunDirector.EnsureExists()                          tanimli, SIFIR cagran
    ///   2. CrowdService.SetPlayer / EnemyTargeting.SetPlayer    tanimli, SIFIR cagran
    ///   3. EnemyFactory.TemplatePrefab -> EnemySpawnDirector    iki taraf da belgeledi,
    ///                                                          hicbiri icra etmedi
    ///
    /// ORTAK IMZA: bir taraf devri belgeler, karsi taraf devralmaz, ve hicbiri hata
    /// basmaz - hepsi tolere etmek uzere tasarlandiklari null'da sessizce erken doner.
    /// Derleme temiz, testler yesil, oyun olu. Derleyici bunu goremez cunku kod
    /// GECERLIDIR; eksik olan bir cagri, bir hata degil.
    ///
    /// YONTEM: kaynak kodu METIN olarak tarar. Unity calisma zamanina ihtiyac duymaz,
    /// bu yuzden EditMode'da saniyeler icinde kosar ve PlayMode'dan ONCE kirmizi yanar -
    /// kusur sinifinin yakalanmasi gereken yer tam olarak orasi.
    ///
    /// Bu dosya URETIM KODUNU DEGISTIRMEZ. Kirmizi yanarsa bulgu uretir.
    /// </summary>
    [TestFixture]
    public sealed class HandoffIntegrityTests
    {
        private const string PrototypeRoot = "Assets/_Prototype";

        // ============================================================ kaynak yukleme

        private static string[] s_cachedFiles;
        private static Dictionary<string, string> s_cachedRaw;
        private static Dictionary<string, string> s_cachedNoComment;
        private static Dictionary<string, string> s_cachedCode;

        /// <summary>Uretim kaynaklari. Testlerin kendisi HARIC - testler cagran sayilmaz.</summary>
        private static string[] KaynakDosyalar()
        {
            if (s_cachedFiles != null)
            {
                return s_cachedFiles;
            }

            s_cachedFiles = Directory
                .GetFiles(PrototypeRoot, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Tests/"))
                .OrderBy(p => p)
                .ToArray();
            return s_cachedFiles;
        }

        private static string Raw(string path)
        {
            if (s_cachedRaw == null)
            {
                s_cachedRaw = new Dictionary<string, string>();
            }

            string v;
            if (!s_cachedRaw.TryGetValue(path, out v))
            {
                v = File.ReadAllText(path);
                s_cachedRaw[path] = v;
            }

            return v;
        }

        /// <summary>Yorumlar silinmis, metin sabitleri KORUNMUS kod. Etiket taramasi bunu kullanir.</summary>
        private static string YorumsuzKod(string path)
        {
            if (s_cachedNoComment == null)
            {
                s_cachedNoComment = new Dictionary<string, string>();
            }

            string v;
            if (!s_cachedNoComment.TryGetValue(path, out v))
            {
                string s = Regex.Replace(Raw(path), @"/\*.*?\*/", " ", RegexOptions.Singleline);
                s = Regex.Replace(s, @"//[^\n]*", " ");
                s_cachedNoComment[path] = v = s;
            }

            return v;
        }

        /// <summary>Yorumlar, metin VE karakter sabitleri silinmis kod. Cagri taramasi bunu kullanir.</summary>
        private static string CagriKodu(string path)
        {
            if (s_cachedCode == null)
            {
                s_cachedCode = new Dictionary<string, string>();
            }

            string v;
            if (!s_cachedCode.TryGetValue(path, out v))
            {
                v = Regex.Replace(YorumsuzKod(path), "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
                // WP-031C: char literals too — '{' / '}' in a hand-written parser (AudioCatalogBuilder, 8896e10e)
                // broke the brace stack SaranTip counts, so seven methods after them lost their enclosing type.
                v = Regex.Replace(v, @"'(?:[^'\\\n]|\\.)'", "' '");
                s_cachedCode[path] = v;
            }

            return v;
        }

        // ==================================================== 1 · SIFIR CAGRANLI KURUCU

        /// <summary>
        /// DEVIR IMZASI DESEN LISTESI - her desen gerekcelidir.
        ///
        /// Olcut: "bu metot, kendi tipi disindan BIRININ cagirmasi gerektigi icin mi var?"
        /// Yanit evetse desen listeye girer. Saf fonksiyon / hesap / donusum imzalari
        /// ALINMAZ; onlarin cagrani olmamasi olu kod sorunudur, devir sorunu degil.
        ///
        ///   Ensure*     "yoksa yarat" kurucu. Tanimi geregi bir kurulum noktasi cagirir.
        ///               Kusur 1 (RunDirector.EnsureExists) tam olarak bu desendendir.
        ///   Install*    kompozisyon koku girisi. Cagrilmazsa hicbir sistem kurulmaz.
        ///   SetPlayer*  aktor baglama. Kusur 2 bu desendendir.
        ///   Bind/Attach/Connect/Wire/Mount   iki modulu birbirine baglayan devir fiilleri.
        ///   Register*   kayit devri; kaydolan taraf cagirmazsa kayit bos kalir.
        ///
        /// KASITLI DISARIDA BIRAKILANLAR (gurultu onleme, olculerek karar verildi):
        ///   Apply*   -> ApplyAdsr/ApplyColor/ApplyTremolo saf DSP ve gorsel hesaplardir;
        ///               listeye alininca 31 yerine 40 imza cikiyor ve 9'u devir degil.
        ///   Try*/Get*/Compute*/Select*  -> sorgu imzalari, devir degil.
        ///
        /// BU TEST NE ZAMAN YALAN SOYLER
        /// -----------------------------
        /// (a) Desen listesi dar tutulursa: yeni bir devir metodu "Prepare"/"Arm" gibi
        ///     listede olmayan bir fiille adlandirilirsa test onu hic gormez ve yesil
        ///     yanar. Bu yuzden TaramaKapsami_OlculuVeYeterli desen listesinin KAPSAMINI
        ///     ayrica olcer ve imza sayisi cokerse kirmizi yanar.
        /// (b) Cagran, tipi bir arayuz/temsilci uzerinden cagirirsa metin taramasi goremez.
        /// (c) Cagran YALNIZ test kodunda ise: testler bilerek haric tutulur, cunku
        ///     "yalniz test cagiriyor" durumu uretimde cagransiz olmakla ayni seydir.
        /// </summary>
        private static readonly string[] DevirOnEkleri =
        {
            "Ensure", "Install", "SetPlayer", "Bind", "Attach", "Register", "Connect", "Wire", "Mount"
        };

        private static readonly HashSet<string> DevirTamAdlari = new HashSet<string>
        {
            "EnsureExists", "EnsureBuilt", "Install", "Bind", "Unbind", "Attach", "Detach",
            "Register", "Unregister", "Connect", "Wire", "Mount", "Hook"
        };

        private static bool DevirImzasiMi(string ad)
        {
            if (DevirTamAdlari.Contains(ad))
            {
                return true;
            }

            for (int i = 0; i < DevirOnEkleri.Length; i++)
            {
                if (ad.StartsWith(DevirOnEkleri[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly Regex PublicStaticDef = new Regex(
            @"\bpublic\s+static\s+(?:readonly\s+|extern\s+|unsafe\s+|async\s+)*" +
            @"([A-Za-z_][\w<>\[\],\.\s\?]*?)\s+([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(",
            RegexOptions.Compiled);

        private static readonly HashSet<string> AnahtarKelimeler = new HashSet<string>
        {
            "if", "for", "while", "switch", "return", "foreach", "lock", "using", "catch", "fixed"
        };

        private struct MetotKaydi
        {
            public string Dosya;
            public string Tip;
            public string Ad;
            public int Konum;
        }

        /// <summary>Bir konumu saran en icteki class/struct adi. Suslu parantez yigini ile.</summary>
        private static string SaranTip(string code, int pos)
        {
            var yigin = new List<string>();
            string bekleyen = null;
            foreach (Match m in Regex.Matches(code, @"\b(?:class|struct)\s+([A-Za-z_]\w*)|\{|\}"))
            {
                if (m.Index > pos)
                {
                    break;
                }

                string t = m.Value;
                if (t == "{")
                {
                    yigin.Add(bekleyen);
                    bekleyen = null;
                }
                else if (t == "}")
                {
                    if (yigin.Count > 0)
                    {
                        yigin.RemoveAt(yigin.Count - 1);
                    }
                }
                else
                {
                    bekleyen = m.Groups[1].Value;
                }
            }

            for (int i = yigin.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(yigin[i]))
                {
                    return yigin[i];
                }
            }

            return null;
        }

        private static List<MetotKaydi> PublicStaticMetotlar()
        {
            var sonuc = new List<MetotKaydi>();
            foreach (string p in KaynakDosyalar())
            {
                string code = CagriKodu(p);
                foreach (Match m in PublicStaticDef.Matches(code))
                {
                    string ad = m.Groups[2].Value;
                    if (AnahtarKelimeler.Contains(ad))
                    {
                        continue;
                    }

                    var kayit = new MetotKaydi();
                    kayit.Dosya = p;
                    kayit.Tip = SaranTip(code, m.Index);
                    kayit.Ad = ad;
                    kayit.Konum = m.Index;
                    sonuc.Add(kayit);
                }
            }

            return sonuc;
        }

        /// <summary>
        /// Bir devir metodunun cagri sayisi.
        ///
        /// IKI AYRI ESLESME - ve neden ikisi de gerekli:
        ///   NITELIKLI  "Tip.Metot(" her yerde. Bu SART: niteliksiz "EnsureExists(" ile
        ///              saymak, dokuz ayri sinifin EnsureExists'ini tek havuzda toplar ve
        ///              RunDirector'in sifir cagranini baskalarinin cagraniyla maskeler.
        ///              (Bu tuzaga bu testin ilk taslagi dustu; olculerek duzeltildi:
        ///              niteliksiz sayimla RunDirector.EnsureExists 23 "cagran" gosteriyordu.)
        ///   NITELIKSIZ "Metot(" YALNIZ kendi dosyasinda, tanim satiri haric. Mesru
        ///              kaliplari korur: [RuntimeInitializeOnLoadMethod] AutoCreate ->
        ///              EnsureExists (PrototypeAudioDirector), ve tipin kendi icinden
        ///              yapilan koruma cagrilari (ProfileService.EnsureLoaded).
        /// </summary>
        private static int CagriSayisi(MetotKaydi k)
        {
            int toplam = 0;

            Regex nitelikli = string.IsNullOrEmpty(k.Tip)
                ? null
                // WP-079C: a namespace-qualified call (DD.….Vfx.AtmosphereSystem.Attach) is a call — only a word
                // character may not precede the type name; the bare-name pattern below stays strict.
                : new Regex(@"(?<!\w)" + Regex.Escape(k.Tip) + @"\s*\.\s*" + Regex.Escape(k.Ad) + @"\s*\(");
            var niteliksiz = new Regex(@"(?<![\w.])" + Regex.Escape(k.Ad) + @"\s*\(");

            foreach (string p in KaynakDosyalar())
            {
                string code = CagriKodu(p);

                if (nitelikli != null)
                {
                    toplam += nitelikli.Matches(code).Count;
                }

                if (p != k.Dosya)
                {
                    continue;
                }

                foreach (Match m in niteliksiz.Matches(code))
                {
                    if (m.Index == k.Konum)
                    {
                        continue; // tanimin kendisi
                    }

                    // Tanim satirlarini ele: "public static X Ad(" bicimi cagri degildir.
                    int bas = System.Math.Max(0, m.Index - 120);
                    string onEk = code.Substring(bas, m.Index - bas);
                    if (Regex.IsMatch(onEk, @"(public|private|internal|protected)\s+static[^;{}]*$"))
                    {
                        continue;
                    }

                    toplam++;
                }
            }

            return toplam;
        }

        [Test]
        public void SifirCagranliDevirKurucusuYok()
        {
            List<MetotKaydi> hepsi = PublicStaticMetotlar();
            List<MetotKaydi> devir = hepsi.Where(k => DevirImzasiMi(k.Ad)).ToList();

            Assert.Greater(devir.Count, 0,
                "Devir imzali hicbir metot bulunamadi; tarama yontemi yalanlanmistir.");

            var bulgular = new List<string>();
            foreach (MetotKaydi k in devir)
            {
                if (CagriSayisi(k) == 0)
                {
                    bulgular.Add("  " + (k.Tip ?? "?") + "." + k.Ad + "()   <- " + k.Dosya);
                }
            }

            if (bulgular.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("SIFIR CAGRANLI DEVIR KURUCUSU (" + bulgular.Count + " adet).");
                sb.AppendLine("Bu, RunDirector.EnsureExists kusurunun ta kendisidir: metot tanimli,");
                sb.AppendLine("derleme temiz, ama onu cagiran kimse yok. Ya bir kurulum noktasindan");
                sb.AppendLine("cagirin ya da metodu silin.");
                sb.AppendLine();
                sb.AppendLine(string.Join("\n", bulgular));
                Assert.Fail(sb.ToString());
            }
        }

        /// <summary>
        /// DESEN LISTESININ KAPSAM OLCUSU - 1 numarali testin curutme kosulu.
        ///
        /// 1 numarali test, taradigi imza kumesi bosalirsa da yesil yanar. Bu test o
        /// sessiz cokusu gurultuye cevirir: tarama gercekten dosya goruyor mu, gercekten
        /// metot ayristiriyor mu, devir imzasi orani makul mu?
        ///
        /// Esikler bugunku sayiya DEGIL, yontemin calistigini kanitlayan alt sinira
        /// baglidir - kod buyuyunce test kendiliginden kirmizi yanmasin diye.
        ///
        /// BU TEST NE ZAMAN YALAN SOYLER: esiklerin cok altinda kalirsa. Ornegin
        /// PublicStaticDef regex'i bir C# surum degisikligiyle bozulur ve 349 yerine 3
        /// metot bulursa, alt sinir bunu yakalar; ama 349 yerine 200 bulursa yakalamaz.
        /// </summary>
        [Test]
        public void TaramaKapsami_OlculuVeYeterli()
        {
            string[] dosyalar = KaynakDosyalar();
            List<MetotKaydi> hepsi = PublicStaticMetotlar();
            List<MetotKaydi> devir = hepsi.Where(k => DevirImzasiMi(k.Ad)).ToList();
            int tipiCozulemeyen = hepsi.Count(k => string.IsNullOrEmpty(k.Tip));

            TestContext.WriteLine("TARAMA KAPSAMI");
            TestContext.WriteLine("  taranan .cs dosya      : " + dosyalar.Length);
            TestContext.WriteLine("  public static metot    : " + hepsi.Count);
            TestContext.WriteLine("  devir imzali sayilan   : " + devir.Count);
            TestContext.WriteLine("  saran tipi cozulemeyen : " + tipiCozulemeyen);
            TestContext.WriteLine("  devir imzalari         : " +
                string.Join(", ", devir.Select(d => d.Tip + "." + d.Ad).OrderBy(x => x).ToArray()));

            Assert.Greater(dosyalar.Length, 100,
                "Tarama " + dosyalar.Length + " dosya gordu. Kok yolu kaymis olmali: " + PrototypeRoot);
            Assert.Greater(hepsi.Count, 150,
                "Yalniz " + hepsi.Count + " public static metot ayristirildi; tanim regex'i bozulmus.");
            Assert.Greater(devir.Count, 15,
                "Yalniz " + devir.Count + " devir imzasi bulundu; desen listesi cokmus. " +
                "1 numarali test bu durumda hicbir sey olcmeden yesil yanar.");
            Assert.AreEqual(0, tipiCozulemeyen,
                "Saran tipi cozulemeyen metot var; nitelikli cagri eslesmesi bunlarda korlesir.");
        }

        // ================================================ 2 · ETIKET YEDEKLERI YALAN SOYLEMESIN

        private static readonly Regex TagAramasi = new Regex(
            @"(?:FindGameObjectWithTag|FindGameObjectsWithTag|FindWithTag|CompareTag)\s*\(\s*([^)]+?)\s*\)",
            RegexOptions.Compiled);

        /// <summary>
        /// Etiket argumanini cozer. Dogrudan sabit metin ise onu, bir tanimlayici ise
        /// ayni dosyadaki "const string AD = ..." tanimini arar.
        ///
        /// NEDEN GEREKLI: EnemyTargeting.cs, FindGameObjectWithTag(PlayerTag) yazar -
        /// PlayerTag bir const'tur. Yalniz duz metin arayan bir regex bu cagriyi HIC
        /// gormez ve test kusur 2'nin yarisini kacirir.
        /// </summary>
        private static string EtiketiCoz(string arg, string dosya)
        {
            arg = arg.Trim();
            Match duz = Regex.Match(arg, "^\"([^\"]*)\"$");
            if (duz.Success)
            {
                return duz.Groups[1].Value;
            }

            if (!Regex.IsMatch(arg, @"^[A-Za-z_]\w*$"))
            {
                return null; // ifade / degisken - statik olarak cozulemez
            }

            Match sabit = Regex.Match(
                YorumsuzKod(dosya),
                @"\bconst\s+string\s+" + Regex.Escape(arg) + @"\s*=\s*""([^""]*)""");
            return sabit.Success ? sabit.Groups[1].Value : null;
        }

        private struct EtiketKullanimi
        {
            public string Etiket;
            public string Dosya;
            public string Ham;
        }

        private static List<EtiketKullanimi> EtiketTuketicileri()
        {
            var sonuc = new List<EtiketKullanimi>();
            foreach (string p in KaynakDosyalar())
            {
                // Yorumsuz ama metinli kod: dokuman yorumlarindaki
                // FindGameObjectWithTag("Player") anlatimlari sayilmasin diye.
                foreach (Match m in TagAramasi.Matches(YorumsuzKod(p)))
                {
                    var k = new EtiketKullanimi();
                    k.Etiket = EtiketiCoz(m.Groups[1].Value, p);
                    k.Dosya = p;
                    k.Ham = m.Value.Trim();
                    sonuc.Add(k);
                }
            }

            return sonuc;
        }

        /// <summary>
        /// 2a · COZULEBILIRLIK. Aranan etiket Unity'ye tanimli mi?
        ///
        /// DIKKAT - BU TESTIN EN ONEMLI INCELIGI: TagManager.asset'in "tags:" listesi
        /// YALNIZ KULLANICI TANIMLI etiketleri tutar. Unity'nin yerlesik etiketleri
        /// (Untagged, Respawn, Finish, EditorOnly, MainCamera, Player, GameController)
        /// o listede ASLA gorunmez. Bu projede tags: [] bostur ve "Player" aranir; naif
        /// bir "TagManager'da var mi" kontrolu burada YANLIS ALARM verir, cunku "Player"
        /// yerlesik bir etikettir ve FindGameObjectWithTag("Player") istisna atmaz.
        /// Bu yuzden olcum InternalEditorUtility.tags uzerinden yapilir: yerlesik +
        /// kullanici tanimli, yani Unity'nin gercekte kabul ettigi kume.
        ///
        /// BU TEST NE ZAMAN YALAN SOYLER: etiket adi bir const'a degil de calisma zamani
        /// ifadesine bagliysa cozulemez ve atlanir; atlananlar rapora yazilir.
        /// </summary>
        /// <summary>
        /// Tarayicinin kendisini kanitlar: sentetik bir kaynak parcasinda etiket
        /// aramasini bulabiliyor mu. Boylece "sifir sonuc" ile "bozuk regex" ayrilir.
        /// </summary>
        private static int DesenKendiniKanitlar()
        {
            const string ornek =
                "var a = GameObject.FindGameObjectWithTag(\"Player\");\n" +
                "if (other.CompareTag(\"Enemy\")) { }\n";
            return TagAramasi.Matches(ornek).Count;
        }

        [Test]
        public void EtiketAramalari_UnityyeTanimliBirEtiketiHedefler()
        {
            List<EtiketKullanimi> kullanimlar = EtiketTuketicileri();

            // Sifir etiket aramasi BASARIDIR, desen yalanlanmasi degil: olu yedekler
            // kaldirildiginda dogal sonuc budur. Deseni yine de korumak gerek, yoksa
            // regex kirilirsa test sessizce yesil yanar. Bu yuzden tarayici sentetik
            // bir ornek uzerinde ayrica sinanir.
            Assert.Greater(DesenKendiniKanitlar(), 0,
                "Tarama deseni sentetik ornegi bile bulamadi; regex kirilmis.");
            if (kullanimlar.Count == 0)
            {
                Assert.Pass("Projede hic etiket aramasi yok - olu yedekler kaldirilmis.");
            }

            var tanimli = new HashSet<string>(InternalEditorUtility.tags);
            var cozulemeyen = new List<string>();
            var bulgular = new List<string>();

            foreach (EtiketKullanimi k in kullanimlar)
            {
                if (k.Etiket == null)
                {
                    cozulemeyen.Add("  " + k.Dosya + "  " + k.Ham);
                    continue;
                }

                if (!tanimli.Contains(k.Etiket))
                {
                    bulgular.Add("  \"" + k.Etiket + "\"  <- " + k.Dosya + "  " + k.Ham);
                }
            }

            TestContext.WriteLine("Unity'ye tanimli etiketler: " + string.Join(", ", tanimli.ToArray()));
            TestContext.WriteLine("Statik cozulemeyen etiket argumani: " + cozulemeyen.Count);
            foreach (string c in cozulemeyen)
            {
                TestContext.WriteLine(c);
            }

            Assert.IsEmpty(bulgular,
                "TANIMSIZ ETIKET ARANIYOR (" + bulgular.Count + "). Unity bu cagrida\n" +
                "UnityException: Tag: X is not defined atar - sessiz null degil, cokme.\n" +
                string.Join("\n", bulgular));
        }

        /// <summary>
        /// 2b · ULASILABILIRLIK - kusur 2'yi gercekten yakalayan olcum.
        ///
        /// Etiketin tanimli olmasi yetmez; o etiketi TASIYAN bir nesne uretilmiyorsa
        /// arama sonsuza dek null doner ve yedek, yalan soyleyen olu koddur. Kusur 2'nin
        /// sessiz sebebi tam olarak buydu: "Player" yerlesik bir etiket oldugu icin cagri
        /// patlamadi, ama bu projede hicbir nesne o etiketi tasimiyordu - rig etiketsizdi,
        /// "Player" burada bir KATMAN adi. Iki taraf da yedegine guvendi, ikisi de bosa
        /// dustu, uc tur boyunca sifir dusman dogdu.
        ///
        /// URETICI ARANAN YERLER: sahne/prefab YAML'inda m_TagString, ve kodda .tag = "X".
        ///
        /// BU TEST NE ZAMAN YALAN SOYLER: etiket bir degiskenden ataniyorsa
        /// (go.tag = someString) uretici gorunmez ve test haksiz yere kirmizi yanar.
        /// Bugun projede oyle bir atama yok; cikarsa bu yorum guncellenmeli.
        /// </summary>
        [Test]
        public void EtiketYedekleri_GercektenUlasilabilirBirNesneyiHedefler()
        {
            List<EtiketKullanimi> kullanimlar = EtiketTuketicileri()
                .Where(k => k.Etiket != null && !k.Ham.StartsWith("CompareTag"))
                .ToList();

            Assert.Greater(DesenKendiniKanitlar(), 0,
                "Tarama deseni sentetik ornegi bile bulamadi; regex kirilmis.");
            if (kullanimlar.Count == 0)
            {
                Assert.Pass("Projede hic etiket aramasi yok - olu yedekler kaldirilmis.");
            }

            // URETICILER - sahne ve prefab YAML'i
            var uretilen = new HashSet<string>();
            foreach (string p in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                if (!p.EndsWith(".unity") && !p.EndsWith(".prefab"))
                {
                    continue;
                }

                foreach (Match m in Regex.Matches(File.ReadAllText(p), @"m_TagString:\s*(.+)"))
                {
                    uretilen.Add(m.Groups[1].Value.Trim());
                }
            }

            // URETICILER - kodda .tag = "X" atamalari (yorumsuz, metinli kod)
            foreach (string p in KaynakDosyalar())
            {
                foreach (Match m in Regex.Matches(YorumsuzKod(p), @"\.tag\s*=\s*""([^""]*)"""))
                {
                    uretilen.Add(m.Groups[1].Value);
                }
            }

            TestContext.WriteLine("Projede GERCEKTEN atanan etiketler: " + string.Join(", ", uretilen.ToArray()));

            var bulgular = new List<string>();
            foreach (EtiketKullanimi k in kullanimlar)
            {
                if (!uretilen.Contains(k.Etiket))
                {
                    bulgular.Add("  \"" + k.Etiket + "\" araniyor ama hicbir nesneye atanmiyor\n" +
                                 "      " + k.Dosya + "   " + k.Ham);
                }
            }

            Assert.IsEmpty(bulgular,
                "ULASILAMAZ ETIKET YEDEGI (" + bulgular.Count + ").\n" +
                "Bu aramalar sonsuza dek null doner: etiket Unity'ye tanimli olabilir, ama\n" +
                "projede o etiketi TASIYAN hicbir sahne nesnesi, prefab ya da kod atamasi yok.\n" +
                "Yedek calisiyormus gibi durur, hic calismaz - kusur 2'nin sessiz sebebi budur.\n" +
                "Ya etiketi ureten tarafi ekleyin ya da yedegi silip devri zorunlu kilin.\n\n" +
                string.Join("\n", bulgular));
        }

        // ============================================ 3 · BELGELENMIS DEVIR ICRA EDILMIS MI

        /// <summary>
        /// Yorumda "sunu biri cagirmali" diyen her yukumluluk, o yorumun hemen altinda
        /// bildirilen uyeye yapisir. Test o uyenin gercekten bir cagrani oldugunu olcer.
        ///
        /// Kusur 2'nin belgesi tam olarak boyleydi: EnemyTargeting ustunde "Whoever owns
        /// the player (G-02) calls SetPlayer once on spawn" yaziyordu ve kimse cagirmiyordu.
        ///
        /// BU TEST SEZGISELDIR. Yakalayamadiklari (durust liste):
        ///   · Yukumlulugu serbest metinle anlatip uyeyi adiyla anmayan yorumlar.
        ///   · Yorum ile uye arasinda 8 satirdan fazla mesafe olan bildirimler.
        ///   · Bir ALANIN devri (EnemyFactory.TemplatePrefab -> EnemySpawnDirector.EnemyPrefab,
        ///     yani kusur 3) - bu bir metot cagrisi degil bir ATAMA oldugu icin bu test onu
        ///     GOREMEZ. Kusur 3'u yakalayan sey 1 numarali testtir (devri yapan
        ///     RunSystemsInstaller.Install'in kendisinin cagrani olmasi sarti).
        ///   · Listede olmayan kaliplarla yazilmis yukumlulukler.
        /// </summary>
        private static readonly Regex YukumlulukIfadesi = new Regex(
            @"must be called|call from|whoever owns|hands off to|is responsible for calling|" +
            @"devreder|baglayacak|caller must call",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UyeBildirimi = new Regex(
            @"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s+[^;{}()]*?\b([A-Za-z_]\w*)\s*\(",
            RegexOptions.Compiled);

        [Test]
        public void BelgelenmisDevir_GercektenIcraEdilmis()
        {
            var dosyaListe = new List<string>();
            var satirListe = new List<int>();
            var adListe = new List<string>();
            var yorumListe = new List<string>();

            foreach (string p in KaynakDosyalar())
            {
                string[] satirlar = Raw(p).Split('\n');
                for (int i = 0; i < satirlar.Length; i++)
                {
                    string t = satirlar[i].TrimStart();
                    bool yorumMu = t.StartsWith("///") || t.StartsWith("//") || t.StartsWith("*");
                    if (!yorumMu || !YukumlulukIfadesi.IsMatch(satirlar[i]))
                    {
                        continue;
                    }

                    int sinir = System.Math.Min(i + 8, satirlar.Length);
                    for (int j = i + 1; j < sinir; j++)
                    {
                        string s = satirlar[j].TrimStart();
                        if (s.Length == 0 || s.StartsWith("///") || s.StartsWith("//") ||
                            s.StartsWith("*") || s.StartsWith("["))
                        {
                            continue;
                        }

                        Match m = UyeBildirimi.Match(satirlar[j]);
                        if (m.Success)
                        {
                            dosyaListe.Add(p);
                            satirListe.Add(j + 1);
                            adListe.Add(m.Groups[1].Value);
                            yorumListe.Add(satirlar[i].Trim());
                        }

                        break;
                    }
                }
            }

            TestContext.WriteLine("Belgelenmis devir yukumlulugu: " + adListe.Count);
            for (int i = 0; i < adListe.Count; i++)
            {
                TestContext.WriteLine("  " + adListe[i] + "()  <- " + dosyaListe[i] + ":" + satirListe[i]);
            }

            Assert.Greater(adListe.Count, 0,
                "Hic yukumluluk yorumu bulunamadi; ifade listesi yalanlanmistir.");

            var bulgular = new List<string>();
            for (int i = 0; i < adListe.Count; i++)
            {
                var niteliksiz = new Regex(@"(?<![\w.])" + Regex.Escape(adListe[i]) + @"\s*\(");
                int cagri = 0;

                foreach (string p in KaynakDosyalar())
                {
                    string code = CagriKodu(p);
                    foreach (Match m in niteliksiz.Matches(code))
                    {
                        int satirNo = code.Substring(0, m.Index).Count(c => c == '\n') + 1;
                        if (p == dosyaListe[i] && satirNo == satirListe[i])
                        {
                            continue; // bildirimin kendisi
                        }

                        cagri++;
                    }
                }

                if (cagri == 0)
                {
                    bulgular.Add("  " + adListe[i] + "()  " + dosyaListe[i] + ":" + satirListe[i] +
                                 "\n      belge: " + yorumListe[i]);
                }
            }

            Assert.IsEmpty(bulgular,
                "BELGELENMIS AMA ICRA EDILMEMIS DEVIR (" + bulgular.Count + ").\n" +
                "Yorum bu uyeyi birinin cagirmasini sart kosuyor; cagiran yok.\n" +
                string.Join("\n", bulgular));
        }
    }
}
