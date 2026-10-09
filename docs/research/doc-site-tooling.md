# Documentation-site tooling for Broadside

Research date: 2026-10-09. Question: what should Broadside use instead of DocFX, starting from what Wiesław Sołtes uses in his newest repositories.

## 1. What Sołtes actually uses (verified from repo files)

His active repos split into three documentation patterns. Only the first is a deliberate move away from DocFX.

### Pattern A: Lunet (.NET static site generator) with `api.dotnet`

| Repo | Lunet version | API reference | Evidence |
|---|---|---|---|
| [TreeDataGrid](https://github.com/wieslawsoltes/TreeDataGrid) | 1.0.10 | yes, two csproj | `site/config.scriban`, `.config/dotnet-tools.json`, `.github/workflows/docs.yml` |
| [PanAndZoom](https://github.com/wieslawsoltes/PanAndZoom) | 1.0.10 | yes | `site/config.scriban` (commit 2026-03-09 "Add Lunet docs infrastructure") |
| [ProMarkdown](https://github.com/wieslawsoltes/ProMarkdown) | 1.0.10 | no (hand-written reference pages) | `site/config.scriban` |
| [XamlVisualEditor](https://github.com/wieslawsoltes/XamlVisualEditor) | 1.0.14 | no | `.config/dotnet-tools.json` |

TreeDataGrid's own reference page, [Lunet Docs Pipeline](https://github.com/wieslawsoltes/TreeDataGrid/blob/master/site/articles/reference/lunet-docs-pipeline.md), says it explicitly: "This repository migrated from DocFX to Lunet" (commit "docs: migrate content from DocFX tree to Lunet site", 2026-03-04), covering content, API docs ("DocFX metadata to Lunet `api.dotnet`"), scripts and workflows.

How API reference is produced (from `site/config.scriban`):

```
with api.dotnet
    title = "TreeDataGrid .NET API Reference"
    path = "/api"
    menu_name = "api"
    config = "Release"
    properties = { TargetFramework: "net8.0" }
    projects = [
        { name: "TreeDataGrid", path: "../src/Avalonia.Controls.TreeDataGrid/Avalonia.Controls.TreeDataGrid.csproj" },
        { name: "TreeDataGrid.Core", path: "../src/TreeDataGrid.Core/TreeDataGrid.Core.csproj" }
    ]
    external_apis = [ { assembly: "Avalonia", url: "https://api-docs.avaloniaui.net/docs" }, ... ]
end
```

Per the [Lunet api-dotnet docs](https://lunet.io/docs/plugins/api-dotnet), the module runs `dotnet build` on the listed csproj files, reads the compiler's XML doc output, and emits root/namespace/member pages under `/api`, with `xref:` links, external-API URL mapping for unresolved types, and Markdown overrides via `src/<project>/apidocs/*.md` files keyed by `uid:`. The generated site shows a 14-namespace index at [wieslawsoltes.github.io/TreeDataGrid/api/](https://wieslawsoltes.github.io/TreeDataGrid/api/).

Deployment (`docs.yml`, TreeDataGrid and ProMarkdown are identical): `actions/setup-dotnet@v4` (10.0.x + 8.0.x), `dotnet tool restore`, `bash ./check-docs.sh` (builds via `dotnet tool run lunet build` inside `site/`, then asserts expected routes exist, no raw `.md` links, MIT footer present), then `peaceiris/actions-gh-pages@v4` publishing `./site/.lunet/build/www` to `gh-pages`. No Node, no Python.

Caveats he documents: Lunet 1.0.10 had a Dart Sass platform-detection bug on macOS 15, so he commits a precompiled `template-main.css` and uses a "lite" bundle; and his sites do not enable Lunet's search (no `with search` block; no search box on the live site).

### Pattern B: still DocFX (older setups, not touched since)

[Dock](https://github.com/wieslawsoltes/Dock), [Xaml.Behaviors](https://github.com/wieslawsoltes/Xaml.Behaviors), [ProDataGrid](https://github.com/wieslawsoltes/ProDataGrid) keep a `docfx/` folder; Dock's `build-docs.sh` runs `dotnet build -c Release` then `dotnet docfx docfx/docfx.json` and publishes `./_site` with peaceiris. Dock's `docfx.json` was last changed 2026-01-17, before the Lunet migrations began.

### Pattern C: MkDocs Material, no API reference

[ProPDF](https://github.com/wieslawsoltes/ProPDF) (his PDF engine, docs configured 2026-09-26) uses `mkdocs.yml` with `theme: material`, light/dark palettes, built-in `search` plugin, pinned `mkdocs==1.6.1` / `mkdocs-material==9.7.7`. Its `documentation.yml` only builds with `mkdocs build --strict` and uploads an artifact; GitHub Pages there hosts the Uno WebAssembly app instead. No C# API pages at all.

Repos with `pages.yml` (RibbonSpace, ScreenKeyboardControl, PdfSpace) deploy WASM sample apps, not docs.

## 2. Comparison for a .NET library

| Tool | C# API reference | Build speed | Theme | Search | Versioning | Dark mode | Agent/repo burden |
|---|---|---|---|---|---|---|---|
| [DocFX 2.81](https://github.com/dotnet/docfx/releases) | native `metadata` from csproj/XML docs, best cref/xref fidelity | slow (Roslyn metadata + template) | dated default, hard to restyle | Lunr client-side | none built in | yes | low: single `dotnet tool`; but templating is opaque to agents |
| [Lunet 1.1.x](https://lunet.io) | native `api.dotnet` from csproj/XML docs, `apidocs/` overrides, `external_apis` | fast build; `dotnet build` dominates | Bootstrap 5 template, clean; small ecosystem | SQLite FTS5/WASM, opt-in, theme must supply UI | none built in | system/light/dark built in | low: one `dotnet tool`, Scriban config; small community, one maintainer |
| [Astro Starlight](https://starlight.astro.build) | none; needs xmldoc2md-style converter to MD/MDX | fast (Vite) | excellent | Pagefind built in | community `starlight-versions` plugin | yes | medium-high: Node toolchain, `package.json`, lockfile, Astro upgrades |
| Docusaurus | none; [XMLDoc2Markdown](https://packages.nuget.org/packages/XMLDoc2Markdown) has a `--platform docusaurus` preset | moderate (webpack/rspack) | good | Algolia or local plugin | native, best in class | yes | high: React/Node, largest dependency tree |
| VitePress | none; generic MD converter | fastest Node option | good | MiniSearch local | none built in | yes | medium: Node, but small |
| [MkDocs Material](https://squidfunk.github.io/mkdocs-material/) | none native; [MkDocsSharp.MDGen](https://www.nuget.org/packages/MkDocsSharp.MDGen) runs at build to emit MD | fast | excellent, widely recognized | built-in client search | `mike` plugin | yes | medium: Python + pip pins; two toolchains |
| [Fumadocs](https://fumadocs.dev/docs/versioning) | none; no C# tooling found, would need custom script | moderate (Next.js) | excellent | Orama built in | folder-based root versions | yes | highest: Next.js/React app |

Converters that bridge XML docs to Markdown for the Node/Python tools: [Nefarius.Tools.XMLDoc2Markdown](https://nuget.org/packages/Nefarius.Tools.XMLDoc2Markdown), [XMLDoc2Markdown](https://packages.nuget.org/packages/XMLDoc2Markdown), [DocGen](https://github.com/alexeyzimarev/docgen), [ApiMark](https://www.nuget.org/packages/DemaConsulting.ApiMark.Tool). All lose cref resolution and member-level navigation compared with DocFX/Lunet, and all add a second generator to maintain.

## 3. Recommendation: Lunet

Reasoning: Broadside is a pure .NET library whose main documentation asset is a large public API surface. Only DocFX and Lunet consume XML docs natively; of the two, Lunet is what Sołtes migrated to, builds faster, has a modern responsive template with dark mode, and keeps the repo single-toolchain, which matters for agent-driven work (one `dotnet tool restore`, no Node lockfile churn, config is a readable Scriban script). Risks to accept: single-maintainer project, small ecosystem, no versioning (acceptable pre-1.0; later, publish per-tag to `v1/` subfolders), and search must be enabled plus a small search box added to the theme. If the team later wants a polished marketing-grade site, MkDocs Material is the fallback Sołtes himself used for ProPDF.

### Setup sketch

```
.config/dotnet-tools.json        # { "lunet": { "version": "1.1.1", "commands": ["lunet"] } }
build-docs.sh / check-docs.sh / serve-docs.sh
site/
  config.scriban                 # extend "lunet-io/templates"; site_project_*; with api.dotnet; with search
  menu.yml                       # Home, Docs, API
  readme.md                      # landing page
  articles/{getting-started,guides,reference}/*.md + menu.yml per folder
  images/
  .lunet/css/site-overrides.css
src/Broadside/apidocs/*.md       # optional narrative merged into API pages by uid
```

`config.scriban` core:

```
extend "lunet-io/templates"
template_theme_default_mode = "system"
site_project_baseurl = "https://<org>.github.io"
site_project_basepath = environment == "dev" ? "" : "/Broadside"
site_project_name = "Broadside"
site_project_github_user = "<org>"
site_project_github_repo = "Broadside"
site_project_init

with search
    enable = true
end

with api.dotnet
    title = "Broadside API Reference"
    path = "/api"
    menu_name = "api"
    config = "Release"
    properties = { TargetFramework: "net8.0" }
    projects = [ { name: "Broadside", path: "../src/Broadside/Broadside.csproj" } ]
end
```

API docs require `<GenerateDocumentationFile>true</GenerateDocumentationFile>` in `Directory.Build.props`; Lunet builds the project and reads the XML.

GitHub Pages workflow (`.github/workflows/docs.yml`), modelled on TreeDataGrid's:

```yaml
on: { push: { branches: [main] }, workflow_dispatch: {} }
permissions: { contents: write }
jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet tool restore
      - run: bash ./check-docs.sh          # lunet build + route/link assertions
      - uses: peaceiris/actions-gh-pages@v4
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: ./site/.lunet/build/www
```

Run `check-docs.sh` on pull requests too, so a broken xref or missing page fails before deploy. Local preview: `./serve-docs.sh` (wraps `dotnet tool run lunet serve` in `site/`).
