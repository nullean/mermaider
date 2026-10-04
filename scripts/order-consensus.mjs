// Can an emulation of ELK's randomised restarts (mode of the outcome distribution) predict ELK's order better
// than a single deterministic sweep? Scored against ELK output for 13 seeds per RNG-driven diagram.
import fs from "node:fs";
const dir = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton/elk";
const draws = JSON.parse(fs.readFileSync(`${dir}/seed-draws.json`));

function load(slug) {
  const inp = JSON.parse(fs.readFileSync(`${dir}/${slug}.elk.input.json`));
  const ref = new Map(draws[slug].ref);
  const nl = Math.max(...[...ref.values()].map(v => v[0])) + 1;
  const nodes = inp.nodes.map((n, i) => ({ id: n.id, layer: ref.get(n.id)[0], model: i, virt: false }));
  const edges = [];
  inp.edges.forEach((e, ei) => {
    let a = e.source, b = e.target; const la = ref.get(a)[0], lb = ref.get(b)[0];
    if (la === lb) return; if (la > lb) [a, b] = [b, a];
    let prev = a;
    for (let l = Math.min(la, lb) + 1; l < Math.max(la, lb); l++) { const v = { id: `v${ei}_${l}`, layer: l, model: ei + 0.5, virt: true }; nodes.push(v); edges.push([prev, v.id]); prev = v.id; }
    edges.push([prev, b]);
  });
  return { nodes, edges, nl, inp };
}
function rng(seed) { let s = seed >>> 0; return () => ((s = (Math.imul(s, 1664525) + 1013904223) >>> 0) / 4294967296); }
function prep(g) {
  const layer = new Map(g.nodes.map(n => [n.id, n.layer])), up = new Map(), down = new Map();
  g.nodes.forEach(n => { up.set(n.id, []); down.set(n.id, []); });
  g.edges.forEach(([u, v]) => { down.get(u).push(v); up.get(v).push(u); });
  const eByLayer = Array.from({ length: g.nl - 1 }, () => []);
  g.edges.forEach(([u, v]) => eByLayer[layer.get(u)].push([u, v]));
  return { layer, up, down, eByLayer };
}
function crossings(order, P) {
  const pos = new Map(); order.forEach(l => l.forEach((id, i) => pos.set(id, i)));
  let c = 0;
  for (const es of P.eByLayer) for (let i = 0; i < es.length; i++) for (let j = i + 1; j < es.length; j++) {
    const a = pos.get(es[i][0]) - pos.get(es[j][0]), b = pos.get(es[i][1]) - pos.get(es[j][1]); if (a * b < 0) c++; }
  return c;
}
function sweeps(order, P, nl, firstDown, iters = 12, v = {}) {
  let best = order.map(l => [...l]), bestC = crossings(order, P);
  for (let it = 0; it < iters; it++) {
    const down_ = (it % 2 === 0) === firstDown;
    const ls = [...Array(nl).keys()]; if (!down_) ls.reverse();
    for (const l of ls.slice(1)) {
      const fixed = order[down_ ? l - 1 : l + 1]; const pos = new Map(fixed.map((id, i) => [id, i])); const cur = new Map(order[l].map((id, i) => [id, i]));
      const stat = id => { const ns = (down_ ? P.up : P.down).get(id).map(x => pos.get(x)).filter(x => x !== undefined); if (!ns.length) return null; if (v.median) { ns.sort((a, b) => a - b); const m = ns.length >> 1; return ns.length % 2 ? ns[m] : (ns[m - 1] + ns[m]) / 2; } return ns.reduce((a, b) => a + b, 0) / ns.length; };
      const bc = id => { const x = stat(id); return x === null ? cur.get(id) : x; };
      order[l] = [...order[l]].sort((a, b) => (bc(a) - bc(b)) || (cur.get(a) - cur.get(b)));
    }
    const c = crossings(order, P); if (c < bestC || (v.takeEqual && c <= bestC)) { bestC = c; best = order.map(l => [...l]); }
  }
  return { order: best, c: bestC };
}
function greedy(order, P) {
  let c = crossings(order, P), imp = true;
  while (imp) { imp = false; for (let l = 0; l < order.length; l++) for (let i = 0; i + 1 < order[l].length; i++) {
    [order[l][i], order[l][i + 1]] = [order[l][i + 1], order[l][i]]; const c2 = crossings(order, P);
    if (c2 < c) { c = c2; imp = true; } else [order[l][i], order[l][i + 1]] = [order[l][i + 1], order[l][i]]; } }
  return c;
}
const sig = (order) => order.map(l => l.filter(x => !x.startsWith("v")).join(",")).join("|");
function modelOrder(g) { const o = Array.from({ length: g.nl }, () => []); g.nodes.forEach(n => o[n.layer].push(n)); return o.map(l => l.sort((a, b) => a.model - b.model).map(n => n.id)); }
function shuffled(l, R) { const a = [...l]; for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(R() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; }

function trial(g, P, R, mode) {
  let order = modelOrder(g);
  if (mode.rand === "all") order = order.map(l => shuffled(l, R));
  else if (mode.rand === "first") order[0] = shuffled(order[0], R);
  const r = sweeps(order, P, g.nl, mode.rand === "all" ? R() < 0.5 : true);
  if (mode.greedy) r.c = greedy(r.order, P);
  return r;
}
function consensus(g, mode) {
  const P = prep(g), R = rng(12345); const counts = new Map();
  for (let run = 0; run < (mode.runs ?? 60); run++) {
    let best = null;
    for (let t = 0; t < (mode.thor ?? 7); t++) { const r = trial(g, P, R, mode); if (!best || r.c < best.c) best = r; }
    const s = sig(best.order); const e = counts.get(s) ?? { n: 0, c: best.c, order: best.order }; e.n++; counts.set(s, e);
  }
  return [...counts.values()].sort((a, b) => b.n - a.n || a.c - b.c)[0].order;
}
function deterministic(g, v = {}) { const P = prep(g); let o = modelOrder(g);
  if (v.reverseInit) o = o.map(l => [...l].reverse());
  const r = sweeps(o, P, g.nl, v.upFirst ? false : true, v.iters ?? 12, v); if (v.greedy) greedy(r.order, P); return r.order; }
function engineOrder(slug) { const f = `tests/Mermaider.Tests/Snapshots/LayoutSkeleton/LayoutSkeletonTests.Skeleton_snapshot_slug=${slug}.verified.txt`; if (!fs.existsSync(f)) return null;
  const o = []; for (const l of fs.readFileSync(f, "utf8").replace(/^\uFEFF/, "").split("\n")) { const r = /^Layer (\d+): (.*)$/.exec(l); if (r) o[+r[1]] = r[2].split(", "); } return o; }

const agree = (order, draw) => { const d = new Map(draw); let n = 0, t = 0; order.forEach((l) => l.filter(x => !x.startsWith("v")).forEach((id, i) => { t++; const w = d.get(id); if (w && w[1] === i) n++; })); return 100 * n / t; };
const strategies = {
  "ENGINE (current)": null,
  "det base": g => deterministic(g),
  "det upFirst": g => deterministic(g, { upFirst: true }),
  "det median": g => deterministic(g, { median: true }),
  "det takeEqual": g => deterministic(g, { takeEqual: true }),
  "det greedy": g => deterministic(g, { greedy: true }),
  "det reverseInit": g => deterministic(g, { reverseInit: true }),
  "det upFirst+takeEq": g => deterministic(g, { upFirst: true, takeEqual: true }),
  "det iters2": g => deterministic(g, { iters: 2 }),
  "det iters4": g => deterministic(g, { iters: 4 }),
};
const rngOnly = process.argv[2] === "rng";
const slugs = Object.keys(draws).filter(s => !rngOnly || new Set(Object.values(draws[s].draws).map(x => JSON.stringify(x))).size > 1);
const luck = [];
for (const [name, fn] of Object.entries(strategies)) {
  let sumRef = 0, sumAll = 0; const per = [];
  for (const s of slugs) {
    const g = load(s); const o = fn ? fn(g) : engineOrder(s);
    if (!o) continue;
    const a = Object.values(draws[s].draws).map(d => agree(o, d));
    const mean = a.reduce((x, y) => x + y, 0) / a.length; sumAll += mean; sumRef += agree(o, draws[s].ref); per.push(`${s.slice(7, 12)}:${Math.round(mean)}`);
  }
  console.log(name.padEnd(30), "vs 13 seeds:", (sumAll / slugs.length).toFixed(1), " vs reference:", (sumRef / slugs.length).toFixed(1), "|", per.join(" "));
}
