// Flowchart parity vs mermaid.js: same geometric metrics on our SVG and the mermaid.js reference SVG.
// Gates (per diagram): our crossings <= mjs crossings (+ slack), no overlap/through violations, area <= 1.5x mjs.
// Usage: node scripts/flow-parity.mjs [slugFilter]
import fs from "node:fs";
import { parse, metrics } from "./flow-metrics.mjs";

const snap = "tests/Mermaider.Tests/Snapshots/";
const ref = snap + "Reference/mermaidjs/";
const filter = process.argv[2];

function parseMjs(svg) {
  const vb = /viewBox="([-\d.]+) ([-\d.]+) ([\d.]+) ([\d.]+)"/.exec(svg);
  const nodes = [];
  const chunks = svg.split(/(?=<g class="node[ "])/).slice(1);
  for (const ch of chunks) {
    const head = /^<g class="node[^"]*" id="[^"]*?flowchart-(.+?)-\d+"[^>]*transform="translate\(([-\d.]+),\s*([-\d.]+)\)"/.exec(ch);
    if (!head) continue;
    const [, id, tx, ty] = head;
    const body = ch.slice(0, ch.indexOf('<g class="label"') > 0 ? ch.indexOf('<g class="label"') : 600);
    const pts = [];
    for (const r of body.matchAll(/<rect[^>]*?x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/g)) pts.push([+r[1], +r[2]], [+r[1] + +r[3], +r[2] + +r[4]]);
    for (const r of body.matchAll(/<polygon points="([^"]+)"[^>]*?(?:transform="translate\((-?[\d.]+),\s*(-?[\d.]+)\)")?/g)) { const n = r[1].match(/-?[\d.]+/g).map(Number); const ox = +(r[2] ?? 0), oy = +(r[3] ?? 0); for (let i = 0; i + 1 < n.length; i += 2) pts.push([n[i] + ox, n[i + 1] + oy]); }
    for (const r of body.matchAll(/<(?:circle|ellipse)[^>]*?\br="([\d.]+)"/g)) pts.push([-+r[1], -+r[1]], [+r[1], +r[1]]);
    if (!pts.length) continue;
    const xs = pts.map(p => p[0] + +tx), ys = pts.map(p => p[1] + +ty);
    nodes.push({ id, x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) });
  }
  const subgraphs = [];
  for (const m of svg.matchAll(/<g class="cluster[^"]*" id="[^"]*?-([^"-]+)"[^>]*><rect[^>]*?x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/g))
    subgraphs.push({ id: m[1], x0: +m[2], y0: +m[3], x1: +m[2] + +m[4], y1: +m[3] + +m[5] });
  const ids = nodes.map(n => n.id).concat(subgraphs.map(s => s.id));
  const edges = [];
  for (const m of svg.matchAll(/<path d="([^"]+)" id="[^"]*?L_(.+?)_\d+"/g)) {
    const key = m[2];
    const s = ids.find(a => key.startsWith(a + "_") && ids.includes(key.slice(a.length + 1))); if (!s) continue;
    const t = key.slice(s.length + 1);
    const tok = m[1].match(/[MLQC]|-?[\d.]+(?:e-?\d+)?/g); const p = []; let i = 0, c = "";
    while (i < tok.length) { if (/[MLQC]/.test(tok[i])) { c = tok[i++]; continue; } if (c === "M" || c === "L") { p.push([+tok[i], +tok[i + 1]]); i += 2; } else if (c === "Q") { p.push([+tok[i + 2], +tok[i + 3]]); i += 4; } else if (c === "C") { p.push([+tok[i + 4], +tok[i + 5]]); i += 6; } else i++; }
    edges.push({ s, t, p, curved: false });
  }
  return { subgraphs, nodes, edges, w: +vb[3], h: +vb[4] };
}

const pad = (s, n) => String(s).padEnd(n);
console.log(pad("slug", 26), pad("cross+overlap ours/mjs", 26), pad("bends/e ours/mjs", 18), pad("area ours/mjs", 22), "violations");
let worse = 0, total = 0;
for (const f of fs.readdirSync(ref).filter(f => /^(flowchart|db-flow|rfc)-.*\.svg$/.test(f)).sort()) {
  const slug = f.replace(".svg", "");
  if (filter && !slug.includes(filter)) continue;
  const ours = ["received", "verified"].map(k => snap + `GallerySnapshotTests.Gallery_example=${slug}.${k}.svg`).find(p => fs.existsSync(p));
  if (!ours) continue;
  const a = parse(fs.readFileSync(ours, "utf8"));
  let b; try { b = parseMjs(fs.readFileSync(ref + f, "utf8")); } catch { continue; }
  if (!a.nodes.length || !b.nodes.length) continue;
  const ma = metrics(a), mb = metrics(b);
  const areaRatio = (ma.w * ma.h) / (mb.w * mb.h);
  const viol = ma.sgOverlap + ma.straddle + ma.throughNode + ma.throughSg;
  const bad = ma.crossings + ma.overlaps > mb.crossings + mb.overlaps + 1 || areaRatio > 1.5 || viol > 0;
  total++; if (bad) worse++;
  console.log(pad(slug, 26), pad(`${ma.crossings + ma.overlaps} / ${mb.crossings + mb.overlaps}`, 26), pad(`${ma.bendsPerEdge.toFixed(1)} / ${mb.bendsPerEdge.toFixed(1)}`, 18),
    pad(`${Math.round(ma.w)}x${Math.round(ma.h)} / ${Math.round(mb.w)}x${Math.round(mb.h)} (${areaRatio.toFixed(2)}x)`, 38), viol + (bad ? "  <-- behind mjs" : ""));
}
console.log(`behind mermaid.js: ${worse}/${total}`);
