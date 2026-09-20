# Real Stars

A star field overhaul for Kitten Space Agency, packaged as a
[StarMap](https://github.com/StarMapLoader/StarMap) mod. No game files are modified and no admin
rights are needed: at launch the mod builds its own copy of the game's shader tree in the mods
folder, patches the star shaders inside it, and uses Harmony to redirect the game's reads of those
two files into the copy. The copy is rebuilt from the current game files every launch.

Status: **skeleton**. The seam is in place and the shaders are byte-for-byte stock apart from a
marker comment, so the sky looks exactly as it does without the mod.

## What it will change

- **Magnitude-based brightness.** Brightness is currently carried by *size*: `Star.vert` sets the
  sprite radius to `scale / 255` in clip units, which makes Sirius a ~46 px octagon at 1080p while a
  faint star is 3.3 px. Since the packed byte follows size ∝ 10^(−0.164 m), the magnitude can be
  recovered in the vertex shader and used to drive a normalised point spread function with energy
  ∝ 10^(−0.4 m) instead.
- **A real point spread function**, replacing the simplified Celestia PSF whose peak is clamped at
  1.5, so bright stars stop reading as discs.
- **Scintillation** through atmospheres, driven by airmass along the view ray.

Not planned: auto exposure (the engine has no exposure control yet) and diffraction spikes.

Colour is already accurate and needs no work: the star binary the game ships carries linearised
Mitchell–Charity blackbody colours, within a few percent of CIE 1931 D65. The game's own
`generatestarbinary` command is the broken path — it reads V−I as though it were B−V and its colour
fit returns black outside −0.4…2.1, culling 158 naked-eye stars including Betelgeuse and Antares.
That only matters if we generate a catalogue of our own, which real-time parallax would require.

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
