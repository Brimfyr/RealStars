# Changelog

## Unreleased

### Added

- Three switches on the Real Stars page of the game's Mods menu (with ModMenu): colour adaptation, twinkle, and the starburst and glare. Each is on by default. With colour adaptation off, every light is shown in its raw colour. The starburst and glare also still follow the game's Lens Flare setting.

### Changed

- The Real Stars page in the Mods menu has no headings that look like items but cannot be clicked. Its sections are parted by dividers, as the game's own menus are, and the whole adaptation time is shown in the slider's read-out.
- A star seen from close by keeps its starburst's needles. A star wide enough to show a disc blurs its own starburst, and that blur grew with the disc without limit, so close to a star the needles went and the burnt-out part ran together into a blob. It now stops at what the Sun's is from Earth.
- The adaptation time setting is the time the quick part of the eye's adaptation takes, which is the change you see happen, and its default is 1.33 s. The eye adapts in two stages, and the setting was the time for the whole course, thirty times as long: a setting of 40 s looked over in a second or two. The default was 1.2 s for the whole course, all but instant. A value already in the settings file is carried over.
- The soft glow round a bright source fades out of sight instead of ending. It stopped at a soft edge, about 110 pixels from the Sun as seen from Saturn; it now thins away to nothing further out, and is unchanged close in.
- The glare burns out close to a bright source. It was held to a quarter of white however bright the source, which left a gray shelf round the Sun. It now runs to white there, about 10 pixels past the Sun's limb at the default lens flare intensity and 17 at the highest, and grows as you approach a star.
- The burnt-out part has an uneven outline. Close to its source the eye's glare has only a few dozen needles round the circle, where the starburst drew a thousand at every distance and they averaged out to an even glow. The glow is now cut into broad lobes near the source that split into finer ones further out, and fade into the needles.
- The Sun's starburst follows the shape of what is left of the Sun when its disc is large and something covers part of it: a planet across a close star, or a hull. It came from one point with a round glow, which lay partly over whatever was in front. A small disc, like the Sun's from Earth, still throws its starburst from one point.

### Removed

- Proper motion. From 1.1.0 the catalogue's stars moved on with the game's clock, each along its own measured motion. The game's own stars (Alpha Centauri, Barnard's Star, Tau Ceti) stand still, and their planets with them, so the background slid past them, and Barnard's Star, the fastest star in the sky, was the one without its motion. The sky is now that of the game's start date, 2025-11-30, and stays there.

### Fixed

- A star no longer flashes for one frame as the camera passes from one star's side to another's. The game lights the scene by the nearest star, and on the frame that changed, the mod's other stars could be drawn about the wrong one: zooming out from Proxima toward Alpha Centauri, about 6,500 AU out, Alpha Centauri B appeared on top of Proxima for a single frame, thousands of times brighter than Proxima is from there.
- A star keeps its glare at the edge of the screen. The part of a star that was off the screen counted as covered, so its starburst and glow shrank as the star crossed the edge, and the bare disc showed beside them. This was plain from Mercury's orbit inward, where the disc is wide. The glare is now the whole star's until the star has all but left the view, unless a planet or moon is in front of the part that is off the screen.
- A star seen from close by is treated as the sphere it is, anywhere in the view. Toward the edge of a wide view a sphere is drawn stretched, up to twice as long as wide, and the starburst, the glow and the colour of the disc were worked out for a round disc, so part of the star stood outside them, blown out to white with a hard edge. From low over a star, with its surface across half the screen, there was no glare at all once the star's centre was behind the camera. Both now follow the star's true outline, and the glare is as wide beside the limb of a star that fills the view as beside a small one.
- Stars are drawn in their own colours. Red and blue were exchanged for every star in the catalogue, in every release so far: cool stars such as Betelgeuse, Antares and Arcturus were drawn blue, and hot ones such as Rigel and Spica orange. The star lighting the scene had the same fault, hidden while its disc was blown out to white: a red dwarf seen from near its planets had a blue disc inside its orange glare.
- A star's disc and halo follow its true direction and distance within 0.2 AU of it. Closer than that the star was taken to be 0.2 AU away. The Sun never showed it, because its globe is what is drawn there, but every planet of a red dwarf is inside that distance: from Proxima b the star's halo was dimmed as if the star were never more than 14 degrees from the horizon, and the star grew no brighter on approach.
- Light sources follow the game's Tonemap Exposure setting. The levels at which a star's core, the Sun's disc and a glare burn out to white were fixed for the default exposure, 1.25, so at a lower one they turned gray instead: at 0.1 the Sun's starburst was a flat gray star, as large as at the default, round a gray disc. A source brighter than white now stays white until the exposure has really come down to it, and its glare draws in as the exposure falls. Nothing changes at the default.
- A star's glare goes out when a vessel covers the star. Only the Sun's and the planets' did: with Alpha Centauri behind a hull, seen from Proxima, its starburst went on radiating round the hull. The game's own stars, and the catalogue's brightest, now go out while a part stands in front of them.
- The last sliver of a large star keeps its glare. With a wide disc almost wholly behind a planet, the crescent still showing had no starburst at all, because the points the disc is sampled at lay too far apart to land on it.
- No black disc in the sky after a jump away from a planet. The game keeps a list of the bodies near the camera and does not empty it where there are none, so the last planet came along to a star with no planets, as far below the camera as it had been, and every star behind it was hidden, the star lighting the scene included. The list is now emptied there. This is a fault in the game, corrected here because this mod's own shaders read the list.
- The free camera is no longer thrown across the system when it closes on a star. The game holds a free camera that follows a star 5.5 radii off it, but measures that from the system's origin instead of from the star, so coming that close to any star but the first put the camera inside Sol. It is now held off the star it follows. This is a fault in the game, not in this mod, and is corrected here until the game does.
- A red dwarf's disc is the colour of its glare. The disc was drawn blown out to white, whatever the star's colour, inside a glare in the star's own orange, with a hard rim where the two met. The glare is the disc's own light, so they are one colour, and a red dwarf's surface is about 500 times fainter than the Sun's, like a setting Sun's, which looks orange rather than white. Where a star's disc or its glare burns out, the picture is now held to the same brightness in the star's own colour, so the disc runs into its glare without an edge. The Sun, whose colour is nearly white, looks as it did.
- A star's starburst no longer cuts out as you close on it. Its reach was limited to 180 pixels from the star's centre, so once the disc itself was nearly that wide there was no room left for it: the starburst and the bright margin round the star were squeezed out over the last tenth of the approach, and the bare globe left behind looked a size smaller. The limit is now measured from the star's edge.
- A star's disc is the same size as its globe where one takes over from the other. The disc was drawn 10% larger in a 60 degree view, and 27% larger in a 90 degree one, and the part of it standing outside the globe went in one step at the end of the handover.
- A star no longer vanishes when you come within 5.5 of its radii. The game stops drawing a star's globe there, from inside the mesh it is drawn on; the globe is now drawn from inside it as well.
- A star's globe, seen up close, has the star's own colour. It was drawn white for every star, and the eye's adaptation to the star's light then turned it faintly blue: Proxima and Barnard's Star had a white globe with a bluish limb. A red dwarf's globe is now pale yellow at the centre and orange toward the limb once it fills the view. From further off it still burns out to white, as the Sun does, with its colour left in its rim and glare.
- A distant vessel's glint flared as it came out from behind a planet's limb. A vessel nearer than the planet's centre was taken to be in front of the planet's air and kept its full light, which the air drawn over it was then expected to dim exactly. A vessel rising over the limb is nearer than the centre and behind the limb's air both, and where a planet's air is drawn thinner than its settings say (Real Atmospheres' Venus and Titan), what was left was several times too bright. Every distant source now carries the air along its line of sight, and no part of its glow is brightened by more than the source itself.

## 1.1.0 - 2026-09-26

### Added

- Proper motion: stars are placed for the game's date and move on as game time passes.
- A soft glow around bright sources, the eye's veiling glare, which gives the Sun a corona.
- The Sun's starburst comes from the part of the Sun still in view, past vessels, planets and the edge of the screen.

### Changed

- Star catalogue of 123,568 AT-HYG v3.2 stars to magnitude 9, with Gaia DR3 distances, replacing the 83,337 Hipparcos stars. The catalogue file is now CC BY-SA 4.0; the code stays MIT.
- Starbursts rebuilt from a simulation of the human eye (Ritschel et al., 2009): about a thousand fine needles with coloured fringes.
- One starburst model for every source: needles grow with brightness, and Jupiter, Sirius and the brightest stars now show small starbursts.
- The game's Threshold Bloom and Global Bloom no longer bury the starbursts or put grey halos around bright stars and planets.

### Fixed

- The Sun's starburst showed through clouds, even from Venus's surface, and stayed white at sunset. It now dims with the clouds and takes the colour of the sunlight reaching you.
- Stars and planets were dimmed by the atmosphere twice, by most of a magnitude at 10° up.
- Coloured stars and planets were drawn up to 0.6 magnitudes too faint.
- Planets' halos and starbursts were drawn too faint.
- Up close, the Sun's sprite showed around its 3D sphere as a bright ring. It now shrinks into the sphere as the sphere fades in.
- Switching the stars off in the settings also removed the Sun.

## 1.0.0 - 2026-09-24

### Added

- Star catalogue of 83,337 Hipparcos stars to magnitude 9, with 3D positions and colours from B−V.
- Star brightness from magnitude, with a realistic point spread function.
- Parallax.
- Twinkling through atmospheres, following simulation time.
- Planet brightness from albedo, phase, rings and eclipses.
- The Sun drawn as a star at every distance, blending into the 3D sphere up close.
- Sun sphere at its real size and photosphere colour, with limb darkening.
- Sun brightness reduced as it fills the screen, replacing the stock sun sprite and lens flare.
- Occlusion of stars, planets and the Sun by planets, atmospheres and vessels.
- Starbursts modelled on the human eye, on the Sun and planets brighter than magnitude −3.
