/*
 * VoidRunner deterministic core — pure-JavaScript port.
 * ====================================================
 * This is a faithful, dependency-free re-implementation of the same engine-agnostic core that the
 * C# game and the `vrverify` tool run (Assets/Scripts/Sim). It exists so the game can run and be
 * *verified* in a browser with zero install — the highest-impact way to show a described game is
 * real: a portfolio visitor plays a seed, or watches a recorded run, right on the page.
 *
 * WHY IT REPRODUCES C# BIT-FOR-BIT
 * --------------------------------
 *  1. RNG: xoshiro256** seeded by SplitMix64 is implemented over BigInt masked to 64 bits, so it is
 *     exactly the C# `ulong` arithmetic — same stream, same seed → same numbers.
 *  2. Math: every floating-point value is forced to 32-bit with Math.fround(), mirroring C# `float`.
 *     The transcendentals are the SAME deterministic polynomials as DetMath.cs (no Math.sin/cos),
 *     so sin/cos/sqrt/atan2 agree to the bit with the C# core and the committed golden vectors.
 *  3. The simulation step order, constants and formulas match Simulation.cs line-for-line.
 *
 * The result: a replay recorded by the C# tool verifies here, and a run played here would verify in
 * the C# tool. The demo proves this live with a "seed → final hash" readout the C# CLI can confirm.
 */
(function (global) {
  'use strict';

  const f = Math.fround; // force float32 semantics on every op, matching C# `float`

  // --------------------------------------------------------------------------------------------
  // 64-bit arithmetic helpers (BigInt masked to 64 bits == C# ulong)
  // --------------------------------------------------------------------------------------------
  const MASK64 = (1n << 64n) - 1n;
  const u64 = (x) => x & MASK64;
  const rotl = (x, k) => u64((x << BigInt(k)) | (x >> BigInt(64 - k)));

  // --------------------------------------------------------------------------------------------
  // DeterministicRandom — xoshiro256** + SplitMix64 (mirror of DeterministicRandom.cs)
  // --------------------------------------------------------------------------------------------
  class DeterministicRandom {
    constructor(seed) {
      seed = u64(BigInt(seed));
      this.seed = seed;
      let sm = seed;
      const next = () => {
        sm = u64(sm + 0x9E3779B97F4A7C15n);
        let z = sm;
        z = u64((z ^ (z >> 30n)) * 0xBF58476D1CE4E5B9n);
        z = u64((z ^ (z >> 27n)) * 0x94D049BB133111EBn);
        return z ^ (z >> 31n);
      };
      this.s0 = next(); this.s1 = next(); this.s2 = next(); this.s3 = next();
      if ((this.s0 | this.s1 | this.s2 | this.s3) === 0n) this.s0 = 0x9E3779B97F4A7C15n;
    }

    static fromString(text) {
      if (!text) return new DeterministicRandom(0n);
      let hash = 1469598103934665603n;
      const prime = 1099511628211n;
      for (const ch of text) {
        hash = u64(hash ^ BigInt(ch.codePointAt(0)));
        hash = u64(hash * prime);
      }
      return new DeterministicRandom(hash);
    }

    nextULong() {
      const result = u64(rotl(u64(this.s1 * 5n), 7) * 9n);
      const t = u64(this.s1 << 17n);
      this.s2 ^= this.s0;
      this.s3 ^= this.s1;
      this.s1 ^= this.s2;
      this.s0 ^= this.s3;
      this.s2 ^= t;
      this.s3 = rotl(this.s3, 45);
      return result;
    }

    range(minIncl, maxExcl) {
      if (maxExcl <= minIncl) return minIncl;
      const span = BigInt(maxExcl - minIncl);
      const limit = MASK64 - (MASK64 % span);
      let sample;
      do { sample = this.nextULong(); } while (sample >= limit);
      return minIncl + Number(sample % span);
    }

    nextDouble() {
      return Number(this.nextULong() >> 11n) * (1.0 / 9007199254740992.0);
    }
    nextFloat() { return f(this.nextDouble()); }
    rangeFloat(min, max) { return f(min + f(this.nextFloat() * f(max - min))); }
    chance(p) { return this.nextFloat() < p; }

    weightedIndex(weights) {
      if (!weights || weights.length === 0) return -1;
      let total = 0;
      for (const w of weights) if (w > 0) total = f(total + w);
      if (total <= 0) return -1;
      let roll = f(this.nextFloat() * total);
      let cursor = 0;
      for (let i = 0; i < weights.length; i++) {
        if (weights[i] <= 0) continue;
        cursor = f(cursor + weights[i]);
        if (roll < cursor) return i;
      }
      for (let i = weights.length - 1; i >= 0; i--) if (weights[i] > 0) return i;
      return -1;
    }
  }

  // --------------------------------------------------------------------------------------------
  // DetMath — deterministic float32 transcendentals (mirror of DetMath.cs)
  // --------------------------------------------------------------------------------------------
  const PI = f(3.14159265358979323846);
  const TwoPI = f(6.28318530717958647692);
  const HalfPI = f(1.57079632679489661923);
  const Deg2Rad = f(0.01745329251994329577);
  const Rad2Deg = f(57.2957795130823208768);

  // IMPORTANT: C# stores every `Nf` literal as a 32-bit float, so all arithmetic constants below are
  // pre-rounded with f() to their float32 value. Multiplying/adding a float32 operand by a
  // float32-rounded constant, then f()-rounding, single-rounds exactly like C# `float OP floatLit` —
  // which is what keeps this port bit-identical with the C# core (double-rounding an un-rounded
  // float64 literal would drift by a ULP and desync a shared replay).
  const C_HALF = f(0.5), C_1_5 = f(1.5), C_INV_TWOPI = f(1 / TwoPI);
  const SIN = [f(-2.3889859e-08), f(2.7525562e-06), f(-1.9840874e-04), f(8.3333310e-03), f(-1.6666667e-01)];
  const ATN = [f(0.0208351), f(-0.0851330), f(0.1801410), f(-0.3302995), f(0.9998660)];

  function detSqrt(x) {
    x = f(x);
    if (x < 0) return NaN;
    if (x === 0) return x;
    if (x === Infinity) return x;
    if (Number.isNaN(x)) return NaN;
    // fast inverse-sqrt bit trick
    const buf = new ArrayBuffer(4);
    const fv = new Float32Array(buf), iv = new Int32Array(buf);
    fv[0] = x;
    let i = iv[0];
    i = (0x5f3759df - (i >> 1)) | 0;
    iv[0] = i;
    let y = fv[0];
    const half = f(x * C_HALF);
    y = f(y * f(C_1_5 - f(half * f(y * y))));
    y = f(y * f(C_1_5 - f(half * f(y * y))));
    y = f(y * f(C_1_5 - f(half * f(y * y))));
    let r = f(x * y);
    if (r !== 0) r = f(C_HALF * f(r + f(x / r)));
    return r;
  }

  function reduceAngle(radians) {
    radians = f(radians);
    if (radians >= -PI && radians <= PI) return radians;
    const k = f(radians * C_INV_TWOPI);
    const rounded = k >= 0 ? f(Math.trunc(f(k + C_HALF))) : f(Math.trunc(f(k - C_HALF)));
    let x = f(radians - f(rounded * TwoPI));
    if (x > PI) x = f(x - TwoPI);
    else if (x < -PI) x = f(x + TwoPI);
    return x;
  }

  function detSin(radians) {
    let x = reduceAngle(f(radians));
    if (x > HalfPI) x = f(PI - x);
    else if (x < -HalfPI) x = f(-PI - x);
    const x2 = f(x * x);
    let p = SIN[0];
    p = f(f(p * x2) + SIN[1]);
    p = f(f(p * x2) + SIN[2]);
    p = f(f(p * x2) + SIN[3]);
    p = f(f(p * x2) + SIN[4]);
    p = f(p * x2);
    return f(x + f(x * p));
  }
  function detCos(radians) { return detSin(f(f(radians) + HalfPI)); }

  function atanUnit(z) {
    const z2 = f(z * z);
    let p = ATN[0];
    p = f(f(p * z2) + ATN[1]);
    p = f(f(p * z2) + ATN[2]);
    p = f(f(p * z2) + ATN[3]);
    p = f(f(p * z2) + ATN[4]);
    return f(p * z);
  }
  function detAtan2(y, x) {
    y = f(y); x = f(x);
    if (Number.isNaN(y) || Number.isNaN(x)) return NaN;
    if (x === 0) { if (y > 0) return HalfPI; if (y < 0) return -HalfPI; return 0; }
    const ax = x < 0 ? f(-x) : x;
    const ay = y < 0 ? f(-y) : y;
    let a, b;
    const swap = ay > ax;
    if (swap) { a = ax; b = ay; } else { a = ay; b = ax; }
    const z = f(a / b);
    let atan = atanUnit(z);
    if (swap) atan = f(HalfPI - atan);
    if (x < 0) atan = f(PI - atan);
    if (y < 0) atan = f(-atan);
    return atan;
  }

  const DetMath = {
    PI, TwoPI, HalfPI, Deg2Rad, Rad2Deg,
    sqrt: detSqrt, sin: detSin, cos: detCos, atan2: detAtan2,
    dirFromDegrees(deg) { const r = f(deg * Deg2Rad); return { x: detCos(r), y: detSin(r) }; },
    degreesOf(x, y) { return f(detAtan2(y, x) * Rad2Deg); },
  };

  // --------------------------------------------------------------------------------------------
  // Vec2 helpers (float32) — free functions to avoid per-op allocation churn
  // --------------------------------------------------------------------------------------------
  const clampf = (v, lo, hi) => (v < lo ? lo : (v > hi ? hi : v));
  const sqrMag = (x, y) => f(f(x * x) + f(y * y));
  const mag = (x, y) => detSqrt(sqrMag(x, y));

  // --------------------------------------------------------------------------------------------
  // Content: registry + a loader-lite that accepts the same JSON shape as the packs.
  // --------------------------------------------------------------------------------------------
  const KNOWN_BEHAVIOURS = { chase: 0, kite: 1, wander: 2, charger: 3 };

  function parseBehaviour(b) {
    switch ((b || '').toLowerCase()) {
      case 'kite': return 1; case 'wander': return 2; case 'charger': return 3; default: return 0;
    }
  }

  class ContentRegistry {
    constructor() {
      this.enemies = new Map(); this.weapons = new Map();
      this.rooms = new Map(); this.waves = new Map();
      this.enemyOrder = []; this.weaponOrder = []; this.roomOrder = [];
    }
    addEnemy(d) { if (!this.enemies.has(d.id)) this.enemyOrder.push(d.id); this.enemies.set(d.id, d); }
    addWeapon(d) { if (!this.weapons.has(d.id)) this.weaponOrder.push(d.id); this.weapons.set(d.id, d); }
    addRoom(d) { if (!this.rooms.has(d.id)) this.roomOrder.push(d.id); this.rooms.set(d.id, d); }
    addWave(d) { this.waves.set(d.id, d); }
  }

  const NUM_MAX = 1_000_000;
  function checkNum(v) { return Number.isFinite(v) && v <= NUM_MAX && v >= -NUM_MAX; }

  // Loads an array of parsed JSON objects (each the ContentFile shape) in order, later overrides
  // earlier — the same merge/validate contract as ContentLoader.cs (a compact subset).
  function loadContent(files) {
    const reg = new ContentRegistry();
    const errors = [], warnings = [];
    const gf = (o, k, d) => (typeof o[k] === 'number' ? f(o[k]) : d);
    const gi = (o, k, d) => (typeof o[k] === 'number' ? Math.round(o[k]) : d);
    const gs = (o, k, d) => (typeof o[k] === 'string' ? o[k] : d);

    for (const { source, data } of files) {
      if (!data || typeof data !== 'object') { errors.push(`[${source}] top-level must be an object`); continue; }

      for (const e of (data.enemies || [])) {
        if (!e || !e.id) { errors.push(`[${source}] an enemy is missing 'id'`); continue; }
        const def = {
          id: e.id, displayName: gs(e, 'displayName', e.id), sprite: gs(e, 'sprite', 'circle'),
          tint: gs(e, 'tint', '#FFFFFF'), maxHealth: gf(e, 'maxHealth', f(10)), moveSpeed: gf(e, 'moveSpeed', f(2)),
          contactDamage: gf(e, 'contactDamage', f(1)), radius: gf(e, 'radius', f(0.4)),
          behaviour: gs(e, 'behaviour', 'chase'), scoreValue: gi(e, 'scoreValue', 10), dropChance: gf(e, 'dropChance', f(0.1)),
        };
        if (![def.maxHealth, def.moveSpeed, def.contactDamage, def.radius, def.dropChance].every(checkNum)) {
          errors.push(`[${source}] enemy '${e.id}' has a non-finite / out-of-range number`); continue;
        }
        if (def.radius <= 0) def.radius = f(0.05);
        if (def.maxHealth <= 0) def.maxHealth = f(1);
        if (!(def.behaviour.toLowerCase() in KNOWN_BEHAVIOURS))
          warnings.push(`[${source}] enemy '${e.id}' unknown behaviour '${def.behaviour}', defaulting to chase`);
        reg.addEnemy(def);
      }

      for (const w of (data.weapons || [])) {
        if (!w || !w.id) { errors.push(`[${source}] a weapon is missing 'id'`); continue; }
        const def = {
          id: w.id, displayName: gs(w, 'displayName', w.id), sprite: gs(w, 'sprite', 'bolt'),
          tint: gs(w, 'tint', '#FFFFFF'), damage: gf(w, 'damage', f(3)), fireRate: gf(w, 'fireRate', f(3)),
          projectileSpeed: gf(w, 'projectileSpeed', f(12)), projectileLifetime: gf(w, 'projectileLifetime', f(1.2)),
          projectilesPerShot: gi(w, 'projectilesPerShot', 1), spreadDegrees: gf(w, 'spreadDegrees', f(0)),
          pierce: gi(w, 'pierce', 0), projectileRadius: gf(w, 'projectileRadius', f(0.15)), rarityWeight: gf(w, 'rarityWeight', f(1)),
        };
        if (![def.damage, def.fireRate, def.projectileSpeed, def.projectileLifetime, def.spreadDegrees, def.projectileRadius, def.rarityWeight].every(checkNum)) {
          errors.push(`[${source}] weapon '${w.id}' has a non-finite / out-of-range number`); continue;
        }
        if (def.projectilesPerShot > 256) def.projectilesPerShot = 256;
        if (def.fireRate <= 0) def.fireRate = f(0.1);
        if (def.projectilesPerShot < 1) def.projectilesPerShot = 1;
        reg.addWeapon(def);
      }

      for (const r of (data.rooms || [])) {
        if (!r || !r.id) { errors.push(`[${source}] a room is missing 'id'`); continue; }
        const def = {
          id: r.id, displayName: gs(r, 'displayName', r.id), width: gf(r, 'width', f(24)), height: gf(r, 'height', f(14)),
          backgroundTint: gs(r, 'backgroundTint', '#0B0E1A'), weight: gf(r, 'weight', f(1)),
          obstacles: [], waveIds: Array.isArray(r.waveIds) ? r.waveIds.filter(x => typeof x === 'string') : [],
        };
        if (![def.width, def.height, def.weight].every(checkNum)) { errors.push(`[${source}] room '${r.id}' bad number`); continue; }
        let obOk = true;
        for (const o of (r.obstacles || [])) {
          if (!o || typeof o !== 'object') continue;
          const ob = { x: gf(o, 'x', f(0)), y: gf(o, 'y', f(0)), width: gf(o, 'width', f(1)), height: gf(o, 'height', f(1)) };
          if (![ob.x, ob.y, ob.width, ob.height].every(checkNum)) { obOk = false; break; }
          if (ob.width < 0) ob.width = f(-ob.width);
          if (ob.height < 0) ob.height = f(-ob.height);
          def.obstacles.push(ob);
        }
        if (!obOk) { errors.push(`[${source}] room '${r.id}' has a bad obstacle number`); continue; }
        if (def.width < 8) def.width = f(8);
        if (def.height < 6) def.height = f(6);
        reg.addRoom(def);
      }

      for (const wv of (data.waves || [])) {
        if (!wv || !wv.id) { errors.push(`[${source}] a wave is missing 'id'`); continue; }
        const groups = [];
        for (const g of (wv.groups || [])) {
          if (!g || typeof g !== 'object') continue;
          groups.push({ enemyId: gs(g, 'enemyId', null), count: gi(g, 'count', 1), delay: gf(g, 'delay', f(0)) });
        }
        reg.addWave({ id: wv.id, groups });
      }
    }

    // Reference validation (rooms→waves→enemies).
    for (const id of reg.roomOrder) {
      const room = reg.rooms.get(id);
      for (const wid of room.waveIds) {
        if (!reg.waves.has(wid)) errors.push(`room '${room.id}' references unknown wave '${wid}'`);
        const wave = reg.waves.get(wid);
        if (wave) for (const g of wave.groups)
          if (!reg.enemies.has(g.enemyId)) errors.push(`wave '${wave.id}' references unknown enemy '${g.enemyId}'`);
      }
    }
    if (reg.enemies.size === 0) errors.push('no enemies defined');
    if (reg.weapons.size === 0) errors.push('no weapons defined');
    if (reg.rooms.size === 0) errors.push('no rooms defined');

    return { registry: reg, errors, warnings, ok: errors.length === 0 };
  }

  // FNV-style content fingerprint (mirror of ContentFingerprint.cs), computed as a BigInt ulong.
  function contentFingerprint(reg) {
    let h = 1469598103934665603n;
    const prime = 1099511628211n;
    const mixStr = (s) => {
      if (s == null) { h = u64((h ^ 0xFFn) * prime); return; }
      for (const c of s) h = u64((h ^ BigInt(c.codePointAt(0))) * prime);
      h = u64((h ^ 0x1Dn) * prime);
    };
    const fbuf = new ArrayBuffer(4), fv = new Float32Array(fbuf), iv = new Int32Array(fbuf);
    const mixF = (x) => { fv[0] = f(x); h = u64((h ^ (BigInt(iv[0] >>> 0))) * prime); };
    const mixI = (i) => { h = u64((h ^ BigInt((i | 0) >>> 0)) * prime); };

    for (const id of reg.enemyOrder) {
      const e = reg.enemies.get(id);
      mixStr(e.id); mixF(e.maxHealth); mixF(e.moveSpeed); mixF(e.contactDamage);
      mixF(e.radius); mixI(e.scoreValue); mixF(e.dropChance); mixStr((e.behaviour || '').toLowerCase());
    }
    for (const id of reg.weaponOrder) {
      const w = reg.weapons.get(id);
      mixStr(w.id); mixF(w.damage); mixF(w.fireRate); mixF(w.projectileSpeed);
      mixF(w.projectileLifetime); mixI(w.projectilesPerShot); mixF(w.spreadDegrees);
      mixI(w.pierce); mixF(w.projectileRadius); mixF(w.rarityWeight);
    }
    for (const id of reg.roomOrder) {
      const r = reg.rooms.get(id);
      mixStr(r.id); mixF(r.width); mixF(r.height); mixF(r.weight);
      mixI(r.obstacles.length);
      for (const o of r.obstacles) { mixF(o.x); mixF(o.y); mixF(o.width); mixF(o.height); }
      for (const wid of r.waveIds) mixStr(wid);
    }
    return h;
  }

  // --------------------------------------------------------------------------------------------
  // Simulation (mirror of Simulation.cs). float32 throughout for bit-parity.
  // --------------------------------------------------------------------------------------------
  const FixedDeltaTime = f(1 / 60);
  const MaxEnemies = 512, MaxProjectiles = 1024, MaxPickups = 64;

  // Float32-rounded gameplay constants (see the note by the DetMath constants): any fractional
  // literal that isn't exactly representable in float32 is pre-rounded here so the JS arithmetic
  // single-rounds exactly like the C# `float` sim.
  const K = {
    p8: f(0.8), p6: f(0.6), p4: f(0.4), p2: f(0.2), p12: f(0.12), p04: f(0.04),
    p45: f(0.45), p35: f(0.35), p05: f(0.05), n6_5: f(6.5), n1_6: f(1.6), n22_5: f(22.5),
    n2_5: f(2.5), n2_2: f(2.2), n1_5: f(1.5), n1_2: f(1.2), n1_1: f(1.1), n0_9: f(0.9),
    n0_55: f(0.55), n6: f(6),
  };

  class Simulation {
    constructor(registry, seed) {
      this.reg = registry;
      this.rng = new DeterministicRandom(seed);
      this.tick = 0; this.score = 0; this.roomNumber = 0; this.enemiesAlive = 0; this.runOver = false;
      this.currentRoom = null;
      this.enemyIndex = new Map(); this.weaponIndex = new Map();
      registry.enemyOrder.forEach((id, i) => this.enemyIndex.set(id, i));
      registry.weaponOrder.forEach((id, i) => this.weaponIndex.set(id, i));

      this.enemies = Array.from({ length: MaxEnemies }, () => ({ active: false }));
      this.projectiles = Array.from({ length: MaxProjectiles }, () => ({ active: false }));
      this.pickups = Array.from({ length: MaxPickups }, () => ({ active: false }));
      this.pending = [];
      this.roomClearGraceTicks = 0;

      this.player = {
        x: 0, y: 0, vx: 0, vy: 0, health: f(100), maxHealth: f(100), moveSpeed: K.n6_5,
        radius: K.p35, weaponId: null, fireCooldown: 0, invulnTimer: 0,
      };
      this._pickStartingWeapon();
      this._enterRoom(1);
    }

    get alive() { return this.player.health > 0; }

    _pickStartingWeapon() {
      let best = null, bestW = -Infinity;
      for (const id of this.reg.weaponOrder) {
        const w = this.reg.weapons.get(id);
        if (w.rarityWeight > bestW) { bestW = w.rarityWeight; best = w.id; }
      }
      this.player.weaponId = best || (this.reg.weaponOrder[0] || null);
    }

    _pickRoom() {
      const rooms = [], weights = [];
      for (const id of this.reg.roomOrder) { const r = this.reg.rooms.get(id); rooms.push(r); weights.push(r.weight); }
      if (rooms.length === 0) return null;
      let idx = this.rng.weightedIndex(weights);
      if (idx < 0) idx = 0;
      return rooms[idx];
    }

    _enterRoom(number) {
      this.roomNumber = number;
      this.player.x = 0; this.player.y = 0;
      this.currentRoom = this._pickRoom();
      this.pending = [];
      this.roomClearGraceTicks = 0;
      if (!this.currentRoom) return;
      this._resolveObstacles(this.player, this.player.radius);

      const waveRepeat = 1 + ((number - 1) / 2 | 0);
      for (let rep = 0; rep < waveRepeat; rep++) {
        for (const waveId of this.currentRoom.waveIds) {
          const wave = this.reg.waves.get(waveId);
          if (!wave) continue;
          for (const group of wave.groups) {
            const baseTick = this.tick + (f(group.delay * 60) | 0) + rep * 90;
            for (let c = 0; c < group.count; c++)
              this.pending.push({ enemyId: group.enemyId, atTick: baseTick + c * 6 });
          }
        }
      }
    }

    step(input) {
      if (this.runOver) return;
      const dt = FixedDeltaTime;
      this._processSpawns();
      this._updatePlayer(input, dt);
      this._updateEnemies(dt);
      this._updateProjectiles(dt);
      this._updatePickups(dt);
      this._resolveCombat();
      this._checkRoomProgress();
      if (!this.alive) this.runOver = true;
      this.tick++;
    }

    _processSpawns() {
      for (let i = this.pending.length - 1; i >= 0; i--) {
        if (this.pending[i].atTick <= this.tick) {
          this._spawnEnemy(this.pending[i].enemyId);
          this.pending.splice(i, 1);
        }
      }
    }

    _spawnEnemy(enemyId) {
      const def = this.reg.enemies.get(enemyId);
      if (!def || !this.currentRoom) return;
      let slot = -1;
      for (let i = 0; i < MaxEnemies; i++) if (!this.enemies[i].active) { slot = i; break; }
      if (slot < 0) return;

      const halfW = f(f(this.currentRoom.width * 0.5) - 1);
      const halfH = f(f(this.currentRoom.height * 0.5) - 1);
      const edge = this.rng.range(0, 4);
      const t = this.rng.nextFloat();
      let px, py;
      switch (edge) {
        case 0: px = f(-halfW + f(t * f(halfW * 2))); py = halfH; break;
        case 1: px = f(-halfW + f(t * f(halfW * 2))); py = f(-halfH); break;
        case 2: px = f(-halfW); py = f(-halfH + f(t * f(halfH * 2))); break;
        default: px = halfW; py = f(-halfH + f(t * f(halfH * 2))); break;
      }

      const diff = f(1 + f((this.roomNumber - 1) * K.p12));
      const speedDiff = f(1 + f((this.roomNumber - 1) * K.p04));
      const e = this.enemies[slot];
      e.active = true; e.x = px; e.y = py; e.vx = 0; e.vy = 0;
      e.maxHealth = f(def.maxHealth * diff); e.health = e.maxHealth;
      e.moveSpeed = f(def.moveSpeed * speedDiff); e.contactDamage = def.contactDamage;
      e.radius = def.radius; e.scoreValue = def.scoreValue; e.dropChance = def.dropChance;
      e.behaviour = parseBehaviour(def.behaviour);
      e.defIndex = this.enemyIndex.has(enemyId) ? this.enemyIndex.get(enemyId) : 0;
      e.aiTimer = this.rng.rangeFloat(0.5, K.n1_5);
      const h = DetMath.dirFromDegrees(this.rng.rangeFloat(0, 360));
      e.aiHx = h.x; e.aiHy = h.y;
      this.enemiesAlive++;
    }

    _updatePlayer(input, dt) {
      const mv = moveVector(input);
      this.player.vx = f(mv.x * this.player.moveSpeed);
      this.player.vy = f(mv.y * this.player.moveSpeed);
      this.player.x = f(this.player.x + f(this.player.vx * dt));
      this.player.y = f(this.player.y + f(this.player.vy * dt));
      this._clampToRoom(this.player, this.player.radius);
      this._resolveObstacles(this.player, this.player.radius);

      if (this.player.invulnTimer > 0) this.player.invulnTimer = f(this.player.invulnTimer - dt);
      if (this.player.fireCooldown > 0) this.player.fireCooldown = f(this.player.fireCooldown - dt);

      if (input.firing && this.player.fireCooldown <= 0) this._fire(aimVector(input));
    }

    _fire(aim) {
      const weapon = this.reg.weapons.get(this.player.weaponId);
      if (!weapon) return;
      this.player.fireCooldown = f(1 / weapon.fireRate);
      const count = Math.max(1, weapon.projectilesPerShot);
      const useAim = sqrMag(aim.x, aim.y) > 1e-8 ? aim : { x: 1, y: 0 };
      const baseAngle = DetMath.degreesOf(useAim.x, useAim.y);
      const spread = weapon.spreadDegrees;
      const wIdx = this.weaponIndex.has(weapon.id) ? this.weaponIndex.get(weapon.id) : 0;
      for (let i = 0; i < count; i++) {
        let angle;
        if (count === 1) angle = baseAngle;
        else { const frac = f(i / (count - 1)); angle = f(f(baseAngle - f(spread * 0.5)) + f(spread * frac)); }
        this._spawnProjectile(angle, weapon, wIdx);
      }
    }

    _spawnProjectile(angleDeg, weapon, defIndex) {
      let slot = -1;
      for (let i = 0; i < MaxProjectiles; i++) if (!this.projectiles[i].active) { slot = i; break; }
      if (slot < 0) return;
      const dir = DetMath.dirFromDegrees(angleDeg);
      const p = this.projectiles[slot];
      p.active = true;
      p.x = f(this.player.x + f(dir.x * f(this.player.radius + K.p2)));
      p.y = f(this.player.y + f(dir.y * f(this.player.radius + K.p2)));
      p.vx = f(dir.x * weapon.projectileSpeed);
      p.vy = f(dir.y * weapon.projectileSpeed);
      p.damage = weapon.damage; p.radius = weapon.projectileRadius;
      p.lifetime = weapon.projectileLifetime; p.pierce = weapon.pierce; p.defIndex = defIndex;
    }

    _updateEnemies(dt) {
      for (let i = 0; i < MaxEnemies; i++) {
        const e = this.enemies[i];
        if (!e.active) continue;
        const toX = f(this.player.x - e.x), toY = f(this.player.y - e.y);
        const dist = mag(toX, toY);
        const dirX = dist > 1e-4 ? f(toX * f(1 / dist)) : 0;
        const dirY = dist > 1e-4 ? f(toY * f(1 / dist)) : 0;

        switch (e.behaviour) {
          case 0: e.vx = f(dirX * e.moveSpeed); e.vy = f(dirY * e.moveSpeed); break;
          case 1: {
            const preferred = K.n6;
            const rX = dist < preferred ? f(-dirX) : dirX;
            const rY = dist < preferred ? f(-dirY) : dirY;
            const tX = f(-dirY), tY = dirX;
            let vx = f(f(rX * K.p6) + f(tX * K.p8)), vy = f(f(rY * K.p6) + f(tY * K.p8));
            const m = mag(vx, vy);
            if (m > 1e-6) { const inv = f(1 / m); vx = f(vx * inv); vy = f(vy * inv); } else { vx = 0; vy = 0; }
            e.vx = f(vx * e.moveSpeed); e.vy = f(vy * e.moveSpeed); break;
          }
          case 2:
            e.aiTimer = f(e.aiTimer - dt);
            if (e.aiTimer <= 0) { const h = DetMath.dirFromDegrees(this.rng.rangeFloat(0, 360)); e.aiHx = h.x; e.aiHy = h.y; e.aiTimer = this.rng.rangeFloat(1, K.n2_5); }
            e.vx = f(e.aiHx * f(e.moveSpeed * K.p6)); e.vy = f(e.aiHy * f(e.moveSpeed * K.p6)); break;
          case 3:
            e.aiTimer = f(e.aiTimer - dt);
            if (e.aiTimer <= 0) { e.aiHx = dirX; e.aiHy = dirY; e.aiTimer = this.rng.rangeFloat(K.n1_2, K.n2_2); }
            e.vx = f(e.aiHx * f(e.moveSpeed * K.n1_6)); e.vy = f(e.aiHy * f(e.moveSpeed * K.n1_6)); break;
        }

        const stepX = f(e.vx * dt), stepY = f(e.vy * dt);
        if (this._hitsObstacle(f(e.x + stepX), f(e.y + stepY), e.radius)) {
          const d = this._tryDeflect(e.x, e.y, e.vx, e.vy, e.radius, dt);
          e.vx = d.x; e.vy = d.y;
        }
        e.x = f(e.x + f(e.vx * dt)); e.y = f(e.y + f(e.vy * dt));
        this._clampToRoom(e, e.radius);
        this._resolveObstacles(e, e.radius);
      }
    }

    _tryDeflect(px, py, vx, vy, radius, dt) {
      const speed = mag(vx, vy);
      if (speed < 1e-5) return { x: vx, y: vy };
      for (let a = 1; a <= 6; a++) {
        const deg = f(a * K.n22_5);
        const l = rotate(vx, vy, deg);
        if (!this._hitsObstacle(f(px + f(l.x * dt)), f(py + f(l.y * dt)), radius)) return l;
        const r = rotate(vx, vy, f(-deg));
        if (!this._hitsObstacle(f(px + f(r.x * dt)), f(py + f(r.y * dt)), radius)) return r;
      }
      return { x: vx, y: vy };
    }

    _updateProjectiles(dt) {
      for (let i = 0; i < MaxProjectiles; i++) {
        const p = this.projectiles[i];
        if (!p.active) continue;
        p.x = f(p.x + f(p.vx * dt)); p.y = f(p.y + f(p.vy * dt));
        p.lifetime = f(p.lifetime - dt);
        if (p.lifetime <= 0 || this._outOfRoom(p.x, p.y, p.radius) || this._hitsObstacle(p.x, p.y, p.radius)) p.active = false;
      }
    }

    _updatePickups() {
      for (let i = 0; i < MaxPickups; i++) {
        const pk = this.pickups[i];
        if (!pk.active) continue;
        const sumR = f(pk.radius + this.player.radius);
        const dx = f(pk.x - this.player.x), dy = f(pk.y - this.player.y);
        if (sqrMag(dx, dy) <= f(sumR * sumR)) { this._applyPickup(pk); pk.active = false; }
      }
    }

    _applyPickup(pk) {
      if (pk.kind === 0) this.player.health = f(Math.min(this.player.maxHealth, f(this.player.health + pk.healAmount)));
      else if (pk.weaponDefIndex >= 0 && pk.weaponDefIndex < this.reg.weaponOrder.length) {
        this.player.weaponId = this.reg.weaponOrder[pk.weaponDefIndex]; this.player.fireCooldown = 0;
      }
    }

    _resolveCombat() {
      for (let pi = 0; pi < MaxProjectiles; pi++) {
        const p = this.projectiles[pi];
        if (!p.active) continue;
        for (let ei = 0; ei < MaxEnemies; ei++) {
          const e = this.enemies[ei];
          if (!e.active) continue;
          const sumR = f(p.radius + e.radius);
          const dx = f(p.x - e.x), dy = f(p.y - e.y);
          if (sqrMag(dx, dy) <= f(sumR * sumR)) {
            e.health = f(e.health - p.damage);
            if (e.health <= 0) this._killEnemy(e);
            if (p.pierce > 0) p.pierce--;
            else { p.active = false; break; }
          }
        }
      }
      if (this.player.invulnTimer <= 0) {
        for (let ei = 0; ei < MaxEnemies; ei++) {
          const e = this.enemies[ei];
          if (!e.active) continue;
          const sumR = f(e.radius + this.player.radius);
          const dx = f(e.x - this.player.x), dy = f(e.y - this.player.y);
          if (sqrMag(dx, dy) <= f(sumR * sumR)) {
            this.player.health = f(this.player.health - e.contactDamage);
            this.player.invulnTimer = f(0.6);
            let pushX = f(this.player.x - e.x), pushY = f(this.player.y - e.y);
            const m = mag(pushX, pushY);
            if (m > 1e-6) { const inv = f(1 / m); pushX = f(pushX * inv); pushY = f(pushY * inv); } else { pushX = 0; pushY = 0; }
            this.player.x = f(this.player.x + f(pushX * K.p8)); this.player.y = f(this.player.y + f(pushY * K.p8));
            this._clampToRoom(this.player, this.player.radius);
            break;
          }
        }
      }
    }

    _killEnemy(e) {
      e.active = false; this.enemiesAlive--; this.score += e.scoreValue;
      if (this.rng.chance(e.dropChance)) this._spawnDrop(e.x, e.y);
    }

    _spawnDrop(x, y) {
      let slot = -1;
      for (let i = 0; i < MaxPickups; i++) if (!this.pickups[i].active) { slot = i; break; }
      if (slot < 0) return;
      const pk = this.pickups[slot];
      pk.active = true; pk.x = x; pk.y = y; pk.radius = K.p45;
      if (this.rng.chance(K.p6) || this.reg.weaponOrder.length === 0) { pk.kind = 0; pk.healAmount = f(20); }
      else {
        pk.kind = 1;
        const weights = this.reg.weaponOrder.map(id => this.reg.weapons.get(id).rarityWeight);
        const idx = this.rng.weightedIndex(weights);
        pk.weaponDefIndex = idx < 0 ? 0 : idx;
      }
    }

    _checkRoomProgress() {
      if (this.enemiesAlive <= 0 && this.pending.length === 0 && this.currentRoom) {
        this.roomClearGraceTicks++;
        if (this.roomClearGraceTicks >= 45) this._advanceRoom();
      } else this.roomClearGraceTicks = 0;
    }

    _advanceRoom() {
      for (let i = 0; i < MaxProjectiles; i++) this.projectiles[i].active = false;
      for (let i = 0; i < MaxPickups; i++) this.pickups[i].active = false;
      this.player.health = f(Math.min(this.player.maxHealth, f(this.player.health + 10)));
      this._enterRoom(this.roomNumber + 1);
    }

    _clampToRoom(o, radius) {
      if (!this.currentRoom) return;
      const halfW = f(f(this.currentRoom.width * 0.5) - radius);
      const halfH = f(f(this.currentRoom.height * 0.5) - radius);
      o.x = clampf(o.x, f(-halfW), halfW);
      o.y = clampf(o.y, f(-halfH), halfH);
    }

    _outOfRoom(x, y, radius) {
      if (!this.currentRoom) return true;
      const halfW = f(f(this.currentRoom.width * 0.5) + radius);
      const halfH = f(f(this.currentRoom.height * 0.5) + radius);
      return x < -halfW || x > halfW || y < -halfH || y > halfH;
    }

    _resolveObstacles(o, radius) {
      if (!this.currentRoom) return;
      const obs = this.currentRoom.obstacles;
      for (let i = 0; i < obs.length; i++) {
        const ob = obs[i];
        const halfW = f(ob.width * 0.5), halfH = f(ob.height * 0.5);
        const cx = clampf(o.x, f(ob.x - halfW), f(ob.x + halfW));
        const cy = clampf(o.y, f(ob.y - halfH), f(ob.y + halfH));
        const dx = f(o.x - cx), dy = f(o.y - cy);
        const distSq = sqrMag(dx, dy);
        if (distSq > f(radius * radius)) continue;
        if (distSq > 1e-12) {
          const dist = detSqrt(distSq);
          const push = f(radius - dist);
          const inv = f(1 / dist);
          o.x = f(o.x + f(f(dx * inv) * push));
          o.y = f(o.y + f(f(dy * inv) * push));
        } else {
          const penX = f(f(halfW + radius) - (f(o.x - ob.x) < 0 ? f(ob.x - o.x) : f(o.x - ob.x)));
          const penY = f(f(halfH + radius) - (f(o.y - ob.y) < 0 ? f(ob.y - o.y) : f(o.y - ob.y)));
          if (penX < penY) o.x = f(o.x + (o.x < ob.x ? f(-penX) : penX));
          else o.y = f(o.y + (o.y < ob.y ? f(-penY) : penY));
        }
      }
    }

    _hitsObstacle(x, y, radius) {
      if (!this.currentRoom) return false;
      const obs = this.currentRoom.obstacles;
      for (let i = 0; i < obs.length; i++) {
        const ob = obs[i];
        const halfW = f(ob.width * 0.5), halfH = f(ob.height * 0.5);
        const cx = clampf(x, f(ob.x - halfW), f(ob.x + halfW));
        const cy = clampf(y, f(ob.y - halfH), f(ob.y + halfH));
        const dx = f(x - cx), dy = f(y - cy);
        if (sqrMag(dx, dy) <= f(radius * radius)) return true;
      }
      return false;
    }

    // 64-bit FNV state hash (mirror of Simulation.StateHash), returned as a BigInt ulong string.
    stateHash() {
      let h = 1469598103934665603n;
      const prime = 1099511628211n;
      const fbuf = new ArrayBuffer(4), fv = new Float32Array(fbuf), iv = new Int32Array(fbuf);
      const mix = (x) => { fv[0] = f(x); h = u64((h ^ (BigInt(iv[0] >>> 0))) * prime); };
      const mixI = (i) => { h = u64((h ^ u64(BigInt(i))) * prime); };
      mixI(this.tick); mixI(this.score); mixI(this.roomNumber);
      mix(this.player.x); mix(this.player.y); mix(this.player.health);
      for (let i = 0; i < MaxEnemies; i++) { const e = this.enemies[i]; if (!e.active) continue; mixI(i); mix(e.x); mix(e.y); mix(e.health); }
      for (let i = 0; i < MaxProjectiles; i++) { const p = this.projectiles[i]; if (!p.active) continue; mixI(i); mix(p.x); mix(p.y); }
      return h;
    }
  }

  // Input helpers (mirror InputCommand).
  function clampMagnitude(x, y, max) {
    const sq = sqrMag(x, y);
    if (sq <= f(max * max)) return { x, y };
    const m = mag(x, y);
    if (m < 1e-6) return { x: 0, y: 0 };
    const inv = f(1 / m);
    return { x: f(f(x * inv) * max), y: f(f(y * inv) * max) };
  }
  function makeInput(moveX, moveY, aimX, aimY, firing) {
    const mv = clampMagnitude(moveX, moveY, 1);
    const mx = Math.round(clampf(mv.x, -1, 1) * 100);
    const my = Math.round(clampf(mv.y, -1, 1) * 100);
    let ang = sqrMag(aimX, aimY) > 1e-8 ? DetMath.degreesOf(aimX, aimY) : DetMath.degreesOf(1, 0);
    let deg = ((Math.round(ang) % 360) + 360) % 360;
    return { moveX: clampf(mx, -100, 100) | 0, moveY: clampf(my, -100, 100) | 0, aimDegrees: deg, firing: !!firing };
  }
  function moveVector(input) { return clampMagnitude(f(input.moveX / 100), f(input.moveY / 100), 1); }
  function aimVector(input) { return DetMath.dirFromDegrees(input.aimDegrees); }
  function rotate(x, y, deg) {
    const rad = f(deg * Deg2Rad); const c = detCos(rad), s = detSin(rad);
    return { x: f(f(x * c) - f(y * s)), y: f(f(x * s) + f(y * c)) };
  }

  // Scripted "bot" input identical to the C# vrverify/tests, so JS↔C# runs match exactly.
  function scriptedInput(tick, salt) {
    const r = new DeterministicRandom(u64(BigInt(tick) * 2654435761n) ^ (salt || 0n));
    const mx = f(r.nextFloat() * 2 - 1);
    const my = f(r.nextFloat() * 2 - 1);
    const aim = f(r.nextFloat() * 360);
    const fire = (tick % 5) !== 0;
    const d = DetMath.dirFromDegrees(aim);
    return makeInput(mx, my, d.x, d.y, fire);
  }

  // --------------------------------------------------------------------------------------------
  // Replay decode + verify (mirror of ReplayCodec / ReplayVerifier). Lets the browser VERIFY a
  // .vrplay recorded by the C# tool — the strongest possible proof the port is faithful.
  // --------------------------------------------------------------------------------------------
  const MaxTicks = 216000;

  function b64ToBytes(b64) {
    if (typeof atob === 'function') {
      const bin = atob(b64); const out = new Uint8Array(bin.length);
      for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
      return out;
    }
    return Uint8Array.from(Buffer.from(b64, 'base64')); // Node fallback
  }

  function deserializeReplay(text) {
    const root = JSON.parse(text);
    if (!root || typeof root !== 'object') throw new Error('replay is not a JSON object');
    if (root.magic !== 'VRPLAY') throw new Error(`not a VoidRunner replay (magic='${root.magic}')`);
    if (root.version !== 1) throw new Error(`unsupported replay version ${root.version}`);

    const inputs = [];
    const blob = typeof root.inputs === 'string' ? root.inputs : '';
    if (blob) {
      const bytes = b64ToBytes(blob);
      if (bytes.length % 7 !== 0) throw new Error('corrupt replay input stream (bad length)');
      let total = 0;
      for (let p = 0; p < bytes.length; p += 7) {
        const run = bytes[p] | (bytes[p + 1] << 8);
        total += run;
        if (total > MaxTicks) throw new Error(`replay expands to too many ticks (> ${MaxTicks})`);
        const moveX = (bytes[p + 2] << 24) >> 24; // sign-extend byte -> sbyte
        const moveY = (bytes[p + 3] << 24) >> 24;
        const aim = bytes[p + 4] | (bytes[p + 5] << 8);
        const firing = bytes[p + 6] !== 0;
        for (let k = 0; k < run; k++) inputs.push({ moveX, moveY, aimDegrees: aim, firing });
      }
    }
    return {
      seed: BigInt(root.seed), contentFingerprint: BigInt(root.contentFingerprint),
      label: root.label || '', finalScore: root.finalScore | 0, finalRoom: root.finalRoom | 0,
      finalStateHash: BigInt(root.finalStateHash), inputs,
    };
  }

  function verifyReplay(replay, registry, expectedFingerprint) {
    if (replay.contentFingerprint !== expectedFingerprint)
      return { reproduced: false, message: 'content fingerprint mismatch' };
    const sim = new Simulation(registry, replay.seed);
    for (const cmd of replay.inputs) { sim.step(cmd); if (sim.runOver) break; }
    const h = sim.stateHash();
    const reproduced = h === replay.finalStateHash && sim.score === replay.finalScore && sim.roomNumber === replay.finalRoom;
    return {
      reproduced, replayedScore: sim.score, replayedRoom: sim.roomNumber, replayedStateHash: h,
      message: reproduced ? 'replay reproduced exactly'
        : `desync: expected hash ${replay.finalStateHash} score ${replay.finalScore}, got ${h} score ${sim.score}`,
    };
  }

  global.VoidRunner = {
    DeterministicRandom, DetMath, Simulation, loadContent, contentFingerprint,
    makeInput, scriptedInput, deserializeReplay, verifyReplay,
    FixedDeltaTime, MaxEnemies, MaxProjectiles, MaxPickups, MaxTicks,
  };
})(typeof window !== 'undefined' ? window : globalThis);
