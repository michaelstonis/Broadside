---
title: "Rendering"
---

# Rendering

This guide is being written for v1 ([#268](https://github.com/michaelstonis/Broadside/issues/268)). Like the [reading guide](reading.md), its code will come from compiled samples that CI runs.

It will show each rendering backend with a runnable sample: the software rasterizer in `Broadside.Rendering`, SkiaSharp, CoreGraphics, Direct2D and Android Canvas. It will cover the render options and their defaults, tiles from a cached display list, cancellation and progress, layers, hit-testing, color management, the fonts and character-map packages, the guarantee that the software rasterizer draws the same bytes on every platform, and memory budgets.

Until then, [Getting started](getting-started.md#render-a-page) renders a page to PNG, and the [API reference](../api/readme.md) documents `SoftwareRasterizer`, `RenderOptions` and each backend.
