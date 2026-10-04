// Print the crossing pairs (with polylines) of one rendered ER SVG. Usage: node scripts/crossing-detail.mjs <slug>
import fs from "node:fs";
const snap = "tests/Mermaider.Tests/Snapshots/";
let f = `${snap}GallerySnapshotTests.Gallery_example=${process.argv[2]}.received.svg`;
if (!fs.existsSync(f)) f = f.replace(".received.", ".verified.");
const svg = fs.readFileSync(f, "utf8"); const edges = [];
for (const m of svg.matchAll(/<path class="er-relationship" data-entity1="([^"]+)" data-entity2="([^"]+)"[^>]*? d="([^"]+)"/g)) {
  const pts = []; const t = m[3].match(/[MLQC]|-?[\d.]+/g); let i = 0, c = "";
  while (i < t.length) { if (/[MLQC]/.test(t[i])) { c = t[i++]; continue; } if (c === "M" || c === "L") { pts.push([+t[i], +t[i + 1]]); i += 2; } else if (c === "Q") { pts.push([+t[i], +t[i + 1]]); i += 4; } else i++; }
  edges.push({ s: m[1], t: m[2], p: pts.filter((p, k) => k === 0 || k === pts.length - 1 || !(Math.abs(pts[k - 1][0] - p[0]) < .01 && Math.abs(p[0] - pts[k + 1][0]) < .01) && !(Math.abs(pts[k - 1][1] - p[1]) < .01 && Math.abs(p[1] - pts[k + 1][1]) < .01)) });
}
const seg = (p, q, r, s) => { const d1x = q[0] - p[0], d1y = q[1] - p[1], d2x = s[0] - r[0], d2y = s[1] - r[1], den = d1x * d2y - d1y * d2x; if (Math.abs(den) < 1e-9) return null; const t = ((r[0] - p[0]) * d2y - (r[1] - p[1]) * d2x) / den, u = ((r[0] - p[0]) * d1y - (r[1] - p[1]) * d1x) / den; return (t > 1e-3 && t < 1 - 1e-3 && u > 1e-3 && u < 1 - 1e-3) ? [p[0] + t * d1x, p[1] + t * d1y] : null; };
const R = p => JSON.stringify(p.map(q => q.map(Math.round)));
for (let i = 0; i < edges.length; i++) for (let j = i + 1; j < edges.length; j++) {
  const a = edges[i], b = edges[j]; if (a.s === b.s || a.t === b.t || a.s === b.t || a.t === b.s) continue;
  for (let x = 0; x < a.p.length - 1; x++) for (let y = 0; y < b.p.length - 1; y++) { const h = seg(a.p[x], a.p[x + 1], b.p[y], b.p[y + 1]); if (h) console.log("CROSS", a.s + ">" + a.t, R(a.p), "\n   x", b.s + ">" + b.t, R(b.p), "at", h.map(Math.round)); }
}
