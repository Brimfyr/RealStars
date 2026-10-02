# Real Stars

Stars, planets and the Sun drawn with their real brightness, colour and position, for Kitten Space Agency. A [StarMap](https://github.com/StarMapLoader/StarMap) mod.

No game files are modified. At launch the mod builds patched copies of the game's star, planet and Sun shaders and loads those instead. If a game update changes one of those shaders, it is left stock until the mod is updated. Works alongside Real Atmospheres.

## Features

### Stars

- 123,568 stars from the AT-HYG catalogue, to magnitude 9, coloured from their B−V index
- Brightness from magnitude, drawn with a realistic point spread function
- Parallax: stars sit at their real distances and shift as you travel
- Proper motion: stars are placed for the game's date and move on as time passes
- Twinkling through atmospheres, stronger and more colourful toward the horizon, following simulation time

### Planets

- Brightness from albedo, phase and distance, on the same magnitude scale as the stars
- Measured colours for the Sun's planets, moons, dwarf planets, asteroids and comets, lit by the Sun's light
- Planetshine on vessels at its real strength: each body's measured colour, albedo and phase curve in visible light, over the part of its day side in view (a quarter of the sunlight in low Earth orbit)
- Planetshine from every body, airless moons included, and out to wherever it is still visible, not just within 15,000 km
- Saturn's rings add to its brightness
- Planets and moons dim in their parent's shadow
- Twinkling reduced by apparent size

### The Sun

- Drawn as a star at every distance, at its real angular size, shrinking into the 3D sphere up close
- Sphere at its real size and photosphere colour, with limb darkening
- Dims as it fills more of the screen
- Replaces the stock sun sprite and lens flare

### Light and the eye

- Every star lights in its real colour, the Sun's included
- The eye adapts to the light in view, and only partly, as people were measured to (Zhu et al., 2026): sunlit scenes look nearly white, a red dwarf's stay warm, and dim or shadowed scenes adapt less
- It follows a change of light over seconds to a minute, as eyes do (Fairchild and Reniff, 1995), and holds while the map is open

### Occlusion and starbursts

- Stars, planets and the Sun fade behind planets, atmospheres and vessels, and the Sun's starburst behind clouds
- Starbursts modelled on a simulation of the human eye: about a thousand fine needles with coloured fringes, on the Sun, the bright planets and the brightest stars
- A soft glow around bright sources, the eye's veiling glare, gives the Sun a corona
- The Sun's starburst comes from the part of the Sun still in view, in the colour of the sunlight reaching you
- Starbursts fade as their source is covered or leaves the screen
- The Sun's starburst is drawn over everything else
- Starbursts follow the game's lens flare setting, and the game's bloom settings no longer bury them

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

Set a star's `<Sunlight>` to its raw colour: what its temperature gives in sRGB (any blackbody colour table), scaled to the brightness you want. Real Stars adapts the picture to that light the way an eye does, which is only partly: a Sun-like star's light looks nearly white, a red dwarf's stays warm (measured by Zhu et al., 2026). The stock Sol's `R="9" G="9" B="9"` is read as the Sun's raw colour at that brightness.

Set a planet's or moon's `<Color>` to its colour in white light, as its maps are. Real Stars lights it with its star's light for its distant sprite; orbit lines keep the colour as written. A body's planetshine takes its map's average colour, and the albedo its surface is drawn with (from its `<MeanAlbedo>`, a Hapke single-scattering albedo).

## Building

`deploy.ps1` builds the mod and installs it into your mods folder. `package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN` builds a release archive in `dist\`. Set `StarMapDir` to your StarMap folder. `make_star_binary.py` rebuilds the star catalogue from AT-HYG v3.2 (`athyg_32_reduced_m10.csv.gz`, in `assets`) and the Hipparcos main catalogue (`hip_main.dat`). `make_body_colours.py` rebuilds `BodyColours.Generated.cs`, the bodies' measured colours, and `make_body_albedos.py` rebuilds `BodyAlbedos.Generated.cs`, their measured albedos and phase curves, from the sources they cite.

## Credits

- Star catalogue built from [AT-HYG](https://www.astronexus.com/projects/at-hyg) v3.2 by David Nash (CC BY-SA 4.0), with positions from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from [VizieR](https://vizier.cds.unistra.fr/) (CDS, Strasbourg), catalogue I/239. Most distances and motions in AT-HYG come from ESA's Gaia DR3.
- Starbursts after T. Ritschel, M. Ihrke, J. R. Frisvad, J. Coppens, K. Myszkowski and H.-P. Seidel, "Temporal Glare: Real-Time Dynamic Simulation of the Scattering in the Human Eye", Eurographics 2009.

The code is MIT; the star catalogue is CC BY-SA 4.0. [CREDITS.md](CREDITS.md) gives its sources and terms.
