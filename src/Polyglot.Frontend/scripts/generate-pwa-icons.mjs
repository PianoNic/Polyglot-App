// One-off generator: renders public/icon.svg to the PWA icon sizes.
// Run with: bun scripts/generate-pwa-icons.mjs
import { Resvg } from '@resvg/resvg-js';
import { readFileSync, writeFileSync } from 'node:fs';

const svg = readFileSync(new URL('../public/icon.svg', import.meta.url));
const sizes = [72, 96, 128, 144, 152, 192, 384, 512];

for (const size of sizes) {
  const resvg = new Resvg(svg, { fitTo: { mode: 'width', value: size } });
  const png = resvg.render().asPng();
  writeFileSync(new URL(`../public/icons/icon-${size}x${size}.png`, import.meta.url), png);
  console.log(`icon-${size}x${size}.png`);
}
