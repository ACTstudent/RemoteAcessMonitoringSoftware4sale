# CAMS public portal

Dependency-free static product, download, deployment, and trust portal for GitHub Pages.

The page follows the system light or dark preference. Its brand colours and the self-hosted Plus Jakarta Sans font come from the CAMS server UI, and the screenshots in `assets/img/` are WebP crops of `DIAGRAMS/prototype-images/`. Icons are an inline sprite of [Phosphor Icons](https://phosphoricons.com) (MIT). Nothing is loaded from another origin.

## Local preview

From the repository root, serve `portal/` with any static HTTP server, for example:

```powershell
npx --yes serve portal
```

Opening `portal/index.html` directly also preserves all core content and download links. HTTP preview is recommended for testing the optional `version.json` enhancement.

## Release updates

Update the visible fallback version in `index.html` and `portal/version.json` together. Installer links use GitHub's stable `releases/latest/download/` routes.

The deployment-specific `CAMS-Server-Root.cer` must never be added here. Administrators obtain it only from their authenticated local CAMS server at `/Admin/Deployment`.
