"""Jelly Survival 효과음·배경음 생성기 (직접 생성, 외부 소리 없음).

기준: docs/work/juice-sound.md 0장 '소리 기준 v2'. 소리를 고칠 때는 각 함수의 입력(음높이·길이·필터)이나
곡 정의(선율·화음·악기 배치)만 바꾸고, 기준을 바꾸면 그 문서에 버전을 남긴다.

    python Client/SoundGen/gen_sounds.py            # 전부 만들기
    python Client/SoundGen/gen_sounds.py sfx_jump   # 이름에 이 글자가 든 것만(크기 표는 전부 다시 쓴다)

출력
- Client/Assets/Resources/Audio/sfx_*.wav : 16비트 모노 44.1 kHz. 들리는 크기(A 가중 50 ms 최대 RMS)를 -14 dB 로 맞춘다.
- Client/Assets/Resources/Audio/bgm_*.wav : 16비트 스테레오 24 kHz, 끊김 없이 반복. A 가중 RMS -20 dB.
- Client/Assets/Scripts/Juice/JuiceLevels.cs : 효과음별 재생 배수(종류별 목표 크기 - 파일 크기). 손으로 고치지 않는다.
고정 시드라 같은 입력이면 같은 파일이 나온다. 필요한 것: Python 3 + numpy.
"""
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Audio"))
LEVELS_CS = os.path.normpath(os.path.join(HERE, "..", "Assets", "Scripts", "Juice", "JuiceLevels.cs"))
SFX_SR = 44100
BGM_SR = 24000
SEED = 20261110

SFX_FILE_LOUDNESS = -14.0   # 효과음 파일의 단기 크기(dB, A 가중)
SFX_PEAK_CEILING = -1.0     # 효과음 파일 피크 상한(dBFS)
BGM_LOUDNESS = -20.0        # 배경음 A 가중 RMS(dB)

# 종류별 목표 크기(내 소리, 효과음 설정 100% 일 때, dB). 기준 v2 표.
SFX_TARGET = {
    "sfx_jump": -26, "sfx_land": -27, "sfx_dash": -24, "sfx_crouch": -29,
    "sfx_stomp": -20, "sfx_dash_hit": -19, "sfx_death": -17,
    "sfx_stun": -25, "sfx_immune": -28,
    "sfx_warn": -21, "sfx_side_warn": -20,
    "sfx_land_knife": -22, "sfx_land_fork": -22, "sfx_land_icicle": -22, "sfx_land_crate": -21, "sfx_side_pass": -22,
    "sfx_count": -21, "sfx_go": -19, "sfx_win": -18, "sfx_lose": -19,
    "sfx_click": -26,
}

# --------------------------------------------------------------------------- 기본 도구


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
    raise ValueError(shape)


def env(n, sr, attack=0.003, tau=0.1, hold=0.0):
    t = np.arange(n) / sr
    a = 0.5 - 0.5 * np.cos(np.pi * np.clip(t / max(attack, 1e-5), 0, 1))
    d = np.where(t < attack + hold, 1.0, np.exp(-(t - attack - hold) / tau))
    return a * d


def jelly_wobble(n, sr, rate=11.0, depth=0.04, tau=0.15):
    """몸 소리 공통 '출렁임': 음높이 배수 1 ± depth, 시간이 갈수록 줄어든다."""
    t = np.arange(n) / sr
    return 1 + depth * np.sin(2 * np.pi * rate * t) * np.exp(-t / tau)


def spectral(x, sr, gain_fn, circular=False):
    """FFT 로 주파수별 이득(위상 0)을 곱한다. 효과음은 앞뒤를 0으로 늘려 끝이 처음으로 감기지 않게,
    반복 배경음은 circular=True 로 원형 그대로(이음매가 이어진다)."""
    pad = 0 if circular else int(0.08 * sr)
    y = np.pad(x, (pad, pad)) if pad else x
    X = np.fft.rfft(y)
    f = np.fft.rfftfreq(len(y), 1 / sr)
    y = np.fft.irfft(X * gain_fn(f), len(y))
    return y[pad:pad + len(x)] if pad else y


def lowpass(x, sr, fc, order=2, circular=False):
    return spectral(x, sr, lambda f: 1 / np.sqrt(1 + (f / fc) ** (2 * order)), circular)


def highpass(x, sr, fc, order=2, circular=False):
    return spectral(x, sr, lambda f: 1 / np.sqrt(1 + (fc / np.maximum(f, 1e-3)) ** (2 * order)), circular)


def bandpass(x, sr, lo, hi, order=2):
    return highpass(lowpass(x, sr, hi, order), sr, lo, order)


def a_weight(x, sr, circular=False):
    def g(f):
        f2 = np.maximum(f, 1e-3) ** 2
        ra = (12194 ** 2 * f2 * f2) / ((f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194 ** 2))
        return ra / 0.7943  # 1 kHz 에서 0 dB
    return spectral(x, sr, g, circular)


def short_term_loudness(x, sr):
    """A 가중 50 ms 창 RMS 의 최댓값(dB). 효과음의 '들리는 크기'."""
    a = a_weight(x, sr)
    win = int(0.05 * sr)
    hop = max(1, win // 4)
    best = 1e-12
    for i in range(0, max(1, len(a) - win + 1), hop):
        best = max(best, float(np.mean(a[i:i + win] ** 2)))
    return 10 * np.log10(best)


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


def db(v):
    return 20 * np.log10(max(v, 1e-9))


def write(name, x, sr):
    """x: 모노 1차원 또는 (2, n) 스테레오."""
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".wav")
    ch = 1 if x.ndim == 1 else 2
    data = x if ch == 1 else x.T.reshape(-1)
    pcm = np.clip(np.round(data * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    return path


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


# --------------------------------------------------------------------------- 악기(효과음·배경음 공용)


def marimba(f, n, sr, tau=None, vel=1.0, rng=None):
    """마림바: 기음 + 4배음(조율된 두 옥타브 위) + 10배음, 말렛이 닿는 짧은 소리."""
    t = np.arange(n) / sr
    if tau is None:
        tau = float(np.clip(0.45 * (440.0 / f) ** 0.5, 0.12, 0.9))
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.2 * vel * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / (tau * 0.22))
    x += 0.04 * vel * np.sin(2 * np.pi * f * 9.9 * t) * np.exp(-t / (tau * 0.06))
    if rng is not None:
        k = min(n, int(0.004 * sr))
        x[:k] += lowpass(rng.standard_normal(k), sr, min(2 * f + 800, sr * 0.4)) * np.linspace(0.06, 0, k) * vel
    a = max(2, int(0.0015 * sr))
    x[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    return x * vel


def bell(f, n, sr, tau=0.2, bright=1.0):
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.25 * bright * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / (tau * 0.6))
    x += 0.08 * bright * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t / (tau * 0.4))
    a = max(2, int(0.002 * sr))
    x[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    return x


def woodblock(f, n, sr, tau=0.03):
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau) + 0.35 * np.sin(2 * np.pi * f * 2.6 * t) * np.exp(-t / (tau * 0.5))
    a = max(2, int(0.002 * sr))
    x[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    return x


# --------------------------------------------------------------------------- 효과음: 몸 소리


def sfx_jump(rng, sr=SFX_SR):
    n = int(0.17 * sr)
    f = np.minimum(sweep(240, 600, n, sr) * jelly_wobble(n, sr, 12, 0.035), 600)
    body = osc(f, sr) + 0.15 * osc(2 * f, sr)
    body *= env(n, sr, 0.005, 0.055, hold=0.02)
    pop = lowpass(noise(int(0.012 * sr), rng), sr, 1500) * env(int(0.012 * sr), sr, 0.002, 0.004) * 0.25
    return mix(n, (0, body, sr), (0, pop, sr))


def sfx_land(rng, sr=SFX_SR):
    n = int(0.16 * sr)
    f = sweep(200, 80, n, sr, tau=0.03) * jelly_wobble(n, sr, 14, 0.07, 0.08)
    body = (osc(f, sr) + 0.3 * osc(f, sr, "tri")) * env(n, sr, 0.003, 0.05)
    k = int(0.04 * sr)
    thump = lowpass(noise(k, rng), sr, 450) * env(k, sr, 0.002, 0.012) * 0.8
    return mix(n, (0, body, sr), (0, thump, sr))


def sfx_dash(rng, sr=SFX_SR):
    n = int(0.21 * sr)
    t = np.arange(n) / sr
    fc = 450 + 1800 * (t / t[-1]) ** 0.7
    air = svf_bandpass(noise(n, rng), sr, fc, q=1.3)
    air *= np.sin(np.pi * np.clip(t / 0.2, 0, 1)) ** 1.5
    tone = osc(sweep(300, 760, n, sr) * jelly_wobble(n, sr, 10, 0.03), sr) * env(n, sr, 0.012, 0.06) * 0.4
    return air / np.max(np.abs(air)) + tone


def sfx_dash_hit(rng, sr=SFX_SR):
    n = int(0.33 * sr)
    thump = osc(sweep(150, 55, n, sr, tau=0.05), sr) * env(n, sr, 0.002, 0.09)
    bwong_f = 300 * jelly_wobble(n, sr, 9, 0.14, 0.2)
    bwong = (osc(bwong_f, sr) + 0.25 * osc(2 * bwong_f, sr)) * env(n, sr, 0.005, 0.11) * 0.6
    k = int(0.03 * sr)
    smack = lowpass(noise(k, rng), sr, 2600) * env(k, sr, 0.002, 0.008) * 0.7
    return mix(n, (0, thump, sr), (0.004, bwong, sr), (0, smack, sr))


def sfx_stomp(rng, sr=SFX_SR):
    n = int(0.34 * sr)
    t = np.arange(n) / sr
    f = (190 + 250 * (1 - np.exp(-t / 0.08))) * (1 + 0.09 * np.sin(2 * np.pi * 16 * t) * np.exp(-t / 0.25))
    boing = (osc(f, sr) + 0.2 * osc(2 * f, sr, "tri")) * env(n, sr, 0.004, 0.11, hold=0.03)
    k = int(0.08 * sr)
    thud = osc(sweep(170, 70, k, sr, tau=0.02), sr) * env(k, sr, 0.002, 0.025) * 0.8
    return mix(n, (0, boing * 0.8, sr), (0, thud, sr))


def sfx_crouch(rng, sr=SFX_SR):
    n = int(0.11 * sr)
    squish = lowpass(noise(n, rng), sr, 900) * env(n, sr, 0.01, 0.03) * 0.6
    tone = osc(sweep(240, 140, n, sr, tau=0.03) * jelly_wobble(n, sr, 13, 0.05), sr) * env(n, sr, 0.005, 0.035)
    return squish + tone


def sfx_death(rng, sr=SFX_SR):
    n = int(0.5 * sr)
    pop_n = int(0.06 * sr)
    pop = osc(sweep(900, 140, pop_n, sr, tau=0.012), sr) * env(pop_n, sr, 0.001, 0.02)
    splat_n = int(0.22 * sr)
    splat = lowpass(noise(splat_n, rng), sr, 1400) * env(splat_n, sr, 0.003, 0.05) * 0.9
    splat = splat * (1 + 0.5 * np.sin(2 * np.pi * 23 * np.arange(splat_n) / sr))  # 질척한 떨림
    parts = [(0, pop, sr), (0.01, splat, sr)]
    for at, f0, amp in [(0.09, 480, 0.5), (0.15, 620, 0.4), (0.22, 540, 0.3), (0.3, 700, 0.22), (0.37, 600, 0.15)]:
        dn = int(0.045 * sr)
        drop = osc(sweep(f0, f0 * 1.8, dn, sr), sr) * env(dn, sr, 0.003, 0.015) * amp
        parts.append((at + rng.uniform(-0.01, 0.01), drop, sr))
    return mix(n, *parts)


# --------------------------------------------------------------------------- 효과음: 상태


def sfx_stun(rng, sr=SFX_SR):
    """빙글빙글: 부드러운 종 네 음이 내려간다(v1 보다 한 옥타브 낮고 짧게)."""
    n = int(0.5 * sr)
    parts = []
    for i, m in enumerate([79, 76, 72, 67]):
        bn = int(0.22 * sr)
        vib = 1 + 0.012 * np.sin(2 * np.pi * 7 * np.arange(bn) / sr)
        b = osc(midi(m) * vib, sr) * env(bn, sr, 0.004, 0.07) + 0.15 * bell(midi(m) * 2, bn, sr, 0.03, 0.3)
        parts.append((i * 0.08, b * (0.9 - i * 0.12), sr))
    return mix(n, *parts)


def sfx_immune(rng, sr=SFX_SR):
    n = int(0.42 * sr)
    parts = []
    for i, m in enumerate([79, 84, 88]):
        parts.append((i * 0.05, bell(midi(m), int(0.25 * sr), sr, 0.07, 0.35) * (0.5 + 0.15 * i), sr))
    sh = bandpass(noise(n, rng), sr, 2500, 4500) * env(n, sr, 0.06, 0.07) * 0.04
    parts.append((0, sh, sr))
    return mix(n, *parts)


# --------------------------------------------------------------------------- 효과음: 위험물


def sfx_warn(rng, sr=SFX_SR):
    """위에서 떨어질 위험물 예고: 마림바 '띠링' 두 음(G5 → C6)."""
    n = int(0.3 * sr)
    return mix(n, (0, marimba(midi(79), n, sr, 0.12, 1.0, rng), sr), (0.085, marimba(midi(84), n, sr, 0.14, 1.0, rng), sr))


def sfx_side_warn(rng, sr=SFX_SR):
    """옆 위험물 예고: 나무 블록 세 번 올라감(위 예고와 다르게 '똑똑똑', 가로로 달리는 느낌)."""
    n = int(0.3 * sr)
    parts = []
    for i, f in enumerate([660, 784, 988]):
        parts.append((i * 0.065, woodblock(f, int(0.1 * sr), sr, 0.03) * (0.8 + 0.1 * i), sr))
    return mix(n, *parts)


def thunk(rng, sr, f0, f1, tau, noise_lp, amp=1.0):
    n = int(0.12 * sr)
    body = osc(sweep(f0, f1, n, sr, tau=0.02), sr) * env(n, sr, 0.002, tau)
    k = int(0.04 * sr)
    nz = lowpass(noise(k, rng), sr, noise_lp) * env(k, sr, 0.001, 0.01) * 0.7
    return mix(n, (0, body, sr), (0, nz, sr)) * amp


def metal_ring(n, sr, freqs, amps, taus):
    t = np.arange(n) / sr
    x = np.zeros(n)
    for f, a, tau in zip(freqs, amps, taus):
        x += a * np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    a = max(2, int(0.002 * sr))
    x[:a] *= np.linspace(0, 1, a)
    return x


def sfx_land_knife(rng, sr=SFX_SR):
    n = int(0.36 * sr)
    ring = metal_ring(n, sr, [1480, 2350, 3320], [0.3, 0.12, 0.05], [0.12, 0.07, 0.04])
    return mix(n, (0, thunk(rng, sr, 170, 85, 0.05, 1300), sr), (0.003, ring, sr))


def sfx_land_fork(rng, sr=SFX_SR):
    n = int(0.32 * sr)
    ring = metal_ring(n, sr, [1975, 2960, 3950], [0.26, 0.1, 0.04], [0.1, 0.06, 0.035])
    return mix(n, (0, thunk(rng, sr, 230, 120, 0.035, 1700, 0.85), sr), (0.002, ring, sr))


def sfx_land_icicle(rng, sr=SFX_SR):
    n = int(0.4 * sr)
    k = int(0.07 * sr)
    crack = bandpass(noise(k, rng), sr, 1500, 4000) * env(k, sr, 0.002, 0.02) * 0.4
    parts = [(0, crack, sr), (0, thunk(rng, sr, 260, 140, 0.03, 2000, 0.6), sr)]
    for _ in range(6):  # 얼음 조각이 짤랑
        f = rng.uniform(1600, 3000)
        bn = int(0.16 * sr)
        parts.append((rng.uniform(0.0, 0.13), bell(f, bn, sr, rng.uniform(0.03, 0.06), 0.15) * rng.uniform(0.12, 0.22), sr))
    return mix(n, *parts)


def sfx_land_crate(rng, sr=SFX_SR):
    n = int(0.34 * sr)
    boom = osc(sweep(105, 62, n, sr, tau=0.06), sr) * env(n, sr, 0.002, 0.08)
    k = int(0.08 * sr)
    wood = bandpass(noise(k, rng), sr, 180, 1200) * env(k, sr, 0.001, 0.025) * 0.9
    knock = woodblock(430, int(0.06 * sr), sr, 0.018) * 0.35
    knock2 = woodblock(470, int(0.05 * sr), sr, 0.014) * 0.18
    return mix(n, (0, boom, sr), (0, wood, sr), (0, knock, sr), (0.07, knock2, sr))


def sfx_side_pass(rng, sr=SFX_SR):
    """옆 위험물이 곁을 지나감: 다가왔다 멀어지는 '휘융'."""
    n = int(0.46 * sr)
    t = np.arange(n) / sr
    u = t / t[-1]
    fc = 400 + 1200 * np.sin(np.pi * u) ** 2
    air = svf_bandpass(noise(n, rng), sr, fc, q=2.0)
    air = air / np.max(np.abs(air)) * np.sin(np.pi * u) ** 2
    tone = osc(520 - 130 * u, sr, "tri") * np.sin(np.pi * u) ** 3 * 0.18
    return air + tone


# --------------------------------------------------------------------------- 효과음: 라운드·UI


def sfx_count(rng, sr=SFX_SR):
    n = int(0.25 * sr)
    return marimba(midi(76), n, sr, 0.09, 1.0, rng) + 0.3 * bell(midi(88), n, sr, 0.04, 0.2)


def sfx_go(rng, sr=SFX_SR):
    n = int(0.55 * sr)
    x = np.zeros(n)
    for m in (81, 85, 88):
        x += marimba(midi(m), n, sr, 0.2, 1.0, rng) + 0.18 * bell(midi(m) * 2, n, sr, 0.1, 0.3)
    return x


def sfx_win(rng, sr=SFX_SR):
    n = int(1.9 * sr)
    parts = []
    for i, m in enumerate([72, 76, 79, 84]):
        parts.append((i * 0.12, marimba(midi(m), int(0.6 * sr), sr, 0.25, 1.0, rng), sr))
    chord_n = int(1.4 * sr)
    for m in (84, 88, 91):
        parts.append((0.48, marimba(midi(m), chord_n, sr, 0.55, 0.8, rng) * 0.7 + bell(midi(m), chord_n, sr, 0.5, 0.4) * 0.3, sr))
    return mix(n, *parts)


def sfx_lose(rng, sr=SFX_SR):
    n = int(1.7 * sr)
    parts = []
    for m, at, d in [(67, 0.0, 0.22), (66, 0.26, 0.22), (65, 0.52, 0.22), (64, 0.78, 0.8)]:
        bn = int((d + 0.15) * sr)
        t = np.arange(bn) / sr
        long = d > 0.5
        f = midi(m) * (1 + 0.025 * np.sin(2 * np.pi * 6 * t) if long else 1) * (1 - 0.03 * (t / t[-1]) if long else 1)
        e = env(bn, sr, 0.02, 0.12, hold=d - 0.05)
        parts.append((at, (osc(f, sr, "tri") + 0.3 * osc(f, sr)) * e, sr))
    return mix(n, *parts)


def sfx_click(rng, sr=SFX_SR):
    n = int(0.06 * sr)
    return osc(sweep(1300, 800, n, sr, tau=0.01), sr) * env(n, sr, 0.0015, 0.014)


SFX = [
    # 이름, 함수, 로우패스(Hz)
    ("sfx_jump", sfx_jump, 5000),
    ("sfx_land", sfx_land, 3500),
    ("sfx_dash", sfx_dash, 5000),
    ("sfx_dash_hit", sfx_dash_hit, 4500),
    ("sfx_stomp", sfx_stomp, 4500),
    ("sfx_crouch", sfx_crouch, 3000),
    ("sfx_death", sfx_death, 4500),
    ("sfx_stun", sfx_stun, 6000),
    ("sfx_immune", sfx_immune, 6000),
    ("sfx_warn", sfx_warn, 6000),
    ("sfx_side_warn", sfx_side_warn, 5000),
    ("sfx_land_knife", sfx_land_knife, 6000),
    ("sfx_land_fork", sfx_land_fork, 6500),
    ("sfx_land_icicle", sfx_land_icicle, 5500),
    ("sfx_land_crate", sfx_land_crate, 3500),
    ("sfx_side_pass", sfx_side_pass, 5000),
    ("sfx_count", sfx_count, 6000),
    ("sfx_go", sfx_go, 7000),
    ("sfx_win", sfx_win, 7000),
    ("sfx_lose", sfx_lose, 4500),
    ("sfx_click", sfx_click, 5000),
]


def finish_sfx(name, x, lp, sr=SFX_SR):
    """걸러내기(앞뒤를 0으로 늘려 감싸기 없음) → 페이드 → 단기 크기 -14 dB, 피크 -1 dBFS 아래."""
    x = lowpass(x, sr, lp, 2)
    x = highpass(x, sr, 60, 2)
    a = int(0.002 * sr)
    x[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))
    k = min(len(x), int(0.025 * sr))
    x[-k:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, k))
    loud = short_term_loudness(x, sr)
    gain = 10 ** ((SFX_FILE_LOUDNESS - loud) / 20)
    peak = np.max(np.abs(x)) * gain
    ceiling = 10 ** (SFX_PEAK_CEILING / 20)
    if peak > ceiling:
        gain *= ceiling / peak
    x = x * gain
    write(name, x, sr)
    loud = short_term_loudness(x, sr)
    t = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(len(x), 1 / sr)
    hf = 100 * t[f > 4000].sum() / t.sum()
    print(f"{name:18s} {len(x) / sr:5.2f}s  loud {loud:6.1f} dB  peak {db(np.max(np.abs(x))):5.1f} dBFS  >4kHz {hf:4.1f}%  target {SFX_TARGET[name]}")
    return loud


def write_levels(levels, bgm_levels):
    lines = [
        "// 자동 생성: Client/SoundGen/gen_sounds.py 가 쓴다. 손으로 고치지 않는다(docs/work/juice-sound.md 0장 기준 v2).",
        "// 효과음 파일마다 '종류별 목표 크기 - 파일의 들리는 크기' 만큼의 재생 배수.",
        "namespace OnlineSeverGame.Client.Juice",
        "{",
        "    public static class JuiceLevels",
        "    {",
        "        public static float Sfx(string file)",
        "        {",
        "            switch (file)",
        "            {",
    ]
    for name, loud in levels.items():
        g = 10 ** ((SFX_TARGET[name] - loud) / 20)
        lines.append(f"                case \"{name}\": return {g:.4f}f; // 목표 {SFX_TARGET[name]} dB, 파일 {loud:.1f} dB")
    lines += ["                default: return 0.3f;", "            }", "        }", "",
              "        // 배경음 파일마다 '목표 크기(A 가중 RMS %.0f dB) - 파일 크기' 만큼의 배수(피크 여유 때문에 덜 올린 곡을 채운다)." % BGM_LOUDNESS,
              "        public static float Bgm(string file)", "        {", "            switch (file)", "            {"]
    for name, loud in bgm_levels.items():
        lines.append(f"                case \"{name}\": return {10 ** ((BGM_LOUDNESS - loud) / 20):.4f}f; // 파일 {loud:.1f} dB")
    lines += ["                default: return 1f;", "            }", "        }", "    }", "}", ""]
    with open(LEVELS_CS, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines))


# =========================================================================== 배경음

NOTE = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def parse_note(s):
    """'C#5', 'Bb4' → MIDI 번호."""
    m = NOTE[s[0]]
    i = 1
    while i < len(s) and s[i] in "#b":
        m += 1 if s[i] == "#" else -1
        i += 1
    return m + 12 * (int(s[i:]) + 1)


CHORD_TYPES = {"": [0, 4, 7], "m": [0, 3, 7], "7": [0, 4, 7, 10], "m7": [0, 3, 7, 10], "maj7": [0, 4, 7, 11]}


def parse_chord(s):
    root = NOTE[s[0]]
    i = 1
    while i < len(s) and s[i] in "#b":
        root += 1 if s[i] == "#" else -1
        i += 1
    return root % 12, CHORD_TYPES[s[i:]]


def parse_song(melody, chords, beats):
    """선율 '음:박 ...|...' 와 화음 'C | Am | Dm G ...' 를 마디별 목록으로. 마디 박 합이 맞는지 검사한다."""
    bars = [b.split() for b in melody.split("|")]
    mel = []
    for bi, toks in enumerate(bars):
        beat = 0.0
        for tok in toks:
            n, d = tok.split(":")
            d = float(d)
            if n != "r":
                mel.append((bi, beat, d, parse_note(n)))
            beat += d
        assert abs(beat - beats) < 1e-6, f"bar {bi + 1}: {beat} beats"
    chs = []
    for bi, c in enumerate(chords.split("|")):
        names = c.split()
        span = beats / len(names)
        for k, nm in enumerate(names):
            chs.append((bi, k * span, span, parse_chord(nm)))
    assert len(bars) == len(chords.split("|")), "melody/chord bar count"
    return mel, chs, len(bars)


def voicing(root, ivs, center, prev=None):
    """화음 음을 center 근처에 쌓는다. 앞 화음과 가장 가깝게(부드러운 성부 진행)."""
    best, best_cost = None, 1e9
    for inv in range(len(ivs)):
        notes = [root + iv for iv in ivs]
        notes = notes[inv:] + [n + 12 for n in notes[:inv]]
        for octv in range(2, 7):
            v = [n + 12 * octv for n in notes]
            cost = abs(np.mean(v) - center) * (0.6 if prev else 1.0)
            if prev:
                cost += sum(min(abs(a - b) for b in prev) for a in v) * 0.5
            if cost < best_cost:
                best, best_cost = v, cost
    return best


# ---------------------------------------------------------------- 배경음 악기(배경음 sr)


def inst_marimba(f, d, vel, sr, rng):
    return marimba(f, int((d + 0.9) * sr), sr, None, vel, rng)


def inst_celesta(f, d, vel, sr, rng):
    n = int((d + 1.6) * sr)
    t = np.arange(n) / sr
    tau = float(np.clip(1.1 * (523.0 / f) ** 0.5, 0.4, 1.6))
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.18 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t / (tau * 0.5))
    x += 0.05 * np.sin(2 * np.pi * f * 3.0 * t) * np.exp(-t / (tau * 0.3))
    x += 0.03 * np.sin(2 * np.pi * f * 4.2 * t) * np.exp(-t / (tau * 0.15))
    a = int(0.002 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x * vel


def inst_musicbox(f, d, vel, sr, rng):
    n = int((d + 1.4) * sr)
    t = np.arange(n) / sr
    x = np.zeros(n)
    for det, amp in ((1.0, 1.0), (1.0012, 0.6)):  # 살짝 어긋난 두 줄 = 반짝임
        ff = f * det
        x += amp * (np.sin(2 * np.pi * ff * t) * np.exp(-t / 0.9)
                    + 0.22 * np.sin(2 * np.pi * ff * 2.0 * t) * np.exp(-t / 0.4)
                    + 0.06 * np.sin(2 * np.pi * ff * 3.0 * t) * np.exp(-t / 0.15)
                    + 0.035 * np.sin(2 * np.pi * ff * 5.2 * t) * np.exp(-t / 0.08))
    a = int(0.0015 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x * vel / 1.6


def inst_glock(f, d, vel, sr, rng):
    n = int((d + 1.2) * sr)
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.8) + 0.15 * np.sin(2 * np.pi * f * 2.71 * t) * np.exp(-t / 0.25)
    a = int(0.001 * sr)
    x[:a] *= np.linspace(0, 1, a)
    return x * vel


def karplus(f, n, sr, rng, bright=0.5, decay=0.996):
    """뜯는 줄(Karplus-Strong). 지연 길이 N 블록씩 한꺼번에 계산한다."""
    N = max(2, int(round(sr / f)))
    y = np.zeros(n + N + 1)
    burst = rng.uniform(-1, 1, N + 1)
    burst = lowpass(burst, sr, 600 + 5000 * bright)
    y[:N + 1] = burst / max(np.max(np.abs(burst)), 1e-9)
    a = 0.5
    k = N + 1
    while k < len(y):
        m = min(N, len(y) - k)
        y[k:k + m] = decay * (a * y[k - N:k - N + m] + (1 - a) * y[k - N - 1:k - N - 1 + m])
        k += m
    out = y[N + 1:N + 1 + n]
    a = min(len(out), max(2, int(0.002 * sr)))
    out[:a] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, a))  # 뜯는 순간 '딱' 없이
    return out


def inst_pluck(f, d, vel, sr, rng, muted=False):
    n = int((d + (0.08 if muted else 0.5)) * sr)
    x = karplus(f, n, sr, rng, bright=0.25 + 0.35 * vel, decay=0.990 if muted else 0.997)
    t = np.arange(n) / sr
    if muted:
        x *= np.exp(-t / 0.12)
    else:
        x *= np.clip((d + 0.5 - t) / 0.3, 0, 1)
    return x * vel


def inst_ep(f, d, vel, sr, rng):
    """FM 전자 피아노: 망치가 닿을 때 밝고 곧 둥글어진다."""
    n = int((d + 0.6) * sr)
    t = np.arange(n) / sr
    index = (1.2 * vel + 0.2) * np.exp(-t / 0.18) + 0.25
    x = np.sin(2 * np.pi * f * t + index * np.sin(2 * np.pi * f * t))
    e = (1 - np.exp(-t / 0.002)) * np.exp(-t / 1.1) * np.clip((d + 0.6 - t) / 0.25, 0, 1)
    return x * e * vel


def inst_bass(f, d, vel, sr, rng):
    n = int((d + 0.08) * sr)
    t = np.arange(n) / sr
    x = np.sin(2 * np.pi * f * t) + 0.25 * (2 / np.pi) * np.arcsin(np.sin(2 * np.pi * 2 * f * t))
    e = (1 - np.exp(-t / 0.006)) * (0.55 + 0.45 * np.exp(-t / 0.12))
    e *= np.clip((d + 0.08 - t) / 0.06, 0, 1)
    return x * e * vel


def inst_bass_pluck(f, d, vel, sr, rng):
    n = int((d + 0.1) * sr)
    soft = inst_bass(f, d, 1.0, sr, rng)
    x = karplus(f, n, sr, rng, bright=0.15, decay=0.998)
    x[:min(n, len(soft))] += 0.5 * soft[:n]
    x *= np.clip((d + 0.1 - np.arange(n) / sr) / 0.06, 0, 1)
    return x * vel


def inst_pad(f, d, vel, sr, rng):
    n = int((d + 0.8) * sr)
    t = np.arange(n) / sr
    x = np.zeros(n)
    for det, ph in ((1.0, 0.0), (1.004, 1.7), (0.996, 3.1)):
        for h in range(1, 7):
            x += np.sin(2 * np.pi * f * det * h * t + ph * h) / h ** 1.6
    e = np.clip(t / 0.45, 0, 1) ** 2 * np.clip((d + 0.8 - t) / 0.8, 0, 1)
    return x * e * vel / 3


# ---------------------------------------------------------------- 타악기


# 타악기 종류별 상대 크기. 한 번 칠 때의 크기(20 ms 최대 RMS)를 이 값으로 맞춘 뒤 세기를 곱한다:
# 줄기 안에 여러 종류가 섞여도(림 + 필인 나무 블록) 한 종류가 갑자기 튀지 않게.
DRUM_LEVEL = {"kick": 1.0, "brush": 0.6, "clap": 0.6, "rim": 0.45, "hat": 0.3, "shaker": 0.3, "block": 0.45, "block_hi": 0.4,
              "triangle": 0.3, "sleigh": 0.35, "clank": 0.4}


def drum(kind, sr, rng, vel=1.0):
    x = _drum(kind, sr, rng)
    win = max(1, int(0.02 * sr))
    peak_rms = max(np.sqrt(np.mean(x[i:i + win] ** 2)) for i in range(0, max(1, len(x) - win), win // 2))
    return x / max(peak_rms, 1e-9) * 0.25 * DRUM_LEVEL[kind] * vel


def _drum(kind, sr, rng):
    if kind == "kick":
        n = int(0.3 * sr)
        x = osc(sweep(120, 46, n, sr, tau=0.025), sr) * env(n, sr, 0.002, 0.13)
        k = int(0.004 * sr)
        x[:k] += lowpass(rng.standard_normal(k), sr, 1800) * 0.15 * np.sin(np.linspace(0, np.pi, k))
    elif kind == "brush":
        n = int(0.16 * sr)
        x = bandpass(rng.standard_normal(n), sr, 1200, 5000) * env(n, sr, 0.004, 0.05) * 0.8
        x += np.sin(2 * np.pi * 190 * np.arange(n) / sr) * env(n, sr, 0.002, 0.035) * 0.35
    elif kind == "clap":
        n = int(0.18 * sr)
        x = np.zeros(n)
        for o in (0.0, 0.009, 0.018):
            i = int(o * sr)
            k = n - i
            x[i:] += bandpass(rng.standard_normal(k), sr, 900, 3200) * env(k, sr, 0.002, 0.012 if o < 0.018 else 0.05)
        x *= 0.6
    elif kind == "rim":
        n = int(0.06 * sr)
        x = woodblock(1700, n, sr, 0.012) * 0.5 + highpass(rng.standard_normal(n), sr, 3000) * env(n, sr, 0.002, 0.006) * 0.2
    elif kind == "hat":
        n = int(0.06 * sr)
        x = lowpass(highpass(rng.standard_normal(n), sr, 5000), sr, 7500) * env(n, sr, 0.003, 0.018)
    elif kind == "shaker":
        n = int(0.08 * sr)
        x = bandpass(rng.standard_normal(n), sr, 3000, 7000) * env(n, sr, 0.012, 0.022)
    elif kind == "block":
        n = int(0.1 * sr)
        x = woodblock(880, n, sr, 0.028)
    elif kind == "block_hi":
        n = int(0.1 * sr)
        x = woodblock(1240, n, sr, 0.022)
    elif kind == "triangle":
        n = int(1.0 * sr)
        x = bell(2600, n, sr, 0.5, 0.2) * 0.5
    elif kind == "sleigh":
        n = int(0.15 * sr)
        x = np.zeros(n)
        for _ in range(5):
            i = int(rng.uniform(0, 0.012) * sr)
            x[i:] += bell(rng.uniform(3200, 5200), n - i, sr, rng.uniform(0.02, 0.05), 0.1) * rng.uniform(0.2, 0.4)
        x += bandpass(rng.standard_normal(n), sr, 4000, 7500) * env(n, sr, 0.004, 0.02) * 0.2
    elif kind == "clank":
        n = int(0.5 * sr)
        x = metal_ring(n, sr, [310, 870, 1530, 2290], [0.6, 0.35, 0.15, 0.06], [0.22, 0.12, 0.07, 0.04])
        x += lowpass(rng.standard_normal(n), sr, 1800) * env(n, sr, 0.001, 0.01) * 0.3
    else:
        raise ValueError(kind)
    return x


# ---------------------------------------------------------------- 곡 조립


class Mixer:
    """악기 줄기(stem)별로 반복 버퍼에 쌓고, 줄기마다 크기를 맞춰 스테레오로 합친다."""

    def __init__(self, sr, n):
        self.sr, self.n = sr, n
        self.stems = {}

    def add(self, stem, i0, sig):
        buf = self.stems.setdefault(stem, np.zeros(self.n))
        i0 %= self.n
        m = min(len(sig), self.n - i0)
        buf[i0:i0 + m] += sig[:m]
        rest = sig[m:]
        while len(rest):  # 반복 곡: 끝을 넘는 꼬리는 처음에 더한다(이음매 없음)
            k = min(len(rest), self.n)
            buf[:k] += rest[:k]
            rest = rest[k:]

    def active_level(self, x):
        """소리 나는 구간의 크기: 100 ms 창 RMS 의 95번째 백분위(dB). 듬성한 줄기가 지나치게 커지지 않게."""
        win = int(0.1 * self.sr)
        r = [np.sqrt(np.mean(x[i:i + win] ** 2)) for i in range(0, self.n - win, win // 2)]
        return db(np.percentile(r, 95))

    def render(self, layout, rng):
        """layout: stem → (목표 dB, 좌우 -1..1, 잔향 보내기 0..1[, "fixed"]).
        선율·화음 줄기는 소리 나는 구간의 크기를 목표에 맞춘다. 북 줄기("fixed")는 한 번 칠 때 크기를 drum() 이 이미
        맞췄으므로 목표 값을 그대로 dB 배수로 곱한다(짧은 딸깍 소리는 100 ms 창으로 재면 작게 나와 지나치게 키워지기 때문)."""
        L = np.zeros(self.n)
        R = np.zeros(self.n)
        sendL = np.zeros(self.n)
        sendR = np.zeros(self.n)
        for stem, x in self.stems.items():
            target, pan, send = layout[stem][:3]
            fixed = len(layout[stem]) > 3 and layout[stem][3] == "fixed"
            x = x * 10 ** ((target if fixed else target - self.active_level(x)) / 20)
            gl, gr = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
            L += x * gl * 1.414
            R += x * gr * 1.414
            sendL += x * gl * send
            sendR += x * gr * send
        # 좌우 다른 잔향(원형 컨볼루션: 꼬리가 처음으로 이어진다)
        for buf, send, seed in ((L, sendL, 1), (R, sendR, 2)):
            r = np.random.default_rng(SEED + seed)
            ir_n = int(1.6 * self.sr)
            t = np.arange(ir_n) / self.sr
            ir = r.standard_normal(ir_n) * np.exp(-t / 0.45)
            ir[: int(0.015 * self.sr)] = 0  # 15 ms 늦게
            ir = lowpass(ir, self.sr, 3200)
            ir /= np.sqrt(np.sum(ir ** 2))
            pad = np.zeros(self.n)
            pad[:min(ir_n, self.n)] = ir[: self.n]
            buf += np.fft.irfft(np.fft.rfft(send) * np.fft.rfft(pad), self.n) * 0.9
        out = np.vstack([L, R])
        for c in range(2):
            x = out[c]
            x = highpass(x, self.sr, 35, 2, circular=True)
            # 효과음 자리(2~5 kHz)를 3 dB 비우고, 말랑하게 9 kHz 위를 깎는다
            x = spectral(x, self.sr, lambda f: 1 - 0.29 * np.exp(-((f - 3200) / 1400) ** 2), circular=True)
            out[c] = lowpass(x, self.sr, 9000, 2, circular=True)
        return out


def master(out, sr):
    """A 가중 RMS 를 BGM_LOUDNESS 로 맞추되, 피크가 -2 dBFS 를 넘으면 그만큼 덜 올린다(리미터로 눌러 찌그러뜨리지 않게).
    덜 올린 만큼은 JuiceLevels.Bgm 배수로 실행 중에 채운다."""
    mono = out.mean(axis=0)
    a = a_weight(mono, sr, circular=True)
    rms = np.sqrt(np.mean(a ** 2))
    gain = 10 ** ((BGM_LOUDNESS - db(rms)) / 20)
    peak = np.max(np.abs(out)) * gain
    if peak > 10 ** (-2 / 20):
        gain *= 10 ** (-2 / 20) / peak
    out = out * gain
    # 안전망: -3 dBFS 위만 tanh 로 살짝(피크를 -2 로 맞췄으므로 거의 닿지 않는다)
    knee, ceil = 10 ** (-3 / 20), 10 ** (-1 / 20)
    mag = np.abs(out)
    over = mag > knee
    out[over] = np.sign(out[over]) * (knee + (ceil - knee) * np.tanh((mag[over] - knee) / (ceil - knee)))
    return out


class Song:
    def __init__(self, name, bpm, beats, melody, chords, swing=0.0, repeats=1):
        self.name, self.bpm, self.beats, self.swing, self.repeats = name, bpm, beats, swing, repeats
        self.mel, self.chords, self.bars = parse_song(melody, chords, beats)
        self.beat_s = 60.0 / bpm
        self.total_bars = self.bars * repeats
        self.sr = BGM_SR
        self.n = int(round(self.total_bars * beats * self.beat_s * self.sr))
        self.mixer = Mixer(self.sr, self.n)

    def at(self, bar, beat, rng=None, jitter=0.004):
        b = beat
        frac = beat - np.floor(beat)
        if self.swing and abs(frac - 0.5) < 1e-6:
            b += self.swing * 0.5
        t = (bar * self.beats + b) * self.beat_s
        if rng is not None:
            t += rng.normal(0, jitter)
        return int(round(t * self.sr))

    def chord_at(self, bar, beat):
        bar %= self.bars
        cur = None
        for (b, s, d, ch) in self.chords:
            if b == bar and s <= beat + 1e-6:
                cur = ch
        return cur

    def next_chord_root(self, bar, beat):
        """다음 화음의 뿌리음(베이스가 다가가는 음)."""
        bar %= self.bars
        later = [(b, s) for (b, s, d, ch) in self.chords if (b, s) > (bar, beat + 1e-6)]
        b, s = later[0] if later else (0, 0.0)
        return self.chord_at(b, s)[0]


def play_melody(song, rng, inst, stem, transpose=0, vel=0.9, only=None, legato=0.95):
    for rep in range(song.repeats):
        for (bar, beat, d, m) in song.mel:
            if only is not None and not only(rep, bar):
                continue
            accent = 1.0 if beat == 0 else 0.92 if abs(beat - round(beat)) < 1e-6 else 0.84
            v = vel * accent * rng.uniform(0.92, 1.0)
            sig = inst(midi(m + transpose), d * song.beat_s * legato, v, song.sr, rng)
            song.mixer.add(stem, song.at(rep * song.bars + bar, beat, rng), sig)


def play_comp(song, rng, inst, stem, pattern, center=62, vel=0.7, only=None):
    """pattern: [(박, 길이, 화음 음 번호들 또는 'all')] 마디마다 반복."""
    prev = None
    for rep in range(song.repeats):
        for bar in range(song.bars):
            if only is not None and not only(rep, bar):
                continue
            for beat, d, which in pattern:
                root, ivs = song.chord_at(bar, beat)
                v_notes = voicing(root, ivs, center, prev)
                prev = v_notes
                notes = v_notes if which == "all" else [v_notes[i % len(v_notes)] + 12 * (i // len(v_notes)) for i in which]
                for k, m in enumerate(notes):
                    sig = inst(midi(m), d * song.beat_s, vel * rng.uniform(0.85, 1.0), song.sr, rng)
                    song.mixer.add(stem, song.at(rep * song.bars + bar, beat, rng) + int(k * 0.006 * song.sr), sig)


def play_bass(song, rng, inst, stem, pattern, octave=36, vel=0.9):
    """pattern: [(박, 길이, 'R'|'5'|'8'|'A')] — 뿌리·5도·옥타브·다음 화음으로 다가가는 음."""
    for rep in range(song.repeats):
        for bar in range(song.bars):
            for beat, d, kind in pattern:
                root, ivs = song.chord_at(bar, beat)
                base = octave + root
                if base > octave + 7:
                    base -= 12
                if kind == "R":
                    m = base
                elif kind == "5":
                    m = base + 7
                elif kind == "8":
                    m = base + 12
                else:  # 다음 뿌리음 반음 아래나 위에서 다가감
                    nr = song.next_chord_root(bar, beat)
                    target = octave + nr
                    if target > octave + 7:
                        target -= 12
                    m = target - 1 if target > base else target + 1
                sig = inst(midi(m), d * song.beat_s, vel * rng.uniform(0.88, 1.0), song.sr, rng)
                song.mixer.add(stem, song.at(rep * song.bars + bar, beat, rng, 0.003), sig)


def play_drums(song, rng, stem, pattern, fills=None):
    """pattern: {종류: [(박, 세기)]} 마디마다. fills: {마디 번호: {종류: [(박, 세기)]}} 그 마디는 이것으로 바꾼다."""
    for rep in range(song.repeats):
        for bar in range(song.bars):
            p = fills.get(bar, pattern) if fills else pattern
            for kind, hits in p.items():
                for beat, vel in hits:
                    sig = drum(kind, song.sr, rng, vel * rng.uniform(0.85, 1.0))
                    # 북 종류마다 줄기를 나눈다: 킥 / 찰찰이(하이햇·셰이커·썰매 방울, 작고 어둡게) / 나머지 타악기
                    st = "kick" if kind == "kick" else "hats" if kind in ("hat", "shaker", "sleigh") else stem
                    song.mixer.add(st, song.at(rep * song.bars + bar, beat, rng, 0.003), sig)


def eighths(beats, v_on=0.9, v_off=0.6):
    return [(b * 0.5, v_on if b % 2 == 0 else v_off) for b in range(int(beats * 2))]


# ---------------------------------------------------------------- 곡 1: 주방 (C장조, 112, 통통 튀는 마림바)

KITCHEN_MELODY = """
E5:.5 G5:.5 C6:1 B5:.5 A5:.5 G5:1 | A5:1.5 G5:.5 E5:1 r:1 | F5:.5 A5:.5 C6:1 A5:.5 F5:.5 A5:1 | G5:2 D5:1 r:1 |
E5:.5 G5:.5 C6:1 D6:.5 C6:.5 B5:1 | A5:1 C6:1 E5:1 r:1 | F5:.5 E5:.5 D5:1 G5:.5 F5:.5 D5:1 | C5:2 r:2 |
A5:1.5 G5:.5 F5:1 A5:1 | B5:1.5 A5:.5 G5:2 | G5:1 B5:1 E6:1.5 D6:.5 | C6:2 A5:1 r:1 |
A5:.5 C6:.5 F6:1 E6:.5 D6:.5 C6:1 | B5:.5 D6:.5 G6:1 F6:.5 E6:.5 D6:1 | F6:1 E6:.5 D6:.5 C6:1 A5:1 | B5:1 G5:.5 A5:.5 B5:1 D6:1
"""
KITCHEN_CHORDS = "C | Am | F | G | C | Am | Dm G | C | F | G | Em | Am | F | G | Dm7 | G7"


def bgm_kitchen(rng):
    s = Song("bgm_kitchen", 112, 4, KITCHEN_MELODY, KITCHEN_CHORDS)
    play_melody(s, rng, inst_marimba, "lead", vel=0.95)
    play_melody(s, rng, inst_glock, "double", transpose=12, vel=0.5, only=lambda rep, bar: bar >= 8)
    play_comp(s, rng, lambda f, d, v, sr, r: inst_pluck(f, d, v, sr, r, muted=True), "comp",
              [(0.5, 0.4, "all"), (1.5, 0.4, [1, 2]), (2.5, 0.4, "all"), (3.5, 0.4, [1, 2])], center=60)
    play_bass(s, rng, inst_bass, "bass", [(0, 1.0, "R"), (1.5, 0.5, "5"), (2, 1.0, "R"), (3, 0.5, "8"), (3.5, 0.5, "A")])
    groove = {"kick": [(0, 1.0), (2, 0.9), (2.5, 0.5)], "rim": [(1, 0.8), (3, 0.8)], "shaker": eighths(4, 0.7, 0.45)}
    fill = {"kick": [(0, 1.0), (2, 0.9)], "rim": [(1, 0.8)], "block": [(2.5, 0.6), (3, 0.7), (3.25, 0.6), (3.5, 0.8), (3.75, 0.9)],
            "shaker": eighths(2, 0.7, 0.45)}
    play_drums(s, rng, "perc", groove, {7: fill, 15: fill})
    out = s.mixer.render({
        "lead": (-17, -0.15, 0.25), "double": (-27, 0.35, 0.35), "comp": (-24, 0.4, 0.2),
        "bass": (-21, 0.0, 0.03), "kick": (-9, 0.0, 0.02, "fixed"), "perc": (-7, -0.3, 0.12, "fixed"), "hats": (-10, 0.3, 0.1, "fixed"),
    }, rng)
    return s, out


# ---------------------------------------------------------------- 곡 2: 겨울 처마 (A단조, 92, 첼레스타·하프·패드)

WINTER_MELODY = """
E5:1.5 D5:.5 C5:1 E5:1 | A5:2 G5:1 F5:1 | E5:1.5 D5:.5 C5:1 G4:1 | D5:3 r:1 |
E5:1 A5:1 G5:.5 F5:.5 E5:1 | F5:1.5 E5:.5 C5:2 | D5:1 F5:1 A5:1 G5:.5 F5:.5 | E5:2 G#4:1 B4:1 |
C5:1 F5:1 A5:1.5 G5:.5 | G5:1 D5:1 B4:2 | B4:1 E5:1 G5:1 B5:1 | A5:3 r:1 |
F5:1 A5:1 D6:1.5 C6:.5 | B5:1 G5:1 D5:2 | E5:1 G5:1 C6:1 B5:.5 A5:.5 | G#5:2 B5:1 D6:1
"""
WINTER_CHORDS = "Am | F | C | G | Am | F | Dm | E | F | G | Em | Am | Dm | G | C | E7"


def bgm_winter(rng):
    s = Song("bgm_winter", 92, 4, WINTER_MELODY, WINTER_CHORDS)
    play_melody(s, rng, inst_celesta, "lead", vel=0.9)
    play_melody(s, rng, inst_musicbox, "double", transpose=12, vel=0.45, only=lambda rep, bar: bar >= 8)
    # 하프처럼 오르내리는 8분음표 뜯기
    play_comp(s, rng, inst_pluck, "comp",
              [(0, 0.5, [0]), (0.5, 0.5, [1]), (1, 0.5, [2]), (1.5, 0.5, [3]), (2, 0.5, [4]), (2.5, 0.5, [3]), (3, 0.5, [2]), (3.5, 0.5, [1])],
              center=55, vel=0.6)
    play_comp(s, rng, inst_pad, "pad", [(0, 4, "all")], center=60, vel=0.5)
    play_bass(s, rng, inst_bass, "bass", [(0, 2, "R"), (2, 1.5, "5"), (3.5, 0.5, "A")], vel=0.8)
    groove = {"sleigh": eighths(4, 0.6, 0.35), "brush": [(3, 0.6)], "kick": [(0, 0.6)]}
    sparkle = dict(groove, triangle=[(0, 0.6)])
    play_drums(s, rng, "perc", groove, {0: sparkle, 4: sparkle, 8: sparkle, 12: sparkle})
    out = s.mixer.render({
        "lead": (-17, 0.1, 0.4), "double": (-28, -0.35, 0.5), "comp": (-24, -0.4, 0.35), "pad": (-27, 0.25, 0.5),
        "bass": (-21, 0.0, 0.05), "kick": (-15, 0.0, 0.05, "fixed"), "perc": (-11, 0.35, 0.25, "fixed"), "hats": (-9, -0.2, 0.3, "fixed"),
    }, rng)
    return s, out


# ---------------------------------------------------------------- 곡 3: 창고 (D 도리안, 100, 스윙, 뜯는 줄·전자 피아노)

WAREHOUSE_MELODY = """
D5:.5 r:.5 F5:.5 G5:.5 A5:1 r:.5 G5:.5 | F5:.5 r:.5 D5:.5 C5:.5 D5:1.5 r:.5 | D5:.5 r:.5 F5:.5 G5:.5 A5:.5 C6:.5 A5:.5 G5:.5 | B5:1 A5:.5 G5:.5 F5:1 r:1 |
A5:.5 r:.5 C6:.5 A5:.5 G5:1 F5:1 | E5:1 G5:.5 E5:.5 C5:1 r:1 | D5:.5 G5:.5 B5:.5 D6:.5 C6:1 B5:.5 A5:.5 | A5:2 r:2 |
D6:1.5 C6:.5 A5:1 F5:1 | G5:1.5 E5:.5 C5:2 | F5:1 A5:1 C6:1 A5:1 | G5:1.5 E5:.5 C5:1 E5:1 |
F5:.5 A5:.5 D6:1 C6:.5 A5:.5 F5:1 | E5:.5 G5:.5 C6:1 D6:.5 C6:.5 G5:1 | B5:1 D6:1 G5:1 B5:1 | C#6:1.5 A5:.5 E5:1 G5:1
"""
WAREHOUSE_CHORDS = "Dm7 | G7 | Dm7 | G7 | Fmaj7 | C | G | Am7 | Bbmaj7 | C | Dm7 | Am7 | Bbmaj7 | C | G | A7"


def bgm_warehouse(rng):
    s = Song("bgm_warehouse", 100, 4, WAREHOUSE_MELODY, WAREHOUSE_CHORDS, swing=0.16)
    play_melody(s, rng, lambda f, d, v, sr, r: inst_pluck(f, d, v * 0.6, sr, r, muted=False) / 0.6, "lead", vel=0.95, legato=0.8)
    play_melody(s, rng, inst_marimba, "double", transpose=-12, vel=0.6)
    play_comp(s, rng, inst_ep, "comp", [(1.5, 0.5, "all"), (3, 0.75, "all")], center=62, vel=0.6)
    play_bass(s, rng, inst_bass_pluck, "bass",
              [(0, 0.5, "R"), (0.75, 0.25, "R"), (1.5, 0.5, "5"), (2, 0.5, "8"), (2.75, 0.25, "5"), (3.5, 0.5, "A")], octave=36)
    groove = {"kick": [(0, 1.0), (1.5, 0.6), (2, 0.9)], "clap": [(1, 0.7), (3, 0.7)], "hat": eighths(4, 0.7, 0.45),
              "block": [(3.5, 0.4)]}
    clank = dict(groove, clank=[(3.5, 0.5)])
    fill = {"kick": [(0, 1.0), (2, 0.9)], "clap": [(1, 0.7)], "hat": eighths(2, 0.7, 0.45),
            "block": [(2, 0.6), (2.5, 0.6), (3, 0.7), (3.5, 0.9)]}
    play_drums(s, rng, "perc", groove, {1: clank, 5: clank, 9: clank, 13: clank, 7: fill, 15: fill})
    out = s.mixer.render({
        "lead": (-17, 0.2, 0.18), "double": (-25, -0.25, 0.15), "comp": (-24, -0.4, 0.25),
        "bass": (-20, 0.0, 0.02), "kick": (-9, 0.0, 0.02, "fixed"), "perc": (-7, 0.3, 0.1, "fixed"), "hats": (-10, -0.3, 0.08, "fixed"),
    }, rng)
    return s, out


# ---------------------------------------------------------------- 곡 4: 디저트 가게 (F장조, 3/4 왈츠, 132, 뮤직박스)

DESSERT_MELODY = """
A5:1 C6:1 F6:1 | E6:1.5 D6:.5 C6:1 | D6:1 A5:1 F5:1 | A5:2 F5:1 |
D6:1 F6:1 D6:1 | C6:1.5 Bb5:.5 G5:1 | A5:1 F5:1 C6:1 | G5:2 E5:1 |
F5:1 Bb5:1 D6:1 | E6:1.5 D6:.5 C6:1 | C6:1 A5:1 E5:1 | F5:1.5 E5:.5 D5:1 |
Bb5:1 D6:1 G6:1 | E6:1 C6:1 G5:1 | A5:1 C6:.5 A5:.5 F5:1 | E5:1 G5:1 Bb5:1
"""
DESSERT_CHORDS = "F | F | Dm | Dm | Bb | C | F | C7 | Bb | C | Am | Dm | Gm | C | F | C7"


def bgm_dessert(rng):
    s = Song("bgm_dessert", 132, 3, DESSERT_MELODY, DESSERT_CHORDS, repeats=2)
    play_melody(s, rng, inst_musicbox, "lead", vel=0.95)
    play_melody(s, rng, inst_glock, "double", transpose=12, vel=0.45, only=lambda rep, bar: rep == 1)
    play_melody(s, rng, inst_marimba, "counter", transpose=-12, vel=0.5, only=lambda rep, bar: rep == 1 and bar % 2 == 0)
    # 쿵-짝-짝: 베이스 1박, 뜯는 화음 2·3박
    play_comp(s, rng, lambda f, d, v, sr, r: inst_pluck(f, d, v, sr, r, muted=True), "comp", [(1, 0.6, "all"), (2, 0.6, "all")],
              center=62, vel=0.6)
    play_bass(s, rng, inst_bass, "bass", [(0, 1.0, "R")], vel=0.85)
    groove = {"kick": [(0, 0.7)], "shaker": [(1, 0.5), (2, 0.5)], "rim": [(2.5, 0.3)]}
    sparkle = dict(groove, triangle=[(0, 0.5)])
    play_drums(s, rng, "perc", groove, {0: sparkle, 8: sparkle})
    out = s.mixer.render({
        "lead": (-17, 0.0, 0.3), "double": (-28, 0.4, 0.4), "counter": (-26, -0.4, 0.3), "comp": (-24, -0.3, 0.2),
        "bass": (-21, 0.0, 0.03), "kick": (-14, 0.0, 0.03, "fixed"), "perc": (-11, 0.3, 0.15, "fixed"), "hats": (-10, -0.3, 0.12, "fixed"),
    }, rng)
    return s, out


BGM = [("bgm_kitchen", bgm_kitchen), ("bgm_winter", bgm_winter), ("bgm_warehouse", bgm_warehouse), ("bgm_dessert", bgm_dessert)]


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else ""
    levels = {}
    for i, (name, fn, lp) in enumerate(SFX):
        path = os.path.join(OUT, name + ".wav")
        if only in name:
            rng = np.random.default_rng(SEED + i)
            levels[name] = finish_sfx(name, fn(rng), lp)
        else:  # 고치지 않은 파일은 지금 크기를 다시 재서 표를 이어 쓴다
            with wave.open(path) as w:
                x = np.frombuffer(w.readframes(w.getnframes()), "<i2") / 32768.0
            levels[name] = short_term_loudness(x, SFX_SR)
    bgm_levels = {}
    for i, (name, fn) in enumerate(BGM):
        if only in name:
            song, out = fn(np.random.default_rng(SEED + 100 + i))
            out = master(out, song.sr)
            write(name, out, song.sr)
            sr = song.sr
        else:
            with wave.open(os.path.join(OUT, name + ".wav")) as w:
                sr = w.getframerate()
                out = (np.frombuffer(w.readframes(w.getnframes()), "<i2").reshape(-1, 2) / 32768.0).T
        a = a_weight(out.mean(axis=0), sr, circular=True)
        bgm_levels[name] = db(np.sqrt(np.mean(a ** 2)))
        if only in name:
            seam = np.max(np.abs(out[:, 0] - out[:, -1]))
            jump = np.max(np.abs(np.diff(out, axis=1)))
            print(f"{name:18s} {out.shape[1] / sr:5.1f}s stereo {sr} Hz  A-RMS {bgm_levels[name]:6.1f} dB  "
                  f"peak {db(np.max(np.abs(out))):5.1f} dBFS  seam {seam:.3f}  max step {jump:.3f}  {os.path.getsize(os.path.join(OUT, name + '.wav')) // 1024} KB")
    write_levels(levels, bgm_levels)


if __name__ == "__main__":
    main()
