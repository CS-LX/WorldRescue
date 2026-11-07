// Build-time only. Runtime uses the committed PNGs; no Node dependency in the mod.
// From the repository root: npm install --prefix .cache/icon-tools --no-audit --no-fund @resvg/resvg-js@2.6.2 remixicon@4.6.0 pngjs@7.0.0
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const dependencies = path.join(root, '.cache/icon-tools/node_modules');
const { Resvg } = require(path.join(dependencies, '@resvg/resvg-js'));
const { PNG } = require(path.join(dependencies, 'pngjs'));
const upstream = path.join(dependencies, 'remixicon');
const icons = [
  'System/shield-check-line', 'System/question-line', 'System/information-line',
  'System/zoom-in-line', 'System/zoom-out-line', 'System/eye-line', 'System/eye-off-line',
  'System/check-line', 'Arrows/arrow-left-s-line', 'Arrows/arrow-right-s-line',
  'Design/anticlockwise-line', 'Design/focus-3-line', 'Document/file-copy-2-line',
  'Others/box-3-line', 'Weather/sparkling-line', 'Finance/copper-coin-line'
];
const sources = path.join(root, 'Art/RemixIcon');
const textures = path.join(root, 'Mod/Assets/Textures/WorldRescue/Remix');
const notices = path.join(root, 'Mod/ThirdParty/RemixIcon');
for (const directory of [sources, textures, notices]) fs.mkdirSync(directory, {recursive:true});
for (const icon of icons) {
  const name = path.basename(icon);
  const svg = fs.readFileSync(path.join(upstream, 'icons', icon + '.svg'), 'utf8');
  fs.writeFileSync(path.join(sources, name + '.svg'), svg);
  const white = svg.replaceAll('currentColor', '#ffffff');
  const png = PNG.sync.read(new Resvg(white, {fitTo:{mode:'width', value:96}}).render().asPng());
  // White RGB even under zero alpha prevents dark fringes during linear sampling.
  // Alpha coverage is preserved. Runtime uses NonPremultiplied blending.
  for (let p = 0; p < png.data.length; p += 4) png.data[p] = png.data[p + 1] = png.data[p + 2] = 255;
  fs.writeFileSync(path.join(textures, name + '.png'), PNG.sync.write(png));
}
fs.copyFileSync(path.join(upstream, 'License'), path.join(notices, 'LICENSE.txt'));
fs.writeFileSync(path.join(notices, 'NOTICE.txt'),
  'Remix Icon v4.6.0 — Remix Design\nhttps://github.com/Remix-Design/RemixIcon/tree/v4.6.0\nLicensed under Apache License 2.0 (see LICENSE.txt).\nModifications by World Rescue: selected SVGs rasterized at 96x96 with white fill and transparent backgrounds. Original SVGs are retained in Art/RemixIcon in the source distribution.\n');
console.log('Built ' + icons.length + ' Remix Icon textures.');
