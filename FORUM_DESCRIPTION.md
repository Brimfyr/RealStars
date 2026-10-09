\[HEADING=1]Real Stars\[/HEADING]

Stars, planets and the Sun drawn with their real brightness, colour and position, for Kitten Space Agency. A \[URL=https://github.com/StarMapLoader/StarMap]StarMap\[/URL] mod.

No game files are modified: the mod loads patched copies of the game's star, planet and Sun shaders at launch. Use the release built for your KSA version. Works alongside Real Atmospheres.

\[HEADING=2]Download\[/HEADING]

\[URL='https://github.com/KSAModding/content-index/blob/main/listings/RealStars.toml']Borea\[/URL]

\[URL='https://spacedock.info/mod/4608/Real%20Stars']SpaceDock\[/URL]

\[URL='https://github.com/Brimfyr/RealStars']GitHub\[/URL]

\[HEADING=2]Features\[/HEADING]

\[HEADING=3]Stars\[/HEADING]

\[LIST]

\[\*]124,585 stars from the AT-HYG catalogue: every star to magnitude 9, and every star within 20 parsecs

\[\*]Colours from real stellar spectra

\[\*]Faint stars fade toward white, as the eye sees them

\[\*]Brightness from magnitude, drawn with a realistic point spread function

\[\*]Parallax: stars sit at their real distances and shift as you travel

\[\*]Twinkling through atmospheres, following simulation time

\[/LIST]

\[HEADING=3]Planets\[/HEADING]

\[LIST]

\[\*]Brightness from albedo, phase and distance, on the same magnitude scale as the stars

\[\*]Measured colours for the Solar System's planets, moons and small bodies

\[\*]Planetshine on vessels at its real strength, from every body

\[\*]Rings add to a planet's brightness, and planets and moons dim in their parent's shadow

\[/LIST]

\[HEADING=3]The Sun and the other stars\[/HEADING]

\[LIST]

\[\*]Drawn as a star at every distance, at its real angular size, blending into the 3D sphere up close

\[\*]Sphere at its real size and colour, with limb darkening

\[\*]Every star in the game's systems is drawn the same way, at its measured brightness: Alpha Centauri, Barnard's Star and Tau Ceti included

\[\*]Replaces the stock sun sprite and lens flare

\[/LIST]

\[HEADING=3]Light and the eye\[/HEADING]

\[LIST]

\[\*]Every star lights its planets in its real colour

\[\*]The eye adapts to the light in view, as far as human vision does: sunlit scenes look nearly white, a red dwarf's stay warm

\[\*]Adaptation follows the real light level and takes time, at a pace you set

\[/LIST]

\[HEADING=3]Starbursts and glare\[/HEADING]

\[LIST]

\[\*]Starbursts modelled on the human eye: fine needles with coloured fringes, on the Sun, the bright planets and the brightest stars

\[\*]A soft glow around bright sources, which gives the Sun a corona

\[\*]Glare that burns out to white close to a bright source

\[\*]Stars, planets and the Sun fade behind planets, atmospheres, clouds, rings and vessels

\[\*]A partly covered Sun throws its starburst from the part still in view

\[\*]Follows the game's Lens Flare and Tonemap Exposure settings

\[/LIST]

\[HEADING=2]Screenshots\[/HEADING]

\[SPOILER="Show images"]

\[ATTACH=full]2119\[/ATTACH]

\[ATTACH=full]2120\[/ATTACH]

\[ATTACH=full]2118\[/ATTACH]

\[ATTACH=full]2117\[/ATTACH]

\[/SPOILER]

\[HEADING=2]Settings\[/HEADING]

With \[URL=https://github.com/MrJeranimo/ModMenu]ModMenu\[/URL] installed, the game's Mods menu has a Real Stars page:

\[LIST]

\[\*]\[B]Colour adaptation\[/B]: the eye's adaptation to the colour of the light. Unticked, every light is shown in its raw colour.

\[\*]\[B]Adaptation time\[/B]: how long the visible part of the adaptation takes, from 0.02 s to 3 s (1.33 s by default).

\[\*]\[B]Twinkle\[/B]: stars and planets twinkling through an atmosphere.

\[\*]\[B]Starburst and glare\[/B]: the starbursts and soft glow. They also follow the game's Lens Flare setting.

\[/LIST]

Settings are saved in \[ICODE]RealStars\\settings.toml\[/ICODE] in the game's documents folder.

\[HEADING=2]Installation\[/HEADING]

Requires \[URL=https://github.com/StarMapLoader/StarMap]StarMap\[/URL]. Each release is named with the KSA build it was tested on.

\[B]With Borea:\[/B] install Real Stars from the mod list.

\[B]Manually:\[/B]

\[LIST=1]

\[\*]Install StarMap and run the game once through \[ICODE]StarMap.Loader.exe\[/ICODE].

\[\*]Extract the release into \[ICODE]Documents\\My Games\\Kitten Space Agency\\mods\\\[/ICODE], giving \[ICODE]mods\\RealStars\\RealStars.dll\[/ICODE].

\[\*]Launch the game once and close it. KSA adds new mods to \[ICODE]manifest.toml\[/ICODE] disabled: set this mod's entry to \[ICODE]enabled = true\[/ICODE].

\[\*]Launch through StarMap.

\[/LIST]

To uninstall, delete \[ICODE]mods\\RealStars\[/ICODE].

\[HEADING=2]For star system authors\[/HEADING]

The \[URL=https://github.com/Brimfyr/RealStars#for-star-system-authors]README\[/URL] explains how Real Stars reads a star's light and a body's colour, and lists the raw colour of each kind of star.

\[HEADING=2]Building\[/HEADING]

\[ICODE]deploy.ps1\[/ICODE] builds the mod and installs it into your mods folder. \[ICODE]package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN\[/ICODE] builds a release archive in \[ICODE]dist\\\[/ICODE]. Set \[ICODE]StarMapDir\[/ICODE] to your StarMap folder. The \[ICODE]make\_\*.py\[/ICODE] scripts rebuild the star catalogue and the generated data tables from the sources they cite.

\[HEADING=2]Credits\[/HEADING]

\[LIST]

\[\*]Star catalogue built from \[URL=https://www.astronexus.com/projects/at-hyg]AT-HYG\[/URL] v3.2 by David Nash (CC BY-SA 4.0), with positions from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from \[URL=https://vizier.cds.unistra.fr/]VizieR\[/URL] (CDS, Strasbourg), catalogue I/239. Most distances and motions in AT-HYG come from ESA's Gaia DR3.

\[\*]Starbursts after T. Ritschel, M. Ihrke, J. R. Frisvad, J. Coppens, K. Myszkowski and H.-P. Seidel, "Temporal Glare: Real-Time Dynamic Simulation of the Scattering in the Human Eye", Eurographics 2009.

\[\*]What an eye sees of a faint light's colour after the naked-eye star colours of R. Neuhäuser et al., MNRAS 516, 693 (2022), and the small coloured lights of N. E. G. Hill (1947) and J. G. Holmes, Documenta Ophthalmologica 3, 240 (1949).

\[\*]Star colours from the spectral library of A. J. Pickles, "A Stellar Spectral Flux Library: 1150-25000 Å", PASP 110, 863 (1998), and the Sun's from the CALSPEC solar reference spectrum (Bohlin, Dickinson and Calzetti, 2001; Neckel and Labs, 1984, through the visible).

\[\*]Brightness of stars the catalogue lacks from M. J. Pecaut and E. E. Mamajek, "Intrinsic Colors, Temperatures, and Bolometric Corrections of Pre-main-sequence Stars", ApJS 208, 9 (2013), Table 5, as E. Mamajek maintains it.

\[/LIST]

The code is MIT; the star catalogue is CC BY-SA 4.0. \[URL=https://github.com/Brimfyr/RealStars/blob/main/CREDITS.md]CREDITS.md\[/URL] gives its sources and terms.

