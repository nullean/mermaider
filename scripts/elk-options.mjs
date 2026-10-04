// Single source of truth for the ELK options that reproduce mermaid.js's ER layout.
export const elkOptions = {
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
export function toElkGraph(inp, extra = {}) {
  return {
    id: "root",
    layoutOptions: { ...elkOptions, ...extra },
    children: inp.nodes.map((n) => ({ id: n.id, width: n.width, height: n.height })),
    edges: inp.edges.map((e) => ({
      id: e.id, sources: [e.source], targets: [e.target],
      labels: e.labelWidth > 0 ? [{ text: "x", width: e.labelWidth, height: e.labelHeight, layoutOptions: { "elk.edgeLabels.inline": "true" } }] : [],
    })),
  };
}
