// One-off icon generator, not part of the build. Rasterises the shield mark already used in the
// app shell and login page into the PNG sizes a PWA manifest needs, so the installed icon is the
// same authored mark rather than a generic placeholder.
import sharp from "sharp";
import { mkdirSync } from "node:fs";

const VOID = "#07090c";
const SIGNAL = "#38bdf8";

/** The shield mark, scaled into a 512x512 icon canvas with a void-coloured background. */
function shieldSvg({ size, padding }) {
  const inner = size - padding * 2;
  // Original mark is drawn in a 20x20 viewBox; scale it to fill the padded inner square.
  const scale = inner / 20;

  return `
<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">
  <rect width="${size}" height="${size}" fill="${VOID}" />
  <g transform="translate(${padding} ${padding}) scale(${scale})">
    <path
      d="M10 1.5 3 4.2v5.4c0 4 2.9 7.4 7 8.9 4.1-1.5 7-4.9 7-8.9V4.2L10 1.5Z"
      fill="${SIGNAL}" fill-opacity="0.16" stroke="${SIGNAL}" stroke-width="1.3"
    />
    <path
      d="M6.4 10.2h2l1.2-2.6 1.5 4.2 1.1-1.6h1.4"
      fill="none" stroke="${SIGNAL}" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"
    />
  </g>
</svg>`;
}

mkdirSync("public/icons", { recursive: true });

const targets = [
  { file: "icon-192.png", size: 192, padding: 192 * 0.14 },
  { file: "icon-512.png", size: 512, padding: 512 * 0.14 },
  // Maskable: Android crops to a circle/rounded-square, so content must sit inside a larger
  // "safe zone" (roughly the central 80%) or the shield gets clipped at the edges.
  { file: "icon-maskable-512.png", size: 512, padding: 512 * 0.24 },
];

for (const target of targets) {
  const svg = shieldSvg(target);
  await sharp(Buffer.from(svg)).png().toFile(`public/icons/${target.file}`);
  console.log(`wrote public/icons/${target.file}`);
}
