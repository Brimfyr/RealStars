# -*- coding: utf-8 -*-
"""Measured colours of the Sun's planets, moons and minor bodies, for their distant sprites and planetshine.

A body's colour here is its colour in white light: its albedo per channel with the Sun as white, as its maps
are. That is the reflected sunlight's linear sRGB over the Sun's own, both through the same CIE 1931 observer
and measured solar spectrum as make_star_binary.py (star_colours.py), so the Sun's raw colour (SunLight.SunRaw)
times this is the reflected light exactly. Real Stars lights it with the star's light in game (BodyColours.cs). Written at unit luminance:
how bright a body is, in the sky and as planetshine, comes from its albedo (make_body_albedos.py).

Each body's reflectance spectrum comes from the measurements listed with it: linear between measured
points, the first segment's slope carried on below the bluest (spectra keep falling toward the ultraviolet),
flat above the reddest (so where only B-V is known, nothing is guessed about the red, unless the body is seen
to keep reddening, as Amalthea is). Colour indices are
turned into reflectance against the Sun's own colours (Holmberg et al. 2006; U-B from Ramirez et al. 2012) at
the bands' effective wavelengths (Bessell 1990).

Writes BodyColours.Generated.cs. Karkoschka's spectra are downloaded to assets/ on first run.
"""
import os
import urllib.request
import numpy as np

from make_star_binary import cie_xyz
import star_colours

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "BodyColours.Generated.cs")
KARKOSCHKA = "https://atmos.nmsu.edu/PDS/data/gbat_0001/data/1995low.tab"

GRID = np.arange(380.0, 781.0, 5.0)
BAND = {"U": 360.0, "B": 436.0, "V": 545.0, "R": 641.0, "I": 798.0}   # Johnson UBV, Kron-Cousins RI
SUN = {"U-B": 0.17, "B-V": 0.64, "V-R": 0.35}
XYZ_TO_RGB = np.array([[3.2406, -1.5372, -0.4986],
                       [-0.9689, 1.8758, 0.0415],
                       [0.0557, -0.2040, 1.0570]])
LUMA = np.array([0.2126729, 0.7151522, 0.0721750])


# ------------------------------------------------------------------------------------------ spectra
def spectrum(points):
    """Reflectance on GRID through (nm, reflectance) points, any scale."""
    x, y = (np.array(v, float) for v in zip(*sorted(points)))
    r = np.interp(GRID, x, y)
    if len(x) > 1:
        below = GRID < x[0]
        r[below] = np.maximum(y[0] + (y[1] - y[0]) / (x[1] - x[0]) * (GRID[below] - x[0]), 0.0)
    return r


def albedos(johnson_cousins, sloan):
    """Geometric albedos in U, B, V, Rc, Ic and in u', g', r', i', z' (Fukugita et al. 1996), each normalised at
    550 nm and averaged: the two sets disagree by a few percent here and there (Venus's B)."""
    a = spectrum(zip((BAND[k] for k in "UBVRI"), johnson_cousins))
    b = spectrum(zip((356.0, 483.0, 626.0, 767.0, 910.0), sloan))
    return 0.5 * (a / np.interp(550.0, GRID, a) + b / np.interp(550.0, GRID, b))


def colours(bv=None, vr=None, ub=None):
    """Johnson B-V and U-B, Kron-Cousins V-R, against the Sun's."""
    points = [(BAND["V"], 1.0)]
    if bv is not None:
        rb = 10 ** (-0.4 * (bv - SUN["B-V"]))
        points.append((BAND["B"], rb))
        if ub is not None:
            points.append((BAND["U"], rb * 10 ** (-0.4 * (ub - SUN["U-B"]))))
    if vr is not None:
        points.append((BAND["R"], 10 ** (0.4 * (vr - SUN["V-R"]))))
    return spectrum(points)


def straight(bv):
    """B-V as one straight line through the visible, for a body seen to keep brightening into the red."""
    rb = 10 ** (-0.4 * (bv - SUN["B-V"]))
    slope = (1.0 - rb) / (BAND["V"] - BAND["B"])
    return spectrum([(BAND["B"], rb), (BAND["V"], 1.0), (780.0, 1.0 + slope * (780.0 - BAND["V"]))])


def ecas(s, u, b, w, x):
    """Eight-Color Asteroid Survey colours s-v, u-v, b-v, v-w, v-x, already relative to the Sun."""
    return spectrum([(337, 10 ** (-0.4 * s)), (359, 10 ** (-0.4 * u)), (437, 10 ** (-0.4 * b)), (550, 1.0),
                     (701, 10 ** (0.4 * w)), (853, 10 ** (0.4 * x))])


def gradient(percent_per_100nm):
    """A reflectance rising linearly through the visible, normalised at 550 nm."""
    return spectrum([(lam, 1.0 + percent_per_100nm / 100.0 * (lam - 550.0) / 100.0) for lam in (400.0, 550.0, 700.0)])


def karkoschka(column):
    """Full-disc albedo from Karkoschka (1998), 1995 data, PDS gbat_0001: column 3 Jupiter, 4 Saturn (rings edge-on),
    5 Uranus, 6 Neptune, 7 Titan."""
    path = os.path.join(HERE, "assets", "karkoschka_1995low.tab")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        print(f"  downloading {KARKOSCHKA}")
        with urllib.request.urlopen(KARKOSCHKA, timeout=120) as r, open(path, "wb") as f:
            f.write(r.read())
    d = np.loadtxt(path)
    return np.interp(GRID, d[:, 0], d[:, column])


# Johnson & McCord (1971, ApJ 169, 589), Table 3A: geometric albedo of Io, Europa and Ganymede, 0.358-0.809 um.
# Ganymede's 0.383 um value (0.594, between 0.362 and 0.474) is left out as a misprint.
JM1971 = {
    "Io": [(358, .156), (383, .330), (402, .369), (433, .552), (467, .688), (498, .811), (532, .838), (564, .851),
           (598, .899), (633, .931), (665, .958), (699, .959), (730, .973), (765, .968), (809, .950)],
    "Europa": [(358, .555), (402, .670), (433, .746), (467, .804), (498, .864), (532, .909), (564, .899), (598, .905),
               (633, .908), (665, .899), (699, .899), (730, .902), (765, .923), (809, .918)],
    "Ganymede": [(358, .362), (402, .474), (433, .539), (467, .568), (498, .587), (532, .613), (564, .615),
                 (598, .625), (633, .632), (665, .630), (699, .627), (730, .632), (765, .637), (809, .647)],
}

# Mayorga, Charbonneau & Thorngren (2020, AJ 160, 238), Table 5: Cassini ISS geometric albedos, fitted over
# longitude, in VIO, GRN, RED and CB2 (420, 569, 647, 752 nm).
CASSINI_NM = (420.0, 569.0, 647.0, 752.0)
CASSINI = {"Io": (.341370, .621452, .670283, .756797), "Europa": (.553986, .660793, .671382, .741602),
           "Ganymede": (.369516, .428328, .433450, .451342), "Callisto": (.149008, .182535, .182846)}


def galilean(name, shape):
    """Johnson & McCord's spectrum, tilted to Cassini's disc-averaged band ratios (normalised at GRN)."""
    base = spectrum(JM1971[shape])
    bands = CASSINI_NM[:len(CASSINI[name])]
    k = np.array(CASSINI[name]) / np.interp(bands, GRID, base)
    return base * np.interp(GRID, bands, k / k[1])


# The Moon: Lane & Irvine (1973, AJ 78, 267) Table VII narrow bands to 0.50 um; beyond, McCord & Johnson (1970)
# as plotted in their Fig. 5, which their 1964 data follow (they doubt their 1965 excess at 0.6-0.85 um).
MOON = [(359, 10 ** (-0.4 * 0.42)), (393, 10 ** (-0.4 * 0.24)), (416, 10 ** (-0.4 * 0.20)), (457, 10 ** (-0.4 * 0.09)),
        (501, 1.0), (550, 10 ** (0.4 * 0.07)), (600, 10 ** (0.4 * 0.17)), (650, 10 ** (0.4 * 0.27)),
        (700, 10 ** (0.4 * 0.36)), (750, 10 ** (0.4 * 0.43)), (800, 10 ** (0.4 * 0.48))]

# The mean dust colours of 25 active long-period comets (Jewitt 2015, AJ 150, 201), for the ones with none of their own.
LONG_PERIOD_COMET = dict(bv=0.78, vr=0.47)

BODIES = [
    # Id, reflectance, source
    ("Mercury", lambda: albedos((.087, .105, .142, .158, .180), (.095, .130, .169, .200, .237)),
     "Mallama et al. 2017, Table 7"),
    ("Venus", lambda: albedos((.348, .658, .689, .658, .640), (.326, .664, .712, .708, .630)),
     "Mallama et al. 2017, Table 7"),
    ("Earth", lambda: albedos((.688, .512, .434, .392, .396), (.722, .497, .388, .393, .412)),
     "Mallama et al. 2017, Table 7"),
    ("Luna", lambda: spectrum(MOON), "Lane & Irvine 1973; McCord & Johnson 1970"),
    ("Mars", lambda: albedos((.060, .088, .170, .250, .285), (.061, .111, .245, .298, .325)),
     "Mallama et al. 2017, Table 7"),
    ("Jupiter", lambda: karkoschka(3), "Karkoschka 1998"),
    ("Io", lambda: galilean("Io", "Io"), "Johnson & McCord 1971; Mayorga et al. 2020"),
    ("Europa", lambda: galilean("Europa", "Europa"), "Johnson & McCord 1971; Mayorga et al. 2020"),
    ("Ganymede", lambda: galilean("Ganymede", "Ganymede"), "Johnson & McCord 1971; Mayorga et al. 2020"),
    ("Callisto", lambda: galilean("Callisto", "Ganymede"), "Mayorga et al. 2020, on Ganymede's spectrum"),
    ("Amalthea", lambda: straight(1.30), "Kulyk & Jockers 2004, B-V; red through the visible (Thomas et al. 1998)"),
    ("Himalia", lambda: colours(bv=0.640, vr=0.375), "Neese 2004 (PDS), mean"),
    ("Elara", lambda: colours(bv=0.660, vr=0.357), "Neese 2004 (PDS), mean"),
    ("Pasiphae", lambda: colours(bv=0.692, vr=0.404), "Neese 2004 (PDS), mean"),
    ("Sinope", lambda: colours(bv=0.767, vr=0.480), "Neese 2004 (PDS), mean"),
    ("Lysithea", lambda: colours(bv=0.674, vr=0.378), "Neese 2004 (PDS), mean"),
    ("Saturn", lambda: karkoschka(4), "Karkoschka 1998, rings edge-on"),
    ("Mimas", lambda: colours(bv=0.65), "Franz 1975 (B-V only)"),
    ("Enceladus", lambda: colours(bv=0.62), "Cruikshank 1980 (B-V only)"),
    ("Tethys", lambda: colours(bv=0.74, ub=0.33), "Cruikshank 1980"),
    ("Dione", lambda: colours(bv=0.76, ub=0.28), "Cruikshank 1980"),
    ("Rhea", lambda: colours(bv=0.76, ub=0.35), "Cruikshank 1980"),
    ("Titan", lambda: karkoschka(7), "Karkoschka 1998"),
    ("Iapetus", lambda: colours(bv=0.72), "Stephan 2009 (B-V only)"),
    ("Uranus", lambda: karkoschka(5), "Karkoschka 1998"),
    ("Titania", lambda: colours(bv=0.70, ub=0.28), "Cruikshank 1980"),
    ("Oberon", lambda: colours(bv=0.68, ub=0.20), "Cruikshank 1980"),
    ("Neptune", lambda: karkoschka(6), "Karkoschka 1998"),
    ("Triton", lambda: colours(bv=0.72), "Degewij et al. 1980 (B-V only)"),
    ("Pluto", lambda: colours(bv=0.954, vr=0.52), "Buie et al. 2010; Verbiscer et al. 2022"),
    ("Charon", lambda: colours(bv=0.7315, vr=0.40), "Buie et al. 2010; Verbiscer et al. 2022"),
    ("Eris", lambda: colours(bv=0.805, vr=0.389), "Hainaut et al. 2012 (MBOSS)"),
    ("Haumea", lambda: colours(bv=0.631, vr=0.370), "Hainaut et al. 2012 (MBOSS)"),
    ("MakemakeComet", lambda: colours(bv=0.828, vr=0.41), "Hainaut et al. 2012; Hromakina et al. 2019 (Makemake)"),
    ("Sedna", lambda: colours(bv=1.111, vr=0.660), "Hainaut et al. 2012 (MBOSS)"),
    ("Ceres", lambda: ecas(.430, .263, .047, .000, -.005), "Zellner et al. 1985 (ECAS)"),
    ("Pallas", lambda: ecas(.170, .075, .000, -.013, -.023), "Zellner et al. 1985 (ECAS)"),
    ("Vesta", lambda: ecas(.652, .428, .142, .085, -.168), "Zellner et al. 1985 (ECAS)"),
    ("Hygiea", lambda: ecas(.279, .174, .012, -.023, -.024), "Zellner et al. 1985 (ECAS)"),
    ("Eros", lambda: ecas(.756, .562, .259, .223, .193), "Zellner et al. 1985 (ECAS)"),
    ("HalleysComet", lambda: colours(vr=0.410), "Hainaut et al. 2012, nucleus (V-R only)"),
    ("HaleBoppComet", lambda: colours(**LONG_PERIOD_COMET), "Jewitt 2015, long-period comet mean"),
    ("LovejoyComet", lambda: colours(**LONG_PERIOD_COMET), "Jewitt 2015, long-period comet mean"),
    ("Oumuamua", lambda: colours(bv=0.70, vr=0.45), "Jewitt et al. 2017"),
    ("Borisov", lambda: colours(bv=0.80, vr=0.47), "Jewitt & Luu 2019, dust"),
    ("3I_ATLAS", lambda: gradient(18.0), "Opitom et al. 2025, dust, 18 %/100 nm"),
]

# ------------------------------------------------------------------------------------------ colour
def in_white_light(reflectance):
    """Linear sRGB of the reflected sunlight over the Sun's own, per channel, then at unit luminance."""
    x, y, z = cie_xyz(GRID)
    sun = np.interp(GRID, *star_colours.sun_spectrum())

    def rgb(s):
        return XYZ_TO_RGB @ np.array([s @ x, s @ y, s @ z])

    c = np.maximum(rgb(reflectance * sun) / rgb(sun), 0.0)
    return c / (c @ LUMA)


def display(c):
    """8-bit sRGB of a colour at its brightest channel, to look at."""
    v = np.clip(c / c.max(), 0.0, 1.0)
    v = np.where(v <= 0.0031308, 12.92 * v, 1.055 * v ** (1 / 2.4) - 0.055)
    return tuple(int(round(255 * t)) for t in v)


def main():
    rows = []
    for body, reflectance, source in BODIES:
        c = in_white_light(reflectance())
        rows.append((body, c, source))
        print(f"  {body:14s} {c[0]:.4f} {c[1]:.4f} {c[2]:.4f}   display {display(c)}   {source}")

    lines = [
        "// Generated by make_body_colours.py: do not edit.",
        "namespace RealStars;",
        "",
        "internal static partial class BodyColours",
        "{",
        "    /// <summary>Measured colours of the Sun's bodies by template Id: each one's albedo per channel with the Sun",
        "    /// as white, linear sRGB at unit luminance. make_body_colours.py has the measurements behind each.</summary>",
        "    internal static readonly Dictionary<string, float[]> Measured = new()",
        "    {",
    ]
    for body, c, source in rows:
        lines.append(f'        ["{body}"] = new[] {{ {c[0]:.4f}f, {c[1]:.4f}f, {c[2]:.4f}f }},   // {source}')
    lines += ["    };", "}", ""]
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"wrote {len(rows)} bodies to {OUT}")


if __name__ == "__main__":
    main()
