// Flowchart quality scoreboard, measured from the committed/received gallery SVGs.
// Metrics (all should be 0 except crossings/bends which are compared informally):
//   sgOverlap   subgraph boxes that partially overlap (neither contains the other)
//   straddle    nodes that partially overlap a subgraph box (neither inside nor outside)
//   throughNode edges whose path runs through a node that is not one of its ends
//   throughSg   edges that pass through a subgraph that contains neither end, or leave/re-enter one that contains both
//   crossings   pairs of edges (sharing no endpoint) whose paths cross
//   bends/edge, canvas size and aspect ratio
// Usage: node scripts/flow-quality.mjs [slugFilter]
import fs from "node:fs";

const snap = "tests/Mermaider.Tests/Snapshots/";
const filter = process.argv[2];

function bboxOfGroup(body) {
  const pts = [];
  for (const m of body.matchAll(/<rect x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/g)) { pts.push([+m[1], +m[2]], [+m[1] + +m[3], +m[2] + +m[4]]); }
  for (const m of body.matchAll(/points="([^"]+)"/g)) { const n = m[1].match(/-?[\d.]+/g).map(Number); for (let i = 0; i + 1 < n.length; i += 2) pts.push([n[i], n[i + 1]]); }
  for (const m of body.matchAll(/<(?:ellipse|circle)[^>]*cx="(-?[\d.]+)" cy="(-?[\d.]+)"[^>]*?(?:rx|r)="([\d.]+)"(?:[^>]*ry="([\d.]+)")?/g)) { const rx = +m[3], ry = +(m[4] ?? m[3]); pts.push([+m[1] - rx, +m[2] - ry], [+m[1] + rx, +m[2] + ry]); }
  if (!pts.length) for (const m of body.matchAll(/ d="([^"]+)"/g)) { const n = m[1].match(/-?[\d.]+/g)?.map(Number) ?? []; for (let i = 0; i + 1 < n.length; i += 2) pts.push([n[i], n[i + 1]]); }
  if (!pts.length) return null;
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  return { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
}

function parse(svg) {
  const groups = [...svg.matchAll(/<g class="(subgraph|node)" data-id="([^"]+)"[^>]*>([\s\S]*?)<\/g>/g)];
  const subgraphs = [], nodes = [];
  for (const g of groups) {
    // subgraph groups can contain nested content; the outer rect is the first rect
    const first = /<rect x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/.exec(g[3]);
    if (g[1] === "subgraph") { if (first) subgraphs.push({ id: g[2], x0: +first[1], y0: +first[2], x1: +first[1] + +first[3], y1: +first[2] + +first[4] }); }
    else { const b = bboxOfGroup(g[3]); if (b) nodes.push({ id: g[2], ...b }); }
  }
  const edges = [];
  for (const m of svg.matchAll(/<path class="edge" data-from="([^"]+)" data-to="([^"]+)"[^>]*? d="([^"]+)"/g)) {
    const t = m[3].match(/[MLQC]|-?[\d.]+/g); const pts = []; let i = 0, c = "";
    while (i < t.length) { if (/[MLQC]/.test(t[i])) { c = t[i++]; continue; } if (c === "M" || c === "L") { pts.push([+t[i], +t[i + 1]]); i += 2; } else if (c === "Q") { pts.push([+t[i], +t[i + 1]]); i += 4; } else if (c === "C") { pts.push([+t[i + 4], +t[i + 5]]); i += 6; } else i++; }
    edges.push({ s: m[1], t: m[2], p: pts, curved: /[CQ]/.test(m[3]) && /C/.test(m[3]) });
  }
  const vb = /viewBox="0 0 ([\d.]+) ([\d.]+)"/.exec(svg);
  return { subgraphs, nodes, edges, w: +vb[1], h: +vb[2] };
}

const inter = (a, b) => a.x0 < b.x1 - 1 && a.x1 > b.x0 + 1 && a.y0 < b.y1 - 1 && a.y1 > b.y0 + 1;
const contains = (a, b) => a.x0 <= b.x0 + 1 && a.y0 <= b.y0 + 1 && a.x1 >= b.x1 - 1 && a.y1 >= b.y1 - 1;
const segHitsBox = (p, q, b, m = 2) => { const x0 = Math.min(p[0], q[0]), x1 = Math.max(p[0], q[0]), y0 = Math.min(p[1], q[1]), y1 = Math.max(p[1], q[1]); return x1 > b.x0 + m && x0 < b.x1 - m && y1 > b.y0 + m && y0 < b.y1 - m; };
const segCross = (p, q, r, s) => { const d1x = q[0] - p[0], d1y = q[1] - p[1], d2x = s[0] - r[0], d2y = s[1] - r[1], den = d1x * d2y - d1y * d2x; if (Math.abs(den) < 1e-9) return false; const t = ((r[0] - p[0]) * d2y - (r[1] - p[1]) * d2x) / den, u = ((r[0] - p[0]) * d1y - (r[1] - p[1]) * d1x) / den; return t > 1e-3 && t < 1 - 1e-3 && u > 1e-3 && u < 1 - 1e-3; };
const pathHits = (pts, b) => { for (let i = 0; i < pts.length - 1; i++) if (segHitsBox(pts[i], pts[i + 1], b)) return true; return false; };
const nodeInSg = (n, sg) => contains(sg, n);

function metrics(d) {
  let sgOverlap = 0, straddle = 0, throughNode = 0, throughSg = 0, crossings = 0, bends = 0, curved = 0;
  for (let i = 0; i < d.subgraphs.length; i++) for (let j = i + 1; j < d.subgraphs.length; j++) { const a = d.subgraphs[i], b = d.subgraphs[j]; if (inter(a, b) && !contains(a, b) && !contains(b, a)) sgOverlap++; }
  for (const n of d.nodes) for (const sg of d.subgraphs) if (inter(n, sg) && !contains(sg, n)) straddle++;
  const nodeById = new Map(d.nodes.map(n => [n.id, n]));
  for (const e of d.edges) {
    bends += Math.max(0, e.p.length - 2); if (e.curved) curved++;
    for (const n of d.nodes) if (n.id !== e.s && n.id !== e.t && pathHits(e.p, n)) throughNode++;
    const s = nodeById.get(e.s), t = nodeById.get(e.t);
    for (const sg of d.subgraphs) {
      const sin = s && nodeInSg(s, sg), tin = t && nodeInSg(t, sg);
      if (!sin && !tin && pathHits(e.p, sg)) throughSg++;
    }
  }
  for (let i = 0; i < d.edges.length; i++) for (let j = i + 1; j < d.edges.length; j++) {
    const a = d.edges[i], b = d.edges[j]; if (a.s === b.s || a.t === b.t || a.s === b.t || a.t === b.s) continue;
    let hit = false; for (let x = 0; x < a.p.length - 1 && !hit; x++) for (let y = 0; y < b.p.length - 1 && !hit; y++) if (segCross(a.p[x], a.p[x + 1], b.p[y], b.p[y + 1])) hit = true;
    if (hit) crossings++;
  }
  return { sgOverlap, straddle, throughNode, throughSg, crossings, bendsPerEdge: bends / Math.max(1, d.edges.length), curved, w: d.w, h: d.h, aspect: Math.max(d.w, d.h) / Math.min(d.w, d.h) };
}

const rows = [];
for (const f of fs.readdirSync(snap).filter(f => /^GallerySnapshotTests\.Gallery_example=(flowchart|db-flow|rfc)-?.*\.(received|verified)\.svg$/.test(f)).sort()) {
  const slug = f.replace("GallerySnapshotTests.Gallery_example=", "").replace(/\.(received|verified)\.svg$/, "");
  if (f.endsWith(".verified.svg") && fs.existsSync(snap + f.replace(".verified.", ".received."))) continue;
  if (filter && !slug.includes(filter)) continue;
  const d = parse(fs.readFileSync(snap + f, "utf8")); if (!d.nodes.length) continue;
  rows.push({ slug, ...metrics(d), sgs: d.subgraphs.length, nodes: d.nodes.length, edges: d.edges.length });
}
const pad = (s, n) => String(s).padEnd(n);
console.log(pad("slug", 28), pad("nodes/edges/sg", 15), pad("sgOverlap", 10), pad("straddle", 9), pad("thruNode", 9), pad("thruSg", 7), pad("crossings", 10), pad("bends/e", 8), pad("curved", 7), pad("size", 12), "aspect");
let bad = 0;
for (const r of rows) {
  const flags = [r.sgOverlap, r.straddle, r.throughNode, r.throughSg].some(v => v > 0) || (r.aspect > 4 && (r.sgs > 0 || r.edges > r.nodes - 1)) || r.curved > 0;
  if (flags) bad++;
  console.log(pad(r.slug, 28), pad(`${r.nodes}/${r.edges}/${r.sgs}`, 15), pad(r.sgOverlap, 10), pad(r.straddle, 9), pad(r.throughNode, 9), pad(r.throughSg, 7), pad(r.crossings, 10), pad(r.bendsPerEdge.toFixed(1), 8), pad(r.curved, 7), pad(`${Math.round(r.w)}x${Math.round(r.h)}`, 12), r.aspect.toFixed(1) + (flags ? "  <-- needs work" : ""));
}
console.log(`diagrams with a violation (overlap/straddle/through/curved/aspect>4 unless a plain chain): ${bad}/${rows.length}`);
