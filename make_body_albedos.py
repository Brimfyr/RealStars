# -*- coding: utf-8 -*-
"""Measured albedos and phase curves of the Sun's bodies, for planetshine and the eye's adaptation.

How bright a body is in visible light takes two measurements: its geometric albedo p, its brightness seen full
against a white disc of its size, and its phase function Phi(alpha), its brightness at phase angle alpha over that
at full. Together they give its spherical albedo, the share of the light on it that it reflects in all:
A = p q, with the phase integral q = 2 int Phi(alpha) sin(alpha) dalpha.

Planetshine needs more than the body seen whole, because a vessel close by sees one part of it at many angles. So
each body gets a surface law, rho(mu0, mu, alpha) = (A / pi) k(alpha) / N(mu0), with
N(mu0) = (1 / pi) int k(alpha) mu dOmega over the sky above a patch. Every patch then reflects the share A of the
light falling on it at any Sun height, as a Lambert surface does, so up close planetshine is the plain albedo; and it
sends that light out by phase angle as k says. Seen whole from afar the law gives
A k(alpha) J(alpha), J(alpha) = (1 / pi) int mu0 mu / N(mu0) dS over the part both lit and seen, and k is solved for,
by a few fixed-point steps, so that this is the measured p Phi(alpha). Past the last measured angle k is held.
BodyColours.cs integrates the same law over the part of the body in view.

The measurements, per body (sources beside each):
  - the planets: the V-band phase curves of The Astronomical Almanac (Mallama & Hilton 2018), their V1(0) with the
    Sun at V = -26.75 (Mallama et al. 2017) over the game's own radius, so its sphere has the measured brightness;
    except the Earth, whose V1(0) there (Mallama et al. 2017) came from a model with forward and back scattering
    swapped (Robinson 2025): the Earth is Robinson's fit to its measured visual phase curve instead
  - the Moon: its phase law in Allen's Astrophysical Quantities (Cox 2000), V1(0) = +0.21
  - Titan: the Cassini green-filter model curve of Garcia Munoz et al. 2017 (Fig. 1, CL1_GRN), read off the figure
  - moons and dwarf planets measured as p and q only: the Moon's phase curve, scaled in magnitude to their q (it
    lands within 0.3 mag of Mercury's measured curve, given Mercury's q)
  - asteroids and small dark moons: the IAU H-G system (Bowell et al. 1989) with their measured slope G, or 0.15
  - bodies with a measured p and no q (the far dwarf planets): q from the icy satellites' relation
    q = 0.366 p + 0.497 (Brucker et al. 2009, Fig. 1: the 0.336 printed there is a typo, since refitting the moons
    plotted gives 0.366, and their second fit, with Europa and Phoebe, comes out exactly as printed)
Bodies not here keep what the game loads (BodyColours.cs).

Writes BodyAlbedos.Generated.cs.
"""
import math
import os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "BodyAlbedos.Generated.cs")

V_SUN = -26.75                  # Mallama et al. 2017, Table 6
AU_KM = 149597870.7
K_STEP = 2.0                    # degrees between the written k(alpha), read back in log
N_STEPS = 40                    # N(mu0) written at mu0 = 0, 1/40 .. 1

# ------------------------------------------------------------------------------------------ quadrature
ALPHA = np.radians(np.linspace(0.0, 180.0, 361))           # k lives on half degrees
DEG = np.degrees(ALPHA)
MU0 = np.linspace(0.0, 1.0, 81)
_x, _w = np.polynomial.legendre.leggauss(64)
MU_OUT, W_MU = 0.5 * (_x + 1.0), 0.5 * _w
PHI_OUT = (np.arange(128) + 0.5) * np.pi / 128
W_PHI = 2.0 * np.pi / 128                                   # 0..pi, doubled for the mirror half
_x, _w = np.polynomial.legendre.leggauss(150)
CT, W_CT = _x, _w
PH = (np.arange(300) + 0.5) * 2.0 * np.pi / 300
W_PH = 2.0 * np.pi / 300


def lambert(a):
    return (np.sin(a) + (np.pi - a) * np.cos(a)) / np.pi


def n_of(k):
    """N(mu0) on MU0: the light a patch sends into the sky above it, per unit k, with the Sun at mu0."""
    out = np.empty_like(MU0)
    mu, ph = MU_OUT[:, None], PHI_OUT[None, :]
    for i, m0 in enumerate(MU0):
        ca = np.clip(m0 * mu + math.sqrt(max(1.0 - m0 * m0, 0.0)) * np.sqrt(1.0 - mu * mu) * np.cos(ph), -1.0, 1.0)
        out[i] = (np.interp(np.arccos(ca), ALPHA, k) * mu * W_MU[:, None]).sum() * W_PHI / np.pi
    return out


def j_of(n_table, mu0_grid=MU0):
    """J(alpha) on ALPHA: the body seen whole from afar, the Sun along +z and the observer alpha away in x-z."""
    st = np.sqrt(1.0 - CT * CT)
    nx, nz = st[:, None] * np.cos(PH)[None, :], np.repeat(CT[:, None], len(PH), axis=1)
    w = W_CT[:, None] * W_PH
    lit = nz > 0.0
    base = np.where(lit, nz * w / np.interp(np.clip(nz, 0.0, 1.0), mu0_grid, n_table), 0.0)
    out = np.empty(len(ALPHA))
    for i, a in enumerate(ALPHA):
        mu = nx * math.sin(a) + nz * math.cos(a)
        out[i] = (base * np.maximum(mu, 0.0)).sum() / np.pi
    return out


def q_of(phi):
    return 2.0 * np.trapz(phi * np.sin(ALPHA), ALPHA)


def solve(p, phi, alpha_max):
    """The surface law of a body seen to follow p phi(alpha) out to alpha_max (degrees): (A, k, N, far-field model)."""
    inside = DEG <= alpha_max + 1e-9
    last = np.nonzero(inside)[0][-1]
    k = np.where(inside, phi / np.maximum(lambert(ALPHA), 1e-6), 0.0)
    k[~inside] = k[last]
    for _ in range(60):
        n = n_of(k)
        j = j_of(n)
        target = np.where(inside, p * phi, p * phi[last] * j / j[last])   # k held past the last measurement
        a = p * q_of(target / p)
        k_new = target / np.maximum(a * j, 1e-30)
        k_new[~inside] = k_new[last]
        k_new /= k_new[0]
        done = np.max(np.abs(k_new - k / k[0]) / k_new) < 1e-6
        k = k_new
        if done:
            break
    n = n_of(k)
    j = j_of(n)
    a = p * q_of(np.where(inside, phi, phi[last] * j / j[last]))
    scale = 1.0 / n[-1]                        # any scale of k gives the same law; N(1) = 1 reads best
    return a, k * scale, n * scale, a * k * j


# ------------------------------------------------------------------------------------------ phase curves
def dimming(mags):
    """A phase function from magnitudes of dimming."""
    return 10.0 ** (-0.4 * mags)


def v1_albedo(v10, radius_km):
    """The geometric albedo of a sphere of this radius with absolute magnitude V1(0)."""
    return (AU_KM / radius_km) ** 2 * 10.0 ** (-0.4 * (v10 - V_SUN))


def mercury(a):      # Mallama & Hilton 2018, Eq. 2
    return dimming(6.3280e-02 * a - 1.6336e-03 * a**2 + 3.3644e-05 * a**3 - 3.4265e-07 * a**4
                   + 1.6893e-09 * a**5 - 3.0334e-12 * a**6)


def venus(a):        # Eq. 3 to 163.7 degrees, Eq. 4 past it
    return dimming(np.where(a <= 163.7,
                            -1.044e-03 * a + 3.687e-04 * a**2 - 2.814e-06 * a**3 + 8.938e-09 * a**4,
                            236.05828 - 2.81914 * a + 8.39034e-03 * a**2 + 4.384))


def mars(a):         # Eq. 6 to 50 degrees, Eq. 7 past it
    return dimming(np.where(a <= 50.0, 0.02267 * a - 0.0001302 * a**2,
                            -0.367 - 0.02573 * a + 0.0003445 * a**2 + 1.601))


def jupiter(a):      # Eq. 8 to 12 degrees, Eq. 9 (Mayorga et al. 2016, Cassini green) past it
    x = np.minimum(a, 179.0) / 180.0
    far = 1.0 - 1.507 * x - 0.363 * x**2 - 0.062 * x**3 + 2.809 * x**4 - 1.876 * x**5
    return dimming(np.where(a <= 12.0, -3.7e-04 * a + 6.16e-04 * a**2,
                            -9.428 - 2.5 * np.log10(np.maximum(far, 1e-9)) + 9.395))


def saturn(a):       # the globe: Eq. 11 to 6 degrees, Eq. 12 past it
    return dimming(np.where(a <= 6.0, -3.7e-04 * a + 6.16e-04 * a**2,
                            -8.94 + 2.446e-4 * a + 2.672e-4 * a**2 - 1.505e-6 * a**3 + 4.767e-9 * a**4 + 8.95))


def uranus(a):       # Eq. 15, the latitude term left out
    return dimming(6.587e-3 * a + 1.045e-4 * a**2)


def neptune(a):      # Eq. 17
    return dimming(7.944e-3 * a + 9.617e-5 * a**2)


def earth(a):        # Robinson 2025, Eq. 14: a Henyey-Greenstein lobe, g = -0.33, fitted to the visual phase curve
    g = -0.33
    return ((1.0 + g) ** 2 / (1.0 + g * g + 2.0 * g * np.cos(np.radians(a)))) ** 1.5


def moon(a):         # Allen's Astrophysical Quantities (Cox 2000)
    return dimming(0.026 * a + 4.0e-9 * a**4)


# Garcia Munoz et al. 2017, Fig. 1, CL1_GRN (569 nm): the model curve read off the figure, as p Phi, every 5 degrees
# to 170 and at 172 (Titan brightens a hundredfold toward 180, lit from behind through its haze).
TITAN_GREEN = [0.2270, 0.2251, 0.2214, 0.2170, 0.2095, 0.2013, 0.1913, 0.1824, 0.1723, 0.1621, 0.1515, 0.1413,
               0.1324, 0.1233, 0.1139, 0.1060, 0.0991, 0.0924, 0.0858, 0.0803, 0.0756, 0.0720, 0.0688, 0.0660,
               0.0646, 0.0638, 0.0630, 0.0653, 0.0674, 0.0729, 0.0795, 0.0952, 0.1125, 0.1507, 0.2280, 0.3208]


def titan(a):
    x = list(np.arange(0.0, 171.0, 5.0)) + [172.0]
    return np.interp(a, x, TITAN_GREEN) / TITAN_GREEN[0]


def hg(slope):
    """The IAU H-G phase function (Bowell et al. 1989)."""
    def phi(a):
        t = np.tan(np.radians(np.minimum(a, 179.0)) / 2.0)
        return (1.0 - slope) * np.exp(-3.33 * t ** 0.63) + slope * np.exp(-1.87 * t ** 1.22)
    return phi


def lunar(scale):
    """The Moon's phase curve, its magnitudes of dimming times scale."""
    return lambda a: dimming(scale * (0.026 * a + 4.0e-9 * a**4))


def icy_q(p):
    """Brucker et al. 2009, Fig. 1, refitted (see above)."""
    return 0.366 * p + 0.497


# ------------------------------------------------------------------------------------------ the bodies
# (Id, geometric albedo, phase function, last measured phase angle, source); ("q", value) in place of a phase
# function asks for the Moon's curve scaled to that phase integral.
BODIES = [
    ("Mercury", v1_albedo(-0.613, 2439.7), mercury, 170.0, "Mallama & Hilton 2018, Eq. 2"),
    ("Venus", v1_albedo(-4.384, 6051.8), venus, 179.0, "Mallama & Hilton 2018, Eqs. 3-4"),
    ("Earth", 0.23, earth, 140.0, "Robinson 2025, Eq. 14"),
    ("Luna", v1_albedo(0.21, 1737.1), moon, 150.0, "Cox 2000 (Allen's Astrophysical Quantities)"),
    ("Mars", v1_albedo(-1.601, 3389.5), mars, 150.0, "Mallama & Hilton 2018, Eqs. 6-7"),
    ("Jupiter", v1_albedo(-9.395, 69911.0), jupiter, 130.0, "Mallama & Hilton 2018, Eqs. 8-9"),
    ("Saturn", v1_albedo(-8.95, 58232.0), saturn, 150.0, "Mallama & Hilton 2018, Eqs. 11-12 (globe)"),
    ("Uranus", v1_albedo(-7.110, 25362.0), uranus, 154.0, "Mallama & Hilton 2018, Eq. 15"),
    ("Neptune", v1_albedo(-7.00, 24622.0), neptune, 133.0, "Mallama & Hilton 2018, Eq. 17"),
    ("Titan", TITAN_GREEN[0], titan, 172.0, "Garcia Munoz et al. 2017, 569 nm"),
    ("Io", 0.63, ("q", 0.80), 150.0, "Simonelli & Veverka 1984"),
    ("Europa", 0.67, ("q", 1.01), 150.0, "Buratti & Veverka 1983, via Brucker et al. 2009"),
    ("Ganymede", 0.44, ("q", 0.80), 150.0, "Squyres & Veverka 1981, via Brucker et al. 2009"),
    ("Callisto", 0.18, ("q", 0.60), 150.0, "Squyres & Veverka 1981, via Brucker et al. 2009"),
    ("Mimas", 0.75, ("q", 0.80), 150.0, "Buratti & Veverka 1984, via Brucker et al. 2009"),
    ("Enceladus", 1.00, ("q", 0.85), 150.0, "Buratti & Veverka 1984, via Brucker et al. 2009"),
    ("Tethys", 0.80, ("q", 0.75), 150.0, "Buratti & Veverka 1984, via Brucker et al. 2009"),
    ("Dione", 0.55, ("q", 0.80), 150.0, "Buratti & Veverka 1984, via Brucker et al. 2009"),
    ("Rhea", 0.65, ("q", 0.70), 150.0, "Buratti & Veverka 1984, via Brucker et al. 2009"),
    ("Miranda", 0.32, ("q", 0.56), 150.0, "Veverka et al. 1991; Buratti et al. 1990"),
    ("Ariel", 0.39, ("q", 0.63), 150.0, "Veverka et al. 1991; Buratti et al. 1990"),
    ("Umbriel", 0.21, ("q", 0.52), 150.0, "Veverka et al. 1991; Buratti et al. 1990"),
    ("Titania", 0.27, ("q", 0.59), 150.0, "Veverka et al. 1991; Buratti et al. 1990"),
    ("Oberon", 0.23, ("q", 0.58), 150.0, "Veverka et al. 1991; Buratti et al. 1990"),
    ("Triton", 0.719, ("q", 1.16), 150.0, "Hicks & Buratti 2004; Hillier et al. 1990"),
    ("Pluto", 0.62, ("q", 1.16), 150.0, "Buratti et al. 2015, 2017"),
    ("Charon", 0.41, ("q", 0.60), 150.0, "Buratti et al. 2017"),
    ("Pan", 0.5, ("q", icy_q(0.5)), 150.0, "Porco et al. 2006; Brucker et al. 2009"),
    ("Eris", 0.96, ("q", icy_q(0.96)), 150.0, "Sicardy et al. 2011; Brucker et al. 2009"),
    ("Haumea", 0.51, ("q", icy_q(0.51)), 150.0, "Ortiz et al. 2017; Brucker et al. 2009"),
    ("MakemakeComet", 0.77, ("q", icy_q(0.77)), 150.0, "Ortiz et al. 2012; Brucker et al. 2009"),
    ("Sedna", 0.32, ("q", icy_q(0.32)), 150.0, "Pal et al. 2012; Brucker et al. 2009"),
    ("Ceres", 0.090, hg(0.12), 120.0, "Li et al. 2006; G from the MPC"),
    ("Pallas", 0.155, hg(0.11), 120.0, "Vernazza et al. 2021; G from the MPC"),
    ("Vesta", 0.4228, hg(0.32), 120.0, "IRAS (Tedesco et al.); G from the MPC"),
    ("Hygiea", 0.0717, hg(0.15), 120.0, "IRAS (Tedesco et al.)"),
    ("Eros", 0.25, hg(0.46), 120.0, "Veverka et al. 2000; G from the MPC"),
    ("Phobos", 0.071, hg(0.15), 120.0, "Zellner & Capen 1974"),
    ("Deimos", 0.068, hg(0.15), 120.0, "Thomas et al. 1996"),
    ("Amalthea", 0.090, hg(0.15), 120.0, "Simonelli et al. 2000"),
    ("Himalia", 0.057, hg(0.10), 120.0, "Grav et al. 2015"),
    ("Elara", 0.046, hg(0.10), 120.0, "Grav et al. 2015"),
    ("Pasiphae", 0.044, hg(0.10), 120.0, "Grav et al. 2015"),
    ("Sinope", 0.042, hg(0.10), 120.0, "Grav et al. 2015"),
    ("Lysithea", 0.036, hg(0.10), 120.0, "Grav et al. 2015"),
    ("HalleysComet", 0.04, hg(0.15), 120.0, "Lamy et al. 2004, nucleus"),
]


def fitted(p, curve, alpha_max):
    """Solve a body's law; a ("q", q) curve is the Moon's scaled until the law's phase integral is q."""
    if not (isinstance(curve, tuple) and curve[0] == "q"):
        return solve(p, curve(DEG), alpha_max) + (None,)
    want = curve[1]
    # start from the scale whose curve, carried to 180 degrees, has the phase integral wanted
    lo, hi = 0.05, 4.0
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        lo, hi = (lo, mid) if q_of(lunar(mid)(DEG)) < want else (mid, hi)
    scale = 0.5 * (lo + hi)
    for _ in range(8):                      # then secant steps on the law itself, whose tail is its own
        a, k, n, model = solve(p, lunar(scale)(DEG), alpha_max)
        got = a / p
        if abs(got - want) < 2e-4:
            break
        a2, _, _, _ = solve(p, lunar(scale * 1.02)(DEG), alpha_max)
        slope = (a2 / p - got) / (scale * 0.02)
        scale -= (got - want) / slope
    return a, k, n, model, scale


def main():
    rows = []
    for body, p, curve, alpha_max, source in BODIES:
        a, k, n, model, scale = fitted(p, curve, alpha_max)
        phi = curve(DEG) if callable(curve) else lunar(scale)(DEG)
        inside = DEG <= alpha_max
        err = np.max(np.abs(model[inside] - p * phi[inside])) / p
        # the law as written: k every K_STEP degrees, N every 1/N_STEPS, read back as BodyColours.cs reads them
        # (k between its steps in log, N straight)
        k_grid, n_grid = np.arange(0.0, 180.0 + 1e-9, K_STEP), np.linspace(0.0, 1.0, N_STEPS + 1)
        ks, ns = np.interp(k_grid, DEG, k), np.interp(n_grid, MU0, n)
        fine = np.linspace(0.0, 1.0, 2001)
        written = a * np.exp(np.interp(DEG, k_grid, np.log(ks))) * j_of(np.interp(fine, n_grid, ns), fine)
        err_w = np.max(np.abs(written[inside] - p * phi[inside]) / (p * phi[inside]))
        how = f"Moon's curve x{scale:.3f}" if scale is not None else f"to {alpha_max:.0f} deg"
        print(f"  {body:14s} p {p:.3f}  q {a / p:.3f}  A {a:.3f}   fit {err:.0e} of p; as written, within "
              f"{100 * err_w:.1f}% everywhere measured   ({how})   {source}")
        rows.append((body, a, p, a / p, ks, ns, source))

    def floats(v):
        return ", ".join(f"{x:.4g}f" for x in v)

    lines = [
        "// Generated by make_body_albedos.py: do not edit.",
        "namespace RealStars;",
        "",
        "internal static partial class BodyColours",
        "{",
        "    /// <summary>Measured reflectance of the Sun's bodies by template Id: the share of the light they reflect (their",
        "    /// spherical albedo), their geometric albedo and phase integral, and the surface law that gives them (the",
        f"    /// law's k every {K_STEP:g} degrees of phase, N every 1/{N_STEPS} of the Sun's height). make_body_albedos.py",
        "    /// has the measurements behind each.</summary>",
        "    internal static readonly Dictionary<string, Photometry> MeasuredPhotometry = new()",
        "    {",
    ]
    for body, a, p, q, ks, ns, source in rows:
        lines.append(f'        ["{body}"] = new({a:.4f}f, {p:.4f}f, {q:.4f}f, "{source}",')
        lines.append(f"            new[] {{ {floats(ks)} }},")
        lines.append(f"            new[] {{ {floats(ns)} }}),")
    lines += ["    };", "}", ""]
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"wrote {len(rows)} bodies to {OUT}")


if __name__ == "__main__":
    main()
