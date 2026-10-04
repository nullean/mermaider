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

import { parse, metrics } from "./flow-metrics.mjs";
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
  const flags = [r.sgOverlap, r.straddle, r.throughNode, r.throughSg].some(v => v > 0) || (r.aspect > (r.sgs > 0 ? 7 : 4) && (r.sgs > 0 || r.edges > r.nodes - 1)) || r.curved > 0;
  if (flags) bad++;
  console.log(pad(r.slug, 28), pad(`${r.nodes}/${r.edges}/${r.sgs}`, 15), pad(r.sgOverlap, 10), pad(r.straddle, 9), pad(r.throughNode, 9), pad(r.throughSg, 7), pad(r.crossings, 10), pad(r.bendsPerEdge.toFixed(1), 8), pad(r.curved, 7), pad(`${Math.round(r.w)}x${Math.round(r.h)}`, 12), r.aspect.toFixed(1) + (flags ? "  <-- needs work" : ""));
}
console.log(`diagrams with a violation (overlap/straddle/through/curved/aspect>4, or >7 with subgraphs [LR pipelines of labelled groups are inherently wide], unless a plain chain): ${bad}/${rows.length}`);
