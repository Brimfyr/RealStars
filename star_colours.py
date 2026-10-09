# -*- coding: utf-8 -*-
"""The colours of stars, from their measured spectra.

A star is not a blackbody, and its colour is not a blackbody's. Measured through the CIE 1931 observer, an
A0 dwarf looks like a 14,000 K blackbody, bluer than either its 9,700 K temperature or the 10,100 K blackbody
its B-V matches; a red dwarf looks less red than a blackbody at its temperature and greener than any
blackbody at all, because its molecular bands take out more red than blue. Up to 0.02 in CIE u'v' between
the two, ten times what an eye notices side by side.

So every star takes the colour of a real spectrum of its kind: the empirical library of Pickles (1998), whose
131 spectra are averages of observed stars of each spectral type and luminosity class, interpolated by B-V
along the dwarf, giant or supergiant sequence the star is on. The Sun takes its own, a measured solar spectrum
(CALSPEC's sun_reference_stis_002, Bohlin, Dickinson & Calzetti 2001: Neckel & Labs 1984 through the visible).

Checked against two other sets before trusting it: the empirical SDSS templates of Kesseli et al. (2017) agree
with Pickles' K and M dwarfs to 0.003-0.009 in u'v'; the PHOENIX (BT-Settl) model spectra do not, by 0.01-0.025
and too red, which is the known trouble models have with red dwarfs' visible colours.

Each spectrum is placed on the B-V axis at its type's standard colour, because synthetic photometry from the
spectra runs blue of the standard Johnson system for red stars (by 0.2 for late M dwarfs, where the B band sees
only the steep blue wing; giants' synthetic B-V stops rising at K4 III where real giants' goes on to 1.6):
  dwarfs        Pecaut & Mamajek's standard B-V for the type, and the G2 dwarf is the Sun itself, at 0.65;
  giants        the median measured B-V of the catalogue's own giants of the type within 150 pc, where there is
                little reddening (G8 III 0.93 from 348 stars, K0 III 1.02 from 754, K5 III 1.52, M1 III 1.60),
                ending at M1 III: past it giants' B-V turns back while their colour hardly changes (K5-M2 III
                differ by 0.01 in green), so a redder giant takes M1 III's;
  supergiants   their synthetic B-V moved onto the standard system by what the dwarfs need at the same synthetic
                colour (too few nearby to measure; M2 I lands at 1.83, Betelgeuse's and Antares' own).
"""
import os
import re
import urllib.request
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "assets")
PICKLES_URL = "https://archive.stsci.edu/hlsps/reference-atlases/cdbs/grid/pickles/dat_uvk/"
PICKLES_DIR = os.path.join(ASSETS, "pickles")
SUN_URL = "https://archive.stsci.edu/hlsps/reference-atlases/cdbs/current_calspec/sun_reference_stis_002.fits"
SUN_PATH = os.path.join(ASSETS, "sun_reference_stis_002.fits")
DWARFS_PATH = os.path.join(ASSETS, "EEM_dwarf_UBVIJHK_colors_Teff.txt")
ATHYG_PATH = os.path.join(ASSETS, "athyg_32_reduced_m10.csv.gz")
SUN_BV = 0.65                        # the Sun's B-V (Ramirez et al. 2012: 0.653), and the G2 V standard

# The sequences, in Pickles' own type names: the solar-metallicity spectra, and the giants only as far as their
# B-V keeps rising (past M1 III it turns back as the molecular bands deepen in V).
DWARFS = ("O5V", "O9V", "B0V", "B1V", "B3V", "B57V", "B8V", "B9V", "A0V", "A2V", "A3V", "A5V", "A7V", "F0V", "F2V",
          "F5V", "F6V", "F8V", "G0V", "G2V", "G5V", "G8V", "K0V", "K2V", "K3V", "K4V", "K5V", "K7V", "M0V", "M1V",
          "M2V", "M2.5V", "M3V", "M4V", "M5V", "M6V")
GIANTS = ("O8III", "B12III", "B3III", "B5III", "B9III", "A0III", "A3III", "A5III", "A7III", "F0III", "F2III", "F5III",
          "G0III", "G5III", "G8III", "K0III", "K1III", "K2III", "K3III", "K4III", "K5III", "M0III", "M1III")
SUPERGIANTS = ("B0I", "B1I", "B3I", "B5I", "B8I", "A0I", "A2I", "F0I", "F5I", "F8I", "G0I", "G2I", "G5I", "G8I",
               "K2I", "K3I", "K4I", "M2I")
STANDARD_NAME = {"B57V": "B6V"}      # Pickles' B5-7 V, by the middle of its range

DWARF, GIANT, SUPERGIANT = 0, 1, 2

# XYZ to linear sRGB (D65 primaries), as make_star_binary.py has it
XYZ_TO_SRGB = np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]])

# Bessell (1990) B and V
B_BAND = np.array([[360, 0], [370, .03], [380, .134], [390, .567], [400, .92], [410, .978], [420, 1], [430, .978],
                   [440, .935], [450, .853], [460, .74], [470, .64], [480, .536], [490, .424], [500, .325],
                   [510, .235], [520, .15], [530, .095], [540, .043], [550, .009], [560, 0]], float)
V_BAND = np.array([[470, 0], [480, .03], [490, .163], [500, .458], [510, .78], [520, .967], [530, 1], [540, .973],
                   [550, .898], [560, .792], [570, .684], [580, .574], [590, .461], [600, .359], [610, .27],
                   [620, .197], [630, .135], [640, .081], [650, .045], [660, .025], [670, .017], [680, .013],
                   [690, .009], [700, 0]], float)


# ------------------------------------------------------------------------------------------------ FITS
def _fits_table(path):
    """The first binary table of a FITS file as {column: array}: just what the library's files need."""
    raw = open(path, "rb").read()
    pos = 0
    while pos < len(raw):
        cards, done = {}, False
        while not done:
            block = raw[pos:pos + 2880]
            pos += 2880
            for i in range(0, 2880, 80):
                card = block[i:i + 80].decode("ascii", "replace")
                if card[:8].strip() == "END":
                    done = True
                    break
                if card[8:10] == "= ":
                    value = card[10:].split(" /")[0].strip()
                    cards[card[:8].strip()] = value.strip("'").strip()
        size = 0
        if int(cards.get("NAXIS", 0)):
            size = abs(int(cards["BITPIX"])) // 8
            for k in range(1, int(cards["NAXIS"]) + 1):
                size *= int(cards[f"NAXIS{k}"])
        if cards.get("XTENSION") == "BINTABLE":
            kinds = {"E": ">f4", "D": ">f8", "J": ">i4", "I": ">i2"}
            names, dtype = [], []
            for k in range(1, int(cards["TFIELDS"]) + 1):
                form = cards[f"TFORM{k}"]
                names.append(cards[f"TTYPE{k}"])
                dtype.append((names[-1], f"S{int(form[:-1])}" if form.endswith("A") else kinds[form[-1]]))
            rows = np.frombuffer(raw[pos:pos + size], dtype=np.dtype(dtype))
            return {n: (np.char.decode(rows[n], "ascii") if rows[n].dtype.kind == "S" else rows[n].astype(float))
                    for n in names}
        pos += (size + 2879) // 2880 * 2880
    raise ValueError("no binary table in " + path)


def _fetch(url, path):
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        print(f"  downloading {url}")
        with urllib.request.urlopen(url, timeout=120) as r, open(path, "wb") as f:
            f.write(r.read())
    return path


# -------------------------------------------------------------------------------------------- colour
def _observer():
    from make_star_binary import cie_xyz
    lam = np.arange(380.0, 781.0, 5.0)
    return lam, np.stack(cie_xyz(lam))


def xyz_of(lam_nm, flam):
    """CIE 1931 XYZ of a spectrum (F_lambda), luminance 1: the observer the whole catalogue uses."""
    lam, cmf = _observer()
    xyz = cmf @ np.interp(lam, lam_nm, flam)
    return xyz / xyz[1]


def srgb_of(xyz):
    """Raw linear sRGB against D65, brightest channel 1, as the catalogue stores a colour."""
    c = np.clip(np.atleast_2d(xyz) @ XYZ_TO_SRGB.T, 0.0, None)
    return c / np.maximum(c.max(1, keepdims=True), 1e-12)


def synthetic_bv(lam_nm, flam):
    fine = np.arange(350.0, 720.1, 1.0)
    f = np.interp(fine, lam_nm, flam)
    b = np.interp(fine, B_BAND[:, 0], B_BAND[:, 1])
    v = np.interp(fine, V_BAND[:, 0], V_BAND[:, 1])
    return -2.5 * np.log10((f * b * fine).sum() / (f * v * fine).sum())


def _standard_bv():
    """Dwarf B-V by spectral type, Pecaut & Mamajek."""
    lines = open(DWARFS_PATH, encoding="latin-1").read().splitlines()
    head = next(i for i, l in enumerate(lines) if l.startswith("#SpT"))
    names = lines[head].lstrip("#").split()
    spt, bv = names.index("SpT"), names.index("B-V")
    out = {}
    for l in lines[head + 1:]:
        if l.startswith("#"):
            break
        p = l.split()
        try:
            out[p[spt]] = float(p[bv])
        except (ValueError, IndexError):
            pass
    return out


_TYPE = re.compile(r"^\s*([OBAFGKM])\s*(\d(?:\.\d)?)\s*[-/]?\s*(Iab|Ia|Ib|III|II|IV|V|I)?(?![a-z])")


def giant_standards(within_pc=150.0, at_least=5):
    """Median measured B-V of the catalogue's giants by type (as 'K5III'), within this distance and to V 9."""
    import csv
    import gzip
    found = {}
    with gzip.open(ATHYG_PATH, "rt", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            m = _TYPE.match(r["spect"] or "")
            if not m or m.group(3) != "III":
                continue
            try:
                v, d, bv = float(r["mag"]), float(r["dist"] or 0.0), float(r["ci"])
            except ValueError:
                continue
            if 0.0 < d <= within_pc and v <= 9.0:
                found.setdefault(f"{m.group(1)}{float(m.group(2)):g}III", []).append(bv)
    return {t: float(np.median(b)) for t, b in found.items() if len(b) >= at_least}


_SEQUENCES = None


def sequences():
    """For each class: B-V on the standard system, and the chromaticity (x, y) of the spectrum there."""
    global _SEQUENCES
    if _SEQUENCES is not None:
        return _SEQUENCES
    index = _fits_table(_fetch(PICKLES_URL + "pickles_uk.fits", os.path.join(PICKLES_DIR, "pickles_uk.fits")))
    files = {t.strip(): f.strip() for f, t in zip(index["FILENAME"], index["SPTYPE"])}

    def spectrum(t):
        s = _fits_table(_fetch(PICKLES_URL + files[t] + ".fits", os.path.join(PICKLES_DIR, files[t] + ".fits")))
        return s["WAVELENGTH"] / 10.0, s["FLUX"]

    zero = -synthetic_bv(*spectrum("A0V"))                      # A0 V at B-V 0.00, as the standard has it
    standard = _standard_bv()

    def chroma_of(xyz):
        return xyz[0] / xyz.sum(), xyz[1] / xyz.sum()

    def chroma(t):
        return chroma_of(xyz_of(*spectrum(t)))

    dwarfs = [(standard[STANDARD_NAME.get(t, t)], *chroma(t), synthetic_bv(*spectrum(t)) + zero, t)
              for t in DWARFS if t != "G2V"]
    dwarfs.append((SUN_BV, *chroma_of(_sun_xyz()), SUN_BV, "the Sun"))
    # What a dwarf's synthetic colour needs to reach the standard one, as a smooth function of it.
    syn = np.array([d[3] for d in dwarfs if d[4] != "the Sun"])
    off = np.array([d[0] for d in dwarfs if d[4] != "the Sun"]) - syn
    fit = np.polyfit(syn, off, 2)

    def standardised(t, measured=None):
        b = synthetic_bv(*spectrum(t)) + zero
        return (measured.get(t, b + np.polyval(fit, b)) if measured else b + np.polyval(fit, b)), *chroma(t), b, t

    measured = giant_standards()
    giants = [standardised(t, measured) for t in GIANTS]
    _SEQUENCES = {DWARF: sorted(dwarfs), GIANT: sorted(giants),
                  SUPERGIANT: sorted(standardised(t) for t in SUPERGIANTS)}
    for c, seq in _SEQUENCES.items():
        b = [p[0] for p in seq]
        if any(b2 <= b1 for b1, b2 in zip(b, b[1:])):
            raise ValueError(f"sequence {c} is not in rising B-V: " + ", ".join(f"{p[4]} {p[0]:.2f}" for p in seq))
    return _SEQUENCES


def colours(bv, cls):
    """Raw linear sRGB, brightest channel 1, for stars of these B-V on these sequences."""
    bv = np.asarray(bv, float)
    cls = np.asarray(cls)
    x = np.empty_like(bv)
    y = np.empty_like(bv)
    for c, seq in sequences().items():
        on = cls == c
        b = np.array([s[0] for s in seq])
        x[on] = np.interp(bv[on], b, [s[1] for s in seq])
        y[on] = np.interp(bv[on], b, [s[2] for s in seq])
    xyz = np.stack([x / y, np.ones_like(y), (1.0 - x - y) / y], 1)
    return srgb_of(xyz)


def sun_spectrum():
    """A measured solar spectrum, nm and F_lambda: CALSPEC's sun_reference_stis_002."""
    s = _fits_table(_fetch(SUN_URL, SUN_PATH))
    return s["WAVELENGTH"] / 10.0, s["FLUX"]


def _sun_xyz():
    return xyz_of(*sun_spectrum())


def sun():
    """The Sun's raw colour, from a measured solar spectrum."""
    return srgb_of(_sun_xyz())[0]


# ---------------------------------------------------------------------------------------- which kind
_LUMINOSITY = re.compile(r"(?<![A-Za-z])(Iab|Ia|Ib|III|II|IV|VI|V|I)(?![a-z])")


def classify(spect, bv, absmag, known):
    """Dwarf, giant or supergiant: by the luminosity class in the spectral type where it has one, otherwise by
    how far above the main sequence the star sits; giant for a red star of unknown distance, as most red stars
    to V 9 are."""
    ms = sorted((b, mv) for b, mv in _main_sequence())
    ms_b = np.array([p[0] for p in ms]); ms_mv = np.array([p[1] for p in ms])
    out = np.full(len(bv), DWARF)
    for i, (sp, b, m, k) in enumerate(zip(spect, bv, absmag, known)):
        hit = _LUMINOSITY.search(sp or "")
        if hit:
            lc = hit.group(1)
            out[i] = SUPERGIANT if lc in ("I", "Ia", "Iab", "Ib") else GIANT if lc in ("II", "III") else DWARF
        elif not k:
            out[i] = GIANT if b > 0.8 else DWARF
        else:
            above = np.interp(b, ms_b, ms_mv) - m        # magnitudes brighter than a dwarf of its colour
            if b > 0.5 and above > 2.5:
                out[i] = SUPERGIANT if m < -4.0 else GIANT
    return out


def _main_sequence():
    lines = open(DWARFS_PATH, encoding="latin-1").read().splitlines()
    head = next(i for i, l in enumerate(lines) if l.startswith("#SpT"))
    names = lines[head].lstrip("#").split()
    bv, mv = names.index("B-V"), names.index("Mv")
    for l in lines[head + 1:]:
        if l.startswith("#"):
            break
        p = l.split()
        try:
            yield float(p[bv]), float(p[mv])
        except (ValueError, IndexError):
            continue
