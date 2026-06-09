---
name: Speedee font CORS
description: McDonald's Speedee woff2 CDN URLs are blocked by CORS when loaded cross-origin. Use system font fallback stack.
---

The Speedee woff2 files hosted at `https://www.mcdonalds.com/etc.clientlibs/.../Speedee-Regular.woff2` and `Speedee-Bold.woff2` return `Access-Control-Allow-Origin: SAMEORIGIN`, which browsers reject as an invalid value for cross-origin font requests.

**Why:** McDonald's CDN intentionally blocks third-party font loading from their hosted assets.

**How to apply:** The `@font-face` declarations in `site.css` are kept so they work if the domain ever changes, but in practice the browser falls back to the declared stack: `'Speedee', -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, "Noto Sans", sans-serif`. If the Speedee font is required, it must be self-hosted by downloading the files and placing them in `wwwroot/fonts/`.
