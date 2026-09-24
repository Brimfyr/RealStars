# Credits and data terms

The code in this repository is MIT (see LICENSE). The one shipped data file is listed here with its
source and terms.

| File | Source | Terms |
|---|---|---|
| `assets/RealStars.bin` | Built by `make_star_binary.py` from the Hipparcos main catalogue (`hip_main.dat`), ESA 1997, *The Hipparcos and Tycho Catalogues*, ESA SP-1200, retrieved from VizieR as catalogue I/239 | Public catalogue data. Use with acknowledgement, below |

The binary holds, per star, a position in parsecs, an absolute V magnitude and an RGB colour. All
three are derived: the position from right ascension, declination and parallax; the magnitude from V
and parallax; the colour from B−V through an effective temperature, a Planck spectrum and the CIE 1931
observer. The Sun is added as a star at the origin.

Required acknowledgement when distributing a release:

> This mod uses the Hipparcos Catalogue (ESA 1997, ESA SP-1200), accessed through the VizieR catalogue
> access tool, CDS, Strasbourg, France (DOI: 10.26093/cds/vizier).

## Not shipped

The game's own star binary and Sun mesh are used in place and not redistributed.
