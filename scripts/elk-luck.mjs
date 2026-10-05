// How well does ELK with another randomSeed agree with the default-seed reference? (the "luck baseline")
import ELK from "elkjs/lib/elk.bundled.js";
import { toElkGraph } from "./elk-options.mjs";
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const elk = new ELK();
async function nodesOf(inp, seed) {
  const g = toElkGraph(inp, seed ? { "elk.randomSeed": String(seed) } : {});
  const o = await elk.layout(g);
  const ys = [...new Set(o.children.map(n => n.y))].sort((a, b) => a - b);
  const m = new Map(); ys.forEach((y, li) => o.children.filter(n => n.y === y).sort((a, b) => a.x - b.x).forEach((n, i) => m.set(n.id, [li, i])));
  return m;
}
const agree = (a, b) => { let n = 0; for (const [k, v] of a) { const w = b.get(k); if (w && w[0] === v[0] && w[1] === v[1]) n++; } return 100 * n / a.size; };
const mirror = (m) => { const mx = new Map(); for (const [k, v] of m) mx.set(k, v[0]); const cnt = new Map(); for (const [k, v] of m) cnt.set(v[0], (cnt.get(v[0]) ?? 0) + 1); const r = new Map(); for (const [k, v] of m) r.set(k, [v[0], cnt.get(v[0]) - 1 - v[1]]); return r; };
let sumLuck = 0, k = 0;
const dump = {};
for (const f of fs.readdirSync(dir).filter(f => f.endsWith(".elk.input.json")).sort()) {
  const inp = JSON.parse(fs.readFileSync(`${dir}/${f}`));
  const ref = await nodesOf(inp, 0);
  const draws = {}; for (let sd = 1; sd <= 13; sd++) draws[sd] = [...(await nodesOf(inp, sd === 1 ? 0 : sd))];
  dump[inp.slug] = { ref: [...ref], draws };
  const others = []; for (let s = 2; s <= 13; s++) others.push(agree(ref, await nodesOf(inp, s)));
  const mean = others.reduce((a, b) => a + b, 0) / others.length;
  if (Math.min(...others) < 100) { console.log(inp.slug.padEnd(34), "other-seed agreement with reference: mean", mean.toFixed(0) + "%", "min", Math.min(...others).toFixed(0), "max", Math.max(...others).toFixed(0)); sumLuck += mean; k++; }
}
console.log("RNG-driven diagrams:", k, " mean luck-baseline agreement:", (sumLuck / k).toFixed(1) + "%");

fs.writeFileSync(`${dir}/seed-draws.json`, JSON.stringify(dump));
