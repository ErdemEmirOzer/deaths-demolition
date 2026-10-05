#!/usr/bin/env python3
"""WP-089 (C-00 05.10) · Codex delivery check — run by the owner session BEFORE importing a Codex export into Unity.

Validates one asset-production package version against CODEX-SISTEM-R2 §4.1 (manifest + SHA256SUMS) and §4.2 (format
rules), plus LICENSES.csv and the Higgsfield ledger (§7). Read-only: it never writes into the package.

usage: python3 tools/codex_manifest_check.py <K/asset-production/AP-0NN> [--version vNNN] [--manifest PATH] [--json]
                                             [--strict]
  --version   default: the highest exports/vNNN of the package
  --manifest  default: manifest/AP0NN-manifest-vNNN.json
  --strict    warnings fail too
exit: 0 PASS (warnings listed) · 1 FAIL (errors, or warnings with --strict) · 64 usage / package not found

Checks (E = error, W = warning):
  manifest   JSON object; package = folder name (AP-0NN), version = vNNN, date YYYY-MM-DD, producer non-empty
             ("codex/<model>", else W), items non-empty, licenses path. Each item: path relative under exports/vNNN/ and
             present, sha256 = the file's, type ∈ fbx|texture|anim|audio|ui, role non-empty, specs object (W if absent),
             source present in the package if named (W). Duplicate paths E. A file under exports/vNNN that no item lists
             is E (except SHA256SUMS, anim-events.json, README*).
  SHA256SUMS manifest/SHA256SUMS-vNNN, manifest/SHA256SUMS or exports/vNNN/SHA256SUMS ("<sha256>  <path>", paths
             relative to the package or to the file); every item listed with the same hash (E); extra entries W.
  fbx        binary FBX, version 7400 (E); name <Asset>_<Part>_LOD<n> or COL_<…> (E); LOD0/1/2 per <Asset>_<Part> (E);
             GlobalSettings UpAxis = 1 (Y up), else W.
  anim       binary FBX 7400 (E); exports/vNNN/anim-events.json present and valid JSON (E).
  texture    .png (E); suffix _BaseColor|_Normal|_ORM|_Emission|_Mask (E); power-of-two sides ≤ 8192 (E), not 2048/4096 W;
             _BaseColor/_Normal/_ORM colour (RGB/RGBA) not grey (E).
  audio      .wav PCM (E); 48000 Hz (E); 24-bit (E); name <Category>_<Weapon>_<Layer>_<nn>.wav (E); true peak (4×
             oversampled) ≤ −1.0 dBTP (E); leading / trailing silence (< −60 dBFS) ≤ 5 ms (E); validation/ empty → W.
  ui         .png with alpha (E) or .svg source; icon_* → lowercase icon_<function>, square 64/128/256 (E); other PNG W.
  licenses   LICENSES.csv present with columns file, source/url, license, date, usage (TR or EN names); every row has a
             license that is not unknown/belirsiz/tbd/? (E).
  ledger     if the package has 03-higgsfield/ or an item names Higgsfield: 03-higgsfield/ledger.csv with columns date,
             model, count, credit, output (E).
"""
import argparse
import csv
import datetime
import hashlib
import json
import re
import struct
import sys
import zlib
from pathlib import Path

TYPES = ("fbx", "texture", "anim", "audio", "ui")
UNLISTED_OK = ("SHA256SUMS", "anim-events.json")
FBX_MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"
FBX_VERSION = 7400
TEXTURE_SUFFIXES = ("BaseColor", "Normal", "ORM", "Emission", "Mask")
COLOUR_TEXTURES = ("BaseColor", "Normal", "ORM")
ICON_SIZES = (64, 128, 256)
AUDIO_RATE, AUDIO_BITS = 48000, 24
PEAK_MAX_DBTP, SILENCE_DBFS, SILENCE_MAX_MS = -1.0, -60.0, 5.0
FBX_NAME = re.compile(r"^(?:COL_[A-Za-z0-9_]+|(?P<base>[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+)_LOD(?P<lod>[0-9]))$")
WAV_NAME = re.compile(r"^[A-Za-z0-9]+_[A-Za-z0-9]+_[A-Za-z0-9]+_\d{2}\.wav$")
ICON_NAME = re.compile(r"^icon_[a-z0-9]+(?:_[a-z0-9]+)*$")
UNKNOWN_LICENSES = ("", "unknown", "belirsiz", "bilinmiyor", "tbd", "?", "n/a")
LICENSE_COLUMNS = {"file": ("dosya", "file", "path"), "source": ("kaynak", "source", "url", "kaynak url"),
                   "license": ("lisans", "license"), "date": ("tarih", "date"), "usage": ("kullanım", "kullanim", "usage")}
LEDGER_COLUMNS = {"date": ("tarih", "date"), "model": ("model",), "count": ("adet", "count"),
                  "credit": ("kredi", "credit", "credits"), "output": ("çıktı", "cikti", "çıktı yolu", "output", "path")}


class Report:
    def __init__(self):
        self.errors, self.warnings = [], []

    def error(self, where, msg):
        self.errors.append({"path": str(where), "message": msg})

    def warn(self, where, msg):
        self.warnings.append({"path": str(where), "message": msg})


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


# --- FBX (binary, 7.x) ------------------------------------------------------------------------------------------------
def fbx_header(path):
    with open(path, "rb") as f:
        head = f.read(27)
    if len(head) < 27 or not head.startswith(FBX_MAGIC):
        return None
    return struct.unpack_from("<I", head, 23)[0]


def _fbx_props(data, pos, count):
    props = []
    for _ in range(count):
        code = chr(data[pos]); pos += 1
        if code in "YCIFDL":
            fmt = {"Y": "<h", "C": "<?", "I": "<i", "F": "<f", "D": "<d", "L": "<q"}[code]
            props.append(struct.unpack_from(fmt, data, pos)[0]); pos += struct.calcsize(fmt)
        elif code in "SR":
            n = struct.unpack_from("<I", data, pos)[0]; pos += 4
            raw = data[pos:pos + n]; pos += n
            props.append(raw.decode("utf-8", "replace") if code == "S" else raw)
        elif code in "fdlib":
            n, enc, clen = struct.unpack_from("<III", data, pos); pos += 12
            props.append(None); pos += clen   # arrays are not needed here
        else:
            raise ValueError("unknown FBX property type %r" % code)
    return props, pos


def _fbx_nodes(data, pos, end, wide):
    """[(name, props, children)] of the node records in data[pos:end] (7.4: 32-bit offsets, 7.5+: 64-bit)."""
    nodes, head = [], (25 if wide else 13)
    while pos + head <= end:
        if wide:
            end_off, nprops, _plen = struct.unpack_from("<QQQ", data, pos)
        else:
            end_off, nprops, _plen = struct.unpack_from("<III", data, pos)
        if end_off == 0:
            break
        nlen = data[pos + head - 1]
        name = data[pos + head:pos + head + nlen].decode("ascii", "replace")
        props, ppos = _fbx_props(data, pos + head + nlen, nprops)
        children = _fbx_nodes(data, ppos, end_off, wide) if ppos < end_off else []
        nodes.append((name, props, children))
        pos = end_off
    return nodes


def fbx_global_settings(path):
    """{'UpAxis': …, 'UnitScaleFactor': …} from GlobalSettings/Properties70, or None when it cannot be read."""
    data = Path(path).read_bytes()
    version = struct.unpack_from("<I", data, 23)[0]
    try:
        for name, _props, children in _fbx_nodes(data, 27, len(data), version >= 7500):
            if name != "GlobalSettings":
                continue
            out = {}
            for cname, _cp, grand in children:
                if cname == "Properties70":
                    for pname, pprops, _ in grand:
                        if pname == "P" and len(pprops) >= 5 and pprops[0] in ("UpAxis", "UnitScaleFactor"):
                            out[pprops[0]] = pprops[4]
            return out
    except (ValueError, struct.error, IndexError):
        return None
    return {}


# --- PNG --------------------------------------------------------------------------------------------------------------
def png_info(path):
    """(width, height, bit_depth, colour_type, has_trns) or None."""
    data = Path(path).read_bytes()
    if not data.startswith(b"\x89PNG\r\n\x1a\n") or data[12:16] != b"IHDR":
        return None
    w, h, depth, ctype = struct.unpack_from(">IIBB", data, 16)
    pos, trns = 8, False
    while pos + 8 <= len(data):
        n = struct.unpack_from(">I", data, pos)[0]
        kind = data[pos + 4:pos + 8]
        if kind == b"tRNS":
            trns = True
        if kind == b"IEND":
            break
        pos += 12 + n
    return w, h, depth, ctype, trns


def power_of_two(n):
    return n > 0 and (n & (n - 1)) == 0


# --- WAV --------------------------------------------------------------------------------------------------------------
PCM_GUID_TAIL = b"\x00\x00\x00\x00\x10\x00\x80\x00\x00\xaa\x00\x38\x9b\x71"


def wav_read(path):
    """(format_ok, rate, bits, channels, samples float ndarray [frames, ch] or None)."""
    data = Path(path).read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        return False, None, None, None, None
    pos, fmt, raw = 12, None, None
    while pos + 8 <= len(data):
        cid, n = data[pos:pos + 4], struct.unpack_from("<I", data, pos + 4)[0]
        body = data[pos + 8:pos + 8 + n]
        if cid == b"fmt ":
            fmt = body
        elif cid == b"data":
            raw = body
        pos += 8 + n + (n & 1)
    if fmt is None or raw is None or len(fmt) < 16:
        return False, None, None, None, None
    tag, ch, rate, _br, _align, bits = struct.unpack_from("<HHIIHH", fmt, 0)
    pcm = tag == 1 or (tag == 0xFFFE and len(fmt) >= 40 and fmt[24:26] == b"\x01\x00" and fmt[26:40] == PCM_GUID_TAIL)
    if not pcm or bits not in (16, 24, 32) or ch < 1:
        return pcm, rate, bits, ch, None
    import numpy as np
    width = bits // 8
    frames = len(raw) // (width * ch)
    b = np.frombuffer(raw[:frames * width * ch], dtype=np.uint8).reshape(-1, width)
    if bits == 24:
        v = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        v = np.where(v >= 1 << 23, v - (1 << 24), v)
    else:
        v = np.frombuffer(b.tobytes(), dtype="<i2" if bits == 16 else "<i4").astype(np.int64)
    samples = (v.astype(np.float64) / float(1 << (bits - 1))).reshape(frames, ch)
    return True, rate, bits, ch, samples


def true_peak_dbtp(samples, factor=4):
    """dBTP with factor× oversampling (FFT zero-padding, per channel)."""
    import numpy as np
    peak = 0.0
    for c in range(samples.shape[1]):
        x = samples[:, c]
        n = len(x)
        if n == 0:
            continue
        spec = np.fft.rfft(x)
        up = np.fft.irfft(spec, n * factor) * factor
        peak = max(peak, float(np.max(np.abs(up))), float(np.max(np.abs(x))))
    return -float("inf") if peak <= 0 else 20.0 * np.log10(peak)


def silence_ms(samples, rate):
    """(leading ms, trailing ms) below SILENCE_DBFS on every channel."""
    import numpy as np
    level = np.max(np.abs(samples), axis=1) if samples.size else np.zeros(0)
    loud = np.nonzero(level > 10 ** (SILENCE_DBFS / 20.0))[0]
    if len(loud) == 0:
        return len(level) * 1000.0 / rate, len(level) * 1000.0 / rate
    return loud[0] * 1000.0 / rate, (len(level) - 1 - loud[-1]) * 1000.0 / rate


# --- CSV headers ------------------------------------------------------------------------------------------------------
def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as f:
        rows = list(csv.reader(f))
    return [h.strip().lower() for h in rows[0]] if rows else [], rows[1:]


def column_map(header, wanted):
    """{key: index} for each wanted key whose alias is in header; missing keys absent."""
    out = {}
    for key, aliases in wanted.items():
        for i, h in enumerate(header):
            if h in aliases:
                out[key] = i
                break
    return out


# --- the check --------------------------------------------------------------------------------------------------------
def latest_version(pkg):
    versions = sorted(p.name for p in (pkg / "exports").glob("v[0-9][0-9][0-9]") if p.is_dir())
    return versions[-1] if versions else None


def find_sums(pkg, version):
    for c in (pkg / "manifest" / ("SHA256SUMS-" + version), pkg / "manifest" / "SHA256SUMS",
              pkg / "exports" / version / "SHA256SUMS"):
        if c.is_file():
            return c
    return None


def check(pkg, version=None, manifest_path=None):
    pkg = Path(pkg).resolve()
    rep = Report()
    version = version or latest_version(pkg)
    if not version or not re.fullmatch(r"v\d{3}", version):
        rep.error(pkg / "exports", "no exports/vNNN version found (or --version not vNNN)")
        return rep, version
    export_dir = pkg / "exports" / version
    if not export_dir.is_dir():
        rep.error(export_dir, "missing export folder")
        return rep, version
    mpath = Path(manifest_path) if manifest_path else pkg / "manifest" / ("%s-manifest-%s.json" % (pkg.name.replace("-", ""), version))
    if not mpath.is_file():
        rep.error(mpath, "manifest missing")
        return rep, version
    try:
        m = json.loads(mpath.read_text(encoding="utf-8"))
    except (ValueError, UnicodeDecodeError) as e:
        rep.error(mpath, "manifest is not valid JSON: %s" % e)
        return rep, version
    if not isinstance(m, dict):
        rep.error(mpath, "manifest must be a JSON object")
        return rep, version

    # header fields
    if m.get("package") != pkg.name:
        rep.error(mpath, "package %r != folder %r" % (m.get("package"), pkg.name))
    if m.get("version") != version:
        rep.error(mpath, "version %r != %r" % (m.get("version"), version))
    try:
        datetime.date.fromisoformat(str(m.get("date")))
    except ValueError:
        rep.error(mpath, "date %r is not YYYY-MM-DD" % m.get("date"))
    producer = m.get("producer")
    if not isinstance(producer, str) or not producer.strip():
        rep.error(mpath, "producer missing")
    elif not producer.startswith("codex/"):
        rep.warn(mpath, "producer %r is not 'codex/<model>'" % producer)
    items = m.get("items")
    if not isinstance(items, list) or not items:
        rep.error(mpath, "items must be a non-empty list")
        items = []

    # items
    listed, by_type = {}, {t: [] for t in TYPES}
    prefix = "exports/%s/" % version
    for i, it in enumerate(items):
        where = "%s items[%d]" % (mpath.name, i)
        if not isinstance(it, dict):
            rep.error(where, "item must be an object")
            continue
        rel = it.get("path")
        if not isinstance(rel, str) or rel.startswith("/") or ".." in Path(rel).parts or not rel.startswith(prefix):
            rep.error(where, "path %r must be relative under %s" % (rel, prefix))
            continue
        if rel in listed:
            rep.error(rel, "listed twice")
            continue
        f = pkg / rel
        if not f.is_file():
            rep.error(rel, "file missing")
            continue
        listed[rel] = f
        digest = sha256(f)
        if it.get("sha256") != digest:
            rep.error(rel, "sha256 %r != file %s" % (it.get("sha256"), digest))
        t = it.get("type")
        if t not in TYPES:
            rep.error(rel, "type %r not in %s" % (t, "|".join(TYPES)))
        else:
            by_type[t].append(f)
        if not isinstance(it.get("role"), str) or not it.get("role").strip():
            rep.error(rel, "role missing")
        if not isinstance(it.get("specs"), dict):
            rep.warn(rel, "specs object missing")
        src = it.get("source")
        if src is not None and (not isinstance(src, str) or not (pkg / src).exists()):
            rep.warn(rel, "source %r not found in the package" % src)
    for f in sorted(p for p in export_dir.rglob("*") if p.is_file()):
        rel = f.relative_to(pkg).as_posix()
        if rel not in listed and f.name not in UNLISTED_OK and f.name != ".DS_Store" and not f.name.startswith("README"):
            rep.error(rel, "file in %s but not in the manifest" % prefix)

    # SHA256SUMS
    sums = find_sums(pkg, version)
    if sums is None:
        rep.error(pkg / "manifest", "SHA256SUMS missing (manifest/SHA256SUMS[-%s] or %sSHA256SUMS)" % (version, prefix))
    else:
        entries = {}
        for n, line in enumerate(sums.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            mm = re.fullmatch(r"([0-9a-f]{64}) [ *](.+)", line.strip())
            if not mm:
                rep.error(sums.name, "line %d is not '<sha256>  <path>'" % n)
                continue
            p = mm.group(2)
            cand = (pkg / p) if (pkg / p).exists() else (sums.parent / p)
            try:
                key = cand.resolve().relative_to(pkg).as_posix()
            except ValueError:
                key = p
            entries[key] = mm.group(1)
        for rel, f in listed.items():
            if rel not in entries:
                rep.error(rel, "not in %s" % sums.name)
            elif entries[rel] != sha256(f):
                rep.error(rel, "%s hash differs from the file" % sums.name)
        for key in entries:
            if key not in listed and Path(key).name not in UNLISTED_OK:
                rep.warn(key, "in %s but not in the manifest" % sums.name)

    check_fbx(rep, pkg, by_type["fbx"], anim=False)
    check_fbx(rep, pkg, by_type["anim"], anim=True)
    if by_type["anim"]:
        events = export_dir / "anim-events.json"
        if not events.is_file():
            rep.error(events.relative_to(pkg), "anim items need exports/%s/anim-events.json" % version)
        else:
            try:
                json.loads(events.read_text(encoding="utf-8"))
            except ValueError as e:
                rep.error(events.relative_to(pkg), "not valid JSON: %s" % e)
    check_textures(rep, pkg, by_type["texture"])
    check_audio(rep, pkg, by_type["audio"])
    check_ui(rep, pkg, by_type["ui"])
    check_licenses(rep, pkg, m.get("licenses"))
    check_ledger(rep, pkg, m)
    return rep, version


def check_fbx(rep, pkg, files, anim):
    lods = {}
    for f in files:
        rel = f.relative_to(pkg).as_posix()
        if f.suffix.lower() != ".fbx":
            rep.error(rel, "must be .fbx")
            continue
        v = fbx_header(f)
        if v is None:
            rep.error(rel, "not a binary FBX")
            continue
        if v != FBX_VERSION:
            rep.error(rel, "FBX version %d, need %d (7.4)" % (v, FBX_VERSION))
        gs = fbx_global_settings(f)
        if gs is None:
            rep.warn(rel, "GlobalSettings could not be read")
        elif gs.get("UpAxis") not in (None, 1):
            rep.warn(rel, "GlobalSettings UpAxis=%s (Y up = 1)" % gs.get("UpAxis"))
        if anim:
            continue
        mm = FBX_NAME.match(f.stem)
        if not mm:
            rep.error(rel, "name must be <Asset>_<Part>_LOD<n> or COL_<…>")
        elif mm.group("base"):
            lods.setdefault((f.parent, mm.group("base")), set()).add(int(mm.group("lod")))
    for (_parent, base), have in sorted(lods.items(), key=lambda kv: kv[0][1]):
        missing = [n for n in (0, 1, 2) if n not in have]
        if missing:
            rep.error(base, "LOD set incomplete: missing " + ", ".join("LOD%d" % n for n in missing))


def check_textures(rep, pkg, files):
    for f in files:
        rel = f.relative_to(pkg).as_posix()
        if f.suffix.lower() != ".png":
            rep.error(rel, "texture must be .png")
            continue
        suffix = f.stem.rsplit("_", 1)[-1] if "_" in f.stem else ""
        if suffix not in TEXTURE_SUFFIXES:
            rep.error(rel, "suffix must be one of _" + ", _".join(TEXTURE_SUFFIXES))
        info = png_info(f)
        if info is None:
            rep.error(rel, "not a PNG")
            continue
        w, h, _depth, ctype, _trns = info
        if not (power_of_two(w) and power_of_two(h)) or max(w, h) > 8192:
            rep.error(rel, "size %dx%d is not power-of-two (≤ 8192)" % (w, h))
        elif (w, h) not in ((2048, 2048), (4096, 4096)):
            rep.warn(rel, "size %dx%d (rule: 2048/4096 unless the task says otherwise)" % (w, h))
        if suffix in COLOUR_TEXTURES and ctype not in (2, 6):
            rep.error(rel, "_%s must be RGB/RGBA (PNG colour type %d)" % (suffix, ctype))


def check_audio(rep, pkg, files):
    for f in files:
        rel = f.relative_to(pkg).as_posix()
        if f.suffix.lower() != ".wav":
            rep.error(rel, "audio must be .wav")
            continue
        if not WAV_NAME.match(f.name):
            rep.error(rel, "name must be <Category>_<Weapon>_<Layer>_<nn>.wav")
        ok, rate, bits, _ch, samples = wav_read(f)
        if not ok:
            rep.error(rel, "not a PCM WAV")
            continue
        if rate != AUDIO_RATE:
            rep.error(rel, "sample rate %s, need %d" % (rate, AUDIO_RATE))
        if bits != AUDIO_BITS:
            rep.error(rel, "bit depth %s, need %d" % (bits, AUDIO_BITS))
        if samples is None or not rate:
            continue
        tp = true_peak_dbtp(samples)
        if tp > PEAK_MAX_DBTP:
            rep.error(rel, "true peak %.2f dBTP > %.1f" % (tp, PEAK_MAX_DBTP))
        lead, tail = silence_ms(samples, rate)
        if lead > SILENCE_MAX_MS or tail > SILENCE_MAX_MS:
            rep.error(rel, "silence lead %.1f ms / tail %.1f ms (≤ %.0f ms)" % (lead, tail, SILENCE_MAX_MS))
    if files and not any(p.is_file() for p in (pkg / "validation").rglob("*")):
        rep.warn("validation/", "audio delivered without a measurement table under validation/")


def check_ui(rep, pkg, files):
    for f in files:
        rel = f.relative_to(pkg).as_posix()
        if f.suffix.lower() == ".svg":
            continue
        if f.suffix.lower() != ".png":
            rep.error(rel, "UI must be .png (or .svg source)")
            continue
        info = png_info(f)
        if info is None:
            rep.error(rel, "not a PNG")
            continue
        w, h, _depth, ctype, trns = info
        if ctype not in (4, 6) and not trns:
            rep.error(rel, "UI PNG must be transparent (alpha channel)")
        if f.stem.startswith("icon_"):
            if not ICON_NAME.match(f.stem):
                rep.error(rel, "icon name must be lowercase icon_<function>")
            if w != h or w not in ICON_SIZES:
                rep.error(rel, "icon %dx%d, need square %s px" % (w, h, "/".join(map(str, ICON_SIZES))))
        else:
            rep.warn(rel, "UI image not named icon_<function>")


def check_licenses(rep, pkg, licenses):
    if not isinstance(licenses, str) or not licenses:
        rep.error("manifest", "licenses path missing")
        return
    path = pkg / licenses
    if not path.is_file():
        rep.error(licenses, "LICENSES.csv missing")
        return
    header, rows = read_csv(path)
    cols = column_map(header, LICENSE_COLUMNS)
    missing = [k for k in LICENSE_COLUMNS if k not in cols]
    if missing:
        rep.error(licenses, "columns missing: " + ", ".join(missing))
        return
    for n, row in enumerate(rows, 2):
        if not any(c.strip() for c in row):
            continue
        lic = row[cols["license"]].strip().lower() if cols["license"] < len(row) else ""
        if lic in UNKNOWN_LICENSES:
            rep.error(licenses, "row %d: license %r is not usable (§7: no unclear licence)" % (n, lic))


def check_ledger(rep, pkg, manifest):
    used = (pkg / "03-higgsfield").is_dir() or "higgsfield" in json.dumps(manifest).lower()
    if not used:
        return
    path = pkg / "03-higgsfield" / "ledger.csv"
    if not path.is_file():
        rep.error("03-higgsfield/ledger.csv", "Higgsfield used but the ledger is missing")
        return
    header, _rows = read_csv(path)
    missing = [k for k in LEDGER_COLUMNS if k not in column_map(header, LEDGER_COLUMNS)]
    if missing:
        rep.error("03-higgsfield/ledger.csv", "columns missing: " + ", ".join(missing))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("package")
    ap.add_argument("--version")
    ap.add_argument("--manifest")
    ap.add_argument("--json", action="store_true")
    ap.add_argument("--strict", action="store_true")
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:
        return 0 if e.code == 0 else 64
    pkg = Path(args.package)
    if not pkg.is_dir():
        print("package folder not found: %s" % pkg, file=sys.stderr)
        return 64
    if args.version and not re.fullmatch(r"v\d{3}", args.version):
        print("--version must be vNNN", file=sys.stderr)
        return 64
    rep, version = check(pkg, args.version, args.manifest)
    failed = bool(rep.errors) or (args.strict and bool(rep.warnings))
    result = "FAIL" if failed else "PASS"
    if args.json:
        print(json.dumps({"package": pkg.resolve().name, "version": version, "result": result,
                          "errors": rep.errors, "warnings": rep.warnings}, ensure_ascii=False, indent=2))
    else:
        for e in rep.errors:
            print("ERROR %s: %s" % (e["path"], e["message"]))
        for w in rep.warnings:
            print("WARN  %s: %s" % (w["path"], w["message"]))
        print("codex-manifest-check %s %s: %s errors=%d warnings=%d" % (pkg.resolve().name, version, result,
                                                                        len(rep.errors), len(rep.warnings)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
