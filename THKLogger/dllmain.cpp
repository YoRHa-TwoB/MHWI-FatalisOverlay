/**
 * THKLogger v5 — passes raw Binary Index/ID to C#. No internal nack mapping.
 * C# uses nack_map_full.json (generated from .thk by Leviathon) for conversion.
 */
#include <windows.h>
#include <Psapi.h>
#include <cstdint>
#include <thread>
#include <chrono>
#include <string>
#include "deps/safetyhook.hpp"

#pragma pack(push, 1)
struct THK_Segment {
    uint8_t  NodeEndRandom, InterruptType, SegmentType, unk1;
    uint32_t unk2;
    int32_t  CheckType;
    uint32_t Parameter1, unk3, unk4, unk5, unk6;
    uint32_t Parameter2, NodeEndingData;
    uint32_t ExtRefThkID, ExtRefNodeID;
    int64_t  LocalRefNodeIndex;
    uint32_t unk7, unk8, unk9, unk10, unk11;
    uint32_t ActionID, ActionParams[3], unk12, ActionParam4, ActionParam5;
    uint32_t unkExtra[6];
};
struct THK_NodeInfo { THK_Segment* mNode; uint32_t mSegmentCount, mNodeExternalID; };
struct THK_Header {
    char mType[4]; uint16_t mVersion, mNodeCount;
    uint32_t mThkType, mNull, mMonsterID, mNull2;
    THK_NodeInfo* mNodeInfoList;
};
#pragma pack(pop)

static constexpr uint32_t SHMEM_SIZE = 65536, SHMEM_MAGIC = 0x54484B50;
#pragma pack(push, 1)
struct PathSeg {
    int16_t  rawNi;           // Binary Index from GetIndices (-1=unknown)
    int16_t  si;              // Segment index
    int16_t  chk;             // functionType
    uint8_t  st, it;          // branchingControl, flowControl
    uint16_t act;             // ActionID
    uint16_t extThk;          // external THK ID (55=Global)
    int16_t  extNode;         // target node ID (not index!)
    int32_t  p1, p2;          // parameters
};
struct CycleEntry { uint32_t cycle_id, elapsed_ms; uint16_t seg_count, act_count; uint32_t act_ids[8]; };
#pragma pack(pop)

static HANDLE g_hShmem; static uint8_t* g_pShmem;
static constexpr uint32_t HDR_SZ = 0x28, MAX_SEG = 512, MAX_CYC = 5;
static constexpr uint32_t CYC_SZ = sizeof(CycleEntry) + MAX_SEG * sizeof(PathSeg);

static inline bool IsValidPtr(uintptr_t p) { return p > 0x10000 && p < 0x00007FFFFFFFFFFFULL; }
static uintptr_t ReadPtr(uintptr_t a) { __try { return *(uintptr_t*)a; } __except(EXCEPTION_EXECUTE_HANDLER) { return 0; } }
static int ReadInt(uintptr_t a)    { __try { return *(int*)a; }      __except(EXCEPTION_EXECUTE_HANDLER) { return 0; } }
static uint32_t ReadU32(uintptr_t a){ __try { return *(uint32_t*)a; } __except(EXCEPTION_EXECUTE_HANDLER) { return 0; } }
static uint16_t ReadU16(uintptr_t a){ __try { return *(uint16_t*)a; } __except(EXCEPTION_EXECUTE_HANDLER) { return 0; } }

static SafetyHookInline g_frameHook, g_thkHook;
static uintptr_t g_gameBase; static HMODULE g_hMod; static bool g_initialized;
static THK_Header* g_cachedHeader; static uintptr_t g_cachedThkAddr; static int g_cachedNodeCount;
static uint32_t g_cycleId, g_segCount, g_actCount; static PathSeg g_segs[MAX_SEG];
static uint32_t g_actIds[8]; static bool g_cycleActive;

static void GetIndices(THK_Segment* seg, THK_Header* header, int* outNode, int* outSeg) {
    *outNode = -1; *outSeg = -1;
    if (!IsValidPtr((uintptr_t)header) || !IsValidPtr((uintptr_t)seg)) return;
    uint16_t nc = header->mNodeCount;
    THK_NodeInfo* nl = header->mNodeInfoList;
    if (!IsValidPtr((uintptr_t)nl)) return;
    for (uint16_t i = 0; i < nc; i++) {
        THK_NodeInfo* info = &nl[i];
        if (!IsValidPtr((uintptr_t)info)) continue;
        THK_Segment* ns = info->mNode; uint32_t sc = info->mSegmentCount;
        if (!IsValidPtr((uintptr_t)ns) || sc > 10000) continue;
        for (uint32_t j = 0; j < sc; j++)
            if (&ns[j] == seg) { *outNode = i; *outSeg = j; return; }
    }
}

static void InitShmem() {
    const char* name = "Local\\FatalisOverlay_THK_Path";
    g_hShmem = OpenFileMappingA(FILE_MAP_ALL_ACCESS, FALSE, name);
    bool created = false;
    if (!g_hShmem) { g_hShmem = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, SHMEM_SIZE, name); created = (g_hShmem && GetLastError() != ERROR_ALREADY_EXISTS); }
    if (g_hShmem) {
        g_pShmem = (uint8_t*)MapViewOfFile(g_hShmem, FILE_MAP_ALL_ACCESS, 0, 0, SHMEM_SIZE);
        if (g_pShmem && created) { memset(g_pShmem, 0, SHMEM_SIZE); *(uint32_t*)(g_pShmem+0x00)=SHMEM_MAGIC; *(uint32_t*)(g_pShmem+0x04)=1; *(uint32_t*)(g_pShmem+0x14)=2; }
        if (g_pShmem) *(uint32_t*)(g_pShmem+0x14) |= 1;
    }
}
static void CommitCycle() {
    if (!g_pShmem || g_segCount == 0) return;
    LONG seq = InterlockedIncrement((LONG*)(g_pShmem + 0x08)) - 1;
    uint32_t slot = (uint32_t)(seq % MAX_CYC);
    uint8_t* dst = g_pShmem + HDR_SZ + slot * CYC_SZ;
    CycleEntry hdr = { (uint32_t)seq, 0, (uint16_t)g_segCount, (uint16_t)g_actCount, {} };
    for (uint32_t i = 0; i < g_actCount && i < 8; i++) hdr.act_ids[i] = g_actIds[i];
    memcpy(dst, &hdr, sizeof(CycleEntry));
    memcpy(dst + sizeof(CycleEntry), g_segs, g_segCount * sizeof(PathSeg));
    InterlockedIncrement((LONG*)(g_pShmem + 0x18));
}
static void ResetCycle() { g_cycleActive = true; g_segCount = 0; g_actCount = 0; memset(g_actIds, 0, sizeof(g_actIds)); }
static void SoftReset()  { g_actCount = 0; memset(g_actIds, 0, sizeof(g_actIds)); }
static void AppendSeg(THK_Segment* seg, int rawNi, int si, int extNode) {
    if (!g_cycleActive || g_segCount >= MAX_SEG) return;
    PathSeg* ps = &g_segs[g_segCount];
    ps->rawNi = (int16_t)rawNi; ps->si = (int16_t)si; ps->chk = (int16_t)seg->CheckType;
    ps->st = seg->SegmentType; ps->it = seg->InterruptType; ps->act = (uint16_t)seg->ActionID;
    uint32_t rt = seg->ExtRefThkID;
    ps->extThk = (uint16_t)(rt == 0xFFFFFFFF ? 0 : rt);
    ps->extNode = (int16_t)(seg->ExtRefNodeID == 0xFFFFFFFF ? -1 : (int)seg->ExtRefNodeID);
    ps->p1 = (int32_t)seg->Parameter1; ps->p2 = (int32_t)seg->Parameter2;
    g_segCount++;
    if (seg->ActionID > 0 && (g_actCount == 0 || g_actIds[g_actCount-1] != seg->ActionID))
        g_actIds[g_actCount++] = seg->ActionID;
}

typedef int (*ProcTHKSegFn)(void*, int*, void*);
static ProcTHKSegFn g_origTHK;

int HookProcTHKSeg(void* cThinkMgr, int* out, void* segment) {
    THK_Segment* seg = (THK_Segment*)segment; uintptr_t segAddr = (uintptr_t)seg;

    THK_Header* header = nullptr; int nc = 0;
    if (g_cachedHeader && segAddr >= g_cachedThkAddr && segAddr < g_cachedThkAddr + 0x200000)
        { header = g_cachedHeader; nc = g_cachedNodeCount; }
    int binaryNi = -1, si = -1;
    if (header) GetIndices(seg, header, &binaryNi, &si);
    if (binaryNi == -1) {
        header = nullptr; nc = 0;
        for (uintptr_t p = (segAddr & ~0xF); p > segAddr - 0x200000; p -= 0x10) {
            if (!IsValidPtr(p)) continue;
            if (ReadU32(p) == 0x004B4854) {
                uint16_t v = ReadU16(p+4), n = ReadU16(p+6); uintptr_t nl = ReadPtr(p+0x18);
                if (v > 0 && v <= 100 && n > 0 && n < 10000 && IsValidPtr(nl)) {
                    g_cachedThkAddr = p; g_cachedHeader = (THK_Header*)p; g_cachedNodeCount = nc = n; header = g_cachedHeader; break;
                }
            }
        }
        if (header) GetIndices(seg, header, &binaryNi, &si);
    }

    bool isCombatMain = (nc >= 190);
    bool isGlobal = (nc > 140 && nc <= 160);
    bool isNode0Seg0 = (binaryNi == 0 && si == 0);

    if (isNode0Seg0 && isCombatMain) {
        if (g_cycleActive && g_segCount > 0 && g_actCount > 0) CommitCycle();
        g_cycleId++; ResetCycle();
    }
    if (g_cycleActive && (isCombatMain || (isGlobal && seg->ActionID > 0))) {
        AppendSeg(seg, binaryNi, si, (int)seg->ExtRefNodeID);
        if (isGlobal && seg->ActionID > 0) { CommitCycle(); SoftReset(); }
    }

    if (g_pShmem) InterlockedIncrement((LONG*)(g_pShmem + 0x1C));
    return g_thkHook.call<int>(cThinkMgr, out, segment);
}

static int OnFrame(float* c, float c2) { return g_frameHook.call<int>(c, c2); }

static uintptr_t ScanAoB(const unsigned char* pat, const unsigned char* mask, size_t len) {
    MODULEINFO mi; GetModuleInformation(GetCurrentProcess(), (HMODULE)g_gameBase, &mi, sizeof(mi));
    auto* s = (unsigned char*)g_gameBase, *e = s + mi.SizeOfImage;
    for (auto* p = s; p < e - len; p++) {
        bool ok = true; for (size_t i = 0; i < len; i++) if (mask[i] && p[i] != pat[i]) { ok = false; break; }
        if (ok) return (uintptr_t)p;
    }
    return 0;
}

static void InitMod() {
    if (g_initialized) return;
    g_gameBase = (uintptr_t)GetModuleHandleA("MonsterHunterWorld.exe");
    if (!g_gameBase) return;
    g_frameHook = safetyhook::create_inline((void*)(g_gameBase + 0x0AE7170), (void*)OnFrame);
    InitShmem();

    static const unsigned char kPat[] = { 0x48,0x89,0x00,0x00,0x00,0x56,0x57,0x41,0x54,0x48,0x00,0x00,0x00,0x48,0x8B,0x00,0x00,0x00,0x00,0x00,0x49,0x8B,0xF0,0x48,0x8B,0xDA,0x48,0x8B,0xF9,0x41 };
    static const unsigned char kMask[] = { 0xFF,0xFF,0x00,0x00,0x00,0xFF,0xFF,0xFF,0xFF,0xFF,0x00,0x00,0x00,0xFF,0xFF,0x00,0x00,0x00,0x00,0x00,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF };
    uintptr_t pat = ScanAoB(kPat, kMask, sizeof(kPat));
    if (pat) { g_origTHK = (ProcTHKSegFn)(pat - 5); g_thkHook = safetyhook::create_inline((void*)(pat - 5), (void*)HookProcTHKSeg); }
    else { uintptr_t fn = g_gameBase + 0x133A9D0; if (IsValidPtr(fn)) { g_origTHK = (ProcTHKSegFn)fn; g_thkHook = safetyhook::create_inline((void*)fn, (void*)HookProcTHKSeg); } }
    g_initialized = true;
}

extern "C" __declspec(dllexport) bool Load() { if (!g_initialized) InitMod(); return true; }

BOOL APIENTRY DllMain(HMODULE h, DWORD r, LPVOID) {
    if (r == DLL_PROCESS_ATTACH) {
        g_hMod = h; DisableThreadLibraryCalls(h);
        std::thread t([]() { for (int i = 0; i < 20 && !g_initialized; i++) { std::this_thread::sleep_for(std::chrono::milliseconds(500)); if (!g_gameBase) g_gameBase = (uintptr_t)GetModuleHandleA("MonsterHunterWorld.exe"); if (g_gameBase && !g_initialized) InitMod(); } });
        t.detach();
    }
    if (r == DLL_PROCESS_DETACH) {
        if (g_cycleActive && g_segCount > 0) CommitCycle();
        g_thkHook.reset(); g_frameHook.reset();
        if (g_pShmem) UnmapViewOfFile(g_pShmem);
        if (g_hShmem) CloseHandle(g_hShmem);
    }
    return TRUE;
}
