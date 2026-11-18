# PZA's WASD & Controller Support for Meitou: build

Author: PZA. Licence: GNU GPL version 3 or later (`LICENSE`).

This is the source of PZA's WASD & Controller Support for Meitou 0.9.18 (beta, commit `6cbbd34` of the development repository). Each release also carries a `-source.zip` with everything the DLL is built from, the shared code included.

You need:
- the .NET SDK (10 or newer)
- an unpacked Meitou release (0.20.1 or 0.21.x, win-x64)
- PZA's Meitou UI 0.3.1 beside this folder (`..\PZAMeitouUi\src`; its code is compiled in), also in the source zip

Build (PowerShell):

```
.\mod\build.ps1 -MeitouDir <the unpacked Meitou folder>
```

It stages the install folder in `mod\out\`. (`-Check` runs the developer's offline checker, which is not part of this repository.)
