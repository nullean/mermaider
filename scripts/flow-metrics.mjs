// Shared SVG parsing + geometry metrics for flowchart scoreboards (see flow-quality.mjs, flow-parity.mjs).
function bboxOfGroup(body) {
  const pts = [];
  for (const m of body.matchAll(/<rect x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/g)) { pts.push([+m[1], +m[2]], [+m[1] + +m[3], +m[2] + +m[4]]); }
  for (const m of body.matchAll(/points="([^"]+)"/g)) { const n = m[1].match(/-?[\d.]+/g).map(Number); for (let i = 0; i + 1 < n.length; i += 2) pts.push([n[i], n[i + 1]]); }
  for (const m of body.matchAll(/<(?:ellipse|circle)[^>]*cx="(-?[\d.]+)" cy="(-?[\d.]+)"[^>]*?(?:rx|r)="([\d.]+)"(?:[^>]*ry="([\d.]+)")?/g)) { const rx = +m[3], ry = +(m[4] ?? m[3]); pts.push([+m[1] - rx, +m[2] - ry], [+m[1] + rx, +m[2] + ry]); }
  if (!pts.length) for (const m of body.matchAll(/ d="([^"]+)"/g)) { const n = m[1].match(/-?[\d.]+/g)?.map(Number) ?? []; for (let i = 0; i + 1 < n.length; i += 2) pts.push([n[i], n[i + 1]]); }
  if (!pts.length) return null;
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  return { x0: Math.min(...xs), y0: Math.min(...ys), x1: Math.max(...xs), y1: Math.max(...ys) };
}

export function parse(svg) {
  const groups = [...svg.matchAll(/<g class="(subgraph|node)" data-id="([^"]+)"[^>]*>([\s\S]*?)<\/g>/g)];
  const subgraphs = [], nodes = [];
  for (const g of groups) {
    // subgraph groups can contain nested content; the outer rect is the first rect
    const first = /<rect x="(-?[\d.]+)" y="(-?[\d.]+)" width="([\d.]+)" height="([\d.]+)"/.exec(g[3]);
    if (g[1] === "subgraph") { if (first) subgraphs.push({ id: g[2], x0: +first[1], y0: +first[2], x1: +first[1] + +first[3], y1: +first[2] + +first[4] }); }
    else { const b = bboxOfGroup(g[3]); if (b) nodes.push({ id: g[2], ...b }); }
  }
  const edges = [];
  for (const m of svg.matchAll(/<path class="edge" data-from="([^"]+)" data-to="([^"]+)"[^>]*? d="([^"]+)"/g)) {
    const t = m[3].match(/[MLQC]|-?[\d.]+/g); const pts = []; let i = 0, c = "";
    while (i < t.length) { if (/[MLQC]/.test(t[i])) { c = t[i++]; continue; } if (c === "M" || c === "L") { pts.push([+t[i], +t[i + 1]]); i += 2; } else if (c === "Q") { pts.push([+t[i], +t[i + 1]]); i += 4; } else if (c === "C") { pts.push([+t[i + 4], +t[i + 5]]); i += 6; } else i++; }
    edges.push({ s: m[1], t: m[2], p: pts, curved: /[CQ]/.test(m[3]) && /C/.test(m[3]) });
  }
  const vb = /viewBox="0 0 ([\d.]+) ([\d.]+)"/.exec(svg);
  return { subgraphs, nodes, edges, w: +vb[1], h: +vb[2] };
}

const inter = (a, b) => a.x0 < b.x1 - 1 && a.x1 > b.x0 + 1 && a.y0 < b.y1 - 1 && a.y1 > b.y0 + 1;
const contains = (a, b) => a.x0 <= b.x0 + 1 && a.y0 <= b.y0 + 1 && a.x1 >= b.x1 - 1 && a.y1 >= b.y1 - 1;
const segHitsBox = (p, q, b, m = 2) => { const x0 = Math.min(p[0], q[0]), x1 = Math.max(p[0], q[0]), y0 = Math.min(p[1], q[1]), y1 = Math.max(p[1], q[1]); return x1 > b.x0 + m && x0 < b.x1 - m && y1 > b.y0 + m && y0 < b.y1 - m; };
const segCross = (p, q, r, s) => { const d1x = q[0] - p[0], d1y = q[1] - p[1], d2x = s[0] - r[0], d2y = s[1] - r[1], den = d1x * d2y - d1y * d2x; if (Math.abs(den) < 1e-9) return false; const t = ((r[0] - p[0]) * d2y - (r[1] - p[1]) * d2x) / den, u = ((r[0] - p[0]) * d1y - (r[1] - p[1]) * d1x) / den; return t > 1e-3 && t < 1 - 1e-3 && u > 1e-3 && u < 1 - 1e-3; };
const pathHits = (pts, b) => { for (let i = 0; i < pts.length - 1; i++) if (segHitsBox(pts[i], pts[i + 1], b)) return true; return false; };
const nodeInSg = (n, sg) => contains(sg, n);

export function metrics(d) {
  let sgOverlap = 0, straddle = 0, throughNode = 0, throughSg = 0, crossings = 0, bends = 0, curved = 0;
  for (let i = 0; i < d.subgraphs.length; i++) for (let j = i + 1; j < d.subgraphs.length; j++) { const a = d.subgraphs[i], b = d.subgraphs[j]; if (inter(a, b) && !contains(a, b) && !contains(b, a)) sgOverlap++; }
  for (const n of d.nodes) for (const sg of d.subgraphs) if (inter(n, sg) && !contains(sg, n)) straddle++;
  const nodeById = new Map(d.nodes.map(n => [n.id, n]));
  for (const e of d.edges) {
    bends += Math.max(0, e.p.length - 2); if (e.curved) curved++;
    for (const n of d.nodes) if (n.id !== e.s && n.id !== e.t && pathHits(e.p, n)) throughNode++;
    const s = nodeById.get(e.s), t = nodeById.get(e.t);
    for (const sg of d.subgraphs) {
      const sin = s && nodeInSg(s, sg), tin = t && nodeInSg(t, sg);
      if (!sin && !tin && pathHits(e.p, sg)) throughSg++;
    }
  }
  for (let i = 0; i < d.edges.length; i++) for (let j = i + 1; j < d.edges.length; j++) {
    const a = d.edges[i], b = d.edges[j]; if (a.s === b.s || a.t === b.t || a.s === b.t || a.t === b.s) continue;
    let hit = false; for (let x = 0; x < a.p.length - 1 && !hit; x++) for (let y = 0; y < b.p.length - 1 && !hit; y++) if (segCross(a.p[x], a.p[x + 1], b.p[y], b.p[y + 1])) hit = true;
    if (hit) crossings++;
  }
  // collinear overlaps: different edges (no shared end) running on top of each other for more than 8px
  let overlaps = 0;
  for (let i = 0; i < d.edges.length; i++) for (let j = i + 1; j < d.edges.length; j++) {
    const a = d.edges[i], b = d.edges[j]; if (a.s === b.s || a.t === b.t || a.s === b.t || a.t === b.s) continue;
    let hit = false;
    for (let x = 0; x < a.p.length - 1 && !hit; x++) for (let y = 0; y < b.p.length - 1 && !hit; y++) {
      const [p, q, r, s] = [a.p[x], a.p[x + 1], b.p[y], b.p[y + 1]];
      if (Math.abs(p[1] - q[1]) < 0.6 && Math.abs(r[1] - s[1]) < 0.6 && Math.abs(p[1] - r[1]) < 1.5) { if (Math.min(Math.max(p[0], q[0]), Math.max(r[0], s[0])) - Math.max(Math.min(p[0], q[0]), Math.min(r[0], s[0])) > 8) hit = true; }
      else if (Math.abs(p[0] - q[0]) < 0.6 && Math.abs(r[0] - s[0]) < 0.6 && Math.abs(p[0] - r[0]) < 1.5) { if (Math.min(Math.max(p[1], q[1]), Math.max(r[1], s[1])) - Math.max(Math.min(p[1], q[1]), Math.min(r[1], s[1])) > 8) hit = true; }
    }
    if (hit) overlaps++;
  }
  return { overlaps, sgOverlap, straddle, throughNode, throughSg, crossings, bendsPerEdge: bends / Math.max(1, d.edges.length), curved, w: d.w, h: d.h, aspect: Math.max(d.w, d.h) / Math.min(d.w, d.h) };
}

