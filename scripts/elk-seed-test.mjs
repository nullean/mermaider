import ELK from "elkjs/lib/elk.bundled.js";
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const elk = new ELK();
const base = { "elk.algorithm": "layered", "elk.direction": "DOWN", "elk.edgeRouting": "ORTHOGONAL", "elk.spacing.baseValue": "40",
  "elk.layered.layering.strategy": "NETWORK_SIMPLEX", "elk.layered.nodePlacement.strategy": "BRANDES_KOEPF",
  "elk.layered.nodePlacement.bk.fixedAlignment": "BALANCED", "elk.layered.cycleBreaking.strategy": "DEPTH_FIRST",
  "elk.layered.considerModelOrder.strategy": "NODES_AND_EDGES" };
async function orderOf(inp, extra, perm = (x) => x) {
  const g = { id: "root", layoutOptions: { ...base, ...extra },
    children: perm(inp.nodes).map(n => ({ id: n.id, width: n.width, height: n.height })),
    edges: perm(inp.edges).map(e => ({ id: e.id, sources: [e.source], targets: [e.target], labels: e.labelWidth > 0 ? [{ text: "x", width: e.labelWidth, height: e.labelHeight, layoutOptions: { "elk.edgeLabels.inline": "true" } }] : [] })) };
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
