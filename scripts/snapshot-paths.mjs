// Where the test suite's snapshots live (Nullean.Snapshots). Verified files are committed under
// tests/Mermaider.Tests/.snapshots/<test folder>/<Class>/; received files go to .artifacts/.snapshots/received/.
// A data-driven case is named {Method}.{slug}.{argument hash}, so these look a case up by its slug.
// Paths are relative to the repository root, which is where the scripts run from.
import fs from "node:fs";

const verifiedRoot = "tests/Mermaider.Tests/.snapshots/Snapshots/";
const receivedRoot = ".artifacts/.snapshots/received/Mermaider.Tests/Snapshots/";

// slug -> path for every case of one data-driven snapshot test; a received file wins over its verified one
// unless verifiedOnly is set.
function cases(classFolder, method, ext, { verifiedOnly = false } = {}) {
  const found = new Map();
  const kinds = verifiedOnly ? ["verified"] : ["verified", "received"];
  for (const kind of kinds) {
    const dir = (kind === "verified" ? verifiedRoot : receivedRoot) + classFolder + "/";
    if (!fs.existsSync(dir)) continue;
    const pattern = new RegExp(`^${method}\\.(.+)\\.[0-9a-f]{8}\\.${kind}\\.${ext}$`);
    for (const f of fs.readdirSync(dir)) {
      const m = pattern.exec(f);
      if (m) found.set(m[1], dir + f);
    }
  }
  return found;
}

/** slug -> SVG path for every gallery example, preferring a received file from the last test run. */
export const galleryCases = () => cases("GallerySnapshotTests", "Gallery", "svg");

/** The gallery SVG for one slug (received if present, else verified), or null. */
export const gallerySvg = slug => galleryCases().get(slug) ?? null;

/** The committed ER layout-skeleton dump for one slug, or null. */
export const erSkeletonTxt = slug => cases("LayoutSkeleton/LayoutSkeletonTests", "Skeleton_snapshot", "txt", { verifiedOnly: true }).get(slug) ?? null;
