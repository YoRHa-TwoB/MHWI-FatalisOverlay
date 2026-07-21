"""Generate Binary-Index → .nack-number mapping from .thk files."""
import json, sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "Leviathon-main"))
from common.thk import Thk, Segment

def is_empty_segment(seg):
    """Match Leviathon checkEmptyFields exactly."""
    # ref fields must be -1
    if seg.extRefThkID != -1:    return False
    if seg.extRefNodeID != -1:   return False
    if seg.localRefNodeID != -1: return False
    # endRandom in [0,1]
    if seg.endRandom not in (0, 1): return False
    # nodeEndingData must be < 20000
    if seg.nodeEndingData // 10000 not in (0, 1): return False
    # All other binary fields must be 0
    for f in Segment.subcons:
        name = f.name
        if name in ("padding","monsterID","isPalico","log",
                     "extRefThkID","extRefNodeID","localRefNodeID",
                     "endRandom","nodeEndingData"):
            continue
        val = getattr(seg, name)
        if val != 0:
            return False
    return True

def is_empty_node(node):
    if node.id != 0: return False
    if node.count != 1: return False
    seg = node.segments[0]
    return is_empty_segment(seg)

def build_map(thk_path):
    thk = Thk.parse_file(thk_path)
    nc = thk.header.structCount
    idx_map = {}   # binary index → nack number
    id_map = {}    # node ID → nack number
    nack_idx = 0
    for bin_idx in range(nc):
        node = thk.nodeList[bin_idx]
        if is_empty_node(node):
            continue
        idx_map[str(bin_idx)] = nack_idx
        id_map[str(node.id)] = nack_idx
        nack_idx += 1
    return idx_map, id_map, nc, nack_idx

def main():
    base = os.path.dirname(__file__)
    thk_dir = os.path.join(base, "黑龙thk文件")

    print("=== Combat_Main (em013_00.thk) ===")
    cm_idx, cm_id, cm_nc, cm_valid = build_map(os.path.join(thk_dir, "em013_00.thk"))
    print(f"  binary nodes: {cm_nc}, valid: {cm_valid}, empty: {cm_nc - cm_valid}")

    print("=== Global (em013_55.thk) ===")
    gl_idx, gl_id, gl_nc, gl_valid = build_map(os.path.join(thk_dir, "em013_55.thk"))
    print(f"  binary nodes: {gl_nc}, valid: {gl_valid}, empty: {gl_nc - gl_valid}")

    out = {
        "cm_idx": cm_idx,    # CM binary index → nack number
        "cm_id": cm_id,      # CM node ID → nack number
        "global_idx": gl_idx,  # Global binary index → nack number
        "global_id": gl_id,    # Global node ID → nack number
    }
    out_path = os.path.join(base, "FatalisOverlay-Source", "nack_map_full.json")
    with open(out_path, "w") as f:
        json.dump(out, f)
    print(f"\nSaved to {out_path}")

if __name__ == "__main__":
    main()
