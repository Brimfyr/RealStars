"""The eye's glare, simulated the way Ritschel et al. 2009 (Temporal Glare) do it, as a reference.

An aperture image of the eye - the pupil, particles in the lens nucleus, vitreous, cornea and
(standing in for the retina) a few large ones, and the lens-fibre grating of the cortex - is
Fourier transformed into the point spread function at 575 nm. The colour PSF is then the paper's
sum of 32 copies of that one, each scaled by 575 nm / lambda and weighted by the CIE observer.

One departure. The paper's Fresnel term exp(i pi r^2 / (lambda d)), taken literally with the
20 mm from pupil to retina, is a chirp no sampled aperture can hold (3 rad a sample at the edge
of a 6 mm pupil on a 2 um grid). A focused eye cancels that term with its lens; what is left is
its residual defocus, so that is what is applied: 0.2 dioptres, the usual scale of it.

    py glare_sim.py [pupil_mm] [seed]    -> glare_ref.npz, glare_ref.png
"""
import sys
import time

import numpy as np
import scipy.fft
from scipy.ndimage import map_coordinates
from PIL import Image

PUPIL_MM = float(sys.argv[1]) if len(sys.argv) > 1 else 6.0
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 20260925
N = 4096                     # aperture samples a side
DX = 2.0e-6                  # metres a sample: 8.2 mm across
LAM0 = 575e-9                # the one wavelength the FFT is taken at
DEFOCUS_D = 0.2              # dioptres
OUT = 1024                   # output PSF, pixels a side
OUT_HALF_DEG = 6.0           # and the angle it covers either side of the source

rng = np.random.default_rng(SEED)
t0 = time.time()
c = (np.arange(N, dtype=np.float32) - N / 2) * DX
X, Y = np.meshgrid(c, c)
R = np.hypot(X, Y)
T = np.clip((PUPIL_MM * 0.5e-3 - R) / DX + 0.5, 0.0, 1.0).astype(np.float32)   # soft-edged pupil


def particles(count, r_min, r_max, spread_mm):
    """Opaque discs, log-uniform in radius, uniform over a disc of the given radius."""
    rad = np.exp(rng.uniform(np.log(r_min), np.log(r_max), count))
    rr = spread_mm * 1e-3 * np.sqrt(rng.random(count))
    th = rng.random(count) * 2 * np.pi
    for x0, y0, a in zip(rr * np.cos(th), rr * np.sin(th), rad):
        i0, j0 = int(round(y0 / DX + N / 2)), int(round(x0 / DX + N / 2))
        w = int(a / DX) + 3
        ys, xs = slice(max(i0 - w, 0), min(i0 + w + 1, N)), slice(max(j0 - w, 0), min(j0 + w + 1, N))
        d = np.hypot(X[ys, xs] - x0, Y[ys, xs] - y0)
        T[ys, xs] *= np.clip((d - a) / DX + 0.5, 0.0, 1.0)
    return count


n = 0
n += particles(750, 2e-6, 6e-6, 3.0)      # lens nucleus: many small ones (the paper's 750)
n += particles(60, 6e-6, 15e-6, 3.2)      # vitreous, projected into the pupil plane
n += particles(40, 8e-6, 12e-6, 3.2)      # corneal flat cells: large, sparse, static
n += particles(20, 15e-6, 25e-6, 3.2)     # few large ones standing in for retinal scatter

# The lens cortex: fibres run radially, about 8 um apart, and only the outer lens has the
# regular spacing, so they show in a large pupil and not a small one. Weak (a phase step
# rather than an obstacle), drawn as partial opacity.
FIBRE_UM, CORTEX_MM, GRATING_OPACITY = 8.0, (2.0, 3.0), 0.15
lines = int(round(2 * np.pi * np.mean(CORTEX_MM) * 1e-3 / (FIBRE_UM * 1e-6)))
band = (R >= CORTEX_MM[0] * 1e-3) & (R <= CORTEX_MM[1] * 1e-3)
phi = np.arctan2(Y[band], X[band])
frac = (phi / (2 * np.pi) * lines) % 1.0
dist_px = np.abs(frac - np.round(frac)) * (2 * np.pi * R[band] / lines) / DX
T[band] *= 1.0 - GRATING_OPACITY * np.clip(1.0 - dist_px, 0.0, 1.0)
print(f"aperture: {PUPIL_MM} mm pupil, {n} particles, {lines} cortex fibres ({time.time() - t0:.1f} s)")

# ---- the PSF at 575 nm ---------------------------------------------------------------------
U = (T * np.exp(1j * (np.pi * DEFOCUS_D / LAM0) * (X * X + Y * Y))).astype(np.complex64)
del X, Y, R
F = np.abs(scipy.fft.fftshift(scipy.fft.fft2(U, workers=-1))) ** 2
F = F.astype(np.float64)
F /= F.sum()
del U
rad_per_px = LAM0 / (N * DX)                 # angle per PSF sample at 575 nm
print(f"PSF: {np.degrees(rad_per_px) * 3600:.1f} arcsec a sample, {np.degrees(rad_per_px * N / 2):.1f} deg "
      f"half-range ({time.time() - t0:.1f} s)")

# ---- 32 scaled copies, CIE-weighted (Wyman, Sloan & Shirley 2013 fit of the 1931 observer) ----
def g(lam, mu, s1, s2):
    return np.exp(-0.5 * ((lam - mu) / np.where(lam < mu, s1, s2)) ** 2)


def cmf(lam):
    x = 1.056 * g(lam, 599.8, 37.9, 31.0) + 0.362 * g(lam, 442.0, 16.0, 26.7) - 0.065 * g(lam, 501.1, 20.4, 26.2)
    y = 0.821 * g(lam, 568.8, 46.9, 40.5) + 0.286 * g(lam, 530.9, 16.3, 31.1)
    z = 1.217 * g(lam, 437.0, 11.8, 36.0) + 0.681 * g(lam, 459.0, 26.0, 13.8)
    return np.array([x, y, z])


XYZ_TO_RGB = np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]])
lams = 380.0 + np.arange(32) * (770.0 - 380.0) / 32
rgb_w = XYZ_TO_RGB @ cmf(lams)                  # linear sRGB response of each wavelength
rgb_w /= rgb_w.sum(axis=1, keepdims=True)       # a flat spectrum sums to white
theta = np.linspace(-OUT_HALF_DEG, OUT_HALF_DEG, OUT) * np.pi / 180
TY, TX = np.meshgrid(theta, theta, indexing="ij")
rgb = np.zeros((3, OUT, OUT))
for i, lam in enumerate(lams):
    s = (LAM0 * 1e9) / lam                      # read the 575 nm PSF at x * 575 / lambda
    coords = np.array([TY * s / rad_per_px + N / 2, TX * s / rad_per_px + N / 2])
    mono = map_coordinates(F, coords, order=1, mode="constant", cval=0.0)
    mono *= s * s                               # scaling preserves energy: F(x s) s^2
    rgb += rgb_w[:, i, None, None] * mono
print(f"spectral PSF: {OUT} px over +-{OUT_HALF_DEG} deg ({time.time() - t0:.1f} s)")

np.savez_compressed("glare_ref.npz", rgb=rgb.astype(np.float32), half_deg=OUT_HALF_DEG, pupil_mm=PUPIL_MM)

# ---- a look at it: log exposure, 7 decades below the peak ------------------------------------
lum = rgb.mean(axis=0)
peak = lum.max()
disp = np.clip((np.log10(np.maximum(rgb, 1e-30) / peak) + 7.0) / 7.0, 0.0, 1.0)
img = (disp.transpose(1, 2, 0) * 255).astype(np.uint8)
Image.fromarray(img, "RGB").save("glare_ref.png")
print(f"wrote glare_ref.npz, glare_ref.png ({time.time() - t0:.1f} s)")
