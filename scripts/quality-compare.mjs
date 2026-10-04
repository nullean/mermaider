// Quality scoreboard: our rendered ER SVGs vs ELK (oracle options, same entity sizes) over several seeds.
// Metrics: edge crossings, overlapping collinear segments of different edges, bends/edge, total length.
// Usage: node scripts/quality-compare.mjs [slugFilter]
import ELK from "elkjs/lib/elk.bundled.js";
import fs from "node:fs";
import { toElkGraph } from "./elk-options.mjs";

const snap = "tests/Mermaider.Tests/Snapshots/";
const dir = snap + "LayoutSkeleton/elk/";
const elk = new ELK();
const filter = process.argv[2];

const segIntersect = (p, q, r, s) => {
  const d1x = q[0] - p[0], d1y = q[1] - p[1], d2x = s[0] - r[0], d2y = s[1] - r[1], den = d1x * d2y - d1y * d2x;
  if (Math.abs(den) < 1e-9) return false;
  const t = ((r[0] - p[0]) * d2y - (r[1] - p[1]) * d2x) / den, u = ((r[0] - p[0]) * d1y - (r[1] - p[1]) * d1x) / den;
  return t > 1e-3 && t < 1 - 1e-3 && u > 1e-3 && u < 1 - 1e-3;
};
function metrics(edges) {
  let crossings = 0, overlaps = 0, bends = 0, length = 0;
  for (const e of edges) { bends += Math.max(0, e.p.length - 2); for (let i = 1; i < e.p.length; i++) length += Math.abs(e.p[i][0] - e.p[i - 1][0]) + Math.abs(e.p[i][1] - e.p[i - 1][1]); }
  for (let i = 0; i < edges.length; i++) for (let j = i + 1; j < edges.length; j++) {
    const a = edges[i], b = edges[j];
    const share = a.s === b.s || a.t === b.t || a.s === b.t || a.t === b.s;
    let hit = false, ov = false;
    for (let x = 0; x < a.p.length - 1; x++) for (let y = 0; y < b.p.length - 1; y++) {
      const [p, q, r, s] = [a.p[x], a.p[x + 1], b.p[y], b.p[y + 1]];
      if (!share && segIntersect(p, q, r, s)) hit = true;
      const aH = Math.abs(p[1] - q[1]) < 0.5, bH = Math.abs(r[1] - s[1]) < 0.5, aV = Math.abs(p[0] - q[0]) < 0.5, bV = Math.abs(r[0] - s[0]) < 0.5;
      if (aH && bH && Math.abs(p[1] - r[1]) < 1.5 && Math.min(Math.max(p[0], q[0]), Math.max(r[0], s[0])) - Math.max(Math.min(p[0], q[0]), Math.min(r[0], s[0])) > 6) ov = true;
      if (aV && bV && Math.abs(p[0] - r[0]) < 1.5 && Math.min(Math.max(p[1], q[1]), Math.max(r[1], s[1])) - Math.max(Math.min(p[1], q[1]), Math.min(r[1], s[1])) > 6) ov = true;
    }
    if (hit) crossings++; if (ov) overlaps++;
  }
  return { crossings, overlaps, bends: bends / Math.max(1, edges.length), length };
}
function oursFromSvg(slug) {
  let f = `${snap}GallerySnapshotTests.Gallery_example=${slug}.received.svg`;
  if (!fs.existsSync(f)) f = `${snap}GallerySnapshotTests.Gallery_example=${slug}.verified.svg`;
  if (!fs.existsSync(f)) return null;
  const svg = fs.readFileSync(f, "utf8"); const edges = [];
  for (const m of svg.matchAll(/<path class="er-relationship" data-entity1="([^"]+)" data-entity2="([^"]+)"[^>]*? d="([^"]+)"/g)) {
    const pts = []; const toks = m[3].match(/[MLQC]|-?[\d.]+(?:e-?\d+)?/g) ?? []; let i = 0, cmd = "";
    while (i < toks.length) { const t = toks[i]; if (/[MLQC]/.test(t)) { cmd = t; i++; continue; }
      if (cmd === "M" || cmd === "L") { pts.push([+toks[i], +toks[i + 1]]); i += 2; } else if (cmd === "Q") { pts.push([+toks[i], +toks[i + 1]]); i += 4; } else if (cmd === "C") { pts.push([+toks[i + 4], +toks[i + 5]]); i += 6; } else i++; }
    // drop the rounded-corner entry/exit points that sit 5px before/after a corner (collinear pairs collapse)
    const clean = pts.filter((p, k) => k === 0 || k === pts.length - 1 || !(Math.abs(pts[k - 1][0] - p[0]) < 0.01 && Math.abs(p[0] - pts[k + 1][0]) < 0.01) && !(Math.abs(pts[k - 1][1] - p[1]) < 0.01 && Math.abs(p[1] - pts[k + 1][1]) < 0.01));
    edges.push({ s: m[1], t: m[2], p: clean });
  }
  return edges;
}
const med = a => [...a].sort((x, y) => x - y)[a.length >> 1];
const rows = [];
for (const f of fs.readdirSync(dir).filter(f => f.endsWith(".elk.input.json")).sort()) {
  const inp = JSON.parse(fs.readFileSync(dir + f)); const slug = inp.slug; if (filter && !slug.includes(filter)) continue;
  const ours = oursFromSvg(slug); if (!ours) continue; const om = metrics(ours);
  const ms = [];
  for (const sd of [0, 2, 3, 4, 5, 6, 7]) {
    const o = await elk.layout(toElkGraph(inp, sd ? { "elk.randomSeed": String(sd) } : {}));
    ms.push(metrics(o.edges.map(e => ({ s: e.sources[0], t: e.targets[0], p: e.sections.flatMap(s => [s.startPoint, ...(s.bendPoints ?? []), s.endPoint]).map(p => [p.x, p.y]) }))));
  }
  rows.push({ slug, om, cr: ms.map(m => m.crossings), ov: ms.map(m => m.overlaps), bd: med(ms.map(m => m.bends)), ln: med(ms.map(m => m.length)) });
}
const pad = (s, n) => String(s).padEnd(n);
console.log(pad("slug", 34), pad("crossings ours | ELK min/med/max", 36), pad("overlaps ours | ELK med", 26), pad("bends/edge ours|ELK", 20), "length ours/ELK");
let okC = 0, okO = 0;
for (const r of rows) {
  const cm = med(r.cr), om = med(r.ov); if (r.om.crossings <= cm) okC++; if (r.om.overlaps <= om) okO++;
  console.log(pad(r.slug, 34), pad(`${r.om.crossings} | ${Math.min(...r.cr)}/${cm}/${Math.max(...r.cr)}${r.om.crossings > cm ? "  <-- worse" : ""}`, 36), pad(`${r.om.overlaps} | ${om}${r.om.overlaps > om ? " <-- worse" : ""}`, 26), pad(`${r.om.bends.toFixed(1)} | ${r.bd.toFixed(1)}`, 20), (r.om.length / r.ln).toFixed(2));
}
console.log(`crossings <= ELK median: ${okC}/${rows.length}   overlaps <= ELK median: ${okO}/${rows.length}`);
