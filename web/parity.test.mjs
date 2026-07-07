/*
 * JS↔C# parity test (Node, zero dependencies).
 *
 * Proves the pure-JavaScript core in voidrunner-core.js reproduces the C# simulation BIT-FOR-BIT:
 *   1. It loads the SAME shipped content packs and asserts the content fingerprint matches C#.
 *   2. For a set of seeds it runs the identical scripted bot and asserts every final state hash,
 *      score, room and tick equals the committed C#-generated golden vector (parity-vectors.json).
 *   3. It deserialises the committed .vrplay recorded by the C# `vrverify` tool and re-verifies it
 *      in JS — the strongest possible proof the two engines agree.
 *
 * Run:  node web/parity.test.mjs
 * CI runs this so a change that breaks cross-language determinism turns the build red.
 */
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..');

// Load the core (attaches globalThis.VoidRunner).
const coreSrc = fs.readFileSync(path.join(here, 'voidrunner-core.js'), 'utf8');
(0, eval)(coreSrc);
const VR = globalThis.VoidRunner;

let failures = 0;
const fail = (msg) => { console.error('  ✗ ' + msg); failures++; };
const ok = (msg) => console.log('  ✓ ' + msg);

// The content JSON files tolerate // and /* */ comments (like the C# loader); strip them for JSON.parse.
function stripComments(s) {
  return s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}
function loadJson(rel) {
  return JSON.parse(stripComments(fs.readFileSync(path.join(repo, rel), 'utf8')));
}

// Load shipped packs in dependency order: base first, then the example mod on top.
const files = [
  { source: 'base/enemies.json', data: loadJson('Assets/StreamingAssets/ContentPacks/base/enemies.json') },
  { source: 'base/weapons.json', data: loadJson('Assets/StreamingAssets/ContentPacks/base/weapons.json') },
  { source: 'base/rooms.json', data: loadJson('Assets/StreamingAssets/ContentPacks/base/rooms.json') },
  { source: 'example-mod/content.json', data: loadJson('Assets/StreamingAssets/ContentPacks/example-mod/content.json') },
];
const { registry, ok: loadedOk, errors } = VR.loadContent(files);
if (!loadedOk) { console.error('content failed to load:', errors); process.exit(1); }

const golden = JSON.parse(fs.readFileSync(path.join(here, 'parity-vectors.json'), 'utf8'));

console.log('Content fingerprint parity:');
const jsFp = VR.contentFingerprint(registry).toString();
if (jsFp === golden.fingerprint) ok(`fingerprint ${jsFp} matches C#`);
else fail(`fingerprint mismatch: js=${jsFp} c#=${golden.fingerprint}`);

console.log('\nSimulation hash parity (per seed):');
for (const run of golden.runs) {
  const sim = new VR.Simulation(registry, BigInt(run.seed));
  for (let t = 0; t < 3000; t++) { sim.step(VR.scriptedInput(t)); if (sim.runOver) break; }
  const h = sim.stateHash().toString();
  if (h === run.hash && sim.score === run.score && sim.roomNumber === run.room && sim.tick === run.tick) {
    ok(`seed ${run.seed}: hash+score+room+tick match C#`);
  } else {
    fail(`seed ${run.seed}: js{hash=${h},score=${sim.score},room=${sim.roomNumber},tick=${sim.tick}} != c#{hash=${run.hash},score=${run.score},room=${run.room},tick=${run.tick}}`);
  }
}

console.log('\nCross-language replay verification:');
try {
  const replayText = fs.readFileSync(path.join(repo, 'docs/samples/cosmic-drift.vrplay'), 'utf8');
  const replay = VR.deserializeReplay(replayText);
  const res = VR.verifyReplay(replay, registry, VR.contentFingerprint(registry));
  if (res.reproduced) ok(`C#-recorded cosmic-drift.vrplay verified in JS (score ${res.replayedScore}, room ${res.replayedRoom})`);
  else fail('sample replay did not reproduce in JS: ' + res.message);
} catch (e) {
  fail('replay verify threw: ' + e.message);
}

console.log('');
if (failures === 0) { console.log('ALL PARITY CHECKS PASSED — the JS core is bit-identical to the C# core.'); process.exit(0); }
console.error(`${failures} parity check(s) FAILED.`);
process.exit(1);
