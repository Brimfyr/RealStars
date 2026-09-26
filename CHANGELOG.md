# Changelog

## 1.1.0 - Unreleased

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
