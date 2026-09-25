"""The needle table for the shader, taken from the simulated eye (glare_ref_2mm.npz).

The corona's needles are lanes of summed speckle, one per direction, and all radial. So the
white-light PSF is resampled onto (radius, angle), the r^-gamma fall of the glow divided out, and
the result averaged along each direction between 0.3 and 2.5 degrees: what is left is how strong
each direction's needle is. Its peaks, strongest first, are the needles.

A needle's length follows from its strength: it stays visible until A r^-gamma drops below what
can be seen, so r_visible ~ A^(1/gamma), the strongest one reaching the burst's full length.

    py glare_sim.py 2.2 && py glare_fit.py glare_ref.npz 160 > needles.glsl
"""
import sys

import numpy as np
from scipy.ndimage import map_coordinates
from scipy.signal import find_peaks

path = sys.argv[1] if len(sys.argv) > 1 else "glare_ref.npz"
COUNT = int(sys.argv[2]) if len(sys.argv) > 2 else 160
d = np.load(path)
rgb, half = d["rgb"].astype(np.float64), float(d["half_deg"])
lum = np.maximum(rgb, 0.0).mean(axis=0)
n = lum.shape[0]
deg_per_px = 2 * half / (n - 1)

radii = np.linspace(0.3, 2.5, 220)
angles = np.linspace(0, 2 * np.pi, 4096, endpoint=False)
RR, AA = np.meshgrid(radii, angles, indexing="ij")
polar = map_coordinates(lum, [RR * np.sin(AA) / deg_per_px + (n - 1) / 2,
                              RR * np.cos(AA) / deg_per_px + (n - 1) / 2], order=1)
env = polar.mean(axis=1, keepdims=True)                 # the glow at each radius
gamma = -np.polyfit(np.log(radii), np.log(env[:, 0]), 1)[0]
lane = (polar / env).mean(axis=0)                       # each direction's strength, glow divided out
contrast = lane.std() / lane.mean()

# Peaks at least ~0.9 deg apart in angle, so neighbours are distinct needles rather than one wide one
peaks, props = find_peaks(lane, distance=10)
order = np.argsort(lane[peaks])[::-1][:COUNT]
sel = peaks[order]
strength = lane[sel] - lane.mean()                      # what stands out above the average lane
strength = np.clip(strength / strength.max(), 0.0, 1.0)
length = strength ** (1.0 / gamma)
theta = angles[sel]
keep = np.argsort(theta)                                 # table in angle order, as before

print(f"// glare_fit.py from {path}: glow falls as r^-{gamma:.2f}; lane contrast {contrast:.3f};"
      f" {len(peaks)} peaks, {COUNT} kept", file=sys.stderr)
print(f"// strength {strength.min():.3f}-{strength.max():.3f}, length {length.min():.2f}-{length.max():.2f}",
      file=sys.stderr)
gaps = np.degrees(np.diff(np.sort(theta)))
print(f"// gaps between needles {gaps.min():.2f}-{gaps.max():.2f} deg", file=sys.stderr)

print(f"        const int rsNeedleCount = {COUNT};")
print(f"        const vec4 rsNeedles[rsNeedleCount] = vec4[rsNeedleCount](")
for j, i in enumerate(keep):
    tail = "," if j < COUNT - 1 else ""
    print(f"            vec4({np.cos(theta[i]):8.5f}, {np.sin(theta[i]):8.5f}, {length[i]:.3f}, {strength[i]:.3f}){tail}")
print("        );")
np.savez("needles.npz", theta=theta[keep], length=length[keep], strength=strength[keep], gamma=gamma)
