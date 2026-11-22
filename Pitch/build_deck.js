// Builds Pitch/MOTTAINAI_Pitch.pptx, an HTML twin (printed to PDF by Edge) and Pitch/SCRIPT.md from one slide description.
// Needs pptxgenjs, react, react-dom, react-icons, sharp: set NODE_PATH to a folder where they are installed.
// Usage: node Pitch/build_deck.js <out-dir-for-html>
const fs = require("fs");
const path = require("path");
const pptxgen = require("pptxgenjs");
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");
const sharp = require("sharp");
const fa = require("react-icons/fa");
const gi = require("react-icons/gi");

const ROOT = path.resolve(__dirname, "..");
const PITCH = path.join(ROOT, "Pitch");
const HTML_DIR = path.resolve(process.argv[2] || path.join(PITCH, "build"));
const APPLY_THEME = process.env.APPLY_THEME;
fs.mkdirSync(HTML_DIR, { recursive: true });

const W = 13.333, H = 7.5, X0 = 0.7, CW = 11.93;
const HEAD = "Fredoka Light SemiBold", BODY = "Calibri", JP = "Yu Gothic UI";
const HEX = { TEAL: "244A53", WATER: "167E9B", WHITE: "FFFFFF", CLOUD: "F5FAF7", CYAN: "21B8C7", GREEN: "62C96B", SUN: "FFC857", CORAL: "FF8360", PURPLE: "9B4DFF", BROWN: "7A4A21", CARD: "FFFFFF", MIST: "E2F1F0" };
const THEME = {
  name: "Mottainai Eco", headFontFace: HEAD, bodyFontFace: BODY,
  colors: { dk1: HEX.TEAL, lt1: HEX.WHITE, dk2: HEX.WATER, lt2: HEX.CLOUD, accent1: HEX.CYAN, accent2: HEX.GREEN, accent3: HEX.SUN, accent4: HEX.CORAL, accent5: HEX.BROWN, accent6: HEX.PURPLE, hlink: HEX.WATER, folHlink: HEX.TEAL },
};
const SCH = { TEAL: "text1", WATER: "text2", WHITE: "background1", CLOUD: "background2", CYAN: "accent1", GREEN: "accent2", SUN: "accent3", CORAL: "accent4", BROWN: "accent5", PURPLE: "accent6" };
const WASTE = [
  { name: "Plastic", pts: "+10", hex: "F2C200" }, { name: "Paper", pts: "+10", hex: "1E6FD9" }, { name: "Glass", pts: "+15", hex: "2E9E44" },
  { name: "Organic", pts: "+5", hex: "7A4A21" }, { name: "Metal", pts: "+20", hex: "8C959F" }, { name: "Valuable", pts: "+100", hex: "9B4DFF" },
  { name: "Toxic", pts: "−30", hex: "D62828" },
];

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE";
pres.title = "MOTTAINAI — Green Hackathon 2026 pitch";
pres.author = "Team MOTTAINAI (SUP'COM)";
pres.theme = { headFontFace: HEAD, bodyFontFace: BODY };
const C = pres.SchemeColor;
const pc = k => (SCH[k] ? C[SCH[k]] : HEX[k] || k);
const hc = k => HEX[k] || k;

// ---------- layouts ----------
const FOOT = "MOTTAINAI · Green Hackathon 2026";
const LAYOUTS = {
  LIGHT: { bg: "CLOUD", foot: "WATER", title: { x: X0, y: 0.45, w: CW, h: 0.85, size: 40, color: "TEAL" } },
  DARK: { bg: "TEAL", foot: "CYAN", title: null },
};
for (const [name, L] of Object.entries(LAYOUTS)) {
  const objects = [{ text: { text: FOOT, options: { x: X0, y: 6.95, w: 6, h: 0.3, fontSize: 12, color: pc(L.foot), margin: 0, valign: "middle" } } }];
  if (L.title) objects.push({ placeholder: { options: { name: "title", type: "title", x: L.title.x, y: L.title.y, w: L.title.w, h: L.title.h, fontFace: HEAD, fontSize: L.title.size, color: pc(L.title.color), valign: "middle", align: "left", margin: 0 }, text: "" } });
  pres.defineSlideMaster({ title: name, background: { color: hc(L.bg) }, objects, slideNumber: { x: W - X0 - 0.6, y: 6.95, w: 0.6, h: 0.3, fontFace: BODY, fontSize: 12, color: hc(L.foot), align: "right", margin: 0 } });
}

// ---------- icons ----------
const iconCache = new Map();
async function icon(Comp, color, px = 384) {
  const key = Comp.name + color;
  if (iconCache.has(key)) return iconCache.get(key);
  const svg = renderToStaticMarkup(React.createElement(Comp, { color: "#" + hc(color), size: px }));
  const buf = await sharp(Buffer.from(svg)).resize(px, px, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toBuffer();
  const data = "image/png;base64," + buf.toString("base64");
  iconCache.set(key, data);
  return data;
}

// ---------- dual renderer (pptx + html) ----------
const htmlSlides = [];
const script = [];
let slideNo = 0;
const esc = s => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const fileUrl = p => "file:///" + p.replace(/\\/g, "/").replace(/ /g, "%20");

function makeSlide({ layout, section, hidden, plan }) {
  slideNo++;
  if (section) pres.addSection({ title: section });
  const L = LAYOUTS[layout];
  const s = pres.addSlide({ masterName: layout, sectionTitle: section ? section : undefined });
  if (hidden) s.hidden = true;
  const html = [];
  const n = slideNo;
  let names = 0;
  const id = kind => `${kind}-${n}-${++names}`;
  const box = (o, extra = "") => `left:${o.x}in;top:${o.y}in;width:${o.w}in;height:${o.h}in;${extra}`;
  const api = {
    n,
    title(text) {
      s.addText(text, { placeholder: "title" });
      const t = L.title;
      html.push(`<div class="t" style="${box(t)}font-family:'${HEAD}';font-size:${t.size}pt;color:#${hc(t.color)};justify-content:center;">${esc(text)}</div>`);
    },
    text(runs, o) {
      const arr = typeof runs === "string" ? [{ text: runs }] : runs;
      const base = { fontSize: o.size || 20, color: o.color || "TEAL", bold: !!o.bold, italic: !!o.italic, font: o.font || "body" };
      const face = f => (f === "head" ? HEAD : f === "jp" ? JP : BODY);
      const pptRuns = arr.map((r, i) => {
        const m = { ...base, ...r };
        const opt = { fontSize: m.fontSize, color: pc(m.color), bold: m.bold, italic: m.italic, fontFace: face(m.font) };
        if (o.spacing) opt.charSpacing = o.spacing;
        if (r.breakLine) opt.breakLine = true;
        return { text: r.text, options: opt };
      });
      s.addText(pptRuns, { x: o.x, y: o.y, w: o.w, h: o.h, margin: 0, valign: o.valign || "top", align: o.align || "left", isTextBox: true, fit: "none", objectName: id("text"), paraSpaceAfter: o.paraAfter || 0, lineSpacingMultiple: o.lineMul || undefined });
      const inner = arr.map(r => {
        const m = { ...base, ...r };
        const fam = m.font === "head" ? `'${HEAD}'` : m.font === "jp" ? `'${JP}','Meiryo'` : `'${BODY}'`;
        return `<span style="font-family:${fam};font-size:${m.fontSize}pt;color:#${hc(m.color)};font-weight:${m.bold ? 700 : 400};font-style:${m.italic ? "italic" : "normal"};${o.spacing ? `letter-spacing:${o.spacing}pt;` : ""}">${esc(m.text)}</span>${r.breakLine ? "<br>" : ""}`;
      }).join("");
      const jc = { top: "flex-start", middle: "center", bottom: "flex-end" }[o.valign || "top"];
      html.push(`<div class="t" style="${box(o)}justify-content:${jc};text-align:${o.align || "left"};${o.lineMul ? `line-height:${1.2 * o.lineMul};` : ""}">${`<div>${inner}</div>`}</div>`);
    },
    rect(o) {
      const shape = o.radius ? pres.shapes.ROUNDED_RECTANGLE : pres.shapes.RECTANGLE;
      const opt = { x: o.x, y: o.y, w: o.w, h: o.h, fill: { color: pc(o.fill), transparency: o.transparency || 0 }, line: o.line ? { color: pc(o.line), width: o.lineW || 1.5 } : { type: "none" }, objectName: id("shape") };
      if (o.radius) opt.rectRadius = o.radius;
      if (o.shadow) opt.shadow = { type: "outer", color: "244A53", opacity: 0.12, blur: 8, offset: 2, angle: 90 };
      s.addShape(shape, opt);
      const op = 1 - (o.transparency || 0) / 100;
      html.push(`<div style="position:absolute;${box(o)}background:#${hc(o.fill)};opacity:${op};border-radius:${o.radius ? o.radius + "in" : 0};${o.line ? `border:${(o.lineW || 1.5)}pt solid #${hc(o.line)};box-sizing:border-box;` : ""}${o.shadow ? "box-shadow:0 2pt 8pt rgba(36,74,83,.12);" : ""}"></div>`);
    },
    circle(o) {
      s.addShape(pres.shapes.OVAL, { x: o.x, y: o.y, w: o.d, h: o.d, fill: { color: pc(o.fill) }, line: { type: "none" }, objectName: id("circle") });
      html.push(`<div style="position:absolute;${box({ x: o.x, y: o.y, w: o.d, h: o.d })}background:#${hc(o.fill)};border-radius:50%;"></div>`);
    },
    image(o) {
      const opt = { x: o.x, y: o.y, w: o.w, h: o.h, altText: o.alt, objectName: id("image") };
      if (o.data) opt.data = o.data; else opt.path = o.path;
      if (o.cover) opt.sizing = { type: "cover", w: o.w, h: o.h };
      if (o.transparency) opt.transparency = o.transparency;
      s.addImage(opt);
      const src = o.data ? "data:" + o.data : fileUrl(o.path);
      html.push(`<img alt="${esc(o.alt)}" src="${src}" style="position:absolute;${box(o)}object-fit:${o.cover ? "cover" : "contain"};${o.transparency ? `opacity:${1 - o.transparency / 100};` : ""}">`);
    },
    chart(type, data, opt, htmlFn) {
      s.addChart(type, data, opt);
      html.push(htmlFn());
    },
    notes(speaker, lines, cue) {
      const text = lines.join(" ");
      const words = text.replace(/\[[^\]]*\]/g, " ").split(/\s+/).filter(w => /[A-Za-z0-9]/.test(w)).length;
      script.push({ n, title: plan.title, time: plan.time, speaker, text, words, demo: !!plan.demo, hidden: !!hidden, extra: cue });
      s.addNotes(`SPEAKER: ${speaker} · ${plan.time}\n\n${text}${cue ? "\n\n" + cue : ""}`);
    },
  };
  htmlSlides.push({ L, html, hidden, n });
  return api;
}

function htmlBar(area, items, max) {
  // mirror of the native bar chart for the PDF twin
  const bw = area.w / items.length;
  let out = "";
  items.forEach((it, i) => {
    const h = (it.v / max) * (area.h - 0.9);
    const x = area.x + i * bw + bw * 0.18, w = bw * 0.64, y = area.y + area.h - 0.45 - h;
    out += `<div style="position:absolute;left:${x}in;top:${y}in;width:${w}in;height:${h}in;background:#${it.hex}"></div>`;
    out += `<div class="t" style="left:${area.x + i * bw}in;top:${y - 0.38}in;width:${bw}in;height:0.34in;justify-content:flex-end;text-align:center;font-family:Calibri;font-size:18pt;color:#244A53;font-weight:700">${it.v}</div>`;
    out += `<div class="t" style="left:${area.x + i * bw}in;top:${area.y + area.h - 0.4}in;width:${bw}in;height:0.36in;text-align:center;font-family:Calibri;font-size:16pt;color:#244A53">${it.label}</div>`;
  });
  out += `<div style="position:absolute;left:${area.x}in;top:${area.y + area.h - 0.45}in;width:${area.w}in;height:0;border-top:1pt solid #9FB8BC"></div>`;
  return out;
}

const ASSET = p => path.join(ROOT, p);
const IMG = {
  ref: ASSET("Docs/reference_v3_master.jpg"),
  banner: ASSET("Docs/media/mottainai_banner.gif"),
  play: ASSET("Docs/art_v3/P5_pc_20s.png"),
  lobby: ASSET("Docs/art_v3/P5_pc_lobby.png"),
  results: ASSET("Docs/art_v3/P5_pc_results.png"),
};

(async () => {
  // ===== 1. Hook =====
  {
    const s = makeSlide({ layout: "DARK", section: "Why", plan: { title: "Hook: the sea is choking", time: "0:25" } });
    s.image({ path: IMG.ref, x: 0, y: 0, w: W, h: H, cover: true, alt: "Concept art of a polluted river flowing to the sea" });
    s.rect({ x: 0, y: 0, w: W, h: H, fill: "TEAL", transparency: 18 });
    s.text("THE MEDITERRANEAN, EVERY YEAR", { x: X0, y: 1.0, w: 8, h: 0.45, size: 20, color: "CYAN", bold: true });
    s.text([{ text: "570,000", fontSize: 88, color: "WHITE", font: "head" }, { text: "  tonnes of plastic", fontSize: 32, color: "WHITE" }], { x: X0, y: 1.45, w: 11.5, h: 1.45, valign: "bottom" });
    s.text("≈ 33,800 plastic bottles every minute", { x: X0, y: 3.05, w: 10, h: 0.55, size: 28, color: "CORAL", bold: true });
    s.text("What if kids could fix it — by playing?", { x: X0, y: 4.55, w: 11.5, h: 0.8, size: 38, color: "SUN", font: "head" });
    s.text("Source: WWF, Stop the Flood of Plastic (2019)", { x: X0, y: 6.45, w: 8, h: 0.3, size: 12, color: "WHITE" });
    s.notes("Yassine AFFES", [
      "Every year, five hundred seventy thousand tonnes of plastic flow into the Mediterranean, our sea.",
      "That's thirty-three thousand eight hundred bottles every minute, according to WWF.",
      "We are four students from SUP'COM, and we asked one question: what if kids could fix it, by playing? [CLICK]",
    ]);
  }
  // ===== 2. Problem =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "The real problem", time: "0:15" } });
    s.title("Plastic starts in our streets and rivers");
    s.text([{ text: "109", fontSize: 80, color: "WATER", font: "head" }, { text: " Mt", fontSize: 40, color: "WATER", font: "head" }], { x: X0, y: 1.7, w: 5.4, h: 1.4, valign: "bottom" });
    s.text("of plastic already sit in the world's rivers", { x: X0, y: 3.2, w: 5.2, h: 0.9, size: 24 });
    s.text("Source: OECD, Global Plastics Outlook (2022)", { x: X0, y: 4.25, w: 5.4, h: 0.3, size: 12, color: "WATER" });
    const steps = [{ ic: fa.FaCity, label: "Streets", fill: "WATER" }, { ic: fa.FaWater, label: "Rivers", fill: "CYAN" }, { ic: gi.GiWaveCrest, label: "Sea", fill: "TEAL" }];
    const arrow = await icon(fa.FaArrowRight, "WATER");
    for (let i = 0; i < 3; i++) {
      const cx = 7.1 + i * 1.95;
      s.circle({ x: cx, y: 1.75, d: 1.3, fill: steps[i].fill });
      s.image({ data: await icon(steps[i].ic, "WHITE"), x: cx + 0.33, y: 2.08, w: 0.64, h: 0.64, alt: steps[i].label + " icon" });
      s.text(steps[i].label, { x: cx - 0.3, y: 3.15, w: 1.9, h: 0.4, size: 20, bold: true, align: "center" });
      if (i < 2) s.image({ data: arrow, x: cx + 1.47, y: 2.24, w: 0.32, h: 0.32, alt: "arrow" });
    }
    s.rect({ x: 7.0, y: 4.05, w: 5.63, h: 1.45, fill: "CARD", radius: 0.2, shadow: true });
    s.image({ data: await icon(fa.FaChalkboardTeacher, "CORAL"), x: 7.3, y: 4.42, w: 0.7, h: 0.7, alt: "lecture icon" });
    s.text("Kids don't learn sorting from lectures.", { x: 8.25, y: 4.15, w: 4.2, h: 1.25, size: 24, bold: true, valign: "middle" });
    s.notes("Yassine AFFES", [
      "Plastic doesn't start in the sea. It starts in our streets and rivers: the OECD counts one hundred nine million tonnes sitting in rivers.",
      "And kids don't learn sorting from lectures. They learn by doing. [CLICK] [HAND OVER TO WAJDI]",
    ]);
  }
  // ===== 3. MOTTAINAI =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "Game", plan: { title: "MOTTAINAI", time: "0:15" } });
    s.text("MOTTAINAI", { x: X0, y: 1.35, w: 5.8, h: 1.1, size: 60, font: "head", color: "TEAL" });
    s.text([{ text: "もったいない", font: "jp" }, { text: " — “What a waste!”" }], { x: X0, y: 2.5, w: 5.8, h: 0.55, size: 26, color: "WATER" });
    s.text("Paddle • Collect • Recycle", { x: X0, y: 3.2, w: 5.8, h: 0.5, size: 26, color: "TEAL", bold: true });
    s.text("A co-op 3D mobile game: three players share one raft to clean a polluted river.", { x: X0, y: 4.0, w: 5.5, h: 1.3, size: 22 });
    s.image({ path: IMG.banner, x: 6.6, y: 1.45, w: 6.03, h: 3.39, alt: "Animated MOTTAINAI banner: three kids on a raft collecting waste" });
    s.text("Concept art — target art direction", { x: 6.6, y: 4.95, w: 6.03, h: 0.3, size: 12, color: "WATER", italic: true });
    WASTE.forEach((w, i) => s.circle({ x: 6.6 + i * 0.5, y: 5.55, d: 0.3, fill: w.hex }));
    s.notes("Wajdi ZENTANI", [
      "So we built MOTTAINAI, a Japanese word for 'what a waste!'.",
      "It's a cooperative 3D mobile game: three players share one raft to clean a polluted river. Paddle, collect, recycle. [CLICK]",
    ]);
  }
  // ===== 4. Mechanics =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "Game mechanics", time: "0:25" } });
    s.title("Three roles, one raft");
    const roles = [
      { name: "Left paddler", ic: gi.GiCanoe, fill: "WATER", lines: ["Swipe down: paddle", "Swipe up: brake"] },
      { name: "Right paddler", ic: gi.GiCanoe, fill: "CYAN", lines: ["Swipe down: paddle", "Swipe up: brake"] },
      { name: "Collector", ic: gi.GiFishingNet, fill: "GREEN", lines: ["Tap waste:", "throw the net"] },
    ];
    for (let i = 0; i < 3; i++) {
      const x = X0 + i * (3.75 + 0.34), r = roles[i];
      s.rect({ x, y: 1.6, w: 3.75, h: 2.75, fill: "CARD", radius: 0.2, shadow: true });
      s.circle({ x: x + 0.35, y: 1.9, d: 0.95, fill: r.fill });
      s.image({ data: await icon(r.ic, "WHITE"), x: x + 0.55, y: 2.1, w: 0.55, h: 0.55, alt: r.name + " icon" });
      s.text(r.name, { x: x + 1.5, y: 1.9, w: 2.1, h: 0.95, size: 24, bold: true, valign: "middle" });
      s.text([{ text: r.lines[0], breakLine: true }, { text: r.lines[1] }], { x: x + 0.35, y: 3.1, w: 3.2, h: 1.0, size: 20 });
    }
    s.rect({ x: X0, y: 4.75, w: CW, h: 1.75, fill: "TEAL", radius: 0.2 });
    s.text("+30 %", { x: X0 + 0.4, y: 4.85, w: 3.0, h: 1.55, size: 60, color: "SUN", font: "head", valign: "middle" });
    s.text([{ text: "SYNC BOOST", bold: true, color: "SUN", breakLine: true }, { text: "Both paddlers stroke within 0.25 s", color: "WHITE" }], { x: X0 + 3.5, y: 4.95, w: 4.6, h: 1.35, size: 22, valign: "middle" });
    s.image({ data: await icon(fa.FaRobot, "CYAN"), x: X0 + 8.35, y: 5.3, w: 0.6, h: 0.6, alt: "bot icon" });
    s.text("Missing players? Bots fill the seats.", { x: X0 + 9.1, y: 4.95, w: 2.6, h: 1.35, size: 20, color: "WHITE", valign: "middle" });
    s.notes("Wajdi ZENTANI", [
      "Each player controls one part of the raft. The left paddler swipes down to push forward and turn right; the right paddler does the opposite.",
      "The collector taps waste to throw the net.",
      "When both paddlers stroke within a quarter second, you get a sync boost: thirty percent more thrust. Missing a friend? A bot takes the seat. [CLICK]",
    ]);
  }
  // ===== 5. Rules =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "Rules and objective", time: "0:15" } });
    s.title("Two minutes to clean the river");
    const tiles = [
      { big: "2:00", label: "per run", ic: fa.FaStopwatch, c: "WATER" }, { big: "1 hit", label: "= game over", ic: fa.FaSkullCrossbones, c: "CORAL" },
      { big: "6 s", label: "shield", ic: fa.FaShieldAlt, c: "CYAN" }, { big: "×1.5", label: "combo: 3 catches in 3 s", ic: fa.FaFire, c: "GREEN" },
    ];
    for (let i = 0; i < 4; i++) {
      const x = X0 + i * (2.75 + 0.31), t = tiles[i];
      s.rect({ x, y: 1.6, w: 2.75, h: 1.95, fill: "CARD", radius: 0.2, shadow: true });
      s.image({ data: await icon(t.ic, t.c), x: x + 0.3, y: 1.85, w: 0.45, h: 0.45, alt: t.label + " icon" });
      s.text(t.big, { x: x + 0.3, y: 2.3, w: 2.3, h: 0.75, size: 40, font: "head", color: "TEAL", valign: "middle" });
      s.text(t.label, { x: x + 0.3, y: 3.05, w: 2.35, h: 0.4, size: 18, color: "WATER" });
    }
    const step = CW / 7;
    WASTE.forEach((w, i) => {
      const cx = X0 + i * step + (step - 1.0) / 2;
      s.circle({ x: cx, y: 4.0, d: 1.0, fill: w.hex });
      s.text(w.pts, { x: cx - 0.2, y: 4.0, w: 1.4, h: 1.0, size: 22, bold: true, color: "WHITE", align: "center", valign: "middle" });
      s.text(w.name, { x: X0 + i * step, y: 5.1, w: step, h: 0.4, size: 18, align: "center" });
    });
    s.text("Goal: clean the most river, together.", { x: X0, y: 5.85, w: CW, h: 0.5, size: 24, bold: true, color: "TEAL" });
    s.notes("Wajdi ZENTANI", [
      "You have two minutes. One hit on a rock or a shark, and it's game over. Grab the shield for six seconds of safety.",
      "Every bin color scores, but toxic waste costs you. [CLICK] [HAND OVER TO RAYEN]",
    ]);
  }
  // ===== 6. Live demo =====
  {
    const s = makeSlide({ layout: "DARK", section: "Demo", plan: { title: "LIVE DEMO", time: "1:00 ±0:20", demo: true } });
    s.text("LIVE DEMO", { x: X0, y: 0.8, w: CW, h: 1.4, size: 80, spacing: 8, font: "head", color: "WHITE", align: "center", valign: "middle" });
    const labels = ["LEFT PADDLER", "RIGHT PADDLER", "COLLECTOR"];
    const phone = await icon(fa.FaMobileAlt, "WHITE");
    for (let i = 0; i < 3; i++) {
      const cx = W / 2 - 0.95 + (i - 1) * 3.4;
      s.circle({ x: cx, y: 2.55, d: 1.9, fill: "WATER" });
      s.image({ data: phone, x: cx + 0.45, y: 3.0, w: 1.0, h: 1.0, alt: "phone icon" });
      s.text(labels[i], { x: cx - 0.6, y: 4.6, w: 3.1, h: 0.45, size: 20, bold: true, color: "CYAN", align: "center" });
    }
    s.text("3 players · 1 raft · real time over the network", { x: X0, y: 5.5, w: CW, h: 0.6, size: 28, color: "SUN", align: "center" });
    s.notes("Rayen TOUIL", [
      "Let's play it live. [START DEMO] Two phones join the Quick Play room. I'm the left paddler, Wajdi is the collector, and a bot takes the right paddle. Start!",
      "Watch the sync: when we stroke together, the raft shoots forward. The net catches plastic, glass, metal... careful, a shark fin!",
      "Every action goes to our server and back sixty times a second. [~40 s of play: steer into a rock to end the run]",
      "Oops, game over! Here are the results: score, waste per bin, accuracy, distance and kilograms of river cleaned. [CLICK]",
    ], [
      "DEMO STEPS: (0) Before the talk: Wi-Fi OFF, mobile data ON on both phones, app opened, name entered, content pack already downloaded, Phone A mirrored to the projector.",
      "(1) Phone A: QUICK PLAY → LEFT PADDLER. Phone B: QUICK PLAY → COLLECTOR. The bot takes RIGHT PADDLER.",
      "(2) START → 3 s countdown → play about 40 s (3-finger tap once to show the Net HUD: network type + RTT).",
      "(3) Steer into a rock to end the run → GAME OVER → results screen.",
      "FALLBACK: if the network fails, show the backup screenshots on slide 6b (hidden: type 7 + Enter in slideshow mode).",
    ].join("\n"));
  }
  // ===== 6b. Demo backup (hidden) =====
  {
    const s = makeSlide({ layout: "LIGHT", hidden: true, plan: { title: "Demo backup (hidden)", time: "—" } });
    s.title("Demo backup");
    s.image({ path: IMG.play, x: X0, y: 1.55, w: 7.3, h: 3.29, alt: "Gameplay screenshot: raft on the river with waste and score" });
    s.image({ path: IMG.lobby, x: 8.35, y: 1.55, w: 4.28, h: 1.93, alt: "Lobby screenshot: role selection" });
    s.image({ path: IMG.results, x: 8.35, y: 3.9, w: 4.28, h: 1.93, alt: "Results screenshot: score and waste per bin" });
    const chip = (x, y, t) => { s.rect({ x, y, w: 1.75, h: 0.45, fill: "TEAL", radius: 0.12 }); s.text(t, { x, y, w: 1.75, h: 0.45, size: 18, bold: true, color: "WHITE", align: "center", valign: "middle" }); };
    chip(X0 + 0.15, 4.25, "Gameplay"); chip(8.5, 2.88, "Lobby"); chip(8.5, 5.23, "Results");
    s.text("Real screenshots from the current build (PC check, art v3).", { x: X0, y: 5.25, w: 7.3, h: 0.45, size: 18, color: "WATER" });
    s.notes("Rayen TOUIL", ["Plan B only. Here is the lobby where each phone picks a role, the river in play, and the results screen. [CLICK]"]);
  }
  // ===== 7. Architecture =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "Tech", plan: { title: "Online architecture", time: "0:20" } });
    s.title("Online architecture");
    const boxes = [
      { ic: fa.FaMobileAlt, t: "Phones", sub: "Android app (Unity, C#)", c: "WATER" },
      { ic: fa.FaCloud, t: "Cloudflare edge", sub: "Marseille (MRS)", c: "CYAN" },
      { ic: fa.FaServer, t: "Game room", sub: "Authoritative sim, 60 Hz, bots", c: "GREEN" },
    ];
    const arrow = await icon(fa.FaArrowRight, "WATER");
    for (let i = 0; i < 3; i++) {
      const x = X0 + i * (3.3 + 1.015), b = boxes[i];
      s.rect({ x, y: 1.6, w: 3.3, h: 2.55, fill: "CARD", radius: 0.2, shadow: true });
      s.circle({ x: x + 1.2, y: 1.8, d: 0.9, fill: b.c });
      s.image({ data: await icon(b.ic, "WHITE"), x: x + 1.42, y: 2.02, w: 0.46, h: 0.46, alt: b.t + " icon" });
      s.text(b.t, { x: x + 0.15, y: 2.8, w: 3.0, h: 0.45, size: 24, bold: true, align: "center" });
      s.text(b.sub, { x: x + 0.15, y: 3.28, w: 3.0, h: 0.75, size: 18, color: "WATER", align: "center" });
      if (i < 2) {
        s.text("wss", { x: x + 3.3, y: 2.2, w: 1.015, h: 0.4, size: 18, bold: true, color: "WATER", align: "center" });
        s.image({ data: arrow, x: x + 3.3 + 0.3, y: 2.65, w: 0.42, h: 0.42, alt: "arrow" });
      }
    }
    s.text("Events up · full snapshots down at 60 Hz · HTTPS for login, 36 MB content pack, telemetry", { x: X0, y: 4.4, w: CW, h: 0.45, size: 20, color: "TEAL" });
    const chips = ["60 Hz tick", "Lag compensation", "Own-raft prediction", "Adaptive interpolation"];
    chips.forEach((c, i) => {
      const x = X0 + i * (2.75 + 0.31);
      s.rect({ x, y: 5.25, w: 2.75, h: 0.65, fill: "WATER", radius: 0.32 });
      s.text(c, { x, y: 5.25, w: 2.75, h: 0.65, size: 18, bold: true, color: "WHITE", align: "center", valign: "middle" });
    });
    s.notes("Rayen TOUIL", [
      "How does it work? Phones connect over secure WebSockets to Cloudflare's edge in Marseille. The game room runs on Cloudflare too, at sixty ticks per second.",
      "The server decides everything; phones predict their own raft and smooth the rest, and lag compensation keeps catches fair. [CLICK]",
    ]);
  }
  // ===== 8. Voice =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "Microphones / voice", time: "0:15" } });
    s.title("Team voice");
    s.circle({ x: 1.6, y: 2.0, d: 2.6, fill: "CYAN" });
    s.image({ data: await icon(fa.FaMicrophone, "WHITE"), x: 2.35, y: 2.75, w: 1.1, h: 1.1, alt: "microphone icon" });
    s.rect({ x: 5.0, y: 2.25, w: 1.3, h: 0.5, fill: "SUN", radius: 0.25 });
    s.text("NEXT", { x: 5.0, y: 2.25, w: 1.3, h: 0.5, size: 18, bold: true, color: "TEAL", align: "center", valign: "middle" });
    s.text("Team voice chat", { x: 5.0, y: 2.95, w: 7.5, h: 0.95, size: 44, font: "head", color: "TEAL", valign: "middle" });
    s.text("Not in the build yet: today the crew talks out loud.", { x: 5.0, y: 4.0, w: 7.5, h: 0.5, size: 22, color: "WATER" });
    s.notes("Rayen TOUIL", [
      "Voice is next. Today, players talk out loud in the same room; that's part of the fun.",
      "Next, we add in-game team voice chat so friends can play from anywhere. [CLICK] [HAND OVER TO MED AMINE]",
    ]);
  }
  // ===== 9. Data =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "Data", time: "0:15" } });
    s.title("What we measure");
    const cards = [
      { t: "Gameplay", ic: gi.GiFishingNet, c: "GREEN", body: "Waste per bin · accuracy · distance · kg cleaned · sync strokes" },
      { t: "Network", ic: fa.FaSignal, c: "CYAN", body: "RTT · jitter · Mbps · network type → CSV on our server" },
    ];
    for (let i = 0; i < 2; i++) {
      const y = 1.6 + i * 2.3, k = cards[i];
      s.rect({ x: X0, y, w: 4.6, h: 2.0, fill: "CARD", radius: 0.2, shadow: true });
      s.circle({ x: X0 + 0.3, y: y + 0.3, d: 0.6, fill: k.c });
      s.image({ data: await icon(k.ic, "WHITE"), x: X0 + 0.42, y: y + 0.42, w: 0.36, h: 0.36, alt: k.t + " icon" });
      s.text(k.t, { x: X0 + 1.1, y: y + 0.3, w: 3.3, h: 0.6, size: 24, bold: true, valign: "middle" });
      s.text(k.body, { x: X0 + 0.3, y: y + 1.0, w: 4.1, h: 0.9, size: 20 });
    }
    const counts = [30, 11, 19, 10, 9, 0, 10];
    s.text("Waste caught in one real match", { x: 5.8, y: 1.6, w: 6.8, h: 0.5, size: 22, bold: true });
    const area = { x: 5.8, y: 2.15, w: 6.83, h: 3.75 };
    s.chart(pres.charts.BAR, [{ name: "Caught", labels: WASTE.map(w => w.name), values: counts }], {
      ...area, barDir: "col", chartColors: WASTE.map(w => w.hex), showValue: true, dataLabelPosition: "outEnd", dataLabelFontSize: 18, dataLabelColor: "244A53", dataLabelFontFace: "+mn-lt", dataLabelFontBold: true,
      catAxisLabelFontSize: 16, catAxisLabelColor: "244A53", catAxisLabelFontFace: "+mn-lt", catAxisLineShow: true, catAxisLineColor: "9FB8BC",
      valAxisHidden: true, valAxisMaxVal: 34, valAxisMinVal: 0, valGridLine: { style: "none" }, catGridLine: { style: "none" }, showLegend: false, showTitle: false, barGapWidthPct: 55, objectName: "chart-waste",
    }, () => htmlBar(area, WASTE.map((w, i) => ({ label: w.name, v: counts[i], hex: w.hex })), 34));
    s.text("Server test match, 22 Nov 2026 (3 bots, 2:00). Next: per-player learning insights.", { x: 5.8, y: 6.0, w: 6.83, h: 0.3, size: 12, color: "WATER" });
    s.notes("Med Amine CHAKROUN", [
      "Every run produces data: waste per bin, accuracy, distance, kilos cleaned, and network quality: latency, jitter, bandwidth.",
      "Network results land in a CSV on our server. Here is one real test match. [CLICK]",
    ]);
  }
  // ===== 10. 5G SA =====
  {
    const s = makeSlide({ layout: "LIGHT", plan: { title: "5G SA: why latency matters", time: "0:25" } });
    s.title("Real-time co-op needs low latency");
    const stats = [
      { big: "71 ms", c: "WATER", label: "Phone on Wi-Fi → game room in Paris", src: "Network Lab, 22 Nov 2026 (avg)" },
      { big: "43 ms", c: "GREEN", label: "Test client → game room in Marseille", src: "Full 2:00 match, 22 Nov 2026 (avg)" },
      { big: "5G SA", c: "TEAL", label: "Same test on 5G SA + latency slice", src: "", next: true },
    ];
    for (let i = 0; i < 3; i++) {
      const x = X0 + i * (3.75 + 0.34), k = stats[i];
      s.rect({ x, y: 1.6, w: 3.75, h: 2.85, fill: "CARD", radius: 0.2, shadow: true, line: k.next ? "SUN" : undefined, lineW: 2.5 });
      if (k.next) { s.rect({ x: x + 0.3, y: 1.85, w: 1.1, h: 0.42, fill: "SUN", radius: 0.21 }); s.text("NEXT", { x: x + 0.3, y: 1.85, w: 1.1, h: 0.42, size: 18, bold: true, color: "TEAL", align: "center", valign: "middle" }); }
      s.text(k.big, { x: x + 0.3, y: 2.3, w: 3.2, h: 1.0, size: 60, font: "head", color: k.c, valign: "middle" });
      s.text(k.label, { x: x + 0.3, y: 3.35, w: 3.2, h: 0.7, size: 18 });
      if (k.src) s.text(k.src, { x: x + 0.3, y: 4.05, w: 3.2, h: 0.3, size: 12, color: "WATER" });
    }
    const chk = await icon(fa.FaCheckCircle, "WHITE");
    ["5G SA / NSA detection in the app", "Low-latency slice request (Android 13+)"].forEach((t, i) => {
      const x = X0 + i * 6.12;
      s.rect({ x, y: 4.75, w: 5.81, h: 0.65, fill: "CYAN", radius: 0.32 });
      s.image({ data: chk, x: x + 0.25, y: 4.89, w: 0.37, h: 0.37, alt: "check icon" });
      s.text(t, { x: x + 0.75, y: 4.75, w: 4.95, h: 0.65, size: 20, bold: true, color: "TEAL", valign: "middle" });
    });
    s.text("Real-time co-op needs edge + 5G SA.", { x: X0, y: 5.75, w: CW, h: 0.6, size: 30, font: "head", color: "TEAL" });
    s.notes("Med Amine CHAKROUN", [
      "Why does latency matter? Paddlers must stroke within a quarter second of each other.",
      "We measured it: a phone on Wi-Fi to our Paris room, seventy-one milliseconds; a Marseille room, forty-three.",
      "The app already detects 5G SA and requests a low-latency slice. Next step: the same test on 5G SA. Real-time co-op needs edge plus 5G SA. [CLICK]",
    ]);
  }
  // ===== 11. Impact =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "Impact", plan: { title: "Impact and next steps", time: "0:10" } });
    s.title("Impact and next steps");
    const cols = [
      { ic: fa.FaSchool, c: "WATER", t: "Schools & NGOs", d: "Classroom sessions with local partners" },
      { ic: fa.FaLeaf, c: "GREEN", t: "Eco-fact per catch", d: "Learn why each bin matters" },
      { ic: gi.GiCrossedSwords, c: "CORAL", t: "Battle mode", d: "Several rafts, team vs team" },
    ];
    for (let i = 0; i < 3; i++) {
      const x = X0 + i * (3.75 + 0.34), k = cols[i];
      s.circle({ x: x + 1.225, y: 1.75, d: 1.3, fill: k.c });
      s.image({ data: await icon(k.ic, "WHITE"), x: x + 1.555, y: 2.08, w: 0.64, h: 0.64, alt: k.t + " icon" });
      s.text(k.t, { x, y: 3.35, w: 3.75, h: 0.55, size: 26, bold: true, align: "center" });
      s.text(k.d, { x: x + 0.3, y: 3.95, w: 3.15, h: 0.9, size: 20, color: "WATER", align: "center" });
    }
    s.notes("Med Amine CHAKROUN", ["Next: classroom sessions with schools and local NGOs, an eco-fact for every catch, and a battle mode with several rafts. [CLICK]"]);
  }
  // ===== 12. Closing =====
  {
    const s = makeSlide({ layout: "DARK", plan: { title: "Closing", time: "0:10" } });
    s.text("MOTTAINAI", { x: X0, y: 0.95, w: CW, h: 1.2, size: 72, font: "head", color: "WHITE", align: "center", valign: "middle" });
    s.text("Every paddle counts. Every piece matters.", { x: X0, y: 2.2, w: CW, h: 0.7, size: 32, font: "head", color: "SUN", align: "center" });
    WASTE.forEach((w, i) => s.circle({ x: W / 2 - 1.7 + i * 0.5, y: 3.15, d: 0.3, fill: w.hex }));
    s.text([{ text: "Rayen TOUIL  ·  Yassine AFFES", breakLine: true }, { text: "Med Amine CHAKROUN  ·  Wajdi ZENTANI" }], { x: X0, y: 3.8, w: CW, h: 0.95, size: 24, color: "WHITE", align: "center" });
    s.text([{ text: "SUP'COM", bold: true, breakLine: true }, { text: "github.com/RayenTouil/Mottainai" }], { x: X0, y: 5.0, w: CW, h: 0.85, size: 20, color: "CYAN", align: "center" });
    s.notes("Med Amine CHAKROUN", ["MOTTAINAI: every paddle counts, every piece matters. Thank you, we're ready for your questions."]);
  }

  // ---------- write pptx ----------
  const pptxPath = path.join(PITCH, "MOTTAINAI_Pitch.pptx");
  await pres.writeFile({ fileName: pptxPath });
  if (APPLY_THEME) await require(APPLY_THEME).applyTheme(pptxPath, THEME);

  // ---------- write html twin ----------
  const fontUrl = fileUrl(ASSET("Mottainai/Assets/_Mottainai/Resources/Fonts/Fredoka-SemiBold.ttf"));
  const pages = htmlSlides.map(({ L, html, n }) => `<section class="slide" style="background:#${hc(L.bg)}">${html.join("\n")}
<div class="t" style="left:${X0}in;top:6.95in;width:6in;height:0.3in;justify-content:center;font-family:Calibri;font-size:12pt;color:#${hc(L.foot)}">${FOOT}</div>
<div class="t" style="left:${W - X0 - 0.6}in;top:6.95in;width:0.6in;height:0.3in;justify-content:center;text-align:right;font-family:Calibri;font-size:12pt;color:#${hc(L.foot)}">${n}</div></section>`);
  const doc = `<!doctype html><html><head><meta charset="utf-8"><title>MOTTAINAI pitch</title><style>
@font-face{font-family:'${HEAD}';src:url('${fontUrl}')}
@page{size:${W}in ${H}in;margin:0}
*{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0}
.slide{position:relative;width:${W}in;height:${H}in;overflow:hidden;page-break-after:always;break-after:page}
.t{position:absolute;display:flex;flex-direction:column;line-height:1.2;white-space:normal;overflow-wrap:normal}
</style></head><body>${pages.join("\n")}</body></html>`;
  fs.writeFileSync(path.join(HTML_DIR, "deck.html"), doc);

  // ---------- SCRIPT.md ----------
  const spoken = script.filter(x => !x.hidden && !x.demo);
  const spokenWords = spoken.reduce((a, b) => a + b.words, 0);
  const demoSec = 60, totalSec = Math.round(spokenWords / 140 * 60) + demoSec;
  const mmss = s => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, "0")}`;
  let md = `# MOTTAINAI — pitch script (Green Hackathon 2026)\n\nTarget: 4:10 + up to 20 s of demo overrun (4:30 max). Pace: about 140 words per minute.\n`;
  md += `Spoken words outside the demo: ${spokenWords} (${mmss(spokenWords / 140 * 60)}) + live demo 1:00 = **${mmss(totalSec)}**.\n\n`;
  md += `Before the talk: install \`Mottainai/Assets/_Mottainai/Resources/Fonts/Fredoka-SemiBold.ttf\` on the presenting PC (titles use it; without it PowerPoint substitutes a default font). Body text uses Calibri (Nunito Sans is not in the repo).\n\n`;
  md += `| Slide | Title | Time | Speaker | Words |\n|---|---|---|---|---|\n`;
  const label = x => (x.n <= 6 ? String(x.n) : x.n === 7 ? "6b" : String(x.n - 1));
  for (const x of script) md += `| ${label(x)} | ${x.title} | ${x.time} | ${x.speaker} | ${x.demo ? "demo" : x.words} |\n`;
  md += `\nCues: [CLICK] = next slide · [START DEMO] = switch the projector to the phone · [HAND OVER TO …] = next speaker steps in.\n`;
  for (const x of script) {
    md += `\n## ${label(x)}. ${x.title} — ${x.time} — ${x.speaker}${x.hidden ? " (hidden, Plan B only)" : ""}\n\n${x.text}\n`;
    if (x.extra) md += `\n${x.extra.split("\n").map(l => "- " + l).join("\n")}\n`;
  }
  md += `
## Plan B (demo or network fails)

- **One phone fails:** keep going with one phone. The server fills the two other seats with bots (1 human + 2 bots works).
- **Mobile network fails:** switch the phones to a Wi-Fi hotspot and retry once (the server is on the Internet, any network works).
- **Still no game after 15 s:** in slideshow mode type \`7\` + Enter to open the hidden slide 6b and say:
  "The network is not on our side today, so here is the same match: the lobby where each phone picks a role, the river in play with the score and the timer, and the results screen with waste per bin, accuracy and kilograms cleaned." Then type \`8\` + Enter and continue with the architecture slide.
- **Projector mirroring fails:** hold the phone up to the jury and narrate; skip the Net HUD.

## Likely jury questions

1. **Why a game?** Kids learn sorting by doing, not by listening. Every waste uses its real bin color, and the raft only moves when the team cooperates.
2. **What is the real impact?** Today: a playable prototype that teaches the 7 bin colors through play. Next: a classroom pilot with a short sorting quiz before and after, to measure what kids learned.
3. **Why 5G SA and not Wi-Fi?** Paddlers must sync within 0.25 s. On Wi-Fi we measured 71 ms average with spikes (p95 88 ms, jitter 16 ms). 5G SA and a latency slice aim for stable low latency outdoors and in schools without good Wi-Fi. The app already detects 5G SA and requests the slice; the 5G SA measurement is our next test.
4. **Data privacy for kids?** No account, no email, no location: a display name and a random token only. The server stores network test numbers (device model, network type, latency, bandwidth), not personal data. A school pilot would add parental consent.
5. **Business model?** Free for schools. Ideas for next: sponsorship by municipalities, NGOs and recycling companies, and cosmetic rewards. Not built yet.
6. **Next steps?** Measure on 5G SA, add eco-facts per catch and team voice chat, run a classroom pilot, then Battle mode (several rafts, team vs team).

## Sources

- WWF (2019), *Stop the Flood of Plastic: How Mediterranean countries can save their sea*: 0.57 Mt of plastic enter the Mediterranean each year, ≈ 33,800 bottles per minute.
- OECD (2022), *Global Plastics Outlook*: 109 Mt of plastic accumulated in rivers, 30 Mt in seas and oceans.
- Game numbers: \`server/src/tuning.json\` (2:00, 1 hit, sync 0.25 s / ×1.3, shield 6 s, combo 3 in 3 s ×1.5, scores).
- Network numbers: \`/api/telemetry.csv\` (Infinix phone on Wi-Fi → room CDG: 70.8 ms avg, p95 87.6, jitter 16.3) and \`server/tools/fakeclient.ts\` on 22 Nov 2026 (room MRS: 43.4 ms avg, p95 47.3, 60 snapshots/s, 226 kbps).
- Chart: the same fakeclient match (waste counts 30/11/19/10/9/0/10).
`;
  fs.writeFileSync(path.join(PITCH, "SCRIPT.md"), md);
  console.log(JSON.stringify({ pptx: pptxPath, html: path.join(HTML_DIR, "deck.html"), slides: slideNo, spokenWords, total: mmss(totalSec) }));
})().catch(e => { console.error(e); process.exit(1); });
