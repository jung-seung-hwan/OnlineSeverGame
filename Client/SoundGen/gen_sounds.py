"""Jelly Survival 효과음·배경음 생성기 (직접 생성, 외부 소리 없음).

기준: docs/work/juice-sound.md 0장 '소리 기준 v1'. 소리를 고칠 때는 각 함수의 입력(음높이·길이·필터)만 바꾸고,
기준을 바꾸면 그 문서에 버전을 남긴다.

    python Client/SoundGen/gen_sounds.py            # 전부 만들기
    python Client/SoundGen/gen_sounds.py sfx_jump   # 이름에 이 글자가 든 것만

출력: Client/Assets/Resources/Audio/*.wav (16비트 모노). 고정 시드라 같은 입력이면 같은 파일이 나온다.
필요한 것: Python 3 + numpy.
"""
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Audio"))
SFX_SR = 44100
BGM_SR = 22050
SEED = 20261110

# --------------------------------------------------------------------------- 기본 도구


def secs(d, sr):
    return np.arange(int(round(d * sr))) / sr


def sweep(f0, f1, n, sr, tau=None):
    """f0 → f1 음높이 곡선. tau 가 있으면 지수적으로(빠르게 다가감), 없으면 로그 직선."""
    t = np.arange(n) / sr
    if tau is None:
        dur = max(n / sr, 1e-6)
        return f0 * (f1 / f0) ** (t / dur)
    return f1 + (f0 - f1) * np.exp(-t / tau)


def osc(freq, sr, shape="sine", phase0=0.0):
    """freq: 샘플마다의 주파수 배열."""
    ph = 2 * np.pi * np.cumsum(freq) / sr + phase0
    if shape == "sine":
        return np.sin(ph)
    if shape == "tri":
        return 2 / np.pi * np.arcsin(np.sin(ph))
    if shape == "soft_square":  # 홀수 배음 3개만: 네모보다 둥글다
        return np.sin(ph) + np.sin(3 * ph) / 3 * 0.6 + np.sin(5 * ph) / 5 * 0.3
    raise ValueError(shape)


def env(n, sr, attack=0.003, tau=0.1, hold=0.0):
    t = np.arange(n) / sr
    a = np.clip(t / max(attack, 1e-5), 0, 1)
    d = np.where(t < attack + hold, 1.0, np.exp(-(t - attack - hold) / tau))
    return a * d


def jelly_wobble(n, sr, rate=11.0, depth=0.04, tau=0.15):
    """몸 소리 공통 '출렁임': 음높이 배수 1 ± depth, 시간이 갈수록 줄어든다."""
    t = np.arange(n) / sr
    return 1 + depth * np.sin(2 * np.pi * rate * t) * np.exp(-t / tau)


def spectral(x, sr, gain_fn):
    """FFT 로 주파수별 이득(위상 0)을 곱한다. 짧은 효과음과 반복 배경음(원형) 모두에 쓴다."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / sr)
    return np.fft.irfft(X * gain_fn(f), len(x))


def lowpass(x, sr, fc, order=2):
    return spectral(x, sr, lambda f: 1 / np.sqrt(1 + (f / fc) ** (2 * order)))


def highpass(x, sr, fc, order=2):
    return spectral(x, sr, lambda f: 1 / np.sqrt(1 + (fc / np.maximum(f, 1e-3)) ** (2 * order)))


def bandpass(x, sr, lo, hi, order=2):
    return highpass(lowpass(x, sr, hi, order), sr, lo, order)


def svf_bandpass(x, sr, fc_curve, q=1.2):
    """시간에 따라 중심 주파수가 움직이는 대역 통과(휙 소리용). 짧은 소리에만(파이썬 반복문)."""
    y = np.zeros_like(x)
    low = band = 0.0
    damp = 1.0 / q
    for i in range(len(x)):
        f = 2 * np.sin(np.pi * min(fc_curve[i], sr * 0.24) / sr)
        high = x[i] - low - damp * band
        band += f * high
        low += f * band
        y[i] = band
    return y


def noise(n, rng):
    return rng.standard_normal(n)


def mix(n, *parts):
    """(시작 초, 신호, sr) 들을 길이 n 버퍼에 더한다."""
    out = np.zeros(n)
    for start, sig, sr in parts:
        i = int(round(start * sr))
        m = min(len(sig), n - i)
        if m > 0:
            out[i:i + m] += sig[:m]
    return out


def fade_out(x, sr, d=0.01):
    k = min(len(x), int(d * sr))
    if k > 0:
        x[-k:] *= np.linspace(1, 0, k)
    return x


def normalize(x, peak_db):
    p = np.max(np.abs(x))
    return x if p == 0 else x * (10 ** (peak_db / 20) / p)


def db(v):
    return 20 * np.log10(max(v, 1e-9))


def write(name, x, sr):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    pcm = np.clip(np.round(x * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    rms = np.sqrt(np.mean(x ** 2))
    print(f"{name:20s} {len(x) / sr:6.2f}s  peak {db(np.max(np.abs(x))):6.1f} dBFS  rms {db(rms):6.1f} dBFS  {os.path.getsize(path) // 1024:5d} KB")


def finish_sfx(name, x, sr=SFX_SR, lp=7000, peak=-3.0):
    x = lowpass(x, sr, lp, 2) if lp else x
    x = highpass(x, sr, 50, 2)
    x = fade_out(x, sr, 0.012)
    x[: int(0.001 * sr)] *= np.linspace(0, 1, int(0.001 * sr))
    write(name, normalize(x, peak), sr)


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# --------------------------------------------------------------------------- 몸 소리


def sfx_jump(rng, sr=SFX_SR):
    n = int(0.17 * sr)
    f = sweep(250, 640, n, sr, tau=None) * jelly_wobble(n, sr, 12, 0.035)
    f = np.minimum(f, 640)
    body = osc(f, sr) + 0.18 * osc(2 * f, sr)
    body *= env(n, sr, 0.004, 0.055, hold=0.02)
    pop = lowpass(noise(int(0.012 * sr), rng), sr, 1800) * 0.25
    return mix(n, (0, body, sr), (0, pop, sr))


def sfx_land(rng, sr=SFX_SR):
    n = int(0.15 * sr)
    f = sweep(210, 80, n, sr, tau=0.03) * jelly_wobble(n, sr, 14, 0.07, 0.08)
    body = (osc(f, sr) + 0.3 * osc(f, sr, "tri")) * env(n, sr, 0.002, 0.045)
    thump = lowpass(noise(int(0.04 * sr), rng), sr, 500) * env(int(0.04 * sr), sr, 0.001, 0.012) * 0.8
    return mix(n, (0, body, sr), (0, thump, sr))


def sfx_dash(rng, sr=SFX_SR):
    n = int(0.21 * sr)
    t = np.arange(n) / sr
    fc = 500 + 2600 * (t / t[-1]) ** 0.7
    air = svf_bandpass(noise(n, rng), sr, fc, q=1.6)
    air *= np.sin(np.pi * np.clip(t / 0.2, 0, 1)) ** 1.5
    tone = osc(sweep(300, 820, n, sr) * jelly_wobble(n, sr, 10, 0.03), sr) * env(n, sr, 0.01, 0.06) * 0.35
    return air / np.max(np.abs(air)) + tone


def sfx_dash_hit(rng, sr=SFX_SR):
    n = int(0.33 * sr)
    thump = osc(sweep(150, 55, n, sr, tau=0.05), sr) * env(n, sr, 0.001, 0.09)
    bwong_f = 300 * jelly_wobble(n, sr, 9, 0.14, 0.2)
    bwong = (osc(bwong_f, sr) + 0.25 * osc(2 * bwong_f, sr)) * env(n, sr, 0.004, 0.11) * 0.55
    smack = lowpass(noise(int(0.03 * sr), rng), sr, 3200) * env(int(0.03 * sr), sr, 0.0005, 0.008) * 0.9
    return mix(n, (0, thump, sr), (0.004, bwong, sr), (0, smack, sr))


def sfx_stomp(rng, sr=SFX_SR):
    n = int(0.34 * sr)
    t = np.arange(n) / sr
    # 스프링 '보잉': 올라가면서 크게 출렁인다
    f = (190 + 260 * (1 - np.exp(-t / 0.08))) * (1 + 0.09 * np.sin(2 * np.pi * 16 * t) * np.exp(-t / 0.25))
    boing = (osc(f, sr) + 0.2 * osc(2 * f, sr, "tri")) * env(n, sr, 0.003, 0.11, hold=0.03)
    thud = osc(sweep(170, 70, int(0.08 * sr), sr, tau=0.02), sr) * env(int(0.08 * sr), sr, 0.001, 0.025) * 0.8
    return mix(n, (0, boing * 0.8, sr), (0, thud, sr))


def sfx_crouch(rng, sr=SFX_SR):
    n = int(0.11 * sr)
    squish = lowpass(noise(n, rng), sr, 1100) * env(n, sr, 0.008, 0.03) * 0.6
    tone = osc(sweep(250, 140, n, sr, tau=0.03) * jelly_wobble(n, sr, 13, 0.05), sr) * env(n, sr, 0.004, 0.035)
    return squish + tone


def sfx_death(rng, sr=SFX_SR):
    n = int(0.5 * sr)
    pop_n = int(0.06 * sr)
    pop = osc(sweep(950, 140, pop_n, sr, tau=0.012), sr) * env(pop_n, sr, 0.0005, 0.02)
    splat_n = int(0.22 * sr)
    splat = lowpass(noise(splat_n, rng), sr, 1600) * env(splat_n, sr, 0.002, 0.05) * 0.9
    splat = splat * (1 + 0.5 * np.sin(2 * np.pi * 23 * np.arange(splat_n) / sr))  # 질척한 떨림
    parts = [(0, pop, sr), (0.01, splat, sr)]
    for i, (at, f0, amp) in enumerate([(0.09, 520, 0.5), (0.15, 700, 0.4), (0.22, 600, 0.3), (0.3, 820, 0.22), (0.37, 680, 0.15)]):
        dn = int(0.045 * sr)
        drop = osc(sweep(f0, f0 * 1.9, dn, sr), sr) * env(dn, sr, 0.002, 0.015) * amp
        parts.append((at + rng.uniform(-0.01, 0.01), drop, sr))
    return mix(n, *parts)


# --------------------------------------------------------------------------- 상태 소리


def bell(f, n, sr, tau=0.2, bright=1.0):
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.3 * bright * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / (tau * 0.6))
    x += 0.12 * bright * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t / (tau * 0.4))
    a = int(0.002 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x


def sfx_stun(rng, sr=SFX_SR):
    n = int(0.65 * sr)
    parts = []
    for i, m in enumerate([88, 84, 81, 77, 81, 84]):  # 빙글빙글 도는 별
        bn = int(0.2 * sr)
        vib = 1 + 0.015 * np.sin(2 * np.pi * 7 * np.arange(bn) / sr)
        b = osc(midi(m) * vib, sr) * env(bn, sr, 0.002, 0.07) + 0.2 * bell(midi(m) * 2, bn, sr, 0.04)
        parts.append((i * 0.075, b * (0.9 - i * 0.08), sr))
    return mix(n, *parts)


def sfx_immune(rng, sr=SFX_SR):
    n = int(0.45 * sr)
    parts = []
    for i, m in enumerate([84, 88, 91, 96]):
        parts.append((i * 0.045, bell(midi(m), int(0.25 * sr), sr, 0.08, 0.6) * (0.5 + 0.12 * i), sr))
    sh = highpass(noise(n, rng), sr, 4000) * env(n, sr, 0.05, 0.08) * 0.06
    parts.append((0, sh, sr))
    return mix(n, *parts)


# --------------------------------------------------------------------------- 위험물 소리


def sfx_warn(rng, sr=SFX_SR):
    """위에서 떨어질 위험물 예고: 높은 '띠링' 두 번."""
    n = int(0.26 * sr)
    parts = []
    for i, m in enumerate([84, 88]):
        bn = int(0.13 * sr)
        b = osc(np.full(bn, midi(m)), sr, "tri") * env(bn, sr, 0.003, 0.04, hold=0.015)
        parts.append((i * 0.085, b, sr))
    return mix(n, *parts)


def sfx_side_warn(rng, sr=SFX_SR):
    """옆 위험물 예고: 짧게 올라가는 '삐삐삐'(위 예고와 다르게 세 번, 가로로 달리는 느낌)."""
    n = int(0.32 * sr)
    parts = []
    for i, m in enumerate([76, 79, 83]):
        bn = int(0.07 * sr)
        b = osc(np.full(bn, midi(m)), sr, "soft_square") * env(bn, sr, 0.002, 0.02, hold=0.02) * 0.7
        parts.append((i * 0.065, b, sr))
    return mix(n, *parts)


def thunk(rng, sr, f0, f1, tau, noise_lp, amp=1.0):
    n = int(0.12 * sr)
    body = osc(sweep(f0, f1, n, sr, tau=0.02), sr) * env(n, sr, 0.001, tau)
    nz = lowpass(noise(int(0.04 * sr), rng), sr, noise_lp) * env(int(0.04 * sr), sr, 0.0005, 0.01) * 0.7
    return mix(n, (0, body, sr), (0, nz, sr)) * amp


def metal_ring(n, sr, freqs, amps, taus):
    t = np.arange(n) / sr
    x = np.zeros(n)
    for f, a, tau in zip(freqs, amps, taus):
        x += a * np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    a = int(0.001 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x


def sfx_land_knife(rng, sr=SFX_SR):
    n = int(0.36 * sr)
    ring = metal_ring(n, sr, [1975, 3135, 4420], [0.32, 0.14, 0.06], [0.12, 0.07, 0.04])
    return mix(n, (0, thunk(rng, sr, 170, 85, 0.05, 1500), sr), (0.003, ring, sr))


def sfx_land_fork(rng, sr=SFX_SR):
    n = int(0.32 * sr)
    ring = metal_ring(n, sr, [2637, 3960, 5270], [0.28, 0.12, 0.05], [0.1, 0.06, 0.035])
    return mix(n, (0, thunk(rng, sr, 230, 120, 0.035, 2000, 0.8), sr), (0.002, ring, sr))


def sfx_land_icicle(rng, sr=SFX_SR):
    n = int(0.42 * sr)
    crack = bandpass(noise(int(0.09 * sr), rng), sr, 2200, 7000) * env(int(0.09 * sr), sr, 0.0005, 0.025) * 0.6
    parts = [(0, crack, sr), (0, thunk(rng, sr, 260, 140, 0.03, 2500, 0.5), sr)]
    for i in range(9):  # 얼음 조각이 짤랑
        f = rng.uniform(2100, 4800)
        bn = int(0.16 * sr)
        parts.append((rng.uniform(0.0, 0.14), bell(f, bn, sr, rng.uniform(0.03, 0.07), 0.3) * rng.uniform(0.12, 0.25), sr))
    return mix(n, *parts)


def sfx_land_crate(rng, sr=SFX_SR):
    n = int(0.34 * sr)
    boom = osc(sweep(105, 62, n, sr, tau=0.06), sr) * env(n, sr, 0.001, 0.08)
    wood = bandpass(noise(int(0.08 * sr), rng), sr, 180, 1300) * env(int(0.08 * sr), sr, 0.0005, 0.025) * 0.9
    knock = osc(np.full(int(0.06 * sr), 430.0), sr) * env(int(0.06 * sr), sr, 0.0005, 0.018) * 0.4
    knock2 = osc(np.full(int(0.05 * sr), 470.0), sr) * env(int(0.05 * sr), sr, 0.0005, 0.014) * 0.2
    return mix(n, (0, boom, sr), (0, wood, sr), (0, knock, sr), (0.07, knock2, sr))


def sfx_side_pass(rng, sr=SFX_SR):
    """옆 위험물이 곁을 지나감: 다가왔다 멀어지는 '휘융'."""
    n = int(0.46 * sr)
    t = np.arange(n) / sr
    u = t / t[-1]
    fc = 450 + 1500 * np.sin(np.pi * u) ** 2
    air = svf_bandpass(noise(n, rng), sr, fc, q=2.2)
    air = air / np.max(np.abs(air)) * np.sin(np.pi * u) ** 2
    tone = osc(560 - 140 * u, sr, "tri") * np.sin(np.pi * u) ** 3 * 0.18
    return air + tone


# --------------------------------------------------------------------------- 라운드·UI


def marimba(f, n, sr, tau=0.3):
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.22 * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / (tau * 0.25))
    x += 0.05 * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / (tau * 0.08))
    a = max(1, int(0.002 * sr))
    x[:a] *= np.linspace(0, 1, a)
    return x


def sfx_count(rng, sr=SFX_SR):
    n = int(0.17 * sr)
    f = np.full(n, 660.0)
    f[: int(0.015 * sr)] *= 1.03
    return osc(f, sr) * env(n, sr, 0.004, 0.06, hold=0.05) + 0.25 * marimba(1320, n, sr, 0.05)


def sfx_go(rng, sr=SFX_SR):
    n = int(0.5 * sr)
    x = np.zeros(n)
    for m in (81, 85, 88):
        x += marimba(midi(m), n, sr, 0.18) + 0.25 * bell(midi(m) * 2, n, sr, 0.1)
    return x


def sfx_win(rng, sr=SFX_SR):
    n = int(1.9 * sr)
    parts = []
    for i, m in enumerate([72, 76, 79, 84]):
        parts.append((i * 0.12, marimba(midi(m), int(0.6 * sr), sr, 0.25), sr))
    chord_n = int(1.4 * sr)
    for m in (84, 88, 91):
        parts.append((0.48, marimba(midi(m), chord_n, sr, 0.55) * 0.7 + bell(midi(m), chord_n, sr, 0.5, 0.5) * 0.35, sr))
    return mix(n, *parts)


def sfx_lose(rng, sr=SFX_SR):
    n = int(1.7 * sr)
    parts = []
    notes = [(67, 0.0, 0.22), (66, 0.26, 0.22), (65, 0.52, 0.22), (64, 0.78, 0.8)]
    for m, at, d in notes:
        bn = int((d + 0.15) * sr)
        t = np.arange(bn) / sr
        vib = 1 + (0.025 * np.sin(2 * np.pi * 6 * t) if d > 0.5 else 0)
        f = midi(m) * vib * (1 - 0.03 * (t / t[-1]) if d > 0.5 else 1)
        e = env(bn, sr, 0.02, 0.12, hold=d - 0.05)
        parts.append((at, (osc(f, sr, "tri") + 0.3 * osc(f, sr)) * e, sr))
    return mix(n, *parts)


def sfx_click(rng, sr=SFX_SR):
    n = int(0.06 * sr)
    return osc(sweep(1400, 850, n, sr, tau=0.01), sr) * env(n, sr, 0.0008, 0.014)


SFX = [
    # 이름, 함수, 로우패스(Hz)
    ("sfx_jump", sfx_jump, 6000),
    ("sfx_land", sfx_land, 4000),
    ("sfx_dash", sfx_dash, 6500),
    ("sfx_dash_hit", sfx_dash_hit, 5000),
    ("sfx_stomp", sfx_stomp, 5000),
    ("sfx_crouch", sfx_crouch, 3500),
    ("sfx_death", sfx_death, 5000),
    ("sfx_stun", sfx_stun, 8000),
    ("sfx_immune", sfx_immune, 9000),
    ("sfx_warn", sfx_warn, 7000),
    ("sfx_side_warn", sfx_side_warn, 6000),
    ("sfx_land_knife", sfx_land_knife, 7500),
    ("sfx_land_fork", sfx_land_fork, 8000),
    ("sfx_land_icicle", sfx_land_icicle, 8500),
    ("sfx_land_crate", sfx_land_crate, 4000),
    ("sfx_side_pass", sfx_side_pass, 6000),
    ("sfx_count", sfx_count, 6000),
    ("sfx_go", sfx_go, 8000),
    ("sfx_win", sfx_win, 8000),
    ("sfx_lose", sfx_lose, 5000),
    ("sfx_click", sfx_click, 6000),
]

# --------------------------------------------------------------------------- 배경음

MAJOR = [0, 2, 4, 5, 7, 9, 11]
MINOR = [0, 2, 3, 5, 7, 8, 10]
DORIAN = [0, 2, 3, 5, 7, 9, 10]


class Track:
    def __init__(self, name, bpm, bars, sr=BGM_SR):
        self.name = name
        self.bpm = bpm
        self.beat = 60.0 / bpm
        self.bars = bars
        self.sr = sr
        self.n = int(round(bars * 4 * self.beat * sr))
        self.buf = np.zeros(self.n)

    def at(self, bar, beat):
        return int(round((bar * 4 + beat) * self.beat * self.sr))

    def place(self, i0, sig, gain=1.0):
        """반복 곡이라 끝을 넘는 꼬리는 처음에 더한다(이음매 없음)."""
        i0 %= self.n
        sig = sig * gain
        m = min(len(sig), self.n - i0)
        self.buf[i0:i0 + m] += sig[:m]
        rest = sig[m:]
        while len(rest):
            k = min(len(rest), self.n)
            self.buf[:k] += rest[:k]
            rest = rest[k:]


def inst_marimba(f, d, sr):
    return marimba(f, int((d + 0.6) * sr), sr, 0.22 + 0.08 * (220 / max(f, 220)))


def inst_celesta(f, d, sr):
    return bell(f, int((d + 1.0) * sr), sr, 0.45, 0.5)


def inst_musicbox(f, d, sr):
    n = int((d + 1.0) * sr)
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.5)
    x += 0.35 * np.sin(2 * np.pi * f * 2 * t) * np.exp(-t / 0.25)
    x += 0.12 * np.sin(2 * np.pi * f * 3.9 * t) * np.exp(-t / 0.1)
    a = int(0.001 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x


def inst_pluck(f, d, sr):
    n = int((d + 0.3) * sr)
    t = np.arange(n) / sr
    x = (np.sin(2 * np.pi * f * t) + np.sin(2 * np.pi * 3 * f * t) / 9 + np.sin(2 * np.pi * 5 * f * t) / 25) * np.exp(-t / 0.13)
    a = int(0.002 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x


def inst_bass(f, d, sr):
    n = int((d + 0.12) * sr)
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) + 0.22 * np.sin(2 * np.pi * 2 * f * t)
    e = np.clip(t / 0.01, 0, 1) * np.exp(-t / max(d * 0.9, 0.12))
    e *= np.clip((d + 0.12 - t) / 0.08, 0, 1)
    return x * e


def inst_pad(f, d, sr):
    n = int((d + 0.6) * sr)
    t = np.arange(n) / sr
    x = sum(np.sin(2 * np.pi * f * k * t + p) for k, p in ((1.0, 0), (1.004, 1.3), (0.996, 2.1))) / 3
    x += 0.15 * np.sin(2 * np.pi * 2 * f * t)
    e = np.clip(t / 0.35, 0, 1) * np.clip((d + 0.6 - t) / 0.6, 0, 1)
    return x * e


def drum_kick(sr):
    n = int(0.25 * sr)
    return osc(sweep(115, 48, n, sr, tau=0.03), sr) * env(n, sr, 0.001, 0.09)


def drum_snap(sr, rng):
    n = int(0.12 * sr)
    return bandpass(noise(n, rng), sr, 900, 3000) * env(n, sr, 0.001, 0.03)


def drum_shaker(sr, rng):
    n = int(0.06 * sr)
    return highpass(noise(n, rng), sr, 5500) * env(n, sr, 0.008, 0.015)


def drum_block(sr, f=880):
    n = int(0.1 * sr)
    t = np.arange(n) / sr
    return (np.sin(2 * np.pi * f * t) + 0.4 * np.sin(2 * np.pi * f * 1.58 * t)) * np.exp(-t / 0.025)


def chord_tones(root, quality):
    third = 3 if quality in ("m", "dim") else 4
    fifth = 6 if quality == "dim" else 7
    return [root, root + third, root + fifth]


def make_melody(rng, key, scale, chords, lo, hi, rhythms, rest_p=0.18):
    """8마디 선율. 강박은 화음 음, 약박은 음계 한두 칸. 5~6마디는 1~2마디 리듬을 따라 화음만 옮긴다(동기 반복)."""
    pitches = sorted(key + o * 12 + s for o in range(-2, 4) for s in scale if lo <= key + o * 12 + s <= hi)

    def nearest(cands, prev):
        cands = [c for c in cands if lo <= c <= hi]
        return min(cands, key=lambda c: abs(c - prev) + rng.uniform(0, 2.5))

    notes = []  # (bar, beat, dur, midi)
    prev = (lo + hi) // 2
    bar_rhythm = {}
    for bar in range(8):
        root, q = chords[bar]
        tones = [t + o * 12 for t in chord_tones(root, q) for o in range(0, 9)]
        if bar in (4, 5):
            rhythm = bar_rhythm[bar - 4]
        elif bar == 7:
            rhythm = [1.0, 1.0, 2.0]
        else:
            rhythm = rhythms[rng.integers(len(rhythms))]
        bar_rhythm[bar] = rhythm
        beat = 0.0
        for k, d in enumerate(rhythm):
            strong = abs(beat - round(beat)) < 1e-6 and int(round(beat)) % 2 == 0
            if bar == 7 and k == len(rhythm) - 1:
                m = nearest([key + o * 12 for o in range(-2, 4)], prev)
            elif strong or k == 0:
                m = nearest(tones, prev)
            else:
                if rng.random() < rest_p:
                    beat += d
                    continue
                i = int(np.searchsorted(pitches, prev))
                step = int(rng.choice([-2, -1, -1, 1, 1, 2]))
                m = pitches[int(np.clip(i + step, 0, len(pitches) - 1))]
            notes.append((bar, beat, d, m))
            prev = m
            beat += d
    return notes


def render_bgm(tr, rng, chords, key, scale, melody_inst, melody_range, rhythms, *,
               mel_gain, comp, comp_gain, bass_pattern, bass_gain, drums, pad_gain=0.0,
               counter_inst=None, counter_gain=0.0, reverb=0.2):
    sr = tr.sr
    melody = make_melody(rng, key, scale, chords, *melody_range, rhythms)
    for half in range(2):
        b0 = half * 8
        for bar, beat, d, m in melody:
            dur = d * tr.beat
            tr.place(tr.at(b0 + bar, beat), melody_inst(midi(m), dur, sr), mel_gain)
            if half == 1 and counter_inst is not None and beat in (0.0, 2.0):
                tr.place(tr.at(b0 + bar, beat), counter_inst(midi(m + 12), dur, sr), counter_gain)
        for bar in range(8):
            root, q = chords[bar]
            tones = chord_tones(root, q)
            # 반주: comp = [(박, 화음 음 번호들, 길이)]
            for beat, idxs, d in comp:
                for ix in idxs:
                    m = tones[ix % 3] + 12 * (ix // 3) + 60 - 12 * ((root + 60) > 66)
                    tr.place(tr.at(b0 + bar, beat), inst_pluck(midi(m), d * tr.beat, sr), comp_gain)
            if pad_gain > 0:
                for t in tones:
                    tr.place(tr.at(b0 + bar, 0), inst_pad(midi(t + 60 - 12 * (t + 60 > 66)), 4 * tr.beat, sr), pad_gain)
            for beat, deg, d in bass_pattern:
                m = 36 + root % 12 + (7 if deg == 5 else 12 if deg == 8 else 0)
                tr.place(tr.at(b0 + bar, beat), inst_bass(midi(m), d * tr.beat, sr), bass_gain)
            for kind, beats, g in drums:
                for beat in beats:
                    if kind == "kick":
                        s = drum_kick(sr)
                    elif kind == "snap":
                        s = drum_snap(sr, rng)
                    elif kind == "shaker":
                        s = drum_shaker(sr, rng)
                    elif kind == "block":
                        s = drum_block(sr)
                    elif kind == "block_hi":
                        s = drum_block(sr, 1250)
                    else:
                        raise ValueError(kind)
                    tr.place(tr.at(b0 + bar, beat), s, g)

    x = tr.buf
    # 공간감: 반복 곡 전체에 원형 컨볼루션(꼬리가 처음으로 이어진다)
    ir_n = int(1.3 * sr)
    ir = noise(ir_n, rng) * np.exp(-np.arange(ir_n) / sr / 0.35)
    ir = lowpass(ir, sr, 3500)
    ir /= np.sqrt(np.sum(ir ** 2))
    pad_ir = np.zeros(tr.n)
    pad_ir[:ir_n] = ir[: tr.n]
    wet = np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(pad_ir), tr.n)
    x = x + reverb * wet / max(np.max(np.abs(wet)), 1e-9) * np.max(np.abs(x))
    # 효과음 자리 비우기(2~5 kHz 약 -4 dB), 말랑하게 7 kHz 위 깎기, 40 Hz 아래 정리
    x = spectral(x, sr, lambda f: 1 - 0.37 * np.exp(-((f - 3300) / 1300) ** 2))
    x = lowpass(x, sr, 7000)
    x = highpass(x, sr, 40)
    x = np.tanh(x / np.max(np.abs(x)) * 1.2)
    write(tr.name, normalize(x, -6.0), sr)


R1 = [[1, 0.5, 0.5, 1, 1], [0.5, 0.5, 1, 0.5, 0.5, 1], [1.5, 0.5, 2], [1, 1, 0.5, 0.5, 1], [0.5, 0.5, 0.5, 0.5, 2]]
R_SPARSE = [[2, 1, 1], [1.5, 0.5, 2], [1, 1, 2], [3, 1]]
R_SWING = [[0.75, 0.25, 1, 0.75, 0.25, 1], [1, 0.5, 0.5, 2], [0.5, 0.5, 1, 1, 1]]


def bgm_kitchen(rng):
    tr = Track("bgm_kitchen", 112, 16)
    C, D, E, F, G, A = 0, 2, 4, 5, 7, 9
    chords = [(C, ""), (A, "m"), (F, ""), (G, ""), (C, ""), (A, "m"), (D, "m"), (G, "")]
    render_bgm(tr, rng, chords, 72, MAJOR, inst_marimba, (67, 86), R1,
               mel_gain=0.55, comp=[(0.5, [0, 1, 2], 0.4), (1.5, [1, 2], 0.4), (2.5, [0, 1, 2], 0.4), (3.5, [1, 2], 0.4)],
               comp_gain=0.12, bass_pattern=[(0, 1, 1.0), (1.5, 5, 0.5), (2, 1, 1.0), (3, 8, 0.5)], bass_gain=0.42,
               drums=[("kick", [0, 2], 0.5), ("snap", [1, 3], 0.12), ("shaker", [0.5, 1.5, 2.5, 3.5], 0.05), ("block", [3.5], 0.1)],
               counter_inst=inst_celesta, counter_gain=0.12, reverb=0.18)


def bgm_winter(rng):
    tr = Track("bgm_winter", 96, 16)
    A, F, C, G, E = 9, 5, 0, 7, 4
    chords = [(A, "m"), (F, ""), (C, ""), (G, ""), (A, "m"), (F, ""), (E, ""), (A, "m")]
    render_bgm(tr, rng, chords, 69, MINOR, inst_celesta, (69, 88), R_SPARSE,
               mel_gain=0.5, comp=[(0, [0, 1, 2], 0.9), (2, [1, 2, 3], 0.9)], comp_gain=0.06,
               bass_pattern=[(0, 1, 2.0), (2, 5, 2.0)], bass_gain=0.35,
               drums=[("shaker", [0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5], 0.035), ("kick", [0], 0.3), ("block_hi", [2.5], 0.05)],
               pad_gain=0.06, counter_inst=inst_musicbox, counter_gain=0.1, reverb=0.3)


def bgm_warehouse(rng):
    tr = Track("bgm_warehouse", 104, 16)
    D, G, F, C, A = 2, 7, 5, 0, 9
    chords = [(D, "m"), (G, ""), (D, "m"), (G, ""), (F, ""), (C, ""), (G, ""), (D, "m")]
    render_bgm(tr, rng, chords, 62, DORIAN, inst_marimba, (62, 79), R_SWING,
               mel_gain=0.5, comp=[(1, [0, 2], 0.3), (3, [0, 2], 0.3)], comp_gain=0.1,
               bass_pattern=[(0, 1, 0.5), (0.75, 1, 0.25), (1.5, 5, 0.5), (2, 8, 0.5), (2.75, 5, 0.25), (3.5, 1, 0.5)], bass_gain=0.45,
               drums=[("kick", [0, 1.5, 2], 0.45), ("block", [1, 3], 0.14), ("block_hi", [2.75, 3.75], 0.07), ("shaker", [0.5, 1.5, 2.5, 3.5], 0.04)],
               counter_inst=inst_pluck, counter_gain=0.12, reverb=0.14)


def bgm_dessert(rng):
    tr = Track("bgm_dessert", 120, 16)
    F, D, Bb, C, G = 5, 2, 10, 0, 7
    chords = [(F, ""), (D, "m"), (Bb, ""), (C, ""), (F, ""), (D, "m"), (G, "m"), (C, "")]
    render_bgm(tr, rng, chords, 77, MAJOR, inst_musicbox, (72, 91), R1,
               mel_gain=0.45, comp=[(1, [0, 1, 2], 0.5), (2, [0, 1, 2], 0.5), (3, [0, 1, 2], 0.5)], comp_gain=0.08,
               bass_pattern=[(0, 1, 1.0), (2, 5, 1.0)], bass_gain=0.38,
               drums=[("kick", [0, 2], 0.4), ("snap", [1, 3], 0.08), ("shaker", [0.5, 1.5, 2.5, 3.5], 0.04)],
               counter_inst=inst_marimba, counter_gain=0.12, reverb=0.24)


BGM = [("bgm_kitchen", bgm_kitchen), ("bgm_winter", bgm_winter), ("bgm_warehouse", bgm_warehouse), ("bgm_dessert", bgm_dessert)]


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    for i, (name, fn, lp) in enumerate(SFX):
        if only in name:
            rng = np.random.default_rng(SEED + i)
            finish_sfx(name, fn(rng), lp=lp)
    for i, (name, fn) in enumerate(BGM):
        if only in name:
            fn(np.random.default_rng(SEED + 100 + i))


if __name__ == "__main__":
    main()
