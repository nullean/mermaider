import ELK from "elkjs/lib/elk.bundled.js";
import { toElkGraph } from "./elk-options.mjs";
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const elk = new ELK();
async function orderOf(inp, extra, perm = (x) => x) {
  const g = toElkGraph({ nodes: perm(inp.nodes), edges: perm(inp.edges) }, extra);
  const o = await elk.layout(g);
  const ys = [...new Set(o.children.map(n => n.y))].sort((a, b) => a - b);
  return ys.map(y => o.children.filter(n => n.y === y).sort((a, b) => a.x - b.x).map(n => n.id).join(",")).join(" | ");
}
const out = [];
for (const f of fs.readdirSync(dir).filter(f => f.endsWith(".elk.input.json")).sort()) {
  const inp = JSON.parse(fs.readFileSync(`${dir}/${f}`));
  const seeds = new Set(); for (const sd of [1, 2, 3, 4, 5, 6, 7, 8]) seeds.add(await orderOf(inp, { "elk.randomSeed": String(sd) }));
  out.push(`${inp.slug.padEnd(34)} distinctOrders=${seeds.size}`);
}
console.log(out.join("\n"));
