// Runs elkjs (as a black-box tool) on emitted inputs; writes per-slug node/edge/port results.
// Usage: node scripts/elk-oracle.mjs   (reads tests/.../LayoutSkeleton/elk/*.elk.input.json)
import ELK from "elkjs/lib/elk.bundled.js";
import { toElkGraph } from "./elk-options.mjs";
import fs from "node:fs";
import path from "node:path";

const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const elk = new ELK();
for (const f of fs.readdirSync(dir).filter((f) => f.endsWith(".elk.input.json"))) {
  const inp = JSON.parse(fs.readFileSync(path.join(dir, f), "utf8"));
  const g = toElkGraph(inp);
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
