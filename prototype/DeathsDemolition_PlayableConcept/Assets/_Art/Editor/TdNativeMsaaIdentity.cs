// Offline source proposal. This records dladdr/file identity, never a hash of all mapped image bytes.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Pipeline.EditorTools
{
    internal static class TdNativeMsaaIdentity
    {
        internal const string Asset = "Assets/_Art/Editor/Native/GfxPluginDDNativeMsaaPositions.dylib";
        private const string BinarySha = "42ccad59c8f96e25ca91271c66ae66fb91f36bdd02935c4cca33da06aa94c4c2";
        private const string DiskUuid = "82c51b8894e13e4c87d0437f2692fbee";
        private const int BinaryBytes = 72696, HeaderBytes = 32, CommandBytes = 2840, CommandCount = 19;
        private const string MetaSha = "c7525886b79959858cfded7cc9e328475366f0ee442f59d5df28b0b9213a28b0";
        [Serializable] internal sealed class Observation
        {
            public string status = "UNKNOWN", reason, dladdrPath, resolvedPath, fileSha256, diskImageUuid, importerMetaSha256;
            public string mappedImageUuid = "NOT_READ: no mapped-header lifetime guarantee in unchanged V001 binary";
            public string exportedBuildId = "NOT_AVAILABLE_IN_EXACT_V001_BINARY";
            public string limit = "dladdr callback/image relation and resolved file bytes only; disk UUID is not a mapped-content hash. Concurrent unload/path replacement is not atomically excluded.";
            public long fileBytes, fileWriteUtcTicksBefore, fileWriteUtcTicksAfter;
            public ulong callbackRva;
            public int playerTargetsChecked;
            public bool resolvedFileMatch, editorOnlyImporterObserved, mappedContentHashProven = false;
        }
        [StructLayout(LayoutKind.Sequential)] private struct DlInfo { public IntPtr file, imageBase, symbol, symbolAddress; }
        [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl)] private static extern int dladdr(IntPtr address, out DlInfo info);
        [DllImport("/usr/lib/libSystem.B.dylib", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern IntPtr realpath(string path, [Out] byte[] resolved);
        private static string Sha(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string PathString(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) throw new NotSupportedException("dladdr image path missing");
            var bytes = new byte[1024];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte value = Marshal.ReadByte(pointer, i);
                if (value == 0) return Encoding.UTF8.GetString(bytes, 0, i);
                bytes[i] = value;
            }
            throw new NotSupportedException("dladdr path exceeds local PATH_MAX budget");
        }
        private static string Resolve(string path)
        {
            var output = new byte[1024];
            if (!Path.IsPathRooted(path) || realpath(path, output) == IntPtr.Zero) throw new NotSupportedException("actual image realpath unavailable");
            int length = Array.IndexOf(output, (byte)0);
            if (length <= 0) throw new NotSupportedException("actual image realpath missing terminator");
            return Encoding.UTF8.GetString(output, 0, length);
        }
        private static byte[] ReadExact(string path, int expectedBytes)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length != expectedBytes) throw new NotSupportedException("actual identity file length differs");
                var bytes = new byte[expectedBytes]; int offset = 0;
                while (offset < bytes.Length) { int read = stream.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
                if (stream.ReadByte() != -1) throw new NotSupportedException("identity file changed during read");
                return bytes;
            }
        }
        private static string ParseDiskUuid(byte[] bytes)
        {
            if (bytes.Length != BinaryBytes || BitConverter.ToUInt32(bytes, 0) != 0xfeedfacf || BitConverter.ToUInt32(bytes, 4) != 0x0100000c
                || BitConverter.ToUInt32(bytes, 12) != 6 || BitConverter.ToUInt32(bytes, 16) != CommandCount || BitConverter.ToUInt32(bytes, 20) != CommandBytes)
                throw new NotSupportedException("approved thin arm64 Mach-O header differs");
            int offset = HeaderBytes, end = HeaderBytes + CommandBytes, uuidCount = 0; string uuid = null;
            for (int i = 0; i < CommandCount; i++)
            {
                if (end - offset < 8) throw new NotSupportedException("Mach-O command truncated");
                uint command = BitConverter.ToUInt32(bytes, offset), size = BitConverter.ToUInt32(bytes, offset + 4);
                if (size < 8 || size % 8 != 0 || size > end - offset) throw new NotSupportedException("Mach-O command bounds differ");
                if (command == 0x1b)
                {
                    if (size != 24) throw new NotSupportedException("LC_UUID size differs");
                    uuidCount++; uuid = BitConverter.ToString(bytes, offset + 8, 16).Replace("-", "").ToLowerInvariant();
                }
                offset = checked(offset + (int)size);
            }
            if (offset != end || uuidCount != 1 || uuid != DiskUuid) throw new NotSupportedException("approved LC_UUID differs");
            return uuid;
        }
        internal static Observation Probe(IntPtr callback)
        {
            var result = new Observation();
            try
            {
                if (Application.platform != RuntimePlatform.OSXEditor || RuntimeInformation.ProcessArchitecture != Architecture.Arm64
                    || IntPtr.Size != 8 || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                    throw new NotSupportedException("diagnostic requires actual OSXEditor/Arm64/Metal process");
                string assetPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Asset));
                byte[] asset = ReadExact(assetPath, BinaryBytes);
                if (Sha(asset) != BinarySha) throw new NotSupportedException("approved imported asset SHA differs");
                byte[] meta = ReadExact(assetPath + ".meta", 1262);
                if (meta.Length > 8192) throw new NotSupportedException("importer metadata budget differs");
                result.importerMetaSha256 = Sha(meta);
                if (result.importerMetaSha256 != MetaSha) throw new NotSupportedException("approved importer metadata SHA differs");
                PluginImporter importer = AssetImporter.GetAtPath(Asset) as PluginImporter;
                if (importer == null || !importer.GetCompatibleWithEditor() || importer.GetCompatibleWithAnyPlatform() || !importer.isPreloaded
                    || importer.GetEditorData("CPU") != "ARM64" || importer.GetEditorData("OS") != "OSX")
                    throw new NotSupportedException("actual importer Editor/CPU/OS/preload differs");
                foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
                {
                    if ((int)target < 0) continue;
                    result.playerTargetsChecked++;
                    if (importer.GetCompatibleWithPlatform(target)) throw new NotSupportedException("native plugin is enabled for Player target: " + target);
                }
                result.editorOnlyImporterObserved = true;
                DlInfo info;
                if (callback == IntPtr.Zero || dladdr(callback, out info) == 0 || info.imageBase == IntPtr.Zero) throw new NotSupportedException("actual callback dladdr unavailable");
                result.dladdrPath = PathString(info.file); result.resolvedPath = Resolve(result.dladdrPath);
                ulong address = unchecked((ulong)callback.ToInt64()), imageBase = unchecked((ulong)info.imageBase.ToInt64());
                if (address < imageBase) throw new NotSupportedException("callback is below its dladdr image base");
                result.callbackRva = address - imageBase;
                if (result.callbackRva != 0xc6c) throw new NotSupportedException("approved callback RVA differs; no pointer mask/fallback");
                var before = new FileInfo(result.resolvedPath); result.fileWriteUtcTicksBefore = before.LastWriteTimeUtc.Ticks;
                byte[] actual = ReadExact(result.resolvedPath, BinaryBytes); result.fileBytes = actual.Length;
                result.fileSha256 = Sha(actual); result.diskImageUuid = ParseDiskUuid(actual);
                var after = new FileInfo(result.resolvedPath); result.fileWriteUtcTicksAfter = after.LastWriteTimeUtc.Ticks;
                DlInfo again;
                if (result.fileSha256 != BinarySha || result.fileWriteUtcTicksBefore != result.fileWriteUtcTicksAfter || before.Length != after.Length
                    || Resolve(result.dladdrPath) != result.resolvedPath || dladdr(callback, out again) == 0 || again.imageBase != info.imageBase || PathString(again.file) != result.dladdrPath)
                    throw new NotSupportedException("observed callback/path/file identity changed or SHA differs");
                result.resolvedFileMatch = true;
                result.status = "OBSERVED_DLADDR_PATH_AND_FILE_SHA_ONLY_MAPPED_IDENTITY_UNKNOWN";
                result.reason = "same callback/image base + resolved exact file SHA/LC_UUID observed; mapped UUID/content and atomic unload/file lifetime remain UNKNOWN";
            }
            catch (Exception error) { result.reason = error.GetType().Name + ": " + error.Message; }
            return result;
        }
    }
}
