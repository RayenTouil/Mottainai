// Generates the Content Pack into public/pack (deterministic): tuning.json + water normal maps + raft skin, manifest with sha256.
import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync, readdirSync, unlinkSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "public", "pack");
mkdirSync(out, { recursive: true });
for (const f of readdirSync(out)) unlinkSync(join(out, f));

function rng(seed) { let s = seed >>> 0; return () => { let t = (s = (s + 0x6d2b79f5) >>> 0); t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }

function normalMap(size, seed, strength) {
  const r = rng(seed), waves = 14, kx = [], ky = [], amp = [], ph = [];
  for (let i = 0; i < waves; i++) {
    const f = 2 + i * 1.7, ang = r() * Math.PI * 2;
    kx[i] = Math.round(Math.cos(ang) * f); ky[i] = Math.round(Math.sin(ang) * f);
    if (kx[i] === 0 && ky[i] === 0) kx[i] = 1;
    amp[i] = 1 / (1 + i * 0.6); ph[i] = Math.floor(r() * size);
  }
  const cosT = new Float32Array(size);
  for (let i = 0; i < size; i++) cosT[i] = Math.cos(2 * Math.PI * i / size);
  const data = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    let du = 0, dv = 0;
    for (let i = 0; i < waves; i++) { const idx = (((kx[i] * x + ky[i] * y + ph[i]) % size) + size) % size; const c = cosT[idx] * amp[i]; du += c * kx[i]; dv += c * ky[i]; }
    let h = (Math.imul(x, 73856093) ^ Math.imul(y, 19349663) ^ seed) >>> 0;
    h ^= h >>> 13; h = Math.imul(h, 0x5bd1e995) >>> 0; h ^= h >>> 15;
    const noise = ((h & 0xff) / 255 - 0.5) * 0.08;
    const nx = -du * 0.045 * strength + noise, ny = -dv * 0.045 * strength - noise, nz = 1, inv = 1 / Math.hypot(nx, ny, nz);
    const o = (y * size + x) * 4;
    data[o] = (nx * inv * 0.5 + 0.5) * 255; data[o + 1] = (ny * inv * 0.5 + 0.5) * 255; data[o + 2] = (nz * inv * 0.5 + 0.5) * 255; data[o + 3] = 255;
  }
  return data;
}

// brown inflatable tube (ART_DIRECTION): alternating rubber panels, light seams, soft grain
function skin(size) {
  const data = Buffer.alloc(size * size * 4);
  const panel = size / 8, seam = 5;
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const k = Math.floor(x / panel), inSeam = x % panel < seam;
    let h = (Math.imul(x, 2654435761) ^ Math.imul(y, 40503)) >>> 0; h ^= h >>> 15;
    const n = ((h & 7) - 4) + Math.round(6 * Math.sin(y / size * Math.PI * 2));
    const base = inSeam ? [150, 112, 82] : k % 2 === 0 ? [96, 60, 40] : [108, 70, 47];
    const o = (y * size + x) * 4;
    for (let c = 0; c < 3; c++) data[o + c] = Math.min(255, Math.max(0, base[c] + n));
    data[o + 3] = 255;
  }
  return data;
}

const files = [
  ["tuning.json", readFileSync(join(root, "src", "tuning.json"))],
  ["water_normal_a_2048.bin", normalMap(2048, 1234, 1.0)],
  ["water_normal_b_2048.bin", normalMap(2048, 98765, 0.6)],
  ["raft_skin_1024.bin", skin(1024)],
];
const manifest = { version: "", totalBytes: 0, files: [] };
for (const [name, buf] of files) {
  if (buf.length > 20 * 1024 * 1024) throw new Error(name + " > 20 MB");
  writeFileSync(join(out, name), buf);
  const hash = createHash("sha256").update(buf).digest("hex");
  manifest.files.push({ name, size: buf.length, hash });
  manifest.totalBytes += buf.length;
}
manifest.version = createHash("sha256").update(manifest.files.map(f => f.hash).join("")).digest("hex").slice(0, 12);
writeFileSync(join(out, "manifest.json"), JSON.stringify(manifest, null, 1));
console.log(`pack ${manifest.version}: ${manifest.files.length} files, ${(manifest.totalBytes / 1048576).toFixed(1)} MB -> public/pack`);
