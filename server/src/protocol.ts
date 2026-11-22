// Binary codec mirroring PROTOCOL.md (little-endian).
export const PROTOCOL_VERSION = 3;

export const C2S = { ROLE: 0x01, READY: 0x02, START: 0x03, LEAVE: 0x04, STROKE: 0x10, BACKPADDLE: 0x11, NET: 0x12, PING: 0x20, STATS: 0x21 } as const;
export const S2C = { WELCOME: 0x80, LOBBY: 0x81, MATCH_START: 0x82, SNAPSHOT: 0x83, EVENTS: 0x84, RESULTS: 0x85, PONG: 0x86, NOTICE: 0x87 } as const;

const enc = new TextEncoder();
const dec = new TextDecoder();

export class Writer {
  buf: ArrayBuffer;
  dv: DataView;
  u8: Uint8Array;
  pos = 0;
  constructor(cap = 1024) { this.buf = new ArrayBuffer(cap); this.dv = new DataView(this.buf); this.u8 = new Uint8Array(this.buf); }
  reset() { this.pos = 0; return this; }
  private need(n: number) {
    if (this.pos + n <= this.buf.byteLength) return;
    let cap = this.buf.byteLength * 2;
    while (cap < this.pos + n) cap *= 2;
    const nb = new ArrayBuffer(cap);
    new Uint8Array(nb).set(this.u8.subarray(0, this.pos));
    this.buf = nb; this.dv = new DataView(nb); this.u8 = new Uint8Array(nb);
  }
  u8w(v: number) { this.need(1); this.dv.setUint8(this.pos, v & 0xff); this.pos += 1; return this; }
  i8(v: number) { this.need(1); this.dv.setInt8(this.pos, clamp(Math.round(v), -128, 127)); this.pos += 1; return this; }
  u16(v: number) { this.need(2); this.dv.setUint16(this.pos, clamp(Math.round(v), 0, 65535), true); this.pos += 2; return this; }
  i16(v: number) { this.need(2); this.dv.setInt16(this.pos, clamp(Math.round(v), -32768, 32767), true); this.pos += 2; return this; }
  u32(v: number) { this.need(4); this.dv.setUint32(this.pos, v >>> 0, true); this.pos += 4; return this; }
  i32(v: number) { this.need(4); this.dv.setInt32(this.pos, v | 0, true); this.pos += 4; return this; }
  f32(v: number) { this.need(4); this.dv.setFloat32(this.pos, v, true); this.pos += 4; return this; }
  f64(v: number) { this.need(8); this.dv.setFloat64(this.pos, v, true); this.pos += 8; return this; }
  str(s: string) { const b = enc.encode(s).subarray(0, 255); this.u8w(b.length); this.need(b.length); this.u8.set(b, this.pos); this.pos += b.length; return this; }
  /** Copy of the written bytes (safe to send while the writer is reused). */
  bytes(): ArrayBuffer { return this.buf.slice(0, this.pos); }
}

export class Reader {
  dv: DataView;
  pos = 0;
  constructor(buf: ArrayBuffer) { this.dv = new DataView(buf); }
  get left() { return this.dv.byteLength - this.pos; }
  u8() { const v = this.dv.getUint8(this.pos); this.pos += 1; return v; }
  i8() { const v = this.dv.getInt8(this.pos); this.pos += 1; return v; }
  u16() { const v = this.dv.getUint16(this.pos, true); this.pos += 2; return v; }
  i16() { const v = this.dv.getInt16(this.pos, true); this.pos += 2; return v; }
  u32() { const v = this.dv.getUint32(this.pos, true); this.pos += 4; return v; }
  i32() { const v = this.dv.getInt32(this.pos, true); this.pos += 4; return v; }
  f32() { const v = this.dv.getFloat32(this.pos, true); this.pos += 4; return v; }
  f64() { const v = this.dv.getFloat64(this.pos, true); this.pos += 8; return v; }
  str() { const n = this.u8(); const s = dec.decode(new Uint8Array(this.dv.buffer, this.dv.byteOffset + this.pos, n)); this.pos += n; return s; }
}

export function clamp(v: number, a: number, b: number) { return v < a ? a : v > b ? b : v; }
