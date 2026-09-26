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
- Saturn's rings add to its brightness
- Planets and moons dim in their parent's shadow
- Twinkling reduced by apparent size

### The Sun

- Drawn as a star at every distance, at its real angular size, blending into the 3D sphere up close
- Sphere at its real size and photosphere colour, with limb darkening
- Dims as it fills more of the screen
- Replaces the stock sun sprite and lens flare

### Occlusion and starbursts

- Stars, planets and the Sun fade behind planets, atmospheres and vessels
- Starbursts modelled on the human eye, on the Sun and on planets brighter than magnitude −3
- Starbursts fade as their source is covered or leaves the screen
- The Sun's starburst is drawn over everything else
- Starbursts follow the game's lens flare setting

## Installation

Requires [StarMap](https://github.com/StarMapLoader/StarMap). Each release is named with the KSA build it was tested on.

**With Borea:** install Real Stars from the mod list.

**Manually:**

1. Install StarMap and run the game once through `StarMap.Loader.exe`.
2. Extract the release into `Documents\My Games\Kitten Space Agency\mods\`, giving `mods\RealStars\RealStars.dll`.
3. Launch the game once and close it. KSA adds new mods to `manifest.toml` disabled: set this mod's entry to `enabled = true`.
4. Launch through StarMap.

To uninstall, delete `mods\RealStars`.

## Building

`deploy.ps1` builds the mod and installs it into your mods folder. `package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN` builds a release archive in `dist\`. Set `StarMapDir` to your StarMap folder. `make_star_binary.py` rebuilds the star catalogue from AT-HYG v3.2 (`athyg_32_reduced_m10.csv.gz`, in `assets`) and the Hipparcos main catalogue (`hip_main.dat`).

## Credits

- Star catalogue built from [AT-HYG](https://www.astronexus.com/projects/at-hyg) v3.2 by David Nash (CC BY-SA 4.0), with positions from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from [VizieR](https://vizier.cds.unistra.fr/) (CDS, Strasbourg), catalogue I/239. Most distances and motions in AT-HYG come from ESA's Gaia DR3.

The code is MIT; the star catalogue is CC BY-SA 4.0. [CREDITS.md](CREDITS.md) gives its sources and terms.
