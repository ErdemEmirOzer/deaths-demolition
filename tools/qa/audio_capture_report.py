#!/usr/bin/env python3
"""WP-065 post-mixer WAV report: BS.1770 loudness, peaks, phases and evidence gates."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import wave
from pathlib import Path

import numpy as np
from scipy.signal import lfilter


PRE_B = np.array([1.53512485958697, -2.69169618940638, 1.19839281085285])
PRE_A = np.array([1.0, -1.69065929318241, 0.73248077421585])
RLB_B = np.array([1.0, -2.0, 1.0])
RLB_A = np.array([1.0, -1.99004745483398, 0.99007225036621])


def read_wav(path: Path) -> tuple[np.ndarray, int]:
    with wave.open(str(path), "rb") as wav:
        channels = wav.getnchannels()
        rate = wav.getframerate()
        width = wav.getsampwidth()
        frames = wav.getnframes()
        if width != 2:
            raise ValueError(f"expected 16-bit PCM, got {width * 8}-bit")
        raw = wav.readframes(frames)
    data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    return data.reshape(-1, channels), rate


def k_energy(data: np.ndarray, rate: int) -> np.ndarray:
    if rate != 48000:
        raise ValueError(f"BS.1770 coefficient set requires 48000 Hz, got {rate}")
    energy = np.zeros(data.shape[0], dtype=np.float32)
    for channel in range(data.shape[1]):
        weighted = lfilter(PRE_B, PRE_A, data[:, channel]).astype(np.float32)
        weighted = lfilter(RLB_B, RLB_A, weighted).astype(np.float32)
        energy += weighted * weighted
    return energy


def window_means(energy: np.ndarray, rate: int, seconds: float, hop_seconds: float) -> np.ndarray:
    window = max(1, int(round(seconds * rate)))
    hop = max(1, int(round(hop_seconds * rate)))
    if energy.size < window:
        padded = np.zeros(window, dtype=np.float32)
        padded[: energy.size] = energy
        energy = padded
    cumulative = np.empty(energy.size + 1, dtype=np.float64)
    cumulative[0] = 0.0
    np.cumsum(energy, dtype=np.float64, out=cumulative[1:])
    starts = np.arange(0, energy.size - window + 1, hop, dtype=np.int64)
    return (cumulative[starts + window] - cumulative[starts]) / window


def to_lufs(mean_square: np.ndarray | float) -> np.ndarray | float:
    return -0.691 + 10.0 * np.log10(np.maximum(mean_square, 1e-20))


def integrated_lufs(energy: np.ndarray, rate: int) -> float:
    means = window_means(energy, rate, 0.4, 0.1)
    loudness = to_lufs(means)
    absolute = loudness > -70.0
    if not np.any(absolute):
        return float("-inf")
    relative_gate = float(to_lufs(np.mean(means[absolute])) - 10.0)
    gated = absolute & (loudness > relative_gate)
    return float(to_lufs(np.mean(means[gated]))) if np.any(gated) else float("-inf")


def lra(energy: np.ndarray, rate: int) -> float | None:
    if energy.size < 3 * rate:
        return None
    means = window_means(energy, rate, 3.0, 1.0)
    loudness = to_lufs(means)
    absolute = loudness > -70.0
    if not np.any(absolute):
        return None
    relative_gate = float(to_lufs(np.mean(means[absolute])) - 20.0)
    gated = loudness[absolute & (loudness > relative_gate)]
    if gated.size < 2:
        return 0.0
    return float(np.percentile(gated, 95) - np.percentile(gated, 10))


def true_peak(data: np.ndarray) -> float:
    peak = float(np.max(np.abs(data))) if data.size else 0.0
    # Four-point Catmull-Rom at quarter-sample positions, matching the runtime meter.
    chunk = 500_000
    for start in range(0, max(0, data.shape[0] - 1), chunk):
        lo = max(0, start - 1)
        hi = min(data.shape[0], start + chunk + 3)
        x = data[lo:hi]
        if x.shape[0] < 2:
            continue
        a = np.vstack((x[0:1], x[:-1]))
        b = x
        c = np.vstack((x[1:], x[-1:]))
        d = np.vstack((x[2:], x[-1:], x[-1:]))
        for fraction in (0.25, 0.5, 0.75):
            t = fraction
            values = 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t)
            peak = max(peak, float(np.max(np.abs(values))))
    return peak


def db(value: float) -> float | None:
    return None if value <= 1e-12 else 20.0 * math.log10(value)


def metrics(data: np.ndarray, energy: np.ndarray, rate: int) -> dict[str, float | None]:
    momentary = to_lufs(window_means(energy, rate, 0.4, 0.1))
    short_term = to_lufs(window_means(energy, rate, 3.0, 1.0)) if energy.size >= 3 * rate else np.array([])
    return {
        "seconds": data.shape[0] / rate,
        "lufs_i": finite_or_none(integrated_lufs(energy, rate)),
        "lufs_m_max": finite_or_none(float(np.max(momentary))),
        "lufs_s_max": finite_or_none(float(np.max(short_term))) if short_term.size else None,
        "lra_lu": lra(energy, rate),
        "sample_peak_dbfs": db(float(np.max(np.abs(data))) if data.size else 0.0),
        "true_peak_dbtp": db(true_peak(data)),
    }


def finite_or_none(value: float) -> float | None:
    return value if math.isfinite(value) else None


def parse_markers(path: Path) -> tuple[list[tuple[float, str]], int, dict]:
    phases: list[tuple[float, str]] = []
    dropped = -1
    density = {
        "max_alive": 0,
        "max_tiers": {"Standard": 0, "Special": 0, "Elite": 0, "Ultra": 0},
        "max_delivered_actions": 0,
        "max_active_voices": 0,
        "max_active_loops": 0,
    }
    for raw in path.read_text(encoding="utf-8").splitlines():
        if not raw or raw.startswith("#"):
            continue
        fields = raw.split("\t", 3)
        if len(fields) != 4:
            continue
        seconds, _, kind, detail = fields
        if kind == "phase":
            phases.append((float(seconds), detail.split()[0]))
        elif kind == "capture" and detail.startswith("end "):
            match = re.search(r"\bdropped (\d+)\b", detail)
            if match:
                dropped = int(match.group(1))
        elif kind == "density":
            match = re.search(r"\balive (\d+)\b", detail)
            if match:
                density["max_alive"] = max(density["max_alive"], int(match.group(1)))
            match = re.search(r"\btiers (\d+)/(\d+)/(\d+)/(\d+)\b", detail)
            if match:
                for name, value in zip(density["max_tiers"], map(int, match.groups())):
                    density["max_tiers"][name] = max(density["max_tiers"][name], value)
            match = re.search(r"\bvoices (\d+)/(\d+)\b", detail)
            if match:
                density["max_active_voices"] = max(density["max_active_voices"], sum(map(int, match.groups())))
            match = re.search(r"\bloops (\d+)/(\d+)\b", detail)
            if match:
                density["max_active_loops"] = max(density["max_active_loops"], sum(map(int, match.groups())))
            match = re.search(r"\bactions (\d+)\b", detail)
            if match:
                density["max_delivered_actions"] = max(density["max_delivered_actions"], int(match.group(1)))
    return phases, dropped, density


def phase_metrics(data: np.ndarray, energy: np.ndarray, rate: int, phases: list[tuple[float, str]]) -> list[dict]:
    result: list[dict] = []
    for index, (start, phase) in enumerate(phases):
        end = phases[index + 1][0] if index + 1 < len(phases) else data.shape[0] / rate
        a = max(0, min(data.shape[0], int(start * rate)))
        b = max(a, min(data.shape[0], int(end * rate)))
        if b <= a:
            continue
        item = {"phase": phase, "start_seconds": start, "end_seconds": end}
        item.update(metrics(data[a:b], energy[a:b], rate))
        result.append(item)
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("wav", type=Path)
    parser.add_argument("--markers", type=Path)
    parser.add_argument("--telemetry", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--require-alive", type=int, default=0)
    parser.add_argument("--require-actions", type=int, default=0)
    parser.add_argument("--require-voices", type=int, default=0)
    parser.add_argument("--require-tier", choices=("Standard", "Special", "Elite", "Ultra"), action="append", default=[])
    parser.add_argument("--require-phase", action="append", default=[])
    args = parser.parse_args()

    markers = args.markers or Path(str(args.wav) + ".markers.txt")
    telemetry = args.telemetry or Path(str(args.wav) + ".telemetry.tsv")
    output = args.output or Path(str(args.wav) + ".report.json")
    data, rate = read_wav(args.wav)
    energy = k_energy(data, rate)
    phases, dropped, density = parse_markers(markers)
    telemetry_text = telemetry.read_text(encoding="utf-8")
    overflow_match = re.search(r"^# ring_overflow\t(\d+)$", telemetry_text, re.MULTILINE)
    overflow = int(overflow_match.group(1)) if overflow_match else -1

    report = {
        "schema": "wp065-audio-capture-v1",
        "wav": str(args.wav),
        "wav_sha256": hashlib.sha256(args.wav.read_bytes()).hexdigest(),
        "sample_rate": rate,
        "channels": data.shape[1],
        "capture_dropped_samples": dropped,
        "telemetry_ring_overflow": overflow,
        **density,
        "full": metrics(data, energy, rate),
        "phases": phase_metrics(data, energy, rate, phases),
    }
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")

    errors = []
    if dropped != 0:
        errors.append(f"capture dropped samples must be 0, got {dropped}")
    if overflow != 0:
        errors.append(f"telemetry ring overflow must be 0, got {overflow}")
    if report["full"]["lufs_i"] is None:
        errors.append("capture has no finite integrated loudness")
    if density["max_alive"] < args.require_alive:
        errors.append(f"max alive {density['max_alive']} is below required {args.require_alive}")
    if density["max_delivered_actions"] < args.require_actions:
        errors.append(f"max delivered actions {density['max_delivered_actions']} is below required {args.require_actions}")
    if density["max_active_voices"] < args.require_voices:
        errors.append(f"max active voices {density['max_active_voices']} is below required {args.require_voices}")
    for required_tier in args.require_tier:
        if density["max_tiers"][required_tier] <= 0:
            errors.append(f"required tier {required_tier!r} was never alive at a density checkpoint")
    for required_phase in args.require_phase:
        phase = next((item for item in report["phases"] if item["phase"] == required_phase), None)
        if phase is None:
            errors.append(f"required phase {required_phase!r} is missing")
        elif phase["lufs_i"] is None:
            errors.append(f"required phase {required_phase!r} has no finite integrated loudness")
    if errors:
        for error in errors:
            print("FAIL:", error)
        return 1
    print(json.dumps(report["full"], sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
