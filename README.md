# Real Stars

A star field overhaul for Kitten Space Agency, packaged as a
[StarMap](https://github.com/StarMapLoader/StarMap) mod. No game files are modified and no admin
rights are needed: at launch the mod builds its own copy of the game's shader tree in the mods
folder, patches the star shaders inside it, and uses Harmony to redirect the game's reads of those
two files into the copy. The copy is rebuilt from the current game files every launch.

## What it changes

- **Brightness is a magnitude again.** Stock carries it in the sprite's *size*: `Star.vert` sets the
  quad radius from the packed byte, so Sirius is a 52 px octagon at 1080p. Worse, the fragment
  shader's point spread function works in an angle derived from the UV offset while its cutoff comes
  from the quad radius — unrelated quantities, so for anything bright the profile stays above the
  1.5 clamp across the whole quad and the star renders as a flat white disc.
- **A real point spread function.** A Moffat profile with β = 2, normalised to unit energy, whose
  1/r² wings are what diffraction and atmospheric seeing both leave. The sprite is sized to where
  that profile crosses one display level, so apparent size follows from brightness instead of
  standing in for it: Sirius becomes a 35 px sprite with a 3 px saturated core.
- **Its own star catalogue**, built from Hipparcos by `make_star_binary.py`. The shipped one holds
  almost no brightness information: its size byte runs 17–239 with a 90th percentile of 17, what
  variation exists below that floor is smuggled into the colour channel, and 92,687 of its 99,038
  stars are floored in both and render identically. End to end it spans 70:1, about 4.6 magnitudes.
  Ours stores V magnitude linearly at 24 bytes per magnitude, spanning 15,000:1 across 83,337 stars
  to V = 9.

Still to come: **scintillation** through atmospheres, driven by airmass along the view ray, and
possibly real-time **parallax**, which needs distances carried into the renderer.

Not planned: auto exposure (the engine has no exposure control yet) and diffraction spikes.

### On colour

The shipped binary's colours are accurate — linearised blackbody, within a few percent of CIE 1931
D65 — and ours agree with them to a median of 7/255 on matched stars, computed independently from
B−V through an effective temperature, a Planck spectrum and the CIE observer.

The game's own `generatestarbinary` is the broken path, and ours avoids all three of its faults: it
reads the V−I column as though it were B−V, its colour fit returns black outside −0.4…2.1 (culling
158 naked-eye stars, Betelgeuse and Antares among them), and it writes Y-up equatorial rather than
the ecliptic Z-up frame the solar system is built in.

## Install

Requires [StarMap](https://github.com/StarMapLoader/StarMap). Extract into your mods folder so you
get `mods\RealStars\RealStars.dll`; the folder must be named exactly `RealStars`, because KSA uses
the folder name as the mod id. KSA adds new mods to `manifest.toml` **disabled**, so set its entry
to `enabled = true` after the first launch.

## Living beside Real Atmospheres

Both mods patch shaders through the same seam, `RenderCore.ShaderModuleUtils.FromFile`, and both
build a full copy of the shader tree. They stay out of each other's way by claiming different files:
Real Atmospheres redirects the whole tree into its copy, while Real Stars redirects `Star.vert` and
`Star.frag` alone and runs its prefix first. Whichever order they load in, each file ends up served
by the mod that patched it.

The consequence is that the star shaders see stock copies of whatever they include. That is fine
while they only include `Common/`, and it is a decision point if scintillation later wants the
atmosphere functions Real Atmospheres patches.

## Building from source

`deploy.ps1` builds the class library and installs it into your mods folder;
`package.ps1 [-Version x.y.z] [-GameBuild vYYYY.M.D.NNNN]` builds a release zip in `dist\`. Both
ship only the assets listed in `release-files.txt`. Set `StarMapDir` to your StarMap folder if it is
somewhere the project does not look; the build references `StarMap.API.dll` and `0Harmony.dll` from
there.
