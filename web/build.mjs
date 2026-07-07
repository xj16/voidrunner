/*
 * Builds the standalone, dependency-free static demo bundle at web/dist/index.html.
 *
 * It inlines: (1) the JS core, (2) the shipped content packs (base + example-mod) as a JS array so
 * the demo runs from file:// with no fetch, and (3) the committed sample .vrplay so "watch verified
 * replay" works offline. The result is a single self-contained HTML file any static host can serve
 * and a portfolio can iframe. No build tools, no npm install.
 *
 * Run:  node web/build.mjs
 */
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..');
const read = (rel) => fs.readFileSync(path.join(repo, rel), 'utf8');

// Content JSON tolerates // and /* */ comments; strip them for a clean embedded JSON.parse.
function stripComments(s) {
  return s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}
function pack(rel, source) {
  return { source, data: JSON.parse(stripComments(read(rel))) };
}

const packs = [
  pack('Assets/StreamingAssets/ContentPacks/base/enemies.json', 'base/enemies.json'),
  pack('Assets/StreamingAssets/ContentPacks/base/weapons.json', 'base/weapons.json'),
  pack('Assets/StreamingAssets/ContentPacks/base/rooms.json', 'base/rooms.json'),
  pack('Assets/StreamingAssets/ContentPacks/example-mod/content.json', 'example-mod/content.json'),
];

const core = read('web/voidrunner-core.js');
const sampleReplay = read('docs/samples/cosmic-drift.vrplay');
let template = read('web/index.template.html');

const coreTag = `<script>\n${core}\n</script>`;
const packsJs = `const PACKS = ${JSON.stringify(packs)};\nwindow.SAMPLE_REPLAY = ${JSON.stringify(sampleReplay)};`;

template = template.replace('<!-- @@CORE@@ -->', coreTag);
template = template.replace('/* @@PACKS@@ */', packsJs);

const outDir = path.join(here, 'dist');
fs.mkdirSync(outDir, { recursive: true });
const outFile = path.join(outDir, 'index.html');
fs.writeFileSync(outFile, template);
// Also copy the raw replay next to it so a hosted build can fetch it too.
fs.writeFileSync(path.join(outDir, 'cosmic-drift.vrplay'), sampleReplay);

const kb = (fs.statSync(outFile).size / 1024).toFixed(1);
console.log(`Built ${path.relative(repo, outFile)} (${kb} KB, self-contained).`);
