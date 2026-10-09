# Real Stars

Stars, planets and the Sun drawn with their real brightness, colour and position, for Kitten Space Agency. A [StarMap](https://github.com/StarMapLoader/StarMap) mod.

No game files are modified. At launch the mod builds patched copies of the game's star, planet and Sun shaders and loads those instead. If a game update changes one of those shaders, it is left stock until the mod is updated. Works alongside Real Atmospheres.

## Features

### Stars

- 124,585 stars from the AT-HYG catalogue: every star to magnitude 9, and every star within 20 parsecs however faint
- Each in the colour of real spectra of its kind (dwarf, giant or supergiant) at its B−V, not a blackbody's: Vega and Sirius blue-white, red dwarfs a yellower orange than a blackbody of their temperature, and the Sun from a measured solar spectrum
- Seen as an eye sees them: a faint star's colour fades into white, blue before orange, so the sky is mostly white with a few bright orange and blue-white stars, and a star shows its full colour as you approach it
- Brightness from magnitude, drawn with a realistic point spread function
- Parallax: stars sit at their real distances and shift as you travel
- Each star is where it is on the game's start date (2025-11-30), carried there along its own measured motion
- Twinkling through atmospheres, stronger and more colourful toward the horizon, following simulation time

### Planets

- Brightness from albedo, phase and distance, on the same magnitude scale as the stars
- Measured colours for the Sun's planets, moons, dwarf planets, asteroids and comets, lit by the Sun's light
- Planetshine on vessels at its real strength: each body's measured colour, albedo and phase curve in visible light, over the part of its day side in view (a quarter of the sunlight in low Earth orbit)
- Planetshine from every body, airless moons included, and out to wherever it is still visible, not just within 15,000 km
- Saturn's rings add to its brightness
- Planets and moons dim in their parent's shadow
- Twinkling reduced by apparent size

### The Sun and the other stars

- Drawn as a star at every distance, at its real angular size, shrinking into the 3D sphere up close
- Sphere at its real size and photosphere colour, with limb darkening
- Dims as it fills more of the screen
- Replaces the stock sun sprite and lens flare
- Every star in the game's systems is drawn the same way, Alpha Centauri, Barnard's Star and Tau Ceti included: a star in the sky from afar, the sphere up close, each once, at its measured brightness and in the colour of its light
- The star lighting the scene has its own brightness and starburst, and the planets it lights are as bright as it makes them

### Light and the eye

- Every star lights in its real colour, the Sun's included: the game's own stars' lights are read as their stars' colours in the catalogue (Proxima Centauri's an M dwarf's orange, not the pale 4,000 K its stock light gives)
- The eye adapts to the light in view, and only partly, as people were measured to (Zhu et al., 2026): sunlit scenes look nearly white, a red dwarf's stay warm, and dim or shadowed scenes adapt less
- It adapts to the light as strong as it really is there, not the game's Earth-strength daylight everywhere: at Pluto, or on a red dwarf's planet, the light is dimmer, and the eye adapts to it less
- It follows a change of light the way eyes do (Fairchild and Reniff, 1995), at a pace you set (see Settings), and holds while the map is open

### Occlusion and starbursts

- Stars, planets and the Sun fade behind planets, atmospheres and vessels, and the Sun's starburst behind clouds
- Starbursts modelled on a simulation of the human eye: about a thousand fine needles with coloured fringes, on the Sun, the bright planets and the brightest stars
- A soft glow around bright sources, the eye's veiling glare, gives the Sun a corona
- The Sun's starburst comes from the part of the Sun still in view, in the colour of the sunlight reaching you
- Close to a bright source the glare burns out to white, with an uneven outline that breaks up into the needles
- A large disc that is partly covered throws its starburst from the shape that is left of it
- Starbursts fade as their source is covered or leaves the screen
- The Sun's starburst is drawn over everything else
- Starbursts follow the game's lens flare setting, and the game's bloom settings no longer bury them
- Every source follows the game's Tonemap Exposure setting: at a lower exposure a starburst draws in, and what is brighter than white stays white rather than turning gray

## Installation

Requires [StarMap](https://github.com/StarMapLoader/StarMap). Each release is named with the KSA build it was tested on.

**With Borea:** install Real Stars from the mod list.

**Manually:**

1. Install StarMap and run the game once through `StarMap.Loader.exe`.
2. Extract the release into `Documents\My Games\Kitten Space Agency\mods\`, giving `mods\RealStars\RealStars.dll`.
3. Launch the game once and close it. KSA adds new mods to `manifest.toml` disabled: set this mod's entry to `enabled = true`.
4. Launch through StarMap.

To uninstall, delete `mods\RealStars`.

## Settings

With [ModMenu](https://github.com/MrJeranimo/ModMenu) installed, the game's Mods menu has a Real Stars page:

- **Colour adaptation**: the eye adapting to the colour of the light it is in, as far as people do. Unticked, every light is shown in its raw colour.
- **Adaptation time**: how long the quick part of the eye's adaptation to a new light takes, which is the change you see happen. The eye adapts in two stages (Fairchild and Reniff, 1995): a quick one, a little over half of the change, and a slow one that finishes the rest over thirty times as long, too gradually to notice. From 0.02 s, at once, to 3 s, the time people take; 1.33 s by default.
- **Twinkle**: stars and planets twinkling when seen through an atmosphere.
- **Starburst and glare**: the starburst and soft glow round the Sun, the bright planets and the brightest stars. They also follow the game's Lens Flare setting and its intensity: either one off is no glare.

The settings are kept in `RealStars\settings.toml` in the game's documents folder (with Borea, the instance's folder), which can also be edited with the game closed.

## For star system authors

Set a star's `<Sunlight>` to its raw colour, scaled to the brightness you want (the stock lights are about 9). A raw colour is the colour of the star's light against the display's white, before any eye adapts to it: Real Stars adapts the picture to that light the way an eye does, which is only partly, so a Sun-like star's light looks nearly white and a red dwarf's stays warm (measured by Zhu et al., 2026). These are the raw colours Real Stars gives stars of each kind, from real spectra; a blackbody of the star's temperature is close for F, G and K stars, but A stars are bluer than any blackbody and red dwarfs yellower:

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

A star is drawn in the colour of its `<Sunlight>`. A star at the place of a real one (within about 0.1 parsec) and of about its brightness is drawn at that star's measured brightness. Any other star's brightness comes from its `<Mass>` and `<MeanRadius>`: a main-sequence dwarf's of that mass when the radius is a dwarf's (Pecaut and Mamajek, 2013), otherwise its radius and the temperature of its `<Sunlight>`'s colour.

Set a planet's or moon's `<Color>` to its colour in white light, as its maps are. Real Stars lights it with its star's light for its distant sprite; orbit lines keep the colour as written. A body's planetshine takes its map's average colour, and the albedo its surface is drawn with (from its `<MeanAlbedo>`, a Hapke single-scattering albedo).

## Building

`deploy.ps1` builds the mod and installs it into your mods folder. `package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN` builds a release archive in `dist\`. Set `StarMapDir` to your StarMap folder. `make_star_binary.py` rebuilds the star catalogue from AT-HYG v3.2 (`athyg_32_reduced_m10.csv.gz`, in `assets`) and the Hipparcos main catalogue (`hip_main.dat`), coloured by `star_colours.py` from the Pickles library and a measured solar spectrum, which it downloads to `assets`; it also writes `SunLight.Generated.cs`, the Sun's and the stock stars' light colours. `make_star_estimates.py` rebuilds `StarEstimates.Generated.cs`, from which a modded star's brightness is estimated, from Mamajek's dwarf sequence table. `make_body_colours.py` rebuilds `BodyColours.Generated.cs`, the bodies' measured colours, and `make_body_albedos.py` rebuilds `BodyAlbedos.Generated.cs`, their measured albedos and phase curves, from the sources they cite.

## Credits

- Star catalogue built from [AT-HYG](https://www.astronexus.com/projects/at-hyg) v3.2 by David Nash (CC BY-SA 4.0), with positions from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from [VizieR](https://vizier.cds.unistra.fr/) (CDS, Strasbourg), catalogue I/239. Most distances and motions in AT-HYG come from ESA's Gaia DR3.
- Starbursts after T. Ritschel, M. Ihrke, J. R. Frisvad, J. Coppens, K. Myszkowski and H.-P. Seidel, "Temporal Glare: Real-Time Dynamic Simulation of the Scattering in the Human Eye", Eurographics 2009.
- What an eye sees of a faint light's colour after the naked-eye star colours of R. Neuhäuser et al., MNRAS 516, 693 (2022), and the small coloured lights of N. E. G. Hill (1947) and J. G. Holmes, Documenta Ophthalmologica 3, 240 (1949).
- Star colours from the spectral library of A. J. Pickles, "A Stellar Spectral Flux Library: 1150-25000 Å", PASP 110, 863 (1998), and the Sun's from the CALSPEC solar reference spectrum (Bohlin, Dickinson and Calzetti, 2001; Neckel and Labs, 1984, through the visible).
- Brightness of stars the catalogue lacks from M. J. Pecaut and E. E. Mamajek, "Intrinsic Colors, Temperatures, and Bolometric Corrections of Pre-main-sequence Stars", ApJS 208, 9 (2013), Table 5, as E. Mamajek maintains it.

The code is MIT; the star catalogue is CC BY-SA 4.0. [CREDITS.md](CREDITS.md) gives its sources and terms.
