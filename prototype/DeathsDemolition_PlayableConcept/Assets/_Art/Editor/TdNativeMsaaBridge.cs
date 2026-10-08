// Offline staging proposal only. Loading this prototype requires a separate ROOT grant.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DD.Art.Pipeline.EditorTools
{
    internal sealed class TdNativeMsaaBridge : IDisposable
    {
        private const string Library = "GfxPluginDDNativeMsaaPositions";
        private const int MaxContexts = 10, HeaderBytes = 128, EntryBytes = 256, Capacity = 4095, MaxBytes = 1048448;
        // Native owns ten immutable 64-byte slots for the loaded plugin lifetime. No managed
        // pointer allocation/free, slot reuse or domain-reload reset can invalidate queued event data.
        private readonly bool _optIn = Environment.GetEnvironmentVariable("DD_NATIVE_MSAA_DIAGNOSTICS") == "1";
        private IntPtr _callback;
        private string _failure;
        private Pending _pending;
        public bool OptIn => _optIn;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct ArmPayload
        {
            public uint structSize, version;
            public ulong nonce;
            public int cameraId, renderOrdinal, sourceManagedId, reserved;
            public ulong nativeRenderBuffer;
            public uint width, height, sampleCount, mip, slice, metalPixelFormat;
        }
        private sealed class Pending
        {
            public ArmPayload value;
            public IntPtr memory;
            public bool armed, disarmQueued;
            public string label, reason;
            public TdNativeMsaaIdentity.Observation identityBefore;
        }
        [Serializable] private sealed class Record
        {
            public string schema = "DD_NATIVE_MSAA_CALLBACK_LEDGER_V1";
            public string status = "UNKNOWN", reason, label, file, sha256;
            public string producerPin, nativeLoadedArtifactIdentity = "UNKNOWN";
            public TdNativeMsaaIdentity.Observation identityBefore, identityAfter;
            public string coverage = "UNKNOWN: SRP Before/AfterDrawCall delivery and all source producer encoders are not proven";
            public string positions = "Raw native entries preserve observed sample index order; no canonical/default pattern is fabricated";
            public ulong nonce;
            public int cameraId, renderOrdinal, sourceManagedId, copyResult = -1, byteCount, currentEntryCount, currentPatternEntryCount;
            public uint headerFlags, overflowCount, patternDisagreements, tokenOverflow;
            public bool armQueued, disarmQueued, disarmAcknowledged, callbackCoverageKnown = false;
        }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DD_GetAbiInfo([Out] byte[] info, uint bytes);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DD_GetMaxLedgerBytes();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint DD_GetExpectedMetalDepthFormat();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr DD_GetRenderEventAndData();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr DD_ReserveArmPayload(ref ArmPayload value, uint bytes);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int DD_CopyLedger([Out] byte[] destination, uint capacity, out uint written);
        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
        private static int I32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);
        private static ulong U64(byte[] bytes, int offset) => BitConverter.ToUInt64(bytes, offset);

        private void LoadAbi()
        {
            if (_callback != IntPtr.Zero) return;
            if (!BitConverter.IsLittleEndian || IntPtr.Size != 8 || Application.platform != RuntimePlatform.OSXEditor
                || RuntimeInformation.ProcessArchitecture != Architecture.Arm64 || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                throw new NotSupportedException("prototype requires little-endian 64-bit Metal");
            if (Marshal.SizeOf<ArmPayload>() != 64) throw new NotSupportedException("managed payload size differs");
            byte[] abi = new byte[64];
            uint[] expected = { 1, 64, HeaderBytes, EntryBytes, Capacity, MaxBytes, 1, 2, 260, 0 };
            if (DD_GetAbiInfo(abi, 64) != 0) throw new NotSupportedException("native ABI query failed");
            for (int i = 0; i < expected.Length; i++)
                if (U32(abi, i * 4) != expected[i]) throw new NotSupportedException("native ABI field differs: " + i);
            if (U32(abi, 40) != MaxContexts) throw new NotSupportedException("native immutable slot capacity differs");
            for (int i = 44; i < 64; i += 4)
                if (U32(abi, i) != 0) throw new NotSupportedException("native ABI reserved field differs");
            if (DD_GetMaxLedgerBytes() != MaxBytes || DD_GetExpectedMetalDepthFormat() != 260)
                throw new NotSupportedException("native ledger/format export differs");
            _callback = DD_GetRenderEventAndData();
            if (_callback == IntPtr.Zero) throw new NotSupportedException("native event callback missing");
        }
        private static CommandBuffer ExistingCommandBuffer(ref RenderingData data)
        {
            FieldInfo field = typeof(RenderingData).GetField("commandBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(CommandBuffer)) throw new NotSupportedException("pinned commandBuffer field differs");
            return field.GetValue(data) as CommandBuffer ?? throw new NotSupportedException("actual URP commandBuffer missing");
        }
        public void Arm(RTHandle source, Camera camera, string label, int ordinal, ref RenderingData data)
        {
            if (!_optIn) return;
            // This path is invoked only for the same five existing MSAA readback labels per pose.
            AbandonPending();
            _pending = new Pending { label = label, reason = _failure };
            if (_failure != null) return;
            try
            {
                RenderTexture rt = source != null ? source.rt : null;
                if (rt == null || !rt.IsCreated() || rt.width != 480 || rt.height != 270 || rt.antiAliasing != 4 || !rt.bindTextureMS
                    || rt.depthStencilFormat != GraphicsFormat.D32_SFloat_S8_UInt)
                    throw new NotSupportedException("actual source is not created 480x270 bindMS D32S8 MSAA4");
                FieldInfo field = typeof(ScriptableRenderer).GetField("useRenderPassEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool) || (bool)field.GetValue(data.cameraData.renderer))
                    throw new NotSupportedException("native render-pass mode is unsupported by this staging bridge");
                LoadAbi();
                _pending.identityBefore = TdNativeMsaaIdentity.Probe(_callback);
                if (!_pending.identityBefore.resolvedFileMatch) throw new NotSupportedException(_pending.identityBefore.reason);
                IntPtr native = rt.depthBuffer.GetNativeRenderBufferPtr();
                if (native == IntPtr.Zero) throw new NotSupportedException("actual depth RenderBuffer native pointer missing");
                _pending.value = new ArmPayload { structSize = 64, version = 1, nonce = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0),
                    cameraId = camera.GetInstanceID(), renderOrdinal = ordinal, sourceManagedId = rt.GetInstanceID(),
                    nativeRenderBuffer = unchecked((ulong)native.ToInt64()), width = 480, height = 270, sampleCount = 4, metalPixelFormat = 260 };
                CommandBuffer cmd = ExistingCommandBuffer(ref data);
                _pending.memory = DD_ReserveArmPayload(ref _pending.value, 64);
                if (_pending.memory == IntPtr.Zero) throw new NotSupportedException("native immutable slot/nonce budget rejected; no reset/retry");
                cmd.IssuePluginEventAndData(_callback, 1, _pending.memory);
                _pending.armed = true;
            }
            catch (Exception error)
            {
                _failure = _pending.reason = error.GetType().Name + ": " + error.Message;
            }
        }
        public void Disarm(ref RenderingData data)
        {
            if (!_optIn || _pending == null || !_pending.armed || _pending.disarmQueued) return;
            try
            {
                ExistingCommandBuffer(ref data).IssuePluginEventAndData(_callback, 2, _pending.memory);
                _pending.disarmQueued = true;
            }
            catch (Exception error) { _failure = _pending.reason = error.GetType().Name + ": " + error.Message; }
        }
        public void ReadLedger(string directory, string label, int ordinal, int cameraId)
        {
            if (!_optIn) return;
            Pending pending = _pending;
            var record = new Record { label = label, renderOrdinal = ordinal, cameraId = cameraId,
                producerPin = Environment.GetEnvironmentVariable("DD_TD_SOURCE_PIN"), reason = pending == null ? "preproducer arm did not execute" : pending.reason };
            if (pending != null)
            {
                record.nonce = pending.value.nonce; record.sourceManagedId = pending.value.sourceManagedId;
                record.armQueued = pending.armed; record.disarmQueued = pending.disarmQueued;
                record.identityBefore = pending.identityBefore;
            }
            try
            {
                if (pending == null || !pending.armed || !pending.disarmQueued) throw new NotSupportedException(record.reason ?? "arm/disarm event not queued");
                ArmPayload p = pending.value;
                record.nonce = p.nonce; record.sourceManagedId = p.sourceManagedId; record.armQueued = pending.armed; record.disarmQueued = pending.disarmQueued;
                if (pending.label != label || p.renderOrdinal != ordinal || p.cameraId != cameraId) throw new NotSupportedException("managed render identity differs");
                record.identityAfter = TdNativeMsaaIdentity.Probe(_callback);
                if (!record.identityAfter.resolvedFileMatch || record.identityBefore == null
                    || record.identityAfter.resolvedPath != record.identityBefore.resolvedPath || record.identityAfter.fileSha256 != record.identityBefore.fileSha256)
                    throw new NotSupportedException("pre/post callback file observation differs: " + record.identityAfter.reason);
                record.nativeLoadedArtifactIdentity = record.identityAfter.status;
                byte[] bytes = new byte[MaxBytes]; uint written;
                record.copyResult = DD_CopyLedger(bytes, MaxBytes, out written);
                if (record.copyResult != 0) throw new NotSupportedException("no completed CPU ledger copy; result=" + record.copyResult);
                uint count = U32(bytes, 20);
                if (written < HeaderBytes || written > MaxBytes || U32(bytes, 0) != 0x44444d53 || U32(bytes, 4) != 1
                    || U32(bytes, 8) != HeaderBytes || U32(bytes, 12) != EntryBytes || U32(bytes, 16) != Capacity
                    || count > Capacity || written != HeaderBytes + count * EntryBytes || U32(bytes, 104) != 0 || U32(bytes, 108) != 0)
                    throw new NotSupportedException("ledger ABI/bounds/inactive/coverage invariant differs");
                if (U64(bytes, 32) != p.nonce || U64(bytes, 40) != p.nonce || U64(bytes, 48) != p.nonce
                    || I32(bytes, 64) != p.cameraId || I32(bytes, 68) != p.renderOrdinal)
                    throw new NotSupportedException("render-thread arm/disarm acknowledgment differs");
                // Record completion; native immutable storage remains reserved for the loaded plugin lifetime.
                record.disarmAcknowledged = true;
                bool armAck = false, disarmAck = false;
                for (int i = 0; i < count; i++)
                {
                    int offset = HeaderBytes + i * EntryBytes;
                    if (U64(bytes, offset + 8) != p.nonce) continue;
                    if (I32(bytes, offset + 16) != p.cameraId || I32(bytes, offset + 20) != p.renderOrdinal || I32(bytes, offset + 228) != p.sourceManagedId)
                        throw new NotSupportedException("current nonce entry camera/ordinal/source identity differs");
                    record.currentEntryCount++;
                    uint ev = U32(bytes, offset + 24), status = U32(bytes, offset + 28), flags = U32(bytes, offset + 156);
                    armAck |= ev == 0x10001 && status == 1; disarmAck |= ev == 0x10002 && status == 2;
                    if ((status == 3 || status == 4) && (flags & 3) == 3) record.currentPatternEntryCount++;
                }
                if (!armAck || !disarmAck) throw new NotSupportedException("current nonce ACK entries missing");
                record.headerFlags = U32(bytes, 28); record.overflowCount = U32(bytes, 24);
                record.patternDisagreements = U32(bytes, 96); record.tokenOverflow = U32(bytes, 100);
                record.file = label + ".native-msaa-ledger";
                using (var stream = File.Create(Path.Combine(directory, record.file))) stream.Write(bytes, 0, checked((int)written));
                record.byteCount = checked((int)written);
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    record.sha256 = BitConverter.ToString(sha.ComputeHash(bytes, 0, record.byteCount)).Replace("-", "").ToLowerInvariant();
                record.status = "CPU_LEDGER_RECORDED_COVERAGE_UNKNOWN";
                record.reason = "CPU ledger copy completed; observations do not establish callback/all-producer coverage or loaded artifact identity";
            }
            catch (Exception error) { record.reason = error.GetType().Name + ": " + error.Message; }
            string json = JsonUtility.ToJson(record, true);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024) throw new InvalidOperationException("native sidecar budget exceeded");
            File.WriteAllText(Path.Combine(directory, label + "-native-msaa.json"), json);
            AbandonPending();
        }
        private void AbandonPending() => _pending = null;
        public void Dispose() => AbandonPending();
    }
}
