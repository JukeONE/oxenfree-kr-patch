# 김쥬크 설치기 배경음 - 오리지널 칩튠 (B 단조, 103 BPM, 60년대 사이키델릭/서프 느낌)
# 멜로디·화음 진행은 새로 작곡. 빠르기와 조성만 참고.
import numpy as np, wave, subprocess

SR = 44100
BPM = 103
BEAT = 60 / BPM

NOTE = {'C':0,'C#':1,'D':2,'D#':3,'E':4,'F':5,'F#':6,'G':7,'G#':8,'A':9,'A#':10,'B':11}
def freq(n):
    name, octv = n[:-1], int(n[-1])
    return 440.0 * 2 ** ((NOTE[name] + 12 * (octv + 1) - 69) / 12)

def pulse(f, dur, duty=0.25, vol=0.2):
    t = np.arange(int(dur * SR)) / SR
    ph = (t * f) % 1.0
    w = np.where(ph < duty, 1.0, -1.0) * vol
    # 짧은 어택/릴리즈 + 약한 비브라토 느낌의 감쇠
    env = np.minimum(1, t / 0.005) * np.exp(-t * 1.2)
    rel = np.minimum(1, (dur - t) / 0.02)
    return w * env * np.clip(rel, 0, 1)

def tri(f, dur, vol=0.32):
    t = np.arange(int(dur * SR)) / SR
    ph = (t * f) % 1.0
    w = (4 * np.abs(ph - 0.5) - 1) * vol
    rel = np.clip((dur - t) / 0.015, 0, 1)
    return w * rel

rng = np.random.default_rng(7)
def noise(dur, vol, decay):
    n = int(dur * SR); t = np.arange(n) / SR
    # 칩튠식 노이즈: 샘플을 몇 개씩 묶어 거친 소리로
    raw = np.repeat(rng.choice([-1.0, 1.0], n // 4 + 1), 4)[:n]
    return raw * vol * np.exp(-t * decay)

def kick(dur=0.18):
    t = np.arange(int(dur * SR)) / SR
    f = 150 * np.exp(-t * 28) + 45
    ph = np.cumsum(f) / SR
    return np.sin(2 * np.pi * ph) * 0.55 * np.exp(-t * 14)

def place(buf, sig, at):
    i = int(at * SR)
    if i >= len(buf): return
    sig = sig[:len(buf) - i]
    buf[i:i + len(sig)] += sig

def seq(notes, start, buf, fn):
    t = start
    for n, b in notes:
        if n != 'R': place(buf, fn(freq(n), b * BEAT), t)
        t += b * BEAT
    return t

def bars(notes):
    s = sum(b for _, b in notes)
    assert abs(s - round(s)) < 1e-9 and round(s) % 4 == 0, (s, notes)
    return notes

# ---------------- 화음 (2마디씩)
CH = {
    'Bm': ['B2', ['B3', 'D4', 'F#4']],
    'G':  ['G2', ['G3', 'B3', 'D4']],
    'A':  ['A2', ['A3', 'C#4', 'E4']],
    'F#': ['F#2', ['F#3', 'A#3', 'C#4']],
    'Em': ['E2', ['E3', 'G3', 'B3']],
}
PROG_A = ['Bm', 'G', 'A', 'F#']          # 각 2마디 → 8마디
PROG_B = ['Em', 'Bm', 'G', 'F#']

# ---------------- 멜로디 (새로 작곡)
LEAD_A = bars([
    ('F#4',1),('B4',.5),('C#5',.5),('D5',1),('C#5',.5),('B4',.5),   ('A4',.5),('B4',1.5),('R',1),('F#4',.5),('A4',.5),
    ('B4',1),('D5',.5),('E5',.5),('F#5',1.5),('E5',.5),             ('D5',.5),('E5',.5),('D5',.5),('B4',.5),('G4',2),
    ('A4',.5),('C#5',.5),('E5',1),('D5',.5),('C#5',.5),('A4',1),    ('E5',1),('F#5',.5),('E5',.5),('C#5',2),
    ('A#4',1),('C#5',.5),('E5',.5),('F#5',1),('E5',.5),('C#5',.5),  ('A#4',1),('F#4',1),('R',2),
])
LEAD_A2 = LEAD_A[:-3] + [('C#5',.5),('A#4',.5),('C#5',1),('B4',2)]
bars(LEAD_A2)
LEAD_B = bars([
    ('G5',1.5),('F#5',.5),('E5',1),('B4',1),                        ('E5',.5),('F#5',.5),('G5',.5),('F#5',.5),('E5',2),
    ('F#5',1.5),('E5',.5),('D5',1),('B4',1),                        ('D5',.5),('E5',.5),('F#5',1),('D5',2),
    ('D5',1),('E5',.5),('D5',.5),('B4',1),('G4',1),                 ('B4',1),('D5',1),('G5',2),
    ('F#5',1),('E5',.5),('C#5',.5),('A#4',1),('C#5',1),             ('F#5',3),('R',1),
])

def section_bass(prog, start, buf):
    t = start
    for c in prog:
        root = CH[c][0]
        up = root[:-1] + str(int(root[-1]) + 1)
        for bar in range(2):
            # 서프 록 느낌의 8분음표 베이스(근음-옥타브 왕복)
            pat = [root, root, up, root, root, up, root, up]
            if bar == 1:
                fifth = CH[c][1][2]; fifth = fifth[:-1] + str(int(fifth[-1]) - 1)
                pat = [root, root, up, root, fifth, fifth, up, root]
            for n in pat:
                place(buf, tri(freq(n), BEAT * 0.5 * 0.92), t); t += BEAT * 0.5
    return t

def section_arp(prog, start, buf, vol=0.07):
    t = start
    for c in prog:
        tones = CH[c][1]
        order = [0, 1, 2, 1] * 4
        for bar in range(2):
            for k in range(8):
                n = tones[order[k]]
                place(buf, pulse(freq(n), BEAT * 0.5 * 0.8, duty=0.125, vol=vol), t); t += BEAT * 0.5
    return t

def section_drums(nbars, start, buf, fill=True):
    t = start
    for b in range(nbars):
        for s in range(8):                       # 8분음표 단위
            at = t + s * BEAT * 0.5
            place(buf, noise(0.04, 0.05 if s % 2 else 0.08, 90), at)          # 하이햇
            if s in (0, 5) or (s == 3 and b % 2): place(buf, kick(), at)       # 킥
            if s in (2, 6): place(buf, noise(0.16, 0.22, 22), at)              # 스네어
        if fill and b == nbars - 1:              # 마지막 마디 필인
            for s in range(4):
                place(buf, noise(0.1, 0.18, 30), t + 2 * BEAT + s * BEAT * 0.25)
        t += 4 * BEAT
    return t

TOTAL_BARS = 4 + 8 + 8 + 8 + 8
length = TOTAL_BARS * 4 * BEAT
lead = np.zeros(int(length * SR) + SR); harm = np.zeros_like(lead); bass = np.zeros_like(lead); drum = np.zeros_like(lead)

t = 0.0
# 인트로 4마디: 베이스+드럼+아르페지오
section_bass(['Bm', 'F#'], t, bass); section_arp(['Bm', 'F#'], t, harm, 0.06); section_drums(4, t, drum)
t += 4 * 4 * BEAT
lp = lambda f, d: pulse(f, d, duty=0.25, vol=0.16)
for prog, mel in [(PROG_A, LEAD_A), (PROG_A, LEAD_A2), (PROG_B, LEAD_B), (PROG_A, LEAD_A2)]:
    section_bass(prog, t, bass); section_arp(prog, t, harm); section_drums(8, t, drum)
    seq(mel, t, lead, lp)
    t += 8 * 4 * BEAT

# 멜로디에 사이키델릭한 에코(3/8박 딜레이)
d = int(BEAT * 0.75 * SR)
echo = np.zeros_like(lead); echo[d:] = lead[:-d] * 0.35; echo[2*d:] += lead[:-2*d] * 0.15
mix = lead + echo + harm + bass + drum
mix = mix[:int(length * SR)]
# 반복 이음매: 끝부분 꼬리를 앞에 겹치기
tail = (lead + echo + harm + bass + drum)[int(length * SR):]
mix[:len(tail)] += tail
mix = np.tanh(mix * 1.1) * 0.8
pcm = (mix * 32767).astype(np.int16)
with wave.open('kimjuke_bgm.wav', 'wb') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-i', 'kimjuke_bgm.wav', '-b:a', '128k', 'kimjuke_bgm.mp3'], check=True)
print('length %.1fs' % length)
