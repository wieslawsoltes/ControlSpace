# Third-party notices

ControlSpace application code and original graphics are distributed under the MIT license in LICENSE.

The .NET source references the following external projects. Their packages are restored by the build and retain their own notices and licenses; dependency binaries have not been built or bundled in this source delivery.

| Component | Selected version | Upstream license / purpose |
|---|---|---|
| Uno Platform / Uno.Sdk | SDK 6.7.30 | Apache-2.0; cross-platform WinUI-compatible hosting |
| Uno.WinUI.Graphics2DSK | 6.7.135 | Uno Platform licensing; host-backed Skia canvas integration |
| SkiaSharp | 3.119.2 | MIT for bindings; underlying Skia and native dependencies retain their own notices |
| .NET | 10 | .NET runtime/SDK distribution licenses and notices |
| Playwright | test tool | Apache-2.0; browser automation only, not shipped as application code |

Official upstream license files and the exact restored transitive dependency graph govern redistribution. Release maintainers should collect notices from the built package graph before distributing binaries. The source archive contains no external font files, Siemens screenshots, Siemens logos, firmware, device catalogs or proprietary project database assets.

Siemens and TIA Portal names identify the requested workflow reference. ControlSpace is not affiliated with, endorsed by or supplied by Siemens. Generic device illustrations and the conveyor HMI are original drawings, not manufacturer device representations.

Host entry-point and project-layout conventions were checked against Uno documentation and the user's existing DesignSpace repository. No proprietary implementation was used.
