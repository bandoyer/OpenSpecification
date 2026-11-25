export const title = "Specification Builder Chain State (per-builder IsChainDiscarded)";

const handles = (v) => [
  [v.incState, v.incFlag],
  [v.incBranchState, v.incBranchFlag],
  [v.ordState, v.ordFlag],
  [v.ordBranchState, v.ordBranchFlag],
];

export const state = (v) => {
  if (v.orphanEmitted && v.activeDropped) return "Both rules broken";
  if (v.orphanEmitted) return "Orphan continuation emitted";
  if (v.activeDropped) return "Active continuation dropped";

  const list = handles(v);
  const exposed = list.some(([s, f]) => s === "discarded" && !f);
  const clobbered = list.some(([s, f]) => s === "active" && f);
  if (exposed && clobbered) return "Handles out of sync (exposed & clobbered)";
  if (exposed) return "Discarded handle exposed (flag clear)";
  if (clobbered) return "Active handle clobbered (flag discarded)";

  const hasActive = list.some(([s]) => s === "active");
  const hasDiscarded = list.some(([s]) => s === "discarded");
  if (!hasActive && !hasDiscarded) return "Start";
  if (hasActive && hasDiscarded) return "Active and discarded handles";
  if (hasActive) return "Active handle(s)";
  return "Discarded handle(s)";
};

export const step = (action, from, to) => {
  if (from.orphanEmitted && from.activeDropped) return null;
  if ((from.orphanEmitted || from.activeDropped) && !(to.orphanEmitted && to.activeDropped)) return null;
  switch (action) {
    case "IncludeTrue":
    case "OrderByTrue":
    case "LinearIncludeTrue":
    case "LinearOrderByTrue":
      return "start chain (true)";
    case "IncludeFalse":
    case "OrderByFalse":
    case "LinearIncludeFalse":
    case "LinearOrderByFalse":
      return "start chain (false)";
    case "IncludeTrueFromOrder":
    case "OrderByTrueFromInclude":
      return "handoff to new chain (true)";
    case "IncludeFalseFromOrder":
    case "OrderByFalseFromInclude":
      return "handoff to new chain (false)";
    case "ThenIncludeTrue":
    case "ThenByTrue":
    case "ContinueIncBranchTrue":
    case "ContinueOrdBranchTrue":
      return to.orphanEmitted && !from.orphanEmitted
        ? "continue chain (true) [orphan!]"
        : to.activeDropped && !from.activeDropped
          ? "continue chain (true) [dropped!]"
          : "continue chain (true)";
    case "BranchThenIncludeTrue":
    case "BranchThenByTrue":
      return to.orphanEmitted && !from.orphanEmitted
        ? "branch handle (true) [orphan!]"
        : to.activeDropped && !from.activeDropped
          ? "branch handle (true) [dropped!]"
          : "branch handle (true)";
    case "ThenIncludeFalse":
    case "ThenByFalse":
    case "ContinueIncBranchFalse":
    case "ContinueOrdBranchFalse":
      return "continue chain (false)";
    case "BranchThenIncludeFalse":
    case "BranchThenByFalse":
      return "branch handle (false)";
    case "EnableCacheTrue":
      return "EnableCache(true)";
    case "EnableCacheFalse":
      return "EnableCache(false)";
    case "SwitchThread":
      return "await switches thread";
    default:
      return action;
  }
};

export const group = (v) => {
  if (v.orphanEmitted || v.activeDropped) return "broken";
  const list = handles(v);
  if (list.some(([s, f]) => (s === "discarded" && !f) || (s === "active" && f))) return "broken";
  if (list.every(([s]) => s === "none")) return "start";
  return "building";
};

export const colors = {
  start: "blue",
  building: "teal",
  broken: "pink",
};
