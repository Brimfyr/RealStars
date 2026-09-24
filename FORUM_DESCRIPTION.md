\[HEADING=1]Real Stars\[/HEADING]

Stars, planets and the Sun drawn with their real brightness, colour and position, for Kitten Space Agency. A \[URL='https://github.com/StarMapLoader/StarMap']StarMap\[/URL] mod.

No game files are modified. At launch the mod builds patched copies of the game's star, planet and Sun shaders and loads those instead. If a game update changes one of those shaders, it is left stock until the mod is updated. Works alongside Real Atmospheres.

\[HEADING=2]Download\[/HEADING]

\[URL='https://github.com/Brimfyr/RealStars']GitHub\[/URL]

\[HEADING=2]Features\[/HEADING]

\[HEADING=3]Stars\[/HEADING]

\[LIST]

\[\*]83,337 stars from the Hipparcos catalogue, to magnitude 9, coloured from their B−V index

\[\*]Brightness from magnitude, drawn with a realistic point spread function

\[\*]Parallax: stars sit at their real distances and shift as you travel

\[\*]Twinkling through atmospheres, stronger and more colourful toward the horizon, following simulation time

\[/LIST]

\[HEADING=3]Planets\[/HEADING]

\[LIST]

\[\*]Brightness from albedo, phase and distance, on the same magnitude scale as the stars

\[\*]Saturn's rings add to its brightness

\[\*]Planets and moons dim in their parent's shadow

\[\*]Twinkling reduced by apparent size

\[/LIST]

\[HEADING=3]The Sun\[/HEADING]

\[LIST]

\[\*]Drawn as a star at every distance, at its real angular size, blending into the 3D sphere up close

\[\*]Sphere at its real size and photosphere colour, with limb darkening

\[\*]Dims as it fills more of the screen

\[\*]Replaces the stock sun sprite and lens flare

\[/LIST]

\[HEADING=3]Occlusion and starbursts\[/HEADING]

\[LIST]

\[\*]Stars, planets and the Sun fade behind planets, atmospheres and vessels

\[\*]Starbursts modelled on the human eye, on the Sun and on planets brighter than magnitude −3

\[\*]Starbursts fade as their source is covered or leaves the screen

\[\*]The Sun's starburst is drawn over everything else

\[\*]Starbursts follow the game's lens flare setting

\[/LIST]

\[HEADING=2]Screenshots\[/HEADING]

\[SPOILER="Show images"]

\[ATTACH type="full" alt="earth-and-orion.jpg"]2021\[/ATTACH]

\[ATTACH type="full" alt="moon-surface.jpg"]2022\[/ATTACH]

\[ATTACH type="full" alt="orbit-sunset.jpg"]2023\[/ATTACH]

\[ATTACH type="full" alt="stars-flickering.gif"]2024\[/ATTACH]

\[ATTACH type="full" alt="zoom-out.gif"]2025\[/ATTACH]

\[/SPOILER]

\[HEADING=2]Installation\[/HEADING]

Requires \[URL='https://github.com/StarMapLoader/StarMap']StarMap\[/URL]. Each release is named with the KSA build it was tested on.

\[B]With Borea:\[/B] install Real Stars from the mod list.

\[B]Manually:\[/B]

\[LIST=1]

\[\*]Install StarMap and run the game once through \[ICODE]StarMap.Loader.exe\[/ICODE].

\[\*]Extract the release into \[ICODE]Documents\\My Games\\Kitten Space Agency\\mods\[/ICODE], giving \[ICODE]mods\\RealStars\\RealStars.dll\[/ICODE].

\[\*]Launch the game once and close it. KSA adds new mods to \[ICODE]manifest.toml\[/ICODE] disabled: set this mod's entry to \[ICODE]enabled = true\[/ICODE].

\[\*]Launch through StarMap.

\[/LIST]

To uninstall, delete \[ICODE]mods\\RealStars\[/ICODE].

\[HEADING=2]Building\[/HEADING]

\[ICODE]deploy.ps1\[/ICODE] builds the mod and installs it into your mods folder. \[ICODE]package.ps1 -Version x.y.z -GameBuild vYYYY.M.D.NNNN\[/ICODE] builds a release archive in \[ICODE]dist\[/ICODE]. Set \[ICODE]StarMapDir\[/ICODE] to your StarMap folder. \[ICODE]make\_star\_binary.py\[/ICODE] rebuilds the star catalogue from the Hipparcos main catalogue (\[ICODE]hip\_main.dat\[/ICODE]).

\[HEADING=2]Credits\[/HEADING]

\[LIST]

\[\*]Star catalogue built from the Hipparcos Catalogue (ESA 1997, ESA SP-1200), retrieved from \[URL='https://vizier.cds.unistra.fr/']VizieR\[/URL] (CDS, Strasbourg), catalogue I/239.

\[/LIST]

The code is MIT. \[URL='https://github.com/Brimfyr/RealStars/blob/main/CREDITS.md']CREDITS.md\[/URL] gives the source and terms of the shipped catalogue.

