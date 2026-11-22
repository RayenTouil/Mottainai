// Final jury deck: merges the application pitch (Downloads/"Green Hackathon - THE PITCH - Mottainai.pptx", Nov 2)
// with Pitch/MOTTAINAI_Pitch.pptx. Writes Pitch/MOTTAINAI_Final.pptx, an HTML twin (print it to PDF with Edge)
// and Pitch/FINAL_GUIDE.md (analysis, structure, slide content, diagrams, script, demo plan, jury Q&A).
// Needs pptxgenjs, jszip, react, react-dom, react-icons, sharp on NODE_PATH. Optional APPLY_THEME = path to apply_theme.js.
// Usage: node Pitch/build_final.js <html-out-dir>
const fs = require("fs");
const path = require("path");
const pptxgen = require("pptxgenjs");
const JSZip = require("jszip");
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");
const sharp = require("sharp");
const fa = require("react-icons/fa");
const gi = require("react-icons/gi");

const ROOT = path.resolve(__dirname, "..");
const PITCH = path.join(ROOT, "Pitch");
const HTML_DIR = path.resolve(process.argv[2] || path.join(PITCH, "build"));
fs.mkdirSync(HTML_DIR, { recursive: true });

const W = 13.333, H = 7.5, X0 = 0.7, CW = 11.93;
const HEAD = "Fredoka Light SemiBold", BODY = "Calibri", JP = "Yu Gothic UI";
const HEX = { DEEP: "073B43", TURQ: "19B8B0", RIVER: "63CDE0", GREEN: "72C78A", WHITE: "FFFFFF", AQUA: "E9F7F2", MUTED: "2F6B73", SUN: "FFC857", CORAL: "FF8360", CARD: "FFFFFF", CLOUDZONE: "D4EFEC", CHIPDARK: "0F5560" };
const THEME = {
  name: "Mottainai River", headFontFace: HEAD, bodyFontFace: BODY,
  colors: { dk1: HEX.DEEP, lt1: HEX.WHITE, dk2: HEX.MUTED, lt2: HEX.AQUA, accent1: HEX.TURQ, accent2: HEX.GREEN, accent3: HEX.RIVER, accent4: HEX.CORAL, accent5: HEX.SUN, accent6: "9B4DFF", hlink: HEX.MUTED, folHlink: HEX.DEEP },
};
const SCH = { DEEP: "text1", MUTED: "text2", WHITE: "background1", AQUA: "background2", TURQ: "accent1", GREEN: "accent2", RIVER: "accent3", CORAL: "accent4", SUN: "accent5" };
const WASTE = [
  { name: "Plastic", pts: "+10", hex: "F2C200" }, { name: "Paper", pts: "+10", hex: "1E6FD9" }, { name: "Glass", pts: "+15", hex: "2E9E44" },
  { name: "Organic", pts: "+5", hex: "7A4A21" }, { name: "Metal", pts: "+20", hex: "8C959F" }, { name: "Valuable", pts: "+100", hex: "9B4DFF" },
  { name: "Toxic", pts: "−30", hex: "D62828" },
];

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE";
pres.title = "MOTTAINAI — final jury presentation";
pres.author = "Team 5le9a Deyra (SUP'COM)";
pres.theme = { headFontFace: HEAD, bodyFontFace: BODY };
const C = pres.SchemeColor;
const pc = k => (SCH[k] ? C[SCH[k]] : HEX[k] || k);
const hc = k => HEX[k] || k;

const FOOT = "MOTTAINAI · Green Hackathon 2026 · 5le9a Deyra";
const LAYOUTS = {
  LIGHT: { bg: "AQUA", foot: "MUTED", title: { x: X0, y: 0.45, w: CW, h: 0.85, size: 38, color: "DEEP" } },
  DARK: { bg: "DEEP", foot: "RIVER", title: null },
};
for (const [name, L] of Object.entries(LAYOUTS)) {
  const objects = [{ text: { text: FOOT, options: { x: X0, y: 6.95, w: 7, h: 0.3, fontSize: 12, color: pc(L.foot), margin: 0, valign: "middle" } } }];
  if (L.title) objects.push({ placeholder: { options: { name: "title", type: "title", x: L.title.x, y: L.title.y, w: L.title.w, h: L.title.h, fontFace: HEAD, fontSize: L.title.size, color: pc(L.title.color), valign: "middle", align: "left", margin: 0 }, text: "" } });
  pres.defineSlideMaster({ title: name, background: { color: hc(L.bg) }, objects, slideNumber: { x: W - X0 - 0.6, y: 6.95, w: 0.6, h: 0.3, fontFace: BODY, fontSize: 12, color: hc(L.foot), align: "right", margin: 0 } });
}

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

// ---------- dual renderer: every call draws into the pptx slide and into the HTML twin ----------
const htmlSlides = [], metas = [];
let slideNo = 0;
const esc = s => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const fileUrl = p => "file:///" + p.replace(/\\/g, "/").replace(/ /g, "%20");
const countWords = t => t.replace(/\[[^\]]*\]/g, " ").split(/\s+/).filter(w => /[A-Za-z0-9]/.test(w)).length;

function makeSlide({ layout, section, hidden }, meta) {
  slideNo++;
  if (section) pres.addSection({ title: section });
  const L = LAYOUTS[layout];
  const s = pres.addSlide({ masterName: layout, sectionTitle: section || undefined });
  if (hidden) s.hidden = true;
  const html = [], n = slideNo;
  let k = 0;
  const id = kind => `${kind}-${n}-${++k}`;
  const box = o => `left:${o.x}in;top:${o.y}in;width:${o.w}in;height:${o.h}in;`;
  meta.n = n; meta.hidden = !!hidden; metas.push(meta);
  const api = {
    title(text) {
      s.addText(text, { placeholder: "title" });
      const t = L.title;
      html.push(`<div class="t" style="${box(t)}font-family:'${HEAD}';font-size:${t.size}pt;color:#${hc(t.color)};justify-content:center;">${esc(text)}</div>`);
    },
    text(runs, o) {
      const arr = typeof runs === "string" ? [{ text: runs }] : runs;
      const base = { fontSize: o.size || 20, color: o.color || "DEEP", bold: !!o.bold, italic: !!o.italic, font: o.font || "body" };
      const face = f => (f === "head" ? HEAD : f === "jp" ? JP : BODY);
      const pptRuns = arr.map(r => {
        const m = { ...base, ...r };
        const opt = { fontSize: m.fontSize, color: pc(m.color), bold: m.bold, italic: m.italic, fontFace: face(m.font) };
        if (o.spacing) opt.charSpacing = o.spacing;
        if (r.breakLine) opt.breakLine = true;
        return { text: r.text, options: opt };
      });
      s.addText(pptRuns, { x: o.x, y: o.y, w: o.w, h: o.h, margin: 0, valign: o.valign || "top", align: o.align || "left", isTextBox: true, fit: "none", objectName: id("text") });
      const inner = arr.map(r => {
        const m = { ...base, ...r };
        const fam = m.font === "head" ? `'${HEAD}'` : m.font === "jp" ? `'${JP}','Meiryo'` : `'${BODY}'`;
        return `<span style="font-family:${fam};font-size:${m.fontSize}pt;color:#${hc(m.color)};font-weight:${m.bold ? 700 : 400};font-style:${m.italic ? "italic" : "normal"};${o.spacing ? `letter-spacing:${o.spacing}pt;` : ""}">${esc(m.text)}</span>${r.breakLine ? "<br>" : ""}`;
      }).join("");
      const jc = { top: "flex-start", middle: "center", bottom: "flex-end" }[o.valign || "top"];
      html.push(`<div class="t" style="${box(o)}justify-content:${jc};text-align:${o.align || "left"};"><div>${inner}</div></div>`);
    },
    rect(o) {
      const shape = o.radius ? pres.shapes.ROUNDED_RECTANGLE : pres.shapes.RECTANGLE;
      const opt = { x: o.x, y: o.y, w: o.w, h: o.h, fill: { color: pc(o.fill), transparency: o.transparency || 0 }, line: o.line ? { color: pc(o.line), width: o.lineW || 1.5 } : { type: "none" }, objectName: id("shape") };
      if (o.radius) opt.rectRadius = o.radius;
      if (o.shadow) opt.shadow = { type: "outer", color: "073B43", opacity: 0.12, blur: 8, offset: 2, angle: 90 };
      s.addShape(shape, opt);
      html.push(`<div style="position:absolute;${box(o)}background:#${hc(o.fill)};opacity:${1 - (o.transparency || 0) / 100};border-radius:${o.radius ? o.radius + "in" : 0};${o.line ? `border:${o.lineW || 1.5}pt solid #${hc(o.line)};box-sizing:border-box;` : ""}${o.shadow ? "box-shadow:0 2pt 8pt rgba(7,59,67,.12);" : ""}"></div>`);
    },
    circle(o) {
      s.addShape(pres.shapes.OVAL, { x: o.x, y: o.y, w: o.d, h: o.d, fill: { color: pc(o.fill) }, line: { type: "none" }, objectName: id("circle") });
      html.push(`<div style="position:absolute;${box({ x: o.x, y: o.y, w: o.d, h: o.d })}background:#${hc(o.fill)};border-radius:50%;"></div>`);
    },
    image(o) {
      const opt = { x: o.x, y: o.y, w: o.w, h: o.h, altText: o.alt, objectName: id("image") };
      if (o.data) opt.data = o.data; else opt.path = o.path;
      if (o.cover) opt.sizing = { type: "cover", w: o.w, h: o.h };
      s.addImage(opt);
      html.push(`<img alt="${esc(o.alt)}" src="${o.data ? "data:" + o.data : fileUrl(o.path)}" style="position:absolute;${box(o)}object-fit:${o.cover ? "cover" : "contain"};">`);
    },
    async iconIn(Comp, fill, x, y, d, color = "WHITE", ratio = 0.5) {
      api.circle({ x, y, d, fill });
      const sz = d * ratio;
      api.image({ data: await icon(Comp, color), x: x + (d - sz) / 2, y: y + (d - sz) / 2, w: sz, h: sz, alt: (Comp.name || "icon").replace(/^(Fa|Gi)/, "") + " icon" });
    },
    notes(text, extra) {
      meta.script = text; meta.words = countWords(text); meta.extra = extra || "";
      s.addNotes(`SPEAKER: ${meta.speaker} · ${meta.time}\n\n${text}${extra ? "\n\n" + extra : ""}`);
    },
  };
  htmlSlides.push({ L, html, n });
  return api;
}

const ASSET = p => path.join(ROOT, p);
const IMG = {
  ref: ASSET("Docs/reference_v3_master.jpg"),
  banner: ASSET("Docs/media/mottainai_banner.gif"),
  phone: ASSET("Docs/art_v3/merge_phone.png"),
  lobby: ASSET("Docs/art_v3/P5_pc_lobby.png"),
  results: ASSET("Docs/art_v3/P5_pc_results.png"),
};

(async () => {
  // ===== 1. Hook =====
  {
    const s = makeSlide({ layout: "DARK", section: "I · Problem" }, {
      title: "The sea is choking", time: "0:20", speaker: "Yassine AFFES",
      onSlide: ["THE MEDITERRANEAN · EVERY YEAR", "570,000 tonnes of plastic", "≈ 33,800 plastic bottles every minute", "What if kids could fix it — by playing?", "Source: WWF, Stop the Flood of Plastic (2019)"],
      visual: "Full-bleed MOTTAINAI concept art (river flowing to the sea) under a 78 % deep-teal veil; huge white number top-left, coral sub-line, question at mid-height with 'by playing?' in green.",
      images: ["Docs/reference_v3_master.jpg"], animation: "Fade in. Optional: number appears first, question on click.",
    });
    s.image({ path: IMG.ref, x: 0, y: 0, w: W, h: H, cover: true, alt: "Concept art: a raft on a river with floating waste flowing to the sea" });
    s.rect({ x: 0, y: 0, w: W, h: H, fill: "DEEP", transparency: 22 });
    s.text("THE MEDITERRANEAN · EVERY YEAR", { x: X0, y: 1.0, w: 8, h: 0.45, size: 20, color: "RIVER", bold: true });
    s.text([{ text: "570,000", fontSize: 88, color: "WHITE", font: "head" }, { text: "  tonnes of plastic", fontSize: 32, color: "WHITE" }], { x: X0, y: 1.45, w: 11.5, h: 1.45, valign: "bottom" });
    s.text("≈ 33,800 plastic bottles every minute", { x: X0, y: 3.05, w: 10, h: 0.55, size: 28, color: "CORAL", bold: true });
    s.text([{ text: "What if kids could fix it — ", color: "WHITE" }, { text: "by playing?", color: "GREEN" }], { x: X0, y: 4.5, w: 11.8, h: 0.9, size: 40, font: "head" });
    s.text("Source: WWF, Stop the Flood of Plastic (2019)", { x: X0, y: 6.45, w: 8, h: 0.3, size: 12, color: "WHITE" });
    s.notes("Every year, five hundred seventy thousand tonnes of plastic enter the Mediterranean. That's almost thirty-four thousand bottles every minute, right on our doorstep. We are 5le9a Deyra, four telecom engineering students from SUP'COM. And we asked one question: what if kids could fix this, by playing? [CLICK]");
  }
  // ===== 2. Problem =====
  {
    const s = makeSlide({ layout: "LIGHT" }, {
      title: "Plastic starts upstream", time: "0:20", speaker: "Yassine AFFES",
      onSlide: ["Plastic starts upstream", "109 Mt of plastic already sit in the world's rivers", "Source: OECD, Global Plastics Outlook (2022)", "Streets → Rivers → Sea", "Ages 7–12: when habits form", "Sorting is taught, rarely practised"],
      visual: "Left: giant stat. Right: three icon circles linked by arrows (streets, rivers, sea), then two white cards with icons (child, teacher).",
      images: ["icons only"], animation: "Fade. Cards appear after the stat (optional).",
    });
    s.title("Plastic starts upstream");
    s.text([{ text: "109", fontSize: 80, color: "DEEP", font: "head" }, { text: " Mt", fontSize: 40, color: "DEEP", font: "head" }], { x: X0, y: 2.1, w: 5.4, h: 1.4, valign: "bottom" });
    s.text("of plastic already sit in the world's rivers", { x: X0, y: 3.6, w: 5.2, h: 0.9, size: 24 });
    s.text("Source: OECD, Global Plastics Outlook (2022)", { x: X0, y: 4.65, w: 5.4, h: 0.3, size: 12, color: "MUTED" });
    const steps = [{ ic: fa.FaCity, label: "Streets", fill: "MUTED" }, { ic: fa.FaWater, label: "Rivers", fill: "TURQ" }, { ic: gi.GiWaveCrest, label: "Sea", fill: "DEEP" }];
    const arrow = await icon(fa.FaArrowRight, "MUTED");
    for (let i = 0; i < 3; i++) {
      const cx = 7.1 + i * 1.95;
      await s.iconIn(steps[i].ic, steps[i].fill, cx, 1.6, 1.3);
      s.text(steps[i].label, { x: cx - 0.3, y: 3.0, w: 1.9, h: 0.4, size: 20, bold: true, align: "center" });
      if (i < 2) s.image({ data: arrow, x: cx + 1.47, y: 2.09, w: 0.32, h: 0.32, alt: "arrow" });
    }
    const cards = [{ ic: fa.FaChild, c: "GREEN", t: "Ages 7–12: when habits form" }, { ic: fa.FaChalkboardTeacher, c: "CORAL", t: "Sorting is taught, rarely practised" }];
    for (let i = 0; i < 2; i++) {
      const y = 3.75 + i * 1.2;
      s.rect({ x: 7.0, y, w: 5.63, h: 1.0, fill: "CARD", radius: 0.18, shadow: true });
      await s.iconIn(cards[i].ic, cards[i].c, 7.2, y + 0.18, 0.64);
      s.text(cards[i].t, { x: 8.05, y, w: 4.45, h: 1.0, size: 22, bold: true, valign: "middle" });
    }
    s.notes("This plastic doesn't start at sea. It starts in our streets and rivers. The OECD counts one hundred nine million tonnes sitting in rivers. Seven to twelve is when habits form. But sorting is taught in lectures, and lectures don't stick. Kids learn by doing. [CLICK] [HAND OVER TO WAJDI]");
  }
  // ===== 3. Idea =====
  {
    const s = makeSlide({ layout: "DARK", section: "II · Idea" }, {
      title: "MOTTAINAI", time: "0:20", speaker: "Wajdi ZENTANI",
      onSlide: ["MOTTAINAI", "もったいない · “What a waste!”", "A real-time co-op mobile game: 3 players, 1 raft, 1 polluted river.", "How the idea evolved: Application — solo runner → Prototype — 3-player co-op raft", "Concept art (target look)"],
      visual: "Deep-teal slide. Left: wordmark, Japanese meaning, one-liner, evolution chips. Right: the animated banner GIF (concept art) with bin-color dots under it.",
      images: ["Docs/media/mottainai_banner.gif"], animation: "Fade. The GIF animates in PowerPoint.",
    });
    s.text("MOTTAINAI", { x: X0, y: 1.15, w: 5.8, h: 1.1, size: 64, font: "head", color: "WHITE" });
    s.text([{ text: "もったいない", font: "jp" }, { text: " · “What a waste!”" }], { x: X0, y: 2.35, w: 5.8, h: 0.55, size: 26, color: "RIVER" });
    s.text("A real-time co-op mobile game: 3 players, 1 raft, 1 polluted river.", { x: X0, y: 3.15, w: 5.7, h: 1.0, size: 24, color: "WHITE" });
    s.text("How the idea evolved", { x: X0, y: 4.45, w: 5.6, h: 0.35, size: 16, color: "RIVER" });
    s.rect({ x: X0, y: 4.85, w: 2.45, h: 0.85, fill: "CHIPDARK", radius: 0.16 });
    s.text([{ text: "Application", bold: true, breakLine: true }, { text: "solo runner" }], { x: X0, y: 4.85, w: 2.45, h: 0.85, size: 18, color: "WHITE", align: "center", valign: "middle" });
    s.image({ data: await icon(fa.FaArrowRight, "RIVER"), x: X0 + 2.6, y: 5.1, w: 0.35, h: 0.35, alt: "arrow" });
    s.rect({ x: X0 + 3.1, y: 4.85, w: 2.75, h: 0.85, fill: "TURQ", radius: 0.16 });
    s.text([{ text: "Prototype", bold: true, breakLine: true }, { text: "3-player co-op raft" }], { x: X0 + 3.1, y: 4.85, w: 2.75, h: 0.85, size: 18, color: "DEEP", align: "center", valign: "middle" });
    s.image({ path: IMG.banner, x: 6.7, y: 1.3, w: 5.93, h: 3.34, alt: "Animated MOTTAINAI banner: three kids on a raft collecting waste" });
    s.text("Concept art (target look)", { x: 6.7, y: 4.75, w: 3.5, h: 0.3, size: 12, color: "RIVER", italic: true });
    WASTE.forEach((w, i) => s.circle({ x: 6.7 + i * 0.5, y: 5.3, d: 0.3, fill: w.hex }));
    s.notes("So we built MOTTAINAI. In Japanese, it means 'what a waste!'. Our first idea was a solo runner. We turned it into a cooperative game, because cleaning a river is teamwork. Three players share one raft. Nobody wins alone. [CLICK]");
  }
  // ===== 4. Experience =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "III · Experience" }, {
      title: "Three roles, one raft", time: "0:25", speaker: "Wajdi ZENTANI",
      onSlide: ["Three roles, one raft", "Left paddler — Swipe ↓ paddle · ↑ brake", "Right paddler — Swipe ↓ paddle · ↑ brake", "Collector — Tap waste: throw the net", "+30 % SYNC BOOST — both paddlers stroke within 0.25 s", "Missing players? Bots fill the seats.", "Caption: Real phone during a full match (collector view)"],
      visual: "Left: real phone screenshot of a match. Right: three role rows (icon circle, name, gesture). Bottom: deep-teal band with the +30 % sync boost and the bot note.",
      images: ["Docs/art_v3/merge_phone.png"], animation: "Fade. Optional: role rows appear one by one on click.",
    });
    s.title("Three roles, one raft");
    s.image({ path: IMG.phone, x: X0, y: 1.55, w: 7.2, h: 3.32, alt: "Real phone screenshot: the yellow raft on the river, waste with bin-color rings, score 324, combo popup" });
    s.text("Real phone during a full match (collector view)", { x: X0, y: 4.9, w: 7.2, h: 0.28, size: 12, color: "MUTED" });
    const roles = [
      { name: "Left paddler", ic: gi.GiCanoe, c: "DEEP", g: "Swipe ↓ paddle · ↑ brake" },
      { name: "Right paddler", ic: gi.GiCanoe, c: "TURQ", g: "Swipe ↓ paddle · ↑ brake" },
      { name: "Collector", ic: gi.GiFishingNet, c: "GREEN", g: "Tap waste: throw the net" },
    ];
    for (let i = 0; i < 3; i++) {
      const y = 1.55 + i * 1.12, r = roles[i];
      s.rect({ x: 8.25, y, w: 4.38, h: 1.0, fill: "CARD", radius: 0.16, shadow: true });
      await s.iconIn(r.ic, r.c, 8.42, y + 0.15, 0.7);
      s.text(r.name, { x: 9.3, y: y + 0.08, w: 3.2, h: 0.45, size: 22, bold: true, valign: "middle" });
      s.text(r.g, { x: 9.3, y: y + 0.52, w: 3.25, h: 0.4, size: 18, color: "MUTED" });
    }
    s.rect({ x: X0, y: 5.3, w: CW, h: 1.3, fill: "DEEP", radius: 0.18 });
    s.text("+30 %", { x: X0 + 0.35, y: 5.3, w: 2.6, h: 1.3, size: 54, color: "SUN", font: "head", valign: "middle" });
    s.text([{ text: "SYNC BOOST", bold: true, color: "SUN", breakLine: true }, { text: "both paddlers stroke within 0.25 s", color: "WHITE" }], { x: X0 + 3.05, y: 5.3, w: 4.9, h: 1.3, size: 22, valign: "middle" });
    s.image({ data: await icon(fa.FaRobot, "RIVER"), x: X0 + 8.05, y: 5.65, w: 0.6, h: 0.6, alt: "bot icon" });
    s.text("Missing players? Bots fill the seats.", { x: X0 + 8.8, y: 5.3, w: 2.95, h: 1.3, size: 20, color: "WHITE", valign: "middle" });
    s.notes("Here's how it plays. The left paddler swipes down to push forward and turn right. The right paddler does the opposite. The collector taps floating waste to throw a net. When both paddlers stroke within a quarter second, the raft gets a sync boost: thirty percent more thrust. Missing a friend? A bot takes the seat. [CLICK]");
  }
  // ===== 5. Loop =====
  {
    const s = makeSlide({ layout: "LIGHT" }, {
      title: "The gameplay loop", time: "0:20", speaker: "Wajdi ZENTANI",
      onSlide: ["The gameplay loop", "Paddle in sync (swipe together) → Catch waste (tap → net) → Score (bin color · combo ×1.5) → Survive (1 hit = game over)", "↻ Repeat for 2:00 · power-ups: shield 6 s, magnet, turbo", "Plastic +10 · Paper +10 · Glass +15 · Organic +5 · Metal +20 · Valuable +100 · Toxic −30", "End of run → waste per bin · accuracy · kg of river cleaned"],
      visual: "Four icon nodes linked by arrows, a repeat line, the seven bin-color circles with points, and a deep-teal results band.",
      images: ["icons only"], animation: "Fade. Optional: nodes appear left to right.",
    });
    s.title("The gameplay loop");
    const nodes = [
      { ic: gi.GiCanoe, c: "DEEP", t: "Paddle in sync", d: "swipe together" },
      { ic: gi.GiFishingNet, c: "TURQ", t: "Catch waste", d: "tap → net" },
      { ic: fa.FaStar, c: "GREEN", t: "Score", d: "bin color · combo ×1.5" },
      { ic: fa.FaShieldAlt, c: "CORAL", t: "Survive", d: "1 hit = game over" },
    ];
    const arrow = await icon(fa.FaArrowRight, "MUTED");
    for (let i = 0; i < 4; i++) {
      const x = X0 + i * 3.16, nd = nodes[i];
      await s.iconIn(nd.ic, nd.c, x + 0.7, 1.55, 1.05);
      s.text(nd.t, { x, y: 2.72, w: 2.45, h: 0.45, size: 22, bold: true, align: "center" });
      s.text(nd.d, { x, y: 3.17, w: 2.45, h: 0.62, size: 18, color: "MUTED", align: "center" });
      if (i < 3) s.image({ data: arrow, x: x + 2.62, y: 1.9, w: 0.36, h: 0.36, alt: "arrow" });
    }
    s.text("↻  Repeat for 2:00 · power-ups: shield 6 s, magnet, turbo", { x: X0, y: 3.9, w: CW, h: 0.4, size: 18, color: "MUTED", align: "center", bold: true });
    const step = CW / 7;
    WASTE.forEach((w, i) => {
      const cx = X0 + i * step + (step - 0.8) / 2;
      s.circle({ x: cx, y: 4.5, d: 0.8, fill: w.hex });
      s.text(w.pts, { x: cx - 0.2, y: 4.5, w: 1.2, h: 0.8, size: 18, bold: true, color: "WHITE", align: "center", valign: "middle" });
      s.text(w.name, { x: X0 + i * step, y: 5.33, w: step, h: 0.35, size: 18, align: "center" });
    });
    s.rect({ x: X0, y: 5.9, w: CW, h: 0.75, fill: "DEEP", radius: 0.16 });
    s.text([{ text: "END OF RUN  →  ", bold: true, color: "SUN" }, { text: "waste per bin · accuracy · kg of river cleaned", color: "WHITE" }], { x: X0, y: 5.9, w: CW, h: 0.75, size: 20, align: "center", valign: "middle" });
    s.notes("Each run lasts two minutes. Catch waste, and each bin color scores. Chain three catches for a combo. One hit on a rock or a shark ends the run. Toxic waste costs points: you learn not to touch it. Then you see the kilos you cleaned. [CLICK] [HAND OVER TO RAYEN]");
  }
  // ===== 6. Live demo =====
  {
    const s = makeSlide({ layout: "DARK", section: "V · Proof" }, {
      title: "Live demo", time: "0:45", speaker: "Rayen TOUIL", demo: true,
      onSlide: ["LIVE DEMO", "2 phones + 1 bot · real time over Cloudflare at 60 Hz", "1 Start — roles already picked", "2 Paddle in sync — watch the boost", "3 Catch waste — bin colors score", "4 Results — kg of river cleaned"],
      visual: "Deep-teal stage slide: big spaced title, sub-line, four numbered steps (the demo script the jury can follow).",
      images: ["icons only"], animation: "Fade, then switch the projector to the mirrored phone.",
    });
    s.text("LIVE DEMO", { x: X0, y: 1.0, w: CW, h: 1.3, size: 80, spacing: 8, font: "head", color: "WHITE", align: "center", valign: "middle" });
    s.text("2 phones + 1 bot · real time over Cloudflare at 60 Hz", { x: X0, y: 2.4, w: CW, h: 0.5, size: 24, color: "RIVER", align: "center" });
    const steps = [["Start", "roles already picked"], ["Paddle in sync", "watch the boost"], ["Catch waste", "bin colors score"], ["Results", "kg of river cleaned"]];
    const fills = ["TURQ", "RIVER", "GREEN", "SUN"];
    for (let i = 0; i < 4; i++) {
      const x = X0 + i * (2.8 + 0.243);
      s.circle({ x: x + 0.9, y: 3.55, d: 1.0, fill: fills[i] });
      s.text(String(i + 1), { x: x + 0.9, y: 3.55, w: 1.0, h: 1.0, size: 40, font: "head", color: "DEEP", align: "center", valign: "middle" });
      s.text(steps[i][0], { x, y: 4.7, w: 2.8, h: 0.45, size: 22, bold: true, color: "WHITE", align: "center" });
      s.text(steps[i][1], { x, y: 5.15, w: 2.8, h: 0.4, size: 18, color: "RIVER", align: "center" });
    }
    s.notes("Let's prove it live. [START DEMO] Two phones, one bot. I'm the left paddler; Wajdi is the collector. Watch: when we stroke together, the raft surges. Wajdi taps a bottle, the net flies, and it scores. The server runs all of this sixty times a second. [~25 s of play, then steer into a rock] Rock ahead... game over. And here is our result: waste per bin, accuracy, and kilos of river cleaned. [CLICK]",
      "DEMO (45 s): before the talk both phones are in the Quick Play lobby with roles picked (A = LEFT PADDLER, B = COLLECTOR, bot = RIGHT); Wi-Fi OFF, mobile data ON; screen timeout 10 min; Phone A mirrored to the projector.\n0:00 press START on Phone A (3 s countdown) · 0:05 three synced strokes, point at the SYNC boost · 0:12 collector catches 2–3 items, point at the bin-color rings and the score · 0:25 point at the Net pill (network + RTT) · 0:30 steer into a rock → GAME OVER · 0:35 results screen: waste per bin, accuracy, kg cleaned · 0:45 [CLICK].\nBACKUP: if nothing moves after 10 s, type 7 + Enter (hidden slide) and play the recorded video Pitch/media/demo_backup.mp4.");
  }
  // ===== 6b. Backup (hidden) =====
  {
    const s = makeSlide({ layout: "LIGHT", hidden: true }, {
      title: "Demo backup (hidden)", time: "—", speaker: "Rayen TOUIL",
      onSlide: ["Demo backup", "Gameplay · Lobby · Results (real screenshots)", "Backup video: Pitch/media/demo_backup.mp4"],
      visual: "Hidden slide: big phone gameplay shot, lobby and results shots, chips labelling each.",
      images: ["Docs/art_v3/merge_phone.png", "Docs/art_v3/P5_pc_lobby.png", "Docs/art_v3/P5_pc_results.png"], animation: "None.",
    });
    s.title("Demo backup");
    s.image({ path: IMG.phone, x: X0, y: 1.55, w: 7.3, h: 3.37, alt: "Phone gameplay screenshot" });
    s.image({ path: IMG.lobby, x: 8.35, y: 1.55, w: 4.28, h: 1.93, alt: "Lobby screenshot: role selection" });
    s.image({ path: IMG.results, x: 8.35, y: 3.9, w: 4.28, h: 1.93, alt: "Results screenshot: score and waste per bin" });
    const chip = (x, y, t) => { s.rect({ x, y, w: 1.75, h: 0.45, fill: "DEEP", radius: 0.12 }); s.text(t, { x, y, w: 1.75, h: 0.45, size: 18, bold: true, color: "WHITE", align: "center", valign: "middle" }); };
    chip(X0 + 0.15, 4.33, "Gameplay"); chip(8.5, 2.88, "Lobby"); chip(8.5, 5.23, "Results");
    s.text("Backup video: Pitch/media/demo_backup.mp4 (insert before the talk)", { x: X0, y: 5.3, w: 7.3, h: 0.45, size: 18, color: "MUTED" });
    s.notes("Plan B only. The network is not on our side today, so here is the same match: the lobby where each phone picks a role, the river in play, and the results with waste per bin and kilos cleaned. [type 8 + Enter]");
  }
  // ===== 7. Architecture =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "IV · Engineering" }, {
      title: "One authoritative game room per match", time: "0:25", speaker: "Rayen TOUIL",
      onSlide: ["One authoritative game room per match", "Phone 1/2/3 · Unity", "wss · binary", "Cloudflare · edge in Marseille (MRS)", "Worker — routing · login token · TLS", "Game room — Durable Object · 60 Hz sim · bots · lag compensation", "Content pack — 36 MB static assets, sha256", "Telemetry — SQLite → /api/telemetry.csv", "Events up · 60 Hz snapshots down · Server decides · TLS + random tokens"],
      visual: "Architecture diagram: three phone nodes on the left, a light-turquoise Cloudflare zone holding the Worker, the game-room Durable Object, the content pack and the telemetry store; chips underneath.",
      images: ["icons only"], diagram: "See section D, diagram 1.", animation: "Fade. Optional: phones, then Worker, then room appear on click.",
    });
    s.title("One authoritative game room per match");
    for (let i = 0; i < 3; i++) {
      const y = 1.75 + i * 1.1;
      s.rect({ x: X0, y, w: 2.75, h: 0.9, fill: "CARD", radius: 0.16, shadow: true });
      await s.iconIn(fa.FaMobileAlt, "DEEP", X0 + 0.15, y + 0.15, 0.6);
      s.text(`Phone ${i + 1} · Unity`, { x: X0 + 0.9, y, w: 1.8, h: 0.9, size: 20, bold: true, valign: "middle" });
    }
    s.text("gestures · rendering · prediction", { x: X0, y: 5.05, w: 2.75, h: 0.5, size: 16, color: "MUTED", align: "center" });
    s.text("wss", { x: 3.5, y: 2.45, w: 1.0, h: 0.4, size: 20, bold: true, align: "center" });
    s.image({ data: await icon(fa.FaExchangeAlt, "MUTED"), x: 3.75, y: 2.9, w: 0.5, h: 0.5, alt: "two-way arrow" });
    s.text("binary", { x: 3.5, y: 3.45, w: 1.0, h: 0.35, size: 16, color: "MUTED", align: "center" });
    s.rect({ x: 4.6, y: 1.5, w: 8.03, h: 4.15, fill: "CLOUDZONE", radius: 0.25 });
    s.text("Cloudflare · edge in Marseille (MRS)", { x: 4.85, y: 1.6, w: 7.5, h: 0.4, size: 18, bold: true, color: "MUTED" });
    const boxes = [
      { x: 4.85, w: 3.1, ic: fa.FaCloud, c: "TURQ", t: "Worker", d: "routing · login token · TLS" },
      { x: 9.1, w: 3.3, ic: fa.FaServer, c: "GREEN", t: "Game room", d: "Durable Object · 60 Hz sim · bots · lag compensation" },
    ];
    for (const b of boxes) {
      s.rect({ x: b.x, y: 2.1, w: b.w, h: 2.05, fill: "CARD", radius: 0.16, shadow: true });
      await s.iconIn(b.ic, b.c, b.x + b.w / 2 - 0.35, 2.25, 0.7);
      s.text(b.t, { x: b.x + 0.1, y: 3.0, w: b.w - 0.2, h: 0.42, size: 22, bold: true, align: "center" });
      s.text(b.d, { x: b.x + 0.15, y: 3.42, w: b.w - 0.3, h: 0.7, size: 16, color: "MUTED", align: "center" });
    }
    s.image({ data: await icon(fa.FaArrowRight, "MUTED"), x: 8.3, y: 2.95, w: 0.45, h: 0.45, alt: "arrow" });
    const subs = [{ x: 4.85, w: 3.1, t: "Content pack", d: "36 MB static assets, sha256" }, { x: 9.1, w: 3.3, t: "Telemetry", d: "SQLite → /api/telemetry.csv" }];
    for (const b of subs) {
      s.rect({ x: b.x, y: 4.4, w: b.w, h: 1.0, fill: "CARD", radius: 0.16 });
      s.text([{ text: b.t, bold: true, fontSize: 18, breakLine: true }, { text: b.d, fontSize: 16, color: "MUTED" }], { x: b.x + 0.1, y: 4.4, w: b.w - 0.2, h: 1.0, align: "center", valign: "middle" });
    }
    ["Events up", "60 Hz snapshots down", "Server decides", "TLS + random tokens"].forEach((c, i) => {
      const x = X0 + i * (2.75 + 0.31);
      s.rect({ x, y: 5.95, w: 2.75, h: 0.6, fill: "TURQ", radius: 0.3 });
      s.text(c, { x, y: 5.95, w: 2.75, h: 0.6, size: 18, bold: true, color: "DEEP", align: "center", valign: "middle" });
    });
    s.notes("Under the hood: three Unity phones connect over secure WebSockets to Cloudflare's edge in Marseille. Each match runs in its own game room, a Durable Object that owns the game state: it simulates the river sixty times a second, runs the bots and streams full snapshots back. Phones send only events, like a stroke or a net throw. [CLICK]");
  }
  // ===== 8. Challenges + measurements =====
  {
    const s = makeSlide({ layout: "LIGHT" }, {
      title: "Engineering challenges we solved", time: "0:30", speaker: "Rayen TOUIL",
      onSlide: ["Engineering challenges we solved", "No UDP for games on Cloudflare → compact binary protocol over WebSockets (TLS), events-only uplink", "A 0.25 s sync window → own-raft prediction, adaptive jitter buffer, lag compensation ≤ 150 ms", "Fair play → server-authoritative: phones only send strokes and net throws", "60 Hz snapshots to every phone · 226 kbps download per phone · 43–71 ms round trip, measured · 60 fps average on a test phone", "Source line with dates"],
      visual: "Three white challenge cards (coral PROBLEM label, bold problem, muted answer) above four big-number tiles and a small source line.",
      images: ["icons only"], animation: "Fade. Optional: metrics row appears on click.",
    });
    s.title("Engineering challenges we solved");
    const cards = [
      { ic: fa.FaExchangeAlt, p: "No UDP for games on Cloudflare", a: "Compact binary protocol over WebSockets (TLS), events-only uplink" },
      { ic: fa.FaTachometerAlt, p: "A 0.25 s sync window", a: "Own-raft prediction, adaptive jitter buffer, lag compensation ≤ 150 ms" },
      { ic: fa.FaBalanceScale, p: "Fair play", a: "Server-authoritative: phones only send strokes and net throws" },
    ];
    for (let i = 0; i < 3; i++) {
      const x = X0 + i * (3.75 + 0.34), c = cards[i];
      s.rect({ x, y: 1.55, w: 3.75, h: 2.75, fill: "CARD", radius: 0.18, shadow: true });
      s.text("PROBLEM", { x: x + 0.3, y: 1.8, w: 2.2, h: 0.35, size: 16, bold: true, color: "CORAL" });
      await s.iconIn(c.ic, "DEEP", x + 3.0, 1.75, 0.5);
      s.text(c.p, { x: x + 0.3, y: 2.2, w: 3.2, h: 0.8, size: 22, bold: true });
      s.text(c.a, { x: x + 0.3, y: 3.05, w: 3.2, h: 1.1, size: 18, color: "MUTED" });
    }
    const stats = [["60 Hz", "snapshots to every phone"], ["226 kbps", "download per phone"], ["43–71 ms", "round trip, measured"], ["60 fps", "average on a test phone"]];
    stats.forEach(([b, l], i) => {
      const x = X0 + i * (2.75 + 0.31);
      s.rect({ x, y: 4.55, w: 2.75, h: 1.45, fill: "DEEP", radius: 0.18 });
      s.text(b, { x: x + 0.2, y: 4.62, w: 2.35, h: 0.8, size: 36, font: "head", color: "WHITE", valign: "middle" });
      s.text(l, { x: x + 0.2, y: 5.42, w: 2.4, h: 0.5, size: 16, color: "RIVER" });
    });
    s.text("Measured 22 Nov 2026: full test match (60 snapshots/s, 226 kbps, 43 ms to a Marseille room); phone Network Lab on Wi-Fi (71 ms to a Paris room); phone full match (60.1 fps avg, worst frame 33 ms).", { x: X0, y: 6.12, w: CW, h: 0.5, size: 12, color: "MUTED" });
    s.notes("Three challenges. One: Cloudflare gives us no UDP, so we wrote a compact binary protocol over WebSockets. Two: the sync window is only a quarter second, so phones predict their own raft, buffer the rest, and the server rewinds net throws to compensate lag. Three: fairness. The server decides everything. Result: sixty snapshots per second, about two hundred kilobits per phone, and sixty frames per second on our test phone. [CLICK] [HAND OVER TO MED AMINE]");
  }
  // ===== 9. Impact + roadmap =====
  {
    const s = makeSlide({ layout: "LIGHT", section: "VI · Impact" }, {
      title: "Impact and roadmap", time: "0:30", speaker: "Med Amine CHAKROUN",
      onSlide: ["Impact and roadmap", "What a child takes away: 7 bin colors, learned by playing · A safety reflex: toxic waste costs · Teamwork: the raft needs both paddlers", "How we will measure it (PLAN): pre/post sorting quiz · sorting accuracy per bin · sync rate · completion rate", "NEXT: 5G SA test + latency slice · Eco-tip after each catch · Sorting center at run end · Team voice chat", "LATER: New rivers and worlds · School leaderboards · Battle mode: team vs team · Adaptive difficulty"],
      visual: "Left: three icon rows (learning outcomes) and the measurement plan with a yellow PLAN chip. Right: two roadmap cards, NEXT (deep teal header) and LATER (turquoise header).",
      images: ["icons only"], diagram: "See section D, diagram 4.", animation: "Fade. Optional: LATER card appears on click.",
    });
    s.title("Impact and roadmap");
    s.text("What a child takes away", { x: X0, y: 1.5, w: 5.6, h: 0.45, size: 22, bold: true });
    const rows = [
      { ic: fa.FaRecycle, c: "GREEN", t: "7 bin colors, learned by playing" },
      { ic: fa.FaExclamationTriangle, c: "CORAL", t: "A safety reflex: toxic waste costs" },
      { ic: fa.FaHandsHelping, c: "TURQ", t: "Teamwork: the raft needs both paddlers" },
    ];
    for (let i = 0; i < 3; i++) {
      const y = 2.05 + i * 0.72;
      await s.iconIn(rows[i].ic, rows[i].c, X0, y, 0.55);
      s.text(rows[i].t, { x: X0 + 0.75, y, w: 4.9, h: 0.55, size: 20, valign: "middle" });
    }
    s.text("How we will measure it", { x: X0, y: 4.35, w: 3.6, h: 0.45, size: 22, bold: true });
    s.rect({ x: X0 + 3.65, y: 4.38, w: 0.95, h: 0.4, fill: "SUN", radius: 0.2 });
    s.text("PLAN", { x: X0 + 3.65, y: 4.38, w: 0.95, h: 0.4, size: 16, bold: true, color: "DEEP", align: "center", valign: "middle" });
    s.text("Pre/post sorting quiz · sorting accuracy per bin · sync rate · completion rate", { x: X0, y: 4.85, w: 5.5, h: 0.95, size: 18, color: "MUTED" });
    const cols = [
      { x: 6.65, head: "NEXT · weeks", fill: "DEEP", txt: "WHITE", items: ["5G SA test + latency slice", "Eco-tip after each catch", "Sorting center at run end", "Team voice chat"] },
      { x: 9.79, head: "LATER", fill: "TURQ", txt: "DEEP", items: ["New rivers and worlds", "School leaderboards", "Battle mode: team vs team", "Adaptive difficulty"] },
    ];
    for (const c of cols) {
      s.rect({ x: c.x, y: 1.5, w: 2.84, h: 4.15, fill: "CARD", radius: 0.18, shadow: true });
      s.rect({ x: c.x, y: 1.5, w: 2.84, h: 0.6, fill: c.fill, radius: 0.18 });
      s.text(c.head, { x: c.x, y: 1.5, w: 2.84, h: 0.6, size: 20, bold: true, color: c.txt, align: "center", valign: "middle" });
      c.items.forEach((it, j) => {
        const y = 2.35 + j * 0.92;
        s.circle({ x: c.x + 0.25, y: y + 0.12, d: 0.14, fill: c.fill });
        s.text(it, { x: c.x + 0.5, y, w: 2.2, h: 0.8, size: 18 });
      });
    }
    s.notes("What does a child take away? The seven bin colors, a safety reflex with toxic waste, and the habit of working as a team. We won't claim results we haven't measured. Our next step is a classroom pilot with a sorting quiz before and after playing. Then: eco-tips for every catch, the sorting center, team voice, and our 5G Standalone test. Later: new rivers, school leaderboards, and a battle mode. [CLICK]");
  }
  // ===== 10. Closing =====
  {
    const s = makeSlide({ layout: "DARK" }, {
      title: "Closing", time: "0:15", speaker: "Med Amine CHAKROUN",
      onSlide: ["MOTTAINAI", "Every paddle counts. Every piece matters.", "A working real-time co-op prototype, built around a mission.", "Team 5le9a Deyra · SUP'COM", "Rayen TOUIL · Yassine AFFES · Med Amine CHAKROUN · Wajdi ZENTANI", "github.com/RayenTouil/Mottainai"],
      visual: "Concept art under a strong deep-teal veil; centered wordmark, green tagline, statement, team block.",
      images: ["Docs/reference_v3_master.jpg"], animation: "Fade (slow).",
    });
    s.image({ path: IMG.ref, x: 0, y: 0, w: W, h: H, cover: true, alt: "Concept art background" });
    s.rect({ x: 0, y: 0, w: W, h: H, fill: "DEEP", transparency: 12 });
    s.text("MOTTAINAI", { x: X0, y: 0.95, w: CW, h: 1.2, size: 72, font: "head", color: "WHITE", align: "center", valign: "middle" });
    s.text("Every paddle counts. Every piece matters.", { x: X0, y: 2.2, w: CW, h: 0.7, size: 34, font: "head", color: "GREEN", align: "center" });
    s.text("A working real-time co-op prototype, built around a mission.", { x: X0, y: 3.05, w: CW, h: 0.5, size: 22, color: "WHITE", align: "center" });
    s.text("Team 5le9a Deyra · SUP'COM", { x: X0, y: 4.0, w: CW, h: 0.45, size: 22, bold: true, color: "RIVER", align: "center" });
    s.text([{ text: "Rayen TOUIL  ·  Yassine AFFES", breakLine: true }, { text: "Med Amine CHAKROUN  ·  Wajdi ZENTANI" }], { x: X0, y: 4.5, w: CW, h: 0.85, size: 20, color: "WHITE", align: "center" });
    s.text("github.com/RayenTouil/Mottainai", { x: X0, y: 5.55, w: CW, h: 0.4, size: 18, color: "RIVER", align: "center" });
    s.notes("MOTTAINAI is more than an idea: it's a working, real-time co-op game with a mission. Every paddle counts. Every piece matters. Thank you.");
  }

  // ---------- pptx + theme + fade transitions ----------
  const pptxPath = path.join(PITCH, "MOTTAINAI_Final.pptx");
  await pres.writeFile({ fileName: pptxPath });
  if (process.env.APPLY_THEME) await require(process.env.APPLY_THEME).applyTheme(pptxPath, THEME);
  const zip = await JSZip.loadAsync(fs.readFileSync(pptxPath));
  for (const name of Object.keys(zip.files).filter(f => /^ppt\/slides\/slide\d+\.xml$/.test(f))) {
    let xml = await zip.file(name).async("string");
    if (!xml.includes("<p:transition")) xml = xml.replace("</p:clrMapOvr>", '</p:clrMapOvr><p:transition spd="med"><p:fade/></p:transition>');
    zip.file(name, xml);
  }
  const out = new JSZip(); // copy files only: folder entries break some cloud viewers
  for (const [n, f] of Object.entries(zip.files)) if (!f.dir) out.file(n, await f.async("nodebuffer"), { createFolders: false });
  fs.writeFileSync(pptxPath, await out.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }));

  // ---------- HTML twin ----------
  const fontUrl = fileUrl(ASSET("Mottainai/Assets/_Mottainai/Resources/Fonts/Fredoka-SemiBold.ttf"));
  const pages = htmlSlides.map(({ L, html, n }) => `<section class="slide" style="background:#${hc(L.bg)}">${html.join("\n")}
<div class="t" style="left:${X0}in;top:6.95in;width:7in;height:0.3in;justify-content:center;font-family:Calibri;font-size:12pt;color:#${hc(L.foot)}">${FOOT}</div>
<div class="t" style="left:${W - X0 - 0.6}in;top:6.95in;width:0.6in;height:0.3in;justify-content:center;text-align:right;font-family:Calibri;font-size:12pt;color:#${hc(L.foot)}">${n}</div></section>`);
  fs.writeFileSync(path.join(HTML_DIR, "final.html"), `<!doctype html><html><head><meta charset="utf-8"><title>MOTTAINAI final</title><style>
@font-face{font-family:'${HEAD}';src:url('${fontUrl}')}
@page{size:${W}in ${H}in;margin:0}
*{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0}
.slide{position:relative;width:${W}in;height:${H}in;overflow:hidden;page-break-after:always;break-after:page}
.t{position:absolute;display:flex;flex-direction:column;line-height:1.2}
</style></head><body>${pages.join("\n")}</body></html>`);

  // ---------- FINAL_GUIDE.md ----------
  const label = m => (m.n <= 6 ? String(m.n) : m.n === 7 ? "6b" : String(m.n - 1));
  const shown = metas.filter(m => !m.hidden);
  const spokenWords = shown.filter(m => !m.demo).reduce((a, m) => a + m.words, 0);
  const toSec = t => { const [a, b] = t.split(":").map(Number); return a * 60 + b; };
  const plannedSec = shown.reduce((a, m) => a + toSec(m.time), 0);
  const mmss = s => `${Math.floor(s / 60)}:${String(Math.round(s % 60)).padStart(2, "0")}`;
  const demoMeta = metas.find(m => m.demo);
  const speechSec = Math.round(spokenWords / 140 * 60) + toSec(demoMeta.time);
  let C_ = "", E_ = "";
  for (const m of metas) {
    C_ += `\n### ${label(m)}. ${m.title} — ${m.time} — ${m.speaker}\n\n`;
    C_ += `- **Text on the slide:**\n${m.onSlide.map(t => `  - ${t}`).join("\n")}\n`;
    C_ += `- **Visual composition:** ${m.visual}\n- **Images:** ${m.images.join(", ")}\n`;
    if (m.diagram) C_ += `- **Diagram:** ${m.diagram}\n`;
    C_ += `- **Animation / transition:** ${m.animation}\n- **Speaker notes:** ${m.script}\n`;
    if (!m.hidden) E_ += `\n**${label(m)}. ${m.title}** (${m.speaker}, ${m.time}${m.demo ? "" : `, ${m.words} words`})\n\n${m.script}\n`;
  }
  const B_ = `| # | Slide | Act | Time | Speaker |\n|---|---|---|---|---|\n` + metas.map(m => `| ${label(m)} | ${m.title} | ${actOf(m.n)} | ${m.time} | ${m.speaker} |`).join("\n");
  function actOf(n) { return ["I Problem", "I Problem", "II Idea", "III Experience", "III Experience", "V Proof", "V Proof (backup)", "IV Engineering", "IV Engineering", "VI Impact", "VI Impact"][n - 1]; }
  const guide = fs.readFileSync(path.join(PITCH, "guide_template.md"), "utf8")
    .replace("{{B}}", B_ + `\n\nPlanned time: **${mmss(plannedSec)}** for ${shown.length} slides. Script at 140 words/min: ${spokenWords} spoken words (${mmss(spokenWords / 140 * 60)}) + 0:45 demo = **${mmss(speechSec)}**, leaving about ${mmss(300 - speechSec)} of margin inside the 5:00 limit.`)
    .replace("{{C}}", C_).replace("{{E}}", E_);
  fs.writeFileSync(path.join(PITCH, "FINAL_GUIDE.md"), guide);
  console.log(JSON.stringify({ pptx: pptxPath, slides: slideNo, spokenWords, planned: mmss(plannedSec), speech: mmss(speechSec) }));
})().catch(e => { console.error(e); process.exit(1); });
