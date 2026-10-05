import fs from "node:fs";
const base = "tests/Mermaider.Tests/Snapshots/LayoutSkeleton";
const slugs = fs.readdirSync(base + "/elk").filter(f => f.endsWith(".elk.output.json")).map(f => f.replace(".elk.output.json", ""));
function elkNodes(slug) {
  const o = JSON.parse(fs.readFileSync(`${base}/elk/${slug}.elk.output.json`));
  const ys = [...new Set(o.nodes.map(n => n.y))].sort((a, b) => a - b);
  const m = new Map();
  ys.forEach((y, li) => o.nodes.filter(n => n.y === y).sort((a, b) => a.x - b.x).forEach((n, i) => m.set(n.id, [li, i])));
  return m;
}
function mjsNodes(slug) {
  const f = `${base}/Reference/${slug}.mjs.ir.txt`; if (!fs.existsSync(f)) return null;
  const m = new Map();
  for (const l of fs.readFileSync(f, "utf8").split("\n")) { const p = l.split("\t"); if (p[0] === "N") m.set(p[1], [+p[2], +p[3]]); }
  return m;
}
function oursNodes(slug) {
  const f = `${base}/LayoutSkeletonTests.Skeleton_snapshot_slug=${slug}.verified.txt`; if (!fs.existsSync(f)) return null;
  const m = new Map();
  for (const l of fs.readFileSync(f, "utf8").replace(/^﻿/, "").split("\n")) {
    const r = /^Layer (\d+): (.*)$/.exec(l); if (r) r[2].split(", ").forEach((id, i) => m.set(id, [+r[1], i]));
  }
  return m;
}
const agree = (a, b) => { if (!a || !b) return NaN; let n = 0, t = 0; for (const [k, v] of a) { if (!b.has(k)) continue; t++; const w = b.get(k); if (v[0] === w[0] && v[1] === w[1]) n++; } return t ? Math.round(100 * n / t) : NaN; };
const layerAgree = (a, b) => { if (!a || !b) return NaN; let n = 0, t = 0; for (const [k, v] of a) { if (!b.has(k)) continue; t++; if (v[0] === b.get(k)[0]) n++; } return t ? Math.round(100 * n / t) : NaN; };
console.log("slug | ELK(ourSize) vs mjs: layer/exact | ours vs ELK: layer/exact | ours vs mjs exact");
for (const s of slugs) {
  const e = elkNodes(s), m = mjsNodes(s), o = oursNodes(s);
  console.log(s.padEnd(34), `${layerAgree(e, m)}/${agree(e, m)}`.padEnd(10), `${layerAgree(o, e)}/${agree(o, e)}`.padEnd(10), agree(o, m));
}
