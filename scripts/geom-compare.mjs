// ours (ELK order forced) vs ELK: node centre-x deltas and route signatures per edge.
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const sig = pts => { // dedupe zero-length, classify D/U/R/L, run-length
  const out = []; for (let i = 1; i < pts.length; i++) { const dx = pts[i][0] - pts[i - 1][0], dy = pts[i][1] - pts[i - 1][1]; if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;
    const c = Math.abs(dy) >= Math.abs(dx) ? (dy > 0 ? "D" : "U") : (dx > 0 ? "R" : "L"); if (out[out.length - 1] !== c) out.push(c); } return out.join(" "); };
const want = process.argv[2];
let tot = 0, ok = 0, devSum = 0, devN = 0; const rows = [];
for (const f of fs.readdirSync(dir).filter(f => f.endsWith(".ours.json")).sort()) {
  const slug = f.replace(".ours.json", ""); if (want && !slug.includes(want)) continue;
  const o = JSON.parse(fs.readFileSync(`${dir}/${f}`)), e = JSON.parse(fs.readFileSync(`${dir}/${slug}.elk.output.json`));
  const eNodes = new Map(e.nodes.map(n => [n.id, n]));
  const dxs = o.nodes.filter(n => eNodes.has(n.id)).map(n => ({ id: n.id, d: (n.x + n.w / 2) - (eNodes.get(n.id).x + eNodes.get(n.id).w / 2) }));
  // normalise by median offset (overall shift)
  const med = dxs.map(d => d.d).sort((a, b) => a - b)[dxs.length >> 1] ?? 0;
  devSum += dxs.reduce((a, d) => a + Math.abs(d.d - med), 0); devN += dxs.length;
  const maxdev = Math.max(...dxs.map(d => Math.abs(d.d - med)));
  const used = new Set(); let eok = 0, et = 0; const bad = [];
  for (const oe of o.edges) {
    const cand = e.edges.findIndex((x, i) => !used.has(i) && x.source === oe.source && x.target === oe.target); if (cand < 0) continue; used.add(cand);
    const a = sig(oe.points), b = sig(e.edges[cand].points); et++; if (a === b) eok++; else bad.push(`${oe.source}>${oe.target}  ours[${a}]  elk[${b}]`);
  }
  tot += et; ok += eok;
  rows.push({ slug, eok, et, maxdev: maxdev.toFixed(0), bad });
}
for (const r of rows) { console.log(`${r.slug.padEnd(34)} routes ${r.eok}/${r.et}  maxXdev(after shift)=${r.maxdev}`); if (want) r.bad.forEach(b => console.log("    " + b)); }
console.log("TOTAL", ok, "/", tot, " meanXdev", (devSum / devN).toFixed(1));
