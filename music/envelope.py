# 곡의 박자 지도: 20ms마다 타악기 세기(0~255) 한 바이트
import sys, numpy as np, librosa
src, out = sys.argv[1], sys.argv[2]
y, sr = librosa.load(src, sr=22050, mono=True)
hop = int(sr * 0.02)
o = librosa.onset.onset_strength(y=y, sr=sr, hop_length=hop)
o = o / (np.percentile(o, 98) + 1e-9)
# 튀었다가 서서히 가라앉도록(감쇠) 처리
env = np.zeros_like(o); v = 0
for i, x in enumerate(o):
    v = max(x, v * 0.82); env[i] = v
env = np.clip(env, 0, 1)
open(out, 'wb').write((env * 255).astype(np.uint8).tobytes())
print(out, len(env), 'frames', '%.1fs' % (len(env) * 0.02))
