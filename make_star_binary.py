# -*- coding: utf-8 -*-
"""Build the star binary from Hipparcos (CDS I/239), in the format the game's own loader reads.

The shipped catalogue carries almost no brightness information. Measured across its 99,038
stars: the size byte runs 17-239 but its 90th percentile is 17, and what variation exists
below that floor is smuggled into the colour channel instead (every star at scale >= 18 has a
peak-normalised colour; the ones at the floor are scaled down to as little as 0.2). 92,687
stars - 93.6% of the sky - are floored in both, so they render identically. End to end its
range is 70:1, about 4.6 magnitudes, where the real sky from Sirius to magnitude 9 spans
13,000:1.

So this writes its own, with three changes:

  brightness  The byte encodes V magnitude LINEARLY rather than as a sprite size, which is
              what the game's generator does (size = k*10^(-0.164 m), which crowds the faint
              end into a few values). At 24 bytes per magnitude the resolution is 0.04 mag
              across the whole range, and the shader decodes it back to a magnitude.
  colour      Hue only, peak-normalised, so brightness lives in one place. Derived from B-V
              through an effective temperature and a blackbody spectrum integrated against
              the CIE 1931 observer, rather than the piecewise fit the game uses - which
              reads the V-I column as though it were B-V, and returns black outside
              -0.4..2.1, culling 158 naked-eye stars including Betelgeuse and Antares.
  frame       True J2000 ecliptic, Z-up, matching the shipped binary. The game's own
              `generatestarbinary` writes Y-up equatorial, so anything it produces is
              misoriented against the solar system.

Record layout, from ModLibrary.LoadStarBinaries: int32 count, then per star float3 direction
(normalised by the loader), byte scale, byte R, G, B.
"""
import math
import os
import struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "assets")
HIP = r"C:\Users\gunsh\Documents\Kitten Space Agency\Proxima Centauri\_build\hip_main.dat"
OUT_NAME = "RealStars.bin"

# ---------------------------------------------------------------- encoding (shader must agree)
# Stars are stored as 3D POSITIONS in parsecs, not directions, and the byte holds ABSOLUTE
# magnitude rather than apparent. The shader then works out both the direction and the
# brightness from where the observer actually is, which is what makes parallax fall out: move,
# and near stars shift against far ones exactly as they should.
#
# Within one system the shift is tiny - 31 arcsec at Pluto, about a quarter of a pixel - but it
# costs nothing to carry, and it is the difference between a sky painted from Earth and a sky
# that would still be right if you left.
MAG_ABS_FAINT = 16.5     # absolute magnitude stored as byte 1
BYTES_PER_MAG = 10.0     # 0.1 mag per step, spanning -8.9 to +16.5
MAG_LIMIT = 9.0          # catalogue cut, on APPARENT magnitude as seen from here

# A star with no usable parallax has no known distance. It goes out here, far enough that it
# cannot parallax noticeably, with an absolute magnitude chosen so that its apparent magnitude
# from the solar system comes out exactly right anyway.
UNKNOWN_DISTANCE_PC = 1000.0

OBLIQUITY = math.radians(23.4392911)   # J2000 mean obliquity
BV_DEFAULT = 0.65                      # solar-ish, for the few stars with no B-V

# Hipparcos columns, verified against stars with known values (Sirius, Capella, Betelgeuse)
C_VMAG, C_RA, C_DEC, C_PLX, C_BV = 5, 8, 9, 11, 37


def parse():
    """Vmag, RA/Dec in degrees, parallax in mas, B-V - for every star with a usable row."""
    vmag, ra, dec, plx, bv, no_bv = [], [], [], [], [], 0
    with open(HIP, encoding="latin-1") as f:
        for line in f:
            p = line.split("|")
            if len(p) < 42:
                continue
            try:
                v = float(p[C_VMAG])
                a = float(p[C_RA])
                d = float(p[C_DEC])
            except ValueError:
                continue                      # no magnitude or no astrometric solution
            if v > MAG_LIMIT:
                continue
            try:
                c = float(p[C_BV])
            except ValueError:
                c, no_bv = BV_DEFAULT, no_bv + 1
            try:
                pl = float(p[C_PLX])
            except ValueError:
                pl = 0.0
            vmag.append(v); ra.append(a); dec.append(d); plx.append(pl); bv.append(c)
    print(f"{len(vmag)} stars to V={MAG_LIMIT} ({no_bv} without a B-V, defaulted to {BV_DEFAULT})")
    return (np.array(vmag), np.array(ra), np.array(dec), np.array(plx), np.array(bv))


def directions(ra_deg, dec_deg):
    """Equatorial J2000 to ecliptic, Z-up: the frame the solar system is built in, so the
    sky sits square with the planets' orbits rather than tilted by the obliquity."""
    ra, dec = np.radians(ra_deg), np.radians(dec_deg)
    x = np.cos(dec) * np.cos(ra)
    y = np.cos(dec) * np.sin(ra)
    z = np.sin(dec)
    c, s = math.cos(OBLIQUITY), math.sin(OBLIQUITY)
    return np.stack([x, y * c + z * s, -y * s + z * c], axis=1)


def distances(plx_mas, vmag):
    """Distance in parsecs from parallax, and the absolute magnitude that follows.

    Hipparcos parallaxes are noisy enough that some come out zero or negative, which is a
    measurement artefact rather than a star behind the observer. Those have no usable distance,
    so they go to a nominal far shell with an absolute magnitude that reproduces the apparent
    one from here: correct where we stand, and honest about not knowing how far away it is.
    """
    known = plx_mas > 0.5                       # below this the distance error exceeds the value
    dist = np.where(known, 1000.0 / np.maximum(plx_mas, 1e-6), UNKNOWN_DISTANCE_PC)
    dist = np.clip(dist, 0.05, 100000.0)
    absmag = vmag - 5.0 * np.log10(dist / 10.0)
    return dist, absmag, known


# ------------------------------------------------------------------------------- colour
def cie_xyz(lam):
    """CIE 1931 2-degree colour matching functions, multi-lobe Gaussian fits from Wyman,
    Sloan & Shirley (2013), accurate to about 1% and needing no tabulated data."""
    def g(x, mu, s1, s2):
        s = np.where(x < mu, s1, s2)
        return np.exp(-0.5 * ((x - mu) / s) ** 2)
    x = 1.056 * g(lam, 599.8, 37.9, 31.0) + 0.362 * g(lam, 442.0, 16.0, 26.7) \
        - 0.065 * g(lam, 501.1, 20.4, 26.2)
    y = 0.821 * g(lam, 568.8, 46.9, 40.5) + 0.286 * g(lam, 530.9, 16.3, 31.1)
    z = 1.217 * g(lam, 437.0, 11.8, 36.0) + 0.681 * g(lam, 459.0, 26.0, 13.8)
    return x, y, z


def planck(lam_nm, T):
    """Spectral radiance, arbitrary scale: only the shape matters once normalised."""
    lam = lam_nm[None, :] * 1e-9
    h, c, k = 6.62607015e-34, 2.99792458e8, 1.380649e-23
    return (2 * h * c ** 2 / lam ** 5) / (np.exp(h * c / (lam * k * T[:, None])) - 1.0)


def teff_from_bv(bv):
    """Ballesteros (2012), a two-blackbody fit good across the main sequence."""
    return 4600.0 * (1.0 / (0.92 * bv + 1.70) + 1.0 / (0.92 * bv + 0.62))


def colours(bv):
    """B-V to linear sRGB, peak-normalised so the byte carries hue and nothing else."""
    T = np.clip(teff_from_bv(np.clip(bv, -0.4, 2.5)), 1500.0, 40000.0)
    lam = np.arange(380.0, 781.0, 5.0)
    xb, yb, zb = cie_xyz(lam)
    spec = planck(lam, T)
    X, Y, Z = spec @ xb, spec @ yb, spec @ zb
    xyz = np.stack([X, Y, Z], 1) / np.maximum(Y, 1e-30)[:, None]   # normalise on luminance
    # XYZ to linear sRGB (D65 primaries)
    M = np.array([[ 3.2406, -1.5372, -0.4986],
                  [-0.9689,  1.8758,  0.0415],
                  [ 0.0557, -0.2040,  1.0570]])
    rgb = np.clip(xyz @ M.T, 0.0, None)
    return rgb / np.maximum(rgb.max(1, keepdims=True), 1e-9), T


# -------------------------------------------------------------------------------- write
SUN_ABS_MAG = 4.83       # the Sun's V absolute magnitude
SUN_BV = 0.65            # its B-V, which puts it at 5772 K through the same colour pipeline


def main():
    vmag, ra, dec, plx, bv = parse()
    dirs = directions(ra, dec)
    dist, absmag, known = distances(plx, vmag)

    # The Sun goes in the catalogue like any other star, at the origin. Once it is too far to
    # be drawn as a sphere it is a point source like the rest, and putting it here means it is
    # drawn by the same shader, with the same profile, and brightens correctly as you approach
    # - rather than by a flare pass whose size is floored and whose colour fades with radius.
    dirs = np.vstack([dirs, [0.0, 0.0, 0.0]])
    dist = np.append(dist, 0.0)
    absmag = np.append(absmag, SUN_ABS_MAG)
    vmag = np.append(vmag, -26.74)
    bv = np.append(bv, SUN_BV)
    known = np.append(known, True)

    rgb, T = colours(bv)

    positions = dirs * dist[:, None]                       # parsecs, ecliptic Z-up; Sun at 0,0,0
    scale = np.clip(np.round((MAG_ABS_FAINT - absmag) * BYTES_PER_MAG) + 1, 1, 255).astype(np.uint8)
    clipped = int(((MAG_ABS_FAINT - absmag) * BYTES_PER_MAG + 1 > 255).sum()
                  + ((MAG_ABS_FAINT - absmag) * BYTES_PER_MAG + 1 < 1).sum())
    rgb8 = np.clip(np.round(rgb * 255.0), 0, 255).astype(np.uint8)

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, OUT_NAME)
    with open(path, "wb") as f:
        f.write(struct.pack("<i", len(vmag)))
        rec = np.empty(len(vmag), dtype=np.dtype([("p", "<f4", 3), ("s", "u1"),
                                                  ("r", "u1"), ("g", "u1"), ("b", "u1")]))
        rec["p"] = positions.astype(np.float32)
        rec["s"] = scale
        rec["r"], rec["g"], rec["b"] = rgb8[:, 0], rgb8[:, 1], rgb8[:, 2]
        f.write(rec.tobytes())

    print(f"{OUT_NAME}: {len(vmag)} stars including the Sun, {os.path.getsize(path)/1e6:.1f} MB")
    print(f"  apparent magnitudes {vmag.min():.2f} to {vmag.max():.2f} from here")
    print(f"  absolute magnitudes {absmag.min():.2f} to {absmag.max():.2f} "
          f"-> bytes {scale.max()} to {scale.min()} ({clipped} clipped)")
    print(f"  distances {dist.min():.2f} to {dist.max():.0f} pc; "
          f"{int(known.sum())} measured, {int((~known).sum())} parked at {UNKNOWN_DISTANCE_PC:.0f} pc")
    print(f"  nearest: {', '.join(f'{d:.2f} pc' for d in np.sort(dist[known])[:5])}")
    print(f"  temperatures {T.min():.0f} K to {T.max():.0f} K")
    for name, v in (("brighter than 1", 1.0), ("naked eye (6)", 6.0), (f"all (<= {MAG_LIMIT})", MAG_LIMIT)):
        print(f"  {name:>16}: {int((vmag <= v).sum()):6d} stars")

    # What the effect actually looks like: the shift a star of this distance shows between
    # opposite sides of the observer's travels.
    print("\n  parallax at the edge of the solar system (Pluto, 39 AU from the Sun):")
    for label, d in (("Proxima-like (1.3 pc)", 1.3), ("Sirius (2.6 pc)", 2.64),
                     ("Vega (7.7 pc)", 7.68), ("Polaris (133 pc)", 133.0)):
        arcsec = 39.0 / d
        print(f"    {label:<22} {arcsec:7.2f}\"  ({arcsec / 3600 * 1080 / 60:.2f} px at 60 deg, 1080p)")


if __name__ == "__main__":
    main()
