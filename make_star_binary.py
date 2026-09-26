# -*- coding: utf-8 -*-
"""Build the star binary from AT-HYG and Hipparcos, in the format the game's own loader reads, with
each star's space velocity after it so the mod can carry the sky to the game's date.

Sources:
  AT-HYG v3.2, magnitude-10 subset (astronexus, CC BY-SA 4.0): which stars, and their V, B-V,
      distance, proper motion and radial velocity. Tycho-2 merged with HYG, with Gaia DR3 distances
      and motions for most stars.
  Hipparcos main catalogue (ESA 1997, CDS I/239): positions, for every star it has.

Why positions come from Hipparcos: AT-HYG's are not all at one epoch. Checked against Hipparcos,
80,453 of its Hipparcos stars are at J2000, but 2,652 are still at J1991.25 - Alpha Centauri,
Arcturus, Sirius and Procyon among them, Alpha Cen A 32" out - and 232 match neither. Hipparcos
gives all of them at J1991.25 to a milliarcsecond. The 40,231 stars Hipparcos lacks keep AT-HYG's
Tycho-2 positions, which are J2000.

The shipped catalogue carries almost no brightness information. Measured across its 99,038
stars: the size byte runs 17-239 but its 90th percentile is 17, and what variation exists
below that floor is smuggled into the colour channel instead (every star at scale >= 18 has a
peak-normalised colour; the ones at the floor are scaled down to as little as 0.2). 92,687
stars - 93.6% of the sky - are floored in both, so they render identically. End to end its
range is 70:1, about 4.6 magnitudes, where the real sky from Sirius to magnitude 9 spans
13,000:1.

So this writes its own, with four changes:

  brightness  The byte encodes V magnitude LINEARLY rather than as a sprite size, which is
              what the game's generator does (size = k*10^(-0.164 m), which crowds the faint
              end into a few values). At 10 bytes per magnitude the resolution is 0.1 mag
              across the whole range, and the shader decodes it back to a magnitude.
  colour      Hue only, so brightness lives in one place: stored with its brightest channel
              at 255 for the bytes' precision, and brought to unit luminance in Star.vert,
              since a colour at its peak is dimmer than white by its own hue. Derived from B-V
              through an effective temperature and a blackbody spectrum integrated against
              the CIE 1931 observer, rather than the piecewise fit the game uses - which
              reads the V-I column as though it were B-V, and returns black outside
              -0.4..2.1, culling 158 naked-eye stars including Betelgeuse and Antares.
  frame       True J2000 ecliptic, Z-up, matching the shipped binary. The game's own
              `generatestarbinary` writes Y-up equatorial, so anything it produces is
              misoriented against the solar system.
  date        Positions at the game's time zero, and each star's velocity after them, so the
              mod can move the sky on as the game's clock runs. Kapteyn's star, the fastest
              here, moves 8.6" a year: 5' between Hipparcos's epoch and the game's.

Layout: int32 count, then per star float3 position (pc), byte absolute magnitude, byte R, G, B -
the record ModLibrary.LoadStarBinaries reads, so the game's own reader still loads the file,
flat. Then the motion block, which that reader never reaches: the tag "RSM1", float64 epoch of the
positions (Julian year), then per star float3 velocity in parsecs per Julian year, same frame.
"""
import csv
import gzip
import math
import os
import struct
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "assets")
# https://www.astronexus.com/downloads/catalogs/athyg_32_reduced_m10.csv.gz
ATHYG = os.path.join(OUT, "athyg_32_reduced_m10.csv.gz")
# https://cdsarc.cds.unistra.fr/ftp/I/239/hip_main.dat
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
MAG_ABS_BRIGHT = MAG_ABS_FAINT - 254.0 / BYTES_PER_MAG
MAG_LIMIT = 9.0          # catalogue cut, on APPARENT magnitude as seen from here

# A star with no distance goes out here, far enough that it cannot parallax noticeably, with an
# absolute magnitude chosen so that its apparent magnitude from the solar system comes out exactly
# right anyway.
UNKNOWN_DISTANCE_PC = 1000.0

OBLIQUITY = math.radians(23.4392911)   # J2000 mean obliquity
BV_DEFAULT = 0.65                      # solar-ish, for the stars with no B-V

# Game time zero: every orbit in Core's Astronomicals.xml is a JPL Horizons state at this Julian day.
GAME_EPOCH = 2000.0 + (2461009.5 - 2451545.0) / 365.25    # 2025.9124, 2025-11-30 00:00
HIP_EPOCH = 1991.25
TYCHO_EPOCH = 2000.0

MAS = math.pi / (180.0 * 3600.0 * 1000.0)   # milliarcseconds to radians
KMS_TO_PC_PER_YR = 365.25 * 86400.0 / 3.0856775814913673e13
MOTION_TAG = b"RSM1"


def num(text, default=math.nan):
    try:
        return float(text)
    except ValueError:
        return default


def hipparcos_positions():
    """HIP number -> (RA, Dec) in degrees, ICRS at J1991.25, for every star with a solution."""
    pos = {}
    with open(HIP, encoding="latin-1") as f:
        for line in f:
            p = line.split("|")
            if len(p) < 42:
                continue
            try:
                pos[int(p[1])] = (float(p[8]), float(p[9]))
            except ValueError:
                continue                      # no astrometric solution
    return pos


def parse():
    """Every AT-HYG star to MAG_LIMIT but the Sun, with its position swapped for Hipparcos's where
    Hipparcos has one."""
    hip = hipparcos_positions()
    cols = {k: [] for k in ("v", "ra", "dec", "epoch", "dist", "bv", "pmra", "pmdec", "rv")}
    from_hip = no_bv = no_dist = no_pm = no_rv = 0
    with gzip.open(ATHYG, "rt", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            v = num(r["mag"])
            if not v <= MAG_LIMIT or r["id"] == "1":       # row 1 is the Sun, which goes in below
                continue
            h = int(r["hip"]) if r["hip"] else None
            if h in hip:
                ra, dec = hip[h]
                epoch = HIP_EPOCH
                from_hip += 1
            else:
                ra, dec = float(r["ra"]) * 15.0, float(r["dec"])   # AT-HYG gives RA in hours
                epoch = TYCHO_EPOCH
            bv = num(r["ci"])
            if math.isnan(bv):
                bv, no_bv = BV_DEFAULT, no_bv + 1
            dist = num(r["dist"], 0.0)
            if not dist > 0.0:
                dist, no_dist = math.nan, no_dist + 1
            pmra, pmdec = num(r["pm_ra"]), num(r["pm_dec"])
            if math.isnan(pmra) or math.isnan(pmdec):
                pmra, pmdec, no_pm = 0.0, 0.0, no_pm + 1
            rv = num(r["rv"])
            if math.isnan(rv):
                rv, no_rv = 0.0, no_rv + 1
            for k, x in zip(cols, (v, ra, dec, epoch, dist, bv, pmra, pmdec, rv)):
                cols[k].append(x)
    n = len(cols["v"])
    print(f"{n} stars to V={MAG_LIMIT}: positions {from_hip} Hipparcos (J{HIP_EPOCH}), "
          f"{n - from_hip} Tycho-2 (J{TYCHO_EPOCH:.0f})")
    print(f"  defaulted: {no_bv} B-V to {BV_DEFAULT}, {no_pm} proper motions and {no_rv} radial "
          f"velocities to zero; {no_dist} with no distance")
    return {k: np.array(x) for k, x in cols.items()}


def to_ecliptic(v):
    """Equatorial J2000 to ecliptic, Z-up: the frame the solar system is built in, so the
    sky sits square with the planets' orbits rather than tilted by the obliquity."""
    c, s = math.cos(OBLIQUITY), math.sin(OBLIQUITY)
    return np.stack([v[:, 0], v[:, 1] * c + v[:, 2] * s, -v[:, 1] * s + v[:, 2] * c], axis=1)


def distances(dist, vmag):
    """Distance in parsecs, and the absolute magnitude that follows.

    A star with no distance goes to a nominal far shell with an absolute magnitude that
    reproduces the apparent one from here: correct where we stand, and honest about not knowing
    how far away it is. Three supergiants with poor Gaia parallaxes come out brighter than the
    byte reaches; they are brought in until they fit, which keeps their magnitude from here
    exact at the cost of a distance that was already doubtful.
    """
    known = ~np.isnan(dist)
    dist = np.where(known, dist, UNKNOWN_DISTANCE_PC)
    absmag = vmag - 5.0 * np.log10(dist / 10.0)
    fits = np.clip(absmag, MAG_ABS_BRIGHT, MAG_ABS_FAINT)
    moved = int((fits != absmag).sum())
    dist = 10.0 * 10.0 ** ((vmag - fits) / 5.0)
    return dist, fits, known, moved


def motion(ra_deg, dec_deg, dist, pmra, pmdec, rv):
    """Unit direction and space velocity (parsecs per Julian year), equatorial.

    Tangential from the proper motion at the distance the star is placed at, so that a star
    with no known distance still crosses the sky at its measured rate as seen from here.
    """
    ra, dec = np.radians(ra_deg), np.radians(dec_deg)
    r = np.stack([np.cos(dec) * np.cos(ra), np.cos(dec) * np.sin(ra), np.sin(dec)], axis=1)
    east = np.stack([-np.sin(ra), np.cos(ra), np.zeros_like(ra)], axis=1)
    north = np.stack([-np.sin(dec) * np.cos(ra), -np.sin(dec) * np.sin(ra), np.cos(dec)], axis=1)
    tangential = (pmra[:, None] * east + pmdec[:, None] * north) * MAS * dist[:, None]
    return r, tangential + (rv * KMS_TO_PC_PER_YR)[:, None] * r


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
    s = parse()
    vmag, bv = s["v"], s["bv"]
    dist, absmag, known, moved = distances(s["dist"], vmag)
    dirs, vel = motion(s["ra"], s["dec"], dist, s["pmra"], s["pmdec"], s["rv"])

    # Straight-line motion from each position's own epoch to the game's: exact for the linear
    # model, which holds for millennia, and it carries the distance too, so a star closing on
    # us brightens as the shader works out its magnitude.
    positions = to_ecliptic(dirs * dist[:, None] + vel * (GAME_EPOCH - s["epoch"])[:, None])
    vel = to_ecliptic(vel)

    # The Sun goes in the catalogue like any other star, at the origin. Once it is too far to
    # be drawn as a sphere it is a point source like the rest, and putting it here means it is
    # drawn by the same shader, with the same profile, and brightens correctly as you approach
    # - rather than by a flare pass whose size is floored and whose colour fades with radius.
    # It is the frame's origin, so it never moves.
    positions = np.vstack([positions, [0.0, 0.0, 0.0]])
    vel = np.vstack([vel, [0.0, 0.0, 0.0]])
    dist = np.append(dist, 0.0)
    absmag = np.append(absmag, SUN_ABS_MAG)
    vmag = np.append(vmag, -26.74)
    bv = np.append(bv, SUN_BV)
    known = np.append(known, True)

    rgb, T = colours(bv)

    scale = np.clip(np.round((MAG_ABS_FAINT - absmag) * BYTES_PER_MAG) + 1, 1, 255).astype(np.uint8)
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
        f.write(MOTION_TAG)
        f.write(struct.pack("<d", GAME_EPOCH))
        f.write(vel.astype("<f4").tobytes())

    print(f"{OUT_NAME}: {len(vmag)} stars including the Sun, {os.path.getsize(path)/1e6:.1f} MB, "
          f"positions at {GAME_EPOCH:.4f}")
    print(f"  apparent magnitudes {vmag.min():.2f} to {vmag.max():.2f} from here")
    print(f"  absolute magnitudes {absmag.min():.2f} to {absmag.max():.2f} "
          f"-> bytes {scale.max()} to {scale.min()} ({moved} moved in to fit)")
    print(f"  distances {dist[:-1].min():.2f} to {dist.max():.0f} pc; "
          f"{int(known.sum()) - 1} measured, {int((~known).sum())} parked at {UNKNOWN_DISTANCE_PC:.0f} pc")
    print(f"  nearest: {', '.join(f'{d:.2f} pc' for d in np.sort(dist[:-1][known[:-1]])[:5])}")
    print(f"  temperatures {T.min():.0f} K to {T.max():.0f} K")
    for name, v in (("brighter than 1", 1.0), ("naked eye (6)", 6.0), (f"all (<= {MAG_LIMIT})", MAG_LIMIT)):
        print(f"  {name:>16}: {int((vmag <= v).sum()):6d} stars")

    # How fast the sky changes: the angular rate as seen from the Sun.
    r = np.linalg.norm(positions[:-1], axis=1)
    radial = np.sum(vel[:-1] * positions[:-1], axis=1) / r
    rate = np.sqrt(np.maximum(np.sum(vel[:-1] ** 2, axis=1) - radial ** 2, 0.0)) / r / MAS / 1000.0
    order = np.argsort(-rate)
    fastest = ", ".join(f'{rate[i]:.2f}"/yr (V {vmag[i]:.2f})' for i in order[:3])
    print(f"\n  fastest across the sky: {fastest}")
    print(f"  moving more than 1\"/yr: {int((rate > 1.0).sum())}, more than 0.1\"/yr: {int((rate > 0.1).sum())}")

    # What parallax actually looks like: the shift a star of this distance shows between
    # opposite sides of the observer's travels.
    print("\n  parallax at the edge of the solar system (Pluto, 39 AU from the Sun):")
    for label, d in (("Proxima-like (1.3 pc)", 1.3), ("Sirius (2.6 pc)", 2.64),
                     ("Vega (7.7 pc)", 7.68), ("Polaris (133 pc)", 133.0)):
        arcsec = 39.0 / d
        print(f"    {label:<22} {arcsec:7.2f}\"  ({arcsec / 3600 * 1080 / 60:.2f} px at 60 deg, 1080p)")


if __name__ == "__main__":
    main()
