using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.UI.EditorTools
{
    /// <summary>
    /// M4-R1 N21 · writes the generated cursor textures (every arrow height the size rule can pick, Arrow and Press) as PNGs
    /// for review sheets: <c>-executeMethod DD.Prototype.UI.EditorTools.UiCursorExport.Run -ddOut &lt;dir&gt;</c>.
    /// </summary>
    public static class UiCursorExport
    {
        public static void Run()
        {
            string outDir = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-ddOut")
                {
                    outDir = args[i + 1];
                }
            }

            int code = 0;
            try
            {
                if (string.IsNullOrEmpty(outDir))
                {
                    throw new ArgumentException("-ddOut <dir> is required");
                }

                Directory.CreateDirectory(outDir);
                for (int h = UiCursor.MinArrowPx; h <= UiCursor.MaxArrowPx; h++)
                {
                    foreach (bool pressed in new[] { false, true })
                    {
                        Texture2D texture = UiCursor.Sized(pressed, h);
                        File.WriteAllBytes(Path.Combine(outDir, texture.name + ".png"), texture.EncodeToPNG());
                    }
                }

                Debug.Log("[UiCursorExport] wrote " + outDir);
            }
            catch (Exception e)
            {
                Debug.LogError("[UiCursorExport] " + e);
                code = 1;
            }

            EditorApplication.Exit(code);
        }
    }
}
