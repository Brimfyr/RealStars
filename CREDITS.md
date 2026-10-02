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

## Body colours

The colours of the Sun's planets, moons and minor bodies (`BodyColours.Generated.cs`, compiled into the
mod) are derived by `make_body_colours.py` from published measurements, each cited there: planetary
albedos (Mallama et al. 2017), the giant planets' and Titan's spectra (Karkoschka 1998, NASA PDS), the
Galilean satellites (Johnson & McCord 1971; Cassini, Mayorga et al. 2020), the Moon (Lane & Irvine 1973;
McCord & Johnson 1970), the Eight-Color Asteroid Survey (Zellner et al. 1985, NASA PDS), small satellite
colours (Neese 2004, NASA PDS) and the MBOSS colours of the outer solar system (Hainaut et al. 2012).

## Body albedos

How the Sun's bodies reflect visible light (`BodyAlbedos.Generated.cs`) is derived by `make_body_albedos.py`
from published geometric albedos and phase curves, each cited there: the planets' phase curves from The
Astronomical Almanac (Mallama & Hilton 2018), the Earth's from Robinson (2025), the Moon's from Allen's
Astrophysical Quantities (Cox 2000), Titan's from Cassini (Garcia Munoz et al. 2017), satellite albedos and
phase integrals from Voyager, Cassini, New Horizons and ground photometry (as compiled by Brucker et al. 2009
and the JPL Solar System Dynamics group), asteroid albedos from the JPL Small-Body Database and the irregular
satellites' from NEOWISE (Grav et al. 2015).

## Not shipped

The game's own star binary and Sun mesh are used in place and not redistributed.
