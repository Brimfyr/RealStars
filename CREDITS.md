# Credits and data terms

The code in this repository is MIT (see LICENSE). The one shipped data file is listed here with its
sources and terms.

| File | Sources | Terms |
|---|---|---|
| `assets/RealStars.bin` | Built by `make_star_binary.py` from [AT-HYG](https://www.astronexus.com/projects/at-hyg) v3.2, magnitude-10 subset, by David Nash, and from the Hipparcos main catalogue (`hip_main.dat`), ESA 1997, *The Hipparcos and Tycho Catalogues*, ESA SP-1200, retrieved from VizieR as catalogue I/239 | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/), as an adaptation of AT-HYG. Acknowledgements below |

AT-HYG combines Tycho-2, Gaia DR3, Hipparcos, the Yale Bright Star Catalog and the Gliese-Jahreiss
Catalog. It supplies the stars to V = 9, their V magnitudes, B−V colours, distances, proper motions
and radial velocities; most distances and motions are Gaia DR3's. Hipparcos supplies the positions of
the 83,337 stars it has, because AT-HYG's are not all at one epoch.

The binary holds, per star, a position in parsecs, an absolute V magnitude, an RGB colour and a
velocity. All are derived: the position from right ascension, declination and distance, carried to
the game's start date (2025-11-30) along the star's motion; the magnitude from V and distance; the
colour from B−V through an effective temperature, a Planck spectrum and the CIE 1931 observer; the
velocity from proper motion, radial velocity and distance. The Sun is added as a star at the origin.

`assets/RealStars.bin` may be shared and adapted under CC BY-SA 4.0: credit the sources above, and
release adaptations under the same licence.

Acknowledgements to include when distributing a release:

> Star data from AT-HYG v3.2 by David Nash, astronexus.com, licensed CC BY-SA 4.0.
>
> This mod uses the Hipparcos Catalogue (ESA 1997, ESA SP-1200), accessed through the VizieR catalogue
> access tool, CDS, Strasbourg, France (DOI: 10.26093/cds/vizier).
>
> This work has made use of data from the European Space Agency (ESA) mission Gaia
> (https://www.cosmos.esa.int/gaia), processed by the Gaia Data Processing and Analysis Consortium
> (DPAC, https://www.cosmos.esa.int/web/gaia/dpac/consortium). Funding for the DPAC has been provided
> by national institutions, in particular the institutions participating in the Gaia Multilateral
> Agreement.

## Not shipped

The game's own star binary and Sun mesh are used in place and not redistributed.
