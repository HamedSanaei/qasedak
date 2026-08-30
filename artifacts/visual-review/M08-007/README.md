# M08-007 visual review

- `landing-desktop.png`: 1440 × 1000 first viewport.
- `landing-desktop-full.png`: 1440 × 7200 board-height review.
- `landing-mobile.png`: exact 390 × 844 CSS-pixel viewport captured through Chrome's
  device-metrics protocol.
- `landing-mobile-full.png`: exact 390-pixel full-page composition review.
- `landing-mobile-menu.png`: opened reusable mobile menu at 390 pixels.
- `responsive-review.json`: exact CDP metrics at the target CSS viewport widths.
- `capture.mjs`: reproducible Chrome DevTools Protocol capture/overflow check; it expects
  the production app on port 3310 and a scoped Chrome debugging session on port 9227.

The screenshots are local Next.js render evidence. A fresh live Penpot export was also
read during implementation and is documented in the sync record; it is not stored here as
a local file by the MCP transport.
