export const title = "Specification Builder Chain State (IsChainDiscarded)";

export const state = (v) => {
  if (v.orphanEmitted && v.activeDropped) return "Both rules broken";
  if (v.orphanEmitted) return "Orphan continuation emitted";
  if (v.activeDropped) return "Active continuation dropped";
  if (v.incState === "none" && v.ordState === "none") {
    return v.flag ? "No chain (flag discarded)" : "Start (flag clear)";
  }
  const hasActive = v.incState === "active" || v.ordState === "active";
  const hasDiscarded = v.incState === "discarded" || v.ordState === "discarded";
  if (hasDiscarded && !v.flag) return "Discarded chain exposed (flag clear)";
  if (hasActive && v.flag) return "Active chain clobbered (flag discarded)";
  if (hasActive) return "Chain active (flag clear)";
  return "Chain discarded (flag discarded)";
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
    case "ThenIncludeTrue":
    case "ThenByTrue":
      return to.orphanEmitted && !from.orphanEmitted
        ? "continue chain (true) [orphan!]"
        : to.activeDropped && !from.activeDropped
          ? "continue chain (true) [dropped!]"
          : "continue chain (true)";
    case "ThenIncludeFalse":
    case "ThenByFalse":
      return "continue chain (false)";
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
  if (v.incState === "none" && v.ordState === "none") return "start";
  return "building";
};

export const colors = {
  start: "blue",
  building: "teal",
  broken: "pink",
};
