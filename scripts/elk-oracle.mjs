// Runs elkjs (as a black-box tool) on emitted inputs; writes per-slug node/edge/port results.
// Usage: node scripts/elk-oracle.mjs   (reads tests/.../LayoutSkeleton/elk/*.elk.input.json)
import ELK from "elkjs/lib/elk.bundled.js";
import fs from "node:fs";
import path from "node:path";

const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const elk = new ELK();
const opts = {
  "elk.algorithm": "layered",
  "elk.direction": "DOWN",
  "elk.edgeRouting": "ORTHOGONAL",
  "elk.hierarchyHandling": "INCLUDE_CHILDREN",
  "elk.spacing.baseValue": "40",
  "elk.layered.layering.strategy": "NETWORK_SIMPLEX",
  "elk.layered.nodePlacement.strategy": "BRANDES_KOEPF",
  "elk.layered.nodePlacement.bk.fixedAlignment": "BALANCED",
  "elk.layered.cycleBreaking.strategy": "DEPTH_FIRST",
  "elk.layered.considerModelOrder.strategy": "NODES_AND_EDGES",
  "elk.layered.mergeEdges": "false",
  "elk.layered.unnecessaryBendpoints": "true",
  "elk.portConstraints": "FREE",
};
for (const f of fs.readdirSync(dir).filter((f) => f.endsWith(".elk.input.json"))) {
  const inp = JSON.parse(fs.readFileSync(path.join(dir, f), "utf8"));
  const g = {
    id: "root",
    layoutOptions: opts,
    children: inp.nodes.map((n) => ({ id: n.id, width: n.width, height: n.height })),
    edges: inp.edges.map((e) => ({
      id: e.id, sources: [e.source], targets: [e.target],
      labels: e.labelWidth > 0 ? [{ text: "x", width: e.labelWidth, height: e.labelHeight, layoutOptions: { "elk.edgeLabels.inline": "true" } }] : [],
    })),
  };
  const out = await elk.layout(g);
  const res = {
    nodes: out.children.map((n) => ({ id: n.id, x: n.x, y: n.y, w: n.width, h: n.height })),
    edges: out.edges.map((e) => ({
      id: e.id, source: e.sources[0], target: e.targets[0],
      points: e.sections.flatMap((s) => [s.startPoint, ...(s.bendPoints ?? []), s.endPoint]).map((p) => [p.x, p.y]),
    })),
  };
  fs.writeFileSync(path.join(dir, f.replace(".input.", ".output.")), JSON.stringify(res, null, 1));
  console.log("ok", inp.slug);
}
