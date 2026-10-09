# Real Stars

Stars, planets and the Sun drawn with their real brightness, colour and position, for Kitten Space Agency. A [StarMap](https://github.com/StarMapLoader/StarMap) mod.

No game files are modified: the mod loads patched copies of the game's star, planet and Sun shaders at launch. Use the release built for your KSA version. Works alongside Real Atmospheres.

## Features

### Stars

- 124,585 stars from the AT-HYG catalogue: every star to magnitude 9, and every star within 20 parsecs
- Colours from real stellar spectra
- Faint stars fade toward white, as the eye sees them
- Brightness from magnitude, drawn with a realistic point spread function
- Parallax: stars sit at their real distances and shift as you travel
- Twinkling through atmospheres, following simulation time

### Planets

- Brightness from albedo, phase and distance, on the same magnitude scale as the stars
- Measured colours for the Solar System's planets, moons and small bodies
- Planetshine on vessels at its real strength, from every body
- Rings add to a planet's brightness, and planets and moons dim in their parent's shadow

### The Sun and the other stars

- Drawn as a star at every distance, at its real angular size, blending into the 3D sphere up close
- Sphere at its real size and colour, with limb darkening
- Every star in the game's systems is drawn the same way, at its measured brightness: Alpha Centauri, Barnard's Star and Tau Ceti included
- Replaces the stock sun sprite and lens flare

### Light and the eye

- Every star lights its planets in its real colour
- The eye adapts to the light in view, as far as human vision does: sunlit scenes look nearly white, a red dwarf's stay warm
- Adaptation follows the real light level and takes time, at a pace you set

### Starbursts and glare

- Starbursts modelled on the human eye: fine needles with coloured fringes, on the Sun, the bright planets and the brightest stars
- A soft glow around bright sources, which gives the Sun a corona
- Glare that burns out to white close to a bright source
- Stars, planets and the Sun fade behind planets, atmospheres, clouds, rings and vessels
- A partly covered Sun throws its starburst from the part still in view
- Follows the game's Lens Flare and Tonemap Exposure settings

## Settings

With [ModMenu](https://github.com/MrJeranimo/ModMenu) installed, the game's Mods menu has a Real Stars page:

- **Colour adaptation**: the eye's adaptation to the colour of the light. Unticked, every light is shown in its raw colour.
- **Adaptation time**: how long the visible part of the adaptation takes, from 0.02 s to 3 s (1.33 s by default).
- **Twinkle**: stars and planets twinkling through an atmosphere.
- **Starburst and glare**: the starbursts and soft glow. They also follow the game's Lens Flare setting.

Settings are saved in `RealStars\settings.toml` in the game's documents folder.

## Installation

Requires [StarMap](https://github.com/StarMapLoader/StarMap). Each release is named with the KSA build it was tested on.

**With Borea:** install Real Stars from the mod list.

**Manually:**

1. Install StarMap and run the game once through `StarMap.Loader.exe`.
2. Extract the release into `Documents\My Games\Kitten Space Agency\mods\`, giving `mods\RealStars\RealStars.dll`.
3. Launch the game once and close it. KSA adds new mods to `manifest.toml` disabled: set this mod's entry to `enabled = true`.
4. Launch through StarMap.

To uninstall, delete `mods\RealStars`.

## For star system authors

Set a star's `<Sunlight>` to its raw colour, the colour of its light against the display's white, scaled to the brightness you want (the stock lights are about 9). Real Stars adapts the picture to that light as an eye would. Raw colours by spectral type, from real spectra:

| Star | B−V | R | G | B |
|---|---|---|---|---|
| O5 V | −0.32 | 0.30 | 0.44 | 1.00 |
| B6 V | −0.14 | 0.39 | 0.53 | 1.00 |
| A0 V | 0.00 | 0.47 | 0.60 | 1.00 |
| F0 V | 0.29 | 0.72 | 0.78 | 1.00 |
| F5 V | 0.44 | 0.89 | 0.90 | 1.00 |
| G2 V (the Sun) | 0.65 | 1.00 | 0.91 | 0.86 |
| K0 V | 0.82 | 1.00 | 0.84 | 0.72 |
| K5 V | 1.15 | 1.00 | 0.59 | 0.36 |
| M0 V | 1.42 | 1.00 | 0.57 | 0.31 |
| M4 V | 1.65 | 1.00 | 0.66 | 0.25 |
| M6 V | 2.01 | 1.00 | 0.60 | 0.16 |
| K0 III | 1.02 | 1.00 | 0.80 | 0.60 |
| M1 III | 1.60 | 1.00 | 0.61 | 0.29 |
| M2 I | 1.84 | 1.00 | 0.56 | 0.18 |

The stock Sol's `R="9" G="9" B="9"` is read as the Sun's raw colour at that brightness, and the stock interstellar stars' lights as their stars' colours in the catalogue.

A star is drawn in the colour of its `<Sunlight>`. A star within about 0.1 parsec of a real one, and of about its brightness, takes that star's measured brightness. Any other star's brightness is estimated from its `<Mass>` and `<MeanRadius>`.

Set a planet's or moon's `<Color>` to its colour in white light. Its planetshine takes its map's average colour and its `<MeanAlbedo>`.

## Building

`deploy.ps1` builds the mod and installs it into your mods folder. `package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN` builds a release archive in `dist\`. Set `StarMapDir` to your StarMap folder. The `make_*.py` scripts rebuild the star catalogue and the generated data tables from the sources they cite.

## Credits

- Star catalogue built from [AT-HYG](https://www.astronexus.com/projects/at-hyg) v3.2 by David Nash (CC BY-SA 4.0), with positions from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from [VizieR](https://vizier.cds.unistra.fr/) (CDS, Strasbourg), catalogue I/239. Most distances and motions in AT-HYG come from ESA's Gaia DR3.
- Starbursts after T. Ritschel, M. Ihrke, J. R. Frisvad, J. Coppens, K. Myszkowski and H.-P. Seidel, "Temporal Glare: Real-Time Dynamic Simulation of the Scattering in the Human Eye", Eurographics 2009.
- What an eye sees of a faint light's colour after the naked-eye star colours of R. Neuhäuser et al., MNRAS 516, 693 (2022), and the small coloured lights of N. E. G. Hill (1947) and J. G. Holmes, Documenta Ophthalmologica 3, 240 (1949).
- Star colours from the spectral library of A. J. Pickles, "A Stellar Spectral Flux Library: 1150-25000 Å", PASP 110, 863 (1998), and the Sun's from the CALSPEC solar reference spectrum (Bohlin, Dickinson and Calzetti, 2001; Neckel and Labs, 1984, through the visible).
- Brightness of stars the catalogue lacks from M. J. Pecaut and E. E. Mamajek, "Intrinsic Colors, Temperatures, and Bolometric Corrections of Pre-main-sequence Stars", ApJS 208, 9 (2013), Table 5, as E. Mamajek maintains it.

The code is MIT; the star catalogue is CC BY-SA 4.0. [CREDITS.md](CREDITS.md) gives its sources and terms.
