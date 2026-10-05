// Prototype: given ELK's layer assignment, which ordering heuristic reproduces ELK's real-node order?
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const slugs = fs.readdirSync(dir).filter(f => f.endsWith(".elk.output.json")).map(f => f.replace(".elk.output.json", ""));

function load(slug) {
  const inp = JSON.parse(fs.readFileSync(`${dir}/${slug}.elk.input.json`));
  const out = JSON.parse(fs.readFileSync(`${dir}/${slug}.elk.output.json`));
  const ys = [...new Set(out.nodes.map(n => n.y))].sort((a, b) => a - b);
  const layer = new Map(out.nodes.map(n => [n.id, ys.indexOf(n.y)]));
  const target = ys.map(y => out.nodes.filter(n => n.y === y).sort((a, b) => a.x - b.x).map(n => n.id));
  const modelIdx = new Map(inp.nodes.map((n, i) => [n.id, i]));
  // oriented edges with virtual chains
  const nodes = inp.nodes.map(n => ({ id: n.id, layer: layer.get(n.id), model: modelIdx.get(n.id), virt: false }));
  const edges = [];
  inp.edges.forEach((e, ei) => {
    let a = e.source, b = e.target;
    if (layer.get(a) === layer.get(b)) return;
    if (layer.get(a) > layer.get(b)) [a, b] = [b, a];
    let prev = a;
    for (let l = layer.get(a) + 1; l < layer.get(b); l++) {
      const v = { id: `v${ei}_${l}`, layer: l, model: ei, virt: true, edgeModel: ei };
      nodes.push(v); edges.push([prev, v.id, ei]); prev = v.id;
    }
    edges.push([prev, b, ei]);
  });
  return { nodes, edges, target, nl: ys.length };
}

function crossingsBetween(order, edges, nodeLayer, l) {
  const pos = new Map(); order.forEach((layer, li) => layer.forEach((id, i) => pos.set(id, i)));
  const es = edges.filter(([u, v]) => nodeLayer.get(u) === l && nodeLayer.get(v) === l + 1).map(([u, v]) => [pos.get(u), pos.get(v)]);
  let c = 0;
  for (let i = 0; i < es.length; i++) for (let j = i + 1; j < es.length; j++)
    if ((es[i][0] - es[j][0]) * (es[i][1] - es[j][1]) < 0) c++;
  return c;
}
const total = (order, edges, nl, nodeLayer) => { let c = 0; for (let l = 0; l < nl - 1; l++) c += crossingsBetween(order, edges, nodeLayer, l); return c; };

function run(g, opt) {
  const nodeLayer = new Map(g.nodes.map(n => [n.id, n.layer]));
  const byId = new Map(g.nodes.map(n => [n.id, n]));
  const up = new Map(), down = new Map();
  g.nodes.forEach(n => { up.set(n.id, []); down.set(n.id, []); });
  g.edges.forEach(([u, v, ei]) => { down.get(u).push([v, ei]); up.get(v).push([u, ei]); });
  // initial order
  let order = Array.from({ length: g.nl }, () => []);
  const key = n => opt.virtKey === "edge" ? (n.virt ? n.edgeModel + 0.5 : n.model) : n.model;
  g.nodes.forEach(n => order[n.layer].push(n));
  order = order.map(l => l.sort((a, b) => key(a) - key(b)).map(n => n.id));
  if (opt.init) {
    const seen = new Set(); const seq = [];
    const roots = g.nodes.filter(n => !n.virt && up.get(n.id).length === 0).sort((a, b) => a.model - b.model);
    const visit = id => { if (seen.has(id)) return; seen.add(id); seq.push(id); [...down.get(id)].sort((a, b) => a[1] - b[1]).forEach(([v]) => visit(v)); };
    if (opt.init === "dfs") roots.forEach(r => visit(r.id));
    else { const q = roots.map(r => r.id); roots.forEach(r => seen.add(r.id)); while (q.length) { const id = q.shift(); seq.push(id); [...down.get(id)].sort((a, b) => a[1] - b[1]).forEach(([v]) => { if (!seen.has(v)) { seen.add(v); q.push(v); } }); } }
    g.nodes.forEach(n => { if (!seen.has(n.id)) seq.push(n.id); });
    const rank = new Map(seq.map((id, i) => [id, i]));
    order = order.map(l => [...l].sort((a, b) => rank.get(a) - rank.get(b)));
  }
  if (opt.shuffle) order = order.map(l => { const a = [...l]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(opt.shuffle() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; });
  let best = order.map(l => [...l]), bestC = total(order, g.edges, g.nl, nodeLayer);
  const sweep = (down_) => {
    const ls = [...Array(g.nl).keys()]; if (!down_) ls.reverse();
    for (const l of ls.slice(1)) {
      const fixed = order[down_ ? l - 1 : l + 1];
      const pos = new Map(fixed.map((id, i) => [id, i]));
      const size = fixed.length;
      const cur = new Map(order[l].map((id, i) => [id, i]));
      const nb = id => (down_ ? up : down).get(id).map(([x]) => pos.get(x)).filter(x => x !== undefined);
      const stat = id => { const ns = nb(id); if (!ns.length) return null; if (opt.agg === "median") { ns.sort((a, b) => a - b); const m = ns.length >> 1; return ns.length % 2 ? ns[m] : (ns[m - 1] + ns[m]) / 2; } return ns.reduce((a, b) => a + b, 0) / ns.length; };
      if (opt.neigh === "slots") {
        const withN = order[l].filter(id => stat(id) !== null).sort((a, b) => (stat(a) - stat(b)) || (cur.get(a) - cur.get(b)));
        let k = 0; order[l] = order[l].map(id => stat(id) === null ? id : withN[k++]);
      } else {
        const bc = id => { const v = stat(id); return v === null ? cur.get(id) : v; };
        order[l] = [...order[l]].sort((a, b) => (bc(a) - bc(b)) || (cur.get(a) - cur.get(b)));
      }
    }
  };
  for (let it = 0; it < (opt.iters ?? 6); it++) {
    sweep(opt.upFirst ? it % 2 === 1 : it % 2 === 0);
    const c = total(order, g.edges, g.nl, nodeLayer);
    if (c < bestC || (opt.takeEqual && c <= bestC)) { bestC = c; best = order.map(l => [...l]); }
  }
  return { order: best, crossings: bestC };
}


function greedy(order, g, nodeLayer) {
  let improved = true, c = total(order, g.edges, g.nl, nodeLayer);
  while (improved) {
    improved = false;
    for (let l = 0; l < g.nl; l++) for (let i = 0; i + 1 < order[l].length; i++) {
      [order[l][i], order[l][i + 1]] = [order[l][i + 1], order[l][i]];
      const c2 = total(order, g.edges, g.nl, nodeLayer);
      if (c2 < c) { c = c2; improved = true; } else [order[l][i], order[l][i + 1]] = [order[l][i + 1], order[l][i]];
    }
  }
  return c;
}
function rng(seed) { let s = seed >>> 0; return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296); }
function runRestarts(g, opt) {
  const nodeLayer = new Map(g.nodes.map(n => [n.id, n.layer]));
  const modelPos = new Map(); // model position for tie rule
  g.nodes.forEach(n => modelPos.set(n.id, n.virt ? n.edgeModel + 0.5 : n.model));
  const dist = o => { let d = 0; o.forEach(l => { for (let i = 0; i < l.length; i++) for (let j = i + 1; j < l.length; j++) if (modelPos.get(l[i]) > modelPos.get(l[j])) d++; }); return d; };
  const R = rng(opt.seed ?? 1);
  let best = null;
  for (let k = 0; k < (opt.restarts ?? 1); k++) {
    const r = run(g, { ...opt, shuffle: k === 0 ? null : R });
    if (opt.greedy) r.crossings = greedy(r.order, g, nodeLayer);
    r.d = dist(r.order);
    const better = !best || r.crossings < best.crossings || (r.crossings === best.crossings && opt.tie === "model" && r.d < best.d);
    if (better) best = r;
  }
  return best;
}
function score(g, res) {
  let n = 0, t = 0;
  g.target.forEach((layer, li) => layer.forEach((id, i) => { t++; if (res.order[li].filter(x => !x.startsWith("v"))[i] === id) n++; }));
  return t ? 100 * n / t : 100;
}
const variants = {
  "base": {},
  "dfs": { init: "dfs" },
  "bfs": { init: "bfs" },
  "dfs+slots": { init: "dfs", neigh: "slots" },
  "bfs+slots": { init: "bfs", neigh: "slots" },
  "dfs+median": { init: "dfs", agg: "median" },
  "dfs+takeEq": { init: "dfs", takeEqual: true },
  "bfs+takeEq": { init: "bfs", takeEqual: true },
};
for (const [name, opt] of Object.entries(variants)) {
  let sum = 0, k = 0; const per = [];
  for (const s of slugs) { const g = load(s); const r = runRestarts(g, opt); const sc = score(g, r); sum += sc; k++; per.push(`${s.replace(/^db-erd-/, "").slice(0, 5)}:${Math.round(sc)}`); }
  console.log(name.padEnd(20), (sum / k).toFixed(1), per.join(" "));
}
// ELK's own crossing count vs ours for the target ordering
let elkWorse = 0;
for (const s of slugs) { const g = load(s); const nodeLayer = new Map(g.nodes.map(n => [n.id, n.layer])); }
if (process.argv[2]) {
  const g = load(process.argv[2]); const nodeLayer = new Map(g.nodes.map(n => [n.id, n.layer]));
  const r = run(g, { takeEqual: true });
  const elkOrder = g.target;
  console.log("model order:", g.nodes.filter(n => !n.virt).map(n => n.id).join(" "));
  console.log("ELK crossings (real only):", total(elkOrder, g.edges.filter(([u, v]) => !u.startsWith("v") && !v.startsWith("v")), g.nl, nodeLayer), " mine:", r.crossings);
  elkOrder.forEach((l, i) => console.log("ELK ", i, l.join(", ")));
  r.order.forEach((l, i) => console.log("MINE", i, l.filter(x => !x.startsWith("v")).join(", ")));
  console.log(g.edges.filter(([u, v]) => !u.startsWith("v") && !v.startsWith("v")).map(e => e[0] + ">" + e[1]).join("  "));
}
