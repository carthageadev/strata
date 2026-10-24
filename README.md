# Strata

![Strata](.github/banner.gif)

[![Download .unitypackage](https://img.shields.io/badge/Download-.unitypackage-2ea44f?style=for-the-badge)](https://github.com/carthageadev/strata/releases/latest/download/Strata.unitypackage)

A terrain painter for HD-2D maps in Unity.

Paint levels, ground, walls, edges and stairs in the Scene view, then bake to a plain prefab. The tool does not need terrain dependencies to run.

## Setup

Unity 6 with URP. Copy this folder into `Assets/Strata` or clone this repo straight in your `Assets` folder

Or in the Package Manager, `+ > Install package from git URL` and paste:

```
https://github.com/carthageadev/strata.git
```

Strata checks for a new version once a session and asks before updating. Turn it off in `Preferences > Strata`.

## Use

- `Tools > Strata > New Terrain` to start
- `Tools > Strata > Terrain Editor` to paint
- `Tools > Strata > Materials` for tile sets
- Drop textures, materials or models on the Strata window to add them

Open `Examples/Strata Sandbox` to try it.
